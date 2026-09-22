using UnityEngine;

public class UIManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject winningPanel;
    [SerializeField] private GameObject failPanel;
    [SerializeField] private GameObject GamePanel;

    [Header("References")]
    [SerializeField] private GameManager gameManager;

    public void ActiveWinningPanel()
    {
        if (winningPanel != null)
        {
            winningPanel.SetActive(true);
            GamePanel.SetActive(false);
        }
    }

    public void ActiveFailPanel()
    {
        if (failPanel != null)
        {
            failPanel.SetActive(true);
            GamePanel.SetActive(false);
        }
    }

    // Wire this to the Fail Panel's Restart button (OnClick).
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
    }
}