using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Serialization;

public class EncyclopediaUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject encyclopediaPanel;
    public GameObject tocPagePanel;
    public ScrollRect tocScrollView;
    public Transform entryListParent;
    public GameObject entryRowPrefab;
    public GameObject entryDetailPanel;
    
    [Header("Detail Panel")]
    public TextMeshProUGUI detailNameText;
    public TextMeshProUGUI detailDescriptionText;
    [FormerlySerializedAs("detailLoreText")]
    public TextMeshProUGUI detailPowerStealingText;
    public Image detailIconImage;
    public Image detailFullImage;
    public TextMeshProUGUI detailStatsText;

    [Header("Visual Tuning")]
    public Color encounteredIconColor = Color.black;
    
    [Header("Controls")]
    public KeyCode toggleKey = KeyCode.V;
    public Button closeButton;
    public Button previousButton;
    public Button nextButton;
    public Button bookmarkButton;

    [Header("Audio")]
    public AudioSource uiAudioSource;
    public AudioClip pageSwapSfx;

    [Header("Menu Mode")]
    [Tooltip("Enable on home screen / menu scenes where PhotonView, LocalUIManager, and LocalInputLocker are not available.")]
    public bool menuMode = false;
    [Tooltip("When menu mode is enabled, keep cursor visible and unlocked while encyclopedia is open.")]
    public bool keepCursorUnlockedInMenuMode = true;
    
    private int _inputLockToken = 0;
    private bool isOpen = false;
    private List<EncyclopediaEntry> unlockedEntries = new List<EncyclopediaEntry>();
    private List<EncyclopediaEntry> orderedEntries = new List<EncyclopediaEntry>();
    private Transform tocContentParent;
    private int currentEntryIndex = -1;
    private bool controlsBound = false;

    private void PlayPageSwapSfx()
    {
        if (uiAudioSource == null || pageSwapSfx == null)
            return;

        uiAudioSource.PlayOneShot(pageSwapSfx);
    }
    
    void Start()
    {
        ResolveTocContentParent();

        if (encyclopediaPanel != null)
            encyclopediaPanel.SetActive(false);
        if (entryDetailPanel != null)
            entryDetailPanel.SetActive(false);

        EnsureControlsBound();

        if (EncyclopediaManager.Instance != null)
        {
            EncyclopediaManager.Instance.OnEntryUnlocked += OnEntryUnlocked;
            EncyclopediaManager.Instance.OnEnemyDiscovered += OnEnemyProgressChanged;
            EncyclopediaManager.Instance.OnEnemyKilled += OnEnemyProgressChanged;
        }
    }

    void Update()
    {
        if (menuMode)
        {
            if (isOpen && keepCursorUnlockedInMenuMode)
                EnsureMenuCursorUnlocked();
            return;
        }

        var photonView = GetComponentInParent<Photon.Pun.PhotonView>();
        if (photonView != null && !photonView.IsMine) return;

        if (Input.GetKeyDown(toggleKey))
        {
            if (!isOpen)
                OpenEncyclopedia();
            else
                CloseEncyclopedia();
        }
    }

    public void OpenEncyclopedia()
    {
        EnsureControlsBound();

        if (isOpen) return;
        
        // Skip UI locking in menu mode
        if (!menuMode)
        {
            var ui = LocalUIManager.Ensure();
            if (ui.IsAnyOpen && !ui.IsOwner("Encyclopedia"))
            {
                Debug.LogWarning("[EncyclopediaUI] Cannot open: another UI is already open");
                return;
            }
            
            if (!ui.TryOpen("Encyclopedia"))
                return;
        }
        
        isOpen = true;
        if (encyclopediaPanel != null)
            encyclopediaPanel.SetActive(true);

        if (menuMode && keepCursorUnlockedInMenuMode)
            EnsureMenuCursorUnlocked();
        
        RefreshEntryList();
        ShowTocPage();
        
        if (!menuMode && _inputLockToken == 0)
            _inputLockToken = LocalInputLocker.Ensure().Acquire("Encyclopedia", lockMovement: false, lockCombat: true, lockCamera: true, cursorUnlock: true);
    }
    
    public void CloseEncyclopedia()
    {
        // allow closing even if opened externally via SetActive fallback
        if (!isOpen)
        {
            if (encyclopediaPanel != null && encyclopediaPanel.activeSelf)
            {
                encyclopediaPanel.SetActive(false);
                currentEntryIndex = -1;
                SetPageMode(showToc: true);
            }

            if (menuMode && keepCursorUnlockedInMenuMode)
                EnsureMenuCursorUnlocked();

            return;
        }
        
        isOpen = false;
        if (encyclopediaPanel != null)
            encyclopediaPanel.SetActive(false);
        currentEntryIndex = -1;
        SetPageMode(showToc: true);
        
        if (!menuMode)
        {
            if (LocalUIManager.Instance != null)
                LocalUIManager.Instance.Close("Encyclopedia");
            
            if (_inputLockToken != 0)
            {
                LocalInputLocker.Ensure().Release(_inputLockToken);
                _inputLockToken = 0;
            }
            LocalInputLocker.Ensure().ForceGameplayCursor();
        }
        else if (keepCursorUnlockedInMenuMode)
        {
            EnsureMenuCursorUnlocked();
        }
    }
    
    private void RefreshEntryList()
    {
        ResolveTocContentParent();
        if (tocContentParent == null || EncyclopediaManager.Instance == null) return;
        
        // Clear existing entries
        foreach (Transform child in tocContentParent)
        {
            Destroy(child.gameObject);
        }
        
        var allEntries = EncyclopediaManager.Instance.allEntries;
        if (allEntries == null || allEntries.Length == 0)
        {
            orderedEntries.Clear();
            ShowNoEntriesMessage();
            UpdateNavigationButtons();
            return;
        }

        orderedEntries = allEntries
            .Where(e => e != null)
            .OrderBy(e => string.IsNullOrEmpty(e.displayName) ? e.enemyId : e.displayName)
            .ToList();
        
        unlockedEntries = EncyclopediaManager.Instance.GetUnlockedEntries();

        // Create TOC rows for every known entry
        foreach (var entry in orderedEntries)
        {
            CreateEntryRow(entry);
        }

        UpdateNavigationButtons();
    }

    private void ResolveTocContentParent()
    {
        if (tocContentParent != null) return;

        if (tocScrollView != null && tocScrollView.content != null)
        {
            tocContentParent = tocScrollView.content;
            if (entryListParent == null)
                entryListParent = tocContentParent;
            return;
        }

        if (entryListParent != null)
        {
            tocContentParent = entryListParent;
            return;
        }

        if (tocScrollView != null)
        {
            var content = tocScrollView.transform.Find("Viewport/Content");
            if (content != null)
            {
                tocContentParent = content;
                entryListParent = tocContentParent;
            }
        }
    }
    
    private void CreateEntryRow(EncyclopediaEntry entry)
    {
        if (entryRowPrefab == null)
        {
            // Create simple row if no prefab
            var go = new GameObject("EntryRow", typeof(RectTransform), typeof(Button));
            var button = go.GetComponent<Button>();
            var text = new GameObject("Text", typeof(TextMeshProUGUI));
            text.transform.SetParent(go.transform, false);
            var tmp = text.GetComponent<TextMeshProUGUI>();
            bool encountered = EncyclopediaManager.Instance != null && EncyclopediaManager.Instance.IsEntryEncountered(entry);
            tmp.text = encountered ? entry.displayName : "???";
            tmp.alignment = TextAlignmentOptions.Left;
            
            button.onClick.AddListener(() => OpenEntryFromToc(entry));
            go.transform.SetParent(tocContentParent, false);
            return;
        }
        
        var row = Instantiate(entryRowPrefab, tocContentParent);
        var rowButton = row.GetComponent<Button>();
        if (rowButton == null)
            rowButton = row.AddComponent<Button>();
        
        bool isEncountered = EncyclopediaManager.Instance != null && EncyclopediaManager.Instance.IsEntryEncountered(entry);

        // Find text component
        var nameText = row.GetComponentInChildren<TextMeshProUGUI>();
        if (nameText != null)
            nameText.text = isEncountered ? entry.displayName : "???";
        
        // Find icon image if exists
        var iconImage = row.GetComponentInChildren<Image>();
        if (iconImage != null)
        {
            bool showIcon = isEncountered && entry.icon != null;
            iconImage.gameObject.SetActive(showIcon);
            if (showIcon)
            {
                iconImage.sprite = entry.icon;
                iconImage.color = encounteredIconColor;
            }
        }
        
        rowButton.onClick.AddListener(() => OpenEntryFromToc(entry));
    }

    private void OpenEntryFromToc(EncyclopediaEntry entry)
    {
        if (entry == null || EncyclopediaManager.Instance == null)
            return;

        currentEntryIndex = orderedEntries.IndexOf(entry);
        if (currentEntryIndex < 0)
            currentEntryIndex = 0;

        PlayPageSwapSfx();

        bool isEncountered = EncyclopediaManager.Instance.IsEntryEncountered(entry);
        ShowEntryDetail(entry, isEncountered);
    }
    
    private void ShowEntryDetail(EncyclopediaEntry entry, bool encountered)
    {
        if (entryDetailPanel == null || entry == null) return;

        SetPageMode(showToc: false);
        
        if (detailNameText != null)
            detailNameText.text = encountered ? entry.displayName : "???";
        
        if (detailDescriptionText != null)
            detailDescriptionText.text = encountered ? entry.description : "This entry has not been encountered yet.";

        bool killed = EncyclopediaManager.Instance != null && EncyclopediaManager.Instance.IsEntryKilled(entry);
        
        if (detailPowerStealingText != null)
        {
            detailPowerStealingText.text = killed && !string.IsNullOrEmpty(entry.powerStealingText)
                ? entry.powerStealingText
                : "???";
            detailPowerStealingText.gameObject.SetActive(true);
        }
        
        if (detailIconImage != null)
        {
            if (encountered && entry.icon != null)
            {
                detailIconImage.sprite = entry.icon;
                detailIconImage.color = encounteredIconColor;
                detailIconImage.gameObject.SetActive(true);
            }
            else
            {
                detailIconImage.gameObject.SetActive(false);
            }
        }
        
        if (detailFullImage != null)
        {
            if (encountered && entry.fullImage != null)
            {
                detailFullImage.sprite = entry.fullImage;
                detailFullImage.gameObject.SetActive(true);
            }
            else
            {
                detailFullImage.gameObject.SetActive(false);
            }
        }
        
        if (detailStatsText != null)
        {
            string stats = encountered
                ? $"Health: {entry.baseHealth}\nDamage: {entry.baseDamage}\nSpeed: {entry.moveSpeed:F1}"
                : "Health: ???\nDamage: ???\nSpeed: ???";
            detailStatsText.text = stats;
        }

        UpdateNavigationButtons();
    }

    public void OnPreviousClicked()
    {
        if (!isOpen && encyclopediaPanel != null && encyclopediaPanel.activeSelf)
            isOpen = true;

        if (!isOpen) return;

        if (currentEntryIndex < 0)
            return;

        if (currentEntryIndex == 0)
        {
            PlayPageSwapSfx();
            ShowTocPage();
            return;
        }

        currentEntryIndex--;
        PlayPageSwapSfx();
        ShowCurrentEntryByIndex();
    }

    public void OnNextClicked()
    {
        if (!isOpen && encyclopediaPanel != null && encyclopediaPanel.activeSelf)
            isOpen = true;

        if (!isOpen || orderedEntries == null || orderedEntries.Count == 0)
            return;

        if (currentEntryIndex < 0)
        {
            currentEntryIndex = 0;
            PlayPageSwapSfx();
            ShowCurrentEntryByIndex();
            return;
        }

        if (currentEntryIndex >= orderedEntries.Count - 1)
            return;

        currentEntryIndex++;
        PlayPageSwapSfx();
        ShowCurrentEntryByIndex();
    }

    public void ShowTocPage()
    {
        if (!isOpen && encyclopediaPanel != null && encyclopediaPanel.activeSelf)
            isOpen = true;

        bool wasOnEntry = currentEntryIndex >= 0;
        currentEntryIndex = -1;
        SetPageMode(showToc: true);
        UpdateNavigationButtons();

        if (wasOnEntry)
            PlayPageSwapSfx();
    }

    private void ShowCurrentEntryByIndex()
    {
        if (orderedEntries == null || orderedEntries.Count == 0)
            return;
        if (currentEntryIndex < 0 || currentEntryIndex >= orderedEntries.Count)
            return;
        if (EncyclopediaManager.Instance == null)
            return;

        var entry = orderedEntries[currentEntryIndex];
        bool encountered = EncyclopediaManager.Instance.IsEntryEncountered(entry);
        ShowEntryDetail(entry, encountered);
    }

    private void SetPageMode(bool showToc)
    {
        if (tocPagePanel != null)
            tocPagePanel.SetActive(showToc);

        if (entryDetailPanel != null)
            entryDetailPanel.SetActive(!showToc);
    }

    private void UpdateNavigationButtons()
    {
        bool isOnToc = currentEntryIndex < 0;
        bool hasEntries = orderedEntries != null && orderedEntries.Count > 0;

        if (previousButton != null)
            previousButton.interactable = !isOnToc;

        if (nextButton != null)
            nextButton.interactable = isOnToc ? hasEntries : currentEntryIndex < orderedEntries.Count - 1;

        if (bookmarkButton != null)
            bookmarkButton.interactable = !isOnToc;
    }
    
    private void ShowNoEntriesMessage()
    {
        var msgGO = new GameObject("NoEntriesMsg", typeof(RectTransform), typeof(TextMeshProUGUI));
        msgGO.transform.SetParent(tocContentParent, false);
        var tmp = msgGO.GetComponent<TextMeshProUGUI>();
        tmp.text = "No entries unlocked yet.\nEncounter and defeat enemies to unlock entries.";
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 18f;
        tmp.color = Color.black;
    }
    
    private void OnEntryUnlocked(EncyclopediaEntry entry)
    {
        // Refresh list if open
        if (isOpen)
        {
            RefreshEntryList();
        }
    }

    private void OnEnemyProgressChanged(string enemyId)
    {
        if (isOpen)
            RefreshEntryList();
    }

    private void EnsureControlsBound()
    {
        if (controlsBound)
            return;

        if (closeButton == null)
            closeButton = FindNamedButton("Close");
        if (previousButton == null)
            previousButton = FindNamedButton("Previous");
        if (nextButton == null)
            nextButton = FindNamedButton("Next");
        if (bookmarkButton == null)
            bookmarkButton = FindNamedButton("Bookmark");

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(CloseEncyclopedia);
            closeButton.onClick.AddListener(CloseEncyclopedia);
        }

        if (previousButton != null)
        {
            previousButton.onClick.RemoveListener(OnPreviousClicked);
            previousButton.onClick.AddListener(OnPreviousClicked);
        }

        if (nextButton != null)
        {
            nextButton.onClick.RemoveListener(OnNextClicked);
            nextButton.onClick.AddListener(OnNextClicked);
        }

        if (bookmarkButton != null)
        {
            bookmarkButton.onClick.RemoveListener(ShowTocPage);
            bookmarkButton.onClick.AddListener(ShowTocPage);
        }

        controlsBound = true;
    }

    private Button FindNamedButton(string textLabel)
    {
        var buttons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            var buttonText = buttons[i].GetComponentInChildren<TextMeshProUGUI>(true);
            if (buttonText != null && string.Equals(buttonText.text?.Trim(), textLabel, System.StringComparison.OrdinalIgnoreCase))
                return buttons[i];
        }

        return null;
    }

    private void EnsureMenuCursorUnlocked()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }
    
    void OnDestroy()
    {
        if (EncyclopediaManager.Instance != null)
        {
            EncyclopediaManager.Instance.OnEntryUnlocked -= OnEntryUnlocked;
            EncyclopediaManager.Instance.OnEnemyDiscovered -= OnEnemyProgressChanged;
            EncyclopediaManager.Instance.OnEnemyKilled -= OnEnemyProgressChanged;
        }
    }
}

