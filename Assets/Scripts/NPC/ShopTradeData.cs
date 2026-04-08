using UnityEngine;

[CreateAssetMenu(fileName = "New Shop Trade", menuName = "AlbuRIOT/Shop Trade Data")]
public class ShopTradeData : ScriptableObject
{
    [Header("Trade Information")]
    public string tradeName;
    [TextArea] public string description;
    
    [Header("Required Items")]
    public ItemData[] requiredItems;
    public int[] requiredQuantities; // Must match requiredItems array length
    
    [Header("Reward Item")]
    public ItemData rewardItem;
    public int rewardQuantity = 1;
    
    [Header("Trade Limits")]
    public int maxUses = -1; // -1 for unlimited
    [Tooltip("How many times this trade has been used (runtime only)")] 
    public int currentUses = 0;
    
    [Header("Unlock Conditions")]
    public bool requiresUnlock = false;
    public string[] requiredShrineIds; // Shrines that must be cleansed to unlock this trade
    public string[] requiredQuestIds; // Quests that must be completed to unlock this trade
    [Tooltip("Runtime: whether this trade is unlocked")] 
    public bool isUnlocked = false;
    
    public bool CanTrade => (maxUses < 0 || currentUses < maxUses) && (!requiresUnlock || isUnlocked);
    
    public void ResetUses()
    {
        currentUses = 0;
    }
    
    public void RecordUse()
    {
        if (maxUses > 0)
        {
            currentUses++;
        }
    }
    
#if UNITY_EDITOR
    void OnValidate()
    {
        if (requiredItems != null && requiredQuantities != null && requiredItems.Length != requiredQuantities.Length)
        {
            Debug.LogWarning($"[ShopTradeData] '{tradeName}': requiredItems.Length ({requiredItems.Length}) != requiredQuantities.Length ({requiredQuantities.Length})");
        }
    }
#endif
}

