using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Dev-only: builds a scrollable list of buttons for every ItemData under Resources (default Items/1ItemData).
/// Attach under the pause dev panel; assign the ScrollRect Content transform as button parent.
/// </summary>
public class DevItemGiverPanel : MonoBehaviour
{
    [Tooltip("Usually the Content object under a ScrollRect (Vertical Layout Group recommended).")]
    [SerializeField] RectTransform buttonParent;

    [Tooltip("Resources path relative to a Resources folder (no file extension).")]
    [SerializeField] string resourcesItemDataPath = "Items/1ItemData";

    [Tooltip("If false, panel only builds in the editor or development builds (same idea as PauseMenu dev panel).")]
    [SerializeField] bool allowInReleaseBuild = false;

    [Tooltip("Rebuild the list each time this object is enabled (e.g. when opening the dev panel).")]
    [SerializeField] bool rebuildOnEnable = true;

    [Tooltip("Quantity granted per click for a single item.")]
    [SerializeField] int quantityPerGrant = 1;

    readonly List<GameObject> _spawnedRows = new List<GameObject>(64);

    static Sprite _whiteSprite;

    static Sprite WhiteSprite()
    {
        if (_whiteSprite == null)
        {
            var tex = Texture2D.whiteTexture;
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }
        return _whiteSprite;
    }

    void OnEnable()
    {
        if (rebuildOnEnable)
            RebuildItemButtons();
    }

    bool CanUseDevItemGiver()
    {
        if (allowInReleaseBuild) return true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
    }

#if UNITY_EDITOR
    [ContextMenu("Rebuild item buttons")]
    void EditorRebuild() => RebuildItemButtons();
#endif

    /// <summary>Clear and recreate all rows from Resources.</summary>
    public void RebuildItemButtons()
    {
        if (!CanUseDevItemGiver())
        {
            ClearRows();
            return;
        }

        if (buttonParent == null)
        {
            Debug.LogWarning("[DevItemGiverPanel] buttonParent is not assigned.");
            return;
        }

        ClearRows();

        var loaded = Resources.LoadAll<ItemData>(resourcesItemDataPath);
        if (loaded == null || loaded.Length == 0)
        {
            Debug.LogWarning($"[DevItemGiverPanel] No ItemData found at Resources/{resourcesItemDataPath}.");
            CreateTextRow("(no ItemData in folder)", null, false);
            return;
        }

        var list = new List<ItemData>(loaded);
        list.Sort((a, b) => string.CompareOrdinal(LabelFor(a), LabelFor(b)));

        CreateActionRow("[ Give ALL (1 each) ]", () => GiveAllItems(list));

        foreach (var item in list)
        {
            if (item == null) continue;
            string label = LabelFor(item);
            CreateTextRow(label, () => GiveItem(item), true);
        }
    }

    static string LabelFor(ItemData item)
    {
        if (item == null) return "(null)";
        if (!string.IsNullOrEmpty(item.itemName))
            return item.itemName;
        return item.name;
    }

    void GiveItem(ItemData item)
    {
        var inv = Inventory.FindLocalInventory();
        if (inv == null)
        {
            Debug.LogWarning("[DevItemGiverPanel] No local inventory found.");
            return;
        }

        int q = Mathf.Max(1, quantityPerGrant);
        bool ok = inv.AddItem(item, q, silent: false);
        Debug.Log(ok
            ? $"[DevItemGiverPanel] Granted x{q}: {LabelFor(item)}"
            : $"[DevItemGiverPanel] Failed to grant {LabelFor(item)} (full inventory?)");
    }

    void GiveAllItems(List<ItemData> items)
    {
        var inv = Inventory.FindLocalInventory();
        if (inv == null)
        {
            Debug.LogWarning("[DevItemGiverPanel] No local inventory found.");
            return;
        }

        int q = Mathf.Max(1, quantityPerGrant);
        int okCount = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item == null) continue;
            if (inv.AddItem(item, q, silent: false))
                okCount++;
        }

        Debug.Log($"[DevItemGiverPanel] Give all: succeeded for {okCount}/{items.Count} entries (qty {q} each).");
    }

    void ClearRows()
    {
        for (int i = 0; i < _spawnedRows.Count; i++)
        {
            if (_spawnedRows[i] != null)
                Destroy(_spawnedRows[i]);
        }
        _spawnedRows.Clear();
    }

    void CreateActionRow(string label, UnityEngine.Events.UnityAction onClick)
    {
        CreateTextRow(label, onClick, false);
    }

    void CreateTextRow(string label, UnityEngine.Events.UnityAction onClick, bool isItemRow)
    {
        var go = new GameObject($"DevItemGiverRow_{_spawnedRows.Count}", typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(buttonParent, false);
        rt.localScale = Vector3.one;

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 32f;
        le.preferredHeight = 32f;
        le.flexibleWidth = 1f;

        var img = go.GetComponent<Image>();
        img.sprite = WhiteSprite();
        img.color = isItemRow ? new Color(0.2f, 0.22f, 0.28f, 0.95f) : new Color(0.25f, 0.32f, 0.22f, 0.95f);

        var btn = go.GetComponent<Button>();
        if (onClick != null)
            btn.onClick.AddListener(onClick);
        else
            btn.interactable = false;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(8f, 2f);
        textRt.offsetMax = new Vector2(-8f, -2f);

        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 14f;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        _spawnedRows.Add(go);
    }
}
