using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[AddComponentMenu("Game/Projection Realtime Viewer")]
public class ProjectionRealtimeViewer : MonoBehaviour
{
    public enum ViewLayout
    {
        Vertical,
        Horizontal
    }

    [Header("引用")]
    [Tooltip("世界模式管理器，用于读取投影模式与投影区域参数。")]
    public ASCIIWorldModeManager asciiWorldModeManager;

    [Tooltip("对象注册管理器，用于读取运行时 Virtual 列表。")]
    public ASCIIWorldRegistryManager registryManager;

    [Header("开关")]
    public bool showViewer = true;
    public bool showVirtualId = true;
    public bool showProjectionMask = true;
    public bool showProjectionId = true;
    public bool showSolidId = true;
    [Tooltip("是否使用按一下切换显示/隐藏，而不是按住显示。")]
    public bool useTapToggle = true;

    [Tooltip("用于切换显示/隐藏的按键。")]
    public KeyCode toggleKey = KeyCode.Tab;

    [Tooltip("初始是否显示 RT 预览。")]
    public bool viewerVisibleByToggle = false;

    [Header("布局")]
    public ViewLayout layout = ViewLayout.Vertical;
    public float marginX = 12f;
    public float marginY = 12f;
    public float panelWidth = 256f;
    public float panelHeight = 144f;
    public float spacing = 24f;

    [Header("预览 Shader")]
    public Shader previewShader;

    [Header("RT 预览颜色")]
    public Color maskColor = Color.green;
    public Color labelColor = Color.white;
    public Color zeroColor = Color.black;
    public Color idTint = Color.white;

    [Header("投影模式下 Virtual 调试显示")]
    [Tooltip("是否绘制当前投影模式下、运行时来源且当前状态为 Virtual 的对象 bounds。")]
    public bool showVirtualBounds = true;

    [Tooltip("是否绘制当前投影模式下、运行时来源且当前状态为 Virtual 的判定采样点。")]
    public bool showVirtualSamplePoints = true;

    [Tooltip("是否绘制落在判定框内的采样点。")]
    public bool showInsideSamplePoints = true;

    [Tooltip("是否绘制落在判定框外的采样点。")]
    public bool showOutsideSamplePoints = true;

    [Tooltip("是否绘制投影模式的判定框。")]
    public bool showProjectionRegionGizmo = true;

    [Header("Invalid Overlap 调试显示")]
    [Tooltip("是否绘制当前 invalid Projection 命中的重叠 Collider。")]
    public bool showInvalidOverlapColliders = true;

    [Tooltip("是否绘制当前处于 invalid 判定命中的 Projection 的 Collider。")]
    public bool showInvalidProjectionColliders = true;

    [Header("Gizmo 颜色")]
    [Tooltip("Virtual bounds 颜色。")]
    public Color virtualBoundsColor = Color.green;

    [Tooltip("框内采样点颜色。")]
    public Color insideSamplePointColor = Color.yellow;

    [Tooltip("框外采样点颜色。")]
    public Color outsideSamplePointColor = Color.red;

    [Tooltip("投影判定框颜色。")]
    public Color projectionRegionColor = Color.cyan;

    [Tooltip("invalid Projection 自身 Collider 颜色。")]
    public Color invalidProjectionColliderColor = new Color(1f, 0.5f, 0f, 1f);

    [Tooltip("与 invalid Projection 重叠的外部 Collider 颜色。")]
    public Color invalidOverlapColliderColor = Color.magenta;

    [Header("Gizmo 尺寸")]
    [Tooltip("采样点球半径。")]
    public float samplePointRadius = 0.03f;

    [Tooltip("判定框绘制深度。")]
    public float regionGizmoDepth = 2f;

    private Material previewMaterial;

    private void Awake()
    {
        EnsurePreviewMaterial();
    }
    private void Update()
    {
        if (!useTapToggle)
            return;

        if (Input.GetKeyDown(toggleKey))
            viewerVisibleByToggle = !viewerVisibleByToggle;
    }

    private void OnEnable()
    {
        EnsurePreviewMaterial();
    }

    private void OnValidate()
    {
        if (panelWidth < 1f) panelWidth = 1f;
        if (panelHeight < 1f) panelHeight = 1f;
        if (spacing < 0f) spacing = 0f;
        if (marginX < 0f) marginX = 0f;
        if (marginY < 0f) marginY = 0f;
        if (samplePointRadius < 0.001f) samplePointRadius = 0.001f;
        if (regionGizmoDepth < 0.01f) regionGizmoDepth = 0.01f;
    }

    private void OnDestroy()
    {
        ReleasePreviewMaterial();
    }

    private void EnsurePreviewMaterial()
    {
        if (previewShader == null)
            previewShader = Shader.Find("Hidden/Debug/ProjectionRTPreview");

        if (previewMaterial == null && previewShader != null)
            previewMaterial = new Material(previewShader);
    }

    private void ReleasePreviewMaterial()
    {
        if (previewMaterial != null)
        {
            Destroy(previewMaterial);
            previewMaterial = null;
        }
    }

    private void OnGUI()
    {
        if (Event.current == null || Event.current.type != EventType.Repaint)
            return;

        if (!showViewer)
            return;

        if (previewMaterial == null)
            EnsurePreviewMaterial();

        if (previewMaterial == null)
            return;

        float x = marginX;
        float y = marginY;

        if (useTapToggle) {
            if (!viewerVisibleByToggle)
                return;
        } else {
            if (!Input.GetKey(toggleKey))
                return;
        }

        if (showVirtualId)
        {
            DrawRTPanel(
                "VirtualIdRT",
                ASCIIDualPassRendererFeature.LatestVirtualIdRT,
                mode: 1,
                x: x,
                y: y
            );
            Advance(ref x, ref y);
        }

        if (showProjectionMask)
        {
            DrawRTPanel(
                "ProjectionMaskRT",
                ASCIIDualPassRendererFeature.LatestProjectionMaskRT,
                mode: 0,
                x: x,
                y: y
            );
            Advance(ref x, ref y);
        }

        if (showProjectionId)
        {
            DrawRTPanel(
                "ProjectionIdRT",
                ASCIIDualPassRendererFeature.LatestProjectionIdRT,
                mode: 1,
                x: x,
                y: y
            );
            Advance(ref x, ref y);
        }

        if (showSolidId)
        {
            DrawRTPanel(
                "SolidIdRT",
                ASCIIDualPassRendererFeature.LatestSolidIdRT,
                mode: 1,
                x: x,
                y: y
            );
        }
    }

    private void Advance(ref float x, ref float y)
    {
        if (layout == ViewLayout.Vertical)
            y += panelHeight + spacing;
        else
            x += panelWidth + spacing;
    }

    private void DrawRTPanel(string label, RTHandle handle, int mode, float x, float y)
    {
        Color oldColor = GUI.color;
        GUI.color = labelColor;
        GUI.Label(new Rect(x, y - 20f, 300f, 20f), label);
        GUI.color = oldColor;

        Rect rect = new Rect(x, y, panelWidth, panelHeight);

        if (handle == null || handle.rt == null)
        {
            GUI.Box(rect, "RT not ready");
            return;
        }

        previewMaterial.SetFloat("_Mode", mode);
        previewMaterial.SetColor("_MaskColor", maskColor);
        previewMaterial.SetColor("_ZeroColor", zeroColor);
        previewMaterial.SetColor("_NonZeroTint", idTint);

        Graphics.DrawTexture(rect, handle.rt, previewMaterial);
    }

    private void OnDrawGizmos()
    {
        if (asciiWorldModeManager == null || registryManager == null)
            return;

        if (!asciiWorldModeManager.IsProjectionMode)
            return;

        Camera cam = asciiWorldModeManager.targetCamera;
        if (cam == null)
            return;

        if (showProjectionRegionGizmo)
            DrawProjectionRegion(cam);

        DrawInvalidProjectionOverlapDebug();

        if (!showVirtualBounds && !showVirtualSamplePoints)
            return;

        IReadOnlyList<ASCIIWorldObject> runtimeVirtuals = registryManager.RuntimeVirtualObjects;
        if (runtimeVirtuals == null)
            return;

        int sampleGridX = Mathf.Max(2, asciiWorldModeManager.sampleGridX);
        int sampleGridY = Mathf.Max(2, asciiWorldModeManager.sampleGridY);
        Rect viewportRect = GetViewportRectFromManager();

        for (int i = 0; i < runtimeVirtuals.Count; i++)
        {
            ASCIIWorldObject worldObject = runtimeVirtuals[i];
            if (worldObject == null)
                continue;

            if (worldObject.CurrentState != ASCIIWorldObject.RuntimeState.Virtual)
                continue;

            Renderer[] renderers = worldObject.CachedRenderers;
            Bounds mergedBounds = GetMergedBounds(renderers, out bool valid);
            if (!valid)
                continue;

            if (showVirtualBounds)
            {
                Gizmos.color = virtualBoundsColor;
                Gizmos.DrawWireCube(mergedBounds.center, mergedBounds.size);
            }

            if (showVirtualSamplePoints)
            {
                Vector3[] samplePoints = GetSamplePointsFromBounds(mergedBounds, sampleGridX, sampleGridY);

                for (int p = 0; p < samplePoints.Length; p++)
                {
                    Vector3 vp = cam.WorldToViewportPoint(samplePoints[p]);
                    bool isValid = vp.z > 0f;
                    bool inside = isValid && viewportRect.Contains(new Vector2(vp.x, vp.y));

                    if (inside)
                    {
                        if (!showInsideSamplePoints)
                            continue;

                        Gizmos.color = insideSamplePointColor;
                    }
                    else
                    {
                        if (!showOutsideSamplePoints)
                            continue;

                        Gizmos.color = outsideSamplePointColor;
                    }

                    Gizmos.DrawSphere(samplePoints[p], samplePointRadius);
                }
            }
        }
    }

    private void DrawInvalidProjectionOverlapDebug()
    {
        if (asciiWorldModeManager == null)
            return;

        if (showInvalidProjectionColliders)
        {
            IReadOnlyList<ASCIIWorldObject> invalidProjections = asciiWorldModeManager.CurrentInvalidProjectionObjects;
            if (invalidProjections != null)
            {
                Gizmos.color = invalidProjectionColliderColor;
                for (int i = 0; i < invalidProjections.Count; i++)
                {
                    ASCIIWorldObject obj = invalidProjections[i];
                    if (obj == null)
                        continue;

                    Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);
                    for (int c = 0; c < colliders.Length; c++)
                        DrawColliderGizmo(colliders[c]);
                }
            }
        }

        if (showInvalidOverlapColliders)
        {
            IReadOnlyList<Collider> overlapColliders = asciiWorldModeManager.CurrentInvalidOverlapColliders;
            if (overlapColliders != null)
            {
                Gizmos.color = invalidOverlapColliderColor;
                for (int i = 0; i < overlapColliders.Count; i++)
                    DrawColliderGizmo(overlapColliders[i]);
            }
        }
    }

    private void DrawColliderGizmo(Collider col)
    {
        if (col == null || !col.enabled)
            return;

        Matrix4x4 oldMatrix = Gizmos.matrix;

        if (col is BoxCollider box)
        {
            Gizmos.matrix = box.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
        else if (col is SphereCollider sphere)
        {
            Gizmos.matrix = sphere.transform.localToWorldMatrix;
            float maxScale = Mathf.Max(Mathf.Abs(sphere.transform.lossyScale.x), Mathf.Abs(sphere.transform.lossyScale.y), Mathf.Abs(sphere.transform.lossyScale.z));
            Gizmos.DrawWireSphere(sphere.center, sphere.radius * maxScale);
        }
        else if (col is CapsuleCollider capsule)
        {
            Bounds b = capsule.bounds;
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawWireCube(b.center, b.size);
        }
        else
        {
            Bounds b = col.bounds;
            if (b.size.sqrMagnitude > 0f)
            {
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.DrawWireCube(b.center, b.size);
            }
        }

        Gizmos.matrix = oldMatrix;
    }

    private void DrawProjectionRegion(Camera cam)
    {
        Rect rect = GetViewportRectFromManager();
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        Vector3 p1 = cam.ViewportToWorldPoint(new Vector3(rect.xMin, rect.yMin, regionGizmoDepth));
        Vector3 p2 = cam.ViewportToWorldPoint(new Vector3(rect.xMax, rect.yMin, regionGizmoDepth));
        Vector3 p3 = cam.ViewportToWorldPoint(new Vector3(rect.xMax, rect.yMax, regionGizmoDepth));
        Vector3 p4 = cam.ViewportToWorldPoint(new Vector3(rect.xMin, rect.yMax, regionGizmoDepth));

        Gizmos.color = projectionRegionColor;
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p4);
        Gizmos.DrawLine(p4, p1);
    }

    private Rect GetViewportRectFromManager()
    {
        if (asciiWorldModeManager == null)
            return new Rect(0f, 0f, 0f, 0f);

        float xMin = asciiWorldModeManager.regionCenterX - asciiWorldModeManager.regionWidth * 0.5f;
        float yMin = asciiWorldModeManager.regionCenterY - asciiWorldModeManager.regionHeight * 0.5f;
        float xMax = asciiWorldModeManager.regionCenterX + asciiWorldModeManager.regionWidth * 0.5f;
        float yMax = asciiWorldModeManager.regionCenterY + asciiWorldModeManager.regionHeight * 0.5f;

        xMin = Mathf.Clamp01(xMin);
        yMin = Mathf.Clamp01(yMin);
        xMax = Mathf.Clamp01(xMax);
        yMax = Mathf.Clamp01(yMax);

        return new Rect(
            xMin,
            yMin,
            Mathf.Max(0f, xMax - xMin),
            Mathf.Max(0f, yMax - yMin)
        );
    }

    private Bounds GetMergedBounds(Renderer[] renderers, out bool valid)
    {
        valid = false;
        Bounds bounds = default;

        if (renderers == null || renderers.Length == 0)
            return bounds;

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

        return bounds;
    }

    private Vector3[] GetSamplePointsFromBounds(Bounds bounds, int gridX, int gridY)
    {
        Vector3 center = bounds.center;
        Vector3 ext = bounds.extents;

        List<Vector3> points = new List<Vector3>(gridX * gridY * 3 + 1);
        points.Add(center);

        for (int y = 0; y < gridY; y++)
        {
            float ty = gridY == 1 ? 0.5f : (float)y / (gridY - 1);
            float py = Mathf.Lerp(-ext.y, ext.y, ty);

            for (int x = 0; x < gridX; x++)
            {
                float tx = gridX == 1 ? 0.5f : (float)x / (gridX - 1);
                float px = Mathf.Lerp(-ext.x, ext.x, tx);

                points.Add(center + new Vector3(px, py, 0f));
                points.Add(center + new Vector3(px, py, ext.z));
                points.Add(center + new Vector3(px, py, -ext.z));
            }
        }

        return points.ToArray();
    }
}