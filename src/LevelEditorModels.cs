using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(DeadCoreEditor.DeadCoreLevelEditorMod), "DeadCore Level Editor Suite - Studio Edition", "9.6.1", "Trufa")]
[assembly: MelonGame(null, null)]

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 1: CORE ENUMS & TRAITS
    // =========================================================================

    public enum AssetCategory
    {
        Building = 0,
        Gameplay = 1,
        Lighting = 2,
        Hazards = 3,
        Instances = 4
    }

    [Flags]
    public enum AssetTrait
    {
        None = 0,
        Jumper = 1 << 0,
        Turbine = 1 << 1,
        Turret = 1 << 2,
        Laser = 1 << 3,
        RotatingLaser = 1 << 4,
        Checkpoint = 1 << 5,
        SpawnGate = 1 << 6,
        GoalGate = 1 << 7,
        Spotlight = 1 << 8,
        Sunlight = 1 << 9,
        Skybox = 1 << 10,
        Switch = 1 << 11,
        Architecture = 1 << 12
    }

    public enum PlacedObjectType
    {
        Generic = 0,
        Jumper,
        Turbine,
        Turret,
        SpawnGate,
        GoalGate,
        Sunlight,
        Spotlight,
        RotatingLaser,
        Laser,
        Checkpoint,
        SkyboxController,
        Switch
    }

    public enum EditorGizmoMode { Select = 0, Translate = 1, Rotate = 2, Scale = 3 }
    public enum EditorInteractionMode { SelectMode = 0, PlacementMode = 1 }

    public enum HistoryActionType
    {
        Placement,
        Deletion,
        Parenting,
        MotionPath,
        Reposition,
        ParameterChange,
        ComponentChange
    }

    public enum AssetSizeTier { All = 0, Small = 1, Medium = 2, Large = 3 }

    // =========================================================================
    // SECTION 2: COMPONENT ARCHITECTURE & ENTITY DATA
    // =========================================================================

    public interface IEditorComponent
    {
        string ComponentTag { get; }
        IEditorComponent Clone();
        string Serialize();
        void Deserialize(string rawData);
    }

    public class EditorEntityData
    {
        public Dictionary<string, IEditorComponent> Components = new Dictionary<string, IEditorComponent>(StringComparer.OrdinalIgnoreCase);

        public T Get<T>() where T : class, IEditorComponent
        {
            foreach (var comp in Components.Values)
            {
                if (comp is T typed) return typed;
            }
            return null;
        }
        public bool TryGetComponent<T>(out T comp) where T : class, IEditorComponent
        {
            comp = Get<T>();
            return comp != null;
        }

        public T GetOrCreate<T>() where T : class, IEditorComponent, new()
        {
            T existing = Get<T>();
            if (existing != null) return existing;

            T created = new T();
            Components[created.ComponentTag] = created;
            return created;
        }

        public void Set(IEditorComponent component)
        {
            if (component == null) return;
            Components[component.ComponentTag] = component;
        }

        public bool Has<T>() where T : class, IEditorComponent
        {
            return Get<T>() != null;
        }

        public bool Remove<T>() where T : class, IEditorComponent
        {
            string tagToRemove = null;
            foreach (var kvp in Components)
            {
                if (kvp.Value is T)
                {
                    tagToRemove = kvp.Key;
                    break;
                }
            }
            return tagToRemove != null && Components.Remove(tagToRemove);
        }

        public EditorEntityData Clone()
        {
            var clone = new EditorEntityData();
            foreach (var kvp in Components)
            {
                clone.Components[kvp.Key] = kvp.Value.Clone();
            }
            return clone;
        }
    }

    // =========================================================================
    // SECTION 3: CONCRETE CONFIGURATION COMPONENTS
    // =========================================================================

    public class JumperConfig : IEditorComponent
    {
        public string ComponentTag => "JUMPER";
        public float Force = 25.0f;
        public bool IsActive = true;

        public IEditorComponent Clone() => new JumperConfig { Force = this.Force, IsActive = this.IsActive };

        public string Serialize() => $"{Force.ToString("F2", CultureInfo.InvariantCulture)}:{(IsActive ? 1 : 0)}";

        public void Deserialize(string rawData)
        {
            if (string.IsNullOrWhiteSpace(rawData)) return;
            string[] parts = rawData.Split(':');
            if (parts.Length >= 1 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                Force = f;
            if (parts.Length >= 2)
                IsActive = parts[1] == "1";
        }
    }

    public class TurbineConfig : IEditorComponent
    {
        public string ComponentTag => "TURBINE";
        public float Speed = 35.0f;

        public IEditorComponent Clone() => new TurbineConfig { Speed = this.Speed };

        public string Serialize() => Speed.ToString("F2", CultureInfo.InvariantCulture);

        public void Deserialize(string rawData)
        {
            if (float.TryParse(rawData, NumberStyles.Float, CultureInfo.InvariantCulture, out float s))
                Speed = s;
        }
    }

    public class TurretConfig : IEditorComponent
    {
        public string ComponentTag => "TURRET";
        public float FireDelay = 1.0f;
        public float FirePower = 1500f;

        public IEditorComponent Clone() => new TurretConfig { FireDelay = this.FireDelay, FirePower = this.FirePower };

        public string Serialize() => $"{FireDelay.ToString("F2", CultureInfo.InvariantCulture)}:{FirePower.ToString("F1", CultureInfo.InvariantCulture)}";

        public void Deserialize(string rawData)
        {
            string[] parts = rawData.Split(':');
            if (parts.Length >= 1 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float fd))
                FireDelay = fd;
            if (parts.Length >= 2 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float fp))
                FirePower = fp;
        }
    }

    public class LaserConfig : IEditorComponent
    {
        public string ComponentTag => "LASER";
        public float RotationSpeed = 0f;
        public bool IsRotating = false;

        public IEditorComponent Clone() => new LaserConfig { RotationSpeed = this.RotationSpeed, IsRotating = this.IsRotating };

        public string Serialize() => $"{RotationSpeed.ToString("F2", CultureInfo.InvariantCulture)}:{(IsRotating ? 1 : 0)}";

        public void Deserialize(string rawData)
        {
            string[] parts = rawData.Split(':');
            if (parts.Length >= 1 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float rs))
                RotationSpeed = rs;
            if (parts.Length >= 2)
                IsRotating = parts[1] == "1" || Mathf.Abs(RotationSpeed) > 0.01f;
        }
    }

    public class GateConfig : IEditorComponent
    {
        public string ComponentTag => "GATE";
        public bool IsSpawn = false;
        public bool IsGoal = false;
        public int CheckpointId = 0;

        public IEditorComponent Clone() => new GateConfig { IsSpawn = this.IsSpawn, IsGoal = this.IsGoal, CheckpointId = this.CheckpointId };

        public string Serialize() => $"{(IsSpawn ? 1 : 0)}:{(IsGoal ? 1 : 0)}:{CheckpointId}";

        public void Deserialize(string rawData)
        {
            string[] parts = rawData.Split(':');
            if (parts.Length >= 1) IsSpawn = parts[0] == "1";
            if (parts.Length >= 2) IsGoal = parts[1] == "1";
            if (parts.Length >= 3 && int.TryParse(parts[2], out int id)) CheckpointId = id;
        }
    }

    public class LightConfig : IEditorComponent
    {
        public string ComponentTag => "LIGHT";
        public Color Color = Color.cyan;
        public float SpotAngle = 60f;
        public float Intensity = 8.0f;
        public float VolumetricIntensity = 4.0f;
        public bool IsDirectional = false;

        public LightConfig Clone()
        {
            return new LightConfig
            {
                Color = this.Color,
                SpotAngle = this.SpotAngle,
                Intensity = this.Intensity,
                VolumetricIntensity = this.VolumetricIntensity,
                IsDirectional = this.IsDirectional
            };
        }

        IEditorComponent IEditorComponent.Clone() => Clone();

        public string Serialize()
        {
            var inv = CultureInfo.InvariantCulture;
            byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(Color.r * 255f), 0, 255);
            byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(Color.g * 255f), 0, 255);
            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(Color.b * 255f), 0, 255);
            string hex = $"{r:X2}{g:X2}{b:X2}";
            return $"{Intensity.ToString("F2", inv)}:{SpotAngle.ToString("F1", inv)}:{hex}:{VolumetricIntensity.ToString("F2", inv)}:{(IsDirectional ? 1 : 0)}";
        }

        public void Deserialize(string rawData)
        {
            if (string.IsNullOrWhiteSpace(rawData)) return;
            string[] p = rawData.Split(':');
            var inv = CultureInfo.InvariantCulture;

            if (p.Length >= 1 && float.TryParse(p[0], NumberStyles.Float, inv, out float i)) Intensity = i;
            if (p.Length >= 2 && float.TryParse(p[1], NumberStyles.Float, inv, out float a)) SpotAngle = a;
            if (p.Length >= 3)
            {
                string hex = p[2].Trim().TrimStart('#');
                if (hex.Length >= 6 &&
                    byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, inv, out byte r) &&
                    byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, inv, out byte g) &&
                    byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, inv, out byte b))
                {
                    Color = new Color(r / 255f, g / 255f, b / 255f, 1f);
                }
            }
            if (p.Length >= 4 && float.TryParse(p[3], NumberStyles.Float, inv, out float v)) VolumetricIntensity = v;
            if (p.Length >= 5) IsDirectional = p[4] == "1";
        }
    }

    public class SkyboxConfig : IEditorComponent
    {
        public string ComponentTag => "SKYBOX";
        public Color TintColor = Color.white;
        public float Exposure = 1.0f;
        public Color GroundColor = new Color(0.04f, 0.08f, 0.15f, 1f);
        public bool HasGroundColor = false;

        public float YawOffset = 0.0f;
        public float SpinSpeed = 0.0f;
        public float CurrentAngle = 0.0f;

        public SkyboxConfig Clone()
        {
            return new SkyboxConfig
            {
                TintColor = this.TintColor,
                Exposure = this.Exposure,
                GroundColor = this.GroundColor,
                HasGroundColor = this.HasGroundColor,
                YawOffset = this.YawOffset,
                SpinSpeed = this.SpinSpeed,
                CurrentAngle = this.YawOffset
            };
        }

        IEditorComponent IEditorComponent.Clone() => Clone();

        public string Serialize()
        {
            var inv = CultureInfo.InvariantCulture;
            byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(TintColor.r * 255f), 0, 255);
            byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(TintColor.g * 255f), 0, 255);
            byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(TintColor.b * 255f), 0, 255);
            string hex = $"{r:X2}{g:X2}{b:X2}";
            return $"{Exposure.ToString("F2", inv)}:{hex}:{YawOffset.ToString("F1", inv)}:{SpinSpeed.ToString("F2", inv)}";
        }

        public void Deserialize(string rawData)
        {
            if (string.IsNullOrWhiteSpace(rawData)) return;
            string[] p = rawData.Split(':');
            var inv = CultureInfo.InvariantCulture;

            if (p.Length >= 1 && float.TryParse(p[0], NumberStyles.Float, inv, out float exp)) Exposure = exp;
            if (p.Length >= 2)
            {
                string hex = p[1].Trim().TrimStart('#');
                if (hex.Length >= 6 &&
                    byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, inv, out byte r) &&
                    byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, inv, out byte g) &&
                    byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, inv, out byte b))
                {
                    TintColor = new Color(r / 255f, g / 255f, b / 255f, 1f);
                }
            }
            if (p.Length >= 3 && float.TryParse(p[2], NumberStyles.Float, inv, out float yaw)) YawOffset = yaw;
            if (p.Length >= 4 && float.TryParse(p[3], NumberStyles.Float, inv, out float spin)) SpinSpeed = spin;
            CurrentAngle = YawOffset;
        }
    }

    public class SwitchConfig : IEditorComponent
    {
        public string ComponentTag => "SWITCH";
        public float TimeBeforeSwitch = 4.0f;
        public bool IsAutoSwitch = true;
        public bool InvertChildren = true;
        public bool InitialStateOn = false;

        public SwitchConfig Clone()
        {
            return new SwitchConfig
            {
                TimeBeforeSwitch = this.TimeBeforeSwitch,
                IsAutoSwitch = this.IsAutoSwitch,
                InvertChildren = this.InvertChildren,
                InitialStateOn = this.InitialStateOn
            };
        }

        IEditorComponent IEditorComponent.Clone() => Clone();

        public string Serialize()
        {
            var inv = CultureInfo.InvariantCulture;
            return $"{TimeBeforeSwitch.ToString("F2", inv)}:{(IsAutoSwitch ? 1 : 0)}:{(InvertChildren ? 1 : 0)}:{(InitialStateOn ? 1 : 0)}";
        }

        public void Deserialize(string rawData)
        {
            if (string.IsNullOrWhiteSpace(rawData)) return;
            string[] p = rawData.Split(':');
            var inv = CultureInfo.InvariantCulture;

            if (p.Length >= 1 && float.TryParse(p[0], NumberStyles.Float, inv, out float t)) TimeBeforeSwitch = t;
            if (p.Length >= 2) IsAutoSwitch = p[1] == "1";
            if (p.Length >= 3) InvertChildren = p[2] == "1";
            if (p.Length >= 4) InitialStateOn = p[3] == "1";
        }
    }

    public class ObjectMotionPath : IEditorComponent
    {
        public string ComponentTag => "PATH";
        public Vector3 PointA = Vector3.zero;
        public Vector3 PointB = Vector3.zero;
        public float Speed = 3.5f;
        public float RotationSpeed = 0f;
        public int RotationAxis = 1;
        public Vector3 CustomAxis = Vector3.up;
        public bool IsActive = true;
        public Collider[] CachedColliders = null;

        public float TotalDistance => Vector3.Distance(PointA, PointB);
        public float TravelDuration => TotalDistance / Mathf.Max(0.1f, Speed);

        public Vector3 GetEffectiveLocalAxis(Transform t = null)
        {
            if (RotationAxis == 0) return Vector3.right;
            if (RotationAxis == 1) return Vector3.up;
            if (RotationAxis == 2) return Vector3.forward;
            if (RotationAxis == 3)
            {
                Vector3 pathDir = PointB - PointA;
                if (pathDir.sqrMagnitude > 0.0001f)
                {
                    pathDir.Normalize();
                    return (t != null) ? t.InverseTransformDirection(pathDir).normalized : pathDir;
                }
                return Vector3.up;
            }
            if (CustomAxis.sqrMagnitude > 0.0001f)
                return CustomAxis.normalized;

            return Vector3.up;
        }

        public ObjectMotionPath Clone()
        {
            return new ObjectMotionPath
            {
                PointA = this.PointA,
                PointB = this.PointB,
                Speed = this.Speed,
                RotationSpeed = this.RotationSpeed,
                RotationAxis = this.RotationAxis,
                CustomAxis = this.CustomAxis,
                IsActive = this.IsActive,
                CachedColliders = null
            };
        }

        IEditorComponent IEditorComponent.Clone() => Clone();

        public Vector3 EvaluatePosition(float normalizedPingPong)
        {
            if (TotalDistance < 0.001f) return PointA;
            float smooth = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(normalizedPingPong));
            return Vector3.Lerp(PointA, PointB, smooth);
        }

        public string Serialize()
        {
            var inv = CultureInfo.InvariantCulture;
            return $"{(IsActive ? 1 : 0)}:{Speed.ToString("F2", inv)}:" +
                   $"{PointA.x.ToString("F4", inv)}:{PointA.y.ToString("F4", inv)}:{PointA.z.ToString("F4", inv)}:" +
                   $"{PointB.x.ToString("F4", inv)}:{PointB.y.ToString("F4", inv)}:{PointB.z.ToString("F4", inv)}:" +
                   $"{RotationSpeed.ToString("F2", inv)}:{RotationAxis}:" +
                   $"{CustomAxis.x.ToString("F4", inv)}:{CustomAxis.y.ToString("F4", inv)}:{CustomAxis.z.ToString("F4", inv)}";
        }

        public void Deserialize(string rawData)
        {
            if (string.IsNullOrWhiteSpace(rawData)) return;
            string[] p = rawData.Split(':');
            var inv = CultureInfo.InvariantCulture;

            if (p.Length >= 8)
            {
                IsActive = p[0] == "1";
                float.TryParse(p[1], NumberStyles.Float, inv, out Speed);
                PointA = new Vector3(ParseFloat(p[2]), ParseFloat(p[3]), ParseFloat(p[4]));
                PointB = new Vector3(ParseFloat(p[5]), ParseFloat(p[6]), ParseFloat(p[7]));
            }
            if (p.Length >= 10)
            {
                float.TryParse(p[8], NumberStyles.Float, inv, out RotationSpeed);
                int.TryParse(p[9], out RotationAxis);
            }
            if (p.Length >= 13)
            {
                CustomAxis = new Vector3(ParseFloat(p[10]), ParseFloat(p[11]), ParseFloat(p[12]));
                if (CustomAxis.sqrMagnitude < 0.0001f) CustomAxis = Vector3.up;
            }
        }

        private static float ParseFloat(string s)
        {
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float val);
            return val;
        }
    }

    // =========================================================================
    // SECTION 4: CATALOG ASSETS & TRAIT MAPPING
    // =========================================================================

    public class CatalogAsset
    {
        public string DisplayName;
        public GameObject SourceTemplate;
        public Mesh FilterMesh;
        public AssetCategory Category;
        public string SubCategory = "Architecture";
        public AssetTrait Traits = AssetTrait.None;

        public Sprite ThumbnailSprite = null;
        public Texture2D ThumbnailTexture = null;

        public Vector3 Dimensions = Vector3.one * 2f;
        public float MaxDimension = 2f;
        public AssetSizeTier SizeTier = AssetSizeTier.Medium;

        public bool IsPrefabInstance = false;
        public CustomPrefabTemplate PrefabTemplate = null;

        public float DefaultScale = 1.0f;
        public float VerticalOffset = 0.0f;
        public Quaternion BaseRotation = Quaternion.identity;

        public bool IsJumper { get => (Traits & AssetTrait.Jumper) != 0; set => SetTrait(AssetTrait.Jumper, value); }
        public bool IsCheckPoint { get => (Traits & (AssetTrait.Checkpoint | AssetTrait.SpawnGate | AssetTrait.GoalGate)) != 0; set => SetTrait(AssetTrait.Checkpoint, value); }
        public bool IsSpawnGate { get => (Traits & AssetTrait.SpawnGate) != 0; set => SetTrait(AssetTrait.SpawnGate, value); }
        public bool IsGoalGate { get => (Traits & AssetTrait.GoalGate) != 0; set => SetTrait(AssetTrait.GoalGate, value); }
        public bool IsTurret { get => (Traits & AssetTrait.Turret) != 0; set => SetTrait(AssetTrait.Turret, value); }
        public bool IsHelix { get => (Traits & AssetTrait.Turbine) != 0; set => SetTrait(AssetTrait.Turbine, value); }
        public bool IsSpotlight { get => (Traits & AssetTrait.Spotlight) != 0; set => SetTrait(AssetTrait.Spotlight, value); }
        public bool IsSunlight { get => (Traits & AssetTrait.Sunlight) != 0; set => SetTrait(AssetTrait.Sunlight, value); }
        public bool IsLaser { get => (Traits & AssetTrait.Laser) != 0; set => SetTrait(AssetTrait.Laser, value); }
        public bool IsRotatingLaser { get => (Traits & AssetTrait.RotatingLaser) != 0; set => SetTrait(AssetTrait.RotatingLaser, value); }
        public bool IsSkybox { get => (Traits & AssetTrait.Skybox) != 0; set => SetTrait(AssetTrait.Skybox, value); }
        public bool IsSwitch { get => (Traits & AssetTrait.Switch) != 0; set => SetTrait(AssetTrait.Switch, value); }

        public void SetTrait(AssetTrait trait, bool enable)
        {
            if (enable) Traits |= trait;
            else Traits &= ~trait;
        }

        public Bounds GetEstimatedBounds()
        {
            if (FilterMesh != null) return FilterMesh.bounds;
            if (SourceTemplate != null)
            {
                var mf = SourceTemplate.GetComponentInChildren<MeshFilter>(true);
                if (mf != null && mf.sharedMesh != null) return mf.sharedMesh.bounds;
            }
            return new Bounds(Vector3.zero, Vector3.one * 2f);
        }

        public void ComputeSizeMetrics()
        {
            Bounds b = GetEstimatedBounds();
            Dimensions = b.size;
            MaxDimension = Mathf.Max(Dimensions.x, Mathf.Max(Dimensions.y, Dimensions.z));

            if (MaxDimension < 3.0f)
                SizeTier = AssetSizeTier.Small;
            else if (MaxDimension < 11.0f)
                SizeTier = AssetSizeTier.Medium;
            else
                SizeTier = AssetSizeTier.Large;
        }

        public string GetSizeBadgeText()
        {
            if (IsPrefabInstance) return "PREFAB";
            if (DisplayName.Contains("16x16") || DisplayName.Contains("16x2x16")) return "16x2x16";
            if (MaxDimension >= 10f) return $"{MaxDimension:F0}m";
            return $"{MaxDimension:F1}m";
        }
    }

    // =========================================================================
    // SECTION 5: PREFAB & CLIPBOARD CONTAINERS
    // =========================================================================

    public class PrefabInstanceItem
    {
        public string AssetName;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Vector3 LocalScale = Vector3.one;
        public EditorEntityData EntityData = new EditorEntityData();

        public float CustomParameter
        {
            get => EntityData.Get<JumperConfig>()?.Force ?? EntityData.Get<TurbineConfig>()?.Speed ?? EntityData.Get<TurretConfig>()?.FireDelay ?? EntityData.Get<LaserConfig>()?.RotationSpeed ?? 0f;
            set
            {
                if (EntityData.Has<JumperConfig>()) EntityData.Get<JumperConfig>().Force = value;
                else if (EntityData.Has<TurbineConfig>()) EntityData.Get<TurbineConfig>().Speed = value;
                else if (EntityData.Has<TurretConfig>()) EntityData.Get<TurretConfig>().FireDelay = value;
                else if (EntityData.Has<LaserConfig>()) EntityData.Get<LaserConfig>().RotationSpeed = value;
            }
        }

        public LightConfig LightCfg { get => EntityData.Get<LightConfig>(); set { if (value != null) EntityData.Set(value); } }
        public ObjectMotionPath MotionPath { get => EntityData.Get<ObjectMotionPath>(); set { if (value != null) EntityData.Set(value); } }
        public SwitchConfig SwitchCfg { get => EntityData.Get<SwitchConfig>(); set { if (value != null) EntityData.Set(value); } }
    }

    public class CustomPrefabTemplate
    {
        public string Name;
        public string FilePath;
        public List<PrefabInstanceItem> Items = new List<PrefabInstanceItem>();
        public GameObject SourceTemplate;
        public Bounds TotalBounds;
    }

    public class ClipboardItem
    {
        public string AssetName;
        public Vector3 RelativeOffset;
        public Quaternion Rotation;
        public Vector3 Scale = Vector3.one;
        public EditorEntityData EntityData = new EditorEntityData();

        public float CustomParameter
        {
            get => EntityData.Get<JumperConfig>()?.Force ?? EntityData.Get<TurbineConfig>()?.Speed ?? EntityData.Get<TurretConfig>()?.FireDelay ?? EntityData.Get<LaserConfig>()?.RotationSpeed ?? 0f;
            set
            {
                if (EntityData.Has<JumperConfig>()) EntityData.Get<JumperConfig>().Force = value;
                else if (EntityData.Has<TurbineConfig>()) EntityData.Get<TurbineConfig>().Speed = value;
                else if (EntityData.Has<TurretConfig>()) EntityData.Get<TurretConfig>().FireDelay = value;
                else if (EntityData.Has<LaserConfig>()) EntityData.Get<LaserConfig>().RotationSpeed = value;
            }
        }
        public LightConfig LightCfg { get => EntityData.Get<LightConfig>(); set { if (value != null) EntityData.Set(value); } }
        public ObjectMotionPath MotionPath { get => EntityData.Get<ObjectMotionPath>(); set { if (value != null) EntityData.Set(value); } }
        public SwitchConfig SwitchCfg { get => EntityData.Get<SwitchConfig>(); set { if (value != null) EntityData.Set(value); } }
    }

    // =========================================================================
    // SECTION 6: HISTORY ACTION RECORD
    // =========================================================================

    public class HistoryRecord
    {
        public HistoryActionType ActionType;
        public GameObject TargetObject;
        public CatalogAsset Asset;
        public string AssetName;

        public Vector3 Position;
        public Quaternion Rotation;
        public float Scale;
        public Vector3 ScaleVector = Vector3.one;
        public float CustomParameter;

        public EditorEntityData EntityDataSnapshot;

        public GameObject PreviousParent;
        public GameObject NewParent;

        public ObjectMotionPath PreviousMotionPath;
        public ObjectMotionPath NewMotionPath;

        public Vector3 PreviousPosition;
        public Vector3 NewPosition;
        public Quaternion PreviousRotation;
        public Quaternion NewRotation;
        public Vector3 PreviousScale;
        public Vector3 NewScale;
    }

    public class LevelMetadata
    {
        public string Title = "Untitled Level";
        public string Author = "Unknown";
        public string Difficulty = "Normal";
        public string Description = "No description provided.";
        public string StagingScene = "level01_Spark01";
    }

    // =========================================================================
    // SECTION 7: THUMBNAIL CAPTURE SERVICE
    // =========================================================================

    public static class ThumbnailCaptureService
    {
        private static GameObject _studioCamObj = null;

        public static void CaptureLevelThumbnail(string levelPath, List<GameObject> activePlacedObjects, Vector3 fallbackSpawnPos)
        {
            if (string.IsNullOrEmpty(levelPath)) return;

            try
            {
                Bounds combinedBounds = new Bounds(Vector3.zero, Vector3.zero);
                bool boundsInitialized = false;

                if (activePlacedObjects != null && activePlacedObjects.Count > 0)
                {
                    for (int i = 0; i < activePlacedObjects.Count; i++)
                    {
                        GameObject go = activePlacedObjects[i];
                        if (go == null || !go.activeSelf) continue;

                        Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
                        for (int c = 0; c < colliders.Length; c++)
                        {
                            Collider col = colliders[c];
                            if (col == null || col.gameObject.name == "Editor_Snapping_Proxy") continue;

                            if (!boundsInitialized)
                            {
                                combinedBounds = col.bounds;
                                boundsInitialized = true;
                            }
                            else
                            {
                                combinedBounds.Encapsulate(col.bounds);
                            }
                        }
                    }
                }

                if (!boundsInitialized)
                {
                    combinedBounds = new Bounds(fallbackSpawnPos, new Vector3(24f, 12f, 24f));
                }

                if (_studioCamObj == null)
                {
                    _studioCamObj = new GameObject("Studio_Thumbnail_Rendering_Camera");
                }

                Camera cam = _studioCamObj.GetComponent<Camera>();
                if (cam == null) cam = _studioCamObj.AddComponent<Camera>();

                cam.clearFlags = CameraClearFlags.Color;
                cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 1f);
                cam.cullingMask = ~LayerMask.GetMask("Ignore Raycast");

                cam.fieldOfView = 18f;
                cam.nearClipPlane = 0.5f;
                cam.farClipPlane = 5000f;

                float maxDim = Mathf.Max(combinedBounds.size.x, combinedBounds.size.y, combinedBounds.size.z);
                float cameraDist = Mathf.Max(35f, maxDim * 2.8f);

                float pitchRad = 35.264f * Mathf.Deg2Rad;
                float yawRad = 45f * Mathf.Deg2Rad;

                Vector3 isoOffset = new Vector3(
                    -Mathf.Cos(pitchRad) * Mathf.Sin(yawRad),
                    Mathf.Sin(pitchRad),
                    -Mathf.Cos(pitchRad) * Mathf.Cos(yawRad)
                ) * cameraDist;

                _studioCamObj.transform.position = combinedBounds.center + isoOffset;
                _studioCamObj.transform.LookAt(combinedBounds.center);

                int renderWidth = 512;
                int renderHeight = 320;
                RenderTexture rt = RenderTexture.GetTemporary(renderWidth, renderHeight, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                Texture2D outputTex = new Texture2D(renderWidth, renderHeight, TextureFormat.RGB24, false);
                outputTex.ReadPixels(new Rect(0, 0, renderWidth, renderHeight), 0, 0);
                outputTex.Apply();

                cam.targetTexture = null;
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);

                byte[] pngBytes = ImageConversion.EncodeToPNG(outputTex);
                GameObject.DestroyImmediate(outputTex);

                string finalPngPath = Path.ChangeExtension(levelPath, ".png");
                File.WriteAllBytes(finalPngPath, pngBytes);

                MelonLogger.Msg($">> [Thumbnail] Captured isometric snapshot: '{finalPngPath}' ({pngBytes.Length / 1024} KB)");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Thumbnail] Failed to capture isometric level snapshot: {ex.Message}");
            }
            finally
            {
                if (_studioCamObj != null)
                {
                    GameObject.Destroy(_studioCamObj);
                    _studioCamObj = null;
                }
            }
        }

        public static Texture2D LoadLevelTexture(string levelPath)
        {
            string pngPath = Path.ChangeExtension(levelPath, ".png");
            if (!File.Exists(pngPath)) return null;

            try
            {
                byte[] rawBytes = File.ReadAllBytes(pngPath);
                Texture2D loadedTex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (ImageConversion.LoadImage(loadedTex, rawBytes))
                {
                    loadedTex.name = Path.GetFileNameWithoutExtension(levelPath) + "_Thumbnail";
                    return loadedTex;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Thumbnail] Could not load texture from '{pngPath}': {ex.Message}");
            }

            return null;
        }

        public static Sprite LoadLevelSprite(string levelPath)
        {
            Texture2D tex = LoadLevelTexture(levelPath);
            if (tex == null) return null;

            return Sprite.Create(
                tex,
                new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f
            );
        }
    }
}