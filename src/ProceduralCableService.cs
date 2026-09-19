using System;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 1: CABLE STYLES & CONFIGURATION COMPONENT
    // =========================================================================

    public enum CableStyle
    {
        NeonStriped = 0,    // Dark matte body + glowing neon spine line
        FullNeon = 1,       // Fully glowing energy conduit
        IndustrialSolid = 2 // Dark rubber/metal power line
    }

    public class CableConfig : IEditorComponent
    {
        public string ComponentTag => "CABLE";

        public Vector3 LocalPointA = new Vector3(-3f, 0f, 0f);
        public Vector3 LocalPointB = new Vector3(3f, 0f, 0f);
        public float Radius = 0.06f;       // Cable thickness
        public float SagAmount = 1.2f;     // How far gravity pulls it down
        public float SlackPercent = 0.15f; // Extra length ratio
        public CableStyle Style = CableStyle.NeonStriped;
        public Color NeonColor = new Color(0f, 0.9f, 1f);
        public float GlowIntensity = 3.5f;

        public CableConfig Clone()
        {
            return new CableConfig
            {
                LocalPointA = this.LocalPointA,
                LocalPointB = this.LocalPointB,
                Radius = this.Radius,
                SagAmount = this.SagAmount,
                SlackPercent = this.SlackPercent,
                Style = this.Style,
                NeonColor = this.NeonColor,
                GlowIntensity = this.GlowIntensity
            };
        }

        IEditorComponent IEditorComponent.Clone() => Clone();

        public string Serialize()
        {
            var inv = CultureInfo.InvariantCulture;
            string hex = PersistenceUtility.ColorToHex(NeonColor);
            return $"{LocalPointA.x.ToString("F3", inv)}:{LocalPointA.y.ToString("F3", inv)}:{LocalPointA.z.ToString("F3", inv)}:" +
                   $"{LocalPointB.x.ToString("F3", inv)}:{LocalPointB.y.ToString("F3", inv)}:{LocalPointB.z.ToString("F3", inv)}:" +
                   $"{Radius.ToString("F3", inv)}:{SagAmount.ToString("F2", inv)}:{(int)Style}:{hex}:{GlowIntensity.ToString("F2", inv)}";
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
            if (p.Length >= 7) Radius = Mathf.Clamp(PersistenceUtility.ParseFloat(p[6], 0.06f), 0.015f, 1.5f);
            if (p.Length >= 8) SagAmount = PersistenceUtility.ParseFloat(p[7], 1.2f);
            if (p.Length >= 9) Style = (CableStyle)PersistenceUtility.ParseInt(p[8], 0);
            if (p.Length >= 10) NeonColor = PersistenceUtility.HexToColor(p[9], Color.cyan);
            if (p.Length >= 11) GlowIntensity = PersistenceUtility.ParseFloat(p[10], 3.5f);
        }
    }

    // =========================================================================
    // SECTION 2: PROCEDURAL MESH GENERATOR & CATENARY MATHEMATICS
    // =========================================================================

    public static class ProceduralCableMeshBuilder
    {
        private const int Subdivisions = 24; // Points along the curve
        private const int RadialSides = 8;   // Octagonal tube cross-section

        public static Mesh BuildExtrudedCableMesh(Vector3 start, Vector3 end, float radius, float sag, CableStyle style)
        {
            Mesh mesh = new Mesh { name = "Procedural_Cable_Mesh" };

            float dist = Vector3.Distance(start, end);
            Vector3 diff = end - start;

            // Generate curve points with catenary/parabolic gravity hang
            Vector3[] curvePoints = new Vector3[Subdivisions + 1];
            for (int i = 0; i <= Subdivisions; i++)
            {
                float t = i / (float)Subdivisions;
                Vector3 linear = Vector3.Lerp(start, end, t);

                // Natural parabolic hang: max at t = 0.5, zero at t = 0 and 1
                float hangFactor = 4f * t * (1f - t);
                Vector3 gravityDrop = Vector3.down * (sag * hangFactor);

                curvePoints[i] = linear + gravityDrop;
            }

            // Compute Rotation Minimizing Frames (RMF) along curve to prevent tube twisting
            Vector3[] tangents = new Vector3[Subdivisions + 1];
            Vector3[] normals = new Vector3[Subdivisions + 1];
            Vector3[] binormals = new Vector3[Subdivisions + 1];

            tangents[0] = (curvePoints[1] - curvePoints[0]).normalized;
            Vector3 initUp = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(tangents[0], initUp)) > 0.95f) initUp = Vector3.right;
            normals[0] = Vector3.Cross(tangents[0], initUp).normalized;
            binormals[0] = Vector3.Cross(tangents[0], normals[0]).normalized;

            for (int i = 1; i <= Subdivisions; i++)
            {
                Vector3 prevT = tangents[i - 1];
                Vector3 curT = (i < Subdivisions) ? (curvePoints[i + 1] - curvePoints[i]).normalized : prevT;
                tangents[i] = curT;

                Vector3 axis = Vector3.Cross(prevT, curT);
                if (axis.sqrMagnitude < 0.0001f)
                {
                    normals[i] = normals[i - 1];
                    binormals[i] = binormals[i - 1];
                }
                else
                {
                    float angle = Vector3.SignedAngle(prevT, curT, axis);
                    Quaternion rot = Quaternion.AngleAxis(angle, axis);
                    normals[i] = (rot * normals[i - 1]).normalized;
                    binormals[i] = (rot * binormals[i - 1]).normalized;
                }
            }

            // Assemble vertices and normals
            int ringVertexCount = RadialSides + 1;
            int totalRings = Subdivisions + 1;
            Vector3[] vertices = new Vector3[totalRings * ringVertexCount];
            Vector3[] vNormals = new Vector3[totalRings * ringVertexCount];
            Vector2[] uvs = new Vector2[totalRings * ringVertexCount];

            float lengthAcc = 0f;

            for (int ring = 0; ring < totalRings; ring++)
            {
                if (ring > 0) lengthAcc += Vector3.Distance(curvePoints[ring], curvePoints[ring - 1]);

                Vector3 center = curvePoints[ring];
                Vector3 rVec = normals[ring];
                Vector3 uVec = binormals[ring];

                for (int side = 0; side <= RadialSides; side++)
                {
                    float u = side / (float)RadialSides;
                    float angle = u * Mathf.PI * 2f;

                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);

                    Vector3 localRadial = (rVec * cos + uVec * sin).normalized;
                    int idx = ring * ringVertexCount + side;

                    vertices[idx] = center + localRadial * radius;
                    vNormals[idx] = localRadial;
                    uvs[idx] = new Vector2(u, lengthAcc * 0.75f);
                }
            }

            // Build Triangles and split into submeshes
            List<int> bodyTris = new List<int>();
            List<int> neonTris = new List<int>();

            for (int ring = 0; ring < Subdivisions; ring++)
            {
                for (int side = 0; side < RadialSides; side++)
                {
                    int current = ring * ringVertexCount + side;
                    int next = current + ringVertexCount;

                    bool isNeonSpineSegment = (style == CableStyle.NeonStriped) && (side == 0);

                    if (style == CableStyle.FullNeon || isNeonSpineSegment)
                    {
                        neonTris.Add(current);
                        neonTris.Add(next);
                        neonTris.Add(current + 1);

                        neonTris.Add(current + 1);
                        neonTris.Add(next);
                        neonTris.Add(next + 1);
                    }
                    else
                    {
                        bodyTris.Add(current);
                        bodyTris.Add(next);
                        bodyTris.Add(current + 1);

                        bodyTris.Add(current + 1);
                        bodyTris.Add(next);
                        bodyTris.Add(next + 1);
                    }
                }
            }

            mesh.vertices = vertices;
            mesh.normals = vNormals;
            mesh.uv = uvs;

            if (style == CableStyle.NeonStriped)
            {
                mesh.subMeshCount = 2;
                mesh.SetTriangles(bodyTris.ToArray(), 0);
                mesh.SetTriangles(neonTris.ToArray(), 1);
            }
            else if (style == CableStyle.FullNeon)
            {
                mesh.subMeshCount = 1;
                mesh.SetTriangles(neonTris.ToArray(), 0);
            }
            else
            {
                mesh.subMeshCount = 1;
                mesh.SetTriangles(bodyTris.ToArray(), 0);
            }

            mesh.RecalculateBounds();
            return mesh;
        }
    }

    // =========================================================================
    // SECTION 3: CABLE RUNTIME & LIFECYCLE SERVICE
    // =========================================================================

    public static class ProceduralCableService
    {
        public static readonly Dictionary<GameObject, CableConfig> PlacedCables = new Dictionary<GameObject, CableConfig>();
        private static Material _cachedJacketMaterial = null;
        private static Material _cachedNeonMaterial = null;

        // Visual 3D endpoint edit markers
        private static readonly Dictionary<GameObject, GameObject> _handleMarkersA = new Dictionary<GameObject, GameObject>();
        private static readonly Dictionary<GameObject, GameObject> _handleMarkersB = new Dictionary<GameObject, GameObject>();

        public static void EnsureMaterials()
        {
            if (_cachedJacketMaterial == null)
            {
                Shader lit = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
                _cachedJacketMaterial = new Material(lit)
                {
                    name = "Mat_Cable_Jacket_Body",
                    color = new Color(0.08f, 0.09f, 0.11f, 1f)
                };
                if (_cachedJacketMaterial.HasProperty("_BaseColor")) _cachedJacketMaterial.SetColor("_BaseColor", new Color(0.08f, 0.09f, 0.11f, 1f));
                if (_cachedJacketMaterial.HasProperty("_Smoothness")) _cachedJacketMaterial.SetFloat("_Smoothness", 0.35f);
                if (_cachedJacketMaterial.HasProperty("_Metallic")) _cachedJacketMaterial.SetFloat("_Metallic", 0.65f);
            }

            if (_cachedNeonMaterial == null)
            {
                Shader unlit = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
                _cachedNeonMaterial = new Material(unlit)
                {
                    name = "Mat_Cable_Neon_Strip",
                    color = Color.cyan
                };
                _cachedNeonMaterial.EnableKeyword("_EMISSION");
            }
        }

        public static GameObject CreateProceduralCable(Vector3 origin, Vector3 localA, Vector3 localB, float radius = 0.06f, float sag = 1.2f, CableStyle style = CableStyle.NeonStriped)
        {
            EnsureMaterials();

            GameObject cableObj = new GameObject("Custom_Procedural_Cable");
            cableObj.transform.position = origin;
            cableObj.transform.rotation = Quaternion.identity;
            cableObj.transform.localScale = Vector3.one;

            MeshFilter mf = cableObj.AddComponent<MeshFilter>();
            MeshRenderer mr = cableObj.AddComponent<MeshRenderer>();

            CableConfig cfg = new CableConfig
            {
                LocalPointA = localA,
                LocalPointB = localB,
                Radius = radius,
                SagAmount = sag,
                Style = style,
                NeonColor = new Color(0f, 0.9f, 1f),
                GlowIntensity = 4.0f
            };

            ApplyCableConfig(cableObj, cfg);
            EditorSessionManager.RegisterPlacedObject(cableObj);
            return cableObj;
        }

        public static void ApplyCableConfig(GameObject cableObj, CableConfig cfg)
        {
            if (cableObj == null || cfg == null) return;
            EnsureMaterials();
            PlacedCables[cableObj] = cfg.Clone();

            MeshFilter mf = cableObj.GetComponent<MeshFilter>() ?? cableObj.AddComponent<MeshFilter>();
            MeshRenderer mr = cableObj.GetComponent<MeshRenderer>() ?? cableObj.AddComponent<MeshRenderer>();

            // Generate procedural mesh
            Mesh cableMesh = ProceduralCableMeshBuilder.BuildExtrudedCableMesh(cfg.LocalPointA, cfg.LocalPointB, cfg.Radius, cfg.SagAmount, cfg.Style);
            mf.sharedMesh = cableMesh;

            // Generate dedicated neon emissive material instance
            Material neonInst = new Material(_cachedNeonMaterial);
            Color finalGlow = cfg.NeonColor * cfg.GlowIntensity;
            neonInst.color = cfg.NeonColor;
            if (neonInst.HasProperty("_Color")) neonInst.SetColor("_Color", cfg.NeonColor);
            if (neonInst.HasProperty("_BaseColor")) neonInst.SetColor("_BaseColor", cfg.NeonColor);
            if (neonInst.HasProperty("_EmissionColor")) neonInst.SetColor("_EmissionColor", finalGlow);
            if (neonInst.HasProperty("_EmissiveColor")) neonInst.SetColor("_EmissiveColor", finalGlow);
            neonInst.EnableKeyword("_EMISSION");

            if (cfg.Style == CableStyle.NeonStriped)
            {
                mr.materials = new Material[] { _cachedJacketMaterial, neonInst };
            }
            else if (cfg.Style == CableStyle.FullNeon)
            {
                mr.materials = new Material[] { neonInst };
            }
            else
            {
                mr.materials = new Material[] { _cachedJacketMaterial };
            }

            // Collider for editor raycasting & selection
            BoxCollider bc = cableObj.GetComponent<BoxCollider>() ?? cableObj.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.center = (cfg.LocalPointA + cfg.LocalPointB) * 0.5f + Vector3.down * (cfg.SagAmount * 0.5f);
            Vector3 span = cfg.LocalPointB - cfg.LocalPointA;
            bc.size = new Vector3(
                Mathf.Max(0.5f, Mathf.Abs(span.x)),
                Mathf.Max(0.8f, Mathf.Abs(cfg.SagAmount) + cfg.Radius * 4f),
                Mathf.Max(0.5f, Mathf.Abs(span.z))
            );
        }

        // =========================================================================
        // SECTION 4: 3D INTERACTIVE HANDLES
        // =========================================================================

        public static bool IsCableHandle(GameObject go, out GameObject cableOwner, out bool isPointB)
        {
            cableOwner = null;
            isPointB = false;
            if (go == null) return false;

            foreach (var kvp in _handleMarkersA)
            {
                if (kvp.Value == go) { cableOwner = kvp.Key; isPointB = false; return true; }
            }

            foreach (var kvp in _handleMarkersB)
            {
                if (kvp.Value == go) { cableOwner = kvp.Key; isPointB = true; return true; }
            }

            return false;
        }

        public static void UpdateCableVisualHandles(GameObject cableObj)
        {
            if (cableObj == null || !PlacedCables.TryGetValue(cableObj, out CableConfig cfg)) return;

            Vector3 worldA = cableObj.transform.TransformPoint(cfg.LocalPointA);
            Vector3 worldB = cableObj.transform.TransformPoint(cfg.LocalPointB);

            if (!_handleMarkersA.TryGetValue(cableObj, out GameObject handleA) || handleA == null)
            {
                handleA = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                handleA.name = "Cable_Handle_A_" + cableObj.name;
                handleA.transform.localScale = Vector3.one * 0.45f;
                handleA.GetComponent<Renderer>().sharedMaterial = GizmoMaterialCache.CreateSolidMaterial(new Color(0.2f, 1f, 0.4f));
                _handleMarkersA[cableObj] = handleA;
            }

            if (!_handleMarkersB.TryGetValue(cableObj, out GameObject handleB) || handleB == null)
            {
                handleB = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                handleB.name = "Cable_Handle_B_" + cableObj.name;
                handleB.transform.localScale = Vector3.one * 0.45f;
                handleB.GetComponent<Renderer>().sharedMaterial = GizmoMaterialCache.CreateSolidMaterial(new Color(1f, 0.5f, 0.1f));
                _handleMarkersB[cableObj] = handleB;
            }

            handleA.SetActive(true);
            handleB.SetActive(true);
            handleA.transform.position = worldA;
            handleB.transform.position = worldB;
        }

        public static void HideCableVisualHandles(GameObject cableObj)
        {
            if (cableObj == null) return;
            if (_handleMarkersA.TryGetValue(cableObj, out var a) && a != null) a.SetActive(false);
            if (_handleMarkersB.TryGetValue(cableObj, out var b) && b != null) b.SetActive(false);
        }

        public static void DestroyCableHandles(GameObject cableObj)
        {
            if (cableObj == null) return;
            if (_handleMarkersA.TryGetValue(cableObj, out var a) && a != null) GameObject.DestroyImmediate(a);
            if (_handleMarkersB.TryGetValue(cableObj, out var b) && b != null) GameObject.DestroyImmediate(b);
            _handleMarkersA.Remove(cableObj);
            _handleMarkersB.Remove(cableObj);
        }
        public static void DestroyAllCableHandles()
        {
            foreach (var kvp in _handleMarkersA) if (kvp.Value != null) GameObject.DestroyImmediate(kvp.Value);
            foreach (var kvp in _handleMarkersB) if (kvp.Value != null) GameObject.DestroyImmediate(kvp.Value);
            _handleMarkersA.Clear();
            _handleMarkersB.Clear();
        }

        public static void OnHandleDragged(GameObject cableObj, bool isPointB, Vector3 newWorldPos)
        {
            if (cableObj == null || !PlacedCables.TryGetValue(cableObj, out CableConfig cfg)) return;

            Vector3 newLocal = cableObj.transform.InverseTransformPoint(newWorldPos);
            if (isPointB) cfg.LocalPointB = newLocal;
            else cfg.LocalPointA = newLocal;

            ApplyCableConfig(cableObj, cfg);
            UpdateCableVisualHandles(cableObj);
        }
    }
}