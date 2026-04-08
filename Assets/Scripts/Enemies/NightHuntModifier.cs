using Photon.Pun;
using UnityEngine;

/// <summary>
/// Applies a runtime EnemyData override so night-spawned enemies
/// keep strong chase behavior without mutating shared ScriptableObjects.
/// Reverts to normal stats when day arrives. Master/offline authority only.
/// </summary>
[DisallowMultipleComponent]
public class NightHuntModifier : MonoBehaviour
{
    [SerializeField, Min(1f)] private float forcedDetectionRange = 220f;
    [SerializeField, Min(1f)] private float forcedChaseLoseRange = 280f;
    [SerializeField, Min(0.1f)] private float chaseSpeedMultiplier = 1.1f;
    [SerializeField] private bool disablePatrolDuringNightHunt = true;
    [Header("VFX")]
    [SerializeField, Min(0.01f)] private float vfxScaleMultiplier = 1f;

    private BaseEnemyAI enemyAI;
    private GameObject activeVFXInstance;
    private EnemyData originalEnemyData;
    private EnemyData runtimeEnemyData;
    private bool applied;
    private bool subscribed;

    private bool HasAuthority()
    {
        if (!PhotonNetwork.IsConnected || PhotonNetwork.OfflineMode) return true;
        return PhotonNetwork.IsMasterClient;
    }

    private void Awake()
    {
        enemyAI = GetComponent<BaseEnemyAI>();
        if (enemyAI == null)
            enemyAI = GetComponentInChildren<BaseEnemyAI>();
    }

    public void SetVfxScaleMultiplier(float scale)
    {
        vfxScaleMultiplier = Mathf.Max(0.01f, scale);
        if (activeVFXInstance != null)
        {
            var baseScale = Vector3.one;
            if (activeVFXPrefabRef != null && activeVFXPrefabRef.transform != null)
                baseScale = activeVFXPrefabRef.transform.localScale;
            activeVFXInstance.transform.localScale = baseScale * vfxScaleMultiplier;
        }
    }

    public void Configure(float detectionRange, float chaseLoseRange, float chaseMult, bool disablePatrol, GameObject activeVFXPrefab = null)
    {
        forcedDetectionRange = Mathf.Max(1f, detectionRange);
        forcedChaseLoseRange = Mathf.Max(forcedDetectionRange, chaseLoseRange);
        chaseSpeedMultiplier = Mathf.Max(0.1f, chaseMult);
        disablePatrolDuringNightHunt = disablePatrol;
        activeVFXPrefabRef = activeVFXPrefab;
        // If stats are already applied (e.g. spawned at night) but VFX prefab was assigned later,
        // ensure the VFX appears immediately.
        if (applied)
            SpawnActiveVFX();
        else
            TryApply();
    }

    private GameObject activeVFXPrefabRef;

    private void OnEnable()
    {
        SubscribeDayNight();
        if (DayNightCycleManager.Instance != null && DayNightCycleManager.Instance.IsDay())
            Revert();
        else
            TryApply();
    }

    private void OnDisable()
    {
        UnsubscribeDayNight();
    }

    private void SubscribeDayNight()
    {
        if (subscribed) return;
        var dn = DayNightCycleManager.Instance ?? FindFirstObjectByType<DayNightCycleManager>();
        if (dn != null)
        {
            dn.OnPhaseChanged += OnPhaseChanged;
            subscribed = true;
        }
    }

    private void UnsubscribeDayNight()
    {
        if (!subscribed) return;
        var dn = DayNightCycleManager.Instance ?? FindFirstObjectByType<DayNightCycleManager>();
        if (dn != null)
        {
            dn.OnPhaseChanged -= OnPhaseChanged;
            subscribed = false;
        }
    }

    private void OnPhaseChanged(DayNightCycleManager.TimePhase phase)
    {
        if (phase == DayNightCycleManager.TimePhase.Day || phase == DayNightCycleManager.TimePhase.Dawn)
            Revert();
        else if (phase == DayNightCycleManager.TimePhase.Dusk || phase == DayNightCycleManager.TimePhase.Night)
            TryApply();
    }

    private void TryApply()
    {
        if (applied || !HasAuthority())
            return;
        if (enemyAI == null || enemyAI.enemyData == null)
            return;

        originalEnemyData = enemyAI.enemyData;
        runtimeEnemyData = Object.Instantiate(originalEnemyData);
        runtimeEnemyData.name = originalEnemyData.name + "_NightHuntRuntime";
        runtimeEnemyData.detectionRange = Mathf.Max(runtimeEnemyData.detectionRange, forcedDetectionRange);
        runtimeEnemyData.chaseLoseRange = Mathf.Max(runtimeEnemyData.chaseLoseRange, forcedChaseLoseRange, runtimeEnemyData.detectionRange);
        runtimeEnemyData.chaseSpeed = Mathf.Max(0.1f, runtimeEnemyData.chaseSpeed * chaseSpeedMultiplier);
        if (disablePatrolDuringNightHunt)
            runtimeEnemyData.enablePatrol = false;

        enemyAI.enemyData = runtimeEnemyData;
        applied = true;
        SpawnActiveVFX();
    }

    private void SpawnActiveVFX()
    {
        if (activeVFXPrefabRef == null || transform == null) return;
        if (activeVFXInstance != null) return;

        activeVFXInstance = Instantiate(activeVFXPrefabRef, transform);
        activeVFXInstance.transform.localPosition = Vector3.zero;
        activeVFXInstance.transform.localRotation = Quaternion.identity;
        var baseScale = Vector3.one;
        if (activeVFXPrefabRef.transform != null)
            baseScale = activeVFXPrefabRef.transform.localScale;
        float safeMult = Mathf.Max(0.01f, vfxScaleMultiplier);
        activeVFXInstance.transform.localScale = baseScale * safeMult;
    }

    private void DestroyActiveVFX()
    {
        if (activeVFXInstance != null)
        {
            Destroy(activeVFXInstance);
            activeVFXInstance = null;
        }
    }

    private void Revert()
    {
        if (!applied || !HasAuthority())
            return;
        if (enemyAI == null || originalEnemyData == null)
            return;

        enemyAI.enemyData = originalEnemyData;
        applied = false;
        DestroyActiveVFX();
        if (runtimeEnemyData != null)
        {
            Destroy(runtimeEnemyData);
            runtimeEnemyData = null;
        }
    }

    private void OnDestroy()
    {
        DestroyActiveVFX();
        if (runtimeEnemyData != null)
            Destroy(runtimeEnemyData);
    }
}
