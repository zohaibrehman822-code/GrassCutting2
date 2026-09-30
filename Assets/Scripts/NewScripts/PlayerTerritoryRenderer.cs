using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class PlayerTerritoryRenderer : MonoBehaviour
{
    [Header("Territory Grass")]
    [SerializeField]
    private GameObject playerTerritoryPrefabGrass;

    [SerializeField] private bool useLowPolyTufts = true;

    [Header("Grass Placement")]
    [Min(0.02f)]
    [SerializeField]
    private float grassSpacing = 0.15f;

    [Range(0f, 0.1f)]
    [SerializeField]
    private float jitter = 0.04f;

    [SerializeField]
    private Vector2 scaleRange =
        new Vector2(0.8f, 1.2f);

    [Range(0.1f, 1f)]
    [SerializeField]
    private float heightMultiplier = 0.65f;

    [SerializeField]
    private bool randomYRotation = true;

    [SerializeField]
    private int randomSeed = 6789;



    [Header("Territory Flowers")]
    [Tooltip("Flower prefabs spawned on captured cells. Leave empty to disable flowers.")]
    [SerializeField]
    private GameObject[] flowerPrefabs;

    [Tooltip("Chance that a candidate slot inside a captured cell becomes a flower. 0 = none, 1 = every slot.")]
    [Range(0f, 1f)]
    [SerializeField]
    private float flowerDensity = 0.3f;

    [Tooltip("World spacing of the candidate flower slots inside each cell. Smaller = more candidates.")]
    [Min(0.1f)]
    [SerializeField]
    private float flowerSpacing = 0.5f;

    [SerializeField]
    private Vector2 flowerScaleRange = new Vector2(0.75f, 1.25f);

    [Tooltip("Vertical offset from the captured cell ground plane.")]
    [SerializeField]
    private float flowerYOffset;

    [Tooltip("Grow flowers in with the same curve, duration and stagger as the grass.")]
    [SerializeField]
    private bool animateFlowerGrowth = true;

    [Header("Growth Animation")]
    [SerializeField]
    private bool animateCapturedGrass = true;

    [SerializeField]
    private bool animateStartingTerritory;

    [Min(0.1f)]
    [SerializeField]
    private float growthDuration = 0.5f;

    [Range(0f, 0.5f)]
    [SerializeField]
    private float growthStagger = 0.2f;

    [Range(0.016f, 0.1f)]
    [SerializeField]
    private float growthUpdateInterval = 0.033f;

    [SerializeField]
    private AnimationCurve growthCurve =
        AnimationCurve.EaseInOut(
            0f, 0f,
            1f, 1f
        );

    [Header("Performance")]
    [SerializeField]
    private bool castShadows;

    [SerializeField]
    private bool receiveShadows;

    [Header("Captured Territory Border (Visual Only)")]
    [Tooltip("Always show a rounded border around this owner's captured cells. Independent of power-ups.")]
    [SerializeField] private bool showCapturedBorder = true;

    [SerializeField, Range(0.05f, 2f)] private float borderHeightMultiplier = 0.3f;
    [SerializeField, Range(0.02f, 0.3f)] private float borderThicknessMultiplier = 0.12f;
    [Tooltip("Small visual clearance from the owned-cell edge. Grass no longer pushes the entire border outward.")]
    [SerializeField, Range(0f, 1f)] private float borderOutwardOffsetMultiplier = 0.025f;
    [SerializeField, Range(0f, 0.35f)] private float borderCornerRadiusMultiplier = 0.35f;
    [SerializeField, Range(2, 16)] private int borderCurveSegments = 8;

    [Tooltip("Optional border material template. A runtime copy is used; the asset is not changed.")]
    [SerializeField] private Material borderMaterial;
    [SerializeField] private bool borderUsesGrassColor = true;
    [SerializeField] private Color borderColor = new Color(0.65f, 0.85f, 0.22f, 1f);

    private GameObject borderObject;
    private Mesh borderMesh;
    private MeshRenderer borderRenderer;
    private Material runtimeBorderMaterial;
    private GrassCutGrid borderGrassGrid;
    private readonly Dictionary<Vector2Int, List<int>> borderGrassCells = new Dictionary<Vector2Int, List<int>>();
    private readonly List<float> grassFootprintRadii = new List<float>();
    private readonly Dictionary<int, float> borderGrassWidthFactors = new Dictionary<int, float>();
    private float maximumCapturedGrassRadius;
    private readonly List<Matrix4x4> borderFringeMatrices = new List<Matrix4x4>();
    private readonly List<Matrix4x4[]> borderFringeBatches = new List<Matrix4x4[]>();

    private const int MaxInstancesPerBatch = 1023;

    private struct GrowingBlade
    {
        public int MatrixIndex;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 TargetScale;
        public float StartTime;
    }

    private readonly List<Matrix4x4> matrices =
        new List<Matrix4x4>();

    // Fixed-size arrays are reused when territory grows.
    private readonly List<Matrix4x4[]> batches =
        new List<Matrix4x4[]>();

    private readonly List<int> batchCounts =
        new List<int>();

    private readonly List<GrowingBlade> growingBlades =
        new List<GrowingBlade>();

    private struct GrowingFlower
    {
        public Transform Transform;
        public Vector3 TargetScale;
        public float StartTime;
    }

    private readonly List<GrowingFlower> growingFlowers =
        new List<GrowingFlower>();

    private Transform flowerRoot;

    private readonly HashSet<Vector2Int> renderedCells =
        new HashSet<Vector2Int>();

    private readonly HashSet<int> cutMatrixIndices =
    new HashSet<int>();

    private IReadOnlyList<Vector3> pendingCutPositions;

    private Mesh grassMesh;
    private Material grassMaterial;

    private TerritoryManager territoryManager;
    private float territoryCellSize;

    private float nextGrowthUpdateTime;
    private float growthEndTime;
    private bool hasBuiltOnce;

    public void Initialize(
        TerritoryManager manager,
        float cellSize)
    {
        Clear();

        territoryManager = manager;
        territoryCellSize = cellSize;
        borderGrassGrid = manager != null ? manager.GrassGrid : null;

        if (playerTerritoryPrefabGrass == null)
        {
            Debug.LogError(
                "PlayerTerritoryRenderer: Territory grass prefab is missing.",
                this
            );

            return;
        }

        MeshFilter meshFilter =
            playerTerritoryPrefabGrass
                .GetComponentInChildren<MeshFilter>();

        if (meshFilter == null ||
            meshFilter.sharedMesh == null)
        {
            Debug.LogError(
                "PlayerTerritoryRenderer: Prefab requires a MeshFilter.",
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
                "PlayerTerritoryRenderer: Grass mesh needs a MeshRenderer and material.",
                this
            );

            return;
        }

        grassMesh = useLowPolyTufts
            ? StylizedGrassMesh.Shared
            : meshFilter.sharedMesh;
        grassMaterial = meshRenderer.sharedMaterial;
        grassMaterial.enableInstancing = true;
    }

    /// <summary>
    /// PaperPlayerTerritory calls this immediately before capture.
    /// The list is consumed synchronously by Rebuild().
    /// </summary>
    public void SetCutPositionsForNextCapture(
        IReadOnlyList<Vector3> cutPositions)
    {
        pendingCutPositions = cutPositions;
    }

    public void ClearPendingCutPositions()
    {
        pendingCutPositions = null;
    }


    public void Rebuild(
        IReadOnlyCollection<Vector2Int> cells)
    {
        if (territoryManager == null ||
            grassMesh == null ||
            grassMaterial == null ||
            cells == null)
        {
            return;
        }

        // Finish any previous growth before starting
        // a new territory update.
        FinishGrowth();
        FinishFlowerGrowth();

        bool shouldAnimate =
            hasBuiltOnce
                ? animateCapturedGrass
                : animateStartingTerritory;

        float animationStartTime =
            Time.time;

        int firstNewMatrix =
            matrices.Count;

        int firstNewFlower =
            growingFlowers.Count;

        // IMPORTANT:
        // Grass is generated ONLY from logically owned cells.
        //
        // Do not use physical cut positions here.
        // Cut positions can extend beyond the logical grid
        // cell and were causing a small visual grass fringe.
        foreach (Vector2Int cell in cells)
        {
            if (!renderedCells.Add(cell))
            {
                continue;
            }

            AddCellGrass(
                cell,
                shouldAnimate,
                animationStartTime
            );

            AddCellFlowers(
                cell,
                shouldAnimate,
                animationStartTime
            );
        }

        // New cells now have their actual grass placement/scale recorded.
        RebuildCapturedBorder(cells);

        if (matrices.Count ==
            firstNewMatrix &&
            growingFlowers.Count == firstNewFlower)
        {
            hasBuiltOnce = true;
            return;
        }

        // Append only newly created blades to GPU batches.
        BuildBatches(firstNewMatrix);

        if (shouldAnimate &&
            (growingBlades.Count > 0 ||
             growingFlowers.Count > 0))
        {
            nextGrowthUpdateTime = 0f;

            growthEndTime =
                animationStartTime +
                growthDuration +
                growthStagger;
        }

        hasBuiltOnce = true;
    }

    private void AddCellGrass(
    Vector2Int cell,
    bool animate,
    float animationStartTime)
    {
        float safeSpacing =
            Mathf.Max(
                0.02f,
                grassSpacing
            );

        // Centre blades within cells so adjacent cells
        // do not create duplicate rows on their borders.
        int bladesPerAxis =
            Mathf.Max(
                1,
                Mathf.RoundToInt(
                    territoryCellSize /
                    safeSpacing
                )
            );

        float actualSpacing =
            territoryCellSize /
            bladesPerAxis;

        float maximumJitter =
            Mathf.Min(
                jitter,
                actualSpacing * 0.35f
            );

        Vector3 cellCenter =
            territoryManager.CellToWorld(
                cell
            );

        float cellMinimumX =
            cellCenter.x -
            territoryCellSize * 0.5f;

        float cellMinimumZ =
            cellCenter.z -
            territoryCellSize * 0.5f;

        for (int x = 0;
             x < bladesPerAxis;
             x++)
        {
            for (int z = 0;
                 z < bladesPerAxis;
                 z++)
            {
                float randomX =
                    RandomValue(
                        cell, x, z, 0
                    );

                float randomZ =
                    RandomValue(
                        cell, x, z, 1
                    );

                float randomScale =
                    RandomValue(
                        cell, x, z, 2
                    );

                float randomRotation =
                    RandomValue(
                        cell, x, z, 3
                    );

                float randomDelay =
                    RandomValue(
                        cell, x, z, 4
                    );

                float offsetX =
                    (randomX * 2f - 1f) *
                    maximumJitter;

                float offsetZ =
                    (randomZ * 2f - 1f) *
                    maximumJitter;

                Vector3 position =
                    new Vector3(
                        cellMinimumX +
                        (x + 0.5f) *
                        actualSpacing +
                        offsetX,

                        cellCenter.y,

                        cellMinimumZ +
                        (z + 0.5f) *
                        actualSpacing +
                        offsetZ
                    );

                float scale =
                    Mathf.Lerp(
                        scaleRange.x,
                        scaleRange.y,
                        randomScale
                    );

                Vector3 targetScale =
                    new Vector3(
                        scale,
                        scale *
                        heightMultiplier,
                        scale
                    );

                Quaternion rotation =
                    randomYRotation
                        ? Quaternion.Euler(
                            0f,
                            randomRotation *
                            360f,
                            0f
                        )
                        : Quaternion.identity;

                AddGrassInstance(
                    position,
                    rotation,
                    targetScale,
                    animate,
                    animationStartTime +
                    randomDelay *
                    growthStagger
                );
            }
        }
    }

    /// <summary>
    /// Supplements only the cleared edge and blade-radius fringe.
    /// Normal cell grass already fills the interior.
    /// </summary>
    ///
    private bool AddCutFringeGrass(
    IReadOnlyList<Vector3> cutPositions,
    bool animate,
    float animationStartTime)
    {
        bool addedAny = false;

        float edgeDistance =
            Mathf.Max(
                0.02f,
                grassSpacing * 0.75f
            );

        for (int i = 0;
             i < cutPositions.Count;
             i++)
        {
            Vector3 position =
                cutPositions[i];

            Vector2Int cell =
                territoryManager.WorldToCell(
                    position
                );

            // TerritoryManager now owns cut fringe cells.
            // Never draw green outside logical ownership.
            if (!renderedCells.Contains(cell))
            {
                continue;
            }

            float localX =
                position.x -
                cell.x * territoryCellSize;

            float localZ =
                position.z -
                cell.y * territoryCellSize;

            float distanceToXEdge =
                Mathf.Min(
                    localX,
                    territoryCellSize -
                    localX
                );

            float distanceToZEdge =
                Mathf.Min(
                    localZ,
                    territoryCellSize -
                    localZ
                );

            float distanceToEdge =
                Mathf.Min(
                    distanceToXEdge,
                    distanceToZEdge
                );

            // Existing cell instances cover the interior.
            if (distanceToEdge >
                edgeDistance)
            {
                continue;
            }

            position.y =
                territoryManager.GroundY;

            float variation =
                Mathf.PerlinNoise(
                    position.x * 13.17f +
                    10f,

                    position.z * 17.23f +
                    20f
                );

            float delay =
                Mathf.PerlinNoise(
                    position.x * 29.31f +
                    40f,

                    position.z * 11.79f +
                    50f
                );

            float scale =
                Mathf.Lerp(
                    scaleRange.x,
                    scaleRange.y,
                    variation
                );

            Vector3 targetScale =
                new Vector3(
                    scale,
                    scale *
                    heightMultiplier,
                    scale
                );

            Quaternion rotation =
                randomYRotation
                    ? Quaternion.Euler(
                        0f,
                        variation * 360f,
                        0f
                    )
                    : Quaternion.identity;

            AddGrassInstance(
                position,
                rotation,
                targetScale,
                animate,
                animationStartTime +
                delay *
                growthStagger
            );

            addedAny = true;
        }

        return addedAny;
    }

    private void AddGrassInstance(
        Vector3 position,
        Quaternion rotation,
        Vector3 targetScale,
        bool animate,
        float startTime)
    {
        int matrixIndex =
            matrices.Count;

        Vector2Int lookupCell = new Vector2Int(
            Mathf.FloorToInt(position.x / territoryCellSize),
            Mathf.FloorToInt(position.z / territoryCellSize));
        if (!borderGrassCells.TryGetValue(lookupCell, out List<int> indices))
        {
            indices = new List<int>();
            borderGrassCells.Add(lookupCell, indices);
        }
        indices.Add(matrixIndex);
        Bounds bounds = grassMesh.bounds;
        float radius = new Vector2(
            Mathf.Max(Mathf.Abs(bounds.min.x), Mathf.Abs(bounds.max.x)) * Mathf.Abs(targetScale.x),
            Mathf.Max(Mathf.Abs(bounds.min.z), Mathf.Abs(bounds.max.z)) * Mathf.Abs(targetScale.z)).magnitude;
        grassFootprintRadii.Add(radius);
        maximumCapturedGrassRadius = Mathf.Max(maximumCapturedGrassRadius, radius);

        if (animate)
        {
            matrices.Add(
                Matrix4x4.TRS(
                    position,
                    rotation,
                    Vector3.zero
                )
            );

            growingBlades.Add(
                new GrowingBlade
                {
                    MatrixIndex =
                        matrixIndex,

                    Position =
                        position,

                    Rotation =
                        rotation,

                    TargetScale =
                        targetScale,

                    StartTime =
                        startTime
                }
            );
        }
        else
        {
            matrices.Add(
                Matrix4x4.TRS(
                    position,
                    rotation,
                    targetScale
                )
            );
        }
    }

    /// <summary>
    /// Scatters flower prefabs over one newly captured cell.
    /// Placement uses the same hash as the grass, so the same capture always
    /// produces the same flowers.
    /// </summary>
    private void AddCellFlowers(
    Vector2Int cell,
    bool animate,
    float animationStartTime)
    {
        if (flowerPrefabs == null ||
            flowerPrefabs.Length == 0 ||
            flowerDensity <= 0f)
        {
            return;
        }

        if (!EnsureFlowerRoot()) return;

        float safeSpacing =
            Mathf.Max(0.1f, flowerSpacing);

        int slotsPerAxis =
            Mathf.Max(
                1,
                Mathf.FloorToInt(
                    territoryCellSize / safeSpacing
                )
            );

        float actualSpacing =
            territoryCellSize / slotsPerAxis;

        float maximumJitter =
            Mathf.Min(
                jitter,
                actualSpacing * 0.35f
            );

        Vector3 cellCenter =
            territoryManager.CellToWorld(cell);

        float cellMinimumX =
            cellCenter.x -
            territoryCellSize * 0.5f;

        float cellMinimumZ =
            cellCenter.z -
            territoryCellSize * 0.5f;

        for (int x = 0;
             x < slotsPerAxis;
             x++)
        {
            for (int z = 0;
                 z < slotsPerAxis;
                 z++)
            {
                // Density roll.
                if (RandomValue(cell, x, z, 15) > flowerDensity)
                {
                    continue;
                }

                float randomX = RandomValue(cell, x, z, 10);
                float randomZ = RandomValue(cell, x, z, 11);
                float randomScale = RandomValue(cell, x, z, 12);
                float randomRotation = RandomValue(cell, x, z, 13);
                float randomDelay = RandomValue(cell, x, z, 14);
                float randomPrefab = RandomValue(cell, x, z, 16);

                GameObject prefab =
                    flowerPrefabs[
                        Mathf.Clamp(
                            Mathf.FloorToInt(
                                randomPrefab * flowerPrefabs.Length),
                            0,
                            flowerPrefabs.Length - 1)];

                if (prefab == null) continue;

                Vector3 position =
                    new Vector3(
                        cellMinimumX +
                        (x + 0.5f) * actualSpacing +
                        (randomX * 2f - 1f) * maximumJitter,

                        cellCenter.y + flowerYOffset,

                        cellMinimumZ +
                        (z + 0.5f) * actualSpacing +
                        (randomZ * 2f - 1f) * maximumJitter
                    );

                // Keep the prefab's authored scale, then apply the variation.
                float scale =
                    Mathf.Lerp(
                        flowerScaleRange.x,
                        flowerScaleRange.y,
                        randomScale
                    );

                Vector3 prefabScale = prefab.transform.localScale;

                Vector3 targetScale =
                    new Vector3(
                        prefabScale.x * scale,
                        prefabScale.y * scale,
                        prefabScale.z * scale
                    );

                Quaternion rotation =
                    randomYRotation
                        ? Quaternion.Euler(
                            0f,
                            randomRotation * 360f,
                            0f
                        )
                        : Quaternion.identity;

                AddFlowerInstance(
                    prefab,
                    position,
                    rotation,
                    targetScale,
                    animate,
                    animationStartTime +
                    randomDelay * growthStagger
                );
            }
        }
    }

    private bool EnsureFlowerRoot()
    {
        if (flowerRoot != null) return true;
        if (flowerPrefabs == null || flowerPrefabs.Length == 0) return false;

        GameObject root = new GameObject(name + "_TerritoryFlowers");
        root.layer = gameObject.layer;
        root.transform.SetParent(
            territoryManager != null ? territoryManager.transform : transform, false);
        flowerRoot = root.transform;
        return true;
    }

    private void AddFlowerInstance(
    GameObject prefab,
    Vector3 position,
    Quaternion rotation,
    Vector3 targetScale,
    bool animate,
    float startTime)
    {
        if (!EnsureFlowerRoot()) return;

        GameObject instance =
            Instantiate(prefab, position, rotation, flowerRoot);
        instance.name = prefab.name;
        instance.SetActive(true);

        if (animate && animateFlowerGrowth)
        {
            instance.transform.localScale = Vector3.zero;

            growingFlowers.Add(
                new GrowingFlower
                {
                    Transform = instance.transform,
                    TargetScale = targetScale,
                    StartTime = startTime
                }
            );
        }
        else
        {
            instance.transform.localScale = targetScale;
        }
    }

    private void RebuildCapturedBorder(IReadOnlyCollection<Vector2Int> cells)
    {
        if (!showCapturedBorder || cells.Count == 0)
        {
            if (borderObject != null) borderObject.SetActive(false);
            ClearBorderGrass();
            return;
        }

        if (borderObject == null)
        {
            if (borderMesh != null) Destroy(borderMesh);
            if (runtimeBorderMaterial != null) Destroy(runtimeBorderMaterial);
            borderObject = new GameObject(name + "_CapturedTerritoryBorder");
            // Enemy renderers are children of moving enemies. Anchor the mesh
            // to the level instead so it stays with the captured land.
            borderObject.transform.SetParent(territoryManager.transform, false);
            borderObject.layer = gameObject.layer;
            MeshFilter filter = borderObject.AddComponent<MeshFilter>();
            borderRenderer = borderObject.AddComponent<MeshRenderer>();
            borderMesh = new Mesh { name = name + " Territory Border" };
            borderMesh.MarkDynamic();
            filter.sharedMesh = borderMesh;

            if (borderMaterial != null)
            {
                runtimeBorderMaterial = new Material(borderMaterial);
            }
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
                if (shader == null)
                {
                    Debug.LogWarning("PlayerTerritoryRenderer: No border shader available.", this);
                    borderObject.SetActive(false);
                    return;
                }
                runtimeBorderMaterial = new Material(shader);
                if (runtimeBorderMaterial.HasProperty("_Smoothness"))
                    runtimeBorderMaterial.SetFloat("_Smoothness", 0.15f);
            }
            borderRenderer.sharedMaterial = runtimeBorderMaterial;
        }

        if (runtimeBorderMaterial == null) return;

        if (borderObject.transform.parent != territoryManager.transform)
            borderObject.transform.SetParent(territoryManager.transform, false);

        Color color = borderColor;
        if (borderUsesGrassColor && grassMaterial != null)
        {
            // The project's grass shader uses these gradient properties;
            // legacy _BaseColor/_Color values may not describe the visible grass.
            if (grassMaterial.HasProperty("_Bottom_Color"))
            {
                color = grassMaterial.GetColor("_Bottom_Color");
                if (grassMaterial.HasProperty("_Top_Color"))
                    color = Color.Lerp(color, grassMaterial.GetColor("_Top_Color"), 0.25f);
            }
            else if (grassMaterial.HasProperty("_BaseColor"))
                color = grassMaterial.GetColor("_BaseColor");
            else if (grassMaterial.HasProperty("_Color"))
                color = grassMaterial.GetColor("_Color");
            else if (grassMaterial.HasProperty("_Top_Color"))
                color = grassMaterial.GetColor("_Top_Color");
        }
        color.a = 1f;
        if (runtimeBorderMaterial.HasProperty("_BaseColor"))
            runtimeBorderMaterial.SetColor("_BaseColor", color);
        if (runtimeBorderMaterial.HasProperty("_Color"))
            runtimeBorderMaterial.SetColor("_Color", color);

        // Follow logical ownership; large tufts must not push every edge outward.
        HashSet<Vector2Int> owned = cells as HashSet<Vector2Int> ?? new HashSet<Vector2Int>(cells);
        float outwardClearance = Mathf.Clamp(borderOutwardOffsetMultiplier, 0f, 0.05f) * territoryCellSize;

        List<TerritoryBorderMesh.Contour> contours = TerritoryBorderMesh.Rebuild(
            borderMesh, territoryManager, cells,
            borderObject.transform.worldToLocalMatrix,
            Mathf.Max(0.01f, borderHeightMultiplier * territoryCellSize),
            Mathf.Max(0.01f, borderThicknessMultiplier * territoryCellSize),
            Mathf.Min(borderCornerRadiusMultiplier, 0.35f), borderCurveSegments, outwardClearance
        );
        RebuildBorderGrass(owned, contours, outwardClearance);

        borderRenderer.shadowCastingMode = castShadows
            ? ShadowCastingMode.On : ShadowCastingMode.Off;
        borderRenderer.receiveShadows = false;
        if (runtimeBorderMaterial.HasProperty("_ReceiveShadows"))
            runtimeBorderMaterial.SetFloat("_ReceiveShadows", 0f);
        runtimeBorderMaterial.EnableKeyword("_RECEIVE_SHADOWS_OFF");
        borderObject.SetActive(isActiveAndEnabled && borderMesh.vertexCount > 0);
    }

    private void ClearBorderGrass()
    {
        borderFringeMatrices.Clear();
        RestoreBorderGrassWidths();
        if (borderGrassGrid != null) borderGrassGrid.SetTerritoryBorderMask(this, null);
    }

    private void RestoreBorderGrassWidths()
    {
        if (borderGrassWidthFactors.Count == 0) return;

        List<int> restore = new List<int>(borderGrassWidthFactors.Keys);
        borderGrassWidthFactors.Clear();
        foreach (int index in restore)
            if (index < matrices.Count) UploadBorderGrassMatrix(index, matrices[index]);
    }

    private void RebuildBorderGrass(
        HashSet<Vector2Int> owned, IReadOnlyList<TerritoryBorderMesh.Contour> contours,
        float clearance)
    {
        // Never decorate unowned cells: this made the visible capture too large.
        borderFringeMatrices.Clear();

        // Clipped synchronously: the deferred job was stopped by the next
        // capture, so blades on freshly captured edges kept full width and
        // spilled outside the border.
        if (borderGrassGrid != null)
        {
            borderGrassGrid.SetTerritoryBorderMask(
                this, contours != null && contours.Count > 0 ? contours : null);
        }

        if (!isActiveAndEnabled || contours == null || contours.Count == 0)
        {
            RestoreBorderGrassWidths();
            return;
        }

        TerritoryBorderMesh.Query inner = new TerritoryBorderMesh.Query(
            contours, false, territoryCellSize, maximumCapturedGrassRadius / 0.95f + 0.001f);
        ApplyCapturedBorderGrass(inner);
    }

    private void ApplyCapturedBorderGrass(TerritoryBorderMesh.Query query)
    {
        HashSet<int> updated = new HashSet<int>();

        foreach (Vector2Int cell in query.BoundaryCells)
        {
            List<int> indices;
            if (!borderGrassCells.TryGetValue(cell, out indices)) continue;
            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                if (index >= matrices.Count || index >= grassFootprintRadii.Count) continue;

                Vector3 position = matrices[index].GetColumn(3);
                float radius = grassFootprintRadii[index];
                float factor = 1f;
                if (radius > 0.00001f)
                {
                    factor = query.Contains(position)
                        ? Mathf.Clamp01(Mathf.Sqrt(query.DistanceSquared(position)) * 0.95f / radius)
                        : 0f;
                }

                if (factor < 0.999f) borderGrassWidthFactors[index] = factor;
                else borderGrassWidthFactors.Remove(index);
                updated.Add(index);
                UploadBorderGrassMatrix(index, matrices[index]);
            }
        }

        if (borderGrassWidthFactors.Count == 0) return;

        List<int> restore = new List<int>();
        foreach (KeyValuePair<int, float> entry in borderGrassWidthFactors)
            if (!updated.Contains(entry.Key)) restore.Add(entry.Key);

        foreach (int index in restore)
        {
            borderGrassWidthFactors.Remove(index);
            if (index < matrices.Count) UploadBorderGrassMatrix(index, matrices[index]);
        }
    }

    private void UploadBorderGrassMatrix(int index, Matrix4x4 matrix)
    {
        int batch = index / MaxInstancesPerBatch;
        int slot = index % MaxInstancesPerBatch;
        if (batch >= batches.Count || slot >= batchCounts[batch]) return;
        if (borderGrassWidthFactors.TryGetValue(index, out float factor))
        {
            // Preserve height and growth; narrow only blades touching the border.
            matrix.SetColumn(0, matrix.GetColumn(0) * factor);
            matrix.SetColumn(2, matrix.GetColumn(2) * factor);
            if (factor <= 0.001f) matrix.SetColumn(1, Vector4.zero);
        }
        batches[batch][slot] = matrix;
    }

    private void OnEnable()
    {
        if (borderObject != null)
            borderObject.SetActive(showCapturedBorder && renderedCells.Count > 0);
        if (hasBuiltOnce && territoryManager != null && grassMaterial != null)
            RebuildCapturedBorder(renderedCells);
    }

    private void OnDisable()
    {
        if (borderObject != null) borderObject.SetActive(false);
        ClearBorderGrass();
    }

    private void OnDestroy()
    {
        ClearBorderGrass();
        ClearFlowers();
        if (borderObject != null)
        {
            borderObject.SetActive(false);
            Destroy(borderObject);
        }
        if (borderMesh != null) Destroy(borderMesh);
        if (runtimeBorderMaterial != null) Destroy(runtimeBorderMaterial);
    }

    private void Update()
    {
        UpdateGrowthAnimation();
        UpdateFlowerGrowth();
        DrawGrass();
    }

    private void UpdateGrowthAnimation()
    {
        if (growingBlades.Count == 0)
        {
            return;
        }

        float currentTime = Time.time;

        if (currentTime >= growthEndTime)
        {
            FinishGrowth();
            return;
        }

        float safeDuration = Mathf.Max(0.1f, growthDuration);

        for (int i = 0; i < growingBlades.Count; i++)
        {
            GrowingBlade blade = growingBlades[i];

            if (currentTime < blade.StartTime)
            {
                continue;
            }

            float progress = Mathf.Clamp01(
                (currentTime - blade.StartTime) / safeDuration
            );

            float scaleProgress = growthCurve != null
                ? growthCurve.Evaluate(progress)
                : Mathf.SmoothStep(0f, 1f, progress);

            scaleProgress = Mathf.Clamp01(scaleProgress);

            SetMatrix(
                blade.MatrixIndex,
                Matrix4x4.TRS(
                    blade.Position,
                    blade.Rotation,
                    blade.TargetScale * scaleProgress
                )
            );
        }
    }

    private void UpdateFlowerGrowth()
    {
        if (growingFlowers.Count == 0)
        {
            return;
        }

        float currentTime = Time.time;

        if (currentTime >= growthEndTime)
        {
            FinishFlowerGrowth();
            return;
        }

        float safeDuration = Mathf.Max(0.1f, growthDuration);

        for (int i = growingFlowers.Count - 1; i >= 0; i--)
        {
            GrowingFlower flower = growingFlowers[i];

            if (flower.Transform == null)
            {
                growingFlowers.RemoveAt(i);
                continue;
            }

            if (currentTime < flower.StartTime)
            {
                continue;
            }

            float progress = Mathf.Clamp01(
                (currentTime - flower.StartTime) / safeDuration
            );

            float scaleProgress = growthCurve != null
                ? growthCurve.Evaluate(progress)
                : Mathf.SmoothStep(0f, 1f, progress);

            flower.Transform.localScale =
                flower.TargetScale * Mathf.Clamp01(scaleProgress);

            if (progress >= 1f)
            {
                growingFlowers.RemoveAt(i);
            }
        }
    }

    private void FinishFlowerGrowth()
    {
        if (growingFlowers.Count == 0)
        {
            return;
        }

        for (int i = 0; i < growingFlowers.Count; i++)
        {
            GrowingFlower flower = growingFlowers[i];
            if (flower.Transform != null)
            {
                flower.Transform.localScale = flower.TargetScale;
            }
        }

        growingFlowers.Clear();
    }

    private void FinishGrowth()
    {
        if (growingBlades.Count == 0)
        {
            return;
        }

        for (int i = 0;
             i < growingBlades.Count;
             i++)
        {
            GrowingBlade blade =
                growingBlades[i];

            SetMatrix(
                blade.MatrixIndex,
                Matrix4x4.TRS(
                    blade.Position,
                    blade.Rotation,
                    blade.TargetScale
                )
            );
        }

        growingBlades.Clear();
    }

    private void SetMatrix(
        int matrixIndex,
        Matrix4x4 matrix)
    {
        if (matrixIndex < 0 ||
            matrixIndex >=
            matrices.Count)
        {
            return;
        }

        matrices[matrixIndex] =
            matrix;

        int batchIndex =
            matrixIndex /
            MaxInstancesPerBatch;

        int indexInsideBatch =
            matrixIndex %
            MaxInstancesPerBatch;

        if (batchIndex >=
            batches.Count ||
            indexInsideBatch >=
            batchCounts[batchIndex])
        {
            return;
        }

        UploadBorderGrassMatrix(matrixIndex, matrix);
    }

    private void BuildBatches(
    int firstNewMatrix)
    {
        int requiredBatches =
            (matrices.Count +
             MaxInstancesPerBatch -
             1) /
            MaxInstancesPerBatch;

        while (batches.Count <
               requiredBatches)
        {
            batches.Add(
                new Matrix4x4[
                    MaxInstancesPerBatch
                ]
            );

            batchCounts.Add(0);
        }

        for (int index =
                 firstNewMatrix;
             index < matrices.Count;
             index++)
        {
            int batchIndex =
                index /
                MaxInstancesPerBatch;

            int indexInsideBatch = index % MaxInstancesPerBatch;
            int newCount =
                indexInsideBatch + 1;

            if (newCount >
                batchCounts[batchIndex])
            {
                batchCounts[batchIndex] =
                    newCount;
            }
            UploadBorderGrassMatrix(index, matrices[index]);
        }
    }

    private void DrawGrass()
    {
        if (grassMesh == null ||
            grassMaterial == null)
        {
            return;
        }

        ShadowCastingMode shadowMode =
            castShadows
                ? ShadowCastingMode.On
                : ShadowCastingMode.Off;

        for (int i = 0;
             i < batches.Count;
             i++)
        {
            int count =
                batchCounts[i];

            if (count == 0)
            {
                continue;
            }

            Graphics.DrawMeshInstanced(
                grassMesh,
                0,
                grassMaterial,
                batches[i],
                count,
                null,
                shadowMode,
                receiveShadows,
                gameObject.layer
            );
        }
        for (int i = 0; i * MaxInstancesPerBatch < borderFringeMatrices.Count; i++)
        {
            int count = Mathf.Min(MaxInstancesPerBatch, borderFringeMatrices.Count - i * MaxInstancesPerBatch);
            Graphics.DrawMeshInstanced(grassMesh, 0, grassMaterial, borderFringeBatches[i], count,
                null, shadowMode, receiveShadows, gameObject.layer);
        }
    }

    private float RandomValue(
        Vector2Int cell,
        int x,
        int z,
        int channel)
    {
        unchecked
        {
            uint hash =
                (uint)randomSeed;

            hash ^=
                (uint)cell.x *
                374761393u;

            hash ^=
                (uint)cell.y *
                668265263u;

            hash ^=
                (uint)x *
                2246822519u;

            hash ^=
                (uint)z *
                3266489917u;

            hash ^=
                (uint)channel *
                1442695041u;

            hash ^= hash >> 16;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            hash *= 3266489917u;
            hash ^= hash >> 16;

            return
                (hash & 0x00FFFFFFu) /
                16777215f;
        }
    }

    public bool CutGrassAt(
    Vector3 worldPosition,
    float radius)
    {
        if (grassMesh == null ||
            grassMaterial == null ||
            matrices.Count == 0)
        {
            return false;
        }

        radius =
            Mathf.Max(0.01f, radius);

        float radiusSqr =
            radius * radius;

        // Prevent the growth animation from restoring blades
        // immediately after they have been cut.
        FinishGrowth();

        bool cutAny = false;

        for (int i = 0;
             i < matrices.Count;
             i++)
        {
            if (cutMatrixIndices.Contains(i))
            {
                continue;
            }

            Vector4 translation =
                matrices[i].GetColumn(3);

            Vector3 bladePosition =
                new Vector3(
                    translation.x,
                    translation.y,
                    translation.z
                );

            float differenceX =
                bladePosition.x -
                worldPosition.x;

            float differenceZ =
                bladePosition.z -
                worldPosition.z;

            float distanceSqr =
                differenceX *
                differenceX +
                differenceZ *
                differenceZ;

            if (distanceSqr >
                radiusSqr)
            {
                continue;
            }

            cutMatrixIndices.Add(i);

            // Keep the instance and its index, but make its
            // scale zero so it is no longer visible.
            SetMatrix(
                i,
                Matrix4x4.TRS(
                    bladePosition,
                    Quaternion.identity,
                    Vector3.zero
                )
            );

            cutAny = true;
        }

        for (int i = 0; i < borderFringeMatrices.Count; i++)
        {
            Matrix4x4 matrix = borderFringeMatrices[i];
            if (matrix.GetColumn(0).sqrMagnitude <= 0.00000001f) continue;
            Vector4 translation = matrix.GetColumn(3);
            float dx = translation.x - worldPosition.x, dz = translation.z - worldPosition.z;
            if (dx * dx + dz * dz > radiusSqr) continue;
            matrix.SetColumn(0, Vector4.zero);
            matrix.SetColumn(1, Vector4.zero);
            matrix.SetColumn(2, Vector4.zero);
            borderFringeMatrices[i] = matrix;
            borderFringeBatches[i / MaxInstancesPerBatch][i % MaxInstancesPerBatch] = matrix;
            cutAny = true;
        }
        return cutAny;
    }

    /// <summary>
    /// Destroys every spawned flower. The old root is deactivated first so
    /// nothing from the previous capture is still visible for the rest of
    /// the frame while new flowers are spawned.
    /// </summary>
    private void ClearFlowers()
    {
        growingFlowers.Clear();

        if (flowerRoot == null) return;

        flowerRoot.gameObject.SetActive(false);
        Destroy(flowerRoot.gameObject);
        flowerRoot = null;
    }

    public void Clear()
    {
        ClearBorderGrass();
        ClearFlowers();
        if (borderObject != null) borderObject.SetActive(false);
        borderGrassCells.Clear();
        grassFootprintRadii.Clear();
        maximumCapturedGrassRadius = 0f;
        growingBlades.Clear();
        renderedCells.Clear();
        cutMatrixIndices.Clear();
        matrices.Clear();

        // Retain batch arrays so subsequent captures
        // do not repeatedly allocate them.
        for (int i = 0;
             i < batchCounts.Count;
             i++)
        {
            batchCounts[i] = 0;
        }

        pendingCutPositions = null;

        hasBuiltOnce = false;
        nextGrowthUpdateTime = 0f;
        growthEndTime = 0f;
    }

    /// <summary>
    /// Redraws all grass from scratch for the given cells, without animation.
    /// Used when this owner loses cells to someone else.
    /// </summary>
    public void RebuildAll(IReadOnlyCollection<Vector2Int> cells)
    {
        bool previousAnimateStart = animateStartingTerritory;
        animateStartingTerritory = false;

        Clear();
        Rebuild(cells);

        animateStartingTerritory = previousAnimateStart;
    }
}


/// <summary>
/// Shared visual-only geometry. It never changes ownership or creates colliders.
/// </summary>
internal static class TerritoryBorderMesh
{
    internal sealed class Contour
    {
        public List<Vector3> Inner;
        public List<Vector3> Outer;
    }

    // Exact contour tests with local edge buckets instead of scanning every
    // segment for every grass blade. All loops use even/odd parity, including holes.
    internal sealed class Query
    {
        private struct Segment
        {
            public Vector2 A, B;
        }

        private readonly List<Segment> segments = new List<Segment>();
        private readonly Dictionary<int, List<int>> rows = new Dictionary<int, List<int>>();
        private readonly Dictionary<Vector2Int, List<int>> nearby = new Dictionary<Vector2Int, List<int>>();
        private readonly float cellSize;
        private readonly float padding;
        internal IEnumerable<Vector2Int> BoundaryCells => nearby.Keys;

        internal Query(IReadOnlyList<Contour> contours, bool outer, float size, float radius)
        {
            cellSize = Mathf.Max(0.01f, size);
            padding = Mathf.Max(0.00001f, radius);
            foreach (Contour contour in contours)
            {
                List<Vector3> points = outer ? contour.Outer : contour.Inner;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 start = points[i], end = points[(i + 1) % points.Count];
                    Segment segment = new Segment {
                        A = new Vector2(start.x, start.z), B = new Vector2(end.x, end.z)
                    };
                    int index = segments.Count;
                    segments.Add(segment);
                    int firstRow = Mathf.FloorToInt(Mathf.Min(start.z, end.z) / cellSize);
                    int lastRow = Mathf.FloorToInt(Mathf.Max(start.z, end.z) / cellSize);
                    for (int row = firstRow; row <= lastRow; row++)
                    {
                        if (!rows.TryGetValue(row, out List<int> list))
                        {
                            list = new List<int>();
                            rows.Add(row, list);
                        }
                        list.Add(index);
                    }
                    int minX = Mathf.FloorToInt((Mathf.Min(start.x, end.x) - padding) / cellSize);
                    int maxX = Mathf.FloorToInt((Mathf.Max(start.x, end.x) + padding) / cellSize);
                    int minZ = Mathf.FloorToInt((Mathf.Min(start.z, end.z) - padding) / cellSize);
                    int maxZ = Mathf.FloorToInt((Mathf.Max(start.z, end.z) + padding) / cellSize);
                    for (int x = minX; x <= maxX; x++)
                        for (int z = minZ; z <= maxZ; z++)
                        {
                            Vector2Int key = new Vector2Int(x, z);
                            if (!nearby.TryGetValue(key, out List<int> list))
                            {
                                list = new List<int>();
                                nearby.Add(key, list);
                            }
                            list.Add(index);
                        }
                }
            }
        }

        internal bool Contains(Vector3 point)
        {
            bool inside = false;
            int row = Mathf.FloorToInt(point.z / cellSize);
            if (!rows.TryGetValue(row, out List<int> indices)) return false;
            foreach (int index in indices)
            {
                Segment segment = segments[index];
                Vector2 a = segment.A, b = segment.B;
                if ((a.y > point.z) != (b.y > point.z) &&
                    point.x < (b.x - a.x) * (point.z - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        // Distances beyond padding are irrelevant for footprint clipping/masks.
        internal float DistanceSquared(Vector3 point)
        {
            Vector2Int cell = new Vector2Int(
                Mathf.FloorToInt(point.x / cellSize), Mathf.FloorToInt(point.z / cellSize));
            float minimum = padding * padding;
            if (!nearby.TryGetValue(cell, out List<int> indices)) return minimum;
            Vector2 position = new Vector2(point.x, point.z);
            foreach (int index in indices)
            {
                Segment segment = segments[index];
                Vector2 delta = segment.B - segment.A;
                float t = delta.sqrMagnitude > 0.00000001f
                    ? Mathf.Clamp01(Vector2.Dot(position - segment.A, delta) / delta.sqrMagnitude) : 0f;
                minimum = Mathf.Min(minimum, (position - segment.A - delta * t).sqrMagnitude);
            }
            return minimum;
        }
    }

    internal static bool Contains(IReadOnlyList<Contour> contours, Vector3 point, bool outer)
    {
        bool inside = false;
        foreach (Contour contour in contours)
        {
            List<Vector3> points = outer ? contour.Outer : contour.Inner;
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            {
                Vector3 a = points[i], b = points[j];
                if ((a.z > point.z) != (b.z > point.z) &&
                    point.x < (b.x - a.x) * (point.z - a.z) / (b.z - a.z) + a.x)
                    inside = !inside;
            }
        }
        return inside;
    }

    internal static float DistanceSquared(IReadOnlyList<Contour> contours, Vector3 point, bool outer)
    {
        float minimum = float.PositiveInfinity;
        Vector2 position = new Vector2(point.x, point.z);
        foreach (Contour contour in contours)
        {
            List<Vector3> points = outer ? contour.Outer : contour.Inner;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 start = points[i], end = points[(i + 1) % points.Count];
                Vector2 a = new Vector2(start.x, start.z), b = new Vector2(end.x, end.z);
                Vector2 delta = b - a;
                float t = delta.sqrMagnitude > 0.00000001f
                    ? Mathf.Clamp01(Vector2.Dot(position - a, delta) / delta.sqrMagnitude) : 0f;
                minimum = Mathf.Min(minimum, (position - a - delta * t).sqrMagnitude);
            }
        }
        return minimum;
    }

    private enum EdgeDirection
    {
        East,
        West,
        North,
        South
    }

    private struct BoundaryEdge
    {
        public Vector2Int Cell;
        public EdgeDirection Direction;
    }

    internal static List<Contour> Rebuild(
        Mesh mesh, TerritoryManager manager, IReadOnlyCollection<Vector2Int> cells,
        Matrix4x4 worldToLocal, float height, float thickness,
        float cornerRadiusMultiplier, int cornerCurveSegments, float outwardClearance)
    {
        HashSet<Vector2Int> owned = cells as HashSet<Vector2Int>
            ?? new HashSet<Vector2Int>(cells);
        List<BoundaryEdge> edges = new List<BoundaryEdge>();
        foreach (Vector2Int cell in owned)
        {
            if (!owned.Contains(cell + Vector2Int.right))
                edges.Add(new BoundaryEdge { Cell = cell, Direction = EdgeDirection.East });
            if (!owned.Contains(cell + Vector2Int.left))
                edges.Add(new BoundaryEdge { Cell = cell, Direction = EdgeDirection.West });
            if (!owned.Contains(cell + Vector2Int.up))
                edges.Add(new BoundaryEdge { Cell = cell, Direction = EdgeDirection.North });
            if (!owned.Contains(cell + Vector2Int.down))
                edges.Add(new BoundaryEdge { Cell = cell, Direction = EdgeDirection.South });
        }
        List<List<Vector2Int>> loops = TraceBoundaryLoops(edges);
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Vector2> uv = new List<Vector2>();
        List<Vector2Int> seams = new List<Vector2Int>();
        List<Contour> contours = new List<Contour>();
        float cellSize = manager.CellSize;

        foreach (List<Vector2Int> loop in loops)
        {
            List<Vector3> points = new List<Vector3>(loop.Count);
            foreach (Vector2Int corner in loop)
            {
                Vector3 point = manager.CellToWorld(corner);
                point.x -= cellSize * 0.5f;
                point.z -= cellSize * 0.5f;
                points.Add(point);
            }

            float radius = Mathf.Max(0f, cornerRadiusMultiplier) * cellSize;
            // Keep the inner face from folding over itself on very tight arcs.
            if (radius > 0f) radius = Mathf.Max(radius, thickness * 1.25f);
            // Offset the original polygon BEFORE rounding. Offsetting an
            // already-rounded concave arc can fold it back over itself.
            List<Vector3> expanded = OffsetBoundaryLoop(
                ChamferStaircaseCorners(points, cellSize), Mathf.Max(0f, outwardClearance)
            );
            List<Vector3> rounded = RoundBoundaryLoop(
                expanded, radius, Mathf.Clamp(cornerCurveSegments, 2, 16), out _,
                radius
            );
            List<Vector3> outer = new List<Vector3>(rounded.Count);
            for (int i = 0; i < rounded.Count; i++)
            {
                Vector3 point = rounded[i];
                Vector3 incoming = (point - rounded[(i + rounded.Count - 1) % rounded.Count]).normalized;
                Vector3 outgoing = (rounded[(i + 1) % rounded.Count] - point).normalized;
                Vector3 leftIncoming = new Vector3(-incoming.z, 0f, incoming.x);
                Vector3 leftOutgoing = new Vector3(-outgoing.z, 0f, outgoing.x);
                Vector3 left = (leftIncoming + leftOutgoing).normalized;
                if (left.sqrMagnitude < 0.000001f) left = leftOutgoing;
                outer.Add(point - left * thickness / Mathf.Max(0.5f, Vector3.Dot(left, leftOutgoing)));
            }
            contours.Add(new Contour { Inner = rounded, Outer = outer });

            int firstVertex = vertices.Count;
            AppendBorderMesh(
                rounded, height, thickness,
                worldToLocal,
                vertices, triangles, uv
            );
            if (vertices.Count > firstVertex)
                seams.Add(new Vector2Int(firstVertex, vertices.Count - 8));
        }

        mesh.Clear();
        mesh.indexFormat = vertices.Count > 65535
            ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, uv);
        mesh.RecalculateNormals();
        Vector3[] normals = mesh.normals;
        foreach (Vector2Int seam in seams)
        {
            for (int faceVertex = 0; faceVertex < 8; faceVertex++)
            {
                Vector3 normal = (normals[seam.x + faceVertex] + normals[seam.y + faceVertex]).normalized;
                normals[seam.x + faceVertex] = normal;
                normals[seam.y + faceVertex] = normal;
            }
        }
        mesh.normals = normals;
        mesh.RecalculateBounds();
        return contours;
    }

    private static void GetDirectedCorners(
        BoundaryEdge edge, out Vector2Int start, out Vector2Int end)
    {
        // Owned territory is always on the left of each directed edge.
        Vector2Int cell = edge.Cell;
        switch (edge.Direction)
        {
            case EdgeDirection.East:
                start = cell + Vector2Int.right;
                end = cell + Vector2Int.one;
                break;
            case EdgeDirection.North:
                start = cell + Vector2Int.one;
                end = cell + Vector2Int.up;
                break;
            case EdgeDirection.West:
                start = cell + Vector2Int.up;
                end = cell;
                break;
            default:
                start = cell;
                end = cell + Vector2Int.right;
                break;
        }
    }

    private static List<List<Vector2Int>> TraceBoundaryLoops(List<BoundaryEdge> edges)
    {
        Dictionary<Vector2Int, List<int>> outgoing =
            new Dictionary<Vector2Int, List<int>>();
        Vector2Int[] starts = new Vector2Int[edges.Count];
        Vector2Int[] ends = new Vector2Int[edges.Count];
        bool[] visited = new bool[edges.Count];

        for (int i = 0; i < edges.Count; i++)
        {
            GetDirectedCorners(edges[i], out starts[i], out ends[i]);
            if (!outgoing.TryGetValue(starts[i], out List<int> candidates))
            {
                candidates = new List<int>(2);
                outgoing.Add(starts[i], candidates);
            }
            candidates.Add(i);
        }

        List<List<Vector2Int>> loops = new List<List<Vector2Int>>();
        for (int first = 0; first < edges.Count; first++)
        {
            if (visited[first]) continue;

            List<Vector2Int> loop = new List<Vector2Int>();
            int current = first;
            bool closed = false;
            for (int step = 0; step < edges.Count; step++)
            {
                visited[current] = true;
                loop.Add(starts[current]);
                Vector2Int endpoint = ends[current];
                if (endpoint == starts[first])
                {
                    closed = true;
                    break;
                }

                if (!outgoing.TryGetValue(endpoint, out List<int> candidates)) break;
                Vector2Int direction = endpoint - starts[current];
                int next = -1;
                int bestTurn = int.MinValue;
                foreach (int candidate in candidates)
                {
                    if (visited[candidate]) continue;
                    Vector2Int nextDirection = ends[candidate] - endpoint;
                    int cross = direction.x * nextDirection.y - direction.y * nextDirection.x;
                    int dot = direction.x * nextDirection.x + direction.y * nextDirection.y;
                    int rank = cross > 0 ? 3 : dot > 0 ? 2 : cross < 0 ? 1 : 0;
                    // A left turn keeps diagonally touching islands separate.
                    if (rank > bestTurn)
                    {
                        bestTurn = rank;
                        next = candidate;
                    }
                }
                if (next < 0) break;
                current = next;
            }

            if (closed && loop.Count >= 4) loops.Add(loop);
        }
        return loops;
    }

    /// <summary>
    /// Flattens a one-cell staircase. Each staircase corner becomes two points
    /// half a cell back along its own edges, so consecutive corners land on the
    /// ideal midline and the boundary reads as a smooth line instead of steps.
    /// Corners whose runs are longer than a cell, or whose turn direction does
    /// not alternate (real 90 degree corners of square blocks, full circles)
    /// are left untouched so compact shapes keep their exact footprint.
    /// </summary>
    private static List<Vector3> ChamferStaircaseCorners(List<Vector3> points, float cellSize)
    {
        int count = points.Count;
        if (cellSize <= 0f || count < 6) return points;

        bool[] isCorner = new bool[count];
        int[] turn = new int[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 incoming = points[i] - points[(i + count - 1) % count];
            Vector3 outgoing = points[(i + 1) % count] - points[i];
            float cross = incoming.x * outgoing.z - incoming.z * outgoing.x;
            if (Mathf.Abs(cross) > 1e-7f) { isCorner[i] = true; turn[i] = cross > 0f ? 1 : -1; }
        }

        List<int> corners = new List<int>(count);
        for (int i = 0; i < count; i++) if (isCorner[i]) corners.Add(i);
        if (corners.Count < 3) return points;

        float tolerance = cellSize * 0.001f;
        bool[] chamfer = new bool[count];
        for (int k = 0; k < corners.Count; k++)
        {
            int i = corners[k];
            int previous = corners[(k + corners.Count - 1) % corners.Count];
            int next = corners[(k + 1) % corners.Count];
            float previousRun = (points[i] - points[previous]).magnitude;
            float nextRun = (points[next] - points[i]).magnitude;
            if (Mathf.Abs(previousRun - cellSize) > tolerance) continue;
            if (Mathf.Abs(nextRun - cellSize) > tolerance) continue;
            if (turn[i] == turn[previous] || turn[i] == turn[next]) continue;
            chamfer[i] = true;
        }

        List<Vector3> result = new List<Vector3>(count + 8);
        for (int i = 0; i < count; i++)
        {
            if (!chamfer[i]) { result.Add(points[i]); continue; }
            Vector3 incoming = (points[i] - points[(i + count - 1) % count]).normalized;
            Vector3 outgoing = (points[(i + 1) % count] - points[i]).normalized;
            result.Add(points[i] - incoming * (cellSize * 0.5f));
            result.Add(points[i] + outgoing * (cellSize * 0.5f));
        }

        List<Vector3> dedup = new List<Vector3>(result.Count);
        for (int i = 0; i < result.Count; i++)
        {
            Vector3 current = result[i];
            if (dedup.Count > 0 && (current - dedup[dedup.Count - 1]).sqrMagnitude < 1e-8f) continue;
            dedup.Add(current);
        }
        if (dedup.Count > 1 && (dedup[0] - dedup[dedup.Count - 1]).sqrMagnitude < 1e-8f)
            dedup.RemoveAt(dedup.Count - 1);
        return dedup.Count >= 3 ? dedup : points;
    }

    private static List<Vector3> OffsetBoundaryLoop(List<Vector3> points, float distance)
    {
        List<Vector3> result = new List<Vector3>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 incoming = (points[i] - points[(i + points.Count - 1) % points.Count]).normalized;
            Vector3 outgoing = (points[(i + 1) % points.Count] - points[i]).normalized;
            Vector3 rightIncoming = new Vector3(incoming.z, 0f, -incoming.x);
            Vector3 rightOutgoing = new Vector3(outgoing.z, 0f, -outgoing.x);
            Vector3 right = (rightIncoming + rightOutgoing).normalized;
            if (right.sqrMagnitude < 0.000001f) right = rightOutgoing;
            float miter = 1f / Mathf.Max(0.5f, Vector3.Dot(right, rightOutgoing));
            result.Add(points[i] + right * distance * miter);
        }
        return result;
    }

    private static List<Vector3> RoundBoundaryLoop(
        List<Vector3> points, float radius, int subdivisions, out float maximumInset,
        float maximumConvexInset = float.PositiveInfinity)
    {
        maximumInset = 0f;
        List<Vector3> corners = new List<Vector3>();
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 incoming = points[i] - points[(i + points.Count - 1) % points.Count];
            Vector3 outgoing = points[(i + 1) % points.Count] - points[i];
            if (Vector3.Cross(incoming, outgoing).sqrMagnitude > 0.00000001f)
                corners.Add(points[i]);
        }

        List<Vector3> result = new List<Vector3>();
        subdivisions = Mathf.Max(2, subdivisions);
        for (int i = 0; i < corners.Count; i++)
        {
            Vector3 point = corners[i];
            Vector3 incoming = point - corners[(i + corners.Count - 1) % corners.Count];
            Vector3 outgoing = corners[(i + 1) % corners.Count] - point;
            float inset = Mathf.Min(radius, Mathf.Min(incoming.magnitude, outgoing.magnitude) * 0.45f);
            if (incoming.x * outgoing.z - incoming.z * outgoing.x > 0f)
                inset = Mathf.Min(inset, maximumConvexInset);
            maximumInset = Mathf.Max(maximumInset, inset);
            if (inset <= 0.00001f)
            {
                result.Add(point);
                continue;
            }

            Vector3 start = point - incoming.normalized * inset;
            Vector3 end = point + outgoing.normalized * inset;
            // Rational quadratic Bezier: an actual circular quarter-arc,
            // tangent to both adjoining straight edges (grid turns are 90 degrees).
            const float weight = 0.70710678f;
            for (int sample = 0; sample <= subdivisions; sample++)
            {
                float t = sample / (float)subdivisions;
                float inverse = 1f - t;
                float denominator = inverse * inverse + 2f * weight * inverse * t + t * t;
                result.Add((inverse * inverse * start + 2f * weight * inverse * t * point + t * t * end) / denominator);
            }
        }
        return result;
    }

    private static void AppendBorderMesh(
        List<Vector3> points, float height, float thickness, Matrix4x4 worldToLocal,
        List<Vector3> vertices, List<int> triangles, List<Vector2> uv)
    {
        if (points.Count < 3) return;
        int firstVertex = vertices.Count;
        float distance = 0f;
        for (int i = 0; i <= points.Count; i++)
        {
            int index = i % points.Count;
            Vector3 point = points[index];
            Vector3 previous = points[(index + points.Count - 1) % points.Count];
            Vector3 next = points[(index + 1) % points.Count];
            Vector3 incoming = (point - previous).normalized;
            Vector3 outgoing = (next - point).normalized;
            Vector3 leftIncoming = new Vector3(-incoming.z, 0f, incoming.x);
            Vector3 leftOutgoing = new Vector3(-outgoing.z, 0f, outgoing.x);
            Vector3 left = (leftIncoming + leftOutgoing).normalized;
            if (left.sqrMagnitude < 0.000001f) left = leftOutgoing;
            float width = thickness * 0.5f / Mathf.Max(0.5f, Vector3.Dot(left, leftOutgoing));
            // Owned cells are on the left: move both faces to the right,
            // outside the visible grass, without touching logical ownership.
            Vector3 bottomLeft = point;
            Vector3 bottomRight = point - left * width * 2f;
            Vector3 topLeft = bottomLeft + Vector3.up * height;
            Vector3 topRight = bottomRight + Vector3.up * height;
            if (i > 0) distance += Vector3.Distance(previous, point);

            // Separate vertices give a crisp flat top and smooth curved sides.
            vertices.Add(worldToLocal.MultiplyPoint3x4(topLeft));
            vertices.Add(worldToLocal.MultiplyPoint3x4(topRight));
            vertices.Add(worldToLocal.MultiplyPoint3x4(bottomLeft));
            vertices.Add(worldToLocal.MultiplyPoint3x4(topLeft));
            vertices.Add(worldToLocal.MultiplyPoint3x4(topRight));
            vertices.Add(worldToLocal.MultiplyPoint3x4(bottomRight));
            vertices.Add(worldToLocal.MultiplyPoint3x4(bottomRight));
            vertices.Add(worldToLocal.MultiplyPoint3x4(bottomLeft));
            for (int face = 0; face < 4; face++)
            {
                uv.Add(new Vector2(distance, 0f));
                uv.Add(new Vector2(distance, 1f));
            }
        }

        for (int i = 0; i < points.Count; i++)
        {
            int current = firstVertex + i * 8;
            int next = current + 8;
            for (int face = 0; face < 4; face++)
            {
                int a = current + face * 2;
                int b = next + face * 2;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(a + 1);
                triangles.Add(a + 1);
                triangles.Add(b);
                triangles.Add(b + 1);
            }
        }
    }
}
