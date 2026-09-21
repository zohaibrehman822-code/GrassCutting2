using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class EnemyTerritoryRenderer : MonoBehaviour
{
    [Header("Territory Grass")]
    [SerializeField] private GameObject enemyTerritoryPrefabGrass;

    [SerializeField] private bool useLowPolyTufts = true;

    [Header("Grass Placement")]
    [Min(0.02f)]
    [SerializeField] private float grassSpacing = 0.15f;

    [Range(0f, 0.1f)]
    [SerializeField] private float jitter = 0.04f;

    [SerializeField] private Vector2 scaleRange = new Vector2(0.8f, 1.2f);

    [Range(0.1f, 1f)]
    [SerializeField] private float heightMultiplier = 0.65f;

    [SerializeField] private bool randomYRotation = true;

    [SerializeField] private int randomSeed = 1234;

    [Header("Growth Animation")]
    [SerializeField] private bool animateCapturedGrass = true;

    [SerializeField] private bool animateStartingTerritory;

    [Min(0.1f)]
    [SerializeField] private float growthDuration = 0.5f;

    [Range(0f, 0.5f)]
    [SerializeField] private float growthStagger = 0.2f;

    [Range(0.016f, 0.1f)]
    [SerializeField] private float growthUpdateInterval = 0.033f;

    [SerializeField] private AnimationCurve growthCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Performance")]
    [SerializeField] private bool castShadows;

    [SerializeField] private bool receiveShadows;

    private const int MaxInstancesPerBatch = 1023;

    private struct GrowingBlade
    {
        public int MatrixIndex;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 TargetScale;
        public float StartTime;
    }

    private readonly List<Matrix4x4> matrices = new List<Matrix4x4>();
    private readonly List<Matrix4x4[]> batches = new List<Matrix4x4[]>();
    private readonly List<int> batchCounts = new List<int>();
    private readonly List<GrowingBlade> growingBlades = new List<GrowingBlade>();
    private readonly HashSet<Vector2Int> renderedCells = new HashSet<Vector2Int>();

    private Mesh grassMesh;
    private Material grassMaterial;

    private TerritoryManager territoryManager;
    private float territoryCellSize;

    private float nextGrowthUpdateTime;
    private float growthEndTime;
    private bool hasBuiltOnce;

    public void Initialize(TerritoryManager manager, float cellSize)
    {
        Clear();

        territoryManager = manager;
        territoryCellSize = cellSize;

        if (enemyTerritoryPrefabGrass == null)
        {
            Debug.LogError("EnemyTerritoryRenderer: Territory grass prefab is missing.", this);
            return;
        }

        MeshFilter meshFilter = enemyTerritoryPrefabGrass.GetComponentInChildren<MeshFilter>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError("EnemyTerritoryRenderer: Prefab requires a MeshFilter.", this);
            return;
        }

        MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();

        if (meshRenderer == null || meshRenderer.sharedMaterial == null)
        {
            Debug.LogError("EnemyTerritoryRenderer: Grass mesh needs a MeshRenderer and material.", this);
            return;
        }

        grassMesh = useLowPolyTufts ? StylizedGrassMesh.Shared : meshFilter.sharedMesh;
        grassMaterial = meshRenderer.sharedMaterial;
        grassMaterial.enableInstancing = true;
    }

    public void Rebuild(IReadOnlyCollection<Vector2Int> cells)
    {
        if (territoryManager == null || grassMesh == null || grassMaterial == null || cells == null)
            return;

        FinishGrowth();

        bool shouldAnimate = hasBuiltOnce ? animateCapturedGrass : animateStartingTerritory;
        float animationStartTime = Time.time;
        int firstNewMatrix = matrices.Count;

        foreach (Vector2Int cell in cells)
        {
            if (!renderedCells.Add(cell))
                continue;

            AddCellGrass(cell, shouldAnimate, animationStartTime);
        }

        if (matrices.Count == firstNewMatrix)
        {
            hasBuiltOnce = true;
            return;
        }

        BuildBatches(firstNewMatrix);

        if (shouldAnimate && growingBlades.Count > 0)
        {
            nextGrowthUpdateTime = 0f;
            growthEndTime = animationStartTime + growthDuration + growthStagger;
        }

        hasBuiltOnce = true;
    }

    private void AddCellGrass(Vector2Int cell, bool animate, float animationStartTime)
    {
        float safeSpacing = Mathf.Max(0.02f, grassSpacing);

        int bladesPerAxis = Mathf.Max(1, Mathf.RoundToInt(territoryCellSize / safeSpacing));
        float actualSpacing = territoryCellSize / bladesPerAxis;
        float maximumJitter = Mathf.Min(jitter, actualSpacing * 0.35f);

        Vector3 cellCenter = territoryManager.CellToWorld(cell);
        float cellMinimumX = cellCenter.x - territoryCellSize * 0.5f;
        float cellMinimumZ = cellCenter.z - territoryCellSize * 0.5f;

        for (int x = 0; x < bladesPerAxis; x++)
        {
            for (int z = 0; z < bladesPerAxis; z++)
            {
                float randomX = RandomValue(cell, x, z, 0);
                float randomZ = RandomValue(cell, x, z, 1);
                float randomScale = RandomValue(cell, x, z, 2);
                float randomRotation = RandomValue(cell, x, z, 3);
                float randomDelay = RandomValue(cell, x, z, 4);

                float offsetX = (randomX * 2f - 1f) * maximumJitter;
                float offsetZ = (randomZ * 2f - 1f) * maximumJitter;

                Vector3 position = new Vector3(
                    cellMinimumX + (x + 0.5f) * actualSpacing + offsetX,
                    cellCenter.y,
                    cellMinimumZ + (z + 0.5f) * actualSpacing + offsetZ
                );

                float scale = Mathf.Lerp(scaleRange.x, scaleRange.y, randomScale);
                Vector3 targetScale = new Vector3(scale, scale * heightMultiplier, scale);

                Quaternion rotation = randomYRotation
                    ? Quaternion.Euler(0f, randomRotation * 360f, 0f)
                    : Quaternion.identity;

                AddGrassInstance(position, rotation, targetScale, animate,
                    animationStartTime + randomDelay * growthStagger);
            }
        }
    }

    private void AddGrassInstance(Vector3 position, Quaternion rotation, Vector3 targetScale, bool animate, float startTime)
    {
        int matrixIndex = matrices.Count;

        if (animate)
        {
            matrices.Add(Matrix4x4.TRS(position, rotation, Vector3.zero));
            growingBlades.Add(new GrowingBlade
            {
                MatrixIndex = matrixIndex,
                Position = position,
                Rotation = rotation,
                TargetScale = targetScale,
                StartTime = startTime
            });
        }
        else
        {
            matrices.Add(Matrix4x4.TRS(position, rotation, targetScale));
        }
    }

    private void Update()
    {
        UpdateGrowthAnimation();
        DrawGrass();
    }

    private void UpdateGrowthAnimation()
    {
        if (growingBlades.Count == 0 || Time.time < nextGrowthUpdateTime)
            return;

        nextGrowthUpdateTime = Time.time + growthUpdateInterval;

        float currentTime = Time.time;
        float safeDuration = Mathf.Max(0.1f, growthDuration);

        for (int i = 0; i < growingBlades.Count; i++)
        {
            GrowingBlade blade = growingBlades[i];

            if (currentTime < blade.StartTime)
                continue;

            float progress = Mathf.Clamp01((currentTime - blade.StartTime) / safeDuration);
            float evaluatedProgress = growthCurve != null
                ? growthCurve.Evaluate(progress)
                : Mathf.SmoothStep(0f, 1f, progress);

            evaluatedProgress = Mathf.Clamp01(evaluatedProgress);
            Vector3 animatedScale = blade.TargetScale * evaluatedProgress;

            SetMatrix(blade.MatrixIndex, Matrix4x4.TRS(blade.Position, blade.Rotation, animatedScale));
        }

        if (currentTime >= growthEndTime)
            FinishGrowth();
    }

    private void FinishGrowth()
    {
        if (growingBlades.Count == 0) return;

        for (int i = 0; i < growingBlades.Count; i++)
        {
            GrowingBlade blade = growingBlades[i];
            SetMatrix(blade.MatrixIndex, Matrix4x4.TRS(blade.Position, blade.Rotation, blade.TargetScale));
        }

        growingBlades.Clear();
    }

    private void SetMatrix(int matrixIndex, Matrix4x4 matrix)
    {
        if (matrixIndex < 0 || matrixIndex >= matrices.Count)
            return;

        matrices[matrixIndex] = matrix;

        int batchIndex = matrixIndex / MaxInstancesPerBatch;
        int indexInsideBatch = matrixIndex % MaxInstancesPerBatch;

        if (batchIndex >= batches.Count || indexInsideBatch >= batchCounts[batchIndex])
            return;

        batches[batchIndex][indexInsideBatch] = matrix;
    }

    private void BuildBatches(int firstNewMatrix)
    {
        int requiredBatches = (matrices.Count + MaxInstancesPerBatch - 1) / MaxInstancesPerBatch;

        while (batches.Count < requiredBatches)
        {
            batches.Add(new Matrix4x4[MaxInstancesPerBatch]);
            batchCounts.Add(0);
        }

        for (int index = firstNewMatrix; index < matrices.Count; index++)
        {
            int batchIndex = index / MaxInstancesPerBatch;
            int indexInsideBatch = index % MaxInstancesPerBatch;

            batches[batchIndex][indexInsideBatch] = matrices[index];

            int newCount = indexInsideBatch + 1;
            if (newCount > batchCounts[batchIndex])
                batchCounts[batchIndex] = newCount;
        }
    }

    private void DrawGrass()
    {
        if (grassMesh == null || grassMaterial == null) return;

        ShadowCastingMode shadowMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;

        for (int i = 0; i < batches.Count; i++)
        {
            int count = batchCounts[i];
            if (count == 0) continue;

            Graphics.DrawMeshInstanced(grassMesh, 0, grassMaterial, batches[i], count,
                null, shadowMode, receiveShadows, gameObject.layer);
        }
    }

    private float RandomValue(Vector2Int cell, int x, int z, int channel)
    {
        unchecked
        {
            uint hash = (uint)randomSeed;
            hash ^= (uint)cell.x * 374761393u;
            hash ^= (uint)cell.y * 668265263u;
            hash ^= (uint)x * 2246822519u;
            hash ^= (uint)z * 3266489917u;
            hash ^= (uint)channel * 1442695041u;
            hash ^= hash >> 16;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            hash *= 3266489917u;
            hash ^= hash >> 16;
            return (hash & 0x00FFFFFFu) / 16777215f;
        }
    }

    public void Clear()
    {
        growingBlades.Clear();
        renderedCells.Clear();
        matrices.Clear();

        for (int i = 0; i < batchCounts.Count; i++)
            batchCounts[i] = 0;

        hasBuiltOnce = false;
        nextGrowthUpdateTime = 0f;
        growthEndTime = 0f;
    }
}