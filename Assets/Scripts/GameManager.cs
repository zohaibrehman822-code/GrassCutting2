using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject[] levelPrefabs; // index 0 = Level 1, index 1 = Level 2, etc.

    [Header("Camera")]
    [SerializeField] private CameraFollow cameraFollow;

    [Header("UI")]
    [SerializeField] private TerritoryPercentage territoryPercentage;
    [SerializeField] private UIManager uiManager;

    private TerritoryManager currentTerritoryManager;
    private GameObject currentLevelInstance;
    private GameObject currentPlayerInstance;
    private int currentLevelNumber;

    private LevelWinCondition currentWinCondition;

    private PaperPlayerTerritory currentPlayerTerritory;

    public void StartLevel(int levelNumber)
    {
        Time.timeScale = 1f;

        int index = levelNumber - 1;

        if (index < 0 || index >= levelPrefabs.Length || levelPrefabs[index] == null)
        {
            Debug.LogError($"GameManager: No level prefab assigned for level {levelNumber}.", this);
            return;
        }

        // Unsubscribe from the previous level's manager and player, if any
        if (currentTerritoryManager != null)
        {
            currentTerritoryManager.OnWinningPercentageReached -= HandleWinningPercentageReached;
        }

        if (currentPlayerTerritory != null)
        {
            currentPlayerTerritory.OnPlayerDeath -= HandlePlayerDeath;
        }

        // Clean up the previously loaded level and player, if any
        if (currentLevelInstance != null)
        {
            Destroy(currentLevelInstance);
            currentLevelInstance = null;
        }

        if (currentPlayerInstance != null)
        {
            Destroy(currentPlayerInstance);
            currentPlayerInstance = null;
        }

        currentTerritoryManager = null;
        currentPlayerTerritory = null;
        currentLevelNumber = levelNumber;

        // Instantiate Level at (0, 0, 0)
        currentLevelInstance = Instantiate(
            levelPrefabs[index],
            Vector3.zero,
            Quaternion.identity
        );

        TerritoryManager territoryManager = currentLevelInstance.GetComponentInChildren<TerritoryManager>();

        if (territoryManager == null)
        {
            Debug.LogError("GameManager: Instantiated level has no TerritoryManager.", currentLevelInstance);
        }

        currentWinCondition = currentLevelInstance.GetComponentInChildren<LevelWinCondition>();

        // Instantiate Player at (0, 0.495, 0)
        currentPlayerInstance = Instantiate(
            playerPrefab,
            new Vector3(0f, 0.679f, 0f),
            Quaternion.identity
        );

        currentPlayerTerritory = currentPlayerInstance.GetComponentInChildren<PaperPlayerTerritory>();

        if (currentPlayerTerritory != null)
        {
            currentPlayerTerritory.OnPlayerDeath += HandlePlayerDeath;
        }

        // Assign the newly spawned Player to the camera
        cameraFollow.SetTarget(currentPlayerInstance.transform);

        // Bind the percentage UI to this level's TerritoryManager
        if (territoryPercentage != null)
        {
            territoryPercentage.Bind(territoryManager);
        }

        currentTerritoryManager = territoryManager;

        if (currentTerritoryManager != null)
        {
            currentTerritoryManager.OnWinningPercentageReached += HandleWinningPercentageReached;
        }
    }

    private void HandleWinningPercentageReached()
    {
        LevelProgress.UnlockLevel(currentLevelNumber + 1);

        if (currentWinCondition != null)
        {
            CoinManager.AddCoins(currentWinCondition.CoinReward);
        }

        if (uiManager != null)
        {
            uiManager.ActiveWinningPanel();
        }
    }

    private void OnDestroy()
    {
        if (currentTerritoryManager != null)
        {
            currentTerritoryManager.OnWinningPercentageReached -= HandleWinningPercentageReached;
        }

        if (currentPlayerTerritory != null)
        {
            currentPlayerTerritory.OnPlayerDeath -= HandlePlayerDeath;
        }
    }

    private void HandlePlayerDeath()
    {
        if (uiManager != null)
        {
            uiManager.ActiveFailPanel();
        }
    }

    public void RestartCurrentLevel()
    {
        StartLevel(currentLevelNumber);
    }
}