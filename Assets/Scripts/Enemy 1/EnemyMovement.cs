using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(RotateObject))]
public class EnemyMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 2f;
    [SerializeField, Min(0.1f)] private float acceleration = 8f;

    [Tooltip("Enemy counts as arrived when this close to its destination.")]
    [SerializeField, Min(0.01f)] private float arriveDistance = 0.2f;

    [Tooltip("New destinations closer than this to the current one are ignored (saves path calculations).")]
    [SerializeField, Min(0.05f)] private float destinationUpdateThreshold = 0.25f;

    [Tooltip("How far from a requested point we look for the nearest NavMesh point.")]
    [SerializeField, Min(0.1f)] private float samplePositionRange = 1f;

    private NavMeshAgent agent;
    private RotateObject rotateObject;

    private Vector3 lastDestination;
    private bool hasDestination;
    private bool wasMoving;
    private float nextTestWanderTime;

    public bool IsMoving => wasMoving;
    public float MoveSpeed => moveSpeed;

    public bool HasArrived
    {
        get
        {
            if (!hasDestination || !agent.isOnNavMesh)
                return true;

            return !agent.pathPending &&
                   agent.remainingDistance <= arriveDistance;
        }
    }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        rotateObject = GetComponent<RotateObject>();

        agent.speed = moveSpeed;
        agent.acceleration = acceleration;

        // The sawblade spins on this transform through RotateObject,
        // so the agent must never rotate it.
        agent.updateRotation = false;
    }

    private void Update()
    {
        UpdateSawbladeSpin();
    }

    /// <summary>
    /// Sends the enemy to the nearest NavMesh point around worldPosition.
    /// Returns false if no NavMesh point was found or the agent is not on the NavMesh.
    /// </summary>
    /// 
    public bool SetDestination(
    Vector3 worldPosition)
    {
        if (!agent.isOnNavMesh &&
            !TryRecoverToNavMesh())
        {
            hasDestination = false;
            return false;
        }

        float thresholdSqr =
            destinationUpdateThreshold *
            destinationUpdateThreshold;

        bool sameDestination =
            hasDestination &&
            (worldPosition - lastDestination)
            .sqrMagnitude < thresholdSqr;

        if (sameDestination &&
            !agent.isStopped)
        {
            if (agent.pathPending)
            {
                return true;
            }

            if (agent.hasPath &&
                agent.pathStatus ==
                NavMeshPathStatus.PathComplete)
            {
                return true;
            }
        }

        NavMeshQueryFilter filter =
            new NavMeshQueryFilter
            {
                agentTypeID = agent.agentTypeID,
                areaMask = agent.areaMask
            };

        if (!NavMesh.SamplePosition(
                worldPosition,
                out NavMeshHit hit,
                samplePositionRange,
                filter))
        {
            hasDestination = false;
            return false;
        }

        agent.isStopped = false;

        if (!agent.SetDestination(hit.position))
        {
            hasDestination = false;
            return false;
        }

        lastDestination = worldPosition;
        hasDestination = true;

        return true;
    }

    private bool TryRecoverToNavMesh()
    {
        NavMeshQueryFilter filter =
            new NavMeshQueryFilter
            {
                agentTypeID = agent.agentTypeID,
                areaMask = agent.areaMask
            };

        float recoveryRange =
            Mathf.Max(
                samplePositionRange * 2f,
                agent.radius * 2f
            );

        if (!NavMesh.SamplePosition(
                transform.position,
                out NavMeshHit hit,
                recoveryRange,
                filter))
        {
            return false;
        }

        if (!agent.Warp(hit.position))
        {
            return false;
        }

        hasDestination = false;
        return agent.isOnNavMesh;
    }

    public void Stop()
    {
        hasDestination = false;

        if (!agent.isOnNavMesh)
            return;

        agent.isStopped = true;
        agent.ResetPath();
    }

    public void Resume()
    {
        if (agent.isOnNavMesh)
        {
            agent.isStopped = false;
        }
    }

    public void SetSpeed(float speed)
    {
        moveSpeed = Mathf.Max(0.1f, speed);
        agent.speed = moveSpeed;
    }

    private void UpdateSawbladeSpin()
    {
        bool moving =
            agent.isOnNavMesh &&
            !agent.isStopped &&
            agent.velocity.sqrMagnitude > 0.01f;

        // Only talk to RotateObject when the state actually changes.
        if (moving == wasMoving)
            return;

        wasMoving = moving;
        rotateObject.SetMoving(moving);
    }

    public bool TryWarpTo(Vector3 worldPosition)
    {
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };

        if (!NavMesh.SamplePosition(
                worldPosition,
                out NavMeshHit hit,
                samplePositionRange,
                filter))
        {
            return false;
        }

        if (!agent.Warp(hit.position))
        {
            return false;
        }

        if (!agent.isOnNavMesh)
        {
            return false;
        }

        agent.ResetPath();
        hasDestination = false;
        wasMoving = false;
        rotateObject.SetMoving(false);

        return true;
    }
}