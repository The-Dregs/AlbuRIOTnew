using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class FirstEncounterDialogueUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject dialoguePanel;
    public TextMeshProUGUI enemyNameText;
    public TextMeshProUGUI dialogueText;
    public Image enemyIconImage;
    public Button continueButton;
    
    [Header("Settings")]
    [Tooltip("Time before auto-closing if no input")]
    public float autoCloseDelay = 5f;
    [Tooltip("Typing speed for text")]
    public float typingSpeed = 0.05f;
    
    private Coroutine typingCoroutine;
    private bool isTyping = false;
    private bool isShowing = false;
    
    void Awake()
    {
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);
            
        if (continueButton != null)
            continueButton.onClick.AddListener(OnContinueClicked);
    }
    
    public void ShowFirstEncounter(EncyclopediaEntry entry)
    {
        if (entry == null || isShowing) return;
        
        isShowing = true;
        
        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);
        
        // Set enemy name
        if (enemyNameText != null)
            enemyNameText.text = entry.displayName;
        
        // Set icon
        if (enemyIconImage != null && entry.icon != null)
        {
            enemyIconImage.sprite = entry.icon;
            enemyIconImage.gameObject.SetActive(true);
        }
        else if (enemyIconImage != null)
        {
            enemyIconImage.gameObject.SetActive(false);
        }
        
        // Type out the dialogue text
        string textToShow = !string.IsNullOrEmpty(entry.firstEncounterText) 
            ? entry.firstEncounterText 
            : $"You've encountered a {entry.displayName}!";
            
        if (typingCoroutine != null)
            StopCoroutine(typingCoroutine);
        typingCoroutine = StartCoroutine(TypeText(textToShow));
        
        // Auto-close after delay
        StartCoroutine(AutoCloseAfterDelay());
        
        // Lock input (optional - can be configured)
        var inputLocker = LocalInputLocker.Ensure();
        if (inputLocker != null)
        {
            // Only lock combat/movement, allow UI interaction
            // This is handled by the continue button
        }
    }
    
    IEnumerator TypeText(string text)
    {
        isTyping = true;
        if (dialogueText != null)
            dialogueText.text = "";
        
        foreach (char c in text.ToCharArray())
        {
            if (dialogueText != null)
                dialogueText.text += c;
            yield return new WaitForSeconds(typingSpeed);
        }
        
        isTyping = false;
    }
    
    IEnumerator AutoCloseAfterDelay()
    {
        yield return new WaitForSeconds(autoCloseDelay);
        if (isShowing)
        {
            CloseDialogue();
        }
    }
    
    public void OnContinueClicked()
    {
        if (isTyping)
        {
            // Complete typing immediately
            if (typingCoroutine != null)
                StopCoroutine(typingCoroutine);
            isTyping = false;
            // Text is already set, just mark as done
        }
        else
        {
            CloseDialogue();
        }
    }
    
    private void CloseDialogue()
    {
        isShowing = false;
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);
        
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }
    }
    
    void Update()
    {
        // Allow closing with any key or mouse click
        if (isShowing && (Input.anyKeyDown || Input.GetMouseButtonDown(0)))
        {
            OnContinueClicked();
        }
    }
}

