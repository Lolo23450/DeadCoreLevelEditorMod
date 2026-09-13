using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using MelonLoader;
using HarmonyLib;
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

[assembly: MelonInfo(typeof(DeadCoreEditor.DeadCoreLevelEditorMod), "DeadCore Level Editor Suite", "5.4.0", "Trufa")]
[assembly: MelonGame(null, null)]

namespace DeadCoreEditor
{
    public class DeadCoreLevelEditorMod : MelonMod
    {
        private static LogsMenu _lastTransformedLogsMenu = null;
        public static int ActiveTab = 0; // Tab index: 0 = Local Levels, 1 = Community
        private static int _lastObservedTab = -1;
        private static float _titleButtonScanTimer = 0f;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("=== DeadCore Level Editor Suite Initialized ===");
            MapBrowserService.EnsureDirectories();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _lastTransformedLogsMenu = null;
            _lastObservedTab = -1;

            string s = sceneName.ToLower();
            if (s.Contains("menu") || s.Contains("title") || s.Contains("boot") || s.Contains("intro"))
            {
                EditorSessionManager.ResetSession();
            }
        }

        public override void OnUpdate()
        {
            string currentScene = SceneManager.GetActiveScene().name.ToLower();
            bool isMenuScene = currentScene.Contains("menu") || currentScene.Contains("title") || currentScene.Contains("boot") || currentScene.Contains("root");

            if (isMenuScene)
            {
                _titleButtonScanTimer -= Time.deltaTime;
                if (_titleButtonScanTimer <= 0f)
                {
                    _titleButtonScanTimer = 0.5f;
                    NativeLogsMenuHijacker.ReplaceTitleScreenLogsButton();
                }

                if (Input.GetKeyDown(KeyCode.F2))
                {
                    NativeLogsMenuHijacker.OpenNativeMenu();
                }

                LogsMenu activeMenu = null;
                LogsMenu[] menus = Resources.FindObjectsOfTypeAll<LogsMenu>();
                for (int i = 0; i < menus.Length; i++)
                {
                    if (menus[i] != null && menus[i].gameObject.scene.isLoaded && menus[i].gameObject.activeInHierarchy)
                    {
                        activeMenu = menus[i];
                        break;
                    }
                }

                if (activeMenu != null)
                {
                    int currentDetectedTab = NativeLogsMenuHijacker.GetActiveNativeTab(activeMenu);

                    if (_lastTransformedLogsMenu != activeMenu || currentDetectedTab != _lastObservedTab)
                    {
                        _lastTransformedLogsMenu = activeMenu;
                        _lastObservedTab = currentDetectedTab;
                        ActiveTab = currentDetectedTab;

                        NativeLogsMenuHijacker.TransformLogsMenu(activeMenu, ActiveTab);
                    }

                    NativeLogsMenuHijacker.EnforceCustomListOnly(activeMenu);
                    NativeLogsMenuHijacker.EnforceBottomBarLabels();

                    // Process confirmation inputs only when input focus is not captured by text fields
                    if (GUIUtility.keyboardControl == 0)
                    {
                        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                        {
                            if (!string.IsNullOrEmpty(MapBrowserService.SelectedMapPath))
                            {
                                MapBrowserService.LaunchSelectedMap();
                            }
                        }

                        if (Input.GetKeyDown(KeyCode.Delete))
                        {
                            NativeLogsMenuHijacker.DeleteCurrentSelectedLevel(activeMenu);
                        }
                    }
                }
                else
                {
                    _lastTransformedLogsMenu = null;
                    _lastObservedTab = -1;
                }

                return;
            }

            if (!EditorSessionManager.IsCustomSessionActive) return;
            EditorSessionManager.UpdateSession();
        }

        public override void OnGUI()
        {
            // Render the active HUD only during custom level sessions
            if (EditorSessionManager.IsCustomSessionActive)
            {
                if (EditorSessionManager.IsEditModeActive)
                {
                    EditorSessionManager.DrawEditorGUI();
                }
                else
                {
                    EditorSessionManager.DrawPlaytestHUD();
                }
            }
        }

        public static void SwitchTab(int tabIndex, LogsMenu menu)
        {
            ActiveTab = tabIndex;
            _lastObservedTab = tabIndex;
            NativeLogsMenuHijacker.TransformLogsMenu(menu, ActiveTab);
        }
    }

    public enum AssetCategory
    {
        Gameplay = 0,      // Interactive elements: Jumpers, Gates, Checkpoints, Lasers, Turrets, Lights, Fans
        Architecture = 1,  // Structural geometry: Platforms, floors, walls, monoliths, pillars, cubes
        Props = 2          // Decorative and environment detailing: Crates, panels, structural trim
    }

    public class CatalogAsset
    {
        public string DisplayName;
        public GameObject SourceTemplate;
        public Mesh FilterMesh;
        public AssetCategory Category;
        public bool IsJumper;
        public bool IsCheckPoint;
        public bool IsSpawnGate;
        public bool IsGoalGate;
        public bool IsTurret;
        public bool IsHelix;
        public bool IsSpotlight;
        public bool IsLaser;
        public bool IsRotatingLaser;
        public float DefaultScale;
        public float VerticalOffset;
        public Quaternion BaseRotation;
    }

    public enum HistoryActionType
    {
        Placement,
        Deletion
    }

    public class HistoryRecord
    {
        public HistoryActionType ActionType;
        public GameObject TargetObject;
        public CatalogAsset Asset;
        public string AssetName;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Scale;
        public float CustomParameter;
    }

    public class LightConfig
    {
        public Color Color = Color.cyan;
        public float Range = 50f;
        public float SpotAngle = 60f;
        public float Intensity = 3.0f;
        public float VolumetricDimmer = 1.5f;
    }

    public class LevelMetadata
    {
        public string Title = "Untitled Level";
        public string Author = "Unknown";
        public string Difficulty = "Normal";
        public string Description = "No description provided.";
    }

    // =========================================================================================================
    // NATIVE LOGS MENU INTERFACE OVERRIDES
    // =========================================================================================================
    public static class NativeLogsMenuHijacker
    {
        private static GameObject _nativePlayButton = null;
        private static GameObject _nativeCreateButton = null;
        private static GameObject _nativeDeleteButton = null;
        private static readonly List<GameObject> _spawnedRowObjects = new List<GameObject>();

        private static Toggle _cachedMyLevelsToggle = null;
        private static Toggle _cachedCommunityToggle = null;
        private static LogsMenu _activeLogsMenuRef = null;

        // UI Element References: Metadata Panel
        private static GameObject _nativeMetadataRoot = null;
        private static TMP_InputField _titleInput = null;
        private static TMP_InputField _authorInput = null;
        private static TMP_InputField _descInput = null;
        private static readonly List<GameObject> _diffButtons = new List<GameObject>();
        private static int _selectedDifficultyIndex = 2; // Default index: 2 ("Normal")

        private static TMP_Text _statsLabelLeft = null;
        private static TMP_Text _statsLabelRight = null;
        private static TMP_Text _saveBtnText = null;
        private static float _saveFeedbackTimer = 0f;

        public static readonly string[] DifficultyNames = new string[] { "Very Easy", "Easy", "Normal", "Hard", "Expert" };
        public static readonly Color[] DifficultyColors = new Color[]
        {
            new Color(0.2f, 0.95f, 0.4f),  // Very Easy (Green)
            new Color(0.1f, 0.85f, 1.0f),  // Easy (Cyan)
            new Color(0.3f, 0.65f, 1.0f),  // Normal (Blue)
            new Color(1.0f, 0.55f, 0.1f),  // Hard (Orange)
            new Color(0.95f, 0.2f, 0.2f)   // Expert (Red)
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

                string[] allLines = File.ReadAllLines(fullPath);
                List<string> objectLines = new List<string>();

                for (int i = 0; i < allLines.Length; i++)
                {
                    string l = allLines[i];
                    if (string.IsNullOrWhiteSpace(l)) continue;
                    string t = l.Trim();
                    if (!t.StartsWith("#"))
                    {
                        objectLines.Add(l);
                    }
                }

                List<string> finalLines = new List<string>();
                finalLines.Add($"#TITLE: {title}");
                finalLines.Add($"#AUTHOR: {author}");
                finalLines.Add($"#DIFFICULTY: {diff}");
                finalLines.Add($"#DESC: {desc}");
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

        private static void RefreshRowTitlesInList(string newTitle)
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

        public static void ReplaceTitleScreenLogsButton()
        {
            TMP_Text[] allTmps = Resources.FindObjectsOfTypeAll<TMP_Text>();
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
                }
            }
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
                    {
                        MenuGroupScript.CurrentGroup.Close();
                    }
                    targetGroup.Open();
                    return;
                }
            }
        }

        public static int GetActiveNativeTab(LogsMenu menu)
        {
            if (_cachedCommunityToggle != null && _cachedCommunityToggle.isOn) return 1;
            if (_cachedMyLevelsToggle != null && _cachedMyLevelsToggle.isOn) return 0;

            Toggle[] allToggles = Resources.FindObjectsOfTypeAll<Toggle>();
            for (int i = 0; i < allToggles.Length; i++)
            {
                Toggle tog = allToggles[i];
                if (tog == null || !tog.gameObject.scene.isLoaded || !tog.isOn) continue;

                TMP_Text t = tog.GetComponentInChildren<TMP_Text>(true);
                if (t == null) continue;

                string txt = t.text.Trim().ToLower();
                if (txt.Contains("community") || txt.Contains("m-log")) return 1;
                if (txt.Contains("my levels") || txt.Contains("t-log")) return 0;
            }

            return 0;
        }

        public static void EnforceCustomListOnly(LogsMenu menu)
        {
            if (menu == null || menu._logsButtonRoot == null) return;

            int count = menu._logsButtonRoot.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform child = menu._logsButtonRoot.GetChild(i);
                if (child != null && !child.name.StartsWith("CustomMap_"))
                {
                    child.gameObject.SetActive(false);
                }
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
            _activeLogsMenuRef = menu;

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
                        {
                            GameObject.Destroy(child.gameObject);
                        }
                        else
                        {
                            child.gameObject.SetActive(false);
                        }
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
                        File.WriteAllText(def, $"#TITLE: Default Level\n#AUTHOR: Community\n#DIFFICULTY: Normal\n#DESC: Starter platform.\nFloor_Platform_16x16;{spawn.x:F4};{(spawn.y - 1.2f):F4};{spawn.z:F4};0.5500;0.0000;0.0000;0.0000;1.0000;0.00\n");
                    }
                    fileList.Add(def);
                }
            }
            else
            {
                if (Directory.Exists(MapBrowserService.DownloadedLevelsDir))
                    fileList.AddRange(Directory.GetFiles(MapBrowserService.DownloadedLevelsDir, "*.txt"));
            }

            if (fileList.Count == 0)
            {
                MapBrowserService.SelectedMapPath = "";
                MapBrowserService.SelectedMapName = "";

                if (menu._shortDesc != null)
                {
                    menu._shortDesc.text = "NO LEVELS FOUND IN THIS TAB";
                    DisableLocalizationScripts(menu._shortDesc.gameObject);
                }
                if (menu._longDesc != null)
                {
                    menu._longDesc.text = "";
                    DisableLocalizationScripts(menu._longDesc.gameObject);
                }
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
                    if (lt._idLabel != null)
                    {
                        lt._idLabel.text = (i + 1).ToString("D2");
                        lt._idLabel.enableWordWrapping = false;
                    }
                    if (lt._nameLabel != null)
                    {
                        lt._nameLabel.text = meta.Title;
                        lt._nameLabel.enableWordWrapping = false;
                    }
                    GameObject.DestroyImmediate(lt);
                }

                TMP_Text[] tmps = itemObj.GetComponentsInChildren<TMP_Text>(true);
                if (tmps.Length >= 2)
                {
                    tmps[0].text = (i + 1).ToString("D2");
                    tmps[0].enableWordWrapping = false;

                    tmps[1].text = meta.Title;
                    tmps[1].enableWordWrapping = false;
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

                for (int c = 0; c < itemObj.transform.childCount; c++)
                {
                    Transform ch = itemObj.transform.GetChild(c);
                    if (ch != null)
                    {
                        string cn = ch.name.ToLower();
                        if (cn.Contains("arrow") || cn.Contains("check") || cn.Contains("dash"))
                        {
                            ch.gameObject.SetActive(false);
                        }
                    }
                }

                string capturedPath = filePath;
                string capturedName = fileName;
                GameObject capturedObj = itemObj;

                btn.onClick = new Button.ButtonClickedEvent();
                btn.onClick.AddListener((Action)(() =>
                {
                    OnLevelSelected(menu, capturedPath, capturedName, capturedObj);
                }));

                _spawnedRowObjects.Add(itemObj);

                if (i == 0 || capturedPath == MapBrowserService.SelectedMapPath)
                {
                    OnLevelSelected(menu, capturedPath, capturedName, capturedObj);
                }
            }

            SetupBottomBarButtons(menu);
        }

        private static void RebrandAndTrimNativeTabs(LogsMenu menu)
        {
            TMP_Text[] allTexts = Resources.FindObjectsOfTypeAll<TMP_Text>();
            for (int i = 0; i < allTexts.Length; i++)
            {
                TMP_Text t = allTexts[i];
                if (t == null || !t.gameObject.scene.isLoaded) continue;

                string clean = t.text.Trim().ToLower();

                if (clean.Contains("t-log") || clean == "my levels")
                {
                    t.text = "My Levels";
                    DisableLocalizationScripts(t.gameObject);

                    _cachedMyLevelsToggle = t.GetComponentInParent<Toggle>();
                    if (_cachedMyLevelsToggle != null)
                    {
                        _cachedMyLevelsToggle.onValueChanged.AddListener((Action<bool>)((isOn) =>
                        {
                            if (isOn) DeadCoreLevelEditorMod.SwitchTab(0, menu);
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
                        _cachedCommunityToggle.onValueChanged.AddListener((Action<bool>)((isOn) =>
                        {
                            if (isOn) DeadCoreLevelEditorMod.SwitchTab(1, menu);
                        }));
                    }
                }
                else if (clean.Contains("d-log"))
                {
                    Toggle dToggle = t.GetComponentInParent<Toggle>();
                    if (dToggle != null)
                    {
                        dToggle.gameObject.SetActive(false);
                    }
                    else if (t.transform.parent != null)
                    {
                        t.transform.parent.gameObject.SetActive(false);
                    }
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

            for (int i = 0; i < _spawnedRowObjects.Count; i++)
            {
                GameObject row = _spawnedRowObjects[i];
                if (row == null) continue;

                Image img = row.GetComponentInChildren<Image>(true);
                if (img != null)
                {
                    img.color = (row == selectedRowObj) ? new Color(0.2f, 0.7f, 0.95f, 0.85f) : new Color(0.12f, 0.15f, 0.2f, 0.5f);
                }
            }

            BuildOrSyncNativeMetadataPanel(menu, meta, objectCount, fullPath);
            MelonLogger.Msg($">> [Native UI] Selected: '{meta.Title}' by '{meta.Author}' ({objectCount} objects)");
        }

        private static void BuildOrSyncNativeMetadataPanel(LogsMenu menu, LevelMetadata meta, int objectCount, string fullPath)
        {
            if (menu == null || menu._logBigPicture == null) return;

            Transform parent = menu._logBigPicture.transform;
            bool isMyLevels = (DeadCoreLevelEditorMod.ActiveTab == 0);

            // Toggle native description fields depending on current mode
            if (menu._shortDesc != null)
            {
                menu._shortDesc.gameObject.SetActive(!isMyLevels);
                if (!isMyLevels)
                {
                    menu._shortDesc.text = $"<b>{meta.Title.ToUpper()}</b>\nBY: {meta.Author.ToUpper()}  |  [{meta.Difficulty.ToUpper()}]  |  OBJECTS: {objectCount}";
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

            // Suppress native lore indicator elements in local level mode
            TMP_Text[] parentTmps = parent.GetComponentsInChildren<TMP_Text>(true);
            for (int t = 0; t < parentTmps.Length; t++)
            {
                if (parentTmps[t] == null) continue;
                if (_nativeMetadataRoot != null && parentTmps[t].transform.IsChildOf(_nativeMetadataRoot.transform)) continue;

                string txt = parentTmps[t].text.Trim();
                if (txt == "..." || txt.Contains("..."))
                {
                    parentTmps[t].gameObject.SetActive(!isMyLevels);
                }
            }

            // Suppress native panel expansion triggers in local level mode
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform ch = parent.GetChild(i);
                if (ch != null && ch.name.ToLower().Contains("expand"))
                {
                    ch.gameObject.SetActive(!isMyLevels);
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

                // Initialize metadata input fields
                CreateNativeInputRow(_nativeMetadataRoot.transform, sampleText, "LEVEL TITLE", 0f, 44f, 28f, out _titleInput);
                CreateNativeInputRow(_nativeMetadataRoot.transform, sampleText, "AUTHOR", -54f, 44f, 24f, out _authorInput);
                CreateDifficultyRow(_nativeMetadataRoot.transform, sampleText, -108f);
                CreateNativeInputRow(_nativeMetadataRoot.transform, sampleText, "DESCRIPTION", -164f, 95f, 22f, out _descInput, true);

                // Render level statistics summary panel
                CreateLevelStatsHUD(_nativeMetadataRoot.transform, sampleText, -272f);

                // Render metadata commit action button
                CreateNativeSaveButton(_nativeMetadataRoot.transform, sampleText, -368f);
            }

            _nativeMetadataRoot.SetActive(isMyLevels);

            if (_titleInput != null) _titleInput.text = meta.Title;
            if (_authorInput != null) _authorInput.text = meta.Author;
            if (_descInput != null) _descInput.text = meta.Description;

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
            labelRt.anchoredPosition = Vector2.zero;

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
            inRt.anchoredPosition = Vector2.zero;

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
                trt.sizeDelta = Vector2.zero;

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

            int lasers = 0, jumpers = 0, turbines = 0, turrets = 0;
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
                }
            }
            catch { }

            DateTime mod = File.GetLastWriteTime(fullPath);
            _statsLabelLeft.text = $"• TOTAL OBJECTS: <b><color=#00E5FF>{objectCount}</color></b>\n• HAZARDS & LASERS: <b><color=#FF5252>{lasers}</color></b>\n• JUMP PADS: <b><color=#FFEB3B>{jumpers}</color></b>";
            _statsLabelRight.text = $"• TURBINES & FANS: <b><color=#69F0AE>{turbines}</color></b>\n• TURRET ENEMIES: <b><color=#FF4081>{turrets}</color></b>\n• LAST SAVED: <color=#B0BEC5>{mod:dd/MM/yyyy HH:mm}</color>";
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
            b.onClick.AddListener((Action)(() =>
            {
                SaveCurrentMetadata(MapBrowserService.SelectedMapPath);
            }));

            GameObject txt = new GameObject("Text");
            txt.transform.SetParent(saveBtn.transform, false);
            RectTransform trt = txt.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;

            _saveBtnText = txt.AddComponent<TextMeshProUGUI>();
            if (sampleTmp != null) { _saveBtnText.font = sampleTmp.font; _saveBtnText.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
            _saveBtnText.fontSize = 22f;
            _saveBtnText.text = "SAVE DETAILS";
            _saveBtnText.fontStyle = FontStyles.Bold;
            _saveBtnText.color = Color.white;
            _saveBtnText.alignment = TextAlignmentOptions.Center;
        }

        // =====================================================================================================
        // BOTTOM NAVIGATION BAR CONFIGURATION
        // =====================================================================================================
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

            // 1. Play Button (Cyan Accent)
            if (_nativePlayButton == null || _nativePlayButton.Equals(null) || _nativePlayButton.transform.parent != parentBar)
            {
                if (_nativePlayButton != null) GameObject.Destroy(_nativePlayButton);
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

            // 2. New Level Creation Button (Green Accent)
            if (_nativeCreateButton == null || _nativeCreateButton.Equals(null) || _nativeCreateButton.transform.parent != parentBar)
            {
                if (_nativeCreateButton != null) GameObject.Destroy(_nativeCreateButton);
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
            createBtn.onClick.AddListener((Action)(() =>
            {
                CreateNewLevel(menu);
            }));

            // 3. Level Deletion Button (Red Accent)
            if (_nativeDeleteButton == null || _nativeDeleteButton.Equals(null) || _nativeDeleteButton.transform.parent != parentBar)
            {
                if (_nativeDeleteButton != null) GameObject.Destroy(_nativeDeleteButton);
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
            deleteBtn.onClick.AddListener((Action)(() =>
            {
                DeleteCurrentSelectedLevel(menu);
            }));

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
            string starterContent = $"#TITLE: New Level {idx}\n#AUTHOR: Player\n#DIFFICULTY: Normal\n#DESC: Custom level created with DeadCore Level Editor.\n" +
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
                {
                    GameObject.DestroyImmediate(c);
                }
            }

            LabelButton lb = btnObj.GetComponent<LabelButton>();
            if (lb != null) GameObject.DestroyImmediate(lb);

            Toggle tog = btnObj.GetComponent<Toggle>();
            if (tog != null) GameObject.DestroyImmediate(tog);

            LogToggle lt = btnObj.GetComponent<LogToggle>();
            if (lt != null) GameObject.DestroyImmediate(lt);

            for (int c = 0; c < btnObj.transform.childCount; c++)
            {
                Transform ch = btnObj.transform.GetChild(c);
                if (ch != null)
                {
                    string cn = ch.name.ToLower();
                    if (cn.Contains("dash") || cn.Contains("check"))
                    {
                        ch.gameObject.SetActive(false);
                    }
                }
            }
        }

        private static void TintChevron(GameObject btnObj, Color accentColor)
        {
            Image[] images = btnObj.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image img = images[i];
                if (img != null && img.color.r > 0.65f && img.color.g > 0.35f && img.color.b < 0.3f)
                {
                    img.color = accentColor;
                }
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

            Text[] legacy = btnObj.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < legacy.Length; i++)
            {
                if (legacy[i] != null)
                {
                    legacy[i].text = label;
                    legacy[i].color = Color.white;
                }
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

    // =========================================================================================================
    // MAP BROWSER FILE SYSTEM SERVICE
    // =========================================================================================================
    public static class MapBrowserService
    {
        public static bool IsBrowserOpen = false;
        public static string SelectedMapPath = "";
        public static string SelectedMapName = "Default_Level";

        public static string MyLevelsDir => Path.Combine(Directory.GetCurrentDirectory(), "UserData", "MyLevels");
        public static string DownloadedLevelsDir => Path.Combine(Directory.GetCurrentDirectory(), "UserData", "DownloadedLevels");

        public static void EnsureDirectories()
        {
            if (!Directory.Exists(MyLevelsDir)) Directory.CreateDirectory(MyLevelsDir);
            if (!Directory.Exists(DownloadedLevelsDir)) Directory.CreateDirectory(DownloadedLevelsDir);

            string defaultMyPath = Path.Combine(MyLevelsDir, "Default_Level.txt");
            if (!File.Exists(defaultMyPath))
            {
                Vector3 spawn = EditorSessionManager.LevelSpawnPosition;
                File.WriteAllText(defaultMyPath, $"#TITLE: Default Level\n#AUTHOR: Community\n#DIFFICULTY: Normal\n#DESC: Starter platform.\nFloor_Platform_16x16;{spawn.x:F4};{(spawn.y - 1.2f):F4};{spawn.z:F4};0.5500;0.0000;0.0000;0.0000;1.0000;0.00\n");
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

            MelonLogger.Msg($">> [Map Browser] Launching: '{SelectedMapName}' from {SelectedMapPath}");
            SceneLoader.LoadLevel("level01_Spark01", false, true);
        }
    }

    // =========================================================================================================
    // CORE EDITOR SESSION MANAGER
    // =========================================================================================================
    public static class EditorSessionManager
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public static bool CustomLevelSelected = true;
        public static bool IsCustomSessionActive = false;
        public static bool IsEditModeActive = false;
        public static bool IsLevelInitialized = false;

        public static List<CatalogAsset> AllAssets = new List<CatalogAsset>();
        public static AssetCategory CurrentTab = AssetCategory.Gameplay;
        public static int SelectedAssetIndex = 0;
        public static bool IsBlockSelected = false;

        public static float ActiveJumperForce = 25.0f;
        public static float ActiveTurbineSpeed = 35.0f;
        public static float ActiveTurretFireDelay = 1.0f;
        public static float ActivePlacementScale = 0.55f;
        public static float CurrentGridSnap = 1.0f;

        public static float ActiveLaserRotationSpeed = 45.0f;
        public static Dictionary<GameObject, float> LaserRotationSpeeds = new Dictionary<GameObject, float>();

        // Transform and Rotation State
        public static float TargetPitch = 0f;
        public static float TargetYaw = 0f;
        public static float TargetRoll = 0f;

        private static KeyCode _lastHeldKey = KeyCode.None;
        private static float _keyHoldDuration = 0f;
        private static float _keyRepeatTimer = 0f;

        // Spotlight Configuration State
        public static bool ShowParamsWindow = true;
        public static GameObject SelectedLightObject = null;
        public static Dictionary<GameObject, LightConfig> PlacedLights = new Dictionary<GameObject, LightConfig>();
        private static float _lightRefreshTimer = 0f;

        public static Material CachedSceneMaterial = null;

        public static Jumper PrefabJumper = null;
        public static CheckPointScript PrefabCheckPoint = null;
        public static GameObject PrefabHelix = null;
        public static GameObject PrefabTurret = null;

        public static List<GameObject> PlacedObjects = new List<GameObject>();
        public static GameObject LastPlacedObject = null;

        public static Dictionary<GameObject, float> JumperForces = new Dictionary<GameObject, float>();
        public static Dictionary<GameObject, float> TurbineSpeeds = new Dictionary<GameObject, float>();
        public static Dictionary<GameObject, float> TurretFireDelays = new Dictionary<GameObject, float>();

        public static Stack<HistoryRecord> UndoHistory = new Stack<HistoryRecord>();
        public static Stack<HistoryRecord> RedoHistory = new Stack<HistoryRecord>();

        private static string _notificationMessage = "";
        private static float _notificationTimer = 0f;

        private static bool _isBoostActive = false;
        private static Vector3 _currentBoostVelocity = Vector3.zero;
        private static float _jumperTriggerCooldown = 0f;

        public static float LevelTimer = 0f;
        public static bool IsLevelCompleted = false;

        public static Camera PlayerCameraInstance = null;
        public static CharacterController PlayerControllerInstance = null;
        public static Vector3 FrozenPlayerPosition = Vector3.zero;
        public static Quaternion FrozenPlayerRotation = Quaternion.identity;
        public static Vector3 LevelSpawnPosition = new Vector3(-241f, -95f, -6f);

        // Dynamic Kill Plane (calculated from lowest placed geometry)
        public static float VoidDeathY
        {
            get
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
                return lowest;
            }
        }

        public static List<CatalogAsset> ActiveTabAssets
        {
            get
            {
                var list = AllAssets.FindAll(a => a.Category == CurrentTab);
                return list;
            }
        }

        public static CatalogAsset CurrentAsset
        {
            get
            {
                var list = ActiveTabAssets;
                if (list.Count == 0) return null;
                int idx = Mathf.Clamp(SelectedAssetIndex, 0, list.Count - 1);
                return list[idx];
            }
        }

        public static bool IsMouseOverUI()
        {
            if (!ShowParamsWindow) return false;
            Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            Rect winRect = new Rect(Screen.width - 340f, 20f, 340f, 420f);
            return winRect.Contains(mouse);
        }

        public static void ShowNotification(string msg)
        {
            _notificationMessage = msg;
            _notificationTimer = 3.5f;
        }

        public static void ResetSession()
        {
            IsCustomSessionActive = false;
            IsEditModeActive = false;
            IsLevelInitialized = false;
            IsLevelCompleted = false;
            LevelTimer = 0f;

            EditorViewportCamera.DestroyCamera();
            PlacementHologramController.DestroyPreview();
            CarouselWheelToolbar.DestroyToolbar();

            PlayerCameraInstance = null;
            PlayerControllerInstance = null;

            AllAssets.Clear();
            CurrentTab = AssetCategory.Gameplay;
            SelectedAssetIndex = 0;
            IsBlockSelected = false;

            PrefabJumper = null;
            PrefabCheckPoint = null;
            PrefabHelix = null;
            PrefabTurret = null;

            PlacedObjects.Clear();
            PlacedLights.Clear();
            SelectedLightObject = null;
            LastPlacedObject = null;
            JumperForces.Clear();
            TurbineSpeeds.Clear();
            TurretFireDelays.Clear();
            UndoHistory.Clear();
            RedoHistory.Clear();

            _isBoostActive = false;
            _jumperTriggerCooldown = 0f;
            _notificationTimer = 0f;
            _lightRefreshTimer = 0f;

            TargetPitch = 0f;
            TargetYaw = 0f;
            TargetRoll = 0f;
            _lastHeldKey = KeyCode.None;
            _keyHoldDuration = 0f;
            _keyRepeatTimer = 0f;

            ActiveJumperForce = 25.0f;
            ActiveTurbineSpeed = 35.0f;
            ActiveTurretFireDelay = 1.0f;
            ActivePlacementScale = 0.55f;
            CurrentGridSnap = 1.0f;
        }

        public static void UpdateSession()
        {
            if (!IsLevelInitialized) return;

            if (_lightRefreshTimer > 0f)
            {
                _lightRefreshTimer -= Time.deltaTime;
                if (_lightRefreshTimer <= 0f)
                {
                    ForceRefreshAllLights();
                }
            }

            // Animate dynamic hazards during playtest mode
            if (!IsEditModeActive)
            {
                for (int i = 0; i < PlacedObjects.Count; i++)
                {
                    GameObject obj = PlacedObjects[i];
                    if (obj == null || !obj.activeSelf) continue;
                    if (obj.name.ToLower().Contains("rotating_laser") || obj.name.ToLower().Contains("rotating laser"))
                    {
                        float speed = LaserRotationSpeeds.ContainsKey(obj) ? LaserRotationSpeeds[obj] : ActiveLaserRotationSpeed;
                        obj.transform.Rotate(Vector3.up, speed * Time.deltaTime, Space.Self);
                    }
                }

                CheckVoidFall();
                CheckJumperBoostPhysics();
                CheckHelixWindPushing();
                CheckGoalTriggerArrival();
                CheckLaserBarriers();

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
                FreezePlayerEntity();
            }

            if (Input.GetKeyDown(KeyCode.F1))
            {
                ToggleEditMode();
            }

            if (Input.GetKeyDown(KeyCode.F3))
            {
                ShowParamsWindow = !ShowParamsWindow;
            }

            if (_notificationTimer > 0f) _notificationTimer -= Time.deltaTime;

            if (IsEditModeActive)
            {
                EditorViewportCamera.UpdateCamera();
                CarouselWheelToolbar.UpdateCarousel();
                PlacementHologramController.UpdatePlacement();
                HandleFlowShortcuts();
                HandleGhostRotationOnly();
                HandleMouseWheel();
                HandleObjectDeletion();
            }

            GameObject p = FindPlayerEntity();
            if (p != null && !IsEditModeActive)
            {
                Vector3 pPos = p.transform.position;
                for (int i = 0; i < PlacedObjects.Count; i++)
                {
                    GameObject obj = PlacedObjects[i];
                    if (obj == null || !obj.activeSelf) continue;
                    if (obj.name.ToLower().Contains("spawn") || obj.name.ToLower().Contains("goal")) continue;

                    CheckPointScript cp = obj.GetComponentInChildren<CheckPointScript>();
                    if (cp != null)
                    {
                        if (Vector3.Distance(pPos, obj.transform.position) < 3.0f)
                        {
                            if (ActiveCustomCheckpoint != cp)
                            {
                                ActiveCustomCheckpoint = cp;
                                MelonLogger.Msg($">> [Checkpoint] Tagged checkpoint at {obj.transform.position}!");
                            }
                        }
                    }
                }
            }
        }

        private static void CheckGoalTriggerArrival()
        {
            if (IsLevelCompleted) return;

            GameObject player = FindPlayerEntity();
            if (player == null) return;

            Vector3 pPos = player.transform.position;

            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                GameObject obj = PlacedObjects[i];
                if (obj == null || !obj.activeSelf) continue;

                if (obj.name.ToLower().Contains("goal"))
                {
                    Vector3 gPos = obj.transform.position;
                    float horizDist = Vector2.Distance(new Vector2(pPos.x, pPos.z), new Vector2(gPos.x, gPos.z));
                    float vertDist = Mathf.Abs(pPos.y - gPos.y);

                    if (horizDist < 3.2f && vertDist < 2.5f)
                    {
                        IsLevelCompleted = true;
                        Cursor.lockState = CursorLockMode.None;
                        Cursor.visible = true;
                        MelonLogger.Msg($">> [VICTORY] Level completed in {LevelTimer:F2} seconds!");
                        break;
                    }
                }
            }
        }

        private static void CheckJumperBoostPhysics()
        {
            if (_jumperTriggerCooldown > 0f) _jumperTriggerCooldown -= Time.deltaTime;

            GameObject player = FindPlayerEntity();
            if (player == null) return;

            CharacterController cc = player.GetComponentInChildren<CharacterController>();
            if (cc == null) return;

            if (_isBoostActive)
            {
                cc.Move(_currentBoostVelocity * Time.deltaTime);
                _currentBoostVelocity.y += -22f * Time.deltaTime;

                if (cc.isGrounded && _jumperTriggerCooldown < 0.2f)
                {
                    _isBoostActive = false;
                }
            }

            if (_jumperTriggerCooldown > 0f) return;

            Vector3 pPos = player.transform.position;

            foreach (var obj in PlacedObjects)
            {
                if (obj == null || !obj.activeSelf) continue;
                if (!JumperForces.ContainsKey(obj) && !obj.name.ToLower().Contains("jumper")) continue;

                Vector3 jPos = obj.transform.position;
                Vector3 diff = pPos - jPos;
                float horiz = new Vector2(diff.x, diff.z).magnitude;
                float vert = Mathf.Abs(diff.y);

                if (horiz < 2.5f && vert < 1.9f)
                {
                    float force = JumperForces.ContainsKey(obj) ? JumperForces[obj] : ActiveJumperForce;

                    Vector3 padUp = obj.transform.up;
                    Vector3 padForward = obj.transform.forward;

                    bool isAngled = Mathf.Abs(Vector3.Dot(padUp, Vector3.up)) < 0.96f;

                    Vector3 launchVelocity;
                    if (isAngled)
                    {
                        Vector3 horizDir = new Vector3(padForward.x, 0f, padForward.z).normalized;
                        if (horizDir.sqrMagnitude < 0.01f) horizDir = new Vector3(padUp.x, 0f, padUp.z).normalized;

                        float forwardSpeed = force * 1.6f;
                        float upwardLift = force * 0.45f;

                        launchVelocity = (horizDir * forwardSpeed) + (Vector3.up * upwardLift);
                    }
                    else
                    {
                        launchVelocity = Vector3.up * (force * 0.85f);
                    }

                    _currentBoostVelocity = launchVelocity;
                    _isBoostActive = true;
                    _jumperTriggerCooldown = 0.35f;

                    MelonLogger.Msg($">> [BOOST] Launched! Speed: {launchVelocity.magnitude:F1} m/s");
                    break;
                }
            }
        }

        private static void CheckHelixWindPushing()
        {
            GameObject player = FindPlayerEntity();
            if (player == null) return;

            CharacterController cc = player.GetComponentInChildren<CharacterController>();
            if (cc == null) return;

            Vector3 pPos = player.transform.position + Vector3.up * 1.0f;

            foreach (var obj in PlacedObjects)
            {
                if (obj == null || !obj.activeSelf) continue;
                if (!obj.name.ToLower().Contains("helix") && !obj.name.ToLower().Contains("helice")) continue;

                float speed = TurbineSpeeds.ContainsKey(obj) ? TurbineSpeeds[obj] : ActiveTurbineSpeed;

                Helix helixScript = obj.GetComponentInChildren<Helix>();
                if (helixScript != null && helixScript._hingeJoint != null)
                {
                    helixScript._hingeJoint.transform.Rotate(Vector3.forward, (speed * 12f) * Time.deltaTime, Space.Self);
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

                    if (perp.magnitude < radius)
                    {
                        Vector3 pushDir = forward;
                        pushDir.y = Mathf.Max(0.22f, pushDir.y);
                        pushDir.Normalize();

                        float pushSpeed = Mathf.Lerp(speed, speed * 0.25f, forwardDist / windRange);
                        cc.Move(pushDir * pushSpeed * Time.deltaTime);
                    }
                }
            }
        }

        private static void CheckLaserBarriers()
        {
            GameObject player = FindPlayerEntity();
            if (player == null) return;

            CharacterController cc = player.GetComponentInChildren<CharacterController>();
            if (cc == null) return;

            Vector3 playerCenter = player.transform.position + cc.center;
            float playerRadius = cc.radius + 0.1f;
            float playerHalfHeight = cc.height * 0.5f;

            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                GameObject obj = PlacedObjects[i];
                if (obj == null || !obj.activeSelf) continue;
                if (!obj.name.ToLower().Contains("laser")) continue;

                BoxCollider bc = obj.GetComponentInChildren<BoxCollider>();
                if (bc == null) continue;

                Vector3 localPlayer = obj.transform.InverseTransformPoint(playerCenter);
                Vector3 boxSize = bc.size;
                Vector3 boxCenter = bc.center;

                float dx = Mathf.Abs(localPlayer.x - boxCenter.x);
                float dy = Mathf.Abs(localPlayer.y - boxCenter.y);
                float dz = Mathf.Abs(localPlayer.z - boxCenter.z);

                Vector3 lossy = obj.transform.lossyScale;
                float limitX = (boxSize.x * 0.5f) + (playerRadius / Mathf.Max(0.001f, lossy.x));
                float limitY = (boxSize.y * 0.5f) + (playerHalfHeight / Mathf.Max(0.001f, lossy.y));
                float limitZ = (boxSize.z * 0.5f) + (playerRadius / Mathf.Max(0.001f, lossy.z));

                if (dx <= limitX && dy <= limitY && dz <= limitZ)
                {
                    MelonLogger.Msg(">> [LASER] Player touched deadly laser barrier! Respawning...");
                    RespawnPlayer(player);
                    break;
                }
            }
        }

        private static void FreezePlayerEntity()
        {
            GameObject player = FindPlayerEntity();
            if (player != null)
            {
                player.transform.position = FrozenPlayerPosition;
                player.transform.rotation = FrozenPlayerRotation;

                CharacterController cc = player.GetComponentInChildren<CharacterController>();
                if (cc != null && cc.enabled) cc.enabled = false;
            }
        }

        private static void CheckVoidFall()
        {
            GameObject player = FindPlayerEntity();
            if (player != null && player.transform.position.y < VoidDeathY)
            {
                RespawnPlayer(player);
            }
        }

        public static CheckPointScript ActiveCustomCheckpoint = null;

        public static void ClearLastCheckpoint()
        {
            ActiveCustomCheckpoint = null;
            try
            {
                IntPtr classPtr = Il2CppClassPointerStore<CheckPointScript>.NativeClassPtr;
                string[] possibleFields = new string[] { "<LastCheckPoint>k__BackingField", "_lastCheckPoint", "LastCheckPoint", "lastCheckPoint" };
                foreach (var fieldName in possibleFields)
                {
                    IntPtr f = IL2CPP.GetIl2CppField(classPtr, fieldName);
                    if (f != IntPtr.Zero)
                    {
                        IntPtr zero = IntPtr.Zero;
                        unsafe
                        {
                            IL2CPP.il2cpp_field_static_set_value(f, (void*)(&zero));
                        }
                        break;
                    }
                }
            }
            catch { }
        }

        public static void RestartRun()
        {
            GameObject player = FindPlayerEntity();
            if (player == null) return;

            CharacterController cc = player.GetComponentInChildren<CharacterController>();
            if (cc != null) cc.enabled = false;

            ClearLastCheckpoint();
            player.transform.position = LevelSpawnPosition + Vector3.up * 0.2f;

            foreach (var obj in PlacedObjects)
            {
                if (obj != null && obj.name.ToLower().Contains("spawn"))
                {
                    player.transform.rotation = obj.transform.rotation;
                    break;
                }
            }

            if (cc != null) cc.enabled = true;

            _isBoostActive = false;
            _currentBoostVelocity = Vector3.zero;
            _jumperTriggerCooldown = 0f;
            LevelTimer = 0f;
            IsLevelCompleted = false;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            UnfreezePlayerControls();
            MelonLogger.Msg(">> [Restart] Restarted run from the actual level start!");
        }

        public static void RespawnPlayer(GameObject player)
        {
            CharacterController cc = player.GetComponentInChildren<CharacterController>();
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
                player.transform.position = LevelSpawnPosition + Vector3.up * 0.2f;
                MelonLogger.Msg(">> [Respawn] Returned to level start.");
            }

            _isBoostActive = false;
            _jumperTriggerCooldown = 0f;

            if (cc != null) cc.enabled = true;
            UnfreezePlayerControls();
        }

        public static void UnfreezePlayerControls()
        {
            GameObject player = FindPlayerEntity();
            if (player == null) return;

            CharacterController cc = player.GetComponentInChildren<CharacterController>();
            if (cc != null) cc.enabled = true;

            foreach (var mb in player.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string typeName = mb.GetType().Name.ToLower();
                if (typeName.Contains("motor") || typeName.Contains("controller") || typeName.Contains("movement") || typeName.Contains("input") || typeName.Contains("look") || typeName.Contains("fps"))
                {
                    mb.enabled = true;
                }
            }
        }

        public static void ToggleEditMode()
        {
            IsEditModeActive = !IsEditModeActive;
            MelonLogger.Msg($">> Viewport Mode: {(IsEditModeActive ? "[3D HAMMER EDIT MODE]" : "[PLAYTEST MODE]")}");

            GameObject player = FindPlayerEntity();

            if (IsEditModeActive)
            {
                _isBoostActive = false;

                if (player != null)
                {
                    FrozenPlayerPosition = player.transform.position;
                    FrozenPlayerRotation = player.transform.rotation;
                    PlayerControllerInstance = player.GetComponentInChildren<CharacterController>();
                    if (PlayerControllerInstance != null) PlayerControllerInstance.enabled = false;
                }

                PlayerCameraInstance = Camera.main;

                EditorViewportCamera.InitializeCamera(PlayerCameraInstance);
                CarouselWheelToolbar.CreateToolbar(EditorViewportCamera.ViewportCamera);

                if (IsBlockSelected && CurrentAsset != null)
                    PlacementHologramController.SpawnHologram(CurrentAsset);
                else
                    PlacementHologramController.DestroyPreview();

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                EditorViewportCamera.DestroyCamera();
                PlacementHologramController.DestroyPreview();
                CarouselWheelToolbar.DestroyToolbar();

                if (PlayerCameraInstance != null) PlayerCameraInstance.enabled = true;
                if (PlayerControllerInstance != null) PlayerControllerInstance.enabled = true;

                UnfreezePlayerControls();

                if (!IsLevelCompleted)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                MelonLogger.Msg(">> Restored Playtest Mode.");
            }
        }

        private static void HandleFlowShortcuts()
        {
            // Category navigation: Numeric shortcuts (1 = Gameplay, 2 = Architecture, 3 = Props)
            if (!Input.GetMouseButton(1))
            {
                if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) SetCategory(AssetCategory.Gameplay);
                else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) SetCategory(AssetCategory.Architecture);
                else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) SetCategory(AssetCategory.Props);
                else if (Input.GetKeyDown(KeyCode.Tab))
                {
                    int step = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? -1 : 1;
                    CycleTabs(step);
                }
            }

            if (Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.Backspace))
            {
                if (IsBlockSelected)
                {
                    IsBlockSelected = false;
                    PlacementHologramController.DestroyPreview();
                    MelonLogger.Msg(">> [Deselect] Block unselected.");
                }
            }

            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) && !Input.GetMouseButton(1))
            {
                if (!IsBlockSelected)
                {
                    SelectCurrentAsset();
                }
            }

            if (Input.GetKeyDown(KeyCode.G))
            {
                CycleGridSnap();
            }

            if (Input.GetKeyDown(KeyCode.Z))
            {
                PerformUndo();
            }

            if (Input.GetKeyDown(KeyCode.Y))
            {
                PerformRedo();
            }

            if (Input.GetKeyDown(KeyCode.F5))
            {
                LevelPersistenceService.SaveLevel(MapBrowserService.SelectedMapName);
            }

            if (Input.GetKeyDown(KeyCode.F6))
            {
                LevelPersistenceService.LoadLevel(MapBrowserService.SelectedMapName);
            }
        }

        public static void SetCategory(AssetCategory newCategory)
        {
            if (CurrentTab == newCategory) return;
            CurrentTab = newCategory;
            SelectedAssetIndex = 0;

            CarouselWheelToolbar.BuildCarouselIcons();

            if (IsBlockSelected)
            {
                if (CurrentAsset != null)
                    PlacementHologramController.SpawnHologram(CurrentAsset);
                else
                    PlacementHologramController.DestroyPreview();
            }

            ShowNotification($"Category: [{CurrentTab}] ({ActiveTabAssets.Count} items)");
            MelonLogger.Msg($">> [CATEGORY] Switched to: [{CurrentTab}] ({ActiveTabAssets.Count} items)");
        }

        public static void CycleTabs(int direction = 1)
        {
            int totalCategories = Enum.GetValues(typeof(AssetCategory)).Length;
            int next = ((int)CurrentTab + direction + totalCategories) % totalCategories;
            SetCategory((AssetCategory)next);
        }

        public static void SelectCurrentAsset()
        {
            if (CurrentAsset == null) return;
            IsBlockSelected = true;
            ActivePlacementScale = CurrentAsset.DefaultScale;
            PlacementHologramController.SpawnHologram(CurrentAsset);
            MelonLogger.Msg($">> [Selected] '{CurrentAsset.DisplayName}'. Left-click in world to place.");
        }

        public static void HandleGhostRotationOnly()
        {
            if (Input.GetKeyDown(KeyCode.T))
            {
                TargetPitch = Mathf.Round(TargetPitch / 90f) * 90f;
                TargetYaw = Mathf.Round(TargetYaw / 90f) * 90f;
                TargetRoll = Mathf.Round(TargetRoll / 90f) * 90f;
                NormalizeAngles();
                ShowNotification("Snapped to Nearest 90°");
                return;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                TargetPitch = 0f;
                TargetYaw = 0f;
                TargetRoll = 0f;
                ShowNotification("Rotation Reset (0°, 0°, 0°)");
                return;
            }

            float step = 15f;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                step = 45f;
            else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                step = 5f;

            KeyCode activeKey = KeyCode.None;
            Vector3 rotationDelta = Vector3.zero;

            if (Input.GetKey(KeyCode.LeftArrow))
            {
                activeKey = KeyCode.LeftArrow;
                rotationDelta.y = -step;
            }
            else if (Input.GetKey(KeyCode.RightArrow))
            {
                activeKey = KeyCode.RightArrow;
                rotationDelta.y = step;
            }
            else if (Input.GetKey(KeyCode.UpArrow))
            {
                activeKey = KeyCode.UpArrow;
                rotationDelta.x = step;
            }
            else if (Input.GetKey(KeyCode.DownArrow))
            {
                activeKey = KeyCode.DownArrow;
                rotationDelta.x = -step;
            }
            else if (Input.GetKey(KeyCode.PageUp) || Input.GetKey(KeyCode.LeftBracket))
            {
                activeKey = KeyCode.PageUp;
                rotationDelta.z = -step;
            }
            else if (Input.GetKey(KeyCode.PageDown) || Input.GetKey(KeyCode.RightBracket))
            {
                activeKey = KeyCode.PageDown;
                rotationDelta.z = step;
            }

            if (activeKey == KeyCode.None)
            {
                _lastHeldKey = KeyCode.None;
                _keyHoldDuration = 0f;
                _keyRepeatTimer = 0f;
                return;
            }

            bool shouldStep = false;
            if (activeKey != _lastHeldKey)
            {
                _lastHeldKey = activeKey;
                _keyHoldDuration = 0f;
                _keyRepeatTimer = 0f;
                shouldStep = true;
            }
            else
            {
                _keyHoldDuration += Time.deltaTime;
                if (_keyHoldDuration > 0.22f)
                {
                    _keyRepeatTimer -= Time.deltaTime;
                    if (_keyRepeatTimer <= 0f)
                    {
                        _keyRepeatTimer = 0.07f;
                        shouldStep = true;
                    }
                }
            }

            if (shouldStep)
            {
                TargetPitch += rotationDelta.x;
                TargetYaw += rotationDelta.y;
                TargetRoll += rotationDelta.z;
                NormalizeAngles();
            }
        }

        private static void NormalizeAngles()
        {
            TargetPitch = (TargetPitch % 360f + 360f) % 360f;
            TargetYaw = (TargetYaw % 360f + 360f) % 360f;
            TargetRoll = (TargetRoll % 360f + 360f) % 360f;
        }

        private static void HandleMouseWheel()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) <= 0.01f) return;

            if (Input.GetKey(KeyCode.LeftShift))
            {
                if (CurrentAsset != null && CurrentAsset.IsJumper)
                {
                    ActiveJumperForce += Mathf.Sign(scroll) * 2.5f;
                    ActiveJumperForce = Mathf.Max(1.0f, ActiveJumperForce);
                    MelonLogger.Msg($">> [Jumper Force] Set: {ActiveJumperForce:F1}");

                    GameObject aimed = GetAimedPlacedObject();
                    if (aimed != null && (JumperForces.ContainsKey(aimed) || aimed.name.ToLower().Contains("jumper")))
                    {
                        ApplyJumperForce(aimed, ActiveJumperForce);
                    }
                }
                else if (CurrentAsset != null && CurrentAsset.IsHelix)
                {
                    ActiveTurbineSpeed += Mathf.Sign(scroll) * 5.0f;
                    ActiveTurbineSpeed = Mathf.Max(1.0f, ActiveTurbineSpeed);
                    MelonLogger.Msg($">> [Turbine Speed] Set: {ActiveTurbineSpeed:F1}");

                    GameObject aimed = GetAimedPlacedObject();
                    if (aimed != null && (TurbineSpeeds.ContainsKey(aimed) || aimed.name.ToLower().Contains("helix")))
                    {
                        ApplyTurbineSpeed(aimed, ActiveTurbineSpeed);
                    }
                }
                else if (CurrentAsset != null && CurrentAsset.IsRotatingLaser)
                {
                    ActiveLaserRotationSpeed += Mathf.Sign(scroll) * 5.0f;
                    MelonLogger.Msg($">> [Rotating Laser Speed] Set: {ActiveLaserRotationSpeed:F1}°/s");

                    GameObject aimed = GetAimedPlacedObject();
                    if (aimed != null && aimed.name.ToLower().Contains("rotating"))
                    {
                        LaserRotationSpeeds[aimed] = ActiveLaserRotationSpeed;
                    }
                }
                else if (CurrentAsset != null && CurrentAsset.IsTurret)
                {
                    ActiveTurretFireDelay -= Mathf.Sign(scroll) * 0.1f;
                    ActiveTurretFireDelay = Mathf.Max(0.05f, ActiveTurretFireDelay);
                    MelonLogger.Msg($">> [Turret Fire Delay] Set: {ActiveTurretFireDelay:F2}s");

                    GameObject aimed = GetAimedPlacedObject();
                    if (aimed != null && (TurretFireDelays.ContainsKey(aimed) || aimed.name.ToLower().Contains("turret")))
                    {
                        ApplyTurretSettings(aimed, ActiveTurretFireDelay, 1500f);
                    }
                }
                else
                {
                    ActivePlacementScale += Mathf.Sign(scroll) * 0.05f;
                    ActivePlacementScale = Mathf.Clamp(ActivePlacementScale, 0.01f, 50.0f);
                    PlacementHologramController.ApplyScaleToPreview();
                }
            }
            else
            {
                var list = ActiveTabAssets;
                if (list.Count > 0)
                {
                    int step = (scroll > 0) ? 1 : -1;
                    SelectedAssetIndex = (SelectedAssetIndex + step + list.Count) % list.Count;
                    CarouselWheelToolbar.SetTargetIndex(SelectedAssetIndex);

                    if (IsBlockSelected)
                    {
                        PlacementHologramController.SpawnHologram(CurrentAsset);
                    }
                }
            }
        }

        private static void HandleObjectDeletion()
        {
            if (Input.GetKeyDown(KeyCode.Delete) || Input.GetMouseButtonDown(2))
            {
                DeleteAimedObject();
            }
        }

        public static GameObject GetAimedPlacedObject()
        {
            if (EditorViewportCamera.ViewportCamera == null) return null;

            Ray ray = Input.GetMouseButton(1)
                ? EditorViewportCamera.ViewportCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : EditorViewportCamera.ViewportCamera.ScreenPointToRay(Input.mousePosition);

            int mask = ~LayerMask.GetMask("Ignore Raycast");
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 1000f, mask))
            {
                GameObject hitObj = hit.collider.gameObject;
                foreach (var obj in PlacedObjects)
                {
                    if (obj == null) continue;
                    if (hitObj == obj || hitObj.transform.IsChildOf(obj.transform))
                    {
                        return obj;
                    }
                }
            }
            return null;
        }

        private static void DeleteAimedObject()
        {
            GameObject target = GetAimedPlacedObject();
            if (target != null)
            {
                PlacedObjects.Remove(target);
                if (PlacedLights.ContainsKey(target)) PlacedLights.Remove(target);
                if (SelectedLightObject == target) SelectedLightObject = null;
                target.SetActive(false);

                float param = 0f;
                if (JumperForces.ContainsKey(target)) param = JumperForces[target];
                else if (TurbineSpeeds.ContainsKey(target)) param = TurbineSpeeds[target];
                else if (TurretFireDelays.ContainsKey(target)) param = TurretFireDelays[target];
                else if (PlacedLights.ContainsKey(target)) param = PlacedLights[target].Range;

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
                MelonLogger.Msg($">> [Delete] Removed '{target.name}'. (Press Z to Undo)");
            }
        }

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
                    PlacedObjects.Remove(record.TargetObject);
                    if (SelectedLightObject == record.TargetObject) SelectedLightObject = null;
                }
                RedoHistory.Push(record);
                ShowNotification($"Undid placement of {record.AssetName}");
            }
            else if (record.ActionType == HistoryActionType.Deletion)
            {
                if (record.TargetObject != null)
                {
                    record.TargetObject.SetActive(true);
                    if (!PlacedObjects.Contains(record.TargetObject))
                        PlacedObjects.Add(record.TargetObject);
                }
                else
                {
                    GameObject recreated = SpawnAssetByName(record.AssetName, record.Position, record.Scale, record.Rotation);
                    if (recreated != null)
                    {
                        record.TargetObject = recreated;
                        if (record.CustomParameter > 0)
                        {
                            if (record.AssetName.ToLower().Contains("jumper")) ApplyJumperForce(recreated, record.CustomParameter);
                            if (record.AssetName.ToLower().Contains("helix")) ApplyTurbineSpeed(recreated, record.CustomParameter);
                            if (record.AssetName.ToLower().Contains("turret")) ApplyTurretSettings(recreated, record.CustomParameter, 1500f);
                            if (record.AssetName.ToLower().Contains("spotlight") && PlacedLights.ContainsKey(recreated))
                            {
                                PlacedLights[recreated].Range = record.CustomParameter;
                                ApplyLightConfig(recreated, PlacedLights[recreated]);
                            }
                        }
                        PlacedObjects.Add(recreated);
                    }
                }
                RedoHistory.Push(record);
                ShowNotification($"Restored deleted {record.AssetName}");
            }
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
                    if (!PlacedObjects.Contains(record.TargetObject))
                        PlacedObjects.Add(record.TargetObject);
                }
                UndoHistory.Push(record);
                ShowNotification($"Redid placement of {record.AssetName}");
            }
            else if (record.ActionType == HistoryActionType.Deletion)
            {
                if (record.TargetObject != null)
                {
                    record.TargetObject.SetActive(false);
                    PlacedObjects.Remove(record.TargetObject);
                    if (SelectedLightObject == record.TargetObject) SelectedLightObject = null;
                }
                UndoHistory.Push(record);
                ShowNotification($"Re-deleted {record.AssetName}");
            }
        }

        public static Quaternion GetCurrentCombinedRotation(CatalogAsset asset)
        {
            Quaternion snappedRot = Quaternion.Euler(TargetPitch, TargetYaw, TargetRoll);
            if (asset == null) return snappedRot;
            return snappedRot * asset.BaseRotation;
        }

        public static Quaternion GetAutoFlatRotation(GameObject template)
        {
            if (template == null) return Quaternion.Euler(0f, 0f, 90f);

            MeshFilter mf = template.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Vector3 size = mf.sharedMesh.bounds.size;
                if (size.x <= size.y && size.z >= size.x) return Quaternion.Euler(0f, 0f, 90f);
                if (size.z <= size.x && size.z <= size.y) return Quaternion.Euler(90f, 0f, 0f);
            }
            return Quaternion.Euler(0f, 0f, 90f);
        }

        private static void CycleGridSnap()
        {
            if (CurrentGridSnap == 1.0f) CurrentGridSnap = 2.0f;
            else if (CurrentGridSnap == 2.0f) CurrentGridSnap = 4.0f;
            else if (CurrentGridSnap == 4.0f) CurrentGridSnap = 0.5f;
            else if (CurrentGridSnap == 0.5f) CurrentGridSnap = 0.0f;
            else CurrentGridSnap = 1.0f;

            string status = (CurrentGridSnap > 0.01f) ? $"{CurrentGridSnap}m" : "OFF";
            ShowNotification($"Grid Snap: {status}");
            MelonLogger.Msg($">> [Grid Snap] Switched to: {status}");
        }

        public static void ClearAllPlacedObjects()
        {
            foreach (var obj in PlacedObjects)
            {
                if (obj != null) GameObject.Destroy(obj);
            }
            PlacedObjects.Clear();
            PlacedLights.Clear();
            SelectedLightObject = null;
            LastPlacedObject = null;
            JumperForces.Clear();
            TurbineSpeeds.Clear();
            TurretFireDelays.Clear();
            UndoHistory.Clear();
            RedoHistory.Clear();
        }

        public static void StripParticlesAndLights(GameObject root)
        {
            if (root == null) return;

            Jumper jc = root.GetComponentInChildren<Jumper>();
            if (jc != null && jc.Fx != null)
            {
                GameObject.DestroyImmediate(jc.Fx);
            }

            Component[] components = root.GetComponentsInChildren<Component>(true);
            foreach (var comp in components)
            {
                if (comp == null || comp is Transform) continue;

                string typeName = comp.GetType().Name.ToLower();
                if (typeName.Contains("particle") || typeName.Contains("emitter") || typeName.Contains("trail") || typeName.Contains("flare") || typeName.Contains("halo"))
                {
                    GameObject.DestroyImmediate(comp);
                }
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var tr in transforms)
            {
                if (tr == null || tr.gameObject == root) continue;
                string objName = tr.gameObject.name.ToLower();
                if (objName.Contains("particle") || objName.Contains("flame") || objName.Contains("fx") || objName.Contains("fire") || objName.Contains("glow") || objName.Contains("flare") || objName.Contains("beam") || objName.Contains("laser") || objName.Contains("anneau"))
                {
                    GameObject.DestroyImmediate(tr.gameObject);
                }
            }
        }

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

            MelonLogger.Msg($">> [Jumper Force] Set to {force:F1} on '{jumperObj.name}'");
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
            }

            MelonLogger.Msg($">> [Turbine Speed] Set to {speed:F1} on '{turbineObj.name}'");
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

                // Expand detection trigger so the turret spots the player across custom distances
                if (ts._triggerAnimation != null)
                {
                    ts._triggerAnimation.enabled = true;
                    ts._triggerAnimation.isTrigger = true;
                    if (ts._triggerAnimation.radius < 60f)
                    {
                        ts._triggerAnimation.radius = 60f;
                    }
                }
            }

            // DO NOT destroy any colliders! 
            // The turret's body and head colliders are what the Switch Gun hits to stun/deactivate it.
            // Ensure all colliders and interactive components are enabled.
            Collider[] colliders = turretObj.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    colliders[i].enabled = true;
                }
            }

            foreach (var mb in turretObj.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string typeName = mb.GetType().Name.ToLower();
                if (typeName.Contains("switch") || typeName.Contains("target") ||
                    typeName.Contains("activat") || typeName.Contains("enemy") ||
                    typeName.Contains("turret"))
                {
                    mb.enabled = true;
                }
            }

            MelonLogger.Msg($">> [Turret] Configured with hitboxes intact! Fire Delay: {fireDelay:F2}s, Power: {firePower:F0}");
        }

        public static void ApplyLightConfig(GameObject lightObj, LightConfig cfg)
        {
            if (lightObj == null || cfg == null) return;

            PlacedLights[lightObj] = cfg;

            Light l = lightObj.GetComponentInChildren<Light>();
            if (l != null)
            {
                l.enabled = false;
                l.range = Mathf.Max(1f, cfg.Range);
                l.spotAngle = Mathf.Clamp(cfg.SpotAngle, 1f, 175f);
                l.color = cfg.Color;
                l.intensity = cfg.Intensity * 1200f;
                l.enabled = true;
            }

            try
            {
                Component[] comps = lightObj.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] != null && comps[i].GetIl2CppType().Name.Contains("HDAdditionalLightData"))
                    {
                        var t = comps[i].GetIl2CppType();

                        var rangeProp = t.GetProperty("range");
                        if (rangeProp != null) rangeProp.SetValue(comps[i], cfg.Range);

                        var intProp = t.GetProperty("intensity");
                        if (intProp != null) intProp.SetValue(comps[i], cfg.Intensity * 1200f);

                        var volDimProp = t.GetProperty("volumetricDimmer");
                        if (volDimProp != null) volDimProp.SetValue(comps[i], cfg.VolumetricDimmer);

                        var useVolProp = t.GetProperty("useVolumetric");
                        if (useVolProp != null) useVolProp.SetValue(comps[i], cfg.VolumetricDimmer > 0.01f);
                    }
                }
            }
            catch { }

            try
            {
                MeshRenderer mr = lightObj.GetComponentInChildren<MeshRenderer>();
                if (mr != null && mr.material != null)
                {
                    mr.material.color = cfg.Color;
                    if (mr.material.HasProperty("_EmissionColor"))
                    {
                        mr.material.SetColor("_EmissionColor", cfg.Color * Mathf.Max(1.5f, cfg.Intensity));
                        mr.material.EnableKeyword("_EMISSION");
                    }
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
                    l.range = cfg.Range + 1.0f;
                    l.spotAngle = cfg.SpotAngle + 1.0f;
                    l.intensity = (cfg.Intensity + 0.5f) * 1200f;
                    l.enabled = false;
                }

                ApplyLightConfig(obj, cfg);
            }

            MelonLogger.Msg($">> [Lights] Automatically synced {PlacedLights.Count} spotlights!");
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
                if (lights[i] != null)
                {
                    lights[i].color = tintColor;
                }
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

        public static void DrawEditorGUI()
        {
            Camera cam = EditorViewportCamera.ViewportCamera;
            if (cam == null) return;

            Color originalColor = GUI.color;

            // Render world-space contextual object labels
            foreach (var obj in PlacedObjects)
            {
                if (obj == null || !obj.activeSelf) continue;

                if (JumperForces.ContainsKey(obj) || obj.name.ToLower().Contains("jumper"))
                {
                    float force = JumperForces.ContainsKey(obj) ? JumperForces[obj] : ActiveJumperForce;
                    Vector3 worldPos = obj.transform.position + Vector3.up * 1.2f;
                    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

                    if (screenPos.z > 0.5f)
                    {
                        float y = Screen.height - screenPos.y;
                        GUI.color = Color.cyan;
                        GUI.Box(new Rect(screenPos.x - 75f, y - 14f, 150f, 26f), $"[Force: {force:F1}]");
                    }
                }
                else if (TurbineSpeeds.ContainsKey(obj) || obj.name.ToLower().Contains("helix"))
                {
                    float speed = TurbineSpeeds.ContainsKey(obj) ? TurbineSpeeds[obj] : ActiveTurbineSpeed;
                    Vector3 worldPos = obj.transform.position + Vector3.up * 2.0f;
                    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

                    if (screenPos.z > 0.5f)
                    {
                        float y = Screen.height - screenPos.y;
                        GUI.color = Color.green;
                        GUI.Box(new Rect(screenPos.x - 75f, y - 14f, 150f, 26f), $"[Speed: {speed:F1}]");
                    }
                }
                else if (TurretFireDelays.ContainsKey(obj) || obj.name.ToLower().Contains("turret"))
                {
                    float delay = TurretFireDelays.ContainsKey(obj) ? TurretFireDelays[obj] : ActiveTurretFireDelay;
                    Vector3 worldPos = obj.transform.position + Vector3.up * 1.6f;
                    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

                    if (screenPos.z > 0.5f)
                    {
                        float y = Screen.height - screenPos.y;
                        GUI.color = Color.red;
                        GUI.Box(new Rect(screenPos.x - 75f, y - 14f, 150f, 26f), $"[Fire Delay: {delay:F2}s]");
                    }
                }
                else if (obj.name.ToLower().Contains("spawn"))
                {
                    Vector3 worldPos = obj.transform.position + Vector3.up * 2.2f;
                    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                    if (screenPos.z > 0.5f)
                    {
                        float y = Screen.height - screenPos.y;
                        GUI.color = new Color(1.0f, 0.45f, 0.05f);
                        GUI.Box(new Rect(screenPos.x - 85f, y - 14f, 170f, 26f), "[Entry / Spawn Point]");
                    }
                }
                else if (obj.name.ToLower().Contains("goal"))
                {
                    Vector3 worldPos = obj.transform.position + Vector3.up * 2.2f;
                    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                    if (screenPos.z > 0.5f)
                    {
                        float y = Screen.height - screenPos.y;
                        GUI.color = new Color(0.1f, 0.65f, 1.0f);
                        GUI.Box(new Rect(screenPos.x - 85f, y - 14f, 170f, 26f), "[Goal / Finish Line]");
                    }
                }
                else if (obj.name.ToLower().Contains("spotlight"))
                {
                    Vector3 worldPos = obj.transform.position + Vector3.up * 1.2f;
                    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                    if (screenPos.z > 0.5f)
                    {
                        float y = Screen.height - screenPos.y;
                        bool isSel = (obj == SelectedLightObject);
                        GUI.color = isSel ? Color.green : Color.yellow;
                        string badge = isSel ? "★ [Selected Spotlight] ★" : "[Tech Spotlight]";
                        GUI.Box(new Rect(screenPos.x - 90f, y - 14f, 180f, 26f), badge);
                    }
                }
                else if (obj.name.ToLower().Contains("rotating"))
                {
                    Vector3 worldPos = obj.transform.position + Vector3.up * 1.2f;
                    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                    if (screenPos.z > 0.5f)
                    {
                        float y = Screen.height - screenPos.y;
                        float spd = LaserRotationSpeeds.ContainsKey(obj) ? LaserRotationSpeeds[obj] : ActiveLaserRotationSpeed;
                        GUI.color = Color.red;
                        GUI.Box(new Rect(screenPos.x - 85f, y - 14f, 170f, 26f), $"[Laser: {spd:F0}°/s]");
                    }
                }
                else if (obj.name.ToLower().Contains("laser"))
                {
                    Vector3 worldPos = obj.transform.position + Vector3.up * 1.5f;
                    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                    if (screenPos.z > 0.5f)
                    {
                        float y = Screen.height - screenPos.y;
                        GUI.color = Color.red;
                        string label = obj.name.ToLower().Contains("rotating") ? "[Rotating Laser]" : (obj.name.ToLower().Contains("long") ? "[Long Laser]" : "[Laser Barrier]");
                        GUI.Box(new Rect(screenPos.x - 85f, y - 14f, 170f, 26f), label);
                    }
                }
                else
                {
                    CheckPointScript cp = obj.GetComponentInChildren<CheckPointScript>();
                    if (cp != null)
                    {
                        Vector3 worldPos = obj.transform.position + Vector3.up * 2.2f;
                        Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

                        if (screenPos.z > 0.5f)
                        {
                            bool isActive = (CheckPointScript.LastCheckPoint == cp);
                            float y = Screen.height - screenPos.y;
                            GUI.color = isActive ? Color.green : new Color(0.4f, 0.8f, 1f);
                            string cpText = isActive ? "[Active Checkpoint]" : "[Checkpoint]";
                            GUI.Box(new Rect(screenPos.x - 75f, y - 14f, 150f, 26f), cpText);
                        }
                    }
                }
            }

            // Render holographic preview parameters
            if (IsBlockSelected && CurrentAsset != null && PlacementHologramController.GhostInstance != null)
            {
                if (CurrentAsset.IsJumper)
                {
                    Vector3 worldPos = PlacementHologramController.GhostInstance.transform.position + Vector3.up * 1.4f;
                    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                    if (screenPos.z > 0.5f)
                    {
                        float y = Screen.height - screenPos.y;
                        GUI.color = Color.yellow;
                        GUI.Box(new Rect(screenPos.x - 100f, y - 14f, 200f, 26f), $"[Set Force: {ActiveJumperForce:F1}] (Shift+Scroll)");
                    }
                }
                else if (CurrentAsset.IsHelix)
                {
                    Vector3 worldPos = PlacementHologramController.GhostInstance.transform.position + Vector3.up * 2.2f;
                    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                    if (screenPos.z > 0.5f)
                    {
                        float y = Screen.height - screenPos.y;
                        GUI.color = Color.green;
                        GUI.Box(new Rect(screenPos.x - 105f, y - 14f, 210f, 26f), $"[Set Speed: {ActiveTurbineSpeed:F1}] (Shift+Scroll)");
                    }
                }
                else if (CurrentAsset.IsTurret)
                {
                    Vector3 worldPos = PlacementHologramController.GhostInstance.transform.position + Vector3.up * 1.8f;
                    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                    if (screenPos.z > 0.5f)
                    {
                        float y = Screen.height - screenPos.y;
                        GUI.color = Color.red;
                        GUI.Box(new Rect(screenPos.x - 105f, y - 14f, 210f, 26f), $"[Set Fire Delay: {ActiveTurretFireDelay:F2}s] (Shift+Scroll)");
                    }
                }
            }

            // Category Selection Header Bar
            float barW = 560f;
            float barH = 34f;
            float barX = (Screen.width - barW) * 0.5f;
            float barY = 14f;

            GUI.color = new Color(0.04f, 0.08f, 0.12f, 0.9f);
            GUI.Box(new Rect(barX, barY, barW, barH), "");

            string[] catLabels = new string[] { "1: GAMEPLAY", "2: ARCHITECTURE", "3: PROPS" };
            float tabBtnW = (barW - 16f) / 3f;

            for (int i = 0; i < 3; i++)
            {
                AssetCategory c = (AssetCategory)i;
                bool isCurrent = (CurrentTab == c);
                GUI.color = isCurrent ? new Color(0.2f, 0.85f, 1f, 1f) : new Color(0.35f, 0.42f, 0.5f, 0.8f);

                int count = AllAssets.FindAll(a => a.Category == c).Count;
                if (GUI.Button(new Rect(barX + 8f + (i * tabBtnW), barY + 4f, tabBtnW - 4f, 26f), $"{catLabels[i]} ({count})"))
                {
                    SetCategory(c);
                }
            }

            // Viewport Status and Shortcut Footer
            string gridName = (CurrentGridSnap > 0.01f) ? $"{CurrentGridSnap}m" : "OFF";
            string status = IsBlockSelected
                ? $"PLACING: '{CurrentAsset?.DisplayName}' | Rot: [P:{TargetPitch:0}° Y:{TargetYaw:0}° R:{TargetRoll:0}°] | Snap: {gridName} (G) | T: 90° Snap | R: Reset"
                : $"MAP: '{MapBrowserService.SelectedMapName}' | Tab / 1-3: Switch Tab | F3: Spotlights | F5: Save | F6: Load | Z: Undo | Y: Redo";

            GUI.color = Color.white;
            GUI.Box(new Rect(Screen.width * 0.5f - 430f, Screen.height - 40f, 860f, 28f), status);

            if (_notificationTimer > 0f)
            {
                GUI.color = new Color(0.2f, 1f, 0.4f, Mathf.Clamp01(_notificationTimer));
                GUI.Box(new Rect(Screen.width * 0.5f - 220f, Screen.height - 85f, 440f, 34f), _notificationMessage);
            }

            if (ShowParamsWindow)
            {
                DrawParametersWindow();
            }

            GUI.color = originalColor;
        }

        private static void DrawParametersWindow()
        {
            float winW = 320f;
            float winH = 390f;
            float winX = Screen.width - winW - 20f;
            float winY = 20f;
            Rect winRect = new Rect(winX, winY, winW, winH);

            Color orig = GUI.color;

            GUI.color = new Color(0.04f, 0.07f, 0.12f, 0.94f);
            GUI.Box(winRect, "");
            GUI.color = new Color(0.12f, 0.65f, 0.95f, 0.85f);
            GUI.Box(new Rect(winX + 2, winY + 2, winW - 4, winH - 4), "");

            GUI.color = new Color(0.08f, 0.14f, 0.22f, 1f);
            GUI.Box(new Rect(winX + 8, winY + 8, winW - 16, 32), "");
            GUI.color = Color.yellow;
            GUI.Label(new Rect(winX + 20, winY + 14, 250, 25), "SPOTLIGHT INSPECTOR (F3)");

            float curY = winY + 48f;

            if (!IsMouseOverUI())
            {
                GameObject aimed = GetAimedPlacedObject();
                if (aimed != null && (PlacedLights.ContainsKey(aimed) || aimed.name.ToLower().Contains("spotlight")))
                {
                    SelectedLightObject = aimed;
                }
            }

            if (SelectedLightObject != null && !SelectedLightObject.activeSelf)
            {
                SelectedLightObject = null;
            }

            List<GameObject> allLights = new List<GameObject>();
            foreach (var kvp in PlacedLights)
            {
                if (kvp.Key != null && kvp.Key.activeSelf) allLights.Add(kvp.Key);
            }

            if (allLights.Count > 0)
            {
                int curIdx = SelectedLightObject != null ? allLights.IndexOf(SelectedLightObject) : -1;
                string lightLabel = curIdx >= 0 ? $"Light {curIdx + 1} of {allLights.Count}" : "None Selected";

                GUI.color = new Color(0.2f, 0.55f, 0.85f);
                if (GUI.Button(new Rect(winX + 15, curY, 40, 24), "<"))
                {
                    curIdx = (curIdx - 1 + allLights.Count) % allLights.Count;
                    SelectedLightObject = allLights[curIdx];
                }

                GUI.color = Color.white;
                GUI.Label(new Rect(winX + 62, curY + 3, 120, 20), lightLabel);

                GUI.color = new Color(0.2f, 0.55f, 0.85f);
                if (GUI.Button(new Rect(winX + 185, curY, 40, 24), ">"))
                {
                    curIdx = (curIdx + 1) % allLights.Count;
                    SelectedLightObject = allLights[curIdx];
                }

                GUI.color = new Color(0.8f, 0.3f, 0.3f);
                if (GUI.Button(new Rect(winX + 235, curY, 70, 24), "Deselect"))
                {
                    SelectedLightObject = null;
                }
                curY += 32f;
            }

            if (SelectedLightObject != null && PlacedLights.ContainsKey(SelectedLightObject))
            {
                LightConfig cfg = PlacedLights[SelectedLightObject];
                bool changed = false;

                GUI.color = Color.white;
                GUI.Label(new Rect(winX + 15, curY, 200, 18), $"Light Range: {cfg.Range:F0}m");
                curY += 18f;
                float newRange = GUI.HorizontalSlider(new Rect(winX + 15, curY, winW - 30, 16), cfg.Range, 1f, 1000f);
                if (Mathf.Abs(newRange - cfg.Range) > 0.5f)
                {
                    cfg.Range = newRange;
                    changed = true;
                }
                curY += 22f;

                GUI.Label(new Rect(winX + 15, curY, 200, 18), $"Beam Angle: {cfg.SpotAngle:F0}°");
                curY += 18f;
                float newAngle = GUI.HorizontalSlider(new Rect(winX + 15, curY, winW - 30, 16), cfg.SpotAngle, 1f, 175f);
                if (Mathf.Abs(newAngle - cfg.SpotAngle) > 0.5f)
                {
                    cfg.SpotAngle = newAngle;
                    changed = true;
                }
                curY += 22f;

                GUI.Label(new Rect(winX + 15, curY, 200, 18), $"Intensity: {cfg.Intensity:F1}x");
                curY += 18f;
                float newInt = GUI.HorizontalSlider(new Rect(winX + 15, curY, winW - 30, 16), cfg.Intensity, 0.1f, 50f);
                if (Mathf.Abs(newInt - cfg.Intensity) > 0.05f)
                {
                    cfg.Intensity = newInt;
                    changed = true;
                }
                curY += 22f;

                GUI.color = new Color(0.4f, 0.9f, 1f);
                GUI.Label(new Rect(winX + 15, curY, 200, 18), $"Volumetric Beam: {cfg.VolumetricDimmer:F1}x");
                curY += 18f;
                float newVol = GUI.HorizontalSlider(new Rect(winX + 15, curY, winW - 30, 16), cfg.VolumetricDimmer, 0.0f, 10.0f);
                if (Mathf.Abs(newVol - cfg.VolumetricDimmer) > 0.05f)
                {
                    cfg.VolumetricDimmer = newVol;
                    changed = true;
                }
                curY += 24f;

                GUI.color = Color.white;
                GUI.Label(new Rect(winX + 15, curY, 150, 18), "Beam Color:");
                curY += 20f;
                float cW = (winW - 50) / 5f;

                GUI.color = Color.cyan;
                if (GUI.Button(new Rect(winX + 15, curY, cW, 22), "Cyan")) { cfg.Color = Color.cyan; changed = true; }
                GUI.color = new Color(1f, 0.5f, 0.1f);
                if (GUI.Button(new Rect(winX + 15 + cW + 4, curY, cW, 22), "Amber")) { cfg.Color = new Color(1f, 0.5f, 0.1f); changed = true; }
                GUI.color = new Color(0.2f, 1f, 0.35f);
                if (GUI.Button(new Rect(winX + 15 + (cW + 4) * 2, curY, cW, 22), "Green")) { cfg.Color = Color.green; changed = true; }
                GUI.color = new Color(1f, 0.25f, 0.25f);
                if (GUI.Button(new Rect(winX + 15 + (cW + 4) * 3, curY, cW, 22), "Red")) { cfg.Color = Color.red; changed = true; }
                GUI.color = Color.white;
                if (GUI.Button(new Rect(winX + 15 + (cW + 4) * 4, curY, cW, 22), "White")) { cfg.Color = Color.white; changed = true; }

                if (changed)
                {
                    ApplyLightConfig(SelectedLightObject, cfg);
                }
            }
            else
            {
                GUI.color = Color.gray;
                GUI.Label(new Rect(winX + 15, curY + 25, 290, 60), allLights.Count > 0
                    ? "Click '<' or '>' above to select a light,\nor aim at any Tech Spotlight in the world."
                    : "No Tech Spotlights placed yet.\nSelect 'Tech Spotlight' from Gameplay to place one.");
            }

            GUI.color = orig;
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
                if (GUI.Button(new Rect(x + 30, y + 150, 125, 42), "Restart"))
                {
                    RestartRun();
                }

                GUI.color = new Color(0.2f, 0.7f, 1f, 1f);
                if (GUI.Button(new Rect(x + 165, y + 150, 130, 42), "Edit (F1)"))
                {
                    ToggleEditMode();
                }

                GUI.color = new Color(0.85f, 0.3f, 0.3f, 1f);
                if (GUI.Button(new Rect(x + 305, y + 150, 125, 42), "Main Menu"))
                {
                    SceneLoader.LoadLevel("MainMenu", false, true);
                }
            }

            GUI.color = origColor;
        }

        public static void InitializeCustomLevel()
        {
            GameObject player = FindPlayerEntity();
            Vector3 startPos = LevelSpawnPosition;
            Vector3 forwardDir = new Vector3(0f, 0f, 1f);

            if (player != null)
            {
                startPos = player.transform.position;
                LevelSpawnPosition = startPos;
                forwardDir = player.transform.forward;
                forwardDir.y = 0;
                forwardDir.Normalize();
            }

            SceneHarvestingService.HarvestAllSceneModels();
            SceneHarvestingService.HideVanillaLevelGeometry();

            if (!string.IsNullOrEmpty(MapBrowserService.SelectedMapPath) && File.Exists(MapBrowserService.SelectedMapPath))
            {
                MelonLogger.Msg($">> Auto-loading selected map: '{MapBrowserService.SelectedMapName}'...");
                LevelPersistenceService.LoadLevelByFullPath(MapBrowserService.SelectedMapPath);
            }

            foreach (var obj in PlacedObjects)
            {
                if (obj != null && obj.name.ToLower().Contains("spawn"))
                {
                    LevelSpawnPosition = obj.transform.position + Vector3.up * 0.2f;
                    startPos = LevelSpawnPosition;
                    MelonLogger.Msg($">> Set player start position to custom Entry Checkpoint at {startPos}!");
                    break;
                }
            }

            bool hasStartingPlatform = false;
            foreach (var obj in PlacedObjects)
            {
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
                    PlacedObjects.Add(startPlatform);
                    MelonLogger.Msg($">> [SafeSpawn] Spawned solid floor platform directly under player at {p0Pos}!");
                }
            }

            if (player != null)
            {
                CharacterController cc = player.GetComponentInChildren<CharacterController>();
                if (cc != null) cc.enabled = false;
                player.transform.position = startPos + Vector3.up * 0.2f;
                if (cc != null) cc.enabled = true;
            }

            LevelTimer = 0f;
            IsLevelCompleted = false;

            _lightRefreshTimer = 0.35f;

            UnfreezePlayerControls();
            IsLevelInitialized = true;
            MelonLogger.Msg(">> Custom Level Initialized & Ready!");
        }

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
                else if (clean.Contains("spotlight") || clean.Contains("light")) found = AllAssets.Find(a => a.IsSpotlight);
                else if (clean.Contains("rotating") && clean.Contains("laser")) found = AllAssets.Find(a => a.IsRotatingLaser);
                else if (clean.Contains("long") && clean.Contains("laser")) found = AllAssets.Find(a => a.DisplayName.ToLower().Contains("long laser"));
                else if (clean.Contains("laser") || clean.Contains("barrier")) found = AllAssets.Find(a => a.IsLaser && !a.IsRotatingLaser);
                else if (clean.Contains("platform") || clean.Contains("floor") || clean.Contains("plateforme"))
                    found = AllAssets.Find(a => a.DisplayName.ToLower().Contains("platform") || a.DisplayName.ToLower().Contains("floor"));
                else if (clean.Contains("jumper") || clean.Contains("launch")) found = AllAssets.Find(a => a.IsJumper);
                else if (clean.Contains("checkpoint")) found = AllAssets.Find(a => a.IsCheckPoint && !a.IsSpawnGate && !a.IsGoalGate);
                else if (clean.Contains("turret") || clean.Contains("defense")) found = AllAssets.Find(a => a.IsTurret);
                else if (clean.Contains("helix") || clean.Contains("turbine") || clean.Contains("fan")) found = AllAssets.Find(a => a.IsHelix);
                else if (clean.Contains("crate")) found = AllAssets.Find(a => a.DisplayName.ToLower().Contains("crate"));
                else if (clean.Contains("pillar") || clean.Contains("monolith")) found = AllAssets.Find(a => a.DisplayName.ToLower().Contains("pillar") || a.DisplayName.ToLower().Contains("monolith"));
                else if (clean.Contains("gate") || clean.Contains("bar")) found = AllAssets.Find(a => a.FilterMesh != null && a.FilterMesh.name.ToLower().Contains("gate"));
                else if (clean.Contains("wall")) found = AllAssets.Find(a => a.DisplayName.ToLower().Contains("wall") || (a.FilterMesh != null && a.FilterMesh.name.ToLower().Contains("wall")));
                else if (clean.Contains("cube")) found = AllAssets.Find(a => a.DisplayName.ToLower().Contains("cube") || (a.FilterMesh != null && a.FilterMesh.name.ToLower().Contains("cube")));
            }

            if (found != null)
            {
                return SpawnCatalogObject(found, position, scale, customRotation);
            }

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

            if (asset.IsSpawnGate)
            {
                obj.name = "Custom_Spawn_Gate";
                ApplyGateVisualTint(obj, new Color(1.0f, 0.45f, 0.05f));
            }
            else if (asset.IsGoalGate)
            {
                obj.name = "Custom_Goal_Gate";
                ApplyGateVisualTint(obj, new Color(0.1f, 0.65f, 1.0f));

                Collider col = obj.GetComponentInChildren<Collider>();
                if (col != null) col.isTrigger = true;
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
            else if (asset.IsRotatingLaser)
            {
                obj.name = "Custom_Rotating_Laser_Barrier";
            }
            else if (asset.IsLaser)
            {
                obj.name = "Custom_Laser_Barrier";
            }
            else if (asset.IsCheckPoint)
            {
                CheckPointScript cp = obj.GetComponentInChildren<CheckPointScript>();
                if (cp != null)
                {
                    cp._id = PlacedObjects.Count + 100;
                    if (cp._spawnPoint == null)
                    {
                        GameObject spObj = new GameObject("SpawnPoint");
                        spObj.transform.SetParent(obj.transform, false);
                        spObj.transform.localPosition = new Vector3(0f, 0.1f, 0f);
                        cp._spawnPoint = spObj.transform;
                    }

                    Collider col = obj.GetComponentInChildren<Collider>();
                    if (col != null) col.isTrigger = true;
                }
            }

            if (asset.IsHelix)
            {
                ApplyTurbineSpeed(obj, ActiveTurbineSpeed);
            }

            if (asset.IsTurret)
            {
                ApplyTurretSettings(obj, ActiveTurretFireDelay, 1500f);
            }

            if (asset.IsJumper)
            {
                ApplyJumperForce(obj, ActiveJumperForce);
            }

            if (!asset.IsTurret && !asset.IsCheckPoint && !asset.IsHelix && !asset.IsSpawnGate && !asset.IsGoalGate && !asset.IsSpotlight && !asset.IsLaser)
            {
                if (obj.GetComponentInChildren<Collider>() == null)
                {
                    MeshFilter mf = obj.GetComponentInChildren<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        MeshCollider mc = obj.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                    }
                }
            }

            return obj;
        }

        public static GameObject FindPlayerEntity()
        {
            GameObject tagged = GameObject.FindWithTag("Player");
            if (tagged != null) return tagged;

            CharacterController cc = GameObject.FindObjectOfType<CharacterController>();
            if (cc != null) return cc.transform.root.gameObject;

            if (Camera.main != null) return Camera.main.transform.root.gameObject;

            return null;
        }
    }

    // =========================================================================================================
    // VIEWPORT FREE CAMERA CONTROLLER
    // =========================================================================================================
    public static class EditorViewportCamera
    {
        private static GameObject _camInstance = null;
        public static Camera ViewportCamera = null;

        private static float _yaw = 0f;
        private static float _pitch = 0f;
        private static float _baseSpeed = 24f;

        public static void InitializeCamera(Camera sourceCam)
        {
            if (_camInstance != null) return;

            _camInstance = new GameObject("Viewport_Editor_Camera");
            ViewportCamera = _camInstance.AddComponent<Camera>();

            if (sourceCam != null)
            {
                ViewportCamera.CopyFrom(sourceCam);
                _camInstance.transform.position = sourceCam.transform.position;
                _camInstance.transform.rotation = sourceCam.transform.rotation;
                _yaw = _camInstance.transform.eulerAngles.y;
                _pitch = _camInstance.transform.eulerAngles.x;

                sourceCam.enabled = false;
            }
        }

        public static void UpdateCamera()
        {
            if (_camInstance == null) return;

            if (Input.GetMouseButton(1))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;

                _yaw += Input.GetAxis("Mouse X") * 2.5f;
                _pitch -= Input.GetAxis("Mouse Y") * 2.5f;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);

                _camInstance.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            float speed = _baseSpeed;
            if (Input.GetKey(KeyCode.LeftShift)) speed *= 3.5f;
            else if (Input.GetKey(KeyCode.LeftControl)) speed *= 0.25f;

            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += _camInstance.transform.forward;
            if (Input.GetKey(KeyCode.S)) move -= _camInstance.transform.forward;
            if (Input.GetKey(KeyCode.D)) move += _camInstance.transform.right;
            if (Input.GetKey(KeyCode.A)) move -= _camInstance.transform.right;
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) move += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) move -= Vector3.up;

            _camInstance.transform.position += move * speed * Time.deltaTime;
        }

        public static void DestroyCamera()
        {
            if (_camInstance != null)
            {
                GameObject.Destroy(_camInstance);
                _camInstance = null;
                ViewportCamera = null;
            }
        }
    }

    // =========================================================================================================
    // PLACEMENT HOLOGRAM CONTROLLER
    // =========================================================================================================
    public static class PlacementHologramController
    {
        private static GameObject _ghostInstance = null;
        public static GameObject GhostInstance => _ghostInstance;

        private static BoxCollider _ghostBoxCollider = null;
        private static Vector3 _targetPosition = Vector3.zero;

        public static void SpawnHologram(CatalogAsset asset)
        {
            DestroyPreview();
            if (asset == null || asset.SourceTemplate == null) return;

            _ghostInstance = GameObject.Instantiate(asset.SourceTemplate);
            _ghostInstance.name = "Holographic_Ghost_Preview";

            _ghostInstance.layer = 2;
            foreach (var tr in _ghostInstance.GetComponentsInChildren<Transform>(true))
            {
                tr.gameObject.layer = 2;
            }

            foreach (var col in _ghostInstance.GetComponentsInChildren<Collider>(true))
            {
                col.enabled = false;
            }

            foreach (var mb in _ghostInstance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                GameObject.DestroyImmediate(mb);
            }

            EditorSessionManager.StripParticlesAndLights(_ghostInstance);

            if (asset.IsSpawnGate)
            {
                EditorSessionManager.ApplyGateVisualTint(_ghostInstance, new Color(1.0f, 0.45f, 0.05f));
            }
            else if (asset.IsGoalGate)
            {
                EditorSessionManager.ApplyGateVisualTint(_ghostInstance, new Color(0.1f, 0.65f, 1.0f));
            }

            Bounds b = CalculateLocalBounds(_ghostInstance);

            _ghostBoxCollider = _ghostInstance.AddComponent<BoxCollider>();
            _ghostBoxCollider.isTrigger = true;
            _ghostBoxCollider.center = b.center;
            _ghostBoxCollider.size = b.size;

            _ghostInstance.transform.localScale = Vector3.one * EditorSessionManager.ActivePlacementScale;

            ApplyRotationToPreview();
            _ghostInstance.SetActive(true);
        }

        public static Bounds CalculateLocalBounds(GameObject go)
        {
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;

            MeshFilter[] mfs = go.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in mfs)
            {
                if (mf.sharedMesh != null)
                {
                    Bounds meshB = mf.sharedMesh.bounds;
                    Vector3 localPos = go.transform.InverseTransformPoint(mf.transform.position);
                    Bounds transformedB = new Bounds(localPos + meshB.center, meshB.size);

                    if (!hasBounds)
                    {
                        b = transformedB;
                        hasBounds = true;
                    }
                    else
                    {
                        b.Encapsulate(transformedB);
                    }
                }
            }

            if (!hasBounds) b = new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f));

            Vector3 size = b.size;
            size.x = Mathf.Max(size.x, 0.2f);
            size.y = Mathf.Max(size.y, 0.2f);
            size.z = Mathf.Max(size.z, 0.2f);
            b.size = size;

            return b;
        }

        public static void ApplyScaleToPreview()
        {
            if (_ghostInstance != null)
            {
                _ghostInstance.transform.localScale = Vector3.one * EditorSessionManager.ActivePlacementScale;
            }
        }

        public static void ApplyRotationToPreview()
        {
            if (_ghostInstance != null)
            {
                _ghostInstance.transform.rotation = EditorSessionManager.GetCurrentCombinedRotation(EditorSessionManager.CurrentAsset);
            }
        }

        public static void UpdatePlacement()
        {
            if (!EditorSessionManager.IsBlockSelected || _ghostInstance == null || EditorViewportCamera.ViewportCamera == null) return;

            if (EditorSessionManager.IsMouseOverUI()) return;

            Ray ray = Input.GetMouseButton(1)
                ? EditorViewportCamera.ViewportCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : EditorViewportCamera.ViewportCamera.ScreenPointToRay(Input.mousePosition);

            int raycastMask = ~LayerMask.GetMask("Ignore Raycast");
            RaycastHit hit;
            bool hasHit = Physics.Raycast(ray, out hit, 1000f, raycastMask);

            Vector3 hitNormal = Vector3.up;
            Vector3 rawTargetPos;
            Collider hitCol = null;

            if (hasHit)
            {
                rawTargetPos = hit.point;
                hitNormal = hit.normal;
                hitCol = hit.collider;
            }
            else
            {
                rawTargetPos = ray.origin + ray.direction * 15f;
                hitNormal = -ray.direction;
            }

            Quaternion targetRot = EditorSessionManager.GetCurrentCombinedRotation(EditorSessionManager.CurrentAsset);

            _targetPosition = CalculateDynamicPosition(rawTargetPos, targetRot, _ghostBoxCollider, hitNormal, hasHit, hitCol, EditorSessionManager.CurrentAsset);

            _ghostInstance.transform.position = Vector3.Lerp(_ghostInstance.transform.position, _targetPosition, Time.deltaTime * 35f);
            _ghostInstance.transform.rotation = Quaternion.Slerp(_ghostInstance.transform.rotation, targetRot, Time.deltaTime * 24f);

            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1))
            {
                CommitPlacement();
            }
        }

        private static Vector3 CalculateDynamicPosition(Vector3 rawPos, Quaternion rot, BoxCollider ghostCol, Vector3 hitNormal, bool hasHit, Collider hitCol, CatalogAsset asset)
        {
            if (ghostCol == null) return rawPos;

            Vector3 halfExtents = Vector3.Scale(ghostCol.size * 0.5f, ghostCol.transform.lossyScale);
            Vector3 centerOffset = Vector3.Scale(ghostCol.center, ghostCol.transform.lossyScale);

            Vector3 targetPos = rawPos;

            if (hasHit)
            {
                Vector3 uX = rot * Vector3.right;
                Vector3 uY = rot * Vector3.up;
                Vector3 uZ = rot * Vector3.forward;

                float extentAlongNormal = halfExtents.x * Mathf.Abs(Vector3.Dot(uX, hitNormal))
                                        + halfExtents.y * Mathf.Abs(Vector3.Dot(uY, hitNormal))
                                        + halfExtents.z * Mathf.Abs(Vector3.Dot(uZ, hitNormal));

                float extraOffset = (asset != null) ? asset.VerticalOffset : 0f;

                Vector3 worldCenter = rawPos + hitNormal * (extentAlongNormal + 0.002f + extraOffset);
                targetPos = worldCenter - (rot * centerOffset);

                if (EditorSessionManager.CurrentGridSnap > 0.01f)
                {
                    float grid = EditorSessionManager.CurrentGridSnap;

                    if (Mathf.Abs(hitNormal.y) > 0.65f)
                    {
                        targetPos.x = Mathf.Round(targetPos.x / grid) * grid;
                        targetPos.z = Mathf.Round(targetPos.z / grid) * grid;
                    }
                    else if (Mathf.Abs(hitNormal.x) > 0.65f)
                    {
                        targetPos.y = Mathf.Round(targetPos.y / grid) * grid;
                        targetPos.z = Mathf.Round(targetPos.z / grid) * grid;
                    }
                    else if (Mathf.Abs(hitNormal.z) > 0.65f)
                    {
                        targetPos.x = Mathf.Round(targetPos.x / grid) * grid;
                        targetPos.y = Mathf.Round(targetPos.y / grid) * grid;
                    }
                }
            }
            else
            {
                if (EditorSessionManager.CurrentGridSnap > 0.01f)
                {
                    float grid = EditorSessionManager.CurrentGridSnap;
                    targetPos = new Vector3(
                        Mathf.Round(targetPos.x / grid) * grid,
                        Mathf.Round(targetPos.y / grid) * grid,
                        Mathf.Round(targetPos.z / grid) * grid
                    );
                }
            }

            int maxPasses = 12;
            int mask = ~LayerMask.GetMask("Ignore Raycast");

            for (int p = 0; p < maxPasses; p++)
            {
                Vector3 currentWorldCenter = targetPos + (rot * centerOffset);
                Collider[] overlaps = Physics.OverlapBox(currentWorldCenter, halfExtents, rot, mask, QueryTriggerInteraction.Ignore);

                bool hadOverlap = false;

                foreach (var col in overlaps)
                {
                    if (col == null || col == ghostCol) continue;
                    if (hasHit && col == hitCol) continue;
                    if (ghostCol.transform.IsChildOf(col.transform) || col.transform.IsChildOf(ghostCol.transform)) continue;
                    if (col.GetComponent<CharacterController>() != null) continue;

                    if (Physics.ComputePenetration(
                        ghostCol, targetPos, rot,
                        col, col.transform.position, col.transform.rotation,
                        out Vector3 pushDir, out float pushDist))
                    {
                        if (pushDist > 0.0005f)
                        {
                            targetPos += pushDir * (pushDist + 0.002f);
                            hadOverlap = true;
                            break;
                        }
                    }
                }

                if (!hadOverlap) break;
            }

            return targetPos;
        }

        private static void CommitPlacement()
        {
            CatalogAsset asset = EditorSessionManager.CurrentAsset;
            if (asset == null) return;

            Quaternion currentRot = EditorSessionManager.GetCurrentCombinedRotation(asset);
            float scale = EditorSessionManager.ActivePlacementScale;

            GameObject placed = EditorSessionManager.SpawnCatalogObject(asset, _targetPosition, scale, currentRot);

            if (placed != null)
            {
                EditorSessionManager.PlacedObjects.Add(placed);
                EditorSessionManager.LastPlacedObject = placed;

                float param = 0f;
                if (asset.IsJumper) param = EditorSessionManager.ActiveJumperForce;
                else if (asset.IsHelix) param = EditorSessionManager.ActiveTurbineSpeed;
                else if (asset.IsTurret) param = EditorSessionManager.ActiveTurretFireDelay;
                else if (asset.IsSpotlight && EditorSessionManager.PlacedLights.ContainsKey(placed)) param = EditorSessionManager.PlacedLights[placed].Range;

                EditorSessionManager.UndoHistory.Push(new HistoryRecord
                {
                    ActionType = HistoryActionType.Placement,
                    TargetObject = placed,
                    Asset = asset,
                    AssetName = asset.DisplayName,
                    Position = _targetPosition,
                    Rotation = currentRot,
                    Scale = scale,
                    CustomParameter = param
                });
                EditorSessionManager.RedoHistory.Clear();

                MelonLogger.Msg($">> Placed '{asset.DisplayName}' (Scale: {scale:F2}x, Grid: {EditorSessionManager.CurrentGridSnap}m)");
            }
        }

        public static void DestroyPreview()
        {
            if (_ghostInstance != null)
            {
                GameObject.Destroy(_ghostInstance);
                _ghostInstance = null;
                _ghostBoxCollider = null;
            }
        }
    }

    // =========================================================================================================
    // 3D CAROUSEL WHEEL TOOLBAR
    // =========================================================================================================
    public static class CarouselWheelToolbar
    {
        public static float WheelRadius = 0.48f;
        public static float IconScaleMultiplier = 0.025f;
        public static float WheelCenterY = -0.75f;
        public static float WheelDistanceZ = 0.95f;

        private static GameObject _wheelRoot = null;
        private static readonly List<GameObject> _spawnedIcons = new List<GameObject>();

        private static float _currentAngle = 0f;
        private static float _targetAngle = 0f;
        private static int _lastHighlightedIndex = -1;

        public static void CreateToolbar(Camera viewCam)
        {
            if (_wheelRoot != null || viewCam == null) return;

            _wheelRoot = new GameObject("Magical_Carousel_Wheel");
            _wheelRoot.transform.SetParent(viewCam.transform, false);

            _wheelRoot.transform.localPosition = new Vector3(0f, WheelCenterY, WheelDistanceZ);
            _wheelRoot.transform.localRotation = Quaternion.identity;
            _wheelRoot.layer = 2;

            _lastHighlightedIndex = -1;
            BuildCarouselIcons();
        }

        public static void SetTargetIndex(int index)
        {
            int total = EditorSessionManager.ActiveTabAssets.Count;
            if (total == 0) return;

            float stepAngle = 360f / total;
            _targetAngle = -index * stepAngle;
        }

        public static void UpdateCarousel()
        {
            if (_wheelRoot == null) return;

            _currentAngle = Mathf.LerpAngle(_currentAngle, _targetAngle, Time.deltaTime * 14f);
            _wheelRoot.transform.localRotation = Quaternion.Euler(0f, 0f, _currentAngle);

            for (int i = 0; i < _spawnedIcons.Count; i++)
            {
                if (_spawnedIcons[i] != null && i < EditorSessionManager.ActiveTabAssets.Count)
                {
                    _spawnedIcons[i].transform.localRotation = Quaternion.Euler(0f, 0f, -_currentAngle) * Quaternion.Euler(15f, Time.time * 30f, 0f);

                    bool isTop = (i == EditorSessionManager.SelectedAssetIndex);
                    float baseScale = EditorSessionManager.ActiveTabAssets[i].DefaultScale * IconScaleMultiplier;
                    _spawnedIcons[i].transform.localScale = Vector3.one * (isTop ? baseScale * 1.35f : baseScale);
                }
            }

            if (_lastHighlightedIndex != EditorSessionManager.SelectedAssetIndex)
            {
                _lastHighlightedIndex = EditorSessionManager.SelectedAssetIndex;
                for (int i = 0; i < _spawnedIcons.Count; i++)
                {
                    if (_spawnedIcons[i] != null)
                    {
                        ApplyIconHighlight(_spawnedIcons[i], i == EditorSessionManager.SelectedAssetIndex);
                    }
                }
            }

            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !EditorSessionManager.IsBlockSelected)
            {
                if (EditorSessionManager.IsMouseOverUI()) return;

                Ray ray = EditorViewportCamera.ViewportCamera.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, 2.5f, 1 << 2))
                {
                    if (hit.collider != null && hit.collider.transform.IsChildOf(_wheelRoot.transform))
                    {
                        EditorSessionManager.SelectCurrentAsset();
                    }
                }
            }
        }

        public static void ApplyIconHighlight(GameObject icon, bool isSelected)
        {
            if (icon == null) return;

            Renderer[] renderers = icon.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer rend = renderers[r];
                if (rend == null) continue;

                Material[] mats = rend.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null) continue;

                    if (isSelected)
                    {
                        mat.color = new Color(0.5f, 1.25f, 1.55f, 1f);
                        if (mat.HasProperty("_EmissionColor"))
                        {
                            mat.SetColor("_EmissionColor", new Color(0.35f, 0.95f, 1.35f, 1f));
                            mat.EnableKeyword("_EMISSION");
                        }
                    }
                    else
                    {
                        mat.color = new Color(0.42f, 0.46f, 0.52f, 0.65f);
                        if (mat.HasProperty("_EmissionColor"))
                        {
                            mat.SetColor("_EmissionColor", Color.black);
                        }
                    }
                }
            }
        }

        public static void BuildCarouselIcons()
        {
            foreach (var icon in _spawnedIcons)
            {
                if (icon != null) GameObject.Destroy(icon);
            }
            _spawnedIcons.Clear();

            var currentList = EditorSessionManager.ActiveTabAssets;
            int total = currentList.Count;
            if (total == 0 || _wheelRoot == null) return;

            float stepAngle = 360f / total;

            for (int i = 0; i < total; i++)
            {
                CatalogAsset asset = currentList[i];
                if (asset.SourceTemplate == null) continue;

                GameObject icon = GameObject.Instantiate(asset.SourceTemplate);
                icon.name = $"Icon_{i}_{asset.DisplayName}";
                icon.transform.SetParent(_wheelRoot.transform, false);

                icon.layer = 2;
                foreach (var tr in icon.GetComponentsInChildren<Transform>(true))
                {
                    tr.gameObject.layer = 2;
                }

                float angleRad = (i * stepAngle + 90f) * Mathf.Deg2Rad;
                icon.transform.localPosition = new Vector3(Mathf.Cos(angleRad) * WheelRadius, Mathf.Sin(angleRad) * WheelRadius, 0f);
                icon.transform.localScale = Vector3.one * (asset.DefaultScale * IconScaleMultiplier);

                foreach (var mb in icon.GetComponentsInChildren<MonoBehaviour>(true)) GameObject.DestroyImmediate(mb);
                foreach (var col in icon.GetComponentsInChildren<Collider>(true)) GameObject.DestroyImmediate(col);

                BoxCollider clickCol = icon.AddComponent<BoxCollider>();
                clickCol.isTrigger = true;
                clickCol.size = Vector3.one * 1.5f;

                EditorSessionManager.StripParticlesAndLights(icon);

                if (asset.IsSpawnGate)
                {
                    EditorSessionManager.ApplyGateVisualTint(icon, new Color(1.0f, 0.45f, 0.05f));
                }
                else if (asset.IsGoalGate)
                {
                    EditorSessionManager.ApplyGateVisualTint(icon, new Color(0.1f, 0.65f, 1.0f));
                }

                ApplyIconHighlight(icon, i == EditorSessionManager.SelectedAssetIndex);

                icon.SetActive(true);
                _spawnedIcons.Add(icon);
            }

            SetTargetIndex(EditorSessionManager.SelectedAssetIndex);
            _currentAngle = _targetAngle;
        }

        public static void DestroyToolbar()
        {
            if (_wheelRoot != null)
            {
                GameObject.Destroy(_wheelRoot);
                _wheelRoot = null;
                _spawnedIcons.Clear();
            }
        }
    }

    // =========================================================================================================
    // LEVEL PERSISTENCE AND SERIALIZATION SERVICE
    // =========================================================================================================
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
            float val;
            if (float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out val))
            {
                return val;
            }
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

            foreach (var obj in EditorSessionManager.PlacedObjects)
            {
                if (obj == null || !obj.activeSelf) continue;
                Vector3 pos = obj.transform.position;
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
                    param = cfg.Range;
                    string hexColor = ColorToHex(cfg.Color);
                    extraParams = $";{cfg.SpotAngle.ToString("F1", inv)};{cfg.Intensity.ToString("F2", inv)};{hexColor};{cfg.VolumetricDimmer.ToString("F2", inv)}";
                }

                lines.Add($"{name};{pos.x.ToString("F4", inv)};{pos.y.ToString("F4", inv)};{pos.z.ToString("F4", inv)};{scale.ToString("F4", inv)};{rot.x.ToString("F4", inv)};{rot.y.ToString("F4", inv)};{rot.z.ToString("F4", inv)};{rot.w.ToString("F4", inv)};{param.ToString("F2", inv)}{extraParams}");
            }

            File.WriteAllLines(path, lines.ToArray());
            EditorSessionManager.ShowNotification($"Saved {lines.Count - 4} objects to {cleanName}.txt!");
            MelonLogger.Msg($">> Saved {lines.Count - 4} objects with full parameters to {path}!");

            MapBrowserService.SelectedMapPath = path;
            MapBrowserService.SelectedMapName = cleanName;
            MapBrowserService.RefreshFiles();
        }

        public static void LoadLevel(string filename)
        {
            string cleanName = Path.GetFileNameWithoutExtension(filename);
            string path = Path.Combine(MapBrowserService.MyLevelsDir, $"{cleanName}.txt");
            if (!File.Exists(path))
            {
                path = Path.Combine(MapBrowserService.DownloadedLevelsDir, $"{cleanName}.txt");
            }

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

            int count = 0;
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string trimmed = line.Trim();
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
                        if (lowName.Contains("jumper") || obj.name.ToLower().Contains("jumper"))
                        {
                            EditorSessionManager.ApplyJumperForce(obj, customParam);
                        }
                        else if (lowName.Contains("helix") || obj.name.ToLower().Contains("helix"))
                        {
                            EditorSessionManager.ApplyTurbineSpeed(obj, customParam);
                        }
                        else if (lowName.Contains("turret") || obj.name.ToLower().Contains("turret"))
                        {
                            EditorSessionManager.ApplyTurretSettings(obj, customParam, 1500f);
                        }
                    }

                    if (lowName.Contains("rotating") && customParam != 0f)
                    {
                        EditorSessionManager.LaserRotationSpeeds[obj] = customParam;
                    }

                    if (lowName.Contains("spotlight") || obj.name.ToLower().Contains("spotlight"))
                    {
                        LightConfig cfg = new LightConfig();
                        if (customParam > 0f) cfg.Range = customParam;

                        if (p.Length >= 11) cfg.SpotAngle = ParseFloat(p[10]);
                        if (p.Length >= 12) cfg.Intensity = ParseFloat(p[11]);
                        if (p.Length >= 13)
                        {
                            cfg.Color = HexToColor(p[12]);
                        }
                        if (p.Length >= 14) cfg.VolumetricDimmer = ParseFloat(p[13]);

                        EditorSessionManager.ApplyLightConfig(obj, cfg);
                        if (EditorSessionManager.SelectedLightObject == null)
                        {
                            EditorSessionManager.SelectedLightObject = obj;
                        }
                    }

                    EditorSessionManager.PlacedObjects.Add(obj);
                    count++;
                }
            }

            string fName = Path.GetFileNameWithoutExtension(fullPath);
            EditorSessionManager.ShowNotification($"Loaded {count} objects from {fName}.txt!");
            MelonLogger.Msg($">> Loaded {count} objects from {fullPath}!");
        }
    }

    // =========================================================================================================
    // SCENE ASSET HARVESTING SERVICE
    // =========================================================================================================
    public static class SceneHarvestingService
    {
        public static Mesh CreateDoubleSidedPlaneMesh(float width, float height)
        {
            Mesh m = new Mesh();
            m.name = "Laser_DoubleSided_Mesh";

            float hw = width * 0.5f;
            float hh = height * 0.5f;

            Vector3[] vertices = new Vector3[]
            {
                // Front surface
                new Vector3(-hw, -hh, 0), new Vector3(hw, -hh, 0), new Vector3(hw, hh, 0), new Vector3(-hw, hh, 0),
                // Reverse surface
                new Vector3(-hw, -hh, 0), new Vector3(hw, -hh, 0), new Vector3(hw, hh, 0), new Vector3(-hw, hh, 0)
            };

            Vector2[] uvs = new Vector2[]
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1)
            };

            Vector3[] normals = new Vector3[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
                -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward
            };

            int[] triangles = new int[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7
            };

            m.vertices = vertices;
            m.uv = uvs;
            m.normals = normals;
            m.triangles = triangles;
            m.RecalculateBounds();
            return m;
        }

        public static void HarvestAllSceneModels()
        {
            EditorSessionManager.AllAssets.Clear();
            HashSet<string> seenMeshes = new HashSet<string>();

            MeshRenderer[] renderers = GameObject.FindObjectsOfType<MeshRenderer>();
            foreach (var r in renderers)
            {
                if (r != null && r.material != null)
                {
                    EditorSessionManager.CachedSceneMaterial = r.material;
                    break;
                }
            }

            GameObject ld = GameObject.Find("_LD") ?? GameObject.Find("L_D") ?? GameObject.Find("l_d");

            // 1. Interactive Launch Pads
            try
            {
                EditorSessionManager.PrefabJumper = GameObject.FindObjectOfType<Jumper>();
                if (ld != null && EditorSessionManager.PrefabJumper == null)
                {
                    Jumper[] jumpers = ld.GetComponentsInChildren<Jumper>(true);
                    if (jumpers.Length > 0) EditorSessionManager.PrefabJumper = jumpers[0];
                }
            }
            catch { }

            if (EditorSessionManager.PrefabJumper != null)
            {
                EditorSessionManager.AllAssets.Add(new CatalogAsset
                {
                    DisplayName = "Launch Jumper Pad",
                    SourceTemplate = EditorSessionManager.PrefabJumper.gameObject,
                    Category = AssetCategory.Gameplay,
                    IsJumper = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = 0f,
                    BaseRotation = Quaternion.Euler(35f, 0f, 0f)
                });
            }

            // 2. Waypoint and Checkpoint Gates (Standard, Spawn, and Goal)
            try
            {
                EditorSessionManager.PrefabCheckPoint = GameObject.FindObjectOfType<CheckPointScript>();
                if (ld != null && EditorSessionManager.PrefabCheckPoint == null)
                {
                    CheckPointScript[] cps = ld.GetComponentsInChildren<CheckPointScript>(true);
                    if (cps.Length > 0) EditorSessionManager.PrefabCheckPoint = cps[0];
                }
            }
            catch { }

            if (EditorSessionManager.PrefabCheckPoint != null)
            {
                EditorSessionManager.AllAssets.Add(new CatalogAsset
                {
                    DisplayName = "Checkpoint Gate",
                    SourceTemplate = EditorSessionManager.PrefabCheckPoint.gameObject,
                    Category = AssetCategory.Gameplay,
                    IsCheckPoint = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = -0.32f,
                    BaseRotation = Quaternion.identity
                });

                EditorSessionManager.AllAssets.Add(new CatalogAsset
                {
                    DisplayName = "Entry Checkpoint (Start)",
                    SourceTemplate = EditorSessionManager.PrefabCheckPoint.gameObject,
                    Category = AssetCategory.Gameplay,
                    IsCheckPoint = true,
                    IsSpawnGate = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = -0.32f,
                    BaseRotation = Quaternion.identity
                });

                EditorSessionManager.AllAssets.Add(new CatalogAsset
                {
                    DisplayName = "Goal Checkpoint (Finish)",
                    SourceTemplate = EditorSessionManager.PrefabCheckPoint.gameObject,
                    Category = AssetCategory.Gameplay,
                    IsCheckPoint = true,
                    IsGoalGate = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = -0.32f,
                    BaseRotation = Quaternion.identity
                });
            }

            // 3. Dynamic Technical Spotlights
            GameObject spotTemplate = new GameObject("Template_Spotlight");
            Light spotLight = spotTemplate.AddComponent<Light>();
            spotLight.type = LightType.Spot;
            spotLight.range = 50f;
            spotLight.spotAngle = 60f;
            spotLight.color = Color.cyan;
            spotLight.intensity = 3.0f;

            GameObject bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.transform.SetParent(spotTemplate.transform, false);
            bulb.transform.localScale = Vector3.one * 0.35f;
            Collider bulbCol = bulb.GetComponent<Collider>();
            if (bulbCol != null) bulbCol.isTrigger = true;

            spotTemplate.SetActive(false);

            EditorSessionManager.AllAssets.Add(new CatalogAsset
            {
                DisplayName = "Tech Spotlight",
                SourceTemplate = spotTemplate,
                Category = AssetCategory.Gameplay,
                IsSpotlight = true,
                DefaultScale = 1.0f,
                VerticalOffset = 0f,
                BaseRotation = Quaternion.identity
            });

            // 4. Laser Shader Material Acquisition
            Material laserMat = null;
            LaserManager lm = GameObject.FindObjectOfType<LaserManager>();
            if (lm != null && lm._sharedMaterial != null)
            {
                laserMat = lm._sharedMaterial;
            }

            if (laserMat == null)
            {
                MeshRenderer[] allRends = Resources.FindObjectsOfTypeAll<MeshRenderer>();
                for (int r = 0; r < allRends.Length; r++)
                {
                    if (allRends[r] != null && allRends[r].sharedMaterial != null && allRends[r].sharedMaterial.name.ToLower().Contains("laser"))
                    {
                        laserMat = allRends[r].sharedMaterial;
                        break;
                    }
                }
            }

            if (laserMat == null)
            {
                Shader s = Shader.Find("Unlit/Color") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("HDRP/Unlit");
                if (s != null)
                {
                    laserMat = new Material(s);
                    laserMat.color = new Color(1f, 0.05f, 0.05f, 0.9f);
                    if (laserMat.HasProperty("_Color")) laserMat.SetColor("_Color", new Color(1f, 0.05f, 0.05f, 0.9f));
                    if (laserMat.HasProperty("_EmissionColor"))
                    {
                        laserMat.SetColor("_EmissionColor", new Color(3.0f, 0.1f, 0.1f, 1f));
                        laserMat.EnableKeyword("_EMISSION");
                    }
                }
            }

            // 4A. Standard Laser Barrier (8m x 4m)
            GameObject laserTemplate = new GameObject("Template_Laser_Barrier");
            Mesh laserMesh = CreateDoubleSidedPlaneMesh(8f, 4f);
            MeshFilter laserMf = laserTemplate.AddComponent<MeshFilter>();
            laserMf.sharedMesh = laserMesh;
            MeshRenderer laserMr = laserTemplate.AddComponent<MeshRenderer>();
            if (laserMat != null) laserMr.sharedMaterial = laserMat;
            BoxCollider laserCol = laserTemplate.AddComponent<BoxCollider>();
            laserCol.isTrigger = true;
            laserCol.size = new Vector3(8f, 4f, 0.35f);
            laserTemplate.AddComponent<LaserScript>();
            laserTemplate.SetActive(false);

            EditorSessionManager.AllAssets.Add(new CatalogAsset
            {
                DisplayName = "Laser Barrier",
                SourceTemplate = laserTemplate,
                FilterMesh = laserMesh,
                Category = AssetCategory.Gameplay,
                IsLaser = true,
                DefaultScale = 1.0f,
                VerticalOffset = 0f,
                BaseRotation = Quaternion.identity
            });

            // 4B. Extended Laser Barrier (22m x 4m)
            GameObject longLaserTemplate = new GameObject("Template_Long_Laser_Barrier");
            Mesh longLaserMesh = CreateDoubleSidedPlaneMesh(22f, 4f);
            MeshFilter longMf = longLaserTemplate.AddComponent<MeshFilter>();
            longMf.sharedMesh = longLaserMesh;
            MeshRenderer longMr = longLaserTemplate.AddComponent<MeshRenderer>();
            if (laserMat != null) longMr.sharedMaterial = laserMat;
            BoxCollider longCol = longLaserTemplate.AddComponent<BoxCollider>();
            longCol.isTrigger = true;
            longCol.size = new Vector3(22f, 4f, 0.35f);
            longLaserTemplate.AddComponent<LaserScript>();
            longLaserTemplate.SetActive(false);

            EditorSessionManager.AllAssets.Add(new CatalogAsset
            {
                DisplayName = "Long Laser Barrier",
                SourceTemplate = longLaserTemplate,
                FilterMesh = longLaserMesh,
                Category = AssetCategory.Gameplay,
                IsLaser = true,
                DefaultScale = 1.0f,
                VerticalOffset = 0f,
                BaseRotation = Quaternion.identity
            });

            // 4C. Rotating Laser Array (Center Hub + 18m Dual Beam)
            GameObject rotLaserRoot = new GameObject("Template_Rotating_Laser");
            GameObject hubObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hubObj.name = "CenterHub";
            hubObj.transform.SetParent(rotLaserRoot.transform, false);
            hubObj.transform.localScale = new Vector3(1.4f, 2.8f, 1.4f);
            if (EditorSessionManager.CachedSceneMaterial != null)
            {
                MeshRenderer hr = hubObj.GetComponent<MeshRenderer>();
                if (hr != null) hr.material = EditorSessionManager.CachedSceneMaterial;
            }

            GameObject rotBeamObj = new GameObject("Beam");
            rotBeamObj.transform.SetParent(rotLaserRoot.transform, false);
            Mesh rotBeamMesh = CreateDoubleSidedPlaneMesh(18f, 2.2f);
            MeshFilter rmf = rotBeamObj.AddComponent<MeshFilter>();
            rmf.sharedMesh = rotBeamMesh;
            MeshRenderer rmr = rotBeamObj.AddComponent<MeshRenderer>();
            if (laserMat != null) rmr.sharedMaterial = laserMat;
            BoxCollider rbc = rotBeamObj.AddComponent<BoxCollider>();
            rbc.isTrigger = true;
            rbc.size = new Vector3(18f, 2.2f, 0.35f);
            rotBeamObj.AddComponent<LaserScript>();

            rotLaserRoot.SetActive(false);

            EditorSessionManager.AllAssets.Add(new CatalogAsset
            {
                DisplayName = "Rotating Laser Barrier",
                SourceTemplate = rotLaserRoot,
                FilterMesh = rotBeamMesh,
                Category = AssetCategory.Gameplay,
                IsLaser = true,
                IsRotatingLaser = true,
                DefaultScale = 1.0f,
                VerticalOffset = 0f,
                BaseRotation = Quaternion.identity
            });

            // 5. Stationary Defense Turrets
            try
            {
                TurretScript nativeTurret = GameObject.FindObjectOfType<TurretScript>();
                if (ld != null && nativeTurret == null)
                {
                    TurretScript[] turrets = ld.GetComponentsInChildren<TurretScript>(true);
                    if (turrets.Length > 0) nativeTurret = turrets[0];
                }

                if (nativeTurret != null)
                {
                    Transform rootT = nativeTurret.transform;
                    while (rootT.parent != null && (rootT.parent.name.ToLower().Contains("tourelle") || rootT.parent.name.ToLower().Contains("turret")))
                    {
                        rootT = rootT.parent;
                    }
                    EditorSessionManager.PrefabTurret = rootT.gameObject;
                }
                else if (ld != null)
                {
                    Transform[] allTransforms = ld.GetComponentsInChildren<Transform>(true);
                    foreach (var tr in allTransforms)
                    {
                        string n = tr.name.ToLower();
                        if (n.Contains("tourelle") || n.Contains("turret"))
                        {
                            EditorSessionManager.PrefabTurret = tr.gameObject;
                            break;
                        }
                    }
                }
            }
            catch { }

            if (EditorSessionManager.PrefabTurret != null)
            {
                EditorSessionManager.AllAssets.Add(new CatalogAsset
                {
                    DisplayName = "Defense Turret Enemy",
                    SourceTemplate = EditorSessionManager.PrefabTurret,
                    Category = AssetCategory.Gameplay,
                    IsTurret = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = 0f,
                    BaseRotation = Quaternion.identity
                });
            }

            // 6. Kinetic Turbines and Wind Generators
            try
            {
                Helix nativeHelix = GameObject.FindObjectOfType<Helix>();
                if (ld != null && nativeHelix == null)
                {
                    Helix[] hels = ld.GetComponentsInChildren<Helix>(true);
                    if (hels.Length > 0) nativeHelix = hels[0];
                }

                if (nativeHelix != null)
                {
                    EditorSessionManager.PrefabHelix = nativeHelix.gameObject;
                }
            }
            catch { }

            if (EditorSessionManager.PrefabHelix != null)
            {
                EditorSessionManager.AllAssets.Add(new CatalogAsset
                {
                    DisplayName = "Helix Turbine Fan",
                    SourceTemplate = EditorSessionManager.PrefabHelix,
                    Category = AssetCategory.Gameplay,
                    IsHelix = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = 0f,
                    BaseRotation = Quaternion.identity
                });
            }

            // 7. Extract Scene Geometry for Architecture and Props
            MeshFilter[] allFilters = GameObject.FindObjectsOfType<MeshFilter>();

            foreach (MeshFilter mf in allFilters)
            {
                if (mf.sharedMesh == null) continue;
                string mName = mf.sharedMesh.name.Trim();
                string goName = mf.gameObject.name.ToLower();
                string mLow = mName.ToLower();

                // Exclude procedural elements, secondary LOD stages, and runtime entities
                if (goName.Contains("helice") || goName.Contains("helix") || mLow.Contains("helix")) continue;
                if (goName.Contains("tourelle") || goName.Contains("turret") || mLow.Contains("tourelle")) continue;

                // Exclude specific low-detail wall assets
                if (mLow.Contains("wall_12x12x01_lod0") || mLow.Contains("wall 12x12x01 lod0") || goName.Contains("wall_12x12x01_lod0")) continue;

                if (mLow.Contains("impostor") || mLow.Contains("hole")) continue;
                if (mLow.Contains("lod1") || mLow.Contains("lod2") || mLow.Contains("lod3")) continue;
                if (mLow.StartsWith("combined")) continue;

                if (!seenMeshes.Contains(mName))
                {
                    seenMeshes.Add(mName);

                    string friendly = CleanDisplayName(mName, goName);
                    Vector3 size = mf.sharedMesh.bounds.size;
                    float defScale = DetermineDefaultScale(friendly, size);
                    Quaternion baseRot = DetermineBaseRotation(friendly, mf.gameObject);
                    AssetCategory cat = CategorizeAsset(friendly, goName, size);

                    EditorSessionManager.AllAssets.Add(new CatalogAsset
                    {
                        DisplayName = friendly,
                        SourceTemplate = mf.gameObject,
                        FilterMesh = mf.sharedMesh,
                        Category = cat,
                        DefaultScale = defScale,
                        VerticalOffset = 0f,
                        BaseRotation = baseRot
                    });
                }
            }
        }

        // Categorize geometry into Structural (Architecture) or Detail (Props) based on dimensional thresholds
        private static AssetCategory CategorizeAsset(string friendly, string goName, Vector3 size)
        {
            string low = (friendly + " " + goName).ToLower();
            float maxDim = Mathf.Max(size.x, size.y, size.z);

            // Large structural meshes: Platforms, walls, columns, monoliths
            if (low.Contains("platform") || low.Contains("plateforme") || low.Contains("floor") ||
                low.Contains("16x2x16") || low.Contains("12x4x2") || low.Contains("monolith") ||
                low.Contains("pillar") || low.Contains("wall") || low.Contains("cube") ||
                low.Contains("tower") || maxDim >= 3.8f)
            {
                return AssetCategory.Architecture;
            }

            // Smaller environmental and interactive details
            return AssetCategory.Props;
        }

        private static string CleanDisplayName(string meshName, string goName)
        {
            if (goName.Contains("16x2x16") || meshName.Contains("16x2x16")) return "Floor Platform 16x16";
            if (goName.Contains("2x2x2") || meshName.Contains("2x2x2")) return "Tech Crate 2x2";
            if (goName.Contains("4x4x4") || meshName.Contains("4x4x4")) return "Monolith Pillar 4x4";
            if (goName.Contains("checkpoint") || meshName.Contains("checkpoint")) return "Checkpoint Gate";
            if (goName.Contains("spark") || meshName.Contains("spark")) return "Energy Spark Core";
            if (goName.Contains("switch") || meshName.Contains("switch")) return "Tech Switch";

            string clean = meshName.Replace("Mesh", "").Replace("Instance", "").Replace("_", " ");
            return char.ToUpper(clean[0]) + clean.Substring(1);
        }

        private static float DetermineDefaultScale(string name, Vector3 size)
        {
            if (name.Contains("16x16")) return 0.55f;
            if (name.Contains("2x2")) return 1.0f;
            if (name.Contains("4x4")) return 1.2f;

            float maxDim = Mathf.Max(size.x, size.y, size.z);
            if (maxDim > 10f) return 0.45f;
            if (maxDim < 1.5f) return 1.5f;
            return 1.0f;
        }

        private static Quaternion DetermineBaseRotation(string name, GameObject template)
        {
            if (name.Contains("16x16") || name.Contains("Platform"))
            {
                return EditorSessionManager.GetAutoFlatRotation(template);
            }
            return Quaternion.identity;
        }

        public static void HideVanillaLevelGeometry()
        {
            var activeScene = SceneManager.GetActiveScene();
            string sName = activeScene.name.ToLower();
            if (sName.Contains("menu")) return;

            foreach (GameObject root in activeScene.GetRootGameObjects())
            {
                if (root == null) continue;
                string r = root.name;
                if (r == "_LA" || r == "_LD" || r == "L_A" || r == "L_D")
                {
                    root.SetActive(false);
                }
            }
        }
    }

    [HarmonyPatch(typeof(TurretScript), nameof(TurretScript.Shoot))]
    public static class TurretShootPatch
    {
        [HarmonyPostfix]
        public static void Postfix(TurretScript __instance)
        {
            if (__instance == null || __instance._bulletPrefab == null) return;

            try
            {
                string bulletPrefabName = __instance._bulletPrefab.name;
                Vector3 turretPos = __instance.transform.position;

                // Collect all colliders on this turret (head, body, base, trigger)
                Collider[] turretCols = __instance.GetComponentsInChildren<Collider>(true);
                SphereCollider triggerSphere = __instance._triggerAnimation;

                // Find the bullet spawned in this firing cycle
                Collider[] hits = Physics.OverlapSphere(turretPos, 8.0f, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < hits.Length; i++)
                {
                    Collider hitCol = hits[i];
                    if (hitCol == null) continue;

                    GameObject hitGo = hitCol.gameObject;
                    bool isBullet = hitGo.name.Contains(bulletPrefabName) ||
                                    hitCol.transform.root.name.Contains(bulletPrefabName) ||
                                    hitGo.name.ToLower().Contains("bullet") ||
                                    hitCol.transform.root.name.ToLower().Contains("bullet");

                    if (!isBullet) continue;

                    // 1. Tell Unity Physics this bullet must IGNORE all colliders on this turret.
                    //    This prevents muzzle self-collision while leaving the turret's hitboxes intact for the player.
                    Collider[] bulletCols = hitCol.transform.root.GetComponentsInChildren<Collider>(true);
                    for (int b = 0; b < bulletCols.Length; b++)
                    {
                        if (bulletCols[b] == null) continue;

                        for (int c = 0; c < turretCols.Length; c++)
                        {
                            if (turretCols[c] != null)
                            {
                                Physics.IgnoreCollision(bulletCols[b], turretCols[c], true);
                            }
                        }

                        if (triggerSphere != null)
                        {
                            Physics.IgnoreCollision(bulletCols[b], triggerSphere, true);
                        }
                    }

                    // 2. Nudge the bullet 0.95m forward along its firing trajectory so it clears the barrel
                    Rigidbody rb = hitGo.GetComponent<Rigidbody>();
                    Vector3 forwardDir = (rb != null && rb.velocity.sqrMagnitude > 0.1f)
                        ? rb.velocity.normalized
                        : __instance.transform.forward;

                    hitGo.transform.position += forwardDir * 0.95f;
                }
            }
            catch { }
        }
    }

    // =========================================================================================================
    // HARMONY HOOKS: LEVEL LIFECYCLE
    // =========================================================================================================
    [HarmonyPatch(typeof(StartLevelManager), nameof(StartLevelManager.StartLevelSequence))]
    public static class StartLevelPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            string currentScene = SceneManager.GetActiveScene().name.ToLower();
            if (currentScene.Contains("menu")) return;

            if (EditorSessionManager.IsCustomSessionActive)
            {
                EditorSessionManager.InitializeCustomLevel();
            }
        }
    }
}