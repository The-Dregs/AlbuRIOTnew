using UnityEngine;
using UnityEngine.Playables;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using AlbuRIOT.Abilities;
using UnityEngine.SceneManagement;

public enum ObjectiveType { Kill, Collect, TalkTo, ReachArea, FindArea, ShrineOffering, PowerSteal, Custom }

[System.Serializable]
public class QuestObjective
{
    [Header("Objective Details")]
    public string objectiveName;
    public string description;
    public ObjectiveType objectiveType = ObjectiveType.Custom;
    
    [Header("Target Information")]
    [Tooltip("for Kill: enemy name; for Collect: item name; for TalkTo/ReachArea/FindArea: id string; for ShrineOffering: shrine id")] 
    public string targetId;
    [Tooltip("how many actions required to complete the objective")] 
    public int requiredCount = 1;
    [Tooltip("for ReachArea/FindArea: when true, all players must be in the area before the objective completes")]
    public bool requireAllPlayers = false;
    [Tooltip("runtime counter; do not edit at runtime")] 
    public int currentCount = 0;
    
    [Header("Rewards")]
    public ItemData rewardItem;
    public int rewardQuantity = 1;
    public AbilityBase rewardAbility;
    
    [Header("Shrine Specific")]
    [Tooltip("Items required for shrine offering")] 
    public ItemData[] requiredOfferings;
    [Tooltip("Quantity of each required offering")] 
    public int[] offeringQuantities;
    
    [Header("Collect Specific (Multi-Item)")]
    [Tooltip("For Collect objectives: multiple item IDs to collect")] 
    public string[] collectItemIds;
    [Tooltip("Required quantity for each collectItemId (must match length)")] 
    public int[] collectQuantities;
    [Tooltip("Runtime progress per item (do not edit at runtime)")] 
    public int[] collectProgress; // tracks currentCount per item
    
    public bool IsCompleted => currentCount >= requiredCount;
    
    public bool IsMultiItemCollect()
    {
        return objectiveType == ObjectiveType.Collect && collectItemIds != null && collectItemIds.Length > 1;
    }
    
    public bool IsMultiItemCollectComplete()
    {
        if (!IsMultiItemCollect()) return false;
        if (collectProgress == null || collectProgress.Length != collectQuantities.Length) return false;
        for (int i = 0; i < collectQuantities.Length; i++)
        {
            if (collectProgress[i] < collectQuantities[i]) return false;
        }
        return true;
    }
}

[System.Serializable]
public class Quest
{
    [Header("Quest Information")]
    public string questName;
    public string description;
    public bool isCompleted;
    public int questID;
    
    [Header("Objectives")]
    public QuestObjective[] objectives;
    public int currentObjectiveIndex = 0;
    
    [Header("Rewards")]
    public ItemData[] rewardItems;
    public int[] rewardQuantities;
    public AbilityBase[] rewardAbilities;
    
    [Header("Quest Flow")]
    public bool requiresAllObjectives = true; // if false, any objective completion completes quest
    public bool autoAdvanceObjectives = true; // if true, automatically advance to next objective
    
    [Header("TalkTo Dialogue")]
    [Tooltip("Dialogue lines shown when the player talks to the NPC for a TalkTo quest. If empty, uses the NPC's default message.")]
    public string[] talkDialogueLines;

    [Header("Cutscenes")]
    [Tooltip("Cutscene to play when quest starts (optional)")]
    public PlayableDirector cutsceneOnStart;
    [Tooltip("Cutscene to play when quest completes (optional)")]
    public PlayableDirector cutsceneOnComplete;
    [Tooltip("Name of the cutscene GameObject to play on start (optional, used by JSON)")]
    public string cutsceneOnStartName;
    [Tooltip("Name of the cutscene GameObject to play on complete (optional, used by JSON)")]
    public string cutsceneOnCompleteName;
    [Tooltip("GameObjects to enable when cutscene starts (works for both start and complete cutscenes)")]
    public GameObject[] enableOnCutscene;
    [Tooltip("GameObjects to disable when cutscene starts (works for both start and complete cutscenes)")]
    public GameObject[] disableOnCutscene;

    [Header("GameObject Activation on Quest Start")]
    [Tooltip("GameObjects to enable when this quest starts.")]
    public GameObject[] enableOnStart;
    [Tooltip("GameObjects to disable when this quest starts.")]
    public GameObject[] disableOnStart;

    [Header("GameObject Activation on Quest Complete")]
    [Tooltip("GameObjects to enable when this quest is completed.")]
    public GameObject[] enableOnComplete;
    [Tooltip("GameObjects to disable when this quest is completed.")]
    public GameObject[] disableOnComplete;
    
    // Legacy support
    public int objectiveID; // legacy id, not used by new system
    public ObjectiveType objectiveType = ObjectiveType.Custom;
    public string targetId;
    public int requiredCount = 1;
    public int currentCount = 0;
    
    public QuestObjective GetCurrentObjective()
    {
        if (objectives == null || objectives.Length == 0) return null;
        if (currentObjectiveIndex < 0 || currentObjectiveIndex >= objectives.Length) return null;
        return objectives[currentObjectiveIndex];
    }
    
    public bool IsAllObjectivesCompleted()
    {
        if (objectives == null || objectives.Length == 0) return true;
        
        if (requiresAllObjectives)
        {
            foreach (var obj in objectives)
            {
                bool complete = obj.IsMultiItemCollect() ? obj.IsMultiItemCollectComplete() : obj.IsCompleted;
                if (!complete) return false;
            }
            return true;
        }
        else
        {
            foreach (var obj in objectives)
            {
                bool complete = obj.IsMultiItemCollect() ? obj.IsMultiItemCollectComplete() : obj.IsCompleted;
                if (complete) return true;
            }
            return false;
        }
    }
}

[DefaultExecutionOrder(500)] // run after LocalInputLocker so LateUpdate cursor enforcement wins
public class QuestManager : MonoBehaviourPun, Photon.Pun.IPunObservable, Photon.Realtime.IInRoomCallbacks
{
    [System.Serializable]
    private class ObjectiveSyncPayload
    {
        public int currentCount;
        public int[] collectProgress;
    }

    [System.Serializable]
    private class QuestSyncPayload
    {
        public bool isCompleted;
        public int currentObjectiveIndex;
        public int currentCount;
        public ObjectiveSyncPayload[] objectives;
    }

    [System.Serializable]
    private class QuestStatePayload
    {
        public int currentQuestIndex;
        public QuestSyncPayload[] quests;
    }

    public static QuestManager Instance { get; private set; }
    
    [Header("Quest Configuration")]
    public Quest[] quests;
    public int currentQuestIndex = 0;
    public TextMeshProUGUI questText;
    [Header("Quest HUD UI")]
    public TextMeshProUGUI questTitleText; // assign for top HUD
    public TextMeshProUGUI questDescriptionText; // assign for top HUD
    [Header("Quest UI Integration")]
    public GameObject disableOnQuestUIOpen; // assign GameObject to disable when quest UI is open
    [Header("Quest Complete Pop UI")]
    public RectTransform questCompletePopTarget; // optional; defaults to questTitleText if not set
    
    [Header("Shrine Integration")]
    public ShrineManager shrineManager;
    
    [Header("Inventory Integration")]
    public Inventory playerInventory;
    
    [Header("Network Settings")]
    [Tooltip("If true, all quest progress is synchronized globally across all players")]
    public bool globalQuestSync = true;

    [Header("Post-Quest Map Transition")]
    [Tooltip("Scene name to load when all quests in this manager are completed (e.g. FIRSTMAP). Leave empty to stay in current scene.")]
    public string allQuestsCompletedNextScene = "FIRSTMAP";

    private bool allQuestsTransitionStarted = false;
    private readonly Dictionary<string, HashSet<int>> _collectObjectiveCompletionsByKey = new Dictionary<string, HashSet<int>>();
    private readonly HashSet<string> _collectCompletionNotifiedLocally = new HashSet<string>();
    // per-objective completion counts visible on all clients (key -> completedCount/totalPlayers)
    private readonly Dictionary<string, int> _collectCompletionCounts = new Dictionary<string, int>();
    private readonly Dictionary<string, int> _collectCompletionTotals = new Dictionary<string, int>();
    
    [Header("Cutscene Settings")]
    [Tooltip("UI overlay for cutscene fade (optional)")]
    public CanvasGroup cutsceneFadeOverlay;
    [Tooltip("Duration for cutscene fade in/out")]
    public float cutsceneFadeDuration = 1f;
    [Tooltip("Skip button for cutscenes (optional, only shown to MasterClient)")]
    public GameObject cutsceneSkipButton;
    [Tooltip("Fully hides all players (model, HUD, camera) while a quest cutscene is playing")]
    public bool hidePlayersDuringCutscene = true;
    
    private PlayableDirector currentCutscene = null;
    private bool cutsceneSkipped = false;
    private bool _questCutsceneActive = false;

    // snapshot lists for full-player hide/restore
    private readonly System.Collections.Generic.List<(Renderer r, bool was)>   _hiddenRenderers = new System.Collections.Generic.List<(Renderer, bool)>();
    private readonly System.Collections.Generic.List<(Canvas c, bool was)>     _hiddenCanvases  = new System.Collections.Generic.List<(Canvas, bool)>();
    private readonly System.Collections.Generic.List<(Camera cam, bool was)>   _hiddenCameras   = new System.Collections.Generic.List<(Camera, bool)>();

    private readonly System.Collections.Generic.List<(PlayerCombat pc, bool was)>          _lockedCombats     = new System.Collections.Generic.List<(PlayerCombat, bool)>();
    private readonly System.Collections.Generic.List<(ThirdPersonController tc, bool was)> _lockedControllers = new System.Collections.Generic.List<(ThirdPersonController, bool)>();
    private int _activeQuestInputLockToken = -1;
    private bool _questCutsceneUiOpened = false;

    [Header("Quest SFX")]
    [Tooltip("AudioSource used to play quest sound cues. If left empty, one will be created automatically.")]
    public AudioSource questAudioSource;
    [Tooltip("Played when a new quest starts.")]
    public AudioClip questStartedSFX;
    [Tooltip("Played when an objective receives progress.")]
    public AudioClip objectiveUpdatedSFX;
    [Tooltip("Played when an objective is completed.")]
    public AudioClip objectiveCompletedSFX;
    [Tooltip("Played when a full quest is completed.")]
    public AudioClip questCompletedSFX;
    [Range(0f, 1f)] public float questSFXVolume = 1f;

    [Header("Quest Complete Pop Settings")]
    [Tooltip("How large the quest UI briefly scales when a quest completes.")]
    public float questCompletePopScale = 1.1f;
    [Tooltip("Total duration of the pop animation, in seconds.")]
    public float questCompletePopDuration = 0.25f;
    [Tooltip("Curve used for the pop in/out interpolation (0-1 over half the duration).")]
    public AnimationCurve questCompletePopCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Vector3 questCompletePopOriginalScale = Vector3.one;
    private Coroutine questCompletePopCoroutine;

    // quest events for ui or other systems
    public event Action<Quest> OnQuestStarted;
    public event Action<Quest> OnQuestUpdated;
    public event Action<Quest> OnQuestCompleted;
    public event Action<QuestObjective> OnObjectiveCompleted;
    public event Action<QuestObjective> OnObjectiveUpdated;

    void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            // Clear stale scene-object refs whenever a scene unloads so they don't prevent GC
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            PhotonNetwork.AddCallbackTarget(this);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        // Ensure PhotonView exists (photonView is read-only, just check it exists)
        if (gameObject.GetComponent<PhotonView>() == null)
        {
            gameObject.AddComponent<PhotonView>();
        }
        
        // ensure index in bounds and ui reflects initial state
        if (quests != null && quests.Length > 0)
        {
            currentQuestIndex = Mathf.Clamp(currentQuestIndex, 0, quests.Length - 1);
            UpdateQuestUI();
        }

        // default pop target to quest title if not assigned
        if (questCompletePopTarget == null && questTitleText != null)
            questCompletePopTarget = questTitleText.rectTransform;
        if (questCompletePopTarget != null)
            questCompletePopOriginalScale = questCompletePopTarget.localScale;
        
        // Auto-find components if not assigned
        if (shrineManager == null)
            shrineManager = FindFirstObjectByType<ShrineManager>();

        // quest sfx setup
        if (questAudioSource == null)
        {
            questAudioSource = gameObject.AddComponent<AudioSource>();
            questAudioSource.playOnAwake = false;
            questAudioSource.spatialBlend = 0f;
        }
        OnQuestStarted += PlayQuestStartedSFX;
        OnObjectiveUpdated += PlayObjectiveUpdatedSFX;
        OnObjectiveCompleted += PlayObjectiveCompletedSFX;
        OnQuestCompleted += PlayQuestCompletedSFX;
    }
    
    void OnDestroy()
    {
        // In edit mode, avoid runtime cleanup (coroutines/events/singletons) which can destabilize the editor
        if (Application.isPlaying)
        {
            // Stop all coroutines
            StopAllCoroutines();

            // Unsubscribe from scene events
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        // Release all component snapshot lists so scene objects can be GC'd
        ClearComponentLists();
        
        // Unsubscribe from inventory events (match EnsurePlayerInventory subscription)
        if (lastInventoryRef != null)
        {
            lastInventoryRef.OnItemAdded -= OnInventoryItemAdded;
            lastInventoryRef.OnInventoryChanged -= OnInventoryChanged;
        }
        if (playerInventory != null && playerInventory != lastInventoryRef)
        {
            playerInventory.OnItemAdded -= OnInventoryItemAdded;
            playerInventory.OnInventoryChanged -= OnInventoryChanged;
        }
        lastInventoryRef = null;
        playerInventory = null;
        
        if (Instance == this)
        {
            Instance = null;
        }
        
        // Clear cached data
        cachedAreaTriggers = null;
        cachedCutscenes = null;
        cachedItems = null;
        
        // Unsubscribe sfx handlers before clearing
        OnQuestStarted -= PlayQuestStartedSFX;
        OnObjectiveUpdated -= PlayObjectiveUpdatedSFX;
        OnObjectiveCompleted -= PlayObjectiveCompletedSFX;
        OnQuestCompleted -= PlayQuestCompletedSFX;

        // Clear event subscriptions
        OnQuestStarted = null;
        OnQuestCompleted = null;
        OnObjectiveCompleted = null;
        OnObjectiveUpdated = null;
    }
    
    void LateUpdate()
    {
        // Enforce cursor last — runs after LocalInputLocker.LateUpdate() due to DefaultExecutionOrder(500)
        if (_questCutsceneActive)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // secret debug shortcut: F5 completes the current quest for fast testing
        if (UnityEngine.Input.GetKeyDown(KeyCode.F5))
        {
            // only allow host/offline to drive authoritative quest completion
            if (!PhotonNetwork.IsConnected || PhotonNetwork.IsMasterClient)
            {
                Quest currentQuest = GetCurrentQuest();
                if (currentQuest != null && !currentQuest.isCompleted)
                {
                    Debug.Log("[QuestManager] Debug F5 pressed - force completing current quest.");
                    CompleteQuest(currentQuestIndex);
                }
            }
        }
#endif
    }

    void Start()
    {
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !globalQuestSync)
        {
            // keep quest progression shared in multiplayer to avoid host/client divergence
            globalQuestSync = true;
        }

        // Request quest state sync if joining existing room (with small delay to ensure room is ready)
        if (PhotonNetwork.IsConnected && !PhotonNetwork.IsMasterClient)
        {
            StartCoroutine(DelayedQuestSync());
        }
        else if (PhotonNetwork.IsConnected && PhotonNetwork.IsMasterClient)
        {
            BroadcastQuestStateBuffered();
        }
        
        // Subscribe to inventory changes for automatic quest checking
        EnsurePlayerInventory();
        
        // Check inventory for existing items when quest manager starts
        StartCoroutine(DelayedInventoryCheck());
    }
    
    private System.Collections.IEnumerator DelayedQuestSync()
    {
        yield return new WaitForSeconds(0.5f);
        RequestQuestStateSync();
    }
    
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        // Not used, but required by IPunObservable
    }
    
    private Inventory lastInventoryRef = null;

    private static string BuildCollectObjectiveKey(int questIndex, int objectiveIndex)
    {
        return $"Q{questIndex}_O{objectiveIndex}";
    }

    private bool IsMultiplayerCollectObjective(ObjectiveType type)
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return false;
        return type == ObjectiveType.Collect || type == ObjectiveType.TalkTo;
    }

    public bool IsCurrentObjectiveLocallyCompleted()
    {
        Quest currentQuest = GetCurrentQuest();
        if (currentQuest == null || currentQuest.isCompleted) return false;
        QuestObjective obj = currentQuest.GetCurrentObjective();
        if (obj == null) return false;
        return obj.IsMultiItemCollect() ? obj.IsMultiItemCollectComplete() : obj.IsCompleted;
    }

    private void ResetCollectCompletionTrackingForCurrentQuest()
    {
        _collectObjectiveCompletionsByKey.Clear();
        _collectCompletionNotifiedLocally.Clear();
        _collectCompletionCounts.Clear();
        _collectCompletionTotals.Clear();
    }

    private void TryNotifyCollectObjectiveCompletionToMaster()
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null)
            return;

        Quest currentQuest = GetCurrentQuest();
        if (currentQuest == null)
            return;

        bool hasObjectives = currentQuest.objectives != null && currentQuest.objectives.Length > 0;

        int objectiveIndex;
        bool localCollectComplete;

        if (hasObjectives)
        {
            objectiveIndex = Mathf.Clamp(currentQuest.currentObjectiveIndex, 0, currentQuest.objectives.Length - 1);
            QuestObjective objective = currentQuest.objectives[objectiveIndex];
            if (objective == null || (objective.objectiveType != ObjectiveType.Collect && objective.objectiveType != ObjectiveType.TalkTo))
                return;

            localCollectComplete = objective.IsMultiItemCollect() ? objective.IsMultiItemCollectComplete() : objective.IsCompleted;
        }
        else
        {
            // legacy single-objective quest: use quest-level fields (objectiveType/targetId/requiredCount)
            if (currentQuest.objectiveType != ObjectiveType.Collect && currentQuest.objectiveType != ObjectiveType.TalkTo)
                return;

            objectiveIndex = 0; // synthetic index for legacy quests
            localCollectComplete = currentQuest.currentCount >= currentQuest.requiredCount;
        }

        if (!localCollectComplete)
            return;

        string key = BuildCollectObjectiveKey(currentQuestIndex, objectiveIndex);
        if (_collectCompletionNotifiedLocally.Contains(key))
            return;

        _collectCompletionNotifiedLocally.Add(key);
        int actorNumber = PhotonNetwork.LocalPlayer.ActorNumber;

        if (PhotonNetwork.IsMasterClient)
        {
            MarkCollectObjectiveCompleteForPlayer(currentQuestIndex, objectiveIndex, actorNumber);
        }
        else if (photonView != null)
        {
            photonView.RPC("RPC_RequestCollectObjectiveCompletion", RpcTarget.MasterClient, currentQuestIndex, objectiveIndex, actorNumber);
        }
    }

    private void MarkCollectObjectiveCompleteForPlayer(int questIndex, int objectiveIndex, int actorNumber)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return;
        if (quests == null || questIndex < 0 || questIndex >= quests.Length) return;
        Quest quest = quests[questIndex];
        if (quest == null) return;

        bool hasObjectives = quest.objectives != null && quest.objectives.Length > 0;

        // validate objective index / type for both new (objectives-array) and legacy (quest-level) quests
        if (hasObjectives)
        {
            if (objectiveIndex < 0 || objectiveIndex >= quest.objectives.Length) return;
            QuestObjective objective = quest.objectives[objectiveIndex];
            if (objective == null || (objective.objectiveType != ObjectiveType.Collect && objective.objectiveType != ObjectiveType.TalkTo)) return;
        }
        else
        {
            // legacy single-objective quest: treat quest-level Collect/TalkTo as objective index 0
            if (objectiveIndex != 0) return;
            if (quest.objectiveType != ObjectiveType.Collect && quest.objectiveType != ObjectiveType.TalkTo) return;
        }

        string key = BuildCollectObjectiveKey(questIndex, objectiveIndex);
        if (!_collectObjectiveCompletionsByKey.TryGetValue(key, out HashSet<int> completedActors))
        {
            completedActors = new HashSet<int>();
            _collectObjectiveCompletionsByKey[key] = completedActors;
        }

        completedActors.Add(actorNumber);

        // prune actors who left the room
        if (PhotonNetwork.CurrentRoom != null)
        {
            completedActors.RemoveWhere(a => !PhotonNetwork.CurrentRoom.Players.ContainsKey(a));
        }

        int requiredPlayers = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.PlayerCount : 1;
        int completedCount = completedActors.Count;

        Debug.Log($"[QuestManager] objective {key} completion: {completedCount}/{requiredPlayers} players done (actor {actorNumber} just finished)");

        // broadcast count to all clients so UI can show progress
        if (photonView != null)
            photonView.RPC("RPC_SyncCollectCompletionCount", RpcTarget.AllBuffered, questIndex, objectiveIndex, completedCount, requiredPlayers);

        if (completedCount >= requiredPlayers)
        {
            if (photonView != null)
                photonView.RPC("RPC_CompleteCollectObjectiveForAll", RpcTarget.AllBuffered, questIndex, objectiveIndex);
            else
                ApplyCollectObjectiveCompletionGlobal(questIndex, objectiveIndex);
        }
    }

    [PunRPC]
    private void RPC_RequestCollectObjectiveCompletion(int questIndex, int objectiveIndex, int actorNumber)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        MarkCollectObjectiveCompleteForPlayer(questIndex, objectiveIndex, actorNumber);
    }

    [PunRPC]
    private void RPC_SyncCollectCompletionCount(int questIndex, int objectiveIndex, int completedCount, int totalPlayers)
    {
        string key = BuildCollectObjectiveKey(questIndex, objectiveIndex);
        _collectCompletionCounts[key] = completedCount;
        _collectCompletionTotals[key] = totalPlayers;
        UpdateQuestUI();
    }

    [PunRPC]
    private void RPC_CompleteCollectObjectiveForAll(int questIndex, int objectiveIndex)
    {
        ApplyCollectObjectiveCompletionGlobal(questIndex, objectiveIndex);
    }

    private void ApplyCollectObjectiveCompletionGlobal(int questIndex, int objectiveIndex)
    {
        if (quests == null || questIndex < 0 || questIndex >= quests.Length) return;
        Quest quest = quests[questIndex];
        if (quest == null) return;

        bool hasObjectives = quest.objectives != null && quest.objectives.Length > 0;
        QuestObjective objective = null;

        if (hasObjectives)
        {
            if (objectiveIndex < 0 || objectiveIndex >= quest.objectives.Length) return;
            objective = quest.objectives[objectiveIndex];
            if (objective == null || (objective.objectiveType != ObjectiveType.Collect && objective.objectiveType != ObjectiveType.TalkTo)) return;
        }
        else
        {
            // legacy single-objective quest: complete the quest-level Collect/TalkTo requirement for everyone
            if (objectiveIndex != 0) return;
            if (quest.objectiveType != ObjectiveType.Collect && quest.objectiveType != ObjectiveType.TalkTo) return;
        }

        currentQuestIndex = questIndex;

        if (hasObjectives && objective.objectiveType == ObjectiveType.Collect && objective.IsMultiItemCollect() && objective.collectQuantities != null)
        {
            if (objective.collectProgress == null || objective.collectProgress.Length != objective.collectQuantities.Length)
                objective.collectProgress = new int[objective.collectQuantities.Length];

            objective.currentCount = 0;
            for (int i = 0; i < objective.collectQuantities.Length; i++)
            {
                int max = Mathf.Max(0, objective.collectQuantities[i]);
                objective.collectProgress[i] = max;
                objective.currentCount += max;
            }
        }
        else
        {
            if (hasObjectives)
            {
                objective.currentCount = Mathf.Max(objective.currentCount, objective.requiredCount);
            }
            else
            {
                // legacy quest: bump quest-level counter to requiredCount so local checks see it as complete
                quest.currentCount = Mathf.Max(quest.currentCount, quest.requiredCount);
            }
        }

        string key = BuildCollectObjectiveKey(questIndex, objectiveIndex);
        _collectCompletionNotifiedLocally.Remove(key);
        _collectObjectiveCompletionsByKey.Remove(key);

        if (hasObjectives && objective != null)
        {
            OnObjectiveCompleted?.Invoke(objective);
        }

        EnsurePlayerInventory();
        if (hasObjectives && objective != null)
        {
            if (objective.rewardItem != null && playerInventory != null)
                playerInventory.AddItem(objective.rewardItem, objective.rewardQuantity);
        }

        if (hasObjectives && quest.autoAdvanceObjectives)
        {
            int nextIndex = objectiveIndex;
            for (int j = objectiveIndex + 1; j < quest.objectives.Length; j++)
            {
                bool done = quest.objectives[j].IsMultiItemCollect() ? quest.objectives[j].IsMultiItemCollectComplete() : quest.objectives[j].IsCompleted;
                if (!done)
                {
                    nextIndex = j;
                    break;
                }
            }
            quest.currentObjectiveIndex = Mathf.Clamp(nextIndex, 0, quest.objectives.Length - 1);
        }

        OnQuestUpdated?.Invoke(quest);

        if (hasObjectives)
        {
            if (quest.IsAllObjectivesCompleted())
                CompleteQuest(currentQuestIndex);
            else
                UpdateQuestUI();
        }
        else
        {
            // legacy quest: quest-level Collect/TalkTo is the whole quest, so finishing it completes the quest
            CompleteQuest(currentQuestIndex);
        }
    }
    
    private void EnsurePlayerInventory()
    {
        if (playerInventory == null)
        {
            playerInventory = Inventory.FindLocalInventory();
        }
        
        // Resubscribe if inventory reference changed
        if (playerInventory != null && playerInventory != lastInventoryRef)
        {
            // Unsubscribe from old reference
            if (lastInventoryRef != null)
            {
                lastInventoryRef.OnItemAdded -= OnInventoryItemAdded;
                lastInventoryRef.OnInventoryChanged -= OnInventoryChanged;
            }
            
            // Subscribe to new reference
            playerInventory.OnItemAdded += OnInventoryItemAdded;
            playerInventory.OnInventoryChanged += OnInventoryChanged;
            lastInventoryRef = playerInventory;
        }
    }

    // force re-resolve the local player inventory after a spawn/scene transition
    public void RefreshPlayerInventory()
    {
        playerInventory = null;
        EnsurePlayerInventory();
        Debug.Log($"[QuestManager] RefreshPlayerInventory -> {(playerInventory != null ? playerInventory.gameObject.name : "null")}");
    }

    public void StartQuest(int index)
    {
        if (index < 0 || index >= quests.Length) return;
        
        ApplyStartQuest(index);
        
        // Sync quest start globally
        if (photonView != null && PhotonNetwork.IsMasterClient)
        {
            photonView.RPC("RPC_StartQuest", RpcTarget.AllBuffered, index);
            BroadcastQuestStateBuffered();
        }
    }

    private void ApplyStartQuest(int index)
    {
        if (index < 0 || index >= quests.Length) return;

        currentQuestIndex = index;
        Quest quest = quests[index];
        quest.isCompleted = false;
        ResetCollectCompletionTrackingForCurrentQuest();
        
        // Reset all objectives
        if (quest.objectives != null)
        {
            foreach (var objective in quest.objectives)
            {
                objective.currentCount = 0;
                // Reset multi-item progress
                if (objective.collectProgress != null)
                {
                    for (int i = 0; i < objective.collectProgress.Length; i++)
                    {
                        objective.collectProgress[i] = 0;
                    }
                }
            }
            // Set initial objective to index 1 for playtest/demo
            quest.currentObjectiveIndex = Mathf.Clamp(1, 0, quest.objectives.Length - 1);
        }
        
        // Legacy support
        quest.currentCount = 0;
        
        Debug.Log($"Quest started: {quest.questName}");
        UpdateQuestUI();
        OnQuestStarted?.Invoke(quest);

        // Enable/disable GameObjects on quest start
        ApplyQuestGameObjects(quest.enableOnStart, quest.disableOnStart);
        
        // Check inventory for items that might already be present
        CheckInventoryForCollectObjectives();
        
        // Play cutscene on start if configured (reference or name-based)
        if (quest.cutsceneOnStart != null)
        {
            PlayQuestCutscene(quest.cutsceneOnStart);
        }
        else if (!string.IsNullOrEmpty(quest.cutsceneOnStartName))
        {
            var startCutscene = FindCutsceneByName(quest.cutsceneOnStartName);
            if (startCutscene != null)
            {
                PlayQuestCutscene(startCutscene);
            }
            else
            {
                Debug.LogWarning($"[QuestManager] Start cutscene '{quest.cutsceneOnStartName}' not found for quest '{quest.questName}'.");
            }
        }
    }
    
    [PunRPC]
    public void RPC_StartQuest(int index)
    {
        // Master client already applied locally, skip
        if (PhotonNetwork.IsMasterClient && photonView != null && photonView.IsMine)
        {
            return;
        }
        ApplyStartQuest(index);
    }

    public void CompleteQuest(int index)
    {
        if (index < 0 || index >= quests.Length) return;
        ApplyCompleteQuest(index);

        // Sync with other players (buffered)
        if (PhotonNetwork.IsMasterClient)
        {
            photonView.RPC("RPC_CompleteQuest", RpcTarget.AllBuffered, index);
            BroadcastQuestStateBuffered();
        }
    }
    
    private void ApplyCompleteQuest(int index)
    {
        if (index < 0 || index >= quests.Length) return;
        Quest quest = quests[index];
        if (quest.isCompleted) return;

        quest.isCompleted = true;
        Debug.Log($"Quest completed: {quest.questName}");

        // chapter-specific hooks
        if (!string.IsNullOrEmpty(quest.questName))
        {
            // after "Return to the Nuno" completes, swap the Nuno mound visuals so the
            // cleaned/fixed mound is shown and the broken mound is hidden.
            if (string.Equals(quest.questName, "Return to the Nuno", StringComparison.OrdinalIgnoreCase))
            {
                TryApplyReturnToNunoMoundState();
            }
        }

        UpdateQuestUI();
        OnQuestCompleted?.Invoke(quest);

        PlayQuestCompletePop();

        // Enable/disable GameObjects on quest complete
        ApplyQuestGameObjects(quest.enableOnComplete, quest.disableOnComplete);

        // Play cutscene on complete if configured (plays for all players)
        bool isMaster = PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
        bool hasCutsceneReference = quest.cutsceneOnComplete != null;
        bool hasCutsceneName = !string.IsNullOrEmpty(quest.cutsceneOnCompleteName);
        bool shouldPlayCutscene = hasCutsceneReference || hasCutsceneName;

        if (shouldPlayCutscene && isMaster)
        {
            PlayableDirector completeCutscene = quest.cutsceneOnComplete;

            // Resolve by name if no direct reference was assigned
            if (completeCutscene == null && hasCutsceneName)
            {
                completeCutscene = FindCutsceneByName(quest.cutsceneOnCompleteName);
                if (completeCutscene == null)
                {
                    Debug.LogWarning($"[QuestManager] Complete cutscene '{quest.cutsceneOnCompleteName}' not found for quest '{quest.questName}'. Falling back to immediate reward.");
                    shouldPlayCutscene = false;
                }
            }

            if (shouldPlayCutscene && completeCutscene != null)
            {
                // Only MasterClient initiates cutscene (sends RPC to all)
                // Other clients will receive the RPC and play automatically
                PlayQuestCutscene(completeCutscene);
                StartCoroutine(CompleteQuestAfterCutscene(quest, index, completeCutscene));
            }
        }

        if (!shouldPlayCutscene)
        {
            // give rewards to each player's own local inventory
            GiveQuestRewards(quest);
            
            // only master starts next quest (will be synced to all via RPC)
            if (isMaster)
            {
                // Auto-start next quest
                if (index + 1 < quests.Length)
                    StartQuest(index + 1);
            }
        }
    }
    
    private IEnumerator CompleteQuestAfterCutscene(Quest quest, int index, PlayableDirector expectedCutscene)
    {
        bool cutsceneObserved = false;

        // wait for the cutscene lifecycle to actually start on this client.
        // this covers pre-play fade/setup time where director.state may still be Stopped.
        float waitForCutsceneStart = 0f;
        const float cutsceneStartTimeout = 5f;
        while (!cutsceneSkipped && expectedCutscene != null && waitForCutsceneStart < cutsceneStartTimeout)
        {
            if (currentCutscene == expectedCutscene || _questCutsceneActive)
            {
                cutsceneObserved = true;
                break;
            }

            waitForCutsceneStart += Time.deltaTime;
            yield return null;
        }

        // Wait until the quest cutscene lifecycle fully ends (fade out + unlock + currentCutscene reset).
        if (cutsceneObserved && !cutsceneSkipped)
        {
            while ((_questCutsceneActive || currentCutscene != null) && !cutsceneSkipped)
            {
                yield return null;
            }
        }

        // give rewards to each player's own local inventory
        GiveQuestRewards(quest);

        // only master starts next quest / transitions (synced to all via RPC)
        bool isMasterAfterCutscene = PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
        if (!isMasterAfterCutscene) yield break;

        // Auto-start next quest, or if this was the last quest, transition to the next map.
        if (index + 1 < quests.Length)
        {
            StartQuest(index + 1);
        }
        else
        {
            // Lock movement/camera so the player cannot move while the loading UI is shown.
            LocalInputLocker.Ensure()?.Acquire("PostQuestSceneTransition", lockMovement: true, lockCombat: true, lockCamera: true, cursorUnlock: false);

            // Show loading immediately on the same frame we hand off from cutscene -> transition.
            var mapTransitionType = System.Type.GetType("MapTransitionManager");
            if (mapTransitionType != null)
            {
                var showMethod = mapTransitionType.GetMethod("ShowPreTransitionLoading",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (showMethod != null)
                {
                    showMethod.Invoke(null, new object[] { "Loading next area..." });
                }
            }

            StartAllQuestsCompletedTransition();
        }
    }

    // --- quest sfx helpers ---
    private void PlayQuestSFX(AudioClip clip)
    {
        if (questAudioSource != null && clip != null)
            questAudioSource.PlayOneShot(clip, questSFXVolume);
    }
    private void PlayQuestStartedSFX(Quest _) => PlayQuestSFX(questStartedSFX);
    private void PlayObjectiveUpdatedSFX(QuestObjective _) => PlayQuestSFX(objectiveUpdatedSFX);
    private void PlayObjectiveCompletedSFX(QuestObjective _) => PlayQuestSFX(objectiveCompletedSFX);
    private void PlayQuestCompletedSFX(Quest _) => PlayQuestSFX(questCompletedSFX);
    
    private void PlayQuestCompletePop()
    {
        if (questCompletePopTarget == null)
            return;

        if (questCompletePopCoroutine != null)
            StopCoroutine(questCompletePopCoroutine);

        questCompletePopCoroutine = StartCoroutine(QuestCompletePopRoutine());
    }

    private IEnumerator QuestCompletePopRoutine()
    {
        if (questCompletePopTarget == null)
            yield break;

        float duration = Mathf.Max(0.01f, questCompletePopDuration);
        float half = duration * 0.5f;
        Vector3 baseScale = questCompletePopOriginalScale;

        float t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(t / half);
            float eval = questCompletePopCurve != null ? questCompletePopCurve.Evaluate(normalized) : normalized;
            float s = Mathf.Lerp(1f, questCompletePopScale, eval);
            questCompletePopTarget.localScale = baseScale * s;
            yield return null;
        }

        t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(t / half);
            float eval = questCompletePopCurve != null ? questCompletePopCurve.Evaluate(normalized) : normalized;
            float s = Mathf.Lerp(questCompletePopScale, 1f, eval);
            questCompletePopTarget.localScale = baseScale * s;
            yield return null;
        }

        questCompletePopTarget.localScale = baseScale;
        questCompletePopCoroutine = null;
    }

    [PunRPC]
    public void RPC_CompleteQuest(int index)
    {
        // Master client already applied locally, skip
        if (PhotonNetwork.IsMasterClient && photonView != null && photonView.IsMine)
        {
            return;
        }
        ApplyCompleteQuest(index);
    }

    // Called by the MasterClient when the final quest in this QuestManager has been completed (after its cutscene and rewards).
    private void StartAllQuestsCompletedTransition()
    {
        if (allQuestsTransitionStarted) return;
        if (string.IsNullOrEmpty(allQuestsCompletedNextScene)) return;

        allQuestsTransitionStarted = true;

        if (PhotonNetwork.IsConnected && photonView != null)
        {
            photonView.RPC("RPC_AllQuestsCompleted_LoadNextMap", RpcTarget.All, allQuestsCompletedNextScene);
        }
        else
        {
            InvokeMapTransition(allQuestsCompletedNextScene);
        }
    }

    /// <summary>
    /// Notifies all clients to transition to the given scene via MapTransitionManager.
    /// Can be called by any system (e.g. ProceduralMapLoader) that needs a networked scene transition.
    /// </summary>
    public void RequestNetworkedMapTransition(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;

        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && PhotonNetwork.InRoom && photonView != null)
        {
            photonView.RPC(nameof(RPC_RequestMapTransition), RpcTarget.All, sceneName);
        }
        else
        {
            InvokeMapTransition(sceneName);
        }
    }

    [PunRPC]
    private void RPC_RequestMapTransition(string sceneName)
    {
        InvokeMapTransition(sceneName);
    }

    [PunRPC]
    private void RPC_AllQuestsCompleted_LoadNextMap(string sceneName)
    {
        InvokeMapTransition(sceneName);
    }

    /// <summary>
    /// Invokes the unified NetworkManager transition entrypoint.
    /// Falls back to synchronized Photon load or local SceneManager load when needed.
    /// </summary>
    private void InvokeMapTransition(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
            return;

        if (NetworkManager.BeginSceneTransition(sceneName))
            return;

        Debug.LogWarning($"[QuestManager] NetworkManager.BeginSceneTransition unavailable for '{sceneName}'. Using synchronized scene load fallback.");

        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.InRoom)
        {
            Debug.LogError($"[QuestManager] Refusing to local-load '{sceneName}' while connected but not in room. This would split clients into different lobbies.");
            return;
        }

        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
            PhotonNetwork.LoadLevel(sceneName);
        else
            SceneManager.LoadScene(sceneName);
    }
    
    private void GiveQuestRewards(Quest quest)
    {
        EnsurePlayerInventory();
        
        // Give item rewards
        if (quest.rewardItems != null && quest.rewardQuantities != null && playerInventory != null)
        {
            for (int i = 0; i < quest.rewardItems.Length && i < quest.rewardQuantities.Length; i++)
            {
                if (quest.rewardItems[i] != null)
                {
                    playerInventory.AddItem(quest.rewardItems[i], quest.rewardQuantities[i]);
                    Debug.Log($"Quest reward: {quest.rewardQuantities[i]}x {quest.rewardItems[i].itemName}");
                }
            }
        }
        
        // Give ability rewards
        // ability rewards now handled by PowerStealManager/PlayerSkillSlots system
    }

    public Quest GetCurrentQuest()
    {
        if (quests == null || quests.Length == 0) return null;
        if (currentQuestIndex < 0 || currentQuestIndex >= quests.Length) return null;
        return quests[currentQuestIndex];
    }
    
    public Quest GetQuestByID(int questID)
    {
        if (quests == null || quests.Length == 0) return null;
        foreach (var quest in quests)
        {
            if (quest != null && quest.questID == questID)
                return quest;
        }
        return null;
    }
    
    public QuestObjective GetCurrentObjective()
    {
        Quest currentQuest = GetCurrentQuest();
        if (currentQuest == null) return null;
        return currentQuest.GetCurrentObjective();
    }
    
    public void UpdateObjectiveProgress(ObjectiveType type, string targetId, int amount = 1)
    {
        if (IsMultiplayerCollectObjective(type))
        {
            // collect progress is tracked per-player locally; objective advances only after all players finish
            UpdateObjectiveProgressInternal(type, targetId, amount, false);
            TryNotifyCollectObjectiveCompletionToMaster();
            return;
        }

        // If we're not master client, don't apply locally - wait for master's authoritative broadcast
        // This prevents double-counting when we receive our own request back
        if (PhotonNetwork.IsConnected && !PhotonNetwork.IsMasterClient)
        {
            // Send request to master, but don't apply locally yet
            // We'll apply when master broadcasts back
            if (photonView != null && globalQuestSync)
            {
                Quest currentQuest = GetCurrentQuest();
                if (currentQuest != null && !currentQuest.isCompleted && currentQuest.objectives != null)
                {
                    for (int idx = 0; idx < currentQuest.objectives.Length; idx++)
                    {
                        var objective = currentQuest.objectives[idx];
                        if (objective.objectiveType != type) continue;
                        
                        bool matched = false;
                        int itemIndex = -1;
                        
                        if (type == ObjectiveType.Collect && objective.IsMultiItemCollect())
                        {
                            if (objective.collectItemIds != null)
                            {
                                for (int i = 0; i < objective.collectItemIds.Length; i++)
                                {
                                    if (string.Equals(objective.collectItemIds[i], targetId, StringComparison.OrdinalIgnoreCase))
                                    {
                                        itemIndex = i;
                                        matched = true;
                                        break;
                                    }
                                }
                            }
                        }
                        else if (string.IsNullOrEmpty(objective.targetId) || string.Equals(objective.targetId, targetId, StringComparison.OrdinalIgnoreCase))
                        {
                            matched = true;
                        }
                        
                        if (matched)
                        {
                            photonView.RPC("RPC_RequestProgressUpdate", RpcTarget.MasterClient, currentQuestIndex, idx, (int)type, targetId, amount, itemIndex);
                            return; // Don't apply locally, wait for master broadcast
                        }
                    }
                }
            }
        }
        
        // Master client or offline mode: apply immediately
        UpdateObjectiveProgressInternal(type, targetId, amount, true);
    }
    
    private void UpdateObjectiveProgressInternal(ObjectiveType type, string targetId, int amount, bool shouldSync)
    {
        Quest currentQuest = GetCurrentQuest();
        if (currentQuest == null || currentQuest.isCompleted) return;
        bool isMultiplayerCollectType = IsMultiplayerCollectObjective(type);
        bool isGroupArea = (type == ObjectiveType.ReachArea || type == ObjectiveType.FindArea) && QuestAreaTrigger_GroupRequiresAll();
        // Only master/client relays group area; other objectives are local-only.
        if (currentQuest.objectives != null && currentQuest.objectives.Length > 0)
        {
            bool objectiveUpdated = false;
            
            for (int idx = 0; idx < currentQuest.objectives.Length; idx++)
            {
                var objective = currentQuest.objectives[idx];
                if (objective.objectiveType != type) continue;
                
                bool matched = false;
                // Handle multi-item Collect
                if (type == ObjectiveType.Collect && objective.IsMultiItemCollect())
                {
                    if (objective.collectItemIds != null && objective.collectProgress != null)
                    {
                        for (int i = 0; i < objective.collectItemIds.Length; i++)
                        {
                            if (string.Equals(objective.collectItemIds[i], targetId, StringComparison.OrdinalIgnoreCase))
                            {
                                int max = objective.collectQuantities != null && i < objective.collectQuantities.Length 
                                    ? objective.collectQuantities[i] : int.MaxValue;
                                objective.collectProgress[i] = Mathf.Clamp(objective.collectProgress[i] + amount, 0, max);
                                matched = true;
                                objectiveUpdated = true;
                                // Update total currentCount (sum of progress)
                                objective.currentCount = 0;
                                foreach (int p in objective.collectProgress) objective.currentCount += p;
                                Debug.Log($"Objective progress ({type}): {targetId} -> {objective.collectProgress[i]}/{max} (total: {objective.currentCount}/{objective.requiredCount})");
                                OnObjectiveUpdated?.Invoke(objective);
                                break;
                            }
                        }
                    }
                }
                // Single-item Collect or other types (legacy)
                else if (string.IsNullOrEmpty(objective.targetId) || string.Equals(objective.targetId, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    objective.currentCount = Mathf.Clamp(objective.currentCount + amount, 0, objective.requiredCount);
                    matched = true;
                    objectiveUpdated = true;
                    Debug.Log($"Objective progress ({type}): {targetId} -> {objective.currentCount}/{objective.requiredCount}");
                    OnObjectiveUpdated?.Invoke(objective);
                }
                
                // Check completion (use multi-item check if applicable)
                bool isComplete = objective.IsMultiItemCollect() ? objective.IsMultiItemCollectComplete() : objective.IsCompleted;
                if (matched && isComplete)
                {
                    if (IsMultiplayerCollectObjective(type))
                    {
                        TryNotifyCollectObjectiveCompletionToMaster();
                        continue;
                    }

                    Debug.Log($"Objective completed: {objective.objectiveName}");
                    OnObjectiveCompleted?.Invoke(objective);
                    
                    // Give objective rewards
                    EnsurePlayerInventory();
                    if (objective.rewardItem != null && playerInventory != null)
                    {
                        playerInventory.AddItem(objective.rewardItem, objective.rewardQuantity);
                    }
                    // ability rewards now handled by PowerStealManager/PlayerSkillSlots system

                    // Auto-advance to the next incomplete objective if configured
                    if (currentQuest.autoAdvanceObjectives)
                    {
                        // advance to next index (or stay if none left)
                        int nextIndex = idx;
                        for (int j = idx + 1; j < currentQuest.objectives.Length; j++)
                        {
                            if (!currentQuest.objectives[j].IsCompleted) { nextIndex = j; break; }
                        }
                        // if all after are completed, keep current index at the last completed one so UI shows completion until quest completes
                        currentQuest.currentObjectiveIndex = Mathf.Clamp(nextIndex, 0, currentQuest.objectives.Length - 1);
                    }
                }
            }
            
            if (objectiveUpdated)
            {
                OnQuestUpdated?.Invoke(currentQuest);

                // For multiplayer Collect/TalkTo, progress is local until all players complete.
                // Do not complete quest or broadcast state from this local progress update.
                if (isMultiplayerCollectType)
                {
                    UpdateQuestUI();
                    return;
                }

                // Check if quest should be completed
                if (currentQuest.IsAllObjectivesCompleted())
                {
                    CompleteQuest(currentQuestIndex);
                }
                else
                {
                    UpdateQuestUI();
                }

                // Sync globally if enabled, or if group ReachArea
                // Find which objective was updated to sync correctly
                if (shouldSync && (globalQuestSync || isGroupArea))
                {
                    if (photonView != null && PhotonNetwork.IsConnected)
                    {
                        // Find the matching objective to get its index and details
                        for (int syncIdx = 0; syncIdx < currentQuest.objectives.Length; syncIdx++)
                        {
                            var syncObjective = currentQuest.objectives[syncIdx];
                            if (syncObjective.objectiveType != type) continue;
                            
                            bool shouldSyncThis = false;
                            int syncItemIndex = -1;
                            
                            // Check if this objective matches
                            if (type == ObjectiveType.Collect && syncObjective.IsMultiItemCollect())
                            {
                                if (syncObjective.collectItemIds != null)
                                {
                                    for (int i = 0; i < syncObjective.collectItemIds.Length; i++)
                                    {
                                        if (string.Equals(syncObjective.collectItemIds[i], targetId, StringComparison.OrdinalIgnoreCase))
                                        {
                                            syncItemIndex = i;
                                            shouldSyncThis = true;
                                            break;
                                        }
                                    }
                                }
                            }
                            else if (string.IsNullOrEmpty(syncObjective.targetId) || string.Equals(syncObjective.targetId, targetId, StringComparison.OrdinalIgnoreCase))
                            {
                                shouldSyncThis = true;
                            }
                            
                            if (shouldSyncThis)
                            {
                                // MasterClient broadcasts directly, others send to master who then broadcasts
                                if (PhotonNetwork.IsMasterClient)
                                {
                                    if (syncItemIndex >= 0)
                                    {
                                        photonView.RPC("RPC_UpdateMultiItemProgress", RpcTarget.AllBuffered, currentQuestIndex, syncIdx, syncItemIndex, amount);
                                    }
                                    else
                                    {
                                        photonView.RPC("RPC_UpdateObjectiveProgress", RpcTarget.AllBuffered, currentQuestIndex, syncIdx, (int)type, targetId, amount);
                                    }
                                }
                                else
                                {
                                    // Send to master client who will broadcast
                                    photonView.RPC("RPC_RequestProgressUpdate", RpcTarget.MasterClient, currentQuestIndex, syncIdx, (int)type, targetId, amount, syncItemIndex);
                                }
                                break; // Only sync the first matching objective
                            }
                        }
                    }
                }

                if (PhotonNetwork.IsConnected && PhotonNetwork.IsMasterClient)
                {
                    BroadcastQuestStateBuffered();
                }
            }
        }
        else
        {
            // Legacy single objective system
            if (currentQuest.objectiveType == type && 
                (string.IsNullOrEmpty(currentQuest.targetId) || string.Equals(currentQuest.targetId, targetId, StringComparison.OrdinalIgnoreCase)))
            {
                currentQuest.currentCount = Mathf.Clamp(currentQuest.currentCount + amount, 0, currentQuest.requiredCount);
                Debug.Log($"Quest progress ({type}): {targetId} -> {currentQuest.currentCount}/{currentQuest.requiredCount}");
                OnQuestUpdated?.Invoke(currentQuest);
                
                if (currentQuest.currentCount >= currentQuest.requiredCount)
                {
                    CompleteQuest(currentQuestIndex);
                }
                else
                {
                    UpdateQuestUI();
                }
                
                // Sync with other players (buffered)
                if (shouldSync && globalQuestSync && photonView != null && PhotonNetwork.IsConnected)
                {
                    // MasterClient broadcasts directly, others send to master who then broadcasts
                    if (PhotonNetwork.IsMasterClient)
                    {
                        photonView.RPC("RPC_UpdateObjectiveProgress", RpcTarget.AllBuffered, currentQuestIndex, 0, (int)type, targetId, amount);
                    }
                    else
                    {
                        // Send to master client who will broadcast
                        photonView.RPC("RPC_RequestProgressUpdate", RpcTarget.MasterClient, currentQuestIndex, 0, (int)type, targetId, amount, -1);
                    }
                }

                if (PhotonNetwork.IsConnected && PhotonNetwork.IsMasterClient)
                {
                    BroadcastQuestStateBuffered();
                }
            }
        }
    }
    
    [PunRPC]
    public void RPC_UpdateObjectiveProgress(int questIndex, int objectiveIndex, int type, string targetId, int amount)
    {
        // Master client already applied locally, skip
        if (PhotonNetwork.IsMasterClient && photonView != null && photonView.IsMine)
        {
            return;
        }
        
        // Sync quest index if needed
        if (questIndex != currentQuestIndex && questIndex >= 0 && questIndex < quests.Length)
        {
            currentQuestIndex = questIndex;
        }
        
        // Apply progress update without syncing (we're already syncing via RPC)
        UpdateObjectiveProgressInternal((ObjectiveType)type, targetId, amount, false);
    }
    
    [PunRPC]
    public void RPC_UpdateMultiItemProgress(int questIndex, int objectiveIndex, int itemIndex, int amount)
    {
        // Master client already applied locally, skip
        if (PhotonNetwork.IsMasterClient && photonView != null && photonView.IsMine)
        {
            return;
        }
        
        // Sync quest index if needed
        if (questIndex != currentQuestIndex && questIndex >= 0 && questIndex < quests.Length)
        {
            currentQuestIndex = questIndex;
        }
        
        Quest currentQuest = GetCurrentQuest();
        if (currentQuest == null || currentQuest.isCompleted) return;
        if (currentQuest.objectives == null || objectiveIndex < 0 || objectiveIndex >= currentQuest.objectives.Length) return;
        
        var objective = currentQuest.objectives[objectiveIndex];
        if (objective.IsMultiItemCollect() && objective.collectProgress != null && 
            itemIndex >= 0 && itemIndex < objective.collectProgress.Length &&
            objective.collectQuantities != null && itemIndex < objective.collectQuantities.Length)
        {
            int max = objective.collectQuantities[itemIndex];
            objective.collectProgress[itemIndex] = Mathf.Clamp(objective.collectProgress[itemIndex] + amount, 0, max);
            objective.currentCount = 0;
            foreach (int p in objective.collectProgress) objective.currentCount += p;
            
            bool isComplete = objective.IsMultiItemCollectComplete();
            if (isComplete)
            {
                OnObjectiveCompleted?.Invoke(objective);
            }
            else
            {
                OnObjectiveUpdated?.Invoke(objective);
            }
            
            if (currentQuest.IsAllObjectivesCompleted())
            {
                CompleteQuest(currentQuestIndex);
            }
            else
            {
                UpdateQuestUI();
                OnQuestUpdated?.Invoke(currentQuest);
            }
        }
    }
    
    [PunRPC]
    public void RPC_SyncFullQuestState(int questIndex, int objectiveIndex)
    {
        // Master client already has correct state, skip
        if (PhotonNetwork.IsMasterClient && photonView != null && photonView.IsMine)
        {
            return;
        }
        
        if (questIndex >= 0 && questIndex < quests.Length)
        {
            currentQuestIndex = questIndex;
            Quest quest = quests[questIndex];
            if (quest != null && objectiveIndex >= 0 && objectiveIndex < quest.objectives.Length)
            {
                quest.currentObjectiveIndex = objectiveIndex;
            }
            UpdateQuestUI();
        }
    }

    [PunRPC]
    public void RPC_SyncQuestStatePayload(string payloadJson)
    {
        if (PhotonNetwork.IsMasterClient && photonView != null && photonView.IsMine)
            return;
        ApplyQuestStatePayload(payloadJson);
    }
    
    public void RequestQuestStateSync()
    {
        if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.IsMasterClient)
        {
            photonView.RPC("RPC_RequestQuestStateSync", RpcTarget.MasterClient);
        }
    }
    
    [PunRPC]
    private void RPC_RequestQuestStateSync(PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        string payloadJson = BuildQuestStatePayloadJson();
        photonView.RPC("RPC_SyncQuestStatePayload", info.Sender, payloadJson);
    }

    private void TryApplyReturnToNunoMoundState()
    {
        // look for the spawn area prefab that contains the Nuno mound visuals and swap
        // the broken mound for the fixed mound once the ritual quest is complete.
        var spawnAreaPrefab = Resources.Load<GameObject>("SpawnAreas/SpawnArea_1");
        if (spawnAreaPrefab == null)
            return;

        // try to resolve in the active scene first
        GameObject instance = GameObject.Find("SpawnArea_1");
        if (instance == null)
        {
            // fallback: look for any root that matches the prefab name
            var allRoots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < allRoots.Length && instance == null; i++)
            {
                if (allRoots[i] != null && allRoots[i].name.Contains("SpawnArea_1"))
                    instance = allRoots[i];
            }
        }

        if (instance == null)
            return;

        Transform fixedMound = instance.transform.Find("Nuno's Mound/Model_Mound/FixedMound");
        Transform brokenMound = instance.transform.Find("Nuno's Mound/Model_Mound/BrokenMound");

        if (fixedMound != null)
            fixedMound.gameObject.SetActive(true);
        if (brokenMound != null)
            brokenMound.gameObject.SetActive(false);
    }
    
    [PunRPC]
    private void RPC_RequestProgressUpdate(int questIndex, int objectiveIndex, int type, string targetId, int amount, int itemIndex)
    {
        // Only MasterClient processes this
        if (!PhotonNetwork.IsMasterClient) return;
        
        // Validate and apply the progress update, then broadcast to all
        Quest currentQuest = GetCurrentQuest();
        if (currentQuest == null || currentQuestIndex != questIndex) return;
        
        // Apply locally first (MasterClient is authoritative)
        UpdateObjectiveProgressInternal((ObjectiveType)type, targetId, amount, false);
        
        // Then broadcast to all clients (including the requester, but they'll skip due to RPC guard)
        if (itemIndex >= 0)
        {
            // Multi-item collect
            photonView.RPC("RPC_UpdateMultiItemProgress", RpcTarget.AllBuffered, questIndex, objectiveIndex, itemIndex, amount);
        }
        else
        {
            // Standard objective
            photonView.RPC("RPC_UpdateObjectiveProgress", RpcTarget.AllBuffered, questIndex, objectiveIndex, type, targetId, amount);
        }
    }

    public void UpdateQuestUI()
    {
        Quest currentQuest = GetCurrentQuest();
        if (questText != null)
        {
            if (currentQuest != null)
            {
                string questDisplay = FormatQuestDisplay(currentQuest);
                questText.text = questDisplay;
            }
            else
            {
                questText.text = "No active quest";
            }
        }
        // update HUD title/description
        if (questTitleText != null)
            questTitleText.text = currentQuest != null ? currentQuest.questName : "No active quest";
        if (questDescriptionText != null)
            questDescriptionText.text = currentQuest != null ? currentQuest.description : "";
    }

    // call this when opening the quest UI
    public void OnQuestUIOpened()
    {
        if (disableOnQuestUIOpen != null)
            disableOnQuestUIOpen.SetActive(false);
    }

    // call this when closing the quest UI
    public void OnQuestUIClosed()
    {
        if (disableOnQuestUIOpen != null)
            disableOnQuestUIOpen.SetActive(true);
    }
    
    private string FormatQuestDisplay(Quest quest)
    {
        if (quest.isCompleted)
        {
            return $"{quest.questName} (Completed)\n{quest.description}";
        }

        string display = $"{quest.questName}\n{quest.description}\n\n";

        // Handle new multi-objective system
        if (quest.objectives != null && quest.objectives.Length > 0)
        {
            display += "Objectives:\n";
            for (int i = 0; i < quest.objectives.Length; i++)
            {
                var objective = quest.objectives[i];
                // Only show completed or current objective
                if (objective.IsCompleted || i == quest.currentObjectiveIndex)
                {
                    // check if locally complete but waiting for all players (TalkTo / Collect)
                    bool locallyDoneWaiting = objective.IsCompleted
                        && i == quest.currentObjectiveIndex
                        && IsMultiplayerCollectObjective(objective.objectiveType);

                    string status = objective.IsCompleted && !locallyDoneWaiting ? "✓" : "○";
                    string progress = objective.requiredCount > 1 ? $" [{objective.currentCount}/{objective.requiredCount}]" : "";
                    string inProgress = (!objective.IsCompleted && i == quest.currentObjectiveIndex) ? " in progress" : "";
                    string waitingText = "";
                    if (locallyDoneWaiting)
                    {
                        // show per-player completion count if available
                        int questIdx = Array.IndexOf(quests, quest);
                        string countKey = BuildCollectObjectiveKey(questIdx >= 0 ? questIdx : currentQuestIndex, i);
                        if (_collectCompletionCounts.TryGetValue(countKey, out int doneCount) &&
                            _collectCompletionTotals.TryGetValue(countKey, out int totalCount))
                        {
                            waitingText = $"     Complete - waiting for other players ({doneCount}/{totalCount})";
                        }
                        else
                        {
                            int total = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.PlayerCount : 1;
                            waitingText = $"     Complete - waiting for other players (1/{total})";
                        }
                    }
                    display += $"{status} {objective.objectiveName}{progress}{inProgress}{waitingText}\n";
                }
                // Hide future objectives
            }
        }
        else
        {
            // Legacy single objective system
            string progress = quest.requiredCount > 1 ? $" [{quest.currentCount}/{quest.requiredCount}]" : "";
            string inProgress = !quest.isCompleted ? " in progress" : "";
            display += $"Progress:{progress}{inProgress}";
        }

        return display;
    }

    // ---- progress apis (legacy support) ----
    public void AddProgress_Kill(string enemyName)
    {
        UpdateObjectiveProgress(ObjectiveType.Kill, enemyName, 1);
    }

    public void AddProgress_Collect(string itemName, int amount = 1)
    {
        UpdateObjectiveProgress(ObjectiveType.Collect, itemName, amount);
    }

    public void AddProgress_TalkTo(string npcId)
    {
        UpdateObjectiveProgress(ObjectiveType.TalkTo, npcId, 1);
    }

    public void AddProgress_ReachArea(string areaId)
    {
        UpdateObjectiveProgress(ObjectiveType.ReachArea, areaId, 1);
    }
    
    public void AddProgress_FindArea(string areaId)
    {
        UpdateObjectiveProgress(ObjectiveType.FindArea, areaId, 1);
    }
    
    // ---- new objective types ----
    public void AddProgress_ShrineOffering(string shrineId, ItemData offeredItem, int quantity)
    {
        Quest currentQuest = GetCurrentQuest();
        if (currentQuest == null || currentQuest.isCompleted) return;
        
        // Check if this shrine offering matches any objective
        if (currentQuest.objectives != null)
        {
            foreach (var objective in currentQuest.objectives)
            {
                if (objective.objectiveType == ObjectiveType.ShrineOffering && 
                    string.Equals(objective.targetId, shrineId, StringComparison.OrdinalIgnoreCase))
                {
                    // Check if the offered item matches requirements
                    if (objective.requiredOfferings != null && objective.offeringQuantities != null)
                    {
                        for (int i = 0; i < objective.requiredOfferings.Length && i < objective.offeringQuantities.Length; i++)
                        {
                            if (objective.requiredOfferings[i] == offeredItem && 
                                objective.offeringQuantities[i] <= quantity)
                            {
                                UpdateObjectiveProgress(ObjectiveType.ShrineOffering, shrineId, 1);
                                return;
                            }
                        }
                    }
                }
            }
        }
    }
    
    public void AddProgress_PowerSteal(string enemyName)
    {
        UpdateObjectiveProgress(ObjectiveType.PowerSteal, enemyName, 1);
    }

    private QuestAreaTrigger[] cachedAreaTriggers;
    private float lastTriggerCacheTime = -1f;
    private const float TRIGGER_CACHE_INTERVAL = 2f;
    
    // Helper for group ReachArea trigger: uses objective.requireAllPlayers if set, else trigger's
    private bool QuestAreaTrigger_GroupRequiresAll()
    {
        var obj = GetCurrentObjective();
        if (obj == null) return false;
        if (obj.objectiveType != ObjectiveType.ReachArea && obj.objectiveType != ObjectiveType.FindArea) return false;
        
        if (cachedAreaTriggers == null || Time.time - lastTriggerCacheTime > TRIGGER_CACHE_INTERVAL)
        {
            cachedAreaTriggers = FindObjectsByType<QuestAreaTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            lastTriggerCacheTime = Time.time;
        }
        
        if (cachedAreaTriggers == null) return false;
        
        foreach (var t in cachedAreaTriggers)
        {
            if (t != null && string.Equals(t.areaId, obj.targetId, System.StringComparison.OrdinalIgnoreCase) && t.GetRequireAllPlayersForCurrentQuest())
                return true;
        }
        return false;
    }
    
    private void OnInventoryItemAdded(ItemData item, int quantity)
    {
        if (item == null) return;
        
        // quest progress for item pickups is handled directly by ItemPickup.UpdateQuestProgress.
        // only run a delayed backup reconciliation to catch items entering inventory via other means
        // (trade, crafting, rewards). the delay prevents double-counting with ItemPickup.
        string identifier = !string.IsNullOrEmpty(item.questId) ? item.questId : item.itemName;
        Debug.Log($"[QuestManager] Item added: {item.itemName} (questId: {item.questId}, identifier: {identifier}) x{quantity}");
        ScheduleInventoryReconcile();
    }
    
    private void OnInventoryChanged()
    {
        // delayed reconcile to avoid double-counting with direct AddProgress_Collect calls
        ScheduleInventoryReconcile();
    }

    private Coroutine _pendingInventoryReconcile;

    private void ScheduleInventoryReconcile()
    {
        // coalesce multiple calls in the same frame into one next-frame check
        if (_pendingInventoryReconcile == null && this != null && isActiveAndEnabled)
            _pendingInventoryReconcile = StartCoroutine(InventoryReconcileNextFrame());
    }

    private IEnumerator InventoryReconcileNextFrame()
    {
        yield return null; // wait one frame so ItemPickup.UpdateQuestProgress finishes first
        _pendingInventoryReconcile = null;
        CheckInventoryForCollectObjectives();
    }
    
    private System.Collections.IEnumerator DelayedInventoryCheck()
    {
        yield return new WaitForSeconds(0.5f);
        CheckInventoryForCollectObjectives();
    }
    
    private void CheckInventoryForCollectObjectives()
    {
        EnsurePlayerInventory();
        if (playerInventory == null) return;
        
        Quest currentQuest = GetCurrentQuest();
        if (currentQuest == null || currentQuest.isCompleted) return;
        if (currentQuest.objectives == null) return;
        
        foreach (var objective in currentQuest.objectives)
        {
            if (objective.objectiveType != ObjectiveType.Collect) continue;
            if (objective.IsCompleted) continue;
            
            // Handle multi-item collect objectives
            if (objective.IsMultiItemCollect())
            {
                if (objective.collectItemIds != null && objective.collectQuantities != null && objective.collectProgress != null)
                {
                    for (int i = 0; i < objective.collectItemIds.Length; i++)
                    {
                        if (i >= objective.collectQuantities.Length || i >= objective.collectProgress.Length) continue;
                        
                        string itemId = objective.collectItemIds[i];
                        int requiredQty = objective.collectQuantities[i];
                        int currentProgress = objective.collectProgress[i];
                        
                        if (currentProgress >= requiredQty) continue;
                        
                        // Find item by ID (check all items in ItemManager)
                        ItemData targetItem = FindItemById(itemId);
                        if (targetItem != null)
                        {
                            int inventoryCount = playerInventory.GetItemCount(targetItem);
                            int needed = requiredQty - currentProgress;
                            int toAdd = Mathf.Min(inventoryCount - currentProgress, needed);
                            
                            if (toAdd > 0)
                            {
                                UpdateObjectiveProgress(ObjectiveType.Collect, itemId, toAdd);
                            }
                        }
                    }
                }
            }
            // Single-item collect objective
            else if (!string.IsNullOrEmpty(objective.targetId))
            {
                int requiredQty = objective.requiredCount;
                int currentProgress = objective.currentCount;
                
                if (currentProgress >= requiredQty) continue;
                
                // Find item by ID
                ItemData targetItem = FindItemById(objective.targetId);
                if (targetItem != null)
                {
                    int inventoryCount = playerInventory.GetItemCount(targetItem);
                    int needed = requiredQty - currentProgress;
                    int toAdd = Mathf.Min(inventoryCount - currentProgress, needed);
                    
                    if (toAdd > 0)
                    {
                        UpdateObjectiveProgress(ObjectiveType.Collect, objective.targetId, toAdd);
                    }
                }
            }
        }
    }
    
    private Dictionary<string, ItemData> cachedItems;
    
    private ItemData FindItemById(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        
        if (cachedItems == null)
        {
            cachedItems = new Dictionary<string, ItemData>(StringComparer.OrdinalIgnoreCase);
            
            var itemMgr = ItemManager.Instance;
            if (itemMgr != null && itemMgr.itemDatabase != null)
            {
                foreach (var item in itemMgr.itemDatabase)
                {
                    if (item != null && !string.IsNullOrEmpty(item.itemName))
                    {
                        cachedItems[item.itemName] = item;
                        if (!string.IsNullOrEmpty(item.questId))
                            cachedItems[item.questId] = item;
                    }
                }
            }
            
            var db = ItemDatabase.Load();
            if (db != null && db.items != null)
            {
                foreach (var item in db.items)
                {
                    if (item != null && !string.IsNullOrEmpty(item.itemName) && !cachedItems.ContainsKey(item.itemName))
                    {
                        cachedItems[item.itemName] = item;
                        if (!string.IsNullOrEmpty(item.questId) && !cachedItems.ContainsKey(item.questId))
                            cachedItems[item.questId] = item;
                    }
                }
            }
        }
        
        if (cachedItems.TryGetValue(itemId, out var cached))
            return cached;
        
        return null;
    }
    
    public void PlayQuestCutscene(PlayableDirector cutscene)
    {
        if (cutscene == null) return;
        
        // Find the quest that owns this cutscene
        Quest quest = FindQuestByCutscene(cutscene);
        
        // Only MasterClient sends RPC to sync cutscene for all players
        bool isMaster = PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
        if (photonView != null && PhotonNetwork.IsConnected && isMaster)
        {
            // play locally first so completion flow can reliably wait on currentCutscene.
            StartCoroutine(PlayCutsceneCoroutine(cutscene, quest));

            // Use the cutscene's name or a unique identifier to sync to other clients only.
            string cutsceneName = cutscene.gameObject.name;
            photonView.RPC("RPC_PlayQuestCutscene", RpcTarget.OthersBuffered, cutsceneName);
        }
        else
        {
            // Non-master client or offline mode - play directly
            StartCoroutine(PlayCutsceneCoroutine(cutscene, quest));
        }
    }
    
    private Quest FindQuestByCutscene(PlayableDirector cutscene)
    {
        if (quests == null || cutscene == null) return null;
        foreach (var quest in quests)
        {
            if (quest == null) continue;
            if (quest.cutsceneOnStart == cutscene || quest.cutsceneOnComplete == cutscene)
                return quest;
        }
        return null;
    }
    
    [PunRPC]
    private void RPC_PlayQuestCutscene(string cutsceneName)
    {
        // Find the cutscene by name in the scene
        PlayableDirector cutscene = FindCutsceneByName(cutsceneName);
        Quest quest = FindQuestByCutsceneName(cutsceneName);
        if (cutscene != null)
        {
            StartCoroutine(PlayCutsceneCoroutine(cutscene, quest));
        }
        else
        {
            Debug.LogWarning($"[QuestManager] Could not find cutscene: {cutsceneName}");
        }
    }
    
    private Quest FindQuestByCutsceneName(string cutsceneName)
    {
        if (quests == null) return null;
        foreach (var quest in quests)
        {
            if (quest == null) continue;
            if (quest.cutsceneOnStart != null && quest.cutsceneOnStart.gameObject.name == cutsceneName)
                return quest;
            if (quest.cutsceneOnComplete != null && quest.cutsceneOnComplete.gameObject.name == cutsceneName)
                return quest;
        }
        return null;
    }
    
    private Dictionary<string, PlayableDirector> cachedCutscenes;
    private float lastCutsceneCacheTime = -1f;
    private const float CUTSCENE_CACHE_INTERVAL = 5f;
    
    private PlayableDirector FindCutsceneByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        
        if (cachedCutscenes == null || Time.time - lastCutsceneCacheTime > CUTSCENE_CACHE_INTERVAL)
        {
            cachedCutscenes = new Dictionary<string, PlayableDirector>();
            
            if (quests != null)
            {
                foreach (var quest in quests)
                {
                    if (quest != null)
                    {
                        if (quest.cutsceneOnStart != null && !string.IsNullOrEmpty(quest.cutsceneOnStart.gameObject.name))
                            cachedCutscenes[quest.cutsceneOnStart.gameObject.name] = quest.cutsceneOnStart;
                        if (quest.cutsceneOnComplete != null && !string.IsNullOrEmpty(quest.cutsceneOnComplete.gameObject.name))
                            cachedCutscenes[quest.cutsceneOnComplete.gameObject.name] = quest.cutsceneOnComplete;
                    }
                }
            }
            
            var allDirectors = FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var director in allDirectors)
            {
                if (director != null && !string.IsNullOrEmpty(director.gameObject.name) && !cachedCutscenes.ContainsKey(director.gameObject.name))
                    cachedCutscenes[director.gameObject.name] = director;
            }
            
            lastCutsceneCacheTime = Time.time;
        }
        
        if (cachedCutscenes != null && cachedCutscenes.TryGetValue(name, out var cached))
            return cached;
        
        return null;
    }
    
    private IEnumerator PlayCutsceneCoroutine(PlayableDirector cutscene, Quest quest)
    {
        if (cutscene == null) yield break;
        
        currentCutscene = cutscene;
        cutsceneSkipped = false;

        // ── Instant blackout on the very first frame ─────────────────────────────
        // Slam the overlay to opaque BEFORE any yield so the first rendered frame
        // never shows the transition into the cutscene camera.
        if (cutsceneFadeOverlay != null)
        {
            cutsceneFadeOverlay.gameObject.SetActive(true);
            cutsceneFadeOverlay.alpha = 1f; // fully black — holds until FadeCutsceneIn below
        }

        // One frame pause so Unity flushes the hide/blackout to the renderer
        yield return null;
        
        // Enable/disable GameObjects before cutscene starts
        if (quest != null)
        {
            EnableCutsceneObjects(quest, true);
        }
        
        // IMPORTANT: only hide player visuals / cameras AFTER the cutscene camera
        // is active, so we never have a frame with no enabled camera.
        if (hidePlayersDuringCutscene)
            HidePlayers();
        
        // Block pause menu and all other UIs while the cutscene runs
        _questCutsceneUiOpened = LocalUIManager.Ensure().TryOpen("QuestCutscene");

        // Lock all local player input (movement, combat, camera) so camera doesn't respond to mouse during cutscene
        LockLocalPlayers();
        _activeQuestInputLockToken = LocalInputLocker.Ensure().Acquire("QuestCutscene", lockMovement: true, lockCombat: true, lockCamera: true, cursorUnlock: true);

        try
        {
        // Force cursor visible — also enforced every LateUpdate() while _questCutsceneActive
        _questCutsceneActive = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Setup skip button if available
        if (cutsceneSkipButton != null)
        {
            cutsceneSkipButton.SetActive(false);
            UnityEngine.UI.Button btn = cutsceneSkipButton.GetComponent<UnityEngine.UI.Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(OnCutsceneSkip);
            }
        }
        
        // Fade in: black screen fades out to reveal the cutscene
        if (cutsceneFadeOverlay != null)
        {
            yield return StartCoroutine(FadeCutsceneIn());
        }
        
        // Show skip button after a delay (only to MasterClient)
        if (cutsceneSkipButton != null)
        {
            StartCoroutine(ShowCutsceneSkipButtonWithDelay());
        }

        // Play the cutscene
        cutscene.Play();
        
        // Wait for cutscene to finish (or be skipped)
        while (cutscene.state == PlayState.Playing && !cutsceneSkipped)
        {
            yield return null;
        }
        
        // Stop cutscene if it's still playing
        if (cutscene.state == PlayState.Playing)
        {
            cutscene.Stop();
        }
        
        // Hide skip button
        if (cutsceneSkipButton != null)
        {
            cutsceneSkipButton.SetActive(false);
        }
        
        // Fade out if overlay is available
        if (cutsceneFadeOverlay != null)
        {
            yield return StartCoroutine(FadeCutsceneOut());
        }
        
        // Restore GameObject states after cutscene
        if (quest != null)
        {
            EnableCutsceneObjects(quest, false);
        }

        // Restore player visibility
        if (hidePlayersDuringCutscene)
            ShowPlayers();

        RestoreQuestCutsceneRuntimeState(releaseInputLock: true);
        }
        finally
        {
            RestoreQuestCutsceneRuntimeState(releaseInputLock: true);
        }
    }
    
    private void ApplyQuestGameObjects(GameObject[] toEnable, GameObject[] toDisable)
    {
        if (toEnable != null)
            foreach (var go in toEnable)
                if (go != null) go.SetActive(true);

        if (toDisable != null)
            foreach (var go in toDisable)
                if (go != null) go.SetActive(false);
    }

    private void EnableCutsceneObjects(Quest quest, bool enable)
    {
        if (quest == null) return;
        
        // Enable objects
        if (quest.enableOnCutscene != null)
        {
            foreach (var obj in quest.enableOnCutscene)
            {
                if (obj != null)
                {
                    obj.SetActive(enable);
                }
            }
        }
        
        // Disable objects (inverse of enable)
        if (quest.disableOnCutscene != null)
        {
            foreach (var obj in quest.disableOnCutscene)
            {
                if (obj != null)
                {
                    obj.SetActive(!enable);
                }
            }
        }
    }
    
    private IEnumerator FadeCutsceneIn()
    {
        if (cutsceneFadeOverlay == null) yield break;

        cutsceneFadeOverlay.gameObject.SetActive(true);
        cutsceneFadeOverlay.alpha = 1f; // ensure we always start fully opaque
        float t = 0f;
        while (t < cutsceneFadeDuration)
        {
            t += Time.deltaTime;
            cutsceneFadeOverlay.alpha = Mathf.Lerp(1f, 0f, t / cutsceneFadeDuration);
            yield return null;
        }
        cutsceneFadeOverlay.alpha = 0f;
    }
    
    private IEnumerator FadeCutsceneOut()
    {
        if (cutsceneFadeOverlay == null) yield break;
        
        float t = 0f;
        while (t < cutsceneFadeDuration)
        {
            t += Time.deltaTime;
            cutsceneFadeOverlay.alpha = Mathf.Lerp(0f, 1f, t / cutsceneFadeDuration);
            yield return null;
        }
        cutsceneFadeOverlay.alpha = 1f;
        cutsceneFadeOverlay.gameObject.SetActive(false);
    }
    
    private IEnumerator ShowCutsceneSkipButtonWithDelay()
    {
        yield return new WaitForSeconds(2f);
        
        // Only show skip button to MasterClient
        bool isHost = PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
        if (cutsceneSkipButton != null && isHost)
        {
            cutsceneSkipButton.SetActive(true);
        }
    }
    
    private void OnCutsceneSkip()
    {
        // Only MasterClient can skip
        bool isHost = PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
        if (isHost && photonView != null && PhotonNetwork.IsConnected)
        {
            photonView.RPC("RPC_SkipQuestCutscene", RpcTarget.AllBuffered);
        }
        else if (isHost)
        {
            // Offline mode - skip directly
            SkipQuestCutscene();
        }
    }
    
    [PunRPC]
    private void RPC_SkipQuestCutscene()
    {
        SkipQuestCutscene();
    }
    
    private void SkipQuestCutscene()
    {
        cutsceneSkipped = true;
        if (currentCutscene != null)
        {
            currentCutscene.Stop();
        }
        
        if (cutsceneSkipButton != null)
        {
            cutsceneSkipButton.SetActive(false);
        }
    }

    private void LockLocalPlayers()
    {
        _lockedCombats.Clear();
        _lockedControllers.Clear();

        var allStats = UnityEngine.Object.FindObjectsByType<PlayerStats>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var s in allStats)
        {
            var pv = s.GetComponent<Photon.Pun.PhotonView>();
            if (pv != null && !pv.IsMine) continue;

            var pc = s.GetComponentInChildren<PlayerCombat>(true) ?? s.GetComponentInParent<PlayerCombat>();
            if (pc != null)
            {
                _lockedCombats.Add((pc, pc.enabled));
                pc.SetCanControl(false);
                pc.enabled = false;
            }

            var tc = s.GetComponentInChildren<ThirdPersonController>(true) ?? s.GetComponentInParent<ThirdPersonController>();
            if (tc != null)
            {
                _lockedControllers.Add((tc, tc.enabled));
                tc.SetCanMove(false);
                tc.SetCanControl(false);
            }
        }
    }

    private void UnlockLocalPlayers()
    {
        for (int i = 0; i < _lockedCombats.Count; i++)
        {
            var (pc, was) = _lockedCombats[i];
            if (pc == null) continue;
            pc.enabled = was;
            pc.SetCanControl(true);
        }
        for (int i = 0; i < _lockedControllers.Count; i++)
        {
            var (tc, was) = _lockedControllers[i];
            if (tc == null) continue;
            tc.SetCanMove(true);
            tc.SetCanControl(true);
        }
        _lockedCombats.Clear();
        _lockedControllers.Clear();
    }

    private void OnSceneUnloaded(Scene scene)
    {
        // scene unload can interrupt cutscene coroutine before ShowPlayers()/UI restore runs.
        // restore first, then clear references.
        RestoreQuestCutsceneRuntimeState(releaseInputLock: true);

        // Drop all scene-object component references so they can be GC'd after unload
        ClearComponentLists();
        _questCutsceneActive = false;
        currentCutscene = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!Application.isPlaying) return;
        if (!PhotonNetwork.IsConnected || PhotonNetwork.IsMasterClient) return;
        StartCoroutine(DelayedQuestSync());
    }

    private void BroadcastQuestStateBuffered()
    {
        if (photonView == null || !PhotonNetwork.IsConnected || !PhotonNetwork.IsMasterClient)
            return;

        string payloadJson = BuildQuestStatePayloadJson();
        photonView.RPC("RPC_SyncQuestStatePayload", RpcTarget.OthersBuffered, payloadJson);
    }

    private string BuildQuestStatePayloadJson()
    {
        var payload = new QuestStatePayload
        {
            currentQuestIndex = currentQuestIndex,
            quests = new QuestSyncPayload[quests != null ? quests.Length : 0]
        };

        if (quests != null)
        {
            for (int i = 0; i < quests.Length; i++)
            {
                Quest q = quests[i];
                if (q == null)
                {
                    payload.quests[i] = new QuestSyncPayload();
                    continue;
                }

                var questPayload = new QuestSyncPayload
                {
                    isCompleted = q.isCompleted,
                    currentObjectiveIndex = q.currentObjectiveIndex,
                    currentCount = q.currentCount,
                    objectives = new ObjectiveSyncPayload[q.objectives != null ? q.objectives.Length : 0]
                };

                if (q.objectives != null)
                {
                    for (int j = 0; j < q.objectives.Length; j++)
                    {
                        QuestObjective o = q.objectives[j];
                        if (o == null)
                        {
                            questPayload.objectives[j] = new ObjectiveSyncPayload();
                            continue;
                        }

                        int[] collect = null;
                        if (o.collectProgress != null)
                        {
                            collect = new int[o.collectProgress.Length];
                            Array.Copy(o.collectProgress, collect, o.collectProgress.Length);
                        }

                        questPayload.objectives[j] = new ObjectiveSyncPayload
                        {
                            currentCount = o.currentCount,
                            collectProgress = collect
                        };
                    }
                }

                payload.quests[i] = questPayload;
            }
        }

        return JsonUtility.ToJson(payload);
    }

    private void ApplyQuestStatePayload(string payloadJson)
    {
        if (string.IsNullOrEmpty(payloadJson) || quests == null)
            return;

        QuestStatePayload payload = null;
        try
        {
            payload = JsonUtility.FromJson<QuestStatePayload>(payloadJson);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[QuestManager] Failed to parse quest payload: {ex.Message}");
            return;
        }

        if (payload == null || payload.quests == null)
            return;

        for (int i = 0; i < quests.Length && i < payload.quests.Length; i++)
        {
            Quest localQuest = quests[i];
            QuestSyncPayload syncQuest = payload.quests[i];
            if (localQuest == null || syncQuest == null)
                continue;

            localQuest.isCompleted = syncQuest.isCompleted;
            localQuest.currentCount = syncQuest.currentCount;

            if (localQuest.objectives != null && localQuest.objectives.Length > 0)
            {
                localQuest.currentObjectiveIndex = Mathf.Clamp(syncQuest.currentObjectiveIndex, 0, localQuest.objectives.Length - 1);

                if (syncQuest.objectives != null)
                {
                    for (int j = 0; j < localQuest.objectives.Length && j < syncQuest.objectives.Length; j++)
                    {
                        QuestObjective localObjective = localQuest.objectives[j];
                        ObjectiveSyncPayload syncObjective = syncQuest.objectives[j];
                        if (localObjective == null || syncObjective == null)
                            continue;

                        // in multiplayer, TalkTo and Collect progress is tracked per-player locally;
                        // do not overwrite with the master's values or it breaks individual tracking
                        bool isPerPlayerObjective = PhotonNetwork.IsConnected && PhotonNetwork.InRoom
                            && (localObjective.objectiveType == ObjectiveType.TalkTo || localObjective.objectiveType == ObjectiveType.Collect);

                        if (!isPerPlayerObjective)
                        {
                            localObjective.currentCount = syncObjective.currentCount;

                            if (syncObjective.collectProgress != null)
                            {
                                localObjective.collectProgress = new int[syncObjective.collectProgress.Length];
                                Array.Copy(syncObjective.collectProgress, localObjective.collectProgress, syncObjective.collectProgress.Length);
                            }
                        }
                    }
                }
            }
            else
            {
                localQuest.currentObjectiveIndex = 0;
            }
        }

        currentQuestIndex = Mathf.Clamp(payload.currentQuestIndex, 0, quests.Length - 1);
        // do not clear _collectCompletionNotifiedLocally here — per-player TalkTo/Collect
        // progress is local and should survive state syncs
        _collectObjectiveCompletionsByKey.Clear();

        // apply enable/disable GameObjects to match synced quest state for joiners
        for (int i = 0; i < quests.Length; i++)
        {
            Quest q = quests[i];
            if (q == null) continue;

            // quests at or before current index have been started
            if (i <= currentQuestIndex)
            {
                ApplyQuestGameObjects(q.enableOnStart, q.disableOnStart);
            }

            // completed quests also apply complete objects
            if (q.isCompleted)
            {
                ApplyQuestGameObjects(q.enableOnComplete, q.disableOnComplete);
            }
        }

        OnQuestUpdated?.Invoke(GetCurrentQuest());
        UpdateQuestUI();
    }

    // --- IInRoomCallbacks ---
    public void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // when a player leaves, prune their actor from all completion sets and recheck
        foreach (var kvp in _collectObjectiveCompletionsByKey)
        {
            kvp.Value.Remove(otherPlayer.ActorNumber);
        }

        // re-evaluate every tracked objective in case the remaining count is now met
        Quest currentQuest = GetCurrentQuest();
        if (currentQuest == null || currentQuest.isCompleted || currentQuest.objectives == null) return;

        for (int i = 0; i < currentQuest.objectives.Length; i++)
        {
            var obj = currentQuest.objectives[i];
            if (obj == null) continue;
            if (obj.objectiveType != ObjectiveType.TalkTo && obj.objectiveType != ObjectiveType.Collect) continue;

            string key = BuildCollectObjectiveKey(currentQuestIndex, i);
            if (!_collectObjectiveCompletionsByKey.TryGetValue(key, out HashSet<int> actors)) continue;

            int requiredPlayers = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.PlayerCount : 1;
            int completedCount = actors.Count;

            // broadcast updated count
            if (photonView != null)
                photonView.RPC("RPC_SyncCollectCompletionCount", RpcTarget.AllBuffered, currentQuestIndex, i, completedCount, requiredPlayers);

            if (completedCount >= requiredPlayers)
            {
                if (photonView != null)
                    photonView.RPC("RPC_CompleteCollectObjectiveForAll", RpcTarget.AllBuffered, currentQuestIndex, i);
                else
                    ApplyCollectObjectiveCompletionGlobal(currentQuestIndex, i);
            }
        }
    }

    public void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // broadcast current completion counts to the new player
        Quest currentQuest = GetCurrentQuest();
        if (currentQuest == null || currentQuest.isCompleted || currentQuest.objectives == null) return;

        for (int i = 0; i < currentQuest.objectives.Length; i++)
        {
            var obj = currentQuest.objectives[i];
            if (obj == null) continue;
            if (obj.objectiveType != ObjectiveType.TalkTo && obj.objectiveType != ObjectiveType.Collect) continue;

            string key = BuildCollectObjectiveKey(currentQuestIndex, i);
            if (!_collectObjectiveCompletionsByKey.TryGetValue(key, out HashSet<int> actors)) continue;

            int requiredPlayers = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.PlayerCount : 1;
            if (photonView != null)
                photonView.RPC("RPC_SyncCollectCompletionCount", RpcTarget.AllBuffered, currentQuestIndex, i, actors.Count, requiredPlayers);
        }
    }

    public void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged) { }
    public void OnPlayerPropertiesUpdate(Photon.Realtime.Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps) { }
    public void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient) { }

    private void ClearComponentLists()
    {
        _hiddenRenderers.Clear();
        _hiddenCanvases.Clear();
        _hiddenCameras.Clear();
        _lockedCombats.Clear();
        _lockedControllers.Clear();
    }

    private void RestoreQuestCutsceneRuntimeState(bool releaseInputLock)
    {
        if (_hiddenRenderers.Count > 0 || _hiddenCanvases.Count > 0 || _hiddenCameras.Count > 0)
            ShowPlayers();

        if (_lockedCombats.Count > 0 || _lockedControllers.Count > 0)
            UnlockLocalPlayers();

        if (_questCutsceneUiOpened)
        {
            LocalUIManager.Instance?.Close("QuestCutscene");
            _questCutsceneUiOpened = false;
        }

        if (cutsceneSkipButton != null)
            cutsceneSkipButton.SetActive(false);

        if (cutsceneFadeOverlay != null)
        {
            cutsceneFadeOverlay.alpha = 0f;
            cutsceneFadeOverlay.gameObject.SetActive(false);
        }

        _questCutsceneActive = false;
        currentCutscene = null;

        if (releaseInputLock && _activeQuestInputLockToken >= 0)
        {
            LocalInputLocker.Ensure().Release(_activeQuestInputLockToken);
            _activeQuestInputLockToken = -1;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void HidePlayers()
    {
        _hiddenRenderers.Clear();
        _hiddenCanvases.Clear();
        _hiddenCameras.Clear();

        // Include inactive so we still hide late-joined / temporarily-disabled player roots
        var players = UnityEngine.Object.FindObjectsByType<PlayerStats>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var p in players)
        {
            foreach (var r in p.GetComponentsInChildren<Renderer>(true))
            {
                _hiddenRenderers.Add((r, r.enabled));
                r.enabled = false;
            }
            foreach (var c in p.GetComponentsInChildren<Canvas>(true))
            {
                _hiddenCanvases.Add((c, c.enabled));
                c.enabled = false;
            }
            foreach (var cam in p.GetComponentsInChildren<Camera>(true))
            {
                _hiddenCameras.Add((cam, cam.enabled));
                cam.enabled = false;
            }
        }
    }

    private void ShowPlayers()
    {
        for (int i = 0; i < _hiddenRenderers.Count; i++)
        {
            var (r, was) = _hiddenRenderers[i];
            if (r != null) r.enabled = was;
        }
        for (int i = 0; i < _hiddenCanvases.Count; i++)
        {
            var (c, was) = _hiddenCanvases[i];
            if (c != null) c.enabled = was;
        }
        for (int i = 0; i < _hiddenCameras.Count; i++)
        {
            var (cam, was) = _hiddenCameras[i];
            if (cam != null) cam.enabled = was;
        }

        _hiddenRenderers.Clear();
        _hiddenCanvases.Clear();
        _hiddenCameras.Clear();
    }
}
