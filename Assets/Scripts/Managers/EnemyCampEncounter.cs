using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Self-contained enemy camp encounter:
/// - Spawns enemies from this camp
/// - Marks camp as cleared once defeat target is reached
/// - Swaps active/cleared visuals
/// - Grants a team-wide stat bonus when cleared
/// Multiplayer-safe: MasterClient (or offline) controls spawns/clear state.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class EnemyCampEncounter : MonoBehaviourPunCallbacks
{
    [Header("Camp Spawn Setup")]
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private bool autoGenerateSpawnPoints = true;
    [SerializeField, Min(1)] private int autoSpawnPointCount = 8;
    [SerializeField, Min(0.5f)] private float autoSpawnAreaRadius = 10f;
    [SerializeField] private Vector3 autoSpawnAreaOffset = Vector3.zero;
    [Tooltip("Optional area collider for spawn sampling. If set, points are sampled inside collider bounds.")]
    [SerializeField] private Collider spawnAreaCollider;
    [SerializeField, Min(1)] private int initialEnemyCount = 8;
    [SerializeField, Min(0f)] private float spawnStartJitterMax = 1.5f;
    [SerializeField, Min(0.05f)] private float spawnInterval = 1.25f;
    [SerializeField, Min(0.1f)] private float clearCheckInterval = 0.5f;
    [SerializeField, Min(0f)] private float spawnRadius = 1.5f;
    [SerializeField] private bool autoStart = true;
    [Tooltip("If true and zero enemies were successfully spawned, camp can still clear instantly.")]
    [SerializeField] private bool clearIfNoEnemiesSpawned = false;

    [Header("Enemy Prefabs")]
    [Tooltip("Photon prefab names under Resources for online play.")]
    [SerializeField] private string[] photonEnemyPrefabNames;
    [Tooltip("Offline/local fallback prefabs.")]
    [SerializeField] private GameObject[] localEnemyPrefabs;

    [Header("Visual Swap")]
    [Tooltip("Objects enabled while camp is active (uncleared).")]
    [SerializeField] private GameObject[] activeStateObjects;
    [Tooltip("Objects enabled once camp is cleared.")]
    [SerializeField] private GameObject[] clearedStateObjects;

    [Header("Clear Reward (Team Buff)")]
    [SerializeField] private bool grantBuffOnClear = true;
    [SerializeField] private int maxHealthBonus = 15;
    [SerializeField] private int maxStaminaBonus = 15;
    [SerializeField] private int baseDamageBonus = 5;
    [SerializeField] private float speedBonus = 0.5f;
    [SerializeField] private bool refillOnBuff = true;

    [Header("Events")]
    [SerializeField] private UnityEvent onCampCleared;

    [Header("Debug")]
    [SerializeField] private bool logDebug;

    private sealed class SpawnedEnemyRecord
    {
        public Transform enemyTransform;
        public BaseEnemyAI enemyAI;
        public bool defeatCounted;
    }

    private readonly List<SpawnedEnemyRecord> activeEnemies = new List<SpawnedEnemyRecord>();
    private Coroutine spawnRoutine;
    private bool isCleared;
    private bool buffGranted;
    private bool initialSpawnComplete;
    private int totalSpawned;
    private int totalDefeated;
    private int clearTargetDefeats;
    private Vector3[] generatedSpawnPoints;
    private WaitForSeconds waitSpawnInterval;
    private WaitForSeconds waitClearCheckInterval;
    private float cachedSpawnInterval = -1f;
    private float cachedClearInterval = -1f;

    public bool IsCleared => isCleared;
    public int TotalDefeated => totalDefeated;
    public int RemainingToClear => Mathf.Max(0, clearTargetDefeats - totalDefeated);

    private void Start()
    {
        BuildSpawnPointData();
        EnsureWaitCache();
        ApplyVisualState(isCleared);

        if (autoStart)
            StartEncounter();
    }

    public void StartEncounter()
    {
        if (isCleared || !HasAuthority())
        {
            if (logDebug && !isCleared)
                Debug.Log($"[{name}] Camp spawn skipped: no authority on this client.");
            return;
        }

        if (spawnRoutine != null)
            StopCoroutine(spawnRoutine);

        spawnRoutine = StartCoroutine(CoRunEncounter());
    }

    public void StopEncounter()
    {
        if (spawnRoutine != null)
        {
            StopCoroutine(spawnRoutine);
            spawnRoutine = null;
        }
    }

    private IEnumerator CoRunEncounter()
    {
        EnsureWaitCache();

        if (spawnStartJitterMax > 0f)
            yield return new WaitForSeconds(Random.Range(0f, spawnStartJitterMax));

        initialSpawnComplete = false;
        totalSpawned = 0;
        totalDefeated = 0;
        clearTargetDefeats = 0;

        int target = Mathf.Max(1, initialEnemyCount);
        for (int i = 0; i < target; i++)
        {
            if (isCleared)
                yield break;

            Transform spawned = SpawnOneEnemy();
            if (spawned != null)
            {
                RegisterSpawnedEnemy(spawned);
                totalSpawned++;
            }

            if (i < target - 1)
                yield return waitSpawnInterval;
        }

        clearTargetDefeats = totalSpawned;
        initialSpawnComplete = true;

        if (clearTargetDefeats == 0 && clearIfNoEnemiesSpawned)
        {
            SetCampCleared();
            yield break;
        }
        else if (clearTargetDefeats == 0)
        {
            Debug.LogWarning($"[{name}] Camp spawned 0 enemies. Check prefab names/paths and Photon setup.");
        }

        while (!isCleared)
        {
            CleanupEnemyRecords();

            if (clearTargetDefeats > 0 && totalDefeated >= clearTargetDefeats)
            {
                SetCampCleared();
                yield break;
            }

            yield return waitClearCheckInterval;
        }
    }

    private Transform SpawnOneEnemy()
    {
        if (!HasAnySpawnSource())
            return null;

        if (!TryGetRandomSpawnPosition(out Vector3 spawnPos))
            return null;
        Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        bool hasPhoton = photonEnemyPrefabNames != null && photonEnemyPrefabNames.Length > 0;
        bool hasLocal = localEnemyPrefabs != null && localEnemyPrefabs.Length > 0;
        int len = Mathf.Max(hasPhoton ? photonEnemyPrefabNames.Length : 0, hasLocal ? localEnemyPrefabs.Length : 0);
        if (len <= 0)
            return null;

        int idx = Random.Range(0, len);
        string photonPrefabName = hasPhoton && idx < photonEnemyPrefabNames.Length ? photonEnemyPrefabNames[idx] : null;
        GameObject localPrefab = hasLocal && idx < localEnemyPrefabs.Length ? localEnemyPrefabs[idx] : null;
        string resolvedPhotonPath = ResolveEnemyPhotonPath(photonPrefabName, localPrefab);

        GameObject spawned = null;
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            if (string.IsNullOrWhiteSpace(resolvedPhotonPath))
            {
                if (logDebug)
                    Debug.LogWarning($"[{name}] Could not resolve a Photon enemy prefab at index {idx}. Name='{photonPrefabName}', local='{(localPrefab != null ? localPrefab.name : "null")}'.");
                return null;
            }

            spawned = PhotonNetwork.Instantiate(resolvedPhotonPath, spawnPos, spawnRot);
        }
        else
        {
            if (localPrefab != null)
            {
                spawned = Instantiate(localPrefab, spawnPos, spawnRot);
            }
            else if (!string.IsNullOrWhiteSpace(resolvedPhotonPath))
            {
                GameObject resourcePrefab = TryLoadResourceEnemyPrefab(resolvedPhotonPath);
                if (resourcePrefab != null)
                {
                    spawned = Instantiate(resourcePrefab, spawnPos, spawnRot);
                }
                else if (logDebug)
                {
                    Debug.LogWarning($"[{name}] Could not load local fallback prefab for '{resolvedPhotonPath}'.");
                }
            }
        }

        return spawned != null ? spawned.transform : null;
    }

    private string ResolveEnemyPhotonPath(string configuredName, GameObject localPrefab)
    {
        var candidates = new List<string>(4);

        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            string trimmed = configuredName.Trim();
            candidates.Add(trimmed);

            if (trimmed.StartsWith("Enemies/"))
                candidates.Add(trimmed.Substring("Enemies/".Length));
            else
                candidates.Add($"Enemies/{trimmed}");
        }

        if (localPrefab != null && !string.IsNullOrWhiteSpace(localPrefab.name))
        {
            string localName = localPrefab.name.Trim();
            candidates.Add(localName);

            if (localName.StartsWith("Enemies/"))
                candidates.Add(localName.Substring("Enemies/".Length));
            else
                candidates.Add($"Enemies/{localName}");
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            string candidate = candidates[i];
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            if (Resources.Load<GameObject>(candidate) != null)
                return candidate;
        }

        return null;
    }

    private GameObject TryLoadResourceEnemyPrefab(string prefabName)
    {
        if (string.IsNullOrWhiteSpace(prefabName))
            return null;

        GameObject loaded = Resources.Load<GameObject>(prefabName);
        if (loaded != null)
            return loaded;

        if (!prefabName.StartsWith("Enemies/"))
            loaded = Resources.Load<GameObject>($"Enemies/{prefabName}");

        return loaded;
    }

    private void RegisterSpawnedEnemy(Transform enemyTransform)
    {
        if (enemyTransform == null)
            return;

        ParentEnemyUnderCamp(enemyTransform);

        BaseEnemyAI enemyAI = enemyTransform.GetComponent<BaseEnemyAI>();
        if (enemyAI == null)
            enemyAI = enemyTransform.GetComponentInChildren<BaseEnemyAI>();

        if (enemyAI != null)
            enemyAI.OnEnemyDied += HandleSpawnedEnemyDied;
        else if (logDebug)
            Debug.LogWarning($"[{name}] Spawned enemy has no BaseEnemyAI: {enemyTransform.name}");

        activeEnemies.Add(new SpawnedEnemyRecord
        {
            enemyTransform = enemyTransform,
            enemyAI = enemyAI,
            defeatCounted = false
        });
    }

    private void HandleSpawnedEnemyDied(BaseEnemyAI deadEnemy)
    {
        if (!HasAuthority() || deadEnemy == null)
            return;

        for (int i = 0; i < activeEnemies.Count; i++)
        {
            SpawnedEnemyRecord record = activeEnemies[i];
            if (record == null || record.enemyAI != deadEnemy)
                continue;

            CountDefeat(record);
            UnsubscribeRecord(record);
            activeEnemies.RemoveAt(i);
            break;
        }

        if (initialSpawnComplete && clearTargetDefeats > 0 && totalDefeated >= clearTargetDefeats)
            SetCampCleared();
    }

    private void CleanupEnemyRecords()
    {
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
        {
            SpawnedEnemyRecord record = activeEnemies[i];
            if (record == null)
            {
                activeEnemies.RemoveAt(i);
                continue;
            }

            bool missingTransform = record.enemyTransform == null;
            bool deadAI = record.enemyAI != null && record.enemyAI.IsDead;
            if (missingTransform || deadAI)
            {
                CountDefeat(record);
                UnsubscribeRecord(record);
                activeEnemies.RemoveAt(i);
            }
        }
    }

    private void CountDefeat(SpawnedEnemyRecord record)
    {
        if (record == null || record.defeatCounted)
            return;

        record.defeatCounted = true;
        totalDefeated++;

        if (logDebug)
            Debug.Log($"[{name}] Camp defeat progress: {totalDefeated}/{Mathf.Max(1, clearTargetDefeats)}");
    }

    private void SetCampCleared()
    {
        if (isCleared)
            return;

        isCleared = true;
        StopEncounter();
        UnsubscribeAllRecords();
        ApplyVisualState(true);
        onCampCleared?.Invoke();

        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && photonView != null)
        {
            photonView.RPC(nameof(RPC_SetCampVisualCleared), RpcTarget.OthersBuffered, true);
        }

        if (grantBuffOnClear)
            GrantTeamBuff();

        if (logDebug)
            Debug.Log($"[{name}] Camp cleared.");
    }

    private void GrantTeamBuff()
    {
        if (buffGranted)
            return;

        buffGranted = true;
        ApplyBuffToLocalOwnedPlayers();

        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && photonView != null)
        {
            photonView.RPC(
                nameof(RPC_GrantBuffToOwnedPlayers),
                RpcTarget.Others,
                maxHealthBonus,
                maxStaminaBonus,
                baseDamageBonus,
                speedBonus,
                refillOnBuff);
        }
    }

    private void ApplyBuffToLocalOwnedPlayers()
    {
        var players = PlayerRegistry.All;
        if (players.Count == 0)
            return;

        for (int i = 0; i < players.Count; i++)
        {
            PlayerStats stats = players[i];
            if (stats == null)
                continue;

            PhotonView playerView = stats.GetComponent<PhotonView>();
            if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && (playerView == null || !playerView.IsMine))
                continue;

            stats.maxHealth += Mathf.Max(0, maxHealthBonus);
            stats.maxStamina += Mathf.Max(0, maxStaminaBonus);
            stats.baseDamage += Mathf.Max(0, baseDamageBonus);
            stats.speedModifier += Mathf.Max(0f, speedBonus);

            if (refillOnBuff)
            {
                stats.currentHealth = stats.maxHealth;
                stats.currentStamina = stats.maxStamina;
            }
            else
            {
                stats.currentHealth = Mathf.Min(stats.currentHealth, stats.maxHealth);
                stats.currentStamina = Mathf.Min(stats.currentStamina, stats.maxStamina);
            }
        }
    }

    [PunRPC]
    private void RPC_SetCampVisualCleared(bool cleared)
    {
        isCleared = cleared;
        ApplyVisualState(cleared);
    }

    [PunRPC]
    private void RPC_GrantBuffToOwnedPlayers(int healthBonus, int staminaBonus, int damageBonus, float moveSpeedBonus, bool refill)
    {
        maxHealthBonus = healthBonus;
        maxStaminaBonus = staminaBonus;
        baseDamageBonus = damageBonus;
        speedBonus = moveSpeedBonus;
        refillOnBuff = refill;
        if (!buffGranted)
        {
            buffGranted = true;
            ApplyBuffToLocalOwnedPlayers();
        }
    }

    [PunRPC]
    private void RPC_SetSpawnedEnemyParent(int enemyViewId)
    {
        if (enemyViewId <= 0)
            return;

        PhotonView enemyView = PhotonView.Find(enemyViewId);
        if (enemyView == null || enemyView.transform == null)
            return;

        enemyView.transform.SetParent(transform, true);
    }

    private void ApplyVisualState(bool cleared)
    {
        SetGroupActive(activeStateObjects, !cleared);
        SetGroupActive(clearedStateObjects, cleared);
    }

    private static void SetGroupActive(GameObject[] objects, bool active)
    {
        if (objects == null)
            return;

        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null)
                objects[i].SetActive(active);
        }
    }

    private void ResolveSpawnPointsIfEmpty()
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
            return;

        int childCount = transform.childCount;
        if (childCount <= 0)
            return;

        spawnPoints = new Transform[childCount];
        for (int i = 0; i < childCount; i++)
            spawnPoints[i] = transform.GetChild(i);
    }

    private void BuildSpawnPointData()
    {
        if (autoGenerateSpawnPoints)
            GenerateAutomaticSpawnPoints();
        else
            generatedSpawnPoints = null;

        if (spawnPoints == null || spawnPoints.Length == 0)
            ResolveSpawnPointsIfEmpty();
    }

    private void GenerateAutomaticSpawnPoints()
    {
        int count = Mathf.Max(1, autoSpawnPointCount);
        generatedSpawnPoints = new Vector3[count];

        Vector3 center = transform.position + autoSpawnAreaOffset;
        for (int i = 0; i < count; i++)
        {
            generatedSpawnPoints[i] = SampleAutomaticPoint(center);
        }
    }

    private Vector3 SampleAutomaticPoint(Vector3 center)
    {
        if (spawnAreaCollider != null)
        {
            Bounds bounds = spawnAreaCollider.bounds;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                Vector3 candidate = new Vector3(
                    Random.Range(bounds.min.x, bounds.max.x),
                    bounds.center.y,
                    Random.Range(bounds.min.z, bounds.max.z));
                Vector3 closest = spawnAreaCollider.ClosestPoint(candidate);
                if ((closest - candidate).sqrMagnitude < 0.01f)
                    return closest;
            }
        }

        Vector2 ring = Random.insideUnitCircle * Mathf.Max(0.5f, autoSpawnAreaRadius);
        return center + new Vector3(ring.x, 0f, ring.y);
    }

    private bool HasAnySpawnSource()
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
            return true;
        return generatedSpawnPoints != null && generatedSpawnPoints.Length > 0;
    }

    private bool TryGetRandomSpawnPosition(out Vector3 spawnPos)
    {
        spawnPos = transform.position;

        bool hasManual = spawnPoints != null && spawnPoints.Length > 0;
        bool hasGenerated = generatedSpawnPoints != null && generatedSpawnPoints.Length > 0;
        if (!hasManual && !hasGenerated)
            return false;

        // Prefer explicit/manual points when they exist.
        if (hasManual)
        {
            Transform point = spawnPoints[Random.Range(0, spawnPoints.Length)];
            if (point != null)
            {
                Vector2 offset2D = Random.insideUnitCircle * spawnRadius;
                spawnPos = point.position + new Vector3(offset2D.x, 0f, offset2D.y);
                return true;
            }
        }

        if (hasGenerated)
        {
            Vector3 basePoint = generatedSpawnPoints[Random.Range(0, generatedSpawnPoints.Length)];
            Vector2 offset2D = Random.insideUnitCircle * spawnRadius;
            spawnPos = basePoint + new Vector3(offset2D.x, 0f, offset2D.y);
            return true;
        }

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!autoGenerateSpawnPoints)
            return;

        Gizmos.color = new Color(0.2f, 0.8f, 0.4f, 0.6f);
        Vector3 center = transform.position + autoSpawnAreaOffset;

        if (spawnAreaCollider != null)
        {
            Bounds bounds = spawnAreaCollider.bounds;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
        else
        {
            Gizmos.DrawWireSphere(center, Mathf.Max(0.5f, autoSpawnAreaRadius));
        }
    }

    private bool HasAuthority()
    {
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
            return PhotonNetwork.IsMasterClient;
        return true;
    }

    private void UnsubscribeAllRecords()
    {
        for (int i = 0; i < activeEnemies.Count; i++)
            UnsubscribeRecord(activeEnemies[i]);
    }

    private void UnsubscribeRecord(SpawnedEnemyRecord record)
    {
        if (record == null || record.enemyAI == null)
            return;

        record.enemyAI.OnEnemyDied -= HandleSpawnedEnemyDied;
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        if (isCleared)
            return;

        if (HasAuthority() && autoStart && spawnRoutine == null)
            StartEncounter();
    }

    private void OnDisable()
    {
        StopEncounter();
        UnsubscribeAllRecords();
        activeEnemies.Clear();
    }

    private void OnValidate()
    {
        EnsureWaitCache();
    }

    private void ParentEnemyUnderCamp(Transform enemyTransform)
    {
        if (enemyTransform == null)
            return;

        enemyTransform.SetParent(transform, true);

        if (!(PhotonNetwork.IsConnected && PhotonNetwork.InRoom) || photonView == null)
            return;

        PhotonView enemyView = enemyTransform.GetComponent<PhotonView>();
        if (enemyView == null)
            return;

        photonView.RPC(nameof(RPC_SetSpawnedEnemyParent), RpcTarget.Others, enemyView.ViewID);
    }

    private void EnsureWaitCache()
    {
        float safeSpawn = Mathf.Max(0.05f, spawnInterval);
        if (waitSpawnInterval == null || !Mathf.Approximately(cachedSpawnInterval, safeSpawn))
        {
            cachedSpawnInterval = safeSpawn;
            waitSpawnInterval = new WaitForSeconds(cachedSpawnInterval);
        }

        float safeClear = Mathf.Max(0.1f, clearCheckInterval);
        if (waitClearCheckInterval == null || !Mathf.Approximately(cachedClearInterval, safeClear))
        {
            cachedClearInterval = safeClear;
            waitClearCheckInterval = new WaitForSeconds(cachedClearInterval);
        }
    }
}
