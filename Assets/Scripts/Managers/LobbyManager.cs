using UnityEngine;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class LobbyManager : MonoBehaviourPunCallbacks
    // show singleplayer button on join/create error
{
    public GameObject lobbyPanel;
    public GameObject mainMenuPanel;
    public GameObject joinOrCreatePanel;
    public GameObject loadingPanel;
    private bool isLoadingTriggeredByUser = false;
    public Button startGameButton;
    public Button singleplayerButton; // assign in Unity for singleplayer mode
    public TextMeshProUGUI loadingStatusText;
    public Button continueButton;
    public Button readyButton; // restored readyButton
    public Button createGameButton;
    public Button joinGameButton;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI[] playerSlots;
    public TextMeshProUGUI roomCodeText;
    public TMP_InputField joinCodeInput;
    public TMP_InputField nameInput;

    [Header("connection options")]
    // number of seconds to wait for Photon to connect before falling back to offline mode
    public float connectionTimeoutSeconds = 5f;
    // if true, skip waiting and fallback to offline immediately when create is pressed and connection isn't ready
    public bool immediateOfflineFallback = false;

    [Header("offline UI")]
    // optional small toast text to inform player when fallback happens
    public TextMeshProUGUI offlineToastText;
    public float offlineToastDuration = 3f;

    public string startDialogueScene = "startDIALOGUE";
    private string pendingJoinCode = null;
    private string createdRoomCode = "";
    private bool isReady = false; // restored ready state
    private bool forceOffline = false; // runtime-detected offline fallback
    // connection flow helpers
    private bool shouldJoinLobbyOnConnect = false;
    private bool shouldCreateRoomOnConnect = false;
    private int currentReconnectAttempts = 0;
    private int maxReconnectAttempts = 3;
    private float reconnectBackoffSeconds = 2f;
    private bool triedProtocolFallback = false;
    private bool isCreatingRoom = false;

    // region fallback helpers
    private enum PhotonRegionFallbackState { None, Asia, HongKong, Best, Failed }
    private PhotonRegionFallbackState regionFallbackState = PhotonRegionFallbackState.None;

    // offline helpers
    bool HasInternet()
    {
        // simple reachability check; if behind captive portal or blocked, OnDisconnected will also trigger fallback
        return Application.internetReachability != NetworkReachability.NotReachable;
    }

        void ShowOfflineToast(string reason = null)
        {
            if (offlineToastText == null) return;
            string msg = string.IsNullOrEmpty(reason) ? "No connection — switched to Offline Mode." : $"No connection — switched to Offline Mode: {reason}";
            offlineToastText.text = msg;
            offlineToastText.gameObject.SetActive(true);
            StartCoroutine(Co_ShowOfflineToast());
        }
    void ActivateOfflineMode(string reason = null)
    {
        if (!PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.OfflineMode = true;
            Debug.Log("[Lobby] Activating Offline Mode" + (string.IsNullOrEmpty(reason) ? string.Empty : $": {reason}"));
        }
        forceOffline = true;
        if (statusText != null) statusText.text = "Offline Mode: starting solo session";
    if (loadingStatusText != null && isLoadingTriggeredByUser) loadingStatusText.text = "Offline Mode: creating local room...";
    ShowOfflineToast(reason);
    }

    [PunRPC]
    public void StartDialogueForAll()
    {
        // use SceneLoader when available so we show a loading UI during the transition
        if (SceneLoader.Instance != null)
        {
            SceneLoader.Instance.LoadScene(startDialogueScene);
        }
        else
        {
            // fallback to PhotonNetwork.LoadLevel if connected or OfflineMode
            if (PhotonNetwork.IsConnectedAndReady || PhotonNetwork.OfflineMode)
                PhotonNetwork.LoadLevel(startDialogueScene);
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene(startDialogueScene);
        }
    }

    void ShowLoading(string message)
    {
        if (!isLoadingTriggeredByUser) return;
        if (loadingPanel != null) loadingPanel.SetActive(true);
        if (loadingStatusText != null) loadingStatusText.text = message;
    }

    void HideLoading()
    {
        if (loadingPanel != null) loadingPanel.SetActive(false);
    }

    void Start()
    {
        isLoadingTriggeredByUser = false;
        HideLoading();
        ShowMainMenu();
        // do NOT auto-connect to Photon; only connect when user clicks create/join
        PhotonNetwork.Disconnect();
        PhotonNetwork.OfflineMode = false;
        startGameButton.interactable = false;
        ClearPlayerSlots();
        if (roomCodeText != null) roomCodeText.text = "";

        // Set name input max length to 10
        if (nameInput != null)
            nameInput.characterLimit = 10;

        // Add validation for name input
        if (nameInput != null && createGameButton != null && joinGameButton != null)
        {
            nameInput.onValueChanged.AddListener(OnNameInputChanged);
            OnNameInputChanged(nameInput.text);
        }
        HideLoading();
        if (offlineToastText != null)
        {
            offlineToastText.gameObject.SetActive(false);
        }
        // assign button listeners
        if (singleplayerButton != null)
            singleplayerButton.onClick.AddListener(OnSingleplayerClicked);
            
        // Do not persist LobbyManager across scenes to avoid PhotonView scene ID clashes
    }
    // called when singleplayer button is clicked
    public void OnSingleplayerClicked()
    {
        // properly disconnect first, then activate offline mode
        if (PhotonNetwork.IsConnected)
        {
            StartCoroutine(Co_DisconnectThenGoOffline());
        }
        else
        {
            ActivateOfflineMode("singleplayer selected");
            LoadStartDialogueScene();
        }
    }
    
    private IEnumerator Co_DisconnectThenGoOffline()
    {
        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.Disconnect();
            // wait for disconnect to complete
            while (PhotonNetwork.IsConnected)
            {
                yield return null;
            }
        }
        ActivateOfflineMode("singleplayer selected");
        LoadStartDialogueScene();
    }
    
    private void LoadStartDialogueScene()
    {
        if (joinOrCreatePanel != null) joinOrCreatePanel.SetActive(false);
        // use unified scene loading (works for both online and offline)
        if (SceneLoader.Instance != null)
        {
            SceneLoader.Instance.LoadScene(startDialogueScene);
        }
        else
        {
            // fallback to PhotonNetwork.LoadLevel if connected or OfflineMode
            if (PhotonNetwork.IsConnectedAndReady || PhotonNetwork.OfflineMode)
                PhotonNetwork.LoadLevel(startDialogueScene);
            else
                UnityEngine.SceneManagement.SceneManager.LoadScene(startDialogueScene);
        }
    }


    public void ShowMainMenu()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (joinOrCreatePanel != null) joinOrCreatePanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
    }

    public void ShowJoinOrCreatePanel()
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (joinOrCreatePanel != null) joinOrCreatePanel.SetActive(true);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
    }



    public void OnStartGameMenuClicked()
    {
        ShowJoinOrCreatePanel();
    }


    public void ShowLobbyPanel()
    {
        if (mainMenuPanel != null && mainMenuPanel.activeSelf) mainMenuPanel.SetActive(false);
        if (joinOrCreatePanel != null && joinOrCreatePanel.activeSelf) joinOrCreatePanel.SetActive(false);
        if (loadingPanel != null && loadingPanel.activeSelf) loadingPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
    }
    public void OnCreateGameMenuClicked()
    {
        // show loading panel only, hide all others
        if (mainMenuPanel != null && mainMenuPanel.activeSelf) mainMenuPanel.SetActive(false);
        if (joinOrCreatePanel != null && joinOrCreatePanel.activeSelf) joinOrCreatePanel.SetActive(false);
        if (lobbyPanel != null && lobbyPanel.activeSelf) lobbyPanel.SetActive(false);
        if (loadingPanel != null) loadingPanel.SetActive(true);
        if (loadingStatusText != null) loadingStatusText.text = "Connecting to Photon...";
        if (statusText != null) statusText.text = "Connecting to Photon...";
        HostLobby();
    }

    public void OnJoinGameMenuClicked()
    {
        // show loading panel only, hide all others
        if (mainMenuPanel != null && mainMenuPanel.activeSelf) mainMenuPanel.SetActive(false);
        if (joinOrCreatePanel != null && joinOrCreatePanel.activeSelf) joinOrCreatePanel.SetActive(false);
        if (lobbyPanel != null && lobbyPanel.activeSelf) lobbyPanel.SetActive(false);
        if (loadingPanel != null) loadingPanel.SetActive(true);
        if (loadingStatusText != null) loadingStatusText.text = "Connecting to Photon...";
        if (statusText != null) statusText.text = "Connecting to Photon...";
        OnJoinGamePanelJoinClicked();
    }

    public void OnOptionsClicked()
    {
        // Show options panel if you have one
    }

    public void OnExitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void OnBackFromLobby()
    {
    LeaveLobby();
    ShowMainMenu();
    }

    public void OnBackFromJoinOrCreate()
    {
        // go back to main menu and hide join/create panel
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (joinOrCreatePanel != null) joinOrCreatePanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        statusText.text = "Returned to main menu.";
    }

    public void OnNewGameClicked()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            Debug.Log("Host is starting the game. Loading startDIALOGUE for all players.");
            PhotonView photonView = PhotonView.Get(this);
            if (photonView == null)
            {
                Debug.LogError("PhotonView component missing on LobbyManager GameObject. Please add a PhotonView.");
                return;
            }
            photonView.RPC("StartDialogueForAll", RpcTarget.AllBuffered);
        }
    }

    void OnNameInputChanged(string value)
    {
        bool hasName = !string.IsNullOrEmpty(value);
        createGameButton.interactable = hasName;
        joinGameButton.interactable = hasName;
    }

    public void HostLobby()
    {
        if (nameInput != null && !string.IsNullOrEmpty(nameInput.text))
            PhotonNetwork.NickName = nameInput.text;
        else
            PhotonNetwork.NickName = "Player";

        // Unified host logic - works for both online and offline
        HostLobbyUnified();
    }
    
    private void HostLobbyUnified()
    {
        // if offline or no internet, don't try to connect; go back to previous screen and show singleplayer option
        if (!HasInternet())
        {
            ShowJoinOrCreatePanel();
            statusText.text = "No internet detected. Please try again or play singleplayer.";
            return;
        }

        // ensure we create the room once connected
        if (!PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            shouldCreateRoomOnConnect = true;
            statusText.text = "Connecting to Photon (Asia)...";
            PhotonNetwork.PhotonServerSettings.AppSettings.UseNameServer = true;
            PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = "asia";
            regionFallbackState = PhotonRegionFallbackState.Asia;
            PhotonNetwork.ConnectUsingSettings();
        }
        else if (PhotonNetwork.OfflineMode)
        {
            OnStartGameClicked();
        }
        else if (PhotonNetwork.IsConnectedAndReady)
        {
            OnStartGameClicked();
        }
        else
        {
            // fallback: not connected, not offline, not ready
            ShowJoinOrCreatePanel();
            statusText.text = "Connection error. Please try again or play singleplayer.";
        }
    }

    public void OnJoinGamePanelJoinClicked()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        isLoadingTriggeredByUser = true;
        ShowLoading("Joining Room...");
        if (nameInput != null && !string.IsNullOrEmpty(nameInput.text))
            PhotonNetwork.NickName = nameInput.text;
        else
            PhotonNetwork.NickName = "Player";

        // Unified join logic - works for both online and offline
        JoinRoomUnified();
    }
    
    private void JoinRoomUnified()
    {
        // if offline or no internet, fallback to solo session
        if (!HasInternet())
        {
            ActivateOfflineMode("no internet for joining");
            // create a local room and continue
            pendingJoinCode = null;
            OnStartGameClicked();
            return;
        }

        if (joinCodeInput != null && !string.IsNullOrEmpty(joinCodeInput.text))
        {
            pendingJoinCode = joinCodeInput.text;
            // request to join lobby once connected
            shouldJoinLobbyOnConnect = true;
            if (!PhotonNetwork.IsConnected)
            {
                statusText.text = "Connecting to Photon (Asia)...";
                PhotonNetwork.PhotonServerSettings.AppSettings.UseNameServer = true;
                PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = "asia";
                regionFallbackState = PhotonRegionFallbackState.Asia;
                PhotonNetwork.ConnectUsingSettings();
            }
            else
                PhotonNetwork.JoinLobby();
            if (statusText != null) statusText.text = "Connecting to lobby...";
        }
        else
        {
            ShowLoading("Please enter a lobby code.");
            StartCoroutine(ShowJoinOrCreatePanelWithDelay());
        }
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("[Lobby] OnConnectedToMaster called. Connected and ready for matchmaking.");
        ShowLoading("Connected to server!");
        statusText.text = "Connected to server!";
        currentReconnectAttempts = 0; // reset reconnect attempts on success
        UpdatePlayerList();

        // handle deferred actions requested while connecting
        if (shouldJoinLobbyOnConnect)
        {
            shouldJoinLobbyOnConnect = false;
            Debug.Log("[Lobby] Joining lobby after connect (deferred).");
            PhotonNetwork.JoinLobby();
        }
        // only trigger room creation if not already creating
        if (shouldCreateRoomOnConnect && !isCreatingRoom)
        {
            shouldCreateRoomOnConnect = false;
            Debug.Log("[Lobby] Creating room after connect (deferred).");
            OnStartGameClicked();
        }
    }

    public override void OnJoinedLobby()
    {
        if (!string.IsNullOrEmpty(pendingJoinCode))
        {
            ShowLoading("Joining room...");
            PhotonNetwork.JoinRoom(pendingJoinCode);
            if (statusText != null) statusText.text = "Joining room...";
            pendingJoinCode = null;
        }
        else
        {
            ShowLoading("No lobby code entered.");
            StartCoroutine(ShowJoinOrCreatePanelWithDelay());
        }
        startGameButton.interactable = true;
        if (roomCodeText != null) roomCodeText.text = "Lobby Code:";
        statusText.text = "In Lobby. Ready to start!";
    }

    public void OnStartGameClicked()
    {
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        isLoadingTriggeredByUser = true;
        // guard against re-entrancy and duplicate triggers
        if (isCreatingRoom || shouldCreateRoomOnConnect)
        {
            Debug.LogWarning("OnStartGameClicked called while already creating or waiting for connection.");
            return;
        }
        isCreatingRoom = true;

        // Unified room creation - works for both online and offline
        CreateRoomUnified();
    }
    
    private void CreateRoomUnified()
    {
        // If offline mode already set, create a local room immediately
        if (PhotonNetwork.OfflineMode)
        {
            ShowLoading("Creating Local Room...");
            statusText.text = "Creating Local Room...";
            string roomName = "OFFLINE";
            createdRoomCode = roomName;
            RoomOptions ro = new RoomOptions { MaxPlayers = 1 };
            PhotonNetwork.CreateRoom(roomName, ro, null);
            return;
        }

        // if connected to Photon already, proceed to create/join a networked room
        if (PhotonNetwork.IsConnectedAndReady)
        {
            ShowLoading("Creating Room...");
            statusText.text = "Creating Room...";
            string roomName = GenerateLobbyCode();
            createdRoomCode = roomName;
            PhotonNetwork.JoinOrCreateRoom(roomName, new RoomOptions { MaxPlayers = 4 }, TypedLobby.Default);
            return;
        }

        // not connected but we appear to have internet: attempt to connect and defer room creation
        if (HasInternet())
        {
            ShowLoading("Connecting to Photon... Creating room when ready...");
            statusText.text = "Connecting to Photon...";
            shouldCreateRoomOnConnect = true;
            // disable 'Connect to Best Server' (use region in settings)
            PhotonNetwork.PhotonServerSettings.AppSettings.UseNameServer = false;
            PhotonNetwork.ConnectUsingSettings();
            // start a short timeout: if connect doesn't happen, fallback to offline mode
            float t = immediateOfflineFallback ? 0f : connectionTimeoutSeconds;
            StartCoroutine(Co_WaitForConnectionThenFallback(t));
            return;
        }

        // no internet -> switch straight to offline mode and create a local room
        ActivateOfflineMode("no internet for creating room");
        // call CreateRoomUnified again; offline path will create the room
        isCreatingRoom = false; // reset before re-entering
        CreateRoomUnified();
    }

    string GenerateLobbyCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        System.Random random = new System.Random();
        char[] code = new char[6];
        for (int i = 0; i < code.Length; i++)
        {
            code[i] = chars[random.Next(chars.Length)];
        }
        return new string(code);
    }

    public override void OnJoinedRoom()
    {
        // If room is full, leave and show error
        if (!PhotonNetwork.OfflineMode && PhotonNetwork.CurrentRoom.PlayerCount > 4)
        {
            ShowLoading("Lobby is full. Returning to join panel...");
            PhotonNetwork.LeaveRoom();
            StartCoroutine(ShowJoinOrCreatePanelWithDelay());
            return;
        }
        StartCoroutine(ShowLobbyWithDelay());
    }

    private IEnumerator ShowLobbyWithDelay()
    {
        yield return new WaitForSeconds(0.5f);
        // creation finished (either online or offline) — allow subsequent create attempts
        isCreatingRoom = false;
        isLoadingTriggeredByUser = false;
        HideLoading();
        statusText.text = PhotonNetwork.OfflineMode ? "Joined Local Session." : "Joined Room! Waiting for players...";
        if (roomCodeText != null)
        {
            string code = !string.IsNullOrEmpty(createdRoomCode) ? createdRoomCode : PhotonNetwork.CurrentRoom.Name;
            roomCodeText.text = PhotonNetwork.OfflineMode ? "OFFLINE" : code;
            GUIUtility.systemCopyBuffer = code;
        }
        UpdatePlayerList();
        UpdateLobbyUI();
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
    }

    void UpdateLobbyUI()
    {
        bool isHost = PhotonNetwork.IsMasterClient;
        if (startGameButton != null) startGameButton.gameObject.SetActive(isHost);
        if (continueButton != null) continueButton.gameObject.SetActive(isHost);
        if (readyButton != null) readyButton.gameObject.SetActive(!isHost && !PhotonNetwork.OfflineMode);

        // update ready button text for local player
        if (readyButton != null)
        {
            var btnText = readyButton.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null)
            {
                bool localReady = PhotonNetwork.LocalPlayer.CustomProperties != null && PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("Ready") && (bool)PhotonNetwork.LocalPlayer.CustomProperties["Ready"];
                btnText.text = localReady ? "Unready" : "Ready";
            }
        }

        if (isHost && !PhotonNetwork.OfflineMode)
        {
            // Enable start if all joiners are ready, or if host is alone
            int joinerCount = PhotonNetwork.PlayerListOthers.Length;
            bool allJoinersReady = true;
            foreach (var player in PhotonNetwork.PlayerListOthers)
            {
                if (player.CustomProperties == null || !player.CustomProperties.ContainsKey("Ready") || !(bool)player.CustomProperties["Ready"])
                {
                    allJoinersReady = false;
                    break;
                }
            }
            // allow host to start if alone, regardless of ready state
            if (startGameButton != null) startGameButton.interactable = (joinerCount == 0) || allJoinersReady;
        }
        else if (PhotonNetwork.OfflineMode)
        {
            // always allow start in offline mode
            if (startGameButton != null) startGameButton.interactable = true;
        }
        else
        {
            if (startGameButton != null) startGameButton.interactable = false;
        }

        // Update player slots to show (READY) next to ready players
        UpdatePlayerList();
    }

    public void OnReadyClicked()
    {
        if (PhotonNetwork.LocalPlayer != null)
        {
            bool currentReady = PhotonNetwork.LocalPlayer.CustomProperties != null && PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("Ready") && (bool)PhotonNetwork.LocalPlayer.CustomProperties["Ready"];
            ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable();
            props["Ready"] = !currentReady;
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
            UpdateLobbyUI();
        }
    }
    private IEnumerator ShowJoinOrCreatePanelWithDelay()
    {
        yield return new WaitForSeconds(1.5f);
        HideLoading();
        ShowJoinOrCreatePanel();
        statusText.text = "Please enter a valid lobby code.";
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        UpdatePlayerList();
        UpdateLobbyUI();
    }



    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        UpdatePlayerList();
        UpdateLobbyUI();
    }

    // Photon callback: when a player's custom properties change (for example Ready flag)
    public override void OnPlayerPropertiesUpdate(Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
    {
        // refresh the UI so host and everyone else sees updated ready states
        UpdatePlayerList();
        UpdateLobbyUI();
    }

    void UpdatePlayerList()
{
    ClearPlayerSlots();
    var players = PhotonNetwork.PlayerList;
    for (int i = 0; i < playerSlots.Length; i++)
    {
        if (i < players.Length)
        {
            string name = !string.IsNullOrEmpty(players[i].NickName) ? players[i].NickName : ($"Player {i + 1}");
            bool ready = false;
            if (players[i].CustomProperties != null && players[i].CustomProperties.ContainsKey("Ready"))
            {
                object readyObj = players[i].CustomProperties["Ready"];
                if (readyObj is bool)
                    ready = (bool)readyObj;
                else if (readyObj is int)
                    ready = ((int)readyObj) != 0;
            }
            bool isHost = players[i].ActorNumber == PhotonNetwork.MasterClient.ActorNumber;
            string hostTag = isHost ? " (Host)" : "";
            string readyTag = ready ? " (READY)" : "";
            playerSlots[i].text = name + hostTag + readyTag;
            Debug.Log($"UpdatePlayerList: {name} host={isHost} ready={ready}");
        }
        else
        {
            playerSlots[i].text = "Waiting...";
        }
    }
}

    void ClearPlayerSlots()
    {
        if (playerSlots == null) return;
        foreach (var slot in playerSlots)
        {
            if (slot != null) slot.text = "Waiting...";
        }
        // show a toast message for offline fallback
        // showOfflineToast is used in ActivateOfflineMode and elsewhere
        ShowOfflineToast();
    }

    private IEnumerator Co_ShowOfflineToast()
    {
        yield return new WaitForSeconds(offlineToastDuration);
        if (offlineToastText != null)
        {
            offlineToastText.gameObject.SetActive(false);
        }
    }

    // leave the lobby and reset UI
    public void LeaveLobby()
    {
        isLoadingTriggeredByUser = false;
        HideLoading();
        ShowMainMenu();
        if (lobbyPanel != null) lobbyPanel.SetActive(false);
        if (loadingPanel != null) loadingPanel.SetActive(false);
        
        // Stop all coroutines
        StopAllCoroutines();
        
        // Cleanup before leaving room
        if (MemoryCleanupManager.Instance != null)
        {
            MemoryCleanupManager.Instance.CleanupProceduralGeneration();
        }
        
        if (PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode) PhotonNetwork.LeaveRoom();
        if (roomCodeText != null) roomCodeText.text = "";
        ClearPlayerSlots();
        createdRoomCode = "";
        pendingJoinCode = null;
    }
    
    void OnDestroy()
    {
        // Stop all coroutines
        StopAllCoroutines();

        // safety: disconnect photon if still connected during shutdown
        if (GlobalPlaymodeCleanup.IsQuitting && PhotonNetwork.IsConnected)
        {
            try { PhotonNetwork.Disconnect(); } catch { }
        }
    }

    // coroutine to wait for Photon connection, then fallback to offline mode if timeout
    private IEnumerator Co_WaitForConnectionThenFallback(float timeoutSeconds)
    {
        float start = Time.time;
        while (Time.time - start < timeoutSeconds)
        {
            if (PhotonNetwork.IsConnectedAndReady)
            {
                yield break;
            }
            yield return null;
        }
        // timed out without becoming connected -> fallback to offline mode
        if (!PhotonNetwork.IsConnectedAndReady)
        {
            Debug.LogWarning("[Lobby] Connection timeout - falling back to offline mode.");
            ActivateOfflineMode("connection timeout");
            isCreatingRoom = false;
            if (isLoadingTriggeredByUser)
            {
                // disable 'Connect to Best Server' (use region in settings)
                PhotonNetwork.PhotonServerSettings.AppSettings.UseNameServer = false;
                OnStartGameClicked();
            }
        }
    }

    private IEnumerator Co_ReconnectAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (!HasInternet())
        {
            ActivateOfflineMode("no internet on reconnect");
            yield break;
        }
        Debug.Log("[Lobby] Reconnect: calling PhotonNetwork.ConnectUsingSettings()");
        // region fallback: try Asia first, then Hong Kong, then Best Server
        if (regionFallbackState == PhotonRegionFallbackState.None || regionFallbackState == PhotonRegionFallbackState.Asia)
        {
            statusText.text = "Reconnecting to Photon (Hong Kong)...";
            PhotonNetwork.PhotonServerSettings.AppSettings.UseNameServer = true;
            PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = "hk";
            regionFallbackState = PhotonRegionFallbackState.HongKong;
            PhotonNetwork.ConnectUsingSettings();
        }
        else if (regionFallbackState == PhotonRegionFallbackState.HongKong)
        {
            statusText.text = "Reconnecting to Photon (Best Server)...";
            PhotonNetwork.PhotonServerSettings.AppSettings.UseNameServer = true;
            PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion = null;
            regionFallbackState = PhotonRegionFallbackState.Best;
            PhotonNetwork.ConnectUsingSettings();
        }
        else
        {
            // all regions failed, go offline and show error
            regionFallbackState = PhotonRegionFallbackState.Failed;
            statusText.text = "Failed to connect to Photon server. Going offline.";
            ActivateOfflineMode("Photon connection failed");
            ShowJoinOrCreatePanel();
        }
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogWarning($"[Lobby] JoinRoomFailed: {message} (code {returnCode})");
        if (loadingStatusText != null) loadingStatusText.text = "Failed to join room. Please check the code and try again.";
        HideLoading();
        ShowJoinOrCreatePanel();
        if (statusText != null) statusText.text = "Failed to join room. Please check the code and try again.";
    }
}