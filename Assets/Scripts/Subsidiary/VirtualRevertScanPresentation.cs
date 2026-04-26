using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("Game/Virtual Revert Scan Presentation")]
public class VirtualRevertScanPresentation : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("世界模式管理器。为空时会自动查找。")] 
    public ASCIIWorldModeManager asciiWorldModeManager;

    [Tooltip("用于计算屏幕空间高亮的摄像机。为空时优先取当前物体上的 Camera。")] 
    public Camera targetCamera;

    [Header("Canvas 预览")]
    [Tooltip("预览态根节点。此节点将保持常驻显示，不再由脚本隐藏。")]
    public GameObject previewCanvasRoot;

    [Tooltip("全屏变暗 Image。建议拉满全屏。")]
    public Image darkOverlayImage;

    [Tooltip("是否在预览时显示 Canvas 变暗层。")]
    public bool useCanvasDarkOverlay = true;

    [Header("遮罩")]
    [Range(0f, 1f)] public float darkOverlayAlpha = 0.65f;
    public Color darkOverlayColor = Color.black;

    [Header("高亮框")]
    [Tooltip("是否为当前 Q 扫描预览候选对象绘制屏幕空间高亮框。")]
    public bool drawScreenHighlightBoxes = true;

    public Color highlightFillColor = new Color(1f, 0.9f, 0.2f, 0.18f);
    public Color highlightOutlineColor = new Color(1f, 0.95f, 0.3f, 1f);

    [Min(1f)] public float outlineThickness = 3f;
    [Min(0f)] public float screenPadding = 8f;

    [Tooltip("是否在高亮框上方显示对象名。")]
    public bool drawObjectName = false;

    public Color labelColor = Color.white;

    private Texture2D solidTex;

    private void Awake()
    {
        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();

        if (asciiWorldModeManager == null)
            asciiWorldModeManager = FindObjectOfType<ASCIIWorldModeManager>();

        EnsureTexture();

        if (previewCanvasRoot != null)
            previewCanvasRoot.SetActive(true);

        RefreshCanvasState(false);
    }

    private void OnEnable()
    {
        if (asciiWorldModeManager == null)
            asciiWorldModeManager = FindObjectOfType<ASCIIWorldModeManager>();

        if (previewCanvasRoot != null)
            previewCanvasRoot.SetActive(true);

        RefreshCanvasPresentation();
    }

    private void OnDisable()
    {
        if (previewCanvasRoot != null)
            previewCanvasRoot.SetActive(true);

        RefreshCanvasState(false);
    }

    private void OnDestroy()
    {
        if (solidTex != null)
            Destroy(solidTex);
    }

    private void Update()
    {
        RefreshCanvasPresentation();
    }

    private void EnsureTexture()
    {
        if (solidTex != null)
            return;

        solidTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        solidTex.SetPixel(0, 0, Color.white);
        solidTex.Apply();
    }

    private void RefreshCanvasPresentation()
    {
        RefreshCanvasState(IsPreviewActive());
    }

    private void RefreshCanvasState(bool active)
    {
        if (previewCanvasRoot != null)
            previewCanvasRoot.SetActive(true);

        if (darkOverlayImage != null)
        {
            bool overlayActive = useCanvasDarkOverlay && active;
            if (darkOverlayImage.gameObject.activeSelf != overlayActive)
                darkOverlayImage.gameObject.SetActive(overlayActive);

            Color c = darkOverlayColor;
            c.a = darkOverlayAlpha;
            darkOverlayImage.color = c;
            darkOverlayImage.enabled = overlayActive;
        }
    }

    private void OnGUI()
    {
        if (!drawScreenHighlightBoxes)
            return;

        if (!IsPreviewActive())
            return;

        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();

        if (targetCamera == null)
            return;

        EnsureTexture();

        IReadOnlyList<ASCIIWorldObject> previewObjects = GetPreviewObjects();
        if (previewObjects == null)
            return;

        for (int i = 0; i < previewObjects.Count; i++)
        {
            ASCIIWorldObject worldObject = previewObjects[i];
            if (worldObject == null)
                continue;

            if (!worldObject.gameObject.activeInHierarchy)
                continue;

            if (!HasAnyVisibleRenderer(worldObject))
                continue;

            if (!TryGetScreenRect(worldObject, out Rect rect))
                continue;

            rect.xMin -= screenPadding;
            rect.yMin -= screenPadding;
            rect.xMax += screenPadding;
            rect.yMax += screenPadding;

            DrawRect(rect, highlightFillColor);
            DrawOutline(rect, highlightOutlineColor, outlineThickness);

            if (drawObjectName)
            {
                Color oldColor = GUI.color;
                GUI.color = labelColor;
                GUI.Label(new Rect(rect.xMin, rect.yMin - 20f, rect.width + 80f, 20f), worldObject.name);
                GUI.color = oldColor;
            }
        }
    }

    private bool IsPreviewActive()
    {
        return asciiWorldModeManager != null && asciiWorldModeManager.IsRevertNearbyVirtualPreviewActive;
    }

    private IReadOnlyList<ASCIIWorldObject> GetPreviewObjects()
    {
        return asciiWorldModeManager != null ? asciiWorldModeManager.RevertNearbyVirtualPreviewObjects : null;
    }

    private bool HasAnyVisibleRenderer(ASCIIWorldObject worldObject)
    {
        if (worldObject == null)
            return false;

        Renderer[] renderers = worldObject.CachedRenderers;
        if (renderers == null || renderers.Length == 0)
            return false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer rend = renderers[i];
            if (rend == null)
                continue;

            if (!rend.enabled)
                continue;

            if (!rend.gameObject.activeInHierarchy)
                continue;

            return true;
        }

        return false;
    }

    private bool TryGetScreenRect(ASCIIWorldObject worldObject, out Rect rect)
    {
        rect = default;
        if (worldObject == null || targetCamera == null)
            return false;

        Renderer[] renderers = worldObject.CachedRenderers;
        Bounds bounds = default;
        bool valid = false;

        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer rend = renderers[i];
                if (rend == null || !rend.enabled)
                    continue;

                if (!valid)
                {
                    bounds = rend.bounds;
                    valid = true;
                }
                else
                {
                    bounds.Encapsulate(rend.bounds);
                }
            }
        }

        if (!valid)
            return TryBuildFallbackScreenRect(worldObject.transform.position, out rect);

        Vector3 c = bounds.center;
        Vector3 e = bounds.extents;
        Vector3[] corners = new Vector3[8]
        {
            c + new Vector3(-e.x, -e.y, -e.z),
            c + new Vector3(-e.x, -e.y,  e.z),
            c + new Vector3(-e.x,  e.y, -e.z),
            c + new Vector3(-e.x,  e.y,  e.z),
            c + new Vector3( e.x, -e.y, -e.z),
            c + new Vector3( e.x, -e.y,  e.z),
            c + new Vector3( e.x,  e.y, -e.z),
            c + new Vector3( e.x,  e.y,  e.z)
        };

        bool hasVisibleCorner = false;
        float minX = float.PositiveInfinity;
        float minY = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float maxY = float.NegativeInfinity;

        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 screen = targetCamera.WorldToScreenPoint(corners[i]);
            if (screen.z <= 0f)
                continue;

            hasVisibleCorner = true;
            float guiY = Screen.height - screen.y;
            minX = Mathf.Min(minX, screen.x);
            minY = Mathf.Min(minY, guiY);
            maxX = Mathf.Max(maxX, screen.x);
            maxY = Mathf.Max(maxY, guiY);
        }

        if (!hasVisibleCorner)
            return TryBuildFallbackScreenRect(bounds.center, out rect);

        minX = Mathf.Clamp(minX, 0f, Screen.width);
        maxX = Mathf.Clamp(maxX, 0f, Screen.width);
        minY = Mathf.Clamp(minY, 0f, Screen.height);
        maxY = Mathf.Clamp(maxY, 0f, Screen.height);

        if (maxX <= minX || maxY <= minY)
            return false;

        rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    private bool TryBuildFallbackScreenRect(Vector3 worldPosition, out Rect rect)
    {
        rect = default;

        if (targetCamera == null)
            return false;

        Vector3 screen = targetCamera.WorldToScreenPoint(worldPosition);
        if (screen.z <= 0f)
            return false;

        float y = Screen.height - screen.y;
        rect = new Rect(screen.x - 20f, y - 20f, 40f, 40f);
        return true;
    }

    private void DrawOutline(Rect rect, Color color, float thickness)
    {
        DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
        DrawRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
        DrawRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
        DrawRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
    }

    private void DrawRect(Rect rect, Color color)
    {
        if (Event.current.type != EventType.Repaint)
            return;

        Color oldColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, solidTex);
        GUI.color = oldColor;
    }
}
