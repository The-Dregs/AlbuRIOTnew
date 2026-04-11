using UnityEngine;
using TMPro;

public enum InventoryTooltipPlacement
{
    /// <summary>Centered above the anchor (grid slots).</summary>
    AboveAnchor,
    /// <summary>To the right of the anchor, vertically centered (equipped preview).</summary>
    ToRightOfAnchor
}

/// <summary>
/// Floating tooltip for inventory slots (name, description, stat modifiers).
/// Assign as a child of the inventory panel; Show() parents it under the hovered slot temporarily.
/// </summary>
public class InventoryItemTooltip : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("Nested Canvas sorting so the tooltip draws above headers and other inventory UI.")]
    [SerializeField] private int tooltipSortOrder = 32760;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI modifiersText;
    [Tooltip("Pixels above the anchor's top edge (grid slots).")]
    public float offsetAboveSlot = 8f;
    [Tooltip("Pixels to the right of the anchor's right edge (equipped slot).")]
    public float offsetRightOfAnchor = 8f;

    private Transform _originalParent;
    private int _originalSiblingIndex;
    private Canvas _overlayCanvas;

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        _originalParent = transform.parent;
        _originalSiblingIndex = transform.GetSiblingIndex();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
        gameObject.SetActive(false);
    }

    private void EnsureRendersOnTop()
    {
        if (_overlayCanvas == null)
        {
            _overlayCanvas = GetComponent<Canvas>();
            if (_overlayCanvas == null)
                _overlayCanvas = gameObject.AddComponent<Canvas>();
        }
        _overlayCanvas.overrideSorting = true;
        _overlayCanvas.sortingOrder = tooltipSortOrder;
    }

    public void Show(ItemData item, RectTransform slotRect, InventoryTooltipPlacement placement = InventoryTooltipPlacement.AboveAnchor)
    {
        if (item == null || slotRect == null) return;

        if (titleText != null)
            titleText.text = item.itemName ?? "";
        if (descriptionText != null)
            descriptionText.text = string.IsNullOrEmpty(item.description) ? "" : item.description;
        if (modifiersText != null)
        {
            string mods = item.GetStatModifiersRichText();
            modifiersText.gameObject.SetActive(!string.IsNullOrEmpty(mods));
            modifiersText.text = mods;
        }

        transform.SetParent(slotRect, false);
        var rt = (RectTransform)transform;
        if (placement == InventoryTooltipPlacement.ToRightOfAnchor)
        {
            // Right-middle of anchor; tooltip's left edge aligns, extends to the right.
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(offsetRightOfAnchor, 0f);
        }
        else
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, offsetAboveSlot);
        }
        rt.SetAsLastSibling();
        EnsureRendersOnTop();

        gameObject.SetActive(true);
        if (canvasGroup != null)
            canvasGroup.alpha = 1f;
    }

    public void Hide()
    {
        if (_originalParent != null)
        {
            transform.SetParent(_originalParent, false);
            transform.SetSiblingIndex(_originalSiblingIndex);
        }
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
    }
}
