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

    [Header("Power-Up Availability")]
    [Tooltip("Level numbers where the boundary-wall power-up is available.")]
    [SerializeField] private int[] boundaryPowerUpLevels = { 3 };
    [Tooltip("Level numbers where the paid speed power-up is available.")]
    [SerializeField] private int[] speedBoostLevels = { 4 };

    private TerritoryManager currentTerritoryManager;
    private GameObject currentLevelInstance;
    private GameObject currentPlayerInstance;
    private int currentLevelNumber;

    private LevelWinCondition currentWinCondition;

    private PaperPlayerTerritory currentPlayerTerritory;
    private PlayerTerritoryBoundary currentPlayerBoundary;
    private bool boundaryPowerUpUsed;
    private bool boundaryActivationInProgress;
    private bool currentLevelEnded;

    public bool IsLevelRunning => currentLevelInstance != null;

    private const int SpeedBoostCost = 100;

    private void CleanupCurrentLevel()
    {
        if (currentPlayerBoundary != null)
        {
            currentPlayerBoundary.DeactivateBoundary();
        }

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
        currentPlayerBoundary = null;
        boundaryPowerUpUsed = false;
        boundaryActivationInProgress = false;
        currentLevelEnded = false;
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

        ConfigureBoundaryPowerUp(territoryManager);

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
        currentLevelEnded = true;
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
        if (currentPlayerBoundary != null)
        {
            currentPlayerBoundary.DeactivateBoundary();
        }

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
        currentLevelEnded = true;
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

    public bool IsSpeedBoostLevel()
    {
        return IsPowerUpEnabledForCurrentLevel(speedBoostLevels) &&
               currentLevelInstance != null &&
               currentPlayerInstance != null &&
               currentPlayerInstance.activeInHierarchy;
    }

    public bool CanUsePlayerSpeedBoost()
    {
        if (currentLevelEnded || !IsSpeedBoostLevel() ||
            CoinManager.Coins < SpeedBoostCost)
        {
            return false;
        }

        if (currentPlayerTerritory != null &&
            !currentPlayerTerritory.isActiveAndEnabled)
        {
            return false;
        }

        Movement movement =
            currentPlayerInstance.GetComponentInChildren<Movement>();

        return movement != null && movement.CanUseSpeedBoost();
    }

    public bool TryActivatePlayerSpeedBoost()
    {
        if (!CanUsePlayerSpeedBoost())
        {
            return false;
        }

        Movement movement =
            currentPlayerInstance.GetComponentInChildren<Movement>();

        if (movement == null)
        {
            return false;
        }

        if (!CoinManager.SpendCoins(SpeedBoostCost))
        {
            return false;
        }

        if (movement != null && movement.TryActivateSpeedBoost())
        {
            return true;
        }

        // Do not charge for an unsuccessful activation.
        CoinManager.AddCoins(SpeedBoostCost);
        return false;
    }

    private bool IsPowerUpEnabledForCurrentLevel(int[] enabledLevels)
    {
        if (enabledLevels == null || currentLevelNumber <= 0)
        {
            return false;
        }

        foreach (int levelNumber in enabledLevels)
        {
            if (levelNumber == currentLevelNumber)
            {
                return true;
            }
        }

        return false;
    }

    private void ConfigureBoundaryPowerUp(TerritoryManager manager)
    {
        currentPlayerBoundary = currentLevelInstance
            .GetComponentInChildren<PlayerTerritoryBoundary>(true);

        // Keep the existing Level 3 appearance/settings. Future enabled
        // levels receive a boundary component automatically if needed.
        if (currentPlayerBoundary == null &&
            IsPowerUpEnabledForCurrentLevel(boundaryPowerUpLevels))
        {
            currentPlayerBoundary = currentLevelInstance
                .AddComponent<PlayerTerritoryBoundary>();
        }

        if (currentPlayerBoundary != null)
        {
            currentPlayerBoundary.Initialize(manager, currentPlayerTerritory);
        }
    }

    public bool IsBoundaryPowerUpLevel()
    {
        return IsPowerUpEnabledForCurrentLevel(boundaryPowerUpLevels) &&
               currentLevelInstance != null &&
               currentPlayerInstance != null &&
               currentPlayerInstance.activeInHierarchy;
    }

    public bool CanUsePlayerBoundary()
    {
        const int wallCost = 80;

        return IsBoundaryPowerUpLevel() &&
               !currentLevelEnded &&
               !boundaryPowerUpUsed &&
               !boundaryActivationInProgress &&
               Time.timeScale > 0f &&
               CoinManager.Coins >= wallCost &&
               currentTerritoryManager != null &&
               currentTerritoryManager.IsInitialized &&
               currentPlayerTerritory != null &&
               currentPlayerTerritory.isActiveAndEnabled &&
               currentPlayerBoundary != null &&
               currentPlayerBoundary.isActiveAndEnabled &&
               !currentPlayerBoundary.IsActive;
    }

    public bool TryActivatePlayerBoundary()
    {
        const int wallCost = 80;

        if (!CanUsePlayerBoundary())
        {
            return false;
        }

        boundaryActivationInProgress = true;

        bool coinsSpent = false;
        bool activated = false;

        try
        {
            coinsSpent = CoinManager.SpendCoins(wallCost);

            if (!coinsSpent)
            {
                return false;
            }

            if (currentPlayerBoundary == null ||
                !currentPlayerBoundary.isActiveAndEnabled)
            {
                return false;
            }

            currentPlayerBoundary.ActivateBoundary();

            activated = currentPlayerBoundary != null &&
                        currentPlayerBoundary.IsActive;

            boundaryPowerUpUsed = activated;

            return activated;
        }
        finally
        {
            if (coinsSpent && !activated)
            {
                CoinManager.AddCoins(wallCost);
            }

            boundaryActivationInProgress = false;
        }
    }
}
