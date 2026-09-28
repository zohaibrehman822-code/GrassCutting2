using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using static UnityEngine.UIElements.UxmlAttributeDescription;

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

    public IReadOnlyList<GameObject> SpawnedEnemies => spawnedEnemies;

    public int RemainingEnemyCount => spawnedEnemies.Count;

    public void NotifyEnemyDied(GameObject enemy)
    {
        spawnedEnemies.Remove(enemy);
    }

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

            Vector3 spawnPosition = spawnPoint.spawnPoint.position;
            spawnPosition.y = 0.64f;

            GameObject enemy = Instantiate(
                spawnPoint.enemyPrefab,
                spawnPosition,
                spawnPoint.spawnPoint.rotation,
                transform
            );

            spawnedEnemies.Add(enemy);

            EnemyAI enemyAI = enemy.GetComponent<EnemyAI>();
            if (enemyAI != null)
            {
                enemyAI.SetSpawner(this);
            }
        }
    }

    public void DespawnAll()
    {
        // Clear ownership first so destruction callbacks cannot modify the
        // collection being iterated or leave stale entries during a restart.
        GameObject[] enemiesToRemove = spawnedEnemies.ToArray();
        spawnedEnemies.Clear();

        foreach (GameObject enemy in enemiesToRemove)
        {
            if (enemy != null)
            {
                // Hide and stop AI immediately; Destroy finishes this frame.
                enemy.SetActive(false);
                Destroy(enemy);
            }
        }
    }

    private void OnDestroy()
    {
        DespawnAll();
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
