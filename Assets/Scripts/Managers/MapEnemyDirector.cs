using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

/// <summary>
/// Per-map enemy spawning director:
/// - Night: periodically spawn extra enemies near players
/// Multiplayer-safe: only MasterClient (or offline) performs spawning.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class MapEnemyDirector : MonoBehaviourPunCallbacks
{
    [Header("Map refs")]
    [SerializeField] private Terrain terrain;
    [SerializeField] private TerrainGenerator terrainGenerator;
    [SerializeField] private DayNightCycleManager dayNight;

    [Header("Night dynamic spawning")]
    [Tooltip("Spawn extra enemies near players during night.")]
    [SerializeField] private bool enableNightDynamicSpawning = true;
    [SerializeField] private string[] nightPhotonEnemyPrefabs;
    [SerializeField] private GameObject[] nightLocalEnemyPrefabs;
    [Tooltip("Base max. Increases by 2 for every two nights (e.g. nights 3–4: +2, nights 5–6: +4).")]
    [SerializeField, Min(0)] private int nightMaxEnemies = 35;
    [SerializeField] private float nightSpawnInterval = 4f;
    [SerializeField] private float dayIdlePollInterval = 2f;
    [SerializeField, Min(0.25f)] private float playerCacheRefreshInterval = 2f;
    [SerializeField] private float nightMinDistanceFromPlayer = 12f;
    [SerializeField] private float nightMaxDistanceFromPlayer = 28f;
    [SerializeField] private float minEnemySpacing = 8f;
    [Tooltip("Optional VFX prefab to spawn at enemy spawn position. Destroyed after spawnVFXDuration seconds (0 = no auto-destroy).")]
    [SerializeField] private GameObject nightSpawnVFXPrefab;
    [SerializeField, Min(0f)] private float spawnVFXDuration = 5f;
    [Tooltip("Seconds before the enemy can move after spawning.")]
    [SerializeField, Min(0f)] private float spawnMovementDelay = 1.5f;
    [Tooltip("3D SFX played at spawn position when enemy spawns.")]
    [SerializeField] private AudioClip spawnSFX;
    [SerializeField, Range(0f, 1f)] private float spawnSFXVolume = 1f;

    [Header("Night Hunt Behavior")]
    [Tooltip("Apply forced night hunt chase behavior to enemies spawned by this director.")]
    [SerializeField] private bool applyNightHuntModifier = true;
    [SerializeField, Min(1f)] private float nightHuntDetectionRange = 220f;
    [SerializeField, Min(1f)] private float nightHuntChaseLoseRange = 280f;
    [SerializeField, Min(0.1f)] private float nightHuntChaseSpeedMultiplier = 1.1f;
    [SerializeField] private bool nightHuntDisablePatrol = true;
    [Tooltip("Optional VFX prefab shown on enemies while night hunt modifier is active (e.g. aura, glow). Parented to enemy, removed when day arrives.")]
    [SerializeField] private GameObject nightHuntActiveVFXPrefab;
    [Tooltip("Uniform scale multiplier applied to the active night hunt VFX on each enemy.")]
    [SerializeField, Min(0.01f)] private float nightHuntVfxScaleMultiplier = 1f;

    [Header("Spawn validation")]
    [SerializeField] private float minHeightAboveSand = 0.02f;
    [SerializeField] private float maxSlope = 30f;

    [Header("Debug")]
    [SerializeField] private bool logDebug = true;

    private readonly List<Transform> activeEnemies = new List<Transform>();
    private readonly List<GameObject> activeSpawnVFX = new List<GameObject>();
    private Coroutine directorRoutine;
    private PlayerStats[] cachedPlayers;
    private float nextPlayerCacheRefreshTime;
    private WaitForSeconds waitNoAuthority;
    private WaitForSeconds waitNightTick;
    private WaitForSeconds waitDayTick;
    private float cachedNightInterval = -1f;
    private float cachedDayInterval = -1f;

    private void Start()
    {
        if (terrain == null) terrain = FindFirstObjectByType<Terrain>();
        if (terrainGenerator == null) terrainGenerator = FindFirstObjectByType<TerrainGenerator>();
        if (dayNight == null) dayNight = FindFirstObjectByType<DayNightCycleManager>();

        if (dayNight != null)
            dayNight.OnPhaseChanged += OnPhaseChanged;

        EnsureWaitCache();
        if (directorRoutine != null) StopCoroutine(directorRoutine);
        directorRoutine = StartCoroutine(CoDirector());
    }

    private void OnDestroy()
    {
        if (dayNight != null)
            dayNight.OnPhaseChanged -= OnPhaseChanged;

        if (directorRoutine != null)
            StopCoroutine(directorRoutine);

        for (int i = activeSpawnVFX.Count - 1; i >= 0; i--)
        {
            if (activeSpawnVFX[i] != null)
                Destroy(activeSpawnVFX[i]);
        }
        activeSpawnVFX.Clear();
    }

    private bool IsAuthority()
    {
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
            return PhotonNetwork.IsMasterClient;
        return true;
    }

    private IEnumerator CoDirector()
    {
        // Wait for terrain/map generation to be available.
        float timeout = 20f;
        float elapsed = 0f;
        while ((terrain == null || terrain.terrainData == null) && elapsed < timeout)
        {
            if (terrain == null) terrain = FindFirstObjectByType<Terrain>();
            elapsed += Time.deltaTime;
            yield return null;
        }

        while (true)
        {
            if (!IsAuthority())
            {
                yield return waitNoAuthority;
                continue;
            }

            CleanupDeadEnemies();

            bool isNight = dayNight != null ? dayNight.IsNight() : true;
            if (isNight)
            {
                if (enableNightDynamicSpawning)
                    MaintainNightEnemies();
                yield return waitNightTick;
            }
            else
            {
                // Night-only director: do nothing during day.
                yield return waitDayTick;
            }
        }
    }

    private void OnPhaseChanged(DayNightCycleManager.TimePhase _)
    {
        // NightHuntModifier on enemies handles revert/apply per-phase.
    }

    private int GetEffectiveNightMaxEnemies()
    {
        if (dayNight == null) return nightMaxEnemies;
        int day = dayNight.CurrentDay;
        int extraPerTwoNights = (Mathf.Max(0, day - 1) / 2) * 2;
        return nightMaxEnemies + extraPerTwoNights;
    }

    private void MaintainNightEnemies()
    {
        int effectiveMax = GetEffectiveNightMaxEnemies();
        if (activeEnemies.Count >= effectiveMax) return;

        var players = GetCachedPlayers();
        if (players == null || players.Length == 0) return;

        int attempts = 0;
        int maxAttempts = 40;
        while (attempts++ < maxAttempts && activeEnemies.Count < effectiveMax)
        {
            PlayerStats ps = players[Random.Range(0, players.Length)];
            if (ps == null) continue;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(Random.Range(
                nightMinDistanceFromPlayer * nightMinDistanceFromPlayer,
                nightMaxDistanceFromPlayer * nightMaxDistanceFromPlayer));

            Vector3 candidate = ps.transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            if (!TryValidateTerrainPoint(candidate, out Vector3 valid)) continue;
            if (IsTooCloseToEnemies(valid, minEnemySpacing)) continue;

            Transform spawned = SpawnEnemy(nightPhotonEnemyPrefabs, nightLocalEnemyPrefabs, valid);
            if (spawned == null) continue;
            SpawnVFXAtPosition(valid);
            PlaySpawnSFXAtPosition(valid);
            ApplySpawnMovementDelay(spawned);
            ApplyNightHuntIfNeeded(spawned);
            activeEnemies.Add(spawned);
            break;
        }
    }

    private Transform SpawnEnemy(string[] photonPrefabs, GameObject[] localPrefabs, Vector3 position)
    {
        bool hasPhoton = photonPrefabs != null && photonPrefabs.Length > 0;
        bool hasLocal = localPrefabs != null && localPrefabs.Length > 0;
        int len = Mathf.Max(hasPhoton ? photonPrefabs.Length : 0, hasLocal ? localPrefabs.Length : 0);
        if (len <= 0) return null;

        int idx = Random.Range(0, len);
        string photonName = hasPhoton && idx < photonPrefabs.Length ? photonPrefabs[idx] : null;
        GameObject local = hasLocal && idx < localPrefabs.Length ? localPrefabs[idx] : null;
        GameObject go = null;

        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            if (string.IsNullOrWhiteSpace(photonName))
                return null;
            try
            {
                go = PhotonNetwork.Instantiate(photonName, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            }
            catch
            {
                return null;
            }
        }
        else
        {
            if (local != null)
                go = Instantiate(local, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            else if (!string.IsNullOrWhiteSpace(photonName))
            {
                GameObject res = Resources.Load<GameObject>(photonName);
                if (res != null) go = Instantiate(res, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            }
        }

        return go != null ? go.transform : null;
    }

    private bool TryValidateTerrainPoint(Vector3 world, out Vector3 validWorld)
    {
        validWorld = world;
        if (terrain == null || terrain.terrainData == null) return false;
        var td = terrain.terrainData;
        Vector3 local = world - terrain.transform.position;
        if (local.x < 0f || local.z < 0f || local.x > td.size.x || local.z > td.size.z) return false;

        float nx = Mathf.Clamp01(local.x / td.size.x);
        float nz = Mathf.Clamp01(local.z / td.size.z);
        float hNorm = td.GetInterpolatedHeight(nx, nz) / td.size.y;
        float sand = terrainGenerator != null ? Mathf.Max(0.001f, terrainGenerator.sandThreshold) : 0.01f;
        if (hNorm <= sand + minHeightAboveSand) return false;

        float slope = td.GetSteepness(nx, nz);
        if (slope > maxSlope) return false;

        validWorld.y = terrain.SampleHeight(validWorld) + terrain.transform.position.y;
        return true;
    }

    private bool IsTooCloseToEnemies(Vector3 pos, float minSpacingMeters)
    {
        float sq = minSpacingMeters * minSpacingMeters;
        for (int i = 0; i < activeEnemies.Count; i++)
        {
            Transform e = activeEnemies[i];
            if (e == null) continue;
            if ((e.position - pos).sqrMagnitude < sq) return true;
        }
        return false;
    }

    private void CleanupDeadEnemies()
    {
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
        {
            if (activeEnemies[i] == null)
                activeEnemies.RemoveAt(i);
        }
    }

    private void SpawnVFXAtPosition(Vector3 position)
    {
        if (nightSpawnVFXPrefab == null) return;

        GameObject vfx = Instantiate(nightSpawnVFXPrefab, position, Quaternion.identity);
        vfx.SetActive(true);
        activeSpawnVFX.Add(vfx);
        float duration = spawnVFXDuration > 0f ? spawnVFXDuration : 5f;
        StartCoroutine(CleanupSpawnVFX(vfx, duration));
    }

    private void PlaySpawnSFXAtPosition(Vector3 position)
    {
        if (spawnSFX == null) return;

        GameObject go = new GameObject("SpawnSFX_OneShot");
        go.transform.position = position;
        var src = go.AddComponent<AudioSource>();
        src.clip = spawnSFX;
        src.volume = spawnSFXVolume;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 5f;
        src.maxDistance = 50f;
        src.playOnAwake = false;
        src.Play();
        Destroy(go, spawnSFX.length + 0.1f);
    }

    private void ApplySpawnMovementDelay(Transform enemyTransform)
    {
        if (spawnMovementDelay <= 0f || enemyTransform == null) return;

        var delay = enemyTransform.GetComponent<SpawnMovementDelay>();
        if (delay == null)
            delay = enemyTransform.gameObject.AddComponent<SpawnMovementDelay>();
        delay.Begin(spawnMovementDelay);
    }

    private IEnumerator CleanupSpawnVFX(GameObject vfx, float displayDuration)
    {
        if (vfx == null) yield break;

        float elapsed = 0f;
        while (elapsed < displayDuration && vfx != null)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (vfx == null)
        {
            activeSpawnVFX.Remove(vfx);
            yield break;
        }

        var particleSystems = vfx.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in particleSystems)
        {
            if (ps != null)
            {
                var emission = ps.emission;
                emission.enabled = false;
            }
        }

        yield return new WaitForSeconds(2f);

        if (vfx != null)
        {
            activeSpawnVFX.Remove(vfx);
            Destroy(vfx);
        }
    }

    private void ApplyNightHuntIfNeeded(Transform enemyTransform)
    {
        if (!applyNightHuntModifier || enemyTransform == null)
            return;

        NightHuntModifier modifier = enemyTransform.GetComponent<NightHuntModifier>();
        if (modifier == null)
            modifier = enemyTransform.gameObject.AddComponent<NightHuntModifier>();

        modifier.Configure(
            nightHuntDetectionRange,
            nightHuntChaseLoseRange,
            nightHuntChaseSpeedMultiplier,
            nightHuntDisablePatrol,
            nightHuntActiveVFXPrefab);
        modifier.SetVfxScaleMultiplier(nightHuntVfxScaleMultiplier);
    }

    private PlayerStats[] GetCachedPlayers()
    {
        if (cachedPlayers == null || Time.time >= nextPlayerCacheRefreshTime)
        {
            cachedPlayers = PlayerRegistry.ToArray();
            nextPlayerCacheRefreshTime = Time.time + Mathf.Max(0.25f, playerCacheRefreshInterval);
        }
        return cachedPlayers;
    }

    private void EnsureWaitCache()
    {
        if (waitNoAuthority == null)
            waitNoAuthority = new WaitForSeconds(1f);

        float safeNight = Mathf.Max(0.5f, nightSpawnInterval);
        if (waitNightTick == null || !Mathf.Approximately(cachedNightInterval, safeNight))
        {
            cachedNightInterval = safeNight;
            waitNightTick = new WaitForSeconds(cachedNightInterval);
        }

        float safeDay = Mathf.Max(0.25f, dayIdlePollInterval);
        if (waitDayTick == null || !Mathf.Approximately(cachedDayInterval, safeDay))
        {
            cachedDayInterval = safeDay;
            waitDayTick = new WaitForSeconds(cachedDayInterval);
        }
    }

    private void OnValidate()
    {
        EnsureWaitCache();
    }

    // Removed runtime AI-data mutation to preserve original enemy behavior.
}

