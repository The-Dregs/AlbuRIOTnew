using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using Photon.Pun;

public class ProceduralMapLoader : MonoBehaviour
{
    [Header("Target Scene")]
    [Tooltip("Name of the procedurally generated map scene to load")]
    public string targetMapSceneName = "FIRSTMAP";
    
    [Header("Loading Panel")]
    [Tooltip("Loading panel GameObject to show during map generation")]
    public GameObject loadingPanel;
    [Tooltip("Optional loading text for status updates")]
    public TMPro.TextMeshProUGUI loadingText;
    [Tooltip("Interval for loading text animation (seconds)")]
    public float loadingTextInterval = 0.4f;
    
    [Header("Spawn Settings")]
    [Tooltip("Which spawn point index to use (0 = first, 1 = second, etc.). -1 = random")]
    public int preferredSpawnIndex = -1;
    
    [Tooltip("Maximum time to wait for terrain generation (seconds)")]
    public float maxGenerationWaitTime = 15f;
    
    [Tooltip("Time to wait after scene loads before checking for spawn points (seconds)")]
    public float initialWaitTime = 2f;
    
    [Header("Debug")]
    public bool enableDebugLogs = true;

    private SceneLoader sceneLoader;
    private bool isTransitioning = false;

    /// <summary>True while ProceduralMapLoader is showing its loading panel.</summary>
    public static bool IsLoadingActive { get; private set; }

    private static ProceduralMapLoader _instance;
    private bool isAnimatingLoadingText = false;
    private string currentLoadingBaseText = "Loading";

void Start()
    {
        sceneLoader = FindFirstObjectByType<SceneLoader>();
        
        // Ensure this GameObject has a trigger collider
        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            Debug.LogWarning("[ProceduralMapLoader] No Collider found! Adding BoxCollider as trigger. Make sure to set it to IsTrigger = true in the inspector.");
            col = gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
        }
        else if (!col.isTrigger)
        {
            Debug.LogWarning("[ProceduralMapLoader] Collider found but IsTrigger is false. Setting to true.");
            col.isTrigger = true;
        }
    }

    [Header("Trigger Settings")]
    [Tooltip("Only trigger once (disable after first use)")]
    public bool triggerOnce = false;
    
    [Tooltip("Delay before triggering (seconds)")]
    public float triggerDelay = 0f;
    
    [Tooltip("Tag to detect (default: Player)")]
    public string triggerTag = "Player";
    
    private bool hasTriggered = false;


    public void LoadProceduralMapAndSpawn()
    {
        if (isTransitioning)
        {
            if (enableDebugLogs) Debug.LogWarning("[ProceduralMapLoader] Already transitioning!");
            return;
        }

        if (string.IsNullOrEmpty(targetMapSceneName))
        {
            Debug.LogError("[ProceduralMapLoader] Target map scene name is not set!");
            return;
        }

        // Multiplayer must use the unified NetworkManager scene-transition flow.
        // Do not run the legacy carryover coroutine in network rooms.
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            if (!PhotonNetwork.InRoom)
            {
                Debug.LogWarning("[ProceduralMapLoader] Ignoring legacy map-load request while connected but not in room.");
                return;
            }

            Inventory.CacheLocalInventory();
            EquipmentManager.CacheLocalEquipment();
            if (!NetworkManager.BeginSceneTransition(targetMapSceneName))
            {
                Debug.LogWarning($"[ProceduralMapLoader] NetworkManager.BeginSceneTransition failed for '{targetMapSceneName}'.");
            }
            return;
        }

        StartCoroutine(Co_LoadMapAndSpawnPlayer());
    }

    private IEnumerator Co_LoadMapAndSpawnPlayer()
    {
        isTransitioning = true;
        IsLoadingActive = true;
        _instance = this;
        PlayerSpawnManager.hasTeleportedByLoader = false;
        CutsceneManager.SetTransitionControlledStart(true);

        // Persist across scene load so coroutine can complete (player was destroyed, this runs the rest)
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        if (loadingPanel != null && loadingPanel.transform.parent == null)
            DontDestroyOnLoad(loadingPanel);

        ShowLoadingPanel("Loading map...");
        
        if (enableDebugLogs) Debug.Log($"[ProceduralMapLoader] Loading procedurally generated map: {targetMapSceneName}");

        // For room-owned generated objects, avoid pre-load destroy passes on clients.
        // Scene transitions and authoritative regeneration handle replacement safely.
        var cleanupManager = MemoryCleanupManager.EnsureInstance();
        bool isNetworkedRoom = PhotonNetwork.IsConnected && PhotonNetwork.InRoom;
        if (!isNetworkedRoom)
        {
            cleanupManager?.CleanupSceneTransitionObjects();
        }
        else if (enableDebugLogs)
        {
            Debug.Log("[ProceduralMapLoader] Skipping transition destroy cleanup in network room (room-owned generation flow).");
        }

        if (isNetworkedRoom)
            yield return null;

        // Cache inventory and equipment before destroying the player
        Inventory.CacheLocalInventory();
        EquipmentManager.CacheLocalEquipment();

        // Clean up local player before scene transition to prevent ViewID exhaustion
        GameObject localPlayer = FindLocalPlayer();
        if (localPlayer != null)
        {
            PhotonView pv = localPlayer.GetComponent<PhotonView>();
            if (isNetworkedRoom)
            {
                if (enableDebugLogs) Debug.Log("[ProceduralMapLoader] Skipping explicit local player destroy in network room.");
            }
            else if (pv == null || pv.IsMine)
            {
                if (enableDebugLogs) Debug.Log("[ProceduralMapLoader] Destroying local player before scene transition");
                Destroy(localPlayer);
                
                yield return new WaitForEndOfFrame();
            }
        }
        
        // Load the scene
        if (sceneLoader != null)
        {
            sceneLoader.LoadScene(targetMapSceneName);
        }
        else if (PhotonNetwork.IsConnectedAndReady || PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.LoadLevel(targetMapSceneName);
        }
        else
        {
            SceneManager.LoadScene(targetMapSceneName);
        }

        // Wait for scene to load
        yield return new WaitUntil(() => SceneManager.GetActiveScene().name == targetMapSceneName);
        
        UpdateLoadingText("Generating terrain...");
        if (enableDebugLogs) Debug.Log($"[ProceduralMapLoader] Scene {targetMapSceneName} loaded, waiting for terrain generation...");

        // Wait initial delay for scene objects to initialize
        yield return new WaitForSeconds(initialWaitTime);

        // Wait for terrain generation to complete
        yield return StartCoroutine(WaitForTerrainGeneration());

        UpdateLoadingText("Preparing spawn...");

        // if the destination scene has a start-scene cutscene, let it handle spawn
        var startCutsceneManager = FindStartSceneCutsceneManager();
        if (startCutsceneManager != null)
        {
            UpdateLoadingText("Starting intro cutscene...");

            startCutsceneManager.BeginStartSceneSequence();

            // hide our loading panel after cutscene manager has started
            // (its own loading panel / fade overlay is already covering the view)
            HideLoadingPanel();
            IsLoadingActive = false;

            // wait for cutscene + spawn to finish (CutsceneManager spawns the player)
            float elapsed = 0f;
            const float maxWait = 90f;
            while (elapsed < maxWait)
            {
                if (startCutsceneManager == null || startCutsceneManager.IsStartSequenceComplete)
                    break;
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (enableDebugLogs) Debug.Log("[ProceduralMapLoader] Start-scene cutscene/spawn sequence completed.");
        }
        else
        {
            UpdateLoadingText("Placing player...");
            yield return PlayerSpawnCoordinator.EnsureLocalPlayerAtSpawn(
                maxWaitSeconds: 15f,
                waitForSpawnMarkers: true,
                enableDebugLogs: enableDebugLogs,
                logPrefix: "[ProceduralMapLoader]");
        }

        HideLoadingPanel();
        isTransitioning = false;
        IsLoadingActive = false;

        // Cleanup: we no longer need to persist (optional - keeps DontDestroyOnLoad active for next use)
        // If triggerOnce, we could Destroy(gameObject) here - leaving as-is for potential reuse

        if (enableDebugLogs) Debug.Log("[ProceduralMapLoader] Scene transition complete.");
    }

    /// <summary>Teleports the player to the spawn position.</summary>
    private void TeleportPlayerToSpawn(GameObject player, Vector3 spawnPosition)
    {
        if (player == null) return;

        PhotonView pv = player.GetComponent<PhotonView>();
        if (pv != null && PhotonNetwork.IsConnected && !pv.IsMine)
            return; // Not local player

        // CharacterController must be disabled during position change
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.enabled = false;
            player.transform.position = spawnPosition;
            cc.enabled = true;
        }
        else
        {
            player.transform.position = spawnPosition;
        }

        // Setup player controls and camera (same as FirstMapSpawnHandler.SetupPlayer)
        var controller = player.GetComponent<ThirdPersonController>();
        if (controller != null)
        {
            controller.SetCanMove(true);
            controller.SetCanControl(true);
        }

        Camera cam = player.transform.Find("Camera")?.GetComponent<Camera>();
        if (cam != null)
        {
            cam.enabled = true;
            cam.tag = "MainCamera";
        }

        var cameraOrbit = player.GetComponentInChildren<ThirdPersonCameraOrbit>();
        if (cameraOrbit != null)
        {
            Transform cameraPivot = player.transform.Find("Camera/CameraPivot/TPCamera");
            if (cameraPivot != null)
            {
                cameraOrbit.AssignTargets(player.transform, cameraPivot);
            }
            cameraOrbit.SetRotationLocked(false);
        }

        if (LocalInputLocker.Ensure() != null)
        {
            LocalInputLocker.Ensure().ForceGameplayCursor();
        }
    }

    private IEnumerator WaitForTerrainGeneration()
    {
        float elapsed = 0f;
        TerrainGenerator terrainGen = null;
        MapResourcesGenerator resourceGen = null;

        while (elapsed < maxGenerationWaitTime)
        {
            terrainGen = FindFirstObjectByType<TerrainGenerator>();
            resourceGen = FindFirstObjectByType<MapResourcesGenerator>();

            // Check if terrain exists and has data
            bool terrainReady = false;
            if (terrainGen != null)
            {
                Terrain terrain = terrainGen.GetComponent<Terrain>();
                if (terrain != null && terrain.terrainData != null)
                {
                    terrainReady = true;
                }
            }

            // Check if spawn markers exist (indicates resource generation completed)
            Transform[] spawnMarkers = FindSpawnMarkers();
            bool spawnsReady = spawnMarkers != null && spawnMarkers.Length > 0;

            if (terrainReady && spawnsReady)
            {
                if (enableDebugLogs) Debug.Log("[ProceduralMapLoader] Terrain and spawn points are ready!");
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (enableDebugLogs)
        {
            Debug.LogWarning($"[ProceduralMapLoader] Timeout waiting for terrain generation after {maxGenerationWaitTime}s");
        }
    }

    private CutsceneManager FindStartSceneCutsceneManager()
    {
        var cutsceneManagers = FindObjectsByType<CutsceneManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cutsceneManagers.Length; i++)
        {
            var manager = cutsceneManagers[i];
            if (manager != null && manager.cutsceneMode == CutsceneMode.StartScene)
                return manager;
        }
        return null;
    }

    private Transform[] FindSpawnMarkers()
    {
        // MapResourcesGenerator creates markers named "SpawnMarker_1", "SpawnMarker_2", etc.
        var spawnMarkers = new System.Collections.Generic.List<Transform>();
        
        // Search for all GameObjects with names starting with "SpawnMarker_"
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        
        foreach (GameObject obj in allObjects)
        {
            if (obj != null && obj.name.StartsWith("SpawnMarker_"))
            {
                spawnMarkers.Add(obj.transform);
            }
        }

        // Sort by name to get consistent ordering (SpawnMarker_1, SpawnMarker_2, etc.)
        spawnMarkers.Sort((a, b) => string.Compare(a.name, b.name));

        if (enableDebugLogs && spawnMarkers.Count > 0)
        {
            Debug.Log($"[ProceduralMapLoader] Found {spawnMarkers.Count} spawn markers");
        }

        return spawnMarkers.Count > 0 ? spawnMarkers.ToArray() : null;
    }

    private Transform SelectSpawnPoint(Transform[] spawnPoints)
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return null;

        int index;
        
        if (preferredSpawnIndex >= 0 && preferredSpawnIndex < spawnPoints.Length)
        {
            index = preferredSpawnIndex;
        }
        else if (preferredSpawnIndex == -1)
        {
            // Random spawn
            index = Random.Range(0, spawnPoints.Length);
        }
        else
        {
            // Invalid index, use first
            index = 0;
        }

        return spawnPoints[index];
    }

    private IEnumerator SpawnPlayerAtPosition(Vector3 spawnPosition)
    {
        yield return new WaitForSeconds(0.5f);
        
        GameObject existingPlayer = FindLocalPlayer();
        
        if (existingPlayer != null)
        {
            PhotonView pv = existingPlayer.GetComponent<PhotonView>();
            if (pv != null && PhotonNetwork.IsConnected && !pv.IsMine)
            {
                if (enableDebugLogs) Debug.Log("[ProceduralMapLoader] Not local player's PhotonView, will spawn new player");
                existingPlayer = null;
            }
            else
            {
                if (enableDebugLogs) Debug.Log($"[ProceduralMapLoader] Teleporting existing player to {spawnPosition}");
                existingPlayer.transform.position = spawnPosition;
                
                CharacterController cc = existingPlayer.GetComponent<CharacterController>();
                if (cc != null)
                {
                    cc.enabled = false;
                    existingPlayer.transform.position = spawnPosition;
                    cc.enabled = true;
                }
                
                var controller = existingPlayer.GetComponent<ThirdPersonController>();
                if (controller != null)
                {
                    controller.SetCanMove(true);
                    controller.SetCanControl(true);
                }
                yield break;
            }
        }
        
        if (existingPlayer == null)
        {
            TutorialSpawnManager spawnManager = FindFirstObjectByType<TutorialSpawnManager>();
            if (spawnManager != null)
            {
                if (spawnManager.spawnPoints != null && spawnManager.spawnPoints.Length > 0)
                {
                    int spawnIndex = preferredSpawnIndex >= 0 && preferredSpawnIndex < spawnManager.spawnPoints.Length 
                        ? preferredSpawnIndex 
                        : (preferredSpawnIndex == -1 ? Random.Range(0, spawnManager.spawnPoints.Length) : 0);
                    
                    if (spawnIndex < spawnManager.spawnPoints.Length)
                    {
                        spawnManager.spawnPoints[spawnIndex].position = spawnPosition;
                    }
                }
                
                if (enableDebugLogs) Debug.Log("[ProceduralMapLoader] Spawning player via TutorialSpawnManager");
                spawnManager.SpawnPlayerForThisClient();
                
                yield return new WaitForSeconds(0.5f);
                
                int attempts = 0;
                GameObject player = null;
                while (player == null && attempts < 10)
                {
                    player = FindLocalPlayer();
                    if (player == null)
                    {
                        yield return new WaitForSeconds(0.1f);
                        attempts++;
                    }
                }
                
                if (player != null)
                {
                    var controller = player.GetComponent<ThirdPersonController>();
                    if (controller != null)
                    {
                        controller.SetCanMove(true);
                        controller.SetCanControl(true);
                    }
                    if (enableDebugLogs) Debug.Log("[ProceduralMapLoader] Player spawned successfully");
                }
                else
                {
                    Debug.LogWarning("[ProceduralMapLoader] Player spawn timed out");
                }
            }
            else
            {
                if (enableDebugLogs) Debug.LogWarning("[ProceduralMapLoader] No TutorialSpawnManager found, player will spawn at default location");
            }
        }
    }
    
    private IEnumerator SpawnPlayerAtDefault()
    {
        TutorialSpawnManager spawnManager = FindFirstObjectByType<TutorialSpawnManager>();
        if (spawnManager != null)
        {
            if (enableDebugLogs) Debug.Log("[ProceduralMapLoader] Spawning player via TutorialSpawnManager at default position");
            spawnManager.SpawnPlayerForThisClient();
            yield return new WaitForSeconds(0.5f);
        }
    }
    
    private void ShowLoadingPanel(string initialText = "Loading...")
    {
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(true);
        }
        
        if (loadingText != null)
        {
            currentLoadingBaseText = initialText.Replace(".", "").Trim();
            loadingText.text = initialText;
            isAnimatingLoadingText = true;
            StartCoroutine(AnimateLoadingText());
        }
    }
    
    private void UpdateLoadingText(string text)
    {
        if (loadingText != null)
        {
            currentLoadingBaseText = text.Replace(".", "").Trim();
            loadingText.text = text;
        }
    }
    
    /// <summary>Allows external systems to hide this loader panel when needed.</summary>
    public static void HideLoadingPanelExternal()
    {
        if (_instance != null)
            _instance.HideLoadingPanel();
    }

    private void HideLoadingPanel()
    {
        isAnimatingLoadingText = false;
        if (_instance == this) _instance = null;

        if (SceneLoader.Instance != null)
            SceneLoader.Instance.HideLoadingPanel();

        if (loadingPanel != null)
        {
            loadingPanel.SetActive(false);
        }
        
        if (loadingText != null)
        {
            loadingText.text = "";
        }
    }
    
    private IEnumerator AnimateLoadingText()
    {
        int dotCount = 0;
        
        while (isAnimatingLoadingText)
        {
            dotCount = (dotCount + 1) % 4;
            string dots = new string('.', dotCount);
            
            if (loadingText != null)
            {
                loadingText.text = $"{currentLoadingBaseText}{dots}";
            }
            
            yield return new WaitForSeconds(loadingTextInterval);
        }
    }

    private GameObject FindLocalPlayer()
    {
        // Try finding by tag first
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        
        if (player != null && PhotonNetwork.IsConnected)
        {
            PhotonView pv = player.GetComponent<PhotonView>();
            if (pv != null && !pv.IsMine)
            {
                // Found a player but it's not the local one, search for local
                PhotonView[] allViews = FindObjectsByType<PhotonView>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (PhotonView view in allViews)
                {
                    if (view != null && view.IsMine && view.CompareTag("Player"))
                    {
                        return view.gameObject;
                    }
                }
                return null; // No local player found
            }
        }
        
        return player;
    }

    // Public method to set target scene programmatically
    public void SetTargetScene(string sceneName)
    {
        targetMapSceneName = sceneName;
    }

    // Public method to load a specific procedural map
    public void LoadMap(string sceneName)
    {
        if (!string.IsNullOrEmpty(sceneName))
        {
            targetMapSceneName = sceneName;
        }
        LoadProceduralMapAndSpawn();
    }


    private void OnTriggerEnter(Collider other)
    {
        // Check if already triggered and triggerOnce is enabled
        if (hasTriggered && triggerOnce)
        {
            return;
        }
        
        // Check tag match
        if (string.IsNullOrEmpty(triggerTag) || !other.CompareTag(triggerTag))
        {
            return;
        }
        
        // For multiplayer, only trigger for local player
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            PhotonView pv = other.GetComponent<PhotonView>();
            if (pv != null && !pv.IsMine)
            {
                return; // Not the local player
            }
        }
        
        // Trigger the map load
        if (triggerDelay > 0f)
        {
            StartCoroutine(Co_DelayedTrigger());
        }
        else
        {
            TriggerMapLoad();
        }
    }
    
    private IEnumerator Co_DelayedTrigger()
    {
        yield return new WaitForSeconds(triggerDelay);
        TriggerMapLoad();
    }
    
    private void TriggerMapLoad()
    {
        if (hasTriggered && triggerOnce)
        {
            return;
        }
        
        hasTriggered = true;
        
        if (enableDebugLogs)
        {
            Debug.Log($"[ProceduralMapLoader] Trigger activated! Loading map: {targetMapSceneName}");
        }

        // in multiplayer, route through NetworkManager unified transition flow.
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && PhotonNetwork.InRoom)
        {
            Inventory.CacheLocalInventory();
            EquipmentManager.CacheLocalEquipment();

            if (!NetworkManager.BeginSceneTransition(targetMapSceneName))
            {
                // fallback: request via quest manager network RPC path
                if (QuestManager.Instance != null)
                    QuestManager.Instance.RequestNetworkedMapTransition(targetMapSceneName);
            }
        }
        else
        {
            // offline: use the existing ProceduralMapLoader flow
            LoadProceduralMapAndSpawn();
        }
        
        // Disable trigger if one-time use
        if (triggerOnce)
        {
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = false;
            }
        }
    }
}
