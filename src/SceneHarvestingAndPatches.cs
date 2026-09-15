using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using SceneManager = UnityEngine.SceneManagement.SceneManager;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppDeadCore;

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 1: SCENE HARVESTING & GEOMETRY EXTRACTION PIPELINE
    // =========================================================================

    /// <summary>
    /// Ingests scene geometry, extracts gameplay prefabs (jumpers, turbines, turrets),
    /// generates procedural hazards/lighting templates, and filters architectural building blocks.
    /// </summary>
    public static class SceneHarvestingService
    {
        public static Light NativeSceneSun = null;

        private static readonly List<Mesh> _proceduralMeshes = new List<Mesh>();
        private static readonly List<Material> _proceduralMaterials = new List<Material>();

        /// <summary>
        /// Cleans up dynamically generated procedural meshes and materials to prevent memory leaks.
        /// </summary>
        public static void CleanupProceduralResources()
        {
            for (int i = 0; i < _proceduralMeshes.Count; i++)
            {
                if (_proceduralMeshes[i] != null)
                {
                    GameObject.Destroy(_proceduralMeshes[i]);
                }
            }
            _proceduralMeshes.Clear();

            for (int i = 0; i < _proceduralMaterials.Count; i++)
            {
                if (_proceduralMaterials[i] != null)
                {
                    GameObject.Destroy(_proceduralMaterials[i]);
                }
            }
            _proceduralMaterials.Clear();
        }

        /// <summary>
        /// Scans active scene lights to hook into the primary directional sun for celestial atmosphere tuning.
        /// </summary>
        public static void DebugDumpSceneLighting()
        {
            NativeSceneSun = null;

            if (RenderSettings.sun != null && RenderSettings.sun.gameObject.scene.isLoaded)
            {
                NativeSceneSun = RenderSettings.sun;
            }

            Light[] allLights = Resources.FindObjectsOfTypeAll<Light>();
            for (int i = 0; i < allLights.Length; i++)
            {
                Light l = allLights[i];
                if (l == null) continue;

                bool isSceneObject = l.gameObject.scene.isLoaded;
                if (NativeSceneSun == null && isSceneObject && l.type == LightType.Directional)
                {
                    if (!l.name.Contains("Template") && !l.name.StartsWith("Custom_"))
                    {
                        NativeSceneSun = l;
                    }
                }
            }
        }

        /// <summary>
        /// Generates an 8-vertex double-sided plane mesh for laser barrier hazards.
        /// Prevents backface culling issues when viewing beams from opposite directions.
        /// </summary>
        public static Mesh CreateDoubleSidedPlaneMesh(float width, float height)
        {
            Mesh m = new Mesh();
            m.name = $"Laser_DoubleSided_{width}x{height}_Mesh";

            float hw = width * 0.5f;
            float hh = height * 0.5f;
            float zOffset = 0.01f;

            Vector3[] vertices = new Vector3[]
            {
                // Front face
                new Vector3(-hw, -hh, zOffset),
                new Vector3(hw, -hh, zOffset),
                new Vector3(hw, hh, zOffset),
                new Vector3(-hw, hh, zOffset),
                // Back face
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
                // Front winding
                0, 2, 1, 0, 3, 2,
                // Back winding
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

        /// <summary>
        /// Complete asset harvesting pipeline. Gathers scene templates, constructs procedural
        /// hazards/lighting, and applies intelligent bounding box filters to scene meshes.
        /// </summary>
        public static void HarvestAllSceneModels()
        {
            EditorSessionManager.AllAssets.Clear();
            HashSet<string> seenMeshes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Cache standard level surface material for procedural props
            MeshRenderer[] renderers = GameObject.FindObjectsOfType<MeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer r = renderers[i];
                if (r != null && r.sharedMaterial != null && !r.sharedMaterial.name.ToLower().Contains("laser"))
                {
                    EditorSessionManager.CachedSceneMaterial = r.sharedMaterial;
                    break;
                }
            }

            GameObject ld = GameObject.Find("_LD") ?? GameObject.Find("L_D") ?? GameObject.Find("l_d");

            // -----------------------------------------------------------------
            // 1. GAMEPLAY: JUMP PADS (LAUNCHERS)
            // -----------------------------------------------------------------
            try
            {
                EditorSessionManager.PrefabJumper = GameObject.FindObjectOfType<Jumper>();
                if (ld != null && EditorSessionManager.PrefabJumper == null)
                {
                    Jumper[] jumpers = ld.GetComponentsInChildren<Jumper>(true);
                    if (jumpers.Length > 0) EditorSessionManager.PrefabJumper = jumpers[0];
                }
            }
            catch { }

            if (EditorSessionManager.PrefabJumper != null)
            {
                EditorSessionManager.AllAssets.Add(new CatalogAsset
                {
                    DisplayName = "Launch Jumper Pad",
                    SourceTemplate = EditorSessionManager.PrefabJumper.gameObject,
                    Category = AssetCategory.Gameplay,
                    SubCategory = "Platforms",
                    IsJumper = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = 0f,
                    BaseRotation = Quaternion.identity
                });
            }

            // -----------------------------------------------------------------
            // 2. GAMEPLAY: GATES & CHECKPOINTS
            // -----------------------------------------------------------------
            try
            {
                EditorSessionManager.PrefabCheckPoint = GameObject.FindObjectOfType<CheckPointScript>();
                if (ld != null && EditorSessionManager.PrefabCheckPoint == null)
                {
                    CheckPointScript[] cps = ld.GetComponentsInChildren<CheckPointScript>(true);
                    if (cps.Length > 0) EditorSessionManager.PrefabCheckPoint = cps[0];
                }
            }
            catch { }

            if (EditorSessionManager.PrefabCheckPoint != null)
            {
                EditorSessionManager.AllAssets.Add(new CatalogAsset
                {
                    DisplayName = "Checkpoint Gate",
                    SourceTemplate = EditorSessionManager.PrefabCheckPoint.gameObject,
                    Category = AssetCategory.Gameplay,
                    SubCategory = "Platforms",
                    IsCheckPoint = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = -0.32f,
                    BaseRotation = Quaternion.identity
                });

                EditorSessionManager.AllAssets.Add(new CatalogAsset
                {
                    DisplayName = "Entry Checkpoint (Start)",
                    SourceTemplate = EditorSessionManager.PrefabCheckPoint.gameObject,
                    Category = AssetCategory.Gameplay,
                    SubCategory = "Platforms",
                    IsCheckPoint = true,
                    IsSpawnGate = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = -0.32f,
                    BaseRotation = Quaternion.identity
                });

                EditorSessionManager.AllAssets.Add(new CatalogAsset
                {
                    DisplayName = "Goal Checkpoint (Finish)",
                    SourceTemplate = EditorSessionManager.PrefabCheckPoint.gameObject,
                    Category = AssetCategory.Gameplay,
                    SubCategory = "Platforms",
                    IsCheckPoint = true,
                    IsGoalGate = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = -0.32f,
                    BaseRotation = Quaternion.identity
                });
            }

            // -----------------------------------------------------------------
            // 3. LIGHTING: TECH SPOTLIGHT (SUBCATEGORY: "LIGHTING")
            // -----------------------------------------------------------------
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

            EditorSessionManager.AllAssets.Add(new CatalogAsset
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
            });

            // -----------------------------------------------------------------
            // 4. LIGHTING: GLOBAL SUNLIGHT (SUBCATEGORY: "LIGHTING")
            // -----------------------------------------------------------------
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

            GameObject sunRay = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            sunRay.name = "Light_Lens";
            sunRay.transform.SetParent(sunTemplate.transform, false);
            sunRay.transform.localScale = new Vector3(0.25f, 1.4f, 0.25f);
            sunRay.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            sunRay.transform.localPosition = new Vector3(0f, 0f, 1.2f);
            Collider rayCol = sunRay.GetComponent<Collider>();
            if (rayCol != null) GameObject.DestroyImmediate(rayCol);

            Shader unlitShader = Shader.Find("Unlit/Color") ?? Shader.Find("Particles/Standard Unlit");
            if (unlitShader != null)
            {
                Material sunMat = new Material(unlitShader);
                sunMat.color = new Color(1f, 0.88f, 0.35f, 1f);
                _proceduralMaterials.Add(sunMat);
                sunOrb.GetComponent<Renderer>().material = sunMat;
                sunRay.GetComponent<Renderer>().material = sunMat;
            }

            sunTemplate.SetActive(false);

            EditorSessionManager.AllAssets.Add(new CatalogAsset
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
            });

            // -----------------------------------------------------------------
            // 5. HAZARDS: LASER BARRIERS (SUBCATEGORY: "HAZARDS")
            // -----------------------------------------------------------------
            Material laserMat = null;
            LaserManager lm = GameObject.FindObjectOfType<LaserManager>();
            if (lm != null && lm._sharedMaterial != null)
            {
                laserMat = lm._sharedMaterial;
            }

            if (laserMat == null)
            {
                Shader s = Shader.Find("Unlit/Color") ?? Shader.Find("Particles/Standard Unlit");
                if (s != null)
                {
                    laserMat = new Material(s);
                    laserMat.color = new Color(1f, 0.05f, 0.05f, 0.9f);
                    _proceduralMaterials.Add(laserMat);
                }
            }

            // Static Barrier 8m x 4m
            GameObject laserTemplate = new GameObject("Template_Laser_Barrier");
            Mesh laserMesh = CreateDoubleSidedPlaneMesh(8f, 4f);
            MeshFilter laserMf = laserTemplate.AddComponent<MeshFilter>();
            laserMf.sharedMesh = laserMesh;
            MeshRenderer laserMr = laserTemplate.AddComponent<MeshRenderer>();
            if (laserMat != null) laserMr.sharedMaterial = laserMat;
            BoxCollider laserCol = laserTemplate.AddComponent<BoxCollider>();
            laserCol.isTrigger = true;
            laserCol.size = new Vector3(8f, 4f, 0.35f);
            laserTemplate.AddComponent<LaserScript>();
            laserTemplate.SetActive(false);

            EditorSessionManager.AllAssets.Add(new CatalogAsset
            {
                DisplayName = "Laser Barrier",
                SourceTemplate = laserTemplate,
                FilterMesh = laserMesh,
                Category = AssetCategory.Gameplay,
                SubCategory = "Hazards",
                IsLaser = true,
                DefaultScale = 1.0f,
                VerticalOffset = 0f,
                BaseRotation = Quaternion.identity
            });

            // Long Barrier 22m x 4m
            GameObject longLaserTemplate = new GameObject("Template_Long_Laser_Barrier");
            Mesh longLaserMesh = CreateDoubleSidedPlaneMesh(22f, 4f);
            MeshFilter longMf = longLaserTemplate.AddComponent<MeshFilter>();
            longMf.sharedMesh = longLaserMesh;
            MeshRenderer longMr = longLaserTemplate.AddComponent<MeshRenderer>();
            if (laserMat != null) longMr.sharedMaterial = laserMat;
            BoxCollider longCol = longLaserTemplate.AddComponent<BoxCollider>();
            longCol.isTrigger = true;
            longCol.size = new Vector3(22f, 4f, 0.35f);
            longLaserTemplate.AddComponent<LaserScript>();
            longLaserTemplate.SetActive(false);

            EditorSessionManager.AllAssets.Add(new CatalogAsset
            {
                DisplayName = "Long Laser Barrier",
                SourceTemplate = longLaserTemplate,
                FilterMesh = longLaserMesh,
                Category = AssetCategory.Gameplay,
                SubCategory = "Hazards",
                IsLaser = true,
                DefaultScale = 1.0f,
                VerticalOffset = 0f,
                BaseRotation = Quaternion.identity
            });

            // Rotating Dual Laser 18m
            GameObject rotLaserRoot = new GameObject("Template_Rotating_Laser");
            GameObject centerHub = GameObject.CreatePrimitive(PrimitiveType.Cube);
            centerHub.name = "CenterHub";
            centerHub.transform.SetParent(rotLaserRoot.transform, false);
            centerHub.transform.localScale = new Vector3(1.4f, 2.8f, 1.4f);
            Collider hubCol = centerHub.GetComponent<Collider>();
            if (hubCol != null) GameObject.DestroyImmediate(hubCol);

            if (EditorSessionManager.CachedSceneMaterial != null)
            {
                MeshRenderer hr = centerHub.GetComponent<MeshRenderer>();
                if (hr != null) hr.material = EditorSessionManager.CachedSceneMaterial;
            }

            GameObject rotBeamObj = new GameObject("Beam");
            rotBeamObj.transform.SetParent(rotLaserRoot.transform, false);
            Mesh rotBeamMesh = CreateDoubleSidedPlaneMesh(18f, 2.2f);
            MeshFilter rmf = rotBeamObj.AddComponent<MeshFilter>();
            rmf.sharedMesh = rotBeamMesh;
            MeshRenderer rmr = rotBeamObj.AddComponent<MeshRenderer>();
            if (laserMat != null) rmr.sharedMaterial = laserMat;
            BoxCollider rbc = rotBeamObj.AddComponent<BoxCollider>();
            rbc.isTrigger = true;
            rbc.size = new Vector3(18f, 2.2f, 0.35f);
            rotBeamObj.AddComponent<LaserScript>();
            rotLaserRoot.SetActive(false);

            EditorSessionManager.AllAssets.Add(new CatalogAsset
            {
                DisplayName = "Rotating Laser Barrier",
                SourceTemplate = rotLaserRoot,
                FilterMesh = rotBeamMesh,
                Category = AssetCategory.Gameplay,
                SubCategory = "Hazards",
                IsLaser = true,
                IsRotatingLaser = true,
                DefaultScale = 1.0f,
                VerticalOffset = 0f,
                BaseRotation = Quaternion.identity
            });

            // -----------------------------------------------------------------
            // 6. HAZARDS: TURRETS & ENEMIES (NATIVE SCAN + ROBUST PROCEDURAL FALLBACK)
            // -----------------------------------------------------------------
            try
            {
                TurretScript nativeTurret = GameObject.FindObjectOfType<TurretScript>();
                if (nativeTurret == null)
                {
                    TurretScript[] allTurrets = Resources.FindObjectsOfTypeAll<TurretScript>();
                    for (int t = 0; t < allTurrets.Length; t++)
                    {
                        if (allTurrets[t] != null && allTurrets[t].gameObject != null)
                        {
                            nativeTurret = allTurrets[t];
                            break;
                        }
                    }
                }

                if (nativeTurret != null)
                {
                    Transform rootT = nativeTurret.transform;
                    while (rootT.parent != null && (rootT.parent.name.ToLower().Contains("tourelle") || rootT.parent.name.ToLower().Contains("turret")))
                    {
                        rootT = rootT.parent;
                    }
                    EditorSessionManager.PrefabTurret = rootT.gameObject;
                }
            }
            catch { }

            // Procedural fallback if the vanilla level environment doesn't contain a turret
            if (EditorSessionManager.PrefabTurret == null)
            {
                GameObject procTurret = new GameObject("Template_Defense_Turret");

                // Pedestal
                GameObject tBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tBase.name = "Base";
                tBase.transform.SetParent(procTurret.transform, false);
                tBase.transform.localScale = new Vector3(1.6f, 0.4f, 1.6f);

                // Turret Head
                GameObject tHead = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                tHead.name = "Head";
                tHead.transform.SetParent(procTurret.transform, false);
                tHead.transform.localPosition = new Vector3(0f, 0.85f, 0f);
                tHead.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);

                // Barrel
                GameObject tBarrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tBarrel.name = "Barrel";
                tBarrel.transform.SetParent(tHead.transform, false);
                tBarrel.transform.localPosition = new Vector3(0f, 0f, 0.85f);
                tBarrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                tBarrel.transform.localScale = new Vector3(0.3f, 0.75f, 0.3f);

                if (EditorSessionManager.CachedSceneMaterial != null)
                {
                    tBase.GetComponent<Renderer>().material = EditorSessionManager.CachedSceneMaterial;
                    tHead.GetComponent<Renderer>().material = EditorSessionManager.CachedSceneMaterial;
                    tBarrel.GetComponent<Renderer>().material = EditorSessionManager.CachedSceneMaterial;
                }

                try
                {
                    TurretScript ts = procTurret.AddComponent<TurretScript>();
                    ts._fireDelay = 1.0f;
                    ts._firePower = 1500f;
                }
                catch { }

                BoxCollider bc = procTurret.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, 0.85f, 0f);
                bc.size = new Vector3(1.8f, 1.8f, 2.0f);

                procTurret.SetActive(false);
                EditorSessionManager.PrefabTurret = procTurret;
            }

            if (EditorSessionManager.PrefabTurret != null)
            {
                EditorSessionManager.AllAssets.Add(new CatalogAsset
                {
                    DisplayName = "Defense Turret Enemy",
                    SourceTemplate = EditorSessionManager.PrefabTurret,
                    Category = AssetCategory.Gameplay,
                    SubCategory = "Hazards",
                    IsTurret = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = 0f,
                    BaseRotation = Quaternion.identity
                });
            }

            // -----------------------------------------------------------------
            // 7. HAZARDS: HELIX TURBINES & WIND FANS
            // -----------------------------------------------------------------
            try
            {
                Helix nativeHelix = GameObject.FindObjectOfType<Helix>();
                if (ld != null && nativeHelix == null)
                {
                    Helix[] hels = ld.GetComponentsInChildren<Helix>(true);
                    if (hels.Length > 0) nativeHelix = hels[0];
                }

                if (nativeHelix != null)
                {
                    EditorSessionManager.PrefabHelix = nativeHelix.gameObject;
                }
            }
            catch { }

            if (EditorSessionManager.PrefabHelix != null)
            {
                EditorSessionManager.AllAssets.Add(new CatalogAsset
                {
                    DisplayName = "Helix Turbine Fan",
                    SourceTemplate = EditorSessionManager.PrefabHelix,
                    Category = AssetCategory.Gameplay,
                    SubCategory = "Hazards",
                    IsHelix = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = 0f,
                    BaseRotation = Quaternion.identity
                });
            }

            // -----------------------------------------------------------------
            // 8. INTELLIGENT BOUNDS FILTERING & ARCHITECTURAL HARVESTING
            // -----------------------------------------------------------------
            MeshFilter[] allFilters = GameObject.FindObjectsOfType<MeshFilter>();
            for (int f = 0; f < allFilters.Length; f++)
            {
                MeshFilter mf = allFilters[f];
                if (mf == null || mf.sharedMesh == null) continue;

                string mName = mf.sharedMesh.name.Trim();
                string goName = mf.gameObject.name.ToLower();
                string mLow = mName.ToLower();

                // Exclude active game scripts and interactive components
                if (goName.Contains("helice") || goName.Contains("tourelle") || goName.Contains("laser")) continue;

                Vector3 size = mf.sharedMesh.bounds.size;
                float maxDim = Mathf.Max(size.x, size.y, size.z);

                // Filter out massive skyboxes, background mountain backdrops, and cloud domes
                if (maxDim > 60f) continue;

                // Filter out micro-debris, bolts, nuts, screws, and tiny collision shells
                if (maxDim < 0.8f || mf.sharedMesh.vertexCount < 12) continue;

                // String exclusions for visual noise and LOD proxies
                if (mLow.Contains("skybox") || mLow.Contains("horizon") || mLow.Contains("fog") ||
                    mLow.Contains("dome") || mLow.Contains("cloud") || mLow.Contains("backdrop")) continue;
                if (mLow.Contains("impostor") || mLow.Contains("lod1") || mLow.Contains("lod2") ||
                    mLow.Contains("lod3") || mLow.Contains("shadow") || mLow.Contains("hole")) continue;

                if (!seenMeshes.Contains(mName))
                {
                    seenMeshes.Add(mName);

                    string friendly = (goName.Contains("16x2x16") || mLow.Contains("16x2x16"))
                        ? "Floor Platform 16x16"
                        : mName.Replace("Mesh", "").Replace("_", " ").Trim();

                    bool isPlat = friendly.ToLower().Contains("platform") || friendly.ToLower().Contains("floor") ||
                                  mLow.Contains("plateforme") || mLow.Contains("16x2x16") || mLow.Contains("step");

                    EditorSessionManager.AllAssets.Add(new CatalogAsset
                    {
                        DisplayName = friendly,
                        SourceTemplate = mf.gameObject,
                        FilterMesh = mf.sharedMesh,
                        Category = AssetCategory.Building,
                        SubCategory = isPlat ? "Platforms" : "Architecture",
                        DefaultScale = isPlat ? 0.55f : 1.0f,
                        VerticalOffset = 0f,
                        BaseRotation = (isPlat && (friendly.Contains("16x16") || friendly.Contains("Platform")))
                            ? Quaternion.Euler(0f, 0f, 90f)
                            : Quaternion.identity
                    });
                }
            }

            MelonLogger.Msg($">> [Harvest] Catalog assembled: {EditorSessionManager.AllAssets.Count} verified building & gameplay entities.");
        }

        /// <summary>
        /// Deactivates vanilla staging environment root containers while keeping the native
        /// player controller and directional sun intact for clean level generation.
        /// </summary>
        public static void HideVanillaLevelGeometry()
        {
            var activeScene = SceneManager.GetActiveScene();
            string sName = activeScene.name.ToLower();
            if (sName.Contains("menu")) return;

            GameObject[] rootObjects = activeScene.GetRootGameObjects();
            for (int i = 0; i < rootObjects.Length; i++)
            {
                GameObject root = rootObjects[i];
                if (root == null) continue;
                string r = root.name;

                if (r == "_LA" || r == "L_A")
                {
                    if (NativeSceneSun != null && NativeSceneSun.transform.IsChildOf(root.transform))
                    {
                        NativeSceneSun.transform.SetParent(null, true);
                        NativeSceneSun.gameObject.SetActive(true);
                        NativeSceneSun.enabled = true;
                    }
                    root.SetActive(false);
                }
                else if (r == "_LD" || r == "L_D")
                {
                    GameObject player = EditorSessionManager.FindPlayerEntity();
                    if (player != null && player.transform.IsChildOf(root.transform))
                    {
                        player.transform.SetParent(null, true);
                        player.SetActive(true);
                    }

                    StartLevelManager slm = root.GetComponentInChildren<StartLevelManager>(true);
                    if (slm != null)
                    {
                        slm.transform.SetParent(null, true);
                        slm.gameObject.SetActive(true);
                    }

                    root.SetActive(false);
                }
            }
        }
    }

    // =========================================================================
    // SECTION 2: HARMONY RUNTIME ENGINE PATCHES
    // =========================================================================

    /// <summary>
    /// Hooks into the game's level sequence start event to trigger custom level generation.
    /// </summary>
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

    /// <summary>
    /// Prevents defense turrets from colliding with their own fired bullets.
    /// </summary>
    [HarmonyPatch(typeof(TurretScript), nameof(TurretScript.Shoot))]
    public static class TurretShootPatch
    {
        private static readonly Collider[] _turretHitsBuffer = new Collider[128];

        [HarmonyPostfix]
        public static void Postfix(TurretScript __instance)
        {
            if (__instance == null || __instance._bulletPrefab == null) return;

            try
            {
                string bulletPrefabName = __instance._bulletPrefab.name;
                Vector3 turretPos = __instance.transform.position;

                Collider[] turretCols = __instance.GetComponentsInChildren<Collider>(true);
                SphereCollider triggerSphere = __instance._triggerAnimation;

                int hitCount = Physics.OverlapSphereNonAlloc(turretPos, 8.0f, _turretHitsBuffer, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < hitCount; i++)
                {
                    Collider hitCol = _turretHitsBuffer[i];
                    if (hitCol == null) continue;

                    GameObject hitGo = hitCol.gameObject;
                    bool isBullet = hitGo.name.Contains(bulletPrefabName) ||
                                    hitCol.transform.root.name.Contains(bulletPrefabName) ||
                                    hitGo.name.ToLower().Contains("bullet") ||
                                    hitCol.transform.root.name.ToLower().Contains("bullet");

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

                    Rigidbody rb = hitGo.GetComponent<Rigidbody>();
                    Vector3 forwardDir = (rb != null && rb.velocity.sqrMagnitude > 0.1f)
                        ? rb.velocity.normalized
                        : __instance.transform.forward;

                    hitGo.transform.position += forwardDir * 0.95f;
                }
            }
            catch { }
        }
    }
}