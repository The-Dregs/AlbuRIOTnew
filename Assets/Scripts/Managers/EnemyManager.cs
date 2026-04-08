using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;
using System.Collections;

public class EnemyManager : MonoBehaviourPun
{
    [Header("Enemy Management")]
    public Transform[] spawnPoints;
    public float spawnRadius = 2f;
    public int maxEnemiesPerSpawn = 3;
    public float spawnCooldown = 5f;
    
    [Header("Enemy Prefabs")]
    public GameObject[] enemyPrefabs;
    
    [Header("Day/Night Spawning")]
    [Tooltip("Only spawn enemies during night phase")]
    public bool respectDayNightCycle = true;
    [Tooltip("Spawn multiplier during night (1.0 = normal, 2.0 = double)")]
    public float nightSpawnMultiplier = 1.5f;
    
    [Header("Debug")]
    public bool showDebugInfo = false;
    public bool enableSpawning = true;
    [SerializeField, Range(0.1f, 5f)] private float debugLogInterval = 1f;
    
    // Runtime data
    private List<BaseEnemyAI> activeEnemies = new List<BaseEnemyAI>();
    private float lastSpawnTime;
    private Dictionary<string, GameObject> enemyPrefabLookup = new Dictionary<string, GameObject>();
    private ChapterGameplayLoop cachedChapterLoop;
    private float nextDebugLogTime;
    
    // Events
    public System.Action<BaseEnemyAI> OnEnemySpawned;
    public System.Action<BaseEnemyAI> OnEnemyDied;
    public System.Action<int> OnEnemyCountChanged;
    
    #region Unity Lifecycle
    
    void Start()
    {
        InitializeEnemyLookup();
        SetupEnemyEvents();
    }
    
    void Update()
    {
        if (showDebugInfo && Time.time >= nextDebugLogTime)
        {
            Debug.Log($"[EnemyManager] Active Enemies: {activeEnemies.Count}");
            nextDebugLogTime = Time.time + Mathf.Max(0.1f, debugLogInterval);
        }
    }
    
    #endregion
    
    #region Initialization
    
    private void InitializeEnemyLookup()
    {
        enemyPrefabLookup.Clear();
        foreach (var prefab in enemyPrefabs)
        {
            if (prefab != null)
            {
                string enemyName = prefab.name.Replace("(Clone)", "").Trim();
                enemyPrefabLookup[enemyName] = prefab;
            }
        }
    }
    
    private void SetupEnemyEvents()
    {
        // Subscriptions are added per-enemy when they spawn
    }
    
    #endregion
    
    #region Public Spawning Methods
    
    public void SpawnEnemy(string enemyName, Vector3 position, Quaternion rotation)
    {
        if (!enableSpawning) return;
        if (!enemyPrefabLookup.ContainsKey(enemyName))
        {
            Debug.LogWarning($"[EnemyManager] Enemy prefab '{enemyName}' not found!");
            return;
        }
        
        GameObject enemyPrefab = enemyPrefabLookup[enemyName];
        SpawnEnemy(enemyPrefab, position, rotation);
    }
    
    public void SpawnEnemy(GameObject enemyPrefab, Vector3 position, Quaternion rotation)
    {
        if (!enableSpawning) return;
        
        if (respectDayNightCycle && DayNightCycleManager.Instance != null)
        {
            if (!DayNightCycleManager.Instance.IsNight())
            {
                if (showDebugInfo)
                {
                    Debug.Log($"[EnemyManager] Cannot spawn {enemyPrefab.name} - it's day time!");
                }
                return;
            }
        }
        
        GameObject enemyInstance = null;
        
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            string resolvedPath = ResolvePhotonEnemyPath(enemyPrefab);
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                if (showDebugInfo)
                {
                    Debug.LogWarning($"[EnemyManager] Could not resolve a Photon Resources path for enemy prefab '{enemyPrefab.name}'.");
                }
                return;
            }

            enemyInstance = PhotonNetwork.Instantiate(resolvedPath, position, rotation);
        }
        else
        {
            enemyInstance = Instantiate(enemyPrefab, position, rotation);
        }
        
        if (enemyInstance != null)
        {
            BaseEnemyAI enemyAI = enemyInstance.GetComponent<BaseEnemyAI>();
            if (enemyAI != null)
            {
                activeEnemies.Add(enemyAI);
                enemyAI.OnEnemyDied += HandleEnemyDied;
                
                if (cachedChapterLoop == null)
                    cachedChapterLoop = FindFirstObjectByType<ChapterGameplayLoop>();
                if (cachedChapterLoop != null)
                {
                    cachedChapterLoop.OnEnemySpawned(enemyAI);
                }
                
                OnEnemySpawned?.Invoke(enemyAI);
                OnEnemyCountChanged?.Invoke(activeEnemies.Count);
                
                if (showDebugInfo)
                {
                    Debug.Log($"[EnemyManager] Spawned {enemyAI.name} at {position}");
                }
            }
        }
    }

    private string ResolvePhotonEnemyPath(GameObject enemyPrefab)
    {
        if (enemyPrefab == null)
            return null;

        string prefabName = enemyPrefab.name?.Trim();
        if (string.IsNullOrWhiteSpace(prefabName))
            return null;

        string[] candidates;
        if (prefabName.StartsWith("Enemies/"))
            candidates = new[] { prefabName, prefabName.Substring("Enemies/".Length) };
        else
            candidates = new[] { prefabName, $"Enemies/{prefabName}" };

        for (int i = 0; i < candidates.Length; i++)
        {
            string candidate = candidates[i];
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            if (Resources.Load<GameObject>(candidate) != null)
                return candidate;
        }

        return null;
    }
    
    public void SpawnEnemyAtRandomPoint(string enemyName)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("[EnemyManager] No spawn points available!");
            return;
        }
        
        Transform spawnPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];
        Vector3 randomOffset = Random.insideUnitSphere * spawnRadius;
        randomOffset.y = 0f; // Keep on ground level
        
        Vector3 spawnPosition = spawnPoint.position + randomOffset;
        SpawnEnemy(enemyName, spawnPosition, Quaternion.identity);
    }
    
    public void SpawnMultipleEnemies(string enemyName, int count)
    {
        StartCoroutine(SpawnMultipleEnemiesCoroutine(enemyName, count));
    }
    
    private IEnumerator SpawnMultipleEnemiesCoroutine(string enemyName, int count)
    {
        for (int i = 0; i < count; i++)
        {
            SpawnEnemyAtRandomPoint(enemyName);
            yield return new WaitForSeconds(spawnCooldown);
        }
    }
    
    #endregion
    
    #region Enemy Management
    
    public void ClearAllEnemies()
    {
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
        {
            if (activeEnemies[i] != null)
            {
                if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                {
                    PhotonNetwork.Destroy(activeEnemies[i].gameObject);
                }
                else
                {
                    Destroy(activeEnemies[i].gameObject);
                }
            }
        }
        activeEnemies.Clear();
        OnEnemyCountChanged?.Invoke(0);
    }
    
    public void KillAllEnemies()
    {
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
        {
            var enemy = activeEnemies[i];
            if (enemy != null && !enemy.IsDead)
            {
                enemy.TakeEnemyDamage(enemy.MaxHealth, gameObject);
            }
        }
    }
    
    public List<BaseEnemyAI> GetEnemiesInRange(Vector3 position, float range)
    {
        List<BaseEnemyAI> enemiesInRange = new List<BaseEnemyAI>();
        
        foreach (var enemy in activeEnemies)
        {
            if (enemy != null && !enemy.IsDead)
            {
                float distance = Vector3.Distance(position, enemy.transform.position);
                if (distance <= range)
                {
                    enemiesInRange.Add(enemy);
                }
            }
        }
        
        return enemiesInRange;
    }
    
    #endregion
    
    #region Event Handlers
    
    private void HandleEnemyDied(BaseEnemyAI enemy)
    {
        if (activeEnemies.Contains(enemy))
        {
            activeEnemies.Remove(enemy);
            OnEnemyDied?.Invoke(enemy);
            OnEnemyCountChanged?.Invoke(activeEnemies.Count);
            NotifyQuestKillProgress(enemy);
            
            if (showDebugInfo)
            {
                Debug.Log($"[EnemyManager] Enemy {enemy.name} died. Remaining: {activeEnemies.Count}");
            }
        }
    }

    private void NotifyQuestKillProgress(BaseEnemyAI enemy)
    {
        string enemyId = ResolveQuestEnemyId(enemy);
        if (string.IsNullOrWhiteSpace(enemyId))
            return;

        PlayerQuestRelay[] relays = FindObjectsByType<PlayerQuestRelay>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (relays == null || relays.Length == 0)
        {
            QuestManager questManager = FindFirstObjectByType<QuestManager>();
            if (questManager != null)
                questManager.AddProgress_Kill(enemyId);
            return;
        }

        bool isNetworkMultiplayer = PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && PhotonNetwork.InRoom;
        for (int i = 0; i < relays.Length; i++)
        {
            PlayerQuestRelay relay = relays[i];
            if (relay == null)
                continue;

            if (isNetworkMultiplayer && relay.photonView != null)
            {
                relay.photonView.RPC(nameof(PlayerQuestRelay.RPC_AddKillProgress), RpcTarget.All, enemyId);
            }
            else
            {
                relay.RPC_AddKillProgress(enemyId);
            }
        }
    }

    private static string ResolveQuestEnemyId(BaseEnemyAI enemy)
    {
        if (enemy != null && enemy.enemyData != null && !string.IsNullOrWhiteSpace(enemy.enemyData.enemyName))
            return enemy.enemyData.enemyName.Trim();

        if (enemy == null)
            return string.Empty;

        return SanitizeEnemyName(enemy.name);
    }

    private static string SanitizeEnemyName(string enemyName)
    {
        if (string.IsNullOrWhiteSpace(enemyName))
            return string.Empty;

        return enemyName.Replace("(Clone)", string.Empty).Trim();
    }
    
    #endregion
    
    #region Public Properties
    
    public int ActiveEnemyCount => activeEnemies.Count;
    public List<BaseEnemyAI> ActiveEnemies => new List<BaseEnemyAI>(activeEnemies);
    public IReadOnlyList<BaseEnemyAI> ActiveEnemiesReadonly => activeEnemies;
    
    #endregion
    
    #region Debug
    
    void OnDrawGizmos()
    {
        if (spawnPoints != null)
        {
            Gizmos.color = Color.green;
            foreach (var spawnPoint in spawnPoints)
            {
                if (spawnPoint != null)
                {
                    Gizmos.DrawWireSphere(spawnPoint.position, spawnRadius);
                }
            }
        }
    }
    
    #endregion
    
    #region Cleanup
    
    void OnDestroy()
    {
        // Stop all coroutines
        StopAllCoroutines();
        
        // Unsubscribe from enemy events
        if (activeEnemies != null)
        {
            for (int i = activeEnemies.Count - 1; i >= 0; i--)
            {
                var enemy = activeEnemies[i];
                if (enemy != null)
                {
                    try
                    {
                        enemy.OnEnemyDied -= HandleEnemyDied;
                    }
                    catch (System.Exception)
                    {
                        // Enemy already destroyed, ignore
                    }
                }
            }
            activeEnemies.Clear();
        }
        
        // Clear collections
        if (enemyPrefabLookup != null)
            enemyPrefabLookup.Clear();
        cachedChapterLoop = null;
        
        // Clear event subscriptions
        OnEnemySpawned = null;
        OnEnemyDied = null;
        OnEnemyCountChanged = null;
    }
    
    #endregion
}
