using UnityEngine;
using UnityEngine.UI;

public class TerrainMaterialScrollbarController : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Renderer using the terrain material.")]
    [SerializeField] private Renderer terrainRenderer;

    [Header("Scrollbars")]
    [SerializeField] private Scrollbar displacementHeightScrollbar;
    [SerializeField] private Scrollbar noiseScaleScrollbar;
    [SerializeField] private Scrollbar scrollSpeedScrollbar;
    [SerializeField] private Scrollbar persistenceScrollbar;
    [SerializeField] private Scrollbar topLightSoftnessScrollbar;

    [Header("Parameter Ranges")]
    [SerializeField] private Vector2 displacementHeightRange = new Vector2(0.5f, 4.5f);
    [SerializeField] private Vector2 noiseScaleRange = new Vector2(0.3f, 1.5f);
    [SerializeField] private Vector2 scrollSpeedRange = new Vector2(0.0f, 2.0f);
    [SerializeField] private Vector2 persistenceRange = new Vector2(0.1f, 0.7f);
    [SerializeField] private Vector2 topLightSoftnessRange = new Vector2(0.0f, 1.0f);

    private MaterialPropertyBlock propertyBlock;

    private static readonly int HeightId = Shader.PropertyToID("_Height");
    private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
    private static readonly int ScrollSpeedId = Shader.PropertyToID("_ScrollSpeed");
    private static readonly int PersistenceId = Shader.PropertyToID("_Persistence");
    private static readonly int TopLightSoftnessId = Shader.PropertyToID("_TopLightSoftness");

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
    }

    private void Start()
    {
        if (terrainRenderer == null || terrainRenderer.sharedMaterial == null)
        {
            Debug.LogWarning("TerrainMaterialScrollbarController: Missing terrain renderer or material.");
            return;
        }

        InitializeScrollbarsFromMaterial();
        RegisterScrollbarEvents();
        ApplyAllScrollbarValues();
    }

    private void OnDestroy()
    {
        UnregisterScrollbarEvents();

        if (terrainRenderer != null)
        {
            terrainRenderer.SetPropertyBlock(null);
        }
    }

    private void InitializeScrollbarsFromMaterial()
    {
        Material material = terrainRenderer.sharedMaterial;

        SetScrollbarFromMaterialValue(
            displacementHeightScrollbar,
            material.GetFloat(HeightId),
            displacementHeightRange
        );

        SetScrollbarFromMaterialValue(
            noiseScaleScrollbar,
            material.GetFloat(NoiseScaleId),
            noiseScaleRange
        );

        SetScrollbarFromMaterialValue(
            scrollSpeedScrollbar,
            material.GetFloat(ScrollSpeedId),
            scrollSpeedRange
        );

        SetScrollbarFromMaterialValue(
            persistenceScrollbar,
            material.GetFloat(PersistenceId),
            persistenceRange
        );

        SetScrollbarFromMaterialValue(
            topLightSoftnessScrollbar,
            material.GetFloat(TopLightSoftnessId),
            topLightSoftnessRange
        );
    }

    private void RegisterScrollbarEvents()
    {
        if (displacementHeightScrollbar != null)
            displacementHeightScrollbar.onValueChanged.AddListener(SetDisplacementHeightNormalized);

        if (noiseScaleScrollbar != null)
            noiseScaleScrollbar.onValueChanged.AddListener(SetNoiseScaleNormalized);

        if (scrollSpeedScrollbar != null)
            scrollSpeedScrollbar.onValueChanged.AddListener(SetScrollSpeedNormalized);

        if (persistenceScrollbar != null)
            persistenceScrollbar.onValueChanged.AddListener(SetPersistenceNormalized);

        if (topLightSoftnessScrollbar != null)
            topLightSoftnessScrollbar.onValueChanged.AddListener(SetTopLightSoftnessNormalized);
    }

    private void UnregisterScrollbarEvents()
    {
        if (displacementHeightScrollbar != null)
            displacementHeightScrollbar.onValueChanged.RemoveListener(SetDisplacementHeightNormalized);

        if (noiseScaleScrollbar != null)
            noiseScaleScrollbar.onValueChanged.RemoveListener(SetNoiseScaleNormalized);

        if (scrollSpeedScrollbar != null)
            scrollSpeedScrollbar.onValueChanged.RemoveListener(SetScrollSpeedNormalized);

        if (persistenceScrollbar != null)
            persistenceScrollbar.onValueChanged.RemoveListener(SetPersistenceNormalized);

        if (topLightSoftnessScrollbar != null)
            topLightSoftnessScrollbar.onValueChanged.RemoveListener(SetTopLightSoftnessNormalized);
    }

    private void ApplyAllScrollbarValues()
    {
        SetDisplacementHeightNormalized(GetScrollbarValue(displacementHeightScrollbar));
        SetNoiseScaleNormalized(GetScrollbarValue(noiseScaleScrollbar));
        SetScrollSpeedNormalized(GetScrollbarValue(scrollSpeedScrollbar));
        SetPersistenceNormalized(GetScrollbarValue(persistenceScrollbar));
        SetTopLightSoftnessNormalized(GetScrollbarValue(topLightSoftnessScrollbar));
    }

    private void SetDisplacementHeightNormalized(float normalizedValue)
    {
        float value = Remap01(normalizedValue, displacementHeightRange);
        SetMaterialFloat(HeightId, value);
    }

    private void SetNoiseScaleNormalized(float normalizedValue)
    {
        float value = Remap01(normalizedValue, noiseScaleRange);
        SetMaterialFloat(NoiseScaleId, value);
    }

    private void SetScrollSpeedNormalized(float normalizedValue)
    {
        float value = Remap01(normalizedValue, scrollSpeedRange);
        SetMaterialFloat(ScrollSpeedId, value);
    }

    private void SetPersistenceNormalized(float normalizedValue)
    {
        float value = Remap01(normalizedValue, persistenceRange);
        SetMaterialFloat(PersistenceId, value);
    }

    private void SetTopLightSoftnessNormalized(float normalizedValue)
    {
        float value = Remap01(normalizedValue, topLightSoftnessRange);
        SetMaterialFloat(TopLightSoftnessId, value);
    }

    private void SetMaterialFloat(int propertyId, float value)
    {
        if (terrainRenderer == null)
            return;

        terrainRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(propertyId, value);
        terrainRenderer.SetPropertyBlock(propertyBlock);
    }

    private static void SetScrollbarFromMaterialValue(Scrollbar scrollbar, float materialValue, Vector2 range)
    {
        if (scrollbar == null)
            return;

        float normalizedValue = Mathf.InverseLerp(range.x, range.y, materialValue);
        scrollbar.SetValueWithoutNotify(normalizedValue);
    }

    private static float Remap01(float normalizedValue, Vector2 range)
    {
        normalizedValue = Mathf.Clamp01(normalizedValue);
        return Mathf.Lerp(range.x, range.y, normalizedValue);
    }

    private static float GetScrollbarValue(Scrollbar scrollbar)
    {
        if (scrollbar == null)
            return 0.0f;

        return scrollbar.value;
    }
}