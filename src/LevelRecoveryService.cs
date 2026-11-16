using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using MelonLoader;

namespace DeadCoreEditor
{
    public class BackupSnapshotInfo
    {
        public string FilePath;
        public string ThumbnailPath;
        public string DisplayName;
        public DateTime Timestamp;
        public int ObjectCount;
        public long FileSizeBytes;
    }

    public static class LevelRecoveryService
    {
        public static string BackupDirectory => Path.Combine(MapBrowserService.MyLevelsDir, "Backups");

        private static string GetCurrentLevelName()
        {
            string name = string.IsNullOrWhiteSpace(MapBrowserService.SelectedMapName)
                ? "Level"
                : Path.GetFileNameWithoutExtension(MapBrowserService.SelectedMapName);
            return string.IsNullOrWhiteSpace(name) ? "Level" : name;
        }

        public static List<BackupSnapshotInfo> GetBackupsForCurrentLevel()
        {
            List<BackupSnapshotInfo> snapshots = new List<BackupSnapshotInfo>();
            if (!Directory.Exists(BackupDirectory)) return snapshots;

            string prefix = GetCurrentLevelName() + "_";
            string[] files = Directory.GetFiles(BackupDirectory, "*.txt", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                string stem = Path.GetFileNameWithoutExtension(path);
                if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                string timestampText = stem.Substring(prefix.Length);
                if (!DateTime.TryParseExact(timestampText, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime timestamp))
                    continue;

                int objectCount = CountObjects(path);
                string thumbnail = Path.ChangeExtension(path, ".png");
                snapshots.Add(new BackupSnapshotInfo
                {
                    FilePath = path,
                    ThumbnailPath = File.Exists(thumbnail) ? thumbnail : null,
                    DisplayName = timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    Timestamp = timestamp,
                    ObjectCount = objectCount,
                    FileSizeBytes = new FileInfo(path).Length
                });
            }

            snapshots.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            return snapshots;
        }

        public static int CountObjects(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return 0;
            int count = 0;
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                count++;
            }
            return count;
        }

        public static void PruneBackupsForLevel(string levelName, int keepCount = 20)
        {
            if (string.IsNullOrWhiteSpace(levelName) || keepCount < 1 || !Directory.Exists(BackupDirectory)) return;
            string prefix = Path.GetFileNameWithoutExtension(levelName) + "_";
            List<string> snapshots = new List<string>();
            string[] candidates = Directory.GetFiles(BackupDirectory, "*.txt", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < candidates.Length; i++)
            {
                string stem = Path.GetFileNameWithoutExtension(candidates[i]);
                if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string stamp = stem.Substring(prefix.Length);
                if (DateTime.TryParseExact(stamp, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    snapshots.Add(candidates[i]);
            }

            snapshots.Sort(StringComparer.OrdinalIgnoreCase);
            int removeCount = snapshots.Count - keepCount;
            for (int i = 0; i < removeCount; i++)
            {
                string textPath = snapshots[i];
                string imagePath = Path.ChangeExtension(textPath, ".png");
                try { if (File.Exists(textPath)) File.Delete(textPath); } catch (Exception ex) { MelonLoader.MelonLogger.Warning("[Recovery] Could not prune backup: " + ex.Message); }
                try { if (File.Exists(imagePath)) File.Delete(imagePath); } catch (Exception ex) { MelonLoader.MelonLogger.Warning("[Recovery] Could not prune thumbnail: " + ex.Message); }
            }
        }

        public static string PreserveExistingFileBeforeOverwrite(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return null;
            try
            {
                if (!Directory.Exists(BackupDirectory)) Directory.CreateDirectory(BackupDirectory);
                string baseName = Path.GetFileNameWithoutExtension(sourcePath);
                DateTime timestamp = DateTime.Now;
                string backupPath;
                do
                {
                    backupPath = Path.Combine(BackupDirectory, baseName + "_" + timestamp.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt");
                    timestamp = timestamp.AddSeconds(1);
                }
                while (File.Exists(backupPath));

                File.Copy(sourcePath, backupPath, false);
                string sourceThumbnail = Path.ChangeExtension(sourcePath, ".png");
                if (File.Exists(sourceThumbnail))
                    File.Copy(sourceThumbnail, Path.ChangeExtension(backupPath, ".png"), true);
                PruneBackupsForLevel(baseName, 20);
                return backupPath;
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning("[Recovery] Could not preserve overwritten file: " + ex.Message);
                return null;
            }
        }

        private static string CreatePreRestoreSnapshot()
        {
            string baseName = GetCurrentLevelName();
            if (!Directory.Exists(BackupDirectory)) Directory.CreateDirectory(BackupDirectory);

            DateTime timestamp = DateTime.Now;
            string path;
            do
            {
                path = Path.Combine(BackupDirectory, baseName + "_" + timestamp.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt");
                timestamp = timestamp.AddSeconds(1);
            }
            while (File.Exists(path));

            if (!LevelPersistenceService.SaveLevelToPath(path, false, MapBrowserService.SelectedMapPath) ||
                !File.Exists(path) || new FileInfo(path).Length == 0)
                throw new IOException("Could not create the pre-restore safety snapshot.");
            return path;
        }

        public static bool RestoreBackup(string backupPath)
        {
            if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
            {
                EditorSessionManager.ShowNotification("Recovery failed: backup file was not found.");
                return false;
            }

            try
            {
                string safeBackupRoot = Path.GetFullPath(BackupDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string fullBackupPath = Path.GetFullPath(backupPath);
                if (!fullBackupPath.StartsWith(safeBackupRoot, StringComparison.OrdinalIgnoreCase))
                {
                    EditorSessionManager.ShowNotification("Recovery failed: selected file is outside the backup folder.");
                    return false;
                }

                int expectedObjectCount = CountObjects(fullBackupPath);
                string guardSnapshot = CreatePreRestoreSnapshot();

                try
                {
                    LevelPersistenceService.LoadLevelByFullPath(fullBackupPath);
                    int restoredObjectCount = EditorSessionManager.PlacedObjects != null ? EditorSessionManager.PlacedObjects.Count : 0;
                    if (restoredObjectCount != expectedObjectCount)
                    {
                        MelonLogger.Warning($"[Recovery] Snapshot restored {restoredObjectCount}/{expectedObjectCount} objects. Rolling back to the pre-restore state.");
                        LevelPersistenceService.LoadLevelByFullPath(guardSnapshot);
                        EditorSessionManager.ShowNotification("Recovery cancelled: the backup could not be fully reconstructed. Your previous level was restored.");
                        return false;
                    }
                }
                catch
                {
                    try { LevelPersistenceService.LoadLevelByFullPath(guardSnapshot); }
                    catch (Exception rollbackEx) { MelonLogger.Error("[Recovery] Rollback failed: " + rollbackEx); }
                    throw;
                }

                StudioUIManager.RefreshHierarchy();
                EditorSessionManager.ShowNotification("Backup restored. Previous working state preserved in Backups.");
                MelonLogger.Msg("[Recovery] Restored backup: " + fullBackupPath);
                MelonLogger.Msg("[Recovery] Pre-restore snapshot: " + guardSnapshot);
                return true;
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error("[Recovery] Restore failed: " + ex);
                EditorSessionManager.ShowNotification("Recovery failed. A safety snapshot was kept when possible.");
                return false;
            }
        }
    }
}
