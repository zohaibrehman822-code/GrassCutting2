using UnityEngine;
using UnityEngine.UI;

public class LevelButton : MonoBehaviour
{
    [Header("Level Info")]
    [Min(1)]
    [SerializeField] private int levelNumber = 1;

    [Header("References")]
    [SerializeField] private Button button;
    [SerializeField] private GameObject lockedImage;

    [Header("Callback")]
    [SerializeField] private LevelSelectManager levelSelectManager;

    private void Awake()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        button.onClick.AddListener(HandleClick);
    }

    private void OnEnable()
    {
        Refresh();
    }

    public void Refresh()
    {
        bool unlocked = LevelProgress.IsLevelUnlocked(levelNumber);

        button.interactable = unlocked;

        if (lockedImage != null)
        {
            lockedImage.SetActive(!unlocked);
        }
    }

    private void HandleClick()
    {
        if (!LevelProgress.IsLevelUnlocked(levelNumber)) return;

        if (levelSelectManager != null)
        {
            levelSelectManager.OnLevelSelected(levelNumber);
        }
    }
}