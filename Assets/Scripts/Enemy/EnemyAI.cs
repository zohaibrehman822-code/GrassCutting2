using UnityEngine;

[RequireComponent(typeof(RotateObject))]
[RequireComponent(typeof(GrassCutter))]
[RequireComponent(typeof(Rigidbody))]
public class EnemyAI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TerritoryManager territoryManager;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Trail")]
    [SerializeField] private float territoryTouchRadius = 0.15f;
    [SerializeField] private float trailCollisionRadius = 0.3f;

    [Header("AI")]
    [SerializeField] private float wanderRadius = 6f;
    [SerializeField] private float wanderInterval = 2f;
    [SerializeField] private float expandChance = 0.4f;

    [Header("Starting Territory")]
    [Min(1)]
    [SerializeField] private int startingWidth = 3;
    [Min(1)]
    [SerializeField] private int startingHeight = 3;

    private Rigidbody rb;
    private RotateObject rotateObject;
    private GrassCutter grassCutter;

    private bool outsideTerritory;
    private bool isAlive = true;
    private Vector3 wanderTarget;
    private float nextWanderTime;

    public bool IsAlive => isAlive;
    public bool IsOutsideTerritory => outsideTerritory;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rotateObject = GetComponent<RotateObject>();
        grassCutter = GetComponent<GrassCutter>();

        rb.constraints =
            RigidbodyConstraints.FreezePositionY |
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;

        if (territoryManager == null)
            territoryManager = FindFirstObjectByType<TerritoryManager>();
    }

    private void Start()
    {
        if (territoryManager != null)
        {
            territoryManager.CreateEnemyStartingTerritory(
                transform.position,
                startingWidth,
                startingHeight
            );

            territoryManager.RegisterEnemyHome(this);
        }

        nextWanderTime = Time.time + wanderInterval;
        PickNewWanderTarget();
    }

    private void OnDestroy()
    {
        if (territoryManager != null)
        {
            territoryManager.RemoveEnemy(this);
        }
    }

    private void FixedUpdate()
    {
        if (!isAlive || territoryManager == null) return;

        bool touchingTerritory = territoryManager.TouchesTerritory(
            transform.position,
            territoryTouchRadius
        );

        if (!outsideTerritory)
        {
            if (!touchingTerritory)
            {
                BeginTrail();
            }
            else
            {
                CheckPlayerTrailCollision();
                AIWander();
            }
        }
        else
        {
            territoryManager.AddEnemyTrailPosition(this, transform.position);

            if (touchingTerritory)
            {
                CompleteTrail();
            }
            else
            {
                CheckPlayerTrailCollision();
                NavigateHome();
            }
        }

        UpdateRotation();
    }

    private void CheckPlayerTrailCollision()
    {
        if (territoryManager.IsEnemyInsidePlayerTrail(this, trailCollisionRadius))
        {
            Debug.Log("Enemy Died - Enemy touched player trail");
            Die();
        }
    }

    private void AIWander()
    {
        if (Time.time >= nextWanderTime)
        {
            nextWanderTime = Time.time + wanderInterval;

            if (Random.value < expandChance)
            {
                Vector3 awayFromHome = transform.position - territoryManager.GetEnemyHomePosition(this);
                awayFromHome.y = 0f;

                if (awayFromHome.sqrMagnitude < 0.1f)
                {
                    Vector3 random = Random.insideUnitSphere;
                    random.y = 0f;
                    awayFromHome = random;
                }

                Vector3 expandTarget = transform.position + awayFromHome.normalized * wanderRadius;
                wanderTarget = ClampToMapBounds(expandTarget);
            }
            else
            {
                PickNewWanderTarget();
            }
        }

        MoveToward(wanderTarget);
    }

    private void NavigateHome()
    {
        Vector3 homeCenter = territoryManager.GetEnemyHomePosition(this);
        Vector3 toHome = homeCenter - transform.position;
        toHome.y = 0f;

        if (toHome.sqrMagnitude > 0.5f)
        {
            MoveToward(homeCenter);
        }
        else
        {
            PickNewWanderTarget();
            MoveToward(wanderTarget);
        }
    }

    private void MoveToward(Vector3 target)
    {
        Vector3 direction = (target - transform.position);
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.1f)
        {
            StopMovement();
            return;
        }

        Vector3 move = direction.normalized * moveSpeed;
        rb.linearVelocity = new Vector3(move.x, 0f, move.z);
    }

    private void UpdateRotation()
    {
        bool isMoving = rb.linearVelocity.sqrMagnitude > 0.1f;
        rotateObject.SetMoving(isMoving);
    }

    private void StopMovement()
    {
        rb.linearVelocity = Vector3.zero;
        rotateObject.SetMoving(false);
    }

    private void PickNewWanderTarget()
    {
        Vector3 randomOffset = new Vector3(
            Random.Range(-wanderRadius, wanderRadius),
            0f,
            Random.Range(-wanderRadius, wanderRadius)
        );

        wanderTarget = ClampToMapBounds(transform.position + randomOffset);
    }

    private Vector3 ClampToMapBounds(Vector3 position)
    {
        Renderer playArea = territoryManager.PlayArea;
        if (playArea == null) return position;

        Bounds bounds = playArea.bounds;
        position.x = Mathf.Clamp(position.x, bounds.min.x + 0.5f, bounds.max.x - 0.5f);
        position.z = Mathf.Clamp(position.z, bounds.min.z + 0.5f, bounds.max.z - 0.5f);
        return position;
    }

    private void BeginTrail()
    {
        outsideTerritory = true;
        territoryManager.StartEnemyTrail(this, transform.position);
    }

    private void CompleteTrail()
    {
        outsideTerritory = false;
        territoryManager.CompleteEnemyTrail(this);
    }

    public void Die()
    {
        if (!isAlive) return;

        isAlive = false;
        StopMovement();
        rotateObject.StopRotation();
        grassCutter.SetCutting(false);

        if (territoryManager != null)
            territoryManager.RemoveEnemy(this);

        gameObject.SetActive(false);
    }
}
