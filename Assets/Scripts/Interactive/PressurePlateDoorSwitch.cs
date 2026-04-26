using UnityEngine;

[DisallowMultipleComponent]
public class PressurePlateDoorSwitch : MonoBehaviour
{
    public enum MechanismType
    {
        /// <summary>
        /// 玩家在激活范围内时：
        /// targetLayers 在板上 = 开门
        /// 不在板上 = 关门
        /// </summary>
        PresenceOpensDoor = 0,

        /// <summary>
        /// 玩家在激活范围内时：
        /// 使用 presenceCustomTargetLayers 检测目标；
        /// 触发时开门还是关门，由 presenceCustomTargetOpensDoor 决定。
        /// </summary>
        PresenceCustomTarget = 1
    }

    public enum DoorMoveSpace
    {
        World = 0,
        Local = 1
    }

    [Header("机关类型")]
    [SerializeField] private MechanismType mechanismType = MechanismType.PresenceOpensDoor;

    [Header("触发器")]
    [Tooltip("小 Trigger：检测目标是否压在机关上。")]
    [SerializeField] private Collider plateTrigger;

    [Tooltip("大 Trigger：检测玩家是否进入机关激活范围。")]
    [SerializeField] private Collider playerActivationTrigger;

    [Header("玩家检测")]
    [Tooltip("只有该 Layer 的物体进入大 Trigger 时，机关才会工作。")]
    [SerializeField] private LayerMask playerLayer;

    [Header("PresenceOpensDoor 目标检测")]
    [Tooltip("PresenceOpensDoor 模式下使用这组 Layer。")]
    [SerializeField] private LayerMask targetLayers;

    [Header("PresenceCustomTarget 目标检测")]
    [Tooltip("PresenceCustomTarget 模式下使用这组 Layer，例如 Player 和 Enemy。")]
    [SerializeField] private LayerMask presenceCustomTargetLayers;

    [Tooltip("PresenceCustomTarget 模式下：true=触发时开门，false=触发时关门。")]
    [SerializeField] private bool presenceCustomTargetOpensDoor = true;

    [Header("门移动")]
    [Tooltip("要移动的门。")]
    [SerializeField] private Transform doorTransform;

    [Tooltip("门移动方向使用世界坐标还是门自身本地坐标。")]
    [SerializeField] private DoorMoveSpace doorMoveSpace = DoorMoveSpace.Local;

    [Tooltip("开门移动方向。建议填单位方向，例如 (0,1,0) 或 (1,0,0)。")]
    [SerializeField] private Vector3 openDirection = Vector3.up;

    [Tooltip("开门时沿方向移动的距离。")]
    [Min(0f)]
    [SerializeField] private float openDistance = 2f;

    [Tooltip("门移动速度。")]
    [Min(0.01f)]
    [SerializeField] private float doorMoveSpeed = 4f;

    [Tooltip("是否在启用时记录当前门位置为关门初始位置。")]
    [SerializeField] private bool captureClosedPositionOnEnable = false;

    [Header("地板与门外观")]
    [Tooltip("地板 Renderer。")]
    [SerializeField] private Renderer plateRenderer;

    [Tooltip("门 Renderer。")]
    [SerializeField] private Renderer doorRenderer;

    [Tooltip("材质颜色属性名。URP Lit 一般为 _BaseColor。")]
    [SerializeField] private string colorPropertyName = "_BaseColor";

    [Header("PresenceOpensDoor 默认颜色")]
    [ColorUsage(false, true)]
    [SerializeField] private Color presenceOpensDoorDefaultPlateColor = Color.red;

    [ColorUsage(false, true)]
    [SerializeField] private Color presenceOpensDoorDefaultDoorColor = Color.red;

    [Header("PresenceCustomTarget 默认颜色")]
    [ColorUsage(false, true)]
    [SerializeField] private Color presenceCustomTargetDefaultPlateColor = Color.blue;

    [ColorUsage(false, true)]
    [SerializeField] private Color presenceCustomTargetDefaultDoorColor = Color.blue;

    [Header("共用颜色")]
    [Tooltip("玩家在激活范围内且压板被触发时，地板和门共用此颜色。")]
    [ColorUsage(false, true)]
    [SerializeField] private Color triggeredColor = Color.green;

    [Tooltip("玩家离开大 Trigger 时，地板使用此共用未激活颜色；门颜色不受影响。")]
    [ColorUsage(false, true)]
    [SerializeField] private Color inactivePlateColor = Color.gray;

    [Header("刷新设置")]
    [Tooltip("状态刷新间隔。0 表示每帧。推荐 0.05。")]
    [Min(0f)]
    [SerializeField] private float refreshInterval = 0.05f;

    [Header("检测设置")]
    [Tooltip("是否忽略 Trigger 碰撞体，仅检测普通 Collider。通常建议关闭。")]
    [SerializeField] private QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.Collide;

    private MaterialPropertyBlock plateMpb;
    private MaterialPropertyBlock doorMpb;

    private bool isOpen;
    private bool isPlayerInsideActivationZone;
    private bool hasValidTargetOnPlate;
    private float refreshTimer;

    private Vector3 doorClosedWorldPosition;
    private Vector3 doorOpenWorldPosition;

    private Collider[] overlapBuffer = new Collider[32];

    public bool IsOpen => isOpen;
    public bool IsPlayerInsideActivationZone => isPlayerInsideActivationZone;
    public bool HasValidTargetOnPlate => hasValidTargetOnPlate;

    private void Reset()
    {
        if (plateRenderer == null)
            plateRenderer = GetComponent<Renderer>();

        AutoFindChildTriggersInEditor();
        ForceTriggerSetup();
    }

    private void Awake()
    {
        AutoFindMissingTriggers();
        ForceTriggerSetup();

        if (plateTrigger == null)
        {
            Debug.LogError($"[{nameof(PressurePlateDoorSwitch)}] 未设置 plateTrigger。", this);
            enabled = false;
            return;
        }

        if (playerActivationTrigger == null)
        {
            Debug.LogError($"[{nameof(PressurePlateDoorSwitch)}] 未设置 playerActivationTrigger。", this);
            enabled = false;
            return;
        }

        if (doorTransform == null)
        {
            Debug.LogError($"[{nameof(PressurePlateDoorSwitch)}] 未设置 doorTransform。", this);
            enabled = false;
            return;
        }

        EnsureRuntimeObjects();
        CacheDoorPositions();
    }

    private void OnEnable()
    {
        EnsureRuntimeObjects();

        if (captureClosedPositionOnEnable)
        {
            CacheDoorPositions();
        }

        RefreshAllStates();
        SnapDoorToCurrentState();
        UpdateVisuals();
    }

    private void Update()
    {
        if (refreshInterval <= 0f)
        {
            RefreshAllStates();
        }
        else
        {
            refreshTimer += Time.deltaTime;
            if (refreshTimer >= refreshInterval)
            {
                refreshTimer = 0f;
                RefreshAllStates();
            }
        }

        UpdateDoorMotion();
    }

    private void RefreshAllStates()
    {
        RefreshPlayerActivationState();

        if (!isPlayerInsideActivationZone)
        {
            ForceClosedState();
            return;
        }

        RefreshPlateState();
    }

    private void RefreshPlayerActivationState()
    {
        isPlayerInsideActivationZone = ScanTriggerForLayer(playerActivationTrigger, playerLayer);
    }

    private void RefreshPlateState()
    {
        LayerMask currentTargetMask = GetCurrentTargetMask();
        hasValidTargetOnPlate = ScanTriggerForLayer(plateTrigger, currentTargetMask);
        ApplyStateByTargetPresence(hasValidTargetOnPlate);
    }

    private LayerMask GetCurrentTargetMask()
    {
        switch (mechanismType)
        {
            case MechanismType.PresenceCustomTarget:
                return presenceCustomTargetLayers;

            case MechanismType.PresenceOpensDoor:
            default:
                return targetLayers;
        }
    }

    private bool ScanTriggerForLayer(Collider triggerCol, LayerMask mask)
    {
        if (triggerCol == null)
            return false;

        Bounds bounds = triggerCol.bounds;

        int count = Physics.OverlapBoxNonAlloc(
            bounds.center,
            bounds.extents,
            overlapBuffer,
            triggerCol.transform.rotation,
            mask,
            queryTriggerInteraction);

        if (count == overlapBuffer.Length)
        {
            overlapBuffer = new Collider[overlapBuffer.Length * 2];
            count = Physics.OverlapBoxNonAlloc(
                bounds.center,
                bounds.extents,
                overlapBuffer,
                triggerCol.transform.rotation,
                mask,
                queryTriggerInteraction);
        }

        for (int i = 0; i < count; i++)
        {
            Collider col = overlapBuffer[i];
            if (col == null)
                continue;

            if (!col.gameObject.activeInHierarchy)
                continue;

            if (col == triggerCol)
                continue;

            if (((1 << col.gameObject.layer) & mask.value) == 0)
                continue;

            if (!IsOverlapMeaningful(triggerCol, col))
                continue;

            return true;
        }

        return false;
    }

    private bool IsOverlapMeaningful(Collider triggerCol, Collider other)
    {
        if (triggerCol == null || other == null)
            return false;

        return triggerCol.bounds.Intersects(other.bounds);
    }

    private void ApplyStateByTargetPresence(bool targetPresent)
    {
        bool nextIsOpen;

        switch (mechanismType)
        {
            case MechanismType.PresenceCustomTarget:
                nextIsOpen = presenceCustomTargetOpensDoor ? targetPresent : !targetPresent;
                break;

            case MechanismType.PresenceOpensDoor:
            default:
                nextIsOpen = targetPresent;
                break;
        }

        isOpen = nextIsOpen;
        UpdateVisuals();
    }

    private void ForceClosedState()
    {
        isOpen = false;
        hasValidTargetOnPlate = false;
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        EnsureRuntimeObjects();

        Color targetPlateColor;
        Color targetDoorColor;

        if (!isPlayerInsideActivationZone)
        {
            targetPlateColor = inactivePlateColor;
            targetDoorColor = GetDefaultDoorColorByMechanismType();
        }
        else if (hasValidTargetOnPlate)
        {
            targetPlateColor = triggeredColor;
            targetDoorColor = triggeredColor;
        }
        else
        {
            targetPlateColor = GetDefaultPlateColorByMechanismType();
            targetDoorColor = GetDefaultDoorColorByMechanismType();
        }

        ApplyRendererColor(plateRenderer, plateMpb, targetPlateColor);
        ApplyRendererColor(doorRenderer, doorMpb, targetDoorColor);
    }

    private Color GetDefaultPlateColorByMechanismType()
    {
        switch (mechanismType)
        {
            case MechanismType.PresenceCustomTarget:
                return presenceCustomTargetDefaultPlateColor;

            case MechanismType.PresenceOpensDoor:
            default:
                return presenceOpensDoorDefaultPlateColor;
        }
    }

    private Color GetDefaultDoorColorByMechanismType()
    {
        switch (mechanismType)
        {
            case MechanismType.PresenceCustomTarget:
                return presenceCustomTargetDefaultDoorColor;

            case MechanismType.PresenceOpensDoor:
            default:
                return presenceOpensDoorDefaultDoorColor;
        }
    }

    private void ApplyRendererColor(Renderer targetRenderer, MaterialPropertyBlock targetMpb, Color color)
    {
        if (targetRenderer == null || targetMpb == null)
            return;

        targetRenderer.GetPropertyBlock(targetMpb);
        targetMpb.SetColor(colorPropertyName, color);
        targetRenderer.SetPropertyBlock(targetMpb);
    }

    private void EnsureRuntimeObjects()
    {
        if (plateMpb == null)
            plateMpb = new MaterialPropertyBlock();

        if (doorMpb == null)
            doorMpb = new MaterialPropertyBlock();

        if (plateRenderer == null)
            plateRenderer = GetComponent<Renderer>();

        if (doorRenderer == null && doorTransform != null)
            doorRenderer = doorTransform.GetComponent<Renderer>();
    }

    private void CacheDoorPositions()
    {
        if (doorTransform == null)
            return;

        doorClosedWorldPosition = doorTransform.position;

        Vector3 dir = openDirection;
        if (dir.sqrMagnitude > 0.000001f)
            dir.Normalize();

        Vector3 worldOffset;
        if (doorMoveSpace == DoorMoveSpace.Local)
        {
            worldOffset = doorTransform.TransformDirection(dir) * openDistance;
        }
        else
        {
            worldOffset = dir * openDistance;
        }

        doorOpenWorldPosition = doorClosedWorldPosition + worldOffset;
    }

    private void UpdateDoorMotion()
    {
        if (doorTransform == null)
            return;

        Vector3 target = isOpen ? doorOpenWorldPosition : doorClosedWorldPosition;
        float step = doorMoveSpeed * Time.deltaTime;
        doorTransform.position = Vector3.MoveTowards(doorTransform.position, target, step);
    }

    private void SnapDoorToCurrentState()
    {
        if (doorTransform == null)
            return;

        doorTransform.position = isOpen ? doorOpenWorldPosition : doorClosedWorldPosition;
    }

    private void AutoFindMissingTriggers()
    {
        if (plateTrigger != null && playerActivationTrigger != null)
            return;

        Collider[] cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            Collider col = cols[i];
            if (col == null)
                continue;

            string lowerName = col.gameObject.name.ToLower();

            if (plateTrigger == null &&
                (lowerName.Contains("plate") || lowerName.Contains("small")))
            {
                plateTrigger = col;
                continue;
            }

            if (playerActivationTrigger == null &&
                (lowerName.Contains("player") || lowerName.Contains("activation") || lowerName.Contains("large")))
            {
                playerActivationTrigger = col;
                continue;
            }
        }
    }

    private void AutoFindChildTriggersInEditor()
    {
        Collider[] cols = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            Collider col = cols[i];
            if (col == null)
                continue;

            string lowerName = col.gameObject.name.ToLower();

            if (plateTrigger == null &&
                (lowerName.Contains("plate") || lowerName.Contains("small")))
            {
                plateTrigger = col;
                continue;
            }

            if (playerActivationTrigger == null &&
                (lowerName.Contains("player") || lowerName.Contains("activation") || lowerName.Contains("large")))
            {
                playerActivationTrigger = col;
                continue;
            }
        }
    }

    private void ForceTriggerSetup()
    {
        if (plateTrigger != null)
            plateTrigger.isTrigger = true;

        if (playerActivationTrigger != null)
            playerActivationTrigger.isTrigger = true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        AutoFindChildTriggersInEditor();
        ForceTriggerSetup();
        EnsureRuntimeObjects();

        if (Application.isPlaying)
        {
            CacheDoorPositions();
            RefreshAllStates();
        }
        else
        {
            UpdateVisuals();
        }
    }
#endif
}