using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using SceneManager = UnityEngine.SceneManagement.SceneManager;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using Il2CppDeadCore;
using Il2CppDeadCore.UI;

using File = System.IO.File;
using Directory = System.IO.Directory;
using Path = System.IO.Path;

[assembly: MelonInfo(typeof(DeadCoreEditor.DeadCoreLevelEditorMod), "DeadCore Level Editor Suite - Studio Edition", "9.5.0", "Trufa")]
[assembly: MelonGame(null, null)]

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 1: STUDIO ENUMS & WORKFLOW STATES
    // =========================================================================

    /// <summary>
    /// Top-level category for assets cataloged by the level editor.
    /// </summary>
    public enum AssetCategory
    {
        Building = 0,
        Gameplay = 1
    }

    /// <summary>
    /// Specific gameplay and mechanical identification for placed entities.
    /// Used by contextual inspectors and runtime simulation logic.
    /// </summary>
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
        Checkpoint
    }

    /// <summary>
    /// Interactive 3D Viewport transformation tool states (Unity Editor Style).
    /// </summary>
    public enum EditorGizmoMode
    {
        Select = 0,
        Translate = 1,
        Rotate = 2,
        Scale = 3
    }

    /// <summary>
    /// Primary user interaction mode.
    /// SelectMode: Allows entity picking and manipulation using 3D handles without placing blocks.
    /// PlacementMode: Equips holographic preview and forces placement adjacent to existing geometry.
    /// </summary>
    public enum EditorInteractionMode
    {
        SelectMode = 0,
        PlacementMode = 1
    }

    /// <summary>
    /// Records discrete modifications for the multi-level Undo/Redo stack.
    /// </summary>
    public enum HistoryActionType
    {
        Placement,
        Deletion,
        Parenting,
        MotionPath,
        Reposition,
        ParameterChange
    }

    // =========================================================================
    // SECTION 2: DATA STRUCTURES & MODEL CONTAINERS
    // =========================================================================

    /// <summary>
    /// Defines an asset registered in the library palette.
    /// Holds reference templates, filter meshes, classifications, and render settings.
    /// </summary>
    public class CatalogAsset
    {
        public string DisplayName;
        public GameObject SourceTemplate;
        public Mesh FilterMesh;
        public AssetCategory Category;
        public string SubCategory = "Architecture";

        public Sprite ThumbnailSprite = null;
        public Texture2D ThumbnailTexture = null;

        public bool IsJumper;
        public bool IsCheckPoint;
        public bool IsSpawnGate;
        public bool IsGoalGate;
        public bool IsTurret;
        public bool IsHelix;
        public bool IsSpotlight;
        public bool IsSunlight;
        public bool IsLaser;
        public bool IsRotatingLaser;

        public float DefaultScale = 1.0f;
        public float VerticalOffset = 0.0f;
        public Quaternion BaseRotation = Quaternion.identity;

        /// <summary>
        /// Retrieves an estimated bounding volume for this asset.
        /// </summary>
        public Bounds GetEstimatedBounds()
        {
            if (FilterMesh != null)
            {
                return FilterMesh.bounds;
            }
            if (SourceTemplate != null)
            {
                var mf = SourceTemplate.GetComponentInChildren<MeshFilter>(true);
                if (mf != null && mf.sharedMesh != null)
                {
                    return mf.sharedMesh.bounds;
                }
            }
            return new Bounds(Vector3.zero, Vector3.one * 2f);
        }

        public override string ToString()
        {
            return $"[{Category}/{SubCategory}] {DisplayName} (Scale: {DefaultScale:F2})";
        }
    }

    /// <summary>
    /// Defines a continuous kinematic movement path between two world coordinates.
    /// </summary>
    public class ObjectMotionPath
    {
        public Vector3 PointA;
        public Vector3 PointB;
        public float Speed = 3.5f;
        public bool IsActive = true;
        public Collider[] CachedColliders = null;

        public float TotalDistance => Vector3.Distance(PointA, PointB);

        public float TravelDuration
        {
            get
            {
                float spd = Mathf.Max(0.1f, Speed);
                return TotalDistance / spd;
            }
        }

        public ObjectMotionPath Clone()
        {
            return new ObjectMotionPath
            {
                PointA = this.PointA,
                PointB = this.PointB,
                Speed = this.Speed,
                IsActive = this.IsActive,
                CachedColliders = null
            };
        }

        /// <summary>
        /// Calculates the interpolated target position along the path given a normalized ping-pong time t [0, 1].
        /// </summary>
        public Vector3 EvaluatePosition(float normalizedPingPong)
        {
            float smooth = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(normalizedPingPong));
            return Vector3.Lerp(PointA, PointB, smooth);
        }
    }

    /// <summary>
    /// Complete snapshot record of a user modification for Undo/Redo operations.
    /// </summary>
    public class HistoryRecord
    {
        public HistoryActionType ActionType;
        public GameObject TargetObject;
        public CatalogAsset Asset;
        public string AssetName;

        // Transform properties
        public Vector3 Position;
        public Quaternion Rotation;
        public float Scale;

        // Parameter properties
        public float CustomParameter;

        // Hierarchy parenting references
        public GameObject PreviousParent;
        public GameObject NewParent;

        // Motion path properties
        public ObjectMotionPath PreviousMotionPath;
        public ObjectMotionPath NewMotionPath;

        // Delta transform changes (used for gizmo modifications)
        public Vector3 PreviousPosition;
        public Vector3 NewPosition;
        public Quaternion PreviousRotation;
        public Quaternion NewRotation;
        public Vector3 PreviousScale;
        public Vector3 NewScale;

        public override string ToString()
        {
            return $"[History: {ActionType}] Target: {(TargetObject != null ? TargetObject.name : AssetName)}";
        }
    }

    /// <summary>
    /// Runtime light configuration parameters for Tech Spotlights and Directional Suns.
    /// </summary>
    public class LightConfig
    {
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
    }

    /// <summary>
    /// Metadata header embedded into exported level files.
    /// </summary>
    public class LevelMetadata
    {
        public string Title = "Untitled Level";
        public string Author = "Unknown";
        public string Difficulty = "Normal";
        public string Description = "No description provided.";
        public string StagingScene = "level01_Spark01";

        public void Sanitize()
        {
            if (string.IsNullOrWhiteSpace(Title)) Title = "Untitled Level";
            if (string.IsNullOrWhiteSpace(Author)) Author = "Unknown";
            if (string.IsNullOrWhiteSpace(Difficulty)) Difficulty = "Normal";
            if (string.IsNullOrWhiteSpace(Description)) Description = "No description provided.";
            if (string.IsNullOrWhiteSpace(StagingScene)) StagingScene = "level01_Spark01";

            Title = Title.Replace(';', '_').Replace('\n', ' ').Trim();
            Author = Author.Replace(';', '_').Replace('\n', ' ').Trim();
            Difficulty = Difficulty.Replace(';', '_').Replace('\n', ' ').Trim();
            Description = Description.Replace(';', '_').Replace('\n', ' ').Trim();
        }
    }

    // =========================================================================
    // SECTION 3: AUTOMATED 3D ISOMETRIC LEVEL THUMBNAIL SERVICE
    // =========================================================================

    /// <summary>
    /// Automates the creation of 3/4 semi-overhead diagonal level screenshots upon save.
    /// Resolves RawImage vs Image texture compatibility and provides encoded PNG snapshots.
    /// </summary>
    public static class ThumbnailCaptureService
    {
        private static GameObject _studioCamObj = null;

        /// <summary>
        /// Evaluates all placed scene geometry, computes bounding extents, positions an offscreen
        /// camera at a 3/4 diagonal semi-overhead aerial angle, and renders a PNG thumbnail to disk.
        /// </summary>
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

                cam.clearFlags = CameraClearFlags.Skybox;
                cam.cullingMask = ~LayerMask.GetMask("Ignore Raycast");
                cam.fieldOfView = 48f;
                cam.nearClipPlane = 0.5f;
                cam.farClipPlane = 4000f;

                float maxDim = Mathf.Max(combinedBounds.size.x, combinedBounds.size.y, combinedBounds.size.z);
                float cameraDistance = Mathf.Max(28f, maxDim * 1.55f);

                // 3/4 diagonal semi-overhead isometric vector (Elevated angle from corner)
                Vector3 directionalOffset = new Vector3(-cameraDistance * 0.85f, cameraDistance * 0.65f, -cameraDistance * 0.85f);
                _studioCamObj.transform.position = combinedBounds.center + directionalOffset;
                _studioCamObj.transform.LookAt(combinedBounds.center + Vector3.up * 1.2f);

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

                // Encode texture to PNG byte array
                byte[] pngBytes = ImageConversion.EncodeToPNG(outputTex);
                GameObject.DestroyImmediate(outputTex);

                string finalPngPath = Path.ChangeExtension(levelPath, ".png");
                File.WriteAllBytes(finalPngPath, pngBytes);

                MelonLogger.Msg($">> [Thumbnail] Captured 3/4 diagonal overhead snapshot: '{finalPngPath}' ({pngBytes.Length / 1024} KB)");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Thumbnail] Failed to capture 3D level snapshot: {ex.Message}");
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

        /// <summary>
        /// Reads an existing PNG snapshot from disk and returns a Texture2D.
        /// Useful for native UI elements that utilize RawImage (.texture).
        /// </summary>
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

        /// <summary>
        /// Reads an existing PNG snapshot from disk and returns a Sprite.
        /// Useful for modern uGUI elements that utilize Image (.sprite).
        /// </summary>
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

    // =========================================================================
    // SECTION 4: MATH & TRANSFORM EXTENSIONS
    // =========================================================================

    /// <summary>
    /// Spatial extension methods for alignment, discretization, and proxy snapping.
    /// </summary>
    public static class TransformMathExtensions
    {
        /// <summary>
        /// Normalizes angles to [0, 360) degrees.
        /// </summary>
        public static float NormalizeAngle(float angle)
        {
            return (angle % 360f + 360f) % 360f;
        }

        /// <summary>
        /// Normalizes Euler vector to [0, 360) degrees.
        /// </summary>
        public static Vector3 NormalizeAngles(Vector3 euler)
        {
            return new Vector3(
                NormalizeAngle(euler.x),
                NormalizeAngle(euler.y),
                NormalizeAngle(euler.z)
            );
        }

        /// <summary>
        /// Snaps a float value to the nearest discrete step.
        /// </summary>
        public static float SnapToGrid(float val, float step)
        {
            if (step <= 0.001f) return val;
            return Mathf.Round(val / step) * step;
        }

        /// <summary>
        /// Snaps a 3D vector to discrete coordinates along each axis.
        /// </summary>
        public static Vector3 SnapVectorToGrid(Vector3 pos, float step)
        {
            if (step <= 0.001f) return pos;
            return new Vector3(
                Mathf.Round(pos.x / step) * step,
                Mathf.Round(pos.y / step) * step,
                Mathf.Round(pos.z / step) * step
            );
        }
    }
}