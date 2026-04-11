using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine.SceneManagement;
using ExitGames.Client.Photon;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class NetworkManager : MonoBehaviourPunCallbacks
{
    [Header("Network Configuration")]
    public string gameVersion = "1.0";
    public int maxPlayersPerRoom = 4;
    [Tooltip("When true, connects to Photon on Start(). Disable for dev to default to Offline mode.")]
    public bool autoConnectOnStart = false;
    [Tooltip("When true and autoConnectOnStart is false, force offline mode on Start for local testing.")]
    public bool startInOfflineModeWhenNotAutoConnecting = false;
    
    [Header("Player Management")]
    public GameObject playerPrefab;
    [Tooltip("Optional. Player 2 prefab for 2nd spawn.")]
    public GameObject playerPrefab2;
    [Tooltip("Optional. Player 3 prefab for 3rd spawn.")]
    public GameObject playerPrefab3;
    [Tooltip("Optional. Player 4 prefab for 4th spawn.")]
    public GameObject playerPrefab4;
    public Transform[] spawnPoints;
    [Tooltip("If true, auto-spawn local player immediately in OnJoinedRoom. Keep false when using cutscene-driven or coordinator-driven spawning.")]
    public bool autoSpawnOnJoinedRoom = false;
    
    [Header("Game State")]
    public bool isGameStarted = false;
    public bool isGamePaused = false;
    
    [Header("Managers")]
    public QuestManager questManager;
    public ShrineManager shrineManager;
    public MovesetManager movesetManager;

    [Header("Runtime Debug Overlay")]
    [Tooltip("Enable in-game debug overlay. Toggle visibility with F2.")]
    public bool enableRuntimeDebugOverlay = true;
    [Tooltip("Key used to toggle the in-game debug overlay.")]
    public KeyCode debugOverlayToggleKey = KeyCode.F2;
    [Tooltip("Maximum number of log lines kept in memory.")]
    [Range(20, 500)] public int debugOverlayBufferSize = 200;
    [Tooltip("Maximum number of lines shown on screen.")]
    [Range(5, 80)] public int debugOverlayVisibleLines = 24;
    [Tooltip("Overlay width in pixels.")]
    [Range(300f, 1400f)] public float debugOverlayWidth = 780f;
    [Tooltip("Overlay height in pixels.")]
    [Range(120f, 700f)] public float debugOverlayHeight = 280f;
    
    // Events (renamed to avoid name collision with Photon callbacks)
    public event Action ConnectedToMasterEvent;
    public event Action JoinedRoomEvent;
    public event Action LeftRoomEvent;
    public event Action<Player> OnPlayerJoined;
    public event Action<Player> OnPlayerLeft;
    public event Action OnGameStarted;
    public event Action OnGamePaused;
    public event Action OnGameResumed;
    
    // Singleton pattern
    public static NetworkManager Instance { get; private set; }

    private readonly Queue<string> runtimeLogBuffer = new Queue<string>();
    private bool isDebugOverlayVisible;
    private GUIStyle debugOverlayTextStyle;
    private GUIStyle debugOverlayTitleStyle;
    private readonly StringBuilder debugOverlayLineBuilder = new StringBuilder(512);
    private bool isSceneTransitionInProgress = false;
    private string transitionTargetScene = string.Empty;
    private const string TRANSITION_SCENE_READY_KEY = "NMTransitionSceneReady";
    private const string TRANSITION_SPAWN_READY_KEY = "NMTransitionSpawnReady";
    
    void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (enableRuntimeDebugOverlay)
            {
                Application.logMessageReceived += OnRuntimeLogReceived;
                AppendRuntimeLog("[NetworkManager] Runtime debug overlay ready. Press F2 to toggle.");
            }
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        // Auto-find managers
        if (questManager == null)
            questManager = FindFirstObjectByType<QuestManager>();
        if (shrineManager == null)
            shrineManager = FindFirstObjectByType<ShrineManager>();
        if (movesetManager == null)
            movesetManager = FindFirstObjectByType<MovesetManager>();
    }
    
    void OnDestroy()
    {
        if (enableRuntimeDebugOverlay)
        {
            Application.logMessageReceived -= OnRuntimeLogReceived;
        }

        if (Instance == this)
        {
            Instance = null;
        }
        
        // Stop all coroutines to prevent leaks
        StopAllCoroutines();

        // safety: disconnect photon if still connected during shutdown
        if (GlobalPlaymodeCleanup.IsQuitting && PhotonNetwork.IsConnected)
        {
            try { PhotonNetwork.Disconnect(); } catch { }
        }
    }

    private void OnApplicationQuit()
    {
        // Ensure Photon is fully disconnected when the application exits so
        // no stale room / player state carries over into the next run.
        ForceDisconnectAndCleanup("[NetworkManager] OnApplicationQuit");
    }
    
    void Start()
    {
        PhotonNetwork.AutomaticallySyncScene = true;
        // log Photon Server Settings to help debug build/connect issues (AppId presence, master-server usage)
        try
        {
            var ss = Photon.Pun.PhotonNetwork.PhotonServerSettings;
            if (ss != null && ss.AppSettings != null)
            {
                Debug.Log($"NetworkManager: PhotonServerSettings found. AppIdRealtime: '{ss.AppSettings.AppIdRealtime}' IsMasterServerAddress: {ss.AppSettings.IsMasterServerAddress}");
            }
            else
            {
                Debug.LogWarning("NetworkManager: PhotonServerSettings or AppSettings is null. Check PhotonServerSettings asset in Resources/PhotonServerSettings.asset");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("NetworkManager: Exception while reading PhotonServerSettings: " + ex.Message);
        }

        // dev-friendly default: offline unless explicitly connecting
        if (!autoConnectOnStart)
        {
            if (startInOfflineModeWhenNotAutoConnecting && !PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            {
                PhotonNetwork.OfflineMode = true;
                Debug.Log("NetworkManager: Started in Offline Mode (dev)");
                // spawn immediately for singleplayer
                SpawnPlayer();
            }
            return;
        }

        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.InRoom)
        {
            Debug.Log("[NetworkManager] Connected to Photon but not in room yet; waiting for room join.");
            return;
        }

        if (!PhotonNetwork.IsConnected)
        {
            PhotonNetwork.GameVersion = gameVersion;
            PhotonNetwork.ConnectUsingSettings();
        }
    }

    void Update()
    {
        if (!enableRuntimeDebugOverlay)
            return;

        if (Input.GetKeyDown(debugOverlayToggleKey))
        {
            isDebugOverlayVisible = !isDebugOverlayVisible;
            AppendRuntimeLog($"[NetworkManager] Debug overlay {(isDebugOverlayVisible ? "enabled" : "disabled")}");
        }
    }

    private void OnGUI()
    {
        if (!enableRuntimeDebugOverlay || !isDebugOverlayVisible)
            return;

        EnsureDebugOverlayStyles();

        float width = Mathf.Clamp(debugOverlayWidth, 300f, Screen.width - 20f);
        float height = Mathf.Clamp(debugOverlayHeight, 120f, Screen.height - 20f);
        Rect panelRect = new Rect(10f, Screen.height - height - 10f, width, height);
        GUI.Box(panelRect, GUIContent.none);

        Rect titleRect = new Rect(panelRect.x + 8f, panelRect.y + 6f, panelRect.width - 16f, 20f);
        GUI.Label(titleRect, "Debug Log (F2)", debugOverlayTitleStyle);

        string[] lines = runtimeLogBuffer.ToArray();
        int start = Mathf.Max(0, lines.Length - Mathf.Max(1, debugOverlayVisibleLines));

        float lineHeight = 18f;
        Rect lineRect = new Rect(panelRect.x + 8f, panelRect.y + 28f, panelRect.width - 16f, lineHeight);
        for (int i = start; i < lines.Length; i++)
        {
            if (lineRect.yMax > panelRect.yMax - 4f)
                break;

            GUI.Label(lineRect, lines[i], debugOverlayTextStyle);
            lineRect.y += lineHeight;
        }
    }

    private void EnsureDebugOverlayStyles()
    {
        if (debugOverlayTextStyle == null)
        {
            debugOverlayTextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Color.white },
                richText = false,
                wordWrap = false
            };
        }

        if (debugOverlayTitleStyle == null)
        {
            debugOverlayTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
        }
    }

    private void OnRuntimeLogReceived(string condition, string stackTrace, LogType type)
    {
        if (!enableRuntimeDebugOverlay)
            return;

        string levelTag = type == LogType.Error || type == LogType.Assert || type == LogType.Exception
            ? "ERR"
            : (type == LogType.Warning ? "WRN" : "LOG");

        string sanitized = string.IsNullOrEmpty(condition)
            ? string.Empty
            : condition.Replace('\n', ' ').Replace('\r', ' ');

        debugOverlayLineBuilder.Clear();
        debugOverlayLineBuilder
            .Append('[')
            .Append(DateTime.Now.ToString("HH:mm:ss"))
            .Append("] ")
            .Append(levelTag)
            .Append(": ")
            .Append(sanitized);

        AppendRuntimeLog(debugOverlayLineBuilder.ToString());
    }

    private void AppendRuntimeLog(string line)
    {
        if (string.IsNullOrEmpty(line))
            return;

        runtimeLogBuffer.Enqueue(line);
        int max = Mathf.Max(20, debugOverlayBufferSize);
        while (runtimeLogBuffer.Count > max)
            runtimeLogBuffer.Dequeue();
    }

    // optional: allow singleplayer/offline sessions without a network connection
    public void StartOfflineSession()
    {
        if (PhotonNetwork.IsConnected)
        {
            Debug.LogWarning("Cannot start offline session while connected. Leave room/disconnect first.");
            return;
        }
        PhotonNetwork.OfflineMode = true;
        Debug.Log("Offline mode enabled. Spawning local player.");
        SpawnPlayer();
    }
    
    #region Photon Callbacks
    
    public override void OnConnectedToMaster()
    {
        Debug.Log("Connected to Photon Master Server");
        ConnectedToMasterEvent?.Invoke();
    }
    
    public override void OnJoinedRoom()
    {
        Debug.Log($"Joined room: {PhotonNetwork.CurrentRoom.Name}");
        JoinedRoomEvent?.Invoke();
        
        // optional immediate spawn; disabled by default to avoid overriding cutscene/tutorial spawn flows
        if (autoSpawnOnJoinedRoom)
        {
            SpawnPlayer();
        }
        
        // Sync game state
        if (PhotonNetwork.IsMasterClient)
        {
            SyncGameState();
        }
    }
    
    public override void OnLeftRoom()
    {
        Debug.Log("Left room");
        LeftRoomEvent?.Invoke();
        
        // Perform cleanup on room leave
        if (MemoryCleanupManager.Instance != null)
        {
            MemoryCleanupManager.Instance.CleanupOnRoomLeave();
        }
        
        // Stop all coroutines
        StopAllCoroutines();
        
        // Clear event subscriptions to prevent leaks
        ClearEventSubscriptions();
    }
    
    private void ClearEventSubscriptions()
    {
        // Clear all event handlers
        ConnectedToMasterEvent = null;
        JoinedRoomEvent = null;
        LeftRoomEvent = null;
        OnPlayerJoined = null;
        OnPlayerLeft = null;
        OnGameStarted = null;
        OnGamePaused = null;
        OnGameResumed = null;
    }
    
    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.Log($"Disconnected from Photon: {cause}");
        
        // Perform cleanup on disconnect
        if (MemoryCleanupManager.Instance != null)
        {
            MemoryCleanupManager.Instance.CleanupOnRoomLeave();
        }
        
        // Stop all coroutines
        StopAllCoroutines();
        
        // Clear event subscriptions
        ClearEventSubscriptions();
    }
    
    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"Player {newPlayer.NickName} joined the room");
        OnPlayerJoined?.Invoke(newPlayer);
        
        // Sync game state with new player
        if (PhotonNetwork.IsMasterClient)
        {
            SyncGameState();
        }
    }
    
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.Log($"Player {otherPlayer.NickName} left the room");
        OnPlayerLeft?.Invoke(otherPlayer);
    }
    
    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        Debug.Log($"Master client switched to: {newMasterClient.NickName}");
        
        // Sync game state with new master
        if (PhotonNetwork.IsMasterClient)
        {
            SyncGameState();
        }
    }
    
    #endregion
    
    #region Player Management
    
    public void SpawnPlayer()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("Player prefab not assigned!");
            return;
        }

        // prevent duplicate local spawns
        var existingLocal = PlayerSpawnCoordinator.FindLocalPlayer();
        if (existingLocal != null)
        {
            Debug.Log("[NetworkManager] Local player already exists, skipping SpawnPlayer.");
            return;
        }
        
        Vector3 spawnPosition = GetSpawnPosition();
        GameObject prefabToSpawn = GetPlayerPrefabForSpawnIndex();

        // only use Photon instantiate when in a room (or offline mode).
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.InRoom)
        {
            Debug.LogWarning("[NetworkManager] SpawnPlayer requested while connected but not in room. Skipping Photon instantiate.");
            return;
        }

        GameObject player;
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            player = PhotonNetwork.Instantiate(prefabToSpawn.name, spawnPosition, Quaternion.identity);
        }
        else
        {
            player = Instantiate(prefabToSpawn, spawnPosition, Quaternion.identity);
        }

        player = EnsureSpawnedPlayerHasRequiredLocalUI(player, spawnPosition);
        
        Debug.Log($"Player spawned at: {spawnPosition}");
    }
    
    private int GetLocalSpawnIndex(int count)
    {
        return PlayerRegistry.GetLocalJoinOrderIndex(count);
    }

    private GameObject GetPlayerPrefabForSpawnIndex()
    {
        int index = GetLocalSpawnIndex(4);
        return GetPlayerPrefabByIndex(index);
    }

    private GameObject GetPlayerPrefabByIndex(int index)
    {
        GameObject selected = playerPrefab;
        switch (index)
        {
            case 1: selected = playerPrefab2 != null ? playerPrefab2 : playerPrefab; break;
            case 2: selected = playerPrefab3 != null ? playerPrefab3 : playerPrefab; break;
            case 3: selected = playerPrefab4 != null ? playerPrefab4 : playerPrefab; break;
            default: selected = playerPrefab; break;
        }

        // Safety fallback for variant prefabs that are missing local HUD/UI wiring.
        if (selected != null && playerPrefab != null && selected != playerPrefab && !HasRequiredPlayerComponents(selected))
        {
            Debug.LogWarning($"[NetworkManager] Selected prefab '{selected.name}' is missing required player components/UI. Falling back to '{playerPrefab.name}'.");
            return playerPrefab;
        }

        return selected;
    }

    private bool HasRequiredPlayerComponents(GameObject prefab)
    {
        if (prefab == null) return false;

        bool hasController = prefab.GetComponent<ThirdPersonController>() != null
            || prefab.GetComponentInChildren<ThirdPersonController>(true) != null;
        bool hasStats = prefab.GetComponent<PlayerStats>() != null
            || prefab.GetComponentInChildren<PlayerStats>(true) != null;
        bool hasInventoryUI = prefab.GetComponentInChildren<InventoryUI>(true) != null;
        bool hasQuestListUI = prefab.GetComponentInChildren<QuestListUI>(true) != null;

        return hasController && hasStats && hasInventoryUI && hasQuestListUI;
    }

    private GameObject EnsureSpawnedPlayerHasRequiredLocalUI(GameObject spawnedPlayer, Vector3 spawnPosition)
    {
        if (spawnedPlayer == null || playerPrefab == null)
            return spawnedPlayer;

        var pv = spawnedPlayer.GetComponent<PhotonView>();
        bool isLocalOwner = pv == null || pv.IsMine;
        if (!isLocalOwner)
            return spawnedPlayer;

        if (HasRequiredPlayerComponents(spawnedPlayer))
            return spawnedPlayer;

        Debug.LogWarning($"[NetworkManager] Spawned player '{spawnedPlayer.name}' is missing required local UI/gameplay components. Re-spawning with '{playerPrefab.name}'.");

        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            if (pv != null && pv.IsMine)
                PhotonNetwork.Destroy(spawnedPlayer);
            return PhotonNetwork.Instantiate(playerPrefab.name, spawnPosition, Quaternion.identity);
        }

        Destroy(spawnedPlayer);
        return Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
    }

    private Vector3 GetSpawnPosition()
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int spawnIndex = GetLocalSpawnIndex(spawnPoints.Length);
            if (spawnIndex < spawnPoints.Length)
            {
                return spawnPoints[spawnIndex].position;
            }
        }
        
        // Default spawn position
        return Vector3.zero;
    }
    
    #endregion

    #region Scene Transition

    public static bool BeginSceneTransition(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
            return false;

        if (Instance == null)
        {
            Debug.LogWarning($"[NetworkManager] BeginSceneTransition('{sceneName}') failed: no NetworkManager instance.");
            return false;
        }

        return Instance.BeginSceneTransitionInternal(sceneName);
    }

    private bool BeginSceneTransitionInternal(string sceneName)
    {
        if (isSceneTransitionInProgress)
        {
            Debug.LogWarning($"[NetworkManager] Transition already in progress to '{transitionTargetScene}'. Ignoring '{sceneName}'.");
            return false;
        }

        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.InRoom)
        {
            Debug.LogError($"[NetworkManager] Cannot transition to '{sceneName}' while connected but not in room.");
            return false;
        }

        StartCoroutine(Co_BeginSceneTransition(sceneName));
        return true;
    }

    private IEnumerator Co_BeginSceneTransition(string sceneName)
    {
        isSceneTransitionInProgress = true;
        transitionTargetScene = sceneName;
        PlayerSpawnManager.hasTeleportedByLoader = false;
        CutsceneManager.SetTransitionControlledStart(true);
        PlayerSpawnCoordinator.CleanupStaleLocalPlayersOutsideActiveScene(enableDebugLogs: true, logPrefix: "[NetworkManagerTransition]");

        Inventory.CacheLocalInventory();
        EquipmentManager.CacheLocalEquipment();

        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && PhotonNetwork.InRoom)
        {
            // reset local transition flags for this handoff
            if (PhotonNetwork.LocalPlayer != null)
            {
                var clear = new Hashtable
                {
                    { TRANSITION_SCENE_READY_KEY, null },
                    { TRANSITION_SPAWN_READY_KEY, null }
                };
                PhotonNetwork.LocalPlayer.SetCustomProperties(clear);
            }

            if (PhotonNetwork.IsMasterClient)
            {
                PhotonNetwork.LoadLevel(sceneName);
            }

            float loadTimeout = 90f;
            float elapsed = 0f;
            while (SceneManager.GetActiveScene().name != sceneName && elapsed < loadTimeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (SceneManager.GetActiveScene().name != sceneName)
            {
                Debug.LogError($"[NetworkManager] Timed out waiting for scene '{sceneName}'.");
                isSceneTransitionInProgress = false;
                yield break;
            }

            yield return StartCoroutine(Co_MarkAndWaitAllPlayers(TRANSITION_SCENE_READY_KEY, 45f));
        }
        else
        {
            SceneManager.LoadScene(sceneName);
            while (SceneManager.GetActiveScene().name != sceneName)
                yield return null;
        }

        yield return null;
        PlayerSpawnCoordinator.CleanupStaleLocalPlayersOutsideActiveScene(enableDebugLogs: true, logPrefix: "[NetworkManagerTransition]");

        CutsceneManager startCutscene = FindStartSceneCutsceneManager();
        if (startCutscene != null)
        {
            startCutscene.BeginStartSceneSequence();
            float maxWait = 120f;
            float elapsed = 0f;
            while (startCutscene != null && !startCutscene.IsStartSequenceComplete && elapsed < maxWait)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            yield return PlayerSpawnCoordinator.EnsureLocalPlayerAtSpawn(
                maxWaitSeconds: 20f,
                waitForSpawnMarkers: true,
                enableDebugLogs: true,
                logPrefix: "[NetworkManagerTransition]");
        }

        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && PhotonNetwork.InRoom)
        {
            yield return StartCoroutine(Co_MarkAndWaitAllPlayers(TRANSITION_SPAWN_READY_KEY, 45f));
            if (PhotonNetwork.LocalPlayer != null)
            {
                var clear = new Hashtable
                {
                    { TRANSITION_SCENE_READY_KEY, null },
                    { TRANSITION_SPAWN_READY_KEY, null }
                };
                PhotonNetwork.LocalPlayer.SetCustomProperties(clear);
            }
        }

        // Defensive finalization: ensure local gameplay input is restored after scene handoff.
        // This covers stuck lock-owner cases (e.g., Tutorial / transition tokens lingering on host).
        yield return null;
        RestoreLocalGameplayAfterTransition();

        isSceneTransitionInProgress = false;
        transitionTargetScene = string.Empty;
    }

    /// <summary>
    /// Offline / not-in-room scene load from dev or pause escape panel.
    /// Runs spawn coordination and input/HUD restore so the local player initializes correctly.
    /// Do not use while in an online Photon room — use <see cref="BeginSceneTransition"/> instead.
    /// </summary>
    public void BeginLocalDevSceneLoad(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
            return;

        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
        {
            Debug.LogWarning("[NetworkManager] BeginLocalDevSceneLoad ignored while in an online room. Use BeginSceneTransition from the host instead.");
            return;
        }

        StartCoroutine(Co_LocalDevSceneLoadFromPauseMenu(sceneName));
    }

    private IEnumerator Co_LocalDevSceneLoadFromPauseMenu(string sceneName)
    {
        PlayerSpawnManager.hasTeleportedByLoader = false;
        CutsceneManager.SetTransitionControlledStart(false);

        Inventory.CacheLocalInventory();
        EquipmentManager.CacheLocalEquipment();

        SceneManager.LoadScene(sceneName);

        float elapsed = 0f;
        const float loadTimeout = 60f;
        while (SceneManager.GetActiveScene().name != sceneName && elapsed < loadTimeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (SceneManager.GetActiveScene().name != sceneName)
        {
            Debug.LogError($"[NetworkManager] Dev local load timed out waiting for scene '{sceneName}'.");
            yield break;
        }

        yield return null;
        yield return null;

        PlayerSpawnCoordinator.CleanupStaleLocalPlayersOutsideActiveScene(
            enableDebugLogs: true,
            logPrefix: "[NetworkManager][DevLocalLoad]");

        CutsceneManager startCutscene = FindStartSceneCutsceneManager();
        if (startCutscene != null)
        {
            float maxWait = 120f;
            float t = 0f;
            while (startCutscene != null && !startCutscene.IsStartSequenceComplete && t < maxWait)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            yield return PlayerSpawnCoordinator.EnsureLocalPlayerAtSpawn(
                maxWaitSeconds: 25f,
                waitForSpawnMarkers: true,
                enableDebugLogs: true,
                logPrefix: "[NetworkManager][DevLocalLoad]");
        }

        yield return null;
        RestoreLocalGameplayAfterTransition();
    }

    private void RestoreLocalGameplayAfterTransition()
    {
        var locker = LocalInputLocker.Ensure();
        if (locker != null)
        {
            locker.ReleaseAllForOwner("Tutorial");
            locker.ReleaseAllForOwner("QuestCutscene");
            locker.ReleaseAllForOwner("AreaCutscene");
            locker.ReleaseAllForOwner("PostQuestSceneTransition");
            locker.ReleaseAllForOwner("CutsceneManager");
            locker.ReleaseAllForOwner("PauseMenu");
            locker.ReleaseAllForOwner("NPCDialogue");
            locker.ReleaseAllForOwner("NunoShop");
            locker.ForceGameplayCursor();
        }

        GameObject localPlayer = PlayerSpawnCoordinator.FindLocalPlayer();
        if (localPlayer == null)
            return;

        var controller = localPlayer.GetComponent<ThirdPersonController>() ?? localPlayer.GetComponentInChildren<ThirdPersonController>(true);
        if (controller != null)
        {
            controller.SetCanMove(true);
            controller.SetCanControl(true);
        }

        var combat = localPlayer.GetComponent<PlayerCombat>() ?? localPlayer.GetComponentInChildren<PlayerCombat>(true);
        if (combat != null)
        {
            combat.enabled = true;
            combat.SetCanControl(true);
        }
    }

    private IEnumerator Co_MarkAndWaitAllPlayers(string key, float timeout)
    {
        if (PhotonNetwork.LocalPlayer != null)
        {
            var props = new Hashtable { { key, true } };
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        }

        float elapsed = 0f;
        while (elapsed < timeout)
        {
            if (AreAllPlayersFlagged(key))
                yield break;

            elapsed += 0.25f;
            yield return new WaitForSeconds(0.25f);
        }
        Debug.LogWarning($"[NetworkManager] Timed out waiting for all players on '{key}'. Continuing.");
    }

    private bool AreAllPlayersFlagged(string key)
    {
        if (PhotonNetwork.CurrentRoom == null) return true;
        var players = PhotonNetwork.PlayerList;
        if (players == null || players.Length == 0) return true;

        for (int i = 0; i < players.Length; i++)
        {
            var p = players[i];
            if (p == null || p.CustomProperties == null || !p.CustomProperties.ContainsKey(key))
                return false;
            if (!(bool)p.CustomProperties[key])
                return false;
        }

        return true;
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

    #endregion
    
    #region Game State Management
    
    public void StartGame()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("Only master client can start the game!");
            return;
        }
        
        isGameStarted = true;
        Debug.Log("Game started!");
        OnGameStarted?.Invoke();
        
        // Sync with all players (buffered for late-joiners and offline parity)
        photonView.RPC("RPC_StartGame", RpcTarget.AllBuffered);
    }
    
    [PunRPC]
    public void RPC_StartGame()
    {
        isGameStarted = true;
        OnGameStarted?.Invoke();
    }
    
    public void PauseGame()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("Only master client can pause the game!");
            return;
        }
        
        isGamePaused = true;
        Debug.Log("Game paused!");
        OnGamePaused?.Invoke();
        
        // Sync with all players (buffered)
        photonView.RPC("RPC_PauseGame", RpcTarget.AllBuffered);
    }
    
    [PunRPC]
    public void RPC_PauseGame()
    {
        isGamePaused = true;
        OnGamePaused?.Invoke();
    }
    
    public void ResumeGame()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("Only master client can resume the game!");
            return;
        }
        
        isGamePaused = false;
        Debug.Log("Game resumed!");
        OnGameResumed?.Invoke();
        
        // Sync with all players (buffered)
        photonView.RPC("RPC_ResumeGame", RpcTarget.AllBuffered);
    }
    
    [PunRPC]
    public void RPC_ResumeGame()
    {
        isGamePaused = false;
        OnGameResumed?.Invoke();
    }
    
    private void SyncGameState()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        // Sync quest state
        if (questManager != null)
        {
            photonView.RPC("RPC_SyncQuestState", RpcTarget.AllBuffered, questManager.currentQuestIndex);
        }
        
        // Sync shrine state
        if (shrineManager != null)
        {
            // Sync shrine states if needed
        }
        
        // Sync moveset state
        if (movesetManager != null)
        {
            string movesetName = movesetManager.CurrentMoveset != null ? movesetManager.CurrentMoveset.movesetName : "";
            photonView.RPC("RPC_SyncMovesetState", RpcTarget.AllBuffered, movesetName);
        }
    }
    
    [PunRPC]
    public void RPC_SyncQuestState(int questIndex)
    {
        if (questManager != null)
        {
            questManager.StartQuest(questIndex);
        }
    }
    
    [PunRPC]
    public void RPC_SyncMovesetState(string movesetName)
    {
        if (movesetManager != null && !string.IsNullOrEmpty(movesetName))
        {
            movesetManager.SetMoveset(movesetManager.GetMovesetByName(movesetName));
        }
    }
    
    #endregion
    
    #region Room Management
    
    public void CreateRoom(string roomName)
    {
        RoomOptions roomOptions = new RoomOptions
        {
            MaxPlayers = maxPlayersPerRoom,
            IsVisible = true,
            IsOpen = true
        };
        
        PhotonNetwork.CreateRoom(roomName, roomOptions);
    }
    
    // convenience: connect online (disabling OfflineMode) then create the room
    public void ConnectAndCreateRoom(string roomName)
    {
        StartCoroutine(Co_ConnectThen(() => CreateRoom(roomName)));
    }

    public void JoinRoom(string roomName)
    {
        PhotonNetwork.JoinRoom(roomName);
    }

    public void ConnectAndJoinRoom(string roomName)
    {
        StartCoroutine(Co_ConnectThen(() => JoinRoom(roomName)));
    }
    
    public void LeaveRoom()
    {
        PhotonNetwork.LeaveRoom();
    }

    /// <summary>
    /// Centralized "hard reset" for Photon state used by exit flows.
    /// Ensures we leave any room, disconnect, stop coroutines and clear events.
    /// Safe to call multiple times.
    /// </summary>
    public static void ForceDisconnectAndCleanup(string caller = "")
    {
        if (Instance != null)
        {
            Instance.InternalForceDisconnectAndCleanup(caller);
        }
        else
        {
            if (!string.IsNullOrEmpty(caller))
            {
                Debug.Log($"[NetworkManager] ForceDisconnectAndCleanup (no instance) from {caller}");
            }
            if (PhotonNetwork.IsConnected)
            {
                PhotonNetwork.Disconnect();
            }
        }
    }

    private void InternalForceDisconnectAndCleanup(string caller)
    {
        if (!string.IsNullOrEmpty(caller))
        {
            Debug.Log($"[NetworkManager] ForceDisconnectAndCleanup called from {caller}");
        }

        // Leave room first (if any), then disconnect.
        if (PhotonNetwork.InRoom)
        {
            try { PhotonNetwork.LeaveRoom(); } catch { }
        }

        if (PhotonNetwork.IsConnected)
        {
            try { PhotonNetwork.Disconnect(); } catch { }
        }

        // Stop any running network-related coroutines and clear callbacks.
        StopAllCoroutines();
        ClearEventSubscriptions();
    }
    
    #endregion
    
    #region Utility Methods
    
    public bool IsConnected()
    {
        return PhotonNetwork.IsConnected;
    }
    
    public bool IsInRoom()
    {
        return PhotonNetwork.InRoom;
    }
    
    public bool IsMasterClient()
    {
        return PhotonNetwork.IsMasterClient;
    }
    
    public Player[] GetPlayers()
    {
        return PhotonNetwork.PlayerList;
    }
    
    public int GetPlayerCount()
    {
        return PhotonNetwork.PlayerList.Length;
    }
    
    public string GetRoomName()
    {
        return PhotonNetwork.CurrentRoom?.Name ?? "";
    }

    public bool IsNetworkReady()
    {
        return PhotonNetwork.InRoom || PhotonNetwork.OfflineMode;
    }

    private System.Collections.IEnumerator Co_ConnectThen(System.Action onReady)
    {
        if (PhotonNetwork.OfflineMode) PhotonNetwork.OfflineMode = false;
        if (!PhotonNetwork.IsConnected)
        {
            PhotonNetwork.GameVersion = gameVersion;
            PhotonNetwork.ConnectUsingSettings();
            yield return new WaitUntil(() => PhotonNetwork.IsConnectedAndReady);
        }
        onReady?.Invoke();
    }
    
    #endregion
    
    #region Chat System
    
    public void SendChatMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        
        string formattedMessage = $"[{PhotonNetwork.LocalPlayer.NickName}]: {message}";
        photonView.RPC("RPC_ChatMessage", RpcTarget.AllBuffered, formattedMessage);
    }
    
    [PunRPC]
    public void RPC_ChatMessage(string message)
    {
        // Handle chat message display
        Debug.Log($"Chat: {message}");
        
        // You can integrate this with a UI chat system
        // For example: ChatUI.Instance.DisplayMessage(message);
    }
    
    #endregion
    
    #region Item and Quest Sync
    
    public void SyncItemPickup(string itemName, int quantity)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        photonView.RPC("RPC_SyncItemPickup", RpcTarget.AllBuffered, itemName, quantity);
    }
    
    [PunRPC]
    public void RPC_SyncItemPickup(string itemName, int quantity)
    {
        // Handle item pickup sync
        Debug.Log($"Item pickup synced: {quantity}x {itemName}");
    }
    
    public void SyncQuestProgress(int questIndex, int objectiveIndex, int progress)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        
        photonView.RPC("RPC_SyncQuestProgress", RpcTarget.AllBuffered, questIndex, objectiveIndex, progress);
    }
    
    [PunRPC]
    public void RPC_SyncQuestProgress(int questIndex, int objectiveIndex, int progress)
    {
        // Handle quest progress sync
        Debug.Log($"Quest progress synced: Quest {questIndex}, Objective {objectiveIndex}, Progress {progress}");
    }
    
    #endregion
}

