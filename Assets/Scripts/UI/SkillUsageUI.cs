using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Displays usage count for skills in the skill slot UI.
/// Shows remaining usages and updates when skills are used.
/// </summary>
public class SkillUsageUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Text component to display usage count (e.g., '2/3' or '1')")]
    public TextMeshProUGUI usageText;
    [Tooltip("Optional: Image overlay that shows when skill is consumed")]
    public Image consumedOverlay;
    
    [Header("Settings")]
    [Tooltip("Format for usage display. {0} = current, {1} = max")]
    public string usageFormat = "{0}/{1}";
    [Tooltip("Text to show when skill is consumed")]
    public string consumedText = "CONSUMED";
    [Tooltip("Color for consumed state")]
    public Color consumedColor = Color.red;
    
    private PlayerSkillSlots skillSlots;
    private int slotIndex = -1;
    private Color originalTextColor;
    
    void Start()
    {
        skillSlots = GetComponentInParent<PlayerSkillSlots>();
        if (skillSlots == null)
            skillSlots = FindFirstObjectByType<PlayerSkillSlots>();
        
        if (usageText != null)
            originalTextColor = usageText.color;
        
        // Try to determine slot index from parent hierarchy
        DetermineSlotIndex();
    }
    
    void Update()
    {
        if (skillSlots == null || slotIndex < 0) return;
        
        UpdateUsageDisplay();
    }
    
    private void DetermineSlotIndex()
    {
        // Try to find slot index from parent name or sibling index
        Transform parent = transform.parent;
        if (parent != null)
        {
            string parentName = parent.name.ToLower();
            if (parentName.Contains("slot1") || parentName.Contains("skill1"))
                slotIndex = 0;
            else if (parentName.Contains("slot2") || parentName.Contains("skill2"))
                slotIndex = 1;
            else if (parentName.Contains("slot3") || parentName.Contains("skill3"))
                slotIndex = 2;
            else
            {
                // Try sibling index
                int siblingIndex = parent.GetSiblingIndex();
                if (siblingIndex >= 0 && siblingIndex < 3)
                    slotIndex = siblingIndex;
            }
        }
    }
    
    public void SetSlotIndex(int index)
    {
        slotIndex = index;
    }
    
    private void UpdateUsageDisplay()
    {
        if (usageText == null) return;
        
        int remaining = skillSlots.GetRemainingUsages(slotIndex);
        int max = skillSlots.GetMaxUsages(slotIndex);
        
        if (max <= 0)
        {
            // Unlimited uses or not initialized
            usageText.text = "";
            usageText.gameObject.SetActive(false);
            if (consumedOverlay != null)
                consumedOverlay.gameObject.SetActive(false);
            return;
        }
        
        if (remaining <= 0)
        {
            // Skill consumed
            usageText.text = consumedText;
            usageText.color = consumedColor;
            usageText.gameObject.SetActive(true);
            if (consumedOverlay != null)
                consumedOverlay.gameObject.SetActive(true);
        }
        else
        {
            // Show usage count
            usageText.text = string.Format(usageFormat, remaining, max);
            usageText.color = originalTextColor;
            usageText.gameObject.SetActive(true);
            if (consumedOverlay != null)
                consumedOverlay.gameObject.SetActive(false);
        }
    }
}

