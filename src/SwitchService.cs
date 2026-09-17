using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 1: SWITCH TARGET & ACTIVATION SERVICE
    // =========================================================================

    public static class SwitchService
    {
        public static Dictionary<GameObject, SwitchConfig> PlacedSwitches = new Dictionary<GameObject, SwitchConfig>();
        public static GameObject PrefabSwitch = null;

        public static void ApplySwitchConfig(GameObject switchGo, SwitchConfig cfg)
        {
            if (switchGo == null || cfg == null) return;
            PlacedSwitches[switchGo] = cfg;

            Interuptor interuptor = switchGo.GetComponentInChildren<Interuptor>(true);
            if (interuptor != null)
            {
                interuptor.IsAutoSwitch = cfg.IsAutoSwitch;
                interuptor.TimeBeforeSwitch = Mathf.Max(0.1f, cfg.TimeBeforeSwitch);
                interuptor._initialStateOn = cfg.InitialStateOn;
                interuptor._isOn = cfg.InitialStateOn;
                interuptor._warningTime = Mathf.Min(1.5f, cfg.TimeBeforeSwitch * 0.3f);
            }
        }

        public static void SyncChildrenState(GameObject switchGo, bool isSwitchOn)
        {
            if (switchGo == null) return;
            if (!PlacedSwitches.TryGetValue(switchGo, out var cfg))
                cfg = new SwitchConfig();

            // When Invert is true: Switch ON => Children turn OFF (ideal for hazard lasers)
            // When Invert is false: Switch ON => Children turn ON (ideal for bridges/platforms)
            bool desiredActive = cfg.InvertChildren ? !isSwitchOn : isSwitchOn;

            for (int i = 0; i < switchGo.transform.childCount; i++)
            {
                Transform child = switchGo.transform.GetChild(i);
                if (child == null || child.name == "Editor_Snapping_Proxy") continue;

                ApplyStateToHierarchy(child.gameObject, desiredActive);
            }
        }

        private static void ApplyStateToHierarchy(GameObject targetGo, bool active)
        {
            if (targetGo == null) return;

            // 1. Standard GameObject visibility and collision
            targetGo.SetActive(active);

            // 2. Synchronize Jumper pad physics and FX
            if (EditorSessionManager.PlacedObjectTypes.TryGetValue(targetGo, out var type) && type == PlacedObjectType.Jumper)
            {
                EditorSessionManager.ApplyJumperActive(targetGo, active);
            }

            // 3. Synchronize Kinematic Motion Paths
            if (EditorSessionManager.MotionPaths.TryGetValue(targetGo, out var mp) && mp != null)
            {
                mp.IsActive = active;
            }

            // 4. Synchronize Turrets
            TurretScript[] turrets = targetGo.GetComponentsInChildren<TurretScript>(true);
            for (int t = 0; t < turrets.Length; t++)
            {
                if (turrets[t] != null) turrets[t].enabled = active;
            }

            // 5. Synchronize Lasers
            LaserScript[] lasers = targetGo.GetComponentsInChildren<LaserScript>(true);
            for (int l = 0; l < lasers.Length; l++)
            {
                if (lasers[l] != null) lasers[l].enabled = active;
            }
        }

        public static void ResetAllSwitchesForPlaytest(bool enteringPlaytest)
        {
            foreach (var kvp in PlacedSwitches)
            {
                GameObject switchGo = kvp.Key;
                SwitchConfig cfg = kvp.Value;
                if (switchGo == null) continue;

                Interuptor interuptor = switchGo.GetComponentInChildren<Interuptor>(true);
                if (interuptor != null)
                {
                    interuptor._isOn = cfg.InitialStateOn;
                }

                if (enteringPlaytest)
                {
                    SyncChildrenState(switchGo, cfg.InitialStateOn);
                }
                else
                {
                    // In Edit Mode, restore all children to active so the user can build and select them
                    for (int i = 0; i < switchGo.transform.childCount; i++)
                    {
                        Transform child = switchGo.transform.GetChild(i);
                        if (child == null || child.name == "Editor_Snapping_Proxy") continue;

                        ApplyStateToHierarchy(child.gameObject, true);
                    }
                }
            }
        }
    }

    // =========================================================================
    // SECTION 2: HARMONY HOOKS (INTERCEPT NATIVE WEAPON INTERACTIONS)
    // =========================================================================

    [HarmonyPatch(typeof(Interuptor), nameof(Interuptor.RealActivation))]
    public static class InteruptorActivatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Interuptor __instance)
        {
            if (__instance == null || EditorSessionManager.IsEditModeActive) return;

            Transform curr = __instance.transform;
            while (curr != null)
            {
                if (SwitchService.PlacedSwitches.ContainsKey(curr.gameObject))
                {
                    SwitchService.SyncChildrenState(curr.gameObject, isSwitchOn: true);
                    break;
                }
                curr = curr.parent;
            }
        }
    }

    [HarmonyPatch(typeof(Interuptor), nameof(Interuptor.RealDeactivation))]
    public static class InteruptorDeactivatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Interuptor __instance)
        {
            if (__instance == null || EditorSessionManager.IsEditModeActive) return;

            Transform curr = __instance.transform;
            while (curr != null)
            {
                if (SwitchService.PlacedSwitches.ContainsKey(curr.gameObject))
                {
                    SwitchService.SyncChildrenState(curr.gameObject, isSwitchOn: false);
                    break;
                }
                curr = curr.parent;
            }
        }
    }
}