using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using MelonLoader;

namespace DeadCoreEditor
{
    public enum TriggerEvent { OnEnter = 0, OnExit = 1, WhileInside = 2 }
    // Numeric values remain stable for existing saved levels. Removed actions are intentionally unused.
    public enum TriggerAction
    {
        Kill = 1, Teleport = 2, CompleteLevel = 5, ToggleObjects = 6, TimeScale = 7, ModifyValue = 8
    }
    public enum TriggerToggleMode { Show = 0, Hide = 1, Toggle = 2 }

    public class TriggerVolumeConfig : IEditorComponent
    {
        public string ComponentTag => "TRIGGER";

        public TriggerEvent Event = TriggerEvent.OnEnter;
        public TriggerAction Action = TriggerAction.Kill;
        public TriggerToggleMode ToggleMode = TriggerToggleMode.Toggle;
        public bool IsActive = true;
        public bool Once = false;
        public float Cooldown = 0.5f;   // seconds between firings
        public float Delay = 0f;        // seconds before action runs
        public float Value = 1f;        // time scale multiplier
        public float Duration = 0.4f;   // time-scale duration (0 = until level restart)
        public Vector3 Offset = new Vector3(0f, 0f, 5f); // teleport offset (local) or launch direction (local)
        public string Text = "Trigger activated!";       // message text
        public string TargetName = "";                   // object name filter for Teleport / ToggleObjects / ModifyValue

        // --- ModifyValue ---
        public ModifyTarget ModTarget = ModifyTarget.Transform;
        public int ModProp = 0;                          // index into TriggerModifyService.PropertyNames(ModTarget)
        public ModifyOp ModOp = ModifyOp.Set;
        public float ModValue = 1f;
        public Color ModColor = Color.white;
        public Vector3 ModVector = Vector3.zero;        // position / euler rotation / scale
        public bool RevertOnExit = false;               // tween back to the previous value when the player leaves

        public TriggerVolumeConfig Clone() => new TriggerVolumeConfig
        {
            Event = Event, Action = Action, ToggleMode = ToggleMode, IsActive = IsActive, Once = Once,
            Cooldown = Cooldown, Delay = Delay, Value = Value, Duration = Duration,
            Offset = Offset, Text = Text, TargetName = TargetName,
            ModTarget = ModTarget, ModProp = ModProp, ModOp = ModOp, ModValue = ModValue,
            ModColor = ModColor, ModVector = ModVector, RevertOnExit = RevertOnExit
        };
        IEditorComponent IEditorComponent.Clone() => Clone();

        public string Serialize()
        {
            var inv = CultureInfo.InvariantCulture;
            return string.Join(":", new[]
            {
                ((int)Event).ToString(), ((int)Action).ToString(), ((int)ToggleMode).ToString(),
                IsActive ? "1" : "0", Once ? "1" : "0",
                Cooldown.ToString("F3", inv), Delay.ToString("F3", inv), Value.ToString("F3", inv), Duration.ToString("F3", inv),
                Offset.x.ToString("F3", inv), Offset.y.ToString("F3", inv), Offset.z.ToString("F3", inv),
                Uri.EscapeDataString(Text ?? ""), Uri.EscapeDataString(TargetName ?? ""),
                ((int)ModTarget).ToString(), ModProp.ToString(), ((int)ModOp).ToString(),
                ModValue.ToString("F4", inv), PersistenceUtility.ColorToHex(ModColor),
                ModVector.x.ToString("F4", inv), ModVector.y.ToString("F4", inv), ModVector.z.ToString("F4", inv),
                RevertOnExit ? "1" : "0"
            });
        }

        public void Deserialize(string rawData)
        {
            if (string.IsNullOrWhiteSpace(rawData)) return;
            string[] p = rawData.Split(':');
            if (p.Length < 12) return;
            int.TryParse(p[0], out int ev); Event = (TriggerEvent)Mathf.Clamp(ev, 0, 2);
            int.TryParse(p[1], out int ac);
            Action = ac == 2 ? TriggerAction.Teleport : ac == 5 ? TriggerAction.CompleteLevel :
                     ac == 6 ? TriggerAction.ToggleObjects : ac == 7 ? TriggerAction.TimeScale :
                     ac == 8 ? TriggerAction.ModifyValue : TriggerAction.Kill;
            int.TryParse(p[2], out int tm); ToggleMode = (TriggerToggleMode)Mathf.Clamp(tm, 0, 2);
            IsActive = p[3] == "1"; Once = p[4] == "1";
            Cooldown = PersistenceUtility.ParseFloat(p[5], 0.5f);
            Delay = PersistenceUtility.ParseFloat(p[6], 0f);
            Value = PersistenceUtility.ParseFloat(p[7], 1f);
            Duration = PersistenceUtility.ParseFloat(p[8], 0.4f);
            Offset = new Vector3(PersistenceUtility.ParseFloat(p[9]), PersistenceUtility.ParseFloat(p[10]), PersistenceUtility.ParseFloat(p[11]));
            if (p.Length > 12) { try { Text = Uri.UnescapeDataString(p[12]); } catch { Text = p[12]; } }
            if (p.Length > 13) { try { TargetName = Uri.UnescapeDataString(p[13]); } catch { TargetName = p[13]; } }
            // ModifyValue fields (absent in levels saved before this action existed)
            if (p.Length > 22)
            {
                ModTarget = (ModifyTarget)Mathf.Clamp(PersistenceUtility.ParseInt(p[14], 0), 0, (int)ModifyTarget.MotionPath);
                ModProp = Mathf.Max(0, PersistenceUtility.ParseInt(p[15], 0));
                ModOp = (ModifyOp)Mathf.Clamp(PersistenceUtility.ParseInt(p[16], 0), 0, 2);
                ModValue = PersistenceUtility.ParseFloat(p[17], 1f);
                ModColor = PersistenceUtility.HexToColor(p[18], Color.white);
                ModVector = new Vector3(PersistenceUtility.ParseFloat(p[19]), PersistenceUtility.ParseFloat(p[20]), PersistenceUtility.ParseFloat(p[21]));
                RevertOnExit = p[22] == "1";
            }
        }
    }

    public static class TriggerVolumeService
    {
        public const string AssetName = "Trigger Volume";
        public static readonly List<GameObject> PlacedVolumes = new List<GameObject>();
        public static readonly Dictionary<GameObject, TriggerVolumeConfig> Configs = new Dictionary<GameObject, TriggerVolumeConfig>();

        private class RuntimeState { public bool Inside; public bool Used; public float CooldownLeft; public float DelayLeft = -1f; }
        private static readonly Dictionary<GameObject, RuntimeState> _state = new Dictionary<GameObject, RuntimeState>();
        private static Material _mat;
        private static Mesh _cube;

        // active time-scale effect
        private static float _timeScaleLeft;

        public static bool IsTriggerVolume(GameObject go) => go != null && Configs.ContainsKey(go);

        public static CatalogAsset MakeCatalogAsset()
        {
            GameObject tpl = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tpl.name = "Template_TriggerVolume";
            tpl.SetActive(false);
            GameObject.DontDestroyOnLoad(tpl);
            var asset = new CatalogAsset
            {
                DisplayName = AssetName,
                SourceTemplate = tpl,
                Category = AssetCategory.Gameplay,
                SubCategory = "Gameplay",
                Traits = AssetTrait.None,
                DefaultScale = 1.0f
            };
            asset.ComputeSizeMetrics();
            return asset;
        }

        private static void EnsureAssets()
        {
            if (_mat == null)
            {
                Shader s = Shader.Find("HDRP/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                _mat = new Material(s) { name = "Mat_TriggerVolume" };
                MakeTransparent(_mat);
                SetVolumeColor(_mat, new Color(1f, 0.6f, 0.1f, EditAlphaActive));
            }
            if (_cube == null)
            {
                GameObject t = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _cube = t.GetComponent<MeshFilter>().sharedMesh;
                GameObject.DestroyImmediate(t);
            }
        }

        public static GameObject Create(Vector3 position, Vector3 size, Quaternion? rot = null)
        {
            EnsureAssets();
            if (size.sqrMagnitude < 0.01f || size == Vector3.one) size = new Vector3(6f, 4f, 6f);
            GameObject go = new GameObject("Custom_Trigger_Volume");
            go.transform.position = position;
            go.transform.rotation = rot ?? Quaternion.identity;
            go.transform.localScale = size;
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true; bc.size = Vector3.one;

            GameObject vis = new GameObject("Volume_Visual_Box");
            vis.transform.SetParent(go.transform, false);
            vis.layer = 2;
            vis.AddComponent<MeshFilter>().sharedMesh = _cube;
            var mr = vis.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            Apply(go, new TriggerVolumeConfig());
            EditorSessionManager.RegisterPlacedObject(go);
            return go;
        }

        public static void Apply(GameObject go, TriggerVolumeConfig cfg)
        {
            if (go == null || cfg == null) return;
            Configs[go] = cfg;
            if (!PlacedVolumes.Contains(go)) PlacedVolumes.Add(go);
            var bc = go.GetComponent<BoxCollider>();
            if (bc != null) { bc.isTrigger = true; bc.enabled = true; }
            // Recolor visual per action (own material instance so volumes can differ)
            Transform vis = go.transform.Find("Volume_Visual_Box");
            if (vis != null)
            {
                var mr = vis.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    EnsureAssets();
                    if (mr.sharedMaterial == _mat) mr.material = new Material(_mat);
                    Color c = ActionColor(cfg.Action); c.a = cfg.IsActive ? EditAlphaActive : EditAlphaInactive;
                    SetVolumeColor(mr.material, c);
                }
            }
        }

        // Edit-mode opacity of the volume box (slightly see-through so geometry inside stays visible).
        private const float EditAlphaActive = 0.26f;
        private const float EditAlphaInactive = 0.10f;

        // Properly configures an HDRP/Unlit material for alpha-blended, double-sided, no-depth-write rendering.
        // Setting only the colour's alpha is not enough: HDRP materials default to opaque and ignore it.
        private static void MakeTransparent(Material m)
        {
            if (m == null) return;
            if (m.HasProperty("_SurfaceType")) m.SetFloat("_SurfaceType", 1f);
            if (m.HasProperty("_BlendMode")) m.SetFloat("_BlendMode", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", 5f);   // SrcAlpha
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", 10f);  // OneMinusSrcAlpha
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_ZTestTransparent")) m.SetFloat("_ZTestTransparent", 4f); // LessEqual
            if (m.HasProperty("_AlphaCutoffEnable")) m.SetFloat("_AlphaCutoffEnable", 0f);
            if (m.HasProperty("_DoubleSidedEnable")) m.SetFloat("_DoubleSidedEnable", 1f);
            if (m.HasProperty("_CullMode")) m.SetFloat("_CullMode", 0f);
            if (m.HasProperty("_CullModeForward")) m.SetFloat("_CullModeForward", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("MotionVectors", false);
            m.renderQueue = 3000;
        }

        private static void SetVolumeColor(Material m, Color c)
        {
            if (m == null) return;
            m.color = c;
            if (m.HasProperty("_UnlitColor")) m.SetColor("_UnlitColor", c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        private static Color ActionColor(TriggerAction a)
        {
            switch (a)
            {
                case TriggerAction.Kill: return new Color(1f, 0.15f, 0.15f);
                case TriggerAction.Teleport: return new Color(0.6f, 0.2f, 1f);
                case TriggerAction.CompleteLevel: return new Color(1f, 0.9f, 0.2f);
                case TriggerAction.ToggleObjects: return new Color(1f, 0.5f, 0.1f);
                case TriggerAction.TimeScale: return new Color(0.9f, 0.9f, 0.9f);
                case TriggerAction.ModifyValue: return new Color(0.2f, 0.9f, 0.6f);
                default: return new Color(0.3f, 0.6f, 1f);
            }
        }

        public static void Unregister(GameObject go)
        {
            PlacedVolumes.Remove(go); Configs.Remove(go); _state.Remove(go); TriggerModifyService.ForgetVolume(go);
        }
        public static void ClearAll() { PlacedVolumes.Clear(); Configs.Clear(); _state.Clear(); ResetRuntime(); }

        public static void UpdateVisuals(bool editMode)
        {
            for (int i = 0; i < PlacedVolumes.Count; i++)
            {
                var go = PlacedVolumes[i]; if (go == null) continue;
                var v = go.transform.Find("Volume_Visual_Box");
                if (v != null && v.gameObject.activeSelf != editMode) v.gameObject.SetActive(editMode);
            }
        }

        public static void ResetRuntime()
        {
            _state.Clear();
            TriggerModifyService.ResetAll();
            if (_timeScaleLeft != 0f || Time.timeScale != 1f) { Time.timeScale = 1f; }
            _timeScaleLeft = 0f;
        }

        private static bool Contains(GameObject vol, Vector3 worldPoint)
        {
            Vector3 l = vol.transform.InverseTransformPoint(worldPoint);
            return Mathf.Abs(l.x) <= 0.5f && Mathf.Abs(l.y) <= 0.5f && Mathf.Abs(l.z) <= 0.5f;
        }

        public static void Tick(GameObject player, CharacterController cc, float dt)
        {
            // Un-scaled dt for timers so slow-motion doesn't stretch cooldowns
            float rdt = Time.unscaledDeltaTime;
            TriggerModifyService.Tick(rdt);
            if (player == null) return;

            if (_timeScaleLeft > 0f)
            {
                _timeScaleLeft -= rdt;
                if (_timeScaleLeft <= 0f) { Time.timeScale = 1f; _timeScaleLeft = 0f; }
            }

            Vector3 p0 = player.transform.position;
            Vector3 p1 = p0 + Vector3.up * 1.0f;

            for (int i = 0; i < PlacedVolumes.Count; i++)
            {
                GameObject vol = PlacedVolumes[i];
                if (vol == null || !vol.activeInHierarchy) continue;
                if (!Configs.TryGetValue(vol, out var cfg) || !cfg.IsActive) continue;
                if (!_state.TryGetValue(vol, out var st)) { st = new RuntimeState(); _state[vol] = st; }

                if (st.CooldownLeft > 0f) st.CooldownLeft -= rdt;
                if (st.DelayLeft >= 0f)
                {
                    st.DelayLeft -= rdt;
                    if (st.DelayLeft < 0f) { Execute(vol, cfg, player, cc); st.DelayLeft = -1f; }
                }

                bool inside = Contains(vol, p0) || Contains(vol, p1);
                bool fire = false;
                switch (cfg.Event)
                {
                    case TriggerEvent.OnEnter: fire = inside && !st.Inside; break;
                    case TriggerEvent.OnExit: fire = !inside && st.Inside; break;
                    case TriggerEvent.WhileInside: fire = inside; break;
                }
                if (cfg.Action == TriggerAction.ModifyValue && cfg.RevertOnExit && st.Inside && !inside)
                    TriggerModifyService.Revert(vol, cfg.Duration);
                st.Inside = inside;

                if (!fire || st.CooldownLeft > 0f || st.DelayLeft >= 0f) continue;
                if (cfg.Once && st.Used) continue;
                st.Used = true;
                st.CooldownLeft = Mathf.Max(0f, cfg.Cooldown);
                if (cfg.Delay > 0.001f) st.DelayLeft = cfg.Delay;
                else Execute(vol, cfg, player, cc);
            }
        }

        private static void Execute(GameObject vol, TriggerVolumeConfig cfg, GameObject player, CharacterController cc)
        {
            try
            {
                switch (cfg.Action)
                {
                    case TriggerAction.Kill:
                        EditorSessionManager.RespawnPlayer(player);
                        break;
                    case TriggerAction.Teleport:
                    {
                        GameObject target = FindByName(cfg.TargetName, vol);
                        Vector3 dest = target != null ? target.transform.position + Vector3.up * 0.2f : vol.transform.position + vol.transform.rotation * cfg.Offset;
                        if (cc != null) cc.enabled = false;
                        player.transform.position = dest;
                        if (cc != null) cc.enabled = true;
                        break;
                    }
                    case TriggerAction.CompleteLevel:
                        EditorSessionManager.IsLevelCompleted = true;
                        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                        break;
                    case TriggerAction.ToggleObjects:
                    {
                        string f = (cfg.TargetName ?? "").Trim().ToLowerInvariant();
                        if (f.Length == 0) break;
                        var list = EditorSessionManager.PlacedObjects;
                        for (int i = 0; i < list.Count; i++)
                        {
                            var o = list[i];
                            if (o == null || o == vol || o.name.ToLowerInvariant().IndexOf(f, StringComparison.Ordinal) < 0) continue;
                            bool next = cfg.ToggleMode == TriggerToggleMode.Show ? true : cfg.ToggleMode == TriggerToggleMode.Hide ? false : !o.activeSelf;
                            o.SetActive(next);
                        }
                        break;
                    }
                    case TriggerAction.ModifyValue:
                        TriggerModifyService.Fire(vol, cfg);
                        break;
                    case TriggerAction.TimeScale:
                        Time.timeScale = Mathf.Clamp(cfg.Value, 0.05f, 4f);
                        _timeScaleLeft = cfg.Duration > 0.01f ? cfg.Duration : 0f;
                        break;
                }
            }
            catch (Exception ex) { MelonLogger.Warning("[Trigger] " + ex.Message); }
        }

        // ---------------------------------------------------------------------
        // 3D picking: analytic ray vs. oriented box, independent of physics layers
        // and working even when the camera sits inside the volume.
        // ---------------------------------------------------------------------
        public static GameObject PickVolume(Ray ray, float maxDistance, Func<GameObject, bool> skip, out float distance)
        {
            GameObject best = null;
            float bestDist = float.MaxValue;
            float bestSize = float.MaxValue;
            distance = float.MaxValue;

            for (int i = 0; i < PlacedVolumes.Count; i++)
            {
                GameObject vol = PlacedVolumes[i];
                if (vol == null || !vol.activeInHierarchy) continue;
                if (skip != null && skip(vol)) continue;
                if (!RayVsVolume(vol, ray, out float d) || d > maxDistance) continue;

                Vector3 ls = vol.transform.lossyScale;
                float size = Mathf.Abs(ls.x * ls.y * ls.z);
                // Nearest wins; for (nearly) equal distances the smaller, more nested volume wins.
                if (d < bestDist - 0.01f || (Mathf.Abs(d - bestDist) <= 0.01f && size < bestSize))
                {
                    best = vol; bestDist = d; bestSize = size;
                }
            }

            distance = bestDist;
            return best;
        }

        // Entry distance when the ray starts outside, exit (far wall) distance when it starts inside.
        private static bool RayVsVolume(GameObject vol, Ray ray, out float dist)
        {
            dist = 0f;
            Transform t = vol.transform;
            Vector3 o = t.InverseTransformPoint(ray.origin);
            Vector3 d = t.InverseTransformVector(ray.direction); // keeps t in world units

            float tMin = float.NegativeInfinity, tMax = float.PositiveInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                float oa = o[axis], da = d[axis];
                if (Mathf.Abs(da) < 1e-8f)
                {
                    if (Mathf.Abs(oa) > 0.5f) return false;
                    continue;
                }
                float t1 = (-0.5f - oa) / da, t2 = (0.5f - oa) / da;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                if (t1 > tMin) tMin = t1;
                if (t2 < tMax) tMax = t2;
                if (tMin > tMax) return false;
            }
            if (tMax < 0f) return false;
            dist = tMin >= 0f ? tMin : tMax;
            return true;
        }

        private static GameObject FindByName(string name, GameObject exclude)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string f = name.Trim().ToLowerInvariant();
            var list = EditorSessionManager.PlacedObjects;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i] != exclude && list[i].name.ToLowerInvariant().Contains(f)) return list[i];
            return null;
        }


    }
}
