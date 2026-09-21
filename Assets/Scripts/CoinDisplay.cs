using TMPro;
using UnityEngine.UI;
using UnityEngine;

public class CoinDisplay : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Text coinText;

    [Header("Display")]
    [SerializeField] private string format = "{0}";

    private void Awake()
    {
        if (coinText == null)
        {
            coinText = GetComponentInChildren<Text>(true);
        }
    }

    private void OnEnable()
    {
        CoinManager.OnCoinsChanged += HandleCoinsChanged;
        Refresh();
    }

    private void OnDisable()
    {
        CoinManager.OnCoinsChanged -= HandleCoinsChanged;
    }

    private void HandleCoinsChanged(int newAmount)
    {
        Refresh();
    }

    private void Refresh()
    {
        if (coinText == null) return;
        coinText.text = string.Format(format, CoinManager.Coins);
    }
}