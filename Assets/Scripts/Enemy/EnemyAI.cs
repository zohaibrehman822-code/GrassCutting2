using UnityEngine;

[RequireComponent(typeof(EnemyMovement))]
public class EnemyAI : MonoBehaviour
{
    private enum State
    {
        Resting,
        Expanding,
        Returning
    }

    [Header("References")]
    [Tooltip("Leave empty to find it automatically.")]
    [SerializeField] private TerritoryManager territoryManager;

    [Header("Starting Territory")]
    [Min(1)]
    [SerializeField] private int startingWidth = 3;
    [Min(1)]
    [SerializeField] private int startingHeight = 3;

    [Header("Territory Check")]
    [Min(0.01f)]
    [SerializeField] private float territoryTouchRadius = 0.15f;

    [Header("AI Timing")]
    [Tooltip("The AI thinks this often, not every frame.")]
    [Min(0.05f)]
    [SerializeField] private float decisionInterval = 0.1f;

    [Tooltip("Seconds the enemy stays home before leaving (random between X and Y).")]
    [SerializeField] private Vector2 restTimeRange = new Vector2(1f, 3f);

    [Min(0.1f)]
    [SerializeField] private float restWanderRadius = 0.5f;

    [Header("Expansion")]
    [Tooltip("Distance of the first waypoint (random between X and Y).")]
    [SerializeField] private Vector2 forwardDistanceRange = new Vector2(2.5f, 5f);

    [Tooltip("Sideways distance of the second waypoint (random between X and Y).")]
    [SerializeField] private Vector2 sideDistanceRange = new Vector2(2f, 4f);

    [Tooltip("Safety: give up and return home after this many seconds outside.")]
    [Min(1f)]
    [SerializeField] private float maxExpandSeconds = 12f;

    [Tooltip("Keeps waypoints away from the plane edge.")]
    [Min(0f)]
    [SerializeField] private float mapEdgeMargin = 0.75f;

    [Header("Debug")]
    [SerializeField] private bool logStateChanges;

    private EnemyMovement movement;

    [Tooltip("Leave empty to use the one on a child object.")]
    [SerializeField] private PlayerTerritoryRenderer territoryRenderer;

    private State state;
    private bool setupDone;
    private bool isAlive = true;
    private bool outsideTerritory;

    private bool wasOutside;

    private Vector3 homePosition;
    private Vector3 waypointA;
    private Vector3 waypointB;
    private int waypointIndex;

    private float nextDecisionTime;
    private float stateEndTime;

    // TerritoryManager already reads these two.
    public bool IsAlive => isAlive;
    public bool IsOutsideTerritory => outsideTerritory;

    private void Awake()
    {
        movement = GetComponent<EnemyMovement>();

        if (territoryManager == null)
        {
            territoryManager = FindFirstObjectByType<TerritoryManager>();
        }

        if (territoryRenderer == null)
        {
            territoryRenderer = GetComponentInChildren<PlayerTerritoryRenderer>(true);
        }
    }

    private void Start()
    {
        if (territoryManager == null)
        {
            Debug.LogError("EnemyAI: No TerritoryManager found.", this);
            enabled = false;
            return;
        }

        // The spawner can run before the manager is ready, so wait if needed.
        if (territoryManager.IsInitialized)
        {
            Setup();
        }
        else
        {
            territoryManager.OnInitialized += HandleTerritoryInitialized;
        }
    }

    private void OnDestroy()
    {
        if (territoryManager != null)
        {
            territoryManager.OnInitialized -= HandleTerritoryInitialized;
            territoryManager.RemoveEnemy(this);
        }
    }

    private void HandleTerritoryInitialized(TerritoryManager manager)
    {
        territoryManager.OnInitialized -= HandleTerritoryInitialized;
        Setup();
    }

    private void Setup()
    {
        if (setupDone) return;

        homePosition = transform.position;

        if (territoryRenderer != null)
        {
            territoryManager.RegisterEnemyRenderer(this, territoryRenderer);
        }
        else
        {
            Debug.LogWarning("EnemyAI: No PlayerTerritoryRenderer found. Enemy territory will be invisible.", this);
        }

        territoryManager.CreateEnemyStartingTerritory(
            this,
            transform.position,
            startingWidth,
            startingHeight
        );

        territoryManager.RegisterEnemyHome(this);

        setupDone = true;
        EnterResting();
    }

    private void Update()
    {
        if (!isAlive || !setupDone) return;

        if (Time.time < nextDecisionTime) return;
        nextDecisionTime = Time.time + decisionInterval;

        outsideTerritory = !territoryManager.EnemyTouchesTerritory(
            this,
            territoryTouchRadius
        );

        UpdateTrail();

        switch (state)
        {
            case State.Resting:
                UpdateResting();
                break;

            case State.Expanding:
                UpdateExpanding();
                break;

            case State.Returning:
                UpdateReturning();
                break;
        }
    }

    // ---------------- Resting ----------------

    private void EnterResting()
    {
        ChangeState(State.Resting);
        stateEndTime = Time.time + Random.Range(restTimeRange.x, restTimeRange.y);
    }

    private void UpdateResting()
    {
        if (Time.time >= stateEndTime)
        {
            BeginExpansion();
            return;
        }

        // Small wander around home so the enemy does not stand frozen.
        if (movement.HasArrived)
        {
            Vector2 offset = Random.insideUnitCircle * restWanderRadius;
            movement.SetDestination(homePosition + new Vector3(offset.x, 0f, offset.y));
        }
    }

    // ---------------- Expanding ----------------

    private void BeginExpansion()
    {
        Vector2 direction2D = Random.insideUnitCircle.normalized;
        Vector3 forward = new Vector3(direction2D.x, 0f, direction2D.y);
        Vector3 side = new Vector3(-forward.z, 0f, forward.x);

        if (Random.value < 0.5f)
        {
            side = -side;
        }

        float forwardDistance = Random.Range(forwardDistanceRange.x, forwardDistanceRange.y);
        float sideDistance = Random.Range(sideDistanceRange.x, sideDistanceRange.y);

        waypointA = ClampToMap(transform.position + forward * forwardDistance);
        waypointB = ClampToMap(waypointA + side * sideDistance);
        waypointIndex = 0;

        if (!movement.SetDestination(waypointA))
        {
            EnterResting();
            return;
        }

        ChangeState(State.Expanding);
        stateEndTime = Time.time + maxExpandSeconds;
    }

    private void UpdateExpanding()
    {
        if (Time.time >= stateEndTime)
        {
            BeginReturning();
            return;
        }

        if (!movement.HasArrived)
            return;

        if (waypointIndex == 0)
        {
            waypointIndex = 1;

            if (!movement.SetDestination(waypointB))
            {
                BeginReturning();
            }
        }
        else
        {
            BeginReturning();
        }
    }

    // ---------------- Returning ----------------

    private void BeginReturning()
    {
        ChangeState(State.Returning);
        movement.SetDestination(homePosition);
    }

    private void UpdateReturning()
    {
        // Back inside territory: done.
        if (!outsideTerritory)
        {
            EnterResting();
            return;
        }

        // Ignored by EnemyMovement if the destination did not change.
        movement.SetDestination(homePosition);
    }

    // ---------------- Helpers ----------------

    private Vector3 ClampToMap(Vector3 position)
    {
        Renderer playArea = territoryManager.PlayArea;

        if (playArea == null)
            return position;

        Bounds bounds = playArea.bounds;

        position.x = Mathf.Clamp(position.x, bounds.min.x + mapEdgeMargin, bounds.max.x - mapEdgeMargin);
        position.z = Mathf.Clamp(position.z, bounds.min.z + mapEdgeMargin, bounds.max.z - mapEdgeMargin);

        return position;
    }

    private void ChangeState(State newState)
    {
        if (state == newState) return;

        state = newState;

        if (logStateChanges)
        {
            Debug.Log($"{name}: {state}", this);
        }
    }

    // Temporary version. Step 10 rewrites this properly.
    public void Die()
    {
        if (!isAlive) return;

        isAlive = false;
        movement.Stop();

        if (territoryManager != null)
        {
            territoryManager.RemoveEnemy(this);
        }

        Debug.Log("Enemy Died");
        gameObject.SetActive(false);
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !setupDone) return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(homePosition, 0.3f);

        if (state == State.Expanding)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, waypointIndex == 0 ? waypointA : waypointB);
            Gizmos.DrawWireSphere(waypointA, 0.2f);
            Gizmos.DrawWireSphere(waypointB, 0.2f);
        }
    }

    private void UpdateTrail()
    {
        if (outsideTerritory)
        {
            if (!wasOutside)
            {
                territoryManager.StartEnemyTrail(this, transform.position);
            }
            else
            {
                territoryManager.AddEnemyTrailPosition(this, transform.position);
            }
        }
        else if (wasOutside)
        {
            territoryManager.CompleteEnemyTrail(this);
        }

        wasOutside = outsideTerritory;
    }
}