using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class QuestListUI : MonoBehaviour
{
    [Header("Bindings")]
    public GameObject panel;
    public Transform listParent;
    public GameObject rowPrefab; // Objective row prefab (e.g., your 'QuestRow') with Name/Description/Progress TMPs
    public KeyCode toggleKey = KeyCode.T;
    [SerializeField] private GameObject disableWhileOpen;

    [Header("Top-Right HUD")]
    [SerializeField] private TextMeshProUGUI hudObjectiveTitle;
    [SerializeField] private TextMeshProUGUI hudObjectiveDescription;
    [SerializeField] private RectTransform hudArrow;
    [Tooltip("Angle offset for arrow sprite (degrees).")]
    [SerializeField] private float hudArrowSpriteRotation = 0f;
    [Tooltip("How often to refresh the HUD and resolve arrow target (seconds).")]
    [SerializeField] private float hudRefreshInterval = 0.5f;

    [Header("HUD Pop Animation")]
    [Tooltip("UI element that briefly scales when a quest completes. Defaults to the HUD title if not set.")]
    [SerializeField] private RectTransform hudPopTarget;
    [Tooltip("How large the HUD briefly scales when a quest completes.")]
    [SerializeField] private float hudPopScale = 1.1f;
    [Tooltip("Total duration of the HUD pop animation in seconds.")]
    [SerializeField] private float hudPopDuration = 0.25f;
    [Tooltip("Curve used for the pop in/out interpolation (0-1 over half the duration).")]
    [SerializeField] private AnimationCurve hudPopCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Vector3 hudPopOriginalScale = Vector3.one;
    private Coroutine hudPopCoroutine;

    [Header("Data Source (optional)")]
    public QuestManager questManagerOverride;
    private QuestManager qm;
    private int _inputLockToken = 0;
    [Tooltip("When open, refresh the list each frame (useful while debugging)")]
    public bool refreshContinuouslyWhileOpen = false;
    [Header("Display")]
    [Tooltip("If enabled, hides quest titles/descriptions and shows only the objectives list.")]
    public bool showObjectivesOnly = true;

    [Header("Fonts")]
    [Tooltip("Font asset applied to all quest list text (titles, descriptions, progress). Leave empty to keep prefab defaults.")]
    [SerializeField] private TMP_FontAsset listFont;
    [Tooltip("Optional font asset for status/progress text (e.g. IN PROGRESS / COMPLETED). If empty, uses listFont.")]
    [SerializeField] private TMP_FontAsset statusFont;

    [Header("Font Sizes")]
    [Tooltip("Font size for quest titles in the list header rows.")]
    [SerializeField] private float questTitleFontSize = 32f;
    [Tooltip("Font size for quest descriptions in the list header rows.")]
    [SerializeField] private float questDescriptionFontSize = 24f;
    [Tooltip("Font size for objective rows (name and description).")]
    [SerializeField] private float objectiveTextFontSize = 18f;

    private Transform playerTransform;
    private Transform hudTarget;
    private float hudRefreshTimer;

    void Start()
    {
        if (panel != null) panel.SetActive(false);
        qm = QuestManager.Instance ?? FindFirstObjectByType<QuestManager>();
        WireRowPrefabIfMissing();
        // auto-create a minimal UI if not wired
        if (panel == null)
        {
            AutoCreateMinimalUI();
            if (panel != null) panel.SetActive(false); // ensure the runtime-created panel starts hidden
        }
        EnsureListLayout();
        
        // default HUD pop target to the HUD title's parent (full HUD container) if not assigned
        if (hudPopTarget == null && hudObjectiveTitle != null)
        {
            var parentRt = hudObjectiveTitle.transform.parent as RectTransform;
            hudPopTarget = parentRt != null ? parentRt : hudObjectiveTitle.rectTransform;
        }
        if (hudPopTarget != null)
            hudPopOriginalScale = hudPopTarget.localScale;
        
        var cam = Camera.main;
        if (cam != null && cam.transform.parent != null) playerTransform = cam.transform.parent;
        if (playerTransform == null && cam != null) playerTransform = cam.transform;
        SubscribeToQuestEvents();
        
        // Delayed refresh to catch quests that load after Start()
        StartCoroutine(DelayedInitialRefresh());
    }
    
    private System.Collections.IEnumerator DelayedInitialRefresh()
    {
        yield return new WaitForSeconds(0.5f);
        if (qm == null) qm = QuestManager.Instance ?? FindFirstObjectByType<QuestManager>();
        if (qm != null && panel != null && panel.activeInHierarchy)
        {
            Refresh();
        }
    }

    void OnDisable()
    {
        UnsubscribeFromQuestEvents();
    }

    void Update()
    {
        // local-only: ignore if attached to a non-local player object
        var photonView = GetComponentInParent<Photon.Pun.PhotonView>();
        if (photonView != null && !photonView.IsMine) return;
        
        if (Input.GetKeyDown(toggleKey))
        {
            bool open = panel != null && !panel.activeSelf;
            var ui = LocalUIManager.Ensure();
                if (open)
                {
                    // enforce strict exclusivity: do not open if any other UI is open
                    if (ui.IsAnyOpen && !ui.IsOwner("QuestList")) {
                        Debug.LogWarning("[QuestListUI] Cannot open: another UI is already open");
                        return;
                    }
                    if (!ui.TryOpen("QuestList")) 
                    {
                        Debug.LogWarning("[QuestListUI] TryOpen failed");
                        return;
                    }
                    if (panel == null)
                    {
                        Debug.LogError("[QuestListUI] Panel is null! Cannot open quest list.");
                        return;
                    }
                    panel.SetActive(true);
                    if (disableWhileOpen != null) disableWhileOpen.SetActive(false);
                    // Refresh quest manager reference before refreshing
                    if (qm == null) qm = QuestManager.Instance ?? FindFirstObjectByType<QuestManager>();
                    if (qm == null)
                    {
                        Debug.LogError("[QuestListUI] QuestManager not found! Cannot display quests.");
                        ShowNoQuestManagerMessage();
                    }
                    else
                    {
                        Refresh();
                    }
                    // partial lock: allow movement, lock combat and camera, unlock cursor
                    if (_inputLockToken == 0)
                        _inputLockToken = LocalInputLocker.Ensure().Acquire("QuestList", lockMovement:false, lockCombat:true, lockCamera:true, cursorUnlock:true);
                }
            else
            {
                // avoid disabling ourselves if the panel is the same GameObject this script is on
                if (panel != null && panel != this.gameObject) panel.SetActive(false);
                if (LocalUIManager.Instance != null) LocalUIManager.Instance.Close("QuestList");
                if (_inputLockToken != 0)
                {
                    LocalInputLocker.Ensure().Release(_inputLockToken);
                    _inputLockToken = 0;
                }
                if (disableWhileOpen != null) disableWhileOpen.SetActive(true);
                LocalInputLocker.Ensure().ForceGameplayCursor();
            }
        }

        if (refreshContinuouslyWhileOpen && panel != null && panel.activeInHierarchy)
        {
            Refresh();
        }

        hudRefreshTimer -= Time.deltaTime;
        if (hudRefreshTimer <= 0f)
        {
            hudRefreshTimer = hudRefreshInterval;
            ResolveHudTarget();
            UpdateHudDisplay();
        }
        UpdateHudArrowRotation();
    }

    public void Refresh()
    {
        if (questManagerOverride != null) qm = questManagerOverride;
        if (qm == null) qm = QuestManager.Instance;
        if (qm == null) qm = FindFirstObjectByType<QuestManager>();
        if (qm == null || listParent == null)
        {
            Debug.LogWarning("[QuestListUI] QuestManager or listParent is null");
            ShowNoQuestManagerMessage();
            return;
        }

        EnsureListLayout();

        foreach (Transform child in listParent)
            Destroy(child.gameObject);

        if (qm.quests == null || qm.quests.Length == 0)
        {
            Debug.LogWarning("[QuestListUI] QuestManager.quests is null or empty. Quest count: " + (qm.quests == null ? "null" : "0"));
            ShowNoQuestsMessage();
            return;
        }

        // Sort quests: current quest first, then completed quests
        var sortedQuests = new System.Collections.Generic.List<Quest>();
        var currentQuest = qm.GetCurrentQuest();
        
        // Add current quest first if it exists
        if (currentQuest != null && !currentQuest.isCompleted)
        {
            sortedQuests.Add(currentQuest);
        }
        
        // Add all completed quests
        for (int i = 0; i < qm.quests.Length; i++)
        {
            if (qm.quests[i] != null && qm.quests[i].isCompleted)
            {
                sortedQuests.Add(qm.quests[i]);
            }
        }

        if (sortedQuests.Count == 0)
        {
            ShowNoCurrentQuestMessage();
            return;
        }

        Debug.Log($"[QuestListUI] Rendering {sortedQuests.Count} quests (current: {currentQuest?.questName ?? "none"})");

        // Render each quest
        foreach (var quest in sortedQuests)
        {
            bool isCurrentQuest = (quest == currentQuest && !quest.isCompleted);
            string questStatus = isCurrentQuest ? "<color=yellow>IN PROGRESS</color>" : "<color=green>COMPLETED</color>";

            // Show quest name header with status
            var questHeaderRow = Instantiate(rowPrefab, listParent);
            EnsureRowLayout(questHeaderRow);

            TextMeshProUGUI headerNameText = null;
            TextMeshProUGUI headerProgressText = null;
            TextMeshProUGUI headerDescriptionText = null;
            var headerNameTf = questHeaderRow.transform.Find("QuestName") ?? questHeaderRow.transform.Find("Name") ?? questHeaderRow.transform.Find("quest/QuestName") ?? questHeaderRow.transform.Find("quest/Name");
            var headerDescTf = questHeaderRow.transform.Find("Description") ?? questHeaderRow.transform.Find("quest/Description");
            var headerProgTf = questHeaderRow.transform.Find("Progress")
                             ?? questHeaderRow.transform.Find("right/Progress")
                             ?? questHeaderRow.transform.Find("Right/Progress");
            if (headerNameTf != null) headerNameText = headerNameTf.GetComponent<TextMeshProUGUI>();
            if (headerDescTf != null) headerDescriptionText = headerDescTf.GetComponent<TextMeshProUGUI>();
            if (headerProgTf != null) headerProgressText = headerProgTf.GetComponent<TextMeshProUGUI>();

            // Fallback mapping
            if (headerNameText == null || headerProgressText == null)
            {
                var allTexts = questHeaderRow.GetComponentsInChildren<TextMeshProUGUI>(true);
                if (headerNameText == null && allTexts.Length > 0) headerNameText = allTexts[0];
                if (headerDescriptionText == null && allTexts.Length > 1) headerDescriptionText = allTexts[1];
                if (headerProgressText == null && allTexts.Length > 2) headerProgressText = allTexts[allTexts.Length - 1];
            }

            if (headerNameText != null)
            {
                headerNameText.text = quest.questName ?? "Unnamed Quest";
            }
            if (headerDescriptionText != null)
            {
                string desc = quest.description ?? "";
                headerDescriptionText.text = desc.TrimEnd('\n');
            }

            // Apply font sizes for the quest header row
            if (headerNameText != null && questTitleFontSize > 0f)
            {
                headerNameText.fontSize = questTitleFontSize;
            }
            if (headerDescriptionText != null && questDescriptionFontSize > 0f)
            {
                headerDescriptionText.fontSize = questDescriptionFontSize;
            }
            if (headerProgressText != null)
            {
                headerProgressText.text = questStatus;
            }

            // Show objectives if they exist
            if (quest.objectives != null && quest.objectives.Length > 0)
            {
                // Render each objective
                for (int i = 0; i < quest.objectives.Length; i++)
                {
                    var obj = quest.objectives[i];
                    var row = Instantiate(rowPrefab, listParent);
                    EnsureRowLayout(row);

                    TextMeshProUGUI nameText = null;
                    TextMeshProUGUI progressText = null;
                    TextMeshProUGUI descriptionText = null;
                    var nameTf = row.transform.Find("QuestName") ?? row.transform.Find("Name") ?? row.transform.Find("quest/QuestName") ?? row.transform.Find("quest/Name");
                    var descTf = row.transform.Find("Description") ?? row.transform.Find("quest/Description");
                    var progTf = row.transform.Find("Progress")
                                 ?? row.transform.Find("right/Progress")
                                 ?? row.transform.Find("Right/Progress");
                    if (nameTf != null) nameText = nameTf.GetComponent<TextMeshProUGUI>();
                    if (descTf != null) descriptionText = descTf.GetComponent<TextMeshProUGUI>();
                    if (progTf != null) progressText = progTf.GetComponent<TextMeshProUGUI>();

                    // Fallback mapping if prefab uses different hierarchy
                    if (nameText == null || progressText == null)
                    {
                        var allTexts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
                        if (nameText == null && allTexts.Length > 0) nameText = allTexts[0];
                        if (descriptionText == null && allTexts.Length > 1) descriptionText = allTexts[Mathf.Min(1, allTexts.Length - 1)];
                        if (progressText == null && allTexts.Length > 2) progressText = allTexts[allTexts.Length - 1];
                    }

                    // Populate row with objective data
                    if (nameText != null)
                    {
                        bool objComplete = obj.IsMultiItemCollect() ? obj.IsMultiItemCollectComplete() : obj.IsCompleted;
                        string status = objComplete ? "<color=green>✓</color>" : (isCurrentQuest && i == quest.currentObjectiveIndex ? "<color=yellow>○</color>" : "");
                        nameText.text = string.IsNullOrEmpty(status) ? obj.objectiveName : ($"{status} {obj.objectiveName}");
                        if (objectiveTextFontSize > 0f)
                            nameText.fontSize = objectiveTextFontSize;
                    }
                    if (descriptionText != null)
                    {
                        string desc = obj.description ?? string.Empty;
                        // Append multi-item progress details if applicable
                        if (obj.IsMultiItemCollect() && obj.collectItemIds != null && obj.collectQuantities != null)
                        {
                            if (!string.IsNullOrEmpty(desc)) desc += "\n";
                            for (int j = 0; j < obj.collectItemIds.Length && j < obj.collectQuantities.Length; j++)
                            {
                                int current = 0;
                                if (obj.collectProgress != null && j < obj.collectProgress.Length)
                                    current = obj.collectProgress[j];
                                int required = obj.collectQuantities[j];
                                desc += $"{obj.collectItemIds[j]}: {current}/{required}\n";
                            }
                        }
                        descriptionText.text = desc.TrimEnd('\n');
                        if (objectiveTextFontSize > 0f)
                            descriptionText.fontSize = objectiveTextFontSize;
                    }
                    if (progressText != null)
                    {
                        bool objComplete = obj.IsMultiItemCollect() ? obj.IsMultiItemCollectComplete() : obj.IsCompleted;
                        string progress = "";
                        
                        if (objComplete)
                        {
                            progress = "done";
                        }
                        else if (isCurrentQuest && i == quest.currentObjectiveIndex)
                        {
                            progress = obj.requiredCount > 1 ? $"{obj.currentCount}/{obj.requiredCount}" : "";
                        }
                        else
                        {
                            progress = obj.requiredCount > 1 ? $"{obj.currentCount}/{obj.requiredCount}" : "";
                        }
                        progressText.text = progress;
                    }
                }
            }
        }
    }

    private string FormatProgress(Quest q)
    {
        if (q == null) return string.Empty;
        if (q.requiredCount > 1)
            return $"{Mathf.Clamp(q.currentCount, 0, Mathf.Max(1, q.requiredCount))}/{Mathf.Max(1, q.requiredCount)}";
        return q.isCompleted ? "completed" : "in progress";
    }

    private void WireRowPrefabIfMissing()
    {
        if (rowPrefab != null) return;
        // create a simple row prefab at runtime: two TMP texts in a horizontal layout
        var go = new GameObject("QuestRow", typeof(RectTransform));
        var nameGO = new GameObject("Name", typeof(TextMeshProUGUI));
        var progressGO = new GameObject("Progress", typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(go.transform, false);
        progressGO.transform.SetParent(go.transform, false);
        var nameTMP = nameGO.GetComponent<TextMeshProUGUI>();
        var progTMP = progressGO.GetComponent<TextMeshProUGUI>();
        nameTMP.alignment = TextAlignmentOptions.Left;
        progTMP.alignment = TextAlignmentOptions.Right;
        rowPrefab = go;
    }

    private void AutoCreateMinimalUI()
    {
        var canvasGO = new GameObject("QuestList_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        panel = new GameObject("Panel", typeof(Image));
        panel.transform.SetParent(canvasGO.transform, false);
        var img = panel.GetComponent<Image>();
        img.color = new Color(0, 0, 0, 0.6f);
        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.6f, 0.1f);
        rect.anchorMax = new Vector2(0.95f, 0.9f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        var scrollGO = new GameObject("Scroll", typeof(ScrollRect));
        scrollGO.transform.SetParent(panel.transform, false);
        var scroll = scrollGO.GetComponent<ScrollRect>();
        var viewport = new GameObject("Viewport", typeof(RectMask2D), typeof(Image));
        viewport.transform.SetParent(scrollGO.transform, false);
        var viewportImg = viewport.GetComponent<Image>();
        if (viewportImg != null)
        {
            var c = viewportImg.color; c.a = 0f; viewportImg.color = c; // transparent so it doesn't cover UI
            viewportImg.raycastTarget = false;
        }
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = content.GetComponent<RectTransform>();
        listParent = content.transform;

        // provide a simple default row prefab
        WireRowPrefabIfMissing();
    }

    private void EnsureListLayout()
    {
        if (listParent == null) return;
        var vlg = listParent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        if (vlg == null)
        {
            vlg = listParent.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            Debug.Log("QuestListUI: Added VerticalLayoutGroup to listParent to stack rows");
        }
        var fitter = listParent.GetComponent<UnityEngine.UI.ContentSizeFitter>();
        if (fitter == null)
        {
            fitter = listParent.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        }
        // Optional: wire ScrollRect content if the parent has one
        var scroll = listParent.GetComponentInParent<UnityEngine.UI.ScrollRect>();
        if (scroll != null && scroll.content != (RectTransform)listParent)
        {
            scroll.content = (RectTransform)listParent;
        }
    }

    private void EnsureRowLayout(GameObject row)
    {
        if (row == null) return;
        var rt = row.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(0, rt.offsetMin.y);
            rt.offsetMax = new Vector2(0, rt.offsetMax.y);
        }
        var le = row.GetComponent<UnityEngine.UI.LayoutElement>();
        if (le == null) le = row.AddComponent<UnityEngine.UI.LayoutElement>();
        if (le.minHeight < 28f) le.minHeight = 36f;
        var hlg = row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        if (hlg == null)
        {
            hlg = row.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;
        }
        // configure children layout for Name and Progress
        var nameTf = row.transform.Find("Name") ?? row.transform.Find("quest/Name");
        var progTf = row.transform.Find("Progress");
        var descTf = row.transform.Find("Description") ?? row.transform.Find("quest/Description");
        // if there is a 'quest' container, make it a vertical stack for name+description
        var questContainer = row.transform.Find("quest");
        if (questContainer != null)
        {
            var v = questContainer.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() ?? questContainer.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            v.childAlignment = TextAnchor.UpperLeft;
            v.spacing = 2f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            var qle = questContainer.GetComponent<UnityEngine.UI.LayoutElement>() ?? questContainer.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            qle.flexibleWidth = 1;
            qle.preferredWidth = 0;
            var qfitter = questContainer.GetComponent<UnityEngine.UI.ContentSizeFitter>() ?? questContainer.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            qfitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            qfitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
        }
        if (nameTf != null)
        {
            var nameLE = nameTf.GetComponent<UnityEngine.UI.LayoutElement>() ?? nameTf.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            nameLE.flexibleWidth = 1;
            nameLE.preferredWidth = 0;
        }
        if (descTf != null)
        {
            var descLE = descTf.GetComponent<UnityEngine.UI.LayoutElement>() ?? descTf.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            descLE.flexibleWidth = 1;
            descLE.preferredWidth = 0;
        }
        if (progTf != null)
        {
            var progLE = progTf.GetComponent<UnityEngine.UI.LayoutElement>() ?? progTf.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            if (progLE.preferredWidth < 80f) progLE.preferredWidth = 120f;
            progLE.flexibleWidth = 0;
        }
        // ensure quest text wraps and grows vertically instead of overlapping other rows
        var allTexts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var tmp in allTexts)
        {
            if (tmp == null) continue;
            // normalize fonts so all quest rows use a consistent look
            if (listFont != null)
            {
                bool isProgress = tmp.gameObject.name == "Progress" || tmp.transform.parent.name == "Progress";
                if (isProgress && statusFont != null)
                    tmp.font = statusFont;
                else
                    tmp.font = listFont;
            }
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Truncate;
            if (tmp.alignment == TextAlignmentOptions.Center || tmp.alignment == TextAlignmentOptions.MidlineLeft || tmp.alignment == TextAlignmentOptions.MidlineRight)
            {
                tmp.alignment = TextAlignmentOptions.TopLeft;
            }
        }
    }

    private string BuildObjectivesString(Quest q, bool isCurrentQuest)
    {
        if (q == null) return string.Empty;
        if (q.objectives == null || q.objectives.Length == 0) return string.Empty;
        System.Text.StringBuilder sb = new System.Text.StringBuilder(128);
        sb.Append("Objectives:\n");
        for (int i = 0; i < q.objectives.Length; i++)
        {
            var obj = q.objectives[i];
            bool show = obj.IsCompleted || (isCurrentQuest && i == q.currentObjectiveIndex);
            if (!show) continue; // show only completed and the active one
            string status = obj.IsCompleted ? "<color=green>✓</color>" : "<color=yellow>○</color>";
            string progress = obj.requiredCount > 1 ? $" [{obj.currentCount}/{obj.requiredCount}]" : string.Empty;
            string inProgress = (!obj.IsCompleted && isCurrentQuest && i == q.currentObjectiveIndex) ? " <i>in progress</i>" : string.Empty;
            sb.Append(status).Append(' ').Append(obj.objectiveName).Append(progress).Append(inProgress).Append('\n');
        }
        return sb.ToString();
    }

    private void SubscribeToQuestEvents()
    {
        if (qm == null) return;
        qm.OnQuestStarted += OnQuestChanged;
        qm.OnQuestUpdated += OnQuestChanged;
        qm.OnQuestCompleted += OnQuestChanged;
        qm.OnQuestCompleted += OnQuestCompletedHudPop;
        qm.OnObjectiveCompleted += OnQuestChanged;
        qm.OnObjectiveUpdated += OnQuestChanged;
    }

    private void UnsubscribeFromQuestEvents()
    {
        if (qm == null) return;
        qm.OnQuestStarted -= OnQuestChanged;
        qm.OnQuestUpdated -= OnQuestChanged;
        qm.OnQuestCompleted -= OnQuestChanged;
        qm.OnQuestCompleted -= OnQuestCompletedHudPop;
        qm.OnObjectiveCompleted -= OnQuestChanged;
        qm.OnObjectiveUpdated -= OnQuestChanged;
    }

    private void OnQuestChanged(Quest _)
    {
        hudRefreshTimer = 0f;
        if (panel != null && panel.activeInHierarchy)
        {
            Refresh();
        }
    }

    private void OnQuestChanged(QuestObjective _)
    {
        hudRefreshTimer = 0f;
        if (panel != null && panel.activeInHierarchy)
        {
            Refresh();
        }
    }

    private void OnQuestCompletedHudPop(Quest quest)
    {
        if (quest == null) return;
        PlayHudPop();
    }

    private void PlayHudPop()
    {
        if (hudPopTarget == null) return;
        if (!isActiveAndEnabled) return;

        if (hudPopCoroutine != null)
            StopCoroutine(hudPopCoroutine);

        hudPopCoroutine = StartCoroutine(HudPopRoutine());
    }

    private System.Collections.IEnumerator HudPopRoutine()
    {
        if (hudPopTarget == null)
            yield break;

        float duration = Mathf.Max(0.01f, hudPopDuration);
        float half = duration * 0.5f;
        Vector3 baseScale = hudPopOriginalScale;

        float t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(t / half);
            float eval = hudPopCurve != null ? hudPopCurve.Evaluate(normalized) : normalized;
            float s = Mathf.Lerp(1f, hudPopScale, eval);
            hudPopTarget.localScale = baseScale * s;
            yield return null;
        }

        t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(t / half);
            float eval = hudPopCurve != null ? hudPopCurve.Evaluate(normalized) : normalized;
            float s = Mathf.Lerp(hudPopScale, 1f, eval);
            hudPopTarget.localScale = baseScale * s;
            yield return null;
        }

        hudPopTarget.localScale = baseScale;
        hudPopCoroutine = null;
    }

    private void UpdateHudDisplay()
    {
        if (questManagerOverride != null && qm != questManagerOverride) qm = questManagerOverride;
        if (qm == null) qm = QuestManager.Instance;
        if (qm == null)
        {
            if (hudObjectiveTitle != null) hudObjectiveTitle.text = "";
            if (hudObjectiveDescription != null) hudObjectiveDescription.text = "";
            return;
        }
        var current = qm.GetCurrentQuest();
        if (current == null || current.isCompleted)
        {
            if (hudObjectiveTitle != null) hudObjectiveTitle.text = "";
            if (hudObjectiveDescription != null) hudObjectiveDescription.text = "";
            return;
        }
        var obj = current.GetCurrentObjective();
        if (obj != null)
        {
            bool localCompleted = qm != null && qm.IsCurrentObjectiveLocallyCompleted();
            if (hudObjectiveTitle != null)
            {
                string completedTag = localCompleted ? " <color=#7CFF7C>(completed)</color>" : "";
                hudObjectiveTitle.text = (obj.objectiveName ?? string.Empty) + completedTag;
            }
            if (hudObjectiveDescription != null)
            {
                string desc = obj.description ?? "";
                if (obj.requiredCount > 1)
                    desc = $"{desc} ({obj.currentCount}/{obj.requiredCount})";
                hudObjectiveDescription.text = desc;
            }
        }
        else
        {
            if (hudObjectiveTitle != null) hudObjectiveTitle.text = current.questName;
            if (hudObjectiveDescription != null) hudObjectiveDescription.text = current.description;
        }
    }

    private void UpdateHudArrowRotation()
    {
        if (hudArrow == null || playerTransform == null || hudTarget == null) return;
        Vector3 to = hudTarget.position - playerTransform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.001f) return;
        Vector3 forward = playerTransform.forward; forward.y = 0f; forward.Normalize();
        Vector3 dir = to.normalized;
        float angle = Vector3.SignedAngle(forward, dir, Vector3.up);
        float z = -(angle + hudArrowSpriteRotation);
        var e = hudArrow.eulerAngles; e.z = z; hudArrow.eulerAngles = e;
    }

    private void ResolveHudTarget()
    {
        hudTarget = null;
        if (qm == null) qm = QuestManager.Instance;
        if (qm == null) return;
        var q = qm.GetCurrentQuest();
        if (q == null || q.isCompleted) return;
        var obj = q.GetCurrentObjective();
        if (obj != null)
        {
            switch (obj.objectiveType)
            {
                case ObjectiveType.ReachArea:
                case ObjectiveType.FindArea:
                    hudTarget = FindQuestAreaTransform(obj.targetId);
                    return;
                case ObjectiveType.TalkTo:
                    hudTarget = FindNpcTransform(obj.targetId);
                    return;
                default: return;
            }
        }
        switch (q.objectiveType)
        {
            case ObjectiveType.ReachArea:
            case ObjectiveType.FindArea:
                hudTarget = FindQuestAreaTransform(q.targetId);
                break;
            case ObjectiveType.TalkTo:
                hudTarget = FindNpcTransform(q.targetId);
                break;
        }
    }

    private Transform FindQuestAreaTransform(string areaId)
    {
        if (string.IsNullOrEmpty(areaId)) return null;
        var areas = FindObjectsByType<QuestAreaTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Transform best = null; float bestDist = float.MaxValue;
        foreach (var a in areas)
        {
            if (a != null && string.Equals(a.areaId, areaId, System.StringComparison.OrdinalIgnoreCase))
            {
                float d = (a.transform.position - (playerTransform != null ? playerTransform.position : Vector3.zero)).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = a.transform; }
            }
        }
        return best;
    }

    private Transform FindNpcTransform(string npcId)
    {
        if (string.IsNullOrEmpty(npcId)) return null;
        var npcs = FindObjectsByType<NPCDialogueTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Transform best = null; float bestDist = float.MaxValue;
        foreach (var n in npcs)
        {
            if (n != null && string.Equals(n.npcId, npcId, System.StringComparison.OrdinalIgnoreCase))
            {
                float d = (n.transform.position - (playerTransform != null ? playerTransform.position : Vector3.zero)).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = n.transform; }
            }
        }
        return best;
    }

    private void ShowQuestWithNoObjectives(Quest quest)
    {
        if (listParent == null || rowPrefab == null)
        {
            // Fallback if no prefab
            var msgGO = new GameObject("NoObjectivesMsg", typeof(RectTransform), typeof(TextMeshProUGUI));
            msgGO.transform.SetParent(listParent, false);
            var tmp = msgGO.GetComponent<TextMeshProUGUI>();
            tmp.text = $"<color=yellow>Quest: {quest.questName}</color>\n<color=red>No objectives defined</color>";
            tmp.fontSize = 18f;
            tmp.alignment = TextAlignmentOptions.Center;
            var rt = msgGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.sizeDelta = new Vector2(0, 60f);
            return;
        }

        var row = Instantiate(rowPrefab, listParent);
        EnsureRowLayout(row);

        TextMeshProUGUI nameText = null;
        TextMeshProUGUI progressText = null;
        TextMeshProUGUI descriptionText = null;
        var nameTf = row.transform.Find("QuestName") ?? row.transform.Find("Name") ?? row.transform.Find("quest/QuestName") ?? row.transform.Find("quest/Name");
        var descTf = row.transform.Find("Description") ?? row.transform.Find("quest/Description");
        var progTf = row.transform.Find("Progress") ?? row.transform.Find("right/Progress") ?? row.transform.Find("Right/Progress");
        
        if (nameTf != null) nameText = nameTf.GetComponent<TextMeshProUGUI>();
        if (descTf != null) descriptionText = descTf.GetComponent<TextMeshProUGUI>();
        if (progTf != null) progressText = progTf.GetComponent<TextMeshProUGUI>();

        // Fallback: find all TMP texts if specific paths don't work
        if (nameText == null || descriptionText == null)
        {
            var allTexts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (nameText == null && allTexts.Length > 0) nameText = allTexts[0];
            if (descriptionText == null && allTexts.Length > 1) descriptionText = allTexts[1];
            if (progressText == null && allTexts.Length > 2) progressText = allTexts[allTexts.Length - 1];
        }

        if (nameText != null)
        {
            nameText.text = quest.questName ?? "Unnamed Quest";
        }
        if (descriptionText != null)
        {
            string desc = quest.description ?? "";
            if (!string.IsNullOrEmpty(desc)) desc += "\n";
            desc += "<color=red>No objectives defined</color>";
            descriptionText.text = desc;
        }
        if (progressText != null)
        {
            progressText.text = "-";
        }
    }

    private void ShowNoQuestManagerMessage()
    {
        if (listParent == null) return;
        if (rowPrefab != null)
        {
            var row = Instantiate(rowPrefab, listParent);
            EnsureRowLayout(row);
            var allTexts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (allTexts.Length > 0)
            {
                if (listFont != null) allTexts[0].font = listFont;
                allTexts[0].text = "<color=yellow>Quest Manager not found</color>";
            }
            if (allTexts.Length > 1)
            {
                if (listFont != null) allTexts[1].font = listFont;
                allTexts[1].text = "Please ensure QuestManager exists in the scene.";
            }
            if (allTexts.Length > 2) allTexts[2].text = "";
            return;
        }
        // Fallback
        var msgGO = new GameObject("NoQuestManagerMsg", typeof(RectTransform), typeof(TextMeshProUGUI));
        msgGO.transform.SetParent(listParent, false);
        var tmp = msgGO.GetComponent<TextMeshProUGUI>();
        if (listFont != null) tmp.font = listFont;
        tmp.text = "<color=yellow>Quest Manager not found</color>\nPlease ensure QuestManager exists in the scene.";
        tmp.fontSize = 18f;
        tmp.alignment = TextAlignmentOptions.Center;
        var rt = msgGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.sizeDelta = new Vector2(0, 60f);
    }

    private void ShowNoQuestsMessage()
    {
        if (listParent == null) return;
        if (rowPrefab != null)
        {
            var row = Instantiate(rowPrefab, listParent);
            EnsureRowLayout(row);
            var allTexts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (allTexts.Length > 0)
            {
                if (listFont != null) allTexts[0].font = listFont;
                allTexts[0].text = "<color=yellow>No quests available</color>";
            }
            if (allTexts.Length > 1)
            {
                if (listFont != null) allTexts[1].font = listFont;
                allTexts[1].text = "Quests will appear here when they are loaded.";
            }
            if (allTexts.Length > 2) allTexts[2].text = "";
            return;
        }
        // Fallback
        var msgGO = new GameObject("NoQuestsMsg", typeof(RectTransform), typeof(TextMeshProUGUI));
        msgGO.transform.SetParent(listParent, false);
        var tmp = msgGO.GetComponent<TextMeshProUGUI>();
        if (listFont != null) tmp.font = listFont;
        tmp.text = "<color=yellow>No quests available</color>\nQuests will appear here when they are loaded.";
        tmp.fontSize = 18f;
        tmp.alignment = TextAlignmentOptions.Center;
        var rt = msgGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.sizeDelta = new Vector2(0, 60f);
    }

    private void ShowNoCurrentQuestMessage()
    {
        if (listParent == null) return;
        if (rowPrefab != null)
        {
            var row = Instantiate(rowPrefab, listParent);
            EnsureRowLayout(row);
            var allTexts = row.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (allTexts.Length > 0)
            {
                if (listFont != null) allTexts[0].font = listFont;
                allTexts[0].text = "<color=yellow>No active quest</color>";
            }
            if (allTexts.Length > 1)
            {
                if (listFont != null) allTexts[1].font = listFont;
                allTexts[1].text = "Start a quest to see objectives here.";
            }
            if (allTexts.Length > 2) allTexts[2].text = "";
            return;
        }
        // Fallback
        var msgGO = new GameObject("NoCurrentQuestMsg", typeof(RectTransform), typeof(TextMeshProUGUI));
        msgGO.transform.SetParent(listParent, false);
        var tmp = msgGO.GetComponent<TextMeshProUGUI>();
        if (listFont != null) tmp.font = listFont;
        tmp.text = "<color=yellow>No active quest</color>\nStart a quest to see objectives here.";
        tmp.fontSize = 18f;
        tmp.alignment = TextAlignmentOptions.Center;
        var rt = msgGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.sizeDelta = new Vector2(0, 60f);
    }

}
