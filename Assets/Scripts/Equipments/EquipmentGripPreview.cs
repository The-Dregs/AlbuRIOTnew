using UnityEngine;

// attach this to your Player (or a child) to preview an ItemData's model on the hand bone in Edit Mode
[ExecuteAlways]
public class EquipmentGripPreview : MonoBehaviour
{
    public EquipmentManager equipmentManager; // auto-found in parent if null
    public ItemData item;                     // the item to preview
    public bool previewActive = false;        // toggle to spawn/remove the preview

    [Tooltip("when true, will keep ItemData overrides applied each refresh")] public bool applyItemOverrides = true;

    [System.NonSerialized] public GameObject previewInstance;

    void OnEnable()
    {
        AutoWire();
        RefreshPreview();
    }

    void OnDisable()
    {
        ClearPreview();
    }

    void OnValidate()
    {
        AutoWire();
        if (previewActive)
        {
            RefreshPreview();
        }
    }

    // Called from editor to update transform in real-time
    public void UpdateTransform(Vector3 position, Vector3 rotation, Vector3 scale)
    {
        if (previewInstance != null)
        {
            previewInstance.transform.localPosition = position;
            previewInstance.transform.localRotation = Quaternion.Euler(rotation);
            previewInstance.transform.localScale = scale;
        }
    }

    private void AutoWire()
    {
        if (equipmentManager == null)
            equipmentManager = GetComponentInParent<EquipmentManager>(true);
    }

    public void RefreshPreview()
    {
        if (!previewActive)
        {
            ClearPreview();
            return;
        }
        if (equipmentManager == null || equipmentManager.handTransform == null || item == null || item.modelPrefab == null)
            return;

        // replace preview if model changed
        if (previewInstance == null || previewInstance.name != item.modelPrefab.name + " [Preview]")
        {
            ClearPreview();
            previewInstance = Instantiate(item.modelPrefab, equipmentManager.handTransform);
            previewInstance.name = item.modelPrefab.name + " [Preview]";
            previewInstance.hideFlags = HideFlags.DontSave;
        }

        // by default keep prefab local pose; optionally apply overrides from ItemData + per-player hold offset
        Vector3 basePos = (applyItemOverrides && item.overrideTransform) ? item.modelLocalPosition : Vector3.zero;
        Quaternion baseRot = (applyItemOverrides && item.overrideTransform) ? Quaternion.Euler(item.modelLocalEulerAngles) : Quaternion.identity;
        previewInstance.transform.localPosition = basePos + equipmentManager.holdPositionOffset;
        previewInstance.transform.localRotation = baseRot * Quaternion.Euler(equipmentManager.holdRotationOffset);
        // scale always from ItemData
        previewInstance.transform.localScale = item.modelScale;
    }

    public void ClearPreview()
    {
        if (previewInstance != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                GameObject.DestroyImmediate(previewInstance);
            else
#endif
                GameObject.Destroy(previewInstance);
            previewInstance = null;
        }
    }

    // write the current preview transform back to ItemData so it equips the same in play mode
    public void BakeToItemData()
    {
        if (item == null || previewInstance == null) return;
        item.overrideTransform = true;
        item.modelLocalPosition = previewInstance.transform.localPosition;
        item.modelLocalEulerAngles = previewInstance.transform.localEulerAngles;
        item.modelScale = previewInstance.transform.localScale;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(item);
#endif
    }
}

















