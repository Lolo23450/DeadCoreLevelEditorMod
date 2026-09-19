using System;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 1: TRUSS & MONOLITH CONFIGURATION COMPONENTS
    // =========================================================================

    public enum TrussStyle
    {
        IndustrialDark = 0, // Matte dark steel structural girder
        NeonLaced = 1,      // Dark frame with glowing neon triangular lacing
        HazardYellow = 2    // Industrial caution girder
    }

    public class TrussConfig : IEditorComponent
    {
        public string ComponentTag => "TRUSS";

        public Vector3 LocalPointA = new Vector3(-4f, 0f, 0f);
        public Vector3 LocalPointB = new Vector3(4f, 0f, 0f);
        public float Width = 0.85f;
        public float BayLength = 1.25f;
        public float StrutThickness = 0.055f;
        public TrussStyle Style = TrussStyle.IndustrialDark;
        public Color AccentColor = new Color(0f, 0.9f, 1f);
        public float GlowIntensity = 3.5f;

        public TrussConfig Clone()
        {
            return new TrussConfig
            {
                LocalPointA = this.LocalPointA,
                LocalPointB = this.LocalPointB,
                Width = this.Width,
                BayLength = this.BayLength,
                StrutThickness = this.StrutThickness,
                Style = this.Style,
                AccentColor = this.AccentColor,
                GlowIntensity = this.GlowIntensity
            };
        }

        IEditorComponent IEditorComponent.Clone() => Clone();

        public string Serialize()
        {
            var inv = CultureInfo.InvariantCulture;
            string hex = PersistenceUtility.ColorToHex(AccentColor);
            return $"{LocalPointA.x.ToString("F3", inv)}:{LocalPointA.y.ToString("F3", inv)}:{LocalPointA.z.ToString("F3", inv)}:" +
                   $"{LocalPointB.x.ToString("F3", inv)}:{LocalPointB.y.ToString("F3", inv)}:{LocalPointB.z.ToString("F3", inv)}:" +
                   $"{Width.ToString("F3", inv)}:{BayLength.ToString("F3", inv)}:{StrutThickness.ToString("F3", inv)}:" +
                   $"{(int)Style}:{hex}:{GlowIntensity.ToString("F2", inv)}";
        }

        public void Deserialize(string rawData)
        {
            if (string.IsNullOrWhiteSpace(rawData)) return;
            string[] p = rawData.Split(':');
            var inv = CultureInfo.InvariantCulture;

            if (p.Length >= 6)
            {
                LocalPointA = new Vector3(PersistenceUtility.ParseFloat(p[0]), PersistenceUtility.ParseFloat(p[1]), PersistenceUtility.ParseFloat(p[2]));
                LocalPointB = new Vector3(PersistenceUtility.ParseFloat(p[3]), PersistenceUtility.ParseFloat(p[4]), PersistenceUtility.ParseFloat(p[5]));
            }
            if (p.Length >= 7) Width = Mathf.Clamp(PersistenceUtility.ParseFloat(p[6], 0.85f), 0.2f, 8.0f);
            if (p.Length >= 8) BayLength = Mathf.Clamp(PersistenceUtility.ParseFloat(p[7], 1.25f), 0.3f, 10.0f);
            if (p.Length >= 9) StrutThickness = Mathf.Clamp(PersistenceUtility.ParseFloat(p[8], 0.055f), 0.015f, 0.5f);
            if (p.Length >= 10) Style = (TrussStyle)PersistenceUtility.ParseInt(p[9], 0);
            if (p.Length >= 11) AccentColor = PersistenceUtility.HexToColor(p[10], Color.cyan);
            if (p.Length >= 12) GlowIntensity = PersistenceUtility.ParseFloat(p[11], 3.5f);
        }
    }

    public class MonolithConfig : IEditorComponent
    {
        public string ComponentTag => "MONOLITH";

        public float BaseScale = 30f;
        public int Seed = 101;
        public int SlabCount = 4;
        public float HeightMultiplier = 2.4f;
        public bool HasAntennaSpire = true;
        public Color SilhouetteTint = new Color(0.035f, 0.038f, 0.045f, 1f);
        public Color BeaconColor = new Color(1f, 0.2f, 0.2f, 1f);

        public MonolithConfig Clone()
        {
            return new MonolithConfig
            {
                BaseScale = this.BaseScale,
                Seed = this.Seed,
                SlabCount = this.SlabCount,
                HeightMultiplier = this.HeightMultiplier,
                HasAntennaSpire = this.HasAntennaSpire,
                SilhouetteTint = this.SilhouetteTint,
                BeaconColor = this.BeaconColor
            };
        }

        IEditorComponent IEditorComponent.Clone() => Clone();

        public string Serialize()
        {
            var inv = CultureInfo.InvariantCulture;
            string silHex = PersistenceUtility.ColorToHex(SilhouetteTint);
            string bcHex = PersistenceUtility.ColorToHex(BeaconColor);
            return $"{BaseScale.ToString("F2", inv)}:{Seed}:{SlabCount}:{HeightMultiplier.ToString("F2", inv)}:" +
                   $"{(HasAntennaSpire ? 1 : 0)}:{silHex}:{bcHex}";
        }

        public void Deserialize(string rawData)
        {
            if (string.IsNullOrWhiteSpace(rawData)) return;
            string[] p = rawData.Split(':');
            var inv = CultureInfo.InvariantCulture;

            if (p.Length >= 1) BaseScale = Mathf.Clamp(PersistenceUtility.ParseFloat(p[0], 30f), 5f, 300f);
            if (p.Length >= 2) Seed = PersistenceUtility.ParseInt(p[1], 101);
            if (p.Length >= 3) SlabCount = Mathf.Clamp(PersistenceUtility.ParseInt(p[2], 4), 2, 7);
            if (p.Length >= 4) HeightMultiplier = Mathf.Clamp(PersistenceUtility.ParseFloat(p[3], 2.4f), 0.5f, 8.0f);
            if (p.Length >= 5) HasAntennaSpire = p[4] == "1";
            if (p.Length >= 6) SilhouetteTint = PersistenceUtility.HexToColor(p[5], new Color(0.035f, 0.038f, 0.045f));
            if (p.Length >= 7) BeaconColor = PersistenceUtility.HexToColor(p[6], Color.red);
        }
    }

    // =========================================================================
    // SECTION 2: PROCEDURAL SPACE-TRUSS MESH GENERATOR
    // =========================================================================

    public static class ProceduralTrussMeshBuilder
    {
        public static Mesh BuildTrussMesh(Vector3 start, Vector3 end, float width, float bayLength, float thickness, TrussStyle style)
        {
            Mesh mesh = new Mesh { name = "Procedural_Truss_Mesh" };

            float length = Vector3.Distance(start, end);
            if (length < 0.25f) length = 0.25f;

            Vector3 fwd = (end - start).normalized;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;

            Vector3 right = Vector3.Cross(fwd, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(fwd, Vector3.right).normalized;
            Vector3 up = Vector3.Cross(right, fwd).normalized;

            int numBays = Mathf.Max(1, Mathf.RoundToInt(length / Mathf.Max(0.2f, bayLength)));
            float actualBayLen = length / numBays;

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> frameTris = new List<int>();
            List<int> laceTris = new List<int>();

            float hw = width * 0.5f;

            // 4 main corner chord offsets (TL, TR, BR, BL)
            Vector3[] chords = new Vector3[]
            {
                -right * hw + up * hw, // 0: Top-Left
                 right * hw + up * hw, // 1: Top-Right
                 right * hw - up * hw, // 2: Bottom-Right
                -right * hw - up * hw  // 3: Bottom-Left
            };

            // 1. Build the 4 main corner chord rails along the full span
            for (int c = 0; c < 4; c++)
            {
                Vector3 cA = start + chords[c];
                Vector3 cB = end + chords[c];
                AddOrientedBar(cA, cB, thickness * 1.5f, fwd, right, up, verts, norms, uvs, frameTris);
            }

            // 2. Build transverse frames and alternating diagonal zigzag triangles (/\/\/\)
            int[][] faces = new int[][]
            {
                new int[] { 0, 1 }, // Top
                new int[] { 1, 2 }, // Right
                new int[] { 2, 3 }, // Bottom
                new int[] { 3, 0 }  // Left
            };

            for (int b = 0; b < numBays; b++)
            {
                float z0 = b * actualBayLen;
                float z1 = (b + 1) * actualBayLen;

                Vector3 p0 = start + fwd * z0;
                Vector3 p1 = start + fwd * z1;

                // Start transverse ring on the very first bay
                if (b == 0)
                {
                    for (int s = 0; s < 4; s++)
                    {
                        Vector3 rA = p0 + chords[faces[s][0]];
                        Vector3 rB = p0 + chords[faces[s][1]];
                        AddOrientedBar(rA, rB, thickness, (rB - rA).normalized, fwd, up, verts, norms, uvs, frameTris);
                    }
                }

                // End transverse ring for current bay
                for (int s = 0; s < 4; s++)
                {
                    Vector3 rA = p1 + chords[faces[s][0]];
                    Vector3 rB = p1 + chords[faces[s][1]];
                    AddOrientedBar(rA, rB, thickness, (rB - rA).normalized, fwd, up, verts, norms, uvs, frameTris);

                    // Alternating diagonal Warren brace
                    Vector3 dStart = (b % 2 == 0) ? (p0 + chords[faces[s][0]]) : (p0 + chords[faces[s][1]]);
                    Vector3 dEnd = (b % 2 == 0) ? (p1 + chords[faces[s][1]]) : (p1 + chords[faces[s][0]]);

                    List<int> targetTriList = (style == TrussStyle.NeonLaced) ? laceTris : frameTris;
                    AddOrientedBar(dStart, dEnd, thickness * 0.9f, (dEnd - dStart).normalized, right, up, verts, norms, uvs, targetTriList);
                }
            }

            mesh.vertices = verts.ToArray();
            mesh.normals = norms.ToArray();
            mesh.uv = uvs.ToArray();

            if (style == TrussStyle.NeonLaced)
            {
                mesh.subMeshCount = 2;
                mesh.SetTriangles(frameTris.ToArray(), 0);
                mesh.SetTriangles(laceTris.ToArray(), 1);
            }
            else
            {
                mesh.subMeshCount = 1;
                mesh.SetTriangles(frameTris.ToArray(), 0);
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddOrientedBar(Vector3 a, Vector3 b, float thickness, Vector3 barFwd, Vector3 sideA, Vector3 sideB,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris)
        {
            int baseIdx = verts.Count;
            float ht = thickness * 0.5f;

            Vector3 o1 = -sideA * ht - sideB * ht;
            Vector3 o2 = sideA * ht - sideB * ht;
            Vector3 o3 = sideA * ht + sideB * ht;
            Vector3 o4 = -sideA * ht + sideB * ht;

            verts.Add(a + o1); verts.Add(a + o2); verts.Add(a + o3); verts.Add(a + o4);
            verts.Add(b + o1); verts.Add(b + o2); verts.Add(b + o3); verts.Add(b + o4);

            for (int i = 0; i < 8; i++) norms.Add(barFwd);

            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));

            int[] f = new int[]
            {
                0, 1, 5, 0, 5, 4,
                1, 2, 6, 1, 6, 5,
                2, 3, 7, 2, 7, 6,
                3, 0, 4, 3, 4, 7
            };

            for (int i = 0; i < f.Length; i++) tris.Add(baseIdx + f[i]);
        }
    }

    // =========================================================================
    // SECTION 3: PROCEDURAL TRUSS RUNTIME SERVICE
    // =========================================================================

    public static class StructuralTrussService
    {
        public static readonly Dictionary<GameObject, TrussConfig> PlacedTrusses = new Dictionary<GameObject, TrussConfig>();
        private static Material _cachedFrameMaterial = null;
        private static Material _cachedNeonLaceMaterial = null;

        private static readonly Dictionary<GameObject, GameObject> _handleMarkersA = new Dictionary<GameObject, GameObject>();
        private static readonly Dictionary<GameObject, GameObject> _handleMarkersB = new Dictionary<GameObject, GameObject>();

        public static void EnsureMaterials()
        {
            if (_cachedFrameMaterial == null)
            {
                Shader s = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
                _cachedFrameMaterial = new Material(s)
                {
                    name = "Mat_Truss_Structural_Steel",
                    color = new Color(0.06f, 0.07f, 0.09f, 1f)
                };
                if (_cachedFrameMaterial.HasProperty("_BaseColor")) _cachedFrameMaterial.SetColor("_BaseColor", new Color(0.06f, 0.07f, 0.09f, 1f));
                if (_cachedFrameMaterial.HasProperty("_Smoothness")) _cachedFrameMaterial.SetFloat("_Smoothness", 0.45f);
                if (_cachedFrameMaterial.HasProperty("_Metallic")) _cachedFrameMaterial.SetFloat("_Metallic", 0.85f);
            }

            if (_cachedNeonLaceMaterial == null)
            {
                Shader unlit = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
                _cachedNeonLaceMaterial = new Material(unlit)
                {
                    name = "Mat_Truss_Neon_Lacing",
                    color = Color.cyan
                };
                _cachedNeonLaceMaterial.EnableKeyword("_EMISSION");
            }
        }

        public static GameObject CreateProceduralTruss(Vector3 origin, Vector3 localA, Vector3 localB, float width = 0.85f, float bayLen = 1.25f, float thickness = 0.055f)
        {
            EnsureMaterials();

            GameObject trussObj = new GameObject("Custom_Space_Truss_Girder");
            trussObj.transform.position = origin;
            trussObj.transform.rotation = Quaternion.identity;
            trussObj.transform.localScale = Vector3.one;

            trussObj.AddComponent<MeshFilter>();
            trussObj.AddComponent<MeshRenderer>();

            TrussConfig cfg = new TrussConfig
            {
                LocalPointA = localA,
                LocalPointB = localB,
                Width = width,
                BayLength = bayLen,
                StrutThickness = thickness,
                Style = TrussStyle.IndustrialDark,
                AccentColor = new Color(0f, 0.9f, 1f),
                GlowIntensity = 4.0f
            };

            ApplyTrussConfig(trussObj, cfg);
            EditorSessionManager.RegisterPlacedObject(trussObj);
            return trussObj;
        }

        public static void ApplyTrussConfig(GameObject trussObj, TrussConfig cfg)
        {
            if (trussObj == null || cfg == null) return;
            EnsureMaterials();
            PlacedTrusses[trussObj] = cfg.Clone();

            MeshFilter mf = trussObj.GetComponent<MeshFilter>() ?? trussObj.AddComponent<MeshFilter>();
            MeshRenderer mr = trussObj.GetComponent<MeshRenderer>() ?? trussObj.AddComponent<MeshRenderer>();

            Mesh trussMesh = ProceduralTrussMeshBuilder.BuildTrussMesh(
                cfg.LocalPointA, cfg.LocalPointB, cfg.Width, cfg.BayLength, cfg.StrutThickness, cfg.Style);
            mf.sharedMesh = trussMesh;

            if (cfg.Style == TrussStyle.NeonLaced)
            {
                Material neonInst = new Material(_cachedNeonLaceMaterial);
                Color glow = cfg.AccentColor * cfg.GlowIntensity;
                neonInst.color = cfg.AccentColor;
                if (neonInst.HasProperty("_Color")) neonInst.SetColor("_Color", cfg.AccentColor);
                if (neonInst.HasProperty("_EmissionColor")) neonInst.SetColor("_EmissionColor", glow);
                neonInst.EnableKeyword("_EMISSION");

                mr.materials = new Material[] { _cachedFrameMaterial, neonInst };
            }
            else
            {
                mr.materials = new Material[] { _cachedFrameMaterial };
            }

            BoxCollider bc = trussObj.GetComponent<BoxCollider>() ?? trussObj.AddComponent<BoxCollider>();
            bc.isTrigger = false;
            bc.center = (cfg.LocalPointA + cfg.LocalPointB) * 0.5f;
            Vector3 span = cfg.LocalPointB - cfg.LocalPointA;
            bc.size = new Vector3(
                Mathf.Max(cfg.Width, Mathf.Abs(span.x)),
                Mathf.Max(cfg.Width, Mathf.Abs(span.y)),
                Mathf.Max(cfg.Width, Mathf.Abs(span.z))
            );
        }

        public static bool IsTrussHandle(GameObject go, out GameObject trussOwner, out bool isPointB)
        {
            trussOwner = null;
            isPointB = false;
            if (go == null) return false;

            foreach (var kvp in _handleMarkersA)
            {
                if (kvp.Value == go) { trussOwner = kvp.Key; isPointB = false; return true; }
            }
            foreach (var kvp in _handleMarkersB)
            {
                if (kvp.Value == go) { trussOwner = kvp.Key; isPointB = true; return true; }
            }
            return false;
        }

        public static void UpdateTrussVisualHandles(GameObject trussObj)
        {
            if (trussObj == null || !PlacedTrusses.TryGetValue(trussObj, out TrussConfig cfg)) return;

            Vector3 worldA = trussObj.transform.TransformPoint(cfg.LocalPointA);
            Vector3 worldB = trussObj.transform.TransformPoint(cfg.LocalPointB);

            if (!_handleMarkersA.TryGetValue(trussObj, out GameObject handleA) || handleA == null)
            {
                handleA = GameObject.CreatePrimitive(PrimitiveType.Cube);
                handleA.name = "Truss_Handle_A_" + trussObj.name;
                handleA.transform.localScale = Vector3.one * 0.55f;
                handleA.GetComponent<Renderer>().sharedMaterial = GizmoMaterialCache.CreateSolidMaterial(new Color(0.2f, 1f, 0.4f));
                _handleMarkersA[trussObj] = handleA;
            }

            if (!_handleMarkersB.TryGetValue(trussObj, out GameObject handleB) || handleB == null)
            {
                handleB = GameObject.CreatePrimitive(PrimitiveType.Cube);
                handleB.name = "Truss_Handle_B_" + trussObj.name;
                handleB.transform.localScale = Vector3.one * 0.55f;
                handleB.GetComponent<Renderer>().sharedMaterial = GizmoMaterialCache.CreateSolidMaterial(new Color(1f, 0.55f, 0.1f));
                _handleMarkersB[trussObj] = handleB;
            }

            handleA.SetActive(true);
            handleB.SetActive(true);
            handleA.transform.position = worldA;
            handleB.transform.position = worldB;
        }

        public static void DestroyTrussHandles(GameObject trussObj)
        {
            if (trussObj == null) return;
            if (_handleMarkersA.TryGetValue(trussObj, out var a) && a != null) GameObject.DestroyImmediate(a);
            if (_handleMarkersB.TryGetValue(trussObj, out var b) && b != null) GameObject.DestroyImmediate(b);
            _handleMarkersA.Remove(trussObj);
            _handleMarkersB.Remove(trussObj);
        }

        public static void DestroyAllTrussHandles()
        {
            foreach (var kvp in _handleMarkersA) if (kvp.Value != null) GameObject.DestroyImmediate(kvp.Value);
            foreach (var kvp in _handleMarkersB) if (kvp.Value != null) GameObject.DestroyImmediate(kvp.Value);
            _handleMarkersA.Clear();
            _handleMarkersB.Clear();
        }

        public static void OnHandleDragged(GameObject trussObj, bool isPointB, Vector3 newWorldPos)
        {
            if (trussObj == null || !PlacedTrusses.TryGetValue(trussObj, out TrussConfig cfg)) return;

            Vector3 newLocal = trussObj.transform.InverseTransformPoint(newWorldPos);
            if (isPointB) cfg.LocalPointB = newLocal;
            else cfg.LocalPointA = newLocal;

            ApplyTrussConfig(trussObj, cfg);
            UpdateTrussVisualHandles(trussObj);
        }

        public static void SnapHandleToSurface(GameObject trussObj, bool isPointB)
        {
            if (trussObj == null || !PlacedTrusses.TryGetValue(trussObj, out TrussConfig cfg)) return;

            Vector3 currentLocal = isPointB ? cfg.LocalPointB : cfg.LocalPointA;
            Vector3 worldPos = trussObj.transform.TransformPoint(currentLocal);

            Vector3[] castDirs = new Vector3[] { Vector3.down, Vector3.up, Vector3.forward, -Vector3.forward, Vector3.right, -Vector3.right };
            RaycastHit bestHit = default;
            float closestDist = float.MaxValue;
            bool hitFound = false;

            for (int i = 0; i < castDirs.Length; i++)
            {
                if (Physics.Raycast(worldPos + castDirs[i] * -0.5f, castDirs[i], out RaycastHit hit, 12.0f))
                {
                    if (hit.collider != null && hit.collider.gameObject != trussObj && !hit.collider.name.Contains("Handle"))
                    {
                        if (hit.distance < closestDist)
                        {
                            closestDist = hit.distance;
                            bestHit = hit;
                            hitFound = true;
                        }
                    }
                }
            }

            if (hitFound)
            {
                Vector3 newLocal = trussObj.transform.InverseTransformPoint(bestHit.point);
                if (isPointB) cfg.LocalPointB = newLocal;
                else cfg.LocalPointA = newLocal;

                ApplyTrussConfig(trussObj, cfg);
                UpdateTrussVisualHandles(trussObj);
                EditorSessionManager.ShowNotification($"Snapped Girder Joint {(isPointB ? "B" : "A")} to surface!");
            }
            else
            {
                EditorSessionManager.ShowNotification("No surface found nearby to anchor girder.");
            }
        }
    }

    // =========================================================================
    // SECTION 4: DISTANT TECH MONOLITH & SILHOUETTE CLUSTERS
    // =========================================================================

    public static class TechMonolithService
    {
        public static readonly Dictionary<GameObject, MonolithConfig> PlacedMonoliths = new Dictionary<GameObject, MonolithConfig>();
        private static readonly List<Light> _strobeLights = new List<Light>();
        private static float _strobeTimer = 0f;

        private static Material _silhouetteMat = null;

        public static Material GetSilhouetteMaterial(Color tint)
        {
            if (_silhouetteMat == null)
            {
                Shader s = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
                _silhouetteMat = new Material(s)
                {
                    name = "Mat_Distant_Brutalist_Silhouette",
                    color = tint
                };
                if (_silhouetteMat.HasProperty("_BaseColor")) _silhouetteMat.SetColor("_BaseColor", tint);
                if (_silhouetteMat.HasProperty("_Smoothness")) _silhouetteMat.SetFloat("_Smoothness", 0.12f);
                if (_silhouetteMat.HasProperty("_Metallic")) _silhouetteMat.SetFloat("_Metallic", 0.90f);
            }
            _silhouetteMat.color = tint;
            return _silhouetteMat;
        }

        public static GameObject CreateTechMonolithCluster(Vector3 position, float baseScale = 30f)
        {
            GameObject clusterRoot = new GameObject("Custom_Tech_Monolith_Cluster");
            clusterRoot.transform.position = position;
            clusterRoot.transform.rotation = Quaternion.identity;
            clusterRoot.transform.localScale = Vector3.one;

            MonolithConfig cfg = new MonolithConfig
            {
                BaseScale = baseScale,
                Seed = UnityEngine.Random.Range(10, 9999),
                SlabCount = 4,
                HeightMultiplier = 2.4f,
                HasAntennaSpire = true,
                SilhouetteTint = new Color(0.035f, 0.038f, 0.045f, 1f),
                BeaconColor = new Color(1f, 0.2f, 0.2f, 1f)
            };

            ApplyMonolithConfig(clusterRoot, cfg);
            EditorSessionManager.RegisterPlacedObject(clusterRoot);
            return clusterRoot;
        }

        public static void ApplyMonolithConfig(GameObject clusterRoot, MonolithConfig cfg)
        {
            if (clusterRoot == null || cfg == null) return;
            PlacedMonoliths[clusterRoot] = cfg.Clone();

            // Clear old children
            for (int i = clusterRoot.transform.childCount - 1; i >= 0; i--)
            {
                GameObject.DestroyImmediate(clusterRoot.transform.GetChild(i).gameObject);
            }

            Material mat = GetSilhouetteMaterial(cfg.SilhouetteTint);
            System.Random rng = new System.Random(cfg.Seed);

            float highestY = 0f;
            Vector3 highestPos = Vector3.zero;

            for (int i = 0; i < cfg.SlabCount; i++)
            {
                GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = $"Slab_{i + 1}";
                slab.transform.SetParent(clusterRoot.transform, false);

                float width = (float)(rng.NextDouble() * 0.6 + 0.6) * cfg.BaseScale;
                float depth = (float)(rng.NextDouble() * 0.6 + 0.6) * cfg.BaseScale;
                float height = (float)(rng.NextDouble() * 1.2 + 0.8) * cfg.BaseScale * cfg.HeightMultiplier;

                float offsetX = (i == 0) ? 0f : (float)(rng.NextDouble() * 1.4 - 0.7) * cfg.BaseScale;
                float offsetZ = (i == 0) ? 0f : (float)(rng.NextDouble() * 1.4 - 0.7) * cfg.BaseScale;
                float offsetY = height * 0.5f;

                slab.transform.localPosition = new Vector3(offsetX, offsetY, offsetZ);
                slab.transform.localScale = new Vector3(width, height, depth);
                slab.GetComponent<Renderer>().sharedMaterial = mat;

                Collider sc = slab.GetComponent<Collider>();
                if (sc != null) GameObject.DestroyImmediate(sc);

                if (offsetY + height * 0.5f > highestY)
                {
                    highestY = offsetY + height * 0.5f;
                    highestPos = new Vector3(offsetX, highestY, offsetZ);
                }
            }

            // Procedural Antenna Spire with flashing warning beacon
            if (cfg.HasAntennaSpire)
            {
                GameObject antennaMast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                antennaMast.name = "Antenna_Mast";
                antennaMast.transform.SetParent(clusterRoot.transform, false);
                antennaMast.transform.localPosition = highestPos + Vector3.up * (cfg.BaseScale * 0.35f);
                antennaMast.transform.localScale = new Vector3(cfg.BaseScale * 0.02f, cfg.BaseScale * 0.35f, cfg.BaseScale * 0.02f);
                antennaMast.GetComponent<Renderer>().sharedMaterial = mat;
                Collider mc = antennaMast.GetComponent<Collider>();
                if (mc != null) GameObject.DestroyImmediate(mc);

                // Flashing beacon dot
                GameObject beaconOrb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                beaconOrb.name = "Beacon_Orb";
                beaconOrb.transform.SetParent(clusterRoot.transform, false);
                beaconOrb.transform.localPosition = highestPos + Vector3.up * (cfg.BaseScale * 0.70f);
                beaconOrb.transform.localScale = Vector3.one * (cfg.BaseScale * 0.08f);

                Material beaconMat = GizmoMaterialCache.CreateSolidMaterial(cfg.BeaconColor);
                beaconOrb.GetComponent<Renderer>().sharedMaterial = beaconMat;
                Collider bc = beaconOrb.GetComponent<Collider>();
                if (bc != null) GameObject.DestroyImmediate(bc);
            }

            // Root box trigger collider for editor viewport selection
            BoxCollider rootCol = clusterRoot.GetComponent<BoxCollider>() ?? clusterRoot.AddComponent<BoxCollider>();
            rootCol.isTrigger = true;
            rootCol.center = new Vector3(0f, highestY * 0.5f, 0f);
            rootCol.size = new Vector3(cfg.BaseScale * 2.8f, highestY * 1.2f, cfg.BaseScale * 2.8f);
        }

        public static void UpdateBeaconTick(float dt)
        {
            _strobeTimer += dt * 3.0f;
            float pulse = Mathf.PingPong(_strobeTimer, 1.0f);
            pulse = (pulse > 0.7f) ? 1.0f : 0.15f; // Sharp aviation strobe flash

            foreach (var kvp in PlacedMonoliths)
            {
                GameObject root = kvp.Key;
                MonolithConfig cfg = kvp.Value;
                if (root == null || !root.activeSelf || !cfg.HasAntennaSpire) continue;

                Transform beacon = root.transform.Find("Beacon_Orb");
                if (beacon != null)
                {
                    Renderer r = beacon.GetComponent<Renderer>();
                    if (r != null && r.material != null)
                    {
                        Color c = cfg.BeaconColor * (pulse * 4.0f);
                        if (r.material.HasProperty("_EmissionColor")) r.material.SetColor("_EmissionColor", c);
                        if (r.material.HasProperty("_Color")) r.material.SetColor("_Color", c);
                    }
                }
            }
        }
    }
}