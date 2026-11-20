using System;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;

namespace DeadCoreEditor
{
    // =========================================================================
    // TRIGGER "MODIFY VALUE" ACTION: smoothly changes values on music, skybox,
    // sunlight, lights, neons, gameplay objects and transforms (pos/rot/scale).
    // Everything touched is restored automatically when the playtest ends.
    // =========================================================================

    public enum ModifyTarget
    {
        Transform = 0,   // position / rotation / scale of named objects
        Music = 1,       // map audio controller - music lane
        Ambient = 2,     // map audio controller - ambience lane
        Skybox = 3,      // skybox controller
        Sunlight = 4,    // directional light(s); falls back to the native scene sun
        Light = 5,       // spot / point lights
        Neon = 6,        // neon glow objects
        Gravity = 7,     // gravity areas
        Turbine = 8,
        Jumper = 9,
        Turret = 10,
        Laser = 11,      // rotating lasers
        MotionPath = 12  // moving platforms
    }

    public enum ModifyOp { Set = 0, Add = 1, Multiply = 2 }

    public static class TriggerModifyService
    {
        // ---------------------------------------------------------------------
        // Property tables (used by the inspector UI and the serializer clamp)
        // ---------------------------------------------------------------------
        public static string[] PropertyNames(ModifyTarget t)
        {
            switch (t)
            {
                case ModifyTarget.Transform: return new[] { "Position", "Rotation", "Scale" };
                case ModifyTarget.Music: return new[] { "Volume", "Pitch" };
                case ModifyTarget.Ambient: return new[] { "Volume", "Pitch" };
                case ModifyTarget.Skybox: return new[] { "Exposure", "Tint", "Spin Speed", "Yaw" };
                case ModifyTarget.Sunlight:
                case ModifyTarget.Light: return new[] { "Intensity", "Color", "Range", "Spot Angle", "Volumetric" };
                case ModifyTarget.Neon: return new[] { "Intensity", "Color", "Frequency" };
                case ModifyTarget.Gravity: return new[] { "Force" };
                case ModifyTarget.Turbine: return new[] { "Speed" };
                case ModifyTarget.Jumper: return new[] { "Force" };
                case ModifyTarget.Turret: return new[] { "Fire Delay" };
                case ModifyTarget.Laser: return new[] { "Rotation Speed" };
                case ModifyTarget.MotionPath: return new[] { "Speed", "Rotation Speed" };
            }
            return new[] { "Value" };
        }

        public static bool IsColorProperty(ModifyTarget t, int prop)
        {
            if (t == ModifyTarget.Skybox) return prop == 1;
            if (t == ModifyTarget.Sunlight || t == ModifyTarget.Light) return prop == 1;
            if (t == ModifyTarget.Neon) return prop == 1;
            return false;
        }

        public static bool IsVectorProperty(ModifyTarget t) => t == ModifyTarget.Transform;

        // ---------------------------------------------------------------------
        // Runtime state
        // ---------------------------------------------------------------------
        private class Mod
        {
            public string Key;
            public Action<float> Blend;   // Blend(0) = value before the trigger, Blend(1) = target value
            public float T;
            public float Dir;             // +1 forward, -1 reverting, 0 idle
            public float Duration;
        }

        private static readonly List<Mod> _running = new List<Mod>();
        private static readonly Dictionary<GameObject, List<Mod>> _byVolume = new Dictionary<GameObject, List<Mod>>();
        private static readonly Dictionary<string, Mod> _originals = new Dictionary<string, Mod>();

        public static void ResetAll()
        {
            // Put everything back exactly how it was before the first trigger touched it.
            foreach (var kv in _originals)
            {
                try { kv.Value.Blend(0f); } catch { }
            }
            _originals.Clear();
            _running.Clear();
            _byVolume.Clear();
        }

        public static void ForgetVolume(GameObject vol)
        {
            if (vol != null) _byVolume.Remove(vol);
        }

        public static void Tick(float unscaledDt)
        {
            if (_running.Count == 0) return;
            for (int i = _running.Count - 1; i >= 0; i--)
            {
                Mod m = _running[i];
                float dur = Mathf.Max(0.0001f, m.Duration);
                m.T = Mathf.Clamp01(m.T + m.Dir * (unscaledDt / dur));
                try { m.Blend(Smooth(m.T)); }
                catch (Exception ex) { MelonLogger.Warning("[Trigger/Modify] " + ex.Message); m.Dir = 0f; }

                if ((m.Dir > 0f && m.T >= 1f) || (m.Dir < 0f && m.T <= 0f))
                {
                    m.Dir = 0f;
                    _running.RemoveAt(i);
                }
            }
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        // ---------------------------------------------------------------------
        // Fire / Revert
        // ---------------------------------------------------------------------
        public static void Fire(GameObject vol, TriggerVolumeConfig cfg)
        {
            var mods = Build(vol, cfg);
            if (mods.Count == 0)
            {
                MelonLogger.Warning("[Trigger/Modify] Nothing matched target '" + cfg.ModTarget + "' filter '" + cfg.TargetName + "'.");
                return;
            }

            if (!_byVolume.TryGetValue(vol, out var list)) { list = new List<Mod>(); _byVolume[vol] = list; }

            for (int i = 0; i < mods.Count; i++)
            {
                Mod m = mods[i];
                m.Duration = cfg.Duration;
                if (!_originals.ContainsKey(m.Key)) _originals[m.Key] = m;

                // A newer modifier on the same property replaces an older still-running one.
                for (int r = _running.Count - 1; r >= 0; r--)
                    if (_running[r].Key == m.Key) _running.RemoveAt(r);

                list.Add(m);
                if (cfg.Duration <= 0.01f)
                {
                    m.T = 1f; m.Dir = 0f;
                    try { m.Blend(1f); } catch (Exception ex) { MelonLogger.Warning("[Trigger/Modify] " + ex.Message); }
                }
                else
                {
                    m.T = 0f; m.Dir = 1f;
                    _running.Add(m);
                }
            }
        }

        public static void Revert(GameObject vol, float duration)
        {
            if (vol == null || !_byVolume.TryGetValue(vol, out var list) || list.Count == 0) return;
            // Latest first so the earliest captured start value wins on stacked modifiers.
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Mod m = list[i];
                m.Duration = duration;
                if (duration <= 0.01f)
                {
                    m.T = 0f; m.Dir = 0f; _running.Remove(m);
                    try { m.Blend(0f); } catch { }
                }
                else
                {
                    m.Dir = -1f;
                    if (!_running.Contains(m)) _running.Add(m);
                }
            }
            list.Clear();
        }

        // ---------------------------------------------------------------------
        // Modifier construction
        // ---------------------------------------------------------------------
        private static List<Mod> Build(GameObject vol, TriggerVolumeConfig cfg)
        {
            var result = new List<Mod>();
            ModifyTarget target = cfg.ModTarget;
            int prop = Mathf.Clamp(cfg.ModProp, 0, PropertyNames(target).Length - 1);
            ModifyOp op = cfg.ModOp;
            string filter = cfg.TargetName ?? "";
            float val = cfg.ModValue;
            Color col = cfg.ModColor;
            Vector3 vec = cfg.ModVector;

            switch (target)
            {
                case ModifyTarget.Transform:
                {
                    if (string.IsNullOrWhiteSpace(filter)) break; // transforms need an explicit name
                    foreach (var go in Matching(EditorSessionManager.PlacedObjects, filter))
                    {
                        Mod m = prop == 0 ? MakePosition(go, op, vec)
                              : prop == 1 ? MakeRotation(go, op, vec)
                              : MakeScale(go, op, vec);
                        if (m != null) result.Add(m);
                    }
                    break;
                }

                case ModifyTarget.Music:
                case ModifyTarget.Ambient:
                {
                    bool amb = target == ModifyTarget.Ambient;
                    foreach (var go in Matching(new List<GameObject>(MapAudioService.PlacedAudioConfigs.Keys), filter))
                    {
                        GameObject g = go;
                        Func<AudioConfig> fresh = () => MapAudioService.PlacedAudioConfigs.TryGetValue(g, out var c) ? c : null;
                        var c0 = fresh(); if (c0 == null) continue;

                        if (prop == 0)
                            result.Add(MakeFloat(Key(target, prop, g),
                                () => { var c = fresh(); return c == null ? 0f : (amb ? c.AmbientVolume : c.MusicVolume); },
                                v => { var c = fresh(); if (c == null) return; v = Mathf.Clamp01(v); if (amb) c.AmbientVolume = v; else c.MusicVolume = v; MapAudioService.ApplyAudioConfig(g, c); },
                                op, val));
                        else
                            result.Add(MakeFloat(Key(target, prop, g),
                                () => { var c = fresh(); return c == null ? 1f : (amb ? c.AmbientPitch : c.MusicPitch); },
                                v => { var c = fresh(); if (c == null) return; v = Mathf.Clamp(v, 0.25f, 4f); if (amb) c.AmbientPitch = v; else c.MusicPitch = v; MapAudioService.ApplyAudioConfig(g, c); },
                                op, val));
                    }
                    break;
                }

                case ModifyTarget.Skybox:
                {
                    string k = Key(target, prop, null);
                    if (prop == 0)
                        result.Add(MakeFloat(k, () => SkyboxControllerService.ActiveConfig.Exposure,
                            v => { SkyboxControllerService.ActiveConfig.Exposure = Mathf.Max(0.01f, v); SkyboxControllerService.ApplyProperties(); }, op, val));
                    else if (prop == 1)
                        result.Add(MakeColor(k, () => SkyboxControllerService.ActiveConfig.TintColor,
                            c => { SkyboxControllerService.ActiveConfig.TintColor = c; SkyboxControllerService.ApplyProperties(); }, op, col));
                    else if (prop == 2)
                        result.Add(MakeFloat(k, () => SkyboxControllerService.ActiveConfig.SpinSpeed,
                            v => { SkyboxControllerService.ActiveConfig.SpinSpeed = v; }, op, val));
                    else
                    {
                        float start = SkyboxControllerService.ActiveConfig.CurrentAngle;
                        float end = op == ModifyOp.Set ? val : op == ModifyOp.Add ? start + val : start * val;
                        bool shortest = op == ModifyOp.Set;
                        result.Add(new Mod
                        {
                            Key = k,
                            Blend = t =>
                            {
                                SkyboxControllerService.ActiveConfig.CurrentAngle = shortest ? Mathf.LerpAngle(start, end, t) : Mathf.LerpUnclamped(start, end, t);
                                SkyboxControllerService.ApplyProperties();
                            }
                        });
                    }
                    break;
                }

                case ModifyTarget.Sunlight:
                case ModifyTarget.Light:
                {
                    bool wantSun = target == ModifyTarget.Sunlight;
                    var candidates = new List<GameObject>();
                    foreach (var kv in EditorSessionManager.PlacedLights)
                    {
                        if (kv.Key == null || kv.Value == null) continue;
                        bool isSun = kv.Value.Kind == LightKind.Directional || kv.Value.IsDirectional;
                        if (isSun == wantSun) candidates.Add(kv.Key);
                    }

                    bool any = false;
                    foreach (var go in Matching(candidates, filter))
                    {
                        any = true;
                        GameObject g = go;
                        Func<LightConfig> fresh = () => EditorSessionManager.PlacedLights.TryGetValue(g, out var c) ? c : null;
                        if (fresh() == null) continue;
                        string k = Key(target, prop, g);

                        if (prop == 1)
                            result.Add(MakeColor(k, () => fresh().Color,
                                c => { var l = fresh(); if (l == null) return; l.Color = c; EditorSessionManager.ApplyLightConfig(g, l); }, op, col));
                        else
                            result.Add(MakeFloat(k,
                                () => { var l = fresh(); if (l == null) return 0f; return prop == 0 ? l.Intensity : prop == 2 ? l.Range : prop == 3 ? l.SpotAngle : l.VolumetricIntensity; },
                                v =>
                                {
                                    var l = fresh(); if (l == null) return;
                                    if (prop == 0) l.Intensity = Mathf.Max(0f, v);
                                    else if (prop == 2) l.Range = Mathf.Max(0.1f, v);
                                    else if (prop == 3) l.SpotAngle = Mathf.Clamp(v, 1f, 179f);
                                    else l.VolumetricIntensity = Mathf.Max(0f, v);
                                    EditorSessionManager.ApplyLightConfig(g, l);
                                }, op, val));
                    }

                    // No custom sun placed (or named): drive the level's native sun directly.
                    Light native = SceneHarvestingService.NativeSceneSun;
                    if (wantSun && !any && string.IsNullOrWhiteSpace(filter) && native != null && (prop == 0 || prop == 1))
                    {
                        string k = Key(target, prop, native.gameObject);
                        if (prop == 0)
                            result.Add(MakeFloat(k, () => native.intensity / 8000f, v => { if (native != null) native.intensity = Mathf.Max(0f, v) * 8000f; }, op, val));
                        else
                            result.Add(MakeColor(k, () => native.color, c => { if (native != null) native.color = c; }, op, col));
                    }
                    break;
                }

                case ModifyTarget.Neon:
                {
                    foreach (var go in Matching(new List<GameObject>(EditorSessionManager.PlacedNeonConfigs.Keys), filter))
                    {
                        GameObject g = go;
                        Func<NeonConfig> fresh = () => EditorSessionManager.PlacedNeonConfigs.TryGetValue(g, out var c) ? c : null;
                        if (fresh() == null) continue;
                        string k = Key(target, prop, g);

                        if (prop == 1)
                            result.Add(MakeColor(k, () => fresh().Color,
                                c => { var n = fresh(); if (n == null) return; n.Color = c; EditorSessionManager.ApplyNeonConfig(g, n); }, op, col));
                        else
                            result.Add(MakeFloat(k,
                                () => { var n = fresh(); return n == null ? 0f : (prop == 0 ? n.Intensity : n.Frequency); },
                                v =>
                                {
                                    var n = fresh(); if (n == null) return;
                                    if (prop == 0) n.Intensity = Mathf.Max(0f, v); else n.Frequency = Mathf.Clamp(v, 0.1f, 25f);
                                    EditorSessionManager.ApplyNeonConfig(g, n);
                                }, op, val));
                    }
                    break;
                }

                case ModifyTarget.Gravity:
                {
                    foreach (var go in Matching(new List<GameObject>(GravityAreaService.PlacedGravityConfigs.Keys), filter))
                    {
                        GameObject g = go;
                        Func<GravityConfig> fresh = () => GravityAreaService.PlacedGravityConfigs.TryGetValue(g, out var c) ? c : null;
                        if (fresh() == null) continue;
                        result.Add(MakeFloat(Key(target, prop, g), () => fresh().GravityForce,
                            v => { var c = fresh(); if (c == null) return; c.GravityForce = v; GravityAreaService.ApplyGravityConfig(g, c); }, op, val));
                    }
                    break;
                }

                case ModifyTarget.Turbine:
                    foreach (var go in Matching(new List<GameObject>(EditorSessionManager.TurbineSpeeds.Keys), filter))
                    {
                        GameObject g = go;
                        result.Add(MakeFloat(Key(target, prop, g), () => EditorSessionManager.TurbineSpeeds.TryGetValue(g, out var s) ? s : 0f,
                            v => EditorSessionManager.ApplyTurbineSpeed(g, v), op, val));
                    }
                    break;

                case ModifyTarget.Jumper:
                    foreach (var go in Matching(new List<GameObject>(EditorSessionManager.JumperForces.Keys), filter))
                    {
                        GameObject g = go;
                        result.Add(MakeFloat(Key(target, prop, g), () => EditorSessionManager.JumperForces.TryGetValue(g, out var f) ? f : 0f,
                            v => EditorSessionManager.ApplyJumperForce(g, v), op, val));
                    }
                    break;

                case ModifyTarget.Turret:
                    foreach (var go in Matching(new List<GameObject>(EditorSessionManager.TurretFireDelays.Keys), filter))
                    {
                        GameObject g = go;
                        result.Add(MakeFloat(Key(target, prop, g), () => EditorSessionManager.TurretFireDelays.TryGetValue(g, out var d) ? d : 1f,
                            v =>
                            {
                                float power = 1500f;
                                if (EditorSessionManager.EntityRegistry.TryGetValue(g, out var data) && data.TryGetComponent<TurretConfig>(out var tcfg)) power = tcfg.FirePower;
                                EditorSessionManager.ApplyTurretSettings(g, Mathf.Max(0.05f, v), power);
                            }, op, val));
                    }
                    break;

                case ModifyTarget.Laser:
                    foreach (var go in Matching(new List<GameObject>(EditorSessionManager.LaserRotationSpeeds.Keys), filter))
                    {
                        GameObject g = go;
                        result.Add(MakeFloat(Key(target, prop, g), () => EditorSessionManager.LaserRotationSpeeds.TryGetValue(g, out var s) ? s : 0f,
                            v => EditorSessionManager.LaserRotationSpeeds[g] = v, op, val));
                    }
                    break;

                case ModifyTarget.MotionPath:
                    foreach (var go in Matching(new List<GameObject>(EditorSessionManager.MotionPaths.Keys), filter))
                    {
                        GameObject g = go;
                        Func<ObjectMotionPath> fresh = () => EditorSessionManager.MotionPaths.TryGetValue(g, out var p) ? p : null;
                        if (fresh() == null) continue;
                        result.Add(MakeFloat(Key(target, prop, g),
                            () => { var p = fresh(); return p == null ? 0f : (prop == 0 ? p.Speed : p.RotationSpeed); },
                            v => { var p = fresh(); if (p == null) return; if (prop == 0) p.Speed = Mathf.Max(0.1f, v); else p.RotationSpeed = v; }, op, val));
                    }
                    break;
            }
            return result;
        }

        // ---------------------------------------------------------------------
        // Typed modifier factories
        // ---------------------------------------------------------------------
        private static Mod MakeFloat(string key, Func<float> get, Action<float> set, ModifyOp op, float value)
        {
            float start = get();
            float end = op == ModifyOp.Set ? value : op == ModifyOp.Add ? start + value : start * value;
            return new Mod { Key = key, Blend = t => set(Mathf.LerpUnclamped(start, end, t)) };
        }

        private static Mod MakeColor(string key, Func<Color> get, Action<Color> set, ModifyOp op, Color value)
        {
            Color start = get();
            Color end = op == ModifyOp.Set ? value : op == ModifyOp.Add ? start + value : start * value;
            return new Mod { Key = key, Blend = t => set(Color.LerpUnclamped(start, end, t)) };
        }

        private static Mod MakePosition(GameObject go, ModifyOp op, Vector3 v)
        {
            Vector3 start = go.transform.position;
            Vector3 end = op == ModifyOp.Set ? v : op == ModifyOp.Add ? start + v : Vector3.Scale(start, v);
            Vector3 last = start;
            return new Mod
            {
                Key = Key(ModifyTarget.Transform, 0, go),
                Blend = t =>
                {
                    if (go == null) return;
                    Vector3 p = Vector3.LerpUnclamped(start, end, t);
                    Vector3 d = p - last; last = p;
                    go.transform.position = p;
                    // Moving platforms re-evaluate their position from the path every frame, so shift the path too.
                    if (EditorSessionManager.MotionPaths.TryGetValue(go, out var mp) && mp != null) { mp.PointA += d; mp.PointB += d; }
                }
            };
        }

        private static Mod MakeRotation(GameObject go, ModifyOp op, Vector3 euler)
        {
            Quaternion start = go.transform.rotation;
            Quaternion end = op == ModifyOp.Set ? Quaternion.Euler(euler) : Quaternion.Euler(Vector3.Scale(start.eulerAngles, euler));
            return new Mod
            {
                Key = Key(ModifyTarget.Transform, 1, go),
                Blend = t =>
                {
                    if (go == null) return;
                    // Add uses a linear euler sweep so spins larger than 180 degrees work.
                    go.transform.rotation = op == ModifyOp.Add ? Quaternion.Euler(euler * t) * start : Quaternion.SlerpUnclamped(start, end, t);
                    // Directional lights and gravity areas derive their direction from the object rotation.
                    if (EditorSessionManager.PlacedLights.TryGetValue(go, out var lc) && lc != null && (lc.Kind == LightKind.Directional || lc.IsDirectional))
                        EditorSessionManager.ApplyLightConfig(go, lc);
                    if (GravityAreaService.PlacedGravityConfigs.TryGetValue(go, out var gc) && gc != null)
                        GravityAreaService.ApplyGravityConfig(go, gc);
                }
            };
        }

        private static Mod MakeScale(GameObject go, ModifyOp op, Vector3 v)
        {
            Vector3 start = go.transform.localScale;
            Vector3 end = op == ModifyOp.Set ? v : op == ModifyOp.Add ? start + v : Vector3.Scale(start, v);
            return new Mod
            {
                Key = Key(ModifyTarget.Transform, 2, go),
                Blend = t => { if (go != null) go.transform.localScale = Vector3.LerpUnclamped(start, end, t); }
            };
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------
        private static string Key(ModifyTarget t, int prop, GameObject go)
            => (int)t + ":" + prop + ":" + (go != null ? go.GetInstanceID() : 0);

        // Comma-separated, case-insensitive "name contains" filter. Blank = everything.
        private static IEnumerable<GameObject> Matching(List<GameObject> source, string filter)
        {
            var tokens = new List<string>();
            foreach (var part in (filter ?? "").Split(','))
            {
                string s = part.Trim().ToLowerInvariant();
                if (s.Length > 0) tokens.Add(s);
            }

            var list = new List<GameObject>(source);
            for (int i = 0; i < list.Count; i++)
            {
                GameObject go = list[i];
                if (go == null) continue;
                if (tokens.Count == 0) { yield return go; continue; }
                string n = go.name.ToLowerInvariant();
                for (int t = 0; t < tokens.Count; t++)
                    if (n.IndexOf(tokens[t], StringComparison.Ordinal) >= 0) { yield return go; break; }
            }
        }
    }
}
