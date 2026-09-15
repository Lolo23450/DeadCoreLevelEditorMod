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
        public float Scale;
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

        // Clipboard System (Ctrl+C / Ctrl+V)
        public static List<ClipboardItem> Clipboard = new List<ClipboardItem>();

        // Placement & Snapping Settings
        public static float ActivePlacementScale = 1.0f;
        public static float CurrentGridSnap = 1.0f;
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

        // Motion Path & Parenting Tools
        public static Dictionary<GameObject, ObjectMotionPath> MotionPaths = new Dictionary<GameObject, ObjectMotionPath>();
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
                ShowNotification("Mode: [SELECT] - Click objects (Ctrl to multi-select, W/E/R to transform)");
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
                    ShowNotification("Mode: [PLACEMENT] - Choose an asset from the browser below");
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
                    if (SelectedObjects.Contains(obj))
                    {
                        SelectedObjects.Remove(obj);
                    }
                    else
                    {
                        SelectedObjects.Add(obj);
                    }
                }
            }
            else
            {
                SelectedObjects.Clear();
                if (obj != null)
                {
                    SelectedObjects.Add(obj);
                }
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
            StudioUIManager.RefreshHierarchy();
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
            {
                return;
            }

            Material highlightMat = GetHighlightMaterial();

            for (int i = 0; i < SelectedObjects.Count; i++)
            {
                GameObject obj = SelectedObjects[i];
                if (obj == null || !obj.activeSelf) continue;

                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "Studio_Selection_Highlight_Wire";
                box.layer = 2;
                Collider c = box.GetComponent<Collider>();
                if (c != null) GameObject.DestroyImmediate(c);
                if (highlightMat != null) box.GetComponent<Renderer>().material = highlightMat;

                GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                beacon.name = "Studio_Selection_Apex_Beacon";
                beacon.layer = 2;
                Collider bc = beacon.GetComponent<Collider>();
                if (bc != null) GameObject.DestroyImmediate(bc);
                if (highlightMat != null) beacon.GetComponent<Renderer>().material = highlightMat;

                Bounds b = PlacementHologramController.CalculateOptimizedProxyBounds(obj);
                Vector3 worldCenter = obj.transform.TransformPoint(b.center);

                box.transform.position = worldCenter;
                box.transform.rotation = obj.transform.rotation;
                box.transform.localScale = Vector3.Scale(b.size * 1.04f, obj.transform.lossyScale);

                beacon.transform.position = worldCenter + Vector3.up * (b.extents.y * obj.transform.lossyScale.y + 0.6f);
                beacon.transform.localScale = Vector3.one * 0.35f;

                _highlightBoxes.Add(box);
                _selectionBeacons.Add(beacon);
            }
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
        // COPY, PASTE & DELETE WORKFLOW
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
                    Scale = obj.transform.localScale.x,
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
                ShowNotification("Clipboard is empty. Press Ctrl+C to copy objects.");
                return;
            }

            Vector3 pasteOrigin;
            if (InteractionMode == EditorInteractionMode.PlacementMode)
            {
                pasteOrigin = PlacementHologramController.TargetPosition;
            }
            else
            {
                // Paste slightly offset from camera or current selection
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
                            Speed = item.MotionPath.Speed
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
                        Scale = item.Scale,
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

        public static void DeleteSelectedObjects()
        {
            if (SelectedObjects.Count == 0) return;

            var toDelete = new List<GameObject>(SelectedObjects);
            for (int i = 0; i < toDelete.Count; i++)
            {
                if (toDelete[i] != null)
                {
                    DeleteSpecifiedObject(toDelete[i]);
                }
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
            MelonLogger.Msg($">> Viewport State: {(IsEditModeActive ? "[STUDIO EDIT MODE]" : "[PLAYTEST MODE]")}");
            SetSpotlightMeshesVisible(IsEditModeActive);

            GameObject player = FindPlayerEntity();

            if (IsEditModeActive)
            {
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
                EditorViewportCamera.DestroyCamera();
                PlacementHologramController.DestroyPreview();
                StudioGizmoController.DestroyGizmo();

                CleanHighlightPool();

                StudioUIManager.SetUIVisible(false);

                if (PlayerCameraInstance != null) PlayerCameraInstance.enabled = true;

                SetPlayerControlsActive(true);

                if (!IsLevelCompleted)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                MelonLogger.Msg(">> Restored Playtest Mode.");
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

            SceneHarvestingService.CleanupProceduralResources();

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
            CurrentGridSnap = 1.0f;
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
                                    MelonLogger.Msg($">> [Checkpoint] Tagged checkpoint at {cp.transform.position}!");
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

                bool isCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

                if (InteractionMode == EditorInteractionMode.PlacementMode)
                {
                    PlacementHologramController.UpdatePlacement();
                    StudioGizmoController.UpdateGizmo();
                }
                else
                {
                    StudioGizmoController.UpdateGizmo();

                    // Viewport Click Selection (Ctrl toggles multi-selection)
                    if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !StudioUIManager.IsPointerOverUI())
                    {
                        if (!StudioGizmoController.IsHoveringHandle)
                        {
                            GameObject aimed = GetAimedPlacedObject();
                            SelectObject(aimed, isAdditive: isCtrl);
                        }
                    }
                }

                // Delete selected objects using "Spr" (Supr / Delete / Backspace)
                bool isDeleteKey = Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace);
                if (isDeleteKey && GUIUtility.keyboardControl == 0 && !StudioUIManager.IsPointerOverUI())
                {
                    DeleteSelectedObjects();
                }

                // Copy (Ctrl + C) and Paste (Ctrl + V)
                if (isCtrl && GUIUtility.keyboardControl == 0)
                {
                    if (Input.GetKeyDown(KeyCode.C))
                    {
                        CopySelectedObjects();
                    }
                    else if (Input.GetKeyDown(KeyCode.V))
                    {
                        PasteClipboardObjects();
                    }
                    else if (Input.GetKeyDown(KeyCode.Z))
                    {
                        if (Input.GetKey(KeyCode.LeftShift)) PerformRedo();
                        else PerformUndo();
                    }
                    else if (Input.GetKeyDown(KeyCode.Y))
                    {
                        PerformRedo();
                    }
                }
            }

            // Shortcuts
            if (Input.GetKeyDown(KeyCode.F1)) ToggleEditMode();
            if (Input.GetKeyDown(KeyCode.F4)) SceneHarvestingService.DebugDumpSceneLighting();
            if (Input.GetKeyDown(KeyCode.F5)) LevelPersistenceService.SaveLevel(MapBrowserService.SelectedMapName);
            if (Input.GetKeyDown(KeyCode.F6)) LevelPersistenceService.LoadLevel(MapBrowserService.SelectedMapName);
        }

        // =========================================================================
        // SOLID OBJECT SPAWNING & HITBOX ENFORCEMENT
        // =========================================================================

        public static GameObject SpawnAssetByName(string partialName, Vector3 position, float scale, Quaternion? customRotation = null)
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

        public static GameObject SpawnCatalogObject(CatalogAsset asset, Vector3 position, float scale, Quaternion? customRotation = null)
        {
            if (asset == null || asset.SourceTemplate == null) return null;

            GameObject obj = GameObject.Instantiate(asset.SourceTemplate);
            obj.name = "Custom_" + asset.DisplayName.Replace(" ", "_");
            obj.transform.position = position;
            obj.transform.rotation = customRotation ?? GetCurrentCombinedRotation(asset);
            obj.transform.localScale = Vector3.one * scale;
            obj.SetActive(true);

            bool isHazard = asset.IsLaser || asset.IsRotatingLaser;
            bool isGate = asset.IsCheckPoint || asset.IsSpawnGate || asset.IsGoalGate;
            bool isLight = asset.IsSpotlight || asset.IsSunlight;
            bool isHelix = asset.IsHelix;

            // Configure colliders: solid geometry for platforms vs triggers for hazards/wind/gates
            Collider[] existingCols = obj.GetComponentsInChildren<Collider>(true);
            bool hasSolidCollider = false;

            for (int i = 0; i < existingCols.Length; i++)
            {
                Collider c = existingCols[i];
                if (c == null || c.gameObject.name == "Editor_Snapping_Proxy") continue;

                c.enabled = true;

                if (isHazard || isLight)
                {
                    c.isTrigger = true;
                }
                else if (isGate)
                {
                    // CHECKPOINTS & GATES: NEVER HAVE BLOCKING COLLISION
                    // Turn all gate/arch/pillar colliders into triggers so the player walks straight through
                    c.isTrigger = true;
                }
                else if (isHelix)
                {
                    // HELIX FIX: Only the wind pushing zone is a trigger!
                    // The switch target and housing remain solid non-triggers so player gun shots hit and deactivate the turbine
                    HelixPushingZone zone = c.GetComponent<HelixPushingZone>() ?? c.GetComponentInParent<HelixPushingZone>();
                    string cName = c.gameObject.name.ToLower();
                    if (zone != null || cName.Contains("zone") || cName.Contains("push") || cName.Contains("vent") || cName.Contains("wind"))
                    {
                        c.isTrigger = true;
                    }
                    else
                    {
                        c.isTrigger = false;
                        hasSolidCollider = true;
                    }
                }
                else
                {
                    // Standard building blocks (platforms, walls, pillars) must be solid
                    c.isTrigger = false;
                    hasSolidCollider = true;
                }
            }

            if (!hasSolidCollider && !isHazard && !isGate && !isLight && !isHelix && !asset.IsTurret)
            {
                MeshFilter[] mfs = obj.GetComponentsInChildren<MeshFilter>(true);
                for (int m = 0; m < mfs.Length; m++)
                {
                    if (mfs[m].sharedMesh != null)
                    {
                        MeshCollider mc = mfs[m].gameObject.AddComponent<MeshCollider>();
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

        public static void AttachEditorSnappingProxy(GameObject obj)
        {
            if (obj == null) return;

            Transform old = obj.transform.Find("Editor_Snapping_Proxy");
            if (old != null) GameObject.DestroyImmediate(old.gameObject);

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
            bc.size = proxyB.size;
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
            else if (low.Contains("turret"))
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

            PlacedObjects.Remove(target);
            PlacedObjectTypes.Remove(target);
            PlacedParentChildCounts.Remove(target);
            SelectedObjects.Remove(target);

            if (target.transform.parent != null)
                RecalculateParentChildCount(target.transform.parent.gameObject);

            if (PlacedLights.ContainsKey(target)) PlacedLights.Remove(target);
            if (MotionPaths.ContainsKey(target)) MotionPaths.Remove(target);
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
                    Scale = target.transform.localScale.x,
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
            PathEditTarget = null;
            ParentingChildTarget = null;
            UndoHistory.Clear();
            RedoHistory.Clear();

            CachedVoidDeathY = -140f;
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
            else if (record.ActionType == HistoryActionType.Reposition)
            {
                if (record.TargetObject != null)
                {
                    record.TargetObject.transform.position = record.PreviousPosition;
                    record.TargetObject.transform.rotation = record.PreviousRotation;
                    record.TargetObject.transform.localScale = record.PreviousScale;
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
            else if (record.ActionType == HistoryActionType.Reposition)
            {
                if (record.TargetObject != null)
                {
                    record.TargetObject.transform.position = record.NewPosition;
                    record.TargetObject.transform.rotation = record.NewRotation;
                    record.TargetObject.transform.localScale = record.NewScale;
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
                ts.enabled = true;
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
            if (l != null)
            {
                l.enabled = false;
                if (cfg.IsDirectional)
                {
                    Light targetSun = SceneHarvestingService.NativeSceneSun != null ? SceneHarvestingService.NativeSceneSun : l;
                    if (targetSun != null)
                    {
                        targetSun.gameObject.SetActive(true);
                        targetSun.enabled = true;
                        targetSun.type = LightType.Directional;
                        targetSun.color = cfg.Color;
                        targetSun.transform.rotation = lightObj.transform.rotation;
                        float hdrpSunIntensity = Mathf.Max(0.1f, cfg.Intensity) * 4000f;
                        targetSun.intensity = hdrpSunIntensity;
                        RenderSettings.sun = targetSun;
                    }
                }
                else
                {
                    l.type = LightType.Spot;
                    l.renderMode = LightRenderMode.Auto;
                    l.range = 150f;
                    l.spotAngle = Mathf.Clamp(cfg.SpotAngle, 5f, 150f);
                    l.color = cfg.Color;
                    l.intensity = Mathf.Pow(Mathf.Max(0.1f, cfg.Intensity), 2.2f) * 8000f;
                }
                l.enabled = true;
            }
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
                if (PlacedObjectTypes.TryGetValue(obj, out PlacedObjectType type) &&
                    (type == PlacedObjectType.Spotlight || type == PlacedObjectType.Sunlight))
                {
                    Transform housing = obj.transform.Find("Light_Housing");
                    if (housing != null) housing.gameObject.SetActive(visible);

                    Transform lens = obj.transform.Find("Light_Lens");
                    if (lens != null) lens.gameObject.SetActive(visible);
                }
            }
        }

        // =========================================================================
        // RUNTIME SIMULATION & PLAYER CONTROL CHECKS
        // =========================================================================

        private static void UpdateObjectMotionPaths(GameObject player, CharacterController cc)
        {
            if (MotionPaths.Count == 0) return;

            bool canPushPlayer = (player != null && !IsEditModeActive && cc != null && cc.isGrounded);
            Vector3 playerFeetPos = canPushPlayer ? player.transform.position : Vector3.zero;

            foreach (var kvp in MotionPaths)
            {
                GameObject obj = kvp.Key;
                ObjectMotionPath path = kvp.Value;
                if (obj == null || !obj.activeSelf || !path.IsActive) continue;

                if (IsEditModeActive && (PathEditTarget == obj || RepositionTarget == obj))
                {
                    obj.transform.position = path.PointA;
                    continue;
                }

                float dist = path.TotalDistance;
                if (dist < 0.05f) continue;

                float speed = Mathf.Max(0.2f, path.Speed);
                float duration = dist / speed;
                float t = Mathf.PingPong(Time.time / duration, 1.0f);
                Vector3 targetPos = path.EvaluatePosition(t);
                Vector3 delta = targetPos - obj.transform.position;

                obj.transform.position = targetPos;

                if (canPushPlayer && delta.sqrMagnitude > 0.00001f)
                {
                    if (path.CachedColliders == null || path.CachedColliders.Length == 0)
                        path.CachedColliders = obj.GetComponentsInChildren<Collider>(true);

                    for (int c = 0; c < path.CachedColliders.Length; c++)
                    {
                        Collider col = path.CachedColliders[c];
                        if (col != null && col.bounds.Contains(playerFeetPos + Vector3.down * 0.15f))
                        {
                            cc.Move(delta);
                            break;
                        }
                    }
                }
            }
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
                MelonLogger.Msg($">> [VICTORY] Course finished cleanly in {LevelTimer:F2} seconds!");
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
                    MelonLogger.Msg(">> [LASER] Hazard collision detected! Respawning...");
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

                MelonLogger.Msg(">> [Respawn] Returned to active Checkpoint!");
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
                MelonLogger.Warning($">> [Abort] Attempted to initialize custom level in non-gameplay scene: '{curScene}'.");
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
                MelonLogger.Msg($">> Auto-loading selected map: '{MapBrowserService.SelectedMapName}'...");
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
            _lightRefreshTimer = 0.35f;
            SetSpotlightMeshesVisible(false);

            UnfreezePlayerControls();
            IsLevelInitialized = true;
            MelonLogger.Msg(">> Custom Level Initialized & Ready!");
        }

        public static GameObject GetAimedPlacedObject()
        {
            if (EditorViewportCamera.ViewportCamera == null) return null;

            Ray ray = Input.GetMouseButton(1)
                ? EditorViewportCamera.ViewportCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : EditorViewportCamera.ViewportCamera.ScreenPointToRay(Input.mousePosition);

            int mask = ~LayerMask.GetMask("Ignore Raycast");
            if (Physics.Raycast(ray, out RaycastHit hit, 2000f, mask, QueryTriggerInteraction.Collide))
            {
                GameObject hitObj = hit.collider.gameObject;
                for (int i = 0; i < PlacedObjects.Count; i++)
                {
                    GameObject obj = PlacedObjects[i];
                    if (obj == null) continue;
                    if (hitObj == obj || hitObj.transform.IsChildOf(obj.transform))
                        return obj;
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

            GameObject tagged = GameObject.FindWithTag("Player");
            if (tagged != null) return tagged;

            CharacterController cc = GameObject.FindObjectOfType<CharacterController>();
            if (cc != null) return cc.transform.root.gameObject;

            return null;
        }

        public static CharacterController GetPlayerController()
        {
            if (_cachedCharacterController != null && _cachedCharacterController.gameObject.activeInHierarchy)
                return _cachedCharacterController;

            FindPlayerEntity();
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
                float scale = obj.transform.localScale.x;
                string name = obj.name.StartsWith("Custom_") ? obj.name.Substring(7) : obj.name;

                float param = 0f;
                string extraParams = "";

                if (EditorSessionManager.JumperForces.ContainsKey(obj))
                {
                    param = EditorSessionManager.JumperForces[obj];
                }
                else if (EditorSessionManager.TurbineSpeeds.ContainsKey(obj))
                {
                    param = EditorSessionManager.TurbineSpeeds[obj];
                }
                else if (EditorSessionManager.TurretFireDelays.ContainsKey(obj))
                {
                    param = EditorSessionManager.TurretFireDelays[obj];
                }
                else if (EditorSessionManager.LaserRotationSpeeds.ContainsKey(obj))
                {
                    param = EditorSessionManager.LaserRotationSpeeds[obj];
                }
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
                    pathParams = $";PATH:1:{mp.Speed.ToString("F2", inv)}:{mp.PointA.x.ToString("F4", inv)}:{mp.PointA.y.ToString("F4", inv)}:{mp.PointA.z.ToString("F4", inv)}:{mp.PointB.x.ToString("F4", inv)}:{mp.PointB.y.ToString("F4", inv)}:{mp.PointB.z.ToString("F4", inv)}";
                }

                int parentIdx = -1;
                if (obj.transform.parent != null && EditorSessionManager.PlacedObjects.Contains(obj.transform.parent.gameObject))
                {
                    parentIdx = EditorSessionManager.PlacedObjects.IndexOf(obj.transform.parent.gameObject);
                }
                string parentParams = $";PARENT:{parentIdx}";

                lines.Add($"{name};{pos.x.ToString("F4", inv)};{pos.y.ToString("F4", inv)};{pos.z.ToString("F4", inv)};{scale.ToString("F4", inv)};{rot.x.ToString("F4", inv)};{rot.y.ToString("F4", inv)};{rot.z.ToString("F4", inv)};{rot.w.ToString("F4", inv)};{param.ToString("F2", inv)}{extraParams}{pathParams}{parentParams}");
            }

            File.WriteAllLines(path, lines.ToArray());
            ThumbnailCaptureService.CaptureLevelThumbnail(path, EditorSessionManager.PlacedObjects, EditorSessionManager.LevelSpawnPosition);

            EditorSessionManager.ShowNotification($"Saved {lines.Count - 5} objects to {cleanName}.txt!");
            MelonLogger.Msg($">> Saved {lines.Count - 5} objects to {path}!");

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
                float scale = ParseFloat(p[4]);
                if (scale <= 0.0001f) scale = 0.55f;

                Quaternion rot = Quaternion.identity;
                if (p.Length >= 9)
                {
                    rot = new Quaternion(ParseFloat(p[5]), ParseFloat(p[6]), ParseFloat(p[7]), ParseFloat(p[8]));
                    if (rot.x == 0f && rot.y == 0f && rot.z == 0f && rot.w == 0f) rot = Quaternion.identity;
                }

                float customParam = (p.Length >= 10) ? ParseFloat(p[9]) : 0f;

                GameObject obj = EditorSessionManager.SpawnAssetByName(rawName, pos, scale, rot);
                if (obj != null)
                {
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

                            obj.transform.position = pA;
                            EditorSessionManager.MotionPaths[obj] = new ObjectMotionPath
                            {
                                PointA = pA,
                                PointB = pB,
                                Speed = spd > 0.1f ? spd : 3.5f
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
            MelonLogger.Msg($">> Loaded {count} objects from {fullPath}!");
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

            MelonLogger.Msg($">> [Map Browser] Launching: '{SelectedMapName}' via Scene: ['{targetScene}']");
            SceneLoader.LoadLevel(targetScene, false, true);
        }
    }
}