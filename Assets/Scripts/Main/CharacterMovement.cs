using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterMovement : MonoBehaviour
{
    [Header("引用")]
    public Camera cam;
    private Rigidbody rb;
    private Animator anim;
    private CapsuleCollider capsuleCollider;

    [Tooltip("世界模式管理器。")]
    public ASCIIWorldModeManager asciiWorldModeManager;

    [Header("移动")]
    public float playerSpeed = 5.0f;
    public float specialLocomotionSpeed = 3.0f;
    public Vector3 playerMoveDir;
    public float characterVelocity;

    [Header("地面检测")]
    public LayerMask groundMask = ~0;
    public bool onGround;
    [Tooltip("运行时地面检测射线的距离 只读。")]
    public float groundDistance;
    public float groundDistanceLimit = 0.35f;

    private Vector3 pointBottom, pointTop;
    private float capsuleRadius;

    [Header("投影视图")]
    [Tooltip("投影视图下是否允许角色移动。")]
    public bool allowMovementInProjectionView = true;

    [Tooltip("Aim / 投影模式下角色根节点朝向镜头方向的跟随速度。")]
    public float shoulderTurnSpeed = 20f;

    [Header("角色模型显示旋转")]
    [Tooltip("角色模型。为空时自动取 CharacterMovement 挂载对象的第一个子物体。")]
    public Transform characterVisual;
    public GameObject aimMachine;
    public GameObject projectionMachine;

    [Tooltip("Aim 模式下角色模型的局部欧拉角偏移。")]
    public Vector3 aimModelLocalEuler = new Vector3(0f, 20f, 0f);

    [Tooltip("投影模式下角色模型的局部欧拉角偏移。")]
    public Vector3 projectionModelLocalEuler = new Vector3(0f, 20f, 0f);

    [Tooltip("角色模型局部旋转插值速度。")]
    public float modelRotateLerpSpeed = 16f;

    [Tooltip("退出 Aim / 投影模式后，是否回到初始局部旋转；关闭则回到 Quaternion.identity。")]
    public bool restoreVisualToInitialLocalRotation = false;

    [Header("跳跃")]
    [Tooltip("起跳初速度。")]
    public float jumpForce = 7.5f;

    [Tooltip("基础重力。")]
    public float gravity = 20.0f;

    [Tooltip("下落时额外重力倍率，越大下落越利落。")]
    public float fallGravityMultiplier = 2.2f;

    [Tooltip("松开跳跃键后，上升阶段额外重力倍率，越大短跳越明显。")]
    public float lowJumpGravityMultiplier = 2.0f;

    [Tooltip("贴地时维持一个轻微向下速度，避免落地发飘。")]
    public float groundedStickForce = 2.0f;

    [Tooltip("最大下落速度。")]
    public float maxFallSpeed = 20.0f;
    [Tooltip("运行时y方向的速度 只读。")]
    public float verticalVelocity;

    [Header("土狼跳")]
    [Tooltip("是否启用土狼跳。开启后，角色离开地面后的短时间内仍可起跳。")]
    public bool enableCoyoteJump = true;

    [Tooltip("土狼跳时间窗口（秒）。建议 0.08 ~ 0.15。")]
    [Range(0f, 0.3f)]
    public float coyoteTime = 0.12f;

    [Tooltip("当前剩余土狼跳时间，仅调试查看。")]
    public float coyoteTimer;

    [Header("跳跃缓冲")]
    [Tooltip("是否启用跳跃缓冲。开启后，落地前短时间按下跳跃键，落地瞬间也会自动起跳。")]
    public bool enableJumpBuffer = true;

    [Tooltip("跳跃缓冲时间窗口（秒）。建议 0.08 ~ 0.15。")]
    [Range(0f, 0.3f)]
    public float jumpBufferTime = 0.12f;

    [Tooltip("当前剩余跳跃缓冲时间，仅调试查看。")]
    public float jumpBufferTimer;


    [Header("输入")]
    [Tooltip("同时输入相反的按键会维持先输入的方向")]
    public bool oppositeInputSustain = true;
    public float targetRight;
    public float targetForward;
    public float horInput, verInput;

    [Header("动画")]
    [Tooltip("动画参数平滑速度。")]
    public float animDampSpeed = 10f;

    [Tooltip("是否写入接地参数。")]
    public bool writeGroundedParam = true;

    [Tooltip("默认地面移动 BlendTree 开关参数名。")]
    public string useAimLocomotionParam = "UseAimLocomotion";
    public string useSkillLocomotionParam = "UseSkillLocomotion";

    [Tooltip("接地参数名。")]
    public string groundedParam = "IsGrounded";

    [Tooltip("竖直速度参数名。")]
    public string verticalSpeedParam = "VerticalSpeed";

    [Tooltip("地面移动速度参数名。")]
    public string speedParam = "Speed";

    [Tooltip("横向移动参数名。")]
    public string moveXParam = "MoveX";

    [Tooltip("纵向移动参数名。")]
    public string moveYParam = "MoveY";

    [Tooltip("起跳 Trigger 参数名。")]
    public string jumpTriggerParam = "Jump";

    [Tooltip("落地 Trigger 参数名。")]
    public string landTriggerParam = "Land";

    private float inputRight, inputForward;
    private float velocityRight, velocityForward;
    private float turnSmoothVelocity;

    private float animMoveX;
    private float animMoveY;
    private float animSpeed;

    private bool isAimMode;
    private bool isProjectionView;
    private bool useAimLocomotion;
    private bool useSkillLocomotion;
    private bool wasOnGround;

    private Quaternion initialVisualLocalRotation = Quaternion.identity;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        anim = GetComponentInChildren<Animator>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        capsuleRadius = capsuleCollider.radius;

        if (cam == null)
            cam = Camera.main;

        if (rb != null)
        {
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        if (characterVisual == null && transform.childCount > 0)
            characterVisual = transform.GetChild(0);

        if (characterVisual != null)
            initialVisualLocalRotation = characterVisual.localRotation;
    }

    private void Update()
    {
        UpdateModes();

        wasOnGround = onGround;
        UpdateGroundCheck();
        UpdateCoyoteTimer();
        UpdateJumpBufferTimer();

        UpdateInput();
        UpdateJumpAndGravity();

        Vector3 horizontalVelocity = rb.velocity;
        horizontalVelocity.y = 0f;
        characterVelocity = horizontalVelocity.magnitude;

        UpdateAnimStateParams();
        UpdateLandingTrigger();
        UpdateAnimMotionParams();
    }

    private void FixedUpdate()
    {
        UpdateMovement();

        if (rb != null)
            rb.velocity = playerMoveDir;
    }

    private void LateUpdate()
    {
        UpdateCharacterVisualRotation();
    }

    private void UpdateModes()
    {
        isProjectionView = asciiWorldModeManager != null && asciiWorldModeManager.IsProjectionMode;
        isAimMode = asciiWorldModeManager != null && asciiWorldModeManager.IsAimMode;

        useAimLocomotion = isAimMode;
        useSkillLocomotion = isProjectionView;

        if (aimMachine != null)
            aimMachine.SetActive(isAimMode && onGround);

        if (projectionMachine != null)
            projectionMachine.SetActive(isProjectionView && onGround);

    }

    private void UpdateGroundCheck()
    {
        Ray playerRay = new Ray(transform.position + Vector3.up * 0.2f, Vector3.down);

        if (Physics.Raycast(playerRay, out RaycastHit hit, 5f, groundMask, QueryTriggerInteraction.Ignore))
        {
            groundDistance = hit.distance;
            Debug.DrawLine(playerRay.origin, hit.point, Color.green);
        }
        else
        {
            groundDistance = 999f;
            Debug.DrawRay(playerRay.origin, Vector3.down * 2f, Color.red);
        }

        pointBottom = transform.position + transform.up * capsuleRadius - transform.up * 0.1f;
        pointTop = transform.position + transform.up * capsuleCollider.height - transform.up * capsuleRadius;

        bool hasGroundCollider = Physics.OverlapCapsule(
            pointBottom,
            pointTop,
            capsuleRadius,
            groundMask,
            QueryTriggerInteraction.Ignore
        ).Length > 0;

        onGround = hasGroundCollider && groundDistance < groundDistanceLimit;
    }

    private void UpdateCoyoteTimer()
    {
        if (!enableCoyoteJump)
        {
            coyoteTimer = 0f;
            return;
        }

        if (onGround)
        {
            coyoteTimer = coyoteTime;
        }
        else
        {
            coyoteTimer -= Time.deltaTime;
            if (coyoteTimer < 0f)
                coyoteTimer = 0f;
        }
    }

    private void UpdateJumpBufferTimer()
    {
        if (!enableJumpBuffer)
        {
            jumpBufferTimer = 0f;
            return;
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            jumpBufferTimer = jumpBufferTime;
        }
        else
        {
            jumpBufferTimer -= Time.deltaTime;
            if (jumpBufferTimer < 0f)
                jumpBufferTimer = 0f;
        }
    }

    private void UpdateInput()
    {
        bool lockMoveInProjection = isProjectionView && !allowMovementInProjectionView;

        if (lockMoveInProjection)
        {
            inputRight = 0f;
            inputForward = 0f;
            targetRight = Mathf.SmoothDamp(targetRight, 0f, ref velocityRight, 0.1f);
            targetForward = Mathf.SmoothDamp(targetForward, 0f, ref velocityForward, 0.1f);
        }
        else
        {
            float hor = oppositeInputSustain ? Input.GetAxis("Horizontal") : Input.GetAxisRaw("Horizontal");
            float ver = oppositeInputSustain ? Input.GetAxis("Vertical") : Input.GetAxisRaw("Vertical");

            float inputX = hor > 0 ? Mathf.Ceil(hor) : Mathf.Floor(hor);
            float inputZ = ver > 0 ? Mathf.Ceil(ver) : Mathf.Floor(ver);

            inputRight = (Input.GetKey(KeyCode.D) && Input.GetKey(KeyCode.A))
                ? inputX
                : ((Input.GetKey(KeyCode.D) ? 1.0f : 0f) - (Input.GetKey(KeyCode.A) ? 1.0f : 0f));

            inputForward = (Input.GetKey(KeyCode.W) && Input.GetKey(KeyCode.S))
                ? inputZ
                : ((Input.GetKey(KeyCode.W) ? 1.0f : 0f) - (Input.GetKey(KeyCode.S) ? 1.0f : 0f));

            targetRight = Mathf.SmoothDamp(targetRight, inputRight, ref velocityRight, 0.1f);
            targetForward = Mathf.SmoothDamp(targetForward, inputForward, ref velocityForward, 0.1f);
        }

        horInput = targetRight * Mathf.Sqrt(1f - targetForward * targetForward * 0.5f);
        verInput = targetForward * Mathf.Sqrt(1f - targetRight * targetRight * 0.5f);
    }

    private void UpdateJumpAndGravity()
    {
        bool canUseCoyoteJump = enableCoyoteJump && coyoteTimer > 0f;
        bool hasBufferedJump = enableJumpBuffer ? jumpBufferTimer > 0f : Input.GetKeyDown(KeyCode.Space);
        bool canJumpNow = onGround || canUseCoyoteJump;

        if (onGround)
        {
            if (verticalVelocity < 0f)
                verticalVelocity = -groundedStickForce;
        }
        else
        {
            float currentGravity = gravity;

            if (verticalVelocity < 0f)
            {
                currentGravity *= fallGravityMultiplier;
            }
            else if (verticalVelocity > 0f && !Input.GetKey(KeyCode.Space))
            {
                currentGravity *= lowJumpGravityMultiplier;
            }

            verticalVelocity -= currentGravity * Time.deltaTime;
            verticalVelocity = Mathf.Max(verticalVelocity, -maxFallSpeed);
        }

        if (hasBufferedJump && canJumpNow)
        {
            verticalVelocity = jumpForce;

            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
            onGround = false;

            TriggerJump();
        }
    }

    private void UpdateMovement()
    {
        if (cam == null)
        {
            playerMoveDir = new Vector3(0.0f, verticalVelocity, 0.0f);
            return;
        }

        bool hasMoveInput = Mathf.Abs(horInput) > 0.001f || Mathf.Abs(verInput) > 0.001f;

        Vector3 camForward = cam.transform.forward;
        Vector3 camRight = cam.transform.right;

        camForward.y = 0f;
        camRight.y = 0f;

        if (camForward.sqrMagnitude > 0.000001f) camForward.Normalize();
        if (camRight.sqrMagnitude > 0.000001f) camRight.Normalize();

        Vector3 moveDir = camRight * horInput + camForward * verInput;

        if (moveDir.sqrMagnitude > 0.000001f)
            moveDir.Normalize();

        float inputMagnitude = Mathf.Clamp01(new Vector2(horInput, verInput).magnitude);

        if ((isAimMode || isProjectionView) && camForward.sqrMagnitude > 0.000001f)
        {
            float targetAngle = Mathf.Atan2(camForward.x, camForward.z) * Mathf.Rad2Deg;
            float smoothTime = 1f / Mathf.Max(shoulderTurnSpeed, 0.0001f);
            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, smoothTime);
            transform.rotation = Quaternion.Euler(0.0f, angle, 0.0f);
        }
        else if (hasMoveInput)
        {
            float targetAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, 0.1f);
            transform.rotation = Quaternion.Euler(0.0f, angle, 0.0f);
        }

        if (hasMoveInput)
        {
            playerMoveDir = ((isAimMode || isProjectionView) ? specialLocomotionSpeed : playerSpeed) * inputMagnitude * new Vector3(moveDir.x, 0.0f, moveDir.z)
                          + new Vector3(0.0f, verticalVelocity, 0.0f);
        }
        else
        {
            playerMoveDir = new Vector3(0.0f, verticalVelocity, 0.0f);
        }
    }

    private void UpdateCharacterVisualRotation()
    {
        if (characterVisual == null)
            return;

        Quaternion targetLocalRotation;

        if (isProjectionView)
        {
            targetLocalRotation = Quaternion.Euler(projectionModelLocalEuler);
        }
        else if (isAimMode)
        {
            targetLocalRotation = Quaternion.Euler(aimModelLocalEuler);
        }
        else
        {
            targetLocalRotation = restoreVisualToInitialLocalRotation
                ? initialVisualLocalRotation
                : Quaternion.identity;
        }

        float t = 1f - Mathf.Exp(-Mathf.Max(modelRotateLerpSpeed, 0.0001f) * Time.deltaTime);
        characterVisual.localRotation = Quaternion.Slerp(characterVisual.localRotation, targetLocalRotation, t);
    }

    private void UpdateAnimStateParams()
    {
        if (anim == null)
            return;

        anim.SetBool(useAimLocomotionParam, useAimLocomotion);
        anim.SetBool(useSkillLocomotionParam, useSkillLocomotion);

        if (writeGroundedParam)
            anim.SetBool(groundedParam, onGround);

        anim.SetFloat(verticalSpeedParam, verticalVelocity);
    }

    private void UpdateLandingTrigger()
    {
        if (anim == null)
            return;

        if (!wasOnGround && onGround)
        {
            anim.ResetTrigger(jumpTriggerParam);
            anim.SetTrigger(landTriggerParam);
        }
    }

    private void TriggerJump()
    {
        if (anim == null)
            return;

        anim.ResetTrigger(landTriggerParam);
        anim.SetTrigger(jumpTriggerParam);
    }

    private void UpdateAnimMotionParams()
    {
        if (anim == null)
            return;

        float inputMagnitude = Mathf.Clamp01(new Vector2(horInput, verInput).magnitude);
        float targetSpeed = onGround ? inputMagnitude : 0f;

        float targetMoveX;
        float targetMoveY;

        if (useAimLocomotion || useSkillLocomotion)
        {
            targetMoveX = horInput;
            targetMoveY = verInput;
        }
        else
        {
            targetMoveX = 0f;
            targetMoveY = targetSpeed;
        }

        float t = 1f - Mathf.Exp(-animDampSpeed * Time.deltaTime);

        animMoveX = Mathf.Lerp(animMoveX, targetMoveX, t);
        animMoveY = Mathf.Lerp(animMoveY, targetMoveY, t);
        animSpeed = Mathf.Lerp(animSpeed, targetSpeed, t);

        if (Mathf.Abs(animMoveX) < 0.001f) animMoveX = 0f;
        if (Mathf.Abs(animMoveY) < 0.001f) animMoveY = 0f;
        if (Mathf.Abs(animSpeed) < 0.001f) animSpeed = 0f;

        anim.SetFloat(moveXParam, animMoveX);
        anim.SetFloat(moveYParam, animMoveY);
        anim.SetFloat(speedParam, animSpeed);
    }
}