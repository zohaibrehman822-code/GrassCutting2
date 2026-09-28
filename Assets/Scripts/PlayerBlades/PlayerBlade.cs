using UnityEngine;

[CreateAssetMenu(
    fileName = "PlayerBlade",
    menuName = "Grass Cutting/Player Blade")]
public sealed class PlayerBlade : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string bladeId;
    [SerializeField] private string bladeName;
    [SerializeField] private GameObject playerPrefab;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 1.4f;
    [SerializeField, Min(0.1f)] private float acceleration = 23f;
    [SerializeField, Min(0.1f)] private float braking = 18f;
    [SerializeField, Min(0.1f)] private float turnAcceleration = 8f;

    [Header("Unlock")]
    [SerializeField, Min(0)] private int price;
    [SerializeField] private bool isUnlocked;

    public string BladeId => bladeId;
    public string BladeName =>
        string.IsNullOrWhiteSpace(bladeName) ? name : bladeName;

    public GameObject PlayerPrefab => playerPrefab;
    public float MoveSpeed => moveSpeed;
    public float Acceleration => acceleration;
    public float Braking => braking;
    public float TurnAcceleration => turnAcceleration;
    public int Price => price;

    private string UnlockKey => "PlayerBladeUnlocked_" + bladeId;

    public bool IsUnlocked =>
        isUnlocked ||
        (!string.IsNullOrWhiteSpace(bladeId) &&
         PlayerPrefs.GetInt(UnlockKey, 0) == 1);

    public bool TryUnlock()
    {
        if (IsUnlocked)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(bladeId))
        {
            Debug.LogError(
                $"PlayerBlade '{name}' needs a unique Blade Id.",
                this
            );
            return false;
        }

        if (price > 0 && !CoinManager.SpendCoins(price))
        {
            return false;
        }

        PlayerPrefs.SetInt(UnlockKey, 1);
        PlayerPrefs.Save();
        return true;
    }
}