using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Divides the grass field into logical cells.
/// The grid provides fast access to grass inside a specific area.
/// It is used for both normal grass cutting and territory capture.
/// </summary>
public class GrassCutGrid : MonoBehaviour
{
    [Header("Grass Field")]
    [SerializeField]
    private OptimizedGrassField grassField;

    [Header("Grid")]
    [Min(0.05f)]
    [SerializeField]
    private float cellSize = 0.5f;

    private readonly Dictionary<Vector2Int, List<int>> cells =
        new Dictionary<Vector2Int, List<int>>();

    public event Action<Vector3> GrassWasCut;

    private int builtVersion = -1;

    /// <summary>
    /// Builds the grass lookup grid when the scene starts.
    /// </summary>
    private void Start()
    {
        EnsureReady();
    }

    private bool EnsureReady()
    {
        if (grassField == null)
        {
            grassField =
                FindFirstObjectByType<OptimizedGrassField>();
        }

        if (grassField == null ||
            !grassField.EnsureGenerated())
        {
            return false;
        }

        if (builtVersion !=
            grassField.GenerationVersion)
        {
            BuildGrid();
        }

        return builtVersion ==
               grassField.GenerationVersion;
    }

    /// <summary>
    /// Builds a dictionary containing all grass indices grouped by cell.
    /// </summary>
    [ContextMenu("Build Grid")]
    public void BuildGrid()
    {
        cells.Clear();
        builtVersion = -1;

        if (grassField == null)
        {
            grassField =
                FindFirstObjectByType<OptimizedGrassField>();
        }

        if (grassField == null ||
            !grassField.EnsureGenerated())
        {
            Debug.LogError(
                "GrassCutGrid: Grass field is missing or generation failed.",
                this
            );

            return;
        }

        int grassCount =
            grassField.GrassCount;

        for (int i = 0;
             i < grassCount;
             i++)
        {
            // Do not restore previously cut blades to the lookup.
            if (grassField.IsGrassCut(i))
            {
                continue;
            }

            Vector3 position =
                grassField.GetGrassPosition(i);

            Vector2Int cell =
                GetCell(position);

            if (!cells.TryGetValue(
                cell,
                out List<int> indices))
            {
                indices = new List<int>(8);
                cells.Add(cell, indices);
            }

            indices.Add(i);
        }

        builtVersion =
            grassField.GenerationVersion;
    }

    /// <summary>
    /// Cuts grass around a world position.
    /// This is used while the player is walking outside territory.
    /// </summary>
    /// 
    public int Cut(
    Vector3 worldPosition,
    float radius)
    {
        Vector3 unusedEffectPosition;
        return Cut(
            worldPosition,
            radius,
            worldPosition,
            out unusedEffectPosition
        );
    }

    /// <summary>
    /// Cuts grass and returns the actual cut blade nearest the preferred
    /// effect position. The effect can then spawn at grass height, not at
    /// the player's collider centre.
    /// </summary>
    public int Cut(
    Vector3 worldPosition,
    float radius,
    Vector3 preferredEffectPosition,
    out Vector3 effectPosition)
    {
        effectPosition = worldPosition;

        if (!EnsureReady())
        {
            return 0;
        }

        radius =
            Mathf.Max(0.01f, radius);

        float safeCellSize =
            Mathf.Max(0.05f, cellSize);

        float radiusSqr =
            radius * radius;

        Vector2Int center =
            GetCell(worldPosition);

        int cellRange =
            Mathf.CeilToInt(
                radius / safeCellSize
            );

        int cutCount = 0;
        float bestEffectDistanceSqr = float.PositiveInfinity;

        for (int x = -cellRange;
             x <= cellRange;
             x++)
        {
            for (int z = -cellRange;
                 z <= cellRange;
                 z++)
            {
                Vector2Int cell =
                    new Vector2Int(
                        center.x + x,
                        center.y + z
                    );

                if (!cells.TryGetValue(
                    cell,
                    out List<int> indices))
                {
                    continue;
                }

                for (int i =
                         indices.Count - 1;
                     i >= 0;
                     i--)
                {
                    int grassIndex =
                        indices[i];

                    Vector3 grassPosition =
                        grassField.GetGrassPosition(
                            grassIndex
                        );

                    float dx =
                        grassPosition.x -
                        worldPosition.x;

                    float dz =
                        grassPosition.z -
                        worldPosition.z;

                    float distanceSqr =
                        dx * dx +
                        dz * dz;

                    if (distanceSqr >
                        radiusSqr)
                    {
                        continue;
                    }

                    if (!grassField.CutGrassAtIndex(
                            grassIndex))
                    {
                        continue;
                    }

                    float effectDx =
                        grassPosition.x - preferredEffectPosition.x;
                    float effectDz =
                        grassPosition.z - preferredEffectPosition.z;
                    float effectDistanceSqr =
                        effectDx * effectDx + effectDz * effectDz;

                    if (effectDistanceSqr < bestEffectDistanceSqr)
                    {
                        bestEffectDistanceSqr = effectDistanceSqr;
                        effectPosition = grassPosition;
                    }

                    // Actual blade position, not a trail sample.
                    GrassWasCut?.Invoke(
                        grassPosition
                    );

                    int lastIndex =
                        indices.Count - 1;

                    indices[i] =
                        indices[lastIndex];

                    indices.RemoveAt(
                        lastIndex
                    );

                    cutCount++;
                }
            }
        }

        return cutCount;
    }

    /// <summary>
    /// Cuts ALL wild grass contained inside one complete territory cell.
    /// This is used when the player captures new territory.
    /// </summary>
    /// 
    public int CutCell(Vector2Int cell)
    {
        if (!EnsureReady())
        {
            return 0;
        }

        if (!cells.TryGetValue(
            cell,
            out List<int> indices))
        {
            return 0;
        }

        int cutCount = 0;

        for (int i =
                 indices.Count - 1;
             i >= 0;
             i--)
        {
            int grassIndex =
                indices[i];

            if (grassField.CutGrassAtIndex(
                grassIndex))
            {
                cutCount++;
            }

            indices.RemoveAt(i);
        }

        // Deliberately do not fire GrassWasCut here:
        // capture can clear many blades at once.
        return cutCount;
    }

    /// <summary>
    /// Cuts ALL wild grass inside multiple territory cells.
    /// This is more efficient than repeatedly using radius-based cutting.
    /// </summary>
    public int CutCells(
        IEnumerable<Vector2Int> territoryCells)
    {
        if (territoryCells == null)
        {
            return 0;
        }

        int totalCut =
            0;

        foreach (Vector2Int cell in territoryCells)
        {
            totalCut +=
                CutCell(cell);
        }

        return totalCut;
    }

    /// <summary>
    /// Converts a world position into a grid coordinate.
    /// </summary>
    public Vector2Int GetCell(
        Vector3 position)
    {
        float safeCellSize =
            Mathf.Max(
                0.05f,
                cellSize
            );

        return new Vector2Int(
            Mathf.FloorToInt(
                position.x /
                safeCellSize
            ),
            Mathf.FloorToInt(
                position.z /
                safeCellSize
            )
        );
    }

    /// <summary>
    /// Returns the configured cell size.
    /// </summary>
    public float CellSize =>
        cellSize;
}
