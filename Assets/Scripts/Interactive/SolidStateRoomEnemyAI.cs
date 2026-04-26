using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Game/Enemy/Solid State Room Enemy AI")]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class SolidStateRoomEnemyAI : MonoBehaviour, IProjectionModePauseReceiver
{
    [Header("引用")]
    [Tooltip("玩家目标。为空时可由房间触发器在激活时传入。")]
    public Transform playerTarget;

    [Tooltip("敌人所属 ASCII 世界对象。若为空则自动在自身或父级查找。")]
    public ASCIIWorldObject asciiWorldObject;

    [Header("移动")]
    [Min(0f)] public float moveSpeed = 2.5f;
    [Min(0f)] public float acceleration = 12f;
    [Min(0f)] public float stoppingDistance = 1.0f;
    [Min(0f)] public float rotationSpeed = 12f;

    [Header("敌人间距")]
    [Tooltip("敌人之间的检测半径。")]
    [Min(0f)] public float separationRadius = 1.2f;

    [Tooltip("敌人之间保持距离的力度。")]
    [Min(0f)] public float separationStrength = 2.0f;

    [Tooltip("用于敌人间距搜索的 LayerMask。建议至少包含 Solid。")]
    public LayerMask separationSearchMask = ~0;

    [Header("高度过滤")]
    [Tooltip("只在水平面上追踪玩家；若高度差超过此值则停止。0 表示不限制。")]
    [Min(0f)] public float maxVerticalChaseDifference = 0f;

    [Header("调试只读")]
    [SerializeField] private bool activatedByRoom = false;
    [SerializeField] private bool projectionModePaused = false;
    [SerializeField] private float currentDistanceToPlayer = 0f;
    [SerializeField] private Vector3 currentVelocity = Vector3.zero;

    private Rigidbody rb;
    private CapsuleCollider capsule;
    private readonly Collider[] overlapBuffer = new Collider[32];

    public bool IsActivatedByRoom => activatedByRoom;
    public bool IsProjectionModePaused => projectionModePaused;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();

        if (asciiWorldObject == null)
            asciiWorldObject = GetComponentInParent<ASCIIWorldObject>();

        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void FixedUpdate()
    {
        if (rb == null)
            return;

        if (!CanMove())
        {
            StopImmediately();
            return;
        }

        if (rb.isKinematic)
        {
            currentVelocity = Vector3.zero;
            return;
        }

        Vector3 toPlayer = playerTarget.position - transform.position;

        if (maxVerticalChaseDifference > 0f && Mathf.Abs(toPlayer.y) > maxVerticalChaseDifference)
        {
            StopSmoothly();
            return;
        }

        Vector3 planarToPlayer = Vector3.ProjectOnPlane(toPlayer, Vector3.up);
        currentDistanceToPlayer = planarToPlayer.magnitude;

        if (currentDistanceToPlayer <= stoppingDistance)
        {
            StopSmoothly();
            FaceDirection(planarToPlayer);
            return;
        }

        Vector3 desiredMoveDir = planarToPlayer.normalized;
        Vector3 separationDir = ComputeSeparationDirection();

        Vector3 finalMoveDir = desiredMoveDir + separationDir * separationStrength;
        finalMoveDir = Vector3.ProjectOnPlane(finalMoveDir, Vector3.up);

        if (finalMoveDir.sqrMagnitude > 0.0001f)
            finalMoveDir.Normalize();
        else
            finalMoveDir = desiredMoveDir;

        Vector3 targetVelocity = finalMoveDir * moveSpeed;
        targetVelocity.y = rb.velocity.y;

        rb.velocity = Vector3.MoveTowards(rb.velocity, targetVelocity, acceleration * Time.fixedDeltaTime);
        currentVelocity = rb.velocity;

        FaceDirection(finalMoveDir);
    }

    private bool CanMove()
    {
        if (!activatedByRoom)
            return false;

        if (playerTarget == null)
            return false;

        if (projectionModePaused)
            return false;

        if (asciiWorldObject != null)
        {
            if (asciiWorldObject.CurrentState == ASCIIWorldObject.RuntimeState.Virtual ||
                asciiWorldObject.CurrentState == ASCIIWorldObject.RuntimeState.Projection)
            {
                return false;
            }
        }

        return true;
    }

    private Vector3 ComputeSeparationDirection()
    {
        if (capsule == null)
            return Vector3.zero;

        Vector3 center = capsule.bounds.center;

        LayerMask searchMask = separationSearchMask.value != 0
            ? separationSearchMask
            : ~0;

        int count = Physics.OverlapSphereNonAlloc(
            center,
            separationRadius,
            overlapBuffer,
            searchMask,
            QueryTriggerInteraction.Ignore);

        if (count <= 0)
            return Vector3.zero;

        Vector3 push = Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            Collider other = overlapBuffer[i];
            overlapBuffer[i] = null;

            if (other == null)
                continue;

            if (other.transform == transform || other.transform.IsChildOf(transform))
                continue;

            SolidStateRoomEnemyAI otherEnemy = other.GetComponentInParent<SolidStateRoomEnemyAI>();
            if (otherEnemy == null || otherEnemy == this)
                continue;

            if (!otherEnemy.isActiveAndEnabled)
                continue;

            Vector3 away = center - other.bounds.center;
            away.y = 0f;

            float dist = away.magnitude;
            if (dist <= 0.0001f)
            {
                away = transform.right;
                dist = 0.0001f;
            }

            float weight = 1f - Mathf.Clamp01(dist / separationRadius);
            push += away.normalized * weight;
        }

        return push;
    }

    private void FaceDirection(Vector3 moveDir)
    {
        if (rb == null)
            return;

        Vector3 planar = Vector3.ProjectOnPlane(moveDir, Vector3.up);
        if (planar.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRot = Quaternion.LookRotation(planar.normalized, Vector3.up);

        if (rb.isKinematic)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime);
            return;
        }

        Quaternion nextRot = Quaternion.Slerp(rb.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime);
        rb.MoveRotation(nextRot);
    }

    private void StopImmediately()
    {
        if (rb == null)
            return;

        if (rb.isKinematic)
        {
            currentVelocity = Vector3.zero;
            return;
        }

        Vector3 v = rb.velocity;
        v.x = 0f;
        v.z = 0f;
        rb.velocity = v;
        currentVelocity = rb.velocity;
    }

    private void StopSmoothly()
    {
        if (rb == null)
            return;

        if (rb.isKinematic)
        {
            currentVelocity = Vector3.zero;
            return;
        }

        Vector3 target = rb.velocity;
        target.x = 0f;
        target.z = 0f;
        rb.velocity = Vector3.MoveTowards(rb.velocity, target, acceleration * Time.fixedDeltaTime);
        currentVelocity = rb.velocity;
    }

    public void SetRoomActive(bool active, Transform target)
    {
        activatedByRoom = active;

        if (target != null)
            playerTarget = target;

        if (!activatedByRoom)
            StopImmediately();
    }

    public void SetProjectionModePaused(bool paused)
    {
        projectionModePaused = paused;

        if (paused)
            StopImmediately();
    }

    private void OnDisable()
    {
        StopImmediately();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, separationRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, stoppingDistance);
    }
#endif
}