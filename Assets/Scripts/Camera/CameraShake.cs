using UnityEngine;
using System.Collections;

/// <summary>
/// Handles camera shake effects for impacts, hits, and other events.
/// Attach to camera or camera rig.
/// </summary>
public class CameraShake : MonoBehaviour
{
    [Header("Shake Settings")]
    [Tooltip("Intensity multiplier for all shakes")]
    [Range(0f, 2f)] public float globalIntensityMultiplier = 1f;
    
    [Header("Hit Enemy Shake")]
    [Tooltip("Shake intensity when hitting an enemy")]
    [Range(0f, 1f)] public float hitEnemyIntensity = 0.3f;
    [Tooltip("Shake duration when hitting an enemy")]
    [Range(0f, 1f)] public float hitEnemyDuration = 0.15f;
    
    [Header("Get Hit Shake")]
    [Tooltip("Shake intensity when player takes damage")]
    [Range(0f, 1f)] public float getHitIntensity = 0.5f;
    [Tooltip("Shake duration when player takes damage")]
    [Range(0f, 1f)] public float getHitDuration = 0.2f;
    
    [Header("Hit Plant Shake")]
    [Tooltip("Shake intensity when hitting a plant")]
    [Range(0f, 1f)] public float hitPlantIntensity = 0.2f;
    [Tooltip("Shake duration when hitting a plant")]
    [Range(0f, 1f)] public float hitPlantDuration = 0.1f;
    
    [Header("Advanced")]
    [Tooltip("Smoothness of shake decay (higher = smoother)")]
    [Range(1f, 10f)] public float shakeSmoothness = 5f;
    
    private Vector3 originalPosition;
    private Coroutine shakeCoroutine;
    private bool isShaking = false;
    
    void Start()
    {
        // Store original local position
        originalPosition = transform.localPosition;
    }
    
    /// <summary>
    /// Shake camera with custom intensity and duration
    /// </summary>
    public void Shake(float intensity, float duration)
    {
        if (intensity <= 0f || duration <= 0f) return;
        
        float finalIntensity = intensity * globalIntensityMultiplier;
        
        if (shakeCoroutine != null)
        {
            StopCoroutine(shakeCoroutine);
        }
        
        shakeCoroutine = StartCoroutine(CoShake(finalIntensity, duration));
    }
    
    /// <summary>
    /// Shake camera when hitting an enemy
    /// </summary>
    public void ShakeHitEnemy()
    {
        Shake(hitEnemyIntensity, hitEnemyDuration);
    }
    
    /// <summary>
    /// Shake camera when player gets hit
    /// </summary>
    public void ShakeGetHit()
    {
        Shake(getHitIntensity, getHitDuration);
    }
    
    /// <summary>
    /// Shake camera when hitting a plant
    /// </summary>
    public void ShakeHitPlant()
    {
        Shake(hitPlantIntensity, hitPlantDuration);
    }
    
    private IEnumerator CoShake(float intensity, float duration)
    {
        isShaking = true;
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            // Calculate shake strength (decays over time)
            float progress = elapsed / duration;
            float strength = intensity * (1f - progress); // Linear decay
            
            // Generate random offset
            Vector3 offset = new Vector3(
                Random.Range(-1f, 1f) * strength,
                Random.Range(-1f, 1f) * strength,
                Random.Range(-1f, 1f) * strength * 0.5f // Less shake on Z axis
            );
            
            // Apply shake with smoothing
            transform.localPosition = Vector3.Lerp(
                transform.localPosition,
                originalPosition + offset,
                Time.deltaTime * shakeSmoothness
            );
            
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        // Smooth return to original position
        while (Vector3.Distance(transform.localPosition, originalPosition) > 0.01f)
        {
            transform.localPosition = Vector3.Lerp(
                transform.localPosition,
                originalPosition,
                Time.deltaTime * shakeSmoothness
            );
            yield return null;
        }
        
        transform.localPosition = originalPosition;
        isShaking = false;
        shakeCoroutine = null;
    }
    
    /// <summary>
    /// Stop any ongoing shake immediately
    /// </summary>
    public void StopShake()
    {
        if (shakeCoroutine != null)
        {
            StopCoroutine(shakeCoroutine);
            shakeCoroutine = null;
        }
        transform.localPosition = originalPosition;
        isShaking = false;
    }
    
    void OnDestroy()
    {
        // Stop shake coroutine
        StopShake();
    }
}

