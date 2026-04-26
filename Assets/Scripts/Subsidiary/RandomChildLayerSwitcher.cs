using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class RandomChildLayerSwitcher : MonoBehaviour
{
    [Serializable]
    public class LayerSwitchEntry
    {
        [Tooltip("目标 Layer 名称，必须已在 Tags and Layers 中存在。")]
        public string layerName = "Default";

        [Tooltip("该 Layer 的随机权重。长期统计下，权重越大，被选中的概率越高。")]
        [Min(0f)]
        public float weight = 1f;

        [Tooltip("切换到该 Layer 时，是否将 Collider.isTrigger 设为 true。")]
        public bool setIsTrigger = false;
    }

    public enum SwitchMode
    {
        [Tooltip("每个子物体独立计时，到点后各自切换。")]
        IndependentPerChild,

        [Tooltip("按全局随机间隔触发，每次只随机挑一个子物体切换。")]
        GlobalOneChildPerTick
    }

    public enum ColliderApplyMode
    {
        [Tooltip("只修改子物体自身 GameObject 上的 Collider。")]
        SelfOnly,

        [Tooltip("修改该子物体及其所有后代上的 Collider。")]
        SelfAndChildren
    }

    [Header("总开关")]
    [Tooltip("是否启用随机切换。关闭后会将所有子物体恢复到初始 Layer，并将 isTrigger 设为 false。")]
    public bool enableRandomSwitching = true;

    [Header("候选 Layer（可多选）")]
    [Tooltip("从这里勾选允许被切换到的候选 Layer。")]
    public LayerMask candidateLayersMask = 0;
    public GameObject labyrinthCeiling;
    public GameObject targetCube;
    public GameObject playerModel;

    [Header("每个 Layer 的配置")]
    [Tooltip("给候选 Layer 配置权重和切换后 Collider.isTrigger 状态。未配置的 Layer 会走默认配置。")]
    public List<LayerSwitchEntry> layerSettings = new List<LayerSwitchEntry>();

    [Tooltip("未单独配置的 Layer，使用这个默认权重。")]
    [Min(0f)]
    public float defaultWeight = 1f;

    [Tooltip("未单独配置的 Layer，切换到它时使用这个默认 isTrigger 设置。")]
    public bool defaultIsTrigger = false;

    [Header("Collider 应用范围")]
    [Tooltip("决定切换 Layer 时，isTrigger 应应用到哪里。")]
    public ColliderApplyMode colliderApplyMode = ColliderApplyMode.SelfOnly;

    [Header("目标子物体范围")]
    [Tooltip("是否包含所有后代子物体。关闭时只处理直接子物体。")]
    public bool includeAllDescendants = false;

    [Tooltip("是否包含未激活的子物体。")]
    public bool includeInactiveChildren = false;

    [Header("切换模式")]
    public SwitchMode switchMode = SwitchMode.IndependentPerChild;

    [Header("切换时间范围（秒）")]
    [Min(0f)]
    public float minInterval = 0.5f;

    [Min(0f)]
    public float maxInterval = 2.0f;

    [Header("行为选项")]
    [Tooltip("是否允许切换到与当前相同的 Layer。关闭后会尽量切到不同 Layer。")]
    public bool allowSameLayer = false;

    [Tooltip("Start 时是否立即刷新一次子物体列表。")]
    public bool refreshChildrenOnStart = true;

    [Tooltip("运行时如果子物体结构变化，是否自动定时刷新子物体列表。")]
    public bool autoRefreshChildren = false;

    [Min(0.1f)]
    public float autoRefreshInterval = 1.0f;

    [Header("调试")]
    [Tooltip("切换 Layer 时是否输出调试日志。")]
    public bool logSwitchResult = false;

    private struct LayerRuntimeData
    {
        public int layerIndex;
        public float weight;
        public bool setIsTrigger;
    }

    private readonly List<Transform> _children = new List<Transform>();
    private readonly Dictionary<Transform, float> _nextSwitchTimePerChild = new Dictionary<Transform, float>();
    private readonly List<LayerRuntimeData> _candidateRuntimeData = new List<LayerRuntimeData>();

    private readonly List<Collider> _tempColliders = new List<Collider>(16);
    private readonly List<Transform> _tempValidChildren = new List<Transform>(64);

    // 新增：记录子物体初始 Layer
    private readonly Dictionary<Transform, int> _initialLayerMap = new Dictionary<Transform, int>();

    private float _nextGlobalSwitchTime = 0f;
    private float _nextAutoRefreshTime = 0f;

    // 新增：用于检测 enableRandomSwitching 运行时切换
    private bool _lastEnableRandomSwitching = true;
    private bool _initialized = false;

    private void Start()
    {
        RebuildCandidateLayerCache();

        if (refreshChildrenOnStart)
        {
            RefreshChildren();
        }

        ResetAllTimers();

        _lastEnableRandomSwitching = enableRandomSwitching;
        _initialized = true;

        if (!enableRandomSwitching)
        {
            RestoreAllChildrenToInitialState();
        }
    }

    private void OnValidate()
    {
        if (maxInterval < minInterval)
        {
            maxInterval = minInterval;
        }

        if (autoRefreshInterval < 0.1f)
        {
            autoRefreshInterval = 0.1f;
        }

        if (defaultWeight < 0f)
        {
            defaultWeight = 0f;
        }

        if (layerSettings != null)
        {
            for (int i = 0; i < layerSettings.Count; i++)
            {
                LayerSwitchEntry entry = layerSettings[i];
                if (entry == null)
                    continue;

                if (entry.weight < 0f)
                {
                    entry.weight = 0f;
                }
            }
        }

        RebuildCandidateLayerCache();
    }

    private void Update()
    {
        if (!_initialized)
            return;

        if (_lastEnableRandomSwitching != enableRandomSwitching)
        {
            HandleEnableStateChanged();
            _lastEnableRandomSwitching = enableRandomSwitching;
        }

        labyrinthCeiling.layer = enableRandomSwitching ? LayerMask.NameToLayer("Projection") : LayerMask.NameToLayer("Default");
        targetCube.SetActive(enableRandomSwitching);
        playerModel.SetActive(enableRandomSwitching);

        if (!enableRandomSwitching)
            return;

        if (_candidateRuntimeData.Count == 0)
            return;

        if (autoRefreshChildren && Time.time >= _nextAutoRefreshTime)
        {
            RefreshChildren();
            _nextAutoRefreshTime = Time.time + autoRefreshInterval;
        }

        if (_children.Count == 0)
            return;

        switch (switchMode)
        {
            case SwitchMode.IndependentPerChild:
                UpdateIndependentPerChild();
                break;

            case SwitchMode.GlobalOneChildPerTick:
                UpdateGlobalOneChildPerTick();
                break;
        }
    }

    private void OnDisable()
    {
        if (_initialized)
        {
            RestoreAllChildrenToInitialState();
        }
    }

    [ContextMenu("刷新子物体列表")]
    public void RefreshChildren()
    {
        _children.Clear();
        _nextSwitchTimePerChild.Clear();

        if (includeAllDescendants)
        {
            CollectDescendants(transform);
        }
        else
        {
            int childCount = transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (!includeInactiveChildren && !child.gameObject.activeInHierarchy)
                    continue;

                _children.Add(child);
            }
        }

        for (int i = 0; i < _children.Count; i++)
        {
            Transform child = _children[i];
            if (child == null) continue;

            if (!_initialLayerMap.ContainsKey(child))
            {
                _initialLayerMap.Add(child, child.gameObject.layer);
            }

            _nextSwitchTimePerChild[child] = Time.time + GetRandomInterval();
        }
    }

    [ContextMenu("立即让所有子物体随机切换一次")]
    public void SwitchAllNow()
    {
        if (!enableRandomSwitching)
            return;

        RebuildCandidateLayerCache();

        for (int i = 0; i < _children.Count; i++)
        {
            Transform child = _children[i];
            if (child == null) continue;

            TrySwitchChildLayer(child);
            _nextSwitchTimePerChild[child] = Time.time + GetRandomInterval();
        }

        _nextGlobalSwitchTime = Time.time + GetRandomInterval();
    }

    public void SetRandomSwitchingEnabled(bool enabled)
    {
        if (enableRandomSwitching == enabled)
            return;

        enableRandomSwitching = enabled;
        HandleEnableStateChanged();
        _lastEnableRandomSwitching = enableRandomSwitching;
    }

    public void ToggleRandomSwitching()
    {
        SetRandomSwitchingEnabled(!enableRandomSwitching);
    }

    public void ApplyCurrentLayerConfigsToAllChildren()
    {
        for (int i = 0; i < _children.Count; i++)
        {
            Transform child = _children[i];
            if (child == null)
                continue;

            ApplyColliderConfigByCurrentLayer(child);
        }
    }

    private void HandleEnableStateChanged()
    {
        if (enableRandomSwitching)
        {
            ResetAllTimers();
        }
        else
        {
            RestoreAllChildrenToInitialState();
        }
    }

    private void RestoreAllChildrenToInitialState()
    {
        for (int i = 0; i < _children.Count; i++)
        {
            Transform child = _children[i];
            if (child == null)
                continue;

            if (_initialLayerMap.TryGetValue(child, out int initialLayer))
            {
                child.gameObject.layer = initialLayer;
            }

            ApplyColliderIsTrigger(child, false);
        }
    }

    private void UpdateIndependentPerChild()
    {
        for (int i = 0; i < _children.Count; i++)
        {
            Transform child = _children[i];
            if (child == null) continue;

            if (!includeInactiveChildren && !child.gameObject.activeInHierarchy)
                continue;

            if (!_nextSwitchTimePerChild.TryGetValue(child, out float nextTime))
            {
                nextTime = Time.time + GetRandomInterval();
                _nextSwitchTimePerChild[child] = nextTime;
            }

            if (Time.time >= nextTime)
            {
                TrySwitchChildLayer(child);
                _nextSwitchTimePerChild[child] = Time.time + GetRandomInterval();
            }
        }
    }

    private void UpdateGlobalOneChildPerTick()
    {
        if (Time.time < _nextGlobalSwitchTime)
            return;

        _tempValidChildren.Clear();

        for (int i = 0; i < _children.Count; i++)
        {
            Transform child = _children[i];
            if (child == null)
                continue;

            if (!includeInactiveChildren && !child.gameObject.activeInHierarchy)
                continue;

            _tempValidChildren.Add(child);
        }

        if (_tempValidChildren.Count > 0)
        {
            int index = UnityEngine.Random.Range(0, _tempValidChildren.Count);
            Transform child = _tempValidChildren[index];
            TrySwitchChildLayer(child);

            if (child != null)
            {
                _nextSwitchTimePerChild[child] = Time.time + GetRandomInterval();
            }
        }

        _nextGlobalSwitchTime = Time.time + GetRandomInterval();
    }

    private void TrySwitchChildLayer(Transform child)
    {
        if (child == null)
            return;

        int currentLayer = child.gameObject.layer;
        LayerRuntimeData runtimeData;
        if (!TryGetWeightedRandomLayer(currentLayer, out runtimeData))
            return;

        child.gameObject.layer = runtimeData.layerIndex;
        ApplyColliderIsTrigger(child, runtimeData.setIsTrigger);

        if (logSwitchResult)
        {
            Debug.Log(
                $"[{nameof(RandomChildLayerSwitcher)}] 子物体 {child.name} 切换到 Layer={LayerMask.LayerToName(runtimeData.layerIndex)}({runtimeData.layerIndex}), isTrigger={runtimeData.setIsTrigger}",
                child);
        }
    }

    private bool TryGetWeightedRandomLayer(int currentLayer, out LayerRuntimeData result)
    {
        result = default;

        float totalWeight = 0f;

        for (int i = 0; i < _candidateRuntimeData.Count; i++)
        {
            LayerRuntimeData data = _candidateRuntimeData[i];

            if (data.weight <= 0f)
                continue;

            if (!allowSameLayer && data.layerIndex == currentLayer)
                continue;

            totalWeight += data.weight;
        }

        if (totalWeight <= 0f)
        {
            if (allowSameLayer)
            {
                for (int i = 0; i < _candidateRuntimeData.Count; i++)
                {
                    if (_candidateRuntimeData[i].layerIndex == currentLayer)
                    {
                        result = _candidateRuntimeData[i];
                        return true;
                    }
                }
            }

            return false;
        }

        float randomValue = UnityEngine.Random.Range(0f, totalWeight);
        float accumulated = 0f;

        for (int i = 0; i < _candidateRuntimeData.Count; i++)
        {
            LayerRuntimeData data = _candidateRuntimeData[i];

            if (data.weight <= 0f)
                continue;

            if (!allowSameLayer && data.layerIndex == currentLayer)
                continue;

            accumulated += data.weight;
            if (randomValue <= accumulated)
            {
                result = data;
                return true;
            }
        }

        for (int i = _candidateRuntimeData.Count - 1; i >= 0; i--)
        {
            LayerRuntimeData data = _candidateRuntimeData[i];

            if (data.weight <= 0f)
                continue;

            if (!allowSameLayer && data.layerIndex == currentLayer)
                continue;

            result = data;
            return true;
        }

        return false;
    }

    private void RebuildCandidateLayerCache()
    {
        _candidateRuntimeData.Clear();

        Dictionary<int, LayerSwitchEntry> settingMap = BuildSettingMap();

        for (int layer = 0; layer < 32; layer++)
        {
            if ((candidateLayersMask.value & (1 << layer)) == 0)
                continue;

            LayerRuntimeData data = new LayerRuntimeData
            {
                layerIndex = layer,
                weight = defaultWeight,
                setIsTrigger = defaultIsTrigger
            };

            if (settingMap.TryGetValue(layer, out LayerSwitchEntry entry) && entry != null)
            {
                data.weight = Mathf.Max(0f, entry.weight);
                data.setIsTrigger = entry.setIsTrigger;
            }

            _candidateRuntimeData.Add(data);
        }
    }

    private Dictionary<int, LayerSwitchEntry> BuildSettingMap()
    {
        Dictionary<int, LayerSwitchEntry> result = new Dictionary<int, LayerSwitchEntry>();

        if (layerSettings == null)
            return result;

        for (int i = 0; i < layerSettings.Count; i++)
        {
            LayerSwitchEntry entry = layerSettings[i];
            if (entry == null)
                continue;

            if (string.IsNullOrWhiteSpace(entry.layerName))
                continue;

            int layerIndex = LayerMask.NameToLayer(entry.layerName);
            if (layerIndex < 0)
            {
                // Debug.LogWarning(
                //     $"[{nameof(RandomChildLayerSwitcher)}] Layer 名称无效：\"{entry.layerName}\"，请先在 Tags and Layers 中创建该 Layer。",
                //     this);
                continue;
            }

            result[layerIndex] = entry;
        }

        return result;
    }

    private void ApplyColliderConfigByCurrentLayer(Transform child)
    {
        if (child == null)
            return;

        int currentLayer = child.gameObject.layer;

        for (int i = 0; i < _candidateRuntimeData.Count; i++)
        {
            if (_candidateRuntimeData[i].layerIndex != currentLayer)
                continue;

            ApplyColliderIsTrigger(child, _candidateRuntimeData[i].setIsTrigger);
            return;
        }

        ApplyColliderIsTrigger(child, defaultIsTrigger);
    }

    private void ApplyColliderIsTrigger(Transform child, bool targetIsTrigger)
    {
        if (child == null)
            return;

        _tempColliders.Clear();

        if (colliderApplyMode == ColliderApplyMode.SelfOnly)
        {
            Collider[] selfColliders = child.GetComponents<Collider>();
            for (int i = 0; i < selfColliders.Length; i++)
            {
                if (selfColliders[i] != null)
                {
                    _tempColliders.Add(selfColliders[i]);
                }
            }
        }
        else
        {
            Collider[] colliders = child.GetComponentsInChildren<Collider>(includeInactiveChildren);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                {
                    _tempColliders.Add(colliders[i]);
                }
            }
        }

        for (int i = 0; i < _tempColliders.Count; i++)
        {
            Collider col = _tempColliders[i];
            if (col == null)
                continue;

            col.isTrigger = targetIsTrigger;
        }
    }

    private float GetRandomInterval()
    {
        if (maxInterval <= minInterval)
            return minInterval;

        return UnityEngine.Random.Range(minInterval, maxInterval);
    }

    private void ResetAllTimers()
    {
        _nextGlobalSwitchTime = Time.time + GetRandomInterval();
        _nextAutoRefreshTime = Time.time + autoRefreshInterval;

        List<Transform> keys = new List<Transform>(_nextSwitchTimePerChild.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            Transform child = keys[i];
            if (child == null)
                continue;

            _nextSwitchTimePerChild[child] = Time.time + GetRandomInterval();
        }
    }

    private void CollectDescendants(Transform root)
    {
        int childCount = root.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform child = root.GetChild(i);

            if (includeInactiveChildren || child.gameObject.activeInHierarchy)
            {
                _children.Add(child);
            }

            CollectDescendants(child);
        }
    }
}