using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;
using Il2CppDeadCore;

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 1: GIZMO & VIEWPORT CONFIGURATION CONSTANTS
    // =========================================================================

    public static class GizmoConfig
    {
        public const float DefaultCameraSpeed = 24f;
        public const float FastCameraMultiplier = 3.5f;
        public const float SlowCameraMultiplier = 0.25f;

        public const float ArrowShaftLength = 0.9f;
        public const float ArrowTotalLength = 1.85f;
        public const float RotationRingRadius = 2.2f;
        public const float CenterBoxSize = 0.50f;

        public const float HandleHitRadius = 24f;
        public const float CenterBoxHitRadius = 18f;
        public const int RotationRingSegments = 36;
    }

    public static class GizmoMaterialCache
    {
        private static Material _matRed;
        private static Material _matGreen;
        private static Material _matBlue;
        private static Material _matYellow;

        public static Material Red => _matRed ??= CreateSolidMaterial(new Color(1f, 0.18f, 0.18f, 1f));
        public static Material Green => _matGreen ??= CreateSolidMaterial(new Color(0.18f, 0.95f, 0.28f, 1f));
        public static Material Blue => _matBlue ??= CreateSolidMaterial(new Color(0.2f, 0.60f, 1f, 1f));
        public static Material Yellow => _matYellow ??= CreateSolidMaterial(new Color(1f, 0.92f, 0.15f, 1f));

        public static Material CreateSolidMaterial(Color col)
        {
            Shader s = Shader.Find("HDRP/Unlit")
                ?? (EditorSessionManager.CachedSceneMaterial != null ? EditorSessionManager.CachedSceneMaterial.shader : null)
                ?? Shader.Find("HDRP/Lit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Standard");

            Material m = (s != null) ? new Material(s) : new Material(Shader.Find("Hidden/InternalErrorShader"));
            m.name = "Gizmo_Opaque_" + col.ToString();

            m.color = col;
            if (m.HasProperty("_Color")) m.SetColor("_Color", col);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_UnlitColor")) m.SetColor("_UnlitColor", col);
            if (m.HasProperty("_EmissiveColor")) m.SetColor("_EmissiveColor", col * 3.0f);
            if (m.HasProperty("_EmissionColor"))
            {
                m.SetColor("_EmissionColor", col * 3.0f);
                m.EnableKeyword("_EMISSION");
            }

            // Keep in standard opaque queue so HDRP forward pass reliably renders it
            m.renderQueue = 2999;
            return m;
        }

        public static void Clear()
        {
            if (_matRed != null) GameObject.Destroy(_matRed);
            if (_matGreen != null) GameObject.Destroy(_matGreen);
            if (_matBlue != null) GameObject.Destroy(_matBlue);
            if (_matYellow != null) GameObject.Destroy(_matYellow);

            _matRed = null;
            _matGreen = null;
            _matBlue = null;
            _matYellow = null;
        }
    }

    // =========================================================================
    // SECTION 2: STUDIO 3D VIEWPORT FLYCAM CONTROLLER
    // =========================================================================
    public static class EditorViewportCamera
    {
        public static Camera ViewportCamera => EditorSessionManager.PlayerCameraInstance ?? Camera.main;

        private static Transform _savedCameraParent = null;
        private static Vector3 _savedLocalPos = Vector3.zero;
        private static Quaternion _savedLocalRot = Quaternion.identity;
        private static float _yaw = 0f;
        private static float _pitch = 0f;
        private static bool _isCameraDetached = false;
        private static readonly List<MonoBehaviour> _disabledCameraScripts = new List<MonoBehaviour>();

        public static void InitializeCamera(Camera sourceCam)
        {
            Camera cam = sourceCam ?? Camera.main;
            if (cam == null) cam = GameObject.FindObjectOfType<Camera>();
            if (cam == null) return;

            EditorSessionManager.PlayerCameraInstance = cam;

            // 1. Disable DeadCore's native occlusion culling & layer culling.
            cam.useOcclusionCulling = false;
            cam.layerCullDistances = new float[32];
            cam.cullingMask = ~0;

            if (!_isCameraDetached)
            {
                _savedCameraParent = cam.transform.parent;
                _savedLocalPos = cam.transform.localPosition;
                _savedLocalRot = cam.transform.localRotation;

                float rawPitch = cam.transform.eulerAngles.x;
                if (rawPitch > 180f) rawPitch -= 360f;
                _pitch = Mathf.Clamp(rawPitch, -89f, 89f);
                _yaw = cam.transform.eulerAngles.y;

                // 2. Detach camera to fly freely
                cam.transform.SetParent(null, true);

                // 3. Disable camera-bound scripts (MouseLook, HeadBob, FPSCamera)
                _disabledCameraScripts.Clear();
                MonoBehaviour[] camScripts = cam.GetComponents<MonoBehaviour>();
                for (int i = 0; i < camScripts.Length; i++)
                {
                    MonoBehaviour mb = camScripts[i];
                    if (mb == null) continue;

                    string fullName = mb.GetIl2CppType().FullName;
                    if (fullName.Contains("HighDefinition") || fullName.Contains("AdditionalCameraData") || fullName.StartsWith("DeadCoreEditor"))
                        continue;

                    if (mb.enabled)
                    {
                        mb.enabled = false;
                        _disabledCameraScripts.Add(mb);
                    }
                }

                _isCameraDetached = true;
            }

            cam.enabled = true;
        }

        public static void FocusOnObject(GameObject target)
        {
            Camera cam = ViewportCamera;
            if (target == null || cam == null) return;

            Vector3 center = StudioGizmoController.GetObjectCenter(target);
            Bounds b = StudioGizmoController.GetObjectWorldBounds(target);
            float radius = Mathf.Max(b.extents.x, b.extents.y, b.extents.z, 2.5f);

            cam.transform.position = center - cam.transform.forward * (radius * 2.2f) + Vector3.up * (radius * 0.6f);
            cam.transform.LookAt(center);

            float rawPitch = cam.transform.eulerAngles.x;
            if (rawPitch > 180f) rawPitch -= 360f;
            _pitch = Mathf.Clamp(rawPitch, -89f, 89f);
            _yaw = cam.transform.eulerAngles.y;
        }

        public static void EnsureCameraConfiguration()
        {
            Camera cam = ViewportCamera;
            if (cam != null)
            {
                cam.enabled = true;
                cam.useOcclusionCulling = false;
            }
        }

        public static void UpdateCamera()
        {
            Camera cam = ViewportCamera;
            if (cam == null || !_isCameraDetached) return;

            bool isFlying = Input.GetMouseButton(1);
            if (isFlying)
            {
                GUIUtility.keyboardControl = 0;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;

                float mouseX = Input.GetAxis("Mouse X");
                float mouseY = Input.GetAxis("Mouse Y");

                _yaw += mouseX * 2.5f;
                _pitch -= mouseY * 2.5f;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);

                cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }
            else
            {
                if (Cursor.lockState != CursorLockMode.None)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }

            float speed = GizmoConfig.DefaultCameraSpeed;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                speed *= GizmoConfig.FastCameraMultiplier;
            }
            else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                speed *= GizmoConfig.SlowCameraMultiplier;
            }

            Vector3 moveDir = Vector3.zero;
            bool allowMove = isFlying || (!StudioUIManager.IsPointerOverUI() && GUIUtility.keyboardControl == 0);

            if (allowMove)
            {
                if (Input.GetKey(KeyCode.W)) moveDir += cam.transform.forward;
                if (Input.GetKey(KeyCode.S)) moveDir -= cam.transform.forward;
                if (Input.GetKey(KeyCode.D)) moveDir += cam.transform.right;
                if (Input.GetKey(KeyCode.A)) moveDir -= cam.transform.right;
                if (Input.GetKey(KeyCode.Space) || (Input.GetKey(KeyCode.E) && isFlying)) moveDir += Vector3.up;
                if (Input.GetKey(KeyCode.Q) && isFlying) moveDir -= Vector3.up;
            }

            if (EditorSessionManager.InteractionMode == EditorInteractionMode.SelectMode && !StudioUIManager.IsPointerOverUI())
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    float zoomSpeed = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 35f : 15f;
                    cam.transform.position += cam.transform.forward * (scroll * zoomSpeed);
                }
            }

            if (moveDir.sqrMagnitude > 0.001f)
            {
                cam.transform.position += moveDir.normalized * (speed * Time.deltaTime);
            }
        }

        public static void DestroyCamera()
        {
            Camera cam = ViewportCamera;
            if (cam != null && _isCameraDetached)
            {
                cam.transform.SetParent(_savedCameraParent, true);
                cam.transform.localPosition = _savedLocalPos;
                cam.transform.localRotation = _savedLocalRot;

                for (int i = 0; i < _disabledCameraScripts.Count; i++)
                {
                    if (_disabledCameraScripts[i] != null) _disabledCameraScripts[i].enabled = true;
                }
                _disabledCameraScripts.Clear();

                cam.useOcclusionCulling = true;
                _isCameraDetached = false;
            }
        }
    }

    // =========================================================================
    // SECTION 3: BOUNDARY SNAPPING ENGINE
    // =========================================================================

    public static class BoundarySnappingCalculator
    {
        public static float GetProjectedHalfExtent(Vector3 localExtents, Vector3 scale, Quaternion rotation, Vector3 normal)
        {
            Vector3 scaledExtents = new Vector3(
                Mathf.Abs(localExtents.x * scale.x),
                Mathf.Abs(localExtents.y * scale.y),
                Mathf.Abs(localExtents.z * scale.z)
            );

            Vector3 ux = rotation * Vector3.right;
            Vector3 uy = rotation * Vector3.up;
            Vector3 uz = rotation * Vector3.forward;

            return Mathf.Abs(Vector3.Dot(ux, normal)) * scaledExtents.x
                 + Mathf.Abs(Vector3.Dot(uy, normal)) * scaledExtents.y
                 + Mathf.Abs(Vector3.Dot(uz, normal)) * scaledExtents.z;
        }

        public static Vector3 GetWorldCenterOffset(Vector3 localCenter, Vector3 scale, Quaternion rotation)
        {
            return rotation * Vector3.Scale(localCenter, scale);
        }

        public static Vector3 CalculateBoundaryPlacement(
            Vector3 rawHitPos,
            Vector3 rawHitNormal,
            GameObject hitPlacedObj,
            Bounds ghostLocalBounds,
            Vector3 ghostScale,
            CatalogAsset asset,
            float gridSnap,
            out Quaternion finalRotation,
            out Vector3 stableNormal)
        {
            bool isGridActive = gridSnap > 0.01f;

            if (hitPlacedObj != null)
            {
                Quaternion rotA = hitPlacedObj.transform.rotation;
                Vector3 scaleA = hitPlacedObj.transform.lossyScale;
                Bounds boundsA = PlacementHologramController.CalculateOptimizedProxyBounds(hitPlacedObj);
                Vector3 centerA_world = hitPlacedObj.transform.position + GetWorldCenterOffset(boundsA.center, scaleA, rotA);
                Vector3 extentsA = Vector3.Scale(boundsA.extents, scaleA);

                Vector3 localHitPoint = Quaternion.Inverse(rotA) * (rawHitPos - centerA_world);

                float nx = localHitPoint.x / Mathf.Max(0.01f, extentsA.x);
                float ny = (localHitPoint.y / Mathf.Max(0.01f, extentsA.y)) * 1.15f;
                float nz = localHitPoint.z / Mathf.Max(0.01f, extentsA.z);

                float ax = Mathf.Abs(nx);
                float ay = Mathf.Abs(ny);
                float az = Mathf.Abs(nz);

                Vector3 localCardNormal;
                if (ay >= ax && ay >= az) localCardNormal = new Vector3(0f, Mathf.Sign(ny), 0f);
                else if (ax >= ay && ax >= az) localCardNormal = new Vector3(Mathf.Sign(nx), 0f, 0f);
                else localCardNormal = new Vector3(0f, 0f, Mathf.Sign(nz));

                Vector3 faceNormal_world = rotA * localCardNormal;
                stableNormal = faceNormal_world;

                finalRotation = PlacementHologramController.CalculateActiveRotation(asset, stableNormal);

                float extA = GetProjectedHalfExtent(boundsA.extents, scaleA, rotA, faceNormal_world);
                float extB = GetProjectedHalfExtent(ghostLocalBounds.extents, ghostScale, finalRotation, faceNormal_world);
                float contactDist = extA + extB;

                Vector3 faceCenter_world = centerA_world + faceNormal_world * contactDist;

                Vector3 t1Local, t2Local;
                if (Mathf.Abs(localCardNormal.y) > 0.5f)
                {
                    t1Local = Vector3.right;
                    t2Local = Vector3.forward;
                }
                else if (Mathf.Abs(localCardNormal.x) > 0.5f)
                {
                    t1Local = Vector3.up;
                    t2Local = Vector3.forward;
                }
                else
                {
                    t1Local = Vector3.right;
                    t2Local = Vector3.up;
                }

                Vector3 t1 = rotA * t1Local;
                Vector3 t2 = rotA * t2Local;

                Vector3 toHit = rawHitPos - faceCenter_world;
                float offset1 = Vector3.Dot(toHit, t1);
                float offset2 = Vector3.Dot(toHit, t2);

                if (isGridActive)
                {
                    offset1 = Mathf.Round(offset1 / gridSnap) * gridSnap;
                    offset2 = Mathf.Round(offset2 / gridSnap) * gridSnap;
                }

                Vector3 targetCenter_world = faceCenter_world + (t1 * offset1) + (t2 * offset2);
                Vector3 ghostCenterOffset_world = GetWorldCenterOffset(ghostLocalBounds.center, ghostScale, finalRotation);

                return targetCenter_world - ghostCenterOffset_world;
            }

            Vector3 contactNormal = rawHitNormal.normalized;
            if (contactNormal.y > 0.65f) contactNormal = Vector3.up;
            else if (contactNormal.y < -0.65f) contactNormal = Vector3.down;
            stableNormal = contactNormal;

            finalRotation = PlacementHologramController.CalculateActiveRotation(asset, stableNormal);

            float normalExtB = GetProjectedHalfExtent(ghostLocalBounds.extents, ghostScale, finalRotation, contactNormal);
            Vector3 contactCenter_world = rawHitPos + contactNormal * (normalExtB + 0.001f);

            if (isGridActive)
            {
                contactCenter_world = new Vector3(
                    Mathf.Round(contactCenter_world.x / gridSnap) * gridSnap,
                    Mathf.Round(contactCenter_world.y / gridSnap) * gridSnap,
                    Mathf.Round(contactCenter_world.z / gridSnap) * gridSnap
                );
            }

            Vector3 finalCenterOffset = GetWorldCenterOffset(ghostLocalBounds.center, ghostScale, finalRotation);
            return contactCenter_world - finalCenterOffset;
        }
    }

    // =========================================================================
    // SECTION 4: HOLOGRAPHIC PREVIEW & INTERACTION
    // =========================================================================

    public static class PlacementHologramController
    {
        private static GameObject _ghostInstance = null;
        public static GameObject GhostInstance => _ghostInstance;

        private static Bounds _cachedGhostLocalBounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        private static Vector3 _targetPosition = Vector3.zero;
        public static Vector3 TargetPosition => _targetPosition;
        private static Vector3 _currentStableNormal = Vector3.up;

        public static void SpawnHologram(CatalogAsset asset)
        {
            DestroyPreview();
            if (asset == null || asset.SourceTemplate == null) return;

            _ghostInstance = GameObject.Instantiate(asset.SourceTemplate);
            _ghostInstance.name = "Holographic_Ghost_Preview";
            _ghostInstance.layer = 2;

            foreach (var tr in _ghostInstance.GetComponentsInChildren<Transform>(true))
            {
                tr.gameObject.layer = 2;
            }

            foreach (var col in _ghostInstance.GetComponentsInChildren<Collider>(true))
            {
                GameObject.DestroyImmediate(col);
            }

            foreach (var mb in _ghostInstance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                GameObject.DestroyImmediate(mb);
            }

            EditorSessionManager.StripParticlesAndLights(_ghostInstance);

            _cachedGhostLocalBounds = CalculateOptimizedProxyBounds(_ghostInstance);
            _ghostInstance.transform.localScale = Vector3.one * EditorSessionManager.ActivePlacementScale;
            ApplyRotationToPreview();
            _ghostInstance.SetActive(true);
        }

        public static Bounds CalculateOptimizedProxyBounds(GameObject go)
        {
            if (go == null) return new Bounds(Vector3.zero, Vector3.one * 2f);

            MeshFilter[] mfs = go.GetComponentsInChildren<MeshFilter>(true);
            Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;

            for (int i = 0; i < mfs.Length; i++)
            {
                MeshFilter mf = mfs[i];
                if (mf == null || mf.sharedMesh == null) continue;
                string n = mf.gameObject.name.ToLowerInvariant();
                if (n.Contains("proxy") || n.Contains("highlight") || n.Contains("beacon") || n.Contains("volumetric")) continue;

                Bounds b = mf.sharedMesh.bounds;
                Vector3[] corners = new Vector3[8]
                {
                    new Vector3(b.min.x, b.min.y, b.min.z),
                    new Vector3(b.max.x, b.min.y, b.min.z),
                    new Vector3(b.min.x, b.max.y, b.min.z),
                    new Vector3(b.max.x, b.max.y, b.min.z),
                    new Vector3(b.min.x, b.min.y, b.max.z),
                    new Vector3(b.max.x, b.min.y, b.max.z),
                    new Vector3(b.min.x, b.max.y, b.max.z),
                    new Vector3(b.max.x, b.max.y, b.max.z)
                };

                for (int c = 0; c < 8; c++)
                {
                    Vector3 worldPt = mf.transform.TransformPoint(corners[c]);
                    Vector3 localPt = go.transform.InverseTransformPoint(worldPt);

                    if (!hasBounds)
                    {
                        localBounds = new Bounds(localPt, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localPt);
                    }
                }
            }

            return hasBounds ? localBounds : new Bounds(Vector3.zero, Vector3.one * 2f);
        }

        public static Quaternion CalculateActiveRotation(CatalogAsset asset, Vector3 surfaceNormal)
        {
            if (EditorSessionManager.AutoAlignToSurface)
            {
                Quaternion alignRot = Quaternion.FromToRotation(Vector3.up, surfaceNormal);
                float snappedYaw = Mathf.Round(EditorSessionManager.TargetYaw / 45f) * 45f;
                Quaternion yawRot = Quaternion.AngleAxis(snappedYaw, surfaceNormal);
                Quaternion baseOffset = (asset != null) ? asset.BaseRotation : Quaternion.identity;
                return yawRot * alignRot * baseOffset;
            }

            return EditorSessionManager.GetCurrentCombinedRotation(asset);
        }

        public static void ApplyRotationToPreview()
        {
            if (_ghostInstance != null)
            {
                _ghostInstance.transform.rotation = CalculateActiveRotation(EditorSessionManager.CurrentAsset, _currentStableNormal);
            }
        }

        public static void SetPlacementScale(float newScale)
        {
            newScale = Mathf.Clamp((float)Math.Round(newScale, 2), 0.05f, 25.0f);
            EditorSessionManager.ActivePlacementScale = newScale;

            if (_ghostInstance != null)
            {
                _ghostInstance.transform.localScale = Vector3.one * newScale;
            }

            EditorSessionManager.ShowNotification($"Placement Scale: {newScale:F2}x");
        }

        public static void UpdatePlacement()
        {
            if (!EditorSessionManager.IsBlockSelected || _ghostInstance == null || EditorViewportCamera.ViewportCamera == null) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                EditorSessionManager.SetInteractionMode(EditorInteractionMode.SelectMode);
                return;
            }

            float scaleStep = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 0.5f : 0.1f;
            if (Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Equals))
            {
                SetPlacementScale(EditorSessionManager.ActivePlacementScale + scaleStep);
            }
            else if (Input.GetKeyDown(KeyCode.KeypadMinus) || Input.GetKeyDown(KeyCode.Minus))
            {
                SetPlacementScale(EditorSessionManager.ActivePlacementScale - scaleStep);
            }

            if (!Input.GetMouseButton(1))
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    bool isShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    bool isCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

                    if (isShift || isCtrl)
                    {
                        float step = isShift ? 0.15f : 0.05f;
                        SetPlacementScale(EditorSessionManager.ActivePlacementScale + (scroll > 0f ? step : -step));
                    }
                }
            }

            if (StudioUIManager.IsPointerOverUI()) return;

            Ray ray = Input.GetMouseButton(1)
                ? EditorViewportCamera.ViewportCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : EditorViewportCamera.ViewportCamera.ScreenPointToRay(Input.mousePosition);

            int raycastMask = ~LayerMask.GetMask("Ignore Raycast");
            RaycastHit[] hits = Physics.RaycastAll(ray, 1500f, raycastMask, QueryTriggerInteraction.Ignore);

            bool hasHit = false;
            RaycastHit closestHit = default;
            float minDist = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit h = hits[i];
                if (h.collider == null || h.collider.gameObject.name == "Editor_Snapping_Proxy") continue;
                if (_ghostInstance != null && (h.collider.gameObject == _ghostInstance || h.collider.transform.IsChildOf(_ghostInstance.transform))) continue;

                if (h.distance < minDist)
                {
                    minDist = h.distance;
                    closestHit = h;
                    hasHit = true;
                }
            }

            Vector3 hitNormal = Vector3.up;
            Vector3 rawTargetPos;
            Collider hitCollider = null;

            if (hasHit)
            {
                rawTargetPos = closestHit.point;
                hitNormal = closestHit.normal;
                hitCollider = closestHit.collider;
            }
            else
            {
                Plane fallbackPlane = new Plane(Vector3.up, _targetPosition);
                if (fallbackPlane.Raycast(ray, out float enter))
                {
                    rawTargetPos = ray.GetPoint(Mathf.Min(enter, 35f));
                }
                else
                {
                    rawTargetPos = ray.origin + ray.direction * 14f;
                }
            }

            GameObject hitPlacedObj = null;
            Transform curr = (hitCollider != null) ? hitCollider.transform : null;
            while (curr != null)
            {
                if (EditorSessionManager.PlacedObjects.Contains(curr.gameObject))
                {
                    hitPlacedObj = curr.gameObject;
                    break;
                }
                curr = curr.parent;
            }

            Vector3 ghostScale = Vector3.one * EditorSessionManager.ActivePlacementScale;
            _targetPosition = BoundarySnappingCalculator.CalculateBoundaryPlacement(
                rawTargetPos,
                hitNormal,
                hitPlacedObj,
                _cachedGhostLocalBounds,
                ghostScale,
                EditorSessionManager.CurrentAsset,
                EditorSessionManager.CurrentGridSnap,
                out Quaternion targetRot,
                out _currentStableNormal
            );

            _ghostInstance.transform.position = _targetPosition;
            _ghostInstance.transform.rotation = targetRot;

            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !StudioUIManager.IsPointerOverUI())
            {
                CommitPlacement(targetRot);
            }
        }

        private static void CommitPlacement(Quaternion placementRotation)
        {
            CatalogAsset asset = EditorSessionManager.CurrentAsset;
            if (asset == null) return;

            float scale = EditorSessionManager.ActivePlacementScale;
            Vector3 placeScale = Vector3.one * scale;
            GameObject placed = EditorSessionManager.SpawnCatalogObject(asset, _targetPosition, placeScale, placementRotation);

            if (placed != null)
            {
                EditorSessionManager.RegisterPlacedObject(placed);
                EditorSessionManager.LastPlacedObject = placed;
                EditorSessionManager.SelectedObject = placed;

                if (EditorSessionManager.SelectedObjects != null)
                {
                    EditorSessionManager.SelectedObjects.Clear();
                    EditorSessionManager.SelectedObjects.Add(placed);
                }

                EditorSessionManager.UndoHistory.Push(new HistoryRecord
                {
                    ActionType = HistoryActionType.Placement,
                    TargetObject = placed,
                    Asset = asset,
                    AssetName = asset.DisplayName,
                    Position = _targetPosition,
                    Rotation = placementRotation,
                    Scale = scale,
                    ScaleVector = placeScale,
                    EntityDataSnapshot = EditorSessionManager.ExtractEntityData(placed)
                });
                EditorSessionManager.RedoHistory.Clear();

                EditorSessionManager.ShowNotification($"Placed '{asset.DisplayName}' ({scale:F2}x)");
            }
        }

        public static void DestroyPreview()
        {
            if (_ghostInstance != null)
            {
                GameObject.Destroy(_ghostInstance);
                _ghostInstance = null;
            }
        }
    }

    // =========================================================================
    // SECTION 5: 3D GIZMO CONTROLLER
    // =========================================================================

    public static class StudioGizmoController
    {
        private static GameObject _gizmoRoot = null;
        private static GameObject _groupTranslate = null;
        private static GameObject _groupRotate = null;
        private static GameObject _groupScale = null;

        private static int _activeDragAxis = -1; // 0=X, 1=Y, 2=Z, 3=Center
        public static bool IsDraggingGizmo => _activeDragAxis != -1;
        public static bool IsHoveringHandle = false;

        public static float GizmoScaleMultiplier = 1.0f;

        private static Vector3 _dragStartCenterPos = Vector3.zero;
        private static Vector2 _dragStartMousePos = Vector2.zero;
        private static float _lastCalculatedGizmoScale = 1.0f;

        private static readonly Dictionary<GameObject, Vector3> _dragStartPositions = new Dictionary<GameObject, Vector3>();
        private static readonly Dictionary<GameObject, Quaternion> _dragStartRotations = new Dictionary<GameObject, Quaternion>();
        private static readonly Dictionary<GameObject, Vector3> _dragStartScales = new Dictionary<GameObject, Vector3>();

        public static void AttachSunVisualWidget(GameObject sunObj)
        {
            if (sunObj == null) return;

            Transform old = sunObj.transform.Find("Sun_Editor_Widget");
            if (old != null) GameObject.DestroyImmediate(old.gameObject);

            GameObject widget = new GameObject("Sun_Editor_Widget");
            widget.transform.SetParent(sunObj.transform, false);
            widget.layer = 0;

            Color sunColor = new Color(1f, 0.88f, 0.35f, 1f);
            if (EditorSessionManager.PlacedLights.TryGetValue(sunObj, out var cfg))
            {
                sunColor = cfg.Color;
            }

            Material sunMat = GizmoMaterialCache.CreateSolidMaterial(sunColor);

            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            StripCollider(orb);
            orb.name = "Sun_Orb";
            orb.transform.SetParent(widget.transform, false);
            orb.transform.localScale = Vector3.one * 0.75f;
            orb.GetComponent<Renderer>().sharedMaterial = sunMat;

            GameObject ray = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            StripCollider(ray);
            ray.name = "Sun_Ray_Shaft";
            ray.transform.SetParent(widget.transform, false);
            ray.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ray.transform.localPosition = new Vector3(0f, 0f, 0.9f);
            ray.transform.localScale = new Vector3(0.12f, 0.65f, 0.12f);
            ray.GetComponent<Renderer>().sharedMaterial = sunMat;

            GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            StripCollider(tip);
            tip.name = "Sun_Ray_Tip";
            tip.transform.SetParent(widget.transform, false);
            tip.transform.localPosition = new Vector3(0f, 0f, 1.6f);
            tip.transform.localScale = Vector3.one * 0.28f;
            tip.GetComponent<Renderer>().sharedMaterial = sunMat;

            SphereCollider sc = widget.AddComponent<SphereCollider>();
            sc.radius = 0.9f;
            sc.isTrigger = false;

            widget.SetActive(EditorSessionManager.IsEditModeActive);
        }

        public static Bounds GetObjectWorldBounds(GameObject obj)
        {
            if (obj == null) return new Bounds(Vector3.zero, Vector3.one);
            Bounds b = PlacementHologramController.CalculateOptimizedProxyBounds(obj);
            Vector3 worldCenter = obj.transform.TransformPoint(b.center);
            Vector3 worldSize = Vector3.Scale(b.size, obj.transform.lossyScale);
            return new Bounds(worldCenter, worldSize);
        }

        public static Vector3 GetObjectCenter(GameObject obj)
        {
            if (obj == null) return Vector3.zero;
            Bounds b = PlacementHologramController.CalculateOptimizedProxyBounds(obj);
            return obj.transform.TransformPoint(b.center);
        }

        public static void InvalidateCachedCenter(GameObject obj)
        {
            // Center is evaluated directly from local proxy bounds without stale cache offsets
        }

        public static void ClearAllCachedCentroids()
        {
            // Center is evaluated directly from local proxy bounds without stale cache offsets
        }

        private static void StripCollider(GameObject go)
        {
            if (go == null) return;
            Collider c = go.GetComponent<Collider>();
            if (c != null) GameObject.DestroyImmediate(c);
        }

        private static void EnsureGizmoInstances()
        {
            if (_gizmoRoot != null) return;

            _gizmoRoot = new GameObject("Studio_3D_Gizmo_Root");
            _gizmoRoot.layer = 0;

            // 1. TRANSLATE
            _groupTranslate = new GameObject("Group_Translate");
            _groupTranslate.transform.SetParent(_gizmoRoot.transform, false);

            Create3DArrow(_groupTranslate.transform, "Arrow_X", Vector3.right, GizmoMaterialCache.Red);
            Create3DArrow(_groupTranslate.transform, "Arrow_Y", Vector3.up, GizmoMaterialCache.Green);
            Create3DArrow(_groupTranslate.transform, "Arrow_Z", Vector3.forward, GizmoMaterialCache.Blue);

            // 2. ROTATE
            _groupRotate = new GameObject("Group_Rotate");
            _groupRotate.transform.SetParent(_gizmoRoot.transform, false);

            float r = GizmoConfig.RotationRingRadius;
            Create3DLineRing(_groupRotate.transform, "Ring_X", Vector3.right, r, GizmoMaterialCache.Red);
            Create3DLineRing(_groupRotate.transform, "Ring_Y", Vector3.up, r, GizmoMaterialCache.Green);
            Create3DLineRing(_groupRotate.transform, "Ring_Z", Vector3.forward, r, GizmoMaterialCache.Blue);

            CreatePrimitiveObj(PrimitiveType.Sphere, _groupRotate.transform, "RotHandle_X", new Vector3(0f, 0f, r), Vector3.one * 0.55f, GizmoMaterialCache.Red);
            CreatePrimitiveObj(PrimitiveType.Sphere, _groupRotate.transform, "RotHandle_Y", new Vector3(r, 0f, 0f), Vector3.one * 0.55f, GizmoMaterialCache.Green);
            CreatePrimitiveObj(PrimitiveType.Sphere, _groupRotate.transform, "RotHandle_Z", new Vector3(0f, r, 0f), Vector3.one * 0.55f, GizmoMaterialCache.Blue);

            // 3. SCALE
            _groupScale = new GameObject("Group_Scale");
            _groupScale.transform.SetParent(_gizmoRoot.transform, false);

            Create3DScaleStem(_groupScale.transform, "Scale_X", Vector3.right, GizmoMaterialCache.Red);
            Create3DScaleStem(_groupScale.transform, "Scale_Y", Vector3.up, GizmoMaterialCache.Green);
            Create3DScaleStem(_groupScale.transform, "Scale_Z", Vector3.forward, GizmoMaterialCache.Blue);
            CreatePrimitiveObj(PrimitiveType.Cube, _groupScale.transform, "Center_ScaleBox", Vector3.zero, Vector3.one * GizmoConfig.CenterBoxSize, GizmoMaterialCache.Yellow);
        }

        private static GameObject Create3DArrow(Transform parent, string name, Vector3 dir, Material mat)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);

            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            StripCollider(shaft);
            shaft.name = "Shaft";
            shaft.transform.SetParent(root.transform, false);
            shaft.transform.localScale = new Vector3(0.18f, GizmoConfig.ArrowShaftLength, 0.18f);
            shaft.transform.localPosition = dir * GizmoConfig.ArrowShaftLength;
            shaft.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            shaft.GetComponent<Renderer>().sharedMaterial = mat;

            GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            StripCollider(tip);
            tip.name = "Tip";
            tip.transform.SetParent(root.transform, false);
            tip.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f);
            tip.transform.localPosition = dir * GizmoConfig.ArrowTotalLength;
            tip.GetComponent<Renderer>().sharedMaterial = mat;

            return root;
        }

        private static GameObject Create3DScaleStem(Transform parent, string name, Vector3 dir, Material mat)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);

            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            StripCollider(shaft);
            shaft.name = "Shaft";
            shaft.transform.SetParent(root.transform, false);
            shaft.transform.localScale = new Vector3(0.18f, GizmoConfig.ArrowShaftLength, 0.18f);
            shaft.transform.localPosition = dir * GizmoConfig.ArrowShaftLength;
            shaft.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            shaft.GetComponent<Renderer>().sharedMaterial = mat;

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            StripCollider(cube);
            cube.name = "TipCube";
            cube.transform.SetParent(root.transform, false);
            cube.transform.localScale = new Vector3(0.52f, 0.52f, 0.52f);
            cube.transform.localPosition = dir * GizmoConfig.ArrowTotalLength;
            cube.GetComponent<Renderer>().sharedMaterial = mat;

            return root;
        }

        private static GameObject Create3DLineRing(Transform parent, string name, Vector3 normalAxis, float radius, Material mat)
        {
            GameObject ring = new GameObject(name);
            ring.transform.SetParent(parent, false);

            LineRenderer lr = ring.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = GizmoConfig.RotationRingSegments;
            lr.startWidth = 0.15f;
            lr.endWidth = 0.15f;
            lr.sharedMaterial = mat;

            Quaternion rot = Quaternion.FromToRotation(Vector3.up, normalAxis);
            for (int i = 0; i < GizmoConfig.RotationRingSegments; i++)
            {
                float a = (i / (float)GizmoConfig.RotationRingSegments) * Mathf.PI * 2f;
                Vector3 pt = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                lr.SetPosition(i, rot * pt);
            }

            return ring;
        }

        private static GameObject CreatePrimitiveObj(PrimitiveType type, Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            StripCollider(go);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        public static float CalculateDynamicGizmoScale(Camera cam, Vector3 center3D, List<GameObject> targets)
        {
            if (cam == null) return 1.0f;

            float dist = Vector3.Distance(cam.transform.position, center3D);
            float fovRad = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float frustumHeightAtDist = 2.0f * dist * Mathf.Tan(fovRad);

            float distanceFalloff = Mathf.Lerp(1.25f, 0.30f, Mathf.InverseLerp(5f, 75f, dist));
            float baseScreenScale = frustumHeightAtDist * 0.080f * distanceFalloff;

            float maxObjExtent = 0f;
            for (int i = 0; i < targets.Count; i++)
            {
                GameObject go = targets[i];
                if (go == null) continue;

                Bounds b = GetObjectWorldBounds(go);
                float ext = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
                if (ext > maxObjExtent) maxObjExtent = ext;
            }

            float clearanceFade = Mathf.Clamp01(1.0f - (dist / 40f));
            float extentClearance = Mathf.Clamp(maxObjExtent * 0.12f, 0f, 6f) * clearanceFade;

            float finalScale = (baseScreenScale + extentClearance) * GizmoScaleMultiplier;
            return Mathf.Clamp(finalScale, 0.2f, 18f);
        }

        public static void UpdateGizmo()
        {
            if (!EditorSessionManager.IsEditModeActive || EditorSessionManager.InteractionMode != EditorInteractionMode.SelectMode)
            {
                if (_gizmoRoot != null) _gizmoRoot.SetActive(false);
                IsHoveringHandle = false;
                return;
            }

            if (Input.GetKeyDown(KeyCode.LeftArrow) && !Input.GetKey(KeyCode.LeftShift))
            {
                GizmoScaleMultiplier = Mathf.Max(0.4f, GizmoScaleMultiplier - 0.15f);
                EditorSessionManager.ShowNotification($"Gizmo Scale: {GizmoScaleMultiplier:F2}x");
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow) && !Input.GetKey(KeyCode.LeftShift))
            {
                GizmoScaleMultiplier = Mathf.Min(3.5f, GizmoScaleMultiplier + 0.15f);
                EditorSessionManager.ShowNotification($"Gizmo Scale: {GizmoScaleMultiplier:F2}x");
            }

            List<GameObject> activeList = new List<GameObject>();
            if (EditorSessionManager.SelectedObjects != null && EditorSessionManager.SelectedObjects.Count > 0)
            {
                for (int i = 0; i < EditorSessionManager.SelectedObjects.Count; i++)
                {
                    GameObject go = EditorSessionManager.SelectedObjects[i];
                    if (go != null && go.activeSelf) activeList.Add(go);
                }
            }
            else if (EditorSessionManager.SelectedObject != null && EditorSessionManager.SelectedObject.activeSelf)
            {
                activeList.Add(EditorSessionManager.SelectedObject);
            }

            if (activeList.Count == 0)
            {
                if (_gizmoRoot != null) _gizmoRoot.SetActive(false);
                IsHoveringHandle = false;
                return;
            }

            EnsureGizmoInstances();

            Camera cam = EditorViewportCamera.ViewportCamera;
            if (cam == null) return;

            Vector3 center3D;
            if (IsDraggingGizmo)
            {
                center3D = _dragStartCenterPos;
            }
            else
            {
                center3D = Vector3.zero;
                for (int i = 0; i < activeList.Count; i++) center3D += GetObjectCenter(activeList[i]);
                center3D /= activeList.Count;
                _gizmoRoot.transform.position = center3D;
            }

            _lastCalculatedGizmoScale = CalculateDynamicGizmoScale(cam, center3D, activeList);
            _gizmoRoot.transform.localScale = Vector3.one * _lastCalculatedGizmoScale;

            EditorGizmoMode mode = EditorSessionManager.CurrentGizmoMode;
            if (mode == EditorGizmoMode.Select) mode = EditorGizmoMode.Translate;

            _groupTranslate.SetActive(mode == EditorGizmoMode.Translate);
            _groupRotate.SetActive(mode == EditorGizmoMode.Rotate);
            _groupScale.SetActive(mode == EditorGizmoMode.Scale);
            _gizmoRoot.SetActive(true);

            int hovered = Check3DHover(cam, center3D, _lastCalculatedGizmoScale, mode);
            IsHoveringHandle = (hovered != -1 || _activeDragAxis != -1);

            Handle3DDragging(activeList, cam, center3D, hovered, mode);
        }

        private static int Check3DHover(Camera cam, Vector3 center3D, float gizmoScale, EditorGizmoMode mode)
        {
            if (StudioUIManager.IsPointerOverUI()) return -1;

            Vector3 sCenter = cam.WorldToScreenPoint(center3D);
            if (sCenter.z <= 0.1f) return -1;

            Vector2 mousePos = Input.mousePosition;
            float hitRadius = GizmoConfig.HandleHitRadius;

            if (mode == EditorGizmoMode.Translate)
            {
                float arrowLen = GizmoConfig.ArrowTotalLength * gizmoScale;
                Vector3 sX = cam.WorldToScreenPoint(center3D + Vector3.right * arrowLen);
                Vector3 sY = cam.WorldToScreenPoint(center3D + Vector3.up * arrowLen);
                Vector3 sZ = cam.WorldToScreenPoint(center3D + Vector3.forward * arrowLen);

                float dX = (sX.z > 0.1f) ? DistanceToSegment2D(mousePos, sCenter, sX) : 9999f;
                float dY = (sY.z > 0.1f) ? DistanceToSegment2D(mousePos, sCenter, sY) : 9999f;
                float dZ = (sZ.z > 0.1f) ? DistanceToSegment2D(mousePos, sCenter, sZ) : 9999f;

                float minD = Mathf.Min(dX, Mathf.Min(dY, dZ));
                if (minD < hitRadius)
                {
                    if (minD == dX) return 0;
                    if (minD == dY) return 1;
                    if (minD == dZ) return 2;
                }
            }
            else if (mode == EditorGizmoMode.Scale)
            {
                if (Vector2.Distance(mousePos, sCenter) < GizmoConfig.CenterBoxHitRadius)
                {
                    return 3;
                }

                float stemLen = GizmoConfig.ArrowTotalLength * gizmoScale;
                Vector3 sX = cam.WorldToScreenPoint(center3D + Vector3.right * stemLen);
                Vector3 sY = cam.WorldToScreenPoint(center3D + Vector3.up * stemLen);
                Vector3 sZ = cam.WorldToScreenPoint(center3D + Vector3.forward * stemLen);

                float dX = (sX.z > 0.1f) ? DistanceToSegment2D(mousePos, sCenter, sX) : 9999f;
                float dY = (sY.z > 0.1f) ? DistanceToSegment2D(mousePos, sCenter, sY) : 9999f;
                float dZ = (sZ.z > 0.1f) ? DistanceToSegment2D(mousePos, sCenter, sZ) : 9999f;

                float minD = Mathf.Min(dX, Mathf.Min(dY, dZ));
                if (minD < hitRadius)
                {
                    if (minD == dX) return 0;
                    if (minD == dY) return 1;
                    if (minD == dZ) return 2;
                }
            }
            else if (mode == EditorGizmoMode.Rotate)
            {
                float r = GizmoConfig.RotationRingRadius * gizmoScale;
                Vector3 sRotX = cam.WorldToScreenPoint(center3D + Vector3.forward * r);
                Vector3 sRotY = cam.WorldToScreenPoint(center3D + Vector3.right * r);
                Vector3 sRotZ = cam.WorldToScreenPoint(center3D + Vector3.up * r);

                float dX = (sRotX.z > 0.1f) ? Vector2.Distance(mousePos, sRotX) : 9999f;
                float dY = (sRotY.z > 0.1f) ? Vector2.Distance(mousePos, sRotY) : 9999f;
                float dZ = (sRotZ.z > 0.1f) ? Vector2.Distance(mousePos, sRotZ) : 9999f;

                float minD = Mathf.Min(dX, Mathf.Min(dY, dZ));
                if (minD < hitRadius + 4f)
                {
                    if (minD == dX) return 0;
                    if (minD == dY) return 1;
                    if (minD == dZ) return 2;
                }
            }

            return -1;
        }

        private static float DistanceToSegment2D(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 pa = p - a;
            Vector2 ba = b - a;
            float l2 = Vector2.Dot(ba, ba);
            if (l2 < 0.0001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(pa, ba) / l2);
            return Vector2.Distance(p, a + ba * t);
        }

        private static void Handle3DDragging(List<GameObject> targets, Camera cam, Vector3 center3D, int hovered, EditorGizmoMode mode)
        {
            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !StudioUIManager.IsPointerOverUI())
            {
                if (hovered != -1)
                {
                    _activeDragAxis = hovered;
                    _dragStartCenterPos = center3D;
                    _dragStartMousePos = Input.mousePosition;

                    _dragStartPositions.Clear();
                    _dragStartRotations.Clear();
                    _dragStartScales.Clear();

                    for (int i = 0; i < targets.Count; i++)
                    {
                        GameObject go = targets[i];
                        if (go != null)
                        {
                            _dragStartPositions[go] = go.transform.position;
                            _dragStartRotations[go] = go.transform.rotation;
                            _dragStartScales[go] = go.transform.localScale;
                        }
                    }
                }
            }

            if (Input.GetMouseButton(0) && _activeDragAxis != -1)
            {
                Vector3 axis3D = (_activeDragAxis == 0) ? Vector3.right : (_activeDragAxis == 1 ? Vector3.up : Vector3.forward);
                float grid = EditorSessionManager.CurrentGridSnap;

                List<GameObject> rootTargets = new List<GameObject>();
                for (int i = 0; i < targets.Count; i++)
                {
                    GameObject t = targets[i];
                    if (t == null) continue;
                    bool isChildOfOther = false;
                    for (int j = 0; j < targets.Count; j++)
                    {
                        if (i != j && targets[j] != null && t.transform.IsChildOf(targets[j].transform))
                        {
                            isChildOfOther = true;
                            break;
                        }
                    }
                    if (!isChildOfOther) rootTargets.Add(t);
                }

                if (mode == EditorGizmoMode.Translate)
                {
                    Vector3 camFwd = cam.transform.forward;
                    Vector3 planeNormal = Vector3.Cross(axis3D, Vector3.Cross(camFwd, axis3D)).normalized;
                    if (planeNormal.sqrMagnitude < 0.001f) planeNormal = -camFwd;

                    Plane plane = new Plane(planeNormal, _dragStartCenterPos);
                    Ray curRay = cam.ScreenPointToRay(Input.mousePosition);
                    Ray startRay = cam.ScreenPointToRay(_dragStartMousePos);

                    if (plane.Raycast(curRay, out float enter) && plane.Raycast(startRay, out float startEnter))
                    {
                        Vector3 curPoint = curRay.GetPoint(enter);
                        Vector3 startPoint = startRay.GetPoint(startEnter);
                        float proj = Vector3.Dot(curPoint - startPoint, axis3D);

                        if (grid > 0.01f)
                        {
                            proj = Mathf.Round(proj / grid) * grid;
                        }

                        Vector3 deltaPos = axis3D * proj;

                        for (int i = 0; i < rootTargets.Count; i++)
                        {
                            GameObject go = rootTargets[i];
                            if (go != null && _dragStartPositions.ContainsKey(go))
                            {
                                go.transform.position = _dragStartPositions[go] + deltaPos;

                                Rigidbody rb = go.GetComponent<Rigidbody>();
                                if (rb != null)
                                {
                                    rb.position = go.transform.position;
                                    rb.velocity = Vector3.zero;
                                    rb.angularVelocity = Vector3.zero;
                                }
                            }
                        }

                        if (_gizmoRoot != null)
                        {
                            _gizmoRoot.transform.position = _dragStartCenterPos + deltaPos;
                        }
                    }
                }
                else if (mode == EditorGizmoMode.Rotate)
                {
                    Plane rotPlane = new Plane(axis3D, _dragStartCenterPos);
                    Ray curRay = cam.ScreenPointToRay(Input.mousePosition);
                    Ray startRay = cam.ScreenPointToRay(_dragStartMousePos);

                    if (rotPlane.Raycast(curRay, out float enter) && rotPlane.Raycast(startRay, out float startEnter))
                    {
                        Vector3 vStart = (startRay.GetPoint(startEnter) - _dragStartCenterPos).normalized;
                        Vector3 vCur = (curRay.GetPoint(enter) - _dragStartCenterPos).normalized;

                        float angle = Vector3.SignedAngle(vStart, vCur, axis3D);
                        if (grid > 0.01f)
                        {
                            float step = (grid >= 2.0f) ? 45f : 15f;
                            angle = Mathf.Round(angle / step) * step;
                        }

                        Quaternion rot = Quaternion.AngleAxis(angle, axis3D);
                        for (int i = 0; i < rootTargets.Count; i++)
                        {
                            GameObject go = rootTargets[i];
                            if (go != null && _dragStartPositions.ContainsKey(go))
                            {
                                Vector3 offset = _dragStartPositions[go] - _dragStartCenterPos;
                                go.transform.position = _dragStartCenterPos + (rot * offset);
                                go.transform.rotation = rot * _dragStartRotations[go];
                            }
                        }
                    }
                }
                else if (mode == EditorGizmoMode.Scale)
                {
                    Vector2 mouseDelta = (Vector2)Input.mousePosition - _dragStartMousePos;
                    float deltaMagnitude;

                    if (_activeDragAxis == 3)
                    {
                        deltaMagnitude = (mouseDelta.x + mouseDelta.y) * 0.008f;
                    }
                    else
                    {
                        Vector3 worldAxis = (_activeDragAxis == 0) ? Vector3.right : (_activeDragAxis == 1 ? Vector3.up : Vector3.forward);
                        Vector3 sCenter = cam.WorldToScreenPoint(_dragStartCenterPos);
                        Vector3 sTip = cam.WorldToScreenPoint(_dragStartCenterPos + worldAxis);
                        Vector2 screenDir = (Vector2)(sTip - sCenter);

                        if (screenDir.sqrMagnitude > 0.001f)
                        {
                            screenDir.Normalize();
                            deltaMagnitude = Vector2.Dot(mouseDelta, screenDir) * 0.008f;
                        }
                        else
                        {
                            deltaMagnitude = (mouseDelta.x + mouseDelta.y) * 0.008f;
                        }
                    }

                    float factor = 1.0f + deltaMagnitude;
                    if (grid > 0.01f) factor = Mathf.Round(factor / 0.1f) * 0.1f;
                    factor = Mathf.Max(0.02f, factor);

                    for (int i = 0; i < rootTargets.Count; i++)
                    {
                        GameObject go = rootTargets[i];
                        if (go == null || !_dragStartScales.ContainsKey(go)) continue;

                        Vector3 baseScale = _dragStartScales[go];

                        if (_activeDragAxis == 3)
                        {
                            Vector3 offset = _dragStartPositions[go] - _dragStartCenterPos;
                            go.transform.position = _dragStartCenterPos + (offset * factor);
                            go.transform.localScale = baseScale * factor;
                        }
                        else
                        {
                            Vector3 worldAxis = (_activeDragAxis == 0) ? Vector3.right : (_activeDragAxis == 1 ? Vector3.up : Vector3.forward);
                            Vector3 localDir = Quaternion.Inverse(go.transform.rotation) * worldAxis;
                            float ax = Mathf.Abs(localDir.x);
                            float ay = Mathf.Abs(localDir.y);
                            float az = Mathf.Abs(localDir.z);

                            Vector3 newScale = baseScale;

                            if (ax >= ay && ax >= az)
                            {
                                newScale.x = Mathf.Max(0.02f, baseScale.x * factor);
                            }
                            else if (ay >= ax && ay >= az)
                            {
                                newScale.y = Mathf.Max(0.02f, baseScale.y * factor);
                            }
                            else
                            {
                                newScale.z = Mathf.Max(0.02f, baseScale.z * factor);
                            }

                            go.transform.localScale = newScale;
                        }
                    }
                }

                StudioUIManager.RefreshInspectorValues();
                EditorSessionManager.UpdateSelectionHighlight();
            }

            if (Input.GetMouseButtonUp(0) && _activeDragAxis != -1)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    GameObject go = targets[i];
                    if (go != null && _dragStartPositions.ContainsKey(go))
                    {
                        InvalidateCachedCenter(go);
                        EditorSessionManager.UndoHistory.Push(new HistoryRecord
                        {
                            ActionType = HistoryActionType.Reposition,
                            TargetObject = go,
                            PreviousPosition = _dragStartPositions[go],
                            NewPosition = go.transform.position,
                            PreviousRotation = _dragStartRotations[go],
                            NewRotation = go.transform.rotation,
                            PreviousScale = _dragStartScales[go],
                            NewScale = go.transform.localScale,
                            EntityDataSnapshot = EditorSessionManager.ExtractEntityData(go)
                        });
                    }
                }
                EditorSessionManager.RedoHistory.Clear();
                _activeDragAxis = -1;
            }
        }

        public static class SkyboxWidgetBuilder
        {
            public static GameObject CreateWidget(Transform parent)
            {
                GameObject widget = new GameObject("Skybox_Editor_Widget");
                widget.transform.SetParent(parent, false);
                widget.layer = 0;

                GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                StripCollider(core);
                core.transform.SetParent(widget.transform, false);
                core.transform.localScale = Vector3.one * 0.45f;
                core.GetComponent<Renderer>().sharedMaterial = GizmoMaterialCache.CreateSolidMaterial(new Color(0.4f, 0.85f, 1f, 1f));

                CreateWireRing(widget.transform, "Ring_Equator", Vector3.up, 1.2f, new Color(0.2f, 0.6f, 1f, 0.85f));
                CreateWireRing(widget.transform, "Ring_Meridian", Vector3.right, 1.2f, new Color(0.3f, 0.9f, 1f, 0.65f));

                GameObject needle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                StripCollider(needle);
                needle.transform.SetParent(widget.transform, false);
                needle.transform.localScale = new Vector3(0.06f, 0.75f, 0.06f);
                needle.transform.localPosition = new Vector3(0f, 0.75f, 0f);
                needle.GetComponent<Renderer>().sharedMaterial = GizmoMaterialCache.CreateSolidMaterial(new Color(1f, 0.85f, 0.2f, 1f));

                SphereCollider sc = widget.AddComponent<SphereCollider>();
                sc.radius = 1.3f;
                sc.isTrigger = true;

                return widget;
            }

            private static void CreateWireRing(Transform parent, string name, Vector3 normalAxis, float radius, Color col)
            {
                GameObject ring = new GameObject(name);
                ring.transform.SetParent(parent, false);

                LineRenderer lr = ring.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = true;
                lr.positionCount = 32;
                lr.startWidth = 0.05f;
                lr.endWidth = 0.05f;
                lr.sharedMaterial = GizmoMaterialCache.CreateSolidMaterial(col);
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                Quaternion rot = Quaternion.FromToRotation(Vector3.up, normalAxis);
                for (int i = 0; i < 32; i++)
                {
                    float a = (i / 32f) * Mathf.PI * 2f;
                    Vector3 pt = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                    lr.SetPosition(i, rot * pt);
                }
            }
        }

        public static void DestroyGizmo()
        {
            if (_gizmoRoot != null)
            {
                GameObject.Destroy(_gizmoRoot);
                _gizmoRoot = null;
                _groupTranslate = null;
                _groupRotate = null;
                _groupScale = null;
            }
            GizmoMaterialCache.Clear();
            _activeDragAxis = -1;
            IsHoveringHandle = false;
        }
    }
}