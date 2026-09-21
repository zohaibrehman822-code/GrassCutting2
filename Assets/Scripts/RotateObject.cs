using UnityEngine;

public class RotateObject : MonoBehaviour
{
    [Header("Rotation")]
    [SerializeField] private float minimumRotationSpeed = 20f;
    [SerializeField] private float maximumRotationSpeed = 150f;
    [SerializeField] private float speedChangeRate = 100f;

    private float currentRotationSpeed;
    private bool isRotating = true;
    private bool isMoving;

    private void Awake()
    {
        currentRotationSpeed = minimumRotationSpeed;
    }

    private void Update()
    {
        if (!isRotating)
            return;

        float targetSpeed = isMoving
            ? maximumRotationSpeed
            : minimumRotationSpeed;

        currentRotationSpeed = Mathf.MoveTowards(
            currentRotationSpeed,
            targetSpeed,
            speedChangeRate * Time.deltaTime
        );

        transform.Rotate(
            0f,
            -currentRotationSpeed * Time.deltaTime,
            0f,
            Space.Self
        );
    }

    public void SetMoving(bool moving)
    {
        isMoving = moving;
    }

    public void StartRotation()
    {
        isRotating = true;
    }

    public void StopRotation()
    {
        isRotating = false;
    }
}