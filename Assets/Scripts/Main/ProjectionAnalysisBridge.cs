using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[AddComponentMenu("Game/Projection Analysis Bridge")]
public class ProjectionAnalysisBridge : MonoBehaviour
{
    [Tooltip("用于计算局部扫描 ROI。")]
    public ASCIIWorldModeManager asciiWorldModeManager;

    private readonly ProjectionFrameData latestFrame = new ProjectionFrameData();

    private bool projectionMaskPending;
    private bool projectionIdPending;
    private bool solidIdPending;

    private bool projectionMaskReady;
    private bool projectionIdReady;
    private bool solidIdReady;

    private int pendingWidth;
    private int pendingHeight;
    private RectInt pendingRoi;

    public bool IsRequestPending => projectionMaskPending || projectionIdPending || solidIdPending;
    public int CompletedFrameVersion { get; private set; } = 0;

    public bool TryGetLatestFrame(out ProjectionFrameData frame)
    {
        if (latestFrame.width <= 0 ||
            latestFrame.height <= 0 ||
            latestFrame.projectionMaskPixels == null ||
            latestFrame.projectionIdPixels == null ||
            latestFrame.solidIdPixels == null)
        {
            frame = null;
            return false;
        }

        frame = latestFrame;
        return true;
    }

    [ContextMenu("Request Readback")]
    public void RequestReadback()
    {
        if (IsRequestPending)
            return;

        RTHandle projectionMaskRT = ASCIIDualPassRendererFeature.LatestProjectionMaskRT;
        RTHandle projectionIdRT = ASCIIDualPassRendererFeature.LatestProjectionIdRT;
        RTHandle solidIdRT = ASCIIDualPassRendererFeature.LatestSolidIdRT;

        if (projectionMaskRT == null || projectionIdRT == null || solidIdRT == null)
            return;

        pendingWidth = ASCIIDualPassRendererFeature.LatestAnalysisWidth;
        pendingHeight = ASCIIDualPassRendererFeature.LatestAnalysisHeight;

        if (pendingWidth <= 0 || pendingHeight <= 0)
            return;

        pendingRoi = BuildAnalysisRoi(pendingWidth, pendingHeight);

        projectionMaskPending = true;
        projectionIdPending = true;
        solidIdPending = true;

        projectionMaskReady = false;
        projectionIdReady = false;
        solidIdReady = false;

        AsyncGPUReadback.Request(projectionMaskRT.rt, 0, request =>
        {
            projectionMaskPending = false;
            if (request.hasError)
                return;

            NativeArray<byte> data = request.GetData<byte>();
            if (latestFrame.projectionMaskPixels == null || latestFrame.projectionMaskPixels.Length != data.Length)
                latestFrame.projectionMaskPixels = new byte[data.Length];

            data.CopyTo(latestFrame.projectionMaskPixels);
            projectionMaskReady = true;
            TryCommitFrame();
        });

        AsyncGPUReadback.Request(projectionIdRT.rt, 0, request =>
        {
            projectionIdPending = false;
            if (request.hasError)
                return;

            NativeArray<float> data = request.GetData<float>();
            if (latestFrame.projectionIdPixels == null || latestFrame.projectionIdPixels.Length != data.Length)
                latestFrame.projectionIdPixels = new int[data.Length];

            for (int i = 0; i < data.Length; i++)
                latestFrame.projectionIdPixels[i] = Mathf.RoundToInt(data[i]);

            projectionIdReady = true;
            TryCommitFrame();
        });

        AsyncGPUReadback.Request(solidIdRT.rt, 0, request =>
        {
            solidIdPending = false;
            if (request.hasError)
                return;

            NativeArray<float> data = request.GetData<float>();
            if (latestFrame.solidIdPixels == null || latestFrame.solidIdPixels.Length != data.Length)
                latestFrame.solidIdPixels = new int[data.Length];

            for (int i = 0; i < data.Length; i++)
                latestFrame.solidIdPixels[i] = Mathf.RoundToInt(data[i]);

            solidIdReady = true;
            TryCommitFrame();
        });
    }

    private void TryCommitFrame()
    {
        if (!projectionMaskReady || !projectionIdReady || !solidIdReady)
            return;

        latestFrame.width = pendingWidth;
        latestFrame.height = pendingHeight;
        latestFrame.analysisRoi = pendingRoi;
        latestFrame.virtualIdPixels = null;

        CompletedFrameVersion++;
    }

    private RectInt BuildAnalysisRoi(int width, int height)
    {
        if (asciiWorldModeManager == null)
            return new RectInt(0, 0, width, height);

        float xMin01 = asciiWorldModeManager.regionCenterX - asciiWorldModeManager.regionWidth * 0.5f;
        float yMin01 = asciiWorldModeManager.regionCenterY - asciiWorldModeManager.regionHeight * 0.5f;
        float xMax01 = asciiWorldModeManager.regionCenterX + asciiWorldModeManager.regionWidth * 0.5f;
        float yMax01 = asciiWorldModeManager.regionCenterY + asciiWorldModeManager.regionHeight * 0.5f;

        xMin01 = Mathf.Clamp01(xMin01);
        yMin01 = Mathf.Clamp01(yMin01);
        xMax01 = Mathf.Clamp01(xMax01);
        yMax01 = Mathf.Clamp01(yMax01);

        int xMin = Mathf.FloorToInt(xMin01 * width);
        int yMin = Mathf.FloorToInt(yMin01 * height);
        int xMax = Mathf.CeilToInt(xMax01 * width);
        int yMax = Mathf.CeilToInt(yMax01 * height);

        xMin = Mathf.Clamp(xMin, 0, width);
        yMin = Mathf.Clamp(yMin, 0, height);
        xMax = Mathf.Clamp(xMax, 0, width);
        yMax = Mathf.Clamp(yMax, 0, height);

        return new RectInt(
            xMin,
            yMin,
            Mathf.Max(0, xMax - xMin),
            Mathf.Max(0, yMax - yMin)
        );
    }
}