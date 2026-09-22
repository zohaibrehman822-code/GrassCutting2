using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TerritoryPercentage : MonoBehaviour
{
    [Header("Player Display")]
    [SerializeField] private Text percentageText;
    [SerializeField] private Image fillImage;

    [Header("Enemy Display")]
    [SerializeField] private Transform enemyListContainer;
    [SerializeField] private GameObject enemyRowPrefab;
    [SerializeField] private string enemyFormat = "{0}: {1}%";

    [Header("Display")]
    [SerializeField] private string format = "{0}%";

    private TerritoryManager territoryManager;
    private int lastPercentage = -1;

    private readonly Dictionary<EnemyAI, Text> enemyRows = new Dictionary<EnemyAI, Text>();
    private readonly List<EnemyAI> staleKeys = new List<EnemyAI>();

    private void Awake()
    {
        if (percentageText == null)
        {
            percentageText = GetComponentInChildren<Text>(true);
        }
    }

    private void OnDisable()
    {
        Unbind();
    }

    public void Bind(TerritoryManager manager)
    {
        Unbind();

        territoryManager = manager;
        lastPercentage = -1;

        if (territoryManager != null)
        {
            territoryManager.OnInitialized += HandleTerritoryInitialized;
        }
    }

    public void Unbind()
    {
        if (territoryManager != null)
        {
            territoryManager.OnInitialized -= HandleTerritoryInitialized;
        }

        territoryManager = null;
        ClearEnemyRows();
    }

    private void HandleTerritoryInitialized(TerritoryManager manager)
    {
        lastPercentage = -1;
        ClearEnemyRows();
        RefreshDisplay();
    }

    private void Update()
    {
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        if (territoryManager == null)
        {
            return;
        }

        int totalCells = territoryManager.TotalCells;

        if (totalCells <= 0)
        {
            return;
        }

        int ownedCount = territoryManager.OwnedCells.Count;

        int percentage = Mathf.CeilToInt(
            ownedCount * 100f / totalCells
        );

        if (percentage != lastPercentage)
        {
            lastPercentage = percentage;

            if (percentageText != null)
            {
                percentageText.text = string.Format(format, percentage);
            }

            if (fillImage != null)
            {
                fillImage.fillAmount = percentage / 100f;
            }
        }

        RefreshEnemyRows(totalCells);
    }

    private void RefreshEnemyRows(int totalCells)
    {
        if (enemyListContainer == null || enemyRowPrefab == null)
        {
            return;
        }

        EnemySpawner spawner = territoryManager.EnemySpawner;

        if (spawner == null)
        {
            return;
        }

        IReadOnlyList<GameObject> spawnedEnemies = spawner.SpawnedEnemies;

        staleKeys.Clear();
        foreach (var key in enemyRows.Keys)
        {
            staleKeys.Add(key);
        }

        for (int i = 0; i < spawnedEnemies.Count; i++)
        {
            if (spawnedEnemies[i] == null) continue;

            EnemyAI enemyAI = spawnedEnemies[i].GetComponent<EnemyAI>();
            if (enemyAI == null || !enemyAI.IsAlive) continue;

            staleKeys.Remove(enemyAI);

            int enemyCells = territoryManager.GetEnemyTerritoryCount(enemyAI);
            int enemyPercentage = Mathf.CeilToInt(enemyCells * 100f / totalCells);

            if (!enemyRows.TryGetValue(enemyAI, out Text rowText))
            {
                GameObject row = Instantiate(enemyRowPrefab, enemyListContainer);
                rowText = row.GetComponentInChildren<Text>(true);
                enemyRows[enemyAI] = rowText;
            }

            if (rowText != null)
            {
                rowText.text = string.Format(enemyFormat, spawnedEnemies[i].name, enemyPercentage);
            }
        }

        // Remove rows for enemies that died or were destroyed.
        for (int i = 0; i < staleKeys.Count; i++)
        {
            if (enemyRows.TryGetValue(staleKeys[i], out Text rowText) && rowText != null)
            {
                Destroy(rowText.transform.parent != null ? rowText.transform.parent.gameObject : rowText.gameObject);
            }

            enemyRows.Remove(staleKeys[i]);
        }
    }

    private void ClearEnemyRows()
    {
        foreach (var kvp in enemyRows)
        {
            if (kvp.Value != null)
            {
                GameObject rowObject = kvp.Value.transform.parent != null
                    ? kvp.Value.transform.parent.gameObject
                    : kvp.Value.gameObject;

                Destroy(rowObject);
            }
        }

        enemyRows.Clear();
    }
}