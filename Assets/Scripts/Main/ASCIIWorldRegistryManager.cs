using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Game/ASCII World Registry Manager")]
public class ASCIIWorldRegistryManager : MonoBehaviour
{
    [Header("Layer")]
    public LayerMask solidLayer;
    public LayerMask virtualLayer;
    public LayerMask projectionLayer;

    [Header("Virtual 数量规则")]
    [Min(1)] public int maxActiveVirtualCount = 2;

    [Header("回退后的重合销毁")]
    [Tooltip("当运行时最旧 Virtual 因超出上限而回退为 Solid 时，若当前位置与其他碰撞体重合，则直接销毁该物体。")]
    public bool destroyVirtualObjectIfRevertedSolidOverlaps = true;

    [Tooltip("用于判定“回退为 Solid 后是否与其他碰撞体重合”的 LayerMask。")]
    public LayerMask solidRevertDestroyOverlapMask = ~0;

    [Tooltip("回退为 Solid 后的重合销毁判定是否把 Trigger 也算进去。")]
    public bool includeTriggerCollidersInSolidRevertDestroyCheck = false;

    [Tooltip("Virtual 回退为 Solid 后，ComputePenetration 返回的穿透深度至少达到该值才视为真正需要销毁。")]
    [Min(0f)] public float solidRevertDestroyPenetrationThreshold = 0.03f;

    [Tooltip("若启用相对阈值，则 Virtual 回退销毁穿透阈值 = 当前 Collider 最小世界轴尺寸 * 该比例，与固定阈值取较大值。")]
    [Range(0f, 0.5f)] public float solidRevertDestroyPenetrationThresholdRatio = 0.06f;

    [Header("场景初始化")]
    [Tooltip("若场景对象初始就在 Virtual Layer，上线时是否强制按 Solid 处理。")]
    public bool forceSceneVirtualLayerToSolid = true;

    [Tooltip("带有该 Tag 的对象若初始位于 Virtual Layer，则不会因 forceSceneVirtualLayerToSolid 而回退为 Solid。")]
    public string persistentVirtualTag = "PersistentVirtual";

    [Tooltip("运行时自动重新分配全场唯一 ID。")]
    public bool autoAssignUniqueIdsOnAwake = true;

    [SerializeField] private List<ASCIIWorldObject> activeObjects = new List<ASCIIWorldObject>();
    [SerializeField] private List<ASCIIWorldObject> runtimeVirtualObjects = new List<ASCIIWorldObject>();
    [SerializeField] private List<ASCIIWorldObject> sceneProjectionObjects = new List<ASCIIWorldObject>();

    private readonly Dictionary<int, ASCIIWorldObject> objectById = new Dictionary<int, ASCIIWorldObject>();
    private readonly HashSet<ASCIIWorldObject> runtimeOriginObjects = new HashSet<ASCIIWorldObject>();

    private int solidLayerIndex = -1;
    private int virtualLayerIndex = -1;
    private int projectionLayerIndex = -1;

    public IReadOnlyList<ASCIIWorldObject> ActiveObjects => activeObjects;
    public IReadOnlyList<ASCIIWorldObject> RuntimeVirtualObjects => runtimeVirtualObjects;
    public IReadOnlyList<ASCIIWorldObject> SceneProjectionObjects => sceneProjectionObjects;

    private void Awake()
    {
        ResolveLayers();
        InitializeSceneObjects();
    }

    private void OnValidate()
    {
        ResolveLayers();

        if (maxActiveVirtualCount < 1)
            maxActiveVirtualCount = 1;
    }

    public void InitializeSceneObjects()
    {
        activeObjects.Clear();
        runtimeVirtualObjects.Clear();
        sceneProjectionObjects.Clear();
        objectById.Clear();
        runtimeOriginObjects.Clear();

        ASCIIWorldObject[] all = FindObjectsOfType<ASCIIWorldObject>(true);

        if (autoAssignUniqueIdsOnAwake)
            AssignUniqueIds(all);

        for (int i = 0; i < all.Length; i++)
        {
            ASCIIWorldObject obj = all[i];
            if (obj == null)
                continue;

            RegisterObjectInternal(obj);

            ASCIIWorldObject.RuntimeState initialState = GetSceneInitialStateFromLayer(obj, obj.gameObject.layer);
            SetObjectState(obj, initialState);

            if (initialState == ASCIIWorldObject.RuntimeState.Projection)
            {
                if (!sceneProjectionObjects.Contains(obj))
                    sceneProjectionObjects.Add(obj);
            }
        }

        CleanupNulls();
    }

    private void AssignUniqueIds(ASCIIWorldObject[] all)
    {
        int nextId = 1;

        for (int i = 0; i < all.Length; i++)
        {
            ASCIIWorldObject obj = all[i];
            if (obj == null)
                continue;

            obj.SetObjectIdFromManager(nextId);
            nextId++;
        }
    }

    public bool IsPersistentVirtualObject(ASCIIWorldObject obj)
    {
        return obj != null &&
               !string.IsNullOrEmpty(persistentVirtualTag) &&
               obj.CompareTag(persistentVirtualTag);
    }

    private ASCIIWorldObject.RuntimeState GetSceneInitialStateFromLayer(ASCIIWorldObject obj, int layer)
    {
        if (layer == projectionLayerIndex && projectionLayerIndex >= 0)
            return ASCIIWorldObject.RuntimeState.Projection;

        if (layer == virtualLayerIndex && virtualLayerIndex >= 0)
        {
            if (IsPersistentVirtualObject(obj))
                return ASCIIWorldObject.RuntimeState.Virtual;

            if (forceSceneVirtualLayerToSolid)
                return ASCIIWorldObject.RuntimeState.Solid;

            return ASCIIWorldObject.RuntimeState.Virtual;
        }

        return ASCIIWorldObject.RuntimeState.Solid;
    }

    private void RegisterObjectInternal(ASCIIWorldObject obj)
    {
        if (obj == null)
            return;

        int id = obj.ObjectId;
        objectById[id] = obj;

        if (!IsPersistentVirtualObject(obj))
        {
            if (!activeObjects.Contains(obj))
                activeObjects.Add(obj);
        }

        obj.ApplyObjectIdToRenderers();
    }

    public bool TryGetObjectById(int objectId, out ASCIIWorldObject worldObject)
    {
        return objectById.TryGetValue(objectId, out worldObject);
    }

    public bool IsRuntimeOriginObject(ASCIIWorldObject worldObject)
    {
        return worldObject != null && runtimeOriginObjects.Contains(worldObject);
    }

    public List<ASCIIWorldObject> GetRuntimeOriginObjects()
    {
        return new List<ASCIIWorldObject>(runtimeOriginObjects);
    }

    public void SetObjectState(ASCIIWorldObject worldObject, ASCIIWorldObject.RuntimeState state)
    {
        if (worldObject == null)
            return;

        worldObject.CurrentState = state;

        int targetLayer = worldObject.gameObject.layer;

        switch (state)
        {
            case ASCIIWorldObject.RuntimeState.Solid:
                if (solidLayerIndex >= 0) targetLayer = solidLayerIndex;
                break;

            case ASCIIWorldObject.RuntimeState.Virtual:
                if (virtualLayerIndex >= 0) targetLayer = virtualLayerIndex;
                break;

            case ASCIIWorldObject.RuntimeState.Projection:
                if (projectionLayerIndex >= 0) targetLayer = projectionLayerIndex;
                break;
        }

        Transform[] children = worldObject.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
            children[i].gameObject.layer = targetLayer;

        ApplyPhysicsState(worldObject, state);
        worldObject.ApplyObjectIdToRenderers();

        SyncListsForState(worldObject);
    }

    public void RestoreVirtualToSolidAndDestroyIfOverlapping(ASCIIWorldObject worldObject)
    {
        if (worldObject == null)
            return;

        SetObjectState(worldObject, ASCIIWorldObject.RuntimeState.Solid);

        if (ShouldDestroyAfterSolidRestore(worldObject))
            DestroyWorldObject(worldObject);
    }

    private void ApplyPhysicsState(ASCIIWorldObject worldObject, ASCIIWorldObject.RuntimeState state)
    {
        bool isSolid = state == ASCIIWorldObject.RuntimeState.Solid;

        Collider[] colliders = worldObject.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null)
                continue;

            col.isTrigger = !isSolid;
        }

        Rigidbody[] rigidbodies = worldObject.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody rb = rigidbodies[i];
            if (rb == null)
                continue;

            if (isSolid)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
            }
            else
            {
                if (!rb.isKinematic)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                rb.useGravity = false;
                rb.isKinematic = true;
            }
        }
    }

    public void SetDraggedPhysicsDisabled(ASCIIWorldObject worldObject, HashSet<Collider> draggedColliderSet)
    {
        if (worldObject == null)
            return;

        Collider[] colliders = worldObject.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null)
                continue;

            col.isTrigger = true;

            if (draggedColliderSet != null)
                draggedColliderSet.Add(col);
        }

        Rigidbody[] rigidbodies = worldObject.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody rb = rigidbodies[i];
            if (rb == null)
                continue;

            if (!rb.isKinematic)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            rb.useGravity = false;
            rb.isKinematic = true;
        }
    }

    public void RegisterAsVirtual(ASCIIWorldObject worldObject)
    {
        if (worldObject == null)
            return;

        RegisterObjectInternal(worldObject);

        runtimeOriginObjects.Add(worldObject);

        runtimeVirtualObjects.Remove(worldObject);
        runtimeVirtualObjects.Add(worldObject);

        sceneProjectionObjects.Remove(worldObject);

        SetObjectState(worldObject, ASCIIWorldObject.RuntimeState.Virtual);
        EnforceVirtualCountLimit();
    }

    // 给“回溯第2秒生成的预览副本”用：统一按运行时来源 Virtual 注册
    public void RegisterRewindClone(ASCIIWorldObject worldObject, ASCIIWorldObject.RuntimeState previewState)
    {
        if (worldObject == null)
            return;

        RegisterObjectInternal(worldObject);
        runtimeOriginObjects.Add(worldObject);
        sceneProjectionObjects.Remove(worldObject);

        SetObjectState(worldObject, previewState);
    }

    // 给“第3秒正式回溯留下来的对象”用：恢复成初始状态，并视为当前 live 对象
    public void RegisterRuntimeReconstructedObject(ASCIIWorldObject worldObject, ASCIIWorldObject.RuntimeState finalState)
    {
        if (worldObject == null)
            return;

        RegisterObjectInternal(worldObject);

        // 回溯重建出来的对象，本质上属于运行时对象
        runtimeOriginObjects.Add(worldObject);

        SetObjectState(worldObject, finalState);

        // 若最终状态是 Projection，不要把它当 sceneProjectionObjects 维护
        // 因为它不是开场场景 Projection，而是运行时回溯生成的 live 对象
        sceneProjectionObjects.Remove(worldObject);
    }

    // 给回溯提交阶段用：若回到初始位置会被 solidRevertDestroyOverlapMask 阻挡，则放弃恢复
    public bool WouldBeBlockedBySolidRevertDestroyMask(ASCIIWorldObject worldObject)
    {
        if (worldObject == null)
            return false;

        int overlapMask = solidRevertDestroyOverlapMask.value;
        if (overlapMask == 0)
            return false;

        QueryTriggerInteraction triggerInteraction = includeTriggerCollidersInSolidRevertDestroyCheck
            ? QueryTriggerInteraction.Collide
            : QueryTriggerInteraction.Ignore;

        Collider[] ownColliders = worldObject.GetComponentsInChildren<Collider>(true);
        Collider[] overlapBuffer = new Collider[128];

        for (int i = 0; i < ownColliders.Length; i++)
        {
            Collider own = ownColliders[i];
            if (own == null || !own.enabled)
                continue;

            Bounds bounds = own.bounds;
            if (bounds.size.sqrMagnitude <= 0f)
                continue;

            int count = Physics.OverlapBoxNonAlloc(
                bounds.center,
                bounds.extents,
                overlapBuffer,
                own.transform.rotation,
                overlapMask,
                triggerInteraction
            );

            for (int c = 0; c < count; c++)
            {
                Collider other = overlapBuffer[c];
                overlapBuffer[c] = null;

                if (other == null || other == own)
                    continue;

                if (other.transform != null && other.transform.IsChildOf(worldObject.transform))
                    continue;

                if (!TryEvaluateColliderOverlapForSolidRestoreDestroy(own, other))
                    continue;

                return true;
            }
        }

        return false;
    }

    private void EnforceVirtualCountLimit()
    {
        while (runtimeVirtualObjects.Count > maxActiveVirtualCount)
        {
            ASCIIWorldObject oldest = runtimeVirtualObjects[0];
            runtimeVirtualObjects.RemoveAt(0);

            if (oldest == null || oldest.CurrentState != ASCIIWorldObject.RuntimeState.Virtual)
                continue;

            RestoreVirtualToSolidAndDestroyIfOverlapping(oldest);
        }
    }

    private bool ShouldDestroyAfterSolidRestore(ASCIIWorldObject worldObject)
    {
        if (!destroyVirtualObjectIfRevertedSolidOverlaps || worldObject == null)
            return false;

        int overlapMask = solidRevertDestroyOverlapMask.value;
        if (overlapMask == 0)
            return false;

        QueryTriggerInteraction triggerInteraction = includeTriggerCollidersInSolidRevertDestroyCheck
            ? QueryTriggerInteraction.Collide
            : QueryTriggerInteraction.Ignore;

        Collider[] ownColliders = worldObject.GetComponentsInChildren<Collider>(true);
        Collider[] overlapBuffer = new Collider[128];

        for (int i = 0; i < ownColliders.Length; i++)
        {
            Collider own = ownColliders[i];
            if (own == null || !own.enabled)
                continue;

            Bounds bounds = own.bounds;
            if (bounds.size.sqrMagnitude <= 0f)
                continue;

            int count = Physics.OverlapBoxNonAlloc(
                bounds.center,
                bounds.extents,
                overlapBuffer,
                own.transform.rotation,
                overlapMask,
                triggerInteraction
            );

            for (int c = 0; c < count; c++)
            {
                Collider other = overlapBuffer[c];
                overlapBuffer[c] = null;

                if (other == null || other == own)
                    continue;

                if (other.transform != null && other.transform.IsChildOf(worldObject.transform))
                    continue;

                if (!TryEvaluateColliderOverlapForSolidRestoreDestroy(own, other))
                    continue;

                return true;
            }
        }

        return false;
    }

    private bool TryEvaluateColliderOverlapForSolidRestoreDestroy(Collider own, Collider other)
    {
        if (own == null || other == null)
            return false;

        bool overlaps = Physics.ComputePenetration(
            own, own.transform.position, own.transform.rotation,
            other, other.transform.position, other.transform.rotation,
            out _, out float distance
        );

        if (!overlaps)
            return false;

        float requiredThreshold = GetPenetrationThresholdForCollider(
            own,
            solidRevertDestroyPenetrationThreshold,
            solidRevertDestroyPenetrationThresholdRatio);

        return distance >= requiredThreshold;
    }

    private float GetPenetrationThresholdForCollider(Collider own, float fixedThreshold, float ratioThreshold)
    {
        float clampedFixedThreshold = Mathf.Max(0f, fixedThreshold);
        float clampedRatio = Mathf.Max(0f, ratioThreshold);

        Bounds bounds = own != null ? own.bounds : default;
        Vector3 size = bounds.size;
        float minAxis = Mathf.Min(size.x, Mathf.Min(size.y, size.z));

        if (minAxis <= 0f)
            return clampedFixedThreshold;

        float scaledThreshold = minAxis * clampedRatio;
        return Mathf.Max(clampedFixedThreshold, scaledThreshold);
    }

    public void DestroyWorldObject(ASCIIWorldObject worldObject)
    {
        if (worldObject == null)
            return;

        activeObjects.Remove(worldObject);
        runtimeVirtualObjects.Remove(worldObject);
        sceneProjectionObjects.Remove(worldObject);
        runtimeOriginObjects.Remove(worldObject);
        objectById.Remove(worldObject.ObjectId);

        Object.Destroy(worldObject.gameObject);
    }

    private void SyncListsForState(ASCIIWorldObject worldObject)
    {
        if (worldObject == null)
            return;

        runtimeVirtualObjects.Remove(worldObject);
        sceneProjectionObjects.Remove(worldObject);

        bool isRuntimeOrigin = runtimeOriginObjects.Contains(worldObject);

        if (worldObject.CurrentState == ASCIIWorldObject.RuntimeState.Virtual)
        {
            if (isRuntimeOrigin)
                runtimeVirtualObjects.Add(worldObject);
        }
        else if (worldObject.CurrentState == ASCIIWorldObject.RuntimeState.Projection)
        {
            if (!isRuntimeOrigin)
                sceneProjectionObjects.Add(worldObject);
        }
    }

    public void CleanupNulls()
    {
        for (int i = activeObjects.Count - 1; i >= 0; i--)
        {
            if (activeObjects[i] == null)
                activeObjects.RemoveAt(i);
        }

        for (int i = runtimeVirtualObjects.Count - 1; i >= 0; i--)
        {
            if (runtimeVirtualObjects[i] == null)
                runtimeVirtualObjects.RemoveAt(i);
        }

        for (int i = sceneProjectionObjects.Count - 1; i >= 0; i--)
        {
            if (sceneProjectionObjects[i] == null)
                sceneProjectionObjects.RemoveAt(i);
        }

        List<ASCIIWorldObject> removeList = new List<ASCIIWorldObject>();
        foreach (ASCIIWorldObject obj in runtimeOriginObjects)
        {
            if (obj == null)
                removeList.Add(obj);
        }

        for (int i = 0; i < removeList.Count; i++)
            runtimeOriginObjects.Remove(removeList[i]);
    }

    private void ResolveLayers()
    {
        solidLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(solidLayer);
        virtualLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(virtualLayer);
        projectionLayerIndex = LayerMaskSingleLayerUtility.ToLayerIndex(projectionLayer);
    }
}