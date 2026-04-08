using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using TMPro;

/// <summary>
/// Loading screen for FIRSTMAP that blocks the game view until the player is teleported to spawn.
/// Shows on scene load and hides when centralized spawn flow completes teleport.
/// </summary>
public class FirstMapLoadingScreen : MonoBehaviour
{
    public static FirstMapLoadingScreen Instance { get; private set; }

    [Header("UI (optional - creates defaults if null)")]
    [Tooltip("Assign a Canvas/panel, or leave null to auto-create")]
    public GameObject loadingPanel;
    [Tooltip("Optional loading text")]
    public TMPro.TextMeshProUGUI loadingText;

    [Header("Settings")]
    [Tooltip("Skip showing if ProceduralMapLoader is already handling loading (coming from trigger)")]
    public bool skipWhenProceduralLoaderActive = true;

    private const string TargetSceneName = "FIRSTMAP";
    private GameObject _createdCanvasRoot;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        if (SceneManager.GetActiveScene().name != TargetSceneName)
            return;

        // skip when a transition system is already handling loading and cutscene flow
        if (skipWhenProceduralLoaderActive && (ProceduralMapLoader.IsLoadingActive || CutsceneManager.TransitionControlledStart))
        {
            if (loadingPanel != null) loadingPanel.SetActive(false);
            return;
        }

        EnsurePanel();
        Show();

        // auto-hide once the start cutscene sequence completes (or after timeout)
        StartCoroutine(Co_AutoHideAfterCutscene());
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Call when player has been teleported. Hides the loading screen (and ProceduralMapLoader's / SceneLoader's if kept visible).</summary>
    public static void HideWhenReady()
    {
        ProceduralMapLoader.HideLoadingPanelExternal();
        if (SceneLoader.Instance != null)
            SceneLoader.Instance.HideLoadingPanel();
        if (Instance != null)
            Instance.Hide();
    }

    private IEnumerator Co_AutoHideAfterCutscene()
    {
        // wait for the start-scene cutscene manager to appear and finish
        CutsceneManager cm = null;
        float elapsed = 0f;
        const float maxWait = 120f;

        // first, wait for CutsceneManager to exist
        while (cm == null && elapsed < maxWait)
        {
            var all = FindObjectsByType<CutsceneManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].cutsceneMode == CutsceneMode.StartScene)
                {
                    cm = all[i];
                    break;
                }
            }
            if (cm == null)
            {
                elapsed += 0.5f;
                yield return new WaitForSeconds(0.5f);
            }
        }

        if (cm == null)
        {
            // no cutscene manager found, hide after a fallback delay
            yield return new WaitForSeconds(3f);
            Hide();
            yield break;
        }

        // cutscene manager found - hide loading so its fade overlay is visible
        Hide();

        // wait for cutscene + spawn to complete (CutsceneManager handles its own overlay)
        while (cm != null && !cm.IsStartSequenceComplete)
        {
            yield return null;
        }
    }

    private void EnsurePanel()
    {
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(true);
            return;
        }

        // Auto-create a simple full-screen loading overlay
        var canvasObj = new GameObject("FirstMapLoadingCanvas");
        var canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767; // Top-most
        canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        var panelObj = new GameObject("LoadingPanel");
        panelObj.transform.SetParent(canvasObj.transform, false);

        var rect = panelObj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = panelObj.AddComponent<UnityEngine.UI.Image>();
        image.color = Color.black;

        var textObj = new GameObject("LoadingText");
        textObj.transform.SetParent(panelObj.transform, false);
        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(400, 80);
        textRect.anchoredPosition = Vector2.zero;

        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = "Loading...";
        tmp.fontSize = 36;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        loadingPanel = panelObj;
        _createdCanvasRoot = canvasObj;
    }

    private void Show()
    {
        ProceduralMapLoader.HideLoadingPanelExternal();
        if (SceneLoader.Instance != null)
            SceneLoader.Instance.HideLoadingPanel();
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(true);
            if (loadingText != null)
                loadingText.text = "Loading...";
        }
    }

    private void Hide()
    {
        if (loadingPanel != null)
            loadingPanel.SetActive(false);
        if (_createdCanvasRoot != null)
        {
            Destroy(_createdCanvasRoot);
            _createdCanvasRoot = null;
            loadingPanel = null;
        }
    }
}
