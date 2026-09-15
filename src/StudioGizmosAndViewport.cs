using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DeadCoreEditor
{
    // =========================================================================
    // SECTION 1: STUDIO 3D VIEWPORT FLYCAM CONTROLLER
    // =========================================================================

    /// <summary>
    /// Manages the free-flying viewport camera in Studio Edit Mode.
    /// Uses standard 3D DCC controls (Hold Right Click to look, WASD to move).
    /// </summary>
    public static class EditorViewportCamera
    {
        private static GameObject _camInstance = null;
        public static Camera ViewportCamera = null;

        private static float _yaw = 0f;
        private static float _pitch = 0f;
        private static float _baseSpeed = 24f;

        /// <summary>
        /// Spawns or re-binds the dedicated viewport camera from the player's active camera.
        /// </summary>
        public static void InitializeCamera(Camera sourceCam)
        {
            if (_camInstance != null) return;

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

            ViewportCamera.nearClipPlane = 0.1f;
            ViewportCamera.farClipPlane = 4000f;
        }

        /// <summary>
        /// Processes freelook orientation, speed modifiers, and 6-axis camera translation.
        /// </summary>
        public static void UpdateCamera()
        {
            if (_camInstance == null || ViewportCamera == null) return;

            // Hold Right Click to engage free mouse-look
            if (Input.GetMouseButton(1))
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
                // Ensure cursor is freed when not aiming
                if (Cursor.lockState != CursorLockMode.None)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }

            // Translation Speed Modifiers: Shift = Sprint (3.5x), Ctrl = Precision Crawl (0.25x)
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
            if (Input.GetKey(KeyCode.W)) moveDir += _camInstance.transform.forward;
            if (Input.GetKey(KeyCode.S)) moveDir -= _camInstance.transform.forward;
            if (Input.GetKey(KeyCode.D)) moveDir += _camInstance.transform.right;
            if (Input.GetKey(KeyCode.A)) moveDir -= _camInstance.transform.right;
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space)) moveDir += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) moveDir -= Vector3.up;

            if (moveDir.sqrMagnitude > 0.001f)
            {
                _camInstance.transform.position += moveDir.normalized * (speed * Time.deltaTime);
            }
        }

        /// <summary>
        /// Safely destroys the viewport camera upon returning to playtest mode.
        /// </summary>
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
    // SECTION 2: HOLOGRAPHIC PREVIEW & ADJACENT PLACEMENT CONTROLLER
    // =========================================================================

    /// <summary>
    /// Manages the placement ghost, raycasting against geometry, discrete normal rounding,
    /// and ensures blocks are locked strictly adjacent to existing surfaces.
    /// </summary>
    public static class PlacementHologramController
    {
        private static GameObject _ghostInstance = null;
        public static GameObject GhostInstance => _ghostInstance;

        private static BoxCollider _ghostBoxCollider = null;
        private static Vector3 _targetPosition = Vector3.zero;
        public static Vector3 TargetPosition => _targetPosition;
        private static Vector3 _currentSnappedNormal = Vector3.up;

        /// <summary>
        /// Spawns a non-colliding holographic preview clone of the given catalog asset.
        /// </summary>
        public static void SpawnHologram(CatalogAsset asset)
        {
            DestroyPreview();
            if (asset == null || asset.SourceTemplate == null) return;

            _ghostInstance = GameObject.Instantiate(asset.SourceTemplate);
            _ghostInstance.name = "Holographic_Ghost_Preview";
            _ghostInstance.layer = 2; // Layer 2 = Ignore Raycast

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

        /// <summary>
        /// Calculates optimized proxy bounding extents across all mesh filters within the hierarchy.
        /// </summary>
        public static Bounds CalculateOptimizedProxyBounds(GameObject go)
        {
            Bounds raw = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;

            MeshFilter[] mfs = go.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in mfs)
            {
                if (mf.sharedMesh != null)
                {
                    Bounds meshB = mf.sharedMesh.bounds;
                    Vector3 localPos = go.transform.InverseTransformPoint(mf.transform.position);
                    Bounds transformedB = new Bounds(localPos + meshB.center, meshB.size);

                    if (!hasBounds)
                    {
                        raw = transformedB;
                        hasBounds = true;
                    }
                    else
                    {
                        raw.Encapsulate(transformedB);
                    }
                }
            }

            if (!hasBounds)
            {
                raw = new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f));
            }

            return raw;
        }

        /// <summary>
        /// Rounds surface normals to pure cardinal directions or 45-degree slope vectors.
        /// </summary>
        public static Vector3 SnapNormalToDiscreteAngles(Vector3 rawNormal)
        {
            if (rawNormal.sqrMagnitude < 0.01f) return Vector3.up;
            rawNormal.Normalize();

            if (rawNormal.y > 0.85f) return Vector3.up;
            if (rawNormal.y < -0.85f) return Vector3.down;

            if (Mathf.Abs(rawNormal.y) < 0.25f)
            {
                if (Mathf.Abs(rawNormal.x) > Mathf.Abs(rawNormal.z))
                {
                    return new Vector3(Mathf.Sign(rawNormal.x), 0f, 0f);
                }
                else
                {
                    return new Vector3(0f, 0f, Mathf.Sign(rawNormal.z));
                }
            }

            return rawNormal;
        }

        /// <summary>
        /// Combines surface normal orientation with user Euler yaw for realistic architectural alignment.
        /// </summary>
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

        /// <summary>
        /// Updates the placement ghost position, enforcing adjacent alignment and grid snapping.
        /// </summary>
        public static void UpdatePlacement()
        {
            if (!EditorSessionManager.IsBlockSelected || _ghostInstance == null || EditorViewportCamera.ViewportCamera == null) return;
            if (StudioUIManager.IsPointerOverUI()) return;

            Ray ray = Input.GetMouseButton(1)
                ? EditorViewportCamera.ViewportCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : EditorViewportCamera.ViewportCamera.ScreenPointToRay(Input.mousePosition);

            int raycastMask = ~LayerMask.GetMask("Ignore Raycast");
            RaycastHit hit;
            bool hasHit = Physics.Raycast(ray, out hit, 1500f, raycastMask, QueryTriggerInteraction.Collide);

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
                // Adjacent fallback: If aiming into empty space, lock adjacent to the last placed block
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

            _targetPosition = CalculateProxySnappedPosition(
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

            // Left click commits the placement
            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !StudioUIManager.IsPointerOverUI())
            {
                CommitPlacement(targetRot);
            }
        }

        /// <summary>
        /// Computes bounding offsets and surface separation so blocks snap cleanly onto faces.
        /// </summary>
        public static Vector3 CalculateProxySnappedPosition(
            Vector3 rawPos,
            Quaternion rot,
            BoxCollider proxyCol,
            Vector3 normal,
            bool hasHit,
            Collider hitCol,
            CatalogAsset asset)
        {
            if (proxyCol == null) return rawPos;

            Vector3 halfExtents = Vector3.Scale(proxyCol.size * 0.5f, proxyCol.transform.lossyScale);
            Vector3 centerOffset = Vector3.Scale(proxyCol.center, proxyCol.transform.lossyScale);

            float grid = EditorSessionManager.CurrentGridSnap;
            bool isGridActive = grid > 0.01f;
            Vector3 targetPos = rawPos;

            if (hasHit)
            {
                Vector3 uX = rot * Vector3.right;
                Vector3 uY = rot * Vector3.up;
                Vector3 uZ = rot * Vector3.forward;

                float extentAlongNormal = halfExtents.x * Mathf.Abs(Vector3.Dot(uX, normal))
                                        + halfExtents.y * Mathf.Abs(Vector3.Dot(uY, normal))
                                        + halfExtents.z * Mathf.Abs(Vector3.Dot(uZ, normal));

                float extraOffset = (asset != null) ? asset.VerticalOffset : 0f;
                Vector3 contactCenter = rawPos + normal * (extentAlongNormal + 0.001f + extraOffset);
                targetPos = contactCenter - (rot * centerOffset);

                if (isGridActive) targetPos = ApplyStrictGridSnap(targetPos, normal, grid);

                // Check for overlapping colliders to push out of penetration
                Vector3 probeHalfExtents = halfExtents - Vector3.one * 0.03f;
                probeHalfExtents.x = Mathf.Max(0.04f, probeHalfExtents.x);
                probeHalfExtents.y = Mathf.Max(0.04f, probeHalfExtents.y);
                probeHalfExtents.z = Mathf.Max(0.04f, probeHalfExtents.z);

                Vector3 currentWorldCenter = targetPos + (rot * centerOffset);
                Collider[] overlaps = Physics.OverlapBox(currentWorldCenter, probeHalfExtents, rot, ~0, QueryTriggerInteraction.Collide);

                Transform hitRoot = (hitCol != null) ? hitCol.transform.root : null;
                Transform ghostRoot = _ghostInstance.transform;

                for (int i = 0; i < overlaps.Length; i++)
                {
                    Collider col = overlaps[i];
                    if (col == null || col == proxyCol) continue;
                    if (col.transform.root == ghostRoot) continue;
                    if (col.GetComponent<CharacterController>() != null) continue;
                    if (hitRoot != null && col.transform.root == hitRoot) continue;

                    bool isPlaced = col.name.StartsWith("Custom_") || col.transform.root.name.StartsWith("Custom_") || col.name == "Editor_Snapping_Proxy";
                    if (!isPlaced) continue;

                    if (Physics.ComputePenetration(
                        proxyCol, targetPos, rot,
                        col, col.transform.position, col.transform.rotation,
                        out Vector3 pushDir, out float pushDist))
                    {
                        if (pushDist > 0.03f)
                        {
                            targetPos += pushDir * pushDist;
                            break;
                        }
                    }
                }

                if (isGridActive) targetPos = ApplyStrictGridSnap(targetPos, normal, grid);
            }
            else
            {
                if (isGridActive)
                {
                    targetPos = new Vector3(
                        Mathf.Round(targetPos.x / grid) * grid,
                        Mathf.Round(targetPos.y / grid) * grid,
                        Mathf.Round(targetPos.z / grid) * grid
                    );
                }
            }

            return targetPos;
        }

        /// <summary>
        /// Snaps positional vectors to discrete intervals based on the active contact normal.
        /// </summary>
        public static Vector3 ApplyStrictGridSnap(Vector3 pos, Vector3 normal, float grid)
        {
            if (grid <= 0.01f) return pos;

            Vector3 snapped = pos;
            float depthStep = (grid > 0.5f) ? (grid * 0.5f) : grid;

            if (Mathf.Abs(normal.y) > 0.65f)
            {
                snapped.x = Mathf.Round(snapped.x / grid) * grid;
                snapped.z = Mathf.Round(snapped.z / grid) * grid;
                snapped.y = Mathf.Round(snapped.y / depthStep) * depthStep;
            }
            else if (Mathf.Abs(normal.x) > 0.65f)
            {
                snapped.y = Mathf.Round(snapped.y / grid) * grid;
                snapped.z = Mathf.Round(snapped.z / grid) * grid;
                snapped.x = Mathf.Round(snapped.x / depthStep) * depthStep;
            }
            else if (Mathf.Abs(normal.z) > 0.65f)
            {
                snapped.x = Mathf.Round(snapped.x / grid) * grid;
                snapped.y = Mathf.Round(snapped.y / depthStep) * depthStep;
                snapped.z = Mathf.Round(snapped.z / grid) * grid;
            }
            else
            {
                snapped.x = Mathf.Round(snapped.x / grid) * grid;
                snapped.y = Mathf.Round(snapped.y / depthStep) * depthStep;
                snapped.z = Mathf.Round(snapped.z / grid) * grid;
            }

            return snapped;
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
                EditorSessionManager.SelectObject(placed);

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
                EditorSessionManager.ShowNotification($"Placed '{asset.DisplayName}'");
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
    // SECTION 3: 3D INTERACTIVE TRANSFORMATION GIZMO CONTROLLER
    // =========================================================================

    /// <summary>
    /// Renders and manages interactive 3D handles for Move, Rotate, and Scale in Select Mode.
    /// Handles are scaled dynamically so they maintain uniform screen size.
    /// </summary>
    public static class StudioGizmoController
    {
        private static GameObject _gizmoRoot = null;
        private static GameObject _axisX = null;
        private static GameObject _axisY = null;
        private static GameObject _axisZ = null;

        private static int _activeDragAxis = -1; // 0=X, 1=Y, 2=Z
        public static bool IsDraggingGizmo => _activeDragAxis != -1;
        public static bool IsHoveringHandle = false;

        private static Vector3 _dragStartObjPos = Vector3.zero;
        private static Quaternion _dragStartObjRot = Quaternion.identity;
        private static Vector3 _dragStartObjScale = Vector3.one;
        private static Vector3 _dragStartMousePos = Vector3.zero;

        /// <summary>
        /// Updates gizmo handle transforms, distance-scaling, and processes handle drag inputs.
        /// </summary>
        public static void UpdateGizmo()
        {
            if (EditorSessionManager.SelectedObject == null ||
                !EditorSessionManager.IsEditModeActive ||
                !EditorSessionManager.SelectedObject.activeSelf ||
                EditorSessionManager.InteractionMode != EditorInteractionMode.SelectMode)
            {
                if (_gizmoRoot != null) _gizmoRoot.SetActive(false);
                IsHoveringHandle = false;
                return;
            }

            if (EditorSessionManager.CurrentGizmoMode == EditorGizmoMode.Select)
            {
                if (_gizmoRoot != null) _gizmoRoot.SetActive(false);
                IsHoveringHandle = false;
                return;
            }

            EnsureGizmoInstances();

            GameObject target = EditorSessionManager.SelectedObject;
            _gizmoRoot.transform.position = target.transform.position;

            if (EditorSessionManager.CurrentGizmoMode == EditorGizmoMode.Scale)
            {
                _gizmoRoot.transform.rotation = target.transform.rotation;
            }
            else
            {
                _gizmoRoot.transform.rotation = Quaternion.identity;
            }

            // Uniform screen-space scaling based on camera distance
            if (EditorViewportCamera.ViewportCamera != null)
            {
                float dist = Vector3.Distance(EditorViewportCamera.ViewportCamera.transform.position, _gizmoRoot.transform.position);
                float scale = Mathf.Max(0.5f, dist * 0.085f);
                _gizmoRoot.transform.localScale = Vector3.one * scale;
            }

            _gizmoRoot.SetActive(true);

            CheckHandleHover();
            HandleGizmoDragging(target);
        }

        private static void EnsureGizmoInstances()
        {
            if (_gizmoRoot != null) return;

            _gizmoRoot = new GameObject("Studio_Transform_Gizmo_Root");
            _gizmoRoot.layer = 2;

            _axisX = CreateAxisHandle(_gizmoRoot.transform, "Gizmo_Handle_X", Vector3.right, new Color(0.95f, 0.2f, 0.2f));
            _axisY = CreateAxisHandle(_gizmoRoot.transform, "Gizmo_Handle_Y", Vector3.up, new Color(0.2f, 0.95f, 0.2f));
            _axisZ = CreateAxisHandle(_gizmoRoot.transform, "Gizmo_Handle_Z", Vector3.forward, new Color(0.2f, 0.45f, 1.0f));
        }

        private static GameObject CreateAxisHandle(Transform parent, string name, Vector3 dir, Color color)
        {
            GameObject handle = new GameObject(name);
            handle.transform.SetParent(parent, false);
            handle.layer = 2;

            // Shaft
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.layer = 2;
            cylinder.transform.SetParent(handle.transform, false);
            cylinder.transform.localScale = new Vector3(0.06f, 0.65f, 0.06f);
            cylinder.transform.localPosition = dir * 0.65f;
            cylinder.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);

            Collider col = cylinder.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            // Tip indicator
            GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.layer = 2;
            tip.transform.SetParent(handle.transform, false);
            tip.transform.localScale = new Vector3(0.26f, 0.26f, 0.26f);
            tip.transform.localPosition = dir * 1.35f;

            Collider tCol = tip.GetComponent<Collider>();
            if (tCol != null) tCol.isTrigger = true;

            Shader s = Shader.Find("Unlit/Color") ?? Shader.Find("Particles/Standard Unlit");
            if (s != null)
            {
                Material m = new Material(s);
                m.color = color;
                cylinder.GetComponent<Renderer>().material = m;
                tip.GetComponent<Renderer>().material = m;
            }

            return handle;
        }

        private static void CheckHandleHover()
        {
            if (EditorViewportCamera.ViewportCamera == null) return;
            Ray ray = EditorViewportCamera.ViewportCamera.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit, 2000f, 1 << 2, QueryTriggerInteraction.Collide))
            {
                IsHoveringHandle = hit.collider.transform.IsChildOf(_axisX.transform) ||
                                   hit.collider.transform.IsChildOf(_axisY.transform) ||
                                   hit.collider.transform.IsChildOf(_axisZ.transform);
            }
            else
            {
                IsHoveringHandle = false;
            }
        }

        private static void HandleGizmoDragging(GameObject target)
        {
            if (EditorViewportCamera.ViewportCamera == null) return;
            Camera cam = EditorViewportCamera.ViewportCamera;

            // Begin dragging handle
            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !StudioUIManager.IsPointerOverUI())
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 2000f, 1 << 2, QueryTriggerInteraction.Collide))
                {
                    if (hit.collider.transform.IsChildOf(_axisX.transform)) _activeDragAxis = 0;
                    else if (hit.collider.transform.IsChildOf(_axisY.transform)) _activeDragAxis = 1;
                    else if (hit.collider.transform.IsChildOf(_axisZ.transform)) _activeDragAxis = 2;

                    if (_activeDragAxis != -1)
                    {
                        _dragStartObjPos = target.transform.position;
                        _dragStartObjRot = target.transform.rotation;
                        _dragStartObjScale = target.transform.localScale;
                        _dragStartMousePos = Input.mousePosition;
                    }
                }
            }

            // Dragging update
            if (Input.GetMouseButton(0) && _activeDragAxis != -1)
            {
                Vector3 delta = Input.mousePosition - _dragStartMousePos;

                if (EditorSessionManager.CurrentGizmoMode == EditorGizmoMode.Translate)
                {
                    Vector3 worldAxis = (_activeDragAxis == 0) ? Vector3.right : (_activeDragAxis == 1 ? Vector3.up : Vector3.forward);
                    Vector3 screenAxis = cam.WorldToScreenPoint(target.transform.position + worldAxis) - cam.WorldToScreenPoint(target.transform.position);
                    screenAxis.z = 0f;
                    float proj = Vector3.Dot(delta, screenAxis.normalized);

                    float distFactor = Vector3.Distance(cam.transform.position, target.transform.position) * 0.0018f;
                    Vector3 targetPos = _dragStartObjPos + worldAxis * (proj * distFactor);

                    float grid = EditorSessionManager.CurrentGridSnap;
                    if (grid > 0.01f)
                    {
                        targetPos = new Vector3(
                            Mathf.Round(targetPos.x / grid) * grid,
                            Mathf.Round(targetPos.y / grid) * grid,
                            Mathf.Round(targetPos.z / grid) * grid
                        );
                    }
                    target.transform.position = targetPos;
                }
                else if (EditorSessionManager.CurrentGizmoMode == EditorGizmoMode.Rotate)
                {
                    float angle = (delta.x - delta.y) * 0.5f;
                    Vector3 rotAxis = (_activeDragAxis == 0) ? Vector3.right : (_activeDragAxis == 1 ? Vector3.up : Vector3.forward);
                    target.transform.rotation = _dragStartObjRot * Quaternion.AngleAxis(angle, rotAxis);
                }
                else if (EditorSessionManager.CurrentGizmoMode == EditorGizmoMode.Scale)
                {
                    float scaleDelta = (delta.x + delta.y) * 0.01f;
                    float newScale = Mathf.Max(0.05f, _dragStartObjScale.x + scaleDelta);
                    target.transform.localScale = Vector3.one * newScale;
                }

                StudioUIManager.RefreshInspectorValues();
                EditorSessionManager.UpdateSelectionHighlight();
            }

            // Finish dragging and commit to Undo stack
            if (Input.GetMouseButtonUp(0) && _activeDragAxis != -1)
            {
                EditorSessionManager.UndoHistory.Push(new HistoryRecord
                {
                    ActionType = HistoryActionType.Reposition,
                    TargetObject = target,
                    PreviousPosition = _dragStartObjPos,
                    NewPosition = target.transform.position,
                    PreviousRotation = _dragStartObjRot,
                    NewRotation = target.transform.rotation,
                    PreviousScale = _dragStartObjScale,
                    NewScale = target.transform.localScale
                });
                EditorSessionManager.RedoHistory.Clear();
                _activeDragAxis = -1;
            }
        }

        /// <summary>
        /// Cleans up gizmo hierarchy on exit.
        /// </summary>
        public static void DestroyGizmo()
        {
            if (_gizmoRoot != null)
            {
                GameObject.Destroy(_gizmoRoot);
                _gizmoRoot = null;
                _axisX = null;
                _axisY = null;
                _axisZ = null;
            }
            _activeDragAxis = -1;
            IsHoveringHandle = false;
        }
    }
}