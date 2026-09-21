using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Generates and renders a large amount of grass using GPU instancing.
/// Individual grass GameObjects are not created, which keeps CPU and
/// memory usage low on mobile and low-end devices.
/// </summary>
[DefaultExecutionOrder(-200)]
public class OptimizedGrassField : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Renderer planeRenderer;
    [SerializeField] private GameObject grassPrefab;

    [SerializeField] private bool useLowPolyTufts = true;

    [Header("Grass Amount")]
    [Min(1)]
    [SerializeField] private int grassCount = 10000;

    [Header("Placement")]
    [Min(0.01f)]
    [SerializeField] private float spacing = 0.05f;

    [Tooltip("On Android/iOS, use fewer instanced tufts while keeping the field full.")]
    [Min(1f)]
    [SerializeField] private float mobileSpacingMultiplier = 1.25f;

    [Range(0f, 0.2f)]
    [SerializeField] private float jitter = 0.04f;

    [Header("Appearance")]
    [SerializeField]
    private Vector2 scaleRange = new Vector2(0.8f, 1.2f);

    [SerializeField]
    private bool randomYRotation = true;

    [Range(0f, 10f)]
    [SerializeField] private float randomTiltDegrees = 4f;

    [Header("Performance")]
    [SerializeField]
    private bool castShadows = false;

    [SerializeField]
    private bool receiveShadows = false;

    [Header("Visibility")]
    [Tooltip("Keep each instanced batch in a small world-space area for camera culling.")]
    [Min(1f)]
    [SerializeField] private float batchTileSize = 4f;

    [Tooltip("Maximum horizontal distance from the camera to a grass batch. Set to 0 to disable distance culling.")]
    [Min(0f)]
    [SerializeField] private float maxDrawDistance = 22f;

    [Min(0f)]
    [SerializeField] private float mobileDrawDistance = 18f;

    [Header("Generation")]
    [SerializeField]
    private bool generateOnStart = true;

    [SerializeField]
    private int randomSeed = 12345;

    private const int MaxInstancesPerBatch = 1023;

    private readonly List<Matrix4x4[]> batches =
        new List<Matrix4x4[]>();

    private readonly List<Bounds> batchBounds =
        new List<Bounds>();

    private readonly Plane[] frustumPlanes = new Plane[6];
    private Camera viewCamera;

    private Mesh grassMesh;
    private Material grassMaterial;

    private Vector3[] grassPositions;
    private Matrix4x4[] grassMatrices;
    private bool[] grassCut;

    private bool generated;

    private int[] activeSlotByGrassIndex;
    private int[] grassIndexByActiveSlot;
    private int activeGrassCount;

    private readonly List<int> batchCounts =
        new List<int>();

    public int GenerationVersion { get; private set; }

    /// <summary>
    /// Returns true when grass generation has completed successfully.
    /// </summary>
    public bool IsGenerated => generated;

    /// <summary>
    /// Returns the total number of generated grass blades.
    /// </summary>
    public int GrassCount =>
        grassPositions != null ? grassPositions.Length : 0;

    /// <summary>
    /// Generates grass before other systems that depend on it start.
    /// </summary>
    private void Awake()
    {
        if (generateOnStart)
        {
            GenerateGrass();
        }
    }

    /// <summary>
    /// Draws all grass batches every frame using GPU instancing.
    /// </summary>

    private void Update()
    {
        if (!generated ||
            grassMesh == null ||
            grassMaterial == null)
        {
            return;
        }

        ShadowCastingMode shadowMode =
            castShadows
                ? ShadowCastingMode.On
                : ShadowCastingMode.Off;

        if (viewCamera == null || !viewCamera.isActiveAndEnabled)
        {
            viewCamera = Camera.main;
        }

        bool checkVisibility = viewCamera != null;
        Vector3 cameraPosition = Vector3.zero;
        float drawDistance = Application.isMobilePlatform
            ? Mathf.Min(maxDrawDistance, mobileDrawDistance)
            : maxDrawDistance;
        float maxDistanceSqr = drawDistance * drawDistance;

        if (checkVisibility)
        {
            GeometryUtility.CalculateFrustumPlanes(viewCamera, frustumPlanes);
            cameraPosition = viewCamera.transform.position;
        }

        for (int i = 0; i < batches.Count; i++)
        {
            int count = batchCounts[i];

            if (count == 0)
            {
                continue;
            }

            if (checkVisibility)
            {
                Bounds bounds = batchBounds[i];
                if (!GeometryUtility.TestPlanesAABB(frustumPlanes, bounds))
                {
                    continue;
                }

                if (drawDistance > 0f)
                {
                    // The camera is elevated; measure ground-plane distance.
                    cameraPosition.y = bounds.center.y;
                    if (bounds.SqrDistance(cameraPosition) > maxDistanceSqr)
                    {
                        continue;
                    }
                }
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
    }

    /// <summary>
    /// Makes sure grass exists before another system uses it.
    /// </summary>
    public bool EnsureGenerated()
    {
        if (!generated)
        {
            GenerateGrass();
        }

        return generated;
    }

    /// <summary>
    /// Generates grass across the renderer bounds using deterministic
    /// random placement.
    /// </summary>
    [ContextMenu("Generate Grass")]
    public void GenerateGrass()
    {
        ClearGrass();

        if (planeRenderer == null)
        {
            Debug.LogError(
                "OptimizedGrassField: Plane Renderer is missing.",
                this
            );

            return;
        }

        if (grassPrefab == null)
        {
            Debug.LogError(
                "OptimizedGrassField: Grass Prefab is missing.",
                this
            );

            return;
        }

        MeshFilter meshFilter =
            grassPrefab.GetComponentInChildren<MeshFilter>();

        MeshRenderer meshRenderer =
            grassPrefab.GetComponentInChildren<MeshRenderer>();

        if (meshFilter == null ||
            meshFilter.sharedMesh == null)
        {
            Debug.LogError(
                "OptimizedGrassField: Grass Prefab requires a MeshFilter.",
                this
            );

            return;
        }

        if (meshRenderer == null ||
            meshRenderer.sharedMaterial == null)
        {
            Debug.LogError(
                "OptimizedGrassField: Grass Prefab requires a MeshRenderer.",
                this
            );

            return;
        }

        grassMesh = useLowPolyTufts
            ? StylizedGrassMesh.Shared
            : meshFilter.sharedMesh;
        grassMaterial = meshRenderer.sharedMaterial;

        // Enable GPU instancing on the grass material.
        grassMaterial.enableInstancing = true;

        Bounds bounds = planeRenderer.bounds;

        float effectiveSpacing = spacing *
            (Application.isMobilePlatform ? mobileSpacingMultiplier : 1f);

        int xCount = Mathf.Max(
            1,
            Mathf.FloorToInt(bounds.size.x / effectiveSpacing)
        );

        int zCount = Mathf.Max(
            1,
            Mathf.FloorToInt(bounds.size.z / effectiveSpacing)
        );

        int finalCount = Mathf.Min(
            grassCount,
            xCount * zCount
        );

        grassPositions = new Vector3[finalCount];
        grassMatrices = new Matrix4x4[finalCount];
        grassCut = new bool[finalCount];

        Random.State previousRandomState = Random.state;

        Random.InitState(randomSeed);

        float groundY = bounds.max.y;

        for (int i = 0; i < finalCount; i++)
        {
            int xIndex = i % xCount;
            int zIndex = i / xCount;

            float x =
                bounds.min.x +
                (xIndex + 0.5f) * effectiveSpacing +
                Random.Range(-jitter, jitter);

            float z =
                bounds.min.z +
                (zIndex + 0.5f) * effectiveSpacing +
                Random.Range(-jitter, jitter);

            Vector3 position = new Vector3(
                x,
                groundY,
                z
            );
            
            Quaternion rotation = Quaternion.Euler(Random.Range(-randomTiltDegrees, randomTiltDegrees),randomYRotation ? Random.Range(0f, 360f) : 0f,Random.Range(-randomTiltDegrees, randomTiltDegrees));

            float scale = Random.Range(
                scaleRange.x,
                scaleRange.y
            );

            grassPositions[i] = position;

            grassMatrices[i] = Matrix4x4.TRS(
                position,
                rotation,
                //Vector3.one * scale

                new Vector3(scale, scale * 0.4f,scale)
            );
        }

        Random.state = previousRandomState;

        BuildBatches();

        generated = true;

        Debug.Log(
            $"Grass generated: {finalCount} blades, " +
            $"GPU batches: {batches.Count}",
            this
        );
    }

    /// <summary>
    /// Groups blades into small world-space tiles, then splits each tile
    /// into Unity's maximum 1023-instance GPU instancing batches.
    /// </summary>

    private void BuildBatches()
    {
        batches.Clear();
        batchCounts.Clear();
        batchBounds.Clear();

        if (grassMatrices == null)
        {
            return;
        }

        int total = grassMatrices.Length;
        activeGrassCount = total;

        activeSlotByGrassIndex =
            new int[total];

        var tileIndices = new Dictionary<Vector2Int, List<int>>();
        Vector3 origin = planeRenderer.bounds.min;
        float tileSize = Mathf.Max(1f, batchTileSize);
        for (int i = 0; i < total; i++)
        {
            Vector3 position = grassPositions[i];
            var tile = new Vector2Int(
                Mathf.FloorToInt((position.x - origin.x) / tileSize),
                Mathf.FloorToInt((position.z - origin.z) / tileSize)
            );

            if (!tileIndices.TryGetValue(tile, out List<int> indices))
            {
                indices = new List<int>();
                tileIndices.Add(tile, indices);
            }

            indices.Add(i);
        }

        int requiredBatches = 0;
        foreach (List<int> indices in tileIndices.Values)
        {
            requiredBatches += (indices.Count + MaxInstancesPerBatch - 1) /
                               MaxInstancesPerBatch;
        }

        grassIndexByActiveSlot = new int[requiredBatches * MaxInstancesPerBatch];

        foreach (List<int> indices in tileIndices.Values)
        {
            for (int start = 0; start < indices.Count; start += MaxInstancesPerBatch)
            {
                int amount = Mathf.Min(MaxInstancesPerBatch, indices.Count - start);
                int batchIndex = batches.Count;
                var batch = new Matrix4x4[MaxInstancesPerBatch];
                var bounds = new Bounds(grassPositions[indices[start]], Vector3.zero);

                for (int local = 0; local < amount; local++)
                {
                    int grassIndex = indices[start + local];
                    int slot = batchIndex * MaxInstancesPerBatch + local;
                    batch[local] = grassMatrices[grassIndex];
                    grassIndexByActiveSlot[slot] = grassIndex;
                    activeSlotByGrassIndex[grassIndex] = slot;
                    bounds.Encapsulate(grassPositions[grassIndex]);
                }

                // Allow for mesh height, tilt, and animated tip movement.
                bounds.Expand(new Vector3(2f, 3f, 2f));
                batches.Add(batch);
                batchCounts.Add(amount);
                batchBounds.Add(bounds);
            }
        }

        // The source array is no longer needed after its matrices are tiled.
        grassMatrices = null;

        GenerationVersion++;
    }


    /// <summary>
    /// Removes one blade by swapping with the last active blade in its tile.
    /// </summary>

    public bool CutGrassAtIndex(int index)
    {
        if (!generated ||
            grassCut == null ||
            index < 0 ||
            index >= grassCut.Length ||
            grassCut[index])
        {
            return false;
        }

        int slot =
            activeSlotByGrassIndex[index];

        if (slot < 0)
        {
            return false;
        }

        int batchIndex = slot / MaxInstancesPerBatch;
        int localSlot = slot % MaxInstancesPerBatch;
        int lastLocalSlot = batchCounts[batchIndex] - 1;

        if (lastLocalSlot < 0)
        {
            return false;
        }

        // Keep each batch spatially coherent as blades are cut.
        if (localSlot != lastLocalSlot)
        {
            int lastSlot = batchIndex * MaxInstancesPerBatch + lastLocalSlot;
            int movedGrassIndex = grassIndexByActiveSlot[lastSlot];
            batches[batchIndex][localSlot] = batches[batchIndex][lastLocalSlot];
            grassIndexByActiveSlot[slot] = movedGrassIndex;
            activeSlotByGrassIndex[movedGrassIndex] = slot;
        }

        grassCut[index] = true;
        activeSlotByGrassIndex[index] = -1;

        activeGrassCount--;

        batchCounts[batchIndex]--;

        return true;
    }

    /// <summary>
    /// Returns the world position of a grass blade.
    /// </summary>
    public Vector3 GetGrassPosition(int index)
    {
        return grassPositions[index];
    }

    /// <summary>
    /// Clears all generated grass data and GPU batches.
    /// </summary>
    /// 
    [ContextMenu("Clear Grass")]
    public void ClearGrass()
    {
        batches.Clear();
        batchCounts.Clear();
        batchBounds.Clear();

        grassPositions = null;
        grassMatrices = null;
        grassCut = null;

        activeSlotByGrassIndex = null;
        grassIndexByActiveSlot = null;
        activeGrassCount = 0;

        generated = false;
    }

    public bool IsGrassCut(int index)
    {
        return grassCut != null
            && index >= 0
            && index < grassCut.Length
            && grassCut[index];
    }
}
