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

    private void Awake()
    {
        bladeCollider = GetComponent<Collider>();
        playerMovement = GetComponent<Movement>();

        if (grassGrid == null)
        {
            grassGrid =
                FindFirstObjectByType<GrassCutGrid>();
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        InitializeParticlePool();
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

        nextCutTime =
            Time.time + cutInterval;

        Vector3 cutPosition =
            bladeCollider != null
                ? bladeCollider.bounds.center
                : transform.position;

        float effectiveRadius = cutRadius;

        if (includeColliderSize &&
            bladeCollider != null)
        {
            Bounds bounds =
                bladeCollider.bounds;

            float colliderRadius =
                Mathf.Max(
                    bounds.extents.x,
                    bounds.extents.z
                );

            effectiveRadius =
                Mathf.Max(
                    cutRadius,
                    colliderRadius
                );
        }

        grassGrid.Cut(cutPosition, effectiveRadius);
    }

    private void OnGrassWasCut(Vector3 grassPosition)
    {
        if (!cuttingEnabled)
        {
            return;
        }

        Vector3 direction = Vector3.zero;
        float moveStrength = 0f;

        if (playerMovement != null)
        {
            direction = playerMovement.CurrentMoveDirection;
            direction.y = 0f;
            moveStrength = direction.magnitude;
        }
        else
        {
            Rigidbody rb = GetComponent<Rigidbody>();

            if (rb != null)
            {
                direction = rb.linearVelocity;
                direction.y = 0f;
                moveStrength = direction.magnitude;
            }
        }

        if (moveStrength < 0.02f)
        {
            return;
        }

        direction /= moveStrength;

        Vector3 toGrass = grassPosition - transform.position;
        toGrass.y = 0f;

        float grassDistance = toGrass.magnitude;

        if (grassDistance < 0.0001f)
        {
            return;
        }

        float forwardDistance = Vector3.Dot(toGrass, direction);

        if (forwardDistance < minimumForwardDistance ||
            forwardDistance / grassDistance < forwardCone)
        {
            return;
        }

        // Audio
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

        // Particle
        grassPosition.y += particleOffset.y;
        PlayCutParticle(grassPosition);

        // UI animation
        if (GrassCollectUI.Instance != null)
        {
            GrassCollectUI.Instance.ShowGrassCollected(grassPosition);
        }
    }

    private void TryPlayCutAudio()
    {
        if (audioSource == null || cutAudioClips == null || cutAudioClips.Length == 0)
        {
            return;
        }

        cutsSinceAudio++;

        if (cutsSinceAudio < cutsPerAudioPlay || Time.time < nextAudioTime)
        {
            return;
        }

        cutsSinceAudio = 0;
        nextAudioTime = Time.time + minAudioInterval;

        AudioClip clip = cutAudioClips[Random.Range(0, cutAudioClips.Length)];
        audioSource.pitch = 1f + Random.Range(-audioPitchVariation, audioPitchVariation);
        audioSource.PlayOneShot(clip, audioVolume);
    }

    private void InitializeParticlePool()
    {
        if (cutParticlePrefab == null)
        {
            return;
        }

        int poolSize =
            Mathf.Max(2, particlePoolSize);

        particlePool =
            new PooledParticle[poolSize];

        GameObject poolObject =
            new GameObject("GrassCutParticlePool");

        particlePoolRoot =
            poolObject.transform;

        for (int i = 0; i < poolSize; i++)
        {
            GameObject particleObject =
                Instantiate(
                    cutParticlePrefab,
                    particlePoolRoot
                );

            particleObject.name =
                cutParticlePrefab.name +
                "_Pooled_" +
                i;

            ParticleSystem[] systems =
                particleObject
                    .GetComponentsInChildren
                        <ParticleSystem>(true);

            float duration =
                CalculateDuration(systems);

            for (int systemIndex = 0;
                 systemIndex < systems.Length;
                 systemIndex++)
            {
                ParticleSystem system =
                    systems[systemIndex];

                if (system == null)
                {
                    continue;
                }

                ParticleSystem.MainModule main =
                    system.main;

                main.loop = false;
                main.playOnAwake = false;
                main.stopAction =
                    ParticleSystemStopAction.None;

                system.Stop(
                    true,
                    ParticleSystemStopBehavior
                        .StopEmittingAndClear
                );
            }

            particleObject.SetActive(false);

            particlePool[i] =
                new PooledParticle
                {
                    Root = particleObject,
                    Systems = systems,
                    Duration = duration
                };
        }
    }

    private void PlayCutParticle(
        Vector3 worldPosition)
    {
        if (particlePool == null ||
            particlePool.Length == 0)
        {
            return;
        }

        PooledParticle particle = null;
        for (int i = 0; i < particlePool.Length; i++)
        {
            int index = (nextParticleIndex + i) % particlePool.Length;
            if (particlePool[index].Playing)
            {
                continue;
            }

            particle = particlePool[index];
            nextParticleIndex = (index + 1) % particlePool.Length;
            break;
        }

        // Keep active bursts at their original world positions.
        if (particle == null)
        {
            return;
        }

        StopParticle(particle);

        particle.Root.transform
            .SetPositionAndRotation(
                worldPosition,
                cutParticlePrefab
                    .transform.rotation
            );

        particle.Root.SetActive(true);

        for (int i = 0;
             i < particle.Systems.Length;
             i++)
        {
            ParticleSystem system =
                particle.Systems[i];

            if (system != null)
            {
                system.Play(true);
            }
        }

        particle.Playing = true;

        particle.ReleaseTime =
            Time.time +
            particle.Duration +
            cleanupPadding;
    }

    private void UpdateParticlePool()
    {
        if (particlePool == null)
        {
            return;
        }

        float currentTime = Time.time;

        for (int i = 0;
             i < particlePool.Length;
             i++)
        {
            PooledParticle particle =
                particlePool[i];

            if (particle.Playing &&
                currentTime >=
                particle.ReleaseTime)
            {
                StopParticle(particle);
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

        if (particlePool == null)
        {
            return;
        }

        for (int i = 0;
             i < particlePool.Length;
             i++)
        {
            StopParticle(particlePool[i]);
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
}
