using UnityEngine;
using Photon.Pun;

// lightweight chapter/quest orchestrator to sequence CHAP 1-3 without blocking
// Configure in Inspector with the QuestManager already set up with your quests/objectives
// Multiplayer: only the local owner (or Master Client) triggers progression calls; QuestManager RPCs propagate state
public class StoryManager : MonoBehaviourPun
{
    [Header("references")]
    public QuestManager questManager;
    public PrologueManager prologueManager; // optional

    [Header("chapter to quest index mapping")] 
    // map each chapter to a quest index in QuestManager.quests
    public int chapter1QuestIndex = 0;
    public int chapter2QuestIndex = 1;
    public int chapter3QuestIndex = 2;

    [Header("settings")] 
    public bool autoStartPrologue = true; 
    public bool autoStartChapter1AfterPrologue = true;

    public int CurrentChapter { get; private set; } = 0; // 0=not started, 1..3

    public System.Action<int> OnChapterStarted; // chapter index
    public System.Action<int> OnChapterCompleted; // chapter index

    void Awake()
    {
        if (questManager == null) questManager = FindFirstObjectByType<QuestManager>();
        if (prologueManager == null) prologueManager = FindFirstObjectByType<PrologueManager>();
    }

    void Start()
    {
        if (autoStartPrologue && prologueManager != null)
        {
            // PrologueManager is assumed to control its own UI/flow; when it finishes, it can call StartChapter(1)
        }
        else if (autoStartChapter1AfterPrologue)
        {
            StartChapter(1);
        }
    }

    public void StartChapter(int chapter)
    {
        if (chapter < 1 || chapter > 3) { Debug.LogWarning($"invalid chapter {chapter}"); return; }
        if (photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !photonView.IsMine)
            return; // only local owner triggers; QuestManager will RPC progress

        CurrentChapter = chapter;
        int questIndex = GetQuestIndexForChapter(chapter);
        if (questManager != null && questIndex >= 0)
        {
            questManager.StartQuest(questIndex);
        }
        OnChapterStarted?.Invoke(chapter);
    }

    public void CompleteChapter(int chapter)
    {
        if (chapter != CurrentChapter) return;
        OnChapterCompleted?.Invoke(chapter);
        if (chapter < 3)
        {
            StartChapter(chapter + 1);
        }
    }

    private int GetQuestIndexForChapter(int chapter)
    {
        switch (chapter)
        {
            case 1: return chapter1QuestIndex;
            case 2: return chapter2QuestIndex;
            case 3: return chapter3QuestIndex;
        }
        return -1;
    }
}
