using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using Il2Cpp;
using Il2CppDeadCore;

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 1: STUDIO 3D VIEWPORT FLYCAM CONTROLLER
    // =========================================================================

    public static class EditorViewportCamera
    {
        private static GameObject _camInstance = null;
        public static Camera ViewportCamera = null;

        private static float _yaw = 0f;
        private static float _pitch = 0f;
        private static float _baseSpeed = 24f;

        public static void InitializeCamera(Camera sourceCam)
        {
            if (_camInstance != null)
            {
                EnsureCameraConfiguration();
                return;
            }

            _camInstance = new GameObject("Studio_Viewport_Camera");
            ViewportCamera = _camInstance.AddComponent<Camera>();

            if (sourceCam != null)
            {
                ViewportCamera.CopyFrom(sourceCam);
                _camInstance.transform.position = sourceCam.transform.position;
                _camInstance.transform.rotation = sourceCam.transform.rotation;
                _yaw = _camInstance.transform.eulerAngles.y;
                _pitch = _camInstance.transform.eulerAngles.x;
                sourceCam.enabled = false;
            }
            else
            {
                _camInstance.transform.position = EditorSessionManager.LevelSpawnPosition + Vector3.up * 4f - Vector3.forward * 8f;
                _camInstance.transform.LookAt(EditorSessionManager.LevelSpawnPosition);
                _yaw = _camInstance.transform.eulerAngles.y;
                _pitch = _camInstance.transform.eulerAngles.x;
            }

            ViewportCamera.nearClipPlane = 0.05f;
            ViewportCamera.farClipPlane = 5000f;
            ViewportCamera.depth = 99f;

            EnsureCameraConfiguration();
        }

        public static void FocusOnObject(GameObject target)
        {
            if (target == null || _camInstance == null) return;
            Vector3 center = StudioGizmoController.GetObjectCenter(target);
            Bounds b = StudioGizmoController.GetObjectWorldBounds(target);
            float radius = Mathf.Max(b.extents.x, b.extents.y, b.extents.z, 2.0f);

            _camInstance.transform.position = center - _camInstance.transform.forward * (radius * 2.5f) + Vector3.up * (radius * 0.8f);
            _camInstance.transform.LookAt(center);
            _yaw = _camInstance.transform.eulerAngles.y;
            _pitch = _camInstance.transform.eulerAngles.x;
        }

        public static void EnsureCameraConfiguration()
        {
            if (ViewportCamera != null)
            {
                ViewportCamera.cullingMask = ~0;
                ViewportCamera.enabled = true;

                if (ViewportCamera.targetTexture != null)
                {
                    ViewportCamera.targetTexture = null;
                }

                Camera[] cams = Camera.allCameras;
                for (int i = 0; i < cams.Length; i++)
                {
                    if (cams[i] != null && cams[i] != ViewportCamera && cams[i].enabled)
                    {
                        cams[i].enabled = false;
                    }
                }
            }
        }

        public static void UpdateCamera()
        {
            if (_camInstance == null || ViewportCamera == null) return;

            EnsureCameraConfiguration();

            bool isFlying = Input.GetMouseButton(1);

            if (isFlying)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;

                float mouseX = Input.GetAxis("Mouse X");
                float mouseY = Input.GetAxis("Mouse Y");

                _yaw += mouseX * 2.5f;
                _pitch -= mouseY * 2.5f;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);

                _camInstance.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }
            else
            {
                if (Cursor.lockState != CursorLockMode.None)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }

            float speed = _baseSpeed;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                speed *= 3.5f;
            }
            else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                speed *= 0.25f;
            }

            Vector3 moveDir = Vector3.zero;
            if (Input.GetKey(KeyCode.W) && (isFlying || GUIUtility.keyboardControl == 0)) moveDir += _camInstance.transform.forward;
            if (Input.GetKey(KeyCode.S) && (isFlying || GUIUtility.keyboardControl == 0)) moveDir -= _camInstance.transform.forward;
            if (Input.GetKey(KeyCode.D) && (isFlying || GUIUtility.keyboardControl == 0)) moveDir += _camInstance.transform.right;
            if (Input.GetKey(KeyCode.A) && (isFlying || GUIUtility.keyboardControl == 0)) moveDir -= _camInstance.transform.right;
            if (Input.GetKey(KeyCode.Space) || (Input.GetKey(KeyCode.E) && isFlying)) moveDir += Vector3.up;
            if (Input.GetKey(KeyCode.Q) && isFlying) moveDir -= Vector3.up;

            if (EditorSessionManager.InteractionMode == EditorInteractionMode.SelectMode && !StudioUIManager.IsPointerOverUI())
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    float zoomSpeed = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 28f : 12f;
                    _camInstance.transform.position += _camInstance.transform.forward * (scroll * zoomSpeed);
                }
            }

            if (moveDir.sqrMagnitude > 0.001f)
            {
                _camInstance.transform.position += moveDir.normalized * (speed * Time.deltaTime);
            }
        }

        public static void DestroyCamera()
        {
            if (_camInstance != null)
            {
                GameObject.Destroy(_camInstance);
                _camInstance = null;
                ViewportCamera = null;
            }
        }
    }

    // =========================================================================
    // SECTION 2: HOLOGRAPHIC PREVIEW & ROW-SNAPPING
    // =========================================================================

    public static class PlacementHologramController
    {
        private static GameObject _ghostInstance = null;
        public static GameObject GhostInstance => _ghostInstance;

        private static BoxCollider _ghostBoxCollider = null;
        private static Vector3 _targetPosition = Vector3.zero;
        public static Vector3 TargetPosition => _targetPosition;
        private static Vector3 _currentSnappedNormal = Vector3.up;

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
                col.enabled = false;
            }

            foreach (var mb in _ghostInstance.GetComponentsInChildren<MonoBehaviour>(true))
            {
                GameObject.DestroyImmediate(mb);
            }

            EditorSessionManager.StripParticlesAndLights(_ghostInstance);

            Bounds b = CalculateOptimizedProxyBounds(_ghostInstance);
            _ghostBoxCollider = _ghostInstance.AddComponent<BoxCollider>();
            _ghostBoxCollider.isTrigger = true;
            _ghostBoxCollider.center = b.center;
            _ghostBoxCollider.size = b.size;

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
                string n = mf.gameObject.name.ToLower();
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

            if (!hasBounds)
            {
                return new Bounds(Vector3.zero, Vector3.one * 2f);
            }

            return localBounds;
        }

        public static Vector3 SnapNormalToDiscreteAngles(Vector3 rawNormal)
        {
            if (rawNormal.sqrMagnitude < 0.01f) return Vector3.up;
            rawNormal.Normalize();

            if (rawNormal.y > 0.65f) return Vector3.up;
            if (rawNormal.y < -0.65f) return Vector3.down;

            if (Mathf.Abs(rawNormal.x) > Mathf.Abs(rawNormal.z))
            {
                return new Vector3(Mathf.Sign(rawNormal.x), 0f, 0f);
            }
            else
            {
                return new Vector3(0f, 0f, Mathf.Sign(rawNormal.z));
            }
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
                _ghostInstance.transform.rotation = CalculateActiveRotation(EditorSessionManager.CurrentAsset, _currentSnappedNormal);
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
            if (Input.GetKeyDown(KeyCode.KeypadPlus) || Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.RightBracket))
            {
                SetPlacementScale(EditorSessionManager.ActivePlacementScale + scaleStep);
            }
            else if (Input.GetKeyDown(KeyCode.KeypadMinus) || Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.LeftBracket))
            {
                SetPlacementScale(EditorSessionManager.ActivePlacementScale - scaleStep);
            }

            if (!Input.GetMouseButton(1))
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    bool isShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    bool isAlt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                    bool isCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

                    if (isShift || isAlt || isCtrl)
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
            bool hasHit = Physics.Raycast(ray, out RaycastHit hit, 1500f, raycastMask, QueryTriggerInteraction.Collide);

            Vector3 hitNormal = Vector3.up;
            Vector3 rawTargetPos;
            Collider hitCollider = null;

            if (hasHit)
            {
                rawTargetPos = hit.point;
                hitNormal = hit.normal;
                hitCollider = hit.collider;
            }
            else
            {
                if (EditorSessionManager.EnforceAdjacentPlacement && EditorSessionManager.LastPlacedObject != null && EditorSessionManager.LastPlacedObject.activeSelf)
                {
                    Transform lastTr = EditorSessionManager.LastPlacedObject.transform;
                    Vector3 toCam = (EditorViewportCamera.ViewportCamera.transform.position - lastTr.position).normalized;
                    rawTargetPos = lastTr.position + Vector3.ProjectOnPlane(toCam, Vector3.up).normalized * (EditorSessionManager.CurrentGridSnap * 2f);
                    hitNormal = Vector3.up;
                }
                else
                {
                    rawTargetPos = ray.origin + ray.direction * 14f;
                    hitNormal = -ray.direction;
                }
            }

            _currentSnappedNormal = SnapNormalToDiscreteAngles(hitNormal);
            Quaternion targetRot = CalculateActiveRotation(EditorSessionManager.CurrentAsset, _currentSnappedNormal);

            _targetPosition = CalculateRowSnappedPosition(
                rawTargetPos,
                targetRot,
                _ghostBoxCollider,
                _currentSnappedNormal,
                hasHit,
                hitCollider,
                EditorSessionManager.CurrentAsset
            );

            _ghostInstance.transform.position = Vector3.Lerp(_ghostInstance.transform.position, _targetPosition, Time.deltaTime * 35f);
            _ghostInstance.transform.rotation = Quaternion.Slerp(_ghostInstance.transform.rotation, targetRot, Time.deltaTime * 24f);

            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !StudioUIManager.IsPointerOverUI())
            {
                if (!StudioGizmoController.IsHoveringHandle)
                {
                    CommitPlacement(targetRot);
                }
            }
        }

        public static Vector3 CalculateRowSnappedPosition(
            Vector3 rawPos,
            Quaternion rot,
            BoxCollider proxyCol,
            Vector3 normal,
            bool hasHit,
            Collider hitCol,
            CatalogAsset asset)
        {
            if (proxyCol == null) return rawPos;

            Vector3 myHalfExtents = Vector3.Scale(proxyCol.size * 0.5f, proxyCol.transform.lossyScale);
            Vector3 centerOffset = Vector3.Scale(proxyCol.center, proxyCol.transform.lossyScale);

            float grid = EditorSessionManager.CurrentGridSnap;
            bool isGridActive = grid > 0.01f;

            GameObject hitPlacedObj = null;
            Transform curr = (hitCol != null) ? hitCol.transform : null;
            while (curr != null)
            {
                if (EditorSessionManager.PlacedObjects.Contains(curr.gameObject))
                {
                    hitPlacedObj = curr.gameObject;
                    break;
                }
                curr = curr.parent;
            }

            if (hitPlacedObj != null)
            {
                Vector3 hitObjPos = hitPlacedObj.transform.position;
                Bounds hitB = CalculateOptimizedProxyBounds(hitPlacedObj);
                Vector3 hitHalfExtents = Vector3.Scale(hitB.extents, hitPlacedObj.transform.lossyScale);

                Vector3 cardNormal = normal;
                float stepDist = Mathf.Abs(cardNormal.x) * (hitHalfExtents.x + myHalfExtents.x)
                               + Mathf.Abs(cardNormal.y) * (hitHalfExtents.y + myHalfExtents.y)
                               + Mathf.Abs(cardNormal.z) * (hitHalfExtents.z + myHalfExtents.z);

                Vector3 alignedPos = hitObjPos + cardNormal * stepDist;

                if (isGridActive)
                {
                    Vector3 mouseOffset = rawPos - hitObjPos;
                    if (Mathf.Abs(cardNormal.x) > 0.5f)
                    {
                        float deltaY = Mathf.Round(mouseOffset.y / grid) * grid;
                        float deltaZ = Mathf.Round(mouseOffset.z / grid) * grid;
                        if (Mathf.Abs(deltaY) < hitHalfExtents.y * 1.5f) alignedPos.y = hitObjPos.y + deltaY;
                        if (Mathf.Abs(deltaZ) < hitHalfExtents.z * 1.5f) alignedPos.z = hitObjPos.z + deltaZ;
                    }
                    else if (Mathf.Abs(cardNormal.y) > 0.5f)
                    {
                        float deltaX = Mathf.Round(mouseOffset.x / grid) * grid;
                        float deltaZ = Mathf.Round(mouseOffset.z / grid) * grid;
                        if (Mathf.Abs(deltaX) < hitHalfExtents.x * 1.5f) alignedPos.x = hitObjPos.x + deltaX;
                        if (Mathf.Abs(deltaZ) < hitHalfExtents.z * 1.5f) alignedPos.z = hitObjPos.z + deltaZ;
                    }
                    else if (Mathf.Abs(cardNormal.z) > 0.5f)
                    {
                        float deltaX = Mathf.Round(mouseOffset.x / grid) * grid;
                        float deltaY = Mathf.Round(mouseOffset.y / grid) * grid;
                        if (Mathf.Abs(deltaX) < hitHalfExtents.x * 1.5f) alignedPos.x = hitObjPos.x + deltaX;
                        if (Mathf.Abs(deltaY) < hitHalfExtents.y * 1.5f) alignedPos.y = hitObjPos.y + deltaY;
                    }
                }

                return alignedPos;
            }

            Vector3 uX = rot * Vector3.right;
            Vector3 uY = rot * Vector3.up;
            Vector3 uZ = rot * Vector3.forward;

            float extentAlongNormal = myHalfExtents.x * Mathf.Abs(Vector3.Dot(uX, normal))
                                    + myHalfExtents.y * Mathf.Abs(Vector3.Dot(uY, normal))
                                    + myHalfExtents.z * Mathf.Abs(Vector3.Dot(uZ, normal));

            Vector3 contactCenter = rawPos + normal * (extentAlongNormal + 0.001f);
            Vector3 targetPos = contactCenter - (rot * centerOffset);

            if (isGridActive)
            {
                targetPos = new Vector3(
                    Mathf.Round(targetPos.x / grid) * grid,
                    Mathf.Round(targetPos.y / grid) * grid,
                    Mathf.Round(targetPos.z / grid) * grid
                );
            }

            return targetPos;
        }

        private static void CommitPlacement(Quaternion placementRotation)
        {
            CatalogAsset asset = EditorSessionManager.CurrentAsset;
            if (asset == null) return;

            float scale = EditorSessionManager.ActivePlacementScale;
            GameObject placed = EditorSessionManager.SpawnCatalogObject(asset, _targetPosition, scale, placementRotation);

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

                float param = 0f;
                if (asset.IsJumper) param = EditorSessionManager.ActiveJumperForce;
                else if (asset.IsHelix) param = EditorSessionManager.ActiveTurbineSpeed;
                else if (asset.IsTurret) param = EditorSessionManager.ActiveTurretFireDelay;

                EditorSessionManager.UndoHistory.Push(new HistoryRecord
                {
                    ActionType = HistoryActionType.Placement,
                    TargetObject = placed,
                    Asset = asset,
                    AssetName = asset.DisplayName,
                    Position = _targetPosition,
                    Rotation = placementRotation,
                    Scale = scale,
                    CustomParameter = param
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
                _ghostBoxCollider = null;
            }
        }
    }

    // =========================================================================
    // SECTION 3: 3D GIZMO SYSTEM & COMPACT DIRECTIONAL SUN WIDGET
    // =========================================================================

    public static class StudioGizmoController
    {
        private static GameObject _gizmoRoot = null;
        private static GameObject _groupTranslate = null;
        private static GameObject _groupRotate = null;
        private static GameObject _groupScale = null;

        // Handles
        private static GameObject _arrowX = null, _arrowY = null, _arrowZ = null;
        private static GameObject _centerSphere = null;

        private static GameObject _ringX = null, _ringY = null, _ringZ = null;
        private static GameObject _rotSphereX = null, _rotSphereY = null, _rotSphereZ = null;

        private static GameObject _scaleX = null, _scaleY = null, _scaleZ = null;
        private static GameObject _centerScaleBox = null;

        private static Material _matRed = null;
        private static Material _matGreen = null;
        private static Material _matBlue = null;
        private static Material _matYellow = null;
        private static Material _matWhite = null;

        private static int _activeDragAxis = -1; // 0=X, 1=Y, 2=Z, 3=Center
        public static bool IsDraggingGizmo => _activeDragAxis != -1;
        public static bool IsHoveringHandle = false;

        private static Vector3 _dragStartCenterPos = Vector3.zero;
        private static Vector2 _dragStartMousePos = Vector2.zero;

        private static readonly RaycastHit[] _gizmoHitBuffer = new RaycastHit[64];
        private static readonly Dictionary<GameObject, Vector3> _cachedLocalCentroids = new Dictionary<GameObject, Vector3>();
        private static readonly Dictionary<GameObject, Vector3> _dragStartPositions = new Dictionary<GameObject, Vector3>();
        private static readonly Dictionary<GameObject, Quaternion> _dragStartRotations = new Dictionary<GameObject, Quaternion>();
        private static readonly Dictionary<GameObject, Vector3> _dragStartScales = new Dictionary<GameObject, Vector3>();

        // =========================================================================
        // MINIMAL COMPACT SUNLIGHT GIZMO WIDGET (SMALL BALL + DIRECTION ARROW ONLY)
        // =========================================================================

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

            Material sunMat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default"));
            sunMat.color = sunColor;
            if (sunMat.HasProperty("_Color")) sunMat.SetColor("_Color", sunColor);
            if (sunMat.HasProperty("_EmissionColor"))
            {
                sunMat.SetColor("_EmissionColor", sunColor * 1.8f);
                sunMat.EnableKeyword("_EMISSION");
            }

            // 1. Small glowing center sphere (0.75m)
            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "Sun_Orb";
            orb.transform.SetParent(widget.transform, false);
            orb.transform.localScale = Vector3.one * 0.75f;
            Renderer rOrb = orb.GetComponent<Renderer>();
            rOrb.sharedMaterial = sunMat;
            rOrb.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 2. Compact single direction arrow pointing along sunlight vector (+Z forward)
            GameObject ray = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ray.name = "Sun_Ray_Shaft";
            ray.transform.SetParent(widget.transform, false);
            ray.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ray.transform.localPosition = new Vector3(0f, 0f, 0.9f);
            ray.transform.localScale = new Vector3(0.12f, 0.65f, 0.12f);
            Renderer rRay = ray.GetComponent<Renderer>();
            rRay.sharedMaterial = sunMat;
            rRay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "Sun_Ray_Tip";
            tip.transform.SetParent(widget.transform, false);
            tip.transform.localPosition = new Vector3(0f, 0f, 1.6f);
            tip.transform.localScale = Vector3.one * 0.28f;
            Renderer rTip = tip.GetComponent<Renderer>();
            rTip.sharedMaterial = sunMat;
            rTip.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 3. Compact pick collider
            SphereCollider sc = widget.AddComponent<SphereCollider>();
            sc.radius = 0.9f;
            sc.isTrigger = false;

            widget.SetActive(EditorSessionManager.IsEditModeActive);
        }

        public static Bounds GetObjectWorldBounds(GameObject obj)
        {
            if (obj == null) return new Bounds(Vector3.zero, Vector3.one);

            Renderer[] rends = obj.GetComponentsInChildren<Renderer>(true);
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;

            for (int i = 0; i < rends.Length; i++)
            {
                Renderer r = rends[i];
                if (r == null || !r.enabled) continue;
                string n = r.gameObject.name;
                if (n.Contains("Proxy") || n.Contains("Gizmo") || n.Contains("Highlight") || n.Contains("Beacon")) continue;

                if (!hasBounds)
                {
                    b = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    b.Encapsulate(r.bounds);
                }
            }

            if (!hasBounds)
            {
                return new Bounds(obj.transform.position, Vector3.one);
            }

            return b;
        }

        public static Vector3 GetObjectCenter(GameObject obj)
        {
            if (obj == null) return Vector3.zero;

            if (!_cachedLocalCentroids.TryGetValue(obj, out Vector3 localCenter))
            {
                Bounds b = GetObjectWorldBounds(obj);
                localCenter = obj.transform.InverseTransformPoint(b.center);
                _cachedLocalCentroids[obj] = localCenter;
            }

            return obj.transform.TransformPoint(localCenter);
        }

        public static void InvalidateCachedCenter(GameObject obj)
        {
            if (obj != null && _cachedLocalCentroids.ContainsKey(obj))
            {
                _cachedLocalCentroids.Remove(obj);
            }
        }

        public static void ClearAllCachedCentroids()
        {
            _cachedLocalCentroids.Clear();
        }

        private static Material CreateSolidMaterial(Color col)
        {
            Shader s = Shader.Find("Unlit/Color");
            if (s == null) s = Shader.Find("Particles/Standard Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");
            if (s == null) s = Shader.Find("GUI/Text Shader");

            Material m = (s != null) ? new Material(s) : new Material(Shader.Find("Hidden/InternalErrorShader") ?? s);

            m.name = "Gizmo_SolidMat_" + col.ToString();
            m.color = col;
            if (m.HasProperty("_Color")) m.SetColor("_Color", col);
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", col);

            if (m.HasProperty("_EmissionColor"))
            {
                m.SetColor("_EmissionColor", col * 1.5f);
                m.EnableKeyword("_EMISSION");
            }

            m.mainTexture = Texture2D.whiteTexture;
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", Texture2D.whiteTexture);

            m.renderQueue = 3000;
            return m;
        }

        private static void EnsureGizmoInstances()
        {
            if (_gizmoRoot != null) return;

            _matRed = CreateSolidMaterial(new Color(1f, 0.22f, 0.22f, 1f));
            _matGreen = CreateSolidMaterial(new Color(0.2f, 0.95f, 0.35f, 1f));
            _matBlue = CreateSolidMaterial(new Color(0.25f, 0.6f, 1f, 1f));
            _matYellow = CreateSolidMaterial(new Color(1f, 0.95f, 0.15f, 1f));
            _matWhite = CreateSolidMaterial(new Color(1f, 1f, 1f, 0.95f));

            _gizmoRoot = new GameObject("Studio_3D_Gizmo_Root");
            _gizmoRoot.layer = 0;

            // 1. TRANSLATE
            _groupTranslate = new GameObject("Group_Translate");
            _groupTranslate.transform.SetParent(_gizmoRoot.transform, false);

            _arrowX = Create3DArrow(_groupTranslate.transform, "Arrow_X", Vector3.right, _matRed);
            _arrowY = Create3DArrow(_groupTranslate.transform, "Arrow_Y", Vector3.up, _matGreen);
            _arrowZ = Create3DArrow(_groupTranslate.transform, "Arrow_Z", Vector3.forward, _matBlue);
            _centerSphere = CreatePrimitiveObj(PrimitiveType.Sphere, _groupTranslate.transform, "Center_Sphere", Vector3.zero, Vector3.one * 0.45f, _matWhite);

            // 2. ROTATE
            _groupRotate = new GameObject("Group_Rotate");
            _groupRotate.transform.SetParent(_gizmoRoot.transform, false);

            float r = 2.2f;
            _ringX = Create3DLineRing(_groupRotate.transform, "Ring_X", Vector3.right, r, _matRed);
            _ringY = Create3DLineRing(_groupRotate.transform, "Ring_Y", Vector3.up, r, _matGreen);
            _ringZ = Create3DLineRing(_groupRotate.transform, "Ring_Z", Vector3.forward, r, _matBlue);

            _rotSphereX = CreatePrimitiveObj(PrimitiveType.Sphere, _groupRotate.transform, "RotHandle_X", new Vector3(0f, 0f, r), Vector3.one * 0.5f, _matRed);
            _rotSphereY = CreatePrimitiveObj(PrimitiveType.Sphere, _groupRotate.transform, "RotHandle_Y", new Vector3(r, 0f, 0f), Vector3.one * 0.5f, _matGreen);
            _rotSphereZ = CreatePrimitiveObj(PrimitiveType.Sphere, _groupRotate.transform, "RotHandle_Z", new Vector3(0f, r, 0f), Vector3.one * 0.5f, _matBlue);

            // 3. SCALE
            _groupScale = new GameObject("Group_Scale");
            _groupScale.transform.SetParent(_gizmoRoot.transform, false);

            _scaleX = Create3DScaleStem(_groupScale.transform, "Scale_X", Vector3.right, _matRed);
            _scaleY = Create3DScaleStem(_groupScale.transform, "Scale_Y", Vector3.up, _matGreen);
            _scaleZ = Create3DScaleStem(_groupScale.transform, "Scale_Z", Vector3.forward, _matBlue);
            _centerScaleBox = CreatePrimitiveObj(PrimitiveType.Cube, _groupScale.transform, "Center_ScaleBox", Vector3.zero, Vector3.one * 0.5f, _matYellow);
        }

        private static GameObject Create3DArrow(Transform parent, string name, Vector3 dir, Material mat)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);

            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Shaft";
            shaft.transform.SetParent(root.transform, false);
            shaft.transform.localScale = new Vector3(0.14f, 0.9f, 0.14f);
            shaft.transform.localPosition = dir * 0.9f;
            shaft.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);

            Renderer sr = shaft.GetComponent<Renderer>();
            sr.sharedMaterial = mat;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;

            GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "Tip";
            tip.transform.SetParent(root.transform, false);
            tip.transform.localScale = new Vector3(0.42f, 0.42f, 0.42f);
            tip.transform.localPosition = dir * 1.85f;

            Renderer tr = tip.GetComponent<Renderer>();
            tr.sharedMaterial = mat;
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;

            return root;
        }

        private static GameObject Create3DScaleStem(Transform parent, string name, Vector3 dir, Material mat)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);

            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Shaft";
            shaft.transform.SetParent(root.transform, false);
            shaft.transform.localScale = new Vector3(0.14f, 0.9f, 0.14f);
            shaft.transform.localPosition = dir * 0.9f;
            shaft.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);

            Renderer sr = shaft.GetComponent<Renderer>();
            sr.sharedMaterial = mat;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "TipCube";
            cube.transform.SetParent(root.transform, false);
            cube.transform.localScale = new Vector3(0.46f, 0.46f, 0.46f);
            cube.transform.localPosition = dir * 1.85f;

            Renderer cr = cube.GetComponent<Renderer>();
            cr.sharedMaterial = mat;
            cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cr.receiveShadows = false;

            return root;
        }

        private static GameObject Create3DLineRing(Transform parent, string name, Vector3 normalAxis, float radius, Material mat)
        {
            GameObject ring = new GameObject(name);
            ring.transform.SetParent(parent, false);

            LineRenderer lr = ring.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = 36;
            lr.startWidth = 0.09f;
            lr.endWidth = 0.09f;
            lr.sharedMaterial = mat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            Quaternion rot = Quaternion.FromToRotation(Vector3.up, normalAxis);
            for (int i = 0; i < 36; i++)
            {
                float a = (i / 36f) * Mathf.PI * 2f;
                Vector3 pt = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                lr.SetPosition(i, rot * pt);
            }

            return ring;
        }

        private static GameObject CreatePrimitiveObj(PrimitiveType type, Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;

            Renderer r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            return go;
        }

        public static void UpdateGizmo()
        {
            if (!EditorSessionManager.IsEditModeActive)
            {
                if (_gizmoRoot != null) _gizmoRoot.SetActive(false);
                IsHoveringHandle = false;
                return;
            }

            bool isPlacing = (EditorSessionManager.InteractionMode == EditorInteractionMode.PlacementMode);
            List<GameObject> activeList = new List<GameObject>();

            if (isPlacing)
            {
                if (PlacementHologramController.GhostInstance != null && PlacementHologramController.GhostInstance.activeSelf)
                    activeList.Add(PlacementHologramController.GhostInstance);
                else if (EditorSessionManager.SelectedObject != null)
                    activeList.Add(EditorSessionManager.SelectedObject);
            }
            else
            {
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

            // Adaptive footprint scaling
            float dist = Vector3.Distance(cam.transform.position, center3D);
            float zoomFactor = Mathf.Lerp(2.2f, 0.85f, Mathf.InverseLerp(2f, 45f, dist));
            float distanceScale = dist * 0.075f * zoomFactor;

            GameObject primary = activeList[0];
            Bounds baseB = PlacementHologramController.CalculateOptimizedProxyBounds(primary);
            float baseDim = Mathf.Max(baseB.size.x, baseB.size.y, baseB.size.z);
            float propFactor = Mathf.Clamp(Mathf.Sqrt(baseDim) * 0.55f, 0.8f, 2.4f);

            float placementScaleMult = Mathf.Clamp(EditorSessionManager.ActivePlacementScale, 0.5f, 2.5f);
            float finalScale = Mathf.Max(0.65f, distanceScale * propFactor * placementScaleMult);

            _gizmoRoot.transform.localScale = Vector3.one * finalScale;

            EditorGizmoMode mode = isPlacing ? EditorGizmoMode.Translate : EditorSessionManager.CurrentGizmoMode;
            if (mode == EditorGizmoMode.Select) mode = EditorGizmoMode.Translate;

            _groupTranslate.SetActive(mode == EditorGizmoMode.Translate);
            _groupRotate.SetActive(mode == EditorGizmoMode.Rotate);
            _groupScale.SetActive(mode == EditorGizmoMode.Scale);
            _gizmoRoot.SetActive(true);

            int hovered = Check3DHover(cam);
            IsHoveringHandle = (hovered != -1 || _activeDragAxis != -1);

            if (!isPlacing)
            {
                Handle3DDragging(activeList, cam, center3D, hovered, mode);
            }
        }

        private static int Check3DHover(Camera cam)
        {
            if (StudioUIManager.IsPointerOverUI()) return -1;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            int hitCount = Physics.RaycastNonAlloc(ray, _gizmoHitBuffer, 2000f, ~0, QueryTriggerInteraction.Collide);

            int best = -1;
            float bestDist = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = _gizmoHitBuffer[i].collider;
                if (col == null) continue;

                Transform t = col.transform;
                int axis = -1;

                if (IsChildOfSafe(t, _arrowX) || IsChildOfSafe(t, _rotSphereX) || IsChildOfSafe(t, _scaleX)) axis = 0;
                else if (IsChildOfSafe(t, _arrowY) || IsChildOfSafe(t, _rotSphereY) || IsChildOfSafe(t, _scaleY)) axis = 1;
                else if (IsChildOfSafe(t, _arrowZ) || IsChildOfSafe(t, _rotSphereZ) || IsChildOfSafe(t, _scaleZ)) axis = 2;
                else if (IsChildOfSafe(t, _centerSphere) || IsChildOfSafe(t, _centerScaleBox)) axis = 3;

                if (axis != -1 && _gizmoHitBuffer[i].distance < bestDist)
                {
                    bestDist = _gizmoHitBuffer[i].distance;
                    best = axis;
                }
            }

            // Occlusion check: If a scene object is closer than the gizmo handle, the handle is occluded
            if (best != -1)
            {
                for (int i = 0; i < hitCount; i++)
                {
                    Collider col = _gizmoHitBuffer[i].collider;
                    if (col == null) continue;
                    Transform t = col.transform;
                    if (IsChildOfSafe(t, _gizmoRoot)) continue;
                    if (col.gameObject.name.Contains("Highlight") || col.gameObject.name.Contains("Beacon")) continue;

                    if (_gizmoHitBuffer[i].distance < bestDist - 0.05f)
                    {
                        return -1; // Occluded by placed scene geometry
                    }
                }
            }

            return best;
        }

        private static bool IsChildOfSafe(Transform t, GameObject target)
        {
            return (target != null && t != null && t.IsChildOf(target.transform));
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
                Vector2 mouseDelta = (Vector2)Input.mousePosition - _dragStartMousePos;
                Vector3 axis3D = (_activeDragAxis == 0) ? Vector3.right : (_activeDragAxis == 1 ? Vector3.up : Vector3.forward);

                if (mode == EditorGizmoMode.Translate)
                {
                    Vector3 deltaPos;
                    if (_activeDragAxis == 3)
                    {
                        deltaPos = cam.transform.right * (mouseDelta.x * 0.02f) + cam.transform.up * (mouseDelta.y * 0.02f);
                    }
                    else
                    {
                        Vector3 screenAxis = cam.WorldToScreenPoint(_dragStartCenterPos + axis3D) - cam.WorldToScreenPoint(_dragStartCenterPos);
                        screenAxis.z = 0f;
                        float proj = Vector2.Dot(mouseDelta, ((Vector2)screenAxis).normalized);
                        float distFactor = Vector3.Distance(cam.transform.position, _dragStartCenterPos) * 0.0018f;
                        deltaPos = axis3D * (proj * distFactor);
                    }

                    float grid = EditorSessionManager.CurrentGridSnap;
                    if (grid > 0.01f)
                    {
                        deltaPos = new Vector3(
                            Mathf.Round(deltaPos.x / grid) * grid,
                            Mathf.Round(deltaPos.y / grid) * grid,
                            Mathf.Round(deltaPos.z / grid) * grid
                        );
                    }

                    for (int i = 0; i < targets.Count; i++)
                    {
                        GameObject go = targets[i];
                        if (go != null && _dragStartPositions.ContainsKey(go))
                            go.transform.position = _dragStartPositions[go] + deltaPos;
                    }
                }
                else if (mode == EditorGizmoMode.Rotate)
                {
                    float angle = (_activeDragAxis == 1) ? -mouseDelta.x * 0.65f : (mouseDelta.y * 0.65f);
                    float grid = EditorSessionManager.CurrentGridSnap;
                    if (grid > 0.01f)
                    {
                        float step = (grid >= 2.0f) ? 45f : 15f;
                        angle = Mathf.Round(angle / step) * step;
                    }

                    Quaternion rot = Quaternion.AngleAxis(angle, axis3D);
                    for (int i = 0; i < targets.Count; i++)
                    {
                        GameObject go = targets[i];
                        if (go != null && _dragStartPositions.ContainsKey(go))
                        {
                            Vector3 offset = _dragStartPositions[go] - _dragStartCenterPos;
                            go.transform.position = _dragStartCenterPos + (rot * offset);
                            go.transform.rotation = rot * _dragStartRotations[go];
                        }
                    }
                }
                else if (mode == EditorGizmoMode.Scale)
                {
                    float factor = 1.0f + ((mouseDelta.x + mouseDelta.y) * 0.008f);
                    float grid = EditorSessionManager.CurrentGridSnap;
                    if (grid > 0.01f) factor = Mathf.Round(factor / 0.1f) * 0.1f;
                    factor = Mathf.Max(0.05f, factor);

                    for (int i = 0; i < targets.Count; i++)
                    {
                        GameObject go = targets[i];
                        if (go != null && _dragStartScales.ContainsKey(go))
                        {
                            Vector3 offset = _dragStartPositions[go] - _dragStartCenterPos;
                            go.transform.position = _dragStartCenterPos + (offset * factor);
                            go.transform.localScale = _dragStartScales[go] * factor;
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
                        EditorSessionManager.UndoHistory.Push(new HistoryRecord
                        {
                            ActionType = HistoryActionType.Reposition,
                            TargetObject = go,
                            PreviousPosition = _dragStartPositions[go],
                            NewPosition = go.transform.position,
                            PreviousRotation = _dragStartRotations[go],
                            NewRotation = go.transform.rotation,
                            PreviousScale = _dragStartScales[go],
                            NewScale = go.transform.localScale
                        });
                    }
                }
                EditorSessionManager.RedoHistory.Clear();
                _activeDragAxis = -1;
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
            _activeDragAxis = -1;
            IsHoveringHandle = false;
        }
    }
}