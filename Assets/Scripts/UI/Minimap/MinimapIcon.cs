using UnityEngine;

[DisallowMultipleComponent]
public class MinimapIcon : MonoBehaviour
{
    [Header("Icon")]
    public Sprite iconSprite;
    public Color iconColor = Color.white;
    [Min(4f)] public float iconSize = 14f;

    [Header("Behavior")]
    [Tooltip("If enabled, the icon rotates with this transform's Y heading.")]
    public bool rotateWithTarget = false;
    [Tooltip("Optional world offset for marker placement.")]
    public Vector3 worldOffset = Vector3.zero;

    public Vector3 WorldPosition => transform.position + worldOffset;

    void OnEnable()
    {
        if (MinimapController.Instance != null)
            MinimapController.Instance.RegisterIcon(this);
    }

    void OnDisable()
    {
        if (MinimapController.Instance != null)
            MinimapController.Instance.UnregisterIcon(this);
    }
}
