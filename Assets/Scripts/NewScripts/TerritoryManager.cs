using System.Collections.Generic;
using UnityEngine;

public class TerritoryManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Renderer playArea;
    [SerializeField] private GrassCutGrid grassGrid;
    [SerializeField] private PlayerTerritoryRenderer territoryRenderer;

    [Header("Territory Grid")]
    [Min(0.25f)]
    [SerializeField] private float cellSize = 0.5f;

    [Header("Starting Territory")]
    [Min(1)]
    [SerializeField] private int startingWidth = 5;
    [Min(1)]
    [SerializeField] private int startingHeight = 5;

    [Header("Debug")]
    [SerializeField] private bool drawDebugGrid;

    private Vector2Int minCell;
    private Vector2Int maxCell;

    private readonly HashSet<Vector2Int> ownedCells = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> trailCells = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> trail = new List<Vector2Int>();

    private bool initialized;

    /// <summary>
    /// Fires whenever the PLAYER's owned territory changes — captured,
    /// lost to an enemy, or gained from a defeated enemy. Boundary/wall
    /// systems (or anything else that needs to react to territory shape
    /// changes) subscribe to this instead of polling every frame.
    /// </summary>
    public event System.Action OnPlayerTerritoryChanged;


    private readonly HashSet<Vector2Int> blocked = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> outside = new HashSet<Vector2Int>();
    private readonly Queue<Vector2Int> captureQueue = new Queue<Vector2Int>();
    private readonly List<Vector2Int> newlyCapturedCells = new List<Vector2Int>();

    private readonly Dictionary<EnemyAI, List<Vector2Int>> enemyTrails = new Dictionary<EnemyAI, List<Vector2Int>>();
    private readonly Dictionary<EnemyAI, HashSet<Vector2Int>> enemyTrailCells = new Dictionary<EnemyAI, HashSet<Vector2Int>>();

    private readonly Dictionary<EnemyAI, List<Vector3>> enemyTrailWorldPositions =
    new Dictionary<EnemyAI, List<Vector3>>();

    private readonly Dictionary<EnemyAI, Vector3> enemyHomePositions = new Dictionary<EnemyAI, Vector3>();

    private readonly Dictionary<EnemyAI, HashSet<Vector2Int>> enemyTerritories = new Dictionary<EnemyAI, HashSet<Vector2Int>>();

    private readonly Dictionary<EnemyAI, PlayerTerritoryRenderer> enemyRenderers = new Dictionary<EnemyAI, PlayerTerritoryRenderer>();

    public event System.Action<TerritoryManager> OnInitialized;

    [Header("Win Condition")]
    [SerializeField] private LevelWinCondition winCondition;

    public event System.Action OnWinningPercentageReached;

    private bool hasWon;

    public bool IsInitialized => initialized;

    private GrassCutter playerTerritoryCutter;



    //[SerializeField] private List<EnemyTerritoryRenderer> enemyTerritoryRenderers = new List<EnemyTerritoryRenderer>();

    [SerializeField] private EnemySpawner enemySpawner;

    private readonly HashSet<Vector2Int> wallProtectedCells =
    new HashSet<Vector2Int>();

    public EnemySpawner EnemySpawner
    {
        get
        {
            if (enemySpawner == null)
            {
                enemySpawner = FindFirstObjectByType<EnemySpawner>();
            }
            return enemySpawner;
        }
    }

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.right,
        Vector2Int.left,
        Vector2Int.up,
        Vector2Int.down
    };

    private void Start()
    {
        Initialize();
    }

    public void Initialize()
    {
        if (playArea == null || territoryRenderer == null)
        {
            Debug.LogError(
                "TerritoryManager: Play Area or Territory Renderer is missing.",
                this
            );
            return;
        }

        if (winCondition == null)
        {
            winCondition = GetComponent<LevelWinCondition>();
        }

        hasWon = false;

        Bounds bounds = playArea.bounds;
        minCell = WorldToCell(bounds.min);

        Vector3 insideMaximum = bounds.max;
        insideMaximum.x -= 0.0001f;
        insideMaximum.z -= 0.0001f;
        maxCell = WorldToCell(insideMaximum);

        ownedCells.Clear();
        trailCells.Clear();
        trail.Clear();
        enemyTrails.Clear();
        enemyTrailCells.Clear();
        enemyTrailWorldPositions.Clear();
        enemyHomePositions.Clear();
        enemyTerritories.Clear();
        enemyRenderers.Clear();

        initialized = true;

        CreateStartingTerritory();

        if (grassGrid != null)
        {
            grassGrid.CutCells(ownedCells);
        }

        territoryRenderer.Initialize(this, cellSize);
        territoryRenderer.Rebuild(ownedCells);

        OnInitialized?.Invoke(this);
    }

    public float CellSize => cellSize;

    public void RegisterEnemyRenderer(EnemyAI enemy, PlayerTerritoryRenderer renderer)
    {
        if (!initialized || enemy == null || renderer == null) return;

        renderer.Initialize(this, cellSize);
        enemyRenderers[enemy] = renderer;
    }

    private void RefreshEnemyRenderer(EnemyAI enemy)
    {
        if (enemyRenderers.TryGetValue(enemy, out PlayerTerritoryRenderer renderer) &&
            enemyTerritories.TryGetValue(enemy, out HashSet<Vector2Int> cells))
        {
            renderer.Rebuild(cells);
        }
    }

    public bool IsInsideEnemyTerritory(
    Vector3 worldPosition)
    {
        if (!initialized)
        {
            return false;
        }

        Vector2Int cell =
            WorldToCell(worldPosition);

        foreach (var entry in enemyTerritories)
        {
            HashSet<Vector2Int> territory =
                entry.Value;

            if (territory != null &&
                territory.Contains(cell))
            {
                return true;
            }
        }

        return false;
    }

    public bool CutEnemyTerritoryGrass(
    Vector3 worldPosition,
    float radius)
    {
        bool cutAny = false;

        foreach (var entry in enemyRenderers)
        {
            EnemyAI enemy =
                entry.Key;

            PlayerTerritoryRenderer renderer =
                entry.Value;

            if (enemy == null ||
                renderer == null)
            {
                continue;
            }

            if (renderer.CutGrassAt(
                    worldPosition,
                    radius))
            {
                cutAny = true;
            }
        }

        return cutAny;
    }

    public bool CutPlayerTerritoryGrass(
    Vector3 worldPosition,
    float radius)
    {
        if (territoryRenderer == null)
        {
            return false;
        }

        return territoryRenderer.CutGrassAt(
            worldPosition,
            radius
        );
    }



    private void CreateStartingTerritory()
    {
        Vector2Int center = WorldToCell(transform.position);
        int halfWidth = startingWidth / 2;
        int halfHeight = startingHeight / 2;

        for (int x = -halfWidth; x <= halfWidth; x++)
        {
            for (int z = -halfHeight; z <= halfHeight; z++)
            {
                Vector2Int cell = new Vector2Int(center.x + x, center.y + z);
                if (IsInsideBounds(cell))
                {
                    ownedCells.Add(cell);
                }
            }
        }
    }

    public bool IsInsideTerritory(Vector3 worldPosition)
    {
        Vector2Int cell = WorldToCell(worldPosition);
        return ownedCells.Contains(cell);
    }

    public void StartTrail(Vector3 worldPosition)
    {
        if (!initialized) return;

        trail.Clear();
        trailCells.Clear();
        AddTrailCell(WorldToCell(worldPosition));
    }

    public void AddTrailPosition(Vector3 worldPosition)
    {
        if (!initialized || trail.Count == 0) return;

        Vector2Int destination = WorldToCell(worldPosition);
        Vector2Int previous = trail[trail.Count - 1];
        AddConnectedTrailCells(previous, destination);
    }

    private void AddConnectedTrailCells(
    Vector2Int from,
    Vector2Int to)
    {
        int currentX = from.x;
        int currentZ = from.y;

        int differenceX =
            Mathf.Abs(to.x - from.x);

        int differenceZ =
            Mathf.Abs(to.y - from.y);

        int directionX =
            from.x < to.x ? 1 : -1;

        int directionZ =
            from.y < to.y ? 1 : -1;

        int error =
            differenceX - differenceZ;

        while (currentX != to.x ||
               currentZ != to.y)
        {
            int doubledError =
                error * 2;

            if (doubledError > -differenceZ)
            {
                error -= differenceZ;
                currentX += directionX;
            }

            if (doubledError < differenceX)
            {
                error += differenceX;
                currentZ += directionZ;
            }

            AddTrailCell(
                new Vector2Int(
                    currentX,
                    currentZ
                )
            );
        }
    }

    private void AddTrailCell(Vector2Int cell)
    {
        if (!IsInsideBounds(cell) || ownedCells.Contains(cell) || !trailCells.Add(cell))
            return;

        trail.Add(cell);
    }

    public void CompleteTrail(IReadOnlyList<Vector3> _)
    {
        if (trail.Count == 0)
        {
            return;
        }

        CaptureEnclosedArea();

        trail.Clear();
        trailCells.Clear();

        if (territoryRenderer != null)
        {
            // Render only logically captured grid cells.
            // Do not add grass using the wider cutter positions.
            territoryRenderer.Rebuild(ownedCells);
        }

        CheckWinCondition();
    }

    private void CaptureEnclosedArea()
    {
        RefreshWallProtectedCells();

        HashSet<Vector2Int> previousTerritory =
            new HashSet<Vector2Int>(ownedCells);

        blocked.Clear();
        blocked.UnionWith(previousTerritory);

        foreach (Vector2Int cell in trail)
        {
            blocked.Add(cell);
        }

        outside.Clear();
        captureQueue.Clear();
        newlyCapturedCells.Clear();

        SeedOutsideCells();

        while (captureQueue.Count > 0)
        {
            Vector2Int current = captureQueue.Dequeue();

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int next = current + Directions[i];

                if (!IsInsideBounds(next) ||
                    blocked.Contains(next) ||
                    !outside.Add(next))
                {
                    continue;
                }

                captureQueue.Enqueue(next);
            }
        }

        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            for (int z = minCell.y; z <= maxCell.y; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);

                if (previousTerritory.Contains(cell) ||
                    trailCells.Contains(cell) ||
                    outside.Contains(cell) ||
                    wallProtectedCells.Contains(cell))
                {
                    continue;
                }

                if (ownedCells.Add(cell))
                {
                    newlyCapturedCells.Add(cell);
                }
            }
        }

        foreach (Vector2Int cell in trail)
        {
            if (!wallProtectedCells.Contains(cell) &&
                ownedCells.Add(cell))
            {
                newlyCapturedCells.Add(cell);
            }
        }

        if (newlyCapturedCells.Count > 0)
        {
            TakeCellsFromOthers(newlyCapturedCells, null);
        }

        if (grassGrid != null &&
            newlyCapturedCells.Count > 0)
        {
            grassGrid.CutCells(newlyCapturedCells);
        }

        if (newlyCapturedCells.Count > 0)
        {
            OnPlayerTerritoryChanged?.Invoke();
        }
    }

    public void CheckWinCondition()
    {
        if (hasWon || winCondition == null || TotalCells <= 0) return;

        if (enemySpawner == null)
        {
            enemySpawner = FindFirstObjectByType<EnemySpawner>();
        }

        // Condition 2: all opponents eliminated.
        if (enemySpawner != null && enemySpawner.RemainingEnemyCount > 0)
            return;

        // Condition 1: required percentage captured.
        float currentPercentage = ownedCells.Count * 100f / TotalCells;
        if (currentPercentage < winCondition.WinningPercentage)
            return;

        // Condition 3: player territory bigger than every remaining opponent.
        // (Guaranteed once Condition 2 holds, checked explicitly for safety.)
        if (enemySpawner != null)
        {
            IReadOnlyList<GameObject> remaining = enemySpawner.SpawnedEnemies;

            for (int i = 0; i < remaining.Count; i++)
            {
                if (remaining[i] == null) continue;

                EnemyAI enemyAI = remaining[i].GetComponent<EnemyAI>();
                if (enemyAI == null || !enemyAI.IsAlive) continue;

                if (ownedCells.Count <= GetEnemyTerritoryCount(enemyAI))
                    return;
            }
        }

        hasWon = true;
        Debug.Log("Level Won");
        OnWinningPercentageReached?.Invoke();
    }

    private void SeedOutsideCells()
    {
        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            AddOutsideCell(new Vector2Int(x, minCell.y));
            AddOutsideCell(new Vector2Int(x, maxCell.y));
        }

        for (int z = minCell.y; z <= maxCell.y; z++)
        {
            AddOutsideCell(new Vector2Int(minCell.x, z));
            AddOutsideCell(new Vector2Int(maxCell.x, z));
        }
    }

    private void AddOutsideCell(Vector2Int cell)
    {
        if (!IsInsideBounds(cell)) return;
        if (blocked.Contains(cell)) return;
        if (outside.Contains(cell)) return;

        outside.Add(cell);
        captureQueue.Enqueue(cell);
    }

    // ──── Enemy Trail System ────

    public void CreateEnemyStartingTerritory(
    EnemyAI enemy,
    Vector3 worldPosition,
    int width,
    int height)
    {
        if (!initialized || enemy == null) return;

        if (!enemyTerritories.TryGetValue(
                enemy,
                out HashSet<Vector2Int> territory))
        {
            territory = new HashSet<Vector2Int>();
            enemyTerritories.Add(enemy, territory);
        }

        Vector2Int center = WorldToCell(worldPosition);
        int halfWidth = width / 2;
        int halfHeight = height / 2;

        List<Vector2Int> cells = new List<Vector2Int>();

        for (int x = -halfWidth; x <= halfWidth; x++)
        {
            for (int z = -halfHeight; z <= halfHeight; z++)
            {
                Vector2Int cell =
                    new Vector2Int(center.x + x, center.y + z);

                if (IsInsideBounds(cell) &&
                    !IsCellClaimed(cell) &&
                    territory.Add(cell))
                {
                    cells.Add(cell);
                }
            }
        }

        if (grassGrid != null && cells.Count > 0)
        {
            grassGrid.CutCells(cells);
        }

        RefreshEnemyRenderer(enemy);
    }

    public void RegisterEnemyHome(EnemyAI enemy)
    {
        if (enemy == null) return;

        if (!enemyHomePositions.ContainsKey(enemy))
        {
            enemyHomePositions[enemy] = enemy.transform.position;
        }
    }

    public Vector3 GetEnemyHomePosition(EnemyAI enemy)
    {
        if (enemyHomePositions.TryGetValue(enemy, out Vector3 home))
            return home;

        return enemy.transform.position;
    }

    public bool EnemyTouchesTerritory(EnemyAI enemy, float radius)
    {
        if (!initialized || enemy == null) return false;

        if (!enemyTerritories.TryGetValue(enemy, out HashSet<Vector2Int> cells))
            return false;

        return TouchesCells(cells, enemy.transform.position, radius);
    }

    public bool TryGetNearestEnemyTerritoryPosition(
    EnemyAI enemy,
    Vector3 fromPosition,
    out Vector3 territoryPosition)
    {
        territoryPosition = fromPosition;

        if (!initialized || enemy == null)
        {
            return false;
        }

        if (!enemyTerritories.TryGetValue(
                enemy,
                out HashSet<Vector2Int> cells) ||
            cells == null ||
            cells.Count == 0)
        {
            return false;
        }

        float nearestDistanceSqr =
            float.PositiveInfinity;

        Vector2Int nearestCell =
            default;

        bool foundCell = false;

        foreach (Vector2Int cell in cells)
        {
            Vector3 cellPosition =
                CellToWorld(cell);

            float distanceSqr =
                new Vector2(
                    cellPosition.x - fromPosition.x,
                    cellPosition.z - fromPosition.z
                ).sqrMagnitude;

            if (distanceSqr >= nearestDistanceSqr)
            {
                continue;
            }

            nearestDistanceSqr = distanceSqr;
            nearestCell = cell;
            foundCell = true;
        }

        if (!foundCell)
        {
            return false;
        }

        territoryPosition =
            CellToWorld(nearestCell);

        territoryPosition.y =
            fromPosition.y;

        return true;
    }

    public void StartEnemyTrail(EnemyAI enemy, Vector3 worldPosition)
    {
        if (!initialized || enemy == null)
        {
            return;
        }

        if (!enemyTrails.TryGetValue(enemy, out List<Vector2Int> trail))
        {
            trail = new List<Vector2Int>(64);
            enemyTrails[enemy] = trail;
        }
        else
        {
            trail.Clear();
        }

        if (!enemyTrailCells.TryGetValue(
                enemy,
                out HashSet<Vector2Int> cells))
        {
            cells = new HashSet<Vector2Int>();
            enemyTrailCells[enemy] = cells;
        }
        else
        {
            cells.Clear();
        }

        if (!enemyTrailWorldPositions.TryGetValue(
                enemy,
                out List<Vector3> worldPositions))
        {
            worldPositions = new List<Vector3>(64);
            enemyTrailWorldPositions[enemy] = worldPositions;
        }
        else
        {
            worldPositions.Clear();
        }

        AddEnemyTrailCell(enemy, WorldToCell(worldPosition));

        if (trail.Count > 0)
        {
            worldPositions.Add(worldPosition);
        }
    }

    private void AddEnemyTrailCell(EnemyAI enemy, Vector2Int cell)
    {
        if (!IsInsideBounds(cell)) return;

        // Never put trail on the enemy's own land.
        if (enemyTerritories.TryGetValue(enemy, out HashSet<Vector2Int> territory) &&
            territory.Contains(cell))
            return;

        if (!enemyTrailCells.TryGetValue(enemy, out HashSet<Vector2Int> cells) || !cells.Add(cell))
            return;

        enemyTrails[enemy].Add(cell);
    }

    public GameObject GetTerritoryCutParticlePrefab(
    Vector3 worldPosition)
    {
        if (!initialized)
        {
            return null;
        }

        Vector2Int cell = WorldToCell(worldPosition);

        if (ownedCells.Contains(cell))
        {
            if (playerTerritoryCutter == null)
            {
                PaperPlayerTerritory player =
                    FindFirstObjectByType<PaperPlayerTerritory>();

                if (player != null)
                {
                    playerTerritoryCutter =
                        player.GetComponentInParent<GrassCutter>();
                }
            }

            return playerTerritoryCutter != null
                ? playerTerritoryCutter
                    .CapturedTerritoryParticlePrefab
                : null;
        }

        foreach (var entry in enemyTerritories)
        {
            if (entry.Key == null ||
                entry.Value == null ||
                !entry.Value.Contains(cell))
            {
                continue;
            }

            GrassCutter ownerCutter =
                entry.Key.GetComponent<GrassCutter>();

            return ownerCutter != null
                ? ownerCutter.CapturedTerritoryParticlePrefab
                : null;
        }

        // No owner means wild grass.
        return null;
    }

    public void AddEnemyTrailPosition(EnemyAI enemy, Vector3 worldPosition)
    {
        if (!initialized || enemy == null)
        {
            return;
        }

        if (!enemyTrails.TryGetValue(
                enemy,
                out List<Vector2Int> trail) ||
            trail.Count == 0)
        {
            return;
        }

        if (enemyTrailWorldPositions.TryGetValue(
                enemy,
                out List<Vector3> worldPositions))
        {
            Vector3 previousPosition =
                worldPositions[worldPositions.Count - 1];

            Vector2 movement =
                new Vector2(
                    worldPosition.x - previousPosition.x,
                    worldPosition.z - previousPosition.z
                );

            if (movement.sqrMagnitude > 0.000001f)
            {
                worldPositions.Add(worldPosition);
            }
        }

        Vector2Int from = trail[trail.Count - 1];
        Vector2Int to = WorldToCell(worldPosition);

        int differenceX = to.x - from.x;
        int differenceZ = to.y - from.y;
        int steps = Mathf.Max(
            Mathf.Abs(differenceX),
            Mathf.Abs(differenceZ)
        );

        if (steps == 0)
        {
            return;
        }

        Vector2Int previous = from;

        for (int step = 1; step <= steps; step++)
        {
            Vector2Int next = new Vector2Int(
                from.x + Mathf.RoundToInt(
                    differenceX * (step / (float)steps)
                ),
                from.y + Mathf.RoundToInt(
                    differenceZ * (step / (float)steps)
                )
            );

            if (next.x != previous.x &&
                next.y != previous.y)
            {
                AddEnemyTrailCell(
                    enemy,
                    new Vector2Int(next.x, previous.y)
                );
            }

            AddEnemyTrailCell(enemy, next);
            previous = next;
        }
    }
    public void CompleteEnemyTrail(EnemyAI enemy)
    {
        if (!initialized || enemy == null)
        {
            return;
        }

        if (!enemyTrails.TryGetValue(
                enemy,
                out List<Vector2Int> trail) ||
            trail.Count == 0)
        {
            if (enemyTrailWorldPositions.TryGetValue(
                    enemy,
                    out List<Vector3> emptyWorldPositions))
            {
                emptyWorldPositions.Clear();
            }

            return;
        }

        HashSet<Vector2Int> trailSet =
            enemyTrailCells[enemy];

        int captured =
            CaptureEnemyEnclosedArea(
                enemy,
                trail,
                trailSet
            );

        trail.Clear();
        trailSet.Clear();

        if (enemyTrailWorldPositions.TryGetValue(
                enemy,
                out List<Vector3> worldPositions))
        {
            worldPositions.Clear();
        }

        if (captured > 0)
        {
            Debug.Log(
                $"Area Captured by Enemy - {enemy.name} ({captured} cells)"
            );
        }
    }

    private int CaptureEnemyEnclosedArea(
    EnemyAI enemy,
    List<Vector2Int> enemyTrail,
    HashSet<Vector2Int> enemyTrailCellsSet)
    {
        if (!enemyTerritories.TryGetValue(
                enemy,
                out HashSet<Vector2Int> territory))
        {
            return 0;
        }

        RefreshWallProtectedCells();

        blocked.Clear();
        blocked.UnionWith(territory);
        blocked.UnionWith(enemyTrailCellsSet);

        outside.Clear();
        captureQueue.Clear();
        newlyCapturedCells.Clear();

        SeedOutsideCells();

        while (captureQueue.Count > 0)
        {
            Vector2Int current = captureQueue.Dequeue();

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int next = current + Directions[i];

                if (!IsInsideBounds(next) ||
                    blocked.Contains(next) ||
                    !outside.Add(next))
                {
                    continue;
                }

                captureQueue.Enqueue(next);
            }
        }

        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            for (int z = minCell.y; z <= maxCell.y; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);

                if (blocked.Contains(cell) ||
                    outside.Contains(cell) ||
                    wallProtectedCells.Contains(cell) ||
                    IsCellOwnedByAnotherEnemy(enemy, cell))
                {
                    continue;
                }

                newlyCapturedCells.Add(cell);
            }
        }

        for (int i = 0; i < enemyTrail.Count; i++)
        {
            Vector2Int cell = enemyTrail[i];

            if (!wallProtectedCells.Contains(cell) &&
                !IsCellOwnedByAnotherEnemy(enemy, cell))
            {
                newlyCapturedCells.Add(cell);
            }
        }

        int capturedCount = newlyCapturedCells.Count;

        if (capturedCount > 0)
        {
            TakeCellsFromOthers(newlyCapturedCells, enemy);

            for (int i = 0; i < newlyCapturedCells.Count; i++)
            {
                territory.Add(newlyCapturedCells[i]);
            }

            if (grassGrid != null)
            {
                grassGrid.CutCells(newlyCapturedCells);
            }

            RefreshEnemyRenderer(enemy);
        }

        newlyCapturedCells.Clear();
        return capturedCount;
    }

    private void AddOutsideCellToSet(
        Vector2Int cell,
        HashSet<Vector2Int> localBlocked,
        HashSet<Vector2Int> localOutside,
        Queue<Vector2Int> queue)
    {
        if (!IsInsideBounds(cell)) return;
        if (localBlocked.Contains(cell)) return;
        if (localOutside.Contains(cell)) return;

        localOutside.Add(cell);
        queue.Enqueue(cell);
    }

    // ──── Trail Collision ────

    public bool IsInsidePlayerTrail(Vector3 worldPosition, float radius)
    {
        if (!initialized || trailCells.Count == 0) return false;

        Vector2Int centerCell = WorldToCell(worldPosition);
        int cellRange = Mathf.CeilToInt(radius / cellSize);

        for (int x = -cellRange; x <= cellRange; x++)
        {
            for (int z = -cellRange; z <= cellRange; z++)
            {
                Vector2Int cell = new Vector2Int(centerCell.x + x, centerCell.y + z);

                if (!trailCells.Contains(cell))
                    continue;

                float cellMinX = cell.x * cellSize;
                float cellMinZ = cell.y * cellSize;

                float closestX = Mathf.Clamp(worldPosition.x, cellMinX, cellMinX + cellSize);
                float closestZ = Mathf.Clamp(worldPosition.z, cellMinZ, cellMinZ + cellSize);

                float dx = worldPosition.x - closestX;
                float dz = worldPosition.z - closestZ;

                if (dx * dx + dz * dz <= radius * radius)
                    return true;
            }
        }

        return false;
    }

    public bool IsInsideEnemyTrail(Vector3 worldPosition, float radius)
    {
        if (!initialized) return false;

        foreach (var kvp in enemyTrailCells)
        {
            HashSet<Vector2Int> trailCellsSet = kvp.Value;
            if (trailCellsSet.Count == 0) continue;

            Vector2Int centerCell = WorldToCell(worldPosition);
            int cellRange = Mathf.CeilToInt(radius / cellSize);

            for (int x = -cellRange; x <= cellRange; x++)
            {
                for (int z = -cellRange; z <= cellRange; z++)
                {
                    Vector2Int cell = new Vector2Int(centerCell.x + x, centerCell.y + z);

                    if (!trailCellsSet.Contains(cell))
                        continue;

                    float cellMinX = cell.x * cellSize;
                    float cellMinZ = cell.y * cellSize;

                    float closestX = Mathf.Clamp(worldPosition.x, cellMinX, cellMinX + cellSize);
                    float closestZ = Mathf.Clamp(worldPosition.z, cellMinZ, cellMinZ + cellSize);

                    float dx = worldPosition.x - closestX;
                    float dz = worldPosition.z - closestZ;

                    if (dx * dx + dz * dz <= radius * radius)
                        return true;
                }
            }
        }

        return false;
    }

    public bool IsEnemyInsidePlayerTrail(EnemyAI enemy, float radius)
    {
        if (enemy == null) return false;
        return IsInsidePlayerTrail(enemy.transform.position, radius);
    }

    public bool IsPlayerInsideEnemyTrail(
    Vector3 playerPosition,
    float radius,
    out EnemyAI killerEnemy)
    {
        killerEnemy = null;

        if (!initialized)
        {
            return false;
        }

        Vector2 playerPoint =
            new Vector2(
                playerPosition.x,
                playerPosition.z
            );

        float hitRadius =
            Mathf.Max(0f, radius);

        float radiusSqr =
            hitRadius * hitRadius;

        foreach (var entry in enemyTrailWorldPositions)
        {
            EnemyAI enemy = entry.Key;
            List<Vector3> points = entry.Value;

            if (enemy == null ||
                !enemy.IsAlive ||
                points == null ||
                points.Count == 0 ||
                !enemyTrailCells.TryGetValue(
                    enemy,
                    out HashSet<Vector2Int> activeCells) ||
                activeCells.Count == 0)
            {
                continue;
            }

            for (int i = 0; i < points.Count; i++)
            {
                Vector2 start =
                    new Vector2(
                        points[i].x,
                        points[i].z
                    );

                Vector2 closestPoint = start;

                if (i + 1 < points.Count)
                {
                    Vector2 end =
                        new Vector2(
                            points[i + 1].x,
                            points[i + 1].z
                        );

                    Vector2 segment =
                        end - start;

                    float segmentLengthSqr =
                        segment.sqrMagnitude;

                    if (segmentLengthSqr > 0.000001f)
                    {
                        float fraction =
                            Mathf.Clamp01(
                                Vector2.Dot(
                                    playerPoint - start,
                                    segment
                                ) / segmentLengthSqr
                            );

                        closestPoint =
                            start + segment * fraction;
                    }
                }

                if ((playerPoint - closestPoint).sqrMagnitude <=
                    radiusSqr)
                {
                    killerEnemy = enemy;
                    return true;
                }
            }
        }

        return false;
    }

    public void CancelPlayerTrail()
    {
        trail.Clear();
        trailCells.Clear();

        if (territoryRenderer != null)
        {
            territoryRenderer.RebuildAll(
                ownedCells
            );
        }

        // Restore enemy territory grass that the player
        // temporarily cut before the trail was cancelled.
        foreach (var entry in enemyRenderers)
        {
            EnemyAI enemy =
                entry.Key;

            PlayerTerritoryRenderer renderer =
                entry.Value;

            if (enemy == null ||
                renderer == null)
            {
                continue;
            }

            if (enemyTerritories.TryGetValue(
                    enemy,
                    out HashSet<Vector2Int> cells))
            {
                renderer.RebuildAll(cells);
            }
        }
    }

    public void RemoveEnemy(EnemyAI enemy)
    {
        if (enemy == null)
        {
            return;
        }

        bool transferredTerritory = false;

        if (enemyTerritories.TryGetValue(
                enemy,
                out HashSet<Vector2Int> defeatedTerritory))
        {
            if (defeatedTerritory.Count > 0)
            {
                ownedCells.UnionWith(defeatedTerritory);
                transferredTerritory = true;

                if (grassGrid != null)
                {
                    grassGrid.CutCells(defeatedTerritory);
                }
            }

            enemyTerritories.Remove(enemy);
        }

        if (enemyRenderers.TryGetValue(
                enemy,
                out PlayerTerritoryRenderer defeatedRenderer))
        {
            if (defeatedRenderer != null)
            {
                defeatedRenderer.Clear();
            }

            enemyRenderers.Remove(enemy);
        }

        enemyTrails.Remove(enemy);
        enemyTrailCells.Remove(enemy);
        enemyTrailWorldPositions.Remove(enemy);
        enemyHomePositions.Remove(enemy);

        if (territoryRenderer != null && transferredTerritory)
        {
            territoryRenderer.RebuildAll(ownedCells);
        }

        if (transferredTerritory)
        {
            OnPlayerTerritoryChanged?.Invoke();
        }
    }

    // ──── Grid Utilities ────

    public Vector2Int WorldToCell(Vector3 position)
    {
        return new Vector2Int(
            Mathf.FloorToInt(position.x / cellSize),
            Mathf.FloorToInt(position.z / cellSize)
        );
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        return new Vector3(
            (cell.x + 0.5f) * cellSize,
            playArea.bounds.max.y,
            (cell.y + 0.5f) * cellSize
        );
    }

    private bool IsInsideBounds(Vector2Int cell)
    {
        return cell.x >= minCell.x &&
               cell.x <= maxCell.x &&
               cell.y >= minCell.y &&
               cell.y <= maxCell.y;
    }

    public IReadOnlyList<Vector2Int> Trail => trail;
    public IReadOnlyCollection<Vector2Int> OwnedCells => ownedCells;

    public int TotalCells =>
        (maxCell.x - minCell.x + 1) *
        (maxCell.y - minCell.y + 1);

    public float GroundY =>
        playArea != null ? playArea.bounds.max.y : 0f;

    public Renderer PlayArea => playArea;

    public bool TouchesTerritory(Vector3 worldPosition, float radius)
    {
        return TouchesCells(ownedCells, worldPosition, radius);
    }

    //public void RegisterEnemyTerritoryRenderer(EnemyTerritoryRenderer renderer)
    //{
    //    if (!enemyTerritoryRenderers.Contains(renderer))
    //        enemyTerritoryRenderers.Add(renderer);
    //}

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGrid || playArea == null) return;

        Gizmos.DrawWireCube(playArea.bounds.center, playArea.bounds.size);
    }

    private bool TouchesCells(HashSet<Vector2Int> cells, Vector3 worldPosition, float radius)
    {
        if (!initialized || cells == null || cells.Count == 0) return false;

        radius = Mathf.Max(0f, radius);
        float radiusSqr = radius * radius;

        int minimumX = Mathf.FloorToInt((worldPosition.x - radius) / cellSize);
        int maximumX = Mathf.FloorToInt((worldPosition.x + radius) / cellSize);
        int minimumZ = Mathf.FloorToInt((worldPosition.z - radius) / cellSize);
        int maximumZ = Mathf.FloorToInt((worldPosition.z + radius) / cellSize);

        for (int x = minimumX; x <= maximumX; x++)
        {
            for (int z = minimumZ; z <= maximumZ; z++)
            {
                if (!cells.Contains(new Vector2Int(x, z)))
                    continue;

                float cellMinimumX = x * cellSize;
                float cellMinimumZ = z * cellSize;

                float closestX = Mathf.Clamp(worldPosition.x, cellMinimumX, cellMinimumX + cellSize);
                float closestZ = Mathf.Clamp(worldPosition.z, cellMinimumZ, cellMinimumZ + cellSize);

                float dx = worldPosition.x - closestX;
                float dz = worldPosition.z - closestZ;

                if (dx * dx + dz * dz <= radiusSqr)
                    return true;
            }
        }

        return false;
    }

    private bool IsCellClaimed(Vector2Int cell)
    {
        if (ownedCells.Contains(cell)) return true;

        foreach (var kvp in enemyTerritories)
        {
            if (kvp.Value.Contains(cell)) return true;
        }

        return false;
    }

    private void TakeCellsFromOthers(
    List<Vector2Int> cells,
    EnemyAI capturer)
    {
        if (capturer != null)
        {
            bool playerLost = false;

            for (int i = 0; i < cells.Count; i++)
            {
                if (ownedCells.Remove(cells[i]))
                {
                    playerLost = true;
                }
            }

            if (playerLost)
            {
                Debug.Log("Territory taken from Player");

                if (territoryRenderer != null)
                {
                    territoryRenderer.RebuildAll(ownedCells);
                }

                OnPlayerTerritoryChanged?.Invoke();
            }

            // Enemies may capture player territory, but never transfer
            // another enemy's territory to themselves.
            return;
        }

        // Player capture can still take territory from any enemy.
        foreach (var entry in enemyTerritories)
        {
            bool lost = false;

            for (int i = 0; i < cells.Count; i++)
            {
                if (entry.Value.Remove(cells[i]))
                {
                    lost = true;
                }
            }

            if (!lost)
            {
                continue;
            }

            Debug.Log(
                $"Territory taken from " +
                $"{(entry.Key != null ? entry.Key.name : "dead enemy")}"
            );

            if (enemyRenderers.TryGetValue(
                    entry.Key,
                    out PlayerTerritoryRenderer renderer) &&
                renderer != null)
            {
                renderer.RebuildAll(entry.Value);
            }
        }
    }

    public int GetEnemyTerritoryCount(EnemyAI enemy)
    {
        if (enemy != null && enemyTerritories.TryGetValue(enemy, out HashSet<Vector2Int> cells))
            return cells.Count;

        return 0;
    }

    public void CancelEnemyTrail(EnemyAI enemy)
    {
        if (enemy == null)
        {
            return;
        }

        enemyTrails.Remove(enemy);
        enemyTrailCells.Remove(enemy);
        enemyTrailWorldPositions.Remove(enemy);

        if (territoryRenderer != null)
        {
            territoryRenderer.RebuildAll(ownedCells);
        }
    }
    private void RefreshWallProtectedCells()
    {
        wallProtectedCells.Clear();

        PlayerTerritoryBoundary boundary =
            GetComponentInParent<PlayerTerritoryBoundary>();

        if (boundary == null || !boundary.IsActive)
        {
            return;
        }

        outside.Clear();
        captureQueue.Clear();

        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            Vector2Int bottom = new Vector2Int(x, minCell.y);
            if (!boundary.BlocksCaptureStep(bottom, bottom + Vector2Int.down) &&
                outside.Add(bottom))
            {
                captureQueue.Enqueue(bottom);
            }

            Vector2Int top = new Vector2Int(x, maxCell.y);
            if (!boundary.BlocksCaptureStep(top, top + Vector2Int.up) &&
                outside.Add(top))
            {
                captureQueue.Enqueue(top);
            }
        }

        for (int z = minCell.y; z <= maxCell.y; z++)
        {
            Vector2Int left = new Vector2Int(minCell.x, z);
            if (!boundary.BlocksCaptureStep(left, left + Vector2Int.left) &&
                outside.Add(left))
            {
                captureQueue.Enqueue(left);
            }

            Vector2Int right = new Vector2Int(maxCell.x, z);
            if (!boundary.BlocksCaptureStep(right, right + Vector2Int.right) &&
                outside.Add(right))
            {
                captureQueue.Enqueue(right);
            }
        }

        while (captureQueue.Count > 0)
        {
            Vector2Int current = captureQueue.Dequeue();

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int next = current + Directions[i];

                if (!IsInsideBounds(next) ||
                    boundary.BlocksCaptureStep(current, next) ||
                    !outside.Add(next))
                {
                    continue;
                }

                captureQueue.Enqueue(next);
            }
        }

        for (int x = minCell.x; x <= maxCell.x; x++)
        {
            for (int z = minCell.y; z <= maxCell.y; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);

                if (!outside.Contains(cell))
                {
                    wallProtectedCells.Add(cell);
                }
            }
        }

        outside.Clear();
        captureQueue.Clear();
    }

    private bool IsCellOwnedByAnotherEnemy(
        EnemyAI capturer,
        Vector2Int cell)
    {
        foreach (var entry in enemyTerritories)
        {
            if (entry.Key != capturer &&
                entry.Value != null &&
                entry.Value.Contains(cell))
            {
                return true;
            }
        }

        return false;
    }
}
