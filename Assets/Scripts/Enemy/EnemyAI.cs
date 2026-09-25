using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(EnemyMovement))]
public class EnemyAI : MonoBehaviour
{
    private enum State
    {
        Resting,
        Expanding,
        Returning
    }

    private enum DifficultyLevel
    {
        Easy,
        Normal,
        Hard
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

    [Header("Difficulty")]
    [SerializeField] private DifficultyLevel difficulty = DifficultyLevel.Normal;

    private int focusedExpansionAttempts = 8;

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

    private float nextReturnRepathTime;

    private EnemyMovement movement;

    [Tooltip("Leave empty to use the one on a child object.")]
    [SerializeField] private PlayerTerritoryRenderer territoryRenderer;

    [Header("Cut Grass Trail")]
    [SerializeField] private GrassCutGrid grassGrid;
    [SerializeField] private bool enableCutGrassTrail = true;

    [Tooltip("Used as a mesh/material source; not instantiated per cut.")]
    [SerializeField] private GameObject cuttedGrassPrefab;

    [Min(0.01f)]
    [SerializeField] private float cuttedGrassScale = 0.4f;

    [SerializeField] private float trailHeightOffset = 0.02f;
    [SerializeField] private bool castTrailShadows;
    [SerializeField] private bool receiveTrailShadows;

    private const int MaxInstancesPerBatch = 1023;

    private readonly List<Matrix4x4[]> cutGrassBatches =
        new List<Matrix4x4[]>(2);

    private Mesh cutGrassMesh;
    private Material cutGrassMaterial;
    private Matrix4x4 cutGrassPrefabMeshLocalMatrix;

    private int activeCutGrassInstanceCount;
    private GrassCutter enemyCutter;

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

    [Min(0.05f)]
    [SerializeField]
    private float cutGrassTrailPointDistance = 0.1f;

    private Vector3 lastCutGrassTrailPosition;

    private EnemySpawner spawner;

    [SerializeField] private GameObject killParticleEffect;

    private PaperPlayerTerritory playerTerritory;

    public void SetSpawner(EnemySpawner enemySpawner)
    {
        spawner = enemySpawner;
    }

    private void Awake()
    {
        movement = GetComponent<EnemyMovement>();
        enemyCutter = GetComponent<GrassCutter>();

        if (territoryManager == null)
        {
            territoryManager =
                FindFirstObjectByType<TerritoryManager>();
        }

        if (territoryRenderer == null)
        {
            territoryRenderer =
                GetComponentInChildren<PlayerTerritoryRenderer>(true);
        }

        if (grassGrid == null)
        {
            grassGrid =
                FindFirstObjectByType<GrassCutGrid>();
        }

        InitializeCutGrassVisual();
    }

    private void OnEnable()
    {
        if (grassGrid == null)
        {
            grassGrid =
                FindFirstObjectByType<GrassCutGrid>();
        }

        if (grassGrid != null)
        {
            grassGrid.GrassWasCut += OnGrassWasCut;
        }
    }

    private void LateUpdate()
    {
        DrawCutGrassTrail();
    }

    private void OnGrassWasCut(
    Vector3 grassPosition)
    {
        // GrassWasCut is shared by the player and every enemy.
        // Only accept cuts made by this specific enemy.
        if (enemyCutter == null ||
            grassGrid == null ||
            grassGrid.LastCutSource != enemyCutter)
        {
            return;
        }

        AddCutGrassVisual(
            grassPosition
        );
    }

    private void AddCutGrassVisual(
    Vector3 grassPosition)
    {
        if (!enableCutGrassTrail ||
            cutGrassMesh == null ||
            cutGrassMaterial == null ||
            cuttedGrassPrefab == null ||
            territoryManager == null)
        {
            return;
        }

        Vector3 visualPosition =
            grassPosition;

        visualPosition.y =
            territoryManager.GroundY +
            trailHeightOffset;

        int instanceIndex =
            activeCutGrassInstanceCount;

        int batchIndex =
            instanceIndex /
            MaxInstancesPerBatch;

        int indexInsideBatch =
            instanceIndex %
            MaxInstancesPerBatch;

        if (batchIndex >=
            cutGrassBatches.Count)
        {
            cutGrassBatches.Add(
                new Matrix4x4[
                    MaxInstancesPerBatch
                ]
            );
        }

        Matrix4x4 cutTransform =
            Matrix4x4.TRS(
                visualPosition,
                cuttedGrassPrefab.transform.rotation,
                Vector3.one *
                cuttedGrassScale
            );

        cutGrassBatches[batchIndex]
            [indexInsideBatch] =
                cutTransform *
                cutGrassPrefabMeshLocalMatrix;

        activeCutGrassInstanceCount++;
    }


    private void InitializeCutGrassVisual()
    {
        if (cuttedGrassPrefab == null)
        {
            return;
        }

        MeshFilter meshFilter =
            cuttedGrassPrefab
                .GetComponentInChildren<MeshFilter>(true);

        if (meshFilter == null ||
            meshFilter.sharedMesh == null)
        {
            Debug.LogError(
                "EnemyAI: Cutted grass prefab needs a MeshFilter and mesh.",
                this
            );

            return;
        }

        MeshRenderer meshRenderer =
            meshFilter.GetComponent<MeshRenderer>();

        if (meshRenderer == null ||
            meshRenderer.sharedMaterial == null)
        {
            Debug.LogError(
                "EnemyAI: Cutted grass mesh needs a MeshRenderer and material.",
                this
            );

            return;
        }

        cutGrassMesh =
            meshFilter.sharedMesh;

        cutGrassMaterial =
            meshRenderer.sharedMaterial;

        cutGrassMaterial.enableInstancing = true;

        cutGrassPrefabMeshLocalMatrix =
            cuttedGrassPrefab.transform.worldToLocalMatrix *
            meshFilter.transform.localToWorldMatrix;
    }

    private void DrawCutGrassTrail()
    {
        if (!enableCutGrassTrail ||
            cutGrassMesh == null ||
            cutGrassMaterial == null ||
            activeCutGrassInstanceCount == 0)
        {
            return;
        }

        ShadowCastingMode shadowMode =
            castTrailShadows
                ? ShadowCastingMode.On
                : ShadowCastingMode.Off;

        for (int batchIndex = 0;
             batchIndex < cutGrassBatches.Count;
             batchIndex++)
        {
            int batchStart =
                batchIndex *
                MaxInstancesPerBatch;

            int remaining =
                activeCutGrassInstanceCount -
                batchStart;

            if (remaining <= 0)
            {
                break;
            }

            int drawCount =
                Mathf.Min(
                    remaining,
                    MaxInstancesPerBatch
                );

            Graphics.DrawMeshInstanced(
                cutGrassMesh,
                0,
                cutGrassMaterial,
                cutGrassBatches[batchIndex],
                drawCount,
                null,
                shadowMode,
                receiveTrailShadows,
                gameObject.layer
            );
        }
    }

    private void ClearCutGrassTrail()
    {
        activeCutGrassInstanceCount = 0;

        // Keep the allocated batch arrays so they can be reused
        // during the enemy's next expansion.
    }

    private void OnDisable()
    {
        if (grassGrid != null)
        {
            grassGrid.GrassWasCut -= OnGrassWasCut;
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
        bool destroyedWithoutDie =
            isAlive;

        isAlive = false;

        if (territoryManager != null)
        {
            territoryManager.OnInitialized -=
                HandleTerritoryInitialized;

            territoryManager.RemoveEnemy(this);
        }

        if (spawner != null)
        {
            spawner.NotifyEnemyDied(
                gameObject
            );
        }

        if (destroyedWithoutDie &&
            Application.isPlaying &&
            gameObject.scene.IsValid() &&
            gameObject.scene.isLoaded &&
            territoryManager != null)
        {
            territoryManager.CheckWinCondition();
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

        ApplyDifficultySettings();

        homePosition = transform.position;

        if (territoryRenderer != null)
        {
            territoryManager.RegisterEnemyRenderer(
                this,
                territoryRenderer
            );
        }
        else
        {
            Debug.LogWarning(
                "EnemyAI: No PlayerTerritoryRenderer found. " +
                "Enemy territory will be invisible.",
                this
            );
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
        if (!isAlive)
        {
            return;
        }

        if (!setupDone)
        {
            if (territoryManager != null &&
                territoryManager.IsInitialized)
            {
                Setup();
            }

            return;
        }

        if (playerTerritory == null)
        {
            playerTerritory =
                FindFirstObjectByType<PaperPlayerTerritory>();
        }

        if (playerTerritory != null &&
            playerTerritory.IsPointOnActiveTrail(
                transform.position,
                territoryTouchRadius
            ))
        {
            playerTerritory.OnPlayerDied();
            return;
        }

        if (Time.time < nextDecisionTime)
        {
            return;
        }

        nextDecisionTime =
            Time.time + decisionInterval;

        outsideTerritory =
            !territoryManager.EnemyTouchesTerritory(
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
        Vector3 toPlayer = Vector3.zero;
        bool hasPlayer = playerTerritory != null;

        if (hasPlayer)
        {
            toPlayer =
                playerTerritory.transform.position -
                transform.position;
            toPlayer.y = 0f;

            if (toPlayer.sqrMagnitude < 0.01f)
            {
                hasPlayer = false;
            }
        }

        float bestScore = float.NegativeInfinity;
        Vector3 bestWaypointA = transform.position;
        Vector3 bestWaypointB = transform.position;

        for (int attempt = 0; attempt < 12; attempt++)
        {
            Vector3 forward;

            if (hasPlayer &&
                attempt < focusedExpansionAttempts)
            {
                forward =
                    Quaternion.Euler(
                        0f,
                        Random.Range(-65f, 65f),
                        0f
                    ) * toPlayer.normalized;
            }
            else
            {
                Vector2 randomDirection =
                    Random.insideUnitCircle.normalized;

                forward = new Vector3(
                    randomDirection.x,
                    0f,
                    randomDirection.y
                );
            }

            if (forward.sqrMagnitude < 0.01f)
            {
                continue;
            }

            Vector3 side =
                new Vector3(-forward.z, 0f, forward.x);

            if (Random.value < 0.5f)
            {
                side = -side;
            }

            float forwardDistance = Random.Range(
                forwardDistanceRange.x,
                forwardDistanceRange.y
            );

            float sideDistance = Random.Range(
                sideDistanceRange.x,
                sideDistanceRange.y
            );

            Vector3 candidateA = ClampToMap(
                transform.position +
                forward * forwardDistance
            );

            Vector3 candidateB = ClampToMap(
                candidateA +
                side * sideDistance
            );

            if ((candidateA - transform.position)
                    .sqrMagnitude < 1f ||
                (candidateB - candidateA)
                    .sqrMagnitude < 1f)
            {
                continue;
            }

            float score = Random.Range(0f, 0.4f);

            if (territoryManager.IsInsideTerritory(candidateA))
            {
                score += 4f;
            }

            if (territoryManager.IsInsideTerritory(
                    (candidateA + candidateB) * 0.5f))
            {
                score += 2f;
            }

            if (territoryManager.IsInsideTerritory(candidateB))
            {
                score += 6f;
            }

            if (territoryManager.IsInsideEnemyTerritory(candidateB))
            {
                score -= 3f;
            }

            if (hasPlayer)
            {
                Vector2 candidatePoint =
                    new Vector2(candidateB.x, candidateB.z);

                Vector2 playerPoint =
                    new Vector2(
                        playerTerritory.transform.position.x,
                        playerTerritory.transform.position.z
                    );

                score -=
                    Vector2.Distance(
                        candidatePoint,
                        playerPoint
                    ) * 0.12f;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestWaypointA = candidateA;
                bestWaypointB = candidateB;
            }
        }

        if (float.IsNegativeInfinity(bestScore))
        {
            EnterResting();
            return;
        }

        waypointA = bestWaypointA;
        waypointB = bestWaypointB;
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

        // Give the return trip longer than the outward trip.
        stateEndTime =
            Time.time + maxExpandSeconds * 2f;

        if (!territoryManager
            .TryGetNearestEnemyTerritoryPosition(
                this,
                transform.position,
                out Vector3 returnPosition))
        {
            Die();
            return;
        }

        homePosition = returnPosition;
        movement.SetDestination(homePosition);

        nextReturnRepathTime = Time.time + 0.5f;
    }

    private void UpdateReturning()
    {
        if (!outsideTerritory)
        {
            EnterResting();
            return;
        }

        if (Time.time >= stateEndTime)
        {
            Debug.LogWarning(
                $"{name}: Could not return to enemy territory. " +
                "Removing the stuck enemy.",
                this
            );

            Die();
            return;
        }

        if (Time.time < nextReturnRepathTime)
        {
            return;
        }

        nextReturnRepathTime = Time.time + 0.5f;

        if (!territoryManager
            .TryGetNearestEnemyTerritoryPosition(
                this,
                transform.position,
                out Vector3 returnPosition))
        {
            Die();
            return;
        }

        homePosition = returnPosition;
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
        if (!isAlive)
        {
            return;
        }

        isAlive = false;

        if (movement != null)
        {
            movement.Stop();
        }

        if (territoryManager != null)
        {
            territoryManager.RemoveEnemy(this);
        }

        if (spawner != null)
        {
            spawner.NotifyEnemyDied(
                gameObject
            );
        }

        Debug.Log(
            $"Enemy Died: {name}",
            this
        );

        if (killParticleEffect != null)
        {
            GameObject particle =
                Instantiate(
                    killParticleEffect,
                    transform.position,
                    Quaternion.identity
                );

            ParticleSystem particleSystem =
                particle.GetComponent<ParticleSystem>();

            if (particleSystem != null)
            {
                ParticleSystem.MainModule main =
                    particleSystem.main;

                Destroy(
                    particle,
                    main.duration +
                    main.startLifetime.constantMax
                );
            }
            else
            {
                Destroy(particle, 2f);
            }
        }

        if (territoryManager != null)
        {
            territoryManager.CheckWinCondition();
        }

        UIManager.Instance.ShowPlayerDiedText();
        Destroy(gameObject);
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
            Vector3 currentPosition = transform.position;

            if (!wasOutside)
            {
                territoryManager.StartEnemyTrail(
                    this,
                    currentPosition
                );

                lastCutGrassTrailPosition =
                    currentPosition;

                CutCapturedGrassAlongTrail(
                    currentPosition
                );
            }
            else
            {
                territoryManager.AddEnemyTrailPosition(
                    this,
                    currentPosition
                );

                Vector3 movement =
                    currentPosition -
                    lastCutGrassTrailPosition;

                movement.y = 0f;

                float requiredDistanceSqr =
                    cutGrassTrailPointDistance *
                    cutGrassTrailPointDistance;

                if (movement.sqrMagnitude >= requiredDistanceSqr)
                {
                    lastCutGrassTrailPosition =
                        currentPosition;

                    CutCapturedGrassAlongTrail(
                        currentPosition
                    );
                }
            }
        }
        else if (wasOutside)
        {
            territoryManager.CompleteEnemyTrail(this);
            ClearCutGrassTrail();
        }

        wasOutside = outsideTerritory;
    }

    private void CutCapturedGrassAlongTrail(
    Vector3 worldPosition)
    {
        bool onPlayerTerritory =
            territoryManager.IsInsideTerritory(
                worldPosition
            );

        bool onEnemyTerritory =
            !onPlayerTerritory &&
            territoryManager.IsInsideEnemyTerritory(
                worldPosition
            );

        if (!onPlayerTerritory && !onEnemyTerritory)
        {
            return;
        }

        float cutRadius =
            enemyCutter != null
                ? enemyCutter.GetEffectiveCutRadius()
                : territoryManager.CellSize * 0.5f;

        bool cutGrass = onPlayerTerritory
            ? territoryManager.CutPlayerTerritoryGrass(
                worldPosition,
                cutRadius
            )
            : territoryManager.CutEnemyTerritoryGrass(
                worldPosition,
                cutRadius
            );

        if (cutGrass && enemyCutter != null)
        {
            Vector3 effectPosition = worldPosition;
            effectPosition.y = territoryManager.GroundY;

            enemyCutter.PlayCapturedTerritoryParticle(
                effectPosition
            );
        }

        AddCutGrassVisual(worldPosition);
    }

    private void ApplyDifficultySettings()
    {
        float speedMultiplier;
        float restMultiplier;
        float routeMultiplier;

        switch (difficulty)
        {
            case DifficultyLevel.Easy:
                speedMultiplier = 0.85f;
                restMultiplier = 1.6f;
                routeMultiplier = 0.85f;
                focusedExpansionAttempts = 4;
                break;

            case DifficultyLevel.Hard:
                speedMultiplier = 1.15f;
                restMultiplier = 0.7f;
                routeMultiplier = 1.1f;
                focusedExpansionAttempts = 10;
                break;

            default:
                speedMultiplier = 1f;
                restMultiplier = 1f;
                routeMultiplier = 1f;
                focusedExpansionAttempts = 8;
                break;
        }

        movement.SetSpeed(
            movement.MoveSpeed * speedMultiplier
        );

        restTimeRange *= restMultiplier;
        forwardDistanceRange *= routeMultiplier;
        sideDistanceRange *= routeMultiplier;
    }

    public void MoveOutsidePlayerTerritory()
    {
        if (!isAlive ||
            !setupDone ||
            territoryManager == null ||
            movement == null ||
            !territoryManager.IsInsideTerritory(transform.position))
        {
            return;
        }

        if (!territoryManager.TryGetNearestEnemyTerritoryPosition(
                this,
                transform.position,
                out Vector3 returnPosition) ||
            !movement.TryWarpTo(returnPosition) ||
            territoryManager.IsInsideTerritory(transform.position))
        {
            Debug.LogWarning(
                $"{name}: No safe NavMesh position was found in its own territory.",
                this
            );
            Die();
            return;
        }

        territoryManager.CancelEnemyTrail(this);
        ClearCutGrassTrail();

        outsideTerritory = false;
        wasOutside = false;
        homePosition = transform.position;
        lastCutGrassTrailPosition = transform.position;

        movement.Stop();
        EnterResting();
    }
}
