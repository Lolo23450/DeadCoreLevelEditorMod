using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using SceneManager = UnityEngine.SceneManagement.SceneManager;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppDeadCore;

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 1: SCENE HARVESTING & GEOMETRY EXTRACTION PIPELINE
    // =========================================================================

    public static class SceneHarvestingService
    {
        public static Light NativeSceneSun = null;

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

            var scene = SceneManager.GetActiveScene();
            if (scene.isLoaded)
            {
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
            Mesh m = new Mesh();
            m.name = $"Laser_DoubleSided_{width}x{height}_Mesh";

            float hw = width * 0.5f;
            float hh = height * 0.5f;
            float zOffset = 0.01f;

            Vector3[] vertices = new Vector3[]
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

            Vector2[] uvs = new Vector2[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1)
            };

            Vector3[] normals = new Vector3[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward
            };

            int[] triangles = new int[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7
            };

            m.vertices = vertices;
            m.uv = uvs;
            m.normals = normals;
            m.triangles = triangles;
            m.RecalculateBounds();

            _proceduralMeshes.Add(m);
            return m;
        }

        public static void EnableGPUInstancingOnMaterial(Material mat)
        {
            if (mat == null) return;
            try
            {
                mat.enableInstancing = true;
            }
            catch { }
        }

        // =========================================================================
        // CORE HARVESTER PIPELINE
        // =========================================================================

        public static void HarvestAllSceneModels()
        {
            EditorSessionManager.AllAssets.Clear();
            CleanupProceduralResources();

            // Vault container surviving scene unloads
            if (_harvesterVault == null)
            {
                _harvesterVault = new GameObject("Studio_Harvester_Vault");
                GameObject.DontDestroyOnLoad(_harvesterVault);
                _harvesterVault.SetActive(false);
            }

            HashSet<string> seenGeometrySignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<int> seenMeshInstanceIDs = new HashSet<int>();
            HashSet<Material> seenMaterials = new HashSet<Material>();
            Dictionary<int, Material[]> meshToOriginalMaterials = new Dictionary<int, Material[]>();
            Dictionary<string, int> displayNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // 1. CACHE ALL MATERIALS & MAP MESH->MATERIALS (PRESERVES TEXTURES & SHADERS)
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

            // 2. HARVEST NATIVE GAMEPLAY ENTITIES
            HarvestNativeGameplayEntities();

            // 3. LIGHTING & PROCEDURAL HAZARDS
            HarvestProceduralHazardsAndLights();

            // 4. ARCHITECTURE PASS A: CURRENT ACTIVE SCENE
            Scene activeScene = SceneManager.GetActiveScene();
            string activeSceneName = activeScene.name;
            HarvestArchitectureFromScene(activeScene, seenGeometrySignatures, seenMeshInstanceIDs, displayNameCounts, isCurrentScene: true);

            // 5. CACHE NATIVE SUN BEFORE MULTI-SCENE ADDITIVE LOADING
            DebugDumpSceneLighting();
            Light primarySun = NativeSceneSun;

            // 6. ARCHITECTURE PASS B: HARVEST ALL SCENES WITH "LEVEL" IN NAME
            HashSet<string> targetedScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int buildCount = SceneManager.sceneCountInBuildSettings;
            for (int b = 0; b < buildCount; b++)
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(b);
                string sName = Path.GetFileNameWithoutExtension(scenePath);
                string sLow = sName.ToLower();

                if (sLow.Contains("level") && !sLow.Contains("menu") && !sLow.Contains("boot") &&
                    !sLow.Contains("title") && !sLow.Contains("load") && !sLow.Contains("transition"))
                {
                    targetedScenes.Add(sName);
                }
            }

            if (MapBrowserService.AvailableStagingScenes != null)
            {
                for (int s = 0; s < MapBrowserService.AvailableStagingScenes.Count; s++)
                {
                    string sName = MapBrowserService.AvailableStagingScenes[s];
                    if (!string.IsNullOrWhiteSpace(sName) && sName.ToLower().Contains("level"))
                    {
                        targetedScenes.Add(sName);
                    }
                }
            }

            foreach (string sceneName in targetedScenes)
            {
                if (sceneName.Equals(activeSceneName, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
                    Scene targetScene = SceneManager.GetSceneByName(sceneName);

                    if (!targetScene.isLoaded)
                        targetScene = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);

                    if (targetScene.isLoaded)
                    {
                        HarvestArchitectureFromScene(targetScene, seenGeometrySignatures, seenMeshInstanceIDs, displayNameCounts, isCurrentScene: false);
                        SceneManager.UnloadSceneAsync(targetScene);
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Harvest] Additive harvest failed for scene '{sceneName}': {ex.Message}");
                }
            }

            // 7. RESTORE PRIMARY SUN (Stops additive scene unloads from wiping sunlight)
            if (primarySun != null && primarySun.gameObject.activeInHierarchy)
            {
                NativeSceneSun = primarySun;
                RenderSettings.sun = primarySun;
                primarySun.enabled = true;
            }
            else
            {
                DebugDumpSceneLighting();
            }

            // 8. ARCHITECTURE PASS C: IN-MEMORY RESIDUAL MESH SWEEP
            HarvestResidualMeshesFromMemory(seenGeometrySignatures, seenMeshInstanceIDs, displayNameCounts, meshToOriginalMaterials);

            // 9. FINAL DEDUPLICATION PASS (Guarantees no duplicates slip through)
            PurgeCatalogDuplicates();

            MelonLogger.Msg($">> [Harvest] Harvested {EditorSessionManager.AllAssets.Count} unique architecture & gameplay assets (2.0m - 50.0m, duplicates purged)!");
        }

        // =========================================================================
        // SCENE ARCHITECTURE HARVESTER
        // =========================================================================

        private static void HarvestArchitectureFromScene(Scene scene, HashSet<string> seenSignatures, HashSet<int> seenMeshIDs, Dictionary<string, int> displayNameCounts, bool isCurrentScene)
        {
            if (!scene.isLoaded) return;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                if (roots[r] == null) continue;
                MeshFilter[] childFilters = roots[r].GetComponentsInChildren<MeshFilter>(true);

                for (int c = 0; c < childFilters.Length; c++)
                {
                    MeshFilter mf = childFilters[c];
                    if (mf == null || mf.sharedMesh == null || mf.gameObject == null) continue;

                    Mesh mesh = mf.sharedMesh;
                    int instanceID = mesh.GetInstanceID();

                    // Skip gameplay entities
                    if (mf.GetComponent<Jumper>() != null || mf.GetComponent<TurretScript>() != null ||
                        mf.GetComponent<LaserScript>() != null || mf.GetComponent<Helix>() != null ||
                        mf.GetComponent<CheckPointScript>() != null)
                    {
                        continue;
                    }

                    string mName = mesh.name.Trim();
                    string goName = mf.gameObject.name.Trim();
                    string mLow = mName.ToLower();
                    string gLow = goName.ToLower();

                    // Filter combined / static batched meshes
                    if (IsCombinedOrBatchedMesh(mLow, gLow)) continue;

                    // Filter impostors, billboards and flat cards
                    if (IsImpostorOrArtifact(mesh, mLow, gLow)) continue;

                    // Filter editor widgets and proxy colliders
                    if (gLow.Contains("gizmo") || gLow.Contains("proxy") || gLow.Contains("wireframe") ||
                        gLow.Contains("highlight") || gLow.Contains("beacon"))
                    {
                        continue;
                    }

                    // Filter skybox, clouds, fog
                    if (mLow.Contains("skybox") || mLow.Contains("horizon") || mLow.Contains("fog") ||
                        mLow.Contains("dome") || mLow.Contains("cloud") || mLow.Contains("backdrop") ||
                        mLow.Contains("ambiance") || mLow.Contains("dust"))
                    {
                        continue;
                    }

                    if (mesh.vertexCount < 4) continue;

                    // STRICT 2.0m - 50.0m SIZE CHECK
                    Vector3 boundsSize = mesh.bounds.size;
                    float maxDim = Mathf.Max(boundsSize.x, Mathf.Max(boundsSize.y, boundsSize.z));
                    if (maxDim < 2.0f || maxDim > 50.0f) continue;

                    // Deduplication check
                    string sig = GetGeometrySignature(mesh, goName);
                    if (!seenSignatures.Add(sig) || !seenMeshIDs.Add(instanceID))
                    {
                        continue;
                    }

                    GameObject templateObj;
                    if (isCurrentScene)
                    {
                        templateObj = mf.gameObject;
                    }
                    else
                    {
                        templateObj = CreatePersistentDeepClone(mf.gameObject, $"Template_Level_{goName}");
                        _proceduralTemplates.Add(templateObj);
                    }

                    RegisterModelAsset(templateObj, mesh, goName, mName, displayNameCounts);
                }
            }
        }

        // =========================================================================
        // RESIDUAL MEMORY SWEEP
        // =========================================================================

        private static void HarvestResidualMeshesFromMemory(HashSet<string> seenSignatures, HashSet<int> seenMeshIDs, Dictionary<string, int> displayNameCounts, Dictionary<int, Material[]> meshToOriginalMaterials)
        {
            Mesh[] allLoadedMeshes = Resources.FindObjectsOfTypeAll<Mesh>();
            for (int m = 0; m < allLoadedMeshes.Length; m++)
            {
                Mesh mesh = allLoadedMeshes[m];
                if (mesh == null) continue;

                int instanceID = mesh.GetInstanceID();
                if (seenMeshIDs.Contains(instanceID)) continue;

                string mName = mesh.name.Trim();
                string mLow = mName.ToLower();

                if (IsCombinedOrBatchedMesh(mLow, "") || IsImpostorOrArtifact(mesh, mLow, ""))
                    continue;

                if (string.IsNullOrWhiteSpace(mName) || mLow.Contains("font") || mLow.Contains("text") ||
                    mLow.Contains("ui-") || mLow.Contains("tmp") || mLow.Contains("cursor") ||
                    mLow.Contains("sprite") || mLow.Contains("sky") || mLow.Contains("cloud") ||
                    mLow.Contains("fog") || mLow.Contains("dome") || mLow.Contains("horizon") ||
                    mLow.Contains("backdrop") || mesh.vertexCount < 4)
                {
                    continue;
                }

                // STRICT 2.0m - 50.0m SIZE CHECK
                Vector3 bSize = mesh.bounds.size;
                float maxDim = Mathf.Max(bSize.x, Mathf.Max(bSize.y, bSize.z));
                if (maxDim < 2.0f || maxDim > 50.0f) continue;

                string sig = GetGeometrySignature(mesh, mName);
                if (!seenSignatures.Add(sig) || !seenMeshIDs.Add(instanceID))
                    continue;

                GameObject templateGo = new GameObject($"Template_RAM_{mName}");
                templateGo.transform.SetParent(_harvesterVault.transform, false);
                templateGo.transform.position = new Vector3(8500f, 8500f, 8500f);
                templateGo.SetActive(false);

                MeshFilter nmf = templateGo.AddComponent<MeshFilter>();
                nmf.sharedMesh = mesh;

                MeshRenderer nmr = templateGo.AddComponent<MeshRenderer>();
                if (meshToOriginalMaterials.TryGetValue(instanceID, out Material[] origMats) && origMats != null && origMats.Length > 0)
                {
                    nmr.sharedMaterials = origMats;
                    for (int o = 0; o < origMats.Length; o++) EnableGPUInstancingOnMaterial(origMats[o]);
                }
                else
                {
                    nmr.sharedMaterial = EditorSessionManager.CachedSceneMaterial;
                }

                _proceduralTemplates.Add(templateGo);
                RegisterModelAsset(templateGo, mesh, mName, mName, displayNameCounts);
            }
        }

        // =========================================================================
        // PERSISTENT DEEP CLONE (PRESERVES SHADERS & MULTI-MATERIALS)
        // =========================================================================

        private static GameObject CreatePersistentDeepClone(GameObject sourceGo, string templateName)
        {
            if (sourceGo == null) return null;

            GameObject clone = GameObject.Instantiate(sourceGo);
            clone.name = templateName;
            clone.transform.SetParent(_harvesterVault.transform, false);
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;

            Component[] comps = clone.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < comps.Length; i++)
            {
                Component c = comps[i];
                if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || c is Renderer) continue;
                if (c is Collider || c is MonoBehaviour || c is Animator || c is Animation || c is Rigidbody)
                {
                    GameObject.DestroyImmediate(c);
                }
            }

            Renderer[] rends = clone.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < rends.Length; r++)
            {
                if (rends[r] == null) continue;
                Material[] mats = rends[r].sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] != null) EnableGPUInstancingOnMaterial(mats[m]);
                }
            }

            clone.SetActive(false);
            return clone;
        }

        // =========================================================================
        // DEDUPLICATION & SIGNATURE GENERATOR
        // =========================================================================

        private static string GetGeometrySignature(Mesh mesh, string baseName)
        {
            if (mesh == null) return string.Empty;
            Vector3 s = mesh.bounds.size;
            // Combines vertex count, submesh count, and 2-decimal rounded 3D dimensions
            return $"{mesh.vertexCount}_{mesh.subMeshCount}_{s.x:F2}x{s.y:F2}x{s.z:F2}";
        }

        private static void PurgeCatalogDuplicates()
        {
            HashSet<string> uniqueSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = EditorSessionManager.AllAssets.Count - 1; i >= 0; i--)
            {
                CatalogAsset asset = EditorSessionManager.AllAssets[i];

                // NEVER PURGE LIGHTS, PREFABS, OR GAMEPLAY ENTITIES!
                if (asset == null || asset.IsPrefabInstance || asset.IsJumper || asset.IsCheckPoint ||
                    asset.IsSpawnGate || asset.IsGoalGate || asset.IsTurret || asset.IsHelix || asset.IsLaser ||
                    asset.IsSpotlight || asset.IsSunlight)
                {
                    continue;
                }

                if (asset.FilterMesh != null)
                {
                    string sig = GetGeometrySignature(asset.FilterMesh, asset.DisplayName);
                    if (!uniqueSignatures.Add(sig))
                    {
                        EditorSessionManager.AllAssets.RemoveAt(i);
                    }
                }
            }
        }

        private static bool IsCombinedOrBatchedMesh(string mLow, string gLow)
        {
            return mLow.Contains("combine") || gLow.Contains("combine") ||
                   mLow.StartsWith("combined mesh") || gLow.StartsWith("combined mesh") ||
                   mLow.Contains("batch") || gLow.Contains("batch");
        }

        private static bool IsImpostorOrArtifact(Mesh mesh, string mLow, string gLow)
        {
            if (mLow.Contains("impostor") || mLow.Contains("imposter") ||
                gLow.Contains("impostor") || gLow.Contains("imposter") ||
                mLow.Contains("billboard") || gLow.Contains("billboard") ||
                mLow.StartsWith("bb_") || mLow.EndsWith("_bb") ||
                gLow.StartsWith("bb_") || gLow.EndsWith("_bb") ||
                mLow.Contains("_card") || gLow.Contains("_card") ||
                mLow.Contains("lod1") || mLow.Contains("lod2") || mLow.Contains("lod3") ||
                mLow.Contains("shadow") || mLow.Contains("hole") ||
                mLow.Contains("collision") || mLow.Contains("collider") ||
                mLow.StartsWith("ucx_") || mLow.StartsWith("ubx_") || mLow.StartsWith("usp_"))
            {
                return true;
            }

            Vector3 size = mesh.bounds.size;
            float minDim = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

            if (minDim < 0.05f && maxDim >= 2.0f && mesh.vertexCount <= 12)
            {
                return true;
            }

            return false;
        }

        private static void RegisterModelAsset(GameObject templateGo, Mesh mesh, string goName, string mName, Dictionary<string, int> displayNameCounts)
        {
            Vector3 boundsSize = mesh.bounds.size;
            float maxDim = Mathf.Max(boundsSize.x, Mathf.Max(boundsSize.y, boundsSize.z));

            if (maxDim < 2.0f || maxDim > 50.0f) return;

            string gLow = goName.ToLower();
            string mLow = mName.ToLower();

            bool isPlatform = gLow.Contains("16x2x16") || mLow.Contains("16x2x16") ||
                              gLow.Contains("platform") || mLow.Contains("platform") ||
                              gLow.Contains("plateforme") || mLow.Contains("plateforme") ||
                              gLow.Contains("floor") || mLow.Contains("sol") || gLow.Contains("step");

            bool isWall = (boundsSize.y > boundsSize.z * 1.8f || boundsSize.y > boundsSize.x * 1.8f) &&
                          (boundsSize.x > 3f || boundsSize.z > 3f);

            bool isColumn = boundsSize.y > (Mathf.Max(boundsSize.x, boundsSize.z) * 2.2f);

            string subCat = "Architecture";
            if (isWall) subCat = "Walls";
            else if (isColumn) subCat = "Columns";
            else if (maxDim > 20.0f) subCat = "Structures";

            string baseName;
            if (gLow.Contains("16x2x16") || mLow.Contains("16x2x16"))
                baseName = "Floor Platform 16x16";
            else
            {
                baseName = !string.IsNullOrWhiteSpace(mName) && !mLow.StartsWith("mesh") && !mLow.StartsWith("polysurface")
                    ? mName.Replace("Mesh", "").Replace("_", " ").Trim()
                    : goName.Replace("_", " ").Trim();
            }

            if (baseName.StartsWith("SM ", StringComparison.OrdinalIgnoreCase)) baseName = baseName.Substring(3);
            if (baseName.StartsWith("m ", StringComparison.OrdinalIgnoreCase)) baseName = baseName.Substring(2);
            if (baseName.StartsWith("geo ", StringComparison.OrdinalIgnoreCase)) baseName = baseName.Substring(4);

            if (string.IsNullOrWhiteSpace(baseName) || baseName.Length < 2)
                baseName = isPlatform ? "Platform Slab" : (isWall ? "Modular Wall" : (isColumn ? "Pillar Column" : "Architecture Block"));

            string finalName = baseName;
            if (displayNameCounts.TryGetValue(baseName, out int count))
            {
                count++;
                displayNameCounts[baseName] = count;
                finalName = $"{baseName} ({count})";
            }
            else
            {
                displayNameCounts[baseName] = 1;
            }

            var asset = new CatalogAsset
            {
                DisplayName = finalName,
                SourceTemplate = templateGo,
                FilterMesh = mesh,
                Category = AssetCategory.Building,
                SubCategory = subCat,
                DefaultScale = isPlatform ? 0.55f : 1.0f,
                VerticalOffset = 0f,
                BaseRotation = (isPlatform && (finalName.Contains("16x16") || finalName.Contains("Platform")))
                    ? Quaternion.Euler(0f, 0f, 90f)
                    : Quaternion.identity
            };

            asset.ComputeSizeMetrics();
            EditorSessionManager.AllAssets.Add(asset);
        }

        // =========================================================================
        // NATIVE GAMEPLAY & HAZARDS (PRESERVED)
        // =========================================================================

        private static void HarvestNativeGameplayEntities()
        {
            try
            {
                EditorSessionManager.PrefabJumper = GameObject.FindObjectOfType<Jumper>();
                if (EditorSessionManager.PrefabJumper == null)
                {
                    Jumper[] allJumpers = Resources.FindObjectsOfTypeAll<Jumper>();
                    if (allJumpers != null && allJumpers.Length > 0) EditorSessionManager.PrefabJumper = allJumpers[0];
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
                    IsJumper = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = 0f,
                    BaseRotation = Quaternion.identity
                };
                jAsset.ComputeSizeMetrics();
                EditorSessionManager.AllAssets.Add(jAsset);
            }

            try
            {
                CheckPointScript nativeCp = GameObject.FindObjectOfType<CheckPointScript>();
                if (nativeCp == null)
                {
                    CheckPointScript[] allCps = Resources.FindObjectsOfTypeAll<CheckPointScript>();
                    if (allCps != null && allCps.Length > 0) nativeCp = allCps[0];
                }

                if (nativeCp != null)
                {
                    Transform rootT = nativeCp.transform;
                    while (rootT.parent != null && (
                        rootT.parent.name.ToLower().Contains("checkpoint") ||
                        rootT.parent.name.ToLower().Contains("gate") ||
                        rootT.parent.name.ToLower().Contains("porte") ||
                        rootT.name.ToLower().Contains("trigger") ||
                        rootT.name.ToLower().Contains("spawn")
                    ))
                    {
                        rootT = rootT.parent;
                    }

                    EditorSessionManager.PrefabCheckPoint = rootT.GetComponentInChildren<CheckPointScript>();

                    var cpAsset = new CatalogAsset
                    {
                        DisplayName = "Checkpoint Gate",
                        SourceTemplate = rootT.gameObject,
                        Category = AssetCategory.Gameplay,
                        SubCategory = "Gameplay",
                        IsCheckPoint = true,
                        DefaultScale = 1.0f,
                        VerticalOffset = 0f,
                        BaseRotation = Quaternion.identity
                    };
                    cpAsset.ComputeSizeMetrics();
                    EditorSessionManager.AllAssets.Add(cpAsset);

                    var spawnAsset = new CatalogAsset
                    {
                        DisplayName = "Entry Checkpoint (Start)",
                        SourceTemplate = rootT.gameObject,
                        Category = AssetCategory.Gameplay,
                        SubCategory = "Gameplay",
                        IsCheckPoint = true,
                        IsSpawnGate = true,
                        DefaultScale = 1.0f,
                        VerticalOffset = 0f,
                        BaseRotation = Quaternion.identity
                    };
                    spawnAsset.ComputeSizeMetrics();
                    EditorSessionManager.AllAssets.Add(spawnAsset);

                    var goalAsset = new CatalogAsset
                    {
                        DisplayName = "Goal Checkpoint (Finish)",
                        SourceTemplate = rootT.gameObject,
                        Category = AssetCategory.Gameplay,
                        SubCategory = "Gameplay",
                        IsCheckPoint = true,
                        IsGoalGate = true,
                        DefaultScale = 1.0f,
                        VerticalOffset = 0f,
                        BaseRotation = Quaternion.identity
                    };
                    goalAsset.ComputeSizeMetrics();
                    EditorSessionManager.AllAssets.Add(goalAsset);
                }
            }
            catch { }

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
                    IsTurret = true
                };
                turretAsset.ComputeSizeMetrics();
                EditorSessionManager.AllAssets.Add(turretAsset);
            }

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
                    IsHelix = true
                };
                helixAsset.ComputeSizeMetrics();
                EditorSessionManager.AllAssets.Add(helixAsset);
            }
        }

        private static void HarvestProceduralHazardsAndLights()
        {
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
                IsSpotlight = true,
                DefaultScale = 1.0f,
                VerticalOffset = 0f,
                BaseRotation = Quaternion.identity
            };
            spotAsset.ComputeSizeMetrics();
            EditorSessionManager.AllAssets.Add(spotAsset);

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
                IsSunlight = true,
                DefaultScale = 1.0f,
                VerticalOffset = 0f,
                BaseRotation = Quaternion.Euler(50f, -30f, 0f)
            };
            sunAsset.ComputeSizeMetrics();
            EditorSessionManager.AllAssets.Add(sunAsset);

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

            GameObject laserSmall = new GameObject("Template_Small_Laser");
            Mesh laserSmallMesh = CreateDoubleSidedPlaneMesh(4f, 3f);
            laserSmall.AddComponent<MeshFilter>().sharedMesh = laserSmallMesh;
            if (laserMat != null) laserSmall.AddComponent<MeshRenderer>().sharedMaterial = laserMat;
            var scCol = laserSmall.AddComponent<BoxCollider>();
            scCol.isTrigger = true; scCol.size = new Vector3(4f, 3f, 0.35f);
            laserSmall.AddComponent<LaserScript>();
            laserSmall.SetActive(false);

            var laserSmallAsset = new CatalogAsset
            {
                DisplayName = "Laser Barrier Compact (4m)",
                SourceTemplate = laserSmall,
                FilterMesh = laserSmallMesh,
                Category = AssetCategory.Gameplay,
                SubCategory = "Hazards",
                IsLaser = true
            };
            laserSmallAsset.ComputeSizeMetrics();
            EditorSessionManager.AllAssets.Add(laserSmallAsset);

            GameObject laserTemplate = new GameObject("Template_Laser_Barrier");
            Mesh laserMesh = CreateDoubleSidedPlaneMesh(8f, 4f);
            laserTemplate.AddComponent<MeshFilter>().sharedMesh = laserMesh;
            if (laserMat != null) laserTemplate.AddComponent<MeshRenderer>().sharedMaterial = laserMat;
            var lCol = laserTemplate.AddComponent<BoxCollider>();
            lCol.isTrigger = true; lCol.size = new Vector3(8f, 4f, 0.35f);
            laserTemplate.AddComponent<LaserScript>();
            laserTemplate.SetActive(false);

            var laserAsset = new CatalogAsset
            {
                DisplayName = "Laser Barrier Medium (8m)",
                SourceTemplate = laserTemplate,
                FilterMesh = laserMesh,
                Category = AssetCategory.Gameplay,
                SubCategory = "Hazards",
                IsLaser = true
            };
            laserAsset.ComputeSizeMetrics();
            EditorSessionManager.AllAssets.Add(laserAsset);

            GameObject longLaserTemplate = new GameObject("Template_Long_Laser_Barrier");
            Mesh longLaserMesh = CreateDoubleSidedPlaneMesh(22f, 4f);
            longLaserTemplate.AddComponent<MeshFilter>().sharedMesh = longLaserMesh;
            if (laserMat != null) longLaserTemplate.AddComponent<MeshRenderer>().sharedMaterial = laserMat;
            var llCol = longLaserTemplate.AddComponent<BoxCollider>();
            llCol.isTrigger = true; llCol.size = new Vector3(22f, 4f, 0.35f);
            longLaserTemplate.AddComponent<LaserScript>();
            longLaserTemplate.SetActive(false);

            var longLaserAsset = new CatalogAsset
            {
                DisplayName = "Laser Barrier Long (22m)",
                SourceTemplate = longLaserTemplate,
                FilterMesh = longLaserMesh,
                Category = AssetCategory.Gameplay,
                SubCategory = "Hazards",
                IsLaser = true
            };
            longLaserAsset.ComputeSizeMetrics();
            EditorSessionManager.AllAssets.Add(longLaserAsset);

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
                IsLaser = true,
                IsRotatingLaser = true
            };
            rotLaserAsset.ComputeSizeMetrics();
            EditorSessionManager.AllAssets.Add(rotLaserAsset);
        }

        // =========================================================================
        // SCENE LEVEL GEOMETRY HIDING (PRESERVES SKYBOX, CLOUDS & VAULT)
        // =========================================================================

        public static void HideVanillaLevelGeometry()
        {
            var activeScene = SceneManager.GetActiveScene();
            string sName = activeScene.name.ToLower();
            if (sName.Contains("menu")) return;

            // 1. Rescue player
            GameObject player = EditorSessionManager.FindPlayerEntity();
            if (player != null)
            {
                player.transform.SetParent(null, true);
                player.SetActive(true);
            }

            // 2. Rescue directional sun
            if (NativeSceneSun != null)
            {
                NativeSceneSun.transform.SetParent(null, true);
                NativeSceneSun.gameObject.SetActive(true);
                NativeSceneSun.enabled = true;
            }

            // 3. Rescue StartLevelManager
            StartLevelManager slm = GameObject.FindObjectOfType<StartLevelManager>();
            if (slm != null)
            {
                slm.transform.SetParent(null, true);
                slm.gameObject.SetActive(true);
            }

            // 4. Rescue EventSystem
            UnityEngine.EventSystems.EventSystem es = GameObject.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
            if (es != null)
            {
                es.transform.SetParent(null, true);
                es.gameObject.SetActive(true);
                es.enabled = true;
                var sim = es.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                if (sim != null) sim.enabled = true;
            }

            // 5. Hide vanilla level design roots, but KEEP _LA (Level Art: Skybox & Clouds)
            GameObject[] rootObjects = activeScene.GetRootGameObjects();
            for (int i = 0; i < rootObjects.Length; i++)
            {
                GameObject root = rootObjects[i];
                if (root == null) continue;
                if (IsProtected(root, player, slm != null ? slm.gameObject : null)) continue;

                string rLow = root.name.ToLower();

                if (rLow == "_ld" || rLow == "l_d")
                {
                    root.SetActive(false);
                    continue;
                }

                if (rLow == "_la" || rLow == "l_a")
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

            if (n.Contains("spark") && !n.Contains("sky") && !n.Contains("light"))
                return true;

            if (n.Contains("zone") && !n.Contains("sky") && !n.Contains("cloud") && !n.Contains("fog") && !n.Contains("volume") && !n.Contains("atmosphere"))
                return true;

            if (n.Contains("trigger") && (n.Contains("kill") || n.Contains("death") || n.Contains("fall") || n.Contains("void") || n.Contains("zone") || n.Contains("respawn") || n.Contains("checkpoint")))
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

            // Protect our persistent Harvester Vault
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
    // SECTION 2: HARMONY RUNTIME ENGINE PATCHES
    // =========================================================================

    [HarmonyPatch(typeof(StartLevelManager), nameof(StartLevelManager.StartLevelSequence))]
    public static class StartLevelPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            string currentScene = SceneManager.GetActiveScene().name.ToLower();
            if (currentScene.Contains("menu")) return;

            if (EditorSessionManager.IsCustomSessionActive && !EditorSessionManager.IsLevelInitialized)
            {
                EditorSessionManager.InitializeCustomLevel();
            }
        }
    }

    // =========================================================================
    // PREVENTS TURRET BULLETS FROM GETTING STUCK ON TURRET COLLIDERS
    // =========================================================================
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