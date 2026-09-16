using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using SceneManager = UnityEngine.SceneManagement.SceneManager;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using Il2CppDeadCore;
using Il2CppDeadCore.UI;

using File = System.IO.File;
using Directory = System.IO.Directory;
using Path = System.IO.Path;

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 1: EDITOR SESSION & LIFECYCLE MANAGER
    // =========================================================================

    public class ClipboardItem
    {
        public string AssetName;
        public Vector3 RelativeOffset;
        public Quaternion Rotation;
        public Vector3 Scale = Vector3.one; // Full 3D scale (X, Y, Z)
        public float CustomParameter;
        public LightConfig LightCfg;
        public ObjectMotionPath MotionPath;
    }
    public static class EditorSessionManager
    {
        // Session and State Tracking
        public static bool CustomLevelSelected = true;
        public static bool IsCustomSessionActive = false;
        public static bool IsEditModeActive = false;
        public static bool IsLevelInitialized = false;

        // Trigger Visualization State
        public static bool AreTriggersVisible = false;

        // Interaction State: Select Mode vs Placement Mode
        public static EditorInteractionMode InteractionMode = EditorInteractionMode.SelectMode;
        public static EditorGizmoMode CurrentGizmoMode = EditorGizmoMode.Select;

        // Asset Catalog References
        public static List<CatalogAsset> AllAssets = new List<CatalogAsset>();
        public static CatalogAsset CurrentAsset = null;
        public static bool IsBlockSelected = false;

        // Harvested Staging Scene Prefab Templates & Materials
        public static Material CachedSceneMaterial = null;
        public static Jumper PrefabJumper = null;
        public static CheckPointScript PrefabCheckPoint = null;
        public static GameObject PrefabHelix = null;
        public static GameObject PrefabTurret = null;

        // Active Selection & Transform Targets (Multi-Selection)
        public static GameObject SelectedObject = null;
        public static List<GameObject> SelectedObjects = new List<GameObject>();
        public static GameObject LastPlacedObject = null;

        // Clipboard System (Ctrl+C / Ctrl+V / Ctrl+D)
        public static List<ClipboardItem> Clipboard = new List<ClipboardItem>();

        // Placement & Snapping Settings
        public static float ActivePlacementScale = 1.0f;
        public static float CurrentGridSnap = 0.0f;
        public static bool AutoAlignToSurface = false;
        public static bool EnforceAdjacentPlacement = true;

        // Rotation Euler Accumulators
        public static float TargetPitch = 0f;
        public static float TargetYaw = 0f;
        public static float TargetRoll = 0f;

        // Contextual Entity Tuning Defaults
        public static float ActiveJumperForce = 25.0f;
        public static float ActiveTurbineSpeed = 35.0f;
        public static float ActiveTurretFireDelay = 1.0f;
        public static float ActiveLaserRotationSpeed = 45.0f;
        public static float DefaultPathSpeed = 3.5f;

        // Motion Path & In-Scene Waypoint Tracking
        public static Dictionary<GameObject, ObjectMotionPath> MotionPaths = new Dictionary<GameObject, ObjectMotionPath>();
        public static readonly Dictionary<GameObject, GameObject> WaypointMarkersA = new Dictionary<GameObject, GameObject>();
        public static readonly Dictionary<GameObject, GameObject> WaypointMarkersB = new Dictionary<GameObject, GameObject>();
        public static readonly Dictionary<GameObject, LineRenderer> WaypointLines = new Dictionary<GameObject, LineRenderer>();

        public static GameObject PathEditTarget = null;
        public static GameObject ParentingChildTarget = null;
        public static GameObject RepositionTarget = null;
        public static Vector3 RepositionStartPosition = Vector3.zero;

        // Property Dictionaries for Placed Entities
        public static List<GameObject> PlacedObjects = new List<GameObject>();
        public static Dictionary<GameObject, PlacedObjectType> PlacedObjectTypes = new Dictionary<GameObject, PlacedObjectType>();
        public static Dictionary<GameObject, int> PlacedParentChildCounts = new Dictionary<GameObject, int>();

        public static List<GameObject> PlacedJumpers = new List<GameObject>();
        public static List<GameObject> PlacedTurbines = new List<GameObject>();
        public static List<GameObject> PlacedLaserBarriers = new List<GameObject>();
        public static List<BoxCollider> PlacedLaserColliders = new List<BoxCollider>();
        public static List<GameObject> PlacedRotatingLasers = new List<GameObject>();
        public static List<CheckPointScript> PlacedCheckpoints = new List<CheckPointScript>();
        public static GameObject PlacedGoalGate = null;
        private static readonly Dictionary<GameObject, Helix> _cachedHelixScripts = new Dictionary<GameObject, Helix>();

        public static Dictionary<GameObject, float> JumperForces = new Dictionary<GameObject, float>();
        public static Dictionary<GameObject, float> TurbineSpeeds = new Dictionary<GameObject, float>();
        public static Dictionary<GameObject, float> TurretFireDelays = new Dictionary<GameObject, float>();
        public static Dictionary<GameObject, float> LaserRotationSpeeds = new Dictionary<GameObject, float>();
        public static Dictionary<GameObject, LightConfig> PlacedLights = new Dictionary<GameObject, LightConfig>();

        // History Undo/Redo Stacks
        public static Stack<HistoryRecord> UndoHistory = new Stack<HistoryRecord>();
        public static Stack<HistoryRecord> RedoHistory = new Stack<HistoryRecord>();

        // Gameplay Playtest & Checkpoint Timers
        public static float LevelTimer = 0f;
        public static bool IsLevelCompleted = false;
        public static CheckPointScript ActiveCustomCheckpoint = null;
        public static float CachedVoidDeathY = -140f;
        private static float _lightRefreshTimer = 0f;

        // Player Entity Caches
        private static GameObject _cachedPlayer = null;
        private static CharacterController _cachedCharacterController = null;
        public static Camera PlayerCameraInstance = null;
        public static CharacterController PlayerControllerInstance = null;
        public static Vector3 FrozenPlayerPosition = Vector3.zero;
        public static Quaternion FrozenPlayerRotation = Quaternion.identity;
        public static Vector3 LevelSpawnPosition = new Vector3(-241f, -95f, -6f);

        // Visual Selection Highlight Pool
        private static readonly List<GameObject> _highlightBoxes = new List<GameObject>();
        private static readonly List<GameObject> _selectionBeacons = new List<GameObject>();

        public struct PlaytestTransformSnapshot
        {
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
        }

        public static readonly Dictionary<GameObject, PlaytestTransformSnapshot> PlaytestSnapshots = new Dictionary<GameObject, PlaytestTransformSnapshot>();

        /// <summary>
        /// Captures the exact design-time local transforms of all placed objects and their sub-components.
        /// </summary>
        public static void CapturePlaytestSnapshots()
        {
            PlaytestSnapshots.Clear();
            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                GameObject obj = PlacedObjects[i];
                if (obj == null) continue;

                Transform[] allTransforms = obj.GetComponentsInChildren<Transform>(true);
                for (int t = 0; t < allTransforms.Length; t++)
                {
                    Transform tr = allTransforms[t];
                    if (tr == null || tr.name == "Editor_Snapping_Proxy") continue;

                    PlaytestSnapshots[tr.gameObject] = new PlaytestTransformSnapshot
                    {
                        LocalPosition = tr.localPosition,
                        LocalRotation = tr.localRotation,
                        LocalScale = tr.localScale
                    };
                }
            }
        }

        /// <summary>
        /// Restores all objects back to their pre-playtest positions, rotations, scales, and physics states.
        /// </summary>
        public static void RestorePlaytestSnapshots()
        {
            if (PlaytestSnapshots.Count == 0) return;

            foreach (var kvp in PlaytestSnapshots)
            {
                GameObject go = kvp.Key;
                if (go == null) continue;

                PlaytestTransformSnapshot snap = kvp.Value;
                Transform tr = go.transform;

                // 1. Freeze rigidbodies to prevent physics fights during reset
                Rigidbody rb = go.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    if (IsEditModeActive)
                    {
                        rb.isKinematic = true;
                    }
                }

                // 2. Restore exact local transform
                tr.localPosition = snap.LocalPosition;
                tr.localRotation = snap.LocalRotation;
                tr.localScale = snap.LocalScale;

                if (rb != null)
                {
                    rb.position = tr.position;
                    rb.rotation = tr.rotation;
                }

                // 3. Reactivate if deactivated during playtest
                if (!go.activeSelf && PlacedObjects.Contains(go))
                {
                    go.SetActive(true);
                }
            }

            // 4. Ensure motion path targets cleanly match Point A
            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                GameObject obj = PlacedObjects[i];
                if (obj == null) continue;

                StudioGizmoController.InvalidateCachedCenter(obj);

                if (MotionPaths.TryGetValue(obj, out var path) && path != null)
                {
                    obj.transform.position = path.PointA;
                }
            }

            UpdateSelectionHighlight();
            StudioUIManager.RefreshInspectorValues();
        }

        // =========================================================================
        // KEYBIND PARENTING WORKFLOW (Ctrl + P / Alt + P)
        // =========================================================================

        public static bool IsDescendantOf(GameObject parentNode, GameObject potentialChild)
        {
            if (parentNode == null || potentialChild == null) return false;
            Transform curr = potentialChild.transform;
            while (curr != null)
            {
                if (curr.gameObject == parentNode) return true;
                curr = curr.parent;
            }
            return false;
        }

        public static void ParentSelectedObjects()
        {
            if (SelectedObjects == null || SelectedObjects.Count == 0)
            {
                ShowNotification("Nothing selected to parent.");
                return;
            }

            // 1. Multi-Selection Mode: Parent all objects to the last selected object
            if (SelectedObjects.Count >= 2)
            {
                GameObject parentTarget = SelectedObject;
                if (parentTarget == null) return;

                int count = 0;
                for (int i = 0; i < SelectedObjects.Count; i++)
                {
                    GameObject child = SelectedObjects[i];
                    if (child == null || child == parentTarget) continue;
                    if (IsWaypointMarker(child, out _, out _)) continue;

                    // Prevent cyclic parenting loops
                    if (IsDescendantOf(child, parentTarget))
                    {
                        ShowNotification($"Cannot parent '{child.name}': Cyclic loop detected.");
                        continue;
                    }

                    GameObject oldParent = child.transform.parent != null ? child.transform.parent.gameObject : null;
                    if (oldParent == parentTarget) continue;

                    child.transform.SetParent(parentTarget.transform, true);

                    if (oldParent != null) RecalculateParentChildCount(oldParent);
                    RecalculateParentChildCount(parentTarget);

                    StudioGizmoController.InvalidateCachedCenter(child);

                    UndoHistory.Push(new HistoryRecord
                    {
                        ActionType = HistoryActionType.Parenting,
                        TargetObject = child,
                        PreviousParent = oldParent,
                        NewParent = parentTarget
                    });
                    count++;
                }

                ParentingChildTarget = null;
                RedoHistory.Clear();
                StudioUIManager.RefreshHierarchy();
                ShowNotification($"Parented {count} object(s) under '{parentTarget.name}' [Ctrl+P]");
            }
            // 2. Two-Step Mode: Single object selection
            else
            {
                GameObject current = SelectedObject;

                if (ParentingChildTarget == null)
                {
                    ParentingChildTarget = current;
                    ShowNotification($"Child locked: '{current.name}'. Select parent & press Ctrl+P.");
                }
                else if (ParentingChildTarget == current)
                {
                    ParentingChildTarget = null;
                    ShowNotification("Parenting cancelled.");
                }
                else
                {
                    // Current is the parent target, ParentingChildTarget is the child
                    if (IsDescendantOf(ParentingChildTarget, current))
                    {
                        ShowNotification("Cannot parent: Cyclic loop detected.");
                        ParentingChildTarget = null;
                        return;
                    }

                    GameObject oldParent = ParentingChildTarget.transform.parent != null ? ParentingChildTarget.transform.parent.gameObject : null;
                    ParentingChildTarget.transform.SetParent(current.transform, true);

                    if (oldParent != null) RecalculateParentChildCount(oldParent);
                    RecalculateParentChildCount(current);

                    StudioGizmoController.InvalidateCachedCenter(ParentingChildTarget);

                    UndoHistory.Push(new HistoryRecord
                    {
                        ActionType = HistoryActionType.Parenting,
                        TargetObject = ParentingChildTarget,
                        PreviousParent = oldParent,
                        NewParent = current
                    });

                    RedoHistory.Clear();
                    ShowNotification($"Parented '{ParentingChildTarget.name}' under '{current.name}' [Ctrl+P]");
                    ParentingChildTarget = null;
                    StudioUIManager.RefreshHierarchy();
                }
            }
        }

        public static void UnparentSelectedObjects()
        {
            if (SelectedObjects == null || SelectedObjects.Count == 0)
            {
                ShowNotification("Nothing selected to unparent.");
                return;
            }

            int count = 0;
            for (int i = 0; i < SelectedObjects.Count; i++)
            {
                GameObject child = SelectedObjects[i];
                if (child == null || child.transform.parent == null) continue;
                if (IsWaypointMarker(child, out _, out _)) continue;

                GameObject oldParent = child.transform.parent.gameObject;
                child.transform.SetParent(null, true);

                RecalculateParentChildCount(oldParent);
                StudioGizmoController.InvalidateCachedCenter(child);

                UndoHistory.Push(new HistoryRecord
                {
                    ActionType = HistoryActionType.Parenting,
                    TargetObject = child,
                    PreviousParent = oldParent,
                    NewParent = null
                });

                count++;
            }

            ParentingChildTarget = null;
            RedoHistory.Clear();
            StudioUIManager.RefreshHierarchy();
            ShowNotification(count > 0 ? $"Unparented {count} object(s) to root [Alt+P]" : "Selected objects are already at root.");
        }

        public static void SetSimulationActive(bool active)
        {
            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                GameObject obj = PlacedObjects[i];
                if (obj == null) continue;

                // 1. Turret scripts
                TurretScript[] ts = obj.GetComponentsInChildren<TurretScript>(true);
                for (int s = 0; s < ts.Length; s++)
                {
                    if (ts[s] != null) ts[s].enabled = active;
                }

                // 2. Freeze all Animators and Animations (Checkpoints, Gates, Platforms)
                // This prevents Unity animations from locking transform.position in Edit Mode
                Animator[] animators = obj.GetComponentsInChildren<Animator>(true);
                for (int a = 0; a < animators.Length; a++)
                {
                    if (animators[a] != null) animators[a].enabled = active;
                }

                Animation[] animations = obj.GetComponentsInChildren<Animation>(true);
                for (int a = 0; a < animations.Length; a++)
                {
                    if (animations[a] != null) animations[a].enabled = active;
                }

                // 3. Make all rigidbodies kinematic in Edit Mode so they don't fall or resist gizmo moves
                Rigidbody[] rbs = obj.GetComponentsInChildren<Rigidbody>(true);
                for (int r = 0; r < rbs.Length; r++)
                {
                    if (rbs[r] != null)
                    {
                        rbs[r].isKinematic = !active;
                        if (!active)
                        {
                            rbs[r].velocity = Vector3.zero;
                            rbs[r].angularVelocity = Vector3.zero;
                        }
                    }
                }
            }
        }

        // Tracks per-jumper active state (for Inspector toggling)
        public static Dictionary<GameObject, bool> JumperActiveStates = new Dictionary<GameObject, bool>();

        public static void SetJumperSimulationActive(bool active)
        {
            for (int i = 0; i < PlacedJumpers.Count; i++)
            {
                GameObject obj = PlacedJumpers[i];
                if (obj == null) continue;

                // Check if this specific pad was manually toggled off in the inspector
                bool isPadEnabled = true;
                if (JumperActiveStates.TryGetValue(obj, out bool padState))
                {
                    isPadEnabled = padState;
                }

                bool shouldBeActive = active && isPadEnabled;

                // 1. Toggle Jumper script and FX
                Jumper[] jumpers = obj.GetComponentsInChildren<Jumper>(true);
                for (int j = 0; j < jumpers.Length; j++)
                {
                    if (jumpers[j] != null)
                    {
                        jumpers[j].enabled = shouldBeActive;
                        if (jumpers[j].Fx != null) jumpers[j].Fx.SetActive(shouldBeActive);
                    }
                }

                // 2. Disable trigger colliders so Unity doesn't execute OnTriggerEnter
                Collider[] cols = obj.GetComponentsInChildren<Collider>(true);
                for (int c = 0; c < cols.Length; c++)
                {
                    if (cols[c] != null && cols[c].isTrigger && cols[c].gameObject.name != "Editor_Snapping_Proxy")
                    {
                        cols[c].enabled = shouldBeActive;
                    }
                }
            }
        }

        public static void ApplyJumperActive(GameObject jumperObj, bool active)
        {
            if (jumperObj == null) return;
            JumperActiveStates[jumperObj] = active;

            // Only activate if not currently in Edit Mode
            bool effectiveActive = active && !IsEditModeActive;

            Jumper[] jumpers = jumperObj.GetComponentsInChildren<Jumper>(true);
            foreach (var jc in jumpers)
            {
                if (jc == null) continue;
                jc.enabled = effectiveActive;
                if (jc.Fx != null) jc.Fx.SetActive(effectiveActive);
            }

            Collider[] cols = jumperObj.GetComponentsInChildren<Collider>(true);
            foreach (var col in cols)
            {
                if (col != null && col.isTrigger && col.gameObject.name != "Editor_Snapping_Proxy")
                {
                    col.enabled = effectiveActive;
                }
            }
        }

        public static void SetSnappingProxiesActive(bool active)
        {
            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                if (PlacedObjects[i] == null) continue;
                Transform proxy = PlacedObjects[i].transform.Find("Editor_Snapping_Proxy");
                if (proxy != null) proxy.gameObject.SetActive(active);
            }
        }

        // =========================================================================
        // MODE TOGGLING & MULTI-SELECTION WORKFLOW
        // =========================================================================

        public static void SetInteractionMode(EditorInteractionMode mode)
        {
            InteractionMode = mode;

            if (mode == EditorInteractionMode.SelectMode)
            {
                IsBlockSelected = false;
                PlacementHologramController.DestroyPreview();
                ShowNotification("Mode: [SELECT]");
            }
            else
            {
                SelectObject(null);
                if (CurrentAsset != null)
                {
                    IsBlockSelected = true;
                    PlacementHologramController.SpawnHologram(CurrentAsset);
                    ShowNotification($"Mode: [PLACEMENT] - Placing '{CurrentAsset.DisplayName}'");
                }
                else
                {
                    ShowNotification("Mode: [PLACEMENT] - Choose an asset from catalog");
                }
            }

            StudioUIManager.RefreshModeDisplay();
        }

        public static void SelectObject(GameObject obj, bool isAdditive = false)
        {
            if (isAdditive)
            {
                if (obj != null)
                {
                    if (SelectedObjects.Contains(obj)) SelectedObjects.Remove(obj);
                    else SelectedObjects.Add(obj);
                }
            }
            else
            {
                SelectedObjects.Clear();
                if (obj != null) SelectedObjects.Add(obj);
            }

            SelectedObject = SelectedObjects.Count > 0 ? SelectedObjects[SelectedObjects.Count - 1] : null;

            if (SelectedObject != null && InteractionMode == EditorInteractionMode.PlacementMode)
            {
                InteractionMode = EditorInteractionMode.SelectMode;
                IsBlockSelected = false;
                PlacementHologramController.DestroyPreview();
                StudioUIManager.RefreshModeDisplay();
            }

            UpdateSelectionHighlight();
            StudioUIManager.NotifyObjectSelected(SelectedObject);
        }

        public static void EquipAsset(CatalogAsset asset)
        {
            if (asset == null) return;
            CurrentAsset = asset;
            ActivePlacementScale = asset.DefaultScale;
            SetInteractionMode(EditorInteractionMode.PlacementMode);
            PlacementHologramController.SpawnHologram(asset);
            ShowNotification($"Equipped: {asset.DisplayName} (Scale: {ActivePlacementScale:F2}x)");
        }

        private static Material _cachedHighlightMat = null;
        private static Material GetHighlightMaterial()
        {
            if (_cachedHighlightMat == null)
            {
                Shader s = Shader.Find("Unlit/Color") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
                _cachedHighlightMat = new Material(s);
                _cachedHighlightMat.color = new Color(0.1f, 0.85f, 1f, 0.45f);
            }
            return _cachedHighlightMat;
        }

        private static Material _cachedSiblingHighlightMat = null;
        private static Material GetSiblingHighlightMaterial()
        {
            if (_cachedSiblingHighlightMat == null)
            {
                Shader s = Shader.Find("Unlit/Color") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
                _cachedSiblingHighlightMat = new Material(s);
                _cachedSiblingHighlightMat.color = new Color(0.04f, 0.22f, 0.48f, 0.35f);
            }
            return _cachedSiblingHighlightMat;
        }

        // =========================================================================
        // PURE 12-LINE HOLLOW WIREFRAME (ZERO SOLID FACES, ZERO COLLIDERS)
        // =========================================================================
        public static void UpdateSelectionHighlight()
        {
            CleanHighlightPool();

            if (SelectedObjects.Count == 0 || !IsEditModeActive || InteractionMode != EditorInteractionMode.SelectMode)
            {
                return;
            }

            Material primaryMat = GetHighlightMaterial();

            for (int i = 0; i < SelectedObjects.Count; i++)
            {
                GameObject obj = SelectedObjects[i];
                if (obj == null || !obj.activeSelf) continue;
                if (IsWaypointMarker(obj, out _, out _)) continue;

                Bounds b = PlacementHologramController.CalculateOptimizedProxyBounds(obj);
                Vector3 worldCenter = obj.transform.TransformPoint(b.center);

                // Creates a clean, hollow, 12-line wireframe box with no solid faces or colliders
                GameObject wireBox = CreateHollowWireframeBox(worldCenter, obj.transform.rotation, Vector3.Scale(b.size * 1.02f, obj.transform.lossyScale), primaryMat);
                _highlightBoxes.Add(wireBox);
            }
        }

        // =========================================================================
        // THICK VOLUMETRIC 3D WIREFRAME (12 SEAMLESS BEAMS, ZERO COLLIDERS)
        // =========================================================================
        private static GameObject CreateHollowWireframeBox(Vector3 pos, Quaternion rot, Vector3 size, Material mat)
        {
            GameObject wireObj = new GameObject("Studio_Selection_Wireframe");
            wireObj.transform.position = pos;
            wireObj.transform.rotation = rot;
            wireObj.layer = 2; // Ignore Raycast

            MeshFilter mf = wireObj.AddComponent<MeshFilter>();
            MeshRenderer mr = wireObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // Dynamically scale beam thickness: at least 0.10m thick on small props, up to 0.40m on large platforms
            float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float t = Mathf.Clamp(maxDim * 0.025f, 0.10f, 0.40f);

            Vector3 h = size * 0.5f;

            List<Vector3> verts = new List<Vector3>(96);
            List<int> tris = new List<int>(144);

            // 4 Beams along X
            AddBeam(new Vector3(0f, -h.y, -h.z), new Vector3(size.x + t, t, t), verts, tris);
            AddBeam(new Vector3(0f, h.y, -h.z), new Vector3(size.x + t, t, t), verts, tris);
            AddBeam(new Vector3(0f, -h.y, h.z), new Vector3(size.x + t, t, t), verts, tris);
            AddBeam(new Vector3(0f, h.y, h.z), new Vector3(size.x + t, t, t), verts, tris);

            // 4 Beams along Y (Vertical pillars)
            AddBeam(new Vector3(-h.x, 0f, -h.z), new Vector3(t, size.y + t, t), verts, tris);
            AddBeam(new Vector3(h.x, 0f, -h.z), new Vector3(t, size.y + t, t), verts, tris);
            AddBeam(new Vector3(-h.x, 0f, h.z), new Vector3(t, size.y + t, t), verts, tris);
            AddBeam(new Vector3(h.x, 0f, h.z), new Vector3(t, size.y + t, t), verts, tris);

            // 4 Beams along Z
            AddBeam(new Vector3(-h.x, -h.y, 0f), new Vector3(t, t, size.z + t), verts, tris);
            AddBeam(new Vector3(h.x, -h.y, 0f), new Vector3(t, t, size.z + t), verts, tris);
            AddBeam(new Vector3(-h.x, h.y, 0f), new Vector3(t, t, size.z + t), verts, tris);
            AddBeam(new Vector3(h.x, h.y, 0f), new Vector3(t, t, size.z + t), verts, tris);

            Mesh m = new Mesh();
            m.name = "Thick_Wireframe_Box_Mesh";
            m.vertices = verts.ToArray();
            m.triangles = tris.ToArray();
            m.RecalculateNormals();
            m.RecalculateBounds();
            mf.sharedMesh = m;

            return wireObj;
        }

        private static void AddBeam(Vector3 center, Vector3 beamSize, List<Vector3> verts, List<int> tris)
        {
            int baseIdx = verts.Count;
            Vector3 bh = beamSize * 0.5f;

            // 8 vertices per beam
            verts.Add(center + new Vector3(-bh.x, -bh.y, -bh.z));
            verts.Add(center + new Vector3(bh.x, -bh.y, -bh.z));
            verts.Add(center + new Vector3(bh.x, bh.y, -bh.z));
            verts.Add(center + new Vector3(-bh.x, bh.y, -bh.z));
            verts.Add(center + new Vector3(-bh.x, -bh.y, bh.z));
            verts.Add(center + new Vector3(bh.x, -bh.y, bh.z));
            verts.Add(center + new Vector3(bh.x, bh.y, bh.z));
            verts.Add(center + new Vector3(-bh.x, bh.y, bh.z));

            // 12 triangles (6 faces)
            int[] f = new int[]
            {
                0, 2, 1, 0, 3, 2, // Front
                5, 6, 4, 4, 6, 7, // Back
                0, 7, 3, 0, 4, 7, // Left
                1, 2, 6, 1, 6, 5, // Right
                3, 6, 2, 3, 7, 6, // Top
                0, 1, 5, 0, 5, 4  // Bottom
            };

            for (int i = 0; i < f.Length; i++)
            {
                tris.Add(baseIdx + f[i]);
            }
        }

        private static void CleanHighlightPool()
        {
            for (int i = 0; i < _highlightBoxes.Count; i++)
            {
                if (_highlightBoxes[i] != null) GameObject.Destroy(_highlightBoxes[i]);
            }
            _highlightBoxes.Clear();

            for (int i = 0; i < _selectionBeacons.Count; i++)
            {
                if (_selectionBeacons[i] != null) GameObject.Destroy(_selectionBeacons[i]);
            }
            _selectionBeacons.Clear();
        }

        public static void ShowNotification(string msg)
        {
            StudioUIManager.SetNotificationText(msg);
        }

        // =========================================================================
        // COPY, PASTE, DUPLICATE & DELETE WORKFLOW
        // =========================================================================

        public static void CopySelectedObjects()
        {
            if (SelectedObjects.Count == 0)
            {
                ShowNotification("Nothing selected to copy.");
                return;
            }

            Clipboard.Clear();
            Vector3 groupCenter = StudioGizmoController.GetObjectCenter(SelectedObject);

            for (int i = 0; i < SelectedObjects.Count; i++)
            {
                GameObject obj = SelectedObjects[i];
                if (obj == null) continue;
                if (IsWaypointMarker(obj, out _, out _)) continue;

                float param = 0f;
                if (JumperForces.ContainsKey(obj)) param = JumperForces[obj];
                else if (TurbineSpeeds.ContainsKey(obj)) param = TurbineSpeeds[obj];
                else if (TurretFireDelays.ContainsKey(obj)) param = TurretFireDelays[obj];
                else if (LaserRotationSpeeds.ContainsKey(obj)) param = LaserRotationSpeeds[obj];

                LightConfig lCfg = PlacedLights.ContainsKey(obj) ? PlacedLights[obj].Clone() : null;
                ObjectMotionPath mPath = MotionPaths.ContainsKey(obj) ? MotionPaths[obj].Clone() : null;

                string rawName = obj.name.StartsWith("Custom_") ? obj.name.Substring(7) : obj.name;

                Clipboard.Add(new ClipboardItem
                {
                    AssetName = rawName,
                    RelativeOffset = obj.transform.position - groupCenter,
                    Rotation = obj.transform.rotation,
                    Scale = obj.transform.localScale, // Saves full Vector3 scale
                    CustomParameter = param,
                    LightCfg = lCfg,
                    MotionPath = mPath
                });
            }

            ShowNotification($"Copied {Clipboard.Count} object(s) [Ctrl+C]");
        }

        public static void PasteClipboardObjects()
        {
            if (Clipboard.Count == 0)
            {
                ShowNotification("Clipboard empty. Press Ctrl+C to copy objects.");
                return;
            }

            Vector3 pasteOrigin;
            if (InteractionMode == EditorInteractionMode.PlacementMode)
            {
                pasteOrigin = PlacementHologramController.TargetPosition;
            }
            else
            {
                Vector3 basePos = (SelectedObject != null) ? SelectedObject.transform.position : LevelSpawnPosition;
                pasteOrigin = basePos + new Vector3(2.5f, 0f, 2.5f);
            }

            SelectedObjects.Clear();

            for (int i = 0; i < Clipboard.Count; i++)
            {
                ClipboardItem item = Clipboard[i];
                Vector3 spawnPos = pasteOrigin + item.RelativeOffset;

                GameObject pasted = SpawnAssetByName(item.AssetName, spawnPos, item.Scale, item.Rotation);
                if (pasted != null)
                {
                    string low = item.AssetName.ToLower();
                    if (item.CustomParameter > 0f)
                    {
                        if (low.Contains("jumper")) ApplyJumperForce(pasted, item.CustomParameter);
                        else if (low.Contains("helix")) ApplyTurbineSpeed(pasted, item.CustomParameter);
                        else if (low.Contains("turret")) ApplyTurretSettings(pasted, item.CustomParameter);
                    }
                    if (low.Contains("rotating") && item.CustomParameter != 0f)
                    {
                        LaserRotationSpeeds[pasted] = item.CustomParameter;
                    }
                    if (item.LightCfg != null)
                    {
                        ApplyLightConfig(pasted, item.LightCfg);
                    }
                    if (item.MotionPath != null)
                    {
                        Vector3 delta = spawnPos - item.MotionPath.PointA;
                        MotionPaths[pasted] = new ObjectMotionPath
                        {
                            PointA = spawnPos,
                            PointB = item.MotionPath.PointB + delta,
                            Speed = item.MotionPath.Speed,
                            RotationSpeed = item.MotionPath.RotationSpeed,
                            RotationAxis = item.MotionPath.RotationAxis,
                            CustomAxis = item.MotionPath.CustomAxis,
                            IsActive = true
                        };
                    }

                    RegisterPlacedObject(pasted);
                    SelectedObjects.Add(pasted);

                    UndoHistory.Push(new HistoryRecord
                    {
                        ActionType = HistoryActionType.Placement,
                        TargetObject = pasted,
                        AssetName = item.AssetName,
                        Position = spawnPos,
                        Rotation = item.Rotation,
                        Scale = item.Scale.x,
                        ScaleVector = item.Scale,
                        CustomParameter = item.CustomParameter
                    });
                }
            }

            RedoHistory.Clear();
            SelectedObject = SelectedObjects.Count > 0 ? SelectedObjects[SelectedObjects.Count - 1] : null;

            UpdateSelectionHighlight();
            StudioUIManager.NotifyObjectSelected(SelectedObject);
            StudioUIManager.RefreshHierarchy();

            ShowNotification($"Pasted {SelectedObjects.Count} object(s) [Ctrl+V]");
        }

        public static void DuplicateSelectedObjects()
        {
            if (SelectedObjects.Count == 0)
            {
                ShowNotification("Nothing selected to duplicate.");
                return;
            }

            CopySelectedObjects();
            float step = CurrentGridSnap > 0.01f ? CurrentGridSnap : 1.5f;
            Vector3 offset = new Vector3(step, 0f, step);

            List<ClipboardItem> dupList = new List<ClipboardItem>(Clipboard);
            Vector3 basePos = (SelectedObject != null) ? SelectedObject.transform.position : LevelSpawnPosition;
            Vector3 pasteOrigin = basePos + offset;

            SelectedObjects.Clear();

            for (int i = 0; i < dupList.Count; i++)
            {
                ClipboardItem item = dupList[i];
                Vector3 spawnPos = pasteOrigin + item.RelativeOffset;

                GameObject pasted = SpawnAssetByName(item.AssetName, spawnPos, item.Scale, item.Rotation);
                if (pasted != null)
                {
                    string low = item.AssetName.ToLower();
                    if (item.CustomParameter > 0f)
                    {
                        if (low.Contains("jumper")) ApplyJumperForce(pasted, item.CustomParameter);
                        else if (low.Contains("helix")) ApplyTurbineSpeed(pasted, item.CustomParameter);
                        else if (low.Contains("turret")) ApplyTurretSettings(pasted, item.CustomParameter);
                    }
                    if (low.Contains("rotating") && item.CustomParameter != 0f)
                    {
                        LaserRotationSpeeds[pasted] = item.CustomParameter;
                    }
                    if (item.LightCfg != null)
                    {
                        ApplyLightConfig(pasted, item.LightCfg);
                    }
                    if (item.MotionPath != null)
                    {
                        Vector3 delta = spawnPos - item.MotionPath.PointA;
                        MotionPaths[pasted] = new ObjectMotionPath
                        {
                            PointA = spawnPos,
                            PointB = item.MotionPath.PointB + delta,
                            Speed = item.MotionPath.Speed,
                            IsActive = true
                        };
                    }

                    RegisterPlacedObject(pasted);
                    SelectedObjects.Add(pasted);

                    UndoHistory.Push(new HistoryRecord
                    {
                        ActionType = HistoryActionType.Placement,
                        TargetObject = pasted,
                        AssetName = item.AssetName,
                        Position = spawnPos,
                        Rotation = item.Rotation,
                        Scale = item.Scale.x,
                        ScaleVector = item.Scale, // Guarda el Vector3 completo
                        CustomParameter = item.CustomParameter
                    });
                }
            }

            RedoHistory.Clear();
            SelectedObject = SelectedObjects.Count > 0 ? SelectedObjects[SelectedObjects.Count - 1] : null;

            UpdateSelectionHighlight();
            StudioUIManager.NotifyObjectSelected(SelectedObject);
            StudioUIManager.RefreshHierarchy();

            ShowNotification($"Duplicated {SelectedObjects.Count} object(s) [Ctrl+D]");
        }

        public static void DeleteSelectedObjects()
        {
            if (SelectedObjects.Count == 0) return;

            var toDelete = new List<GameObject>(SelectedObjects);
            for (int i = 0; i < toDelete.Count; i++)
            {
                GameObject target = toDelete[i];
                if (target == null) continue;

                if (IsWaypointMarker(target, out GameObject owner, out _))
                {
                    if (owner != null)
                    {
                        MotionPaths.Remove(owner);
                        DestroyWaypointVisuals(owner);
                        ShowNotification($"Removed motion path from '{owner.name}'");
                    }
                    continue;
                }

                DeleteSpecifiedObject(target);
            }

            SelectedObjects.Clear();
            SelectedObject = null;
            UpdateSelectionHighlight();
            StudioUIManager.NotifyObjectSelected(null);
            StudioUIManager.RefreshHierarchy();

            ShowNotification($"Deleted {toDelete.Count} object(s) [Supr]");
        }

        // =========================================================================
        // EDIT MODE LIFECYCLE & INPUT DISPATCH
        // =========================================================================

        public static void ToggleEditMode()
        {
            IsEditModeActive = !IsEditModeActive;
            SetSpotlightMeshesVisible(IsEditModeActive);

            GameObject player = FindPlayerEntity();
            SetSimulationActive(!IsEditModeActive);
            SetSnappingProxiesActive(IsEditModeActive);
            SetJumperSimulationActive(!IsEditModeActive);

            if (IsEditModeActive)
            {
                // EXITING PLAYTEST -> Reset all objects to their original positions/rotations
                RestorePlaytestSnapshots();

                if (player != null)
                {
                    FrozenPlayerPosition = player.transform.position;
                    FrozenPlayerRotation = player.transform.rotation;
                    SetPlayerControlsActive(false);
                }

                PlayerCameraInstance = Camera.main;

                EditorViewportCamera.InitializeCamera(PlayerCameraInstance);
                StudioUIManager.InitializeUI();
                StudioUIManager.SetUIVisible(true);

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                SetInteractionMode(EditorInteractionMode.SelectMode);
            }
            else
            {
                // ENTERING PLAYTEST -> Snapshot current state and reset timers
                CapturePlaytestSnapshots();
                LevelTimer = 0f;
                IsLevelCompleted = false;

                EditorViewportCamera.DestroyCamera();
                PlacementHologramController.DestroyPreview();
                StudioGizmoController.DestroyGizmo();

                CleanHighlightPool();
                HideAllWaypointMarkers();

                StudioUIManager.SetUIVisible(false);

                if (PlayerCameraInstance != null) PlayerCameraInstance.enabled = true;

                SetPlayerControlsActive(true);

                if (!IsLevelCompleted)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
        }

        public static void ResetSession()
        {
            IsCustomSessionActive = false;
            IsEditModeActive = false;
            IsLevelInitialized = false;
            IsLevelCompleted = false;
            LevelTimer = 0f;

            _cachedPlayer = null;
            _cachedCharacterController = null;
            SceneHarvestingService.NativeSceneSun = null;

            EditorViewportCamera.DestroyCamera();
            PlacementHologramController.DestroyPreview();
            StudioGizmoController.DestroyGizmo();
            StudioGizmoController.ClearAllCachedCentroids();

            SceneHarvestingService.CleanupProceduralResources();
            DestroyAllWaypointVisuals();

            PlayerCameraInstance = null;
            PlayerControllerInstance = null;

            AllAssets.Clear();
            CurrentAsset = null;
            IsBlockSelected = false;
            SelectedObject = null;
            SelectedObjects.Clear();
            Clipboard.Clear();

            PrefabJumper = null;
            PrefabCheckPoint = null;
            PrefabHelix = null;
            PrefabTurret = null;

            ClearAllPlacedObjects();
            CleanHighlightPool();

            _lightRefreshTimer = 0f;
            TargetPitch = 0f;
            TargetYaw = 0f;
            TargetRoll = 0f;

            ActiveJumperForce = 25.0f;
            ActiveTurbineSpeed = 35.0f;
            ActiveTurretFireDelay = 1.0f;
            ActivePlacementScale = 1.0f;
            CurrentGridSnap = 0.0f;
            AutoAlignToSurface = false;
            InteractionMode = EditorInteractionMode.SelectMode;
        }

        public static void UpdateSession()
        {
            if (!IsLevelInitialized) return;

            if (_lightRefreshTimer > 0f)
            {
                _lightRefreshTimer -= Time.deltaTime;
                if (_lightRefreshTimer <= 0f) ForceRefreshAllLights();
            }

            if (SelectedObject != null && PlacedObjectTypes.TryGetValue(SelectedObject, out var selType) && selType == PlacedObjectType.Sunlight)
            {
                Light activeSun = RenderSettings.sun;
                if (activeSun != null)
                {
                    activeSun.transform.rotation = SelectedObject.transform.rotation;
                }
            }

            GameObject player = FindPlayerEntity();
            CharacterController cc = GetPlayerController();

            UpdateObjectMotionPaths(player, cc);

            if (!IsEditModeActive)
            {
                float dt = Time.deltaTime;

                for (int i = 0; i < PlacedRotatingLasers.Count; i++)
                {
                    GameObject obj = PlacedRotatingLasers[i];
                    if (obj == null || !obj.activeSelf) continue;
                    float speed = LaserRotationSpeeds.ContainsKey(obj) ? LaserRotationSpeeds[obj] : ActiveLaserRotationSpeed;
                    obj.transform.Rotate(Vector3.up, speed * dt, Space.Self);
                }

                if (player != null)
                {
                    CheckVoidFall(player);
                    CheckHelixWindPushing(player, cc);
                    CheckLaserBarriers(player, cc);
                    CheckGoalTriggerArrival(player);

                    Vector3 pPos = player.transform.position;
                    for (int i = 0; i < PlacedCheckpoints.Count; i++)
                    {
                        CheckPointScript cp = PlacedCheckpoints[i];
                        if (cp != null && cp.gameObject.activeSelf)
                        {
                            if ((pPos - cp.transform.position).sqrMagnitude < 9.0f)
                            {
                                if (ActiveCustomCheckpoint != cp)
                                {
                                    ActiveCustomCheckpoint = cp;
                                }
                            }
                        }
                    }
                }

                if (!IsLevelCompleted)
                {
                    LevelTimer += Time.deltaTime;
                }
                else
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
            else
            {
                FreezePlayerEntity(player, cc);
                EditorViewportCamera.UpdateCamera();
                StudioUIManager.UpdateHierarchyDragDrop();

                bool isCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

                if (InteractionMode == EditorInteractionMode.PlacementMode)
                {
                    PlacementHologramController.UpdatePlacement();
                    StudioGizmoController.UpdateGizmo();
                }
                else
                {
                    StudioGizmoController.UpdateGizmo();

                    if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !StudioUIManager.IsPointerOverUI())
                    {
                        if (!StudioGizmoController.IsHoveringHandle)
                        {
                            GameObject aimed = GetAimedPlacedObject();
                            SelectObject(aimed, isAdditive: isCtrl);
                        }
                    }
                }

                bool isDeleteKey = Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace);
                if (isDeleteKey && GUIUtility.keyboardControl == 0 && !StudioUIManager.IsPointerOverUI())
                {
                    DeleteSelectedObjects();
                }

                bool isShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                bool isAlt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

                if (isCtrl && GUIUtility.keyboardControl == 0)
                {
                    if (Input.GetKeyDown(KeyCode.P))
                    {
                        if (isShift) UnparentSelectedObjects();
                        else ParentSelectedObjects();
                    }
                    else if (Input.GetKeyDown(KeyCode.D))
                    {
                        DuplicateSelectedObjects();
                    }
                    else if (Input.GetKeyDown(KeyCode.C))
                    {
                        CopySelectedObjects();
                    }
                    else if (Input.GetKeyDown(KeyCode.V))
                    {
                        PasteClipboardObjects();
                    }
                    else if (Input.GetKeyDown(KeyCode.Z))
                    {
                        if (isShift) PerformRedo();
                        else PerformUndo();
                    }
                    else if (Input.GetKeyDown(KeyCode.Y))
                    {
                        PerformRedo();
                    }
                }

                // Alt + P to Unparent (standard Blender shortcut)
                if (isAlt && Input.GetKeyDown(KeyCode.P) && GUIUtility.keyboardControl == 0)
                {
                    UnparentSelectedObjects();
                }
            }

            if (Input.GetKeyDown(KeyCode.F1)) ToggleEditMode();
            if (Input.GetKeyDown(KeyCode.F4)) SceneHarvestingService.DebugDumpSceneLighting();
            if (Input.GetKeyDown(KeyCode.F5)) LevelPersistenceService.SaveLevel(MapBrowserService.SelectedMapName);
            if (Input.GetKeyDown(KeyCode.F6)) LevelPersistenceService.LoadLevel(MapBrowserService.SelectedMapName);
        }

        // =========================================================================
        // SOLID OBJECT SPAWNING: PRESERVES MESH COLLIDERS & NATIVE GAMEPLAY HITBOXES
        // =========================================================================

        public static GameObject SpawnAssetByName(string partialName, Vector3 position, Vector3 scale, Quaternion? customRotation = null)
        {
            if (string.IsNullOrEmpty(partialName)) return null;

            string clean = partialName.Replace("_", " ").Trim().ToLower();
            string raw = partialName.Trim().ToLower();

            CatalogAsset found = AllAssets.Find(a =>
                a.DisplayName.ToLower() == clean ||
                (a.FilterMesh != null && a.FilterMesh.name.ToLower() == raw) ||
                (a.SourceTemplate != null && a.SourceTemplate.name.ToLower() == raw)
            );

            if (found == null)
            {
                found = AllAssets.Find(a =>
                    a.DisplayName.ToLower().Contains(clean) || clean.Contains(a.DisplayName.ToLower()) ||
                    (a.FilterMesh != null && a.FilterMesh.name.ToLower().Contains(raw))
                );
            }

            if (found == null)
            {
                if (clean.Contains("spawn") || clean.Contains("entry")) found = AllAssets.Find(a => a.IsSpawnGate);
                else if (clean.Contains("goal") || clean.Contains("finish") || clean.Contains("end")) found = AllAssets.Find(a => a.IsGoalGate);
                else if (clean.Contains("sunlight") || clean.Contains("sun")) found = AllAssets.Find(a => a.IsSunlight);
                else if (clean.Contains("spotlight") || clean.Contains("light")) found = AllAssets.Find(a => a.IsSpotlight);
                else if (clean.Contains("rotating") && clean.Contains("laser")) found = AllAssets.Find(a => a.IsRotatingLaser);
                else if (clean.Contains("laser")) found = AllAssets.Find(a => a.IsLaser);
                else if (clean.Contains("platform") || clean.Contains("floor")) found = AllAssets.Find(a => a.DisplayName.ToLower().Contains("platform") || a.DisplayName.ToLower().Contains("floor"));
                else if (clean.Contains("jumper")) found = AllAssets.Find(a => a.IsJumper);
                else if (clean.Contains("checkpoint")) found = AllAssets.Find(a => a.IsCheckPoint && !a.IsSpawnGate && !a.IsGoalGate);
                else if (clean.Contains("turret")) found = AllAssets.Find(a => a.IsTurret);
                else if (clean.Contains("helix")) found = AllAssets.Find(a => a.IsHelix);
            }

            if (found != null) return SpawnCatalogObject(found, position, scale, customRotation);
            return null;
        }

        public static GameObject SpawnAssetByName(string partialName, Vector3 position, float uniformScale, Quaternion? customRotation = null)
        {
            return SpawnAssetByName(partialName, position, Vector3.one * uniformScale, customRotation);
        }

        public static GameObject SpawnCatalogObject(CatalogAsset asset, Vector3 position, Vector3 scale, Quaternion? customRotation = null)
        {
            if (asset == null || asset.SourceTemplate == null) return null;

            GameObject obj = GameObject.Instantiate(asset.SourceTemplate);
            obj.name = "Custom_" + asset.DisplayName.Replace(" ", "_");
            obj.transform.position = position;
            obj.transform.rotation = customRotation ?? GetCurrentCombinedRotation(asset);
            obj.transform.localScale = scale; // Applies true 3D scale (X, Y, Z)
            obj.SetActive(true);

            bool isHazard = asset.IsLaser || asset.IsRotatingLaser;
            bool isGate = asset.IsCheckPoint || asset.IsSpawnGate || asset.IsGoalGate;
            bool isLight = asset.IsSpotlight || asset.IsSunlight;
            bool isHelix = asset.IsHelix;
            bool isTurret = asset.IsTurret;
            bool isJumper = asset.IsJumper;

            Collider[] existingCols = obj.GetComponentsInChildren<Collider>(true);
            bool hasSolidCollider = false;

            for (int i = 0; i < existingCols.Length; i++)
            {
                Collider c = existingCols[i];
                if (c == null || c.gameObject.name == "Editor_Snapping_Proxy") continue;

                c.enabled = true;

                if (isHazard || isLight || isGate)
                {
                    c.isTrigger = true;
                }
                else if (isHelix)
                {
                    HelixPushingZone zone = c.GetComponent<HelixPushingZone>() ?? c.GetComponentInParent<HelixPushingZone>();
                    string cName = c.gameObject.name.ToLower();

                    // Keep pushing zones AND blade/kill triggers as triggers
                    if (zone != null || cName.Contains("zone") || cName.Contains("push") ||
                        cName.Contains("vent") || cName.Contains("wind") || cName.Contains("kill") ||
                        cName.Contains("death") || cName.Contains("blade") || cName.Contains("hazard"))
                    {
                        c.isTrigger = true;
                    }
                    else
                    {
                        // Keep the native housing solid without breaking native triggers
                        if (!c.isTrigger) hasSolidCollider = true;
                    }
                }
                else if (isJumper || isTurret)
                {
                    // Leave native trigger zones and solid base switches untouched
                    if (!c.isTrigger) hasSolidCollider = true;
                }
                else
                {
                    c.isTrigger = false;
                    hasSolidCollider = true;
                }
            }

            // ACCURATE GAMEPLAY MESH COLLIDERS FOR PLATFORMS/ARCHITECTURE
            if (!hasSolidCollider && !isHazard && !isGate && !isLight && !isHelix && !isTurret && !isJumper)
            {
                MeshFilter[] mfs = obj.GetComponentsInChildren<MeshFilter>(true);
                for (int m = 0; m < mfs.Length; m++)
                {
                    if (mfs[m] != null && mfs[m].sharedMesh != null)
                    {
                        MeshCollider mc = mfs[m].gameObject.GetComponent<MeshCollider>();
                        if (mc == null) mc = mfs[m].gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mfs[m].sharedMesh;
                        mc.convex = false;
                        mc.isTrigger = false;
                        mc.enabled = true;
                        hasSolidCollider = true;
                    }
                }

                if (!hasSolidCollider)
                {
                    BoxCollider bc = obj.AddComponent<BoxCollider>();
                    Bounds b = PlacementHologramController.CalculateOptimizedProxyBounds(obj);
                    bc.center = b.center;
                    bc.size = b.size;
                    bc.isTrigger = false;
                    bc.enabled = true;
                }
            }

            if (asset.IsSpawnGate)
            {
                obj.name = "Custom_Spawn_Gate";
                ApplyGateVisualTint(obj, new Color(1.0f, 0.45f, 0.05f));
            }
            else if (asset.IsGoalGate)
            {
                obj.name = "Custom_Goal_Gate";
                ApplyGateVisualTint(obj, new Color(0.1f, 0.65f, 1.0f));
            }

            if (asset.IsCheckPoint)
            {
                CheckPointScript cp = obj.GetComponentInChildren<CheckPointScript>();
                if (cp != null)
                {
                    cp._id = asset.IsSpawnGate ? 0 : (asset.IsGoalGate ? 9999 : (PlacedObjects.Count + 100));

                    if (cp._spawnPoint == null)
                    {
                        GameObject spObj = new GameObject("SpawnPoint");
                        spObj.transform.SetParent(obj.transform, false);
                        spObj.transform.localPosition = new Vector3(0f, 0.1f, 0f);
                        spObj.transform.localRotation = Quaternion.identity;
                        cp._spawnPoint = spObj.transform;
                    }

                    Collider col = obj.GetComponentInChildren<Collider>();
                    if (col != null) col.isTrigger = true;
                }
            }

            if (asset.IsSunlight)
            {
                obj.name = "Custom_Global_Sunlight";
                Light l = obj.GetComponentInChildren<Light>();
                if (l != null)
                {
                    l.enabled = true;
                    PlacedLights[obj] = new LightConfig
                    {
                        IsDirectional = true,
                        Color = new Color(1f, 0.85f, 0.6f),
                        Intensity = 3.0f,
                        VolumetricIntensity = 1.0f
                    };
                    ApplyLightConfig(obj, PlacedLights[obj]);
                }
                StudioGizmoController.AttachSunVisualWidget(obj);
            }
            else if (asset.IsSpotlight)
            {
                Light l = obj.GetComponentInChildren<Light>();
                if (l != null)
                {
                    l.enabled = true;
                    if (!PlacedLights.ContainsKey(obj)) PlacedLights[obj] = new LightConfig();
                    ApplyLightConfig(obj, PlacedLights[obj]);
                }
            }

            if (asset.IsHelix) ApplyTurbineSpeed(obj, ActiveTurbineSpeed);
            if (asset.IsTurret) ApplyTurretSettings(obj, ActiveTurretFireDelay, 1500f);
            if (asset.IsJumper) ApplyJumperForce(obj, ActiveJumperForce);

            AttachEditorSnappingProxy(obj);
            return obj;
        }

        public static GameObject SpawnCatalogObject(CatalogAsset asset, Vector3 position, float uniformScale, Quaternion? customRotation = null)
        {
            return SpawnCatalogObject(asset, position, Vector3.one * uniformScale, customRotation);
        }
        public static void AttachEditorSnappingProxy(GameObject obj)
        {
            if (obj == null) return;

            Transform old = obj.transform.Find("Editor_Snapping_Proxy");
            if (old != null) GameObject.Destroy(old.gameObject); // Safe destroy

            GameObject proxyObj = new GameObject("Editor_Snapping_Proxy");
            proxyObj.transform.SetParent(obj.transform, false);
            proxyObj.transform.localPosition = Vector3.zero;
            proxyObj.transform.localRotation = Quaternion.identity;
            proxyObj.transform.localScale = Vector3.one;
            proxyObj.layer = 2; // Layer 2: Ignore Raycast

            Bounds proxyB = PlacementHologramController.CalculateOptimizedProxyBounds(obj);
            BoxCollider bc = proxyObj.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.center = proxyB.center;

            // Enforce minimum 0.2m thickness on all 3 axes to prevent PhysX zero-volume engine crash
            bc.size = new Vector3(
                Mathf.Max(0.2f, Mathf.Abs(proxyB.size.x)),
                Mathf.Max(0.2f, Mathf.Abs(proxyB.size.y)),
                Mathf.Max(0.2f, Mathf.Abs(proxyB.size.z))
            );
        }

        // =========================================================================
        // ENTITY REGISTRATION & HIERARCHY TRACKING
        // =========================================================================

        public static void RegisterPlacedObject(GameObject obj)
        {
            if (obj == null || PlacedObjects.Contains(obj)) return;

            PlacedObjects.Add(obj);
            string low = obj.name.ToLower();
            PlacedObjectType identifiedType = PlacedObjectType.Generic;

            CheckPointScript cp = obj.GetComponentInChildren<CheckPointScript>();
            if (cp != null && !PlacedCheckpoints.Contains(cp))
                PlacedCheckpoints.Add(cp);

            if (low.Contains("jumper"))
            {
                identifiedType = PlacedObjectType.Jumper;
                if (!PlacedJumpers.Contains(obj)) PlacedJumpers.Add(obj);
            }
            else if (low.Contains("helix") || low.Contains("helice"))
            {
                identifiedType = PlacedObjectType.Turbine;
                if (!PlacedTurbines.Contains(obj)) PlacedTurbines.Add(obj);
                Helix h = obj.GetComponentInChildren<Helix>();
                if (h != null) _cachedHelixScripts[obj] = h;
            }
            else if (low.Contains("turret") || obj.GetComponentInChildren<TurretScript>() != null)
            {
                identifiedType = PlacedObjectType.Turret;
            }
            else if (low.Contains("spawn"))
            {
                identifiedType = PlacedObjectType.SpawnGate;
            }
            else if (low.Contains("goal"))
            {
                identifiedType = PlacedObjectType.GoalGate;
                PlacedGoalGate = obj;
            }
            else if (low.Contains("sunlight"))
            {
                identifiedType = PlacedObjectType.Sunlight;
            }
            else if (low.Contains("spotlight"))
            {
                identifiedType = PlacedObjectType.Spotlight;
            }
            else if (low.Contains("rotating_laser") || low.Contains("rotating laser"))
            {
                identifiedType = PlacedObjectType.RotatingLaser;
                if (!PlacedRotatingLasers.Contains(obj)) PlacedRotatingLasers.Add(obj);

                if (!PlacedLaserBarriers.Contains(obj))
                {
                    PlacedLaserBarriers.Add(obj);
                    BoxCollider bc = obj.transform.Find("Beam")?.GetComponent<BoxCollider>();
                    if (bc == null)
                    {
                        var cols = obj.GetComponentsInChildren<BoxCollider>(true);
                        for (int c = 0; c < cols.Length; c++)
                        {
                            if (cols[c].gameObject.name != "Editor_Snapping_Proxy") { bc = cols[c]; break; }
                        }
                    }
                    PlacedLaserColliders.Add(bc);
                }
            }
            else if (low.Contains("laser"))
            {
                identifiedType = PlacedObjectType.Laser;
                if (!PlacedLaserBarriers.Contains(obj))
                {
                    PlacedLaserBarriers.Add(obj);
                    BoxCollider bc = obj.GetComponent<BoxCollider>();
                    if (bc == null)
                    {
                        var cols = obj.GetComponentsInChildren<BoxCollider>(true);
                        for (int c = 0; c < cols.Length; c++)
                        {
                            if (cols[c].gameObject.name != "Editor_Snapping_Proxy") { bc = cols[c]; break; }
                        }
                    }
                    PlacedLaserColliders.Add(bc);
                }
            }
            else if (cp != null)
            {
                identifiedType = PlacedObjectType.Checkpoint;
            }

            PlacedObjectTypes[obj] = identifiedType;

            if (obj.transform.parent != null)
                RecalculateParentChildCount(obj.transform.parent.gameObject);

            RecalculateVoidDeathY();
            StudioUIManager.RefreshHierarchy();
        }

        public static void RemovePlacedObjectFromTracking(GameObject target)
        {
            if (target == null) return;

            StudioGizmoController.InvalidateCachedCenter(target);

            PlacedObjects.Remove(target);
            PlacedObjectTypes.Remove(target);
            PlacedParentChildCounts.Remove(target);
            SelectedObjects.Remove(target);

            if (target.transform.parent != null)
                RecalculateParentChildCount(target.transform.parent.gameObject);

            if (PlacedLights.ContainsKey(target)) PlacedLights.Remove(target);

            if (MotionPaths.ContainsKey(target)) MotionPaths.Remove(target);
            DestroyWaypointVisuals(target);

            if (PathEditTarget == target) PathEditTarget = null;
            if (ParentingChildTarget == target) ParentingChildTarget = null;
            if (RepositionTarget == target) RepositionTarget = null;
            if (SelectedObject == target) SelectedObject = SelectedObjects.Count > 0 ? SelectedObjects[SelectedObjects.Count - 1] : null;

            PlacedJumpers.Remove(target);
            PlacedTurbines.Remove(target);
            _cachedHelixScripts.Remove(target);
            PlacedRotatingLasers.Remove(target);

            int laserIdx = PlacedLaserBarriers.IndexOf(target);
            if (laserIdx >= 0)
            {
                PlacedLaserBarriers.RemoveAt(laserIdx);
                if (laserIdx < PlacedLaserColliders.Count) PlacedLaserColliders.RemoveAt(laserIdx);
            }

            if (PlacedGoalGate == target) PlacedGoalGate = null;

            CheckPointScript cp = target.GetComponentInChildren<CheckPointScript>();
            if (cp != null) PlacedCheckpoints.Remove(cp);

            RecalculateVoidDeathY();
            StudioUIManager.RefreshHierarchy();
        }

        public static void DeleteSpecifiedObject(GameObject target)
        {
            if (target != null)
            {
                RemovePlacedObjectFromTracking(target);
                target.SetActive(false);

                float param = 0f;
                if (JumperForces.ContainsKey(target)) param = JumperForces[target];
                else if (TurbineSpeeds.ContainsKey(target)) param = TurbineSpeeds[target];
                else if (TurretFireDelays.ContainsKey(target)) param = TurretFireDelays[target];
                else if (PlacedLights.ContainsKey(target)) param = PlacedLights[target].Intensity;

                UndoHistory.Push(new HistoryRecord
                {
                    ActionType = HistoryActionType.Deletion,
                    TargetObject = target,
                    AssetName = target.name.StartsWith("Custom_") ? target.name.Substring(7) : target.name,
                    Position = target.transform.position,
                    Rotation = target.transform.rotation,
                    ScaleVector = target.transform.localScale, // Saves full Vector3 scale
                    CustomParameter = param
                });
                RedoHistory.Clear();

                if (LastPlacedObject == target) LastPlacedObject = null;
            }
        }

        public static void ClearAllPlacedObjects()
        {
            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                if (PlacedObjects[i] != null) GameObject.Destroy(PlacedObjects[i]);
            }
            PlacedObjects.Clear();
            PlacedObjectTypes.Clear();
            PlacedParentChildCounts.Clear();
            PlacedLights.Clear();
            RepositionTarget = null;
            LastPlacedObject = null;
            SelectedObjects.Clear();
            SelectedObject = null;

            PlacedJumpers.Clear();
            PlacedTurbines.Clear();
            _cachedHelixScripts.Clear();
            PlacedLaserBarriers.Clear();
            PlacedLaserColliders.Clear();
            PlacedRotatingLasers.Clear();
            PlacedCheckpoints.Clear();
            PlacedGoalGate = null;

            JumperForces.Clear();
            TurbineSpeeds.Clear();
            TurretFireDelays.Clear();

            MotionPaths.Clear();
            DestroyAllWaypointVisuals();
            StudioGizmoController.ClearAllCachedCentroids();

            PathEditTarget = null;
            ParentingChildTarget = null;
            UndoHistory.Clear();
            RedoHistory.Clear();

            CachedVoidDeathY = -140f;
            PlaytestSnapshots.Clear();
            StudioUIManager.RefreshHierarchy();
        }

        public static void RecalculateParentChildCount(GameObject parentObj)
        {
            if (parentObj == null) return;
            int count = 0;
            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                if (PlacedObjects[i] != null && PlacedObjects[i].transform.parent == parentObj.transform)
                    count++;
            }
            if (count > 0) PlacedParentChildCounts[parentObj] = count;
            else PlacedParentChildCounts.Remove(parentObj);
        }

        public static void RecalculateVoidDeathY()
        {
            float lowest = -140f;
            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                if (PlacedObjects[i] != null && PlacedObjects[i].activeSelf)
                {
                    float y = PlacedObjects[i].transform.position.y - 60f;
                    if (y < lowest) lowest = y;
                }
            }
            CachedVoidDeathY = lowest;
        }

        // =========================================================================
        // UNDO / REDO ENGINE
        // =========================================================================

        public static void PerformUndo()
        {
            if (UndoHistory.Count == 0)
            {
                ShowNotification("Nothing to undo.");
                return;
            }

            var record = UndoHistory.Pop();

            if (record.ActionType == HistoryActionType.Placement)
            {
                if (record.TargetObject != null)
                {
                    record.TargetObject.SetActive(false);
                    RemovePlacedObjectFromTracking(record.TargetObject);
                }
                RedoHistory.Push(record);
                ShowNotification($"Undid placement of {record.AssetName}");
            }
            else if (record.ActionType == HistoryActionType.Deletion)
            {
                if (record.TargetObject != null)
                {
                    record.TargetObject.SetActive(true);
                    RegisterPlacedObject(record.TargetObject);
                }
                RedoHistory.Push(record);
                ShowNotification($"Restored deleted {record.AssetName}");
            }
            else if (record.ActionType == HistoryActionType.Parenting)
            {
                if (record.TargetObject != null)
                {
                    Transform prevT = record.PreviousParent != null ? record.PreviousParent.transform : null;
                    record.TargetObject.transform.SetParent(prevT, true);

                    if (record.NewParent != null) RecalculateParentChildCount(record.NewParent);
                    if (record.PreviousParent != null) RecalculateParentChildCount(record.PreviousParent);
                    StudioGizmoController.InvalidateCachedCenter(record.TargetObject);
                }
                RedoHistory.Push(record);
                ShowNotification("Undid Parenting");
            }
            else if (record.ActionType == HistoryActionType.Reposition)
            {
                if (record.TargetObject != null)
                {
                    record.TargetObject.transform.position = record.PreviousPosition;
                    record.TargetObject.transform.rotation = record.PreviousRotation;
                    record.TargetObject.transform.localScale = record.PreviousScale;
                    StudioGizmoController.InvalidateCachedCenter(record.TargetObject);
                }
                RedoHistory.Push(record);
                ShowNotification("Undid Transform change");
            }
            StudioUIManager.RefreshHierarchy();
            StudioUIManager.RefreshInspectorValues();
        }

        public static void PerformRedo()
        {
            if (RedoHistory.Count == 0)
            {
                ShowNotification("Nothing to redo.");
                return;
            }

            var record = RedoHistory.Pop();

            if (record.ActionType == HistoryActionType.Placement)
            {
                if (record.TargetObject != null)
                {
                    record.TargetObject.SetActive(true);
                    RegisterPlacedObject(record.TargetObject);
                }
                UndoHistory.Push(record);
                ShowNotification($"Redid placement of {record.AssetName}");
            }
            else if (record.ActionType == HistoryActionType.Deletion)
            {
                if (record.TargetObject != null)
                {
                    record.TargetObject.SetActive(false);
                    RemovePlacedObjectFromTracking(record.TargetObject);
                }
                UndoHistory.Push(record);
                ShowNotification($"Re-deleted {record.AssetName}");
            }
            else if (record.ActionType == HistoryActionType.Parenting)
            {
                if (record.TargetObject != null)
                {
                    Transform newT = record.NewParent != null ? record.NewParent.transform : null;
                    record.TargetObject.transform.SetParent(newT, true);

                    if (record.PreviousParent != null) RecalculateParentChildCount(record.PreviousParent);
                    if (record.NewParent != null) RecalculateParentChildCount(record.NewParent);
                    StudioGizmoController.InvalidateCachedCenter(record.TargetObject);
                }
                UndoHistory.Push(record);
                ShowNotification("Redid Parenting");
            }
            else if (record.ActionType == HistoryActionType.Reposition)
            {
                if (record.TargetObject != null)
                {
                    record.TargetObject.transform.position = record.NewPosition;
                    record.TargetObject.transform.rotation = record.NewRotation;
                    record.TargetObject.transform.localScale = record.NewScale;
                    StudioGizmoController.InvalidateCachedCenter(record.TargetObject);
                }
                UndoHistory.Push(record);
                ShowNotification("Redid Transform change");
            }
            StudioUIManager.RefreshHierarchy();
            StudioUIManager.RefreshInspectorValues();
        }

        // =========================================================================
        // COMPONENT SETTINGS APPLIERS
        // =========================================================================

        public static void ApplyJumperForce(GameObject jumperObj, float force)
        {
            if (jumperObj == null) return;
            JumperForces[jumperObj] = force;

            Jumper[] jumpers = jumperObj.GetComponentsInChildren<Jumper>(true);
            foreach (var jc in jumpers)
            {
                if (jc == null) continue;
                jc.jumpAcceleration = force;
                jc.currentAcceleration = force;
            }
        }

        public static void ApplyTurbineSpeed(GameObject turbineObj, float speed)
        {
            if (turbineObj == null) return;
            TurbineSpeeds[turbineObj] = speed;

            HelixPushingZone zone = turbineObj.GetComponentInChildren<HelixPushingZone>();
            if (zone != null)
            {
                zone.enabled = true;
                zone._maxForce = speed;
                zone._maxVelocity = speed * 2.0f;
            }

            Helix h = turbineObj.GetComponentInChildren<Helix>();
            if (h != null)
            {
                h.enabled = true;
                h._maximumVelocity = speed * 20f;
                _cachedHelixScripts[turbineObj] = h;
            }
        }

        public static void ApplyTurretSettings(GameObject turretObj, float fireDelay, float firePower = 1500f)
        {
            if (turretObj == null) return;
            TurretFireDelays[turretObj] = fireDelay;

            TurretScript[] turretScripts = turretObj.GetComponentsInChildren<TurretScript>(true);
            for (int i = 0; i < turretScripts.Length; i++)
            {
                TurretScript ts = turretScripts[i];
                if (ts == null) continue;

                ts.gameObject.SetActive(true);
                ts.enabled = !IsEditModeActive;
                ts._fireDelay = Mathf.Max(0.05f, fireDelay);
                ts._firePower = firePower;

                if (ts._triggerAnimation != null)
                {
                    ts._triggerAnimation.enabled = true;
                    ts._triggerAnimation.isTrigger = true;
                    if (ts._triggerAnimation.radius < 60f) ts._triggerAnimation.radius = 60f;
                }
            }

            Collider[] colliders = turretObj.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null) colliders[i].enabled = true;
            }
        }

        // =========================================================================
        // HDRP VOLUMETRIC LIGHTING & DIRECTIONAL SUN CONTROLLER
        // =========================================================================

        public static void ApplyLightConfig(GameObject lightObj, LightConfig cfg)
        {
            if (lightObj == null || cfg == null) return;
            PlacedLights[lightObj] = cfg;

            Light l = lightObj.GetComponentInChildren<Light>();

            if (cfg.IsDirectional)
            {
                Light targetSun = (SceneHarvestingService.NativeSceneSun != null && SceneHarvestingService.NativeSceneSun.gameObject.activeInHierarchy)
                    ? SceneHarvestingService.NativeSceneSun
                    : l;

                if (targetSun != null)
                {
                    targetSun.gameObject.SetActive(true);
                    targetSun.enabled = true;
                    targetSun.type = LightType.Directional;
                    targetSun.transform.rotation = lightObj.transform.rotation;
                    targetSun.color = cfg.Color;

                    float sunLux = Mathf.Max(0.1f, cfg.Intensity) * 8000f;
                    targetSun.intensity = sunLux;
                    RenderSettings.sun = targetSun;

                    ApplyHDRPVolumetricSettings(targetSun.gameObject, cfg.VolumetricIntensity, sunLux, cfg.Color);
                }

                if (l != null && targetSun != l)
                {
                    l.enabled = false;
                }

                StudioGizmoController.AttachSunVisualWidget(lightObj);
            }
            else
            {
                if (l != null)
                {
                    l.enabled = true;
                    l.type = LightType.Spot;
                    l.renderMode = LightRenderMode.Auto;
                    l.range = 150f;
                    l.spotAngle = Mathf.Clamp(cfg.SpotAngle, 5f, 150f);
                    l.color = cfg.Color;

                    float lumens = Mathf.Pow(Mathf.Max(0.1f, cfg.Intensity), 2.2f) * 8000f;
                    l.intensity = lumens;

                    ApplyHDRPVolumetricSettings(l.gameObject, cfg.VolumetricIntensity, lumens, cfg.Color);
                }
            }

            Renderer[] rends = lightObj.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null) continue;
                Material m = rends[i].material;
                if (m == null) continue;

                float volVal = Mathf.Clamp(cfg.VolumetricIntensity, 0f, 16f);
                if (m.HasProperty("_VolumetricIntensity")) m.SetFloat("_VolumetricIntensity", volVal);
                if (m.HasProperty("_VolumetricDimmer")) m.SetFloat("_VolumetricDimmer", volVal);
                if (m.HasProperty("_Volumetric")) m.SetFloat("_Volumetric", volVal);
                if (m.HasProperty("_Color")) m.SetColor("_Color", cfg.Color);
                if (m.HasProperty("_EmissionColor"))
                {
                    m.SetColor("_EmissionColor", cfg.Color * (cfg.Intensity * 0.75f));
                    m.EnableKeyword("_EMISSION");
                }
            }
        }

        private static void ApplyHDRPVolumetricSettings(GameObject lightGo, float volumetricIntensity, float physicalIntensity, Color lightColor)
        {
            if (lightGo == null) return;

            Component hdLight = null;
            Component[] comps = lightGo.GetComponentsInChildren<Component>(true);

            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue;
                string fullName = comps[i].GetIl2CppType().FullName;
                if (fullName.Contains("HDAdditionalLightData") || fullName.Contains("AdditionalLightData"))
                {
                    hdLight = comps[i];
                    break;
                }
            }

            if (hdLight != null)
            {
                try
                {
                    Type type = hdLight.GetType();
                    float dimmer = Mathf.Clamp(volumetricIntensity, 0f, 16f);
                    bool enableVol = volumetricIntensity > 0.01f;

                    SetFieldOrProp(type, hdLight, "useColorTemperature", false);
                    SetFieldOrProp(type, hdLight, "m_UseColorTemperature", false);
                    SetFieldOrProp(type, hdLight, "color", lightColor);
                    SetFieldOrProp(type, hdLight, "colorFilter", lightColor);

                    SetFieldOrProp(type, hdLight, "intensity", physicalIntensity);

                    SetFieldOrProp(type, hdLight, "volumetricDimmer", dimmer);
                    SetFieldOrProp(type, hdLight, "m_VolumetricDimmer", dimmer);
                    SetFieldOrProp(type, hdLight, "affectsVolumetric", enableVol);
                    SetFieldOrProp(type, hdLight, "m_AffectsVolumetric", enableVol);
                    SetFieldOrProp(type, hdLight, "useVolumetric", enableVol);
                    SetFieldOrProp(type, hdLight, "m_UseVolumetric", enableVol);
                    SetFieldOrProp(type, hdLight, "volumetricShadowDimmer", enableVol ? 1f : 0f);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[HDRP Sun] Setting error: {ex.Message}");
                }
            }
        }

        private static void SetFieldOrProp(Type type, object target, string name, object val)
        {
            try
            {
                var prop = type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(target, val, null);
                    return;
                }
                var field = type.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(target, val);
                }
            }
            catch { }
        }

        public static void ForceRefreshAllLights()
        {
            foreach (var kvp in PlacedLights)
            {
                GameObject obj = kvp.Key;
                LightConfig cfg = kvp.Value;
                if (obj == null || !obj.activeSelf || cfg == null) continue;

                Light l = obj.GetComponentInChildren<Light>();
                if (l != null)
                {
                    l.spotAngle = cfg.SpotAngle + 0.1f;
                    l.enabled = false;
                }
                ApplyLightConfig(obj, cfg);
            }
        }

        public static void ApplyGateVisualTint(GameObject gateObj, Color tintColor)
        {
            if (gateObj == null) return;

            Renderer[] renderers = gateObj.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;

                Material[] mats = r.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null) continue;

                    mat.color = tintColor;
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", tintColor);
                    if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", tintColor);
                    if (mat.HasProperty("_EmissionColor"))
                    {
                        mat.SetColor("_EmissionColor", tintColor * 2.2f);
                        mat.EnableKeyword("_EMISSION");
                    }
                }
            }

            Light[] lights = gateObj.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null) lights[i].color = tintColor;
            }
        }

        public static void StripParticlesAndLights(GameObject root)
        {
            if (root == null) return;

            Jumper jc = root.GetComponentInChildren<Jumper>();
            if (jc != null && jc.Fx != null)
                GameObject.DestroyImmediate(jc.Fx);

            Component[] components = root.GetComponentsInChildren<Component>(true);
            foreach (var comp in components)
            {
                if (comp == null || comp is Transform) continue;
                string typeName = comp.GetType().Name.ToLower();
                if (typeName.Contains("particle") || typeName.Contains("emitter") || typeName.Contains("trail") || typeName.Contains("flare") || typeName.Contains("halo"))
                    GameObject.DestroyImmediate(comp);
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var tr in transforms)
            {
                if (tr == null || tr.gameObject == root) continue;
                string objName = tr.gameObject.name.ToLower();

                if (objName == "beam" || objName.Contains("laser_barrier")) continue;

                if (objName.Contains("particle") || objName.Contains("flame") || objName.Contains("fx") ||
                    objName.Contains("fire") || objName.Contains("glow") || objName.Contains("flare") ||
                    (objName.Contains("beam") && objName.Contains("fx")) || objName.Contains("anneau"))
                {
                    GameObject.DestroyImmediate(tr.gameObject);
                }
            }
        }

        public static void SetSpotlightMeshesVisible(bool visible)
        {
            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                GameObject obj = PlacedObjects[i];
                if (obj == null) continue;

                Transform housing = obj.transform.Find("Light_Housing");
                if (housing != null) housing.gameObject.SetActive(visible);

                Transform lens = obj.transform.Find("Light_Lens");
                if (lens != null) lens.gameObject.SetActive(visible);

                Transform sunWidget = obj.transform.Find("Sun_Editor_Widget");
                if (sunWidget != null) sunWidget.gameObject.SetActive(visible);
            }
        }

        // =========================================================================
        // MOTION PATHS & FULLY MOVEABLE IN-SCENE WAYPOINTS
        // =========================================================================

        public static bool IsWaypointMarker(GameObject go, out GameObject ownerObj, out bool isPointB)
        {
            ownerObj = null;
            isPointB = false;
            if (go == null) return false;

            foreach (var kvp in WaypointMarkersB)
            {
                if (kvp.Value == go)
                {
                    ownerObj = kvp.Key;
                    isPointB = true;
                    return true;
                }
            }

            foreach (var kvp in WaypointMarkersA)
            {
                if (kvp.Value == go)
                {
                    ownerObj = kvp.Key;
                    isPointB = false;
                    return true;
                }
            }

            return false;
        }

        private static bool IsChildOfAnyWaypoint(GameObject hitGo, out GameObject markerRoot)
        {
            markerRoot = null;
            if (hitGo == null) return false;

            foreach (var kvp in WaypointMarkersB)
            {
                if (kvp.Value != null && (hitGo == kvp.Value || hitGo.transform.IsChildOf(kvp.Value.transform)))
                {
                    markerRoot = kvp.Value;
                    return true;
                }
            }

            foreach (var kvp in WaypointMarkersA)
            {
                if (kvp.Value != null && (hitGo == kvp.Value || hitGo.transform.IsChildOf(kvp.Value.transform)))
                {
                    markerRoot = kvp.Value;
                    return true;
                }
            }

            return false;
        }

        public static void DestroyWaypointVisuals(GameObject obj)
        {
            if (obj == null) return;

            if (WaypointMarkersA.TryGetValue(obj, out GameObject a) && a != null)
            {
                GameObject.DestroyImmediate(a);
            }
            WaypointMarkersA.Remove(obj);

            if (WaypointMarkersB.TryGetValue(obj, out GameObject b) && b != null)
            {
                GameObject.DestroyImmediate(b);
            }
            WaypointMarkersB.Remove(obj);

            if (WaypointLines.TryGetValue(obj, out LineRenderer l) && l != null)
            {
                GameObject.DestroyImmediate(l.gameObject);
            }
            WaypointLines.Remove(obj);
        }

        public static void DestroyAllWaypointVisuals()
        {
            foreach (var kvp in WaypointMarkersA) if (kvp.Value != null) GameObject.DestroyImmediate(kvp.Value);
            WaypointMarkersA.Clear();

            foreach (var kvp in WaypointMarkersB) if (kvp.Value != null) GameObject.DestroyImmediate(kvp.Value);
            WaypointMarkersB.Clear();

            foreach (var kvp in WaypointLines) if (kvp.Value != null) GameObject.DestroyImmediate(kvp.Value.gameObject);
            WaypointLines.Clear();
        }

        private static void HideAllWaypointMarkers()
        {
            foreach (var kvp in WaypointMarkersA) if (kvp.Value != null) kvp.Value.SetActive(false);
            foreach (var kvp in WaypointMarkersB) if (kvp.Value != null) kvp.Value.SetActive(false);
            foreach (var kvp in WaypointLines) if (kvp.Value != null) kvp.Value.gameObject.SetActive(false);
        }

        private static void UpdateObjectMotionPaths(GameObject player, CharacterController cc)
        {
            if (MotionPaths.Count == 0)
            {
                HideAllWaypointMarkers();
                return;
            }

            bool canPushPlayer = (player != null && !IsEditModeActive && cc != null && cc.isGrounded);
            Vector3 playerFeetPos = canPushPlayer ? player.transform.position : Vector3.zero;
            float dt = Time.deltaTime;

            foreach (var kvp in MotionPaths)
            {
                GameObject obj = kvp.Key;
                ObjectMotionPath path = kvp.Value;
                if (obj == null || !obj.activeSelf || !path.IsActive) continue;

                if (IsEditModeActive)
                {
                    if (SelectedObject != null && IsWaypointMarker(SelectedObject, out GameObject ownerB, out bool isB) && ownerB == obj)
                    {
                        if (isB)
                        {
                            path.PointB = SelectedObject.transform.position;
                        }
                        else
                        {
                            path.PointA = SelectedObject.transform.position;
                            obj.transform.position = path.PointA;
                        }
                    }
                    else if (SelectedObject == obj)
                    {
                        if (obj.transform.position != path.PointA)
                        {
                            Vector3 delta = obj.transform.position - path.PointA;
                            path.PointA = obj.transform.position;
                            path.PointB += delta;
                        }
                    }
                    else
                    {
                        obj.transform.position = path.PointA;
                    }

                    UpdateWaypointVisuals(obj, path);
                    continue;
                }

                HideWaypointVisual(obj);

                // 1. Detect if player is standing on this platform
                bool playerOnPlatform = false;
                if (canPushPlayer && obj != null)
                {
                    if (path.CachedColliders == null || path.CachedColliders.Length == 0)
                        path.CachedColliders = obj.GetComponentsInChildren<Collider>(true);

                    for (int c = 0; c < path.CachedColliders.Length; c++)
                    {
                        Collider col = path.CachedColliders[c];
                        if (col == null || !col.gameObject.activeInHierarchy || col.isTrigger) continue;

                        try
                        {
                            if (col.bounds.Contains(playerFeetPos + Vector3.down * 0.2f))
                            {
                                playerOnPlatform = true;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                // 2. Continuous rotation & player tangential momentum
                if (Mathf.Abs(path.RotationSpeed) > 0.01f)
                {
                    Vector3 localAxis = path.GetEffectiveLocalAxis(obj.transform);

                    float rotAngle = path.RotationSpeed * dt;
                    Vector3 worldAxis = obj.transform.TransformDirection(localAxis);
                    Quaternion rotDelta = Quaternion.AngleAxis(rotAngle, worldAxis);

                    // If player is on the platform, rotate them with the surface
                    if (playerOnPlatform)
                    {
                        Vector3 offset = player.transform.position - obj.transform.position;
                        Vector3 newOffset = rotDelta * offset;
                        Vector3 rotPush = newOffset - offset;
                        cc.Move(rotPush);

                        // If the axis has a vertical (Y) component, rotate the player view accordingly
                        if (Mathf.Abs(worldAxis.y) > 0.25f)
                        {
                            float yawAngle = rotAngle * worldAxis.y;
                            player.transform.rotation = Quaternion.AngleAxis(yawAngle, Vector3.up) * player.transform.rotation;
                        }
                    }

                    obj.transform.Rotate(localAxis, rotAngle, Space.Self);
                }

                // 3. Linear movement between Point A and Point B
                float dist = path.TotalDistance;
                if (dist < 0.05f || path.Speed < 0.01f) continue; // Standalone rotation mode

                float speed = Mathf.Max(0.1f, path.Speed);
                float duration = dist / speed;
                float t = Mathf.PingPong(LevelTimer / duration, 1.0f);
                Vector3 targetPos = path.EvaluatePosition(t);
                Vector3 deltaPos = targetPos - obj.transform.position;

                obj.transform.position = targetPos;

                if (playerOnPlatform && deltaPos.sqrMagnitude > 0.00001f)
                {
                    cc.Move(deltaPos);
                }
            }
        }
        public static void UpdateWaypointVisuals(GameObject obj, ObjectMotionPath path)
        {
            if (obj == null || path == null) return;

            if (!WaypointMarkersA.TryGetValue(obj, out GameObject markerA) || markerA == null)
            {
                markerA = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                markerA.name = "Waypoint_A_" + obj.name;
                markerA.layer = 0;

                SphereCollider sc = markerA.GetComponent<SphereCollider>();
                if (sc != null) { sc.isTrigger = false; sc.radius = 0.55f; }

                markerA.transform.localScale = Vector3.one * 0.95f;
                Material ma = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default"));
                ma.color = new Color(0.2f, 1f, 0.4f, 1f);
                if (ma.HasProperty("_EmissionColor")) { ma.SetColor("_EmissionColor", new Color(0.2f, 1f, 0.4f) * 2f); ma.EnableKeyword("_EMISSION"); }
                markerA.GetComponent<Renderer>().sharedMaterial = ma;
                markerA.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                WaypointMarkersA[obj] = markerA;
            }

            if (!WaypointMarkersB.TryGetValue(obj, out GameObject markerB) || markerB == null)
            {
                markerB = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                markerB.name = "Waypoint_B_" + obj.name;
                markerB.layer = 0;

                SphereCollider sc = markerB.GetComponent<SphereCollider>();
                if (sc != null) { sc.isTrigger = false; sc.radius = 0.55f; }

                markerB.transform.localScale = Vector3.one * 0.95f;
                Material mb = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default"));
                mb.color = new Color(1f, 0.65f, 0.1f, 1f);
                if (mb.HasProperty("_EmissionColor")) { mb.SetColor("_EmissionColor", new Color(1f, 0.65f, 0.1f) * 2f); mb.EnableKeyword("_EMISSION"); }
                markerB.GetComponent<Renderer>().sharedMaterial = mb;
                markerB.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                WaypointMarkersB[obj] = markerB;
            }

            if (!WaypointLines.TryGetValue(obj, out LineRenderer lr) || lr == null)
            {
                GameObject lineObj = new GameObject("Waypoint_Line_" + obj.name);
                lineObj.layer = 2;
                lr = lineObj.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.startWidth = 0.15f;
                lr.endWidth = 0.15f;
                Material ml = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default"));
                ml.color = new Color(0.3f, 0.85f, 1f, 0.8f);
                lr.sharedMaterial = ml;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                WaypointLines[obj] = lr;
            }

            markerA.SetActive(true);
            markerB.SetActive(true);
            lr.gameObject.SetActive(true);

            markerA.transform.position = path.PointA;
            markerB.transform.position = path.PointB;

            lr.SetPosition(0, path.PointA);
            lr.SetPosition(1, path.PointB);
        }

        private static void HideWaypointVisual(GameObject obj)
        {
            if (WaypointMarkersA.TryGetValue(obj, out GameObject a) && a != null) a.SetActive(false);
            if (WaypointMarkersB.TryGetValue(obj, out GameObject b) && b != null) b.SetActive(false);
            if (WaypointLines.TryGetValue(obj, out LineRenderer l) && l != null) l.gameObject.SetActive(false);
        }

        private static void CheckGoalTriggerArrival(GameObject player)
        {
            if (IsLevelCompleted || PlacedGoalGate == null || !PlacedGoalGate.activeSelf) return;

            Vector3 pPos = player.transform.position;
            Vector3 gPos = PlacedGoalGate.transform.position;

            float horizDistSq = (pPos.x - gPos.x) * (pPos.x - gPos.x) + (pPos.z - gPos.z) * (pPos.z - gPos.z);
            float vertDist = Mathf.Abs(pPos.y - gPos.y);

            if (horizDistSq < 10.24f && vertDist < 2.5f)
            {
                IsLevelCompleted = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private static void CheckHelixWindPushing(GameObject player, CharacterController cc)
        {
            if (PlacedTurbines.Count == 0 || cc == null) return;

            Vector3 pPos = player.transform.position + Vector3.up * 1.0f;
            float dt = Time.deltaTime;

            for (int i = 0; i < PlacedTurbines.Count; i++)
            {
                GameObject obj = PlacedTurbines[i];
                if (obj == null || !obj.activeSelf) continue;

                float speed = TurbineSpeeds.ContainsKey(obj) ? TurbineSpeeds[obj] : ActiveTurbineSpeed;

                if (!_cachedHelixScripts.TryGetValue(obj, out Helix helixScript) || helixScript == null)
                {
                    helixScript = obj.GetComponentInChildren<Helix>();
                    _cachedHelixScripts[obj] = helixScript;
                }

                if (helixScript != null && helixScript._hingeJoint != null)
                {
                    helixScript._hingeJoint.transform.Rotate(Vector3.forward, (speed * 12f) * dt, Space.Self);
                }

                Vector3 hPos = obj.transform.position;
                Vector3 forward = obj.transform.forward;
                Vector3 toPlayer = pPos - hPos;

                float forwardDist = Vector3.Dot(toPlayer, forward);
                float windRange = 16.0f * obj.transform.localScale.z;

                if (forwardDist > 0.2f && forwardDist < windRange)
                {
                    Vector3 perp = toPlayer - forward * forwardDist;
                    float radius = 3.0f * obj.transform.localScale.x;

                    if (perp.sqrMagnitude < radius * radius)
                    {
                        Vector3 pushDir = forward;
                        pushDir.y = Mathf.Max(0.22f, pushDir.y);
                        pushDir.Normalize();

                        float pushSpeed = Mathf.Lerp(speed, speed * 0.25f, forwardDist / windRange);
                        cc.Move(pushDir * pushSpeed * dt);
                    }
                }
            }
        }

        private static void CheckLaserBarriers(GameObject player, CharacterController cc)
        {
            if (PlacedLaserBarriers.Count == 0 || cc == null) return;

            Vector3 playerCenter = player.transform.position + cc.center;
            float playerRadius = cc.radius + 0.1f;
            float playerHalfHeight = cc.height * 0.5f;

            for (int i = 0; i < PlacedLaserBarriers.Count; i++)
            {
                GameObject obj = PlacedLaserBarriers[i];
                if (obj == null || !obj.activeSelf) continue;

                if ((obj.transform.position - playerCenter).sqrMagnitude > 2500f) continue;

                BoxCollider bc = (i < PlacedLaserColliders.Count) ? PlacedLaserColliders[i] : null;
                if (bc == null || !bc.gameObject.activeInHierarchy)
                {
                    bc = obj.transform.Find("Beam")?.GetComponent<BoxCollider>() ?? obj.GetComponent<BoxCollider>();
                    if (bc == null)
                    {
                        var allCols = obj.GetComponentsInChildren<BoxCollider>(true);
                        for (int c = 0; c < allCols.Length; c++)
                        {
                            if (allCols[c].gameObject.name != "Editor_Snapping_Proxy")
                            {
                                bc = allCols[c];
                                break;
                            }
                        }
                    }
                    if (i < PlacedLaserColliders.Count) PlacedLaserColliders[i] = bc;
                }
                if (bc == null) continue;

                Vector3 localPlayer = bc.transform.InverseTransformPoint(playerCenter);
                Vector3 boxSize = bc.size;
                Vector3 boxCenter = bc.center;

                float dx = Mathf.Abs(localPlayer.x - boxCenter.x);
                float dy = Mathf.Abs(localPlayer.y - boxCenter.y);
                float dz = Mathf.Abs(localPlayer.z - boxCenter.z);

                Vector3 lossy = bc.transform.lossyScale;
                float limitX = (boxSize.x * 0.5f) + (playerRadius / Mathf.Max(0.001f, lossy.x));
                float limitY = (boxSize.y * 0.5f) + (playerHalfHeight / Mathf.Max(0.001f, lossy.y));
                float limitZ = (boxSize.z * 0.5f) + (playerRadius / Mathf.Max(0.001f, lossy.z));

                if (dx <= limitX && dy <= limitY && dz <= limitZ)
                {
                    RespawnPlayer(player);
                    break;
                }
            }
        }

        private static void FreezePlayerEntity(GameObject player, CharacterController cc)
        {
            if (player != null)
            {
                player.transform.position = FrozenPlayerPosition;
                player.transform.rotation = FrozenPlayerRotation;
                if (cc != null && cc.enabled) cc.enabled = false;
            }
        }

        private static void CheckVoidFall(GameObject player)
        {
            if (player.transform.position.y < CachedVoidDeathY)
                RespawnPlayer(player);
        }

        public static void ClearLastCheckpoint() { ActiveCustomCheckpoint = null; }

        public static void RestartRun()
        {
            GameObject player = FindPlayerEntity();
            if (player == null) return;

            CharacterController cc = GetPlayerController();
            if (cc != null) cc.enabled = false;

            Rigidbody rb = player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            ClearLastCheckpoint();

            Transform spawnPoint = null;
            Quaternion spawnRot = Quaternion.identity;

            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                GameObject obj = PlacedObjects[i];
                if (obj != null && PlacedObjectTypes.TryGetValue(obj, out var t) && t == PlacedObjectType.SpawnGate)
                {
                    CheckPointScript cp = obj.GetComponentInChildren<CheckPointScript>();
                    spawnPoint = (cp != null && cp._spawnPoint != null) ? cp._spawnPoint : obj.transform;
                    spawnRot = obj.transform.rotation;
                    break;
                }
            }

            if (spawnPoint != null)
            {
                player.transform.position = spawnPoint.position;
                player.transform.rotation = spawnRot;
            }
            else
            {
                player.transform.position = LevelSpawnPosition + Vector3.up * 0.2f;
            }

            if (cc != null) cc.enabled = true;

            LevelTimer = 0f;
            IsLevelCompleted = false;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            UnfreezePlayerControls();
        }

        public static void RespawnPlayer(GameObject player)
        {
            CharacterController cc = GetPlayerController();
            if (cc != null) cc.enabled = false;

            if (ActiveCustomCheckpoint != null && ActiveCustomCheckpoint.gameObject.activeInHierarchy)
            {
                Transform sp = ActiveCustomCheckpoint._spawnPoint;
                Vector3 targetPos = (sp != null) ? sp.position : (ActiveCustomCheckpoint.transform.position + Vector3.up * 0.2f);
                Quaternion targetRot = (sp != null) ? sp.rotation : ActiveCustomCheckpoint.transform.rotation;

                player.transform.position = targetPos;
                player.transform.rotation = targetRot;
            }
            else
            {
                RestartRun();
                return;
            }

            if (cc != null) cc.enabled = true;
            UnfreezePlayerControls();
        }

        public static void SetPlayerControlsActive(bool active)
        {
            GameObject player = FindPlayerEntity();
            if (player == null) return;

            CharacterController cc = GetPlayerController();
            if (cc != null) cc.enabled = active;

            foreach (var mb in player.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string typeName = mb.GetType().Name.ToLower();
                if (typeName.Contains("motor") || typeName.Contains("controller") ||
                    typeName.Contains("movement") || typeName.Contains("input") ||
                    typeName.Contains("look") || typeName.Contains("fps") ||
                    typeName.Contains("weapon") || typeName.Contains("gun") ||
                    typeName.Contains("shoot") || typeName.Contains("switch") ||
                    typeName.Contains("fire") || typeName.Contains("arm"))
                {
                    mb.enabled = active;
                }
            }
        }

        public static void UnfreezePlayerControls()
        {
            SetPlayerControlsActive(true);
        }

        public static void InitializeCustomLevel()
        {
            string curScene = SceneManager.GetActiveScene().name.ToLower();
            if (curScene.Contains("load") || curScene.Contains("menu") || curScene.Contains("boot"))
            {
                return;
            }

            GameObject player = FindPlayerEntity();
            Vector3 startPos = LevelSpawnPosition;

            if (player != null)
            {
                startPos = player.transform.position;
                LevelSpawnPosition = startPos;
            }

            SceneHarvestingService.HarvestAllSceneModels();
            SceneHarvestingService.DebugDumpSceneLighting();
            SceneHarvestingService.HideVanillaLevelGeometry();

            if (!string.IsNullOrEmpty(MapBrowserService.SelectedMapPath) && File.Exists(MapBrowserService.SelectedMapPath))
            {
                LevelPersistenceService.LoadLevelByFullPath(MapBrowserService.SelectedMapPath);
            }

            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                GameObject obj = PlacedObjects[i];
                if (obj != null && PlacedObjectTypes.TryGetValue(obj, out var t) && t == PlacedObjectType.SpawnGate)
                {
                    LevelSpawnPosition = obj.transform.position + Vector3.up * 0.2f;
                    startPos = LevelSpawnPosition;
                    break;
                }
            }

            bool hasStartingPlatform = false;
            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                GameObject obj = PlacedObjects[i];
                if (obj != null && (obj.name.ToLower().Contains("platform") || obj.name.ToLower().Contains("floor") || obj.name.ToLower().Contains("plateforme")))
                {
                    if (Vector3.Distance(obj.transform.position, startPos) < 50f)
                    {
                        hasStartingPlatform = true;
                        break;
                    }
                }
            }

            if (!hasStartingPlatform)
            {
                Vector3 p0Pos = startPos - new Vector3(0f, 1.2f, 0f);
                GameObject startPlatform = SpawnAssetByName("Floor_Platform_16x16", p0Pos, 0.55f);
                if (startPlatform == null) startPlatform = SpawnAssetByName("Platform", p0Pos, 0.8f);

                if (startPlatform != null)
                {
                    RegisterPlacedObject(startPlatform);
                }
            }

            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                if (PlacedObjects[i] != null)
                    AttachEditorSnappingProxy(PlacedObjects[i]);
            }

            if (player != null)
            {
                CharacterController cc = GetPlayerController();
                if (cc != null) cc.enabled = false;
                player.transform.position = startPos + Vector3.up * 0.2f;
                if (cc != null) cc.enabled = true;
            }

            LevelTimer = 0f;
            IsLevelCompleted = false;
            SetSnappingProxiesActive(false);
            _lightRefreshTimer = 0.35f;
            SetSpotlightMeshesVisible(false);
            CapturePlaytestSnapshots();

            UnfreezePlayerControls();
            IsLevelInitialized = true;
        }

        // =========================================================================
        // ROBUST RAYCAST OBJECT PICKING WITH HIERARCHY WALKING
        // =========================================================================

        public static GameObject GetAimedPlacedObject()
        {
            if (EditorViewportCamera.ViewportCamera == null) return null;

            Ray ray = Input.GetMouseButton(1)
                ? EditorViewportCamera.ViewportCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : EditorViewportCamera.ViewportCamera.ScreenPointToRay(Input.mousePosition);

            RaycastHit[] hits = Physics.RaycastAll(ray, 2000f, ~0, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0) return null;

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            // 1. Waypoint Markers Priority
            for (int h = 0; h < hits.Length; h++)
            {
                Collider col = hits[h].collider;
                if (col == null) continue;
                GameObject hitGo = col.gameObject;

                if (IsWaypointMarker(hitGo, out _, out _) || IsChildOfAnyWaypoint(hitGo, out hitGo))
                {
                    return hitGo;
                }
            }

            // 2. Placed Scene Objects: Find closest registered object
            for (int h = 0; h < hits.Length; h++)
            {
                Collider col = hits[h].collider;
                if (col == null) continue;
                GameObject hitGo = col.gameObject;

                // Skip editor highlights, beacons, and camera gizmos
                if (hitGo.name.Contains("Highlight") || hitGo.name.Contains("Beacon") || hitGo.name.Contains("Ghost")) continue;
                if (hitGo.name.StartsWith("Studio_3D_Gizmo") || (hitGo.transform.root != null && hitGo.transform.root.name == "Studio_3D_Gizmo_Root")) continue;
                if (_cachedPlayer != null && (hitGo == _cachedPlayer || hitGo.transform.root.gameObject == _cachedPlayer)) continue;

                Transform curr = hitGo.transform;
                while (curr != null)
                {
                    if (PlacedObjects.Contains(curr.gameObject))
                    {
                        return curr.gameObject;
                    }
                    curr = curr.parent;
                }
            }

            return null;
        }

        public static Quaternion GetCurrentCombinedRotation(CatalogAsset asset)
        {
            Quaternion snappedRot = Quaternion.Euler(TargetPitch, TargetYaw, TargetRoll);
            if (asset == null) return snappedRot;
            return snappedRot * asset.BaseRotation;
        }

        public static GameObject FindPlayerEntity()
        {
            string curScene = SceneManager.GetActiveScene().name.ToLower();
            if (curScene.Contains("menu") || curScene.Contains("load") || curScene.Contains("boot") || curScene.Contains("title"))
                return null;

            if (_cachedPlayer != null && _cachedPlayer.activeInHierarchy)
                return _cachedPlayer;

            GameObject tagged = GameObject.FindWithTag("Player");
            if (tagged != null)
            {
                _cachedPlayer = tagged;
                _cachedCharacterController = tagged.GetComponentInChildren<CharacterController>();
                PlayerControllerInstance = _cachedCharacterController;
                return _cachedPlayer;
            }

            CharacterController cc = GameObject.FindObjectOfType<CharacterController>();
            if (cc != null)
            {
                _cachedCharacterController = cc;
                PlayerControllerInstance = cc;
                _cachedPlayer = cc.transform.root.gameObject;
                return _cachedPlayer;
            }

            return null;
        }

        public static CharacterController GetPlayerController()
        {
            if (_cachedCharacterController != null && _cachedCharacterController.gameObject.activeInHierarchy)
                return _cachedCharacterController;

            GameObject p = FindPlayerEntity();
            if (p != null && _cachedCharacterController == null)
            {
                _cachedCharacterController = p.GetComponentInChildren<CharacterController>();
                PlayerControllerInstance = _cachedCharacterController;
            }

            return _cachedCharacterController;
        }

        public static void DrawPlaytestHUD()
        {
            Color origColor = GUI.color;

            float timerW = 220f;
            float timerH = 42f;
            float timerX = 20f;
            float timerY = Screen.height - timerH - 25f;

            GUI.color = new Color(0.04f, 0.08f, 0.14f, 0.85f);
            GUI.Box(new Rect(timerX, timerY, timerW, timerH), "");
            GUI.color = Color.cyan;
            int minutes = Mathf.FloorToInt(LevelTimer / 60F);
            int seconds = Mathf.FloorToInt(LevelTimer % 60F);
            int fraction = Mathf.FloorToInt((LevelTimer * 100) % 100);
            GUI.Label(new Rect(timerX + 15f, timerY + 10f, 190f, 25f), $"TIME:  {minutes:00}:{seconds:00}.{fraction:00}");

            if (IsLevelCompleted)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                float w = 460f;
                float h = 260f;
                float x = (Screen.width - w) * 0.5f;
                float y = (Screen.height - h) * 0.5f;

                GUI.color = new Color(0.03f, 0.06f, 0.1f, 0.95f);
                GUI.Box(new Rect(x, y, w, h), "");
                GUI.color = new Color(0.12f, 0.75f, 0.95f, 0.9f);
                GUI.Box(new Rect(x + 2, y + 2, w - 4, h - 4), "");

                GUI.color = Color.white;
                GUI.Label(new Rect(x + 30, y + 30, 400, 30), "=== COURSE COMPLETED! ===");
                GUI.color = Color.cyan;
                GUI.Label(new Rect(x + 30, y + 70, 400, 25), $"Final Time:  {minutes:00}:{seconds:00}.{fraction:00}");
                GUI.color = Color.gray;
                GUI.Label(new Rect(x + 30, y + 105, 400, 25), $"Course:  {MapBrowserService.SelectedMapName}");

                GUI.color = new Color(0.2f, 0.85f, 0.4f, 1f);
                if (GUI.Button(new Rect(x + 30, y + 150, 125, 42), "Restart")) RestartRun();

                GUI.color = new Color(0.2f, 0.7f, 1f, 1f);
                if (GUI.Button(new Rect(x + 165, y + 150, 130, 42), "Edit (F1)")) ToggleEditMode();

                GUI.color = new Color(0.85f, 0.3f, 0.3f, 1f);
                if (GUI.Button(new Rect(x + 305, y + 150, 125, 42), "Main Menu"))
                    SceneLoader.LoadLevel("MainMenu", false, true);
            }

            GUI.color = origColor;
        }
    }

    // =========================================================================
    // SECTION 2: DISK SERIALIZATION & LEVEL PERSISTENCE
    // =========================================================================

    public static class LevelPersistenceService
    {
        private static string ColorToHex(Color c)
        {
            byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            return $"{r:X2}{g:X2}{b:X2}";
        }

        private static Color HexToColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return Color.cyan;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length >= 6 &&
                byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) &&
                byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) &&
                byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            {
                return new Color(r / 255f, g / 255f, b / 255f, 1f);
            }
            return Color.cyan;
        }

        private static float ParseFloat(string str)
        {
            if (string.IsNullOrWhiteSpace(str)) return 0f;
            str = str.Trim().Replace(',', '.');
            if (float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                return val;
            return 0f;
        }

        public static void SaveLevel(string filename)
        {
            string saveDir = MapBrowserService.MyLevelsDir;
            if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

            string cleanName = Path.GetFileNameWithoutExtension(filename);
            if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "Default_Level";

            string path = Path.Combine(saveDir, $"{cleanName}.txt");
            List<string> lines = new List<string>();
            var inv = CultureInfo.InvariantCulture;

            LevelMetadata existingMeta = NativeLogsMenuHijacker.ReadLevelMetadata(path, cleanName);
            lines.Add($"#TITLE: {existingMeta.Title}");
            lines.Add($"#AUTHOR: {existingMeta.Author}");
            lines.Add($"#DIFFICULTY: {existingMeta.Difficulty}");
            lines.Add($"#DESC: {existingMeta.Description}");
            lines.Add($"#SCENE: {MapBrowserService.SelectedStagingScene}");

            for (int i = 0; i < EditorSessionManager.PlacedObjects.Count; i++)
            {
                GameObject obj = EditorSessionManager.PlacedObjects[i];
                if (obj == null || !obj.activeSelf) continue;

                Vector3 pos = EditorSessionManager.MotionPaths.ContainsKey(obj)
                    ? EditorSessionManager.MotionPaths[obj].PointA
                    : obj.transform.position;

                Quaternion rot = obj.transform.rotation;
                Vector3 scl = obj.transform.localScale; // Full 3D scale
                string name = obj.name.StartsWith("Custom_") ? obj.name.Substring(7) : obj.name;

                float param = 0f;
                string extraParams = "";

                if (EditorSessionManager.JumperForces.ContainsKey(obj))
                    param = EditorSessionManager.JumperForces[obj];
                else if (EditorSessionManager.TurbineSpeeds.ContainsKey(obj))
                    param = EditorSessionManager.TurbineSpeeds[obj];
                else if (EditorSessionManager.TurretFireDelays.ContainsKey(obj))
                    param = EditorSessionManager.TurretFireDelays[obj];
                else if (EditorSessionManager.LaserRotationSpeeds.ContainsKey(obj))
                    param = EditorSessionManager.LaserRotationSpeeds[obj];
                else if (EditorSessionManager.PlacedLights.ContainsKey(obj))
                {
                    LightConfig cfg = EditorSessionManager.PlacedLights[obj];
                    param = cfg.Intensity;
                    string hexColor = ColorToHex(cfg.Color);
                    extraParams = $";{cfg.SpotAngle.ToString("F1", inv)};{hexColor};{cfg.VolumetricIntensity.ToString("F2", inv)}";
                }

                string pathParams = ";PATH:0";
                if (EditorSessionManager.MotionPaths.ContainsKey(obj))
                {
                    var mp = EditorSessionManager.MotionPaths[obj];
                    pathParams = $";PATH:1:{mp.Speed.ToString("F2", inv)}:{mp.PointA.x.ToString("F4", inv)}:{mp.PointA.y.ToString("F4", inv)}:{mp.PointA.z.ToString("F4", inv)}:{mp.PointB.x.ToString("F4", inv)}:{mp.PointB.y.ToString("F4", inv)}:{mp.PointB.z.ToString("F4", inv)}:{mp.RotationSpeed.ToString("F2", inv)}:{mp.RotationAxis}:{mp.CustomAxis.x.ToString("F4", inv)}:{mp.CustomAxis.y.ToString("F4", inv)}:{mp.CustomAxis.z.ToString("F4", inv)}";
                }

                int parentIdx = -1;
                if (obj.transform.parent != null && EditorSessionManager.PlacedObjects.Contains(obj.transform.parent.gameObject))
                {
                    parentIdx = EditorSessionManager.PlacedObjects.IndexOf(obj.transform.parent.gameObject);
                }
                string parentParams = $";PARENT:{parentIdx}";

                // Exact 3D scale tag
                string scale3Params = $";SCALE3:{scl.x.ToString("F4", inv)}:{scl.y.ToString("F4", inv)}:{scl.z.ToString("F4", inv)}";

                lines.Add($"{name};{pos.x.ToString("F4", inv)};{pos.y.ToString("F4", inv)};{pos.z.ToString("F4", inv)};{scl.x.ToString("F4", inv)};{rot.x.ToString("F4", inv)};{rot.y.ToString("F4", inv)};{rot.z.ToString("F4", inv)};{rot.w.ToString("F4", inv)};{param.ToString("F2", inv)}{extraParams}{pathParams}{parentParams}{scale3Params}");
            }

            File.WriteAllLines(path, lines.ToArray());
            ThumbnailCaptureService.CaptureLevelThumbnail(path, EditorSessionManager.PlacedObjects, EditorSessionManager.LevelSpawnPosition);

            EditorSessionManager.ShowNotification($"Saved {lines.Count - 5} objects to {cleanName}.txt!");

            MapBrowserService.SelectedMapPath = path;
            MapBrowserService.SelectedMapName = cleanName;
            MapBrowserService.RefreshFiles();
        }

        public static void LoadLevel(string filename)
        {
            string cleanName = Path.GetFileNameWithoutExtension(filename);
            string path = Path.Combine(MapBrowserService.MyLevelsDir, $"{cleanName}.txt");
            if (!File.Exists(path))
                path = Path.Combine(MapBrowserService.DownloadedLevelsDir, $"{cleanName}.txt");

            if (!File.Exists(path))
            {
                EditorSessionManager.ShowNotification($"Save file '{cleanName}.txt' not found!");
                return;
            }

            LoadLevelByFullPath(path);
        }

        public static void LoadLevelByFullPath(string fullPath)
        {
            if (!File.Exists(fullPath)) return;

            string[] lines = File.ReadAllLines(fullPath);
            EditorSessionManager.ClearAllPlacedObjects();

            List<int> loadedParentIndices = new List<int>();
            int count = 0;

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string trimmed = line.Trim();

                if (trimmed.StartsWith("#SCENE:", StringComparison.OrdinalIgnoreCase))
                {
                    MapBrowserService.SelectedStagingScene = trimmed.Substring(7).Trim();
                    continue;
                }
                if (trimmed.StartsWith("#")) continue;

                string[] p = trimmed.Split(';');
                if (p.Length < 5) continue;

                string rawName = p[0];
                Vector3 pos = new Vector3(ParseFloat(p[1]), ParseFloat(p[2]), ParseFloat(p[3]));

                // Default uniform scale fallback
                float uniformScale = ParseFloat(p[4]);
                if (uniformScale <= 0.0001f) uniformScale = 0.55f;
                Vector3 scaleVec = Vector3.one * uniformScale;

                // Check for exact 3D non-uniform scale tag
                int scale3TagIdx = trimmed.IndexOf(";SCALE3:", StringComparison.OrdinalIgnoreCase);
                if (scale3TagIdx != -1)
                {
                    string scaleSub = trimmed.Substring(scale3TagIdx + 8).Split(';')[0];
                    string[] sParts = scaleSub.Split(':');
                    if (sParts.Length >= 3)
                    {
                        scaleVec = new Vector3(ParseFloat(sParts[0]), ParseFloat(sParts[1]), ParseFloat(sParts[2]));
                    }
                }

                // Prevent 0-scale invisible objects
                scaleVec.x = Mathf.Max(0.01f, scaleVec.x);
                scaleVec.y = Mathf.Max(0.01f, scaleVec.y);
                scaleVec.z = Mathf.Max(0.01f, scaleVec.z);

                Quaternion rot = Quaternion.identity;
                if (p.Length >= 9)
                {
                    rot = new Quaternion(ParseFloat(p[5]), ParseFloat(p[6]), ParseFloat(p[7]), ParseFloat(p[8]));
                    if (rot.x == 0f && rot.y == 0f && rot.z == 0f && rot.w == 0f) rot = Quaternion.identity;
                }

                float customParam = (p.Length >= 10) ? ParseFloat(p[9]) : 0f;

                GameObject obj = EditorSessionManager.SpawnAssetByName(rawName, pos, scaleVec, rot);
                if (obj != null)
                {
                    obj.transform.localScale = scaleVec; // Ensures non-uniform scale is applied
                    string lowName = rawName.ToLower();

                    if (customParam > 0f)
                    {
                        if (lowName.Contains("jumper"))
                            EditorSessionManager.ApplyJumperForce(obj, customParam);
                        else if (lowName.Contains("helix"))
                            EditorSessionManager.ApplyTurbineSpeed(obj, customParam);
                        else if (lowName.Contains("turret"))
                            EditorSessionManager.ApplyTurretSettings(obj, customParam, 1500f);
                    }

                    if (lowName.Contains("rotating") && customParam != 0f)
                        EditorSessionManager.LaserRotationSpeeds[obj] = customParam;

                    if (lowName.Contains("spotlight") || lowName.Contains("sunlight"))
                    {
                        LightConfig cfg = new LightConfig();
                        cfg.IsDirectional = lowName.Contains("sunlight");
                        if (customParam > 0f) cfg.Intensity = customParam;

                        if (p.Length >= 11) cfg.SpotAngle = ParseFloat(p[10]);
                        if (p.Length >= 12) cfg.Color = HexToColor(p[11]);
                        if (p.Length >= 13) cfg.VolumetricIntensity = ParseFloat(p[12]);

                        EditorSessionManager.ApplyLightConfig(obj, cfg);

                        if (cfg.IsDirectional)
                        {
                            StudioGizmoController.AttachSunVisualWidget(obj);
                        }
                    }

                    int pathTagIdx = trimmed.IndexOf(";PATH:");
                    if (pathTagIdx != -1)
                    {
                        string pathSub = trimmed.Substring(pathTagIdx + 6).Split(';')[0];
                        string[] pathParts = pathSub.Split(':');

                        if (pathParts.Length >= 8 && pathParts[0] == "1")
                        {
                            float spd = ParseFloat(pathParts[1]);
                            Vector3 pA = new Vector3(ParseFloat(pathParts[2]), ParseFloat(pathParts[3]), ParseFloat(pathParts[4]));
                            Vector3 pB = new Vector3(ParseFloat(pathParts[5]), ParseFloat(pathParts[6]), ParseFloat(pathParts[7]));

                            float rotSpd = (pathParts.Length >= 9) ? ParseFloat(pathParts[8]) : 0f;
                            int rotAxis = (pathParts.Length >= 10 && int.TryParse(pathParts[9], out int ra)) ? ra : 1;

                            Vector3 customAxis = Vector3.up;
                            if (pathParts.Length >= 13)
                            {
                                customAxis = new Vector3(ParseFloat(pathParts[10]), ParseFloat(pathParts[11]), ParseFloat(pathParts[12]));
                                if (customAxis.sqrMagnitude < 0.0001f) customAxis = Vector3.up;
                            }
                            else
                            {
                                if (rotAxis == 0) customAxis = Vector3.right;
                                else if (rotAxis == 2) customAxis = Vector3.forward;
                                else customAxis = Vector3.up;
                            }

                            obj.transform.position = pA;
                            EditorSessionManager.MotionPaths[obj] = new ObjectMotionPath
                            {
                                PointA = pA,
                                PointB = pB,
                                Speed = spd > 0.01f ? spd : 3.5f,
                                RotationSpeed = rotSpd,
                                RotationAxis = rotAxis,
                                CustomAxis = customAxis,
                                IsActive = true
                            };
                        }
                    }

                    int pIndex = -1;
                    int parentTagIdx = trimmed.IndexOf(";PARENT:");
                    if (parentTagIdx != -1)
                    {
                        string pVal = trimmed.Substring(parentTagIdx + 8).Split(';')[0].Trim();
                        int.TryParse(pVal, out pIndex);
                    }
                    loadedParentIndices.Add(pIndex);

                    EditorSessionManager.RegisterPlacedObject(obj);
                    count++;
                }
            }

            for (int i = 0; i < EditorSessionManager.PlacedObjects.Count; i++)
            {
                if (i < loadedParentIndices.Count && loadedParentIndices[i] >= 0 && loadedParentIndices[i] < EditorSessionManager.PlacedObjects.Count)
                {
                    GameObject child = EditorSessionManager.PlacedObjects[i];
                    GameObject parent = EditorSessionManager.PlacedObjects[loadedParentIndices[i]];
                    if (child != null && parent != null && child != parent)
                    {
                        child.transform.SetParent(parent.transform, true);
                        EditorSessionManager.RecalculateParentChildCount(parent);
                    }
                }
            }

            string fName = Path.GetFileNameWithoutExtension(fullPath);
            EditorSessionManager.ShowNotification($"Loaded {count} objects from {fName}.txt!");
            StudioUIManager.RefreshHierarchy();
        }
    }

    // =========================================================================
    // SECTION 3: MAP BROWSER & STAGING SCENE SERVICES
    // =========================================================================

    public static class MapBrowserService
    {
        public static bool IsBrowserOpen = false;
        public static string SelectedMapPath = "";
        public static string SelectedMapName = "Default_Level";

        public static List<string> AvailableStagingScenes = new List<string>();
        public static string SelectedStagingScene = "level01_Spark01";

        public static string MyLevelsDir => Path.Combine(Directory.GetCurrentDirectory(), "UserData", "MyLevels");
        public static string DownloadedLevelsDir => Path.Combine(Directory.GetCurrentDirectory(), "UserData", "DownloadedLevels");

        public static void ScanStagingScenes()
        {
            AvailableStagingScenes.Clear();
            AvailableStagingScenes.Add("level01_Spark01");
        }

        public static void EnsureDirectories()
        {
            if (!Directory.Exists(MyLevelsDir)) Directory.CreateDirectory(MyLevelsDir);
            if (!Directory.Exists(DownloadedLevelsDir)) Directory.CreateDirectory(DownloadedLevelsDir);

            string defaultMyPath = Path.Combine(MyLevelsDir, "Default_Level.txt");
            if (!File.Exists(defaultMyPath))
            {
                Vector3 spawn = EditorSessionManager.LevelSpawnPosition;
                File.WriteAllText(defaultMyPath, $"#TITLE: Default Level\n#AUTHOR: Community\n#DIFFICULTY: Normal\n#DESC: Starter platform.\n#SCENE: level01_Spark01\nFloor_Platform_16x16;{spawn.x:F4};{(spawn.y - 1.2f):F4};{spawn.z:F4};0.5500;0.0000;0.0000;0.0000;1.0000;0.00\n");
            }

            RefreshFiles();
        }

        public static void RefreshFiles()
        {
            if (string.IsNullOrEmpty(SelectedMapPath) || !File.Exists(SelectedMapPath))
            {
                string def = Path.Combine(MyLevelsDir, "Default_Level.txt");
                if (File.Exists(def))
                {
                    SelectedMapPath = def;
                    SelectedMapName = "Default_Level";
                }
            }
        }

        public static void LaunchSelectedMap()
        {
            IsBrowserOpen = false;
            EditorSessionManager.CustomLevelSelected = true;
            EditorSessionManager.IsCustomSessionActive = true;
            EditorSessionManager.IsLevelInitialized = false;

            string targetScene = "level01_Spark01";
            string requested = !string.IsNullOrWhiteSpace(SelectedStagingScene) ? SelectedStagingScene.Trim() : "";

            if (!string.IsNullOrEmpty(requested) && AvailableStagingScenes.Contains(requested))
            {
                targetScene = requested;
            }

            SceneLoader.LoadLevel(targetScene, false, true);
        }
    }
}