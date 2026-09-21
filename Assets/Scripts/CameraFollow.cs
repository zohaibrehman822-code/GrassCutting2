using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Target is assigned automatically when the Player is instantiated.")]
    [SerializeField] private Transform target;

    [Header("Position")]
    [SerializeField]
    private Vector3 offset = new Vector3(0f, 5f, -8f);

    [Tooltip("Higher values follow faster. Try 0.20 to 0.35.")]
    [Min(0.01f)]
    [SerializeField]
    private float positionSmoothTime = 0.25f;

    [Header("Look")]
    [SerializeField]
    private Vector3 lookOffset = new Vector3(0f, 0.75f, 0f);

    [Tooltip("Higher values rotate faster.")]
    [Min(0.1f)]
    [SerializeField]
    private float rotationSmoothness = 7f;

    [Min(0.01f)]
    [SerializeField]
    private float lookPointSmoothTime = 0.2f;

    [Header("Startup")]
    [SerializeField]
    private bool snapToTargetOnStart = true;

    private Vector3 positionVelocity;
    private Vector3 lookPointVelocity;
    private Vector3 smoothedLookPoint;

    private void Start()
    {
        // Target is assigned by GameManager after Player is spawned.
        if (target == null)
        {
            return;
        }

        smoothedLookPoint = target.position + lookOffset;

        if (snapToTargetOnStart)
        {
            SnapToTarget();
        }
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        transform.position = target.position + offset;

        //float deltaTime = Time.deltaTime;

        //if (deltaTime <= 0f)
        //{
        //    return;
        //}

        //// -------------------------
        //// Position
        //// -------------------------

        //Vector3 desiredPosition =
        //    target.position + offset;

        //transform.position =
        //    Vector3.SmoothDamp(
        //        transform.position,
        //        desiredPosition,
        //        ref positionVelocity,
        //        positionSmoothTime,
        //        Mathf.Infinity,
        //        deltaTime
        //    );

        //// -------------------------
        //// Look Point
        //// -------------------------

        //Vector3 desiredLookPoint =
        //    target.position + lookOffset;

        //smoothedLookPoint =
        //    Vector3.SmoothDamp(
        //        smoothedLookPoint,
        //        desiredLookPoint,
        //        ref lookPointVelocity,
        //        lookPointSmoothTime,
        //        Mathf.Infinity,
        //        deltaTime
        //    );

        //// -------------------------
        //// Rotation
        //// -------------------------

        //Vector3 lookDirection =
        //    smoothedLookPoint - transform.position;

        //if (lookDirection.sqrMagnitude < 0.0001f)
        //{
        //    return;
        //}

        //Quaternion targetRotation =
        //    Quaternion.LookRotation(
        //        lookDirection,
        //        Vector3.up
        //    );

        //float rotationAmount =
        //    1f - Mathf.Exp(
        //        -rotationSmoothness * deltaTime
        //    );

        //transform.rotation =
        //    Quaternion.Slerp(
        //        transform.rotation,
        //        targetRotation,
        //        rotationAmount
        //    );
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;

        if (target == null)
        {
            return;
        }

        // Immediately move camera to the new Player.
        SnapToTarget();
    }

    public void SnapToTarget()
    {
        if (target == null)
        {
            return;
        }

        positionVelocity = Vector3.zero;
        lookPointVelocity = Vector3.zero;

        transform.position =
            target.position + offset;

        smoothedLookPoint =
            target.position + lookOffset;

        Vector3 lookDirection =
            smoothedLookPoint - transform.position;

        if (lookDirection.sqrMagnitude > 0.0001f)
        {
            transform.rotation =
                Quaternion.LookRotation(
                    lookDirection,
                    Vector3.up
                );
        }
    }
}
