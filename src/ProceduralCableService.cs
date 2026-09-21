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
        NeonStriped = 0,    // Dark matte jacket + glowing neon spine line
        FullNeon = 1,       // Fully glowing energy conduit
        IndustrialSolid = 2 // Dark rubber/metal conduit
    }

    public enum CableBundleType
    {
        Single = 0,
        Twin = 1,
        Triple = 2
    }

    public class CableConfig : IEditorComponent
    {
        public string ComponentTag => "CABLE";

        public Vector3 LocalPointA = new Vector3(-3f, 0f, 0f);
        public Vector3 LocalPointB = new Vector3(3f, 0f, 0f);
        public float Radius = 0.06f;            // Cable thickness
        public float SagAmount = 1.2f;          // Gravity sag distance
        public CableStyle Style = CableStyle.NeonStriped;
        public CableBundleType Bundle = CableBundleType.Single;
        public Color NeonColor = new Color(0f, 0.9f, 1f);
        public float GlowIntensity = 3.5f;
        public bool HasMountSockets = true;     // Metal junction collars at ends
        public float EnergyFlowSpeed = 1.5f;    // Flowing neon UV speed (0 = static)

        public CableConfig Clone()
        {
            return new CableConfig
            {
                LocalPointA = this.LocalPointA,
                LocalPointB = this.LocalPointB,
                Radius = this.Radius,
                SagAmount = this.SagAmount,
                Style = this.Style,
                Bundle = this.Bundle,
                NeonColor = this.NeonColor,
                GlowIntensity = this.GlowIntensity,
                HasMountSockets = this.HasMountSockets,
                EnergyFlowSpeed = this.EnergyFlowSpeed
            };
        }

        IEditorComponent IEditorComponent.Clone() => Clone();

        public string Serialize()
        {
            var inv = CultureInfo.InvariantCulture;
            string hex = PersistenceUtility.ColorToHex(NeonColor);
            return $"{LocalPointA.x.ToString("F3", inv)}:{LocalPointA.y.ToString("F3", inv)}:{LocalPointA.z.ToString("F3", inv)}:" +
                   $"{LocalPointB.x.ToString("F3", inv)}:{LocalPointB.y.ToString("F3", inv)}:{LocalPointB.z.ToString("F3", inv)}:" +
                   $"{Radius.ToString("F3", inv)}:{SagAmount.ToString("F2", inv)}:{(int)Style}:{hex}:{GlowIntensity.ToString("F2", inv)}:" +
                   $"{(HasMountSockets ? 1 : 0)}:{(int)Bundle}:{EnergyFlowSpeed.ToString("F2", inv)}";
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
            if (p.Length >= 12) HasMountSockets = p[11] == "1";
            if (p.Length >= 13) Bundle = (CableBundleType)PersistenceUtility.ParseInt(p[12], 0);
            if (p.Length >= 14) EnergyFlowSpeed = PersistenceUtility.ParseFloat(p[13], 1.5f);
        }
    }

    // =========================================================================
    // SECTION 2: PROCEDURAL MESH GENERATOR & TUBE EXTRUDER
    // =========================================================================

    public static class ProceduralCableMeshBuilder
    {
        private const int RadialSides = 8; // Octagonal cross-section

        public static Mesh BuildExtrudedCableMesh(Vector3 start, Vector3 end, float radius, float sag, CableStyle style, CableBundleType bundle, bool sockets)
        {
            Mesh mesh = new Mesh { name = "Procedural_Cable_Mesh" };

            float span = Vector3.Distance(start, end);
            int subdivisions = Mathf.Clamp(Mathf.RoundToInt(span * 3.5f), 12, 64);

            List<Vector3> allVertices = new List<Vector3>();
            List<Vector3> allNormals = new List<Vector3>();
            List<Vector2> allUVs = new List<Vector2>();
            List<int> bodyTris = new List<int>();
            List<int> neonTris = new List<int>();

            // Calculate radial separation vectors for multi-strand cables
            List<Vector3> strandOffsets = GetStrandOffsets(start, end, radius, bundle);

            for (int s = 0; s < strandOffsets.Count; s++)
            {
                Vector3 sOffset = strandOffsets[s];
                BuildSingleStrand(start + sOffset, end + sOffset, radius, sag, subdivisions, style, allVertices, allNormals, allUVs, bodyTris, neonTris);
            }

            // Procedural wall socket collars at both ends
            if (sockets)
            {
                float collarRadius = radius * (strandOffsets.Count > 1 ? 2.3f : 1.65f);
                BuildMountSocket(start, (end - start).normalized, collarRadius, allVertices, allNormals, allUVs, bodyTris);
                BuildMountSocket(end, (start - end).normalized, collarRadius, allVertices, allNormals, allUVs, bodyTris);
            }

            mesh.vertices = allVertices.ToArray();
            mesh.normals = allNormals.ToArray();
            mesh.uv = allUVs.ToArray();

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

        private static List<Vector3> GetStrandOffsets(Vector3 start, Vector3 end, float radius, CableBundleType bundle)
        {
            List<Vector3> offsets = new List<Vector3>();
            Vector3 fwd = (end - start).normalized;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;

            Vector3 right = Vector3.Cross(fwd, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.001f) right = Vector3.Cross(fwd, Vector3.right).normalized;
            Vector3 up = Vector3.Cross(right, fwd).normalized;

            float spacing = radius * 2.2f;

            switch (bundle)
            {
                case CableBundleType.Twin:
                    offsets.Add(right * (spacing * 0.5f));
                    offsets.Add(-right * (spacing * 0.5f));
                    break;
                case CableBundleType.Triple:
                    offsets.Add(up * (spacing * 0.577f));
                    offsets.Add(-up * (spacing * 0.288f) + right * (spacing * 0.5f));
                    offsets.Add(-up * (spacing * 0.288f) - right * (spacing * 0.5f));
                    break;
                default:
                    offsets.Add(Vector3.zero);
                    break;
            }

            return offsets;
        }

        private static void BuildSingleStrand(Vector3 start, Vector3 end, float radius, float sag, int subdivisions, CableStyle style,
            List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> bodyTris, List<int> neonTris)
        {
            Vector3[] curvePoints = new Vector3[subdivisions + 1];
            for (int i = 0; i <= subdivisions; i++)
            {
                float t = i / (float)subdivisions;
                Vector3 linear = Vector3.Lerp(start, end, t);
                float hangFactor = 4f * t * (1f - t);
                curvePoints[i] = linear + Vector3.down * (sag * hangFactor);
            }

            // Rotation Minimizing Frames (RMF) along curve to prevent tube twisting
            Vector3[] tangents = new Vector3[subdivisions + 1];
            Vector3[] normals = new Vector3[subdivisions + 1];
            Vector3[] binormals = new Vector3[subdivisions + 1];

            tangents[0] = (curvePoints[1] - curvePoints[0]).normalized;
            Vector3 initUp = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(tangents[0], initUp)) > 0.95f) initUp = Vector3.right;
            normals[0] = Vector3.Cross(tangents[0], initUp).normalized;
            binormals[0] = Vector3.Cross(tangents[0], normals[0]).normalized;

            for (int i = 1; i <= subdivisions; i++)
            {
                Vector3 prevT = tangents[i - 1];
                Vector3 curT = (i < subdivisions) ? (curvePoints[i + 1] - curvePoints[i]).normalized : prevT;
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

            int baseVertexIndex = verts.Count;
            int ringVertexCount = RadialSides + 1;
            float lengthAcc = 0f;

            for (int ring = 0; ring <= subdivisions; ring++)
            {
                if (ring > 0) lengthAcc += Vector3.Distance(curvePoints[ring], curvePoints[ring - 1]);

                Vector3 center = curvePoints[ring];
                Vector3 rVec = normals[ring];
                Vector3 uVec = binormals[ring];

                for (int side = 0; side <= RadialSides; side++)
                {
                    float u = side / (float)RadialSides;
                    float angle = u * Mathf.PI * 2f;

                    Vector3 localRadial = (rVec * Mathf.Cos(angle) + uVec * Mathf.Sin(angle)).normalized;

                    verts.Add(center + localRadial * radius);
                    norms.Add(localRadial);
                    uvs.Add(new Vector2(u, lengthAcc * 1.25f));
                }
            }

            // Triangulate cylindrical body segments (Clean, single-pass, outward-facing winding)
            for (int ring = 0; ring < subdivisions; ring++)
            {
                for (int side = 0; side < RadialSides; side++)
                {
                    int current = baseVertexIndex + ring * ringVertexCount + side;
                    int next = current + ringVertexCount;

                    bool isNeonSpine = (style == CableStyle.NeonStriped) && (side == 0);
                    List<int> targetTris = (style == CableStyle.FullNeon || isNeonSpine) ? neonTris : bodyTris;

                    targetTris.Add(current);
                    targetTris.Add(current + 1);
                    targetTris.Add(next);

                    targetTris.Add(current + 1);
                    targetTris.Add(next + 1);
                    targetTris.Add(next);
                }
            }

            // Closed end-caps
            BuildCap(curvePoints[0], -tangents[0], radius, verts, norms, uvs, bodyTris, true);
            BuildCap(curvePoints[subdivisions], tangents[subdivisions], radius, verts, norms, uvs, bodyTris, false);
        }

        private static void BuildCap(Vector3 center, Vector3 normal, float radius, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris, bool reverse)
        {
            int centerIdx = verts.Count;
            verts.Add(center);
            norms.Add(normal);
            uvs.Add(new Vector2(0.5f, 0.5f));

            Vector3 rVec = Vector3.Cross(normal, Vector3.up).normalized;
            if (rVec.sqrMagnitude < 0.001f) rVec = Vector3.Cross(normal, Vector3.right).normalized;
            Vector3 uVec = Vector3.Cross(rVec, normal).normalized;

            int ringStart = verts.Count;
            for (int i = 0; i < RadialSides; i++)
            {
                float a = (i / (float)RadialSides) * Mathf.PI * 2f;
                Vector3 p = center + (rVec * Mathf.Cos(a) + uVec * Mathf.Sin(a)) * radius;
                verts.Add(p);
                norms.Add(normal);
                uvs.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
            }

            for (int i = 0; i < RadialSides; i++)
            {
                int next = (i + 1) % RadialSides;
                if (reverse)
                {
                    tris.Add(centerIdx);
                    tris.Add(ringStart + i);
                    tris.Add(ringStart + next);
                }
                else
                {
                    tris.Add(centerIdx);
                    tris.Add(ringStart + next);
                    tris.Add(ringStart + i);
                }
            }
        }

        private static void BuildMountSocket(Vector3 origin, Vector3 outwardDir, float collarRadius, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris)
        {
            int baseIdx = verts.Count;
            float collarDepth = collarRadius * 0.75f;
            Vector3 backCenter = origin - outwardDir * collarDepth;

            Vector3 rVec = Vector3.Cross(outwardDir, Vector3.up).normalized;
            if (rVec.sqrMagnitude < 0.001f) rVec = Vector3.Cross(outwardDir, Vector3.right).normalized;
            Vector3 uVec = Vector3.Cross(rVec, outwardDir).normalized;

            for (int i = 0; i < RadialSides; i++)
            {
                float a = (i / (float)RadialSides) * Mathf.PI * 2f;
                Vector3 offset = (rVec * Mathf.Cos(a) + uVec * Mathf.Sin(a)) * collarRadius;

                verts.Add(origin + offset);
                norms.Add(offset.normalized);
                uvs.Add(new Vector2(i / (float)RadialSides, 1f));

                verts.Add(backCenter + offset);
                norms.Add(offset.normalized);
                uvs.Add(new Vector2(i / (float)RadialSides, 0f));
            }

            for (int i = 0; i < RadialSides; i++)
            {
                int next = (i + 1) % RadialSides;
                int f1 = baseIdx + i * 2;
                int b1 = f1 + 1;
                int f2 = baseIdx + next * 2;
                int b2 = f2 + 1;

                tris.Add(f1);
                tris.Add(f2);
                tris.Add(b1);

                tris.Add(f2);
                tris.Add(b2);
                tris.Add(b1);
            }
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
                Shader lit = Shader.Find("HDRP/Lit")
                    ?? (EditorSessionManager.CachedSceneMaterial != null ? EditorSessionManager.CachedSceneMaterial.shader : null)
                    ?? Shader.Find("Standard");

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
                Shader unlit = Shader.Find("HDRP/Unlit")
                    ?? Shader.Find("Unlit/Color")
                    ?? _cachedJacketMaterial.shader;

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

            cableObj.AddComponent<MeshFilter>();
            cableObj.AddComponent<MeshRenderer>();

            CableConfig cfg = new CableConfig
            {
                LocalPointA = localA,
                LocalPointB = localB,
                Radius = radius,
                SagAmount = sag,
                Style = style,
                Bundle = CableBundleType.Single,
                NeonColor = new Color(0f, 0.9f, 1f),
                GlowIntensity = 4.0f,
                HasMountSockets = true,
                EnergyFlowSpeed = 1.5f
            };

            ApplyCableConfig(cableObj, cfg);
            EditorSessionManager.RegisterPlacedObject(cableObj);

            if (EditorSessionManager.IsEditModeActive)
                UpdateCableVisualHandles(cableObj);
            else
                HideCableVisualHandles(cableObj);

            return cableObj;
        }

        public static void ApplyCableConfig(GameObject cableObj, CableConfig cfg)
        {
            if (cableObj == null || cfg == null) return;
            EnsureMaterials();
            PlacedCables[cableObj] = cfg.Clone();

            MeshFilter mf = cableObj.GetComponent<MeshFilter>() ?? cableObj.AddComponent<MeshFilter>();
            MeshRenderer mr = cableObj.GetComponent<MeshRenderer>() ?? cableObj.AddComponent<MeshRenderer>();

            // Safely clean up old procedural mesh to prevent unmanaged memory leaks
            if (mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("Procedural_"))
            {
                GameObject.DestroyImmediate(mf.sharedMesh);
            }

            // Generate fresh procedural mesh
            Mesh cableMesh = ProceduralCableMeshBuilder.BuildExtrudedCableMesh(
                cfg.LocalPointA, cfg.LocalPointB, cfg.Radius, cfg.SagAmount, cfg.Style, cfg.Bundle, cfg.HasMountSockets);
            mf.sharedMesh = cableMesh;

            // Dedicated emissive material instance
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
                mr.sharedMaterials = new Material[] { _cachedJacketMaterial, neonInst };
            }
            else if (cfg.Style == CableStyle.FullNeon)
            {
                mr.sharedMaterials = new Material[] { neonInst };
            }
            else
            {
                mr.sharedMaterials = new Material[] { _cachedJacketMaterial };
            }

            // Expanded trigger collider for effortless 3D selection in editor
            BoxCollider bc = cableObj.GetComponent<BoxCollider>() ?? cableObj.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.center = (cfg.LocalPointA + cfg.LocalPointB) * 0.5f + Vector3.down * (cfg.SagAmount * 0.5f);
            Vector3 span = cfg.LocalPointB - cfg.LocalPointA;
            bc.size = new Vector3(
                Mathf.Max(0.8f, Mathf.Abs(span.x)),
                Mathf.Max(1.0f, Mathf.Abs(cfg.SagAmount) + cfg.Radius * 4f),
                Mathf.Max(0.8f, Mathf.Abs(span.z))
            );
        }

        // =========================================================================
        // SECTION 4: 3D INTERACTIVE HANDLES & SURFACE SNAPPING
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
            if (cableObj == null || !cableObj.activeInHierarchy || !PlacedCables.TryGetValue(cableObj, out CableConfig cfg))
            {
                HideCableVisualHandles(cableObj);
                return;
            }

            Vector3 worldA = cableObj.transform.TransformPoint(cfg.LocalPointA);
            Vector3 worldB = cableObj.transform.TransformPoint(cfg.LocalPointB);

            if (!_handleMarkersA.TryGetValue(cableObj, out GameObject handleA) || handleA == null)
            {
                handleA = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                handleA.name = "Cable_Handle_A_" + cableObj.name;
                handleA.transform.localScale = Vector3.one * 0.55f;
                handleA.layer = 0;
                SphereCollider sc = handleA.GetComponent<SphereCollider>();
                if (sc != null) { sc.isTrigger = false; sc.radius = 0.55f; }
                handleA.GetComponent<Renderer>().sharedMaterial = GizmoMaterialCache.CreateSolidMaterial(new Color(0.2f, 1f, 0.4f));
                _handleMarkersA[cableObj] = handleA;
            }

            if (!_handleMarkersB.TryGetValue(cableObj, out GameObject handleB) || handleB == null)
            {
                handleB = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                handleB.name = "Cable_Handle_B_" + cableObj.name;
                handleB.transform.localScale = Vector3.one * 0.55f;
                handleB.layer = 0;
                SphereCollider sc = handleB.GetComponent<SphereCollider>();
                if (sc != null) { sc.isTrigger = false; sc.radius = 0.55f; }
                handleB.GetComponent<Renderer>().sharedMaterial = GizmoMaterialCache.CreateSolidMaterial(new Color(1f, 0.5f, 0.1f));
                _handleMarkersB[cableObj] = handleB;
            }

            handleA.SetActive(true);
            handleB.SetActive(true);
            handleA.transform.position = worldA;
            handleB.transform.position = worldB;
        }

        public static void UpdateAllCableHandles()
        {
            foreach (var kvp in PlacedCables)
            {
                GameObject cable = kvp.Key;
                if (cable == null || !cable.activeInHierarchy)
                {
                    HideCableVisualHandles(cable);
                    continue;
                }
                UpdateCableVisualHandles(cable);
            }
        }

        public static void HideCableVisualHandles(GameObject cableObj)
        {
            if (cableObj == null) return;
            if (_handleMarkersA.TryGetValue(cableObj, out var a) && a != null) a.SetActive(false);
            if (_handleMarkersB.TryGetValue(cableObj, out var b) && b != null) b.SetActive(false);
        }

        public static void HideAllCableHandles()
        {
            foreach (var kvp in _handleMarkersA) if (kvp.Value != null) kvp.Value.SetActive(false);
            foreach (var kvp in _handleMarkersB) if (kvp.Value != null) kvp.Value.SetActive(false);
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

        public static void AddAllCableHandles()
        {
            UpdateAllCableHandles();
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

        public static void SnapHandleToSurface(GameObject cableObj, bool isPointB)
        {
            if (cableObj == null || !PlacedCables.TryGetValue(cableObj, out CableConfig cfg)) return;

            Vector3 currentLocal = isPointB ? cfg.LocalPointB : cfg.LocalPointA;
            Vector3 worldPos = cableObj.transform.TransformPoint(currentLocal);

            Vector3[] castDirs = new Vector3[] { Vector3.down, Vector3.up, Vector3.forward, -Vector3.forward, Vector3.right, -Vector3.right };
            RaycastHit bestHit = default;
            float closestDist = float.MaxValue;
            bool hitFound = false;

            for (int i = 0; i < castDirs.Length; i++)
            {
                if (Physics.Raycast(worldPos + castDirs[i] * -0.5f, castDirs[i], out RaycastHit hit, 8.0f))
                {
                    if (hit.collider != null && hit.collider.gameObject != cableObj && !hit.collider.name.Contains("Handle"))
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
                Vector3 newLocal = cableObj.transform.InverseTransformPoint(bestHit.point);
                if (isPointB) cfg.LocalPointB = newLocal;
                else cfg.LocalPointA = newLocal;

                ApplyCableConfig(cableObj, cfg);
                UpdateCableVisualHandles(cableObj);
                EditorSessionManager.ShowNotification($"Snapped Point {(isPointB ? "B" : "A")} to surface!");
            }
            else
            {
                EditorSessionManager.ShowNotification("No surface found nearby to snap to.");
            }
        }

        public static void UpdateEnergyFlowTick(float dt)
        {
            if (PlacedCables.Count == 0) return;

            foreach (var kvp in PlacedCables)
            {
                GameObject cable = kvp.Key;
                CableConfig cfg = kvp.Value;
                if (cable == null || !cable.activeSelf || cfg == null) continue;
                if (cfg.Style == CableStyle.IndustrialSolid || Mathf.Abs(cfg.EnergyFlowSpeed) < 0.01f) continue;

                MeshRenderer rend = cable.GetComponent<MeshRenderer>();
                if (rend == null) continue;

                // Use sharedMaterials to avoid instantiating new arrays and materials every frame
                Material[] mats = rend.sharedMaterials;
                int neonMatIdx = (cfg.Style == CableStyle.NeonStriped) ? 1 : 0;
                if (mats != null && neonMatIdx < mats.Length && mats[neonMatIdx] != null)
                {
                    Vector2 curOffset = mats[neonMatIdx].mainTextureOffset;
                    curOffset.y -= cfg.EnergyFlowSpeed * dt;
                    mats[neonMatIdx].mainTextureOffset = curOffset;
                }
            }
        }
    }
}