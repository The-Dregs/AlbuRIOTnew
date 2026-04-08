
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Day/night cycle UI — timer overlay, day notification banner, and night transition vfx overlay.
/// Attach to an object under a Canvas. Assign fields in the inspector or let it auto-create them.
/// Day notifications: slide down, hold, slide up. Night notifications: fade in, hold, fade out only.
/// </summary>
public class DayNightTimerUI : MonoBehaviour
{
    [Header("Cutscene Control")]
    [SerializeField] private CanvasGroup timerCanvasGroup;
    [Tooltip("Delay before showing post-spawn notification (gives player a moment to see the world)")]
    [SerializeField] private float postSpawnNotificationDelay = 0.3f;
    private bool cutsceneActive = true;
    [Header("Notification Audio")]
    [Tooltip("Audio clip played when night notification appears.")]
    [SerializeField] private AudioClip nightNotificationSFX;
    [SerializeField] private AudioSource notificationAudioSource;
    [Header("Notification Banner")]
    [Tooltip("RectTransform for the day notification. Uses its inspector position — no position override.")]
    [SerializeField] private RectTransform dayNotificationRect;

    [Header("Manager")]
    [SerializeField] private DayNightCycleManager dayNightCycleManager;

    [Header("Timer Overlay (small HUD element)")]
    [Tooltip("Shows countdown and phase: 'Day Time = 02:30' or 'Night Time = 02:30'.")]
    [SerializeField] private TextMeshProUGUI timerText;
    [Tooltip("Optional icon/image that can swap between a sun and moon.")]
    [SerializeField] private Image phaseIcon;
    [SerializeField] private Sprite sunSprite;
    [SerializeField] private Sprite moonSprite;

    [Header("Day Notification Overlay")]
    [Tooltip("CanvasGroup wrapping the day banner. Day-only.")]
    [SerializeField] private CanvasGroup dayNotificationGroup;
    [Tooltip("Text that displays 'Day 1', etc. Black text.")]
    [SerializeField] private TextMeshProUGUI dayNotificationText;
    [Tooltip("How long the banner stays fully visible.")]
    [SerializeField] private float notificationHoldTime = 2f;
    [Tooltip("Fade in/out speed for the banner.")]
    [SerializeField] private float notificationFadeSpeed = 2f;

    [Header("Night Notification Overlay")]
    [Tooltip("CanvasGroup for the 'Night Time' text banner. Separate from the dark overlay.")]
    [SerializeField] private CanvasGroup nightNotificationGroup;
    [Tooltip("Text that displays 'Night Time'.")]
    [SerializeField] private TextMeshProUGUI nightNotificationText;
    [Tooltip("Seconds for the night notification to fade in.")]
    [SerializeField, Min(0.01f)] private float nightNotificationFadeInDuration = 0.25f;
    [Tooltip("Seconds for the night notification to stay visible.")]
    [SerializeField, Min(0f)] private float nightNotificationHoldDuration = 1.25f;
    [Tooltip("Seconds for the night notification to fade out.")]
    [SerializeField, Min(0.01f)] private float nightNotificationFadeOutDuration = 0.25f;

    [Header("Night Transition Overlay")]
    [Tooltip("Full-screen dark overlay during dusk. Separate from night notification.")]
    [SerializeField] private CanvasGroup nightOverlayGroup;
    [Tooltip("Target alpha when night overlay is fully shown.")]
    [Range(0f, 1f)]
    [SerializeField] private float nightOverlayMaxAlpha = 0.35f;
    [Tooltip("Seconds for the dusk overlay to fade in to max alpha.")]
    [SerializeField, Min(0.01f)] private float nightOverlayFadeInDuration = 0.6f;
    [Tooltip("Seconds for the dusk overlay to fade out back to 0.")]
    [SerializeField, Min(0.01f)] private float nightOverlayFadeOutDuration = 0.6f;

    [Header("Legacy UI Text (optional)")]
    [SerializeField] private Text timerTextLegacy;

    [Header("Update")]
    [SerializeField] private float refreshInterval = 0.15f;
    [Tooltip("When day time has this many seconds left, timer blinks red/white to alert.")]
    [SerializeField] private float dayTimeAlertThreshold = 10f;
    [Tooltip("Blink speed (cycles per second).")]
    [SerializeField] private float dayTimeBlinkSpeed = 2f;

    // notification state
    private enum NotifState { Hidden, FadingIn, Hold, FadingOut }
    private NotifState _notifState = NotifState.Hidden;
    private float _notifTimer;
    private float _nextRefreshTime;
    private DayNightCycleManager.TimePhase _lastKnownPhase;
    private bool _subscribedEvents;
    private bool _isNightNotification; // true = fade only, false = slide down/up

    private void Start()
    {
        HideTimerAndNotifications();
        ResolveManager();
        if (dayNotificationRect == null && dayNotificationGroup != null)
            dayNotificationRect = dayNotificationGroup.GetComponent<RectTransform>();
        if (dayNotificationGroup != null) dayNotificationGroup.alpha = 0f;
        if (nightNotificationGroup != null) nightNotificationGroup.alpha = 0f;
        if (nightOverlayGroup != null) nightOverlayGroup.alpha = 0f;

        // These HUD overlays should never block UI clicks (e.g. Nuno continue button).
        DisableRaycastBlocking(timerCanvasGroup);
        DisableRaycastBlocking(dayNotificationGroup);
        DisableRaycastBlocking(nightNotificationGroup);
        DisableRaycastBlocking(nightOverlayGroup);

        SubscribeEvents();
        RefreshNow();
    }

private void OnEnable()
{
    SubscribeEvents();
}

private void OnDisable()
{
    UnsubscribeEvents();
}

private void Update()
{
    if (cutsceneActive)
    {
        HideTimerAndNotifications();
        return;
    }
    UpdateNotification();
    UpdateNightOverlay();
    UpdateTimerColor();
    if (Time.unscaledTime < _nextRefreshTime) return;
    _nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
    RefreshNow();
}

#region Manager & Events

private void ResolveManager()
{
    if (dayNightCycleManager == null)
        dayNightCycleManager = DayNightCycleManager.Instance;
    if (dayNightCycleManager == null)
        dayNightCycleManager = FindFirstObjectByType<DayNightCycleManager>();
}

private void SubscribeEvents()
{
    if (_subscribedEvents) return;
    ResolveManager();
    if (dayNightCycleManager == null) return;
    dayNightCycleManager.OnPhaseChanged += HandlePhaseChanged;
    dayNightCycleManager.OnNewDay += HandleNewDay;
    _subscribedEvents = true;
    _lastKnownPhase = dayNightCycleManager.CurrentPhase;
}

private void UnsubscribeEvents()
{
    if (!_subscribedEvents || dayNightCycleManager == null) return;
    dayNightCycleManager.OnPhaseChanged -= HandlePhaseChanged;
    dayNightCycleManager.OnNewDay -= HandleNewDay;
    _subscribedEvents = false;
}

#endregion

#region Timer Refresh

private void RefreshNow()
{
    if (dayNightCycleManager == null)
    {
        SetTMP(timerText, "--:--");
        SetLegacy(timerTextLegacy, "--:--");
        return;
    }

    dayNightCycleManager.GetPhaseBoundaries(out float dawnStart, out float dayStart, out float dayEnd, out float duskEnd);
    var phase = dayNightCycleManager.CurrentPhase;

    float safeDuration = Mathf.Max(5f, dayNightCycleManager.dayDuration);
    float SecondsUntil(float target01) => Mathf.Repeat(target01 - dayNightCycleManager.timeOfDay, 1f) * safeDuration;

    float countdown;
    string label;
    switch (phase)
    {
        case DayNightCycleManager.TimePhase.Day:
            countdown = SecondsUntil(dayEnd); // remaining "sun/day" time until dusk begins
            label = "Day Time Left";
            break;
        case DayNightCycleManager.TimePhase.Night:
            countdown = SecondsUntil(dawnStart); // remaining night until dawn begins
            label = "Night Time Left";
            break;
        case DayNightCycleManager.TimePhase.Dawn:
            countdown = SecondsUntil(dayStart);
            label = "Day Starts In";
            break;
        case DayNightCycleManager.TimePhase.Dusk:
        default:
            countdown = SecondsUntil(duskEnd);
            label = "Night Starts In";
            break;
    }

    string text = $"{label} = {FormatTime(countdown)}";
    SetTMP(timerText, text);
    SetLegacy(timerTextLegacy, text);
    if (phaseIcon != null)
    {
        bool showSun = phase == DayNightCycleManager.TimePhase.Day || phase == DayNightCycleManager.TimePhase.Dawn;
        if (showSun && sunSprite != null) phaseIcon.sprite = sunSprite;
        else if (!showSun && moonSprite != null) phaseIcon.sprite = moonSprite;
    }
    // Ensure timer canvas is enabled
    if (timerCanvasGroup != null && timerCanvasGroup.gameObject != null)
        timerCanvasGroup.gameObject.SetActive(true);
}

    private void UpdateTimerColor()
    {
        if (timerText == null || dayNightCycleManager == null) return;

        dayNightCycleManager.GetPhaseBoundaries(out _, out _, out float dayEnd, out _);
        float safeDuration = Mathf.Max(5f, dayNightCycleManager.dayDuration);
        float dayCountdown = Mathf.Repeat(dayEnd - dayNightCycleManager.timeOfDay, 1f) * safeDuration;
        var phase = dayNightCycleManager.CurrentPhase;

        if (phase == DayNightCycleManager.TimePhase.Day && dayCountdown <= dayTimeAlertThreshold && dayCountdown > 0f)
        {
            float t = (Mathf.Sin(Time.unscaledTime * dayTimeBlinkSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
            timerText.color = Color.Lerp(Color.white, Color.red, t);
        }
        else
        {
            bool dayLike = phase == DayNightCycleManager.TimePhase.Day || phase == DayNightCycleManager.TimePhase.Dawn;
            timerText.color = dayLike ? Color.white : Color.red;
        }
    }

#endregion

#region Day Notification Banner

private void HandleNewDay(int dayNumber)
{
    if (!cutsceneActive)
        ShowNotification($"Day {dayNumber}");
}

private void HandlePhaseChanged(DayNightCycleManager.TimePhase phase)
{
    if (phase == DayNightCycleManager.TimePhase.Dusk || phase == DayNightCycleManager.TimePhase.Night)
    {
        if (_lastKnownPhase == DayNightCycleManager.TimePhase.Day ||
            _lastKnownPhase == DayNightCycleManager.TimePhase.Dawn)
        {
            if (nightOverlayGroup != null)
                nightOverlayGroup.alpha = nightOverlayMaxAlpha;
            if (!cutsceneActive)
            {
                ShowNotification("Night Time", isNightNotif: true);
                TriggerNightCameraShake();
            }
        }
    }
    _lastKnownPhase = phase;
}

private void TriggerNightCameraShake()
{
    var localPlayer = PlayerRegistry.GetLocalPlayerTransform();
    if (localPlayer != null)
    {
        var ps = localPlayer.GetComponent<PlayerStats>();
        if (ps != null)
            ps.TriggerCameraShake(0.5f, 0.4f);
    }
    else
    {
        var shake = FindFirstObjectByType<CameraShake>();
        if (shake != null)
            shake.Shake(0.5f, 0.4f);
    }
}

private void ShowNotification(string message, bool isNightNotif = false)
{
    _isNightNotification = isNightNotif;
    if (isNightNotif)
    {
        if (nightNotificationGroup == null || nightNotificationText == null) return;
        if (dayNotificationGroup != null) dayNotificationGroup.alpha = 0f;
        nightNotificationText.text = message;
        if (notificationAudioSource != null && nightNotificationSFX != null)
            notificationAudioSource.PlayOneShot(nightNotificationSFX);
    }
    else
    {
        if (dayNotificationGroup == null || dayNotificationText == null) return;
        if (nightNotificationGroup != null) nightNotificationGroup.alpha = 0f;
        dayNotificationText.text = message;
    }
    _notifState = NotifState.FadingIn;
    _notifTimer = 0f;
}
    
        // Utility to hide timer and notifications during cutscene
        private void HideTimerAndNotifications()
{
    if (timerCanvasGroup != null)
    {
        timerCanvasGroup.alpha = 0f;
        timerCanvasGroup.blocksRaycasts = false;
        timerCanvasGroup.interactable = false;
    }
    if (dayNotificationGroup != null)
    {
        dayNotificationGroup.alpha = 0f;
        dayNotificationGroup.blocksRaycasts = false;
        dayNotificationGroup.interactable = false;
    }
    if (nightNotificationGroup != null)
    {
        nightNotificationGroup.alpha = 0f;
        nightNotificationGroup.blocksRaycasts = false;
        nightNotificationGroup.interactable = false;
    }
    if (nightOverlayGroup != null)
    {
        nightOverlayGroup.alpha = 0f;
        nightOverlayGroup.blocksRaycasts = false;
        nightOverlayGroup.interactable = false;
    }
}

/// <summary>Call from CutsceneManager after cutscene and spawn complete.</summary>
public void OnCutsceneFinished()
{
    cutsceneActive = false;
    if (timerCanvasGroup != null && timerCanvasGroup.gameObject != null)
        timerCanvasGroup.gameObject.SetActive(true);
    if (dayNotificationGroup != null && dayNotificationGroup.gameObject != null)
        dayNotificationGroup.gameObject.SetActive(true);
    if (nightNotificationGroup != null && nightNotificationGroup.gameObject != null)
        nightNotificationGroup.gameObject.SetActive(true);
    if (nightOverlayGroup != null && nightOverlayGroup.gameObject != null)
        nightOverlayGroup.gameObject.SetActive(true);
    timerCanvasGroup.alpha = 1f;
    dayNotificationGroup.alpha = 0f;
    nightNotificationGroup.alpha = 0f;
    nightOverlayGroup.alpha = 0f;
    DisableRaycastBlocking(timerCanvasGroup);
    DisableRaycastBlocking(dayNotificationGroup);
    DisableRaycastBlocking(nightNotificationGroup);
    DisableRaycastBlocking(nightOverlayGroup);
    RefreshNow();
    StartCoroutine(ShowDayNotificationAfterSpawn());
}

private IEnumerator ShowDayNotificationAfterSpawn()
{
    if (postSpawnNotificationDelay > 0f)
        yield return new WaitForSecondsRealtime(postSpawnNotificationDelay);
    if (dayNotificationGroup != null && dayNotificationText != null && dayNightCycleManager != null)
        ShowNotification($"Day {dayNightCycleManager.CurrentDay}", isNightNotif: false);
}

    private void UpdateNotification()
    {
        bool isNightNotif = _isNightNotification;
        float fadeSpeed = notificationFadeSpeed * Time.unscaledDeltaTime;

        // Night: simple single-canvasgroup fade only (no color changes, no movement).
        if (isNightNotif)
        {
            if (nightNotificationGroup == null) return;

            switch (_notifState)
            {
                case NotifState.Hidden:
                    break;
                case NotifState.FadingIn:
                    {
                        float step = Time.unscaledDeltaTime / Mathf.Max(0.01f, nightNotificationFadeInDuration);
                        nightNotificationGroup.alpha = Mathf.MoveTowards(nightNotificationGroup.alpha, 1f, step);
                    }
                    if (nightNotificationGroup.alpha >= 0.99f)
                    {
                        nightNotificationGroup.alpha = 1f;
                        _notifState = NotifState.Hold;
                        _notifTimer = nightNotificationHoldDuration;
                    }
                    break;
                case NotifState.Hold:
                    _notifTimer -= Time.unscaledDeltaTime;
                    if (_notifTimer <= 0f) _notifState = NotifState.FadingOut;
                    break;
                case NotifState.FadingOut:
                    {
                        float step = Time.unscaledDeltaTime / Mathf.Max(0.01f, nightNotificationFadeOutDuration);
                        nightNotificationGroup.alpha = Mathf.MoveTowards(nightNotificationGroup.alpha, 0f, step);
                    }
                    if (nightNotificationGroup.alpha <= 0.01f)
                    {
                        nightNotificationGroup.alpha = 0f;
                        _notifState = NotifState.Hidden;
                    }
                    break;
            }
            return;
        }

        // Day: fade the day banner and force black text.
        if (dayNotificationGroup == null) return;
        if (dayNotificationText != null) dayNotificationText.color = Color.black;

        switch (_notifState)
        {
            case NotifState.Hidden:
                break;
            case NotifState.FadingIn:
                dayNotificationGroup.alpha = Mathf.MoveTowards(dayNotificationGroup.alpha, 1f, fadeSpeed);
                if (dayNotificationGroup.alpha >= 0.99f)
                {
                    dayNotificationGroup.alpha = 1f;
                    _notifState = NotifState.Hold;
                    _notifTimer = notificationHoldTime;
                }
                break;
            case NotifState.Hold:
                _notifTimer -= Time.unscaledDeltaTime;
                if (_notifTimer <= 0f) _notifState = NotifState.FadingOut;
                break;
            case NotifState.FadingOut:
                dayNotificationGroup.alpha = Mathf.MoveTowards(dayNotificationGroup.alpha, 0f, fadeSpeed);
                if (dayNotificationGroup.alpha <= 0.01f)
                {
                    dayNotificationGroup.alpha = 0f;
                    _notifState = NotifState.Hidden;
                }
                break;
        }
    }

#endregion

#region Night Transition Overlay

private void UpdateNightOverlay()
{
    if (nightOverlayGroup == null || dayNightCycleManager == null) return;

    bool showOverlay = dayNightCycleManager.CurrentPhase == DayNightCycleManager.TimePhase.Dusk;
    float target = showOverlay ? nightOverlayMaxAlpha : 0f;
    float duration = showOverlay ? Mathf.Max(0.01f, nightOverlayFadeInDuration) : Mathf.Max(0.01f, nightOverlayFadeOutDuration);
    float step = Time.unscaledDeltaTime * (1f / duration) * Mathf.Max(0.01f, nightOverlayMaxAlpha);
    nightOverlayGroup.alpha = Mathf.MoveTowards(nightOverlayGroup.alpha, target, step);
}

#endregion

#region Helpers

private static string FormatTime(float seconds)
{
    int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
    int minutes = total / 60;
    int secs = total % 60;
    return $"{minutes:00}:{secs:00}";
}

private static void SetTMP(TextMeshProUGUI target, string value)
{
    if (target != null) target.text = value;
}

private static void SetLegacy(Text target, string value)
{
    if (target != null) target.text = value;
}

    private static void DisableRaycastBlocking(CanvasGroup group)
    {
        if (group == null) return;
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    #endregion
}

