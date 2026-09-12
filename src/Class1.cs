using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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

[assembly: MelonInfo(typeof(DeadCoreEditor.DeadCoreLevelEditorMod), "DeadCore Level Editor Suite", "5.4.0", "Trufa")]
[assembly: MelonGame(null, null)]

namespace DeadCoreEditor
{
    public class DeadCoreLevelEditorMod : MelonMod
    {
        private static LogsMenu _lastTransformedLogsMenu = null;
        public static int ActiveTab = 0; // 0 = My Levels, 1 = Community
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

                // Locate active LogsMenu in scene
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
            string currentScene = SceneManager.GetActiveScene().name.ToLower();

            if (EditorSessionManager.IsCustomSessionActive && EditorSessionManager.IsEditModeActive)
            {
                EditorSessionManager.DrawEditorGUI();
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
        Essentials = 0,
        Extracted = 1,
        Misc = 2
    }

    public class CatalogAsset
    {
        public string DisplayName;
        public GameObject SourceTemplate;
        public Mesh FilterMesh;
        public AssetCategory Category;
        public bool IsJumper;
        public bool IsCheckPoint;
        public bool IsTurret;
        public bool IsHelix;
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

    // --- NATIVE LOGSMENU HIJACKER ---
    public static class NativeLogsMenuHijacker
    {
        private static GameObject _nativePlayButton = null;
        private static GameObject _nativeCreateButton = null;
        private static GameObject _nativeDeleteButton = null;
        private static readonly List<GameObject> _spawnedRowObjects = new List<GameObject>();

        private static Toggle _cachedMyLevelsToggle = null;
        private static Toggle _cachedCommunityToggle = null;

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
            if (currentTab == 0) // My Levels
            {
                if (Directory.Exists(MapBrowserService.MyLevelsDir))
                    fileList.AddRange(Directory.GetFiles(MapBrowserService.MyLevelsDir, "*.txt"));

                if (fileList.Count == 0)
                {
                    string def = Path.Combine(MapBrowserService.MyLevelsDir, "Default_Level.txt");
                    if (!File.Exists(def))
                    {
                        Vector3 spawn = EditorSessionManager.LevelSpawnPosition;
                        File.WriteAllText(def, $"Floor_Platform_16x16;{spawn.x:F4};{(spawn.y - 1.2f):F4};{spawn.z:F4};0.5500;0.0000;0.0000;0.0000;1.0000;0.00\n");
                    }
                    fileList.Add(def);
                }
            }
            else // Community
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
            }

            for (int i = 0; i < fileList.Count; i++)
            {
                string filePath = fileList[i];
                string fileName = Path.GetFileNameWithoutExtension(filePath);

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
                        lt._nameLabel.text = fileName;
                        lt._nameLabel.enableWordWrapping = false;
                    }
                    GameObject.DestroyImmediate(lt);
                }

                TMP_Text[] tmps = itemObj.GetComponentsInChildren<TMP_Text>(true);
                if (tmps.Length >= 2)
                {
                    tmps[0].text = (i + 1).ToString("D2");
                    tmps[0].enableWordWrapping = false;

                    tmps[1].text = fileName;
                    tmps[1].enableWordWrapping = false;
                }

                Toggle tog = itemObj.GetComponent<Toggle>();
                if (tog != null) GameObject.DestroyImmediate(tog);

                Button btn = itemObj.GetComponent<Button>();
                if (btn == null) btn = itemObj.AddComponent<Button>();

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
            try { objectCount = File.ReadAllLines(fullPath).Length; } catch { }

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

            if (menu._shortDesc != null)
            {
                menu._shortDesc.text = $"LEVEL: {fileName}\nOBJECTS: {objectCount}";
                DisableLocalizationScripts(menu._shortDesc.gameObject);
            }

            if (menu._longDesc != null)
            {
                menu._longDesc.text = "";
                DisableLocalizationScripts(menu._longDesc.gameObject);
            }

            TMP_Text[] strayTexts = menu.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < strayTexts.Length; i++)
            {
                if (strayTexts[i] == null) continue;
                string st = strayTexts[i].text.Trim().ToLower();
                if (st == "dash" || st == "the call" || st == "unknown" || st == "knowledge")
                {
                    strayTexts[i].text = "";
                }
            }

            if (menu._logBigPicture != null)
            {
                menu._logBigPicture.color = new Color(0.08f, 0.45f, 0.75f, 0.35f);
            }

            MelonLogger.Msg($">> [Native UI] Selected Level: '{fileName}' ({objectCount} objects)");
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

            // --- 1. [ PLAY LEVEL ] BUTTON (Cyan Accent) ---
            if (_nativePlayButton == null || _nativePlayButton.Equals(null))
            {
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

            // --- 2. [ + NEW LEVEL ] BUTTON (Green Accent) ---
            if (_nativeCreateButton == null || _nativeCreateButton.Equals(null))
            {
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

            // --- 3. [ DELETE LEVEL ] BUTTON (Red Accent) ---
            if (_nativeDeleteButton == null || _nativeDeleteButton.Equals(null))
            {
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
            string starterContent = $"Floor_Platform_16x16;{spawn.x:F4};{(spawn.y - 1.2f):F4};{spawn.z:F4};0.5500;0.0000;0.0000;0.0000;1.0000;0.00\n";
            File.WriteAllText(newPath, starterContent);

            MelonLogger.Msg($">> Created new level with solid floor platform: '{newName}.txt'");

            DeadCoreLevelEditorMod.SwitchTab(0, menu);
            MapBrowserService.SelectedMapPath = newPath;
            MapBrowserService.SelectedMapName = newName;

            TransformLogsMenu(menu, 0);
        }

        private static void CleanNativeButtonClone(GameObject btnObj)
        {
            DisableLocalizationScripts(btnObj);

            BackButton clonedBackScript = btnObj.GetComponent<BackButton>();
            if (clonedBackScript != null) GameObject.DestroyImmediate(clonedBackScript);

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
            TMP_Text tmp = btnObj.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
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

            TextLabel tl = root.GetComponent<TextLabel>();
            if (tl != null) tl.enabled = false;

            Component[] comps = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < comps.Length; i++)
            {
                Component c = comps[i];
                if (c == null) continue;
                string typeName = c.GetIl2CppType().Name;
                if (typeName.Contains("Translate") || typeName.Contains("Localization") || typeName == "TextLabel")
                {
                    MonoBehaviour mb = c.TryCast<MonoBehaviour>();
                    if (mb != null) mb.enabled = false;
                }
            }
        }
    }

    // --- OVERHAULED MAP BROWSER SERVICE ---
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
                File.WriteAllText(defaultMyPath, $"Floor_Platform_16x16;{spawn.x:F4};{(spawn.y - 1.2f):F4};{spawn.z:F4};0.5500;0.0000;0.0000;0.0000;1.0000;0.00\n");
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

    // --- CORE EDITOR MANAGER ---
    public static class EditorSessionManager
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public static bool CustomLevelSelected = true;
        public static bool IsCustomSessionActive = false;
        public static bool IsEditModeActive = false;
        public static bool IsLevelInitialized = false;

        public static List<CatalogAsset> AllAssets = new List<CatalogAsset>();
        public static AssetCategory CurrentTab = AssetCategory.Essentials;
        public static int SelectedAssetIndex = 0;
        public static bool IsBlockSelected = false;

        public static float ActiveJumperForce = 18.0f;
        public static float ActiveTurbineSpeed = 35.0f;
        public static float ActiveTurretFireDelay = 1.2f;
        public static float ActivePlacementScale = 0.55f;
        public static float CurrentGridSnap = 1.0f;

        // --- ENHANCED ROTATION STATE ---
        public static float TargetPitch = 0f;
        public static float TargetYaw = 0f;
        public static float TargetRoll = 0f;

        private static KeyCode _lastHeldKey = KeyCode.None;
        private static float _keyHoldDuration = 0f;
        private static float _keyRepeatTimer = 0f;

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

        public static Camera PlayerCameraInstance = null;
        public static CharacterController PlayerControllerInstance = null;
        public static Vector3 FrozenPlayerPosition = Vector3.zero;
        public static Quaternion FrozenPlayerRotation = Quaternion.identity;
        public static Vector3 LevelSpawnPosition = new Vector3(-241f, -95f, -6f);
        public static float VoidDeathY = -140f;

        public static List<CatalogAsset> ActiveTabAssets
        {
            get
            {
                var list = AllAssets.FindAll(a => a.Category == CurrentTab);
                if (list.Count == 0 && AllAssets.Count > 0) return AllAssets;
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

            EditorViewportCamera.DestroyCamera();
            PlacementHologramController.DestroyPreview();
            CarouselWheelToolbar.DestroyToolbar();

            PlayerCameraInstance = null;
            PlayerControllerInstance = null;

            AllAssets.Clear();
            CurrentTab = AssetCategory.Essentials;
            SelectedAssetIndex = 0;
            IsBlockSelected = false;

            PrefabJumper = null;
            PrefabCheckPoint = null;
            PrefabHelix = null;
            PrefabTurret = null;

            PlacedObjects.Clear();
            LastPlacedObject = null;
            JumperForces.Clear();
            TurbineSpeeds.Clear();
            TurretFireDelays.Clear();
            UndoHistory.Clear();
            RedoHistory.Clear();

            _isBoostActive = false;
            _jumperTriggerCooldown = 0f;
            _notificationTimer = 0f;

            TargetPitch = 0f;
            TargetYaw = 0f;
            TargetRoll = 0f;
            _lastHeldKey = KeyCode.None;
            _keyHoldDuration = 0f;
            _keyRepeatTimer = 0f;

            ActiveJumperForce = 18.0f;
            ActiveTurbineSpeed = 35.0f;
            ActiveTurretFireDelay = 1.2f;
            ActivePlacementScale = 0.55f;
            CurrentGridSnap = 1.0f;
        }

        public static void UpdateSession()
        {
            if (!IsLevelInitialized) return;

            if (!IsEditModeActive)
            {
                CheckVoidFall();
                CheckJumperBoostPhysics();
                CheckHelixWindPushing();
            }
            else
            {
                FreezePlayerEntity();
            }

            if (Input.GetKeyDown(KeyCode.F1))
            {
                ToggleEditMode();
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
                        float upwardLift = Mathf.Clamp(force * 0.42f, 6.0f, 13.5f);

                        launchVelocity = (horizDir * forwardSpeed) + (Vector3.up * upwardLift);
                    }
                    else
                    {
                        float upwardSpeed = Mathf.Clamp(force * 0.65f, 7.0f, 15.0f);
                        launchVelocity = Vector3.up * upwardSpeed;
                    }

                    _currentBoostVelocity = launchVelocity;
                    _isBoostActive = true;
                    _jumperTriggerCooldown = 0.40f;

                    MelonLogger.Msg($">> [BOOST] Launched! Speed: {force:F1} m/s");
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
                float windRange = 14.0f * obj.transform.localScale.z;

                if (forwardDist > 0.2f && forwardDist < windRange)
                {
                    Vector3 perp = toPlayer - forward * forwardDist;
                    float radius = 2.8f * obj.transform.localScale.x;

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

        public static void RespawnPlayer(GameObject player)
        {
            CharacterController cc = player.GetComponentInChildren<CharacterController>();
            if (cc != null) cc.enabled = false;

            if (CheckPointScript.LastCheckPoint != null && CheckPointScript.LastCheckPoint.gameObject.activeInHierarchy)
            {
                Transform sp = CheckPointScript.LastCheckPoint._spawnPoint;
                Vector3 targetPos = (sp != null) ? sp.position : (CheckPointScript.LastCheckPoint.transform.position + Vector3.up * 0.2f);
                Quaternion targetRot = (sp != null) ? sp.rotation : CheckPointScript.LastCheckPoint.transform.rotation;

                player.transform.position = targetPos;
                player.transform.rotation = targetRot;

                MelonLogger.Msg(">> [Respawn] Returned to active Checkpoint!");
            }
            else
            {
                player.transform.position = LevelSpawnPosition;
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

                if (IsBlockSelected)
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

                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                MelonLogger.Msg(">> Restored Playtest Mode.");
            }
        }

        private static void HandleFlowShortcuts()
        {
            if (Input.GetKeyDown(KeyCode.Tab) && !Input.GetMouseButton(1))
            {
                CycleTabs();
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

            // F5 now writes strictly into UserData/MyLevels/{SelectedMapName}.txt
            if (Input.GetKeyDown(KeyCode.F5))
            {
                LevelPersistenceService.SaveLevel(MapBrowserService.SelectedMapName);
            }

            if (Input.GetKeyDown(KeyCode.F6))
            {
                LevelPersistenceService.LoadLevel(MapBrowserService.SelectedMapName);
            }
        }

        public static void CycleTabs()
        {
            CurrentTab = (AssetCategory)(((int)CurrentTab + 1) % 3);
            SelectedAssetIndex = 0;

            CarouselWheelToolbar.BuildCarouselIcons();

            if (IsBlockSelected)
            {
                PlacementHologramController.SpawnHologram(CurrentAsset);
            }

            MelonLogger.Msg($">> [TAB SWITCH] Category: [{CurrentTab}] ({ActiveTabAssets.Count} items)");
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
                    ActiveJumperForce += Mathf.Sign(scroll) * 1.0f;
                    ActiveJumperForce = Mathf.Clamp(ActiveJumperForce, 2.0f, 60.0f);
                    MelonLogger.Msg($">> [Jumper Force] Set: {ActiveJumperForce:F1}");

                    GameObject aimed = GetAimedPlacedObject();
                    if (aimed != null && (JumperForces.ContainsKey(aimed) || aimed.name.ToLower().Contains("jumper")))
                    {
                        ApplyJumperForce(aimed, ActiveJumperForce);
                    }
                }
                else if (CurrentAsset != null && CurrentAsset.IsHelix)
                {
                    ActiveTurbineSpeed += Mathf.Sign(scroll) * 2.5f;
                    ActiveTurbineSpeed = Mathf.Clamp(ActiveTurbineSpeed, 5.0f, 150.0f);
                    MelonLogger.Msg($">> [Turbine Speed] Set: {ActiveTurbineSpeed:F1}");

                    GameObject aimed = GetAimedPlacedObject();
                    if (aimed != null && (TurbineSpeeds.ContainsKey(aimed) || aimed.name.ToLower().Contains("helix")))
                    {
                        ApplyTurbineSpeed(aimed, ActiveTurbineSpeed);
                    }
                }
                else if (CurrentAsset != null && CurrentAsset.IsTurret)
                {
                    ActiveTurretFireDelay -= Mathf.Sign(scroll) * 0.15f;
                    ActiveTurretFireDelay = Mathf.Clamp(ActiveTurretFireDelay, 0.2f, 6.0f);
                    MelonLogger.Msg($">> [Turret Fire Delay] Set: {ActiveTurretFireDelay:F2}s");

                    GameObject aimed = GetAimedPlacedObject();
                    if (aimed != null && (TurretFireDelays.ContainsKey(aimed) || aimed.name.ToLower().Contains("turret")))
                    {
                        ApplyTurretSettings(aimed, ActiveTurretFireDelay, 1000f);
                    }
                }
                else
                {
                    ActivePlacementScale += Mathf.Sign(scroll) * 0.05f;
                    ActivePlacementScale = Mathf.Clamp(ActivePlacementScale, 0.05f, 10.0f);
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
            if (Physics.Raycast(ray, out hit, 300f, mask))
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
                target.SetActive(false);

                float param = 0f;
                if (JumperForces.ContainsKey(target)) param = JumperForces[target];
                else if (TurbineSpeeds.ContainsKey(target)) param = TurbineSpeeds[target];
                else if (TurretFireDelays.ContainsKey(target)) param = TurretFireDelays[target];

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
                            if (record.AssetName.ToLower().Contains("turret")) ApplyTurretSettings(recreated, record.CustomParameter, 1000f);
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
                if (size.x <= size.y && size.x <= size.z) return Quaternion.Euler(0f, 0f, 90f);
                if (size.z <= size.x && size.z <= size.y) return Quaternion.Euler(90f, 0f, 0f);
            }
            return Quaternion.Euler(0f, 0f, 90f);
        }

        private static void CycleGridSnap()
        {
            if (CurrentGridSnap == 1.0f) CurrentGridSnap = 2.0f;
            else if (CurrentGridSnap == 2.0f) CurrentGridSnap = 4.0f;
            else if (CurrentGridSnap == 4.0f) CurrentGridSnap = 0.0f;
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
                if (typeName.Contains("particle") || typeName.Contains("light") || typeName.Contains("emitter") || typeName.Contains("trail") || typeName.Contains("flare") || typeName.Contains("halo"))
                {
                    GameObject.DestroyImmediate(comp);
                }
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var tr in transforms)
            {
                if (tr == null || tr.gameObject == root) continue;
                string objName = tr.gameObject.name.ToLower();
                if (objName.Contains("particle") || objName.Contains("flame") || objName.Contains("fx") || objName.Contains("fire") || objName.Contains("glow") || objName.Contains("light") || objName.Contains("flare") || objName.Contains("beam") || objName.Contains("laser") || objName.Contains("anneau"))
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
                zone._maxVelocity = speed * 1.5f;
            }

            Helix h = turbineObj.GetComponentInChildren<Helix>();
            if (h != null)
            {
                h.enabled = true;
                h._maximumVelocity = speed * 10f;
            }

            MelonLogger.Msg($">> [Turbine Speed] Set to {speed:F1} on '{turbineObj.name}'");
        }

        public static void ApplyTurretSettings(GameObject turretObj, float fireDelay, float firePower = 1000f)
        {
            if (turretObj == null) return;

            TurretFireDelays[turretObj] = fireDelay;

            TurretScript ts = turretObj.GetComponentInChildren<TurretScript>();
            if (ts != null)
            {
                ts._fireDelay = fireDelay;
                ts._firePower = 1000f;
                ts.enabled = true;
            }

            MelonLogger.Msg($">> [Turret Settings] Fire Delay: {fireDelay:F2}s, Power: 1000 on '{turretObj.name}'");
        }

        public static void DrawEditorGUI()
        {
            Camera cam = EditorViewportCamera.ViewportCamera;
            if (cam == null) return;

            Color originalColor = GUI.color;

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

                if (TurbineSpeeds.ContainsKey(obj) || obj.name.ToLower().Contains("helix"))
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

                if (TurretFireDelays.ContainsKey(obj) || obj.name.ToLower().Contains("turret"))
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

            GUI.color = Color.white;
            string tabName = CurrentTab == AssetCategory.Essentials ? "1: ESSENTIALS" : (CurrentTab == AssetCategory.Extracted ? "2: EXTRACTED" : "3: MISC");
            string gridName = (CurrentGridSnap > 0.01f) ? $"{CurrentGridSnap}m" : "OFF";

            string status = IsBlockSelected
                ? $"[{tabName}] | PLACING: '{CurrentAsset?.DisplayName}' | Rot: [P:{TargetPitch:0}° Y:{TargetYaw:0}° R:{TargetRoll:0}°] | Snap: {gridName} (G) | T: 90° Snap | R: Reset"
                : $"[{tabName}] | MAP: '{MapBrowserService.SelectedMapName}' | F5: Save | F6: Load | Z: Undo | Y: Redo";

            GUI.Box(new Rect(Screen.width * 0.5f - 430f, 15f, 860f, 32f), status);

            if (_notificationTimer > 0f)
            {
                GUI.color = new Color(0.2f, 1f, 0.4f, Mathf.Clamp01(_notificationTimer));
                GUI.Box(new Rect(Screen.width * 0.5f - 220f, Screen.height - 85f, 440f, 34f), _notificationMessage);
            }

            GUI.color = originalColor;
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

            // Auto-load selected map from browser
            if (!string.IsNullOrEmpty(MapBrowserService.SelectedMapPath) && File.Exists(MapBrowserService.SelectedMapPath))
            {
                MelonLogger.Msg($">> Auto-loading selected map: '{MapBrowserService.SelectedMapName}'...");
                LevelPersistenceService.LoadLevelByFullPath(MapBrowserService.SelectedMapPath);
            }

            // Platform Safeguard: Verify if a solid platform exists within 30m of spawn
            bool hasStartingPlatform = false;
            foreach (var obj in PlacedObjects)
            {
                if (obj != null && (obj.name.ToLower().Contains("platform") || obj.name.ToLower().Contains("floor")))
                {
                    if (Vector3.Distance(obj.transform.position, startPos) < 30f)
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

            UnfreezePlayerControls();
            IsLevelInitialized = true;
            MelonLogger.Msg(">> Custom Level Initialized & Ready!");
        }

        public static GameObject SpawnAssetByName(string partialName, Vector3 position, float scale, Quaternion? customRotation = null)
        {
            if (string.IsNullOrEmpty(partialName)) return null;

            string clean = partialName.Replace("_", " ").Trim().ToLower();

            CatalogAsset found = AllAssets.Find(a => a.DisplayName.ToLower() == clean);
            if (found == null)
            {
                found = AllAssets.Find(a => a.DisplayName.ToLower().Contains(clean) || clean.Contains(a.DisplayName.ToLower()));
            }

            if (found == null)
            {
                if (clean.Contains("platform") || clean.Contains("floor")) found = AllAssets.Find(a => a.DisplayName.ToLower().Contains("platform") || a.DisplayName.ToLower().Contains("floor"));
                else if (clean.Contains("jumper")) found = AllAssets.Find(a => a.IsJumper);
                else if (clean.Contains("checkpoint")) found = AllAssets.Find(a => a.IsCheckPoint);
                else if (clean.Contains("turret")) found = AllAssets.Find(a => a.IsTurret);
                else if (clean.Contains("helix") || clean.Contains("turbine")) found = AllAssets.Find(a => a.IsHelix);
                else if (clean.Contains("crate")) found = AllAssets.Find(a => a.DisplayName.ToLower().Contains("crate"));
                else if (clean.Contains("pillar")) found = AllAssets.Find(a => a.DisplayName.ToLower().Contains("pillar"));
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

            if (asset.IsCheckPoint)
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
                ApplyTurretSettings(obj, ActiveTurretFireDelay, 1000f);
            }

            if (asset.IsJumper)
            {
                ApplyJumperForce(obj, ActiveJumperForce);
            }

            if (obj.GetComponentInChildren<Collider>() == null)
            {
                MeshFilter mf = obj.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    MeshCollider mc = obj.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
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

    // 3D Viewport FreeCam
    public static class EditorViewportCamera
    {
        private static GameObject _camInstance = null;
        public static Camera ViewportCamera = null;

        private static float _yaw = 0f;
        private static float _pitch = 0f;
        private static float _baseSpeed = 18f;

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
            if (Input.GetKey(KeyCode.LeftShift)) speed *= 2.5f;

            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += _camInstance.transform.forward;
            if (Input.GetKey(KeyCode.S)) move -= _camInstance.transform.forward;
            if (Input.GetKey(KeyCode.D)) move += _camInstance.transform.right;
            if (Input.GetKey(KeyCode.A)) move -= _camInstance.transform.right;
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) move += Vector3.up;
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl)) move -= Vector3.up;

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

    // 3D Holographic Placement Preview
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

            _ghostInstance.layer = 2; // Ignore Raycast
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

            Ray ray = Input.GetMouseButton(1)
                ? EditorViewportCamera.ViewportCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : EditorViewportCamera.ViewportCamera.ScreenPointToRay(Input.mousePosition);

            int raycastMask = ~LayerMask.GetMask("Ignore Raycast");
            RaycastHit hit;
            bool hasHit = Physics.Raycast(ray, out hit, 300f, raycastMask);

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

    // 3D Carousel Wheel Toolbar
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

        public static void CreateToolbar(Camera viewCam)
        {
            if (_wheelRoot != null || viewCam == null) return;

            _wheelRoot = new GameObject("Magical_Carousel_Wheel");
            _wheelRoot.transform.SetParent(viewCam.transform, false);

            _wheelRoot.transform.localPosition = new Vector3(0f, WheelCenterY, WheelDistanceZ);
            _wheelRoot.transform.localRotation = Quaternion.identity;
            _wheelRoot.layer = 2;

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
                if (_spawnedIcons[i] != null)
                {
                    _spawnedIcons[i].transform.localRotation = Quaternion.Euler(0f, 0f, -_currentAngle) * Quaternion.Euler(15f, Time.time * 30f, 0f);

                    Transform blades = _spawnedIcons[i].transform.Find("Blades");
                    if (blades != null)
                    {
                        blades.Rotate(Vector3.forward, 360f * Time.deltaTime, Space.Self);
                    }

                    bool isTop = (i == EditorSessionManager.SelectedAssetIndex);
                    float baseScale = EditorSessionManager.ActiveTabAssets[i].DefaultScale * IconScaleMultiplier;
                    _spawnedIcons[i].transform.localScale = Vector3.one * (isTop ? baseScale * 1.3f : baseScale);
                }
            }

            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !EditorSessionManager.IsBlockSelected)
            {
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

    // Persistence Service
    public static class LevelPersistenceService
    {
        // F5 now writes strictly into UserData/MyLevels/
        public static void SaveLevel(string filename)
        {
            string saveDir = MapBrowserService.MyLevelsDir;
            if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

            // Strip path or extension if passed
            string cleanName = Path.GetFileNameWithoutExtension(filename);
            if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "Default_Level";

            string path = Path.Combine(saveDir, $"{cleanName}.txt");
            List<string> lines = new List<string>();

            foreach (var obj in EditorSessionManager.PlacedObjects)
            {
                if (obj == null || !obj.activeSelf) continue;
                Vector3 pos = obj.transform.position;
                Quaternion rot = obj.transform.rotation;
                float scale = obj.transform.localScale.x;
                string name = obj.name.StartsWith("Custom_") ? obj.name.Substring(7) : obj.name;

                float param = 0f;
                if (EditorSessionManager.JumperForces.ContainsKey(obj)) param = EditorSessionManager.JumperForces[obj];
                else if (EditorSessionManager.TurbineSpeeds.ContainsKey(obj)) param = EditorSessionManager.TurbineSpeeds[obj];
                else if (EditorSessionManager.TurretFireDelays.ContainsKey(obj)) param = EditorSessionManager.TurretFireDelays[obj];

                lines.Add($"{name};{pos.x:F4};{pos.y:F4};{pos.z:F4};{scale:F4};{rot.x:F4};{rot.y:F4};{rot.z:F4};{rot.w:F4};{param:F2}");
            }

            File.WriteAllLines(path, lines.ToArray());
            EditorSessionManager.ShowNotification($"Saved {lines.Count} objects to {cleanName}.txt!");
            MelonLogger.Msg($">> Saved {lines.Count} objects to {path}!");

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
                string[] p = line.Split(';');
                if (p.Length < 5) continue;

                string rawName = p[0];
                Vector3 pos = new Vector3(float.Parse(p[1]), float.Parse(p[2]), float.Parse(p[3]));
                float scale = float.Parse(p[4]);

                Quaternion rot = Quaternion.identity;
                if (p.Length >= 9)
                {
                    rot = new Quaternion(float.Parse(p[5]), float.Parse(p[6]), float.Parse(p[7]), float.Parse(p[8]));
                }

                float customParam = (p.Length >= 10) ? float.Parse(p[9]) : 0f;

                GameObject obj = EditorSessionManager.SpawnAssetByName(rawName, pos, scale, rot);
                if (obj != null)
                {
                    if (customParam > 0f)
                    {
                        if (rawName.ToLower().Contains("jumper") || obj.name.ToLower().Contains("jumper"))
                        {
                            EditorSessionManager.ApplyJumperForce(obj, customParam);
                        }
                        else if (rawName.ToLower().Contains("helix") || obj.name.ToLower().Contains("helix"))
                        {
                            EditorSessionManager.ApplyTurbineSpeed(obj, customParam);
                        }
                        else if (rawName.ToLower().Contains("turret") || obj.name.ToLower().Contains("turret"))
                        {
                            EditorSessionManager.ApplyTurretSettings(obj, customParam, 1000f);
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

    // Scene Harvesting Service
    public static class SceneHarvestingService
    {
        public static void HarvestAllSceneModels()
        {
            EditorSessionManager.AllAssets.Clear();
            HashSet<string> seenMeshes = new HashSet<string>();

            MeshRenderer[] renderers = GameObject.FindObjectsOfType<MeshRenderer>();
            foreach (var r in renderers)
            {
                if (r.material != null)
                {
                    EditorSessionManager.CachedSceneMaterial = r.material;
                    break;
                }
            }

            GameObject ld = GameObject.Find("_LD");

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
                    Category = AssetCategory.Essentials,
                    IsJumper = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = 0f,
                    BaseRotation = Quaternion.Euler(35f, 0f, 0f)
                });
            }

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
                    Category = AssetCategory.Essentials,
                    IsCheckPoint = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = -0.32f,
                    BaseRotation = Quaternion.identity
                });
            }

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
                    Category = AssetCategory.Essentials,
                    IsTurret = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = 0f,
                    BaseRotation = Quaternion.identity
                });
            }

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
                    Category = AssetCategory.Essentials,
                    IsHelix = true,
                    DefaultScale = 1.0f,
                    VerticalOffset = 0f,
                    BaseRotation = Quaternion.identity
                });
            }

            MeshFilter[] allFilters = GameObject.FindObjectsOfType<MeshFilter>();

            foreach (MeshFilter mf in allFilters)
            {
                if (mf.sharedMesh == null) continue;
                string mName = mf.sharedMesh.name.Trim();
                string goName = mf.gameObject.name.ToLower();

                if (goName.Contains("helice") || goName.Contains("helix") || mName.ToLower().Contains("helice") || mName.ToLower().Contains("helix")) continue;
                if (goName.Contains("tourelle") || goName.Contains("turret") || mName.ToLower().Contains("tourelle")) continue;
                if (goName.Contains("laser") || mName.ToLower().Contains("laser")) continue;

                if (mName.ToLower().Contains("impostor") || mName.ToLower().Contains("hole")) continue;
                if (mName.ToLower().Contains("lod1") || mName.ToLower().Contains("lod2") || mName.ToLower().Contains("lod3")) continue;
                if (mName.ToLower().StartsWith("combined")) continue;

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

        private static AssetCategory CategorizeAsset(string friendly, string goName, Vector3 size)
        {
            if (goName.Contains("16x2x16") || friendly.Contains("Platform")) return AssetCategory.Essentials;
            if (goName.Contains("2x2x2") || friendly.Contains("Tech Crate")) return AssetCategory.Essentials;
            if (goName.Contains("4x4x4") || friendly.Contains("Pillar")) return AssetCategory.Essentials;
            if (goName.Contains("checkpoint") || friendly.Contains("Checkpoint")) return AssetCategory.Essentials;

            float maxDim = Mathf.Max(size.x, size.y, size.z);
            if (maxDim > 5.0f || goName.Contains("wall") || goName.Contains("beam") || goName.Contains("floor") || goName.Contains("monolith") || goName.Contains("decor"))
            {
                return AssetCategory.Extracted;
            }

            return AssetCategory.Misc;
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
                if (root.name == "_LA" || root.name == "_LD")
                    root.SetActive(false);
            }
        }
    }

    // Gameplay Initialization Hook
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