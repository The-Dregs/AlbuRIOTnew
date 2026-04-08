using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class LoadingScreenManager : MonoBehaviour
{
    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        if (loadingPanel != null) DontDestroyOnLoad(loadingPanel);
    }
    [Header("Loading Screen UI")]
    public GameObject loadingPanel; // Assign your loading panel in Inspector
    public TMPro.TextMeshProUGUI loadingText; // Assign your TMP text for animated dots
    public float dotInterval = 0.4f; // seconds between dot changes
    private bool animatingDots = false;

    // Call this to load a scene with loading screen
    public void LoadSceneAsync(string sceneName)
    {
    StartCoroutine(LoadSceneCoroutine(sceneName));
    }

    IEnumerator LoadSceneCoroutine(string sceneName)
    {
        if (loadingPanel != null) loadingPanel.SetActive(true);
        if (loadingText != null)
        {
            loadingText.text = "Loading";
            animatingDots = true;
            StartCoroutine(AnimateLoadingDots());
        }

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        asyncLoad.allowSceneActivation = false;

        // Wait until the scene is loaded
        while (asyncLoad.progress < 0.9f)
        {
            yield return null;
        }

        // Optionally wait a bit for network objects to spawn
        yield return new WaitForSeconds(1f);

        // Activate the scene
        asyncLoad.allowSceneActivation = true;

        // Hide loading panel after scene is active
        yield return null;
        if (loadingPanel != null) loadingPanel.SetActive(false);
        animatingDots = false;
    }

    IEnumerator AnimateLoadingDots()
    {
        int dotCount = 0;
        while (animatingDots)
        {
            dotCount = (dotCount + 1) % 4; // cycles 0,1,2,3
            string dots = new string('.', dotCount);
            loadingText.text = $"Loading{dots}";
            yield return new WaitForSeconds(dotInterval);
        }
    }

    private void CleanupBeforeSceneLoad()
    {
    var allObjects = GameObject.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var go in allObjects)
        {
            // only destroy root objects in the active scene that are not marked DontDestroyOnLoad
            if (go.scene.isLoaded && go.transform.parent == null && go.hideFlags == HideFlags.None)
            {
                // skip DontDestroyOnLoad objects (they are in a special scene)
                if (go.name == "DontDestroyOnLoad") continue;
                // skip this LoadingScreenManager itself if it's persistent
                if (go == this.gameObject) continue;
                Destroy(go);
            }
        }
    }
}
