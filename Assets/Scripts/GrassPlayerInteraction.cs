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

    [Tooltip("Materials used by enemy captured grass (red/blue).")]
    [SerializeField] private Material[] enemyGrassMaterials;

    [Header("Interaction")]
    [Min(0.1f)]
    [SerializeField] private float radius = 0.65f;

    [Range(0f, 0.4f)]
    [SerializeField] private float bendDistance = 0.18f;

    [Min(0.1f)]
    [Tooltip("How quickly bending follows movement and settles after stopping.")]
    [SerializeField] private float responseSpeed = 8f;

    [Min(0.1f)]
    [Tooltip("Seconds for grass at the previous player position to settle.")]
    [SerializeField] private float trailFadeSeconds = 0.65f;

    [Range(0f, 0.3f)]
    [SerializeField] private float spinBendStrength = 0.12f;

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
    private Vector2 previousPosition;
    private float previousRotation;

    private void Awake()
    {
        movement = GetComponent<Movement>();
        ResetInteractionState();
    }

    private void OnEnable()
    {
        ResetInteractionState();
    }

    private void ResetInteractionState()
    {
        Vector3 position = transform.position;
        trailPosition = new Vector2(position.x, position.z);
        previousPosition = trailPosition;
        previousRotation = transform.eulerAngles.y;
        lastTravelDirection = Vector2.up;
        trailDirectionAngle = Mathf.PI * 0.5f;
        strength = 0f;
        trailStrength = 0f;
    }

    private void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        if (movement == null || deltaTime <= 0f)
        {
            return;
        }

        Vector3 position = transform.position;
        Vector2 currentPosition = new Vector2(position.x, position.z);
        float rotation = transform.eulerAngles.y;
        Vector2 displacement = currentPosition - previousPosition;
        float angularSpeed = Mathf.Abs(
            Mathf.DeltaAngle(previousRotation, rotation)) / deltaTime;

        float teleportDistance = Mathf.Max(1f, radius * 2f);
        if (displacement.sqrMagnitude > teleportDistance * teleportDistance)
        {
            ResetInteractionState();
            displacement = Vector2.zero;
            angularSpeed = 0f;
        }

        Vector3 movementDirection = movement.CurrentMoveDirection;
        Vector2 travelDirection = new Vector2(
            movementDirection.x, movementDirection.z);
        float movingStrength = Mathf.Clamp01(travelDirection.magnitude);
        float follow = 1f - Mathf.Exp(-Mathf.Max(0.1f, responseSpeed) * deltaTime);

        if (movingStrength > 0.001f)
        {
            Vector2 actualDirection = displacement.sqrMagnitude > 0.000001f
                ? displacement.normalized
                : travelDirection.normalized;
            Vector2 blendedDirection = Vector2.Lerp(
                lastTravelDirection, actualDirection, follow);
            lastTravelDirection = blendedDirection.sqrMagnitude > 0.000001f
                ? blendedDirection.normalized
                : actualDirection;
        }

        float targetStrength = Mathf.Max(
            movingStrength,
            Mathf.Clamp01(angularSpeed / 180f) * spinBendStrength);
        strength = Mathf.Lerp(strength, targetStrength, follow);
        if (strength < 0.001f)
        {
            strength = 0f;
        }

        if (movingStrength > 0.01f)
        {
            Vector2 oldTrailPosition = trailPosition;
            trailPosition = Vector2.Lerp(
                trailPosition, previousPosition,
                1f - Mathf.Exp(-12f * deltaTime));
            Vector2 wakeDirection = trailPosition - oldTrailPosition;
            if (wakeDirection.sqrMagnitude > 0.000001f)
            {
                float targetAngle = Mathf.Atan2(wakeDirection.y, wakeDirection.x);
                trailDirectionAngle = Mathf.LerpAngle(
                    trailDirectionAngle * Mathf.Rad2Deg,
                    targetAngle * Mathf.Rad2Deg,
                    follow) * Mathf.Deg2Rad;
            }
            trailStrength = Mathf.Lerp(trailStrength, movingStrength, follow);
        }
        else
        {
            trailStrength = Mathf.MoveTowards(
                trailStrength,
                0f,
                deltaTime / Mathf.Max(0.1f, trailFadeSeconds)
            );
        }

        previousPosition = currentPosition;
        previousRotation = rotation;

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

        if (enemyGrassMaterials != null)
        {
            for (int i = 0; i < enemyGrassMaterials.Length; i++)
            {
                SetMaterialInteraction(
                    enemyGrassMaterials[i],
                    centerData,
                    motionData,
                    trailData
                );
            }
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

        if (enemyGrassMaterials != null)
        {
            for (int i = 0; i < enemyGrassMaterials.Length; i++)
            {
                SetMaterialInteraction(
                    enemyGrassMaterials[i],
                    Vector4.zero,
                    Vector4.zero,
                    Vector4.zero
                );
            }
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
