using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Game/ASCII World Mode Manager")]
public class ASCIIWorldModeManager : MonoBehaviour
{
    [System.Serializable]
    private struct DragTransformSnapshot
    {
        public Vector3 worldPosition;
        public Quaternion worldRotation;
    }

    private struct RigidbodyPauseSnapshot
    {
        public bool isKinematic;
        public bool useGravity;
        public Vector3 velocity;
        public Vector3 angularVelocity;
    }

    private struct ProjectionEnterSnapshot
    {
        public Vector3 worldPosition;
        public Quaternion worldRotation;
    }

    [Header("模式")]
    public KeyCode toggleProjectionModeKey = KeyCode.E;
    public KeyCode holdAimKey = KeyCode.Mouse1;

    [Header("状态")]
    public bool isProjectionMode = false;
    public bool isAimMode = false;

    public bool InProjectionView => isProjectionMode || isAimMode;
    public bool IsProjectionMode => isProjectionMode;
    public bool IsAimMode => isAimMode;

    [Header("引用")]
    public Camera targetCamera;
    public ProjectionAnalysisBridge projectionAnalysisBridge;
    public ASCIIWorldRegistryManager registryManager;

    [Header("投影拖动")]
    public Transform projectionDragPivot;
    public LayerMask projectionDraggingLayer;
    public LayerMask projectionInvalidLayer;
    public LayerMask projectionInvalidWithSolidLayer;

    [Header("投影中心射线遮挡")]
    [Tooltip("屏幕中心射线选中 Projection / ProjectionDragging 时，会被这些 Layer 阻挡。")] 
    public LayerMask projectionCenterRayOccluderMask;

    [Tooltip("屏幕中心射线选中 Projection_Invalid 时，会被这些 Layer 阻挡。")] 
    public LayerMask projectionInvalidCenterRayOccluderMask;

    [Header("投影非法判定")]
    [Tooltip("拖动投影时，若与这些 Layer 上的物体发生重合，则判定为非法。")]
    public LayerMask projectionInvalidOverlapMask;

    [Tooltip("Projection 非法判定时，ComputePenetration 返回的穿透深度至少达到该值才视为真正非法。")]
    [Min(0f)] public float projectionInvalidPenetrationThreshold = 0.02f;

    [Tooltip("若启用相对阈值，则 Projection 非法穿透阈值 = 当前 Collider 最小世界轴尺寸 * 该比例，与固定阈值取较大值。")]
    [Range(0f, 0.5f)] public float projectionInvalidPenetrationThresholdRatio = 0.05f;

    [Tooltip("是否启用相对阈值。关闭后，Projection / Solid / 归位销毁判定都只使用各自的固定穿透阈值，不再叠加基于 Collider 尺寸的比例阈值。")]
    public bool useRelativePenetrationThreshold = true;

    [Tooltip("Virtual 刚切到 Projection 后，若位移和旋转变化都很小，则暂时忽略轻微重合，避免贴地静置误判。")]
    public bool enableProjectionInitialPoseTolerance = true;

    [Tooltip("Projection 相比刚切入时，位移至少超过该值后，才取消“初始静止容忍”。")]
    [Min(0f)] public float projectionInitialPoseMoveTolerance = 0.02f;

    [Tooltip("Projection 相比刚切入时，旋转角度至少超过该值后，才取消“初始静止容忍”。")]
    [Min(0f)] public float projectionInitialPoseRotateTolerance = 2f;

    [Header("Solid 非法判定")]
    [Tooltip("拖动时，被带动的 Solid 若与这些 Layer 上的物体发生重合，则判定为 Solid 非法。")]
    public LayerMask solidInvalidOverlapMask;

    [Tooltip("Solid 非法判定时，ComputePenetration 返回的穿透深度至少达到该值才视为真正非法。")]
    [Min(0f)] public float solidInvalidPenetrationThreshold = 0.02f;

    [Tooltip("若启用相对阈值，则 Solid 非法穿透阈值 = 当前 Collider 最小世界轴尺寸 * 该比例，与固定阈值取较大值。")]
    [Range(0f, 0.5f)] public float solidInvalidPenetrationThresholdRatio = 0.05f;

    [Tooltip("非法重合检测是否把 Trigger 也算进去。")]
    public bool includeTriggerCollidersInInvalidCheck = false;
    [Tooltip("松开鼠标左键时，是否额外执行一次“缩盒核心区补判”。")]
    public bool enableProjectionReleaseShrinkCoreCheck = true;

    [Tooltip("松手时缩盒核心区补判所使用的 LayerMask。0 表示沿用 projectionInvalidOverlapMask。")]
    public LayerMask projectionReleaseShrinkCoreMask = 0;

    [Header("归位后销毁")]
    [Tooltip("当被投影带动的 Solid 因非法而归位时，若归位位置仍与其他碰撞体重合，则直接销毁该 Solid。")]
    public bool destroyCoveredSolidIfRestoreOverlaps = true;

    [Tooltip("归位后销毁判定所使用的 LayerMask。0 表示沿用 solidInvalidOverlapMask。")]
    public LayerMask destroyCoveredSolidRestoreOverlapMask = 0;

    [Tooltip("Solid 归位后销毁判定时，ComputePenetration 返回的穿透深度至少达到该值才视为真正需要销毁。建议不小于 Solid 非法阈值。")]
    [Min(0f)] public float solidRestoreDestroyPenetrationThreshold = 0.03f;

    [Tooltip("若启用相对阈值，则 Solid 归位销毁穿透阈值 = 当前 Collider 最小世界轴尺寸 * 该比例，与固定阈值取较大值。")]
    [Range(0f, 0.5f)] public float solidRestoreDestroyPenetrationThresholdRatio = 0.06f;

    [Header("被投影遮挡的 Solid 跟随拖动")]
    [Tooltip("拖拽投影集时，是否让被当前投影集覆盖的 Solid 一起跟随移动。")]
    public bool moveCoveredSolidsAlongWithDraggedProjection = true;
    [Tooltip("判定 Solid 被当前投影集充分覆盖所需的最小覆盖比例。")]
    [Range(0f, 1f)] public float solidFullyCoveredThreshold = 0.995f;

    [Header("投影模式区域（Viewport）")]
    [Range(0f, 1f)] public float regionCenterX = 0.5f;
    [Range(0f, 1f)] public float regionCenterY = 0.5f;
    [Range(0.01f, 1f)] public float regionWidth = 0.3f;
    [Range(0.01f, 1f)] public float regionHeight = 0.3f;

    [Header("Bounds 判定阈值")]
    [Range(0f, 1f)] public float switchToProjectionRatio = 1.0f;
    [Range(0f, 1f)] public float switchBackRatio = 0.05f;

    [Header("Bounds 采样")]
    [Range(2, 8)] public int sampleGridX = 4;
    [Range(2, 8)] public int sampleGridY = 4;

    [Header("瞄准模式：Solid 转 Virtual")]
    [Tooltip("瞄准模式下，从屏幕中心发射射线时的最大检测距离。")]
    public float aimConvertRayDistance = 100f;
    [Tooltip("瞄准模式下，允许被射线命中并尝试转换为 Virtual 的 Layer。")]
    public LayerMask aimConvertTargetLayers = ~0;

    [Header("Virtual / Projection 区域判定刷新")]
    [Tooltip("投影模式下，按固定间隔刷新一次 Virtual / Projection 的区域切换判定。")]
    public float virtualProjectionSwitchInterval = 0.05f;

    [Header("Q键：范围 Virtual 回实预览")]
    [Tooltip("是否启用 Q 键范围内 Virtual 回实预览与确认。")] 
    public bool enableRevertNearbyVirtualByKey = true;

    [Tooltip("触发范围 Virtual 回实预览 / 确认的按键。")] 
    public KeyCode revertNearbyVirtualKey = KeyCode.Q;

    [Tooltip("Q 键扫描中心。为空时默认使用当前物体 Transform。")] 
    public Transform revertNearbyVirtualCenter;

    [Tooltip("Q 键扫描半径。")] 
    [Min(0f)] public float revertNearbyVirtualRadius = 3f;

    [Tooltip("Q 键第一次点按后，预览确认持续时长。")] 
    [Min(0.01f)] public float revertNearbyVirtualPreviewDuration = 2f;

    [Tooltip("预览期间刷新候选 Virtual 的间隔。")] 
    [Min(0.01f)] public float revertNearbyVirtualScanInterval = 0.05f;

    [Header("投影模式时停")]
    [Tooltip("进入投影模式时，是否冻结当前所有 Solid 状态对象的 Rigidbody。")]
    public bool freezeSolidRigidbodiesInProjectionMode = true;

    [Tooltip("进入/退出投影模式时，是否向场景内暂停接收器广播。敌人后续可通过实现 IProjectionModePauseReceiver 接口接入。")]
    public bool notifyProjectionModePauseReceivers = true;

    [Tooltip("查找暂停接收器时是否包含未激活对象。")]
    public bool includeInactivePauseReceivers = true;

    [Header("运行时输出（只读）")]
    [SerializeField] private ASCIIWorldObject currentCenterProjectionObject;
    [SerializeField] private List<ASCIIWorldObject> selectedProjectionObjects = new List<ASCIIWorldObject>();
    [SerializeField] private List<ASCIIWorldObject> coveredSolidObjects = new List<ASCIIWorldObject>();
    [SerializeField] private List<ASCIIWorldObject> connectedProjectionObjects = new List<ASCIIWorldObject>();

    private float scanTimer = 0f;
    private bool skipBoundsSwitchThisFrame = false;

    private readonly ProjectionAnalyzer analyzer = new ProjectionAnalyzer();

    private bool waitingForConnectedAnalysis = false;
    private bool connectedSelectionLocked = false;
    private ASCIIWorldObject lockedSeedProjectionObject;
    private int waitingStartFrameVersion = -1;

    private bool dragActionStarted = false;
    private bool isDraggingConnectedGroup = false;
    private bool isCurrentDragInvalid = false;
    private bool isCurrentDragSolidInvalid = false;

    private readonly Collider[] invalidOverlapCandidateBuffer = new Collider[128];
    private readonly Collider[] aimWakeCandidateBuffer = new Collider[128];
    private readonly List<Collider> currentInvalidOverlapColliders = new List<Collider>();
    private readonly List<ASCIIWorldObject> currentInvalidProjectionObjects = new List<ASCIIWorldObject>();

    private readonly Dictionary<ASCIIWorldObject, Transform> originalParentByObject = new Dictionary<ASCIIWorldObject, Transform>();
    private readonly Dictionary<ASCIIWorldObject, int> originalSiblingIndexByObject = new Dictionary<ASCIIWorldObject, int>();
    private readonly Dictionary<ASCIIWorldObject, DragTransformSnapshot> dragStartSnapshotByObject = new Dictionary<ASCIIWorldObject, DragTransformSnapshot>();
    private readonly Dictionary<ASCIIWorldObject, ProjectionEnterSnapshot> projectionEnterSnapshotByObject = new Dictionary<ASCIIWorldObject, ProjectionEnterSnapshot>();
    private readonly HashSet<ASCIIWorldObject> attachedDraggedObjects = new HashSet<ASCIIWorldObject>();
    private readonly HashSet<Collider> draggedColliderSet = new HashSet<Collider>();
    private readonly Dictionary<Rigidbody, RigidbodyPauseSnapshot> pausedSolidRigidbodySnapshots = new Dictionary<Rigidbody, RigidbodyPauseSnapshot>();
    private readonly Dictionary<ASCIIWorldObject, GameObject> coveredSolidVirtualGhostRoots = new Dictionary<ASCIIWorldObject, GameObject>();
    private readonly List<ASCIIWorldObject> pendingDestroyCoveredSolidObjects = new List<ASCIIWorldObject>();
    private readonly HashSet<int> persistentOcclusionInvalidProjectionIds = new HashSet<int>();
    private readonly HashSet<int> releaseOcclusionHoldProjectionIds = new HashSet<int>();
    private readonly List<ASCIIWorldObject> pendingReleaseOcclusionCheckObjects = new List<ASCIIWorldObject>();

    private bool waitingForReleaseOcclusionSnapshot = false;
    private int pendingReleaseOcclusionFrameVersion = -1;
    private bool projectionModePauseApplied = false;

    private bool revertNearbyVirtualPreviewActive = false;
    private float revertNearbyVirtualPreviewExpireTime = 0f;
    private float revertNearbyVirtualPreviewScanTimer = 0f;
    private readonly List<ASCIIWorldObject> revertNearbyVirtualPreviewCandidates = new List<ASCIIWorldObject>();
    private bool externalAimInputPressed = false;

    public IReadOnlyList<ASCIIWorldObject> ActiveVirtualObjects
    {
        get
        {
            if (registryManager == null)
                return System.Array.Empty<ASCIIWorldObject>();
            return registryManager.RuntimeVirtualObjects;
        }
    }

    public ASCIIWorldObject CurrentCenterProjectionObject => currentCenterProjectionObject;
    public IReadOnlyList<ASCIIWorldObject> SelectedProjectionObjects => selectedProjectionObjects;
    public IReadOnlyList<ASCIIWorldObject> CoveredSolidObjects => coveredSolidObjects;
    public IReadOnlyList<ASCIIWorldObject> ConnectedProjectionObjects => connectedProjectionObjects;
    public IReadOnlyList<Collider> CurrentInvalidOverlapColliders => currentInvalidOverlapColliders;
    public IReadOnlyList<ASCIIWorldObject> CurrentInvalidProjectionObjects => currentInvalidProjectionObjects;
    public bool IsRevertNearbyVirtualPreviewActive => revertNearbyVirtualPreviewActive;
    public IReadOnlyList<ASCIIWorldObject> RevertNearbyVirtualPreviewObjects => revertNearbyVirtualPreviewCandidates;
    public float RevertNearbyVirtualPreviewRemainingTime => revertNearbyVirtualPreviewActive ? Mathf.Max(0f, revertNearbyVirtualPreviewExpireTime - Time.time) : 0f;

    public bool TryTriggerRevertNearbyVirtualAction()
    {
        if (registryManager == null || !enableRevertNearbyVirtualByKey)
            return false;

        if (revertNearbyVirtualPreviewActive)
            ConfirmRevertNearbyVirtualPreview();
        else
            BeginRevertNearbyVirtualPreview();

        return true;
    }

    public bool TryToggleProjectionModeAction()
    {
        if (isProjectionMode)
        {
            ExitProjectionMode();
            return true;
        }

        if (isAimMode)
            return false;

        EnterProjectionMode();
        return true;
    }

    public void SetExternalAimInputPressed(bool pressed)
    {
        externalAimInputPressed = pressed;
    }

    private void Awake()
    {
        if (targetCamera == null && Camera.main != null)
            targetCamera = Camera.main;
    }

    private void OnValidate()
    {
        if (sampleGridX < 2) sampleGridX = 2;
        if (sampleGridY < 2) sampleGridY = 2;
    }

    private void Update()
    {
        skipBoundsSwitchThisFrame = false;

        HandleModeInput();
        CleanupNulls();

        if (registryManager == null)
            return;

        UpdateRevertNearbyVirtualPreviewState();

        if (enableRevertNearbyVirtualByKey && Input.GetKeyDown(revertNearbyVirtualKey))
        {
            if (revertNearbyVirtualPreviewActive)
                ConfirmRevertNearbyVirtualPreview();
            else
                BeginRevertNearbyVirtualPreview();
        }

        if (IsAimMode)
        {
            if (Input.GetKeyDown(KeyCode.Mouse0))
                TryPickSolidToVirtual();

            ClearProjectionSelection();
            return;
        }

        if (!IsProjectionMode)
        {
            EndDragConnectedGroup(false);
            RestoreRuntimeProjectionsToVirtual();
            ClearProjectionSelection();
            return;
        }

        if (isDraggingConnectedGroup)
            UpdateDraggedProjectionInvalidState();

        if (Input.GetKeyUp(KeyCode.Mouse0) && dragActionStarted)
        {
            StopCurrentDragButStayInProjectionMode();
            return;
        }

        if (!connectedSelectionLocked)
            UpdateCenterProjectionRaycast();

        if (Input.GetKeyDown(KeyCode.Mouse0))
        {
            bool started = StartConnectedSelectionOnce();
            if (started)
                return;
        }

        if (waitingForConnectedAnalysis)
            TryResolveConnectedSelectionAndBeginDrag();

        if (waitingForReleaseOcclusionSnapshot)
            TryResolveReleaseOcclusionSnapshot();

        scanTimer += Time.deltaTime;
        if (scanTimer >= virtualProjectionSwitchInterval)
        {
            scanTimer = 0f;

            if (!skipBoundsSwitchThisFrame)
                UpdateVirtualProjectionSwitchingByBounds();

            RefreshPersistentOcclusionInvalidProjections();
        }
    }

    private void HandleModeInput()
    {
        bool aimHeld = Input.GetKey(holdAimKey) || externalAimInputPressed;

        if (IsAimMode)
        {
            isAimMode = aimHeld;
            return;
        }

        if (IsProjectionMode)
        {
            if (Input.GetKeyDown(toggleProjectionModeKey))
                ExitProjectionMode();

            isAimMode = false;
            return;
        }

        if (Input.GetKeyDown(toggleProjectionModeKey))
        {
            EnterProjectionMode();
            return;
        }

        isAimMode = aimHeld;
    }

    private void BeginRevertNearbyVirtualPreview()
    {
        revertNearbyVirtualPreviewActive = true;
        revertNearbyVirtualPreviewExpireTime = Time.time + Mathf.Max(0.01f, revertNearbyVirtualPreviewDuration);
        revertNearbyVirtualPreviewScanTimer = 0f;
        RefreshRevertNearbyVirtualPreviewCandidates();
    }

    private void ConfirmRevertNearbyVirtualPreview()
    {
        List<ASCIIWorldObject> candidates = new List<ASCIIWorldObject>(revertNearbyVirtualPreviewCandidates);
        CancelRevertNearbyVirtualPreview();

        bool changedAny = false;

        for (int i = 0; i < candidates.Count; i++)
        {
            ASCIIWorldObject worldObject = candidates[i];
            if (worldObject == null)
                continue;

            if (!IsEligibleForNearbyVirtualRevert(worldObject))
                continue;

            registryManager.RestoreVirtualToSolidAndDestroyIfOverlapping(worldObject);

            if (worldObject != null && projectionModePauseApplied && freezeSolidRigidbodiesInProjectionMode)
                ReapplyProjectionModeFreezeToSolidObject(worldObject);

            changedAny = true;
        }

        if (changedAny)
            Physics.SyncTransforms();
    }

    private void CancelRevertNearbyVirtualPreview()
    {
        revertNearbyVirtualPreviewActive = false;
        revertNearbyVirtualPreviewExpireTime = 0f;
        revertNearbyVirtualPreviewScanTimer = 0f;
        revertNearbyVirtualPreviewCandidates.Clear();
    }

    private void UpdateRevertNearbyVirtualPreviewState()
    {
        if (!revertNearbyVirtualPreviewActive)
            return;

        if (Time.time >= revertNearbyVirtualPreviewExpireTime)
        {
            CancelRevertNearbyVirtualPreview();
            return;
        }

        revertNearbyVirtualPreviewScanTimer -= Time.deltaTime;
        if (revertNearbyVirtualPreviewScanTimer <= 0f)
        {
            revertNearbyVirtualPreviewScanTimer = Mathf.Max(0.01f, revertNearbyVirtualScanInterval);
            RefreshRevertNearbyVirtualPreviewCandidates();
        }
    }

    private void RefreshRevertNearbyVirtualPreviewCandidates()
    {
        revertNearbyVirtualPreviewCandidates.Clear();

        ASCIIWorldObject[] allObjects = FindObjectsByType<ASCIIWorldObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (allObjects == null || allObjects.Length == 0)
            return;

        Vector3 center = GetRevertNearbyVirtualCenterPosition();
        float radius = Mathf.Max(0f, revertNearbyVirtualRadius);
        float radiusSqr = radius * radius;

        for (int i = 0; i < allObjects.Length; i++)
        {
            ASCIIWorldObject worldObject = allObjects[i];
            if (!IsEligibleForNearbyVirtualRevert(worldObject))
                continue;

            Vector3 objectPos = GetWorldObjectCenterPosition(worldObject);
            if ((objectPos - center).sqrMagnitude > radiusSqr)
                continue;

            revertNearbyVirtualPreviewCandidates.Add(worldObject);
        }
    }

    private bool IsEligibleForNearbyVirtualRevert(ASCIIWorldObject worldObject)
    {
        if (worldObject == null || registryManager == null)
            return false;

        // 关键修复 1：不允许隐藏对象参与 Q 键扫描
        if (!worldObject.gameObject.activeInHierarchy)
            return false;

        // 关键修复 2：没有任何可见 Renderer 时，也不参与高亮/扫描
        if (!HasAnyVisibleRenderer(worldObject))
            return false;

        if (worldObject.CurrentState != ASCIIWorldObject.RuntimeState.Virtual)
            return false;

        if (registryManager.IsPersistentVirtualObject(worldObject))
            return true;

        IReadOnlyList<ASCIIWorldObject> activeObjects = registryManager.ActiveObjects;
        for (int i = 0; i < activeObjects.Count; i++)
        {
            if (activeObjects[i] == worldObject)
                return true;
        }

        return false;
    }

    private Vector3 GetRevertNearbyVirtualCenterPosition()
    {
        Transform centerTransform = revertNearbyVirtualCenter != null ? revertNearbyVirtualCenter : transform;
        return centerTransform != null ? centerTransform.position : Vector3.zero;
    }

    private Vector3 GetWorldObjectCenterPosition(ASCIIWorldObject worldObject)
    {
        if (worldObject == null)
            return Vector3.zero;

        Collider[] colliders = worldObject.GetComponentsInChildren<Collider>(true);
        Bounds mergedBounds = default;
        bool hasBounds = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null || !col.enabled)
                continue;

            Bounds bounds = col.bounds;
            if (bounds.size.sqrMagnitude <= 0f)
                continue;

            if (!hasBounds)
            {
                mergedBounds = bounds;
                hasBounds = true;
            }
            else
            {
                mergedBounds.Encapsulate(bounds.min);
                mergedBounds.Encapsulate(bounds.max);
            }
        }

        if (hasBounds)
            return mergedBounds.center;

        return worldObject.transform.position;
    }

    private bool HasAnyVisibleRenderer(ASCIIWorldObject worldObject)
    {
        if (worldObject == null)
            return false;

        Renderer[] renderers = worldObject.CachedRenderers;
        if (renderers == null || renderers.Length == 0)
            return false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer rend = renderers[i];
            if (rend == null)
                continue;

            if (!rend.enabled)
                continue;

            if (!rend.gameObject.activeInHierarchy)
                continue;

            return true;
        }

        return false;
    }

    private void TryPickSolidToVirtual()
    {
        if (targetCamera == null || registryManager == null)
            return;

        Ray ray = targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, aimConvertRayDistance, aimConvertTargetLayers, QueryTriggerInteraction.Ignore))
            return;

        ASCIIWorldObject worldObject = hit.collider != null
            ? hit.collider.GetComponentInParent<ASCIIWorldObject>()
            : null;

        if (worldObject == null)
            return;

        if (worldObject.CurrentState != ASCIIWorldObject.RuntimeState.Solid)
            return;

        Bounds wakeBounds = GetAimConversionWakeBounds(worldObject, out bool hasWakeBounds);

        registryManager.RegisterAsVirtual(worldObject);

        Physics.SyncTransforms();

        if (hasWakeBounds)
            WakeSolidRigidbodiesAroundBounds(wakeBounds, worldObject);
    }

    private Bounds GetAimConversionWakeBounds(ASCIIWorldObject worldObject, out bool valid)
    {
        valid = false;

        if (worldObject == null)
            return default;

        Collider[] colliders = worldObject.GetComponentsInChildren<Collider>(true);
        Bounds merged = default;
        bool hasAny = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null || !col.enabled)
                continue;

            Bounds b = col.bounds;
            if (b.size.sqrMagnitude <= 0f)
                continue;

            if (!hasAny)
            {
                merged = b;
                hasAny = true;
            }
            else
            {
                merged.Encapsulate(b.min);
                merged.Encapsulate(b.max);
            }
        }

        valid = hasAny;
        return merged;
    }

    private void WakeSolidRigidbodiesAroundBounds(Bounds sourceBounds, ASCIIWorldObject ignoreObject)
    {
        if (registryManager == null)
            return;

        Vector3 extents = sourceBounds.extents;
        extents.x = Mathf.Max(extents.x + 0.02f, 0.02f);
        extents.y = Mathf.Max(extents.y + 0.02f, 0.02f);
        extents.z = Mathf.Max(extents.z + 0.02f, 0.02f);

        int count = Physics.OverlapBoxNonAlloc(
            sourceBounds.center,
            extents,
            aimWakeCandidateBuffer,
            Quaternion.identity,
            ~0,
            QueryTriggerInteraction.Ignore
        );

        HashSet<Rigidbody> wokenRigidbodies = new HashSet<Rigidbody>();

        for (int i = 0; i < count; i++)
        {
            Collider other = aimWakeCandidateBuffer[i];
            aimWakeCandidateBuffer[i] = null;

            if (other == null)
                continue;

            ASCIIWorldObject otherWorldObject = other.GetComponentInParent<ASCIIWorldObject>();
            if (otherWorldObject == null || otherWorldObject == ignoreObject)
                continue;

            if (ignoreObject != null &&
                other.transform != null &&
                other.transform.IsChildOf(ignoreObject.transform))
            {
                continue;
            }

            if (otherWorldObject.CurrentState != ASCIIWorldObject.RuntimeState.Solid)
                continue;

            Rigidbody[] rigidbodies = otherWorldObject.GetComponentsInChildren<Rigidbody>(true);
            for (int r = 0; r < rigidbodies.Length; r++)
            {
                Rigidbody rb = rigidbodies[r];
                if (rb == null || rb.isKinematic)
                    continue;

                if (wokenRigidbodies.Add(rb))
                    rb.WakeUp();
            }
        }
    }

    private void UpdateVirtualProjectionSwitchingByBounds()
    {
        if (registryManager == null)
            return;

        Rect viewportRect = GetViewportRect();
        List<ASCIIWorldObject> runtimeOriginObjects = registryManager.GetRuntimeOriginObjects();

        for (int i = 0; i < runtimeOriginObjects.Count; i++)
        {
            ASCIIWorldObject worldObject = runtimeOriginObjects[i];
            if (worldObject == null)
                continue;

            float insideRatio = CalculateObjectInsideRatio(worldObject, viewportRect);

            if (worldObject.CurrentState == ASCIIWorldObject.RuntimeState.Virtual)
            {
                if (insideRatio >= switchToProjectionRatio)
                {
                    registryManager.SetObjectState(worldObject, ASCIIWorldObject.RuntimeState.Projection);
                    RecordProjectionEnterSnapshot(worldObject);
                    ApplyProjectionInvalidLayerIfOverlapping(worldObject);
                }
            }
            else if (worldObject.CurrentState == ASCIIWorldObject.RuntimeState.Projection)
            {
                if (persistentOcclusionInvalidProjectionIds.Contains(worldObject.ObjectId))
                {
                    int invalidLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(projectionInvalidLayer);
                    if (invalidLayerIndex >= 0)
                        SetObjectLayerRecursively(worldObject, invalidLayerIndex);
                    continue;
                }

                if (IsProjectionObjectCurrentlyOnInvalidLayer(worldObject))
                    continue;

                if (insideRatio < switchBackRatio)
                {
                    ClearProjectionEnterSnapshot(worldObject);
                    registryManager.SetObjectState(worldObject, ASCIIWorldObject.RuntimeState.Virtual);
                }
            }
        }
    }

    private void UpdateCenterProjectionRaycast()
    {
        currentCenterProjectionObject = null;

        if (targetCamera == null || registryManager == null)
            return;

        int projectionLayerMask = registryManager.projectionLayer.value;
        int projectionDraggingLayerMask = projectionDraggingLayer.value;
        int projectionInvalidLayerMask = projectionInvalidLayer.value;

        int normalProjectionMask = projectionLayerMask | projectionDraggingLayerMask;
        int invalidProjectionMask = projectionInvalidLayerMask;

        int normalOccluderMask = projectionCenterRayOccluderMask.value;
        int invalidOccluderMask = projectionInvalidCenterRayOccluderMask.value;

        int combinedMask =
            normalProjectionMask |
            invalidProjectionMask |
            normalOccluderMask |
            invalidOccluderMask;

        if (combinedMask == 0)
            return;

        bool normalProjectionBlocked = false;
        bool invalidProjectionBlocked = false;

        Ray ray = targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            aimConvertRayDistance,
            combinedMask,
            QueryTriggerInteraction.Collide
        );

        if (hits == null || hits.Length == 0)
            return;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null)
                continue;

            int hitLayer = col.gameObject.layer;

            bool hitNormalProjection = IsLayerIncludedInMask(hitLayer, normalProjectionMask);
            bool hitInvalidProjection = IsLayerIncludedInMask(hitLayer, invalidProjectionMask);

            if (hitNormalProjection || hitInvalidProjection)
            {
                ASCIIWorldObject obj = col.GetComponentInParent<ASCIIWorldObject>();
                if (obj == null || obj.CurrentState != ASCIIWorldObject.RuntimeState.Projection)
                    continue;

                if (hitInvalidProjection)
                {
                    if (!invalidProjectionBlocked)
                    {
                        currentCenterProjectionObject = obj;
                        return;
                    }
                }
                else
                {
                    if (!normalProjectionBlocked)
                    {
                        currentCenterProjectionObject = obj;
                        return;
                    }
                }
            }

            if (IsLayerIncludedInMask(hitLayer, normalOccluderMask))
                normalProjectionBlocked = true;

            if (IsLayerIncludedInMask(hitLayer, invalidOccluderMask))
                invalidProjectionBlocked = true;
        }
    }

    private static bool IsLayerIncludedInMask(int layer, int mask)
    {
        return (mask & (1 << layer)) != 0;
    }

    private bool IsProjectionObjectCurrentlyOnInvalidLayer(ASCIIWorldObject obj)
    {
        if (obj == null)
            return false;

        int invalidLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(projectionInvalidLayer);
        return invalidLayerIndex >= 0 && obj.gameObject.layer == invalidLayerIndex;
    }

    private ProjectionFrameData BuildInitialConnectivityFrameExcludingInvalidProjectionPixels(ProjectionFrameData sourceFrame)
    {
        if (sourceFrame == null)
            return sourceFrame;

        HashSet<int> invalidProjectionIds = CollectCurrentInvalidProjectionObjectIds();
        if (invalidProjectionIds.Count == 0)
            return sourceFrame;

        ProjectionFrameData filteredFrame = new ProjectionFrameData
        {
            width = sourceFrame.width,
            height = sourceFrame.height,
            analysisRoi = sourceFrame.analysisRoi,
            virtualIdPixels = sourceFrame.virtualIdPixels,
            solidIdPixels = sourceFrame.solidIdPixels,
            projectionMaskPixels = sourceFrame.projectionMaskPixels != null
                ? (byte[])sourceFrame.projectionMaskPixels.Clone()
                : null,
            projectionIdPixels = sourceFrame.projectionIdPixels != null
                ? (int[])sourceFrame.projectionIdPixels.Clone()
                : null,
        };

        if (filteredFrame.projectionMaskPixels == null || filteredFrame.projectionIdPixels == null)
            return sourceFrame;

        int pixelCount = Mathf.Min(filteredFrame.projectionMaskPixels.Length, filteredFrame.projectionIdPixels.Length);
        for (int i = 0; i < pixelCount; i++)
        {
            int objectId = filteredFrame.projectionIdPixels[i];
            if (objectId == 0)
                continue;

            if (!invalidProjectionIds.Contains(objectId))
                continue;

            filteredFrame.projectionMaskPixels[i] = 0;
            filteredFrame.projectionIdPixels[i] = 0;
        }

        return filteredFrame;
    }

    private HashSet<int> CollectCurrentInvalidProjectionObjectIds()
    {
        HashSet<int> invalidIds = new HashSet<int>();
        if (registryManager == null)
            return invalidIds;

        IReadOnlyList<ASCIIWorldObject> activeObjects = registryManager.ActiveObjects;
        for (int i = 0; i < activeObjects.Count; i++)
        {
            ASCIIWorldObject obj = activeObjects[i];
            if (obj == null)
                continue;

            if (obj.CurrentState != ASCIIWorldObject.RuntimeState.Projection)
                continue;

            if (!IsProjectionObjectCurrentlyOnInvalidLayer(obj))
                continue;

            invalidIds.Add(obj.ObjectId);
        }

        return invalidIds;
    }

    private void ApplyProjectionInvalidLayerIfOverlapping(ASCIIWorldObject projectionObj)
    {
        if (projectionObj == null || registryManager == null)
            return;

        if (projectionObj.CurrentState != ASCIIWorldObject.RuntimeState.Projection)
            return;

        int normalLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(registryManager.projectionLayer);
        int invalidLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(projectionInvalidLayer);

        bool overlapsInvalidMask = TryCollectObjectInvalidOverlaps(projectionObj, projectionInvalidOverlapMask, null);
        int targetLayerIndex = overlapsInvalidMask ? invalidLayerIndex : normalLayerIndex;

        persistentOcclusionInvalidProjectionIds.Remove(projectionObj.ObjectId);

        if (targetLayerIndex >= 0)
            SetObjectLayerRecursively(projectionObj, targetLayerIndex);
    }

    private bool StartConnectedSelectionOnce()
    {
        if (currentCenterProjectionObject == null)
            return false;

        lockedSeedProjectionObject = currentCenterProjectionObject;
        connectedSelectionLocked = true;
        dragActionStarted = false;

        selectedProjectionObjects.Clear();
        coveredSolidObjects.Clear();
        connectedProjectionObjects.Clear();

        if (IsProjectionObjectCurrentlyOnInvalidLayer(lockedSeedProjectionObject))
        {
            selectedProjectionObjects.Add(lockedSeedProjectionObject);
            connectedProjectionObjects.Add(lockedSeedProjectionObject);

            waitingForConnectedAnalysis = false;
            waitingStartFrameVersion = -1;

            BeginDragConnectedGroup();
            dragActionStarted = true;
            return true;
        }

        if (projectionAnalysisBridge == null || projectionAnalysisBridge.IsRequestPending)
            return false;

        waitingForConnectedAnalysis = true;
        waitingStartFrameVersion = projectionAnalysisBridge.CompletedFrameVersion;
        projectionAnalysisBridge.RequestReadback();

        return true;
    }

    private void TryResolveConnectedSelectionAndBeginDrag()
    {
        if (projectionAnalysisBridge == null || registryManager == null)
            return;

        if (projectionAnalysisBridge.IsRequestPending)
            return;

        if (projectionAnalysisBridge.CompletedFrameVersion <= waitingStartFrameVersion)
            return;

        waitingForConnectedAnalysis = false;

        if (lockedSeedProjectionObject == null)
            return;

        if (!projectionAnalysisBridge.TryGetLatestFrame(out ProjectionFrameData frame))
            return;

        ProjectionFrameData connectivityFrame = BuildInitialConnectivityFrameExcludingInvalidProjectionPixels(frame);
        Vector2Int centerPixel = GetAnalysisCenterPixel(connectivityFrame.width, connectivityFrame.height);
        bool ok = analyzer.BuildConnectedProjectionGroupFromObjectId(
            connectivityFrame,
            lockedSeedProjectionObject.ObjectId,
            centerPixel
        );

        if (!ok)
            return;

        HashSet<int> selectedProjectionIds = new HashSet<int>(analyzer.ConnectedIdList);

        selectedProjectionObjects.Clear();
        coveredSolidObjects.Clear();
        connectedProjectionObjects.Clear();

        foreach (int id in analyzer.ConnectedIdList)
        {
            if (!registryManager.TryGetObjectById(id, out ASCIIWorldObject obj) || obj == null)
                continue;

            if (obj.CurrentState == ASCIIWorldObject.RuntimeState.Projection)
            {
                selectedProjectionObjects.Add(obj);

                if (!connectedProjectionObjects.Contains(obj))
                    connectedProjectionObjects.Add(obj);
            }
        }

        if (moveCoveredSolidsAlongWithDraggedProjection)
        {
            List<int> coveredSolidIds = analyzer.FindFullyCoveredSolidIds(
                frame,
                selectedProjectionIds,
                solidFullyCoveredThreshold
            );

            for (int i = 0; i < coveredSolidIds.Count; i++)
            {
                int solidId = coveredSolidIds[i];

                if (!registryManager.TryGetObjectById(solidId, out ASCIIWorldObject solidObj) || solidObj == null)
                    continue;

                if (solidObj.CurrentState != ASCIIWorldObject.RuntimeState.Solid)
                    continue;

                coveredSolidObjects.Add(solidObj);

                if (!connectedProjectionObjects.Contains(solidObj))
                    connectedProjectionObjects.Add(solidObj);
            }
        }

        if (connectedProjectionObjects.Count == 0)
            return;

        BeginDragConnectedGroup();
        dragActionStarted = true;
    }

    private void BeginDragConnectedGroup()
    {
        EndDragConnectedGroup(false);

        if ((selectedProjectionObjects == null || selectedProjectionObjects.Count == 0) &&
            (coveredSolidObjects == null || coveredSolidObjects.Count == 0))
            return;

        if (projectionDragPivot == null || registryManager == null)
            return;

        attachedDraggedObjects.Clear();
        draggedColliderSet.Clear();
        dragStartSnapshotByObject.Clear();

        for (int i = 0; i < selectedProjectionObjects.Count; i++)
        {
            ASCIIWorldObject obj = selectedProjectionObjects[i];
            if (obj == null)
                continue;

            persistentOcclusionInvalidProjectionIds.Remove(obj.ObjectId);
            pendingReleaseOcclusionCheckObjects.Remove(obj);

            CaptureDragStartSnapshot(obj);
            AttachObjectToPivot(obj);
            attachedDraggedObjects.Add(obj);

            registryManager.SetDraggedPhysicsDisabled(obj, draggedColliderSet);
        }

        for (int i = 0; i < coveredSolidObjects.Count; i++)
        {
            ASCIIWorldObject solidObj = coveredSolidObjects[i];
            if (solidObj == null)
                continue;

            CaptureDragStartSnapshot(solidObj);
            AttachObjectToPivot(solidObj);
            attachedDraggedObjects.Add(solidObj);

            registryManager.SetDraggedPhysicsDisabled(solidObj, draggedColliderSet);
        }


        isDraggingConnectedGroup = attachedDraggedObjects.Count > 0;
        isCurrentDragInvalid = false;
        isCurrentDragSolidInvalid = false;

        if (isDraggingConnectedGroup)
            UpdateDraggedProjectionInvalidState();
    }

    private void EndDragConnectedGroup(bool revertToDragStart)
    {
        if (!isDraggingConnectedGroup && attachedDraggedObjects.Count == 0)
            return;

        List<ASCIIWorldObject> dragged = new List<ASCIIWorldObject>(attachedDraggedObjects);

        for (int i = 0; i < dragged.Count; i++)
            DetachObjectFromOriginalParent(dragged[i]);

        if (revertToDragStart)
        {
            for (int i = 0; i < dragged.Count; i++)
                RestoreDragStartPose(dragged[i]);
        }

        for (int i = 0; i < dragged.Count; i++)
            FinalizeDetachedDraggedObject(dragged[i]);

        DestroyCoveredSolidVirtualGhosts();

        attachedDraggedObjects.Clear();
        draggedColliderSet.Clear();
        dragStartSnapshotByObject.Clear();
        currentInvalidOverlapColliders.Clear();
        currentInvalidProjectionObjects.Clear();
        pendingDestroyCoveredSolidObjects.Clear();
        isDraggingConnectedGroup = false;
        isCurrentDragInvalid = false;
    }

    private void StopCurrentDragButStayInProjectionMode()
    {
        bool anySolidInvalid = CollectCoveredSolidReleaseInvalidOverlaps();
        CaptureReleaseOcclusionHoldForSelectedProjections();

        if (anySolidInvalid)
            RestoreCoveredSolidObjectsToDragStartWhileAttached();

        EndDragConnectedGroup(false);

        if (anySolidInvalid)
        {
            Physics.SyncTransforms();
            MarkCoveredSolidObjectsForDestroyIfRestoreStillOverlaps();
        }

        DestroyPendingCoveredSolidObjectsAfterRestore();
        ApplyReleasedProjectionInvalidLayers();
        RequestReleaseOcclusionSnapshotForSelectedProjections();

        waitingForConnectedAnalysis = false;
        connectedSelectionLocked = false;
        lockedSeedProjectionObject = null;
        waitingStartFrameVersion = -1;
        dragActionStarted = false;

        skipBoundsSwitchThisFrame = true;
    }


    private void UpdateDraggedProjectionInvalidState()
    {
        if (!isDraggingConnectedGroup)
            return;

        bool anyProjectionInvalid = false;
        bool anySolidInvalid = false;
        bool keepInvalidByCenterOccluder = false;
        currentInvalidOverlapColliders.Clear();
        currentInvalidProjectionObjects.Clear();

        for (int i = 0; i < selectedProjectionObjects.Count; i++)
        {
            ASCIIWorldObject projectionObj = selectedProjectionObjects[i];
            if (projectionObj == null)
                continue;

            bool overlapsInvalid = TryCollectObjectInvalidOverlaps(
                projectionObj,
                projectionInvalidOverlapMask,
                currentInvalidOverlapColliders);

            if (overlapsInvalid)
            {
                anyProjectionInvalid = true;
                if (!currentInvalidProjectionObjects.Contains(projectionObj))
                    currentInvalidProjectionObjects.Add(projectionObj);
                continue;
            }

            if (IsProjectionObjectCurrentlyOnInvalidLayer(projectionObj) &&
                IsDraggedInvalidProjectionBlockedFromScreenCenter(projectionObj))
            {
                anyProjectionInvalid = true;
                keepInvalidByCenterOccluder = true;
                if (!currentInvalidProjectionObjects.Contains(projectionObj))
                    currentInvalidProjectionObjects.Add(projectionObj);
            }
        }

        if (moveCoveredSolidsAlongWithDraggedProjection)
        {
            for (int i = 0; i < coveredSolidObjects.Count; i++)
            {
                ASCIIWorldObject solidObj = coveredSolidObjects[i];
                if (solidObj == null || !attachedDraggedObjects.Contains(solidObj))
                    continue;

                if (TryCollectObjectInvalidOverlaps(solidObj, solidInvalidOverlapMask, currentInvalidOverlapColliders))
                    anySolidInvalid = true;
            }
        }

        bool anyInvalid = anyProjectionInvalid || anySolidInvalid;
        isCurrentDragInvalid = anyInvalid;
        isCurrentDragSolidInvalid = anySolidInvalid;
        ApplyCurrentDraggedProjectionLayers(anyProjectionInvalid, anySolidInvalid);

        if (keepInvalidByCenterOccluder)
        {
            int invalidLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(projectionInvalidLayer);
            if (invalidLayerIndex >= 0)
            {
                for (int i = 0; i < selectedProjectionObjects.Count; i++)
                {
                    ASCIIWorldObject projectionObj = selectedProjectionObjects[i];
                    if (projectionObj == null || projectionObj.CurrentState != ASCIIWorldObject.RuntimeState.Projection)
                        continue;

                    if (currentInvalidProjectionObjects.Contains(projectionObj))
                        SetObjectLayerRecursively(projectionObj, invalidLayerIndex);
                }
            }
        }
    }

    private void CaptureReleaseOcclusionHoldForSelectedProjections()
    {
        releaseOcclusionHoldProjectionIds.Clear();

        for (int i = 0; i < selectedProjectionObjects.Count; i++)
        {
            ASCIIWorldObject projectionObj = selectedProjectionObjects[i];
            if (projectionObj == null || projectionObj.CurrentState != ASCIIWorldObject.RuntimeState.Projection)
                continue;

            if (!IsProjectionObjectCurrentlyOnInvalidLayer(projectionObj))
                continue;

            if (TryCollectObjectInvalidOverlaps(projectionObj, projectionInvalidOverlapMask, null))
                continue;

            if (!IsDraggedInvalidProjectionBlockedFromScreenCenter(projectionObj))
                continue;

            releaseOcclusionHoldProjectionIds.Add(projectionObj.ObjectId);
        }
    }

    private void ApplyCurrentDraggedProjectionLayers(bool anyProjectionInvalid, bool anySolidInvalid)
    {
        int targetLayerIndex;

        bool shouldDragCoveredSolids =
            moveCoveredSolidsAlongWithDraggedProjection &&
            coveredSolidObjects != null &&
            coveredSolidObjects.Count > 0;

        if (anyProjectionInvalid)
        {
            targetLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(projectionInvalidLayer);
        }
        else if (anySolidInvalid && shouldDragCoveredSolids)
        {
            targetLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(projectionInvalidWithSolidLayer);
        }
        else if (shouldDragCoveredSolids)
        {
            targetLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(projectionDraggingLayer);
        }
        else
        {
            targetLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(registryManager.projectionLayer);
        }

        if (anySolidInvalid && shouldDragCoveredSolids)
            CreateCoveredSolidVirtualGhosts();
        else
            DestroyCoveredSolidVirtualGhosts();

        if (targetLayerIndex >= 0)
        {
            for (int i = 0; i < selectedProjectionObjects.Count; i++)
            {
                ASCIIWorldObject projectionObj = selectedProjectionObjects[i];
                if (projectionObj == null || projectionObj.CurrentState != ASCIIWorldObject.RuntimeState.Projection)
                    continue;

                SetObjectLayerRecursively(projectionObj, targetLayerIndex);
            }
        }

        SyncCoveredSolidDragState(shouldDragCoveredSolids);
    }

    private void ApplyReleasedProjectionInvalidLayers()
    {
        if (registryManager == null)
            return;

        int normalLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(registryManager.projectionLayer);
        int invalidLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(projectionInvalidLayer);

        currentInvalidOverlapColliders.Clear();
        currentInvalidProjectionObjects.Clear();

        for (int i = 0; i < selectedProjectionObjects.Count; i++)
        {
            ASCIIWorldObject projectionObj = selectedProjectionObjects[i];
            if (projectionObj == null)
                continue;

            // bool overlapsInvalidMask = TryCollectObjectInvalidOverlaps(
            //     projectionObj,
            //     projectionInvalidOverlapMask,
            //     currentInvalidOverlapColliders);
            bool overlapsInvalidMask = IsProjectionInvalidOnRelease(projectionObj, currentInvalidOverlapColliders);

            if (!overlapsInvalidMask && enableProjectionReleaseShrinkCoreCheck)
            {
                LayerMask shrinkMask = projectionReleaseShrinkCoreMask.value != 0
                    ? projectionReleaseShrinkCoreMask
                    : projectionInvalidOverlapMask;

                overlapsInvalidMask = TryCollectObjectInvalidOverlapsByShrinkCore(
                    projectionObj,
                    shrinkMask,
                    currentInvalidOverlapColliders);
            }

            if (overlapsInvalidMask)
            {
                if (!currentInvalidProjectionObjects.Contains(projectionObj))
                    currentInvalidProjectionObjects.Add(projectionObj);

                if (invalidLayerIndex >= 0)
                    SetObjectLayerRecursively(projectionObj, invalidLayerIndex);
            }
            else
            {
                if (normalLayerIndex >= 0)
                    SetObjectLayerRecursively(projectionObj, normalLayerIndex);
            }
        }
    }


    private bool CollectCoveredSolidReleaseInvalidOverlaps()
    {
        bool anySolidInvalid = false;

        currentInvalidOverlapColliders.Clear();
        currentInvalidProjectionObjects.Clear();

        for (int i = 0; i < selectedProjectionObjects.Count; i++)
        {
            ASCIIWorldObject projectionObj = selectedProjectionObjects[i];
            if (projectionObj == null || projectionObj.CurrentState != ASCIIWorldObject.RuntimeState.Projection)
                continue;

            if (TryCollectObjectInvalidOverlaps(projectionObj, projectionInvalidOverlapMask, currentInvalidOverlapColliders))
            {
                if (!currentInvalidProjectionObjects.Contains(projectionObj))
                    currentInvalidProjectionObjects.Add(projectionObj);
            }
        }

        if (moveCoveredSolidsAlongWithDraggedProjection)
        {
            for (int i = 0; i < coveredSolidObjects.Count; i++)
            {
                ASCIIWorldObject solidObj = coveredSolidObjects[i];
                if (solidObj == null || !attachedDraggedObjects.Contains(solidObj))
                    continue;

                if (TryCollectObjectInvalidOverlaps(solidObj, solidInvalidOverlapMask, currentInvalidOverlapColliders))
                    anySolidInvalid = true;
            }
        }

        return anySolidInvalid;
    }


    private void MarkCoveredSolidObjectsForDestroyIfRestoreStillOverlaps()
    {
        pendingDestroyCoveredSolidObjects.Clear();

        if (!destroyCoveredSolidIfRestoreOverlaps)
            return;

        int overlapMask = destroyCoveredSolidRestoreOverlapMask.value != 0
            ? destroyCoveredSolidRestoreOverlapMask.value
            : solidInvalidOverlapMask.value;

        for (int i = 0; i < coveredSolidObjects.Count; i++)
        {
            ASCIIWorldObject solidObj = coveredSolidObjects[i];
            if (solidObj == null)
                continue;

            if (!DoesWorldObjectOverlapAnyCollider(solidObj, overlapMask, solidRestoreDestroyPenetrationThreshold, solidRestoreDestroyPenetrationThresholdRatio))
                continue;

            if (!pendingDestroyCoveredSolidObjects.Contains(solidObj))
                pendingDestroyCoveredSolidObjects.Add(solidObj);
        }
    }

    private void DestroyPendingCoveredSolidObjectsAfterRestore()
    {
        if (pendingDestroyCoveredSolidObjects.Count == 0)
            return;

        for (int i = 0; i < pendingDestroyCoveredSolidObjects.Count; i++)
        {
            ASCIIWorldObject solidObj = pendingDestroyCoveredSolidObjects[i];
            if (solidObj == null)
                continue;

            if (registryManager != null)
                registryManager.DestroyWorldObject(solidObj);
            else
                Destroy(solidObj.gameObject);
        }

        pendingDestroyCoveredSolidObjects.Clear();
        coveredSolidObjects.RemoveAll(item => item == null);
    }

    private bool DoesWorldObjectOverlapAnyCollider(
        ASCIIWorldObject worldObject,
        int overlapMask,
        float fixedThreshold,
        float ratioThreshold)
    {
        if (worldObject == null || overlapMask == 0)
            return false;

        QueryTriggerInteraction triggerInteraction = includeTriggerCollidersInInvalidCheck
            ? QueryTriggerInteraction.Collide
            : QueryTriggerInteraction.Ignore;

        Collider[] ownColliders = worldObject.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < ownColliders.Length; i++)
        {
            Collider own = ownColliders[i];
            if (own == null || !own.enabled)
                continue;

            Bounds bounds = own.bounds;
            if (bounds.size.sqrMagnitude <= 0f)
                continue;

            int candidateCount = Physics.OverlapBoxNonAlloc(
                bounds.center,
                bounds.extents,
                invalidOverlapCandidateBuffer,
                own.transform.rotation,
                overlapMask,
                triggerInteraction
            );

            for (int c = 0; c < candidateCount; c++)
            {
                Collider other = invalidOverlapCandidateBuffer[c];
                invalidOverlapCandidateBuffer[c] = null;

                if (other == null || other == own)
                    continue;

                if (other.transform != null && other.transform.IsChildOf(worldObject.transform))
                    continue;

                if (!TryEvaluateColliderOverlapWithThreshold(own, other, fixedThreshold, ratioThreshold, out _, out _))
                    continue;

                return true;
            }
        }

        return false;
    }

    private void RestoreCoveredSolidObjectsToDragStartWhileAttached()
    {
        for (int i = 0; i < coveredSolidObjects.Count; i++)
        {
            ASCIIWorldObject solidObj = coveredSolidObjects[i];
            if (solidObj == null || !attachedDraggedObjects.Contains(solidObj))
                continue;

            RestoreDragStartPose(solidObj);
        }
    }

    private bool TryCollectObjectInvalidOverlaps(
        ASCIIWorldObject worldObj,
        LayerMask overlapLayerMask,
        List<Collider> debugCollector)
    {
        if (worldObj == null)
            return false;

        int overlapMask = overlapLayerMask.value;
        if (overlapMask == 0)
            return false;

        QueryTriggerInteraction triggerInteraction = includeTriggerCollidersInInvalidCheck
            ? QueryTriggerInteraction.Collide
            : QueryTriggerInteraction.Ignore;

        bool useProjectionTolerance = worldObj.CurrentState == ASCIIWorldObject.RuntimeState.Projection;
        float fixedThreshold = useProjectionTolerance ? projectionInvalidPenetrationThreshold : solidInvalidPenetrationThreshold;
        float ratioThreshold = useProjectionTolerance ? projectionInvalidPenetrationThresholdRatio : solidInvalidPenetrationThresholdRatio;

        Collider[] ownColliders = worldObj.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < ownColliders.Length; i++)
        {
            Collider own = ownColliders[i];
            if (own == null || !own.enabled)
                continue;

            Bounds bounds = own.bounds;
            if (bounds.size.sqrMagnitude <= 0f)
                continue;

            int candidateCount = Physics.OverlapBoxNonAlloc(
                bounds.center,
                bounds.extents,
                invalidOverlapCandidateBuffer,
                own.transform.rotation,
                overlapMask,
                triggerInteraction
            );

            for (int c = 0; c < candidateCount; c++)
            {
                Collider other = invalidOverlapCandidateBuffer[c];
                invalidOverlapCandidateBuffer[c] = null;

                if (other == null || other == own)
                    continue;

                ASCIIWorldObject otherObj = other.GetComponentInParent<ASCIIWorldObject>();
                if (otherObj != null && attachedDraggedObjects.Contains(otherObj))
                    continue;

                bool overlaps = useProjectionTolerance
                    ? TryEvaluateColliderOverlap(own, other, worldObj, out _, out _)
                    : TryEvaluateColliderOverlapWithThreshold(own, other, fixedThreshold, ratioThreshold, out _, out _);

                if (!overlaps)
                    continue;

                if (debugCollector != null && !debugCollector.Contains(other))
                    debugCollector.Add(other);

                return true;
            }
        }

        return false;
    }

    private bool TryCollectObjectInvalidOverlapsByShrinkCore(
        ASCIIWorldObject worldObj,
        LayerMask overlapLayerMask,
        List<Collider> debugCollector)
    {
        if (worldObj == null)
            return false;

        int overlapMask = overlapLayerMask.value;
        if (overlapMask == 0)
            return false;

        QueryTriggerInteraction triggerInteraction = includeTriggerCollidersInInvalidCheck
            ? QueryTriggerInteraction.Collide
            : QueryTriggerInteraction.Ignore;

        float shrink = Mathf.Max(0f, projectionInvalidPenetrationThreshold);

        Collider[] ownColliders = worldObj.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < ownColliders.Length; i++)
        {
            Collider own = ownColliders[i];
            if (own == null || !own.enabled)
                continue;

            if (!TryBuildShrinkCoreOverlapBox(own, shrink, out Vector3 center, out Vector3 halfExtents, out Quaternion orientation))
                continue;

            int candidateCount = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                invalidOverlapCandidateBuffer,
                orientation,
                overlapMask,
                triggerInteraction
            );

            for (int c = 0; c < candidateCount; c++)
            {
                Collider other = invalidOverlapCandidateBuffer[c];
                invalidOverlapCandidateBuffer[c] = null;

                if (other == null || other == own)
                    continue;

                if (other.transform != null && other.transform.IsChildOf(worldObj.transform))
                    continue;

                ASCIIWorldObject otherObj = other.GetComponentInParent<ASCIIWorldObject>();
                if (otherObj != null && attachedDraggedObjects.Contains(otherObj))
                    continue;

                if (debugCollector != null && !debugCollector.Contains(other))
                    debugCollector.Add(other);

                return true;
            }
        }

        return false;
    }

    private bool TryBuildShrinkCoreOverlapBox(
        Collider collider,
        float shrinkPerFace,
        out Vector3 worldCenter,
        out Vector3 halfExtents,
        out Quaternion orientation)
    {
        worldCenter = Vector3.zero;
        halfExtents = Vector3.zero;
        orientation = Quaternion.identity;

        if (collider == null)
            return false;

        if (collider is BoxCollider box)
            return TryBuildShrinkCoreBoxForBoxCollider(box, shrinkPerFace, out worldCenter, out halfExtents, out orientation);

        if (collider is SphereCollider sphere)
            return TryBuildShrinkCoreBoxForSphereCollider(sphere, shrinkPerFace, out worldCenter, out halfExtents, out orientation);

        if (collider is CapsuleCollider capsule)
            return TryBuildShrinkCoreBoxForCapsuleCollider(capsule, shrinkPerFace, out worldCenter, out halfExtents, out orientation);

        Bounds bounds = collider.bounds;
        if (bounds.size.sqrMagnitude <= 0f)
            return false;

        Vector3 shrunkSize = bounds.size - Vector3.one * (2f * shrinkPerFace);
        shrunkSize.x = Mathf.Max(0f, shrunkSize.x);
        shrunkSize.y = Mathf.Max(0f, shrunkSize.y);
        shrunkSize.z = Mathf.Max(0f, shrunkSize.z);

        if (shrunkSize.x <= 0f || shrunkSize.y <= 0f || shrunkSize.z <= 0f)
            return false;

        worldCenter = bounds.center;
        halfExtents = shrunkSize * 0.5f;
        orientation = collider.transform.rotation;
        return true;
    }

    private bool TryBuildShrinkCoreBoxForBoxCollider(
        BoxCollider box,
        float shrinkPerFace,
        out Vector3 worldCenter,
        out Vector3 halfExtents,
        out Quaternion orientation)
    {
        worldCenter = Vector3.zero;
        halfExtents = Vector3.zero;
        orientation = Quaternion.identity;

        if (box == null)
            return false;

        Transform t = box.transform;
        Vector3 absScale = AbsVector3(t.lossyScale);

        Vector3 scaledCenter = Vector3.Scale(box.center, t.lossyScale);
        Vector3 scaledSize = Vector3.Scale(box.size, absScale);

        scaledSize.x = Mathf.Max(0f, scaledSize.x - 2f * shrinkPerFace);
        scaledSize.y = Mathf.Max(0f, scaledSize.y - 2f * shrinkPerFace);
        scaledSize.z = Mathf.Max(0f, scaledSize.z - 2f * shrinkPerFace);

        if (scaledSize.x <= 0f || scaledSize.y <= 0f || scaledSize.z <= 0f)
            return false;

        worldCenter = t.TransformPoint(box.center);
        halfExtents = scaledSize * 0.5f;
        orientation = t.rotation;
        return true;
    }

    private bool TryBuildShrinkCoreBoxForSphereCollider(
        SphereCollider sphere,
        float shrinkPerFace,
        out Vector3 worldCenter,
        out Vector3 halfExtents,
        out Quaternion orientation)
    {
        worldCenter = Vector3.zero;
        halfExtents = Vector3.zero;
        orientation = Quaternion.identity;

        if (sphere == null)
            return false;

        Transform t = sphere.transform;
        float scale = MaxAbsComponent(t.lossyScale);
        float radius = sphere.radius * scale - shrinkPerFace;

        if (radius <= 0f)
            return false;

        worldCenter = t.TransformPoint(sphere.center);
        halfExtents = Vector3.one * radius;
        orientation = t.rotation;
        return true;
    }

    private bool TryBuildShrinkCoreBoxForCapsuleCollider(
        CapsuleCollider capsule,
        float shrinkPerFace,
        out Vector3 worldCenter,
        out Vector3 halfExtents,
        out Quaternion orientation)
    {
        worldCenter = Vector3.zero;
        halfExtents = Vector3.zero;
        orientation = Quaternion.identity;

        if (capsule == null)
            return false;

        Transform t = capsule.transform;
        Vector3 absScale = AbsVector3(t.lossyScale);

        float radius = capsule.radius;
        float height = capsule.height;

        worldCenter = t.TransformPoint(capsule.center);
        orientation = t.rotation;

        switch (capsule.direction)
        {
            case 0:
            {
                float scaledRadius = radius * Mathf.Max(absScale.y, absScale.z) - shrinkPerFace;
                float scaledHalfHeight = height * absScale.x * 0.5f - shrinkPerFace;
                if (scaledRadius <= 0f || scaledHalfHeight <= 0f)
                    return false;

                halfExtents = new Vector3(scaledHalfHeight, scaledRadius, scaledRadius);
                return true;
            }
            case 1:
            {
                float scaledRadius = radius * Mathf.Max(absScale.x, absScale.z) - shrinkPerFace;
                float scaledHalfHeight = height * absScale.y * 0.5f - shrinkPerFace;
                if (scaledRadius <= 0f || scaledHalfHeight <= 0f)
                    return false;

                halfExtents = new Vector3(scaledRadius, scaledHalfHeight, scaledRadius);
                return true;
            }
            default:
            {
                float scaledRadius = radius * Mathf.Max(absScale.x, absScale.y) - shrinkPerFace;
                float scaledHalfHeight = height * absScale.z * 0.5f - shrinkPerFace;
                if (scaledRadius <= 0f || scaledHalfHeight <= 0f)
                    return false;

                halfExtents = new Vector3(scaledRadius, scaledRadius, scaledHalfHeight);
                return true;
            }
        }
    }

    private static Vector3 AbsVector3(Vector3 v)
    {
        return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }

    private static float MaxAbsComponent(Vector3 v)
    {
        return Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }

    private bool TryEvaluateColliderOverlap(
        Collider own,
        Collider other,
        ASCIIWorldObject ownerWorldObj,
        out float penetrationDistance,
        out float requiredThreshold)
    {
        penetrationDistance = 0f;
        requiredThreshold = 0f;

        if (own == null || other == null)
            return false;

        if (other.transform != null && ownerWorldObj != null && other.transform.IsChildOf(ownerWorldObj.transform))
            return false;

        bool overlaps = Physics.ComputePenetration(
            own, own.transform.position, own.transform.rotation,
            other, other.transform.position, other.transform.rotation,
            out _, out float distance
        );

        if (!overlaps)
            return false;

        penetrationDistance = distance;
        requiredThreshold = GetPenetrationThresholdForCollider(own, projectionInvalidPenetrationThreshold, projectionInvalidPenetrationThresholdRatio);
        if (distance >= requiredThreshold)
            return true;

        return !ShouldIgnoreProjectionOverlapBecauseStillNearEnterPose(ownerWorldObj);
    }

    private bool TryEvaluateColliderOverlapWithThreshold(
        Collider own,
        Collider other,
        float fixedThreshold,
        float ratioThreshold,
        out float penetrationDistance,
        out float requiredThreshold)
    {
        penetrationDistance = 0f;
        requiredThreshold = 0f;

        if (own == null || other == null)
            return false;

        bool overlaps = Physics.ComputePenetration(
            own, own.transform.position, own.transform.rotation,
            other, other.transform.position, other.transform.rotation,
            out _, out float distance
        );

        if (!overlaps)
            return false;

        penetrationDistance = distance;
        requiredThreshold = GetPenetrationThresholdForCollider(own, fixedThreshold, ratioThreshold);
        return distance >= requiredThreshold;
    }

    private float GetProjectionPenetrationThresholdForCollider(Collider own)
    {
        return GetPenetrationThresholdForCollider(own, projectionInvalidPenetrationThreshold, projectionInvalidPenetrationThresholdRatio);
    }

    private float GetPenetrationThresholdForCollider(Collider own, float fixedThreshold, float ratioThreshold)
    {
        float clampedFixedThreshold = Mathf.Max(0f, fixedThreshold);
        if (!useRelativePenetrationThreshold)
            return clampedFixedThreshold;

        float clampedRatio = Mathf.Max(0f, ratioThreshold);
        if (clampedRatio <= 0f)
            return clampedFixedThreshold;

        Bounds bounds = own != null ? own.bounds : default;
        Vector3 size = bounds.size;
        float minAxis = Mathf.Min(size.x, Mathf.Min(size.y, size.z));

        if (minAxis <= 0f)
            return clampedFixedThreshold;

        float scaledThreshold = minAxis * clampedRatio;
        return Mathf.Max(clampedFixedThreshold, scaledThreshold);
    }

    private bool ShouldIgnoreProjectionOverlapBecauseStillNearEnterPose(ASCIIWorldObject projectionObj)
    {
        if (!enableProjectionInitialPoseTolerance || projectionObj == null)
            return false;

        if (!projectionEnterSnapshotByObject.TryGetValue(projectionObj, out ProjectionEnterSnapshot snapshot))
            return false;

        float movedDistance = Vector3.Distance(projectionObj.transform.position, snapshot.worldPosition);
        float rotatedAngle = Quaternion.Angle(projectionObj.transform.rotation, snapshot.worldRotation);

        return movedDistance < Mathf.Max(0f, projectionInitialPoseMoveTolerance) &&
               rotatedAngle < Mathf.Max(0f, projectionInitialPoseRotateTolerance);
    }

    private void RecordProjectionEnterSnapshot(ASCIIWorldObject projectionObj)
    {
        if (projectionObj == null)
            return;

        ProjectionEnterSnapshot snapshot = new ProjectionEnterSnapshot
        {
            worldPosition = projectionObj.transform.position,
            worldRotation = projectionObj.transform.rotation
        };

        projectionEnterSnapshotByObject[projectionObj] = snapshot;
    }

    private void ClearProjectionEnterSnapshot(ASCIIWorldObject projectionObj)
    {
        if (projectionObj == null)
            return;

        projectionEnterSnapshotByObject.Remove(projectionObj);
    }

    private void CaptureDragStartSnapshot(ASCIIWorldObject obj)
    {
        if (obj == null || dragStartSnapshotByObject.ContainsKey(obj))
            return;

        DragTransformSnapshot snapshot = new DragTransformSnapshot
        {
            worldPosition = obj.transform.position,
            worldRotation = obj.transform.rotation
        };
        dragStartSnapshotByObject[obj] = snapshot;
    }

    private void FinalizeDetachedDraggedObject(ASCIIWorldObject obj)
    {
        if (obj == null)
            return;

        registryManager.SetObjectState(obj, obj.CurrentState);

        if (obj.CurrentState == ASCIIWorldObject.RuntimeState.Projection)
        {
            int forcedLayerIndex = -1;

            if (releaseOcclusionHoldProjectionIds.Contains(obj.ObjectId) ||
                persistentOcclusionInvalidProjectionIds.Contains(obj.ObjectId))
            {
                forcedLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(projectionInvalidLayer);
            }

            if (forcedLayerIndex >= 0)
                SetObjectLayerRecursively(obj, forcedLayerIndex);
        }

        if (IsProjectionMode && freezeSolidRigidbodiesInProjectionMode)
            ReapplyProjectionModeFreezeToSolidObject(obj);

        obj.ApplyObjectIdToRenderers();
    }

    private void SyncCoveredSolidDragState(bool shouldDragCoveredSolids)
    {
        for (int i = 0; i < coveredSolidObjects.Count; i++)
        {
            ASCIIWorldObject solidObj = coveredSolidObjects[i];
            if (solidObj == null)
                continue;

            bool isAttached = attachedDraggedObjects.Contains(solidObj);

            if (shouldDragCoveredSolids)
            {
                if (isAttached)
                    continue;

                CaptureDragStartSnapshot(solidObj);
                AttachObjectToPivot(solidObj);
                attachedDraggedObjects.Add(solidObj);
                registryManager.SetDraggedPhysicsDisabled(solidObj, draggedColliderSet);
            }
            else
            {
                if (!isAttached)
                    continue;

                DetachObjectFromOriginalParent(solidObj);
                RestoreDragStartPose(solidObj);
                attachedDraggedObjects.Remove(solidObj);
                FinalizeDetachedDraggedObject(solidObj);
            }
        }
    }

    private void SetObjectLayerRecursively(ASCIIWorldObject obj, int layerIndex)
    {
        if (obj == null || layerIndex < 0)
            return;

        Transform[] children = obj.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
            children[i].gameObject.layer = layerIndex;
    }

    private void AttachObjectToPivot(ASCIIWorldObject obj)
    {
        if (obj == null || projectionDragPivot == null)
            return;

        Transform tr = obj.transform;

        if (!originalParentByObject.ContainsKey(obj))
            originalParentByObject.Add(obj, tr.parent);

        if (!originalSiblingIndexByObject.ContainsKey(obj))
            originalSiblingIndexByObject.Add(obj, tr.GetSiblingIndex());

        tr.SetParent(projectionDragPivot, true);
    }

    private void DetachObjectFromOriginalParent(ASCIIWorldObject obj)
    {
        if (obj == null)
            return;

        if (!originalParentByObject.TryGetValue(obj, out Transform originalParent))
            return;

        Transform tr = obj.transform;
        tr.SetParent(originalParent, true);

        if (originalSiblingIndexByObject.TryGetValue(obj, out int siblingIndex) &&
            originalParent != null &&
            siblingIndex >= 0 &&
            siblingIndex < originalParent.childCount)
        {
            tr.SetSiblingIndex(siblingIndex);
        }

        originalParentByObject.Remove(obj);
        originalSiblingIndexByObject.Remove(obj);
    }

    private void RestoreDragStartPose(ASCIIWorldObject obj)
    {
        if (obj == null)
            return;

        if (!dragStartSnapshotByObject.TryGetValue(obj, out DragTransformSnapshot snapshot))
            return;

        obj.transform.position = snapshot.worldPosition;
        obj.transform.rotation = snapshot.worldRotation;
    }

    private float CalculateObjectInsideRatio(ASCIIWorldObject worldObject, Rect viewportRect)
    {
        if (worldObject == null || targetCamera == null)
            return 0f;

        Renderer[] renderers = worldObject.CachedRenderers;
        if (renderers == null || renderers.Length == 0)
            return 0f;

        Bounds mergedBounds = GetMergedBounds(renderers, out bool valid);
        if (!valid)
            return 0f;

        Vector3[] samplePoints = GetSamplePointsFromBounds(mergedBounds, sampleGridX, sampleGridY);

        int validCount = 0;
        int insideCount = 0;

        for (int i = 0; i < samplePoints.Length; i++)
        {
            Vector3 vp = targetCamera.WorldToViewportPoint(samplePoints[i]);
            if (vp.z <= 0f)
                continue;

            validCount++;

            if (viewportRect.Contains(new Vector2(vp.x, vp.y)))
                insideCount++;
        }

        if (validCount == 0)
            return 0f;

        return (float)insideCount / validCount;
    }

    private Bounds GetMergedBounds(Renderer[] renderers, out bool valid)
    {
        valid = false;
        Bounds bounds = default;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer rend = renderers[i];
            if (rend == null || !rend.enabled)
                continue;

            if (!valid)
            {
                bounds = rend.bounds;
                valid = true;
            }
            else
            {
                bounds.Encapsulate(rend.bounds);
            }
        }

        return bounds;
    }

    private Vector3[] GetSamplePointsFromBounds(Bounds bounds, int gridX, int gridY)
    {
        Vector3 center = bounds.center;
        Vector3 ext = bounds.extents;

        List<Vector3> points = new List<Vector3>(gridX * gridY * 3 + 1);
        points.Add(center);

        for (int y = 0; y < gridY; y++)
        {
            float ty = gridY == 1 ? 0.5f : (float)y / (gridY - 1);
            float py = Mathf.Lerp(-ext.y, ext.y, ty);

            for (int x = 0; x < gridX; x++)
            {
                float tx = gridX == 1 ? 0.5f : (float)x / (gridX - 1);
                float px = Mathf.Lerp(-ext.x, ext.x, tx);

                points.Add(center + new Vector3(px, py, 0f));
                points.Add(center + new Vector3(px, py, ext.z));
                points.Add(center + new Vector3(px, py, -ext.z));
            }
        }

        return points.ToArray();
    }

    private Rect GetViewportRect()
    {
        float xMin = regionCenterX - regionWidth * 0.5f;
        float yMin = regionCenterY - regionHeight * 0.5f;
        float xMax = regionCenterX + regionWidth * 0.5f;
        float yMax = regionCenterY + regionHeight * 0.5f;

        xMin = Mathf.Clamp01(xMin);
        yMin = Mathf.Clamp01(yMin);
        xMax = Mathf.Clamp01(xMax);
        yMax = Mathf.Clamp01(yMax);

        return new Rect(
            xMin,
            yMin,
            Mathf.Max(0f, xMax - xMin),
            Mathf.Max(0f, yMax - yMin)
        );
    }

    private Vector2Int GetAnalysisCenterPixel(int width, int height)
    {
        int x = Mathf.RoundToInt(regionCenterX * width);
        int y = Mathf.RoundToInt(regionCenterY * height);

        x = Mathf.Clamp(x, 0, Mathf.Max(0, width - 1));
        y = Mathf.Clamp(y, 0, Mathf.Max(0, height - 1));

        return new Vector2Int(x, y);
    }

    private void CreateCoveredSolidVirtualGhosts()
    {
        DestroyCoveredSolidVirtualGhosts();

        if (!moveCoveredSolidsAlongWithDraggedProjection || coveredSolidObjects == null || coveredSolidObjects.Count == 0)
            return;

        int virtualLayerIndex = registryManager != null
            ? LayerMaskSingleLayerUtility.ToLayerIndex(registryManager.virtualLayer)
            : -1;

        for (int i = 0; i < coveredSolidObjects.Count; i++)
        {
            ASCIIWorldObject solidObj = coveredSolidObjects[i];
            if (solidObj == null)
                continue;

            GameObject ghostRoot = BuildVirtualGhostForSolidObject(solidObj, virtualLayerIndex);
            if (ghostRoot != null)
                coveredSolidVirtualGhostRoots[solidObj] = ghostRoot;
        }
    }

    private void DestroyCoveredSolidVirtualGhosts()
    {
        if (coveredSolidVirtualGhostRoots.Count == 0)
            return;

        List<GameObject> ghostRoots = new List<GameObject>(coveredSolidVirtualGhostRoots.Values);
        coveredSolidVirtualGhostRoots.Clear();

        for (int i = 0; i < ghostRoots.Count; i++)
        {
            GameObject ghostRoot = ghostRoots[i];
            if (ghostRoot == null)
                continue;

            Destroy(ghostRoot);
        }
    }

    private GameObject BuildVirtualGhostForSolidObject(ASCIIWorldObject solidObj, int virtualLayerIndex)
    {
        if (solidObj == null)
            return null;

        GameObject ghostRoot = new GameObject(solidObj.name + "_VirtualGhost");
        Transform sourceRoot = solidObj.transform;
        Transform ghostRootTransform = ghostRoot.transform;

        Vector3 ghostWorldPosition = sourceRoot.position;
        Quaternion sourceRootWorldRotation = sourceRoot.rotation;
        Vector3 sourceRootWorldScale = sourceRoot.lossyScale;

        if (dragStartSnapshotByObject.TryGetValue(solidObj, out DragTransformSnapshot snapshot))
        {
            ghostWorldPosition = snapshot.worldPosition;
            sourceRootWorldRotation = snapshot.worldRotation;
        }

        ghostRootTransform.SetParent(null, false);
        ghostRootTransform.position = ghostWorldPosition;
        ghostRootTransform.rotation = Quaternion.identity;
        ghostRootTransform.localScale = Vector3.one;

        if (virtualLayerIndex >= 0)
            ghostRoot.layer = virtualLayerIndex;

        bool createdAnyMesh = false;
        CopyMeshHierarchyRecursive(
            sourceRoot,
            ghostRootTransform,
            virtualLayerIndex,
            ref createdAnyMesh,
            true,
            sourceRootWorldRotation,
            sourceRootWorldScale);

        if (!createdAnyMesh)
        {
            Object.Destroy(ghostRoot);
            return null;
        }

        return ghostRoot;
    }

    private void CopyMeshHierarchyRecursive(
        Transform source,
        Transform ghostParent,
        int virtualLayerIndex,
        ref bool createdAnyMesh,
        bool isRoot,
        Quaternion rootWorldRotation,
        Vector3 rootWorldScale)
    {
        GameObject ghostNode = new GameObject(source.name);
        Transform ghostNodeTransform = ghostNode.transform;
        ghostNodeTransform.SetParent(ghostParent, false);

        if (isRoot)
        {
            ghostNodeTransform.localPosition = Vector3.zero;
            ghostNodeTransform.localRotation = rootWorldRotation;
            ghostNodeTransform.localScale = rootWorldScale;
        }
        else
        {
            ghostNodeTransform.localPosition = source.localPosition;
            ghostNodeTransform.localRotation = source.localRotation;
            ghostNodeTransform.localScale = source.localScale;
        }

        if (virtualLayerIndex >= 0)
            ghostNode.layer = virtualLayerIndex;

        MeshFilter sourceMeshFilter = source.GetComponent<MeshFilter>();
        MeshRenderer sourceMeshRenderer = source.GetComponent<MeshRenderer>();
        SkinnedMeshRenderer sourceSkinnedMeshRenderer = source.GetComponent<SkinnedMeshRenderer>();

        if (sourceMeshFilter != null && sourceMeshRenderer != null)
        {
            MeshFilter ghostMeshFilter = ghostNode.AddComponent<MeshFilter>();
            ghostMeshFilter.sharedMesh = sourceMeshFilter.sharedMesh;

            MeshRenderer ghostMeshRenderer = ghostNode.AddComponent<MeshRenderer>();
            ghostMeshRenderer.sharedMaterials = sourceMeshRenderer.sharedMaterials;
            ghostMeshRenderer.shadowCastingMode = sourceMeshRenderer.shadowCastingMode;
            ghostMeshRenderer.receiveShadows = sourceMeshRenderer.receiveShadows;
            ghostMeshRenderer.lightProbeUsage = sourceMeshRenderer.lightProbeUsage;
            ghostMeshRenderer.reflectionProbeUsage = sourceMeshRenderer.reflectionProbeUsage;
            ghostMeshRenderer.motionVectorGenerationMode = sourceMeshRenderer.motionVectorGenerationMode;
            ghostMeshRenderer.allowOcclusionWhenDynamic = sourceMeshRenderer.allowOcclusionWhenDynamic;
            ghostMeshRenderer.probeAnchor = sourceMeshRenderer.probeAnchor;
            createdAnyMesh = true;
        }
        else if (sourceSkinnedMeshRenderer != null)
        {
            Mesh bakedMesh = new Mesh();
            bakedMesh.name = sourceSkinnedMeshRenderer.name + "_GhostBaked";
            sourceSkinnedMeshRenderer.BakeMesh(bakedMesh);

            MeshFilter ghostMeshFilter = ghostNode.AddComponent<MeshFilter>();
            ghostMeshFilter.sharedMesh = bakedMesh;

            MeshRenderer ghostMeshRenderer = ghostNode.AddComponent<MeshRenderer>();
            ghostMeshRenderer.sharedMaterials = sourceSkinnedMeshRenderer.sharedMaterials;
            ghostMeshRenderer.shadowCastingMode = sourceSkinnedMeshRenderer.shadowCastingMode;
            ghostMeshRenderer.receiveShadows = sourceSkinnedMeshRenderer.receiveShadows;
            ghostMeshRenderer.lightProbeUsage = sourceSkinnedMeshRenderer.lightProbeUsage;
            ghostMeshRenderer.reflectionProbeUsage = sourceSkinnedMeshRenderer.reflectionProbeUsage;
            ghostMeshRenderer.motionVectorGenerationMode = sourceSkinnedMeshRenderer.motionVectorGenerationMode;
            ghostMeshRenderer.allowOcclusionWhenDynamic = sourceSkinnedMeshRenderer.allowOcclusionWhenDynamic;
            ghostMeshRenderer.probeAnchor = sourceSkinnedMeshRenderer.probeAnchor;
            createdAnyMesh = true;
        }

        for (int i = 0; i < source.childCount; i++)
        {
            CopyMeshHierarchyRecursive(
                source.GetChild(i),
                ghostNodeTransform,
                virtualLayerIndex,
                ref createdAnyMesh,
                false,
                rootWorldRotation,
                rootWorldScale);
        }
    }



    private bool IsDraggedInvalidProjectionBlockedFromScreenCenter(ASCIIWorldObject worldObject)
    {
        if (worldObject == null || targetCamera == null)
            return false;

        Renderer[] renderers = worldObject.CachedRenderers;
        if (renderers == null || renderers.Length == 0)
            return false;

        Bounds mergedBounds = GetMergedBounds(renderers, out bool validBounds);
        if (!validBounds)
            return false;

        Vector3 targetPoint = mergedBounds.center;
        Vector3 origin = targetCamera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, 0f));
        Vector3 toTarget = targetPoint - origin;
        float distance = toTarget.magnitude;
        if (distance <= 0.0001f)
            return false;

        int occluderMask = projectionCenterRayOccluderMask.value;
        if (occluderMask == 0)
            return false;

        Vector3 direction = toTarget / distance;
        QueryTriggerInteraction triggerInteraction = includeTriggerCollidersInInvalidCheck
            ? QueryTriggerInteraction.Collide
            : QueryTriggerInteraction.Ignore;

        RaycastHit[] hits = Physics.RaycastAll(origin, direction, distance, occluderMask, triggerInteraction);
        if (hits == null || hits.Length == 0)
            return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null)
                continue;

            ASCIIWorldObject hitObj = col.GetComponentInParent<ASCIIWorldObject>();
            if (hitObj == worldObject)
                continue;

            return true;
        }

        return false;
    }

    private bool IsObjectCenterBlockedByProjectionInvalidMask(ASCIIWorldObject worldObject)
    {
        if (worldObject == null || targetCamera == null)
            return false;

        Renderer[] renderers = worldObject.CachedRenderers;
        if (renderers == null || renderers.Length == 0)
            return false;

        Bounds mergedBounds = GetMergedBounds(renderers, out bool validBounds);
        if (!validBounds)
            return false;

        Vector3 center = mergedBounds.center;
        Vector3 origin = targetCamera.transform.position;
        Vector3 toCenter = center - origin;
        float distance = toCenter.magnitude;
        if (distance <= 0.0001f)
            return false;

        int blockMask = projectionInvalidOverlapMask.value;
        if (blockMask == 0)
            return false;

        Vector3 dir = toCenter / distance;
        QueryTriggerInteraction triggerInteraction = includeTriggerCollidersInInvalidCheck
            ? QueryTriggerInteraction.Collide
            : QueryTriggerInteraction.Ignore;

        RaycastHit[] hits = Physics.RaycastAll(origin, dir, distance, blockMask, triggerInteraction);
        if (hits == null || hits.Length == 0)
            return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null)
                continue;

            ASCIIWorldObject hitObj = col.GetComponentInParent<ASCIIWorldObject>();
            if (hitObj == worldObject)
                continue;

            return true;
        }

        return false;
    }

    private bool IsProjectionIllegalForPersistence(ASCIIWorldObject obj)
    {
        if (obj == null)
            return false;

        return IsProjectionObjectCurrentlyOnInvalidLayer(obj) ||
               persistentOcclusionInvalidProjectionIds.Contains(obj.ObjectId);
    }

    private bool IsProjectionInvalidOnRelease(ASCIIWorldObject projectionObj, List<Collider> debugCollector)
    {
        if (projectionObj == null || projectionObj.CurrentState != ASCIIWorldObject.RuntimeState.Projection)
            return false;

        bool overlapsInvalidMask = TryCollectObjectInvalidOverlaps(
            projectionObj,
            projectionInvalidOverlapMask,
            debugCollector);

        if (!overlapsInvalidMask && enableProjectionReleaseShrinkCoreCheck)
        {
            LayerMask shrinkMask = projectionReleaseShrinkCoreMask.value != 0
                ? projectionReleaseShrinkCoreMask
                : projectionInvalidOverlapMask;

            overlapsInvalidMask = TryCollectObjectInvalidOverlapsByShrinkCore(
                projectionObj,
                shrinkMask,
                debugCollector);
        }

        return overlapsInvalidMask;
    }

    private void RequestReleaseOcclusionSnapshotForSelectedProjections()
    {
        pendingReleaseOcclusionCheckObjects.Clear();

        for (int i = 0; i < selectedProjectionObjects.Count; i++)
        {
            ASCIIWorldObject projectionObj = selectedProjectionObjects[i];
            if (projectionObj == null || projectionObj.CurrentState != ASCIIWorldObject.RuntimeState.Projection)
                continue;

            // 只对当前已不是碰撞非法的 Projection 做遮挡非法复核。
            // if (TryCollectObjectInvalidOverlaps(projectionObj, projectionInvalidOverlapMask, null))
            //     continue;
            if (IsProjectionInvalidOnRelease(projectionObj, null))
                continue;

            pendingReleaseOcclusionCheckObjects.Add(projectionObj);
        }

        if (pendingReleaseOcclusionCheckObjects.Count == 0)
        {
            waitingForReleaseOcclusionSnapshot = false;
            pendingReleaseOcclusionFrameVersion = -1;
            return;
        }

        if (projectionAnalysisBridge == null)
        {
            pendingReleaseOcclusionCheckObjects.Clear();
            waitingForReleaseOcclusionSnapshot = false;
            pendingReleaseOcclusionFrameVersion = -1;
            return;
        }

        waitingForReleaseOcclusionSnapshot = true;
        pendingReleaseOcclusionFrameVersion = projectionAnalysisBridge.CompletedFrameVersion;

        if (!projectionAnalysisBridge.IsRequestPending)
            projectionAnalysisBridge.RequestReadback();
    }

    private void TryResolveReleaseOcclusionSnapshot()
    {
        if (!waitingForReleaseOcclusionSnapshot)
            return;

        if (projectionAnalysisBridge == null)
        {
            waitingForReleaseOcclusionSnapshot = false;
            pendingReleaseOcclusionFrameVersion = -1;
            pendingReleaseOcclusionCheckObjects.Clear();
            releaseOcclusionHoldProjectionIds.Clear();
            return;
        }

        if (projectionAnalysisBridge.IsRequestPending)
            return;

        if (projectionAnalysisBridge.CompletedFrameVersion <= pendingReleaseOcclusionFrameVersion)
        {
            projectionAnalysisBridge.RequestReadback();
            return;
        }

        waitingForReleaseOcclusionSnapshot = false;

        if (!projectionAnalysisBridge.TryGetLatestFrame(out ProjectionFrameData frame) || frame == null)
        {
            pendingReleaseOcclusionFrameVersion = -1;
            pendingReleaseOcclusionCheckObjects.Clear();
            releaseOcclusionHoldProjectionIds.Clear();
            return;
        }

        int normalLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(registryManager.projectionLayer);
        int invalidLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(projectionInvalidLayer);

        currentInvalidProjectionObjects.Clear();

        for (int i = 0; i < pendingReleaseOcclusionCheckObjects.Count; i++)
        {
            ASCIIWorldObject projectionObj = pendingReleaseOcclusionCheckObjects[i];
            if (projectionObj == null || projectionObj.CurrentState != ASCIIWorldObject.RuntimeState.Projection)
                continue;

            // if (TryCollectObjectInvalidOverlaps(projectionObj, projectionInvalidOverlapMask, null))
            //     continue;
            if (IsProjectionInvalidOnRelease(projectionObj, null))
            {
                persistentOcclusionInvalidProjectionIds.Remove(projectionObj.ObjectId);
                releaseOcclusionHoldProjectionIds.Remove(projectionObj.ObjectId);

                if (invalidLayerIndex >= 0)
                    SetObjectLayerRecursively(projectionObj, invalidLayerIndex);

                if (!currentInvalidProjectionObjects.Contains(projectionObj))
                    currentInvalidProjectionObjects.Add(projectionObj);

                continue;
            }

            bool occlusionInvalid = IsProjectionOcclusionInvalidInFrame(frame, projectionObj.ObjectId);

            if (occlusionInvalid)
            {
                persistentOcclusionInvalidProjectionIds.Add(projectionObj.ObjectId);
                releaseOcclusionHoldProjectionIds.Remove(projectionObj.ObjectId);
                if (invalidLayerIndex >= 0)
                    SetObjectLayerRecursively(projectionObj, invalidLayerIndex);

                if (!currentInvalidProjectionObjects.Contains(projectionObj))
                    currentInvalidProjectionObjects.Add(projectionObj);
            }
            else
            {
                persistentOcclusionInvalidProjectionIds.Remove(projectionObj.ObjectId);
                releaseOcclusionHoldProjectionIds.Remove(projectionObj.ObjectId);
                if (normalLayerIndex >= 0)
                    SetObjectLayerRecursively(projectionObj, normalLayerIndex);
            }
        }

        pendingReleaseOcclusionFrameVersion = -1;
        pendingReleaseOcclusionCheckObjects.Clear();
        releaseOcclusionHoldProjectionIds.Clear();
    }

    private bool IsProjectionOcclusionInvalidInFrame(ProjectionFrameData frame, int objectId)
    {
        if (frame == null ||
            frame.projectionIdPixels == null ||
            frame.projectionMaskPixels == null ||
            objectId <= 0)
        {
            return false;
        }

        bool hasAnyIdPixel = false;
        bool hasAnyVisibleMaskPixel = false;

        int pixelCount = Mathf.Min(frame.projectionIdPixels.Length, frame.projectionMaskPixels.Length);
        for (int i = 0; i < pixelCount; i++)
        {
            if (frame.projectionIdPixels[i] != objectId)
                continue;

            hasAnyIdPixel = true;
            if (frame.projectionMaskPixels[i] != 0)
            {
                hasAnyVisibleMaskPixel = true;
                break;
            }
        }

        return hasAnyIdPixel && !hasAnyVisibleMaskPixel;
    }

    private void RefreshPersistentOcclusionInvalidProjections()
    {
        if (!IsProjectionMode || registryManager == null || persistentOcclusionInvalidProjectionIds.Count == 0)
            return;

        int normalLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(registryManager.projectionLayer);
        int invalidLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(projectionInvalidLayer);
        if (normalLayerIndex < 0 || invalidLayerIndex < 0)
            return;

        List<int> idsToRemove = null;
        Rect fullViewportRect = new Rect(0f, 0f, 1f, 1f);

        foreach (int objectId in persistentOcclusionInvalidProjectionIds)
        {
            if (!registryManager.TryGetObjectById(objectId, out ASCIIWorldObject obj) || obj == null)
            {
                idsToRemove ??= new List<int>();
                idsToRemove.Add(objectId);
                continue;
            }

            if (obj.CurrentState != ASCIIWorldObject.RuntimeState.Projection)
            {
                idsToRemove ??= new List<int>();
                idsToRemove.Add(objectId);
                continue;
            }

            // 持久遮挡非法对象在恢复前始终保持 invalid layer，避免被其他逻辑洗回合法层。
            SetObjectLayerRecursively(obj, invalidLayerIndex);

            // 仍与非法遮挡层发生重合时，不恢复。
            if (TryCollectObjectInvalidOverlaps(obj, projectionInvalidOverlapMask, null))
                continue;

            // 必须重新进入相机视野，同时对象中心不再被 projectionInvalidOverlapMask 挡住，才恢复为合法投影。
            float visibleRatio = CalculateObjectInsideRatio(obj, fullViewportRect);
            if (visibleRatio <= 0f)
                continue;

            if (IsObjectCenterBlockedByProjectionInvalidMask(obj))
                continue;

            SetObjectLayerRecursively(obj, normalLayerIndex);
            idsToRemove ??= new List<int>();
            idsToRemove.Add(objectId);
        }

        if (idsToRemove == null)
            return;

        for (int i = 0; i < idsToRemove.Count; i++)
            persistentOcclusionInvalidProjectionIds.Remove(idsToRemove[i]);
    }

    private void RestoreRuntimeProjectionsToVirtual()
    {
        if (registryManager == null)
            return;

        List<ASCIIWorldObject> runtimeOriginObjects = registryManager.GetRuntimeOriginObjects();

        for (int i = 0; i < runtimeOriginObjects.Count; i++)
        {
            ASCIIWorldObject obj = runtimeOriginObjects[i];
            if (obj == null)
                continue;

            if (obj.CurrentState != ASCIIWorldObject.RuntimeState.Projection)
                continue;

            if (IsProjectionIllegalForPersistence(obj))
                continue;

            ClearProjectionEnterSnapshot(obj);
            registryManager.SetObjectState(obj, ASCIIWorldObject.RuntimeState.Virtual);
        }
    }

    private void EnterProjectionMode()
    {
        if (isProjectionMode)
            return;

        isProjectionMode = true;
        isAimMode = false;
        ApplyProjectionModePauseState(true);
    }

    private void ExitProjectionMode()
    {
        EndDragConnectedGroup(false);
        RestoreRuntimeProjectionsToVirtual();

        isProjectionMode = false;
        waitingForConnectedAnalysis = false;
        connectedSelectionLocked = false;
        lockedSeedProjectionObject = null;
        waitingStartFrameVersion = -1;
        waitingForReleaseOcclusionSnapshot = false;
        pendingReleaseOcclusionFrameVersion = -1;
        pendingReleaseOcclusionCheckObjects.Clear();
        releaseOcclusionHoldProjectionIds.Clear();
        dragActionStarted = false;

        projectionEnterSnapshotByObject.Clear();
        ApplyProjectionModePauseState(false);
        ClearProjectionSelection();
    }

    private void ClearProjectionSelection()
    {
        currentCenterProjectionObject = null;
        connectedProjectionObjects.Clear();
        selectedProjectionObjects.Clear();
        coveredSolidObjects.Clear();
    }


    private void ApplyProjectionModePauseState(bool paused)
    {
        if (paused)
        {
            if (projectionModePauseApplied)
                return;

            if (freezeSolidRigidbodiesInProjectionMode)
                FreezeCurrentSolidRigidbodies();

            if (notifyProjectionModePauseReceivers)
                NotifyProjectionModePauseReceivers(true);

            projectionModePauseApplied = true;
            return;
        }

        if (!projectionModePauseApplied)
            return;

        RestorePausedSolidRigidbodies();

        if (notifyProjectionModePauseReceivers)
            NotifyProjectionModePauseReceivers(false);

        projectionModePauseApplied = false;
    }

    private void FreezeCurrentSolidRigidbodies()
    {
        pausedSolidRigidbodySnapshots.Clear();

        if (registryManager == null)
            return;

        IReadOnlyList<ASCIIWorldObject> activeObjects = registryManager.ActiveObjects;
        if (activeObjects == null)
            return;

        for (int i = 0; i < activeObjects.Count; i++)
        {
            ASCIIWorldObject worldObject = activeObjects[i];
            if (worldObject == null)
                continue;

            if (worldObject.CurrentState != ASCIIWorldObject.RuntimeState.Solid)
                continue;

            Rigidbody[] rigidbodies = worldObject.GetComponentsInChildren<Rigidbody>(true);
            for (int r = 0; r < rigidbodies.Length; r++)
            {
                Rigidbody rb = rigidbodies[r];
                if (rb == null || pausedSolidRigidbodySnapshots.ContainsKey(rb))
                    continue;

                pausedSolidRigidbodySnapshots.Add(rb, new RigidbodyPauseSnapshot
                {
                    isKinematic = rb.isKinematic,
                    useGravity = rb.useGravity,
                    velocity = rb.velocity,
                    angularVelocity = rb.angularVelocity,
                });

                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.useGravity = false;
                rb.isKinematic = true;
            }
        }
    }

    private void RestorePausedSolidRigidbodies()
    {
        foreach (KeyValuePair<Rigidbody, RigidbodyPauseSnapshot> pair in pausedSolidRigidbodySnapshots)
        {
            Rigidbody rb = pair.Key;
            if (rb == null)
                continue;

            RigidbodyPauseSnapshot snapshot = pair.Value;
            rb.isKinematic = snapshot.isKinematic;
            rb.useGravity = snapshot.useGravity;
            rb.velocity = snapshot.velocity;
            rb.angularVelocity = snapshot.angularVelocity;
        }

        pausedSolidRigidbodySnapshots.Clear();
    }

    private void ReapplyProjectionModeFreezeToSolidObject(ASCIIWorldObject worldObject)
    {
        if (worldObject == null)
            return;

        if (worldObject.CurrentState != ASCIIWorldObject.RuntimeState.Solid)
            return;

        Rigidbody[] rigidbodies = worldObject.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody rb = rigidbodies[i];
            if (rb == null)
                continue;

            if (!pausedSolidRigidbodySnapshots.ContainsKey(rb))
            {
                pausedSolidRigidbodySnapshots.Add(rb, new RigidbodyPauseSnapshot
                {
                    isKinematic = rb.isKinematic,
                    useGravity = rb.useGravity,
                    velocity = rb.velocity,
                    angularVelocity = rb.angularVelocity,
                });
            }

            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = false;
            rb.isKinematic = true;
        }
    }


    private void NotifyProjectionModePauseReceivers(bool paused)
    {
        FindObjectsInactive inactiveMode = includeInactivePauseReceivers
            ? FindObjectsInactive.Include
            : FindObjectsInactive.Exclude;

        MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(inactiveMode, FindObjectsSortMode.None);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour is IProjectionModePauseReceiver receiver)
                receiver.SetProjectionModePaused(paused);
        }
    }

    private void OnDisable()
    {
        CancelRevertNearbyVirtualPreview();
        DestroyCoveredSolidVirtualGhosts();
        ApplyProjectionModePauseState(false);
    }

    private void OnDestroy()
    {
        CancelRevertNearbyVirtualPreview();
        DestroyCoveredSolidVirtualGhosts();
        ApplyProjectionModePauseState(false);
    }

    [ContextMenu("Cleanup Nulls")]
    public void CleanupNulls()
    {
        List<ASCIIWorldObject> attached = new List<ASCIIWorldObject>(attachedDraggedObjects);
        for (int i = 0; i < attached.Count; i++)
        {
            if (attached[i] == null)
                attachedDraggedObjects.Remove(attached[i]);
        }

        for (int i = connectedProjectionObjects.Count - 1; i >= 0; i--)
        {
            if (connectedProjectionObjects[i] == null)
                connectedProjectionObjects.RemoveAt(i);
        }

        for (int i = selectedProjectionObjects.Count - 1; i >= 0; i--)
        {
            if (selectedProjectionObjects[i] == null)
                selectedProjectionObjects.RemoveAt(i);
        }

        for (int i = coveredSolidObjects.Count - 1; i >= 0; i--)
        {
            if (coveredSolidObjects[i] == null)
                coveredSolidObjects.RemoveAt(i);
        }

        for (int i = revertNearbyVirtualPreviewCandidates.Count - 1; i >= 0; i--)
        {
            if (revertNearbyVirtualPreviewCandidates[i] == null)
                revertNearbyVirtualPreviewCandidates.RemoveAt(i);
        }

        registryManager.CleanupNulls();
    }
}

public interface IProjectionModePauseReceiver
{
    void SetProjectionModePaused(bool paused);
}
