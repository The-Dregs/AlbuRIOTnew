using UnityEngine;
using UnityEngine.Serialization;

// attach this to your Player (or a child) to preview an ItemData's model on the hand bone in Edit Mode
[ExecuteAlways]
public class EquipmentGripPreview : MonoBehaviour
{
    public EquipmentManager equipmentManager; // auto-found in parent if null
    public ItemData item;                     // the item to preview
    public bool previewActive = false;        // toggle to spawn/remove the preview

    [FormerlySerializedAs("applyItemOverrides")]
    [Tooltip("When Override Transform is on, refresh always uses ItemData pose. When Override is off: if this is on, the preview uses the model prefab root local position/rotation; if off, uses zeros under the hand.")]
    public bool applyItemDataPoseWhenRefreshing = true;

    [Header("Editor — Reload preview from ItemData")]
    [Tooltip("Check once to move the '[Preview]' in the scene to match the ItemData → Equipment Model numbers (+ hold offsets). Use when you typed position/rotation on the ItemData asset, or after Apply overwrote the asset from a stale preview. Unchecks automatically.")]
    public bool reloadPreviewFromItemDataOnce;

    [Header("Editor — Apply Item Overrides (scene → asset)")]
    [Tooltip("Copies the scene preview object's pose INTO ItemData (not the other way). Pose the '[Preview]' child under the hand in the Scene view first. If you only edited ItemData fields, use Reload Preview from ItemData instead — otherwise this reads whatever the preview currently is (often 0,0,0) and overwrites your asset. Unchecks automatically.")]
    public bool applyItemOverridesOnce;

    [Tooltip("Check this to destroy the preview and respawn from the current ItemData (use after switching Item or model prefab).")]
    public bool resetPreview;

    [System.NonSerialized] public GameObject previewInstance;

    // Tracks what we last built so switching ItemData / prefab forces a new instance.
    [System.NonSerialized] private ItemData _lastPreviewedItem;
    [System.NonSerialized] private GameObject _lastPreviewedPrefab;

    void OnEnable()
    {
        AutoWire();
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            ScheduleEditorValidateApply();
            return;
        }
#endif
        ApplyInspectorDrivenPreviewState();
    }

    void OnDisable()
    {
        ClearPreview();
        _lastPreviewedItem = null;
        _lastPreviewedPrefab = null;
    }

    void OnValidate()
    {
        AutoWire();
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            ScheduleEditorValidateApply();
            return;
        }
#endif
        ApplyInspectorDrivenPreviewState();
    }

#if UNITY_EDITOR
    /// <summary>
    /// Destroy/Instantiate is not allowed inside OnValidate; defer to next editor tick.
    /// Also used when EquipmentManager hold offsets change in the inspector.
    /// </summary>
    public void ScheduleEditorValidateApply()
    {
        if (Application.isPlaying)
        {
            if (previewActive)
                RefreshPreview();
            return;
        }
        UnityEditor.EditorApplication.delayCall -= EditorDeferredApply;
        UnityEditor.EditorApplication.delayCall += EditorDeferredApply;
    }

    private void EditorDeferredApply()
    {
        if (this == null) return;
        ApplyInspectorDrivenPreviewState();
    }
#endif

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

    /// <summary>Inspector / deferred editor path: handles reset checkbox, preview on/off, and refresh.</summary>
    private void ApplyInspectorDrivenPreviewState()
    {
        if (resetPreview)
        {
            resetPreview = false;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            ClearPreview();
            _lastPreviewedItem = null;
            _lastPreviewedPrefab = null;
            if (previewActive)
                RefreshPreview();
            return;
        }

        ProcessReloadPreviewFromItemDataOnce();

        // Bake must run BEFORE the normal RefreshPreview when saving pose — Refresh overwrites the preview from ItemData.
        if (TryProcessApplyItemOverridesOnce())
            return;

        if (previewActive)
            RefreshPreview();
        else
        {
            ClearPreview();
            _lastPreviewedItem = null;
            _lastPreviewedPrefab = null;
        }
    }

    private void ProcessReloadPreviewFromItemDataOnce()
    {
        if (!reloadPreviewFromItemDataOnce)
            return;
        reloadPreviewFromItemDataOnce = false;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        if (previewActive)
            RefreshPreview();
    }

    /// <summary>
    /// One-shot: read current preview transform, write to ItemData, then refresh so the scene matches the asset.
    /// Returns true if the flag was consumed (caller should skip a redundant RefreshPreview before this ran).
    /// </summary>
    private bool TryProcessApplyItemOverridesOnce()
    {
        if (!applyItemOverridesOnce)
            return false;

        applyItemOverridesOnce = false;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif

        if (!previewActive || item == null)
            return true;

        if (previewInstance == null)
            RefreshPreview();

        if (previewInstance == null)
            return true;

        BakeToItemData();
        RefreshPreview();
        return true;
    }

    public void RefreshPreview()
    {
        if (!previewActive)
        {
            ClearPreview();
            _lastPreviewedItem = null;
            _lastPreviewedPrefab = null;
            return;
        }

        if (equipmentManager == null || equipmentManager.handTransform == null || item == null || item.modelPrefab == null)
        {
            ClearPreview();
            _lastPreviewedItem = null;
            _lastPreviewedPrefab = null;
            return;
        }

        bool itemOrPrefabChanged = _lastPreviewedItem != item || _lastPreviewedPrefab != item.modelPrefab;
        string expectedName = item.modelPrefab.name + " [Preview]";
        bool nameMismatch = previewInstance != null && previewInstance.name != expectedName;
        bool needNewInstance = previewInstance == null || itemOrPrefabChanged || nameMismatch;

        if (needNewInstance)
        {
            ClearPreview();
            previewInstance = Instantiate(item.modelPrefab, equipmentManager.handTransform);
            previewInstance.name = expectedName;
            previewInstance.hideFlags = HideFlags.DontSave;
            _lastPreviewedItem = item;
            _lastPreviewedPrefab = item.modelPrefab;
        }

        Vector3 basePos;
        Quaternion baseRot;
        if (item.overrideTransform)
        {
            basePos = item.modelLocalPosition;
            baseRot = item.GetEquipmentModelLocalRotation();
        }
        else if (applyItemDataPoseWhenRefreshing && item.modelPrefab != null)
        {
            basePos = item.modelPrefab.transform.localPosition;
            baseRot = item.modelPrefab.transform.localRotation;
        }
        else
        {
            basePos = Vector3.zero;
            baseRot = Quaternion.identity;
        }
        previewInstance.transform.localPosition = basePos + equipmentManager.holdPositionOffset;
        previewInstance.transform.localRotation = baseRot * Quaternion.Euler(equipmentManager.holdRotationOffset);
        previewInstance.transform.localScale = item.modelScale;
    }

    public void ClearPreview()
    {
        if (previewInstance == null) return;
        var go = previewInstance;
        previewInstance = null;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            DestroyImmediate(go);
        else
#endif
            Destroy(go);
    }

#if UNITY_EDITOR
    [ContextMenu("Reset preview (clear + rebuild from ItemData)")]
    private void ContextResetPreview()
    {
        ClearPreview();
        _lastPreviewedItem = null;
        _lastPreviewedPrefab = null;
        if (previewActive)
            RefreshPreview();
    }
#endif

    /// <summary>
    /// Writes preview local pose into ItemData. Strips EquipmentManager hold offsets so values match what EquipModelLocal expects (base pose only).
    /// </summary>
    public void BakeToItemData()
    {
        if (item == null || previewInstance == null) return;

        Transform t = previewInstance.transform;
        Vector3 pos = t.localPosition;
        Quaternion rot = t.localRotation;
        if (equipmentManager != null)
        {
            pos -= equipmentManager.holdPositionOffset;
            Quaternion holdQ = Quaternion.Euler(equipmentManager.holdRotationOffset);
            rot = rot * Quaternion.Inverse(holdQ);
        }

        item.ApplyBakedEquipmentGripPose(pos, rot, t.localScale);
    }
}
