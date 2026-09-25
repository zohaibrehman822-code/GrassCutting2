using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Panels")]
    [SerializeField] private GameObject winningPanel;
    [SerializeField] private GameObject failPanel;
    [SerializeField] private GameObject GamePanel;

    [Header("References")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private Text PlayerDiedText;

    private Coroutine playerDiedCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (PlayerDiedText != null)
        {
            PlayerDiedText.gameObject.SetActive(false);
        }
    }

    public void ActiveWinningPanel()
    {
        if (winningPanel != null)
        {
            winningPanel.SetActive(true);

            if (GamePanel != null)
            {
                GamePanel.SetActive(false);
            }
        }
    }

    public void ActiveFailPanel()
    {
        if (failPanel != null)
        {
            failPanel.SetActive(true);

            if (GamePanel != null)
            {
                GamePanel.SetActive(false);
            }
        }
    }

    public void ShowPlayerDiedText()
    {
        if (PlayerDiedText == null)
            return;

        if (playerDiedCoroutine != null)
        {
            StopCoroutine(playerDiedCoroutine);
        }

        playerDiedCoroutine = StartCoroutine(ShowPlayerDiedTextCoroutine());
    }

    private IEnumerator ShowPlayerDiedTextCoroutine()
    {
        Debug.Log(" ** UI ** ");

        PlayerDiedText.gameObject.SetActive(true);

        yield return new WaitForSeconds(2.5f);

        PlayerDiedText.gameObject.SetActive(false);

        playerDiedCoroutine = null;
    }

    public void OnRestartButtonPressed()
    {
        if (failPanel != null)
        {
            failPanel.SetActive(false);
        }

        if (gameManager != null)
        {
            gameManager.RestartCurrentLevel();
        }

        if (GamePanel != null)
        {
            GamePanel.SetActive(true);
        }
    }

    public void ActivatePlayerBoundary()
    {
        Debug.Log("Boundary Function Called");

        PlayerTerritoryBoundary boundary =
            FindObjectOfType<PlayerTerritoryBoundary>();

        if (boundary == null)
        {
            Debug.LogError("PlayerTerritoryBoundary not found on the active Level.");
            return;
        }

        boundary.ActivateBoundary();

        Debug.Log("Player Boundary Activated");
    }
}