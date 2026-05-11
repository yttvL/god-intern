using UnityEngine;

public class SimpleYRotation : MonoBehaviour
{
    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 40.0f;

    private void Update()
    {
        float rotationInput = 0.0f;

        if (Input.GetKey(KeyCode.A))
        {
            rotationInput += 1.0f;
        }

        if (Input.GetKey(KeyCode.D))
        {
            rotationInput -= 1.0f;
        }

        float deltaRotation =
            rotationInput *
            rotationSpeed *
            Time.deltaTime;

        transform.Rotate(
            0.0f,
            deltaRotation,
            0.0f,
            Space.World
        );
    }
}