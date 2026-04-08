using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class ItemPickupNotification : MonoBehaviour
{
    [Header("UI Setup")]
    [Tooltip("Optional: Prefab asset for notification entries. If null, notifications will be auto-created. The prefab should have an Image (for icon) and TextMeshProUGUI (for text) as children.")]
    public GameObject notificationPrefab;
    [Tooltip("Parent transform to spawn notifications under (should be a Canvas or child of Canvas). If null, will use this transform.")]
    public Transform notificationParent;
    
    [Header("Layout Settings")]
    [Tooltip("Vertical spacing between notifications")]
    public float notificationSpacing = 60f;
    [Tooltip("Maximum number of simultaneous notifications")]
    public int maxNotifications = 5;
    
    [Header("Settings")]
    [Tooltip("How long each notification stays visible before fading")]
    public float displayDuration = 2f;
    [Tooltip("How long the fade-out takes")]
    public float fadeDuration = 1f;
    
    private Inventory playerInventory;
    private Photon.Pun.PhotonView pv;
    private Queue<NotificationEntry> notificationQueue = new Queue<NotificationEntry>();
    private List<NotificationEntry> activeNotifications = new List<NotificationEntry>();
    private bool isProcessingQueue = false;
    
    [System.Serializable]
    private class NotificationEntry
    {
        public ItemData item;
        public int quantity;
        public GameObject uiObject;
        public Image iconImage;
        public TextMeshProUGUI text;
        public RectTransform rectTransform;
        public Coroutine fadeCoroutine;
    }
    
    void Awake()
    {
        pv = GetComponentInParent<Photon.Pun.PhotonView>();
        
        if (notificationParent == null)
            notificationParent = transform;
    }
    
    void Start()
    {
        FindInventory();
        
        if (playerInventory != null)
        {
            playerInventory.OnItemAdded += OnItemAdded;
        }
    }
    
    void OnDestroy()
    {
        if (playerInventory != null)
        {
            playerInventory.OnItemAdded -= OnItemAdded;
        }
        
        foreach (var entry in activeNotifications)
        {
            if (entry.fadeCoroutine != null && entry.uiObject != null)
                StopCoroutine(entry.fadeCoroutine);
        }
    }
    
    private void FindInventory()
    {
        if (pv != null && !pv.IsMine)
        {
            return;
        }
        
        playerInventory = Inventory.FindLocalInventory();
        if (playerInventory == null)
        {
            var playerStats = FindFirstObjectByType<PlayerStats>();
            if (playerStats != null)
                playerInventory = playerStats.GetComponent<Inventory>();
        }
    }
    
    private void OnItemAdded(ItemData item, int quantity)
    {
        if (item == null) return;
        
        if (pv != null && !pv.IsMine) return;
        
        notificationQueue.Enqueue(new NotificationEntry { item = item, quantity = quantity });
        
        if (!isProcessingQueue)
        {
            StartCoroutine(ProcessNotificationQueue());
        }
    }
    
    private IEnumerator ProcessNotificationQueue()
    {
        isProcessingQueue = true;
        
        while (notificationQueue.Count > 0 || activeNotifications.Count > 0)
        {
            RemoveFinishedNotifications();
            
            if (activeNotifications.Count >= maxNotifications)
            {
                yield return new WaitForSeconds(0.1f);
                continue;
            }
            
            if (notificationQueue.Count > 0)
            {
                var entry = notificationQueue.Dequeue();
                CreateNotification(entry);
            }
            
            yield return new WaitForSeconds(0.1f);
        }
        
        isProcessingQueue = false;
    }
    
    private void RemoveFinishedNotifications()
    {
        for (int i = activeNotifications.Count - 1; i >= 0; i--)
        {
            var entry = activeNotifications[i];
            if (entry.uiObject == null || !entry.uiObject.activeSelf)
            {
                activeNotifications.RemoveAt(i);
            }
        }
    }
    
    private void CreateNotification(NotificationEntry entry)
    {
        GameObject notificationObj;
        
        if (notificationPrefab != null)
        {
            notificationObj = Instantiate(notificationPrefab, notificationParent);
        }
        else
        {
            notificationObj = CreateAutoNotificationUI();
        }
        
        entry.uiObject = notificationObj;
        entry.rectTransform = notificationObj.GetComponent<RectTransform>();
        if (entry.rectTransform == null)
            entry.rectTransform = notificationObj.AddComponent<RectTransform>();
        
        entry.iconImage = FindImageInChildren(notificationObj, skipBackground: true);
        entry.text = FindComponentInChildren<TextMeshProUGUI>(notificationObj);
        
        UpdateNotificationEntry(entry);
        
        activeNotifications.Add(entry);
        RepositionNotifications();
        
        entry.fadeCoroutine = StartCoroutine(CoShowAndFade(entry));
    }
    
    private GameObject CreateAutoNotificationUI()
    {
        GameObject notificationObj = new GameObject("ItemNotification");
        notificationObj.transform.SetParent(notificationParent, false);
        
        RectTransform rect = notificationObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(300f, 50f);
        rect.anchoredPosition = new Vector2(0f, -20f);
        
        Image bg = notificationObj.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.7f);
        
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(notificationObj.transform, false);
        RectTransform iconRect = iconObj.AddComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = new Vector2(25f, 0f);
        iconRect.sizeDelta = new Vector2(40f, 40f);
        Image iconImg = iconObj.AddComponent<Image>();
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(notificationObj.transform, false);
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.offsetMin = new Vector2(60f, 0f);
        textRect.offsetMax = new Vector2(-10f, 0f);
        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Left;
        text.fontSize = 20f;
        text.color = Color.white;
        text.raycastTarget = false;
        
        return notificationObj;
    }
    
    private Image FindImageInChildren(GameObject obj, bool skipBackground = false)
    {
        Image component = null;
        if (!skipBackground)
        {
            component = obj.GetComponent<Image>();
        }
        
        if (component == null)
        {
            Image[] images = obj.GetComponentsInChildren<Image>();
            foreach (var img in images)
            {
                if (img == null) continue;
                if (skipBackground && img.transform.gameObject == obj)
                    continue;
                component = img;
                break;
            }
        }
        return component;
    }
    
    private T FindComponentInChildren<T>(GameObject obj) where T : Component
    {
        T component = obj.GetComponent<T>();
        if (component == null)
            component = obj.GetComponentInChildren<T>();
        return component;
    }
    
    private void UpdateNotificationEntry(NotificationEntry entry)
    {
        if (entry.iconImage != null && entry.item != null)
        {
            if (entry.item.icon != null)
            {
                entry.iconImage.sprite = entry.item.icon;
                entry.iconImage.enabled = true;
                entry.iconImage.color = Color.white;
            }
            else
            {
                entry.iconImage.enabled = false;
            }
        }
        
        if (entry.text != null && entry.item != null)
        {
            string message = entry.quantity > 1 
                ? $"Obtained {entry.item.itemName} x{entry.quantity}" 
                : $"Obtained {entry.item.itemName}";
            entry.text.text = message;
        }
    }
    
    private void RepositionNotifications()
    {
        float baseOffset = -20f;
        for (int i = 0; i < activeNotifications.Count; i++)
        {
            var entry = activeNotifications[i];
            if (entry.uiObject != null && entry.rectTransform != null)
            {
                float yPos = baseOffset - (i * notificationSpacing);
                entry.rectTransform.anchoredPosition = new Vector2(0f, yPos);
                entry.rectTransform.anchorMin = new Vector2(0.5f, 1f);
                entry.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                entry.rectTransform.pivot = new Vector2(0.5f, 1f);
            }
        }
    }
    
    private IEnumerator CoShowAndFade(NotificationEntry entry)
    {
        if (entry.uiObject == null || entry.text == null) yield break;
        
        CanvasGroup canvasGroup = entry.uiObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = entry.uiObject.AddComponent<CanvasGroup>();
        
        canvasGroup.alpha = 1f;
        entry.uiObject.SetActive(true);
        
        yield return new WaitForSeconds(displayDuration);
        
        float elapsed = 0f;
        
        while (elapsed < fadeDuration && entry.uiObject != null)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, t);
            yield return null;
        }
        
        if (entry.uiObject != null)
        {
            entry.uiObject.SetActive(false);
            Destroy(entry.uiObject);
        }
        
        activeNotifications.Remove(entry);
        RepositionNotifications();
        entry.fadeCoroutine = null;
    }
}

