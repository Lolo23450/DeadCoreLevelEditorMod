using Il2Cpp;
using MelonLoader;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static UnityEngine.GraphicsBuffer;
using File = System.IO.File;
using SceneManager = UnityEngine.SceneManagement.SceneManager;

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 1: EDITOR SESSION & LIFECYCLE MANAGER
    // =========================================================================

    public static class EditorSessionManager
    {
        // Session and State Tracking
        public static bool CustomLevelSelected = true;
        public static bool IsCustomSessionActive = false;
        public static bool IsEditModeActive = false;
        public static bool IsLevelInitialized = false;

        public static bool AreTriggersVisible = false;

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

        // Unified Clipboard System
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
        public static float ActiveJumperReactivateDelay = 4.0f;
        public static float ActiveTurbineSpeed = 35.0f;
        public static float ActiveTurretFireDelay = 1.0f;
        public static float ActiveLaserRotationSpeed = 45.0f;
        public static float DefaultPathSpeed = 3.5f;

        // Unified Entity Component Registry
        public static readonly Dictionary<GameObject, EditorEntityData> EntityRegistry = new Dictionary<GameObject, EditorEntityData>();

        // Motion Path & In-Scene Waypoint Tracking
        public static Dictionary<GameObject, ObjectMotionPath> MotionPaths = new Dictionary<GameObject, ObjectMotionPath>();
        public static readonly Dictionary<GameObject, GameObject> WaypointMarkersA = new Dictionary<GameObject, GameObject>();
        public static readonly Dictionary<GameObject, GameObject> WaypointMarkersB = new Dictionary<GameObject, GameObject>();
        public static readonly Dictionary<GameObject, LineRenderer> WaypointLines = new Dictionary<GameObject, LineRenderer>();

        public static GameObject PathEditTarget = null;
        public static GameObject ParentingChildTarget = null;
        public static GameObject RepositionTarget = null;
        public static Vector3 RepositionStartPosition = Vector3.zero;

        // Tracking Lists & Fast Lookups
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
        public static readonly Dictionary<GameObject, Helix> CachedHelixScripts = new Dictionary<GameObject, Helix>();

        // Synchronized Component Dictionaries
        public static Dictionary<GameObject, float> JumperForces = new Dictionary<GameObject, float>();
        public static Dictionary<GameObject, float> JumperReactivateDelays = new Dictionary<GameObject, float>();
        public static Dictionary<GameObject, bool> JumperActiveStates = new Dictionary<GameObject, bool>();
        public static Dictionary<GameObject, float> TurbineSpeeds = new Dictionary<GameObject, float>();
        public static Dictionary<GameObject, float> TurretFireDelays = new Dictionary<GameObject, float>();
        public static Dictionary<GameObject, float> LaserRotationSpeeds = new Dictionary<GameObject, float>();
        public static Dictionary<GameObject, LightConfig> PlacedLights = new Dictionary<GameObject, LightConfig>();
        public static Dictionary<GameObject, NeonConfig> PlacedNeonConfigs = new Dictionary<GameObject, NeonConfig>();

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

        // Snapshot Structure
        public struct PlaytestTransformSnapshot
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 LocalScale;
        }

        public static readonly Dictionary<GameObject, PlaytestTransformSnapshot> PlaytestSnapshots = new Dictionary<GameObject, PlaytestTransformSnapshot>();

        // =========================================================================
        // SECTION 2: ENTITY DATA BINDING & CONVERSION
        // =========================================================================

        public static EditorEntityData ExtractEntityData(GameObject obj)
        {
            if (obj == null) return new EditorEntityData();

            if (EntityRegistry.TryGetValue(obj, out var existing))
            {
                SyncDictionariesToEntityData(obj, existing);
                return existing.Clone();
            }

            var data = new EditorEntityData();
            SyncDictionariesToEntityData(obj, data);
            EntityRegistry[obj] = data;
            return data.Clone();
        }

        private static void SyncDictionariesToEntityData(GameObject obj, EditorEntityData data)
        {
            if (obj == null || data == null) return;

            if (JumperForces.TryGetValue(obj, out float jf))
            {
                var jc = data.GetOrCreate<JumperConfig>();
                jc.Force = jf;
                if (JumperActiveStates.TryGetValue(obj, out bool ja)) jc.IsActive = ja;
                if (JumperReactivateDelays.TryGetValue(obj, out float jd)) jc.ReactivateDelay = jd;
            }

            if (TurbineSpeeds.TryGetValue(obj, out float ts))
            {
                var tc = data.GetOrCreate<TurbineConfig>();
                tc.Speed = ts;
            }

            if (TurretFireDelays.TryGetValue(obj, out float fd))
            {
                var tc = data.GetOrCreate<TurretConfig>();
                tc.FireDelay = fd;
            }

            if (LaserRotationSpeeds.TryGetValue(obj, out float lrs))
            {
                var lc = data.GetOrCreate<LaserConfig>();
                lc.RotationSpeed = lrs;
                lc.IsRotating = Mathf.Abs(lrs) > 0.01f;
            }

            if (PlacedLights.TryGetValue(obj, out var lcCfg))
            {
                data.Set(lcCfg.Clone());
            }

            if (MotionPaths.TryGetValue(obj, out var mp))
            {
                data.Set(mp.Clone());
            }
            else
            {
                data.Remove<ObjectMotionPath>();
            }

            if (SwitchService.PlacedSwitches.TryGetValue(obj, out var swCfg))
            {
                data.Set(swCfg.Clone());
            }

            if (PlacedNeonConfigs.TryGetValue(obj, out var neonCfg))
            {
                data.Set(neonCfg.Clone());
            }

            if (ProceduralCableService.PlacedCables.TryGetValue(obj, out var cableCfg))
            {
                data.Set(cableCfg.Clone());
            }

            if (StructuralTrussService.PlacedTrusses.TryGetValue(obj, out var trussCfg))
            {
                data.Set(trussCfg.Clone());
            }

            if (GravityAreaService.PlacedGravityConfigs.TryGetValue(obj, out var gravCfg))
            {
                data.Set(gravCfg.Clone());
            }
            else
            {
                GravityArea ga = obj.GetComponentInChildren<GravityArea>(true);
                if (ga != null)
                {
                    var gCfg = data.GetOrCreate<GravityConfig>();
                    gCfg.GravityForce = ga.gravity.magnitude > 0.01f ? ga.gravity.magnitude : 9.81f;
                    Vector3 localDir = Quaternion.Inverse(obj.transform.rotation) * ga.gravity.normalized;
                    gCfg.LocalAxis = localDir.sqrMagnitude > 0.01f ? localDir : Vector3.up;
                    gCfg.AffectOthers = ga.affectOthers;
                    gCfg.ChangeGravity = ga._changeGravity;
                    gCfg.IsActive = ga.enabled;
                }
                else
                {
                    GravityReceiver gr = obj.GetComponentInChildren<GravityReceiver>(true);
                    if (gr != null)
                    {
                        var gCfg = data.GetOrCreate<GravityConfig>();
                        gCfg.IsActive = gr.enabled;
                    }
                }
            }
            if (MapAudioService.PlacedAudioConfigs.TryGetValue(obj, out var audioCfg))
            {
                data.Set(audioCfg.Clone());
            }
            if (PlacedObjectTypes.TryGetValue(obj, out var pType))
            {
                if (pType == PlacedObjectType.SpawnGate || pType == PlacedObjectType.GoalGate || pType == PlacedObjectType.Checkpoint)
                {
                    var gateCfg = data.GetOrCreate<GateConfig>();
                    gateCfg.IsSpawn = (pType == PlacedObjectType.SpawnGate);
                    gateCfg.IsGoal = (pType == PlacedObjectType.GoalGate);
                    CheckPointScript cp = obj.GetComponentInChildren<CheckPointScript>();
                    if (cp != null) gateCfg.CheckpointId = cp._id;
                }
                else if (pType == PlacedObjectType.SkyboxController)
                {
                    data.Set(SkyboxControllerService.ActiveConfig.Clone());
                }
                else if (pType == PlacedObjectType.GravityArea && !data.Has<GravityConfig>())
                {
                    GravityArea ga = obj.GetComponentInChildren<GravityArea>(true);
                    if (ga != null)
                    {
                        var gCfg = data.GetOrCreate<GravityConfig>();

                        if (ga.gravity.sqrMagnitude > 0.001f)
                        {
                            gCfg.GravityForce = ga.gravity.magnitude;
                            Vector3 unrotated = Quaternion.Inverse(obj.transform.rotation) * ga.gravity.normalized;
                            gCfg.LocalAxis = unrotated.sqrMagnitude > 0.001f ? unrotated : Vector3.up;
                        }
                        else
                        {
                            gCfg.GravityForce = 0f;
                            gCfg.LocalAxis = Vector3.up;
                        }

                        gCfg.AffectOthers = ga.affectOthers;
                        gCfg.ChangeGravity = ga._changeGravity;
                        gCfg.IsActive = ga.enabled;
                    }
                }
            }
        }

        public static void ApplyEntityData(GameObject obj, EditorEntityData data)
        {
            if (obj == null || data == null) return;

            EntityRegistry[obj] = data.Clone();

            if (data.TryGetComponent<JumperConfig>(out var jc))
            {
                ApplyJumperForce(obj, jc.Force);
                ApplyJumperReactivateDelay(obj, jc.ReactivateDelay);
                ApplyJumperActive(obj, jc.IsActive);
            }

            if (data.TryGetComponent<TurbineConfig>(out var tc))
            {
                ApplyTurbineSpeed(obj, tc.Speed);
            }

            if (data.TryGetComponent<TurretConfig>(out var turC))
            {
                ApplyTurretSettings(obj, turC.FireDelay, turC.FirePower);
            }

            if (data.TryGetComponent<LaserConfig>(out var lc))
            {
                LaserRotationSpeeds[obj] = lc.RotationSpeed;
            }

            if (data.TryGetComponent<LightConfig>(out var lightC))
            {
                ApplyLightConfig(obj, lightC.Clone());
            }

            if (data.TryGetComponent<NeonConfig>(out var nc))
            {
                ApplyNeonConfig(obj, nc.Clone());
            }

            if (data.TryGetComponent<CableConfig>(out var cblCfg))
            {
                ProceduralCableService.ApplyCableConfig(obj, cblCfg.Clone());
            }

            if (data.TryGetComponent<TrussConfig>(out var trc))
            {
                StructuralTrussService.ApplyTrussConfig(obj, trc.Clone());
            }

            if (data.TryGetComponent<GravityConfig>(out var gravCfg))
            {
                GravityAreaService.ApplyGravityConfig(obj, gravCfg.Clone());
            }

            if (data.TryGetComponent<ObjectMotionPath>(out var mp) && mp.IsActive && (mp.TotalDistance > 0.05f || Mathf.Abs(mp.RotationSpeed) > 0.01f))
            {
                MotionPaths[obj] = mp.Clone();
                var rb = obj.GetComponent<Rigidbody>();
                if (rb == null) rb = obj.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }
            else
            {
                MotionPaths.Remove(obj);
            }

            if (data.TryGetComponent<SkyboxConfig>(out var sc))
            {
                SkyboxControllerService.Initialize(obj, sc.Clone());
            }

            if (data.TryGetComponent<SwitchConfig>(out var swc))
            {
                SwitchService.ApplySwitchConfig(obj, swc.Clone());
            }

            if (data.TryGetComponent<GateConfig>(out var gateCfg))
            {
                CheckPointScript cp = obj.GetComponentInChildren<CheckPointScript>();
                if (gateCfg.IsSpawn)
                {
                    PlacedObjectTypes[obj] = PlacedObjectType.SpawnGate;
                    ApplyGateVisualTint(obj, new Color(1.0f, 0.45f, 0.05f));
                    if (cp != null) cp._id = 0;
                }
                else if (gateCfg.IsGoal)
                {
                    PlacedObjectTypes[obj] = PlacedObjectType.GoalGate;
                    PlacedGoalGate = obj;
                    ApplyGateVisualTint(obj, new Color(0.1f, 0.65f, 1.0f));
                    if (cp != null) cp._id = 9999;
                }
                else
                {
                    PlacedObjectTypes[obj] = PlacedObjectType.Checkpoint;
                    if (cp != null && gateCfg.CheckpointId != 0) cp._id = gateCfg.CheckpointId;
                }
            }
            if (data.TryGetComponent<AudioConfig>(out var audioCfg))
            {
                PlacedObjectTypes[obj] = PlacedObjectType.AudioController;
                MapAudioService.ApplyAudioConfig(obj, audioCfg);
            }
        }

        // =========================================================================
        // SECTION 3: PLAYTEST SNAPSHOTS & RESTORATION
        // =========================================================================

        public static void CapturePlaytestSnapshots()
        {
            PlaytestSnapshots.Clear();
            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                GameObject obj = PlacedObjects[i];
                if (obj == null) continue;

                PlaytestSnapshots[obj] = new PlaytestTransformSnapshot
                {
                    Position = obj.transform.position,
                    Rotation = obj.transform.rotation,
                    LocalScale = obj.transform.localScale
                };
            }
        }

        public static void RestorePlaytestSnapshots()
        {
            if (PlaytestSnapshots.Count == 0) return;

            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                GameObject obj = PlacedObjects[i];
                if (obj == null) continue;

                if (PlaytestSnapshots.TryGetValue(obj, out PlaytestTransformSnapshot snap))
                {
                    Rigidbody rb = obj.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.isKinematic = true;
                        rb.useGravity = false;
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }

                    if (MotionPaths.TryGetValue(obj, out var mp))
                    {
                        if (mp.PointA != Vector3.zero || obj.transform.position == Vector3.zero)
                            obj.transform.position = mp.PointA;
                    }
                    else if (obj.transform.parent == null)
                    {
                        if (snap.Position != Vector3.zero || obj.transform.position == Vector3.zero)
                            obj.transform.position = snap.Position;
                        obj.transform.rotation = snap.Rotation;
                        obj.transform.localScale = snap.LocalScale;
                    }

                    if (rb != null)
                    {
                        rb.position = obj.transform.position;
                        rb.rotation = obj.transform.rotation;
                    }
                }

                if (!obj.activeSelf) obj.SetActive(true);

                Renderer[] rends = obj.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < rends.Length; r++)
                {
                    if (rends[r] != null) rends[r].enabled = true;
                }

                StudioGizmoController.InvalidateCachedCenter(obj);
            }

            UpdateSelectionHighlight();
        }

        // =========================================================================
        // SECTION 4: SELECTION, PARENTING & CLIPBOARD
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
                if (obj == null || IsWaypointMarker(obj, out _, out _)) continue;

                string rawName = obj.name.StartsWith("Custom_") ? obj.name.Substring(7) : obj.name;
                EditorEntityData entityData = ExtractEntityData(obj);

                Clipboard.Add(new ClipboardItem
                {
                    AssetName = rawName,
                    RelativeOffset = obj.transform.position - groupCenter,
                    Rotation = obj.transform.rotation,
                    Scale = obj.transform.localScale,
                    EntityData = entityData
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

            Vector3 pasteOrigin = (InteractionMode == EditorInteractionMode.PlacementMode)
                ? PlacementHologramController.TargetPosition
                : ((SelectedObject != null) ? SelectedObject.transform.position : LevelSpawnPosition) + new Vector3(2.5f, 0f, 2.5f);

            SelectedObjects.Clear();

            for (int i = 0; i < Clipboard.Count; i++)
            {
                ClipboardItem item = Clipboard[i];
                Vector3 spawnPos = pasteOrigin + item.RelativeOffset;

                GameObject pasted = SpawnAssetByName(item.AssetName, spawnPos, item.Scale, item.Rotation);
                if (pasted != null)
                {
                    if (item.EntityData != null)
                    {
                        ApplyEntityData(pasted, item.EntityData.Clone());
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
                        EntityDataSnapshot = item.EntityData?.Clone()
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
                    if (item.EntityData != null)
                    {
                        ApplyEntityData(pasted, item.EntityData.Clone());
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
                        EntityDataSnapshot = item.EntityData?.Clone()
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

                if (ProceduralCableService.IsCableHandle(target, out GameObject cOwner, out _))
                {
                    target = cOwner;
                }
                else if (StructuralTrussService.IsTrussHandle(target, out GameObject tOwner, out _))
                {
                    target = tOwner;
                }
                else if (IsWaypointMarker(target, out GameObject owner, out _))
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
        // SECTION 5: MODES & SELECTION LOGIC
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

        public static void SelectAllPlacedObjects()
        {
            SelectedObjects.Clear();
            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                GameObject obj = PlacedObjects[i];
                if (obj != null && obj.activeSelf && !IsWaypointMarker(obj, out _, out _))
                {
                    SelectedObjects.Add(obj);
                }
            }

            SelectedObject = SelectedObjects.Count > 0 ? SelectedObjects[SelectedObjects.Count - 1] : null;

            UpdateSelectionHighlight();
            StudioUIManager.NotifyObjectSelected(SelectedObject);
            StudioUIManager.RefreshHierarchy();
            ShowNotification($"Selected all {SelectedObjects.Count} object(s) [Ctrl+A]");
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

        public static void UpdateSelectionHighlight()
        {
            CleanHighlightPool();

            if (SelectedObjects.Count == 0 || !IsEditModeActive || InteractionMode != EditorInteractionMode.SelectMode)
                return;

            Material primaryMat = GizmoMaterialCache.Red;

            for (int i = 0; i < SelectedObjects.Count; i++)
            {
                GameObject obj = SelectedObjects[i];
                if (obj == null || !obj.activeSelf || IsWaypointMarker(obj, out _, out _)) continue;

                Bounds b = PlacementHologramController.CalculateOptimizedProxyBounds(obj);
                Vector3 worldCenter = obj.transform.TransformPoint(b.center);

                GameObject wireBox = CreateHollowWireframeBox(worldCenter, obj.transform.rotation, Vector3.Scale(b.size * 1.02f, obj.transform.lossyScale), primaryMat);
                _highlightBoxes.Add(wireBox);
            }
        }

        private static GameObject CreateHollowWireframeBox(Vector3 pos, Quaternion rot, Vector3 size, Material mat)
        {
            GameObject wireObj = new GameObject("Studio_Selection_Wireframe");
            wireObj.transform.position = pos;
            wireObj.transform.rotation = rot;
            wireObj.layer = 2;

            MeshFilter mf = wireObj.AddComponent<MeshFilter>();
            MeshRenderer mr = wireObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float t = Mathf.Clamp(maxDim * 0.025f, 0.10f, 0.40f);

            Vector3 h = size * 0.5f;

            List<Vector3> verts = new List<Vector3>(96);
            List<int> tris = new List<int>(144);

            AddBeam(new Vector3(0f, -h.y, -h.z), new Vector3(size.x + t, t, t), verts, tris);
            AddBeam(new Vector3(0f, h.y, -h.z), new Vector3(size.x + t, t, t), verts, tris);
            AddBeam(new Vector3(0f, -h.y, h.z), new Vector3(size.x + t, t, t), verts, tris);
            AddBeam(new Vector3(0f, h.y, h.z), new Vector3(size.x + t, t, t), verts, tris);

            AddBeam(new Vector3(-h.x, 0f, -h.z), new Vector3(t, size.y + t, t), verts, tris);
            AddBeam(new Vector3(h.x, 0f, -h.z), new Vector3(t, size.y + t, t), verts, tris);
            AddBeam(new Vector3(-h.x, 0f, h.z), new Vector3(t, size.y + t, t), verts, tris);
            AddBeam(new Vector3(h.x, 0f, h.z), new Vector3(t, size.y + t, t), verts, tris);

            AddBeam(new Vector3(-h.x, -h.y, 0f), new Vector3(t, t, size.z + t), verts, tris);
            AddBeam(new Vector3(h.x, -h.y, 0f), new Vector3(t, t, size.z + t), verts, tris);
            AddBeam(new Vector3(-h.x, h.y, 0f), new Vector3(t, t, size.z + t), verts, tris);
            AddBeam(new Vector3(h.x, h.y, 0f), new Vector3(t, t, size.z + t), verts, tris);

            Mesh m = new Mesh
            {
                name = "Thick_Wireframe_Box_Mesh",
                vertices = verts.ToArray(),
                triangles = tris.ToArray()
            };
            m.RecalculateNormals();
            m.RecalculateBounds();
            mf.sharedMesh = m;

            return wireObj;
        }

        private static void AddBeam(Vector3 center, Vector3 beamSize, List<Vector3> verts, List<int> tris)
        {
            int baseIdx = verts.Count;
            Vector3 bh = beamSize * 0.5f;

            verts.Add(center + new Vector3(-bh.x, -bh.y, -bh.z));
            verts.Add(center + new Vector3(bh.x, -bh.y, -bh.z));
            verts.Add(center + new Vector3(bh.x, -bh.y, -bh.z));
            verts.Add(center + new Vector3(-bh.x, bh.y, -bh.z));
            verts.Add(center + new Vector3(-bh.x, -bh.y, -bh.z));
            verts.Add(center + new Vector3(bh.x, -bh.y, -bh.z));
            verts.Add(center + new Vector3(bh.x, bh.y, -bh.z));
            verts.Add(center + new Vector3(-bh.x, bh.y, -bh.z));

            int[] f = new int[]
            {
                0, 2, 1, 0, 3, 2,
                5, 6, 4, 4, 6, 7,
                0, 7, 3, 0, 4, 7,
                1, 2, 6, 1, 6, 5,
                3, 6, 2, 3, 7, 6,
                0, 1, 5, 0, 5, 4
            };

            for (int i = 0; i < f.Length; i++)
            {
                tris.Add(baseIdx + f[i]);
            }
        }

        public static void CleanHighlightPool()
        {
            for (int i = 0; i < _highlightBoxes.Count; i++)
            {
                if (_highlightBoxes[i] != null) GameObject.Destroy(_highlightBoxes[i]);
            }
            _highlightBoxes.Clear();
        }

        public static void ShowNotification(string msg)
        {
            StudioUIManager.SetNotificationText(msg);
        }

        // =========================================================================
        // SECTION 6: SIMULATION & MODE LIFECYCLE (WITH AUTO-SAVE BACKUP)
        // =========================================================================

        public static void ToggleEditMode()
        {
            IsEditModeActive = !IsEditModeActive;
            SetSpotlightMeshesVisible(IsEditModeActive);

            GameObject player = FindPlayerEntity();
            SetSimulationActive(!IsEditModeActive);
            SetSnappingProxiesActive(IsEditModeActive ? EditorConfigService.Config.SnappingProxiesVisible : false);
            SetJumperSimulationActive(!IsEditModeActive);
            MapAudioService.RefreshPlaybackForActiveMode();

            if (IsEditModeActive)
            {
                RestorePlaytestSnapshots();
                SwitchService.ResetAllSwitchesForPlaytest(enteringPlaytest: false);
                ForceRefreshAllJumpers();

                if (player != null)
                {
                    FrozenPlayerPosition = player.transform.position;
                    FrozenPlayerRotation = player.transform.rotation;
                    SetPlayerControlsActive(false);
                }

                if (PlayerCameraInstance == null || !PlayerCameraInstance.gameObject.activeInHierarchy)
                {
                    PlayerCameraInstance = Camera.main ?? GameObject.FindObjectOfType<Camera>();
                }

                EditorViewportCamera.InitializeCamera(PlayerCameraInstance);
                StudioUIManager.InitializeUI();
                StudioUIManager.SetUIVisible(true);

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                SetInteractionMode(EditorInteractionMode.SelectMode);

                ProceduralCableService.UpdateAllCableHandles();
                StructuralTrussService.UpdateAllTrussHandles();
            }
            else
            {
                if (EditorConfigService.Config.AutoSaveOnPlaytest)
                {
                    StudioUIManager.PerformAutoSaveBackup();
                }

                CapturePlaytestSnapshots();
                SwitchService.ResetAllSwitchesForPlaytest(enteringPlaytest: true);
                ForceRefreshAllTurbines();
                ForceRefreshAllJumpers();

                IsLevelCompleted = false;

                EditorViewportCamera.DestroyCamera();
                PlacementHologramController.DestroyPreview();
                StudioGizmoController.DestroyGizmo();
                CleanHighlightPool();
                HideAllWaypointMarkers();

                ProceduralCableService.HideAllCableHandles();
                StructuralTrussService.HideAllTrussHandles();

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

        public static void SetSimulationActive(bool active)
        {
            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                GameObject obj = PlacedObjects[i];
                if (obj == null) continue;

                if (PlacedObjectTypes.TryGetValue(obj, out var objType) && objType == PlacedObjectType.Turbine)
                    continue;

                TurretScript[] ts = obj.GetComponentsInChildren<TurretScript>(true);
                for (int s = 0; s < ts.Length; s++) if (ts[s] != null) ts[s].enabled = active;

                Animator[] animators = obj.GetComponentsInChildren<Animator>(true);
                for (int a = 0; a < animators.Length; a++) if (animators[a] != null) animators[a].enabled = active;

                Animation[] animations = obj.GetComponentsInChildren<Animation>(true);
                for (int a = 0; a < animations.Length; a++) if (animations[a] != null) animations[a].enabled = active;

                Rigidbody[] rbs = obj.GetComponentsInChildren<Rigidbody>(true);
                for (int r = 0; r < rbs.Length; r++)
                {
                    if (rbs[r] != null)
                    {
                        rbs[r].isKinematic = true;
                        rbs[r].useGravity = false;
                        rbs[r].velocity = Vector3.zero;
                        rbs[r].angularVelocity = Vector3.zero;
                    }
                }
            }
        }

        public static void SetJumperSimulationActive(bool active)
        {
            for (int i = 0; i < PlacedJumpers.Count; i++)
            {
                GameObject obj = PlacedJumpers[i];
                if (obj == null) continue;

                bool shouldBeActive = active;
                if (active && JumperActiveStates.TryGetValue(obj, out bool desiredState))
                {
                    shouldBeActive = desiredState;
                }

                ApplyJumperActive(obj, shouldBeActive);
            }
        }

        public static void ApplyJumperReactivateDelay(GameObject jumperObj, float delay)
        {
            if (jumperObj == null) return;
            delay = Mathf.Max(0.1f, delay);
            JumperReactivateDelays[jumperObj] = delay;

            if (EntityRegistry.TryGetValue(jumperObj, out var data))
            {
                var jc = data.GetOrCreate<JumperConfig>();
                jc.ReactivateDelay = delay;
            }

            Jumper[] jumpers = jumperObj.GetComponentsInChildren<Jumper>(true);
            for (int i = 0; i < jumpers.Length; i++)
            {
                Jumper j = jumpers[i];
                if (j == null) continue;

                // DeadCore's native Jumper field controlling cooldown before reactivation
                j.switchDuration = delay;

                // Also keep any child Interuptor synced to this exact duration
                if (j._interuptor != null)
                {
                    j._interuptor.IsAutoSwitch = true;
                    j._interuptor.TimeBeforeSwitch = delay;
                    j._interuptor._warningTime = Mathf.Min(1.5f, delay * 0.3f);
                }
            }
        }

        public static void ApplyJumperActive(GameObject jumperObj, bool active)
        {
            if (jumperObj == null) return;
            JumperActiveStates[jumperObj] = active;

            float delay = 4.0f;
            if (JumperReactivateDelays.TryGetValue(jumperObj, out float d)) delay = d;
            else if (EntityRegistry.TryGetValue(jumperObj, out var ent) && ent.TryGetComponent<JumperConfig>(out var jcfg)) delay = jcfg.ReactivateDelay;

            Jumper[] jumpers = jumperObj.GetComponentsInChildren<Jumper>(true);
            foreach (var jc in jumpers)
            {
                if (jc == null) continue;

                jc.switchDuration = Mathf.Max(0.1f, delay);
                jc.enabled = active;
                if (jc.Fx != null) jc.Fx.SetActive(active);

                if (jc._interuptor != null)
                {
                    jc._interuptor.TimeBeforeSwitch = Mathf.Max(0.1f, delay);
                    jc._interuptor._warningTime = Mathf.Min(1.5f, delay * 0.3f);
                    jc._interuptor.IsAutoSwitch = true;
                }

                if (!IsEditModeActive)
                {
                    if (active)
                    {
                        try { jc.SwitchOn(); } catch { }
                    }
                    else
                    {
                        try { jc.SwitchOff(); } catch { }
                    }
                }
            }
        }

        public static void ForceRefreshAllJumpers()
        {
            if (PlacedJumpers == null || PlacedJumpers.Count == 0) return;

            for (int i = 0; i < PlacedJumpers.Count; i++)
            {
                GameObject obj = PlacedJumpers[i];
                if (obj == null || !obj.activeInHierarchy) continue;

                float force = ActiveJumperForce;
                float delay = ActiveJumperReactivateDelay;
                bool active = true;

                if (JumperForces.TryGetValue(obj, out float f)) force = f;
                if (JumperReactivateDelays.TryGetValue(obj, out float d)) delay = d;
                if (JumperActiveStates.TryGetValue(obj, out bool a)) active = a;

                if (EntityRegistry.TryGetValue(obj, out var data) && data.TryGetComponent<JumperConfig>(out var jc))
                {
                    force = jc.Force;
                    delay = jc.ReactivateDelay;
                    active = jc.IsActive;
                }

                ApplyJumperForce(obj, force);
                ApplyJumperReactivateDelay(obj, delay);
                ApplyJumperActive(obj, active);
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

            _detectedMaterialNeonProps.Clear();
            _detectedShaderNeonProps.Clear();

            EditorViewportCamera.DestroyCamera();
            PlacementHologramController.DestroyPreview();
            StudioGizmoController.DestroyGizmo();
            StudioGizmoController.ClearAllCachedCentroids();

            GlowAnimationService.ClearCache();

            SceneHarvestingService.CleanupProceduralResources();
            SkyboxControllerService.ResetToSceneDefault();
            MapAudioService.StopLevelAudio();
            MapAudioService.StopPreview();
            MapAudioService.PlacedAudioConfigs.Clear();

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
            ActiveJumperReactivateDelay = 4.0f;
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
                if (_lightRefreshTimer <= 0f)
                {
                    ForceRefreshAllTurbines();
                    ForceRefreshAllJumpers();
                    ForceRefreshAllLights();
                }
            }

            if (SelectedObject != null && PlacedObjectTypes.TryGetValue(SelectedObject, out var selType) && selType == PlacedObjectType.Sunlight)
            {
                Light activeSun = RenderSettings.sun;
                if (activeSun != null)
                {
                    activeSun.transform.rotation = SelectedObject.transform.rotation;
                }
            }

            if (GravityAreaService.PlacedGravityAreas.Count > 0)
            {
                GravityAreaService.UpdateAllGravityRotations();
            }

            GameObject player = FindPlayerEntity();
            CharacterController cc = GetPlayerController();

            UpdateObjectMotionPaths(player, cc);

            GlowAnimationService.UpdateTick(Time.deltaTime);

            if (!IsEditModeActive)
            {
                PlaytestSimulationEngine.RunPlaytestTick(player, cc, Time.deltaTime);
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
                            SelectObject(GetAimedPlacedObject(), isAdditive: isCtrl);
                    }
                }
            }

            if (IsEditModeActive)
            {
                if (SelectedObject != null && ProceduralCableService.IsCableHandle(SelectedObject, out GameObject draggedCable, out bool isCableEndB))
                {
                    ProceduralCableService.OnHandleDragged(draggedCable, isCableEndB, SelectedObject.transform.position);
                }

                if (SelectedObject != null && StructuralTrussService.IsTrussHandle(SelectedObject, out GameObject draggedTruss, out bool isTrussEndB))
                {
                    StructuralTrussService.OnHandleDragged(draggedTruss, isTrussEndB, SelectedObject.transform.position);
                }

                ProceduralCableService.UpdateAllCableHandles();
                StructuralTrussService.UpdateAllTrussHandles();
            }

            ProceduralCableService.UpdateEnergyFlowTick(Time.deltaTime);
        }

        // =========================================================================
        // SECTION 7: OBJECT SPAWNING & REGISTRATION
        // =========================================================================

        public static GameObject SpawnAssetByName(string partialName, Vector3 position, Vector3 scale, Quaternion? customRotation = null)
        {
            if (string.IsNullOrEmpty(partialName)) return null;

            string clean = partialName.Replace("_", " ").Trim().ToLower();
            string raw = partialName.Trim().ToLower();
            string normalized = PersistenceUtility.CleanAssetName(partialName).ToLower();

            CatalogAsset found = AllAssets.Find(a =>
                a.DisplayName.ToLower() == clean ||
                a.DisplayName.ToLower() == normalized ||
                (a.FilterMesh != null && (a.FilterMesh.name.ToLower() == raw || a.FilterMesh.name.ToLower() == normalized)) ||
                (a.SourceTemplate != null && (a.SourceTemplate.name.ToLower() == raw || a.SourceTemplate.name.ToLower() == normalized))
            );

            if (found == null)
            {
                found = AllAssets.Find(a =>
                    a.DisplayName.ToLower().Contains(clean) || clean.Contains(a.DisplayName.ToLower()) ||
                    a.DisplayName.ToLower().Contains(normalized) || normalized.Contains(a.DisplayName.ToLower()) ||
                    (a.FilterMesh != null && a.FilterMesh.name.ToLower().Contains(raw))
                );
            }

            if (found == null)
            {
                if (clean.Contains("spawn") || clean.Contains("entry")) found = AllAssets.Find(a => a.IsSpawnGate);
                else if (clean.Contains("goal") || clean.Contains("finish") || clean.Contains("end")) found = AllAssets.Find(a => a.IsGoalGate);
                else if (clean.Contains("sunlight") || clean.Contains("sun")) found = AllAssets.Find(a => a.IsSunlight);
                else if (clean.Contains("pointlight") || clean.Contains("omni") || clean.Contains("bulb"))
                    found = AllAssets.Find(a => a.DisplayName.ToLower().Contains("omni") || a.DisplayName.ToLower().Contains("point") || a.DisplayName.ToLower().Contains("bulb"));
                else if (clean.Contains("spotlight") || clean.Contains("light"))
                    found = AllAssets.Find(a => a.IsSpotlight);
                else if (clean.Contains("rotating") && clean.Contains("laser")) found = AllAssets.Find(a => a.IsRotatingLaser);
                else if (clean.Contains("laser")) found = AllAssets.Find(a => a.IsLaser);
                else if (clean.Contains("platform") || clean.Contains("floor") || clean.Contains("plateforme") || clean.Contains("16x16"))
                    found = AllAssets.Find(a => a.DisplayName.ToLower().Contains("platform") || a.DisplayName.ToLower().Contains("floor") || a.DisplayName.ToLower().Contains("16x16"));
                else if (clean.Contains("jumper")) found = AllAssets.Find(a => a.IsJumper);
                else if (clean.Contains("checkpoint")) found = AllAssets.Find(a => a.IsCheckPoint && !a.IsSpawnGate && !a.IsGoalGate);
                else if (clean.Contains("turret")) found = AllAssets.Find(a => a.IsTurret);
                else if (clean.Contains("skybox") || clean.Contains("sky")) found = AllAssets.Find(a => a.IsSkybox);
                else if (clean.Contains("helix")) found = AllAssets.Find(a => a.IsHelix);
                else if (clean.Contains("switch")) found = AllAssets.Find(a => a.IsSwitch);

                if (clean.Contains("cable") || clean.Contains("wire"))
                {
                    GameObject cable = ProceduralCableService.CreateProceduralCable(
                        position,
                        new Vector3(-3f, 0f, 0f),
                        new Vector3(3f, 0f, 0f)
                    );
                    if (customRotation.HasValue) cable.transform.rotation = customRotation.Value;
                    cable.transform.localScale = scale;
                    return cable;
                }
                if (clean.Contains("truss") || clean.Contains("girder"))
                {
                    GameObject truss = StructuralTrussService.CreateProceduralTruss(
                        position,
                        new Vector3(-4f, 0f, 0f),
                        new Vector3(4f, 0f, 0f)
                    );
                    if (customRotation.HasValue) truss.transform.rotation = customRotation.Value;
                    truss.transform.localScale = scale;
                    return truss;
                }
                if (clean.Contains("gravity"))
                {
                    return GravityAreaService.CreateProceduralGravityArea(
                        position,
                        scale,
                        9.81f
                    );
                }
            }

            if (found == null)
            {
                found = AllAssets.Find(a => !a.IsPrefabInstance && (a.Traits & AssetTrait.Architecture) != 0);
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
            if (asset == null) return null;

            if (asset.IsPrefabInstance && asset.PrefabTemplate != null)
            {
                Quaternion rot = customRotation ?? GetCurrentCombinedRotation(asset);
                return PrefabInstanceManager.SpawnInstance(asset.PrefabTemplate, position, rot, scale);
            }

            if (asset.SourceTemplate == null) return null;

            GameObject obj = GameObject.Instantiate(asset.SourceTemplate);
            obj.name = "Custom_" + asset.DisplayName.Replace(" ", "_");
            obj.transform.position = position;
            obj.transform.rotation = customRotation ?? GetCurrentCombinedRotation(asset);
            obj.transform.localScale = scale;
            obj.isStatic = false;

            LODGroup[] lods = obj.GetComponentsInChildren<LODGroup>(true);
            for (int l = 0; l < lods.Length; l++)
            {
                if (lods[l] != null) GameObject.DestroyImmediate(lods[l]);
            }

            bool isGravity = asset.IsGravityArea || asset.DisplayName.ToLower().Contains("gravity");
            if (!asset.IsHelix && !asset.IsTurret && !asset.IsJumper && !asset.IsCheckPoint && !asset.IsSpawnGate && !asset.IsGoalGate && !isGravity)
            {
                Rigidbody[] strayRbs = obj.GetComponentsInChildren<Rigidbody>(true);
                for (int r = 0; r < strayRbs.Length; r++)
                {
                    if (strayRbs[r] != null) GameObject.DestroyImmediate(strayRbs[r]);
                }

                Animation[] strayAnims = obj.GetComponentsInChildren<Animation>(true);
                for (int a = 0; a < strayAnims.Length; a++)
                {
                    if (strayAnims[a] != null) GameObject.DestroyImmediate(strayAnims[a]);
                }

                Animator[] strayAnimators = obj.GetComponentsInChildren<Animator>(true);
                for (int a = 0; a < strayAnimators.Length; a++)
                {
                    if (strayAnimators[a] != null) GameObject.DestroyImmediate(strayAnimators[a]);
                }
            }

            Renderer[] allRends = obj.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < allRends.Length; r++)
            {
                if (allRends[r] != null)
                {
                    allRends[r].enabled = true;
                    if (allRends[r].gameObject.name != "Editor_Snapping_Proxy" && allRends[r].gameObject.name != "Volume_Visual_Box")
                        allRends[r].gameObject.layer = 0;
                }
            }

            bool isHazard = asset.IsLaser || asset.IsRotatingLaser;
            bool isGate = asset.IsCheckPoint || asset.IsSpawnGate || asset.IsGoalGate;
            bool isLight = asset.IsSpotlight || asset.IsSunlight || asset.IsSkybox;
            bool isHelix = asset.IsHelix;
            bool isTurret = asset.IsTurret;
            bool isJumper = asset.IsJumper;
            bool isSwitch = asset.IsSwitch;

            Collider[] existingCols = obj.GetComponentsInChildren<Collider>(true);
            bool hasSolidCollider = false;

            for (int i = 0; i < existingCols.Length; i++)
            {
                Collider c = existingCols[i];
                if (c == null || c.gameObject.name == "Editor_Snapping_Proxy") continue;

                c.enabled = true;

                if (isHazard || isLight || isGravity)
                {
                    c.isTrigger = true;
                }
                else if (isGate)
                {
                    if (!c.isTrigger) hasSolidCollider = true;
                }
                else if (isHelix)
                {
                    HelixPushingZone zone = c.GetComponent<HelixPushingZone>() ?? c.GetComponentInParent<HelixPushingZone>();
                    string cName = c.gameObject.name.ToLower();

                    if (zone != null || cName.Contains("zone") || cName.Contains("push") ||
                        cName.Contains("vent") || cName.Contains("wind") || cName.Contains("kill") ||
                        cName.Contains("death") || cName.Contains("blade") || cName.Contains("hazard"))
                    {
                        c.isTrigger = true;
                    }
                    else if (!c.isTrigger)
                    {
                        hasSolidCollider = true;
                    }
                }
                else if (isJumper || isTurret || isSwitch)
                {
                    if (!c.isTrigger) hasSolidCollider = true;
                }
                else
                {
                    c.isTrigger = false;
                    hasSolidCollider = true;
                }
            }

            if (!hasSolidCollider && !isHazard && !isGate && !isLight && !isHelix && !isTurret && !isJumper && !isGravity)
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

            if (asset.IsCheckPoint || asset.IsSpawnGate || asset.IsGoalGate)
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

                    Collider col = cp.GetComponent<Collider>() ?? obj.GetComponentInChildren<Collider>();
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
                        Kind = LightKind.Directional,
                        IsDirectional = true,
                        Color = new Color(1f, 0.85f, 0.6f),
                        Intensity = 3.0f,
                        VolumetricIntensity = 1.0f
                    };
                    ApplyLightConfig(obj, PlacedLights[obj]);
                }
                StudioGizmoController.AttachSunVisualWidget(obj);
            }
            else if (asset.IsSpotlight || asset.DisplayName.ToLower().Contains("omni") || asset.DisplayName.ToLower().Contains("point") || asset.DisplayName.ToLower().Contains("bulb"))
            {
                Light l = obj.GetComponentInChildren<Light>();
                if (l != null)
                {
                    l.enabled = true;
                    if (!PlacedLights.ContainsKey(obj))
                    {
                        string dName = asset.DisplayName.ToLower();
                        bool isPoint = (l.type == LightType.Point) || dName.Contains("omni") || dName.Contains("point") || dName.Contains("bulb");

                        PlacedLights[obj] = new LightConfig
                        {
                            Kind = isPoint ? LightKind.Point : LightKind.Spot,
                            Range = (l.range > 0f) ? l.range : (isPoint ? 25f : 150f),
                            SpotAngle = (l.spotAngle > 0f) ? l.spotAngle : 60f,
                            Color = (l.color != default) ? l.color : Color.cyan,
                            Intensity = isPoint ? 6.0f : 8.0f,
                            VolumetricIntensity = isPoint ? 1.5f : 4.0f
                        };
                    }
                    ApplyLightConfig(obj, PlacedLights[obj]);
                }
            }
            else if (asset.IsSkybox)
            {
                obj.name = "Custom_Skybox_Controller";
                SkyboxControllerService.Initialize(obj, SkyboxControllerService.ActiveConfig);

                obj.layer = 0;
                BoxCollider bc = obj.GetComponent<BoxCollider>();
                if (bc == null) bc = obj.AddComponent<BoxCollider>();
                bc.size = new Vector3(2.5f, 2.5f, 2.5f);
                bc.isTrigger = true;
            }
            else if (isGravity)
            {
                BoxCollider bc = obj.GetComponent<BoxCollider>();
                if (bc == null) bc = obj.AddComponent<BoxCollider>();
                bc.isTrigger = true;
                bc.enabled = true;

                GravityArea ga = obj.GetComponentInChildren<GravityArea>(true);
                if (ga != null)
                {
                    ga.enabled = true;
                    ga.affectOthers = true;
                    ga._changeGravity = true;
                }

                if (!GravityAreaService.PlacedGravityConfigs.ContainsKey(obj))
                {
                    float force = 9.81f;
                    Vector3 localAxis = Vector3.up;

                    if (ga != null && ga.gravity.sqrMagnitude > 0.01f)
                    {
                        force = ga.gravity.magnitude;
                        Vector3 unrotated = Quaternion.Inverse(obj.transform.rotation) * ga.gravity.normalized;
                        if (unrotated.sqrMagnitude > 0.01f) localAxis = unrotated;
                    }

                    GravityAreaService.PlacedGravityConfigs[obj] = new GravityConfig
                    {
                        GravityForce = force,
                        LocalAxis = localAxis,
                        AffectOthers = true,
                        ChangeGravity = true,
                        IsActive = true
                    };
                }
                GravityAreaService.ApplyGravityConfig(obj, GravityAreaService.PlacedGravityConfigs[obj]);
            }

            if (asset.IsHelix) ApplyTurbineSpeed(obj, ActiveTurbineSpeed);
            if (asset.IsTurret) ApplyTurretSettings(obj, ActiveTurretFireDelay, 1500f);
            if (asset.IsJumper)
            {
                ApplyJumperForce(obj, ActiveJumperForce);
                ApplyJumperReactivateDelay(obj, ActiveJumperReactivateDelay);
                ApplyJumperActive(obj, true);
            }
            if (asset.IsSwitch) SwitchService.ApplySwitchConfig(obj, new SwitchConfig());

            obj.SetActive(true);
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
            if (old != null) GameObject.Destroy(old.gameObject);

            GameObject proxyObj = new GameObject("Editor_Snapping_Proxy");
            proxyObj.transform.SetParent(obj.transform, false);
            proxyObj.transform.localPosition = Vector3.zero;
            proxyObj.transform.localRotation = Quaternion.identity;
            proxyObj.transform.localScale = Vector3.one;
            proxyObj.layer = 2;

            Bounds proxyB = PlacementHologramController.CalculateOptimizedProxyBounds(obj);
            BoxCollider bc = proxyObj.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.center = proxyB.center;
            bc.size = new Vector3(
                Mathf.Max(0.2f, Mathf.Abs(proxyB.size.x)),
                Mathf.Max(0.2f, Mathf.Abs(proxyB.size.y)),
                Mathf.Max(0.2f, Mathf.Abs(proxyB.size.z))
            );
        }

        public static void RegisterPlacedObject(GameObject obj)
        {
            if (obj == null || PlacedObjects.Contains(obj)) return;

            PlacedObjects.Add(obj);

            PlaytestSnapshots[obj] = new PlaytestTransformSnapshot
            {
                Position = obj.transform.position,
                Rotation = obj.transform.rotation,
                LocalScale = obj.transform.localScale
            };

            string low = obj.name.ToLower();
            PlacedObjectType identifiedType = PlacedObjectType.Generic;

            CheckPointScript cp = obj.GetComponentInChildren<CheckPointScript>();
            if (cp != null && !PlacedCheckpoints.Contains(cp))
            {
                PlacedCheckpoints.Add(cp);
            }

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
                if (h != null) CachedHelixScripts[obj] = h;
            }
            else if (low.Contains("turret") || obj.GetComponentInChildren<TurretScript>() != null)
            {
                identifiedType = PlacedObjectType.Turret;
            }
            else if (low.Contains("spawn") || low.Contains("entry"))
            {
                identifiedType = PlacedObjectType.SpawnGate;
            }
            else if (low.Contains("goal") || low.Contains("finish"))
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
            else if (low.Contains("skybox"))
            {
                identifiedType = PlacedObjectType.SkyboxController;
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
            else if (low.Contains("switch") || low.Contains("interupt") || obj.GetComponentInChildren<Interuptor>() != null)
            {
                identifiedType = PlacedObjectType.Switch;
                if (!SwitchService.PlacedSwitches.ContainsKey(obj))
                    SwitchService.PlacedSwitches[obj] = new SwitchConfig();
            }
            else if (cp != null || low.Contains("checkpoint"))
            {
                identifiedType = PlacedObjectType.Checkpoint;
            }
            else if (low.Contains("gravity") || obj.GetComponentInChildren<GravityArea>() != null || obj.GetComponentInChildren<GravityReceiver>() != null)
            {
                identifiedType = PlacedObjectType.GravityArea;
                if (!GravityAreaService.PlacedGravityAreas.Contains(obj))
                    GravityAreaService.PlacedGravityAreas.Add(obj);
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
            EntityRegistry.Remove(target);
            PlaytestSnapshots.Remove(target);

            if (target.transform.parent != null)
                RecalculateParentChildCount(target.transform.parent.gameObject);

            PlacedLights.Remove(target);
            MotionPaths.Remove(target);
            DestroyWaypointVisuals(target);
            PlacedNeonConfigs.Remove(target);

            ProceduralCableService.PlacedCables.Remove(target);
            ProceduralCableService.DestroyCableHandles(target);
            StructuralTrussService.PlacedTrusses.Remove(target);
            StructuralTrussService.DestroyTrussHandles(target);

            GravityAreaService.PlacedGravityAreas.Remove(target);
            GravityAreaService.PlacedGravityConfigs.Remove(target);
            StudioUIManager.LockedObjects.Remove(target);

            if (PathEditTarget == target) PathEditTarget = null;
            if (ParentingChildTarget == target) ParentingChildTarget = null;
            if (RepositionTarget == target) RepositionTarget = null;
            if (SelectedObject == target) SelectedObject = SelectedObjects.Count > 0 ? SelectedObjects[SelectedObjects.Count - 1] : null;

            PlacedJumpers.Remove(target);
            JumperForces.Remove(target);
            JumperReactivateDelays.Remove(target);
            JumperActiveStates.Remove(target);

            PlacedTurbines.Remove(target);
            CachedHelixScripts.Remove(target);
            PlacedRotatingLasers.Remove(target);

            int laserIdx = PlacedLaserBarriers.IndexOf(target);
            if (laserIdx >= 0)
            {
                PlacedLaserBarriers.RemoveAt(laserIdx);
                if (laserIdx < PlacedLaserColliders.Count) PlacedLaserColliders.RemoveAt(laserIdx);
            }

            SwitchService.PlacedSwitches.Remove(target);

            if (PlacedGoalGate == target) PlacedGoalGate = null;

            CheckPointScript cp = target.GetComponentInChildren<CheckPointScript>();
            if (cp != null) PlacedCheckpoints.Remove(cp);

            RecalculateVoidDeathY();
            StudioUIManager.RefreshHierarchy();
        }

        public static void DeleteSpecifiedObject(GameObject target)
        {
            if (target == null) return;

            if (ProceduralCableService.IsCableHandle(target, out GameObject resolvedCable, out _))
            {
                target = resolvedCable;
            }
            else if (StructuralTrussService.IsTrussHandle(target, out GameObject resolvedTruss, out _))
            {
                target = resolvedTruss;
            }

            if (target != null)
            {
                EditorEntityData dataSnapshot = ExtractEntityData(target);
                RemovePlacedObjectFromTracking(target);
                target.SetActive(false);

                UndoHistory.Push(new HistoryRecord
                {
                    ActionType = HistoryActionType.Deletion,
                    TargetObject = target,
                    AssetName = target.name.StartsWith("Custom_") ? target.name.Substring(7) : target.name,
                    Position = target.transform.position,
                    Rotation = target.transform.rotation,
                    ScaleVector = target.transform.localScale,
                    EntityDataSnapshot = dataSnapshot
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
            EntityRegistry.Clear();
            RepositionTarget = null;
            LastPlacedObject = null;
            SelectedObjects.Clear();
            SelectedObject = null;

            _detectedMaterialNeonProps.Clear();
            _detectedShaderNeonProps.Clear();

            PlacedJumpers.Clear();
            JumperForces.Clear();
            JumperReactivateDelays.Clear();
            JumperActiveStates.Clear();

            PlacedTurbines.Clear();
            CachedHelixScripts.Clear();
            PlacedLaserBarriers.Clear();
            PlacedLaserColliders.Clear();
            PlacedRotatingLasers.Clear();
            PlacedCheckpoints.Clear();
            PlacedGoalGate = null;
            PlacedNeonConfigs.Clear();

            ProceduralCableService.DestroyAllCableHandles();
            ProceduralCableService.PlacedCables.Clear();
            StructuralTrussService.DestroyAllTrussHandles();
            StructuralTrussService.PlacedTrusses.Clear();

            GravityAreaService.PlacedGravityAreas.Clear();
            GravityAreaService.PlacedGravityConfigs.Clear();
            StudioUIManager.LockedObjects.Clear();

            ActiveCustomCheckpoint = null;

            TurbineSpeeds.Clear();
            TurretFireDelays.Clear();

            MotionPaths.Clear();
            DestroyAllWaypointVisuals();
            StudioGizmoController.ClearAllCachedCentroids();
            SwitchService.PlacedSwitches.Clear();

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
        // SECTION 8: UNDO / REDO SYSTEM
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
                    if (record.EntityDataSnapshot != null)
                        ApplyEntityData(record.TargetObject, record.EntityDataSnapshot);
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
                    if (record.EntityDataSnapshot != null)
                        ApplyEntityData(record.TargetObject, record.EntityDataSnapshot);
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
        // SECTION 9: ENTITY APPLIERS & SETTINGS
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

            if (EntityRegistry.TryGetValue(turbineObj, out var data))
            {
                var tc = data.GetOrCreate<TurbineConfig>();
                tc.Speed = speed;
            }

            HelixPushingZone[] zones = turbineObj.GetComponentsInChildren<HelixPushingZone>(true);
            for (int z = 0; z < zones.Length; z++)
            {
                HelixPushingZone zone = zones[z];
                if (zone != null)
                {
                    zone._maxForce = speed;
                    zone._maxVelocity = speed * 2.0f;
                }
            }

            Helix[] helices = turbineObj.GetComponentsInChildren<Helix>(true);
            for (int hIdx = 0; hIdx < helices.Length; hIdx++)
            {
                Helix h = helices[hIdx];
                if (h == null) continue;

                h._maximumVelocity = speed * 20f;
                CachedHelixScripts[turbineObj] = h;

                if (h._hingeJoint == null)
                {
                    h._hingeJoint = h.GetComponent<HingeJoint>()
                                 ?? h.GetComponentInChildren<HingeJoint>(true)
                                 ?? turbineObj.GetComponentInChildren<HingeJoint>(true);
                }

                if (h._hingeJoint != null)
                {
                    h._hingeJoint.useMotor = true;
                    JointMotor m = h._hingeJoint.motor;
                    m.targetVelocity = speed * 20f;
                    m.force = 3000f;
                    m.freeSpin = false;
                    h._hingeJoint.motor = m;
                }
            }

            HingeJoint[] allJoints = turbineObj.GetComponentsInChildren<HingeJoint>(true);
            for (int j = 0; j < allJoints.Length; j++)
            {
                HingeJoint hj = allJoints[j];
                if (hj != null)
                {
                    hj.useMotor = true;
                    JointMotor m = hj.motor;
                    m.targetVelocity = speed * 20f;
                    m.force = 3000f;
                    m.freeSpin = false;
                    hj.motor = m;
                }
            }
        }

        public static void ForceRefreshAllTurbines()
        {
            if (PlacedTurbines == null || PlacedTurbines.Count == 0) return;

            for (int i = 0; i < PlacedTurbines.Count; i++)
            {
                GameObject obj = PlacedTurbines[i];
                if (obj == null || !obj.activeInHierarchy) continue;

                float speed = ActiveTurbineSpeed;
                if (TurbineSpeeds.TryGetValue(obj, out float s))
                {
                    speed = s;
                }
                else if (EntityRegistry.TryGetValue(obj, out var data) && data.TryGetComponent<TurbineConfig>(out var tc))
                {
                    speed = tc.Speed;
                }

                ApplyTurbineSpeed(obj, speed);
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

        public static void ApplyLightConfig(GameObject lightObj, LightConfig cfg)
        {
            if (lightObj == null || cfg == null) return;
            PlacedLights[lightObj] = cfg;

            Light l = lightObj.GetComponentInChildren<Light>();

            if (cfg.Kind == LightKind.Directional || cfg.IsDirectional)
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

                if (l != null && targetSun != l) l.enabled = false;
                StudioGizmoController.AttachSunVisualWidget(lightObj);
            }
            else if (cfg.Kind == LightKind.Point)
            {
                if (l != null)
                {
                    l.enabled = true;
                    l.type = LightType.Point;
                    l.renderMode = LightRenderMode.Auto;
                    l.range = Mathf.Max(1f, cfg.Range);
                    l.color = cfg.Color;

                    float lumens = Mathf.Pow(Mathf.Max(0.1f, cfg.Intensity), 2.0f) * 3500f;
                    l.intensity = lumens;
                    l.shadows = LightShadows.None;

                    ApplyHDRPVolumetricSettings(l.gameObject, cfg.VolumetricIntensity, lumens, cfg.Color);
                }
            }
            else
            {
                if (l != null)
                {
                    l.enabled = true;
                    l.type = LightType.Spot;
                    l.renderMode = LightRenderMode.Auto;
                    l.range = Mathf.Max(1f, cfg.Range > 0 ? cfg.Range : 150f);
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
                Material m = rends[i].sharedMaterial;
                if (m == null) continue;

                float volVal = Mathf.Clamp(cfg.VolumetricIntensity, 0f, 16f);
                if (m.HasProperty("_VolumetricIntensity")) m.SetFloat("_VolumetricIntensity", volVal);
                if (m.HasProperty("_VolumetricDimmer")) m.SetFloat("_VolumetricDimmer", volVal);
                if (m.HasProperty("_Volumetric")) m.SetFloat("_Volumetric", volVal);
                if (m.HasProperty("_Color")) m.SetColor("_Color", cfg.Color);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", cfg.Color);
                if (m.HasProperty("_EmissionColor"))
                {
                    m.SetColor("_EmissionColor", cfg.Color * (cfg.Intensity * 0.75f));
                    m.EnableKeyword("_EMISSION");
                }
            }
        }

        private static readonly HashSet<string> _loggedShaderDumps = new HashSet<string>();
        private static readonly Dictionary<int, HashSet<string>> _detectedMaterialNeonProps = new Dictionary<int, HashSet<string>>();
        private static readonly Dictionary<string, HashSet<string>> _detectedShaderNeonProps = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] DeadCoreNeonProperties = new string[]
        {
            "_LinesColor", "_LineColor", "_ColorLines", "_ColorLine",
            "_Color2", "_SecondColor", "_SecondaryColor",
            "_EnergyColor", "_CircuitColor", "_GlowColor", "_NeonColor",
            "_PulseColor", "_StripeColor", "_StripesColor", "_PatternColor",
            "_EmissionColor", "_EmissiveColor", "_EmissiveColorLDR",
            "_TintColor", "_Glow_Color", "_Line_Color", "_Lines_Color"
        };

        public static void ApplyNeonConfig(GameObject obj, NeonConfig cfg)
        {
            if (obj == null || cfg == null) return;
            PlacedNeonConfigs[obj] = cfg.Clone();

            if (!cfg.IsActive) return;

            Renderer[] rends = obj.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0) return;

            Color finalEmissive = cfg.Color * cfg.Intensity;

            for (int r = 0; r < rends.Length; r++)
            {
                Renderer rend = rends[r];
                if (rend == null || rend.gameObject.name == "Editor_Snapping_Proxy") continue;

                Material[] mats = rend.materials;
                if (mats == null || mats.Length == 0) continue;

                MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                rend.GetPropertyBlock(mpb);

                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null || mat.shader == null) continue;

                    Shader shader = mat.shader;
                    string sName = shader.name;

                    int matId = mat.GetInstanceID();
                    if (!_detectedMaterialNeonProps.TryGetValue(matId, out var matProps))
                    {
                        matProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        _detectedMaterialNeonProps[matId] = matProps;
                    }

                    if (!_detectedShaderNeonProps.TryGetValue(sName, out var shaderProps))
                    {
                        shaderProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        _detectedShaderNeonProps[sName] = shaderProps;
                    }

                    try
                    {
                        int pCount = shader.GetPropertyCount();
                        for (int i = 0; i < pCount; i++)
                        {
                            string pName = shader.GetPropertyName(i);
                            var pType = shader.GetPropertyType(i);
                            string pLow = pName.ToLowerInvariant();

                            if (pType == UnityEngine.Rendering.ShaderPropertyType.Color)
                            {
                                bool isNeonProp = matProps.Contains(pName) ||
                                                  shaderProps.Contains(pName) ||
                                                  pLow.Contains("line") || pLow.Contains("emiss") ||
                                                  pLow.Contains("glow") || pLow.Contains("circuit") ||
                                                  pLow.Contains("energy") || pLow.Contains("pulse") ||
                                                  pLow.Contains("neon") || pLow.Contains("color2") ||
                                                  pLow.Contains("second") || pLow.Contains("stripe");

                                if (!isNeonProp && mat.HasProperty(pName))
                                {
                                    Color cur = mat.GetColor(pName);
                                    if (cur.b > 0.40f && cur.g > 0.30f && cur.b > cur.r * 1.15f)
                                    {
                                        isNeonProp = true;
                                    }
                                }

                                if (isNeonProp)
                                {
                                    matProps.Add(pName);
                                    if (pName != "_Color" && pName != "_BaseColor" && pName != "_MainColor")
                                    {
                                        shaderProps.Add(pName);
                                    }

                                    mat.SetColor(pName, finalEmissive);
                                    mpb.SetColor(pName, finalEmissive);
                                }
                            }
                            else if (pType == UnityEngine.Rendering.ShaderPropertyType.Float || pType == UnityEngine.Rendering.ShaderPropertyType.Range)
                            {
                                if (matProps.Contains(pName) || shaderProps.Contains(pName) ||
                                    pLow.Contains("emissiveintensity") || pLow.Contains("emissionintensity") ||
                                    pLow.Contains("linesintensity") || pLow.Contains("lineintensity") ||
                                    pLow.Contains("glowintensity") || pLow.Contains("emissivemultiplier"))
                                {
                                    matProps.Add(pName);
                                    shaderProps.Add(pName);
                                    mat.SetFloat(pName, cfg.Intensity);
                                    mpb.SetFloat(pName, cfg.Intensity);
                                }
                            }
                        }
                    }
                    catch { }

                    for (int k = 0; k < DeadCoreNeonProperties.Length; k++)
                    {
                        string prop = DeadCoreNeonProperties[k];
                        if (mat.HasProperty(prop))
                        {
                            mat.SetColor(prop, finalEmissive);
                            mpb.SetColor(prop, finalEmissive);
                        }
                    }

                    string sLow = sName.ToLowerInvariant();
                    if (sLow.Contains("unlit") || sLow.Contains("additive") || sLow.Contains("laser") || sLow.Contains("beam"))
                    {
                        if (mat.HasProperty("_TintColor")) { mat.SetColor("_TintColor", cfg.Color); mpb.SetColor("_TintColor", cfg.Color); }
                        if (mat.HasProperty("_Color")) { mat.SetColor("_Color", cfg.Color); mpb.SetColor("_Color", cfg.Color); }
                    }

                    mat.EnableKeyword("_EMISSION");
                    mat.EnableKeyword("_EMISSIVE_COLOR_MAP");
                    mat.EnableKeyword("_EMISSIVE_ENABLE");
                    mat.EnableKeyword("_EMISSIVE_ANIMATED");

                    if (mat.HasProperty("_UseEmissiveIntensity")) mat.SetFloat("_UseEmissiveIntensity", 1.0f);
                    if (mat.HasProperty("_EmissiveIntensity")) mat.SetFloat("_EmissiveIntensity", cfg.Intensity);
                }

                rend.materials = mats;
                rend.SetPropertyBlock(mpb);
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
                if (field != null) field.SetValue(target, val);
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
                    if (cfg.Kind == LightKind.Spot)
                        l.spotAngle = cfg.SpotAngle + 0.05f;
                    else if (cfg.Kind == LightKind.Point)
                        l.range = cfg.Range + 0.05f;

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

            TrailRenderer[] trails = gateObj.GetComponentsInChildren<TrailRenderer>(true);
            for (int i = 0; i < trails.Length; i++)
            {
                if (trails[i] != null)
                {
                    trails[i].startColor = tintColor;
                    trails[i].endColor = tintColor;
                }
            }
        }

        public static void StripParticlesAndLights(GameObject root)
        {
            if (root == null) return;

            Jumper jc = root.GetComponentInChildren<Jumper>();
            if (jc != null && jc.Fx != null) GameObject.DestroyImmediate(jc.Fx);

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

                Transform bulb = obj.transform.Find("Light_Bulb");
                if (bulb != null) bulb.gameObject.SetActive(visible);

                Transform sunWidget = obj.transform.Find("Sun_Editor_Widget");
                if (sunWidget != null) sunWidget.gameObject.SetActive(visible);

                Transform skyWidget = obj.transform.Find("Skybox_Editor_Widget");
                if (skyWidget != null) skyWidget.gameObject.SetActive(visible);

                Transform audioWidget = obj.transform.Find("Audio_Editor_Widget");
                if (audioWidget != null) audioWidget.gameObject.SetActive(visible);

                // Completely hide visual renderers & colliders for Audio Controllers during playtest
                if (PlacedObjectTypes.TryGetValue(obj, out var pType) && pType == PlacedObjectType.AudioController)
                {
                    Renderer[] rends = obj.GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < rends.Length; r++)
                    {
                        if (rends[r] != null) rends[r].enabled = visible;
                    }

                    Collider[] cols = obj.GetComponentsInChildren<Collider>(true);
                    for (int c = 0; c < cols.Length; c++)
                    {
                        if (cols[c] != null && cols[c].gameObject.name != "Editor_Snapping_Proxy")
                            cols[c].enabled = visible;
                    }
                }
            }
        }

        // =========================================================================
        // SECTION 10: WAYPOINTS & MOTION PATHS
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

        public static void DestroyWaypointVisuals(GameObject obj)
        {
            if (obj == null) return;

            if (WaypointMarkersA.TryGetValue(obj, out GameObject a) && a != null) GameObject.DestroyImmediate(a);
            WaypointMarkersA.Remove(obj);

            if (WaypointMarkersB.TryGetValue(obj, out GameObject b) && b != null) GameObject.DestroyImmediate(b);
            WaypointMarkersB.Remove(obj);

            if (WaypointLines.TryGetValue(obj, out LineRenderer l) && l != null) GameObject.DestroyImmediate(l.gameObject);
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

        public static void HideAllWaypointMarkers()
        {
            foreach (var kvp in WaypointMarkersA) if (kvp.Value != null) kvp.Value.SetActive(false);
            foreach (var kvp in WaypointMarkersB) if (kvp.Value != null) kvp.Value.SetActive(false);
            foreach (var kvp in WaypointLines) if (kvp.Value != null) kvp.Value.gameObject.SetActive(false);
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
                Material ma = GizmoMaterialCache.CreateSolidMaterial(new Color(0.2f, 1f, 0.4f, 1f));
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
                Material mb = GizmoMaterialCache.CreateSolidMaterial(new Color(1f, 0.65f, 0.1f, 1f));
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
                Material ml = GizmoMaterialCache.CreateSolidMaterial(new Color(0.3f, 0.85f, 1f, 0.8f));
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
                        if (isB) path.PointB = SelectedObject.transform.position;
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
                        if (path.PointA != Vector3.zero || obj.transform.position == Vector3.zero)
                            obj.transform.position = path.PointA;
                    }

                    UpdateWaypointVisuals(obj, path);
                    continue;
                }

                HideWaypointVisual(obj);

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

                if (Mathf.Abs(path.RotationSpeed) > 0.01f)
                {
                    Vector3 localAxis = path.GetEffectiveLocalAxis(obj.transform);
                    float rotAngle = path.RotationSpeed * dt;
                    Vector3 worldAxis = obj.transform.TransformDirection(localAxis);
                    Quaternion rotDelta = Quaternion.AngleAxis(rotAngle, worldAxis);

                    if (playerOnPlatform)
                    {
                        Vector3 offset = player.transform.position - obj.transform.position;
                        Vector3 newOffset = rotDelta * offset;
                        Vector3 rotPush = newOffset - offset;
                        cc.Move(rotPush);

                        if (Mathf.Abs(worldAxis.y) > 0.25f)
                        {
                            float yawAngle = rotAngle * worldAxis.y;
                            player.transform.rotation = Quaternion.AngleAxis(yawAngle, Vector3.up) * player.transform.rotation;
                        }
                    }

                    obj.transform.Rotate(localAxis, rotAngle, Space.Self);
                }

                float dist = path.TotalDistance;
                if (dist < 0.05f || path.Speed < 0.01f) continue;

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

        // =========================================================================
        // SECTION 11: PLAYTEST SIMULATION ENGINE
        // =========================================================================

        public static class PlaytestSimulationEngine
        {
            public static void RunPlaytestTick(GameObject player, CharacterController cc, float dt)
            {
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
                    SkyboxControllerService.UpdateTick(dt);

                    Vector3 pPos = player.transform.position;
                    for (int i = 0; i < PlacedCheckpoints.Count; i++)
                    {
                        CheckPointScript cp = PlacedCheckpoints[i];
                        if (cp != null && cp.gameObject.activeSelf)
                        {
                            Vector3 checkPos = (cp._spawnPoint != null) ? cp._spawnPoint.position : cp.transform.position;
                            if ((pPos - checkPos).sqrMagnitude < 16.0f || (pPos - cp.transform.position).sqrMagnitude < 16.0f)
                            {
                                if (ActiveCustomCheckpoint != cp)
                                {
                                    ActiveCustomCheckpoint = cp;
                                    MelonLogger.Msg($">> [Checkpoint] Tagged checkpoint at {cp.transform.position}!");
                                }
                            }
                        }
                    }
                }

                if (!IsLevelCompleted)
                {
                    LevelTimer += dt;
                }
                else
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }

            private static void CheckGoalTriggerArrival(GameObject player)
            {
                if (IsLevelCompleted || PlacedGoalGate == null || !PlacedGoalGate.activeSelf) return;

                Vector3 pPos = player.transform.position;
                Vector3 gPos = PlacedGoalGate.transform.position;

                float horizDistSq = (pPos.x - gPos.x) * (pPos.x - gPos.x) + (pPos.z - gPos.z) * (pPos.z - gPos.z);
                float vertDist = Mathf.Abs(pPos.y - gPos.y);

                if (horizDistSq < 16.0f && vertDist < 3.5f)
                {
                    IsLevelCompleted = true;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    MelonLogger.Msg($">> [VICTORY] Level completed in {LevelTimer:F2} seconds!");
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

                    if (!CachedHelixScripts.TryGetValue(obj, out Helix helixScript) || helixScript == null)
                    {
                        helixScript = obj.GetComponentInChildren<Helix>(true);
                        CachedHelixScripts[obj] = helixScript;
                    }

                    if (helixScript != null && !helixScript.enabled)
                        continue;

                    HelixPushingZone zone = obj.GetComponentInChildren<HelixPushingZone>(true);
                    if (zone != null && !zone.enabled)
                        continue;

                    Interuptor finSwitch = obj.GetComponentInChildren<Interuptor>(true);
                    if (finSwitch != null && finSwitch._isOn)
                        continue;

                    if (helixScript != null && helixScript._hingeJoint != null)
                    {
                        if (!helixScript._hingeJoint.useMotor)
                            continue;
                    }

                    float speed = TurbineSpeeds.ContainsKey(obj) ? TurbineSpeeds[obj] : ActiveTurbineSpeed;

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
                                if (allCols[c].gameObject.name != "Editor_Snapping_Proxy") { bc = allCols[c]; break; }
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

            private static void CheckVoidFall(GameObject player)
            {
                if (player.transform.position.y < CachedVoidDeathY)
                    RespawnPlayer(player);
            }
        }

        // =========================================================================
        // SECTION 12: PLAYER ENTITY & RESPAWNING
        // =========================================================================

        public static void ClearLastCheckpoint()
        {
            ActiveCustomCheckpoint = null;
        }

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

            SwitchService.ResetAllSwitchesForPlaytest(enteringPlaytest: true);

            UnfreezePlayerControls();
            ForceRefreshAllTurbines();
            ForceRefreshAllJumpers();

            MelonLogger.Msg(">> [Restart] Restarted run cleanly from Entry Gate!");
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

                SwitchService.ResetAllSwitchesForPlaytest(enteringPlaytest: true);

                MelonLogger.Msg(">> [Respawn] Returned to active Checkpoint!");
            }
            else
            {
                RestartRun();
                return;
            }

            if (cc != null) cc.enabled = true;
            ForceRefreshAllTurbines();
            ForceRefreshAllJumpers();
            UnfreezePlayerControls();
        }

        private static readonly List<MonoBehaviour> _disabledPlayerScripts = new List<MonoBehaviour>();
        public static void SetPlayerControlsActive(bool active)
        {
            GameObject player = FindPlayerEntity();
            if (player == null) return;

            CharacterController cc = GetPlayerController();
            if (cc != null) cc.enabled = active;

            Rigidbody rb = player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = !active;
                if (!active)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }

            if (!active)
            {
                _disabledPlayerScripts.Clear();

                MonoBehaviour[] scripts = player.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < scripts.Length; i++)
                {
                    MonoBehaviour mb = scripts[i];
                    if (mb == null) continue;

                    string name = mb.GetIl2CppType().Name.ToLowerInvariant();
                    string ns = mb.GetIl2CppType().Namespace ?? "";
                    if (ns.StartsWith("DeadCoreEditor")) continue;

                    if (name.Contains("manager") || name.Contains("scene") || name.Contains("level") ||
                        name.Contains("game") || name.Contains("core") || name.Contains("audio") ||
                        name.Contains("sound") || name.Contains("music") || name.Contains("life") ||
                        name.Contains("health") || name.Contains("spawn") || name.Contains("checkpoint") ||
                        name.Contains("trigger") || name.Contains("loader") || name.Contains("stream"))
                    {
                        continue;
                    }

                    if (name.Contains("look") || name.Contains("input") || name.Contains("motor") ||
                        name.Contains("gun") || name.Contains("shoot") || name.Contains("arm") ||
                        name.Contains("weapon") || name.Contains("movement") || name.Contains("controller"))
                    {
                        if (mb.enabled)
                        {
                            mb.enabled = false;
                            _disabledPlayerScripts.Add(mb);
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < _disabledPlayerScripts.Count; i++)
                {
                    if (_disabledPlayerScripts[i] != null) _disabledPlayerScripts[i].enabled = true;
                }
                _disabledPlayerScripts.Clear();
            }
        }
        public static void UnfreezePlayerControls() => SetPlayerControlsActive(true);

        private static void FreezePlayerEntity(GameObject player, CharacterController cc)
        {
            if (player != null)
            {
                player.transform.position = FrozenPlayerPosition;
                player.transform.rotation = FrozenPlayerRotation;

                if (cc != null && cc.enabled) cc.enabled = false;

                Rigidbody rb = player.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }

        public static void InitializeCustomLevel()
        {
            if (IsLevelInitialized) return;

            string curScene = SceneManager.GetActiveScene().name.ToLower();
            if (curScene.Contains("load") || curScene.Contains("menu") || curScene.Contains("boot"))
                return;

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
            PrefabInstanceManager.LoadAllPrefabsFromDisk();

            if (!string.IsNullOrEmpty(MapBrowserService.SelectedMapPath) && File.Exists(MapBrowserService.SelectedMapPath))
            {
                LevelPersistenceService.LoadLevelByFullPath(MapBrowserService.SelectedMapPath);
            }

            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                GameObject obj = PlacedObjects[i];
                if (obj != null && PlacedObjectTypes.TryGetValue(obj, out var t) && t == PlacedObjectType.SpawnGate)
                {
                    CheckPointScript cp = obj.GetComponentInChildren<CheckPointScript>();
                    LevelSpawnPosition = (cp != null && cp._spawnPoint != null) ? cp._spawnPoint.position : (obj.transform.position + Vector3.up * 0.2f);
                    startPos = LevelSpawnPosition;
                    break;
                }
            }

            if (PlacedObjects.Count == 0)
            {
                Vector3 p0Pos = startPos - new Vector3(0f, 1.2f, 0f);
                GameObject startPlatform = SpawnAssetByName("Floor_Platform_16x16", p0Pos, 0.55f)
                                        ?? SpawnAssetByName("Platform", p0Pos, 0.8f);

                if (startPlatform != null) RegisterPlacedObject(startPlatform);
            }

            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                if (PlacedObjects[i] != null) AttachEditorSnappingProxy(PlacedObjects[i]);
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

            ProceduralCableService.HideAllCableHandles();
            StructuralTrussService.HideAllTrussHandles();

            CapturePlaytestSnapshots();

            UnfreezePlayerControls();
            IsLevelInitialized = true;
        }

        public static GameObject GetAimedPlacedObject()
        {
            if (EditorViewportCamera.ViewportCamera == null) return null;

            Ray ray = Input.GetMouseButton(1)
                ? EditorViewportCamera.ViewportCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : EditorViewportCamera.ViewportCamera.ScreenPointToRay(Input.mousePosition);

            RaycastHit[] hits = Physics.RaycastAll(ray, 2000f, ~0, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0) return null;

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int h = 0; h < hits.Length; h++)
            {
                Collider col = hits[h].collider;
                if (col == null || col.gameObject == null) continue;
                GameObject hitGo = col.gameObject;

                if (IsWaypointMarker(hitGo, out _, out _))
                    return hitGo;

                if (ProceduralCableService.IsCableHandle(hitGo, out _, out _))
                    return hitGo;

                if (StructuralTrussService.IsTrussHandle(hitGo, out _, out _))
                    return hitGo;
            }

            for (int h = 0; h < hits.Length; h++)
            {
                Collider col = hits[h].collider;
                if (col == null || col.gameObject == null) continue;
                GameObject hitGo = col.gameObject;

                if (hitGo == null) continue;

                string n = hitGo.name;
                if (string.IsNullOrEmpty(n)) continue;

                if (n.Contains("Highlight") || n.Contains("Beacon") || n.Contains("Ghost")) continue;
                if (n.StartsWith("Studio_3D_Gizmo") || (hitGo.transform.root != null && hitGo.transform.root.name == "Studio_3D_Gizmo_Root")) continue;
                if (_cachedPlayer != null && (hitGo == _cachedPlayer || hitGo.transform.root.gameObject == _cachedPlayer)) continue;

                Transform curr = hitGo.transform;
                while (curr != null)
                {
                    if (curr.gameObject != null && PlacedObjects.Contains(curr.gameObject))
                    {
                        if (StudioUIManager.LockedObjects.Contains(curr.gameObject))
                            break;

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

            if (_cachedPlayer != null && _cachedPlayer.activeInHierarchy) return _cachedPlayer;

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
}