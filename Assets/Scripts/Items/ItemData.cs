using UnityEngine;

[CreateAssetMenu(fileName = "New Item", menuName = "AlbuRIOT/Item Data")]
public class ItemData : ScriptableObject
{
    [Header("Basic Information")]
    public string itemName;
    [TextArea] public string description;
    public Sprite icon;
    public ItemType itemType = ItemType.Consumable;

    /// <summary>Equipment and armor are single-slot, non-stackable gear.</summary>
    public bool IsEquipment => itemType == ItemType.Equipment || itemType == ItemType.Armor;
    
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
    [Tooltip("When true (after grip preview bake), rotation uses the quaternion below so Euler round-trip does not shift the model.")]
    [HideInInspector] public bool modelGripQuaternionAuthoritative;
    [HideInInspector] public Quaternion modelLocalGripQuaternion = Quaternion.identity;

    /// <summary>Base local rotation for equipped model (hand space), before EquipmentManager hold rotation.</summary>
    public Quaternion GetEquipmentModelLocalRotation()
    {
        if (!overrideTransform) return Quaternion.identity;
        if (modelGripQuaternionAuthoritative)
            return modelLocalGripQuaternion;
        return Quaternion.Euler(modelLocalEulerAngles);
    }

    /// <summary>Writes pose from grip preview bake; stores exact quaternion to avoid Euler drift on re-apply.</summary>
    public void ApplyBakedEquipmentGripPose(Vector3 localPosition, Quaternion localRotationBase, Vector3 localScale)
    {
        overrideTransform = true;
        modelLocalPosition = localPosition;
        modelLocalGripQuaternion = localRotationBase;
        // Keep Euler in sync with the baked quaternion so the inspector and OnValidate agree (avoids clearing quaternion authority).
        modelLocalEulerAngles = localRotationBase.eulerAngles;
        modelGripQuaternionAuthoritative = true;
        modelScale = localScale;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    /// <summary>Rich text lines for HP/Stamina/Damage/Speed/Stamina cost modifiers (TMP color tags).</summary>
    public string GetStatModifiersRichText()
    {
        var sb = new System.Text.StringBuilder(128);
        AppendModLine(sb, "HP", healthModifier);
        AppendModLine(sb, "Stamina", staminaModifier);
        AppendModLine(sb, "Damage", damageModifier);
        if (Mathf.Abs(speedModifier) > 0.0001f)
        {
            string c = speedModifier > 0f ? "#00CC00" : "#CC4444";
            sb.AppendLine($"Speed <color={c}>{FormatSigned(speedModifier)}</color>");
        }
        AppendModLine(sb, "Stamina cost", staminaCostModifier);

        if (itemType == ItemType.Consumable)
            AppendConsumableEffectLines(sb);

        if (sb.Length == 0) return "";
        if (sb[sb.Length - 1] == '\n') sb.Length -= 1;
        return sb.ToString();
    }

    private void AppendConsumableEffectLines(System.Text.StringBuilder sb)
    {
        if (healAmount > 0)
            sb.AppendLine($"On use: Restore HP <color=#00CC00>+{healAmount}</color>");
        if (staminaRestore > 0)
            sb.AppendLine($"On use: Restore Stamina <color=#00CC00>+{staminaRestore}</color>");
        if (effectDuration > 0.0001f)
            sb.AppendLine($"Effect duration: <color=#00CC00>{effectDuration:0.#}s</color>");
    }

    private static void AppendModLine(System.Text.StringBuilder sb, string label, int value)
    {
        if (value == 0) return;
        string color = value > 0 ? "#00CC00" : "#CC4444";
        sb.AppendLine($"{label} <color={color}>{FormatSigned(value)}</color>");
    }

    private static string FormatSigned(int v) => v > 0 ? $"+{v}" : v.ToString();
    private static string FormatSigned(float v) => v > 0f ? $"+{v:0.##}" : v.ToString("0.##");

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (IsEquipment)
            maxStack = 1;
        SyncEquipmentGripRotationFields();
    }

    private void SyncEquipmentGripRotationFields()
    {
        if (modelGripQuaternionAuthoritative)
        {
            var fromEuler = Quaternion.Euler(modelLocalEulerAngles);
            // 0.05f was too tight: float drift after bake cleared authority and overwrote the baked quaternion.
            if (Quaternion.Angle(fromEuler, modelLocalGripQuaternion) > 1f)
                modelGripQuaternionAuthoritative = false;
        }
        if (!modelGripQuaternionAuthoritative)
            modelLocalGripQuaternion = Quaternion.Euler(modelLocalEulerAngles);
    }
#endif
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