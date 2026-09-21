using TMPro;
using UnityEngine;

public class TerritoryPercentageDisplay : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TerritoryManager territoryManager;
    [SerializeField] private TextMeshProUGUI percentageText;

    [Header("Display")]
    [SerializeField] private string format = "{0}%";

    private int lastPercentage = -1;

    private void Awake()
    {
        if (territoryManager == null)
        {
            territoryManager =
                FindFirstObjectByType<TerritoryManager>();
        }

        if (percentageText == null)
        {
            percentageText =
                GetComponentInChildren<TextMeshProUGUI>();
        }
    }

    private void Update()
    {
        if (territoryManager == null ||
            percentageText == null)
        {
            return;
        }

        int totalCells =
            territoryManager.TotalCells;

        if (totalCells <= 0)
        {
            return;
        }

        int ownedCount =
            territoryManager.OwnedCells.Count;

        int percentage =
            Mathf.CeilToInt(
                ownedCount * 100f / totalCells
            );

        if (percentage == lastPercentage)
        {
            return;
        }

        lastPercentage = percentage;

        percentageText.text =
            string.Format(format, percentage);
    }
}
