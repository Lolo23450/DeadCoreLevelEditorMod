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
    // SECTION 1: STUDIO UGUI SYSTEM (DOCKED, TREE-HIERARCHY, ZERO OVERLAPS)
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

        // Scene Hierarchy (Left Panel: 245px Tree View)
        private static GameObject _hierarchyPanel = null;
        private static RectTransform _hierarchyContent = null;
        private static TMP_InputField _hierarchySearchInput = null;
        private static readonly List<GameObject> _hierarchyRows = new List<GameObject>();
        private static readonly HashSet<GameObject> _collapsedParents = new HashSet<GameObject>();

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
        private static TMP_InputField _scaleInput = null;

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

        // Merged Lighting & Atmosphere Section
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

        private static GameObject _motionPathSection = null;
        private static TMP_Text _motionPathStatusText = null;
        private static GameObject _parentingSection = null;

        // Docked Bottom Asset Browser
        private static GameObject _assetBrowserPanel = null;
        private static RectTransform _browserContent = null;
        private static TMP_InputField _browserSearchInput = null;
        private static string _activeBrowserCategory = "Architecture";
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

            BuildTopToolbar();
            BuildHierarchyPanel();
            BuildInspectorPanel();
            BuildAssetBrowserPanel();
            BuildToastOverlay();

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

                if (EditorSessionManager.PlacedObjectTypes.TryGetValue(obj, out var pType) &&
                    (pType == PlacedObjectType.Spotlight || pType == PlacedObjectType.Sunlight))
                {
                    BoxCollider bc = obj.GetComponent<BoxCollider>();
                    if (bc == null)
                    {
                        bc = obj.AddComponent<BoxCollider>();
                        bc.size = new Vector3(1.4f, 1.4f, 1.6f);
                        bc.center = new Vector3(0f, 0f, 0.4f);
                        bc.isTrigger = false;
                        bc.enabled = true;
                    }

                    obj.layer = 0;
                    Transform housing = obj.transform.Find("Light_Housing");
                    if (housing != null) housing.gameObject.layer = 0;
                    Transform lens = obj.transform.Find("Light_Lens");
                    if (lens != null) lens.gameObject.layer = 0;
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
        // THUMBNAIL RENDERER
        // =========================================================================

        public static class AssetThumbnailRenderer
        {
            private static Camera _previewCam = null;
            private static GameObject _studioStage = null;
            private static Light _studioLight = null;
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
                _previewCam.backgroundColor = new Color(0.14f, 0.16f, 0.20f, 1f);
                _previewCam.cullingMask = 1 << 2;
                _previewCam.fieldOfView = 28f;
                _previewCam.nearClipPlane = 0.1f;
                _previewCam.farClipPlane = 500f;

                _previewRt = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32);
                _previewCam.targetTexture = _previewRt;

                GameObject lightObj = new GameObject("Studio_KeyLight");
                lightObj.transform.SetParent(_studioStage.transform, false);
                _studioLight = lightObj.AddComponent<Light>();
                _studioLight.type = LightType.Directional;
                _studioLight.color = new Color(1f, 0.96f, 0.90f);
                _studioLight.intensity = 2.2f;
                _studioLight.cullingMask = 1 << 2;
                lightObj.transform.rotation = Quaternion.Euler(40f, -40f, 0f);
            }

            public static Sprite GenerateThumbnail(CatalogAsset asset)
            {
                if (asset == null || asset.SourceTemplate == null) return null;

                EnsureStudio();

                GameObject tempModel = GameObject.Instantiate(asset.SourceTemplate);
                tempModel.SetActive(true);
                tempModel.transform.position = StagePosition;
                tempModel.transform.rotation = Quaternion.Euler(18f, -38f, 0f) * asset.BaseRotation;
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
                float camDist = (radius / Mathf.Sin(fovRad)) * 1.3f;

                Vector3 camPos = modelCenter + new Vector3(camDist * 0.7f, camDist * 0.55f, -camDist * 0.85f);
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
            _toolbarPanel = CreatePanel(_canvasRoot.transform, "Top_Toolbar", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -20f), new Vector2(0f, 40f), new Color(0.11f, 0.12f, 0.14f, 0.98f));

            HorizontalLayoutGroup hlg = _toolbarPanel.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(10, 10, 5, 5);
            hlg.spacing = 6f;
            hlg.childControlWidth = false;
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

            CreateButton(_toolbarPanel.transform, "Btn_Translate", "Move (W)", 75f, () => SetGizmoMode(EditorGizmoMode.Translate));
            CreateButton(_toolbarPanel.transform, "Btn_Rotate", "Rotate (E)", 75f, () => SetGizmoMode(EditorGizmoMode.Rotate));
            CreateButton(_toolbarPanel.transform, "Btn_Scale", "Scale (R)", 75f, () => SetGizmoMode(EditorGizmoMode.Scale));

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

            CreateButton(_toolbarPanel.transform, "Btn_Snapshot", "📷 3D Snapshot", 115f, () =>
            {
                ThumbnailCaptureService.CaptureLevelThumbnail(MapBrowserService.SelectedMapPath, EditorSessionManager.PlacedObjects, EditorSessionManager.LevelSpawnPosition);
                EditorSessionManager.ShowNotification("Captured 3D diagonal overhead thumbnail!");
            }, new Color(0.2f, 0.5f, 0.8f));

            CreateButton(_toolbarPanel.transform, "Btn_Playtest", "▶ PLAYTEST (F1)", 140f, () => EditorSessionManager.ToggleEditMode(), new Color(0.18f, 0.65f, 0.32f));
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
        // HIERARCHY PANEL (LEFT: 245px TREE VIEW DOCKED, ZERO BUTTON OVERLAPS)
        // =========================================================================

        private static void BuildHierarchyPanel()
        {
            _hierarchyPanel = CreatePanel(_canvasRoot.transform, "Hierarchy_Panel",
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(122.5f, -20f), new Vector2(245f, -40f),
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

            GameObject bottomBar = CreatePanel(_hierarchyPanel.transform, "Hierarchy_BottomBar",
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 18f), new Vector2(0f, 36f),
                new Color(0.08f, 0.09f, 0.11f, 0.95f));

            Button delBtn = CreateButton(bottomBar.transform, "Btn_DeleteSelected", "Delete Selected [Supr]", 225f, () =>
            {
                EditorSessionManager.DeleteSelectedObjects();
            }, new Color(0.75f, 0.22f, 0.22f, 1f));

            RectTransform dbrt = delBtn.GetComponent<RectTransform>();
            dbrt.anchorMin = new Vector2(0f, 0f);
            dbrt.anchorMax = new Vector2(1f, 1f);
            dbrt.offsetMin = new Vector2(8f, 4f);
            dbrt.offsetMax = new Vector2(-8f, -4f);
        }

        // =========================================================================
        // EXPANDABLE INSPECTOR PANEL (RIGHT, SECTION CARDS, ZERO OVERLAPS)
        // =========================================================================

        private static void BuildInspectorPanel()
        {
            _isInspectorExpanded = false;
            _inspectorPanel = CreatePanel(_canvasRoot.transform, "Inspector_Panel",
                new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-140f, -20f), new Vector2(280f, -40f),
                new Color(0.12f, 0.13f, 0.15f, 0.98f));
            _inspectorPanelRt = _inspectorPanel.GetComponent<RectTransform>();

            // Title bar with Expand button
            GameObject titleBar = new GameObject("TitleBar");
            titleBar.transform.SetParent(_inspectorPanel.transform, false);
            RectTransform tbrt = titleBar.AddComponent<RectTransform>();
            tbrt.anchorMin = new Vector2(0f, 1f);
            tbrt.anchorMax = new Vector2(1f, 1f);
            tbrt.pivot = new Vector2(0.5f, 1f);
            tbrt.anchoredPosition = new Vector2(0f, 0f);
            tbrt.sizeDelta = new Vector2(0f, 34f);

            _inspectorTitleText = CreateText(titleBar.transform, "Inspector",
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(12f, 0f), new Vector2(-95f, 0f),
                13f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);
            _inspectorTitleText.enableWordWrapping = false;
            _inspectorTitleText.overflowMode = TextOverflowModes.Ellipsis;

            _inspectorExpandBtn = CreateButton(titleBar.transform, "Btn_ExpandInspector", "[⤢ Expand]", 80f, ToggleInspectorExpansion, new Color(0.20f, 0.23f, 0.28f, 1f));
            RectTransform ebrt = _inspectorExpandBtn.GetComponent<RectTransform>();
            ebrt.anchorMin = new Vector2(1f, 0.5f);
            ebrt.anchorMax = new Vector2(1f, 0.5f);
            ebrt.pivot = new Vector2(1f, 0.5f);
            ebrt.anchoredPosition = new Vector2(-8f, 0f);
            ebrt.sizeDelta = new Vector2(80f, 22f);
            _inspectorExpandBtnText = _inspectorExpandBtn.GetComponentInChildren<TMP_Text>();

            GameObject scrollObj = CreateScrollView(_inspectorPanel.transform, "Inspector_Scroll",
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(6f, 6f), new Vector2(-12f, -38f),
                out _inspectorContent);

            // 1. Transform Card
            GameObject transCard = CreateSectionCard(_inspectorContent, "Transform", "Transform & Alignment");
            CreateVector3Row(transCard.transform, "Position", out _posXInput, out _posYInput, out _posZInput, OnTransformInputChanged);
            CreateVector3Row(transCard.transform, "Rotation", out _rotXInput, out _rotYInput, out _rotZInput, OnTransformInputChanged);

            GameObject rotBtnRow = CreateRowContainer(transCard.transform, "Row_RotButtons", 26f);
            HorizontalLayoutGroup rhlg = rotBtnRow.AddComponent<HorizontalLayoutGroup>();
            rhlg.spacing = 6f; rhlg.childControlWidth = true; rhlg.childForceExpandWidth = true;

            CreateButton(rotBtnRow.transform, "Btn_Snap90", "Snap 90°", 120f, () =>
            {
                if (EditorSessionManager.SelectedObject != null)
                {
                    Vector3 e = EditorSessionManager.SelectedObject.transform.eulerAngles;
                    e.x = Mathf.Round(e.x / 90f) * 90f;
                    e.y = Mathf.Round(e.y / 90f) * 90f;
                    e.z = Mathf.Round(e.z / 90f) * 90f;
                    EditorSessionManager.SelectedObject.transform.rotation = Quaternion.Euler(e);
                    RefreshInspectorValues();
                    EditorSessionManager.ShowNotification("Rotation snapped to 90°");
                }
            });
            CreateButton(rotBtnRow.transform, "Btn_ResetRot", "Reset Rot", 120f, () =>
            {
                if (EditorSessionManager.SelectedObject != null)
                {
                    EditorSessionManager.SelectedObject.transform.rotation = Quaternion.identity;
                    RefreshInspectorValues();
                    EditorSessionManager.ShowNotification("Rotation reset to (0,0,0)");
                }
            });

            CreateSingleFloatRow(transCard.transform, "Scale", out _scaleInput, OnTransformInputChanged);

            // 2. Specialized Gameplay Cards
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
                if (_laserValueText != null) _laserValueText.text = $"{val:F0}°/s";
            });

            // 3. Merged Clean Lighting Card
            _lightSection = CreateSectionCard(_inspectorContent, "Lighting", "💡 Lighting Properties");

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
                    if (_lightAngleValText != null) _lightAngleValText.text = $"{val:F0}°";
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

            GameObject swatchObj = new GameObject("Swatch");
            swatchObj.transform.SetParent(swatchRow.transform, false);
            RectTransform swrt = swatchObj.AddComponent<RectTransform>();
            swrt.anchorMin = new Vector2(1f, 0.5f);
            swrt.anchorMax = new Vector2(1f, 0.5f);
            swrt.pivot = new Vector2(1f, 0.5f);
            swrt.anchoredPosition = new Vector2(-4f, 0f);
            swrt.sizeDelta = new Vector2(60f, 18f);

            _lightColorPreviewSwatch = swatchObj.AddComponent<Image>();
            _lightColorPreviewSwatch.color = Color.cyan;

            GameObject colorPresetsRow = CreateRowContainer(_lightSection.transform, "Row_ColorPresets", 26f);
            HorizontalLayoutGroup chlg = colorPresetsRow.AddComponent<HorizontalLayoutGroup>();
            chlg.spacing = 4f; chlg.childControlWidth = true; chlg.childForceExpandWidth = true;
            CreateButton(colorPresetsRow.transform, "Btn_Cyan", "Cyan", 40f, () => ApplyPresetColor(Color.cyan), Color.cyan);
            CreateButton(colorPresetsRow.transform, "Btn_Sun", "Gold", 40f, () => ApplyPresetColor(new Color(1f, 0.8f, 0.35f)), new Color(1f, 0.8f, 0.35f));
            CreateButton(colorPresetsRow.transform, "Btn_Green", "Acid", 40f, () => ApplyPresetColor(new Color(0.2f, 1f, 0.4f)), new Color(0.2f, 1f, 0.4f));
            CreateButton(colorPresetsRow.transform, "Btn_Red", "Red", 40f, () => ApplyPresetColor(new Color(1f, 0.2f, 0.2f)), new Color(1f, 0.2f, 0.2f));
            CreateButton(colorPresetsRow.transform, "Btn_White", "White", 40f, () => ApplyPresetColor(Color.white), Color.white);
            CreateButton(colorPresetsRow.transform, "Btn_Violet", "Violet", 40f, () => ApplyPresetColor(new Color(0.7f, 0.3f, 1f)), new Color(0.7f, 0.3f, 1f));

            // 4. Motion Path Card
            _motionPathSection = CreateSectionCard(_inspectorContent, "MotionPath", "Kinematic Motion Path");
            _motionPathStatusText = CreateText(_motionPathSection.transform, "No Path Bound", new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero, 11f, FontStyles.Normal, Color.gray, TextAlignmentOptions.MidlineLeft);
            LayoutElement mple = _motionPathStatusText.gameObject.AddComponent<LayoutElement>();
            mple.preferredHeight = 20f;

            GameObject motionBtnRow = CreateRowContainer(_motionPathSection.transform, "Row_MotionButtons", 26f);
            HorizontalLayoutGroup mhlg = motionBtnRow.AddComponent<HorizontalLayoutGroup>();
            mhlg.spacing = 6f; mhlg.childControlWidth = true; mhlg.childForceExpandWidth = true;

            CreateButton(motionBtnRow.transform, "Btn_SetA", "Lock Point A", 120f, () =>
            {
                if (EditorSessionManager.SelectedObject == null) return;
                GameObject obj = EditorSessionManager.SelectedObject;
                if (!EditorSessionManager.MotionPaths.ContainsKey(obj))
                {
                    EditorSessionManager.MotionPaths[obj] = new ObjectMotionPath { PointA = obj.transform.position, PointB = obj.transform.position + Vector3.up * 5f, Speed = 3.5f };
                }
                else
                {
                    EditorSessionManager.MotionPaths[obj].PointA = obj.transform.position;
                }
                RefreshInspectorValues();
                EditorSessionManager.ShowNotification("Point A locked!");
            });

            CreateButton(motionBtnRow.transform, "Btn_SetB", "Lock Point B", 120f, () =>
            {
                if (EditorSessionManager.SelectedObject == null) return;
                GameObject obj = EditorSessionManager.SelectedObject;
                if (!EditorSessionManager.MotionPaths.ContainsKey(obj))
                {
                    EditorSessionManager.MotionPaths[obj] = new ObjectMotionPath { PointA = obj.transform.position, PointB = obj.transform.position + Vector3.up * 5f, Speed = 3.5f };
                }
                else
                {
                    EditorSessionManager.MotionPaths[obj].PointB = obj.transform.position;
                }
                RefreshInspectorValues();
                EditorSessionManager.ShowNotification("Point B locked!");
            });

            // 5. Parenting Card
            _parentingSection = CreateSectionCard(_inspectorContent, "Parenting", "Assembly Parenting");
            GameObject parentBtnRow = CreateRowContainer(_parentingSection.transform, "Row_ParentButtons", 26f);
            HorizontalLayoutGroup phlg = parentBtnRow.AddComponent<HorizontalLayoutGroup>();
            phlg.spacing = 6f; phlg.childControlWidth = true; phlg.childForceExpandWidth = true;

            CreateButton(parentBtnRow.transform, "Btn_PickParent", "Pick Parent", 120f, () =>
            {
                if (EditorSessionManager.SelectedObject != null)
                {
                    EditorSessionManager.ParentingChildTarget = EditorSessionManager.SelectedObject;
                    EditorSessionManager.ShowNotification($"Selected '{EditorSessionManager.SelectedObject.name}'. Click target parent in Hierarchy.");
                }
            });

            CreateButton(parentBtnRow.transform, "Btn_Unparent", "Unparent", 120f, () =>
            {
                if (EditorSessionManager.SelectedObject != null && EditorSessionManager.SelectedObject.transform.parent != null)
                {
                    GameObject oldP = EditorSessionManager.SelectedObject.transform.parent.gameObject;
                    EditorSessionManager.SelectedObject.transform.SetParent(null, true);
                    EditorSessionManager.RecalculateParentChildCount(oldP);
                    RefreshHierarchy();
                    EditorSessionManager.ShowNotification("Object unparented to root.");
                }
            });
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
                _inspectorExpandBtnText.text = _isInspectorExpanded ? "[⤡ Slim]" : "[⤢ Expand]";
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
            abrt.offsetMin = new Vector2(250f, 0f);
            abrt.offsetMax = new Vector2(-290f, 210f);

            Image abImg = _assetBrowserPanel.AddComponent<Image>();
            abImg.color = new Color(0.10f, 0.11f, 0.13f, 0.98f);

            GameObject topTabs = new GameObject("Browser_Tabs");
            topTabs.transform.SetParent(_assetBrowserPanel.transform, false);
            RectTransform ttrt = topTabs.AddComponent<RectTransform>();
            ttrt.anchorMin = new Vector2(0f, 1f);
            ttrt.anchorMax = new Vector2(1f, 1f);
            ttrt.pivot = new Vector2(0.5f, 1f);
            ttrt.sizeDelta = new Vector2(0f, 30f);
            ttrt.anchoredPosition = new Vector2(0f, 0f);

            HorizontalLayoutGroup thlg = topTabs.AddComponent<HorizontalLayoutGroup>();
            thlg.padding = new RectOffset(8, 8, 4, 4);
            thlg.spacing = 5f;
            thlg.childControlWidth = false;
            thlg.childForceExpandWidth = false;

            string[] categories = new string[] { "Architecture", "Gameplay", "Hazards", "All" };
            for (int i = 0; i < categories.Length; i++)
            {
                string cat = categories[i];
                CreateButton(topTabs.transform, "Tab_" + cat, cat, 120f, () =>
                {
                    _activeBrowserCategory = cat;
                    RefreshAssetBrowser();
                });
            }

            _browserSearchInput = CreateInputField(_assetBrowserPanel.transform, "BrowserSearch", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-105f, -15f), new Vector2(190f, 22f), "Filter catalog...", (s) => RefreshAssetBrowser());

            GameObject scrollObj = new GameObject("Browser_Scroll");
            scrollObj.transform.SetParent(_assetBrowserPanel.transform, false);
            RectTransform srt = scrollObj.AddComponent<RectTransform>();
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = new Vector2(1f, 1f);
            srt.offsetMin = new Vector2(8f, 6f);
            srt.offsetMax = new Vector2(-8f, -32f);

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
            _browserContent.anchoredPosition = Vector2.zero;

            GridLayoutGroup glg = content.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(95f, 100f);
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

        public static void RefreshAssetBrowser()
        {
            if (_browserContent == null) return;

            for (int i = 0; i < _browserCards.Count; i++)
            {
                if (_browserCards[i] != null) GameObject.Destroy(_browserCards[i]);
            }
            _browserCards.Clear();

            string filter = (_browserSearchInput != null && !string.IsNullOrEmpty(_browserSearchInput.text))
                ? _browserSearchInput.text.ToLower() : "";

            for (int i = 0; i < EditorSessionManager.AllAssets.Count; i++)
            {
                CatalogAsset asset = EditorSessionManager.AllAssets[i];
                if (asset == null || asset.SourceTemplate == null) continue;

                if (!string.IsNullOrEmpty(filter) && !asset.DisplayName.ToLower().Contains(filter))
                    continue;

                if (_activeBrowserCategory == "Architecture")
                {
                    bool isArch = asset.Category == AssetCategory.Building ||
                                  asset.SubCategory.Equals("Architecture", StringComparison.OrdinalIgnoreCase) ||
                                  asset.SubCategory.Equals("Platforms", StringComparison.OrdinalIgnoreCase);

                    if (asset.IsJumper || asset.IsCheckPoint || asset.IsSpawnGate || asset.IsGoalGate ||
                        asset.IsLaser || asset.IsRotatingLaser || asset.IsTurret || asset.IsHelix)
                    {
                        isArch = false;
                    }

                    if (!isArch) continue;
                }
                else if (_activeBrowserCategory == "Gameplay")
                {
                    bool isGame = asset.IsJumper || asset.IsCheckPoint || asset.IsSpawnGate || asset.IsGoalGate ||
                                  asset.IsSpotlight || asset.IsSunlight ||
                                  asset.SubCategory.Equals("Gameplay", StringComparison.OrdinalIgnoreCase);

                    if (!isGame) continue;
                }
                else if (_activeBrowserCategory == "Hazards")
                {
                    bool isHazard = asset.IsLaser || asset.IsRotatingLaser || asset.IsTurret || asset.IsHelix ||
                                    asset.SubCategory.Equals("Hazards", StringComparison.OrdinalIgnoreCase);

                    if (!isHazard) continue;
                }

                GameObject card = new GameObject("Card_" + asset.DisplayName);
                card.transform.SetParent(_browserContent, false);
                RectTransform crt = card.AddComponent<RectTransform>();
                crt.sizeDelta = new Vector2(95f, 100f);

                Image bg = card.AddComponent<Image>();
                bg.color = new Color(0.16f, 0.18f, 0.22f, 0.95f);

                Button btn = card.AddComponent<Button>();
                CatalogAsset capturedAsset = asset;
                btn.onClick.AddListener((Action)(() => EditorSessionManager.EquipAsset(capturedAsset)));

                GameObject preview = new GameObject("Thumbnail");
                preview.transform.SetParent(card.transform, false);
                RectTransform prt = preview.AddComponent<RectTransform>();
                prt.anchorMin = new Vector2(0.08f, 0.28f);
                prt.anchorMax = new Vector2(0.92f, 0.95f);
                prt.sizeDelta = Vector2.zero;
                Image pImg = preview.AddComponent<Image>();

                if (asset.ThumbnailSprite == null)
                {
                    asset.ThumbnailSprite = AssetThumbnailRenderer.GenerateThumbnail(asset);
                }

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

                GameObject labelObj = new GameObject("Label");
                labelObj.transform.SetParent(card.transform, false);
                RectTransform lrt = labelObj.AddComponent<RectTransform>();
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = new Vector2(1f, 0.3f);
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

        private static void ApplyPresetColor(Color c)
        {
            if (EditorSessionManager.SelectedObject == null) return;
            if (EditorSessionManager.PlacedLights.TryGetValue(EditorSessionManager.SelectedObject, out var cfg))
            {
                cfg.Color = c;
                EditorSessionManager.ApplyLightConfig(EditorSessionManager.SelectedObject, cfg);
                if (_lightColorPreviewSwatch != null) _lightColorPreviewSwatch.color = c;
                EditorSessionManager.ShowNotification("Applied light preset.");
            }
        }

        private static void BuildToastOverlay()
        {
            GameObject toastObj = CreatePanel(_canvasRoot.transform, "Toast_Overlay", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 225f), new Vector2(480f, 26f), new Color(0.08f, 0.10f, 0.12f, 0.90f));
            _toastText = CreateText(toastObj.transform, "Studio Editor Ready", new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero, 12f, FontStyles.Bold, new Color(0.2f, 0.9f, 1.0f), TextAlignmentOptions.Center);
        }

        // =========================================================================
        // HIERARCHY REFRESH (TREE VIEW: PARENT-CHILD BRANCHES + EXPAND/COLLAPSE)
        // =========================================================================

        public static void RefreshHierarchy()
        {
            EnsureSelectableColliders();

            if (_hierarchyContent == null) return;

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
            rt.sizeDelta = new Vector2(0f, 24f);

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 24f;
            le.minHeight = 24f;
            le.flexibleHeight = 0f;
            le.flexibleWidth = 1f;

            bool isSelected = (EditorSessionManager.SelectedObjects != null && EditorSessionManager.SelectedObjects.Contains(node)) ||
                              (EditorSessionManager.SelectedObject == node);

            Image bg = row.AddComponent<Image>();
            bg.color = isSelected ? new Color(0.18f, 0.52f, 0.88f, 0.95f) :
                       (hasChildren ? new Color(0.16f, 0.18f, 0.22f, 0.80f) : new Color(0.11f, 0.12f, 0.14f, 0.60f));

            float leftPadding = 8f + (depth * 18f);

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
                ft.text = isCollapsed ? "►" : "▼";
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

            Button b = row.AddComponent<Button>();
            GameObject captured = node;
            b.onClick.AddListener((Action)(() =>
            {
                if (EditorSessionManager.ParentingChildTarget != null && EditorSessionManager.ParentingChildTarget != captured)
                {
                    EditorSessionManager.ParentingChildTarget.transform.SetParent(captured.transform, true);
                    EditorSessionManager.RecalculateParentChildCount(captured);
                    EditorSessionManager.ShowNotification($"Linked '{EditorSessionManager.ParentingChildTarget.name}' -> '{captured.name}'");
                    EditorSessionManager.ParentingChildTarget = null;
                    RefreshHierarchy();
                }
                else
                {
                    bool isCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                    EditorSessionManager.SelectObject(captured, isAdditive: isCtrl);
                }
            }));

            string treeBranch = (depth > 0) ? "└── " : "";
            string parentBadge = hasChildren ? $" ({childrenMap[node].Count})" : "";
            string displayName = treeBranch + captured.name + parentBadge;

            TMP_Text rowText = CreateText(row.transform, displayName,
                new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(leftPadding, 0f), new Vector2(-4f, 0f),
                11f, hasChildren ? FontStyles.Bold : FontStyles.Normal,
                hasChildren ? new Color(0.9f, 0.95f, 1f) : Color.white,
                TextAlignmentOptions.MidlineLeft);
            rowText.enableWordWrapping = false;
            rowText.overflowMode = TextOverflowModes.Ellipsis;

            _hierarchyRows.Add(row);

            if (hasChildren && !isCollapsed)
            {
                for (int c = 0; c < childrenMap[node].Count; c++)
                {
                    RenderHierarchyTreeNode(childrenMap[node][c], depth + 1, childrenMap, searchFilter);
                }
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

            RefreshHierarchy();
            RefreshInspectorValues();
        }

        // =========================================================================
        // INSPECTOR VALUES REFRESH (ZERO OVERLAPS, AUTO-EXPANDING)
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
                if (_scaleInput != null) _scaleInput.text = "1";

                if (_jumperSection != null) _jumperSection.SetActive(false);
                if (_turbineSection != null) _turbineSection.SetActive(false);
                if (_turretSection != null) _turretSection.SetActive(false);
                if (_laserSection != null) _laserSection.SetActive(false);
                if (_lightSection != null) _lightSection.SetActive(false);
                if (_motionPathSection != null) _motionPathSection.SetActive(false);
                if (_parentingSection != null) _parentingSection.SetActive(false);
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

            if (_scaleInput != null) _scaleInput.text = scl.x.ToString("F2", CultureInfo.InvariantCulture);

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
                    if (_laserValueText != null) _laserValueText.text = $"{speed:F0}°/s";
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
                    if (_lightAngleSlider != null) { _lightAngleSlider.value = cfg.SpotAngle; _lightAngleValText.text = $"{cfg.SpotAngle:F0}°"; }
                    if (_lightVolSlider != null) { _lightVolSlider.value = cfg.VolumetricIntensity; _lightVolValText.text = $"{cfg.VolumetricIntensity:F1}"; }
                    if (_lightColorPreviewSwatch != null) { _lightColorPreviewSwatch.color = cfg.Color; }
                }
            }

            if (_motionPathSection != null)
            {
                _motionPathSection.SetActive(true);
                if (EditorSessionManager.MotionPaths.ContainsKey(obj))
                {
                    var p = EditorSessionManager.MotionPaths[obj];
                    if (_motionPathStatusText != null) _motionPathStatusText.text = $"Path: {p.Speed:F1} m/s (Active)";
                }
                else
                {
                    if (_motionPathStatusText != null) _motionPathStatusText.text = "No Path Bound (Stationary)";
                }
            }

            if (_parentingSection != null)
            {
                _parentingSection.SetActive(true);
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

            float sc = ParseFloat(_scaleInput?.text, obj.transform.localScale.x);

            obj.transform.position = new Vector3(x, y, z);
            obj.transform.rotation = Quaternion.Euler(rx, ry, rz);
            obj.transform.localScale = Vector3.one * Mathf.Max(0.01f, sc);

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
        // UGUI FACTORY HELPERS (EXPLICIT HEIGHTS, ZERO OVERLAPS)
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
                LayoutElement le = obj.AddComponent<LayoutElement>();
                le.preferredWidth = width;
                le.flexibleWidth = 0;
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

            CreateText(obj.transform, label, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 11f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);

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

        /// <summary>
        /// Generates a clean, fully constrained uGUI Slider with a slim background track, cyan fill, and white handle.
        /// Zero background overflows or oversized rect bounds.
        /// </summary>
        private static GameObject CreateInspectorSliderRow(Transform parent, string label, out Slider slider, out TMP_Text valText, float minVal, float maxVal, Action<float> onSliderChanged)
        {
            GameObject row = CreateRowContainer(parent, "Row_Slider_" + label, 24f);

            // Label text on the left
            CreateText(row.transform, label, new Vector2(0f, 0f), new Vector2(0.32f, 1f), new Vector2(4f, 0f), Vector2.zero, 10f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);

            // Value text on the right
            valText = CreateText(row.transform, "0", new Vector2(0.80f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-4f, 0f), 10f, FontStyles.Bold, new Color(0.2f, 0.85f, 1f), TextAlignmentOptions.MidlineRight);

            // Slider container (33% to 78% width)
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

            // 1. Slim Background Track
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

            // 2. Fill Area
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

            // 3. Handle Knob
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
                    _saveBtnText.text = "✓ SAVED!";
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
            nt.text = ">"; nt.fontSize = 22f; nt.alignment = TextAlignmentOptions.Center; nt.color = Color.white;
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
            TMP_Text[] allTmps = Resources.FindObjectsOfTypeAll<TMP_Text>();
            bool replaced = false;

            for (int i = 0; i < allTmps.Length; i++)
            {
                TMP_Text tmp = allTmps[i];
                if (tmp == null || !tmp.gameObject.scene.isLoaded || !tmp.gameObject.activeInHierarchy) continue;
                if (tmp.GetComponentInParent<LogsMenu>() != null) continue;

                string t = tmp.text.Trim().ToLower();
                if (t == "logs" || t == "log" || t == "archives" || t == "codex" || t == "records")
                {
                    tmp.text = "Level Editor";
                    DisableLocalizationScripts(tmp.gameObject);
                    replaced = true;
                }
            }

            return replaced;
        }

        public static void OpenNativeMenu()
        {
            LogsMenu[] allMenus = Resources.FindObjectsOfTypeAll<LogsMenu>();
            for (int i = 0; i < allMenus.Length; i++)
            {
                LogsMenu target = allMenus[i];
                if (target == null || !target.gameObject.scene.isLoaded) continue;

                MenuGroupScript targetGroup = target.GetComponentInParent<MenuGroupScript>();
                if (targetGroup != null)
                {
                    if (MenuGroupScript.CurrentGroup != null && MenuGroupScript.CurrentGroup != targetGroup)
                        MenuGroupScript.CurrentGroup.Close();
                    targetGroup.Open();
                    return;
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
            _statsLabelLeft.text = $"• TOTAL OBJECTS: <b><color=#00E5FF>{objectCount}</color></b>\n• HAZARDS & LASERS: <b><color=#FF5252>{lasers}</color></b>\n• JUMP PADS: <b><color=#FFEB3B>{jumpers}</color></b>";
            _statsLabelRight.text = $"• MOVING PATHS: <b><color=#E040FB>{paths}</color></b>\n• TURRET ENEMIES: <b><color=#FF4081>{turrets}</color></b>\n• LAST SAVED: <color=#B0BEC5>{mod:dd/MM/yyyy HH:mm}</color>";
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

            Component[] comps = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < comps.Length; i++)
            {
                Component c = comps[i];
                if (c == null) continue;
                string typeName = c.GetIl2CppType().Name;
                if (typeName == "TextLabel" || typeName.Contains("Translate") || typeName.Contains("Localization"))
                {
                    MonoBehaviour mb = c.TryCast<MonoBehaviour>();
                    if (mb != null) mb.enabled = false;
                }
            }
        }
    }

    // =========================================================================
    // SECTION 3: MELONMOD ENTRY POINT & MAIN RUNTIME LOOP
    // =========================================================================

    public class DeadCoreLevelEditorMod : MelonMod
    {
        private static LogsMenu _lastTransformedLogsMenu = null;
        private static LogsMenu _cachedActiveMenu = null;
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

                if (_cachedActiveMenu == null || !_cachedActiveMenu.gameObject.scene.isLoaded || !_cachedActiveMenu.gameObject.activeInHierarchy)
                {
                    _cachedActiveMenu = GameObject.FindObjectOfType<LogsMenu>();
                    if (_cachedActiveMenu == null)
                    {
                        LogsMenu[] menus = Resources.FindObjectsOfTypeAll<LogsMenu>();
                        for (int i = 0; i < menus.Length; i++)
                        {
                            if (menus[i] != null && menus[i].gameObject.scene.isLoaded && menus[i].gameObject.activeInHierarchy)
                            {
                                _cachedActiveMenu = menus[i];
                                break;
                            }
                        }
                    }
                }

                LogsMenu activeMenu = _cachedActiveMenu;
                if (activeMenu != null)
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