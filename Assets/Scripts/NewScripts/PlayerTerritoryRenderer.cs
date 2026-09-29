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
    [SerializeField, Range(0.02f, 0.3f)] private float borderThicknessMultiplier = 0.24f;
    [Tooltip("Extra outward clearance in territory cells, beyond the grass mesh footprint. Visual only.")]
    [SerializeField, Range(0f, 1f)] private float borderOutwardOffsetMultiplier = 0.025f;
    [SerializeField, Range(0f, 3f)] private float borderCornerRadiusMultiplier = 1f;
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
    private readonly List<Matrix4x4> borderFringeMatrices = new List<Matrix4x4>();
    private readonly List<Matrix4x4[]> borderFringeBatches = new List<Matrix4x4[]>();

    private struct CellGrassOverhang
    {
        public float East, West, North, South;
    }

    private readonly Dictionary<Vector2Int, CellGrassOverhang> cellGrassOverhangs =
        new Dictionary<Vector2Int, CellGrassOverhang>();

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

        bool shouldAnimate =
            hasBuiltOnce
                ? animateCapturedGrass
                : animateStartingTerritory;

        float animationStartTime =
            Time.time;

        int firstNewMatrix =
            matrices.Count;

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
        }

        // New cells now have their actual grass placement/scale recorded.
        RebuildCapturedBorder(cells);

        if (matrices.Count ==
            firstNewMatrix)
        {
            hasBuiltOnce = true;
            return;
        }

        // Append only newly created blades to GPU batches.
        BuildBatches(firstNewMatrix);

        if (shouldAnimate &&
            growingBlades.Count > 0)
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

        Bounds bladeBounds = grassMesh.bounds;
        CellGrassOverhang overhang = new CellGrassOverhang();

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

                Vector3 boundsCenter = position + rotation * Vector3.Scale(bladeBounds.center, targetScale);
                Vector3 right = rotation * Vector3.right;
                Vector3 forward = rotation * Vector3.forward;
                float extentX = Mathf.Abs(right.x) * bladeBounds.extents.x * Mathf.Abs(targetScale.x) +
                    Mathf.Abs(forward.x) * bladeBounds.extents.z * Mathf.Abs(targetScale.z);
                float extentZ = Mathf.Abs(right.z) * bladeBounds.extents.x * Mathf.Abs(targetScale.x) +
                    Mathf.Abs(forward.z) * bladeBounds.extents.z * Mathf.Abs(targetScale.z);
                overhang.East = Mathf.Max(overhang.East,
                    boundsCenter.x + extentX - cellMinimumX - territoryCellSize);
                overhang.West = Mathf.Max(overhang.West, cellMinimumX - boundsCenter.x + extentX);
                overhang.North = Mathf.Max(overhang.North,
                    boundsCenter.z + extentZ - cellMinimumZ - territoryCellSize);
                overhang.South = Mathf.Max(overhang.South, cellMinimumZ - boundsCenter.z + extentZ);

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
        cellGrassOverhangs[cell] = overhang;
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

        // The blades are inset from cell edges. Only their actual overhang
        // needs clearance, not the entire mesh radius plus extra curve padding.
        HashSet<Vector2Int> owned = cells as HashSet<Vector2Int> ?? new HashSet<Vector2Int>(cells);
        float footprint = 0f;
        foreach (Vector2Int cell in owned)
        {
            if (!cellGrassOverhangs.TryGetValue(cell, out CellGrassOverhang overhang)) continue;
            bool east = !owned.Contains(cell + Vector2Int.right);
            bool west = !owned.Contains(cell + Vector2Int.left);
            bool north = !owned.Contains(cell + Vector2Int.up);
            bool south = !owned.Contains(cell + Vector2Int.down);
            if (east) footprint = Mathf.Max(footprint, overhang.East);
            if (west) footprint = Mathf.Max(footprint, overhang.West);
            if (north) footprint = Mathf.Max(footprint, overhang.North);
            if (south) footprint = Mathf.Max(footprint, overhang.South);
            // Include corner overhang without making every cell pay for the
            // largest possible random rotation and scale of the whole prefab.
            if (east && north) footprint = Mathf.Max(footprint, new Vector2(overhang.East, overhang.North).magnitude);
            if (east && south) footprint = Mathf.Max(footprint, new Vector2(overhang.East, overhang.South).magnitude);
            if (west && north) footprint = Mathf.Max(footprint, new Vector2(overhang.West, overhang.North).magnitude);
            if (west && south) footprint = Mathf.Max(footprint, new Vector2(overhang.West, overhang.South).magnitude);
        }
        float outwardClearance = footprint +
            Mathf.Max(0f, borderOutwardOffsetMultiplier) * territoryCellSize;

        List<TerritoryBorderMesh.Contour> contours = TerritoryBorderMesh.Rebuild(
            borderMesh, territoryManager, cells,
            borderObject.transform.worldToLocalMatrix,
            Mathf.Max(0.01f, borderHeightMultiplier * territoryCellSize),
            Mathf.Max(0.01f, borderThicknessMultiplier * territoryCellSize),
            borderCornerRadiusMultiplier, borderCurveSegments, outwardClearance
        );
        RebuildBorderGrass(owned, contours, outwardClearance);
        if (borderGrassGrid != null)
            borderGrassGrid.SetTerritoryBorderMask(this, isActiveAndEnabled ? contours : null);
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
        if (borderGrassGrid != null) borderGrassGrid.SetTerritoryBorderMask(this, null);
    }

    private void RebuildBorderGrass(
        HashSet<Vector2Int> owned, IReadOnlyList<TerritoryBorderMesh.Contour> contours,
        float clearance)
    {
        borderFringeMatrices.Clear();
        if (!isActiveAndEnabled || contours.Count == 0) return;

        HashSet<Vector2Int> candidates = new HashSet<Vector2Int>();
        int range = Mathf.Max(1, Mathf.CeilToInt(clearance / territoryCellSize) + 1);
        foreach (Vector2Int cell in owned)
        {
            if (owned.Contains(cell + Vector2Int.right) && owned.Contains(cell + Vector2Int.left) &&
                owned.Contains(cell + Vector2Int.up) && owned.Contains(cell + Vector2Int.down)) continue;
            for (int x = -range; x <= range; x++)
                for (int z = -range; z <= range; z++)
                {
                    Vector2Int candidate = cell + new Vector2Int(x, z);
                    if (!owned.Contains(candidate)) candidates.Add(candidate);
                }
        }

        int rows = Mathf.Max(1, Mathf.RoundToInt(territoryCellSize / Mathf.Max(0.02f, grassSpacing)));
        float step = territoryCellSize / rows;
        Bounds bounds = grassMesh.bounds;
        float meshRadius = new Vector2(
            Mathf.Max(Mathf.Abs(bounds.min.x), Mathf.Abs(bounds.max.x)),
            Mathf.Max(Mathf.Abs(bounds.min.z), Mathf.Abs(bounds.max.z))
        ).magnitude;
        Bounds playBounds = territoryManager.PlayArea.bounds;
        foreach (Vector2Int cell in candidates)
        {
            Vector3 centre = territoryManager.CellToWorld(cell);
            for (int x = 0; x < rows; x++)
                for (int z = 0; z < rows; z++)
                {
                    Vector3 position = centre + new Vector3(
                        (x + 0.5f) * step - territoryCellSize * 0.5f, 0f,
                        (z + 0.5f) * step - territoryCellSize * 0.5f
                    );
                    if (position.x < playBounds.min.x || position.x > playBounds.max.x ||
                        position.z < playBounds.min.z || position.z > playBounds.max.z) continue;
                    // Decoration must not repaint another owner's captured land.
                    if (territoryManager.IsInsideTerritory(position) ||
                        territoryManager.IsInsideEnemyTerritory(position)) continue;
                    if (!TerritoryBorderMesh.Contains(contours, position, false)) continue;
                    float distance = Mathf.Sqrt(TerritoryBorderMesh.DistanceSquared(contours, position, false));
                    float scale = Mathf.Lerp(scaleRange.x, scaleRange.y, RandomValue(cell, x, z, 2));
                    float horizontalScale = meshRadius > 0.00001f
                        ? Mathf.Min(scale, distance * 0.85f / meshRadius) : scale;
                    if (horizontalScale <= 0.001f) continue;
                    Quaternion rotation = randomYRotation
                        ? Quaternion.Euler(0f, RandomValue(cell, x, z, 3) * 360f, 0f) : Quaternion.identity;
                    borderFringeMatrices.Add(Matrix4x4.TRS(position, rotation,
                        new Vector3(horizontalScale, scale * heightMultiplier, horizontalScale)));
                }
        }

        int count = (borderFringeMatrices.Count + MaxInstancesPerBatch - 1) / MaxInstancesPerBatch;
        while (borderFringeBatches.Count < count)
            borderFringeBatches.Add(new Matrix4x4[MaxInstancesPerBatch]);
        for (int i = 0; i < borderFringeMatrices.Count; i++)
            borderFringeBatches[i / MaxInstancesPerBatch][i % MaxInstancesPerBatch] = borderFringeMatrices[i];
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

        batches[batchIndex]
            [indexInsideBatch] =
                matrix;
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

            int indexInsideBatch =
                index %
                MaxInstancesPerBatch;

            batches[batchIndex]
                [indexInsideBatch] =
                    matrices[index];

            int newCount =
                indexInsideBatch + 1;

            if (newCount >
                batchCounts[batchIndex])
            {
                batchCounts[batchIndex] =
                    newCount;
            }
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

    public void Clear()
    {
        ClearBorderGrass();
        if (borderObject != null) borderObject.SetActive(false);
        cellGrassOverhangs.Clear();
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
                points, Mathf.Max(0f, outwardClearance)
            );
            List<Vector3> rounded = RoundBoundaryLoop(
                expanded, radius, Mathf.Clamp(cornerCurveSegments, 2, 16), out _,
                Mathf.Max(0.001f, outwardClearance)
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
