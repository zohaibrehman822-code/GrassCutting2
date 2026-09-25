using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cuts GPU-instanced grass and plays a pooled cutting effect.
/// No allocations, Instantiate, or Destroy calls occur while cutting.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class GrassCutter : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private GrassCutGrid grassGrid;

    [Header("Cutting")]
    [Min(0.01f)]
    [SerializeField]
    private float cutRadius = 0.25f;

    [Min(0.01f)]
    [SerializeField]
    private float cutInterval = 0.05f;

    [SerializeField]
    private bool includeColliderSize = true;

    [SerializeField]
    private bool cuttingEnabled = true;

    [Header("Cut Particle")]
    [Tooltip("Drag your grass-cutting particle prefab here.")]
    [SerializeField]
    private GameObject cutParticlePrefab;

    [SerializeField] private GameObject capturedTerritoryParticlePrefab;
    private PooledParticle[] capturedTerritoryParticlePool;
    private int nextCapturedParticleIndex;

    [Tooltip("Small fixed pool recommended for low-end devices.")]
    [Range(2, 25)]
    [SerializeField]
    private int particlePoolSize = 25;

    [Tooltip("Only the Y value is applied; bursts stay at the cut blade's exact X/Z position.")]
    [SerializeField]
    private Vector3 particleOffset =
        new Vector3(0f, 0.05f, 0f);

    [Min(0f)]
    [SerializeField] private float minimumForwardDistance = 0.3f;

    [Range(0f, 1f)]
    [SerializeField] private float forwardCone = 0.6f;

    [Tooltip("Number of cut blades between bursts at normal movement speed.")]
    [Min(1)]
    [SerializeField] private int cutsPerFastParticle = 4;

    [Tooltip("Number of cut blades between bursts when moving slowly.")]
    [Min(1)]
    [SerializeField] private int cutsPerSlowParticle = 2;

    [Tooltip("Movement input strength below this value counts as slow.")]
    [Range(0f, 1f)]
    [SerializeField] private float slowMovementThreshold = 0.55f;

    [Tooltip("Extra time before returning the particle to the pool.")]
    [Min(0f)]
    [SerializeField]
    private float cleanupPadding = 0.1f;

    private TerritoryManager territoryManager;
    private EnemyAI enemyAI;
    private float nextOwnTerritoryParticleTime;

    public GameObject CapturedTerritoryParticlePrefab =>
        capturedTerritoryParticlePrefab;

    private sealed class PooledParticle
    {
        public GameObject Root;
        public ParticleSystem[] Systems;
        public float Duration;
        public float ReleaseTime;
        public bool Playing;
    }

    private Collider bladeCollider;

    private PooledParticle[] particlePool;
    private Transform particlePoolRoot;

    private float nextCutTime;
    private int nextParticleIndex;
    private int cutsSinceParticle;

    private Movement playerMovement;

    [Header("Cut Audio")]
    [SerializeField] private AudioSource audioSource;

    [Tooltip("One or more clips; a random one plays each time.")]
    [SerializeField] private AudioClip[] cutAudioClips;

    [Tooltip("Play a sound every N grass blades cut.")]
    [Min(1)]
    [SerializeField] private int cutsPerAudioPlay = 15;

    [Range(0f, 0.2f)]
    [SerializeField] private float audioPitchVariation = 0.08f;

    [Range(0f, 1f)]
    [SerializeField] private float audioVolume = 0.6f;

    private int cutsSinceAudio;
    [Min(0.05f)]
    [SerializeField] private float minAudioInterval = 0.25f;

    private float nextAudioTime;

    [Header("Grass Collection UI")]
    [SerializeField] private GrassCollectUI grassCollectUI;

    private EnemyMovement enemyMovement;

    private void Awake()
    {
        bladeCollider = GetComponent<Collider>();
        playerMovement = GetComponent<Movement>();
        enemyMovement = GetComponent<EnemyMovement>();
        enemyAI = GetComponent<EnemyAI>();

        if (grassGrid == null)
        {
            grassGrid = FindFirstObjectByType<GrassCutGrid>();
        }

        territoryManager = FindFirstObjectByType<TerritoryManager>();

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        InitializeParticlePool();
    }

    public float GetEffectiveCutRadius()
    {
        float effectiveRadius =
            Mathf.Max(0.01f, cutRadius);

        if (includeColliderSize &&
            bladeCollider != null)
        {
            float colliderRadius = 0f;

            if (bladeCollider is BoxCollider boxCollider)
            {
                Vector3 scale =
                    boxCollider.transform.lossyScale;

                float halfWidth =
                    Mathf.Abs(
                        boxCollider.size.x *
                        scale.x
                    ) * 0.5f;

                float halfDepth =
                    Mathf.Abs(
                        boxCollider.size.z *
                        scale.z
                    ) * 0.5f;

                // Use the smaller horizontal dimension.
                // This represents the cutter width without allowing
                // its longer body dimension to widen diagonal cuts.
                colliderRadius =
                    Mathf.Min(
                        halfWidth,
                        halfDepth
                    );
            }
            else if (bladeCollider is SphereCollider sphereCollider)
            {
                Vector3 scale =
                    sphereCollider.transform.lossyScale;

                colliderRadius =
                    sphereCollider.radius *
                    Mathf.Max(
                        Mathf.Abs(scale.x),
                        Mathf.Abs(scale.z)
                    );
            }
            else
            {
                Bounds bounds =
                    bladeCollider.bounds;

                colliderRadius =
                    Mathf.Min(
                        bounds.extents.x,
                        bounds.extents.z
                    );
            }

            effectiveRadius =
                Mathf.Max(
                    effectiveRadius,
                    colliderRadius
                );
        }

        // Keep the physical cutting width inside the same
        // logical width used by the territory grid.
        if (grassGrid != null)
        {
            float territoryRadius =
                Mathf.Max(
                    0.01f,
                    grassGrid.CellSize * 0.5f
                );

            effectiveRadius =
                Mathf.Min(
                    effectiveRadius,
                    territoryRadius
                );
        }

        return Mathf.Max(
            0.01f,
            effectiveRadius
        );
    }

    private void OnEnable()
    {
        nextCutTime = 0f;
        cutsSinceParticle = 0;

        if (grassGrid != null)
        {
            grassGrid.GrassWasCut += OnGrassWasCut;
        }
    }

    private void Update()
    {
        UpdateParticlePool();

        if (!cuttingEnabled ||
            grassGrid == null ||
            Time.time < nextCutTime)
        {
            return;
        }

        nextCutTime = Time.time + cutInterval;

        Vector3 cutPosition =
            bladeCollider != null
                ? bladeCollider.bounds.center
                : transform.position;

        grassGrid.Cut(
            cutPosition,
            GetEffectiveCutRadius(),
            this
        );

        if (territoryManager == null ||
            Time.time < nextOwnTerritoryParticleTime)
        {
            return;
        }

        bool moving = false;
        bool onOwnTerritory = false;

        if (playerMovement != null)
        {
            Vector3 direction = playerMovement.CurrentMoveDirection;
            direction.y = 0f;

            moving = direction.sqrMagnitude > 0.0004f;
            onOwnTerritory =
                territoryManager.IsInsideTerritory(cutPosition);
        }
        else if (enemyMovement != null && enemyAI != null)
        {
            moving = enemyMovement.IsMoving;
            onOwnTerritory =
                territoryManager.EnemyTouchesTerritory(
                    enemyAI,
                    0.01f
                );
        }

        if (!moving || !onOwnTerritory)
        {
            return;
        }

        nextOwnTerritoryParticleTime =
            Time.time +
            cutInterval * Mathf.Max(1, cutsPerFastParticle);

        Vector3 effectPosition = cutPosition;
        effectPosition.y = territoryManager.GroundY;

        PlayCapturedTerritoryParticle(effectPosition);
    }

    private void OnGrassWasCut(Vector3 grassPosition)
    {
        if (!cuttingEnabled)
        {
            return;
        }

        // Ignore grass cut by territory capture or another cutter.
        if (grassGrid != null &&
            grassGrid.LastCutSource != null &&
            grassGrid.LastCutSource != this)
        {
            return;
        }

        float moveStrength = 0f;

        if (playerMovement != null)
        {
            Vector3 movementDirection =
                playerMovement.CurrentMoveDirection;

            movementDirection.y = 0f;
            moveStrength = movementDirection.magnitude;
        }
        else if (enemyMovement != null)
        {
            moveStrength = enemyMovement.IsMoving ? 1f : 0f;
        }
        else
        {
            Rigidbody rb = GetComponent<Rigidbody>();

            if (rb != null)
            {
                Vector3 velocity = rb.linearVelocity;
                velocity.y = 0f;
                moveStrength = velocity.magnitude;
            }
        }

        if (moveStrength < 0.02f)
        {
            return;
        }

        TryPlayCutAudio();

        int cutsPerParticle =
            moveStrength >= slowMovementThreshold
                ? Mathf.Max(1, cutsPerFastParticle)
                : Mathf.Max(1, cutsPerSlowParticle);

        cutsSinceParticle++;

        if (cutsSinceParticle < cutsPerParticle)
        {
            return;
        }

        cutsSinceParticle = 0;

        grassPosition.y += particleOffset.y;
        PlayCutParticle(grassPosition);
    }

    private void TryPlayCutAudio()
    {
        if (audioSource == null || cutAudioClips == null || cutAudioClips.Length == 0)
        {
            //Debug.Log("Con 1");
            return;
        }

        cutsSinceAudio++;

        if (cutsSinceAudio < cutsPerAudioPlay || Time.time < nextAudioTime)
        {
            //Debug.Log("Con 2");
            return;
        }

        cutsSinceAudio = 0;
        nextAudioTime = Time.time + minAudioInterval;

        AudioClip clip = cutAudioClips[Random.Range(0, cutAudioClips.Length)];
        audioSource.pitch = 1f + Random.Range(-audioPitchVariation, audioPitchVariation);
        //Debug.Log("Audio Function");
        audioSource.PlayOneShot(clip, audioVolume);
    }

    private void InitializeParticlePool()
    {
        if (cutParticlePrefab == null &&
            capturedTerritoryParticlePrefab == null)
        {
            return;
        }

        int poolSize = Mathf.Max(2, particlePoolSize);

        GameObject poolObject =
            new GameObject("GrassCutParticlePool");

        particlePoolRoot = poolObject.transform;

        for (int poolType = 0; poolType < 2; poolType++)
        {
            GameObject prefab = poolType == 0
                ? cutParticlePrefab
                : capturedTerritoryParticlePrefab;

            if (prefab == null)
            {
                continue;
            }

            PooledParticle[] pool =
                new PooledParticle[poolSize];

            for (int i = 0; i < poolSize; i++)
            {
                GameObject particleObject =
                    Instantiate(prefab, particlePoolRoot);

                particleObject.name =
                    prefab.name + "_Pooled_" + i;

                ParticleSystem[] systems =
                    particleObject
                        .GetComponentsInChildren<ParticleSystem>(true);

                float duration = CalculateDuration(systems);

                for (int systemIndex = 0;
                     systemIndex < systems.Length;
                     systemIndex++)
                {
                    ParticleSystem system = systems[systemIndex];

                    if (system == null)
                    {
                        continue;
                    }

                    ParticleSystem.MainModule main = system.main;
                    main.loop = false;
                    main.playOnAwake = false;
                    main.stopAction = ParticleSystemStopAction.None;

                    system.Stop(
                        true,
                        ParticleSystemStopBehavior.StopEmittingAndClear
                    );
                }

                particleObject.SetActive(false);

                pool[i] = new PooledParticle
                {
                    Root = particleObject,
                    Systems = systems,
                    Duration = duration
                };
            }

            if (poolType == 0)
            {
                particlePool = pool;
            }
            else
            {
                capturedTerritoryParticlePool = pool;
            }
        }
    }

    private void PlayCutParticle(
    Vector3 worldPosition,
    bool capturedTerritory = false)
    {
        GameObject ownerParticlePrefab =
            territoryManager != null
                ? territoryManager.GetTerritoryCutParticlePrefab(
                    worldPosition
                )
                : null;

        bool useCapturedPool =
            ownerParticlePrefab != null ||
            (capturedTerritory && territoryManager == null);

        PooledParticle[] pool = useCapturedPool
            ? capturedTerritoryParticlePool
            : particlePool;

        GameObject poolPrefab = useCapturedPool
            ? capturedTerritoryParticlePrefab
            : cutParticlePrefab;

        if (pool == null ||
            pool.Length == 0 ||
            poolPrefab == null)
        {
            return;
        }

        int nextIndex = useCapturedPool
            ? nextCapturedParticleIndex
            : nextParticleIndex;

        PooledParticle particle = null;

        for (int i = 0; i < pool.Length; i++)
        {
            int index = (nextIndex + i) % pool.Length;

            if (pool[index].Playing)
            {
                continue;
            }

            particle = pool[index];
            nextIndex = (index + 1) % pool.Length;
            break;
        }

        if (particle == null)
        {
            return;
        }

        if (useCapturedPool)
        {
            nextCapturedParticleIndex = nextIndex;
        }
        else
        {
            nextParticleIndex = nextIndex;
        }

        StopParticle(particle);

        if (ownerParticlePrefab != null)
        {
            ParticleSystem sourceSystem =
                ownerParticlePrefab
                    .GetComponentInChildren<ParticleSystem>(true);

            if (sourceSystem != null)
            {
                ParticleSystem.MainModule sourceMain =
                    sourceSystem.main;

                for (int i = 0; i < particle.Systems.Length; i++)
                {
                    if (particle.Systems[i] == null)
                    {
                        continue;
                    }

                    ParticleSystem.MainModule pooledMain =
                        particle.Systems[i].main;

                    pooledMain.startColor =
                        sourceMain.startColor;
                }
            }
        }

        particle.Root.transform.SetPositionAndRotation(
            worldPosition,
            poolPrefab.transform.rotation
        );

        particle.Root.SetActive(true);

        for (int i = 0; i < particle.Systems.Length; i++)
        {
            if (particle.Systems[i] != null)
            {
                particle.Systems[i].Play(true);
            }
        }

        particle.Playing = true;
        particle.ReleaseTime =
            Time.time + particle.Duration + cleanupPadding;
    }

    public void PlayCapturedTerritoryParticle(
    Vector3 worldPosition)
    {
        if (!cuttingEnabled ||
            capturedTerritoryParticlePrefab == null)
        {
            return;
        }

        worldPosition.y += particleOffset.y;
        PlayCutParticle(worldPosition, true);
    }

    private void UpdateParticlePool()
    {
        float currentTime = Time.time;

        for (int poolType = 0; poolType < 2; poolType++)
        {
            PooledParticle[] pool = poolType == 0
                ? particlePool
                : capturedTerritoryParticlePool;

            if (pool == null)
            {
                continue;
            }

            for (int i = 0; i < pool.Length; i++)
            {
                PooledParticle particle = pool[i];

                if (particle.Playing &&
                    currentTime >= particle.ReleaseTime)
                {
                    StopParticle(particle);
                }
            }
        }
    }

    private static float CalculateDuration(
        ParticleSystem[] systems)
    {
        float longestDuration = 0.1f;

        for (int i = 0;
             i < systems.Length;
             i++)
        {
            ParticleSystem system =
                systems[i];

            if (system == null)
            {
                continue;
            }

            ParticleSystem.MainModule main =
                system.main;

            float duration =
                main.startDelay.constantMax +
                main.duration +
                main.startLifetime.constantMax;

            longestDuration =
                Mathf.Max(
                    longestDuration,
                    duration
                );
        }

        return longestDuration;
    }

    private static void StopParticle(
        PooledParticle particle)
    {
        if (particle == null)
        {
            return;
        }

        for (int i = 0;
             i < particle.Systems.Length;
             i++)
        {
            ParticleSystem system =
                particle.Systems[i];

            if (system != null)
            {
                system.Stop(
                    true,
                    ParticleSystemStopBehavior
                        .StopEmittingAndClear
                );
            }
        }

        if (particle.Root != null)
        {
            particle.Root.SetActive(false);
        }

        particle.Playing = false;
    }

    public void SetCutting(bool enabled)
    {
        cuttingEnabled = enabled;

        if (!enabled)
        {
            cutsSinceParticle = 0;
        }
    }

    private void OnDisable()
    {
        if (grassGrid != null)
        {
            grassGrid.GrassWasCut -= OnGrassWasCut;
        }

        cutsSinceParticle = 0;
        cutsSinceAudio = 0;

        for (int poolType = 0; poolType < 2; poolType++)
        {
            PooledParticle[] pool = poolType == 0
                ? particlePool
                : capturedTerritoryParticlePool;

            if (pool == null)
            {
                continue;
            }

            for (int i = 0; i < pool.Length; i++)
            {
                StopParticle(pool[i]);
            }
        }
    }

    private void OnDestroy()
    {
        if (particlePoolRoot != null)
        {
            Destroy(
                particlePoolRoot.gameObject
            );
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, cutRadius);
    }
}
