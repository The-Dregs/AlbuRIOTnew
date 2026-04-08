using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine.SceneManagement;
using Photon.Pun;

public class EncyclopediaManager : MonoBehaviour
{
    public static EncyclopediaManager Instance { get; private set; }

    private const string SaveFileName = "encyclopedia_state.json";
    
    [Header("Encyclopedia Data")]
    [Tooltip("All available encyclopedia entries")]
    public EncyclopediaEntry[] allEntries;

    [Header("UI Bridge")]
    [Tooltip("Optional explicit UI target for button-driven open/close from menu scenes.")]
    public EncyclopediaUI defaultEncyclopediaUI;
    [Tooltip("Optional direct panel fallback for menu scenes when EncyclopediaUI cannot be resolved.")]
    public GameObject defaultEncyclopediaPanel;
    
    [Header("First Encounter")]
    [Tooltip("If false, encyclopedia discovery will not spawn/show first encounter dialogue.")]
    public bool enableFirstEncounterDialogue = false;
    [Tooltip("Prefab for first encounter dialogue")]
    public GameObject firstEncounterDialoguePrefab;
    
    private HashSet<string> discoveredEnemyIds = new HashSet<string>();
    private HashSet<string> killedEnemyIds = new HashSet<string>();
    private EnemyManager subscribedEnemyManager;
    
    public System.Action<string> OnEnemyDiscovered;
    public System.Action<string> OnEnemyKilled;
    public System.Action<EncyclopediaEntry> OnEntryUnlocked;
    
    [Header("Nuno Dialogue")]
    [Tooltip("If true, Nuno will comment the first time you kill any enemy and the first time you kill each enemy type.")]
    public bool enableFirstKillNunoDialogue = true;

    [Header("Progress Sources")]
    [Tooltip("Use EnemyManager.OnEnemyDied as a kill source in multiplayer. Keep OFF for per-player progression.")]
    public bool useEnemyManagerDeathsInMultiplayer = false;
    
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[EncyclopediaManager] Duplicate manager component detected. Removing duplicate component only.");
            Destroy(this);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;
        EncyclopediaProgressEvents.OnEncounterRecorded += DiscoverEnemy;
        EncyclopediaProgressEvents.OnKillRecorded += KillEnemy;
        
        LoadEncyclopediaData();
    }
    
    void Start()
    {
        TryBindEnemyManager();
    }

    void Update()
    {
        if (subscribedEnemyManager == null)
            TryBindEnemyManager();
    }
    
    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        
        SceneManager.sceneLoaded -= OnSceneLoaded;
        EncyclopediaProgressEvents.OnEncounterRecorded -= DiscoverEnemy;
        EncyclopediaProgressEvents.OnKillRecorded -= KillEnemy;
        UnbindEnemyManager();
        
        // Clear event subscriptions
        OnEnemyDiscovered = null;
        OnEnemyKilled = null;
        OnEntryUnlocked = null;
        
        // Clear collections
        if (discoveredEnemyIds != null)
            discoveredEnemyIds.Clear();
        if (killedEnemyIds != null)
            killedEnemyIds.Clear();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TryBindEnemyManager();
    }

    private void TryBindEnemyManager()
    {
        if (subscribedEnemyManager != null)
            return;

        if (PhotonNetwork.IsConnected && !useEnemyManagerDeathsInMultiplayer)
            return;

        var enemyManager = FindFirstObjectByType<EnemyManager>();
        if (enemyManager == null)
            return;

        subscribedEnemyManager = enemyManager;
        subscribedEnemyManager.OnEnemyDied -= HandleEnemyDied;
        subscribedEnemyManager.OnEnemyDied += HandleEnemyDied;
        Debug.Log("[EncyclopediaManager] Bound to EnemyManager enemy death events.");
    }

    private void UnbindEnemyManager()
    {
        if (subscribedEnemyManager == null)
            return;

        subscribedEnemyManager.OnEnemyDied -= HandleEnemyDied;
        subscribedEnemyManager = null;
    }
    
    private void HandleEnemyDied(BaseEnemyAI enemy)
    {
        if (enemy == null || enemy.enemyData == null) return;
        
        string enemyId = !string.IsNullOrEmpty(enemy.enemyData.enemyName) 
            ? enemy.enemyData.enemyName 
            : enemy.gameObject.name;
        
        KillEnemy(enemyId);
    }
    
    private void LoadEncyclopediaData()
    {
        discoveredEnemyIds.Clear();
        killedEnemyIds.Clear();

        string savePath = GetSavePath();

        // Prefer local JSON save file for the lobby and cross-scene persistence
        if (File.Exists(savePath))
        {
            try
            {
                string json = File.ReadAllText(savePath);
                var saveData = JsonUtility.FromJson<EncyclopediaSaveData>(json);
                if (saveData != null)
                {
                    if (saveData.discoveredEnemyIds != null)
                    {
                        foreach (string id in saveData.discoveredEnemyIds)
                        {
                            if (!string.IsNullOrEmpty(id))
                                discoveredEnemyIds.Add(id);
                        }
                    }

                    if (saveData.killedEnemyIds != null)
                    {
                        foreach (string id in saveData.killedEnemyIds)
                        {
                            if (!string.IsNullOrEmpty(id))
                                killedEnemyIds.Add(id);
                        }
                    }
                }

                return;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[EncyclopediaManager] Failed to load local JSON save: {ex.Message}");
            }
        }

        // fallback to PlayerPrefs if the JSON file does not exist yet
        string discoveredJson = PlayerPrefs.GetString("Encyclopedia_Discovered", "");
        string killedJson = PlayerPrefs.GetString("Encyclopedia_Killed", "");

        if (!string.IsNullOrEmpty(discoveredJson))
        {
            string[] ids = discoveredJson.Split(',');
            foreach (string id in ids)
            {
                if (!string.IsNullOrEmpty(id))
                    discoveredEnemyIds.Add(id);
            }
        }

        if (!string.IsNullOrEmpty(killedJson))
        {
            string[] ids = killedJson.Split(',');
            foreach (string id in ids)
            {
                if (!string.IsNullOrEmpty(id))
                    killedEnemyIds.Add(id);
            }
        }

        SaveEncyclopediaData();
    }
    
    private void SaveEncyclopediaData()
    {
        PlayerPrefs.SetString("Encyclopedia_Discovered", string.Join(",", discoveredEnemyIds));
        PlayerPrefs.SetString("Encyclopedia_Killed", string.Join(",", killedEnemyIds));
        PlayerPrefs.Save();

        try
        {
            var saveData = new EncyclopediaSaveData
            {
                discoveredEnemyIds = discoveredEnemyIds.ToArray(),
                killedEnemyIds = killedEnemyIds.ToArray()
            };

            string json = JsonUtility.ToJson(saveData, true);
            File.WriteAllText(GetSavePath(), json);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[EncyclopediaManager] Failed to write local JSON save: {ex.Message}");
        }
    }

    private string GetSavePath()
    {
        return Path.Combine(Application.persistentDataPath, SaveFileName);
    }
    
    public bool IsEnemyDiscovered(string enemyId)
    {
        return discoveredEnemyIds.Contains(enemyId);
    }
    
    public bool IsEnemyKilled(string enemyId)
    {
        return killedEnemyIds.Contains(enemyId);
    }

    public bool IsEntryUnlocked(EncyclopediaEntry entry)
    {
        if (entry == null) return false;

        return entry.unlockOnEncounter
            ? IsEntryDiscovered(entry)
            : IsEntryKilled(entry);
    }

    public bool IsEntryEncountered(EncyclopediaEntry entry)
    {
        return IsEntryDiscovered(entry) || IsEntryKilled(entry);
    }
    
    public EncyclopediaEntry GetEntry(string enemyId)
    {
        return allEntries.FirstOrDefault(e => e != null && e.MatchesEnemyId(enemyId));
    }
    
    public List<EncyclopediaEntry> GetUnlockedEntries()
    {
        List<EncyclopediaEntry> unlocked = new List<EncyclopediaEntry>();
        foreach (var entry in allEntries)
        {
            if (entry == null) continue;
            
            bool isUnlocked = entry.unlockOnEncounter 
                ? IsEntryDiscovered(entry)
                : IsEntryKilled(entry);
                
            if (isUnlocked)
                unlocked.Add(entry);
        }
        return unlocked.OrderBy(e => e.displayName).ToList();
    }

    public bool IsEntryDiscovered(EncyclopediaEntry entry)
    {
        if (entry == null) return false;

        foreach (string discoveredId in discoveredEnemyIds)
        {
            if (entry.MatchesEnemyId(discoveredId))
                return true;
        }
        return false;
    }

    public bool IsEntryKilled(EncyclopediaEntry entry)
    {
        if (entry == null) return false;

        foreach (string killedId in killedEnemyIds)
        {
            if (entry.MatchesEnemyId(killedId))
                return true;
        }
        return false;
    }
    
    public void DiscoverEnemy(string enemyId)
    {
        if (string.IsNullOrEmpty(enemyId)) return;
        if (discoveredEnemyIds.Contains(enemyId)) return;
        
        discoveredEnemyIds.Add(enemyId);
        SaveEncyclopediaData();
        
        var entry = GetEntry(enemyId);
        if (entry != null && entry.unlockOnEncounter)
        {
            OnEntryUnlocked?.Invoke(entry);
        }
        
        OnEnemyDiscovered?.Invoke(enemyId);
        
        // optional: encyclopedia encounter dialogue can be disabled when another system handles voicelines
        if (enableFirstEncounterDialogue)
        {
            ShowFirstEncounterDialogue(enemyId);
        }
    }
    
    public void KillEnemy(string enemyId)
    {
        if (string.IsNullOrEmpty(enemyId)) return;
        
        // Ensure discovered if not already
        if (!discoveredEnemyIds.Contains(enemyId))
        {
            discoveredEnemyIds.Add(enemyId);
        }
        
        if (!killedEnemyIds.Contains(enemyId))
        {
            // this is the first time this enemy type has been killed
            killedEnemyIds.Add(enemyId);
            SaveEncyclopediaData();
            
            var entry = GetEntry(enemyId);
            if (entry != null && !entry.unlockOnEncounter)
            {
                OnEntryUnlocked?.Invoke(entry);
            }
            
            OnEnemyKilled?.Invoke(enemyId);
            
            // nuno commentary:
            // - on the very first enemy kill ever, give a short skills HUD/tutorial line
            // - on every first kill per enemy type, give a short flavor line about that creature
            if (enableFirstKillNunoDialogue)
            {
                bool isFirstEverKill = killedEnemyIds.Count == 1;
                if (isFirstEverKill)
                {
                    ShowFirstEverKillDialogue(enemyId, entry);
                }
                else
                {
                    ShowFirstKillPerTypeDialogue(enemyId, entry);
                }
            }
        }
    }
    
    private void ShowFirstEncounterDialogue(string enemyId)
    {
        var entry = GetEntry(enemyId);
        if (entry == null) return;
        
        // Only show if this is the first discovery
        if (!discoveredEnemyIds.Contains(enemyId))
            return;
        
        // Find or create first encounter dialogue UI
        var dialogueUI = FindFirstObjectByType<FirstEncounterDialogueUI>();
        if (dialogueUI == null && firstEncounterDialoguePrefab != null)
        {
            var go = Instantiate(firstEncounterDialoguePrefab);
            dialogueUI = go.GetComponent<FirstEncounterDialogueUI>();
        }
        
        if (dialogueUI != null)
        {
            dialogueUI.ShowFirstEncounter(entry);
        }
        else
        {
            Debug.LogWarning($"[EncyclopediaManager] Could not show first encounter dialogue for {enemyId}. FirstEncounterDialogueUI component not found.");
        }
    }
    
    private void ShowFirstEverKillDialogue(string enemyId, EncyclopediaEntry entry)
    {
        var nunoUI = NunoDialogueBarUI.Instance ?? FindFirstObjectByType<NunoDialogueBarUI>();
        if (nunoUI == null) return;
        
        string displayName = entry != null && !string.IsNullOrEmpty(entry.displayName)
            ? entry.displayName
            : enemyId;
        
        string[] lines =
        {
            $"Hah! You felled your first foe — the {displayName}.",
            "The spirits of these creatures leave echoes of power behind.",
            "Watch the skill bar on your screen, Albularyo. As you steal powers, new abilities will appear there for you to use."
        };
        
        nunoUI.ShowDialogueSequence("Nuno", lines);
    }
    
    private void ShowFirstKillPerTypeDialogue(string enemyId, EncyclopediaEntry entry)
    {
        var nunoUI = NunoDialogueBarUI.Instance ?? FindFirstObjectByType<NunoDialogueBarUI>();
        if (nunoUI == null) return;
        
        string displayName = entry != null && !string.IsNullOrEmpty(entry.displayName)
            ? entry.displayName
            : enemyId;
        
        string line = $"You have struck down a {displayName}. Remember its movements and weaknesses — each spirit you face teaches you a new way to survive.";
        nunoUI.ShowDialogue("Nuno", line);
    }
    
    public void ResetEncyclopedia()
    {
        discoveredEnemyIds.Clear();
        killedEnemyIds.Clear();
        SaveEncyclopediaData();
        Debug.Log("[EncyclopediaManager] Encyclopedia reset.");
    }

    // button-friendly UI bridge methods
    public void OpenEncyclopediaFromButton()
    {
        var ui = ResolveEncyclopediaUI();
        if (ui != null)
        {
            ui.OpenEncyclopedia();
            return;
        }

        var panel = ResolveEncyclopediaPanel();
        if (panel != null)
        {
            panel.SetActive(true);
            return;
        }

        Debug.LogWarning("[EncyclopediaManager] No EncyclopediaUI or panel found to open.");
    }

    public void CloseEncyclopediaFromButton()
    {
        var ui = ResolveEncyclopediaUI();
        if (ui != null)
        {
            ui.CloseEncyclopedia();
            return;
        }

        var panel = ResolveEncyclopediaPanel();
        if (panel != null)
        {
            panel.SetActive(false);
            return;
        }

        Debug.LogWarning("[EncyclopediaManager] No EncyclopediaUI or panel found to close.");
    }

    public void ToggleEncyclopediaFromButton()
    {
        var ui = ResolveEncyclopediaUI();
        if (ui != null)
        {
            if (ui.encyclopediaPanel != null && ui.encyclopediaPanel.activeSelf)
                ui.CloseEncyclopedia();
            else
                ui.OpenEncyclopedia();
            return;
        }

        var panel = ResolveEncyclopediaPanel();
        if (panel != null)
        {
            panel.SetActive(!panel.activeSelf);
            return;
        }

        Debug.LogWarning("[EncyclopediaManager] No EncyclopediaUI or panel found to toggle.");
    }

    private EncyclopediaUI ResolveEncyclopediaUI()
    {
        if (defaultEncyclopediaUI != null)
            return defaultEncyclopediaUI;

        if (defaultEncyclopediaPanel != null)
        {
            var panelUi = defaultEncyclopediaPanel.GetComponent<EncyclopediaUI>();
            if (panelUi == null)
                panelUi = defaultEncyclopediaPanel.GetComponentInChildren<EncyclopediaUI>(true);
            if (panelUi == null)
                panelUi = defaultEncyclopediaPanel.GetComponentInParent<EncyclopediaUI>();

            // handle setups where EncyclopediaUI lives on a separate object but points to this panel
            if (panelUi == null)
            {
                var allUi = FindObjectsByType<EncyclopediaUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < allUi.Length; i++)
                {
                    if (allUi[i] != null && allUi[i].encyclopediaPanel == defaultEncyclopediaPanel)
                    {
                        panelUi = allUi[i];
                        break;
                    }
                }
            }

            if (panelUi != null)
                return panelUi;
        }

        return FindFirstObjectByType<EncyclopediaUI>();
    }

    private GameObject ResolveEncyclopediaPanel()
    {
        if (defaultEncyclopediaPanel != null)
            return defaultEncyclopediaPanel;

        if (defaultEncyclopediaUI != null && defaultEncyclopediaUI.encyclopediaPanel != null)
            return defaultEncyclopediaUI.encyclopediaPanel;

        var ui = FindFirstObjectByType<EncyclopediaUI>();
        if (ui != null && ui.encyclopediaPanel != null)
            return ui.encyclopediaPanel;

        if (ui != null)
            return ui.gameObject;

        return null;
    }
}

[System.Serializable]
public class EncyclopediaSaveData
{
    public string[] discoveredEnemyIds;
    public string[] killedEnemyIds;
}

