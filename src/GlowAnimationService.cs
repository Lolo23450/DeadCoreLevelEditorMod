using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeadCoreEditor
{
    public static class GlowAnimationService
    {
        private static readonly MaterialPropertyBlock _sharedMpb = new MaterialPropertyBlock();
        private static readonly Dictionary<int, Component> _cachedHdLightComponents = new Dictionary<int, Component>();
        private static readonly HashSet<int> _activePulsingLights = new HashSet<int>();

        private static readonly string[] NeonColorProperties = new string[]
        {
            "_EmissionColor", "_EmissiveColor", "_LinesColor", "_LineColor",
            "_ColorLines", "_ColorLine", "_Color2", "_EnergyColor",
            "_CircuitColor", "_GlowColor", "_NeonColor", "_TintColor"
        };

        public static void ClearCache()
        {
            _cachedHdLightComponents.Clear();
            _activePulsingLights.Clear();
        }

        public static float EvaluateMultiplier(GlowMode mode, float freq, float minMult, float maxMult, int syncGroup, float phaseOffset, int instanceSeed)
        {
            if (mode == GlowMode.Steady) return 1.0f;

            float time = Time.time;
            float effectiveTime = (time * Mathf.Max(0.01f, freq)) + phaseOffset;
            float seed = syncGroup != 0 ? (syncGroup * 37.17f) : (instanceSeed * 0.13f);

            switch (mode)
            {
                case GlowMode.Breathe:
                    {
                        float sine = (Mathf.Sin(effectiveTime * Mathf.PI * 2f) * 0.5f) + 0.5f;
                        return Mathf.Lerp(minMult, maxMult, sine);
                    }

                case GlowMode.Glitch:
                    {
                        float n1 = Mathf.PerlinNoise(effectiveTime * 4.0f, seed);
                        float n2 = Mathf.PerlinNoise(effectiveTime * 18.0f, seed + 9.31f);

                        if (n1 > 0.82f || n2 > 0.88f) return minMult * 0.1f; // Blackout dropout
                        if (n1 > 0.65f && n1 < 0.70f) return maxMult * 1.5f; // Overvoltage spark
                        if (n2 < 0.25f) return Mathf.Lerp(minMult, 1.0f, n2 * 4f);

                        return Mathf.Lerp(minMult, 1.0f, n1);
                    }

                case GlowMode.Surge:
                    {
                        float cycle = effectiveTime % 1.0f;
                        if (cycle < 0.0f) cycle += 1.0f;

                        if (cycle < 0.70f) return Mathf.Lerp(minMult, 1.0f, cycle / 0.70f);
                        if (cycle < 0.85f) return Mathf.Lerp(1.0f, maxMult, (cycle - 0.70f) / 0.15f);
                        return minMult * 0.05f;
                    }

                case GlowMode.Alarm:
                    {
                        float cycle = effectiveTime % 1.0f;
                        if (cycle < 0.0f) cycle += 1.0f;
                        return (cycle < 0.5f) ? maxMult : minMult;
                    }

                case GlowMode.Heartbeat:
                    {
                        float cycle = effectiveTime % 1.0f;
                        if (cycle < 0.0f) cycle += 1.0f;

                        if (cycle < 0.14f) return Mathf.Lerp(minMult, maxMult, Mathf.Sin((cycle / 0.14f) * Mathf.PI));
                        if (cycle >= 0.20f && cycle < 0.34f) return Mathf.Lerp(minMult, maxMult * 0.8f, Mathf.Sin(((cycle - 0.20f) / 0.14f) * Mathf.PI));
                        return minMult;
                    }

                default:
                    return 1.0f;
            }
        }

        public static void UpdateTick(float dt)
        {
            if (EditorSessionManager.PlacedObjects == null || EditorSessionManager.PlacedObjects.Count == 0) return;

            // =========================================================================
            // 1. ANIMATED NEON TRIMS & CIRCUITS
            // =========================================================================
            foreach (var kvp in EditorSessionManager.PlacedNeonConfigs)
            {
                GameObject obj = kvp.Key;
                NeonConfig cfg = kvp.Value;
                if (obj == null || !obj.activeInHierarchy || cfg == null || !cfg.IsActive) continue;
                if (cfg.Mode == GlowMode.Steady) continue;

                float mult = EvaluateMultiplier(cfg.Mode, cfg.Frequency, cfg.MinMultiplier, cfg.MaxMultiplier, cfg.SyncGroup, cfg.PhaseOffset, obj.GetInstanceID());
                Color activeEmissive = cfg.Color * (cfg.Intensity * mult);

                Renderer[] rends = obj.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < rends.Length; r++)
                {
                    Renderer rend = rends[r];
                    if (rend == null || rend.gameObject.name == "Editor_Snapping_Proxy") continue;

                    rend.GetPropertyBlock(_sharedMpb);
                    for (int p = 0; p < NeonColorProperties.Length; p++)
                    {
                        _sharedMpb.SetColor(NeonColorProperties[p], activeEmissive);
                    }
                    _sharedMpb.SetFloat("_EmissiveIntensity", cfg.Intensity * mult);
                    rend.SetPropertyBlock(_sharedMpb);
                }
            }

            // =========================================================================
            // 2. ANIMATED PHYSICAL LIGHTS (HDRP COMPATIBLE)
            // =========================================================================
            foreach (var kvp in EditorSessionManager.PlacedLights)
            {
                GameObject lightObj = kvp.Key;
                LightConfig cfg = kvp.Value;
                if (lightObj == null || !lightObj.activeInHierarchy || cfg == null) continue;

                int instanceId = lightObj.GetInstanceID();

                // If steady, ensure brightness returns to base intensity
                if (cfg.Mode == GlowMode.Steady)
                {
                    if (_activePulsingLights.Remove(instanceId))
                    {
                        EditorSessionManager.ApplyLightConfig(lightObj, cfg);
                    }
                    continue;
                }

                _activePulsingLights.Add(instanceId);

                float mult = EvaluateMultiplier(cfg.Mode, cfg.Frequency, cfg.MinMultiplier, cfg.MaxMultiplier, cfg.SyncGroup, cfg.PhaseOffset, instanceId);

                Light targetLight = cfg.IsDirectional
                    ? (RenderSettings.sun ?? SceneHarvestingService.NativeSceneSun)
                    : lightObj.GetComponentInChildren<Light>();

                if (targetLight != null && targetLight.enabled)
                {
                    float baseIntensity = cfg.IsDirectional
                        ? Mathf.Max(0.1f, cfg.Intensity) * 8000f
                        : Mathf.Pow(Mathf.Max(0.1f, cfg.Intensity), 2.2f) * 8000f;

                    float finalIntensity = baseIntensity * mult;
                    targetLight.intensity = finalIntensity;

                    // Update HDRP AdditionalLightData so the HDRP render pipeline reflects the change
                    UpdateHDRPLightIntensity(targetLight.gameObject, finalIntensity, cfg.VolumetricIntensity * mult);
                }

                // Modulate fixture lens and housing emissive material
                Renderer[] rends = lightObj.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rends.Length; i++)
                {
                    if (rends[i] == null || rends[i].gameObject.name == "Editor_Snapping_Proxy") continue;
                    Material m = rends[i].sharedMaterial;
                    if (m == null) continue;

                    if (m.HasProperty("_EmissionColor"))
                    {
                        m.SetColor("_EmissionColor", cfg.Color * (cfg.Intensity * 0.75f * mult));
                        m.EnableKeyword("_EMISSION");
                    }
                    if (m.HasProperty("_VolumetricIntensity"))
                    {
                        m.SetFloat("_VolumetricIntensity", cfg.VolumetricIntensity * mult);
                    }
                }
            }
        }

        private static void UpdateHDRPLightIntensity(GameObject lightGo, float physicalIntensity, float volumetricDimmer)
        {
            if (lightGo == null) return;
            int id = lightGo.GetInstanceID();

            if (!_cachedHdLightComponents.TryGetValue(id, out Component hdLight) || hdLight == null)
            {
                Component[] comps = lightGo.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] == null) continue;
                    string fullName = comps[i].GetIl2CppType().FullName;
                    if (fullName.Contains("HDAdditionalLightData") || fullName.Contains("AdditionalLightData"))
                    {
                        hdLight = comps[i];
                        _cachedHdLightComponents[id] = hdLight;
                        break;
                    }
                }
            }

            if (hdLight != null)
            {
                try
                {
                    Type type = hdLight.GetType();
                    SetFieldOrProp(type, hdLight, "intensity", physicalIntensity);
                    SetFieldOrProp(type, hdLight, "volumetricDimmer", Mathf.Clamp(volumetricDimmer, 0f, 16f));
                    SetFieldOrProp(type, hdLight, "m_VolumetricDimmer", Mathf.Clamp(volumetricDimmer, 0f, 16f));
                }
                catch { }
            }
        }

        private static void SetFieldOrProp(Type type, object target, string name, object val)
        {
            try
            {
                var prop = type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(target, val, null);
                    return;
                }
                var field = type.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null) field.SetValue(target, val);
            }
            catch { }
        }
    }
}