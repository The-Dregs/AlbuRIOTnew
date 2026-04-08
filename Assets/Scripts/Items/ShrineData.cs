using UnityEngine;
using AlbuRIOT.Abilities;

[CreateAssetMenu(fileName = "New Shrine", menuName = "AlbuRIOT/Shrine Data")]
public class ShrineData : ScriptableObject
{
    [Header("Shrine Information")]
    public string shrineId;
    public string shrineName;
    [TextArea] public string description;
    
    [Header("Accepted Offerings")]
    public ItemData[] acceptedOfferings;
    
    [Header("Required Offerings")]
    public ItemData[] requiredOfferings;
    public int[] offeringQuantities;
    
    [Header("Rewards")]
    public ItemData[] rewardItems;
    public int[] rewardQuantities;
    public AbilityBase[] rewardAbilities;
    
    [Header("Visual")]
    public Sprite shrineIcon;
    public GameObject shrinePrefab;
    
    [Header("VFX")]
    public GameObject offeringVFX;
    public GameObject completionVFX;
    public AudioClip offeringSound;
    public AudioClip completionSound;
    
    [Header("Quest Integration")]
    public bool isQuestObjective = false;
    public string questId = "";
    
    [Header("Power Stealing")]
    public bool grantsPower = false;
    public string associatedEnemy = "";
    public float powerDuration = 30f;
}

