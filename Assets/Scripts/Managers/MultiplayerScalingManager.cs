using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;

public class MultiplayerScalingManager : MonoBehaviourPun
{
    [Header("Scaling Settings")]
    [Tooltip("Enable multiplayer scaling for enemies")]
    public bool enableScaling = true;
    [Tooltip("Health multiplier per additional player (e.g., 1.5 = +50% per player)")]
    public float healthMultiplierPerPlayer = 1.0f;
    [Tooltip("Damage multiplier per additional player")]
    public float damageMultiplierPerPlayer = 0.2f;
    [Tooltip("Spawn count multiplier per additional player")]
    public float spawnCountMultiplierPerPlayer = 0.5f;
    
    [Header("Scaling Formula")]
    [Tooltip("Formula: baseValue * (1 + (playerCount - 1) * multiplier)")]
    public bool useLinearScaling = true;
    [Tooltip("Alternative: exponential scaling base (e.g., 1.2 = 20% per player)")]
    public float exponentialBase = 1.2f;
    [SerializeField] private bool enableDebugLogs = false;
    
    public static MultiplayerScalingManager Instance { get; private set; }
    
    private int cachedPlayerCount = 1;
    private float lastPlayerCountCheck = 0f;
    private const float PLAYER_COUNT_CHECK_INTERVAL = 1f;
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }
    
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (Time.time - lastPlayerCountCheck > PLAYER_COUNT_CHECK_INTERVAL)
        {
            UpdatePlayerCount();
            lastPlayerCountCheck = Time.time;
        }
    }
    
    private void UpdatePlayerCount()
    {
        int newCount = GetPlayerCount();
        if (newCount != cachedPlayerCount)
        {
            cachedPlayerCount = newCount;
            OnPlayerCountChanged(newCount);
        }
    }
    
    public int GetPlayerCount()
    {
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            return PhotonNetwork.PlayerList.Length;
        }
        else
        {
            var players = GameObject.FindGameObjectsWithTag("Player");
            return Mathf.Max(1, players.Length);
        }
    }
    
    private void OnPlayerCountChanged(int newCount)
    {
        if (enableDebugLogs) Debug.Log($"[MultiplayerScaling] Player count changed to {newCount}");
        
        if (enableScaling && newCount > 1)
        {
            ScaleExistingEnemies();
        }
    }
    
    public float GetHealthMultiplier()
    {
        if (!enableScaling) return 1f;
        
        int playerCount = Mathf.Max(1, cachedPlayerCount);
        if (playerCount <= 1) return 1f;
        
        if (useLinearScaling)
        {
            return 1f + (playerCount - 1) * healthMultiplierPerPlayer;
        }
        else
        {
            return Mathf.Pow(exponentialBase, playerCount - 1);
        }
    }
    
    public float GetDamageMultiplier()
    {
        if (!enableScaling) return 1f;
        
        int playerCount = Mathf.Max(1, cachedPlayerCount);
        if (playerCount <= 1) return 1f;
        
        if (useLinearScaling)
        {
            return 1f + (playerCount - 1) * damageMultiplierPerPlayer;
        }
        else
        {
            return Mathf.Pow(exponentialBase, playerCount - 1) * 0.5f;
        }
    }
    
    public int GetScaledSpawnCount(int baseCount)
    {
        if (!enableScaling) return baseCount;
        
        int playerCount = Mathf.Max(1, cachedPlayerCount);
        if (playerCount <= 1) return baseCount;
        
        float multiplier = 1f + (playerCount - 1) * spawnCountMultiplierPerPlayer;
        return Mathf.RoundToInt(baseCount * multiplier);
    }
    
    public int GetScaledHealth(int baseHealth)
    {
        return Mathf.RoundToInt(baseHealth * GetHealthMultiplier());
    }
    
    public int GetScaledDamage(int baseDamage)
    {
        return Mathf.RoundToInt(baseDamage * GetDamageMultiplier());
    }
    
    private void ScaleExistingEnemies()
    {
        var enemyManager = FindFirstObjectByType<EnemyManager>();
        if (enemyManager == null) return;
        
        var enemies = enemyManager.ActiveEnemiesReadonly;
        float healthMult = GetHealthMultiplier();
        
        if (Mathf.Approximately(healthMult, 1f)) return;
        
        foreach (var enemy in enemies)
        {
            if (enemy == null || enemy.IsDead) continue;
            
            if (PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode)
            {
                int oldMaxHealth = enemy.MaxHealth;
                int newMaxHealth = GetScaledHealth(oldMaxHealth);
                
                if (newMaxHealth != oldMaxHealth)
                {
                    int currentHealth = enemy.CurrentHealth;
                    float healthPercent = oldMaxHealth > 0 ? (float)currentHealth / oldMaxHealth : 1f;
                    int newCurrentHealth = Mathf.RoundToInt(newMaxHealth * healthPercent);
                    
                    var healthField = typeof(BaseEnemyAI).GetField("currentHealth", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (healthField != null)
                    {
                        healthField.SetValue(enemy, newCurrentHealth);
                    }
                }
            }
        }
    }
}

