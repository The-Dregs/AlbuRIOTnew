using UnityEngine;
using Photon.Pun;
using System;
using TMPro;
using UnityEngine.UI;

public class NunoShopManager : MonoBehaviour
{
    [Header("Shop Configuration")]
    public ShopTradeData[] availableTrades;
    
    [Header("UI References")]
    public GameObject shopPanel;
    public Transform tradeListParent;
    public GameObject tradeSlotPrefab;
    public Button closeButton;
    
    // Singleton pattern for shared UI across all NPC merchants
    private static NunoShopManager _instance;
    public static NunoShopManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var existing = FindFirstObjectByType<NunoShopManager>();
                if (existing != null)
                    _instance = existing;
                else
                {
                    var go = new GameObject("NunoShopManager_Singleton");
                    _instance = go.AddComponent<NunoShopManager>();
                }
            }
            return _instance;
        }
    }
    
    private Inventory playerInventory;
    private PhotonView playerPhotonView;
    private QuestManager questManager;
    private bool isOpen = false;
    private int inputLockToken = 0;
    private ShopTradeData[] currentActiveTrades; // Trades from the NPC that opened the shop
    
    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Debug.LogWarning("Multiple NunoShopManager instances detected. Destroying duplicate.");
            Destroy(this);
            return;
        }
        
        if (shopPanel != null)
            shopPanel.SetActive(false);
        else
        {
            // Try to find existing UI in children
            var existing = transform.GetComponentInChildren<Canvas>(true);
            if (existing != null) shopPanel = existing.gameObject;
        }

        // Ensure trades get populated even if the scene forgot to assign them in the inspector.
        if (availableTrades == null || availableTrades.Length == 0)
        {
            var loader = FindFirstObjectByType<ShopTradeJsonLoader>();
            if (loader != null)
            {
                loader.LoadAndApplyTrades();
            }
            else
            {
                // Fall back to attaching a loader to this object so the default JSON is used.
                var localLoader = GetComponent<ShopTradeJsonLoader>();
                if (localLoader == null) localLoader = gameObject.AddComponent<ShopTradeJsonLoader>();
                localLoader.shopManager = this;
                if (string.IsNullOrEmpty(localLoader.tradeJsonFile))
                    localLoader.tradeJsonFile = "NunoTrades";
                localLoader.LoadAndApplyTrades();
            }
        }
    }
    
    void OnDestroy()
    {
        if (playerInventory != null)
            playerInventory.OnInventoryChanged -= OnInventoryChanged;
    }
    
    private void EnsureShopUI()
    {
        if (shopPanel != null && tradeListParent != null)
        {
            // UI exists; still ensure we have a usable slot prefab
            if (tradeSlotPrefab == null)
            {
                Debug.LogWarning("NunoShopManager: tradeSlotPrefab not set. Creating minimal prefab at runtime.");
                CreateDefaultTradeSlotPrefab();
            }
            EnsureCloseButtonHooked();
            EnsureTradeListLayout();
            return;
        }
        
        // Try to find existing UI in children first
        if (shopPanel == null)
        {
            var existing = transform.GetComponentInChildren<Canvas>(true);
            if (existing != null)
            {
                shopPanel = existing.gameObject;
                if (tradeListParent == null)
                {
                    var list = shopPanel.transform.Find("TradeListParent");
                    if (list != null) tradeListParent = list;
                }
            }
        }

        // If we found an existing UI, ensure the trade slot prefab too
        if (shopPanel != null && tradeListParent != null && tradeSlotPrefab == null)
        {
            Debug.LogWarning("NunoShopManager: tradeSlotPrefab not set. Creating minimal prefab at runtime.");
            CreateDefaultTradeSlotPrefab();
            EnsureCloseButtonHooked();
            EnsureTradeListLayout();
            return;
        }
        
        // If still no UI found, create one
        if (shopPanel == null || tradeListParent == null)
        {
            Debug.LogWarning("NunoShopManager: Creating default shop UI at runtime. Consider setting up UI in editor.");
            CreateDefaultShopUI();
        }
        
        EnsureCloseButtonHooked();
    }

    private void EnsureTradeListLayout()
    {
        if (tradeListParent == null) return;

        // If the assigned tradeListParent has a ScrollRect on it, treat it as the scroll root
        // and create the standard Viewport/Content hierarchy, then re-point tradeListParent
        // to the Content so rows have a proper layout container.
        var rootScroll = tradeListParent.GetComponent<ScrollRect>();
        if (rootScroll != null)
        {
            var rootRect = tradeListParent as RectTransform;
            if (rootRect != null)
            {
                RectTransform viewportRect = rootScroll.viewport;
                if (viewportRect == null)
                {
                    var viewportGO = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
                    viewportGO.transform.SetParent(rootRect, false);
                    viewportRect = viewportGO.GetComponent<RectTransform>();
                    viewportRect.anchorMin = Vector2.zero;
                    viewportRect.anchorMax = Vector2.one;
                    viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;
                    var vpImage = viewportGO.GetComponent<Image>();
                    vpImage.color = new Color(0, 0, 0, 0.01f);
                    rootScroll.viewport = viewportRect;
                }

                if (rootScroll.content == null || rootScroll.content == rootRect)
                {
                    var contentGO = new GameObject("Content", typeof(RectTransform));
                    contentGO.transform.SetParent(viewportRect, false);
                    var contentRect = contentGO.GetComponent<RectTransform>();
                    contentRect.anchorMin = new Vector2(0, 1);
                    contentRect.anchorMax = new Vector2(1, 1);
                    contentRect.pivot = new Vector2(0.5f, 1);
                    contentRect.offsetMin = Vector2.zero;
                    contentRect.offsetMax = Vector2.zero;
                    rootScroll.content = contentRect;
                    tradeListParent = contentRect;
                }
                else
                {
                    tradeListParent = rootScroll.content;
                }
            }
        }

        var vlg = tradeListParent.GetComponent<VerticalLayoutGroup>();
        if (vlg == null)
        {
            vlg = tradeListParent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8;
            vlg.padding = new RectOffset(12, 12, 12, 12);
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
        }
        
        var fitter = tradeListParent.GetComponent<ContentSizeFitter>();
        if (fitter == null)
        {
            fitter = tradeListParent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
        
        var scroll = tradeListParent.GetComponentInParent<ScrollRect>();
        var listRectTransform = tradeListParent as RectTransform;
        if (scroll != null && listRectTransform != null)
        {
            if (scroll.content == null)
                scroll.content = listRectTransform;
            
            if (scroll.viewport == null)
            {
                var viewport = scroll.transform as RectTransform;
                if (viewport != null)
                    scroll.viewport = viewport;
            }
            
            scroll.horizontal = false;
            scroll.vertical = true;
        }
    }

    private void EnsureCloseButtonHooked()
    {
        if (closeButton == null && shopPanel != null)
        {
            // try to find an existing close button in the assigned shop panel
            var candidates = shopPanel.GetComponentsInChildren<Button>(true);
            foreach (var btn in candidates)
            {
                var label = btn.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null && label.text != null &&
                    label.text.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    closeButton = btn;
                    break;
                }
            }
        }
        
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(CloseShop);
            closeButton.onClick.AddListener(CloseShop);
        }
    }
    
    private void CreateDefaultShopUI()
    {
        // Create canvas for local player (Screen Space - Overlay)
        var canvasGO = new GameObject("NunoShop_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        
        // Don't parent to manager (Screen Space - Overlay renders independently)
        shopPanel = canvasGO;
        
        // Create background panel
        var bgPanel = new GameObject("Background", typeof(Image));
        bgPanel.transform.SetParent(canvasGO.transform, false);
        var bgImg = bgPanel.GetComponent<Image>();
        bgImg.color = new Color(0.1f, 0.1f, 0.15f, 0.95f);
        var bgRect = bgPanel.GetComponent<RectTransform>();
        bgRect.anchorMin = new Vector2(0.2f, 0.1f);
        bgRect.anchorMax = new Vector2(0.8f, 0.9f);
        bgRect.offsetMin = bgRect.offsetMax = Vector2.zero;
        
        // Create title
        var titleGO = new GameObject("Title", typeof(TextMeshProUGUI));
        titleGO.transform.SetParent(bgPanel.transform, false);
        var titleText = titleGO.GetComponent<TextMeshProUGUI>();
        titleText.text = "Nuno's Trade Shop";
        titleText.fontSize = 36;
        titleText.alignment = TextAlignmentOptions.Center;
        var titleRect = titleGO.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.05f, 0.85f);
        titleRect.anchorMax = new Vector2(0.95f, 0.98f);
        titleRect.offsetMin = titleRect.offsetMax = Vector2.zero;
        
        var headerGO = new GameObject("ColumnHeader");
        headerGO.transform.SetParent(bgPanel.transform, false);
        var headerRect = headerGO.AddComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0.05f, 0.76f);
        headerRect.anchorMax = new Vector2(0.95f, 0.81f);
        headerRect.offsetMin = headerRect.offsetMax = Vector2.zero;
        var headerHLG = headerGO.AddComponent<HorizontalLayoutGroup>();
        headerHLG.spacing = 16;
        headerHLG.padding = new RectOffset(12, 4, 12, 4);
        headerHLG.childAlignment = TextAnchor.MiddleLeft;
        headerHLG.childForceExpandWidth = false;
        headerHLG.childForceExpandHeight = true;
        headerHLG.childControlWidth = true;
        var reqHeader = new GameObject("RequiredHeader", typeof(TextMeshProUGUI));
        reqHeader.transform.SetParent(headerGO.transform, false);
        reqHeader.GetComponent<TextMeshProUGUI>().text = "Required";
        reqHeader.GetComponent<TextMeshProUGUI>().fontSize = 14;
        reqHeader.GetComponent<TextMeshProUGUI>().fontStyle = FontStyles.Bold;
        var reqHdrLE = reqHeader.AddComponent<LayoutElement>();
        reqHdrLE.preferredWidth = 200;
        reqHdrLE.minWidth = 200;
        reqHdrLE.flexibleWidth = 1;
        var rewHeader = new GameObject("RewardHeader", typeof(TextMeshProUGUI));
        rewHeader.transform.SetParent(headerGO.transform, false);
        rewHeader.GetComponent<TextMeshProUGUI>().text = "Reward";
        rewHeader.GetComponent<TextMeshProUGUI>().fontSize = 14;
        rewHeader.GetComponent<TextMeshProUGUI>().fontStyle = FontStyles.Bold;
        var rewHdrLE = rewHeader.AddComponent<LayoutElement>();
        rewHdrLE.preferredWidth = 200;
        rewHdrLE.minWidth = 200;
        rewHdrLE.flexibleWidth = 1;
        var btnSpacer = new GameObject("BtnSpacer");
        btnSpacer.transform.SetParent(headerGO.transform, false);
        btnSpacer.AddComponent<LayoutElement>().preferredWidth = 90;
        
        var listGO = new GameObject("TradeListParent");
        listGO.transform.SetParent(bgPanel.transform, false);
        var scrollRect = listGO.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(listGO.transform, false);
        viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.01f);
        var viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;
        var content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        var contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.offsetMin = new Vector2(0, 0);
        contentRect.offsetMax = new Vector2(0, 0);
        scrollRect.content = contentRect;
        scrollRect.viewport = viewportRect;
        var vlg = content.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.padding = new RectOffset(12, 12, 12, 12);
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        tradeListParent = content.transform;
        var listRect = listGO.GetComponent<RectTransform>();
        listRect.anchorMin = new Vector2(0.05f, 0.12f);
        listRect.anchorMax = new Vector2(0.95f, 0.75f);
        listRect.offsetMin = listRect.offsetMax = Vector2.zero;
        
        // Create close button
        var closeBtnGO = new GameObject("CloseButton", typeof(Button), typeof(Image));
        closeBtnGO.transform.SetParent(bgPanel.transform, false);
        var closeBtn = closeBtnGO.GetComponent<Button>();
        closeButton = closeBtn;
        closeBtn.onClick.RemoveAllListeners();
        closeBtn.onClick.AddListener(CloseShop);
        var btnImg = closeBtnGO.GetComponent<Image>();
        btnImg.color = new Color(0.3f, 0.1f, 0.1f);
        var btnRect = closeBtnGO.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.4f, 0.05f);
        btnRect.anchorMax = new Vector2(0.6f, 0.12f);
        btnRect.offsetMin = btnRect.offsetMax = Vector2.zero;
        
        var btnTextGO = new GameObject("Label", typeof(TextMeshProUGUI));
        btnTextGO.transform.SetParent(closeBtnGO.transform, false);
        var btnText = btnTextGO.GetComponent<TextMeshProUGUI>();
        btnText.text = "Close (ESC)";
        btnText.alignment = TextAlignmentOptions.Center;
        btnText.fontSize = 20;
        var textRect = btnTextGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        
        // Create a simple trade slot prefab if none exists
        if (tradeSlotPrefab == null)
        {
            Debug.LogWarning("NunoShopManager: tradeSlotPrefab not set. Creating minimal prefab at runtime.");
            CreateDefaultTradeSlotPrefab();
        }
    }
    
    private void CreateDefaultTradeSlotPrefab()
    {
        var slotGO = new GameObject("DefaultTradeSlot");
        var image = slotGO.AddComponent<Image>();
        image.color = new Color(0.22f, 0.25f, 0.3f, 0.95f);
        var slotLayout = slotGO.AddComponent<LayoutElement>();
        slotLayout.preferredHeight = 80;
        slotLayout.flexibleWidth = 1;
        var slotUI = slotGO.AddComponent<NunoTradeSlotUI>();
        
        var hlg = slotGO.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 16;
        hlg.padding = new RectOffset(12, 6, 12, 6);
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        
        var requiredSection = new GameObject("RequiredSection");
        requiredSection.transform.SetParent(slotGO.transform, false);
        var reqSecHLG = requiredSection.AddComponent<HorizontalLayoutGroup>();
        reqSecHLG.spacing = 6;
        reqSecHLG.childAlignment = TextAnchor.MiddleLeft;
        reqSecHLG.childForceExpandWidth = false;
        reqSecHLG.childForceExpandHeight = true;
        reqSecHLG.childControlWidth = true;
        reqSecHLG.childControlHeight = true;
        var reqSecLE = requiredSection.AddComponent<LayoutElement>();
        reqSecLE.preferredWidth = 200;
        reqSecLE.minWidth = 200;
        reqSecLE.flexibleWidth = 1;
        
        var reqIconGO = new GameObject("RequiredIcon", typeof(Image));
        reqIconGO.transform.SetParent(requiredSection.transform, false);
        slotUI.requiredIcon = reqIconGO.GetComponent<Image>();
        reqIconGO.GetComponent<Image>().color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        var reqIconLE = reqIconGO.AddComponent<LayoutElement>();
        reqIconLE.preferredWidth = reqIconLE.preferredHeight = 40;
        
        var reqTextGO = new GameObject("RequiredText", typeof(TextMeshProUGUI));
        reqTextGO.transform.SetParent(requiredSection.transform, false);
        slotUI.requiredText = reqTextGO.GetComponent<TextMeshProUGUI>();
        slotUI.requiredText.fontSize = 15;
        slotUI.requiredText.enableWordWrapping = true;
        slotUI.requiredText.overflowMode = TMPro.TextOverflowModes.Overflow;
        var reqTextLE = reqTextGO.AddComponent<LayoutElement>();
        reqTextLE.flexibleWidth = 1;
        reqTextLE.minWidth = 60;
        
        var rewardSection = new GameObject("RewardSection");
        rewardSection.transform.SetParent(slotGO.transform, false);
        var rewSecHLG = rewardSection.AddComponent<HorizontalLayoutGroup>();
        rewSecHLG.spacing = 6;
        rewSecHLG.childAlignment = TextAnchor.MiddleLeft;
        rewSecHLG.childForceExpandWidth = false;
        rewSecHLG.childForceExpandHeight = true;
        rewSecHLG.childControlWidth = true;
        rewSecHLG.childControlHeight = true;
        var rewSecLE = rewardSection.AddComponent<LayoutElement>();
        rewSecLE.preferredWidth = 200;
        rewSecLE.minWidth = 200;
        rewSecLE.flexibleWidth = 1;
        
        var rewIconGO = new GameObject("RewardIcon", typeof(Image));
        rewIconGO.transform.SetParent(rewardSection.transform, false);
        slotUI.rewardIcon = rewIconGO.GetComponent<Image>();
        rewIconGO.GetComponent<Image>().color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        var rewIconLE = rewIconGO.AddComponent<LayoutElement>();
        rewIconLE.preferredWidth = rewIconLE.preferredHeight = 40;
        
        var rewTextGO = new GameObject("RewardText", typeof(TextMeshProUGUI));
        rewTextGO.transform.SetParent(rewardSection.transform, false);
        slotUI.rewardText = rewTextGO.GetComponent<TextMeshProUGUI>();
        slotUI.rewardText.fontSize = 15;
        slotUI.rewardText.enableWordWrapping = true;
        slotUI.rewardText.overflowMode = TMPro.TextOverflowModes.Overflow;
        var rewTextLE = rewTextGO.AddComponent<LayoutElement>();
        rewTextLE.flexibleWidth = 1;
        rewTextLE.minWidth = 60;
        
        var btnGO = new GameObject("TradeButton", typeof(Button), typeof(Image));
        btnGO.transform.SetParent(slotGO.transform, false);
        slotUI.tradeButton = btnGO.GetComponent<Button>();
        var btnImg = btnGO.GetComponent<Image>();
        btnImg.color = new Color(0.15f, 0.4f, 0.2f);
        var btnLE = btnGO.AddComponent<LayoutElement>();
        btnLE.preferredWidth = 90;
        btnLE.preferredHeight = 36;
        btnLE.flexibleWidth = 0;
        
        var btnTextGO = new GameObject("Label", typeof(TextMeshProUGUI));
        btnTextGO.transform.SetParent(btnGO.transform, false);
        var btnText = btnTextGO.GetComponent<TextMeshProUGUI>();
        btnText.text = "Trade";
        btnText.fontSize = 14;
        btnText.alignment = TextAlignmentOptions.Center;
        var btnTextRect = btnTextGO.GetComponent<RectTransform>();
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.offsetMin = new Vector2(4, 2);
        btnTextRect.offsetMax = new Vector2(-4, -2);
        
        var overlayGO = new GameObject("DisabledOverlay", typeof(Image));
        overlayGO.transform.SetParent(slotGO.transform, false);
        overlayGO.transform.SetAsLastSibling();
        slotUI.disabledOverlay = overlayGO;
        overlayGO.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);
        var overlayRect = overlayGO.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;
        overlayGO.AddComponent<LayoutElement>().ignoreLayout = true;
        
        tradeSlotPrefab = slotGO;
        slotGO.SetActive(false);
    }
    
    private void EnsureReferences()
    {
        if (playerInventory == null)
        {
            playerInventory = Inventory.FindLocalInventory();
            if (playerInventory != null)
                playerInventory.OnInventoryChanged += OnInventoryChanged;
        }
        if (questManager == null)
            questManager = FindFirstObjectByType<QuestManager>();
    }
    
    private void OnInventoryChanged()
    {
        if (isOpen)
            RefreshTradeList();
    }
    
    public void OpenShop()
    {
        OpenShop(availableTrades);
    }
    
    public void OpenShop(ShopTradeData[] tradesToShow)
    {
        EnsureReferences();
        
        // gate the entire shop until the Chapter 2 quest "Return to the Nuno" is completed
        if (questManager != null)
        {
            bool canOpenShop = false;
            var quests = questManager.quests;
            if (quests != null)
            {
                for (int i = 0; i < quests.Length; i++)
                {
                    var q = quests[i];
                    if (q == null || !q.isCompleted) continue;
                    if (string.Equals(q.questName, "Return to the Nuno", StringComparison.OrdinalIgnoreCase))
                    {
                        canOpenShop = true;
                        break;
                    }
                }
            }

            if (!canOpenShop)
            {
                Debug.Log("[NunoShopManager] Shop is locked until quest 'Return to the Nuno' is completed.");
                return;
            }
        }

        EnsureShopUI();
        
        if (!LocalUIManager.Ensure().TryOpen("NunoShop"))
        {
            Debug.Log("NunoShop: another UI is open; cannot open shop");
            return;
        }
        
        currentActiveTrades = (tradesToShow != null && tradesToShow.Length > 0) ? tradesToShow : availableTrades;
        
        isOpen = true;
        if (shopPanel != null)
            shopPanel.SetActive(true);
            
        CheckTradeUnlocks();
        RefreshTradeList();
        
        if (inputLockToken == 0)
            inputLockToken = LocalInputLocker.Ensure().Acquire("NunoShop", lockMovement:false, lockCombat:true, lockCamera:true, cursorUnlock:true);
    }
    
    public void CloseShop()
    {
        isOpen = false;
        if (shopPanel != null)
            shopPanel.SetActive(false);
            
        LocalUIManager.Instance?.Close("NunoShop");
        
        if (inputLockToken != 0)
        {
            LocalInputLocker.Ensure().Release(inputLockToken);
            inputLockToken = 0;
        }
        
        LocalInputLocker.Ensure().ForceGameplayCursor();
    }
    
    void Update()
    {
        if (isOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            CloseShop();
        }
    }
    
    public void ExecuteTrade(int tradeIndex)
    {
        EnsureReferences();
        
        if (currentActiveTrades == null) currentActiveTrades = availableTrades;
        if (tradeIndex < 0 || tradeIndex >= currentActiveTrades.Length) return;
        
        // Check local player authority
        if (playerPhotonView == null && playerInventory != null)
            playerPhotonView = playerInventory.GetComponent<PhotonView>();
        if (playerPhotonView != null && !playerPhotonView.IsMine) return;
        
        ShopTradeData trade = currentActiveTrades[tradeIndex];
        if (trade == null || !trade.CanTrade) return;
        
        if (!HasRequiredItems(trade))
        {
            Debug.Log($"Cannot complete trade: missing required items");
            return;
        }
        
        RemoveRequiredItems(trade);
        AddRewardItem(trade);
        
        trade.RecordUse();
        RefreshTradeList();
        
        Debug.Log($"Completed trade: {trade.tradeName}");
    }
    
    public void OnCloseButton()
    {
        CloseShop();
    }
    
    private bool HasRequiredItems(ShopTradeData trade)
    {
        if (playerInventory == null || trade.requiredItems == null) return false;
        
        for (int i = 0; i < trade.requiredItems.Length; i++)
        {
            if (trade.requiredItems[i] == null) continue;
            if (!playerInventory.HasItem(trade.requiredItems[i], trade.requiredQuantities[i]))
                return false;
        }
        
        return true;
    }
    
    private void RemoveRequiredItems(ShopTradeData trade)
    {
        if (playerInventory == null || trade.requiredItems == null) return;
        
        for (int i = 0; i < trade.requiredItems.Length; i++)
        {
            if (trade.requiredItems[i] == null) continue;
            playerInventory.RemoveItem(trade.requiredItems[i], trade.requiredQuantities[i]);
        }
    }
    
    private void AddRewardItem(ShopTradeData trade)
    {
        if (playerInventory == null || trade.rewardItem == null) return;
        playerInventory.AddItem(trade.rewardItem, trade.rewardQuantity);
    }
    
    private void CheckTradeUnlocks()
    {
        if (currentActiveTrades == null) currentActiveTrades = availableTrades;
        if (currentActiveTrades == null) return;
        
        foreach (var trade in currentActiveTrades)
        {
            if (trade == null || !trade.requiresUnlock) continue;
            
            trade.isUnlocked = CheckUnlockConditions(trade);
        }
    }
    
    private bool CheckUnlockConditions(ShopTradeData trade)
    {
        bool shrineUnlocked = true;
        if (trade.requiredShrineIds != null && trade.requiredShrineIds.Length > 0)
        {
            shrineUnlocked = false;
            // TODO: Check if shrines are cleansed (requires shrine completion tracking)
        }
        
        bool questUnlocked = true;
        if (trade.requiredQuestIds != null && trade.requiredQuestIds.Length > 0 && questManager != null)
        {
            questUnlocked = false;
            foreach (var questIdStr in trade.requiredQuestIds)
            {
                if (string.IsNullOrEmpty(questIdStr)) continue;

                // first, try to parse as numeric questID
                if (int.TryParse(questIdStr, out int questId))
                {
                    var questById = questManager.GetQuestByID(questId);
                    if (questById != null && questById.isCompleted)
                    {
                        questUnlocked = true;
                        break;
                    }
                }
                else
                {
                    // fallback: treat string as questName for JSON-defined quests (e.g. \"Return to the Nuno\")
                    var quests = questManager.quests;
                    if (quests != null)
                    {
                        for (int i = 0; i < quests.Length; i++)
                        {
                            var q = quests[i];
                            if (q == null || !q.isCompleted) continue;
                            if (string.Equals(q.questName, questIdStr, StringComparison.OrdinalIgnoreCase))
                            {
                                questUnlocked = true;
                                break;
                            }
                        }
                    }

                    if (questUnlocked)
                        break;
                }
            }
        }
        
        return shrineUnlocked && questUnlocked;
    }
    
    private void RefreshTradeList()
    {
        if (currentActiveTrades == null) currentActiveTrades = availableTrades;
        if (tradeListParent == null || tradeSlotPrefab == null || currentActiveTrades == null) return;
        
        foreach (Transform child in tradeListParent)
        {
            Destroy(child.gameObject);
        }
        
        for (int i = 0; i < currentActiveTrades.Length; i++)
        {
            ShopTradeData trade = currentActiveTrades[i];
            if (trade == null) continue;
            
            GameObject slot = Instantiate(tradeSlotPrefab, tradeListParent);
            slot.SetActive(true);
            var ui = slot.GetComponent<NunoTradeSlotUI>();
            if (ui != null)
                ui.Initialize(trade, i, this);
        }
    }
}

