using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PlayerBladeSelection : MonoBehaviour
{
    [Header("Blades")]
    [SerializeField] private PlayerBlade[] blades;

    [Header("Preview")]
    [SerializeField] private Transform previewSpawnPoint;

    [Header("Panels")]
    [SerializeField] private GameObject selectionPanel;
    [SerializeField] private GameObject levelSelectionPanel;

    [Header("Display")]
    [SerializeField] private TMP_Text bladeNameText;
    [SerializeField] private TMP_Text priceText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text coinsText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button buyButton;

    private bool wasSelectionPanelActive;

    private int currentIndex;
    private GameObject previewInstance;

    private GameManager gameManager;

    // A static reference survives a normal Unity scene transition.
    public static PlayerBlade SelectedBlade { get; private set; }

    private void Awake()
    {
        gameManager = GetComponent<GameManager>();
    }

    private void OnEnable()
    {
        currentIndex = 0;
        ShowCurrentBlade();
    }

    private void OnDisable()
    {
        RemovePreview();
    }

    private void Update()
    {
        if (gameManager == null)
        {
            return;
        }

        if (gameManager.IsLevelRunning)
        {
            wasSelectionPanelActive = false;
            RemovePreview();
            return;
        }

        bool selectionPanelActive =
            selectionPanel != null &&
            selectionPanel.activeInHierarchy;

        bool selectionJustOpened =
            selectionPanelActive &&
            !wasSelectionPanelActive;

        wasSelectionPanelActive = selectionPanelActive;

        if (selectionJustOpened)
        {
            currentIndex = 0;
            ShowCurrentBlade();
            return;
        }

        // Preserve the existing preview on menu screens.
        // Recreate it after returning from gameplay.
        if (previewInstance == null && CanSpawnPreview)
        {
            currentIndex = 0;
            ShowCurrentBlade();
        }
    }

    private bool CanSpawnPreview =>
        previewSpawnPoint != null &&
        GetCurrentBlade() != null &&
        GetCurrentBlade().PlayerPrefab != null;

    public void ShowNextBlade()
    {
        if (blades == null || blades.Length == 0)
        {
            return;
        }

        currentIndex = (currentIndex + 1) % blades.Length;
        ShowCurrentBlade();
    }

    public void ShowPreviousBlade()
    {
        if (blades == null || blades.Length == 0)
        {
            return;
        }

        currentIndex =
            (currentIndex - 1 + blades.Length) % blades.Length;

        ShowCurrentBlade();
    }

    public void BuyCurrentBlade()
    {
        PlayerBlade blade = GetCurrentBlade();

        if (blade == null || blade.PlayerPrefab == null)
        {
            return;
        }

        if (!blade.TryUnlock())
        {
            if (statusText != null)
            {
                statusText.text = "Not enough coins";
            }

            return;
        }

        RefreshDisplay();
    }

    public void ConfirmBladeAndOpenLevels()
    {
        PlayerBlade blade = GetCurrentBlade();

        if (blade == null ||
            blade.PlayerPrefab == null ||
            !blade.IsUnlocked)
        {
            if (statusText != null)
            {
                statusText.text = "Unlock this blade first";
            }

            return;
        }

        SelectedBlade = blade;

        if (selectionPanel != null)
        {
            selectionPanel.SetActive(false);
        }

        if (levelSelectionPanel != null)
        {
            levelSelectionPanel.SetActive(true);
        }
    }

    private PlayerBlade GetCurrentBlade()
    {
        if (blades == null ||
            blades.Length == 0 ||
            currentIndex < 0 ||
            currentIndex >= blades.Length)
        {
            return null;
        }

        return blades[currentIndex];
    }

    private void ShowCurrentBlade()
    {
        RemovePreview();

        PlayerBlade blade = GetCurrentBlade();

        if (blade != null &&
            blade.PlayerPrefab != null &&
            previewSpawnPoint != null)
        {
            previewInstance = Instantiate(
                blade.PlayerPrefab,
                previewSpawnPoint.position,
                previewSpawnPoint.rotation
            );

            // The preview uses the real prefab visually, but must not
            // move, cut grass, capture territory, or affect grass shaders.
            foreach (Movement movement in
                     previewInstance.GetComponentsInChildren<Movement>(true))
            {
                movement.enabled = false;
            }

            foreach (GrassCutter cutter in
                     previewInstance.GetComponentsInChildren<GrassCutter>(true))
            {
                cutter.enabled = false;
            }

            foreach (PaperPlayerTerritory territory in
                     previewInstance.GetComponentsInChildren<PaperPlayerTerritory>(true))
            {
                territory.enabled = false;
            }

            foreach (GrassPlayerInteraction interaction in
                     previewInstance.GetComponentsInChildren<GrassPlayerInteraction>(true))
            {
                interaction.enabled = false;
            }

            foreach (Collider collider in
                     previewInstance.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }

            foreach (Rigidbody body in
                     previewInstance.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
            }
        }

        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        PlayerBlade blade = GetCurrentBlade();
        bool valid = blade != null && blade.PlayerPrefab != null;
        bool unlocked = valid && blade.IsUnlocked;

        if (bladeNameText != null)
        {
            bladeNameText.text = valid
                ? blade.BladeName
                : "No blade assigned";
        }

        if (priceText != null)
        {
            priceText.text = valid && !unlocked
                ? $"Price: {blade.Price}"
                : "";
        }

        if (statusText != null)
        {
            statusText.text = valid
                ? (unlocked ? "Unlocked" : "Locked")
                : "Assign a PlayerBlade asset";
        }

        if (coinsText != null)
        {
            coinsText.text = $"Coins: {CoinManager.Coins}";
        }

        if (confirmButton != null)
        {
            confirmButton.interactable = unlocked;
        }

        if (buyButton != null)
        {
            buyButton.interactable =
                valid &&
                !unlocked &&
                CoinManager.Coins >= blade.Price;
        }
    }

    public void RemovePreview()
    {
        if (previewInstance == null)
        {
            return;
        }

        // Hide immediately; Destroy completes at the end of the frame.
        previewInstance.SetActive(false);
        Destroy(previewInstance);
        previewInstance = null;
    }
}