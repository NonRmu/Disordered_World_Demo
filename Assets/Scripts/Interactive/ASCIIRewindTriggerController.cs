using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[AddComponentMenu("Game/ASCII Rewind Trigger Controller")]
public class ASCIIRewindTriggerController : MonoBehaviour
{
    [System.Serializable]
    public sealed class SnapshotEntry
    {
        public string snapshotKey;
        public string sourceName;
        public ASCIIWorldObject.RuntimeState initialState;
        public int initialLayer;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public Transform parentAtSnapshot;
        public GameObject sourcePrefab;
        public ASCIIWorldObject sourceObject;
    }

    //[Header("引用")]
    private ASCIIWorldRegistryManager registryManager;

    [Header("触发")]
    [Tooltip("可触发该区域回溯的玩家 Layer。")]
    public LayerMask playerLayerMask;

    [Header("调试")]
    public bool drawSnapshotGizmos = true;
    public Color snapshotGizmoColor = new Color(0.3f, 1f, 1f, 0.65f);
    public bool verboseLog = false;

    [SerializeField] private List<SnapshotEntry> initialSnapshots = new List<SnapshotEntry>();
    [SerializeField] private int playerInsideCount = 0;
    [SerializeField] private bool snapshotInitialized = false;

    private Collider triggerCollider;
    private Transform templateRoot;

    public bool HasInitializedSnapshot => snapshotInitialized;
    public bool IsPlayerInside => playerInsideCount > 0;
    public IReadOnlyList<SnapshotEntry> InitialSnapshots => initialSnapshots;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null)
            triggerCollider.isTrigger = true;

        registryManager = FindFirstObjectByType<ASCIIWorldRegistryManager>();

        EnsureTemplateRoot();
    }

    private IEnumerator Start()
    {
        yield return null;
        CaptureInitialSnapshots();
    }

    public void CaptureInitialSnapshots()
    {
        initialSnapshots.Clear();
        snapshotInitialized = false;
        ClearTemplateChildren();

        if (registryManager == null)
        {
            Debug.LogWarning($"[{nameof(ASCIIRewindTriggerController)}] {name} 未指定 registryManager。", this);
            return;
        }

        if (triggerCollider == null)
            triggerCollider = GetComponent<Collider>();

        if (triggerCollider == null)
        {
            Debug.LogWarning($"[{nameof(ASCIIRewindTriggerController)}] {name} 缺少 Trigger Collider。", this);
            return;
        }

        ASCIIWorldObject[] allWorldObjects = FindObjectsOfType<ASCIIWorldObject>(true);
        HashSet<ASCIIWorldObject> uniqueObjects = new HashSet<ASCIIWorldObject>();

        for (int i = 0; i < allWorldObjects.Length; i++)
        {
            ASCIIWorldObject obj = allWorldObjects[i];
            if (obj == null)
                continue;

            // 初始不记录隐藏物体
            if (!obj.gameObject.activeInHierarchy)
                continue;

            if (!IsRecordableState(obj.CurrentState))
                continue;

            if (!IsOnSupportedRuntimeLayer(obj.gameObject.layer))
                continue;

            if (!IntersectsTrigger(obj))
                continue;

            if (!uniqueObjects.Add(obj))
                continue;

            SnapshotEntry entry = BuildSnapshotEntry(obj);
            if (entry != null)
                initialSnapshots.Add(entry);
        }

        snapshotInitialized = true;

        if (verboseLog)
            Debug.Log($"[{nameof(ASCIIRewindTriggerController)}] {name} 初始快照记录完成，数量 = {initialSnapshots.Count}", this);
    }

    public bool CanUseAsValidTrigger()
    {
        return snapshotInitialized && initialSnapshots.Count > 0 && IsPlayerInside;
    }

    public bool TryGetCurrentLiveObject(SnapshotEntry snapshot, out ASCIIWorldObject liveObject)
    {
        liveObject = null;
        if (snapshot == null)
            return false;

        if (snapshot.sourceObject != null)
        {
            liveObject = snapshot.sourceObject;
            return true;
        }

        return false;
    }

    public List<ASCIIWorldObject> CreatePreviewObjects(Transform previewRoot, bool disableGameplayScripts)
    {
        List<ASCIIWorldObject> created = new List<ASCIIWorldObject>();

        if (!snapshotInitialized || previewRoot == null || registryManager == null)
            return created;

        for (int i = 0; i < initialSnapshots.Count; i++)
        {
            SnapshotEntry snapshot = initialSnapshots[i];
            ASCIIWorldObject clone = InstantiateSnapshotObject(snapshot, previewRoot, disableGameplayScripts);
            if (clone == null)
                continue;

            registryManager.RegisterRewindClone(clone, ASCIIWorldObject.RuntimeState.Virtual);
            created.Add(clone);
        }

        return created;
    }

    public void CommitRewindFromPreview(IReadOnlyList<ASCIIWorldObject> previewObjects)
    {
        if (registryManager == null || !snapshotInitialized)
            return;

        Dictionary<string, ASCIIWorldObject> previewByKey = new Dictionary<string, ASCIIWorldObject>();
        if (previewObjects != null)
        {
            for (int i = 0; i < previewObjects.Count; i++)
            {
                ASCIIWorldObject preview = previewObjects[i];
                if (preview == null)
                    continue;

                previewByKey[preview.name] = preview;
            }
        }

        // 先销毁“当前 live 对象”，并清空旧引用
        for (int i = 0; i < initialSnapshots.Count; i++)
        {
            SnapshotEntry snapshot = initialSnapshots[i];
            if (snapshot == null)
                continue;

            if (snapshot.sourceObject != null)
            {
                registryManager.DestroyWorldObject(snapshot.sourceObject);
                snapshot.sourceObject = null;
            }
        }

        // 再恢复新的对象，并把新的 live 对象写回 snapshot.sourceObject
        for (int i = 0; i < initialSnapshots.Count; i++)
        {
            SnapshotEntry snapshot = initialSnapshots[i];
            if (snapshot == null)
                continue;

            ASCIIWorldObject target = null;
            bool fromPreview = false;
            string previewName = BuildPreviewObjectName(snapshot);

            if (previewByKey.TryGetValue(previewName, out ASCIIWorldObject preview) && preview != null)
            {
                target = preview;
                fromPreview = true;
            }
            else
            {
                target = InstantiateSnapshotObject(snapshot, null, disableGameplayScripts: false);
            }

            if (target == null)
            {
                snapshot.sourceObject = null;
                continue;
            }

            RestoreSnapshotTransform(target.transform, snapshot);
            EnableGameplayScripts(target.gameObject);
            registryManager.RegisterRuntimeReconstructedObject(target, snapshot.initialState);

            // 若回溯落点被 solidRevertDestroyOverlapMask 阻挡，则该对象本次不恢复
            if (registryManager.WouldBeBlockedBySolidRevertDestroyMask(target))
            {
                registryManager.DestroyWorldObject(target);
                snapshot.sourceObject = null;

                if (verboseLog)
                {
                    Debug.Log(
                        $"[{nameof(ASCIIRewindTriggerController)}] {name} 回溯目标 {snapshot.sourceName} 在初始位置被 solidRevertDestroyOverlapMask 阻挡，已放弃恢复。",
                        this);
                }

                continue;
            }

            // 关键修复：记录这次成功恢复后的 live 对象
            snapshot.sourceObject = target;

            if (fromPreview)
                previewByKey.Remove(previewName);
        }
    }

    public string BuildPreviewObjectName(SnapshotEntry snapshot)
    {
        if (snapshot == null)
            return "RewindPreview_Null";

        return $"RewindPreview_{snapshot.snapshotKey}";
    }

    private SnapshotEntry BuildSnapshotEntry(ASCIIWorldObject obj)
    {
        if (obj == null)
            return null;

        SnapshotEntry entry = new SnapshotEntry();
        entry.snapshotKey = GenerateSnapshotKey(obj);
        entry.sourceName = obj.name;
        entry.initialState = obj.CurrentState;
        entry.initialLayer = obj.gameObject.layer;
        entry.localPosition = obj.transform.localPosition;
        entry.localRotation = obj.transform.localRotation;
        entry.localScale = obj.transform.localScale;
        entry.parentAtSnapshot = obj.transform.parent;
        entry.sourceObject = obj;
        entry.sourcePrefab = CreateTemplateClone(obj);
        return entry;
    }

    private ASCIIWorldObject InstantiateSnapshotObject(SnapshotEntry snapshot, Transform previewRoot, bool disableGameplayScripts)
    {
        if (snapshot == null)
            return null;

        // 关键修复：优先用初始模板，而不是当前 live 对象
        GameObject sourceGameObject = snapshot.sourcePrefab != null
            ? snapshot.sourcePrefab
            : (snapshot.sourceObject != null ? snapshot.sourceObject.gameObject : null);

        if (sourceGameObject == null)
            return null;

        Transform parent = previewRoot != null ? previewRoot : snapshot.parentAtSnapshot;
        GameObject cloneRoot = Instantiate(sourceGameObject, parent);
        cloneRoot.name = previewRoot != null ? BuildPreviewObjectName(snapshot) : snapshot.sourceName;
        cloneRoot.SetActive(true);

        Transform cloneTransform = cloneRoot.transform;
        RestoreSnapshotTransform(cloneTransform, snapshot);

        ASCIIWorldObject cloneWorldObject = cloneRoot.GetComponent<ASCIIWorldObject>();
        if (cloneWorldObject == null)
        {
            cloneWorldObject = cloneRoot.GetComponentInChildren<ASCIIWorldObject>(true);
            if (cloneWorldObject == null)
            {
                Destroy(cloneRoot);
                return null;
            }
        }

        if (disableGameplayScripts)
            DisableGameplayScripts(cloneRoot, cloneWorldObject);

        return cloneWorldObject;
    }

    private void RestoreSnapshotTransform(Transform target, SnapshotEntry snapshot)
    {
        if (target == null || snapshot == null)
            return;

        if (target.parent != snapshot.parentAtSnapshot)
            target.SetParent(snapshot.parentAtSnapshot, false);

        target.localPosition = snapshot.localPosition;
        target.localRotation = snapshot.localRotation;
        target.localScale = snapshot.localScale;
    }

    private void DisableGameplayScripts(GameObject root, ASCIIWorldObject keepWorldObject)
    {
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour mb = behaviours[i];
            if (mb == null)
                continue;

            if (mb == this)
                continue;

            if (mb == keepWorldObject)
                continue;

            mb.enabled = false;
        }
    }

    private void EnableGameplayScripts(GameObject root)
    {
        if (root == null)
            return;

        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour mb = behaviours[i];
            if (mb == null)
                continue;

            if (mb == this)
                continue;

            mb.enabled = true;
        }
    }

    private string GenerateSnapshotKey(ASCIIWorldObject obj)
    {
        string path = GetTransformPath(obj.transform);
        return $"{path}_{obj.ObjectId}";
    }

    private string GetTransformPath(Transform current)
    {
        if (current == null)
            return "null";

        string path = current.name;
        while (current.parent != null)
        {
            current = current.parent;
            path = current.name + "/" + path;
        }

        return path;
    }

    private bool IntersectsTrigger(ASCIIWorldObject obj)
    {
        if (triggerCollider == null || obj == null)
            return false;

        Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);
        if (colliders == null || colliders.Length == 0)
        {
            Renderer[] renderers = obj.CachedRenderers;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer rend = renderers[i];
                if (rend == null)
                    continue;

                if (triggerCollider.bounds.Intersects(rend.bounds))
                    return true;
            }

            return false;
        }

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null || !col.enabled)
                continue;

            if (triggerCollider.bounds.Intersects(col.bounds))
                return true;
        }

        return false;
    }

    private bool IsRecordableState(ASCIIWorldObject.RuntimeState state)
    {
        return state == ASCIIWorldObject.RuntimeState.Solid ||
               state == ASCIIWorldObject.RuntimeState.Virtual ||
               state == ASCIIWorldObject.RuntimeState.Projection;
    }

    private bool IsOnSupportedRuntimeLayer(int layer)
    {
        if (registryManager == null)
            return false;

        int solid = LayerMaskSingleLayerUtility.ToLayerIndex(registryManager.solidLayer);
        int virtualLayer = LayerMaskSingleLayerUtility.ToLayerIndex(registryManager.virtualLayer);
        int projection = LayerMaskSingleLayerUtility.ToLayerIndex(registryManager.projectionLayer);

        return layer == solid || layer == virtualLayer || layer == projection;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayerLayer(other.gameObject.layer))
            return;

        playerInsideCount++;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayerLayer(other.gameObject.layer))
            return;

        playerInsideCount = Mathf.Max(0, playerInsideCount - 1);
    }

    private bool IsPlayerLayer(int layer)
    {
        return (playerLayerMask.value & (1 << layer)) != 0;
    }

    private void EnsureTemplateRoot()
    {
        if (templateRoot != null)
            return;

        Transform existing = transform.Find("__RewindTemplates");
        if (existing != null)
        {
            templateRoot = existing;
            templateRoot.gameObject.SetActive(false);
            return;
        }

        GameObject root = new GameObject("__RewindTemplates");
        root.hideFlags = HideFlags.HideInHierarchy;
        root.transform.SetParent(transform, false);
        root.SetActive(false);
        templateRoot = root.transform;
    }

    private void ClearTemplateChildren()
    {
        EnsureTemplateRoot();
        if (templateRoot == null)
            return;

        for (int i = templateRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = templateRoot.GetChild(i);
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }

    private GameObject CreateTemplateClone(ASCIIWorldObject obj)
    {
        if (obj == null)
            return null;

        EnsureTemplateRoot();
        GameObject clone = Instantiate(obj.gameObject, templateRoot);
        clone.name = "Template_" + obj.name;
        clone.SetActive(false);
        return clone;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawSnapshotGizmos || !snapshotInitialized)
            return;

        Gizmos.color = snapshotGizmoColor;
        for (int i = 0; i < initialSnapshots.Count; i++)
        {
            SnapshotEntry snapshot = initialSnapshots[i];
            if (snapshot == null)
                continue;

            Transform parent = snapshot.parentAtSnapshot;
            Matrix4x4 matrix = parent != null ? parent.localToWorldMatrix : Matrix4x4.identity;
            Vector3 worldPos = matrix.MultiplyPoint3x4(snapshot.localPosition);
            Gizmos.DrawSphere(worldPos, 0.08f);
        }
    }
}