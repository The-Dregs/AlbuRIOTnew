using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class InventoryUI : MonoBehaviour
{
    public GameObject inventoryPanel;
    [Header("fixed slots (3x2)")]
    [Tooltip("exactly 6 slot UI boxes in reading order (row-major): top-left to bottom-right of the right grid")] public ItemSlotUI[] slotUIs = new ItemSlotUI[Inventory.SLOT_COUNT];
    [Header("legacy dynamic list (unused)")]
    [HideInInspector] public Transform itemListParent; // kept for backward compatibility, unused in fixed-six layout
    [HideInInspector] public GameObject itemSlotPrefab; // kept for backward compatibility, unused in fixed-six layout

    [Header("auto-wired references")]
    [SerializeField, HideInInspector] private Inventory playerInventory;
    [SerializeField, HideInInspector] private ThirdPersonController playerController;
    [SerializeField, HideInInspector] private PlayerCombat playerCombat;
    [SerializeField, HideInInspector] private ThirdPersonCameraOrbit cameraOrbit;
    [SerializeField, HideInInspector] private EquipmentManager equipmentManager;
    public TMPro.TextMeshProUGUI equippedItemText;
    public Image equippedItemIcon; // optional: shows the equipped item's icon in the large left square
    public UnityEngine.UI.Button unequipButton; // optional: button to unequip current item back to inventory
    [Header("Equipped Item Details")]
    public TMPro.TextMeshProUGUI equippedItemName; // optional: displays the equipped item's name
    public TMPro.TextMeshProUGUI equippedItemDescription; // optional: displays the equipped item's description
    private int _inputLockToken = 0;
    private CanvasGroup _panelCanvasGroup; // used when panel is the same object as this controller
    private string _lastStateSignature = ""; // snapshot of inventory + equipped to detect changes
    private bool _isOpen = false; // runtime state
    private ItemData _hoveredItem = null; // currently hovered item for description display

    // read-only accessors for other UI pieces
    public Inventory PlayerInventory => playerInventory;
    public EquipmentManager EquipmentManager => equipmentManager;

    private void Start()
    {
        AutoWireIfNeeded();
        // always keep this controller enabled so Update() runs. If the assigned
        // inventoryPanel happens to be this same GameObject, hide via CanvasGroup
        // instead of deactivating the object.
        EnsurePanelReference();
        if (playerInventory != null)
        {
            playerInventory.OnInventoryChanged -= RefreshUI;
            playerInventory.OnInventoryChanged += RefreshUI;
            Debug.Log($"[InventoryUI] Subscribed to inventory changes on {playerInventory.gameObject.name}");
        }
        else
        {
            Debug.LogWarning("[InventoryUI] playerInventory is null in Start() - UI may not update!");
        }
        // draw once and set state signature
        _lastStateSignature = BuildStateSignature();
        RefreshUI();
    }

    private void Update()
    {
        if (playerInventory == null || equipmentManager == null)
            AutoWireIfNeeded();
        var photonView = playerInventory != null ? playerInventory.GetComponent<Photon.Pun.PhotonView>() : null;
        if (photonView != null && !photonView.IsMine) return;
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (!IsPanelVisible()) OpenInventory(); else CloseInventory();
        }

        // guard against missed events: refresh if content changed
        MaybeRefreshOnContentChange();

        // if something else disabled the panel while open (e.g., another script),
        // re-assert visibility every frame during the open state
        if (_isOpen)
        {
            EnsurePanelReference();
            if (inventoryPanel != null && !inventoryPanel.activeSelf)
            {
                Debug.LogWarning("[inventory] panel was disabled externally while open; re-enabling.");
                inventoryPanel.SetActive(true);
            }
        }
    }

    public void OpenInventory()
    {
        AutoWireIfNeeded();
        var ui = LocalUIManager.Ensure();
        // enforce strict exclusivity: do not open if any other UI is open
        if (ui.IsAnyOpen && !ui.IsOwner("Inventory")) {
            Debug.LogWarning("[inventory] cannot open: another UI is already open");
            return;
        }
        if (!ui.TryOpen("Inventory")) {
            Debug.LogWarning($"[inventory] TryOpen blocked by '{ui.CurrentOwner}', forcing close and opening inventory.");
            ui.ForceClose();
            ui.TryOpen("Inventory");
        }
        EnsurePanelReference();
        SetPanelVisible(true);
        Debug.Log("[inventory] open inventory ui");
        _isOpen = true;
        RefreshUI();
        if (_inputLockToken == 0)
            _inputLockToken = LocalInputLocker.Ensure().Acquire("Inventory", lockMovement:false, lockCombat:true, lockCamera:true, cursorUnlock:true);
    }

    public void CloseInventory()
    {
        SetPanelVisible(false);
        Debug.Log("[inventory] close inventory ui");
        _isOpen = false;
        var ui = LocalUIManager.Ensure();
        if (ui.IsOwner("Inventory")) ui.Close("Inventory"); else ui.ForceClose();
        if (_inputLockToken != 0)
        {
            LocalInputLocker.Ensure().Release(_inputLockToken);
            _inputLockToken = 0;
        }
        LocalInputLocker.Ensure().ForceGameplayCursor();
    }

    private void AutoWireIfNeeded()
    {
        if (playerInventory == null)
            playerInventory = GetComponentInParent<Inventory>(true);
        if (equipmentManager == null)
            equipmentManager = GetComponentInParent<EquipmentManager>(true);
        if (playerController == null)
            playerController = GetComponentInParent<ThirdPersonController>(true);
        if (playerCombat == null)
            playerCombat = GetComponentInParent<PlayerCombat>(true);
        if (cameraOrbit == null)
            cameraOrbit = FindFirstObjectByType<ThirdPersonCameraOrbit>();

        EnsurePanelReference();

        // auto-populate slotUIs from children if not fully assigned
        int assigned = 0;
        if (slotUIs != null)
        {
            for (int i = 0; i < slotUIs.Length; i++) if (slotUIs[i] != null) assigned++;
        }
        if (slotUIs == null || slotUIs.Length < Inventory.SLOT_COUNT || assigned < Inventory.SLOT_COUNT)
        {
            Transform root = (inventoryPanel != null) ? inventoryPanel.transform : this.transform;
            var found = root.GetComponentsInChildren<ItemSlotUI>(true);
            if (found != null && found.Length > 0)
            {
                var newArr = new ItemSlotUI[Inventory.SLOT_COUNT];
                int take = Mathf.Min(Inventory.SLOT_COUNT, found.Length);
                for (int i = 0; i < take; i++) newArr[i] = found[i];
                slotUIs = newArr;
            }
        }
    }

    // ensure we know which panel to show/hide. if designers assigned the controller
    // object itself, fall back to a CanvasGroup so we can hide without disabling Update().
    private void EnsurePanelReference()
    {
        if (inventoryPanel == null)
        {
            // try to find a child called "InventoryPanel" first
            var t = transform.Find("InventoryPanel");
            if (t != null) inventoryPanel = t.gameObject;
            else
            {
                // else, pick first direct child under this object
                if (transform.childCount > 0) inventoryPanel = transform.GetChild(0).gameObject;
            }
        }

        // if panel is this same GameObject, rely on CanvasGroup toggling
        if (inventoryPanel == this.gameObject)
        {
            // prefer the dedicated child named "InventoryPanel" if it exists
            var child = transform.Find("InventoryPanel");
            if (child != null)
            {
                Debug.Log("[inventory] inventoryPanel pointed to controller; switching to child 'InventoryPanel'.");
                inventoryPanel = child.gameObject;
                _panelCanvasGroup = null; // no longer needed for child panel approach
                return;
            }
            if (_panelCanvasGroup == null)
            {
                _panelCanvasGroup = gameObject.GetComponent<CanvasGroup>();
                if (_panelCanvasGroup == null) _panelCanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }
    }

    private bool IsPanelVisible()
    {
        if (inventoryPanel == null) return false;
        if (inventoryPanel == this.gameObject)
        {
            if (_panelCanvasGroup != null) return _panelCanvasGroup.alpha > 0.5f && _panelCanvasGroup.interactable;
            return gameObject.activeSelf;
        }
        return inventoryPanel.activeSelf;
    }

    private void SetPanelVisible(bool visible)
    {
        if (inventoryPanel == null) return;
        if (inventoryPanel == this.gameObject)
        {
            if (_panelCanvasGroup == null)
            {
                _panelCanvasGroup = gameObject.GetComponent<CanvasGroup>();
                if (_panelCanvasGroup == null) _panelCanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
            _panelCanvasGroup.alpha = visible ? 1f : 0f;
            _panelCanvasGroup.interactable = visible;
            _panelCanvasGroup.blocksRaycasts = visible;
            // also toggle a child named "InventoryPanel" for designers who hid that child
            var child = transform.Find("InventoryPanel");
            if (child != null && child.gameObject.activeSelf != visible)
                child.gameObject.SetActive(visible);
        }
        else
        {
            if (inventoryPanel.activeSelf != visible) inventoryPanel.SetActive(visible);
        }
    }

    void OnDestroy()
    {
        if (playerInventory != null)
            playerInventory.OnInventoryChanged -= RefreshUI;
    }

    public void RefreshUI()
    {
        if (playerInventory == null)
        {
            Debug.LogWarning("[InventoryUI] RefreshUI called but playerInventory is null!");
            return;
        }

        // fixed six-slot UI binding (defensive against partial assignment)
        int uiCount = (slotUIs != null) ? slotUIs.Length : 0;
        int loop = Mathf.Min(Inventory.SLOT_COUNT, uiCount);
        for (int i = 0; i < loop; i++)
        {
            var ui = slotUIs[i];
            if (ui == null) continue;
            var slot = playerInventory.GetSlot(i);
            // Always render the actual slot contents regardless of equipped item type.
            // This avoids hiding other stacks/instances that share the same ItemData.
            if (slot != null && slot.item != null)
            {
                ui.SetSlot(slot);
                if (!ui.gameObject.activeSelf) ui.gameObject.SetActive(true);
            }
            else
            {
                ui.Clear();
                if (ui.gameObject.activeSelf) ui.gameObject.SetActive(false);
            }
        }
        // if designer forgot to assign all six, no crash; optional warning once

        // equipped text and icon
        if (equipmentManager != null)
        {
            // Icon always shows equipped item (not hovered item)
            if (equippedItemIcon != null)
            {
                if (equipmentManager.equippedItem != null)
                {
                    equippedItemIcon.enabled = true;
                    equippedItemIcon.sprite = equipmentManager.equippedItem.icon;
                }
                else
                {
                    equippedItemIcon.sprite = null;
                    equippedItemIcon.enabled = false; // hide white box
                }
            }
            
            // Text fields show hovered item if hovering, otherwise show equipped item
            ItemData displayItem = _hoveredItem != null ? _hoveredItem : equipmentManager.equippedItem;
            
            if (equippedItemText != null)
                equippedItemText.text = equipmentManager.equippedItem != null ? $"Equipped: {equipmentManager.equippedItem.itemName}" : "Equipped: None";
            
            if (equippedItemName != null)
            {
                if (displayItem != null)
                {
                    equippedItemName.text = displayItem.itemName;
                    equippedItemName.gameObject.SetActive(true);
                }
                else
                {
                    equippedItemName.text = "";
                    equippedItemName.gameObject.SetActive(false);
                }
            }
            if (equippedItemDescription != null)
            {
                if (displayItem != null)
                {
                    equippedItemDescription.text = displayItem.description;
                    equippedItemDescription.gameObject.SetActive(true);
                }
                else
                {
                    equippedItemDescription.text = "";
                    equippedItemDescription.gameObject.SetActive(false);
                }
            }
            if (unequipButton != null)
                unequipButton.gameObject.SetActive(equipmentManager.equippedItem != null && _hoveredItem == null);
        }

        // update snapshot after drawing
        _lastStateSignature = BuildStateSignature();
    }

    // Equip handler: delegate to centralized EquipmentManager API
    public bool TryEquipFromSlot(InventorySlot slot)
    {
        if (slot == null || slot.item == null) return false;
        if (playerInventory == null || equipmentManager == null) return false;
        // Only Equipment and Armor can be equipped
        if (slot.item.itemType != ItemType.Equipment && slot.item.itemType != ItemType.Armor)
            return false;
        var pv = playerInventory != null ? playerInventory.GetComponent<Photon.Pun.PhotonView>() : null;
        if (pv != null && !pv.IsMine) return false;
        int slotIndex = -1;
        if (playerInventory != null)
        {
            for (int i = 0; i < Inventory.SLOT_COUNT; i++)
            {
                var s = playerInventory.GetSlot(i);
                if (s == slot)
                {
                    slotIndex = i;
                    break;
                }
            }
        }
        Debug.Log($"[inventory] Equip SlotIndex={slotIndex}, Item={(slot.item != null ? slot.item.itemName : "null")}");
        bool ok = equipmentManager.TryEquipFromInventorySlot(playerInventory, slot);
        if (ok)
        {
            // Clear stale hover state so Unequip button appears immediately after equipping.
            _hoveredItem = null;
            RefreshUI();
        }
        return ok;
    }

    public bool TryUseItem(InventorySlot slot)
    {
        if (slot == null || slot.item == null) return false;
        if (playerInventory == null) return false;
        var pv = playerInventory != null ? playerInventory.GetComponent<Photon.Pun.PhotonView>() : null;
        if (pv != null && !pv.IsMine) return false;
        
        // Apply consumable effects
        var stats = GetComponentInParent<PlayerStats>();
        if (stats != null)
        {
            if (slot.item.healAmount > 0)
            {
                stats.Heal(slot.item.healAmount);
                Debug.Log($"[inventory] Used {slot.item.itemName} - healed {slot.item.healAmount}");
            }
            if (slot.item.staminaRestore > 0)
            {
                stats.RestoreStamina(slot.item.staminaRestore);
                Debug.Log($"[inventory] Used {slot.item.itemName} - restored {slot.item.staminaRestore} stamina");
            }
        }
        
        // Play sound
        var audioSource = GetComponent<AudioSource>();
        if (audioSource != null && slot.item.useSound != null)
        {
            audioSource.PlayOneShot(slot.item.useSound);
        }
        
        // Play effect
        if (slot.item.useEffect != null)
        {
            Instantiate(slot.item.useEffect, transform.position, Quaternion.identity);
        }
        
        // Remove one from inventory
        bool removed = playerInventory.RemoveItem(slot.item, 1);
        if (removed)
        {
            // Consumables can destroy/move slot contents under the pointer; clear stale hover.
            _hoveredItem = null;
            RefreshUI();
        }
        return removed;
    }

    public void OnClickUnequip()
    {
        if (equipmentManager == null) return;
        var pv = equipmentManager.GetComponent<Photon.Pun.PhotonView>();
        if (pv != null && !pv.IsMine) return;
        equipmentManager.Unequip();
        RefreshUI();
    }
    
    public void ShowHoveredItem(ItemData item)
    {
        _hoveredItem = item;
        RefreshUI();
    }
    
    public void ClearHoveredItem()
    {
        _hoveredItem = null;
        RefreshUI();
    }

    // change detection helpers -------------------------------------------------
    private void MaybeRefreshOnContentChange()
    {
        string sig = BuildStateSignature();
        if (sig != _lastStateSignature)
        {
            RefreshUI();
        }
    }

    private string BuildStateSignature()
    {
        var sb = new System.Text.StringBuilder(64);
        if (playerInventory != null)
        {
            for (int i = 0; i < Inventory.SLOT_COUNT; i++)
            {
                var s = playerInventory.GetSlot(i);
                if (s != null && s.item != null)
                {
                    sb.Append(s.item.itemName).Append('#').Append(s.quantity).Append('|');
                }
                else sb.Append("._.|");
            }
        }
        sb.Append('|');
        if (equipmentManager != null && equipmentManager.equippedItem != null)
            sb.Append("E:").Append(equipmentManager.equippedItem.itemName);
        else
            sb.Append("E:_");
        return sb.ToString();
    }
}
