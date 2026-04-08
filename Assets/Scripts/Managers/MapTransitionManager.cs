using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Photon.Pun;
using ExitGames.Client.Photon;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Handles preloading and transitioning to the next gameplay map in a multiplayer-safe way.
/// Flow:
///  - Called (via QuestManager RPC) after the LAST quest's completion cutscene has fully finished.
///  - Still in the current scene, shows a loading panel and begins AsyncOperation to load the next scene.
///  - Keeps allowSceneActivation = false until loading reaches ~90%.
///  - When ready, allows activation so the new map becomes active.
///  - In the new scene, ensures a local player exists (spawns if needed) and teleports them to a spawn marker.
///  - Hides the loading panel once the player is in place.
/// </summary>
public class MapTransitionManager : MonoBehaviourPunCallbacks
{
    public static MapTransitionManager Instance { get; private set; }

    [Header("Loading UI")]
    [Tooltip("Optional standalone loading panel; if null, will use SceneLoader.Instance.loadingPanel when available.")]
    public GameObject loadingPanel;
    [Tooltip("Optional status text to show during loading.")]
    public TMPro.TextMeshProUGUI loadingText;

    [Header("Settings")]
    [Tooltip("Minimum time (seconds) to keep the loading panel visible, to avoid instant flashes.")]
    public float minimumLoadingTime = 1.0f;
    [Tooltip("Enable debug logs for map transitions.")]
    public bool enableDebugLogs = true;
    [Tooltip("Sorting order used for loading canvases so loading stays above gameplay HUD.")]
    public int loadingCanvasSortOrder = 50000;

    [Header("Multiplayer Sync")]
    [Tooltip("Wait for all players in the room to finish loading before activating the scene")]
    public bool waitForAllPlayers = true;

    [Header("Exit Button")]
    [Tooltip("Button shown on loading panel after a delay — lets the player return to the homescreen")]
    public GameObject exitButton;
    [Tooltip("Seconds to wait before showing the exit button (0 = always visible)")]
    public float exitButtonDelay = 10f;
    [Tooltip("Scene name to load when the exit button is pressed")]
    public string homescreenSceneName = "HOMESCREEN";

    private bool isTransitioning = false;
    private GameObject runtimeLoadingCanvas;
    private GameObject runtimeLoadingPanel;
    private TextMeshProUGUI runtimeLoadingText;
    private readonly Dictionary<Canvas, bool> suppressedCanvasStates = new Dictionary<Canvas, bool>();
    private bool areGameplayCanvasesSuppressed = false;
    private Coroutine loadingDotsCoroutine;
    private Coroutine exitButtonDelayCoroutine;
    private bool isExiting = false;
    private const string MAP_READY_KEY = "MapTransitionReady";

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        StopLoadingDotsAnim();
        HideExitButton();

        // skip if we already cleaned up during exit
        if (!isExiting && PhotonNetwork.IsConnectedAndReady && PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer != null)
        {
            var props = new Hashtable { { MAP_READY_KEY, null } };
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        }

        RestoreSuppressedGameplayCanvases();
    }

    public static void BeginTransition(string nextSceneName)
    {
        if (string.IsNullOrEmpty(nextSceneName))
            return;

        var mgr = EnsureInstance();
        if (mgr.isTransitioning)
            return;

        // show blocking loading UI immediately on the same frame transition starts
        mgr.ShowLoadingPanel("Loading next area");
        mgr.StartLoadingDotsAnim("Loading next area");
        mgr.SetupExitButton();

        mgr.StartCoroutine(mgr.Co_TransitionToScene(nextSceneName));
    }

    public static void ShowPreTransitionLoading(string message = "Loading next area...")
    {
        var mgr = EnsureInstance();
        mgr.ShowLoadingPanel(string.IsNullOrEmpty(message) ? "Loading next area..." : message);
    }

    private static MapTransitionManager EnsureInstance()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("MapTransitionManager");
        return go.AddComponent<MapTransitionManager>();
    }

    private IEnumerator Co_TransitionToScene(string nextSceneName)
    {
        isTransitioning = true;
        PlayerSpawnManager.hasTeleportedByLoader = false;
        CutsceneManager.SetTransitionControlledStart(true);

        if (enableDebugLogs) Debug.Log($"[MapTransitionManager] Starting transition to '{nextSceneName}'");

        // Cache local inventory so it can be restored when the new player prefab spawns.
        Inventory.CacheLocalInventory();

        // Cache equipped item so it can be restored on the new player prefab.
        EquipmentManager.CacheLocalEquipment();

        // For room-owned generated objects, avoid pre-load destroy passes in network rooms.
        var cleanupManager = MemoryCleanupManager.EnsureInstance();
        bool isNetworkedRoomForCleanup = PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && PhotonNetwork.InRoom;
        if (!isNetworkedRoomForCleanup)
        {
            cleanupManager?.CleanupSceneTransitionObjects();
        }
        else if (enableDebugLogs)
        {
            Debug.Log("[MapTransitionManager] Skipping transition destroy cleanup in network room (room-owned generation flow).");
        }

        if (isNetworkedRoomForCleanup)
            yield return null;

        yield return null;

        // Show loading UI while we are still in the current scene.
        ShowLoadingPanel("Loading next area...");

        float startTime = Time.time;

        bool isNetworked = PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && PhotonNetwork.InRoom;

        if (isNetworked)
        {
            // networked: master calls PhotonNetwork.LoadLevel which sets the room
            // property. all clients with AutomaticallySyncScene then auto-load.
            // non-master clients simply wait for the auto-sync scene change.
            if (PhotonNetwork.IsMasterClient)
            {
                if (enableDebugLogs) Debug.Log($"[MapTransitionManager] Master calling PhotonNetwork.LoadLevel('{nextSceneName}')");
                PhotonNetwork.LoadLevel(nextSceneName);
            }
            else
            {
                if (enableDebugLogs) Debug.Log($"[MapTransitionManager] Non-master waiting for auto-sync to '{nextSceneName}'");
            }

            // wait for the target scene to become active
            float sceneTimeout = 60f;
            float sceneElapsed = 0f;
            while (SceneManager.GetActiveScene().name != nextSceneName && sceneElapsed < sceneTimeout)
            {
                sceneElapsed += Time.deltaTime;
                yield return null;
            }

            if (SceneManager.GetActiveScene().name != nextSceneName)
            {
                Debug.LogError($"[MapTransitionManager] Timed out waiting for scene '{nextSceneName}' to load after {sceneTimeout}s!");
                isTransitioning = false;
                HideLoadingPanel();
                yield break;
            }
        }
        else
        {
            // offline: load with activation control for smooth loading screen
            AsyncOperation op = SceneManager.LoadSceneAsync(nextSceneName);
            op.allowSceneActivation = false;

            while (!op.isDone && op.progress < 0.9f)
                yield return null;

            while (Time.time - startTime < minimumLoadingTime)
                yield return null;

            if (enableDebugLogs) Debug.Log($"[MapTransitionManager] Scene '{nextSceneName}' reached 90%, activating.");
            op.allowSceneActivation = true;

            while (!op.isDone)
                yield return null;
        }

        // ensure minimum loading panel time
        while (Time.time - startTime < minimumLoadingTime)
            yield return null;

        // small delay to let Awake/Start on the new scene complete
        yield return null;

        // wait for all players to finish loading before continuing
        if (waitForAllPlayers && !PhotonNetwork.OfflineMode && PhotonNetwork.InRoom)
        {
            yield return StartCoroutine(Co_WaitForAllPlayersReady());
        }

        // Keep blocking with loading UI until procedural resources are actually ready.
        StartLoadingDotsAnim("Generating map resources");
        ShowLoadingPanel("Generating map resources");
        yield return StartCoroutine(Co_WaitForMapResourcesReady());

        // If the destination scene has a start cutscene flow, play it first, then spawn.
        var startCutsceneManager = FindStartSceneCutsceneManager();
        if (startCutsceneManager != null)
        {
            StartLoadingDotsAnim("Starting intro cutscene");
            UpdateLoadingText("Starting intro cutscene");
            // hide loading so the cutscene is visible
            StopLoadingDotsAnim();
            HideExitButton();
            HideLoadingPanel();

            startCutsceneManager.BeginStartSceneSequence();
            yield return StartCoroutine(Co_WaitForStartCutsceneAndSpawn(startCutsceneManager));

            // fail-safe: ensure loading panel is off after intro cutscene + spawn sequence
            StopLoadingDotsAnim();
            HideExitButton();
            HideLoadingPanel();
        }
        else
        {
            // Ensure a local player exists in the new scene and place them at a map spawn marker.
            StartLoadingDotsAnim("Placing player");
            UpdateLoadingText("Placing player");
            yield return PlayerSpawnCoordinator.EnsureLocalPlayerAtSpawn(
                maxWaitSeconds: 20f,
                waitForSpawnMarkers: true,
                enableDebugLogs: enableDebugLogs,
                logPrefix: "[MapTransitionManager]");

            // Hide loading UI once the player is ready.
            StopLoadingDotsAnim();
            HideExitButton();
            HideLoadingPanel();
        }

        // Extra fail-safe in case any late frame re-enabled loading panel.
        StartCoroutine(Co_FinalizeLoadingHide());

        // Transition lock was acquired by QuestManager before map handoff.
        // Release it now that player spawn/teleport is complete.
        LocalInputLocker.Ensure()?.ReleaseAllForOwner("PostQuestSceneTransition");

        isTransitioning = false;
        if (enableDebugLogs) Debug.Log("[MapTransitionManager] Transition complete.");
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

    private IEnumerator Co_WaitForStartCutsceneAndSpawn(CutsceneManager cutsceneManager)
    {
        float elapsed = 0f;
        const float maxWait = 90f;

        while (elapsed < maxWait)
        {
            if (cutsceneManager == null)
            {
                // cutscene manager may destroy itself after spawn flow
                break;
            }

            if (cutsceneManager.IsStartSequenceComplete)
                break;

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (enableDebugLogs)
            Debug.Log("[MapTransitionManager] Start-scene cutscene/spawn sequence completed.");
    }

    private IEnumerator Co_WaitForMapResourcesReady()
    {
        float elapsed = 0f;
        const float maxWait = 30f;

        while (elapsed < maxWait)
        {
            // If this scene does not use procedural generation, continue immediately.
            var mapGenerator = FindFirstObjectByType<MapResourcesGenerator>();
            if (mapGenerator == null)
            {
                yield break;
            }

            bool terrainReady = false;
            var terrain = FindFirstObjectByType<Terrain>();
            if (terrain != null && terrain.terrainData != null)
            {
                terrainReady = true;
            }

            Transform[] markers = FindSpawnMarkers();
            bool markerReady = markers != null && markers.Length > 0;
            var shared = MapResourcesGenerator.GetSharedSpawnPositions();
            bool sharedReady = shared != null && shared.Count > 0;

            if (terrainReady && (markerReady || sharedReady))
            {
                if (enableDebugLogs) Debug.Log("[MapTransitionManager] Map resources ready for spawn.");
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (enableDebugLogs) Debug.LogWarning("[MapTransitionManager] Timed out waiting for map resources. Continuing with spawn fallback.");
    }

    private IEnumerator Co_EnsurePlayerAndTeleport()
{
    float elapsed = 0f;
    float maxWait = 20f; // give terrain/resources time to finish
    bool triedTutorialSpawner = false;
    bool triedNetworkManager = false;
    while (elapsed < maxWait)
    {
        GameObject player = FindLocalPlayer();
        // Step 1: ensure we HAVE a player instance in the new scene
        if (player == null)
        {
            if (!triedTutorialSpawner)
            {
                var spawnMgr = FindFirstObjectByType<TutorialSpawnManager>();
                if (spawnMgr != null)
                {
                    if (enableDebugLogs) Debug.Log("[MapTransitionManager] No local player, requesting spawn via TutorialSpawnManager.");
                    spawnMgr.SpawnPlayerForThisClient();
                    triedTutorialSpawner = true;
                }
            }
            else if (!triedNetworkManager)
            {
                var netMgr = NetworkManager.Instance != null ? NetworkManager.Instance : FindFirstObjectByType<NetworkManager>();
                if (netMgr != null)
                {
                    if (enableDebugLogs) Debug.Log("[MapTransitionManager] No local player, requesting NetworkManager.SpawnPlayer().");
                    netMgr.SpawnPlayer();
                    triedNetworkManager = true;
                }
            }
        }
        else
        {
            // Step 2: wait until spawn markers exist (MapResourcesGenerator finished)
            Transform[] markers = FindSpawnMarkers();
            if (markers != null && markers.Length > 0)
            {
                TeleportPlayerToSpawn(player);
                yield break;
            }
            else
            {
                if (enableDebugLogs) Debug.Log("[MapTransitionManager] Player exists but no spawn markers yet, waiting...");
            }
        }
        elapsed += Time.deltaTime;
        yield return null;
    }
    // Fallback: if we run out of time, just use the player's current position
    if (enableDebugLogs) Debug.LogWarning("[MapTransitionManager] Timed out waiting for player + spawn markers; leaving player where they are.");
}

    private GameObject FindLocalPlayer()
    {
        var stats = FindObjectsByType<PlayerStats>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var s in stats)
        {
            var pv = s.GetComponent<PhotonView>();
            if (pv == null || pv.IsMine)
                return s.gameObject;
        }
        // Fallback: tagged "Player"
        var go = GameObject.FindWithTag("Player");
        if (go != null)
        {
            var pv = go.GetComponent<PhotonView>();
            if (pv == null || pv.IsMine) return go;
        }
        return null;
    }

    private void TeleportPlayerToSpawn(GameObject player)
    {
        if (player == null) return;

        // Find spawn markers in the new scene (SpawnMarker_1, SpawnMarker_2, etc.)
        Transform[] spawnMarkers = FindSpawnMarkers();
        Vector3 spawnPosition = player.transform.position;

        if (spawnMarkers != null && spawnMarkers.Length > 0)
        {
            int index = 0;
            if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
            {
                int actor = PhotonNetwork.LocalPlayer.ActorNumber;
                index = Mathf.Clamp(actor - 1, 0, spawnMarkers.Length - 1);
            }
            if (index < spawnMarkers.Length && spawnMarkers[index] != null)
            {
                spawnPosition = spawnMarkers[index].position;
            }
        }

        var cc = player.GetComponent<CharacterController>();
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

        if (enableDebugLogs)
            Debug.Log($"[MapTransitionManager] Teleported player to {spawnPosition}");
    }

    private Transform[] FindSpawnMarkers()
    {
        var objs = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var list = new System.Collections.Generic.List<Transform>();
        foreach (var o in objs)
        {
            if (o != null && o.name.StartsWith("SpawnMarker_"))
                list.Add(o.transform);
        }
        list.Sort((a, b) => string.Compare(a.name, b.name));
        return list.Count > 0 ? list.ToArray() : null;
    }

    #region Loading Dots, Exit Button & Multiplayer Sync

    private void StartLoadingDotsAnim(string baseText)
    {
        StopLoadingDotsAnim();
        loadingDotsCoroutine = StartCoroutine(Co_AnimateLoadingDots(baseText));
    }

    private void StopLoadingDotsAnim()
    {
        if (loadingDotsCoroutine != null)
        {
            StopCoroutine(loadingDotsCoroutine);
            loadingDotsCoroutine = null;
        }
    }

    private IEnumerator Co_AnimateLoadingDots(string baseText)
    {
        string[] frames = { $"{baseText} .", $"{baseText} . .", $"{baseText} . . ." };
        int index = 0;
        while (true)
        {
            UpdateLoadingText(frames[index]);
            index = (index + 1) % frames.Length;
            yield return new WaitForSeconds(0.4f);
        }
    }

    private void SetupExitButton()
    {
        if (exitButton == null) return;
        exitButton.SetActive(false);
        var btn = exitButton.GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(OnExitToHomescreen);
        }
        exitButtonDelayCoroutine = StartCoroutine(Co_ShowExitButtonAfterDelay());
    }

    private IEnumerator Co_ShowExitButtonAfterDelay()
    {
        if (exitButton == null) yield break;
        yield return new WaitForSeconds(Mathf.Max(0f, exitButtonDelay));
        if (exitButton != null)
            exitButton.SetActive(true);
    }

    private void HideExitButton()
    {
        if (exitButtonDelayCoroutine != null)
        {
            StopCoroutine(exitButtonDelayCoroutine);
            exitButtonDelayCoroutine = null;
        }
        if (exitButton != null)
            exitButton.SetActive(false);
    }

    public void OnExitToHomescreen()
    {
        isExiting = true;
        StopAllCoroutines();
        loadingDotsCoroutine = null;
        exitButtonDelayCoroutine = null;

        if (exitButton != null) exitButton.SetActive(false);

        HideLoadingPanel();

        // clear ready property before disconnecting
        if (PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer != null)
        {
            var props = new Hashtable { { MAP_READY_KEY, null } };
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        }

        // enter menu mode so cursor is visible on homescreen
        var locker = LocalInputLocker.Ensure();
        if (locker != null)
            locker.EnterMenuMode();

        // disconnect and load homescreen (centralized cleanup so no stale Photon state remains)
        NetworkManager.ForceDisconnectAndCleanup("[MapTransitionManager] OnExitToHomescreen");

        isTransitioning = false;
        SceneManager.LoadScene(homescreenSceneName);
    }

    private IEnumerator Co_WaitForAllPlayersReady()
    {
        // mark local player as ready
        if (PhotonNetwork.LocalPlayer != null)
        {
            var props = new Hashtable { { MAP_READY_KEY, true } };
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        }

        // switch to "waiting for players" text
        StartLoadingDotsAnim("Waiting for other players");

        float timeout = 30f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            if (AllPlayersMapReady())
                break;

            elapsed += 0.25f;
            yield return new WaitForSeconds(0.25f);
        }

        if (elapsed >= timeout && enableDebugLogs)
            Debug.LogWarning("[MapTransitionManager] Timed out waiting for all players — proceeding anyway.");

        // switch back to loading text
        StartLoadingDotsAnim("Loading");
    }

    private bool AllPlayersMapReady()
    {
        if (PhotonNetwork.CurrentRoom == null) return true;

        var players = PhotonNetwork.PlayerList;
        if (players == null || players.Length == 0) return true;

        foreach (var player in players)
        {
            if (player.CustomProperties == null || !player.CustomProperties.ContainsKey(MAP_READY_KEY))
                return false;
            if (!(bool)player.CustomProperties[MAP_READY_KEY])
                return false;
        }
        return true;
    }

    #endregion

    private void ShowLoadingPanel(string message)
    {
        // Show SceneLoader panel if available
        if (SceneLoader.Instance != null && SceneLoader.Instance.loadingPanel != null)
        {
            EnsureActiveWithParents(SceneLoader.Instance.loadingPanel);
        }

        // Also show explicitly assigned transition panel when provided
        if (loadingPanel != null)
        {
            EnsureActiveWithParents(loadingPanel);
        }

        // If neither is configured, create runtime fallback overlay
        if ((SceneLoader.Instance == null || SceneLoader.Instance.loadingPanel == null) && loadingPanel == null)
        {
            EnsureRuntimeLoadingOverlay();
            if (runtimeLoadingPanel != null)
                runtimeLoadingPanel.SetActive(true);
        }

        ForceTopMostLoadingCanvases();
        SuppressGameplayCanvasesForLoading();

        UpdateLoadingText(message);
    }

    private void UpdateLoadingText(string message)
    {
        if (!string.IsNullOrEmpty(message))
        {
            if (loadingText != null)
                loadingText.text = message;
            else if (runtimeLoadingText != null)
                runtimeLoadingText.text = message;
        }
    }

    private void HideLoadingPanel()
    {
        if (SceneLoader.Instance != null)
        {
            SceneLoader.Instance.HideLoadingPanel();
        }

        ProceduralMapLoader.HideLoadingPanelExternal();

        if (SceneLoader.Instance != null && SceneLoader.Instance.loadingPanel != null)
        {
            SceneLoader.Instance.loadingPanel.SetActive(false);
        }
        if (loadingPanel != null)
            loadingPanel.SetActive(false);
        if (runtimeLoadingPanel != null)
            runtimeLoadingPanel.SetActive(false);
        if (runtimeLoadingCanvas != null)
            runtimeLoadingCanvas.SetActive(false);

        var staleFadeCanvas = GameObject.Find("CutsceneFadeCanvas");
        if (staleFadeCanvas != null)
            staleFadeCanvas.SetActive(false);

        ForceDisableLingeringLoadingObjects();
        RestoreSuppressedGameplayCanvases();
    }

    private void SuppressGameplayCanvasesForLoading()
    {
        if (areGameplayCanvasesSuppressed)
            return;

        suppressedCanvasStates.Clear();

        var allCanvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allCanvases.Length; i++)
        {
            var canvas = allCanvases[i];
            if (canvas == null)
                continue;

            if (IsCanvasPartOfLoadingUi(canvas))
                continue;

            suppressedCanvasStates[canvas] = canvas.enabled;
            canvas.enabled = false;
        }

        areGameplayCanvasesSuppressed = true;
    }

    private void RestoreSuppressedGameplayCanvases()
    {
        if (!areGameplayCanvasesSuppressed)
            return;

        foreach (var kvp in suppressedCanvasStates)
        {
            Canvas canvas = kvp.Key;
            if (canvas == null)
                continue;

            canvas.enabled = kvp.Value;
        }

        suppressedCanvasStates.Clear();
        areGameplayCanvasesSuppressed = false;
    }

    private bool IsCanvasPartOfLoadingUi(Canvas canvas)
    {
        if (canvas == null)
            return false;

        if (runtimeLoadingCanvas != null && canvas.transform.IsChildOf(runtimeLoadingCanvas.transform))
            return true;

        if (loadingPanel != null && canvas.transform.IsChildOf(loadingPanel.transform))
            return true;

        if (SceneLoader.Instance != null && SceneLoader.Instance.loadingPanel != null && canvas.transform.IsChildOf(SceneLoader.Instance.loadingPanel.transform))
            return true;

        string objectName = canvas.gameObject.name;
        if (!string.IsNullOrEmpty(objectName) && objectName.ToLowerInvariant().Contains("loading"))
            return true;

        return false;
    }

    private void ForceDisableLingeringLoadingObjects()
    {
        var allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allObjects.Length; i++)
        {
            var go = allObjects[i];
            if (go == null)
                continue;

            string n = go.name;
            if (string.IsNullOrEmpty(n))
                continue;

            bool looksLikeLoading =
                n.Equals("LoadingPanel") ||
                n.Equals("Loading Panel") ||
                n.Equals("MapTransitionLoadingCanvas") ||
                n.Equals("FirstMapLoadingCanvas") ||
                n.Equals("CutsceneFadeCanvas") ||
                n.ToLowerInvariant().Contains("loading panel");

            if (!looksLikeLoading)
                continue;

            // Keep this manager object alive; only disable its loading UI children.
            if (go == gameObject)
                continue;

            go.SetActive(false);
        }
    }

    private void EnsureActiveWithParents(GameObject panel)
    {
        if (panel == null) return;

        Transform t = panel.transform;
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
            t = t.parent;
        }

        panel.SetActive(true);

        // Ensure Canvas components are enabled so panel is actually rendered.
        var canvases = panel.GetComponentsInParent<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] == null) continue;
            canvases[i].enabled = true;
            canvases[i].overrideSorting = true;
            if (canvases[i].sortingOrder < loadingCanvasSortOrder)
                canvases[i].sortingOrder = loadingCanvasSortOrder;
        }
    }

    private void ForceTopMostLoadingCanvases()
    {
        if (loadingPanel != null)
        {
            var canvases = loadingPanel.GetComponentsInParent<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i] == null) continue;
                canvases[i].enabled = true;
                canvases[i].overrideSorting = true;
                canvases[i].sortingOrder = Mathf.Max(canvases[i].sortingOrder, loadingCanvasSortOrder);
            }
        }

        if (SceneLoader.Instance != null && SceneLoader.Instance.loadingPanel != null)
        {
            var canvases = SceneLoader.Instance.loadingPanel.GetComponentsInParent<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i] == null) continue;
                canvases[i].enabled = true;
                canvases[i].overrideSorting = true;
                canvases[i].sortingOrder = Mathf.Max(canvases[i].sortingOrder, loadingCanvasSortOrder);
            }
        }
    }

    private IEnumerator Co_FinalizeLoadingHide()
    {
        HideLoadingPanel();
        yield return null;
        HideLoadingPanel();
        yield return null;
        HideLoadingPanel();
    }

    private void EnsureRuntimeLoadingOverlay()
    {
        if (runtimeLoadingCanvas != null && runtimeLoadingPanel != null)
        {
            runtimeLoadingCanvas.SetActive(true);
            return;
        }

        runtimeLoadingCanvas = new GameObject("MapTransitionLoadingCanvas");
        DontDestroyOnLoad(runtimeLoadingCanvas);
        var canvas = runtimeLoadingCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767;
        runtimeLoadingCanvas.AddComponent<UnityEngine.UI.CanvasScaler>();
        runtimeLoadingCanvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        runtimeLoadingPanel = new GameObject("LoadingPanel");
        runtimeLoadingPanel.transform.SetParent(runtimeLoadingCanvas.transform, false);
        var panelRect = runtimeLoadingPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        var panelImage = runtimeLoadingPanel.AddComponent<UnityEngine.UI.Image>();
        panelImage.color = Color.black;

        var textObj = new GameObject("LoadingText");
        textObj.transform.SetParent(runtimeLoadingPanel.transform, false);
        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(700f, 80f);
        textRect.anchoredPosition = Vector2.zero;
        runtimeLoadingText = textObj.AddComponent<TextMeshProUGUI>();
        runtimeLoadingText.fontSize = 30f;
        runtimeLoadingText.alignment = TextAlignmentOptions.Center;
        runtimeLoadingText.color = Color.white;
        runtimeLoadingText.text = "Loading...";
    }
}

