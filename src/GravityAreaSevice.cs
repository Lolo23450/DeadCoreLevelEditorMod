using System;
using System.Collections.Generic;
using UnityEngine;
using Il2Cpp;
using MelonLoader;

namespace DeadCoreEditor
{
    public static class GravityAreaService
    {
        public static GameObject PrefabGravityArea = null;
        public static readonly List<GameObject> PlacedGravityAreas = new List<GameObject>();
        public static readonly Dictionary<GameObject, GravityConfig> PlacedGravityConfigs = new Dictionary<GameObject, GravityConfig>();

        private static Material _volumeBoxMat = null;
        private static Material _arrowMat = null;

        public static void EnsureMaterials()
        {
            if (_volumeBoxMat == null)
            {
                Shader s = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
                _volumeBoxMat = new Material(s)
                {
                    name = "Mat_GravityArea_Preview",
                    color = new Color(0.45f, 0.15f, 0.95f, 0.20f)
                };
            }

            if (_arrowMat == null)
            {
                Shader s = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
                _arrowMat = new Material(s)
                {
                    name = "Mat_GravityArrow_Preview",
                    color = new Color(0.2f, 0.95f, 1f, 0.95f)
                };
            }
        }

        public static GameObject CreateProceduralGravityArea(Vector3 position, Vector3 size, float force = 9.81f)
        {
            EnsureMaterials();

            GameObject go = new GameObject("Custom_Gravity_Area");
            go.transform.position = position;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = size;
            go.layer = 0;

            // Trigger box collider
            BoxCollider bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = Vector3.one;
            bc.center = Vector3.zero;

            // Visual Volume Indicator (Mesh only, NO collider)
            GameObject boxVisual = new GameObject("Volume_Visual_Box");
            boxVisual.transform.SetParent(go.transform, false);
            boxVisual.transform.localPosition = Vector3.zero;
            boxVisual.transform.localScale = Vector3.one;
            boxVisual.layer = 2; // Ignore Raycast

            GameObject tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh cubeMesh = tempCube.GetComponent<MeshFilter>().sharedMesh;
            GameObject.DestroyImmediate(tempCube);

            MeshFilter mf = boxVisual.AddComponent<MeshFilter>();
            mf.sharedMesh = cubeMesh;

            MeshRenderer mr = boxVisual.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _volumeBoxMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // Direction Arrow Shaft & Tip
            CreateDirectionArrow(boxVisual.transform);

            // Attach native DeadCore GravityArea component
            GravityArea area = go.AddComponent<GravityArea>();
            if (area != null)
            {
                area.affectOthers = true;
                area._changeGravity = true;
            }

            GravityConfig cfg = new GravityConfig
            {
                GravityForce = force,
                LocalAxis = Vector3.up,
                AffectOthers = true,
                ChangeGravity = true,
                IsActive = true
            };

            go.GetComponent<GravityArea>()?.Awake();

            ApplyGravityConfig(go, cfg);
            EditorSessionManager.RegisterPlacedObject(go);

            return go;
        }

        private static void CreateDirectionArrow(Transform parent)
        {
            GameObject arrowRoot = new GameObject("Gravity_Arrow_Indicator");
            arrowRoot.transform.SetParent(parent, false);
            arrowRoot.layer = 2;

            // Cylinder Shaft
            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            GameObject.DestroyImmediate(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(arrowRoot.transform, false);
            shaft.transform.localScale = new Vector3(0.08f, 0.35f, 0.08f);
            shaft.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            shaft.GetComponent<Renderer>().sharedMaterial = _arrowMat;
            shaft.layer = 2;

            // Sphere Tip
            GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            GameObject.DestroyImmediate(tip.GetComponent<Collider>());
            tip.transform.SetParent(arrowRoot.transform, false);
            tip.transform.localScale = new Vector3(0.22f, 0.22f, 0.22f);
            tip.transform.localPosition = new Vector3(0f, 0.50f, 0f);
            tip.GetComponent<Renderer>().sharedMaterial = _arrowMat;
            tip.layer = 2;
        }

        public static void ApplyGravityConfig(GameObject go, GravityConfig cfg)
        {
            if (go == null || cfg == null) return;
            PlacedGravityConfigs[go] = cfg.Clone();

            Vector3 worldGrav = cfg.CalculateWorldGravity(go.transform.rotation);

            GravityArea area = go.GetComponentInChildren<GravityArea>(true);
            if (area != null)
            {
                area.gravity = worldGrav;
                area.affectOthers = cfg.AffectOthers;
                area._changeGravity = cfg.ChangeGravity;
                area.enabled = cfg.IsActive;
            }

            BoxCollider bc = go.GetComponent<BoxCollider>();
            if (bc != null)
            {
                bc.enabled = cfg.IsActive;
                bc.isTrigger = true;
            }

            GravityReceiver receiver = go.GetComponentInChildren<GravityReceiver>(true);
            if (receiver != null)
            {
                if (cfg.IsActive) receiver.OnSwitchOn();
                else receiver.OnSwitchOff();
            }

            Transform vis = go.transform.Find("Volume_Visual_Box");
            if (vis != null)
            {
                vis.gameObject.SetActive(EditorSessionManager.IsEditModeActive);
            }
        }

        public static void UpdateAllGravityRotations()
        {
            for (int i = 0; i < PlacedGravityAreas.Count; i++)
            {
                GameObject go = PlacedGravityAreas[i];
                if (go == null || !go.activeInHierarchy) continue;

                if (PlacedGravityConfigs.TryGetValue(go, out var cfg))
                {
                    GravityArea area = go.GetComponentInChildren<GravityArea>(true);
                    if (area != null)
                    {
                        area.gravity = cfg.CalculateWorldGravity(go.transform.rotation);
                    }
                }
            }
        }

        public static void SetVisualsVisible(bool visible)
        {
            for (int i = 0; i < PlacedGravityAreas.Count; i++)
            {
                GameObject go = PlacedGravityAreas[i];
                if (go == null) continue;
                Transform vis = go.transform.Find("Volume_Visual_Box");
                if (vis != null) vis.gameObject.SetActive(visible);
            }
        }
    }
}