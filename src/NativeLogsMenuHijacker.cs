using Il2Cpp;
using Il2CppDeadCore.UI;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using SceneManager = UnityEngine.SceneManagement.SceneManager;

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 13: NATIVE LOGS MENU HIJACKER & METADATA DRAWER
    // =========================================================================

    public static class NativeLogsMenuHijacker
    {
        private static LogsMenu _lastMenu = null;

        // Native Bottom Bar Buttons
        private static GameObject _nativeBackButtonTemplate = null;
        private static GameObject _nativePlayButton = null;
        private static GameObject _nativeCreateButton = null;
        private static GameObject _nativeDeleteButton = null;

        // Custom Row Tracking
        private static readonly List<GameObject> _spawnedRowObjects = new List<GameObject>();
        public static int SpawnedRowCount => _spawnedRowObjects.Count;

        // Native Tabs
        private static Toggle _cachedMyLevelsToggle = null;
        private static Toggle _cachedCommunityToggle = null;

        // Header Bar Filter & Search Controls
        private static GameObject _topFilterRoot = null;
        private static TMP_InputField _searchFilterInput = null;
        private static string _currentSearchQuery = "";
        private static int _localSortMode = 0; // 0=Name, 1=Date Modified
        private static int _communitySortMode = 0; // 0=Downloads, 1=Newest, 2=Name
        private static bool _communityShowInstalledOnly = false;
        private static TMP_Text _sortButtonText = null;
        private static Button _communityFilterBtn = null;
        private static TMP_Text _communityFilterBtnText = null;

        // Expandable Metadata Drawer
        private static GameObject _drawerMaskContainer = null;
        private static GameObject _nativeMetadataRoot = null;
        private static RectTransform _drawerRt = null;
        private static GameObject _drawerToggleButton = null;
        private static TMP_Text _drawerToggleText = null;
        private static bool _isDrawerExpanded = false;
        private static float _drawerCurrentT = 0f; // 0.0 = Collapsed, 1.0 = Expanded
        private static float _drawerTargetT = 0f;

        // Metadata Fields
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

        // Community Detail View
        private static GameObject _communityCardRoot = null;

        public static readonly string[] DifficultyNames = new string[] { "Very Easy", "Easy", "Normal", "Hard", "Expert" };
        public static readonly Color[] DifficultyColors = new Color[]
        {
            new Color(0.2f, 0.95f, 0.4f),
            new Color(0.1f, 0.85f, 1.0f),
            new Color(0.3f, 0.65f, 1.0f),
            new Color(1.0f, 0.55f, 0.1f),
            new Color(0.95f, 0.2f, 0.2f)
        };

        // =========================================================================
        // METADATA SERIALIZATION
        // =========================================================================

        public static LevelMetadata ReadLevelMetadata(string fullPath, string fallbackTitle)
        {
            LevelMetadata meta = new LevelMetadata { Title = fallbackTitle };
            if (!File.Exists(fullPath)) return meta;

            try
            {
                string[] lines = File.ReadAllLines(fullPath);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string trimmed = line.Trim();

                    if (trimmed.StartsWith("#TITLE:", StringComparison.OrdinalIgnoreCase)) meta.Title = trimmed.Substring(7).Trim();
                    else if (trimmed.StartsWith("#AUTHOR:", StringComparison.OrdinalIgnoreCase)) meta.Author = trimmed.Substring(8).Trim();
                    else if (trimmed.StartsWith("#DIFFICULTY:", StringComparison.OrdinalIgnoreCase)) meta.Difficulty = trimmed.Substring(12).Trim();
                    else if (trimmed.StartsWith("#DESC:", StringComparison.OrdinalIgnoreCase)) meta.Description = trimmed.Substring(6).Trim();
                    else if (trimmed.StartsWith("#SCENE:", StringComparison.OrdinalIgnoreCase)) meta.StagingScene = trimmed.Substring(7).Trim();
                    else if (!trimmed.StartsWith("#")) break;
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
                    ? _titleInput.text.Trim() : Path.GetFileNameWithoutExtension(fullPath);

                string author = (_authorInput != null && !string.IsNullOrWhiteSpace(_authorInput.text))
                    ? _authorInput.text.Trim() : "Unknown";

                string desc = (_descInput != null) ? _descInput.text.Trim() : "";
                string diff = DifficultyNames[_selectedDifficultyIndex];
                string scene = !string.IsNullOrEmpty(MapBrowserService.SelectedStagingScene) ? MapBrowserService.SelectedStagingScene : "level01_Spark01";

                string[] allLines = File.ReadAllLines(fullPath);
                List<string> objectLines = new List<string>();

                for (int i = 0; i < allLines.Length; i++)
                {
                    string l = allLines[i];
                    if (string.IsNullOrWhiteSpace(l)) continue;
                    if (!l.Trim().StartsWith("#")) objectLines.Add(l);
                }

                List<string> finalLines = new List<string>
                {
                    $"#TITLE: {title}",
                    $"#AUTHOR: {author}",
                    $"#DIFFICULTY: {diff}",
                    $"#DESC: {desc}",
                    $"#SCENE: {scene}"
                };
                finalLines.AddRange(objectLines);

                File.WriteAllLines(fullPath, finalLines.ToArray());

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

        // =========================================================================
        // SCENE SELECTOR & ROW UTILITIES
        // =========================================================================

        public static void CreateSceneSelectorRow(Transform parent, TMP_Text sampleTmp, float height = 32f)
        {
            GameObject row = new GameObject("Row_SceneSelector", Il2CppType.Of<RectTransform>());
            row.transform.SetParent(parent, false);

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            le.flexibleWidth = 1f;

            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            GameObject labelObj = new GameObject("Label", Il2CppType.Of<RectTransform>());
            labelObj.transform.SetParent(row.transform, false);

            LayoutElement labelLe = labelObj.AddComponent<LayoutElement>();
            labelLe.preferredWidth = 130f;
            labelLe.minWidth = 130f;
            labelLe.flexibleWidth = 0f;

            TMP_Text labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
            CopyNativeFont(sampleTmp, labelTmp);
            labelTmp.fontSize = 14f;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.color = new Color(0.2f, 0.82f, 1f, 1f);
            labelTmp.text = "BASE SCENE";
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;

            GameObject container = new GameObject("SceneContainer", Il2CppType.Of<RectTransform>());
            container.transform.SetParent(row.transform, false);

            LayoutElement cLe = container.AddComponent<LayoutElement>();
            cLe.flexibleWidth = 1f;

            HorizontalLayoutGroup chlg = container.AddComponent<HorizontalLayoutGroup>();
            chlg.spacing = 6f;
            chlg.childControlWidth = true;
            chlg.childControlHeight = true;
            chlg.childForceExpandWidth = false;
            chlg.childForceExpandHeight = true;

            Button pb = CreateNativeInlineButton(container.transform, "Btn_PrevScene", "<", () => CycleStagingScene(-1), 36f, sampleTmp, null, 32f, 18f);
            if (pb != null) pb.GetComponent<LayoutElement>().preferredWidth = 36f;

            GameObject displayObj = new GameObject("SceneDisplay", Il2CppType.Of<RectTransform>());
            displayObj.transform.SetParent(container.transform, false);
            LayoutElement dle = displayObj.AddComponent<LayoutElement>();
            dle.flexibleWidth = 1f;
            displayObj.AddComponent<Image>().color = new Color(0.06f, 0.10f, 0.18f, 0.92f);

            _sceneLabelText = CreateNativeLabel(displayObj.transform, MapBrowserService.SelectedStagingScene, 14f, FontStyles.Bold, new Color(0.2f, 0.95f, 0.4f), TextAlignmentOptions.Center, sampleTmp);

            Button nb = CreateNativeInlineButton(container.transform, "Btn_NextScene", ">", () => CycleStagingScene(1), 36f, sampleTmp, null, 32f, 18f);
            if (nb != null) nb.GetComponent<LayoutElement>().preferredWidth = 36f;
        }

        private static void CycleStagingScene(int dir)
        {
            var list = MapBrowserService.AvailableStagingScenes;
            if (list == null || list.Count <= 1) return;

            int idx = list.IndexOf(MapBrowserService.SelectedStagingScene);
            if (idx < 0) idx = 0;

            idx = (idx + dir + list.Count) % list.Count;
            MapBrowserService.SelectedStagingScene = list[idx];

            if (_sceneLabelText != null) _sceneLabelText.text = MapBrowserService.SelectedStagingScene;
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

        // =========================================================================
        // NATIVE LOGS MENU LIFECYCLE & INJECTION
        // =========================================================================

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
                    if (tmp == null || !tmp.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(tmp.text)) continue;
                    if (tmp.GetComponentInParent<LogsMenu>() != null) continue;

                    string t = tmp.text.Trim().ToLowerInvariant();
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

            bool isCommunity = (DeadCoreLevelEditorMod.ActiveTab == 1);
            string allowedPrefix = isCommunity ? "OnlineMap_" : "CustomMap_";

            int count = menu._logsButtonRoot.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform child = menu._logsButtonRoot.GetChild(i);
                if (child != null)
                {
                    if (child.name.StartsWith(allowedPrefix))
                        child.gameObject.SetActive(true);
                    else
                        child.gameObject.SetActive(false);
                }
            }
        }

        public static void EnforceBottomBarLabels()
        {
            float dt = Time.unscaledDeltaTime;

            if (_saveFeedbackTimer > 0f)
            {
                _saveFeedbackTimer -= dt;
                if (_saveFeedbackTimer <= 0f && _saveBtnText != null)
                {
                    _saveBtnText.text = "SAVE DETAILS";
                    _saveBtnText.color = Color.white;
                }
            }

            UpdateDrawerTransition(dt);

            if (_nativePlayButton != null && _nativePlayButton.activeInHierarchy) SetButtonText(_nativePlayButton, "Play", 34f);
            if (_nativeCreateButton != null && _nativeCreateButton.activeInHierarchy) SetButtonText(_nativeCreateButton, "+ New", 34f);
            if (_nativeDeleteButton != null && _nativeDeleteButton.activeInHierarchy) SetButtonText(_nativeDeleteButton, "Delete", 34f);
        }

        // =========================================================================
        // TRANSFORM LOGS MENU (MAIN ENTRY POINT)
        // =========================================================================

        public static void TransformLogsMenu(LogsMenu menu, int currentTab)
        {
            if (menu == null) return;

            if (_lastMenu != menu)
            {
                _lastMenu = menu;
                _topFilterRoot = null;
                _nativeMetadataRoot = null;
                _drawerMaskContainer = null;
                _drawerToggleButton = null;
                _communityCardRoot = null;
                _searchFilterInput = null;
                _sortButtonText = null;
            }

            MapBrowserService.EnsureDirectories();
            MapBrowserService.RefreshFiles();

            if (menu._logTitle != null)
            {
                menu._logTitle.text = "Level Editor";
                menu._logTitle.alignment = TextAlignmentOptions.Center;
                DisableLocalizationScripts(menu._logTitle.gameObject);
            }

            RebrandAndTrimNativeTabs(menu);
            BuildHeaderSearchAndFilterBar(menu, currentTab);

            _spawnedRowObjects.Clear();
            if (menu._logsButtonRoot != null)
            {
                for (int i = menu._logsButtonRoot.childCount - 1; i >= 0; i--)
                {
                    Transform child = menu._logsButtonRoot.GetChild(i);
                    if (child != null)
                    {
                        if (child.name.StartsWith("CustomMap_") || child.name.StartsWith("OnlineMap_"))
                            GameObject.DestroyImmediate(child.gameObject);
                        else
                            child.gameObject.SetActive(false);
                    }
                }
            }

            if (menu._logPrefab == null || menu._logsButtonRoot == null) return;
            menu._logPrefab.gameObject.SetActive(false);

            // TAB 0: MY LEVELS (LOCAL)
            if (currentTab == 0)
            {
                if (_communityCardRoot != null) _communityCardRoot.SetActive(false);
                if (_nativeMetadataRoot != null) _nativeMetadataRoot.SetActive(_isDrawerExpanded);
                if (_drawerToggleButton != null) _drawerToggleButton.SetActive(true);

                if (_nativePlayButton != null) _nativePlayButton.SetActive(true);
                if (_nativeCreateButton != null) _nativeCreateButton.SetActive(true);
                if (_nativeDeleteButton != null) _nativeDeleteButton.SetActive(true);

                List<string> fileList = new List<string>();
                if (Directory.Exists(MapBrowserService.MyLevelsDir))
                    fileList.AddRange(Directory.GetFiles(MapBrowserService.MyLevelsDir, "*.txt"));

                if (fileList.Count == 0)
                {
                    MapBrowserService.EnsureDirectories();
                    fileList.Add(Path.Combine(MapBrowserService.MyLevelsDir, "Default_Level.txt"));
                }

                if (_localSortMode == 0)
                {
                    fileList.Sort((a, b) => string.Compare(Path.GetFileNameWithoutExtension(a), Path.GetFileNameWithoutExtension(b), StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    fileList.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
                }

                int displayIndex = 1;
                for (int i = 0; i < fileList.Count; i++)
                {
                    string filePath = fileList[i];
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    LevelMetadata meta = ReadLevelMetadata(filePath, fileName);

                    if (!string.IsNullOrEmpty(_currentSearchQuery))
                    {
                        string q = _currentSearchQuery.ToLowerInvariant();
                        if (!meta.Title.ToLowerInvariant().Contains(q) &&
                            !meta.Author.ToLowerInvariant().Contains(q) &&
                            !fileName.ToLowerInvariant().Contains(q))
                        {
                            continue;
                        }
                    }

                    GameObject itemObj = GameObject.Instantiate(menu._logPrefab.gameObject, menu._logsButtonRoot.transform);
                    itemObj.name = $"CustomMap_{fileName}";
                    itemObj.SetActive(true);
                    DisableLocalizationScripts(itemObj);

                    SetupRowVisuals(itemObj, displayIndex.ToString("D2"), meta.Title);
                    displayIndex++;

                    string capturedPath = filePath;
                    string capturedName = fileName;
                    GameObject capturedObj = itemObj;

                    Button btn = itemObj.GetComponent<Button>() ?? itemObj.AddComponent<Button>();
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener((Action)(() => OnLevelSelected(menu, capturedPath, capturedName, capturedObj)));
                    _spawnedRowObjects.Add(itemObj);

                    if (_spawnedRowObjects.Count == 1 || capturedPath == MapBrowserService.SelectedMapPath)
                        OnLevelSelected(menu, capturedPath, capturedName, capturedObj);
                }

                if (_spawnedRowObjects.Count == 0)
                {
                    GameObject emptyObj = GameObject.Instantiate(menu._logPrefab.gameObject, menu._logsButtonRoot.transform);
                    emptyObj.name = "CustomMap_Empty";
                    emptyObj.SetActive(true);
                    DisableLocalizationScripts(emptyObj);
                    SetupRowVisuals(emptyObj, "--", "No matching local levels found.");
                    _spawnedRowObjects.Add(emptyObj);
                }
            }
            // TAB 1: COMMUNITY (CLOUD)
            else
            {
                if (_nativeMetadataRoot != null) _nativeMetadataRoot.SetActive(false);
                if (_drawerToggleButton != null) _drawerToggleButton.SetActive(false);
                if (_nativeCreateButton != null) _nativeCreateButton.SetActive(false);
                if (_nativeDeleteButton != null) _nativeDeleteButton.SetActive(false);

                var cloudLevels = new List<RemoteLevelItem>(CommunityLevelService.CachedCommunityLevels);

                if (cloudLevels == null || cloudLevels.Count == 0)
                {
                    GameObject emptyObj = GameObject.Instantiate(menu._logPrefab.gameObject, menu._logsButtonRoot.transform);
                    emptyObj.name = "OnlineMap_Empty";
                    emptyObj.SetActive(true);
                    DisableLocalizationScripts(emptyObj);
                    SetupRowVisuals(emptyObj, "--", CommunityLevelService.IsFetching ? "Fetching community levels..." : "No levels found.");
                    _spawnedRowObjects.Add(emptyObj);
                    SetupBottomBarButtons(menu);
                    return;
                }

                if (_communitySortMode == 0)
                    cloudLevels.Sort((a, b) => b.downloads.CompareTo(a.downloads));
                else if (_communitySortMode == 1)
                    cloudLevels.Sort((a, b) => b.created_at.CompareTo(a.created_at));
                else
                    cloudLevels.Sort((a, b) => string.Compare(a.title, b.title, StringComparison.OrdinalIgnoreCase));

                int displayIndex = 1;
                for (int i = 0; i < cloudLevels.Count; i++)
                {
                    RemoteLevelItem item = cloudLevels[i];
                    string expectedLocalPath = Path.Combine(MapBrowserService.DownloadedLevelsDir, $"{item.title}_{item.id}.txt");
                    bool isDownloaded = File.Exists(expectedLocalPath);

                    if (_communityShowInstalledOnly && !isDownloaded)
                        continue;

                    if (!string.IsNullOrEmpty(_currentSearchQuery))
                    {
                        string q = _currentSearchQuery.ToLowerInvariant();
                        if (!item.title.ToLowerInvariant().Contains(q) &&
                            !item.author.ToLowerInvariant().Contains(q))
                            continue;
                    }

                    GameObject itemObj = GameObject.Instantiate(menu._logPrefab.gameObject, menu._logsButtonRoot.transform);
                    itemObj.name = $"OnlineMap_{item.id}";
                    itemObj.SetActive(true);
                    DisableLocalizationScripts(itemObj);

                    string statusTag = isDownloaded ? "<color=#00E5FF>[Installed]</color> " : "";
                    SetupRowVisuals(itemObj, displayIndex.ToString("D2"), statusTag + item.title);
                    displayIndex++;

                    RemoteLevelItem capturedItem = item;
                    GameObject capturedObj = itemObj;

                    Button btn = itemObj.GetComponent<Button>() ?? itemObj.AddComponent<Button>();
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener((Action)(() => OnCommunityLevelSelected(menu, capturedItem, capturedObj)));
                    _spawnedRowObjects.Add(itemObj);

                    if (_spawnedRowObjects.Count == 1)
                        OnCommunityLevelSelected(menu, capturedItem, capturedObj);
                }

                if (_spawnedRowObjects.Count == 0)
                {
                    GameObject emptyObj = GameObject.Instantiate(menu._logPrefab.gameObject, menu._logsButtonRoot.transform);
                    emptyObj.name = "OnlineMap_Empty";
                    emptyObj.SetActive(true);
                    DisableLocalizationScripts(emptyObj);
                    SetupRowVisuals(emptyObj, "--", "No matching community levels.");
                    _spawnedRowObjects.Add(emptyObj);
                }
            }

            SetupBottomBarButtons(menu);
        }

        public static void TransformLogsMenu(LogsMenu menu)
        {
            TransformLogsMenu(menu, DeadCoreLevelEditorMod.ActiveTab);
        }

        // =========================================================================
        // TOP HEADER SEARCH & FILTER BAR (SUBSTANTIAL, PROPORTIONAL SIZING)
        // =========================================================================

        private static void BuildHeaderSearchAndFilterBar(LogsMenu menu, int currentTab)
        {
            TMP_Text sampleTmp = menu._shortDesc != null ? menu._shortDesc : menu.GetComponentInChildren<TMP_Text>(true);

            Transform headerContainer = (menu._logTitle != null && menu._logTitle.transform.parent != null)
                ? menu._logTitle.transform.parent
                : (menu._logBigPicture != null ? menu._logBigPicture.transform.parent : menu.transform);

            if (_topFilterRoot == null || _topFilterRoot.Equals(null))
            {
                _topFilterRoot = new GameObject("HeaderFilterBar", Il2CppType.Of<RectTransform>());
                _topFilterRoot.transform.SetParent(headerContainer, false);
                _topFilterRoot.transform.SetAsLastSibling();

                RectTransform frt = _topFilterRoot.GetComponent<RectTransform>();
                frt.anchorMin = Vector2.zero;
                frt.anchorMax = Vector2.one;
                frt.sizeDelta = Vector2.zero;
                frt.anchoredPosition = Vector2.zero;

                LayoutElement rootLe = _topFilterRoot.AddComponent<LayoutElement>();
                rootLe.ignoreLayout = true;

                // 1. LEFT SIDE: SEARCH BOX (Enlarged to 340x44px, 18px font)
                GameObject searchObj = new GameObject("SearchInputBox", Il2CppType.Of<RectTransform>());
                searchObj.transform.SetParent(_topFilterRoot.transform, false);

                RectTransform srt = searchObj.GetComponent<RectTransform>();
                srt.anchorMin = new Vector2(0f, 0.5f);
                srt.anchorMax = new Vector2(0f, 0.5f);
                srt.pivot = new Vector2(0f, 0.5f);
                srt.anchoredPosition = new Vector2(18f, 0f);
                srt.sizeDelta = new Vector2(340f, 44f);

                searchObj.AddComponent<Image>().color = new Color(0.08f, 0.12f, 0.20f, 0.98f);

                GameObject textObj = new GameObject("Text", Il2CppType.Of<RectTransform>());
                textObj.transform.SetParent(searchObj.transform, false);
                RectTransform trt = textObj.GetComponent<RectTransform>();
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(14f, 0f);
                trt.offsetMax = new Vector2(-14f, 0f);

                TMP_Text inTmp = textObj.AddComponent<TextMeshProUGUI>();
                CopyNativeFont(sampleTmp, inTmp);
                inTmp.fontSize = 18f;
                inTmp.color = Color.white;
                inTmp.alignment = TextAlignmentOptions.MidlineLeft;

                // Placeholder
                GameObject phObj = new GameObject("Placeholder", Il2CppType.Of<RectTransform>());
                phObj.transform.SetParent(searchObj.transform, false);
                RectTransform phrt = phObj.GetComponent<RectTransform>();
                phrt.anchorMin = Vector2.zero;
                phrt.anchorMax = Vector2.one;
                phrt.offsetMin = new Vector2(14f, 0f);
                phrt.offsetMax = new Vector2(-14f, 0f);

                TMP_Text phTmp = phObj.AddComponent<TextMeshProUGUI>();
                CopyNativeFont(sampleTmp, phTmp);
                phTmp.text = "🔍  Search levels...";
                phTmp.fontSize = 17f;
                phTmp.fontStyle = FontStyles.Italic;
                phTmp.color = new Color(0.55f, 0.65f, 0.80f, 0.65f);
                phTmp.alignment = TextAlignmentOptions.MidlineLeft;

                _searchFilterInput = searchObj.AddComponent<TMP_InputField>();
                _searchFilterInput.textViewport = trt;
                _searchFilterInput.textComponent = inTmp;
                _searchFilterInput.placeholder = phTmp;
                _searchFilterInput.pointSize = 18f;
                _searchFilterInput.onValueChanged.AddListener((Action<string>)((q) =>
                {
                    _currentSearchQuery = q;
                    TransformLogsMenu(menu, DeadCoreLevelEditorMod.ActiveTab);
                }));

                // 2. RIGHT SIDE: CONTROLS CONTAINER (Enlarged to 420x46px)
                GameObject rightControls = new GameObject("RightFilterControls", Il2CppType.Of<RectTransform>());
                rightControls.transform.SetParent(_topFilterRoot.transform, false);

                RectTransform rcrt = rightControls.GetComponent<RectTransform>();
                rcrt.anchorMin = new Vector2(1f, 0.5f);
                rcrt.anchorMax = new Vector2(1f, 0.5f);
                rcrt.pivot = new Vector2(1f, 0.5f);
                rcrt.anchoredPosition = new Vector2(-18f, 0f);
                rcrt.sizeDelta = new Vector2(420f, 46f);

                HorizontalLayoutGroup rhlg = rightControls.AddComponent<HorizontalLayoutGroup>();
                rhlg.spacing = 10f;
                rhlg.childAlignment = TextAnchor.MiddleRight;
                rhlg.childControlWidth = false;
                rhlg.childControlHeight = true;
                rhlg.childForceExpandWidth = false;
                rhlg.childForceExpandHeight = true;

                // Community Filter (All / Installed) Button
                _communityFilterBtn = CreateNativeInlineButton(rightControls.transform, "Btn_FilterInstalled", "Filter: All", () =>
                {
                    _communityShowInstalledOnly = !_communityShowInstalledOnly;
                    if (_communityFilterBtnText != null)
                        _communityFilterBtnText.text = _communityShowInstalledOnly ? "Filter: Installed" : "Filter: All";
                    TransformLogsMenu(menu, DeadCoreLevelEditorMod.ActiveTab);
                }, 160f, sampleTmp, new Color(0.12f, 0.22f, 0.35f, 0.95f), 44f, 16f);

                if (_communityFilterBtn != null)
                    _communityFilterBtnText = _communityFilterBtn.GetComponentInChildren<TMP_Text>();

                // Sort Button
                Button sortBtn = CreateNativeInlineButton(rightControls.transform, "Btn_ToggleSort", "Sort: Name", () =>
                {
                    if (DeadCoreLevelEditorMod.ActiveTab == 0)
                    {
                        _localSortMode = (_localSortMode + 1) % 2;
                        if (_sortButtonText != null)
                            _sortButtonText.text = _localSortMode == 0 ? "Sort: Name" : "Sort: Date";
                    }
                    else
                    {
                        _communitySortMode = (_communitySortMode + 1) % 3;
                        if (_sortButtonText != null)
                            _sortButtonText.text = _communitySortMode == 0 ? "Sort: Downloads" : (_communitySortMode == 1 ? "Sort: Newest" : "Sort: Name");
                    }
                    TransformLogsMenu(menu, DeadCoreLevelEditorMod.ActiveTab);
                }, 175f, sampleTmp, new Color(0.15f, 0.20f, 0.32f, 0.95f), 44f, 16f);

                if (sortBtn != null) _sortButtonText = sortBtn.GetComponentInChildren<TMP_Text>();
            }

            if (_communityFilterBtn != null)
                _communityFilterBtn.gameObject.SetActive(currentTab == 1);

            if (_communityFilterBtnText != null)
                _communityFilterBtnText.text = _communityShowInstalledOnly ? "Filter: Installed" : "Filter: All";

            if (_sortButtonText != null)
            {
                if (currentTab == 0)
                    _sortButtonText.text = _localSortMode == 0 ? "Sort: Name" : "Sort: Date";
                else
                    _sortButtonText.text = _communitySortMode == 0 ? "Sort: Downloads" : (_communitySortMode == 1 ? "Sort: Newest" : "Sort: Name");
            }
        }

        private static void SetupRowVisuals(GameObject rowObj, string idText, string titleText)
        {
            LogToggle lt = rowObj.GetComponent<LogToggle>();
            if (lt != null)
            {
                if (lt._idLabel != null) { lt._idLabel.text = idText; lt._idLabel.enableWordWrapping = false; }
                if (lt._nameLabel != null) { lt._nameLabel.text = titleText; lt._nameLabel.enableWordWrapping = false; }
                GameObject.DestroyImmediate(lt);
            }

            TMP_Text[] tmps = rowObj.GetComponentsInChildren<TMP_Text>(true);
            if (tmps.Length >= 2)
            {
                tmps[0].text = idText; tmps[0].enableWordWrapping = false;
                tmps[1].text = titleText; tmps[1].enableWordWrapping = false;
            }

            Toggle tog = rowObj.GetComponent<Toggle>();
            if (tog != null) GameObject.DestroyImmediate(tog);
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

        // =========================================================================
        // SELECTION & EXPANDABLE RIGHT-SIDE METADATA DRAWER
        // =========================================================================

        private static void OnLevelSelected(LogsMenu menu, string fullPath, string fileName, GameObject selectedRowObj)
        {
            if (_communityCardRoot != null) _communityCardRoot.SetActive(false);

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
            MapBrowserService.SelectedStagingScene = (!string.IsNullOrEmpty(meta.StagingScene) && MapBrowserService.AvailableStagingScenes.Contains(meta.StagingScene))
                ? meta.StagingScene : "level01_Spark01";

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

            bool isMyLevels = (DeadCoreLevelEditorMod.ActiveTab == 0);

            if (menu._shortDesc != null) menu._shortDesc.gameObject.SetActive(false);
            if (menu._longDesc != null) menu._longDesc.gameObject.SetActive(false);

            menu._logBigPicture.gameObject.SetActive(true);
            menu._logBigPicture.color = Color.white;

            if (_communityCardRoot != null) _communityCardRoot.SetActive(false);

            TMP_Text sampleText = menu._shortDesc != null ? menu._shortDesc : menu.GetComponentInChildren<TMP_Text>(true);

            // 1. Ensure Clipping Viewport Mask over _logBigPicture
            if (_drawerMaskContainer == null || _drawerMaskContainer.Equals(null))
            {
                _drawerMaskContainer = new GameObject("MetadataDrawerMask", Il2CppType.Of<RectTransform>());
                _drawerMaskContainer.transform.SetParent(menu._logBigPicture.transform, false);

                RectTransform maskRt = _drawerMaskContainer.GetComponent<RectTransform>();
                maskRt.anchorMin = Vector2.zero;
                maskRt.anchorMax = Vector2.one;
                maskRt.sizeDelta = Vector2.zero;
                _drawerMaskContainer.AddComponent<RectMask2D>();
            }

            // 2. Expand/Collapse Toggle Button (Enlarged to 160x42px, 16px font)
            if (_drawerToggleButton == null || _drawerToggleButton.Equals(null))
            {
                _drawerToggleButton = new GameObject("Btn_ToggleDrawer", Il2CppType.Of<RectTransform>());
                _drawerToggleButton.transform.SetParent(menu._logBigPicture.transform, false);

                RectTransform tbrt = _drawerToggleButton.GetComponent<RectTransform>();
                tbrt.anchorMin = new Vector2(1f, 1f);
                tbrt.anchorMax = new Vector2(1f, 1f);
                tbrt.pivot = new Vector2(1f, 1f);
                tbrt.sizeDelta = new Vector2(160f, 42f);
                tbrt.anchoredPosition = new Vector2(-16f, -16f);

                _drawerToggleButton.AddComponent<Image>().color = new Color(0.12f, 0.45f, 0.85f, 0.95f);
                Button btn = _drawerToggleButton.AddComponent<Button>();

                _drawerToggleText = CreateNativeLabel(_drawerToggleButton.transform, "[ < EDIT INFO ]", 16f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, sampleText);

                btn.onClick.AddListener((Action)(() =>
                {
                    _isDrawerExpanded = !_isDrawerExpanded;
                    _drawerTargetT = _isDrawerExpanded ? 1f : 0f;
                    if (_drawerToggleText != null)
                        _drawerToggleText.text = _isDrawerExpanded ? "[ CLOSE > ]" : "[ < EDIT INFO ]";
                }));
            }

            _drawerToggleButton.SetActive(isMyLevels);
            _drawerToggleButton.transform.SetAsLastSibling();

            // 3. Build Metadata Panel (Docked to Right Half)
            if (_nativeMetadataRoot == null || _nativeMetadataRoot.Equals(null))
            {
                _nativeMetadataRoot = new GameObject("Native_Metadata_Root", Il2CppType.Of<RectTransform>());
                _nativeMetadataRoot.transform.SetParent(_drawerMaskContainer.transform, false);

                _drawerRt = _nativeMetadataRoot.GetComponent<RectTransform>();
                _drawerRt.anchorMin = new Vector2(0.40f, 0f);
                _drawerRt.anchorMax = new Vector2(1f, 1f);
                _drawerRt.pivot = new Vector2(1f, 0.5f);
                _drawerRt.sizeDelta = Vector2.zero;

                Image bgImg = _nativeMetadataRoot.AddComponent<Image>();
                bgImg.color = new Color(0.04f, 0.07f, 0.12f, 0.94f);

                VerticalLayoutGroup vlg = _nativeMetadataRoot.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(16, 16, 48, 12);
                vlg.spacing = 6f;
                vlg.childControlWidth = true;
                vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;

                CreateNativeInputRow(_nativeMetadataRoot.transform, sampleText, "LEVEL TITLE", 32f, 20f, out _titleInput);
                CreateNativeInputRow(_nativeMetadataRoot.transform, sampleText, "AUTHOR", 32f, 20f, out _authorInput);
                CreateDifficultyRow(_nativeMetadataRoot.transform, sampleText, 32f);
                CreateSceneSelectorRow(_nativeMetadataRoot.transform, sampleText, 32f);
                CreateNativeInputRow(_nativeMetadataRoot.transform, sampleText, "DESCRIPTION", 48f, 18f, out _descInput, true);
                CreateLevelStatsHUD(_nativeMetadataRoot.transform, sampleText, 38f);
                CreateNativeSaveButton(_nativeMetadataRoot.transform, sampleText, 38f);

                GameObject spacer = new GameObject("BottomSpacer", Il2CppType.Of<RectTransform>());
                spacer.transform.SetParent(_nativeMetadataRoot.transform, false);
                LayoutElement spLe = spacer.AddComponent<LayoutElement>();
                spLe.flexibleHeight = 1f;

                _drawerCurrentT = _isDrawerExpanded ? 1f : 0f;
                _drawerTargetT = _drawerCurrentT;
                ApplyDrawerNormalizedPosition(_drawerCurrentT);
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

        private static void UpdateDrawerTransition(float dt)
        {
            if (_drawerRt == null || _nativeMetadataRoot == null) return;

            if (Mathf.Abs(_drawerCurrentT - _drawerTargetT) > 0.001f)
            {
                _drawerCurrentT = Mathf.MoveTowards(_drawerCurrentT, _drawerTargetT, dt * 4.2f);
                ApplyDrawerNormalizedPosition(_drawerCurrentT);

                if (_drawerCurrentT <= 0.002f && !_isDrawerExpanded)
                {
                    _nativeMetadataRoot.SetActive(false);
                }
                else if (!_nativeMetadataRoot.activeSelf && DeadCoreLevelEditorMod.ActiveTab == 0)
                {
                    _nativeMetadataRoot.SetActive(true);
                }
            }
        }

        private static void ApplyDrawerNormalizedPosition(float t)
        {
            if (_drawerRt == null) return;
            float smooth = Mathf.SmoothStep(0f, 1f, t);
            float drawerWidth = _drawerRt.rect.width > 50f ? _drawerRt.rect.width : 500f;
            _drawerRt.anchoredPosition = new Vector2(drawerWidth * (1f - smooth), 0f);
        }

        // =========================================================================
        // COMMUNITY TAB (REMOTE LEVEL CARDS & DETAIL VIEW)
        // =========================================================================

        private static void OnCommunityLevelSelected(LogsMenu menu, RemoteLevelItem item, GameObject selectedRowObj)
        {
            if (menu == null || item == null) return;

            string localTxtPath = Path.Combine(MapBrowserService.DownloadedLevelsDir, $"{item.title}_{item.id}.txt");
            string localPngPath = Path.Combine(MapBrowserService.DownloadedLevelsDir, $"{item.title}_{item.id}.png");
            bool isDownloaded = File.Exists(localTxtPath);

            MapBrowserService.SelectedMapPath = isDownloaded ? localTxtPath : "";
            MapBrowserService.SelectedMapName = isDownloaded ? Path.GetFileNameWithoutExtension(localTxtPath) : item.title;
            MapBrowserService.SelectedStagingScene = !string.IsNullOrEmpty(item.staging_scene) ? item.staging_scene : "level01_Spark01";

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
                if (File.Exists(localPngPath))
                {
                    Texture2D tex = ThumbnailCaptureService.LoadLevelTexture(localPngPath);
                    if (tex != null) menu._logBigPicture.texture = tex;
                }
                else
                {
                    MelonCoroutines.Start(FetchRemoteThumbnailCoroutine(item.id, menu._logBigPicture));
                }
                menu._logBigPicture.gameObject.SetActive(true);
            }

            BuildCommunityDetailCard(menu, item, isDownloaded);
        }

        private static IEnumerator FetchRemoteThumbnailCoroutine(string levelId, RawImage targetImg)
        {
            var downloadTask = Task.Run(async () =>
            {
                try
                {
                    using var client = new HttpClient();
                    var resp = await client.GetAsync($"{CommunityLevelService.BaseApiUrl}/api/levels/{levelId}/thumbnail");
                    if (resp.IsSuccessStatusCode)
                    {
                        return await resp.Content.ReadAsByteArrayAsync();
                    }
                }
                catch { }
                return null;
            });

            while (!downloadTask.IsCompleted)
            {
                yield return null;
            }

            if (!downloadTask.IsFaulted && downloadTask.Result != null && downloadTask.Result.Length > 0 && targetImg != null)
            {
                try
                {
                    byte[] rawBytes = downloadTask.Result;
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (ImageConversion.LoadImage(tex, rawBytes))
                    {
                        targetImg.texture = tex;
                        targetImg.color = Color.white;
                    }
                }
                catch { }
            }
        }

        private static void BuildCommunityDetailCard(LogsMenu menu, RemoteLevelItem item, bool isDownloaded)
        {
            if (_nativeMetadataRoot != null) _nativeMetadataRoot.SetActive(false);
            if (_drawerToggleButton != null) _drawerToggleButton.SetActive(false);

            if (_communityCardRoot == null || _communityCardRoot.Equals(null))
            {
                _communityCardRoot = new GameObject("Community_Detail_Root", Il2CppType.Of<RectTransform>());
                _communityCardRoot.transform.SetParent(menu._logBigPicture.transform, false);

                RectTransform rootRt = _communityCardRoot.GetComponent<RectTransform>();
                rootRt.anchorMin = Vector2.zero;
                rootRt.anchorMax = Vector2.one;
                rootRt.pivot = new Vector2(0.5f, 0.5f);
                rootRt.offsetMin = Vector2.zero;
                rootRt.offsetMax = Vector2.zero;

                _communityCardRoot.AddComponent<Image>().color = new Color(0.03f, 0.05f, 0.09f, 0.75f);

                VerticalLayoutGroup vlg = _communityCardRoot.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(16, 16, 14, 14);
                vlg.spacing = 8f;
                vlg.childControlWidth = true;
                vlg.childControlHeight = false;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
            }

            _communityCardRoot.SetActive(true);
            _communityCardRoot.transform.SetAsLastSibling();

            for (int i = _communityCardRoot.transform.childCount - 1; i >= 0; i--)
            {
                GameObject.Destroy(_communityCardRoot.transform.GetChild(i).gameObject);
            }

            TMP_Text sample = menu._shortDesc != null ? menu._shortDesc : menu.GetComponentInChildren<TMP_Text>(true);

            GameObject headerRow = new GameObject("HeaderRow", Il2CppType.Of<RectTransform>());
            headerRow.transform.SetParent(_communityCardRoot.transform, false);
            headerRow.AddComponent<LayoutElement>().preferredHeight = 32f;
            HorizontalLayoutGroup hhlg = headerRow.AddComponent<HorizontalLayoutGroup>();
            hhlg.childControlWidth = true;
            hhlg.childForceExpandWidth = false;
            hhlg.spacing = 10f;

            var title = CreateNativeLabel(headerRow.transform, item.title, 18f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, sample);
            title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            Color diffColor = GetDifficultyColor(item.difficulty);
            GameObject diffBadge = new GameObject("DiffBadge", Il2CppType.Of<RectTransform>());
            diffBadge.transform.SetParent(headerRow.transform, false);
            LayoutElement dble = diffBadge.AddComponent<LayoutElement>();
            dble.preferredWidth = 95f;
            dble.preferredHeight = 26f;
            diffBadge.AddComponent<Image>().color = new Color(diffColor.r, diffColor.g, diffColor.b, 0.25f);
            CreateNativeLabel(diffBadge.transform, item.difficulty.ToUpper(), 11f, FontStyles.Bold, diffColor, TextAlignmentOptions.Center, sample);

            CreateNativeLabel(_communityCardRoot.transform, $"Author: <color=white><b>{item.author}</b></color>    |    Base Scene: <color=#00E5FF>{item.staging_scene}</color>", 12.5f, FontStyles.Normal, new Color(0.65f, 0.75f, 0.88f), TextAlignmentOptions.MidlineLeft, sample);

            GameObject statsRow = new GameObject("StatsRow", Il2CppType.Of<RectTransform>());
            statsRow.transform.SetParent(_communityCardRoot.transform, false);
            statsRow.AddComponent<LayoutElement>().preferredHeight = 26f;
            HorizontalLayoutGroup shlg = statsRow.AddComponent<HorizontalLayoutGroup>();
            shlg.spacing = 8f;
            shlg.childControlWidth = true;
            shlg.childForceExpandWidth = false;

            CreateStatPill(statsRow.transform, sample, $"DOWNLOADS: {item.downloads}", new Color(0.2f, 0.7f, 1f));
            CreateStatPill(statsRow.transform, sample, $"OBJECTS: {item.object_count}", new Color(0.3f, 0.95f, 0.5f));

            GameObject descBox = new GameObject("DescBox", Il2CppType.Of<RectTransform>());
            descBox.transform.SetParent(_communityCardRoot.transform, false);
            descBox.AddComponent<LayoutElement>().preferredHeight = 65f;
            descBox.AddComponent<Image>().color = new Color(0.05f, 0.08f, 0.14f, 0.80f);

            string descText = !string.IsNullOrWhiteSpace(item.description) ? item.description : "No description provided.";
            CreateNativeLabel(descBox.transform, descText, 11.5f, FontStyles.Italic, new Color(0.85f, 0.88f, 0.92f), TextAlignmentOptions.TopLeft, sample);

            GameObject btnRow = new GameObject("ActionBtnRow", Il2CppType.Of<RectTransform>());
            btnRow.transform.SetParent(_communityCardRoot.transform, false);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 42f;
            HorizontalLayoutGroup bhlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            bhlg.spacing = 10f;
            bhlg.childControlWidth = true;
            bhlg.childForceExpandWidth = true;

            if (isDownloaded)
            {
                CreateNativeInlineButton(btnRow.transform, "Btn_Play", "▶  PLAY LEVEL", () => MapBrowserService.LaunchSelectedMap(), 170f, sample, new Color(0.18f, 0.65f, 0.35f, 0.95f), 40f, 15f);

                CreateNativeInlineButton(btnRow.transform, "Btn_ReDownload", "↻ UPDATE", () =>
                {
                    CommunityLevelService.DownloadLevel(item, (success) => { if (success) TransformLogsMenu(menu, 1); });
                }, 140f, sample, new Color(0.14f, 0.18f, 0.26f, 0.95f), 40f, 15f);

                CreateNativeInlineButton(btnRow.transform, "Btn_DeleteLocal", "UNINSTALL", () =>
                {
                    string localTxt = Path.Combine(MapBrowserService.DownloadedLevelsDir, $"{item.title}_{item.id}.txt");
                    string localPng = Path.Combine(MapBrowserService.DownloadedLevelsDir, $"{item.title}_{item.id}.png");
                    try
                    {
                        if (File.Exists(localTxt)) File.Delete(localTxt);
                        if (File.Exists(localPng)) File.Delete(localPng);
                    }
                    catch { }
                    TransformLogsMenu(menu, 1);
                }, 130f, sample, new Color(0.45f, 0.18f, 0.18f, 0.95f), 40f, 15f);
            }
            else
            {
                CreateNativeInlineButton(btnRow.transform, "Btn_Download", "DOWNLOAD & INSTALL LEVEL", () =>
                {
                    CommunityLevelService.DownloadLevel(item, (success) =>
                    {
                        if (success) TransformLogsMenu(menu, 1);
                    });
                }, 280f, sample, new Color(0.12f, 0.55f, 0.90f, 0.95f), 42f, 16f);
            }
        }

        private static void CreateStatPill(Transform parent, TMP_Text sample, string label, Color textColor)
        {
            GameObject pill = new GameObject("StatPill", Il2CppType.Of<RectTransform>());
            pill.transform.SetParent(parent, false);
            LayoutElement le = pill.AddComponent<LayoutElement>();
            le.preferredWidth = 140f;
            le.preferredHeight = 24f;
            pill.AddComponent<Image>().color = new Color(0.06f, 0.10f, 0.18f, 0.85f);

            CreateNativeLabel(pill.transform, label, 10.5f, FontStyles.Bold, textColor, TextAlignmentOptions.Center, sample);
        }

        // =========================================================================
        // DIFFICULTY ROW & LEVEL STATS
        // =========================================================================

        public static Color GetDifficultyColor(string diff)
        {
            if (string.IsNullOrEmpty(diff)) return new Color(0.3f, 0.65f, 1.0f);
            string d = diff.ToLowerInvariant();
            if (d.Contains("very easy")) return new Color(0.2f, 0.95f, 0.4f);
            if (d.Contains("easy")) return new Color(0.1f, 0.85f, 1.0f);
            if (d.Contains("hard")) return new Color(1.0f, 0.55f, 0.1f);
            if (d.Contains("expert") || d.Contains("insane")) return new Color(0.95f, 0.2f, 0.2f);
            return new Color(0.3f, 0.65f, 1.0f);
        }

        private static void HighlightSelectedDifficulty()
        {
            for (int i = 0; i < _diffButtons.Count; i++)
            {
                Image img = _diffButtons[i].GetComponent<Image>();
                TMP_Text txt = _diffButtons[i].GetComponentInChildren<TMP_Text>(true);

                if (i == _selectedDifficultyIndex)
                {
                    if (img != null) img.color = new Color(DifficultyColors[i].r * 0.85f, DifficultyColors[i].g * 0.85f, DifficultyColors[i].b * 0.85f, 0.70f);
                    if (txt != null) { txt.color = Color.white; txt.fontStyle = FontStyles.Bold; }
                }
                else
                {
                    if (img != null) img.color = new Color(0.04f, 0.07f, 0.12f, 0.45f);
                    if (txt != null) { txt.color = DifficultyColors[i] * 0.8f; txt.fontStyle = FontStyles.Normal; }
                }
            }
        }

        private static void CreateDifficultyRow(Transform parent, TMP_Text sampleTmp, float height = 32f)
        {
            GameObject row = new GameObject("Row_Difficulty", Il2CppType.Of<RectTransform>());
            row.transform.SetParent(parent, false);

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            le.flexibleWidth = 1f;

            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            GameObject labelObj = new GameObject("Label", Il2CppType.Of<RectTransform>());
            labelObj.transform.SetParent(row.transform, false);

            LayoutElement labelLe = labelObj.AddComponent<LayoutElement>();
            labelLe.preferredWidth = 130f;
            labelLe.minWidth = 130f;
            labelLe.flexibleWidth = 0f;

            TMP_Text labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
            CopyNativeFont(sampleTmp, labelTmp);
            labelTmp.fontSize = 14f;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.color = new Color(0.15f, 0.85f, 1f, 1f);
            labelTmp.text = "DIFFICULTY";
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;

            GameObject btnContainer = new GameObject("BtnContainer", Il2CppType.Of<RectTransform>());
            btnContainer.transform.SetParent(row.transform, false);

            LayoutElement bcLe = btnContainer.AddComponent<LayoutElement>();
            bcLe.flexibleWidth = 1f;

            HorizontalLayoutGroup bchlg = btnContainer.AddComponent<HorizontalLayoutGroup>();
            bchlg.spacing = 4f;
            bchlg.childControlWidth = true;
            bchlg.childControlHeight = true;
            bchlg.childForceExpandWidth = true;
            bchlg.childForceExpandHeight = true;

            _diffButtons.Clear();

            for (int i = 0; i < DifficultyNames.Length; i++)
            {
                int captureIdx = i;
                GameObject btn = new GameObject("Diff_" + DifficultyNames[i], Il2CppType.Of<RectTransform>());
                btn.transform.SetParent(btnContainer.transform, false);

                btn.AddComponent<Image>().color = new Color(0.06f, 0.10f, 0.18f, 0.70f);
                Button bComp = btn.AddComponent<Button>();
                bComp.onClick.AddListener((Action)(() =>
                {
                    _selectedDifficultyIndex = captureIdx;
                    HighlightSelectedDifficulty();
                }));

                CreateNativeLabel(btn.transform, DifficultyNames[i], 12f, FontStyles.Bold, DifficultyColors[i], TextAlignmentOptions.Center, sampleTmp);
                _diffButtons.Add(btn);
            }
        }

        private static void CreateLevelStatsHUD(Transform parent, TMP_Text sampleTmp, float height = 38f)
        {
            GameObject statsBox = new GameObject("HUD_LevelStats", Il2CppType.Of<RectTransform>());
            statsBox.transform.SetParent(parent, false);

            LayoutElement le = statsBox.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            le.flexibleWidth = 1f;

            statsBox.AddComponent<Image>().color = new Color(0.03f, 0.06f, 0.12f, 0.90f);

            HorizontalLayoutGroup hlg = statsBox.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(10, 10, 4, 4);
            hlg.spacing = 10f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            GameObject leftObj = new GameObject("StatsLeft", Il2CppType.Of<RectTransform>());
            leftObj.transform.SetParent(statsBox.transform, false);
            _statsLabelLeft = leftObj.AddComponent<TextMeshProUGUI>();
            CopyNativeFont(sampleTmp, _statsLabelLeft);
            _statsLabelLeft.fontSize = 12f;
            _statsLabelLeft.color = new Color(0.6f, 0.85f, 1f, 0.95f);
            _statsLabelLeft.alignment = TextAlignmentOptions.MidlineLeft;

            GameObject rightObj = new GameObject("StatsRight", Il2CppType.Of<RectTransform>());
            rightObj.transform.SetParent(statsBox.transform, false);
            _statsLabelRight = rightObj.AddComponent<TextMeshProUGUI>();
            CopyNativeFont(sampleTmp, _statsLabelRight);
            _statsLabelRight.fontSize = 12f;
            _statsLabelRight.color = new Color(0.6f, 0.85f, 1f, 0.95f);
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
                    string l = lines[i].ToLowerInvariant();
                    if (l.StartsWith("#")) continue;
                    if (l.Contains("laser")) lasers++;
                    else if (l.Contains("jumper")) jumpers++;
                    else if (l.Contains("helix")) turbines++;
                    else if (l.Contains("turret")) turrets++;
                    if (l.Contains(";path:1") || l.Contains(";comp:path:")) paths++;
                }
            }
            catch { }

            DateTime mod = File.GetLastWriteTime(fullPath);
            _statsLabelLeft.text = $"- TOTAL OBJECTS: <b><color=#00E5FF>{objectCount}</color></b>\n- HAZARDS & LASERS: <b><color=#FF5252>{lasers}</color></b>\n- JUMP PADS: <b><color=#FFEB3B>{jumpers}</color></b>";
            _statsLabelRight.text = $"- MOVING PATHS: <b><color=#E040FB>{paths}</color></b>\n- TURRETS: <b><color=#FF4081>{turrets}</color></b>\n- SAVED: <color=#B0BEC5>{mod:dd/MM/yyyy HH:mm}</color>";
        }

        private static void CreateNativeSaveButton(Transform parent, TMP_Text sampleTmp, float height = 38f)
        {
            GameObject buttonRow = new GameObject("Row_ActionButtons", Il2CppType.Of<RectTransform>());
            buttonRow.transform.SetParent(parent, false);

            LayoutElement rowLe = buttonRow.AddComponent<LayoutElement>();
            rowLe.preferredHeight = height;
            rowLe.minHeight = height;
            rowLe.flexibleWidth = 1f;

            HorizontalLayoutGroup hlg = buttonRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            Button saveBtn = CreateNativeInlineButton(buttonRow.transform, "Btn_SaveMetadata", "SAVE DETAILS", () => SaveCurrentMetadata(MapBrowserService.SelectedMapPath), 150f, sampleTmp, new Color(0.12f, 0.55f, 0.88f, 0.90f), 38f, 15f);
            if (saveBtn != null) _saveBtnText = saveBtn.GetComponentInChildren<TMP_Text>();

            CreateNativeInlineButton(buttonRow.transform, "Btn_PublishCommunity", "PUBLISH TO CLOUD", () =>
            {
                SaveCurrentMetadata(MapBrowserService.SelectedMapPath);
                CommunityLevelService.PublishCurrentLevel(MapBrowserService.SelectedMapPath);
            }, 170f, sampleTmp, new Color(0.18f, 0.65f, 0.35f, 0.95f), 38f, 15f);
        }

        private static void CreateNativeInputRow(Transform parent, TMP_Text sampleTmp, string labelName, float height, float fontSize, out TMP_InputField inputField, bool isMultiLine = false)
        {
            GameObject row = new GameObject("Row_" + labelName, Il2CppType.Of<RectTransform>());
            row.transform.SetParent(parent, false);

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            le.flexibleWidth = 1f;

            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            GameObject labelObj = new GameObject("Label", Il2CppType.Of<RectTransform>());
            labelObj.transform.SetParent(row.transform, false);

            LayoutElement labelLe = labelObj.AddComponent<LayoutElement>();
            labelLe.preferredWidth = 130f;
            labelLe.minWidth = 130f;
            labelLe.flexibleWidth = 0f;

            TMP_Text labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
            CopyNativeFont(sampleTmp, labelTmp);
            labelTmp.fontSize = 14f;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.color = new Color(0.15f, 0.85f, 1f, 1f);
            labelTmp.text = labelName;
            labelTmp.alignment = isMultiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;

            GameObject inputObj = new GameObject("InputField", Il2CppType.Of<RectTransform>());
            inputObj.transform.SetParent(row.transform, false);

            LayoutElement inLe = inputObj.AddComponent<LayoutElement>();
            inLe.flexibleWidth = 1f;

            inputObj.AddComponent<Image>().color = new Color(0.06f, 0.10f, 0.18f, 0.85f);

            GameObject textObj = new GameObject("Text", Il2CppType.Of<RectTransform>());
            textObj.transform.SetParent(inputObj.transform, false);
            RectTransform textRt = textObj.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(8f, 0f);
            textRt.offsetMax = new Vector2(-8f, 0f);

            TMP_Text inTmp = textObj.AddComponent<TextMeshProUGUI>();
            CopyNativeFont(sampleTmp, inTmp);
            inTmp.fontSize = fontSize;
            inTmp.color = Color.white;
            inTmp.alignment = isMultiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;

            inputField = inputObj.AddComponent<TMP_InputField>();
            inputField.textViewport = textRt;
            inputField.textComponent = inTmp;
            inputField.pointSize = fontSize;
            inputField.lineType = isMultiLine ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
        }

        // =========================================================================
        // NATIVE CLONED BOTTOM BAR BUTTONS
        // =========================================================================

        private static void SetupBottomBarButtons(LogsMenu menu)
        {
            BackButton nativeBack = GameObject.FindObjectOfType<BackButton>();
            if (nativeBack == null) return;
            _nativeBackButtonTemplate = nativeBack.gameObject;

            Transform parentBar = nativeBack.transform.parent;
            RectTransform backRt = nativeBack.GetComponent<RectTransform>();

            float nativeWidth = backRt.rect.width > 50f ? backRt.rect.width : 220f;
            float nativeHeight = backRt.rect.height > 20f ? backRt.rect.height : 55f;

            TMP_Text backTmp = nativeBack.GetComponentInChildren<TMP_Text>(true);
            float nativeFontSize = (backTmp != null && backTmp.fontSize > 15f) ? backTmp.fontSize : 34f;
            float spacing = 15f;

            // 1. PLAY BUTTON
            if (_nativePlayButton == null || _nativePlayButton.Equals(null) || _nativePlayButton.transform.parent != parentBar)
            {
                if (_nativePlayButton != null) GameObject.DestroyImmediate(_nativePlayButton);
                _nativePlayButton = GameObject.Instantiate(_nativeBackButtonTemplate, parentBar);
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

            Button playBtn = _nativePlayButton.GetComponent<Button>() ?? _nativePlayButton.AddComponent<Button>();
            playBtn.onClick = new Button.ButtonClickedEvent();
            playBtn.onClick.AddListener((Action)(() =>
            {
                if (!string.IsNullOrEmpty(MapBrowserService.SelectedMapPath))
                    MapBrowserService.LaunchSelectedMap();
            }));

            // 2. CREATE NEW BUTTON
            if (_nativeCreateButton == null || _nativeCreateButton.Equals(null) || _nativeCreateButton.transform.parent != parentBar)
            {
                if (_nativeCreateButton != null) GameObject.DestroyImmediate(_nativeCreateButton);
                _nativeCreateButton = GameObject.Instantiate(_nativeBackButtonTemplate, parentBar);
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

            Button createBtn = _nativeCreateButton.GetComponent<Button>() ?? _nativeCreateButton.AddComponent<Button>();
            createBtn.onClick = new Button.ButtonClickedEvent();
            createBtn.onClick.AddListener((Action)(() => CreateNewLevel(menu)));

            // 3. DELETE BUTTON
            if (_nativeDeleteButton == null || _nativeDeleteButton.Equals(null) || _nativeDeleteButton.transform.parent != parentBar)
            {
                if (_nativeDeleteButton != null) GameObject.DestroyImmediate(_nativeDeleteButton);
                _nativeDeleteButton = GameObject.Instantiate(_nativeBackButtonTemplate, parentBar);
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

            Button deleteBtn = _nativeDeleteButton.GetComponent<Button>() ?? _nativeDeleteButton.AddComponent<Button>();
            deleteBtn.onClick = new Button.ButtonClickedEvent();
            deleteBtn.onClick.AddListener((Action)(() => DeleteCurrentSelectedLevel(menu)));

            EnforceBottomBarLabels();
        }

        public static void DeleteCurrentSelectedLevel(LogsMenu menu)
        {
            if (string.IsNullOrEmpty(MapBrowserService.SelectedMapPath) || !File.Exists(MapBrowserService.SelectedMapPath))
                return;

            try
            {
                string deletingFile = MapBrowserService.SelectedMapPath;
                File.Delete(deletingFile);

                string pngFile = Path.ChangeExtension(deletingFile, ".png");
                if (File.Exists(pngFile)) File.Delete(pngFile);

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

            string scene = !string.IsNullOrEmpty(MapBrowserService.SelectedStagingScene) ? MapBrowserService.SelectedStagingScene : "level01_Spark01";
            string starterContent =
                $"#TITLE: New Level {idx}\n" +
                $"#AUTHOR: Player\n" +
                $"#DIFFICULTY: Normal\n" +
                $"#DESC: Custom level created with DeadCore Level Editor.\n" +
                $"#SCENE: {scene}\n" +
                "Floor_Platform_16x16;-241.0000;-97.7000;-6.0000;1.0000;0.0000;0.0000;0.7071;0.7071;0.00;PARENT:-1;SCALE3:1.0000:1.0000:1.0000\n" +
                "Spawn_Gate;-241.0000;-96.7000;-6.0000;1.0000;0.0000;0.0000;0.0000;1.0000;0.00;COMP:GATE:1:0:0;COMP:NEON:1:FF730D:8.00;PARENT:-1;SCALE3:1.0000:1.0000:1.0000\n" +
                "Skybox_Controller;-247.0000;-96.4750;-0.5000;1.0000;0.0000;0.0000;0.0000;1.0000;0.00;COMP:SKYBOX:4.00:FFEEF5:0.0:0.00;PARENT:-1;SCALE3:1.0000:1.0000:1.0000\n" +
                "Global_Sunlight;-247.0000;-95.3547;-4.0000;1.0000;0.4082;-0.2346;0.1094;0.8754;3.00;COMP:LIGHT:3.00:60.0:F1DBCA:1.00:1;PARENT:-1;SCALE3:1.0000:1.0000:1.0000\n";
            File.WriteAllText(newPath, starterContent);

            DeadCoreLevelEditorMod.SwitchTab(0, menu);
            MapBrowserService.SelectedMapPath = newPath;
            MapBrowserService.SelectedMapName = newName;

            TransformLogsMenu(menu, 0);
        }

        // =========================================================================
        // NATIVE UI HELPERS
        // =========================================================================

        private static Button CreateNativeInlineButton(Transform parent, string name, string label, Action onClick, float width, TMP_Text sampleTmp, Color? bgColor = null, float height = 38f, float fontSize = 15f)
        {
            GameObject btnObj = new GameObject(name, Il2CppType.Of<RectTransform>());
            btnObj.transform.SetParent(parent, false);

            LayoutElement le = btnObj.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;
            le.minHeight = height;

            Image img = btnObj.AddComponent<Image>();
            img.color = bgColor ?? new Color(0.14f, 0.18f, 0.28f, 0.95f);

            Button btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = img;

            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            cb.pressedColor = new Color(0.3f, 0.8f, 1f, 1f);
            btn.colors = cb;

            btn.onClick.AddListener((Action)(() => onClick?.Invoke()));

            CreateNativeLabel(btnObj.transform, label, fontSize, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, sampleTmp);
            return btn;
        }

        private static TMP_Text CreateNativeLabel(Transform parent, string text, float fontSize, FontStyles style, Color color, TextAlignmentOptions align, TMP_Text sampleTmp)
        {
            GameObject obj = new GameObject("Label", Il2CppType.Of<RectTransform>());
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;

            TMP_Text tmp = obj.AddComponent<TextMeshProUGUI>();
            CopyNativeFont(sampleTmp, tmp);
            tmp.text = text ?? "";
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = align;
            return tmp;
        }

        private static void CopyNativeFont(TMP_Text source, TMP_Text target)
        {
            if (source != null && target != null)
            {
                target.font = source.font;
                target.fontSharedMaterial = source.fontSharedMaterial;
            }
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

                    string typeName = c.GetIl2CppType().Name;
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
}