using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// ==================================== Pipeline Overview ==============================================
// 1. Convert scene color to luminance
// 2. Apply two Gaussian blurs:
//    a. G1 = Gaussian(sigma)
//    b. G2 = Gaussian(sigma * k)
// 3. Difference of Gaussians:
//    DoG = G1 - tau * G2
// 4. Threshold DoG into a binary edge mask.
// 5. Run Sobel on the mask to estimate gradient direction.
// 6. Quantize direction into 4 edge types (vertical / horizontal / diagonals).
// 7. For each ASCII cell, vote for the dominant edge direction.
// =====================================================================================================

public class ASCIIRendererFeature : ScriptableRendererFeature
{
    [Serializable]
    public class ASCIISettings
    {
        [Header("Resources")]
        public ComputeShader asciiCompute;
        public Texture asciiTex;
        public Texture edgeTex;

        [Header("DoG / Sobel")]

        [Range(1, 10)]
        public int gaussianKernelSize = 2;

        [Tooltip(
            "Sigma in Gaussian blur.\n" +
            "Controls how wide the first Gaussian blur is.\n" +
            "Larger sigma ¡ú stronger blur.\n" +
            "Smaller sigma ¡ú preserves fine detail but increases noise.")]
        [Range(0.1f, 5.0f)]
        public float stdev = 2.0f;

        [Tooltip(
            "Scale factor k for the second Gaussian blur.\n" +
            "G2 uses sigma * k.\n")]
        [Range(0.0f, 5.0f)]
        public float stdevScale = 1.6f;

        [Tooltip(
            "Weighting factor tau for DoG.\n" +
            "DoG = G(sigma) - tau * G(sigma * k)\n")]
        [Range(0.0f, 5.0f)]
        public float tau = 1.0f;

        [Tooltip(
            "Converts continuous DoG values into a binary edge mask:\n" +
            "DoG >= threshold ¡ú edge (1)\n" +
            "DoG < threshold ¡ú non-edge (0)\n")]
        [Range(0.001f, 0.1f)]
        public float threshold = 0.005f;

        [Tooltip(
            "Inverts the edge mask.\n" +
            "Swaps edge/non-edge regions before Sobel.")]
        public bool invert = false;

        [Header("ASCII Fill / Edge")]

        [Tooltip(
            "Minimum number of pixels in a cell that must agree on an edge direction " +
            "for that direction to be considered dominant.")]
        [Range(0, 64)]
        public int edgeThreshold = 8;

        [Range(0.0f, 10.0f)]
        public float exposure = 1.0f;

        [Range(0.0f, 10.0f)]
        public float attenuation = 1.0f;

        public bool useDownscaledColor = false;
        public Color fillColor = Color.white;
        public bool useSeperateEdgeColor = false;
        public Color edgeColor = Color.white;

        [Header("Debug Views")]
        public bool viewDog = false;
        public bool viewSobel = false;
        public bool viewGrid = false;
        public bool debugEdges = false;
        public bool viewUncompressedEdges = false;
        public bool viewQuantizedSobel = false;
        public bool noEdges = false;
        public bool noFill = false;

        [Header("Injection")]
        public RenderPassEvent renderPassInjectionPoint = RenderPassEvent.AfterRenderingPostProcessing;

        [Tooltip("Usually keep this on while developing so the effect is visible in SV.")]
        public bool applyToSceneView = true;
    }

    [SerializeField]
    private Shader asciiShader;

    [SerializeField]
    private ASCIIPreset preset;

    [SerializeField]
    private ASCIISettings settings = new ASCIISettings();

    private Material asciiMaterial;
    private ASCIIRenderPass asciiPass;

    public override void Create()
    {
        DisposeMaterial();

        if (asciiShader == null)
        {
            asciiPass = null;
            return;
        }

        asciiMaterial = CoreUtils.CreateEngineMaterial(asciiShader);
        asciiPass = new ASCIIRenderPass(asciiMaterial, settings);
        asciiPass.renderPassEvent = settings.renderPassInjectionPoint;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (asciiPass == null || asciiMaterial == null)
        {
            return;
        }

        if (settings == null || settings.asciiCompute == null || settings.asciiTex == null || settings.edgeTex == null)
        {
            return;
        }

        CameraType cameraType = renderingData.cameraData.cameraType;

        bool isGameCamera = cameraType == CameraType.Game;
        bool isSceneViewCamera = settings.applyToSceneView && cameraType == CameraType.SceneView;

        if (!isGameCamera && !isSceneViewCamera)
        {
            return;
        }

        asciiPass.renderPassEvent = settings.renderPassInjectionPoint;
        asciiPass.SetSettings(settings);

        renderer.EnqueuePass(asciiPass);
    }

    protected override void Dispose(bool disposing)
    {
        asciiPass?.ReleaseAtlasRTHandles();

        DisposeMaterial();
        asciiPass = null;
    }

    private void DisposeMaterial()
    {
        if (asciiMaterial != null)
        {
            CoreUtils.Destroy(asciiMaterial);
            asciiMaterial = null;
        }
    }


    public void ApplyPresetToSettings()
    {
        if (preset == null || settings == null)
            return;

        settings.gaussianKernelSize = preset.gaussianKernelSize;
        settings.stdev = preset.stdev;
        settings.stdevScale = preset.stdevScale;
        settings.tau = preset.tau;
        settings.threshold = preset.threshold;
        settings.invert = preset.invert;

        settings.edgeThreshold = preset.edgeThreshold;
        settings.exposure = preset.exposure;
        settings.attenuation = preset.attenuation;

        settings.useDownscaledColor = preset.useDownscaledColor;
        settings.fillColor = preset.fillColor;

        settings.useSeperateEdgeColor = preset.useSeperateEdgeColor;
        settings.edgeColor = preset.edgeColor;

        settings.viewDog = preset.viewDog;
        settings.viewSobel = preset.viewSobel;
        settings.viewGrid = preset.viewGrid;
        settings.debugEdges = preset.debugEdges;
        settings.viewUncompressedEdges = preset.viewUncompressedEdges;
        settings.viewQuantizedSobel = preset.viewQuantizedSobel;
        settings.noEdges = preset.noEdges;
        settings.noFill = preset.noFill;

        settings.renderPassInjectionPoint = preset.renderPassInjectionPoint;
        settings.applyToSceneView = preset.applyToSceneView;

        Create();
    }

    public void SaveSettingsToPreset()
    {
        if (preset == null || settings == null)
            return;

        preset.gaussianKernelSize = settings.gaussianKernelSize;
        preset.stdev = settings.stdev;
        preset.stdevScale = settings.stdevScale;
        preset.tau = settings.tau;
        preset.threshold = settings.threshold;
        preset.invert = settings.invert;

        preset.edgeThreshold = settings.edgeThreshold;
        preset.exposure = settings.exposure;
        preset.attenuation = settings.attenuation;

        preset.useDownscaledColor = settings.useDownscaledColor;
        preset.fillColor = settings.fillColor;

        preset.useSeperateEdgeColor = settings.useSeperateEdgeColor;
        preset.edgeColor = settings.edgeColor;

        preset.viewDog = settings.viewDog;
        preset.viewSobel = settings.viewSobel;
        preset.viewGrid = settings.viewGrid;
        preset.debugEdges = settings.debugEdges;
        preset.viewUncompressedEdges = settings.viewUncompressedEdges;
        preset.viewQuantizedSobel = settings.viewQuantizedSobel;
        preset.noEdges = settings.noEdges;
        preset.noFill = settings.noFill;

        preset.renderPassInjectionPoint = settings.renderPassInjectionPoint;
        preset.applyToSceneView = settings.applyToSceneView;

    #if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(preset);
    #endif
    }
}