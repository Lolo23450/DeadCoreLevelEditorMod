using System;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
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
    // CONFIGURATION DATA & SERVICE
    // =========================================================================

    public class EditorConfigData
    {
        // Viewport & Camera
        public float MinAssetSize = 1.4f;
        public float FlycamSpeed = 24.0f;
        public float FastCamMultiplier = 3.5f;
        public float SlowCamMultiplier = 0.25f;
        public float MouseSensitivity = 2.5f;
        public bool InvertLookY = false;
        public bool RequireRmbForFlight = true;
        public bool SmoothFlycam = true;
        public float FlycamSmoothing = 12.0f;
        public float EditorFov = 75.0f;

        // Gizmo & Snapping Matrix
        public float GizmoScaleMultiplier = 1.0f;
        public float DefaultGridSnap = 1.0f;
        public bool SnappingProxiesVisible = false;

        // UI & Display
        public bool ShowSizeBadges = true;
        public float NotificationDuration = 2.5f;

        // Auto-Save & Level Safety
        public int AutoSaveIntervalMinutes = 5; // 0 = Disabled
        public bool AutoSaveOnPlaytest = true;

        // Dictionaries
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
            Keybindings["SelectAll"] = "Ctrl+A";
        }

        public void ResetAll()
        {
            MinAssetSize = 1.4f;
            FlycamSpeed = 24.0f;
            FastCamMultiplier = 3.5f;
            SlowCamMultiplier = 0.25f;
            MouseSensitivity = 2.5f;
            InvertLookY = false;
            RequireRmbForFlight = true;
            SmoothFlycam = true;
            FlycamSmoothing = 12.0f;
            EditorFov = 75.0f;

            GizmoScaleMultiplier = 1.0f;
            DefaultGridSnap = 1.0f;
            SnappingProxiesVisible = false;

            ShowSizeBadges = true;
            NotificationDuration = 2.5f;

            AutoSaveIntervalMinutes = 5;
            AutoSaveOnPlaytest = true;

            SetDefaultKeybindings();
        }
    }

    public static class EditorConfigService
    {
        public static EditorConfigData Config = new EditorConfigData();
        public static string ConfigPath => Path.Combine(Directory.GetCurrentDirectory(), "UserData", "EditorConfig.json");

        private static readonly HashSet<KeyCode> CameraFlyKeys = new HashSet<KeyCode>
        {
            KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Q, KeyCode.E, KeyCode.Space
        };

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

        public static void ResetToDefaults()
        {
            Config.ResetAll();
            SaveConfig();
        }

        public static bool IsCameraKeyConflict(string keyCombo, out string conflictReason)
        {
            conflictReason = null;
            if (string.IsNullOrWhiteSpace(keyCombo) || keyCombo == "None") return false;

            string[] tokens = keyCombo.Split('+');
            bool hasModifier = false;
            string rawKey = "";

            for (int i = 0; i < tokens.Length; i++)
            {
                string t = tokens[i].Trim();
                if (t.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    hasModifier = true;
                }
                else
                {
                    rawKey = t;
                }
            }

            if (!hasModifier && Enum.TryParse<KeyCode>(rawKey, true, out KeyCode kc))
            {
                if (CameraFlyKeys.Contains(kc))
                {
                    conflictReason = $"'{kc}' is reserved for Viewport Camera flight. Add a modifier (e.g. Ctrl+{kc} or Alt+{kc}).";
                    return true;
                }
            }

            return false;
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

        public static bool IsCustomShortcutTriggered(string actionName, string defaultCombo)
        {
            if (!Config.Keybindings.TryGetValue(actionName, out string shortcut) || string.IsNullOrWhiteSpace(shortcut) || shortcut == "None")
                return false;

            if (shortcut.Equals(defaultCombo, StringComparison.OrdinalIgnoreCase))
                return false;

            return IsShortcutTriggered(actionName);
        }
    }

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
            sb.AppendLine($"  \"RequireRmbForFlight\": {(data.RequireRmbForFlight ? "true" : "false")},");
            sb.AppendLine($"  \"SmoothFlycam\": {(data.SmoothFlycam ? "true" : "false")},");
            sb.AppendLine($"  \"FlycamSmoothing\": {data.FlycamSmoothing.ToString("F1", Inv)},");
            sb.AppendLine($"  \"EditorFov\": {data.EditorFov.ToString("F1", Inv)},");
            sb.AppendLine($"  \"GizmoScaleMultiplier\": {data.GizmoScaleMultiplier.ToString("F2", Inv)},");
            sb.AppendLine($"  \"DefaultGridSnap\": {data.DefaultGridSnap.ToString("F2", Inv)},");
            sb.AppendLine($"  \"ShowSizeBadges\": {(data.ShowSizeBadges ? "true" : "false")},");
            sb.AppendLine($"  \"SnappingProxiesVisible\": {(data.SnappingProxiesVisible ? "true" : "false")},");
            sb.AppendLine($"  \"NotificationDuration\": {data.NotificationDuration.ToString("F2", Inv)},");
            sb.AppendLine($"  \"AutoSaveIntervalMinutes\": {data.AutoSaveIntervalMinutes},");
            sb.AppendLine($"  \"AutoSaveOnPlaytest\": {(data.AutoSaveOnPlaytest ? "true" : "false")},");

            sb.AppendLine("  \"Keybindings\": {");
            int kbCount = 0;
            foreach (var kvp in data.Keybindings)
            {
                kbCount++;
                string comma = (kbCount < data.Keybindings.Count) ? "," : "";
                sb.AppendLine($"    \"{Escape(kvp.Key)}\": \"{Escape(kvp.Value)}\"{comma}");
            }
            sb.AppendLine("  },");

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
            data.RequireRmbForFlight = ExtractBool(json, "RequireRmbForFlight", true);
            data.SmoothFlycam = ExtractBool(json, "SmoothFlycam", true);
            data.FlycamSmoothing = ExtractFloat(json, "FlycamSmoothing", 12f);
            data.EditorFov = ExtractFloat(json, "EditorFov", 75f);
            data.GizmoScaleMultiplier = ExtractFloat(json, "GizmoScaleMultiplier", 1.0f);
            data.DefaultGridSnap = ExtractFloat(json, "DefaultGridSnap", 1.0f);
            data.ShowSizeBadges = ExtractBool(json, "ShowSizeBadges", true);
            data.SnappingProxiesVisible = ExtractBool(json, "SnappingProxiesVisible", false);
            data.NotificationDuration = ExtractFloat(json, "NotificationDuration", 2.5f);
            data.AutoSaveIntervalMinutes = ExtractInt(json, "AutoSaveIntervalMinutes", 5);
            data.AutoSaveOnPlaytest = ExtractBool(json, "AutoSaveOnPlaytest", true);

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

        private static int ExtractInt(string json, string key, int fallback)
        {
            string marker = $"\"{key}\":";
            int idx = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx == -1) return fallback;

            int start = idx + marker.Length;
            int end = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, start);
            if (end == -1) end = json.Length;

            string raw = json.Substring(start, end - start).Trim();
            if (int.TryParse(raw, NumberStyles.Integer, Inv, out int res)) return res;
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
    // FLOATING WINDOW BASE CONTAINER (WITH SCROLL & RAYCAST SHIELD)
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

            // Raycast-blocking background image: absorbs clicks and scroll gestures
            Image bg = win.WindowRoot.AddComponent<Image>();
            bg.color = new Color(0.10f, 0.12f, 0.16f, 0.98f);
            bg.raycastTarget = true;

            // Header Bar
            GameObject titleBar = new GameObject("TitleBar", Il2CppType.Of<RectTransform>());
            titleBar.transform.SetParent(win.WindowRoot.transform, false);

            RectTransform tbrt = titleBar.GetComponent<RectTransform>();
            tbrt.anchorMin = new Vector2(0f, 1f);
            tbrt.anchorMax = new Vector2(1f, 1f);
            tbrt.pivot = new Vector2(0.5f, 1f);
            tbrt.sizeDelta = new Vector2(0f, 32f);
            tbrt.anchoredPosition = Vector2.zero;

            Image tbBg = titleBar.AddComponent<Image>();
            tbBg.color = new Color(0.15f, 0.18f, 0.25f, 0.98f);
            tbBg.raycastTarget = true;

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

            Button closeBtn = StudioUIManager.CreateButtonPrimitive(titleBar.transform, "Btn_Close", "X", 24f, () => win.Hide(), new Color(0.75f, 0.22f, 0.22f, 1f));
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
            win.ContentRt.offsetMax = new Vector2(-8f, -36f);

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
    // STUDIO UI MANAGER - CORE SHELL (PARTIAL)
    // =========================================================================

    public static partial class StudioUIManager
    {
        internal static GameObject _canvasRoot = null;
        internal static Canvas _canvas = null;

        // Top Toolbar
        internal static GameObject _toolbarPanel = null;
        internal static GameObject _activeDropdownMenu = null;
        internal static RectTransform _activeDropdownAnchor = null;
        internal static Button _modeTogglePillBtn = null;
        internal static TMP_Text _modeTogglePillText = null;
        internal static TMP_Text _surfaceAlignBtnText = null;
        internal static TMP_Text _gridSnapBtnText = null;

        // Toast Notification System
        private static GameObject _notificationBanner = null;
        private static TMP_Text _notificationText = null;
        private static float _notificationTimer = 0f;

        // Auto-Save System
        private static float _autoSaveTimer = 0f;

        // Snap Settings Matrix
        public static float SnapTranslate = 1.0f;
        public static float SnapRotate = 15f;
        public static float SnapScale = 0.1f;

        // Category Dock
        internal static GameObject _floatingCategoryDock = null;
        internal static readonly Dictionary<string, Button> _categoryDockButtons = new Dictionary<string, Button>();
        internal static TMP_Text _assetCountBadgeText = null;

        internal static List<GameObject> GetSelectionTargets(GameObject primary)
        {
            if (EditorSessionManager.SelectedObjects != null && EditorSessionManager.SelectedObjects.Count > 1)
            {
                return EditorSessionManager.SelectedObjects;
            }
            return (primary != null) ? new List<GameObject> { primary } : new List<GameObject>();
        }

        public static void SetNotificationText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            MelonLogger.Msg($"[Studio] {text}");

            if (_notificationBanner != null && _notificationText != null)
            {
                _notificationText.text = text;
                _notificationTimer = Mathf.Max(1.0f, EditorConfigService.Config.NotificationDuration);
                _notificationBanner.SetActive(true);
            }
        }

        public static void UpdateAutoSaveTick(float dt)
        {
            if (!EditorSessionManager.IsCustomSessionActive || EditorSessionManager.IsEditModeActive == false) return;

            int minutes = EditorConfigService.Config.AutoSaveIntervalMinutes;
            if (minutes <= 0) return;

            _autoSaveTimer += dt;
            if (_autoSaveTimer >= minutes * 60f)
            {
                _autoSaveTimer = 0f;
                PerformAutoSaveBackup();
            }
        }

        public static void PerformAutoSaveBackup()
        {
            try
            {
                string baseName = string.IsNullOrWhiteSpace(MapBrowserService.SelectedMapName) ? "Level" : MapBrowserService.SelectedMapName;
                string backupDir = Path.Combine(MapBrowserService.MyLevelsDir, "Backups");
                if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

                string backupPath = Path.Combine(backupDir, $"{baseName}_AutoSave.txt");
                LevelPersistenceService.SaveLevel(backupPath);
                SetNotificationText($"[Auto-Save] Backup created at {DateTime.Now:HH:mm:ss}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Auto-Save] Failed: {ex.Message}");
            }
        }

        public static void UpdateNotificationBannerTick(float dt)
        {
            if (_notificationTimer > 0f)
            {
                _notificationTimer -= dt;
                if (_notificationTimer <= 0f && _notificationBanner != null)
                {
                    _notificationBanner.SetActive(false);
                }
            }
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
            try { BuildNotificationBanner(); } catch (Exception ex) { MelonLogger.Error($"[UI] NotificationBanner: {ex}"); }
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

        private static void BuildNotificationBanner()
        {
            _notificationBanner = new GameObject("Notification_Banner", Il2CppType.Of<RectTransform>());
            _notificationBanner.transform.SetParent(_canvasRoot.transform, false);

            RectTransform nbrt = _notificationBanner.GetComponent<RectTransform>();
            nbrt.anchorMin = new Vector2(0.5f, 0f);
            nbrt.anchorMax = new Vector2(0.5f, 0f);
            nbrt.pivot = new Vector2(0.5f, 0f);
            nbrt.anchoredPosition = new Vector2(0f, 305f);
            nbrt.sizeDelta = new Vector2(450f, 30f);

            Image img = _notificationBanner.AddComponent<Image>();
            img.color = new Color(0.08f, 0.12f, 0.18f, 0.95f);
            img.raycastTarget = false;

            _notificationText = CreateTextPrimitive(_notificationBanner.transform, "", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 11f, FontStyles.Bold, new Color(0.2f, 0.85f, 1f), TextAlignmentOptions.Center);
            _notificationText.raycastTarget = false;

            _notificationBanner.SetActive(false);
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
                    else if (pType == PlacedObjectType.GravityArea)
                    {
                        BoxCollider bc = obj.GetComponent<BoxCollider>();
                        if (bc != null)
                        {
                            bc.isTrigger = true;
                            bc.enabled = true;
                        }
                        obj.layer = 0;
                    }
                }
            }
        }

        // =========================================================================
        // TOP TOOLBAR
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

            // Options Dropdown
            CreateDropdownMenuButton(_toolbarPanel.transform, "Options", 80f, (anchor) =>
            {
                OpenDropdownMenu(anchor, new List<DropdownItem>
                {
                    new DropdownItem("Preferences & Settings", () => _preferencesWin?.Toggle()),
                    new DropdownItem("Save Level", () => LevelPersistenceService.SaveLevel(MapBrowserService.SelectedMapName), null, GetShortcutHint("SaveLevel")),
                    new DropdownItem("Publish to Community", () =>
                    {
                        OpenPublishPreflightModal();
                    }, new Color(0.3f, 0.95f, 0.5f)),
                    new DropdownItem("Load Level", () => LevelPersistenceService.LoadLevel(MapBrowserService.SelectedMapName), null, GetShortcutHint("LoadLevel")),
                    new DropdownItem("Capture Snapshot", () =>
                        ThumbnailCaptureService.CaptureViewportSnapshot(MapBrowserService.SelectedMapPath)
                    ),
                    new DropdownItem("Reset Level", () => EditorSessionManager.ClearAllPlacedObjects(), new Color(0.9f, 0.3f, 0.3f)),
                    new DropdownItem("Select All", () => EditorSessionManager.SelectAllPlacedObjects(), null, GetShortcutHint("SelectAll")),
                    new DropdownItem("Deselect All", () => EditorSessionManager.SelectObject(null), null, "Esc"),
                });
            });

            // Tools Dropdown
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
                    new DropdownItem("+ Procedural Space-Truss Girder", () =>
                    {
                        Vector3 camPos = EditorViewportCamera.ViewportCamera != null
                            ? EditorViewportCamera.ViewportCamera.transform.position + EditorViewportCamera.ViewportCamera.transform.forward * 10f
                            : EditorSessionManager.LevelSpawnPosition;

                        GameObject truss = StructuralTrussService.CreateProceduralTruss(camPos, new Vector3(-5f, 0f, 0f), new Vector3(5f, 0f, 0f));
                        EditorSessionManager.SelectObject(truss);
                        RefreshHierarchy();
                    }),
                });
            });

            // Edit Dropdown
            CreateDropdownMenuButton(_toolbarPanel.transform, "Edit", 70f, (anchor) =>
            {
                var editItems = new List<DropdownItem>
                {
                    new DropdownItem("Undo", () => EditorSessionManager.PerformUndo(), null, GetShortcutHint("Undo")),
                    new DropdownItem("Redo", () => EditorSessionManager.PerformRedo(), null, GetShortcutHint("Redo")),
                    new DropdownItem("Duplicate", () => EditorSessionManager.DuplicateSelectedObjects(), null, GetShortcutHint("Duplicate")),
                    new DropdownItem("Create Prefab", () => QuickAutoCreatePrefab(), null, GetShortcutHint("CreatePrefab")),
                    new DropdownItem("Snap 90 deg", () => ExecuteSnap90(), null, GetShortcutHint("Snap90")),
                    new DropdownItem("Reset Rotation", () => ExecuteResetRotation(), null, GetShortcutHint("ResetRot")),
                    new DropdownItem("Parent Selected", () => EditorSessionManager.ParentSelectedObjects(), null, GetShortcutHint("Parent")),
                    new DropdownItem("Unparent Selected", () => EditorSessionManager.UnparentSelectedObjects(), null, GetShortcutHint("Unparent"))
                };

                if (EditorSessionManager.UndoHistory != null && EditorSessionManager.UndoHistory.Count > 0)
                {
                    editItems.Add(new DropdownItem("--- Recent Actions ---", null, Color.gray));
                    var stackArr = EditorSessionManager.UndoHistory.ToArray();
                    int count = Mathf.Min(stackArr.Length, 6);
                    for (int h = 0; h < count; h++)
                    {
                        var rec = stackArr[h];
                        string desc = $"{rec.ActionType}: {rec.AssetName ?? "Object"}";
                        editItems.Add(new DropdownItem($"[{h + 1}] {desc}", () =>
                        {
                            EditorSessionManager.PerformUndo();
                        }, new Color(0.6f, 0.75f, 0.9f)));
                    }
                }

                OpenDropdownMenu(anchor, editItems);
            });

            CreateToolbarDivider(_toolbarPanel.transform);

            // Direct Transform Buttons
            CreateButtonPrimitive(_toolbarPanel.transform, "Btn_Translate", "Move", 70f, () => SetGizmoMode(EditorGizmoMode.Translate));
            CreateButtonPrimitive(_toolbarPanel.transform, "Btn_Rotate", "Rotate", 70f, () => SetGizmoMode(EditorGizmoMode.Rotate));
            CreateButtonPrimitive(_toolbarPanel.transform, "Btn_Scale", "Scale", 70f, () => SetGizmoMode(EditorGizmoMode.Scale));

            CreateToolbarDivider(_toolbarPanel.transform);

            // Surface Align Matrix Flyout
            Button alignBtn = CreateButtonPrimitive(_toolbarPanel.transform, "Btn_SurfaceAlign", "Align: OFF", 95f, () =>
            {
                RectTransform art = _surfaceAlignBtnText?.transform.parent?.GetComponent<RectTransform>();
                if (art != null)
                {
                    OpenDropdownMenu(art, new List<DropdownItem>
                    {
                        new DropdownItem(EditorSessionManager.AutoAlignToSurface ? "Disable Auto-Align" : "Enable Auto-Align", () =>
                        {
                            EditorSessionManager.AutoAlignToSurface = !EditorSessionManager.AutoAlignToSurface;
                            string st = EditorSessionManager.AutoAlignToSurface ? "ON" : "OFF";
                            if (_surfaceAlignBtnText != null) _surfaceAlignBtnText.text = $"Align: {st}";
                            PlacementHologramController.ApplyRotationToPreview();
                        }, EditorSessionManager.AutoAlignToSurface ? new Color(0.3f, 0.95f, 0.4f) : Color.white),
                        new DropdownItem("Normal Invert (Ceiling)", () =>
                        {
                            EditorSessionManager.TargetRoll = (EditorSessionManager.TargetRoll + 180f) % 360f;
                            PlacementHologramController.ApplyRotationToPreview();
                        }),
                        new DropdownItem("Reset Upright", () =>
                        {
                            EditorSessionManager.TargetPitch = 0f;
                            EditorSessionManager.TargetRoll = 0f;
                            PlacementHologramController.ApplyRotationToPreview();
                        })
                    });
                }
            });
            _surfaceAlignBtnText = alignBtn.GetComponentInChildren<TMP_Text>();

            // Multi-Axis Snapping Matrix Popover
            Button snapBtn = CreateButtonPrimitive(_toolbarPanel.transform, "Btn_GridSnap", "Snap: 1.0m", 95f, () =>
            {
                RectTransform srt = _gridSnapBtnText?.transform.parent?.GetComponent<RectTransform>();
                if (srt != null)
                {
                    OpenDropdownMenu(srt, new List<DropdownItem>
                    {
                        new DropdownItem("Translate: 0.1m", () => SetSnapTranslate(0.1f)),
                        new DropdownItem("Translate: 0.5m", () => SetSnapTranslate(0.5f)),
                        new DropdownItem("Translate: 1.0m", () => SetSnapTranslate(1.0f)),
                        new DropdownItem("Translate: 2.0m", () => SetSnapTranslate(2.0f)),
                        new DropdownItem("Translate: 4.0m", () => SetSnapTranslate(4.0f)),
                        new DropdownItem("Translate: OFF", () => SetSnapTranslate(0.0f), Color.gray),
                        new DropdownItem("Rotate: 15 deg", () => { SnapRotate = 15f; SetNotificationText("Rotation snap: 15 deg"); }),
                        new DropdownItem("Rotate: 45 deg", () => { SnapRotate = 45f; SetNotificationText("Rotation snap: 45 deg"); }),
                        new DropdownItem("Rotate: 90 deg", () => { SnapRotate = 90f; SetNotificationText("Rotation snap: 90 deg"); })
                    });
                }
            });
            _gridSnapBtnText = snapBtn.GetComponentInChildren<TMP_Text>();

            GameObject spacer = new GameObject("Spacer", Il2CppType.Of<RectTransform>());
            spacer.transform.SetParent(_toolbarPanel.transform, false);
            LayoutElement sle = spacer.AddComponent<LayoutElement>();
            sle.flexibleWidth = 1f;

            // Header Publish
            CreateButtonPrimitive(_toolbarPanel.transform, "Btn_HeaderPublish", "PUBLISH", 90f, () =>
            {
                OpenPublishPreflightModal();
            }, new Color(0.18f, 0.65f, 0.35f, 1f));

            CreateButtonPrimitive(_toolbarPanel.transform, "Btn_Playtest", "PLAYTEST (F1)", 125f, () => EditorSessionManager.ToggleEditMode(), new Color(0.18f, 0.65f, 0.32f));
        }

        private static void SetSnapTranslate(float snap)
        {
            EditorSessionManager.CurrentGridSnap = snap;
            SnapTranslate = snap;
            string st = (snap > 0.01f) ? $"{snap}m" : "OFF";
            if (_gridSnapBtnText != null) _gridSnapBtnText.text = $"Snap: {st}";
            SetNotificationText($"Translation snap set to: {st}");
        }

        private static string GetShortcutHint(string key)
        {
            if (EditorConfigService.Config.Keybindings.TryGetValue(key, out string val) && !string.IsNullOrEmpty(val))
                return val;
            return "";
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
            prt.sizeDelta = new Vector2(230f, 28f);

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

            _modeTogglePillText = CreateTextPrimitive(pillObj.transform, "MODE: [SELECT] (Space)", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 11f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        }

        public static void RefreshModeDisplay()
        {
            if (_modeTogglePillText == null || _modeTogglePillBtn == null) return;

            if (EditorSessionManager.InteractionMode == EditorInteractionMode.SelectMode)
            {
                _modeTogglePillText.text = "MODE: [SELECT] (Space)";
                _modeTogglePillBtn.image.color = new Color(0.18f, 0.45f, 0.85f, 0.95f);
            }
            else
            {
                _modeTogglePillText.text = "MODE: [PLACEMENT] (Tab)";
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

        // =========================================================================
        // PRE-FLIGHT SANITY CHECKER MODAL
        // =========================================================================

        private static void OpenPublishPreflightModal()
        {
            var modal = StudioFloatingWindow.Create(_canvasRoot.transform, "Win_PublishPreflight", "Publish Sanity Check", new Vector2(400f, 290f), Vector2.zero);

            VerticalLayoutGroup vlg = modal.ContentRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            bool hasSpawn = false;
            bool hasGoal = false;
            for (int i = 0; i < EditorSessionManager.PlacedObjects.Count; i++)
            {
                var o = EditorSessionManager.PlacedObjects[i];
                if (o == null) continue;
                if (EditorSessionManager.PlacedObjectTypes.TryGetValue(o, out var t))
                {
                    if (t == PlacedObjectType.SpawnGate) hasSpawn = true;
                    if (t == PlacedObjectType.GoalGate) hasGoal = true;
                }
            }

            CreateTextPrimitive(modal.ContentRt, "Pre-Flight Verification Checklist:", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 11f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);

            string spawnStatus = hasSpawn ? "[PASS] Entry Checkpoint present" : "[FAIL] No Entry Checkpoint (Spawn ID 0)";
            CreateTextPrimitive(modal.ContentRt, spawnStatus, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Normal, hasSpawn ? new Color(0.2f, 0.9f, 0.4f) : new Color(0.9f, 0.2f, 0.2f), TextAlignmentOptions.MidlineLeft);

            string goalStatus = hasGoal ? "[PASS] Goal Checkpoint present" : "[FAIL] No Goal Checkpoint (Finish ID 9999)";
            CreateTextPrimitive(modal.ContentRt, goalStatus, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Normal, hasGoal ? new Color(0.2f, 0.9f, 0.4f) : new Color(0.9f, 0.2f, 0.2f), TextAlignmentOptions.MidlineLeft);

            string countStatus = $"[INFO] Total Placed Entities: {EditorSessionManager.PlacedObjects.Count}";
            CreateTextPrimitive(modal.ContentRt, countStatus, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Normal, Color.cyan, TextAlignmentOptions.MidlineLeft);

            GameObject btnRow = CreateRowContainerPrimitive(modal.ContentRt, "Preflight_Btns", 30f);
            SetupRowHorizontalLayoutPrimitive(btnRow, 8f);

            CreateButtonPrimitive(btnRow.transform, "Btn_CancelPublish", "Cancel", 140f, () => modal.Hide(), new Color(0.35f, 0.38f, 0.45f));

            Button commitBtn = CreateButtonPrimitive(btnRow.transform, "Btn_CommitPublish", "Publish to Cloud", 180f, () =>
            {
                modal.Hide();
                LevelPersistenceService.SaveLevel(MapBrowserService.SelectedMapName);
                CommunityLevelService.PublishCurrentLevel(MapBrowserService.SelectedMapPath);
            }, hasSpawn && hasGoal ? new Color(0.18f, 0.65f, 0.35f) : new Color(0.5f, 0.2f, 0.2f));

            if (!hasSpawn || !hasGoal)
            {
                commitBtn.interactable = false;
            }

            modal.Show();
        }

        // =========================================================================
        // DROPDOWN MENU ENGINE
        // =========================================================================

        internal class DropdownItem
        {
            public string Label;
            public Action Callback;
            public Color? TextColor;
            public string Shortcut;
            public DropdownItem(string label, Action cb, Color? col = null, string shortcut = null)
            {
                Label = label;
                Callback = cb;
                TextColor = col;
                Shortcut = shortcut;
            }
        }

        internal static void CreateDropdownMenuButton(Transform parent, string label, float width, Action<RectTransform> onTrigger)
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

        internal static void OpenDropdownMenu(RectTransform anchor, List<DropdownItem> items)
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
            dmRt.sizeDelta = new Vector2(250f, items.Count * 28f + 8f);

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
                if (item.Callback != null)
                {
                    eBtn.onClick.AddListener((Action)(() =>
                    {
                        CloseAllDropdowns();
                        item.Callback?.Invoke();
                    }));
                }

                TMP_Text itemTxt = CreateTextPrimitive(entry.transform, item.Label,
                    Vector2.zero, Vector2.one,
                    Vector2.zero, Vector2.zero,
                    10.5f, FontStyles.Normal, item.TextColor ?? Color.white, TextAlignmentOptions.MidlineLeft);

                RectTransform itRt = itemTxt.GetComponent<RectTransform>();
                itRt.offsetMin = new Vector2(10f, 0f);
                itRt.offsetMax = new Vector2(-75f, 0f);
                itemTxt.raycastTarget = false;

                if (!string.IsNullOrEmpty(item.Shortcut))
                {
                    TMP_Text scTxt = CreateTextPrimitive(entry.transform, item.Shortcut,
                        new Vector2(1f, 0f), Vector2.one,
                        Vector2.zero, Vector2.zero,
                        9.5f, FontStyles.Italic, new Color(0.65f, 0.7f, 0.8f, 0.85f), TextAlignmentOptions.MidlineRight);

                    RectTransform scRt = scTxt.GetComponent<RectTransform>();
                    scRt.pivot = new Vector2(1f, 0.5f);
                    scRt.sizeDelta = new Vector2(70f, 24f);
                    scRt.anchoredPosition = new Vector2(-10f, 0f);
                    scTxt.raycastTarget = false;
                }
            }
        }

        internal static void CloseAllDropdowns()
        {
            if (_activeDropdownMenu != null)
            {
                GameObject.Destroy(_activeDropdownMenu);
                _activeDropdownMenu = null;
            }
            _activeDropdownAnchor = null;
        }

        // =========================================================================
        // FLOATING CATEGORY DOCK
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
            dockRt.sizeDelta = new Vector2(740f, 32f);

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

            for (int i = _floatingCategoryDock.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = _floatingCategoryDock.transform.GetChild(i);
                if (child != null) GameObject.Destroy(child.gameObject);
            }
            _categoryDockButtons.Clear();

            Button allBtn = CreateButtonPrimitive(_floatingCategoryDock.transform, "TabDock_All", "All", 75f, () =>
            {
                _activeBrowserCategory = "All";
                UpdateCategoryDockStyles();
                RefreshAssetBrowser();
            }, new Color(0.14f, 0.16f, 0.20f, 0.90f));
            _categoryDockButtons["All"] = allBtn;

            foreach (var customCat in EditorConfigService.Config.CustomCategories.Keys)
            {
                string catName = customCat;
                int matchCount = EditorConfigService.Config.CustomCategories[catName]?.Count ?? 0;
                string label = $"{catName} [{matchCount}]";

                Button customBtn = CreateButtonPrimitive(_floatingCategoryDock.transform, "TabDock_" + catName, label, 100f, () =>
                {
                    _activeBrowserCategory = catName;
                    UpdateCategoryDockStyles();
                    RefreshAssetBrowser();
                }, new Color(0.14f, 0.16f, 0.20f, 0.90f));

                EventTrigger trigger = customBtn.gameObject.AddComponent<EventTrigger>();
                var clickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                clickEntry.callback.AddListener((Action<BaseEventData>)((e) =>
                {
                    var pe = e.Cast<PointerEventData>();
                    if (pe.button == PointerEventData.InputButton.Right)
                    {
                        RectTransform trt = customBtn.GetComponent<RectTransform>();
                        OpenDropdownMenu(trt, new List<DropdownItem>
                        {
                            new DropdownItem($"Delete Tab: {catName}", () =>
                            {
                                EditorConfigService.DeleteCategory(catName);
                                RebuildCategoryDockButtons();
                                RefreshAssetBrowser();
                            }, new Color(0.9f, 0.3f, 0.3f)),
                            new DropdownItem("Clear Filter Assets", () =>
                            {
                                if (EditorConfigService.Config.CustomCategories.ContainsKey(catName))
                                {
                                    EditorConfigService.Config.CustomCategories[catName].Clear();
                                    EditorConfigService.SaveConfig();
                                    RebuildCategoryDockButtons();
                                    RefreshAssetBrowser();
                                }
                            })
                        });
                    }
                }));
                trigger.triggers.Add(clickEntry);

                _categoryDockButtons[catName] = customBtn;
            }

            CreateButtonPrimitive(_floatingCategoryDock.transform, "Btn_AddNewTabDock", "+ Tab", 60f, () =>
            {
                _assignTargetAsset = null;
                OpenAssignCategoryWindow(null);
            }, new Color(0.18f, 0.55f, 0.35f, 0.95f));

            GameObject spacer = new GameObject("DockSpacer", Il2CppType.Of<RectTransform>());
            spacer.transform.SetParent(_floatingCategoryDock.transform, false);
            LayoutElement sle = spacer.AddComponent<LayoutElement>();
            sle.flexibleWidth = 1f;

            GameObject badgeObj = new GameObject("AssetCountBadge", Il2CppType.Of<RectTransform>());
            badgeObj.transform.SetParent(_floatingCategoryDock.transform, false);
            LayoutElement ble = badgeObj.AddComponent<LayoutElement>();
            ble.preferredWidth = 95f;
            ble.minWidth = 95f;
            ble.flexibleWidth = 0f;
            badgeObj.AddComponent<Image>().color = new Color(0.12f, 0.15f, 0.22f, 0.9f);

            _assetCountBadgeText = CreateTextPrimitive(badgeObj.transform, "0 Props", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 10f, FontStyles.Bold, new Color(0.25f, 0.85f, 1f), TextAlignmentOptions.Center);

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
        // REUSABLE UI PRIMITIVES
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

        internal static GameObject CreateScrollViewPrimitive(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size, out RectTransform content)
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

        internal static GameObject CreateModularSection(Transform parent, string name, string title)
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

        internal static GameObject AddSliderRow(Transform parent, string label, float min, float max, float def, string format, Action<float> onChange)
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

        internal static void AddColorControl(Transform parent, string label, Color initialColor, Action<Color> onColorChanged)
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

        internal static void AddToggleRow(Transform parent, string label, bool initial, Action<bool> onToggle)
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

        internal static void CreateVector3Row(Transform parent, string label, out TMP_InputField xIn, out TMP_InputField yIn, out TMP_InputField zIn, Action<string> onChange)
        {
            GameObject row = CreateRowContainerPrimitive(parent, "Row_" + label, 22f);
            CreateTextPrimitive(row.transform, label, new Vector2(0f, 0f), new Vector2(0.24f, 1f), Vector2.zero, Vector2.zero, 10f, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft);

            xIn = CreateInputFieldPrimitive(row.transform, "X", new Vector2(0.25f, 0f), new Vector2(0.49f, 1f), Vector2.zero, Vector2.zero, "X", onChange);
            yIn = CreateInputFieldPrimitive(row.transform, "Y", new Vector2(0.50f, 0f), new Vector2(0.74f, 1f), Vector2.zero, Vector2.zero, "Y", onChange);
            zIn = CreateInputFieldPrimitive(row.transform, "Z", new Vector2(0.75f, 0f), new Vector2(0.99f, 1f), Vector2.zero, Vector2.zero, "Z", onChange);
        }

        internal static GameObject CreateRowContainerPrimitive(Transform parent, string name, float height)
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

        internal static HorizontalLayoutGroup SetupRowHorizontalLayoutPrimitive(GameObject row, float spacing = 6f)
        {
            if (row == null) return null;
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
    }
}