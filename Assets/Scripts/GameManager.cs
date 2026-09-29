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

    [Header("Revive")]
    [Tooltip("Starting value of the revive countdown (5 -> 0).")]
    [SerializeField, Min(1)] private int reviveCountdownSeconds = 5;
    [Tooltip("Revives offered per level attempt. 0 disables the revive flow.")]
    [SerializeField, Min(0)] private int maxRevivesPerAttempt = 1;

    private int revivesRemaining;
    private bool reviveCountdownActive;

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
        reviveCountdownActive = false;
        revivesRemaining = 0;

        if (uiManager != null)
        {
            uiManager.CancelReviveCountdown();
        }

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

        if (currentPlayerInstance != null)
        {
            currentPlayerInstance.SetActive(false);
            Destroy(currentPlayerInstance);
            currentPlayerInstance = null;
        }

        if (currentLevelInstance != null)
        {
            currentLevelInstance.SetActive(false);

            foreach (EnemySpawner levelSpawner in
                     currentLevelInstance
                         .GetComponentsInChildren<EnemySpawner>(true))
            {
                levelSpawner.DespawnAll();
            }

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

        revivesRemaining = maxRevivesPerAttempt;
        reviveCountdownActive = false;

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
                : new Vector3(0f, 0.17f, 0f);

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
        reviveCountdownActive = false;

        if (uiManager != null)
        {
            uiManager.CancelReviveCountdown();
        }

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
    }

    private void HandlePlayerDeath()
    {
        currentLevelEnded = true;
        SetPlayerMovementEnabled(false);

        // Pause the revive offer instead of failing immediately.
        if (revivesRemaining > 0 &&
            uiManager != null &&
            uiManager.HasReviveUI)
        {
            revivesRemaining--;
            reviveCountdownActive = true;

            uiManager.StartReviveCountdown(
                reviveCountdownSeconds,
                HandleReviveCountdownFinished
            );
            return;
        }

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

    private void HandleReviveCountdownFinished()
    {
        if (!reviveCountdownActive ||
            !currentLevelEnded ||
            currentLevelInstance == null)
        {
            return;
        }

        reviveCountdownActive = false;

        if (uiManager != null)
        {
            uiManager.ActiveFailPanel();
        }
    }

    public void RevivePlayer()
    {
        if (!reviveCountdownActive ||
            !currentLevelEnded ||
            currentLevelInstance == null ||
            currentPlayerInstance == null ||
            !currentPlayerInstance.activeInHierarchy)
        {
            return;
        }

        bool revived = RespawnPlayerAfterRevive();

        reviveCountdownActive = false;

        if (uiManager != null)
        {
            uiManager.CancelReviveCountdown();
        }

        if (!revived)
        {
            Time.timeScale = 0f;

            if (uiManager != null)
            {
                uiManager.ActiveFailPanel();
            }

            return;
        }

        currentLevelEnded = false;

        SetPlayerMovementEnabled(true);

        Time.timeScale = 1f;
    }

    private bool RespawnPlayerAfterRevive()
    {
        if (currentTerritoryManager == null ||
            !currentTerritoryManager.IsInitialized ||
            currentPlayerInstance == null ||
            currentPlayerTerritory == null)
        {
            return false;
        }

        Movement movement =
            currentPlayerInstance.GetComponentInChildren<Movement>();

        if (movement == null || !movement.isActiveAndEnabled)
        {
            return false;
        }

        Rigidbody body = movement.GetComponent<Rigidbody>();

        if (body == null)
        {
            return false;
        }

        Transform spawnPoint =
            currentTerritoryManager.PlayerSpawnPoint;

        Vector3 spawnPosition = spawnPoint != null
            ? spawnPoint.position
            : new Vector3(0f, 0.17f, 0f);

        Quaternion spawnRotation = spawnPoint != null
            ? spawnPoint.rotation
            : Quaternion.identity;

        // The original spawn location may have been captured by an enemy.
        if (!currentTerritoryManager.IsInsideTerritory(spawnPosition))
        {
            bool found = false;
            float nearestDistanceSqr = float.PositiveInfinity;
            Vector3 nearestPosition = spawnPosition;

            foreach (Vector2Int cell in
                     currentTerritoryManager.OwnedCells)
            {
                Vector3 position =
                    currentTerritoryManager.CellToWorld(cell);

                float dx = position.x - spawnPosition.x;
                float dz = position.z - spawnPosition.z;
                float distanceSqr = dx * dx + dz * dz;

                if (distanceSqr >= nearestDistanceSqr)
                {
                    continue;
                }

                nearestDistanceSqr = distanceSqr;
                nearestPosition = position;
                found = true;
            }

            if (!found)
            {
                Debug.LogWarning(
                    "Revive failed: the player has no remaining territory.",
                    this
                );

                return false;
            }

            spawnPosition.x = nearestPosition.x;
            spawnPosition.z = nearestPosition.z;
        }

        movement.SetCanMove(false);

        Transform playerRoot = currentPlayerInstance.transform;

        Vector3 bodyLocalPosition =
            playerRoot.InverseTransformPoint(body.position);

        Quaternion bodyLocalRotation =
            Quaternion.Inverse(playerRoot.rotation) * body.rotation;

        playerRoot.SetPositionAndRotation(
            spawnPosition,
            spawnRotation
        );

        body.position =
            playerRoot.TransformPoint(bodyLocalPosition);

        body.rotation =
            playerRoot.rotation * bodyLocalRotation;

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        movement.SetGroundHeight(body.position.y);

        Physics.SyncTransforms();

        // Reuse the same player so power-up usage and timers are preserved.
        // OnPlayerDied() already cleared and cancelled its active trail.
        currentPlayerTerritory.enabled = true;

        if (cameraFollow != null)
        {
            cameraFollow.SetTarget(playerRoot);
        }

        return true;
    }
}
