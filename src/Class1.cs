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
using SceneUtility = UnityEngine.SceneManagement.SceneUtility;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using Il2CppDeadCore;
using Il2CppDeadCore.UI;

using File = System.IO.File;
using Directory = System.IO.Directory;
using Path = System.IO.Path;

[assembly: MelonInfo(typeof(DeadCoreEditor.DeadCoreLevelEditorMod), "DeadCore Level Editor Suite", "6.4.0", "Trufa")]
[assembly: MelonGame(null, null)]

namespace DeadCoreEditor
{
    /*
     * Mod Lifecycle Coordinator
     * Manages engine initialization, monitors scene changes, isolates custom editor states
     * from vanilla campaign sequences, and routes frame/physics updates.
     */
    public class DeadCoreLevelEditorMod : MelonMod
    {
        private static LogsMenu _lastTransformedLogsMenu = null;
        private static LogsMenu _cachedActiveMenu = null;
        public static int ActiveTab = 0;
        private static float _titleButtonScanTimer = 0f;
        private static bool _titleButtonHooked = false;
        private static float _menuScanTimer = 0f;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("=== DeadCore Level Editor Suite Initialized ===");
            MapBrowserService.EnsureDirectories();
            MapBrowserService.ScanStagingScenes();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _lastTransformedLogsMenu = null;
            _cachedActiveMenu = null;
            _titleButtonHooked = false;
            _titleButtonScanTimer = 0.1f;
            _menuScanTimer = 0f;

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
                if (!_titleButtonHooked)
                {
                    _titleButtonScanTimer -= Time.deltaTime;
                    if (_titleButtonScanTimer <= 0f)
                    {
                        _titleButtonScanTimer = 0.5f;
                        if (NativeLogsMenuHijacker.ReplaceTitleScreenLogsButton())
                        {
                            _titleButtonHooked = true;
                        }
                    }
                }

                if (Input.GetKeyDown(KeyCode.F2))
                {
                    NativeLogsMenuHijacker.OpenNativeMenu();
                }

                // Fast native active scene search (instant, no frame delay, no unmanaged heap scan)
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
                }

                return;
            }

            if (!EditorSessionManager.IsCustomSessionActive) return;
            EditorSessionManager.UpdateSession();
        }

        public override void OnFixedUpdate()
        {
            if (!EditorSessionManager.IsCustomSessionActive) return;
            EditorSessionManager.FixedUpdateSession();
        }

        public override void OnGUI()
        {
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
            if (ActiveTab == tabIndex && _lastTransformedLogsMenu == menu && NativeLogsMenuHijacker.SpawnedRowCount > 0) return;
            ActiveTab = tabIndex;
            NativeLogsMenuHijacker.TransformLogsMenu(menu, ActiveTab);
        }
    }

    public enum AssetCategory
    {
        Building = 0,
        Gameplay = 1
    }

    public enum PlacedObjectType
    {
        Generic = 0,
        Jumper,
        Turbine,
        Turret,
        SpawnGate,
        GoalGate,
        Sunlight,
        Spotlight,
        RotatingLaser,
        Laser,
        Checkpoint
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
        public bool IsSunlight;
        public bool IsLaser;
        public bool IsRotatingLaser;
        public float DefaultScale;
        public float VerticalOffset;
        public Quaternion BaseRotation;
    }

    public class ObjectMotionPath
    {
        public Vector3 PointA;
        public Vector3 PointB;
        public float Speed = 3.5f;
        public bool IsActive = true;
        public Collider[] CachedColliders = null;

        public ObjectMotionPath Clone()
        {
            return new ObjectMotionPath
            {
                PointA = this.PointA,
                PointB = this.PointB,
                Speed = this.Speed,
                IsActive = this.IsActive,
                CachedColliders = null
            };
        }
    }

    public enum HistoryActionType
    {
        Placement,
        Deletion,
        Parenting,
        MotionPath,
        Reposition
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

        public GameObject PreviousParent;
        public GameObject NewParent;

        public ObjectMotionPath PreviousMotionPath;
        public ObjectMotionPath NewMotionPath;

        public Vector3 PreviousPosition;
        public Vector3 NewPosition;
    }

    public class LightConfig
    {
        public Color Color = Color.cyan;
        public float SpotAngle = 60f;
        public float Intensity = 8.0f;
        public float VolumetricIntensity = 4.0f;
        public bool IsDirectional = false;
    }

    public class LevelMetadata
    {
        public string Title = "Untitled Level";
        public string Author = "Unknown";
        public string Difficulty = "Normal";
        public string Description = "No description provided.";
        public string StagingScene = "level01_Spark01";
    }

    /*
     * Native UI Hijacker & Browser Integrator
     * Intercepts DeadCore's built-in Logs Menu and repurposes it into an interactive level browser.
     * Uses DestroyImmediate to ensure native components do not hijack button inputs.
     */
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
                    {
                        objectLines.Add(l);
                    }
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
                    {
                        MenuGroupScript.CurrentGroup.Close();
                    }
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
                            GameObject.DestroyImmediate(child.gameObject);
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
            // Scans scene-active TMP elements across all UI root trees to guarantee catching tabs
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
            MapBrowserService.SelectedStagingScene = meta.StagingScene;

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

                CreateNativeInputRow(_nativeMetadataRoot.transform, sampleText, "LEVEL TITLE", 0f, 44f, 28f, out _titleInput);
                CreateNativeInputRow(_nativeMetadataRoot.transform, sampleText, "AUTHOR", -54f, 44f, 24f, out _authorInput);
                CreateDifficultyRow(_nativeMetadataRoot.transform, sampleText, -108f);
                CreateNativeInputRow(_nativeMetadataRoot.transform, sampleText, "DESCRIPTION", -164f, 95f, 22f, out _descInput, true);

                CreateLevelStatsHUD(_nativeMetadataRoot.transform, sampleText, -272f);
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
            createBtn.onClick.AddListener((Action)(() =>
            {
                CreateNewLevel(menu);
            }));

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

    /*
     * Persistent Map Storage & Staging Service
     * Manages level directory hierarchies, dynamically discovers all available game scenes
     * from Unity's build index, and stages execution.
     */
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
            int count = SceneManager.sceneCountInBuildSettings;

            for (int i = 0; i < count; i++)
            {
                string p = SceneUtility.GetScenePathByBuildIndex(i);
                string sceneName = Path.GetFileNameWithoutExtension(p);
                string sLower = sceneName.ToLower();

                if (!sLower.Contains("menu") && !sLower.Contains("boot") && !sLower.Contains("title") && !sLower.Contains("intro") && !sLower.Contains("root"))
                {
                    if (!AvailableStagingScenes.Contains(sceneName))
                    {
                        AvailableStagingScenes.Add(sceneName);
                    }
                }
            }

            if (!AvailableStagingScenes.Contains("level01_Spark01"))
            {
                AvailableStagingScenes.Insert(0, "level01_Spark01");
            }

            MelonLogger.Msg($">> Discovered {AvailableStagingScenes.Count} staging campaign scene(s) in build settings.");
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

            string targetScene = !string.IsNullOrEmpty(SelectedStagingScene) ? SelectedStagingScene : "level01_Spark01";

            MelonLogger.Msg($">> [Map Browser] Launching: '{SelectedMapName}' via Scene: '{targetScene}'");
            SceneLoader.LoadLevel(targetScene, false, true);
        }
    }

    /*
     * Central Editor Session & Gameplay Engine
     * Orchestrates interactive 3D placement, frame-rate independent physics evaluations
     * (parabolic launch pads, radial wind push tunnels, OBB laser collisions),
     * and IMGUI developer tooling.
     */
    public static class EditorSessionManager
    {
        public static bool CustomLevelSelected = true;
        public static bool IsCustomSessionActive = false;
        public static bool IsEditModeActive = false;
        public static bool IsLevelInitialized = false;

        public static List<CatalogAsset> AllAssets = new List<CatalogAsset>();
        public static List<CatalogAsset> BuildingAssets = new List<CatalogAsset>();
        public static List<CatalogAsset> GameplayAssets = new List<CatalogAsset>();

        public static AssetCategory CurrentTab = AssetCategory.Building;
        public static int SelectedAssetIndex = 0;
        public static bool IsBlockSelected = false;

        public static float ActiveJumperForce = 25.0f;
        public static float ActiveTurbineSpeed = 35.0f;
        public static float ActiveTurretFireDelay = 1.0f;
        public static float ActivePlacementScale = 0.55f;
        public static float CurrentGridSnap = 1.0f;
        public static bool AutoAlignToSurface = false;

        public static Dictionary<GameObject, ObjectMotionPath> MotionPaths = new Dictionary<GameObject, ObjectMotionPath>();
        public static GameObject PathEditTarget = null;
        public static float DefaultPathSpeed = 3.5f;

        public static GameObject ParentingChildTarget = null;
        public static GameObject RepositionTarget = null;
        public static Vector3 RepositionStartPosition = Vector3.zero;

        public static float ActiveLaserRotationSpeed = 45.0f;
        public static Dictionary<GameObject, float> LaserRotationSpeeds = new Dictionary<GameObject, float>();

        public static float TargetPitch = 0f;
        public static float TargetYaw = 0f;
        public static float TargetRoll = 0f;

        private static KeyCode _lastHeldKey = KeyCode.None;
        private static float _keyHoldDuration = 0f;
        private static float _keyRepeatTimer = 0f;

        public static bool ShowParamsWindow = true;
        public static bool ShowDebugOverlay = false;
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

        public static Stack<HistoryRecord> UndoHistory = new Stack<HistoryRecord>();
        public static Stack<HistoryRecord> RedoHistory = new Stack<HistoryRecord>();

        private static string _notificationMessage = "";
        private static float _notificationTimer = 0f;

        private static bool _isBoostActive = false;
        private static Vector3 _currentBoostVelocity = Vector3.zero;
        private static float _jumperTriggerCooldown = 0f;

        public static float LevelTimer = 0f;
        public static bool IsLevelCompleted = false;

        private static GameObject _cachedPlayer = null;
        private static CharacterController _cachedCharacterController = null;
        public static Camera PlayerCameraInstance = null;
        public static CharacterController PlayerControllerInstance = null;
        public static Vector3 FrozenPlayerPosition = Vector3.zero;
        public static Quaternion FrozenPlayerRotation = Quaternion.identity;
        public static Vector3 LevelSpawnPosition = new Vector3(-241f, -95f, -6f);

        public static float CachedVoidDeathY = -140f;

        private static float _debugFps = 60f;
        private static float _debugFpsTimer = 0.25f;

        public static List<CatalogAsset> ActiveTabAssets =>
            (CurrentTab == AssetCategory.Building) ? BuildingAssets : GameplayAssets;

        public static void RebuildAssetCategoryCaches()
        {
            BuildingAssets.Clear();
            GameplayAssets.Clear();
            for (int i = 0; i < AllAssets.Count; i++)
            {
                CatalogAsset a = AllAssets[i];
                if (a.Category == AssetCategory.Building)
                    BuildingAssets.Add(a);
                else
                    GameplayAssets.Add(a);
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
            Rect winRect = new Rect(Screen.width - 360f, 20f, 340f, 490f);
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

            _cachedPlayer = null;
            _cachedCharacterController = null;
            SceneHarvestingService.NativeSceneSun = null;

            EditorViewportCamera.DestroyCamera();
            PlacementHologramController.DestroyPreview();
            CarouselWheelToolbar.DestroyToolbar();

            // Destroy procedural meshes and materials to eliminate native memory leaks
            SceneHarvestingService.CleanupProceduralResources();

            PlayerCameraInstance = null;
            PlayerControllerInstance = null;

            AllAssets.Clear();
            BuildingAssets.Clear();
            GameplayAssets.Clear();
            CurrentTab = AssetCategory.Building;
            SelectedAssetIndex = 0;
            IsBlockSelected = false;

            PrefabJumper = null;
            PrefabCheckPoint = null;
            PrefabHelix = null;
            PrefabTurret = null;

            ClearAllPlacedObjects();

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
            AutoAlignToSurface = false;
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

            GameObject player = FindPlayerEntity();
            CharacterController cc = GetPlayerController();

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
            }

            if (Input.GetKeyDown(KeyCode.F1))
            {
                ToggleEditMode();
            }

            if (Input.GetKeyDown(KeyCode.F3))
            {
                ShowParamsWindow = !ShowParamsWindow;
            }

            if (Input.GetKeyDown(KeyCode.F4))
            {
                SceneHarvestingService.DebugDumpSceneLighting();
                ShowNotification("Lighting hierarchy dumped to MelonLoader console (F4)");
            }

            if (Input.GetKeyDown(KeyCode.F7))
            {
                ShowDebugOverlay = !ShowDebugOverlay;
                ShowNotification($"Debug Engine Overlay: {(ShowDebugOverlay ? "ENABLED" : "DISABLED")}");
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

                if (Input.GetMouseButtonDown(0) && !IsBlockSelected && !IsMouseOverUI())
                {
                    GameObject aimed = GetAimedPlacedObject();
                    if (aimed != null && (PlacedLights.ContainsKey(aimed) ||
                        (PlacedObjectTypes.TryGetValue(aimed, out var t) && (t == PlacedObjectType.Spotlight || t == PlacedObjectType.Sunlight))))
                    {
                        SelectedLightObject = aimed;
                        ShowNotification("Selected Light (Use Arrow Keys to Aim)");
                    }
                }
            }
        }

        /*
         * Deterministic Physics Dispatcher (FixedUpdate)
         * Evaluates kinematic paths, wind push tunnels, launch pad accelerations, and hazard volumes
         * at a fixed time-step to preserve identical physics across any display refresh rate.
         */
        public static void FixedUpdateSession()
        {
            if (!IsLevelInitialized) return;

            GameObject player = FindPlayerEntity();
            CharacterController cc = GetPlayerController();

            UpdateObjectMotionPaths(player, cc);

            if (!IsEditModeActive && player != null)
            {
                CheckVoidFall(player);
                CheckJumperBoostPhysics(player, cc);
                CheckHelixWindPushing(player, cc);
                CheckLaserBarriers(player, cc);
            }
        }

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

                float dist = Vector3.Distance(path.PointA, path.PointB);
                if (dist < 0.05f) continue;

                float speed = Mathf.Max(0.2f, path.Speed);
                float duration = dist / speed;
                float t = Mathf.PingPong(Time.time / duration, 1.0f);
                float smoothT = Mathf.SmoothStep(0f, 1f, t);

                Vector3 targetPos = Vector3.Lerp(path.PointA, path.PointB, smoothT);
                Vector3 delta = targetPos - obj.transform.position;

                obj.transform.position = targetPos;

                if (canPushPlayer && delta.sqrMagnitude > 0.00001f)
                {
                    if (path.CachedColliders == null || path.CachedColliders.Length == 0)
                    {
                        path.CachedColliders = obj.GetComponentsInChildren<Collider>(true);
                    }

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
                MelonLogger.Msg($">> [VICTORY] Level completed in {LevelTimer:F2} seconds!");
            }
        }

        private static void CheckJumperBoostPhysics(GameObject player, CharacterController cc)
        {
            float fdt = Time.fixedDeltaTime;
            if (_jumperTriggerCooldown > 0f) _jumperTriggerCooldown -= fdt;
            if (cc == null) return;

            if (_isBoostActive)
            {
                cc.Move(_currentBoostVelocity * fdt);
                _currentBoostVelocity.y += -22f * fdt;

                if (cc.isGrounded && _jumperTriggerCooldown < 0.2f)
                {
                    _isBoostActive = false;
                }
            }

            if (_jumperTriggerCooldown > 0f) return;

            Vector3 pPos = player.transform.position;

            for (int i = 0; i < PlacedJumpers.Count; i++)
            {
                GameObject obj = PlacedJumpers[i];
                if (obj == null || !obj.activeSelf) continue;

                Vector3 jPos = obj.transform.position;
                Vector3 diff = pPos - jPos;

                float horizSq = (diff.x * diff.x) + (diff.z * diff.z);
                float vert = Mathf.Abs(diff.y);

                if (horizSq < 6.25f && vert < 1.9f)
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

        private static void CheckHelixWindPushing(GameObject player, CharacterController cc)
        {
            if (PlacedTurbines.Count == 0 || cc == null) return;

            Vector3 pPos = player.transform.position + Vector3.up * 1.0f;
            float fdt = Time.fixedDeltaTime;

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
                    helixScript._hingeJoint.transform.Rotate(Vector3.forward, (speed * 12f) * fdt, Space.Self);
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
                        cc.Move(pushDir * pushSpeed * fdt);
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

                if ((obj.transform.position - playerCenter).sqrMagnitude > 225f) continue;

                BoxCollider bc = (i < PlacedLaserColliders.Count) ? PlacedLaserColliders[i] : null;
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
            {
                RespawnPlayer(player);
            }
        }

        public static CheckPointScript ActiveCustomCheckpoint = null;

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
            player.transform.position = LevelSpawnPosition + Vector3.up * 0.2f;

            foreach (var obj in PlacedObjects)
            {
                if (obj != null && PlacedObjectTypes.TryGetValue(obj, out var t) && t == PlacedObjectType.SpawnGate)
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
            MelonLogger.Msg(">> [Restart] Restarted run cleanly from level start!");
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

            CharacterController cc = GetPlayerController();
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
            SetSpotlightMeshesVisible(IsEditModeActive);

            GameObject player = FindPlayerEntity();

            if (IsEditModeActive)
            {
                _isBoostActive = false;

                if (player != null)
                {
                    FrozenPlayerPosition = player.transform.position;
                    FrozenPlayerRotation = player.transform.rotation;
                    PlayerControllerInstance = GetPlayerController();
                    if (PlayerControllerInstance != null) PlayerControllerInstance.enabled = false;
                }

                PlayerCameraInstance = Camera.main;

                EditorViewportCamera.InitializeCamera(PlayerCameraInstance);
                CarouselWheelToolbar.CreateToolbar(EditorViewportCamera.ViewportCamera);

                SelectCurrentAsset();

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

        /*
         * Calculates world-space position under the camera crosshair with active grid snap rounding.
         * Used for setting Point B waypoints and moving objects without needing an active block hologram.
         */
        public static Vector3 GetAimedWorldPosition()
        {
            if (EditorViewportCamera.ViewportCamera == null) return Vector3.zero;

            Ray ray = Input.GetMouseButton(1)
                ? EditorViewportCamera.ViewportCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : EditorViewportCamera.ViewportCamera.ScreenPointToRay(Input.mousePosition);

            int mask = ~LayerMask.GetMask("Ignore Raycast");
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, mask, QueryTriggerInteraction.Ignore))
            {
                float grid = CurrentGridSnap;
                if (grid > 0.01f)
                {
                    return new Vector3(
                        Mathf.Round(hit.point.x / grid) * grid,
                        Mathf.Round(hit.point.y / grid) * grid,
                        Mathf.Round(hit.point.z / grid) * grid
                    );
                }
                return hit.point;
            }

            return ray.origin + ray.direction * 15f;
        }

        /*
         * Editor Workflow Hotkey Processor
         * Manages shortcut dispatch for asset palette toggling, entity repositioning (V),
         * compound parenting (P), waypoint definition (M), surface orientation (C),
         * undo/redo, and disk serialization.
         */
        private static void HandleFlowShortcuts()
        {
            bool isCtrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (!Input.GetMouseButton(1) && !isCtrlHeld)
            {
                if (Input.GetKeyDown(KeyCode.Tab))
                {
                    AssetCategory nextCat = (CurrentTab == AssetCategory.Building) ? AssetCategory.Gameplay : AssetCategory.Building;
                    SetCategory(nextCat);
                }
                else if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
                {
                    SetCategory(AssetCategory.Building);
                }
                else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
                {
                    SetCategory(AssetCategory.Gameplay);
                }
            }

            // Direct in-place Entity Pick-up & Reposition Tool (V)
            if (Input.GetKeyDown(KeyCode.V) && !isCtrlHeld)
            {
                if (RepositionTarget == null)
                {
                    GameObject aimed = GetAimedPlacedObject();
                    if (aimed != null)
                    {
                        RepositionTarget = aimed;
                        RepositionStartPosition = aimed.transform.position;
                        ShowNotification($"Picked up '{aimed.name}'! Aim & Left-Click to drop. (Backspace to cancel)");
                        MelonLogger.Msg($">> [Move] Picked up '{aimed.name}' for repositioning.");
                    }
                    else
                    {
                        ShowNotification("Aim at an object first to pick up and move it.");
                    }
                }
                else
                {
                    Vector3 finalPos = RepositionTarget.transform.position;
                    Vector3 delta = finalPos - RepositionStartPosition;

                    if (MotionPaths.ContainsKey(RepositionTarget))
                    {
                        MotionPaths[RepositionTarget].PointA += delta;
                        MotionPaths[RepositionTarget].PointB += delta;
                    }

                    UndoHistory.Push(new HistoryRecord
                    {
                        ActionType = HistoryActionType.Reposition,
                        TargetObject = RepositionTarget,
                        PreviousPosition = RepositionStartPosition,
                        NewPosition = finalPos
                    });
                    RedoHistory.Clear();

                    ShowNotification($"Placed '{RepositionTarget.name}' at new coordinates!");
                    RepositionTarget = null;
                }
            }

            if (RepositionTarget != null)
            {
                RepositionTarget.transform.position = GetAimedWorldPosition();
                if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !IsMouseOverUI())
                {
                    Vector3 finalPos = RepositionTarget.transform.position;
                    Vector3 delta = finalPos - RepositionStartPosition;

                    if (MotionPaths.ContainsKey(RepositionTarget))
                    {
                        MotionPaths[RepositionTarget].PointA += delta;
                        MotionPaths[RepositionTarget].PointB += delta;
                    }

                    UndoHistory.Push(new HistoryRecord
                    {
                        ActionType = HistoryActionType.Reposition,
                        TargetObject = RepositionTarget,
                        PreviousPosition = RepositionStartPosition,
                        NewPosition = finalPos
                    });
                    RedoHistory.Clear();

                    ShowNotification($"Placed '{RepositionTarget.name}' at new coordinates!");
                    RepositionTarget = null;
                }
            }

            // Compound hierarchy assembly linking & unlinking (P)
            if (Input.GetKeyDown(KeyCode.P) && !isCtrlHeld)
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    GameObject aimed = GetAimedPlacedObject();
                    if (aimed != null)
                    {
                        if (aimed.transform.parent != null)
                        {
                            GameObject oldParent = aimed.transform.parent.gameObject;

                            UndoHistory.Push(new HistoryRecord
                            {
                                ActionType = HistoryActionType.Parenting,
                                TargetObject = aimed,
                                PreviousParent = oldParent,
                                NewParent = null
                            });
                            RedoHistory.Clear();

                            aimed.transform.SetParent(null, true);
                            RecalculateParentChildCount(oldParent);

                            ShowNotification($"Unparented '{aimed.name}' from '{oldParent.name}' (Ctrl+Z to Undo)");
                            MelonLogger.Msg($">> [Parenting] Unparented '{aimed.name}'");
                        }
                        else
                        {
                            ShowNotification($"'{aimed.name}' has no parent.");
                        }
                    }
                    ParentingChildTarget = null;
                }
                else
                {
                    if (ParentingChildTarget == null)
                    {
                        GameObject aimed = GetAimedPlacedObject();
                        if (aimed != null)
                        {
                            ParentingChildTarget = aimed;
                            ShowNotification($"Selected Child '{aimed.name}'. Now aim at PARENT and press P.");
                            MelonLogger.Msg($">> [Parenting] Selected Child '{aimed.name}'. Awaiting parent selection...");
                        }
                        else
                        {
                            ShowNotification("Aim at an object to select as child first!");
                        }
                    }
                    else
                    {
                        GameObject aimedParent = GetAimedPlacedObject();
                        if (aimedParent != null && aimedParent != ParentingChildTarget)
                        {
                            if (aimedParent.transform.IsChildOf(ParentingChildTarget.transform))
                            {
                                ShowNotification("Cannot parent to own child!");
                            }
                            else
                            {
                                GameObject oldParent = ParentingChildTarget.transform.parent != null ? ParentingChildTarget.transform.parent.gameObject : null;

                                UndoHistory.Push(new HistoryRecord
                                {
                                    ActionType = HistoryActionType.Parenting,
                                    TargetObject = ParentingChildTarget,
                                    PreviousParent = oldParent,
                                    NewParent = aimedParent
                                });
                                RedoHistory.Clear();

                                ParentingChildTarget.transform.SetParent(aimedParent.transform, true);
                                if (oldParent != null) RecalculateParentChildCount(oldParent);
                                RecalculateParentChildCount(aimedParent);

                                ShowNotification($"Linked: '{ParentingChildTarget.name}' -> '{aimedParent.name}'! (Ctrl+Z to Undo)");
                                MelonLogger.Msg($">> [Parenting] Linked child '{ParentingChildTarget.name}' to parent '{aimedParent.name}'");
                                ParentingChildTarget = null;
                            }
                        }
                        else
                        {
                            ShowNotification("Invalid parent. Parenting cancelled.");
                            ParentingChildTarget = null;
                        }
                    }
                }
            }

            // Kinematic waypoint definition (M)
            if (Input.GetKeyDown(KeyCode.M) && !isCtrlHeld)
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    GameObject aimed = GetAimedPlacedObject();
                    if (aimed != null && MotionPaths.ContainsKey(aimed))
                    {
                        ObjectMotionPath prevPath = MotionPaths[aimed].Clone();
                        aimed.transform.position = prevPath.PointA;
                        MotionPaths.Remove(aimed);

                        UndoHistory.Push(new HistoryRecord
                        {
                            ActionType = HistoryActionType.MotionPath,
                            TargetObject = aimed,
                            PreviousMotionPath = prevPath,
                            NewMotionPath = null
                        });
                        RedoHistory.Clear();

                        ShowNotification($"Cleared motion path for '{aimed.name}' (Ctrl+Z to Undo)");
                    }
                    PathEditTarget = null;
                }
                else
                {
                    if (PathEditTarget == null)
                    {
                        GameObject aimed = GetAimedPlacedObject();
                        if (aimed != null)
                        {
                            PathEditTarget = aimed;
                            if (!MotionPaths.ContainsKey(aimed))
                            {
                                MotionPaths[aimed] = new ObjectMotionPath
                                {
                                    PointA = aimed.transform.position,
                                    PointB = aimed.transform.position + Vector3.up * 6.0f,
                                    Speed = DefaultPathSpeed
                                };
                            }
                            else
                            {
                                aimed.transform.position = MotionPaths[aimed].PointA;
                            }
                            ShowNotification($"[Path Edit] Aim at destination & press M to lock Point B.");
                        }
                    }
                    else
                    {
                        Vector3 targetB = GetAimedWorldPosition();
                        ObjectMotionPath prevPath = MotionPaths.ContainsKey(PathEditTarget) ? MotionPaths[PathEditTarget].Clone() : null;

                        MotionPaths[PathEditTarget].PointB = targetB;
                        ObjectMotionPath newPath = MotionPaths[PathEditTarget].Clone();

                        UndoHistory.Push(new HistoryRecord
                        {
                            ActionType = HistoryActionType.MotionPath,
                            TargetObject = PathEditTarget,
                            PreviousMotionPath = prevPath,
                            NewMotionPath = newPath
                        });
                        RedoHistory.Clear();

                        ShowNotification($"[Path Edit] Point B locked at {targetB.x:F1}, {targetB.y:F1}, {targetB.z:F1}!");
                        PathEditTarget = null;
                    }
                }
            }

            // Cancellation keybinds (Backspace, Escape, X)
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.X) || Input.GetKeyDown(KeyCode.Backspace))
            {
                if (RepositionTarget != null)
                {
                    RepositionTarget.transform.position = RepositionStartPosition;
                    RepositionTarget = null;
                    ShowNotification("Movement/Reposition Cancelled (Backspace)");
                }
                else if (PathEditTarget != null)
                {
                    PathEditTarget = null;
                    ShowNotification("Path Editing Cancelled (Backspace)");
                }
                else if (ParentingChildTarget != null)
                {
                    ParentingChildTarget = null;
                    ShowNotification("Parenting Cancelled (Backspace)");
                }
                else if (IsBlockSelected)
                {
                    IsBlockSelected = false;
                    PlacementHologramController.DestroyPreview();
                    ShowNotification("Placement Cancelled (Backspace)");
                }
                else if (SelectedLightObject != null)
                {
                    SelectedLightObject = null;
                    ShowNotification("Light Deselected (Backspace)");
                }
            }

            if (Input.GetKeyDown(KeyCode.C) && !isCtrlHeld)
            {
                AutoAlignToSurface = !AutoAlignToSurface;
                string st = AutoAlignToSurface ? "ON (Wall/Ceiling)" : "OFF (Manual)";
                ShowNotification($"Surface Align: {st}");
                PlacementHologramController.ApplyRotationToPreview();
            }

            if (Input.GetKeyDown(KeyCode.G) && !isCtrlHeld)
            {
                CycleGridSnap();
            }

            if (isCtrlHeld)
            {
                if (Input.GetKeyDown(KeyCode.Z))
                {
                    if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                        PerformRedo();
                    else
                        PerformUndo();
                }
                else if (Input.GetKeyDown(KeyCode.Y))
                {
                    PerformRedo();
                }
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

        public static void RecalculateParentChildCount(GameObject parentObj)
        {
            if (parentObj == null) return;
            int count = 0;
            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                if (PlacedObjects[i] != null && PlacedObjects[i].transform.parent == parentObj.transform)
                    count++;
            }
            if (count > 0)
                PlacedParentChildCounts[parentObj] = count;
            else
                PlacedParentChildCounts.Remove(parentObj);
        }

        public static void SetCategory(AssetCategory newCategory)
        {
            if (CurrentTab == newCategory) return;
            CurrentTab = newCategory;
            SelectedAssetIndex = 0;

            CarouselWheelToolbar.BuildCarouselIcons();
            SelectCurrentAsset();

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
            if (SelectedLightObject != null && SelectedLightObject.activeSelf && !IsBlockSelected)
            {
                HandleSelectedSpotlightRotation();
                return;
            }

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

        private static void HandleSelectedSpotlightRotation()
        {
            if (SelectedLightObject == null || !SelectedLightObject.activeSelf) return;

            bool isSun = PlacedObjectTypes.TryGetValue(SelectedLightObject, out var t) && t == PlacedObjectType.Sunlight;

            if (Input.GetKeyDown(KeyCode.T))
            {
                Vector3 e = SelectedLightObject.transform.eulerAngles;
                e.x = Mathf.Round(e.x / 90f) * 90f;
                e.y = Mathf.Round(e.y / 90f) * 90f;
                e.z = Mathf.Round(e.z / 90f) * 90f;
                SelectedLightObject.transform.rotation = Quaternion.Euler(e);

                if (PlacedLights.TryGetValue(SelectedLightObject, out LightConfig cfg))
                {
                    ApplyLightConfig(SelectedLightObject, cfg);
                }

                ShowNotification($"Light Snapped to 90° ({e.x:F0}°, {e.y:F0}°, {e.z:F0}°)");
                return;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                SelectedLightObject.transform.rotation = isSun ? Quaternion.Euler(50f, -30f, 0f) : Quaternion.identity;

                if (PlacedLights.TryGetValue(SelectedLightObject, out LightConfig cfg))
                {
                    ApplyLightConfig(SelectedLightObject, cfg);
                }

                ShowNotification("Light Orientation Reset");
                return;
            }

            float step = 5f;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                step = 15f;
            else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                step = 1f;

            KeyCode activeKey = KeyCode.None;
            Vector3 rotDelta = Vector3.zero;

            if (Input.GetKey(KeyCode.LeftArrow))
            {
                activeKey = KeyCode.LeftArrow;
                rotDelta.y = -step;
            }
            else if (Input.GetKey(KeyCode.RightArrow))
            {
                activeKey = KeyCode.RightArrow;
                rotDelta.y = step;
            }
            else if (Input.GetKey(KeyCode.UpArrow))
            {
                activeKey = KeyCode.UpArrow;
                rotDelta.x = step;
            }
            else if (Input.GetKey(KeyCode.DownArrow))
            {
                activeKey = KeyCode.DownArrow;
                rotDelta.x = -step;
            }
            else if (Input.GetKey(KeyCode.PageUp) || Input.GetKey(KeyCode.LeftBracket))
            {
                activeKey = KeyCode.PageUp;
                rotDelta.z = -step;
            }
            else if (Input.GetKey(KeyCode.PageDown) || Input.GetKey(KeyCode.RightBracket))
            {
                activeKey = KeyCode.PageDown;
                rotDelta.z = step;
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
                if (Mathf.Abs(rotDelta.y) > 0.001f)
                    SelectedLightObject.transform.Rotate(Vector3.up, rotDelta.y, Space.World);

                if (Mathf.Abs(rotDelta.x) > 0.001f)
                    SelectedLightObject.transform.Rotate(Vector3.right, rotDelta.x, Space.Self);

                if (Mathf.Abs(rotDelta.z) > 0.001f)
                    SelectedLightObject.transform.Rotate(Vector3.forward, rotDelta.z, Space.Self);

                if (PlacedLights.TryGetValue(SelectedLightObject, out LightConfig cfg))
                {
                    ApplyLightConfig(SelectedLightObject, cfg);
                }

                Vector3 angles = SelectedLightObject.transform.eulerAngles;
                ShowNotification($"Aim: Pitch {angles.x:F0}° | Yaw {angles.y:F0}° | Roll {angles.z:F0}°");
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
                float stepDir = Mathf.Sign(scroll);

                GameObject aimed = GetAimedPlacedObject();
                if ((PathEditTarget != null || aimed != null) && (MotionPaths.ContainsKey(PathEditTarget ?? aimed)))
                {
                    GameObject target = PathEditTarget ?? aimed;
                    MotionPaths[target].Speed = Mathf.Clamp(MotionPaths[target].Speed + stepDir * 0.5f, 0.5f, 30.0f);
                    ShowNotification($"[Path Speed] {MotionPaths[target].Speed:F1} m/s");
                    return;
                }

                if (CurrentAsset != null && CurrentAsset.IsJumper)
                {
                    ActiveJumperForce = Mathf.Max(1.0f, ActiveJumperForce + stepDir * 2.5f);
                    MelonLogger.Msg($">> [Jumper Force] Set: {ActiveJumperForce:F1}");
                    if (aimed != null && (JumperForces.ContainsKey(aimed) || aimed.name.ToLower().Contains("jumper")))
                        ApplyJumperForce(aimed, ActiveJumperForce);
                }
                else if (CurrentAsset != null && CurrentAsset.IsHelix)
                {
                    ActiveTurbineSpeed = Mathf.Max(1.0f, ActiveTurbineSpeed + stepDir * 5.0f);
                    MelonLogger.Msg($">> [Turbine Speed] Set: {ActiveTurbineSpeed:F1}");
                    if (aimed != null && (TurbineSpeeds.ContainsKey(aimed) || aimed.name.ToLower().Contains("helix")))
                        ApplyTurbineSpeed(aimed, ActiveTurbineSpeed);
                }
                else if (CurrentAsset != null && CurrentAsset.IsRotatingLaser)
                {
                    ActiveLaserRotationSpeed += stepDir * 5.0f;
                    MelonLogger.Msg($">> [Rotating Laser Speed] Set: {ActiveLaserRotationSpeed:F1}°/s");
                    if (aimed != null && aimed.name.ToLower().Contains("rotating"))
                        LaserRotationSpeeds[aimed] = ActiveLaserRotationSpeed;
                }
                else if (CurrentAsset != null && CurrentAsset.IsTurret)
                {
                    ActiveTurretFireDelay = Mathf.Max(0.05f, ActiveTurretFireDelay - stepDir * 0.1f);
                    MelonLogger.Msg($">> [Turret Fire Delay] Set: {ActiveTurretFireDelay:F2}s");
                    if (aimed != null && (TurretFireDelays.ContainsKey(aimed) || aimed.name.ToLower().Contains("turret")))
                        ApplyTurretSettings(aimed, ActiveTurretFireDelay, 1500f);
                }
                else
                {
                    ActivePlacementScale = Mathf.Clamp(ActivePlacementScale + stepDir * 0.05f, 0.01f, 50.0f);
                    PlacementHologramController.ApplyScaleToPreview();
                }
            }
            else
            {
                var list = ActiveTabAssets;
                if (list.Count > 0)
                {
                    int step = (scroll < 0) ? 1 : -1;
                    SelectedAssetIndex = (SelectedAssetIndex + step + list.Count) % list.Count;
                    CarouselWheelToolbar.SetTargetIndex(SelectedAssetIndex);
                    SelectCurrentAsset();
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
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, mask, QueryTriggerInteraction.Collide))
            {
                GameObject hitObj = hit.collider.gameObject;
                for (int i = 0; i < PlacedObjects.Count; i++)
                {
                    GameObject obj = PlacedObjects[i];
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
                MelonLogger.Msg($">> [Delete] Removed '{target.name}'. (Press Ctrl+Z to Undo)");
            }
        }

        public static void RegisterPlacedObject(GameObject obj)
        {
            if (obj == null || PlacedObjects.Contains(obj)) return;

            PlacedObjects.Add(obj);
            string low = obj.name.ToLower();
            PlacedObjectType identifiedType = PlacedObjectType.Generic;

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
            }
            else if (low.Contains("laser"))
            {
                identifiedType = PlacedObjectType.Laser;
                if (!PlacedLaserBarriers.Contains(obj))
                {
                    PlacedLaserBarriers.Add(obj);
                    BoxCollider bc = obj.GetComponentInChildren<BoxCollider>();
                    PlacedLaserColliders.Add(bc);
                }
            }
            else
            {
                CheckPointScript cp = obj.GetComponentInChildren<CheckPointScript>();
                if (cp != null)
                {
                    identifiedType = PlacedObjectType.Checkpoint;
                    if (!PlacedCheckpoints.Contains(cp)) PlacedCheckpoints.Add(cp);
                }
            }

            PlacedObjectTypes[obj] = identifiedType;

            if (obj.transform.parent != null)
            {
                RecalculateParentChildCount(obj.transform.parent.gameObject);
            }

            RecalculateVoidDeathY();
        }

        public static void RemovePlacedObjectFromTracking(GameObject target)
        {
            if (target == null) return;

            PlacedObjects.Remove(target);
            PlacedObjectTypes.Remove(target);
            PlacedParentChildCounts.Remove(target);

            if (target.transform.parent != null)
            {
                RecalculateParentChildCount(target.transform.parent.gameObject);
            }

            if (PlacedLights.ContainsKey(target)) PlacedLights.Remove(target);
            if (MotionPaths.ContainsKey(target)) MotionPaths.Remove(target);
            if (PathEditTarget == target) PathEditTarget = null;
            if (ParentingChildTarget == target) ParentingChildTarget = null;
            if (RepositionTarget == target) RepositionTarget = null;
            if (SelectedLightObject == target) SelectedLightObject = null;

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
                            if ((record.AssetName.ToLower().Contains("spotlight") || record.AssetName.ToLower().Contains("sunlight")) && PlacedLights.ContainsKey(recreated))
                            {
                                PlacedLights[recreated].Intensity = record.CustomParameter;
                                ApplyLightConfig(recreated, PlacedLights[recreated]);
                            }
                        }
                        RegisterPlacedObject(recreated);
                    }
                }
                RedoHistory.Push(record);
                ShowNotification($"Restored deleted {record.AssetName}");
            }
            else if (record.ActionType == HistoryActionType.Parenting)
            {
                if (record.TargetObject != null)
                {
                    GameObject oldP = record.PreviousParent;
                    GameObject newP = record.TargetObject.transform.parent != null ? record.TargetObject.transform.parent.gameObject : null;
                    record.TargetObject.transform.SetParent(oldP != null ? oldP.transform : null, true);
                    if (oldP != null) RecalculateParentChildCount(oldP);
                    if (newP != null) RecalculateParentChildCount(newP);
                }
                RedoHistory.Push(record);
                ShowNotification($"Undid parenting on '{record.TargetObject.name}'");
            }
            else if (record.ActionType == HistoryActionType.MotionPath)
            {
                if (record.TargetObject != null)
                {
                    if (record.PreviousMotionPath != null)
                        MotionPaths[record.TargetObject] = record.PreviousMotionPath.Clone();
                    else
                        MotionPaths.Remove(record.TargetObject);
                }
                RedoHistory.Push(record);
                ShowNotification($"Undid motion path on '{record.TargetObject.name}'");
            }
            else if (record.ActionType == HistoryActionType.Reposition)
            {
                if (record.TargetObject != null)
                {
                    Vector3 delta = record.PreviousPosition - record.TargetObject.transform.position;
                    record.TargetObject.transform.position = record.PreviousPosition;

                    if (MotionPaths.ContainsKey(record.TargetObject))
                    {
                        MotionPaths[record.TargetObject].PointA += delta;
                        MotionPaths[record.TargetObject].PointB += delta;
                    }
                }
                RedoHistory.Push(record);
                ShowNotification($"Undid movement on '{record.TargetObject.name}'");
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
                    GameObject oldP = record.TargetObject.transform.parent != null ? record.TargetObject.transform.parent.gameObject : null;
                    GameObject newP = record.NewParent;
                    record.TargetObject.transform.SetParent(newP != null ? newP.transform : null, true);
                    if (oldP != null) RecalculateParentChildCount(oldP);
                    if (newP != null) RecalculateParentChildCount(newP);
                }
                UndoHistory.Push(record);
                ShowNotification($"Redid parenting on '{record.TargetObject.name}'");
            }
            else if (record.ActionType == HistoryActionType.MotionPath)
            {
                if (record.TargetObject != null)
                {
                    if (record.NewMotionPath != null)
                        MotionPaths[record.TargetObject] = record.NewMotionPath.Clone();
                    else
                        MotionPaths.Remove(record.TargetObject);
                }
                UndoHistory.Push(record);
                ShowNotification($"Redid motion path on '{record.TargetObject.name}'");
            }
            else if (record.ActionType == HistoryActionType.Reposition)
            {
                if (record.TargetObject != null)
                {
                    Vector3 delta = record.NewPosition - record.TargetObject.transform.position;
                    record.TargetObject.transform.position = record.NewPosition;

                    if (MotionPaths.ContainsKey(record.TargetObject))
                    {
                        MotionPaths[record.TargetObject].PointA += delta;
                        MotionPaths[record.TargetObject].PointB += delta;
                    }
                }
                UndoHistory.Push(record);
                ShowNotification($"Redid movement on '{record.TargetObject.name}'");
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
            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                if (PlacedObjects[i] != null) GameObject.Destroy(PlacedObjects[i]);
            }
            PlacedObjects.Clear();
            PlacedObjectTypes.Clear();
            PlacedParentChildCounts.Clear();
            PlacedLights.Clear();
            SelectedLightObject = null;
            RepositionTarget = null;
            LastPlacedObject = null;

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
                _cachedHelixScripts[turbineObj] = h;
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

        /*
         * HDRP-Compliant Light Coordinator
         * Adapts to Unity HDRP lighting pipeline while avoiding legacy non-functional ambient mode overrides.
         */
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
                    Light targetSun = SceneHarvestingService.NativeSceneSun != null
                        ? SceneHarvestingService.NativeSceneSun
                        : l;

                    if (targetSun != null)
                    {
                        targetSun.gameObject.SetActive(true);
                        targetSun.enabled = true;
                        targetSun.type = LightType.Directional;
                        targetSun.color = cfg.Color;
                        targetSun.transform.rotation = lightObj.transform.rotation;

                        // DeadCore Redux native sun runs at high physical Lux values in HDRP
                        float hdrpSunIntensity = Mathf.Max(0.1f, cfg.Intensity) * 4000f;
                        targetSun.intensity = hdrpSunIntensity;
                        RenderSettings.sun = targetSun;

                        try
                        {
                            Component[] sunComps = targetSun.GetComponentsInChildren<Component>(true);
                            for (int i = 0; i < sunComps.Length; i++)
                            {
                                if (sunComps[i] != null && sunComps[i].GetIl2CppType().Name.Contains("HDAdditionalLightData"))
                                {
                                    var prop = sunComps[i].GetIl2CppType().GetProperty("intensity");
                                    if (prop != null) prop.SetValue(sunComps[i], hdrpSunIntensity);
                                }
                            }
                        }
                        catch { }
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

            try
            {
                Component[] comps = lightObj.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] != null && comps[i].GetIl2CppType().Name.Contains("HDAdditionalLightData"))
                    {
                        var t = comps[i].GetIl2CppType();

                        var rangeProp = t.GetProperty("range");
                        if (rangeProp != null && !cfg.IsDirectional) rangeProp.SetValue(comps[i], 150f);

                        var intProp = t.GetProperty("intensity");
                        if (intProp != null)
                        {
                            float actualInt = cfg.IsDirectional
                                ? (Mathf.Max(0.1f, cfg.Intensity) * 4000f)
                                : (Mathf.Pow(Mathf.Max(0.1f, cfg.Intensity), 2.2f) * 8000f);
                            intProp.SetValue(comps[i], actualInt);
                        }

                        if (!cfg.IsDirectional)
                        {
                            var volDimProp = t.GetProperty("volumetricDimmer");
                            if (volDimProp != null) volDimProp.SetValue(comps[i], cfg.VolumetricIntensity);

                            var useVolProp = t.GetProperty("useVolumetric");
                            if (useVolProp != null) useVolProp.SetValue(comps[i], cfg.VolumetricIntensity > 0.01f);
                        }
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
                        mr.material.SetColor("_EmissionColor", cfg.Color * Mathf.Max(2.0f, cfg.Intensity * 0.8f));
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
                    l.spotAngle = cfg.SpotAngle + 0.1f;
                    l.enabled = false;
                }

                ApplyLightConfig(obj, cfg);
            }

            MelonLogger.Msg($">> [Lights] Automatically synced {PlacedLights.Count} lights!");
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

        /*
         * Note: IMGUI (OnGUI) is currently utilized as an isolated developer/debug HUD.
         * Production migration will target native Unity Canvas/TMP interfaces.
         */
        public static void DrawEditorGUI()
        {
            Camera cam = EditorViewportCamera.ViewportCamera;
            if (cam == null) return;

            Color originalColor = GUI.color;
            float sw = Screen.width;
            float sh = Screen.height;

            for (int o = 0; o < PlacedObjects.Count; o++)
            {
                GameObject obj = PlacedObjects[o];
                if (obj == null || !obj.activeSelf) continue;

                Vector3 screenPos = cam.WorldToScreenPoint(obj.transform.position + Vector3.up * 1.5f);
                if (screenPos.z <= 0.1f || screenPos.x < -120f || screenPos.x > sw + 120f || screenPos.y < -80f || screenPos.y > sh + 80f)
                    continue;

                float y = sh - screenPos.y;

                if (MotionPaths.ContainsKey(obj))
                {
                    var mp = MotionPaths[obj];
                    GUI.color = new Color(0.9f, 0.3f, 1f);
                    GUI.Box(new Rect(screenPos.x - 90f, y - 30f, 180f, 26f), $"[✦ Path: {mp.Speed:F1}m/s]");

                    Vector3 screenA = cam.WorldToScreenPoint(mp.PointA);
                    if (screenA.z > 0.2f)
                    {
                        GUI.color = Color.cyan;
                        GUI.Label(new Rect(screenA.x - 30f, sh - screenA.y - 10f, 60f, 20f), "<b>[Start A]</b>");
                    }
                    Vector3 screenB = cam.WorldToScreenPoint(mp.PointB);
                    if (screenB.z > 0.2f)
                    {
                        GUI.color = Color.magenta;
                        GUI.Label(new Rect(screenB.x - 30f, sh - screenB.y - 10f, 60f, 20f), "<b>[End B]</b>");
                    }
                }

                if (PlacedParentChildCounts.TryGetValue(obj, out int placedChildCount) && placedChildCount > 0)
                {
                    GUI.color = new Color(1.0f, 0.7f, 0.2f);
                    GUI.Box(new Rect(screenPos.x - 95f, y - 55f, 190f, 22f), $"★ Assembly Parent ({placedChildCount} Objects)");
                }

                PlacedObjectTypes.TryGetValue(obj, out PlacedObjectType objType);

                switch (objType)
                {
                    case PlacedObjectType.Jumper:
                        {
                            float force = JumperForces.ContainsKey(obj) ? JumperForces[obj] : ActiveJumperForce;
                            GUI.color = Color.cyan;
                            GUI.Box(new Rect(screenPos.x - 75f, y - 14f, 150f, 26f), $"[Force: {force:F1}]");
                            break;
                        }
                    case PlacedObjectType.Turbine:
                        {
                            float speed = TurbineSpeeds.ContainsKey(obj) ? TurbineSpeeds[obj] : ActiveTurbineSpeed;
                            GUI.color = Color.green;
                            GUI.Box(new Rect(screenPos.x - 75f, y - 14f, 150f, 26f), $"[Speed: {speed:F1}]");
                            break;
                        }
                    case PlacedObjectType.Turret:
                        {
                            float delay = TurretFireDelays.ContainsKey(obj) ? TurretFireDelays[obj] : ActiveTurretFireDelay;
                            GUI.color = Color.red;
                            GUI.Box(new Rect(screenPos.x - 75f, y - 14f, 150f, 26f), $"[Fire Delay: {delay:F2}s]");
                            break;
                        }
                    case PlacedObjectType.SpawnGate:
                        {
                            GUI.color = new Color(1.0f, 0.45f, 0.05f);
                            GUI.Box(new Rect(screenPos.x - 85f, y - 14f, 170f, 26f), "[Entry / Spawn Point]");
                            break;
                        }
                    case PlacedObjectType.GoalGate:
                        {
                            GUI.color = new Color(0.1f, 0.65f, 1.0f);
                            GUI.Box(new Rect(screenPos.x - 85f, y - 14f, 170f, 26f), "[Goal / Finish Line]");
                            break;
                        }
                    case PlacedObjectType.Sunlight:
                        {
                            bool isSel = (obj == SelectedLightObject);
                            GUI.color = isSel ? Color.green : new Color(1.0f, 0.85f, 0.2f);
                            string badge = isSel ? "★ [Aim Sunlight: Arrows] ★" : "[Global Sunlight Source]";
                            GUI.Box(new Rect(screenPos.x - 100f, y - 14f, 200f, 26f), badge);
                            break;
                        }
                    case PlacedObjectType.Spotlight:
                        {
                            bool isSel = (obj == SelectedLightObject);
                            GUI.color = isSel ? Color.green : Color.yellow;
                            string badge = isSel ? "★ [Aim Spotlight: Arrows] ★" : "[Tech Spotlight]";
                            GUI.Box(new Rect(screenPos.x - 100f, y - 14f, 200f, 26f), badge);
                            break;
                        }
                    case PlacedObjectType.RotatingLaser:
                        {
                            float spd = LaserRotationSpeeds.ContainsKey(obj) ? LaserRotationSpeeds[obj] : ActiveLaserRotationSpeed;
                            GUI.color = Color.red;
                            GUI.Box(new Rect(screenPos.x - 85f, y - 14f, 170f, 26f), $"[Laser: {spd:F0}°/s]");
                            break;
                        }
                    case PlacedObjectType.Laser:
                        {
                            GUI.color = Color.red;
                            string label = obj.name.ToLower().Contains("long") ? "[Long Laser]" : "[Laser Barrier]";
                            GUI.Box(new Rect(screenPos.x - 85f, y - 14f, 170f, 26f), label);
                            break;
                        }
                    case PlacedObjectType.Checkpoint:
                        {
                            CheckPointScript cp = obj.GetComponentInChildren<CheckPointScript>();
                            bool isActive = (ActiveCustomCheckpoint != null && ActiveCustomCheckpoint == cp);
                            GUI.color = isActive ? Color.green : new Color(0.4f, 0.8f, 1f);
                            string cpText = isActive ? "[Active Checkpoint]" : "[Checkpoint]";
                            GUI.Box(new Rect(screenPos.x - 75f, y - 14f, 150f, 26f), cpText);
                            break;
                        }
                    default:
                        break;
                }
            }

            if (PathEditTarget != null)
            {
                Vector3 dest = GetAimedWorldPosition();
                Vector3 screenPos = cam.WorldToScreenPoint(dest);
                if (screenPos.z > 0.5f)
                {
                    float y = Screen.height - screenPos.y;
                    GUI.color = Color.magenta;
                    GUI.Box(new Rect(screenPos.x - 110f, y - 16f, 220f, 32f), "<b>[ TARGET POINT B (Press M) ]</b>");
                }
            }

            if (ParentingChildTarget != null)
            {
                Vector3 worldPos = ParentingChildTarget.transform.position + Vector3.up * 2.5f;
                Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                if (screenPos.z > 0.5f)
                {
                    float y = Screen.height - screenPos.y;
                    GUI.color = Color.yellow;
                    GUI.Box(new Rect(screenPos.x - 125f, y - 16f, 250f, 32f), "<b>[ AIM AT PARENT & PRESS P ]</b>");
                }
            }

            if (RepositionTarget != null)
            {
                Vector3 screenPos = cam.WorldToScreenPoint(RepositionTarget.transform.position + Vector3.up * 2.0f);
                if (screenPos.z > 0.5f)
                {
                    float y = Screen.height - screenPos.y;
                    GUI.color = new Color(0.2f, 1f, 0.4f);
                    GUI.Box(new Rect(screenPos.x - 160f, y - 16f, 320f, 32f), "<b>[ MOVING ENTITY: LEFT-CLICK TO DROP ]</b>");
                }
            }

            if (SelectedLightObject != null && SelectedLightObject.activeSelf && !IsBlockSelected)
            {
                GUI.color = new Color(1f, 0.88f, 0.2f, 0.95f);
                GUI.Box(new Rect(Screen.width * 0.5f - 275f, Screen.height - 75f, 550f, 28f),
                    "★ AIMING LIGHT: Arrow Keys (Pan/Tilt) | [ ] (Roll) | R (Reset) | Backspace (Deselect)");
            }

            float barW = 460f;
            float barH = 34f;
            float barX = (Screen.width - barW) * 0.5f;
            float barY = 14f;

            GUI.color = new Color(0.04f, 0.08f, 0.12f, 0.92f);
            GUI.Box(new Rect(barX, barY, barW, barH), "");

            string[] catLabels = new string[] { "1: BUILDING", "2: GAMEPLAY" };
            float tabBtnW = (barW - 14f) / 2f;

            for (int i = 0; i < 2; i++)
            {
                AssetCategory c = (AssetCategory)i;
                bool isCurrent = (CurrentTab == c);
                GUI.color = isCurrent ? new Color(0.2f, 0.85f, 1f, 1f) : new Color(0.22f, 0.28f, 0.36f, 0.85f);

                int count = (c == AssetCategory.Building) ? BuildingAssets.Count : GameplayAssets.Count;
                if (GUI.Button(new Rect(barX + 6f + (i * tabBtnW), barY + 4f, tabBtnW - 4f, 26f), $"{catLabels[i]} ({count})"))
                {
                    SetCategory(c);
                }
            }

            string gridName = (CurrentGridSnap > 0.01f) ? $"{CurrentGridSnap}m" : "OFF";
            string alignMode = AutoAlignToSurface ? "<color=#69F0AE>SURFACE (C)</color>" : "<color=#FFB74D>MANUAL (C)</color>";
            string pathInfo = PathEditTarget != null ? "<color=#E040FB>SETTING POINT B (M)</color>" : "M: Path";
            string parentInfo = ParentingChildTarget != null ? "<color=#FFD54F>LINKING PARENT (P)</color>" : "P: Parent";
            string moveInfo = RepositionTarget != null ? "<color=#00E676>MOVING (V/Backspace)</color>" : "V: Move";

            string status = IsBlockSelected
                ? $"EQUIPPED: '{CurrentAsset?.DisplayName}' | Left-Click: Place | RMB/Backspace: Cancel | {moveInfo} | {pathInfo} | {parentInfo} | Align: {alignMode} | Snap: {gridName} (G)"
                : $"MAP: '{MapBrowserService.SelectedMapName}' | Tab/1-2: Tabs | {moveInfo} | {pathInfo} | {parentInfo} | C: Align | Ctrl+Z: Undo | Ctrl+Y: Redo";

            GUI.color = Color.white;
            GUI.Box(new Rect(Screen.width * 0.5f - 520f, Screen.height - 40f, 1040f, 28f), status);

            if (_notificationTimer > 0f)
            {
                GUI.color = new Color(0.2f, 1f, 0.4f, Mathf.Clamp01(_notificationTimer));
                GUI.Box(new Rect(Screen.width * 0.5f - 220f, Screen.height - 110f, 440f, 32f), _notificationMessage);
            }

            if (ShowParamsWindow)
            {
                DrawParametersWindow();
            }

            if (ShowDebugOverlay)
            {
                DrawDebugOverlay();
            }

            GUI.color = originalColor;
        }

        private static void DrawDebugOverlay()
        {
            _debugFpsTimer -= Time.deltaTime;
            if (_debugFpsTimer <= 0f)
            {
                _debugFps = 1.0f / Mathf.Max(0.0001f, Time.deltaTime);
                _debugFpsTimer = 0.2f;
            }

            float winW = 320f;
            float winH = 260f;
            Rect winRect = new Rect(20f, 20f, winW, winH);

            GUI.color = new Color(0.02f, 0.04f, 0.08f, 0.92f);
            GUI.Box(winRect, "");
            GUI.color = new Color(0.1f, 0.8f, 0.4f, 0.9f);
            GUI.Box(new Rect(winRect.x + 2, winRect.y + 2, winW - 4, winH - 4), "");

            GUI.color = Color.green;
            GUI.Label(new Rect(32f, 28f, 290f, 24f), "<b>DIAGNOSTIC ENGINE TELEMETRY (F7)</b>");

            GUI.color = Color.white;
            float curY = 56f;

            string fpsCol = _debugFps >= 55f ? "#00E676" : (_debugFps >= 30f ? "#FFEA00" : "#FF1744");
            GUI.Label(new Rect(32f, curY, 290f, 20f), $"FPS: <b><color={fpsCol}>{_debugFps:F1}</color></b> (Delta: {Time.deltaTime * 1000f:F1}ms)");
            curY += 22f;

            long allocatedBytes = GC.GetTotalMemory(false) / (1024 * 1024);
            GUI.Label(new Rect(32f, curY, 290f, 20f), $"Mono Heap Usage: <b><color=#00E5FF>{allocatedBytes} MB</color></b>");
            curY += 22f;

            GUI.Label(new Rect(32f, curY, 290f, 20f), $"Total Placed Entities: <b><color=#FFD54F>{PlacedObjects.Count}</color></b>");
            curY += 22f;

            GUI.Label(new Rect(32f, curY, 290f, 20f), $"Active Motion Paths: <b><color=#E040FB>{MotionPaths.Count}</color></b>");
            curY += 22f;

            Vector3 hitPos = GetAimedWorldPosition();
            GUI.Label(new Rect(32f, curY, 290f, 20f), $"Raycast Pos: <b><color=#80D8FF>{hitPos.x:F1}, {hitPos.y:F1}, {hitPos.z:F1}</color></b>");
            curY += 22f;

            GameObject aimed = GetAimedPlacedObject();
            string aimedName = aimed != null ? aimed.name : "None";
            GUI.Label(new Rect(32f, curY, 290f, 20f), $"Cursor Target: <b><color=#FF8A80>{aimedName}</color></b>");
            curY += 22f;

            string nativeSun = SceneHarvestingService.NativeSceneSun != null ? SceneHarvestingService.NativeSceneSun.name : "Unbound";
            GUI.Label(new Rect(32f, curY, 290f, 20f), $"HDRP Sun Hook: <b><color=#FFE57F>{nativeSun}</color></b>");
        }

        private static void DrawParametersWindow()
        {
            float winW = 340f;
            float winH = 490f;
            float winX = Screen.width - winW - 20f;
            float winY = 20f;
            Rect winRect = new Rect(winX, winY, winW, winH);

            Color orig = GUI.color;

            GUI.color = new Color(0.04f, 0.07f, 0.12f, 0.95f);
            GUI.Box(winRect, "");
            GUI.color = new Color(0.12f, 0.65f, 0.95f, 0.85f);
            GUI.Box(new Rect(winX + 2, winY + 2, winW - 4, winH - 4), "");

            GUI.color = new Color(0.08f, 0.14f, 0.22f, 1f);
            GUI.Box(new Rect(winX + 8, winY + 8, winW - 16, 32), "");
            GUI.color = Color.yellow;
            GUI.Label(new Rect(winX + 20, winY + 14, 280, 25), "LIGHT & ATMOSPHERE (F3)");

            float curY = winY + 46f;

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
                    IsBlockSelected = false;
                    PlacementHologramController.DestroyPreview();
                }

                GUI.color = Color.white;
                GUI.Label(new Rect(winX + 62, curY + 3, 120, 20), lightLabel);

                GUI.color = new Color(0.2f, 0.55f, 0.85f);
                if (GUI.Button(new Rect(winX + 185, curY, 40, 24), ">"))
                {
                    curIdx = (curIdx + 1) % allLights.Count;
                    SelectedLightObject = allLights[curIdx];
                    IsBlockSelected = false;
                    PlacementHologramController.DestroyPreview();
                }

                GUI.color = new Color(0.8f, 0.3f, 0.3f);
                if (GUI.Button(new Rect(winX + 235, curY, 80, 24), "Deselect"))
                {
                    SelectedLightObject = null;
                }
                curY += 32f;
            }

            if (SelectedLightObject != null && PlacedLights.ContainsKey(SelectedLightObject))
            {
                LightConfig cfg = PlacedLights[SelectedLightObject];
                bool changed = false;

                GUI.color = new Color(1f, 0.9f, 0.3f);
                string title = cfg.IsDirectional ? "[GLOBAL SUNLIGHT SOURCE]" : "[TECH SPOTLIGHT]";
                GUI.Label(new Rect(winX + 15, curY, winW - 30, 18), $"{title} Aim with Arrows | [ ] Roll");
                curY += 22f;

                GUI.color = Color.white;
                float displayLux = cfg.IsDirectional ? (cfg.Intensity * 4000f) : cfg.Intensity;
                string unitLabel = cfg.IsDirectional ? $"{displayLux:F0} Lux" : $"{cfg.Intensity:F1}x";
                GUI.Label(new Rect(winX + 15, curY, 200, 18), $"Light Intensity: <b><color=#00E5FF>{unitLabel}</color></b>");
                curY += 18f;

                float newInt = GUI.HorizontalSlider(new Rect(winX + 15, curY, winW - 30, 16), cfg.Intensity, 0.1f, 30.0f);
                if (Mathf.Abs(newInt - cfg.Intensity) > 0.05f)
                {
                    cfg.Intensity = newInt;
                    changed = true;
                }
                curY += 22f;

                if (!cfg.IsDirectional)
                {
                    GUI.Label(new Rect(winX + 15, curY, 200, 18), $"Cone Angle: <b><color=#FFEB3B>{cfg.SpotAngle:F0}°</color></b>");
                    curY += 18f;
                    float newAngle = GUI.HorizontalSlider(new Rect(winX + 15, curY, winW - 30, 16), cfg.SpotAngle, 10f, 150f);
                    if (Mathf.Abs(newAngle - cfg.SpotAngle) > 0.5f)
                    {
                        cfg.SpotAngle = newAngle;
                        changed = true;
                    }
                    curY += 22f;
                }
                else
                {
                    GUI.color = Color.gray;
                    GUI.Label(new Rect(winX + 15, curY, 280, 18), "Cone Angle: [INFINITE / GLOBAL SUN]");
                    curY += 36f;
                }

                if (!cfg.IsDirectional)
                {
                    GUI.color = new Color(0.6f, 0.9f, 1f);
                    GUI.Label(new Rect(winX + 15, curY, 240, 18), $"Volumetric Intensity: <b><color=#E040FB>{cfg.VolumetricIntensity:F1}x</color></b>");
                    curY += 18f;
                    float newVol = GUI.HorizontalSlider(new Rect(winX + 15, curY, winW - 30, 16), cfg.VolumetricIntensity, 0.0f, 10.0f);
                    if (Mathf.Abs(newVol - cfg.VolumetricIntensity) > 0.05f)
                    {
                        cfg.VolumetricIntensity = newVol;
                        changed = true;
                    }
                    curY += 26f;
                }
                else
                {
                    GUI.color = Color.gray;
                    GUI.Label(new Rect(winX + 15, curY, 280, 18), "Volumetric Fog: [NATIVE SKY SYSTEM]");
                    curY += 24f;
                }

                GUI.color = Color.white;
                GUI.Label(new Rect(winX + 15, curY, 180, 18), "Light Color (RGB Picker):");

                Color oldGuiCol = GUI.color;
                GUI.color = cfg.Color;
                GUI.Box(new Rect(winX + winW - 65f, curY - 2f, 50f, 22f), "");
                GUI.color = oldGuiCol;
                curY += 24f;

                GUI.color = new Color(1f, 0.3f, 0.3f);
                GUI.Label(new Rect(winX + 15, curY, 60, 16), $"R: {cfg.Color.r:F2}");
                float r = GUI.HorizontalSlider(new Rect(winX + 75, curY + 2, winW - 95, 14), cfg.Color.r, 0f, 1f);
                curY += 18f;

                GUI.color = new Color(0.3f, 1f, 0.4f);
                GUI.Label(new Rect(winX + 15, curY, 60, 16), $"G: {cfg.Color.g:F2}");
                float g = GUI.HorizontalSlider(new Rect(winX + 75, curY + 2, winW - 95, 14), cfg.Color.g, 0f, 1f);
                curY += 18f;

                GUI.color = new Color(0.3f, 0.7f, 1f);
                GUI.Label(new Rect(winX + 15, curY, 60, 16), $"B: {cfg.Color.b:F2}");
                float b = GUI.HorizontalSlider(new Rect(winX + 75, curY + 2, winW - 95, 14), cfg.Color.b, 0f, 1f);
                curY += 24f;

                if (Mathf.Abs(r - cfg.Color.r) > 0.01f || Mathf.Abs(g - cfg.Color.g) > 0.01f || Mathf.Abs(b - cfg.Color.b) > 0.01f)
                {
                    cfg.Color = new Color(r, g, b, 1f);
                    changed = true;
                }

                float cW = (winW - 50) / 5f;
                GUI.color = Color.cyan;
                if (GUI.Button(new Rect(winX + 15, curY, cW, 20), "Cyan")) { cfg.Color = Color.cyan; changed = true; }
                GUI.color = new Color(1f, 0.75f, 0.3f);
                if (GUI.Button(new Rect(winX + 15 + cW + 4, curY, cW, 20), "Sun")) { cfg.Color = new Color(1f, 0.75f, 0.3f); changed = true; }
                GUI.color = new Color(0.2f, 1f, 0.35f);
                if (GUI.Button(new Rect(winX + 15 + (cW + 4) * 2, curY, cW, 20), "Green")) { cfg.Color = Color.green; changed = true; }
                GUI.color = new Color(1f, 0.25f, 0.25f);
                if (GUI.Button(new Rect(winX + 15 + (cW + 4) * 3, curY, cW, 20), "Red")) { cfg.Color = Color.red; changed = true; }
                GUI.color = Color.white;
                if (GUI.Button(new Rect(winX + 15 + (cW + 4) * 4, curY, cW, 20), "White")) { cfg.Color = Color.white; changed = true; }
                curY += 28f;

                GUI.color = new Color(0.2f, 0.65f, 0.95f);
                string resetLabel = cfg.IsDirectional ? "Reset Sun Angle (50°, -30°, 0°)" : "Reset Aim (0°, 0°, 0°)";
                if (GUI.Button(new Rect(winX + 15, curY, winW - 30, 24), resetLabel))
                {
                    SelectedLightObject.transform.rotation = cfg.IsDirectional
                        ? Quaternion.Euler(50f, -30f, 0f)
                        : Quaternion.identity;
                    ApplyLightConfig(SelectedLightObject, cfg);
                }

                if (changed)
                {
                    ApplyLightConfig(SelectedLightObject, cfg);
                }
            }
            else
            {
                GUI.color = Color.gray;
                GUI.Label(new Rect(winX + 15, curY + 25, 290, 80), allLights.Count > 0
                    ? "Click '<' or '>' above to select a light,\nor click any Tech Spotlight or Sunlight in the world."
                    : "No lights placed yet.\nSelect 'Global Sunlight' or 'Tech Spotlight' from Gameplay to place one.");
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
                    MelonLogger.Msg($">> Set player start position to custom Entry Checkpoint at {startPos}!");
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
                    MelonLogger.Msg($">> [SafeSpawn] Spawned solid floor platform directly under player at {p0Pos}!");
                }
            }

            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                if (PlacedObjects[i] != null)
                {
                    AttachEditorSnappingProxy(PlacedObjects[i]);
                }
            }

            for (int i = 0; i < PlacedObjects.Count; i++)
            {
                GameObject obj = PlacedObjects[i];
                if (obj != null && obj.activeSelf && PlacedObjectTypes.TryGetValue(obj, out var t) && t == PlacedObjectType.Turret)
                {
                    float delay = TurretFireDelays.ContainsKey(obj) ? TurretFireDelays[obj] : ActiveTurretFireDelay;
                    ApplyTurretSettings(obj, delay, 1500f);
                }
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
                else if (clean.Contains("long") && clean.Contains("laser")) found = AllAssets.Find(a => a.DisplayName.ToLower().Contains("long laser"));
                else if (clean.Contains("laser") || clean.Contains("barrier")) found = AllAssets.Find(a => a.IsLaser && !a.IsRotatingLaser);
                else if (clean.Contains("platform") || clean.Contains("floor") || clean.Contains("plateforme") || clean.Contains("16x16") || clean.Contains("16x2x16"))
                    found = AllAssets.Find(a => a.DisplayName.ToLower().Contains("platform") || a.DisplayName.ToLower().Contains("floor") || (a.FilterMesh != null && a.FilterMesh.name.ToLower().Contains("16x2x16")));
                else if (clean.Contains("jumper") || clean.Contains("launch")) found = AllAssets.Find(a => a.IsJumper);
                else if (clean.Contains("checkpoint")) found = AllAssets.Find(a => a.IsCheckPoint && !a.IsSpawnGate && !a.IsGoalGate);
                else if (clean.Contains("turret") || clean.Contains("defense")) found = AllAssets.Find(a => a.IsTurret);
                else if (clean.Contains("helix") || clean.Contains("turbine") || clean.Contains("fan")) found = AllAssets.Find(a => a.IsHelix);
                else if (clean.Contains("crate")) found = AllAssets.Find(a => a.DisplayName.ToLower().Contains("crate"));
                else if (clean.Contains("pillar") || clean.Contains("monolith")) found = AllAssets.Find(a => a.DisplayName.ToLower().Contains("pillar") || a.DisplayName.ToLower().Contains("monolith"));
                else if (clean.Contains("gate") || clean.Contains("bar")) found = AllAssets.Find(a => a.Category == AssetCategory.Building && (a.DisplayName.ToLower().Contains("gate") || a.DisplayName.ToLower().Contains("bar") || (a.FilterMesh != null && a.FilterMesh.name.ToLower().Contains("gate"))));
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

            // 1. Visual tints & gate roles
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

            // 2. Universal Checkpoint Setup (Runs for Spawn, Normal Checkpoint, AND Goal Gate)
            if (asset.IsCheckPoint)
            {
                CheckPointScript cp = obj.GetComponentInChildren<CheckPointScript>();
                if (cp != null)
                {
                    // Spawn is 0, Goal is 9999, mid-checkpoints get indexed
                    cp._id = asset.IsSpawnGate ? 0 : (asset.IsGoalGate ? 9999 : (PlacedObjects.Count + 100));

                    // Guarantee _spawnPoint is NEVER null
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

            // 3. Other Gameplay Mechanics (Sun, Spotlights, Lasers, Turbines, Turrets, Jumpers)
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
            else if (asset.IsRotatingLaser)
            {
                obj.name = "Custom_Rotating_Laser_Barrier";
            }
            else if (asset.IsLaser)
            {
                obj.name = "Custom_Laser_Barrier";
            }

            if (asset.IsHelix) ApplyTurbineSpeed(obj, ActiveTurbineSpeed);
            if (asset.IsTurret) ApplyTurretSettings(obj, ActiveTurretFireDelay, 1500f);
            if (asset.IsJumper) ApplyJumperForce(obj, ActiveJumperForce);

            if (!asset.IsTurret && !asset.IsCheckPoint && !asset.IsHelix && !asset.IsSpawnGate && !asset.IsGoalGate && !asset.IsSpotlight && !asset.IsSunlight && !asset.IsLaser)
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

        public static GameObject FindPlayerEntity()
        {
            if (_cachedPlayer != null && _cachedPlayer.activeInHierarchy)
                return _cachedPlayer;

            _cachedPlayer = GameObject.FindWithTag("Player");
            if (_cachedPlayer == null)
            {
                CharacterController cc = GameObject.FindObjectOfType<CharacterController>();
                if (cc != null) _cachedPlayer = cc.transform.root.gameObject;
                else if (Camera.main != null) _cachedPlayer = Camera.main.transform.root.gameObject;
            }

            if (_cachedPlayer != null)
            {
                _cachedCharacterController = _cachedPlayer.GetComponentInChildren<CharacterController>();
            }

            return _cachedPlayer;
        }

        public static CharacterController GetPlayerController()
        {
            if (_cachedCharacterController != null && _cachedCharacterController.gameObject.activeInHierarchy)
                return _cachedCharacterController;

            FindPlayerEntity();
            return _cachedCharacterController;
        }
    }

    /*
     * Viewport Freecam Navigation Engine
     * Decouples the player controller during edit mode to enable full 6-DOF orbital and translational
     * navigation. Supports sprint multipliers, micro-speed crawls, and mouse freelook capture.
     */
    public static class EditorViewportCamera
    {
        private static GameObject _camInstance = null;
        public static Camera ViewportCamera = null;

        private static float _yaw = 0f;
        private static float _pitch = 0f;
        private static float _baseSpeed = 24f;

        private static float _rmbDownTime = 0f;
        private static Vector2 _rmbDownMousePos = Vector2.zero;

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

            if (Input.GetMouseButtonDown(1))
            {
                _rmbDownTime = Time.realtimeSinceStartup;
                _rmbDownMousePos = Input.mousePosition;
            }
            if (Input.GetMouseButtonUp(1))
            {
                float duration = Time.realtimeSinceStartup - _rmbDownTime;
                float dist = Vector2.Distance(Input.mousePosition, _rmbDownMousePos);

                if (duration < 0.25f && dist < 5f)
                {
                    if (EditorSessionManager.IsBlockSelected)
                    {
                        EditorSessionManager.IsBlockSelected = false;
                        PlacementHologramController.DestroyPreview();
                        EditorSessionManager.ShowNotification("Placement Cancelled");
                    }
                }
            }

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

    /*
     * Holographic Placement Engine
     * Projects a live preview of the currently equipped catalog asset into the viewport.
     */
    public static class PlacementHologramController
    {
        private static GameObject _ghostInstance = null;
        public static GameObject GhostInstance => _ghostInstance;

        private static BoxCollider _ghostBoxCollider = null;
        private static Vector3 _targetPosition = Vector3.zero;
        public static Vector3 TargetPosition => _targetPosition;

        private static Vector3 _currentSnappedNormal = Vector3.up;

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

            Bounds b = CalculateOptimizedProxyBounds(_ghostInstance);

            _ghostBoxCollider = _ghostInstance.AddComponent<BoxCollider>();
            _ghostBoxCollider.isTrigger = true;
            _ghostBoxCollider.center = b.center;
            _ghostBoxCollider.size = b.size;

            _ghostInstance.transform.localScale = Vector3.one * EditorSessionManager.ActivePlacementScale;

            ApplyRotationToPreview();
            _ghostInstance.SetActive(true);
        }

        public static Bounds CalculateOptimizedProxyBounds(GameObject go)
        {
            Bounds raw = new Bounds(Vector3.zero, Vector3.zero);
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
                        raw = transformedB;
                        hasBounds = true;
                    }
                    else
                    {
                        raw.Encapsulate(transformedB);
                    }
                }
            }

            if (!hasBounds) raw = new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f));

            Vector3 size = raw.size;
            size.x = Mathf.Max(0.5f, Mathf.Round(size.x * 2f) * 0.5f);
            size.y = Mathf.Max(0.5f, Mathf.Round(size.y * 2f) * 0.5f);
            size.z = Mathf.Max(0.5f, Mathf.Round(size.z * 2f) * 0.5f);

            Vector3 center = raw.center;
            center.x = Mathf.Round(center.x * 4f) * 0.25f;
            center.y = Mathf.Round(center.y * 4f) * 0.25f;
            center.z = Mathf.Round(center.z * 4f) * 0.25f;

            return new Bounds(center, size);
        }

        public static Vector3 SnapNormalToDiscreteAngles(Vector3 rawNormal)
        {
            if (rawNormal.sqrMagnitude < 0.01f) return Vector3.up;
            rawNormal.Normalize();

            if (rawNormal.y > 0.85f) return Vector3.up;
            if (rawNormal.y < -0.85f) return Vector3.down;
            if (Mathf.Abs(rawNormal.y) < 0.25f)
            {
                if (Mathf.Abs(rawNormal.x) > Mathf.Abs(rawNormal.z))
                    return new Vector3(Mathf.Sign(rawNormal.x), 0f, 0f);
                else
                    return new Vector3(0f, 0f, Mathf.Sign(rawNormal.z));
            }

            float signY = Mathf.Sign(rawNormal.y);
            float signX = Mathf.Sign(rawNormal.x);
            float signZ = Mathf.Sign(rawNormal.z);

            if (Mathf.Abs(rawNormal.x) > Mathf.Abs(rawNormal.z))
            {
                return new Vector3(signX * 0.7071f, signY * 0.7071f, 0f);
            }
            else
            {
                return new Vector3(0f, signY * 0.7071f, signZ * 0.7071f);
            }
        }

        public static Quaternion CalculateActiveRotation(CatalogAsset asset, Vector3 surfaceNormal)
        {
            if (EditorSessionManager.AutoAlignToSurface)
            {
                Quaternion alignRot = Quaternion.FromToRotation(Vector3.up, surfaceNormal);
                float snappedYaw = Mathf.Round(EditorSessionManager.TargetYaw / 45f) * 45f;
                Quaternion yawRot = Quaternion.AngleAxis(snappedYaw, surfaceNormal);

                Quaternion baseOffset = (asset != null) ? asset.BaseRotation : Quaternion.identity;
                return yawRot * alignRot * baseOffset;
            }

            return EditorSessionManager.GetCurrentCombinedRotation(asset);
        }

        public static void ApplyRotationToPreview()
        {
            if (_ghostInstance != null)
            {
                _ghostInstance.transform.rotation = CalculateActiveRotation(EditorSessionManager.CurrentAsset, _currentSnappedNormal);
            }
        }

        public static void ApplyScaleToPreview()
        {
            if (_ghostInstance != null)
            {
                _ghostInstance.transform.localScale = Vector3.one * EditorSessionManager.ActivePlacementScale;
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
            bool hasHit = Physics.Raycast(ray, out hit, 1000f, raycastMask, QueryTriggerInteraction.Collide);

            Vector3 hitNormal = Vector3.up;
            Vector3 rawTargetPos;
            Collider hitCollider = null;

            if (hasHit)
            {
                rawTargetPos = hit.point;
                hitNormal = hit.normal;
                hitCollider = hit.collider;
            }
            else
            {
                rawTargetPos = ray.origin + ray.direction * 15f;
                hitNormal = -ray.direction;
            }

            _currentSnappedNormal = SnapNormalToDiscreteAngles(hitNormal);
            Quaternion targetRot = CalculateActiveRotation(EditorSessionManager.CurrentAsset, _currentSnappedNormal);

            _targetPosition = CalculateProxySnappedPosition(
                rawTargetPos,
                targetRot,
                _ghostBoxCollider,
                _currentSnappedNormal,
                hasHit,
                hitCollider,
                EditorSessionManager.CurrentAsset
            );

            _ghostInstance.transform.position = Vector3.Lerp(_ghostInstance.transform.position, _targetPosition, Time.deltaTime * 35f);
            _ghostInstance.transform.rotation = Quaternion.Slerp(_ghostInstance.transform.rotation, targetRot, Time.deltaTime * 24f);

            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1))
            {
                CommitPlacement(targetRot);
            }
        }

        private static Vector3 CalculateProxySnappedPosition(
            Vector3 rawPos,
            Quaternion rot,
            BoxCollider proxyCol,
            Vector3 normal,
            bool hasHit,
            Collider hitCol,
            CatalogAsset asset)
        {
            if (proxyCol == null) return rawPos;

            Vector3 halfExtents = Vector3.Scale(proxyCol.size * 0.5f, proxyCol.transform.lossyScale);
            Vector3 centerOffset = Vector3.Scale(proxyCol.center, proxyCol.transform.lossyScale);

            float grid = EditorSessionManager.CurrentGridSnap;
            bool isGridActive = grid > 0.01f;

            Vector3 targetPos = rawPos;

            if (hasHit)
            {
                Vector3 uX = rot * Vector3.right;
                Vector3 uY = rot * Vector3.up;
                Vector3 uZ = rot * Vector3.forward;

                float extentAlongNormal = halfExtents.x * Mathf.Abs(Vector3.Dot(uX, normal))
                                        + halfExtents.y * Mathf.Abs(Vector3.Dot(uY, normal))
                                        + halfExtents.z * Mathf.Abs(Vector3.Dot(uZ, normal));

                float extraOffset = (asset != null) ? asset.VerticalOffset : 0f;

                Vector3 contactCenter = rawPos + normal * (extentAlongNormal + 0.001f + extraOffset);
                targetPos = contactCenter - (rot * centerOffset);

                if (isGridActive)
                {
                    targetPos = ApplyStrictGridSnap(targetPos, normal, grid);
                }

                Vector3 probeHalfExtents = halfExtents - Vector3.one * 0.03f;
                probeHalfExtents.x = Mathf.Max(0.04f, probeHalfExtents.x);
                probeHalfExtents.y = Mathf.Max(0.04f, probeHalfExtents.y);
                probeHalfExtents.z = Mathf.Max(0.04f, probeHalfExtents.z);

                Vector3 currentWorldCenter = targetPos + (rot * centerOffset);
                Collider[] overlaps = Physics.OverlapBox(currentWorldCenter, probeHalfExtents, rot, ~0, QueryTriggerInteraction.Collide);

                Transform hitRoot = (hitCol != null) ? hitCol.transform.root : null;
                Transform ghostRoot = _ghostInstance.transform;

                for (int i = 0; i < overlaps.Length; i++)
                {
                    Collider col = overlaps[i];
                    if (col == null || col == proxyCol) continue;
                    if (col.transform.root == ghostRoot) continue;
                    if (col.GetComponent<CharacterController>() != null) continue;

                    if (hitRoot != null && col.transform.root == hitRoot) continue;

                    bool isPlaced = col.name.StartsWith("Custom_") ||
                                    col.transform.root.name.StartsWith("Custom_") ||
                                    col.name == "Editor_Snapping_Proxy";

                    if (!isPlaced) continue;

                    if (Physics.ComputePenetration(
                        proxyCol, targetPos, rot,
                        col, col.transform.position, col.transform.rotation,
                        out Vector3 pushDir, out float pushDist))
                    {
                        if (pushDist > 0.03f)
                        {
                            targetPos += pushDir * pushDist;
                            break;
                        }
                    }
                }

                if (isGridActive)
                {
                    targetPos = ApplyStrictGridSnap(targetPos, normal, grid);
                }
            }
            else
            {
                if (isGridActive)
                {
                    targetPos = new Vector3(
                        Mathf.Round(targetPos.x / grid) * grid,
                        Mathf.Round(targetPos.y / grid) * grid,
                        Mathf.Round(targetPos.z / grid) * grid
                    );
                }
            }

            return targetPos;
        }

        private static Vector3 ApplyStrictGridSnap(Vector3 pos, Vector3 normal, float grid)
        {
            if (grid <= 0.01f) return pos;

            Vector3 snapped = pos;
            float depthStep = (grid > 0.5f) ? (grid * 0.5f) : grid;

            if (Mathf.Abs(normal.y) > 0.65f)
            {
                snapped.x = Mathf.Round(snapped.x / grid) * grid;
                snapped.z = Mathf.Round(snapped.z / grid) * grid;
                snapped.y = Mathf.Round(snapped.y / depthStep) * depthStep;
            }
            else if (Mathf.Abs(normal.x) > 0.65f)
            {
                snapped.y = Mathf.Round(snapped.y / grid) * grid;
                snapped.z = Mathf.Round(snapped.z / grid) * grid;
                snapped.x = Mathf.Round(snapped.x / depthStep) * depthStep;
            }
            else if (Mathf.Abs(normal.z) > 0.65f)
            {
                snapped.x = Mathf.Round(snapped.x / grid) * grid;
                snapped.y = Mathf.Round(snapped.y / depthStep) * depthStep;
                snapped.z = Mathf.Round(snapped.z / grid) * grid;
            }
            else
            {
                snapped.x = Mathf.Round(snapped.x / grid) * grid;
                snapped.y = Mathf.Round(snapped.y / depthStep) * depthStep;
                snapped.z = Mathf.Round(snapped.z / grid) * grid;
            }

            return snapped;
        }

        private static void CommitPlacement(Quaternion placementRotation)
        {
            CatalogAsset asset = EditorSessionManager.CurrentAsset;
            if (asset == null) return;

            float scale = EditorSessionManager.ActivePlacementScale;

            GameObject placed = EditorSessionManager.SpawnCatalogObject(asset, _targetPosition, scale, placementRotation);

            if (placed != null)
            {
                EditorSessionManager.RegisterPlacedObject(placed);
                EditorSessionManager.LastPlacedObject = placed;

                if (asset.IsSpotlight || asset.IsSunlight)
                {
                    EditorSessionManager.SelectedLightObject = placed;
                }

                float param = 0f;
                if (asset.IsJumper) param = EditorSessionManager.ActiveJumperForce;
                else if (asset.IsHelix) param = EditorSessionManager.ActiveTurbineSpeed;
                else if (asset.IsTurret) param = EditorSessionManager.ActiveTurretFireDelay;
                else if ((asset.IsSpotlight || asset.IsSunlight) && EditorSessionManager.PlacedLights.ContainsKey(placed)) param = EditorSessionManager.PlacedLights[placed].Intensity;

                EditorSessionManager.UndoHistory.Push(new HistoryRecord
                {
                    ActionType = HistoryActionType.Placement,
                    TargetObject = placed,
                    Asset = asset,
                    AssetName = asset.DisplayName,
                    Position = _targetPosition,
                    Rotation = placementRotation,
                    Scale = scale,
                    CustomParameter = param
                });
                EditorSessionManager.RedoHistory.Clear();

                MelonLogger.Msg($">> Placed '{asset.DisplayName}' at {_targetPosition} (Snap: {EditorSessionManager.CurrentGridSnap}m, Align: {EditorSessionManager.AutoAlignToSurface})");
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

    /*
     * 3D HUD Carousel Wheel
     * Projects a curved cylindrical asset tray into screen space.
     */
    public static class CarouselWheelToolbar
    {
        public static float ArcRadius = 0.75f;
        public static float IconScaleMultiplier = 0.026f;
        public static float ArcCenterY = -0.35f;
        public static float ArcDistanceZ = 0.96f;

        private static GameObject _wheelRoot = null;
        private static readonly List<GameObject> _spawnedIcons = new List<GameObject>();

        private static float _targetOffset = 0f;
        private static float _currentOffset = 0f;
        private static int _lastHighlightedIndex = -1;

        public static void CreateToolbar(Camera viewCam)
        {
            if (_wheelRoot != null || viewCam == null) return;

            _wheelRoot = new GameObject("Magical_Carousel_Arc");
            _wheelRoot.transform.SetParent(viewCam.transform, false);

            _wheelRoot.transform.localPosition = new Vector3(0f, ArcCenterY, ArcDistanceZ);
            _wheelRoot.transform.localRotation = Quaternion.identity;
            _wheelRoot.layer = 2;

            _lastHighlightedIndex = -1;
            BuildCarouselIcons();
        }

        public static void SetTargetIndex(int index)
        {
            _targetOffset = index;
        }

        public static void UpdateCarousel()
        {
            if (_wheelRoot == null) return;

            _currentOffset = Mathf.Lerp(_currentOffset, _targetOffset, Time.deltaTime * 14f);

            var list = EditorSessionManager.ActiveTabAssets;
            int total = list.Count;
            if (total == 0) return;

            float slotSpacingAngle = 24f;

            for (int i = 0; i < _spawnedIcons.Count; i++)
            {
                GameObject icon = _spawnedIcons[i];
                if (icon == null || i >= total) continue;

                float diff = i - _currentOffset;

                while (diff > total * 0.5f) diff -= total;
                while (diff < -total * 0.5f) diff += total;

                float absDiff = Mathf.Abs(diff);

                if (absDiff > 3.4f)
                {
                    if (icon.activeSelf) icon.SetActive(false);
                    continue;
                }

                if (!icon.activeSelf) icon.SetActive(true);

                float angleDeg = 90f - (diff * slotSpacingAngle);
                float angleRad = angleDeg * Mathf.Deg2Rad;

                Vector3 targetPos = new Vector3(Mathf.Cos(angleRad) * ArcRadius, (Mathf.Sin(angleRad) - 1.0f) * ArcRadius, 0f);
                icon.transform.localPosition = targetPos;
                icon.transform.localRotation = Quaternion.Euler(15f, Time.time * 30f, 0f);

                bool isSelected = (i == EditorSessionManager.SelectedAssetIndex);
                float baseScale = list[i].DefaultScale * IconScaleMultiplier;
                float falloff = Mathf.Clamp01(1.0f - (absDiff / 3.4f));

                float finalScale = isSelected ? (baseScale * 1.35f) : (baseScale * Mathf.Lerp(0.5f, 1.0f, falloff));
                icon.transform.localScale = Vector3.one * finalScale;

                ApplyIconAlpha(icon, isSelected, falloff);
            }

            if (_lastHighlightedIndex != EditorSessionManager.SelectedAssetIndex)
            {
                _lastHighlightedIndex = EditorSessionManager.SelectedAssetIndex;
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

        private static void ApplyIconAlpha(GameObject icon, bool isSelected, float alpha)
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
                        mat.color = new Color(0.5f, 1.3f, 1.6f, 1f);
                        if (mat.HasProperty("_EmissionColor"))
                        {
                            mat.SetColor("_EmissionColor", new Color(0.4f, 1.0f, 1.4f, 1f));
                            mat.EnableKeyword("_EMISSION");
                        }
                    }
                    else
                    {
                        Color c = new Color(0.45f, 0.5f, 0.58f, alpha);
                        mat.color = c;
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
            for (int i = 0; i < _spawnedIcons.Count; i++)
            {
                if (_spawnedIcons[i] != null) GameObject.Destroy(_spawnedIcons[i]);
            }
            _spawnedIcons.Clear();

            var currentList = EditorSessionManager.ActiveTabAssets;
            int total = currentList.Count;
            if (total == 0 || _wheelRoot == null) return;

            for (int i = 0; i < total; i++)
            {
                CatalogAsset asset = currentList[i];
                if (asset.SourceTemplate == null) continue;

                GameObject icon = GameObject.Instantiate(asset.SourceTemplate);
                icon.name = $"ArcIcon_{i}_{asset.DisplayName}";
                icon.transform.SetParent(_wheelRoot.transform, false);

                icon.layer = 2;
                foreach (var tr in icon.GetComponentsInChildren<Transform>(true))
                {
                    tr.gameObject.layer = 2;
                }

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
                else if (asset.IsSunlight)
                {
                    Renderer r = icon.GetComponentInChildren<Renderer>();
                    if (r != null && r.material != null)
                    {
                        r.material.color = new Color(1f, 0.85f, 0.2f, 1f);
                        if (r.material.HasProperty("_EmissionColor"))
                        {
                            r.material.SetColor("_EmissionColor", new Color(1.5f, 1.2f, 0.3f, 1f));
                            r.material.EnableKeyword("_EMISSION");
                        }
                    }
                }

                icon.SetActive(true);
                _spawnedIcons.Add(icon);
            }

            SetTargetIndex(EditorSessionManager.SelectedAssetIndex);
            _currentOffset = _targetOffset;
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

    /*
     * Serialization & Course Disk IO Pipeline
     * Formats level entities, custom physics parameters, compound parent-child hierarchies,
     * explicit 8-part kinematic paths (PATH:1:Speed:Ax:Ay:Az:Bx:By:Bz), and staging scene tags.
     */
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
            EditorSessionManager.ShowNotification($"Saved {lines.Count - 5} objects to {cleanName}.txt!");
            MelonLogger.Msg($">> Saved {lines.Count - 5} objects with full parameters to {path}!");

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

                    if (lowName.Contains("spotlight") || lowName.Contains("sunlight") || obj.name.ToLower().Contains("spotlight") || obj.name.ToLower().Contains("sunlight"))
                    {
                        LightConfig cfg = new LightConfig();
                        cfg.IsDirectional = lowName.Contains("sunlight") || obj.name.ToLower().Contains("sunlight");
                        if (customParam > 0f) cfg.Intensity = customParam;

                        if (p.Length >= 11) cfg.SpotAngle = ParseFloat(p[10]);
                        if (p.Length >= 12) cfg.Color = HexToColor(p[11]);
                        if (p.Length >= 13) cfg.VolumetricIntensity = ParseFloat(p[12]);

                        EditorSessionManager.ApplyLightConfig(obj, cfg);
                        if (EditorSessionManager.SelectedLightObject == null)
                        {
                            EditorSessionManager.SelectedLightObject = obj;
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

                            obj.transform.position = pA;
                            EditorSessionManager.MotionPaths[obj] = new ObjectMotionPath
                            {
                                PointA = pA,
                                PointB = pB,
                                Speed = spd > 0.1f ? spd : 3.5f
                            };
                        }
                        else if (pathParts.Length >= 5 && pathParts[0] == "1")
                        {
                            float spd = ParseFloat(pathParts[1]);
                            Vector3 pB = new Vector3(ParseFloat(pathParts[2]), ParseFloat(pathParts[3]), ParseFloat(pathParts[4]));

                            EditorSessionManager.MotionPaths[obj] = new ObjectMotionPath
                            {
                                PointA = pos,
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
        }
    }

    /*
     * Scene Harvesting & Geometry Ingestion Pipeline
     * Manages model extraction, creates leak-free procedural laser geometry,
     * and guarantees deterministic resource cleanup via tracked pools.
     */
    public static class SceneHarvestingService
    {
        public static Light NativeSceneSun = null;

        private static readonly List<Mesh> _proceduralMeshes = new List<Mesh>();
        private static readonly List<Material> _proceduralMaterials = new List<Material>();

        public static void CleanupProceduralResources()
        {
            for (int i = 0; i < _proceduralMeshes.Count; i++)
            {
                if (_proceduralMeshes[i] != null)
                {
                    GameObject.Destroy(_proceduralMeshes[i]);
                }
            }
            _proceduralMeshes.Clear();

            for (int i = 0; i < _proceduralMaterials.Count; i++)
            {
                if (_proceduralMaterials[i] != null)
                {
                    GameObject.Destroy(_proceduralMaterials[i]);
                }
            }
            _proceduralMaterials.Clear();

            MelonLogger.Msg(">> Cleaned all dynamic procedural meshes and materials.");
        }

        public static void DebugDumpSceneLighting()
        {
            MelonLogger.Msg("==================================================");
            MelonLogger.Msg("          SCENE LIGHTING & ATMOSPHERE DUMP        ");
            MelonLogger.Msg("==================================================");

            NativeSceneSun = null;

            if (RenderSettings.sun != null && RenderSettings.sun.gameObject.scene.isLoaded)
            {
                NativeSceneSun = RenderSettings.sun;
                MelonLogger.Msg($"[Native Sun] Successfully hooked from RenderSettings.sun: '{NativeSceneSun.name}'");
            }

            Light[] allLights = Resources.FindObjectsOfTypeAll<Light>();
            MelonLogger.Msg($"[Scene Lights] Found {allLights.Length} total Light components in scene/memory:");

            for (int i = 0; i < allLights.Length; i++)
            {
                Light l = allLights[i];
                if (l == null) continue;

                string path = l.name;
                Transform parent = l.transform.parent;
                while (parent != null)
                {
                    path = parent.name + "/" + path;
                    parent = parent.parent;
                }

                bool isSceneObject = l.gameObject.scene.isLoaded;
                MelonLogger.Msg($"  #{i:D2} [{(isSceneObject ? "SCENE" : "ASSET")}] Path: '{path}' | Type: {l.type} | Active: {l.gameObject.activeInHierarchy} (CompEnabled: {l.enabled}) | Color: {l.color} | Int: {l.intensity} | Range: {l.range}");

                if (NativeSceneSun == null && isSceneObject && l.type == LightType.Directional)
                {
                    if (!l.name.Contains("Template") && !l.name.StartsWith("Custom_"))
                    {
                        NativeSceneSun = l;
                        MelonLogger.Msg($"  >>> [CANDIDATE FOUND] Hooked native sun fallback: '{path}'");
                    }
                }
            }

            MelonLogger.Msg("==================================================");
        }

        public static Mesh CreateDoubleSidedPlaneMesh(float width, float height)
        {
            Mesh m = new Mesh();
            m.name = "Laser_DoubleSided_Mesh";

            float hw = width * 0.5f;
            float hh = height * 0.5f;
            float zOffset = 0.01f;

            Vector3[] vertices = new Vector3[]
            {
                new Vector3(-hw, -hh, zOffset), new Vector3(hw, -hh, zOffset), new Vector3(hw, hh, zOffset), new Vector3(-hw, hh, zOffset),
                new Vector3(-hw, -hh, -zOffset), new Vector3(hw, -hh, -zOffset), new Vector3(hw, hh, -zOffset), new Vector3(-hw, hh, -zOffset)
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

            _proceduralMeshes.Add(m);
            return m;
        }

        public static void HarvestAllSceneModels()
        {
            EditorSessionManager.AllAssets.Clear();
            HashSet<string> seenMeshes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            MeshRenderer[] renderers = GameObject.FindObjectsOfType<MeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer r = renderers[i];
                if (r != null && r.sharedMaterial != null && !r.sharedMaterial.name.ToLower().Contains("laser"))
                {
                    EditorSessionManager.CachedSceneMaterial = r.sharedMaterial;
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
                    BaseRotation = Quaternion.identity
                });
            }

            // 2. Waypoint and Checkpoint Gates
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

            // 3A. Dynamic Technical Spotlight
            GameObject spotTemplate = new GameObject("Template_Spotlight");
            Light spotLight = spotTemplate.AddComponent<Light>();
            spotLight.type = LightType.Spot;
            spotLight.range = 120f;
            spotLight.spotAngle = 60f;
            spotLight.color = Color.cyan;
            spotLight.intensity = 28000f;

            GameObject housing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            housing.name = "Light_Housing";
            housing.transform.SetParent(spotTemplate.transform, false);
            housing.transform.localScale = new Vector3(0.6f, 0.4f, 0.6f);
            housing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            Collider housingCol = housing.GetComponent<Collider>();
            if (housingCol != null) GameObject.DestroyImmediate(housingCol);

            GameObject lens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lens.name = "Light_Lens";
            lens.transform.SetParent(spotTemplate.transform, false);
            lens.transform.localScale = new Vector3(0.55f, 0.55f, 0.35f);
            lens.transform.localPosition = new Vector3(0f, 0f, 0.38f);

            if (EditorSessionManager.CachedSceneMaterial != null)
            {
                Renderer hr = housing.GetComponent<Renderer>();
                if (hr != null) hr.material = EditorSessionManager.CachedSceneMaterial;
            }

            spotTemplate.SetActive(false);

            EditorSessionManager.AllAssets.Add(new CatalogAsset
            {
                DisplayName = "Tech Spotlight",
                SourceTemplate = spotTemplate,
                FilterMesh = housing.GetComponent<MeshFilter>()?.sharedMesh,
                Category = AssetCategory.Gameplay,
                IsSpotlight = true,
                DefaultScale = 1.0f,
                VerticalOffset = 0f,
                BaseRotation = Quaternion.identity
            });

            // 3B. Global Directional Sunlight
            GameObject sunTemplate = new GameObject("Template_Sunlight");
            Light sunLight = sunTemplate.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.renderMode = LightRenderMode.ForcePixel;
            sunLight.cullingMask = ~0;
            sunLight.color = new Color(1f, 0.85f, 0.6f);
            sunLight.intensity = 3.5f;

            GameObject sunOrb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sunOrb.name = "Light_Housing";
            sunOrb.transform.SetParent(sunTemplate.transform, false);
            sunOrb.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);

            Collider sunCol = sunOrb.GetComponent<Collider>();
            if (sunCol != null) GameObject.DestroyImmediate(sunCol);

            GameObject sunRay = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            sunRay.name = "Light_Lens";
            sunRay.transform.SetParent(sunTemplate.transform, false);
            sunRay.transform.localScale = new Vector3(0.25f, 1.4f, 0.25f);
            sunRay.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            sunRay.transform.localPosition = new Vector3(0f, 0f, 1.2f);

            Collider rayCol = sunRay.GetComponent<Collider>();
            if (rayCol != null) GameObject.DestroyImmediate(rayCol);

            Shader unlitShader = Shader.Find("Unlit/Color") ?? Shader.Find("Particles/Standard Unlit");
            if (unlitShader != null)
            {
                Material sunMat = new Material(unlitShader);
                sunMat.color = new Color(1f, 0.88f, 0.35f, 1f);
                _proceduralMaterials.Add(sunMat);
                sunOrb.GetComponent<Renderer>().material = sunMat;
                sunRay.GetComponent<Renderer>().material = sunMat;
            }

            sunTemplate.SetActive(false);

            EditorSessionManager.AllAssets.Add(new CatalogAsset
            {
                DisplayName = "Global Sunlight",
                SourceTemplate = sunTemplate,
                FilterMesh = sunOrb.GetComponent<MeshFilter>()?.sharedMesh,
                Category = AssetCategory.Gameplay,
                IsSunlight = true,
                DefaultScale = 1.0f,
                VerticalOffset = 0f,
                BaseRotation = Quaternion.Euler(50f, -30f, 0f)
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
                    _proceduralMaterials.Add(laserMat);
                }
            }

            // 4A. Standard Static Laser Barrier (8m x 4m)
            GameObject laserTemplate = new GameObject("Template_Laser_Barrier");
            Mesh laserMesh = CreateDoubleSidedPlaneMesh(8f, 4f);
            MeshFilter laserMf = laserTemplate.AddComponent<MeshFilter>();
            laserMf.sharedMesh = laserMesh;
            MeshRenderer laserMr = laserTemplate.AddComponent<MeshRenderer>();
            if (laserMat != null) laserMr.sharedMaterial = laserMat;
            BoxCollider laserCol = laserTemplate.AddComponent<BoxCollider>();
            laserCol.isTrigger = true;
            laserCol.size = new Vector3(8f, 4f, 0.35f);
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

            // 4B. Extended Static Laser Barrier (22m x 4m)
            GameObject longLaserTemplate = new GameObject("Template_Long_Laser_Barrier");
            Mesh longLaserMesh = CreateDoubleSidedPlaneMesh(22f, 4f);
            MeshFilter longMf = longLaserTemplate.AddComponent<MeshFilter>();
            longMf.sharedMesh = longLaserMesh;
            MeshRenderer longMr = longLaserTemplate.AddComponent<MeshRenderer>();
            if (laserMat != null) longMr.sharedMaterial = laserMat;
            BoxCollider longCol = longLaserTemplate.AddComponent<BoxCollider>();
            longCol.isTrigger = true;
            longCol.size = new Vector3(22f, 4f, 0.35f);
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

            // 4C. Rotating Kinetic Laser Array (18m Dual Beam)
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
                    for (int i = 0; i < allTransforms.Length; i++)
                    {
                        string n = allTransforms[i].name.ToLower();
                        if (n.Contains("tourelle") || n.Contains("turret"))
                        {
                            EditorSessionManager.PrefabTurret = allTransforms[i].gameObject;
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

            // 7. Structural Architecture & Level Geometry Extraction
            MeshFilter[] allFilters = GameObject.FindObjectsOfType<MeshFilter>();

            for (int f = 0; f < allFilters.Length; f++)
            {
                MeshFilter mf = allFilters[f];
                if (mf == null || mf.sharedMesh == null) continue;
                string mName = mf.sharedMesh.name.Trim();
                string goName = mf.gameObject.name.ToLower();
                string mLow = mName.ToLower();

                if (goName.Contains("helice") || goName.Contains("helix") || mLow.Contains("helix")) continue;
                if (goName.Contains("tourelle") || goName.Contains("turret") || mLow.Contains("tourelle")) continue;
                if (mLow.Contains("wall_12x12x01_lod0") || mLow.Contains("wall 12x12x01 lod0") || goName.Contains("wall_12x12x01_lod0")) continue;

                if (mLow.Contains("impostor") || mLow.Contains("hole")) continue;
                if (mLow.Contains("lod1") || mLow.Contains("lod2") || mLow.Contains("lod3")) continue;
                if (mLow.StartsWith("combined")) continue;

                if (mLow.Contains("cylinder001") || goName.Contains("cylinder001") || mLow.Contains("cylinder 001") || goName.Contains("cylinder 001")) continue;

                if (mLow.Contains("decal") || mLow.Contains("bolt") || mLow.Contains("screw") ||
                    mLow.Contains("debris") || mLow.Contains("shard") || mLow.Contains("cable") ||
                    mLow.Contains("wire") || mLow.Contains("rivet"))
                {
                    continue;
                }

                Vector3 size = mf.sharedMesh.bounds.size;
                float maxDim = Mathf.Max(size.x, size.y, size.z);

                if (maxDim < 1.5f) continue;
                if (mf.sharedMesh.vertexCount < 8) continue;

                if (!seenMeshes.Contains(mName))
                {
                    seenMeshes.Add(mName);

                    string friendly = CleanDisplayName(mName, goName);
                    float defScale = DetermineDefaultScale(friendly, size);
                    Quaternion baseRot = DetermineBaseRotation(friendly, mf.gameObject);
                    AssetCategory cat = CategorizeAsset(friendly, goName);

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

            EditorSessionManager.RebuildAssetCategoryCaches();
        }

        private static AssetCategory CategorizeAsset(string friendly, string goName)
        {
            string low = (friendly + " " + goName).ToLower();

            if (low.Contains("switch") || low.Contains("spark") || low.Contains("activat") || low.Contains("trigger"))
            {
                return AssetCategory.Gameplay;
            }

            return AssetCategory.Building;
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

            GameObject[] rootObjects = activeScene.GetRootGameObjects();
            for (int i = 0; i < rootObjects.Length; i++)
            {
                GameObject root = rootObjects[i];
                if (root == null) continue;
                string r = root.name;

                if (r == "_LA" || r == "L_A")
                {
                    if (NativeSceneSun != null && NativeSceneSun.transform.IsChildOf(root.transform))
                    {
                        NativeSceneSun.transform.SetParent(null, true);
                        NativeSceneSun.gameObject.SetActive(true);
                        NativeSceneSun.enabled = true;
                        MelonLogger.Msg(">> Preserved native directional sun before hiding _LA!");
                    }
                    root.SetActive(false);
                }
                else if (r == "_LD" || r == "L_D")
                {
                    root.SetActive(false);
                }
            }
        }
    }

    /*
     * Harmony Runtime Detours & Gameplay Patches
     */
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

    [HarmonyPatch(typeof(TurretScript), nameof(TurretScript.Shoot))]
    [HarmonyPatch(typeof(TurretScript), nameof(TurretScript.Shoot))]
    public static class TurretShootPatch
    {
        // Increased buffer so bullets are never missed in dense geometry
        private static readonly Collider[] _turretHitsBuffer = new Collider[128];

        [HarmonyPostfix]
        public static void Postfix(TurretScript __instance)
        {
            if (__instance == null || __instance._bulletPrefab == null) return;

            try
            {
                string bulletPrefabName = __instance._bulletPrefab.name;
                Vector3 turretPos = __instance.transform.position;

                Collider[] turretCols = __instance.GetComponentsInChildren<Collider>(true);
                SphereCollider triggerSphere = __instance._triggerAnimation;

                int hitCount = Physics.OverlapSphereNonAlloc(turretPos, 8.0f, _turretHitsBuffer, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < hitCount; i++)
                {
                    Collider hitCol = _turretHitsBuffer[i];
                    if (hitCol == null) continue;

                    GameObject hitGo = hitCol.gameObject;
                    bool isBullet = hitGo.name.Contains(bulletPrefabName) ||
                                    hitCol.transform.root.name.Contains(bulletPrefabName) ||
                                    hitGo.name.ToLower().Contains("bullet") ||
                                    hitCol.transform.root.name.ToLower().Contains("bullet");

                    if (!isBullet) continue;

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
}
