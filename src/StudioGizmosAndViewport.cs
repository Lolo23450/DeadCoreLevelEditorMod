using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Il2Cpp;
using Il2CppDeadCore;

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

                if (!StudioUIManager.IsPointerOverUI() && GUIUtility.keyboardControl == 0)
                {
                    if (Input.GetKeyDown(KeyCode.W))
                    {
                        EditorSessionManager.SetInteractionMode(EditorInteractionMode.SelectMode);
                        EditorSessionManager.CurrentGizmoMode = EditorGizmoMode.Translate;
                        EditorSessionManager.ShowNotification("Gizmo: [TRANSLATE]");
                    }
                    else if (Input.GetKeyDown(KeyCode.E))
                    {
                        EditorSessionManager.SetInteractionMode(EditorInteractionMode.SelectMode);
                        EditorSessionManager.CurrentGizmoMode = EditorGizmoMode.Rotate;
                        EditorSessionManager.ShowNotification("Gizmo: [ROTATE]");
                    }
                    else if (Input.GetKeyDown(KeyCode.R))
                    {
                        EditorSessionManager.SetInteractionMode(EditorInteractionMode.SelectMode);
                        EditorSessionManager.CurrentGizmoMode = EditorGizmoMode.Scale;
                        EditorSessionManager.ShowNotification("Gizmo: [SCALE]");
                    }
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
    // SECTION 2: HOLOGRAPHIC PREVIEW & ADJACENT PLACEMENT CONTROLLER
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
            Renderer[] rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                Bounds worldB = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++)
                {
                    if (rends[i] != null && !rends[i].gameObject.name.Contains("Proxy"))
                    {
                        worldB.Encapsulate(rends[i].bounds);
                    }
                }
                Vector3 localCenter = go.transform.InverseTransformPoint(worldB.center);
                Vector3 localSize = go.transform.InverseTransformVector(worldB.size);
                return new Bounds(localCenter, new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z)));
            }

            return new Bounds(Vector3.zero, new Vector3(2f, 2f, 2f));
        }

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

        public static void UpdatePlacement()
        {
            if (!EditorSessionManager.IsBlockSelected || _ghostInstance == null || EditorViewportCamera.ViewportCamera == null) return;
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

            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !StudioUIManager.IsPointerOverUI())
            {
                CommitPlacement(targetRot);
            }
        }

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
    // SECTION 3: REVOLUTIONARY SCREEN-SPACE PERSPECTIVE GIZMO SYSTEM
    // =========================================================================

    /// <summary>
    /// Renders transform gizmos as an un-cullable 2D overlay projected from 3D world space.
    /// Completely immune to shader stripping, lighting path discrepancies, and depth occlusion.
    /// </summary>
    public static class StudioGizmoController
    {
        private static GameObject _canvasObj = null;
        private static Canvas _overlayCanvas = null;
        private static GameObject _gizmoRoot = null;

        // UI Line Stems
        private static RectTransform _stemX = null;
        private static RectTransform _stemY = null;
        private static RectTransform _stemZ = null;
        private static Image _imgStemX = null;
        private static Image _imgStemY = null;
        private static Image _imgStemZ = null;

        // Interactive UI Knobs
        private static RectTransform _knobX = null;
        private static RectTransform _knobY = null;
        private static RectTransform _knobZ = null;
        private static Image _imgKnobX = null;
        private static Image _imgKnobY = null;
        private static Image _imgKnobZ = null;
        private static Text _textKnobX = null;
        private static Text _textKnobY = null;
        private static Text _textKnobZ = null;

        // Central Anchor
        private static RectTransform _centerRing = null;
        private static Image _imgCenterRing = null;

        // State Tracking
        private static int _activeDragAxis = -1; // 0=X, 1=Y, 2=Z, 3=Free/Center
        public static bool IsDraggingGizmo => _activeDragAxis != -1;
        public static bool IsHoveringHandle = false;

        private static Vector3 _dragStartObjPos = Vector3.zero;
        private static Vector3 _dragStartCenterPos = Vector3.zero;
        private static Quaternion _dragStartObjRot = Quaternion.identity;
        private static Vector3 _dragStartObjScale = Vector3.one;
        private static Vector2 _dragStartMousePos = Vector2.zero;

        // Visual Colors
        private static readonly Color ColorX = new Color(1.0f, 0.22f, 0.22f, 0.95f);
        private static readonly Color ColorY = new Color(0.22f, 0.95f, 0.35f, 0.95f);
        private static readonly Color ColorZ = new Color(0.25f, 0.60f, 1.0f, 0.95f);
        private static readonly Color ColorHover = new Color(1.0f, 0.95f, 0.15f, 1f);
        private static readonly Color ColorCenter = new Color(1.0f, 1.0f, 1.0f, 0.85f);

        /// <summary>
        /// Retrieves the exact world-space bounding box of the target hierarchy using active renderers.
        /// </summary>
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
                if (n.Contains("Proxy") || n.Contains("Highlight") || n.Contains("Beacon")) continue;

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
                b = new Bounds(obj.transform.position, Vector3.one * 2f);
            }

            return b;
        }

        /// <summary>
        /// Calculates the accurate geometric center of the target GameObject in world space.
        /// Surfaces slightly above floor slabs so gizmos never get buried in concrete.
        /// </summary>
        public static Vector3 GetObjectCenter(GameObject obj)
        {
            if (obj == null) return Vector3.zero;

            Bounds b = GetObjectWorldBounds(obj);
            Vector3 center = b.center;

            if (b.size.y < 3.0f && (b.size.x > 4.0f || b.size.z > 4.0f))
            {
                center.y = b.max.y + 0.2f;
            }

            return center;
        }

        private static void EnsureCanvasAndElements()
        {
            if (_canvasObj != null) return;

            _canvasObj = new GameObject("Studio_ScreenSpace_Gizmo_Canvas");
            _overlayCanvas = _canvasObj.AddComponent<Canvas>();
            _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = 950; // Below UI panels, above 3D world

            _canvasObj.AddComponent<CanvasScaler>();
            _canvasObj.AddComponent<GraphicRaycaster>();

            _gizmoRoot = new GameObject("Gizmo_Overlay_Root");
            _gizmoRoot.transform.SetParent(_canvasObj.transform, false);
            RectTransform rootRt = _gizmoRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.sizeDelta = Vector2.zero;

            // Stems
            _stemX = CreateStemUI(_gizmoRoot.transform, "Stem_X", ColorX, out _imgStemX);
            _stemY = CreateStemUI(_gizmoRoot.transform, "Stem_Y", ColorY, out _imgStemY);
            _stemZ = CreateStemUI(_gizmoRoot.transform, "Stem_Z", ColorZ, out _imgStemZ);

            // Knobs
            _knobX = CreateKnobUI(_gizmoRoot.transform, "Knob_X", "X", ColorX, out _imgKnobX, out _textKnobX);
            _knobY = CreateKnobUI(_gizmoRoot.transform, "Knob_Y", "Y", ColorY, out _imgKnobY, out _textKnobY);
            _knobZ = CreateKnobUI(_gizmoRoot.transform, "Knob_Z", "Z", ColorZ, out _imgKnobZ, out _textKnobZ);

            // Center Ring
            _centerRing = CreateCenterAnchorUI(_gizmoRoot.transform, "Center_Ring", ColorCenter, out _imgCenterRing);
        }

        private static RectTransform CreateStemUI(Transform parent, string name, Color c, out Image img)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.sizeDelta = new Vector2(80f, 6f); // 6px thick line

            img = obj.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;

            return rt;
        }

        private static RectTransform CreateKnobUI(Transform parent, string name, string label, Color c, out Image img, out Text txt)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.sizeDelta = new Vector2(28f, 28f); // 28x28px large target

            img = obj.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;

            GameObject textObj = new GameObject("Label");
            textObj.transform.SetParent(obj.transform, false);
            RectTransform trt = textObj.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;

            txt = textObj.AddComponent<Text>();
            txt.text = label;
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.fontSize = 15;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.raycastTarget = false;

            return rt;
        }

        private static RectTransform CreateCenterAnchorUI(Transform parent, string name, Color c, out Image img)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.sizeDelta = new Vector2(16f, 16f);

            img = obj.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;

            return rt;
        }

        /// <summary>
        /// Updates the 3D-to-2D projected handles, distance-scaling, hover highlights, and drag translations.
        /// </summary>
        public static void UpdateGizmo()
        {
            // Auto-select starting platform if none is selected
            if (EditorSessionManager.SelectedObject == null && EditorSessionManager.PlacedObjects.Count > 0)
            {
                if (EditorSessionManager.LastPlacedObject != null && EditorSessionManager.LastPlacedObject.activeSelf)
                {
                    EditorSessionManager.SelectObject(EditorSessionManager.LastPlacedObject);
                }
                else
                {
                    for (int i = 0; i < EditorSessionManager.PlacedObjects.Count; i++)
                    {
                        if (EditorSessionManager.PlacedObjects[i] != null && EditorSessionManager.PlacedObjects[i].activeSelf)
                        {
                            EditorSessionManager.SelectObject(EditorSessionManager.PlacedObjects[i]);
                            break;
                        }
                    }
                }
            }

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
                EditorSessionManager.CurrentGizmoMode = EditorGizmoMode.Translate;
            }

            EnsureCanvasAndElements();

            GameObject target = EditorSessionManager.SelectedObject;
            Camera cam = EditorViewportCamera.ViewportCamera;

            if (cam == null) return;

            // 1. Calculate 3D center and orientation
            Vector3 center3D = GetObjectCenter(target);
            Vector3 screenCenter3D = cam.WorldToScreenPoint(center3D);

            // Hide if behind the camera
            if (screenCenter3D.z <= 0.1f)
            {
                _gizmoRoot.SetActive(false);
                IsHoveringHandle = false;
                return;
            }

            _gizmoRoot.SetActive(true);

            Vector2 center2D = new Vector2(screenCenter3D.x, screenCenter3D.y);
            _centerRing.anchoredPosition = center2D;

            // 2. Determine world directions based on Gizmo Mode
            Vector3 dirX, dirY, dirZ;
            if (EditorSessionManager.CurrentGizmoMode == EditorGizmoMode.Scale)
            {
                dirX = target.transform.right;
                dirY = target.transform.up;
                dirZ = target.transform.forward;
            }
            else
            {
                dirX = Vector3.right;
                dirY = Vector3.up;
                dirZ = Vector3.forward;
            }

            // 3. Scale axis reach based on model bounds and scale multiplier
            Bounds b = GetObjectWorldBounds(target);
            float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
            float boundReach = Mathf.Max(1.6f, maxDim * 0.55f);
            float scaleMult = Mathf.Clamp(EditorSessionManager.ActivePlacementScale, 0.5f, 3.0f);
            float worldAxisLength = boundReach * scaleMult;

            // 4. Project 3D tips to 2D screen positions
            Vector3 tipX3D = cam.WorldToScreenPoint(center3D + dirX * worldAxisLength);
            Vector3 tipY3D = cam.WorldToScreenPoint(center3D + dirY * worldAxisLength);
            Vector3 tipZ3D = cam.WorldToScreenPoint(center3D + dirZ * worldAxisLength);

            Vector2 tipX2D = (tipX3D.z > 0.1f) ? new Vector2(tipX3D.x, tipX3D.y) : (center2D + Vector2.right * 70f);
            Vector2 tipY2D = (tipY3D.z > 0.1f) ? new Vector2(tipY3D.x, tipY3D.y) : (center2D + Vector2.up * 70f);
            Vector2 tipZ2D = (tipZ3D.z > 0.1f) ? new Vector2(tipZ3D.x, tipZ3D.y) : (center2D + new Vector2(0.7f, 0.7f) * 70f);

            // Clamp screen length so gizmo is never a microscopic dot or flying off the monitor
            tipX2D = EnforceScreenReach(center2D, tipX2D);
            tipY2D = EnforceScreenReach(center2D, tipY2D);
            tipZ2D = EnforceScreenReach(center2D, tipZ2D);

            // 5. Update UI Stems and Knobs
            UpdateStemAndKnob(_stemX, _knobX, center2D, tipX2D);
            UpdateStemAndKnob(_stemY, _knobY, center2D, tipY2D);
            UpdateStemAndKnob(_stemZ, _knobZ, center2D, tipZ2D);

            // 6. Handle Mouse Hover & Dragging
            int hovered = CheckScreenHover(center2D, tipX2D, tipY2D, tipZ2D);
            UpdateHandleColors(hovered);
            HandleScreenDragging(target, cam, center3D, dirX, dirY, dirZ, center2D, tipX2D, tipY2D, tipZ2D, hovered);
        }

        private static Vector2 EnforceScreenReach(Vector2 center, Vector2 tip)
        {
            Vector2 delta = tip - center;
            float dist = delta.magnitude;
            float clamped = Mathf.Clamp(dist, 70f, 260f); // Minimum 70px reach, max 260px
            return center + delta.normalized * clamped;
        }

        private static void UpdateStemAndKnob(RectTransform stem, RectTransform knob, Vector2 origin, Vector2 target)
        {
            Vector2 dir = target - origin;
            float dist = dir.magnitude;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            stem.anchoredPosition = origin;
            stem.sizeDelta = new Vector2(dist, 6f);
            stem.localRotation = Quaternion.Euler(0f, 0f, angle);

            knob.anchoredPosition = target;
        }

        private static int CheckScreenHover(Vector2 c, Vector2 x, Vector2 y, Vector2 z)
        {
            Vector2 mouse = Input.mousePosition;

            // Distance to knobs (radius 22px)
            if (Vector2.Distance(mouse, x) < 22f) return 0;
            if (Vector2.Distance(mouse, y) < 22f) return 1;
            if (Vector2.Distance(mouse, z) < 22f) return 2;

            // Distance to stems (within 10px of line)
            if (DistanceToSegment(mouse, c, x) < 10f) return 0;
            if (DistanceToSegment(mouse, c, y) < 10f) return 1;
            if (DistanceToSegment(mouse, c, z) < 10f) return 2;

            // Center ring
            if (Vector2.Distance(mouse, c) < 14f) return 3;

            return -1;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.001f) return Vector2.Distance(p, a);

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            Vector2 proj = a + t * ab;
            return Vector2.Distance(p, proj);
        }

        private static void UpdateHandleColors(int hovered)
        {
            int active = (_activeDragAxis != -1) ? _activeDragAxis : hovered;
            IsHoveringHandle = (active != -1);

            _imgStemX.color = (active == 0) ? ColorHover : ColorX;
            _imgKnobX.color = (active == 0) ? ColorHover : ColorX;

            _imgStemY.color = (active == 1) ? ColorHover : ColorY;
            _imgKnobY.color = (active == 1) ? ColorHover : ColorY;

            _imgStemZ.color = (active == 2) ? ColorHover : ColorZ;
            _imgKnobZ.color = (active == 2) ? ColorHover : ColorZ;

            _imgCenterRing.color = (active == 3) ? ColorHover : ColorCenter;
        }

        private static void HandleScreenDragging(
            GameObject target,
            Camera cam,
            Vector3 center3D,
            Vector3 dirX,
            Vector3 dirY,
            Vector3 dirZ,
            Vector2 center2D,
            Vector2 tipX2D,
            Vector2 tipY2D,
            Vector2 tipZ2D,
            int hovered)
        {
            // Mouse Down: Engage Drag
            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !StudioUIManager.IsPointerOverUI())
            {
                if (hovered != -1)
                {
                    _activeDragAxis = hovered;
                    _dragStartObjPos = target.transform.position;
                    _dragStartCenterPos = center3D;
                    _dragStartObjRot = target.transform.rotation;
                    _dragStartObjScale = target.transform.localScale;
                    _dragStartMousePos = Input.mousePosition;
                }
            }

            // Mouse Held: Process Real-Time 3D Transformation
            if (Input.GetMouseButton(0) && _activeDragAxis != -1)
            {
                Vector2 mouseDelta = (Vector2)Input.mousePosition - _dragStartMousePos;

                Vector3 chosenAxis3D = Vector3.right;
                Vector2 chosenStem2D = Vector2.right;

                if (_activeDragAxis == 0) { chosenAxis3D = dirX; chosenStem2D = (tipX2D - center2D).normalized; }
                else if (_activeDragAxis == 1) { chosenAxis3D = dirY; chosenStem2D = (tipY2D - center2D).normalized; }
                else if (_activeDragAxis == 2) { chosenAxis3D = dirZ; chosenStem2D = (tipZ2D - center2D).normalized; }

                float screenProj = Vector2.Dot(mouseDelta, chosenStem2D);
                float distToCam = Vector3.Distance(cam.transform.position, _dragStartCenterPos);
                float worldMoveDelta = (screenProj / 140f) * Mathf.Max(0.5f, distToCam * 0.12f);

                if (EditorSessionManager.CurrentGizmoMode == EditorGizmoMode.Translate)
                {
                    Vector3 targetPos = _dragStartObjPos + chosenAxis3D * worldMoveDelta;

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
                    float angle = (mouseDelta.x - mouseDelta.y) * 0.65f;
                    float grid = EditorSessionManager.CurrentGridSnap;
                    if (grid > 0.01f)
                    {
                        float step = (grid >= 2.0f) ? 45f : 15f;
                        angle = Mathf.Round(angle / step) * step;
                    }
                    target.transform.rotation = _dragStartObjRot * Quaternion.AngleAxis(angle, chosenAxis3D);
                }
                else if (EditorSessionManager.CurrentGizmoMode == EditorGizmoMode.Scale)
                {
                    float factor = 1.0f + (screenProj / 120f);
                    float newScale = Mathf.Max(0.01f, _dragStartObjScale.x * factor);

                    float grid = EditorSessionManager.CurrentGridSnap;
                    if (grid > 0.01f)
                    {
                        newScale = Mathf.Round(newScale / 0.1f) * 0.1f;
                    }

                    target.transform.localScale = Vector3.one * newScale;
                }

                StudioUIManager.RefreshInspectorValues();
                EditorSessionManager.UpdateSelectionHighlight();
            }

            // Mouse Up: Commit to Undo Stack
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

        public static void DestroyGizmo()
        {
            if (_canvasObj != null)
            {
                GameObject.Destroy(_canvasObj);
                _canvasObj = null;
                _overlayCanvas = null;
                _gizmoRoot = null;
            }
            _activeDragAxis = -1;
            IsHoveringHandle = false;
        }
    }
}