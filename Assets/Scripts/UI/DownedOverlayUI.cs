using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows a blinking red transparent overlay when the player is downed (multiplayer death state).
/// </summary>
public class DownedOverlayUI : MonoBehaviour
{
    [Header("Overlay")]
    public Image overlayImage; // Full-screen red image
    [Range(0f, 1f)] public float maxAlpha = 0.7f;
    [Range(0f, 1f)] public float minAlpha = 0.3f;
    [Tooltip("Blink speed (cycles per second)")]
    public float blinkSpeed = 1.5f;
    
    [Header("Timer Display")]
    [Tooltip("Optional text to show remaining downed time")]
    public TMPro.TextMeshProUGUI timerText;
    [Tooltip("Show timer countdown")]
    public bool showTimer = true;
    
    [Header("Auto")]
    [Tooltip("If true, will enable the overlay GameObject at runtime even if disabled in inspector.")]
    public bool autoEnableOverlay = true;
    [Tooltip("If true and overlayImage is unassigned, will try to find a child named 'DownedOverlayImage'.")]
    public bool autoFindByName = true;
    [Tooltip("Child name to look for when autoFindByName is enabled.")]
    public string overlayChildName = "DownedOverlayImage";
    
    private Color baseColor = Color.red;
    private float blinkPhase = 0f;
    private bool isDowned = false;
    private PlayerStats playerStats;
    
    void Awake()
    {
        TryAutoFind();
        PrepareOverlay();
        
        // Find local player stats
        playerStats = FindLocalPlayerStats();
    }
    
    void Update()
    {
        if (overlayImage == null) return;
        
        // Check if local player is downed
        if (playerStats == null)
        {
            playerStats = FindLocalPlayerStats();
        }
        
        bool wasDowned = isDowned;
        isDowned = playerStats != null && playerStats.IsDowned;
        
        if (isDowned)
        {
            // Blinking effect
            blinkPhase += blinkSpeed * Time.deltaTime * Mathf.PI * 2f;
            if (blinkPhase > Mathf.PI * 2f) blinkPhase -= Mathf.PI * 2f;
            
            // Sin wave for smooth blinking (0 to 1)
            float blinkValue = (Mathf.Sin(blinkPhase) + 1f) * 0.5f; // 0 to 1
            float alpha = Mathf.Lerp(minAlpha, maxAlpha, blinkValue);
            
            SetAlpha(alpha);
            
            if (!overlayImage.gameObject.activeSelf)
            {
                overlayImage.gameObject.SetActive(true);
            }
            
            // Update timer display
            if (showTimer && timerText != null && playerStats != null)
            {
                float timeRemaining = playerStats.DownedTimeRemaining;
                if (timeRemaining > 0f)
                {
                    int seconds = Mathf.CeilToInt(timeRemaining);
                    timerText.text = $"Time remaining: {seconds}s";
                    timerText.gameObject.SetActive(true);
                    
                    // Flash red when time is running out (< 5 seconds)
                    if (seconds <= 5)
                    {
                        float flash = (Mathf.Sin(blinkPhase * 2f) + 1f) * 0.5f;
                        timerText.color = Color.Lerp(Color.white, Color.red, flash);
                    }
                    else
                    {
                        timerText.color = Color.white;
                    }
                }
                else
                {
                    timerText.text = "Time expired!";
                    timerText.color = Color.red;
                }
            }
        }
        else if (wasDowned && !isDowned)
        {
            // Player was revived or respawned, hide overlay
            SetAlpha(0f);
            overlayImage.enabled = false;
            if (timerText != null)
                timerText.gameObject.SetActive(false);
        }
        else
        {
            // Not downed, ensure overlay is hidden
            if (overlayImage.enabled && overlayImage.color.a > 0f)
            {
                SetAlpha(0f);
                overlayImage.enabled = false;
            }
            if (timerText != null && timerText.gameObject.activeSelf)
                timerText.gameObject.SetActive(false);
        }
    }
    
    private void SetAlpha(float a)
    {
        if (overlayImage == null) return;
        var c = baseColor;
        c.a = a;
        overlayImage.color = c;
        overlayImage.enabled = a > 0f;
    }
    
    private void TryAutoFind()
    {
        if (overlayImage != null) return;
        if (!autoFindByName || string.IsNullOrEmpty(overlayChildName)) return;
        
        // Search inactive children too
        var transforms = GetComponentsInChildren<Transform>(true);
        foreach (var t in transforms)
        {
            if (t != null && t.name == overlayChildName)
            {
                overlayImage = t.GetComponent<Image>();
                if (overlayImage != null) break;
            }
        }
    }
    
    private void PrepareOverlay()
    {
        if (overlayImage == null) return;
        if (autoEnableOverlay && !overlayImage.gameObject.activeSelf)
            overlayImage.gameObject.SetActive(true);
        
        baseColor = overlayImage.color;
        // Start fully transparent
        SetAlpha(0f);
        overlayImage.enabled = false;
    }
    
    private PlayerStats FindLocalPlayerStats()
    {
        var t = PlayerRegistry.GetLocalPlayerTransform();
        return t != null ? t.GetComponent<PlayerStats>() : null;
    }
    
    void OnValidate()
    {
        // Keep base color in sync in editor if possible
        if (overlayImage != null)
            baseColor = overlayImage.color;
    }
}

