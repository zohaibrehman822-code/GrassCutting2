using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Prefabs")]

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

    public bool IsLevelRunning => currentLevelInstance != null;

    private void CleanupCurrentLevel()
    {
        if (currentTerritoryManager != null)
        {
            currentTerritoryManager.OnWinningPercentageReached -=
                HandleWinningPercentageReached;
        }

        if (currentPlayerTerritory != null)
        {
            currentPlayerTerritory.OnPlayerDeath -=
                HandlePlayerDeath;
        }

        // Deactivate immediately so the next player's searches
        // cannot find components belonging to the old player.
        if (currentPlayerInstance != null)
        {
            currentPlayerInstance.SetActive(false);
            Destroy(currentPlayerInstance);
            currentPlayerInstance = null;
        }

        // Destroy is deferred, but inactive objects are excluded
        // from the existing default FindFirstObjectByType searches.
        if (currentLevelInstance != null)
        {
            currentLevelInstance.SetActive(false);
            Destroy(currentLevelInstance);
            currentLevelInstance = null;
        }

        currentTerritoryManager = null;
        currentPlayerTerritory = null;
        currentWinCondition = null;
    }

    public void ReturnToMainMenu()
    {
        CleanupCurrentLevel();

        Time.timeScale = 1f;

        if (territoryPercentage != null)
        {
            territoryPercentage.Unbind();
        }

        if (cameraFollow != null)
        {
            cameraFollow.SetTarget(null);
        }

        currentLevelNumber = 0;
    }

    public void StartLevel(int levelNumber)
    {
        int index = levelNumber - 1;

        if (index < 0 ||
            index >= levelPrefabs.Length ||
            levelPrefabs[index] == null)
        {
            Debug.LogError(
                $"GameManager: No level prefab assigned for level {levelNumber}.",
                this
            );
            return;
        }

        PlayerBlade selectedBlade = PlayerBladeSelection.SelectedBlade;

        if (selectedBlade == null)
        {
            Debug.LogError(
                "GameManager: No player blade has been selected.",
                this
            );
            return;
        }

        if (!selectedBlade.IsUnlocked)
        {
            Debug.LogError(
                "GameManager: The selected blade is locked.",
                this
            );
            return;
        }

        if (selectedBlade.PlayerPrefab == null)
        {
            Debug.LogError(
                $"GameManager: No PlayerPrefab assigned to blade '{selectedBlade.BladeName}'.",
                this
            );
            return;
        }

        Time.timeScale = 1f;

        CleanupCurrentLevel();

        currentLevelNumber = levelNumber;

        currentLevelInstance = Instantiate(
            levelPrefabs[index],
            Vector3.zero,
            Quaternion.identity
        );

        TerritoryManager territoryManager =
            currentLevelInstance
                .GetComponentInChildren<TerritoryManager>();

        if (territoryManager == null)
        {
            Debug.LogError(
                "GameManager: Instantiated level has no TerritoryManager.",
                currentLevelInstance
            );
            return;
        }

        currentWinCondition =
            currentLevelInstance
                .GetComponentInChildren<LevelWinCondition>();

        Transform spawnPoint = territoryManager.PlayerSpawnPoint;

        Vector3 spawnPosition =
            spawnPoint != null
                ? spawnPoint.position
                : new Vector3(0f, 0.64f, 0f);

        Quaternion spawnRotation =
            spawnPoint != null
                ? spawnPoint.rotation
                : Quaternion.identity;

        // Always spawn the prefab belonging to the selected PlayerBlade.
        currentPlayerInstance = Instantiate(
            selectedBlade.PlayerPrefab,
            spawnPosition,
            spawnRotation
        );

        Movement movement =
            currentPlayerInstance
                .GetComponentInChildren<Movement>();

        if (movement != null)
        {
            movement.ApplyBladeStats(selectedBlade);
        }

        currentPlayerTerritory =
            currentPlayerInstance
                .GetComponentInChildren<PaperPlayerTerritory>();

        if (currentPlayerTerritory != null)
        {
            currentPlayerTerritory.OnPlayerDeath +=
                HandlePlayerDeath;
        }

        if (cameraFollow != null)
        {
            cameraFollow.SetTarget(
                currentPlayerInstance.transform
            );
        }

        if (territoryPercentage != null)
        {
            territoryPercentage.Bind(territoryManager);
        }

        currentTerritoryManager = territoryManager;

        currentTerritoryManager.OnWinningPercentageReached +=
            HandleWinningPercentageReached;
    }

    private void HandleWinningPercentageReached()
    {
        LevelProgress.UnlockLevel(currentLevelNumber + 1);

        if (currentWinCondition != null)
        {
            CoinManager.AddCoins(currentWinCondition.CoinReward);
        }

        SetPlayerMovementEnabled(false);

        if (uiManager != null)
        {
            uiManager.ActiveWinningPanel();
        }
    }

    private void SetPlayerMovementEnabled(bool value)
    {
        if (currentPlayerInstance == null)
        {
            return;
        }

        foreach (Movement movement in
                 currentPlayerInstance.GetComponentsInChildren<Movement>(true))
        {
            movement.SetCanMove(value);
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
        SetPlayerMovementEnabled(false);

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