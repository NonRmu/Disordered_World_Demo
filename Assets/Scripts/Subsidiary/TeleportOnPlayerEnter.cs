using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class TeleportOnPlayerEnter : MonoBehaviour
{
    [Header("目标脚本")]
    [Tooltip("玩家进入触发区后，要启用的 RandomChildLayerSwitcher。")]
    public RandomChildLayerSwitcher targetSwitcher;

    [Header("触发器传送目标点")]
    [Tooltip("玩家进入触发区后，将被移动到这个目标点的位置与朝向。")]
    public Transform targetPos;

    [Header("倒计时随机传送点")]
    [Tooltip("倒计时结束后，玩家会被传送到这里面的随机一个点。点位的旋转也会作为角色朝向。")]
    public List<Transform> randomTeleportPoints = new List<Transform>();

    [Header("倒计时设置")]
    [Tooltip("场景初始化后，首次倒计时秒数。")]
    [Min(0.01f)] public float initialCountdown = 10f;

    [Tooltip("每次倒计时结束后，下一次倒计时会在当前基础上增加一个随机值（最小值）。")]
    public float countdownAddMin = 1f;

    [Tooltip("每次倒计时结束后，下一次倒计时会在当前基础上增加一个随机值（最大值）。")]
    public float countdownAddMax = 3f;

    [Tooltip("倒计时结束时，是否立刻重新开始下一轮倒计时。")]
    public bool restartCountdownAfterTeleport = true;

    [Header("UI 显示")]
    [Tooltip("用于显示倒计时/乱码的 TMP 文本。")]
    public TMP_Text countdownText;

    [Tooltip("倒计时显示格式：true=向上取整整数；false=保留一位小数。")]
    public bool showAsInteger = true;

    [Header("倒计时颜色渐变")]
    [Tooltip("是否启用倒计时文字颜色渐变。")]
    public bool useCountdownColorLerp = true;

    [Tooltip("倒计时开始时的颜色。")]
    public Color countdownStartColor = Color.green;

    [Tooltip("倒计时结束时的颜色。")]
    public Color countdownEndColor = Color.red;

    [Header("乱码效果")]
    [Tooltip("触发后，UI 是否进入乱码随机变换。")]
    public bool enableGlitchTextOnTriggered = true;

    [Tooltip("乱码内容刷新间隔。")]
    [Min(0.01f)] public float glitchRefreshInterval = 0.05f;

    [Tooltip("乱码长度随机范围。")]
    public Vector2Int glitchTextLengthRange = new Vector2Int(4, 8);

    [Tooltip("乱码字符集。")]
    public string glitchCharset = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*?";

    [Tooltip("乱码阶段的文字颜色。")]
    public Color glitchTextColor = Color.magenta;

    [Header("乱码阶段 UI 随机变化")]
    [Tooltip("是否在乱码阶段随机修改 UI 的 PosX / PosY / Width / FontSize。")]
    public bool enableGlitchRectJitter = true;

    [Tooltip("每次修改 UI 参数的随机间隔最小值。")]
    [Min(0.01f)] public float glitchRectChangeIntervalMin = 0.05f;

    [Tooltip("每次修改 UI 参数的随机间隔最大值。")]
    [Min(0.01f)] public float glitchRectChangeIntervalMax = 0.2f;

    [Tooltip("乱码阶段 PosX 的绝对随机范围。")]
    public Vector2 glitchPosXRange = new Vector2(-300f, 300f);

    [Tooltip("乱码阶段 PosY 的绝对随机范围。")]
    public Vector2 glitchPosYRange = new Vector2(-150f, 150f);

    [Tooltip("乱码阶段 Width 的绝对随机范围。")]
    public Vector2 glitchWidthRange = new Vector2(60f, 220f);
    [Tooltip("乱码阶段 Height 的绝对随机范围。")]
    public Vector2 glitchHeightRange = new Vector2(30f, 120f);

    [Tooltip("乱码阶段字号的随机范围。")]
    public Vector2 glitchFontSizeRange = new Vector2(24f, 72f);

    [Header("Player Layer 设置")]
    [Tooltip("用于识别玩家的 LayerMask。")]
    public LayerMask playerLayerMask;

    [Header("玩家引用（可选）")]
    [Tooltip("若指定，则倒计时随机传送时直接使用这个玩家引用；若为空，则按 LayerMask 自动查找。")]
    public Transform playerTransformOverride;

    [Header("可选设置")]
    [Tooltip("若玩家带有 Rigidbody，是否在传送前清空速度。")]
    public bool resetRigidbodyVelocity = true;

    [Tooltip("触发后是否隐藏本触发器身上的 Renderer。")]
    public bool hideRenderersWhenTriggered = true;

    [Tooltip("触发后是否移除 Collider，使其彻底失效。")]
    public bool removeColliderWhenTriggered = true;

    private bool _triggered = false;

    private float _currentCountdownDuration;
    private float _remainingTime;

    private Coroutine _countdownCoroutine;
    private Coroutine _glitchCoroutine;
    private Coroutine _glitchRectCoroutine;

    private Transform _cachedPlayerTransform;
    private Rigidbody _cachedPlayerRigidbody;
    private Collider _selfCollider;
    private Renderer[] _renderers;
    private RectTransform _countdownRect;

    private void Awake()
    {
        _selfCollider = GetComponent<Collider>();
        if (_selfCollider != null && !_selfCollider.isTrigger)
        {
            Debug.LogWarning(
                $"[{nameof(TeleportOnPlayerEnter)}] 当前物体上的 Collider 不是 Trigger，请勾选 Is Trigger。",
                this);
        }

        _renderers = GetComponentsInChildren<Renderer>(true);
        _currentCountdownDuration = Mathf.Max(0.01f, initialCountdown);
        _remainingTime = _currentCountdownDuration;

        CachePlayerReference();
        CacheCountdownRectReference();
        RefreshCountdownUIImmediate();
    }

    private void Start()
    {
        if (!_triggered)
        {
            _countdownCoroutine = StartCoroutine(CountdownRoutine());
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_triggered)
            return;

        if (!IsInLayerMask(other.gameObject.layer, playerLayerMask))
            return;

        _triggered = true;
        StopCountdown();

        if (targetSwitcher != null)
        {
            targetSwitcher.SetRandomSwitchingEnabled(true);
        }

        TeleportTargetToTriggerDestination(other.transform, other.attachedRigidbody);
        DisableThisTriggerObject();

        if (enableGlitchTextOnTriggered)
        {
            if (countdownText != null)
            {
                countdownText.color = glitchTextColor;
            }

            _glitchCoroutine = StartCoroutine(GlitchTextRoutine());

            if (enableGlitchRectJitter)
            {
                CacheCountdownRectReference();
                _glitchRectCoroutine = StartCoroutine(GlitchRectRoutine());
            }
        }
    }

    private IEnumerator CountdownRoutine()
    {
        while (!_triggered)
        {
            _remainingTime = _currentCountdownDuration;
            RefreshCountdownUIImmediate();

            while (_remainingTime > 0f && !_triggered)
            {
                _remainingTime -= Time.deltaTime;
                if (_remainingTime < 0f)
                    _remainingTime = 0f;

                RefreshCountdownUIImmediate();
                yield return null;
            }

            if (_triggered)
                yield break;

            TeleportPlayerToRandomPoint();

            if (!restartCountdownAfterTeleport)
                yield break;

            float randomAdd = Random.Range(
                Mathf.Min(countdownAddMin, countdownAddMax),
                Mathf.Max(countdownAddMin, countdownAddMax));

            _currentCountdownDuration += randomAdd;
        }
    }

    private void StopCountdown()
    {
        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null;
        }
    }

    private void CachePlayerReference()
    {
        if (playerTransformOverride != null)
        {
            _cachedPlayerTransform = playerTransformOverride;
            _cachedPlayerRigidbody = _cachedPlayerTransform.GetComponent<Rigidbody>();

            if (_cachedPlayerRigidbody == null)
                _cachedPlayerRigidbody = _cachedPlayerTransform.GetComponentInParent<Rigidbody>();

            return;
        }

        GameObject[] allObjects = FindObjectsOfType<GameObject>(true);
        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject go = allObjects[i];
            if (!IsInLayerMask(go.layer, playerLayerMask))
                continue;

            _cachedPlayerTransform = go.transform;
            _cachedPlayerRigidbody = go.GetComponent<Rigidbody>();

            if (_cachedPlayerRigidbody == null)
                _cachedPlayerRigidbody = go.GetComponentInParent<Rigidbody>();

            return;
        }
    }

    private void CacheCountdownRectReference()
    {
        if (countdownText == null)
            return;

        _countdownRect = countdownText.rectTransform;
    }

    private void TeleportPlayerToRandomPoint()
    {
        if (_triggered)
            return;

        if (_cachedPlayerTransform == null)
        {
            CachePlayerReference();
            if (_cachedPlayerTransform == null)
            {
                Debug.LogWarning(
                    $"[{nameof(TeleportOnPlayerEnter)}] 倒计时结束时未找到玩家，无法执行随机传送。",
                    this);
                return;
            }
        }

        Transform randomPoint = GetRandomTeleportPoint();
        if (randomPoint == null)
        {
            Debug.LogWarning(
                $"[{nameof(TeleportOnPlayerEnter)}] randomTeleportPoints 为空或全是空引用，无法执行随机传送。",
                this);
            return;
        }

        TeleportTransform(
            _cachedPlayerTransform,
            _cachedPlayerRigidbody,
            randomPoint.position,
            randomPoint.rotation);
    }

    private Transform GetRandomTeleportPoint()
    {
        if (randomTeleportPoints == null || randomTeleportPoints.Count == 0)
            return null;

        List<Transform> validPoints = null;

        for (int i = 0; i < randomTeleportPoints.Count; i++)
        {
            if (randomTeleportPoints[i] == null)
                continue;

            if (validPoints == null)
                validPoints = new List<Transform>();

            validPoints.Add(randomTeleportPoints[i]);
        }

        if (validPoints == null || validPoints.Count == 0)
            return null;

        int index = Random.Range(0, validPoints.Count);
        return validPoints[index];
    }

    private void TeleportTargetToTriggerDestination(Transform targetTransform, Rigidbody targetRb)
    {
        if (targetPos == null)
            return;

        TeleportTransform(targetTransform, targetRb, targetPos.position, targetPos.rotation);
    }

    private void TeleportTransform(Transform targetTransform, Rigidbody rb, Vector3 position, Quaternion rotation)
    {
        if (targetTransform == null)
            return;

        if (rb != null)
        {
            if (resetRigidbodyVelocity)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            rb.position = position;
            rb.rotation = rotation;

            // 同步 Transform，避免某些情况下视觉晚一帧
            targetTransform.position = position;
            targetTransform.rotation = rotation;
        }
        else
        {
            targetTransform.position = position;
            targetTransform.rotation = rotation;
        }
    }

    private void DisableThisTriggerObject()
    {
        if (removeColliderWhenTriggered && _selfCollider != null)
        {
            Destroy(_selfCollider);
            _selfCollider = null;
        }

        if (hideRenderersWhenTriggered && _renderers != null)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                    _renderers[i].enabled = false;
            }
        }
    }

    private void RefreshCountdownUIImmediate()
    {
        if (countdownText == null)
            return;

        float displayValue = Mathf.Max(0f, _remainingTime);

        if (showAsInteger)
            countdownText.text = Mathf.CeilToInt(displayValue).ToString();
        else
            countdownText.text = displayValue.ToString("F1");

        UpdateCountdownColor();
    }

    private void UpdateCountdownColor()
    {
        if (countdownText == null)
            return;

        if (_triggered)
            return;

        if (!useCountdownColorLerp)
            return;

        float duration = Mathf.Max(0.0001f, _currentCountdownDuration);
        float t = 1f - Mathf.Clamp01(_remainingTime / duration);
        countdownText.color = Color.Lerp(countdownStartColor, countdownEndColor, t);
    }

    private IEnumerator GlitchTextRoutine()
    {
        while (true)
        {
            if (countdownText != null)
            {
                int randomLength = Random.Range(
                    Mathf.Min(glitchTextLengthRange.x, glitchTextLengthRange.y),
                    Mathf.Max(glitchTextLengthRange.x, glitchTextLengthRange.y) + 1);

                countdownText.text = GenerateRandomGlitchString(randomLength);
                countdownText.color = glitchTextColor;
            }

            yield return new WaitForSeconds(glitchRefreshInterval);
        }
    }

    private IEnumerator GlitchRectRoutine()
    {
        if (_countdownRect == null)
            yield break;

        while (true)
        {
            if (_countdownRect != null)
            {
                Vector2 anchoredPos = _countdownRect.anchoredPosition;
                anchoredPos.x = Random.Range(
                    Mathf.Min(glitchPosXRange.x, glitchPosXRange.y),
                    Mathf.Max(glitchPosXRange.x, glitchPosXRange.y));
                anchoredPos.y = Random.Range(
                    Mathf.Min(glitchPosYRange.x, glitchPosYRange.y),
                    Mathf.Max(glitchPosYRange.x, glitchPosYRange.y));
                _countdownRect.anchoredPosition = anchoredPos;

                float width = Random.Range(
                    Mathf.Min(glitchWidthRange.x, glitchWidthRange.y),
                    Mathf.Max(glitchWidthRange.x, glitchWidthRange.y));
                _countdownRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);

                float height = Random.Range(
                    Mathf.Min(glitchHeightRange.x, glitchHeightRange.y),
                    Mathf.Max(glitchHeightRange.x, glitchHeightRange.y));
                _countdownRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

                float fontSize = Random.Range(
                    Mathf.Min(glitchFontSizeRange.x, glitchFontSizeRange.y),
                    Mathf.Max(glitchFontSizeRange.x, glitchFontSizeRange.y));
                countdownText.fontSize = fontSize;
            }

            float wait = Random.Range(
                Mathf.Min(glitchRectChangeIntervalMin, glitchRectChangeIntervalMax),
                Mathf.Max(glitchRectChangeIntervalMin, glitchRectChangeIntervalMax));

            yield return new WaitForSeconds(wait);
        }
    }

    private string GenerateRandomGlitchString(int length)
    {
        if (string.IsNullOrEmpty(glitchCharset))
            glitchCharset = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        length = Mathf.Max(1, length);

        char[] chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            int idx = Random.Range(0, glitchCharset.Length);
            chars[i] = glitchCharset[idx];
        }

        return new string(chars);
    }

    private static bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (initialCountdown < 0.01f)
            initialCountdown = 0.01f;

        if (glitchRefreshInterval < 0.01f)
            glitchRefreshInterval = 0.01f;

        if (glitchRectChangeIntervalMin < 0.01f)
            glitchRectChangeIntervalMin = 0.01f;

        if (glitchRectChangeIntervalMax < 0.01f)
            glitchRectChangeIntervalMax = 0.01f;

        if (glitchTextLengthRange.x < 1)
            glitchTextLengthRange.x = 1;

        if (glitchTextLengthRange.y < 1)
            glitchTextLengthRange.y = 1;

        if (glitchWidthRange.x < 1f)
            glitchWidthRange.x = 1f;

        if (glitchWidthRange.y < 1f)
            glitchWidthRange.y = 1f;

        if (glitchHeightRange.x < 1f)
            glitchHeightRange.x = 1f;

        if (glitchHeightRange.y < 1f)
            glitchHeightRange.y = 1f;

        if (glitchFontSizeRange.x < 1f)
            glitchFontSizeRange.x = 1f;

        if (glitchFontSizeRange.y < 1f)
            glitchFontSizeRange.y = 1f;
    }
#endif
}