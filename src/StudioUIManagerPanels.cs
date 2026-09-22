using System;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;

namespace DeadCoreEditor
{
    public static partial class StudioUIManager
    {
        // =========================================================================
        // STATE DECLARATIONS
        // =========================================================================

        private static float _browserScrollTarget = 1.0f;
        private static bool _isBrowserTargetInitialized = false;

        // Hierarchy
        private static GameObject _hierarchyPanel = null;
        private static ScrollRect _hierarchyScrollRect = null;
        private static RectTransform _hierarchyContent = null;
        private static TMP_InputField _hierarchySearchInput = null;
        private static readonly List<GameObject> _hierarchyRows = new List<GameObject>();
        private static readonly HashSet<GameObject> _collapsedParents = new HashSet<GameObject>();
        public static readonly HashSet<GameObject> LockedObjects = new HashSet<GameObject>();
        private static string _hierarchyFilterType = "ALL";
        private static TMP_Text _hierarchyBudgetGaugeText = null;

        private static readonly Dictionary<GameObject, GameObject> _targetToRowMap = new Dictionary<GameObject, GameObject>();
        private static readonly Dictionary<GameObject, GameObject> _rowToTargetMap = new Dictionary<GameObject, GameObject>();
        private static GameObject _dragCandidateNode = null;
        private static Vector2 _dragStartMousePos = Vector2.zero;
        private static bool _isDraggingHierarchyNode = false;
        private static GameObject _dragGhostObj = null;
        private static TMP_Text _dragGhostText = null;

        // Modular Inspector
        private static GameObject _inspectorPanel = null;
        private static RectTransform _inspectorPanelRt = null;
        private static RectTransform _inspectorContent = null;
        private static TMP_Text _inspectorTitleText = null;
        private static Button _inspectorExpandBtn = null;
        private static TMP_Text _inspectorExpandBtnText = null;
        private static bool _isInspectorExpanded = false;
        private static readonly List<GameObject> _activeInspectorCards = new List<GameObject>();

        private static TMP_InputField _posXInput = null;
        private static TMP_InputField _posYInput = null;
        private static TMP_InputField _posZInput = null;
        private static TMP_InputField _rotXInput = null;
        private static TMP_InputField _rotYInput = null;
        private static TMP_InputField _rotZInput = null;
        private static TMP_InputField _scaleXInput = null;
        private static TMP_InputField _scaleYInput = null;
        private static TMP_InputField _scaleZInput = null;
        private static bool _useWorldCoordinates = false;
        private static TMP_Text _coordSpaceToggleText = null;

        // Asset Browser
        private static GameObject _assetBrowserPanel = null;
        private static ScrollRect _assetBrowserScrollRect = null;
        private static RectTransform _browserContent = null;
        private static TMP_InputField _browserSearchInput = null;
        private static string _activeBrowserCategory = "All";
        private static CatalogAsset _assignTargetAsset = null;
        private static AssetSizeTier _activeSizeFilter = AssetSizeTier.All;
        private static int _browserSortMode = 0;
        private static TMP_Text _sortSizeBtnText = null;
        private static bool _includeTinyProps = false;
        private static TMP_Text _toggleTinyPropsText = null;
        private static readonly List<GameObject> _browserCards = new List<GameObject>();

        // Context Menu
        private static GameObject _contextMenuRoot = null;
        private static CatalogAsset _contextTargetAsset = null;

        // Movable Windows
        private static StudioFloatingWindow _preferencesWin = null;
        private static StudioFloatingWindow _occurrencesWin = null;
        private static StudioFloatingWindow _uniformScalerWin = null;
        private static StudioFloatingWindow _assignCategoryWin = null;
        private static StudioFloatingWindow _batchRenamerWin = null;
        private static StudioFloatingWindow _distributeSpacingWin = null;

        // Preferences Tabs
        private static int _activePrefTab = 0;
        private static readonly List<Button> _prefTabButtons = new List<Button>();
        private static readonly List<GameObject> _prefTabPages = new List<GameObject>();
        private static TMP_InputField _prefShortcutSearchInput = null;
        private static string _prefShortcutSearchFilter = "";

        // Occurrences
        private static RectTransform _occurrencesContent = null;
        private static TMP_Text _occurrencesTitleHeader = null;
        private static string _currentOccurrencesTargetName = "";
        private static readonly List<GameObject> _currentOccurrencesList = new List<GameObject>();

        // Scaler
        private static float _uniformScaleValue = 1.0f;
        private static bool _scaleCentroidPivot = true;

        // Matrix Cloner / Spacing
        private static int _arrayCountX = 3;
        private static int _arrayCountZ = 3;
        private static float _arraySpacingX = 4.0f;
        private static float _arraySpacingZ = 4.0f;

        // Category Assign
        private static TMP_InputField _assignCategoryInput = null;
        private static RectTransform _existingCategoryChipsRoot = null;

        // Renamer
        private static TMP_InputField _batchRenameInput = null;
        private static TMP_InputField _replaceFindInput = null;
        private static TMP_InputField _replaceWithInput = null;

        // Rebinder
        private static string _activeRebindingActionKey = null;
        private static TMP_Text _activeRebindLabel = null;
        private static readonly Dictionary<string, TMP_Text> _shortcutDisplayLabels = new Dictionary<string, TMP_Text>();

        private static bool _suppressInspectorCallbacks = false;

        // =========================================================================
        // TICK UPDATE & POINTER DETECTION
        // =========================================================================

        public static void UpdateUI()
        {
            float dt = Time.unscaledDeltaTime;
            UpdateRebindingTick();
            UpdateNotificationBannerTick(dt);
            UpdateAutoSaveTick(dt);

            if (_assetBrowserScrollRect != null && _assetBrowserPanel != null && _assetBrowserPanel.activeInHierarchy)
            {
                if (!_isBrowserTargetInitialized)
                {
                    _browserScrollTarget = Mathf.Clamp01(_assetBrowserScrollRect.verticalNormalizedPosition);
                    _isBrowserTargetInitialized = true;
                }

                Vector2 mousePos = Input.mousePosition;
                RectTransform abrt = _assetBrowserPanel.GetComponent<RectTransform>();
                if (abrt != null && RectTransformUtility.RectangleContainsScreenPoint(abrt, mousePos))
                {
                    float scrollWheel = Input.GetAxis("Mouse ScrollWheel");
                    if (Mathf.Abs(scrollWheel) > 0.001f)
                    {
                        float contentH = _browserContent != null ? _browserContent.rect.height : 1000f;
                        float viewH = (_assetBrowserScrollRect.viewport != null) ? _assetBrowserScrollRect.viewport.rect.height : 220f;
                        float scrollableH = Mathf.Max(120f, contentH - viewH);

                        float step = (65f / scrollableH) * (scrollWheel > 0f ? 1f : -1f);
                        _browserScrollTarget = Mathf.Clamp01(_browserScrollTarget + step);
                    }
                }

                if (Mathf.Abs(_assetBrowserScrollRect.verticalNormalizedPosition - _browserScrollTarget) > 0.0005f)
                {
                    _assetBrowserScrollRect.verticalNormalizedPosition = Mathf.Lerp(
                        _assetBrowserScrollRect.verticalNormalizedPosition,
                        _browserScrollTarget,
                        dt * 16f
                    );
                }
            }

            if (_activeDropdownMenu != null && Input.GetMouseButtonDown(0))
            {
                RectTransform dmRt = _activeDropdownMenu.GetComponent<RectTransform>();
                if (dmRt != null && !RectTransformUtility.RectangleContainsScreenPoint(dmRt, Input.mousePosition))
                {
                    if (_activeDropdownAnchor == null || !RectTransformUtility.RectangleContainsScreenPoint(_activeDropdownAnchor, Input.mousePosition))
                    {
                        CloseAllDropdowns();
                    }
                }
            }

            if (_contextMenuRoot != null && _contextMenuRoot.activeSelf && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
            {
                RectTransform cmRt = _contextMenuRoot.GetComponent<RectTransform>();
                if (cmRt != null && !RectTransformUtility.RectangleContainsScreenPoint(cmRt, Input.mousePosition))
                {
                    CloseContextMenu();
                }
            }
        }

        public static bool IsPointerOverUI()
        {
            if (_canvasRoot == null || !_canvasRoot.activeInHierarchy) return false;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return true;

            Vector2 mousePos = Input.mousePosition;

            if (_activeDropdownMenu != null && _activeDropdownMenu.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(_activeDropdownMenu.GetComponent<RectTransform>(), mousePos))
                return true;

            if (_contextMenuRoot != null && _contextMenuRoot.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(_contextMenuRoot.GetComponent<RectTransform>(), mousePos))
                return true;

            if (IsWindowHovered(_preferencesWin, mousePos) ||
                IsWindowHovered(_occurrencesWin, mousePos) ||
                IsWindowHovered(_uniformScalerWin, mousePos) ||
                IsWindowHovered(_assignCategoryWin, mousePos) ||
                IsWindowHovered(_batchRenamerWin, mousePos) ||
                IsWindowHovered(_distributeSpacingWin, mousePos))
                return true;

            if (_modeTogglePillBtn != null && _modeTogglePillBtn.gameObject.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(_modeTogglePillBtn.GetComponent<RectTransform>(), mousePos))
                return true;

            if (_floatingCategoryDock != null && _floatingCategoryDock.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(_floatingCategoryDock.GetComponent<RectTransform>(), mousePos))
                return true;

            if (_assetBrowserPanel != null && _assetBrowserPanel.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(_assetBrowserPanel.GetComponent<RectTransform>(), mousePos))
                return true;

            if (_hierarchyPanel != null && _hierarchyPanel.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(_hierarchyPanel.GetComponent<RectTransform>(), mousePos))
                return true;

            if (_inspectorPanel != null && _inspectorPanel.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(_inspectorPanel.GetComponent<RectTransform>(), mousePos))
                return true;

            if (_toolbarPanel != null && _toolbarPanel.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(_toolbarPanel.GetComponent<RectTransform>(), mousePos))
                return true;

            return false;
        }

        private static bool IsWindowHovered(StudioFloatingWindow win, Vector2 mousePos)
        {
            return win != null && win.WindowRoot != null && win.WindowRoot.activeInHierarchy &&
                   RectTransformUtility.RectangleContainsScreenPoint(win.RootRt, mousePos);
        }

        // =========================================================================
        // UPGRADED TABBED PREFERENCES WINDOW (1.8X LARGER, STRICT LAYOUT)
        // =========================================================================

        private static void BuildPreferencesWindow()
        {
            if (_preferencesWin != null && _preferencesWin.WindowRoot != null)
            {
                GameObject.DestroyImmediate(_preferencesWin.WindowRoot);
                _preferencesWin = null;
            }

            // Window increased by ~1.8x to 940 x 680
            _preferencesWin = StudioFloatingWindow.Create(_canvasRoot.transform, "Win_Preferences", "Preferences & Configuration", new Vector2(940f, 680f), Vector2.zero);

            // 1. Top Sub-Tabs Navigation Bar (Pinned to the top)
            GameObject tabNav = new GameObject("Pref_TabBar", Il2CppType.Of<RectTransform>());
            tabNav.transform.SetParent(_preferencesWin.ContentRt, false);

            RectTransform tnRt = tabNav.GetComponent<RectTransform>();
            tnRt.anchorMin = new Vector2(0f, 1f);
            tnRt.anchorMax = new Vector2(1f, 1f);
            tnRt.pivot = new Vector2(0.5f, 1f);
            tnRt.anchoredPosition = new Vector2(0f, 0f);
            tnRt.sizeDelta = new Vector2(0f, 36f);

            HorizontalLayoutGroup tnhlg = tabNav.AddComponent<HorizontalLayoutGroup>();
            tnhlg.padding = new RectOffset(2, 2, 2, 2);
            tnhlg.spacing = 6f;
            tnhlg.childControlWidth = true;
            tnhlg.childControlHeight = true;
            tnhlg.childForceExpandWidth = true;
            tnhlg.childForceExpandHeight = true;

            _prefTabButtons.Clear();
            _prefTabPages.Clear();

            string[] tabNames = new string[] { "Viewport & Camera", "Snapping & Gizmos", "Keyboard Hotkeys", "Auto-Save & Safety" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                int captureIdx = i;
                Button tb = CreateButtonPrimitive(tabNav.transform, "BtnPrefTab_" + i, tabNames[i], 180f, () => SwitchPreferencesTab(captureIdx), new Color(0.14f, 0.17f, 0.22f));
                _prefTabButtons.Add(tb);
            }

            // 2. Viewport & Cam Page (Tab 0)
            GameObject pageCam = CreatePreferencesPage("Page_Cam", out RectTransform contentCam);
            BuildPreferencesPageCamera(contentCam);
            _prefTabPages.Add(pageCam);

            // 3. Snapping & Gizmos Page (Tab 1)
            GameObject pageGizmo = CreatePreferencesPage("Page_Gizmo", out RectTransform contentGizmo);
            BuildPreferencesPageGizmo(contentGizmo);
            _prefTabPages.Add(pageGizmo);

            // 4. Hotkeys Page (Tab 2)
            GameObject pageKeys = CreatePreferencesPage("Page_Keys", out RectTransform contentKeys);
            BuildPreferencesPageHotkeys(contentKeys);
            _prefTabPages.Add(pageKeys);

            // 5. Auto-Save & Safety Page (Tab 3)
            GameObject pageSave = CreatePreferencesPage("Page_Save", out RectTransform contentSave);
            BuildPreferencesPageSafety(contentSave);
            _prefTabPages.Add(pageSave);

            // 6. Bottom Master Actions Bar (Pinned to the bottom)
            GameObject bottomBar = new GameObject("Pref_BottomActions", Il2CppType.Of<RectTransform>());
            bottomBar.transform.SetParent(_preferencesWin.ContentRt, false);

            RectTransform bbRt = bottomBar.GetComponent<RectTransform>();
            bbRt.anchorMin = new Vector2(0f, 0f);
            bbRt.anchorMax = new Vector2(1f, 0f);
            bbRt.pivot = new Vector2(0.5f, 0f);
            bbRt.anchoredPosition = new Vector2(0f, 0f);
            bbRt.sizeDelta = new Vector2(0f, 38f);

            HorizontalLayoutGroup bbhlg = bottomBar.AddComponent<HorizontalLayoutGroup>();
            bbhlg.padding = new RectOffset(4, 4, 2, 2);
            bbhlg.spacing = 10f;
            bbhlg.childControlWidth = false;
            bbhlg.childControlHeight = true;
            bbhlg.childForceExpandWidth = false;
            bbhlg.childForceExpandHeight = true;

            CreateButtonPrimitive(bottomBar.transform, "Btn_ResetAllPrefs", "Reset All Preferences to Vanilla Defaults", 320f, () =>
            {
                EditorConfigService.ResetToDefaults();
                SetNotificationText("All preferences restored to vanilla defaults.");
                _preferencesWin.Hide();
                BuildPreferencesWindow();
                _preferencesWin.Show();
                RefreshAssetBrowser();
            }, new Color(0.65f, 0.2f, 0.2f, 1f));

            GameObject spacer = new GameObject("Spacer", Il2CppType.Of<RectTransform>());
            spacer.transform.SetParent(bottomBar.transform, false);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

            CreateButtonPrimitive(bottomBar.transform, "Btn_SaveClosePrefs", "Save & Close", 180f, () =>
            {
                EditorConfigService.SaveConfig();
                _preferencesWin.Hide();
                SetNotificationText("Preferences saved.");
            }, new Color(0.18f, 0.65f, 0.35f, 1f));

            SwitchPreferencesTab(0);
        }

        private static GameObject CreatePreferencesPage(string name, out RectTransform contentRt)
        {
            GameObject pageObj = new GameObject(name, Il2CppType.Of<RectTransform>());
            pageObj.transform.SetParent(_preferencesWin.ContentRt, false);

            RectTransform prt = pageObj.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            // Offsets cleanly preserve 42px for the top tabs and 46px for the bottom buttons
            prt.offsetMin = new Vector2(2f, 46f);
            prt.offsetMax = new Vector2(-2f, -42f);

            CreateScrollViewPrimitive(pageObj.transform, "Scroll",
                Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero,
                out contentRt);

            return pageObj;
        }

        private static void SwitchPreferencesTab(int tabIndex)
        {
            _activePrefTab = tabIndex;

            for (int i = 0; i < _prefTabPages.Count; i++)
            {
                bool isActive = (i == tabIndex);
                if (_prefTabPages[i] != null)
                {
                    _prefTabPages[i].SetActive(isActive);
                }

                if (i < _prefTabButtons.Count && _prefTabButtons[i] != null)
                {
                    // Safe direct Image lookup prevents IL2CPP NullReferenceException
                    Image btnImg = _prefTabButtons[i].GetComponent<Image>();
                    if (btnImg != null)
                    {
                        btnImg.color = isActive ? new Color(0.18f, 0.52f, 0.92f, 0.98f) : new Color(0.14f, 0.17f, 0.22f, 0.90f);
                    }

                    TMP_Text txt = _prefTabButtons[i].GetComponentInChildren<TMP_Text>();
                    if (txt != null)
                    {
                        txt.color = isActive ? Color.white : new Color(0.75f, 0.80f, 0.88f);
                        txt.fontStyle = isActive ? FontStyles.Bold : FontStyles.Normal;
                    }
                }
            }
        }

        private static void BuildPreferencesPageCamera(RectTransform content)
        {
            var cfg = EditorConfigService.Config;

            var flyCard = CreateModularSection(content, "Flycam", "Viewport Camera & Flight Controls");

            AddToggleRow(flyCard.transform, "Require Right-Click to Fly (Protects W, A, S, D, Q, E)", cfg.RequireRmbForFlight, (val) =>
            {
                cfg.RequireRmbForFlight = val;
                EditorConfigService.SaveConfig();
            });

            AddToggleRow(flyCard.transform, "Smooth Damped Flycam", cfg.SmoothFlycam, (val) =>
            {
                cfg.SmoothFlycam = val;
                EditorConfigService.SaveConfig();
            });

            AddSliderRow(flyCard.transform, "Flycam Damping Smoothing", 4f, 25f, cfg.FlycamSmoothing, "{0:F0}", (val) =>
            {
                cfg.FlycamSmoothing = val;
                EditorConfigService.SaveConfig();
            });

            AddSliderRow(flyCard.transform, "Base Flycam Speed (m/s)", 6f, 80f, cfg.FlycamSpeed, "{0:F0} m/s", (val) =>
            {
                cfg.FlycamSpeed = val;
                EditorConfigService.SaveConfig();
            });

            AddSliderRow(flyCard.transform, "Shift Fast Boost Multiplier", 1.5f, 8.0f, cfg.FastCamMultiplier, "{0:F1}x", (val) =>
            {
                cfg.FastCamMultiplier = val;
                EditorConfigService.SaveConfig();
            });

            AddSliderRow(flyCard.transform, "Ctrl Slow Precision Multiplier", 0.05f, 0.5f, cfg.SlowCamMultiplier, "{0:F2}x", (val) =>
            {
                cfg.SlowCamMultiplier = val;
                EditorConfigService.SaveConfig();
            });

            AddSliderRow(flyCard.transform, "Mouse Look Sensitivity", 0.5f, 6.0f, cfg.MouseSensitivity, "{0:F1}x", (val) =>
            {
                cfg.MouseSensitivity = val;
                EditorConfigService.SaveConfig();
            });

            AddToggleRow(flyCard.transform, "Invert Look Y-Axis", cfg.InvertLookY, (val) =>
            {
                cfg.InvertLookY = val;
                EditorConfigService.SaveConfig();
            });

            AddSliderRow(flyCard.transform, "Viewport FOV", 45f, 105f, cfg.EditorFov, "{0:F0} deg", (val) =>
            {
                cfg.EditorFov = val;
                if (EditorViewportCamera.ViewportCamera != null) EditorViewportCamera.ViewportCamera.fieldOfView = val;
                EditorConfigService.SaveConfig();
            });

            var displayCard = CreateModularSection(content, "Display", "Catalog Palette Display");

            AddSliderRow(displayCard.transform, "Min Asset Filter Cutoff (m)", 0.2f, 4.0f, cfg.MinAssetSize, "{0:F1}m", (val) =>
            {
                cfg.MinAssetSize = (float)Math.Round(val, 1);
                EditorConfigService.SaveConfig();
                RefreshAssetBrowser();
            });

            AddToggleRow(displayCard.transform, "Show Size Badges on Browser Cards", cfg.ShowSizeBadges, (val) =>
            {
                cfg.ShowSizeBadges = val;
                EditorConfigService.SaveConfig();
                RefreshAssetBrowser();
            });
        }

        private static void BuildPreferencesPageGizmo(RectTransform content)
        {
            var cfg = EditorConfigService.Config;

            var gizmoCard = CreateModularSection(content, "Gizmos", "3D Transformation Gizmos");

            AddSliderRow(gizmoCard.transform, "Gizmo Base Scale Multiplier", 0.4f, 3.0f, cfg.GizmoScaleMultiplier, "{0:F2}x", (val) =>
            {
                cfg.GizmoScaleMultiplier = val;
                EditorConfigService.SaveConfig();
            });

            AddToggleRow(gizmoCard.transform, "Show Snapping Proxy Colliders in Scene", cfg.SnappingProxiesVisible, (val) =>
            {
                cfg.SnappingProxiesVisible = val;
                EditorSessionManager.SetSnappingProxiesActive(val);
                EditorConfigService.SaveConfig();
            });

            var snapCard = CreateModularSection(content, "Snapping", "Default Grid Snapping Matrix");

            AddSliderRow(snapCard.transform, "Default Grid Translation Snap (m)", 0.0f, 4.0f, cfg.DefaultGridSnap, "{0:F2}m", (val) =>
            {
                cfg.DefaultGridSnap = val;
                SetSnapTranslate(val);
                EditorConfigService.SaveConfig();
            });

            AddSliderRow(snapCard.transform, "Default Rotation Snap Angle", 5f, 90f, SnapRotate, "{0:F0} deg", (val) =>
            {
                SnapRotate = val;
            });

            AddSliderRow(snapCard.transform, "Default Scale Increment Snap", 0.02f, 0.5f, SnapScale, "{0:F2}", (val) =>
            {
                SnapScale = val;
            });
        }

        private static void BuildPreferencesPageHotkeys(RectTransform content)
        {
            var headerCard = CreateModularSection(content, "KeybindsHeader", "Shortcut Configuration & Remapping");
            CreateTextPrimitive(headerCard.transform, "Click any button to rebind. Press Esc to cancel or Del to unbind.\n<color=#FFD54F>Note: W, A, S, D, Q, E, Space cannot be bound without Ctrl or Alt to prevent camera flight collision.</color>",
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10.5f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);

            _prefShortcutSearchInput = CreateInputFieldPrimitive(headerCard.transform, "ShortcutSearch", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, 26f), "Filter keybinds...", (val) =>
            {
                _prefShortcutSearchFilter = val.Trim().ToLowerInvariant();
                RebuildPreferencesHotkeyList(content);
            });

            RebuildPreferencesHotkeyList(content);
        }

        private static void RebuildPreferencesHotkeyList(RectTransform content)
        {
            Transform oldList = content.Find("Card_HotkeyList");
            if (oldList != null) GameObject.DestroyImmediate(oldList.gameObject);

            var listCard = CreateModularSection(content, "HotkeyList", "Assigned Bindings");

            _shortcutDisplayLabels.Clear();
            var defaults = new EditorConfigData();

            foreach (var kvp in EditorConfigService.Config.Keybindings)
            {
                string actionKey = kvp.Key;
                if (!string.IsNullOrEmpty(_prefShortcutSearchFilter) &&
                    !actionKey.ToLowerInvariant().Contains(_prefShortcutSearchFilter) &&
                    !kvp.Value.ToLowerInvariant().Contains(_prefShortcutSearchFilter))
                {
                    continue;
                }

                GameObject row = CreateRowContainerPrimitive(listCard.transform, "Row_Shortcut_" + actionKey, 30f);

                CreateTextPrimitive(row.transform, actionKey, new Vector2(0f, 0f), new Vector2(0.55f, 1f), new Vector2(6f, 0f), Vector2.zero, 11f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);

                Button rebindBtn = CreateButtonPrimitive(row.transform, "Btn_Rebind_" + actionKey, kvp.Value, 160f, null, new Color(0.18f, 0.22f, 0.30f));
                RectTransform rbrt = rebindBtn.GetComponent<RectTransform>();
                rbrt.anchorMin = new Vector2(1f, 0.5f);
                rbrt.anchorMax = new Vector2(1f, 0.5f);
                rbrt.pivot = new Vector2(1f, 0.5f);
                rbrt.anchoredPosition = new Vector2(-44f, 0f);
                rbrt.sizeDelta = new Vector2(160f, 24f);

                TMP_Text label = rebindBtn.GetComponentInChildren<TMP_Text>();
                _shortcutDisplayLabels[actionKey] = label;

                rebindBtn.onClick.AddListener((Action)(() =>
                {
                    StartRebindingKey(actionKey, label);
                }));

                string defCombo = defaults.Keybindings.TryGetValue(actionKey, out string d) ? d : "None";
                Button resetBtn = CreateButtonPrimitive(row.transform, "Btn_Reset_" + actionKey, "↺", 30f, () =>
                {
                    EditorConfigService.Config.Keybindings[actionKey] = defCombo;
                    label.text = defCombo;
                    label.color = Color.white;
                    EditorConfigService.SaveConfig();
                    SetNotificationText($"Reset '{actionKey}' to '{defCombo}'");
                }, new Color(0.24f, 0.28f, 0.36f));

                RectTransform rrst = resetBtn.GetComponent<RectTransform>();
                rrst.anchorMin = new Vector2(1f, 0.5f);
                rrst.anchorMax = new Vector2(1f, 0.5f);
                rrst.pivot = new Vector2(1f, 0.5f);
                rrst.anchoredPosition = new Vector2(-6f, 0f);
                rrst.sizeDelta = new Vector2(32f, 24f);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }

        private static void BuildPreferencesPageSafety(RectTransform content)
        {
            var cfg = EditorConfigService.Config;

            var safetyCard = CreateModularSection(content, "AutoSave", "Auto-Save Backups & Crash Prevention");

            AddSliderRow(safetyCard.transform, "Auto-Save Interval (Minutes, 0=Off)", 0f, 15f, cfg.AutoSaveIntervalMinutes, "{0:F0} min", (val) =>
            {
                cfg.AutoSaveIntervalMinutes = Mathf.RoundToInt(val);
                EditorConfigService.SaveConfig();
            });

            AddToggleRow(safetyCard.transform, "Auto-Save Backup Before Playtesting (F1)", cfg.AutoSaveOnPlaytest, (val) =>
            {
                cfg.AutoSaveOnPlaytest = val;
                EditorConfigService.SaveConfig();
            });

            CreateButtonPrimitive(safetyCard.transform, "Btn_ForceBackupNow", "Force Auto-Save Backup Now", 360f, () =>
            {
                PerformAutoSaveBackup();
            }, new Color(0.18f, 0.52f, 0.88f, 1f));

            var toastCard = CreateModularSection(content, "Toast", "User Interface Alerts");

            AddSliderRow(toastCard.transform, "Notification Display Time", 1.0f, 6.0f, cfg.NotificationDuration, "{0:F1}s", (val) =>
            {
                cfg.NotificationDuration = val;
                EditorConfigService.SaveConfig();
            });
        }

        private static void StartRebindingKey(string actionKey, TMP_Text label)
        {
            _activeRebindingActionKey = actionKey;
            _activeRebindLabel = label;
            label.text = "<Press Key...>";
            label.color = Color.yellow;
            GUIUtility.keyboardControl = 9999;
        }

        public static void UpdateRebindingTick()
        {
            if (string.IsNullOrEmpty(_activeRebindingActionKey) || _activeRebindLabel == null) return;

            GUIUtility.keyboardControl = 9999;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _activeRebindLabel.text = EditorConfigService.Config.Keybindings[_activeRebindingActionKey];
                _activeRebindLabel.color = Color.white;
                _activeRebindingActionKey = null;
                _activeRebindLabel = null;
                GUIUtility.keyboardControl = 0;
                return;
            }

            if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
            {
                EditorConfigService.Config.Keybindings[_activeRebindingActionKey] = "None";
                _activeRebindLabel.text = "None";
                _activeRebindLabel.color = Color.gray;
                EditorConfigService.SaveConfig();
                _activeRebindingActionKey = null;
                _activeRebindLabel = null;
                GUIUtility.keyboardControl = 0;
                return;
            }

            foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
            {
                if (kc == KeyCode.LeftControl || kc == KeyCode.RightControl ||
                    kc == KeyCode.LeftAlt || kc == KeyCode.RightAlt ||
                    kc == KeyCode.LeftShift || kc == KeyCode.RightShift ||
                    kc == KeyCode.None || kc == KeyCode.Escape)
                    continue;

                if (Input.GetKeyDown(kc))
                {
                    bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                    bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                    bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                    string combo = "";
                    if (ctrl) combo += "Ctrl+";
                    if (alt) combo += "Alt+";
                    if (shift) combo += "Shift+";
                    combo += kc.ToString();

                    if (EditorConfigService.IsCameraKeyConflict(combo, out string conflictReason))
                    {
                        SetNotificationText(conflictReason);
                        _activeRebindLabel.text = "<Conflict! Add Ctrl/Alt>";
                        _activeRebindLabel.color = new Color(1f, 0.3f, 0.3f);
                        return;
                    }

                    EditorConfigService.Config.Keybindings[_activeRebindingActionKey] = combo;
                    _activeRebindLabel.text = combo;
                    _activeRebindLabel.color = Color.white;
                    EditorConfigService.SaveConfig();

                    _activeRebindingActionKey = null;
                    _activeRebindLabel = null;
                    GUIUtility.keyboardControl = 0;
                    break;
                }
            }
        }

        // =========================================================================
        // ASSET BROWSER PANEL & ADVANCED SORTING / SEARCH
        // =========================================================================

        private static void BuildAssetBrowserPanel()
        {
            _assetBrowserPanel = CreatePanelPrimitive(_canvasRoot.transform, "AssetBrowser_Panel",
                Vector2.zero, new Vector2(1f, 0f),
                Vector2.zero, new Vector2(0f, 265f),
                new Color(0.10f, 0.11f, 0.13f, 0.98f));

            RectTransform abrt = _assetBrowserPanel.GetComponent<RectTransform>();
            if (abrt != null)
            {
                abrt.pivot = new Vector2(0.5f, 0f);
                abrt.offsetMin = new Vector2(265f, 0f);
                abrt.offsetMax = new Vector2(-290f, 265f);
            }

            GameObject controlStrip = CreatePanelPrimitive(_assetBrowserPanel.transform, "Browser_ControlStrip",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -14f), new Vector2(0f, 28f),
                Color.clear);

            HorizontalLayoutGroup cshlg = controlStrip.AddComponent<HorizontalLayoutGroup>();
            cshlg.padding = new RectOffset(8, 8, 2, 2);
            cshlg.spacing = 6f;
            cshlg.childControlWidth = true;
            cshlg.childControlHeight = true;
            cshlg.childForceExpandWidth = false;
            cshlg.childForceExpandHeight = true;

            CreateSizeFilterButton(controlStrip.transform, "ALL", AssetSizeTier.All, 55f);
            CreateSizeFilterButton(controlStrip.transform, "SMALL", AssetSizeTier.Small, 65f);
            CreateSizeFilterButton(controlStrip.transform, "MEDIUM", AssetSizeTier.Medium, 75f);
            CreateSizeFilterButton(controlStrip.transform, "LARGE", AssetSizeTier.Large, 65f);

            Button sortBtn = CreateButtonPrimitive(controlStrip.transform, "Btn_ToggleSortOrder", "Sort: Size", 80f, () =>
            {
                _browserSortMode = (_browserSortMode + 1) % 4;
                string[] labels = new string[] { "Sort: Size", "Sort: Size Desc", "Sort: A-Z", "Sort: Tris" };
                if (_sortSizeBtnText != null) _sortSizeBtnText.text = labels[_browserSortMode];
                RefreshAssetBrowser();
            }, new Color(0.18f, 0.28f, 0.40f, 1f));
            if (sortBtn != null) _sortSizeBtnText = sortBtn.GetComponentInChildren<TMP_Text>();

            Button tinyBtn = CreateButtonPrimitive(controlStrip.transform, "Btn_ToggleTiny", "Tiny: OFF", 75f, () =>
            {
                _includeTinyProps = !_includeTinyProps;
                if (_toggleTinyPropsText != null) _toggleTinyPropsText.text = _includeTinyProps ? "Tiny: ON" : "Tiny: OFF";
                RefreshAssetBrowser();
            }, new Color(0.22f, 0.25f, 0.32f, 0.95f));
            if (tinyBtn != null) _toggleTinyPropsText = tinyBtn.GetComponentInChildren<TMP_Text>();

            _browserSearchInput = CreateInputFieldPrimitive(controlStrip.transform, "BrowserSearch",
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                "Search palette (e.g. type:hazard, size:>5, platform)...", (s) => RefreshAssetBrowser());
            if (_browserSearchInput != null)
            {
                LayoutElement searchLe = _browserSearchInput.gameObject.AddComponent<LayoutElement>();
                searchLe.preferredWidth = 200f;
                searchLe.flexibleWidth = 1f;
            }

            GameObject scrollObj = CreateScrollViewPrimitive(_assetBrowserPanel.transform, "Browser_Scroll",
                Vector2.zero, Vector2.one,
                new Vector2(8f, 6f), new Vector2(-16f, -42f),
                out _browserContent);

            if (scrollObj != null)
            {
                Image scrollImg = scrollObj.GetComponent<Image>() ?? scrollObj.AddComponent<Image>();
                scrollImg.color = Color.clear;
                scrollImg.raycastTarget = true;

                _assetBrowserScrollRect = scrollObj.GetComponent<ScrollRect>();
                if (_assetBrowserScrollRect != null)
                {
                    _assetBrowserScrollRect.movementType = ScrollRect.MovementType.Clamped;
                    _assetBrowserScrollRect.scrollSensitivity = 40f;
                }

                Transform vp = scrollObj.transform.Find("Viewport");
                if (vp != null)
                {
                    Image vpImg = vp.GetComponent<Image>() ?? vp.gameObject.AddComponent<Image>();
                    vpImg.color = Color.clear;
                    vpImg.raycastTarget = true;
                }
            }

            if (_browserContent != null)
            {
                VerticalLayoutGroup oldVlg = _browserContent.GetComponent<VerticalLayoutGroup>();
                if (oldVlg != null) GameObject.DestroyImmediate(oldVlg);

                GridLayoutGroup glg = _browserContent.gameObject.AddComponent<GridLayoutGroup>();
                glg.cellSize = new Vector2(106f, 116f);
                glg.spacing = new Vector2(8f, 8f);
                glg.padding = new RectOffset(6, 6, 6, 6);
                glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
                glg.startAxis = GridLayoutGroup.Axis.Horizontal;
                glg.childAlignment = TextAnchor.UpperLeft;
                glg.constraint = GridLayoutGroup.Constraint.Flexible;
            }
        }

        private static void CreateSizeFilterButton(Transform parent, string label, AssetSizeTier tier, float width)
        {
            CreateButtonPrimitive(parent, "BtnSize_" + tier, label, width, () =>
            {
                _activeSizeFilter = tier;
                RefreshAssetBrowser();
            }, new Color(0.14f, 0.17f, 0.22f, 0.95f));
        }

        public static void RefreshAssetBrowser()
        {
            if (_browserContent == null) return;
            CloseContextMenu();
            CloseAllDropdowns();
            _isBrowserTargetInitialized = false;

            for (int i = 0; i < _browserCards.Count; i++)
            {
                if (_browserCards[i] != null) GameObject.Destroy(_browserCards[i]);
            }
            _browserCards.Clear();

            string rawSearch = (_browserSearchInput != null && !string.IsNullOrEmpty(_browserSearchInput.text))
                ? _browserSearchInput.text.Trim().ToLowerInvariant() : "";

            string[] searchTokens = !string.IsNullOrEmpty(rawSearch)
                ? rawSearch.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                : null;

            List<CatalogAsset> matchedAssets = new List<CatalogAsset>();
            float minCutoff = EditorConfigService.Config.MinAssetSize;

            for (int i = 0; i < EditorSessionManager.AllAssets.Count; i++)
            {
                CatalogAsset asset = EditorSessionManager.AllAssets[i];
                if (asset == null || asset.SourceTemplate == null) continue;

                if (!_includeTinyProps && asset.MaxDimension < minCutoff && !asset.IsPrefabInstance && (asset.Traits & AssetTrait.Architecture) != 0)
                    continue;

                if (_activeBrowserCategory != "All")
                {
                    if (!EditorConfigService.IsAssetInCategory(_activeBrowserCategory, asset.DisplayName))
                        continue;
                }

                if (_activeSizeFilter != AssetSizeTier.All && asset.SizeTier != _activeSizeFilter)
                    continue;

                if (searchTokens != null && searchTokens.Length > 0)
                {
                    string dName = asset.DisplayName.ToLowerInvariant();
                    string subCat = (asset.SubCategory ?? "").ToLowerInvariant();
                    string badge = asset.GetSizeBadgeText().ToLowerInvariant();

                    bool allTokensMatched = true;
                    for (int t = 0; t < searchTokens.Length; t++)
                    {
                        string token = searchTokens[t];
                        if (token.StartsWith("type:"))
                        {
                            string typeKey = token.Substring(5);
                            if (typeKey == "hazard" && !asset.IsLaser && !asset.IsRotatingLaser && !asset.IsTurret) { allTokensMatched = false; break; }
                            if (typeKey == "light" && !asset.IsSpotlight && !asset.IsSunlight && !asset.IsSkybox) { allTokensMatched = false; break; }
                            if (typeKey == "gravity" && !asset.IsGravityArea) { allTokensMatched = false; break; }
                            continue;
                        }
                        if (token.StartsWith("size:>"))
                        {
                            if (float.TryParse(token.Substring(6), NumberStyles.Float, CultureInfo.InvariantCulture, out float minS))
                            {
                                if (asset.MaxDimension <= minS) { allTokensMatched = false; break; }
                            }
                            continue;
                        }

                        bool tokenFound = dName.Contains(token) || subCat.Contains(token) || badge.Contains(token);
                        if (!tokenFound) { allTokensMatched = false; break; }
                    }
                    if (!allTokensMatched) continue;
                }

                matchedAssets.Add(asset);
            }

            matchedAssets.Sort((a, b) =>
            {
                switch (_browserSortMode)
                {
                    case 0: return a.MaxDimension.CompareTo(b.MaxDimension);
                    case 1: return b.MaxDimension.CompareTo(a.MaxDimension);
                    case 2: return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
                    case 3:
                        int triA = a.FilterMesh != null ? a.FilterMesh.triangles.Length / 3 : 0;
                        int triB = b.FilterMesh != null ? b.FilterMesh.triangles.Length / 3 : 0;
                        return triB.CompareTo(triA);
                    default: return a.MaxDimension.CompareTo(b.MaxDimension);
                }
            });

            if (_assetCountBadgeText != null)
                _assetCountBadgeText.text = $"{matchedAssets.Count} Props";

            for (int i = 0; i < matchedAssets.Count; i++)
            {
                CatalogAsset asset = matchedAssets[i];

                GameObject card = new GameObject("Card_" + asset.DisplayName, Il2CppType.Of<RectTransform>());
                card.transform.SetParent(_browserContent, false);

                RectTransform crt = card.GetComponent<RectTransform>();
                crt.sizeDelta = new Vector2(106f, 116f);

                Image bg = card.AddComponent<Image>();
                bg.color = new Color(0.14f, 0.16f, 0.20f, 0.95f);
                bg.raycastTarget = true;

                CatalogAsset capturedAsset = asset;

                Button btn = card.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener((Action)(() =>
                {
                    EditorSessionManager.EquipAsset(capturedAsset);
                    RefreshModeDisplay();
                }));

                EventTrigger trigger = card.AddComponent<EventTrigger>();
                var clickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                clickEntry.callback.AddListener((Action<BaseEventData>)((e) =>
                {
                    var pe = e.Cast<PointerEventData>();
                    if (pe.button == PointerEventData.InputButton.Right)
                    {
                        ShowContextMenuForAsset(capturedAsset, pe.position);
                    }
                }));
                trigger.triggers.Add(clickEntry);

                var dragEntry = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
                dragEntry.callback.AddListener((Action<BaseEventData>)((e) =>
                {
                    if (_assetBrowserScrollRect != null)
                        _assetBrowserScrollRect.OnDrag(e.Cast<PointerEventData>());
                }));
                trigger.triggers.Add(dragEntry);

                GameObject preview = new GameObject("Thumbnail", Il2CppType.Of<RectTransform>());
                preview.transform.SetParent(card.transform, false);

                RectTransform prt = preview.GetComponent<RectTransform>();
                prt.anchorMin = new Vector2(0.5f, 1f);
                prt.anchorMax = new Vector2(0.5f, 1f);
                prt.pivot = new Vector2(0.5f, 1f);
                prt.anchoredPosition = new Vector2(0f, -6f);
                prt.sizeDelta = new Vector2(92f, 76f);

                Image pImg = preview.AddComponent<Image>();
                pImg.raycastTarget = false;
                pImg.color = new Color(0.12f, 0.15f, 0.20f, 0.9f);

                AssetThumbnailRenderer.RequestThumbnail(asset, pImg);

                Color pipColor = Color.clear;
                if (asset.IsJumper) pipColor = new Color(1f, 0.8f, 0.2f);
                else if (asset.IsHelix) pipColor = new Color(0.2f, 0.9f, 0.4f);
                else if (asset.IsLaser || asset.IsRotatingLaser) pipColor = new Color(1f, 0.2f, 0.2f);
                else if (asset.IsGravityArea) pipColor = new Color(0.6f, 0.2f, 1f);

                if (pipColor != Color.clear)
                {
                    GameObject pip = new GameObject("TraitPip", Il2CppType.Of<RectTransform>());
                    pip.transform.SetParent(card.transform, false);
                    RectTransform pipRt = pip.GetComponent<RectTransform>();
                    pipRt.anchorMin = new Vector2(0f, 1f);
                    pipRt.anchorMax = new Vector2(0f, 1f);
                    pipRt.pivot = new Vector2(0f, 1f);
                    pipRt.sizeDelta = new Vector2(8f, 8f);
                    pipRt.anchoredPosition = new Vector2(2f, -2f);
                    pip.AddComponent<Image>().color = pipColor;
                }

                if (EditorConfigService.Config.ShowSizeBadges)
                {
                    GameObject badgeObj = new GameObject("SizeBadge", Il2CppType.Of<RectTransform>());
                    badgeObj.transform.SetParent(card.transform, false);

                    RectTransform bdrt = badgeObj.GetComponent<RectTransform>();
                    bdrt.anchorMin = new Vector2(0.50f, 0.72f);
                    bdrt.anchorMax = new Vector2(0.96f, 0.92f);
                    bdrt.sizeDelta = Vector2.zero;

                    Color badgeColor = asset.IsPrefabInstance ? new Color(0.65f, 0.2f, 0.95f, 0.9f) :
                        (asset.SizeTier == AssetSizeTier.Small ? new Color(0.2f, 0.85f, 0.4f, 0.85f) :
                        (asset.SizeTier == AssetSizeTier.Medium ? new Color(0.1f, 0.7f, 1.0f, 0.85f) :
                        new Color(1.0f, 0.55f, 0.15f, 0.85f)));

                    badgeObj.AddComponent<Image>().color = badgeColor;
                    CreateTextPrimitive(badgeObj.transform, asset.GetSizeBadgeText(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 8.5f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
                }

                GameObject dotsObj = new GameObject("Btn_Dots", Il2CppType.Of<RectTransform>());
                dotsObj.transform.SetParent(card.transform, false);
                RectTransform drt = dotsObj.GetComponent<RectTransform>();
                drt.anchorMin = new Vector2(0f, 1f);
                drt.anchorMax = new Vector2(0f, 1f);
                drt.pivot = new Vector2(0f, 1f);
                drt.anchoredPosition = new Vector2(2f, -2f);
                drt.sizeDelta = new Vector2(18f, 18f);

                dotsObj.AddComponent<Image>().color = new Color(0.18f, 0.22f, 0.30f, 0.8f);
                Button dotsBtn = dotsObj.AddComponent<Button>();
                CreateTextPrimitive(dotsObj.transform, "...", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 11f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
                dotsBtn.onClick.AddListener((Action)(() => ShowContextMenuForAsset(capturedAsset, Input.mousePosition)));

                GameObject labelObj = new GameObject("Label", Il2CppType.Of<RectTransform>());
                labelObj.transform.SetParent(card.transform, false);

                RectTransform lrt = labelObj.GetComponent<RectTransform>();
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = new Vector2(1f, 0.24f);
                lrt.offsetMin = new Vector2(2f, 2f);
                lrt.offsetMax = new Vector2(-2f, -2f);

                TMP_Text label = labelObj.AddComponent<TextMeshProUGUI>();
                label.text = asset.DisplayName;
                label.fontSize = 8.5f;
                label.alignment = TextAlignmentOptions.Center;
                label.color = Color.white;
                label.overflowMode = TextOverflowModes.Ellipsis;

                _browserCards.Add(card);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_browserContent);
        }

        // =========================================================================
        // CONTEXT MENU
        // =========================================================================

        private static void BuildContextMenuOverlay()
        {
            _contextMenuRoot = new GameObject("Asset_Context_Menu", Il2CppType.Of<RectTransform>());
            _contextMenuRoot.transform.SetParent(_canvasRoot.transform, false);

            RectTransform cmRt = _contextMenuRoot.GetComponent<RectTransform>();
            cmRt.sizeDelta = new Vector2(220f, 145f);
            cmRt.pivot = new Vector2(0f, 1f);

            Image cmBg = _contextMenuRoot.AddComponent<Image>();
            cmBg.color = new Color(0.09f, 0.11f, 0.14f, 0.98f);

            VerticalLayoutGroup vlg = _contextMenuRoot.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 3f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            CreateContextMenuOption("Find Occurrences in Scene", () =>
            {
                if (_contextTargetAsset != null) OpenOccurrencesWindowForAsset(_contextTargetAsset.DisplayName);
                CloseContextMenu();
            });

            CreateContextMenuOption("Replace Selected With This", () =>
            {
                if (_contextTargetAsset != null) SwapSelectedObjectsWithEquipped(_contextTargetAsset);
                CloseContextMenu();
            });

            CreateContextMenuOption("Manage Tabs for Prop...", () =>
            {
                var target = _contextTargetAsset;
                CloseContextMenu();
                if (target != null) OpenAssignCategoryWindow(target);
            });

            CreateContextMenuOption("Copy Asset Name", () =>
            {
                if (_contextTargetAsset != null) GUIUtility.systemCopyBuffer = _contextTargetAsset.DisplayName;
                CloseContextMenu();
            });

            CreateContextMenuOption("Delete Custom Prefab", () =>
            {
                if (_contextTargetAsset != null && _contextTargetAsset.IsPrefabInstance && _contextTargetAsset.PrefabTemplate != null)
                {
                    PrefabInstanceManager.DeleteInstance(_contextTargetAsset.PrefabTemplate);
                }
                CloseContextMenu();
            }, new Color(0.85f, 0.25f, 0.25f, 1f));

            _contextMenuRoot.SetActive(false);
        }

        private static void CreateContextMenuOption(string label, Action onClick, Color? textColor = null)
        {
            GameObject btnObj = new GameObject("Option_" + label, Il2CppType.Of<RectTransform>());
            btnObj.transform.SetParent(_contextMenuRoot.transform, false);

            LayoutElement le = btnObj.AddComponent<LayoutElement>();
            le.preferredHeight = 25f;
            le.minHeight = 25f;

            Image img = btnObj.AddComponent<Image>();
            img.color = new Color(0.14f, 0.16f, 0.22f, 0.90f);

            Button btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener((Action)(() => onClick?.Invoke()));

            TMP_Text txt = CreateTextPrimitive(btnObj.transform, label, Vector2.zero, Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, 0f), 10f, FontStyles.Normal, textColor ?? Color.white, TextAlignmentOptions.MidlineLeft);
            txt.enableWordWrapping = false;
        }

        public static void ShowContextMenuForAsset(CatalogAsset asset, Vector2 screenPos)
        {
            if (_contextMenuRoot == null || asset == null) return;
            CloseAllDropdowns();
            _contextTargetAsset = asset;

            RectTransform rt = _contextMenuRoot.GetComponent<RectTransform>();
            Vector2 clamped = screenPos;
            if (clamped.x + 225f > Screen.width) clamped.x -= 225f;
            if (clamped.y - 145f < 0f) clamped.y += 145f;

            rt.position = clamped;
            _contextMenuRoot.SetActive(true);
            _contextMenuRoot.transform.SetAsLastSibling();

            Canvas cmCanvas = _contextMenuRoot.GetComponent<Canvas>();
            if (cmCanvas == null)
            {
                cmCanvas = _contextMenuRoot.AddComponent<Canvas>();
                cmCanvas.overrideSorting = true;
                cmCanvas.sortingOrder = 1200;
                _contextMenuRoot.AddComponent<GraphicRaycaster>();
            }
        }

        public static void CloseContextMenu()
        {
            if (_contextMenuRoot != null && _contextMenuRoot.activeSelf)
            {
                _contextMenuRoot.SetActive(false);
                _contextTargetAsset = null;
            }
        }

        // =========================================================================
        // HIERARCHY PANEL (LOCKING, BUDGET GAUGE, TYPE FILTERING)
        // =========================================================================

        private static void BuildHierarchyPanel()
        {
            _hierarchyPanel = CreatePanelPrimitive(_canvasRoot.transform, "Hierarchy_Panel",
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(130f, -18f), new Vector2(260f, -36f),
                new Color(0.10f, 0.11f, 0.13f, 0.98f));

            GameObject header = CreatePanelPrimitive(_hierarchyPanel.transform, "Hierarchy_Header",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -16f), new Vector2(0f, 32f),
                new Color(0.14f, 0.16f, 0.20f, 0.98f));

            CreateTextPrimitive(header.transform, "HIERARCHY",
                Vector2.zero, Vector2.one,
                new Vector2(10f, 0f), new Vector2(-110f, 0f),
                11f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);

            _hierarchyBudgetGaugeText = CreateTextPrimitive(header.transform, "0 / 5000",
                new Vector2(1f, 0f), Vector2.one,
                Vector2.zero, Vector2.zero,
                12f, FontStyles.Bold, new Color(0.2f, 0.85f, 1f), TextAlignmentOptions.MidlineRight);

            RectTransform bgRt = _hierarchyBudgetGaugeText.GetComponent<RectTransform>();
            bgRt.pivot = new Vector2(1f, 0.5f);
            bgRt.sizeDelta = new Vector2(100f, 30f);
            bgRt.anchoredPosition = new Vector2(-10f, 0f);

            GameObject filterStrip = CreatePanelPrimitive(_hierarchyPanel.transform, "Hierarchy_FilterStrip",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -40f), new Vector2(0f, 20f),
                new Color(0.07f, 0.08f, 0.10f, 0.95f));

            HorizontalLayoutGroup fshlg = filterStrip.AddComponent<HorizontalLayoutGroup>();
            fshlg.padding = new RectOffset(4, 4, 1, 1);
            fshlg.spacing = 3f;
            fshlg.childControlWidth = true;
            fshlg.childForceExpandWidth = true;

            CreateHierarchyTypeFilterBtn(filterStrip.transform, "ALL");
            CreateHierarchyTypeFilterBtn(filterStrip.transform, "HAZARD");
            CreateHierarchyTypeFilterBtn(filterStrip.transform, "LIGHT");
            CreateHierarchyTypeFilterBtn(filterStrip.transform, "GRAV");

            GameObject searchRow = CreatePanelPrimitive(_hierarchyPanel.transform, "Hierarchy_SearchRow",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -62f), new Vector2(0f, 24f),
                new Color(0.08f, 0.09f, 0.11f, 0.95f));

            _hierarchySearchInput = CreateInputFieldPrimitive(searchRow.transform, "SearchInput",
                Vector2.zero, Vector2.one,
                new Vector2(6f, 2f), new Vector2(-6f, -2f),
                "Search scene nodes...", (val) => RefreshHierarchy());

            GameObject scrollObj = CreateScrollViewPrimitive(_hierarchyPanel.transform, "Hierarchy_Scroll",
                Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero,
                out _hierarchyContent);

            if (scrollObj != null)
            {
                RectTransform srt = scrollObj.GetComponent<RectTransform>();
                srt.anchorMin = new Vector2(0f, 0f);
                srt.anchorMax = new Vector2(1f, 1f);
                srt.offsetMin = new Vector2(6f, 40f);
                srt.offsetMax = new Vector2(-6f, -76f);

                _hierarchyScrollRect = scrollObj.GetComponent<ScrollRect>();
                if (_hierarchyScrollRect != null)
                    _hierarchyScrollRect.movementType = ScrollRect.MovementType.Clamped;
            }

            GameObject bottomBar = CreatePanelPrimitive(_hierarchyPanel.transform, "Hierarchy_BottomBar",
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 18f), new Vector2(0f, 36f),
                new Color(0.08f, 0.09f, 0.11f, 0.95f));

            HorizontalLayoutGroup bhlg = bottomBar.AddComponent<HorizontalLayoutGroup>();
            bhlg.padding = new RectOffset(6, 6, 4, 4);
            bhlg.spacing = 6f;
            bhlg.childControlWidth = true;
            bhlg.childForceExpandWidth = true;

            CreateButtonPrimitive(bottomBar.transform, "Btn_GroupContainer", "Group [Ctrl+G]", 110f, () =>
            {
                GroupSelectedUnderNewContainer();
            }, new Color(0.2f, 0.45f, 0.75f, 1f));

            CreateButtonPrimitive(bottomBar.transform, "Btn_DeleteSelected", "Delete [Supr]", 110f, () =>
            {
                EditorSessionManager.DeleteSelectedObjects();
            }, new Color(0.75f, 0.22f, 0.22f, 1f));
        }

        private static void CreateHierarchyTypeFilterBtn(Transform parent, string typeTag)
        {
            Button b = CreateButtonPrimitive(parent, "BtnFilt_" + typeTag, typeTag, 55f, () =>
            {
                _hierarchyFilterType = typeTag;
                RefreshHierarchy();
            }, _hierarchyFilterType == typeTag ? new Color(0.2f, 0.5f, 0.85f) : new Color(0.12f, 0.14f, 0.18f));
        }

        private static void GroupSelectedUnderNewContainer()
        {
            if (EditorSessionManager.SelectedObjects == null || EditorSessionManager.SelectedObjects.Count == 0)
            {
                SetNotificationText("Select objects to group.");
                return;
            }

            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < EditorSessionManager.SelectedObjects.Count; i++)
                centroid += EditorSessionManager.SelectedObjects[i].transform.position;
            centroid /= EditorSessionManager.SelectedObjects.Count;

            GameObject groupObj = new GameObject("Custom_Group_Container");
            groupObj.transform.position = centroid;
            groupObj.transform.rotation = Quaternion.identity;
            groupObj.transform.localScale = Vector3.one;

            EditorSessionManager.RegisterPlacedObject(groupObj);

            for (int i = 0; i < EditorSessionManager.SelectedObjects.Count; i++)
            {
                var o = EditorSessionManager.SelectedObjects[i];
                if (o != null && o != groupObj)
                {
                    o.transform.SetParent(groupObj.transform, true);
                }
            }

            EditorSessionManager.RecalculateParentChildCount(groupObj);
            RefreshHierarchy();
            EditorSessionManager.SelectObject(groupObj);
            SetNotificationText("Group container created.");
        }

        public static void RefreshHierarchy()
        {
            EnsureSelectableColliders();
            if (_hierarchyContent == null) return;

            _targetToRowMap.Clear();
            _rowToTargetMap.Clear();

            for (int i = _hierarchyContent.childCount - 1; i >= 0; i--)
            {
                Transform child = _hierarchyContent.GetChild(i);
                if (child != null && child.gameObject != null)
                    GameObject.DestroyImmediate(child.gameObject);
            }
            _hierarchyRows.Clear();

            string search = (_hierarchySearchInput != null && !string.IsNullOrEmpty(_hierarchySearchInput.text))
                ? _hierarchySearchInput.text.Trim().ToLowerInvariant() : "";

            if (_hierarchyBudgetGaugeText != null)
            {
                int count = EditorSessionManager.PlacedObjects.Count;
                int maxBudget = 5000;
                _hierarchyBudgetGaugeText.text = $"{count} / {maxBudget}";

                if (count > 4000)
                    _hierarchyBudgetGaugeText.color = new Color(1f, 0.4f, 0.2f);
                else if (count > 2500)
                    _hierarchyBudgetGaugeText.color = new Color(1f, 0.85f, 0.2f);
                else
                    _hierarchyBudgetGaugeText.color = new Color(0.2f, 0.85f, 1f);
            }

            List<GameObject> rootNodes = new List<GameObject>();
            Dictionary<GameObject, List<GameObject>> childrenMap = new Dictionary<GameObject, List<GameObject>>();

            if (EditorSessionManager.PlacedObjects == null) return;

            for (int i = 0; i < EditorSessionManager.PlacedObjects.Count; i++)
            {
                GameObject obj = EditorSessionManager.PlacedObjects[i];
                if (obj == null || !obj || obj.Equals(null)) continue;

                try
                {
                    if (obj.transform == null || !obj.activeSelf) continue;

                    if (_hierarchyFilterType != "ALL")
                    {
                        EditorSessionManager.PlacedObjectTypes.TryGetValue(obj, out var pt);
                        if (_hierarchyFilterType == "HAZARD" && pt != PlacedObjectType.Laser && pt != PlacedObjectType.RotatingLaser && pt != PlacedObjectType.Turret) continue;
                        if (_hierarchyFilterType == "LIGHT" && pt != PlacedObjectType.Spotlight && pt != PlacedObjectType.Sunlight && pt != PlacedObjectType.SkyboxController) continue;
                        if (_hierarchyFilterType == "GRAV" && pt != PlacedObjectType.GravityArea) continue;
                    }

                    GameObject parent = (obj.transform.parent != null &&
                                         obj.transform.parent.gameObject != null &&
                                         EditorSessionManager.PlacedObjects.Contains(obj.transform.parent.gameObject))
                        ? obj.transform.parent.gameObject : null;

                    if (parent == null || !parent || parent.Equals(null))
                    {
                        rootNodes.Add(obj);
                    }
                    else
                    {
                        if (!childrenMap.ContainsKey(parent)) childrenMap[parent] = new List<GameObject>();
                        childrenMap[parent].Add(obj);
                    }
                }
                catch { continue; }
            }

            for (int i = 0; i < rootNodes.Count; i++)
            {
                if (rootNodes[i] != null && rootNodes[i] && !rootNodes[i].Equals(null))
                {
                    RenderHierarchyTreeNode(rootNodes[i], 0, childrenMap, search);
                }
            }

            try
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_hierarchyContent);
            }
            catch { }
        }

        private static string GetCleanEntityLabel(GameObject go, out string typeTag, out Color tagColor)
        {
            typeTag = "PROP";
            tagColor = new Color(0.4f, 0.5f, 0.65f);
            if (go == null || !go || go.Equals(null)) return "Unknown";

            string n = "Unknown";
            try { n = go.name ?? "Unknown"; } catch { return "Unknown"; }
            if (n.StartsWith("Custom_")) n = n.Substring(7);
            n = n.Replace("_", " ");

            try
            {
                if (EditorSessionManager.PlacedObjectTypes != null &&
                    EditorSessionManager.PlacedObjectTypes.TryGetValue(go, out PlacedObjectType pType))
                {
                    switch (pType)
                    {
                        case PlacedObjectType.Jumper: typeTag = "JUMP"; tagColor = new Color(0.95f, 0.75f, 0.1f); break;
                        case PlacedObjectType.Turbine: typeTag = "TURB"; tagColor = new Color(0.2f, 0.85f, 0.4f); break;
                        case PlacedObjectType.Turret: typeTag = "TURR"; tagColor = new Color(0.95f, 0.35f, 0.35f); break;
                        case PlacedObjectType.Laser:
                        case PlacedObjectType.RotatingLaser: typeTag = "LASR"; tagColor = new Color(1.0f, 0.2f, 0.2f); break;
                        case PlacedObjectType.Spotlight:
                        case PlacedObjectType.Sunlight: typeTag = "LGHT"; tagColor = new Color(0.2f, 0.85f, 1.0f); break;
                        case PlacedObjectType.Checkpoint:
                        case PlacedObjectType.SpawnGate:
                        case PlacedObjectType.GoalGate: typeTag = "GATE"; tagColor = new Color(0.3f, 0.65f, 1.0f); break;
                        case PlacedObjectType.Switch: typeTag = "SWTH"; tagColor = new Color(0.85f, 0.4f, 0.95f); break;
                        case PlacedObjectType.SkyboxController: typeTag = "SKYB"; tagColor = new Color(0.5f, 0.8f, 1.0f); break;
                        case PlacedObjectType.GravityArea: typeTag = "GRAV"; tagColor = new Color(0.6f, 0.2f, 1.0f); break;
                    }
                }
            }
            catch { }

            return n;
        }

        private static void RenderHierarchyTreeNode(GameObject node, int depth, Dictionary<GameObject, List<GameObject>> childrenMap, string searchFilter)
        {
            if (node == null || !node || node.Equals(null)) return;
            try
            {
                if (node.transform == null || !node.activeSelf) return;
            }
            catch { return; }

            string nodeName = "Object";
            try { nodeName = node.name ?? "Object"; } catch { return; }

            bool hasChildren = childrenMap != null && childrenMap.ContainsKey(node) && childrenMap[node] != null && childrenMap[node].Count > 0;
            bool isCollapsed = _collapsedParents != null && _collapsedParents.Contains(node);

            bool matchesSearch = string.IsNullOrEmpty(searchFilter) || nodeName.ToLowerInvariant().Contains(searchFilter);
            bool childMatches = false;

            if (hasChildren && !string.IsNullOrEmpty(searchFilter))
            {
                var childList = childrenMap[node];
                for (int c = 0; c < childList.Count; c++)
                {
                    GameObject child = childList[c];
                    if (child == null || !child || child.Equals(null)) continue;
                    try
                    {
                        if (child.name != null && child.name.ToLowerInvariant().Contains(searchFilter))
                        {
                            childMatches = true;
                            break;
                        }
                    }
                    catch { }
                }
            }

            if (!matchesSearch && !childMatches) return;

            GameObject row = new GameObject($"Row_{nodeName}", Il2CppType.Of<RectTransform>());
            row.transform.SetParent(_hierarchyContent, false);

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 26f;
            le.minHeight = 26f;

            _targetToRowMap[node] = row;
            _rowToTargetMap[row] = node;

            bool isDirectlySelected = false;
            try
            {
                isDirectlySelected = (EditorSessionManager.SelectedObjects != null && EditorSessionManager.SelectedObjects.Contains(node)) ||
                                      (EditorSessionManager.SelectedObject == node);
            }
            catch { }

            Image bg = row.AddComponent<Image>();
            bg.color = isDirectlySelected ? new Color(0.18f, 0.52f, 0.88f, 0.95f) :
                       (hasChildren ? new Color(0.15f, 0.17f, 0.22f, 0.85f) : new Color(0.11f, 0.12f, 0.15f, 0.70f));

            float leftPadding = 6f + (depth * 14f);

            if (depth > 0)
            {
                GameObject guide = new GameObject("GuideLine", Il2CppType.Of<RectTransform>());
                guide.transform.SetParent(row.transform, false);
                RectTransform grt = guide.GetComponent<RectTransform>();
                grt.anchorMin = new Vector2(0f, 0f);
                grt.anchorMax = new Vector2(0f, 1f);
                grt.sizeDelta = new Vector2(1f, 0f);
                grt.anchoredPosition = new Vector2(leftPadding - 6f, 0f);
                guide.AddComponent<Image>().color = new Color(0.25f, 0.28f, 0.35f, 0.6f);
            }

            if (hasChildren)
            {
                GameObject foldoutBtn = new GameObject("Foldout", Il2CppType.Of<RectTransform>());
                foldoutBtn.transform.SetParent(row.transform, false);
                RectTransform fbrt = foldoutBtn.GetComponent<RectTransform>();
                fbrt.anchorMin = new Vector2(0f, 0.5f);
                fbrt.anchorMax = new Vector2(0f, 0.5f);
                fbrt.pivot = new Vector2(0f, 0.5f);
                fbrt.anchoredPosition = new Vector2(leftPadding - 2f, 0f);
                fbrt.sizeDelta = new Vector2(14f, 20f);

                TMP_Text ft = foldoutBtn.AddComponent<TextMeshProUGUI>();
                ft.text = isCollapsed ? ">" : "v";
                ft.fontSize = 9.5f;
                ft.alignment = TextAlignmentOptions.Center;
                ft.color = new Color(0.3f, 0.85f, 1f);

                Button fb = foldoutBtn.AddComponent<Button>();
                GameObject capturedNode = node;
                fb.onClick.AddListener((Action)(() =>
                {
                    if (capturedNode == null || !capturedNode || capturedNode.Equals(null)) return;
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
                if (captured == null || !captured || captured.Equals(null)) return;
                if (!_isDraggingHierarchyNode)
                {
                    bool isCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                    EditorSessionManager.SelectObject(captured, isAdditive: isCtrl);
                }
            }));

            string cleanTitle = GetCleanEntityLabel(captured, out string typeTag, out Color tagCol);

            GameObject tagObj = new GameObject("Tag", Il2CppType.Of<RectTransform>());
            tagObj.transform.SetParent(row.transform, false);
            RectTransform tagRt = tagObj.GetComponent<RectTransform>();
            tagRt.anchorMin = new Vector2(0f, 0.5f);
            tagRt.anchorMax = new Vector2(0f, 0.5f);
            tagRt.pivot = new Vector2(0f, 0.5f);
            tagRt.anchoredPosition = new Vector2(leftPadding, 0f);
            tagRt.sizeDelta = new Vector2(34f, 16f);
            tagObj.AddComponent<Image>().color = tagCol;

            CreateTextPrimitive(tagObj.transform, typeTag, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 8f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
            leftPadding += 38f;

            int childCount = (hasChildren && childrenMap[node] != null) ? childrenMap[node].Count : 0;
            string parentBadge = childCount > 0 ? $" ({childCount})" : "";
            TMP_Text rowText = CreateTextPrimitive(row.transform, cleanTitle + parentBadge,
                Vector2.zero, Vector2.one,
                new Vector2(leftPadding, 0f), new Vector2(-75f, 0f),
                10f, hasChildren ? FontStyles.Bold : FontStyles.Normal,
                Color.white, TextAlignmentOptions.MidlineLeft);
            if (rowText != null)
            {
                rowText.enableWordWrapping = false;
                rowText.overflowMode = TextOverflowModes.Ellipsis;
            }

            GameObject lockBtnObj = new GameObject("Btn_Lock", Il2CppType.Of<RectTransform>());
            lockBtnObj.transform.SetParent(row.transform, false);
            RectTransform lkrt = lockBtnObj.GetComponent<RectTransform>();
            lkrt.anchorMin = new Vector2(1f, 0.5f);
            lkrt.anchorMax = new Vector2(1f, 0.5f);
            lkrt.pivot = new Vector2(1f, 0.5f);
            lkrt.anchoredPosition = new Vector2(-54f, 0f);
            lkrt.sizeDelta = new Vector2(16f, 16f);
            lockBtnObj.AddComponent<Image>().color = new Color(0.14f, 0.16f, 0.22f, 0.9f);
            Button lkBtn = lockBtnObj.AddComponent<Button>();

            bool isLocked = LockedObjects.Contains(captured);
            TMP_Text lkTxt = CreateTextPrimitive(lockBtnObj.transform, isLocked ? "L" : "-", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 9f, FontStyles.Bold, isLocked ? new Color(1f, 0.8f, 0.2f) : Color.gray, TextAlignmentOptions.Center);

            lkBtn.onClick.AddListener((Action)(() =>
            {
                if (LockedObjects.Contains(captured))
                {
                    LockedObjects.Remove(captured);
                    lkTxt.text = "-";
                    lkTxt.color = Color.gray;
                    SetNotificationText($"Unlocked: {captured.name}");
                }
                else
                {
                    LockedObjects.Add(captured);
                    lkTxt.text = "L";
                    lkTxt.color = new Color(1f, 0.8f, 0.2f);
                    SetNotificationText($"Locked: {captured.name}");
                }
            }));

            GameObject eyeBtnObj = new GameObject("Btn_Eye", Il2CppType.Of<RectTransform>());
            eyeBtnObj.transform.SetParent(row.transform, false);
            RectTransform eyert = eyeBtnObj.GetComponent<RectTransform>();
            eyert.anchorMin = new Vector2(1f, 0.5f);
            eyert.anchorMax = new Vector2(1f, 0.5f);
            eyert.pivot = new Vector2(1f, 0.5f);
            eyert.anchoredPosition = new Vector2(-36f, 0f);
            eyert.sizeDelta = new Vector2(16f, 16f);
            eyeBtnObj.AddComponent<Image>().color = new Color(0.16f, 0.20f, 0.26f, 0.9f);
            Button eyeBtn = eyeBtnObj.AddComponent<Button>();

            bool isActive = true;
            try { isActive = captured.activeSelf; } catch { }
            TMP_Text eyeTxt = CreateTextPrimitive(eyeBtnObj.transform, isActive ? "V" : "-", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 9.5f, FontStyles.Bold, isActive ? new Color(0.3f, 0.9f, 1f) : Color.gray, TextAlignmentOptions.Center);

            eyeBtn.onClick.AddListener((Action)(() =>
            {
                if (captured == null || !captured || captured.Equals(null)) return;
                try
                {
                    captured.SetActive(!captured.activeSelf);
                    if (eyeTxt != null)
                    {
                        eyeTxt.text = captured.activeSelf ? "V" : "-";
                        eyeTxt.color = captured.activeSelf ? new Color(0.3f, 0.9f, 1f) : Color.gray;
                    }
                }
                catch { }
            }));

            GameObject focusBtnObj = new GameObject("Btn_Focus", Il2CppType.Of<RectTransform>());
            focusBtnObj.transform.SetParent(row.transform, false);
            RectTransform fcrt = focusBtnObj.GetComponent<RectTransform>();
            fcrt.anchorMin = new Vector2(1f, 0.5f);
            fcrt.anchorMax = new Vector2(1f, 0.5f);
            fcrt.pivot = new Vector2(1f, 0.5f);
            fcrt.anchoredPosition = new Vector2(-18f, 0f);
            fcrt.sizeDelta = new Vector2(16f, 16f);
            focusBtnObj.AddComponent<Image>().color = new Color(0.18f, 0.24f, 0.32f, 0.9f);
            Button fcBtn = focusBtnObj.AddComponent<Button>();
            CreateTextPrimitive(focusBtnObj.transform, "F", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 9.5f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
            fcBtn.onClick.AddListener((Action)(() =>
            {
                if (captured == null || !captured || captured.Equals(null)) return;
                EditorSessionManager.SelectObject(captured);
                EditorViewportCamera.FocusOnObject(captured);
            }));

            GameObject delBtnObj = new GameObject("Btn_Del", Il2CppType.Of<RectTransform>());
            delBtnObj.transform.SetParent(row.transform, false);
            RectTransform drt = delBtnObj.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(1f, 0.5f);
            drt.anchorMax = new Vector2(1f, 0.5f);
            drt.pivot = new Vector2(1f, 0.5f);
            drt.anchoredPosition = new Vector2(-1f, 0f);
            drt.sizeDelta = new Vector2(16f, 16f);
            delBtnObj.AddComponent<Image>().color = new Color(0.35f, 0.15f, 0.15f, 0.9f);
            Button dBtn = delBtnObj.AddComponent<Button>();
            CreateTextPrimitive(delBtnObj.transform, "X", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 9f, FontStyles.Bold, new Color(1f, 0.4f, 0.4f), TextAlignmentOptions.Center);
            dBtn.onClick.AddListener((Action)(() =>
            {
                if (captured == null || !captured || captured.Equals(null)) return;
                EditorSessionManager.DeleteSpecifiedObject(captured);
            }));

            _hierarchyRows.Add(row);

            if (hasChildren && !isCollapsed && childrenMap[node] != null)
            {
                var list = childrenMap[node];
                for (int c = 0; c < list.Count; c++)
                {
                    GameObject child = list[c];
                    if (child != null && child && !child.Equals(null))
                    {
                        RenderHierarchyTreeNode(child, depth + 1, childrenMap, searchFilter);
                    }
                }
            }
        }

        private static void UpdateHierarchyHighlightOnly()
        {
            if (_targetToRowMap == null || _targetToRowMap.Count == 0) return;

            GameObject primary = EditorSessionManager.SelectedObject;

            foreach (var kvp in _targetToRowMap)
            {
                GameObject target = kvp.Key;
                GameObject row = kvp.Value;
                if (target == null || row == null) continue;

                Image bg = row.GetComponent<Image>();
                if (bg == null) continue;

                bool isDirectlySelected = (EditorSessionManager.SelectedObjects != null && EditorSessionManager.SelectedObjects.Contains(target)) ||
                                          (target == primary);

                if (isDirectlySelected)
                {
                    bg.color = new Color(0.18f, 0.52f, 0.88f, 0.95f);
                }
                else
                {
                    int childCount = 0;
                    EditorSessionManager.PlacedParentChildCounts.TryGetValue(target, out childCount);
                    bg.color = (childCount > 0) ? new Color(0.15f, 0.17f, 0.22f, 0.85f) : new Color(0.11f, 0.12f, 0.15f, 0.70f);
                }
            }
        }

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

                if (!_isDraggingHierarchyNode && Vector2.Distance(Input.mousePosition, _dragStartMousePos) > 8f)
                {
                    _isDraggingHierarchyNode = true;
                    CreateDragGhost(_dragCandidateNode);
                }

                if (_isDraggingHierarchyNode)
                {
                    UpdateDragGhostPosition();
                    UpdateHierarchyDragVisuals();
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                if (_hierarchyScrollRect != null) _hierarchyScrollRect.vertical = true;

                if (_isDraggingHierarchyNode && _dragCandidateNode != null)
                {
                    GameObject dropTarget = GetHoveredHierarchyNode();
                    if (dropTarget != null)
                    {
                        if (dropTarget != _dragCandidateNode && !EditorSessionManager.IsDescendantOf(_dragCandidateNode, dropTarget))
                        {
                            GameObject prevParent = _dragCandidateNode.transform.parent != null
                                ? _dragCandidateNode.transform.parent.gameObject : null;

                            _dragCandidateNode.transform.SetParent(dropTarget.transform, true);

                            if (prevParent != null) EditorSessionManager.RecalculateParentChildCount(prevParent);
                            EditorSessionManager.RecalculateParentChildCount(dropTarget);

                            RefreshHierarchy();
                        }
                    }
                    else if (IsMouseOverHierarchyPanel() && _dragCandidateNode.transform.parent != null)
                    {
                        GameObject oldParent = _dragCandidateNode.transform.parent.gameObject;
                        _dragCandidateNode.transform.SetParent(null, true);

                        EditorSessionManager.RecalculateParentChildCount(oldParent);
                        RefreshHierarchy();
                    }

                    CleanupDragGhost();
                    UpdateHierarchyHighlightOnly();
                }

                _dragCandidateNode = null;
                _isDraggingHierarchyNode = false;
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

        private static void CreateDragGhost(GameObject node)
        {
            CleanupDragGhost();
            if (node == null || _canvasRoot == null) return;

            _dragGhostObj = new GameObject("Hierarchy_Drag_Ghost", Il2CppType.Of<RectTransform>());
            _dragGhostObj.transform.SetParent(_canvasRoot.transform, false);

            RectTransform rt = _dragGhostObj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(190f, 24f);
            rt.pivot = new Vector2(0f, 1f);

            Image bg = _dragGhostObj.AddComponent<Image>();
            bg.color = new Color(0.95f, 0.65f, 0.15f, 0.90f);
            bg.raycastTarget = false;

            _dragGhostText = CreateTextPrimitive(_dragGhostObj.transform, "[Moving] " + node.name,
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
                    if (dropTarget == _dragCandidateNode || EditorSessionManager.IsDescendantOf(_dragCandidateNode, dropTarget))
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
                        : new Color(0.11f, 0.12f, 0.15f, 0.70f);
                }
            }

            if (dropTarget == null && IsMouseOverHierarchyPanel() && _dragGhostText != null)
            {
                _dragGhostText.text = "[Release to Unparent] (Root)";
            }
        }

        // =========================================================================
        // MODULAR INSPECTOR (LOCAL/WORLD COORD TOGGLE & EXPANSION)
        // =========================================================================

        private static void BuildInspectorPanel()
        {
            _isInspectorExpanded = false;
            _inspectorPanel = CreatePanelPrimitive(_canvasRoot.transform, "Inspector_Panel",
                new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-140f, -18f), new Vector2(280f, -36f),
                new Color(0.10f, 0.11f, 0.14f, 0.98f));
            _inspectorPanelRt = _inspectorPanel.GetComponent<RectTransform>();

            GameObject titleBar = CreatePanelPrimitive(_inspectorPanel.transform, "TitleBar",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -16f), new Vector2(0f, 32f),
                new Color(0.14f, 0.16f, 0.20f, 0.98f));

            _inspectorTitleText = CreateTextPrimitive(titleBar.transform, "Inspector",
                Vector2.zero, Vector2.one,
                new Vector2(10f, 0f), new Vector2(-80f, 0f),
                11f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);
            _inspectorTitleText.enableWordWrapping = false;
            _inspectorTitleText.overflowMode = TextOverflowModes.Ellipsis;

            _inspectorExpandBtn = CreateButtonPrimitive(titleBar.transform, "Btn_ExpandInspector", "[+ Expand]", 68f, ToggleInspectorExpansion, new Color(0.20f, 0.23f, 0.28f, 1f));
            RectTransform ebrt = _inspectorExpandBtn.GetComponent<RectTransform>();
            ebrt.anchorMin = new Vector2(1f, 0.5f);
            ebrt.anchorMax = new Vector2(1f, 0.5f);
            ebrt.pivot = new Vector2(1f, 0.5f);
            ebrt.anchoredPosition = new Vector2(-6f, 0f);
            ebrt.sizeDelta = new Vector2(68f, 22f);
            _inspectorExpandBtnText = _inspectorExpandBtn.GetComponentInChildren<TMP_Text>();

            CreateScrollViewPrimitive(_inspectorPanel.transform, "Inspector_Scroll",
                Vector2.zero, Vector2.one,
                new Vector2(6f, 6f), new Vector2(-12f, -38f),
                out _inspectorContent);

            var transCard = CreateModularSection(_inspectorContent, "Transform", "Transform & Alignment");

            GameObject coordToggleRow = CreateRowContainerPrimitive(transCard.transform, "Row_CoordSpace", 22f);
            Button cBtn = CreateButtonPrimitive(coordToggleRow.transform, "Btn_CoordSpace", "Space: LOCAL", 130f, () =>
            {
                _useWorldCoordinates = !_useWorldCoordinates;
                if (_coordSpaceToggleText != null) _coordSpaceToggleText.text = _useWorldCoordinates ? "Space: WORLD" : "Space: LOCAL";
                RefreshInspectorValues();
            }, new Color(0.16f, 0.22f, 0.32f));
            _coordSpaceToggleText = cBtn.GetComponentInChildren<TMP_Text>();

            CreateVector3Row(transCard.transform, "Position", out _posXInput, out _posYInput, out _posZInput, OnTransformInputChanged);
            CreateVector3Row(transCard.transform, "Rotation", out _rotXInput, out _rotYInput, out _rotZInput, OnTransformInputChanged);
            CreateVector3Row(transCard.transform, "Scale", out _scaleXInput, out _scaleYInput, out _scaleZInput, OnTransformInputChanged);
        }

        private static void ToggleInspectorExpansion()
        {
            _isInspectorExpanded = !_isInspectorExpanded;
            if (_inspectorPanelRt != null)
            {
                float w = _isInspectorExpanded ? 380f : 280f;
                _inspectorPanelRt.sizeDelta = new Vector2(w, -36f);
                _inspectorPanelRt.anchoredPosition = new Vector2(-w * 0.5f, -18f);
            }
            if (_inspectorExpandBtnText != null)
            {
                _inspectorExpandBtnText.text = _isInspectorExpanded ? "[- Slim]" : "[+ Expand]";
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
                    _inspectorTitleText.text = (obj != null) ? GetCleanEntityLabel(obj, out _, out _) : "Inspector (None Selected)";
                }
            }

            UpdateHierarchyHighlightOnly();
            RebuildModularInspectorCards(obj);
            RefreshInspectorValues();
        }

        private static void RebuildModularInspectorCards(GameObject obj)
        {
            for (int i = 0; i < _activeInspectorCards.Count; i++)
            {
                if (_activeInspectorCards[i] != null) GameObject.Destroy(_activeInspectorCards[i]);
            }
            _activeInspectorCards.Clear();

            if (obj == null) return;

            EditorEntityData data = EditorSessionManager.ExtractEntityData(obj);
            EditorSessionManager.PlacedObjectTypes.TryGetValue(obj, out PlacedObjectType type);

            // JUMPER
            if (data.Has<JumperConfig>() || type == PlacedObjectType.Jumper)
            {
                var card = CreateModularSection(_inspectorContent, "Jumper", "Jumper Launch Pad");
                var jc = data.GetOrCreate<JumperConfig>();
                AddSliderRow(card.transform, "Launch Force", 5f, 400f, jc.Force, "{0:F1}", (v) =>
                {
                    jc.Force = v;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                        EditorSessionManager.ApplyJumperForce(targets[t], v);
                });
                AddToggleRow(card.transform, "Active Pad", jc.IsActive, (state) =>
                {
                    jc.IsActive = state;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                        EditorSessionManager.ApplyJumperActive(targets[t], state);
                });
                _activeInspectorCards.Add(card);
            }

            // TURBINE
            if (data.Has<TurbineConfig>() || type == PlacedObjectType.Turbine)
            {
                var card = CreateModularSection(_inspectorContent, "Turbine", "Helix Turbine Fan");
                var tc = data.GetOrCreate<TurbineConfig>();
                AddSliderRow(card.transform, "Wind Speed", 5f, 100f, tc.Speed, "{0:F1}", (v) =>
                {
                    tc.Speed = v;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                        EditorSessionManager.ApplyTurbineSpeed(targets[t], v);
                });
                _activeInspectorCards.Add(card);
            }

            // TURRET
            if (data.Has<TurretConfig>() || type == PlacedObjectType.Turret)
            {
                var card = CreateModularSection(_inspectorContent, "Turret", "Defense Turret");
                var tc = data.GetOrCreate<TurretConfig>();
                AddSliderRow(card.transform, "Fire Delay", 0.1f, 5.0f, tc.FireDelay, "{0:F2}s", (v) =>
                {
                    tc.FireDelay = v;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                        EditorSessionManager.ApplyTurretSettings(targets[t], v, tc.FirePower);
                });
                AddSliderRow(card.transform, "Fire Power", 500f, 3000f, tc.FirePower, "{0:F0}", (v) =>
                {
                    tc.FirePower = v;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                        EditorSessionManager.ApplyTurretSettings(targets[t], tc.FireDelay, v);
                });
                _activeInspectorCards.Add(card);
            }

            // LASER
            if (data.Has<LaserConfig>() || type == PlacedObjectType.RotatingLaser || type == PlacedObjectType.Laser)
            {
                var card = CreateModularSection(_inspectorContent, "Laser", "Laser Barrier Hazard");
                var lc = data.GetOrCreate<LaserConfig>();
                AddSliderRow(card.transform, "Rotation Spd", 0f, 180f, lc.RotationSpeed, "{0:F0} d/s", (v) =>
                {
                    lc.RotationSpeed = v;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                        EditorSessionManager.LaserRotationSpeeds[targets[t]] = v;
                });
                _activeInspectorCards.Add(card);
            }

            // LIGHTING
            if (data.Has<LightConfig>() || EditorSessionManager.PlacedLights.ContainsKey(obj))
            {
                var card = CreateModularSection(_inspectorContent, "Lighting", "Light & Volumetrics");
                var lc = data.GetOrCreate<LightConfig>();

                AddSliderRow(card.transform, "Intensity", 0.1f, 30f, lc.Intensity, "{0:F1}", (v) =>
                {
                    lc.Intensity = v;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                        EditorSessionManager.ApplyLightConfig(targets[t], lc);
                });

                AddColorControl(card.transform, "Light Color", lc.Color, (newCol) =>
                {
                    lc.Color = newCol;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                        EditorSessionManager.ApplyLightConfig(targets[t], lc);
                });

                GameObject lAnimHeader = CreateRowContainerPrimitive(card.transform, "Row_LAnimHeader", 20f);
                CreateTextPrimitive(lAnimHeader.transform, "-- Flicker & Strobe Profile --", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 9.5f, FontStyles.Bold, new Color(0.3f, 0.85f, 1f), TextAlignmentOptions.MidlineLeft);

                GameObject lModeRow = CreateRowContainerPrimitive(card.transform, "Row_LGlowMode", 24f);
                SetupRowHorizontalLayoutPrimitive(lModeRow, 4f);
                CreateButtonPrimitive(lModeRow.transform, "Btn_LGlowMode", $"Profile: [{lc.Mode}]", 240f, () =>
                {
                    lc.Mode = (GlowMode)(((int)lc.Mode + 1) % 6);
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if (EditorSessionManager.PlacedLights.TryGetValue(targets[t], out var tlc))
                        {
                            tlc.Mode = lc.Mode;
                            EditorSessionManager.ApplyLightConfig(targets[t], tlc);
                        }
                    }
                    RebuildModularInspectorCards(obj);
                }, new Color(0.18f, 0.28f, 0.40f));

                if (lc.Mode != GlowMode.Steady)
                {
                    AddSliderRow(card.transform, "Frequency (Hz)", 0.2f, 12f, lc.Frequency, "{0:F1} Hz", (v) =>
                    {
                        lc.Frequency = v;
                        var targets = GetSelectionTargets(obj);
                        for (int t = 0; t < targets.Count; t++)
                            if (EditorSessionManager.PlacedLights.TryGetValue(targets[t], out var tlc)) tlc.Frequency = v;
                    });
                    AddSliderRow(card.transform, "Min Floor", 0.0f, 1.5f, lc.MinMultiplier, "{0:F2}x", (v) =>
                    {
                        lc.MinMultiplier = v;
                        var targets = GetSelectionTargets(obj);
                        for (int t = 0; t < targets.Count; t++)
                            if (EditorSessionManager.PlacedLights.TryGetValue(targets[t], out var tlc)) tlc.MinMultiplier = v;
                    });
                    AddSliderRow(card.transform, "Max Peak", 1.0f, 4.0f, lc.MaxMultiplier, "{0:F2}x", (v) =>
                    {
                        lc.MaxMultiplier = v;
                        var targets = GetSelectionTargets(obj);
                        for (int t = 0; t < targets.Count; t++)
                            if (EditorSessionManager.PlacedLights.TryGetValue(targets[t], out var tlc)) tlc.MaxMultiplier = v;
                    });
                    AddSliderRow(card.transform, "Circuit Sync ID", 0f, 12f, lc.SyncGroup, "Group {0:F0}", (v) =>
                    {
                        lc.SyncGroup = Mathf.RoundToInt(v);
                        var targets = GetSelectionTargets(obj);
                        for (int t = 0; t < targets.Count; t++)
                            if (EditorSessionManager.PlacedLights.TryGetValue(targets[t], out var tlc)) tlc.SyncGroup = lc.SyncGroup;
                    });
                }

                if (lc.Kind == LightKind.Point)
                {
                    AddSliderRow(card.transform, "Radius / Range", 3f, 80f, lc.Range, "{0:F0}m", (v) =>
                    {
                        lc.Range = v;
                        var targets = GetSelectionTargets(obj);
                        for (int t = 0; t < targets.Count; t++)
                            EditorSessionManager.ApplyLightConfig(targets[t], lc);
                    });
                }
                else if (lc.Kind == LightKind.Spot)
                {
                    AddSliderRow(card.transform, "Spot Angle", 10f, 150f, lc.SpotAngle, "{0:F0} deg", (v) =>
                    {
                        lc.SpotAngle = v;
                        var targets = GetSelectionTargets(obj);
                        for (int t = 0; t < targets.Count; t++)
                            EditorSessionManager.ApplyLightConfig(targets[t], lc);
                    });
                }

                _activeInspectorCards.Add(card);
            }

            // SWITCH TARGET
            if (data.Has<SwitchConfig>() || SwitchService.PlacedSwitches.ContainsKey(obj))
            {
                var card = CreateModularSection(_inspectorContent, "Switch", "Switch Target");
                var sc = data.GetOrCreate<SwitchConfig>();

                AddSliderRow(card.transform, "Active Time", 0.5f, 30.0f, sc.TimeBeforeSwitch, "{0:F1}s", (v) =>
                {
                    sc.TimeBeforeSwitch = v;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                        SwitchService.ApplySwitchConfig(targets[t], sc);
                });
                AddToggleRow(card.transform, "Timed Reset", sc.IsAutoSwitch, (st) =>
                {
                    sc.IsAutoSwitch = st;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                        SwitchService.ApplySwitchConfig(targets[t], sc);
                });
                AddToggleRow(card.transform, "Invert Output", sc.InvertChildren, (st) =>
                {
                    sc.InvertChildren = st;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                        SwitchService.ApplySwitchConfig(targets[t], sc);
                });
                AddToggleRow(card.transform, "Initial State On", sc.InitialStateOn, (st) =>
                {
                    sc.InitialStateOn = st;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                        SwitchService.ApplySwitchConfig(targets[t], sc);
                });

                _activeInspectorCards.Add(card);
            }

            // SKYBOX
            bool isSkybox = data.Has<SkyboxConfig>() || type == PlacedObjectType.SkyboxController || (obj.name != null && obj.name.ToLower().Contains("skybox"));
            if (isSkybox)
            {
                var card = CreateModularSection(_inspectorContent, "Skybox", "Celestial Skybox & Atmosphere");
                var sc = data.GetOrCreate<SkyboxConfig>();

                AddSliderRow(card.transform, "Exposure", 0.1f, 4.0f, sc.Exposure, "{0:F2}x", (v) =>
                {
                    sc.Exposure = v;
                    SkyboxControllerService.ActiveConfig.Exposure = v;
                    SkyboxControllerService.ApplyProperties();
                });
                AddSliderRow(card.transform, "Yaw Rotation", 0f, 360f, sc.YawOffset, "{0:F0} deg", (v) =>
                {
                    sc.YawOffset = v;
                    sc.CurrentAngle = v;
                    SkyboxControllerService.ActiveConfig.YawOffset = v;
                    SkyboxControllerService.ActiveConfig.CurrentAngle = v;
                    SkyboxControllerService.ApplyRotation(v);
                });
                AddSliderRow(card.transform, "Spin Speed", -30f, 30f, sc.SpinSpeed, "{0:F1} deg/s", (v) =>
                {
                    sc.SpinSpeed = v;
                    SkyboxControllerService.ActiveConfig.SpinSpeed = v;
                });
                AddColorControl(card.transform, "Sky Tint", sc.TintColor, (newCol) =>
                {
                    sc.TintColor = newCol;
                    SkyboxControllerService.ActiveConfig.TintColor = newCol;
                    SkyboxControllerService.ApplyProperties();
                });

                _activeInspectorCards.Add(card);
            }

            // GATE
            bool isGate = data.Has<GateConfig>() || type == PlacedObjectType.Checkpoint || type == PlacedObjectType.SpawnGate || type == PlacedObjectType.GoalGate;
            if (isGate)
            {
                var card = CreateModularSection(_inspectorContent, "Gate", "Gate & Checkpoint Settings");
                string role = type == PlacedObjectType.SpawnGate ? "Level Start (Spawn)" :
                             (type == PlacedObjectType.GoalGate ? "Level Finish (Goal)" : "Checkpoint Marker");
                Color badgeCol = type == PlacedObjectType.SpawnGate ? new Color(1f, 0.5f, 0.1f) :
                                (type == PlacedObjectType.GoalGate ? new Color(0.1f, 0.7f, 1f) : new Color(0.3f, 0.9f, 0.4f));

                GameObject badgeRow = CreateRowContainerPrimitive(card.transform, "Row_GateBadge", 22f);
                CreateTextPrimitive(badgeRow.transform, $"Role: [{role}]", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Bold, badgeCol, TextAlignmentOptions.MidlineLeft);

                _activeInspectorCards.Add(card);
            }

            // GRAVITY AREA
            if (data.Has<GravityConfig>() || type == PlacedObjectType.GravityArea || obj.GetComponentInChildren<GravityArea>() != null)
            {
                var card = CreateModularSection(_inspectorContent, "GravityArea", "Zero-G / Gravity Volume");
                var gc = data.GetOrCreate<GravityConfig>();
                if (GravityAreaService.PlacedGravityConfigs.TryGetValue(obj, out var existingGc))
                    gc = existingGc;

                Vector3 currentWorldGrav = gc.CalculateWorldGravity(obj.transform.rotation);
                CreateTextPrimitive(card.transform, $"Vector: ({currentWorldGrav.x:F1}, {currentWorldGrav.y:F1}, {currentWorldGrav.z:F1})",
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Normal, Color.cyan, TextAlignmentOptions.MidlineLeft);

                AddSliderRow(card.transform, "Gravity Force", -60f, 60f, gc.GravityForce, "{0:F1} m/s2", (v) =>
                {
                    gc.GravityForce = v;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if (GravityAreaService.PlacedGravityConfigs.TryGetValue(targets[t], out var tgc))
                        {
                            tgc.GravityForce = v;
                            GravityAreaService.ApplyGravityConfig(targets[t], tgc);
                        }
                        else
                        {
                            var newCfg = gc.Clone();
                            newCfg.GravityForce = v;
                            GravityAreaService.ApplyGravityConfig(targets[t], newCfg);
                        }
                    }
                });

                GameObject axisHeader = CreateRowContainerPrimitive(card.transform, "Row_AxisHeader", 20f);
                CreateTextPrimitive(axisHeader.transform, "-- Pull Direction (Relative to Box) --",
                    Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 9.5f, FontStyles.Bold, new Color(0.3f, 0.85f, 1f), TextAlignmentOptions.MidlineLeft);

                GameObject axisRow = CreateRowContainerPrimitive(card.transform, "Row_GravAxisBtns", 24f);
                SetupRowHorizontalLayoutPrimitive(axisRow, 4f);

                CreateButtonPrimitive(axisRow.transform, "Btn_AxisTop", "Top (+Y)", 65f, () =>
                {
                    gc.LocalAxis = Vector3.up;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if (GravityAreaService.PlacedGravityConfigs.TryGetValue(targets[t], out var tgc))
                        {
                            tgc.LocalAxis = Vector3.up;
                            GravityAreaService.ApplyGravityConfig(targets[t], tgc);
                        }
                    }
                    RebuildModularInspectorCards(obj);
                }, gc.LocalAxis == Vector3.up ? new Color(0.18f, 0.52f, 0.92f) : new Color(0.18f, 0.22f, 0.28f));

                CreateButtonPrimitive(axisRow.transform, "Btn_AxisBottom", "Bottom (-Y)", 65f, () =>
                {
                    gc.LocalAxis = Vector3.down;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if (GravityAreaService.PlacedGravityConfigs.TryGetValue(targets[t], out var tgc))
                        {
                            tgc.LocalAxis = Vector3.down;
                            GravityAreaService.ApplyGravityConfig(targets[t], tgc);
                        }
                    }
                    RebuildModularInspectorCards(obj);
                }, gc.LocalAxis == Vector3.down ? new Color(0.18f, 0.52f, 0.92f) : new Color(0.18f, 0.22f, 0.28f));

                CreateButtonPrimitive(axisRow.transform, "Btn_AxisFwd", "Forward (+Z)", 75f, () =>
                {
                    gc.LocalAxis = Vector3.forward;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if (GravityAreaService.PlacedGravityConfigs.TryGetValue(targets[t], out var tgc))
                        {
                            tgc.LocalAxis = Vector3.forward;
                            GravityAreaService.ApplyGravityConfig(targets[t], tgc);
                        }
                    }
                    RebuildModularInspectorCards(obj);
                }, gc.LocalAxis == Vector3.forward ? new Color(0.18f, 0.52f, 0.92f) : new Color(0.18f, 0.22f, 0.28f));

                GameObject presetRow = CreateRowContainerPrimitive(card.transform, "Row_GravPresets", 24f);
                SetupRowHorizontalLayoutPrimitive(presetRow, 4f);

                CreateButtonPrimitive(presetRow.transform, "Btn_PresetNormal", "Normal (9.8)", 75f, () =>
                {
                    gc.GravityForce = 9.81f;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if (GravityAreaService.PlacedGravityConfigs.TryGetValue(targets[t], out var tgc))
                        {
                            tgc.GravityForce = 9.81f;
                            GravityAreaService.ApplyGravityConfig(targets[t], tgc);
                        }
                    }
                    RebuildModularInspectorCards(obj);
                }, new Color(0.18f, 0.45f, 0.85f));

                CreateButtonPrimitive(presetRow.transform, "Btn_PresetHigh", "High (20.0)", 75f, () =>
                {
                    gc.GravityForce = 20.0f;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if (GravityAreaService.PlacedGravityConfigs.TryGetValue(targets[t], out var tgc))
                        {
                            tgc.GravityForce = 20.0f;
                            GravityAreaService.ApplyGravityConfig(targets[t], tgc);
                        }
                    }
                    RebuildModularInspectorCards(obj);
                }, new Color(0.85f, 0.45f, 0.15f));

                CreateButtonPrimitive(presetRow.transform, "Btn_PresetZeroG", "Zero-G (0.0)", 75f, () =>
                {
                    gc.GravityForce = 0.0f;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if (GravityAreaService.PlacedGravityConfigs.TryGetValue(targets[t], out var tgc))
                        {
                            tgc.GravityForce = 0.0f;
                            GravityAreaService.ApplyGravityConfig(targets[t], tgc);
                        }
                    }
                    RebuildModularInspectorCards(obj);
                }, new Color(0.5f, 0.2f, 0.8f));

                AddToggleRow(card.transform, "Affect Non-Player Objects", gc.AffectOthers, (st) =>
                {
                    gc.AffectOthers = st;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                        GravityAreaService.ApplyGravityConfig(targets[t], gc);
                });

                _activeInspectorCards.Add(card);
            }

            // MOTION PATH
            GameObject pathOwner = obj;
            if (EditorSessionManager.IsWaypointMarker(obj, out GameObject resolvedOwner, out _)) pathOwner = resolvedOwner;

            if (pathOwner != null)
            {
                var card = CreateModularSection(_inspectorContent, "MotionPath", "Kinematic Motion Path");
                if (EditorSessionManager.MotionPaths.TryGetValue(pathOwner, out var mp) && mp != null)
                {
                    CreateTextPrimitive(card.transform, $"Active Path: {mp.TotalDistance:F1}m travel", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Normal, Color.cyan, TextAlignmentOptions.MidlineLeft);

                    AddSliderRow(card.transform, "Speed (m/s)", 0.0f, 25f, mp.Speed, "{0:F1} m/s", (v) =>
                    {
                        mp.Speed = v;
                        var targets = GetSelectionTargets(obj);
                        for (int t = 0; t < targets.Count; t++)
                        {
                            GameObject po = targets[t];
                            if (EditorSessionManager.IsWaypointMarker(po, out GameObject ro, out _)) po = ro;
                            if (EditorSessionManager.MotionPaths.TryGetValue(po, out var pmp)) pmp.Speed = v;
                        }
                    });

                    AddSliderRow(card.transform, "Spin (deg/s)", -150f, 150f, mp.RotationSpeed, "{0:F0} d/s", (v) =>
                    {
                        mp.RotationSpeed = Mathf.Round(v);
                        var targets = GetSelectionTargets(obj);
                        for (int t = 0; t < targets.Count; t++)
                        {
                            GameObject po = targets[t];
                            if (EditorSessionManager.IsWaypointMarker(po, out GameObject ro, out _)) po = ro;
                            if (EditorSessionManager.MotionPaths.TryGetValue(po, out var pmp)) pmp.RotationSpeed = Mathf.Round(v);
                        }
                    });

                    CreateButtonPrimitive(card.transform, "Btn_DelPath", "Remove Motion Path", 240f, () =>
                    {
                        var targets = GetSelectionTargets(obj);
                        for (int t = 0; t < targets.Count; t++)
                        {
                            GameObject po = targets[t];
                            if (EditorSessionManager.IsWaypointMarker(po, out GameObject ro, out _)) po = ro;
                            EditorSessionManager.MotionPaths.Remove(po);
                            EditorSessionManager.DestroyWaypointVisuals(po);

                            if (EditorSessionManager.EntityRegistry.TryGetValue(po, out var entData))
                                entData.Remove<ObjectMotionPath>();
                        }
                        RebuildModularInspectorCards(obj);
                    }, new Color(0.7f, 0.2f, 0.2f, 0.9f));
                }
                else
                {
                    CreateButtonPrimitive(card.transform, "Btn_CreatePath", "[+ Create Motion Path]", 240f, () =>
                    {
                        var targets = GetSelectionTargets(obj);
                        for (int t = 0; t < targets.Count; t++)
                        {
                            GameObject po = targets[t];
                            if (EditorSessionManager.IsWaypointMarker(po, out GameObject ro, out _)) po = ro;
                            Vector3 startPos = po.transform.position;
                            Vector3 endPos = startPos + new Vector3(10f, 0f, 0f);

                            ObjectMotionPath newPath = new ObjectMotionPath
                            {
                                PointA = startPos,
                                PointB = endPos,
                                Speed = 4.0f,
                                IsActive = true
                            };

                            EditorSessionManager.MotionPaths[po] = newPath;
                            var rb = po.GetComponent<Rigidbody>() ?? po.AddComponent<Rigidbody>();
                            rb.isKinematic = true;

                            EditorSessionManager.UpdateWaypointVisuals(po, newPath);
                        }
                        RebuildModularInspectorCards(obj);
                    }, new Color(0.2f, 0.65f, 0.95f, 1f));
                }
                _activeInspectorCards.Add(card);
            }

            // NEON
            Renderer[] rends = obj.GetComponentsInChildren<Renderer>(true);
            bool hasMeshes = rends != null && rends.Length > 0 &&
                             type != PlacedObjectType.Spotlight &&
                             type != PlacedObjectType.Sunlight &&
                             type != PlacedObjectType.SkyboxController;

            if (hasMeshes)
            {
                var card = CreateModularSection(_inspectorContent, "Neon", "Neon & Emissive Accent");
                var nc = data.GetOrCreate<NeonConfig>();

                GameObject presetRow = CreateRowContainerPrimitive(card.transform, "Row_NeonPresets", 24f);
                SetupRowHorizontalLayoutPrimitive(presetRow, 4f);

                var presets = new (string name, Color col)[]
                {
                    ("Cyan", new Color(0f, 0.9f, 1f)),
                    ("Orange", new Color(1f, 0.45f, 0.05f)),
                    ("Pink", new Color(1f, 0.1f, 0.6f)),
                    ("Green", new Color(0.1f, 1f, 0.35f)),
                    ("Purple", new Color(0.7f, 0.15f, 1f)),
                    ("Red", new Color(1f, 0.15f, 0.15f))
                };

                for (int p = 0; p < presets.Length; p++)
                {
                    var pr = presets[p];
                    CreateButtonPrimitive(presetRow.transform, "Btn_Preset_" + pr.name, pr.name, 48f, () =>
                    {
                        nc.Color = pr.col;
                        var targets = GetSelectionTargets(obj);
                        for (int t = 0; t < targets.Count; t++)
                        {
                            var targetNc = nc.Clone();
                            EditorSessionManager.ApplyNeonConfig(targets[t], targetNc);
                        }
                        RebuildModularInspectorCards(obj);
                    }, pr.col * 0.35f);
                }

                AddSliderRow(card.transform, "Glow Power", 0.5f, 8.0f, nc.Intensity, "{0:F1}x", (v) =>
                {
                    nc.Intensity = v;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if (EditorSessionManager.PlacedNeonConfigs.TryGetValue(targets[t], out var tnc))
                        {
                            tnc.Intensity = v;
                            EditorSessionManager.ApplyNeonConfig(targets[t], tnc);
                        }
                    }
                });

                AddColorControl(card.transform, "Accent Tint", nc.Color, (newCol) =>
                {
                    nc.Color = newCol;
                    var targets = GetSelectionTargets(obj);
                    for (int t = 0; t < targets.Count; t++)
                    {
                        if (EditorSessionManager.PlacedNeonConfigs.TryGetValue(targets[t], out var tnc))
                        {
                            tnc.Color = newCol;
                            EditorSessionManager.ApplyNeonConfig(targets[t], tnc);
                        }
                    }
                });

                _activeInspectorCards.Add(card);
            }

            // CABLE
            GameObject cableTarget = obj;
            if (ProceduralCableService.IsCableHandle(obj, out GameObject cOwner, out _))
                cableTarget = cOwner;

            if (cableTarget != null && (data.Has<CableConfig>() || ProceduralCableService.PlacedCables.ContainsKey(cableTarget)))
            {
                var card = CreateModularSection(_inspectorContent, "Cable", "Procedural Wire & Cable");
                var cc = data.GetOrCreate<CableConfig>();
                if (ProceduralCableService.PlacedCables.TryGetValue(cableTarget, out var existingCc))
                    cc = existingCc;

                AddSliderRow(card.transform, "Thickness (m)", 0.02f, 0.8f, cc.Radius, "{0:F3}m", (v) =>
                {
                    cc.Radius = v;
                    ProceduralCableService.ApplyCableConfig(cableTarget, cc);
                });
                AddSliderRow(card.transform, "Gravity Sag (m)", -5.0f, 15.0f, cc.SagAmount, "{0:F2}m", (v) =>
                {
                    cc.SagAmount = v;
                    ProceduralCableService.ApplyCableConfig(cableTarget, cc);
                });

                _activeInspectorCards.Add(card);
            }

            // TRUSS
            GameObject trussTarget = obj;
            if (StructuralTrussService.IsTrussHandle(obj, out GameObject tOwner, out _))
                trussTarget = tOwner;

            if (trussTarget != null && (data.Has<TrussConfig>() || StructuralTrussService.PlacedTrusses.ContainsKey(trussTarget)))
            {
                var card = CreateModularSection(_inspectorContent, "Truss", "Structural Space-Truss Girder");
                var tc = data.GetOrCreate<TrussConfig>();
                if (StructuralTrussService.PlacedTrusses.TryGetValue(trussTarget, out var existingTc))
                    tc = existingTc;

                AddSliderRow(card.transform, "Width (m)", 0.3f, 4.0f, tc.Width, "{0:F2}m", (v) =>
                {
                    tc.Width = v;
                    StructuralTrussService.ApplyTrussConfig(trussTarget, tc);
                });
                AddSliderRow(card.transform, "Bay Length (m)", 0.4f, 5.0f, tc.BayLength, "{0:F2}m", (v) =>
                {
                    tc.BayLength = v;
                    StructuralTrussService.ApplyTrussConfig(trussTarget, tc);
                });

                _activeInspectorCards.Add(card);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_inspectorContent);
        }

        public static void RefreshInspectorValues()
        {
            GameObject obj = EditorSessionManager.SelectedObject;
            if (obj == null || !obj.activeSelf) return;

            _suppressInspectorCallbacks = true;

            Vector3 pos = _useWorldCoordinates ? obj.transform.position : obj.transform.localPosition;
            Vector3 rot = _useWorldCoordinates ? obj.transform.eulerAngles : obj.transform.localEulerAngles;
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

            _suppressInspectorCallbacks = false;
        }

        private static void OnTransformInputChanged(string val)
        {
            if (_suppressInspectorCallbacks || EditorSessionManager.SelectedObject == null) return;

            GameObject primary = EditorSessionManager.SelectedObject;
            Vector3 curPos = _useWorldCoordinates ? primary.transform.position : primary.transform.localPosition;
            Vector3 curRot = _useWorldCoordinates ? primary.transform.eulerAngles : primary.transform.localEulerAngles;

            float x = PersistenceUtility.ParseFloat(_posXInput?.text, curPos.x);
            float y = PersistenceUtility.ParseFloat(_posYInput?.text, curPos.y);
            float z = PersistenceUtility.ParseFloat(_posZInput?.text, curPos.z);

            float rx = PersistenceUtility.ParseFloat(_rotXInput?.text, curRot.x);
            float ry = PersistenceUtility.ParseFloat(_rotYInput?.text, curRot.y);
            float rz = PersistenceUtility.ParseFloat(_rotZInput?.text, curRot.z);

            float sx = PersistenceUtility.ParseFloat(_scaleXInput?.text, primary.transform.localScale.x);
            float sy = PersistenceUtility.ParseFloat(_scaleYInput?.text, primary.transform.localScale.y);
            float sz = PersistenceUtility.ParseFloat(_scaleZInput?.text, primary.transform.localScale.z);

            Vector3 newPos = new Vector3(x, y, z);
            Quaternion newRot = Quaternion.Euler(rx, ry, rz);
            Vector3 newScale = new Vector3(Mathf.Max(0.01f, sx), Mathf.Max(0.01f, sy), Mathf.Max(0.01f, sz));

            if (_useWorldCoordinates)
            {
                primary.transform.position = newPos;
                primary.transform.rotation = newRot;
            }
            else
            {
                primary.transform.localPosition = newPos;
                primary.transform.localRotation = newRot;
            }
            primary.transform.localScale = newScale;

            StudioGizmoController.InvalidateCachedCenter(primary);
            EditorSessionManager.UpdateSelectionHighlight();
        }

        public static void SwapSelectedObjectsWithEquipped(CatalogAsset targetAsset = null)
        {
            CatalogAsset newAsset = targetAsset ?? EditorSessionManager.CurrentAsset;
            if (newAsset == null)
            {
                EditorSessionManager.ShowNotification("Equip or choose a prop in the Asset Browser first.");
                return;
            }

            List<GameObject> toSwap = new List<GameObject>();
            if (EditorSessionManager.SelectedObjects != null && EditorSessionManager.SelectedObjects.Count > 0)
            {
                for (int i = 0; i < EditorSessionManager.SelectedObjects.Count; i++)
                {
                    GameObject o = EditorSessionManager.SelectedObjects[i];
                    if (o != null && !toSwap.Contains(o)) toSwap.Add(o);
                }
            }
            else if (EditorSessionManager.SelectedObject != null)
            {
                toSwap.Add(EditorSessionManager.SelectedObject);
            }

            if (toSwap.Count == 0) return;

            EditorSessionManager.SelectedObjects.Clear();

            for (int i = 0; i < toSwap.Count; i++)
            {
                GameObject old = toSwap[i];
                if (old == null) continue;

                Vector3 pos = old.transform.position;
                Quaternion rot = old.transform.rotation;
                Vector3 scale = old.transform.localScale;
                Transform parent = old.transform.parent;

                GameObject swapped = EditorSessionManager.SpawnCatalogObject(newAsset, pos, scale, rot);
                if (swapped != null)
                {
                    if (parent != null)
                    {
                        swapped.transform.SetParent(parent, true);
                        EditorSessionManager.RecalculateParentChildCount(parent.gameObject);
                    }

                    EditorSessionManager.RegisterPlacedObject(swapped);
                    EditorSessionManager.SelectedObjects.Add(swapped);
                }

                EditorSessionManager.DeleteSpecifiedObject(old);
            }

            EditorSessionManager.SelectedObject = EditorSessionManager.SelectedObjects.Count > 0 ? EditorSessionManager.SelectedObjects[0] : null;
            RefreshHierarchy();
            NotifyObjectSelected(EditorSessionManager.SelectedObject);
            SetNotificationText($"Swapped {toSwap.Count} prop(s) with '{newAsset.DisplayName}'");
        }

        // =========================================================================
        // OCCURRENCES, SCALER, DISTRIBUTE & RENAMER WINDOWS
        // =========================================================================

        private static void BuildOccurrencesWindow()
        {
            _occurrencesWin = StudioFloatingWindow.Create(_canvasRoot.transform, "Win_Occurrences", "Scene Occurrences", new Vector2(380f, 400f), new Vector2(-150f, 50f));

            _occurrencesTitleHeader = CreateTextPrimitive(_occurrencesWin.ContentRt, "Occurrences: None", Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, 15f), new Vector2(0f, 24f), 11f, FontStyles.Bold, Color.cyan, TextAlignmentOptions.MidlineLeft);

            CreateScrollViewPrimitive(_occurrencesWin.ContentRt, "Occurrences_Scroll", Vector2.zero, Vector2.one, new Vector2(0f, 34f), new Vector2(0f, -60f), out _occurrencesContent);

            GameObject bRow = CreateRowContainerPrimitive(_occurrencesWin.ContentRt, "Occ_Actions", 30f);
            SetupRowHorizontalLayoutPrimitive(bRow, 6f);
            RectTransform brt = bRow.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 0f);
            brt.anchoredPosition = Vector2.zero;

            CreateButtonPrimitive(bRow.transform, "Btn_MassSelect", "Select All", 160f, MassSelectCurrentOccurrences, new Color(0.18f, 0.45f, 0.85f, 1f));
            CreateButtonPrimitive(bRow.transform, "Btn_BatchSwap", "Swap With Equipped", 180f, () =>
            {
                if (_currentOccurrencesList.Count > 0 && EditorSessionManager.CurrentAsset != null)
                {
                    EditorSessionManager.SelectedObjects = new List<GameObject>(_currentOccurrencesList);
                    SwapSelectedObjectsWithEquipped(EditorSessionManager.CurrentAsset);
                    _occurrencesWin.Hide();
                }
            }, new Color(0.2f, 0.65f, 0.35f, 1f));
        }

        public static void OpenOccurrencesWindowForSelection()
        {
            if (EditorSessionManager.SelectedObject != null)
            {
                string raw = EditorSessionManager.SelectedObject.name;
                if (raw.StartsWith("Custom_")) raw = raw.Substring(7);
                OpenOccurrencesWindowForAsset(raw.Replace("_", " ").Trim());
            }
            else
            {
                _occurrencesWin?.Show();
            }
        }

        public static void OpenOccurrencesWindowForAsset(string assetDisplayName)
        {
            if (_occurrencesWin == null) return;
            _currentOccurrencesTargetName = assetDisplayName.ToLowerInvariant();
            _currentOccurrencesList.Clear();

            string cleanSearch = PersistenceUtility.CleanAssetName(assetDisplayName).ToLowerInvariant();

            for (int i = 0; i < EditorSessionManager.PlacedObjects.Count; i++)
            {
                GameObject obj = EditorSessionManager.PlacedObjects[i];
                if (obj == null) continue;
                string objName = obj.name.ToLowerInvariant();
                string objClean = PersistenceUtility.CleanAssetName(obj.name).ToLowerInvariant();

                if (objName.Contains(_currentOccurrencesTargetName) || objClean.Contains(cleanSearch))
                {
                    _currentOccurrencesList.Add(obj);
                }
            }

            if (_occurrencesTitleHeader != null)
                _occurrencesTitleHeader.text = $"Found {_currentOccurrencesList.Count}x '{assetDisplayName}' in Scene:";

            for (int i = _occurrencesContent.childCount - 1; i >= 0; i--)
            {
                GameObject.Destroy(_occurrencesContent.GetChild(i).gameObject);
            }

            for (int i = 0; i < _currentOccurrencesList.Count; i++)
            {
                GameObject target = _currentOccurrencesList[i];
                GameObject row = new GameObject("Occ_Row", Il2CppType.Of<RectTransform>());
                row.transform.SetParent(_occurrencesContent, false);

                LayoutElement le = row.AddComponent<LayoutElement>();
                le.preferredHeight = 26f;
                le.minHeight = 26f;

                Image bg = row.AddComponent<Image>();
                bg.color = new Color(0.14f, 0.17f, 0.22f, 0.95f);

                Button btn = row.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener((Action)(() =>
                {
                    EditorSessionManager.SelectObject(target);
                    EditorViewportCamera.FocusOnObject(target);
                }));

                Vector3 p = target.transform.position;
                string label = $"#{i + 1:D2}: {target.name} ({p.x:F1}, {p.y:F1}, {p.z:F1})";
                CreateTextPrimitive(row.transform, label, Vector2.zero, Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, 0f), 10f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);
            }

            _occurrencesWin.Show();
        }

        private static void MassSelectCurrentOccurrences()
        {
            if (_currentOccurrencesList.Count == 0) return;
            EditorSessionManager.SelectedObjects.Clear();
            for (int i = 0; i < _currentOccurrencesList.Count; i++)
            {
                if (_currentOccurrencesList[i] != null) EditorSessionManager.SelectedObjects.Add(_currentOccurrencesList[i]);
            }
            EditorSessionManager.SelectedObject = EditorSessionManager.SelectedObjects[0];
            EditorSessionManager.UpdateSelectionHighlight();
            NotifyObjectSelected(EditorSessionManager.SelectedObject);
            RefreshHierarchy();
        }

        private static void BuildUniformScalerWindow()
        {
            _uniformScalerWin = StudioFloatingWindow.Create(_canvasRoot.transform, "Win_UniformScaler", "Uniform Multiplier Scaler", new Vector2(350f, 220f), Vector2.zero);

            VerticalLayoutGroup vlg = _uniformScalerWin.ContentRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            CreateTextPrimitive(_uniformScalerWin.ContentRt, "Multiply selected objects' size uniformly:", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10.5f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);

            GameObject chipRow = CreateRowContainerPrimitive(_uniformScalerWin.ContentRt, "Chips", 24f);
            SetupRowHorizontalLayoutPrimitive(chipRow, 4f);
            float[] presets = new float[] { 0.5f, 0.75f, 1.25f, 1.5f, 2.0f };
            for (int i = 0; i < presets.Length; i++)
            {
                float factor = presets[i];
                CreateButtonPrimitive(chipRow.transform, $"Chip_{factor}", $"{factor:F2}x", 50f, () => ApplyUniformScaleFactor(factor), new Color(0.18f, 0.22f, 0.30f));
            }

            AddSliderRow(_uniformScalerWin.ContentRt, "Multiplier", 0.1f, 5.0f, _uniformScaleValue, "{0:F2}x", (val) =>
            {
                _uniformScaleValue = (float)Math.Round(val, 2);
            });

            AddToggleRow(_uniformScalerWin.ContentRt, "Scale around collective Centroid Pivot", _scaleCentroidPivot, (toggled) =>
            {
                _scaleCentroidPivot = toggled;
            });

            CreateButtonPrimitive(_uniformScalerWin.ContentRt, "Btn_ApplyScale", "Apply Scale Multiplier", 320f, () =>
            {
                ApplyUniformScaleFactor(_uniformScaleValue);
            }, new Color(0.2f, 0.65f, 0.95f, 1f));
        }

        private static void ApplyUniformScaleFactor(float factor)
        {
            if (EditorSessionManager.SelectedObjects == null || EditorSessionManager.SelectedObjects.Count == 0) return;
            if (Mathf.Abs(factor - 1.0f) < 0.001f) return;

            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < EditorSessionManager.SelectedObjects.Count; i++)
                centroid += EditorSessionManager.SelectedObjects[i].transform.position;
            centroid /= EditorSessionManager.SelectedObjects.Count;

            for (int i = 0; i < EditorSessionManager.SelectedObjects.Count; i++)
            {
                GameObject obj = EditorSessionManager.SelectedObjects[i];
                if (obj == null) continue;

                Vector3 oldPos = obj.transform.position;
                Vector3 oldScale = obj.transform.localScale;

                Vector3 newScale = oldScale * factor;
                newScale.x = Mathf.Max(0.01f, newScale.x);
                newScale.y = Mathf.Max(0.01f, newScale.y);
                newScale.z = Mathf.Max(0.01f, newScale.z);

                Vector3 newPos = oldPos;
                if (_scaleCentroidPivot)
                    newPos = centroid + (oldPos - centroid) * factor;

                obj.transform.position = newPos;
                obj.transform.localScale = newScale;

                StudioGizmoController.InvalidateCachedCenter(obj);
            }

            RefreshInspectorValues();
            EditorSessionManager.UpdateSelectionHighlight();
        }

        private static void BuildDistributeSpacingWindow()
        {
            _distributeSpacingWin = StudioFloatingWindow.Create(_canvasRoot.transform, "Win_Distribute", "Distribute & Matrix Cloner", new Vector2(380f, 320f), Vector2.zero);

            VerticalLayoutGroup vlg = _distributeSpacingWin.ContentRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            CreateTextPrimitive(_distributeSpacingWin.ContentRt, "-- Distribute 3+ Selected Along Axis --", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);

            GameObject btnRow = CreateRowContainerPrimitive(_distributeSpacingWin.ContentRt, "Dist_AxisBtns", 28f);
            SetupRowHorizontalLayoutPrimitive(btnRow, 6f);
            CreateButtonPrimitive(btnRow.transform, "Dist_X", "Distribute X", 100f, () => DistributeSelectedAlongAxis(0), new Color(0.85f, 0.25f, 0.25f, 1f));
            CreateButtonPrimitive(btnRow.transform, "Dist_Y", "Distribute Y", 100f, () => DistributeSelectedAlongAxis(1), new Color(0.25f, 0.85f, 0.35f, 1f));
            CreateButtonPrimitive(btnRow.transform, "Dist_Z", "Distribute Z", 100f, () => DistributeSelectedAlongAxis(2), new Color(0.25f, 0.55f, 0.95f, 1f));

            CreateTextPrimitive(_distributeSpacingWin.ContentRt, "-- 3D Matrix Cloner Array (Select 1 Object) --", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Bold, Color.cyan, TextAlignmentOptions.MidlineLeft);

            AddSliderRow(_distributeSpacingWin.ContentRt, "Columns X", 1f, 8f, _arrayCountX, "{0:F0}", (v) => _arrayCountX = Mathf.RoundToInt(v));
            AddSliderRow(_distributeSpacingWin.ContentRt, "Columns Z", 1f, 8f, _arrayCountZ, "{0:F0}", (v) => _arrayCountZ = Mathf.RoundToInt(v));
            AddSliderRow(_distributeSpacingWin.ContentRt, "Spacing X (m)", 0.5f, 20f, _arraySpacingX, "{0:F1}m", (v) => _arraySpacingX = v);
            AddSliderRow(_distributeSpacingWin.ContentRt, "Spacing Z (m)", 0.5f, 20f, _arraySpacingZ, "{0:F1}m", (v) => _arraySpacingZ = v);

            CreateButtonPrimitive(_distributeSpacingWin.ContentRt, "Btn_GenerateMatrix", "Generate Clone Grid", 340f, () =>
            {
                GenerateMatrixClones();
            }, new Color(0.2f, 0.65f, 0.95f, 1f));
        }

        private static void GenerateMatrixClones()
        {
            if (EditorSessionManager.SelectedObject == null)
            {
                SetNotificationText("Select an object to generate clones from.");
                return;
            }

            GameObject src = EditorSessionManager.SelectedObject;
            string rawName = src.name.StartsWith("Custom_") ? src.name.Substring(7) : src.name;
            EditorEntityData data = EditorSessionManager.ExtractEntityData(src);
            Vector3 origin = src.transform.position;

            int spawned = 0;
            for (int x = 0; x < _arrayCountX; x++)
            {
                for (int z = 0; z < _arrayCountZ; z++)
                {
                    if (x == 0 && z == 0) continue;
                    Vector3 p = origin + new Vector3(x * _arraySpacingX, 0f, z * _arraySpacingZ);
                    GameObject clone = EditorSessionManager.SpawnAssetByName(rawName, p, src.transform.localScale, src.transform.rotation);
                    if (clone != null)
                    {
                        EditorSessionManager.ApplyEntityData(clone, data.Clone());
                        EditorSessionManager.RegisterPlacedObject(clone);
                        spawned++;
                    }
                }
            }

            RefreshHierarchy();
            SetNotificationText($"Matrix created: {spawned} clones added.");
        }

        private static void DistributeSelectedAlongAxis(int axis)
        {
            if (EditorSessionManager.SelectedObjects == null || EditorSessionManager.SelectedObjects.Count < 3)
            {
                SetNotificationText("Select 3 or more objects to distribute.");
                return;
            }

            var list = new List<GameObject>(EditorSessionManager.SelectedObjects);
            list.Sort((a, b) =>
            {
                float valA = axis == 0 ? a.transform.position.x : (axis == 1 ? a.transform.position.y : a.transform.position.z);
                float valB = axis == 0 ? b.transform.position.x : (axis == 1 ? b.transform.position.y : b.transform.position.z);
                return valA.CompareTo(valB);
            });

            Vector3 firstPos = list[0].transform.position;
            Vector3 lastPos = list[list.Count - 1].transform.position;

            for (int i = 1; i < list.Count - 1; i++)
            {
                float t = i / (float)(list.Count - 1);
                Vector3 p = list[i].transform.position;
                if (axis == 0) p.x = Mathf.Lerp(firstPos.x, lastPos.x, t);
                else if (axis == 1) p.y = Mathf.Lerp(firstPos.y, lastPos.y, t);
                else p.z = Mathf.Lerp(firstPos.z, lastPos.z, t);

                list[i].transform.position = p;
                StudioGizmoController.InvalidateCachedCenter(list[i]);
            }

            RefreshInspectorValues();
            EditorSessionManager.UpdateSelectionHighlight();
        }

        private static void BuildAssignCategoryWindow()
        {
            _assignCategoryWin = StudioFloatingWindow.Create(_canvasRoot.transform, "Win_AssignCategory", "Custom Tabs & Categories", new Vector2(380f, 320f), Vector2.zero);

            VerticalLayoutGroup vlg = _assignCategoryWin.ContentRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            GameObject createRow = CreateRowContainerPrimitive(_assignCategoryWin.ContentRt, "CreateTabRow", 28f);
            SetupRowHorizontalLayoutPrimitive(createRow, 6f);

            _assignCategoryInput = CreateInputFieldPrimitive(createRow.transform, "NewCategoryInput", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, "New tab name...", null);
            LayoutElement inLe = _assignCategoryInput.gameObject.AddComponent<LayoutElement>();
            inLe.flexibleWidth = 1f;

            CreateButtonPrimitive(createRow.transform, "Btn_CreateTab", "+ Create", 80f, () =>
            {
                if (_assignCategoryInput != null && !string.IsNullOrWhiteSpace(_assignCategoryInput.text))
                {
                    string newCat = _assignCategoryInput.text.Trim();
                    EditorConfigService.CreateCategory(newCat);
                    if (_assignTargetAsset != null)
                    {
                        EditorConfigService.ToggleAssetInCategory(newCat, _assignTargetAsset.DisplayName);
                    }
                    _assignCategoryInput.text = "";
                    RebuildCategoryDockButtons();
                    RefreshCategoryListInAssignWindow();
                    RefreshAssetBrowser();
                }
            }, new Color(0.18f, 0.55f, 0.35f, 1f));

            CreateScrollViewPrimitive(_assignCategoryWin.ContentRt, "CategoryChips_Scroll", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, 190f), out _existingCategoryChipsRoot);
        }

        public static void OpenAssignCategoryWindow(CatalogAsset asset)
        {
            if (_assignCategoryWin == null) return;
            _assignTargetAsset = asset;

            _assignCategoryWin.TitleText.text = (_assignTargetAsset != null)
                ? $"Tabs: {_assignTargetAsset.DisplayName}"
                : "Manage Custom Tabs";

            RefreshCategoryListInAssignWindow();
            _assignCategoryWin.Show();
        }

        private static void RefreshCategoryListInAssignWindow()
        {
            if (_existingCategoryChipsRoot == null) return;

            for (int i = _existingCategoryChipsRoot.childCount - 1; i >= 0; i--)
            {
                GameObject.Destroy(_existingCategoryChipsRoot.GetChild(i).gameObject);
            }

            var cats = EditorConfigService.Config.CustomCategories;

            if (cats.Count == 0)
            {
                CreateTextPrimitive(_existingCategoryChipsRoot, "No custom tabs created yet.\nType a name above and click '+ Create'.", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, 40f), 10.5f, FontStyles.Italic, Color.gray, TextAlignmentOptions.Center);
                return;
            }

            foreach (var kvp in cats)
            {
                string catName = kvp.Key;
                bool isAssigned = _assignTargetAsset != null && kvp.Value != null && kvp.Value.Contains(_assignTargetAsset.DisplayName);

                GameObject row = CreateRowContainerPrimitive(_existingCategoryChipsRoot, "Row_" + catName, 26f);
                SetupRowHorizontalLayoutPrimitive(row, 6f);

                string btnLabel = (_assignTargetAsset != null)
                    ? (isAssigned ? $"[X] {catName}" : $"[ + ] {catName}")
                    : $"Tab: {catName} ({kvp.Value?.Count ?? 0} props)";

                Color btnCol = isAssigned ? new Color(0.18f, 0.52f, 0.88f, 0.95f) : new Color(0.16f, 0.18f, 0.24f, 0.90f);

                Button toggleBtn = CreateButtonPrimitive(row.transform, "Btn_Toggle_" + catName, btnLabel, 260f, () =>
                {
                    if (_assignTargetAsset != null)
                    {
                        EditorConfigService.ToggleAssetInCategory(catName, _assignTargetAsset.DisplayName);
                        RefreshCategoryListInAssignWindow();
                        RefreshAssetBrowser();
                    }
                }, btnCol);
                toggleBtn.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

                Button delBtn = CreateButtonPrimitive(row.transform, "Btn_DelCat_" + catName, "X", 28f, () =>
                {
                    EditorConfigService.DeleteCategory(catName);
                    RebuildCategoryDockButtons();
                    RefreshCategoryListInAssignWindow();
                    RefreshAssetBrowser();
                }, new Color(0.65f, 0.2f, 0.2f, 0.9f));
                delBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 28f;
            }
        }

        private static void BuildBatchRenamerWindow()
        {
            _batchRenamerWin = StudioFloatingWindow.Create(_canvasRoot.transform, "Win_BatchRenamer", "Batch Renamer & Replace", new Vector2(380f, 260f), Vector2.zero);

            VerticalLayoutGroup vlg = _batchRenamerWin.ContentRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            CreateTextPrimitive(_batchRenamerWin.ContentRt, "-- Sequential Suffix Renaming --", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);

            _batchRenameInput = CreateInputFieldPrimitive(_batchRenamerWin.ContentRt, "RenameInput", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, 26f), "Base Name (e.g. Tower Wall)", null);

            CreateButtonPrimitive(_batchRenamerWin.ContentRt, "Btn_ApplyRename", "Rename Selected Nodes", 340f, () =>
            {
                if (EditorSessionManager.SelectedObjects != null && _batchRenameInput != null && !string.IsNullOrWhiteSpace(_batchRenameInput.text))
                {
                    string baseName = _batchRenameInput.text.Trim();
                    for (int i = 0; i < EditorSessionManager.SelectedObjects.Count; i++)
                    {
                        GameObject o = EditorSessionManager.SelectedObjects[i];
                        if (o != null) o.name = $"Custom_{baseName} ({i + 1})";
                    }
                    RefreshHierarchy();
                    NotifyObjectSelected(EditorSessionManager.SelectedObject);
                    _batchRenamerWin.Hide();
                }
            }, new Color(0.2f, 0.65f, 0.95f, 1f));

            CreateTextPrimitive(_batchRenamerWin.ContentRt, "-- Find & Replace in Selection Names --", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Bold, Color.cyan, TextAlignmentOptions.MidlineLeft);

            _replaceFindInput = CreateInputFieldPrimitive(_batchRenamerWin.ContentRt, "FindInput", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, 24f), "Find string...", null);
            _replaceWithInput = CreateInputFieldPrimitive(_batchRenamerWin.ContentRt, "WithInput", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, 24f), "Replace with...", null);

            CreateButtonPrimitive(_batchRenamerWin.ContentRt, "Btn_ApplyReplace", "Replace in Selected", 340f, () =>
            {
                if (EditorSessionManager.SelectedObjects != null && _replaceFindInput != null && !string.IsNullOrEmpty(_replaceFindInput.text))
                {
                    string find = _replaceFindInput.text;
                    string rep = _replaceWithInput != null ? _replaceWithInput.text : "";
                    for (int i = 0; i < EditorSessionManager.SelectedObjects.Count; i++)
                    {
                        GameObject o = EditorSessionManager.SelectedObjects[i];
                        if (o != null) o.name = o.name.Replace(find, rep);
                    }
                    RefreshHierarchy();
                    NotifyObjectSelected(EditorSessionManager.SelectedObject);
                    _batchRenamerWin.Hide();
                }
            }, new Color(0.18f, 0.52f, 0.88f, 0.95f));
        }

        // =========================================================================
        // ISOMETRIC ASSET THUMBNAIL RENDERER
        // =========================================================================

        public static class AssetThumbnailRenderer
        {
            private static Camera _previewCam = null;
            private static GameObject _studioStage = null;
            private static Light _studioKeyLight = null;
            private static Light _studioFillLight = null;
            private static RenderTexture _previewRt = null;

            private static readonly Vector3 StagePosition = new Vector3(9000f, 9000f, 9000f);
            private static readonly Queue<CatalogAsset> _renderQueue = new Queue<CatalogAsset>();
            private static readonly Dictionary<CatalogAsset, Image> _pendingTargetImages = new Dictionary<CatalogAsset, Image>();

            public static void RequestThumbnail(CatalogAsset asset, Image targetImg)
            {
                if (asset == null || asset.ThumbnailSprite != null)
                {
                    if (asset != null && targetImg != null && asset.ThumbnailSprite != null)
                    {
                        targetImg.sprite = asset.ThumbnailSprite;
                        targetImg.color = Color.white;
                    }
                    return;
                }

                if (!_pendingTargetImages.ContainsKey(asset))
                {
                    _pendingTargetImages[asset] = targetImg;
                    _renderQueue.Enqueue(asset);
                }
            }

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
                _previewCam.backgroundColor = new Color(0.10f, 0.12f, 0.16f, 1f);
                _previewCam.cullingMask = 1 << 2;
                _previewCam.fieldOfView = 22f;
                _previewCam.nearClipPlane = 0.1f;
                _previewCam.farClipPlane = 500f;

                _previewRt = new RenderTexture(128, 128, 16, RenderTextureFormat.ARGB32);
                _previewCam.targetTexture = _previewRt;

                GameObject keyObj = new GameObject("Studio_KeyLight");
                keyObj.transform.SetParent(_studioStage.transform, false);
                _studioKeyLight = keyObj.AddComponent<Light>();
                _studioKeyLight.type = LightType.Directional;
                _studioKeyLight.color = new Color(1f, 0.97f, 0.92f);
                _studioKeyLight.intensity = 2400f;
                _studioKeyLight.cullingMask = 1 << 2;
                keyObj.transform.rotation = Quaternion.Euler(35f, -40f, 0f);

                GameObject fillObj = new GameObject("Studio_FillLight");
                fillObj.transform.SetParent(_studioStage.transform, false);
                _studioFillLight = fillObj.AddComponent<Light>();
                _studioFillLight.type = LightType.Directional;
                _studioFillLight.color = new Color(0.65f, 0.75f, 0.90f);
                _studioFillLight.intensity = 900f;
                _studioFillLight.cullingMask = 1 << 2;
                fillObj.transform.rotation = Quaternion.Euler(-25f, 140f, 0f);
            }

            public static void ProcessQueueTick()
            {
                if (_renderQueue.Count == 0) return;

                CatalogAsset asset = _renderQueue.Dequeue();
                if (asset == null || asset.SourceTemplate == null) return;

                EnsureStudio();

                GameObject tempModel = GameObject.Instantiate(asset.SourceTemplate);
                tempModel.SetActive(true);
                tempModel.transform.position = StagePosition;
                tempModel.transform.rotation = Quaternion.Euler(22f, -42f, 0f) * asset.BaseRotation;
                tempModel.transform.localScale = Vector3.one * asset.DefaultScale;

                foreach (var tr in tempModel.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = 2;
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

                if (_pendingTargetImages.TryGetValue(asset, out Image targetImg) && targetImg != null)
                {
                    targetImg.sprite = sprite;
                    targetImg.color = Color.white;
                    _pendingTargetImages.Remove(asset);
                }
            }

            public static void Cleanup()
            {
                _renderQueue.Clear();
                _pendingTargetImages.Clear();
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
    }
}