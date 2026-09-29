using UnityEngine;
using UnityEngine.UI;
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

    [Header("Speed Power-Up")]
    [SerializeField] private Button speedBoostButton;

    [Header("Boundary Power-Up")]
    [SerializeField] private Button boundaryPowerUpButton;



    private Coroutine playerDiedCoroutine;


    [Header("Revive")]
    [Tooltip("Optional container for the whole revive UI. Leave empty to toggle the button and the countdown text individually.")]
    [SerializeField] private GameObject revivePanel;
    [SerializeField] private Button reviveButton;
    [SerializeField] private Text reviveCountdownText;

    private Coroutine reviveCoroutine;
    private System.Action reviveCountdownFinished;

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

        // The revive widgets live inside GamePanel and must stay
        // hidden outside the revive window.
        SetReviveUIVisible(false);
    }

    private void Update()
    {
        UpdatePowerUpButton(
            speedBoostButton,
            gameManager != null && gameManager.IsSpeedBoostLevel(),
            gameManager != null && gameManager.CanUsePlayerSpeedBoost()
        );

        UpdatePowerUpButton(
            boundaryPowerUpButton,
            gameManager != null && gameManager.IsBoundaryPowerUpLevel(),
            gameManager != null && gameManager.CanUsePlayerBoundary()
        );
    }

    private void UpdatePowerUpButton(Button button, bool visible, bool usable)
    {
        if (button == null)
        {
            return;
        }

        if (button.gameObject.activeSelf != visible)
        {
            button.gameObject.SetActive(visible);
        }

        button.interactable = visible && usable;
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
        if (gameManager != null && gameManager.TryActivatePlayerBoundary())
        {
            if (boundaryPowerUpButton != null)
            {
                boundaryPowerUpButton.interactable = false;
            }
        }
    }

    public void ActivatePlayerSpeedBoost()
    {
        if (gameManager == null)
        {
            return;
        }

        if (gameManager.TryActivatePlayerSpeedBoost())
        {
            if (speedBoostButton != null)
            {
                speedBoostButton.interactable = false;
            }
        }
    }

    public bool HasReviveUI => reviveButton != null || revivePanel != null;

    public void StartReviveCountdown(
    int seconds,
    System.Action onCountdownFinished)
    {
        CancelReviveCountdown();

        if (!isActiveAndEnabled || reviveButton == null)
        {
            onCountdownFinished?.Invoke();
            return;
        }

        if (winningPanel != null)
        {
            winningPanel.SetActive(false);
        }

        if (failPanel != null)
        {
            failPanel.SetActive(false);
        }

        if (GamePanel != null)
        {
            GamePanel.SetActive(true);
        }

        reviveCountdownFinished = onCountdownFinished;

        SetReviveUIVisible(true);
        reviveButton.interactable = true;

        reviveCoroutine = StartCoroutine(
            ReviveCountdownCoroutine(Mathf.Max(1, seconds))
        );
    }

    private IEnumerator ReviveCountdownCoroutine(int seconds)
    {
        float deadline =
            Time.realtimeSinceStartup + Mathf.Max(1, seconds);

        int lastDisplayed = -1;

        while (Time.realtimeSinceStartup < deadline)
        {
            int remaining = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    deadline - Time.realtimeSinceStartup
                )
            );

            if (remaining != lastDisplayed)
            {
                SetReviveCountdownText(remaining);
                lastDisplayed = remaining;
            }

            yield return null;
        }

        SetReviveCountdownText(0);

        reviveCoroutine = null;

        System.Action onFinished = reviveCountdownFinished;
        reviveCountdownFinished = null;

        SetReviveUIVisible(false);

        onFinished?.Invoke();
    }

    public void CancelReviveCountdown()
    {
        if (reviveCoroutine != null)
        {
            StopCoroutine(reviveCoroutine);
            reviveCoroutine = null;
        }

        reviveCountdownFinished = null;
        SetReviveUIVisible(false);
    }

    public void OnReviveButtonPressed()
    {
        if (gameManager != null)
        {
            gameManager.RevivePlayer();
        }
    }

    private void SetReviveUIVisible(bool visible)
    {
        if (revivePanel != null)
        {
            revivePanel.SetActive(visible);
        }

        if (reviveButton != null)
        {
            reviveButton.gameObject.SetActive(visible);
        }

        if (reviveCountdownText != null)
        {
            reviveCountdownText.gameObject.SetActive(visible);
        }
    }

    private void SetReviveCountdownText(int value)
    {
        if (reviveCountdownText != null)
        {
            reviveCountdownText.text = value.ToString();
        }
    }
}