using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("Game/UI/Ability Input Preview")]
public class AbilityInputPreview : MonoBehaviour
{
    public enum AbilityType
    {
        TapWindowConfirm,   // 点按进入窗口，再点按完成
        TwoStageHoldCharge, // 两段蓄力
        Toggle,             // 点按开关
        HoldActive          // 长按保持
    }

    public enum AbilityVisualState
    {
        Locked,
        Ready,
        Pressing,
        Using,
        Cooldown,
        Failed
    }

    public enum InputBindingMode
    {
        LocalKey,
        ASCIIWorldModeManagerAction,
        ASCIIRewindChargeControllerAction
    }

    public enum BoundActionType
    {
        None = 0,
        QScanMode = 1,
        EProjectionMode = 2,
        RRewindMode = 3,
        MouseRightAimMode = 4
    }

    [Serializable]
    public sealed class AbilityBinding
    {
        [Header("基础")]
        public AbilityType abilityType;
        public InputBindingMode inputBindingMode = InputBindingMode.LocalKey;
        public KeyCode triggerKey;
        public BoundActionType boundActionType = BoundActionType.None;
        public bool unlocked = true;

        [Header("UI")]
        public Image iconImage;
        public Graphic labelGraphic;
        public Image radialProgressImage;

        [Header("冷却时间")]
        [Min(0f)] public float cooldownDuration = 0f;

        [Header("窗口确认")]
        [Min(0.01f)] public float tapWindowDuration = 3f;
        [Min(0.01f)] public float tapConfirmFeedbackDuration = 0.2f;

        [Header("两段蓄力")]
        [Min(0.01f)] public float firstStageDuration = 1f;
        [Min(0.01f)] public float secondStageDuration = 1.5f;
        public bool allowEnterSecondStage = true;
        [Min(0.01f)] public float failedFeedbackDuration = 0.2f;

        [NonSerialized] public AbilityVisualState runtimeState = AbilityVisualState.Ready;
        [NonSerialized] public float timer = 0f;
        [NonSerialized] public float cooldownTimer = 0f;
        [NonSerialized] public bool toggleActive = false;
        [NonSerialized] public int chargeStage = 0; // 0=无, 1=第一段, 2=第二段
        [NonSerialized] public bool blockReenterUntilRelease = false;
        [NonSerialized] public bool tapConfirmFeedbackActive = false;
        [NonSerialized] public Vector3 initialScale = Vector3.one;
        [NonSerialized] public bool externalPrevActive = false;
        [NonSerialized] public bool externalPrevPreviewSpawned = false;
        [NonSerialized] public bool externalPrevCooldown = false;
    }

    [Header("引用")]
    public ASCIIWorldModeManager asciiWorldModeManager;
    public ASCIIRewindChargeController asciiRewindChargeController;

    [Header("能力配置")]
    public List<AbilityBinding> abilities = new List<AbilityBinding>();

    [Header("颜色")]
    public Color lockedColor = new Color(0.22f, 0.24f, 0.28f, 0.45f);
    public Color readyColor = new Color(0.94f, 0.96f, 1.00f, 1.00f);
    public Color pressingColor = new Color(1.00f, 0.88f, 0.32f, 1.00f);
    public Color usingColor = new Color(0.45f, 0.90f, 1.00f, 1.00f);
    public Color cooldownColor = new Color(0.58f, 0.64f, 0.76f, 0.95f);
    public Color failedColor = new Color(1.00f, 0.30f, 0.30f, 1.00f);

    [Header("环形颜色")]
    public Color pressingRadialColor = new Color(1.00f, 0.86f, 0.30f, 0.95f);
    public Color usingRadialColor = new Color(0.45f, 0.90f, 1.00f, 0.95f);
    public Color cooldownRadialColor = new Color(0.30f, 0.40f, 0.55f, 0.85f);

    [Header("统一缩放")]
    [Min(1f)] public float usingScaleMultiplier = 1.06f;

    private void Awake()
    {
        if (asciiWorldModeManager == null)
            asciiWorldModeManager = FindObjectOfType<ASCIIWorldModeManager>();
        if (asciiRewindChargeController == null)
            asciiRewindChargeController = FindObjectOfType<ASCIIRewindChargeController>();

        CacheInitialScales();
        ResetAllAbilities();
    }

    private void OnEnable()
    {
        if (asciiWorldModeManager == null)
            asciiWorldModeManager = FindObjectOfType<ASCIIWorldModeManager>();
        if (asciiRewindChargeController == null)
            asciiRewindChargeController = FindObjectOfType<ASCIIRewindChargeController>();

        CacheInitialScales();
        RefreshAllVisuals();
    }

    private void Update()
    {
        for (int i = 0; i < abilities.Count; i++)
        {
            AbilityBinding ability = abilities[i];
            if (ability == null)
                continue;

            UpdateAbility(ability);
            RefreshVisual(ability);
        }
    }

    private void CacheInitialScales()
    {
        for (int i = 0; i < abilities.Count; i++)
        {
            AbilityBinding ability = abilities[i];
            if (ability == null || ability.iconImage == null)
                continue;

            ability.initialScale = ability.iconImage.rectTransform.localScale;
        }
    }

    private void ResetAllAbilities()
    {
        for (int i = 0; i < abilities.Count; i++)
        {
            AbilityBinding ability = abilities[i];
            if (ability == null)
                continue;

            ability.timer = 0f;
            ability.cooldownTimer = 0f;
            ability.toggleActive = false;
            ability.chargeStage = 0;
            ability.blockReenterUntilRelease = false;
            ability.tapConfirmFeedbackActive = false;
            ability.externalPrevActive = false;
            ability.externalPrevPreviewSpawned = false;
            ability.externalPrevCooldown = false;
            ability.runtimeState = ability.unlocked ? AbilityVisualState.Ready : AbilityVisualState.Locked;
        }

        RefreshAllVisuals();
    }

    private void RefreshAllVisuals()
    {
        for (int i = 0; i < abilities.Count; i++)
        {
            AbilityBinding ability = abilities[i];
            if (ability == null)
                continue;

            RefreshVisual(ability);
        }
    }

    private void UpdateAbility(AbilityBinding ability)
    {
        if (ability == null)
            return;

        if (!ability.unlocked)
        {
            ability.timer = 0f;
            ability.cooldownTimer = 0f;
            ability.toggleActive = false;
            ability.chargeStage = 0;
            ability.blockReenterUntilRelease = false;
            ability.tapConfirmFeedbackActive = false;
            ability.externalPrevActive = false;
            ability.externalPrevPreviewSpawned = false;
            ability.externalPrevCooldown = false;
            ability.runtimeState = AbilityVisualState.Locked;
            return;
        }

        if (ability.inputBindingMode != InputBindingMode.LocalKey)
        {
            UpdateExternalBoundAbility(ability);
            return;
        }

        if (ability.cooldownTimer > 0f)
        {
            ability.cooldownTimer -= Time.deltaTime;
            if (ability.cooldownTimer <= 0f)
            {
                ability.cooldownTimer = 0f;
                ability.runtimeState = AbilityVisualState.Ready;
            }
            else
            {
                ability.runtimeState = AbilityVisualState.Cooldown;
            }
            return;
        }

        switch (ability.abilityType)
        {
            case AbilityType.TapWindowConfirm:
                UpdateTapWindowConfirm(ability);
                break;

            case AbilityType.TwoStageHoldCharge:
                UpdateTwoStageHoldCharge(ability);
                break;

            case AbilityType.Toggle:
                UpdateToggle(ability);
                break;

            case AbilityType.HoldActive:
                UpdateHoldActive(ability);
                break;
        }
    }

    private void UpdateExternalBoundAbility(AbilityBinding ability)
    {
        switch (ability.inputBindingMode)
        {
            case InputBindingMode.ASCIIWorldModeManagerAction:
                UpdateASCIIWorldModeManagerBoundAbility(ability);
                break;

            case InputBindingMode.ASCIIRewindChargeControllerAction:
                UpdateASCIIRewindBoundAbility(ability);
                break;

            default:
                ResetExternalRuntimeState(ability);
                ability.runtimeState = AbilityVisualState.Ready;
                break;
        }
    }

    private void UpdateASCIIWorldModeManagerBoundAbility(AbilityBinding ability)
    {
        switch (ability.boundActionType)
        {
            case BoundActionType.QScanMode:
                UpdateExternalTapWindowConfirm(
                    ability,
                    asciiWorldModeManager != null && asciiWorldModeManager.IsRevertNearbyVirtualPreviewActive,
                    IsBoundActionPressedThisFrame(ability),
                    GetWorldModeManagerTapWindowDuration(ability));
                return;

            case BoundActionType.EProjectionMode:
                UpdateExternalToggle(
                    ability,
                    asciiWorldModeManager != null && asciiWorldModeManager.IsProjectionMode);
                return;

            case BoundActionType.MouseRightAimMode:
                UpdateExternalHoldActive(
                    ability,
                    asciiWorldModeManager != null && asciiWorldModeManager.IsAimMode);
                return;

            default:
                ResetExternalRuntimeState(ability);
                ability.runtimeState = AbilityVisualState.Ready;
                return;
        }
    }

    private void UpdateASCIIRewindBoundAbility(AbilityBinding ability)
    {
        if (asciiRewindChargeController == null)
        {
            ResetExternalRuntimeState(ability);
            ability.runtimeState = ability.unlocked ? AbilityVisualState.Ready : AbilityVisualState.Locked;
            return;
        }

        bool hasValidTrigger = asciiRewindChargeController.CurrentValidTrigger != null;
        bool isCharging = asciiRewindChargeController.IsCharging;
        bool previewSpawned = asciiRewindChargeController.PreviewSpawned;
        bool isCoolingDown = asciiRewindChargeController.IsCoolingDown;

        bool wasCharging = ability.externalPrevActive;
        bool wasCoolingDown = ability.externalPrevCooldown;
        int previousChargeStage = ability.chargeStage;

        bool chargeEntered = isCharging && !wasCharging;
        bool chargeExited = !isCharging && wasCharging;
        bool cooldownEntered = isCoolingDown && !wasCoolingDown;

        KeyCode rewindKey = asciiRewindChargeController.rewindKey;
        bool rewindHeld = Input.GetKey(rewindKey);
        bool rewindDown = Input.GetKeyDown(rewindKey);

        ability.externalPrevActive = isCharging;
        ability.externalPrevPreviewSpawned = previewSpawned;
        ability.externalPrevCooldown = isCoolingDown;
        ability.toggleActive = isCharging;
        ability.tapConfirmFeedbackActive = false;
        ability.blockReenterUntilRelease = false;

        float failedDuration = Mathf.Max(0.01f, ability.failedFeedbackDuration);
        float stage2Duration = Mathf.Max(0.01f, ability.secondStageDuration);
        float controllerStage1Threshold = Mathf.Max(0.01f, asciiRewindChargeController.AbortThresholdTime);
        float controllerStage2TotalThreshold = Mathf.Max(controllerStage1Threshold, asciiRewindChargeController.PreviewThresholdTime);
        float controllerStage2Duration = Mathf.Max(0.01f, controllerStage2TotalThreshold - controllerStage1Threshold);
        float runtimeCooldownDuration = Mathf.Max(
            0f,
            ability.boundActionType == BoundActionType.RRewindMode
                ? asciiRewindChargeController.cooldownAfterSuccess
                : ability.cooldownDuration);

        if (cooldownEntered)
        {
            ability.cooldownTimer = runtimeCooldownDuration;
            ability.timer = 0f;
            ability.chargeStage = 0;
            ability.runtimeState = AbilityVisualState.Cooldown;
        }

        if (isCoolingDown)
        {
            if (runtimeCooldownDuration > 0f)
                ability.cooldownTimer = Mathf.Max(0f, ability.cooldownTimer - Time.deltaTime);
            else
                ability.cooldownTimer = 0f;

            ability.runtimeState = AbilityVisualState.Cooldown;
            return;
        }

        ability.cooldownTimer = 0f;

        // Failed 反馈保活：只允许主动再次按住 R 打断
        if (ability.runtimeState == AbilityVisualState.Failed)
        {
            bool activelyRestarting = rewindHeld && (rewindDown || isCharging);

            if (activelyRestarting)
            {
                ability.timer = 0f;

                if (isCharging)
                {
                    float chargeTimerNow = asciiRewindChargeController.ChargeTimer;
                    if (previewSpawned || (hasValidTrigger && chargeTimerNow >= controllerStage1Threshold))
                    {
                        ability.chargeStage = 2;
                        float stage2Elapsed = Mathf.Max(0f, chargeTimerNow - controllerStage1Threshold);
                        ability.timer = Mathf.Min(stage2Duration, stage2Elapsed / controllerStage2Duration * stage2Duration);
                        ability.runtimeState = AbilityVisualState.Using;
                    }
                    else
                    {
                        ability.chargeStage = 1;
                        ability.runtimeState = AbilityVisualState.Pressing;
                    }
                }
                else
                {
                    ability.chargeStage = 1;
                    ability.runtimeState = AbilityVisualState.Pressing;
                }

                return;
            }

            ability.timer += Time.deltaTime;
            if (ability.timer >= failedDuration)
            {
                ability.timer = 0f;
                ability.chargeStage = 0;
                ability.runtimeState = AbilityVisualState.Ready;
            }
            else
            {
                ability.runtimeState = AbilityVisualState.Failed;
            }

            return;
        }

        if (chargeEntered)
        {
            ability.timer = 0f;
            ability.chargeStage = 1;
            ability.runtimeState = AbilityVisualState.Pressing;
        }

        if (chargeExited)
        {
            bool failedByInvalidArea =
                previousChargeStage == 1 &&
                !previewSpawned &&
                !isCoolingDown &&
                rewindHeld;

            ability.timer = 0f;
            ability.chargeStage = 0;
            ability.runtimeState = failedByInvalidArea
                ? AbilityVisualState.Failed
                : AbilityVisualState.Ready;
            return;
        }

        if (!isCharging)
        {
            ability.timer = 0f;
            ability.chargeStage = 0;
            ability.runtimeState = AbilityVisualState.Ready;
            return;
        }

        float chargeTimer = asciiRewindChargeController.ChargeTimer;

        if (previewSpawned || (hasValidTrigger && chargeTimer >= controllerStage1Threshold))
        {
            ability.chargeStage = 2;
            float stage2Elapsed = Mathf.Max(0f, chargeTimer - controllerStage1Threshold);
            ability.timer = Mathf.Min(stage2Duration, stage2Elapsed / controllerStage2Duration * stage2Duration);
            ability.runtimeState = AbilityVisualState.Using;
            return;
        }

        ability.chargeStage = 1;
        ability.timer = Mathf.Min(controllerStage1Threshold, chargeTimer);
        ability.runtimeState = AbilityVisualState.Pressing;
    }

    private void UpdateExternalTapWindowConfirm(
        AbilityBinding ability,
        bool isActive,
        bool confirmPressedThisFrame,
        float runtimeTapWindowDuration)
    {
        bool wasActive = ability.externalPrevActive;
        bool entered = isActive && !wasActive;
        bool exited = !isActive && wasActive;

        ability.externalPrevActive = isActive;
        ability.externalPrevPreviewSpawned = false;
        ability.externalPrevCooldown = false;
        ability.toggleActive = isActive;
        ability.cooldownTimer = 0f;
        ability.blockReenterUntilRelease = false;

        if (ability.tapConfirmFeedbackActive)
        {
            if (confirmPressedThisFrame)
            {
                ability.timer = 0f;
                ability.tapConfirmFeedbackActive = false;
                ability.runtimeState = AbilityVisualState.Using;
                return;
            }

            ability.timer += Time.deltaTime;
            if (ability.timer >= Mathf.Max(0.01f, ability.tapConfirmFeedbackDuration))
            {
                ability.timer = 0f;
                ability.tapConfirmFeedbackActive = false;
                ability.runtimeState = AbilityVisualState.Ready;
            }
            else
            {
                ability.runtimeState = AbilityVisualState.Pressing;
            }
            return;
        }

        if (entered)
            ability.timer = 0f;

        if (isActive)
        {
            if (!entered && confirmPressedThisFrame)
            {
                ability.timer = 0f;
                ability.tapConfirmFeedbackActive = true;
                ability.runtimeState = AbilityVisualState.Pressing;
                return;
            }

            ability.timer += Time.deltaTime;
            ability.runtimeState = AbilityVisualState.Using;
            return;
        }

        if (exited && confirmPressedThisFrame)
        {
            ability.timer = 0f;
            ability.tapConfirmFeedbackActive = true;
            ability.runtimeState = AbilityVisualState.Pressing;
            return;
        }

        if (ability.runtimeState == AbilityVisualState.Using && ability.timer > runtimeTapWindowDuration)
        {
            ability.timer = 0f;
        }

        ability.timer = 0f;
        ability.runtimeState = AbilityVisualState.Ready;
    }

    private void UpdateExternalToggle(AbilityBinding ability, bool isActive)
    {
        ability.externalPrevActive = isActive;
        ability.externalPrevPreviewSpawned = false;
        ability.externalPrevCooldown = false;
        ability.toggleActive = isActive;
        ability.cooldownTimer = 0f;
        ability.timer = 0f;
        ability.chargeStage = 0;
        ability.tapConfirmFeedbackActive = false;
        ability.runtimeState = isActive ? AbilityVisualState.Using : AbilityVisualState.Ready;
    }

    private void UpdateExternalHoldActive(AbilityBinding ability, bool isActive)
    {
        ability.externalPrevActive = isActive;
        ability.externalPrevPreviewSpawned = false;
        ability.externalPrevCooldown = false;
        ability.toggleActive = isActive;
        ability.cooldownTimer = 0f;
        ability.timer = 0f;
        ability.chargeStage = 0;
        ability.tapConfirmFeedbackActive = false;
        ability.runtimeState = isActive ? AbilityVisualState.Using : AbilityVisualState.Ready;
    }

    private void ResetExternalRuntimeState(AbilityBinding ability)
    {
        ability.timer = 0f;
        ability.cooldownTimer = 0f;
        ability.toggleActive = false;
        ability.chargeStage = 0;
        ability.blockReenterUntilRelease = false;
        ability.tapConfirmFeedbackActive = false;
        ability.externalPrevActive = false;
        ability.externalPrevPreviewSpawned = false;
        ability.externalPrevCooldown = false;
    }

    private bool IsBoundActionPressedThisFrame(AbilityBinding ability)
    {
        if (ability == null)
            return false;

        if (ability.triggerKey != KeyCode.None && Input.GetKeyDown(ability.triggerKey))
            return true;

        switch (ability.boundActionType)
        {
            case BoundActionType.QScanMode:
                return asciiWorldModeManager != null && Input.GetKeyDown(asciiWorldModeManager.revertNearbyVirtualKey);
            case BoundActionType.EProjectionMode:
                return asciiWorldModeManager != null && Input.GetKeyDown(asciiWorldModeManager.toggleProjectionModeKey);
            case BoundActionType.RRewindMode:
                return asciiRewindChargeController != null && Input.GetKeyDown(asciiRewindChargeController.rewindKey);
            case BoundActionType.MouseRightAimMode:
                return asciiWorldModeManager != null && Input.GetKeyDown(asciiWorldModeManager.holdAimKey);
            default:
                return false;
        }
    }

    private float GetWorldModeManagerTapWindowDuration(AbilityBinding ability)
    {
        if (ability == null)
            return 0.01f;

        if (ability.inputBindingMode == InputBindingMode.ASCIIWorldModeManagerAction &&
            ability.boundActionType == BoundActionType.QScanMode &&
            asciiWorldModeManager != null)
        {
            return Mathf.Max(0.01f, asciiWorldModeManager.revertNearbyVirtualPreviewDuration);
        }

        return Mathf.Max(0.01f, ability.tapWindowDuration);
    }

    private void UpdateTapWindowConfirm(AbilityBinding ability)
    {
        if (ability.tapConfirmFeedbackActive)
        {
            if (Input.GetKeyDown(ability.triggerKey))
            {
                ability.timer = 0f;
                ability.tapConfirmFeedbackActive = false;
                ability.runtimeState = AbilityVisualState.Using;
                return;
            }

            ability.timer += Time.deltaTime;

            if (ability.timer >= Mathf.Max(0.01f, ability.tapConfirmFeedbackDuration))
            {
                ability.timer = 0f;
                ability.tapConfirmFeedbackActive = false;
                ability.runtimeState = AbilityVisualState.Ready;
            }
            else
            {
                ability.runtimeState = AbilityVisualState.Pressing;
            }

            return;
        }

        if (ability.runtimeState == AbilityVisualState.Using)
        {
            ability.timer += Time.deltaTime;

            if (Input.GetKeyDown(ability.triggerKey))
            {
                ability.timer = 0f;
                ability.tapConfirmFeedbackActive = true;
                ability.runtimeState = AbilityVisualState.Pressing;
                return;
            }

            if (ability.timer >= Mathf.Max(0.01f, ability.tapWindowDuration))
            {
                ability.timer = 0f;
                ability.runtimeState = AbilityVisualState.Ready;
            }

            return;
        }

        if (Input.GetKeyDown(ability.triggerKey))
        {
            ability.timer = 0f;
            ability.tapConfirmFeedbackActive = false;
            ability.runtimeState = AbilityVisualState.Using;
        }
        else
        {
            ability.runtimeState = AbilityVisualState.Ready;
        }
    }

    private void UpdateTwoStageHoldCharge(AbilityBinding ability)
    {
        bool holding = Input.GetKey(ability.triggerKey);
        bool pressedThisFrame = Input.GetKeyDown(ability.triggerKey);

        // Failed 反馈保活：只允许主动再次按下打断
        if (ability.runtimeState == AbilityVisualState.Failed)
        {
            if (pressedThisFrame)
            {
                ability.timer = 0f;
                ability.chargeStage = 1;
                ability.blockReenterUntilRelease = false;
                ability.runtimeState = AbilityVisualState.Pressing;
                return;
            }

            ability.timer += Time.deltaTime;
            if (ability.timer >= Mathf.Max(0.01f, ability.failedFeedbackDuration))
            {
                ability.timer = 0f;
                ability.chargeStage = 0;
                ability.blockReenterUntilRelease = false;
                ability.runtimeState = AbilityVisualState.Ready;
            }
            else
            {
                ability.runtimeState = AbilityVisualState.Failed;
            }

            return;
        }

        // 主动取消：任何非 Failed 状态下松手都直接回 Ready
        if (!holding)
        {
            ability.timer = 0f;
            ability.chargeStage = 0;
            ability.blockReenterUntilRelease = false;
            ability.runtimeState = AbilityVisualState.Ready;
            return;
        }

        if (ability.blockReenterUntilRelease)
        {
            ability.runtimeState = AbilityVisualState.Ready;
            return;
        }

        if (ability.chargeStage == 0)
        {
            ability.chargeStage = 1;
            ability.timer = 0f;
        }

        if (ability.chargeStage == 1)
        {
            ability.timer += Time.deltaTime;
            ability.runtimeState = AbilityVisualState.Pressing;

            if (ability.timer >= Mathf.Max(0.01f, ability.firstStageDuration))
            {
                if (!ability.allowEnterSecondStage)
                {
                    ability.timer = 0f;
                    ability.chargeStage = 0;
                    ability.runtimeState = AbilityVisualState.Failed;
                    return;
                }

                ability.chargeStage = 2;
                ability.timer = 0f;
                ability.runtimeState = AbilityVisualState.Using;
            }

            return;
        }

        if (ability.chargeStage == 2)
        {
            ability.timer += Time.deltaTime;
            ability.runtimeState = AbilityVisualState.Using;

            if (ability.timer >= Mathf.Max(0.01f, ability.secondStageDuration))
            {
                ability.timer = 0f;
                ability.chargeStage = 0;
                ability.cooldownTimer = Mathf.Max(0f, ability.cooldownDuration);
                ability.runtimeState = ability.cooldownTimer > 0f
                    ? AbilityVisualState.Cooldown
                    : AbilityVisualState.Ready;
            }
        }
    }

    private void UpdateToggle(AbilityBinding ability)
    {
        if (Input.GetKeyDown(ability.triggerKey))
            ability.toggleActive = !ability.toggleActive;

        ability.runtimeState = ability.toggleActive
            ? AbilityVisualState.Using
            : AbilityVisualState.Ready;
    }

    private void UpdateHoldActive(AbilityBinding ability)
    {
        bool holding = Input.GetKey(ability.triggerKey);
        ability.runtimeState = holding
            ? AbilityVisualState.Using
            : AbilityVisualState.Ready;
    }

    private void RefreshVisual(AbilityBinding ability)
    {
        if (ability == null)
            return;

        Color targetColor = readyColor;
        Color radialColor = pressingRadialColor;
        bool showRadial = false;
        float radialFill = 0f;
        float scaleMultiplier = 1f;

        switch (ability.runtimeState)
        {
            case AbilityVisualState.Locked:
                targetColor = lockedColor;
                showRadial = false;
                break;

            case AbilityVisualState.Ready:
                targetColor = readyColor;
                showRadial = false;
                break;

            case AbilityVisualState.Pressing:
                targetColor = pressingColor;
                radialColor = pressingRadialColor;
                showRadial = ShouldShowPressingRadial(ability);
                scaleMultiplier = usingScaleMultiplier;
                radialFill = GetPressingFill(ability);
                break;

            case AbilityVisualState.Using:
                targetColor = ShouldKeepIconReadyColorWhileUsing(ability) ? readyColor : usingColor;
                radialColor = GetUsingRadialColor(ability);
                scaleMultiplier = GetUsingScaleMultiplier(ability);
                showRadial = ShouldShowUsingRadial(ability);
                radialFill = GetUsingFill(ability);
                break;

            case AbilityVisualState.Cooldown:
                targetColor = cooldownColor;
                radialColor = cooldownRadialColor;
                showRadial = true;
                radialFill = GetCooldownFill(ability);
                break;

            case AbilityVisualState.Failed:
                targetColor = failedColor;
                showRadial = false;
                scaleMultiplier = usingScaleMultiplier;
                radialFill = 0f;
                break;
        }

        if (ability.iconImage != null)
        {
            ability.iconImage.color = targetColor;
            ability.iconImage.rectTransform.localScale = ability.initialScale * scaleMultiplier;
        }

        if (ability.labelGraphic != null)
            ability.labelGraphic.color = targetColor;

        if (ability.radialProgressImage != null)
        {
            ability.radialProgressImage.enabled = showRadial;
            ability.radialProgressImage.color = radialColor;
            ability.radialProgressImage.fillAmount = showRadial ? radialFill : 0f;
        }
    }

    private bool ShouldShowPressingRadial(AbilityBinding ability)
    {
        if (ability == null)
            return false;

        if (ability.abilityType == AbilityType.TapWindowConfirm && ability.tapConfirmFeedbackActive)
            return false;

        return ability.abilityType == AbilityType.TwoStageHoldCharge;
    }

    private float GetPressingFill(AbilityBinding ability)
    {
        if (ability == null)
            return 0f;

        if (ability.abilityType == AbilityType.TapWindowConfirm && ability.tapConfirmFeedbackActive)
            return 0f;

        if (ability.abilityType == AbilityType.TwoStageHoldCharge)
        {
            if (ability.chargeStage == 1)
                return Mathf.Clamp01(ability.timer / Mathf.Max(0.01f, ability.firstStageDuration));

            if (ability.chargeStage == 2)
                return Mathf.Clamp01(ability.timer / Mathf.Max(0.01f, ability.secondStageDuration));
        }

        return 0f;
    }

    private Color GetUsingRadialColor(AbilityBinding ability)
    {
        if (ability == null)
            return usingRadialColor;

        if (ability.inputBindingMode == InputBindingMode.ASCIIRewindChargeControllerAction &&
            ability.boundActionType == BoundActionType.RRewindMode)
            return usingColor;

        return usingRadialColor;
    }

    private bool ShouldKeepIconReadyColorWhileUsing(AbilityBinding ability)
    {
        if (ability == null)
            return false;

        return ability.inputBindingMode == InputBindingMode.ASCIIRewindChargeControllerAction &&
               ability.boundActionType == BoundActionType.RRewindMode;
    }

    private float GetUsingScaleMultiplier(AbilityBinding ability)
    {
        if (ability == null)
            return usingScaleMultiplier;

        return usingScaleMultiplier;
    }

    private bool ShouldShowUsingRadial(AbilityBinding ability)
    {
        if (ability == null)
            return false;

        return ability.abilityType == AbilityType.TapWindowConfirm ||
               ability.abilityType == AbilityType.TwoStageHoldCharge;
    }

    private float GetUsingFill(AbilityBinding ability)
    {
        if (ability == null)
            return 0f;

        if (ability.abilityType == AbilityType.TapWindowConfirm)
        {
            float runtimeTapWindowDuration = GetWorldModeManagerTapWindowDuration(ability);
            return Mathf.Clamp01(ability.timer / runtimeTapWindowDuration);
        }

        if (ability.abilityType == AbilityType.TwoStageHoldCharge && ability.chargeStage == 2)
            return Mathf.Clamp01(ability.timer / Mathf.Max(0.01f, ability.secondStageDuration));

        return 0f;
    }

    private float GetCooldownFill(AbilityBinding ability)
    {
        if (ability == null)
            return 0f;

        float runtimeCooldownDuration = ability.cooldownDuration;

        if (ability.inputBindingMode == InputBindingMode.ASCIIRewindChargeControllerAction &&
            ability.boundActionType == BoundActionType.RRewindMode &&
            asciiRewindChargeController != null)
        {
            runtimeCooldownDuration = asciiRewindChargeController.cooldownAfterSuccess;
        }

        if (runtimeCooldownDuration <= 0.0001f)
            return 0f;

        return Mathf.Clamp01(ability.cooldownTimer / runtimeCooldownDuration);
    }

    public bool TriggerBoundAbility(int abilityIndex)
    {
        if (abilityIndex < 0 || abilityIndex >= abilities.Count)
            return false;

        AbilityBinding ability = abilities[abilityIndex];
        if (ability == null || !ability.unlocked)
            return false;

        switch (ability.inputBindingMode)
        {
            case InputBindingMode.ASCIIWorldModeManagerAction:
                if (asciiWorldModeManager == null)
                    return false;
                switch (ability.boundActionType)
                {
                    case BoundActionType.QScanMode: return asciiWorldModeManager.TryTriggerRevertNearbyVirtualAction();
                    case BoundActionType.EProjectionMode: return asciiWorldModeManager.TryToggleProjectionModeAction();
                    case BoundActionType.MouseRightAimMode: asciiWorldModeManager.SetExternalAimInputPressed(true); return true;
                }
                return false;

            case InputBindingMode.ASCIIRewindChargeControllerAction:
                if (asciiRewindChargeController == null)
                    return false;
                return asciiRewindChargeController.TryBeginChargeAction();
        }

        return false;
    }

    public bool SetBoundAbilityHeld(int abilityIndex, bool pressed)
    {
        if (abilityIndex < 0 || abilityIndex >= abilities.Count)
            return false;

        AbilityBinding ability = abilities[abilityIndex];
        if (ability == null || !ability.unlocked)
            return false;

        switch (ability.inputBindingMode)
        {
            case InputBindingMode.ASCIIWorldModeManagerAction:
                if (asciiWorldModeManager == null)
                    return false;

                if (ability.boundActionType == BoundActionType.MouseRightAimMode)
                {
                    asciiWorldModeManager.SetExternalAimInputPressed(pressed);
                    return true;
                }

                if (pressed)
                    return TriggerBoundAbility(abilityIndex);

                return false;

            case InputBindingMode.ASCIIRewindChargeControllerAction:
                if (asciiRewindChargeController == null)
                    return false;

                asciiRewindChargeController.SetExternalChargeHeld(pressed);
                if (pressed)
                    return asciiRewindChargeController.TryBeginChargeAction();

                return true;
        }

        return false;
    }

    [ContextMenu("Reset All")]
    public void ResetPreview()
    {
        ResetAllAbilities();
    }
}