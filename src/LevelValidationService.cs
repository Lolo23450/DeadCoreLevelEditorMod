using System;
using System.Collections.Generic;
using UnityEngine;
using Il2Cpp;

namespace DeadCoreEditor
{
    public enum LevelValidationSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    public class LevelValidationIssue
    {
        public LevelValidationSeverity Severity;
        public string Code;
        public string Message;
        public GameObject TargetObject;

        public LevelValidationIssue(LevelValidationSeverity severity, string code, string message, GameObject targetObject = null)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            TargetObject = targetObject;
        }
    }

    public static class LevelValidationService
    {
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        public static List<LevelValidationIssue> ValidateCurrentLevel()
        {
            List<LevelValidationIssue> issues = new List<LevelValidationIssue>();
            List<GameObject> objects = EditorSessionManager.PlacedObjects ?? new List<GameObject>();
            HashSet<GameObject> uniqueObjects = new HashSet<GameObject>();
            HashSet<int> checkpointIds = new HashSet<int>();
            int spawnCount = 0;
            int goalCount = 0;
            int validObjectCount = 0;

            for (int i = 0; i < objects.Count; i++)
            {
                GameObject obj = objects[i];
                if (obj == null)
                {
                    issues.Add(new LevelValidationIssue(LevelValidationSeverity.Error, "NULL_OBJECT", $"Placed object entry #{i + 1} is missing."));
                    continue;
                }

                if (!uniqueObjects.Add(obj))
                {
                    issues.Add(new LevelValidationIssue(LevelValidationSeverity.Error, "DUPLICATE_REFERENCE", $"'{obj.name}' appears more than once in the placed-object registry.", obj));
                    continue;
                }

                validObjectCount++;

                try
                {
                Vector3 position = obj.transform.position;
                Vector3 scale = obj.transform.localScale;
                Quaternion rotation = obj.transform.rotation;
                if (string.IsNullOrWhiteSpace(obj.name))
                    issues.Add(new LevelValidationIssue(LevelValidationSeverity.Warning, "EMPTY_OBJECT_NAME", "A placed object has an empty name.", obj));

                if (!IsFinite(position) || !IsFinite(scale) || !IsFinite(rotation))
                {
                    issues.Add(new LevelValidationIssue(LevelValidationSeverity.Error, "NON_FINITE_TRANSFORM", $"'{obj.name}' has a NaN or infinite transform value.", obj));
                }
                else if (Mathf.Abs(scale.x) < 0.0001f || Mathf.Abs(scale.y) < 0.0001f || Mathf.Abs(scale.z) < 0.0001f)
                {
                    issues.Add(new LevelValidationIssue(LevelValidationSeverity.Warning, "ZERO_SCALE", $"'{obj.name}' has a near-zero scale on at least one axis.", obj));
                }

                if (obj.transform.parent != null && !EditorSessionManager.PlacedObjects.Contains(obj.transform.parent.gameObject))
                    issues.Add(new LevelValidationIssue(LevelValidationSeverity.Warning, "EXTERNAL_PARENT", $"'{obj.name}' is parented to an object outside this level's placed-object registry. That parent relationship will not be saved.", obj));

                Component[] attachedComponents = obj.GetComponents<Component>();
                if (attachedComponents != null)
                {
                    for (int c = 0; c < attachedComponents.Length; c++)
                    {
                        if (attachedComponents[c] == null)
                        {
                            issues.Add(new LevelValidationIssue(LevelValidationSeverity.Warning, "MISSING_COMPONENT", $"'{obj.name}' has a missing Unity component reference.", obj));
                            break;
                        }
                    }
                }

                if (EditorSessionManager.PlacedObjectTypes.TryGetValue(obj, out PlacedObjectType objectType))
                {
                    if (objectType == PlacedObjectType.SpawnGate) spawnCount++;
                    if (objectType == PlacedObjectType.GoalGate) goalCount++;

                    if (objectType == PlacedObjectType.Checkpoint)
                    {
                        CheckPointScript checkpoint = obj.GetComponentInChildren<CheckPointScript>(true);
                        if (checkpoint == null)
                        {
                            issues.Add(new LevelValidationIssue(LevelValidationSeverity.Warning, "MISSING_CHECKPOINT_COMPONENT", $"Checkpoint '{obj.name}' has no CheckPointScript component.", obj));
                        }
                        else if (!checkpointIds.Add(checkpoint._id))
                        {
                            issues.Add(new LevelValidationIssue(LevelValidationSeverity.Warning, "DUPLICATE_CHECKPOINT_ID", $"Checkpoint ID {checkpoint._id} is used more than once.", obj));
                        }
                    }
                }
                else
                {
                    issues.Add(new LevelValidationIssue(LevelValidationSeverity.Warning, "UNCLASSIFIED_OBJECT", $"'{obj.name}' is placed but has no registered editor type.", obj));
                }

                Rigidbody rigidbody = obj.GetComponent<Rigidbody>();
                if (rigidbody != null)
                {
                    if (!IsFinite(rigidbody.mass) || rigidbody.mass <= 0f ||
                        !IsFinite(rigidbody.drag) || rigidbody.drag < 0f ||
                        !IsFinite(rigidbody.angularDrag) || rigidbody.angularDrag < 0f)
                    {
                        issues.Add(new LevelValidationIssue(LevelValidationSeverity.Error, "INVALID_RIGIDBODY", $"'{obj.name}' has invalid Rigidbody mass or drag values.", obj));
                    }
                }

                EditorEntityData validationData = EditorSessionManager.ExtractEntityData(obj);
                if (validationData != null)
                {
                    if (validationData.TryGetComponent<AudioConfig>(out AudioConfig audio))
                    {
                        if (!IsFinite(audio.MusicVolume) || !IsFinite(audio.AmbientVolume) || !IsFinite(audio.MusicPitch) ||
                            audio.MusicVolume < 0f || audio.MusicVolume > 1f || audio.AmbientVolume < 0f || audio.AmbientVolume > 1f || audio.MusicPitch <= 0f)
                            issues.Add(new LevelValidationIssue(LevelValidationSeverity.Error, "INVALID_AUDIO_CONFIG", $"'{obj.name}' has invalid audio volume or pitch settings.", obj));
                    }
                    if (validationData.TryGetComponent<LightConfig>(out LightConfig light))
                    {
                        if (!IsFinite(light.Intensity) || !IsFinite(light.Range) || !IsFinite(light.SpotAngle) || !IsFinite(light.VolumetricIntensity) ||
                            light.Intensity < 0f || light.Range < 0f || light.SpotAngle < 0f || light.VolumetricIntensity < 0f)
                            issues.Add(new LevelValidationIssue(LevelValidationSeverity.Error, "INVALID_LIGHT_CONFIG", $"'{obj.name}' has invalid light intensity, range, angle or volumetric settings.", obj));
                    }
                }

                if (EditorSessionManager.MotionPaths.TryGetValue(obj, out ObjectMotionPath path) && path != null)
                {
                    if (!path.IsActive)
                        issues.Add(new LevelValidationIssue(LevelValidationSeverity.Warning, "INACTIVE_MOTION_PATH", $"'{obj.name}' has a motion path entry that is marked inactive.", obj));
                    if (!IsFinite(path.PointA) || !IsFinite(path.PointB) || !IsFinite(path.Speed) || !IsFinite(path.RotationSpeed))
                    {
                        issues.Add(new LevelValidationIssue(LevelValidationSeverity.Error, "INVALID_MOTION_PATH", $"'{obj.name}' has non-finite motion-path values.", obj));
                    }
                    else if (path.Speed <= 0.01f && Mathf.Abs(path.RotationSpeed) <= 0.01f)
                    {
                        issues.Add(new LevelValidationIssue(LevelValidationSeverity.Error, "STALLED_MOTION_PATH", $"'{obj.name}' has a motion path with no movement or rotation.", obj));
                    }
                    else if (path.TotalDistance < 0.05f && Mathf.Abs(path.RotationSpeed) <= 0.01f)
                    {
                        issues.Add(new LevelValidationIssue(LevelValidationSeverity.Warning, "SHORT_MOTION_PATH", $"'{obj.name}' has an almost zero-length motion path.", obj));
                    }
                }

                if (EditorSessionManager.EntityRegistry.TryGetValue(obj, out EditorEntityData entityData) && entityData != null && entityData.Components != null)
                {
                    foreach (KeyValuePair<string, IEditorComponent> component in entityData.Components)
                    {
                        if (component.Value == null)
                            issues.Add(new LevelValidationIssue(LevelValidationSeverity.Warning, "NULL_COMPONENT", $"'{obj.name}' has an empty '{component.Key}' component entry.", obj));
                    }
                }
                }
                catch (Exception ex)
                {
                    issues.Add(new LevelValidationIssue(LevelValidationSeverity.Warning, "INSPECTION_FAILED",
                        $"Could not fully inspect '{obj.name}': {ex.GetType().Name}: {ex.Message}", obj));
                }
            }

            if (validObjectCount == 0)
                issues.Add(new LevelValidationIssue(LevelValidationSeverity.Error, "EMPTY_LEVEL", "The level contains no valid placed objects."));

            if (spawnCount == 0)
                issues.Add(new LevelValidationIssue(LevelValidationSeverity.Error, "MISSING_SPAWN", "No Spawn Gate was found. Add a level-entry Spawn Gate."));
            else if (spawnCount > 1)
                issues.Add(new LevelValidationIssue(LevelValidationSeverity.Warning, "MULTIPLE_SPAWNS", $"Found {spawnCount} Spawn Gates. Check which one should be the level entry."));

            if (goalCount == 0)
                issues.Add(new LevelValidationIssue(LevelValidationSeverity.Error, "MISSING_GOAL", "No Goal Gate was found. Add a level-finish Goal Gate."));
            else if (goalCount > 1)
                issues.Add(new LevelValidationIssue(LevelValidationSeverity.Warning, "MULTIPLE_GOALS", $"Found {goalCount} Goal Gates. Check which one should finish the level."));

            if (issues.Count == 0)
                issues.Add(new LevelValidationIssue(LevelValidationSeverity.Info, "CLEAN", "No validation issues found. The level passed all current checks."));

            return issues;
        }

        public static int CountSeverity(List<LevelValidationIssue> issues, LevelValidationSeverity severity)
        {
            if (issues == null) return 0;
            int count = 0;
            for (int i = 0; i < issues.Count; i++)
                if (issues[i] != null && issues[i].Severity == severity) count++;
            return count;
        }
    }
}
