using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach this directly to the Boss_Bakunawa root GameObject.
/// Drag your Canvas/Slider from the scene into the fields below.
/// Fades in on spawn and fades out after death.
/// </summary>
public class BossHealthBarUI : MonoBehaviour
{
    [Header("UI References (from your Canvas)")]
    [Tooltip("Drag the Slider from your boss bar Canvas here.")]
    public Slider healthSlider;
    [Tooltip("Optional: TMP or Text GameObject showing boss name.")]
    public GameObject nameTextObject;
    [Tooltip("Optional: Shadow TMP or Text GameObject for boss name.")]
    public GameObject nameTextShadowObject;
    [Tooltip("Optional: TMP or Text GameObject showing e.g. '450 / 500'.")]
    public GameObject healthTextObject;
    [Tooltip("The root CanvasGroup to fade in/out. Add a CanvasGroup to your bar root.")]
    public CanvasGroup barCanvasGroup;

    [Header("Settings")]
    public string bossDisplayName = "Bakunawa";
    [Tooltip("Seconds to fade in when the bar appears.")]
    public float fadeInDuration = 1f;
    [Tooltip("Seconds the bar stays visible after the boss dies before fading out.")]
    public float hideDelayAfterDeath = 2f;
    [Tooltip("Seconds to fade out after death delay.")]
    public float fadeOutDuration = 1f;
    [Tooltip("Canvas Sort Order for the boss bar. Keep this LOW (e.g. 0) so pause/escape menus render on top.")]
    public int canvasSortOrder = 0;

    private BakunawaAI bakunawa;
    private float hideTimer = -1f;

    private enum BarState { Hidden, FadingIn, Visible, FadingOut }
    private BarState state = BarState.Hidden;
    private float fadeProgress = 0f;

    private void Start()
    {
        bakunawa = GetComponent<BakunawaAI>();

        if (healthSlider != null)
        {
            healthSlider.minValue = 0f;
            healthSlider.maxValue = 1f;
            healthSlider.wholeNumbers = false;
            healthSlider.interactable = false;
        }

        SetText(nameTextObject, bossDisplayName);
        SetText(nameTextShadowObject, bossDisplayName);

        // Ensure CanvasGroup is active but transparent
        if (barCanvasGroup != null)
        {
            barCanvasGroup.gameObject.SetActive(true);
            barCanvasGroup.alpha = 0f;

            // Force the Canvas sort order so the bar never renders on top of the pause/escape menu
            Canvas barCanvas = barCanvasGroup.GetComponentInParent<Canvas>();
            if (barCanvas == null) barCanvas = barCanvasGroup.GetComponent<Canvas>();
            if (barCanvas != null)
            {
                barCanvas.overrideSorting = true;
                barCanvas.sortingOrder = canvasSortOrder;
            }
        }

        state = BarState.Hidden;
        Refresh();

        // Start fade-in immediately when boss spawns
        BeginFadeIn();
    }

    private void Update()
    {
        if (bakunawa == null) return;

        HandleFade();

        if (bakunawa.IsDead)
        {
            Refresh();

            if (state == BarState.Visible || state == BarState.FadingIn)
            {
                if (hideTimer < 0f)
                    hideTimer = hideDelayAfterDeath;

                hideTimer -= Time.deltaTime;
                if (hideTimer <= 0f)
                {
                    hideTimer = -1f;
                    BeginFadeOut();
                }
            }
            return;
        }

        // Boss alive
        hideTimer = -1f;
        if (state == BarState.Hidden || state == BarState.FadingOut)
            BeginFadeIn();

        Refresh();
    }

    private void HandleFade()
    {
        if (barCanvasGroup == null) return;

        if (state == BarState.FadingIn)
        {
            float dur = Mathf.Max(0.01f, fadeInDuration);
            fadeProgress = Mathf.Min(1f, fadeProgress + Time.deltaTime / dur);
            barCanvasGroup.alpha = fadeProgress;
            if (fadeProgress >= 1f)
                state = BarState.Visible;
        }
        else if (state == BarState.FadingOut)
        {
            float dur = Mathf.Max(0.01f, fadeOutDuration);
            fadeProgress = Mathf.Max(0f, fadeProgress - Time.deltaTime / dur);
            barCanvasGroup.alpha = fadeProgress;
            if (fadeProgress <= 0f)
                state = BarState.Hidden;
        }
    }

    private void BeginFadeIn()
    {
        state = BarState.FadingIn;
        if (barCanvasGroup != null)
            fadeProgress = barCanvasGroup.alpha; // resume from current alpha if mid-fade
    }

    private void BeginFadeOut()
    {
        state = BarState.FadingOut;
        if (barCanvasGroup != null)
            fadeProgress = barCanvasGroup.alpha;
    }

    private void Refresh()
    {
        if (bakunawa == null) return;

        float frac = bakunawa.MaxHealth > 0
            ? Mathf.Clamp01((float)bakunawa.CurrentHealth / bakunawa.MaxHealth)
            : 0f;

        if (healthSlider != null)
            healthSlider.value = frac;

        SetText(healthTextObject, $"{bakunawa.CurrentHealth} / {bakunawa.MaxHealth}");
    }

    private static void SetText(GameObject go, string text)
    {
        if (go == null || string.IsNullOrEmpty(text)) return;
        var tmp = go.GetComponent<TMPro.TextMeshProUGUI>();
        if (tmp != null) { tmp.text = text; return; }
        var uitext = go.GetComponent<Text>();
        if (uitext != null) uitext.text = text;
    }
}
