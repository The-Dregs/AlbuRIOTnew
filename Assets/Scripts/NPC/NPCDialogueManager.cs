using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class NPCDialogueManager : MonoBehaviour
{
    public float lookDuration = 0.5f; // seconds for smooth look
    // internal references; auto-detected at runtime (not shown in Inspector)
    private Transform playerTransform;
    private Transform cameraTransform;
    private Transform npcTransform;
    public GameObject dialoguePanel;
    public TextMeshProUGUI speakerText;
    public TextMeshProUGUI dialogueText;
    public Button nextButton;
    [Header("Audio")]
    [Tooltip("Audio source for playing dialogue sounds. If not assigned, will create one automatically.")]
    public AudioSource audioSource;

    private NPCDialogueData currentDialogue;
    private int currentLine = 0;

    // centralized local input lock token
    private int inputLockToken = 0;

    // events to allow quest hooks and triggers to know when dialogue starts/ends
    public System.Action<DialogueData> OnDialogueStarted;
    public System.Action<DialogueData> OnDialogueEnded;

    void Start()
    {
        EnsureUI();
        EnsureAudioSource();
        if (dialoguePanel != null) dialoguePanel.SetActive(false);
        if (nextButton != null)
        {
            nextButton.onClick.RemoveAllListeners();
            nextButton.onClick.AddListener(NextLine);
        }
    }

    public void StartDialogue(NPCDialogueData dialogue)
    {
        if (!LocalUIManager.Ensure().TryOpen("NPCDialogue"))
        {
            Debug.Log("NPCDialogueManager: another UI is open; cannot start dialogue");
            return;
        }
        // acquire lock immediately so player can't move while we align the view
        if (inputLockToken == 0)
            inputLockToken = LocalInputLocker.Ensure().Acquire("NPCDialogue", lockMovement:true, lockCombat:true, lockCamera:true, cursorUnlock:true);
        // fallback: attempt to find local player if not set
        if (playerTransform == null)
            playerTransform = FindLocalPlayerTransform();
        // fallback: camera
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        // default npc to this manager's transform unless provided externally
        if (npcTransform == null)
            npcTransform = transform;
        StartCoroutine(SmoothLookAndPause(dialogue));
    }

    // overload that accepts explicit player/npc
    public void StartDialogue(NPCDialogueData dialogue, Transform player, Transform npc)
    {
        if (!LocalUIManager.Ensure().TryOpen("NPCDialogue"))
        {
            Debug.Log("NPCDialogueManager: another UI is open; cannot start dialogue");
            return;
        }
        if (inputLockToken == 0)
            inputLockToken = LocalInputLocker.Ensure().Acquire("NPCDialogue", lockMovement:true, lockCombat:true, lockCamera:true, cursorUnlock:true);
        if (player != null) playerTransform = player;
        if (npc != null) npcTransform = npc;
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        StartCoroutine(SmoothLookAndPause(dialogue));
    }

    private System.Collections.IEnumerator SmoothLookAndPause(NPCDialogueData dialogue)
    {
        if (playerTransform == null)
            playerTransform = FindLocalPlayerTransform();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        if (npcTransform == null)
            npcTransform = transform;
        float elapsed = 0f;
        Quaternion startPlayerRot = playerTransform != null ? playerTransform.rotation : Quaternion.identity;
        Quaternion targetPlayerRot = startPlayerRot;
        if (playerTransform != null && npcTransform != null)
        {
            Vector3 direction = npcTransform.position - playerTransform.position;
            direction.y = 0f;
            if (direction != Vector3.zero)
                targetPlayerRot = Quaternion.LookRotation(direction, Vector3.up);
        }
        Quaternion startCamRot = cameraTransform != null ? cameraTransform.rotation : Quaternion.identity;
        Quaternion targetCamRot = startCamRot;
        if (cameraTransform != null && npcTransform != null)
        {
            Vector3 camDir = npcTransform.position - cameraTransform.position;
            camDir.y = 0f;
            if (camDir != Vector3.zero)
                targetCamRot = Quaternion.LookRotation(camDir, Vector3.up);
        }
        while (elapsed < lookDuration)
        {
            float t = elapsed / lookDuration;
            if (playerTransform != null)
                playerTransform.rotation = Quaternion.Slerp(startPlayerRot, targetPlayerRot, t);
            if (cameraTransform != null)
                cameraTransform.rotation = Quaternion.Slerp(startCamRot, targetCamRot, t);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        if (playerTransform != null)
            playerTransform.rotation = targetPlayerRot;
        if (cameraTransform != null)
            cameraTransform.rotation = targetCamRot;
        currentDialogue = dialogue;
        currentLine = 0;
    EnsureUI();
    EnsureNextButtonWired();
        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(true);
            // bring dialogue canvas to front so it renders above the player HUD
            BringDialogueToFront();
        }
        // input lock was already acquired in StartDialogue; LocalInputLocker also unlocks cursor
        // bridge event for external systems (no type change to keep compatibility)
        OnDialogueStarted?.Invoke(null);
        ShowLine();
    }

    void ShowLine()
    {
        if (currentDialogue != null && currentDialogue.lines != null && currentLine < currentDialogue.lines.Length)
        {
            if (speakerText != null) speakerText.text = currentDialogue.lines[currentLine].speaker;
            if (dialogueText != null) dialogueText.text = currentDialogue.lines[currentLine].text;
            
            // Play sound clip for this line if available
            PlayLineSound(currentDialogue.lines[currentLine]);
        }
        else
        {
            EndDialogue();
        }
    }
    
    void PlayLineSound(NPCDialogueData.Line line)
    {
        if (line == null || line.soundClip == null) return;
        
        EnsureAudioSource();
        if (audioSource != null)
        {
            audioSource.PlayOneShot(line.soundClip);
        }
    }
    
    void EnsureAudioSource()
    {
        if (audioSource != null) return;
        
        // Try to find existing AudioSource on this GameObject
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            // Create new AudioSource
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f; // 2D sound
        }
    }

    public void NextLine()
    {
        currentLine++;
        ShowLine();
    }

    void EndDialogue()
    {
        if (dialoguePanel != null) dialoguePanel.SetActive(false);
    var finished = currentDialogue;
        currentDialogue = null;
        // restore local input and cursor
        if (inputLockToken != 0)
        {
            LocalInputLocker.Ensure().Release(inputLockToken);
            inputLockToken = 0;
        }
        if (LocalUIManager.Instance != null)
            LocalUIManager.Instance.Close("NPCDialogue");
        LocalInputLocker.Ensure().ForceGameplayCursor();
        OnDialogueEnded?.Invoke(null);
    }

    private void EnsureNextButtonWired()
    {
        if (dialoguePanel == null) return;
        if (nextButton == null)
        {
            // try find a button named "Next" first; fallback to any Button under panel
            var buttons = dialoguePanel.GetComponentsInChildren<Button>(true);
            foreach (var b in buttons)
            {
                if (b.name.ToLower().Contains("next")) { nextButton = b; break; }
            }
            if (nextButton == null && buttons.Length > 0) nextButton = buttons[0];
        }
        if (nextButton != null)
        {
            nextButton.onClick.RemoveAllListeners();
            nextButton.onClick.AddListener(NextLine);
            nextButton.interactable = true;
        }
    }

    private void EnsureUI()
    {
        if (dialoguePanel != null && speakerText != null && dialogueText != null && nextButton != null)
            return;

        // try to find an existing canvas child
        if (dialoguePanel == null)
        {
            var existing = transform.GetComponentInChildren<Canvas>(true);
            if (existing != null) dialoguePanel = existing.gameObject;
        }
        if (dialoguePanel != null)
        {
            if (speakerText == null) speakerText = dialoguePanel.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (dialogueText == null)
            {
                var texts = dialoguePanel.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
                if (texts != null && texts.Length > 0) dialogueText = texts[texts.Length - 1];
            }
            if (nextButton == null) nextButton = dialoguePanel.GetComponentInChildren<UnityEngine.UI.Button>(true);
            if (nextButton != null)
            {
                nextButton.onClick.RemoveAllListeners();
                nextButton.onClick.AddListener(NextLine);
            }
        }

        if (dialoguePanel != null && speakerText != null && dialogueText != null && nextButton != null)
            return;

        // create a minimal UI if not found
        var canvasGO = new GameObject("NPCDialogue_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        dialoguePanel = canvasGO;

        var panel = new GameObject("Panel", typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        var img = panel.GetComponent<Image>();
        img.color = new Color(0, 0, 0, 0.6f);
        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.1f, 0.05f);
        rect.anchorMax = new Vector2(0.9f, 0.3f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        var speakerGO = new GameObject("Speaker", typeof(TextMeshProUGUI));
        speakerGO.transform.SetParent(panel.transform, false);
        speakerText = speakerGO.GetComponent<TextMeshProUGUI>();
        speakerText.fontSize = 28;
        speakerText.alignment = TextAlignmentOptions.Left;
        var sRect = speakerGO.GetComponent<RectTransform>();
        sRect.anchorMin = new Vector2(0.02f, 0.6f);
        sRect.anchorMax = new Vector2(0.98f, 0.95f);
        sRect.offsetMin = sRect.offsetMax = Vector2.zero;

        var textGO = new GameObject("Text", typeof(TextMeshProUGUI));
        textGO.transform.SetParent(panel.transform, false);
        dialogueText = textGO.GetComponent<TextMeshProUGUI>();
        dialogueText.fontSize = 24;
        dialogueText.alignment = TextAlignmentOptions.TopLeft;
        var tRect = textGO.GetComponent<RectTransform>();
        tRect.anchorMin = new Vector2(0.02f, 0.15f);
        tRect.anchorMax = new Vector2(0.8f, 0.6f);
        tRect.offsetMin = tRect.offsetMax = Vector2.zero;

        var btnGO = new GameObject("NextButton", typeof(Button), typeof(Image));
        btnGO.transform.SetParent(panel.transform, false);
        nextButton = btnGO.GetComponent<Button>();
        var btnImg = btnGO.GetComponent<Image>();
        btnImg.color = new Color(1, 1, 1, 0.2f);
        var bRect = btnGO.GetComponent<RectTransform>();
        bRect.anchorMin = new Vector2(0.82f, 0.15f);
        bRect.anchorMax = new Vector2(0.98f, 0.35f);
        bRect.offsetMin = bRect.offsetMax = Vector2.zero;
        var btnTextGO = new GameObject("Label", typeof(TextMeshProUGUI));
        btnTextGO.transform.SetParent(btnGO.transform, false);
        var btnText = btnTextGO.GetComponent<TextMeshProUGUI>();
        btnText.text = "Next";
        btnText.alignment = TextAlignmentOptions.Center;
        btnText.fontSize = 24;
        var lbRect = btnTextGO.GetComponent<RectTransform>();
        lbRect.anchorMin = new Vector2(0, 0);
        lbRect.anchorMax = new Vector2(1, 1);
        lbRect.offsetMin = lbRect.offsetMax = Vector2.zero;
        nextButton.onClick.RemoveAllListeners();
        nextButton.onClick.AddListener(NextLine);
    }

    /// <summary>ensure the dialogue panel's canvas renders above all other canvases (player hud, etc).</summary>
    private void BringDialogueToFront()
    {
        if (dialoguePanel == null) return;

        // find the canvas that owns the dialogue panel
        Canvas canvas = dialoguePanel.GetComponent<Canvas>();
        if (canvas == null) canvas = dialoguePanel.GetComponentInParent<Canvas>();
        if (canvas == null) return;

        canvas.overrideSorting = true;
        canvas.sortingOrder = 999;
    }

    private Transform FindLocalPlayerTransform()
    {
        var t = PlayerRegistry.GetLocalPlayerTransform();
        if (t != null) return t;
        var go = GameObject.FindGameObjectWithTag("Player");
        return go != null ? go.transform : null;
    }

    // per-component locking removed in favor of LocalInputLocker
}
