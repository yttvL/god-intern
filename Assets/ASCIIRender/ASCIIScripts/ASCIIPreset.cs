using UnityEngine;
using UnityEngine.Rendering.Universal;

[CreateAssetMenu(fileName = "ASCIIPreset", menuName = "ASCII/ASCII Preset")]
public class ASCIIPreset : ScriptableObject
{
    [Header("DoG / Sobel")]
    public int gaussianKernelSize = 2;
    public float stdev = 2.0f;
    public float stdevScale = 1.6f;
    public float tau = 1.0f;
    public float threshold = 0.005f;
    public bool invert = false;

    [Header("ASCII Fill / Edge")]
    public int edgeThreshold = 8;
    public float exposure = 1.0f;
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
    public bool applyToSceneView = true;
}