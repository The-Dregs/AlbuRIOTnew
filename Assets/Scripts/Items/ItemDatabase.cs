using System.Collections.Generic;
using UnityEngine;

// Simple Resources-based database for item lookup by name (as network id)
[CreateAssetMenu(fileName = "ItemDatabase", menuName = "Inventory/Item Database")]
public class ItemDatabase : ScriptableObject
{
    public List<ItemData> items = new List<ItemData>();

    private static ItemDatabase _cached;
    public static ItemDatabase Load()
    {
        if (_cached != null) return _cached;
        _cached = Resources.Load<ItemDatabase>("ItemDatabase");
        if (_cached == null)
        {
            Debug.LogWarning("ItemDatabase: No Resources/ItemDatabase asset found. Network lookups will fail.");
        }
        return _cached;
    }

    public ItemData FindByName(string itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return null;
        foreach (var it in items)
        {
            if (it != null && string.Equals(it.itemName, itemName, System.StringComparison.OrdinalIgnoreCase))
                return it;
        }
        return null;
    }
}
