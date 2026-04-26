using UnityEngine;

public sealed class ProjectionFrameData
{
    public int width;
    public int height;

    // 可选保留
    public int[] virtualIdPixels;

    // Projection layer
    public byte[] projectionMaskPixels;
    public int[] projectionIdPixels;

    // Solid layer
    public int[] solidIdPixels;

    // 本次分析只扫描的 ROI（基于分析 RT 尺寸）
    public RectInt analysisRoi;
}