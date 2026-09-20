using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.Networking;
using System.Net.Http;
using System.Threading.Tasks;
using Il2Cpp;

namespace DeadCoreEditor
{
    [Serializable]
    public class RemoteLevelItem
    {
        public string id;
        public string title;
        public string author;
        public string author_steam_id;
        public string difficulty;
        public string staging_scene;
        public string description;
        public int object_count;
        public int downloads;
        public long created_at;
    }

    public static class CommunityLevelService
    {
        public const string BaseApiUrl = "https://deadcore-backend.trufamods.workers.dev";

        public static List<RemoteLevelItem> CachedCommunityLevels = new List<RemoteLevelItem>();
        public static bool IsFetching = false;
        public static string StatusMessage = "";

        public static void FetchCommunityLevels(Action onComplete = null)
        {
            if (IsFetching) return;
            MelonCoroutines.Start(FetchLevelsCoroutine(onComplete));
        }

        private static IEnumerator FetchLevelsCoroutine(Action onComplete)
        {
            IsFetching = true;
            StatusMessage = "Fetching community levels...";

            UnityWebRequest req = UnityWebRequest.Get($"{BaseApiUrl}/api/levels?page=1");
            try
            {
                yield return req.SendWebRequest();

                if (!string.IsNullOrEmpty(req.error))
                {
                    StatusMessage = $"Failed: {req.error}";
                    MelonLogger.Warning($"[Community] Failed to fetch levels: {req.error}");
                }
                else
                {
                    string json = req.downloadHandler.text;
                    CachedCommunityLevels = ParseLevelsJson(json);
                    StatusMessage = $"Loaded {CachedCommunityLevels.Count} levels.";
                }
            }
            finally
            {
                req.Dispose();
            }

            IsFetching = false;
            onComplete?.Invoke();
        }

        public static void DownloadLevel(RemoteLevelItem item, Action<bool> onDone = null)
        {
            MelonCoroutines.Start(DownloadLevelCoroutine(item, onDone));
        }

        private static IEnumerator DownloadLevelCoroutine(RemoteLevelItem item, Action<bool> onDone)
        {
            string saveDir = MapBrowserService.DownloadedLevelsDir;
            if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

            string txtPath = Path.Combine(saveDir, $"{item.title}_{item.id}.txt");
            string pngPath = Path.Combine(saveDir, $"{item.title}_{item.id}.png");

            // 1. Download .txt map file
            UnityWebRequest reqTxt = UnityWebRequest.Get($"{BaseApiUrl}/api/levels/{item.id}/download");
            bool downloadSuccess = false;
            try
            {
                yield return reqTxt.SendWebRequest();

                if (!string.IsNullOrEmpty(reqTxt.error))
                {
                    EditorSessionManager.ShowNotification($"Download failed: {reqTxt.error}");
                }
                else
                {
                    File.WriteAllText(txtPath, reqTxt.downloadHandler.text);
                    downloadSuccess = true;
                }
            }
            finally
            {
                reqTxt.Dispose();
            }

            if (!downloadSuccess)
            {
                onDone?.Invoke(false);
                yield break;
            }

            // 2. Download .png thumbnail
            UnityWebRequest reqPng = UnityWebRequest.Get($"{BaseApiUrl}/api/levels/{item.id}/thumbnail");
            try
            {
                yield return reqPng.SendWebRequest();

                if (string.IsNullOrEmpty(reqPng.error) && reqPng.downloadHandler.data != null)
                {
                    File.WriteAllBytes(pngPath, reqPng.downloadHandler.data);
                }
            }
            finally
            {
                reqPng.Dispose();
            }

            item.downloads++; // Increments the in-memory count immediately
            MapBrowserService.RefreshFiles();
            onDone?.Invoke(true);
        }

        public static void PublishCurrentLevel(string localTxtPath)
        {
            if (!File.Exists(localTxtPath))
            {
                EditorSessionManager.ShowNotification("Level file not found to upload.");
                return;
            }

            MelonCoroutines.Start(PublishLevelCoroutine(localTxtPath));
        }

        private static IEnumerator PublishLevelCoroutine(string localTxtPath)
        {
            EditorSessionManager.ShowNotification("Publishing to community...");

            string mapContent = File.ReadAllText(localTxtPath);
            string fileName = Path.GetFileName(localTxtPath);
            string pngPath = Path.ChangeExtension(localTxtPath, ".png");
            byte[] thumbBytes = File.Exists(pngPath) ? File.ReadAllBytes(pngPath) : Array.Empty<byte>();

            string steamTicket = GetSteamAuthSessionTicket();

            var uploadTask = Task.Run(async () =>
            {
                using var client = new HttpClient();
                using var form = new MultipartFormDataContent();

                // 1. Map .txt file (Quotes explicitly added for Cloudflare Workers parser compatibility)
                var mapPart = new ByteArrayContent(Encoding.UTF8.GetBytes(mapContent));
                mapPart.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
                mapPart.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("form-data")
                {
                    Name = "\"map\"",
                    FileName = $"\"{fileName}\""
                };
                form.Add(mapPart);

                // 2. Thumbnail .png
                if (thumbBytes != null && thumbBytes.Length > 0)
                {
                    var thumbPart = new ByteArrayContent(thumbBytes);
                    thumbPart.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                    thumbPart.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("form-data")
                    {
                        Name = "\"thumbnail\"",
                        FileName = "\"thumb.png\""
                    };
                    form.Add(thumbPart);
                }

                // 3. Steam Auth Header
                client.DefaultRequestHeaders.Add("X-Steam-Auth-Ticket", steamTicket);

                var response = await client.PostAsync($"{BaseApiUrl}/api/levels/upload", form);
                string responseBody = await response.Content.ReadAsStringAsync();
                return (success: response.IsSuccessStatusCode, statusCode: (int)response.StatusCode, body: responseBody);
            });

            while (!uploadTask.IsCompleted)
            {
                yield return null;
            }

            if (uploadTask.IsFaulted)
            {
                string err = uploadTask.Exception?.InnerException?.Message ?? uploadTask.Exception?.Message ?? "Network error";
                EditorSessionManager.ShowNotification($"Upload Failed: {err}");
                MelonLogger.Error($"[Community Upload Exception] {err}");
            }
            else
            {
                var result = uploadTask.Result;
                if (!result.success)
                {
                    EditorSessionManager.ShowNotification($"Upload Failed ({result.statusCode}): {result.body}");
                    MelonLogger.Error($"[Community Upload] {result.statusCode}: {result.body}");
                }
                else
                {
                    EditorSessionManager.ShowNotification("Level published to Community successfully!");
                    FetchCommunityLevels();
                }
            }
        }

        private static string GetSteamAuthSessionTicket()
        {
            try
            {
                byte[] ticketBuffer = new byte[1024];
                uint ticketLength = 0;
                // SteamUser.GetAuthSessionTicket(ticketBuffer, 1024, out ticketLength);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < ticketLength; i++) sb.AppendFormat("{0:x2}", ticketBuffer[i]);
                return sb.Length > 0 ? sb.ToString() : "dev_steam_ticket_sample";
            }
            catch
            {
                return "dev_steam_ticket_sample";
            }
        }

        private static List<RemoteLevelItem> ParseLevelsJson(string json)
        {
            List<RemoteLevelItem> items = new List<RemoteLevelItem>();
            if (string.IsNullOrWhiteSpace(json) || json == "[]") return items;

            string[] records = json.Split(new string[] { "},{" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < records.Length; i++)
            {
                string r = records[i];
                var item = new RemoteLevelItem
                {
                    id = ExtractJsonString(r, "id"),
                    title = ExtractJsonString(r, "title"),
                    author = ExtractJsonString(r, "author"),
                    author_steam_id = ExtractJsonString(r, "author_steam_id"),
                    difficulty = ExtractJsonString(r, "difficulty"),
                    staging_scene = ExtractJsonString(r, "staging_scene"),
                    description = ExtractJsonString(r, "description"),
                    object_count = ExtractJsonInt(r, "object_count"),
                    downloads = ExtractJsonInt(r, "downloads")
                };
                if (!string.IsNullOrEmpty(item.id)) items.Add(item);
            }
            return items;
        }

        private static string ExtractJsonString(string block, string key)
        {
            string marker = $"\"{key}\":\"";
            int idx = block.IndexOf(marker);
            if (idx == -1) return "";
            int start = idx + marker.Length;
            int end = block.IndexOf("\"", start);
            return end != -1 ? block.Substring(start, end - start) : "";
        }

        private static int ExtractJsonInt(string block, string key)
        {
            string marker = $"\"{key}\":";
            int idx = block.IndexOf(marker);
            if (idx == -1) return 0;
            int start = idx + marker.Length;
            int end = block.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, start);
            if (end == -1) end = block.Length;
            int.TryParse(block.Substring(start, end - start).Trim(), out int val);
            return val;
        }
    }
}