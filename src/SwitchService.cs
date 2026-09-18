using System.Collections.Generic;
using HarmonyLib;
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
        public static bool IsResettingSwitches = false;

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

        /// <summary>
        /// Finds all objects connected to this switch, whether they are parented UNDER
        /// the switch or the switch is parented UNDER them (parent object).
        /// Safely ignores internal switch prefab meshes and colliders.
        /// </summary>
        public static List<GameObject> GetConnectedObjects(GameObject switchGo)
        {
            List<GameObject> connected = new List<GameObject>();
            if (switchGo == null) return connected;

            // 1. Check if the switch is parented under another placed object (e.g., Laser -> Switch)
            if (switchGo.transform.parent != null)
            {
                GameObject parentObj = switchGo.transform.parent.gameObject;
                if (EditorSessionManager.PlacedObjects.Contains(parentObj) && !PlacedSwitches.ContainsKey(parentObj))
                {
                    connected.Add(parentObj);
                }
            }

            // 2. Check objects parented under the switch (e.g., Switch -> Laser)
            for (int i = 0; i < switchGo.transform.childCount; i++)
            {
                Transform child = switchGo.transform.GetChild(i);
                if (child == null || child.name == "Editor_Snapping_Proxy") continue;

                // Only include placed level objects, ignoring native switch internal parts
                if (EditorSessionManager.PlacedObjects.Contains(child.gameObject) ||
                    child.name.StartsWith("Custom_") ||
                    child.GetComponent<LaserScript>() != null ||
                    child.GetComponent<Jumper>() != null ||
                    child.GetComponent<Helix>() != null ||
                    child.GetComponent<TurretScript>() != null)
                {
                    if (!PlacedSwitches.ContainsKey(child.gameObject) && !connected.Contains(child.gameObject))
                    {
                        connected.Add(child.gameObject);
                    }
                }
            }

            return connected;
        }

        public static void SyncChildrenState(GameObject switchGo, bool isSwitchOn)
        {
            if (switchGo == null) return;
            if (!PlacedSwitches.TryGetValue(switchGo, out var cfg))
                cfg = new SwitchConfig();

            // When Invert is true: Switch ON => Connected objects turn OFF (e.g. hazard lasers)
            // When Invert is false: Switch ON => Connected objects turn ON (e.g. platforms/bridges)
            bool desiredActive = cfg.InvertChildren ? !isSwitchOn : isSwitchOn;

            List<GameObject> targets = GetConnectedObjects(switchGo);
            for (int i = 0; i < targets.Count; i++)
            {
                ApplyStateToHierarchy(targets[i], desiredActive, switchGo);
            }
        }

        private static void ApplyStateToHierarchy(GameObject targetGo, bool active, GameObject switchGo = null)
        {
            if (targetGo == null) return;

            bool isParentOfSwitch = (switchGo != null && switchGo.transform.IsChildOf(targetGo.transform));

            if (!isParentOfSwitch)
            {
                // Target is a child: safe to toggle the GameObject directly
                targetGo.SetActive(active);
            }
            else
            {
                // Target is the parent of the switch: do NOT SetActive(false) on the parent,
                // otherwise the switch inside it would be destroyed/deactivated too!
                ToggleParentComponentsExcludingSwitch(targetGo, switchGo, active);
            }

            // Synchronize Jumper pad physics and FX
            if (EditorSessionManager.PlacedObjectTypes.TryGetValue(targetGo, out var type) && type == PlacedObjectType.Jumper)
            {
                EditorSessionManager.ApplyJumperActive(targetGo, active);
            }

            // Synchronize Kinematic Motion Paths
            if (EditorSessionManager.MotionPaths.TryGetValue(targetGo, out var mp) && mp != null)
            {
                mp.IsActive = active;
            }

            // Synchronize Turrets
            TurretScript[] turrets = targetGo.GetComponentsInChildren<TurretScript>(true);
            for (int t = 0; t < turrets.Length; t++)
            {
                if (turrets[t] != null && (switchGo == null || !turrets[t].transform.IsChildOf(switchGo.transform)))
                    turrets[t].enabled = active;
            }

            // Synchronize Lasers
            LaserScript[] lasers = targetGo.GetComponentsInChildren<LaserScript>(true);
            for (int l = 0; l < lasers.Length; l++)
            {
                if (lasers[l] != null && (switchGo == null || !lasers[l].transform.IsChildOf(switchGo.transform)))
                    lasers[l].enabled = active;
            }

            // Synchronize Turbines
            Helix[] turbines = targetGo.GetComponentsInChildren<Helix>(true);
            for (int h = 0; h < turbines.Length; h++)
            {
                if (turbines[h] != null && (switchGo == null || !turbines[h].transform.IsChildOf(switchGo.transform)))
                    turbines[h].enabled = active;
            }
            HelixPushingZone[] zones = targetGo.GetComponentsInChildren<HelixPushingZone>(true);
            for (int z = 0; z < zones.Length; z++)
            {
                if (zones[z] != null && (switchGo == null || !zones[z].transform.IsChildOf(switchGo.transform)))
                    zones[z].enabled = active;
            }
        }

        private static void ToggleParentComponentsExcludingSwitch(GameObject parentGo, GameObject switchGo, bool active)
        {
            if (parentGo == null) return;

            Renderer[] rends = parentGo.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < rends.Length; r++)
            {
                if (rends[r] != null && (switchGo == null || !rends[r].transform.IsChildOf(switchGo.transform)))
                    rends[r].enabled = active;
            }

            Collider[] cols = parentGo.GetComponentsInChildren<Collider>(true);
            for (int c = 0; c < cols.Length; c++)
            {
                if (cols[c] != null && (switchGo == null || !cols[c].transform.IsChildOf(switchGo.transform)))
                {
                    if (cols[c].gameObject.name != "Editor_Snapping_Proxy")
                        cols[c].enabled = active;
                }
            }
        }

        public static void ResetAllSwitchesForPlaytest(bool enteringPlaytest)
        {
            IsResettingSwitches = true;
            try
            {
                foreach (var kvp in PlacedSwitches)
                {
                    GameObject switchGo = kvp.Key;
                    SwitchConfig cfg = kvp.Value;
                    if (switchGo == null) continue;

                    if (!switchGo.activeSelf) switchGo.SetActive(true);

                    Interuptor interuptor = switchGo.GetComponentInChildren<Interuptor>(true);
                    if (interuptor != null)
                    {
                        if (!interuptor.gameObject.activeSelf) interuptor.gameObject.SetActive(true);
                        interuptor.enabled = true;

                        interuptor._initialStateOn = cfg.InitialStateOn;
                        interuptor._isOn = cfg.InitialStateOn;
                        interuptor.IsAutoSwitch = cfg.IsAutoSwitch;
                        interuptor.TimeBeforeSwitch = Mathf.Max(0.1f, cfg.TimeBeforeSwitch);

                        // Call native activation/deactivation to reset shaders, materials, and timers cleanly
                        try
                        {
                            if (cfg.InitialStateOn)
                                interuptor.RealActivation();
                            else
                                interuptor.RealDeactivation();
                        }
                        catch { }

                        interuptor._isOn = cfg.InitialStateOn;
                    }

                    List<GameObject> connected = GetConnectedObjects(switchGo);

                    if (enteringPlaytest)
                    {
                        // Playtest mode: synchronize connected objects to their initial state
                        bool desiredActive = cfg.InvertChildren ? !cfg.InitialStateOn : cfg.InitialStateOn;
                        for (int i = 0; i < connected.Count; i++)
                        {
                            ApplyStateToHierarchy(connected[i], desiredActive, switchGo);
                        }
                    }
                    else
                    {
                        // Edit Mode (F1): restore ALL connected objects to active so they can be viewed & edited
                        for (int i = 0; i < connected.Count; i++)
                        {
                            ApplyStateToHierarchy(connected[i], true, switchGo);
                        }
                    }
                }
            }
            finally
            {
                IsResettingSwitches = false;
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
            if (__instance == null || EditorSessionManager.IsEditModeActive || SwitchService.IsResettingSwitches) return;

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
            if (__instance == null || EditorSessionManager.IsEditModeActive || SwitchService.IsResettingSwitches) return;

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