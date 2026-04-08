using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;

/// <summary>
/// Watches the local player's health every frame.
/// When health drops, flashes the red overlay proportional to damage taken.
/// Alpha is always set directly — never enables/disables the image.
/// </summary>
public class DamageOverlayUI : MonoBehaviour
{
    [Header("Overlay Image")]
    [Tooltip("The full-screen red Image. Assign directly or auto-found by child name.")]
    public Image overlayImage;
    [Tooltip("Child GameObject name to search for if overlayImage is unassigned.")]
    public string overlayChildName = "RedImageForDamage";

    [Header("Overlay Settings")]
    [Range(0f, 1f)]
    [Tooltip("Maximum alpha the overlay reaches on a full-health hit.")]
    public float maxAlpha = 0.75f;
    [Tooltip("How fast the overlay fades out per second.")]
    public float fadeOutPerSecond = 2f;
    [Tooltip("Minimum alpha so even tiny hits always produce visible feedback.")]
    [Range(0f, 1f)]
    public float minAlphaStep = 0.2f;
    [Tooltip("Multiplier on the damage proportion. Higher = more intense flash per hit.")]
    public float damageIntensityScale = 4f;
    [Tooltip("Minimum alpha boost applied on repeat hits so each hit visibly retriggers.")]
    [Range(0f, 1f)]
    public float retriggerBoost = 0.12f;

    // RGB of the overlay — alpha is controlled separately
    private Color rgbColor = Color.red;
    private float currentAlpha = 0f;

    private PlayerStats watchedPlayer;
    private int lastKnownHealth = -1;

    void Awake()
    {
        ResolveOverlayImage();

        if (overlayImage != null)
        {
            // Ensure it's always active/enabled — we control visibility via alpha only
            overlayImage.gameObject.SetActive(true);
            overlayImage.enabled = true;
            // Store just the RGB from whatever color is set in Inspector
            rgbColor = overlayImage.color;
            rgbColor.a = 0f;
            // Start fully transparent
            overlayImage.color = rgbColor;
        }
    }

    void Start()
    {
        ResolveOverlayImage();
        FindLocalPlayer();
    }

    void Update()
    {
        if (watchedPlayer == null)
        {
            FindLocalPlayer();
            return;
        }

        // Fade out every frame
        if (currentAlpha > 0f)
        {
            currentAlpha = Mathf.Max(0f, currentAlpha - fadeOutPerSecond * Time.deltaTime);
            SetAlpha(currentAlpha);
        }
    }

    // LateUpdate catches health drops after all damage is applied (backup for Pulse)
    void LateUpdate()
    {
        if (watchedPlayer == null)
        {
            FindLocalPlayer();
            return;
        }

        int health = watchedPlayer.currentHealth;

        // Health dropped since last frame — flash (catches any damage path that missed Pulse)
        if (lastKnownHealth >= 0 && health < lastKnownHealth)
        {
            int dmg = lastKnownHealth - health;
            float proportion = watchedPlayer.maxHealth > 0
                ? (float)dmg / watchedPlayer.maxHealth
                : 0.25f;
            Flash(proportion);
        }

        lastKnownHealth = health;
    }

    private void Flash(float proportion)
    {
        float target = Mathf.Clamp01(proportion * damageIntensityScale) * maxAlpha;
        if (target < minAlphaStep) target = minAlphaStep;
        // Ensure every hit visibly retriggers, even during fade or same-sized consecutive hits.
        float boosted = currentAlpha + Mathf.Max(0f, retriggerBoost);
        currentAlpha = Mathf.Clamp01(Mathf.Max(target, boosted));
        SetAlpha(currentAlpha);
    }

    private void SetAlpha(float a)
    {
        if (overlayImage == null) ResolveOverlayImage();
        if (overlayImage == null) return;
        if (!overlayImage.gameObject.activeSelf) overlayImage.gameObject.SetActive(true);
        if (!overlayImage.enabled) overlayImage.enabled = true;
        Color c = rgbColor;
        c.a = a;
        overlayImage.color = c;
    }

    /// <summary>
    /// External pulse entry point (called from PlayerStats, still works).
    /// </summary>
    public void Pulse(float amount01)
    {
        if (overlayImage == null) ResolveOverlayImage();
        if (overlayImage != null)
            Flash(amount01);
    }

    private void FindLocalPlayer()
    {
        // 1. If we're under a player (e.g. on their Canvas), use that directly
        var parentStats = GetComponentInParent<PlayerStats>();
        if (parentStats != null)
        {
            var pv = parentStats.GetComponent<PhotonView>();
            if (pv == null || !PhotonNetwork.IsConnected || PhotonNetwork.OfflineMode || pv.IsMine)
            {
                watchedPlayer = parentStats;
                lastKnownHealth = parentStats.currentHealth;
                return;
            }
        }

        // 2. Scene-wide search — prefer local player
        var localT = PlayerRegistry.GetLocalPlayerTransform();
        if (localT != null)
        {
            var ps = localT.GetComponent<PlayerStats>();
            if (ps != null)
            {
                watchedPlayer = ps;
                lastKnownHealth = ps.currentHealth;
                return;
            }
        }
    }

    private void ResolveOverlayImage()
    {
        if (overlayImage != null) return;
        // Search in children
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t != null && t.name == overlayChildName)
            {
                overlayImage = t.GetComponent<Image>();
                if (overlayImage != null) return;
            }
        }
        // Search in parent/siblings (overlay may be sibling under same Canvas)
        Transform parent = transform.parent;
        if (parent != null)
        {
            foreach (Transform child in parent)
            {
                if (child != null && child.name == overlayChildName)
                {
                    overlayImage = child.GetComponent<Image>();
                    if (overlayImage != null) return;
                }
            }
            var parentImage = parent.GetComponentInChildren<Image>(true);
            if (parentImage != null && parentImage.name == overlayChildName)
            {
                overlayImage = parentImage;
                return;
            }
        }
        // Fallback: any Image in hierarchy
        var anyImage = GetComponentInChildren<Image>(true);
        if (anyImage != null)
        {
            overlayImage = anyImage;
            return;
        }
        if (transform.parent != null)
        {
            anyImage = transform.parent.GetComponentInChildren<Image>(true);
            if (anyImage != null)
            {
                overlayImage = anyImage;
                return;
            }
        }
    }

    void OnValidate()
    {
        if (overlayImage != null)
        {
            rgbColor = overlayImage.color;
            rgbColor.a = 0f;
        }
    }
}
