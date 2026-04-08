using UnityEngine;
using Photon.Pun;

public class EquipmentManager : MonoBehaviourPun
{
    public PlayerStats playerStats;
    public ItemData equippedItem;
    public Inventory playerInventory; // Assign in Inspector or via script

    [Header("Equipment Model Handling")]
    public Transform handTransform; // Assign this in the Inspector to your character's hand bone
    [Tooltip("Per-player offset for all equipped items. Use to correct grip for different models/skeletons (e.g. sword rotated outwards on female model).")]
    public Vector3 holdPositionOffset = Vector3.zero;
    [Tooltip("Per-player rotation offset (Euler degrees) for all equipped items.")]
    public Vector3 holdRotationOffset = Vector3.zero;
    private GameObject equippedModelInstance;

#if UNITY_EDITOR
    void OnValidate()
    {
        // Notify EquipmentGripPreview so it updates instantly when hold offsets change
        var previews = Object.FindObjectsByType<EquipmentGripPreview>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var p in previews)
        {
            if (p != null && p.equipmentManager == this && p.previewActive)
                p.RefreshPreview();
        }
    }
#endif

    // static cache for persisting equipped item across scene loads
    private static string cachedEquippedItemName;
    private static bool hasCachedEquipment;

    /// <summary>
    /// Caches the local player's equipped item name so it can be restored after a scene load.
    /// </summary>
    public static void CacheLocalEquipment()
    {
        var allEquip = Object.FindObjectsByType<EquipmentManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var em in allEquip)
        {
            var pv = em.GetComponent<PhotonView>();
            if (pv != null && !pv.IsMine) continue;

            if (em.equippedItem != null)
            {
                cachedEquippedItemName = em.equippedItem.itemName;
                hasCachedEquipment = true;
                Debug.Log($"[EquipmentManager] Cached equipped item '{cachedEquippedItemName}' for scene transition.");
            }
            else
            {
                cachedEquippedItemName = null;
                hasCachedEquipment = false;
            }
            return;
        }
    }

    /// <summary>
    /// Restores the cached equipped item on the local player after a scene load.
    /// The item is looked up from the inventory first; if not found there, from the ItemDatabase.
    /// </summary>
    public void TryRestoreCachedEquipment()
    {
        if (!hasCachedEquipment || string.IsNullOrEmpty(cachedEquippedItemName)) return;

        var pv = GetComponent<PhotonView>();
        if (pv != null && !pv.IsMine) return;

        // already have the right item equipped
        if (equippedItem != null && equippedItem.itemName == cachedEquippedItemName) return;

        // look up the ItemData from the item database
        var db = ItemDatabase.Load();
        if (db == null)
        {
            Debug.LogWarning("[EquipmentManager] Cannot restore cached equipment: ItemDatabase not found.");
            return;
        }

        ItemData item = db.FindByName(cachedEquippedItemName);
        if (item == null)
        {
            Debug.LogWarning($"[EquipmentManager] Cannot restore cached equipment: '{cachedEquippedItemName}' not found in database.");
            hasCachedEquipment = false;
            return;
        }

        // if the item is in inventory, remove it first so we don't duplicate
        if (playerInventory != null)
        {
            int slot = playerInventory.FindItemSlot(item);
            if (slot >= 0)
            {
                playerInventory.RemoveItem(item, 1);
            }
        }

        // equip without going through inventory removal flow
        if (equippedItem != null)
        {
            // return current to inventory before equipping cached one
            if (playerInventory != null && equippedItem != item)
            {
                playerInventory.AddItem(equippedItem, 1, silent: true);
            }
            if (playerStats != null) playerStats.RemoveEquipment(equippedItem);
        }

        equippedItem = item;
        if (playerStats != null) playerStats.ApplyEquipment(item);
        EquipModelLocal(item);

        // sync visuals to other clients
        if (pv != null && (PhotonNetwork.IsConnected || PhotonNetwork.OfflineMode))
            pv.RPC(nameof(RPC_EquipModel), RpcTarget.Others, cachedEquippedItemName);

        Debug.Log($"[EquipmentManager] Restored cached equipped item '{cachedEquippedItemName}' after scene transition.");
        hasCachedEquipment = false;
        cachedEquippedItemName = null;
    }

    void Awake()
    {
        ResolveHandTransform();
    }

    /// <summary>
    /// Resolves handTransform from the current character's skeleton.
    /// Always resolves at runtime so each player (different models/skeletons) gets the correct hand.
    /// Uses HumanBodyBones for Humanoid rigs (works across different models regardless of bone names),
    /// then falls back to name-based search, then keeps Inspector assignment.
    /// </summary>
    private void ResolveHandTransform()
    {
        var animator = GetComponentInChildren<Animator>();
        if (animator == null) return;

        // Prefer HumanBodyBones: works for any Humanoid rig regardless of bone names
        if (animator.isHuman && animator.avatar != null && animator.avatar.isHuman)
        {
            Transform humanHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (humanHand != null)
            {
                handTransform = humanHand;
                Debug.Log($"[EquipmentManager] Resolved handTransform via HumanBodyBones: {handTransform.name}");
                return;
            }
        }

        // Fallback: name-based search (for generic rigs)
        string[] handNames = { "mixamorig:RightHand", "RightHand", "mixamorig:Hand_R", "Hand_R", "Bip01_R_Hand", "hand_r", "Hand" };
        foreach (var handName in handNames)
        {
            Transform hand = FindChildRecursive(animator.transform, handName);
            if (hand != null)
            {
                handTransform = hand;
                Debug.Log($"[EquipmentManager] Auto-found handTransform by name: {handTransform.name}");
                return;
            }
        }

        if (handTransform == null)
        {
            Debug.LogWarning("[EquipmentManager] handTransform not assigned and could not be auto-found. Assign in Inspector or use a Humanoid rig.");
        }
    }

    private Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            Transform found = FindChildRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private void SetLayerRecursive(Transform transform, int layer)
    {
        transform.gameObject.layer = layer;
        foreach (Transform child in transform)
        {
            SetLayerRecursive(child, layer);
        }
    }

    // Centralized entry point for UI: equip exactly one from a specific inventory slot
    public bool TryEquipFromInventorySlot(Inventory inventory, InventorySlot slot)
    {
        var pv = photonView;
        if (pv != null && !pv.IsMine) return false;
        if (inventory == null || slot == null || slot.item == null) return false;
        // remove strictly from the given slot to avoid cross-slot merges
        bool removed = inventory.RemoveSpecificSlot(slot, 1);
        if (!removed) return false;
        Equip(slot.item);
        return true;
    }

    // Centralized entry for world pickups: prefer equip if hands free; else store in inventory
    public void HandlePickup(ItemData item)
    {
        var pv = photonView;
        if (pv != null && !pv.IsMine) return;
        if (item == null) return;
        if (equippedItem == null)
        {
            Equip(item);
        }
        else if (playerInventory != null)
        {
            playerInventory.AddItem(item, 1);
        }
    }

    public void Equip(ItemData item)
    {
        var pv = photonView;
        if (pv != null && !pv.IsMine) return;
        // Only unequip previous (which returns it to inventory) before equipping new
        if (equippedItem != null)
        {
            Unequip();
        }
        equippedItem = item;
        if (playerStats != null && item != null) playerStats.ApplyEquipment(item);
        Debug.Log($"[equipment] Equip| equipped item={(item != null ? item.itemName : "null")}");
        
        // Equip model locally first (for immediate feedback)
        if (item != null)
        {
            EquipModelLocal(item);
            
            // Broadcast model equip to all clients using itemName as id
            string id = item.itemName;
            if (pv != null && (PhotonNetwork.IsConnected || PhotonNetwork.OfflineMode))
                pv.RPC(nameof(RPC_EquipModel), RpcTarget.Others, id);
        }
    }
    
    private void EquipModelLocal(ItemData item)
    {
        if (item == null)
        {
            Debug.LogWarning("[EquipmentManager] Cannot equip model: ItemData is null");
            return;
        }
        
        if (item.modelPrefab == null)
        {
            Debug.LogWarning($"[EquipmentManager] Cannot equip {item.itemName}: modelPrefab is null. Assign modelPrefab in ItemData asset.");
            return;
        }
        
        if (handTransform == null)
        {
            Debug.LogWarning($"[EquipmentManager] Cannot equip {item.itemName}: handTransform is null. Assign handTransform in Inspector (e.g., RightHand bone).");
            return;
        }
        
        // clear previous model instance only (don't call RPC_ClearModel which also nulls equippedItem)
        if (equippedModelInstance != null)
        {
            Destroy(equippedModelInstance);
            equippedModelInstance = null;
        }
        
        // Instantiate new model as child of hand transform
        equippedModelInstance = Instantiate(item.modelPrefab, handTransform);
        equippedModelInstance.name = $"{item.itemName}_Model";
        
        // Apply transform: item overrides (or prefab default) + per-player hold offset
        Vector3 basePos = item.overrideTransform ? item.modelLocalPosition : Vector3.zero;
        Quaternion baseRot = item.overrideTransform ? Quaternion.Euler(item.modelLocalEulerAngles) : Quaternion.identity;
        equippedModelInstance.transform.localPosition = basePos + holdPositionOffset;
        equippedModelInstance.transform.localRotation = baseRot * Quaternion.Euler(holdRotationOffset);
        
        // Always apply scale
        equippedModelInstance.transform.localScale = item.modelScale;
        
        // Ensure model is visible (not hidden)
        equippedModelInstance.SetActive(true);
        
        // Enable and verify all renderers are visible
        var renderers = equippedModelInstance.GetComponentsInChildren<Renderer>(true);
        int enabledCount = 0;
        foreach (var renderer in renderers)
        {
            renderer.enabled = true;
            renderer.gameObject.SetActive(true);
            enabledCount++;
            
            // Check if material exists
            if (renderer.sharedMaterial == null && renderer.materials.Length == 0)
            {
                Debug.LogWarning($"[EquipmentManager] Renderer '{renderer.name}' has no material assigned!");
            }
        }
        
        // Check for mesh filters (without renderers)
        var meshFilters = equippedModelInstance.GetComponentsInChildren<MeshFilter>(true);
        foreach (var filter in meshFilters)
        {
            if (filter.sharedMesh == null)
            {
                Debug.LogWarning($"[EquipmentManager] MeshFilter '{filter.name}' has no mesh assigned!");
            }
        }
        
        // Log detailed info for debugging
        Debug.Log($"[EquipmentManager] Equipped model '{item.modelPrefab.name}' for {item.itemName} on {handTransform.name}");
        Debug.Log($"[EquipmentManager] Model position: {equippedModelInstance.transform.position}, localPosition: {equippedModelInstance.transform.localPosition}");
        Debug.Log($"[EquipmentManager] Model scale: {equippedModelInstance.transform.localScale}");
        Debug.Log($"[EquipmentManager] Found {renderers.Length} Renderer component(s), {enabledCount} enabled");
        
        // Warn if model might not be visible
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"[EquipmentManager] Model '{item.modelPrefab.name}' has no Renderer components - it may not be visible!");
        }
        else
        {
            Debug.Log($"[EquipmentManager] Renderers enabled. Model should be visible. Check if materials are assigned and not transparent.");
        }
        
        // Ensure the model is on a visible layer (not a culling layer)
        int defaultLayer = LayerMask.NameToLayer("Default");
        if (defaultLayer >= 0)
        {
            SetLayerRecursive(equippedModelInstance.transform, defaultLayer);
        }
        
        // Final verification - check if model is actually in scene
        if (equippedModelInstance == null || !equippedModelInstance.activeInHierarchy)
        {
            Debug.LogError($"[EquipmentManager] CRITICAL: Model instantiated but not active in hierarchy! GameObject: {equippedModelInstance?.name}");
        }
        else
        {
            Debug.Log($"[EquipmentManager] ✓ Model '{equippedModelInstance.name}' is active in scene hierarchy under '{handTransform.name}'");
            
            // Check model bounds to verify it has size
            Bounds bounds = new Bounds();
            bool hasBounds = false;
            foreach (var renderer in renderers)
            {
                if (renderer.bounds.size.magnitude > 0.001f)
                {
                    if (!hasBounds)
                    {
                        bounds = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }
            }
            
            if (hasBounds)
            {
                Debug.Log($"[EquipmentManager] Model bounds: center={bounds.center}, size={bounds.size}");
                if (bounds.size.magnitude < 0.01f)
                {
                    Debug.LogWarning($"[EquipmentManager] Model bounds are very small ({bounds.size.magnitude:F4}) - model may be too small to see!");
                }
            }
            else
            {
                Debug.LogWarning($"[EquipmentManager] Could not calculate model bounds - renderers may have no mesh/material");
            }
        }
    }

    public void Unequip()
    {
        var pv = photonView;
        if (pv != null && !pv.IsMine) return;
        if (equippedItem != null)
        {
            if (playerInventory != null)
            {
                Debug.Log($"[equipment] Unequip| returning item={equippedItem.itemName} to inventory");
                playerInventory.AddItem(equippedItem, 1, silent: true);
            }
            if (playerStats != null) playerStats.RemoveEquipment(equippedItem);
            Debug.Log($"[equipment] Unequip| unequipped item={(equippedItem != null ? equippedItem.itemName : "null")}");
            equippedItem = null;
        }
        // Remove visuals locally first, then sync across clients
        RPC_ClearModel();
        if (pv != null && (PhotonNetwork.IsConnected || PhotonNetwork.OfflineMode))
            pv.RPC(nameof(RPC_ClearModel), RpcTarget.Others);
    }

    // Refresh/reequip the current item's model (useful if model didn't spawn correctly)
    public void RefreshEquippedModel()
    {
        if (equippedItem != null)
        {
            Debug.Log($"[EquipmentManager] Refreshing equipped model for {equippedItem.itemName}");
            EquipModelLocal(equippedItem);
        }
    }

    [PunRPC]
    private void RPC_EquipModel(string itemName)
    {
        // This RPC is called on OTHER clients, so we need to look up the item from database
        var db = ItemDatabase.Load();
        if (db == null)
        {
            Debug.LogWarning($"[EquipmentManager] RPC_EquipModel: ItemDatabase not found. Cannot equip {itemName}");
            return;
        }
        
        var item = db.FindByName(itemName);
        if (item == null)
        {
            Debug.LogWarning($"[EquipmentManager] RPC_EquipModel: Item '{itemName}' not found in database");
            return;
        }
        
        // sync equipped item reference so other systems (e.g. DestructiblePlant) can check it
        equippedItem = item;
        EquipModelLocal(item);
    }

    [PunRPC]
    private void RPC_ClearModel()
    {
        equippedItem = null;
        if (equippedModelInstance != null)
        {
            Destroy(equippedModelInstance);
            equippedModelInstance = null;
        }
    }
}
