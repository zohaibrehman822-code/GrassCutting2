using UnityEngine;

public class UIManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject winningPanel;
    [SerializeField] private GameObject GamePanel;

    public void ActiveWinningPanel()
    {
        if (winningPanel != null)
        {
            winningPanel.SetActive(true);
            GamePanel.SetActive(false);
        }
    }
}