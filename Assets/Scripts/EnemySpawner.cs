using System.Collections.Generic;
using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [System.Serializable]
    public struct EnemySpawnPoint
    {
        public GameObject enemyPrefab;
        public Transform spawnPoint;
    }

    [Header("Enemies To Spawn")]
    [SerializeField] private EnemySpawnPoint[] enemies;

    private readonly List<GameObject> spawnedEnemies = new List<GameObject>();

    private void Start()
    {
        SpawnAll();
    }

    public void SpawnAll()
    {
        if (enemies == null || enemies.Length == 0) return;

        foreach (EnemySpawnPoint spawnPoint in enemies)
        {
            if (spawnPoint.enemyPrefab == null || spawnPoint.spawnPoint == null)
                continue;

            GameObject enemy = Instantiate(
                spawnPoint.enemyPrefab,
                spawnPoint.spawnPoint.position,
                spawnPoint.spawnPoint.rotation
            );

            spawnedEnemies.Add(enemy);
        }
    }

    public void DespawnAll()
    {
        foreach (GameObject enemy in spawnedEnemies)
        {
            if (enemy != null)
            {
                Destroy(enemy);
            }
        }

        spawnedEnemies.Clear();
    }

    private void OnDrawGizmosSelected()
    {
        if (enemies == null) return;

        Gizmos.color = Color.red;

        foreach (EnemySpawnPoint spawnPoint in enemies)
        {
            if (spawnPoint.spawnPoint == null)
                continue;

            Gizmos.DrawWireSphere(
                spawnPoint.spawnPoint.position,
                0.3f
            );
        }
    }
}
