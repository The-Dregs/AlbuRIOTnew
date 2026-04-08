using UnityEngine;
using Photon.Pun;
using System.Collections;
using System.Collections.Generic;
using System;

/// <summary>
/// Centralized memory cleanup manager to prevent leaks and optimize performance.
/// Handles cleanup on scene changes, room leave, and game exit.
/// </summary>
public class MemoryCleanupManager : MonoBehaviourPun
{
    public static MemoryCleanupManager Instance { get; private set; }

    public static MemoryCleanupManager EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        var existing = FindFirstObjectByType<MemoryCleanupManager>();
        if (existing != null)
            return existing;

        var go = new GameObject("MemoryCleanupManager");
        return go.AddComponent<MemoryCleanupManager>();
    }
    
    [Header("Cleanup Settings")]
    [Tooltip("Automatically cleanup on scene change")]
    public bool cleanupOnSceneChange = true;
    [Tooltip("Automatically cleanup on room leave")]
    public bool cleanupOnRoomLeave = true;
    [Tooltip("Force garbage collection after cleanup")]
    public bool forceGCAfterCleanup = true;
    [Tooltip("Unload unused assets after cleanup")]
    public bool unloadUnusedAssets = true;
    [SerializeField] private bool enableDebugLogs = false;
    [SerializeField, Tooltip("Minimum seconds between automatic lightweight cleanups")]
    private float minCleanupInterval = 5f;
    
    private List<Action> registeredCleanupActions = new List<Action>();
    private bool isCleaningUp = false;
    private float lastCleanupTime = -999f;
    private Coroutine unloadRoutine;
    
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        // Subscribe to scene change events
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        
        // Subscribe to global quit event
        GlobalPlaymodeCleanup.OnQuitting += OnApplicationQuitting;
    }
    
    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        GlobalPlaymodeCleanup.OnQuitting -= OnApplicationQuitting;
        StopAllCoroutines();
        unloadRoutine = null;
        registeredCleanupActions?.Clear();
    }
    
    /// <summary>
    /// Register a cleanup action to be called during cleanup
    /// </summary>
    public void RegisterCleanupAction(Action cleanupAction)
    {
        if (cleanupAction != null && !registeredCleanupActions.Contains(cleanupAction))
        {
            registeredCleanupActions.Add(cleanupAction);
        }
    }
    
    /// <summary>
    /// Unregister a cleanup action
    /// </summary>
    public void UnregisterCleanupAction(Action cleanupAction)
    {
        if (cleanupAction != null)
        {
            registeredCleanupActions.Remove(cleanupAction);
        }
    }
    
    /// <summary>
    /// Perform full cleanup (called on scene change, room leave, or game exit)
    /// </summary>
    public void PerformFullCleanup(bool aggressive = false)
    {
        if (isCleaningUp) return;
        if (!aggressive && Time.unscaledTime - lastCleanupTime < Mathf.Max(0.25f, minCleanupInterval)) return;
        isCleaningUp = true;
        lastCleanupTime = Time.unscaledTime;
        
        if (enableDebugLogs) Debug.Log($"[MemoryCleanupManager] Starting {(aggressive ? "aggressive" : "lightweight")} cleanup...");
        
        // Stop all coroutines on this object
        StopAllCoroutines();
        unloadRoutine = null;
        
        // Execute registered cleanup actions
        for (int i = 0; i < registeredCleanupActions.Count; i++)
        {
            try
            {
                registeredCleanupActions[i]?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[MemoryCleanupManager] Error in cleanup action: {e.Message}");
            }
        }
        
        // Clear collections
        ClearAllCollections();
        
        // Unload unused assets
        if (unloadUnusedAssets)
        {
            if (aggressive)
            {
                Resources.UnloadUnusedAssets();
            }
            else if (unloadRoutine == null && isActiveAndEnabled)
            {
                unloadRoutine = StartCoroutine(CoUnloadUnusedAssets());
            }
        }
        
        // Force garbage collection
        if (forceGCAfterCleanup && aggressive)
        {
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect();
        }
        
        isCleaningUp = false;
        if (enableDebugLogs) Debug.Log("[MemoryCleanupManager] Cleanup complete.");
    }
    
    /// <summary>
    /// Cleanup on room leave (multiplayer)
    /// </summary>
    public void CleanupOnRoomLeave()
    {
        if (!cleanupOnRoomLeave) return;
        
        if (enableDebugLogs) Debug.Log("[MemoryCleanupManager] Cleaning up on room leave...");
        
        // Cleanup network objects
        // Note: PhotonNetwork automatically handles RPC cleanup on disconnect
        // No manual cleanup needed
        
        PerformFullCleanup(true);
    }
    
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (cleanupOnSceneChange)
        {
            if (enableDebugLogs) Debug.Log($"[MemoryCleanupManager] Scene loaded: {scene.name}, performing cleanup...");
            PerformFullCleanup(false);
        }
    }
    
    private void OnApplicationQuitting()
    {
        if (enableDebugLogs) Debug.Log("[MemoryCleanupManager] Application quitting, performing final cleanup...");
        PerformFullCleanup(true);
    }
    
    private void ClearAllCollections()
    {
        // Clear static collections that might hold references
        // This is a safety measure - individual managers should handle their own cleanup
    }
    
    private IEnumerator CoUnloadUnusedAssets()
    {
        yield return null; // Wait a frame
        Resources.UnloadUnusedAssets();
        unloadRoutine = null;
    }
    
    /// <summary>
    /// Cleanup specific to procedural generation
    /// </summary>
    public void CleanupProceduralGeneration()
    {
        if (enableDebugLogs) Debug.Log("[MemoryCleanupManager] Cleaning up procedural generation...");
        
        // Find and cleanup map generators
        var mapGen = FindFirstObjectByType<MapResourcesGenerator>();
        if (mapGen != null)
        {
            mapGen.CleanupAll();
        }
        
        var terrainGen = FindFirstObjectByType<TerrainGenerator>();
        if (terrainGen != null)
        {
            // TerrainGenerator cleanup if needed
        }
        
        var enemySpawner = FindFirstObjectByType<PerlinEnemySpawner>();
        if (enemySpawner != null)
        {
            enemySpawner.Cleanup();
        }
    }

    /// <summary>
    /// Removes leftover runtime scene objects before a map transition so buffered
    /// network instantiations do not collide with newly loaded scene content.
    /// </summary>
    public void CleanupSceneTransitionObjects()
    {
        if (enableDebugLogs) Debug.Log("[MemoryCleanupManager] Cleaning up scene transition objects...");

        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            if (enableDebugLogs)
                Debug.Log("[MemoryCleanupManager] Skipping transition destroy cleanup in network room (room-owned generation flow).");
            return;
        }

        CleanupProceduralGeneration();

        DestroySceneObjects<BaseEnemyAI>();
        DestroySceneObjects<ItemPickup>();
        DestroySceneObjects<DestructiblePlant>();
        DestroyLikelyStaleRuntimePhotonViews(enableDebugLogs);
    }

    /// <summary>
    /// Destroys all Photon-instantiated objects created by the local actor and clears
    /// their buffered instantiate/RPC events to release ViewIDs before scene transitions.
    /// </summary>
    public void CleanupLocalActorPhotonObjectsForTransition()
    {
        // intentionally disabled: room-owned scene instantiation flow should not pre-destroy actor objects
        // during transitions, as this can emit stale destroy events to joiners.
        if (enableDebugLogs)
            Debug.Log("[MemoryCleanupManager] CleanupLocalActorPhotonObjectsForTransition disabled for room-owned generation flow.");
    }

    private static void DestroyLikelyStaleRuntimePhotonViews(bool logDebug)
    {
        var allViews = FindObjectsByType<PhotonView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int localActor = (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer != null)
            ? PhotonNetwork.LocalPlayer.ActorNumber
            : -1;

        for (int i = 0; i < allViews.Length; i++)
        {
            PhotonView pv = allViews[i];
            if (pv == null || pv.ViewID <= 0)
                continue;

            // local actor-owned objects are purged via DestroyPlayerObjects() to avoid duplicate destroy events
            if (localActor > 0 && pv.CreatorActorNr == localActor)
                continue;

            GameObject go = pv.gameObject;
            if (go == null)
                continue;

            if (IsProtectedTransitionObject(go))
                continue;

            if (!LooksLikeTransitionRuntimeSpawn(go))
                continue;

            if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
            {
                if (PhotonNetwork.IsMasterClient)
                {
                    PhotonNetwork.Destroy(go);
                }
                else
                {
                    // non-owners should not local-destroy active network objects; the owner/master will emit proper destroy events
                    continue;
                }
            }
            else
            {
                Destroy(go);
            }

            if (logDebug)
                Debug.Log($"[MemoryCleanupManager] Cleared stale runtime PhotonView '{go.name}' (ViewID {pv.ViewID}).");
        }
    }

    private static bool LooksLikeTransitionRuntimeSpawn(GameObject go)
    {
        if (go == null)
            return false;

        if (go.GetComponent<BaseEnemyAI>() != null || go.GetComponentInChildren<BaseEnemyAI>(true) != null)
            return true;

        if (go.GetComponent<ItemPickup>() != null || go.GetComponent<DestructiblePlant>() != null)
            return true;

        string name = go.name;
        if (string.IsNullOrEmpty(name))
            return false;

        return name.StartsWith("Enemy_")
               || name.StartsWith("Item_")
               || name.IndexOf("ShipRemnants", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsProtectedTransitionObject(GameObject go)
    {
        if (go == null)
            return true;

        if (go.GetComponentInParent<PlayerStats>() != null)
            return true;
        if (go.GetComponentInParent<ThirdPersonController>() != null)
            return true;

        if (go.GetComponentInParent<MemoryCleanupManager>() != null)
            return true;
        if (go.GetComponentInParent<MapTransitionManager>() != null)
            return true;
        if (go.GetComponentInParent<ProceduralMapLoader>() != null)
            return true;
        if (go.GetComponentInParent<SceneLoader>() != null)
            return true;
        if (go.GetComponentInParent<NetworkManager>() != null)
            return true;
        if (go.GetComponentInParent<QuestManager>() != null)
            return true;
        if (go.GetComponentInParent<LocalInputLocker>() != null)
            return true;
        if (go.GetComponentInParent<LocalUIManager>() != null)
            return true;
        if (go.GetComponentInParent<ScreenFader>() != null)
            return true;
        if (go.GetComponentInParent<CutsceneManager>() != null)
            return true;

        return false;
    }

    private static void DestroySceneObjects<T>() where T : Component
    {
        var objects = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int localActor = (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer != null)
            ? PhotonNetwork.LocalPlayer.ActorNumber
            : -1;

        for (int i = 0; i < objects.Length; i++)
        {
            var component = objects[i];
            if (component == null)
                continue;

            var go = component.gameObject;
            if (go == null)
                continue;

            var pv = go.GetComponent<PhotonView>();
            if (pv != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom && pv.ViewID > 0)
            {
                // local actor-owned objects are purged via DestroyPlayerObjects() to avoid duplicate destroy events
                if (localActor > 0 && pv.CreatorActorNr == localActor)
                    continue;

                if (PhotonNetwork.IsMasterClient)
                {
                    PhotonNetwork.Destroy(go);
                }
                else
                {
                    // non-owners should not remove active network objects locally; wait for network destroy event
                    continue;
                }

                continue;
            }

            Destroy(go);
        }
    }
}

