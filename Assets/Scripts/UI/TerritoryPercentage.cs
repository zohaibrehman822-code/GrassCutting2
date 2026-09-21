using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TerritoryPercentage : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Text percentageText;
    [SerializeField] private Image fillImage;

    [Header("Display")]
    [SerializeField] private string format = "{0}%";

    private TerritoryManager territoryManager;
    private int lastPercentage = -1;

    private void Awake()
    {
        if (percentageText == null)
        {
            percentageText = GetComponentInChildren<Text>(true);
        }

        Debug.Log($"[TerritoryPercentage] Awake. percentageText assigned: {percentageText != null}");
        Debug.Log($"[TerritoryPercentage] Fill Image assigned: {fillImage != null}");
    }

    private void OnDisable()
    {
        Unbind();
    }

    public void Bind(TerritoryManager manager)
    {
        Debug.Log($"[TerritoryPercentage] Bind called with manager: {manager}");

        Unbind();

        territoryManager = manager;
        lastPercentage = -1;

        if (territoryManager != null)
        {
            territoryManager.OnInitialized += HandleTerritoryInitialized;
        }
        else
        {
            Debug.LogWarning("[TerritoryPercentage] Bind called with a null TerritoryManager!");
        }
    }

    public void Unbind()
    {
        if (territoryManager != null)
        {
            territoryManager.OnInitialized -= HandleTerritoryInitialized;
        }

        territoryManager = null;
    }

    private void HandleTerritoryInitialized(TerritoryManager manager)
    {
        Debug.Log("[TerritoryPercentage] HandleTerritoryInitialized fired.");

        lastPercentage = -1;
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

        if (percentage == lastPercentage)
        {
            return;
        }

        lastPercentage = percentage;

        // Update percentage text
        if (percentageText != null)
        {
            percentageText.text = string.Format(format, percentage);
        }

        // Update fill image
        if (fillImage != null)
        {
            fillImage.fillAmount = percentage / 100f;
        }

      
    }
}