using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

public class ASCIIDualPassRendererFeature : ScriptableRendererFeature
{
    [Serializable]
    public class Settings
    {
        [Header("Layers（必须只勾一个 Layer）")]
        public LayerMask solidLayer;
        public LayerMask virtualLayer;
        public LayerMask projectionLayer;
        public LayerMask projectionDraggingLayer;
        public LayerMask projectionInvalidLayer;
        public LayerMask projectionInvalidWithSolidLayer;

        [Header("Projection Mask Occluders")]
        [Tooltip("这些 Layer 上的物体会遮挡 ProjectionMaskRT，例如 Default、Solid。")]
        public LayerMask projectionMaskOccluderLayers;

        [Header("Pass Event")]
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingOpaques;

        [Header("Override Materials")]
        public Material virtualOverrideMaterial;
        public Material projectionOverrideMaterial;
        public Material projectionDraggingOverrideMaterial;
        public Material projectionInvalidOverrideMaterial;
        public Material projectionInvalidWithSolidOverrideMaterial;

        [Header("Internal Shaders")]
        public Shader maskShader;
        public Shader projectionIdShader;

        [Header("Rendering")]
        public bool renderTransparentsToo = true;

        [Header("Analysis")]
        public bool outputAnalysisRTs = true;

        [Tooltip("分析 RT 降采样。1=全分辨率，2=半分辨率，4=四分之一分辨率。")]
        [Min(1)] public int analysisDownsample = 2;
    }

    public Settings settings = new Settings();

    private RenderAsciiDualPass pass;
    private Material maskMaterial;
    private Material projectionIdMaterial;

    public static RTHandle LatestVirtualIdRT { get; private set; }
    public static RTHandle LatestProjectionMaskRT { get; private set; }
    public static RTHandle LatestProjectionIdRT { get; private set; }
    public static RTHandle LatestSolidIdRT { get; private set; }
    public static int LatestAnalysisWidth { get; private set; }
    public static int LatestAnalysisHeight { get; private set; }
    public static LayerMask LatestProjectionMaskOccluderLayers { get; private set; }
    public static LayerMask LatestProjectionFamilyLayers { get; private set; }

    public override void Create()
    {
        if (maskMaterial != null)
            CoreUtils.Destroy(maskMaterial);

        if (projectionIdMaterial != null)
            CoreUtils.Destroy(projectionIdMaterial);

        maskMaterial = settings.maskShader != null
            ? CoreUtils.CreateEngineMaterial(settings.maskShader)
            : null;

        projectionIdMaterial = settings.projectionIdShader != null
            ? CoreUtils.CreateEngineMaterial(settings.projectionIdShader)
            : null;

        pass = new RenderAsciiDualPass(settings, maskMaterial, projectionIdMaterial)
        {
            renderPassEvent = settings.passEvent
        };
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        if (pass == null)
            return;

        pass.Setup(renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (pass == null)
            return;

        CameraType camType = renderingData.cameraData.cameraType;
        if (camType != CameraType.Game && camType != CameraType.SceneView)
            return;

        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        if (maskMaterial != null)
            CoreUtils.Destroy(maskMaterial);

        if (projectionIdMaterial != null)
            CoreUtils.Destroy(projectionIdMaterial);

        pass?.Dispose();
        pass = null;

        LatestVirtualIdRT = null;
        LatestProjectionMaskRT = null;
        LatestProjectionIdRT = null;
        LatestSolidIdRT = null;
        LatestAnalysisWidth = 0;
        LatestAnalysisHeight = 0;
        LatestProjectionMaskOccluderLayers = 0;
        LatestProjectionFamilyLayers = 0;
    }

    class RenderAsciiDualPass : ScriptableRenderPass
    {
        private readonly Settings settings;
        private readonly Material maskMaterial;
        private readonly Material projectionIdMaterial;

        private RTHandle cameraColorTarget;
        private RTHandle cameraDepthTarget;

        private RTHandle virtualLitRT;
        private RTHandle analysisDepthRT;
        private RTHandle virtualIdRT;
        private RTHandle projectionMaskRT;
        private RTHandle projectionIdRT;
        private RTHandle solidIdRT;

        private readonly ProfilingSampler profiling = new ProfilingSampler("ASCII Dual Pass");

        private readonly List<ShaderTagId> shaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit"),
        };

        private static readonly int LitAsciiTexID = Shader.PropertyToID("_LitAsciiSourceTex");
        private static readonly int AsciiMaskTexID = Shader.PropertyToID("_AsciiMaskTex");

        public RenderAsciiDualPass(Settings settings, Material maskMaterial, Material projectionIdMaterial)
        {
            this.settings = settings;
            this.maskMaterial = maskMaterial;
            this.projectionIdMaterial = projectionIdMaterial;
        }

        public void Setup(RTHandle colorTarget, RTHandle depthTarget)
        {
            cameraColorTarget = colorTarget;
            cameraDepthTarget = depthTarget;
        }

        public void Dispose()
        {
            virtualLitRT?.Release();
            virtualLitRT = null;

            analysisDepthRT?.Release();
            analysisDepthRT = null;

            virtualIdRT?.Release();
            virtualIdRT = null;

            projectionMaskRT?.Release();
            projectionMaskRT = null;

            projectionIdRT?.Release();
            projectionIdRT = null;

            solidIdRT?.Release();
            solidIdRT = null;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var cameraDesc = renderingData.cameraData.cameraTargetDescriptor;
            cameraDesc.depthBufferBits = 0;
            cameraDesc.msaaSamples = 1;

            RenderingUtils.ReAllocateIfNeeded(
                ref virtualLitRT,
                cameraDesc,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_ASCII_Virtual_Lit_RT"
            );

            if (settings.outputAnalysisRTs)
            {
                int ds = Mathf.Max(1, settings.analysisDownsample);

                var analysisColorDesc = cameraDesc;
                analysisColorDesc.width = Mathf.Max(1, cameraDesc.width / ds);
                analysisColorDesc.height = Mathf.Max(1, cameraDesc.height / ds);
                analysisColorDesc.msaaSamples = 1;
                analysisColorDesc.depthBufferBits = 0;

                var analysisDepthDesc = analysisColorDesc;
                analysisDepthDesc.graphicsFormat = GraphicsFormat.None;
                analysisDepthDesc.depthStencilFormat = GraphicsFormat.D32_SFloat;
                analysisDepthDesc.depthBufferBits = 32;
                analysisDepthDesc.bindMS = false;

                RenderingUtils.ReAllocateIfNeeded(
                    ref analysisDepthRT,
                    analysisDepthDesc,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    name: "_ASCII_Analysis_Depth_RT"
                );

                var idDesc = analysisColorDesc;
                idDesc.graphicsFormat = GraphicsFormat.R32_SFloat;

                RenderingUtils.ReAllocateIfNeeded(
                    ref virtualIdRT,
                    idDesc,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    name: "_ASCII_Virtual_Id_RT"
                );

                var maskDesc = analysisColorDesc;
                maskDesc.graphicsFormat = GraphicsFormat.R8_UNorm;

                RenderingUtils.ReAllocateIfNeeded(
                    ref projectionMaskRT,
                    maskDesc,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    name: "_ASCII_Projection_Mask_RT"
                );

                RenderingUtils.ReAllocateIfNeeded(
                    ref projectionIdRT,
                    idDesc,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    name: "_ASCII_Projection_Id_RT"
                );

                RenderingUtils.ReAllocateIfNeeded(
                    ref solidIdRT,
                    idDesc,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    name: "_ASCII_Solid_Id_RT"
                );

                LatestAnalysisWidth = analysisColorDesc.width;
                LatestAnalysisHeight = analysisColorDesc.height;
            }
            else
            {
                analysisDepthRT?.Release();
                analysisDepthRT = null;

                virtualIdRT?.Release();
                virtualIdRT = null;

                projectionMaskRT?.Release();
                projectionMaskRT = null;

                projectionIdRT?.Release();
                projectionIdRT = null;

                solidIdRT?.Release();
                solidIdRT = null;

                LatestAnalysisWidth = 0;
                LatestAnalysisHeight = 0;
            }

            ConfigureInput(ScriptableRenderPassInput.Depth);

            LatestVirtualIdRT = virtualIdRT;
            LatestProjectionMaskRT = projectionMaskRT;
            LatestProjectionIdRT = projectionIdRT;
            LatestSolidIdRT = solidIdRT;
            LatestProjectionMaskOccluderLayers = settings.projectionMaskOccluderLayers;
            LatestProjectionFamilyLayers = settings.projectionLayer | settings.projectionDraggingLayer | settings.projectionInvalidLayer | settings.projectionInvalidWithSolidLayer;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (cameraColorTarget == null || cameraDepthTarget == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get("ASCII Dual Pass");

            using (new ProfilingScope(cmd, profiling))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var opaqueSorting = renderingData.cameraData.defaultOpaqueSortFlags;

                var solidOpaqueFiltering = new FilteringSettings(RenderQueueRange.opaque, settings.solidLayer);
                var solidTransparentFiltering = new FilteringSettings(RenderQueueRange.transparent, settings.solidLayer);

                var virtualOpaqueFiltering = new FilteringSettings(RenderQueueRange.opaque, settings.virtualLayer);
                var virtualTransparentFiltering = new FilteringSettings(RenderQueueRange.transparent, settings.virtualLayer);

                var projectionOpaqueFiltering = new FilteringSettings(RenderQueueRange.opaque, settings.projectionLayer);
                var projectionTransparentFiltering = new FilteringSettings(RenderQueueRange.transparent, settings.projectionLayer);
                var projectionDraggingOpaqueFiltering = new FilteringSettings(RenderQueueRange.opaque, settings.projectionDraggingLayer);
                var projectionDraggingTransparentFiltering = new FilteringSettings(RenderQueueRange.transparent, settings.projectionDraggingLayer);
                var projectionInvalidOpaqueFiltering = new FilteringSettings(RenderQueueRange.opaque, settings.projectionInvalidLayer);
                var projectionInvalidTransparentFiltering = new FilteringSettings(RenderQueueRange.transparent, settings.projectionInvalidLayer);
                var projectionSolidInvalidOpaqueFiltering = new FilteringSettings(RenderQueueRange.opaque, settings.projectionInvalidWithSolidLayer);
                var projectionSolidInvalidTransparentFiltering = new FilteringSettings(RenderQueueRange.transparent, settings.projectionInvalidWithSolidLayer);

                int projectionFamilyLayerMask =
                    settings.projectionLayer.value |
                    settings.projectionDraggingLayer.value |
                    settings.projectionInvalidLayer.value |
                    settings.projectionInvalidWithSolidLayer.value;

                var projectionFamilyOpaqueFiltering = new FilteringSettings(RenderQueueRange.opaque, projectionFamilyLayerMask);
                var projectionFamilyTransparentFiltering = new FilteringSettings(RenderQueueRange.transparent, projectionFamilyLayerMask);

                var occluderOpaqueFiltering = new FilteringSettings(RenderQueueRange.opaque, settings.projectionMaskOccluderLayers);
                var occluderTransparentFiltering = new FilteringSettings(RenderQueueRange.transparent, settings.projectionMaskOccluderLayers);

                // A. Virtual 原始来源图（全分辨率）
                CoreUtils.SetRenderTarget(cmd, virtualLitRT, cameraDepthTarget, ClearFlag.Color, Color.clear);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var litOpaqueDrawing = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
                context.DrawRenderers(renderingData.cullResults, ref litOpaqueDrawing, ref virtualOpaqueFiltering);

                if (settings.renderTransparentsToo)
                {
                    var litTransparentDrawing = RenderingUtils.CreateDrawingSettings(
                        shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                    context.DrawRenderers(renderingData.cullResults, ref litTransparentDrawing, ref virtualTransparentFiltering);
                }

                if (settings.outputAnalysisRTs && analysisDepthRT != null)
                {
                    // B. VirtualIdRT
                    if (virtualIdRT != null && projectionIdMaterial != null)
                    {
                        CoreUtils.SetRenderTarget(cmd, virtualIdRT, analysisDepthRT, ClearFlag.All, Color.clear);
                        context.ExecuteCommandBuffer(cmd);
                        cmd.Clear();

                        var idOpaqueDrawing = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
                        idOpaqueDrawing.overrideMaterial = projectionIdMaterial;
                        idOpaqueDrawing.overrideMaterialPassIndex = 0;
                        context.DrawRenderers(renderingData.cullResults, ref idOpaqueDrawing, ref virtualOpaqueFiltering);

                        if (settings.renderTransparentsToo)
                        {
                            var idTransparentDrawing = RenderingUtils.CreateDrawingSettings(
                                shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                            idTransparentDrawing.overrideMaterial = projectionIdMaterial;
                            idTransparentDrawing.overrideMaterialPassIndex = 0;
                            context.DrawRenderers(renderingData.cullResults, ref idTransparentDrawing, ref virtualTransparentFiltering);
                        }
                    }

                    // C. ProjectionMaskRT：先把遮挡层写入 analysisDepthRT
                    if (projectionMaskRT != null && maskMaterial != null)
                    {
                        CoreUtils.SetRenderTarget(cmd, projectionMaskRT, analysisDepthRT, ClearFlag.All, Color.clear);
                        context.ExecuteCommandBuffer(cmd);
                        cmd.Clear();

                        if (settings.projectionMaskOccluderLayers.value != 0)
                        {
                            var occOpaqueDrawing = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
                            context.DrawRenderers(renderingData.cullResults, ref occOpaqueDrawing, ref occluderOpaqueFiltering);

                            if (settings.renderTransparentsToo)
                            {
                                var occTransparentDrawing = RenderingUtils.CreateDrawingSettings(
                                    shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                                context.DrawRenderers(renderingData.cullResults, ref occTransparentDrawing, ref occluderTransparentFiltering);
                            }
                        }

                        CoreUtils.SetRenderTarget(cmd, projectionMaskRT, analysisDepthRT, ClearFlag.Color, Color.clear);
                        context.ExecuteCommandBuffer(cmd);
                        cmd.Clear();

                        var maskOpaqueDrawing = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
                        maskOpaqueDrawing.overrideMaterial = maskMaterial;
                        maskOpaqueDrawing.overrideMaterialPassIndex = 0;
                        context.DrawRenderers(renderingData.cullResults, ref maskOpaqueDrawing, ref projectionFamilyOpaqueFiltering);

                        if (settings.renderTransparentsToo)
                        {
                            var maskTransparentDrawing = RenderingUtils.CreateDrawingSettings(
                                shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                            maskTransparentDrawing.overrideMaterial = maskMaterial;
                            maskTransparentDrawing.overrideMaterialPassIndex = 0;
                            context.DrawRenderers(renderingData.cullResults, ref maskTransparentDrawing, ref projectionFamilyTransparentFiltering);
                        }
                    }

                    // D. ProjectionIdRT：显示所有 Projection 对象 ID，不做遮挡裁剪
                    if (projectionIdRT != null && projectionIdMaterial != null)
                    {
                        CoreUtils.SetRenderTarget(cmd, projectionIdRT, analysisDepthRT, ClearFlag.All, Color.clear);
                        context.ExecuteCommandBuffer(cmd);
                        cmd.Clear();

                        var projIdOpaqueDrawing = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
                        projIdOpaqueDrawing.overrideMaterial = projectionIdMaterial;
                        projIdOpaqueDrawing.overrideMaterialPassIndex = 0;
                        context.DrawRenderers(renderingData.cullResults, ref projIdOpaqueDrawing, ref projectionFamilyOpaqueFiltering);

                        if (settings.renderTransparentsToo)
                        {
                            var projIdTransparentDrawing = RenderingUtils.CreateDrawingSettings(
                                shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                            projIdTransparentDrawing.overrideMaterial = projectionIdMaterial;
                            projIdTransparentDrawing.overrideMaterialPassIndex = 0;
                            context.DrawRenderers(renderingData.cullResults, ref projIdTransparentDrawing, ref projectionFamilyTransparentFiltering);
                        }
                    }

                    // E. SolidIdRT：显示所有 Solid 对象 ID
                    if (solidIdRT != null && projectionIdMaterial != null)
                    {
                        CoreUtils.SetRenderTarget(cmd, solidIdRT, analysisDepthRT, ClearFlag.All, Color.clear);
                        context.ExecuteCommandBuffer(cmd);
                        cmd.Clear();

                        var solidIdOpaqueDrawing = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
                        solidIdOpaqueDrawing.overrideMaterial = projectionIdMaterial;
                        solidIdOpaqueDrawing.overrideMaterialPassIndex = 0;
                        context.DrawRenderers(renderingData.cullResults, ref solidIdOpaqueDrawing, ref solidOpaqueFiltering);

                        if (settings.renderTransparentsToo)
                        {
                            var solidIdTransparentDrawing = RenderingUtils.CreateDrawingSettings(
                                shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                            solidIdTransparentDrawing.overrideMaterial = projectionIdMaterial;
                            solidIdTransparentDrawing.overrideMaterialPassIndex = 0;
                            context.DrawRenderers(renderingData.cullResults, ref solidIdTransparentDrawing, ref solidTransparentFiltering);
                        }
                    }
                }

                // F. 绘制 Virtual
                if (settings.virtualOverrideMaterial != null)
                {
                    cmd.SetGlobalTexture(LitAsciiTexID, virtualLitRT);
                    CoreUtils.SetRenderTarget(cmd, cameraColorTarget, cameraDepthTarget);
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    var finalVirtualOpaque = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
                    finalVirtualOpaque.overrideMaterial = settings.virtualOverrideMaterial;
                    finalVirtualOpaque.overrideMaterialPassIndex = 0;
                    context.DrawRenderers(renderingData.cullResults, ref finalVirtualOpaque, ref virtualOpaqueFiltering);

                    if (settings.renderTransparentsToo)
                    {
                        var finalVirtualTransparent = RenderingUtils.CreateDrawingSettings(
                            shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                        finalVirtualTransparent.overrideMaterial = settings.virtualOverrideMaterial;
                        finalVirtualTransparent.overrideMaterialPassIndex = 0;
                        context.DrawRenderers(renderingData.cullResults, ref finalVirtualTransparent, ref virtualTransparentFiltering);
                    }
                }

                // G. 绘制 Projection / ProjectionDragging / ProjectionInvalid
                if (projectionMaskRT != null &&
                    (settings.projectionOverrideMaterial != null ||
                     settings.projectionDraggingOverrideMaterial != null ||
                     settings.projectionInvalidOverrideMaterial != null ||
                     settings.projectionInvalidWithSolidOverrideMaterial != null))
                {
                    cmd.SetGlobalTexture(AsciiMaskTexID, projectionMaskRT);
                    CoreUtils.SetRenderTarget(cmd, cameraColorTarget, cameraDepthTarget);
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    if (settings.projectionOverrideMaterial != null)
                    {
                        var finalProjectionOpaque = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
                        finalProjectionOpaque.overrideMaterial = settings.projectionOverrideMaterial;
                        finalProjectionOpaque.overrideMaterialPassIndex = 0;
                        context.DrawRenderers(renderingData.cullResults, ref finalProjectionOpaque, ref projectionOpaqueFiltering);

                        if (settings.renderTransparentsToo)
                        {
                            var finalProjectionTransparent = RenderingUtils.CreateDrawingSettings(
                                shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                            finalProjectionTransparent.overrideMaterial = settings.projectionOverrideMaterial;
                            finalProjectionTransparent.overrideMaterialPassIndex = 0;
                            context.DrawRenderers(renderingData.cullResults, ref finalProjectionTransparent, ref projectionTransparentFiltering);
                        }
                    }

                    if (settings.projectionDraggingOverrideMaterial != null)
                    {
                        var finalProjectionDraggingOpaque = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
                        finalProjectionDraggingOpaque.overrideMaterial = settings.projectionDraggingOverrideMaterial;
                        finalProjectionDraggingOpaque.overrideMaterialPassIndex = 0;
                        context.DrawRenderers(renderingData.cullResults, ref finalProjectionDraggingOpaque, ref projectionDraggingOpaqueFiltering);

                        if (settings.renderTransparentsToo)
                        {
                            var finalProjectionDraggingTransparent = RenderingUtils.CreateDrawingSettings(
                                shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                            finalProjectionDraggingTransparent.overrideMaterial = settings.projectionDraggingOverrideMaterial;
                            finalProjectionDraggingTransparent.overrideMaterialPassIndex = 0;
                            context.DrawRenderers(renderingData.cullResults, ref finalProjectionDraggingTransparent, ref projectionDraggingTransparentFiltering);
                        }
                    }

                    if (settings.projectionInvalidWithSolidOverrideMaterial != null)
                    {
                        var finalProjectionSolidInvalidOpaque = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
                        finalProjectionSolidInvalidOpaque.overrideMaterial = settings.projectionInvalidWithSolidOverrideMaterial;
                        finalProjectionSolidInvalidOpaque.overrideMaterialPassIndex = 0;
                        context.DrawRenderers(renderingData.cullResults, ref finalProjectionSolidInvalidOpaque, ref projectionSolidInvalidOpaqueFiltering);

                        if (settings.renderTransparentsToo)
                        {
                            var finalProjectionSolidInvalidTransparent = RenderingUtils.CreateDrawingSettings(
                                shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                            finalProjectionSolidInvalidTransparent.overrideMaterial = settings.projectionInvalidWithSolidOverrideMaterial;
                            finalProjectionSolidInvalidTransparent.overrideMaterialPassIndex = 0;
                            context.DrawRenderers(renderingData.cullResults, ref finalProjectionSolidInvalidTransparent, ref projectionSolidInvalidTransparentFiltering);
                        }
                    }

                    // projectionInvalidLayer 最后绘制；若材质关闭 _UseAsciiMask 并设置 ZTest Always / ZWrite Off，可实现屏幕最上层显示。
                    if (settings.projectionInvalidOverrideMaterial != null)
                    {
                        var finalProjectionInvalidOpaque = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
                        finalProjectionInvalidOpaque.overrideMaterial = settings.projectionInvalidOverrideMaterial;
                        finalProjectionInvalidOpaque.overrideMaterialPassIndex = 0;
                        context.DrawRenderers(renderingData.cullResults, ref finalProjectionInvalidOpaque, ref projectionInvalidOpaqueFiltering);

                        if (settings.renderTransparentsToo)
                        {
                            var finalProjectionInvalidTransparent = RenderingUtils.CreateDrawingSettings(
                                shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
                            finalProjectionInvalidTransparent.overrideMaterial = settings.projectionInvalidOverrideMaterial;
                            finalProjectionInvalidTransparent.overrideMaterialPassIndex = 0;
                            context.DrawRenderers(renderingData.cullResults, ref finalProjectionInvalidTransparent, ref projectionInvalidTransparentFiltering);
                        }
                    }
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
// using System;
// using System.Collections.Generic;
// using UnityEngine;
// using UnityEngine.Rendering;
// using UnityEngine.Rendering.Universal;
// using UnityEngine.Experimental.Rendering;

// public class ASCIIDualPassRendererFeature : ScriptableRendererFeature
// {
//     [Serializable]
//     public class Settings
//     {
//         [Header("Layers（必须只勾一个 Layer）")]
//         public LayerMask solidLayer;
//         public LayerMask virtualLayer;
//         public LayerMask projectionLayer;
//         public LayerMask projectionDraggingLayer;
//         public LayerMask projectionInvalidLayer;
//         public LayerMask projectionInvalidWithSolidLayer;

//         [Header("Projection Mask Occluders")]
//         [Tooltip("这些 Layer 上的物体会遮挡 ProjectionMaskRT，例如 Default、Solid。")]
//         public LayerMask projectionMaskOccluderLayers;

//         [Header("Pass Event")]
//         public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingOpaques;

//         [Header("Override Materials")]
//         public Material virtualOverrideMaterial;
//         public Material projectionOverrideMaterial;
//         public Material projectionDraggingOverrideMaterial;
//         public Material projectionInvalidOverrideMaterial;
//         public Material projectionInvalidWithSolidOverrideMaterial;

//         [Header("Internal Shaders")]
//         public Shader maskShader;
//         public Shader projectionIdShader;

//         [Header("Rendering")]
//         public bool renderTransparentsToo = true;

//         [Header("Analysis")]
//         public bool outputAnalysisRTs = true;

//         [Tooltip("分析 RT 降采样。1=全分辨率，2=半分辨率，4=四分之一分辨率。")]
//         [Min(1)] public int analysisDownsample = 2;
//     }

//     public Settings settings = new Settings();

//     private RenderAsciiDualPass pass;
//     private Material maskMaterial;
//     private Material projectionIdMaterial;

//     public static RTHandle LatestVirtualIdRT { get; private set; }
//     public static RTHandle LatestProjectionMaskRT { get; private set; }
//     public static RTHandle LatestProjectionIdRT { get; private set; }
//     public static RTHandle LatestSolidIdRT { get; private set; }
//     public static int LatestAnalysisWidth { get; private set; }
//     public static int LatestAnalysisHeight { get; private set; }
//     public static LayerMask LatestProjectionMaskOccluderLayers { get; private set; }
//     public static LayerMask LatestProjectionFamilyLayers { get; private set; }

//     public override void Create()
//     {
//         if (maskMaterial != null)
//             CoreUtils.Destroy(maskMaterial);

//         if (projectionIdMaterial != null)
//             CoreUtils.Destroy(projectionIdMaterial);

//         maskMaterial = settings.maskShader != null
//             ? CoreUtils.CreateEngineMaterial(settings.maskShader)
//             : null;

//         projectionIdMaterial = settings.projectionIdShader != null
//             ? CoreUtils.CreateEngineMaterial(settings.projectionIdShader)
//             : null;

//         pass = new RenderAsciiDualPass(settings, maskMaterial, projectionIdMaterial)
//         {
//             renderPassEvent = settings.passEvent
//         };
//     }

//     public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
//     {
//         if (pass == null)
//             return;

//         pass.Setup(renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
//     }

//     public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
//     {
//         if (pass == null)
//             return;

//         CameraType camType = renderingData.cameraData.cameraType;
//         if (camType != CameraType.Game && camType != CameraType.SceneView)
//             return;

//         renderer.EnqueuePass(pass);
//     }

//     protected override void Dispose(bool disposing)
//     {
//         if (maskMaterial != null)
//             CoreUtils.Destroy(maskMaterial);

//         if (projectionIdMaterial != null)
//             CoreUtils.Destroy(projectionIdMaterial);

//         pass?.Dispose();
//         pass = null;

//         LatestVirtualIdRT = null;
//         LatestProjectionMaskRT = null;
//         LatestProjectionIdRT = null;
//         LatestSolidIdRT = null;
//         LatestAnalysisWidth = 0;
//         LatestAnalysisHeight = 0;
//         LatestProjectionMaskOccluderLayers = 0;
//         LatestProjectionFamilyLayers = 0;
//     }

//     class RenderAsciiDualPass : ScriptableRenderPass
//     {
//         private readonly Settings settings;
//         private readonly Material maskMaterial;
//         private readonly Material projectionIdMaterial;

//         private RTHandle cameraColorTarget;
//         private RTHandle cameraDepthTarget;

//         private RTHandle virtualLitRT;
//         private RTHandle analysisDepthRT;
//         private RTHandle virtualIdRT;
//         private RTHandle projectionMaskRT;
//         private RTHandle projectionIdRT;
//         private RTHandle solidIdRT;

//         private readonly ProfilingSampler profiling = new ProfilingSampler("ASCII Dual Pass");

//         private readonly List<ShaderTagId> shaderTagIds = new List<ShaderTagId>
//         {
//             new ShaderTagId("UniversalForward"),
//             new ShaderTagId("UniversalForwardOnly"),
//             new ShaderTagId("SRPDefaultUnlit"),
//         };

//         private static readonly int LitAsciiTexID = Shader.PropertyToID("_LitAsciiSourceTex");
//         private static readonly int AsciiMaskTexID = Shader.PropertyToID("_AsciiMaskTex");

//         public RenderAsciiDualPass(Settings settings, Material maskMaterial, Material projectionIdMaterial)
//         {
//             this.settings = settings;
//             this.maskMaterial = maskMaterial;
//             this.projectionIdMaterial = projectionIdMaterial;
//         }

//         public void Setup(RTHandle colorTarget, RTHandle depthTarget)
//         {
//             cameraColorTarget = colorTarget;
//             cameraDepthTarget = depthTarget;
//         }

//         public void Dispose()
//         {
//             virtualLitRT?.Release();
//             virtualLitRT = null;

//             analysisDepthRT?.Release();
//             analysisDepthRT = null;

//             virtualIdRT?.Release();
//             virtualIdRT = null;

//             projectionMaskRT?.Release();
//             projectionMaskRT = null;

//             projectionIdRT?.Release();
//             projectionIdRT = null;

//             solidIdRT?.Release();
//             solidIdRT = null;
//         }

//         public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
//         {
//             var cameraDesc = renderingData.cameraData.cameraTargetDescriptor;
//             cameraDesc.depthBufferBits = 0;
//             cameraDesc.msaaSamples = 1;

//             RenderingUtils.ReAllocateIfNeeded(
//                 ref virtualLitRT,
//                 cameraDesc,
//                 FilterMode.Bilinear,
//                 TextureWrapMode.Clamp,
//                 name: "_ASCII_Virtual_Lit_RT"
//             );

//             if (settings.outputAnalysisRTs)
//             {
//                 int ds = Mathf.Max(1, settings.analysisDownsample);

//                 var analysisColorDesc = cameraDesc;
//                 analysisColorDesc.width = Mathf.Max(1, cameraDesc.width / ds);
//                 analysisColorDesc.height = Mathf.Max(1, cameraDesc.height / ds);
//                 analysisColorDesc.msaaSamples = 1;
//                 analysisColorDesc.depthBufferBits = 0;

//                 var analysisDepthDesc = analysisColorDesc;
//                 analysisDepthDesc.graphicsFormat = GraphicsFormat.None;
//                 analysisDepthDesc.depthStencilFormat = GraphicsFormat.D32_SFloat;
//                 analysisDepthDesc.depthBufferBits = 32;
//                 analysisDepthDesc.bindMS = false;

//                 RenderingUtils.ReAllocateIfNeeded(
//                     ref analysisDepthRT,
//                     analysisDepthDesc,
//                     FilterMode.Point,
//                     TextureWrapMode.Clamp,
//                     name: "_ASCII_Analysis_Depth_RT"
//                 );

//                 var idDesc = analysisColorDesc;
//                 idDesc.graphicsFormat = GraphicsFormat.R32_SFloat;

//                 RenderingUtils.ReAllocateIfNeeded(
//                     ref virtualIdRT,
//                     idDesc,
//                     FilterMode.Point,
//                     TextureWrapMode.Clamp,
//                     name: "_ASCII_Virtual_Id_RT"
//                 );

//                 var maskDesc = analysisColorDesc;
//                 maskDesc.graphicsFormat = GraphicsFormat.R8_UNorm;

//                 RenderingUtils.ReAllocateIfNeeded(
//                     ref projectionMaskRT,
//                     maskDesc,
//                     FilterMode.Point,
//                     TextureWrapMode.Clamp,
//                     name: "_ASCII_Projection_Mask_RT"
//                 );

//                 RenderingUtils.ReAllocateIfNeeded(
//                     ref projectionIdRT,
//                     idDesc,
//                     FilterMode.Point,
//                     TextureWrapMode.Clamp,
//                     name: "_ASCII_Projection_Id_RT"
//                 );

//                 RenderingUtils.ReAllocateIfNeeded(
//                     ref solidIdRT,
//                     idDesc,
//                     FilterMode.Point,
//                     TextureWrapMode.Clamp,
//                     name: "_ASCII_Solid_Id_RT"
//                 );

//                 LatestAnalysisWidth = analysisColorDesc.width;
//                 LatestAnalysisHeight = analysisColorDesc.height;
//             }
//             else
//             {
//                 analysisDepthRT?.Release();
//                 analysisDepthRT = null;

//                 virtualIdRT?.Release();
//                 virtualIdRT = null;

//                 projectionMaskRT?.Release();
//                 projectionMaskRT = null;

//                 projectionIdRT?.Release();
//                 projectionIdRT = null;

//                 solidIdRT?.Release();
//                 solidIdRT = null;

//                 LatestAnalysisWidth = 0;
//                 LatestAnalysisHeight = 0;
//             }

//             ConfigureInput(ScriptableRenderPassInput.Depth);

//             LatestVirtualIdRT = virtualIdRT;
//             LatestProjectionMaskRT = projectionMaskRT;
//             LatestProjectionIdRT = projectionIdRT;
//             LatestSolidIdRT = solidIdRT;
//             LatestProjectionMaskOccluderLayers = settings.projectionMaskOccluderLayers;
//             LatestProjectionFamilyLayers = settings.projectionLayer | settings.projectionDraggingLayer | settings.projectionInvalidLayer | settings.projectionInvalidWithSolidLayer;
//         }

//         public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
//         {
//             if (cameraColorTarget == null || cameraDepthTarget == null)
//                 return;

//             CommandBuffer cmd = CommandBufferPool.Get("ASCII Dual Pass");

//             using (new ProfilingScope(cmd, profiling))
//             {
//                 context.ExecuteCommandBuffer(cmd);
//                 cmd.Clear();

//                 var opaqueSorting = renderingData.cameraData.defaultOpaqueSortFlags;

//                 var solidOpaqueFiltering = new FilteringSettings(RenderQueueRange.opaque, settings.solidLayer);
//                 var solidTransparentFiltering = new FilteringSettings(RenderQueueRange.transparent, settings.solidLayer);

//                 var virtualOpaqueFiltering = new FilteringSettings(RenderQueueRange.opaque, settings.virtualLayer);
//                 var virtualTransparentFiltering = new FilteringSettings(RenderQueueRange.transparent, settings.virtualLayer);

//                 var projectionOpaqueFiltering = new FilteringSettings(RenderQueueRange.opaque, settings.projectionLayer);
//                 var projectionTransparentFiltering = new FilteringSettings(RenderQueueRange.transparent, settings.projectionLayer);
//                 var projectionDraggingOpaqueFiltering = new FilteringSettings(RenderQueueRange.opaque, settings.projectionDraggingLayer);
//                 var projectionDraggingTransparentFiltering = new FilteringSettings(RenderQueueRange.transparent, settings.projectionDraggingLayer);
//                 var projectionInvalidOpaqueFiltering = new FilteringSettings(RenderQueueRange.opaque, settings.projectionInvalidLayer);
//                 var projectionInvalidTransparentFiltering = new FilteringSettings(RenderQueueRange.transparent, settings.projectionInvalidLayer);
//                 var projectionSolidInvalidOpaqueFiltering = new FilteringSettings(RenderQueueRange.opaque, settings.projectionInvalidWithSolidLayer);

//                 int projectionFamilyLayerMask =
//                     settings.projectionLayer.value |
//                     settings.projectionDraggingLayer.value |
//                     settings.projectionInvalidLayer.value |
//                     settings.projectionInvalidWithSolidLayer.value;

//                 var projectionFamilyOpaqueFiltering = new FilteringSettings(RenderQueueRange.opaque, projectionFamilyLayerMask);
//                 var projectionFamilyTransparentFiltering = new FilteringSettings(RenderQueueRange.transparent, projectionFamilyLayerMask);

//                 var occluderOpaqueFiltering = new FilteringSettings(RenderQueueRange.opaque, settings.projectionMaskOccluderLayers);
//                 var occluderTransparentFiltering = new FilteringSettings(RenderQueueRange.transparent, settings.projectionMaskOccluderLayers);

//                 // A. Virtual 原始来源图（全分辨率）
//                 CoreUtils.SetRenderTarget(cmd, virtualLitRT, cameraDepthTarget, ClearFlag.Color, Color.clear);
//                 context.ExecuteCommandBuffer(cmd);
//                 cmd.Clear();

//                 var litOpaqueDrawing = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
//                 context.DrawRenderers(renderingData.cullResults, ref litOpaqueDrawing, ref virtualOpaqueFiltering);

//                 if (settings.renderTransparentsToo)
//                 {
//                     var litTransparentDrawing = RenderingUtils.CreateDrawingSettings(
//                         shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
//                     context.DrawRenderers(renderingData.cullResults, ref litTransparentDrawing, ref virtualTransparentFiltering);
//                 }

//                 if (settings.outputAnalysisRTs && analysisDepthRT != null)
//                 {
//                     // B. VirtualIdRT
//                     if (virtualIdRT != null && projectionIdMaterial != null)
//                     {
//                         CoreUtils.SetRenderTarget(cmd, virtualIdRT, analysisDepthRT, ClearFlag.All, Color.clear);
//                         context.ExecuteCommandBuffer(cmd);
//                         cmd.Clear();

//                         var idOpaqueDrawing = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
//                         idOpaqueDrawing.overrideMaterial = projectionIdMaterial;
//                         idOpaqueDrawing.overrideMaterialPassIndex = 0;
//                         context.DrawRenderers(renderingData.cullResults, ref idOpaqueDrawing, ref virtualOpaqueFiltering);

//                         if (settings.renderTransparentsToo)
//                         {
//                             var idTransparentDrawing = RenderingUtils.CreateDrawingSettings(
//                                 shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
//                             idTransparentDrawing.overrideMaterial = projectionIdMaterial;
//                             idTransparentDrawing.overrideMaterialPassIndex = 0;
//                             context.DrawRenderers(renderingData.cullResults, ref idTransparentDrawing, ref virtualTransparentFiltering);
//                         }
//                     }

//                     // C. ProjectionMaskRT：先把遮挡层写入 analysisDepthRT
//                     if (projectionMaskRT != null && maskMaterial != null)
//                     {
//                         CoreUtils.SetRenderTarget(cmd, projectionMaskRT, analysisDepthRT, ClearFlag.All, Color.clear);
//                         context.ExecuteCommandBuffer(cmd);
//                         cmd.Clear();

//                         if (settings.projectionMaskOccluderLayers.value != 0)
//                         {
//                             var occOpaqueDrawing = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
//                             context.DrawRenderers(renderingData.cullResults, ref occOpaqueDrawing, ref occluderOpaqueFiltering);

//                             if (settings.renderTransparentsToo)
//                             {
//                                 var occTransparentDrawing = RenderingUtils.CreateDrawingSettings(
//                                     shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
//                                 context.DrawRenderers(renderingData.cullResults, ref occTransparentDrawing, ref occluderTransparentFiltering);
//                             }
//                         }

//                         CoreUtils.SetRenderTarget(cmd, projectionMaskRT, analysisDepthRT, ClearFlag.Color, Color.clear);
//                         context.ExecuteCommandBuffer(cmd);
//                         cmd.Clear();

//                         var maskOpaqueDrawing = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
//                         maskOpaqueDrawing.overrideMaterial = maskMaterial;
//                         maskOpaqueDrawing.overrideMaterialPassIndex = 0;
//                         context.DrawRenderers(renderingData.cullResults, ref maskOpaqueDrawing, ref projectionFamilyOpaqueFiltering);

//                         if (settings.renderTransparentsToo)
//                         {
//                             var maskTransparentDrawing = RenderingUtils.CreateDrawingSettings(
//                                 shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
//                             maskTransparentDrawing.overrideMaterial = maskMaterial;
//                             maskTransparentDrawing.overrideMaterialPassIndex = 0;
//                             context.DrawRenderers(renderingData.cullResults, ref maskTransparentDrawing, ref projectionFamilyTransparentFiltering);
//                         }
//                     }

//                     // D. ProjectionIdRT：显示所有 Projection 对象 ID，不做遮挡裁剪
//                     if (projectionIdRT != null && projectionIdMaterial != null)
//                     {
//                         CoreUtils.SetRenderTarget(cmd, projectionIdRT, analysisDepthRT, ClearFlag.All, Color.clear);
//                         context.ExecuteCommandBuffer(cmd);
//                         cmd.Clear();

//                         var projIdOpaqueDrawing = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
//                         projIdOpaqueDrawing.overrideMaterial = projectionIdMaterial;
//                         projIdOpaqueDrawing.overrideMaterialPassIndex = 0;
//                         context.DrawRenderers(renderingData.cullResults, ref projIdOpaqueDrawing, ref projectionFamilyOpaqueFiltering);

//                         if (settings.renderTransparentsToo)
//                         {
//                             var projIdTransparentDrawing = RenderingUtils.CreateDrawingSettings(
//                                 shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
//                             projIdTransparentDrawing.overrideMaterial = projectionIdMaterial;
//                             projIdTransparentDrawing.overrideMaterialPassIndex = 0;
//                             context.DrawRenderers(renderingData.cullResults, ref projIdTransparentDrawing, ref projectionFamilyTransparentFiltering);
//                         }
//                     }

//                     // E. SolidIdRT：显示所有 Solid 对象 ID
//                     if (solidIdRT != null && projectionIdMaterial != null)
//                     {
//                         CoreUtils.SetRenderTarget(cmd, solidIdRT, analysisDepthRT, ClearFlag.All, Color.clear);
//                         context.ExecuteCommandBuffer(cmd);
//                         cmd.Clear();

//                         var solidIdOpaqueDrawing = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
//                         solidIdOpaqueDrawing.overrideMaterial = projectionIdMaterial;
//                         solidIdOpaqueDrawing.overrideMaterialPassIndex = 0;
//                         context.DrawRenderers(renderingData.cullResults, ref solidIdOpaqueDrawing, ref solidOpaqueFiltering);

//                         if (settings.renderTransparentsToo)
//                         {
//                             var solidIdTransparentDrawing = RenderingUtils.CreateDrawingSettings(
//                                 shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
//                             solidIdTransparentDrawing.overrideMaterial = projectionIdMaterial;
//                             solidIdTransparentDrawing.overrideMaterialPassIndex = 0;
//                             context.DrawRenderers(renderingData.cullResults, ref solidIdTransparentDrawing, ref solidTransparentFiltering);
//                         }
//                     }
//                 }

//                 // F. 绘制 Virtual
//                 if (settings.virtualOverrideMaterial != null)
//                 {
//                     cmd.SetGlobalTexture(LitAsciiTexID, virtualLitRT);
//                     CoreUtils.SetRenderTarget(cmd, cameraColorTarget, cameraDepthTarget);
//                     context.ExecuteCommandBuffer(cmd);
//                     cmd.Clear();

//                     var finalVirtualOpaque = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
//                     finalVirtualOpaque.overrideMaterial = settings.virtualOverrideMaterial;
//                     finalVirtualOpaque.overrideMaterialPassIndex = 0;
//                     context.DrawRenderers(renderingData.cullResults, ref finalVirtualOpaque, ref virtualOpaqueFiltering);

//                     if (settings.renderTransparentsToo)
//                     {
//                         var finalVirtualTransparent = RenderingUtils.CreateDrawingSettings(
//                             shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
//                         finalVirtualTransparent.overrideMaterial = settings.virtualOverrideMaterial;
//                         finalVirtualTransparent.overrideMaterialPassIndex = 0;
//                         context.DrawRenderers(renderingData.cullResults, ref finalVirtualTransparent, ref virtualTransparentFiltering);
//                     }
//                 }

//                 // G. 绘制 Projection / ProjectionDragging / ProjectionInvalid
//                 if (projectionMaskRT != null &&
//                     (settings.projectionOverrideMaterial != null ||
//                      settings.projectionDraggingOverrideMaterial != null ||
//                      settings.projectionInvalidOverrideMaterial != null ||
//                      settings.projectionInvalidWithSolidOverrideMaterial != null))
//                 {
//                     cmd.SetGlobalTexture(AsciiMaskTexID, projectionMaskRT);
//                     CoreUtils.SetRenderTarget(cmd, cameraColorTarget, cameraDepthTarget);
//                     context.ExecuteCommandBuffer(cmd);
//                     cmd.Clear();

//                     if (settings.projectionOverrideMaterial != null)
//                     {
//                         var finalProjectionOpaque = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
//                         finalProjectionOpaque.overrideMaterial = settings.projectionOverrideMaterial;
//                         finalProjectionOpaque.overrideMaterialPassIndex = 0;
//                         context.DrawRenderers(renderingData.cullResults, ref finalProjectionOpaque, ref projectionOpaqueFiltering);

//                         if (settings.renderTransparentsToo)
//                         {
//                             var finalProjectionTransparent = RenderingUtils.CreateDrawingSettings(
//                                 shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
//                             finalProjectionTransparent.overrideMaterial = settings.projectionOverrideMaterial;
//                             finalProjectionTransparent.overrideMaterialPassIndex = 0;
//                             context.DrawRenderers(renderingData.cullResults, ref finalProjectionTransparent, ref projectionTransparentFiltering);
//                         }
//                     }

//                     if (settings.projectionDraggingOverrideMaterial != null)
//                     {
//                         var finalProjectionDraggingOpaque = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
//                         finalProjectionDraggingOpaque.overrideMaterial = settings.projectionDraggingOverrideMaterial;
//                         finalProjectionDraggingOpaque.overrideMaterialPassIndex = 0;
//                         context.DrawRenderers(renderingData.cullResults, ref finalProjectionDraggingOpaque, ref projectionDraggingOpaqueFiltering);

//                         if (settings.renderTransparentsToo)
//                         {
//                             var finalProjectionDraggingTransparent = RenderingUtils.CreateDrawingSettings(
//                                 shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
//                             finalProjectionDraggingTransparent.overrideMaterial = settings.projectionDraggingOverrideMaterial;
//                             finalProjectionDraggingTransparent.overrideMaterialPassIndex = 0;
//                             context.DrawRenderers(renderingData.cullResults, ref finalProjectionDraggingTransparent, ref projectionDraggingTransparentFiltering);
//                         }
//                     }

//                     if (settings.projectionInvalidOverrideMaterial != null)
//                     {
//                         var finalProjectionInvalidOpaque = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
//                         finalProjectionInvalidOpaque.overrideMaterial = settings.projectionInvalidOverrideMaterial;
//                         finalProjectionInvalidOpaque.overrideMaterialPassIndex = 0;
//                         context.DrawRenderers(renderingData.cullResults, ref finalProjectionInvalidOpaque, ref projectionInvalidOpaqueFiltering);

//                         if (settings.renderTransparentsToo)
//                         {
//                             var finalProjectionInvalidTransparent = RenderingUtils.CreateDrawingSettings(
//                                 shaderTagIds, ref renderingData, SortingCriteria.CommonTransparent);
//                             finalProjectionInvalidTransparent.overrideMaterial = settings.projectionInvalidOverrideMaterial;
//                             finalProjectionInvalidTransparent.overrideMaterialPassIndex = 0;
//                             context.DrawRenderers(renderingData.cullResults, ref finalProjectionInvalidTransparent, ref projectionInvalidTransparentFiltering);
//                         }
//                     }

//                     if (settings.projectionInvalidWithSolidOverrideMaterial != null)
//                     {
//                         var finalProjectionSolidInvalidOpaque = RenderingUtils.CreateDrawingSettings(shaderTagIds, ref renderingData, opaqueSorting);
//                         finalProjectionSolidInvalidOpaque.overrideMaterial = settings.projectionInvalidWithSolidOverrideMaterial;
//                         finalProjectionSolidInvalidOpaque.overrideMaterialPassIndex = 0;
//                         context.DrawRenderers(renderingData.cullResults, ref finalProjectionSolidInvalidOpaque, ref projectionSolidInvalidOpaqueFiltering);
//                     }
//                 }
//             }

//             context.ExecuteCommandBuffer(cmd);
//             CommandBufferPool.Release(cmd);
//         }
//     }
// }