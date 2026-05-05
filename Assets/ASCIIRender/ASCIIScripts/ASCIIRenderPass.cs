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
public class ASCIIRenderPass : ScriptableRenderPass
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
    private static readonly int FillColorId = Shader.PropertyToID("_FillColor");
    private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    private static readonly int UseSeparateEdgeColorId = Shader.PropertyToID("_UseSeparateEdgeColor");

    private static readonly int ResultWidthId = Shader.PropertyToID("_ResultWidth");
    private static readonly int ResultHeightId = Shader.PropertyToID("_ResultHeight");

    // Material created and disposed by the RendererFeature.
    private readonly Material material;

    // Settings are owned by the RendererFeature and refreshed every frame in ASCIIRendererFeature.AddRenderPasses().
    private ASCIIRendererFeature.ASCIISettings settings;

    // For tracking whether the user changed the atlas textures in the Inspector.
    // If the texture reference changes, we release the old RTHandle wrapper and allocate a new one.
    private Texture currentAsciiTex;
    private Texture currentEdgeTex;

    // RTHandle wrappers around external atlas textures.
    private RTHandle asciiTexRTHandle;
    private RTHandle edgeTexRTHandle;



    /// <summary>
    /// Data package for a raster blit RenderGraph pass.
    /// </summary>
    private class BlitPassData
    {
        public TextureHandle source;
        public TextureHandle extraTextureInput;
        public int extraTextureId;
        public bool hasExtraTextureInput;
        public Material material;
        public int shaderPass;
    }

    /// <summary>
    /// Data package for the ASCII compute RenderGraph pass.
    /// </summary>
    private class ComputePassData
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
        public int useSeparateEdgeColor;
        public Vector4 fillColor;
        public Vector4 edgeColor;

        public int width;
        public int height;
    }


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
    /// Releases RTHandle wrappers for imported atlas textures.
    /// This is called when the RendererFeature / RenderPass is disposed.
    /// </summary>
    public void ReleaseAtlasRTHandles()
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





    /// ================================= main URP entry ========================================
    /// <summary>
    /// Declares temporary textures and registers raster / compute passes.
    /// </summary>
    /// <param name="renderGraph">Use the current frame's RenderGraph to create textures and add passes.</param>
    /// <param name="frameData">URP frame context. Use it to access camera color, descriptor and other resources.</param>
    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (material == null || settings == null || settings.asciiCompute == null)
            return;

        if (settings.asciiTex == null || settings.edgeTex == null)
            return;

        if (!SystemInfo.supportsComputeShaders)
            return;

        // UniversalResourceData contains current frame render targets.
        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

        if (resourceData.isActiveTargetBackBuffer)
            return;

        // Current camera color texture.
        // This is the full-screen image produced by URP so far.
        TextureHandle cameraColor = resourceData.activeColorTexture;

        if (!cameraColor.IsValid())
            return;

        // UniversalCameraData contains camera resolution and target texture descriptor.
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

        // Copy the camera descriptor as a base for temporary textures.
        RenderTextureDescriptor fullDesc = cameraData.cameraTargetDescriptor;
        fullDesc.msaaSamples = 1;
        fullDesc.depthBufferBits = 0;

        int width = Mathf.Max(1, fullDesc.width);
        int height = Mathf.Max(1, fullDesc.height);

        UpdateSettingsToMaterial();
        EnsureAtlasRTHandles();

        TextureHandle asciiTex = renderGraph.ImportTexture(asciiTexRTHandle);
        TextureHandle edgeTex = renderGraph.ImportTexture(edgeTexRTHandle);

        TextureHandle luminance = CreateRenderGraphTexture(
            renderGraph,
            fullDesc,
            "_ASCII_LuminanceTex",
            GraphicsFormat.R16_SFloat,
            width,
            height,
            false
        );

        TextureHandle ping = CreateRenderGraphTexture(
            renderGraph,
            fullDesc,
            "_ASCII_PingTex",
            fullDesc.graphicsFormat,
            width,
            height,
            false
        );

        TextureHandle dog = CreateRenderGraphTexture(
            renderGraph,
            fullDesc,
            "_ASCII_DoGTex",
            fullDesc.graphicsFormat,
            width,
            height,
            false
        );

        TextureHandle sobel = CreateRenderGraphTexture(
            renderGraph,
            fullDesc,
            "_ASCII_SobelTex",
            fullDesc.graphicsFormat,
            width,
            height,
            false
        );

        TextureHandle downscale1 = CreateRenderGraphTexture(
            renderGraph,
            fullDesc,
            "_ASCII_DownscaleHalfTex",
            fullDesc.graphicsFormat,
            Mathf.Max(1, width / 2),
            Mathf.Max(1, height / 2),
            false
        );

        TextureHandle downscale2 = CreateRenderGraphTexture(
            renderGraph,
            fullDesc,
            "_ASCII_DownscaleQuarterTex",
            fullDesc.graphicsFormat,
            Mathf.Max(1, width / 4),
            Mathf.Max(1, height / 4),
            false
        );

        TextureHandle downscale3 = CreateRenderGraphTexture(
            renderGraph,
            fullDesc,
            "_ASCII_DownscaleEighthTex",
            fullDesc.graphicsFormat,
            Mathf.Max(1, width / 8),
            Mathf.Max(1, height / 8),
            false
        );

        TextureHandle asciiResult = CreateRenderGraphTexture(
            renderGraph,
            fullDesc,
            "_ASCII_ResultTex",
            fullDesc.graphicsFormat,
            width,
            height,
            true
        );

        AddBlitPass(renderGraph, cameraColor, luminance, LuminancePass, "RG_ASCII_LuminanceExtract");

        AddBlitPass(renderGraph, luminance, ping, HorizontalBlurPass, "RG_ASCII_GaussianBlurHorizontal");

        AddBlitPass(renderGraph, ping, dog, VerticalBlurDifferencePass, "RG_ASCII_GaussianBlurVertical_DoG");

        AddBlitPassWithExtraTextureInput(
            renderGraph,
            dog,
            ping,
            luminance,
            LuminanceTexId,
            SobelHorizontalPass,
            "RG_ASCII_SobelHorizontal"
        );

        AddBlitPass(renderGraph, ping, sobel, SobelVerticalPass, "RG_ASCII_SobelVertical");

        AddBlitPassWithExtraTextureInput(
            renderGraph,
            cameraColor,
            ping,
            luminance,
            LuminanceTexId,
            PackLuminancePass,
            "RG_ASCII_PackColorLuminance"
        );

        AddBlitPass(renderGraph, ping, downscale1, CopyPass, "RG_ASCII_DownscaleHalf");
        AddBlitPass(renderGraph, downscale1, downscale2, CopyPass, "RG_ASCII_DownscaleQuarter");
        AddBlitPass(renderGraph, downscale2, downscale3, CopyPass, "RG_ASCII_DownscaleEighth");

        AddComputePass(
            renderGraph,
            sobel,
            downscale3,
            asciiResult,
            asciiTex,
            edgeTex,
            width,
            height,
            "RG_ASCIICompute_AtlasMapping"
        );

        TextureHandle finalSource = asciiResult;

        if (settings.viewDog)
            finalSource = dog;

        if (settings.viewSobel)
            finalSource = sobel;

        AddBlitPass(renderGraph, finalSource, cameraColor, CopyPass, "RG_ASCII_CopyComputeToCamera");
    }





    /// ================================= settings / resource helpers ========================================
    /// <summary>
    /// Copies Inspector settings into the full-screen Material.
    /// Settings live in C# on the RendererFeature.
    /// This function bridges those two layers by writing values into the Material.
    /// </summary>
    private void UpdateSettingsToMaterial()
    {
        material.SetTexture(AsciiTexId, settings.asciiTex);

        material.SetFloat(KId, settings.stdevScale);
        material.SetFloat(SigmaId, settings.stdev);
        material.SetFloat(TauId, settings.tau);
        material.SetFloat(ThresholdId, settings.threshold);

        material.SetInt(GaussianKernelSizeId, settings.gaussianKernelSize);
        material.SetInt(InvertId, settings.invert ? 1 : 0);
    }

    /// <summary>
    /// Ensures that external atlas textures have RTHandle wrappers.
    /// </summary>
    private void EnsureAtlasRTHandles()
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

    /// <summary>
    /// Creates a temporary RenderGraph texture from a base camera descriptor.
    /// This helper centralizes the common descriptor edits needed by intermediate textures:
    /// custom width/height, custom graphics format, no depth buffer, no MSAA, and optional
    /// random write support for compute shader outputs.
    /// </summary>
    /// <param name="renderGraph"></param>
    /// <param name="baseDesc"></param>
    /// <param name="name">Debug name visible in Frame Debugger / RenderGraph tools.</param>
    /// <param name="format">Graphics format used to store pixel data.</param>
    /// <param name="width"></param>
    /// <param name="height"></param>
    /// <param name="enableRandomWrite">True if a compute shader needs UAV/random-write access.</param>
    /// <returns></returns>
    private static TextureHandle CreateRenderGraphTexture(
        RenderGraph renderGraph,
        RenderTextureDescriptor baseDesc,
        string textureName,
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

        // The final false means this texture is not explicitly cleared on creation.
        // This is safe because every pass using these textures fully overwrites them.
        return UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, textureName, false);
    }





    /// ================================= RenderGraph pass helpers ========================================
    /// <summary>
    /// Adds a simple full-screen blit pass with one source texture and one destination texture.
    /// </summary>
    /// <param name="renderGraph"></param>
    /// <param name="source"></param>
    /// <param name="destination"></param>
    /// <param name="shaderPass"></param>
    /// <param name="passName"></param>
    private void AddBlitPass(
        RenderGraph renderGraph,
        TextureHandle source,
        TextureHandle destination,
        int shaderPass,
        string passName
    )
    {
        AddBlitPassWithExtraTextureInput(
            renderGraph,
            source,
            destination,
            TextureHandle.nullHandle,
            0,
            shaderPass,
            passName
        );
    }

    /// <summary>
    /// Adds a full-screen raster blit pass, optionally with a second input texture.
    /// The main source texture is passed through Blitter.BlitTexture().
    /// The optional extra texture is bound manually as a global shader texture.
    /// </summary>
    /// <param name="renderGraph">Current frame RenderGraph.</param>
    /// <param name="source">Main input texture for Blitter.BlitTexture().</param>
    /// <param name="destination">Output render target.</param>
    /// <param name="extraTexture">Optional second texture sampled by the shader pass.</param>
    /// <param name="extraTextureId">Shader property ID used to bind the extra texture.</param>
    /// <param name="shaderPass">Material shader pass index to execute.</param>
    /// <param name="passName">Debug name for this RenderGraph pass.</param>
    private void AddBlitPassWithExtraTextureInput(
        RenderGraph renderGraph,
        TextureHandle source,
        TextureHandle destination,
        TextureHandle extraTextureInput,
        int extraTextureId,
        int shaderPass,
        string passName
    )
    {
        // AddRasterRenderPass creates a raster pass and returns a builder.
        // The builder configures resource dependencies and the render function.
        using IRasterRenderGraphBuilder builder =
            renderGraph.AddRasterRenderPass<BlitPassData>(passName, out BlitPassData passData);

        // PassData stores values needed later when RenderGraph executes this pass.
        passData.source = source;
        passData.extraTextureInput = extraTextureInput;
        passData.extraTextureId = extraTextureId;
        passData.hasExtraTextureInput = extraTextureInput.IsValid();
        passData.material = material;
        passData.shaderPass = shaderPass;

        // Tell RenderGraph that this pass reads the main source texture.
        builder.UseTexture(passData.source, AccessFlags.Read);

        // If the shader samples a second texture, RenderGraph must know it is also read.
        if (passData.hasExtraTextureInput)
            builder.UseTexture(passData.extraTextureInput, AccessFlags.Read);

        // Tell RenderGraph that this pass writes to destination.
        builder.SetRenderAttachment(destination, 0);

        // SetGlobalTexture modifies global shader state, so RenderGraph needs permission.
        if (passData.hasExtraTextureInput)
            builder.AllowGlobalStateModification(true);

        // This function is executed later by RenderGraph.
        builder.SetRenderFunc(static (BlitPassData data, RasterGraphContext context) =>
        {
            // Bind the optional second texture to the shader before blitting.
            if (data.hasExtraTextureInput)
                context.cmd.SetGlobalTexture(data.extraTextureId, data.extraTextureInput);

            // Record a full-screen blit command.
            Blitter.BlitTexture(
                context.cmd,    // Command buffer to record into, provided by RenderGraph.
                data.source,

                new Vector4(1.0f, 1.0f, 0.0f, 0.0f),    // Source UV scale/bias: xy scale, zw offset.

                data.material,
                data.shaderPass
            );
        });
    }

    /// <summary>
    /// Adds the compute pass that selects ASCII glyphs and writes the final ASCII image.
    /// </summary>
    /// <param name="renderGraph"></param>
    /// <param name="sobel">Sobel edge texture.</param>
    /// <param name="luminanceDownscaled">Downscaled luminance/color texture.</param>
    /// <param name="result">Compute output texture.</param>
    /// <param name="asciiTex">Imported fill ASCII atlas texture.</param>
    /// <param name="edgeTex">Imported edge ASCII atlas texture.</param>
    /// <param name="width"></param>
    /// <param name="height"></param>
    private void AddComputePass(
        RenderGraph renderGraph,
        TextureHandle sobel,
        TextureHandle luminanceDownscaled,
        TextureHandle result,
        TextureHandle asciiTex,
        TextureHandle edgeTex,
        int width,
        int height,
        string passName
    )
    {
        using IComputeRenderGraphBuilder builder =
            renderGraph.AddComputePass<ComputePassData>(passName, out ComputePassData passData);

        // Find the compute kernel once for this pass registration.
        int kernel = settings.asciiCompute.FindKernel(ComputeKernelName);

        // Store compute shader and kernel for execution.
        passData.compute = settings.asciiCompute;
        passData.kernel = kernel;

        // Store all textures needed by the compute shader.
        passData.sobel = sobel;
        passData.luminance = luminanceDownscaled;
        passData.result = result;
        passData.asciiTex = asciiTex;
        passData.edgeTex = edgeTex;

        // Store debug toggles as ints because compute shader parameters use int values.
        passData.viewUncompressed = settings.viewUncompressedEdges ? 1 : 0;
        passData.debugEdges = settings.debugEdges ? 1 : 0;
        passData.grid = settings.viewGrid ? 1 : 0;
        passData.noEdges = settings.noEdges ? 1 : 0;
        passData.noFill = settings.noFill ? 1 : 0;

        /*passData.viewUncompressed = 0;
        passData.debugEdges = 0;
        passData.grid = 0;
        passData.noEdges = 0;
        passData.noFill = 0;*/

        // Store ASCII selection controls.
        passData.edgeThreshold = settings.edgeThreshold;
        passData.exposure = settings.exposure;
        passData.attenuation = settings.attenuation;
        passData.useDownscaledColor = settings.useDownscaledColor ? 1 : 0;
        passData.useSeparateEdgeColor = settings.useSeperateEdgeColor ? 1 : 0;
        passData.fillColor = settings.fillColor;
        passData.edgeColor = settings.edgeColor;

        // Store output size for dispatch group count and compute shader bounds.
        passData.width = width;
        passData.height = height;

        // Declare RenderGraph read dependencies.
        builder.UseTexture(passData.sobel, AccessFlags.Read);
        builder.UseTexture(passData.luminance, AccessFlags.Read);
        builder.UseTexture(passData.asciiTex, AccessFlags.Read);
        builder.UseTexture(passData.edgeTex, AccessFlags.Read);
        builder.UseTexture(passData.result, AccessFlags.Write);
        builder.SetRenderFunc(static (ComputePassData data, ComputeGraphContext context) =>
        {
            // Bind textures to compute shader.
            context.cmd.SetComputeTextureParam(data.compute, data.kernel, "_SobelTex", data.sobel);
            context.cmd.SetComputeTextureParam(data.compute, data.kernel, "_LuminanceTex", data.luminance);
            context.cmd.SetComputeTextureParam(data.compute, data.kernel, "_Result", data.result);
            context.cmd.SetComputeTextureParam(data.compute, data.kernel, "_AsciiTex", data.asciiTex);
            context.cmd.SetComputeTextureParam(data.compute, data.kernel, "_EdgeAsciiTex", data.edgeTex);

            // Bind debug / mode parameters.
            context.cmd.SetComputeIntParam(data.compute, ViewUncompressedId, data.viewUncompressed);
            context.cmd.SetComputeIntParam(data.compute, DebugEdgesId, data.debugEdges);
            context.cmd.SetComputeIntParam(data.compute, GridId, data.grid);
            context.cmd.SetComputeIntParam(data.compute, NoEdgesId, data.noEdges);
            context.cmd.SetComputeIntParam(data.compute, NoFillId, data.noFill);
            context.cmd.SetComputeIntParam(data.compute, EdgeThresholdId, data.edgeThreshold);

            // Bind ASCII fill controls.
            context.cmd.SetComputeFloatParam(data.compute, ExposureId, data.exposure);
            context.cmd.SetComputeFloatParam(data.compute, AttenuationId, data.attenuation);
            context.cmd.SetComputeIntParam(data.compute, UseDownscaledColorId, data.useDownscaledColor);
            context.cmd.SetComputeIntParam(data.compute, UseSeparateEdgeColorId, data.useSeparateEdgeColor);
            context.cmd.SetComputeVectorParam(data.compute, FillColorId, data.fillColor);
            context.cmd.SetComputeVectorParam(data.compute, EdgeColorId, data.edgeColor);

            // Bind output dimensions.
            context.cmd.SetComputeIntParam(data.compute, ResultWidthId, data.width);
            context.cmd.SetComputeIntParam(data.compute, ResultHeightId, data.height);

            // Dispatch one 8x8-thread group per 8x8 screen block.
            int groupsX = Mathf.CeilToInt(data.width / 8.0f);
            int groupsY = Mathf.CeilToInt(data.height / 8.0f);

            context.cmd.DispatchCompute(data.compute, data.kernel, groupsX, groupsY, 1);
        });
    }





    /*
    ================================================================================
    ASCII Render Pass data flow: Settings & TextureHandle from C# → Shader
    ================================================================================

    1. Settings（C#）→ Material → Shader

    → RendererFeature.Create() 创建 Material
        asciiMaterial = CoreUtils.CreateEngineMaterial(asciiShader)

    → RendererFeature (Inspector)
        settings.stdev / settings.tau

    → ASCIIRendererFeature.AddRenderPasses()
        asciiPass.SetSettings(settings)

    → ASCIIRenderPass.RecordRenderGraph()
        Material 写入 Shader Property 对应的 uniform value
        UploadSettingsToMaterial()
            material.SetFloat(ShaderProp("_Sigma"), settings.stdev)
            material.SetFloat(ShaderProp("_Tau"), settings.tau)

    → Blit Pass 注册: AddFullscreenBlitPass(source, destination, shaderPass)
        IRasterRenderGraphBuilder builder AddRasterRenderPass<BlitPassData>(..., out passData)
        return builder out data
        参数进入 PassData
        passData.material = material
        builder.SetRenderFunc(ExecuteBlit)

    ================================================================================
    2. TextureHandle（source → destination）→ Shader

    → ASCIIRenderPass.RecordRenderGraph()
        TextureHandle source
        TextureHandle destination
        
        AddBlitPass(source, destination)
            return builder out passData

    → 参数进入 PassData
        passData.source = source

    → Builder 声明依赖
        builder.UseTexture(passData.source, Read)
        builder.SetRenderAttachment(destination, 0)

    → 执行阶段（SetRenderFunc）
        data写入command buffer
        AddBlitPass
        {
            Blitter.BlitTexture(context.cmd, data.source, ..., data.material)
        }

    **TextureHandle (Compute) 显式绑定
        context.cmd.SetComputeTextureParam(
            shader,
            "_SourceTex",   // shader变量名
            data.source     // TextureHandle
        )

    ================================================================================
    Builder → RenderGraph → SetRenderFunc → CommandBuffer → GPU

    RecordRenderGraph()开始:
    Add pass → 填 passData → builder 声明依赖 → SetRenderFunc → Dispose 提交定义

    RecordRenderGraph()结束:
    RenderGraph 排序，执行每个 pass 的 SetRenderFunc，
    在 ExecuteBlit 里把 fullscreen draw call 录进 command buffer
    CommandBuffer 提交 GPU 执行

    ================================================================================
    */
}