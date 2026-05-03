using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// ==================================== Pipeline Overview ==============================================
// 1. Convert scene color to luminance
// 2. Apply two Gaussian blurs:
//    a. G1 = Gaussian(考)
//    b. G2 = Gaussian(考 * k)
// 3. Difference of Gaussians:
//    DoG = G1 - 而 * G2
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
            "Larger 考 ↙ stronger blur.\n" +
            "Smaller 考 ↙ preserves fine detail but increases noise.")]
        [Range(0.1f, 5.0f)]
        public float stdev = 2.0f;

        [Tooltip(
            "Scale factor k for the second Gaussian blur.\n" +
            "G2 uses 考 * k.\n")]
        [Range(0.0f, 5.0f)]
        public float stdevScale = 1.6f;

        [Tooltip(
            "Weighting factor 而 for DoG.\n" +
            "DoG = G(考) - 而 * G(考 * k)\n")]
        [Range(0.0f, 5.0f)]
        public float tau = 1.0f;

        [Tooltip(
            "Converts continuous DoG values into a binary edge mask:\n" +
            "DoG >= threshold ↙ edge (1)\n" +
            "DoG < threshold ↙ non-edge (0)\n")]
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
}