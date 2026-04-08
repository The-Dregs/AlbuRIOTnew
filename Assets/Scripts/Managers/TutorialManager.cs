using UnityEngine;
using TMPro;
using Photon.Pun;
using System.Collections.Generic;

public class TutorialManager : MonoBehaviourPunCallbacks
{
    public static bool IsDialogueBlockingUI { get; private set; }

    [Header("Tutorial Data")]
    [Tooltip("Tutorial data definitions - each entry defines a complete tutorial step")]
    public TutorialData[] tutorialSteps;

    [Header("Legacy Support (for backwards compatibility)")]
    [Tooltip("Old dialogue panels array - will be migrated to TutorialData")]
    public GameObject[] dialoguePanels;
    [Tooltip("Old continue prompts array")]
    public GameObject[] continuePrompts;
    public float continueDelay = 1.2f;

    [Header("Global UI (optional - for legacy quest UI)")]
    public GameObject questUI;

    [Header("Player Settings")]
    public string playerTag = "Player";

    // Per-player dialogue tracking: PhotonView ViewID -> active tutorial data
    private Dictionary<int, TutorialData> activePlayerTutorials = new Dictionary<int, TutorialData>();
    private Dictionary<int, Coroutine> activeContinueCoroutines = new Dictionary<int, Coroutine>();
    private Dictionary<int, bool> waitingForContinue = new Dictionary<int, bool>();
    private Dictionary<int, GameObject> activeDialoguePanels = new Dictionary<int, GameObject>(); // Track panel GameObjects per player
    private Dictionary<int, GameObject> activeContinuePrompts = new Dictionary<int, GameObject>(); // Track continue prompts per player
    private Dictionary<int, int> activeInputLockTokens = new Dictionary<int, int>(); // playerID -> LocalInputLocker token
    private Dictionary<int, PlayerCombat> activePlayerCombat = new Dictionary<int, PlayerCombat>();
    private Dictionary<int, bool> activePlayerCombatWasEnabled = new Dictionary<int, bool>();
    private readonly List<int> reusableKeysToClose = new List<int>(8);

    private void Start()
    {
        // Hide all legacy panels if they exist
        if (dialoguePanels != null)
        {
            foreach (var panel in dialoguePanels)
                if (panel != null) panel.SetActive(false);
        }
        if (continuePrompts != null)
        {
            foreach (var prompt in continuePrompts)
                if (prompt != null) prompt.SetActive(false);
        }

        // Hide global quest UI
        if (questUI != null) questUI.SetActive(false);
    }

    /// <summary>
    /// Show tutorial for a specific player (only that player will see it)
    /// </summary>
    public void ShowTutorialForPlayer(GameObject player, int tutorialIndex)
    {
        if (player == null) return;

        var pv = player.GetComponent<PhotonView>();
        // Use ViewID as unique identifier (works offline too, just uses instance ID)
        int playerID = pv != null ? pv.ViewID : player.GetInstanceID();

        // Only show for local player in multiplayer
        if (pv != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !pv.IsMine)
            return;

        // Use TutorialData if available, fall back to legacy
        TutorialData tutorialData = null;
        GameObject dialoguePanelPrefab = null;
        GameObject continuePromptPrefab = null;
        float delay = continueDelay;

        if (tutorialSteps != null && tutorialIndex >= 0 && tutorialIndex < tutorialSteps.Length)
        {
            tutorialData = tutorialSteps[tutorialIndex];
            if (tutorialData != null)
            {
                dialoguePanelPrefab = tutorialData.dialoguePanel;
                continuePromptPrefab = tutorialData.continuePrompt;
                delay = tutorialData.continueDelay;
            }
        }
        else if (dialoguePanels != null && tutorialIndex >= 0 && tutorialIndex < dialoguePanels.Length)
        {
            // Legacy support
            dialoguePanelPrefab = dialoguePanels[tutorialIndex];
            continuePromptPrefab = (continuePrompts != null && tutorialIndex < continuePrompts.Length) 
                ? continuePrompts[tutorialIndex] : null;
        }

        if (dialoguePanelPrefab == null) return;

        // Hide any existing dialogue for this player
        HideTutorialForPlayer(player);

        // Instantiate panel per-player (ensures per-player UI in multiplayer)
        GameObject dialoguePanel = InstantiatePanelForPlayer(playerID, dialoguePanelPrefab);
        GameObject continuePrompt = continuePromptPrefab != null 
            ? InstantiatePanelForPlayer(playerID, continuePromptPrefab, dialoguePanel) 
            : null;

        if (dialoguePanel == null) return;

        // Show dialogue panel
        dialoguePanel.SetActive(true);
        if (continuePrompt != null) continuePrompt.SetActive(false);

        // Track active tutorial
        activePlayerTutorials[playerID] = tutorialData;
        activeDialoguePanels[playerID] = dialoguePanel;
        if (continuePrompt != null) activeContinuePrompts[playerID] = continuePrompt;
        waitingForContinue[playerID] = false;
        IsDialogueBlockingUI = activeDialoguePanels.Count > 0;

        // Lock player input and unlock cursor (movement, combat, camera locked; cursor free for UI)
        int lockToken = LocalInputLocker.Ensure().Acquire("Tutorial", lockMovement: true, lockCombat: true, lockCamera: true, cursorUnlock: true);
        activeInputLockTokens[playerID] = lockToken;

        // hard-disable combat while tutorial dialogue is open so left-click cannot attack while closing dialogue
        var combat = player.GetComponentInChildren<PlayerCombat>(true) ?? player.GetComponentInParent<PlayerCombat>();
        if (combat != null)
        {
            activePlayerCombat[playerID] = combat;
            activePlayerCombatWasEnabled[playerID] = combat.enabled;
            combat.SetCanControl(false);
            combat.enabled = false;
        }

        // Start continue prompt coroutine
        if (activeContinueCoroutines.ContainsKey(playerID) && activeContinueCoroutines[playerID] != null)
        {
            StopCoroutine(activeContinueCoroutines[playerID]);
        }
        activeContinueCoroutines[playerID] = StartCoroutine(ShowContinuePromptAfterDelay(playerID, continuePrompt, delay));

        // Update dialogue text if provided
        if (tutorialData != null && !string.IsNullOrEmpty(tutorialData.dialogueText))
        {
            UpdateDialogueText(dialoguePanel, tutorialData.dialogueText);
        }
    }

    

    private System.Collections.IEnumerator ShowContinuePromptAfterDelay(int playerID, GameObject prompt, float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (prompt != null && waitingForContinue.ContainsKey(playerID))
        {
            prompt.SetActive(true);
            waitingForContinue[playerID] = true;
        }
    }

    /// <summary>
    /// Hide tutorial for a specific player
    /// </summary>
    public void HideTutorialForPlayer(GameObject player)
    {
        if (player == null) return;

        var pv = player.GetComponent<PhotonView>();
        int playerID = pv != null ? pv.ViewID : player.GetInstanceID();

        // Only hide for local player in multiplayer
        if (pv != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !pv.IsMine)
            return;

        CloseDialogueForPlayer(playerID);
    }

    private void Update()
    {
        if (waitingForContinue.Count == 0) return;
        if (!Input.GetMouseButtonDown(0)) return;

        // Check for continue input for each active player (only local player can continue)
        reusableKeysToClose.Clear();
        
        // First pass: collect keys to close without modifying the dictionary
        foreach (var kvp in waitingForContinue)
        {
            int playerID = kvp.Key;
            bool isWaiting = kvp.Value;

            if (isWaiting)
            {
                // Get the panel for this player
                GameObject panel = null;
                if (activeDialoguePanels.ContainsKey(playerID))
                {
                    panel = activeDialoguePanels[playerID];
                }
                else if (dialoguePanels != null && playerID >= 0 && playerID < dialoguePanels.Length)
                {
                    // Legacy fallback
                    panel = dialoguePanels[playerID];
                }

                if (panel != null && panel.activeSelf)
                {
                    reusableKeysToClose.Add(playerID);
                }
            }
        }

        // Second pass: close dialogues for collected keys (modifies dictionary)
        for (int i = 0; i < reusableKeysToClose.Count; i++)
        {
            CloseDialogueForPlayer(reusableKeysToClose[i]);
        }
    }

    private GameObject InstantiatePanelForPlayer(int playerID, GameObject panelPrefab, GameObject parentCanvas = null)
    {
        if (panelPrefab == null) return null;

        // If panel is already instantiated for this player, reuse it
        if (activeDialoguePanels.ContainsKey(playerID) && activeDialoguePanels[playerID] != null)
        {
            var existing = activeDialoguePanels[playerID];
            if (existing.name.Contains(panelPrefab.name) || existing.name == panelPrefab.name)
            {
                return existing;
            }
        }

        // Check if it's a prefab (has PrefabAsset or is in Resources)
        bool isPrefab = !panelPrefab.scene.IsValid();
        
        if (isPrefab)
        {
            // Instantiate prefab
            GameObject instance = Instantiate(panelPrefab);
            instance.name = $"{panelPrefab.name}_Player{playerID}";
            
            // Parent to player's UI canvas if available, otherwise scene root
            if (parentCanvas != null)
            {
                instance.transform.SetParent(parentCanvas.transform, false);
            }
            
            return instance;
        }
        else
        {
            // Scene reference - use directly (for backwards compatibility)
            // In multiplayer, this should only be visible to local player due to IsMine check
            return panelPrefab;
        }
    }

    private void UpdateDialogueText(GameObject panel, string text)
    {
        if (panel == null || string.IsNullOrEmpty(text)) return;

        // Try to find TextMeshPro component in panel or children
        var tmpText = panel.GetComponentInChildren<TMPro.TextMeshProUGUI>();
        if (tmpText != null)
        {
            tmpText.text = text;
        }
    }

    private void CloseDialogueForPlayer(int playerID)
    {
        // Hide and destroy instantiated panels
        if (activeDialoguePanels.ContainsKey(playerID))
        {
            var panel = activeDialoguePanels[playerID];
            if (panel != null)
            {
                // Only destroy if it's an instantiated prefab (not a scene reference)
                bool isInstantiated = panel.name.Contains("_Player") || !panel.scene.IsValid();
                if (isInstantiated && Application.isPlaying)
                {
                    Destroy(panel);
                }
                else
                {
                    panel.SetActive(false);
                }
            }
            activeDialoguePanels.Remove(playerID);
        }

        IsDialogueBlockingUI = activeDialoguePanels.Count > 0;

        if (activeContinuePrompts.ContainsKey(playerID))
        {
            var prompt = activeContinuePrompts[playerID];
            if (prompt != null)
            {
                bool isInstantiated = prompt.name.Contains("_Player") || !prompt.scene.IsValid();
                if (isInstantiated && Application.isPlaying)
                {
                    Destroy(prompt);
                }
                else
                {
                    prompt.SetActive(false);
                }
            }
            activeContinuePrompts.Remove(playerID);
        }

        // Clean up tutorial data reference
        activePlayerTutorials.Remove(playerID);

        // Legacy support (only if not using instantiated panels)
        if (!activeDialoguePanels.ContainsKey(playerID) && dialoguePanels != null && playerID >= 0 && playerID < dialoguePanels.Length)
        {
            var panel = dialoguePanels[playerID];
            if (panel != null) panel.SetActive(false);
        }
        if (!activeContinuePrompts.ContainsKey(playerID) && continuePrompts != null && playerID >= 0 && playerID < continuePrompts.Length)
        {
            var prompt = continuePrompts[playerID];
            if (prompt != null) prompt.SetActive(false);
        }

        // Stop coroutine
        if (activeContinueCoroutines.ContainsKey(playerID) && activeContinueCoroutines[playerID] != null)
        {
            StopCoroutine(activeContinueCoroutines[playerID]);
            activeContinueCoroutines.Remove(playerID);
        }

        waitingForContinue.Remove(playerID);

        // Release input lock and restore gameplay cursor
        if (activeInputLockTokens.TryGetValue(playerID, out int token))
        {
            LocalInputLocker.Ensure().Release(token);
            activeInputLockTokens.Remove(playerID);
            LocalInputLocker.Ensure().ForceGameplayCursor();
        }

        if (activePlayerCombat.TryGetValue(playerID, out PlayerCombat combat))
        {
            bool wasEnabled = activePlayerCombatWasEnabled.TryGetValue(playerID, out bool enabledBefore) ? enabledBefore : true;
            if (combat != null)
            {
                combat.enabled = wasEnabled;
                combat.SetCanControl(true);
            }
            activePlayerCombat.Remove(playerID);
            activePlayerCombatWasEnabled.Remove(playerID);
        }
    }

    // Legacy method for backwards compatibility
    public void OnTutorialTrigger(int triggerIndex)
    {
        // Find local player
        var localPlayer = FindLocalPlayer();
        if (localPlayer != null)
        {
            ShowTutorialForPlayer(localPlayer, triggerIndex);
        }
    }

    // Legacy method
    public void ShowTutorialPanel(int panelIndex)
    {
        var localPlayer = FindLocalPlayer();
        if (localPlayer != null)
        {
            ShowTutorialForPlayer(localPlayer, panelIndex);
        }
    }

    private GameObject cachedLocalPlayer;
    private float lastPlayerCacheTime = -1f;
    private const float PLAYER_CACHE_INTERVAL = 1f;
    
    private GameObject FindLocalPlayer()
    {
        if (cachedLocalPlayer != null && Time.time - lastPlayerCacheTime < PLAYER_CACHE_INTERVAL)
        {
            if (cachedLocalPlayer != null)
                return cachedLocalPlayer;
        }
        
        var players = GameObject.FindGameObjectsWithTag(playerTag);
        foreach (var player in players)
        {
            if (player == null) continue;
            var pv = player.GetComponent<PhotonView>();
            if (pv == null || !PhotonNetwork.IsConnected || pv.IsMine)
            {
                cachedLocalPlayer = player;
                lastPlayerCacheTime = Time.time;
                return player;
            }
        }
        cachedLocalPlayer = players.Length > 0 ? players[0] : null;
        lastPlayerCacheTime = Time.time;
        return cachedLocalPlayer;
    }
    
    void OnDestroy()
    {
        cachedLocalPlayer = null;
        foreach (var coroutine in activeContinueCoroutines.Values)
        {
            if (coroutine != null)
                StopCoroutine(coroutine);
        }
        activeContinueCoroutines.Clear();
        if (Application.isPlaying)
        {
            foreach (var token in activeInputLockTokens.Values)
                LocalInputLocker.Instance?.Release(token);
            LocalInputLocker.Instance?.ForceGameplayCursor();

            foreach (var kvp in activePlayerCombat)
            {
                var id = kvp.Key;
                var combat = kvp.Value;
                bool wasEnabled = activePlayerCombatWasEnabled.TryGetValue(id, out bool enabledBefore) ? enabledBefore : true;
                if (combat != null)
                {
                    combat.enabled = wasEnabled;
                    combat.SetCanControl(true);
                }
            }
        }
        activeInputLockTokens.Clear();
        activePlayerCombat.Clear();
        activePlayerCombatWasEnabled.Clear();
        activePlayerTutorials.Clear();
        activeDialoguePanels.Clear();
        activeContinuePrompts.Clear();
        waitingForContinue.Clear();
        reusableKeysToClose.Clear();
        IsDialogueBlockingUI = false;
    }
}