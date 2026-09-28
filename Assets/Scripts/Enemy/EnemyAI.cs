using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.AI;

[RequireComponent(typeof(EnemyMovement))]
public class EnemyAI : MonoBehaviour
{
    private enum State
    {
        Resting,
        Expanding,
        Hunting,
        Returning
    }

    public enum DifficultyLevel
    {
        Easy,
        Normal,
        Hard,
        Human,
        Advanced,
        Master
    }

    // ------------------------------------------------------------------
    // Difficulty profile: every "personality" number lives here.
    // ------------------------------------------------------------------
    private struct Profile
    {
        public float speed;            // movement speed multiplier
        public float rest;             // rest time multiplier
        public float route;            // route length multiplier
        public float decision;         // seconds between AI decisions (reaction time)
        public int candidates;         // how many routes it "thinks about"
        public float focusedRatio;     // share of routes aimed toward the player
        public float mistakeChance;    // chance to pick a random route instead of the best
        public float dangerAwareness;  // chance per decision to notice danger
        public float dangerRadius;     // how close the player must be to feel dangerous
        public float huntChance;       // chance per check to hunt the player's trail
        public float huntRange;        // max distance to a trail point worth chasing
        public bool huntWhileOutside;  // can switch to hunting mid-expansion
        public float extendChance;     // chance to greedily extend a route
        public float jitter;           // human imprecision on waypoints
        public float areaWeight;       // how much it values capture efficiency
        public float ambitionMin;      // route size variety (min)
        public float ambitionMax;      // route size variety (max)
        public float loopChance;       // chance to use a 3 point loop (bigger capture)
    }

    private static Profile GetProfile(DifficultyLevel level)
    {
        switch (level)
        {
            case DifficultyLevel.Easy:
                return new Profile
                {
                    speed = 0.85f,
                    rest = 1.6f,
                    route = 0.85f,
                    decision = 0.35f,
                    candidates = 5,
                    focusedRatio = 0.3f,
                    mistakeChance = 0.35f,
                    dangerAwareness = 0f,
                    dangerRadius = 0f,
                    huntChance = 0f,
                    huntRange = 0f,
                    huntWhileOutside = false,
                    extendChance = 0f,
                    jitter = 0.6f,
                    areaWeight = 0.5f,
                    ambitionMin = 0.8f,
                    ambitionMax = 1.0f,
                    loopChance = 0f
                };

            case DifficultyLevel.Hard:
                return new Profile
                {
                    speed = 1.1f,
                    rest = 0.75f,
                    route = 1.05f,
                    decision = 0.12f,
                    candidates = 14,
                    focusedRatio = 0.75f,
                    mistakeChance = 0.08f,
                    dangerAwareness = 0.35f,
                    dangerRadius = 2.4f,
                    huntChance = 0.35f,
                    huntRange = 7f,
                    huntWhileOutside = false,
                    extendChance = 0.1f,
                    jitter = 0.3f,
                    areaWeight = 1.5f,
                    ambitionMin = 0.9f,
                    ambitionMax = 1.2f,
                    loopChance = 0.5f
                };

            case DifficultyLevel.Human:
                // Smart but imperfect: varied risk, hesitation, small mistakes.
                return new Profile
                {
                    speed = 1.0f,
                    rest = 0.85f,
                    route = 1.0f,
                    decision = 0.14f,
                    candidates = 16,
                    focusedRatio = 0.5f,
                    mistakeChance = 0.12f,
                    dangerAwareness = 0.6f,
                    dangerRadius = 2.6f,
                    huntChance = 0.5f,
                    huntRange = 8f,
                    huntWhileOutside = true,
                    extendChance = 0.25f,
                    jitter = 0.5f,
                    areaWeight = 1.4f,
                    ambitionMin = 0.7f,
                    ambitionMax = 1.4f,
                    loopChance = 0.55f
                };

            case DifficultyLevel.Advanced:
                return new Profile
                {
                    speed = 1.08f,
                    rest = 0.6f,
                    route = 1.1f,
                    decision = 0.08f,
                    candidates = 22,
                    focusedRatio = 0.7f,
                    mistakeChance = 0.04f,
                    dangerAwareness = 0.85f,
                    dangerRadius = 3.0f,
                    huntChance = 0.75f,
                    huntRange = 10f,
                    huntWhileOutside = true,
                    extendChance = 0.3f,
                    jitter = 0.2f,
                    areaWeight = 2.0f,
                    ambitionMin = 0.8f,
                    ambitionMax = 1.4f,
                    loopChance = 0.75f
                };

            case DifficultyLevel.Master:
                return new Profile
                {
                    speed = 1.15f,
                    rest = 0.45f,
                    route = 1.15f,
                    decision = 0.05f,
                    candidates = 30,
                    focusedRatio = 0.75f,
                    mistakeChance = 0f,
                    dangerAwareness = 1f,
                    dangerRadius = 3.4f,
                    huntChance = 1f,
                    huntRange = 12f,
                    huntWhileOutside = true,
                    extendChance = 0.35f,
                    jitter = 0.1f,
                    areaWeight = 2.5f,
                    ambitionMin = 0.85f,
                    ambitionMax = 1.5f,
                    loopChance = 0.9f
                };

            default: // Normal
                return new Profile
                {
                    speed = 1f,
                    rest = 1f,
                    route = 1f,
                    decision = 0.2f,
                    candidates = 10,
                    focusedRatio = 0.6f,
                    mistakeChance = 0.15f,
                    dangerAwareness = 0.1f,
                    dangerRadius = 2.0f,
                    huntChance = 0.05f,
                    huntRange = 5f,
                    huntWhileOutside = false,
                    extendChance = 0f,
                    jitter = 0.4f,
                    areaWeight = 1f,
                    ambitionMin = 0.9f,
                    ambitionMax = 1.1f,
                    loopChance = 0.25f
                };
        }
    }

    private struct RouteCandidate
    {
        public Vector3 a;
        public Vector3 b;
        public Vector3 c;
        public int count;
        public float score;
    }

    private struct HuntCandidate
    {
        public Vector3 position;
        public float score;
    }

    private readonly List<HuntCandidate> huntCandidates = new List<HuntCandidate>(9);

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
    [Tooltip("Overridden by the difficulty level at setup.")]
    [Min(0.05f)]
    [SerializeField] private float decisionInterval = 0.1f;

    [Header("Difficulty")]
    [SerializeField] private DifficultyLevel difficulty = DifficultyLevel.Normal;

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

    [Header("Hunting")]
    [Tooltip("Max seconds spent chasing the player's trail before giving up.")]
    [Min(1f)]
    [SerializeField] private float huntMaxSeconds = 8f;

    [Header("Tactical Decisions")]
    [Tooltip("Predict player movement, evaluate safe capture loops, and intercept exposed trails. Easy keeps its relaxed behaviour.")]
    [SerializeField] private bool enableTacticalDecisions = true;
    [SerializeField, Range(0f, 1.5f)] private float playerPredictionSeconds = 0.65f;
    [Tooltip("Extra value assigned to stealing player-owned area.")]
    [SerializeField, Min(0f)] private float playerTerritoryPressure = 1.5f;
    [Tooltip("Time allowed without progress before changing the current plan.")]
    [SerializeField, Min(1f)] private float stuckRecoverySeconds = 2f;

    private Vector3 trackedPlayerPosition;
    private Vector3 trackedPlayerVelocity;
    private bool hasTrackedPlayerPosition;
    private bool previouslyTrackedPlayerOutside;
    private Vector3 progressCheckPosition;
    private float lastProgressTime;
    private NavMeshPath tacticalPath;
    private readonly Vector3[] tacticalCorners = new Vector3[64];

    private bool UsesTactics => enableTacticalDecisions &&
                               difficulty != DifficultyLevel.Easy;

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

    [Header("Captured Territory Trail Density")]
    [Tooltip("Visual rows across the cut width. Wild-grass density is unchanged.")]
    [SerializeField, Min(1)]
    private int capturedTrailRows = 5;

    private const int MaxInstancesPerBatch = 1023;

    private readonly List<Matrix4x4[]> cutGrassBatches =
        new List<Matrix4x4[]>(2);

    private Mesh cutGrassMesh;
    private Material cutGrassMaterial;
    private Matrix4x4 cutGrassPrefabMeshLocalMatrix;

    private int activeCutGrassInstanceCount;
    private GrassCutter enemyCutter;
    private UnityEngine.AI.NavMeshAgent navAgent;

    private State state;
    private bool setupDone;
    private bool isAlive = true;
    private bool outsideTerritory;

    private bool wasOutside;

    private Vector3 homePosition;

    // Route the enemy follows while expanding (2-4 waypoints).
    private readonly List<Vector3> route = new List<Vector3>(4);
    private readonly List<RouteCandidate> routeCandidates =
        new List<RouteCandidate>(32);
    private int waypointIndex;
    private int extensionsDone;
    private Vector3 expansionOrigin;

    private float nextDecisionTime;
    private float stateEndTime;

    // Difficulty
    private Profile profile;

    // Player tracking (for hunting + danger)
    private readonly List<Vector3> playerTrailSamples =
        new List<Vector3>(256);
    private bool playerOutside;
    private float nextPlayerSampleTime;
    private float nextHuntCheckTime;
    private float nextHuntRepathTime;

    // My own trail (for danger checks)
    private readonly List<Vector3> ownTrailSamples =
        new List<Vector3>(256);

    // TerritoryManager already reads these two.
    public bool IsAlive => isAlive;
    public bool IsOutsideTerritory => outsideTerritory;

    [Min(0.05f)]
    [SerializeField]
    private float cutGrassTrailPointDistance = 0.1f;

    private Vector3 lastCutGrassTrailPosition;

    private float distanceSinceCapturedTrailSample;

    private EnemySpawner spawner;

    [SerializeField] private GameObject killParticleEffect;

    private PaperPlayerTerritory playerTerritory;

    public void SetSpawner(EnemySpawner enemySpawner)
    {
        spawner = enemySpawner;
    }

    /// <summary>Lets a spawner pick the difficulty. Call before Setup runs.</summary>
    public void SetDifficulty(DifficultyLevel level)
    {
        if (!setupDone)
        {
            difficulty = level;
        }
    }

    private void Awake()
    {
        tacticalPath = new NavMeshPath();
        movement = GetComponent<EnemyMovement>();
        enemyCutter = GetComponent<GrassCutter>();
        navAgent = GetComponent<UnityEngine.AI.NavMeshAgent>();

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

    private void OnGrassWasCut(Vector3 grassPosition)
    {
        if (enemyCutter == null ||
            grassGrid == null ||
            grassGrid.LastCutSource != enemyCutter)
        {
            return;
        }

        AddCutGrassVisual(grassPosition);
    }

    private void AddCutGrassVisual(Vector3 grassPosition)
    {
        if (!enableCutGrassTrail ||
            cutGrassMesh == null ||
            cutGrassMaterial == null ||
            cuttedGrassPrefab == null ||
            territoryManager == null)
        {
            return;
        }

        Vector3 visualPosition = grassPosition;

        visualPosition.y =
            territoryManager.GroundY +
            trailHeightOffset;

        int instanceIndex = activeCutGrassInstanceCount;
        int batchIndex = instanceIndex / MaxInstancesPerBatch;
        int indexInsideBatch = instanceIndex % MaxInstancesPerBatch;

        if (batchIndex >= cutGrassBatches.Count)
        {
            cutGrassBatches.Add(new Matrix4x4[MaxInstancesPerBatch]);
        }

        Matrix4x4 cutTransform =
            Matrix4x4.TRS(
                visualPosition,
                cuttedGrassPrefab.transform.rotation,
                Vector3.one * cuttedGrassScale
            );

        cutGrassBatches[batchIndex][indexInsideBatch] =
            cutTransform * cutGrassPrefabMeshLocalMatrix;

        activeCutGrassInstanceCount++;
    }

    private void InitializeCutGrassVisual()
    {
        if (cuttedGrassPrefab == null)
        {
            return;
        }

        MeshFilter meshFilter =
            cuttedGrassPrefab.GetComponentInChildren<MeshFilter>(true);

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

        cutGrassMesh = meshFilter.sharedMesh;
        cutGrassMaterial = meshRenderer.sharedMaterial;
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
            int batchStart = batchIndex * MaxInstancesPerBatch;
            int remaining = activeCutGrassInstanceCount - batchStart;

            if (remaining <= 0)
            {
                break;
            }

            int drawCount = Mathf.Min(remaining, MaxInstancesPerBatch);

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
        distanceSinceCapturedTrailSample = 0f;

        // Keep batch arrays allocated for reuse.
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
        bool destroyedWithoutDie = isAlive;

        isAlive = false;

        if (territoryManager != null)
        {
            territoryManager.OnInitialized -= HandleTerritoryInitialized;
            territoryManager.RemoveEnemy(this);
        }

        if (spawner != null)
        {
            spawner.NotifyEnemyDied(gameObject);
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
        progressCheckPosition = transform.position;
        lastProgressTime = Time.time;
        EnterResting();
    }

    private void Update()
    {
        if (!isAlive || Time.timeScale <= 0f)
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

        if (playerTerritory != null)
        {
            if (playerTerritory.IsPointOnActiveTrail(
                    transform.position,
                    territoryTouchRadius
                ))
            {
                playerTerritory.OnPlayerDied();
                return;
            }

            UpdatePlayerTracking();
        }
        else
        {
            playerOutside = false;
            playerTrailSamples.Clear();
            hasTrackedPlayerPosition = false;
        }

        outsideTerritory =
            !territoryManager.EnemyTouchesTerritory(
                this,
                territoryTouchRadius
            );

        // Track the actual movement path independently of AI timing.
        UpdateTrail();

        if (!isAlive || Time.time < nextDecisionTime)
        {
            return;
        }

        nextDecisionTime = Time.time + decisionInterval;

        if (UsesTactics && RecoverStalledPlan())
        {
            return;
        }

        switch (state)
        {
            case State.Resting:
                UpdateResting();
                break;

            case State.Expanding:
                UpdateExpanding();
                break;

            case State.Hunting:
                UpdateHunting();
                break;

            case State.Returning:
                UpdateReturning();
                break;
        }
    }

    // ---------------- Player awareness ----------------

    private static float FlatSqr(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    /// <summary>
    /// Remembers where the player walked while outside their territory.
    /// Those points are (roughly) the player's live trail.
    /// </summary>
    private void UpdatePlayerTracking()
    {
        if (!playerTerritory.isActiveAndEnabled)
        {
            playerOutside = false;
            previouslyTrackedPlayerOutside = false;
            playerTrailSamples.Clear();
            hasTrackedPlayerPosition = false;
            return;
        }

        Vector3 playerPos = playerTerritory.transform.position;
        playerOutside = playerTerritory.IsOutsideTerritory;

        if (playerOutside != previouslyTrackedPlayerOutside)
        {
            playerTrailSamples.Clear();
            nextPlayerSampleTime = 0f;
            previouslyTrackedPlayerOutside = playerOutside;
        }

        if (!playerOutside)
        {
            if (playerTrailSamples.Count > 0)
            {
                playerTrailSamples.Clear();
            }

        }

        if (Time.time < nextPlayerSampleTime)
        {
            return;
        }

        const float sampleInterval = 0.1f;

        if (hasTrackedPlayerPosition)
        {
            float elapsed = Mathf.Max(
                sampleInterval,
                Time.time - (nextPlayerSampleTime - sampleInterval));
            Vector3 velocity = (playerPos - trackedPlayerPosition) / elapsed;
            velocity.y = 0f;
            trackedPlayerVelocity = Vector3.Lerp(
                trackedPlayerVelocity, velocity, 0.6f);
        }
        else
        {
            trackedPlayerVelocity = Vector3.zero;
            hasTrackedPlayerPosition = true;
        }

        trackedPlayerPosition = playerPos;
        nextPlayerSampleTime = Time.time + sampleInterval;

        if (!playerOutside)
        {
            return;
        }

        int count = playerTrailSamples.Count;

        if (count > 0 &&
            FlatSqr(playerTrailSamples[count - 1], playerPos) < 0.09f)
        {
            return;
        }

        playerTrailSamples.Add(playerPos);

        if (playerTrailSamples.Count > 250)
        {
            playerTrailSamples.RemoveAt(0);
        }
    }

    /// <summary>
    /// True when the player is close enough to cut my trail or me.
    /// </summary>
    private bool IsThreatened()
    {
        if (playerTerritory == null || profile.dangerRadius <= 0f)
        {
            return false;
        }

        Vector3 me = transform.position;
        Vector3 player = playerTerritory.transform.position;

        float radius = profile.dangerRadius;

        // A player at home can still cut an enemy trail in player territory.
        if (!UsesTactics && !playerOutside)
        {
            radius *= 0.6f;
        }

        float radiusSqr = radius * radius;

        if (FlatSqr(me, player) < radiusSqr)
        {
            return true;
        }

        Vector3 predictedPlayer = player +
            trackedPlayerVelocity * playerPredictionSeconds;
        float trailRadiusSqr = radiusSqr * (UsesTactics ? 0.64f : 0.36f);

        for (int i = 0; i < ownTrailSamples.Count; i++)
        {
            Vector3 end = i + 1 < ownTrailSamples.Count
                ? ownTrailSamples[i + 1]
                : me;
            Vector3 closest = ClosestFlatPoint(player, ownTrailSamples[i], end);

            if (FlatSqr(closest, player) < trailRadiusSqr ||
                (UsesTactics && FlatSqr(
                    ClosestFlatPoint(predictedPlayer, ownTrailSamples[i], end),
                    predictedPlayer) < trailRadiusSqr))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds a point on the player's live trail that is worth cutting.
    /// </summary>
    private bool TryFindHuntTarget(out Vector3 target)
    {
        target = Vector3.zero;

        if (playerTerritory == null ||
            !playerOutside ||
            !playerTerritory.isActiveAndEnabled ||
            playerTrailSamples.Count < 2 ||
            profile.huntRange <= 0f)
        {
            return false;
        }

        Vector3 me = transform.position;
        float rangeSqr = profile.huntRange * profile.huntRange;
        float bestScore = float.PositiveInfinity;
        bool found = false;
        Vector3 player = playerTerritory.transform.position;
        int stride = Mathf.Max(1, playerTrailSamples.Count / 32);
        huntCandidates.Clear();

        for (int i = 0; i < playerTrailSamples.Count - 1; i += stride)
        {
            Vector3 candidate = ClosestFlatPoint(
                me, playerTrailSamples[i],
                playerTrailSamples[Mathf.Min(i + stride, playerTrailSamples.Count - 1)]);
            float distSqr = FlatSqr(candidate, me);

            if (distSqr > rangeSqr ||
                !playerTerritory.IsPointOnActiveTrail(candidate, territoryTouchRadius))
            {
                continue;
            }

            // Prefer a cut away from the moving head, not a body chase.
            float score = Mathf.Sqrt(distSqr) +
                (UsesTactics ? 1.5f / Mathf.Max(0.4f,
                    Mathf.Sqrt(FlatSqr(candidate, player))) : 0f);

            int insertIndex = 0;
            while (insertIndex < huntCandidates.Count &&
                   huntCandidates[insertIndex].score <= score)
            {
                insertIndex++;
            }
            if (insertIndex >= 8) continue;
            huntCandidates.Insert(insertIndex,
                new HuntCandidate { position = candidate, score = score });
            if (huntCandidates.Count > 8) huntCandidates.RemoveAt(8);
        }

        // Expensive navigation checks are limited to the best eight options,
        // regardless of the order in which trail points were recorded.
        float threatTime = UsesTactics && outsideTerritory
            ? GetOwnTrailThreatTime() : float.PositiveInfinity;
        for (int i = 0; i < huntCandidates.Count; i++)
        {
            Vector3 candidate = huntCandidates[i].position;
            if (!TryGetTacticalPath(me, candidate, out Vector3 reachable,
                    out float pathLength) ||
                !playerTerritory.IsPointOnActiveTrail(reachable, territoryTouchRadius))
            {
                continue;
            }

            float travelTime = pathLength / Mathf.Max(0.1f, movement.MoveSpeed);
            if (travelTime > huntMaxSeconds)
            {
                continue;
            }

            // Do not sacrifice a long exposed trail for a distant attack.
            if (travelTime + 0.5f > threatTime)
            {
                continue;
            }

            float score = huntCandidates[i].score + pathLength -
                Mathf.Sqrt(FlatSqr(candidate, me));
            if (score < bestScore)
            {
                bestScore = score;
                target = reachable;
                found = true;
            }
        }

        return found;
    }

    private bool TryStartHunt()
    {
        if (profile.huntChance <= 0f ||
            Time.time < nextHuntCheckTime)
        {
            return false;
        }

        nextHuntCheckTime = Time.time + (UsesTactics ? 0.25f : 0.5f);

        float chance = UsesTactics
            ? Mathf.Max(profile.huntChance,
                difficulty == DifficultyLevel.Normal ? 0.4f : 0.8f)
            : profile.huntChance;

        if (Random.value > chance)
        {
            return false;
        }

        if (!TryFindHuntTarget(out Vector3 target))
        {
            return false;
        }

        if (!movement.SetDestination(target))
        {
            return false;
        }

        ChangeState(State.Hunting);
        stateEndTime = Time.time + huntMaxSeconds;
        nextHuntRepathTime = Time.time + 0.2f;
        return true;
    }

    // ---------------- Resting ----------------

    private void EnterResting()
    {
        ChangeState(State.Resting);
        stateEndTime = Time.time +
            Random.Range(restTimeRange.x, restTimeRange.y);
        if (UsesTactics)
        {
            stateEndTime = Mathf.Min(stateEndTime, Time.time +
                (difficulty == DifficultyLevel.Normal ? 2f : 1.25f));
        }
        progressCheckPosition = transform.position;
        lastProgressTime = Time.time;
    }

    private void UpdateResting()
    {
        if (outsideTerritory)
        {
            BeginReturning();
            return;
        }
        // A smart player attacks an exposed trail instead of waiting.
        if (TryStartHunt())
        {
            return;
        }

        if (Time.time >= stateEndTime)
        {
            BeginExpansion();
            return;
        }

        // Small wander around home so the enemy does not stand frozen.
        if (movement.HasArrived)
        {
            Vector2 offset = Random.insideUnitCircle * restWanderRadius;
            Vector3 destination = homePosition +
                new Vector3(offset.x, 0f, offset.y);
            if (territoryManager.IsInsideEnemyTerritory(this, destination))
            {
                movement.SetDestination(destination);
            }
        }
    }

    // ---------------- Expanding ----------------

    private Vector3 JitterPoint(Vector3 point)
    {
        if (profile.jitter <= 0f)
        {
            return point;
        }

        Vector2 offset = Random.insideUnitCircle * profile.jitter;
        return ClampToMap(point + new Vector3(offset.x, 0f, offset.y));
    }

    private static float Cross2(Vector3 u, Vector3 v)
    {
        return u.x * v.z - u.z * v.x;
    }

    private static float EstimateArea(
        Vector3 o, Vector3 a, Vector3 b, Vector3 c, int count)
    {
        float twice = Cross2(a - o, b - o);

        if (count == 3)
        {
            twice += Cross2(b - o, c - o);
        }

        return Mathf.Abs(twice) * 0.5f;
    }

    private void BeginExpansion()
    {
        Vector3 origin = transform.position;
        Vector3 playerPos = Vector3.zero;
        Vector3 toPlayer = Vector3.zero;
        bool hasPlayer = false;

        if (playerTerritory != null)
        {
            playerPos = playerTerritory.transform.position +
                (UsesTactics ? trackedPlayerVelocity * playerPredictionSeconds : Vector3.zero);
            toPlayer = playerPos - origin;
            toPlayer.y = 0f;
            hasPlayer = toPlayer.sqrMagnitude >= 0.01f;
        }

        // Humans do not take the same size risk every time.
        float ambition =
            Random.Range(profile.ambitionMin, profile.ambitionMax);

        // Careful players shrink their plan when the enemy is close.
        if (hasPlayer &&
            profile.dangerAwareness > 0.3f &&
            toPlayer.magnitude < 6f)
        {
            ambition *= 0.8f;
        }

        int focusedCount =
            Mathf.RoundToInt(profile.candidates * profile.focusedRatio);

        bool checkSafety = hasPlayer && profile.dangerAwareness > 0.3f;
        float safeRadius = profile.dangerRadius * 1.3f;
        float safeRadiusSqr = safeRadius * safeRadius;

        routeCandidates.Clear();

        for (int attempt = 0; attempt < profile.candidates; attempt++)
        {
            Vector3 forward;

            if (hasPlayer && attempt < focusedCount)
            {
                forward =
                    Quaternion.Euler(0f, Random.Range(-65f, 65f), 0f) *
                    toPlayer.normalized;
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

            Vector3 side = new Vector3(-forward.z, 0f, forward.x);

            if (Random.value < 0.5f)
            {
                side = -side;
            }

            float forwardDistance =
                Random.Range(forwardDistanceRange.x, forwardDistanceRange.y) *
                ambition;

            float sideDistance =
                Random.Range(sideDistanceRange.x, sideDistanceRange.y) *
                ambition;

            Vector3 a = ClampToMap(origin + forward * forwardDistance);
            Vector3 b = ClampToMap(a + side * sideDistance);

            if ((a - origin).sqrMagnitude < 1f ||
                (b - a).sqrMagnitude < 1f)
            {
                continue;
            }

            int count = 2;
            Vector3 c = b;

            // 3 point loop: out, across, back. Captures roughly twice the
            // area of the 2 point triangle for a similar risk.
            if (Random.value < profile.loopChance)
            {
                Vector3 loopBack = ClampToMap(
                    b - forward * forwardDistance * Random.Range(0.75f, 1f)
                );

                if ((loopBack - b).sqrMagnitude >= 1f)
                {
                    c = loopBack;
                    count = 3;
                }
            }

            // Human imprecision.
            a = JitterPoint(a);
            b = JitterPoint(b);

            if (count == 3)
            {
                c = JitterPoint(c);
            }

            // Validate the entire loop, not just its first destination.
            float tacticalScore = 0f;
            if (UsesTactics && !TryScoreCaptureRoute(
                    origin, ref a, ref b, ref c, count, out tacticalScore))
            {
                continue;
            }

            float pathLength =
                (a - origin).magnitude +
                (b - a).magnitude +
                (count == 3
                    ? (c - b).magnitude + (c - origin).magnitude
                    : (b - origin).magnitude);

            float area = EstimateArea(origin, a, b, c, count);

            // Capture efficiency: area gained per distance walked.
            float score =
                Random.Range(0f, 0.4f) +
                (area / Mathf.Max(1f, pathLength)) * profile.areaWeight;

            // Cutting into the player's land is worth a lot.
            if (territoryManager.IsInsideTerritory(a)) score += 4f;
            if (territoryManager.IsInsideTerritory((a + b) * 0.5f)) score += 2f;
            if (territoryManager.IsInsideTerritory(b)) score += 6f;
            if (count == 3 && territoryManager.IsInsideTerritory(c)) score += 3f;

            // Walking through enemy land is wasted effort.
            if (territoryManager.IsInsideEnemyTerritory(a)) score -= 2.5f;
            if (territoryManager.IsInsideEnemyTerritory(b)) score -= 2.5f;
            if (count == 3 && territoryManager.IsInsideEnemyTerritory(c)) score -= 2.5f;

            if (hasPlayer)
            {
                Vector3 lastPoint = count == 3 ? c : b;

                score -=
                    Mathf.Sqrt(FlatSqr(lastPoint, playerPos)) * 0.12f;

                // Avoid planning a route right next to the player.
                if (checkSafety)
                {
                    if (FlatSqr(a, playerPos) < safeRadiusSqr) score -= 3f;
                    if (FlatSqr(b, playerPos) < safeRadiusSqr) score -= 3f;
                    if (count == 3 && FlatSqr(c, playerPos) < safeRadiusSqr) score -= 3f;
                }
            }

            if (UsesTactics)
            {
                score = tacticalScore + Random.Range(0f, 0.15f);
            }

            routeCandidates.Add(new RouteCandidate
            {
                a = a,
                b = b,
                c = c,
                count = count,
                score = score
            });
        }

        if (routeCandidates.Count == 0)
        {
            EnterResting();
            return;
        }

        // Pick the best route, except when it "makes a mistake".
        int pick = 0;

        if (routeCandidates.Count > 1 &&
            Random.value < profile.mistakeChance)
        {
            pick = Random.Range(0, routeCandidates.Count);
        }
        else
        {
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < routeCandidates.Count; i++)
            {
                if (routeCandidates[i].score > bestScore)
                {
                    bestScore = routeCandidates[i].score;
                    pick = i;
                }
            }
        }

        RouteCandidate chosen = routeCandidates[pick];

        route.Clear();
        route.Add(chosen.a);
        route.Add(chosen.b);

        if (chosen.count == 3)
        {
            route.Add(chosen.c);
        }

        expansionOrigin = origin;
        extensionsDone = 0;
        waypointIndex = 0;

        if (!movement.SetDestination(route[0]))
        {
            EnterResting();
            return;
        }

        ChangeState(State.Expanding);

        stateEndTime =
            Time.time +
            maxExpandSeconds * Mathf.Max(1f, ambition) +
            (chosen.count - 2) * 2f;
    }

    private void UpdateExpanding()
    {
        if (Time.time >= stateEndTime)
        {
            BeginReturning();
            return;
        }

        if (outsideTerritory)
        {
            // An immediately reachable trail cut can be safer than retreating.
            if (UsesTactics && profile.huntWhileOutside && TryStartHunt())
            {
                return;
            }
            // Notice the player getting close and run home.
            if (profile.dangerAwareness > 0f &&
                Random.value < profile.dangerAwareness &&
                IsThreatened())
            {
                BeginReturning();
                return;
            }

            // Opportunity: the player left a trail nearby.
            if (!UsesTactics && profile.huntWhileOutside && TryStartHunt())
            {
                return;
            }
        }

        if (!movement.HasArrived)
        {
            return;
        }

        waypointIndex++;

        if (waypointIndex < route.Count)
        {
            if (!movement.SetDestination(route[waypointIndex]))
            {
                BeginReturning();
            }

            return;
        }

        // Greedy players sometimes push one waypoint further.
        if (extensionsDone < 1 &&
            Random.value < profile.extendChance &&
            !IsThreatened() &&
            TryExtendRoute())
        {
            return;
        }

        BeginReturning();
    }

    private bool TryExtendRoute()
    {
        if (route.Count == 0)
        {
            return false;
        }

        Vector3 last = route[route.Count - 1];
        Vector3 previous =
            route.Count >= 2 ? route[route.Count - 2] : expansionOrigin;

        Vector3 direction = last - previous;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.01f)
        {
            return false;
        }

        direction.Normalize();

        float distance = Random.Range(
            forwardDistanceRange.x * 0.7f,
            forwardDistanceRange.x * 1.3f
        );

        Vector3 optionLeft = ClampToMap(
            last + Quaternion.Euler(0f, -70f, 0f) * direction * distance
        );

        Vector3 optionRight = ClampToMap(
            last + Quaternion.Euler(0f, 70f, 0f) * direction * distance
        );

        // Curve back toward home so the loop closes nicely.
        Vector3 next =
            FlatSqr(optionLeft, expansionOrigin) <
            FlatSqr(optionRight, expansionOrigin)
                ? optionLeft
                : optionRight;

        if (FlatSqr(next, last) < 1f)
        {
            return false;
        }

        if (UsesTactics &&
            (!TryGetTacticalPath(transform.position, next,
                out next, out float extensionLength) ||
             !IsTacticalLegClear(transform.position, next) ||
             !territoryManager.TryGetNearestEnemyTerritoryPosition(
                 this, next, out Vector3 returnPoint) ||
             !TryGetTacticalPath(next, returnPoint, out _, out float returnLength) ||
             (extensionLength + returnLength) / Mathf.Max(0.1f, movement.MoveSpeed)
                 + 0.75f >= GetOwnTrailThreatTime()))
        {
            return false;
        }

        route.Add(next);
        extensionsDone++;
        waypointIndex = route.Count - 1;
        stateEndTime += 3f;

        return movement.SetDestination(next);
    }

    // ---------------- Hunting ----------------

    private void UpdateHunting()
    {
        if (Time.time >= stateEndTime || !playerOutside ||
            playerTerritory == null || !playerTerritory.isActiveAndEnabled)
        {
            EndHunt();
            return;
        }

        if (Time.time >= nextHuntRepathTime)
        {
            nextHuntRepathTime = Time.time + 0.2f;
            if (!TryFindHuntTarget(out Vector3 target) ||
                !movement.SetDestination(target))
            {
                EndHunt();
            }
        }
    }

    private void EndHunt()
    {
        // Do not spam hunts that failed.
        nextHuntCheckTime = Time.time + (UsesTactics ? 0.75f : 2.5f);

        if (outsideTerritory)
        {
            BeginReturning();
        }
        else
        {
            EnterResting();
        }
    }

    // ---------------- Returning ----------------

    private void BeginReturning()
    {
        ChangeState(State.Returning);

        // Give the return trip longer than the outward trip.
        stateEndTime = Time.time + maxExpandSeconds * 2f;

        if (!TryFindReturnPosition(out Vector3 returnPosition))
        {
            // A temporary carved obstacle is not a reason for immediate death.
            // Keep the existing return timeout while retrying safe home targets.
            movement.Stop();
            nextReturnRepathTime = Time.time + 0.5f;
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

        if (!TryFindReturnPosition(out Vector3 returnPosition))
        {
            return;
        }

        homePosition = returnPosition;
        movement.SetDestination(homePosition);
    }

    // ---------------- Helpers ----------------

    private static Vector3 ClosestFlatPoint(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        segment.y = 0f;
        Vector3 offset = point - start;
        offset.y = 0f;
        float t = segment.sqrMagnitude > 0.000001f
            ? Mathf.Clamp01(Vector3.Dot(offset, segment) / segment.sqrMagnitude)
            : 0f;
        return start + (end - start) * t;
    }

    private NavMeshQueryFilter GetTacticalFilter()
    {
        return new NavMeshQueryFilter
        {
            agentTypeID = navAgent.agentTypeID,
            areaMask = navAgent.areaMask
        };
    }

    private bool TryGetTacticalPath(Vector3 from, Vector3 requested,
        out Vector3 destination, out float length)
    {
        destination = requested;
        length = 0f;
        if (navAgent == null || !navAgent.isActiveAndEnabled || !navAgent.isOnNavMesh)
        {
            return false;
        }

        NavMeshQueryFilter filter = GetTacticalFilter();
        // Blades float above the surface (the prefabs have a base offset).
        // Navigation queries must use ground positions, not the blade centre.
        from.y = territoryManager.GroundY;
        requested.y = territoryManager.GroundY;
        float sampleRadius = Mathf.Max(0.15f, territoryManager.CellSize * 0.5f);
        if (!NavMesh.SamplePosition(from, out NavMeshHit start, sampleRadius, filter) ||
            !NavMesh.SamplePosition(requested, out NavMeshHit end, sampleRadius, filter) ||
            !NavMesh.CalculatePath(start.position, end.position, filter, tacticalPath) ||
            tacticalPath.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        int count = tacticalPath.GetCornersNonAlloc(tacticalCorners);
        if (count == 0 || count >= tacticalCorners.Length)
        {
            return false;
        }

        for (int i = 1; i < count; i++)
        {
            length += Vector3.Distance(tacticalCorners[i - 1], tacticalCorners[i]);
        }
        destination = end.position;
        return true;
    }

    private bool IsTacticalLegClear(Vector3 from, Vector3 to)
    {
        from.y = territoryManager.GroundY;
        to.y = territoryManager.GroundY;
        return !NavMesh.Raycast(from, to, out _, GetTacticalFilter());
    }

    private float GetOwnTrailThreatTime()
    {
        if (playerTerritory == null || !playerTerritory.isActiveAndEnabled ||
            ownTrailSamples.Count == 0)
        {
            return float.PositiveInfinity;
        }

        Vector3 player = playerTerritory.transform.position;
        Vector3 predicted = player + trackedPlayerVelocity * playerPredictionSeconds;
        float closestSqr = float.PositiveInfinity;
        for (int i = 0; i < ownTrailSamples.Count; i++)
        {
            Vector3 end = i + 1 < ownTrailSamples.Count
                ? ownTrailSamples[i + 1] : transform.position;
            closestSqr = Mathf.Min(closestSqr, Mathf.Min(
                FlatSqr(player, ClosestFlatPoint(player, ownTrailSamples[i], end)),
                FlatSqr(predicted, ClosestFlatPoint(predicted, ownTrailSamples[i], end))));
        }

        float playerSpeed = Mathf.Max(1f, trackedPlayerVelocity.magnitude);
        return Mathf.Max(0f, Mathf.Sqrt(closestSqr) - territoryTouchRadius) / playerSpeed;
    }

    private bool TryScoreCaptureRoute(Vector3 origin, ref Vector3 a,
        ref Vector3 b, ref Vector3 c, int count, out float score)
    {
        score = 0f;
        if (!TryGetTacticalPath(origin, a, out a, out float firstLength) ||
            !TryGetTacticalPath(a, b, out b, out float secondLength))
        {
            return false;
        }

        float length = firstLength + secondLength;
        if (count == 3)
        {
            if (!TryGetTacticalPath(b, c, out c, out float thirdLength))
            {
                return false;
            }
            length += thirdLength;
        }

        Vector3 last = count == 3 ? c : b;
        if (!TryGetTacticalPath(last, origin, out _, out float returnLength))
        {
            return false;
        }
        length += returnLength;

        // Detours around obstacles would make the proposed capture polygon wrong.
        if (!IsTacticalLegClear(origin, a) ||
            !IsTacticalLegClear(a, b) ||
            (count == 3 && !IsTacticalLegClear(b, c)) ||
            !IsTacticalLegClear(last, origin))
        {
            return false;
        }

        if (length / Mathf.Max(0.1f, movement.MoveSpeed) > maxExpandSeconds)
        {
            return false;
        }

        float minX = Mathf.Min(origin.x, a.x, b.x, last.x);
        float maxX = Mathf.Max(origin.x, a.x, b.x, last.x);
        float minZ = Mathf.Min(origin.z, a.z, b.z, last.z);
        float maxZ = Mathf.Max(origin.z, a.z, b.z, last.z);
        float sampleArea = (maxX - minX) * (maxZ - minZ) / 36f;
        float value = 0f;
        int usableSamples = 0;

        // Bounded sampling estimates actual new land, rather than rewarding
        // loops that mostly cover this enemy's already-owned territory.
        for (int x = 0; x < 6; x++)
        {
            for (int z = 0; z < 6; z++)
            {
                Vector3 point = new Vector3(
                    Mathf.Lerp(minX, maxX, (x + 0.5f) / 6f), origin.y,
                    Mathf.Lerp(minZ, maxZ, (z + 0.5f) / 6f));
                if (!IsInsideRoutePolygon(point, origin, a, b, c, count) ||
                    territoryManager.IsInsideEnemyTerritory(this, point))
                {
                    continue;
                }
                // Keep other enemies' land out of the preferred capture plan.
                if (territoryManager.IsInsideEnemyTerritory(point))
                {
                    value -= sampleArea;
                    continue;
                }
                usableSamples++;
                value += sampleArea * (territoryManager.IsInsideTerritory(point)
                    ? 1f + playerTerritoryPressure : 1f);
            }
        }

        if (usableSamples == 0)
        {
            return false;
        }
        score = value / Mathf.Max(1f, length) * profile.areaWeight;

        if (playerTerritory != null && playerTerritory.isActiveAndEnabled)
        {
            Vector3 player = playerTerritory.transform.position +
                trackedPlayerVelocity * playerPredictionSeconds;
            float riskSqr = Mathf.Min(
                FlatSqr(player, ClosestFlatPoint(player, origin, a)),
                FlatSqr(player, ClosestFlatPoint(player, a, b)),
                FlatSqr(player, ClosestFlatPoint(player, b, last)),
                FlatSqr(player, ClosestFlatPoint(player, last, origin)));
            float safeRadius = Mathf.Max(0.5f, profile.dangerRadius);
            score -= Mathf.Max(0f, 1f - Mathf.Sqrt(riskSqr) / safeRadius) *
                (2f + profile.dangerAwareness * 3f);
        }
        return true;
    }

    private static bool IsInsideRoutePolygon(Vector3 point, Vector3 origin,
        Vector3 a, Vector3 b, Vector3 c, int count)
    {
        bool inside = false;
        int vertices = count + 1;
        Vector3 previous = count == 3 ? c : b;
        for (int i = 0; i < vertices; i++)
        {
            Vector3 current = i == 0 ? origin : i == 1 ? a : i == 2 ? b : c;
            if ((current.z > point.z) != (previous.z > point.z) &&
                point.x < (previous.x - current.x) * (point.z - current.z) /
                (previous.z - current.z) + current.x)
            {
                inside = !inside;
            }
            previous = current;
        }
        return inside;
    }

    private bool TryFindReturnPosition(out Vector3 destination)
    {
        destination = transform.position;
        if (!territoryManager.TryGetNearestEnemyTerritoryPosition(
                this, transform.position, out Vector3 nearest))
        {
            return false;
        }
        if (!UsesTactics)
        {
            destination = nearest;
            return true;
        }

        bool found = false;
        float bestScore = float.PositiveInfinity;
        // Try the nearest patch, original departure point and home, then
        // nearby alternatives if a new wall has blocked the nearest patch.
        for (int i = 0; i < 15; i++)
        {
            Vector3 candidate;
            if (i == 0) candidate = nearest;
            else if (i == 1) candidate = expansionOrigin;
            else if (i == 2) candidate = homePosition;
            else
            {
                float angle = (i - 3) * Mathf.PI / 6f;
                float radius = territoryManager.CellSize * (i < 9 ? 2f : 4f);
                candidate = nearest + new Vector3(
                    Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            }
            if (!territoryManager.IsInsideEnemyTerritory(this, candidate) ||
                !TryGetTacticalPath(transform.position, candidate,
                    out Vector3 reachable, out float length) ||
                !territoryManager.IsInsideEnemyTerritory(this, reachable))
            {
                continue;
            }
            float score = length;
            if (playerTerritory != null && playerTerritory.isActiveAndEnabled)
            {
                float distance = Mathf.Sqrt(FlatSqr(reachable,
                    playerTerritory.transform.position +
                    trackedPlayerVelocity * playerPredictionSeconds));
                score += Mathf.Max(0f, profile.dangerRadius - distance);
            }
            if (score < bestScore)
            {
                bestScore = score;
                destination = reachable;
                found = true;
            }
        }
        return found;
    }

    private bool RecoverStalledPlan()
    {
        if (state == State.Resting || movement.HasArrived ||
            FlatSqr(progressCheckPosition, transform.position) >= 0.04f)
        {
            progressCheckPosition = transform.position;
            lastProgressTime = Time.time;
            return false;
        }
        if (Time.time - lastProgressTime < stuckRecoverySeconds)
        {
            return false;
        }

        lastProgressTime = Time.time;
        progressCheckPosition = transform.position;
        if (state == State.Returning)
        {
            movement.Stop();
            nextReturnRepathTime = 0f;
            return false;
        }
        if (state == State.Hunting)
        {
            EndHunt();
        }
        else if (outsideTerritory)
        {
            BeginReturning();
        }
        else
        {
            movement.Stop();
            EnterResting();
        }
        return true;
    }

    private Vector3 ClampToMap(Vector3 position)
    {
        Renderer playArea = territoryManager.PlayArea;

        if (playArea == null)
            return position;

        Bounds bounds = playArea.bounds;

        position.x = Mathf.Clamp(
            position.x,
            bounds.min.x + mapEdgeMargin,
            bounds.max.x - mapEdgeMargin);

        position.z = Mathf.Clamp(
            position.z,
            bounds.min.z + mapEdgeMargin,
            bounds.max.z - mapEdgeMargin);

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
            spawner.NotifyEnemyDied(gameObject);
        }

        Debug.Log($"Enemy Died: {name}", this);

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
                ParticleSystem.MainModule main = particleSystem.main;

                Destroy(
                    particle,
                    main.duration + main.startLifetime.constantMax
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

            Vector3 previous = transform.position;

            for (int i = 0; i < route.Count; i++)
            {
                Gizmos.DrawWireSphere(route[i], 0.2f);

                if (i >= waypointIndex)
                {
                    Gizmos.DrawLine(previous, route[i]);
                    previous = route[i];
                }
            }
        }

        if (state == State.Hunting)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, 0.4f);
        }
    }

    private void UpdateTrail()
    {
        Vector3 currentPosition = transform.position;

        if (outsideTerritory)
        {
            if (!wasOutside)
            {
                territoryManager.StartEnemyTrail(
                    this,
                    currentPosition
                );

                ownTrailSamples.Clear();
                ownTrailSamples.Add(currentPosition);

                lastCutGrassTrailPosition = currentPosition;
                distanceSinceCapturedTrailSample = 0f;

                CutCapturedGrassAlongTrail(currentPosition);
            }
            else
            {
                territoryManager.AddEnemyTrailPosition(
                    this,
                    currentPosition
                );

                RecordOwnTrailSample(currentPosition);

                Vector3 segmentStart = lastCutGrassTrailPosition;

                Vector3 delta = currentPosition - segmentStart;
                delta.y = 0f;

                float remainingDistance = delta.magnitude;

                if (remainingDistance > 0.000001f)
                {
                    Vector3 direction = delta / remainingDistance;

                    float spacing = Mathf.Max(
                        0.05f,
                        cutGrassTrailPointDistance
                    );

                    distanceSinceCapturedTrailSample = Mathf.Clamp(
                        distanceSinceCapturedTrailSample,
                        0f,
                        spacing
                    );

                    while (remainingDistance + 0.000001f >=
                           spacing - distanceSinceCapturedTrailSample)
                    {
                        float distanceToSample =
                            spacing - distanceSinceCapturedTrailSample;

                        segmentStart += direction * distanceToSample;

                        remainingDistance = Mathf.Max(
                            0f,
                            remainingDistance - distanceToSample
                        );

                        distanceSinceCapturedTrailSample = 0f;

                        // This function returns immediately on wild grass.
                        // Extra visuals are only added on opponent territory.
                        CutCapturedGrassAlongTrail(segmentStart);
                    }

                    distanceSinceCapturedTrailSample += remainingDistance;
                    lastCutGrassTrailPosition = currentPosition;
                }
            }
        }
        else
        {
            if (wasOutside)
            {
                // Include the return segment before completing capture.
                territoryManager.AddEnemyTrailPosition(
                    this,
                    currentPosition
                );

                territoryManager.CompleteEnemyTrail(this);
                ClearCutGrassTrail();
                ownTrailSamples.Clear();
            }

            lastCutGrassTrailPosition = currentPosition;
            distanceSinceCapturedTrailSample = 0f;
        }

        wasOutside = outsideTerritory;
    }

    private void RecordOwnTrailSample(Vector3 position)
    {
        int count = ownTrailSamples.Count;

        if (count > 0 &&
            FlatSqr(ownTrailSamples[count - 1], position) < 0.16f)
        {
            return;
        }

        ownTrailSamples.Add(position);

        if (ownTrailSamples.Count > 300)
        {
            ownTrailSamples.RemoveAt(0);
        }
    }

    private void CutCapturedGrassAlongTrail(Vector3 worldPosition)
    {
        if (territoryManager == null ||
            !territoryManager.IsInitialized ||
            territoryManager.IsInsideEnemyTerritory(
                this,
                worldPosition))
        {
            return;
        }

        bool onPlayerTerritory =
            territoryManager.IsInsideTerritory(worldPosition);

        bool onEnemyTerritory =
            territoryManager.IsInsideEnemyTerritory(worldPosition);

        // Wild grass continues using OnGrassWasCut.
        if (!onPlayerTerritory && !onEnemyTerritory)
        {
            return;
        }

        float cutRadius = enemyCutter != null
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

            // Existing territory-based particle selection is unchanged.
            enemyCutter.PlayCapturedTerritoryParticle(effectPosition);
        }

        Vector3 direction = Vector3.forward;

        if (navAgent != null && navAgent.isActiveAndEnabled)
        {
            direction = navAgent.velocity;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.000001f)
            {
                direction.Normalize();
            }
            else
            {
                direction = Vector3.forward;
            }
        }

        Vector3 sideways = new Vector3(-direction.z, 0f, direction.x);

        int rows = Mathf.Max(1, capturedTrailRows);

        // Preserve a centre row.
        if (rows % 2 == 0)
        {
            rows++;
        }

        float halfWidth = cutRadius * 0.8f;

        for (int row = 0; row < rows; row++)
        {
            float offset = rows == 1
                ? 0f
                : Mathf.Lerp(
                    -halfWidth,
                    halfWidth,
                    row / (float)(rows - 1)
                );

            Vector3 position = worldPosition + sideways * offset;

            if (territoryManager.IsInsideEnemyTerritory(this, position))
            {
                continue;
            }

            bool onCapturedTerritory =
                territoryManager.IsInsideTerritory(position) ||
                territoryManager.IsInsideEnemyTerritory(position);

            if (onCapturedTerritory)
            {
                AddCutGrassVisual(position);
            }
        }
    }

    private void ApplyDifficultySettings()
    {
        profile = GetProfile(difficulty);

        decisionInterval = profile.decision;

        movement.SetSpeed(movement.MoveSpeed * profile.speed);

        restTimeRange *= profile.rest;
        forwardDistanceRange *= profile.route;
        sideDistanceRange *= profile.route;
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
        ownTrailSamples.Clear();

        outsideTerritory = false;
        wasOutside = false;
        homePosition = transform.position;
        lastCutGrassTrailPosition = transform.position;

        movement.Stop();
        EnterResting();
    }
}
