using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP RenderGraph render pass for the ASCII post-processing effect.
/// The RendererFeature owns the Material and Settings, the passes them into this RenderPass.
/// This class uses those resources to register RenderGraph work every frame.
/// </summary>
public sealed class ASCIIRenderPass : ScriptableRenderPass
{
    private const int CopyPass = 0;
    private const int LuminancePass = 1;
    private const int PackLuminancePass = 2;
    private const int HorizontalBlurPass = 3;
    private const int VerticalBlurDifferencePass = 4;
    private const int SobelHorizontalPass = 5;
    private const int SobelVerticalPass = 6;

    private const string ComputeKernelName = "CS_DrawEdges";

    // Cached shader property IDs.
    private static readonly int AsciiTexId = Shader.PropertyToID("_AsciiTex");
    private static readonly int LuminanceTexId = Shader.PropertyToID("_LuminanceTex");

    private static readonly int SigmaId = Shader.PropertyToID("_Sigma");
    private static readonly int KId = Shader.PropertyToID("_K");
    private static readonly int TauId = Shader.PropertyToID("_Tau");
    private static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
    private static readonly int GaussianKernelSizeId = Shader.PropertyToID("_GaussianKernelSize");
    private static readonly int InvertId = Shader.PropertyToID("_Invert");

    private static readonly int ViewUncompressedId = Shader.PropertyToID("_ViewUncompressed");
    private static readonly int DebugEdgesId = Shader.PropertyToID("_DebugEdges");
    private static readonly int GridId = Shader.PropertyToID("_Grid");
    private static readonly int NoEdgesId = Shader.PropertyToID("_NoEdges");
    private static readonly int NoFillId = Shader.PropertyToID("_NoFill");
    private static readonly int EdgeThresholdId = Shader.PropertyToID("_EdgeThreshold");
    private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
    private static readonly int AttenuationId = Shader.PropertyToID("_Attenuation");
    private static readonly int UseDownscaledColorId = Shader.PropertyToID("_UseDownscaledColor");

    private static readonly int ResultWidthId = Shader.PropertyToID("_ResultWidth");
    private static readonly int ResultHeightId = Shader.PropertyToID("_ResultHeight");

    // Material created and disposed by the RendererFeature.
    private readonly Material material;

    // Settings are owned by the RendererFeature and refreshed every frame in ASCIIRendererFeature.AddRenderPasses().
    private ASCIIRendererFeature.ASCIISettings settings;

    // For trackingwhether the user changed the atlas textures in the Inspector.
    // If the texture reference changes, we release the old RTHandle wrapper and allocate a new one.
    private Texture currentAsciiTex;
    private Texture currentEdgeTex;

    // RTHandle wrappers around external atlas textures.
    private RTHandle asciiTexRTHandle;
    private RTHandle edgeTexRTHandle;

    public ASCIIRenderPass(Material material, ASCIIRendererFeature.ASCIISettings settings)
    {
        this.material = material;
        this.settings = settings;
    }

    /// <summary>
    /// Updates the pass with the latest settings from the RendererFeature.
    /// This is called from ASCIIRendererFeature.AddRenderPasses()
    /// </summary>
    /// <param name="newSettings">Latest Inspector settings object.</param>
    public void SetSettings(ASCIIRendererFeature.ASCIISettings newSettings)
    {
        settings = newSettings;
    }

    /// <summary>
    /// Releases RTHandle wrappers created for imported external textures.
    /// </summary>
    public void Dispose()
    {
        ReleaseImportedTextureHandles();
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (material == null || settings == null || settings.asciiCompute == null)
            return;

        if (settings.asciiTex == null || settings.edgeTex == null)
            return;

        if (!SystemInfo.supportsComputeShaders)
            return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

        if (resourceData.isActiveTargetBackBuffer)
            return;

        TextureHandle cameraColor = resourceData.activeColorTexture;

        if (!cameraColor.IsValid())
            return;

        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

        RenderTextureDescriptor fullDesc = cameraData.cameraTargetDescriptor;
        fullDesc.msaaSamples = 1;
        fullDesc.depthBufferBits = 0;

        int width = Mathf.Max(1, fullDesc.width);
        int height = Mathf.Max(1, fullDesc.height);

        UpdateMaterialSettings();
        EnsureImportedTextureHandles();

        TextureHandle asciiTex = renderGraph.ImportTexture(asciiTexRTHandle);
        TextureHandle edgeTex = renderGraph.ImportTexture(edgeTexRTHandle);

        TextureHandle luminance = CreateTexture(
            renderGraph,
            fullDesc,
            "ASCII Luminance",
            GraphicsFormat.R16_SFloat,
            width,
            height,
            false
        );

        TextureHandle ping = CreateTexture(
            renderGraph,
            fullDesc,
            "ASCII Ping",
            fullDesc.graphicsFormat,
            width,
            height,
            false
        );

        TextureHandle dog = CreateTexture(
            renderGraph,
            fullDesc,
            "ASCII DoG",
            fullDesc.graphicsFormat,
            width,
            height,
            false
        );

        TextureHandle sobel = CreateTexture(
            renderGraph,
            fullDesc,
            "ASCII Sobel",
            fullDesc.graphicsFormat,
            width,
            height,
            false
        );

        TextureHandle downscale1 = CreateTexture(
            renderGraph,
            fullDesc,
            "ASCII Downscale 1",
            fullDesc.graphicsFormat,
            Mathf.Max(1, width / 2),
            Mathf.Max(1, height / 2),
            false
        );

        TextureHandle downscale2 = CreateTexture(
            renderGraph,
            fullDesc,
            "ASCII Downscale 2",
            fullDesc.graphicsFormat,
            Mathf.Max(1, width / 4),
            Mathf.Max(1, height / 4),
            false
        );

        TextureHandle downscale3 = CreateTexture(
            renderGraph,
            fullDesc,
            "ASCII Downscale 3",
            fullDesc.graphicsFormat,
            Mathf.Max(1, width / 8),
            Mathf.Max(1, height / 8),
            false
        );

        TextureHandle asciiResult = CreateTexture(
            renderGraph,
            fullDesc,
            "ASCII Result",
            fullDesc.graphicsFormat,
            width,
            height,
            true
        );

        AddBlitPass(renderGraph, cameraColor, luminance, LuminancePass, "ASCII - Luminance");

        AddBlitPass(renderGraph, luminance, ping, HorizontalBlurPass, "ASCII - Horizontal Blur");

        AddBlitPass(renderGraph, ping, dog, VerticalBlurDifferencePass, "ASCII - Vertical Blur + Difference");

        AddBlitPassWithExtraTexture(
            renderGraph,
            dog,
            ping,
            luminance,
            LuminanceTexId,
            SobelHorizontalPass,
            "ASCII - Sobel Horizontal"
        );

        AddBlitPass(renderGraph, ping, sobel, SobelVerticalPass, "ASCII - Sobel Vertical");

        AddBlitPassWithExtraTexture(
            renderGraph,
            cameraColor,
            ping,
            luminance,
            LuminanceTexId,
            PackLuminancePass,
            "ASCII - Pack Luminance"
        );

        AddBlitPass(renderGraph, ping, downscale1, CopyPass, "ASCII - Downscale 1");
        AddBlitPass(renderGraph, downscale1, downscale2, CopyPass, "ASCII - Downscale 2");
        AddBlitPass(renderGraph, downscale2, downscale3, CopyPass, "ASCII - Downscale 3");

        AddComputePass(
            renderGraph,
            sobel,
            downscale3,
            asciiResult,
            asciiTex,
            edgeTex,
            width,
            height
        );

        TextureHandle finalSource = asciiResult;

        if (settings.viewDog)
            finalSource = dog;

        if (settings.viewSobel)
            finalSource = sobel;

        AddBlitPass(renderGraph, finalSource, cameraColor, CopyPass, "ASCII - Final Output");
    }

    private void UpdateMaterialSettings()
    {
        material.SetTexture(AsciiTexId, settings.asciiTex);

        material.SetFloat(KId, settings.stdevScale);
        material.SetFloat(SigmaId, settings.stdev);
        material.SetFloat(TauId, settings.tau);
        material.SetFloat(ThresholdId, settings.threshold);

        material.SetInt(GaussianKernelSizeId, settings.gaussianKernelSize);
        material.SetInt(InvertId, settings.invert ? 1 : 0);
    }

    private void EnsureImportedTextureHandles()
    {
        if (currentAsciiTex != settings.asciiTex)
        {
            if (asciiTexRTHandle != null)
                RTHandles.Release(asciiTexRTHandle);

            asciiTexRTHandle = RTHandles.Alloc(settings.asciiTex);
            currentAsciiTex = settings.asciiTex;
        }

        if (currentEdgeTex != settings.edgeTex)
        {
            if (edgeTexRTHandle != null)
                RTHandles.Release(edgeTexRTHandle);

            edgeTexRTHandle = RTHandles.Alloc(settings.edgeTex);
            currentEdgeTex = settings.edgeTex;
        }
    }

    private void ReleaseImportedTextureHandles()
    {
        if (asciiTexRTHandle != null)
        {
            RTHandles.Release(asciiTexRTHandle);
            asciiTexRTHandle = null;
        }

        if (edgeTexRTHandle != null)
        {
            RTHandles.Release(edgeTexRTHandle);
            edgeTexRTHandle = null;
        }

        currentAsciiTex = null;
        currentEdgeTex = null;
    }

    private static TextureHandle CreateTexture(
        RenderGraph renderGraph,
        RenderTextureDescriptor baseDesc,
        string name,
        GraphicsFormat format,
        int width,
        int height,
        bool enableRandomWrite
    )
    {
        RenderTextureDescriptor desc = baseDesc;
        desc.width = width;
        desc.height = height;
        desc.depthBufferBits = 0;
        desc.msaaSamples = 1;
        desc.graphicsFormat = format;
        desc.enableRandomWrite = enableRandomWrite;

        return UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, name, false);
    }

    private void AddBlitPass(
        RenderGraph renderGraph,
        TextureHandle source,
        TextureHandle destination,
        int shaderPass,
        string passName
    )
    {
        AddBlitPassWithExtraTexture(
            renderGraph,
            source,
            destination,
            TextureHandle.nullHandle,
            0,
            shaderPass,
            passName
        );
    }

    private void AddBlitPassWithExtraTexture(
        RenderGraph renderGraph,
        TextureHandle source,
        TextureHandle destination,
        TextureHandle extraTexture,
        int extraTextureId,
        int shaderPass,
        string passName
    )
    {
        using IRasterRenderGraphBuilder builder =
            renderGraph.AddRasterRenderPass<BlitPassData>(passName, out BlitPassData passData);

        passData.source = source;
        passData.extraTexture = extraTexture;
        passData.extraTextureId = extraTextureId;
        passData.hasExtraTexture = extraTexture.IsValid();
        passData.material = material;
        passData.shaderPass = shaderPass;

        builder.UseTexture(source, AccessFlags.Read);

        if (passData.hasExtraTexture)
            builder.UseTexture(extraTexture, AccessFlags.Read);

        builder.SetRenderAttachment(destination, 0);

        if (passData.hasExtraTexture)
            builder.AllowGlobalStateModification(true);

        builder.SetRenderFunc(static (BlitPassData data, RasterGraphContext context) =>
        {
            if (data.hasExtraTexture)
                context.cmd.SetGlobalTexture(data.extraTextureId, data.extraTexture);

            Blitter.BlitTexture(
                context.cmd,
                data.source,
                new Vector4(1.0f, 1.0f, 0.0f, 0.0f),
                data.material,
                data.shaderPass
            );
        });
    }

    private void AddComputePass(
        RenderGraph renderGraph,
        TextureHandle sobel,
        TextureHandle luminanceDownscaled,
        TextureHandle result,
        TextureHandle asciiTex,
        TextureHandle edgeTex,
        int width,
        int height
    )
    {
        using IComputeRenderGraphBuilder builder =
            renderGraph.AddComputePass<ComputePassData>("ASCII - Compute Characters", out ComputePassData passData);

        int kernel = settings.asciiCompute.FindKernel(ComputeKernelName);

        passData.compute = settings.asciiCompute;
        passData.kernel = kernel;

        passData.sobel = sobel;
        passData.luminance = luminanceDownscaled;
        passData.result = result;
        passData.asciiTex = asciiTex;
        passData.edgeTex = edgeTex;

        passData.viewUncompressed = settings.viewUncompressedEdges ? 1 : 0;
        passData.debugEdges = settings.debugEdges ? 1 : 0;
        passData.grid = settings.viewGrid ? 1 : 0;
        passData.noEdges = settings.noEdges ? 1 : 0;
        passData.noFill = settings.noFill ? 1 : 0;

        passData.edgeThreshold = settings.edgeThreshold;
        passData.exposure = settings.exposure;
        passData.attenuation = settings.attenuation;
        passData.useDownscaledColor = settings.useDownscaledColor ? 1 : 0;

        passData.width = width;
        passData.height = height;

        builder.UseTexture(sobel, AccessFlags.Read);
        builder.UseTexture(luminanceDownscaled, AccessFlags.Read);
        builder.UseTexture(asciiTex, AccessFlags.Read);
        builder.UseTexture(edgeTex, AccessFlags.Read);
        builder.UseTexture(result, AccessFlags.Write);

        builder.SetRenderFunc(static (ComputePassData data, ComputeGraphContext context) =>
        {
            context.cmd.SetComputeTextureParam(data.compute, data.kernel, "_SobelTex", data.sobel);
            context.cmd.SetComputeTextureParam(data.compute, data.kernel, "_LuminanceTex", data.luminance);
            context.cmd.SetComputeTextureParam(data.compute, data.kernel, "_Result", data.result);
            context.cmd.SetComputeTextureParam(data.compute, data.kernel, "_AsciiTex", data.asciiTex);
            context.cmd.SetComputeTextureParam(data.compute, data.kernel, "_EdgeAsciiTex", data.edgeTex);

            context.cmd.SetComputeIntParam(data.compute, ViewUncompressedId, data.viewUncompressed);
            context.cmd.SetComputeIntParam(data.compute, DebugEdgesId, data.debugEdges);
            context.cmd.SetComputeIntParam(data.compute, GridId, data.grid);
            context.cmd.SetComputeIntParam(data.compute, NoEdgesId, data.noEdges);
            context.cmd.SetComputeIntParam(data.compute, NoFillId, data.noFill);
            context.cmd.SetComputeIntParam(data.compute, EdgeThresholdId, data.edgeThreshold);

            context.cmd.SetComputeFloatParam(data.compute, ExposureId, data.exposure);
            context.cmd.SetComputeFloatParam(data.compute, AttenuationId, data.attenuation);
            context.cmd.SetComputeIntParam(data.compute, UseDownscaledColorId, data.useDownscaledColor);

            context.cmd.SetComputeIntParam(data.compute, ResultWidthId, data.width);
            context.cmd.SetComputeIntParam(data.compute, ResultHeightId, data.height);

            int groupsX = Mathf.CeilToInt(data.width / 8.0f);
            int groupsY = Mathf.CeilToInt(data.height / 8.0f);

            context.cmd.DispatchCompute(data.compute, data.kernel, groupsX, groupsY, 1);
        });
    }

    private sealed class BlitPassData
    {
        public TextureHandle source;
        public TextureHandle extraTexture;
        public int extraTextureId;
        public bool hasExtraTexture;
        public Material material;
        public int shaderPass;
    }

    private sealed class ComputePassData
    {
        public ComputeShader compute;
        public int kernel;

        public TextureHandle sobel;
        public TextureHandle luminance;
        public TextureHandle result;
        public TextureHandle asciiTex;
        public TextureHandle edgeTex;

        public int viewUncompressed;
        public int debugEdges;
        public int grid;
        public int noEdges;
        public int noFill;

        public int edgeThreshold;
        public float exposure;
        public float attenuation;
        public int useDownscaledColor;

        public int width;
        public int height;
    }
}