using UnityEngine;
using TMPro;
using System.Collections;

public class FontChanger : MonoBehaviour
{
    [Header("Font Settings")]
    public TMP_FontAsset[] fonts; // Array of TMP font assets to cycle through
    public float changeInterval = 1f; // Time between font changes in seconds
    
    [Header("Color Settings")]
    public Color[] colors; // Array of colors to cycle through
    public bool useCustomColors = false; // Toggle to use custom colors or auto-generated reds
    
    [Header("Text Component")]
    public TextMeshProUGUI targetText; // The TMP Text component to change fonts
    
    private int currentFontIndex = 0;
    private int currentColorIndex = 0;
    private Coroutine fontChangeCoroutine;
    
    void Start()
    {
                
        // If no target text is assigned, try to get it from this GameObject
        if (targetText == null)
        {
            targetText = GetComponent<TextMeshProUGUI>();
        }
        
        // Generate default red colors if not using custom colors
        if (!useCustomColors || colors.Length == 0)
        {
            GenerateRedColors();
        }
        else
        {
            Debug.Log("FontChanger: Using custom colors, count: " + colors.Length);
        }
        
        
        // Start the font changing coroutine
        if (targetText != null && fonts.Length > 0)
        {
            fontChangeCoroutine = StartCoroutine(ChangeFontRoutine());
        }
        else
        {
        }
    }
    
    void GenerateRedColors()
    {
        // Generate darker red colors with full alpha
        colors = new Color[]
        {
            new Color(0.8f, 0.1f, 0.1f, 1f), // Dark red
            new Color(0.7f, 0.05f, 0.05f, 1f), // Darker red
            new Color(0.6f, 0.0f, 0.0f, 1f), // Very dark red
            new Color(0.9f, 0.2f, 0.2f, 1f), // Slightly lighter red
            new Color(0.5f, 0.0f, 0.0f, 1f), // Deep red
            new Color(0.75f, 0.15f, 0.15f, 1f) // Medium dark red
        };
    }
    
    IEnumerator ChangeFontRoutine()
    {
        while (true)
        {
            // Change to next font
            currentFontIndex = (currentFontIndex + 1) % fonts.Length;
            targetText.font = fonts[currentFontIndex];
            
            // Change to next color
            if (colors.Length > 0)
            {
                currentColorIndex = (currentColorIndex + 1) % colors.Length;
                targetText.color = colors[currentColorIndex];
            }
            else
            {
                Debug.LogWarning("FontChanger: No colors available!");
            }
            
            // Wait for the specified interval
            yield return new WaitForSeconds(changeInterval);
        }
    }
    
    // Public method to manually change font (can be called from other scripts)
    public void ChangeFont()
    {
        if (targetText != null && fonts.Length > 0)
        {
            currentFontIndex = (currentFontIndex + 1) % fonts.Length;
            targetText.font = fonts[currentFontIndex];
            
            if (colors.Length > 0)
            {
                currentColorIndex = (currentColorIndex + 1) % colors.Length;
                targetText.color = colors[currentColorIndex];
            }
        }
    }
    
    // Public method to stop the font changing
    public void StopFontChanging()
    {
        if (fontChangeCoroutine != null)
        {
            StopCoroutine(fontChangeCoroutine);
            fontChangeCoroutine = null;
        }
    }
    
    // Public method to start the font changing
    public void StartFontChanging()
    {
        if (fontChangeCoroutine == null && targetText != null && fonts.Length > 0)
        {
            fontChangeCoroutine = StartCoroutine(ChangeFontRoutine());
        }
    }
}
