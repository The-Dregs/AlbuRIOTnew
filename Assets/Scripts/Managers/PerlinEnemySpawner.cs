using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

// spawns enemies (e.g., Tikbalang) over a terrain using Perlin noise masks
// multiplayer-safe: only MasterClient spawns using PhotonNetwork.Instantiate; offline falls back to local Instantiate
public class PerlinEnemySpawner : MonoBehaviourPunCallbacks
{
    [Header("terrain")]
    public Terrain terrain; // assign the Terrain in FIRSTMAP
    public TerrainGenerator terrainGenerator; // optional; if assigned, will use its sandThreshold for water/beach avoidance

    [Header("enemy prefabs (multiple)")]
    [Tooltip("Names of enemy prefabs under a Resources/ folder, for PhotonNetwork.Instantiate")]
    public string[] photonPrefabNames;
    [Tooltip("Local prefab fallbacks used when offline/Play Mode without Photon")]
    public GameObject[] localPrefabs;

    [Header("spawn distribution (perlin)")]
    [Tooltip("Desired number of enemies to spawn across the map")] public int desiredCount = 18;
    [Tooltip("Perlin frequency; smaller = larger features; tune to your island size")] public float perlinScale = 0.035f;
    [Range(0f, 1f)] public float perlinThreshold = 0.58f;
    public int seed = 0; // 0 = random
    [Tooltip("reduce threshold step-by-step if not enough valid spots found")] public float backoffStep = 0.05f;
    [Tooltip("minimum threshold when backing off")] public float minThreshold = 0.35f;

    [Header("placement filters")]
    [Tooltip("skip points with normalized height below this (water/beach). If TerrainGenerator is linked, this is auto-set")] public float minNormalizedHeight = 0.08f;
    [Tooltip("skip points with slope angle above this many degrees")] public float maxSlope = 28f;
    [Tooltip("enforce a minimum spacing between spawns (meters)")] public float minSpawnSpacing = 18f;

    [Header("debug")]
    public bool logSummary = true;
    public bool drawGizmos = false;
    [Range(0.1f, 2f)] public float gizmoSphere = 0.6f;

    private readonly List<Transform> _spawned = new List<Transform>();
    private Vector2 _offset;
    [System.NonSerialized] public int lastPlaced = 0;
    [System.NonSerialized] public int lastAttempts = 0;

    // Metrics for formulas 12.3 and 13.x
    [System.NonSerialized] public float lastInitialThreshold = 0f;
    [System.NonSerialized] public float lastFinalThreshold = 0f;
    [System.NonSerialized] public int lastBackoffSteps = 0;
    [System.NonSerialized] public int lastPerlinRejected = 0;
    [System.NonSerialized] public int lastHeightRejected = 0;
    [System.NonSerialized] public int lastSlopeRejected = 0;
    [System.NonSerialized] public int lastValidCandidates = 0; // passed perlin+height+slope, before spacing
    [System.NonSerialized] public int lastSpacingRejected = 0;

    void Start()
    {
        if (terrain == null)
        {
            terrain = FindFirstObjectByType<Terrain>();
        }
        if (terrainGenerator == null)
        {
            terrainGenerator = FindFirstObjectByType<TerrainGenerator>();
        }
        if (terrainGenerator != null)
        {
            // use the terrain's sand threshold + a small buffer to keep spawns inland
            minNormalizedHeight = Mathf.Max(minNormalizedHeight, terrainGenerator.sandThreshold + 0.02f);
        }

        // only master spawns in multiplayer; otherwise offline we can spawn locally
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            if (logSummary) Debug.Log("perlin spawner: not master, skipping spawn on this client");
            enabled = false; // nothing to do
            return;
        }

        // initialize noise offset for seed variance
        int useSeed = seed != 0 ? seed : Random.Range(int.MinValue, int.MaxValue);
        var prng = new System.Random(useSeed);
        _offset = new Vector2(prng.Next(-100000, 100000), prng.Next(-100000, 100000));

        SpawnAll();
    }

    public void ClearAll()
    {
        ApplyClearAll();
        // Sync with other players (buffered for persistence)
        if (PhotonNetwork.IsMasterClient)
        {
            photonView.RPC("RPC_ClearAll", RpcTarget.AllBuffered);
        }
    }
    
    [PunRPC]
    public void RPC_ClearAll()
    {
        ApplyClearAll();
    }
    
    private void ApplyClearAll()
    {
        foreach (var t in _spawned)
        {
            if (t == null) continue;
            if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
            {
                var pv = t.GetComponent<PhotonView>();
                if (pv != null && pv.IsMine)
                {
                    PhotonNetwork.Destroy(t.gameObject);
                }
                else if (pv == null)
                {
                    Destroy(t.gameObject);
                }
            }
            else
            {
                Destroy(t.gameObject);
            }
        }
        _spawned.Clear();
    }

    public void SpawnAll()
    {
        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning("perlin spawner: no terrain assigned");
            return;
        }

        ApplySpawnAll();
        // Sync with other players (buffered for persistence)
        if (PhotonNetwork.IsMasterClient)
        {
            photonView.RPC("RPC_SpawnAll", RpcTarget.AllBuffered);
        }
    }
    
    [PunRPC]
    public void RPC_SpawnAll()
    {
        ApplySpawnAll();
    }
    
    private void ApplySpawnAll()
    {
        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning("perlin spawner: no terrain assigned");
            return;
        }

        ApplyClearAll();

        var td = terrain.terrainData;
        Vector3 size = td.size; // x = width, y = height, z = length
        float threshold = perlinThreshold;
        lastInitialThreshold = threshold;
        lastBackoffSteps = 0;
        int placed = 0;
        int attempts = 0;
        int maxAttempts = desiredCount * 50;
        // Pre-allocate list with estimated capacity to reduce allocations
        var positions = new List<Vector3>(desiredCount);
        var prng = new System.Random(seed != 0 ? seed : Random.Range(int.MinValue, int.MaxValue));

        // reset per-run counters
        int perlinRejected = 0;
        int heightRejected = 0;
        int slopeRejected = 0;
        int validCandidates = 0;
        int spacingRejected = 0;

        // sampling strategy: random rejection sampling with perlin mask + constraints
        while (placed < desiredCount && attempts < maxAttempts)
        {
            attempts++;
            // pick a random normalized point
            float nx = Random.value;
            float nz = Random.value;
            // sample perlin
            float vx = (nx * size.x) * perlinScale + _offset.x;
            float vz = (nz * size.z) * perlinScale + _offset.y;
            float p = Mathf.PerlinNoise(vx, vz);
            if (p < threshold) { perlinRejected++; continue; }

            float hNorm = td.GetInterpolatedHeight(nx, nz) / size.y;
            if (hNorm < minNormalizedHeight) { heightRejected++; continue; }
            float slope = td.GetSteepness(nx, nz);
            if (slope > maxSlope) { slopeRejected++; continue; }

            validCandidates++;

            Vector3 world = new Vector3(nx * size.x, 0f, nz * size.z) + terrain.transform.position;
            // correct height using SampleHeight to align exactly on terrain
            world.y = terrain.SampleHeight(world) + terrain.transform.position.y;

            bool tooClose = false;
            foreach (var pt in positions)
            {
                if ((pt - world).sqrMagnitude < (minSpawnSpacing * minSpawnSpacing))
                { tooClose = true; break; }
            }
            if (tooClose) { spacingRejected++; continue; }

            // pick a random prefab index
            int prefabIndex = prng.Next(0, photonPrefabNames != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom ? photonPrefabNames.Length : localPrefabs.Length);

            Transform spawned = DoSpawn(world, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), prefabIndex);
            if (spawned != null)
            {
                positions.Add(world);
                _spawned.Add(spawned);
                placed++;
                // adjust grounding after colliders initialize
                StartCoroutine(GroundAfterInit(spawned));
            }

            // if we can't hit the target, relax threshold gradually
            if (attempts % 200 == 0 && placed < desiredCount && threshold > minThreshold)
            {
                float next = Mathf.Max(minThreshold, threshold - backoffStep);
                if (next < threshold) lastBackoffSteps++;
                threshold = next;
                if (logSummary) Debug.Log($"perlin spawner: backing off threshold to {threshold:F2} after {attempts} attempts, placed {placed}");
            }
        }

        lastPlaced = placed; lastAttempts = attempts;
        lastFinalThreshold = threshold;
        lastPerlinRejected = perlinRejected;
        lastHeightRejected = heightRejected;
        lastSlopeRejected = slopeRejected;
        lastValidCandidates = validCandidates;
        lastSpacingRejected = spacingRejected;
        if (logSummary)
        {
            Debug.Log($"perlin spawner: placed {placed}/{desiredCount} enemies in {attempts} attempts");
        }
    }

    private Transform DoSpawn(Vector3 position, Quaternion rotation)
    {
        return DoSpawn(position, rotation, 0);
    }

    // Overload to support prefab index
    private Transform DoSpawn(Vector3 position, Quaternion rotation, int prefabIndex)
    {
        GameObject go = null;
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            if (photonPrefabNames != null && photonPrefabNames.Length > 0 && prefabIndex < photonPrefabNames.Length)
            {
                string prefabName = photonPrefabNames[prefabIndex];
                if (!string.IsNullOrEmpty(prefabName))
                    go = PhotonNetwork.Instantiate(prefabName, position, rotation);
            }
            else if (localPrefabs != null && localPrefabs.Length > 0 && prefabIndex < localPrefabs.Length)
            {
                go = Instantiate(localPrefabs[prefabIndex], position, rotation);
            }
        }
        else
        {
            if (localPrefabs != null && localPrefabs.Length > 0 && prefabIndex < localPrefabs.Length)
            {
                go = Instantiate(localPrefabs[prefabIndex], position, rotation);
            }
            else if (photonPrefabNames != null && photonPrefabNames.Length > 0 && prefabIndex < photonPrefabNames.Length)
            {
                var res = Resources.Load<GameObject>(photonPrefabNames[prefabIndex]);
                if (res != null) go = Instantiate(res, position, rotation);
            }
        }
        if (go == null)
        {
            Debug.LogWarning("perlin spawner: failed to spawn (no prefab configured or not found)");
            return null;
        }
        return go.transform;
    }

    private System.Collections.IEnumerator GroundAfterInit(Transform t)
    {
        // wait end of frame so colliders/skins report correct bounds
        yield return null; // 1 frame
        if (t == null || terrain == null) yield break;

        // compute accurate ground via terrain height and a safety raycast
        Vector3 pos = t.position;
        float groundY = terrain.SampleHeight(pos) + terrain.transform.position.y;
        // optional raycast to catch non-terrain colliders (rocks etc.) slightly above terrain
        Ray ray = new Ray(new Vector3(pos.x, groundY + 50f, pos.z), Vector3.down);
        if (Physics.Raycast(ray, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore))
        {
            groundY = hit.point.y;
        }

        // adjust so the bottom of the main collider sits on ground
        float bottomY = float.NaN;
        // prefer CharacterController bounds, then any Collider in children
        var cc = t.GetComponentInChildren<CharacterController>();
        if (cc != null)
        {
            bottomY = cc.bounds.min.y;
        }
        else
        {
            var col = t.GetComponentInChildren<Collider>();
            if (col != null) bottomY = col.bounds.min.y;
        }

        if (!float.IsNaN(bottomY))
        {
            float delta = groundY - bottomY;
            pos.y += delta + 0.02f; // tiny lift to avoid z-fighting
        }
        else
        {
            // no collider found: just place transform origin on ground
            pos.y = groundY;
        }

        // apply; if Rigidbody present and kinematic, move directly
        var rb = t.GetComponent<Rigidbody>();
        if (rb != null && rb.isKinematic)
            rb.MovePosition(pos);
        else
            t.position = pos;
    }

    void OnDestroy()
    {
        // Stop all coroutines
        StopAllCoroutines();
        
        // Cleanup spawned enemies
        Cleanup();
    }
    
    /// <summary>
    /// Cleanup all spawned enemies and clear collections
    /// </summary>
    public void Cleanup()
    {
        ApplyClearAll();
        
        // Clear collections
        if (_spawned != null)
            _spawned.Clear();
    }
    
    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Gizmos.color = Color.red;
        foreach (var t in _spawned)
        {
            if (t == null) continue;
            Gizmos.DrawSphere(t.position, gizmoSphere);
        }
    }
}
