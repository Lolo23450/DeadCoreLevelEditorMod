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

        public static void EnsureMaterials()
        {
            if (_volumeBoxMat == null)
            {
                Shader s = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
                _volumeBoxMat = new Material(s)
                {
                    name = "Mat_GravityArea_Preview",
                    color = new Color(0.4f, 0.1f, 0.9f, 0.25f)
                };
            }
        }

        public static GameObject CreateProceduralGravityArea(Vector3 position, Vector3 size, Vector3 direction)
        {
            EnsureMaterials();

            GameObject go = new GameObject("Custom_Gravity_Area");
            go.transform.position = position;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = size;
            go.layer = 0;

            // 1. Ensure any existing colliders are wiped or converted
            Collider[] existing = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null) GameObject.DestroyImmediate(existing[i]);
            }

            // 2. The main Trigger Box Collider for GravityArea
            BoxCollider bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = Vector3.one;
            bc.center = Vector3.zero;

            // 3. Visual Volume Indicator (Mesh only, NO collider)
            GameObject boxVisual = new GameObject("Volume_Visual_Box");
            boxVisual.transform.SetParent(go.transform, false);
            boxVisual.transform.localPosition = Vector3.zero;
            boxVisual.transform.localScale = Vector3.one;
            boxVisual.layer = 2; // Ignore Raycast layer

            // Use a clean primitive mesh without GameObject.CreatePrimitive (which adds a BoxCollider)
            GameObject tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh cubeMesh = tempCube.GetComponent<MeshFilter>().sharedMesh;
            GameObject.DestroyImmediate(tempCube);

            MeshFilter mf = boxVisual.AddComponent<MeshFilter>();
            mf.sharedMesh = cubeMesh;

            MeshRenderer mr = boxVisual.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _volumeBoxMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // 4. Attach native DeadCore GravityArea component
            GravityArea area = go.AddComponent<GravityArea>();
            if (area != null)
            {
                area.gravity = direction;
                area.affectOthers = true;
                area._changeGravity = true;
            }

            GravityConfig cfg = new GravityConfig
            {
                GravityDirection = direction,
                AffectOthers = true,
                ChangeGravity = true,
                IsActive = true
            };

            go.GetComponent<GravityArea>()?.Awake();

            ApplyGravityConfig(go, cfg);
            EditorSessionManager.RegisterPlacedObject(go);

            // Clean up any stray solid colliders that might have been added
            Collider[] allCols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < allCols.Length; i++)
            {
                if (allCols[i] != null && allCols[i].gameObject.name != "Editor_Snapping_Proxy")
                {
                    allCols[i].isTrigger = true;
                }
            }

            return go;
        }

        public static void ApplyGravityConfig(GameObject go, GravityConfig cfg)
        {
            if (go == null || cfg == null) return;
            PlacedGravityConfigs[go] = cfg.Clone();

            GravityArea area = go.GetComponentInChildren<GravityArea>(true);
            if (area != null)
            {
                area.gravity = cfg.GravityDirection;
                area.affectOthers = cfg.AffectOthers;
                area._changeGravity = cfg.ChangeGravity;
                area.enabled = cfg.IsActive;
            }

            BoxCollider bc = go.GetComponent<BoxCollider>();
            if (bc != null)
            {
                bc.enabled = cfg.IsActive;
            }

            // Also support GravityReceiver if present on the target
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