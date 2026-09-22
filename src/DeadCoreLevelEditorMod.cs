using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2Cpp;
using Il2CppDeadCore.UI;

// Explicit alias to resolve ambiguity with Il2Cpp.SceneManager
using SceneManager = UnityEngine.SceneManagement.SceneManager;

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 14: MELONMOD RUNTIME ENTRY POINT & SHORTCUT ORCHESTRATION
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
            LoggerInstance.Msg("   DeadCore Level Editor Suite - Modular Studio Edition        ");
            LoggerInstance.Msg("===============================================================");
            MapBrowserService.EnsureDirectories();
            MapBrowserService.ScanStagingScenes();
            EditorConfigService.LoadConfig();
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

            // Interactive Shortcut Rebinder Listening Tick
            StudioUIManager.UpdateRebindingTick();

            // Run UI ticks, scrolling & click-outside detectors
            StudioUIManager.UpdateUI();

            // Configurable Keyboard Shortcuts Routing
            if (GUIUtility.keyboardControl == 0)
            {
                bool isCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

                if (EditorConfigService.IsShortcutTriggered("SelectAll") || (isCtrl && Input.GetKeyDown(KeyCode.A)))
                    EditorSessionManager.SelectAllPlacedObjects();

                if (EditorConfigService.IsCustomShortcutTriggered("TogglePlaytest", "F1"))
                    EditorSessionManager.ToggleEditMode();

                if (EditorSessionManager.IsEditModeActive)
                {
                    if (isCtrl && Input.GetKeyDown(KeyCode.G))
                        EditorSessionManager.ParentSelectedObjects();

                    if (EditorConfigService.IsCustomShortcutTriggered("SaveLevel", "F5"))
                        LevelPersistenceService.SaveLevel(MapBrowserService.SelectedMapName);
                    else if (EditorConfigService.IsCustomShortcutTriggered("LoadLevel", "F6"))
                        LevelPersistenceService.LoadLevel(MapBrowserService.SelectedMapName);
                    else if (EditorConfigService.IsCustomShortcutTriggered("Undo", "Ctrl+Z"))
                        EditorSessionManager.PerformUndo();
                    else if (EditorConfigService.IsCustomShortcutTriggered("Redo", "Ctrl+Y"))
                        EditorSessionManager.PerformRedo();
                    else if (EditorConfigService.IsCustomShortcutTriggered("Duplicate", "Ctrl+D"))
                        EditorSessionManager.DuplicateSelectedObjects();
                    else if (EditorConfigService.IsCustomShortcutTriggered("Parent", "Ctrl+P"))
                        EditorSessionManager.ParentSelectedObjects();
                    else if (EditorConfigService.IsCustomShortcutTriggered("Unparent", "Alt+P"))
                        EditorSessionManager.UnparentSelectedObjects();
                    else if (EditorConfigService.IsCustomShortcutTriggered("Delete", "Delete") && !StudioUIManager.IsPointerOverUI())
                        EditorSessionManager.DeleteSelectedObjects();

                    if (EditorConfigService.IsShortcutTriggered("CreatePrefab"))
                        StudioUIManager.QuickAutoCreatePrefab();
                    else if (EditorConfigService.IsShortcutTriggered("Snap90"))
                        StudioUIManager.ExecuteSnap90();
                    else if (EditorConfigService.IsShortcutTriggered("ResetRot"))
                        StudioUIManager.ExecuteResetRotation();
                    else if (EditorConfigService.IsShortcutTriggered("FocusCamera") && EditorSessionManager.SelectedObject != null && !StudioUIManager.IsPointerOverUI())
                        EditorViewportCamera.FocusOnObject(EditorSessionManager.SelectedObject);
                }
            }

            StudioUIManager.EnsureSelectableColliders();
            StudioUIManager.AssetThumbnailRenderer.ProcessQueueTick();
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

            if (tabIndex == 1) // Community Tab
            {
                CommunityLevelService.FetchCommunityLevels(() =>
                {
                    NativeLogsMenuHijacker.TransformLogsMenu(menu, ActiveTab);
                });
            }
            else
            {
                NativeLogsMenuHijacker.TransformLogsMenu(menu, ActiveTab);
            }
        }
    }
}