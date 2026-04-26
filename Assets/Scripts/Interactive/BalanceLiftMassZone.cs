using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class BalanceLiftMassZone : MonoBehaviour
{
    [Header("检测层")]
    [Tooltip("只有这些 Layer 的 Rigidbody 才会被统计。")]
    [SerializeField] private LayerMask validLayers = ~0;

    [Header("调试")]
    [SerializeField] private bool logMassChanges = false;

    private readonly HashSet<Rigidbody> trackedBodies = new HashSet<Rigidbody>();
    private readonly List<Rigidbody> cleanupBuffer = new List<Rigidbody>();

    public float CurrentTotalMass { get; private set; }

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        if (!col.isTrigger)
        {
            Debug.LogWarning($"[BalanceLiftMassZone] {name} 的 Collider 不是 Trigger，建议勾选 Is Trigger。", this);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryAdd(other);
    }

    private void OnTriggerExit(Collider other)
    {
        TryRemove(other);
    }

    private void LateUpdate()
    {
        CleanupInvalidBodies();
    }

    private void TryAdd(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null)
            return;

        if ((validLayers.value & (1 << rb.gameObject.layer)) == 0)
            return;

        if (trackedBodies.Add(rb))
        {
            RecalculateMass();

            if (logMassChanges)
            {
                Debug.Log(
                    $"[BalanceLiftMassZone] {name} 添加 {rb.name}，当前总质量 = {CurrentTotalMass:F2}",
                    this
                );
            }
        }
    }

    private void TryRemove(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null)
            return;

        if (trackedBodies.Remove(rb))
        {
            RecalculateMass();

            if (logMassChanges)
            {
                Debug.Log(
                    $"[BalanceLiftMassZone] {name} 移除 {rb.name}，当前总质量 = {CurrentTotalMass:F2}",
                    this
                );
            }
        }
    }

    private void CleanupInvalidBodies()
    {
        if (trackedBodies.Count == 0)
            return;

        cleanupBuffer.Clear();

        foreach (Rigidbody rb in trackedBodies)
        {
            if (rb == null)
            {
                cleanupBuffer.Add(null);
                continue;
            }

            if ((validLayers.value & (1 << rb.gameObject.layer)) == 0)
            {
                cleanupBuffer.Add(rb);
            }
        }

        if (cleanupBuffer.Count == 0)
            return;

        for (int i = 0; i < cleanupBuffer.Count; i++)
        {
            trackedBodies.Remove(cleanupBuffer[i]);
        }

        RecalculateMass();
    }

    private void RecalculateMass()
    {
        float total = 0f;

        foreach (Rigidbody rb in trackedBodies)
        {
            if (rb == null)
                continue;

            total += Mathf.Max(0f, rb.mass);
        }

        CurrentTotalMass = total;
    }
}