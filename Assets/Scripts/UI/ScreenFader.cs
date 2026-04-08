using UnityEngine;
using UnityEngine.UI;

// Simple full-screen fader for screen transitions
public class ScreenFader : MonoBehaviour
{
    public static ScreenFader Instance { get; private set; }

    [Header("Fader")]
    public Image fadeImage; // full-screen black image
    [Range(0f,1f)] public float defaultFadeDuration = 0.8f;

    private Coroutine activeRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        EnsureUI();
        SetAlpha(0f);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void EnsureUI()
    {
        if (fadeImage != null) return;

        // Create a canvas and black image if not assigned
        var canvasGO = new GameObject("ScreenFader_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        DontDestroyOnLoad(canvasGO);

        var imgGO = new GameObject("Fade", typeof(Image));
        imgGO.transform.SetParent(canvasGO.transform, false);
        fadeImage = imgGO.GetComponent<Image>();
        fadeImage.color = new Color(0f, 0f, 0f, 0f);
        var rect = imgGO.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    public void SetAlpha(float a)
    {
        if (fadeImage == null) return;
        var c = fadeImage.color; c.a = Mathf.Clamp01(a); fadeImage.color = c;
        fadeImage.enabled = c.a > 0f;
    }

    public void FadeOut(float duration = -1f)
    {
        if (duration <= 0f) duration = defaultFadeDuration;
        StartFade(1f, duration);
    }

    public void FadeIn(float duration = -1f)
    {
        if (duration <= 0f) duration = defaultFadeDuration;
        StartFade(0f, duration);
    }

    public void StartFade(float targetAlpha, float duration)
    {
        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(CoFade(targetAlpha, duration));
    }

    private System.Collections.IEnumerator CoFade(float targetAlpha, float duration)
    {
        if (fadeImage == null) yield break;
        fadeImage.enabled = true;
        float start = fadeImage.color.a;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.0001f, duration);
            float a = Mathf.Lerp(start, targetAlpha, Mathf.Clamp01(t));
            SetAlpha(a);
            yield return null;
        }
        SetAlpha(targetAlpha);
        if (Mathf.Approximately(targetAlpha, 0f)) fadeImage.enabled = false;
        activeRoutine = null;
    }
}


