using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Add to the equipped-item preview (needs a Graphic with Raycast Target). Shows the same item tooltip as grid slots.
/// </summary>
public class InventoryEquippedSlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private InventoryUI _ui;

    public void Bind(InventoryUI ui) => _ui = ui;

    private void Awake()
    {
        if (_ui == null)
            _ui = GetComponentInParent<InventoryUI>(true);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_ui == null) return;
        var em = _ui.EquipmentManager;
        if (em == null || em.equippedItem == null) return;
        var rt = transform as RectTransform;
        if (rt != null)
            _ui.ShowItemTooltip(em.equippedItem, rt, InventoryTooltipPlacement.ToRightOfAnchor);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _ui?.HideItemTooltip();
    }
}
