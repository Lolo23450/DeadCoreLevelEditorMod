using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MelonLoader;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace DeadCoreEditor
{
    public static class MapAudioService
    {
        public class TrackItem
        {
            public string Id;
            public string DisplayName;
            public bool IsFMOD;
            public bool IsMusic;
            public float Duration;
        }

        public static readonly Dictionary<string, AudioClip> DiscoveredClips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, TrackItem> AvailableTracks = new Dictionary<string, TrackItem>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, AudioClip> CustomClips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<GameObject, AudioConfig> PlacedAudioConfigs = new Dictionary<GameObject, AudioConfig>();

        private static GameObject _audioHost = null;
        private static AudioSource _unityMusicSource = null;
        private static AudioSource _unityPreviewSource = null;

        public static string CurrentlyPreviewingTrackId = "";
        public static string CustomAudioDir => Path.Combine(Directory.GetCurrentDirectory(), "UserData", "CustomAudio");

        public static void EnsureAudioHost()
        {
            if (_audioHost != null) return;

            _audioHost = new GameObject("Studio_Audio_Host");
            GameObject.DontDestroyOnLoad(_audioHost);

            _unityMusicSource = _audioHost.AddComponent<AudioSource>();
            _unityMusicSource.playOnAwake = false;
            _unityMusicSource.loop = true;
            _unityMusicSource.spatialBlend = 0f;

            _unityPreviewSource = _audioHost.AddComponent<AudioSource>();
            _unityPreviewSource.playOnAwake = false;
            _unityPreviewSource.loop = false;
            _unityPreviewSource.spatialBlend = 0f;

            if (!Directory.Exists(CustomAudioDir))
            {
                try { Directory.CreateDirectory(CustomAudioDir); } catch { }
            }
        }

        public static void OpenCustomAudioFolder()
        {
            EnsureAudioHost();
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = CustomAudioDir,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Audio] Could not open folder: {ex.Message}");
            }
        }

        // =========================================================================
        // TRACK DISCOVERY
        // =========================================================================

        public static void ScanGameAudioClips(Action onComplete = null)
        {
            EnsureAudioHost();
            AvailableTracks.Clear();

            MelonLogger.Msg("==================================================");
            MelonLogger.Msg("[Audio Engine] Scanning DeadCore Native Music System...");

            // 1. Scan actual playable scene music and ambiance objects
            ScanDeadCoreSceneMusicObjects();

            // 2. Dump native IL2CPP signatures & fields
            InspectNativeMusicEngine();

            // 3. Scan custom audio folder
            ScanCustomAudioFolder(onComplete);

            MelonLogger.Msg($"[Audio Engine] Scan finished. Total clean tracks: {AvailableTracks.Count}");
            MelonLogger.Msg("==================================================");
        }

        private static void ScanDeadCoreSceneMusicObjects()
        {
            try
            {
                for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
                {
                    Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                    if (!scene.isLoaded) continue;

                    GameObject[] roots = scene.GetRootGameObjects();
                    for (int r = 0; r < roots.Length; r++)
                    {
                        if (roots[r] == null) continue;
                        Transform[] allTransforms = roots[r].GetComponentsInChildren<Transform>(true);
                        for (int t = 0; t < allTransforms.Length; t++)
                        {
                            GameObject go = allTransforms[t].gameObject;
                            string gName = go.name;
                            string gLow = gName.ToLowerInvariant();

                            // Filter out collider trigger boxes, master relays, silences, and impostors
                            if (gLow.Contains("trigger") ||
                                gLow.Contains("master") ||
                                gLow.Contains("empty") ||
                                gLow.Contains("impostor") ||
                                gLow.Contains("boost") ||
                                gLow.Contains("gauge") ||
                                gLow.Contains("channel") ||
                                gLow.Contains("settings") ||
                                gLow.Contains("endlevel"))
                            {
                                continue;
                            }

                            // Music tracks
                            if (gName.StartsWith("Music_TrackList_", StringComparison.OrdinalIgnoreCase))
                            {
                                string label = gName.Replace("Music_TrackList_", "").Replace("_", " ").Trim();
                                RegisterTrack(gName, $"[OST] {label}", true, true);
                            }
                            // Ambient soundscapes
                            else if (gName.StartsWith("Ambiance", StringComparison.OrdinalIgnoreCase) ||
                                     gName.StartsWith("SoundLoop", StringComparison.OrdinalIgnoreCase))
                            {
                                string label = gName.Replace("Ambiance_", "").Replace("Ambiance", "").Replace("_", " ").Trim();
                                if (string.IsNullOrEmpty(label)) label = "Tower Atmosphere";
                                RegisterTrack(gName, $"[AMB] {label}", true, false);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Audio Engine] Scene scan error: {ex.Message}");
            }
        }

        private static void InspectNativeMusicEngine()
        {
            try
            {
                MelonLogger.Msg("--------------------------------------------------");

                // 1. Inspect MusicManagerScript native IL2CPP methods & fields
                GameObject mmGo = GameObject.Find("MusicManager");
                if (mmGo != null)
                {
                    Component mm = GetIl2CppComponent(mmGo, "MusicManagerScript");
                    if (mm != null)
                    {
                        var il2Type = mm.GetIl2CppType();
                        MelonLogger.Msg($"[MusicManager Native API] '{mmGo.name}':");

                        var methods = il2Type.GetMethods(Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.Instance);
                        for (int m = 0; m < methods.Length; m++)
                        {
                            string mName = methods[m].Name;
                            if (!mName.StartsWith("get_") && !mName.StartsWith("set_") && !mName.StartsWith("op_"))
                            {
                                var pars = methods[m].GetParameters();
                                string pStr = "";
                                for (int p = 0; p < pars.Length; p++) pStr += $"{pars[p].ParameterType.Name} {pars[p].Name}, ";
                                MelonLogger.Msg($"   • Method: {mName}({pStr.TrimEnd(' ', ',')})");
                            }
                        }

                        var fields = il2Type.GetFields(Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance);
                        for (int f = 0; f < fields.Length; f++)
                        {
                            try
                            {
                                var val = fields[f].GetValue(mm);
                                MelonLogger.Msg($"   • Field: {fields[f].FieldType.Name} {fields[f].Name} = {val}");
                            }
                            catch { }
                        }
                    }
                }

                // 2. Inspect MusicTransformerReceiver native IL2CPP methods & fields
                GameObject track01 = GameObject.Find("Music_TrackList_01");
                if (track01 != null)
                {
                    Component receiver = GetIl2CppComponent(track01, "MusicTransformerReceiver");
                    if (receiver != null)
                    {
                        var il2Type = receiver.GetIl2CppType();
                        MelonLogger.Msg($"[MusicTransformer Native API] '{track01.name}':");

                        var methods = il2Type.GetMethods(Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.Instance);
                        for (int m = 0; m < methods.Length; m++)
                        {
                            string mName = methods[m].Name;
                            if (!mName.StartsWith("get_") && !mName.StartsWith("set_") && !mName.StartsWith("op_"))
                            {
                                var pars = methods[m].GetParameters();
                                string pStr = "";
                                for (int p = 0; p < pars.Length; p++) pStr += $"{pars[p].ParameterType.Name} {pars[p].Name}, ";
                                MelonLogger.Msg($"   • Method: {mName}({pStr.TrimEnd(' ', ',')})");
                            }
                        }

                        var fields = il2Type.GetFields(Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance);
                        for (int f = 0; f < fields.Length; f++)
                        {
                            try
                            {
                                var val = fields[f].GetValue(receiver);
                                MelonLogger.Msg($"   • Field: {fields[f].FieldType.Name} {fields[f].Name} = {val}");
                            }
                            catch { }
                        }
                    }
                }

                MelonLogger.Msg("--------------------------------------------------");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Music Engine Diagnostics] {ex.Message}");
            }
        }

        private static void RegisterTrack(string id, string displayName, bool isFmod, bool isMusic)
        {
            if (AvailableTracks.ContainsKey(id)) return;

            AvailableTracks[id] = new TrackItem
            {
                Id = id,
                DisplayName = displayName,
                IsFMOD = isFmod,
                IsMusic = isMusic,
                Duration = 0f
            };
            MelonLogger.Msg($"   • Playable: '{displayName}' ({id})");
        }

        public static void RegisterClip(AudioClip clip, string source = "")
        {
            if (clip == null || string.IsNullOrWhiteSpace(clip.name)) return;
            string name = clip.name.Trim();

            if (!DiscoveredClips.ContainsKey(name)) DiscoveredClips[name] = clip;
            if (!CustomClips.ContainsKey(name)) CustomClips[name] = clip;

            RegisterTrack(name, name, false, clip.length >= 15f);
        }

        private static void ScanCustomAudioFolder(Action onComplete)
        {
            if (Directory.Exists(CustomAudioDir))
            {
                string[] files = Directory.GetFiles(CustomAudioDir, "*.*", SearchOption.AllDirectories);
                List<string> validFiles = new List<string>();
                for (int i = 0; i < files.Length; i++)
                {
                    string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                    if (ext == ".ogg" || ext == ".wav") validFiles.Add(files[i]);
                }

                if (validFiles.Count > 0)
                {
                    MelonCoroutines.Start(LoadCustomAudioCoroutine(validFiles, onComplete));
                    return;
                }
            }
            onComplete?.Invoke();
        }

        private static IEnumerator LoadCustomAudioCoroutine(List<string> files, Action onComplete)
        {
            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];
                string name = Path.GetFileNameWithoutExtension(file);
                string trackId = "[Custom] " + name;

                if (CustomClips.ContainsKey(trackId)) continue;

                AudioType aType = file.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? AudioType.OGGVORBIS : AudioType.WAV;
                UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip("file://" + file, aType);
                try
                {
                    yield return req.SendWebRequest();

                    if (string.IsNullOrEmpty(req.error))
                    {
                        AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                        if (clip != null)
                        {
                            clip.name = trackId;
                            CustomClips[trackId] = clip;
                            DiscoveredClips[trackId] = clip;
                            RegisterTrack(trackId, "[Custom] " + name, false, clip.length >= 15f);
                        }
                    }
                }
                finally
                {
                    req.Dispose();
                }
            }
            onComplete?.Invoke();
        }

        // =========================================================================
        // PLAYBACK ENGINE
        // =========================================================================

        public static void ApplyAudioConfig(GameObject controllerObj, AudioConfig cfg)
        {
            if (controllerObj == null || cfg == null) return;
            EnsureAudioHost();

            PlacedAudioConfigs[controllerObj] = cfg.Clone();
            bool shouldPlay = !EditorSessionManager.IsEditModeActive || cfg.PlayInEditMode;

            if (shouldPlay && !string.IsNullOrEmpty(cfg.MusicTrackName))
            {
                PlayTrack(cfg.MusicTrackName, cfg.MusicVolume);
            }
            else
            {
                StopAllAudio();
            }
        }

        private static void PlayTrack(string trackId, float volume)
        {
            if (string.IsNullOrEmpty(trackId)) return;

            // 1. Try DeadCore Native Music / Scene Emitter Hook
            if (TryPlaySpecificDeadCoreTrack(trackId, volume))
            {
                return;
            }

            // 2. Custom Audio File
            if (CustomClips.TryGetValue(trackId, out AudioClip clip) && clip != null)
            {
                StopAllAudio();
                _unityMusicSource.clip = clip;
                _unityMusicSource.volume = volume;
                _unityMusicSource.Play();
            }
        }

        public static void StopAllAudio()
        {
            StopDeadCoreMusic();
            if (_unityMusicSource != null) _unityMusicSource.Stop();
        }

        public static void StopLevelAudio() => StopAllAudio();

        public static void PreviewTrack(string trackId)
        {
            EnsureAudioHost();

            if (CurrentlyPreviewingTrackId == trackId)
            {
                StopPreview();
                return;
            }

            StopPreview();
            CurrentlyPreviewingTrackId = trackId;

            MelonLogger.Msg($"[Audio Preview] Starting preview for '{trackId}'...");

            // 1. Play Specific DeadCore Native Track
            if (TryPlaySpecificDeadCoreTrack(trackId, 0.9f))
            {
                return;
            }

            // 2. Custom Audio Clip
            if (CustomClips.TryGetValue(trackId, out AudioClip clip) && clip != null)
            {
                _unityPreviewSource.clip = clip;
                _unityPreviewSource.volume = 0.85f;
                _unityPreviewSource.Play();
                MelonLogger.Msg($"[Audio Preview] Playing Custom File: '{trackId}'");
            }
        }

        public static void StopPreview()
        {
            StopDeadCoreMusic();
            if (_unityPreviewSource != null) _unityPreviewSource.Stop();
            CurrentlyPreviewingTrackId = "";
        }

        public static bool IsPreviewPlaying(string trackId)
        {
            return CurrentlyPreviewingTrackId == trackId;
        }

        // =========================================================================
        // TARGETED DEADCORE TRACKLIST INVOCATION (IL2CPP-SAFE)
        // =========================================================================

        private static bool TryPlaySpecificDeadCoreTrack(string trackName, float volume)
        {
            // 1. Buscar el GameObject en la escena (incluyendo objetos inactivos)
            GameObject targetGo = GameObject.Find(trackName);
            if (targetGo == null)
            {
                var allGos = Resources.FindObjectsOfTypeAll(Il2CppType.Of<GameObject>());
                for (int i = 0; i < allGos.Length; i++)
                {
                    var go = allGos[i]?.TryCast<GameObject>();
                    if (go != null && go.name.Equals(trackName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetGo = go;
                        break;
                    }
                }
            }

            // A. FMOD_StudioEventEmitter (Ambiance loops)
            if (targetGo != null)
            {
                Component emitter = GetIl2CppComponent(targetGo, "FMOD_StudioEventEmitter");
                if (emitter != null)
                {
                    try
                    {
                        targetGo.SetActive(true);
                        AttachObjectToCamera(targetGo);

                        InvokeIl2CppMethod(emitter, "Play");
                        MelonLogger.Msg($"[Audio Engine] Triggered FMOD_StudioEventEmitter.Play() on '{targetGo.name}'");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[Audio Engine] Emitter play error: {ex.Message}");
                    }
                }
            }

            // B. MusicManagerScript Native Tracklist Control
            GameObject mmGo = GameObject.Find("MusicManager");
            if (mmGo == null)
            {
                var allGos = Resources.FindObjectsOfTypeAll(Il2CppType.Of<GameObject>());
                for (int i = 0; i < allGos.Length; i++)
                {
                    var go = allGos[i]?.TryCast<GameObject>();
                    if (go != null && go.name.Equals("MusicManager", StringComparison.OrdinalIgnoreCase))
                    {
                        mmGo = go;
                        break;
                    }
                }
            }

            if (mmGo != null)
            {
                Component mm = GetIl2CppComponent(mmGo, "MusicManagerScript");
                if (mm != null)
                {
                    try
                    {
                        // 1. Desactivar el modo Speedrun shuffle
                        var mmScript = mm.TryCast<MusicManagerScript>();
                        if (mmScript != null)
                        {
                            mmScript._isSpeedRunTracks = false;
                        }

                        // 2. Localizar la referencia de MusicTrackList
                        Il2CppSystem.Object targetTrackList = null;

                        // 2a. Directamente desde el componente en targetGo
                        if (targetGo != null)
                        {
                            Component mtlComp = GetIl2CppComponent(targetGo, "MusicTrackList");
                            if (mtlComp != null) targetTrackList = mtlComp;

                            // 2b. O a traves de los campos de MusicTransformerReceiver
                            if (targetTrackList == null)
                            {
                                Component rec = GetIl2CppComponent(targetGo, "MusicTransformerReceiver");
                                if (rec != null)
                                {
                                    var rFields = rec.GetIl2CppType().GetFields(Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Instance);
                                    for (int f = 0; f < rFields.Length; f++)
                                    {
                                        if (rFields[f].FieldType.Name.IndexOf("track", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            rFields[f].Name.IndexOf("track", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            targetTrackList = rFields[f].GetValue(rec);
                                            if (targetTrackList != null) break;
                                        }
                                    }
                                }
                            }
                        }

                        // 2c. Busqueda profunda en Resources si el objeto de la escena estaba inaccesible
                        if (targetTrackList == null)
                        {
                            var allComps = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Component>());
                            for (int i = 0; i < allComps.Length; i++)
                            {
                                var c = allComps[i]?.TryCast<Component>();
                                if (c != null && c.GetIl2CppType().Name.Equals("MusicTrackList", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (c.gameObject != null && c.gameObject.name.Equals(trackName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        targetTrackList = c;
                                        break;
                                    }
                                }
                            }
                        }

                        // 3. Invocar ChangeTracklist asegurando parametros validos
                        if (targetTrackList != null)
                        {
                            InvokeIl2CppMethod(mm, "ChangeTracklist", targetTrackList);
                            MelonLogger.Msg($"[Audio Engine] Successfully invoked ChangeTracklist({targetTrackList}) for '{trackName}'!");
                            return true;
                        }
                        else
                        {
                            MelonLogger.Warning($"[Audio Engine] Could not locate 'MusicTrackList' reference for '{trackName}'. Skipping invocation to avoid parameter mismatch.");
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[Audio Engine] Track switch error: {ex.Message}");
                    }
                }
            }

            return false;
        }

        public static object InvokeIl2CppMethod(Il2CppSystem.Object target, string methodName, params Il2CppSystem.Object[] args)
        {
            if (target == null) return null;
            try
            {
                var il2Type = target.GetIl2CppType();
                var methods = il2Type.GetMethods(
                    Il2CppSystem.Reflection.BindingFlags.Public |
                    Il2CppSystem.Reflection.BindingFlags.NonPublic |
                    Il2CppSystem.Reflection.BindingFlags.Instance |
                    Il2CppSystem.Reflection.BindingFlags.Static);

                int passedArgCount = (args != null) ? args.Length : 0;
                Il2CppSystem.Reflection.MethodInfo bestMethod = null;
                Il2CppSystem.Reflection.ParameterInfo[] bestParams = null;

                // 1. Buscar coincidencia exacta por cantidad de parametros
                for (int i = 0; i < methods.Length; i++)
                {
                    var m = methods[i];
                    if (m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
                    {
                        var p = m.GetParameters();
                        if (p.Length == passedArgCount)
                        {
                            bestMethod = m;
                            bestParams = p;
                            break;
                        }
                    }
                }

                // 2. Si no coincide exactamente, tomar la primera sobrecarga disponible para rellenar parametros
                if (bestMethod == null)
                {
                    for (int i = 0; i < methods.Length; i++)
                    {
                        var m = methods[i];
                        if (m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
                        {
                            bestMethod = m;
                            bestParams = m.GetParameters();
                            break;
                        }
                    }
                }

                if (bestMethod == null)
                {
                    MelonLogger.Warning($"[IL2CPP Invoke] Method '{methodName}' not found on '{il2Type.Name}'.");
                    return null;
                }

                int expectedCount = (bestParams != null) ? bestParams.Length : 0;
                if (expectedCount == 0)
                {
                    return bestMethod.Invoke(target, null);
                }

                // Construir el array con la cantidad exacta esperada por la firma nativa
                var paramArray = new Il2CppReferenceArray<Il2CppSystem.Object>(expectedCount);
                for (int i = 0; i < expectedCount; i++)
                {
                    if (args != null && i < args.Length && args[i] != null)
                    {
                        paramArray[i] = args[i];
                    }
                    else
                    {
                        // Autocompletar valores por defecto para argumentos faltantes (ej: force = true, fade = 1.0f)
                        string pTypeName = bestParams[i].ParameterType.Name.ToLowerInvariant();
                        if (pTypeName.Contains("bool"))
                        {
                            paramArray[i] = BoxBoolean(true);
                        }
                        else if (pTypeName.Contains("single") || pTypeName.Contains("float"))
                        {
                            paramArray[i] = BoxSingle(1.0f);
                        }
                        else if (pTypeName.Contains("int"))
                        {
                            paramArray[i] = BoxInt32(0);
                        }
                        else
                        {
                            paramArray[i] = null;
                        }
                    }
                }

                return bestMethod.Invoke(target, paramArray);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[IL2CPP Invoke Error on {methodName}] {ex.Message}");
                return null;
            }
        }

        private static Il2CppSystem.Object BoxBoolean(bool val)
        {
            IntPtr mem = Marshal.AllocHGlobal(1);
            try
            {
                Marshal.WriteByte(mem, (byte)(val ? 1 : 0));
                IntPtr ptr = IL2CPP.il2cpp_value_box(Il2CppClassPointerStore<bool>.NativeClassPtr, mem);
                return new Il2CppSystem.Object(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(mem);
            }
        }

        private static Il2CppSystem.Object BoxSingle(float val)
        {
            IntPtr mem = Marshal.AllocHGlobal(4);
            try
            {
                byte[] bytes = BitConverter.GetBytes(val);
                Marshal.Copy(bytes, 0, mem, 4);
                IntPtr ptr = IL2CPP.il2cpp_value_box(Il2CppClassPointerStore<float>.NativeClassPtr, mem);
                return new Il2CppSystem.Object(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(mem);
            }
        }

        private static Il2CppSystem.Object BoxInt32(int val)
        {
            IntPtr mem = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(mem, val);
                IntPtr ptr = IL2CPP.il2cpp_value_box(Il2CppClassPointerStore<int>.NativeClassPtr, mem);
                return new Il2CppSystem.Object(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(mem);
            }
        }

        private static void StopDeadCoreMusic()
        {
            GameObject mmGo = GameObject.Find("MusicManager");
            if (mmGo != null)
            {
                Component mm = GetIl2CppComponent(mmGo, "MusicManagerScript");
                if (mm != null)
                {
                    try { InvokeIl2CppMethod(mm, "Stop"); } catch { }
                }
            }

            try
            {
                var comps = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Component>());
                if (comps == null) return;

                for (int i = 0; i < comps.Length; i++)
                {
                    Component comp = comps[i]?.TryCast<Component>();
                    if (comp != null && comp.GetIl2CppType().Name == "FMOD_StudioEventEmitter")
                    {
                        InvokeIl2CppMethod(comp, "Stop");
                    }
                }
            }
            catch { }
        }

        private static void AttachObjectToCamera(GameObject go)
        {
            try
            {
                Camera cam = Camera.main ?? GameObject.FindObjectOfType<Camera>();
                if (cam != null && go != null)
                {
                    go.transform.position = cam.transform.position;
                }
            }
            catch { }
        }

        public static Component GetIl2CppComponent(GameObject go, string typeName)
        {
            if (go == null) return null;
            var comps = go.GetComponents<Component>();
            if (comps == null) return null;

            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] != null && comps[i].GetIl2CppType().Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                    return comps[i];
            }
            return null;
        }

        public static AudioClip GetClip(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (CustomClips.TryGetValue(name.Trim(), out AudioClip clip)) return clip;
            if (DiscoveredClips.TryGetValue(name.Trim(), out clip)) return clip;
            return null;
        }

        public static void RefreshPlaybackForActiveMode()
        {
            foreach (var kvp in PlacedAudioConfigs)
            {
                if (kvp.Key != null && kvp.Key.activeInHierarchy)
                {
                    ApplyAudioConfig(kvp.Key, kvp.Value);
                    return;
                }
            }
            StopAllAudio();
        }
    }
}