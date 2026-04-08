using UnityEngine;
using TMPro;

// Displays the current in-progress quest at the top-right of a World-Space Canvas.
// - Auto-finds QuestManager and listens for quest start/update/complete events
// - Auto-creates a TextMeshProUGUI under a World-Space Canvas if none is assigned
// - Hides when there is no active (in-progress) quest
// - Hides while the Quest List (T) panel is open via LocalUIManager owner check
// Attach this to any GameObject in your scene (or directly to the HUD Text object).
public class QuestHUD : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("If set, the HUD text will be created as a child of this Canvas. If null, a World-Space Canvas will be auto-found.")]
    public Canvas targetCanvas;
    [Tooltip("Optional existing TMP text to use. If null, one will be created.")]
    public TextMeshProUGUI hudText;
    [Tooltip("If no hudText is provided and QuestManager has a questText, reuse that instead of creating a new one.")]
    public bool tryUseQuestManagerText = true;

    [Header("Layout")] 
    [Tooltip("Offset from the top-right corner in canvas units (x is left, y is down).")]
    public Vector2 topRightOffset = new Vector2(20f, 20f);
    [Tooltip("Font size for the HUD text when auto-created.")]
    public float fontSize = 28f;

    [Header("Behavior")] 
    [Tooltip("Hide the HUD while the Quest List (T) panel is open.")]
    public bool hideWhileQuestListOpen = true;
    [Tooltip("Hide the HUD while the Pause Menu is open.")]
    public bool hideWhilePauseMenuOpen = true;

    private QuestManager qm;

    void Awake()
    {
        // Prefer an explicitly assigned QuestManager if available in scene
        qm = FindFirstObjectByType<QuestManager>();
    }

    void Start()
    {
        EnsureBindings();
        WireQuestEvents(true);
        RefreshAll();
    }

    void OnDestroy()
    {
        WireQuestEvents(false);
    }

    void Update()
    {
        // Poll for QuestList open/close since LocalUIManager has no events
        RefreshVisibilityOnly();
    }

    private void EnsureBindings()
    {
        if (qm == null) qm = FindFirstObjectByType<QuestManager>();

        if (hudText == null && tryUseQuestManagerText && qm != null && qm.questText != null)
        {
            hudText = qm.questText;
        }

        if (hudText == null)
        {
            // Find or pick a target canvas (prefer World Space)
            if (targetCanvas == null)
            {
                var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var c in canvases)
                {
                    if (c != null && c.renderMode == RenderMode.WorldSpace) { targetCanvas = c; break; }
                }
                if (targetCanvas == null && canvases != null && canvases.Length > 0)
                {
                    // fallback to any canvas
                    targetCanvas = canvases[0];
                }
            }

            // Create the text under the target canvas
            if (targetCanvas != null)
            {
                var go = new GameObject("QuestHUDText", typeof(RectTransform), typeof(TextMeshProUGUI));
                go.transform.SetParent(targetCanvas.transform, false);
                hudText = go.GetComponent<TextMeshProUGUI>();
                ConfigureHudText(hudText);
            }
            else
            {
                Debug.LogWarning("QuestHUD: No Canvas found. Please assign a Canvas or create one.");
            }
        }

        // If we reused an existing text, still ensure layout/style is correct
        if (hudText != null)
        {
            ConfigureHudText(hudText);
        }
    }

    private void ConfigureHudText(TextMeshProUGUI tmp)
    {
        if (tmp == null) return;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.alignment = TextAlignmentOptions.TopRight;
        if (fontSize > 0) tmp.fontSize = fontSize;
        var rt = tmp.rectTransform;
        if (rt != null)
        {
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-Mathf.Abs(topRightOffset.x), -Mathf.Abs(topRightOffset.y));
            if (rt.sizeDelta.x < 100f) rt.sizeDelta = new Vector2(520, 160);
        }
    }

    private void WireQuestEvents(bool subscribe)
    {
        if (qm == null) return;

        // Avoid duplicate subscriptions by clearing first
        if (!subscribe)
        {
            qm.OnQuestStarted -= OnQuestEvent;
            qm.OnQuestUpdated -= OnQuestEvent;
            qm.OnQuestCompleted -= OnQuestEvent;
            return;
        }

        qm.OnQuestStarted -= OnQuestEvent; qm.OnQuestStarted += OnQuestEvent;
        qm.OnQuestUpdated -= OnQuestEvent; qm.OnQuestUpdated += OnQuestEvent;
        qm.OnQuestCompleted -= OnQuestEvent; qm.OnQuestCompleted += OnQuestEvent;
    }

    private void OnQuestEvent(Quest q)
    {
        RefreshAll();
    }

    private void RefreshAll()
    {
        UpdateTextFromQuest();
        RefreshVisibilityOnly();
    }

    private void UpdateTextFromQuest()
    {
        if (hudText == null) return;
        var q = GetActiveQuest();
        if (q == null || q.isCompleted)
        {
        hudText.text = string.Empty;
        return;
        }
    }

    private void RefreshVisibilityOnly()
    {
        if (hudText == null) return;
        bool listOpen = hideWhileQuestListOpen && LocalUIManager.Instance != null && LocalUIManager.Instance.IsOwner("QuestList");
        bool pauseOpen = hideWhilePauseMenuOpen && LocalUIManager.Instance != null && LocalUIManager.Instance.IsOwner("PauseMenu");
        bool hasActive = HasActiveQuest();
        hudText.gameObject.SetActive(!listOpen && !pauseOpen && hasActive);
    }

    private Quest GetActiveQuest()
    {
        if (qm == null || qm.quests == null || qm.quests.Length == 0) return null;
        // Rely on QuestManager's current quest selection
        int idx = Mathf.Clamp(qm.currentQuestIndex, 0, qm.quests.Length - 1);
        return qm.quests[idx];
    }

    private bool HasActiveQuest()
    {
        var q = GetActiveQuest();
        return q != null && !q.isCompleted;
    }

    private string FormatProgress(Quest q)
    {
        if (q == null) return string.Empty;
        if (q.isCompleted) return "completed";

        // show per-objective detail when available
        if (q.objectives != null && q.objectives.Length > 0)
        {
            var obj = q.GetCurrentObjective();
            if (obj != null)
            {
                bool locallyComplete = obj.IsMultiItemCollect() ? obj.IsMultiItemCollectComplete() : obj.IsCompleted;
                bool isMultiplayerWaiting = locallyComplete
                    && Photon.Pun.PhotonNetwork.IsConnected
                    && Photon.Pun.PhotonNetwork.InRoom
                    && (obj.objectiveType == ObjectiveType.Collect || obj.objectiveType == ObjectiveType.TalkTo);

                if (isMultiplayerWaiting)
                    return $"{obj.objectiveName}     Complete";
                else if (locallyComplete)
                    return $"{obj.objectiveName}  \u2713";
                else
                {
                    string prog = obj.requiredCount > 1 ? $" [{obj.currentCount}/{obj.requiredCount}]" : "";
                    return $"{obj.objectiveName}{prog}";
                }
            }
        }

        // legacy
        if (q.requiredCount > 1)
            return $"{Mathf.Clamp(q.currentCount, 0, Mathf.Max(1, q.requiredCount))}/{Mathf.Max(1, q.requiredCount)}";
        return "in progress";
    }
}
