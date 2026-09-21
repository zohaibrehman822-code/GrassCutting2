using UnityEngine;

public static class LevelProgress
{
    private const string UnlockedLevelKey = "UnlockedLevel";

    // Level numbers are 1-based (Level 1, Level 2, Level 3...)
    public static int HighestUnlockedLevel
    {
        get => PlayerPrefs.GetInt(UnlockedLevelKey, 1); // Level 1 unlocked by default
        private set
        {
            PlayerPrefs.SetInt(UnlockedLevelKey, value);
            PlayerPrefs.Save();
        }
    }

    public static bool IsLevelUnlocked(int levelNumber)
    {
        return levelNumber <= HighestUnlockedLevel;
    }

    public static void UnlockLevel(int levelNumber)
    {
        if (levelNumber > HighestUnlockedLevel)
        {
            HighestUnlockedLevel = levelNumber;
        }
    }

    // Handy for testing in the Editor
    public static void ResetProgress()
    {
        PlayerPrefs.DeleteKey(UnlockedLevelKey);
    }
}