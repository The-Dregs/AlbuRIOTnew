using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.Video;

public class DialogueManager : MonoBehaviourPunCallbacks
{
    // parameterless version for Unity Invoke
    public void StartDialogue()
    {
        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        // Show host control text for joiners only while dialogue is active
        if (!PhotonNetwork.IsMasterClient && hostControlText != null)
        {
            hostControlText.gameObject.SetActive(true);
            hostControlText.text = "Host is controlling the dialogue.";
        }

        if (dialogueLines.Length > 0)
        {
            currentLine = 0;
            DisplayNextLine();
        }
    }
    // background video per line (replaces background image per line)
    [Header("Background Video (per line)")]
    [Tooltip("RawImage to display the background video (behind dialogue)")]
    public RawImage backgroundVideoImage;
    [Tooltip("VideoPlayer used for background clips")]
    public VideoPlayer backgroundPlayer;
    [Tooltip("Background video clips corresponding to each dialogue line index")]
    public VideoClip[] backgroundClips;
    [Tooltip("Loop the current background clip while a line is active")]
    public bool loopClips = true;
    [Header("Dialogue UI")]
    public TextMeshProUGUI dialogueText;
    public TextMeshProUGUI hostControlText; // assign in inspector
    public GameObject dialoguePanel;
    public Button continueButton;
    public Button skipButton;

    [Header("Flicker Effect")]
    [Tooltip("enable a light flicker effect on each dialogue transition. not a fade.")]
    public bool enableFlicker = true;
    [Tooltip("full-screen black Image overlay sitting above background video and below text.")]
    public Image flickerOverlay;
    [Tooltip("duration of the 'flicker on' when a line starts.")]
    public float flickerOnDuration = 0.35f;
    [Tooltip("duration of the 'flicker off' when leaving a line.")]
    public float flickerOffDuration = 0.20f;
    [Tooltip("random interval range (seconds) between flicker pulses.")]
    public Vector2 flickerIntervalRange = new Vector2(0.03f, 0.12f);
    [Tooltip("min alpha for the dark pulses (0 clear, 1 full black)")]
    [Range(0f, 1f)] public float flickerDarkMin = 0.6f;
    [Tooltip("extra delay after flicker completes before typing starts")]
    public float postFlickerDelay = 0.05f;

    [Header("Audio")]
    [Tooltip("audio source used to play narration clips (per line).")]
    public AudioSource narrationSource;
    [Tooltip("narration clips matched to dialogue lines by index.")]
    public AudioClip[] narrationClips;
    [Tooltip("loop the narration clip.")]
    public bool narrationLoop = false;
    [Tooltip("audio source used to play ui sfx (switch on/off).")]
    public AudioSource sfxSource;
    [Tooltip("sfx for light switching ON (played at start of FlickerOn).")]
    public AudioClip switchOnSfx;
    [Tooltip("sfx for light switching OFF (played at start of FlickerOff).")]
    public AudioClip switchOffSfx;

    [Header("testing / editor")]
    [Tooltip("when enabled, continue/skip will be available locally in the editor or when not connected to photon.")]
    public bool enableLocalTestingControls = true;
    [Tooltip("force local control regardless of photon connection state (useful when you deliberately want solo control in editor).")]
    public bool forceLocalControl = false;

    private bool allowLocalControl = false; // computed at runtime

    [Header("Dialogue Content")]
    [TextArea(3, 10)]
    public string[] dialogueLines;

    [Header("Settings")]
    public float textSpeed = 0.05f;
    public string prologueScene = "Prologue"; // Change to prologue scene
    public float sceneTransitionDelay = 1f;

    private int currentLine = 0;
    private bool isTyping = false;
    private Coroutine typingCoroutine;

    void Start()
    {
        // Hide dialogue panel initially
        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        // Hide host control text initially
        if (hostControlText != null)
            hostControlText.gameObject.SetActive(false);

        // prepare flicker overlay
        if (flickerOverlay != null)
        {
            var c = flickerOverlay.color; c.a = 1f; flickerOverlay.color = c; // start black
            flickerOverlay.gameObject.SetActive(true);
        }

        // decide if we allow local control for testing
        // rules: forceLocalControl OR (not connected/in room) OR (in editor and enableLocalTestingControls)
        bool notConnectedOrNoRoom = !PhotonNetwork.IsConnected || !PhotonNetwork.InRoom;
        allowLocalControl = forceLocalControl || notConnectedOrNoRoom || (Application.isEditor && enableLocalTestingControls);

        // Set up button listeners for host, or enable for local testing
        if (PhotonNetwork.IsMasterClient || allowLocalControl)
        {
            if (continueButton != null)
                continueButton.onClick.AddListener(OnHostContinueClicked);
            if (skipButton != null)
                skipButton.onClick.AddListener(OnHostSkipClicked);
            if (continueButton != null) continueButton.gameObject.SetActive(true);
            if (skipButton != null) skipButton.gameObject.SetActive(true);
        }
        else
        {
            if (continueButton != null)
                continueButton.gameObject.SetActive(false);
            if (skipButton != null)
                skipButton.gameObject.SetActive(false);
        }
        // Start dialogue after a short delay
        Invoke("StartDialogue", 1.5f);
    }

    public void StartDialogue(DialogueData dialogue)
    {
        if (dialoguePanel != null)
            dialoguePanel.SetActive(true);

        if (dialogueLines.Length > 0)
        {
            DisplayNextLine();
        }
    }

    void DisplayNextLine()
    {
        if (currentLine < dialogueLines.Length)
        {
            if (typingCoroutine != null)
                StopCoroutine(typingCoroutine);

            // transition with flicker and then show the line
            StartCoroutine(DoLineTransitionAndShow(currentLine));
        }
        else
        {
            EndDialogue();
        }
    }

    IEnumerator ShowLineWithDelay(string text, float delay)
    {
        dialogueText.text = ""; // Clear text before delay
        yield return new WaitForSeconds(delay);
        typingCoroutine = StartCoroutine(TypeText(text));
    }

    IEnumerator TypeText(string text)
    {
        isTyping = true;
        dialogueText.text = "";

        foreach (char c in text.ToCharArray())
        {
            dialogueText.text += c;
            yield return new WaitForSeconds(textSpeed);
        }

        isTyping = false;
    }

    // called by host only
    public void OnHostContinueClicked()
    {
        if (allowLocalControl || !PhotonNetwork.IsConnected || !PhotonNetwork.InRoom)
        {
            Debug.Log("dialogue: local continue (testing mode)");
            ContinueDialogueInternal();
        }
        else
        {
            photonView.RPC("RPC_ContinueDialogue", RpcTarget.AllBuffered);
        }
    }

    [PunRPC]
    public void RPC_ContinueDialogue()
    {
        ContinueDialogueInternal();
    }

    private void ContinueDialogueInternal()
    {
        Debug.Log($"ContinueDialogueInternal called. isTyping={isTyping}, currentLine={currentLine}");
        if (isTyping)
        {
            // If still typing, complete the current line and do NOT advance
            if (typingCoroutine != null)
                StopCoroutine(typingCoroutine);
            dialogueText.text = dialogueLines[currentLine];
            isTyping = false;
            // Do not advance yet, wait for next click
        }
        else
        {
            // Move to next line
            currentLine++;
            DisplayNextLine();
        }
    }

    // called by host only
    public void OnHostSkipClicked()
    {
        if (allowLocalControl || !PhotonNetwork.IsConnected || !PhotonNetwork.InRoom)
        {
            Debug.Log("dialogue: local skip (testing mode)");
            SkipDialogueInternal();
        }
        else
        {
            photonView.RPC("RPC_SkipDialogue", RpcTarget.AllBuffered);
        }
    }

    [PunRPC]
    public void RPC_SkipDialogue()
    {
        SkipDialogueInternal();
    }

    private void SkipDialogueInternal()
    {
        // Skip directly to main game
        EndDialogue();
    }

    private bool hasEndedDialogue = false;
    void EndDialogue()
    {
        // guard against duplicate scene loads
        if (hasEndedDialogue) return;
        hasEndedDialogue = true;
        Debug.Log("Dialogue finished, loading main game...");

        // Hide host control text when dialogue ends
        if (hostControlText != null)
            hostControlText.gameObject.SetActive(false);

        // stop background video
        if (backgroundPlayer != null)
        {
            backgroundPlayer.Stop();
        }
        if (backgroundVideoImage != null)
        {
            backgroundVideoImage.gameObject.SetActive(false);
        }

        // ensure overlay not blocking after end
        if (flickerOverlay != null)
        {
            var c = flickerOverlay.color; c.a = 0f; flickerOverlay.color = c;
        }

        // stop audio
        if (narrationSource != null) narrationSource.Stop();
        if (sfxSource != null) sfxSource.Stop();

        // Show loading screen and load next scene
        var loader = FindFirstObjectByType<LoadingScreenManager>();
        if (loader != null)
        {
            loader.LoadSceneAsync("TUTORIAL");
        }
        else
        {
            Photon.Pun.PhotonNetwork.LoadLevel("TUTORIAL");
        }
    }


    // ---------------- background video helpers ----------------
    void SetupBackgroundVideoForLine(int index)
    {
        if (backgroundPlayer == null || backgroundVideoImage == null || backgroundClips == null || index >= backgroundClips.Length)
        {
            return;
        }

        var clip = backgroundClips[index];
        if (clip == null)
        {
            return;
        }

        StartCoroutine(PrepareAndPlayBackground(index));
    }

    IEnumerator PrepareAndPlayBackground(int index)
    {
        var clip = backgroundClips[index];
        if (clip == null) yield break;

        backgroundPlayer.Stop();
        backgroundPlayer.isLooping = loopClips;
        backgroundPlayer.renderMode = VideoRenderMode.APIOnly;
        backgroundPlayer.clip = clip;

        backgroundVideoImage.gameObject.SetActive(true);

        backgroundPlayer.Prepare();
        while (!backgroundPlayer.isPrepared)
            yield return null;

        backgroundVideoImage.texture = backgroundPlayer.texture;
        backgroundPlayer.Play();
    }

    // ---------------- flicker helpers ----------------
    IEnumerator DoLineTransitionAndShow(int index)
    {
        bool isFirstLine = index == 0;

        if (enableFlicker && flickerOverlay != null && !isFirstLine)
        {
            // play switch off at the start of flicker off
            PlaySfx(switchOffSfx);
            yield return FlickerOff();
        }

        // update background (prepare + play)
        SetupBackgroundVideoForLine(index);

        if (enableFlicker && flickerOverlay != null)
        {
            // play switch on at the start of flicker on
            PlaySfx(switchOnSfx);
            yield return FlickerOn();
        }

        // small delay after flicker to settle
        if (postFlickerDelay > 0f)
            yield return new WaitForSeconds(postFlickerDelay);

        // play narration for this line (if any)
        PlayNarrationForIndex(index);

        // type the line
        typingCoroutine = StartCoroutine(ShowLineWithDelay(dialogueLines[index], 0f));
    }

    IEnumerator FlickerOn()
    {
        float elapsed = 0f;
        SetOverlayAlpha(1f); // start dark
        while (elapsed < flickerOnDuration)
        {
            // random pulses between fully on (clear) and dark flashes
            float next = Random.value < 0.5f ? 0f : Random.Range(flickerDarkMin, 1f);
            SetOverlayAlpha(next);
            float wait = Random.Range(flickerIntervalRange.x, flickerIntervalRange.y);
            elapsed += wait;
            yield return new WaitForSeconds(wait);
        }
        SetOverlayAlpha(0f); // end with lights on (clear)
    }

    IEnumerator FlickerOff()
    {
        float elapsed = 0f;
        // start mostly lit
        SetOverlayAlpha(0f);
        while (elapsed < flickerOffDuration)
        {
            float next = Random.value < 0.5f ? 1f : Random.Range(flickerDarkMin, 1f);
            // also sprinkle clears to feel like unstable power
            if (Random.value < 0.25f) next = 0f;
            SetOverlayAlpha(next);
            float wait = Random.Range(flickerIntervalRange.x, flickerIntervalRange.y);
            elapsed += wait;
            yield return new WaitForSeconds(wait);
        }
        SetOverlayAlpha(1f); // end with lights off (black)
    }

    void SetOverlayAlpha(float a)
    {
        if (flickerOverlay == null) return;
        var c = flickerOverlay.color;
        c.a = Mathf.Clamp01(a);
        flickerOverlay.color = c;
    }

    // ---------------- audio helpers ----------------
    void PlayNarrationForIndex(int index)
    {
        if (narrationSource == null || narrationClips == null || index >= narrationClips.Length)
            return;
        var clip = narrationClips[index];
        if (clip == null) return;
        narrationSource.loop = narrationLoop;
        narrationSource.clip = clip;
        narrationSource.Play();
    }

    void PlaySfx(AudioClip clip)
    {
        if (sfxSource == null || clip == null) return;
        sfxSource.PlayOneShot(clip);
    }
}
