using System;
using System.Reflection;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 1: CACHED REFLECTION ACCESSOR FOR HDRP VOLUME PARAMETERS
    // =========================================================================

    public class VolumeParameterAccessor
    {
        public object TargetParameter { get; private set; }
        private PropertyInfo _overrideProp;
        private FieldInfo _overrideField;
        private PropertyInfo _valProp;
        private FieldInfo _valField;
        private Type _valType;

        public VolumeParameterAccessor(object paramObj)
        {
            if (paramObj == null) return;
            TargetParameter = paramObj;

            Type t = paramObj.GetType();
            _overrideProp = t.GetProperty("overrideState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (_overrideProp == null)
                _overrideField = t.GetField("overrideState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            _valProp = t.GetProperty("value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (_valProp != null && _valProp.CanWrite)
            {
                _valType = _valProp.PropertyType;
            }
            else
            {
                _valField = t.GetField("value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (_valField != null) _valType = _valField.FieldType;
            }
        }

        public bool SetValue(object val)
        {
            if (TargetParameter == null || _valType == null) return false;

            try
            {
                // Ensure overrideState = true
                if (_overrideProp != null && _overrideProp.CanWrite) _overrideProp.SetValue(TargetParameter, true, null);
                else if (_overrideField != null) _overrideField.SetValue(TargetParameter, true);

                // Set value
                if (val is Color c && _valType == typeof(Color))
                {
                    if (_valProp != null) { _valProp.SetValue(TargetParameter, c, null); return true; }
                    if (_valField != null) { _valField.SetValue(TargetParameter, c); return true; }
                }

                object converted = Convert.ChangeType(val, _valType);
                if (_valProp != null) { _valProp.SetValue(TargetParameter, converted, null); return true; }
                if (_valField != null) { _valField.SetValue(TargetParameter, converted); return true; }
            }
            catch { }

            return false;
        }

        public object GetValue()
        {
            if (TargetParameter == null) return null;
            try
            {
                if (_valProp != null && _valProp.CanRead) return _valProp.GetValue(TargetParameter, null);
                if (_valField != null) return _valField.GetValue(TargetParameter);
            }
            catch { }
            return null;
        }
    }

    // =========================================================================
    // SECTION 2: CELESTIAL SKYBOX & HDRP VOLUME SERVICE
    // =========================================================================

    public static class SkyboxControllerService
    {
        public static SkyboxConfig ActiveConfig = new SkyboxConfig();
        public static GameObject ActiveControllerObject = null;

        private static readonly List<object> _hookedSkySettings = new List<object>();
        private static readonly List<VolumeParameterAccessor> _exposureAccessors = new List<VolumeParameterAccessor>();
        private static readonly List<VolumeParameterAccessor> _colorFilterAccessors = new List<VolumeParameterAccessor>();
        private static readonly List<VolumeParameterAccessor> _skyRotationAccessors = new List<VolumeParameterAccessor>();
        private static readonly List<VolumeParameterAccessor> _skyLuxAccessors = new List<VolumeParameterAccessor>();
        private static readonly List<VolumeParameterAccessor> _skyMultiplierAccessors = new List<VolumeParameterAccessor>();

        private static readonly Dictionary<object, float> _baseLuxValues = new Dictionary<object, float>();
        private static Light _sceneSunlight = null;

        // Scene baseline caches for clean restoration
        private static Color _baselineSunColor = Color.white;
        private static Color _baselineAmbientColor = Color.white;
        private static bool _hasStoredBaseline = false;

        public static void Initialize(GameObject controllerObj, SkyboxConfig config)
        {
            ActiveControllerObject = controllerObj;
            ActiveConfig = config ?? new SkyboxConfig();

            FindAndHookDeadCoreReduxSky();
            ApplyProperties();
        }

        public static void FindAndHookDeadCoreReduxSky()
        {
            ClearHookedAccessors();

            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.isLoaded) return;

            // Locate primary scene sun
            LocateSceneSun();

            // Store default lighting baseline if not yet stored
            if (!_hasStoredBaseline && _sceneSunlight != null)
            {
                _baselineSunColor = _sceneSunlight.color;
                _baselineAmbientColor = RenderSettings.ambientSkyColor;
                _hasStoredBaseline = true;
            }

            GameObject[] roots = activeScene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                GameObject root = roots[r];
                if (root == null) continue;

                string rLow = root.name.ToLowerInvariant();
                if (rLow == "_la" || rLow == "l_a" || rLow == "_ld" || rLow == "l_d") continue;

                Component[] comps = root.GetComponentsInChildren<Component>(true);
                for (int c = 0; c < comps.Length; c++)
                {
                    Component comp = comps[c];
                    if (comp == null) continue;

                    string typeName = comp.GetIl2CppType().Name;
                    if (typeName == "Volume" || typeName.EndsWith(".Volume"))
                    {
                        HookVolumeComponents(comp);
                    }
                }
            }

            MelonLogger.Msg($">> [Skybox Engine] Hooked {_hookedSkySettings.Count} Sky(s), {_exposureAccessors.Count} Exposure(s), {_colorFilterAccessors.Count} Color Filter(s).");
        }

        private static void LocateSceneSun()
        {
            if (RenderSettings.sun != null && !RenderSettings.sun.name.StartsWith("Custom_"))
            {
                _sceneSunlight = RenderSettings.sun;
                return;
            }

            Light[] lights = GameObject.FindObjectsOfType<Light>();
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional && !lights[i].name.StartsWith("Custom_"))
                {
                    _sceneSunlight = lights[i];
                    return;
                }
            }
        }

        private static void HookVolumeComponents(Component volumeComp)
        {
            object typedVolume = CastToRuntimeType(volumeComp);
            if (typedVolume == null) return;

            object profile = GetMemberValue(typedVolume, "sharedProfile") ??
                             GetMemberValue(typedVolume, "profile") ??
                             GetMemberValue(typedVolume, "m_InternalProfile");

            if (profile == null) return;

            object typedProfile = CastToRuntimeType(profile);
            string profileName = GetMemberValue(typedProfile, "name")?.ToString() ?? "Unknown";

            object compList = GetMemberValue(typedProfile, "components") ?? GetMemberValue(typedProfile, "m_Components");
            if (compList == null) return;

            int count = 0;
            object countObj = GetMemberValue(compList, "Count") ?? GetMemberValue(compList, "count");
            if (countObj != null) count = Convert.ToInt32(countObj);

            bool foundColorAdjustments = false;

            for (int i = 0; i < count; i++)
            {
                object rawItem = CallMethod(compList, "get_Item", i);
                if (rawItem == null) continue;

                object typedItem = CastToRuntimeType(rawItem);
                string itemType = typedItem.GetType().Name;

                // 1. Hook HDRP Sky
                if (itemType.Contains("Sky") && !itemType.Contains("StaticLighting"))
                {
                    if (!_hookedSkySettings.Contains(typedItem))
                    {
                        _hookedSkySettings.Add(typedItem);

                        float baseLux = 15000f;
                        object luxParam = GetMemberValue(typedItem, "desiredLuxValue") ?? GetMemberValue(typedItem, "lux");
                        if (luxParam != null)
                        {
                            var luxAccessor = new VolumeParameterAccessor(luxParam);
                            _skyLuxAccessors.Add(luxAccessor);
                            object val = luxAccessor.GetValue();
                            if (val != null) baseLux = Convert.ToSingle(val);
                        }
                        _baseLuxValues[typedItem] = (baseLux > 10f) ? baseLux : 15000f;

                        object rotParam = GetMemberValue(typedItem, "rotation");
                        if (rotParam != null) _skyRotationAccessors.Add(new VolumeParameterAccessor(rotParam));

                        object multParam = GetMemberValue(typedItem, "multiplier");
                        if (multParam != null) _skyMultiplierAccessors.Add(new VolumeParameterAccessor(multParam));
                    }
                }

                // 2. Hook Exposure Compensation
                if (itemType.Equals("Exposure", StringComparison.OrdinalIgnoreCase) || itemType.Contains("Exposure"))
                {
                    object compParam = GetMemberValue(typedItem, "compensation");
                    if (compParam != null) _exposureAccessors.Add(new VolumeParameterAccessor(compParam));
                }

                // 3. Hook Color Adjustments
                if (itemType.Contains("ColorAdjustments") || itemType.Contains("ColorGrading"))
                {
                    foundColorAdjustments = true;
                    object colorFilterParam = GetMemberValue(typedItem, "colorFilter");
                    if (colorFilterParam != null) _colorFilterAccessors.Add(new VolumeParameterAccessor(colorFilterParam));
                }
            }

            // Dynamically inject ColorAdjustments if missing
            if (!foundColorAdjustments)
            {
                InjectColorAdjustments(typedProfile, profileName);
            }
        }

        private static void InjectColorAdjustments(object typedProfile, string profileName)
        {
            Type caType = FindLoadedType("UnityEngine.Rendering.HighDefinition.ColorAdjustments", "ColorAdjustments");
            if (caType == null) return;

            try
            {
                object newCA = null;
                var addMethod = typedProfile.GetType().GetMethod("Add", new Type[] { typeof(Type), typeof(bool) });
                if (addMethod != null)
                {
                    newCA = addMethod.Invoke(typedProfile, new object[] { caType, true });
                }
                else
                {
                    var genericAdd = typedProfile.GetType().GetMethod("Add", new Type[] { typeof(bool) });
                    if (genericAdd != null)
                    {
                        newCA = genericAdd.MakeGenericMethod(caType).Invoke(typedProfile, new object[] { true });
                    }
                }

                if (newCA != null)
                {
                    object typedCA = CastToRuntimeType(newCA);
                    object colorFilterParam = GetMemberValue(typedCA, "colorFilter");
                    if (colorFilterParam != null)
                    {
                        _colorFilterAccessors.Add(new VolumeParameterAccessor(colorFilterParam));
                        MelonLogger.Msg($">> [Skybox Engine] Dynamically injected ColorAdjustments into '{profileName}'");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Skybox Engine] Could not inject ColorAdjustments: {ex.Message}");
            }
        }

        public static void ApplyProperties()
        {
            if (_hookedSkySettings.Count == 0)
                FindAndHookDeadCoreReduxSky();

            float rawExposure = Mathf.Max(0.01f, ActiveConfig.Exposure);
            float evCompensation = Mathf.Log(rawExposure, 2f);
            Color tint = ActiveConfig.TintColor;

            // 1. Camera exposure compensation
            for (int i = 0; i < _exposureAccessors.Count; i++)
            {
                _exposureAccessors[i].SetValue(evCompensation);
            }

            // 2. RGB color grading filter
            for (int i = 0; i < _colorFilterAccessors.Count; i++)
            {
                _colorFilterAccessors[i].SetValue(tint);
            }

            // 3. Drive sun and ambient color (only if player hasn't placed a custom sun)
            bool hasCustomSun = false;
            if (EditorSessionManager.PlacedObjects != null)
            {
                for (int i = 0; i < EditorSessionManager.PlacedObjects.Count; i++)
                {
                    var obj = EditorSessionManager.PlacedObjects[i];
                    if (obj != null && EditorSessionManager.PlacedObjectTypes.TryGetValue(obj, out var t) && t == PlacedObjectType.Sunlight)
                    {
                        hasCustomSun = true;
                        break;
                    }
                }
            }

            if (!hasCustomSun && _sceneSunlight != null)
            {
                _sceneSunlight.color = tint;
            }
            RenderSettings.ambientSkyColor = tint;

            // 4. Drive HDRI sky intensity
            for (int i = 0; i < _skyLuxAccessors.Count; i++)
            {
                float baseLux = 15000f;
                if (i < _hookedSkySettings.Count && _baseLuxValues.TryGetValue(_hookedSkySettings[i], out float bl))
                    baseLux = bl;

                float targetLux = baseLux * Mathf.Pow(rawExposure, 1.2f);
                _skyLuxAccessors[i].SetValue(targetLux);
            }

            for (int i = 0; i < _skyMultiplierAccessors.Count; i++)
            {
                _skyMultiplierAccessors[i].SetValue(rawExposure);
            }

            ApplyRotation(ActiveConfig.CurrentAngle);
        }

        public static void ApplyRotation(float angleDegrees)
        {
            float clampedAngle = angleDegrees % 360f;
            if (clampedAngle < 0f) clampedAngle += 360f;

            for (int i = 0; i < _skyRotationAccessors.Count; i++)
            {
                _skyRotationAccessors[i].SetValue(clampedAngle);
            }
        }

        public static void UpdateTick(float dt)
        {
            if (ActiveConfig == null) return;

            if (Mathf.Abs(ActiveConfig.SpinSpeed) > 0.001f)
            {
                ActiveConfig.CurrentAngle = (ActiveConfig.CurrentAngle + (ActiveConfig.SpinSpeed * dt)) % 360f;
                ApplyRotation(ActiveConfig.CurrentAngle);
            }
        }

        public static void ResetToSceneDefault()
        {
            if (_hasStoredBaseline)
            {
                if (_sceneSunlight != null) _sceneSunlight.color = _baselineSunColor;
                RenderSettings.ambientSkyColor = _baselineAmbientColor;
            }

            ClearHookedAccessors();
            _hasStoredBaseline = false;
        }

        private static void ClearHookedAccessors()
        {
            _hookedSkySettings.Clear();
            _exposureAccessors.Clear();
            _colorFilterAccessors.Clear();
            _skyRotationAccessors.Clear();
            _skyLuxAccessors.Clear();
            _skyMultiplierAccessors.Clear();
            _baseLuxValues.Clear();
            _sceneSunlight = null;
        }

        // =========================================================================
        // SECTION 3: IL2CPP & RUNTIME REFLECTION HELPERS
        // =========================================================================

        private static object CastToRuntimeType(object comp)
        {
            if (comp == null) return null;

            MethodInfo getIl2Cpp = comp.GetType().GetMethod("GetIl2CppType", BindingFlags.Public | BindingFlags.Instance);
            if (getIl2Cpp == null) return comp;

            try
            {
                var il2Type = getIl2Cpp.Invoke(comp, null) as Il2CppSystem.Type;
                if (il2Type == null) return comp;

                Type target = FindLoadedType(il2Type.FullName, il2Type.Name);
                if (target == null || target == comp.GetType()) return comp;

                PropertyInfo ptrProp = comp.GetType().GetProperty("Pointer", BindingFlags.Public | BindingFlags.Instance) ??
                                       comp.GetType().GetProperty("m_CachedPtr", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (ptrProp != null)
                {
                    IntPtr ptr = (IntPtr)ptrProp.GetValue(comp, null);
                    ConstructorInfo ctor = target.GetConstructor(new Type[] { typeof(IntPtr) });
                    if (ctor != null) return ctor.Invoke(new object[] { ptr });
                }
            }
            catch { }

            return comp;
        }

        private static Type FindLoadedType(string fullName, string name)
        {
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                Type t = asms[i].GetType(fullName);
                if (t != null) return t;
            }
            for (int i = 0; i < asms.Length; i++)
            {
                Type[] types = null;
                try { types = asms[i].GetTypes(); } catch { }
                if (types == null) continue;

                for (int j = 0; j < types.Length; j++)
                {
                    if (types[j].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return types[j];
                }
            }
            return null;
        }

        private static object GetMemberValue(object target, string name)
        {
            if (target == null) return null;
            try
            {
                Type t = target.GetType();
                var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null && prop.CanRead) return prop.GetValue(target, null);

                var field = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field != null) return field.GetValue(target);
            }
            catch { }
            return null;
        }

        private static object CallMethod(object target, string methodName, params object[] args)
        {
            if (target == null) return null;
            try
            {
                Type t = target.GetType();
                var method = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (method != null) return method.Invoke(target, args);
            }
            catch { }
            return null;
        }
    }
}