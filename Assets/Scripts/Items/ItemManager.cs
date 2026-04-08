using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;

public class ItemManager : MonoBehaviour
{
    [Header("Item Database")]
    public ItemData[] itemDatabase;
    
    [Header("Item Spawning")]
    public GameObject itemPickupPrefab;
    public Transform[] itemSpawnPoints;
    
    // Singleton pattern
    public static ItemManager Instance { get; private set; }
    
    // Item lookup dictionary
    private Dictionary<string, ItemData> itemLookup;
    
    // Events
    public System.Action<ItemData, Vector3> OnItemSpawned;
    public System.Action<ItemData> OnItemPickedUp;
    
    void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        // Initialize item lookup
        InitializeItemLookup();
    }
    
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void InitializeItemLookup()
    {
        itemLookup = new Dictionary<string, ItemData>();
        
        if (itemDatabase != null)
        {
            foreach (var item in itemDatabase)
            {
                if (item != null && !string.IsNullOrEmpty(item.itemName))
                {
                    itemLookup[item.itemName] = item;
                }
            }
        }
        
        Debug.Log($"ItemManager initialized with {itemLookup.Count} items");
    }
    
    public ItemData GetItemDataByName(string itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return null;
        
        if (itemLookup != null && itemLookup.ContainsKey(itemName))
        {
            return itemLookup[itemName];
        }
        
        Debug.LogWarning($"Item not found: {itemName}");
        return null;
    }
    
    public ItemData[] GetItemsByType(ItemType itemType)
    {
        if (itemDatabase == null) return new ItemData[0];
        
        return itemDatabase.Where(item => item != null && item.itemType == itemType).ToArray();
    }
    
    public ItemData[] GetQuestItems()
    {
        if (itemDatabase == null) return new ItemData[0];
        
        return itemDatabase.Where(item => item != null && item.isQuestItem).ToArray();
    }
    
    public ItemData[] GetOfferingItems()
    {
        if (itemDatabase == null) return new ItemData[0];
        
        return itemDatabase.Where(item => item != null && item.canBeOffered).ToArray();
    }
    
    public void SpawnItem(ItemData item, Vector3 position, int quantity = 1)
    {
        if (item == null || itemPickupPrefab == null) return;
        
        GameObject itemPickup;
        
        // Use PhotonNetwork.Instantiate if in a networked room, otherwise use regular Instantiate
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            Debug.Log($"Spawning networked item {item.itemName} x{quantity} at {position}");
            itemPickup = PhotonNetwork.Instantiate(itemPickupPrefab.name, position, Quaternion.identity);
        }
        else
        {
            Debug.Log($"Spawning local item {item.itemName} x{quantity} at {position}");
            itemPickup = Instantiate(itemPickupPrefab, position, Quaternion.identity);
        }
        
        // Set up the item pickup component
        var pickupComponent = itemPickup.GetComponent<ItemPickup>();
        if (pickupComponent != null)
        {
            pickupComponent.SetItem(item, quantity);
        }
        
        OnItemSpawned?.Invoke(item, position);
    }
    
    public void SpawnRandomItem(Vector3 position)
    {
        if (itemDatabase == null || itemDatabase.Length == 0) return;
        
        ItemData randomItem = itemDatabase[Random.Range(0, itemDatabase.Length)];
        if (randomItem != null)
        {
            SpawnItem(randomItem, position, Random.Range(1, randomItem.maxStack + 1));
        }
    }
    
    public void SpawnItemAtRandomLocation(ItemData item, int quantity = 1)
    {
        if (item == null || itemSpawnPoints == null || itemSpawnPoints.Length == 0) return;
        
        Transform randomSpawnPoint = itemSpawnPoints[Random.Range(0, itemSpawnPoints.Length)];
        SpawnItem(item, randomSpawnPoint.position, quantity);
    }
    
    public void SpawnQuestItems()
    {
        ItemData[] questItems = GetQuestItems();
        
        foreach (var item in questItems)
        {
            if (item != null)
            {
                SpawnItemAtRandomLocation(item, 1);
            }
        }
    }
    
    public void SpawnOfferingItems()
    {
        ItemData[] offeringItems = GetOfferingItems();
        
        foreach (var item in offeringItems)
        {
            if (item != null)
            {
                SpawnItemAtRandomLocation(item, Random.Range(1, 3));
            }
        }
    }
    
    public bool IsValidItem(string itemName)
    {
        return !string.IsNullOrEmpty(itemName) && itemLookup != null && itemLookup.ContainsKey(itemName);
    }
    
    public int GetItemCount()
    {
        return itemLookup != null ? itemLookup.Count : 0;
    }
    
    public string[] GetAllItemNames()
    {
        if (itemLookup == null) return new string[0];
        
        return itemLookup.Keys.ToArray();
    }
    
    // Method to refresh item lookup (useful for runtime item additions)
    public void RefreshItemLookup()
    {
        InitializeItemLookup();
    }
}

