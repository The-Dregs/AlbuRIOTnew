using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Displays combo counter and progress feedback for the player's combo system.
/// </summary>
public class ComboCounterUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject comboPanel;
    public TextMeshProUGUI comboCountText;
    public TextMeshProUGUI comboMultiplierText;
    public Image comboProgressBar;
    public TextMeshProUGUI comboHitText; // Optional: shows "HIT!" when combo connects
    
    [Header("Settings")]
    [Tooltip("Time to show combo counter after last hit")]
    public float comboDisplayDuration = 2f;
    [Tooltip("Fade out duration")]
    public float fadeOutDuration = 0.5f;
    
    [Header("Animation")]
    [Tooltip("Scale animation when combo hits")]
    public bool enableHitAnimation = true;
    [Tooltip("Scale multiplier on hit")]
    public float hitScaleMultiplier = 1.2f;
    [Tooltip("Animation duration")]
    public float hitAnimationDuration = 0.2f;
    
    private PlayerCombat playerCombat;
    private float comboDisplayTimer = 0f;
    private CanvasGroup canvasGroup;
    private Vector3 originalScale;
    private Coroutine hitAnimationCoroutine;
    
    void Start()
    {
        // Find player combat component
        playerCombat = FindFirstObjectByType<PlayerCombat>();
        if (playerCombat != null)
        {
            playerCombat.OnComboHit += OnComboHit;
            playerCombat.OnComboProgress += OnComboProgress;
            playerCombat.OnComboReset += OnComboReset;
            playerCombat.OnComboComplete += OnComboComplete;
        }
        
        // Setup canvas group for fade
        canvasGroup = comboPanel != null ? comboPanel.GetComponent<CanvasGroup>() : null;
        if (canvasGroup == null && comboPanel != null)
            canvasGroup = comboPanel.AddComponent<CanvasGroup>();
        
        // Store original scale
        if (comboPanel != null)
            originalScale = comboPanel.transform.localScale;
        
        // Hide initially
        HideCombo();
    }
    
    void Update()
    {
        // Update display timer
        if (comboDisplayTimer > 0f)
        {
            comboDisplayTimer -= Time.deltaTime;
            
            // Fade out when timer is low
            if (comboDisplayTimer <= fadeOutDuration && canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Clamp01(comboDisplayTimer / fadeOutDuration);
            }
            
            if (comboDisplayTimer <= 0f)
            {
                HideCombo();
            }
        }
    }
    
    private void OnComboHit(int comboCount)
    {
        if (playerCombat == null) return;
        
        ShowCombo(comboCount);
        
        // Show hit text briefly
        if (comboHitText != null)
        {
            comboHitText.text = "HIT!";
            comboHitText.gameObject.SetActive(true);
            Invoke(nameof(HideHitText), 0.3f);
        }
        
        // Play hit animation
        if (enableHitAnimation && comboPanel != null)
        {
            if (hitAnimationCoroutine != null)
                StopCoroutine(hitAnimationCoroutine);
            hitAnimationCoroutine = StartCoroutine(CoHitAnimation());
        }
    }
    
    private void OnComboProgress(int hitNumber)
    {
        if (playerCombat == null) return;
        ShowCombo(hitNumber);
    }
    
    private void OnComboReset()
    {
        HideCombo();
    }
    
    private void OnComboComplete()
    {
        if (comboHitText != null)
        {
            comboHitText.text = "COMBO COMPLETE!";
            comboHitText.gameObject.SetActive(true);
            Invoke(nameof(HideHitText), 1f);
        }
    }
    
    private void ShowCombo(int comboCount)
    {
        if (comboPanel != null)
            comboPanel.SetActive(true);
        
        comboDisplayTimer = comboDisplayDuration;
        
        if (canvasGroup != null)
            canvasGroup.alpha = 1f;
        
        // Update combo count
        if (comboCountText != null)
        {
            comboCountText.text = $"COMBO x{comboCount}";
        }
        
        // Update multiplier
        if (comboMultiplierText != null && playerCombat != null)
        {
            float[] multipliers = new float[] { 1.0f, 1.2f, 1.5f };
            int index = Mathf.Clamp(comboCount - 1, 0, multipliers.Length - 1);
            float multiplier = multipliers[index];
            comboMultiplierText.text = $"{multiplier:F1}x DAMAGE";
        }
        
        // Update progress bar
        if (comboProgressBar != null && playerCombat != null)
        {
            int maxCombo = playerCombat.IsArmed ? 3 : 2;
            float progress = (float)comboCount / maxCombo;
            comboProgressBar.fillAmount = progress;
        }
    }
    
    private void HideCombo()
    {
        if (comboPanel != null)
            comboPanel.SetActive(false);
        
        comboDisplayTimer = 0f;
        
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }
    
    private void HideHitText()
    {
        if (comboHitText != null)
            comboHitText.gameObject.SetActive(false);
    }
    
    private System.Collections.IEnumerator CoHitAnimation()
    {
        if (comboPanel == null) yield break;
        
        float elapsed = 0f;
        Vector3 startScale = comboPanel.transform.localScale;
        Vector3 targetScale = originalScale * hitScaleMultiplier;
        
        // Scale up
        while (elapsed < hitAnimationDuration * 0.5f)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / (hitAnimationDuration * 0.5f);
            comboPanel.transform.localScale = Vector3.Lerp(startScale, targetScale, t);
            yield return null;
        }
        
        // Scale down
        elapsed = 0f;
        while (elapsed < hitAnimationDuration * 0.5f)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / (hitAnimationDuration * 0.5f);
            comboPanel.transform.localScale = Vector3.Lerp(targetScale, originalScale, t);
            yield return null;
        }
        
        comboPanel.transform.localScale = originalScale;
        hitAnimationCoroutine = null;
    }
    
    void OnDestroy()
    {
        if (playerCombat != null)
        {
            playerCombat.OnComboHit -= OnComboHit;
            playerCombat.OnComboProgress -= OnComboProgress;
            playerCombat.OnComboReset -= OnComboReset;
            playerCombat.OnComboComplete -= OnComboComplete;
        }
    }
}

