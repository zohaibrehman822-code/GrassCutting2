using UnityEngine;

/// <summary>
/// Sends the player and one fading previous position to the grass materials.
/// The shader bends nearby tufts on the GPU, with no per-blade CPU work.
/// </summary>
[DefaultExecutionOrder(-300)]
[RequireComponent(typeof(Movement))]
public sealed class GrassPlayerInteraction : MonoBehaviour
{
    [Header("Grass Materials")]
    [SerializeField] private Material wildGrassMaterial;
    [SerializeField] private Material territoryGrassMaterial;

    [Header("Interaction")]
    [Min(0.1f)]
    [SerializeField] private float radius = 1.3f;

    [Range(0f, 0.4f)]
    [SerializeField] private float bendDistance = 0.25f;

    [Min(0.1f)]
    [Tooltip("How quickly bending settles after movement stops. Entry is immediate.")]
    [SerializeField] private float responseSpeed = 1.4f;

    [Min(0.1f)]
    [Tooltip("Seconds for grass at the previous player position to settle.")]
    [SerializeField] private float trailFadeSeconds = 0.9f;

    private static readonly int InteractionCenterId =
        Shader.PropertyToID("_Grass_Interaction_Center");

    private static readonly int InteractionMotionId =
        Shader.PropertyToID("_Grass_Interaction_Motion");

    private static readonly int InteractionTrailId =
        Shader.PropertyToID("_Grass_Interaction_Trail");

    private Movement movement;
    private float strength;
    private Vector2 lastTravelDirection = Vector2.up;
    private Vector2 trailPosition;
    private float trailDirectionAngle;
    private float trailStrength;

    private void Awake()
    {
        movement = GetComponent<Movement>();
        Vector3 position = transform.position;
        trailPosition = new Vector2(position.x, position.z);
    }

    private void Update()
    {
        Vector3 direction = movement.CurrentMoveDirection;
        direction.y = 0f;

        Vector2 travelDirection = new Vector2(
            direction.x,
            direction.z
        );

        if (travelDirection.sqrMagnitude > 0.001f)
        {
            lastTravelDirection = travelDirection.normalized;
        }

        float targetStrength = Mathf.Clamp01(direction.magnitude);
        strength = targetStrength > strength
            ? targetStrength
            : Mathf.MoveTowards(
                strength,
                targetStrength,
                responseSpeed * Time.deltaTime
            );

        Vector3 position = transform.position;
        Vector2 currentPosition = new Vector2(position.x, position.z);
        float captureDistance = Mathf.Min(radius * 0.4f, 0.5f);
        float distanceFromTrailSqr =
            (currentPosition - trailPosition).sqrMagnitude;

        if (targetStrength > 0.01f && trailStrength <= 0.001f)
        {
            trailPosition = currentPosition;
            trailDirectionAngle = Mathf.Atan2(
                lastTravelDirection.y,
                lastTravelDirection.x
            );
            trailStrength = targetStrength;
        }
        else if (targetStrength > 0.01f &&
                 distanceFromTrailSqr <= captureDistance * captureDistance)
        {
            trailStrength = Mathf.Max(trailStrength, targetStrength);
            trailDirectionAngle = Mathf.Atan2(
                lastTravelDirection.y,
                lastTravelDirection.x
            );
        }
        else
        {
            trailStrength = Mathf.MoveTowards(
                trailStrength,
                0f,
                Time.deltaTime / Mathf.Max(0.1f, trailFadeSeconds)
            );
        }

        Vector4 centerData = new Vector4(
            position.x,
            transform.eulerAngles.y * Mathf.Deg2Rad,
            position.z,
            radius
        );

        Vector4 motionData = new Vector4(
            lastTravelDirection.x,
            lastTravelDirection.y,
            strength,
            bendDistance
        );

        Vector4 trailData = new Vector4(
            trailPosition.x,
            trailPosition.y,
            trailStrength,
            trailDirectionAngle
        );

        SetMaterialInteraction(wildGrassMaterial, centerData, motionData, trailData);

        if (territoryGrassMaterial != wildGrassMaterial)
        {
            SetMaterialInteraction(
                territoryGrassMaterial,
                centerData,
                motionData,
                trailData
            );
        }
    }

    private void OnDisable()
    {
        strength = 0f;
        trailStrength = 0f;
        SetMaterialInteraction(
            wildGrassMaterial,
            Vector4.zero,
            Vector4.zero,
            Vector4.zero
        );

        if (territoryGrassMaterial != wildGrassMaterial)
        {
            SetMaterialInteraction(
                territoryGrassMaterial,
                Vector4.zero,
                Vector4.zero,
                Vector4.zero
            );
        }
    }

    private static void SetMaterialInteraction(
        Material material,
        Vector4 centerData,
        Vector4 motionData,
        Vector4 trailData)
    {
        if (material == null)
        {
            return;
        }

        material.SetVector(InteractionCenterId, centerData);
        material.SetVector(InteractionMotionId, motionData);
        material.SetVector(InteractionTrailId, trailData);
    }
}
