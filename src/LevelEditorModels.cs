using System;
using System.IO;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(DeadCoreEditor.DeadCoreLevelEditorMod), "DeadCore Level Editor Suite - Studio Edition", "9.6.0", "Trufa")]
[assembly: MelonGame(null, null)]

namespace DeadCoreEditor
{
    public enum AssetCategory { Building = 0, Gameplay = 1 }

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

    public enum EditorGizmoMode { Select = 0, Translate = 1, Rotate = 2, Scale = 3 }
    public enum EditorInteractionMode { SelectMode = 0, PlacementMode = 1 }

    public enum HistoryActionType
    {
        Placement,
        Deletion,
        Parenting,
        MotionPath,
        Reposition,
        ParameterChange
    }

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
    }

    public class ObjectMotionPath
    {
        public Vector3 PointA;
        public Vector3 PointB;
        public float Speed = 3.5f;
        public bool IsActive = true;
        public Collider[] CachedColliders = null;

        public float TotalDistance => Vector3.Distance(PointA, PointB);
        public float TravelDuration => TotalDistance / Mathf.Max(0.1f, Speed);

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

        public Vector3 EvaluatePosition(float normalizedPingPong)
        {
            float smooth = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(normalizedPingPong));
            return Vector3.Lerp(PointA, PointB, smooth);
        }
    }

    public class HistoryRecord
    {
        public HistoryActionType ActionType;
        public GameObject TargetObject;
        public CatalogAsset Asset;
        public string AssetName;

        public Vector3 Position;
        public Quaternion Rotation;
        public float Scale;
        public float CustomParameter;

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

    public class LevelMetadata
    {
        public string Title = "Untitled Level";
        public string Author = "Unknown";
        public string Difficulty = "Normal";
        public string Description = "No description provided.";
        public string StagingScene = "level01_Spark01";
    }

    // =========================================================================
    // SECTION 3: AUTOMATED 3D ISOMETRIC LEVEL THUMBNAIL SERVICE
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

                // Dimmed, modern studio backdrop
                cam.clearFlags = CameraClearFlags.Color;
                cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 1f);
                cam.cullingMask = ~LayerMask.GetMask("Ignore Raycast");

                // True isometric simulation with a low FOV (18 deg)
                cam.fieldOfView = 18f;
                cam.nearClipPlane = 0.5f;
                cam.farClipPlane = 5000f;

                float maxDim = Mathf.Max(combinedBounds.size.x, combinedBounds.size.y, combinedBounds.size.z);
                float cameraDist = Mathf.Max(35f, maxDim * 2.8f);

                // Exact 3/4 isometric coordinates (35.264° pitch, 45° azimuth)
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