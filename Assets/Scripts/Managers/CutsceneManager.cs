using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using ExitGames.Client.Photon;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public enum CutsceneMode
{
    StartScene,
    EndScene
}

public class CutsceneManager : MonoBehaviourPunCallbacks
{
    public static bool TransitionControlledStart { get; private set; } = false;

    public static void SetTransitionControlledStart(bool enabled)
    {
        TransitionControlledStart = enabled;
    }

    [Header("Cutscene Mode")]
    [Tooltip("StartScene: Plays at scene start, then spawns player. EndScene: Plays to end scene, then transitions to next scene.")]
    public CutsceneMode cutsceneMode = CutsceneMode.StartScene;

    [Header("Cutscene Timeline")]
    public PlayableDirector cutsceneDirector;
    [Tooltip("Optional runtime lookup name for start-scene cutscene director (useful when cutscene object is spawned from a prefab)")]
    public string startCutsceneDirectorName = "SpawnArea_1Cutscene";
    [Tooltip("How long to wait for a runtime-spawned cutscene director before continuing")]
    public float startCutsceneResolveTimeout = 20f;
    [Tooltip("How long to wait for the cutscene to enter Playing state before considering it failed")]
    public float startCutscenePlayStartTimeout = 8f;
    [Tooltip("When true, player spawn is blocked if the start cutscene cannot be started")]
    public bool requireStartCutsceneBeforeSpawn = true;
    [Header("Player Spawner")]
    [Tooltip("Only used in StartScene mode")]
    public TutorialSpawnManager spawnManager;
    [Header("UI")]
    public GameObject skipButton;
    public CanvasGroup fadeOverlay;
    public float fadeDuration = 1f;
    public float skipButtonDelay = 2f;
    [Tooltip("Optional loading panel shown immediately on scene start until cutscene is ready to play")]
    public GameObject loadingPanel;

    [Header("Multiplayer Sync")]
    [Tooltip("Wait for all players in the room to finish loading before playing the cutscene")]
    public bool waitForAllPlayers = true;

    [Header("Exit Button")]
    [Tooltip("Button shown on loading panel after a delay — lets the player return to the homescreen")]
    public GameObject exitButton;
    [Tooltip("Seconds to wait before showing the exit button (0 = always visible)")]
    public float exitButtonDelay = 10f;
    [Tooltip("Scene name to load when the exit button is pressed")]
    public string homescreenSceneName = "HOMESCREEN";
    
    [Header("Scene Transition (EndScene Mode Only)")]
    [Tooltip("Scene name to load after ending cutscene completes")]
    public string nextSceneName = "";
    
    [Header("GameObject Control")]
    [Tooltip("GameObjects to enable when cutscene starts")]
    public GameObject[] enableOnCutscene;
    [Tooltip("GameObjects to disable when cutscene starts")]
    public GameObject[] disableOnCutscene;

    [Header("Transform Reset")]
    [Tooltip("Transforms whose local position/rotation should be saved before the cutscene and restored after (e.g. NPC_Nuno animated by the timeline)")]
    public Transform[] resetTransformsAfterCutscene;

    private bool cutsceneSkipped = false;
    private bool objectsStateChanged = false;
    private bool hasTransitioned = false;
    private bool startSequenceStarted = false;
    private bool startCutsceneRoutineRunning = false;
    private GameObject runtimeFadeCanvas;
    private Image runtimeFadeImage;
    private GameObject runtimeSkipCanvas;
    private GameObject runtimeLoadingCanvas;
    private bool createdLoadingPanelAtRuntime;
    public bool IsStartSequenceComplete { get; private set; }

    // saved transforms to restore after cutscene
    private struct SavedTransform
    {
        public Transform target;
        public Vector3 localPosition;
        public Quaternion localRotation;
    }
    private SavedTransform[] savedTransforms;

    // loading text animation
    private TextMeshProUGUI loadingTMP;
    private Text loadingLegacyText;
    private Coroutine loadingDotsCoroutine;
    private Coroutine exitButtonDelayCoroutine;
    private bool isExiting = false;
    private const string READY_KEY = "CutsceneReady";
    private const string READY_SCENE_KEY = "CutsceneReadyScene";

    void Awake()
    {
        // show loading panel as early as possible so the camera is never visible
        if (cutsceneMode == CutsceneMode.StartScene)
        {
            EnsureLoadingPanelForStartScene();

            // hide exit button initially and hook up its click
            if (exitButton != null)
            {
                exitButton.SetActive(false);
                var btn = exitButton.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(OnExitToHomescreen);
                }
            }

            // start the delay timer for showing the exit button
            exitButtonDelayCoroutine = StartCoroutine(ShowExitButtonAfterDelay());
        }
    }

    void Start()
    {
        EnsureSkipButton();

        if (cutsceneMode == CutsceneMode.StartScene)
        {
            // only create the fade overlay now if there's no loading panel.
            // if a loading panel is assigned, it covers the screen instead;
            // the fade overlay will be created later in FadeInThenPlayCutscene.
            if (loadingPanel == null)
                EnsureRuntimeFadeOverlay(startBlack: true);
        }
        if (skipButton != null)
        {
            skipButton.SetActive(false);
            Button btn = skipButton.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(OnSkipButtonClicked);
            }
        }
        
        if (cutsceneMode == CutsceneMode.StartScene)
        {
            if (TransitionControlledStart)
            {
                Debug.Log("[CutsceneManager] Transition-controlled start is active; waiting for external begin.");
            }
            else
            {
                BeginStartSceneSequence();
            }
        }
    }

    public void BeginStartSceneSequence()
    {
        if (cutsceneMode != CutsceneMode.StartScene) return;
        if (startSequenceStarted) return;
        startSequenceStarted = true;
        IsStartSequenceComplete = false;

        if (PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer != null)
        {
            var props = new Hashtable { { READY_KEY, false }, { READY_SCENE_KEY, null } };
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        }

        // pause day/night cycle until cutscene + spawn completes
        if (DayNightCycleManager.Instance != null)
            DayNightCycleManager.Instance.isPaused = true;

        StartStartCutscene();
    }

    private void StartStartCutscene()
    {
        if (PhotonNetwork.OfflineMode && !PhotonNetwork.InRoom)
        {
            PhotonNetwork.CreateRoom("OfflineRoom");
            StartCoroutine(WaitForOfflineRoomThenStart());
        }
        else if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.InRoom)
        {
            StartCoroutine(WaitForRoomThenStartCutscene());
        }
        else if (PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                if (photonView != null)
                {
                    photonView.RPC("RPC_FadeInThenPlayCutscene", RpcTarget.All);
                }
                else
                {
                    StartCoroutine(FadeInThenPlayCutscene());
                }
            }
            else
            {
                Debug.Log("[CutsceneManager] Waiting for master to start start-scene cutscene RPC.");
            }
        }
        else if (PhotonNetwork.OfflineMode && PhotonNetwork.InRoom && photonView != null)
        {
            photonView.RPC("RPC_FadeInThenPlayCutscene", RpcTarget.All);
        }
        else
        {
            StartCoroutine(FadeInThenPlayCutscene());
        }
    }

    private IEnumerator WaitForRoomThenStartCutscene()
    {
        float timeout = 10f;
        float elapsed = 0f;
        while (!PhotonNetwork.InRoom && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!PhotonNetwork.InRoom)
        {
            Debug.LogWarning("[CutsceneManager] Timed out waiting for room before start cutscene.");
            yield break;
        }

        StartStartCutscene();
    }

    public void PlayEndingCutscene()
    {
        if (cutsceneMode != CutsceneMode.EndScene)
        {
            Debug.LogWarning("[CutsceneManager] PlayEndingCutscene called but mode is not EndScene");
            return;
        }

        bool isHost = PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
        if (isHost && photonView != null)
        {
            photonView.RPC("RPC_PlayEndingCutscene", RpcTarget.AllBuffered);
        }
        else if (photonView == null)
        {
            StartCoroutine(PlayEndingCutsceneCoroutine());
        }
    }
    
    private IEnumerator WaitForOfflineRoomThenStart()
    {
        // Wait for room creation to complete
        while (!PhotonNetwork.InRoom)
        {
            yield return null;
        }
        // Small delay to ensure everything is initialized
        yield return new WaitForSeconds(0.1f);
        if (photonView != null)
        {
            photonView.RPC("RPC_FadeInThenPlayCutscene", RpcTarget.All);
        }
        else
        {
            // Fallback if photonView not ready
            StartCoroutine(FadeInThenPlayCutscene());
        }
    }

    public override void OnJoinedRoom()
    {
        if (cutsceneMode == CutsceneMode.StartScene && TransitionControlledStart)
        {
            return;
        }

        if (cutsceneMode != CutsceneMode.StartScene)
            return;

        if (startSequenceStarted)
            return;

        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
        {
            if (PhotonNetwork.IsMasterClient)
                BeginStartSceneSequence();
            return;
        }

        BeginStartSceneSequence();
    }

    IEnumerator ShowSkipButtonWithDelay()
    {
        yield return new WaitForSeconds(skipButtonDelay);
        
        // Only show skip button to host (MasterClient)
        bool isHost = PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
        if (skipButton != null && isHost)
            skipButton.SetActive(true);
    }

    [PunRPC]
    public void RPC_FadeInThenPlayCutscene()
    {
        if (startCutsceneRoutineRunning)
        {
            return;
        }
        StartCoroutine(FadeInThenPlayCutscene());
    }

    IEnumerator FadeInThenPlayCutscene()
    {
        if (startCutsceneRoutineRunning)
            yield break;
        startCutsceneRoutineRunning = true;

        EnsureLoadingPanelForStartScene();
        SetLocalPlayerCutsceneState(true);
        LocalUIManager.Instance?.ForceClose();

        // Enable/disable GameObjects before cutscene starts
        EnableCutsceneObjects(true);

        // wait for all players to be ready before playing the cutscene
        if (waitForAllPlayers && !PhotonNetwork.OfflineMode && PhotonNetwork.InRoom)
        {
            yield return StartCoroutine(WaitForAllPlayersReady());
        }

        // resolve the cutscene director while loading panel is still visible
        yield return StartCoroutine(ResolveStartCutsceneDirectorIfNeeded());

        // wait for the cutscene director's position to stabilize after Photon sync
        if (cutsceneDirector != null)
        {
            yield return StartCoroutine(WaitForTransformStable(cutsceneDirector.transform, 15, 0.001f, 8f));
        }

        // save transforms that the timeline will animate so we can restore them after
        // (must run after director is resolved so we can find NPC_Nuno within the cutscene hierarchy)
        SaveTransformsBeforeCutscene();

        // now create the fade overlay (black) on top of everything
        EnsureRuntimeFadeOverlay(startBlack: true);

        // start the cutscene behind the black fade overlay
        if (cutsceneDirector != null && !cutsceneSkipped)
        {
            cutsceneDirector.time = 0;
            cutsceneDirector.Evaluate();
            cutsceneDirector.Play();
        }

        // hide loading panel — the black fade overlay is now covering the screen
        StopLoadingDots();
        HideExitButton();
        if (loadingPanel != null)
            loadingPanel.SetActive(false);

        if (fadeOverlay == null)
        {
            Debug.LogWarning("[CutsceneManager] Fade overlay was not available; continuing without fade-in.");
            StartCoroutine(ShowSkipButtonWithDelay());
            StartCoroutine(PlayCutsceneThenSpawn());
            startCutsceneRoutineRunning = false;
            yield break;
        }

        // fade from black to transparent, revealing the already-playing cutscene
        float safeFadeDuration = Mathf.Max(0.01f, fadeDuration);
        float t = 0f;
        while (t < safeFadeDuration)
        {
            t += Time.deltaTime;
            if (fadeOverlay == null)
            {
                EnsureRuntimeFadeOverlay(startBlack: false);
            }

            if (fadeOverlay != null)
            {
                fadeOverlay.alpha = Mathf.Lerp(1f, 0f, t / safeFadeDuration);
            }
            yield return null;
        }

        if (fadeOverlay != null)
        {
            fadeOverlay.alpha = 0f;
        }
        CleanupRuntimeFadeOverlay();

        // Show skip button after fade in
        StartCoroutine(ShowSkipButtonWithDelay());
        
        StartCoroutine(PlayCutsceneThenSpawn());
        startCutsceneRoutineRunning = false;
    }

    private void SetLocalPlayerCutsceneState(bool inCutscene)
    {
        GameObject player = PlayerSpawnCoordinator.FindLocalPlayer();
        if (player == null)
            return;

        var controller = player.GetComponent<ThirdPersonController>();
        if (controller != null)
        {
            controller.SetCanMove(!inCutscene);
            controller.SetCanControl(!inCutscene);
        }

        var combat = player.GetComponentInChildren<PlayerCombat>(true) ?? player.GetComponentInParent<PlayerCombat>();
        if (combat != null)
        {
            combat.enabled = !inCutscene;
            combat.SetCanControl(!inCutscene);
        }

        Camera cam = player.transform.Find("Camera")?.GetComponent<Camera>();
        if (cam != null)
            cam.enabled = !inCutscene;

        var cameraOrbit = player.GetComponentInChildren<ThirdPersonCameraOrbit>();
        if (cameraOrbit != null)
            cameraOrbit.SetRotationLocked(inCutscene);
    }

    private IEnumerator ResolveStartCutsceneDirectorIfNeeded()
    {
        if (cutsceneMode != CutsceneMode.StartScene)
            yield break;

        if (cutsceneDirector != null)
            yield break;

        float timeout = Mathf.Max(0f, startCutsceneResolveTimeout);
        if (timeout <= 0f)
            yield break;

        float elapsed = 0f;
        while (elapsed < timeout && cutsceneDirector == null)
        {
            cutsceneDirector = FindStartCutsceneDirector();
            if (cutsceneDirector != null)
                break;

            elapsed += 0.2f;
            yield return new WaitForSeconds(0.2f);
        }

        if (cutsceneDirector == null)
        {
            Debug.LogWarning($"[CutsceneManager] No start cutscene director found after {timeout:F1}s. Continuing to player spawn.");
        }
    }

    private PlayableDirector FindStartCutsceneDirector()
    {
        if (!string.IsNullOrEmpty(startCutsceneDirectorName))
        {
            var all = FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var dir = all[i];
                if (dir != null && dir.gameObject != null && dir.gameObject.name == startCutsceneDirectorName)
                    return dir;
            }
        }

        var fallback = FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < fallback.Length; i++)
        {
            var dir = fallback[i];
            if (dir == null || dir.gameObject == null) continue;
            if (dir == cutsceneDirector) continue;

            string n = dir.gameObject.name;
            if (!string.IsNullOrEmpty(n) && n.ToLowerInvariant().Contains("cutscene"))
                return dir;
        }

        return null;
    }

    private void EnsureSkipButton()
    {
        if (skipButton != null)
            return;

        // ensure an EventSystem exists so UI clicks register
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var esObj = new GameObject("EventSystem");
            esObj.AddComponent<EventSystem>();
            esObj.AddComponent<StandaloneInputModule>();
        }

        runtimeSkipCanvas = new GameObject("CutsceneSkipCanvas");
        var canvas = runtimeSkipCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767;
        runtimeSkipCanvas.AddComponent<CanvasScaler>();
        runtimeSkipCanvas.AddComponent<GraphicRaycaster>();

        var buttonObj = new GameObject("SkipButton");
        buttonObj.transform.SetParent(runtimeSkipCanvas.transform, false);
        var rect = buttonObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(180f, 56f);
        rect.anchoredPosition = new Vector2(-24f, -24f);

        var image = buttonObj.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.75f);

        var button = buttonObj.AddComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OnSkipButtonClicked);

        var textObj = new GameObject("Label");
        textObj.transform.SetParent(buttonObj.transform, false);
        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var label = textObj.AddComponent<Text>();
        label.text = "Skip";
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.fontSize = 24;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        skipButton = buttonObj;
    }

    private void EnsureRuntimeFadeOverlay(bool startBlack)
    {
        if (runtimeFadeCanvas != null && fadeOverlay != null)
        {
            runtimeFadeCanvas.SetActive(true);
            fadeOverlay.alpha = startBlack ? 1f : 0f;
            fadeOverlay.blocksRaycasts = true;
            if (runtimeFadeImage != null)
            {
                runtimeFadeImage.raycastTarget = true;
                runtimeFadeImage.color = Color.black;
            }
            return;
        }

        CleanupRuntimeFadeOverlay();

        runtimeFadeCanvas = new GameObject("CutsceneFadeCanvas");

        Canvas canvas = runtimeFadeCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32766;
        runtimeFadeCanvas.AddComponent<CanvasScaler>();
        runtimeFadeCanvas.AddComponent<GraphicRaycaster>();

        GameObject panel = new GameObject("FadeOverlay");
        panel.transform.SetParent(runtimeFadeCanvas.transform, false);
        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        runtimeFadeImage = panel.AddComponent<Image>();
        runtimeFadeImage.color = Color.black;
        runtimeFadeImage.raycastTarget = true;

        fadeOverlay = panel.AddComponent<CanvasGroup>();
        fadeOverlay.alpha = startBlack ? 1f : 0f;
        fadeOverlay.interactable = false;
        fadeOverlay.blocksRaycasts = true;
    }

    private void CleanupRuntimeFadeOverlay()
    {
        if (fadeOverlay != null)
        {
            fadeOverlay.alpha = 0f;
            fadeOverlay.blocksRaycasts = false;
            fadeOverlay.interactable = false;
        }

        if (runtimeFadeCanvas != null)
        {
            Destroy(runtimeFadeCanvas);
            runtimeFadeCanvas = null;
        }

        runtimeFadeImage = null;
        fadeOverlay = null;
    }

    /// <summary>
    /// Waits until a transform's world position stops moving for a number of consecutive frames,
    /// or until the timeout is reached.
    /// </summary>
    private IEnumerator WaitForTransformStable(Transform target, int stableFrames, float threshold, float timeout)
    {
        if (target == null) yield break;

        Vector3 lastPos = target.position;
        int stableCount = 0;
        float elapsed = 0f;

        while (stableCount < stableFrames && elapsed < timeout)
        {
            yield return null;
            elapsed += Time.deltaTime;

            if (target == null) yield break;

            float delta = Vector3.Distance(target.position, lastPos);
            if (delta < threshold)
                stableCount++;
            else
                stableCount = 0;

            lastPos = target.position;
        }

        if (elapsed >= timeout)
            Debug.LogWarning($"[CutsceneManager] Transform did not stabilize within {timeout:F1}s — proceeding anyway.");
    }

    #region Loading Text & Multiplayer Sync

    private void EnsureLoadingPanelForStartScene()
    {
        if (cutsceneMode != CutsceneMode.StartScene)
            return;

        if (loadingPanel == null)
            CreateRuntimeLoadingPanel();

        if (loadingPanel != null)
        {
            var parentCanvas = loadingPanel.GetComponentInParent<Canvas>(true);
            if (parentCanvas != null)
                parentCanvas.gameObject.SetActive(true);

            loadingPanel.SetActive(true);

            if (loadingTMP == null)
                loadingTMP = loadingPanel.GetComponentInChildren<TextMeshProUGUI>(true);
            if (loadingTMP == null && loadingLegacyText == null)
                loadingLegacyText = loadingPanel.GetComponentInChildren<Text>(true);
        }

        if (loadingDotsCoroutine == null)
            loadingDotsCoroutine = StartCoroutine(AnimateLoadingDots("Loading"));
    }

    private void CreateRuntimeLoadingPanel()
    {
        if (runtimeLoadingCanvas != null)
            return;

        runtimeLoadingCanvas = new GameObject("CutsceneLoadingCanvas");
        var canvas = runtimeLoadingCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32765;
        runtimeLoadingCanvas.AddComponent<CanvasScaler>();
        runtimeLoadingCanvas.AddComponent<GraphicRaycaster>();

        var panelObj = new GameObject("LoadingPanel");
        panelObj.transform.SetParent(runtimeLoadingCanvas.transform, false);

        var panelRect = panelObj.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        var panelImage = panelObj.AddComponent<Image>();
        panelImage.color = Color.black;

        var textObj = new GameObject("LoadingText");
        textObj.transform.SetParent(panelObj.transform, false);

        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(900f, 120f);
        textRect.anchoredPosition = Vector2.zero;

        loadingTMP = textObj.AddComponent<TextMeshProUGUI>();
        loadingTMP.alignment = TextAlignmentOptions.Center;
        loadingTMP.fontSize = 42f;
        loadingTMP.color = Color.white;
        loadingTMP.text = "Loading";

        loadingPanel = panelObj;
        createdLoadingPanelAtRuntime = true;
    }

    private IEnumerator ShowExitButtonAfterDelay()
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
        if (loadingPanel != null) loadingPanel.SetActive(false);

        if (runtimeLoadingCanvas != null)
        {
            Destroy(runtimeLoadingCanvas);
            runtimeLoadingCanvas = null;
            loadingPanel = null;
            loadingTMP = null;
            loadingLegacyText = null;
            createdLoadingPanelAtRuntime = false;
        }

        // clean up fade/skip overlays
        CleanupRuntimeFadeOverlay();
        if (runtimeSkipCanvas != null)
        {
            Destroy(runtimeSkipCanvas);
            runtimeSkipCanvas = null;
        }

        // clear ready property before disconnecting
        if (PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer != null)
        {
            var props = new Hashtable { { READY_KEY, null }, { READY_SCENE_KEY, null } };
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        }

        // enter menu mode so cursor is visible on homescreen
        var locker = LocalInputLocker.Ensure();
        if (locker != null)
            locker.EnterMenuMode();

        // disconnect and load homescreen (centralized cleanup so no stale Photon state remains)
        NetworkManager.ForceDisconnectAndCleanup("[CutsceneManager] OnExitToHomescreen");

        SceneManager.LoadScene(homescreenSceneName);
    }

    private void SetLoadingText(string text)
    {
        if (loadingTMP != null)
            loadingTMP.text = text;
        else if (loadingLegacyText != null)
            loadingLegacyText.text = text;
    }

    private IEnumerator AnimateLoadingDots(string baseText)
    {
        string[] frames = { $"{baseText} .", $"{baseText} . .", $"{baseText} . . ." };
        int index = 0;
        while (true)
        {
            SetLoadingText(frames[index]);
            index = (index + 1) % frames.Length;
            yield return new WaitForSeconds(0.4f);
        }
    }

    private void StopLoadingDots()
    {
        if (loadingDotsCoroutine != null)
        {
            StopCoroutine(loadingDotsCoroutine);
            loadingDotsCoroutine = null;
        }
    }

    private IEnumerator WaitForAllPlayersReady()
    {
        EnsureLoadingPanelForStartScene();

        // mark local player as ready
        if (PhotonNetwork.LocalPlayer != null)
        {
            string currentScene = SceneManager.GetActiveScene().name;
            var props = new Hashtable { { READY_KEY, true }, { READY_SCENE_KEY, currentScene } };
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        }

        // switch to "waiting for players" animated text
        StopLoadingDots();
        loadingDotsCoroutine = StartCoroutine(AnimateLoadingDots("Waiting for other players"));

        // Always base readiness on the players who are actually in the room right now.
        // Using MaxPlayers would make smaller parties (e.g. 2 players in a 4‑slot room)
        // wait unnecessarily for non‑existent players.
        float timeout = 30f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            if (AllPlayersReady())
                break;

            elapsed += 0.25f;
            yield return new WaitForSeconds(0.25f);
        }

        if (elapsed >= timeout)
            Debug.LogWarning("[CutsceneManager] Timed out waiting for all players — proceeding anyway.");

        // switch back to loading text while the cutscene director resolves
        StopLoadingDots();
        loadingDotsCoroutine = StartCoroutine(AnimateLoadingDots("Loading"));
    }

    private bool AllPlayersReady()
    {
        if (PhotonNetwork.CurrentRoom == null) return true;

        string currentScene = SceneManager.GetActiveScene().name;

        var players = PhotonNetwork.PlayerList;
        if (players == null || players.Length == 0) return true;

        foreach (var player in players)
        {
            if (player.CustomProperties == null || !player.CustomProperties.ContainsKey(READY_KEY))
                return false;
            if (!(bool)player.CustomProperties[READY_KEY])
                return false;
            if (!player.CustomProperties.ContainsKey(READY_SCENE_KEY))
                return false;

            object readyScene = player.CustomProperties[READY_SCENE_KEY];
            if (readyScene == null || !string.Equals(readyScene as string, currentScene, System.StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    #endregion

    private void OnDestroy()
    {
        StopLoadingDots();

        // skip if we already cleaned up during exit
        if (!isExiting && PhotonNetwork.IsConnectedAndReady && PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer != null)
        {
            var props = new Hashtable { { READY_KEY, null }, { READY_SCENE_KEY, null } };
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        }

        // final safety: if the start-cutscene object somehow survived, destroy it now so
        // no stray timeline/camera objects remain in the scene after this manager is gone.
        DisableCutsceneParentByName();

        CleanupRuntimeFadeOverlay();
        if (runtimeSkipCanvas != null)
        {
            Destroy(runtimeSkipCanvas);
            runtimeSkipCanvas = null;
        }

        if (runtimeLoadingCanvas != null)
        {
            Destroy(runtimeLoadingCanvas);
            runtimeLoadingCanvas = null;
            if (createdLoadingPanelAtRuntime)
                loadingPanel = null;
            loadingTMP = null;
            loadingLegacyText = null;
            createdLoadingPanelAtRuntime = false;
        }
    }

    public void OnSkipButtonClicked()
    {
        // only host can skip (button only visible to host anyway)
        bool isHost = PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
        if (!isHost) return;

        if (photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            photonView.RPC("RPC_SkipCutscene", RpcTarget.AllBuffered);
        }
        else
        {
            // fallback when no PhotonView or not in a room (offline / solo testing)
            RPC_SkipCutscene();
        }
    }

    [PunRPC]
    public void RPC_SkipCutscene()
    {
        cutsceneSkipped = true;
        if (cutsceneDirector != null)
        {
            cutsceneDirector.Stop();
        }
        StopAllCoroutines();

        // aggressively tear down any start-cutscene hierarchy so it can't keep animating
        // or block gameplay objects even if coroutines were interrupted.
        DisableCutsceneParentByName();
        EnableCutsceneObjects(false);
        
        if (cutsceneMode == CutsceneMode.StartScene)
        {
            StartCoroutine(PlayCutsceneThenSpawnImmediate());
        }
        else
        {
            StartCoroutine(TransitionToNextSceneImmediate());
        }
    }

    [PunRPC]
    public void RPC_PlayEndingCutscene()
    {
        StartCoroutine(PlayEndingCutsceneCoroutine());
    }

    IEnumerator FadeOutCutscene()
    {
        EnsureRuntimeFadeOverlay(startBlack: false);
        if (fadeOverlay == null) yield break;
        
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            fadeOverlay.alpha = Mathf.Lerp(0f, 1f, t / fadeDuration);
            yield return null;
        }
        fadeOverlay.alpha = 1f;
    }

    IEnumerator PlayEndingCutsceneCoroutine()
    {
        EnableCutsceneObjects(true);

        EnsureRuntimeFadeOverlay(startBlack: false);
        
        StartCoroutine(ShowSkipButtonWithDelay());
        
        if (cutsceneDirector != null && !cutsceneSkipped)
        {
            cutsceneDirector.Play();
            while (cutsceneDirector.state == PlayState.Playing && !cutsceneSkipped)
            {
                yield return null;
            }
            
            if (cutsceneDirector.state == PlayState.Playing)
            {
                cutsceneDirector.Stop();
            }
        }
        
        if (skipButton != null) skipButton.SetActive(false);
        
        EnableCutsceneObjects(false);
        
        yield return StartCoroutine(FadeOutCutscene());
        
        if (!cutsceneSkipped)
        {
            yield return StartCoroutine(TransitionToNextScene());
        }
    }

    IEnumerator TransitionToNextScene()
    {
        if (hasTransitioned) yield break;
        hasTransitioned = true;
        
        if (string.IsNullOrEmpty(nextSceneName))
        {
            Debug.LogWarning("[CutsceneManager] Next scene name not set! Cannot transition.");
            yield break;
        }
        
        // lock all local players' movement and persist them across scenes
        LockAndPersistAllLocalPlayers();
        
        yield return new WaitForEndOfFrame();
        
        bool isHost = PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
        if (!isHost)
        {
            yield break;
        }
        
        if (SceneLoader.Instance != null)
        {
            SceneLoader.Instance.LoadScene(nextSceneName);
        }
        else if (PhotonNetwork.IsConnectedAndReady || PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.LoadLevel(nextSceneName);
        }
        else
        {
            SceneManager.LoadScene(nextSceneName);
        }
    }

    IEnumerator TransitionToNextSceneImmediate()
    {
        if (hasTransitioned) yield break;
        hasTransitioned = true;
        
        if (string.IsNullOrEmpty(nextSceneName))
        {
            Debug.LogWarning("[CutsceneManager] Next scene name not set! Cannot transition.");
            yield break;
        }
        
        // lock all local players' movement and persist them across scenes
        LockAndPersistAllLocalPlayers();
        
        bool isHost = PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
        if (!isHost)
        {
            yield break;
        }
        
        EnsureRuntimeFadeOverlay(startBlack: true);
        
        yield return new WaitForSeconds(0.3f);
        
        if (SceneLoader.Instance != null)
        {
            SceneLoader.Instance.LoadScene(nextSceneName);
        }
        else if (PhotonNetwork.IsConnectedAndReady || PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.LoadLevel(nextSceneName);
        }
        else
        {
            SceneManager.LoadScene(nextSceneName);
        }
    }

    IEnumerator PlayCutsceneThenSpawn()
    {
        if (!cutsceneSkipped)
        {
            yield return StartCoroutine(ResolveStartCutsceneDirectorIfNeeded());

            if (cutsceneDirector != null)
            {
                // start playback if not already started by FadeInThenPlayCutscene
                if (cutsceneDirector.state != PlayState.Playing)
                {
                    cutsceneDirector.time = 0;
                    cutsceneDirector.Evaluate();
                    cutsceneDirector.Play();
                }

                float playStartWait = 0f;
                float playStartTimeout = Mathf.Max(0.1f, startCutscenePlayStartTimeout);
                while (!cutsceneSkipped && cutsceneDirector != null && cutsceneDirector.state != PlayState.Playing && playStartWait < playStartTimeout)
                {
                    playStartWait += Time.deltaTime;
                    yield return null;
                }

                if (!cutsceneSkipped && (cutsceneDirector == null || cutsceneDirector.state != PlayState.Playing))
                {
                    Debug.LogWarning($"[CutsceneManager] Start cutscene never entered Playing state within {playStartTimeout:F1}s.");
                    if (requireStartCutsceneBeforeSpawn)
                    {
                        if (skipButton != null) skipButton.SetActive(false);
                        yield break;
                    }
                }

                while (!cutsceneSkipped && cutsceneDirector != null && cutsceneDirector.state == PlayState.Playing)
                {
                    yield return null;
                }
            }
            else if (requireStartCutsceneBeforeSpawn)
            {
                Debug.LogWarning("[CutsceneManager] Start cutscene director was not found; spawn is blocked by requireStartCutsceneBeforeSpawn.");
                if (skipButton != null) skipButton.SetActive(false);
                yield break;
            }
        }
        
        // disable the cutscene director object now that it's done
        if (cutsceneDirector != null)
            cutsceneDirector.gameObject.SetActive(false);

        // disable the entire cutscene parent (e.g. SpawnArea_1Cutscene) so children don't block NPCs
        DisableCutsceneParentByName();

        // restore transforms that were animated by the timeline (e.g. NPC_Nuno position)
        RestoreTransformsAfterCutscene();

        // Restore GameObject states after cutscene
        EnableCutsceneObjects(false);
        
        // No fade out - just spawn player directly
        // Spawn player and wait for setup to complete
        yield return StartCoroutine(SpawnAndSetupPlayerCoroutine());

        IsStartSequenceComplete = true;
        TransitionControlledStart = false;

        // resume day/night cycle now that cutscene + spawn is done
        if (DayNightCycleManager.Instance != null)
            DayNightCycleManager.Instance.isPaused = false;

        // show day/night timer UI (it stays hidden during cutscene)
        NotifyDayNightTimerCutsceneFinished();
        
        if (skipButton != null) skipButton.SetActive(false);
        
        // Small delay before destroying to ensure everything is set up
        yield return new WaitForSeconds(0.5f);
        Destroy(gameObject);
    }

    IEnumerator PlayCutsceneThenSpawnImmediate()
    {
        // disable the cutscene director object
        if (cutsceneDirector != null)
            cutsceneDirector.gameObject.SetActive(false);

        // disable the entire cutscene parent (e.g. SpawnArea_1Cutscene) so children don't block NPCs
        DisableCutsceneParentByName();

        // restore transforms that were animated by the timeline (e.g. NPC_Nuno position)
        RestoreTransformsAfterCutscene();

        // No fade out when skipping - just spawn player directly
        // Spawn player and wait for setup to complete
        yield return StartCoroutine(SpawnAndSetupPlayerCoroutine());

        IsStartSequenceComplete = true;
        TransitionControlledStart = false;

        // resume day/night cycle now that cutscene + spawn is done
        if (DayNightCycleManager.Instance != null)
            DayNightCycleManager.Instance.isPaused = false;

        // show day/night timer UI (it stays hidden during cutscene)
        NotifyDayNightTimerCutsceneFinished();
        
        if (skipButton != null) skipButton.SetActive(false);
        
        // Small delay before destroying to ensure everything is set up
        yield return new WaitForSeconds(0.5f);
        Destroy(gameObject);
    }

    private void NotifyDayNightTimerCutsceneFinished()
    {
        var timerUI = FindFirstObjectByType<DayNightTimerUI>();
        if (timerUI != null)
            timerUI.OnCutsceneFinished();
    }
    
    /// <summary>
    /// finds the cutscene parent object by startCutsceneDirectorName and disables its entire hierarchy.
    /// this ensures children (camera, player model, colliders, etc.) don't interfere with NPCs.
    /// </summary>
    private void DisableCutsceneParentByName()
    {
        if (string.IsNullOrEmpty(startCutsceneDirectorName)) return;

        var allObjects = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allObjects.Length; i++)
        {
            if (allObjects[i] != null && allObjects[i].gameObject.name == startCutsceneDirectorName)
            {
                // fully remove the start-cutscene hierarchy once it has finished so it can no longer
                // drive animations or leave stray visuals in the scene (e.g. SpawnArea_1Cutscene extras).
                Destroy(allObjects[i].gameObject);
                Debug.Log($"[CutsceneManager] destroyed cutscene object '{startCutsceneDirectorName}'.");
                return;
            }
        }
    }

    private const string NUNO_RESET_NAME = "NPC_Nuno";

    private void SaveTransformsBeforeCutscene()
    {
        var toSave = GatherTransformsToReset();
        if (toSave == null || toSave.Count == 0) return;

        savedTransforms = new SavedTransform[toSave.Count];
        for (int i = 0; i < toSave.Count; i++)
        {
            var t = toSave[i];
            if (t == null) continue;
            savedTransforms[i] = new SavedTransform
            {
                target = t,
                localPosition = t.localPosition,
                localRotation = t.localRotation
            };
        }
        Debug.Log($"[CutsceneManager] saved {toSave.Count} transform(s) before cutscene.");
    }

    /// <summary>
    /// gathers transforms to save/restore. uses inspector array if populated; otherwise finds NPC_Nuno
    /// by name (needed when cutscene is auto-placed by MapResourcesGenerator and resetTransformsAfterCutscene
    /// is empty because the Nuno lives inside the spawned prefab).
    /// </summary>
    private List<Transform> GatherTransformsToReset()
    {
        var list = new List<Transform>();
        if (resetTransformsAfterCutscene != null && resetTransformsAfterCutscene.Length > 0)
        {
            foreach (var t in resetTransformsAfterCutscene)
            {
                if (t != null) list.Add(t);
            }
            return list;
        }

        // fallback: find NPC_Nuno when cutscene is auto-placed. search within cutscene hierarchy first
        // (scoped to avoid wrong Nuno), then globally with inactive included (Find only finds active).
        Transform nuno = FindNunoInHierarchy(cutsceneDirector?.transform?.parent);
        if (nuno == null)
        {
            nuno = FindNunoInScene();
        }
        if (nuno != null)
        {
            list.Add(nuno);
        }
        return list;
    }

    private static Transform FindNunoInHierarchy(Transform root)
    {
        if (root == null) return null;
        var t = root.Find(NUNO_RESET_NAME);
        if (t != null) return t;
        foreach (Transform child in root)
        {
            var found = FindNunoInHierarchy(child);
            if (found != null) return found;
        }
        return null;
    }

    private static Transform FindNunoInScene()
    {
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].gameObject.name == NUNO_RESET_NAME)
                return all[i];
        }
        return null;
    }

    private void RestoreTransformsAfterCutscene()
    {
        if (savedTransforms == null) return;

        for (int i = 0; i < savedTransforms.Length; i++)
        {
            var saved = savedTransforms[i];
            if (saved.target == null) continue;
            saved.target.localPosition = saved.localPosition;
            saved.target.localRotation = saved.localRotation;
            Debug.Log($"[CutsceneManager] restored transform '{saved.target.name}' to original local position.");
        }
        savedTransforms = null;
    }

    private void EnableCutsceneObjects(bool enable)
    {
        // Enable objects
        if (enableOnCutscene != null)
        {
            foreach (var obj in enableOnCutscene)
            {
                if (obj != null)
                {
                    obj.SetActive(enable);
                }
            }
        }
        
        // Disable objects (inverse of enable)
        if (disableOnCutscene != null)
        {
            foreach (var obj in disableOnCutscene)
            {
                if (obj != null)
                {
                    obj.SetActive(!enable);
                }
            }
        }
        
        objectsStateChanged = enable;
    }

    // locks movement and only persists local players for offline/singleplayer flows
    private void LockAndPersistAllLocalPlayers()
    {
        bool allowPersistAcrossScenes = !PhotonNetwork.IsConnected || PhotonNetwork.OfflineMode;

        var allPlayers = PlayerRegistry.All;
        if (allPlayers != null && allPlayers.Count > 0)
        {
            foreach (var ps in allPlayers)
            {
                if (ps == null) continue;
                var pv = ps.GetComponent<PhotonView>();
                bool isLocal = pv == null || !PhotonNetwork.IsConnected || pv.IsMine;
                if (!isLocal) continue;

                var controller = ps.GetComponent<ThirdPersonController>();
                if (controller != null)
                {
                    controller.SetCanMove(false);
                    controller.SetCanControl(false);
                }

                if (allowPersistAcrossScenes)
                {
                    DontDestroyOnLoad(ps.gameObject);
                    Debug.Log($"[CutsceneManager] Persisted local player: {ps.gameObject.name}");
                }
            }
        }
        else
        {
            // fallback: find all tagged players
            var tagged = GameObject.FindGameObjectsWithTag("Player");
            foreach (var go in tagged)
            {
                if (go == null) continue;
                var pv = go.GetComponent<PhotonView>();
                bool isLocal = pv == null || !PhotonNetwork.IsConnected || pv.IsMine;
                if (!isLocal) continue;

                var controller = go.GetComponent<ThirdPersonController>();
                if (controller != null)
                {
                    controller.SetCanMove(false);
                    controller.SetCanControl(false);
                }

                if (allowPersistAcrossScenes)
                {
                    DontDestroyOnLoad(go);
                    Debug.Log($"[CutsceneManager] Persisted local player (tag fallback): {go.name}");
                }
            }
        }
    }
    
    private IEnumerator SpawnAndSetupPlayerCoroutine()
    {
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.InRoom)
        {
            float roomWait = 0f;
            const float roomWaitTimeout = 10f;
            while (!PhotonNetwork.InRoom && roomWait < roomWaitTimeout)
            {
                roomWait += Time.deltaTime;
                yield return null;
            }

            if (!PhotonNetwork.InRoom)
            {
                Debug.LogWarning("[CutsceneManager] Spawn skipped because client is not in room yet.");
                yield break;
            }
        }

        // always use centralized spawn coordinator to avoid split spawn paths during start-scene flow.
        yield return PlayerSpawnCoordinator.EnsureLocalPlayerAtSpawn(
            maxWaitSeconds: 20f,
            waitForSpawnMarkers: true,
            enableDebugLogs: true,
            logPrefix: "[CutsceneManager]");

        yield return StartCoroutine(WaitForPlayerAndSetup());
    }

    private void SpawnAndSetupPlayer()
    {
        // This method is kept for backward compatibility but shouldn't be called directly
        // Use SpawnAndSetupPlayerCoroutine() in coroutines instead
        StartCoroutine(SpawnAndSetupPlayerCoroutine());
    }
    
    private IEnumerator WaitForPlayerAndSetup()
    {
        // Wait a frame for the player to spawn
        yield return null;
        
        // Try to find the local player, with multiple attempts
        GameObject player = null;
        int maxAttempts = 30;
        int attempts = 0;
        
        while (player == null && attempts < maxAttempts)
        {
            player = PlayerSpawnCoordinator.FindLocalPlayer();
            if (player == null)
            {
                yield return new WaitForSeconds(0.2f);
                attempts++;
            }
        }
        
        if (player != null)
        {
            Debug.Log("[CutsceneManager] Local player found, setting up camera.");

            if (PlayerSpawnCoordinator.TryGetBestSpawnPosition(out Vector3 spawnPosition, out Vector3 faceDirection, out string source, requireSpawnMarkers: true))
            {
                PlayerSpawnCoordinator.TeleportAndSetupPlayer(player, spawnPosition, faceDirection);
                Debug.Log($"[CutsceneManager] Repositioned local player using centralized spawn source: {source}");
            }
            
            // Setup camera
            Camera cam = player.transform.Find("Camera")?.GetComponent<Camera>();
            if (cam != null)
            {
                cam.enabled = true;
                cam.tag = "MainCamera";
            }
            
            // Setup camera orbit
            var cameraOrbit = player.GetComponentInChildren<ThirdPersonCameraOrbit>();
            if (cameraOrbit != null)
            {
                Transform cameraPivot = player.transform.Find("Camera/CameraPivot/TPCamera");
                if (cameraPivot != null)
                {
                    cameraOrbit.AssignTargets(player.transform, cameraPivot);
                }
                // Ensure camera rotation is unlocked after cutscene
                cameraOrbit.SetRotationLocked(false);
            }
            
            // Ensure LocalInputLocker has correct state after spawn
            var controller = player.GetComponent<ThirdPersonController>();
            if (controller != null)
            {
                controller.SetCanMove(true);
                controller.SetCanControl(true);
            }

            var combat = player.GetComponentInChildren<PlayerCombat>(true) ?? player.GetComponentInParent<PlayerCombat>();
            if (combat != null)
            {
                combat.enabled = true;
                combat.SetCanControl(true);
            }

            // Clear stale lock owners from prior cutscene/transition systems.
            var locker = LocalInputLocker.Ensure();
            if (locker != null)
            {
                locker.ReleaseAllForOwner("QuestCutscene");
                locker.ReleaseAllForOwner("AreaCutscene");
                locker.ReleaseAllForOwner("PostQuestSceneTransition");
            }

            LocalUIManager.Instance?.ForceClose();
            
            // Force gameplay cursor state
            locker?.ForceGameplayCursor();
            
            // re-resolve QuestManager inventory now that local player exists
            var qm = QuestManager.Instance;
            if (qm != null)
            {
                qm.RefreshPlayerInventory();
            }

            // Wait an extra frame to ensure all LateUpdates have completed
            yield return null;
            
            // Double-check camera is unlocked (defensive)
            if (cameraOrbit != null)
            {
                cameraOrbit.SetRotationLocked(false);
            }
        }
        else
        {
            Debug.LogWarning("[CutsceneManager] Player not found after spawn attempt. It may have been spawned elsewhere.");
        }
    }
}
