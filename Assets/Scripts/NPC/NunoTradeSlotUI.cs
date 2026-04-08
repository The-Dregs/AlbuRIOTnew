using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Text;

public class NunoTradeSlotUI : MonoBehaviour
{
    private static Sprite _placeholderSprite;
    private static Sprite PlaceholderSprite
    {
        get
        {
            if (_placeholderSprite == null)
            {
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, new Color(0.45f, 0.2f, 0.55f));
                tex.Apply();
                tex.filterMode = FilterMode.Point;
                _placeholderSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one);
            }
            return _placeholderSprite;
        }
    }

    [Header("UI References")]
    public Image requiredIcon;
    public TextMeshProUGUI requiredText;
    public Image rewardIcon;
    public TextMeshProUGUI rewardText;
    public Button tradeButton;
    public GameObject disabledOverlay;
    
    private ShopTradeData trade;
    private int tradeIndex;
    private NunoShopManager shopManager;
    
    public void Initialize(ShopTradeData tradeData, int index, NunoShopManager manager)
    {
        trade = tradeData;
        tradeIndex = index;
        shopManager = manager;
        if (trade == null) return;
        if (tradeButton != null) tradeButton.onClick.RemoveAllListeners();
        if (tradeButton != null) tradeButton.onClick.AddListener(OnTradeButton);
        UpdateUI();
    }
    
    private void UpdateUI()
    {
        if (trade == null) return;
        
        if (requiredIcon != null && trade.requiredItems != null && trade.requiredItems.Length > 0 && trade.requiredItems[0] != null)
        {
            requiredIcon.preserveAspect = true;
            requiredIcon.enabled = true;
            requiredIcon.sprite = trade.requiredItems[0].icon != null ? trade.requiredItems[0].icon : PlaceholderSprite;
        }
        else if (requiredIcon != null) requiredIcon.enabled = false;
        
        if (rewardIcon != null && trade.rewardItem != null)
        {
            rewardIcon.preserveAspect = true;
            rewardIcon.enabled = true;
            rewardIcon.sprite = trade.rewardItem.icon != null ? trade.rewardItem.icon : PlaceholderSprite;
        }
        else if (rewardIcon != null) rewardIcon.enabled = false;
        
        if (rewardText != null)
        {
            if (trade.rewardItem != null)
                rewardText.text = $"{trade.rewardQuantity}x {trade.rewardItem.itemName}";
            else
                rewardText.text = "Invalid Reward";
        }
        
        // Display required items
        if (requiredText != null)
        {
            StringBuilder sb = new StringBuilder();
            if (trade.requiredItems != null && trade.requiredQuantities != null)
            {
                for (int i = 0; i < trade.requiredItems.Length; i++)
                {
                    if (trade.requiredItems[i] == null) continue;
                    sb.Append($"{trade.requiredQuantities[i]}x {trade.requiredItems[i].itemName}");
                    if (i < trade.requiredItems.Length - 1)
                        sb.Append(", ");
                }
            }
            requiredText.text = sb.ToString();
        }
        
        // Check if player can afford trade
        bool canAfford = CanAfford();
        if (tradeButton != null)
        {
            tradeButton.interactable = canAfford && trade.CanTrade;
        }
        
        if (disabledOverlay != null)
        {
            disabledOverlay.SetActive(!canAfford || !trade.CanTrade);
        }
    }
    
    private bool CanAfford()
    {
        if (shopManager == null) return false;
        
        var inventory = Inventory.FindLocalInventory();
        if (inventory == null || trade == null || trade.requiredItems == null) return false;
        
        for (int i = 0; i < trade.requiredItems.Length; i++)
        {
            if (trade.requiredItems[i] == null) continue;
            if (!inventory.HasItem(trade.requiredItems[i], trade.requiredQuantities[i]))
                return false;
        }
        
        return true;
    }
    
    public void OnTradeButton()
    {
        if (shopManager != null)
        {
            shopManager.ExecuteTrade(tradeIndex);
            UpdateUI();
        }
    }
}

