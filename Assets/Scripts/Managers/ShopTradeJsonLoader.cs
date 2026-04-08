using UnityEngine;

public class ShopTradeJsonLoader : MonoBehaviour
{
    [Tooltip("Name of the JSON file in Resources/Trades (without extension)")]
    public string tradeJsonFile = "NunoTrades";
    public NunoShopManager shopManager;
    
    [System.Serializable]
    public class ShopTradeDataJson
    {
        public string tradeName;
        public string description;
        public string[] requiredItemNames;
        public int[] requiredQuantities;
        public string rewardItemName;
        public int rewardQuantity = 1;
        public int maxUses = -1;
        public bool requiresUnlock = false;
        public string[] requiredShrineIds;
        public string[] requiredQuestIds;
    }
    
    [System.Serializable]
    public class ShopTradeDataContainer
    {
        public ShopTradeDataJson[] trades;
    }

    void Awake()
    {
        LoadAndApplyTrades();
    }

    public void LoadAndApplyTrades()
    {
        if (shopManager == null) shopManager = NunoShopManager.Instance;
        if (shopManager == null)
        {
            Debug.LogError("ShopTradeJsonLoader: NunoShopManager.Instance not found!");
            return;
        }
        
        var file = Resources.Load<TextAsset>("Trades/" + tradeJsonFile);
        if (file == null)
        {
            Debug.LogError($"ShopTradeJsonLoader: Could not load trade JSON: {tradeJsonFile}");
            return;
        }
        
        var container = JsonUtility.FromJson<ShopTradeDataContainer>(file.text);
        if (container == null || container.trades == null)
        {
            Debug.LogError("ShopTradeJsonLoader: Failed to parse trade JSON");
            return;
        }
        
        var db = ItemDatabase.Load();
        if (db == null)
        {
            Debug.LogError("ShopTradeJsonLoader: Resources/ItemDatabase not found!");
            return;
        }
        
        var shopTrades = new ShopTradeData[container.trades.Length];
        for (int i = 0; i < container.trades.Length; i++)
        {
            var tradeJson = container.trades[i];
            ItemData[] requiredItems = null;
            if (tradeJson.requiredItemNames != null && tradeJson.requiredItemNames.Length > 0)
            {
                requiredItems = new ItemData[tradeJson.requiredItemNames.Length];
                for (int j = 0; j < tradeJson.requiredItemNames.Length; j++)
                {
                    requiredItems[j] = db.FindByName(tradeJson.requiredItemNames[j]);
                    if (requiredItems[j] == null)
                        Debug.LogWarning($"ShopTradeJsonLoader: Item '{tradeJson.requiredItemNames[j]}' not found in ItemDatabase");
                }
            }
            
            var rewardItem = db.FindByName(tradeJson.rewardItemName);
            if (rewardItem == null)
                Debug.LogWarning($"ShopTradeJsonLoader: Reward item '{tradeJson.rewardItemName}' not found in ItemDatabase");
            
            shopTrades[i] = CreateRuntimeShopTrade(tradeJson, requiredItems, rewardItem);
        }
        
        shopManager.availableTrades = shopTrades;
        Debug.Log($"ShopTradeJsonLoader: Loaded {shopTrades.Length} trades from {tradeJsonFile} via ItemDatabase");
    }
    
    private ShopTradeData CreateRuntimeShopTrade(ShopTradeDataJson json, ItemData[] requiredItems, ItemData rewardItem)
    {
        // Create a ScriptableObject instance at runtime
        ShopTradeData trade = ScriptableObject.CreateInstance<ShopTradeData>();
        
        trade.tradeName = json.tradeName;
        trade.description = json.description;
        trade.requiredItems = requiredItems;
        trade.requiredQuantities = json.requiredQuantities;
        trade.rewardItem = rewardItem;
        trade.rewardQuantity = json.rewardQuantity;
        trade.maxUses = json.maxUses;
        trade.currentUses = 0;
        trade.requiresUnlock = json.requiresUnlock;
        trade.requiredShrineIds = json.requiredShrineIds;
        trade.requiredQuestIds = json.requiredQuestIds;
        trade.isUnlocked = !json.requiresUnlock; // Start unlocked if no unlock required
        
        return trade;
    }
}

