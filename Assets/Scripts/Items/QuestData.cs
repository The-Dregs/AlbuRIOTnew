using UnityEngine;
using AlbuRIOT.Abilities;

[CreateAssetMenu(fileName = "New Quest", menuName = "AlbuRIOT/Quest Data")]
public class QuestData : ScriptableObject
{
    [Header("Quest Information")]
    public string questName;
    [TextArea] public string description;
    public int questID;
    
    [Header("Objectives")]
    public QuestObjective[] objectives;
    
    [Header("Rewards")]
    public ItemData[] rewardItems;
    public int[] rewardQuantities;
    public AbilityBase[] rewardAbilities;
    
    [Header("Quest Flow")]
    public bool requiresAllObjectives = true;
    public bool autoAdvanceObjectives = true;
    
    [Header("Prerequisites")]
    public QuestData[] prerequisiteQuests;
    public int requiredLevel = 1;
    
    [Header("Quest Type")]
    public QuestType questType = QuestType.Main;
    public QuestPriority priority = QuestPriority.Normal;
}

public enum QuestType
{
    Main,
    Side,
    Daily,
    Event,
    Tutorial
}

public enum QuestPriority
{
    Low,
    Normal,
    High,
    Critical
}

