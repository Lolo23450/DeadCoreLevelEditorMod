using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using MelonLoader;
using Il2Cpp;

using File = System.IO.File;
using Directory = System.IO.Directory;
using Path = System.IO.Path;

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 1: PERSISTENCE & PARSING UTILITIES
    // =========================================================================

    public static class PersistenceUtility
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static float ParseFloat(string str, float defaultValue = 0f)
        {
            if (string.IsNullOrWhiteSpace(str)) return defaultValue;
            str = str.Trim().Replace(',', '.');
            if (float.TryParse(str, NumberStyles.Float, Inv, out float val))
                return val;
            return defaultValue;
        }

        public static int ParseInt(string str, int defaultValue = 0)
        {
            if (string.IsNullOrWhiteSpace(str)) return defaultValue;
            if (int.TryParse(str.Trim(), NumberStyles.Integer, Inv, out int val))
                return val;
            return defaultValue;
        }

        public static Vector3 ParseVector3(string x, string y, string z, Vector3 fallback = default)
        {
            return new Vector3(
                ParseFloat(x, fallback.x),
                ParseFloat(y, fallback.y),
                ParseFloat(z, fallback.z)
            );
        }

        public static Quaternion ParseQuaternion(string x, string y, string z, string w)
        {
            float qx = ParseFloat(x);
            float qy = ParseFloat(y);
            float qz = ParseFloat(z);
            float qw = ParseFloat(w);

            if (Mathf.Approximately(qx, 0f) && Mathf.Approximately(qy, 0f) &&
                Mathf.Approximately(qz, 0f) && Mathf.Approximately(qw, 0f))
            {
                return Quaternion.identity;
            }

            return new Quaternion(qx, qy, qz, qw);
        }

        public static string ColorToHex(Color c)
        {
            byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            return $"{r:X2}{g:X2}{b:X2}";
        }

        public static Color HexToColor(string hex, Color fallback = default)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback == default ? Color.cyan : fallback;
            hex = hex.Trim().TrimStart('#');
            if (hex.Length >= 6 &&
                byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, Inv, out byte r) &&
                byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, Inv, out byte g) &&
                byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, Inv, out byte b))
            {
                return new Color(r / 255f, g / 255f, b / 255f, 1f);
            }
            return fallback == default ? Color.cyan : fallback;
        }

        public static string ExtractSubTagData(string line, string tag)
        {
            int idx = line.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
            if (idx == -1) return null;
            return line.Substring(idx + tag.Length).Split(';')[0].Trim();
        }

        public static string CleanAssetName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return "";
            string s = rawName.Trim();
            if (s.StartsWith("Custom_", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(7);

            int lastUnderscore = s.LastIndexOf('_');
            if (lastUnderscore > 0 && lastUnderscore < s.Length - 1)
            {
                string tail = s.Substring(lastUnderscore + 1);
                if (tail.StartsWith("(") && tail.EndsWith(")"))
                {
                    s = s.Substring(0, lastUnderscore);
                }
                else if (int.TryParse(tail, out _))
                {
                    s = s.Substring(0, lastUnderscore);
                }
            }

            int lastParen = s.LastIndexOf('(');
            if (lastParen > 0 && s.EndsWith(")"))
            {
                s = s.Substring(0, lastParen).Trim();
            }

            return s.Replace("_", " ").Trim();
        }
    }

    // =========================================================================
    // SECTION 2: PREFAB INSTANCE MANAGER
    // =========================================================================

    public static class PrefabInstanceManager
    {
        public static List<CustomPrefabTemplate> SavedPrefabs = new List<CustomPrefabTemplate>();
        public static string PrefabsDir => Path.Combine(Directory.GetCurrentDirectory(), "UserData", "MyPrefabs");

        public static void EnsureDirectories()
        {
            if (!Directory.Exists(PrefabsDir)) Directory.CreateDirectory(PrefabsDir);
        }

        public static void CreateInstanceTemplateFromSelection(string templateName = "")
        {
            if (EditorSessionManager.SelectedObjects == null || EditorSessionManager.SelectedObjects.Count == 0)
            {
                EditorSessionManager.ShowNotification("Select objects first to create an Instance.");
                return;
            }

            EnsureDirectories();

            if (string.IsNullOrWhiteSpace(templateName))
            {
                int idx = SavedPrefabs.Count + 1;
                templateName = $"Instance_{idx}";
                while (File.Exists(Path.Combine(PrefabsDir, $"{templateName}.prefab.txt")))
                {
                    idx++;
                    templateName = $"Instance_{idx}";
                }
            }

            Bounds collectiveBounds = new Bounds();
            bool boundsInit = false;

            for (int i = 0; i < EditorSessionManager.SelectedObjects.Count; i++)
            {
                GameObject obj = EditorSessionManager.SelectedObjects[i];
                if (obj == null) continue;
                if (EditorSessionManager.IsWaypointMarker(obj, out _, out _)) continue;

                Bounds b = StudioGizmoController.GetObjectWorldBounds(obj);
                if (!boundsInit)
                {
                    collectiveBounds = b;
                    boundsInit = true;
                }
                else
                {
                    collectiveBounds.Encapsulate(b);
                }
            }

            Vector3 collectiveCenter = boundsInit ? collectiveBounds.center : EditorSessionManager.SelectedObject.transform.position;
            CustomPrefabTemplate template = new CustomPrefabTemplate
            {
                Name = templateName,
                TotalBounds = collectiveBounds,
                FilePath = Path.Combine(PrefabsDir, $"{templateName}.prefab.txt")
            };

            for (int i = 0; i < EditorSessionManager.SelectedObjects.Count; i++)
            {
                GameObject obj = EditorSessionManager.SelectedObjects[i];
                if (obj == null) continue;
                if (EditorSessionManager.IsWaypointMarker(obj, out _, out _)) continue;

                string rawName = obj.name.StartsWith("Custom_") ? obj.name.Substring(7) : obj.name;
                EditorEntityData entityData = EditorSessionManager.ExtractEntityData(obj);

                template.Items.Add(new PrefabInstanceItem
                {
                    AssetName = rawName,
                    LocalPosition = obj.transform.position - collectiveCenter,
                    LocalRotation = obj.transform.rotation,
                    LocalScale = obj.transform.localScale,
                    EntityData = entityData
                });
            }

            BuildVisualTemplateObject(template);
            SavePrefabToDisk(template);

            RegisterTemplateAsCatalogAsset(template);
            SavedPrefabs.Add(template);

            EditorSessionManager.ShowNotification($"Saved Instance: '{templateName}' ({template.Items.Count} objects)");
            StudioUIManager.RefreshAssetBrowser();
        }

        public static void BuildVisualTemplateObject(CustomPrefabTemplate template)
        {
            if (template.SourceTemplate != null)
                GameObject.Destroy(template.SourceTemplate);

            GameObject root = new GameObject($"Template_Instance_{template.Name}");
            root.transform.position = new Vector3(8500f, 8500f, 8500f);
            root.SetActive(false);

            for (int i = 0; i < template.Items.Count; i++)
            {
                PrefabInstanceItem item = template.Items[i];
                GameObject child = EditorSessionManager.SpawnAssetByName(item.AssetName, item.LocalPosition, item.LocalScale, item.LocalRotation);
                if (child != null)
                {
                    child.name = item.AssetName;
                    child.transform.SetParent(root.transform, false);
                    child.transform.localPosition = item.LocalPosition;
                    child.transform.localRotation = item.LocalRotation;
                    child.transform.localScale = item.LocalScale;
                }
            }

            template.SourceTemplate = root;
        }

        public static GameObject SpawnInstance(CustomPrefabTemplate template, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (template == null || template.Items.Count == 0) return null;

            GameObject instanceRoot = new GameObject($"Custom_Instance_{template.Name}");
            instanceRoot.transform.position = position;
            instanceRoot.transform.rotation = rotation;
            instanceRoot.transform.localScale = scale;

            for (int i = 0; i < template.Items.Count; i++)
            {
                PrefabInstanceItem item = template.Items[i];
                Vector3 worldPos = position + (rotation * Vector3.Scale(item.LocalPosition, scale));
                Quaternion worldRot = rotation * item.LocalRotation;
                Vector3 worldScale = Vector3.Scale(item.LocalScale, scale);

                GameObject child = EditorSessionManager.SpawnAssetByName(item.AssetName, worldPos, worldScale, worldRot);
                if (child != null)
                {
                    child.transform.SetParent(instanceRoot.transform, true);
                    child.transform.localScale = worldScale;

                    if (item.EntityData != null)
                    {
                        EditorSessionManager.ApplyEntityData(child, item.EntityData.Clone());
                    }

                    EditorSessionManager.RegisterPlacedObject(child);
                }
            }

            EditorSessionManager.RegisterPlacedObject(instanceRoot);
            return instanceRoot;
        }

        public static void DeleteInstance(CustomPrefabTemplate template)
        {
            if (template == null) return;

            if (File.Exists(template.FilePath))
            {
                try { File.Delete(template.FilePath); } catch { }
            }

            string pngPath = Path.ChangeExtension(template.FilePath, ".png");
            if (File.Exists(pngPath))
            {
                try { File.Delete(pngPath); } catch { }
            }

            CatalogAsset catAsset = EditorSessionManager.AllAssets.Find(a => a.PrefabTemplate == template);
            if (catAsset != null)
            {
                EditorSessionManager.AllAssets.Remove(catAsset);
            }

            if (template.SourceTemplate != null)
            {
                GameObject.Destroy(template.SourceTemplate);
            }

            SavedPrefabs.Remove(template);
            EditorSessionManager.ShowNotification($"Deleted Instance: '{template.Name}'");
            StudioUIManager.RefreshAssetBrowser();
        }

        public static void RegisterTemplateAsCatalogAsset(CustomPrefabTemplate template)
        {
            if (template.SourceTemplate == null)
                BuildVisualTemplateObject(template);

            CatalogAsset asset = new CatalogAsset
            {
                DisplayName = $"[Prefab] {template.Name}",
                SourceTemplate = template.SourceTemplate,
                Category = AssetCategory.Building,
                SubCategory = "Instances",
                DefaultScale = 1.0f,
                IsPrefabInstance = true,
                PrefabTemplate = template
            };

            asset.ComputeSizeMetrics();
            EditorSessionManager.AllAssets.Add(asset);
        }

        public static void SavePrefabToDisk(CustomPrefabTemplate template)
        {
            EnsureDirectories();
            List<string> lines = new List<string>();
            var inv = PersistenceUtility.Inv;

            lines.Add($"#PREFAB: {template.Name}");
            for (int i = 0; i < template.Items.Count; i++)
            {
                var it = template.Items[i];
                string line = $"{it.AssetName};{it.LocalPosition.x.ToString("F4", inv)};{it.LocalPosition.y.ToString("F4", inv)};{it.LocalPosition.z.ToString("F4", inv)};" +
                              $"{it.LocalRotation.x.ToString("F4", inv)};{it.LocalRotation.y.ToString("F4", inv)};{it.LocalRotation.z.ToString("F4", inv)};{it.LocalRotation.w.ToString("F4", inv)};" +
                              $"{it.LocalScale.x.ToString("F4", inv)};{it.LocalScale.y.ToString("F4", inv)};{it.LocalScale.z.ToString("F4", inv)};{it.CustomParameter.ToString("F2", inv)}";

                if (it.EntityData != null)
                {
                    foreach (var comp in it.EntityData.Components.Values)
                    {
                        line += $";COMP:{comp.ComponentTag}:{comp.Serialize()}";
                    }
                }

                lines.Add(line);
            }

            File.WriteAllLines(template.FilePath, lines.ToArray());
        }

        public static void LoadAllPrefabsFromDisk()
        {
            EnsureDirectories();
            string[] files = Directory.GetFiles(PrefabsDir, "*.prefab.txt");

            for (int f = 0; f < files.Length; f++)
            {
                string path = files[f];
                string name = Path.GetFileNameWithoutExtension(path).Replace(".prefab", "");

                if (SavedPrefabs.Exists(p => p.Name == name)) continue;

                string[] lines = File.ReadAllLines(path);
                CustomPrefabTemplate template = new CustomPrefabTemplate
                {
                    Name = name,
                    FilePath = path
                };

                for (int l = 0; l < lines.Length; l++)
                {
                    string line = lines[l].Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

                    string[] p = line.Split(';');
                    if (p.Length < 11) continue;

                    var item = new PrefabInstanceItem
                    {
                        AssetName = p[0],
                        LocalPosition = PersistenceUtility.ParseVector3(p[1], p[2], p[3]),
                        LocalRotation = PersistenceUtility.ParseQuaternion(p[4], p[5], p[6], p[7]),
                        LocalScale = PersistenceUtility.ParseVector3(p[8], p[9], p[10], Vector3.one),
                        CustomParameter = (p.Length >= 12) ? PersistenceUtility.ParseFloat(p[11]) : 0f
                    };

                    LevelPersistenceService.ParseComponentTokensIntoEntityData(line, item.EntityData, item.AssetName, item.CustomParameter, null, item.LocalPosition);

                    template.Items.Add(item);
                }

                BuildVisualTemplateObject(template);
                RegisterTemplateAsCatalogAsset(template);
                SavedPrefabs.Add(template);
            }
        }
    }

    // =========================================================================
    // SECTION 3: LEVEL SERIALIZATION & PERSISTENCE SERVICE
    // =========================================================================

    public static class LevelPersistenceService
    {
        // New fields are introduced additively. The loader continues to accept older files
        // that do not contain a format marker (treated as version 1).
        public const int CurrentFormatVersion = 3;

        public static bool SaveLevel(string filename)
        {
            try
            {
                string saveDir = MapBrowserService.MyLevelsDir;
                if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

                string cleanName = Path.GetFileNameWithoutExtension(filename);
                if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "Default_Level";

                string path = Path.Combine(saveDir, $"{cleanName}.txt");
                if (File.Exists(path))
                {
                    string preserved = LevelRecoveryService.PreserveExistingFileBeforeOverwrite(path);
                    if (string.IsNullOrWhiteSpace(preserved))
                    {
                        EditorSessionManager.ShowNotification("Save cancelled: could not create a safety backup of the existing level.");
                        MelonLogger.Warning("[Persistence] Refusing to overwrite without a recovery snapshot: " + path);
                        return false;
                    }
                }

                bool saved = SaveLevelToPath(path, true, path);
                if (!saved)
                    EditorSessionManager.ShowNotification("Save failed. The previous file was preserved when possible.");
                return saved;
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Persistence] Save failed: " + ex);
                EditorSessionManager.ShowNotification("Save failed. Check the MelonLoader log for details.");
                return false;
            }
        }

        /// <summary>
        /// Saves the current in-memory level under a new name. Returns null when the
        /// destination exists and overwrite was not explicitly allowed.
        /// </summary>
        public static string NormalizeLevelName(string filename)
        {
            string cleanName = Path.GetFileNameWithoutExtension(filename ?? string.Empty).Trim();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++) cleanName = cleanName.Replace(invalid[i], '_');
            cleanName = cleanName.Trim().Trim('.');
            return cleanName;
        }

        public static string SaveLevelAs(string filename, bool makeActive, bool overwrite)
        {
            try
            {
                string cleanName = NormalizeLevelName(filename);
                if (string.IsNullOrWhiteSpace(cleanName)) return null;

                if (!Directory.Exists(MapBrowserService.MyLevelsDir))
                    Directory.CreateDirectory(MapBrowserService.MyLevelsDir);

                string path = Path.Combine(MapBrowserService.MyLevelsDir, cleanName + ".txt");
                if (File.Exists(path) && !overwrite) return null;
                if (File.Exists(path) && overwrite)
                {
                    string preserved = LevelRecoveryService.PreserveExistingFileBeforeOverwrite(path);
                    if (string.IsNullOrWhiteSpace(preserved)) return null;
                }

                string metadataSource = (File.Exists(path) && overwrite)
                    ? path
                    : (!string.IsNullOrWhiteSpace(MapBrowserService.SelectedMapPath) && File.Exists(MapBrowserService.SelectedMapPath)
                        ? MapBrowserService.SelectedMapPath
                        : path);

                if (!SaveLevelToPath(path, makeActive, metadataSource)) return null;
                if (makeActive)
                {
                    MapBrowserService.SelectedMapPath = path;
                    MapBrowserService.SelectedMapName = cleanName;
                    MapBrowserService.RefreshFiles();
                }
                return path;
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Persistence] Save As failed: " + ex);
                EditorSessionManager.ShowNotification("Save As failed. Check the MelonLoader log for details.");
                return null;
            }
        }

        public static string GetUniqueLevelName(string requestedName)
        {
            string baseName = NormalizeLevelName(requestedName);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Level_Copy";

            if (!Directory.Exists(MapBrowserService.MyLevelsDir))
                Directory.CreateDirectory(MapBrowserService.MyLevelsDir);

            string candidate = baseName;
            int suffix = 2;
            while (File.Exists(Path.Combine(MapBrowserService.MyLevelsDir, candidate + ".txt")))
            {
                candidate = baseName + "_" + suffix;
                suffix++;
            }
            return candidate;
        }

        public static bool SaveLevelToPath(string path, bool makeActive, string metadataSourcePath = null)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string saveDir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(saveDir) && !Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

                List<string> lines = new List<string>();
                var inv = PersistenceUtility.Inv;

                string cleanName = Path.GetFileNameWithoutExtension(path);
                string metadataPath = string.IsNullOrWhiteSpace(metadataSourcePath) ? path : metadataSourcePath;
                LevelMetadata existingMeta = NativeLogsMenuHijacker.ReadLevelMetadata(metadataPath, cleanName);
                lines.Add($"#FORMAT_VERSION: {CurrentFormatVersion}");
                lines.Add($"#TITLE: {existingMeta.Title}");
                lines.Add($"#AUTHOR: {existingMeta.Author}");
                lines.Add($"#DIFFICULTY: {existingMeta.Difficulty}");
                lines.Add($"#DESC: {existingMeta.Description}");
                lines.Add($"#SCENE: {MapBrowserService.SelectedStagingScene}");

                for (int i = 0; i < EditorSessionManager.PlacedObjects.Count; i++)
                {
                    GameObject obj = EditorSessionManager.PlacedObjects[i];
                    if (obj == null) continue;

                    Vector3 pos = EditorSessionManager.MotionPaths.ContainsKey(obj)
                        ? EditorSessionManager.MotionPaths[obj].PointA
                        : obj.transform.position;

                    Quaternion rot = obj.transform.rotation;
                    Vector3 scl = obj.transform.localScale;
                    string name = obj.name.StartsWith("Custom_") ? obj.name.Substring(7) : obj.name;

                    int parentIdx = -1;
                    Transform serializedParent = obj.transform.parent;
                    if (serializedParent != null && EditorSessionManager.PlacedObjects.Contains(serializedParent.gameObject))
                        parentIdx = EditorSessionManager.PlacedObjects.IndexOf(serializedParent.gameObject);

                    // Version 3 stores hierarchy-relative transforms explicitly. Earlier
                    // versions wrote localScale but restored it as though it were worldScale.
                    Vector3 savedLocalPosition = serializedParent != null && parentIdx >= 0
                        ? serializedParent.InverseTransformPoint(pos)
                        : pos;
                    Quaternion savedLocalRotation = serializedParent != null && parentIdx >= 0
                        ? Quaternion.Inverse(serializedParent.rotation) * rot
                        : rot;
                    Vector3 savedScale = parentIdx >= 0 ? obj.transform.localScale : obj.transform.lossyScale;

                    float legacyParam = 0f;
                    EditorEntityData data = EditorSessionManager.ExtractEntityData(obj);

                    if (data.Has<JumperConfig>()) legacyParam = data.Get<JumperConfig>().Force;
                    else if (data.Has<TurbineConfig>()) legacyParam = data.Get<TurbineConfig>().Speed;
                    else if (data.Has<TurretConfig>()) legacyParam = data.Get<TurretConfig>().FireDelay;
                    else if (data.Has<LaserConfig>()) legacyParam = data.Get<LaserConfig>().RotationSpeed;
                    else if (data.Has<LightConfig>()) legacyParam = data.Get<LightConfig>().Intensity;
                    else if (data.Has<GravityConfig>()) legacyParam = data.Get<GravityConfig>().GravityForce;

                    string line = $"{name};{pos.x.ToString("F4", inv)};{pos.y.ToString("F4", inv)};{pos.z.ToString("F4", inv)};" +
                                  $"{scl.x.ToString("F4", inv)};{rot.x.ToString("F4", inv)};{rot.y.ToString("F4", inv)};{rot.z.ToString("F4", inv)};{rot.w.ToString("F4", inv)};" +
                                  $"{legacyParam.ToString("F2", inv)}";

                    foreach (var comp in data.Components.Values)
                        line += $";COMP:{comp.ComponentTag}:{comp.Serialize()}";

                    line += $";PARENT:{parentIdx}";
                    line += $";SCALE3:{savedScale.x.ToString("F4", inv)}:{savedScale.y.ToString("F4", inv)}:{savedScale.z.ToString("F4", inv)}";
                    line += $";LOCALPOS:{savedLocalPosition.x.ToString("F4", inv)}:{savedLocalPosition.y.ToString("F4", inv)}:{savedLocalPosition.z.ToString("F4", inv)}";
                    line += $";LOCALROT:{savedLocalRotation.x.ToString("F5", inv)}:{savedLocalRotation.y.ToString("F5", inv)}:{savedLocalRotation.z.ToString("F5", inv)}:{savedLocalRotation.w.ToString("F5", inv)}";
                    line += $";ACTIVE:{(obj.activeSelf ? 1 : 0)}";
                    lines.Add(line);
                }

                File.WriteAllLines(path, lines.ToArray());
                if (!File.Exists(path) || new System.IO.FileInfo(path).Length == 0)
                    throw new System.IO.IOException("The level file was not written successfully.");

                try
                {
                    ThumbnailCaptureService.CaptureLevelThumbnail(path, EditorSessionManager.PlacedObjects, EditorSessionManager.LevelSpawnPosition);
                }
                catch (Exception thumbnailEx)
                {
                    MelonLogger.Warning("[Persistence] Level saved, but thumbnail capture failed: " + thumbnailEx.Message);
                }

                if (makeActive)
                {
                    EditorSessionManager.ShowNotification($"Saved {lines.Count - 6} objects to {cleanName}.txt!");
                    MapBrowserService.SelectedMapPath = path;
                    MapBrowserService.SelectedMapName = cleanName;
                    MapBrowserService.RefreshFiles();
                }

                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Persistence] Could not write '" + path + "': " + ex);
                return false;
            }
        }

        public static void LoadLevel(string filename)
        {
            string cleanName = Path.GetFileNameWithoutExtension(filename);
            string path = Path.Combine(MapBrowserService.MyLevelsDir, $"{cleanName}.txt");
            if (!File.Exists(path))
                path = Path.Combine(MapBrowserService.DownloadedLevelsDir, $"{cleanName}.txt");

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
            List<Vector3> loadedScales = new List<Vector3>();
            List<Vector3> loadedLocalPositions = new List<Vector3>();
            List<Quaternion> loadedLocalRotations = new List<Quaternion>();
            List<bool> hasLocalTransforms = new List<bool>();
            int count = 0;
            int fileFormatVersion = 1;
            for (int i = 0; i < lines.Length; i++)
            {
                string header = lines[i] != null ? lines[i].Trim() : string.Empty;
                if (header.StartsWith("#FORMAT_VERSION:", StringComparison.OrdinalIgnoreCase))
                {
                    fileFormatVersion = PersistenceUtility.ParseInt(header.Substring("#FORMAT_VERSION:".Length), 1);
                    break;
                }
            }
            if (fileFormatVersion > CurrentFormatVersion)
                MelonLogger.Warning($"[Persistence] Level format v{fileFormatVersion} is newer than this editor (v{CurrentFormatVersion}); attempting backward-compatible load.");

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
                Vector3 pos = PersistenceUtility.ParseVector3(p[1], p[2], p[3]);

                float uniformScale = PersistenceUtility.ParseFloat(p[4], 0.55f);
                if (uniformScale <= 0.0001f) uniformScale = 0.55f;
                Vector3 scaleVec = Vector3.one * uniformScale;

                string scale3Data = PersistenceUtility.ExtractSubTagData(trimmed, ";SCALE3:");
                if (!string.IsNullOrEmpty(scale3Data))
                {
                    string[] sParts = scale3Data.Split(':');
                    if (sParts.Length >= 3)
                        scaleVec = PersistenceUtility.ParseVector3(sParts[0], sParts[1], sParts[2], scaleVec);
                }

                scaleVec.x = Mathf.Max(0.01f, scaleVec.x);
                scaleVec.y = Mathf.Max(0.01f, scaleVec.y);
                scaleVec.z = Mathf.Max(0.01f, scaleVec.z);

                Quaternion rot = Quaternion.identity;
                if (p.Length >= 9)
                {
                    rot = PersistenceUtility.ParseQuaternion(p[5], p[6], p[7], p[8]);
                }

                float customParam = (p.Length >= 10) ? PersistenceUtility.ParseFloat(p[9]) : 0f;

                GameObject obj = EditorSessionManager.SpawnAssetByName(rawName, pos, scaleVec, rot);
                if (obj == null)
                {
                    string cleanName = PersistenceUtility.CleanAssetName(rawName);
                    obj = EditorSessionManager.SpawnAssetByName(cleanName, pos, scaleVec, rot);
                }

                if (obj != null)
                {
                    obj.transform.position = pos;
                    obj.transform.rotation = rot;
                    obj.transform.localScale = scaleVec;

                    EditorEntityData data = new EditorEntityData();
                    ParseComponentTokensIntoEntityData(trimmed, data, rawName, customParam, p, pos);
                    EditorSessionManager.ApplyEntityData(obj, data);

                    int pIndex = -1;
                    string parentData = PersistenceUtility.ExtractSubTagData(trimmed, ";PARENT:");
                    if (!string.IsNullOrEmpty(parentData))
                    {
                        pIndex = PersistenceUtility.ParseInt(parentData, -1);
                    }

                    loadedParentIndices.Add(pIndex);
                    loadedScales.Add(scaleVec);

                    string localPosData = PersistenceUtility.ExtractSubTagData(trimmed, ";LOCALPOS:");
                    string localRotData = PersistenceUtility.ExtractSubTagData(trimmed, ";LOCALROT:");
                    bool hasLocalTransform = !string.IsNullOrEmpty(localPosData) && !string.IsNullOrEmpty(localRotData);
                    Vector3 localPos = Vector3.zero;
                    Quaternion localRot = Quaternion.identity;
                    if (hasLocalTransform)
                    {
                        string[] lp = localPosData.Split(':');
                        string[] lr = localRotData.Split(':');
                        if (lp.Length >= 3 && lr.Length >= 4)
                        {
                            localPos = PersistenceUtility.ParseVector3(lp[0], lp[1], lp[2]);
                            localRot = PersistenceUtility.ParseQuaternion(lr[0], lr[1], lr[2], lr[3]);
                        }
                        else hasLocalTransform = false;
                    }
                    loadedLocalPositions.Add(localPos);
                    loadedLocalRotations.Add(localRot);
                    hasLocalTransforms.Add(hasLocalTransform);

                    EditorSessionManager.RegisterPlacedObject(obj);
                    string activeTag = PersistenceUtility.ExtractSubTagData(trimmed, ";ACTIVE:");
                    if (!string.IsNullOrEmpty(activeTag) && PersistenceUtility.ParseInt(activeTag, 1) == 0)
                        obj.SetActive(false);
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
                        if (i < hasLocalTransforms.Count && hasLocalTransforms[i])
                        {
                            // New-format levels restore the complete local transform.
                            // SetParent(false) avoids Unity preserving a temporary world
                            // transform and then multiplying the parent's scale again.
                            child.transform.SetParent(parent.transform, false);
                            child.transform.localPosition = loadedLocalPositions[i];
                            child.transform.localRotation = loadedLocalRotations[i];
                            child.transform.localScale = i < loadedScales.Count ? loadedScales[i] : Vector3.one;
                        }
                        else
                        {
                            // Legacy files stored world position/rotation and local scale.
                            // Preserve those values without dividing localScale by lossyScale.
                            Vector3 desiredWorldPos = child.transform.position;
                            Quaternion desiredWorldRot = child.transform.rotation;
                            child.transform.SetParent(parent.transform, true);
                            child.transform.position = desiredWorldPos;
                            child.transform.rotation = desiredWorldRot;
                            if (i < loadedScales.Count) child.transform.localScale = loadedScales[i];
                        }

                        EditorSessionManager.RecalculateParentChildCount(parent);
                    }
                }
            }

            EditorSessionManager.CapturePlaytestSnapshots();

            string fName = Path.GetFileNameWithoutExtension(fullPath);
            EditorSessionManager.ShowNotification($"Loaded {count} objects from {fName}.txt!");
            StudioUIManager.RefreshHierarchy();
        }

        public static void ParseComponentTokensIntoEntityData(string line, EditorEntityData data, string rawName, float legacyParam, string[] splitParts = null, Vector3 fallbackPos = default)
        {
            if (data == null) return;

            string lowName = rawName.ToLower();

            int compIdx = 0;
            bool foundModernTags = false;
            while ((compIdx = line.IndexOf(";COMP:", compIdx, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                foundModernTags = true;
                string sub = line.Substring(compIdx + 6).Split(';')[0];
                int firstColon = sub.IndexOf(':');
                if (firstColon != -1)
                {
                    string tag = sub.Substring(0, firstColon).ToUpper();
                    string payload = sub.Substring(firstColon + 1);

                    IEditorComponent comp = CreateComponentByTag(tag);
                    if (comp != null)
                    {
                        comp.Deserialize(payload);

                        if (comp is ObjectMotionPath pathComp)
                        {
                            if (!pathComp.IsActive || (pathComp.TotalDistance < 0.05f && Mathf.Abs(pathComp.RotationSpeed) < 0.01f))
                            {
                                compIdx += 6;
                                continue;
                            }
                        }

                        data.Set(comp);
                    }
                }
                compIdx += 6;
            }

            if (!foundModernTags)
            {
                if (lowName.Contains("spawn") || lowName.Contains("entry"))
                {
                    var gc = data.GetOrCreate<GateConfig>();
                    gc.IsSpawn = true;
                    gc.IsGoal = false;
                }
                else if (lowName.Contains("goal") || lowName.Contains("finish"))
                {
                    var gc = data.GetOrCreate<GateConfig>();
                    gc.IsSpawn = false;
                    gc.IsGoal = true;
                }
                else if (lowName.Contains("checkpoint") || lowName.Contains("gate"))
                {
                    var gc = data.GetOrCreate<GateConfig>();
                    gc.IsSpawn = false;
                    gc.IsGoal = false;
                }
                else if (lowName.Contains("jumper"))
                {
                    var jc = data.GetOrCreate<JumperConfig>();
                    if (legacyParam > 0f) jc.Force = legacyParam;
                }
                else if (lowName.Contains("helix"))
                {
                    var tc = data.GetOrCreate<TurbineConfig>();
                    if (legacyParam > 0f) tc.Speed = legacyParam;
                }
                else if (lowName.Contains("turret"))
                {
                    var tc = data.GetOrCreate<TurretConfig>();
                    if (legacyParam > 0f) tc.FireDelay = legacyParam;
                }
                else if (lowName.Contains("rotating") || lowName.Contains("laser"))
                {
                    var lc = data.GetOrCreate<LaserConfig>();
                    lc.RotationSpeed = legacyParam;
                    lc.IsRotating = lowName.Contains("rotating") && legacyParam != 0f;
                }
                else if (lowName.Contains("gravity"))
                {
                    var gc = data.GetOrCreate<GravityConfig>();
                    if (legacyParam > 0.01f) gc.GravityForce = legacyParam;
                }

                if (lowName.Contains("spotlight") || lowName.Contains("sunlight"))
                {
                    var light = data.GetOrCreate<LightConfig>();
                    light.IsDirectional = lowName.Contains("sunlight");
                    if (legacyParam > 0f) light.Intensity = legacyParam;

                    if (splitParts != null)
                    {
                        if (splitParts.Length >= 11) light.SpotAngle = PersistenceUtility.ParseFloat(splitParts[10], light.SpotAngle);
                        if (splitParts.Length >= 12) light.Color = PersistenceUtility.HexToColor(splitParts[11], light.Color);
                        if (splitParts.Length >= 13) light.VolumetricIntensity = PersistenceUtility.ParseFloat(splitParts[12], light.VolumetricIntensity);
                    }
                }

                string pathSub = PersistenceUtility.ExtractSubTagData(line, ";PATH:");
                if (!string.IsNullOrEmpty(pathSub) && pathSub.StartsWith("1:"))
                {
                    var mp = new ObjectMotionPath();
                    mp.Deserialize(pathSub);
                    if (mp.IsActive && (mp.TotalDistance > 0.05f || Mathf.Abs(mp.RotationSpeed) > 0.01f))
                    {
                        data.Set(mp);
                    }
                }

                string skySub = PersistenceUtility.ExtractSubTagData(line, ";SKYBOX:");
                if (!string.IsNullOrEmpty(skySub))
                {
                    var sc = data.GetOrCreate<SkyboxConfig>();
                    sc.Deserialize(skySub);
                }

                string switchSub = PersistenceUtility.ExtractSubTagData(line, ";SWITCH:");
                if (!string.IsNullOrEmpty(switchSub))
                {
                    var swc = data.GetOrCreate<SwitchConfig>();
                    swc.Deserialize(switchSub);
                }
            }
        }

        private static IEditorComponent CreateComponentByTag(string tag)
        {
            switch (tag.ToUpper())
            {
                case "JUMPER": return new JumperConfig();
                case "TURBINE": return new TurbineConfig();
                case "TURRET": return new TurretConfig();
                case "LASER": return new LaserConfig();
                case "LIGHT": return new LightConfig();
                case "PATH": return new ObjectMotionPath();
                case "SKYBOX": return new SkyboxConfig();
                case "SWITCH": return new SwitchConfig();
                case "GATE": return new GateConfig();
                case "NEON": return new NeonConfig();
                case "CABLE": return new CableConfig();
                case "TRUSS": return new TrussConfig();
                case "GRAVITY": return new GravityConfig();
                case "TRIGGER": return new TriggerVolumeConfig();
                case "AUDIO": return new AudioConfig();
                default: return null;
            }
        }
    }

    // =========================================================================
    // SECTION 4: MAP BROWSER & STAGING SCENE SERVICE
    // =========================================================================

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
            AvailableStagingScenes.Add("level01_Spark01");
            AvailableStagingScenes.Add("level01_Spark02");
            AvailableStagingScenes.Add("level02");
            AvailableStagingScenes.Add("level04");
        }

        public static void EnsureDirectories()
        {
            if (!Directory.Exists(MyLevelsDir)) Directory.CreateDirectory(MyLevelsDir);
            if (!Directory.Exists(DownloadedLevelsDir)) Directory.CreateDirectory(DownloadedLevelsDir);

            string defaultMyPath = Path.Combine(MyLevelsDir, "Default_Level.txt");
            if (!File.Exists(defaultMyPath))
            {
                string defaultContent =
                    "#TITLE: Default Level\n" +
                    "#AUTHOR: Community\n" +
                    "#DIFFICULTY: Normal\n" +
                    "#DESC: Starter platform.\n" +
                    "#SCENE: level01_Spark01\n" +
                    "Floor_Platform_16x16;-241.0000;-97.7000;-6.0000;1.0000;0.0000;0.0000;0.7071;0.7071;0.00;PARENT:-1;SCALE3:1.0000:1.0000:1.0000\n" +
                    "Spawn_Gate;-241.0000;-96.7000;-6.0000;1.0000;0.0000;0.0000;0.0000;1.0000;0.00;COMP:GATE:1:0:0;COMP:NEON:1:FF730D:8.00;PARENT:-1;SCALE3:1.0000:1.0000:1.0000\n" +
                    "Skybox_Controller;-247.0000;-96.4750;-0.5000;1.0000;0.0000;0.0000;0.0000;1.0000;0.00;COMP:SKYBOX:4.00:FFEEF5:0.0:0.00;PARENT:-1;SCALE3:1.0000:1.0000:1.0000\n" +
                    "Global_Sunlight;-247.0000;-95.3547;-4.0000;1.0000;0.4082;-0.2346;0.1094;0.8754;3.00;COMP:LIGHT:3.00:60.0:F1DBCA:1.00:1;PARENT:-1;SCALE3:1.0000:1.0000:1.0000\n";

                File.WriteAllText(defaultMyPath, defaultContent);
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

            string targetScene = "level01_Spark01";
            string requested = !string.IsNullOrWhiteSpace(SelectedStagingScene) ? SelectedStagingScene.Trim() : "";

            if (!string.IsNullOrEmpty(requested) && AvailableStagingScenes.Contains(requested))
            {
                targetScene = requested;
            }

            SceneLoader.LoadLevel(targetScene, false, true);
        }
    }
}
