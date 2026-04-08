using Photon.Pun;
using System.Collections.Generic;
using UnityEngine;
using System;

[System.Serializable]
public class InventorySlot
{
    public ItemData item;
    public int quantity;

    public InventorySlot(ItemData item, int quantity)
    {
        this.item = item;
        this.quantity = quantity;
    }
    
    public bool IsEmpty => item == null || quantity <= 0;
}

public class Inventory : MonoBehaviourPun, IPunObservable
{
    [Header("Inventory Configuration")]
    public const int SLOT_COUNT = 12;
    [SerializeField] private InventorySlot[] slots = new InventorySlot[SLOT_COUNT];
    
    [Header("Events")]
    public System.Action OnInventoryChanged;
    public System.Action<ItemData, int> OnItemAdded;
    public System.Action<ItemData, int> OnItemRemoved;
    public System.Action<int> OnSlotChanged;
    
    [Header("Network Sync")]
    public bool syncWithNetwork = true;
    
    public int SlotCount => SLOT_COUNT;

    // Runtime-only cache used to preserve the local player's inventory across scene loads.
    // Data is serialized into item names + quantities so it can be safely restored in the next scene.
    [Serializable]
    private struct CachedSlot
    {
        public string itemName;
        public int quantity;
    }

    private static CachedSlot[] cachedSlots;
    private static bool hasCachedSlots;
    private static int cachedOwnerActorNumber = -1;
    private bool _restoreScheduled;

    public static bool HasCachedInventory => hasCachedSlots && cachedSlots != null;
    
    /// <summary>
    /// Finds the local player's inventory. Safe for multiplayer (returns Inventory belonging to local PhotonView).
    /// Returns null if no local player found (offline mode fallback) or in single-player.
    /// </summary>
    public static Inventory FindLocalInventory()
    {
        var t = PlayerRegistry.GetLocalPlayerTransform();
        if (t != null)
        {
            var inv = t.GetComponent<Inventory>();
            if (inv != null) return inv;
        }

        // Fallback: try to find a likely local-player inventory, not arbitrary scene inventory.
        var allInventories = UnityEngine.Object.FindObjectsByType<Inventory>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < allInventories.Length; i++)
        {
            var inv = allInventories[i];
            if (inv != null && inv.IsLocalPlayerOwnedInventory())
                return inv;
        }

        return null;
    }
    
    public InventorySlot GetSlot(int index)
    {
        if (slots == null) return null;
        int len = slots.Length;
        if (index < 0 || index >= len) return null;
        return slots[index];
    }

    public int FindFirstEmptySlot()
    {
        EnsureSize();
        int len = (slots != null) ? slots.Length : 0;
        int limit = Mathf.Min(SLOT_COUNT, len);
        for (int i = 0; i < limit; i++)
        {
            var s = slots[i];
            if (s == null || s.IsEmpty) return i;
        }
        return -1;
    }
    
    public int FindItemSlot(ItemData item)
    {
        if (item == null) return -1;
        EnsureSize();
        
        int len = (slots != null) ? slots.Length : 0;
        int limit = Mathf.Min(SLOT_COUNT, len);
        for (int i = 0; i < limit; i++)
        {
            var s = slots[i];
            if (s != null && s.item == item && s.quantity < item.maxStack) return i;
        }
        return -1;
    }

    void Awake()
    {
        // photonView is provided by MonoBehaviourPun
        EnsureSize();

        // If this is the local owner's inventory and we previously cached data for a scene transition,
        // restore it now so the player keeps their items after loading a new map.
        if (hasCachedSlots && cachedSlots != null)
        {
            bool isLocalOwner = IsLocalPlayerOwnedInventory();
            if (isLocalOwner)
            {
                RestoreFromCache();
            }
            else if (!_restoreScheduled)
            {
                _restoreScheduled = true;
                StartCoroutine(Co_TryRestoreFromCache());
            }
        }
    }

    private System.Collections.IEnumerator Co_TryRestoreFromCache()
    {
        float elapsed = 0f;
        const float timeout = 5f;
        while (elapsed < timeout && hasCachedSlots)
        {
            if (IsLocalPlayerOwnedInventory())
            {
                RestoreFromCache();
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        _restoreScheduled = false;
    }

    public void TryRestoreCachedInventoryIfLocalOwner()
    {
        if (!HasCachedInventory) return;
        if (!IsLocalPlayerOwnedInventory()) return;
        RestoreFromCache();
    }

    private bool IsLocalPlayerOwnedInventory()
    {
        var pv = GetComponent<PhotonView>() ?? GetComponentInParent<PhotonView>();
        if (pv != null)
        {
            return !syncWithNetwork || !PhotonNetwork.IsConnected || pv.IsMine;
        }

        // Offline/local fallback: only treat inventories attached to player roots as valid restore targets.
        return GetComponentInParent<PlayerStats>() != null || GetComponentInParent<ThirdPersonController>() != null;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        EnsureSize();
    }
#endif

    private void EnsureSize()
    {
        if (slots == null || slots.Length != SLOT_COUNT)
        {
            var old = slots;
            slots = new InventorySlot[SLOT_COUNT];
            if (old != null)
            {
                int copy = Mathf.Min(old.Length, SLOT_COUNT);
                for (int i = 0; i < copy; i++) slots[i] = old[i];
            }
        }
    }

    /// <summary>
    /// Caches the local player's inventory into a static buffer so it can be restored after a scene load.
    /// Safe to call multiple times; subsequent calls overwrite the previous cache.
    /// </summary>
    public static void CacheLocalInventory()
    {
        Inventory localInv = FindLocalInventory();
        if (localInv == null) return;

        localInv.EnsureSize();
        cachedSlots = new CachedSlot[SLOT_COUNT];

        PhotonView pv = localInv.GetComponent<PhotonView>();
        cachedOwnerActorNumber = (pv != null && pv.Owner != null) ? pv.Owner.ActorNumber : -1;

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            var slot = localInv.slots != null && i < localInv.slots.Length ? localInv.slots[i] : null;
            if (slot != null && slot.item != null && slot.quantity > 0)
            {
                cachedSlots[i] = new CachedSlot
                {
                    itemName = slot.item.itemName,
                    quantity = slot.quantity
                };
            }
            else
            {
                cachedSlots[i] = new CachedSlot
                {
                    itemName = string.Empty,
                    quantity = 0
                };
            }
        }

        hasCachedSlots = true;
        Debug.Log("[Inventory] Cached local inventory for scene transition.");
    }

    private void RestoreFromCache()
    {
        if (!hasCachedSlots || cachedSlots == null || cachedSlots.Length != SLOT_COUNT)
            return;

        EnsureSize();

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            string itemName = cachedSlots[i].itemName;
            int quantity = cachedSlots[i].quantity;

            if (!string.IsNullOrEmpty(itemName) && quantity > 0)
            {
                ItemData item = GetItemDataByName(itemName);
                if (item != null)
                {
                    slots[i] = new InventorySlot(item, quantity);
                }
                else
                {
                    slots[i] = null;
                }
            }
            else
            {
                slots[i] = null;
            }
        }

        hasCachedSlots = false;
        cachedOwnerActorNumber = -1;
        OnInventoryChanged?.Invoke();
        Debug.Log("[Inventory] Restored cached inventory after scene transition.");
    }

    public bool AddItem(ItemData item, int quantity = 1, bool silent = false)
    {
        if (photonView != null && !photonView.IsMine && syncWithNetwork) return false;
        if (item == null || quantity <= 0) return false;
        EnsureSize();

        int remainingQuantity = quantity;
        bool isStackable = item.maxStack > 1;
        bool isUnique = item.uniqueInstance || item.itemType == ItemType.Unique;

        if (isUnique)
        {
            // Unique: always insert individual instances into empty slots
            for (int addCount = 0; addCount < quantity; addCount++)
            {
                bool placed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null || slots[i].IsEmpty)
                    {
                        slots[i] = new InventorySlot(item, 1);
                        OnSlotChanged?.Invoke(i);
                        placed = true;
                        break;
                    }
                }
                if (!placed)
                    return false; // No slot for one of the items
            }
            OnInventoryChanged?.Invoke();
            if (!silent) OnItemAdded?.Invoke(item, quantity);
            if (photonView != null && photonView.IsMine && syncWithNetwork)
            {
                photonView.RPC("RPC_AddItem", RpcTarget.Others, item.itemName, quantity, silent);
            }
            return true;
        }

        // Previous stack/merge logic for regular items...
        // 1) Stack into current slots (for stackable >1, or merge all equipment into a single slot)
        for (int i = 0; i < slots.Length && remainingQuantity > 0; i++)
        {
            var s = slots[i];
            if (s != null && s.item == item && (isStackable ? s.quantity < item.maxStack : true))
            {
                int addable = isStackable ? Mathf.Min(remainingQuantity, item.maxStack - s.quantity) : remainingQuantity;
                s.quantity += addable;
                remainingQuantity -= addable;
                OnSlotChanged?.Invoke(i);
                // For equipment (maxStack==1) always stack all into one slot
                if (!isStackable) remainingQuantity = 0;
            }
        }

        // 2) New slot for leftovers (for stackables)
        for (int i = 0; i < slots.Length && remainingQuantity > 0; i++)
        {
            var s = slots[i];
            if (s == null || s.IsEmpty)
            {
                int addable = isStackable ? Mathf.Min(remainingQuantity, item.maxStack) : 1;
                slots[i] = new InventorySlot(item, addable);
                remainingQuantity -= addable;
                OnSlotChanged?.Invoke(i);
                if (!isStackable) break; // Only ever one slot for equipment!
            }
        }

        bool success = remainingQuantity == 0;
        if (success)
        {
            OnInventoryChanged?.Invoke();
            if (!silent) OnItemAdded?.Invoke(item, quantity);
            if (photonView != null && photonView.IsMine && syncWithNetwork)
            {
                photonView.RPC("RPC_AddItem", RpcTarget.Others, item.itemName, quantity, silent);
            }
        }
        return success;
    }
    
    [PunRPC]
    public void RPC_AddItem(string itemName, int quantity, bool silent = false)
    {
        if (photonView != null && !photonView.IsMine) return;
        
        ItemData item = GetItemDataByName(itemName);
        if (item != null)
        {
            AddItemLocal(item, quantity, silent);
        }
    }
    
    /// <summary>
    /// External RPC to grant items to this inventory. Can be called by MasterClient or other authorized sources.
    /// This method is designed to be called from external sources (like DestructiblePlant) and will execute
    /// on the target client where this inventory belongs. The RPC target ensures it only runs on the correct client.
    /// </summary>
    [PunRPC]
    public void RPC_GrantItem(string itemName, int quantity, bool silent = false)
    {
        Debug.Log($"[Inventory] RPC_GrantItem received - Item: {itemName}, Quantity: {quantity}, Silent: {silent}, IsMine: {(photonView != null ? photonView.IsMine.ToString() : "no PV")}");
        
        // No IsMine check needed - the RPC target (player's owner) ensures this only runs on the correct client
        // If somehow called on wrong client, AddItemLocal will still work safely
        
        ItemData item = GetItemDataByName(itemName);
        Debug.Log($"[Inventory] RPC_GrantItem - GetItemDataByName result: {(item != null ? item.itemName : "NULL")} for '{itemName}'");
        
        if (item != null)
        {
            bool success = AddItemLocal(item, quantity, silent);
            Debug.Log($"[Inventory] RPC_GrantItem result - Success: {success}, Item: {itemName}, Quantity: {quantity}");

            // ensure quest progress is updated immediately for quest items granted via RPC,
            // so destructible plants / interactables reliably drive Collect objectives in
            // both singleplayer and multiplayer.
            if (success && item.isQuestItem)
            {
                var questManager = QuestManager.Instance ?? UnityEngine.Object.FindFirstObjectByType<QuestManager>();
                if (questManager != null)
                {
                    string identifier = !string.IsNullOrEmpty(item.questId) ? item.questId : item.itemName;
                    questManager.AddProgress_Collect(identifier, quantity);
                }
            }
        }
        else
        {
            Debug.LogError($"[Inventory] RPC_GrantItem: Could not find item '{itemName}' in ItemManager! ItemManager.Instance: {(ItemManager.Instance != null ? "exists" : "NULL")}");
        }
    }
    
    private bool AddItemLocal(ItemData item, int quantity, bool silent = false)
    {
        if (item == null || quantity <= 0)
        {
            Debug.LogWarning($"[Inventory] AddItemLocal: Invalid item or quantity (item: {(item != null ? item.itemName : "null")}, qty: {quantity})");
            return false;
        }
        
        EnsureSize();

        int remainingQuantity = quantity;
        bool isStackable = item.maxStack > 1;
        bool isUnique = item.uniqueInstance || item.itemType == ItemType.Unique;
        
        Debug.Log($"[Inventory] AddItemLocal - Item: {item.itemName}, Quantity: {quantity}, Stackable: {isStackable}, Unique: {isUnique}");

        if (isUnique)
        {
            // Unique: always insert individual instances into empty slots
            for (int addCount = 0; addCount < quantity; addCount++)
            {
                bool placed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null || slots[i].IsEmpty)
                    {
                        slots[i] = new InventorySlot(item, 1);
                        OnSlotChanged?.Invoke(i);
                        placed = true;
                        break;
                    }
                }
                if (!placed)
                {
                    Debug.LogWarning($"[Inventory] AddItemLocal: No slot for unique item {item.itemName} (added {addCount}/{quantity})");
                    if (addCount > 0)
                    {
                        // Fire event for what we successfully added
                        OnInventoryChanged?.Invoke();
                        if (!silent) OnItemAdded?.Invoke(item, addCount);
                    }
                    return false;
                }
            }
            OnInventoryChanged?.Invoke();
            if (!silent) OnItemAdded?.Invoke(item, quantity);
            Debug.Log($"[Inventory] AddItemLocal: Successfully added {quantity} unique items");
            return true;
        }

        // Stackable items: stack into existing slots first
        for (int i = 0; i < slots.Length && remainingQuantity > 0; i++)
        {
            var s = slots[i];
            if (s != null && s.item == item && (isStackable ? s.quantity < item.maxStack : true))
            {
                int addable = isStackable ? Mathf.Min(remainingQuantity, item.maxStack - s.quantity) : remainingQuantity;
                s.quantity += addable;
                remainingQuantity -= addable;
                OnSlotChanged?.Invoke(i);
                // For equipment (maxStack==1) always stack all into one slot
                if (!isStackable) remainingQuantity = 0;
            }
        }

        // New slot for leftovers (for stackables)
        for (int i = 0; i < slots.Length && remainingQuantity > 0; i++)
        {
            var s = slots[i];
            if (s == null || s.IsEmpty)
            {
                int addable = isStackable ? Mathf.Min(remainingQuantity, item.maxStack) : 1;
                slots[i] = new InventorySlot(item, addable);
                remainingQuantity -= addable;
                OnSlotChanged?.Invoke(i);
                if (!isStackable) break; // Only ever one slot for equipment!
            }
        }

        bool success = remainingQuantity == 0;
        if (success)
        {
            OnInventoryChanged?.Invoke();
            if (!silent) OnItemAdded?.Invoke(item, quantity);
            Debug.Log($"[Inventory] AddItemLocal: Successfully added {quantity} items");
        }
        else
        {
            int added = quantity - remainingQuantity;
            Debug.LogWarning($"[Inventory] AddItemLocal: Only added {added}/{quantity} items (inventory full?)");
            if (added > 0)
            {
                // Fire event for what we successfully added
                OnInventoryChanged?.Invoke();
                if (!silent) OnItemAdded?.Invoke(item, added);
            }
        }
        return success;
    }

    public bool RemoveItem(ItemData item, int quantity = 1)
    {
        if (photonView != null && !photonView.IsMine && syncWithNetwork) return false;
        if (item == null || quantity <= 0) return false;
        EnsureSize();

        int remainingQuantity = quantity;
        
        // Remove from slots left-to-right
        int len = (slots != null) ? slots.Length : 0;
        int limit = Mathf.Min(SLOT_COUNT, len);
        for (int i = 0; i < limit && remainingQuantity > 0; i++)
        {
            var s = slots[i];
            if (s != null && s.item == item)
            {
                if (s.quantity > remainingQuantity)
                {
                    s.quantity -= remainingQuantity;
                    remainingQuantity = 0;
                }
                else
                {
                    remainingQuantity -= s.quantity;
                    // clear slot fully
                    slots[i] = null;
                }
                OnSlotChanged?.Invoke(i);
            }
        }

        bool success = remainingQuantity <= 0;
        if (success)
        {
            OnInventoryChanged?.Invoke();
            OnItemRemoved?.Invoke(item, quantity);
            
            // Sync with other players
            if (photonView != null && photonView.IsMine && syncWithNetwork)
            {
                photonView.RPC("RPC_RemoveItem", RpcTarget.Others, item.itemName, quantity);
            }
        }
        return success;
    }
    
    [PunRPC]
    public void RPC_RemoveItem(string itemName, int quantity)
    {
        if (photonView != null && !photonView.IsMine) return;
        
        ItemData item = GetItemDataByName(itemName);
        if (item != null)
        {
            RemoveItemLocal(item, quantity);
        }
    }
    
    private void RemoveItemLocal(ItemData item, int quantity)
    {
        if (item == null || quantity <= 0) return;
        EnsureSize();

        int remainingQuantity = quantity;
        
        // Same logic as RemoveItem but without network sync
        int len = (slots != null) ? slots.Length : 0;
        int limit = Mathf.Min(SLOT_COUNT, len);
        for (int i = 0; i < limit && remainingQuantity > 0; i++)
        {
            var s = slots[i];
            if (s != null && s.item == item)
            {
                if (s.quantity > remainingQuantity)
                {
                    s.quantity -= remainingQuantity;
                    remainingQuantity = 0;
                }
                else
                {
                    remainingQuantity -= s.quantity;
                    slots[i] = null;
                }
                OnSlotChanged?.Invoke(i);
            }
        }

        if (remainingQuantity <= 0)
        {
            OnInventoryChanged?.Invoke();
            OnItemRemoved?.Invoke(item, quantity);
        }
    }

    public bool HasItem(ItemData item, int quantity = 1)
    {
        if (item == null || quantity <= 0) return false;
        int count = 0;
        int len = (slots != null) ? slots.Length : 0;
        int limit = Mathf.Min(SLOT_COUNT, len);
        for (int i = 0; i < limit; i++)
        {
            var s = slots[i];
            if (s != null && s.item == item)
                count += s.quantity;
        }
        return count >= quantity;
    }
    
    public int GetItemCount(ItemData item)
    {
        if (item == null) return 0;
        int count = 0;
        int len = (slots != null) ? slots.Length : 0;
        int limit = Mathf.Min(SLOT_COUNT, len);
        for (int i = 0; i < limit; i++)
        {
            var s = slots[i];
            if (s != null && s.item == item)
                count += s.quantity;
        }
        return count;
    }
    
    public void SwapSlots(int fromIndex, int toIndex)
    {
        if (photonView != null && !photonView.IsMine && syncWithNetwork) return;
        if (fromIndex < 0 || fromIndex >= SLOT_COUNT || toIndex < 0 || toIndex >= SLOT_COUNT) return;
        
        var temp = slots[fromIndex];
        slots[fromIndex] = slots[toIndex];
        slots[toIndex] = temp;
        
        OnSlotChanged?.Invoke(fromIndex);
        OnSlotChanged?.Invoke(toIndex);
        OnInventoryChanged?.Invoke();
        
        // Sync with other players
        if (photonView != null && photonView.IsMine && syncWithNetwork)
        {
            photonView.RPC("RPC_SwapSlots", RpcTarget.Others, fromIndex, toIndex);
        }
    }
    
    [PunRPC]
    public void RPC_SwapSlots(int fromIndex, int toIndex)
    {
        if (photonView != null && !photonView.IsMine) return;
        
        var temp = slots[fromIndex];
        slots[fromIndex] = slots[toIndex];
        slots[toIndex] = temp;
        
        OnSlotChanged?.Invoke(fromIndex);
        OnSlotChanged?.Invoke(toIndex);
        OnInventoryChanged?.Invoke();
    }
    
    private ItemData GetItemDataByName(string itemName)
    {
        // 1) Prefer runtime ItemManager if present
        var itemMgr = ItemManager.Instance != null ? ItemManager.Instance : FindFirstObjectByType<ItemManager>();
        if (itemMgr != null)
        {
            var byMgr = itemMgr.GetItemDataByName(itemName);
            if (byMgr != null) return byMgr;
        }

        // 2) Fallback to Resources/ItemDatabase asset (works without ItemManager in scene)
        var db = ItemDatabase.Load();
        if (db != null)
        {
            var byDb = db.FindByName(itemName);
            if (byDb != null) return byDb;
        }

        Debug.LogError($"[Inventory] GetItemDataByName: Could not resolve '{itemName}' via ItemManager or Resources/ItemDatabase.");
        return null;
    }
    
    // Network synchronization
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // Send inventory data
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                var slot = slots[i];
                if (slot != null && slot.item != null)
                {
                    stream.SendNext(slot.item.itemName);
                    stream.SendNext(slot.quantity);
                }
                else
                {
                    stream.SendNext("");
                    stream.SendNext(0);
                }
            }
        }
        else
        {
            // Receive inventory data
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                string itemName = (string)stream.ReceiveNext();
                int quantity = (int)stream.ReceiveNext();
                
                if (!string.IsNullOrEmpty(itemName))
                {
                    ItemData item = GetItemDataByName(itemName);
                    if (item != null)
                    {
                        slots[i] = new InventorySlot(item, quantity);
                    }
                    else
                    {
                        slots[i] = null;
                    }
                }
                else
                {
                    slots[i] = null;
                }
            }
            
            OnInventoryChanged?.Invoke();
        }
    }

    // Removes exactly from a specific slot instance (needed for unique items in separate slots, not by value)
    public bool RemoveSpecificSlot(InventorySlot specificSlot, int quantity = 1)
    {
        if (specificSlot == null || specificSlot.item == null) return false;
        EnsureSize();
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == specificSlot)
            {
                int preQuantity = slots[i].quantity;
                string itemName = slots[i].item != null ? slots[i].item.itemName : "null";
                if (slots[i].quantity > quantity)
                {
                    slots[i].quantity -= quantity;
                    Debug.Log($"[inventory] RemoveSpecificSlot | slot={i} | item={itemName} | pre={preQuantity} | post={slots[i].quantity} | partial-removal");
                }
                else
                {
                    Debug.Log($"[inventory] RemoveSpecificSlot | slot={i} | item={itemName} | pre={preQuantity} | post=0 | slot-cleared");
                    slots[i] = null;
                }
                OnSlotChanged?.Invoke(i);
                OnInventoryChanged?.Invoke();
                OnItemRemoved?.Invoke(specificSlot.item, quantity);
                return true;
            }
        }
        Debug.Log($"[inventory] RemoveSpecificSlot | SLOT NOT FOUND | item={(specificSlot.item != null ? specificSlot.item.itemName : "null")} | quantity={quantity} | FAIL");
        return false;
    }
    
    void OnDestroy()
    {
        // Clear event subscriptions to prevent memory leaks
        OnInventoryChanged = null;
        OnItemAdded = null;
        OnItemRemoved = null;
        OnSlotChanged = null;
    }
}
