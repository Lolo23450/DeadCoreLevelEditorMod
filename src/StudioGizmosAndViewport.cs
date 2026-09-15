using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
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

        /// <summary>
        /// Spawns or re-binds the dedicated viewport camera from the player's active camera.
        /// </summary>
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

        /// <summary>
        /// Forces all layers to render and disables overlay cameras that could occlude gizmos.
        /// </summary>
        public static void EnsureCameraConfiguration()
        {
            if (ViewportCamera != null)
            {
                ViewportCamera.cullingMask = ~0; // Render ALL layers
                ViewportCamera.enabled = true;

                // Clear any target texture copied from native post-process cameras
                if (ViewportCamera.targetTexture != null)
                {
                    ViewportCamera.targetTexture = null;
                }

                // Disable any other cameras in scene that might draw over the viewport
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

        /// <summary>
        /// Processes freelook orientation, speed modifiers, and 6-axis camera translation.
        /// </summary>
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

                // W/E/R Hotkey shortcuts for transformation tools
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
    // SECTION 3: 3D INTERACTIVE TRANSFORMATION GIZMO CONTROLLER
    // =========================================================================

    /// <summary>
    /// Renders and manages interactive 3D handles for Move, Rotate, and Scale in Select Mode.
    /// Uses native scene shaders to guarantee rendering and automatically surfaces above platforms.
    /// </summary>
    public static class StudioGizmoController
    {
        private static GameObject _gizmoRoot = null;
        private static GameObject _axisX = null;
        private static GameObject _axisY = null;
        private static GameObject _axisZ = null;

        private static Renderer _cylRendX = null;
        private static Renderer _tipSphereRendX = null;
        private static Renderer _tipCubeRendX = null;

        private static Renderer _cylRendY = null;
        private static Renderer _tipSphereRendY = null;
        private static Renderer _tipCubeRendY = null;

        private static Renderer _cylRendZ = null;
        private static Renderer _tipSphereRendZ = null;
        private static Renderer _tipCubeRendZ = null;

        private static GameObject _tipSphereObjX = null;
        private static GameObject _tipCubeObjX = null;
        private static GameObject _tipSphereObjY = null;
        private static GameObject _tipCubeObjY = null;
        private static GameObject _tipSphereObjZ = null;
        private static GameObject _tipCubeObjZ = null;

        private static Material _matBaseX = null;
        private static Material _matBaseY = null;
        private static Material _matBaseZ = null;
        private static Material _matHover = null;

        private static int _activeDragAxis = -1; // 0=X, 1=Y, 2=Z
        public static bool IsDraggingGizmo => _activeDragAxis != -1;
        public static bool IsHoveringHandle = false;

        private static Vector3 _dragStartObjPos = Vector3.zero;
        private static Vector3 _dragStartCenterPos = Vector3.zero;
        private static Quaternion _dragStartObjRot = Quaternion.identity;
        private static Vector3 _dragStartObjScale = Vector3.one;
        private static Vector3 _dragStartMousePos = Vector3.zero;

        private static readonly Color ColorX = new Color(1.0f, 0.15f, 0.15f, 1f);
        private static readonly Color ColorY = new Color(0.15f, 1.0f, 0.25f, 1f);
        private static readonly Color ColorZ = new Color(0.15f, 0.55f, 1.0f, 1f);
        private static readonly Color ColorHover = new Color(1.0f, 0.95f, 0.1f, 1f);

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
                if (n.Contains("Proxy") || n.Contains("Highlight") || n.Contains("Beacon") || n.Contains("Gizmo")) continue;

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
        /// Calculates the accurate geometric center of the target GameObject in world space,
        /// surfacing slightly above flat platforms so X and Z handles are never buried in slabs.
        /// </summary>
        public static Vector3 GetObjectCenter(GameObject obj)
        {
            if (obj == null) return Vector3.zero;

            Bounds b = GetObjectWorldBounds(obj);
            Vector3 center = b.center;

            // If object is flat (like a floor slab), surface handles slightly above top face
            if (b.size.y < 3.0f && (b.size.x > 4.0f || b.size.z > 4.0f))
            {
                center.y = b.max.y + 0.2f;
            }

            return center;
        }

        /// <summary>
        /// Clones an active material from the scene (e.g. laser hazard or scene geometry)
        /// to guarantee the shader is compiled, unlit, and rendering in DeadCore's pipeline.
        /// </summary>
        private static Material CreateGizmoMaterial(Color col)
        {
            Material template = null;

            try
            {
                // 1. Try LaserManager material (unlit, glowing, emissive, highly visible)
                LaserManager lm = GameObject.FindObjectOfType<LaserManager>();
                if (lm != null && lm._sharedMaterial != null)
                {
                    template = lm._sharedMaterial;
                }
            }
            catch { }

            // 2. Try harvested scene geometry material
            if (template == null && EditorSessionManager.CachedSceneMaterial != null)
            {
                template = EditorSessionManager.CachedSceneMaterial;
            }

            // 3. Fallback: borrow from any active MeshRenderer in the scene
            if (template == null)
            {
                MeshRenderer[] rends = GameObject.FindObjectsOfType<MeshRenderer>();
                for (int i = 0; i < rends.Length; i++)
                {
                    if (rends[i] != null && rends[i].sharedMaterial != null)
                    {
                        template = rends[i].sharedMaterial;
                        break;
                    }
                }
            }

            Material m;
            if (template != null)
            {
                m = new Material(template);
            }
            else
            {
                Shader s = Shader.Find("Diffuse") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
                m = new Material(s);
            }

            m.name = $"Gizmo_Mat_{col.r:F2}_{col.g:F2}_{col.b:F2}";

            // Ensure solid white texture so shaders that multiply by _MainTex don't sample 0-alpha
            m.mainTexture = Texture2D.whiteTexture;
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", Texture2D.whiteTexture);

            m.color = col;
            if (m.HasProperty("_Color")) m.SetColor("_Color", col);
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", col);

            // Enable emission so handles glow vibrantly even in unlit/dark environments
            if (m.HasProperty("_EmissionColor"))
            {
                m.SetColor("_EmissionColor", col * 2.2f);
                m.EnableKeyword("_EMISSION");
            }

            m.renderQueue = 5000; // Overlay Queue
            return m;
        }

        /// <summary>
        /// Updates gizmo handle transforms, distance/model scaling, and processes drag inputs.
        /// </summary>
        public static void UpdateGizmo()
        {
            // Auto-select starting/placed platform if entering edit mode with nothing selected
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

            // If in SelectMode with Select tool active, auto-promote to Translate so handles show immediately
            if (EditorSessionManager.CurrentGizmoMode == EditorGizmoMode.Select)
            {
                EditorSessionManager.CurrentGizmoMode = EditorGizmoMode.Translate;
            }

            EnsureGizmoInstances();

            GameObject target = EditorSessionManager.SelectedObject;

            // CENTER GIZMO ON OBJECT'S VISUAL BOUNDS
            Vector3 centerPos = GetObjectCenter(target);
            _gizmoRoot.transform.position = centerPos;

            if (EditorSessionManager.CurrentGizmoMode == EditorGizmoMode.Scale)
            {
                _gizmoRoot.transform.rotation = target.transform.rotation;
            }
            else
            {
                _gizmoRoot.transform.rotation = Quaternion.identity;
            }

            // SCALE GIZMO BASED ON CAMERA DISTANCE, OBJECT BOUNDS, AND PLACEMENT SCALE MULTIPLIER
            if (EditorViewportCamera.ViewportCamera != null)
            {
                Camera cam = EditorViewportCamera.ViewportCamera;
                float dist = Vector3.Distance(cam.transform.position, centerPos);
                float screenScale = Mathf.Clamp(dist * 0.11f, 0.9f, 25.0f);

                Bounds b = GetObjectWorldBounds(target);
                float maxModelDim = Mathf.Max(b.size.x, b.size.y, b.size.z);
                float boundFactor = 1.0f;
                if (maxModelDim > 2.0f)
                {
                    boundFactor = Mathf.Clamp(maxModelDim * 0.20f, 1.0f, 4.0f);
                }

                float scaleMult = Mathf.Clamp(EditorSessionManager.ActivePlacementScale, 0.5f, 3.0f);

                float finalScale = screenScale * boundFactor * scaleMult;
                _gizmoRoot.transform.localScale = Vector3.one * finalScale;
            }

            // TOGGLE TIPS: SPHERES FOR TRANSLATE/ROTATE VS CUBES FOR SCALE
            bool isScale = (EditorSessionManager.CurrentGizmoMode == EditorGizmoMode.Scale);
            if (_tipSphereObjX != null) _tipSphereObjX.SetActive(!isScale);
            if (_tipCubeObjX != null) _tipCubeObjX.SetActive(isScale);
            if (_tipSphereObjY != null) _tipSphereObjY.SetActive(!isScale);
            if (_tipCubeObjY != null) _tipCubeObjY.SetActive(isScale);
            if (_tipSphereObjZ != null) _tipSphereObjZ.SetActive(!isScale);
            if (_tipCubeObjZ != null) _tipCubeObjZ.SetActive(isScale);

            _gizmoRoot.SetActive(true);

            int hoveredAxis = CheckHandleHover();
            UpdateHandleColors(hoveredAxis);
            HandleGizmoDragging(target, hoveredAxis);
        }

        private static void EnsureGizmoInstances()
        {
            if (_gizmoRoot != null) return;

            _matBaseX = CreateGizmoMaterial(ColorX);
            _matBaseY = CreateGizmoMaterial(ColorY);
            _matBaseZ = CreateGizmoMaterial(ColorZ);
            _matHover = CreateGizmoMaterial(ColorHover);

            _gizmoRoot = new GameObject("Studio_Transform_Gizmo_Root");
            _gizmoRoot.layer = 0; // Layer 0 = Default (Never culled by game camera)

            _axisX = CreateAxisHandle(_gizmoRoot.transform, "Gizmo_Handle_X", Vector3.right, _matBaseX,
                out _cylRendX, out _tipSphereRendX, out _tipCubeRendX, out _tipSphereObjX, out _tipCubeObjX);

            _axisY = CreateAxisHandle(_gizmoRoot.transform, "Gizmo_Handle_Y", Vector3.up, _matBaseY,
                out _cylRendY, out _tipSphereRendY, out _tipCubeRendY, out _tipSphereObjY, out _tipCubeObjY);

            _axisZ = CreateAxisHandle(_gizmoRoot.transform, "Gizmo_Handle_Z", Vector3.forward, _matBaseZ,
                out _cylRendZ, out _tipSphereRendZ, out _tipCubeRendZ, out _tipSphereObjZ, out _tipCubeObjZ);
        }

        private static GameObject CreateAxisHandle(
            Transform parent,
            string name,
            Vector3 dir,
            Material baseMat,
            out Renderer cylRend,
            out Renderer tipSphereRend,
            out Renderer tipCubeRend,
            out GameObject tipSphereObj,
            out GameObject tipCubeObj)
        {
            GameObject handle = new GameObject(name);
            handle.transform.SetParent(parent, false);
            handle.layer = 0;

            // Thick Shaft Cylinder
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = "Shaft";
            cylinder.layer = 0;
            cylinder.transform.SetParent(handle.transform, false);
            cylinder.transform.localScale = new Vector3(0.18f, 1.20f, 0.18f);
            cylinder.transform.localPosition = dir * 1.20f;
            cylinder.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);

            Collider col = cylinder.GetComponent<Collider>();
            if (col != null) col.isTrigger = false;

            cylRend = cylinder.GetComponent<Renderer>();
            cylRend.material = baseMat;
            cylRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cylRend.receiveShadows = false;

            // Bold Translate Tip (Sphere)
            tipSphereObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tipSphereObj.name = "Tip_Sphere";
            tipSphereObj.layer = 0;
            tipSphereObj.transform.SetParent(handle.transform, false);
            tipSphereObj.transform.localScale = new Vector3(0.65f, 0.65f, 0.65f);
            tipSphereObj.transform.localPosition = dir * 2.65f;

            Collider tCol = tipSphereObj.GetComponent<Collider>();
            if (tCol != null) tCol.isTrigger = false;

            tipSphereRend = tipSphereObj.GetComponent<Renderer>();
            tipSphereRend.material = baseMat;
            tipSphereRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tipSphereRend.receiveShadows = false;

            // Bold Scale Tip (Cube)
            tipCubeObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tipCubeObj.name = "Tip_Cube";
            tipCubeObj.layer = 0;
            tipCubeObj.transform.SetParent(handle.transform, false);
            tipCubeObj.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f);
            tipCubeObj.transform.localPosition = dir * 2.65f;
            tipCubeObj.transform.localRotation = Quaternion.identity;

            Collider cCol = tipCubeObj.GetComponent<Collider>();
            if (cCol != null) cCol.isTrigger = false;

            tipCubeRend = tipCubeObj.GetComponent<Renderer>();
            tipCubeRend.material = baseMat;
            tipCubeRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tipCubeRend.receiveShadows = false;

            tipCubeObj.SetActive(false);

            return handle;
        }

        /// <summary>
        /// Raycasts across all layers to cleanly detect hovering over gizmo handles.
        /// </summary>
        private static int CheckHandleHover()
        {
            if (EditorViewportCamera.ViewportCamera == null || _gizmoRoot == null || !_gizmoRoot.activeSelf)
            {
                IsHoveringHandle = false;
                return -1;
            }

            Ray ray = EditorViewportCamera.ViewportCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit[] hits = Physics.RaycastAll(ray, 5000f, ~0, QueryTriggerInteraction.Collide);

            int closestAxis = -1;
            float closestDist = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (c == null) continue;

                Transform tr = c.transform;
                int axis = -1;

                if (_axisX != null && tr.IsChildOf(_axisX.transform)) axis = 0;
                else if (_axisY != null && tr.IsChildOf(_axisY.transform)) axis = 1;
                else if (_axisZ != null && tr.IsChildOf(_axisZ.transform)) axis = 2;

                if (axis != -1 && hits[i].distance < closestDist)
                {
                    closestDist = hits[i].distance;
                    closestAxis = axis;
                }
            }

            IsHoveringHandle = (closestAxis != -1);
            return closestAxis;
        }

        private static void UpdateHandleColors(int hoveredAxis)
        {
            int targetAxis = (_activeDragAxis != -1) ? _activeDragAxis : hoveredAxis;

            if (_cylRendX != null) _cylRendX.material = (targetAxis == 0) ? _matHover : _matBaseX;
            if (_tipSphereRendX != null) _tipSphereRendX.material = (targetAxis == 0) ? _matHover : _matBaseX;
            if (_tipCubeRendX != null) _tipCubeRendX.material = (targetAxis == 0) ? _matHover : _matBaseX;

            if (_cylRendY != null) _cylRendY.material = (targetAxis == 1) ? _matHover : _matBaseY;
            if (_tipSphereRendY != null) _tipSphereRendY.material = (targetAxis == 1) ? _matHover : _matBaseY;
            if (_tipCubeRendY != null) _tipCubeRendY.material = (targetAxis == 1) ? _matHover : _matBaseY;

            if (_cylRendZ != null) _cylRendZ.material = (targetAxis == 2) ? _matHover : _matBaseZ;
            if (_tipSphereRendZ != null) _tipSphereRendZ.material = (targetAxis == 2) ? _matHover : _matBaseZ;
            if (_tipCubeRendZ != null) _tipCubeRendZ.material = (targetAxis == 2) ? _matHover : _matBaseZ;
        }

        private static void HandleGizmoDragging(GameObject target, int hoveredAxis)
        {
            if (EditorViewportCamera.ViewportCamera == null) return;
            Camera cam = EditorViewportCamera.ViewportCamera;

            // Mouse down: Start drag
            if (Input.GetMouseButtonDown(0) && !Input.GetMouseButton(1) && !StudioUIManager.IsPointerOverUI())
            {
                if (hoveredAxis != -1)
                {
                    _activeDragAxis = hoveredAxis;
                    _dragStartObjPos = target.transform.position;
                    _dragStartCenterPos = GetObjectCenter(target);
                    _dragStartObjRot = target.transform.rotation;
                    _dragStartObjScale = target.transform.localScale;
                    _dragStartMousePos = Input.mousePosition;
                }
            }

            // Mouse held down: Process transform modification
            if (Input.GetMouseButton(0) && _activeDragAxis != -1)
            {
                Vector3 delta = Input.mousePosition - _dragStartMousePos;
                Vector3 worldAxis = (_activeDragAxis == 0) ? Vector3.right : (_activeDragAxis == 1 ? Vector3.up : Vector3.forward);

                if (EditorSessionManager.CurrentGizmoMode == EditorGizmoMode.Translate)
                {
                    Vector3 screenAxis = cam.WorldToScreenPoint(_dragStartCenterPos + worldAxis) - cam.WorldToScreenPoint(_dragStartCenterPos);
                    screenAxis.z = 0f;

                    float proj = Vector3.Dot(delta, screenAxis.normalized);
                    float distFactor = Vector3.Distance(cam.transform.position, _dragStartCenterPos) * 0.0018f;
                    Vector3 targetDelta = worldAxis * (proj * distFactor);

                    float grid = EditorSessionManager.CurrentGridSnap;
                    if (grid > 0.01f)
                    {
                        Vector3 potentialPos = _dragStartObjPos + targetDelta;
                        potentialPos = new Vector3(
                            Mathf.Round(potentialPos.x / grid) * grid,
                            Mathf.Round(potentialPos.y / grid) * grid,
                            Mathf.Round(potentialPos.z / grid) * grid
                        );
                        target.transform.position = potentialPos;
                    }
                    else
                    {
                        target.transform.position = _dragStartObjPos + targetDelta;
                    }
                }
                else if (EditorSessionManager.CurrentGizmoMode == EditorGizmoMode.Rotate)
                {
                    float angle = (delta.x - delta.y) * 0.55f;
                    float grid = EditorSessionManager.CurrentGridSnap;
                    if (grid > 0.01f)
                    {
                        float step = (grid >= 2.0f) ? 45f : 15f;
                        angle = Mathf.Round(angle / step) * step;
                    }
                    target.transform.rotation = _dragStartObjRot * Quaternion.AngleAxis(angle, worldAxis);
                }
                else if (EditorSessionManager.CurrentGizmoMode == EditorGizmoMode.Scale)
                {
                    Vector3 screenAxis = cam.WorldToScreenPoint(_dragStartCenterPos + worldAxis) - cam.WorldToScreenPoint(_dragStartCenterPos);
                    screenAxis.z = 0f;

                    float proj = Vector3.Dot(delta, screenAxis.normalized);
                    float distFactor = Vector3.Distance(cam.transform.position, _dragStartCenterPos) * 0.0018f;

                    // Proportional scaling based on object's existing model scale and scale multiplier
                    float scaleFactor = 1.0f + (proj * distFactor * 1.5f);
                    float newScale = Mathf.Max(0.02f, _dragStartObjScale.x * scaleFactor);

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

            // Mouse released: Commit to Undo stack
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

                _cylRendX = null;
                _tipSphereRendX = null;
                _tipCubeRendX = null;

                _cylRendY = null;
                _tipSphereRendY = null;
                _tipCubeRendY = null;

                _cylRendZ = null;
                _tipSphereRendZ = null;
                _tipCubeRendZ = null;

                _tipSphereObjX = null;
                _tipCubeObjX = null;
                _tipSphereObjY = null;
                _tipCubeObjY = null;
                _tipSphereObjZ = null;
                _tipCubeObjZ = null;
            }
            _activeDragAxis = -1;
            IsHoveringHandle = false;
        }
    }
}