using UnityEngine;

[DisallowMultipleComponent]
public class BalanceLiftMassController : MonoBehaviour
{
    public enum HeightDifferenceReferenceMode
    {
        UseHighPlatformAsReference,
        UseLowPlatformAsReference
    }

    [Header("平台引用")]
    [SerializeField] private Transform highPlatform;
    [SerializeField] private Transform lowPlatform;

    [Header("质量统计区")]
    [SerializeField] private BalanceLiftMassZone highZone;
    [SerializeField] private BalanceLiftMassZone lowZone;

    [Header("移动轴")]
    [Tooltip("通常为世界 Y 轴。")]
    [SerializeField] private Vector3 moveAxis = Vector3.up;

    [Header("初始高度差设置")]
    [Tooltip("默认状态下，两平台沿 MoveAxis 的高度差。比如 10 表示 High 比 Low 高 10。")]
    [Min(0f)]
    [SerializeField] private float initialHeightDifference = 10f;

    [Tooltip("记录默认位置时，以哪个平台当前位置为基准，只修改另一个平台沿 MoveAxis 的位置。")]
    [SerializeField] private HeightDifferenceReferenceMode referenceMode = HeightDifferenceReferenceMode.UseLowPlatformAsReference;

    [Tooltip("Awake 时记录当前平台位置作为默认位置。")]
    [SerializeField] private bool captureDefaultPositionsOnAwake = true;

    [Tooltip("记录默认位置时，是否强制按 Initial Height Difference 修正另一侧平台。")]
    [SerializeField] private bool enforceInitialHeightDifferenceOnCapture = true;

    [Header("质量差映射")]
    [Tooltip("effectiveMass 达到该值时，高低关系完全反转。比如 20 表示 effectiveMass=20 时 Low 比 High 高 InitialHeightDifference。")]
    [Min(0.0001f)]
    [SerializeField] private float targetMassToInvert = 20f;

    [Tooltip("平台朝目标位置移动的速度。")]
    [Min(0f)]
    [SerializeField] private float moveSpeed = 3f;

    [Header("限制")]
    [Tooltip("是否限制 effectiveMass 的范围。")]
    [SerializeField] private bool clampEffectiveMass = true;

    [Tooltip("effectiveMass 的绝对值上限。通常设为 TargetMassToInvert。")]
    [Min(0.0001f)]
    [SerializeField] private float maxEffectiveMassMagnitude = 20f;

    [Header("调试")]
    [SerializeField] private bool logMassInfo = false;

    [Header("运行时只读")]
    [SerializeField] private float currentHighMass;
    [SerializeField] private float currentLowMass;
    [SerializeField] private float currentEffectiveMass;
    [SerializeField] private float currentTargetDifference;
    [SerializeField] private float currentOffsetFromDefault;

    private Vector3 axisNormalized;
    private Vector3 highDefaultPosition;
    private Vector3 lowDefaultPosition;

    // > 0 : High 下 / Low 上
    // < 0 : High 上 / Low 下
    private float currentOffset;

    public float HighMass => highZone != null ? highZone.CurrentTotalMass : 0f;
    public float LowMass => lowZone != null ? lowZone.CurrentTotalMass : 0f;
    public float EffectiveMass => HighMass - LowMass;

    private void Reset()
    {
        moveAxis = Vector3.up;
        initialHeightDifference = 10f;
        referenceMode = HeightDifferenceReferenceMode.UseLowPlatformAsReference;
        captureDefaultPositionsOnAwake = true;
        enforceInitialHeightDifferenceOnCapture = true;

        targetMassToInvert = 20f;
        moveSpeed = 3f;
        clampEffectiveMass = true;
        maxEffectiveMassMagnitude = 20f;

        logMassInfo = false;
    }

    private void Awake()
    {
        if (highPlatform == null || lowPlatform == null)
        {
            Debug.LogError("[BalanceLiftMassController] HighPlatform / LowPlatform 未设置。", this);
            enabled = false;
            return;
        }

        axisNormalized = moveAxis.sqrMagnitude > 0.000001f ? moveAxis.normalized : Vector3.up;

        if (captureDefaultPositionsOnAwake)
        {
            CaptureDefaultPositionsInternal(enforceInitialHeightDifferenceOnCapture);
        }
        else
        {
            highDefaultPosition = highPlatform.position;
            lowDefaultPosition = lowPlatform.position;
        }

        // effectiveMass = 0 时，平台就应保持默认位置，不额外移动
        currentOffset = GetTargetOffset();
        ApplyImmediate(currentOffset);
        UpdateRuntimeDebugValues();
    }

    private void Update()
    {
        float targetOffset = GetTargetOffset();
        currentOffset = Mathf.MoveTowards(currentOffset, targetOffset, moveSpeed * Time.deltaTime);

        ApplyImmediate(currentOffset);
        UpdateRuntimeDebugValues();

        if (logMassInfo)
        {
            Debug.Log(
                $"[BalanceLiftMassController] HighMass={currentHighMass:F2}, LowMass={currentLowMass:F2}, EffectiveMass={currentEffectiveMass:F2}",
                this
            );
        }
    }

    private void CaptureDefaultPositionsInternal(bool enforceDifference)
    {
        Vector3 currentHigh = highPlatform.position;
        Vector3 currentLow = lowPlatform.position;

        if (!enforceDifference)
        {
            highDefaultPosition = currentHigh;
            lowDefaultPosition = currentLow;
            return;
        }

        switch (referenceMode)
        {
            case HeightDifferenceReferenceMode.UseHighPlatformAsReference:
            {
                // 保持高位平台当前位置不动
                highDefaultPosition = currentHigh;

                // 低位平台保留自身“非轴向分量”，只改沿 moveAxis 的轴向标量
                float highAxis = Vector3.Dot(currentHigh, axisNormalized);
                float targetLowAxis = highAxis - initialHeightDifference;

                lowDefaultPosition = SetAxisComponent(currentLow, targetLowAxis);
                break;
            }

            case HeightDifferenceReferenceMode.UseLowPlatformAsReference:
            default:
            {
                // 保持低位平台当前位置不动
                lowDefaultPosition = currentLow;

                // 高位平台保留自身“非轴向分量”，只改沿 moveAxis 的轴向标量
                float lowAxis = Vector3.Dot(currentLow, axisNormalized);
                float targetHighAxis = lowAxis + initialHeightDifference;

                highDefaultPosition = SetAxisComponent(currentHigh, targetHighAxis);
                break;
            }
        }
    }

    /// <summary>
    /// 保留 worldPos 在 moveAxis 垂直方向上的分量，只修改其沿 moveAxis 的标量值。
    /// 若 moveAxis = Vector3.up，则等价于只改 Y，不改 X/Z。
    /// </summary>
    private Vector3 SetAxisComponent(Vector3 worldPos, float targetAxisValue)
    {
        float currentAxisValue = Vector3.Dot(worldPos, axisNormalized);
        return worldPos + axisNormalized * (targetAxisValue - currentAxisValue);
    }

    private float GetTargetOffset()
    {
        float effectiveMass = EffectiveMass;

        if (clampEffectiveMass)
            effectiveMass = Mathf.Clamp(effectiveMass, -maxEffectiveMassMagnitude, maxEffectiveMassMagnitude);

        // 默认位置本身就是 effectiveMass = 0 时的状态：
        // effectiveMass = 0 -> offset = 0
        // effectiveMass = targetMassToInvert * 0.5 -> 两平台持平
        // effectiveMass = targetMassToInvert -> 完全反转
        float offsetPerMass = initialHeightDifference / targetMassToInvert;
        return effectiveMass * offsetPerMass;
    }

    private void ApplyImmediate(float offset)
    {
        if (highPlatform != null)
            highPlatform.position = highDefaultPosition - axisNormalized * offset;

        if (lowPlatform != null)
            lowPlatform.position = lowDefaultPosition + axisNormalized * offset;
    }

    private void UpdateRuntimeDebugValues()
    {
        currentHighMass = HighMass;
        currentLowMass = LowMass;
        currentEffectiveMass = EffectiveMass;
        currentOffsetFromDefault = currentOffset;

        // 当前总高度差 = 默认高度差 - 2 * offset
        float predictedDifference = initialHeightDifference - 2f * currentOffset;
        currentTargetDifference = Mathf.Clamp(
            predictedDifference,
            -initialHeightDifference,
            initialHeightDifference
        );
    }

    [ContextMenu("记录当前为默认位置")]
    public void CaptureCurrentAsDefaultPositions()
    {
        CaptureDefaultPositionsInternal(enforceInitialHeightDifferenceOnCapture);
        currentOffset = GetTargetOffset();
        ApplyImmediate(currentOffset);
        UpdateRuntimeDebugValues();
    }

    [ContextMenu("重置到默认位置")]
    public void ResetToDefaultPositions()
    {
        currentOffset = 0f;

        if (highPlatform != null)
            highPlatform.position = highDefaultPosition;

        if (lowPlatform != null)
            lowPlatform.position = lowDefaultPosition;

        UpdateRuntimeDebugValues();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (moveAxis.sqrMagnitude <= 0.000001f)
            moveAxis = Vector3.up;

        if (targetMassToInvert < 0.0001f)
            targetMassToInvert = 0.0001f;

        if (maxEffectiveMassMagnitude < 0.0001f)
            maxEffectiveMassMagnitude = 0.0001f;

        if (initialHeightDifference < 0f)
            initialHeightDifference = 0f;
    }
#endif
}