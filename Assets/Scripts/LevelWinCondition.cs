using UnityEngine;

public class LevelWinCondition : MonoBehaviour
{
    [Header("Win Settings")]
    [Range(1f, 100f)]
    [SerializeField] private float winningPercentage = 50f;

    [Header("Reward")]
    [Min(0)]
    [SerializeField] private int coinReward = 2;

    public float WinningPercentage => winningPercentage;
    public int CoinReward => coinReward;
}