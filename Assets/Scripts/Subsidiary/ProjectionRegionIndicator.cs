using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("Game/Projection Region Indicator")]
[RequireComponent(typeof(LineRenderer))]
public class ProjectionRegionIndicator : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("投影模式管理器。为空时自动查找。")]
    public ASCIIWorldModeManager worldModeManager;

    [Tooltip("目标 Canvas。为空时自动获取当前物体上的 Canvas。")]
    public Canvas targetCanvas;

    [Tooltip("Canvas 根 RectTransform。通常就是 Canvas 本体。")]
    public RectTransform canvasRect;

    [Header("区域根节点")]
    [Tooltip("整个区域框根节点。")]
    public RectTransform frameRoot;

    [Tooltip("中间填色区域。")]
    public Image fillImage;

    [Header("四角横竖线")]
    public Image cornerLT_H;
    public Image cornerLT_V;
    public Image cornerRT_H;
    public Image cornerRT_V;
    public Image cornerLB_H;
    public Image cornerLB_V;
    public Image cornerRB_H;
    public Image cornerRB_V;

    [Header("显示逻辑")]
    [Tooltip("是否仅在投影模式下显示。关闭后始终显示。")]
    public bool showOnlyInProjectionMode = true;

    [Tooltip("是否显示四角边框。")]
    public bool showCorners = true;

    [Tooltip("是否显示填色。")]
    public bool showFill = true;

    [Header("样式")]
    [Tooltip("四角边框颜色。")]
    public Color cornerColor = new Color(0.15f, 1f, 1f, 0.95f);

    [Tooltip("填色颜色。")]
    public Color fillColor = new Color(0.15f, 1f, 1f, 0.08f);

    [Min(0f)]
    [Tooltip("角线粗细，单位像素。")]
    public float lineThickness = 4f;

    [Min(0f)]
    [Tooltip("每个角横线长度占区域宽度的比例。")]
    [Range(0f, 0.5f)] public float cornerLengthRatioX = 0.14f;

    [Min(0f)]
    [Tooltip("每个角竖线长度占区域高度的比例。")]
    [Range(0f, 0.5f)] public float cornerLengthRatioY = 0.18f;

    [Min(0f)]
    [Tooltip("角线最小长度，单位像素。")]
    public float cornerMinLength = 24f;

    [Min(0f)]
    [Tooltip("角线最大长度，单位像素。0 表示不限制。")]
    public float cornerMaxLength = 80f;

    [Min(0f)]
    [Tooltip("填色相对外框内缩，单位像素。")]
    public float fillInsetPixels = 6f;

    [Tooltip("整体像素偏移。")]
    public Vector2 pixelOffset = Vector2.zero;

    [Header("刷新")]
    [Tooltip("是否每帧刷新。")]
    public bool refreshEveryFrame = true;

    private void Awake()
    {
        ResolveReferences();
        ApplyStyle();
        RefreshNow();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ApplyStyle();
        RefreshNow();
    }

    private void OnValidate()
    {
        if (lineThickness < 0f) lineThickness = 0f;
        if (cornerMinLength < 0f) cornerMinLength = 0f;
        if (cornerMaxLength < 0f) cornerMaxLength = 0f;
        if (fillInsetPixels < 0f) fillInsetPixels = 0f;

        ResolveReferences();
        ApplyStyle();

        if (!Application.isPlaying)
            RefreshNow();
    }

    private void LateUpdate()
    {
        if (refreshEveryFrame)
            RefreshNow();
    }

    [ContextMenu("刷新区域UI")]
    public void RefreshNow()
    {
        ResolveReferences();

        if (worldModeManager == null || canvasRect == null || frameRoot == null)
        {
            SetRootVisible(false);
            return;
        }

        bool shouldShow = !showOnlyInProjectionMode || worldModeManager.IsProjectionMode;
        SetRootVisible(shouldShow);

        if (!shouldShow)
            return;

        ApplyStyle();
        UpdateLayout();
    }

    private void ResolveReferences()
    {
        if (worldModeManager == null)
            worldModeManager = FindFirstObjectByType<ASCIIWorldModeManager>();

        if (targetCanvas == null)
            targetCanvas = GetComponent<Canvas>();

        if (canvasRect == null && targetCanvas != null)
            canvasRect = targetCanvas.GetComponent<RectTransform>();
    }

    private void ApplyStyle()
    {
        SetImageStyle(fillImage, fillColor, showFill);

        SetImageStyle(cornerLT_H, cornerColor, showCorners);
        SetImageStyle(cornerLT_V, cornerColor, showCorners);
        SetImageStyle(cornerRT_H, cornerColor, showCorners);
        SetImageStyle(cornerRT_V, cornerColor, showCorners);
        SetImageStyle(cornerLB_H, cornerColor, showCorners);
        SetImageStyle(cornerLB_V, cornerColor, showCorners);
        SetImageStyle(cornerRB_H, cornerColor, showCorners);
        SetImageStyle(cornerRB_V, cornerColor, showCorners);
    }

    private void SetImageStyle(Image image, Color color, bool visible)
    {
        if (image == null)
            return;

        image.color = color;
        image.enabled = visible;
    }

    private void UpdateLayout()
    {
        Rect viewportRect = GetViewportRectFromManager();

        float canvasWidth = canvasRect.rect.width;
        float canvasHeight = canvasRect.rect.height;

        float xMin = viewportRect.xMin * canvasWidth;
        float xMax = viewportRect.xMax * canvasWidth;
        float yMin = viewportRect.yMin * canvasHeight;
        float yMax = viewportRect.yMax * canvasHeight;

        float width = Mathf.Max(0f, xMax - xMin);
        float height = Mathf.Max(0f, yMax - yMin);

        Vector2 center = new Vector2(
            xMin + width * 0.5f,
            yMin + height * 0.5f
        ) + pixelOffset;

        frameRoot.anchorMin = new Vector2(0f, 0f);
        frameRoot.anchorMax = new Vector2(0f, 0f);
        frameRoot.pivot = new Vector2(0.5f, 0.5f);
        frameRoot.anchoredPosition = center;
        frameRoot.sizeDelta = new Vector2(width, height);

        float hLen = Mathf.Max(cornerMinLength, width * cornerLengthRatioX);
        float vLen = Mathf.Max(cornerMinLength, height * cornerLengthRatioY);

        if (cornerMaxLength > 0f)
        {
            hLen = Mathf.Min(hLen, cornerMaxLength);
            vLen = Mathf.Min(vLen, cornerMaxLength);
        }

        hLen = Mathf.Min(hLen, width);
        vLen = Mathf.Min(vLen, height);

        float halfW = width * 0.5f;
        float halfH = height * 0.5f;
        float halfT = lineThickness * 0.5f;

        // 左上
        LayoutRect(cornerLT_H, new Vector2(-halfW + hLen * 0.5f,  halfH - halfT), new Vector2(hLen, lineThickness));
        LayoutRect(cornerLT_V, new Vector2(-halfW + halfT,         halfH - vLen * 0.5f), new Vector2(lineThickness, vLen));

        // 右上
        LayoutRect(cornerRT_H, new Vector2( halfW - hLen * 0.5f,   halfH - halfT), new Vector2(hLen, lineThickness));
        LayoutRect(cornerRT_V, new Vector2( halfW - halfT,         halfH - vLen * 0.5f), new Vector2(lineThickness, vLen));

        // 左下
        LayoutRect(cornerLB_H, new Vector2(-halfW + hLen * 0.5f,  -halfH + halfT), new Vector2(hLen, lineThickness));
        LayoutRect(cornerLB_V, new Vector2(-halfW + halfT,        -halfH + vLen * 0.5f), new Vector2(lineThickness, vLen));

        // 右下
        LayoutRect(cornerRB_H, new Vector2( halfW - hLen * 0.5f,  -halfH + halfT), new Vector2(hLen, lineThickness));
        LayoutRect(cornerRB_V, new Vector2( halfW - halfT,        -halfH + vLen * 0.5f), new Vector2(lineThickness, vLen));

        if (fillImage != null)
        {
            RectTransform fillRect = fillImage.rectTransform;
            fillRect.anchorMin = new Vector2(0.5f, 0.5f);
            fillRect.anchorMax = new Vector2(0.5f, 0.5f);
            fillRect.pivot = new Vector2(0.5f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = new Vector2(
                Mathf.Max(0f, width - fillInsetPixels * 2f),
                Mathf.Max(0f, height - fillInsetPixels * 2f)
            );
        }
    }

    private void LayoutRect(Image image, Vector2 anchoredPos, Vector2 size)
    {
        if (image == null)
            return;

        RectTransform rt = image.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
    }

    private Rect GetViewportRectFromManager()
    {
        float halfW = worldModeManager.regionWidth * 0.5f;
        float halfH = worldModeManager.regionHeight * 0.5f;

        float xMin = Mathf.Clamp01(worldModeManager.regionCenterX - halfW);
        float xMax = Mathf.Clamp01(worldModeManager.regionCenterX + halfW);
        float yMin = Mathf.Clamp01(worldModeManager.regionCenterY - halfH);
        float yMax = Mathf.Clamp01(worldModeManager.regionCenterY + halfH);

        if (xMax < xMin) xMax = xMin;
        if (yMax < yMin) yMax = yMin;

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private void SetRootVisible(bool visible)
    {
        if (frameRoot != null)
            frameRoot.gameObject.SetActive(visible);
    }

    public void SetFillEnabled(bool enabled)
    {
        showFill = enabled;
        ApplyStyle();
    }

    public void SetCornersEnabled(bool enabled)
    {
        showCorners = enabled;
        ApplyStyle();
    }

    public void SetFillColor(Color color)
    {
        fillColor = color;
        ApplyStyle();
    }

    public void SetCornerColor(Color color)
    {
        cornerColor = color;
        ApplyStyle();
    }
}