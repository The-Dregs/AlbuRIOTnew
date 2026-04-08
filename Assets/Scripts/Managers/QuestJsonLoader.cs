using UnityEngine;
using System.IO;
using System.Collections.Generic;

public class QuestJsonLoader : MonoBehaviour
{
    [Tooltip("Name of the JSON file in Resources/Quests (without extension)")]
    public string questJsonFile = "TutorialQuest";
    public QuestManager questManager;

    [System.Serializable]
    public class QuestObjectiveData {
        public string objectiveName;
        public string description;
        public string objectiveType;
        public string targetId;
        public int requiredCount;
        [Tooltip("for ReachArea/FindArea: when true, all players must be in the area")]
        public bool requireAllPlayers = false;
        // For multi-item Collect objectives
        public string[] collectItemIds;
        public int[] collectQuantities;
    }
    [System.Serializable]
    public class QuestData {
        public string questName;
        public string description;
        public bool requiresAllObjectives = true;
        public bool autoAdvanceObjectives = true;
        // Optional: where to start in the objectives list (e.g., 1 to skip the first for playtests)
        public int startObjectiveIndex = 0;
        // Optional cutscene names (must match PlayableDirector GameObject names in the scene)
        public string cutsceneOnStartName;
        public string cutsceneOnCompleteName;
        // dialogue lines shown during TalkTo quests (optional)
        public string[] talkDialogueLines;
        // For singular quests (TalkTo, ReachArea, FindArea, Kill): leave objectives null/empty and set these
        public string objectiveType;
        public string targetId;
        public int requiredCount = 1;
        public QuestObjectiveData[] objectives;
    }

    [System.Serializable]
    public class QuestListData {
        public QuestData[] quests;
    }

    void Awake() {
        if (!questManager) questManager = FindFirstObjectByType<QuestManager>();
        LoadAndApplyQuest();
    }

    public void LoadAndApplyQuest() {
        if (questManager == null) { Debug.LogError("QuestManager not found!"); return; }
        TextAsset file = Resources.Load<TextAsset>("Quests/"+questJsonFile);
        if (!file) { Debug.LogError("Could not load quest JSON: " + questJsonFile); return; }

        QuestData[] questsToLoad;
        var listData = JsonUtility.FromJson<QuestListData>(file.text);
        if (listData != null && listData.quests != null && listData.quests.Length > 0) {
            questsToLoad = listData.quests;
        } else {
            var singleData = JsonUtility.FromJson<QuestData>(file.text);
            if (singleData == null || singleData.objectives == null) { Debug.LogError("Failed to parse quest"); return; }
            questsToLoad = new QuestData[] { singleData };
        }

        var loadedList = new List<Quest>();
        for (int q = 0; q < questsToLoad.Length; q++) {
            QuestData qd = questsToLoad[q];
            if (qd == null) continue;
            bool hasObjectives = qd.objectives != null && qd.objectives.Length > 0;
            bool hasSingularTarget = !string.IsNullOrEmpty(qd.objectiveType) && !string.IsNullOrEmpty(qd.targetId);
            if (!hasObjectives && !hasSingularTarget) {
                Debug.LogWarning($"Quest '{qd.questName}' has no objectives and no objectiveType/targetId; skipping.");
                continue;
            }
            loadedList.Add(BuildQuestFromData(qd));
        }
        questManager.quests = loadedList.ToArray();
        questManager.currentQuestIndex = 0;
        questManager.UpdateQuestUI();
    }

    private Quest BuildQuestFromData(QuestData questData) {
        bool hasObjectives = questData.objectives != null && questData.objectives.Length > 0;
        Quest newQuest = new Quest {
            questName = questData.questName,
            description = questData.description,
            requiresAllObjectives = questData.requiresAllObjectives,
            autoAdvanceObjectives = questData.autoAdvanceObjectives,
            cutsceneOnStartName = questData.cutsceneOnStartName,
            cutsceneOnCompleteName = questData.cutsceneOnCompleteName,
            talkDialogueLines = questData.talkDialogueLines,
            objectives = hasObjectives ? new QuestObjective[questData.objectives.Length] : null,
        };
        if (hasObjectives) {
            for (int i = 0; i < questData.objectives.Length; i++) {
                var od = questData.objectives[i];
                ObjectiveType type;
                if (!System.Enum.TryParse(od.objectiveType, true, out type)) type = ObjectiveType.Custom;
                var obj = new QuestObjective {
                    objectiveName = od.objectiveName,
                    description = od.description,
                    objectiveType = type,
                    targetId = od.targetId,
                    requiredCount = od.requiredCount,
                    requireAllPlayers = od.requireAllPlayers
                };
                if (type == ObjectiveType.Collect && od.collectItemIds != null && od.collectItemIds.Length > 0) {
                    obj.collectItemIds = od.collectItemIds;
                    obj.collectQuantities = od.collectQuantities != null && od.collectQuantities.Length == od.collectItemIds.Length
                        ? od.collectQuantities
                        : new int[od.collectItemIds.Length];
                    obj.collectProgress = new int[od.collectItemIds.Length];
                    if (od.requiredCount <= 1) {
                        int sum = 0;
                        foreach (int q in obj.collectQuantities) sum += q;
                        obj.requiredCount = sum;
                    }
                }
                newQuest.objectives[i] = obj;
            }
            newQuest.currentObjectiveIndex = Mathf.Clamp(questData.startObjectiveIndex, 0, newQuest.objectives.Length - 1);
        } else {
            // Singular quest: use quest-level targetId/objectiveType (legacy support)
            newQuest.targetId = questData.targetId;
            newQuest.requiredCount = questData.requiredCount;
            if (!string.IsNullOrEmpty(questData.objectiveType) && System.Enum.TryParse(questData.objectiveType, true, out ObjectiveType qType))
                newQuest.objectiveType = qType;
        }
        return newQuest;
    }
}
