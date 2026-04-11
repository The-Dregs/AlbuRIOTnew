using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class InventoryUI : MonoBehaviour
{
    public GameObject inventoryPanel;
    [Header("fixed slots (grid)")]
    [Tooltip("Inventory slot UI boxes in reading order (row-major); must match Inventory.SLOT_COUNT.")] public ItemSlotUI[] slotUIs = new ItemSlotUI[Inventory.SLOT_COUNT];
    [Tooltip("If set, ItemSlotUI discovery is limited to this transform (recommended: your grid container).")]
    public Transform slotsGridRoot;
    [Header("legacy dynamic list (unused)")]
    [HideInInspector] public Transform itemListParent; // kept for backward compatibility, unused in fixed-six layout
    [HideInInspector] public GameObject itemSlotPrefab; // kept for backward compatibility, unused in fixed-six layout

    [Header("auto-wired references")]
    [SerializeField, HideInInspector] private Inventory playerInventory;
    [SerializeField, HideInInspector] private ThirdPersonController playerController;
    [SerializeField, HideInInspector] private PlayerCombat playerCombat;
    [SerializeField, HideInInspector] private ThirdPersonCameraOrbit cameraOrbit;
    [SerializeField, HideInInspector] private EquipmentManager equipmentManager;
    [SerializeField, HideInInspector] private PlayerStats playerStats;
    public TMPro.TextMeshProUGUI equippedItemText;
    public Image equippedItemIcon; // optional: shows the equipped item's icon in the large left square
    public UnityEngine.UI.Button unequipButton; // optional: button to unequip current item back to inventory
    [Header("Equipped Item Details")]
    public TMPro.TextMeshProUGUI equippedItemName; // optional: displays the equipped item's name
    public TMPro.TextMeshProUGUI equippedItemDescription; // optional: displays the equipped item's description
    [Header("Player stats (inventory open)")]
    [Tooltip("TMP lines: base value + green equipment bonus, e.g. HP: 100 +10")] public TMPro.TextMeshProUGUI statHpText;
    public TMPro.TextMeshProUGUI statStaminaText;
    public TMPro.TextMeshProUGUI statDamageText;
    public TMPro.TextMeshProUGUI statSpeedText;
    [Header("Item hover")]
    [Tooltip("Optional. Child panel under inventory root; reparented to hovered slot while visible.")]
    public InventoryItemTooltip itemTooltip;
    [Header("Equipped item tooltip")]
    [Tooltip("Optional. RectTransform for the large equipped preview; defaults to equippedItemIcon. Needs a Graphic with Raycast Target for hover (icon Image is enabled when an item is equipped).")]
    public RectTransform equippedItemHoverArea;
    private int _inputLockToken = 0;
    private CanvasGroup _panelCanvasGroup; // used when panel is the same object as this controller
    private string _lastStateSignature = ""; // snapshot of inventory + equipped to detect changes
    private bool _isOpen = false; // runtime state

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
        WireUnequipButton();
        WireEquippedTooltipHover();
        RefreshUI();
    }

    private void WireEquippedTooltipHover()
    {
        RectTransform rt = equippedItemHoverArea;
        if (rt == null && equippedItemIcon != null)
            rt = equippedItemIcon.rectTransform;
        if (rt == null) return;
        var h = rt.GetComponent<InventoryEquippedSlotHover>();
        if (h == null)
            h = rt.gameObject.AddComponent<InventoryEquippedSlotHover>();
        h.Bind(this);
    }

    private void WireUnequipButton()
    {
        if (unequipButton == null) return;
        unequipButton.onClick.RemoveListener(OnClickUnequip);
        unequipButton.onClick.AddListener(OnClickUnequip);
    }

    private void Update()
    {
        if (playerInventory == null || equipmentManager == null || playerStats == null)
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
            UpdateStatsTexts();
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
        WireUnequipButton();
        WireEquippedTooltipHover();
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
        HideItemTooltip();
    }

    private void AutoWireIfNeeded()
    {
        if (playerInventory == null)
            playerInventory = GetComponentInParent<Inventory>(true);
        if (equipmentManager == null)
            equipmentManager = GetComponentInParent<EquipmentManager>(true);
        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>(true);
        if (playerController == null)
            playerController = GetComponentInParent<ThirdPersonController>(true);
        if (playerCombat == null)
            playerCombat = GetComponentInParent<PlayerCombat>(true);
        if (cameraOrbit == null)
            cameraOrbit = FindFirstObjectByType<ThirdPersonCameraOrbit>();

        EnsurePanelReference();

        EnsureSlotUIsBound();
    }

    /// <summary>
    /// Resizes slotUIs to SLOT_COUNT, preserves existing inspector wiring, fills nulls from hierarchy in stable order.
    /// Does not replace non-null entries (avoids scrambled order / missing items after expanding the grid).
    /// </summary>
    private void EnsureSlotUIsBound()
    {
        int count = Inventory.SLOT_COUNT;
        if (slotUIs == null || slotUIs.Length != count)
        {
            var old = slotUIs;
            slotUIs = new ItemSlotUI[count];
            if (old != null)
            {
                int copy = Mathf.Min(old.Length, count);
                for (int i = 0; i < copy; i++)
                    slotUIs[i] = old[i];
            }
        }

        int assigned = 0;
        for (int i = 0; i < slotUIs.Length; i++)
            if (slotUIs[i] != null) assigned++;
        if (assigned >= count) return;

        Transform root = slotsGridRoot != null
            ? slotsGridRoot
            : ((inventoryPanel != null) ? inventoryPanel.transform : this.transform);
        var found = root.GetComponentsInChildren<ItemSlotUI>(true);
        if (found == null || found.Length == 0) return;

        System.Array.Sort(found, (a, b) =>
            string.CompareOrdinal(GetHierarchySortKey(a.transform, root), GetHierarchySortKey(b.transform, root)));

        var used = new HashSet<ItemSlotUI>();
        for (int i = 0; i < slotUIs.Length; i++)
            if (slotUIs[i] != null) used.Add(slotUIs[i]);

        int fi = 0;
        for (int i = 0; i < slotUIs.Length; i++)
        {
            if (slotUIs[i] != null) continue;
            while (fi < found.Length && used.Contains(found[fi]))
                fi++;
            if (fi >= found.Length) break;
            slotUIs[i] = found[fi];
            used.Add(found[fi]);
            fi++;
        }
    }

    private static string GetHierarchySortKey(Transform t, Transform stopAt)
    {
        var s = new System.Text.StringBuilder();
        while (t != null && t != stopAt)
        {
            s.Insert(0, $"{t.GetSiblingIndex():D4}/");
            t = t.parent;
        }
        return s.ToString();
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

        EnsurePanelReference();
        EnsureSlotUIsBound();

        // fixed grid UI binding (defensive against partial assignment)
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
                // Keep empty slot objects active so the grid stays visible and raycasts work.
                if (!ui.gameObject.activeSelf) ui.gameObject.SetActive(true);
            }
        }
        // if designer forgot to assign all slots, indices with null slotUIs are skipped

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
                    var c = equippedItemIcon.color;
                    c.a = equipmentManager.equippedItem.icon != null ? 1f : 0f;
                    equippedItemIcon.color = c;
                    // Raycast so InventoryEquippedSlotHover can show tooltips on the preview (Unequip stays on top via sibling order).
                    equippedItemIcon.raycastTarget = true;
                }
                else
                {
                    equippedItemIcon.sprite = null;
                    var c = equippedItemIcon.color;
                    c.a = 0f;
                    equippedItemIcon.color = c;
                    equippedItemIcon.enabled = true;
                    equippedItemIcon.raycastTarget = false;
                }
            }
            
            ItemData eqItem = equipmentManager.equippedItem;
            if (equippedItemText != null)
                equippedItemText.text = eqItem != null ? $"Equipped: {eqItem.itemName}" : "Equipped: None";
            
            if (equippedItemName != null)
            {
                if (eqItem != null)
                {
                    equippedItemName.text = eqItem.itemName;
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
                if (eqItem != null)
                {
                    equippedItemDescription.text = eqItem.description;
                    equippedItemDescription.gameObject.SetActive(true);
                }
                else
                {
                    equippedItemDescription.text = "";
                    equippedItemDescription.gameObject.SetActive(false);
                }
            }
            if (unequipButton != null)
            {
                unequipButton.gameObject.SetActive(eqItem != null);
                if (eqItem != null)
                {
                    unequipButton.interactable = true;
                    unequipButton.transform.SetAsLastSibling();
                }
            }
        }

        UpdateStatsTexts();

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
            HideItemTooltip();
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
            HideItemTooltip();
            RefreshUI();
        }
        return removed;
    }

    public void OnClickUnequip()
    {
        if (equipmentManager == null) return;
        // PhotonView is usually on the player root, not on EquipmentManager.
        var pv = equipmentManager.GetComponent<Photon.Pun.PhotonView>()
            ?? equipmentManager.GetComponentInParent<Photon.Pun.PhotonView>();
        if (pv != null && !pv.IsMine) return;
        equipmentManager.Unequip();
        RefreshUI();
    }
    
    public void ShowItemTooltip(ItemData item, RectTransform slotRect, InventoryTooltipPlacement placement = InventoryTooltipPlacement.AboveAnchor)
    {
        if (itemTooltip == null || item == null || slotRect == null) return;
        itemTooltip.Show(item, slotRect, placement);
    }

    public void HideItemTooltip()
    {
        if (itemTooltip == null) return;
        itemTooltip.Hide();
    }

    private void UpdateStatsTexts()
    {
        if (statHpText == null && statStaminaText == null && statDamageText == null && statSpeedText == null)
            return;
        AutoWireIfNeeded();
        if (playerStats == null) return;
        ItemData eq = equipmentManager != null ? equipmentManager.equippedItem : null;

        string GreenInt(int v)
        {
            if (v == 0) return "";
            return v > 0
                ? $" <color=#00CC00>+{v}</color>"
                : $" <color=#CC4444>{v}</color>";
        }
        string GreenSpeed(float v)
        {
            if (Mathf.Abs(v) < 0.0001f) return "";
            return v > 0f
                ? $" <color=#00CC00>+{v:0.##}</color>"
                : $" <color=#CC4444>{v:0.##}</color>";
        }

        if (statHpText != null)
        {
            int baseMax = playerStats.GetDisplayedBaseMaxHealth(eq);
            int bonus = eq != null ? eq.healthModifier : 0;
            statHpText.text = $"HP: {playerStats.currentHealth}/{baseMax}{GreenInt(bonus)}";
        }
        if (statStaminaText != null)
        {
            int baseMax = playerStats.GetDisplayedBaseMaxStamina(eq);
            int bonus = eq != null ? eq.staminaModifier : 0;
            statStaminaText.text = $"Stamina: {playerStats.currentStamina}/{baseMax}{GreenInt(bonus)}";
        }
        if (statDamageText != null)
        {
            int baseDmg = playerStats.GetDisplayedBaseDamage(eq);
            int bonus = eq != null ? eq.damageModifier : 0;
            statDamageText.text = $"Damage: {baseDmg}{GreenInt(bonus)}";
        }
        if (statSpeedText != null)
        {
            float baseWalk = playerStats.GetDisplayedBaseWalkSpeed(playerController, eq);
            float bonus = eq != null ? eq.speedModifier : 0f;
            statSpeedText.text = $"Speed: {baseWalk:0.#}{GreenSpeed(bonus)}";
        }
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
