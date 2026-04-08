using UnityEngine;

public class NunoMerchantTrigger : MonoBehaviour
{
    [Header("Shop Configuration")]
    public ShopTradeData[] availableTrades;
    public GameObject interactPrompt;
    
    [Header("UI References")]
    public GameObject shopPanel;
    public Transform tradeListParent;
    public GameObject tradeSlotPrefab;
    
    [Header("Trigger")]
    [Tooltip("optional: assign a specific box collider to use as the interaction trigger. if left empty, uses colliders on this gameobject.")]
    public BoxCollider dialogueTriggerCollider;
    
    [Header("Quest Integration")]
    [Tooltip("identifier used by quest objectives for talk-to tasks")] 
    public string npcId;
    
    [Header("Quest Dialogue (shown instead of shop when TalkTo quest is active)")]
    [Tooltip("speaker name shown in the dialogue bar")]
    public string dialogueSpeakerName = "Nuno";
    [Tooltip("icon shown next to the speaker name")]
    public Sprite dialogueSpeakerIcon;
    [TextArea(2, 5)]
    [Tooltip("message shown when the player talks to the nuno during a TalkTo quest")]
    public string questDialogueMessage = "Halika, bata. May kailangan akong sabihin sa iyo...";
    [Tooltip("optional voiceline played during the quest dialogue")]
    public AudioClip questDialogueVoice;
    
    [Header("Per-Quest Voice Clips (for multi-line dialogues)")]
    [Tooltip("map quest names to per-line voice clips. each entry's voiceClips array should match the talkDialogueLines count in the quest JSON.")]
    public QuestDialogueVoiceEntry[] questVoiceEntries;
    
    [Header("Audio")]
    [Tooltip("audio source used to play voice clips during quest dialogue. if empty, falls back to NunoDialogueBarUI's voiceSource.")]
    public AudioSource dialogueAudioSource;
    
    [System.Serializable]
    public class QuestDialogueVoiceEntry
    {
        [Tooltip("quest name as defined in the JSON (e.g. 'Ask the Nuno')")] 
        public string questName;
        [Tooltip("one voice clip per dialogue line, in order")] 
        public AudioClip[] voiceClips;
    }
    
    private bool playerInRange = false;
    private GameObject player;
    private PlayerInteractHUD playerHUD;
    // true when a separate collider is handling triggers (disables local OnTriggerEnter/Exit)
    private bool usesExternalTrigger;

    void Start()
    {
        if (dialogueTriggerCollider != null)
        {
            dialogueTriggerCollider.isTrigger = true;
            // attach forwarder to the collider's gameobject so trigger events route back here
            var fwd = dialogueTriggerCollider.gameObject.GetComponent<NunoTriggerForwarder>();
            if (fwd == null)
                fwd = dialogueTriggerCollider.gameObject.AddComponent<NunoTriggerForwarder>();
            fwd.owner = this;
            usesExternalTrigger = (dialogueTriggerCollider.gameObject != gameObject);
        }
    }
    
    private bool IsLocalPlayer(GameObject go)
    {
        var pv = go.GetComponentInParent<Photon.Pun.PhotonView>();
        if (pv == null) return true;
        return pv.IsMine;
    }
    
    /// <summary>
    /// checks if there is an active TalkTo quest objective targeting this npc.
    /// </summary>
    private bool IsTalkObjectiveActive()
    {
        if (string.IsNullOrEmpty(npcId)) return false;
        var qm = FindFirstObjectByType<QuestManager>();
        if (qm == null) return false;
        var q = qm.GetCurrentQuest();
        if (q == null || q.isCompleted) return false;
        var obj = q.GetCurrentObjective();
        if (obj != null)
        {
            return obj.objectiveType == ObjectiveType.TalkTo
                && !obj.IsCompleted
                && string.Equals(obj.targetId, npcId, System.StringComparison.OrdinalIgnoreCase);
        }
        // legacy single-objective quest fallback
        return q.objectiveType == ObjectiveType.TalkTo
            && q.currentCount < q.requiredCount
            && string.Equals(q.targetId, npcId, System.StringComparison.OrdinalIgnoreCase);
    }

    // returns true when the local player already completed this talk objective but the quest
    // is still waiting for other players to finish theirs
    private bool IsLocallyCompletedWaitingForOthers()
    {
        if (string.IsNullOrEmpty(npcId)) return false;
        var qm = FindFirstObjectByType<QuestManager>();
        if (qm == null) return false;
        var q = qm.GetCurrentQuest();
        if (q == null || q.isCompleted) return false;
        var obj = q.GetCurrentObjective();
        if (obj == null) return false;
        if (obj.objectiveType != ObjectiveType.TalkTo) return false;
        if (!string.Equals(obj.targetId, npcId, System.StringComparison.OrdinalIgnoreCase)) return false;
        return obj.IsCompleted && qm.IsCurrentObjectiveLocallyCompleted();
    }
    
    public void OnTriggerEnter(Collider other)
    {
        if (usesExternalTrigger) return;
        HandleTriggerEnter(other);
    }
    
    public void HandleTriggerEnter(Collider other)
    {
        var playerRoot = GetPlayerRoot(other);
        if (playerRoot == null) return;
        playerInRange = true;
        player = playerRoot;
        
        if (IsLocalPlayer(playerRoot))
        {
            if (interactPrompt != null)
                interactPrompt.SetActive(true);
                
            playerHUD = playerRoot.GetComponentInChildren<PlayerInteractHUD>(true);
            if (playerHUD != null)
            {
                string prompt;
                if (IsLocallyCompletedWaitingForOthers())
                    prompt = "Waiting for other players...";
                else if (IsTalkObjectiveActive())
                    prompt = "Press \"E\" to talk";
                else
                    prompt = "Press \"E\" to trade";
                playerHUD.Show(prompt);
            }
        }
    }
    
    GameObject GetPlayerRoot(Collider other)
    {
        var ps = other.GetComponentInParent<PlayerStats>();
        return ps != null ? ps.gameObject : null;
    }
    
    public void OnTriggerExit(Collider other)
    {
        if (usesExternalTrigger) return;
        HandleTriggerExit(other);
    }
    
    public void HandleTriggerExit(Collider other)
    {
        var playerRoot = GetPlayerRoot(other);
        if (playerRoot == null) return;
        playerInRange = false;
        player = null;
        if (interactPrompt != null)
            interactPrompt.SetActive(false);
        if (playerHUD != null)
        {
            playerHUD.Hide();
            playerHUD = null;
        }
    }
    
    void Update()
    {
        // don't allow interaction if any UI or dialogue is open
        if (LocalUIManager.Instance != null && LocalUIManager.Instance.IsAnyOpen)
        {
            if (playerHUD != null)
            {
                playerHUD.Hide();
            }
            return;
        }
        
        if (playerInRange && Input.GetKeyDown(KeyCode.E))
        {
            var local = FindLocalPlayer();
            if (local == null) return;

            // block interaction if locally completed but waiting for other players
            if (IsLocallyCompletedWaitingForOthers())
            {
                Debug.Log($"nuno talk blocked: already completed, waiting for other players");
                return;
            }
            
            if (IsTalkObjectiveActive())
            {
                // quest talk mode: show dialogue instead of shop
                ShowQuestDialogue();
            }
            else
            {
                // normal mode: open trade shop
                OpenTradeShop();
            }
            
            if (playerHUD != null)
                playerHUD.Hide();
            if (interactPrompt != null)
                interactPrompt.SetActive(false);
        }
    }
    
    private void OpenTradeShop()
    {
        var manager = NunoShopManager.Instance;
        if (manager != null)
        {
            var trades = (availableTrades != null && availableTrades.Length > 0) ? availableTrades : manager.availableTrades;
            manager.OpenShop(trades);
        }
    }
    
    private void ShowQuestDialogue()
    {
        var dialogueUI = NunoDialogueBarUI.Instance;
        if (dialogueUI == null)
            dialogueUI = FindFirstObjectByType<NunoDialogueBarUI>();
        
        if (dialogueUI != null)
        {
            // pull dialogue lines from the current quest if available
            string[] lines = null;
            var qm = FindFirstObjectByType<QuestManager>();
            if (qm != null)
            {
                var q = qm.GetCurrentQuest();
                if (q != null && q.talkDialogueLines != null && q.talkDialogueLines.Length > 0)
                    lines = q.talkDialogueLines;
            }

            if (lines != null && lines.Length > 0)
            {
                // look up per-line voice clips for this quest
                AudioClip[] voiceClips = null;
                if (qm != null && questVoiceEntries != null)
                {
                    var q = qm.GetCurrentQuest();
                    if (q != null)
                    {
                        foreach (var entry in questVoiceEntries)
                        {
                            if (entry != null && string.Equals(entry.questName, q.questName, System.StringComparison.OrdinalIgnoreCase))
                            {
                                voiceClips = entry.voiceClips;
                                break;
                            }
                        }
                    }
                }

                // multi-line dialogue from quest data
                dialogueUI.ShowDialogueSequence(dialogueSpeakerName, lines, dialogueSpeakerIcon, () =>
                {
                    ApplyTalkProgress();
                }, voiceClips, dialogueAudioSource);
            }
            else
            {
                // fallback to single configured message
                dialogueUI.ShowDialogue(dialogueSpeakerName, questDialogueMessage, questDialogueVoice, dialogueSpeakerIcon, () =>
                {
                    ApplyTalkProgress();
                }, dialogueAudioSource);
            }
            Debug.Log($"nuno quest dialogue shown for npc: {npcId}");
        }
        else
        {
            // fallback: no dialogue UI found, just complete the quest progress
            Debug.LogWarning($"NunoDialogueBarUI not found — completing talk objective for {npcId} without dialogue");
            ApplyTalkProgress();
        }
    }
    
    private GameObject FindLocalPlayer()
    {
        var t = PlayerRegistry.GetLocalPlayerTransform();
        if (t != null) return t.gameObject;
        return GameObject.FindGameObjectWithTag("Player");
    }
    
    private void ApplyTalkProgress()
    {
        var qm = FindFirstObjectByType<QuestManager>();
        if (qm != null && !string.IsNullOrEmpty(npcId))
        {
            qm.AddProgress_TalkTo(npcId);
            Debug.Log($"quest talk progress updated for nuno: {npcId}");
        }
    }
}

/// <summary>
/// forwards trigger events from an assigned box collider back to NunoMerchantTrigger.
/// automatically added at runtime — do not add manually.
/// </summary>
public class NunoTriggerForwarder : MonoBehaviour
{
    [HideInInspector] public NunoMerchantTrigger owner;

    void OnTriggerEnter(Collider other)
    {
        if (owner != null) owner.HandleTriggerEnter(other);
    }

    void OnTriggerExit(Collider other)
    {
        if (owner != null) owner.HandleTriggerExit(other);
    }
}

