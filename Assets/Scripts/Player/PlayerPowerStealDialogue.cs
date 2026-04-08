using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Simple, player-local dialogue for explaining power stealing and the skill bar.
/// - Listens to PowerStealManager.OnPowerStolen on the same GameObject.
/// - Optional: detects nearby enemies to show a brief introduction line + voiceline the first time you meet them.
/// - Shows a short tutorial the first time ANY power is stolen.
/// - Shows a brief line the first time a power is stolen from each enemy type.
/// Wire this to your own dialogue UI under the player.
/// </summary>
public class PlayerPowerStealDialogue : MonoBehaviour
{
    [Header("Dialogue UI")]
    [Tooltip("Root panel GameObject for the dialogue (will be SetActive(true/false)).")]
    public GameObject dialoguePanel;

    [Tooltip("TextMeshProUGUI used to display the dialogue text.")]
    public TextMeshProUGUI dialogueText;

    [Tooltip("Optional prefix for the speaker name, e.g. \"Nuno: \". Left empty for no prefix.")]
    public string speakerPrefix = "Nuno: ";

    [Header("Behavior")]
    [Tooltip("Seconds before the dialogue auto-closes if the player does nothing. 0 = no auto-close.")]
    public float autoCloseDelay = 6f;

    [Tooltip("If true, any key press or mouse click will close the current dialogue.")]
    public bool closeOnAnyInput = true;

     [Tooltip("Seconds between each character when typing out dialogue text. 0 = no typing effect.")]
     public float typingSpeed = 0.03f;

    [Header("Skill Bar HUD")]
    [Tooltip("Optional root GameObject for the skill bar HUD. Will be activated when the first stolen power tutorial starts.")]
    public GameObject skillBarRoot;

    [Tooltip("If true, the skill bar HUD is activated when the first kill tutorial begins.")]
    public bool activateSkillBarOnFirstKill = true;

    [Header("Intro Detection (optional)")]
    [Tooltip("If true, the player will auto-introduce nearby enemies before you fight them.")]
    public bool enableProximityIntro = true;

    [Tooltip("How far to look for enemies to introduce.")]
    public float introRadius = 20f;

    [Tooltip("Minimum seconds between intro dialogues (prevents spam when many enemies are around).")]
    public float introCooldown = 8f;

    [Tooltip("How often (in seconds) to scan for nearby enemies.")]
    public float introCheckInterval = 0.5f;

    [Tooltip("LayerMask used to find enemies via Physics.OverlapSphere. Leave as Default if you prefer script-based lookup.")]
    public LayerMask enemyLayerMask = -1;

    [Header("Intro Voicelines (optional)")]
    [Tooltip("Enemy IDs to match against the PowerSteal/EnemyData names for intro voicelines.")]
    public string[] introEnemyIds;

    [Tooltip("One clip per introEnemyIds entry. If set, the matching clip will play when that enemy is first introduced.")]
    public AudioClip[] introVoiceClips;

    [Tooltip("AudioSource used to play intro voicelines. Leave null to skip audio.")]
    public AudioSource introVoiceSource;

    [Header("First Kill Tutorial")]
    [Tooltip("Voiceline to play during the first-ever power steal tutorial sequence.")]
    public AudioClip firstKillVoiceClip;

    [Tooltip("Optional AudioSource for the first kill voiceline. If null, Intro Voice Source is used.")]
    public AudioSource firstKillVoiceSource;

    [Tooltip("Seconds each line of the first kill tutorial stays on screen before auto-advancing. If 0, falls back to Auto Close Delay.")]
    public float firstKillLineDelay = 4f;

    private PowerStealManager powerStealManager;
    private bool hasShownFirstPowerDialogue;
    private readonly HashSet<string> explainedEnemyIds = new HashSet<string>();
    private readonly HashSet<string> introducedEnemyIds = new HashSet<string>();

    private Coroutine autoCloseRoutine;
    private bool isShowing;
    private float nextIntroAllowedTime;
    private float nextIntroCheckTime;
    private Coroutine firstKillSequenceRoutine;
    private Coroutine typingRoutine;
    private bool introCooldownPending;

    void Awake()
    {
        powerStealManager = GetComponent<PowerStealManager>();
        if (powerStealManager != null)
        {
            powerStealManager.OnPowerStolen += HandlePowerStolen;
        }

        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        // Ensure any pre-assigned voice sources are configured for 2D UI playback.
        if (introVoiceSource != null)
            ConfigureVoiceSource(introVoiceSource);
        if (firstKillVoiceSource != null)
            ConfigureVoiceSource(firstKillVoiceSource);
    }

    void OnDestroy()
    {
        if (powerStealManager != null)
        {
            powerStealManager.OnPowerStolen -= HandlePowerStolen;
        }
    }

    void Update()
    {
        // Handle closing current dialogue via input.
        if (isShowing)
        {
            if (closeOnAnyInput && (Input.anyKeyDown || Input.GetMouseButtonDown(0)))
            {
                HideDialogue();
            }
        }

        // Handle proximity-based enemy introductions.
        if (!enableProximityIntro) return;

        // While the first-kill tutorial sequence is running, suppress all enemy
        // intro detection and voicelines so the messages do not overlap.
        if (firstKillSequenceRoutine != null) return;

        // Only the local player should drive intros.
        var pv = GetComponent<Photon.Pun.PhotonView>();
        if (pv != null && Photon.Pun.PhotonNetwork.IsConnected && !pv.IsMine)
            return;

        // Do not start a new intro while a dialogue is already on-screen.
        if (isShowing) return;

        // Simple timer-based check to avoid scanning every frame.
        if (Time.time < nextIntroCheckTime) return;
        nextIntroCheckTime = Time.time + Mathf.Max(0.05f, introCheckInterval);

        // Respect cooldown so many enemies do not spam intros.
        if (Time.time < nextIntroAllowedTime) return;

        TryRunProximityIntro();
    }

    private void HandlePowerStolen(string enemyId)
    {
        // If we are already running the first-kill tutorial sequence, ignore any
        // additional power-steal callbacks until it finishes to avoid overlapping text.
        if (firstKillSequenceRoutine != null)
            return;

        // Only run for the local player (in case this script exists on remote avatars).
        var pv = GetComponent<Photon.Pun.PhotonView>();
        if (pv != null && Photon.Pun.PhotonNetwork.IsConnected && !pv.IsMine)
            return;

        if (string.IsNullOrEmpty(enemyId))
            enemyId = "that creature";

        // First-ever power stolen for this player: tutorial about skills HUD.
        if (!hasShownFirstPowerDialogue)
        {
            hasShownFirstPowerDialogue = true;

            // Make sure we don't overlap with a previous intro; replace any existing text.
            if (isShowing)
                HideDialogue();

            StartFirstKillSequence();
        }
        else if (!explainedEnemyIds.Contains(enemyId))
        {
            // First time stealing from this specific enemy type.
            explainedEnemyIds.Add(enemyId);

            string message =
                $"The spirit of the {enemyId} now answers your call.\n" +
                "Each kind of foe grants a different trick — learn when to use them.";

            if (isShowing)
                HideDialogue();

            ShowDialogue(message);
        }
        // Subsequent steals from the same enemy type: no additional dialogue.
    }

    private void TryRunProximityIntro()
    {
        // Use a simple physics overlap to find the nearest enemy around the player.
        Collider[] hits = Physics.OverlapSphere(transform.position, introRadius, enemyLayerMask);
        if (hits == null || hits.Length == 0) return;

        Transform nearest = null;
        float nearestSqr = float.MaxValue;
        string nearestId = null;

        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;

            // Try to find a BaseEnemyAI on this collider or its parent.
            var enemy = col.GetComponentInParent<BaseEnemyAI>();
            if (enemy == null || enemy.enemyData == null) continue;

            string id = !string.IsNullOrEmpty(enemy.enemyData.enemyName)
                ? enemy.enemyData.enemyName
                : enemy.gameObject.name;

            if (string.IsNullOrEmpty(id)) continue;

            // Skip enemies we've already introduced for this player.
            if (introducedEnemyIds.Contains(id)) continue;

            float sqr = (enemy.transform.position - transform.position).sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = enemy.transform;
                nearestId = id;
            }
        }

        if (nearest == null || string.IsNullOrEmpty(nearestId)) return;

        // Mark as introduced so we only explain each enemy type once per player.
        introducedEnemyIds.Add(nearestId);
        // Defer starting the cooldown timer until after this intro dialogue closes
        // (so the cooldown counts from the end of the line / voiceline, not the start).
        introCooldownPending = true;

        string introMessage = GetIntroMessageForEnemy(nearestId);
        PlayIntroVoiceFor(nearestId);
        ShowDialogue(introMessage);
    }

    private void PlayIntroVoiceFor(string enemyId)
    {
        if (introVoiceSource == null || introEnemyIds == null || introVoiceClips == null)
            return;

        for (int i = 0; i < introEnemyIds.Length && i < introVoiceClips.Length; i++)
        {
            if (string.Equals(introEnemyIds[i], enemyId, System.StringComparison.OrdinalIgnoreCase)
                && introVoiceClips[i] != null)
            {
                ConfigureVoiceSource(introVoiceSource);
                introVoiceSource.PlayOneShot(introVoiceClips[i]);
                return;
            }
        }
    }

    private void StartFirstKillSequence()
    {
        if (firstKillSequenceRoutine != null)
        {
            StopCoroutine(firstKillSequenceRoutine);
            firstKillSequenceRoutine = null;
        }

        if (activateSkillBarOnFirstKill && skillBarRoot != null)
        {
            skillBarRoot.SetActive(true);
        }

        string[] lines =
        {
            "AHA! I knew it! You have the mark of an al bu laryo!",
            "When you cleanse powerful spirits, they leave echoes you can wield.",
            "Watch your skill bar — new abilities will appear there when you steal their powers.",
            "These stolen powers vary per spirits and are limited, so use them wisely!"
        };

        firstKillSequenceRoutine = StartCoroutine(FirstKillSequenceRoutine(lines));
    }

    private IEnumerator FirstKillSequenceRoutine(string[] lines)
    {
        if (lines == null || lines.Length == 0)
            yield break;

        isShowing = true;

        // stop any previous auto-close and voice
        if (autoCloseRoutine != null)
        {
            StopCoroutine(autoCloseRoutine);
            autoCloseRoutine = null;
        }

        if (typingRoutine != null)
        {
            StopCoroutine(typingRoutine);
            typingRoutine = null;
        }

        PlayFirstKillVoiceline();

        float perLineDelay = firstKillLineDelay > 0f ? firstKillLineDelay : Mathf.Max(0.5f, autoCloseDelay);

        for (int i = 0; i < lines.Length; i++)
        {
            if (dialoguePanel == null || dialogueText == null)
                break;

            dialoguePanel.SetActive(true);

            string message = lines[i] ?? string.Empty;
            string finalText = !string.IsNullOrEmpty(speakerPrefix) ? speakerPrefix + message : message;

            // Type this line out character by character, but keep it fully
            // contained in this coroutine to avoid overlapping typing routines.
            dialogueText.text = string.Empty;
            if (typingSpeed > 0f)
            {
                for (int c = 0; c < finalText.Length && isShowing; c++)
                {
                    dialogueText.text += finalText[c];
                    yield return new WaitForSeconds(typingSpeed);
                }
            }
            else
            {
                dialogueText.text = finalText;
            }

            // After the line is fully shown, wait a bit before moving to the next.
            float elapsed = 0f;
            while (elapsed < perLineDelay && isShowing)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!isShowing)
                break;
        }

        // after the sequence of lines, wait for the first-kill voiceline to finish (if any)
        AudioSource src = firstKillVoiceSource != null ? firstKillVoiceSource : introVoiceSource;
        if (src != null)
        {
            while (isShowing && src.isPlaying)
            {
                yield return null;
            }
        }

        if (isShowing)
            HideDialogue();

        firstKillSequenceRoutine = null;
    }

    private void PlayFirstKillVoiceline()
    {
        if (firstKillVoiceClip == null)
            return;

        AudioSource src = firstKillVoiceSource != null ? firstKillVoiceSource : introVoiceSource;
        if (src == null)
            return;

        ConfigureVoiceSource(src);
        src.PlayOneShot(firstKillVoiceClip);
    }

    private string GetIntroMessageForEnemy(string enemyId)
    {
        if (string.IsNullOrEmpty(enemyId))
            return "Be careful. Learn how these spirits move before you strike.";

        switch (enemyId)
        {
            case "Aswang":
                return "Ah—an Aswang… a shapeshifter that hunts at night. Hm… do not trust what you see. When it crouches—watch carefully! It is about to leap at you.";

            case "Tikbalang":
                return "A Tikbalang—half-man, half-horse. It leads travelers astray and tramples those who wander. When it lowers its head, it will charge. When it rears up, it will stomp the ground.";

            case "Bakunawa":
                return "The Bakunawa—the serpent that swallows the moon. It is ancient and very dangerous. It strikes with its head, spits a beam of water, calls a wave, and whips its tail to raise a whirlwind. Stay alert.";

            case "Minokawa":
                return "The Minokawa—a dragon-bird that lives in the sky. It can swallow the sun and moon. It cuts a line of light in front of it and slams its wings down. Do not stand in its path.";

            case "Berberoka":
                return "The Berberoka—it lives in the water and drowns those who come too close. It pulls you into its vortex and floods the ground with water. Keep your distance from its pools.";

            case "Amomongo":
                return "The Amomongo… a savage giant ape. It tears its prey apart. It slams the ground with tremendous strength, and when it enters a rage it becomes even more dangerous. Do not let it grab you.";

            case "Tiyanak":
                return "A Tiyanak… a child’s spirit that lures with its cries. Do not follow the sound of a baby in the dark. When it gets close, it lunges to bite.";

            case "Wakwak":
                return "A Wakwak—it flies at night and its wings sound like a woman’s slippers. It hunts by sound. Stay quiet. When it rises, it will dive down on you.";

            case "Pugot":
                return "A Pugot—a headless creature that carries its head. It guards what it claims. It throws its head at you and stomps the ground. Do not take what is not yours.";

            case "Sirena":
                return "A Sirena—half-woman, half-fish. Her songs can pull you under. She bursts water around her when you get close. Do not listen.";

            case "Sigbin":
                return "The Sigbin—it walks backward and hides in the shadows. It drains the life from its victims. It steps back and slashes when you think it is retreating. Watch your back.";

            case "Manananggal":
                return "A Manananggal—it splits in half at night and hunts with its wings. Salt and light can stop it. When it rises into the air, it will dive down on you.";

            case "Kapre":
                return "A Kapre—a giant who smokes and sits in trees. He is strong but slow. He vanishes in smoke and strikes from behind, or leaps in and slams the ground. Do not let him grab you.";

            case "Busaw":
                return "The Busaw—it lives in graveyards and feeds on the dead. It will treat the living the same way. It reaches out and grasps everyone nearby.";

            case "Bungisngis":
                return "A Bungisngis—a giant with a wide grin. Its laugh is loud and hurts those in front of it. It pounds the ground and sends a shock through the earth.";

            case "ShadowDiwata":
            case "Shadow Touched Diwata":
                return "A Diwata touched by shadow. She was once a spirit of the forest; now she serves the darkness. She pulls you toward her with a dark veil and slashes with her lament. End her suffering.";

            default:
                return $"Careful, that is a {enemyId}. Learn how it moves before you strike—some spirits are deadlier than they look.";
        }
    }

    private void ConfigureVoiceSource(AudioSource src)
    {
        if (src == null) return;
        src.spatialBlend = 0f;          // 2D so it always plays at full volume
        src.playOnAwake = false;
        src.loop = false;
        src.mute = false;
        if (!src.gameObject.activeInHierarchy)
            src.gameObject.SetActive(true);
        src.enabled = true;
    }

    private void ShowDialogue(string message)
    {
        if (dialoguePanel == null || dialogueText == null)
        {
            Debug.LogWarning("[PlayerPowerStealDialogue] Dialogue UI not assigned on player; message was: " + message);
            return;
        }

        isShowing = true;

        if (autoCloseRoutine != null)
        {
            StopCoroutine(autoCloseRoutine);
            autoCloseRoutine = null;
        }
        if (typingRoutine != null)
        {
            StopCoroutine(typingRoutine);
            typingRoutine = null;
        }

        dialoguePanel.SetActive(true);

        string finalText = !string.IsNullOrEmpty(speakerPrefix) ? speakerPrefix + message : message;

        if (typingSpeed > 0f)
        {
            typingRoutine = StartCoroutine(TypeText(finalText));
        }
        else
        {
            dialogueText.text = finalText;
        }

        autoCloseRoutine = StartCoroutine(AutoCloseAfterAudioOrDelay());
    }

    private IEnumerator AutoCloseAfterAudioOrDelay()
    {
        // Prefer to wait for intro/ambient audio to finish, if any.
        AudioSource src = introVoiceSource;
        if (src != null && src.isPlaying)
        {
            while (isShowing && src.isPlaying)
            {
                yield return null;
            }

            if (isShowing)
                HideDialogue();
            yield break;
        }

        // No audio playing: fall back to autoCloseDelay (or a small default if unset)
        float delay = autoCloseDelay > 0f ? autoCloseDelay : 3f;
        float elapsedTime = 0f;
        while (elapsedTime < delay && isShowing)
        {
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        if (isShowing)
            HideDialogue();
    }

    public void HideDialogue()
    {
        isShowing = false;

        if (autoCloseRoutine != null)
        {
            StopCoroutine(autoCloseRoutine);
            autoCloseRoutine = null;
        }

        if (typingRoutine != null)
        {
            StopCoroutine(typingRoutine);
            typingRoutine = null;
        }

        if (dialoguePanel != null)
            dialoguePanel.SetActive(false);

        // If this dialogue was an enemy intro, start the intro cooldown now so
        // the cooldown counts from the end of the line / voiceline instead of
        // from when we first detected the enemy.
        if (introCooldownPending)
        {
            nextIntroAllowedTime = Time.time + Mathf.Max(0f, introCooldown);
            introCooldownPending = false;
        }
    }

    private IEnumerator TypeText(string fullText)
    {
        if (dialogueText == null)
            yield break;

        dialogueText.text = string.Empty;

        for (int i = 0; i < fullText.Length; i++)
        {
            dialogueText.text += fullText[i];
            yield return new WaitForSeconds(typingSpeed);
        }

        typingRoutine = null;
    }
}

