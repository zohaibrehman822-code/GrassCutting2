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

        if (!outsideTerritory)
        {
            if (!touchingTerritory)
            {
                BeginTrail();
            }

            return;
        }

        if (territoryManager.IsPlayerInsideEnemyTrail(
    transform.position,
    trailKillRadius,
    out EnemyAI killerEnemy))
        {
            int enemyTerritoryCount = territoryManager.GetEnemyTerritoryCount(killerEnemy);
            int playerTerritoryCount = territoryManager.OwnedCells.Count;

            if (playerTerritoryCount > enemyTerritoryCount)
            {
                Debug.Log("Level Passed - Enemy Died (Player had bigger territory)");
                killerEnemy.Die();
            }
            else if (enemyTerritoryCount > playerTerritoryCount)
            {
                Debug.Log("Level Failed - Player Died (Enemy had bigger territory)");
                OnPlayerDied();
                return;
            }
            // Equal territory: nothing happens, tell me if you want a rule for this case.
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

    private void OnPlayerDied()
    {
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

    private void OnGrassWasCut(
    Vector3 grassPosition)
    {
        if (playerCutter != null &&
            grassGrid.LastCutSource != playerCutter)
        {
            return;
        }

        // Keep real cut positions for the existing capture flow.
        cutGrassPositions.Add(
            grassPosition
        );

        // Trails that began in enemy territory use logical trail
        // samples instead. Do not add a second visual here.
        if (useSampledCutGrassTrail)
        {
            return;
        }

        AddCutGrassVisual(
            grassPosition
        );
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

        if (trailRenderer != null)
        {
            trailRenderer.positionCount = 0;
        }

        lastTrailPosition =
            transform.position;

        // A trail beginning in enemy territory cannot rely on
        // GrassWasCut because its wild grass was already removed.
        // Keep using sampled visuals until this trail completes.
        useSampledCutGrassTrail =
            territoryManager.IsInsideEnemyTerritory(
                transform.position
            );

        territoryManager.StartTrail(
            transform.position
        );

        AddVisualTrailPoint(
            transform.position
        );
    }

    private void UpdateTrail()
    {
        Vector3 currentPosition =
            transform.position;

        Vector3 movement =
            currentPosition -
            lastTrailPosition;

        movement.y = 0f;

        float requiredDistanceSqr =
            trailPointDistance *
            trailPointDistance;

        if (movement.sqrMagnitude <
            requiredDistanceSqr)
        {
            return;
        }

        lastTrailPosition =
            currentPosition;

        AddVisualTrailPoint(
            currentPosition
        );
    }

    private void AddVisualTrailPoint(
    Vector3 worldPosition)
    {
        worldPosition.y =
            territoryManager.GroundY +
            trailHeightOffset;

        trailPositions.Add(
            worldPosition
        );

        bool insideEnemyTerritory =
            territoryManager.IsInsideEnemyTerritory(
                worldPosition
            );

        // Render from logical trail samples while inside enemy
        // territory, or for the remainder of a trail that began
        // inside enemy territory.
        if (insideEnemyTerritory ||
            useSampledCutGrassTrail)
        {
            AddCutGrassVisual(
                worldPosition
            );
        }

        if (trailRenderer == null ||
            !enableLineRenderer)
        {
            return;
        }

        trailRenderer.positionCount =
            trailPositions.Count;

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

        if (trailRenderer != null)
        {
            trailRenderer.positionCount = 0;
        }

        // Batch arrays remain allocated for reuse.
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