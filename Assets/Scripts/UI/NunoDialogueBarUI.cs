using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// bottom-screen dialogue bar for nuno tutorial guidance.
/// shows speaker name, message with typing effect, and plays optional voiceline.
/// </summary>
public class NunoDialogueBarUI : MonoBehaviour
{
    public static NunoDialogueBarUI Instance { get; private set; }

    [Header("UI References")]
    public GameObject dialogueBarPanel;
    public TextMeshProUGUI speakerNameText;
    public TextMeshProUGUI messageText;
    public Image speakerIcon;
    public Button continueButton;

    [Header("Settings")]
    public float typingSpeed = 0.03f;
    public float autoCloseDelay = 6f;

    [Header("Audio")]
    public AudioSource voiceSource;

    private Coroutine typingCoroutine;
    private Coroutine autoCloseCoroutine;
    private bool isTyping;
    private bool isShowing;
    private string fullMessage;
    private System.Action onCloseCallback;

    // multi-line sequence state
    private string[] sequenceLines;
    private AudioClip[] sequenceVoiceClips;
    private int sequenceIndex;
    private string sequenceSpeaker;
    private Sprite sequenceIcon;

    // the active audio source for the current dialogue (may be overridden per-call)
    private AudioSource activeVoiceSource;

    // input lock token — released when dialogue closes
    private int inputLockToken;

    private const string UI_OWNER = "NunoDialogue";
    private const string INPUT_LOCK_OWNER = "NunoDialogue";

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // ensure this dialogue bar renders above player hud / other ui and receives clicks
        var parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null)
        {
            parentCanvas = gameObject.AddComponent<Canvas>();
            parentCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<GraphicRaycaster>();
        }
        parentCanvas.overrideSorting = true;
        parentCanvas.sortingOrder = 5000;

        if (dialogueBarPanel != null)
            dialogueBarPanel.SetActive(false);

        if (continueButton != null)
        {
            continueButton.onClick.RemoveAllListeners();
            continueButton.onClick.AddListener(OnContinueClicked);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (!isShowing) return;
        if (Input.GetKeyDown(KeyCode.Escape))
            OnContinueClicked();
        // keyboard fallback for advancing dialogue (in case button clicks don't register)
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            OnContinueClicked();
    }

    /// <summary>
    /// show a sequence of dialogue lines. the player advances through them with the continue button.
    /// onClose fires after the last line is dismissed.
    /// </summary>
    public void ShowDialogueSequence(string speaker, string[] lines, Sprite icon = null, System.Action onClose = null, AudioClip[] voiceClips = null, AudioSource overrideAudioSource = null)
    {
        if (lines == null || lines.Length == 0)
        {
            onClose?.Invoke();
            return;
        }

        var ui = LocalUIManager.Ensure();
        if (ui != null && !ui.TryOpen(UI_OWNER))
        {
            Debug.Log($"[NunoDialogueBarUI] blocked by '{ui.CurrentOwner}'");
            return;
        }

        // resolve which audio source to use: override > inspector-assigned > null
        activeVoiceSource = overrideAudioSource != null ? overrideAudioSource : voiceSource;
        if (activeVoiceSource == null)
            Debug.LogWarning("[NunoDialogueBarUI] no AudioSource available — voice clips will not play. assign one on NunoMerchantTrigger or NunoDialogueBarUI.");

        sequenceLines = lines;
        sequenceVoiceClips = voiceClips;
        sequenceIndex = 0;
        sequenceSpeaker = speaker;
        sequenceIcon = icon;
        onCloseCallback = onClose;

        Debug.Log($"[NunoDialogueBarUI] starting dialogue sequence: {lines.Length} lines, {(voiceClips != null ? voiceClips.Length : 0)} voice clips, audioSource={(activeVoiceSource != null ? activeVoiceSource.gameObject.name : "NONE")}");

        AcquireInputLock();
        ShowSequenceLine();
    }

    private void ShowSequenceLine()
    {
        if (sequenceIndex >= sequenceLines.Length)
        {
            // all lines shown, close and fire callback
            sequenceLines = null;
            CloseDialogue();
            return;
        }

        // reset state for the new line
        isShowing = true;
        fullMessage = sequenceLines[sequenceIndex];

        if (dialogueBarPanel != null)
            dialogueBarPanel.SetActive(true);

        if (continueButton != null)
            continueButton.interactable = true;

        if (speakerNameText != null)
            speakerNameText.text = sequenceSpeaker;

        if (speakerIcon != null)
        {
            if (sequenceIcon != null)
            {
                speakerIcon.sprite = sequenceIcon;
                speakerIcon.gameObject.SetActive(true);
            }
            else
            {
                speakerIcon.gameObject.SetActive(false);
            }
        }

        if (typingCoroutine != null)
            StopCoroutine(typingCoroutine);
        typingCoroutine = StartCoroutine(TypeText(fullMessage));

        // stop previous voice and play per-line clip if available
        if (activeVoiceSource != null && activeVoiceSource.isPlaying)
            activeVoiceSource.Stop();
        if (sequenceVoiceClips != null && sequenceIndex < sequenceVoiceClips.Length && sequenceVoiceClips[sequenceIndex] != null)
        {
            if (activeVoiceSource != null)
            {
                activeVoiceSource.clip = sequenceVoiceClips[sequenceIndex];
                activeVoiceSource.Play();
                Debug.Log($"[NunoDialogueBarUI] playing voice clip [{sequenceIndex}]: {sequenceVoiceClips[sequenceIndex].name} on {activeVoiceSource.gameObject.name} (volume={activeVoiceSource.volume}, mute={activeVoiceSource.mute}, enabled={activeVoiceSource.enabled})");
            }
            else
            {
                Debug.LogWarning($"[NunoDialogueBarUI] voice clip [{sequenceIndex}] available but no AudioSource to play it");
            }
        }
        else if (sequenceVoiceClips == null || sequenceIndex >= sequenceVoiceClips.Length)
        {
            Debug.Log($"[NunoDialogueBarUI] no voice clip for line [{sequenceIndex}]");
        }

        // for multi-line sequences we do not auto-advance on a timer; the player
        // explicitly advances using the continue button or keyboard. this keeps
        // the text and voicelines in sync and avoids accidental skips.
        if (autoCloseCoroutine != null)
        {
            StopCoroutine(autoCloseCoroutine);
            autoCloseCoroutine = null;
        }
    }

    /// <summary>
    /// show the dialogue bar with a message and optional voiceline.
    /// </summary>
    public void ShowDialogue(string speaker, string message, AudioClip voiceClip = null, Sprite icon = null, System.Action onClose = null, AudioSource overrideAudioSource = null)
    {
        if (isShowing) return;

        var ui = LocalUIManager.Ensure();
        if (ui != null && !ui.TryOpen(UI_OWNER))
        {
            Debug.Log($"[NunoDialogueBarUI] blocked by '{ui.CurrentOwner}'");
            return;
        }

        // resolve which audio source to use
        activeVoiceSource = overrideAudioSource != null ? overrideAudioSource : voiceSource;

        isShowing = true;
        fullMessage = message;
        onCloseCallback = onClose;

        AcquireInputLock();

        if (dialogueBarPanel != null)
            dialogueBarPanel.SetActive(true);

        if (continueButton != null)
            continueButton.interactable = true;

        if (speakerNameText != null)
            speakerNameText.text = speaker;

        if (speakerIcon != null)
        {
            if (icon != null)
            {
                speakerIcon.sprite = icon;
                speakerIcon.gameObject.SetActive(true);
            }
            else
            {
                speakerIcon.gameObject.SetActive(false);
            }
        }

        // play voiceline
        if (voiceClip != null && activeVoiceSource != null)
        {
            activeVoiceSource.clip = voiceClip;
            activeVoiceSource.Play();
        }

        // start typing effect
        if (typingCoroutine != null)
            StopCoroutine(typingCoroutine);
        typingCoroutine = StartCoroutine(TypeText(message));

        // auto-close after delay
        if (autoCloseCoroutine != null)
            StopCoroutine(autoCloseCoroutine);
        autoCloseCoroutine = StartCoroutine(AutoCloseAfterDelay());
    }

    private IEnumerator TypeText(string text)
    {
        isTyping = true;
        if (messageText != null)
            messageText.text = "";

        foreach (char c in text)
        {
            if (messageText != null)
                messageText.text += c;
            yield return new WaitForSeconds(typingSpeed);
        }

        isTyping = false;
    }

    private IEnumerator AutoCloseAfterDelay()
    {
        yield return new WaitForSeconds(autoCloseDelay);
        if (!isShowing) yield break;

        // only single-line dialogues auto-close; multi-line sequences are advanced
        // exclusively by the continue button / keyboard so the player controls pacing.
        if (sequenceLines == null)
            CloseDialogue();
    }

    public void OnContinueClicked()
    {
        if (isTyping)
        {
            // complete typing immediately
            if (typingCoroutine != null)
                StopCoroutine(typingCoroutine);
            isTyping = false;
            if (messageText != null)
                messageText.text = fullMessage;
        }
        else if (sequenceLines != null)
        {
            // advance to next line in sequence
            if (autoCloseCoroutine != null)
            {
                StopCoroutine(autoCloseCoroutine);
                autoCloseCoroutine = null;
            }
            isShowing = false;
            sequenceIndex++;
            ShowSequenceLine();
        }
        else
        {
            CloseDialogue();
        }
    }

    private void AcquireInputLock()
    {
        if (inputLockToken != 0) return;
        var locker = LocalInputLocker.Ensure();
        if (locker != null)
            inputLockToken = locker.Acquire(INPUT_LOCK_OWNER, lockMovement: true, lockCombat: true, lockCamera: true, cursorUnlock: true);
    }

    private void ReleaseInputLock()
    {
        if (inputLockToken == 0) return;
        var locker = LocalInputLocker.Ensure();
        if (locker != null)
            locker.Release(inputLockToken);
        inputLockToken = 0;
    }

    private void CloseDialogue()
    {
        isShowing = false;
        sequenceLines = null;
        sequenceVoiceClips = null;

        ReleaseInputLock();

        var ui = LocalUIManager.Ensure();
        if (ui != null && ui.IsOwner(UI_OWNER))
            ui.Close(UI_OWNER);

        if (autoCloseCoroutine != null)
        {
            StopCoroutine(autoCloseCoroutine);
            autoCloseCoroutine = null;
        }

        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }

        if (activeVoiceSource != null && activeVoiceSource.isPlaying)
            activeVoiceSource.Stop();

        if (dialogueBarPanel != null)
            dialogueBarPanel.SetActive(false);

        var callback = onCloseCallback;
        onCloseCallback = null;
        callback?.Invoke();
    }

    public bool IsShowing => isShowing;
}
