using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

/// <summary>
/// Builds and maintains a per-edge boundary around the PLAYER's owned
/// territory (TerritoryManager.OwnedCells). This is an opt-in, level-specific
/// feature: it does nothing until explicitly activated (added in a later step).
///
/// STEP 1: Border-edge detection + debug gizmo.
/// STEP 2: Pooled wall mesh segments built from those edges.
/// STEP 3 (this update): NavMeshObstacle (carve mode) added to each segment
/// so NavMeshAgent-driven enemies cannot cross. Still no activation flow.
/// </summary>
public class PlayerTerritoryBoundary : MonoBehaviour
{
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

    private sealed class WallSegment
    {
        public Transform Transform;
        public MeshRenderer Renderer;
        public BoxCollider Collider;
        public NavMeshObstacle Obstacle;
    }

    [Header("References")]
    [Tooltip("Leave empty to find it automatically.")]
    [SerializeField] private TerritoryManager territoryManager;

    [Header("Wall Appearance")]
    [Tooltip("Wall height = this multiplier * TerritoryManager.CellSize.")]
    [Range(0.1f, 2f)]
    [SerializeField] private float wallHeightMultiplier = 0.5f;

    [Tooltip("Wall thickness = this multiplier * TerritoryManager.CellSize.")]
    [Range(0.02f, 0.3f)]
    [SerializeField] private float wallThicknessMultiplier = 0.1f;

    [Tooltip("Optional material. Leave empty to use a simple generated one.")]
    [SerializeField] private Material wallMaterial;

    [SerializeField] private Color wallColor = new Color(0.2f, 0.9f, 1f, 1f);

    [SerializeField] private bool castShadows = false;

    [Header("Blocking (NavMeshObstacle)")]
    [Tooltip("If true, obstacles carve holes in the NavMesh so enemies path around walls. Only carves while the segment is stationary, which matches how rebuilds work here (infrequent, not per-frame).")]
    [SerializeField] private bool carveOnlyStationary = true;

    [Header("Debug (Step 1)")]
    [SerializeField] private bool debugDrawGizmos = true;
    [SerializeField] private Color gizmoColor = Color.cyan;
    [Min(0f)]
    [SerializeField] private float gizmoHeightOffset = 0.05f;

    // Reused every rebuild to avoid per-call list allocations.
    private readonly List<BoundaryEdge> currentEdges = new List<BoundaryEdge>(64);

    // Pooled wall segments. Grown on demand, never destroyed during rebuilds.
    private readonly List<WallSegment> wallPool = new List<WallSegment>(64);
    private Transform wallPoolRoot;
    private Material runtimeMaterial;

    private static Mesh sharedCubeMesh;

    private bool isActive;

    public bool IsActive => isActive;

    public int CurrentEdgeCount => currentEdges.Count;

    private readonly HashSet<BoundaryEdge> captureWallEdges =
    new HashSet<BoundaryEdge>();


    private void Awake()
    {
        if (territoryManager == null)
        {
            territoryManager =
                FindFirstObjectByType<TerritoryManager>();
        }
    }

    private WallSegment CreateWallSegment(int index)
    {
        GameObject segmentObject = new GameObject($"WallSegment_{index}");
        segmentObject.transform.SetParent(wallPoolRoot, false);

        MeshFilter meshFilter = segmentObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = GetSharedCubeMesh();

        MeshRenderer meshRenderer = segmentObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = GetOrCreateMaterial();
        meshRenderer.shadowCastingMode =
            castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        BoxCollider boxCollider = segmentObject.AddComponent<BoxCollider>();
        boxCollider.center = Vector3.zero;
        boxCollider.size = Vector3.one;

        NavMeshObstacle obstacle = segmentObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = Vector3.zero;
        obstacle.size = Vector3.one;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = carveOnlyStationary;

        segmentObject.SetActive(false);

        return new WallSegment
        {
            Transform = segmentObject.transform,
            Renderer = meshRenderer,
            Collider = boxCollider,
            Obstacle = obstacle
        };
    }
    private void BuildWallSegments()
    {
        if (territoryManager == null)
        {
            return;
        }

        EnsureWallPoolCapacity(currentEdges.Count);

        PaperPlayerTerritory player =
            FindFirstObjectByType<PaperPlayerTerritory>();

        Collider[] playerColliders = null;

        if (player != null)
        {
            Rigidbody playerBody = player.GetComponentInParent<Rigidbody>();
            playerColliders = playerBody != null
                ? playerBody.GetComponentsInChildren<Collider>(true)
                : player.GetComponentsInChildren<Collider>(true);
        }

        float cellSize = territoryManager.CellSize;
        float wallHeight = Mathf.Max(0.01f, wallHeightMultiplier * cellSize);
        float wallThickness = Mathf.Max(0.01f, wallThicknessMultiplier * cellSize);
        float halfHeight = wallHeight * 0.5f;

        for (int i = 0; i < currentEdges.Count; i++)
        {
            WallSegment segment = wallPool[i];

            if (!TryGetEdgeEndpoints(
                    currentEdges[i],
                    out Vector3 pointA,
                    out Vector3 pointB))
            {
                segment.Transform.gameObject.SetActive(false);
                continue;
            }

            Vector3 direction = pointB - pointA;
            float length = direction.magnitude;

            if (length < 0.0001f)
            {
                segment.Transform.gameObject.SetActive(false);
                continue;
            }

            direction /= length;

            Vector3 midpoint = (pointA + pointB) * 0.5f;
            midpoint.y += halfHeight;

            segment.Transform.gameObject.SetActive(true);
            segment.Transform.SetPositionAndRotation(
                midpoint,
                Quaternion.LookRotation(direction, Vector3.up)
            );
            segment.Transform.localScale =
                new Vector3(wallThickness, wallHeight, length);

            if (playerColliders != null)
            {
                foreach (Collider playerCollider in playerColliders)
                {
                    if (playerCollider != null && playerCollider.gameObject.activeInHierarchy)
                    {
                        Physics.IgnoreCollision(
                            segment.Collider,
                            playerCollider,
                            true
                        );
                    }
                }
            }
        }

        for (int i = currentEdges.Count; i < wallPool.Count; i++)
        {
            wallPool[i].Transform.gameObject.SetActive(false);
        }
    }


    private void OnDestroy()
    {
        if (wallPoolRoot != null)
        {
            Destroy(wallPoolRoot.gameObject);
        }
    }

    /// <summary>
    /// Recomputes which cell edges sit on the outer border of the
    /// player's owned territory. An edge exists wherever an owned cell
    /// is adjacent to a cell that is NOT owned (including outside the map).
    /// </summary>
    /// 
    public void RefreshBoundaryEdges()
    {
        currentEdges.Clear();
        captureWallEdges.Clear();

        if (territoryManager == null)
        {
            Debug.LogWarning(
                "PlayerTerritoryBoundary: No TerritoryManager assigned/found.",
                this
            );
            return;
        }

        HashSet<Vector2Int> ownedCells =
            territoryManager.OwnedCells as HashSet<Vector2Int>;

        if (ownedCells == null)
        {
            ownedCells = new HashSet<Vector2Int>(territoryManager.OwnedCells);
        }

        foreach (Vector2Int cell in ownedCells)
        {
            CheckEdge(cell, cell + Vector2Int.right, EdgeDirection.East, ownedCells);
            CheckEdge(cell, cell + Vector2Int.left, EdgeDirection.West, ownedCells);
            CheckEdge(cell, cell + Vector2Int.up, EdgeDirection.North, ownedCells);
            CheckEdge(cell, cell + Vector2Int.down, EdgeDirection.South, ownedCells);
        }
    }

    private void CheckEdge(
    Vector2Int cell,
    Vector2Int neighbor,
    EdgeDirection direction,
    HashSet<Vector2Int> ownedCells)
    {
        if (ownedCells.Contains(neighbor))
        {
            return;
        }

        BoundaryEdge edge = new BoundaryEdge
        {
            Cell = cell,
            Direction = direction
        };

        currentEdges.Add(edge);
        captureWallEdges.Add(edge);
    }

    public bool BlocksCaptureStep(Vector2Int from, Vector2Int to)
    {
        if (!isActive)
        {
            return false;
        }

        if (to == from + Vector2Int.right)
        {
            return captureWallEdges.Contains(new BoundaryEdge
            {
                Cell = from,
                Direction = EdgeDirection.East
            }) ||
            captureWallEdges.Contains(new BoundaryEdge
            {
                Cell = to,
                Direction = EdgeDirection.West
            });
        }

        if (to == from + Vector2Int.left)
        {
            return captureWallEdges.Contains(new BoundaryEdge
            {
                Cell = from,
                Direction = EdgeDirection.West
            }) ||
            captureWallEdges.Contains(new BoundaryEdge
            {
                Cell = to,
                Direction = EdgeDirection.East
            });
        }

        if (to == from + Vector2Int.up)
        {
            return captureWallEdges.Contains(new BoundaryEdge
            {
                Cell = from,
                Direction = EdgeDirection.North
            }) ||
            captureWallEdges.Contains(new BoundaryEdge
            {
                Cell = to,
                Direction = EdgeDirection.South
            });
        }

        if (to == from + Vector2Int.down)
        {
            return captureWallEdges.Contains(new BoundaryEdge
            {
                Cell = from,
                Direction = EdgeDirection.South
            }) ||
            captureWallEdges.Contains(new BoundaryEdge
            {
                Cell = to,
                Direction = EdgeDirection.North
            });
        }

        return false;
    }

    /// <summary>
    /// Returns the two world-space endpoints of a boundary edge segment.
    /// </summary>
    private bool TryGetEdgeEndpoints(
        BoundaryEdge edge,
        out Vector3 pointA,
        out Vector3 pointB)
    {
        pointA = Vector3.zero;
        pointB = Vector3.zero;

        if (territoryManager == null)
        {
            return false;
        }

        Vector3 cellCenter = territoryManager.CellToWorld(edge.Cell);
        float half = territoryManager.CellSize * 0.5f;

        switch (edge.Direction)
        {
            case EdgeDirection.East:
                pointA = cellCenter + new Vector3(half, 0f, -half);
                pointB = cellCenter + new Vector3(half, 0f, half);
                break;

            case EdgeDirection.West:
                pointA = cellCenter + new Vector3(-half, 0f, -half);
                pointB = cellCenter + new Vector3(-half, 0f, half);
                break;

            case EdgeDirection.North:
                pointA = cellCenter + new Vector3(-half, 0f, half);
                pointB = cellCenter + new Vector3(half, 0f, half);
                break;

            case EdgeDirection.South:
                pointA = cellCenter + new Vector3(-half, 0f, -half);
                pointB = cellCenter + new Vector3(half, 0f, -half);
                break;
        }

        return true;
    }

    /// <summary>
    /// Positions/scales pooled wall segments (mesh + NavMeshObstacle) to
    /// match currentEdges. Grows the pool with new segments only when more
    /// are needed; never destroys segments, only activates/deactivates them.
    /// </summary>
    /// 


    private void EnsureWallPoolCapacity(int required)
    {
        if (wallPoolRoot == null)
        {
            GameObject rootObject = new GameObject("PlayerTerritoryBoundary_WallPool");
            rootObject.transform.SetParent(transform, false);
            wallPoolRoot = rootObject.transform;
        }

        while (wallPool.Count < required)
        {
            wallPool.Add(CreateWallSegment(wallPool.Count));
        }
    }



    private Material GetOrCreateMaterial()
    {
        if (wallMaterial != null)
        {
            return wallMaterial;
        }

        if (runtimeMaterial != null)
        {
            return runtimeMaterial;
        }

        Shader shader =
            Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("Standard") ??
            Shader.Find("Diffuse");

        runtimeMaterial = new Material(shader);

        if (runtimeMaterial.HasProperty("_BaseColor"))
        {
            runtimeMaterial.SetColor("_BaseColor", wallColor);
        }

        if (runtimeMaterial.HasProperty("_Color"))
        {
            runtimeMaterial.color = wallColor;
        }

        return runtimeMaterial;
    }

    private static Mesh GetSharedCubeMesh()
    {
        if (sharedCubeMesh == null)
        {
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sharedCubeMesh = temp.GetComponent<MeshFilter>().sharedMesh;

            if (Application.isPlaying)
            {
                Destroy(temp);
            }
            else
            {
                DestroyImmediate(temp);
            }
        }

        return sharedCubeMesh;
    }

    /// <summary>
    /// Public entry point later steps (activation, territory-change hook)
    /// will call to fully refresh the boundary (visual + blocking).
    /// </summary>

    public void RebuildBoundaryVisual()
    {
        if (isActive)
        {
            MoveEnemiesOutside();
        }

        RefreshBoundaryEdges();
        BuildWallSegments();
    }

    private void MoveEnemiesOutside()
    {
        if (territoryManager == null)
        {
            return;
        }

        EnemyAI[] enemies =
            FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);

        foreach (EnemyAI enemy in enemies)
        {
            if (enemy != null &&
                enemy.IsAlive &&
                territoryManager.IsInsideTerritory(enemy.transform.position))
            {
                enemy.MoveOutsidePlayerTerritory();
            }
        }
    }

    [ContextMenu("Detect Border Edges (Debug)")]
    private void DebugDetectBorderEdges()
    {
        RefreshBoundaryEdges();

        int ownedCount =
            territoryManager != null
                ? territoryManager.OwnedCells.Count
                : 0;

        Debug.Log(
            $"PlayerTerritoryBoundary: Detected {currentEdges.Count} boundary edges " +
            $"from {ownedCount} owned cells.",
            this
        );
    }

    [ContextMenu("Rebuild Wall Visual (Debug)")]
    private void DebugRebuildWallVisual()
    {
        RebuildBoundaryVisual();

        Debug.Log(
            $"PlayerTerritoryBoundary: Built {currentEdges.Count} wall segments with NavMeshObstacle carving.",
            this
        );
    }

    private void OnDrawGizmosSelected()
    {
        if (!debugDrawGizmos ||
            territoryManager == null ||
            currentEdges.Count == 0)
        {
            return;
        }

        Gizmos.color = gizmoColor;

        Vector3 offset = new Vector3(0f, gizmoHeightOffset, 0f);

        for (int i = 0; i < currentEdges.Count; i++)
        {
            if (!TryGetEdgeEndpoints(currentEdges[i], out Vector3 pointA, out Vector3 pointB))
            {
                continue;
            }

            Gizmos.DrawLine(pointA + offset, pointB + offset);
        }
    }


    /// <summary>
    /// Public entry point for a UI Button's OnClick(). Turns the boundary on
    /// for this level, builds it immediately from the current territory, and
    /// subscribes to future territory changes so it stays in sync. OFF by
    /// default; safe to call multiple times — repeat calls are ignored.
    /// </summary>
    public void ActivateBoundary()
    {

        if (isActive)
        {
            return;
        }

        if (territoryManager == null)
        {
            territoryManager = FindFirstObjectByType<TerritoryManager>();
        }

        if (territoryManager == null)
        {
            Debug.LogWarning(
                "PlayerTerritoryBoundary: Cannot activate, no TerritoryManager found.",
                this
            );
            return;
        }

        isActive = true;

        territoryManager.OnPlayerTerritoryChanged += HandleTerritoryChanged;

        RebuildBoundaryVisual();
    }

    /// <summary>
    /// Turns the boundary off and hides all wall segments. Segments are
    /// deactivated, not destroyed, so a later ActivateBoundary() call is cheap.
    /// </summary>
    public void DeactivateBoundary()
    {
        if (!isActive)
        {
            return;
        }

        isActive = false;

        if (territoryManager != null)
        {
            territoryManager.OnPlayerTerritoryChanged -= HandleTerritoryChanged;
        }

        for (int i = 0; i < wallPool.Count; i++)
        {
            wallPool[i].Transform.gameObject.SetActive(false);
        }
    }

    private void HandleTerritoryChanged()
    {
        if (!isActive)
        {
            return;
        }

        RebuildBoundaryVisual();
    }
}