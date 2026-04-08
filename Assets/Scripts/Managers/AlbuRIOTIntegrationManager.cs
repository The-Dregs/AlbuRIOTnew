using UnityEngine;
using Photon.Pun;
using System;
using System.Collections.Generic;
using AlbuRIOT.Abilities;

public class AlbuRIOTIntegrationManager : MonoBehaviourPun
{
    [Header("Core Managers")]
    public NetworkManager networkManager;
    public GameManager gameManager;
    public QuestManager questManager;
    public ShrineManager shrineManager;
    public MovesetManager movesetManager;
    public ItemManager itemManager;
    public DataTableManager dataTableManager;
    
    [Header("Combat Systems")]
    public PlayerCombat playerCombat;
    public DebuffManager debuffManager;
    public PowerStealManager powerStealManager;
    public VFXManager vfxManager;
    
    [Header("Player Systems")]
    public PlayerStats playerStats;
    public Inventory inventory;
    
    [Header("Integration Settings")]
    public bool autoFindComponents = true;
    public bool enableDebugLogging = true;
    
    // Events
    public System.Action OnSystemsInitialized;
    public System.Action<string> OnSystemError;
    
    // Singleton pattern
    public static AlbuRIOTIntegrationManager Instance { get; private set; }
    
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
        
        if (autoFindComponents)
        {
            AutoFindComponents();
        }

        InitializeSystems();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        StopAllCoroutines();
        OnSystemsInitialized = null;
        OnSystemError = null;
    }

    void Start()
    {
        SetupSystemConnections();
        OnSystemsInitialized?.Invoke();
        
        if (enableDebugLogging)
        {
            Debug.Log("AlbuRIOT Integration Manager initialized successfully!");
        }
    }
    
    private void AutoFindComponents()
    {
        // Core Managers
        if (networkManager == null)
            networkManager = FindFirstObjectByType<NetworkManager>();
        if (gameManager == null)
            gameManager = FindFirstObjectByType<GameManager>();
        if (questManager == null)
            questManager = FindFirstObjectByType<QuestManager>();
        if (shrineManager == null)
            shrineManager = FindFirstObjectByType<ShrineManager>();
        if (movesetManager == null)
            movesetManager = FindFirstObjectByType<MovesetManager>();
        if (itemManager == null)
            itemManager = FindFirstObjectByType<ItemManager>();
        if (dataTableManager == null)
            dataTableManager = FindFirstObjectByType<DataTableManager>();
        
        // Combat Systems
        if (playerCombat == null)
            playerCombat = FindFirstObjectByType<PlayerCombat>();
        if (debuffManager == null)
            debuffManager = FindFirstObjectByType<DebuffManager>();
        if (powerStealManager == null)
            powerStealManager = FindFirstObjectByType<PowerStealManager>();
        if (vfxManager == null)
            vfxManager = FindFirstObjectByType<VFXManager>();
        
        // Player Systems
        if (playerStats == null)
            playerStats = FindFirstObjectByType<PlayerStats>();
        if (inventory == null)
            inventory = Inventory.FindLocalInventory();
    }
    
    private void InitializeSystems()
    {
        try
        {
            // Initialize DataTableManager first
            if (dataTableManager != null)
            {
                if (enableDebugLogging) Debug.Log("DataTableManager initialized");
            }
            
            // Initialize ItemManager
            if (itemManager != null)
            {
                itemManager.RefreshItemLookup();
                if (enableDebugLogging) Debug.Log("ItemManager initialized");
            }
            
            // Initialize QuestManager
            if (questManager != null)
            {
                if (enableDebugLogging) Debug.Log("QuestManager initialized");
            }
            
            // Initialize MovesetManager
            if (movesetManager != null)
            {
                if (enableDebugLogging) Debug.Log("MovesetManager initialized");
            }
            
            // Initialize VFXManager
            if (vfxManager != null)
            {
                if (enableDebugLogging) Debug.Log("VFXManager initialized");
            }
            
            // Initialize PowerStealManager
            if (powerStealManager != null)
            {
                if (enableDebugLogging) Debug.Log("PowerStealManager initialized");
            }
            
            // Initialize DebuffManager
            if (debuffManager != null)
            {
                if (enableDebugLogging) Debug.Log("DebuffManager initialized");
            }
            
        }
        catch (Exception e)
        {
            OnSystemError?.Invoke($"System initialization error: {e.Message}");
            Debug.LogError($"System initialization error: {e.Message}");
        }
    }
    
    private void SetupSystemConnections()
    {
        try
        {
            // Note: Managers now use EnsureReferences/EnsurePlayerInventory for multiplayer safety
            // Manual assignment is no longer needed as they auto-find local player components
            
            // Connect QuestManager with other systems
            if (questManager != null && shrineManager != null)
            {
                questManager.shrineManager = shrineManager;
            }
            
            // Connect MovesetManager with other systems
            if (movesetManager != null)
            {
                if (playerCombat != null)
                    movesetManager.playerCombat = playerCombat;
                if (playerStats != null)
                    movesetManager.playerStats = playerStats;
                if (vfxManager != null)
                    movesetManager.vfxManager = vfxManager;
            }
            
            // Connect PlayerCombat with other systems
            if (playerCombat != null)
            {
                if (movesetManager != null)
                    playerCombat.movesetManager = movesetManager;
                if (vfxManager != null)
                {
                    playerCombat.vfxManager = vfxManager;
                    playerCombat.effectsManager = vfxManager;
                }
                if (powerStealManager != null)
                    playerCombat.powerStealManager = powerStealManager;
            }
            
            // Connect PowerStealManager with other systems
            if (powerStealManager != null)
            {
                // powerStealManager auto-finds its own dependencies in Awake(); no direct field access needed
            }
            
            // Connect DebuffManager with other systems
            if (debuffManager != null)
            {
                // debuffManager auto-finds PlayerStats and MovesetManager in Awake()
            }
            
            if (enableDebugLogging) Debug.Log("System connections established successfully!");
        }
        catch (Exception e)
        {
            OnSystemError?.Invoke($"System connection error: {e.Message}");
            Debug.LogError($"System connection error: {e.Message}");
        }
    }
    
    #region Public API Methods
    
    public void StartQuest(int questIndex)
    {
        if (questManager != null)
        {
            questManager.StartQuest(questIndex);
        }
    }
    
    public void StealPowerFromEnemy(string enemyName, Vector3 position)
    {
        if (powerStealManager != null)
        {
            powerStealManager.StealPowerFromEnemy(enemyName, position);
        }
    }
    
    public void ApplyDebuff(string debuffName, float duration, float magnitude = 1f)
    {
        if (debuffManager != null && dataTableManager != null)
        {
            var debuffData = dataTableManager.GetDebuffData(debuffName);
            if (debuffData != null)
            {
                debuffManager.ApplyDebuff(debuffData, duration, magnitude);
            }
        }
    }
    
    public void PlayMovesetVFX(string movesetName, string moveName, Vector3 position, Quaternion rotation)
    {
        if (vfxManager != null)
        {
            vfxManager.PlayMovesetVFX(movesetName, moveName, position, rotation);
        }
    }
    
    public void PlayPowerStealVFX(string enemyName, Vector3 position, Quaternion rotation)
    {
        if (vfxManager != null)
        {
            vfxManager.PlayPowerStealVFX(enemyName, position, rotation);
        }
    }
    
    public void AddItemToInventory(string itemName, int quantity = 1)
    {
        if (inventory != null && dataTableManager != null)
        {
            var itemData = dataTableManager.GetItemData(itemName);
            if (itemData != null)
            {
                inventory.AddItem(itemData, quantity);
            }
        }
    }
    
    public void MakeShrineOffering(string shrineId, string itemName, int quantity)
    {
        if (shrineManager != null && dataTableManager != null)
        {
            var itemData = dataTableManager.GetItemData(itemName);
            if (itemData != null)
            {
                shrineManager.MakeOffering(itemData, quantity);
            }
        }
    }
    
    #endregion
    
    #region Debug Methods
    
    public void DebugSystemStatus()
    {
        Debug.Log("=== AlbuRIOT System Status ===");
        Debug.Log($"NetworkManager: {(networkManager != null ? "✓" : "✗")}");
        Debug.Log($"GameManager: {(gameManager != null ? "✓" : "✗")}");
        Debug.Log($"QuestManager: {(questManager != null ? "✓" : "✗")}");
        Debug.Log($"ShrineManager: {(shrineManager != null ? "✓" : "✗")}");
        Debug.Log($"MovesetManager: {(movesetManager != null ? "✓" : "✗")}");
        Debug.Log($"ItemManager: {(itemManager != null ? "✓" : "✗")}");
        Debug.Log($"DataTableManager: {(dataTableManager != null ? "✓" : "✗")}");
        Debug.Log($"PlayerCombat: {(playerCombat != null ? "✓" : "✗")}");
        Debug.Log($"DebuffManager: {(debuffManager != null ? "✓" : "✗")}");
        Debug.Log($"PowerStealManager: {(powerStealManager != null ? "✓" : "✗")}");
        Debug.Log($"VFXManager: {(vfxManager != null ? "✓" : "✗")}");
        Debug.Log($"PlayerStats: {(playerStats != null ? "✓" : "✗")}");
        Debug.Log($"Inventory: {(inventory != null ? "✓" : "✗")}");
    }
    
    public void TestPowerSteal(string enemyName)
    {
        StealPowerFromEnemy(enemyName, transform.position);
    }
    
    public void TestDebuff(string debuffName)
    {
        ApplyDebuff(debuffName, 10f, 1f);
    }
    
    public void TestVFX(string movesetName, string moveName)
    {
        PlayMovesetVFX(movesetName, moveName, transform.position, transform.rotation);
    }
    
    #endregion
    
    #region Event Handlers
    
    void Update()
    {
        // Debug hotkeys are editor/development-only to keep runtime overhead zero in builds.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!enableDebugLogging) return;
        if (Input.GetKeyDown(KeyCode.F1)) DebugSystemStatus();
        if (Input.GetKeyDown(KeyCode.F2)) TestPowerSteal("Aswang");
        if (Input.GetKeyDown(KeyCode.F3)) TestDebuff("Slow");
        if (Input.GetKeyDown(KeyCode.F4)) TestVFX("Aswang", "Shadow Swarm");
#endif
    }
    
    #endregion
}

