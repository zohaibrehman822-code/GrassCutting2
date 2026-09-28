using TMPro;
using UnityEngine.UI;
using UnityEngine;

public class CoinDisplay : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Text coinText;
    [SerializeField] private TMP_Text sharedCoinText;
    [Tooltip("Hide the shared coin text while any of these panels is open.")]
    [SerializeField] private GameObject[] hiddenOnPanels;

    [Header("Display")]
    [SerializeField] private string format = "{0}";

    private void Awake()
    {
        if (coinText == null && sharedCoinText == null)
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

    private void LateUpdate()
    {
        if (sharedCoinText == null)
        {
            return;
        }

        bool visible = true;

        if (hiddenOnPanels != null)
        {
            foreach (GameObject panel in hiddenOnPanels)
            {
                if (panel != null && panel.activeInHierarchy)
                {
                    visible = false;
                    break;
                }
            }
        }

        if (sharedCoinText.gameObject.activeSelf != visible)
        {
            sharedCoinText.gameObject.SetActive(visible);
        }
    }

    private void Refresh()
    {
        string text = string.Format(format, CoinManager.Coins);

        if (coinText != null)
        {
            coinText.text = text;
        }

        if (sharedCoinText != null)
        {
            sharedCoinText.text = text;
        }
    }
}
