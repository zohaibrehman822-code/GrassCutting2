using UnityEngine;
using System;

public static class CoinManager
{
    private const string CoinsKey = "PlayerCoins";
    private const int StartingCoins = 100;

    public static event Action<int> OnCoinsChanged;

    public static int Coins
    {
        get => PlayerPrefs.GetInt(CoinsKey, StartingCoins);
        private set
        {
            PlayerPrefs.SetInt(CoinsKey, value);
            PlayerPrefs.Save();
            OnCoinsChanged?.Invoke(value);
        }
    }

    public static void AddCoins(int amount)
    {
        if (amount <= 0) return;
        Coins += amount;
    }

    public static bool SpendCoins(int amount)
    {
        if (amount <= 0 || Coins < amount) return false;
        Coins -= amount;
        return true;
    }

    // Handy for testing
    public static void ResetCoins()
    {
        PlayerPrefs.DeleteKey(CoinsKey);
    }
}