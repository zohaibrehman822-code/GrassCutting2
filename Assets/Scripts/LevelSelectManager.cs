using UnityEngine;

public class LevelSelectManager : MonoBehaviour
{
    [SerializeField] private GameManager gameManager;

    public void OnLevelSelected(int levelNumber)
    {
        if (gameManager == null)
        {
            Debug.LogError("LevelSelectManager: GameManager reference is missing.", this);
            return;
        }

        gameManager.StartLevel(levelNumber);
    }
}