using System;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using SceneManager = UnityEngine.SceneManagement.SceneManager;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using Il2CppDeadCore.UI;

using File = System.IO.File;
using Directory = System.IO.Directory;
using Path = System.IO.Path;

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 1: ZERO-DEPENDENCY MANAGED JSON & CONFIGURATION ENGINE
    // =========================================================================

    public class EditorConfigData
    {
        public float MinAssetSize = 1.4f;
        public float FlycamSpeed = 24.0f;
        public float FastCamMultiplier = 3.5f;
        public float SlowCamMultiplier = 0.25f;
        public float MouseSensitivity = 2.5f;
        public bool InvertLookY = false;
        public float EditorFov = 75.0f;
        public float GizmoScaleMultiplier = 1.0f;
        public float DefaultGridSnap = 1.0f;
        public bool ShowSizeBadges = true;
        public bool SnappingProxiesVisible = false;

        public Dictionary<string, List<string>> CustomCategories = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Keybindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public EditorConfigData()
        {
            SetDefaultKeybindings();
        }

        public void SetDefaultKeybindings()
        {
            Keybindings["TogglePlaytest"] = "F1";
            Keybindings["SaveLevel"] = "F5";
            Keybindings["LoadLevel"] = "F6";
            Keybindings["Undo"] = "Ctrl+Z";
            Keybindings["Redo"] = "Ctrl+Y";
            Keybindings["Duplicate"] = "Ctrl+D";
            Keybindings["Delete"] = "Delete";
            Keybindings["CreatePrefab"] = "Ctrl+Alt+P";
            Keybindings["Snap90"] = "Alt+R";
            Keybindings["ResetRot"] = "Alt+Shift+R";
            Keybindings["FocusCamera"] = "F";
            Keybindings["Parent"] = "Ctrl+P";
            Keybindings["Unparent"] = "Alt+P";
        }
    }

    public static class EditorConfigService
    {
        public static EditorConfigData Config = new EditorConfigData();
        public static string ConfigPath => Path.Combine(Directory.GetCurrentDirectory(), "UserData", "EditorConfig.json");
        public static void LoadConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                if (!File.Exists(ConfigPath))
                {
                    SaveConfig();
                    return;
                }

                string raw = File.ReadAllText(ConfigPath);
                var parsed = SimpleJsonEngine.Deserialize(raw);
                if (parsed != null) Config = parsed;
                if (Config.Keybindings == null || Config.Keybindings.Count == 0) Config.SetDefaultKeybindings();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Config] Failed reading json: {ex.Message}. Rebuilding defaults.");
                Config = new EditorConfigData();
                SaveConfig();
            }
        }

        public static void SaveConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = SimpleJsonEngine.Serialize(Config);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Config] Failed saving json: {ex.Message}");
            }
        }

        public static void AssignAssetToCategory(string categoryName, string assetDisplayName)
        {
            if (string.IsNullOrWhiteSpace(categoryName) || string.IsNullOrWhiteSpace(assetDisplayName)) return;
            string cat = categoryName.Trim();

            if (!Config.CustomCategories.ContainsKey(cat))
                Config.CustomCategories[cat] = new List<string>();

            if (!Config.CustomCategories[cat].Contains(assetDisplayName))
                Config.CustomCategories[cat].Add(assetDisplayName);

            SaveConfig();
        }


        public static void CreateCategory(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return;
            string cat = categoryName.Trim();
            if (!Config.CustomCategories.ContainsKey(cat))
            {
                Config.CustomCategories[cat] = new List<string>();
                SaveConfig();
            }
        }

        public static void DeleteCategory(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return;
            if (Config.CustomCategories.Remove(categoryName.Trim()))
            {
                SaveConfig();
            }
        }

        public static void ToggleAssetInCategory(string categoryName, string assetDisplayName)
        {
            if (string.IsNullOrWhiteSpace(categoryName) || string.IsNullOrWhiteSpace(assetDisplayName)) return;
            string cat = categoryName.Trim();

            if (!Config.CustomCategories.ContainsKey(cat))
                Config.CustomCategories[cat] = new List<string>();

            if (Config.CustomCategories[cat].Contains(assetDisplayName))
                Config.CustomCategories[cat].Remove(assetDisplayName);
            else
                Config.CustomCategories[cat].Add(assetDisplayName);

            SaveConfig();
        }

        public static bool IsAssetInCategory(string categoryName, string assetDisplayName)
        {
            if (string.IsNullOrWhiteSpace(categoryName) || string.IsNullOrWhiteSpace(assetDisplayName)) return false;
            return Config.CustomCategories.TryGetValue(categoryName.Trim(), out var list) && list != null && list.Contains(assetDisplayName);
        }

        public static List<string> GetAssignedCategories(string assetDisplayName)
        {
            List<string> result = new List<string>();
            foreach (var kvp in Config.CustomCategories)
            {
                if (kvp.Value != null && kvp.Value.Contains(assetDisplayName))
                    result.Add(kvp.Key);
            }
            return result;
        }

        public static bool IsShortcutTriggered(string actionName)
        {
            if (!Config.Keybindings.TryGetValue(actionName, out string shortcut) || string.IsNullOrWhiteSpace(shortcut) || shortcut == "None")
                return false;

            string[] tokens = shortcut.Split('+');
            if (tokens.Length == 0) return false;

            bool needCtrl = false;
            bool needAlt = false;
            bool needShift = false;
            string keyToken = "";

            for (int i = 0; i < tokens.Length; i++)
            {
                string t = tokens[i].Trim();
                if (t.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) needCtrl = true;
                else if (t.Equals("Alt", StringComparison.OrdinalIgnoreCase)) needAlt = true;
                else if (t.Equals("Shift", StringComparison.OrdinalIgnoreCase)) needShift = true;
                else keyToken = t;
            }

            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (needCtrl != ctrlHeld || needAlt != altHeld || needShift != shiftHeld)
                return false;

            if (Enum.TryParse<KeyCode>(keyToken, true, out KeyCode kc))
            {
                return Input.GetKeyDown(kc);
            }

            return false;
        }

        /// <summary>
        /// Only triggers if the shortcut has been customized to something other than the engine's built-in default,
        /// preventing duplicate execution with EditorSessionManager's hardcoded handlers.
        /// </summary>
        public static bool IsCustomShortcutTriggered(string actionName, string defaultCombo)
        {
            if (!Config.Keybindings.TryGetValue(actionName, out string shortcut) || string.IsNullOrWhiteSpace(shortcut) || shortcut == "None")
                return false;

            if (shortcut.Equals(defaultCombo, StringComparison.OrdinalIgnoreCase))
                return false;

            return IsShortcutTriggered(actionName);
        }
    }

    /// <summary>
    /// Lightweight, zero-dependency JSON serializer/deserializer running purely in managed memory.
    /// Eliminates all Il2Cpp type-conversion crashes with UnityEngine.JsonUtility.
    /// </summary>
    public static class SimpleJsonEngine
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Serialize(EditorConfigData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"MinAssetSize\": {data.MinAssetSize.ToString("F2", Inv)},");
            sb.AppendLine($"  \"FlycamSpeed\": {data.FlycamSpeed.ToString("F1", Inv)},");
            sb.AppendLine($"  \"FastCamMultiplier\": {data.FastCamMultiplier.ToString("F2", Inv)},");
            sb.AppendLine($"  \"SlowCamMultiplier\": {data.SlowCamMultiplier.ToString("F2", Inv)},");
            sb.AppendLine($"  \"MouseSensitivity\": {data.MouseSensitivity.ToString("F2", Inv)},");
            sb.AppendLine($"  \"InvertLookY\": {(data.InvertLookY ? "true" : "false")},");
            sb.AppendLine($"  \"EditorFov\": {data.EditorFov.ToString("F1", Inv)},");
            sb.AppendLine($"  \"GizmoScaleMultiplier\": {data.GizmoScaleMultiplier.ToString("F2", Inv)},");
            sb.AppendLine($"  \"DefaultGridSnap\": {data.DefaultGridSnap.ToString("F2", Inv)},");
            sb.AppendLine($"  \"ShowSizeBadges\": {(data.ShowSizeBadges ? "true" : "false")},");
            sb.AppendLine($"  \"SnappingProxiesVisible\": {(data.SnappingProxiesVisible ? "true" : "false")},");

            // Keybindings map
            sb.AppendLine("  \"Keybindings\": {");
            int kbCount = 0;
            foreach (var kvp in data.Keybindings)
            {
                kbCount++;
                string comma = (kbCount < data.Keybindings.Count) ? "," : "";
                sb.AppendLine($"    \"{Escape(kvp.Key)}\": \"{Escape(kvp.Value)}\"{comma}");
            }
            sb.AppendLine("  },");

            // Custom categories map
            sb.AppendLine("  \"CustomCategories\": {");
            int catCount = 0;
            foreach (var kvp in data.CustomCategories)
            {
                catCount++;
                string comma = (catCount < data.CustomCategories.Count) ? "," : "";
                sb.AppendLine($"    \"{Escape(kvp.Key)}\": [");
                if (kvp.Value != null)
                {
                    for (int i = 0; i < kvp.Value.Count; i++)
                    {
                        string itemComma = (i < kvp.Value.Count - 1) ? "," : "";
                        sb.AppendLine($"      \"{Escape(kvp.Value[i])}\"{itemComma}");
                    }
                }
                sb.AppendLine($"    ]{comma}");
            }
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public static EditorConfigData Deserialize(string json)
        {
            var data = new EditorConfigData();
            if (string.IsNullOrWhiteSpace(json)) return data;

            data.MinAssetSize = ExtractFloat(json, "MinAssetSize", 1.4f);
            data.FlycamSpeed = ExtractFloat(json, "FlycamSpeed", 24f);
            data.FastCamMultiplier = ExtractFloat(json, "FastCamMultiplier", 3.5f);
            data.SlowCamMultiplier = ExtractFloat(json, "SlowCamMultiplier", 0.25f);
            data.MouseSensitivity = ExtractFloat(json, "MouseSensitivity", 2.5f);
            data.InvertLookY = ExtractBool(json, "InvertLookY", false);
            data.EditorFov = ExtractFloat(json, "EditorFov", 75f);
            data.GizmoScaleMultiplier = ExtractFloat(json, "GizmoScaleMultiplier", 1.0f);
            data.DefaultGridSnap = ExtractFloat(json, "DefaultGridSnap", 1.0f);
            data.ShowSizeBadges = ExtractBool(json, "ShowSizeBadges", true);
            data.SnappingProxiesVisible = ExtractBool(json, "SnappingProxiesVisible", false);

            ExtractDictionary(json, "Keybindings", data.Keybindings);
            ExtractStringListDictionary(json, "CustomCategories", data.CustomCategories);

            return data;
        }

        private static float ExtractFloat(string json, string key, float fallback)
        {
            string marker = $"\"{key}\":";
            int idx = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx == -1) return fallback;

            int start = idx + marker.Length;
            int end = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, start);
            if (end == -1) end = json.Length;

            string raw = json.Substring(start, end - start).Trim();
            if (float.TryParse(raw, NumberStyles.Float, Inv, out float res)) return res;
            return fallback;
        }

        private static bool ExtractBool(string json, string key, bool fallback)
        {
            string marker = $"\"{key}\":";
            int idx = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx == -1) return fallback;

            int start = idx + marker.Length;
            int end = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, start);
            if (end == -1) end = json.Length;

            string raw = json.Substring(start, end - start).Trim().ToLowerInvariant();
            if (raw.StartsWith("true")) return true;
            if (raw.StartsWith("false")) return false;
            return fallback;
        }

        private static void ExtractDictionary(string json, string blockName, Dictionary<string, string> target)
        {
            string marker = $"\"{blockName}\":";
            int idx = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx == -1) return;

            int openBrace = json.IndexOf('{', idx + marker.Length);
            if (openBrace == -1) return;
            int closeBrace = json.IndexOf('}', openBrace);
            if (closeBrace == -1) return;

            string block = json.Substring(openBrace + 1, closeBrace - openBrace - 1);
            string[] lines = block.Split(new char[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                int colon = line.IndexOf(':');
                if (colon == -1) continue;

                string k = line.Substring(0, colon).Trim().Trim('"');
                string v = line.Substring(colon + 1).Trim().Trim('"');
                if (!string.IsNullOrEmpty(k)) target[k] = v;
            }
        }

        private static void ExtractStringListDictionary(string json, string blockName, Dictionary<string, List<string>> target)
        {
            string marker = $"\"{blockName}\":";
            int idx = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx == -1) return;

            int openBrace = json.IndexOf('{', idx + marker.Length);
            if (openBrace == -1) return;

            int depth = 0;
            int closeBrace = -1;
            for (int i = openBrace; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}')
                {
                    depth--;
                    if (depth == 0) { closeBrace = i; break; }
                }
            }
            if (closeBrace == -1) return;

            string block = json.Substring(openBrace + 1, closeBrace - openBrace - 1);
            int cursor = 0;
            while (cursor < block.Length)
            {
                int quoteStart = block.IndexOf('"', cursor);
                if (quoteStart == -1) break;
                int quoteEnd = block.IndexOf('"', quoteStart + 1);
                if (quoteEnd == -1) break;

                string catKey = block.Substring(quoteStart + 1, quoteEnd - quoteStart - 1).Trim();
                int arrStart = block.IndexOf('[', quoteEnd);
                if (arrStart == -1) break;
                int arrEnd = block.IndexOf(']', arrStart);
                if (arrEnd == -1) break;

                string arrayBody = block.Substring(arrStart + 1, arrEnd - arrStart - 1);
                string[] items = arrayBody.Split(new char[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                var list = new List<string>();
                for (int m = 0; m < items.Length; m++)
                {
                    string it = items[m].Trim().Trim('"');
                    if (!string.IsNullOrWhiteSpace(it)) list.Add(it);
                }

                target[catKey] = list;
                cursor = arrEnd + 1;
            }
        }

        private static string Escape(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
    }

    // =========================================================================
    // SECTION 2: MOVABLE & RESIZABLE FLOATING WINDOW CONTAINER
    // =========================================================================

    public class StudioFloatingWindow
    {
        public GameObject WindowRoot;
        public RectTransform RootRt;
        public RectTransform ContentRt;
        public TMP_Text TitleText;
        public bool IsDragging = false;
        private Vector2 _dragOffset = Vector2.zero;

        public static StudioFloatingWindow Create(Transform parent, string name, string title, Vector2 size, Vector2 defaultPos)
        {
            var win = new StudioFloatingWindow();

            win.WindowRoot = new GameObject(name, Il2CppType.Of<RectTransform>());
            win.WindowRoot.transform.SetParent(parent, false);

            win.RootRt = win.WindowRoot.GetComponent<RectTransform>();
            win.RootRt.sizeDelta = size;
            win.RootRt.anchoredPosition = defaultPos;
            win.RootRt.pivot = new Vector2(0.5f, 0.5f);

            Image bg = win.WindowRoot.AddComponent<Image>();
            bg.color = new Color(0.10f, 0.12f, 0.16f, 0.98f);

            GameObject titleBar = new GameObject("TitleBar", Il2CppType.Of<RectTransform>());
            titleBar.transform.SetParent(win.WindowRoot.transform, false);

            RectTransform tbrt = titleBar.GetComponent<RectTransform>();
            tbrt.anchorMin = new Vector2(0f, 1f);
            tbrt.anchorMax = new Vector2(1f, 1f);
            tbrt.pivot = new Vector2(0.5f, 1f);
            tbrt.sizeDelta = new Vector2(0f, 30f);
            tbrt.anchoredPosition = Vector2.zero;

            Image tbBg = titleBar.AddComponent<Image>();
            tbBg.color = new Color(0.15f, 0.18f, 0.25f, 0.98f);

            EventTrigger trigger = titleBar.AddComponent<EventTrigger>();

            var entryDown = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            entryDown.callback.AddListener((Action<BaseEventData>)((e) =>
            {
                var pe = e.Cast<PointerEventData>();
                RectTransformUtility.ScreenPointToLocalPointInRectangle(win.RootRt, pe.position, pe.pressEventCamera, out win._dragOffset);
                win.IsDragging = true;
                win.WindowRoot.transform.SetAsLastSibling();
            }));
            trigger.triggers.Add(entryDown);

            var entryUp = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            entryUp.callback.AddListener((Action<BaseEventData>)((e) => { win.IsDragging = false; }));
            trigger.triggers.Add(entryUp);

            var entryDrag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
            entryDrag.callback.AddListener((Action<BaseEventData>)((e) =>
            {
                var pe = e.Cast<PointerEventData>();
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent.GetComponent<RectTransform>(), pe.position, pe.pressEventCamera, out Vector2 localPoint))
                {
                    win.RootRt.anchoredPosition = localPoint - win._dragOffset;
                }
            }));
            trigger.triggers.Add(entryDrag);

            win.TitleText = StudioUIManager.CreateTextPrimitive(titleBar.transform, title, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-40f, 0f), 11.5f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);

            Button closeBtn = StudioUIManager.CreateButtonPrimitive(titleBar.transform, "Btn_Close", "✕", 24f, () => win.Hide(), new Color(0.75f, 0.22f, 0.22f, 1f));
            RectTransform cbrt = closeBtn.GetComponent<RectTransform>();
            cbrt.anchorMin = new Vector2(1f, 0.5f);
            cbrt.anchorMax = new Vector2(1f, 0.5f);
            cbrt.pivot = new Vector2(1f, 0.5f);
            cbrt.anchoredPosition = new Vector2(-4f, 0f);
            cbrt.sizeDelta = new Vector2(22f, 22f);

            GameObject body = new GameObject("Body", Il2CppType.Of<RectTransform>());
            body.transform.SetParent(win.WindowRoot.transform, false);
            win.ContentRt = body.GetComponent<RectTransform>();
            win.ContentRt.anchorMin = Vector2.zero;
            win.ContentRt.anchorMax = Vector2.one;
            win.ContentRt.offsetMin = new Vector2(8f, 8f);
            win.ContentRt.offsetMax = new Vector2(-8f, -34f);

            win.WindowRoot.SetActive(false);
            return win;
        }

        public void Show()
        {
            WindowRoot.SetActive(true);
            WindowRoot.transform.SetAsLastSibling();
        }

        public void Hide() => WindowRoot.SetActive(false);

        public void Toggle()
        {
            if (WindowRoot.activeSelf) Hide();
            else Show();
        }
    }

    // =========================================================================
    // SECTION 3: STUDIO UGUI ENGINE & LIFECYCLE
    // =========================================================================

    public static class StudioUIManager
    {
        private static GameObject _canvasRoot = null;
        private static Canvas _canvas = null;

        // Top Toolbar & Navigation
        private static GameObject _toolbarPanel = null;
        private static GameObject _activeDropdownMenu = null;
        private static Button _modeTogglePillBtn = null;
        private static TMP_Text _modeTogglePillText = null;
        private static TMP_Text _surfaceAlignBtnText = null;
        private static TMP_Text _gridSnapBtnText = null;

        // Floating Category Bar
        private static GameObject _floatingCategoryDock = null;
        private static readonly Dictionary<string, Button> _categoryDockButtons = new Dictionary<string, Button>();
        private static TMP_Text _assetCountBadgeText = null;

        // Hierarchy Panel
        private static GameObject _hierarchyPanel = null;
        private static ScrollRect _hierarchyScrollRect = null;
        private static RectTransform _hierarchyContent = null;
        private static TMP_InputField _hierarchySearchInput = null;
        private static readonly List<GameObject> _hierarchyRows = new List<GameObject>();
        private static readonly HashSet<GameObject> _collapsedParents = new HashSet<GameObject>();

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

        // Asset Browser
        private static GameObject _assetBrowserPanel = null;
        private static ScrollRect _assetBrowserScrollRect = null;
        private static RectTransform _browserContent = null;
        private static TMP_InputField _browserSearchInput = null;
        private static string _activeBrowserCategory = "All";
        private static CatalogAsset _assignTargetAsset = null;
        private static AssetSizeTier _activeSizeFilter = AssetSizeTier.All;
        private static bool _sortSizeAscending = true;
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

        // Occurrences Window Elements
        private static RectTransform _occurrencesContent = null;
        private static TMP_Text _occurrencesTitleHeader = null;
        private static string _currentOccurrencesTargetName = "";
        private static readonly List<GameObject> _currentOccurrencesList = new List<GameObject>();

        // Uniform Scaler Elements
        private static float _uniformScaleValue = 1.0f;
        private static bool _scaleCentroidPivot = true;

        // Category Assign Elements
        private static TMP_InputField _assignCategoryInput = null;
        private static RectTransform _existingCategoryChipsRoot = null;

        // Renamer Elements
        private static TMP_InputField _batchRenameInput = null;

        // Shortcut Rebinder State
        private static string _activeRebindingActionKey = null;
        private static TMP_Text _activeRebindLabel = null;
        private static readonly Dictionary<string, TMP_Text> _shortcutDisplayLabels = new Dictionary<string, TMP_Text>();

        private static bool _suppressInspectorCallbacks = false;
        private static List<GameObject> GetSelectionTargets(GameObject primary)
        {
            if (EditorSessionManager.SelectedObjects != null && EditorSessionManager.SelectedObjects.Count > 1)
            {
                return EditorSessionManager.SelectedObjects;
            }
            return (primary != null) ? new List<GameObject> { primary } : new List<GameObject>();
        }
        public static void UpdateUI()
        {
            UpdateRebindingTick();

            // Direct mouse-wheel scroll listener for Asset Browser
            if (_assetBrowserScrollRect != null && _assetBrowserPanel != null && _assetBrowserPanel.activeInHierarchy)
            {
                Vector2 mousePos = Input.mousePosition;
                RectTransform abrt = _assetBrowserPanel.GetComponent<RectTransform>();
                if (abrt != null && RectTransformUtility.RectangleContainsScreenPoint(abrt, mousePos))
                {
                    float scrollWheel = Input.GetAxis("Mouse ScrollWheel");
                    if (Mathf.Abs(scrollWheel) > 0.001f)
                    {
                        float contentH = _browserContent != null ? _browserContent.rect.height : 1000f;
                        float viewH = (_assetBrowserScrollRect.viewport != null) ? _assetBrowserScrollRect.viewport.rect.height : 220f;
                        float scrollableH = Mathf.Max(1f, contentH - viewH);

                        float step = (90f / scrollableH) * (scrollWheel > 0 ? 1f : -1f);
                        _assetBrowserScrollRect.verticalNormalizedPosition = Mathf.Clamp01(_assetBrowserScrollRect.verticalNormalizedPosition + step);
                    }
                }
            }

            // Auto-close open dropdowns when clicking outside
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

            // Auto-close context menu when clicking outside
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

        public static void SetNotificationText(string text)
        {
            if (!string.IsNullOrEmpty(text)) MelonLogger.Msg($"[Studio] {text}");
        }

        public static void SetUIVisible(bool visible)
        {
            if (_canvasRoot != null) _canvasRoot.SetActive(visible);
            if (!visible)
            {
                CloseAllDropdowns();
                CloseContextMenu();
            }
        }

        public static void InitializeUI()
        {
            if (_canvasRoot != null) return;

            EditorConfigService.LoadConfig();

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

            try { BuildTopToolbar(); } catch (Exception ex) { MelonLogger.Error($"[UI] TopToolbar: {ex}"); }
            try { BuildModePill(); } catch (Exception ex) { MelonLogger.Error($"[UI] ModePill: {ex}"); }
            try { BuildHierarchyPanel(); } catch (Exception ex) { MelonLogger.Error($"[UI] HierarchyPanel: {ex}"); }
            try { BuildInspectorPanel(); } catch (Exception ex) { MelonLogger.Error($"[UI] InspectorPanel: {ex}"); }
            try { BuildFloatingCategoryDock(); } catch (Exception ex) { MelonLogger.Error($"[UI] CategoryDock: {ex}"); }
            try { BuildAssetBrowserPanel(); } catch (Exception ex) { MelonLogger.Error($"[UI] AssetBrowser: {ex}"); }
            try { BuildContextMenuOverlay(); } catch (Exception ex) { MelonLogger.Error($"[UI] ContextMenu: {ex}"); }

            try { BuildPreferencesWindow(); } catch (Exception ex) { MelonLogger.Error($"[UI] Preferences: {ex}"); }
            try { BuildOccurrencesWindow(); } catch (Exception ex) { MelonLogger.Error($"[UI] Occurrences: {ex}"); }
            try { BuildUniformScalerWindow(); } catch (Exception ex) { MelonLogger.Error($"[UI] Scaler: {ex}"); }
            try { BuildAssignCategoryWindow(); } catch (Exception ex) { MelonLogger.Error($"[UI] AssignCategory: {ex}"); }
            try { BuildBatchRenamerWindow(); } catch (Exception ex) { MelonLogger.Error($"[UI] Renamer: {ex}"); }
            try { BuildDistributeSpacingWindow(); } catch (Exception ex) { MelonLogger.Error($"[UI] Distribute: {ex}"); }

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
                    bool isLight = (pType == PlacedObjectType.Spotlight || pType == PlacedObjectType.Sunlight || pType == PlacedObjectType.SkyboxController);
                    bool isGate = (pType == PlacedObjectType.Checkpoint || pType == PlacedObjectType.SpawnGate || pType == PlacedObjectType.GoalGate);

                    if (isLight)
                    {
                        BoxCollider bc = obj.GetComponent<BoxCollider>();
                        if (bc == null)
                        {
                            bc = obj.AddComponent<BoxCollider>();
                            bc.size = new Vector3(1.4f, 1.4f, 1.6f);
                            bc.center = new Vector3(0f, 0f, 0.4f);
                        }
                        bc.isTrigger = true;
                        bc.enabled = true;
                        obj.layer = 0;
                    }
                    else if (isGate)
                    {
                        CheckPointScript cp = obj.GetComponentInChildren<CheckPointScript>();
                        if (cp != null)
                        {
                            Collider col = cp.GetComponent<Collider>() ?? obj.GetComponentInChildren<Collider>();
                            if (col != null)
                            {
                                col.isTrigger = true;
                                col.enabled = true;
                            }
                        }
                        obj.layer = 0;
                    }
                }
            }
        }

        // =========================================================================
        // SECTION 4: TOP TOOLBAR WITH DROPDOWN MENUS
        // =========================================================================

        private static void BuildTopToolbar()
        {
            _toolbarPanel = CreatePanelPrimitive(_canvasRoot.transform, "Top_Toolbar",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -18f), new Vector2(0f, 36f),
                new Color(0.09f, 0.10f, 0.13f, 0.98f));

            HorizontalLayoutGroup hlg = _toolbarPanel.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 8, 4, 4);
            hlg.spacing = 6f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            // 1. Dropdown: Options
            CreateDropdownMenuButton(_toolbarPanel.transform, "Options", 80f, (anchor) =>
            {
                OpenDropdownMenu(anchor, new List<DropdownItem>
                {
                    new DropdownItem("Preferences & Shortcuts", () => _preferencesWin?.Toggle()),
                    new DropdownItem("Save Level (F5)", () => LevelPersistenceService.SaveLevel(MapBrowserService.SelectedMapName)),
                    new DropdownItem("Load Level (F6)", () => LevelPersistenceService.LoadLevel(MapBrowserService.SelectedMapName)),
                    new DropdownItem("Capture Snapshot (F4)", () => ThumbnailCaptureService.CaptureLevelThumbnail(MapBrowserService.SelectedMapPath, EditorSessionManager.PlacedObjects, EditorSessionManager.LevelSpawnPosition)),
                    new DropdownItem("Reset Level", () => EditorSessionManager.ClearAllPlacedObjects(), new Color(0.9f, 0.3f, 0.3f))
                });
            });

            // 2. Dropdown: Tools
            CreateDropdownMenuButton(_toolbarPanel.transform, "Tools", 75f, (anchor) =>
            {
                OpenDropdownMenu(anchor, new List<DropdownItem>
                {
                    new DropdownItem("Find Occurrences...", () => OpenOccurrencesWindowForSelection()),
                    new DropdownItem("Uniform Scaler Tool...", () => _uniformScalerWin?.Show()),
                    new DropdownItem("Distribute Spacing Tool...", () => _distributeSpacingWin?.Show()),
                    new DropdownItem("Batch Renamer & Indexer...", () => _batchRenamerWin?.Show()),
                    new DropdownItem("Replace with Equipped Prop", () => SwapSelectedObjectsWithEquipped()),
                    new DropdownItem("+ Procedural Wire / Cable", () =>
                    {
                        Vector3 camPos = EditorViewportCamera.ViewportCamera != null 
                            ? EditorViewportCamera.ViewportCamera.transform.position + EditorViewportCamera.ViewportCamera.transform.forward * 8f 
                            : EditorSessionManager.LevelSpawnPosition;
        
                        GameObject cable = ProceduralCableService.CreateProceduralCable(camPos, new Vector3(-4f, 1f, 0f), new Vector3(4f, -0.5f, 0f));
                        EditorSessionManager.SelectObject(cable);
                        RefreshHierarchy();
                    }),
                    new DropdownItem("+ Procedural Wire / Cable", () =>
                    {
                        Vector3 camPos = EditorViewportCamera.ViewportCamera != null
                            ? EditorViewportCamera.ViewportCamera.transform.position + EditorViewportCamera.ViewportCamera.transform.forward * 8f
                            : EditorSessionManager.LevelSpawnPosition;

                        GameObject cable = ProceduralCableService.CreateProceduralCable(camPos, new Vector3(-4f, 1f, 0f), new Vector3(4f, -0.5f, 0f));
                        EditorSessionManager.SelectObject(cable);
                        RefreshHierarchy();
                    }),
                    new DropdownItem("+ Procedural Space-Truss Girder", () =>
                    {
                        Vector3 camPos = EditorViewportCamera.ViewportCamera != null
                            ? EditorViewportCamera.ViewportCamera.transform.position + EditorViewportCamera.ViewportCamera.transform.forward * 10f
                            : EditorSessionManager.LevelSpawnPosition;

                        GameObject truss = StructuralTrussService.CreateProceduralTruss(camPos, new Vector3(-5f, 0f, 0f), new Vector3(5f, 0f, 0f));
                        EditorSessionManager.SelectObject(truss);
                        RefreshHierarchy();
                    }),
                    new DropdownItem("+ Tech Monolith Silhouette Cluster", () =>
                    {
                        Vector3 camPos = EditorViewportCamera.ViewportCamera != null
                            ? EditorViewportCamera.ViewportCamera.transform.position + EditorViewportCamera.ViewportCamera.transform.forward * 45f
                            : EditorSessionManager.LevelSpawnPosition + Vector3.forward * 60f;

                        GameObject cluster = TechMonolithService.CreateTechMonolithCluster(camPos, baseScale: 35f);
                        EditorSessionManager.SelectObject(cluster);
                        RefreshHierarchy();
                    }),
                });
            });

            // 3. Dropdown: Edit
            CreateDropdownMenuButton(_toolbarPanel.transform, "Edit", 70f, (anchor) =>
            {
                OpenDropdownMenu(anchor, new List<DropdownItem>
                {
                    new DropdownItem("Undo (Ctrl+Z)", () => EditorSessionManager.PerformUndo()),
                    new DropdownItem("Redo (Ctrl+Y)", () => EditorSessionManager.PerformRedo()),
                    new DropdownItem("Duplicate (Ctrl+D)", () => EditorSessionManager.DuplicateSelectedObjects()),
                    new DropdownItem("Create Prefab from Selected", () => QuickAutoCreatePrefab()),
                    new DropdownItem("Snap Rotation to 90 (Alt+R)", () => ExecuteSnap90()),
                    new DropdownItem("Reset Rotation to 0 (Alt+Shift+R)", () => ExecuteResetRotation()),
                    new DropdownItem("Parent Selected (Ctrl+P)", () => EditorSessionManager.ParentSelectedObjects()),
                    new DropdownItem("Unparent Selected (Alt+P)", () => EditorSessionManager.UnparentSelectedObjects())
                });
            });

            CreateToolbarDivider(_toolbarPanel.transform);

            // Direct Transform Buttons
            CreateButtonPrimitive(_toolbarPanel.transform, "Btn_Translate", "Move", 70f, () => SetGizmoMode(EditorGizmoMode.Translate));
            CreateButtonPrimitive(_toolbarPanel.transform, "Btn_Rotate", "Rotate", 70f, () => SetGizmoMode(EditorGizmoMode.Rotate));
            CreateButtonPrimitive(_toolbarPanel.transform, "Btn_Scale", "Scale", 70f, () => SetGizmoMode(EditorGizmoMode.Scale));

            CreateToolbarDivider(_toolbarPanel.transform);

            // Direct Alignment & Snapping
            Button alignBtn = CreateButtonPrimitive(_toolbarPanel.transform, "Btn_SurfaceAlign", "Align: OFF", 85f, () =>
            {
                EditorSessionManager.AutoAlignToSurface = !EditorSessionManager.AutoAlignToSurface;
                string st = EditorSessionManager.AutoAlignToSurface ? "ON" : "OFF";
                if (_surfaceAlignBtnText != null) _surfaceAlignBtnText.text = $"Align: {st}";
                PlacementHologramController.ApplyRotationToPreview();
            });
            _surfaceAlignBtnText = alignBtn.GetComponentInChildren<TMP_Text>();

            Button snapBtn = CreateButtonPrimitive(_toolbarPanel.transform, "Btn_GridSnap", "Snap: 1.0m", 85f, () =>
            {
                if (EditorSessionManager.CurrentGridSnap == 1.0f) EditorSessionManager.CurrentGridSnap = 2.0f;
                else if (EditorSessionManager.CurrentGridSnap == 2.0f) EditorSessionManager.CurrentGridSnap = 4.0f;
                else if (EditorSessionManager.CurrentGridSnap == 4.0f) EditorSessionManager.CurrentGridSnap = 0.5f;
                else if (EditorSessionManager.CurrentGridSnap == 0.5f) EditorSessionManager.CurrentGridSnap = 0.0f;
                else EditorSessionManager.CurrentGridSnap = 1.0f;

                string st = (EditorSessionManager.CurrentGridSnap > 0.01f) ? $"{EditorSessionManager.CurrentGridSnap}m" : "OFF";
                if (_gridSnapBtnText != null) _gridSnapBtnText.text = $"Snap: {st}";
            });
            _gridSnapBtnText = snapBtn.GetComponentInChildren<TMP_Text>();

            GameObject spacer = new GameObject("Spacer", Il2CppType.Of<RectTransform>());
            spacer.transform.SetParent(_toolbarPanel.transform, false);
            LayoutElement sle = spacer.AddComponent<LayoutElement>();
            sle.flexibleWidth = 1f;

            CreateButtonPrimitive(_toolbarPanel.transform, "Btn_Playtest", "PLAYTEST (F1)", 125f, () => EditorSessionManager.ToggleEditMode(), new Color(0.18f, 0.65f, 0.32f));
        }

        private static void CreateToolbarDivider(Transform parent)
        {
            GameObject div = new GameObject("Divider", Il2CppType.Of<RectTransform>());
            div.transform.SetParent(parent, false);
            LayoutElement le = div.AddComponent<LayoutElement>();
            le.preferredWidth = 2f;
            le.minWidth = 2f;
            le.preferredHeight = 20f;
            div.AddComponent<Image>().color = new Color(0.20f, 0.24f, 0.30f, 0.8f);
        }

        private static void BuildModePill()
        {
            GameObject pillObj = new GameObject("Floating_Mode_Pill", Il2CppType.Of<RectTransform>());
            pillObj.transform.SetParent(_canvasRoot.transform, false);

            RectTransform prt = pillObj.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 1f);
            prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.anchoredPosition = new Vector2(0f, -42f);
            prt.sizeDelta = new Vector2(210f, 28f);

            Image pImg = pillObj.AddComponent<Image>();
            pImg.color = new Color(0.18f, 0.45f, 0.85f, 0.95f);

            _modeTogglePillBtn = pillObj.AddComponent<Button>();
            _modeTogglePillBtn.targetGraphic = pImg;
            _modeTogglePillBtn.onClick.AddListener((Action)(() =>
            {
                if (EditorSessionManager.InteractionMode == EditorInteractionMode.SelectMode)
                    EditorSessionManager.SetInteractionMode(EditorInteractionMode.PlacementMode);
                else
                    EditorSessionManager.SetInteractionMode(EditorInteractionMode.SelectMode);
                RefreshModeDisplay();
            }));

            _modeTogglePillText = CreateTextPrimitive(pillObj.transform, "MODE: [SELECT]", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 11f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        }

        public static void RefreshModeDisplay()
        {
            if (_modeTogglePillText == null || _modeTogglePillBtn == null) return;

            if (EditorSessionManager.InteractionMode == EditorInteractionMode.SelectMode)
            {
                _modeTogglePillText.text = "MODE: [SELECT]";
                _modeTogglePillBtn.image.color = new Color(0.18f, 0.45f, 0.85f, 0.95f);
            }
            else
            {
                _modeTogglePillText.text = "MODE: [PLACEMENT]";
                _modeTogglePillBtn.image.color = new Color(0.85f, 0.45f, 0.15f, 0.95f);
            }
        }

        private static void SetGizmoMode(EditorGizmoMode mode)
        {
            EditorSessionManager.SetInteractionMode(EditorInteractionMode.SelectMode);
            EditorSessionManager.CurrentGizmoMode = mode;
            RefreshModeDisplay();
        }

        public static void ExecuteSnap90()
        {
            if (EditorSessionManager.SelectedObject == null) return;
            Vector3 e = EditorSessionManager.SelectedObject.transform.eulerAngles;
            e.x = Mathf.Round(e.x / 90f) * 90f;
            e.y = Mathf.Round(e.y / 90f) * 90f;
            e.z = Mathf.Round(e.z / 90f) * 90f;
            EditorSessionManager.SelectedObject.transform.rotation = Quaternion.Euler(e);
            StudioGizmoController.InvalidateCachedCenter(EditorSessionManager.SelectedObject);
            RefreshInspectorValues();
        }

        public static void ExecuteResetRotation()
        {
            if (EditorSessionManager.SelectedObject == null) return;
            EditorSessionManager.SelectedObject.transform.rotation = Quaternion.identity;
            StudioGizmoController.InvalidateCachedCenter(EditorSessionManager.SelectedObject);
            RefreshInspectorValues();
        }

        public static void QuickAutoCreatePrefab()
        {
            if (EditorSessionManager.SelectedObjects == null || EditorSessionManager.SelectedObjects.Count == 0)
            {
                SetNotificationText("Select objects first to make a Prefab.");
                return;
            }
            int idx = PrefabInstanceManager.SavedPrefabs.Count + 1;
            string autoName = $"Prefab_{idx:D2}";
            PrefabInstanceManager.CreateInstanceTemplateFromSelection(autoName);
            RebuildCategoryDockButtons();
            RefreshAssetBrowser();
        }

        private class DropdownItem
        {
            public string Label;
            public Action Callback;
            public Color? TextColor;
            public DropdownItem(string label, Action cb, Color? col = null) { Label = label; Callback = cb; TextColor = col; }
        }

        private static RectTransform _activeDropdownAnchor = null;

        private static void CreateDropdownMenuButton(Transform parent, string label, float width, Action<RectTransform> onTrigger)
        {
            GameObject btnObj = new GameObject("Btn_" + label.Replace(" ", "_"), Il2CppType.Of<RectTransform>());
            btnObj.transform.SetParent(parent, false);

            LayoutElement le = btnObj.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            le.preferredHeight = 24f;

            Image img = btnObj.AddComponent<Image>();
            img.color = new Color(0.16f, 0.18f, 0.24f, 0.95f);

            Button btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = img;

            TMP_Text txt = CreateTextPrimitive(btnObj.transform, label, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10.5f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
            txt.raycastTarget = false;

            RectTransform rt = btnObj.GetComponent<RectTransform>();
            btn.onClick.AddListener((Action)(() => onTrigger?.Invoke(rt)));
        }

        private static void OpenDropdownMenu(RectTransform anchor, List<DropdownItem> items)
        {
            if (_activeDropdownMenu != null && _activeDropdownAnchor == anchor)
            {
                CloseAllDropdowns();
                return;
            }

            CloseAllDropdowns();
            if (anchor == null || items == null || items.Count == 0) return;

            _activeDropdownAnchor = anchor;

            _activeDropdownMenu = new GameObject("Dropdown_Popup", Il2CppType.Of<RectTransform>());
            _activeDropdownMenu.layer = 5;
            _activeDropdownMenu.transform.SetParent(anchor, false);

            RectTransform dmRt = _activeDropdownMenu.GetComponent<RectTransform>();
            dmRt.anchorMin = new Vector2(0f, 0f);
            dmRt.anchorMax = new Vector2(0f, 0f);
            dmRt.pivot = new Vector2(0f, 1f);
            dmRt.anchoredPosition = new Vector2(0f, -4f);
            dmRt.sizeDelta = new Vector2(230f, items.Count * 28f + 8f);

            Canvas dropCanvas = _activeDropdownMenu.AddComponent<Canvas>();
            dropCanvas.overrideSorting = true;
            dropCanvas.sortingOrder = 1200;
            _activeDropdownMenu.AddComponent<GraphicRaycaster>();

            Image dmBg = _activeDropdownMenu.AddComponent<Image>();
            dmBg.color = new Color(0.09f, 0.11f, 0.15f, 0.98f);

            VerticalLayoutGroup vlg = _activeDropdownMenu.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 3f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            for (int i = 0; i < items.Count; i++)
            {
                DropdownItem item = items[i];
                GameObject entry = new GameObject("Entry_" + item.Label, Il2CppType.Of<RectTransform>());
                entry.transform.SetParent(_activeDropdownMenu.transform, false);

                LayoutElement ele = entry.AddComponent<LayoutElement>();
                ele.preferredHeight = 24f;
                ele.minHeight = 24f;

                Image eImg = entry.AddComponent<Image>();
                eImg.color = new Color(0.15f, 0.18f, 0.24f, 0.95f);

                Button eBtn = entry.AddComponent<Button>();
                eBtn.targetGraphic = eImg;
                eBtn.onClick.AddListener((Action)(() =>
                {
                    CloseAllDropdowns();
                    item.Callback?.Invoke();
                }));

                TMP_Text itemTxt = CreateTextPrimitive(entry.transform, item.Label, Vector2.zero, Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, 0f), 10.5f, FontStyles.Normal, item.TextColor ?? Color.white, TextAlignmentOptions.MidlineLeft);
                itemTxt.raycastTarget = false;
            }
        }

        private static void CloseAllDropdowns()
        {
            if (_activeDropdownMenu != null)
            {
                GameObject.Destroy(_activeDropdownMenu);
                _activeDropdownMenu = null;
            }
            _activeDropdownAnchor = null;
        }

        // =========================================================================
        // SECTION 5: FLOATING CATEGORY DOCK
        // =========================================================================

        private static void BuildFloatingCategoryDock()
        {
            _floatingCategoryDock = new GameObject("Floating_Category_Dock", Il2CppType.Of<RectTransform>());
            _floatingCategoryDock.transform.SetParent(_canvasRoot.transform, false);

            RectTransform dockRt = _floatingCategoryDock.GetComponent<RectTransform>();
            dockRt.anchorMin = new Vector2(0.5f, 0f);
            dockRt.anchorMax = new Vector2(0.5f, 0f);
            dockRt.pivot = new Vector2(0.5f, 0f);
            dockRt.anchoredPosition = new Vector2(0f, 268f);
            dockRt.sizeDelta = new Vector2(700f, 32f);

            Image dockBg = _floatingCategoryDock.AddComponent<Image>();
            dockBg.color = new Color(0.08f, 0.09f, 0.12f, 0.95f);

            HorizontalLayoutGroup hlg = _floatingCategoryDock.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(6, 6, 3, 3);
            hlg.spacing = 6f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            RebuildCategoryDockButtons();
        }

        public static void RebuildCategoryDockButtons()
        {
            if (_floatingCategoryDock == null) return;

            // 1. DESTROY ALL PREVIOUS SIBLINGS IN THE DOCK (prevents + Tab and Badge duplicates)
            for (int i = _floatingCategoryDock.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = _floatingCategoryDock.transform.GetChild(i);
                if (child != null) GameObject.Destroy(child.gameObject);
            }
            _categoryDockButtons.Clear();

            // 2. "ALL" TAB (Always first)
            Button allBtn = CreateButtonPrimitive(_floatingCategoryDock.transform, "TabDock_All", "All", 75f, () =>
            {
                _activeBrowserCategory = "All";
                UpdateCategoryDockStyles();
                RefreshAssetBrowser();
            }, new Color(0.14f, 0.16f, 0.20f, 0.90f));
            _categoryDockButtons["All"] = allBtn;

            // 3. USER-CREATED TABS ONLY
            foreach (var customCat in EditorConfigService.Config.CustomCategories.Keys)
            {
                string catName = customCat;
                Button customBtn = CreateButtonPrimitive(_floatingCategoryDock.transform, "TabDock_" + catName, catName, 90f, () =>
                {
                    _activeBrowserCategory = catName;
                    UpdateCategoryDockStyles();
                    RefreshAssetBrowser();
                }, new Color(0.14f, 0.16f, 0.20f, 0.90f));
                _categoryDockButtons[catName] = customBtn;
            }

            // 4. [+ TAB] BUTTON (Single instance appended at the end of tabs)
            CreateButtonPrimitive(_floatingCategoryDock.transform, "Btn_AddNewTabDock", "+ Tab", 60f, () =>
            {
                _assignTargetAsset = null;
                OpenAssignCategoryWindow(null);
            }, new Color(0.18f, 0.55f, 0.35f, 0.95f));

            // 5. FLEXIBLE SPACER TO PUSH BADGE TO THE FAR RIGHT
            GameObject spacer = new GameObject("DockSpacer", Il2CppType.Of<RectTransform>());
            spacer.transform.SetParent(_floatingCategoryDock.transform, false);
            LayoutElement sle = spacer.AddComponent<LayoutElement>();
            sle.flexibleWidth = 1f;

            // 6. PROPS COUNT BADGE
            GameObject badgeObj = new GameObject("AssetCountBadge", Il2CppType.Of<RectTransform>());
            badgeObj.transform.SetParent(_floatingCategoryDock.transform, false);
            LayoutElement ble = badgeObj.AddComponent<LayoutElement>();
            ble.preferredWidth = 85f;
            ble.minWidth = 85f;
            ble.flexibleWidth = 0f;
            badgeObj.AddComponent<Image>().color = new Color(0.12f, 0.15f, 0.22f, 0.9f);

            _assetCountBadgeText = CreateTextPrimitive(badgeObj.transform, "0 Props", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Bold, new Color(0.25f, 0.85f, 1f), TextAlignmentOptions.Center);

            // Revert fallback to "All" if current active tab was removed
            if (_activeBrowserCategory != "All" && !EditorConfigService.Config.CustomCategories.ContainsKey(_activeBrowserCategory))
                _activeBrowserCategory = "All";

            UpdateCategoryDockStyles();
        }
        private static void UpdateCategoryDockStyles()
        {
            foreach (var kvp in _categoryDockButtons)
            {
                bool isSelected = kvp.Key == _activeBrowserCategory;
                kvp.Value.image.color = isSelected ? new Color(0.18f, 0.52f, 0.92f, 0.98f) : new Color(0.14f, 0.16f, 0.20f, 0.90f);
                TMP_Text txt = kvp.Value.GetComponentInChildren<TMP_Text>();
                if (txt != null)
                {
                    txt.color = isSelected ? Color.white : new Color(0.75f, 0.80f, 0.88f);
                    txt.fontStyle = isSelected ? FontStyles.Bold : FontStyles.Normal;
                }
            }
        }

        // =========================================================================
        // SECTION 6: ASSET BROWSER (SQUARE THUMBNAILS & CLAMPED SCROLLING)
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

            // Top Control Strip
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

            Button sortBtn = CreateButtonPrimitive(controlStrip.transform, "Btn_ToggleSortOrder", "▲ Size", 65f, () =>
            {
                _sortSizeAscending = !_sortSizeAscending;
                if (_sortSizeBtnText != null) _sortSizeBtnText.text = _sortSizeAscending ? "▲ Size" : "▼ Size";
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
                "Search asset palette...", (s) => RefreshAssetBrowser());
            if (_browserSearchInput != null)
            {
                LayoutElement searchLe = _browserSearchInput.gameObject.AddComponent<LayoutElement>();
                searchLe.preferredWidth = 200f;
                searchLe.flexibleWidth = 1f;
            }

            // Scroll View Container
            GameObject scrollObj = CreateScrollViewPrimitive(_assetBrowserPanel.transform, "Browser_Scroll",
                Vector2.zero, Vector2.one,
                new Vector2(8f, 6f), new Vector2(-16f, -42f),
                out _browserContent);

            if (scrollObj != null)
            {
                // Ensure the background of the scroll view catches raycasts
                Image scrollImg = scrollObj.GetComponent<Image>() ?? scrollObj.AddComponent<Image>();
                scrollImg.color = Color.clear;
                scrollImg.raycastTarget = true;

                _assetBrowserScrollRect = scrollObj.GetComponent<ScrollRect>();
                if (_assetBrowserScrollRect != null)
                {
                    _assetBrowserScrollRect.movementType = ScrollRect.MovementType.Clamped;
                    _assetBrowserScrollRect.scrollSensitivity = 35f;
                }

                // Ensure the viewport catches raycasts over empty margins
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

                // 1. Minimum Size Check (enforce >= 1.4m by default, bypassable with Tiny toggle)
                if (!_includeTinyProps && asset.MaxDimension < minCutoff && !asset.IsPrefabInstance && (asset.Traits & AssetTrait.Architecture) != 0)
                    continue;

                // 2. Dynamic Custom Category Filter ("All" shows everything, otherwise checks custom tabs created from scratch)
                if (_activeBrowserCategory != "All")
                {
                    if (!EditorConfigService.IsAssetInCategory(_activeBrowserCategory, asset.DisplayName))
                        continue;
                }

                // 3. Size Tier Filter
                if (_activeSizeFilter != AssetSizeTier.All && asset.SizeTier != _activeSizeFilter)
                    continue;

                // 4. Multi-token Search Filter
                if (searchTokens != null && searchTokens.Length > 0)
                {
                    string dName = asset.DisplayName.ToLowerInvariant();
                    string subCat = (asset.SubCategory ?? "").ToLowerInvariant();
                    string badge = asset.GetSizeBadgeText().ToLowerInvariant();
                    string tier = asset.SizeTier.ToString().ToLowerInvariant();

                    bool allTokensMatched = true;
                    for (int t = 0; t < searchTokens.Length; t++)
                    {
                        string token = searchTokens[t];
                        bool tokenFound = dName.Contains(token) || subCat.Contains(token) || badge.Contains(token) || tier.Contains(token);
                        if (!tokenFound) { allTokensMatched = false; break; }
                    }
                    if (!allTokensMatched) continue;
                }

                matchedAssets.Add(asset);
            }

            matchedAssets.Sort((a, b) =>
            {
                int cmp = a.MaxDimension.CompareTo(b.MaxDimension);
                return _sortSizeAscending ? cmp : -cmp;
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

                // EventTrigger captures Right-Click AND forwards Scroll/Drag so it doesn't block scrolling
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

                // FORWARD SCROLL TO SCROLLRECT
                var scrollEntry = new EventTrigger.Entry { eventID = EventTriggerType.Scroll };
                scrollEntry.callback.AddListener((Action<BaseEventData>)((e) =>
                {
                    if (_assetBrowserScrollRect != null)
                        _assetBrowserScrollRect.OnScroll(e.Cast<PointerEventData>());
                }));
                trigger.triggers.Add(scrollEntry);

                // FORWARD DRAG TO SCROLLRECT
                var dragEntry = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
                dragEntry.callback.AddListener((Action<BaseEventData>)((e) =>
                {
                    if (_assetBrowserScrollRect != null)
                        _assetBrowserScrollRect.OnDrag(e.Cast<PointerEventData>());
                }));
                trigger.triggers.Add(dragEntry);

                // Strict 1:1 Square Thumbnail Container
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

                // Size Badge
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

                // Options/Context Dots Button
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

                // Label
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
        // SECTION 7: ASSET BROWSER CONTEXT MENU
        // =========================================================================

        private static void BuildContextMenuOverlay()
        {
            _contextMenuRoot = new GameObject("Asset_Context_Menu", Il2CppType.Of<RectTransform>());
            _contextMenuRoot.transform.SetParent(_canvasRoot.transform, false);

            RectTransform cmRt = _contextMenuRoot.GetComponent<RectTransform>();
            cmRt.sizeDelta = new Vector2(220f, 120f);
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

            CreateContextMenuOption("🔍 Find Occurrences in Scene", () =>
            {
                if (_contextTargetAsset != null) OpenOccurrencesWindowForAsset(_contextTargetAsset.DisplayName);
                CloseContextMenu();
            });

            CreateContextMenuOption("🏷 Manage Tabs for Prop...", () =>
            {
                var target = _contextTargetAsset;
                CloseContextMenu();
                if (target != null) OpenAssignCategoryWindow(target);
            });

            CreateContextMenuOption("📋 Copy Asset Name", () =>
            {
                if (_contextTargetAsset != null) GUIUtility.systemCopyBuffer = _contextTargetAsset.DisplayName;
                CloseContextMenu();
            });

            CreateContextMenuOption("✕ Delete Custom Prefab", () =>
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
            if (clamped.y - 125f < 0f) clamped.y += 125f;

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
        // SECTION 8: DRAGGABLE FLOATING TOOL WINDOWS
        // =========================================================================

        // 1. Preferences & Shortcuts Window
        private static void BuildPreferencesWindow()
        {
            _preferencesWin = StudioFloatingWindow.Create(_canvasRoot.transform, "Win_Preferences", "Editor Preferences & Shortcuts", new Vector2(480f, 440f), Vector2.zero);

            GameObject scrollObj = CreateScrollViewPrimitive(_preferencesWin.ContentRt, "Prefs_Scroll", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, out RectTransform content);

            // General & Tuning Card
            var generalCard = CreateModularSection(content, "General", "General & Tuning");
            AddSliderRow(generalCard.transform, "Min Asset Cutoff (m)", 0.2f, 5.0f, EditorConfigService.Config.MinAssetSize, "{0:F1}m", (val) =>
            {
                EditorConfigService.Config.MinAssetSize = (float)Math.Round(val, 1);
                EditorConfigService.SaveConfig();
                RefreshAssetBrowser();
            });

            AddSliderRow(generalCard.transform, "Flycam Speed", 6f, 80f, EditorConfigService.Config.FlycamSpeed, "{0:F0}", (val) =>
            {
                EditorConfigService.Config.FlycamSpeed = val;
                EditorConfigService.SaveConfig();
            });

            AddSliderRow(generalCard.transform, "Mouse Sensitivity", 0.5f, 6.0f, EditorConfigService.Config.MouseSensitivity, "{0:F1}", (val) =>
            {
                EditorConfigService.Config.MouseSensitivity = val;
                EditorConfigService.SaveConfig();
            });

            AddSliderRow(generalCard.transform, "Viewport FOV", 45f, 100f, EditorConfigService.Config.EditorFov, "{0:F0}°", (val) =>
            {
                EditorConfigService.Config.EditorFov = val;
                if (EditorViewportCamera.ViewportCamera != null) EditorViewportCamera.ViewportCamera.fieldOfView = val;
                EditorConfigService.SaveConfig();
            });

            AddSliderRow(generalCard.transform, "Gizmo Scale Mult", 0.4f, 2.5f, EditorConfigService.Config.GizmoScaleMultiplier, "{0:F2}x", (val) =>
            {
                EditorConfigService.Config.GizmoScaleMultiplier = val;
                StudioGizmoController.GizmoScaleMultiplier = val;
                EditorConfigService.SaveConfig();
            });

            AddToggleRow(generalCard.transform, "Invert Look Y-Axis", EditorConfigService.Config.InvertLookY, (toggled) =>
            {
                EditorConfigService.Config.InvertLookY = toggled;
                EditorConfigService.SaveConfig();
            });

            AddToggleRow(generalCard.transform, "Show Size Badges on Browser Cards", EditorConfigService.Config.ShowSizeBadges, (toggled) =>
            {
                EditorConfigService.Config.ShowSizeBadges = toggled;
                EditorConfigService.SaveConfig();
                RefreshAssetBrowser();
            });

            AddToggleRow(generalCard.transform, "Snapping Proxies Visible in Viewport", EditorConfigService.Config.SnappingProxiesVisible, (toggled) =>
            {
                EditorConfigService.Config.SnappingProxiesVisible = toggled;
                EditorSessionManager.SetSnappingProxiesActive(toggled);
                EditorConfigService.SaveConfig();
            });

            // Keyboard Shortcuts Card
            var shortcutCard = CreateModularSection(content, "Shortcuts", "Keyboard Shortcuts & Keybinds");
            CreateTextPrimitive(shortcutCard.transform, "Click any button to rebind. Press Esc to cancel or Del to unbind.", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 9.5f, FontStyles.Normal, Color.gray, TextAlignmentOptions.MidlineLeft);

            _shortcutDisplayLabels.Clear();
            foreach (var kvp in EditorConfigService.Config.Keybindings)
            {
                string actionKey = kvp.Key;
                GameObject row = CreateRowContainerPrimitive(shortcutCard.transform, "Row_Shortcut_" + actionKey, 24f);

                CreateTextPrimitive(row.transform, actionKey, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(4f, 0f), Vector2.zero, 9.5f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);

                Button rebindBtn = CreateButtonPrimitive(row.transform, "Btn_Rebind", kvp.Value, 100f, null, new Color(0.18f, 0.22f, 0.30f));
                RectTransform rbrt = rebindBtn.GetComponent<RectTransform>();
                rbrt.anchorMin = new Vector2(1f, 0.5f);
                rbrt.anchorMax = new Vector2(1f, 0.5f);
                rbrt.pivot = new Vector2(1f, 0.5f);
                rbrt.anchoredPosition = new Vector2(-4f, 0f);
                rbrt.sizeDelta = new Vector2(110f, 20f);

                TMP_Text label = rebindBtn.GetComponentInChildren<TMP_Text>();
                _shortcutDisplayLabels[actionKey] = label;

                rebindBtn.onClick.AddListener((Action)(() =>
                {
                    StartRebindingKey(actionKey, label);
                }));
            }

            Button resetKbBtn = CreateButtonPrimitive(shortcutCard.transform, "Btn_ResetKeybinds", "Reset to Default Shortcuts", 340f, () =>
            {
                EditorConfigService.Config.SetDefaultKeybindings();
                EditorConfigService.SaveConfig();
                foreach (var kvp in EditorConfigService.Config.Keybindings)
                {
                    if (_shortcutDisplayLabels.TryGetValue(kvp.Key, out TMP_Text lbl))
                        lbl.text = kvp.Value;
                }
            }, new Color(0.24f, 0.28f, 0.35f));
        }

        private static void StartRebindingKey(string actionKey, TMP_Text label)
        {
            _activeRebindingActionKey = actionKey;
            _activeRebindLabel = label;
            label.text = "<Press Key...>";
            label.color = Color.yellow;
        }

        public static void UpdateRebindingTick()
        {
            if (string.IsNullOrEmpty(_activeRebindingActionKey) || _activeRebindLabel == null) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _activeRebindLabel.text = EditorConfigService.Config.Keybindings[_activeRebindingActionKey];
                _activeRebindLabel.color = Color.white;
                _activeRebindingActionKey = null;
                _activeRebindLabel = null;
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
                return;
            }

            foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
            {
                if (kc == KeyCode.LeftControl || kc == KeyCode.RightControl ||
                    kc == KeyCode.LeftAlt || kc == KeyCode.RightAlt ||
                    kc == KeyCode.LeftShift || kc == KeyCode.RightShift ||
                    kc == KeyCode.None || kc == KeyCode.Escape)
                {
                    continue;
                }

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

                    EditorConfigService.Config.Keybindings[_activeRebindingActionKey] = combo;
                    _activeRebindLabel.text = combo;
                    _activeRebindLabel.color = Color.white;
                    EditorConfigService.SaveConfig();

                    _activeRebindingActionKey = null;
                    _activeRebindLabel = null;
                    break;
                }
            }
        }

        // 2. Occurrences Finder Window
        private static void BuildOccurrencesWindow()
        {
            _occurrencesWin = StudioFloatingWindow.Create(_canvasRoot.transform, "Win_Occurrences", "Scene Occurrences", new Vector2(360f, 400f), new Vector2(-150f, 50f));

            _occurrencesTitleHeader = CreateTextPrimitive(_occurrencesWin.ContentRt, "Occurrences: None", Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, 15f), new Vector2(0f, 24f), 11f, FontStyles.Bold, Color.cyan, TextAlignmentOptions.MidlineLeft);

            CreateScrollViewPrimitive(_occurrencesWin.ContentRt, "Occurrences_Scroll", Vector2.zero, Vector2.one, new Vector2(0f, 34f), new Vector2(0f, -60f), out _occurrencesContent);

            Button massSelectBtn = CreateButtonPrimitive(_occurrencesWin.ContentRt, "Btn_MassSelect", "Select All Occurrences", 340f, () =>
            {
                MassSelectCurrentOccurrences();
            }, new Color(0.18f, 0.45f, 0.85f, 1f));
            RectTransform msrt = massSelectBtn.GetComponent<RectTransform>();
            msrt.anchorMin = new Vector2(0f, 0f);
            msrt.anchorMax = new Vector2(1f, 0f);
            msrt.pivot = new Vector2(0.5f, 0f);
            msrt.sizeDelta = new Vector2(0f, 28f);
            msrt.anchoredPosition = Vector2.zero;
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

        // 3. Uniform Scaler Window
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

            CreateButtonPrimitive(_uniformScalerWin.ContentRt, "Btn_ApplyScale", "Apply Uniform Scale Multiplier", 320f, () =>
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
            {
                centroid += EditorSessionManager.SelectedObjects[i].transform.position;
            }
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
                {
                    newPos = centroid + (oldPos - centroid) * factor;
                }

                obj.transform.position = newPos;
                obj.transform.localScale = newScale;

                StudioGizmoController.InvalidateCachedCenter(obj);

                EditorSessionManager.UndoHistory.Push(new HistoryRecord
                {
                    ActionType = HistoryActionType.Reposition,
                    TargetObject = obj,
                    PreviousPosition = oldPos,
                    NewPosition = newPos,
                    PreviousRotation = obj.transform.rotation,
                    NewRotation = obj.transform.rotation,
                    PreviousScale = oldScale,
                    NewScale = newScale,
                    EntityDataSnapshot = EditorSessionManager.ExtractEntityData(obj)
                });
            }

            EditorSessionManager.RedoHistory.Clear();
            RefreshInspectorValues();
            EditorSessionManager.UpdateSelectionHighlight();
        }

        // 4. Distribute Spacing Tool Window
        private static void BuildDistributeSpacingWindow()
        {
            _distributeSpacingWin = StudioFloatingWindow.Create(_canvasRoot.transform, "Win_Distribute", "Distribute Spacing Tool", new Vector2(360f, 210f), Vector2.zero);

            VerticalLayoutGroup vlg = _distributeSpacingWin.ContentRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            CreateTextPrimitive(_distributeSpacingWin.ContentRt, "Evenly space 3+ selected objects along an axis:", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);

            GameObject btnRow = CreateRowContainerPrimitive(_distributeSpacingWin.ContentRt, "Dist_AxisBtns", 28f);
            SetupRowHorizontalLayoutPrimitive(btnRow, 6f);

            CreateButtonPrimitive(btnRow.transform, "Dist_X", "Distribute X", 100f, () => DistributeSelectedAlongAxis(0), new Color(0.85f, 0.25f, 0.25f, 1f));
            CreateButtonPrimitive(btnRow.transform, "Dist_Y", "Distribute Y", 100f, () => DistributeSelectedAlongAxis(1), new Color(0.25f, 0.85f, 0.35f, 1f));
            CreateButtonPrimitive(btnRow.transform, "Dist_Z", "Distribute Z", 100f, () => DistributeSelectedAlongAxis(2), new Color(0.25f, 0.55f, 0.95f, 1f));

            CreateTextPrimitive(_distributeSpacingWin.ContentRt, "Align all selected objects to match primary:", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);

            GameObject alignRow = CreateRowContainerPrimitive(_distributeSpacingWin.ContentRt, "Align_AxisBtns", 28f);
            SetupRowHorizontalLayoutPrimitive(alignRow, 6f);

            CreateButtonPrimitive(alignRow.transform, "Align_X", "Align X", 100f, () => AlignSelectedAlongAxis(0));
            CreateButtonPrimitive(alignRow.transform, "Align_Y", "Align Y", 100f, () => AlignSelectedAlongAxis(1));
            CreateButtonPrimitive(alignRow.transform, "Align_Z", "Align Z", 100f, () => AlignSelectedAlongAxis(2));
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

        private static void AlignSelectedAlongAxis(int axis)
        {
            if (EditorSessionManager.SelectedObjects == null || EditorSessionManager.SelectedObjects.Count < 2 || EditorSessionManager.SelectedObject == null)
                return;

            Vector3 target = EditorSessionManager.SelectedObject.transform.position;
            for (int i = 0; i < EditorSessionManager.SelectedObjects.Count; i++)
            {
                GameObject o = EditorSessionManager.SelectedObjects[i];
                if (o == null || o == EditorSessionManager.SelectedObject) continue;

                Vector3 p = o.transform.position;
                if (axis == 0) p.x = target.x;
                else if (axis == 1) p.y = target.y;
                else p.z = target.z;

                o.transform.position = p;
                StudioGizmoController.InvalidateCachedCenter(o);
            }

            RefreshInspectorValues();
            EditorSessionManager.UpdateSelectionHighlight();
        }

        // 5. Assign Category Window
        private static void BuildAssignCategoryWindow()
        {
            _assignCategoryWin = StudioFloatingWindow.Create(_canvasRoot.transform, "Win_AssignCategory", "Custom Tabs & Categories", new Vector2(380f, 320f), Vector2.zero);

            VerticalLayoutGroup vlg = _assignCategoryWin.ContentRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Create new tab bar
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

            // Scroll view containing the tab list
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

                // Assign/Unassign toggle button
                string btnLabel = (_assignTargetAsset != null)
                    ? (isAssigned ? $"[✓]  {catName}" : $"[ + ]  {catName}")
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

                // Delete category button
                Button delBtn = CreateButtonPrimitive(row.transform, "Btn_DelCat_" + catName, "✕", 28f, () =>
                {
                    EditorConfigService.DeleteCategory(catName);
                    RebuildCategoryDockButtons();
                    RefreshCategoryListInAssignWindow();
                    RefreshAssetBrowser();
                }, new Color(0.65f, 0.2f, 0.2f, 0.9f));
                delBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = 28f;
            }
        }

        // 6. Batch Renamer Window
        private static void BuildBatchRenamerWindow()
        {
            _batchRenamerWin = StudioFloatingWindow.Create(_canvasRoot.transform, "Win_BatchRenamer", "Batch Renamer & Suffix Indexer", new Vector2(360f, 180f), Vector2.zero);

            VerticalLayoutGroup vlg = _batchRenamerWin.ContentRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            CreateTextPrimitive(_batchRenamerWin.ContentRt, "Batch rename selected objects sequentially:", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10.5f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);

            _batchRenameInput = CreateInputFieldPrimitive(_batchRenamerWin.ContentRt, "RenameInput", Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f, 26f), "Base Name (e.g. Tower Wall)", null);

            CreateButtonPrimitive(_batchRenamerWin.ContentRt, "Btn_ApplyRename", "Rename Selected Nodes", 330f, () =>
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
        }

        private static void SwapSelectedObjectsWithEquipped()
        {
            if (EditorSessionManager.CurrentAsset == null || EditorSessionManager.SelectedObjects == null || EditorSessionManager.SelectedObjects.Count == 0) return;

            CatalogAsset newAsset = EditorSessionManager.CurrentAsset;
            List<GameObject> toSwap = new List<GameObject>(EditorSessionManager.SelectedObjects);
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
                    swapped.transform.SetParent(parent, true);
                    EditorSessionManager.RegisterPlacedObject(swapped);
                    EditorSessionManager.SelectedObjects.Add(swapped);
                }

                EditorSessionManager.DeleteSpecifiedObject(old);
            }

            EditorSessionManager.SelectedObject = EditorSessionManager.SelectedObjects.Count > 0 ? EditorSessionManager.SelectedObjects[0] : null;
            RefreshHierarchy();
            NotifyObjectSelected(EditorSessionManager.SelectedObject);
            EditorSessionManager.UpdateSelectionHighlight();
        }

        // =========================================================================
        // SECTION 9: MODULAR INSPECTOR BUILDER
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

            _inspectorTitleText.rectTransform.offsetMax = new Vector2(-85f, 0f);
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

            // 1. JUMPER CARD
            if (data.Has<JumperConfig>() || type == PlacedObjectType.Jumper)
            {
                var card = CreateModularSection(_inspectorContent, "Jumper", "Jumper Launch Pad");
                var jc = data.GetOrCreate<JumperConfig>();
                AddSliderRow(card.transform, "Launch Force", 5f, 85f, jc.Force, "{0:F1}", (v) =>
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

            // 2. TURBINE CARD
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

            // 3. TURRET CARD
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

            // 4. LASER CARD
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

            // 5. LIGHTING CARD
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

                if (!lc.IsDirectional)
                {
                    AddSliderRow(card.transform, "Spot Angle", 10f, 150f, lc.SpotAngle, "{0:F0}°", (v) =>
                    {
                        lc.SpotAngle = v;
                        var targets = GetSelectionTargets(obj);
                        for (int t = 0; t < targets.Count; t++)
                            EditorSessionManager.ApplyLightConfig(targets[t], lc);
                    });
                }

                AddSliderRow(card.transform, "Volumetric", 0f, 10f, lc.VolumetricIntensity, "{0:F1}", (v) =>
                {
                    lc.VolumetricIntensity = v;
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

                _activeInspectorCards.Add(card);
            }

            // 6. SWITCH TARGET CARD
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

            // 7. SKYBOX CONTROLLER CARD
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

                AddSliderRow(card.transform, "Yaw Rotation", 0f, 360f, sc.YawOffset, "{0:F0}°", (v) =>
                {
                    sc.YawOffset = v;
                    sc.CurrentAngle = v;
                    SkyboxControllerService.ActiveConfig.YawOffset = v;
                    SkyboxControllerService.ActiveConfig.CurrentAngle = v;
                    SkyboxControllerService.ApplyRotation(v);
                });

                AddSliderRow(card.transform, "Spin Speed", -30f, 30f, sc.SpinSpeed, "{0:F1}°/s", (v) =>
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

            // 8. GATE & CHECKPOINT CARD
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

            // 9. MOTION PATH CARD
            GameObject pathOwner = obj;
            if (EditorSessionManager.IsWaypointMarker(obj, out GameObject resolvedOwner, out _)) pathOwner = resolvedOwner;

            if (pathOwner != null)
            {
                var card = CreateModularSection(_inspectorContent, "MotionPath", "Kinematic Motion Path");
                if (EditorSessionManager.MotionPaths.TryGetValue(pathOwner, out var mp) && mp != null)
                {
                    CreateTextPrimitive(card.transform, $"Active Path: {mp.TotalDistance:F1}m total travel", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Normal, Color.cyan, TextAlignmentOptions.MidlineLeft);

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

            // 10. NEON & EMISSIVE ACCENT CARD (Multi-Selection Enabled!)
            Renderer[] rends = obj.GetComponentsInChildren<Renderer>(true);
            bool hasMeshes = rends != null && rends.Length > 0 &&
                             type != PlacedObjectType.Spotlight &&
                             type != PlacedObjectType.Sunlight &&
                             type != PlacedObjectType.SkyboxController;

            if (hasMeshes)
            {
                var card = CreateModularSection(_inspectorContent, "Neon", "Neon & Emissive Accent");
                var nc = data.GetOrCreate<NeonConfig>();

                // Quick Palette Presets
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
                        else
                        {
                            var newCfg = nc.Clone();
                            newCfg.Intensity = v;
                            EditorSessionManager.ApplyNeonConfig(targets[t], newCfg);
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
                        else
                        {
                            var newCfg = nc.Clone();
                            newCfg.Color = newCol;
                            EditorSessionManager.ApplyNeonConfig(targets[t], newCfg);
                        }
                    }
                });

                _activeInspectorCards.Add(card);
            }

            // 11. PROCEDURAL CABLE & WIRE CARD
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

                // Style & Bundle Toggles
                GameObject styleRow = CreateRowContainerPrimitive(card.transform, "Row_CableStyle", 24f);
                SetupRowHorizontalLayoutPrimitive(styleRow, 4f);
                CreateButtonPrimitive(styleRow.transform, "Btn_ToggleStyle", $"Style: [{cc.Style}]", 130f, () =>
                {
                    cc.Style = (CableStyle)(((int)cc.Style + 1) % 3);
                    ProceduralCableService.ApplyCableConfig(cableTarget, cc);
                    RebuildModularInspectorCards(cableTarget);
                }, new Color(0.18f, 0.25f, 0.35f));

                CreateButtonPrimitive(styleRow.transform, "Btn_ToggleBundle", $"Cluster: [{cc.Bundle}]", 130f, () =>
                {
                    cc.Bundle = (CableBundleType)(((int)cc.Bundle + 1) % 3);
                    ProceduralCableService.ApplyCableConfig(cableTarget, cc);
                    RebuildModularInspectorCards(cableTarget);
                }, new Color(0.20f, 0.30f, 0.40f));

                AddToggleRow(card.transform, "Metal Wall Clamps / Sockets", cc.HasMountSockets, (st) =>
                {
                    cc.HasMountSockets = st;
                    ProceduralCableService.ApplyCableConfig(cableTarget, cc);
                });

                // Surface Snapping Buttons
                GameObject snapRow = CreateRowContainerPrimitive(card.transform, "Row_SnapHandles", 24f);
                SetupRowHorizontalLayoutPrimitive(snapRow, 4f);
                CreateButtonPrimitive(snapRow.transform, "Btn_SnapA", "Snap End [A] to Wall", 130f, () =>
                {
                    ProceduralCableService.SnapHandleToSurface(cableTarget, isPointB: false);
                }, new Color(0.18f, 0.45f, 0.30f));

                CreateButtonPrimitive(snapRow.transform, "Btn_SnapB", "Snap End [B] to Wall", 130f, () =>
                {
                    ProceduralCableService.SnapHandleToSurface(cableTarget, isPointB: true);
                }, new Color(0.45f, 0.30f, 0.18f));

                // Neon options
                if (cc.Style != CableStyle.IndustrialSolid)
                {
                    AddSliderRow(card.transform, "Energy Flow Spd", 0.0f, 8.0f, cc.EnergyFlowSpeed, "{0:F1}x", (v) =>
                    {
                        cc.EnergyFlowSpeed = v;
                        ProceduralCableService.ApplyCableConfig(cableTarget, cc);
                    });

                    AddSliderRow(card.transform, "Neon Glow Power", 0.5f, 10.0f, cc.GlowIntensity, "{0:F1}x", (v) =>
                    {
                        cc.GlowIntensity = v;
                        ProceduralCableService.ApplyCableConfig(cableTarget, cc);
                    });

                    AddColorControl(card.transform, "Conduit Color", cc.NeonColor, (newCol) =>
                    {
                        cc.NeonColor = newCol;
                        ProceduralCableService.ApplyCableConfig(cableTarget, cc);
                    });
                }

                _activeInspectorCards.Add(card);
            }

            // 12. PROCEDURAL SPACE-TRUSS GIRDER CARD
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

                AddSliderRow(card.transform, "Strut Thickness", 0.02f, 0.25f, tc.StrutThickness, "{0:F3}m", (v) =>
                {
                    tc.StrutThickness = v;
                    StructuralTrussService.ApplyTrussConfig(trussTarget, tc);
                });

                // Style Toggle
                GameObject styleRow = CreateRowContainerPrimitive(card.transform, "Row_TrussStyle", 24f);
                SetupRowHorizontalLayoutPrimitive(styleRow, 4f);
                CreateButtonPrimitive(styleRow.transform, "Btn_ToggleStyle", $"Style: [{tc.Style}]", 240f, () =>
                {
                    tc.Style = (TrussStyle)(((int)tc.Style + 1) % 3);
                    StructuralTrussService.ApplyTrussConfig(trussTarget, tc);
                    RebuildModularInspectorCards(trussTarget);
                }, new Color(0.18f, 0.25f, 0.35f));

                // Snap buttons
                GameObject snapRow = CreateRowContainerPrimitive(card.transform, "Row_SnapTruss", 24f);
                SetupRowHorizontalLayoutPrimitive(snapRow, 4f);
                CreateButtonPrimitive(snapRow.transform, "Btn_SnapA", "Anchor Joint [A]", 120f, () =>
                {
                    StructuralTrussService.SnapHandleToSurface(trussTarget, isPointB: false);
                }, new Color(0.18f, 0.45f, 0.30f));

                CreateButtonPrimitive(snapRow.transform, "Btn_SnapB", "Anchor Joint [B]", 120f, () =>
                {
                    StructuralTrussService.SnapHandleToSurface(trussTarget, isPointB: true);
                }, new Color(0.45f, 0.30f, 0.18f));

                if (tc.Style == TrussStyle.NeonLaced)
                {
                    AddSliderRow(card.transform, "Neon Glow", 0.5f, 10.0f, tc.GlowIntensity, "{0:F1}x", (v) =>
                    {
                        tc.GlowIntensity = v;
                        StructuralTrussService.ApplyTrussConfig(trussTarget, tc);
                    });

                    AddColorControl(card.transform, "Lace Tint", tc.AccentColor, (newCol) =>
                    {
                        tc.AccentColor = newCol;
                        StructuralTrussService.ApplyTrussConfig(trussTarget, tc);
                    });
                }

                _activeInspectorCards.Add(card);
            }

            // 13. DISTANT TECH MONOLITH CLUSTER CARD
            if (obj != null && (data.Has<MonolithConfig>() || TechMonolithService.PlacedMonoliths.ContainsKey(obj)))
            {
                var card = CreateModularSection(_inspectorContent, "Monolith", "Distant Tech Monolith Silhouette");
                var mc = data.GetOrCreate<MonolithConfig>();
                if (TechMonolithService.PlacedMonoliths.TryGetValue(obj, out var existingMc))
                    mc = existingMc;

                AddSliderRow(card.transform, "Cluster Scale", 10f, 200f, mc.BaseScale, "{0:F0}m", (v) =>
                {
                    mc.BaseScale = v;
                    TechMonolithService.ApplyMonolithConfig(obj, mc);
                });

                AddSliderRow(card.transform, "Height Mult", 0.8f, 5.0f, mc.HeightMultiplier, "{0:F1}x", (v) =>
                {
                    mc.HeightMultiplier = v;
                    TechMonolithService.ApplyMonolithConfig(obj, mc);
                });

                AddSliderRow(card.transform, "Slab Count", 2f, 7f, mc.SlabCount, "{0:F0}", (v) =>
                {
                    mc.SlabCount = Mathf.RoundToInt(v);
                    TechMonolithService.ApplyMonolithConfig(obj, mc);
                });

                AddToggleRow(card.transform, "Antenna & Strobe Beacon", mc.HasAntennaSpire, (st) =>
                {
                    mc.HasAntennaSpire = st;
                    TechMonolithService.ApplyMonolithConfig(obj, mc);
                });

                CreateButtonPrimitive(card.transform, "Btn_RandomizeSeed", "🎲 Re-roll Monolith Shape", 240f, () =>
                {
                    mc.Seed = UnityEngine.Random.Range(10, 99999);
                    TechMonolithService.ApplyMonolithConfig(obj, mc);
                }, new Color(0.25f, 0.35f, 0.45f));

                AddColorControl(card.transform, "Silhouette Shade", mc.SilhouetteTint, (newCol) =>
                {
                    mc.SilhouetteTint = newCol;
                    TechMonolithService.ApplyMonolithConfig(obj, mc);
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

            _suppressInspectorCallbacks = false;
        }

        private static void OnTransformInputChanged(string val)
        {
            if (_suppressInspectorCallbacks || EditorSessionManager.SelectedObject == null) return;

            GameObject primary = EditorSessionManager.SelectedObject;
            float x = PersistenceUtility.ParseFloat(_posXInput?.text, primary.transform.position.x);
            float y = PersistenceUtility.ParseFloat(_posYInput?.text, primary.transform.position.y);
            float z = PersistenceUtility.ParseFloat(_posZInput?.text, primary.transform.position.z);

            float rx = PersistenceUtility.ParseFloat(_rotXInput?.text, primary.transform.eulerAngles.x);
            float ry = PersistenceUtility.ParseFloat(_rotYInput?.text, primary.transform.eulerAngles.y);
            float rz = PersistenceUtility.ParseFloat(_rotZInput?.text, primary.transform.eulerAngles.z);

            float sx = PersistenceUtility.ParseFloat(_scaleXInput?.text, primary.transform.localScale.x);
            float sy = PersistenceUtility.ParseFloat(_scaleYInput?.text, primary.transform.localScale.y);
            float sz = PersistenceUtility.ParseFloat(_scaleZInput?.text, primary.transform.localScale.z);

            Vector3 newPos = new Vector3(x, y, z);
            Quaternion newRot = Quaternion.Euler(rx, ry, rz);
            Vector3 newScale = new Vector3(Mathf.Max(0.01f, sx), Mathf.Max(0.01f, sy), Mathf.Max(0.01f, sz));

            Vector3 posDelta = newPos - primary.transform.position;
            Quaternion rotDelta = newRot * Quaternion.Inverse(primary.transform.rotation);
            Vector3 scaleDelta = new Vector3(
                Mathf.Abs(primary.transform.localScale.x) > 0.001f ? newScale.x / primary.transform.localScale.x : 1f,
                Mathf.Abs(primary.transform.localScale.y) > 0.001f ? newScale.y / primary.transform.localScale.y : 1f,
                Mathf.Abs(primary.transform.localScale.z) > 0.001f ? newScale.z / primary.transform.localScale.z : 1f
            );

            var targets = GetSelectionTargets(primary);
            bool isMulti = targets.Count > 1;

            for (int t = 0; t < targets.Count; t++)
            {
                GameObject target = targets[t];
                if (target == null) continue;

                if (target == primary || !isMulti)
                {
                    target.transform.position = newPos;
                    target.transform.rotation = newRot;
                    target.transform.localScale = newScale;
                }
                else
                {
                    // Delta transform maintains relative group alignment in multi-selection
                    target.transform.position += posDelta;
                    target.transform.rotation = rotDelta * target.transform.rotation;
                    target.transform.localScale = Vector3.Scale(target.transform.localScale, scaleDelta);
                }

                StudioGizmoController.InvalidateCachedCenter(target);
            }

            EditorSessionManager.UpdateSelectionHighlight();
        }

        // =========================================================================
        // SECTION 10: OVERHAULED SCENE HIERARCHY
        // =========================================================================

        private static void BuildHierarchyPanel()
        {
            _hierarchyPanel = CreatePanelPrimitive(_canvasRoot.transform, "Hierarchy_Panel",
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(130f, -18f), new Vector2(260f, -36f),
                new Color(0.10f, 0.11f, 0.13f, 0.98f));

            // Header (height 32px, y: 0 to -32px)
            GameObject header = CreatePanelPrimitive(_hierarchyPanel.transform, "Hierarchy_Header",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -16f), new Vector2(0f, 32f),
                new Color(0.14f, 0.16f, 0.20f, 0.98f));

            CreateTextPrimitive(header.transform, "SCENE HIERARCHY",
                Vector2.zero, Vector2.one,
                new Vector2(10f, 0f), new Vector2(-10f, 0f),
                11f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);

            // Search row (height 26px, y: -32px to -58px)
            GameObject searchRow = CreatePanelPrimitive(_hierarchyPanel.transform, "Hierarchy_SearchRow",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -44f), new Vector2(0f, 26f),
                new Color(0.08f, 0.09f, 0.11f, 0.95f));

            _hierarchySearchInput = CreateInputFieldPrimitive(searchRow.transform, "SearchInput",
                Vector2.zero, Vector2.one,
                new Vector2(6f, 2f), new Vector2(-6f, -2f),
                "Search scene props...", (val) => RefreshHierarchy());

            // Scroll View starts strictly at -62px and stops at +38px (above bottom bar)
            GameObject scrollObj = CreateScrollViewPrimitive(_hierarchyPanel.transform, "Hierarchy_Scroll",
                Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero,
                out _hierarchyContent);

            if (scrollObj != null)
            {
                RectTransform srt = scrollObj.GetComponent<RectTransform>();
                srt.anchorMin = new Vector2(0f, 0f);
                srt.anchorMax = new Vector2(1f, 1f);
                srt.offsetMin = new Vector2(6f, 40f);   // Clears bottom bar
                srt.offsetMax = new Vector2(-6f, -62f); // Clears search row & header

                _hierarchyScrollRect = scrollObj.GetComponent<ScrollRect>();
                if (_hierarchyScrollRect != null)
                    _hierarchyScrollRect.movementType = ScrollRect.MovementType.Clamped;
            }

            // Bottom Actions Bar (height 36px, y: 0 to 36px)
            GameObject bottomBar = CreatePanelPrimitive(_hierarchyPanel.transform, "Hierarchy_BottomBar",
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 18f), new Vector2(0f, 36f),
                new Color(0.08f, 0.09f, 0.11f, 0.95f));

            Button delBtn = CreateButtonPrimitive(bottomBar.transform, "Btn_DeleteSelected", "Delete Selected [Supr]", 240f, () =>
            {
                EditorSessionManager.DeleteSelectedObjects();
            }, new Color(0.75f, 0.22f, 0.22f, 1f));

            RectTransform dbrt = delBtn.GetComponent<RectTransform>();
            dbrt.anchorMin = Vector2.zero;
            dbrt.anchorMax = Vector2.one;
            dbrt.offsetMin = new Vector2(8f, 4f);
            dbrt.offsetMax = new Vector2(-8f, -4f);
        }

        public static void RefreshHierarchy()
        {
            EnsureSelectableColliders();
            if (_hierarchyContent == null) return;

            _targetToRowMap.Clear();
            _rowToTargetMap.Clear();

            // Destroy existing rows immediately to avoid ghost references
            if (_hierarchyContent != null)
            {
                for (int i = _hierarchyContent.childCount - 1; i >= 0; i--)
                {
                    Transform child = _hierarchyContent.GetChild(i);
                    if (child != null && child.gameObject != null)
                        GameObject.DestroyImmediate(child.gameObject);
                }
            }
            _hierarchyRows.Clear();

            string search = (_hierarchySearchInput != null && !string.IsNullOrEmpty(_hierarchySearchInput.text))
                ? _hierarchySearchInput.text.Trim().ToLowerInvariant() : "";

            List<GameObject> rootNodes = new List<GameObject>();
            Dictionary<GameObject, List<GameObject>> childrenMap = new Dictionary<GameObject, List<GameObject>>();

            if (EditorSessionManager.PlacedObjects == null) return;

            for (int i = 0; i < EditorSessionManager.PlacedObjects.Count; i++)
            {
                GameObject obj = EditorSessionManager.PlacedObjects[i];
                // Rigorous validity check against dead/destroyed Il2Cpp pointers
                if (obj == null || !obj || obj.Equals(null)) continue;

                try
                {
                    if (obj.transform == null || !obj.activeSelf) continue;

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
                catch
                {
                    continue;
                }
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
                    }
                }
            }
            catch { }

            return n;
        }

        private static void RenderHierarchyTreeNode(GameObject node, int depth, Dictionary<GameObject, List<GameObject>> childrenMap, string searchFilter)
        {
            // 1. Guard against dead/destroyed objects during deletion cycles
            if (node == null || !node || node.Equals(null)) return;
            try
            {
                if (node.transform == null || !node.activeSelf) return;
            }
            catch
            {
                return;
            }

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
                new Vector2(leftPadding, 0f), new Vector2(-60f, 0f),
                10f, hasChildren ? FontStyles.Bold : FontStyles.Normal,
                Color.white, TextAlignmentOptions.MidlineLeft);
            if (rowText != null)
            {
                rowText.enableWordWrapping = false;
                rowText.overflowMode = TextOverflowModes.Ellipsis;
            }

            // Visibility Toggle Button
            GameObject eyeBtnObj = new GameObject("Btn_Eye", Il2CppType.Of<RectTransform>());
            eyeBtnObj.transform.SetParent(row.transform, false);
            RectTransform eyert = eyeBtnObj.GetComponent<RectTransform>();
            eyert.anchorMin = new Vector2(1f, 0.5f);
            eyert.anchorMax = new Vector2(1f, 0.5f);
            eyert.pivot = new Vector2(1f, 0.5f);
            eyert.anchoredPosition = new Vector2(-38f, 0f);
            eyert.sizeDelta = new Vector2(17f, 17f);
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

            // Focus Button
            GameObject focusBtnObj = new GameObject("Btn_Focus", Il2CppType.Of<RectTransform>());
            focusBtnObj.transform.SetParent(row.transform, false);
            RectTransform fcrt = focusBtnObj.GetComponent<RectTransform>();
            fcrt.anchorMin = new Vector2(1f, 0.5f);
            fcrt.anchorMax = new Vector2(1f, 0.5f);
            fcrt.pivot = new Vector2(1f, 0.5f);
            fcrt.anchoredPosition = new Vector2(-20f, 0f);
            fcrt.sizeDelta = new Vector2(17f, 17f);
            focusBtnObj.AddComponent<Image>().color = new Color(0.18f, 0.24f, 0.32f, 0.9f);
            Button fcBtn = focusBtnObj.AddComponent<Button>();
            CreateTextPrimitive(focusBtnObj.transform, "F", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
            fcBtn.onClick.AddListener((Action)(() =>
            {
                if (captured == null || !captured || captured.Equals(null)) return;
                EditorSessionManager.SelectObject(captured);
                EditorViewportCamera.FocusOnObject(captured);
            }));

            // Delete Button
            GameObject delBtnObj = new GameObject("Btn_Del", Il2CppType.Of<RectTransform>());
            delBtnObj.transform.SetParent(row.transform, false);
            RectTransform drt = delBtnObj.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(1f, 0.5f);
            drt.anchorMax = new Vector2(1f, 0.5f);
            drt.pivot = new Vector2(1f, 0.5f);
            drt.anchoredPosition = new Vector2(-2f, 0f);
            drt.sizeDelta = new Vector2(17f, 17f);
            delBtnObj.AddComponent<Image>().color = new Color(0.35f, 0.15f, 0.15f, 0.9f);
            Button dBtn = delBtnObj.AddComponent<Button>();
            CreateTextPrimitive(delBtnObj.transform, "X", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 9.5f, FontStyles.Bold, new Color(1f, 0.4f, 0.4f), TextAlignmentOptions.Center);
            dBtn.onClick.AddListener((Action)(() =>
            {
                if (captured == null || !captured || captured.Equals(null)) return;
                EditorSessionManager.DeleteSpecifiedObject(captured);
            }));

            _hierarchyRows.Add(row);

            // Safe child node recursion
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
        // SECTION 11: REUSABLE UI PRIMITIVES & BUILDER HELPERS
        // =========================================================================

        public static GameObject CreatePanelPrimitive(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, Color color)
        {
            GameObject obj = new GameObject(name, Il2CppType.Of<RectTransform>());
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            Image img = obj.AddComponent<Image>();
            img.color = color;
            return obj;
        }

        public static TMP_Text CreateTextPrimitive(Transform parent, string text, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, float fontSize, FontStyles style, Color color, TextAlignmentOptions align)
        {
            if (parent == null) return null;
            GameObject obj = new GameObject("Text", Il2CppType.Of<RectTransform>());
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = anchorMin;
                rt.anchorMax = anchorMax;
                rt.anchoredPosition = pos;
                rt.sizeDelta = size;
            }

            TMP_Text tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text ?? "";
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = align;
            return tmp;
        }

        public static Button CreateButtonPrimitive(Transform parent, string name, string label, float width, Action onClick, Color? bgColor = null)
        {
            GameObject obj = new GameObject(name, Il2CppType.Of<RectTransform>());
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, 24f);

            LayoutElement le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 24f;
            le.minWidth = width > 0 ? Mathf.Min(width, 36f) : 0f;
            le.minHeight = 20f;
            le.flexibleWidth = 0f;

            Image img = obj.AddComponent<Image>();
            img.color = bgColor ?? new Color(0.18f, 0.20f, 0.25f, 1f);

            Button btn = obj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            cb.pressedColor = new Color(0.2f, 0.7f, 1.0f, 1f);
            btn.colors = cb;
            btn.onClick.AddListener((Action)(() => onClick?.Invoke()));

            TMP_Text btnText = CreateTextPrimitive(obj.transform, label, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
            btnText.enableWordWrapping = false;
            btnText.overflowMode = TextOverflowModes.Ellipsis;

            return btn;
        }

        public static TMP_InputField CreateInputFieldPrimitive(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, string placeholder, Action<string> onEndEdit)
        {
            GameObject obj = new GameObject(name, Il2CppType.Of<RectTransform>());
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = anchorMin;
                rt.anchorMax = anchorMax;
                rt.anchoredPosition = pos;
                rt.sizeDelta = size;
            }

            Image img = obj.AddComponent<Image>();
            img.color = new Color(0.08f, 0.09f, 0.11f, 0.95f);

            GameObject textObj = new GameObject("Text", Il2CppType.Of<RectTransform>());
            textObj.transform.SetParent(obj.transform, false);
            RectTransform trt = textObj.GetComponent<RectTransform>();
            if (trt != null)
            {
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(6f, 2f);
                trt.offsetMax = new Vector2(-6f, -2f);
            }

            TMP_Text inTmp = textObj.AddComponent<TextMeshProUGUI>();
            inTmp.fontSize = 10f;
            inTmp.color = Color.white;
            inTmp.alignment = TextAlignmentOptions.MidlineLeft;

            TMP_InputField inputField = obj.AddComponent<TMP_InputField>();
            inputField.textViewport = trt;
            inputField.textComponent = inTmp;

            if (!string.IsNullOrEmpty(placeholder))
            {
                GameObject phObj = new GameObject("Placeholder", Il2CppType.Of<RectTransform>());
                phObj.transform.SetParent(obj.transform, false);
                RectTransform phrt = phObj.GetComponent<RectTransform>();
                if (phrt != null)
                {
                    phrt.anchorMin = Vector2.zero;
                    phrt.anchorMax = Vector2.one;
                    phrt.offsetMin = new Vector2(6f, 2f);
                    phrt.offsetMax = new Vector2(-6f, -2f);
                }

                TMP_Text phTmp = phObj.AddComponent<TextMeshProUGUI>();
                phTmp.text = placeholder;
                phTmp.fontSize = 9.5f;
                phTmp.color = new Color(0.45f, 0.50f, 0.60f, 0.8f);
                phTmp.fontStyle = FontStyles.Italic;
                phTmp.alignment = TextAlignmentOptions.MidlineLeft;
                inputField.placeholder = phTmp;
            }

            // Guard against uninstantiated Il2Cpp UnityEvent before Awake runs
            if (onEndEdit != null)
            {
                try
                {
                    if (inputField.onEndEdit == null)
                        inputField.m_OnEndEdit = new TMP_InputField.SubmitEvent();

                    inputField.onEndEdit.AddListener((Action<string>)((val) => onEndEdit.Invoke(val)));
                }
                catch { }
            }

            return inputField;
        }

        private static GameObject CreateScrollViewPrimitive(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, out RectTransform content)
        {
            GameObject scrollObj = new GameObject(name, Il2CppType.Of<RectTransform>());
            scrollObj.transform.SetParent(parent, false);

            RectTransform rt = scrollObj.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            ScrollRect sr = scrollObj.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 22f;
            sr.movementType = ScrollRect.MovementType.Clamped;

            GameObject viewport = new GameObject("Viewport", Il2CppType.Of<RectTransform>());
            viewport.transform.SetParent(scrollObj.transform, false);
            RectTransform vrt = viewport.GetComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.sizeDelta = Vector2.zero;
            viewport.AddComponent<RectMask2D>();

            GameObject cObj = new GameObject("Content", Il2CppType.Of<RectTransform>());
            cObj.transform.SetParent(viewport.transform, false);
            content = cObj.GetComponent<RectTransform>();
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

        private static GameObject CreateModularSection(Transform parent, string name, string title)
        {
            GameObject card = new GameObject("Card_" + name, Il2CppType.Of<RectTransform>());
            card.transform.SetParent(parent, false);

            Image bg = card.AddComponent<Image>();
            bg.color = new Color(0.13f, 0.14f, 0.18f, 0.95f);

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

            GameObject header = new GameObject("Header", Il2CppType.Of<RectTransform>());
            header.transform.SetParent(card.transform, false);
            LayoutElement hle = header.AddComponent<LayoutElement>();
            hle.preferredHeight = 20f;
            hle.minHeight = 20f;
            header.AddComponent<Image>().color = new Color(0.18f, 0.20f, 0.26f, 0.95f);

            CreateTextPrimitive(header.transform, title, Vector2.zero, Vector2.one, new Vector2(6f, 0f), new Vector2(-6f, 0f), 10.5f, FontStyles.Bold, new Color(0.25f, 0.85f, 1f), TextAlignmentOptions.MidlineLeft);

            return card;
        }

        private static GameObject AddSliderRow(Transform parent, string label, float min, float max, float def, string format, Action<float> onChange)
        {
            GameObject row = CreateRowContainerPrimitive(parent, "Row_Slider_" + label, 22f);

            CreateTextPrimitive(row.transform, label, new Vector2(0f, 0f), new Vector2(0.35f, 1f), new Vector2(4f, 0f), Vector2.zero, 9.5f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);
            TMP_Text valText = CreateTextPrimitive(row.transform, string.Format(CultureInfo.InvariantCulture, format, def), new Vector2(0.78f, 0f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-4f, 0f), 9.5f, FontStyles.Bold, new Color(0.2f, 0.85f, 1f), TextAlignmentOptions.MidlineRight);

            GameObject sliderObj = new GameObject("Slider", Il2CppType.Of<RectTransform>());
            sliderObj.transform.SetParent(row.transform, false);

            RectTransform srt = sliderObj.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.36f, 0.5f);
            srt.anchorMax = new Vector2(0.76f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = Vector2.zero;
            srt.sizeDelta = new Vector2(0f, 14f);

            Slider slider = sliderObj.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = def;

            GameObject track = new GameObject("Track", Il2CppType.Of<RectTransform>());
            track.transform.SetParent(sliderObj.transform, false);
            RectTransform trt = track.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 0.5f);
            trt.anchorMax = new Vector2(1f, 0.5f);
            trt.sizeDelta = new Vector2(0f, 4f);
            track.AddComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 1f);

            GameObject fillArea = new GameObject("FillArea", Il2CppType.Of<RectTransform>());
            fillArea.transform.SetParent(sliderObj.transform, false);
            RectTransform fart = fillArea.GetComponent<RectTransform>();
            fart.anchorMin = new Vector2(0f, 0.5f);
            fart.anchorMax = new Vector2(1f, 0.5f);
            fart.sizeDelta = new Vector2(-6f, 4f);

            GameObject fill = new GameObject("Fill", Il2CppType.Of<RectTransform>());
            fill.transform.SetParent(fillArea.transform, false);
            RectTransform frt = fill.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(0f, 1f);
            frt.sizeDelta = Vector2.zero;
            fill.AddComponent<Image>().color = new Color(0.18f, 0.65f, 0.95f, 1f);

            GameObject handleArea = new GameObject("HandleArea", Il2CppType.Of<RectTransform>());
            handleArea.transform.SetParent(sliderObj.transform, false);
            RectTransform hart = handleArea.GetComponent<RectTransform>();
            hart.anchorMin = Vector2.zero;
            hart.anchorMax = Vector2.one;
            hart.sizeDelta = Vector2.zero;

            GameObject handle = new GameObject("Handle", Il2CppType.Of<RectTransform>());
            handle.transform.SetParent(handleArea.transform, false);
            RectTransform hrt = handle.GetComponent<RectTransform>();
            hrt.sizeDelta = new Vector2(6f, 14f);
            Image handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;

            slider.fillRect = frt;
            slider.handleRect = hrt;
            slider.targetGraphic = handleImg;

            slider.onValueChanged.AddListener((Action<float>)((v) =>
            {
                if (valText != null) valText.text = string.Format(CultureInfo.InvariantCulture, format, v);
                onChange?.Invoke(v);
            }));

            return row;
        }

        private static void AddColorControl(Transform parent, string label, Color initialColor, Action<Color> onColorChanged)
        {
            GameObject swatchRow = CreateRowContainerPrimitive(parent, "Row_Swatch_" + label, 20f);
            CreateTextPrimitive(swatchRow.transform, label, Vector2.zero, new Vector2(0.65f, 1f), new Vector2(4f, 0f), Vector2.zero, 9.5f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);

            GameObject swatchObj = new GameObject("Swatch", Il2CppType.Of<RectTransform>());
            swatchObj.transform.SetParent(swatchRow.transform, false);
            RectTransform swrt = swatchObj.GetComponent<RectTransform>();
            swrt.anchorMin = new Vector2(1f, 0.5f);
            swrt.anchorMax = new Vector2(1f, 0.5f);
            swrt.pivot = new Vector2(1f, 0.5f);
            swrt.anchoredPosition = new Vector2(-4f, 0f);
            swrt.sizeDelta = new Vector2(50f, 16f);

            Image previewSwatch = swatchObj.AddComponent<Image>();
            previewSwatch.color = initialColor;

            Color col = initialColor;

            AddSliderRow(parent, "R", 0f, 255f, col.r * 255f, "{0:F0}", (v) =>
            {
                col.r = v / 255f;
                previewSwatch.color = col;
                onColorChanged?.Invoke(col);
            });

            AddSliderRow(parent, "G", 0f, 255f, col.g * 255f, "{0:F0}", (v) =>
            {
                col.g = v / 255f;
                previewSwatch.color = col;
                onColorChanged?.Invoke(col);
            });

            AddSliderRow(parent, "B", 0f, 255f, col.b * 255f, "{0:F0}", (v) =>
            {
                col.b = v / 255f;
                previewSwatch.color = col;
                onColorChanged?.Invoke(col);
            });
        }
        private static void AddToggleRow(Transform parent, string label, bool initial, Action<bool> onToggle)
        {
            GameObject row = CreateRowContainerPrimitive(parent, "Row_Toggle_" + label, 22f);
            CreateTextPrimitive(row.transform, label, Vector2.zero, new Vector2(0.75f, 1f), new Vector2(4f, 0f), Vector2.zero, 9.5f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);

            bool state = initial;
            Button btn = CreateButtonPrimitive(row.transform, "ToggleBtn", state ? "ON" : "OFF", 60f, null, state ? new Color(0.2f, 0.65f, 0.35f) : new Color(0.25f, 0.28f, 0.35f));
            RectTransform brt = btn.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(1f, 0.5f);
            brt.anchorMax = new Vector2(1f, 0.5f);
            brt.pivot = new Vector2(1f, 0.5f);
            brt.anchoredPosition = new Vector2(-4f, 0f);
            brt.sizeDelta = new Vector2(60f, 20f);

            TMP_Text bText = btn.GetComponentInChildren<TMP_Text>();
            btn.onClick.AddListener((Action)(() =>
            {
                state = !state;
                bText.text = state ? "ON" : "OFF";
                btn.image.color = state ? new Color(0.2f, 0.65f, 0.35f) : new Color(0.25f, 0.28f, 0.35f);
                onToggle?.Invoke(state);
            }));
        }

        private static void CreateVector3Row(Transform parent, string label, out TMP_InputField xIn, out TMP_InputField yIn, out TMP_InputField zIn, Action<string> onChange)
        {
            GameObject row = CreateRowContainerPrimitive(parent, "Row_" + label, 22f);
            CreateTextPrimitive(row.transform, label, new Vector2(0f, 0f), new Vector2(0.24f, 1f), Vector2.zero, Vector2.zero, 10f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);

            xIn = CreateInputFieldPrimitive(row.transform, "X", new Vector2(0.25f, 0f), new Vector2(0.49f, 1f), Vector2.zero, Vector2.zero, "X", onChange);
            yIn = CreateInputFieldPrimitive(row.transform, "Y", new Vector2(0.50f, 0f), new Vector2(0.74f, 1f), Vector2.zero, Vector2.zero, "Y", onChange);
            zIn = CreateInputFieldPrimitive(row.transform, "Z", new Vector2(0.75f, 0f), new Vector2(0.99f, 1f), Vector2.zero, Vector2.zero, "Z", onChange);
        }

        private static GameObject CreateRowContainerPrimitive(Transform parent, string name, float height)
        {
            GameObject row = new GameObject(name, Il2CppType.Of<RectTransform>());
            row.transform.SetParent(parent, false);

            RectTransform rt = row.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(0f, height);

            LayoutElement le = row.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            le.flexibleWidth = 1f;

            return row;
        }

        private static HorizontalLayoutGroup SetupRowHorizontalLayoutPrimitive(GameObject row, float spacing = 6f)
        {
            HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>() ?? row.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(2, 2, 2, 2);
            hlg.spacing = spacing;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            return hlg;
        }

        // =========================================================================
        // SECTION 12: ISOMETRIC ASSET THUMBNAIL RENDERER
        // =========================================================================

        public static class AssetThumbnailRenderer
        {
            private static Camera _previewCam = null;
            private static GameObject _studioStage = null;
            private static Light _studioKeyLight = null;
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

            private static Light _studioFillLight = null;

            private static void EnsureStudio()
            {
                if (_studioStage != null) return;

                _studioStage = new GameObject("Asset_Thumbnail_Studio_Stage");
                _studioStage.transform.position = StagePosition;
                _studioStage.layer = 2;

                // Camera setup
                GameObject camObj = new GameObject("Studio_Cam");
                camObj.transform.SetParent(_studioStage.transform, false);
                _previewCam = camObj.AddComponent<Camera>();
                _previewCam.clearFlags = CameraClearFlags.Color;
                _previewCam.backgroundColor = new Color(0.10f, 0.12f, 0.16f, 1f); // Neutral studio dark gray
                _previewCam.cullingMask = 1 << 2;
                _previewCam.fieldOfView = 22f;
                _previewCam.nearClipPlane = 0.1f;
                _previewCam.farClipPlane = 500f;

                _previewRt = new RenderTexture(128, 128, 16, RenderTextureFormat.ARGB32);
                _previewCam.targetTexture = _previewRt;

                // 1. Key Light: Lowered from 15,000f to 2,400f to eliminate clipped white highlights
                GameObject keyObj = new GameObject("Studio_KeyLight");
                keyObj.transform.SetParent(_studioStage.transform, false);
                _studioKeyLight = keyObj.AddComponent<Light>();
                _studioKeyLight.type = LightType.Directional;
                _studioKeyLight.color = new Color(1f, 0.97f, 0.92f);
                _studioKeyLight.intensity = 2400f;
                _studioKeyLight.cullingMask = 1 << 2;
                keyObj.transform.rotation = Quaternion.Euler(35f, -40f, 0f);

                // 2. Fill Light: Soft cool light to illuminate dark faces so dark props stay clearly visible
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

    // =========================================================================
    // SECTION 13: NATIVE LOGS MENU HIJACKER & THUMBNAILS (FIXED & ALIGNED)
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

        public static void CreateSceneSelectorRow(Transform parent, TMP_Text sampleTmp, float height = 30f)
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

            // Fixed-width Label
            GameObject labelObj = new GameObject("Label", Il2CppType.Of<RectTransform>());
            labelObj.transform.SetParent(row.transform, false);

            LayoutElement labelLe = labelObj.AddComponent<LayoutElement>();
            labelLe.preferredWidth = 140f;
            labelLe.minWidth = 140f;
            labelLe.flexibleWidth = 0f;

            TMP_Text labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
            if (sampleTmp != null) { labelTmp.font = sampleTmp.font; labelTmp.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
            labelTmp.fontSize = 15f;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.color = new Color(0.2f, 0.82f, 1f, 1f);
            labelTmp.text = "BASE SCENE";
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;

            // Selector Container
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

            // Prev Button
            GameObject prevBtn = new GameObject("Btn_PrevScene", Il2CppType.Of<RectTransform>());
            prevBtn.transform.SetParent(container.transform, false);
            LayoutElement ple = prevBtn.AddComponent<LayoutElement>();
            ple.preferredWidth = 34f;
            ple.minWidth = 34f;
            prevBtn.AddComponent<Image>().color = new Color(0.12f, 0.18f, 0.30f, 0.95f);
            Button pb = prevBtn.AddComponent<Button>();
            pb.onClick.AddListener((Action)(() => CycleStagingScene(-1)));
            TMP_Text pt = StudioUIManager.CreateTextPrimitive(prevBtn.transform, "<", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 18f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
            if (sampleTmp != null) { pt.font = sampleTmp.font; pt.fontSharedMaterial = sampleTmp.fontSharedMaterial; }

            // Display Label
            GameObject displayObj = new GameObject("SceneDisplay", Il2CppType.Of<RectTransform>());
            displayObj.transform.SetParent(container.transform, false);
            LayoutElement dle = displayObj.AddComponent<LayoutElement>();
            dle.flexibleWidth = 1f;
            displayObj.AddComponent<Image>().color = new Color(0.06f, 0.10f, 0.18f, 0.92f);

            _sceneLabelText = StudioUIManager.CreateTextPrimitive(displayObj.transform, MapBrowserService.SelectedStagingScene, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 15f, FontStyles.Bold, new Color(0.2f, 0.95f, 0.4f), TextAlignmentOptions.Center);
            if (sampleTmp != null) { _sceneLabelText.font = sampleTmp.font; _sceneLabelText.fontSharedMaterial = sampleTmp.fontSharedMaterial; }

            // Next Button
            GameObject nextBtn = new GameObject("Btn_NextScene", Il2CppType.Of<RectTransform>());
            nextBtn.transform.SetParent(container.transform, false);
            LayoutElement nle = nextBtn.AddComponent<LayoutElement>();
            nle.preferredWidth = 34f;
            nle.minWidth = 34f;
            nextBtn.AddComponent<Image>().color = new Color(0.12f, 0.18f, 0.30f, 0.95f);
            Button nb = nextBtn.AddComponent<Button>();
            nb.onClick.AddListener((Action)(() => CycleStagingScene(1)));
            TMP_Text nt = StudioUIManager.CreateTextPrimitive(nextBtn.transform, ">", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 18f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
            if (sampleTmp != null) { nt.font = sampleTmp.font; nt.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
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

            if (_nativePlayButton != null && _nativePlayButton.activeInHierarchy) SetButtonText(_nativePlayButton, "Play", 34f);
            if (_nativeCreateButton != null && _nativeCreateButton.activeInHierarchy) SetButtonText(_nativeCreateButton, "+ New", 34f);
            if (_nativeDeleteButton != null && _nativeDeleteButton.activeInHierarchy) SetButtonText(_nativeDeleteButton, "Delete", 34f);
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
                        if (child.name.StartsWith("CustomMap_")) GameObject.DestroyImmediate(child.gameObject);
                        else child.gameObject.SetActive(false);
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
                    MapBrowserService.EnsureDirectories();
                    fileList.Add(Path.Combine(MapBrowserService.MyLevelsDir, "Default_Level.txt"));
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

                Button btn = itemObj.GetComponent<Button>() ?? itemObj.AddComponent<Button>();
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

            // 1. DO NOT TOUCH _logBigPicture's position or scale — keep it in its original native position
            menu._logBigPicture.gameObject.SetActive(true);
            menu._logBigPicture.color = Color.white;

            // 2. OVERLAY THE METADATA PANEL DIRECTLY OVER THE NATIVE PICTURE FRAME
            if (_nativeMetadataRoot == null || _nativeMetadataRoot.Equals(null))
            {
                _nativeMetadataRoot = new GameObject("Native_Metadata_Root", Il2CppType.Of<RectTransform>());
                _nativeMetadataRoot.transform.SetParent(menu._logBigPicture.transform, false);

                RectTransform rootRt = _nativeMetadataRoot.GetComponent<RectTransform>();
                // Stretch to fill the native image bounds 1:1
                rootRt.anchorMin = Vector2.zero;
                rootRt.anchorMax = Vector2.one;
                rootRt.pivot = new Vector2(0.5f, 0.5f);
                rootRt.offsetMin = Vector2.zero;
                rootRt.offsetMax = Vector2.zero;

                // Subtle dark scrim so the level screenshot shows through while keeping text readable
                Image bgImg = _nativeMetadataRoot.AddComponent<Image>();
                bgImg.color = new Color(0.02f, 0.04f, 0.07f, 0.40f);

                VerticalLayoutGroup vlg = _nativeMetadataRoot.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(14, 14, 8, 16);
                vlg.spacing = 6f;
                vlg.childControlWidth = true;
                vlg.childControlHeight = false;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;

                TMP_Text sampleText = menu._shortDesc != null ? menu._shortDesc : menu.GetComponentInChildren<TMP_Text>(true);

                CreateNativeInputRow(_nativeMetadataRoot.transform, sampleText, "LEVEL TITLE", 30f, 14f, out _titleInput);
                CreateNativeInputRow(_nativeMetadataRoot.transform, sampleText, "AUTHOR", 30f, 14f, out _authorInput);
                CreateDifficultyRow(_nativeMetadataRoot.transform, sampleText, 32f);
                CreateSceneSelectorRow(_nativeMetadataRoot.transform, sampleText, 30f);
                CreateNativeInputRow(_nativeMetadataRoot.transform, sampleText, "DESCRIPTION", 46f, 13f, out _descInput, true);
                CreateLevelStatsHUD(_nativeMetadataRoot.transform, sampleText, 46f);
                CreateNativeSaveButton(_nativeMetadataRoot.transform, sampleText, 34f);
            }

            _nativeMetadataRoot.transform.SetAsLastSibling();

            // Keep the native yellow expand icon in the bottom-right corner visible on top
            for (int i = 0; i < menu._logBigPicture.transform.childCount; i++)
            {
                Transform child = menu._logBigPicture.transform.GetChild(i);
                if (child != null && child.gameObject != _nativeMetadataRoot && child.name.ToLower().Contains("expand"))
                {
                    child.SetAsLastSibling();
                }
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

            // Fixed-width column ensures all labels align perfectly
            GameObject labelObj = new GameObject("Label", Il2CppType.Of<RectTransform>());
            labelObj.transform.SetParent(row.transform, false);

            LayoutElement labelLe = labelObj.AddComponent<LayoutElement>();
            labelLe.preferredWidth = 140f;
            labelLe.minWidth = 140f;
            labelLe.flexibleWidth = 0f;

            TMP_Text labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
            if (sampleTmp != null) { labelTmp.font = sampleTmp.font; labelTmp.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
            labelTmp.fontSize = 15f;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.color = new Color(0.2f, 0.85f, 1f, 1f);
            labelTmp.text = labelName;
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;

            // Translucent glass input background
            GameObject inputObj = new GameObject("InputField", Il2CppType.Of<RectTransform>());
            inputObj.transform.SetParent(row.transform, false);

            LayoutElement inLe = inputObj.AddComponent<LayoutElement>();
            inLe.flexibleWidth = 1f;

            inputObj.AddComponent<Image>().color = new Color(0.04f, 0.08f, 0.15f, 0.55f);

            GameObject textObj = new GameObject("Text", Il2CppType.Of<RectTransform>());
            textObj.transform.SetParent(inputObj.transform, false);
            RectTransform textRt = textObj.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(8f, 2f);
            textRt.offsetMax = new Vector2(-8f, -2f);

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

        private static void CreateDifficultyRow(Transform parent, TMP_Text sampleTmp, float height = 34f)
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
            labelLe.preferredWidth = 140f;
            labelLe.minWidth = 140f;
            labelLe.flexibleWidth = 0f;

            TMP_Text labelTmp = labelObj.AddComponent<TextMeshProUGUI>();
            if (sampleTmp != null) { labelTmp.font = sampleTmp.font; labelTmp.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
            labelTmp.fontSize = 15f;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.color = new Color(0.2f, 0.85f, 1f, 1f);
            labelTmp.text = "DIFFICULTY";
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;

            GameObject btnContainer = new GameObject("BtnContainer", Il2CppType.Of<RectTransform>());
            btnContainer.transform.SetParent(row.transform, false);

            LayoutElement bcLe = btnContainer.AddComponent<LayoutElement>();
            bcLe.flexibleWidth = 1f;

            HorizontalLayoutGroup bchlg = btnContainer.AddComponent<HorizontalLayoutGroup>();
            bchlg.spacing = 6f;
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

                btn.AddComponent<Image>().color = new Color(0.04f, 0.08f, 0.14f, 0.50f);
                Button bComp = btn.AddComponent<Button>();
                bComp.onClick.AddListener((Action)(() =>
                {
                    _selectedDifficultyIndex = captureIdx;
                    HighlightSelectedDifficulty();
                }));

                TMP_Text bTmp = StudioUIManager.CreateTextPrimitive(btn.transform, DifficultyNames[i], Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 13f, FontStyles.Bold, DifficultyColors[i], TextAlignmentOptions.Center);
                if (sampleTmp != null) { bTmp.font = sampleTmp.font; bTmp.fontSharedMaterial = sampleTmp.fontSharedMaterial; }

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
                    // Translucent colored glow when selected
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

        private static void CreateLevelStatsHUD(Transform parent, TMP_Text sampleTmp, float height = 48f)
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
            if (sampleTmp != null) { _statsLabelLeft.font = sampleTmp.font; _statsLabelLeft.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
            _statsLabelLeft.fontSize = 12f;
            _statsLabelLeft.color = new Color(0.6f, 0.85f, 1f, 0.95f);
            _statsLabelLeft.alignment = TextAlignmentOptions.MidlineLeft;

            GameObject rightObj = new GameObject("StatsRight", Il2CppType.Of<RectTransform>());
            rightObj.transform.SetParent(statsBox.transform, false);
            _statsLabelRight = rightObj.AddComponent<TextMeshProUGUI>();
            if (sampleTmp != null) { _statsLabelRight.font = sampleTmp.font; _statsLabelRight.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
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
            _statsLabelRight.text = $"- MOVING PATHS: <b><color=#E040FB>{paths}</color></b>\n- TURRET ENEMIES: <b><color=#FF4081>{turrets}</color></b>\n- LAST SAVED: <color=#B0BEC5>{mod:dd/MM/yyyy HH:mm}</color>";
        }
        private static void CreateNativeSaveButton(Transform parent, TMP_Text sampleTmp, float height = 36f)
        {
            GameObject saveBtn = new GameObject("Btn_SaveMetadata", Il2CppType.Of<RectTransform>());
            saveBtn.transform.SetParent(parent, false);

            LayoutElement le = saveBtn.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            le.flexibleWidth = 1f;

            // Semi-transparent blue glass button
            saveBtn.AddComponent<Image>().color = new Color(0.12f, 0.55f, 0.88f, 0.75f);
            Button b = saveBtn.AddComponent<Button>();
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener((Action)(() => SaveCurrentMetadata(MapBrowserService.SelectedMapPath)));

            _saveBtnText = StudioUIManager.CreateTextPrimitive(saveBtn.transform, "SAVE DETAILS", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 15f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
            if (sampleTmp != null) { _saveBtnText.font = sampleTmp.font; _saveBtnText.fontSharedMaterial = sampleTmp.fontSharedMaterial; }
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

            Button playBtn = _nativePlayButton.GetComponent<Button>() ?? _nativePlayButton.AddComponent<Button>();
            playBtn.onClick = new Button.ButtonClickedEvent();
            playBtn.onClick.AddListener((Action)(() =>
            {
                if (!string.IsNullOrEmpty(MapBrowserService.SelectedMapPath))
                    MapBrowserService.LaunchSelectedMap();
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

            Button createBtn = _nativeCreateButton.GetComponent<Button>() ?? _nativeCreateButton.AddComponent<Button>();
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

            Vector3 spawn = EditorSessionManager.LevelSpawnPosition;
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

            // Inside DeadCoreLevelEditorMod.OnUpdate():
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

            // Run UI ticks, scrolling & click-outside detectors
            StudioUIManager.UpdateUI();

            // Configurable Keyboard Shortcuts Routing
            if (GUIUtility.keyboardControl == 0)
            {
                if (EditorConfigService.IsCustomShortcutTriggered("TogglePlaytest", "F1"))
                    EditorSessionManager.ToggleEditMode();

                if (EditorSessionManager.IsEditModeActive)
                {
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
            NativeLogsMenuHijacker.TransformLogsMenu(menu, ActiveTab);
        }
    }
}