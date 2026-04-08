using UnityEngine;

[CreateAssetMenu(fileName = "New Item", menuName = "AlbuRIOT/Item Data")]
public class ItemData : ScriptableObject
{
    [Header("Basic Information")]
    public string itemName;
    [TextArea] public string description;
    public Sprite icon;
    public ItemType itemType = ItemType.Consumable;
    
    [Header("Stacking")]
    public int maxStack = 1;
    
    [Header("Equipment Stats")]
    public int healthModifier = 0;
    public int staminaModifier = 0;
    public int damageModifier = 0;
    public float speedModifier = 0f;
    public int staminaCostModifier = 0;
    
    [Header("Consumable Effects")]
    public int healAmount = 0;
    public int staminaRestore = 0;
    public float effectDuration = 0f;
    
    [Header("Quest Integration")]
    public bool isQuestItem = false;
    public string questId = "";
    
    [Header("Shrine Integration")]
    public bool canBeOffered = false;
    public int offeringValue = 1;
    
    [Header("Power Stealing")]
    public bool grantsPower = false;
    public string associatedEnemy = "";
    
    [Header("Advanced")]
    public bool uniqueInstance = false; // If true, this item never merges with others, always uses a unique inventory slot
    
    [Header("Audio")]
    public AudioClip pickupSound;
    public AudioClip useSound;
    
    [Header("Visual Effects")]
    public GameObject pickupEffect;
    public GameObject useEffect;

    [Header("Equipment Model (optional)")]
    [Tooltip("Prefab to attach to the player's hand when this item is equipped. If null, no model is spawned.")]
    public GameObject modelPrefab;
    [Tooltip("If true, overrides the prefab's local position/rotation using the fields below.")]
    public bool overrideTransform = false;
    public Vector3 modelLocalPosition = Vector3.zero;
    public Vector3 modelLocalEulerAngles = Vector3.zero;
    public Vector3 modelScale = Vector3.one;
}

public enum ItemType
{
    Consumable,
    Equipment,
    Quest,
    Offering,
    Power,
    Misc,
    Armor,
    Unique,
    Items
}