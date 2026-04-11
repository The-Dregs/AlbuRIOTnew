using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class ItemSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Image iconImage;
    public TextMeshProUGUI quantityText;
    public UnityEngine.UI.Button equipButton;
    [Tooltip("optional: background/image to show only when there is an item")] public GameObject filledVisual;
    private InventorySlot slot;
    private InventoryUI inventoryUI; // cached from parent for multiplayer safety
    private TextMeshProUGUI buttonText; // cached button text component

    void Awake()
    {
        // find the InventoryUI in parents so each player's UI talks to its own manager
        inventoryUI = GetComponentInParent<InventoryUI>(true);
        // cache button text component
        if (equipButton != null)
            buttonText = equipButton.GetComponentInChildren<TextMeshProUGUI>(true);
    }

    public void SetSlot(InventorySlot slot)
    {
        this.slot = slot;
        if (iconImage != null)
        {
            iconImage.sprite = slot.item != null ? slot.item.icon : null;
            bool hasIcon = slot.item != null && slot.item.icon != null;
            var c = iconImage.color;
            c.a = hasIcon ? 1f : 0f;
            iconImage.color = c;
            iconImage.enabled = true;
            // Avoid blocking clicks/hover on the slot when there is no icon to show.
            iconImage.raycastTarget = hasIcon;
        }
        
        // Handle quantity display based on item type
        if (quantityText != null)
        {
            if (slot.item.IsEquipment)
            {
                // Equipment is always one per slot; never show a stack count.
                quantityText.text = "";
            }
            else if (slot.item.itemType == ItemType.Consumable)
            {
                quantityText.text = $"{slot.quantity}x";
            }
            else
            {
                quantityText.text = slot.quantity > 1 ? ($"{slot.quantity}x") : "";
            }
        }
        
        // Handle button visibility and text based on item type
        if (equipButton != null)
        {
            bool showButton = slot != null && slot.item != null && 
                (slot.item.itemType == ItemType.Consumable || 
                 slot.item.itemType == ItemType.Equipment || 
                 slot.item.itemType == ItemType.Armor);
            equipButton.interactable = showButton;
            equipButton.gameObject.SetActive(showButton);
            
            // Update button text based on item type
            if (buttonText != null && showButton)
            {
                if (slot.item.itemType == ItemType.Consumable)
                    buttonText.text = "Use";
                else
                    buttonText.text = "Equip";
            }
        }
        
        if (filledVisual != null) filledVisual.SetActive(true);
    }

    public void Clear()
    {
        slot = null;
        if (iconImage != null)
        {
            iconImage.sprite = null;
            var c = iconImage.color;
            c.a = 0f;
            iconImage.color = c;
            iconImage.enabled = true;
            iconImage.raycastTarget = false;
        }
        if (quantityText != null) quantityText.text = "";
        if (equipButton != null)
        {
            equipButton.interactable = false;
            equipButton.gameObject.SetActive(false);
        }
        if (filledVisual != null) filledVisual.SetActive(false);
    }

    public void OnEquipButton()
    {
        if (inventoryUI == null) inventoryUI = GetComponentInParent<InventoryUI>(true);
        if (inventoryUI == null) return;
        if (slot == null || slot.item == null) return;
        
        // Handle consumable items differently from equipable items
        if (slot.item.itemType == ItemType.Consumable)
        {
            inventoryUI.TryUseItem(slot);
        }
        else
        {
            inventoryUI.TryEquipFromSlot(slot);
        }
    }
    
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (inventoryUI == null) inventoryUI = GetComponentInParent<InventoryUI>(true);
        if (inventoryUI == null) return;
        if (slot != null && slot.item != null)
        {
            var rt = transform as RectTransform;
            if (rt != null)
                inventoryUI.ShowItemTooltip(slot.item, rt);
        }
    }
    
    public void OnPointerExit(PointerEventData eventData)
    {
        if (inventoryUI == null) inventoryUI = GetComponentInParent<InventoryUI>(true);
        if (inventoryUI == null) return;
        inventoryUI.HideItemTooltip();
    }
}
