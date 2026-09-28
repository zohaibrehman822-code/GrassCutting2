using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class PaperPlayerTerritory : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TerritoryManager territoryManager;
    [SerializeField] private GrassCutGrid grassGrid;
    [SerializeField] private PlayerTerritoryRenderer territoryRenderer;

    [Header("Trail Placement")]
    [Min(0.05f)]
    [SerializeField] private float trailPointDistance = 0.2f;

    [Header("Line Trail")]
    [SerializeField] private bool enableLineRenderer;
    [SerializeField] private LineRenderer trailRenderer;

    [Header("Cut Grass Trail")]
    [SerializeField] private bool enableCutGrassTrail = true;

    [Tooltip("Used as a mesh/material source; not instantiated per cut.")]
    [SerializeField] private GameObject cuttedGrassPrefab;

    [Min(0.01f)]
    [SerializeField] private float cuttedGrassScale = 0.25f;

    [SerializeField] private float trailHeightOffset = 0.03f;
    [SerializeField] private bool castTrailShadows;
    [SerializeField] private bool receiveTrailShadows;

    private const int MaxInstancesPerBatch = 1023;

    private readonly List<Vector3> trailPositions =
        new List<Vector3>(128);

    // Actual positions reported by GrassCutGrid.
    private readonly List<Vector3> cutGrassPositions =
        new List<Vector3>(256);

    private readonly List<Matrix4x4[]> trailBatches =
        new List<Matrix4x4[]>(2);

    private Mesh trailMesh;
    private Material trailMaterial;
    private Matrix4x4 prefabMeshLocalMatrix;

    private int activeTrailInstanceCount;
    private bool outsideTerritory;

    private bool useSampledCutGrassTrail;
    private Vector3 lastTrailPosition;

    private float distanceSinceCapturedTrailSample;

    public bool IsOutsideTerritory => outsideTerritory;

    [Header("Capture Contact")]
    [Tooltip("Match this to the Player body's ground-level radius.")]
    [Min(0.01f)]
    private Movement playerMovement;
    [SerializeField] private float territoryTouchRadius = 0.15f;

    [Header("Trail Kill")]
    [SerializeField] private float trailKillRadius = 0.3f;

    private GrassCutter playerCutter;

    public event System.Action OnPlayerDeath;

    [Header("Captured Territory Trail Density")]
    [Tooltip("Visual rows across the cut width. Wild-grass density is unchanged.")]
    [SerializeField, Min(1)]
    private int capturedTrailRows = 5;

    private void Awake()
    {
        if (territoryManager == null)
        {
            territoryManager =
                FindFirstObjectByType<TerritoryManager>();
        }

        if (grassGrid == null)
        {
            grassGrid =
                FindFirstObjectByType<GrassCutGrid>();
        }

        if (territoryRenderer == null)
        {
            territoryRenderer =
                FindFirstObjectByType<PlayerTerritoryRenderer>();
        }

        InitializeCutGrassVisual();

        if (trailRenderer != null)
        {
            trailRenderer.useWorldSpace = true;
            trailRenderer.positionCount = 0;
            trailRenderer.enabled = enableLineRenderer;
        }
        playerMovement = GetComponent<Movement>();
        playerCutter = GetComponentInParent<GrassCutter>();
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

    private void OnDisable()
    {
        if (grassGrid != null)
        {
            grassGrid.GrassWasCut -= OnGrassWasCut;
        }
    }

    private void Update()
    {
        SyncLineRendererSetting();

        if (territoryManager == null)
        {
            return;
        }

        bool touchingTerritory =
            territoryManager.TouchesTerritory(
                transform.position,
                territoryTouchRadius
            );

        if (territoryManager.IsPlayerInsideEnemyTrail(
                transform.position,
                trailKillRadius,
                out EnemyAI hitEnemy))
        {
            hitEnemy.Die();
        }

        if (!outsideTerritory)
        {
            if (!touchingTerritory)
            {
                BeginTrail();
            }

            return;
        }

        territoryManager.AddTrailPosition(
            transform.position
        );

        UpdateTrail();

        if (touchingTerritory)
        {
            CompleteTrail();
        }
    }

    public bool IsPointOnActiveTrail(Vector3 worldPosition, float radius)
    {
        if (!enabled || !outsideTerritory || trailPositions.Count == 0)
        {
            return false;
        }

        Vector2 point = new Vector2(worldPosition.x, worldPosition.z);
        float radiusSqr = radius * radius;

        for (int i = 0; i < trailPositions.Count; i++)
        {
            Vector3 startPosition = trailPositions[i];
            Vector3 endPosition = i + 1 < trailPositions.Count
                ? trailPositions[i + 1]
                : transform.position;

            Vector2 start = new Vector2(startPosition.x, startPosition.z);
            Vector2 end = new Vector2(endPosition.x, endPosition.z);
            Vector2 segment = end - start;

            float fraction = segment.sqrMagnitude > 0.000001f
                ? Mathf.Clamp01(
                    Vector2.Dot(point - start, segment) / segment.sqrMagnitude
                  )
                : 0f;

            Vector2 closestPoint = start + segment * fraction;

            if ((point - closestPoint).sqrMagnitude <= radiusSqr)
            {
                return true;
            }
        }

        return false;
    }

    public void OnPlayerDied()
    {
        if (!enabled)
        {
            return;
        }

        enabled = false;
        outsideTerritory = false;
        cutGrassPositions.Clear();
        ClearTrailVisuals();

        if (territoryManager != null)
        {
            territoryManager.CancelPlayerTrail();
        }

        Time.timeScale = 0f;
        Debug.Log("Player Died - Game Over");

        OnPlayerDeath?.Invoke();
    }

    private void LateUpdate()
    {
        DrawCutGrassTrail();
    }

    private void OnGrassWasCut(Vector3 grassPosition)
    {
        if (playerCutter == null ||
            grassGrid == null ||
            grassGrid.LastCutSource != playerCutter)
        {
            return;
        }

        cutGrassPositions.Add(grassPosition);

        // Preserve one visual for every actual wild-grass blade cut.
        AddCutGrassVisual(grassPosition);
    }

    private void AddCutGrassVisual(
    Vector3 grassPosition)
    {
        if (!enableCutGrassTrail ||
            trailMesh == null ||
            trailMaterial == null ||
            cuttedGrassPrefab == null ||
            territoryManager == null)
        {
            return;
        }

        Vector3 visualPosition =
            grassPosition;

        visualPosition.y =
            territoryManager.GroundY +
            trailHeightOffset;

        int instanceIndex =
            activeTrailInstanceCount;

        int batchIndex =
            instanceIndex /
            MaxInstancesPerBatch;

        int indexInsideBatch =
            instanceIndex %
            MaxInstancesPerBatch;

        if (batchIndex >=
            trailBatches.Count)
        {
            trailBatches.Add(
                new Matrix4x4[
                    MaxInstancesPerBatch
                ]
            );
        }

        Matrix4x4 cutTransform =
            Matrix4x4.TRS(
                visualPosition,
                cuttedGrassPrefab.transform.rotation,
                Vector3.one *
                cuttedGrassScale
            );

        trailBatches[batchIndex]
            [indexInsideBatch] =
                cutTransform *
                prefabMeshLocalMatrix;

        activeTrailInstanceCount++;
    }

    private void InitializeCutGrassVisual()
    {
        if (cuttedGrassPrefab == null)
        {
            return;
        }

        MeshFilter meshFilter =
            cuttedGrassPrefab
                .GetComponentInChildren<MeshFilter>(true);

        if (meshFilter == null ||
            meshFilter.sharedMesh == null)
        {
            Debug.LogError(
                "PaperPlayerTerritory: Cutted grass prefab needs a MeshFilter and mesh.",
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
                "PaperPlayerTerritory: Cutted grass mesh needs a MeshRenderer and material.",
                this
            );

            return;
        }

        trailMesh = meshFilter.sharedMesh;
        trailMaterial = meshRenderer.sharedMaterial;
        trailMaterial.enableInstancing = true;

        prefabMeshLocalMatrix =
            cuttedGrassPrefab.transform.worldToLocalMatrix *
            meshFilter.transform.localToWorldMatrix;
    }

    private void BeginTrail()
    {
        outsideTerritory = true;

        trailPositions.Clear();
        distanceSinceCapturedTrailSample = 0f;

        if (trailRenderer != null)
        {
            trailRenderer.positionCount = 0;
        }

        lastTrailPosition = transform.position;

        // Wild-grass visuals remain driven by OnGrassWasCut.
        // Captured-territory visuals are handled by AddVisualTrailPoint.
        useSampledCutGrassTrail = false;

        territoryManager.StartTrail(lastTrailPosition);

        AddVisualTrailPoint(lastTrailPosition);
    }

    private void UpdateTrail()
    {
        Vector3 currentPosition = transform.position;
        Vector3 segmentStart = lastTrailPosition;

        Vector3 movement = currentPosition - segmentStart;
        movement.y = 0f;

        float remainingDistance = movement.magnitude;

        if (remainingDistance <= 0.000001f)
        {
            return;
        }

        Vector3 direction = movement / remainingDistance;
        float spacing = Mathf.Max(0.05f, trailPointDistance);

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

            // Your existing function adds extra rows only on enemy territory.
            // It does not add extra cutted-grass visuals on wild grass.
            AddVisualTrailPoint(segmentStart);
        }

        distanceSinceCapturedTrailSample += remainingDistance;
        lastTrailPosition = currentPosition;
    }

    private void AddVisualTrailPoint(Vector3 worldPosition)
    {
        worldPosition.y =
            territoryManager.GroundY + trailHeightOffset;

        Vector3 previousPosition = trailPositions.Count > 0
            ? trailPositions[trailPositions.Count - 1]
            : worldPosition;

        trailPositions.Add(worldPosition);

        if (territoryManager.IsInsideEnemyTerritory(worldPosition))
        {
            float cutRadius = playerCutter != null
                ? playerCutter.GetEffectiveCutRadius()
                : territoryManager.CellSize * 0.5f;

            bool cutEnemyGrass =
                territoryManager.CutEnemyTerritoryGrass(
                    worldPosition,
                    cutRadius
                );

            if (cutEnemyGrass && playerCutter != null)
            {
                // Keep the existing territory-based particle colours.
                playerCutter.PlayCapturedTerritoryParticle(worldPosition);
            }

            Vector3 direction = worldPosition - previousPosition;
            direction.y = 0f;

            if (direction.sqrMagnitude <= 0.000001f)
            {
                direction = Vector3.forward;
            }
            else
            {
                direction.Normalize();
            }

            Vector3 sideways =
                new Vector3(-direction.z, 0f, direction.x);

            int rows = Mathf.Max(1, capturedTrailRows);

            // An odd count preserves the original centre row.
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

                Vector3 position =
                    worldPosition + sideways * offset;

                // Never add these extra visuals to neighbouring wild
                // or player-owned territory.
                if (territoryManager.IsInsideEnemyTerritory(position))
                {
                    AddCutGrassVisual(position);
                }
            }
        }

        if (trailRenderer == null || !enableLineRenderer)
        {
            return;
        }

        trailRenderer.positionCount = trailPositions.Count;

        trailRenderer.SetPosition(
            trailPositions.Count - 1,
            worldPosition
        );
    }

    private void CompleteTrail()
    {
        outsideTerritory = false;

        territoryManager.CompleteTrail(
            cutGrassPositions
        );

        cutGrassPositions.Clear();
        ClearTrailVisuals();
    }

    private void ClearTrailVisuals()
    {
        trailPositions.Clear();
        activeTrailInstanceCount = 0;

        useSampledCutGrassTrail = false;
        distanceSinceCapturedTrailSample = 0f;

        if (trailRenderer != null)
        {
            trailRenderer.positionCount = 0;
        }

        // Keep batch arrays allocated for reuse.
    }

    private void DrawCutGrassTrail()
    {
        if (!enableCutGrassTrail ||
            trailMesh == null ||
            trailMaterial == null ||
            activeTrailInstanceCount == 0)
        {
            return;
        }

        ShadowCastingMode shadowMode =
            castTrailShadows
                ? ShadowCastingMode.On
                : ShadowCastingMode.Off;

        for (int batchIndex = 0;
             batchIndex < trailBatches.Count;
             batchIndex++)
        {
            int batchStart =
                batchIndex * MaxInstancesPerBatch;

            int remaining =
                activeTrailInstanceCount -
                batchStart;

            if (remaining <= 0)
            {
                break;
            }

            int drawCount =
                Mathf.Min(
                    remaining,
                    MaxInstancesPerBatch
                );

            Graphics.DrawMeshInstanced(
                trailMesh,
                0,
                trailMaterial,
                trailBatches[batchIndex],
                drawCount,
                null,
                shadowMode,
                receiveTrailShadows,
                gameObject.layer
            );
        }
    }

    private void SyncLineRendererSetting()
    {
        if (trailRenderer == null ||
            trailRenderer.enabled ==
            enableLineRenderer)
        {
            return;
        }

        trailRenderer.enabled =
            enableLineRenderer;

        if (!enableLineRenderer)
        {
            trailRenderer.positionCount = 0;
            return;
        }

        trailRenderer.positionCount =
            trailPositions.Count;

        for (int i = 0;
             i < trailPositions.Count;
             i++)
        {
            trailRenderer.SetPosition(
                i,
                trailPositions[i]
            );
        }
    }
}
