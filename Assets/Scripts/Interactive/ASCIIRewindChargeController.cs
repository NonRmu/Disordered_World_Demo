using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Game/ASCII Rewind Charge Controller")]
public class ASCIIRewindChargeController : MonoBehaviour
{
    [Header("引用")]
    public ASCIIWorldRegistryManager registryManager;

    [Header("输入")]
    public KeyCode rewindKey = KeyCode.R;

    [Header("阶段时长")]
    [Tooltip("阶段一持续时长：若始终没有合法触发区，持续到该时长后中止。")]
    [Min(0.01f)] public float invalidAreaAbortDelay = 1f;

    [Tooltip("阶段二持续时长：进入二阶段后显示预览，再经过这段时长后视为成功。")]
    [Min(0.01f)] public float previewSpawnTime = 1f;

    [Tooltip("成功提交后的冷却时长。")]
    [Min(0f)] public float cooldownAfterSuccess = 3f;

    [Header("运行时只读")]
    [SerializeField] private bool isCharging;
    [SerializeField] private float chargeTimer;
    [SerializeField] private float cooldownTimer;
    [SerializeField] private ASCIIRewindTriggerController currentValidTrigger;
    [SerializeField] private bool previewSpawned;

    private readonly List<ASCIIWorldObject> previewObjects = new List<ASCIIWorldObject>();
    private readonly List<ASCIIRewindTriggerController> allTriggers = new List<ASCIIRewindTriggerController>();
    private Transform previewRoot;
    private bool externalChargeHeld;
    private bool lastExternalChargeHeld;

    public bool IsCoolingDown => cooldownTimer > 0f;
    public bool IsCharging => isCharging;
    public float ChargeTimer => chargeTimer;
    public bool PreviewSpawned => previewSpawned;
    public ASCIIRewindTriggerController CurrentValidTrigger => currentValidTrigger;
    public float AbortThresholdTime => Mathf.Max(0.01f, invalidAreaAbortDelay);
    public float PreviewThresholdTime => AbortThresholdTime + Mathf.Max(0.01f, previewSpawnTime);

    public bool TryBeginChargeAction()
    {
        return TryBeginCharge();
    }

    public void SetExternalChargeHeld(bool pressed)
    {
        externalChargeHeld = pressed;
    }

    public void CancelChargeAction()
    {
        CancelCharge();
    }

    private void Awake()
    {
        RefreshTriggerCache();
    }

    private void Update()
    {
        if (cooldownTimer > 0f)
            cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.deltaTime);

        bool externalDown = externalChargeHeld && !lastExternalChargeHeld;
        lastExternalChargeHeld = externalChargeHeld;

        if (Input.GetKeyDown(rewindKey) || externalDown)
            TryBeginCharge();

        if (!isCharging)
            return;

        if (!Input.GetKey(rewindKey) && !externalChargeHeld)
        {
            CancelCharge();
            return;
        }

        currentValidTrigger = FindBestValidTrigger();
        chargeTimer += Time.deltaTime;

        float abortThreshold = AbortThresholdTime;
        float successThreshold = PreviewThresholdTime;

        if (currentValidTrigger == null && chargeTimer >= abortThreshold)
        {
            CancelCharge();
            return;
        }

        if (currentValidTrigger != null && chargeTimer >= abortThreshold && !previewSpawned)
            SpawnPreviewObjects();

        if (currentValidTrigger != null && chargeTimer >= successThreshold)
        {
            CommitCharge();
            return;
        }
    }

    public void RefreshTriggerCache()
    {
        allTriggers.Clear();
        ASCIIRewindTriggerController[] found = FindObjectsOfType<ASCIIRewindTriggerController>(true);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null)
                allTriggers.Add(found[i]);
        }
    }

    private bool TryBeginCharge()
    {
        if (registryManager == null)
            return false;

        if (IsCoolingDown)
            return false;

        if (isCharging)
            return false;

        isCharging = true;
        chargeTimer = 0f;
        previewSpawned = false;
        currentValidTrigger = FindBestValidTrigger();
        EnsurePreviewRoot();
        ClearPreviewObjectsImmediate();
        return true;
    }

    private ASCIIRewindTriggerController FindBestValidTrigger()
    {
        ASCIIRewindTriggerController best = null;
        float bestSqrDistance = float.MaxValue;
        Vector3 origin = transform.position;

        for (int i = 0; i < allTriggers.Count; i++)
        {
            ASCIIRewindTriggerController trigger = allTriggers[i];
            if (trigger == null)
                continue;

            if (!trigger.CanUseAsValidTrigger())
                continue;

            float sqrDistance = (trigger.transform.position - origin).sqrMagnitude;
            if (best == null || sqrDistance < bestSqrDistance)
            {
                best = trigger;
                bestSqrDistance = sqrDistance;
            }
        }

        return best;
    }

    private void EnsurePreviewRoot()
    {
        if (previewRoot != null)
            return;

        GameObject root = new GameObject("ASCIIRewindPreviewRoot");
        root.hideFlags = HideFlags.None;
        previewRoot = root.transform;
    }

    private void SpawnPreviewObjects()
    {
        if (currentValidTrigger == null)
            return;

        ClearPreviewObjectsImmediate();
        previewObjects.AddRange(currentValidTrigger.CreatePreviewObjects(previewRoot, disableGameplayScripts: true));
        previewSpawned = true;
    }

    private void CommitCharge()
    {
        if (currentValidTrigger == null)
        {
            CancelCharge();
            return;
        }

        currentValidTrigger.CommitRewindFromPreview(previewObjects);
        previewObjects.Clear();
        previewSpawned = false;
        isCharging = false;
        chargeTimer = 0f;
        cooldownTimer = cooldownAfterSuccess;
    }

    private void CancelCharge()
    {
        ClearPreviewObjectsImmediate();
        previewSpawned = false;
        isCharging = false;
        chargeTimer = 0f;
        currentValidTrigger = null;
    }

    private void ClearPreviewObjectsImmediate()
    {
        for (int i = 0; i < previewObjects.Count; i++)
        {
            ASCIIWorldObject preview = previewObjects[i];
            if (preview == null)
                continue;

            if (registryManager != null)
                registryManager.DestroyWorldObject(preview);
            else
                Destroy(preview.gameObject);
        }

        previewObjects.Clear();
    }
}
