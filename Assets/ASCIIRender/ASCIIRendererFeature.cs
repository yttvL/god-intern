using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class ASCIIRendererFeature : ScriptableRendererFeature
{
    [Serializable]
    public class ASCIISettings
    {
        [Header("Resources")]
        public ComputeShader asciiCompute;
        public Texture asciiTex;
        public Texture edgeTex;

        [Header("DoG / Edge Detection")]
        [Range(1, 10)]
        public int gaussianKernelSize = 2;

        [Range(0.1f, 5.0f)]
        public float stdev = 2.0f;

        [Range(0.0f, 5.0f)]
        public float stdevScale = 1.6f;

        [Range(0.0f, 5.0f)]
        public float tau = 1.0f;

        [Range(0.001f, 0.1f)]
        public float threshold = 0.005f;

        public bool invert = false;

        [Header("ASCII Fill / Edge")]
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
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

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
        asciiPass = new ASCIIRenderPass(asciiMaterial, settings)
        {
            renderPassEvent = settings.renderPassEvent
        };
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

        asciiPass.renderPassEvent = settings.renderPassEvent;
        asciiPass.SetSettings(settings);

        renderer.EnqueuePass(asciiPass);
    }

    protected override void Dispose(bool disposing)
    {
        asciiPass?.Dispose();

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