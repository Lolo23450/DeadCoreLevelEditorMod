using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2Cpp;
using Il2CppDeadCore;
using SceneManager = UnityEngine.SceneManagement.SceneManager;

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 1: FILTERING & CLASSIFICATION PIPELINE
    // =========================================================================

    public static class ModelHarvestFilter
    {
        // "dec_" allowed so decoration/decor assets pass through freely
        private static readonly HashSet<string> IgnoredMeshPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ucx_", "ubx_", "usp_", "bb_", "col_", "proxy_", "nav_"
        };

        private static readonly string[] IgnoredKeywords = new string[]
        {
            "combine", "batch", "impostor", "imposter", "billboard", "_card",
            "shadow", "gizmo", "proxy",
            "wireframe", "highlight", "beacon", "skybox", "horizon", "fog",
            "cloud", "dust", "font", "text",
            "ui-", "tmp", "cursor", "sprite", "lightdata",
            "particle", "emitter"
        };

        // Matches LOD1, 2, 3, 5, 6, 7, 8, 9 (LOD4 is explicitly allowed/kept)
        private static readonly Regex FilteredLodRegex = new Regex(@"(?:^|[\W_])lod[12356789](?:$|[\W_])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool ShouldIgnoreMesh(Mesh mesh, string meshName, string goName, GameObject sourceGo = null)
        {
            if (mesh == null || mesh.vertexCount < 6) return true;

            string mLow = meshName.ToLowerInvariant();
            string gLow = goName.ToLowerInvariant();

            // 1. LOD check: Keep LOD0 and LOD4. Exclude LOD1, LOD2, LOD3, LOD5+.
            bool isLod4 = mLow.Contains("lod4") || gLow.Contains("lod4");
            if (!isLod4)
            {
                if (mLow.Contains("lod1") || mLow.Contains("lod2") || mLow.Contains("lod3") || mLow.Contains("lod5") ||
                    gLow.Contains("lod1") || gLow.Contains("lod2") || gLow.Contains("lod3") || gLow.Contains("lod5") ||
                    FilteredLodRegex.IsMatch(mLow) || FilteredLodRegex.IsMatch(gLow))
                {
                    return true;
                }
            }

            // 2. Hierarchy and LODGroup check: reject if renderer belongs to an excluded LOD level (keeps LOD4)
            if (sourceGo != null && HasLowerLodInHierarchy(sourceGo))
            {
                return true;
            }

            // 3. Filter out internal gameplay bugs/mosquitoes/gauges
            if (mLow.Contains("mosquito") || gLow.Contains("mosquito") ||
                mLow.Contains("robot") || gLow.Contains("robot") ||
                mLow.Contains("gauge") || gLow.Contains("gauge"))
            {
                return true;
            }

            // 4. Prefix checks
            foreach (var prefix in IgnoredMeshPrefixes)
            {
                if (mLow.StartsWith(prefix) || gLow.StartsWith(prefix)) return true;
            }

            // 5. Substring keyword checks
            for (int i = 0; i < IgnoredKeywords.Length; i++)
            {
                string kw = IgnoredKeywords[i];
                if (mLow.Contains(kw) || gLow.Contains(kw)) return true;
            }

            // 6. Flat quad / billboard card geometry checks
            Vector3 size = mesh.bounds.size;
            float minDim = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

            if (minDim < 0.015f && maxDim >= 3.0f && mesh.vertexCount <= 8)
                return true;

            return false;
        }

        public static bool HasLowerLodInHierarchy(GameObject go)
        {
            if (go == null) return false;

            // Engine-level LODGroup inspection (skips LOD4 so it is kept)
            try
            {
                LODGroup lodGroup = go.GetComponentInParent<LODGroup>();
                if (lodGroup != null)
                {
                    var lods = lodGroup.GetLODs();
                    if (lods != null && lods.Length > 1)
                    {
                        Renderer r = go.GetComponent<Renderer>();
                        if (r != null)
                        {
                            for (int l = 1; l < lods.Length; l++)
                            {
                                if (l == 4) continue; // Keep LOD4!

                                var rends = lods[l].renderers;
                                if (rends != null)
                                {
                                    for (int ri = 0; ri < rends.Length; ri++)
                                    {
                                        if (rends[ri] == r) return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // Hierarchy name inspection up to 4 parent levels
            Transform curr = go.transform;
            int depth = 0;
            while (curr != null && depth < 4)
            {
                string nLow = curr.name.ToLowerInvariant();
                bool isLod4 = nLow.Contains("lod4");
                if (!isLod4)
                {
                    if (nLow.Contains("lod1") || nLow.Contains("lod2") || nLow.Contains("lod3") || nLow.Contains("lod5") || FilteredLodRegex.IsMatch(nLow))
                    {
                        return true;
                    }
                }
                curr = curr.parent;
                depth++;
            }

            return false;
        }

        // Min: 1.2m, Max: 24.0m
        public static bool IsValidHarvestSize(Bounds bounds, Transform contextTransform = null, float minSize = 1.2f, float maxSize = 24.0f)
        {
            Vector3 s = bounds.size;
            float unscaledMax = Mathf.Max(s.x, Mathf.Max(s.y, s.z));

            if (contextTransform != null)
            {
                Vector3 scaled = Vector3.Scale(s, contextTransform.lossyScale);
                float scaledMax = Mathf.Max(Mathf.Abs(scaled.x), Mathf.Max(Mathf.Abs(scaled.y), Mathf.Abs(scaled.z)));

                bool validScaled = (scaledMax >= minSize && scaledMax <= maxSize);
                bool validUnscaled = (unscaledMax >= minSize && unscaledMax <= maxSize);
                return validScaled || validUnscaled;
            }

            return unscaledMax >= minSize && unscaledMax <= maxSize;
        }
    }

    public static class GeometryClassifier
    {
        public static (string subCategory, string friendlyName, bool isPlatform) Classify(Bounds bounds, string goName, string meshName)
        {
            Vector3 s = bounds.size;
            float maxDim = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            string gLow = goName.ToLowerInvariant();
            string mLow = meshName.ToLowerInvariant();

            bool isPlatform = gLow.Contains("16x2x16") || mLow.Contains("16x2x16") ||
                              gLow.Contains("platform") || mLow.Contains("platform") ||
                              gLow.Contains("plateforme") || mLow.Contains("plateforme") ||
                              gLow.Contains("floor") || mLow.Contains("sol") || gLow.Contains("step") ||
                              (s.y <= 2.5f && (s.x >= 6f || s.z >= 6f));

            bool isWall = !isPlatform && (s.y > s.z * 1.8f || s.y > s.x * 1.8f) && (s.x > 2f || s.z > 2f);
            bool isColumn = !isPlatform && !isWall && s.y > (Mathf.Max(s.x, s.z) * 2.0f);
            bool isDecor = !isPlatform && !isWall && !isColumn &&
                           (maxDim < 4.5f || gLow.Contains("decor") || mLow.Contains("decor") || gLow.Contains("prop") || mLow.Contains("prop") || gLow.Contains("detail"));

            string subCategory = "Architecture";
            if (isPlatform) subCategory = "Platforms";
            else if (isWall) subCategory = "Walls";
            else if (isColumn) subCategory = "Columns";
            else if (isDecor) subCategory = "Decor";
            else if (maxDim > 14.0f) subCategory = "Structures";

            string baseName;
            if (gLow.Contains("16x2x16") || mLow.Contains("16x2x16"))
            {
                baseName = "Floor Platform 16x16";
            }
            else
            {
                baseName = !string.IsNullOrWhiteSpace(meshName) && !mLow.StartsWith("mesh") && !mLow.StartsWith("polysurface")
                    ? meshName.Replace("Mesh", "").Replace("_", " ").Trim()
                    : goName.Replace("_", " ").Trim();
            }

            // Strip prefix tags
            if (baseName.StartsWith("SM ", StringComparison.OrdinalIgnoreCase)) baseName = baseName.Substring(3);
            if (baseName.StartsWith("m ", StringComparison.OrdinalIgnoreCase)) baseName = baseName.Substring(2);
            if (baseName.StartsWith("geo ", StringComparison.OrdinalIgnoreCase)) baseName = baseName.Substring(4);

            // Strip LOD markers (except LOD4 label note if present)
            int lodIdx = baseName.IndexOf("LOD", StringComparison.OrdinalIgnoreCase);
            if (lodIdx > 0 && !baseName.Contains("LOD4") && !baseName.Contains("LOD 4"))
            {
                baseName = baseName.Substring(0, lodIdx).Trim();
            }

            // Translate native tags
            baseName = baseName.Replace("plateforme", "Platform")
                               .Replace("tourelle", "Turret Base")
                               .Replace("anneau", "Ring")
                               .Replace("corps", "Chassis")
                               .Replace("canon", "Cannon Tube")
                               .Replace("mur", "Wall")
                               .Replace("pilier", "Pillar")
                               .Trim();

            if (string.IsNullOrWhiteSpace(baseName) || baseName.Length < 2)
            {
                baseName = isPlatform ? "Platform Slab" : (isWall ? "Modular Wall" : (isColumn ? "Pillar Column" : (isDecor ? "Decor Prop" : "Architecture Block")));
            }

            return (subCategory, baseName, isPlatform);
        }
    }

    // =========================================================================
    // SECTION 2: SCENE HARVESTING PIPELINE
    // =========================================================================

    public static class SceneHarvestingService
    {
        public static Light NativeSceneSun = null;
        public static bool IsHarvestingAdditive = false;

        private static readonly List<Mesh> _proceduralMeshes = new List<Mesh>();
        private static readonly List<Material> _proceduralMaterials = new List<Material>();
        private static readonly List<GameObject> _proceduralTemplates = new List<GameObject>();
        private static GameObject _harvesterVault = null;

        public static void CleanupProceduralResources()
        {
            for (int i = 0; i < _proceduralMeshes.Count; i++)
            {
                if (_proceduralMeshes[i] != null) GameObject.Destroy(_proceduralMeshes[i]);
            }
            _proceduralMeshes.Clear();

            for (int i = 0; i < _proceduralMaterials.Count; i++)
            {
                if (_proceduralMaterials[i] != null) GameObject.Destroy(_proceduralMaterials[i]);
            }
            _proceduralMaterials.Clear();

            for (int i = 0; i < _proceduralTemplates.Count; i++)
            {
                if (_proceduralTemplates[i] != null) GameObject.Destroy(_proceduralTemplates[i]);
            }
            _proceduralTemplates.Clear();

            if (_harvesterVault != null)
            {
                GameObject.Destroy(_harvesterVault);
                _harvesterVault = null;
            }
        }

        public static void DebugDumpSceneLighting()
        {
            NativeSceneSun = null;

            if (RenderSettings.sun != null)
            {
                NativeSceneSun = RenderSettings.sun;
                return;
            }

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    if (roots[r] == null) continue;
                    Light[] lights = roots[r].GetComponentsInChildren<Light>(true);
                    for (int i = 0; i < lights.Length; i++)
                    {
                        Light l = lights[i];
                        if (l != null && l.type == LightType.Directional &&
                            !l.name.Contains("Template") && !l.name.StartsWith("Custom_"))
                        {
                            NativeSceneSun = l;
                            return;
                        }
                    }
                }
            }
        }

        public static Mesh CreateDoubleSidedPlaneMesh(float width, float height)
        {
            Mesh m = new Mesh { name = $"Laser_DoubleSided_{width}x{height}_Mesh" };

            float hw = width * 0.5f;
            float hh = height * 0.5f;
            float zOffset = 0.01f;

            m.vertices = new Vector3[]
            {
                new Vector3(-hw, -hh, zOffset),
                new Vector3(hw, -hh, zOffset),
                new Vector3(hw, hh, zOffset),
                new Vector3(-hw, hh, zOffset),
                new Vector3(-hw, -hh, -zOffset),
                new Vector3(hw, -hh, -zOffset),
                new Vector3(hw, hh, -zOffset),
                new Vector3(-hw, hh, -zOffset)
            };

            m.uv = new Vector2[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1)
            };

            m.normals = new Vector3[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward
            };

            m.triangles = new int[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7
            };

            m.RecalculateBounds();
            _proceduralMeshes.Add(m);
            return m;
        }

        public static void EnableGPUInstancingOnMaterial(Material mat)
        {
            if (mat == null) return;
            try { mat.enableInstancing = true; } catch { }
        }

        public static void HarvestAllSceneModels()
        {
            EditorSessionManager.AllAssets.Clear();
            CleanupProceduralResources();

            if (_harvesterVault == null)
            {
                _harvesterVault = new GameObject("Studio_Harvester_Vault");
                GameObject.DontDestroyOnLoad(_harvesterVault);
                _harvesterVault.SetActive(false);
            }

            HashSet<int> seenMeshInstanceIDs = new HashSet<int>();
            HashSet<Material> seenMaterials = new HashSet<Material>();
            Dictionary<int, Material[]> meshToOriginalMaterials = new Dictionary<int, Material[]>();
            Dictionary<string, int> displayNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // 1. Cache all materials & mapping across both MeshRenderer and SkinnedMeshRenderer
            MeshRenderer[] renderers = Resources.FindObjectsOfTypeAll<MeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer r = renderers[i];
                if (r == null) continue;

                Material[] mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat != null && seenMaterials.Add(mat))
                    {
                        EnableGPUInstancingOnMaterial(mat);
                        if (EditorSessionManager.CachedSceneMaterial == null && !mat.name.ToLower().Contains("laser"))
                        {
                            EditorSessionManager.CachedSceneMaterial = mat;
                        }
                    }
                }

                MeshFilter rmf = r.GetComponent<MeshFilter>();
                if (rmf != null && rmf.sharedMesh != null && mats != null && mats.Length > 0)
                {
                    int mId = rmf.sharedMesh.GetInstanceID();
                    if (!meshToOriginalMaterials.ContainsKey(mId))
                    {
                        meshToOriginalMaterials[mId] = mats;
                    }
                }
            }

            SkinnedMeshRenderer[] smRenderers = Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>();
            for (int i = 0; i < smRenderers.Length; i++)
            {
                SkinnedMeshRenderer smr = smRenderers[i];
                if (smr == null || smr.sharedMesh == null) continue;

                Material[] mats = smr.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat != null && seenMaterials.Add(mat))
                    {
                        EnableGPUInstancingOnMaterial(mat);
                    }
                }

                int mId = smr.sharedMesh.GetInstanceID();
                if (!meshToOriginalMaterials.ContainsKey(mId) && mats != null && mats.Length > 0)
                {
                    meshToOriginalMaterials[mId] = mats;
                }
            }

            // 2. Native gameplay entities & procedural hazards
            HarvestNativeGameplayEntities();
            HarvestProceduralHazardsAndLights();

            // 3. TARGETED HARVEST: Level Design roots (_LD, L_D, LevelDesign)
            HarvestTargetedRoots(new string[] { "_ld", "l_d", "ld", "leveldesign", "level_design" }, "Level Design", seenMeshInstanceIDs, displayNameCounts, meshToOriginalMaterials);

            // 4. TARGETED HARVEST: Level Art & Atmosphere roots (_LA, L_A, Atmosphere, Art, Props)
            HarvestTargetedRoots(new string[] { "_la", "l_a", "la", "levelatmosphere", "level_atmosphere", "levelart", "level_art", "atmosphere", "art", "props", "decor", "environment", "env", "world" }, "Level Art / Atmosphere", seenMeshInstanceIDs, displayNameCounts, meshToOriginalMaterials);

            // 5. Sweep all remaining loaded scene hierarchies
            HarvestAllLoadedScenesArchitecture(seenMeshInstanceIDs, displayNameCounts, meshToOriginalMaterials);

            // 6. FEATURE A: AssetBundle Deep Excavation (Reflection-based, zero compile dependencies)
            HarvestLoadedAssetBundles(seenMeshInstanceIDs, displayNameCounts, meshToOriginalMaterials);

            // 7. FEATURE F: Cross-Level / Multi-Tower Additive Harvesting
            HarvestCrossLevelTowers(seenMeshInstanceIDs, displayNameCounts, meshToOriginalMaterials);

            // 8. Deep search through remaining loaded components in memory
            HarvestLoadedComponentsFromMemory(seenMeshInstanceIDs, displayNameCounts, meshToOriginalMaterials);

            // 9. Cache lighting
            DebugDumpSceneLighting();

            // 10. Residual mesh memory sweep
            HarvestResidualMeshesFromMemory(seenMeshInstanceIDs, displayNameCounts, meshToOriginalMaterials);

            MelonLogger.Msg($">> [Harvest] Complete! Catalog populated with {EditorSessionManager.AllAssets.Count} unique assets (1.2m - 24.0m, LOD4 kept).");
        }

        // =========================================================================
        // FEATURE A: ASSETBUNDLE DEEP EXCAVATION (Decoupled Reflection)
        // =========================================================================

        public static void HarvestLoadedAssetBundles(HashSet<int> seenMeshIDs, Dictionary<string, int> displayNameCounts, Dictionary<int, Material[]> meshToOriginalMaterials)
        {
            try
            {
                Type assetBundleType = null;
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    assetBundleType = asms[i].GetType("UnityEngine.AssetBundle");
                    if (assetBundleType != null) break;
                }

                if (assetBundleType == null) return;

                MethodInfo getAllMethod = assetBundleType.GetMethod("GetAllLoadedAssetBundles", BindingFlags.Public | BindingFlags.Static);
                if (getAllMethod == null) return;

                IEnumerable rawBundles = getAllMethod.Invoke(null, null) as IEnumerable;
                if (rawBundles == null) return;

                MethodInfo loadAllMethod = assetBundleType.GetMethod("LoadAllAssets", new Type[] { typeof(Type) });
                if (loadAllMethod == null) return;

                int bundleHarvested = 0;

                foreach (object bundle in rawBundles)
                {
                    if (bundle == null) continue;

                    // 1. Inspect all GameObjects stored in the bundle
                    Il2CppSystem.Object[] rawGos = loadAllMethod.Invoke(bundle, new object[] { typeof(GameObject) }) as Il2CppSystem.Object[];
                    if (rawGos != null)
                    {
                        for (int i = 0; i < rawGos.Length; i++)
                        {
                            if (rawGos[i] == null) continue;
                            GameObject go = rawGos[i].TryCast<GameObject>();
                            if (go == null) continue;

                            MeshFilter[] mfs = go.GetComponentsInChildren<MeshFilter>(true);
                            for (int f = 0; f < mfs.Length; f++)
                            {
                                MeshFilter mf = mfs[f];
                                if (mf == null || mf.sharedMesh == null) continue;

                                Renderer r = mf.GetComponent<Renderer>();
                                if (TryHarvestMesh(mf.sharedMesh, mf.gameObject.name, mf.sharedMesh.name, mf.gameObject, r, seenMeshIDs, displayNameCounts, meshToOriginalMaterials))
                                {
                                    bundleHarvested++;
                                }
                            }

                            SkinnedMeshRenderer[] smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                            for (int s = 0; s < smrs.Length; s++)
                            {
                                SkinnedMeshRenderer smr = smrs[s];
                                if (smr == null || smr.sharedMesh == null) continue;

                                if (TryHarvestMesh(smr.sharedMesh, smr.gameObject.name, smr.sharedMesh.name, smr.gameObject, smr, seenMeshIDs, displayNameCounts, meshToOriginalMaterials))
                                {
                                    bundleHarvested++;
                                }
                            }
                        }
                    }

                    // 2. Direct Meshes stored in the bundle
                    Il2CppSystem.Object[] rawMeshes = loadAllMethod.Invoke(bundle, new object[] { typeof(Mesh) }) as Il2CppSystem.Object[];
                    if (rawMeshes != null)
                    {
                        for (int m = 0; m < rawMeshes.Length; m++)
                        {
                            if (rawMeshes[m] == null) continue;
                            Mesh mesh = rawMeshes[m].TryCast<Mesh>();
                            if (mesh == null) continue;

                            if (TryHarvestMesh(mesh, mesh.name, mesh.name, null, null, seenMeshIDs, displayNameCounts, meshToOriginalMaterials))
                            {
                                bundleHarvested++;
                            }
                        }
                    }
                }

                if (bundleHarvested > 0)
                {
                    MelonLogger.Msg($">> [Harvest-AssetBundles] Extracted {bundleHarvested} assets from loaded AssetBundles.");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Harvest-AssetBundles] Note: {ex.Message}");
            }
        }

        // =========================================================================
        // FEATURE F: CROSS-LEVEL / MULTI-TOWER ADDITIVE HARVESTING
        // =========================================================================

        public static void HarvestCrossLevelTowers(HashSet<int> seenMeshIDs, Dictionary<string, int> displayNameCounts, Dictionary<int, Material[]> meshToOriginalMaterials)
        {
            IsHarvestingAdditive = true;
            try
            {
                List<string> levelPaths = GetDiscoveredLevelScenePaths();
                string activeScenePath = SceneManager.GetActiveScene().path;

                for (int i = 0; i < levelPaths.Count; i++)
                {
                    string scenePath = levelPaths[i];
                    if (string.IsNullOrWhiteSpace(scenePath)) continue;

                    // Skip active scene (already scanned)
                    if (string.Equals(scenePath, activeScenePath, StringComparison.OrdinalIgnoreCase)) continue;

                    string sName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
                    try
                    {
                        SceneManager.LoadScene(scenePath, LoadSceneMode.Additive);
                        Scene loadedScene = SceneManager.GetSceneByPath(scenePath);

                        // Fixed: IsValid() is a method on the Scene struct
                        if (!loadedScene.IsValid())
                        {
                            loadedScene = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
                        }

                        if (loadedScene.IsValid() && loadedScene.isLoaded)
                        {
                            int prevCount = seenMeshIDs.Count;
                            HarvestSceneRoots(loadedScene, seenMeshIDs, displayNameCounts, meshToOriginalMaterials);
                            int added = seenMeshIDs.Count - prevCount;
                            MelonLogger.Msg($">> [Harvest-CrossLevel] Additively harvested {added} assets from '{sName}'");

                            SceneManager.UnloadSceneAsync(loadedScene);
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[Harvest-CrossLevel] Skipped '{sName}': {ex.Message}");
                    }
                }
            }
            finally
            {
                IsHarvestingAdditive = false;
            }
        }

        private static List<string> GetDiscoveredLevelScenePaths()
        {
            List<string> list = new List<string>();
            int count = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < count; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrEmpty(path)) continue;
                string pLow = path.ToLowerInvariant();

                // Harvest actual world levels/towers, ignoring menus, boots, and loaders
                if ((pLow.Contains("level") || pLow.Contains("spark") || pLow.Contains("tower") || pLow.Contains("stage")) &&
                    !pLow.Contains("menu") && !pLow.Contains("boot") && !pLow.Contains("init") && !pLow.Contains("title") && !pLow.Contains("load"))
                {
                    list.Add(path);
                }
            }
            return list;
        }

        private static void HarvestSceneRoots(Scene scene, HashSet<int> seenMeshIDs, Dictionary<string, int> displayNameCounts, Dictionary<int, Material[]> meshToOriginalMaterials)
        {
            if (!scene.isLoaded) return;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                GameObject root = roots[r];
                if (root == null) continue;

                MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
                for (int f = 0; f < filters.Length; f++)
                {
                    MeshFilter mf = filters[f];
                    if (mf == null || mf.sharedMesh == null || mf.gameObject == null) continue;

                    Renderer rend = mf.GetComponent<MeshRenderer>() ?? mf.GetComponentInParent<MeshRenderer>();
                    TryHarvestMesh(mf.sharedMesh, mf.gameObject.name, mf.sharedMesh.name, mf.gameObject, rend, seenMeshIDs, displayNameCounts, meshToOriginalMaterials);
                }

                SkinnedMeshRenderer[] smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (int s = 0; s < smrs.Length; s++)
                {
                    SkinnedMeshRenderer smr = smrs[s];
                    if (smr == null || smr.sharedMesh == null || smr.gameObject == null) continue;

                    TryHarvestMesh(smr.sharedMesh, smr.gameObject.name, smr.sharedMesh.name, smr.gameObject, smr, seenMeshIDs, displayNameCounts, meshToOriginalMaterials);
                }
            }
        }

        // =========================================================================
        // TARGETED & GENERAL SWEEPS
        // =========================================================================

        private static void HarvestTargetedRoots(string[] targetKeywords, string groupLabel, HashSet<int> seenMeshIDs, Dictionary<string, int> displayNameCounts, Dictionary<int, Material[]> meshToOriginalMaterials)
        {
            int harvestedCount = 0;

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    GameObject root = roots[r];
                    if (root == null) continue;

                    string rLow = root.name.ToLowerInvariant();
                    bool isTarget = false;

                    for (int k = 0; k < targetKeywords.Length; k++)
                    {
                        if (rLow == targetKeywords[k] || rLow.Contains(targetKeywords[k]))
                        {
                            isTarget = true;
                            break;
                        }
                    }

                    if (!isTarget) continue;

                    MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
                    for (int f = 0; f < filters.Length; f++)
                    {
                        MeshFilter mf = filters[f];
                        if (mf == null || mf.sharedMesh == null || mf.gameObject == null) continue;

                        if (TryHarvestMesh(mf.sharedMesh, mf.gameObject.name, mf.sharedMesh.name, mf.gameObject,
                            mf.GetComponent<MeshRenderer>() ?? mf.GetComponentInParent<MeshRenderer>(),
                            seenMeshIDs, displayNameCounts, meshToOriginalMaterials))
                        {
                            harvestedCount++;
                        }
                    }

                    SkinnedMeshRenderer[] smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    for (int sm = 0; sm < smrs.Length; sm++)
                    {
                        SkinnedMeshRenderer smr = smrs[sm];
                        if (smr == null || smr.sharedMesh == null || smr.gameObject == null) continue;

                        if (TryHarvestMesh(smr.sharedMesh, smr.gameObject.name, smr.sharedMesh.name, smr.gameObject,
                            smr, seenMeshIDs, displayNameCounts, meshToOriginalMaterials))
                        {
                            harvestedCount++;
                        }
                    }
                }
            }

            if (harvestedCount > 0)
            {
                MelonLogger.Msg($">> [Harvest-{groupLabel}] Harvested {harvestedCount} models from '{groupLabel}' roots.");
            }
        }

        private static void HarvestAllLoadedScenesArchitecture(HashSet<int> seenMeshIDs, Dictionary<string, int> displayNameCounts, Dictionary<int, Material[]> meshToOriginalMaterials)
        {
            int generalCount = 0;

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    GameObject root = roots[r];
                    if (root == null) continue;

                    if (root == _harvesterVault || root.name.StartsWith("Studio_") || root.name.StartsWith("Custom_"))
                        continue;

                    MeshFilter[] childFilters = root.GetComponentsInChildren<MeshFilter>(true);
                    for (int c = 0; c < childFilters.Length; c++)
                    {
                        MeshFilter mf = childFilters[c];
                        if (mf == null || mf.sharedMesh == null || mf.gameObject == null) continue;

                        if (TryHarvestMesh(mf.sharedMesh, mf.gameObject.name, mf.sharedMesh.name, mf.gameObject,
                            mf.GetComponent<MeshRenderer>() ?? mf.GetComponentInParent<MeshRenderer>(),
                            seenMeshIDs, displayNameCounts, meshToOriginalMaterials))
                        {
                            generalCount++;
                        }
                    }

                    SkinnedMeshRenderer[] smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    for (int sm = 0; sm < smrs.Length; sm++)
                    {
                        SkinnedMeshRenderer smr = smrs[sm];
                        if (smr == null || smr.sharedMesh == null || smr.gameObject == null) continue;

                        if (TryHarvestMesh(smr.sharedMesh, smr.gameObject.name, smr.sharedMesh.name, smr.gameObject,
                            smr, seenMeshIDs, displayNameCounts, meshToOriginalMaterials))
                        {
                            generalCount++;
                        }
                    }
                }
            }

            if (generalCount > 0)
            {
                MelonLogger.Msg($">> [Harvest-General] Harvested {generalCount} additional models across scene hierarchies.");
            }
        }

        private static void HarvestLoadedComponentsFromMemory(HashSet<int> seenMeshIDs, Dictionary<string, int> displayNameCounts, Dictionary<int, Material[]> meshToOriginalMaterials)
        {
            int extraCount = 0;

            MeshFilter[] allFilters = Resources.FindObjectsOfTypeAll<MeshFilter>();
            for (int i = 0; i < allFilters.Length; i++)
            {
                MeshFilter mf = allFilters[i];
                if (mf == null || mf.sharedMesh == null || mf.gameObject == null) continue;

                if (TryHarvestMesh(mf.sharedMesh, mf.gameObject.name, mf.sharedMesh.name, mf.gameObject,
                    mf.GetComponent<MeshRenderer>(), seenMeshIDs, displayNameCounts, meshToOriginalMaterials))
                {
                    extraCount++;
                }
            }

            SkinnedMeshRenderer[] allSkinned = Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>();
            for (int i = 0; i < allSkinned.Length; i++)
            {
                SkinnedMeshRenderer smr = allSkinned[i];
                if (smr == null || smr.sharedMesh == null || smr.gameObject == null) continue;

                if (TryHarvestMesh(smr.sharedMesh, smr.gameObject.name, smr.sharedMesh.name, smr.gameObject,
                    smr, seenMeshIDs, displayNameCounts, meshToOriginalMaterials))
                {
                    extraCount++;
                }
            }

            if (extraCount > 0)
            {
                MelonLogger.Msg($">> [Harvest-DeepMemory] Discovered {extraCount} additional prefab/memory components.");
            }
        }

        private static bool TryHarvestMesh(Mesh mesh, string goName, string meshName, GameObject sourceGo, Renderer sourceRend,
            HashSet<int> seenMeshIDs, Dictionary<string, int> displayNameCounts, Dictionary<int, Material[]> meshToOriginalMaterials)
        {
            if (mesh == null) return false;
            int instanceID = mesh.GetInstanceID();

            if (seenMeshIDs.Contains(instanceID)) return false;

            if (sourceGo != null)
            {
                if (sourceGo.GetComponent<Jumper>() != null || sourceGo.GetComponent<TurretScript>() != null ||
                    sourceGo.GetComponent<LaserScript>() != null || sourceGo.GetComponent<Helix>() != null ||
                    sourceGo.GetComponent<CheckPointScript>() != null)
                {
                    return false;
                }
            }

            string mName = meshName.Trim();
            string gName = goName.Trim();

            if (ModelHarvestFilter.ShouldIgnoreMesh(mesh, mName, gName, sourceGo)) return false;
            if (!ModelHarvestFilter.IsValidHarvestSize(mesh.bounds, sourceGo != null ? sourceGo.transform : null, 1.2f, 24.0f)) return false;

            seenMeshIDs.Add(instanceID);

            GameObject templateObj = CreateCleanVaultTemplate(mesh, gName, mName, sourceRend, meshToOriginalMaterials);
            _proceduralTemplates.Add(templateObj);

            RegisterModelAsset(templateObj, mesh, gName, mName, displayNameCounts);
            return true;
        }

        private static void HarvestResidualMeshesFromMemory(HashSet<int> seenMeshIDs, Dictionary<string, int> displayNameCounts, Dictionary<int, Material[]> meshToOriginalMaterials)
        {
            Mesh[] allLoadedMeshes = Resources.FindObjectsOfTypeAll<Mesh>();
            int memoryCount = 0;

            for (int m = 0; m < allLoadedMeshes.Length; m++)
            {
                Mesh mesh = allLoadedMeshes[m];
                if (mesh == null) continue;

                int instanceID = mesh.GetInstanceID();
                if (seenMeshIDs.Contains(instanceID)) continue;

                string mName = mesh.name.Trim();
                if (ModelHarvestFilter.ShouldIgnoreMesh(mesh, mName, mName, null)) continue;
                if (!ModelHarvestFilter.IsValidHarvestSize(mesh.bounds, null, 1.2f, 24.0f)) continue;

                seenMeshIDs.Add(instanceID);

                GameObject templateGo = CreateCleanVaultTemplate(mesh, mName, mName, null, meshToOriginalMaterials);
                _proceduralTemplates.Add(templateGo);
                RegisterModelAsset(templateGo, mesh, mName, mName, displayNameCounts);
                memoryCount++;
            }

            if (memoryCount > 0)
            {
                MelonLogger.Msg($">> [Harvest-Memory] Extracted {memoryCount} residual loaded meshes (1.2m - 24.0m).");
            }
        }

        private static GameObject CreateCleanVaultTemplate(Mesh mesh, string goName, string mName, Renderer sourceRend, Dictionary<int, Material[]> meshToOriginalMaterials)
        {
            GameObject templateGo = new GameObject($"Template_{mName}");
            templateGo.transform.SetParent(_harvesterVault.transform, false);
            templateGo.transform.position = Vector3.zero;
            templateGo.transform.rotation = Quaternion.identity;
            templateGo.transform.localScale = Vector3.one;

            MeshFilter nmf = templateGo.AddComponent<MeshFilter>();
            nmf.sharedMesh = mesh;

            MeshRenderer nmr = templateGo.AddComponent<MeshRenderer>();
            int instanceID = mesh.GetInstanceID();

            if (sourceRend != null && sourceRend.sharedMaterials != null && sourceRend.sharedMaterials.Length > 0)
            {
                nmr.sharedMaterials = sourceRend.sharedMaterials;
                for (int o = 0; o < sourceRend.sharedMaterials.Length; o++) EnableGPUInstancingOnMaterial(sourceRend.sharedMaterials[o]);
            }
            else if (meshToOriginalMaterials.TryGetValue(instanceID, out Material[] origMats) && origMats != null && origMats.Length > 0)
            {
                nmr.sharedMaterials = origMats;
                for (int o = 0; o < origMats.Length; o++) EnableGPUInstancingOnMaterial(origMats[o]);
            }
            else
            {
                nmr.sharedMaterial = EditorSessionManager.CachedSceneMaterial;
            }

            templateGo.SetActive(false);
            return templateGo;
        }

        private static void RegisterModelAsset(GameObject templateGo, Mesh mesh, string goName, string mName, Dictionary<string, int> displayNameCounts)
        {
            var classification = GeometryClassifier.Classify(mesh.bounds, goName, mName);

            string finalName = classification.friendlyName;
            if (displayNameCounts.TryGetValue(classification.friendlyName, out int count))
            {
                count++;
                displayNameCounts[classification.friendlyName] = count;
                finalName = $"{classification.friendlyName} ({count})";
            }
            else
            {
                displayNameCounts[classification.friendlyName] = 1;
            }

            var asset = new CatalogAsset
            {
                DisplayName = finalName,
                SourceTemplate = templateGo,
                FilterMesh = mesh,
                Category = AssetCategory.Building,
                SubCategory = classification.subCategory,
                Traits = AssetTrait.Architecture,
                DefaultScale = classification.isPlatform ? 0.55f : 1.0f,
                VerticalOffset = 0f,
                BaseRotation = (classification.isPlatform && (finalName.Contains("16x16") || finalName.Contains("Platform")))
                    ? Quaternion.Euler(0f, 0f, 90f)
                    : Quaternion.identity
            };

            asset.ComputeSizeMetrics();
            EditorSessionManager.AllAssets.Add(asset);
        }

        // =========================================================================
        // SECTION 3: NATIVE GAMEPLAY HARVESTING
        // =========================================================================

        private static void HarvestNativeGameplayEntities()
        {
            // 1. Launch Jumper
            try
            {
                EditorSessionManager.PrefabJumper = GameObject.FindObjectOfType<Jumper>();
                if (EditorSessionManager.PrefabJumper == null)
                {
                    GameObject ld = GameObject.Find("_LD") ?? GameObject.Find("L_D") ?? GameObject.Find("l_d");
                    if (ld != null)
                    {
                        Jumper[] jumpers = ld.GetComponentsInChildren<Jumper>(true);
                        if (jumpers.Length > 0) EditorSessionManager.PrefabJumper = jumpers[0];
                    }
                }
                if (EditorSessionManager.PrefabJumper == null)
                {
                    Jumper[] all = Resources.FindObjectsOfTypeAll<Jumper>();
                    if (all != null && all.Length > 0) EditorSessionManager.PrefabJumper = all[0];
                }
            }
            catch { }

            if (EditorSessionManager.PrefabJumper != null)
            {
                var jAsset = new CatalogAsset
                {
                    DisplayName = "Launch Jumper Pad",
                    SourceTemplate = EditorSessionManager.PrefabJumper.gameObject,
                    Category = AssetCategory.Gameplay,
                    SubCategory = "Gameplay",
                    Traits = AssetTrait.Jumper,
                    DefaultScale = 1.0f
                };
                jAsset.ComputeSizeMetrics();
                EditorSessionManager.AllAssets.Add(jAsset);
            }

            // 2. Checkpoint & Gates
            try
            {
                EditorSessionManager.PrefabCheckPoint = GameObject.FindObjectOfType<CheckPointScript>();
                if (EditorSessionManager.PrefabCheckPoint == null)
                {
                    GameObject ld = GameObject.Find("_LD") ?? GameObject.Find("L_D") ?? GameObject.Find("l_d");
                    if (ld != null)
                    {
                        CheckPointScript[] cps = ld.GetComponentsInChildren<CheckPointScript>(true);
                        if (cps.Length > 0) EditorSessionManager.PrefabCheckPoint = cps[0];
                    }
                }
                if (EditorSessionManager.PrefabCheckPoint == null)
                {
                    CheckPointScript[] all = Resources.FindObjectsOfTypeAll<CheckPointScript>();
                    if (all != null && all.Length > 0) EditorSessionManager.PrefabCheckPoint = all[0];
                }
            }
            catch { }

            if (EditorSessionManager.PrefabCheckPoint != null)
            {
                var cpAsset = new CatalogAsset
                {
                    DisplayName = "Checkpoint Gate",
                    SourceTemplate = EditorSessionManager.PrefabCheckPoint.gameObject,
                    Category = AssetCategory.Gameplay,
                    SubCategory = "Gameplay",
                    Traits = AssetTrait.Checkpoint,
                    DefaultScale = 1.0f,
                    VerticalOffset = -0.32f,
                    BaseRotation = Quaternion.identity
                };
                cpAsset.ComputeSizeMetrics();
                EditorSessionManager.AllAssets.Add(cpAsset);

                var spawnAsset = new CatalogAsset
                {
                    DisplayName = "Entry Checkpoint (Start)",
                    SourceTemplate = EditorSessionManager.PrefabCheckPoint.gameObject,
                    Category = AssetCategory.Gameplay,
                    SubCategory = "Gameplay",
                    Traits = AssetTrait.Checkpoint | AssetTrait.SpawnGate,
                    DefaultScale = 1.0f,
                    VerticalOffset = -0.32f,
                    BaseRotation = Quaternion.identity
                };
                spawnAsset.ComputeSizeMetrics();
                EditorSessionManager.AllAssets.Add(spawnAsset);

                var goalAsset = new CatalogAsset
                {
                    DisplayName = "Goal Checkpoint (Finish)",
                    SourceTemplate = EditorSessionManager.PrefabCheckPoint.gameObject,
                    Category = AssetCategory.Gameplay,
                    SubCategory = "Gameplay",
                    Traits = AssetTrait.Checkpoint | AssetTrait.GoalGate,
                    DefaultScale = 1.0f,
                    VerticalOffset = -0.32f,
                    BaseRotation = Quaternion.identity
                };
                goalAsset.ComputeSizeMetrics();
                EditorSessionManager.AllAssets.Add(goalAsset);
            }

            // 3. Defense Turret
            try
            {
                TurretScript nativeTurret = GameObject.FindObjectOfType<TurretScript>();
                if (nativeTurret == null)
                {
                    TurretScript[] all = Resources.FindObjectsOfTypeAll<TurretScript>();
                    if (all != null && all.Length > 0) nativeTurret = all[0];
                }

                if (nativeTurret != null)
                {
                    Transform rootT = nativeTurret.transform;
                    while (rootT.parent != null && (rootT.parent.name.ToLower().Contains("tourelle") || rootT.parent.name.ToLower().Contains("turret")))
                        rootT = rootT.parent;
                    EditorSessionManager.PrefabTurret = rootT.gameObject;
                }
            }
            catch { }

            if (EditorSessionManager.PrefabTurret != null)
            {
                var turretAsset = new CatalogAsset
                {
                    DisplayName = "Defense Turret Enemy",
                    SourceTemplate = EditorSessionManager.PrefabTurret,
                    Category = AssetCategory.Gameplay,
                    SubCategory = "Hazards",
                    Traits = AssetTrait.Turret
                };
                turretAsset.ComputeSizeMetrics();
                EditorSessionManager.AllAssets.Add(turretAsset);
            }

            // 4. Helix Turbine Fan
            try
            {
                Helix nativeHelix = GameObject.FindObjectOfType<Helix>();
                if (nativeHelix == null)
                {
                    Helix[] all = Resources.FindObjectsOfTypeAll<Helix>();
                    if (all != null && all.Length > 0) nativeHelix = all[0];
                }
                if (nativeHelix != null) EditorSessionManager.PrefabHelix = nativeHelix.gameObject;
            }
            catch { }

            if (EditorSessionManager.PrefabHelix != null)
            {
                var helixAsset = new CatalogAsset
                {
                    DisplayName = "Helix Turbine Fan",
                    SourceTemplate = EditorSessionManager.PrefabHelix,
                    Category = AssetCategory.Gameplay,
                    SubCategory = "Hazards",
                    Traits = AssetTrait.Turbine
                };
                helixAsset.ComputeSizeMetrics();
                EditorSessionManager.AllAssets.Add(helixAsset);
            }

            // 5. Interuptor / Switch
            try
            {
                Interuptor nativeInteruptor = GameObject.FindObjectOfType<Interuptor>();
                if (nativeInteruptor == null)
                {
                    Interuptor[] all = Resources.FindObjectsOfTypeAll<Interuptor>();
                    if (all != null && all.Length > 0) nativeInteruptor = all[0];
                }
                if (nativeInteruptor != null)
                {
                    Transform rootT = nativeInteruptor.transform;
                    while (rootT.parent != null && (
                        rootT.parent.name.ToLower().Contains("interupt") ||
                        rootT.parent.name.ToLower().Contains("switch") ||
                        rootT.parent.name.ToLower().Contains("target")))
                    {
                        rootT = rootT.parent;
                    }
                    SwitchService.PrefabSwitch = rootT.gameObject;
                }
            }
            catch { }

            if (SwitchService.PrefabSwitch != null)
            {
                var switchAsset = new CatalogAsset
                {
                    DisplayName = "Switch Target",
                    SourceTemplate = SwitchService.PrefabSwitch,
                    Category = AssetCategory.Gameplay,
                    SubCategory = "Gameplay",
                    Traits = AssetTrait.Switch,
                    DefaultScale = 1.0f
                };
                switchAsset.ComputeSizeMetrics();
                EditorSessionManager.AllAssets.Add(switchAsset);
            }
        }

        private static void HarvestProceduralHazardsAndLights()
        {
            // 1. Tech Spotlight
            GameObject spotTemplate = new GameObject("Template_Spotlight");
            Light spotLight = spotTemplate.AddComponent<Light>();
            spotLight.type = LightType.Spot;
            spotLight.range = 120f;
            spotLight.spotAngle = 60f;
            spotLight.color = Color.cyan;
            spotLight.intensity = 28000f;

            GameObject spotHousing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            spotHousing.name = "Light_Housing";
            spotHousing.transform.SetParent(spotTemplate.transform, false);
            spotHousing.transform.localScale = new Vector3(0.6f, 0.4f, 0.6f);
            spotHousing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Collider sCol = spotHousing.GetComponent<Collider>();
            if (sCol != null) GameObject.DestroyImmediate(sCol);

            GameObject spotLens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            spotLens.name = "Light_Lens";
            spotLens.transform.SetParent(spotTemplate.transform, false);
            spotLens.transform.localScale = new Vector3(0.55f, 0.55f, 0.35f);
            spotLens.transform.localPosition = new Vector3(0f, 0f, 0.38f);
            Collider slCol = spotLens.GetComponent<Collider>();
            if (slCol != null) GameObject.DestroyImmediate(slCol);

            if (EditorSessionManager.CachedSceneMaterial != null)
            {
                Renderer hr = spotHousing.GetComponent<Renderer>();
                if (hr != null) hr.material = EditorSessionManager.CachedSceneMaterial;
            }
            spotTemplate.SetActive(false);

            var spotAsset = new CatalogAsset
            {
                DisplayName = "Tech Spotlight",
                SourceTemplate = spotTemplate,
                FilterMesh = spotHousing.GetComponent<MeshFilter>()?.sharedMesh,
                Category = AssetCategory.Gameplay,
                SubCategory = "Lighting",
                Traits = AssetTrait.Spotlight,
                DefaultScale = 1.0f
            };
            spotAsset.ComputeSizeMetrics();
            EditorSessionManager.AllAssets.Add(spotAsset);

            // 2. Global Sunlight
            GameObject sunTemplate = new GameObject("Template_Sunlight");
            Light sunLight = sunTemplate.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.color = new Color(1f, 0.85f, 0.6f);
            sunLight.intensity = 3.5f;

            GameObject sunOrb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sunOrb.name = "Light_Housing";
            sunOrb.transform.SetParent(sunTemplate.transform, false);
            sunOrb.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);
            Collider sunCol = sunOrb.GetComponent<Collider>();
            if (sunCol != null) GameObject.DestroyImmediate(sunCol);

            Shader unlitShader = Shader.Find("Unlit/Color") ?? Shader.Find("Particles/Standard Unlit");
            if (unlitShader != null)
            {
                Material sunMat = new Material(unlitShader) { color = new Color(1f, 0.88f, 0.35f, 1f) };
                EnableGPUInstancingOnMaterial(sunMat);
                _proceduralMaterials.Add(sunMat);
                sunOrb.GetComponent<Renderer>().material = sunMat;
            }
            sunTemplate.SetActive(false);

            var sunAsset = new CatalogAsset
            {
                DisplayName = "Global Sunlight",
                SourceTemplate = sunTemplate,
                FilterMesh = sunOrb.GetComponent<MeshFilter>()?.sharedMesh,
                Category = AssetCategory.Gameplay,
                SubCategory = "Lighting",
                Traits = AssetTrait.Sunlight,
                DefaultScale = 1.0f,
                BaseRotation = Quaternion.Euler(50f, -30f, 0f)
            };
            sunAsset.ComputeSizeMetrics();
            EditorSessionManager.AllAssets.Add(sunAsset);

            // 3. Skybox Controller
            GameObject skyTemplate = new GameObject("Template_Skybox_Controller");
            StudioGizmoController.SkyboxWidgetBuilder.CreateWidget(skyTemplate.transform);
            BoxCollider skyCol = skyTemplate.AddComponent<BoxCollider>();
            skyCol.size = new Vector3(2.5f, 2.5f, 2.5f);
            skyCol.isTrigger = true;
            skyTemplate.SetActive(false);

            var skyAsset = new CatalogAsset
            {
                DisplayName = "Skybox Controller",
                SourceTemplate = skyTemplate,
                Category = AssetCategory.Gameplay,
                SubCategory = "Lighting",
                Traits = AssetTrait.Skybox,
                DefaultScale = 1.0f
            };
            skyAsset.ComputeSizeMetrics();
            EditorSessionManager.AllAssets.Add(skyAsset);

            // 4. Laser Materials & Barriers
            Material laserMat = GameObject.FindObjectOfType<LaserManager>()?._sharedMaterial;
            if (laserMat != null)
            {
                EnableGPUInstancingOnMaterial(laserMat);
            }
            else if (unlitShader != null)
            {
                laserMat = new Material(unlitShader) { color = new Color(1f, 0.05f, 0.05f, 0.9f) };
                EnableGPUInstancingOnMaterial(laserMat);
                _proceduralMaterials.Add(laserMat);
            }

            RegisterLaserBarrier("Laser Barrier Compact (4m)", 4f, 3f, laserMat, AssetTrait.Laser);
            RegisterLaserBarrier("Laser Barrier Medium (8m)", 8f, 4f, laserMat, AssetTrait.Laser);
            RegisterLaserBarrier("Laser Barrier Long (22m)", 22f, 4f, laserMat, AssetTrait.Laser);

            // 5. Rotating Laser
            GameObject rotLaserRoot = new GameObject("Template_Rotating_Laser");
            GameObject centerHub = GameObject.CreatePrimitive(PrimitiveType.Cube);
            centerHub.name = "CenterHub";
            centerHub.transform.SetParent(rotLaserRoot.transform, false);
            centerHub.transform.localScale = new Vector3(1.4f, 2.8f, 1.4f);
            Collider hubCol = centerHub.GetComponent<Collider>();
            if (hubCol != null) GameObject.DestroyImmediate(hubCol);

            GameObject rotBeamObj = new GameObject("Beam");
            rotBeamObj.transform.SetParent(rotLaserRoot.transform, false);
            Mesh rotBeamMesh = CreateDoubleSidedPlaneMesh(18f, 2.2f);
            rotBeamObj.AddComponent<MeshFilter>().sharedMesh = rotBeamMesh;
            if (laserMat != null) rotBeamObj.AddComponent<MeshRenderer>().sharedMaterial = laserMat;
            var rbc = rotBeamObj.AddComponent<BoxCollider>();
            rbc.isTrigger = true; rbc.size = new Vector3(18f, 2.2f, 0.35f);
            rotBeamObj.AddComponent<LaserScript>();
            rotLaserRoot.SetActive(false);

            var rotLaserAsset = new CatalogAsset
            {
                DisplayName = "Rotating Laser Barrier (18m)",
                SourceTemplate = rotLaserRoot,
                FilterMesh = rotBeamMesh,
                Category = AssetCategory.Gameplay,
                SubCategory = "Hazards",
                Traits = AssetTrait.Laser | AssetTrait.RotatingLaser
            };
            rotLaserAsset.ComputeSizeMetrics();
            EditorSessionManager.AllAssets.Add(rotLaserAsset);
        }

        private static void RegisterLaserBarrier(string name, float width, float height, Material mat, AssetTrait traits)
        {
            GameObject go = new GameObject("Template_" + name.Replace(" ", "_"));
            Mesh mesh = CreateDoubleSidedPlaneMesh(width, height);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            if (mat != null) go.AddComponent<MeshRenderer>().sharedMaterial = mat;

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(width, height, 0.35f);
            go.AddComponent<LaserScript>();
            go.SetActive(false);

            var asset = new CatalogAsset
            {
                DisplayName = name,
                SourceTemplate = go,
                FilterMesh = mesh,
                Category = AssetCategory.Gameplay,
                SubCategory = "Hazards",
                Traits = traits
            };
            asset.ComputeSizeMetrics();
            EditorSessionManager.AllAssets.Add(asset);
        }

        // =========================================================================
        // SECTION 4: LEVEL GEOMETRY HIDING & RESCUE
        // =========================================================================

        public static void HideVanillaLevelGeometry()
        {
            var activeScene = SceneManager.GetActiveScene();
            string sName = activeScene.name.ToLower();
            if (sName.Contains("menu")) return;

            // 1. Rescue core runtime objects
            GameObject player = EditorSessionManager.FindPlayerEntity();
            if (player != null)
            {
                player.transform.SetParent(null, true);
                player.SetActive(true);
            }

            if (NativeSceneSun != null)
            {
                NativeSceneSun.transform.SetParent(null, true);
                NativeSceneSun.gameObject.SetActive(true);
                NativeSceneSun.enabled = true;
            }

            StartLevelManager slm = GameObject.FindObjectOfType<StartLevelManager>();
            if (slm != null)
            {
                slm.transform.SetParent(null, true);
                slm.gameObject.SetActive(true);
            }

            UnityEngine.EventSystems.EventSystem es = GameObject.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
            if (es != null)
            {
                es.transform.SetParent(null, true);
                es.gameObject.SetActive(true);
                es.enabled = true;
                var sim = es.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                if (sim != null) sim.enabled = true;
            }

            // 2. Hide vanilla level design roots, preserving level atmosphere (_LA)
            GameObject[] rootObjects = activeScene.GetRootGameObjects();
            for (int i = 0; i < rootObjects.Length; i++)
            {
                GameObject root = rootObjects[i];
                if (root == null) continue;
                if (IsProtected(root, player, slm != null ? slm.gameObject : null)) continue;

                string rLow = root.name.ToLower();
                if (rLow == "_ld" || rLow == "l_d" || rLow == "ld")
                {
                    root.SetActive(false);
                    continue;
                }

                if (rLow == "_la" || rLow == "l_a" || rLow == "la")
                {
                    root.SetActive(true);
                    continue;
                }

                if (IsVanillaGameplayHazardOrPickup(root))
                {
                    root.SetActive(false);
                    continue;
                }

                Transform[] children = root.GetComponentsInChildren<Transform>(true);
                for (int c = 0; c < children.Length; c++)
                {
                    Transform t = children[c];
                    if (t == null) continue;
                    GameObject go = t.gameObject;

                    if (IsProtected(go, player, slm != null ? slm.gameObject : null)) continue;

                    if (IsVanillaGameplayHazardOrPickup(go))
                    {
                        go.SetActive(false);
                    }
                }
            }
        }

        private static bool IsVanillaGameplayHazardOrPickup(GameObject go)
        {
            if (go == null) return false;
            string n = go.name.ToLower();

            if (n.Contains("spark") && !n.Contains("sky") && !n.Contains("light") && !n.Contains("platform") && !n.Contains("floor"))
                return true;

            if (n.Contains("zone") && !n.Contains("sky") && !n.Contains("cloud") &&
                !n.Contains("fog") && !n.Contains("volume") && !n.Contains("atmosphere"))
                return true;

            if (n.Contains("trigger") && (n.Contains("kill") || n.Contains("death") ||
                n.Contains("fall") || n.Contains("void") || n.Contains("respawn") || n.Contains("checkpoint")))
                return true;

            return false;
        }

        private static bool IsAtmosphereOrSkyObject(GameObject go)
        {
            if (go == null) return false;
            string nLow = go.name.ToLower();

            if (nLow.Contains("sky") || nLow.Contains("cloud") || nLow.Contains("fog") ||
                nLow.Contains("volume") || nLow.Contains("atmosphere") || nLow.Contains("dome") ||
                nLow.Contains("horizon") || nLow.Contains("backdrop") || nLow.Contains("star") ||
                nLow.Contains("sun") || nLow.Contains("light") || nLow.Contains("env") ||
                nLow.Contains("ambient") || nLow.Contains("post"))
            {
                return true;
            }

            Component[] comps = go.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue;
                string cName = comps[i].GetIl2CppType().Name.ToLower();
                if (cName.Contains("volume") || cName.Contains("sky") || cName.Contains("cloud") ||
                    cName.Contains("atmosphere") || cName.Contains("lightdata"))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsProtected(GameObject go, GameObject player, GameObject slm)
        {
            if (go == null) return false;

            if (player != null && (go == player || go.transform.IsChildOf(player.transform)))
                return true;

            if (NativeSceneSun != null && (go == NativeSceneSun.gameObject || go.transform.IsChildOf(NativeSceneSun.transform)))
                return true;

            if (slm != null && (go == slm || go.transform.IsChildOf(slm.transform)))
                return true;

            if (go.GetComponent<UnityEngine.EventSystems.EventSystem>() != null ||
                go.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>() != null ||
                go.GetComponent<Canvas>() != null)
                return true;

            if (IsAtmosphereOrSkyObject(go))
                return true;

            if (_harvesterVault != null && (go == _harvesterVault || go.transform.IsChildOf(_harvesterVault.transform)))
                return true;

            string name = go.name;
            if (name.StartsWith("Template_") ||
                name.StartsWith("Custom_") ||
                name.StartsWith("Studio_") ||
                name.StartsWith("Editor_") ||
                name.StartsWith("Holographic_") ||
                name.StartsWith("Waypoint_") ||
                name.StartsWith("Highlight_") ||
                name.StartsWith("Card_"))
            {
                return true;
            }

            if (EditorSessionManager.PlacedObjects != null && EditorSessionManager.PlacedObjects.Contains(go))
                return true;

            return false;
        }
    }

    // =========================================================================
    // SECTION 5: ENGINE HARMONY PATCHES
    // =========================================================================

    [HarmonyPatch(typeof(StartLevelManager), nameof(StartLevelManager.StartLevelSequence))]
    public static class StartLevelPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Do not initialize custom level if an additive background scan is running
            if (SceneHarvestingService.IsHarvestingAdditive) return;

            string currentScene = SceneManager.GetActiveScene().name.ToLower();
            if (currentScene.Contains("menu")) return;

            if (EditorSessionManager.IsCustomSessionActive && !EditorSessionManager.IsLevelInitialized)
            {
                EditorSessionManager.InitializeCustomLevel();
            }
        }
    }

    [HarmonyPatch(typeof(TurretScript), nameof(TurretScript.Shoot))]
    public static class TurretShootPatch
    {
        [HarmonyPostfix]
        public static void Postfix(TurretScript __instance)
        {
            if (__instance == null || __instance._bulletPrefab == null) return;

            try
            {
                string bulletPrefabName = __instance._bulletPrefab.name;
                Vector3 turretPos = __instance.transform.position;

                Collider[] turretCols = __instance.transform.root.GetComponentsInChildren<Collider>(true);
                SphereCollider triggerSphere = __instance._triggerAnimation;

                Collider[] nearbyCols = Physics.OverlapSphere(turretPos, 6.0f, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < nearbyCols.Length; i++)
                {
                    Collider hitCol = nearbyCols[i];
                    if (hitCol == null) continue;

                    GameObject hitGo = hitCol.gameObject;
                    bool isBullet = hitGo.name.Contains(bulletPrefabName) ||
                                    hitGo.name.ToLower().Contains("bullet") ||
                                    (hitCol.transform.root != null && hitCol.transform.root.name.ToLower().Contains("bullet"));

                    if (!isBullet) continue;

                    Collider[] bulletCols = hitCol.transform.root.GetComponentsInChildren<Collider>(true);
                    for (int b = 0; b < bulletCols.Length; b++)
                    {
                        if (bulletCols[b] == null) continue;

                        for (int c = 0; c < turretCols.Length; c++)
                        {
                            if (turretCols[c] != null)
                            {
                                Physics.IgnoreCollision(bulletCols[b], turretCols[c], true);
                            }
                        }

                        if (triggerSphere != null)
                        {
                            Physics.IgnoreCollision(bulletCols[b], triggerSphere, true);
                        }
                    }
                }
            }
            catch { }
        }
    }
}