using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
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
    // SECTION 1: STUDIO UGUI SYSTEM (DOCKED, INTERACTIVE TREE HIERARCHY)
    // =========================================================================

    public static class StudioUIManager
    {
        private static GameObject _canvasRoot = null;
        private static Canvas _canvas = null;

        // Top Toolbar
        private static GameObject _toolbarPanel = null;
        private static Button _modeToggleBtn = null;
        private static TMP_Text _modeToggleBtnText = null;
        private static TMP_Text _surfaceAlignBtnText = null;
        private static TMP_Text _gridSnapBtnText = null;

        // Scene Hierarchy (Left Panel: 260px Interactive Tree View)
        private static GameObject _hierarchyPanel = null;
        private static ScrollRect _hierarchyScrollRect = null;
        private static RectTransform _hierarchyContent = null;
        private static TMP_InputField _hierarchySearchInput = null;
        private static readonly List<GameObject> _hierarchyRows = new List<GameObject>();
        private static readonly HashSet<GameObject> _collapsedParents = new HashSet<GameObject>();

        private static Slider _motionPathRotSlider = null;
        private static TMP_Text _motionPathRotValText = null;
        private static Button _motionPathAxisBtn = null;
        private static TMP_Text _motionPathAxisBtnText = null;

        private static TMP_InputField _motionPathAxisXInput = null;
        private static TMP_InputField _motionPathAxisYInput = null;
        private static TMP_InputField _motionPathAxisZInput = null;

        // Hierarchy Drag & Drop Mapping
        private static readonly Dictionary<GameObject, GameObject> _targetToRowMap = new Dictionary<GameObject, GameObject>();
        private static readonly Dictionary<GameObject, GameObject> _rowToTargetMap = new Dictionary<GameObject, GameObject>();
        private static GameObject _dragCandidateNode = null;
        private static Vector2 _dragStartMousePos = Vector2.zero;
        private static bool _isDraggingHierarchyNode = false;
        private static GameObject _dragGhostObj = null;
        private static TMP_Text _dragGhostText = null;

        // Contextual Inspector (Right Panel: 280px - 380px)
        private static GameObject _inspectorPanel = null;
        private static RectTransform _inspectorPanelRt = null;
        private static RectTransform _inspectorContent = null;
        private static TMP_Text _inspectorTitleText = null;
        private static Button _inspectorExpandBtn = null;
        private static TMP_Text _inspectorExpandBtnText = null;
        private static bool _isInspectorExpanded = false;

        // Transform Coordinate Inputs
        private static TMP_InputField _posXInput = null;
        private static TMP_InputField _posYInput = null;
        private static TMP_InputField _posZInput = null;
        private static TMP_InputField _rotXInput = null;
        private static TMP_InputField _rotYInput = null;
        private static TMP_InputField _rotZInput = null;
        private static TMP_InputField _scaleXInput = null;
        private static TMP_InputField _scaleYInput = null;
        private static TMP_InputField _scaleZInput = null;

        // Section Cards
        private static GameObject _jumperSection = null;
        private static Slider _jumperSlider = null;
        private static TMP_Text _jumperValueText = null;

        private static GameObject _turbineSection = null;
        private static Slider _turbineSlider = null;
        private static TMP_Text _turbineValueText = null;

        private static GameObject _turretSection = null;
        private static Slider _turretSlider = null;
        private static TMP_Text _turretValueText = null;

        private static GameObject _laserSection = null;
        private static Slider _laserSlider = null;
        private static TMP_Text _laserValueText = null;

        // Merged Lighting & Atmosphere Section + RGB Selector
        private static GameObject _lightSection = null;
        private static TMP_Text _lightTypeBadgeText = null;
        private static Slider _lightIntensitySlider = null;
        private static TMP_Text _lightIntensityValText = null;
        private static GameObject _spotAngleRowObj = null;
        private static Slider _lightAngleSlider = null;
        private static TMP_Text _lightAngleValText = null;
        private static Slider _lightVolSlider = null;
        private static TMP_Text _lightVolValText = null;
        private static Image _lightColorPreviewSwatch = null;

        private static Slider _lightRSlider = null;
        private static TMP_Text _lightRValText = null;
        private static Slider _lightGSlider = null;
        private static TMP_Text _lightGValText = null;
        private static Slider _lightBSlider = null;
        private static TMP_Text _lightBValText = null;

        // Motion Path Section
        private static GameObject _motionPathSection = null;
        private static TMP_Text _motionPathStatusText = null;
        private static Slider _motionPathSpeedSlider = null;
        private static TMP_Text _motionPathSpeedValText = null;
        private static GameObject _motionPathCreateBtnObj = null;
        private static GameObject _motionPathActiveControlsObj = null;

        // Docked Bottom Asset Browser state
        private static GameObject _assetBrowserPanel = null;
        private static RectTransform _browserContent = null;
        private static TMP_InputField _browserSearchInput = null;
        private static string _activeBrowserCategory = "Architecture";
        private static AssetSizeTier _activeSizeFilter = AssetSizeTier.All;
        private static bool _sortSizeAscending = true;
        private static TMP_Text _sortSizeBtnText = null;
        private static readonly List<GameObject> _browserCards = new List<GameObject>();

        // Toast Notification Banner
        private static TMP_Text _toastText = null;
        private static bool _suppressInspectorCallbacks = false;

        public static bool IsPointerOverUI()
        {
            if (_canvasRoot == null || !_canvasRoot.activeInHierarchy) return false;
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        public static void SetNotificationText(string text)
        {
            if (_toastText != null)
            {
                _toastText.text = text;
            }
        }

        public static void SetUIVisible(bool visible)
        {
            if (_canvasRoot != null)
            {
                _canvasRoot.SetActive(visible);
            }
        }

        public static void InitializeUI()
        {
            if (_canvasRoot != null) return;

            if (GameObject.FindObjectOfType<EventSystem>() == null)
            {
                GameObject esObj = new GameObject("Studio_EventSystem");
                esObj.AddComponent<EventSystem>();
                esObj.AddComponent<StandaloneInputModule>();
            }

            _canvasRoot = new GameObject("Studio_Editor_Canvas");
            _canvas = _canvasRoot.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 999;

            CanvasScaler scaler = _canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _canvasRoot.AddComponent<GraphicRaycaster>();

            try { BuildTopToolbar(); } catch (Exception ex) { MelonLogger.Error($"[UI] TopToolbar Error: {ex}"); }
            try { BuildHierarchyPanel(); } catch (Exception ex) { MelonLogger.Error($"[UI] HierarchyPanel Error: {ex}"); }
            try { BuildInspectorPanel(); } catch (Exception ex) { MelonLogger.Error($"[UI] InspectorPanel Error: {ex}"); }
            try { BuildAssetBrowserPanel(); } catch (Exception ex) { MelonLogger.Error($"[UI] AssetBrowser Error: {ex}"); }
            try { BuildToastOverlay(); } catch (Exception ex) { MelonLogger.Error($"[UI] Toast Error: {ex}"); }

            EnsureSelectableColliders();
            RefreshHierarchy();
            RefreshAssetBrowser();
            RefreshModeDisplay();
            NotifyObjectSelected(EditorSessionManager.SelectedObject);
        }

        public static void DestroyUI()
        {
            AssetThumbnailRenderer.Cleanup();
            if (_canvasRoot != null)
            {
                GameObject.Destroy(_canvasRoot);
                _canvasRoot = null;
            }
        }

        public static void EnsureSelectableColliders()
        {
            if (EditorSessionManager.PlacedObjects == null) return;

            for (int i = 0; i < EditorSessionManager.PlacedObjects.Count; i++)
            {
                GameObject obj = EditorSessionManager.PlacedObjects[i];
                if (obj == null) continue;

                if (EditorSessionManager.PlacedObjectTypes.TryGetValue(obj, out var pType))
                {
                    bool isLight = (pType == PlacedObjectType.Spotlight || pType == PlacedObjectType.Sunlight);
                    bool isGate = (pType == PlacedObjectType.Checkpoint || pType == PlacedObjectType.SpawnGate || pType == PlacedObjectType.GoalGate);

                    if (isLight || isGate)
                    {
                        BoxCollider bc = obj.GetComponent<BoxCollider>();
                        if (bc == null)
                        {
                            bc = obj.AddComponent<BoxCollider>();
                            bc.size = isGate ? new Vector3(3.5f, 4.5f, 1.5f) : new Vector3(1.4f, 1.4f, 1.6f);
                            bc.center = isGate ? new Vector3(0f, 2.25f, 0f) : new Vector3(0f, 0f, 0.4f);
                        }

                        // 1. MUST be a trigger so the player runs straight through without colliding
                        bc.isTrigger = true;

                        // 2. Only active in Edit Mode for clicking/selection; disabled during playtest for gates
                        bc.enabled = EditorSessionManager.IsEditModeActive || isLight;

                        obj.layer = 0; // Default layer so the editor raycast can hit it in Edit Mode
                    }
                }
            }
        }

        public static void RefreshModeDisplay()
        {
            if (_modeToggleBtnText == null) return;

            if (EditorSessionManager.InteractionMode == EditorInteractionMode.SelectMode)
            {
                _modeToggleBtnText.text = "MODE: [SELECT]";
                _modeToggleBtn.image.color = new Color(0.18f, 0.45f, 0.85f, 1f);
            }
            else
            {
                _modeToggleBtnText.text = "MODE: [PLACEMENT]";
                _modeToggleBtn.image.color = new Color(0.85f, 0.45f, 0.15f, 1f);
            }
        }

        // =========================================================================
        // ISOMETRIC THUMBNAIL RENDERER
        // =========================================================================

        public static class AssetThumbnailRenderer
        {
            private static Camera _previewCam = null;
            private static GameObject _studioStage = null;
            private static Light _studioKeyLight = null;
            private static Light _studioFillLight = null;
            private static RenderTexture _previewRt = null;

            private static readonly Vector3 StagePosition = new Vector3(9000f, 9000f, 9000f);

            private static void EnsureStudio()
            {
                if (_studioStage != null) return;

                _studioStage = new GameObject("Asset_Thumbnail_Studio_Stage");
                _studioStage.transform.position = StagePosition;
                _studioStage.layer = 2;

                GameObject camObj = new GameObject("Studio_Cam");
                camObj.transform.SetParent(_studioStage.transform, false);
                _previewCam = camObj.AddComponent<Camera>();
                _previewCam.clearFlags = CameraClearFlags.Color;
                _previewCam.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 1f);
                _previewCam.cullingMask = 1 << 2;
                _previewCam.fieldOfView = 18f;
                _previewCam.nearClipPlane = 0.1f;
                _previewCam.farClipPlane = 500f;

                _previewRt = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32);
                _previewCam.targetTexture = _previewRt;

                GameObject keyObj = new GameObject("Studio_KeyLight");
                keyObj.transform.SetParent(_studioStage.transform, false);
                _studioKeyLight = keyObj.AddComponent<Light>();
                _studioKeyLight.type = LightType.Directional;
                _studioKeyLight.color = new Color(1f, 0.95f, 0.88f);
                _studioKeyLight.intensity = 0.95f;
                _studioKeyLight.cullingMask = 1 << 2;
                keyObj.transform.rotation = Quaternion.Euler(38f, -42f, 0f);

                GameObject fillObj = new GameObject("Studio_FillLight");
                fillObj.transform.SetParent(_studioStage.transform, false);
                _studioFillLight = fillObj.AddComponent<Light>();
                _studioFillLight.type = LightType.Directional;
                _studioFillLight.color = new Color(0.45f, 0.75f, 1f);
                _studioFillLight.intensity = 0.35f;
                _studioFillLight.cullingMask = 1 << 2;
                fillObj.transform.rotation = Quaternion.Euler(60f, 135f, 0f);
            }

            public static Sprite GenerateThumbnail(CatalogAsset asset)
            {
                if (asset == null || asset.SourceTemplate == null) return null;

                EnsureStudio();

                GameObject tempModel = GameObject.Instantiate(asset.SourceTemplate);
                tempModel.SetActive(true);
                tempModel.transform.position = StagePosition;

                tempModel.transform.rotation = Quaternion.Euler(30f, -45f, 0f) * asset.BaseRotation;
                tempModel.transform.localScale = Vector3.one * asset.DefaultScale;

                foreach (var tr in tempModel.GetComponentsInChildren<Transform>(true))
                {
                    tr.gameObject.layer = 2;
                }

                foreach (var l in tempModel.GetComponentsInChildren<Light>(true)) GameObject.DestroyImmediate(l);
                foreach (var col in tempModel.GetComponentsInChildren<Collider>(true)) GameObject.DestroyImmediate(col);
                foreach (var mb in tempModel.GetComponentsInChildren<MonoBehaviour>(true)) GameObject.DestroyImmediate(mb);

                Bounds b = new Bounds(tempModel.transform.position, Vector3.zero);
                bool hasBounds = false;
                foreach (var mf in tempModel.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh != null)
                    {
                        if (!hasBounds) { b = mf.sharedMesh.bounds; hasBounds = true; }
                        else b.Encapsulate(mf.sharedMesh.bounds);
                    }
                }

                if (!hasBounds) b = new Bounds(Vector3.zero, Vector3.one * 2f);

                Vector3 modelCenter = tempModel.transform.TransformPoint(b.center);
                float radius = Mathf.Max(b.extents.x, b.extents.y, b.extents.z) * asset.DefaultScale;
                if (radius < 0.2f) radius = 1.0f;

                float fovRad = _previewCam.fieldOfView * 0.5f * Mathf.Deg2Rad;
                float camDist = (radius / Mathf.Sin(fovRad)) * 1.35f;

                Vector3 camPos = modelCenter + new Vector3(camDist * 0.707f, camDist * 0.577f, -camDist * 0.707f);
                _previewCam.transform.position = camPos;
                _previewCam.transform.LookAt(modelCenter);

                RenderTexture.active = _previewRt;
                _previewCam.Render();

                Texture2D tex = new Texture2D(128, 128, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                tex.Apply();

                RenderTexture.active = null;
                GameObject.DestroyImmediate(tempModel);

                Sprite sprite = Sprite.Create(tex, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 100f);
                asset.ThumbnailSprite = sprite;
                asset.ThumbnailTexture = tex;

                return sprite;
            }

            public static void Cleanup()
            {
                if (_studioStage != null)
                {
                    GameObject.Destroy(_studioStage);
                    _studioStage = null;
                }
                if (_previewRt != null)
                {
                    _previewRt.Release();
                    _previewRt = null;
                }
            }
        }

        // =========================================================================
        // TOP TOOLBAR
        // =========================================================================

        private static void BuildTopToolbar()
        {
            _toolbarPanel = CreatePanel(_canvasRoot.transform, "Top_Toolbar",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -20f), new Vector2(0f, 40f),
                new Color(0.11f, 0.12f, 0.14f, 0.98f));

            HorizontalLayoutGroup hlg = _toolbarPanel.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(10, 10, 6, 6);
            hlg.spacing = 6f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            _modeToggleBtn = CreateButton(_toolbarPanel.transform, "Btn_ModeToggle", "MODE: [SELECT]", 145f, () =>
            {
                if (EditorSessionManager.InteractionMode == EditorInteractionMode.SelectMode)
                {
                    EditorSessionManager.SetInteractionMode(EditorInteractionMode.PlacementMode);
                }
                else
                {
                    EditorSessionManager.SetInteractionMode(EditorInteractionMode.SelectMode);
                }
            }, new Color(0.18f, 0.45f, 0.85f, 1f));
            _modeToggleBtnText = _modeToggleBtn.GetComponentInChildren<TMP_Text>();

            CreateButton(_toolbarPanel.transform, "Btn_Translate", "Move", 75f, () => SetGizmoMode(EditorGizmoMode.Translate));
            CreateButton(_toolbarPanel.transform, "Btn_Rotate", "Rotate", 75f, () => SetGizmoMode(EditorGizmoMode.Rotate));
            CreateButton(_toolbarPanel.transform, "Btn_Scale", "Scale", 75f, () => SetGizmoMode(EditorGizmoMode.Scale));

            Button alignBtn = CreateButton(_toolbarPanel.transform, "Btn_SurfaceAlign", "Align: OFF", 95f, () =>
            {
                EditorSessionManager.AutoAlignToSurface = !EditorSessionManager.AutoAlignToSurface;
                string st = EditorSessionManager.AutoAlignToSurface ? "ON" : "OFF";
                if (_surfaceAlignBtnText != null) _surfaceAlignBtnText.text = $"Align: {st}";
                EditorSessionManager.ShowNotification($"Surface Align: {st}");
                PlacementHologramController.ApplyRotationToPreview();
            });
            _surfaceAlignBtnText = alignBtn.GetComponentInChildren<TMP_Text>();

            Button snapBtn = CreateButton(_toolbarPanel.transform, "Btn_GridSnap", "Snap: 1.0m", 95f, () =>
            {
                if (EditorSessionManager.CurrentGridSnap == 1.0f) EditorSessionManager.CurrentGridSnap = 2.0f;
                else if (EditorSessionManager.CurrentGridSnap == 2.0f) EditorSessionManager.CurrentGridSnap = 4.0f;
                else if (EditorSessionManager.CurrentGridSnap == 4.0f) EditorSessionManager.CurrentGridSnap = 0.5f;
                else if (EditorSessionManager.CurrentGridSnap == 0.5f) EditorSessionManager.CurrentGridSnap = 0.0f;
                else EditorSessionManager.CurrentGridSnap = 1.0f;

                string st = (EditorSessionManager.CurrentGridSnap > 0.01f) ? $"{EditorSessionManager.CurrentGridSnap}m" : "OFF";
                if (_gridSnapBtnText != null) _gridSnapBtnText.text = $"Snap: {st}";
                EditorSessionManager.ShowNotification($"Grid Snap: {st}");
            });
            _gridSnapBtnText = snapBtn.GetComponentInChildren<TMP_Text>();

            CreateButton(_toolbarPanel.transform, "Btn_Snapshot", "[SNAP] 3D", 100f, () =>
            {
                ThumbnailCaptureService.CaptureLevelThumbnail(MapBrowserService.SelectedMapPath, EditorSessionManager.PlacedObjects, EditorSessionManager.LevelSpawnPosition);
                EditorSessionManager.ShowNotification("Captured 3D isometric thumbnail!");
            }, new Color(0.2f, 0.5f, 0.8f));

            CreateButton(_toolbarPanel.transform, "Btn_Playtest", "PLAYTEST (F1)", 130f, () => EditorSessionManager.ToggleEditMode(), new Color(0.18f, 0.65f, 0.32f));
            CreateButton(_toolbarPanel.transform, "Btn_Save", "Save (F5)", 80f, () => LevelPersistenceService.SaveLevel(MapBrowserService.SelectedMapName));
            CreateButton(_toolbarPanel.transform, "Btn_Load", "Load (F6)", 80f, () => LevelPersistenceService.LoadLevel(MapBrowserService.SelectedMapName));
        }

        private static void SetGizmoMode(EditorGizmoMode mode)
        {
            EditorSessionManager.SetInteractionMode(EditorInteractionMode.SelectMode);
            EditorSessionManager.CurrentGizmoMode = mode;
            EditorSessionManager.ShowNotification($"Active Gizmo Tool: {mode}");
        }

        // =========================================================================
        // HIERARCHY PANEL (INTERACTIVE TREE & INLINE TOOLS)
        // =========================================================================

        private static void BuildHierarchyPanel()
        {
            _hierarchyPanel = CreatePanel(_canvasRoot.transform, "Hierarchy_Panel",
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(130f, -20f), new Vector2(260f, -40f),
                new Color(0.11f, 0.12f, 0.14f, 0.98f));

            CreateText(_hierarchyPanel.transform, "Scene Hierarchy",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(12f, -14f), new Vector2(-24f, 24f),
                13f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);

            _hierarchySearchInput = CreateInputField(_hierarchyPanel.transform, "SearchInput",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(10f, -40f), new Vector2(-20f, 24f),
                "Filter hierarchy...", (val) => RefreshHierarchy());

            GameObject scrollObj = CreateScrollView(_hierarchyPanel.transform, "Hierarchy_Scroll",
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(6f, 24f), new Vector2(-12f, -112f),
                out _hierarchyContent);
            _hierarchyScrollRect = scrollObj.GetComponent<ScrollRect>();

            GameObject bottomBar = CreatePanel(_hierarchyPanel.transform, "Hierarchy_BottomBar",
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 18f), new Vector2(0f, 36f),
                new Color(0.08f, 0.09f, 0.11f, 0.95f));

            Button delBtn = CreateButton(bottomBar.transform, "Btn_DeleteSelected", "Delete Selected [Supr]", 240f, () =>
            {
                EditorSessionManager.DeleteSelectedObjects();
            }, new Color(0.75f, 0.22f, 0.22f, 1f));

            RectTransform dbrt = delBtn.GetComponent<RectTransform>();
            dbrt.anchorMin = new Vector2(0f, 0f);
            dbrt.anchorMax = new Vector2(1f, 1f);
            dbrt.offsetMin = new Vector2(8f, 4f);
            dbrt.offsetMax = new Vector2(-8f, -4f);
        }

        private static string GetRotationAxisName(int axisIndex)
        {
            switch (axisIndex)
            {
                case 0: return "X - Tumble";
                case 1: return "Y - Turntable";
                case 2: return "Z - Roll";
                case 3: return "Path (A -> B)";
                case 4: return "Custom Override";
                default: return "Custom Override";
            }
        }

        private static void OnMotionPathAxisInputChanged(string val)
        {
            if (_suppressInspectorCallbacks || EditorSessionManager.SelectedObject == null) return;

            GameObject target = EditorSessionManager.SelectedObject;
            if (EditorSessionManager.IsWaypointMarker(target, out GameObject owner, out bool _)) target = owner;

            if (!EditorSessionManager.MotionPaths.TryGetValue(target, out var mp) || mp == null) return;

            float x = ParseFloat(_motionPathAxisXInput?.text, mp.CustomAxis.x);
            float y = ParseFloat(_motionPathAxisYInput?.text, mp.CustomAxis.y);
            float z = ParseFloat(_motionPathAxisZInput?.text, mp.CustomAxis.z);

            Vector3 newAxis = new Vector3(x, y, z);
            if (newAxis.sqrMagnitude > 0.0001f)
                mp.CustomAxis = newAxis.normalized;
            else
                mp.CustomAxis = Vector3.up;

            mp.RotationAxis = 4; // Automatically switches to Custom Override on user input
            if (_motionPathAxisBtnText != null)
                _motionPathAxisBtnText.text = "Axis: [Custom Override]";
        }

        // =========================================================================
        // TARGETED FIX: Safe RectTransform Instantiation
        // =========================================================================
        private static void BuildInspectorPanel()
        {
            _isInspectorExpanded = false;
            _inspectorPanel = CreatePanel(_canvasRoot.transform, "Inspector_Panel",
                new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-140f, -20f), new Vector2(280f, -40f),
                new Color(0.12f, 0.13f, 0.15f, 0.98f));
            _inspectorPanelRt = _inspectorPanel.GetComponent<RectTransform>();

            // 1. TitleBar created with native RectTransform
            GameObject titleBar = new GameObject("TitleBar", Il2CppType.Of<RectTransform>());
            titleBar.transform.SetParent(_inspectorPanel.transform, false);
            RectTransform tbrt = titleBar.GetComponent<RectTransform>();
            if (tbrt != null)
            {
                tbrt.anchorMin = new Vector2(0f, 1f);
                tbrt.anchorMax = new Vector2(1f, 1f);
                tbrt.pivot = new Vector2(0.5f, 1f);
                tbrt.anchoredPosition = new Vector2(0f, 0f);
                tbrt.sizeDelta = new Vector2(0f, 34f);
            }

            _inspectorTitleText = CreateText(titleBar.transform, "Inspector",
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(12f, 0f), new Vector2(-95f, 0f),
                13f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);
            if (_inspectorTitleText != null)
            {
                _inspectorTitleText.enableWordWrapping = false;
                _inspectorTitleText.overflowMode = TextOverflowModes.Ellipsis;
            }

            _inspectorExpandBtn = CreateButton(titleBar.transform, "Btn_ExpandInspector", "[+ Expand]", 80f, ToggleInspectorExpansion, new Color(0.20f, 0.23f, 0.28f, 1f));
            if (_inspectorExpandBtn != null)
            {
                RectTransform ebrt = _inspectorExpandBtn.GetComponent<RectTransform>();
                if (ebrt != null)
                {
                    ebrt.anchorMin = new Vector2(1f, 0.5f);
                    ebrt.anchorMax = new Vector2(1f, 0.5f);
                    ebrt.pivot = new Vector2(1f, 0.5f);
                    ebrt.anchoredPosition = new Vector2(-8f, 0f);
                    ebrt.sizeDelta = new Vector2(80f, 22f);
                }
                _inspectorExpandBtnText = _inspectorExpandBtn.GetComponentInChildren<TMP_Text>();
            }

            GameObject scrollObj = CreateScrollView(_inspectorPanel.transform, "Inspector_Scroll",
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(6f, 6f), new Vector2(-12f, -38f),
                out _inspectorContent);

            // 1. Transform Card
            GameObject transCard = CreateSectionCard(_inspectorContent, "Transform", "Transform & Alignment");
            CreateVector3Row(transCard.transform, "Position", out _posXInput, out _posYInput, out _posZInput, OnTransformInputChanged);
            CreateVector3Row(transCard.transform, "Rotation", out _rotXInput, out _rotYInput, out _rotZInput, OnTransformInputChanged);

            GameObject rotBtnRow = CreateRowContainer(transCard.transform, "Row_RotButtons", 26f);
            SetupRowHorizontalLayout(rotBtnRow, 6f);

            CreateButton(rotBtnRow.transform, "Btn_Snap90", "Snap 90", 120f, () =>
            {
                if (EditorSessionManager.SelectedObject != null)
                {
                    Vector3 e = EditorSessionManager.SelectedObject.transform.eulerAngles;
                    e.x = Mathf.Round(e.x / 90f) * 90f;
                    e.y = Mathf.Round(e.y / 90f) * 90f;
                    e.z = Mathf.Round(e.z / 90f) * 90f;
                    EditorSessionManager.SelectedObject.transform.rotation = Quaternion.Euler(e);
                    StudioGizmoController.InvalidateCachedCenter(EditorSessionManager.SelectedObject);
                    RefreshInspectorValues();
                    EditorSessionManager.ShowNotification("Rotation snapped to 90 deg");
                }
            });
            CreateButton(rotBtnRow.transform, "Btn_ResetRot", "Reset Rot", 120f, () =>
            {
                if (EditorSessionManager.SelectedObject != null)
                {
                    EditorSessionManager.SelectedObject.transform.rotation = Quaternion.identity;
                    StudioGizmoController.InvalidateCachedCenter(EditorSessionManager.SelectedObject);
                    RefreshInspectorValues();
                    EditorSessionManager.ShowNotification("Rotation reset to (0,0,0)");
                }
            });

            CreateVector3Row(transCard.transform, "Scale", out _scaleXInput, out _scaleYInput, out _scaleZInput, OnTransformInputChanged);

            // 2. Gameplay Cards
            _jumperSection = CreateSectionCard(_inspectorContent, "Jumper", "Jumper Launch Pad");
            CreateInspectorSliderRow(_jumperSection.transform, "Launch Force", out _jumperSlider, out _jumperValueText, 5f, 75f, (val) =>
            {
                if (_suppressInspectorCallbacks || EditorSessionManager.SelectedObject == null) return;
                EditorSessionManager.ApplyJumperForce(EditorSessionManager.SelectedObject, val);
                if (_jumperValueText != null) _jumperValueText.text = $"{val:F1}";
            });

            _turbineSection = CreateSectionCard(_inspectorContent, "Turbine", "Helix Turbine Fan");
            CreateInspectorSliderRow(_turbineSection.transform, "Wind Speed", out _turbineSlider, out _turbineValueText, 5f, 100f, (val) =>
            {
                if (_suppressInspectorCallbacks || EditorSessionManager.SelectedObject == null) return;
                EditorSessionManager.ApplyTurbineSpeed(EditorSessionManager.SelectedObject, val);
                if (_turbineValueText != null) _turbineValueText.text = $"{val:F1}";
            });

            _turretSection = CreateSectionCard(_inspectorContent, "Turret", "Defense Turret");
            CreateInspectorSliderRow(_turretSection.transform, "Fire Delay", out _turretSlider, out _turretValueText, 0.1f, 5.0f, (val) =>
            {
                if (_suppressInspectorCallbacks || EditorSessionManager.SelectedObject == null) return;
                EditorSessionManager.ApplyTurretSettings(EditorSessionManager.SelectedObject, val, 1500f);
                if (_turretValueText != null) _turretValueText.text = $"{val:F2}s";
            });

            _laserSection = CreateSectionCard(_inspectorContent, "Laser", "Laser Barrier Hazard");
            CreateInspectorSliderRow(_laserSection.transform, "Rotation Spd", out _laserSlider, out _laserValueText, 0f, 180f, (val) =>
            {
                if (_suppressInspectorCallbacks || EditorSessionManager.SelectedObject == null) return;
                EditorSessionManager.LaserRotationSpeeds[EditorSessionManager.SelectedObject] = val;
                if (_laserValueText != null) _laserValueText.text = $"{val:F0} d/s";
            });

            // 3. Merged Lighting Card + RGB Color Selector
            _lightSection = CreateSectionCard(_inspectorContent, "Lighting", "Lighting Properties");

            GameObject badgeRow = CreateRowContainer(_lightSection.transform, "Row_Badge", 22f);
            _lightTypeBadgeText = CreateText(badgeRow.transform, "Type: [Tech Spotlight]", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 11f, FontStyles.Bold, new Color(0.25f, 0.9f, 1f), TextAlignmentOptions.MidlineLeft);

            CreateInspectorSliderRow(_lightSection.transform, "Intensity", out _lightIntensitySlider, out _lightIntensityValText, 0.1f, 30f, (val) =>
            {
                if (_suppressInspectorCallbacks || EditorSessionManager.SelectedObject == null) return;
                if (EditorSessionManager.PlacedLights.TryGetValue(EditorSessionManager.SelectedObject, out var cfg))
                {
                    cfg.Intensity = val;
                    EditorSessionManager.ApplyLightConfig(EditorSessionManager.SelectedObject, cfg);
                    if (_lightIntensityValText != null) _lightIntensityValText.text = $"{val:F1}";
                }
            });

            _spotAngleRowObj = CreateInspectorSliderRow(_lightSection.transform, "Spot Angle", out _lightAngleSlider, out _lightAngleValText, 10f, 150f, (val) =>
            {
                if (_suppressInspectorCallbacks || EditorSessionManager.SelectedObject == null) return;
                if (EditorSessionManager.PlacedLights.TryGetValue(EditorSessionManager.SelectedObject, out var cfg))
                {
                    cfg.SpotAngle = val;
                    EditorSessionManager.ApplyLightConfig(EditorSessionManager.SelectedObject, cfg);
                    if (_lightAngleValText != null) _lightAngleValText.text = $"{val:F0} deg";
                }
            });

            CreateInspectorSliderRow(_lightSection.transform, "Volumetric", out _lightVolSlider, out _lightVolValText, 0f, 10f, (val) =>
            {
                if (_suppressInspectorCallbacks || EditorSessionManager.SelectedObject == null) return;
                if (EditorSessionManager.PlacedLights.TryGetValue(EditorSessionManager.SelectedObject, out var cfg))
                {
                    cfg.VolumetricIntensity = val;
                    EditorSessionManager.ApplyLightConfig(EditorSessionManager.SelectedObject, cfg);
                    if (_lightVolValText != null) _lightVolValText.text = $"{val:F1}";
                }
            });

            GameObject swatchRow = CreateRowContainer(_lightSection.transform, "Row_Swatch", 24f);
            CreateText(swatchRow.transform, "Active Color Swatch", new Vector2(0f, 0f), new Vector2(0.65f, 1f), new Vector2(4f, 0f), Vector2.zero, 10f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);

            // 2. Swatch created with native RectTransform
            GameObject swatchObj = new GameObject("Swatch", Il2CppType.Of<RectTransform>());
            swatchObj.transform.SetParent(swatchRow.transform, false);
            RectTransform swrt = swatchObj.GetComponent<RectTransform>();
            if (swrt != null)
            {
                swrt.anchorMin = new Vector2(1f, 0.5f);
                swrt.anchorMax = new Vector2(1f, 0.5f);
                swrt.pivot = new Vector2(1f, 0.5f);
                swrt.anchoredPosition = new Vector2(-4f, 0f);
                swrt.sizeDelta = new Vector2(60f, 18f);
            }

            _lightColorPreviewSwatch = swatchObj.AddComponent<Image>();
            _lightColorPreviewSwatch.color = Color.cyan;

            CreateInspectorSliderRow(_lightSection.transform, "Red (R)", out _lightRSlider, out _lightRValText, 0f, 255f, (val) =>
            {
                if (_suppressInspectorCallbacks || EditorSessionManager.SelectedObject == null) return;
                OnLightRGBChanged();
            });

            CreateInspectorSliderRow(_lightSection.transform, "Green (G)", out _lightGSlider, out _lightGValText, 0f, 255f, (val) =>
            {
                if (_suppressInspectorCallbacks || EditorSessionManager.SelectedObject == null) return;
                OnLightRGBChanged();
            });

            CreateInspectorSliderRow(_lightSection.transform, "Blue (B)", out _lightBSlider, out _lightBValText, 0f, 255f, (val) =>
            {
                if (_suppressInspectorCallbacks || EditorSessionManager.SelectedObject == null) return;
                OnLightRGBChanged();
            });

            GameObject colorPresetsRow = CreateRowContainer(_lightSection.transform, "Row_ColorPresets", 26f);
            SetupRowHorizontalLayout(colorPresetsRow, 4f);

            CreateButton(colorPresetsRow.transform, "Btn_Cyan", "Cyan", 40f, () => ApplyPresetColor(Color.cyan), Color.cyan);
            CreateButton(colorPresetsRow.transform, "Btn_Sun", "Gold", 40f, () => ApplyPresetColor(new Color(1f, 0.8f, 0.35f)), new Color(1f, 0.8f, 0.35f));
            CreateButton(colorPresetsRow.transform, "Btn_Green", "Acid", 40f, () => ApplyPresetColor(new Color(0.2f, 1f, 0.4f)), new Color(0.2f, 1f, 0.4f));
            CreateButton(colorPresetsRow.transform, "Btn_Red", "Red", 40f, () => ApplyPresetColor(new Color(1f, 0.2f, 0.2f)), new Color(1f, 0.2f, 0.2f));
            CreateButton(colorPresetsRow.transform, "Btn_White", "White", 40f, () => ApplyPresetColor(Color.white), Color.white);
            CreateButton(colorPresetsRow.transform, "Btn_Violet", "Violet", 40f, () => ApplyPresetColor(new Color(0.7f, 0.3f, 1f)), new Color(0.7f, 0.3f, 1f));

            // 4. Motion Path Card
            _motionPathSection = CreateSectionCard(_inspectorContent, "MotionPath", "Kinematic Motion Path");

            _motionPathCreateBtnObj = CreateRowContainer(_motionPathSection.transform, "Row_CreatePath", 28f);
            SetupRowHorizontalLayout(_motionPathCreateBtnObj, 0f);

            CreateButton(_motionPathCreateBtnObj.transform, "Btn_CreateMotionPath", "[+ Create Motion Path]", 240f, () =>
            {
                GameObject target = EditorSessionManager.SelectedObject;
                if (target == null) return;

                if (EditorSessionManager.IsWaypointMarker(target, out GameObject owner, out _))
                    target = owner;

                Vector3 startPos = target.transform.position;
                Vector3 endPos = startPos + new Vector3(10f, 0f, 0f);

                ObjectMotionPath newPath = new ObjectMotionPath
                {
                    PointA = startPos,
                    PointB = endPos,
                    Speed = 4.0f,
                    IsActive = true
                };

                EditorSessionManager.MotionPaths[target] = newPath;

                var rb = target.GetComponent<Rigidbody>();
                if (rb == null) rb = target.AddComponent<Rigidbody>();
                rb.isKinematic = true;

                EditorSessionManager.UpdateWaypointVisuals(target, newPath);
                RefreshInspectorValues();
                EditorSessionManager.ShowNotification("Created Motion Path! Drag the orange sphere (Point B).");
            }, new Color(0.2f, 0.65f, 0.95f, 1f));

            _motionPathActiveControlsObj = new GameObject("ActiveControls", Il2CppType.Of<RectTransform>());
            _motionPathActiveControlsObj.transform.SetParent(_motionPathSection.transform, false);

            VerticalLayoutGroup mpcVlg = _motionPathActiveControlsObj.AddComponent<VerticalLayoutGroup>();
            mpcVlg.padding = new RectOffset(2, 2, 2, 2);
            mpcVlg.spacing = 4f;
            mpcVlg.childControlWidth = true;
            mpcVlg.childControlHeight = true;
            mpcVlg.childForceExpandWidth = true;
            mpcVlg.childForceExpandHeight = false;

            ContentSizeFitter mpcCsf = _motionPathActiveControlsObj.AddComponent<ContentSizeFitter>();
            mpcCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _motionPathStatusText = CreateText(_motionPathActiveControlsObj.transform, "Path Active", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 11f, FontStyles.Normal, Color.cyan, TextAlignmentOptions.MidlineLeft);
            if (_motionPathStatusText != null)
            {
                LayoutElement mple = _motionPathStatusText.gameObject.AddComponent<LayoutElement>();
                mple.preferredHeight = 18f;
            }

            // Move Speed Slider
            CreateInspectorSliderRow(_motionPathActiveControlsObj.transform, "Speed (m/s)", out _motionPathSpeedSlider, out _motionPathSpeedValText, 0.0f, 25f, (val) =>
            {
                if (_suppressInspectorCallbacks) return;
                GameObject target = EditorSessionManager.SelectedObject;
                if (target == null) return;
                if (EditorSessionManager.IsWaypointMarker(target, out GameObject owner, out _)) target = owner;

                ObjectMotionPath motionPath = null;
                if (EditorSessionManager.MotionPaths.TryGetValue(target, out motionPath) && motionPath != null)
                {
                    motionPath.Speed = val;
                    if (_motionPathSpeedValText != null) _motionPathSpeedValText.text = $"{val:F1} m/s";
                }
            });

            // Rotation Speed Slider (-150 to +150 deg/s)
            CreateInspectorSliderRow(_motionPathActiveControlsObj.transform, "Spin (deg/s)", out _motionPathRotSlider, out _motionPathRotValText, -150f, 150f, (val) =>
            {
                if (_suppressInspectorCallbacks) return;
                GameObject target = EditorSessionManager.SelectedObject;
                if (target == null) return;
                if (EditorSessionManager.IsWaypointMarker(target, out GameObject owner, out _)) target = owner;

                ObjectMotionPath motionPath = null;
                if (EditorSessionManager.MotionPaths.TryGetValue(target, out motionPath) && motionPath != null)
                {
                    motionPath.RotationSpeed = Mathf.Round(val);
                    if (_motionPathRotValText != null) _motionPathRotValText.text = $"{motionPath.RotationSpeed:F0} d/s";
                }
            });

            // Rotation Axis Toggle & Zero Reset Buttons
            GameObject rotRow = CreateRowContainer(_motionPathActiveControlsObj.transform, "Row_RotControls", 24f);
            SetupRowHorizontalLayout(rotRow, 4f);

            _motionPathAxisBtn = CreateButton(rotRow.transform, "Btn_ToggleAxis", "Axis: [Y - Turntable]", 145f, () =>
            {
                GameObject target = EditorSessionManager.SelectedObject;
                if (target == null) return;
                if (EditorSessionManager.IsWaypointMarker(target, out GameObject owner, out _)) target = owner;

                if (EditorSessionManager.MotionPaths.TryGetValue(target, out var motionPath) && motionPath != null)
                {
                    // Cycles: 0 (X), 1 (Y), 2 (Z), 3 (Path Dir), 4 (Custom Override)
                    motionPath.RotationAxis = (motionPath.RotationAxis + 1) % 5;

                    if (motionPath.RotationAxis == 0) motionPath.CustomAxis = Vector3.right;
                    else if (motionPath.RotationAxis == 1) motionPath.CustomAxis = Vector3.up;
                    else if (motionPath.RotationAxis == 2) motionPath.CustomAxis = Vector3.forward;
                    else if (motionPath.RotationAxis == 3)
                    {
                        Vector3 delta = motionPath.PointB - motionPath.PointA;
                        motionPath.CustomAxis = delta.sqrMagnitude > 0.001f ? delta.normalized : Vector3.up;
                    }

                    RefreshInspectorValues();
                    EditorSessionManager.ShowNotification($"Rotation Axis set to [{GetRotationAxisName(motionPath.RotationAxis)}]");
                }
            }, new Color(0.18f, 0.28f, 0.40f, 1f));
            _motionPathAxisBtnText = _motionPathAxisBtn.GetComponentInChildren<TMP_Text>();

            CreateButton(rotRow.transform, "Btn_ResetSpin", "Stop Spin (0)", 95f, () =>
            {
                GameObject target = EditorSessionManager.SelectedObject;
                if (target == null) return;
                if (EditorSessionManager.IsWaypointMarker(target, out GameObject owner, out _)) target = owner;

                if (EditorSessionManager.MotionPaths.TryGetValue(target, out var motionPath) && motionPath != null)
                {
                    motionPath.RotationSpeed = 0f;
                    if (_motionPathRotSlider != null) _motionPathRotSlider.value = 0f;
                    if (_motionPathRotValText != null) _motionPathRotValText.text = "0 d/s";
                    EditorSessionManager.ShowNotification("Rotation stopped.");
                }
            }, new Color(0.25f, 0.28f, 0.32f, 1f));

            // Custom 3D Axis Override Inputs (X, Y, Z)
            CreateVector3Row(_motionPathActiveControlsObj.transform, "Axis Vector", out _motionPathAxisXInput, out _motionPathAxisYInput, out _motionPathAxisZInput, OnMotionPathAxisInputChanged);

            // Quick Axis Helpers: Align to Path & Normalize
            GameObject axisHelperRow = CreateRowContainer(_motionPathActiveControlsObj.transform, "Row_AxisHelpers", 24f);
            SetupRowHorizontalLayout(axisHelperRow, 4f);

            CreateButton(axisHelperRow.transform, "Btn_AlignPath", "Align Path Dir", 120f, () =>
            {
                GameObject target = EditorSessionManager.SelectedObject;
                if (target == null) return;
                if (EditorSessionManager.IsWaypointMarker(target, out GameObject owner, out _)) target = owner;

                if (EditorSessionManager.MotionPaths.TryGetValue(target, out var mp) && mp != null)
                {
                    Vector3 delta = mp.PointB - mp.PointA;
                    if (delta.sqrMagnitude > 0.001f)
                    {
                        mp.RotationAxis = 4; // Set to Custom Override
                        mp.CustomAxis = target.transform.InverseTransformDirection(delta.normalized).normalized;
                        RefreshInspectorValues();
                        EditorSessionManager.ShowNotification($"Aligned axis to path: ({mp.CustomAxis.x:F2}, {mp.CustomAxis.y:F2}, {mp.CustomAxis.z:F2})");
                    }
                }
            }, new Color(0.18f, 0.32f, 0.45f, 1f));

            CreateButton(axisHelperRow.transform, "Btn_NormAxis", "Normalize", 115f, () =>
            {
                GameObject target = EditorSessionManager.SelectedObject;
                if (target == null) return;
                if (EditorSessionManager.IsWaypointMarker(target, out GameObject owner, out _)) target = owner;

                if (EditorSessionManager.MotionPaths.TryGetValue(target, out var mp) && mp != null)
                {
                    if (mp.CustomAxis.sqrMagnitude > 0.0001f)
                        mp.CustomAxis.Normalize();
                    else
                        mp.CustomAxis = Vector3.up;

                    mp.RotationAxis = 4;
                    RefreshInspectorValues();
                    EditorSessionManager.ShowNotification("Normalized spin axis vector.");
                }
            }, new Color(0.22f, 0.25f, 0.30f, 1f));
        }

        private static void ApplyPointBOffset(Vector3 offset)
        {
            GameObject target = EditorSessionManager.SelectedObject;
            if (target == null) return;
            if (EditorSessionManager.IsWaypointMarker(target, out GameObject owner, out _)) target = owner;

            ObjectMotionPath motionPath = null;
            if (EditorSessionManager.MotionPaths.TryGetValue(target, out motionPath) && motionPath != null)
            {
                motionPath.PointB = motionPath.PointA + offset;
                EditorSessionManager.UpdateWaypointVisuals(target, motionPath);
                RefreshInspectorValues();
                EditorSessionManager.ShowNotification($"Moved Point B by {offset}");
            }
        }

        private static void OnLightRGBChanged()
        {
            if (EditorSessionManager.SelectedObject == null) return;
            if (!EditorSessionManager.PlacedLights.TryGetValue(EditorSessionManager.SelectedObject, out var cfg)) return;

            float r = (_lightRSlider != null) ? _lightRSlider.value / 255f : cfg.Color.r;
            float g = (_lightGSlider != null) ? _lightGSlider.value / 255f : cfg.Color.g;
            float b = (_lightBSlider != null) ? _lightBSlider.value / 255f : cfg.Color.b;

            Color newCol = new Color(r, g, b, 1f);
            cfg.Color = newCol;

            if (_lightRValText != null) _lightRValText.text = Mathf.RoundToInt(_lightRSlider.value).ToString();
            if (_lightGValText != null) _lightGValText.text = Mathf.RoundToInt(_lightGSlider.value).ToString();
            if (_lightBValText != null) _lightBValText.text = Mathf.RoundToInt(_lightBSlider.value).ToString();
            if (_lightColorPreviewSwatch != null) _lightColorPreviewSwatch.color = newCol;

            EditorSessionManager.ApplyLightConfig(EditorSessionManager.SelectedObject, cfg);
        }

        private static void ApplyPresetColor(Color c)
        {
            if (EditorSessionManager.SelectedObject == null) return;
            if (EditorSessionManager.PlacedLights.TryGetValue(EditorSessionManager.SelectedObject, out var cfg))
            {
                cfg.Color = c;
                EditorSessionManager.ApplyLightConfig(EditorSessionManager.SelectedObject, cfg);

                _suppressInspectorCallbacks = true;
                if (_lightRSlider != null) { _lightRSlider.value = c.r * 255f; if (_lightRValText != null) _lightRValText.text = Mathf.RoundToInt(c.r * 255f).ToString(); }
                if (_lightGSlider != null) { _lightGSlider.value = c.g * 255f; if (_lightGValText != null) _lightGValText.text = Mathf.RoundToInt(c.g * 255f).ToString(); }
                if (_lightBSlider != null) { _lightBSlider.value = c.b * 255f; if (_lightBValText != null) _lightBValText.text = Mathf.RoundToInt(c.b * 255f).ToString(); }
                if (_lightColorPreviewSwatch != null) _lightColorPreviewSwatch.color = c;
                _suppressInspectorCallbacks = false;

                EditorSessionManager.ShowNotification("Applied light preset.");
            }
        }

        private static void ToggleInspectorExpansion()
        {
            _isInspectorExpanded = !_isInspectorExpanded;
            if (_inspectorPanelRt != null)
            {
                float w = _isInspectorExpanded ? 380f : 280f;
                _inspectorPanelRt.sizeDelta = new Vector2(w, -40f);
                _inspectorPanelRt.anchoredPosition = new Vector2(-w * 0.5f, -20f);
            }
            if (_inspectorExpandBtnText != null)
            {
                _inspectorExpandBtnText.text = _isInspectorExpanded ? "[- Slim]" : "[+ Expand]";
            }
        }

        private static void OffsetPointB(Vector3 offset)
        {
            GameObject target = EditorSessionManager.SelectedObject;
            if (target == null) return;
            if (EditorSessionManager.IsWaypointMarker(target, out GameObject owner, out _)) target = owner;

            if (EditorSessionManager.MotionPaths.TryGetValue(target, out var path))
            {
                path.PointB = path.PointA + offset;
                EditorSessionManager.UpdateWaypointVisuals(target, path);
                RefreshInspectorValues();
                EditorSessionManager.ShowNotification($"Point B offset to {offset}");
            }
        }

        // =========================================================================
        // ASSET BROWSER (BOTTOM DOCKED BETWEEN HIERARCHY & INSPECTOR)
        // =========================================================================

        private static void BuildAssetBrowserPanel()
        {
            _assetBrowserPanel = new GameObject("AssetBrowser_Panel");
            _assetBrowserPanel.transform.SetParent(_canvasRoot.transform, false);

            RectTransform abrt = _assetBrowserPanel.AddComponent<RectTransform>();
            abrt.anchorMin = new Vector2(0f, 0f);
            abrt.anchorMax = new Vector2(1f, 0f);
            abrt.pivot = new Vector2(0.5f, 0f);
            abrt.offsetMin = new Vector2(265f, 0f);
            abrt.offsetMax = new Vector2(-290f, 300f);

            Image abImg = _assetBrowserPanel.AddComponent<Image>();
            abImg.color = new Color(0.10f, 0.11f, 0.13f, 0.98f);

            // 1. Top Category Tabs
            GameObject topTabs = new GameObject("Browser_CategoryTabs");
            topTabs.transform.SetParent(_assetBrowserPanel.transform, false);
            RectTransform ttrt = topTabs.AddComponent<RectTransform>();
            ttrt.anchorMin = new Vector2(0f, 1f);
            ttrt.anchorMax = new Vector2(1f, 1f);
            ttrt.pivot = new Vector2(0.5f, 1f);
            ttrt.sizeDelta = new Vector2(0f, 30f);
            ttrt.anchoredPosition = new Vector2(0f, -2f);

            HorizontalLayoutGroup thlg = topTabs.AddComponent<HorizontalLayoutGroup>();
            thlg.padding = new RectOffset(6, 6, 2, 2);
            thlg.spacing = 4f;
            thlg.childControlWidth = true;
            thlg.childControlHeight = true;
            thlg.childForceExpandWidth = false;
            thlg.childForceExpandHeight = true;

            string[] categories = new string[] { "Architecture", "Platforms", "Gameplay", "Hazards", "All" };
            for (int i = 0; i < categories.Length; i++)
            {
                string cat = categories[i];
                CreateButton(topTabs.transform, "Tab_" + cat, cat, 100f, () =>
                {
                    _activeBrowserCategory = cat;
                    RefreshAssetBrowser();
                });
            }

            _browserSearchInput = CreateInputField(_assetBrowserPanel.transform, "BrowserSearch",
                new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-105f, -16f), new Vector2(180f, 24f),
                "Search models...", (s) => RefreshAssetBrowser());

            // 2. Secondary Size Tier & Sort Row
            GameObject sizeBar = new GameObject("Browser_SizeBar");
            sizeBar.transform.SetParent(_assetBrowserPanel.transform, false);
            RectTransform sbrt = sizeBar.AddComponent<RectTransform>();
            sbrt.anchorMin = new Vector2(0f, 1f);
            sbrt.anchorMax = new Vector2(1f, 1f);
            sbrt.pivot = new Vector2(0.5f, 1f);
            sbrt.sizeDelta = new Vector2(0f, 24f);
            sbrt.anchoredPosition = new Vector2(0f, -34f);

            HorizontalLayoutGroup shlg = sizeBar.AddComponent<HorizontalLayoutGroup>();
            shlg.padding = new RectOffset(6, 6, 2, 2);
            shlg.spacing = 4f;
            shlg.childControlWidth = true;
            shlg.childControlHeight = true;
            shlg.childForceExpandWidth = false;
            shlg.childForceExpandHeight = true;

            CreateSizeFilterButton(sizeBar.transform, "ALL SIZES", AssetSizeTier.All, 85f);
            CreateSizeFilterButton(sizeBar.transform, "SMALL <4m", AssetSizeTier.Small, 90f);
            CreateSizeFilterButton(sizeBar.transform, "MEDIUM 4-15m", AssetSizeTier.Medium, 105f);
            CreateSizeFilterButton(sizeBar.transform, "LARGE 15-45m", AssetSizeTier.Large, 105f);
            CreateSizeFilterButton(sizeBar.transform, "GIANT >45m", AssetSizeTier.Giant, 95f);

            Button sortBtn = CreateButton(sizeBar.transform, "Btn_ToggleSortOrder", "SIZE: ▲ ASC", 100f, () =>
            {
                _sortSizeAscending = !_sortSizeAscending;
                if (_sortSizeBtnText != null)
                    _sortSizeBtnText.text = _sortSizeAscending ? "SIZE: ▲ ASC" : "SIZE: ▼ DESC";
                RefreshAssetBrowser();
            }, new Color(0.18f, 0.28f, 0.40f, 1f));
            _sortSizeBtnText = sortBtn.GetComponentInChildren<TMP_Text>();

            // 3. Scrollable Grid Area
            GameObject scrollObj = new GameObject("Browser_Scroll");
            scrollObj.transform.SetParent(_assetBrowserPanel.transform, false);
            RectTransform srt = scrollObj.AddComponent<RectTransform>();
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = new Vector2(1f, 1f);
            srt.offsetMin = new Vector2(8f, 6f);
            srt.offsetMax = new Vector2(-8f, -62f);

            ScrollRect sr = scrollObj.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 30f;

            GameObject vp = new GameObject("Viewport");
            vp.transform.SetParent(scrollObj.transform, false);
            RectTransform vprt = vp.AddComponent<RectTransform>();
            vprt.anchorMin = Vector2.zero;
            vprt.anchorMax = Vector2.one;
            vprt.sizeDelta = Vector2.zero;
            vp.AddComponent<RectMask2D>();

            GameObject content = new GameObject("Content");
            content.transform.SetParent(vp.transform, false);
            _browserContent = content.AddComponent<RectTransform>();
            _browserContent.anchorMin = new Vector2(0f, 1f);
            _browserContent.anchorMax = new Vector2(1f, 1f);
            _browserContent.pivot = new Vector2(0.5f, 1f);
            _browserContent.sizeDelta = new Vector2(0f, 0f);

            GridLayoutGroup glg = content.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(100f, 106f);
            glg.spacing = new Vector2(6f, 6f);
            glg.padding = new RectOffset(8, 8, 6, 6);
            glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
            glg.startAxis = GridLayoutGroup.Axis.Horizontal;
            glg.childAlignment = TextAnchor.UpperLeft;
            glg.constraint = GridLayoutGroup.Constraint.Flexible;

            ContentSizeFitter csf = content.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vprt;
            sr.content = _browserContent;
        }

        private static void CreateSizeFilterButton(Transform parent, string label, AssetSizeTier tier, float width)
        {
            CreateButton(parent, "BtnSize_" + tier, label, width, () =>
            {
                _activeSizeFilter = tier;
                RefreshAssetBrowser();
            }, new Color(0.14f, 0.17f, 0.22f, 0.95f));
        }

        public static void RefreshAssetBrowser()
        {
            if (_browserContent == null) return;

            for (int i = 0; i < _browserCards.Count; i++)
            {
                if (_browserCards[i] != null) GameObject.Destroy(_browserCards[i]);
            }
            _browserCards.Clear();

            string search = (_browserSearchInput != null && !string.IsNullOrEmpty(_browserSearchInput.text))
                ? _browserSearchInput.text.ToLower() : "";

            // 1. calculGather all assets matching the category tab and search text
            List<CatalogAsset> matchedAssets = new List<CatalogAsset>();

            for (int i = 0; i < EditorSessionManager.AllAssets.Count; i++)
            {
                CatalogAsset asset = EditorSessionManager.AllAssets[i];
                if (asset == null || asset.SourceTemplate == null) continue;

                if (!string.IsNullOrEmpty(search) && !asset.DisplayName.ToLower().Contains(search))
                    continue;

                // Category filtering
                if (_activeBrowserCategory == "Architecture")
                {
                    bool isArch = asset.Category == AssetCategory.Building;
                    if (asset.IsJumper || asset.IsCheckPoint || asset.IsSpawnGate || asset.IsGoalGate ||
                        asset.IsLaser || asset.IsRotatingLaser || asset.IsTurret || asset.IsHelix)
                    {
                        isArch = false;
                    }
                    if (!isArch) continue;
                }
                else if (_activeBrowserCategory == "Platforms")
                {
                    bool isPlat = asset.SubCategory.Equals("Platforms", StringComparison.OrdinalIgnoreCase) ||
                                  asset.DisplayName.ToLower().Contains("platform") ||
                                  asset.DisplayName.ToLower().Contains("floor") ||
                                  asset.DisplayName.ToLower().Contains("16x16");
                    if (!isPlat) continue;
                }
                else if (_activeBrowserCategory == "Gameplay")
                {
                    bool isGame = asset.IsJumper || asset.IsCheckPoint || asset.IsSpawnGate || asset.IsGoalGate ||
                                  asset.IsSpotlight || asset.IsSunlight ||
                                  asset.SubCategory.Equals("Lighting", StringComparison.OrdinalIgnoreCase);
                    if (!isGame) continue;
                }
                else if (_activeBrowserCategory == "Hazards")
                {
                    bool isHazard = asset.IsLaser || asset.IsRotatingLaser || asset.IsTurret || asset.IsHelix ||
                                    asset.SubCategory.Equals("Hazards", StringComparison.OrdinalIgnoreCase);
                    if (!isHazard) continue;
                }

                // Size Tier filter
                if (_activeSizeFilter != AssetSizeTier.All && asset.SizeTier != _activeSizeFilter)
                    continue;

                matchedAssets.Add(asset);
            }

            // 2. Sort systematically by physical size in every tab
            matchedAssets.Sort((a, b) =>
            {
                int cmp = a.MaxDimension.CompareTo(b.MaxDimension);
                return _sortSizeAscending ? cmp : -cmp;
            });

            // 3. Render cards with color-coded size tags
            for (int i = 0; i < matchedAssets.Count; i++)
            {
                CatalogAsset asset = matchedAssets[i];

                GameObject card = new GameObject("Card_" + asset.DisplayName);
                card.transform.SetParent(_browserContent, false);
                RectTransform crt = card.AddComponent<RectTransform>();
                crt.sizeDelta = new Vector2(100f, 106f);

                Image bg = card.AddComponent<Image>();
                bg.color = new Color(0.15f, 0.17f, 0.21f, 0.95f);

                Button btn = card.AddComponent<Button>();
                CatalogAsset capturedAsset = asset;
                btn.onClick.AddListener((Action)(() => EditorSessionManager.EquipAsset(capturedAsset)));

                // Thumbnail Container
                GameObject preview = new GameObject("Thumbnail");
                preview.transform.SetParent(card.transform, false);
                RectTransform prt = preview.AddComponent<RectTransform>();
                prt.anchorMin = new Vector2(0.06f, 0.28f);
                prt.anchorMax = new Vector2(0.94f, 0.95f);
                prt.sizeDelta = Vector2.zero;
                Image pImg = preview.AddComponent<Image>();

                if (asset.ThumbnailSprite == null)
                    asset.ThumbnailSprite = AssetThumbnailRenderer.GenerateThumbnail(asset);

                if (asset.ThumbnailSprite != null)
                {
                    pImg.sprite = asset.ThumbnailSprite;
                    pImg.color = Color.white;
                }
                else
                {
                    pImg.color = asset.IsLaser ? new Color(1f, 0.2f, 0.2f) :
                                 (asset.IsSunlight ? new Color(1f, 0.85f, 0.2f) :
                                 (asset.IsSpotlight ? Color.cyan :
                                 (asset.IsJumper ? Color.green : new Color(0.25f, 0.35f, 0.45f))));
                }

                // Color-coded Size Badge Overlay (Top-Right)
                GameObject badgeObj = new GameObject("SizeBadge");
                badgeObj.transform.SetParent(card.transform, false);
                RectTransform bdrt = badgeObj.AddComponent<RectTransform>();
                bdrt.anchorMin = new Vector2(0.52f, 0.76f);
                bdrt.anchorMax = new Vector2(0.96f, 0.96f);
                bdrt.sizeDelta = Vector2.zero;

                Color badgeColor = asset.SizeTier == AssetSizeTier.Small ? new Color(0.2f, 0.85f, 0.4f, 0.85f) :
                                  (asset.SizeTier == AssetSizeTier.Medium ? new Color(0.1f, 0.7f, 1.0f, 0.85f) :
                                  (asset.SizeTier == AssetSizeTier.Large ? new Color(1.0f, 0.6f, 0.1f, 0.85f) :
                                   new Color(0.9f, 0.25f, 0.25f, 0.85f)));

                badgeObj.AddComponent<Image>().color = badgeColor;
                TMP_Text badgeTmp = CreateText(badgeObj.transform, asset.GetSizeBadgeText(),
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                    9f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
                badgeTmp.enableWordWrapping = false;

                // Title Label (Bottom)
                GameObject labelObj = new GameObject("Label");
                labelObj.transform.SetParent(card.transform, false);
                RectTransform lrt = labelObj.AddComponent<RectTransform>();
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = new Vector2(1f, 0.28f);
                lrt.offsetMin = new Vector2(3f, 2f);
                lrt.offsetMax = new Vector2(-3f, -2f);

                TMP_Text label = labelObj.AddComponent<TextMeshProUGUI>();
                label.text = asset.DisplayName;
                label.fontSize = 10f;
                label.alignment = TextAlignmentOptions.Center;
                label.color = Color.white;
                label.overflowMode = TextOverflowModes.Ellipsis;

                _browserCards.Add(card);
            }
        }
        private static void BuildToastOverlay()
        {
            GameObject toastObj = CreatePanel(_canvasRoot.transform, "Toast_Overlay", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 310f), new Vector2(480f, 26f), new Color(0.08f, 0.10f, 0.12f, 0.90f));
            _toastText = CreateText(toastObj.transform, "Studio Editor Ready", new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero, 12f, FontStyles.Bold, new Color(0.2f, 0.9f, 1.0f), TextAlignmentOptions.Center);
        }

        // =========================================================================
        // OVERHAULED INTERACTIVE HIERARCHY REFRESH
        // =========================================================================

        public static void RefreshHierarchy()
        {
            EnsureSelectableColliders();

            if (_hierarchyContent == null) return;

            _targetToRowMap.Clear();
            _rowToTargetMap.Clear();

            for (int i = 0; i < _hierarchyRows.Count; i++)
            {
                if (_hierarchyRows[i] != null) GameObject.Destroy(_hierarchyRows[i]);
            }
            _hierarchyRows.Clear();

            string search = (_hierarchySearchInput != null && !string.IsNullOrEmpty(_hierarchySearchInput.text)) ? _hierarchySearchInput.text.ToLower() : "";

            List<GameObject> rootNodes = new List<GameObject>();
            Dictionary<GameObject, List<GameObject>> childrenMap = new Dictionary<GameObject, List<GameObject>>();

            for (int i = 0; i < EditorSessionManager.PlacedObjects.Count; i++)
            {
                GameObject obj = EditorSessionManager.PlacedObjects[i];
                if (obj == null || !obj.activeSelf) continue;

                GameObject parent = (obj.transform.parent != null && EditorSessionManager.PlacedObjects.Contains(obj.transform.parent.gameObject))
                    ? obj.transform.parent.gameObject : null;

                if (parent == null)
                {
                    rootNodes.Add(obj);
                }
                else
                {
                    if (!childrenMap.ContainsKey(parent)) childrenMap[parent] = new List<GameObject>();
                    childrenMap[parent].Add(obj);
                }
            }

            for (int i = 0; i < rootNodes.Count; i++)
            {
                RenderHierarchyTreeNode(rootNodes[i], 0, childrenMap, search);
            }
        }

        private static void RenderHierarchyTreeNode(GameObject node, int depth, Dictionary<GameObject, List<GameObject>> childrenMap, string searchFilter)
        {
            if (node == null || !node.activeSelf) return;

            bool hasChildren = childrenMap.ContainsKey(node) && childrenMap[node].Count > 0;
            bool isCollapsed = _collapsedParents.Contains(node);

            bool matchesSearch = string.IsNullOrEmpty(searchFilter) || node.name.ToLower().Contains(searchFilter);
            bool childMatches = false;

            if (hasChildren && !string.IsNullOrEmpty(searchFilter))
            {
                for (int c = 0; c < childrenMap[node].Count; c++)
                {
                    if (childrenMap[node][c].name.ToLower().Contains(searchFilter)) { childMatches = true; break; }
                }
            }

            if (!matchesSearch && !childMatches) return;

            GameObject row = new GameObject($"Row_{node.name}");
            row.transform.SetParent(_hierarchyContent, false);

            RectTransform rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 26f);

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 26f;
            le.minHeight = 26f;
            le.flexibleHeight = 0f;
            le.flexibleWidth = 1f;

            _targetToRowMap[node] = row;
            _rowToTargetMap[row] = node;

            bool isDirectlySelected = (EditorSessionManager.SelectedObjects != null && EditorSessionManager.SelectedObjects.Contains(node)) ||
                                      (EditorSessionManager.SelectedObject == node);

            bool isSameTypeSibling = !isDirectlySelected &&
                                     EditorSessionManager.SelectedObject != null &&
                                     node.name == EditorSessionManager.SelectedObject.name;

            Image bg = row.AddComponent<Image>();
            bg.color = isDirectlySelected ? new Color(0.18f, 0.52f, 0.88f, 0.95f) :
                       (isSameTypeSibling ? new Color(0.06f, 0.22f, 0.44f, 0.90f) :
                       (hasChildren ? new Color(0.16f, 0.18f, 0.22f, 0.80f) : new Color(0.11f, 0.12f, 0.14f, 0.60f)));

            float leftPadding = 6f + (depth * 14f);

            // Foldout Arrow
            if (hasChildren)
            {
                GameObject foldoutBtn = new GameObject("Foldout");
                foldoutBtn.transform.SetParent(row.transform, false);
                RectTransform fbrt = foldoutBtn.AddComponent<RectTransform>();
                fbrt.anchorMin = new Vector2(0f, 0.5f);
                fbrt.anchorMax = new Vector2(0f, 0.5f);
                fbrt.pivot = new Vector2(0f, 0.5f);
                fbrt.anchoredPosition = new Vector2(leftPadding - 2f, 0f);
                fbrt.sizeDelta = new Vector2(16f, 20f);

                TMP_Text ft = foldoutBtn.AddComponent<TextMeshProUGUI>();
                ft.text = isCollapsed ? ">" : "v";
                ft.fontSize = 11f;
                ft.alignment = TextAlignmentOptions.Center;
                ft.color = new Color(0.3f, 0.85f, 1f);

                Button fb = foldoutBtn.AddComponent<Button>();
                GameObject capturedNode = node;
                fb.onClick.AddListener((Action)(() =>
                {
                    if (_collapsedParents.Contains(capturedNode)) _collapsedParents.Remove(capturedNode);
                    else _collapsedParents.Add(capturedNode);
                    RefreshHierarchy();
                }));

                leftPadding += 16f;
            }

            // Selection Button
            Button b = row.AddComponent<Button>();
            GameObject captured = node;
            b.onClick.AddListener((Action)(() =>
            {
                if (!_isDraggingHierarchyNode)
                {
                    bool isCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                    EditorSessionManager.SelectObject(captured, isAdditive: isCtrl);
                }
            }));

            string treeBranch = (depth > 0) ? "|-- " : "";
            string parentBadge = hasChildren ? $" ({childrenMap[node].Count})" : "";
            string displayName = treeBranch + captured.name + parentBadge;

            TMP_Text rowText = CreateText(row.transform, displayName,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(leftPadding, 0f), new Vector2(-45f, 0f),
                11f, hasChildren ? FontStyles.Bold : FontStyles.Normal,
                hasChildren ? new Color(0.9f, 0.95f, 1f) : Color.white,
                TextAlignmentOptions.MidlineLeft);
            rowText.enableWordWrapping = false;
            rowText.overflowMode = TextOverflowModes.Ellipsis;

            bool isChild = (node.transform.parent != null && EditorSessionManager.PlacedObjects.Contains(node.transform.parent.gameObject));

            // Focus Button [F]
            GameObject focusBtnObj = new GameObject("Btn_Focus");
            focusBtnObj.transform.SetParent(row.transform, false);
            RectTransform fcrt = focusBtnObj.AddComponent<RectTransform>();
            fcrt.anchorMin = new Vector2(1f, 0.5f);
            fcrt.anchorMax = new Vector2(1f, 0.5f);
            fcrt.pivot = new Vector2(1f, 0.5f);
            fcrt.anchoredPosition = isChild ? new Vector2(-22f, 0f) : new Vector2(-2f, 0f);
            fcrt.sizeDelta = new Vector2(18f, 18f);

            focusBtnObj.AddComponent<Image>().color = new Color(0.15f, 0.18f, 0.24f, 0.9f);
            Button fcBtn = focusBtnObj.AddComponent<Button>();
            TMP_Text fcTxt = CreateText(focusBtnObj.transform, "F", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
            fcBtn.onClick.AddListener((Action)(() =>
            {
                EditorSessionManager.SelectObject(captured);
                EditorViewportCamera.FocusOnObject(captured);
            }));

            // Quick Unparent Button [X]
            if (isChild)
            {
                GameObject unpBtnObj = new GameObject("Btn_Unparent");
                unpBtnObj.transform.SetParent(row.transform, false);
                RectTransform unprt = unpBtnObj.AddComponent<RectTransform>();
                unprt.anchorMin = new Vector2(1f, 0.5f);
                unprt.anchorMax = new Vector2(1f, 0.5f);
                unprt.pivot = new Vector2(1f, 0.5f);
                unprt.anchoredPosition = new Vector2(-2f, 0f);
                unprt.sizeDelta = new Vector2(18f, 18f);

                unpBtnObj.AddComponent<Image>().color = new Color(0.24f, 0.12f, 0.12f, 0.9f);
                Button unpBtn = unpBtnObj.AddComponent<Button>();
                TMP_Text unpTxt = CreateText(unpBtnObj.transform, "X", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Bold, new Color(1f, 0.4f, 0.4f), TextAlignmentOptions.Center);
                unpBtn.onClick.AddListener((Action)(() =>
                {
                    GameObject oldP = captured.transform.parent.gameObject;
                    captured.transform.SetParent(null, true);
                    EditorSessionManager.RecalculateParentChildCount(oldP);
                    EditorSessionManager.ShowNotification($"Unparented '{captured.name}' to root.");
                    RefreshHierarchy();
                }));
            }

            _hierarchyRows.Add(row);

            if (hasChildren && !isCollapsed)
            {
                for (int c = 0; c < childrenMap[node].Count; c++)
                {
                    RenderHierarchyTreeNode(childrenMap[node][c], depth + 1, childrenMap, searchFilter);
                }
            }
        }

        // =========================================================================
        // DRAG & DROP PARENTING UPDATE LOOP
        // =========================================================================
        public static void UpdateHierarchyDragDrop()
        {
            if (_hierarchyPanel == null || !_hierarchyPanel.activeInHierarchy || _hierarchyScrollRect == null) return;

            if (Input.GetMouseButtonDown(0))
            {
                GameObject hovered = GetHoveredHierarchyNode();
                if (hovered != null)
                {
                    _dragCandidateNode = hovered;
                    _dragStartMousePos = Input.mousePosition;
                    _isDraggingHierarchyNode = false;

                    _hierarchyScrollRect.StopMovement();
                    _hierarchyScrollRect.vertical = false;
                }
            }

            if (Input.GetMouseButton(0) && _dragCandidateNode != null)
            {
                if (_hierarchyScrollRect.vertical)
                {
                    _hierarchyScrollRect.StopMovement();
                    _hierarchyScrollRect.vertical = false;
                }

                if (!_isDraggingHierarchyNode)
                {
                    if (Vector2.Distance(Input.mousePosition, _dragStartMousePos) > 8f)
                    {
                        _isDraggingHierarchyNode = true;
                        CreateDragGhost(_dragCandidateNode);
                    }
                }

                if (_isDraggingHierarchyNode)
                {
                    UpdateDragGhostPosition();
                    UpdateHierarchyDragVisuals();
                    HandleHierarchyAutoScroll();
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                if (_hierarchyScrollRect != null)
                {
                    _hierarchyScrollRect.vertical = true;
                }

                if (_isDraggingHierarchyNode && _dragCandidateNode != null)
                {
                    GameObject dropTarget = GetHoveredHierarchyNode();

                    if (dropTarget != null)
                    {
                        if (dropTarget != _dragCandidateNode && !IsDescendantOf(_dragCandidateNode, dropTarget))
                        {
                            GameObject prevParent = _dragCandidateNode.transform.parent != null
                                ? _dragCandidateNode.transform.parent.gameObject : null;

                            _dragCandidateNode.transform.SetParent(dropTarget.transform, true);

                            if (prevParent != null) EditorSessionManager.RecalculateParentChildCount(prevParent);
                            EditorSessionManager.RecalculateParentChildCount(dropTarget);

                            EditorSessionManager.ShowNotification($"Parented '{_dragCandidateNode.name}' under '{dropTarget.name}'");
                            RefreshHierarchy();
                        }
                    }
                    else
                    {
                        if (IsMouseOverHierarchyPanel() && _dragCandidateNode.transform.parent != null)
                        {
                            GameObject oldParent = _dragCandidateNode.transform.parent.gameObject;
                            _dragCandidateNode.transform.SetParent(null, true);

                            EditorSessionManager.RecalculateParentChildCount(oldParent);
                            EditorSessionManager.ShowNotification($"Unparented '{_dragCandidateNode.name}' to root.");
                            RefreshHierarchy();
                        }
                    }

                    CleanupDragGhost();
                    UpdateHierarchyHighlightOnly();
                }

                _dragCandidateNode = null;
                _isDraggingHierarchyNode = false;
            }
        }

        private static void HandleHierarchyAutoScroll()
        {
            if (_hierarchyScrollRect == null || _hierarchyScrollRect.viewport == null) return;

            RectTransform vp = _hierarchyScrollRect.viewport;
            Vector2 localPoint;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(vp, Input.mousePosition, null, out localPoint))
            {
                float halfH = vp.rect.height * 0.5f;
                float topThreshold = halfH - 25f;
                float bottomThreshold = -halfH + 25f;

                if (localPoint.y > topThreshold)
                {
                    float factor = Mathf.InverseLerp(topThreshold, halfH, localPoint.y);
                    _hierarchyScrollRect.verticalNormalizedPosition += factor * 2.2f * Time.deltaTime;
                    _hierarchyScrollRect.verticalNormalizedPosition = Mathf.Clamp01(_hierarchyScrollRect.verticalNormalizedPosition);
                }
                else if (localPoint.y < bottomThreshold)
                {
                    float factor = Mathf.InverseLerp(bottomThreshold, -halfH, localPoint.y);
                    _hierarchyScrollRect.verticalNormalizedPosition -= factor * 2.2f * Time.deltaTime;
                    _hierarchyScrollRect.verticalNormalizedPosition = Mathf.Clamp01(_hierarchyScrollRect.verticalNormalizedPosition);
                }
            }
        }

        private static GameObject GetHoveredHierarchyNode()
        {
            if (_rowToTargetMap == null || _rowToTargetMap.Count == 0) return null;
            Vector2 mousePos = Input.mousePosition;

            foreach (var kvp in _rowToTargetMap)
            {
                GameObject row = kvp.Key;
                GameObject target = kvp.Value;
                if (row == null || target == null || !row.activeInHierarchy) continue;

                RectTransform rt = row.GetComponent<RectTransform>();
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, null))
                {
                    return target;
                }
            }

            return null;
        }

        private static bool IsMouseOverHierarchyPanel()
        {
            if (_hierarchyPanel == null) return false;
            RectTransform rt = _hierarchyPanel.GetComponent<RectTransform>();
            return rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null);
        }

        private static bool IsDescendantOf(GameObject parentNode, GameObject potentialChild)
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

        private static void CreateDragGhost(GameObject node)
        {
            CleanupDragGhost();
            if (node == null || _canvasRoot == null) return;

            _dragGhostObj = new GameObject("Hierarchy_Drag_Ghost");
            _dragGhostObj.transform.SetParent(_canvasRoot.transform, false);

            RectTransform rt = _dragGhostObj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(190f, 24f);
            rt.pivot = new Vector2(0f, 1f);

            Image bg = _dragGhostObj.AddComponent<Image>();
            bg.color = new Color(0.95f, 0.65f, 0.15f, 0.90f);
            bg.raycastTarget = false;

            _dragGhostText = CreateText(_dragGhostObj.transform, "[Moving] " + node.name,
                Vector2.zero, Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, 0f),
                10f, FontStyles.Bold, Color.black, TextAlignmentOptions.MidlineLeft);
            _dragGhostText.raycastTarget = false;

            UpdateDragGhostPosition();
        }

        private static void UpdateDragGhostPosition()
        {
            if (_dragGhostObj == null) return;
            RectTransform rt = _dragGhostObj.GetComponent<RectTransform>();
            Vector2 mousePos = Input.mousePosition;
            rt.position = new Vector3(mousePos.x + 14f, mousePos.y - 10f, 0f);
        }

        private static void CleanupDragGhost()
        {
            if (_dragGhostObj != null)
            {
                GameObject.DestroyImmediate(_dragGhostObj);
                _dragGhostObj = null;
                _dragGhostText = null;
            }
        }

        private static void UpdateHierarchyDragVisuals()
        {
            if (_targetToRowMap == null || _targetToRowMap.Count == 0) return;

            GameObject dropTarget = GetHoveredHierarchyNode();

            foreach (var kvp in _targetToRowMap)
            {
                GameObject target = kvp.Key;
                GameObject row = kvp.Value;
                if (target == null || row == null) continue;

                Image bg = row.GetComponent<Image>();
                if (bg == null) continue;

                if (target == dropTarget)
                {
                    if (dropTarget == _dragCandidateNode || IsDescendantOf(_dragCandidateNode, dropTarget))
                    {
                        bg.color = new Color(0.75f, 0.2f, 0.2f, 0.90f);
                        if (_dragGhostText != null) _dragGhostText.text = "[Invalid] Child Loop";
                    }
                    else
                    {
                        bg.color = new Color(0.95f, 0.65f, 0.15f, 0.95f);
                        if (_dragGhostText != null) _dragGhostText.text = $"[Parent] {dropTarget.name}";
                    }
                }
                else
                {
                    bool isDirectlySelected = (EditorSessionManager.SelectedObjects != null && EditorSessionManager.SelectedObjects.Contains(target));
                    bg.color = isDirectlySelected
                        ? new Color(0.18f, 0.52f, 0.88f, 0.95f)
                        : new Color(0.11f, 0.12f, 0.14f, 0.60f);
                }
            }

            if (dropTarget == null && IsMouseOverHierarchyPanel() && _dragGhostText != null)
            {
                _dragGhostText.text = "[Release to Unparent] (Root)";
            }
        }

        public static void NotifyObjectSelected(GameObject obj)
        {
            if (_inspectorTitleText != null)
            {
                if (EditorSessionManager.SelectedObjects != null && EditorSessionManager.SelectedObjects.Count > 1)
                {
                    _inspectorTitleText.text = $"Selection ({EditorSessionManager.SelectedObjects.Count} Objects)";
                }
                else
                {
                    _inspectorTitleText.text = (obj != null) ? obj.name : "Inspector (None Selected)";
                }
            }

            UpdateHierarchyHighlightOnly();
            RefreshInspectorValues();
        }

        private static void UpdateHierarchyHighlightOnly()
        {
            if (_targetToRowMap == null || _targetToRowMap.Count == 0) return;

            GameObject primary = EditorSessionManager.SelectedObject;
            string primaryName = (primary != null) ? primary.name : null;

            foreach (var kvp in _targetToRowMap)
            {
                GameObject target = kvp.Key;
                GameObject row = kvp.Value;
                if (target == null || row == null) continue;

                Image bg = row.GetComponent<Image>();
                if (bg == null) continue;

                bool isDirectlySelected = (EditorSessionManager.SelectedObjects != null && EditorSessionManager.SelectedObjects.Contains(target)) ||
                                          (target == primary);

                bool isSameTypeSibling = !isDirectlySelected && primaryName != null && target.name == primaryName;

                if (isDirectlySelected)
                {
                    bg.color = new Color(0.18f, 0.52f, 0.88f, 0.95f);
                }
                else if (isSameTypeSibling)
                {
                    bg.color = new Color(0.06f, 0.22f, 0.44f, 0.90f);
                }
                else
                {
                    int childCount = 0;
                    EditorSessionManager.PlacedParentChildCounts.TryGetValue(target, out childCount);
                    bg.color = (childCount > 0) ? new Color(0.16f, 0.18f, 0.22f, 0.80f) : new Color(0.11f, 0.12f, 0.14f, 0.60f);
                }
            }
        }

        // =========================================================================
        // INSPECTOR VALUES REFRESH
        // =========================================================================

        public static void RefreshInspectorValues()
        {
            GameObject obj = EditorSessionManager.SelectedObject;
            if (obj == null || !obj.activeSelf)
            {
                if (_posXInput != null) _posXInput.text = "0";
                if (_posYInput != null) _posYInput.text = "0";
                if (_posZInput != null) _posZInput.text = "0";
                if (_rotXInput != null) _rotXInput.text = "0";
                if (_rotYInput != null) _rotYInput.text = "0";
                if (_rotZInput != null) _rotZInput.text = "0";
                if (_scaleXInput != null) _scaleXInput.text = "1";
                if (_scaleYInput != null) _scaleYInput.text = "1";
                if (_scaleZInput != null) _scaleZInput.text = "1";

                if (_jumperSection != null) _jumperSection.SetActive(false);
                if (_turbineSection != null) _turbineSection.SetActive(false);
                if (_turretSection != null) _turretSection.SetActive(false);
                if (_laserSection != null) _laserSection.SetActive(false);
                if (_lightSection != null) _lightSection.SetActive(false);
                if (_motionPathSection != null) _motionPathSection.SetActive(false);
                if (_motionPathAxisXInput != null) _motionPathAxisXInput.text = "0";
                if (_motionPathAxisYInput != null) _motionPathAxisYInput.text = "1";
                if (_motionPathAxisZInput != null) _motionPathAxisZInput.text = "0";
                return;
            }

            _suppressInspectorCallbacks = true;

            Vector3 pos = obj.transform.position;
            Vector3 rot = obj.transform.eulerAngles;
            Vector3 scl = obj.transform.localScale;

            if (_posXInput != null) _posXInput.text = pos.x.ToString("F2", CultureInfo.InvariantCulture);
            if (_posYInput != null) _posYInput.text = pos.y.ToString("F2", CultureInfo.InvariantCulture);
            if (_posZInput != null) _posZInput.text = pos.z.ToString("F2", CultureInfo.InvariantCulture);

            if (_rotXInput != null) _rotXInput.text = rot.x.ToString("F1", CultureInfo.InvariantCulture);
            if (_rotYInput != null) _rotYInput.text = rot.y.ToString("F1", CultureInfo.InvariantCulture);
            if (_rotZInput != null) _rotZInput.text = rot.z.ToString("F1", CultureInfo.InvariantCulture);

            if (_scaleXInput != null) _scaleXInput.text = scl.x.ToString("F2", CultureInfo.InvariantCulture);
            if (_scaleYInput != null) _scaleYInput.text = scl.y.ToString("F2", CultureInfo.InvariantCulture);
            if (_scaleZInput != null) _scaleZInput.text = scl.z.ToString("F2", CultureInfo.InvariantCulture);

            EditorSessionManager.PlacedObjectTypes.TryGetValue(obj, out PlacedObjectType type);

            if (_jumperSection != null)
            {
                bool isJ = (type == PlacedObjectType.Jumper);
                _jumperSection.SetActive(isJ);
                if (isJ && _jumperSlider != null)
                {
                    float force = EditorSessionManager.JumperForces.ContainsKey(obj) ? EditorSessionManager.JumperForces[obj] : EditorSessionManager.ActiveJumperForce;
                    _jumperSlider.value = force;
                    if (_jumperValueText != null) _jumperValueText.text = $"{force:F1}";
                }
            }

            if (_turbineSection != null)
            {
                bool isT = (type == PlacedObjectType.Turbine);
                _turbineSection.SetActive(isT);
                if (isT && _turbineSlider != null)
                {
                    float speed = EditorSessionManager.TurbineSpeeds.ContainsKey(obj) ? EditorSessionManager.TurbineSpeeds[obj] : EditorSessionManager.ActiveTurbineSpeed;
                    _turbineSlider.value = speed;
                    if (_turbineValueText != null) _turbineValueText.text = $"{speed:F1}";
                }
            }

            if (_turretSection != null)
            {
                bool isTur = (type == PlacedObjectType.Turret);
                _turretSection.SetActive(isTur);
                if (isTur && _turretSlider != null)
                {
                    float delay = EditorSessionManager.TurretFireDelays.ContainsKey(obj) ? EditorSessionManager.TurretFireDelays[obj] : EditorSessionManager.ActiveTurretFireDelay;
                    _turretSlider.value = delay;
                    if (_turretValueText != null) _turretValueText.text = $"{delay:F2}s";
                }
            }

            if (_laserSection != null)
            {
                bool isRotLaser = (type == PlacedObjectType.RotatingLaser);
                _laserSection.SetActive(isRotLaser);
                if (isRotLaser && _laserSlider != null)
                {
                    float speed = EditorSessionManager.LaserRotationSpeeds.ContainsKey(obj) ? EditorSessionManager.LaserRotationSpeeds[obj] : EditorSessionManager.ActiveLaserRotationSpeed;
                    _laserSlider.value = speed;
                    if (_laserValueText != null) _laserValueText.text = $"{speed:F0} d/s";
                }
            }

            if (_lightSection != null)
            {
                bool isLight = (type == PlacedObjectType.Spotlight || type == PlacedObjectType.Sunlight) || EditorSessionManager.PlacedLights.ContainsKey(obj);
                _lightSection.SetActive(isLight);

                if (isLight && EditorSessionManager.PlacedLights.TryGetValue(obj, out var cfg))
                {
                    bool isSpot = !cfg.IsDirectional;
                    if (_lightTypeBadgeText != null)
                    {
                        _lightTypeBadgeText.text = isSpot ? "Type: [Tech Spotlight]" : "Type: [Global Sun]";
                    }
                    if (_spotAngleRowObj != null)
                    {
                        _spotAngleRowObj.SetActive(isSpot);
                    }

                    if (_lightIntensitySlider != null) { _lightIntensitySlider.value = cfg.Intensity; _lightIntensityValText.text = $"{cfg.Intensity:F1}"; }
                    if (_lightAngleSlider != null) { _lightAngleSlider.value = cfg.SpotAngle; _lightAngleValText.text = $"{cfg.SpotAngle:F0} deg"; }
                    if (_lightVolSlider != null) { _lightVolSlider.value = cfg.VolumetricIntensity; _lightVolValText.text = $"{cfg.VolumetricIntensity:F1}"; }

                    if (_lightRSlider != null) { _lightRSlider.value = cfg.Color.r * 255f; if (_lightRValText != null) _lightRValText.text = Mathf.RoundToInt(cfg.Color.r * 255f).ToString(); }
                    if (_lightGSlider != null) { _lightGSlider.value = cfg.Color.g * 255f; if (_lightGValText != null) _lightGValText.text = Mathf.RoundToInt(cfg.Color.g * 255f).ToString(); }
                    if (_lightBSlider != null) { _lightBSlider.value = cfg.Color.b * 255f; if (_lightBValText != null) _lightBValText.text = Mathf.RoundToInt(cfg.Color.b * 255f).ToString(); }

                    if (_lightColorPreviewSwatch != null) { _lightColorPreviewSwatch.color = cfg.Color; }
                }
            }

            if (_motionPathSection != null)
            {
                GameObject pathOwner = obj;
                if (EditorSessionManager.IsWaypointMarker(obj, out GameObject resolvedOwner, out _))
                {
                    pathOwner = resolvedOwner;
                }

                ObjectMotionPath motionPath = null;
                bool hasPath = false;
                if (pathOwner != null)
                {
                    hasPath = EditorSessionManager.MotionPaths.TryGetValue(pathOwner, out motionPath);
                }

                _motionPathSection.SetActive(hasPath || pathOwner == obj);

                if (_motionPathCreateBtnObj != null) _motionPathCreateBtnObj.SetActive(!hasPath);
                if (_motionPathActiveControlsObj != null) _motionPathActiveControlsObj.SetActive(hasPath);

                if (hasPath && motionPath != null)
                {
                    float dist = motionPath.TotalDistance;
                    if (_motionPathStatusText != null)
                        _motionPathStatusText.text = $"Distance: {dist:F1}m ({motionPath.Speed:F1} m/s)";

                    if (_motionPathSpeedSlider != null)
                    {
                        _motionPathSpeedSlider.value = motionPath.Speed;
                        if (_motionPathSpeedValText != null) _motionPathSpeedValText.text = $"{motionPath.Speed:F1} m/s";
                    }

                    if (_motionPathRotSlider != null)
                    {
                        _motionPathRotSlider.value = motionPath.RotationSpeed;
                        if (_motionPathRotValText != null) _motionPathRotValText.text = $"{motionPath.RotationSpeed:F0} d/s";
                    }

                    if (_motionPathAxisBtnText != null)
                    {
                        _motionPathAxisBtnText.text = $"Axis: [{GetRotationAxisName(motionPath.RotationAxis)}]";
                    }

                    Vector3 effAxis = (motionPath.RotationAxis == 4)
                        ? motionPath.CustomAxis
                        : motionPath.GetEffectiveLocalAxis(pathOwner != null ? pathOwner.transform : null);

                    if (_motionPathAxisXInput != null) _motionPathAxisXInput.text = effAxis.x.ToString("F2", CultureInfo.InvariantCulture);
                    if (_motionPathAxisYInput != null) _motionPathAxisYInput.text = effAxis.y.ToString("F2", CultureInfo.InvariantCulture);
                    if (_motionPathAxisZInput != null) _motionPathAxisZInput.text = effAxis.z.ToString("F2", CultureInfo.InvariantCulture);
                }
            }

            _suppressInspectorCallbacks = false;
        }

        private static void OnTransformInputChanged(string val)
        {
            if (_suppressInspectorCallbacks || EditorSessionManager.SelectedObject == null) return;

            GameObject obj = EditorSessionManager.SelectedObject;
            float x = ParseFloat(_posXInput?.text, obj.transform.position.x);
            float y = ParseFloat(_posYInput?.text, obj.transform.position.y);
            float z = ParseFloat(_posZInput?.text, obj.transform.position.z);

            float rx = ParseFloat(_rotXInput?.text, obj.transform.eulerAngles.x);
            float ry = ParseFloat(_rotYInput?.text, obj.transform.eulerAngles.y);
            float rz = ParseFloat(_rotZInput?.text, obj.transform.eulerAngles.z);

            // Read X, Y, and Z scales independently
            float sx = ParseFloat(_scaleXInput?.text, obj.transform.localScale.x);
            float sy = ParseFloat(_scaleYInput?.text, obj.transform.localScale.y);
            float sz = ParseFloat(_scaleZInput?.text, obj.transform.localScale.z);

            obj.transform.position = new Vector3(x, y, z);
            obj.transform.rotation = Quaternion.Euler(rx, ry, rz);
            obj.transform.localScale = new Vector3(Mathf.Max(0.01f, sx), Mathf.Max(0.01f, sy), Mathf.Max(0.01f, sz));

            StudioGizmoController.InvalidateCachedCenter(obj);
            EditorSessionManager.UpdateSelectionHighlight();
        }

        private static float ParseFloat(string str, float def)
        {
            if (string.IsNullOrWhiteSpace(str)) return def;
            str = str.Trim().Replace(',', '.');
            if (float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                return result;
            return def;
        }

        // =========================================================================
        // UGUI FACTORY HELPERS
        // =========================================================================

        private static GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Color color)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            Image img = obj.AddComponent<Image>();
            img.color = color;

            return obj;
        }

        private static TMP_Text CreateText(Transform parent, string text, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, float fontSize, FontStyles style, Color color, TextAlignmentOptions align)
        {
            GameObject obj = new GameObject("Text");
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            TMP_Text tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = align;

            return tmp;
        }

        private static Button CreateButton(Transform parent, string name, string label, float width, Action onClick, Color? bgColor = null, Vector2? fixedPos = null)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.AddComponent<RectTransform>();
            if (fixedPos.HasValue)
            {
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 0f);
                rt.anchoredPosition = fixedPos.Value;
                rt.sizeDelta = new Vector2(width, 26f);
            }
            else
            {
                rt.sizeDelta = new Vector2(width, 26f);

                LayoutElement le = obj.AddComponent<LayoutElement>();
                le.preferredWidth = width;
                le.preferredHeight = 26f;
                le.minWidth = width > 0 ? Mathf.Min(width, 40f) : 0f;
                le.minHeight = 22f;
                le.flexibleWidth = 1f;
            }

            Image img = obj.AddComponent<Image>();
            img.color = bgColor ?? new Color(0.20f, 0.22f, 0.26f, 1f);

            Button btn = obj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            cb.pressedColor = new Color(0.2f, 0.7f, 1.0f, 1f);
            btn.colors = cb;

            btn.onClick.AddListener((Action)(() => onClick?.Invoke()));

            TMP_Text btnText = CreateText(obj.transform, label, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 11f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
            btnText.enableWordWrapping = false;
            btnText.overflowMode = TextOverflowModes.Ellipsis;

            return btn;
        }

        private static TMP_InputField CreateInputField(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, string placeholder, Action<string> onEndEdit)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            Image img = obj.AddComponent<Image>();
            img.color = new Color(0.08f, 0.09f, 0.11f, 0.95f);

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(obj.transform, false);
            RectTransform trt = textObj.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(5f, 2f);
            trt.offsetMax = new Vector2(-5f, -2f);

            TMP_Text inTmp = textObj.AddComponent<TextMeshProUGUI>();
            inTmp.fontSize = 11f;
            inTmp.color = Color.white;
            inTmp.alignment = TextAlignmentOptions.MidlineLeft;

            TMP_InputField inputField = obj.AddComponent<TMP_InputField>();
            inputField.textViewport = trt;
            inputField.textComponent = inTmp;
            inputField.onEndEdit.AddListener((Action<string>)((val) => onEndEdit?.Invoke(val)));

            return inputField;
        }

        private static GameObject CreateSectionCard(Transform parent, string name, string title)
        {
            GameObject card = new GameObject("Card_" + name);
            card.transform.SetParent(parent, false);

            RectTransform crt = card.AddComponent<RectTransform>();
            crt.sizeDelta = new Vector2(0f, 0f);

            Image bg = card.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.16f, 0.19f, 0.95f);

            VerticalLayoutGroup vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.spacing = 5f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            ContentSizeFitter csf = card.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            LayoutElement le = card.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;

            GameObject header = new GameObject("Header");
            header.transform.SetParent(card.transform, false);
            LayoutElement hle = header.AddComponent<LayoutElement>();
            hle.preferredHeight = 22f;
            hle.minHeight = 22f;

            Image hbg = header.AddComponent<Image>();
            hbg.color = new Color(0.20f, 0.22f, 0.27f, 0.95f);

            CreateText(header.transform, title, Vector2.zero, Vector2.one, new Vector2(6f, 0f), new Vector2(-6f, 0f), 11f, FontStyles.Bold, new Color(0.25f, 0.85f, 1f), TextAlignmentOptions.MidlineLeft);

            return card;
        }

        private static GameObject CreateRowContainer(Transform parent, string name, float height)
        {
            GameObject row = new GameObject(name);
            row.transform.SetParent(parent, false);

            RectTransform rt = row.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(0f, height);

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            le.flexibleWidth = 1f;

            return row;
        }

        private static HorizontalLayoutGroup SetupRowHorizontalLayout(GameObject row, float spacing = 6f)
        {
            HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null) hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(2, 2, 2, 2);
            hlg.spacing = spacing;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            return hlg;
        }

        private static void CreateVector3Row(Transform parent, string label, out TMP_InputField xIn, out TMP_InputField yIn, out TMP_InputField zIn, Action<string> onChange)
        {
            GameObject row = CreateRowContainer(parent, "Row_" + label, 24f);

            CreateText(row.transform, label, new Vector2(0f, 0.5f), new Vector2(0.22f, 0.5f), Vector2.zero, Vector2.zero, 11f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);

            xIn = CreateInputField(row.transform, "X", new Vector2(0.24f, 0f), new Vector2(0.48f, 1f), Vector2.zero, Vector2.zero, "X", onChange);
            yIn = CreateInputField(row.transform, "Y", new Vector2(0.50f, 0f), new Vector2(0.74f, 1f), Vector2.zero, Vector2.zero, "Y", onChange);
            zIn = CreateInputField(row.transform, "Z", new Vector2(0.76f, 0f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero, "Z", onChange);
        }

        private static void CreateSingleFloatRow(Transform parent, string label, out TMP_InputField valIn, Action<string> onChange)
        {
            GameObject row = CreateRowContainer(parent, "Row_" + label, 24f);

            CreateText(row.transform, label, new Vector2(0f, 0.5f), new Vector2(0.22f, 0.5f), Vector2.zero, Vector2.zero, 11f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);
            valIn = CreateInputField(row.transform, "Val", new Vector2(0.24f, 0f), new Vector2(0.98f, 1f), Vector2.zero, Vector2.zero, "Value", onChange);
        }

        private static GameObject CreateInspectorSliderRow(Transform parent, string label, out Slider slider, out TMP_Text valText, float minVal, float maxVal, Action<float> onSliderChanged)
        {
            GameObject row = CreateRowContainer(parent, "Row_Slider_" + label, 24f);

            CreateText(row.transform, label, new Vector2(0f, 0f), new Vector2(0.32f, 1f), new Vector2(4f, 0f), Vector2.zero, 10f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);
            valText = CreateText(row.transform, "0", new Vector2(0.80f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-4f, 0f), 10f, FontStyles.Bold, new Color(0.2f, 0.85f, 1f), TextAlignmentOptions.MidlineRight);

            GameObject sliderObj = new GameObject("Slider");
            sliderObj.transform.SetParent(row.transform, false);

            RectTransform srt = sliderObj.AddComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.33f, 0f);
            srt.anchorMax = new Vector2(0.78f, 1f);
            srt.offsetMin = new Vector2(0f, 4f);
            srt.offsetMax = new Vector2(0f, -4f);
            srt.sizeDelta = Vector2.zero;

            slider = sliderObj.AddComponent<Slider>();
            slider.minValue = minVal;
            slider.maxValue = maxVal;
            slider.direction = Slider.Direction.LeftToRight;

            GameObject trackObj = new GameObject("Track");
            trackObj.transform.SetParent(sliderObj.transform, false);
            RectTransform trt = trackObj.AddComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 0.35f);
            trt.anchorMax = new Vector2(1f, 0.65f);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            trt.sizeDelta = Vector2.zero;
            Image trackImg = trackObj.AddComponent<Image>();
            trackImg.color = new Color(0.08f, 0.09f, 0.12f, 1f);
            trackImg.raycastTarget = false;

            GameObject fillArea = new GameObject("FillArea");
            fillArea.transform.SetParent(sliderObj.transform, false);
            RectTransform fart = fillArea.AddComponent<RectTransform>();
            fart.anchorMin = new Vector2(0f, 0.35f);
            fart.anchorMax = new Vector2(1f, 0.65f);
            fart.offsetMin = new Vector2(2f, 0f);
            fart.offsetMax = new Vector2(-2f, 0f);
            fart.sizeDelta = Vector2.zero;

            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(fillArea.transform, false);
            RectTransform frt = fillObj.AddComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            frt.sizeDelta = Vector2.zero;
            Image fillImg = fillObj.AddComponent<Image>();
            fillImg.color = new Color(0.18f, 0.65f, 0.95f, 0.9f);
            fillImg.raycastTarget = false;

            GameObject handleArea = new GameObject("HandleArea");
            handleArea.transform.SetParent(sliderObj.transform, false);
            RectTransform hart = handleArea.AddComponent<RectTransform>();
            hart.anchorMin = Vector2.zero;
            hart.anchorMax = Vector2.one;
            hart.offsetMin = new Vector2(6f, 0f);
            hart.offsetMax = new Vector2(-6f, 0f);
            hart.sizeDelta = Vector2.zero;

            GameObject handleObj = new GameObject("Handle");
            handleObj.transform.SetParent(handleArea.transform, false);
            RectTransform hrt = handleObj.AddComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0.5f, 0.5f);
            hrt.anchorMax = new Vector2(0.5f, 0.5f);
            hrt.sizeDelta = new Vector2(10f, 16f);
            Image handleImg = handleObj.AddComponent<Image>();
            handleImg.color = Color.white;
            handleImg.raycastTarget = true;

            slider.fillRect = frt;
            slider.handleRect = hrt;
            slider.targetGraphic = handleImg;

            slider.onValueChanged.AddListener((Action<float>)((v) => onSliderChanged?.Invoke(v)));
            return row;
        }

        private static GameObject CreateScrollView(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, out RectTransform content)
        {
            GameObject scrollObj = new GameObject(name);
            scrollObj.transform.SetParent(parent, false);

            RectTransform rt = scrollObj.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            ScrollRect sr = scrollObj.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 25f;

            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollObj.transform, false);
            RectTransform vrt = viewport.AddComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.sizeDelta = Vector2.zero;
            viewport.AddComponent<RectMask2D>();

            GameObject cObj = new GameObject("Content");
            cObj.transform.SetParent(viewport.transform, false);
            content = cObj.AddComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;

            VerticalLayoutGroup vlg = cObj.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 4f;

            ContentSizeFitter csf = cObj.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vrt;
            sr.content = content;

            return scrollObj;
        }
    }

    // =========================================================================
    // SECTION 2: NATIVE LOGS MENU HIJACKER & THUMBNAIL TEXTURE HOOK
    // =========================================================================

    public static class NativeLogsMenuHijacker
    {
        private static GameObject _nativePlayButton = null;
        private static GameObject _nativeCreateButton = null;
        private static GameObject _nativeDeleteButton = null;
        private static readonly List<GameObject> _spawnedRowObjects = new List<GameObject>();
        public static int SpawnedRowCount => _spawnedRowObjects.Count;

        private static Toggle _cachedMyLevelsToggle = null;
        private static Toggle _cachedCommunityToggle = null;

        private static GameObject _nativeMetadataRoot = null;
        private static TMP_InputField _titleInput = null;
        private static TMP_InputField _authorInput = null;
        private static TMP_InputField _descInput = null;
        private static TMP_Text _sceneLabelText = null;
        private static readonly List<GameObject> _diffButtons = new List<GameObject>();
        private static int _selectedDifficultyIndex = 2;

        private static TMP_Text _statsLabelLeft = null;
        private static TMP_Text _statsLabelRight = null;
        private static TMP_Text _saveBtnText = null;
        private static float _saveFeedbackTimer = 0f;

        public static readonly string[] DifficultyNames = new string[] { "Very Easy", "Easy", "Normal", "Hard", "Expert" };
        public static readonly Color[] DifficultyColors = new Color[]
        {
            new Color(0.2f, 0.95f, 0.4f),
            new Color(0.1f, 0.85f, 1.0f),
            new Color(0.3f, 0.65f, 1.0f),
            new Color(1.0f, 0.55f, 0.1f),
            new Color(0.95f, 0.2f, 0.2f)
        };

        public static LevelMetadata ReadLevelMetadata(string fullPath, string fallbackTitle)
        {
            LevelMetadata meta = new LevelMetadata();
            meta.Title = fallbackTitle;

            if (!File.Exists(fullPath)) return meta;

            try
            {
                string[] lines = File.ReadAllLines(fullPath);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string trimmed = line.Trim();

                    if (trimmed.StartsWith("#TITLE:", StringComparison.OrdinalIgnoreCase))
                        meta.Title = trimmed.Substring(7).Trim();
                    else if (trimmed.StartsWith("#AUTHOR:", StringComparison.OrdinalIgnoreCase))
                        meta.Author = trimmed.Substring(8).Trim();
                    else if (trimmed.StartsWith("#DIFFICULTY:", StringComparison.OrdinalIgnoreCase))
                        meta.Difficulty = trimmed.Substring(12).Trim();
                    else if (trimmed.StartsWith("#DESC:", StringComparison.OrdinalIgnoreCase))
                        meta.Description = trimmed.Substring(6).Trim();
                    else if (trimmed.StartsWith("#SCENE:", StringComparison.OrdinalIgnoreCase))
                        meta.StagingScene = trimmed.Substring(7).Trim();
                    else if (!trimmed.StartsWith("#"))
                        break;
                }
            }
            catch { }

            return meta;
        }

        public static void SaveCurrentMetadata(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath)) return;

            try
            {
                string title = (_titleInput != null && !string.IsNullOrWhiteSpace(_titleInput.text))
                    ? _titleInput.text.Trim()
                    : Path.GetFileNameWithoutExtension(fullPath);

                string author = (_authorInput != null && !string.IsNullOrWhiteSpace(_authorInput.text))
                    ? _authorInput.text.Trim()
                    : "Unknown";

                string desc = (_descInput != null) ? _descInput.text.Trim() : "";
                string diff = DifficultyNames[_selectedDifficultyIndex];
                string scene = !string.IsNullOrEmpty(MapBrowserService.SelectedStagingScene) ? MapBrowserService.SelectedStagingScene : "level01_Spark01";

                string[] allLines = File.ReadAllLines(fullPath);
                List<string> objectLines = new List<string>();

                for (int i = 0; i < allLines.Length; i++)
                {
                    string l = allLines[i];
                    if (string.IsNullOrWhiteSpace(l)) continue;
                    string t = l.Trim();
                    if (!t.StartsWith("#"))
                        objectLines.Add(l);
                }

                List<string> finalLines = new List<string>();
                finalLines.Add($"#TITLE: {title}");
                finalLines.Add($"#AUTHOR: {author}");
                finalLines.Add($"#DIFFICULTY: {diff}");
                finalLines.Add($"#DESC: {desc}");
                finalLines.Add($"#SCENE: {scene}");
                finalLines.AddRange(objectLines);

                File.WriteAllLines(fullPath, finalLines.ToArray());
                MelonLogger.Msg($">> [Metadata] Saved '{title}' by '{author}' ({objectLines.Count} objects) directly to '{fullPath}'!");

                if (_saveBtnText != null)
                {
                    _saveBtnText.text = "SAVED!";
                    _saveBtnText.color = new Color(0.3f, 1f, 0.5f);
                    _saveFeedbackTimer = 1.5f;
                }

                UpdateLevelStatsHUD(fullPath, objectLines.Count);
                RefreshRowTitlesInList(title);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to save metadata: {ex.Message}");
            }
        }

        public static void CreateSceneSelectorRow(Transform parent, TMP_Text sampleTmp, float posY)
        {
            GameObject row = new GameObject("Row_SceneSelector");
            row.transform.SetParent(parent, false);

            RectTransform rowRt = row.AddComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0f, 1f);
            rowRt.anchorMax = new Vector2(1f, 1f);
            rowRt.pivot = new Vector2(0.5f, 1f);
            rowRt.sizeDelta = new Vector2(0f, 38f);
            rowRt.anchoredPosition = new Vector2(0f, posY);

            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(row.transform, false);
            RectTransform labelRt = labelObj.AddComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0f, 0.5f);
            labelRt.anchorMax = new Vector2(0.25f, 0.5f);
            labelRt.sizeDelta = Vector2.zero;

            TMP_Text labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
            if (sampleTmp != null) { labelTmp.font = sampleTmp.font; labelTmp.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
            labelTmp.fontSize = 20f;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.color = new Color(0.2f, 0.82f, 1f, 1f);
            labelTmp.text = "BASE SCENE";
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;

            GameObject prevBtn = new GameObject("Btn_PrevScene");
            prevBtn.transform.SetParent(row.transform, false);
            RectTransform prevRt = prevBtn.AddComponent<RectTransform>();
            prevRt.anchorMin = new Vector2(0.26f, 0f);
            prevRt.anchorMax = new Vector2(0.34f, 1f);
            prevRt.sizeDelta = Vector2.zero;
            prevBtn.AddComponent<Image>().color = new Color(0.1f, 0.15f, 0.25f, 0.9f);
            Button pb = prevBtn.AddComponent<Button>();
            pb.onClick.AddListener((Action)(() => CycleStagingScene(-1)));

            GameObject prevTxt = new GameObject("Text");
            prevTxt.transform.SetParent(prevBtn.transform, false);
            RectTransform ptrt = prevTxt.AddComponent<RectTransform>();
            ptrt.anchorMin = Vector2.zero; ptrt.anchorMax = Vector2.one;
            TMP_Text pt = prevTxt.AddComponent<TextMeshProUGUI>();
            if (sampleTmp != null) { pt.font = sampleTmp.font; pt.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
            pt.text = "<"; pt.fontSize = 22f; pt.alignment = TextAlignmentOptions.Center; pt.color = Color.white;

            GameObject displayObj = new GameObject("SceneDisplay");
            displayObj.transform.SetParent(row.transform, false);
            RectTransform dispRt = displayObj.AddComponent<RectTransform>();
            dispRt.anchorMin = new Vector2(0.35f, 0f);
            dispRt.anchorMax = new Vector2(0.91f, 1f);
            dispRt.sizeDelta = Vector2.zero;
            displayObj.AddComponent<Image>().color = new Color(0.04f, 0.08f, 0.15f, 0.88f);

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(displayObj.transform, false);
            RectTransform trt = textObj.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            _sceneLabelText = textObj.AddComponent<TextMeshProUGUI>();
            if (sampleTmp != null) { _sceneLabelText.font = sampleTmp.font; _sceneLabelText.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
            _sceneLabelText.fontSize = 18f;
            _sceneLabelText.alignment = TextAlignmentOptions.Center;
            _sceneLabelText.color = new Color(0.2f, 0.95f, 0.4f);
            _sceneLabelText.text = MapBrowserService.SelectedStagingScene;

            GameObject nextBtn = new GameObject("Btn_NextScene");
            nextBtn.transform.SetParent(row.transform, false);
            RectTransform nextRt = nextBtn.AddComponent<RectTransform>();
            nextRt.anchorMin = new Vector2(0.92f, 0.0f);
            nextRt.anchorMax = new Vector2(1.0f, 1f);
            nextRt.sizeDelta = Vector2.zero;
            nextBtn.AddComponent<Image>().color = new Color(0.1f, 0.15f, 0.25f, 0.9f);
            Button nb = nextBtn.AddComponent<Button>();
            nb.onClick.AddListener((Action)(() => CycleStagingScene(1)));

            GameObject nextTxt = new GameObject("Text");
            nextTxt.transform.SetParent(nextBtn.transform, false);
            RectTransform ntrt = nextTxt.AddComponent<RectTransform>();
            ntrt.anchorMin = Vector2.zero; ntrt.anchorMax = Vector2.one;
            TMP_Text nt = nextTxt.AddComponent<TextMeshProUGUI>();
            if (sampleTmp != null) { nt.font = sampleTmp.font; nt.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
            nt.text = ">"; nt.fontSize = 22f; nt.alignment = TextAlignmentOptions.Center; pt.color = Color.white;
        }

        private static void CycleStagingScene(int dir)
        {
            var list = MapBrowserService.AvailableStagingScenes;
            if (list == null || list.Count <= 1) return;

            int idx = list.IndexOf(MapBrowserService.SelectedStagingScene);
            if (idx < 0) idx = 0;

            idx = (idx + dir + list.Count) % list.Count;
            MapBrowserService.SelectedStagingScene = list[idx];

            if (_sceneLabelText != null)
                _sceneLabelText.text = MapBrowserService.SelectedStagingScene;
        }

        public static void RefreshRowTitlesInList(string newTitle)
        {
            for (int i = 0; i < _spawnedRowObjects.Count; i++)
            {
                GameObject row = _spawnedRowObjects[i];
                if (row == null) continue;
                if (row.name == $"CustomMap_{MapBrowserService.SelectedMapName}")
                {
                    TMP_Text[] tmps = row.GetComponentsInChildren<TMP_Text>(true);
                    if (tmps.Length >= 2) tmps[1].text = newTitle;
                    break;
                }
            }
        }

        public static bool ReplaceTitleScreenLogsButton()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.isLoaded) return false;

            bool replaced = false;
            GameObject[] roots = scene.GetRootGameObjects();

            for (int r = 0; r < roots.Length; r++)
            {
                if (roots[r] == null) continue;
                TMP_Text[] allTmps = roots[r].GetComponentsInChildren<TMP_Text>(true);

                for (int i = 0; i < allTmps.Length; i++)
                {
                    TMP_Text tmp = allTmps[i];
                    // Skip if null, inactive, or if text is null/empty
                    if (tmp == null || !tmp.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(tmp.text)) continue;
                    if (tmp.GetComponentInParent<LogsMenu>() != null) continue;

                    string t = tmp.text.Trim().ToLower();
                    if (t == "logs" || t == "log" || t == "archives" || t == "codex" || t == "records")
                    {
                        tmp.text = "Level Editor";
                        DisableLocalizationScripts(tmp.gameObject);
                        replaced = true;
                    }
                }
            }

            return replaced;
        }

        public static void OpenNativeMenu()
        {
            // Access through DeadCoreLevelEditorMod or find it safely in scene roots
            LogsMenu target = DeadCoreLevelEditorMod._cachedActiveMenu;
            if (target == null)
            {
                target = GameObject.FindObjectOfType<LogsMenu>();
                if (target == null)
                {
                    var scene = SceneManager.GetActiveScene();
                    if (scene.isLoaded)
                    {
                        GameObject[] roots = scene.GetRootGameObjects();
                        for (int r = 0; r < roots.Length; r++)
                        {
                            if (roots[r] == null) continue;
                            target = roots[r].GetComponentInChildren<LogsMenu>(true);
                            if (target != null) break;
                        }
                    }
                }
            }

            if (target != null)
            {
                MenuGroupScript targetGroup = target.GetComponentInParent<MenuGroupScript>();
                if (targetGroup != null)
                {
                    if (MenuGroupScript.CurrentGroup != null && MenuGroupScript.CurrentGroup != targetGroup)
                        MenuGroupScript.CurrentGroup.Close();
                    targetGroup.Open();
                }
            }
        }

        public static void EnforceCustomListOnly(LogsMenu menu)
        {
            if (menu == null || menu._logsButtonRoot == null) return;

            int count = menu._logsButtonRoot.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform child = menu._logsButtonRoot.GetChild(i);
                if (child != null && !child.name.StartsWith("CustomMap_"))
                    child.gameObject.SetActive(false);
            }
        }

        public static void EnforceBottomBarLabels()
        {
            if (_saveFeedbackTimer > 0f)
            {
                _saveFeedbackTimer -= Time.deltaTime;
                if (_saveFeedbackTimer <= 0f && _saveBtnText != null)
                {
                    _saveBtnText.text = "SAVE DETAILS";
                    _saveBtnText.color = Color.white;
                }
            }

            if (_nativePlayButton != null && _nativePlayButton.activeInHierarchy)
                SetButtonText(_nativePlayButton, "Play", 34f);

            if (_nativeCreateButton != null && _nativeCreateButton.activeInHierarchy)
                SetButtonText(_nativeCreateButton, "+ New", 34f);

            if (_nativeDeleteButton != null && _nativeDeleteButton.activeInHierarchy)
                SetButtonText(_nativeDeleteButton, "Delete", 34f);
        }

        public static void TransformLogsMenu(LogsMenu menu, int currentTab)
        {
            if (menu == null) return;

            MapBrowserService.EnsureDirectories();
            MapBrowserService.RefreshFiles();

            if (menu._logTitle != null)
            {
                menu._logTitle.text = "Level Editor";
                DisableLocalizationScripts(menu._logTitle.gameObject);
            }

            RebrandAndTrimNativeTabs(menu);

            _spawnedRowObjects.Clear();
            if (menu._logsButtonRoot != null)
            {
                for (int i = menu._logsButtonRoot.childCount - 1; i >= 0; i--)
                {
                    Transform child = menu._logsButtonRoot.GetChild(i);
                    if (child != null)
                    {
                        if (child.name.StartsWith("CustomMap_"))
                            GameObject.DestroyImmediate(child.gameObject);
                        else
                            child.gameObject.SetActive(false);
                    }
                }
            }

            if (menu._logPrefab == null || menu._logsButtonRoot == null) return;
            menu._logPrefab.gameObject.SetActive(false);

            List<string> fileList = new List<string>();
            if (currentTab == 0)
            {
                if (Directory.Exists(MapBrowserService.MyLevelsDir))
                    fileList.AddRange(Directory.GetFiles(MapBrowserService.MyLevelsDir, "*.txt"));

                if (fileList.Count == 0)
                {
                    string def = Path.Combine(MapBrowserService.MyLevelsDir, "Default_Level.txt");
                    if (!File.Exists(def))
                    {
                        Vector3 spawn = EditorSessionManager.LevelSpawnPosition;
                        File.WriteAllText(def, $"#TITLE: Default Level\n#AUTHOR: Community\n#DIFFICULTY: Normal\n#DESC: Starter platform.\n#SCENE: level01_Spark01\nFloor_Platform_16x16;{spawn.x:F4};{(spawn.y - 1.2f):F4};{spawn.z:F4};0.5500;0.0000;0.0000;0.0000;1.0000;0.00\n");
                    }
                    fileList.Add(def);
                }
            }
            else
            {
                if (Directory.Exists(MapBrowserService.DownloadedLevelsDir))
                    fileList.AddRange(Directory.GetFiles(MapBrowserService.DownloadedLevelsDir, "*.txt"));
            }

            for (int i = 0; i < fileList.Count; i++)
            {
                string filePath = fileList[i];
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                LevelMetadata meta = ReadLevelMetadata(filePath, fileName);

                GameObject itemObj = GameObject.Instantiate(menu._logPrefab.gameObject, menu._logsButtonRoot.transform);
                itemObj.name = $"CustomMap_{fileName}";
                itemObj.SetActive(true);

                DisableLocalizationScripts(itemObj);

                LogToggle lt = itemObj.GetComponent<LogToggle>();
                if (lt != null)
                {
                    if (lt._idLabel != null) { lt._idLabel.text = (i + 1).ToString("D2"); lt._idLabel.enableWordWrapping = false; }
                    if (lt._nameLabel != null) { lt._nameLabel.text = meta.Title; lt._nameLabel.enableWordWrapping = false; }
                    GameObject.DestroyImmediate(lt);
                }

                TMP_Text[] tmps = itemObj.GetComponentsInChildren<TMP_Text>(true);
                if (tmps.Length >= 2)
                {
                    tmps[0].text = (i + 1).ToString("D2"); tmps[0].enableWordWrapping = false;
                    tmps[1].text = meta.Title; tmps[1].enableWordWrapping = false;
                }

                Toggle tog = itemObj.GetComponent<Toggle>();
                if (tog != null) GameObject.DestroyImmediate(tog);

                Button btn = itemObj.GetComponent<Button>();
                if (btn == null) btn = itemObj.AddComponent<Button>();

                ColorBlock cb = btn.colors;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(0.3f, 0.8f, 1f, 1f);
                cb.pressedColor = new Color(1f, 0.7f, 0.2f, 1f);
                btn.colors = cb;

                string capturedPath = filePath;
                string capturedName = fileName;
                GameObject capturedObj = itemObj;

                btn.onClick = new Button.ButtonClickedEvent();
                btn.onClick.AddListener((Action)(() => OnLevelSelected(menu, capturedPath, capturedName, capturedObj)));

                _spawnedRowObjects.Add(itemObj);

                if (i == 0 || capturedPath == MapBrowserService.SelectedMapPath)
                    OnLevelSelected(menu, capturedPath, capturedName, capturedObj);
            }

            SetupBottomBarButtons(menu);
        }

        private static void RebrandAndTrimNativeTabs(LogsMenu menu)
        {
            TMP_Text[] allTexts = GameObject.FindObjectsOfType<TMP_Text>();
            for (int i = 0; i < allTexts.Length; i++)
            {
                TMP_Text t = allTexts[i];
                if (t == null) continue;

                string clean = t.text.Trim().ToLower();

                if (clean.Contains("t-log") || clean == "my levels")
                {
                    t.text = "My Levels";
                    DisableLocalizationScripts(t.gameObject);

                    _cachedMyLevelsToggle = t.GetComponentInParent<Toggle>();
                    if (_cachedMyLevelsToggle != null)
                    {
                        _cachedMyLevelsToggle.onValueChanged.RemoveAllListeners();
                        _cachedMyLevelsToggle.onValueChanged.AddListener((Action<bool>)((isOn) =>
                        {
                            if (isOn && DeadCoreLevelEditorMod.ActiveTab != 0)
                                DeadCoreLevelEditorMod.SwitchTab(0, menu);
                        }));
                    }
                }
                else if (clean.Contains("m-log") || clean == "community")
                {
                    t.text = "Community";
                    DisableLocalizationScripts(t.gameObject);

                    _cachedCommunityToggle = t.GetComponentInParent<Toggle>();
                    if (_cachedCommunityToggle != null)
                    {
                        _cachedCommunityToggle.onValueChanged.RemoveAllListeners();
                        _cachedCommunityToggle.onValueChanged.AddListener((Action<bool>)((isOn) =>
                        {
                            if (isOn && DeadCoreLevelEditorMod.ActiveTab != 1)
                                DeadCoreLevelEditorMod.SwitchTab(1, menu);
                        }));
                    }
                }
                else if (clean.Contains("d-log"))
                {
                    Toggle dToggle = t.GetComponentInParent<Toggle>();
                    if (dToggle != null) dToggle.gameObject.SetActive(false);
                    else if (t.transform.parent != null) t.transform.parent.gameObject.SetActive(false);
                }
            }
        }

        private static void OnLevelSelected(LogsMenu menu, string fullPath, string fileName, GameObject selectedRowObj)
        {
            MapBrowserService.SelectedMapPath = fullPath;
            MapBrowserService.SelectedMapName = fileName;

            int objectCount = 0;
            try
            {
                string[] lines = File.ReadAllLines(fullPath);
                for (int l = 0; l < lines.Length; l++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[l]) && !lines[l].Trim().StartsWith("#"))
                        objectCount++;
                }
            }
            catch { }

            LevelMetadata meta = ReadLevelMetadata(fullPath, fileName);

            if (!string.IsNullOrEmpty(meta.StagingScene) && MapBrowserService.AvailableStagingScenes.Contains(meta.StagingScene))
                MapBrowserService.SelectedStagingScene = meta.StagingScene;
            else
                MapBrowserService.SelectedStagingScene = "level01_Spark01";

            for (int i = 0; i < _spawnedRowObjects.Count; i++)
            {
                GameObject row = _spawnedRowObjects[i];
                if (row == null) continue;

                Image img = row.GetComponentInChildren<Image>(true);
                if (img != null)
                    img.color = (row == selectedRowObj) ? new Color(0.2f, 0.7f, 0.95f, 0.85f) : new Color(0.12f, 0.15f, 0.2f, 0.5f);
            }

            if (menu._logBigPicture != null)
            {
                Texture2D thumbTex = ThumbnailCaptureService.LoadLevelTexture(fullPath);
                if (thumbTex != null)
                {
                    menu._logBigPicture.texture = thumbTex;
                    menu._logBigPicture.color = Color.white;
                    menu._logBigPicture.gameObject.SetActive(true);
                }
            }

            BuildOrSyncNativeMetadataPanel(menu, meta, objectCount, fullPath);
        }

        private static void BuildOrSyncNativeMetadataPanel(LogsMenu menu, LevelMetadata meta, int objectCount, string fullPath)
        {
            if (menu == null || menu._logBigPicture == null) return;

            Transform parent = menu._logBigPicture.transform;
            bool isMyLevels = (DeadCoreLevelEditorMod.ActiveTab == 0);

            if (menu._shortDesc != null)
            {
                menu._shortDesc.gameObject.SetActive(!isMyLevels);
                if (!isMyLevels)
                {
                    menu._shortDesc.text = $"<b>{meta.Title.ToUpper()}</b>\nBY: {meta.Author.ToUpper()}  |  [{meta.Difficulty.ToUpper()}]  |  OBJECTS: {objectCount}\nSCENE: {meta.StagingScene}";
                    DisableLocalizationScripts(menu._shortDesc.gameObject);
                }
                else
                {
                    menu._shortDesc.text = "";
                }
            }

            if (menu._longDesc != null)
            {
                menu._longDesc.gameObject.SetActive(!isMyLevels);
                if (!isMyLevels)
                {
                    menu._longDesc.text = meta.Description;
                    DisableLocalizationScripts(menu._longDesc.gameObject);
                }
                else
                {
                    menu._longDesc.text = "";
                }
            }

            if (_nativeMetadataRoot == null || _nativeMetadataRoot.Equals(null))
            {
                _nativeMetadataRoot = new GameObject("Native_Metadata_Root");
                _nativeMetadataRoot.transform.SetParent(parent, false);

                RectTransform rootRt = _nativeMetadataRoot.AddComponent<RectTransform>();
                rootRt.anchorMin = Vector2.zero;
                rootRt.anchorMax = Vector2.one;
                rootRt.offsetMin = new Vector2(30f, 25f);
                rootRt.offsetMax = new Vector2(-30f, -25f);

                TMP_Text sampleText = menu._shortDesc != null ? menu._shortDesc : menu.GetComponentInChildren<TMP_Text>(true);

                CreateNativeInputRow(_nativeMetadataRoot.transform, sampleText, "LEVEL TITLE", 0f, 38f, 24f, out _titleInput);
                CreateNativeInputRow(_nativeMetadataRoot.transform, sampleText, "AUTHOR", -44f, 38f, 20f, out _authorInput);
                CreateDifficultyRow(_nativeMetadataRoot.transform, sampleText, -88f);
                CreateSceneSelectorRow(_nativeMetadataRoot.transform, sampleText, -136f);
                CreateNativeInputRow(_nativeMetadataRoot.transform, sampleText, "DESCRIPTION", -180f, 65f, 19f, out _descInput, true);
                CreateLevelStatsHUD(_nativeMetadataRoot.transform, sampleText, -252f);
                CreateNativeSaveButton(_nativeMetadataRoot.transform, sampleText, -342f);
            }

            _nativeMetadataRoot.SetActive(isMyLevels);

            if (_titleInput != null) _titleInput.text = meta.Title;
            if (_authorInput != null) _authorInput.text = meta.Author;
            if (_descInput != null) _descInput.text = meta.Description;

            if (_sceneLabelText != null)
            {
                _sceneLabelText.text = !string.IsNullOrEmpty(meta.StagingScene) ? meta.StagingScene : "level01_Spark01";
                MapBrowserService.SelectedStagingScene = _sceneLabelText.text;
            }

            _selectedDifficultyIndex = 2;
            for (int i = 0; i < DifficultyNames.Length; i++)
            {
                if (string.Equals(meta.Difficulty, DifficultyNames[i], StringComparison.OrdinalIgnoreCase))
                {
                    _selectedDifficultyIndex = i;
                    break;
                }
            }
            HighlightSelectedDifficulty();

            UpdateLevelStatsHUD(fullPath, objectCount);
        }

        private static void CreateNativeInputRow(Transform parent, TMP_Text sampleTmp, string labelName, float posY, float height, float fontSize, out TMP_InputField inputField, bool isMultiLine = false)
        {
            GameObject row = new GameObject("Row_" + labelName);
            row.transform.SetParent(parent, false);

            RectTransform rowRt = row.AddComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0f, 1f);
            rowRt.anchorMax = new Vector2(1f, 1f);
            rowRt.pivot = new Vector2(0.5f, 1f);
            rowRt.sizeDelta = new Vector2(0f, height);
            rowRt.anchoredPosition = new Vector2(0f, posY);

            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(row.transform, false);
            RectTransform labelRt = labelObj.AddComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0f, 0.5f);
            labelRt.anchorMax = new Vector2(0.25f, 0.5f);
            labelRt.sizeDelta = Vector2.zero;

            TMP_Text labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
            if (sampleTmp != null) { labelTmp.font = sampleTmp.font; labelTmp.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
            labelTmp.fontSize = 20f;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.color = new Color(0.2f, 0.82f, 1f, 1f);
            labelTmp.text = labelName;
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;

            GameObject inputObj = new GameObject("InputField");
            inputObj.transform.SetParent(row.transform, false);
            RectTransform inRt = inputObj.AddComponent<RectTransform>();
            inRt.anchorMin = new Vector2(0.26f, 0f);
            inRt.anchorMax = new Vector2(1f, 1f);
            inRt.sizeDelta = Vector2.zero;

            Image bg = inputObj.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.08f, 0.15f, 0.88f);

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(inputObj.transform, false);
            RectTransform textRt = textObj.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12f, 5f);
            textRt.offsetMax = new Vector2(-12f, -5f);

            TMP_Text inTmp = textObj.AddComponent<TextMeshProUGUI>();
            if (sampleTmp != null) { inTmp.font = sampleTmp.font; inTmp.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
            inTmp.fontSize = fontSize;
            inTmp.color = Color.white;
            inTmp.alignment = isMultiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;

            inputField = inputObj.AddComponent<TMP_InputField>();
            inputField.textViewport = textRt;
            inputField.textComponent = inTmp;
            inputField.lineType = isMultiLine ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
        }

        private static void CreateDifficultyRow(Transform parent, TMP_Text sampleTmp, float posY)
        {
            GameObject row = new GameObject("Row_Difficulty");
            row.transform.SetParent(parent, false);

            RectTransform rowRt = row.AddComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0f, 1f);
            rowRt.anchorMax = new Vector2(1f, 1f);
            rowRt.pivot = new Vector2(0.5f, 1f);
            rowRt.sizeDelta = new Vector2(0f, 48f);
            rowRt.anchoredPosition = new Vector2(0f, posY);

            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(row.transform, false);
            RectTransform labelRt = labelObj.AddComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0f, 0.5f);
            labelRt.anchorMax = new Vector2(0.25f, 0.5f);
            labelRt.sizeDelta = Vector2.zero;

            TMP_Text labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
            if (sampleTmp != null) { labelTmp.font = sampleTmp.font; labelTmp.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
            labelTmp.fontSize = 20f;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.color = new Color(0.2f, 0.82f, 1f, 1f);
            labelTmp.text = "DIFFICULTY";
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;

            _diffButtons.Clear();
            float btnW = 0.74f / 5f;

            for (int i = 0; i < DifficultyNames.Length; i++)
            {
                int captureIdx = i;
                GameObject btn = new GameObject("Diff_" + DifficultyNames[i]);
                btn.transform.SetParent(row.transform, false);

                RectTransform brt = btn.AddComponent<RectTransform>();
                brt.anchorMin = new Vector2(0.26f + (i * btnW), 0.02f);
                brt.anchorMax = new Vector2(0.26f + ((i + 1) * btnW) - 0.012f, 0.98f);
                brt.sizeDelta = Vector2.zero;

                Image bImg = btn.AddComponent<Image>();
                bImg.color = new Color(0.08f, 0.14f, 0.22f, 0.9f);

                Button bComp = btn.AddComponent<Button>();
                ColorBlock cb = bComp.colors;
                cb.normalColor = Color.white;
                cb.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
                cb.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
                bComp.colors = cb;

                bComp.onClick.AddListener((Action)(() =>
                {
                    _selectedDifficultyIndex = captureIdx;
                    HighlightSelectedDifficulty();
                }));

                GameObject bText = new GameObject("Text");
                bText.transform.SetParent(btn.transform, false);
                RectTransform trt = bText.AddComponent<RectTransform>();
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;

                TMP_Text bTmp = bText.AddComponent<TextMeshProUGUI>();
                if (sampleTmp != null) { bTmp.font = sampleTmp.font; bTmp.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
                bTmp.fontSize = 20f;
                bTmp.fontStyle = FontStyles.Bold;
                bTmp.text = DifficultyNames[i];
                bTmp.alignment = TextAlignmentOptions.Center;
                bTmp.color = DifficultyColors[i];

                _diffButtons.Add(btn);
            }
        }

        private static void HighlightSelectedDifficulty()
        {
            for (int i = 0; i < _diffButtons.Count; i++)
            {
                Image img = _diffButtons[i].GetComponent<Image>();
                TMP_Text txt = _diffButtons[i].GetComponentInChildren<TMP_Text>(true);

                if (i == _selectedDifficultyIndex)
                {
                    if (img != null) img.color = new Color(DifficultyColors[i].r * 0.75f, DifficultyColors[i].g * 0.75f, DifficultyColors[i].b * 0.75f, 0.95f);
                    if (txt != null) { txt.color = Color.white; txt.fontStyle = FontStyles.Bold; }
                }
                else
                {
                    if (img != null) img.color = new Color(0.06f, 0.1f, 0.16f, 0.8f);
                    if (txt != null) { txt.color = DifficultyColors[i] * 0.75f; txt.fontStyle = FontStyles.Normal; }
                }
            }
        }

        private static void CreateLevelStatsHUD(Transform parent, TMP_Text sampleTmp, float posY)
        {
            GameObject statsBox = new GameObject("HUD_LevelStats");
            statsBox.transform.SetParent(parent, false);

            RectTransform srt = statsBox.AddComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.26f, 1f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.pivot = new Vector2(0.5f, 1f);
            srt.sizeDelta = new Vector2(0f, 86f);
            srt.anchoredPosition = new Vector2(0f, posY);

            Image bg = statsBox.AddComponent<Image>();
            bg.color = new Color(0.03f, 0.06f, 0.12f, 0.75f);

            GameObject leftObj = new GameObject("StatsLeft");
            leftObj.transform.SetParent(statsBox.transform, false);
            RectTransform lrt = leftObj.AddComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0.04f, 0f);
            lrt.anchorMax = new Vector2(0.50f, 1f);
            lrt.sizeDelta = Vector2.zero;

            _statsLabelLeft = leftObj.AddComponent<TextMeshProUGUI>();
            if (sampleTmp != null) { _statsLabelLeft.font = sampleTmp.font; _statsLabelLeft.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
            _statsLabelLeft.fontSize = 17f;
            _statsLabelLeft.color = new Color(0.6f, 0.85f, 1f, 0.9f);
            _statsLabelLeft.alignment = TextAlignmentOptions.MidlineLeft;

            GameObject rightObj = new GameObject("StatsRight");
            rightObj.transform.SetParent(statsBox.transform, false);
            RectTransform rrt = rightObj.AddComponent<RectTransform>();
            rrt.anchorMin = new Vector2(0.52f, 0f);
            rrt.anchorMax = new Vector2(0.96f, 1f);
            rrt.sizeDelta = Vector2.zero;

            _statsLabelRight = rightObj.AddComponent<TextMeshProUGUI>();
            if (sampleTmp != null) { _statsLabelRight.font = sampleTmp.font; _statsLabelRight.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
            _statsLabelRight.fontSize = 17f;
            _statsLabelRight.color = new Color(0.6f, 0.85f, 1f, 0.9f);
            _statsLabelRight.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private static void UpdateLevelStatsHUD(string fullPath, int objectCount)
        {
            if (_statsLabelLeft == null || _statsLabelRight == null || !File.Exists(fullPath)) return;

            int lasers = 0, jumpers = 0, turbines = 0, turrets = 0, paths = 0;
            try
            {
                string[] lines = File.ReadAllLines(fullPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string l = lines[i].ToLower();
                    if (l.StartsWith("#")) continue;
                    if (l.Contains("laser")) lasers++;
                    else if (l.Contains("jumper")) jumpers++;
                    else if (l.Contains("helix")) turbines++;
                    else if (l.Contains("turret")) turrets++;
                    if (l.Contains(";path:1")) paths++;
                }
            }
            catch { }

            DateTime mod = File.GetLastWriteTime(fullPath);
            _statsLabelLeft.text = $"- TOTAL OBJECTS: <b><color=#00E5FF>{objectCount}</color></b>\n- HAZARDS & LASERS: <b><color=#FF5252>{lasers}</color></b>\n- JUMP PADS: <b><color=#FFEB3B>{jumpers}</color></b>";
            _statsLabelRight.text = $"- MOVING PATHS: <b><color=#E040FB>{paths}</color></b>\n- TURRET ENEMIES: <b><color=#FF4081>{turrets}</color></b>\n- LAST SAVED: <color=#B0BEC5>{mod:dd/MM/yyyy HH:mm}</color>";
        }

        private static void CreateNativeSaveButton(Transform parent, TMP_Text sampleTmp, float posY)
        {
            GameObject saveBtn = new GameObject("Btn_SaveMetadata");
            saveBtn.transform.SetParent(parent, false);

            RectTransform srt = saveBtn.AddComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.26f, 1f);
            srt.anchorMax = new Vector2(0.66f, 1f);
            srt.pivot = new Vector2(0f, 1f);
            srt.sizeDelta = new Vector2(0f, 48f);
            srt.anchoredPosition = new Vector2(0f, posY);

            Image img = saveBtn.AddComponent<Image>();
            img.color = new Color(0.12f, 0.65f, 0.95f, 0.95f);

            Button b = saveBtn.AddComponent<Button>();
            ColorBlock cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            cb.pressedColor = new Color(0.2f, 1f, 0.5f, 1f);
            b.colors = cb;

            b.onClick.RemoveAllListeners();
            b.onClick.AddListener((Action)(() => SaveCurrentMetadata(MapBrowserService.SelectedMapPath)));

            GameObject txt = new GameObject("Text");
            txt.transform.SetParent(saveBtn.transform, false);
            RectTransform trt = txt.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;

            _saveBtnText = txt.AddComponent<TextMeshProUGUI>();
            if (sampleTmp != null) { _saveBtnText.font = sampleTmp.font; _saveBtnText.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
            _saveBtnText.fontSize = 22f;
            _saveBtnText.text = "SAVE DETAILS";
            _saveBtnText.fontStyle = FontStyles.Bold;
            _saveBtnText.color = Color.white;
            _saveBtnText.alignment = TextAlignmentOptions.Center;
        }

        private static void SetupBottomBarButtons(LogsMenu menu)
        {
            BackButton nativeBack = GameObject.FindObjectOfType<BackButton>();
            if (nativeBack == null) return;

            Transform parentBar = nativeBack.transform.parent;
            RectTransform backRt = nativeBack.GetComponent<RectTransform>();

            float nativeWidth = backRt.rect.width > 50f ? backRt.rect.width : (backRt.sizeDelta.x > 50f ? backRt.sizeDelta.x : 220f);
            float nativeHeight = backRt.rect.height > 20f ? backRt.rect.height : (backRt.sizeDelta.y > 20f ? backRt.sizeDelta.y : 55f);

            TMP_Text backTmp = nativeBack.GetComponentInChildren<TMP_Text>(true);
            float nativeFontSize = (backTmp != null && backTmp.fontSize > 15f) ? backTmp.fontSize : 34f;
            float spacing = 15f;

            if (_nativePlayButton == null || _nativePlayButton.Equals(null) || _nativePlayButton.transform.parent != parentBar)
            {
                if (_nativePlayButton != null) GameObject.DestroyImmediate(_nativePlayButton);
                _nativePlayButton = GameObject.Instantiate(nativeBack.gameObject, parentBar);
                _nativePlayButton.name = "Btn_NativePlayLevel";
            }

            _nativePlayButton.SetActive(true);
            CleanNativeButtonClone(_nativePlayButton);

            RectTransform playRt = _nativePlayButton.GetComponent<RectTransform>();
            playRt.anchorMin = backRt.anchorMin;
            playRt.anchorMax = backRt.anchorMax;
            playRt.pivot = backRt.pivot;
            playRt.localScale = backRt.localScale;
            playRt.sizeDelta = new Vector2(nativeWidth, nativeHeight);
            playRt.anchoredPosition = new Vector2(backRt.anchoredPosition.x - (nativeWidth + spacing), backRt.anchoredPosition.y);

            TintChevron(_nativePlayButton, new Color(0.1f, 0.85f, 1f, 1f));
            SetButtonText(_nativePlayButton, "Play", nativeFontSize);

            Button playBtn = _nativePlayButton.GetComponent<Button>();
            if (playBtn == null) playBtn = _nativePlayButton.AddComponent<Button>();
            playBtn.onClick = new Button.ButtonClickedEvent();
            playBtn.onClick.AddListener((Action)(() =>
            {
                if (!string.IsNullOrEmpty(MapBrowserService.SelectedMapPath))
                {
                    MelonLogger.Msg($">> Launching selected map: '{MapBrowserService.SelectedMapName}' from '{MapBrowserService.SelectedMapPath}'");
                    MapBrowserService.LaunchSelectedMap();
                }
            }));

            if (_nativeCreateButton == null || _nativeCreateButton.Equals(null) || _nativeCreateButton.transform.parent != parentBar)
            {
                if (_nativeCreateButton != null) GameObject.DestroyImmediate(_nativeCreateButton);
                _nativeCreateButton = GameObject.Instantiate(nativeBack.gameObject, parentBar);
                _nativeCreateButton.name = "Btn_NativeCreateLevel";
            }

            _nativeCreateButton.SetActive(true);
            CleanNativeButtonClone(_nativeCreateButton);

            RectTransform createRt = _nativeCreateButton.GetComponent<RectTransform>();
            createRt.anchorMin = backRt.anchorMin;
            createRt.anchorMax = backRt.anchorMax;
            createRt.pivot = backRt.pivot;
            createRt.localScale = backRt.localScale;
            createRt.sizeDelta = new Vector2(nativeWidth, nativeHeight);
            createRt.anchoredPosition = new Vector2(backRt.anchoredPosition.x - (nativeWidth + spacing) * 2f, backRt.anchoredPosition.y);

            TintChevron(_nativeCreateButton, new Color(0.25f, 0.9f, 0.45f, 1f));
            SetButtonText(_nativeCreateButton, "+ New", nativeFontSize);

            Button createBtn = _nativeCreateButton.GetComponent<Button>();
            if (createBtn == null) createBtn = _nativeCreateButton.AddComponent<Button>();
            createBtn.onClick = new Button.ButtonClickedEvent();
            createBtn.onClick.AddListener((Action)(() => CreateNewLevel(menu)));

            if (_nativeDeleteButton == null || _nativeDeleteButton.Equals(null) || _nativeDeleteButton.transform.parent != parentBar)
            {
                if (_nativeDeleteButton != null) GameObject.DestroyImmediate(_nativeDeleteButton);
                _nativeDeleteButton = GameObject.Instantiate(nativeBack.gameObject, parentBar);
                _nativeDeleteButton.name = "Btn_NativeDeleteLevel";
            }

            _nativeDeleteButton.SetActive(true);
            CleanNativeButtonClone(_nativeDeleteButton);

            RectTransform deleteRt = _nativeDeleteButton.GetComponent<RectTransform>();
            deleteRt.anchorMin = backRt.anchorMin;
            deleteRt.anchorMax = backRt.anchorMax;
            deleteRt.pivot = backRt.pivot;
            deleteRt.localScale = backRt.localScale;
            deleteRt.sizeDelta = new Vector2(nativeWidth, nativeHeight);
            deleteRt.anchoredPosition = new Vector2(backRt.anchoredPosition.x - (nativeWidth + spacing) * 3f, backRt.anchoredPosition.y);

            TintChevron(_nativeDeleteButton, new Color(0.95f, 0.25f, 0.25f, 1f));
            SetButtonText(_nativeDeleteButton, "Delete", nativeFontSize);

            Button deleteBtn = _nativeDeleteButton.GetComponent<Button>();
            if (deleteBtn == null) deleteBtn = _nativeDeleteButton.AddComponent<Button>();
            deleteBtn.onClick = new Button.ButtonClickedEvent();
            deleteBtn.onClick.AddListener((Action)(() => DeleteCurrentSelectedLevel(menu)));

            EnforceBottomBarLabels();
        }

        public static void DeleteCurrentSelectedLevel(LogsMenu menu)
        {
            if (string.IsNullOrEmpty(MapBrowserService.SelectedMapPath) || !File.Exists(MapBrowserService.SelectedMapPath))
            {
                MelonLogger.Warning("No level selected to delete.");
                return;
            }

            try
            {
                string deletingFile = MapBrowserService.SelectedMapPath;
                File.Delete(deletingFile);

                string pngFile = Path.ChangeExtension(deletingFile, ".png");
                if (File.Exists(pngFile)) File.Delete(pngFile);

                MelonLogger.Msg($">> Deleted custom level: '{deletingFile}'");

                MapBrowserService.SelectedMapPath = "";
                MapBrowserService.SelectedMapName = "";

                TransformLogsMenu(menu, DeadCoreLevelEditorMod.ActiveTab);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to delete level: {ex.Message}");
            }
        }

        private static void CreateNewLevel(LogsMenu menu)
        {
            string saveDir = MapBrowserService.MyLevelsDir;
            if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

            int idx = 1;
            string newName = $"New_Level_{idx}";
            string newPath = Path.Combine(saveDir, $"{newName}.txt");

            while (File.Exists(newPath))
            {
                idx++;
                newName = $"New_Level_{idx}";
                newPath = Path.Combine(saveDir, $"{newName}.txt");
            }

            Vector3 spawn = EditorSessionManager.LevelSpawnPosition;
            string scene = !string.IsNullOrEmpty(MapBrowserService.SelectedStagingScene) ? MapBrowserService.SelectedStagingScene : "level01_Spark01";
            string starterContent = $"#TITLE: New Level {idx}\n#AUTHOR: Player\n#DIFFICULTY: Normal\n#DESC: Custom level created with DeadCore Level Editor.\n#SCENE: {scene}\n" +
                                   $"Floor_Platform_16x16;{spawn.x:F4};{(spawn.y - 1.2f):F4};{spawn.z:F4};0.5500;0.0000;0.0000;0.0000;1.0000;0.00\n";
            File.WriteAllText(newPath, starterContent);

            MelonLogger.Msg($">> Created new level with solid floor platform: '{newName}.txt'");

            DeadCoreLevelEditorMod.SwitchTab(0, menu);
            MapBrowserService.SelectedMapPath = newPath;
            MapBrowserService.SelectedMapName = newName;

            TransformLogsMenu(menu, 0);
        }

        private static void CleanNativeButtonClone(GameObject btnObj)
        {
            Component[] comps = btnObj.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < comps.Length; i++)
            {
                Component c = comps[i];
                if (c == null) continue;
                string typeName = c.GetIl2CppType().Name;
                if (typeName == "TextLabel" || typeName.Contains("Translate") || typeName.Contains("Localization") || typeName == "BackButton")
                    GameObject.DestroyImmediate(c);
            }

            LabelButton lb = btnObj.GetComponent<LabelButton>();
            if (lb != null) GameObject.DestroyImmediate(lb);

            Toggle tog = btnObj.GetComponent<Toggle>();
            if (tog != null) GameObject.DestroyImmediate(tog);

            LogToggle lt = btnObj.GetComponent<LogToggle>();
            if (lt != null) GameObject.DestroyImmediate(lt);
        }

        private static void TintChevron(GameObject btnObj, Color accentColor)
        {
            Image[] images = btnObj.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image img = images[i];
                if (img != null && img.color.r > 0.65f && img.color.g > 0.35f && img.color.b < 0.3f)
                    img.color = accentColor;
            }
        }

        private static void SetButtonText(GameObject btnObj, string label, float fontSize)
        {
            TMP_Text[] tmps = btnObj.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < tmps.Length; i++)
            {
                TMP_Text tmp = tmps[i];
                if (tmp == null) continue;

                RectTransform textRt = tmp.GetComponent<RectTransform>();
                if (textRt != null)
                {
                    textRt.anchorMin = Vector2.zero;
                    textRt.anchorMax = Vector2.one;
                    textRt.sizeDelta = Vector2.zero;
                    textRt.anchoredPosition = Vector2.zero;
                }

                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontSize = fontSize;
                tmp.text = label;
                tmp.color = Color.white;
            }
        }

        private static void DisableLocalizationScripts(GameObject root)
        {
            if (root == null) return;

            try
            {
                Component[] comps = root.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < comps.Length; i++)
                {
                    Component c = comps[i];
                    if (c == null) continue;

                    var il2Type = c.GetIl2CppType();
                    if (il2Type == null) continue;

                    string typeName = il2Type.Name;
                    if (typeName == "TextLabel" || typeName.Contains("Translate") || typeName.Contains("Localization"))
                    {
                        MonoBehaviour mb = c.TryCast<MonoBehaviour>();
                        if (mb != null) mb.enabled = false;
                    }
                }
            }
            catch { }
        }
    }

    // =========================================================================
    // SECTION 3: MELONMOD ENTRY POINT & MAIN RUNTIME LOOP
    // =========================================================================

    public class DeadCoreLevelEditorMod : MelonMod
    {
        private static LogsMenu _lastTransformedLogsMenu = null;
        public static LogsMenu _cachedActiveMenu = null;
        public static int ActiveTab = 0;
        private static float _titleButtonScanTimer = 0f;
        private static bool _titleButtonHooked = false;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("===============================================================");
            LoggerInstance.Msg("   DeadCore Level Editor Suite - Unified Studio Edition        ");
            LoggerInstance.Msg("===============================================================");
            MapBrowserService.EnsureDirectories();
            MapBrowserService.ScanStagingScenes();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _lastTransformedLogsMenu = null;
            _cachedActiveMenu = null;
            _titleButtonHooked = false;
            _titleButtonScanTimer = 0.1f;

            string s = sceneName.ToLower();
            if (s.Contains("menu") || s.Contains("title") || s.Contains("boot"))
            {
                EditorSessionManager.ResetSession();
                StudioUIManager.DestroyUI();
                StudioGizmoController.DestroyGizmo();
            }
        }

        public override void OnUpdate()
        {
            string currentScene = SceneManager.GetActiveScene().name.ToLower();
            bool isIgnoredScene = currentScene.Contains("menu") || currentScene.Contains("title") ||
                                  currentScene.Contains("boot") || currentScene.Contains("root") ||
                                  currentScene.Contains("load") || currentScene.Contains("transition");

            if (isIgnoredScene)
            {
                if (!_titleButtonHooked)
                {
                    _titleButtonScanTimer -= Time.deltaTime;
                    if (_titleButtonScanTimer <= 0f)
                    {
                        _titleButtonScanTimer = 0.5f;
                        if (NativeLogsMenuHijacker.ReplaceTitleScreenLogsButton())
                            _titleButtonHooked = true;
                    }
                }

                if (Input.GetKeyDown(KeyCode.F2))
                    NativeLogsMenuHijacker.OpenNativeMenu();

                // ONLY find and transform the LogsMenu when it is actually OPEN and ACTIVE
                LogsMenu activeMenu = GameObject.FindObjectOfType<LogsMenu>();

                if (activeMenu != null && activeMenu.gameObject.activeInHierarchy)
                {
                    if (_lastTransformedLogsMenu != activeMenu)
                    {
                        _lastTransformedLogsMenu = activeMenu;
                        NativeLogsMenuHijacker.TransformLogsMenu(activeMenu, ActiveTab);
                    }

                    NativeLogsMenuHijacker.EnforceCustomListOnly(activeMenu);
                    NativeLogsMenuHijacker.EnforceBottomBarLabels();

                    if (GUIUtility.keyboardControl == 0)
                    {
                        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                        {
                            if (!string.IsNullOrEmpty(MapBrowserService.SelectedMapPath))
                                MapBrowserService.LaunchSelectedMap();
                        }

                        if (Input.GetKeyDown(KeyCode.Delete))
                            NativeLogsMenuHijacker.DeleteCurrentSelectedLevel(activeMenu);
                    }
                }
                else
                {
                    _lastTransformedLogsMenu = null;
                }

                return;
            }

            if (!EditorSessionManager.IsCustomSessionActive) return;

            if (!EditorSessionManager.IsLevelInitialized)
            {
                bool isLevelScene = currentScene.Contains("level") || currentScene.Contains("spark");
                if (isLevelScene)
                {
                    GameObject player = EditorSessionManager.FindPlayerEntity();
                    if (player != null && player.GetComponentInChildren<CharacterController>() != null)
                    {
                        MelonLogger.Msg($">> [Lifecycle] Player detected in '{currentScene}'. Initializing Studio Editor!");
                        EditorSessionManager.InitializeCustomLevel();
                    }
                }
                return;
            }

            StudioUIManager.EnsureSelectableColliders();
            EditorSessionManager.UpdateSession();
        }

        public override void OnGUI()
        {
            if (EditorSessionManager.IsCustomSessionActive && !EditorSessionManager.IsEditModeActive)
            {
                EditorSessionManager.DrawPlaytestHUD();
            }
        }

        public static void SwitchTab(int tabIndex, LogsMenu menu)
        {
            if (ActiveTab == tabIndex && _lastTransformedLogsMenu == menu && NativeLogsMenuHijacker.SpawnedRowCount > 0) return;
            ActiveTab = tabIndex;
            NativeLogsMenuHijacker.TransformLogsMenu(menu, ActiveTab);
        }
    }
}