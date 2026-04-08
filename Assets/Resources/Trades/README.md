# Nuno Trading JSON Setup

This folder contains JSON files that define trades for the Nuno merchant system.

## File Structure

Each JSON file follows this structure:

```json
{
  "trades": [
    {
      "tradeName": "Name of the trade",
      "description": "Description of what the trade does",
      "requiredItemNames": ["Item1", "Item2"],
      "requiredQuantities": [3, 2],
      "rewardItemName": "RewardItem",
      "rewardQuantity": 1,
      "maxUses": -1,
      "requiresUnlock": false,
      "requiredShrineIds": [],
      "requiredQuestIds": []
    }
  ]
}
```

## Field Descriptions

- **tradeName**: Display name for the trade
- **description**: Flavor text describing the trade
- **requiredItemNames**: Array of item names the player must have
- **requiredQuantities**: Array of quantities needed (must match requiredItemNames length)
- **rewardItemName**: Name of the item the player receives
- **rewardQuantity**: How many of the reward item to give
- **maxUses**: -1 for unlimited trades, or a number to limit usage
- **requiresUnlock**: Whether this trade needs to be unlocked
- **requiredShrineIds**: Array of shrine IDs that must be cleansed to unlock
- **requiredQuestIds**: Array of quest IDs (as strings) that must be completed

## Using the Loader

Add the `ShopTradeJsonLoader` component to your Nuno NPC GameObject:

1. Select the Nuno NPC in the scene
2. Add Component → ShopTradeJsonLoader
3. Set the `tradeJsonFile` field to "NunoTrades" (or your JSON filename without .json)
4. Make sure `NunoShopManager` is also on the same GameObject

The loader will automatically load trades on Awake and populate the shop.

## Item Names

Use the exact `itemName` from your ItemData ScriptableObjects:
- "Aloe Vera"
- "Lunar Lily"
- "Bolo Knife"
- "Geranium"
- etc.

Check Assets/Resources/Items/1ItemData/ for available items.

## Example Trades

### Simple Trade: 3 Aloe for 1 Bolo
```json
{
  "tradeName": "Healing Aloe Exchange",
  "description": "Trade aloe for a bolo knife.",
  "requiredItemNames": ["Aloe Vera"],
  "requiredQuantities": [3],
  "rewardItemName": "Bolo Knife",
  "rewardQuantity": 1,
  "maxUses": -1,
  "requiresUnlock": false,
  "requiredShrineIds": [],
  "requiredQuestIds": []
}
```

### Multi-Item Trade
```json
{
  "tradeName": "Herbal Bundle",
  "description": "Combine different herbs.",
  "requiredItemNames": ["Aloe Vera", "Lunar Lily"],
  "requiredQuantities": [2, 2],
  "rewardItemName": "Geranium",
  "rewardQuantity": 5,
  "maxUses": -1,
  "requiresUnlock": false,
  "requiredShrineIds": [],
  "requiredQuestIds": []
}
```

### Unlockable Trade
```json
{
  "tradeName": "Advanced Trade",
  "description": "Requires shrine completion.",
  "requiredItemNames": ["Crystal"],
  "requiredQuantities": [1],
  "rewardItemName": "Bolo Knife",
  "rewardQuantity": 1,
  "maxUses": 1,
  "requiresUnlock": true,
  "requiredShrineIds": ["FirstShrine"],
  "requiredQuestIds": ["2"]
}
```

## Notes

- Item names must exactly match ItemData ScriptableObject `itemName` fields
- Quest IDs are strings, not numbers
- Shrine IDs should match shrine identifiers used in ShrineManager
- The loader creates runtime ShopTradeData objects from the JSON
- If an item name isn't found, a warning is logged but the system continues

