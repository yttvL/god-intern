using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class FakeSunOrbitController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Renderer terrainRenderer;
    [SerializeField] private Transform fakeSun;

    [Tooltip(
        "Orbit center and orbit plane reference.\n" +
        "Position = center of the orbit.\n" +
        "Rotation = orientation of the orbit plane.\n" +
        "The circle is drawn using orbitOrigin.right and orbitOrigin.forward."
    )]
    [SerializeField] private Transform orbitOrigin;

    [Header("Orbit Settings")]
    [Min(0.0f)]
    [SerializeField] private float radiusY = 5.0f;
    [Min(0.0f)]
    [SerializeField] private float radiusZ = 5.0f;
    [SerializeField] private float speedDegreesPerSecond = 15.0f;

    [Range(0.0f, 360.0f)]
    [SerializeField] private float startAngleDegrees = 0.0f;

    [Tooltip("If enabled, the fake sun rotates automatically during Play Mode.")]
    [SerializeField] private bool animateOnPlay = true;

    [Header("Shader Binding")]
    [Tooltip("Shader property name used by the terrain shader for fake light direction.")]
    [SerializeField] private string lightDirectionProperty = "_LightDirection";

    [Tooltip(
        "If true, sends direction from orbit origin to fake sun.\n" +
        "This matches dot(normalWS, lightDir), where surfaces facing the fake sun become brighter."
    )]
    [SerializeField] private bool directionPointsToLight = true;

    [Header("Editor Preview")]
    [SerializeField] private bool updateInEditMode = true;
    [SerializeField] private bool drawOrbitGizmo = true;

    [Range(8, 128)]
    [SerializeField] private int gizmoSegments = 64;

    private MaterialPropertyBlock propertyBlock;
    private int lightDirectionPropertyId;
    private float currentAngleDegrees;

    private void OnEnable()
    {
        EnsurePropertyBlock();
        CachePropertyId();

        currentAngleDegrees = startAngleDegrees;
        UpdateOrbitAndShader();
    }

    private void Awake()
    {
        EnsurePropertyBlock();
        CachePropertyId();

        currentAngleDegrees = startAngleDegrees;
        UpdateOrbitAndShader();
    }

    private void Update()
    {
        EnsurePropertyBlock();
        CachePropertyId();

        if (Application.isPlaying)
        {
            if (animateOnPlay)
            {
                currentAngleDegrees += speedDegreesPerSecond * Time.deltaTime;
                currentAngleDegrees = Mathf.Repeat(currentAngleDegrees, 360.0f);
            }

            UpdateOrbitAndShader();
        }
        else
        {
            if (updateInEditMode)
            {
                currentAngleDegrees = startAngleDegrees;
                UpdateOrbitAndShader();
            }
        }
    }

    private void OnValidate()
    {
        radiusY = Mathf.Max(0.0f, radiusY);
        radiusZ = Mathf.Max(0.0f, radiusZ);
        gizmoSegments = Mathf.Clamp(gizmoSegments, 8, 128);

        EnsurePropertyBlock();
        CachePropertyId();

        if (!Application.isPlaying && updateInEditMode)
        {
            currentAngleDegrees = startAngleDegrees;
            UpdateOrbitAndShader();
        }
    }

    private void EnsurePropertyBlock()
    {
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }
    }

    private void CachePropertyId()
    {
        lightDirectionPropertyId = Shader.PropertyToID(lightDirectionProperty);
    }

    private void UpdateOrbitAndShader()
    {
        if (fakeSun == null || orbitOrigin == null)
        {
            return;
        }

        UpdateFakeSunPosition();
        ApplyLightDirectionToMaterial();
    }

    private void UpdateFakeSunPosition()
    {
        float radians = currentAngleDegrees * Mathf.Deg2Rad;

        // orbitOrigin.rotation defines the orbit plane.
        // up + forward form the plane axes.
        Vector3 axisA = orbitOrigin.up;
        Vector3 axisB = orbitOrigin.forward;

        Vector3 offset =
            axisA * Mathf.Cos(radians) * radiusY +
            axisB * Mathf.Sin(radians) * radiusZ;

        fakeSun.position = orbitOrigin.position + offset;
    }

    private Vector3 ComputeLightDirection()
    {
        if (fakeSun == null || orbitOrigin == null)
        {
            return Vector3.up;
        }

        Vector3 direction;

        if (directionPointsToLight)
        {
            // Correct for shader logic:
            // dot(normalWS, lightDir)
            // Surfaces facing the fake sun become brighter.
            direction = fakeSun.position - orbitOrigin.position;
        }
        else
        {
            // Reverse direction, only if your shader expects light ray travel direction.
            direction = orbitOrigin.position - fakeSun.position;
        }

        if (direction.sqrMagnitude < 0.000001f)
        {
            return orbitOrigin.up;
        }

        return direction.normalized;
    }

    private void ApplyLightDirectionToMaterial()
    {
        if (terrainRenderer == null)
        {
            return;
        }

        Vector3 lightDirection = ComputeLightDirection();

        terrainRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetVector(lightDirectionPropertyId, new Vector4(
            lightDirection.x,
            lightDirection.y,
            lightDirection.z,
            0.0f
        ));
        terrainRenderer.SetPropertyBlock(propertyBlock);
    }

    private void OnDrawGizmos()
    {
        if (!drawOrbitGizmo || orbitOrigin == null)
        {
            return;
        }

        DrawOrbitGizmo();
        DrawLightDirectionGizmo();
    }

    private void DrawOrbitGizmo()
    {
        Vector3 center = orbitOrigin.position;
        Vector3 axisA = orbitOrigin.up;
        Vector3 axisB = orbitOrigin.forward;

        Vector3 previousPoint = center + axisA * radiusY;

        Gizmos.color = Color.yellow;

        for (int i = 1; i <= gizmoSegments; i++)
        {
            float t = (float)i / gizmoSegments;
            float radians = t * Mathf.PI * 2.0f;

            Vector3 nextPoint =
                center +
                axisA * Mathf.Cos(radians) * radiusY +
                axisB * Mathf.Sin(radians) * radiusZ;

            Gizmos.DrawLine(previousPoint, nextPoint);
            previousPoint = nextPoint;
        }

    }

    private void DrawLightDirectionGizmo()
    {
        if (fakeSun == null || orbitOrigin == null)
        {
            return;
        }

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(orbitOrigin.position, fakeSun.position);

#if UNITY_EDITOR
        Handles.color = Color.cyan;
        Vector3 dir = ComputeLightDirection();
        float maxRadius = Mathf.Max(radiusY, radiusZ);

        Handles.ArrowHandleCap(
            0,
            orbitOrigin.position,
            Quaternion.LookRotation(dir),
            Mathf.Max(maxRadius * 0.25f, 0.25f),
            EventType.Repaint
        );
#endif
    }
}