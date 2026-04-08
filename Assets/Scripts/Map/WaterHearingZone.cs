using UnityEngine;

/// <summary>
/// Trigger zone that attenuates local player hearing while inside (e.g. underwater areas).
/// Requires a trigger collider.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WaterHearingZone : MonoBehaviour
{
    [Range(0.05f, 1f)]
    [SerializeField] private float hearingMultiplierInside = 0.45f;

    private int zoneId;

    private void Awake()
    {
        zoneId = GetInstanceID();
    }

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        LocalPlayerHearingController hearing = other.GetComponentInParent<LocalPlayerHearingController>();
        if (hearing != null)
        {
            hearing.EnterHearingZone(zoneId, hearingMultiplierInside);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        LocalPlayerHearingController hearing = other.GetComponentInParent<LocalPlayerHearingController>();
        if (hearing != null)
        {
            hearing.ExitHearingZone(zoneId);
        }
    }
}
