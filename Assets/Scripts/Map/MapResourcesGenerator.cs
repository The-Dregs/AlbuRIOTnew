using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using UnityEngine.SceneManagement;

[System.Serializable]
public class HerbResourceConfig
{
    [Tooltip("Photon prefab path (e.g. Items/3Items/Item_AloeVera)")]
    public string photonPrefab;
    [Tooltip("Local prefab for offline mode. Leave None to use Photon only.")]
    public GameObject localPrefab;
    [Tooltip("Target number of this herb type to spawn")]
    [Range(0, 200)] public int desiredCount = 60;
    [Tooltip("Perlin noise scale for distribution")]
    public float perlinScale = 0.05f;
    [Tooltip("Perlin threshold: higher = sparser spawns")]
    [Range(0f, 1f)] public float perlinThreshold = 0.6f;
    [Tooltip("Minimum distance between instances of this herb")]
    [Range(0.02f, 10f)] public float minSpacing = 6f;
}

// Generates level-1 map resources and points of interest over the procedurally generated island
// - Places 3 ship-remnant piles inland (not sand)
// - Scatters herb pickups using a Perlin mask
// - Places a broken ship quest area on the island edge (land just above sand)
// - Chooses up to 4 randomized player spawn points on the island edge and optionally spawns a single entrance prefab near spawn
// Networking: Only MasterClient performs placement; state is synchronized via buffered RPCs. Offline works with local instantiation.
[RequireComponent(typeof(PhotonView))]
public class MapResourcesGenerator : MonoBehaviourPunCallbacks
{
    private const string GeneratedRootName = "GeneratedEnvironment";
    private const string DecorCullingManagerName = "GeneratedDecorCullingManager";

    [Header("terrain")]
    [SerializeField] private Terrain terrain;
    [SerializeField] private TerrainGenerator terrainGenerator;

    [Header("remnants (inland)")]
    [Tooltip("Three distinct remnant prefabs.")]
    [SerializeField] private string[] remnantPhotonPrefabs = new string[3];
    [SerializeField] private GameObject[] remnantLocalPrefabs = new GameObject[3];
    [Tooltip("Optional guard enemies for each remnant index (0..2). Leave empty to disable a slot.")]
    [SerializeField] private string[] remnantGuardPhotonPrefabs = new string[3];
    [SerializeField] private GameObject[] remnantGuardLocalPrefabs = new GameObject[3];
    [SerializeField, Range(1f, 30f)] private float remnantGuardOffset = 4.5f;
    [SerializeField, Range(0.5f, 8f)] private float remnantGuardClearanceRadius = 1.6f;
    [SerializeField, Range(0.01f, 0.5f)] private float inlandAboveSand = 0.12f; // how far above sand to classify inland
    [SerializeField, Range(4f, 80f)] private float inlandMinSeparation = 18f; // spacing between remnants

    [Header("herb resources")]
    [Tooltip("Add herb types to spawn. Each entry has its own prefab, count, and Perlin distribution settings. Use the array size to add/remove entries.")]
    [SerializeField]
    private HerbResourceConfig[] herbResources = new HerbResourceConfig[]
    {
        new HerbResourceConfig { photonPrefab = "Items/3Items/Item_AloeVera", desiredCount = 25, perlinScale = 0.05f, perlinThreshold = 0.6f, minSpacing = 5f }
    };
    [Tooltip("Minimum height above the water surface required for herb spawns.")]
    [SerializeField, Range(0f, 2f)] private float herbAboveWaterOffset = 0.05f;

    [System.Serializable]
    public struct ResourceScatterMetrics
    {
        public string label;
        public int desiredCount;
        public int placed;
        public int attempts;
        public float perlinScale;
        public float perlinThreshold;
        public float minSpacing;
        public int perlinRejected;
        public int heightRejected;
        public int slopeRejected;
        public int biomeRejected;
        public int waterRejected;
        public int spacingRejected;
        public int validCandidates; // passed perlin+height+slope+water before spacing
    }

    [Header("testing (read-only)")]
    [SerializeField] private ResourceScatterMetrics[] lastHerbScatterMetrics;
    [SerializeField] private ResourceScatterMetrics lastPlantScatterMetrics;
    [SerializeField] private ResourceScatterMetrics lastFernScatterMetrics;
    [SerializeField] private ResourceScatterMetrics lastTreeScatterMetrics;
    [SerializeField] private ResourceScatterMetrics lastSmallRockScatterMetrics;
    [SerializeField] private ResourceScatterMetrics lastBigRockScatterMetrics;
    [SerializeField] private ResourceScatterMetrics lastCampPlacementMetrics;

    [System.Serializable]
    public struct RemnantPlacementMetrics
    {
        public int remnantsDesired;
        public int remnantsPlaced;
        public int remnantAttempts;
        public int clearanceRejected;
        public int spawnFailed;
        public int guardsDesired;
        public int guardsPlaced;
        public int guardSpawnFailed;
    }

    [System.Serializable]
    public struct BrokenShipPlacementMetrics
    {
        public int desired; // 0/1
        public int placed;  // 0/1
        public int attempts;
        public int clearanceRejected;
        public int spawnFailed;
        public float minDistanceFromSpawns;
    }

    [System.Serializable]
    public struct SpawnPlacementMetrics
    {
        public int spawnSlotsDesired;
        public int spawnSlotsCandidateCount;
        public int spawnSlotsFinalCount;
        public bool usingEntranceChildMarkers;

        public int entranceDesired; // 0/1
        public int entrancePlaced;  // 0/1
        public bool entranceReusedExisting;
        public int entranceCandidateTries;
    }

    [SerializeField] private RemnantPlacementMetrics lastRemnantPlacementMetrics;
    [SerializeField] private BrokenShipPlacementMetrics lastBrokenShipPlacementMetrics;
    [SerializeField] private SpawnPlacementMetrics lastSpawnPlacementMetrics;

    public IReadOnlyList<HerbResourceConfig> HerbResources => herbResources;
    public IReadOnlyList<ResourceScatterMetrics> LastHerbScatterMetrics => lastHerbScatterMetrics;
    public ResourceScatterMetrics LastPlantScatterMetrics => lastPlantScatterMetrics;
    public ResourceScatterMetrics LastFernScatterMetrics => lastFernScatterMetrics;
    public ResourceScatterMetrics LastTreeScatterMetrics => lastTreeScatterMetrics;
    public ResourceScatterMetrics LastSmallRockScatterMetrics => lastSmallRockScatterMetrics;
    public ResourceScatterMetrics LastBigRockScatterMetrics => lastBigRockScatterMetrics;
    public ResourceScatterMetrics LastCampPlacementMetrics => lastCampPlacementMetrics;
    public RemnantPlacementMetrics LastRemnantPlacementMetrics => lastRemnantPlacementMetrics;
    public BrokenShipPlacementMetrics LastBrokenShipPlacementMetrics => lastBrokenShipPlacementMetrics;
    public SpawnPlacementMetrics LastSpawnPlacementMetrics => lastSpawnPlacementMetrics;

    [Header("broken ship quest area (edge)")]
    [SerializeField] private string brokenShipPhotonPrefab;
    [SerializeField] private GameObject brokenShipLocalPrefab;
    [SerializeField, Range(0.01f, 0.2f)] private float edgeBandAboveSand = 0.03f; // how far above sand to consider edge
    [SerializeField, Range(0.01f, 0.2f)] private float edgeBandWidth = 0.04f;
    [SerializeField, Range(5f, 500f)] private float minBrokenShipToSpawnDistance = 80f; // keep broken ship far from spawn area
    [SerializeField, Range(-180f, 180f)] private float brokenShipFacingYawOffset = 0f; // additional yaw on top of inland-facing rotation

    [Header("spawn points (up to 4 players)")]
    [Tooltip("Assign the water plane/object. Spawns will always be placed above this surface.")]
    [SerializeField] private Transform waterTransform;
    [Tooltip("If true, water surface Y is taken from the assigned water object's Renderer bounds (max.y). If false, uses waterTransform.position.y (recommended for a simple WaterCube reference).")]
    [SerializeField] private bool waterUseRendererBounds = false;
    [SerializeField, Range(1, 4)] private int maxPlayers = 4;
    [SerializeField] private string playerSpawnMarkerPhotonPrefab; // optional marker prefab to visualize spawn points
    [SerializeField] private GameObject playerSpawnMarkerLocalPrefab;
    [SerializeField] private string spawnEntrancePhotonPrefab;
    [SerializeField] private GameObject spawnEntranceLocalPrefab;
    [SerializeField, Range(1f, 30f)] private float spawnSpacing = 10f; // spacing between spawn points
    [Tooltip("How far up the beach band to spawn (0 = water edge, 1 = grass line). 0.6-0.8 is a good dry beach.")]
    [SerializeField, Range(0f, 1f)] private float spawnBeachPosition = 0.7f;
    [SerializeField, Range(0f, 30f)] private float spawnEntranceInlandOffset = 8f;
    [SerializeField, Range(-3f, 3f)] private float spawnEntranceVerticalOffset = 0f;
    [Tooltip("Minimum height above the water surface required for player spawns.")]
    [SerializeField, Range(0f, 3f)] private float spawnAboveWaterOffset = 0.35f;
    [Tooltip("Minimum height above the water surface required for SpawnArea objects (SpawnEntrance / boat / guides). Increase this to force the whole SpawnArea to sit higher above the assigned Water transform.")]
    [SerializeField, Range(0f, 10f)] private float spawnAreaAboveWaterOffset = 0.35f;

    [Header("edge detection (shared by spawns, broken ship, etc.)")]
    [SerializeField, Range(0.5f, 0.95f)] private float edgeOuterRingMinRadius = 0.72f;
    [SerializeField, Range(0.1f, 1f)] private float edgeOuterWaterNeighborRatio = 0.3f;
    [SerializeField, Range(2f, 80f)] private float edgeOuterNeighborCheckRadius = 16f;

    [Header("understory plants (destructible)")]
    [Tooltip("Photon prefab names for networked/interactable plants (index-aligned with local prefabs). Leave empty to use local prefabs.")]
    [SerializeField] private string[] plantPhotonPrefabs;
    [SerializeField] private GameObject[] plantLocalPrefabs; // assign your fern/palm prefabs here
    [SerializeField, Range(0, 3000)] private int plantDesiredCount = 800;
    [SerializeField, Range(0.2f, 20f)] private float plantMinSpacing = 3.0f;
    [SerializeField, Range(0.1f, 3f)] private float plantScaleMin = 1.0f; // used only if plantKeepPrefabScale = false
    [SerializeField] private bool plantKeepPrefabScale = true; // keep original prefab scale for destructible plants
    [SerializeField, Range(0, 1500)] private int maxNetworkedPlants = 300; // cap to avoid Photon viewID exhaustion in large maps

    [Header("ferns (decor, non-destructible)")]
    [Tooltip("Photon prefab names for decorative ferns (optional). Index-aligned with local prefabs.")]
    [SerializeField] private string[] fernPhotonPrefabs;
    [SerializeField] private GameObject[] fernLocalPrefabs;
    [SerializeField, Range(0, 3000)] private int fernDesiredCount = 900;
    [SerializeField, Range(0.2f, 20f)] private float fernMinSpacing = 3.0f;
    [SerializeField, Range(0.1f, 3f)] private float fernScaleMin = 0.8f;
    [SerializeField, Range(0.1f, 3f)] private float fernScaleMax = 1.3f;

    [Header("trees (grass-only)")]
    [Tooltip("Photon prefab names for trees (optional). Index-aligned with local prefabs.")]
    [SerializeField] private string[] treePhotonPrefabs;
    [SerializeField] private GameObject[] treeLocalPrefabs;
    [SerializeField, Range(0, 20000)] private int treeDesiredCount = 1200;
    [SerializeField, Range(1f, 40f)] private float treeMinSpacing = 6f;
    [SerializeField, Range(0.1f, 3f)] private float treeScaleMin = 0.8f;
    [SerializeField, Range(0.1f, 3f)] private float treeScaleMax = 1.4f;

    [Header("rocks (small & big)")]
    [Tooltip("Photon prefab names for small rocks (optional). Index-aligned with local prefabs.")]
    [SerializeField] private string[] smallRockPhotonPrefabs;
    [SerializeField] private GameObject[] smallRockLocalPrefabs;
    [SerializeField, Range(0, 20000)] private int smallRockDesiredCount = 160;
    [SerializeField, Range(1f, 60f)] private float smallRockMinSpacing = 6f;
    [Tooltip("Photon prefab names for big rocks (optional). Index-aligned with local prefabs.")]
    [SerializeField] private string[] bigRockPhotonPrefabs;
    [SerializeField] private GameObject[] bigRockLocalPrefabs;
    [SerializeField, Range(0, 20000)] private int bigRockDesiredCount = 60;
    [SerializeField, Range(1f, 80f)] private float bigRockMinSpacing = 14f;

    [Header("enemy camps (placer)")]
    [Tooltip("Place camp prefabs across inland map areas.")]
    [SerializeField] private bool enableCampGeneration = true;
    [Tooltip("Photon prefab names for camps. Leave empty slots to use local prefab name as fallback.")]
    [SerializeField] private string[] campPhotonPrefabs;
    [Tooltip("Offline/local camp prefabs. Also used to infer Photon prefab name if omitted.")]
    [SerializeField] private GameObject[] campLocalPrefabs;
    [SerializeField, Range(0, 32)] private int campCount = 4;
    [SerializeField, Range(8f, 200f)] private float campMinSpacing = 70f;
    [SerializeField, Range(1f, 40f)] private float campClearanceRadius = 10f;
    [SerializeField, Range(0.02f, 0.6f)] private float campInlandAboveSand = 0.12f;
    [SerializeField] private bool randomizeCampYaw = true;


    [Header("filters & misc")]
    [Tooltip("Minimum normalized terrain height (0-1) for grass/plants/ferns. Must be above beach/water. Increase if grass spawns in water.")]
    [SerializeField, Range(0.05f, 0.5f)] private float minGrassHeight = 0.22f;
    [SerializeField, Range(0f, 60f)] private float maxSlope = 28f;
    [SerializeField] private bool logSummary = true;
    [SerializeField] private LayerMask groundRaycastMask = ~0; // used when snapping to ground; can exclude object layers

    [Header("Performance")]
    [Tooltip("Items to spawn per frame before yielding. Lower = smoother but slower total. 0 = no frame spread (all at once).")]
    [SerializeField, Range(0, 200)] private int itemsPerFrame = 15;

    [Tooltip("Max rejection-sample attempts per frame before yielding. Prevents long stalls in scatter loops.")]
    [SerializeField, Range(50, 5000)] private int maxAttemptsPerFrame = 400;

    [Tooltip("Disables this generator component after a successful generation pass to remove ongoing runtime overhead.")]
    [SerializeField] private bool disableComponentAfterGeneration = true;

    // Exposed for testing tools: keep default behavior unless a test run overrides it.
    public bool DisableComponentAfterGeneration
    {
        get => disableComponentAfterGeneration;
        set => disableComponentAfterGeneration = value;
    }

    // Exposed for testing tools: speed up scatter loops without changing placement rules.
    public int TestingItemsPerFrame
    {
        get => itemsPerFrame;
        set => itemsPerFrame = value;
    }

    public int TestingMaxAttemptsPerFrame
    {
        get => maxAttemptsPerFrame;
        set => maxAttemptsPerFrame = value;
    }

    [Header("Decor density scaling")]
    [Tooltip("Global density scale for decorative scatter (trees/ferns/rocks). 1 = use desired counts.")]
    [SerializeField, Range(0.05f, 1f)] private float globalDecorDensityScale = 1f;
    [Tooltip("Extra density scale applied only when in multiplayer room for decorative scatter.")]
    [SerializeField] private bool reduceDecorDensityInMultiplayer = true;
    [SerializeField, Range(0.05f, 1f)] private float multiplayerDecorDensityScale = 0.6f;
    [SerializeField] private bool capDecorCountInMultiplayer = true;
    [SerializeField, Range(0, 5000)] private int multiplayerMaxTrees = 320;
    [SerializeField, Range(0, 5000)] private int multiplayerMaxFerns = 260;
    [SerializeField, Range(0, 5000)] private int multiplayerMaxSmallRocks = 140;
    [SerializeField, Range(0, 5000)] private int multiplayerMaxBigRocks = 80;

    [Header("Runtime decor culling")]
    [SerializeField] private bool enableRuntimeDecorCulling = true;
    [SerializeField] private bool skipLodGroupObjectsInRuntimeCulling = true;
    [SerializeField, Range(40f, 500f)] private float decorHideDistance = 140f;
    [SerializeField, Range(0.5f, 0.99f)] private float decorShowHysteresis = 0.86f;
    [SerializeField, Range(20f, 300f)] private float decorColliderDisableDistance = 90f;
    [SerializeField, Range(0.05f, 2f)] private float decorCullingUpdateInterval = 0.2f;
    [SerializeField, Range(20, 4000)] private int decorMaxTogglesPerTick = 350;
    [SerializeField] private bool decorCullingAlsoDisablesColliders = false;
    [SerializeField] private bool showDecorCullingDebugOverlay = false;

    private readonly List<Transform> spawned = new List<Transform>();
    private List<Vector3> occupied; // global positions to prevent cross-category overlap
    private Vector2 perlinOffset;
    private float[,,] cachedAlphamap; // avoids per-call GetAlphamaps allocations
    private float[,] cachedHeightNorm; // [y,x] normalized height, filled once from terrain
    private float[,] cachedSlope;      // [y,x] slope degrees, filled once from terrain
    private Dictionary<int, List<Vector3>> spatialGrid; // cell -> positions for O(1) proximity
    private float spatialCellSize = 8f; // grid cell size in meters
    private int _itemsPlacedThisFrame; // for frame-spread yielding
    private int _attemptsThisFrame; // for attempt-based yielding in scatter loops
    private Coroutine generateRoutine;
    private GeneratedDecorCullingManager decorCullingManager;
    private static List<Vector3> sharedSpawnPositions = new List<Vector3>(); // Shared spawn positions for non-master clients
    private int latestTerrainSeedGenerationId = -1;
    private int lastAppliedTerrainGenerationId = -1;
    private int decorSeed;
    private bool hasDecorSeed;
    private int latestDecorSeedGenerationId = -1;
    private static readonly Collider[] _spawnBlockerBuffer = new Collider[32];
    private bool generationTriggeredByEvent; // prevents Start() from duplicating a generation already started by HandleTerrainGenerationCompleted
    private bool generationPassInProgress;

    // Testing/automation helpers (read-only)
    public bool IsGenerating => generateRoutine != null;
    public int LastCompletedTerrainGenerationId { get; private set; } = -1;

    // computed from the final generated heightmap; used to spawn on the actual island (centered), not the terrain border.
    private bool hasIslandFootprint;
    private Vector2 islandCenterLocalXZ;
    private float islandRadiusLocal;

    private void OnEnable()
    {
        TerrainGenerator.OnGenerationCompleted += HandleTerrainGenerationCompleted;
    }

    void Start()
    {
        ResolveActiveTerrainAndGenerator();

        if (terrain == null) terrain = FindFirstObjectByType<Terrain>();
        if (terrainGenerator == null) terrainGenerator = FindFirstObjectByType<TerrainGenerator>();

        // if HandleTerrainGenerationCompleted already fired (e.g. synchronously from TerrainGenerator.Start),
        // skip starting a second concurrent generation coroutine
        if (generationTriggeredByEvent)
        {
            if (logSummary) Debug.Log("[MapResourcesGenerator] Start() skipped; generation already triggered by terrain event.");
            return;
        }

        // Non-master still runs local deterministic decor generation once seeds are synced.
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            if (logSummary) Debug.Log("MapResourcesGenerator: joiner mode, waiting for terrain/decor seed sync.");
            generateRoutine = StartCoroutine(GenerateWhenTerrainReady());
            return;
        }

        int seed = Random.Range(int.MinValue, int.MaxValue);
        var prng = new System.Random(seed);
        perlinOffset = new Vector2(prng.Next(-100000, 100000), prng.Next(-100000, 100000));

        // Authoritative (master/offline): ensure TerrainGenerator runs FIRST, then resources generate AFTER.
        // This prevents GenerateOnStart / manual regen timing from producing mismatched caches/spawns.
        if (terrainGenerator != null)
        {
            // Make MapResourcesGenerator the orchestrator: stop TerrainGenerator from auto-running out of order.
            terrainGenerator.generateOnStart = false;

            bool terrainAlreadyReady = terrainGenerator.IsGenerationComplete && !terrainGenerator.IsGenerationInProgress && terrainGenerator.LastGenerationId > 0;
            if (!terrainAlreadyReady && !terrainGenerator.IsGenerationInProgress)
            {
                if (logSummary) Debug.Log("[MapResourcesGenerator] Triggering TerrainGenerator.GenerateTerrain() before spawning resources.");
                terrainGenerator.GenerateTerrain();
                generationTriggeredByEvent = true; // generation completion event will trigger GenerateWhenTerrainReady
            }
        }

        // Safety net: if the completion event doesn't fire (or terrain was already ready), we still wait and then generate.
        generateRoutine = StartCoroutine(GenerateWhenTerrainReady());
    }

    private void HandleTerrainGenerationCompleted(int generationId)
    {
        if (!isActiveAndEnabled)
            return;

        generationTriggeredByEvent = true;

        if (generateRoutine != null)
        {
            StopCoroutine(generateRoutine);
            generateRoutine = null;
        }

        if (IsAuthoritativeGenerator())
        {
            // sync seeds to joiners so they produce the same terrain and deterministic decor
            SyncTerrainSeedToClients(generationId);
        }

        generateRoutine = StartCoroutine(GenerateWhenTerrainReady());
    }

    /// <summary>
    /// Master sends the terrain seed to all other clients via buffered RPC.
    /// Joiners will generate the same terrain from this seed.
    /// </summary>
    private void SyncTerrainSeedToClients(int generationId)
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
            return;
        if (terrainGenerator == null) return;
        if (photonView == null) return;

        int terrainSeed = terrainGenerator.seed;
        latestTerrainSeedGenerationId = generationId;
        photonView.RPC(nameof(RPC_SyncTerrainSeed), RpcTarget.OthersBuffered, terrainSeed, generationId);
        if (logSummary) Debug.Log($"[MapResourcesGenerator] Synced terrain seed {terrainSeed} for generation {generationId} to clients.");
    }

    [PunRPC]
    private void RPC_SyncTerrainSeed(int terrainSeed, int generationId)
    {
        if (latestTerrainSeedGenerationId > 0 && generationId > 0 && generationId < latestTerrainSeedGenerationId)
        {
            if (logSummary) Debug.Log($"[MapResourcesGenerator] Ignoring stale terrain seed for generation {generationId}; latest is {latestTerrainSeedGenerationId}.");
            return;
        }

        if (logSummary) Debug.Log($"[MapResourcesGenerator] Received terrain seed {terrainSeed} from master.");

        if (terrainGenerator == null)
            terrainGenerator = FindFirstObjectByType<TerrainGenerator>();

        if (terrainGenerator != null)
        {
            latestTerrainSeedGenerationId = Mathf.Max(latestTerrainSeedGenerationId, generationId);
            terrainGenerator.GenerateWithSeed(terrainSeed);
        }
        else
        {
            Debug.LogWarning("[MapResourcesGenerator] RPC_SyncTerrainSeed: TerrainGenerator not found!");
        }
    }

    [PunRPC]
    private void RPC_SyncDecorSeed(int syncedDecorSeed, int generationId)
    {
        if (latestDecorSeedGenerationId > 0 && generationId > 0 && generationId < latestDecorSeedGenerationId)
        {
            if (logSummary) Debug.Log($"[MapResourcesGenerator] Ignoring stale decor seed for generation {generationId}; latest is {latestDecorSeedGenerationId}.");
            return;
        }

        decorSeed = syncedDecorSeed;
        hasDecorSeed = true;
        if (generationId > 0)
            latestDecorSeedGenerationId = generationId;
        if (logSummary) Debug.Log($"[MapResourcesGenerator] Received decor seed {decorSeed} for generation {generationId}.");
    }

    private bool IsAuthoritativeGenerator()
    {
        return !(PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient);
    }

    private int ComputeDecorSeed(int terrainSeed, int generationId)
    {
        unchecked
        {
            int g = generationId <= 0 ? 1 : generationId;
            return (terrainSeed * 73856093) ^ (g * 19349663) ^ 0x2C3A5F17;
        }
    }

    private void SyncDecorSeedToClients(int generationId)
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
            return;
        if (terrainGenerator == null || photonView == null)
            return;

        decorSeed = ComputeDecorSeed(terrainGenerator.seed, generationId);
        hasDecorSeed = true;
        latestDecorSeedGenerationId = generationId;
        photonView.RPC(nameof(RPC_SyncDecorSeed), RpcTarget.OthersBuffered, decorSeed, generationId);
        if (logSummary) Debug.Log($"[MapResourcesGenerator] Synced decor seed {decorSeed} for generation {generationId}.");
    }

    private System.Collections.IEnumerator GenerateWhenTerrainReady()
    {
        // Always clear generateRoutine when the coroutine ends, even on early exits.
        // (Unity Coroutines keep the Coroutine handle non-null after completion, so we must null it ourselves
        // or test tools waiting on IsGenerating will time out.)
        try
        {
            // wait until terrain generation is fully complete before spawning map resources.
            float timeout = 15f;
            float waited = 0f;
            int readyGenerationId = -1;
            while (true)
            {
                ResolveActiveTerrainAndGenerator();
                bool ready = terrain != null && terrain.terrainData != null;
                if (ready && terrainGenerator != null)
                {
                    ready = terrainGenerator.IsGenerationComplete && !terrainGenerator.IsGenerationInProgress && terrainGenerator.LastGenerationId > 0;
                    if (ready)
                        readyGenerationId = terrainGenerator.LastGenerationId;
                }
                if (ready) break;
                if (waited > timeout) break;
                yield return null; waited += Time.deltaTime;
            }

            // Give TerrainGenerator one full frame to finish any late edits (splat/details/normal updates),
            // then resolve references again so our caches match the final, post-modification terrain.
            yield return null;
            yield return new WaitForEndOfFrame();
            ResolveActiveTerrainAndGenerator();

            if (readyGenerationId > 0 && readyGenerationId == lastAppliedTerrainGenerationId)
            {
                if (logSummary) Debug.Log($"[MapResourcesGenerator] Terrain generation {readyGenerationId} already consumed; skipping duplicate resource generation.");
                yield break;
            }

            if (generationPassInProgress)
            {
                if (logSummary) Debug.Log("[MapResourcesGenerator] Generation pass already in progress; skipping duplicate run.");
                yield break;
            }

            generationPassInProgress = true;
            if (IsAuthoritativeGenerator())
            {
                int generationId = readyGenerationId > 0 ? readyGenerationId : (terrainGenerator != null ? terrainGenerator.LastGenerationId : 1);
                SyncTerrainSeedToClients(generationId);
                SyncDecorSeedToClients(generationId);
                if (!hasDecorSeed)
                {
                    decorSeed = ComputeDecorSeed(terrainGenerator != null ? terrainGenerator.seed : 1, generationId);
                    hasDecorSeed = true;
                    latestDecorSeedGenerationId = generationId;
                }
                yield return ApplyGenerateAllCoroutine();
            }
            else
            {
                int targetGenerationId = readyGenerationId > 0 ? readyGenerationId : (terrainGenerator != null ? terrainGenerator.LastGenerationId : 1);
                float seedWait = 0f;
                const float seedWaitTimeout = 8f;
                while (seedWait < seedWaitTimeout)
                {
                    bool hasMatchingSeed = hasDecorSeed && latestDecorSeedGenerationId >= targetGenerationId;
                    if (hasMatchingSeed)
                        break;

                    seedWait += Time.deltaTime;
                    yield return null;
                }

                if (!(hasDecorSeed && latestDecorSeedGenerationId >= targetGenerationId))
                {
                    decorSeed = ComputeDecorSeed(terrainGenerator != null ? terrainGenerator.seed : 1, targetGenerationId);
                    hasDecorSeed = true;
                    latestDecorSeedGenerationId = targetGenerationId;
                    Debug.LogWarning($"[MapResourcesGenerator] Decor seed RPC timeout. Using deterministic fallback seed {decorSeed}.");
                }

                yield return ApplyGenerateLocalDecorOnlyCoroutine();
            }

            if (terrainGenerator != null && terrainGenerator.LastGenerationId > 0)
                lastAppliedTerrainGenerationId = terrainGenerator.LastGenerationId;
        }
        finally
        {
            generationPassInProgress = false;
            generateRoutine = null;
        }
    }

    public void GenerateAll()
    {
        // Master (or offline) does the spawning. PhotonNetwork.Instantiate will replicate to others; no extra RPC needed.
        if (generationPassInProgress)
        {
            if (logSummary) Debug.Log("[MapResourcesGenerator] GenerateAll() ignored because a generation pass is already running.");
            return;
        }
        if (generateRoutine != null)
        {
            StopCoroutine(generateRoutine);
            generateRoutine = null;
        }
        generateRoutine = StartCoroutine(GenerateWhenTerrainReady());
    }

    private IEnumerator ApplyGenerateAllCoroutine()
    {
        ResolveActiveTerrainAndGenerator();

        if (terrain == null || terrain.terrainData == null || terrainGenerator == null)
        {
            Debug.LogWarning("MapResourcesGenerator: missing Terrain/TerrainGenerator");
            yield break;
        }

        // Clean up old spawned objects before generating new ones
        CleanupOldObjects();

        // Clear collections (optimize memory)
        if (spawned != null)
            spawned.Clear();
        if (occupied != null)
            occupied.Clear();

        // Pre-allocate with reasonable capacity to reduce allocations
        occupied = new List<Vector3>(4096);

        // Cache alphamap and height/slope once so scatter loops only do array lookups (no raycasts/terrain API in loops).
        var td = terrain.terrainData;
        int aw = td.alphamapWidth;
        int ah = td.alphamapHeight;
        cachedAlphamap = td.GetAlphamaps(0, 0, aw, ah);
        cachedHeightNorm = new float[ah, aw];
        cachedSlope = new float[ah, aw];
        float sizeY = td.size.y;
        for (int ay = 0; ay < ah; ay++)
        {
            for (int ax = 0; ax < aw; ax++)
            {
                float nx = aw > 1 ? ax / (float)(aw - 1) : 0f;
                float nz = ah > 1 ? ay / (float)(ah - 1) : 0f;
                cachedHeightNorm[ay, ax] = td.GetInterpolatedHeight(nx, nz) / sizeY;
                cachedSlope[ay, ax] = td.GetSteepness(nx, nz);
            }
            if (ay > 0 && (ay % 32) == 0) yield return null; // spread cache build over frames to avoid lag spike
        }

        // Determine the actual island footprint from the finalized height cache.
        ComputeIslandFootprintFromCache();

        // Init spatial grid for O(1) proximity
        spatialGrid = new Dictionary<int, List<Vector3>>();
        _itemsPlacedThisFrame = 0;
        _attemptsThisFrame = 0;

        // 1) Cache scene roots and prepare local runtime systems.
        var questRoot = GetSceneRoot("Quest");
        var markerRoot = GetSceneRoot("SpawnMarkers");
        var spawnAreaRoot = GetSceneRoot("SpawnArea");
        EnsureDecorCullingManager();

        // 2) Non-interactable decor first (so interactables can avoid these colliders).
        yield return ScatterDeterministicDecor();

        // 3) Place exactly three ship remnants inland, with optional guards.
        lastRemnantPlacementMetrics = default;
        var inlandPositions = FindInlandPositions(12, inlandAboveSand, inlandMinSeparation);
        var remnantsRoot = GetSceneRoot("Remnants");
        var remnantGuardsRoot = GetSceneRoot("RemnantGuards");
        ClearChildren(remnantsRoot);
        ClearChildren(remnantGuardsRoot);
        int remnantsPlaced = 0;
        int remnantAttempts = 0;
        int remnantClearRejected = 0;
        int remnantSpawnFailed = 0;
        int guardsDesired = 0;
        int guardsPlaced = 0;
        int guardSpawnFailed = 0;
        for (int i = 0; i < inlandPositions.Count && remnantsPlaced < 3; i++)
        {
            remnantAttempts++;
            Vector3 pos = inlandPositions[i];
            if (!TryResolveClearPlacement(pos, 2.75f, out pos))
            {
                remnantClearRejected++;
                continue;
            }

            int remIdx = remnantsPlaced;
            string remPhoton = remIdx < remnantPhotonPrefabs.Length ? remnantPhotonPrefabs[remIdx] : null;
            GameObject remLocal = remIdx < remnantLocalPrefabs.Length ? remnantLocalPrefabs[remIdx] : null;
            var rem = SpawnPrefab(remPhoton, remLocal, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), true);
            if (rem != null)
            {
                rem.name = $"GeneratedRemnant_{remnantsPlaced + 1}";
                rem.SetParent(remnantsRoot, true);
                TrySyncGeneratedParentForNetwork(rem, "Remnants");
                spawned.Add(rem);
                ReserveOccupied(pos);
            }
            else
            {
                remnantSpawnFailed++;
                continue;
            }

            // Guard desired if config exists for this remnant slot
            bool hasGuardConfig =
                (remnantGuardPhotonPrefabs != null && remIdx < remnantGuardPhotonPrefabs.Length && !string.IsNullOrWhiteSpace(remnantGuardPhotonPrefabs[remIdx])) ||
                (remnantGuardLocalPrefabs != null && remIdx < remnantGuardLocalPrefabs.Length && remnantGuardLocalPrefabs[remIdx] != null);
            if (hasGuardConfig) guardsDesired++;

            if (SpawnGuardForRemnant(remIdx, pos, remnantGuardsRoot))
                guardsPlaced++;
            else if (hasGuardConfig)
                guardSpawnFailed++;
            remnantsPlaced++;
        }

        lastRemnantPlacementMetrics = new RemnantPlacementMetrics
        {
            remnantsDesired = 3,
            remnantsPlaced = remnantsPlaced,
            remnantAttempts = remnantAttempts,
            clearanceRejected = remnantClearRejected,
            spawnFailed = remnantSpawnFailed,
            guardsDesired = guardsDesired,
            guardsPlaced = guardsPlaced,
            guardSpawnFailed = guardSpawnFailed
        };

        // 4) Place camp prefabs across inland map areas.
        yield return SpawnGeneratedEnemyCamps();

        // 5) Herbs scattered by perlin
        yield return ScatterHerbs();

        // 6) Understory plants on strong grass, low slope
        yield return ScatterPlants();

        // 7) Build spawn points first, then place broken ship far from those spawns.
        lastBrokenShipPlacementMetrics = default;
        lastSpawnPlacementMetrics = default;
        // target the upper portion of the beach band so spawns are on dry sand, not underwater
        float beachMinH = ComputeBeachSpawnMinHeight();
        Vector3 spawnBase = FindIslandShorePosition(spawnSpacing, maxSlope, beachMinH);
        var spawns = GenerateClusteredEdgeSpawnsFromBase(spawnBase, maxPlayers, spawnSpacing, maxSlope);
        spawns = ResolveSpawnPositions(spawns, spawnSpacing, 1.6f, beachMinH);
        int spawnCandidateCount = spawns != null ? spawns.Count : 0;

        int brokenDesired = (!string.IsNullOrWhiteSpace(brokenShipPhotonPrefab) || brokenShipLocalPrefab != null) ? 1 : 0;
        int brokenAttempts = brokenDesired > 0 ? 1 : 0;
        int brokenClearRejected = 0;
        int brokenSpawnFailed = 0;
        int brokenPlaced = 0;

        Vector3 brokenEdge = FindIslandShorePositionFarFromPoints(spawns, spawnSpacing, maxSlope, minBrokenShipToSpawnDistance, beachMinH);
        if (brokenDesired > 0 && TryResolveClearPlacement(brokenEdge, 4.5f, out brokenEdge))
        {
            RemoveExistingBrokenShipQuest();
            var broken = SpawnPrefab(brokenShipPhotonPrefab, brokenShipLocalPrefab, brokenEdge, GetBrokenShipShoreFacingRotation(brokenEdge), true);
            if (broken != null)
            {
                broken.name = "BrokenShipQuest";
                broken.SetParent(questRoot, true);
                spawned.Add(broken);
                ReserveOccupied(brokenEdge);
                brokenPlaced = 1;
            }
            else
            {
                brokenSpawnFailed++;
            }
        }
        else if (brokenDesired > 0)
        {
            brokenClearRejected++;
        }

        lastBrokenShipPlacementMetrics = new BrokenShipPlacementMetrics
        {
            desired = brokenDesired,
            placed = brokenPlaced,
            attempts = brokenAttempts,
            clearanceRejected = brokenClearRejected,
            spawnFailed = brokenSpawnFailed,
            minDistanceFromSpawns = minBrokenShipToSpawnDistance
        };

        RemoveExistingSpawnMarkers();

        bool usingEntranceChildMarkers = false;
        List<Vector3> finalSpawns = new List<Vector3>(spawns);

        // clamp all spawn candidates above water surface as a safety net
        for (int i = 0; i < finalSpawns.Count; i++)
            finalSpawns[i] = ClampAboveWater(finalSpawns[i], spawnAboveWaterOffset);

        // Place a single optional entrance prefab near the first spawn marker.
        int entranceDesired = (!string.IsNullOrWhiteSpace(spawnEntrancePhotonPrefab) || spawnEntranceLocalPrefab != null) ? 1 : 0;
        int entrancePlaced = 0;
        bool entranceReusedExisting = false;
        int entranceCandidateTries = 0;
        if (spawns.Count > 0)
        {
            Vector3 basePos = spawns[0];
            Vector3 terrainCenter = terrain.transform.position + new Vector3(terrain.terrainData.size.x * 0.5f, 0f, terrain.terrainData.size.z * 0.5f);
            Vector3 toCenter = terrainCenter - basePos;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude < 0.01f) toCenter = Vector3.forward;
            toCenter.Normalize();

            // Pick an entrance position that is guaranteed to be on island terrain (not just above water).
            // We try the desired inland offset first, then progressively pull it back toward the shoreline
            // spawn until the candidate is inside terrain bounds and above the beach minimum height band.
            Vector3 entrancePos = basePos;
            if (terrain != null && terrain.terrainData != null)
            {
                var terrainData = terrain.terrainData;
                Vector3 size = terrainData.size;
                bool found = false;
                const int tries = 6;
                for (int t = 0; t < tries; t++)
                {
                    entranceCandidateTries++;
                    float k = t / (float)(tries - 1); // 0..1
                    float inland = Mathf.Lerp(spawnEntranceInlandOffset, 0f, k);
                    Vector3 candidate = basePos + toCenter * inland;

                    // Must be inside the terrain rectangle; if not, skip this candidate.
                    Vector3 local = candidate - terrain.transform.position;
                    if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z)
                        continue;

                    // Must be on/above the intended beach band (keeps it on island, not out in the water plane).
                    float nx = Mathf.Clamp01(local.x / size.x);
                    float nz = Mathf.Clamp01(local.z / size.z);
                    float hNorm = SampleHeightCached(nx, nz);
                    if (hNorm < beachMinH)
                        continue;

                    // Use terrain Y here; actual prefab will be snapped with bounds later.
                    candidate.y = GroundYTerrainOnly(candidate);
                    entrancePos = candidate;
                    found = true;
                    break;
                }

                if (!found)
                {
                    // fallback: base spawn position is already edge-valid; just use terrain height.
                    entrancePos = basePos;
                    entrancePos.y = GroundYTerrainOnly(entrancePos);
                }
            }
            else
            {
                entrancePos = basePos + toCenter * spawnEntranceInlandOffset;
                entrancePos.y = GroundYTerrainOnly(entrancePos);
            }

            // Face toward dry area (land): direction from entrance to island center = inland
            Vector3 lookDir = terrainCenter - entrancePos;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude < 0.01f)
                lookDir = toCenter;
            // always align entrance to terrain slope; cameras are kept upright separately
            Vector3 up = ApproxTerrainNormal(entrancePos);
            Vector3 forward = Vector3.ProjectOnPlane(lookDir, up);
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.ProjectOnPlane(-toCenter, up);
            }
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.forward;
            }
            Quaternion entranceRot = Quaternion.LookRotation(forward.normalized, up) * Quaternion.Euler(0f, 180f, 0f);

            // if a SpawnEntrance already exists under SpawnArea (e.g. from a previous generation
            // pass or a pre-placed prefab), reuse and reposition it instead of spawning another
            // one. this guarantees we only ever have a single entrance instance.
            Transform entrance = null;
            if (spawnAreaRoot != null)
            {
                var existing = spawnAreaRoot.Find("SpawnEntrance");
                if (existing != null)
                {
                    entrance = existing;
                    entranceReusedExisting = true;
                }
            }

            if (entrance == null)
            {
                entrance = SpawnPrefab(spawnEntrancePhotonPrefab, spawnEntranceLocalPrefab, entrancePos, entranceRot, true);
                if (entrance != null)
                {
                    entrance.name = "SpawnEntrance";
                    entrance.SetParent(spawnAreaRoot, true);
                    spawned.Add(entrance);
                }
            }

            if (entrance != null)
            {
                entrancePlaced = 1;

                // Ensure the SpawnEntrance sits on land. This accounts for prefabs whose pivot is not at the bottom.
                // Then apply optional vertical offset. We do NOT force it above the water plane here so it can sit
                // flush with the beach based solely on the terrain height.
                SnapToGround(entrance, GroundSnapMode.TerrainOnly);
                if (Mathf.Abs(spawnEntranceVerticalOffset) > 0.0001f)
                {
                    Vector3 p = entrance.position;
                    p.y += spawnEntranceVerticalOffset;
                    entrance.position = p;
                }

                // Hard clamp the entrance XZ so it always stays within the *island* footprint (the falloff island),
                // not the full terrain square. This uses the cached island center/radius derived from the final
                // heightmap + sand band, so it continues to work even when TerrainGenerator.islandSize is very small.
                if (terrain != null && terrain.terrainData != null)
                {
                    Vector3 size = terrain.terrainData.size;
                    Vector3 local3 = entrance.position - terrain.transform.position;
                    Vector2 pXZ = new Vector2(local3.x, local3.z);

                    // Prefer the true island footprint if we have it; otherwise fall back to the old terrain-center ring.
                    Vector2 centerXZ;
                    float minRadius;
                    float maxRadius;

                    if (hasIslandFootprint)
                    {
                        centerXZ = islandCenterLocalXZ;
                        maxRadius = islandRadiusLocal * 0.98f;                               // just inside the island edge
                        minRadius = Mathf.Max(5f, islandRadiusLocal * 0.70f);               // keep it near the shoreline band
                    }
                    else
                    {
                        centerXZ = new Vector2(size.x * 0.5f, size.z * 0.5f);
                        float terrainRadius = Mathf.Min(size.x, size.z) * 0.5f;
                        maxRadius = terrainRadius * 0.95f;
                        minRadius = terrainRadius * 0.35f;
                    }

                    Vector2 fromCenter = pXZ - centerXZ;
                    float r = fromCenter.magnitude;
                    if (r < 0.0001f)
                    {
                        fromCenter = Vector2.right;
                        r = 0.001f;
                    }
                    fromCenter /= r;

                    float clampedR = Mathf.Clamp(r, minRadius, maxRadius);
                    if (!Mathf.Approximately(clampedR, r))
                    {
                        Vector2 clampedXZ = centerXZ + fromCenter * clampedR;
                        Vector3 newWorld = new Vector3(clampedXZ.x, entrance.position.y, clampedXZ.y) + terrain.transform.position;
                        newWorld.y = GroundYTerrainOnly(newWorld);
                        entrance.position = newWorld;
                        SnapToGround(entrance, GroundSnapMode.TerrainOnly);
                        if (Mathf.Abs(spawnEntranceVerticalOffset) > 0.0001f)
                        {
                            Vector3 adj = entrance.position;
                            adj.y += spawnEntranceVerticalOffset;
                            entrance.position = adj;
                        }
                    }
                }

                // Recompute tilt after snapping, so the entrance matches the final terrain slope.
                // Keep it facing inland (toward the island center), but use the terrain normal as "up".
                {
                    Vector3 finalPos = entrance.position;
                    Vector3 inland = terrainCenter - finalPos;
                    inland.y = 0f;
                    if (inland.sqrMagnitude < 0.01f)
                        inland = toCenter;

                    Vector3 finalUp = ApproxTerrainNormal(finalPos);
                    Vector3 finalForward = Vector3.ProjectOnPlane(inland, finalUp);
                    if (finalForward.sqrMagnitude < 0.01f)
                        finalForward = Vector3.ProjectOnPlane(-toCenter, finalUp);
                    if (finalForward.sqrMagnitude < 0.01f)
                        finalForward = Vector3.forward;

                    entrance.rotation = Quaternion.LookRotation(finalForward.normalized, finalUp) * Quaternion.Euler(0f, 180f, 0f);
                }

                ApplyCutsceneCameraUprightOverride(entrance);
                TrySyncCutsceneCameraUprightOverrideForNetwork(entrance);

                // If the prefab has child markers (SpawnMarker_*), use those as authoritative spawn points.
                if (TryCollectSpawnMarkersFromEntrance(entrance, maxPlayers, out List<Vector3> childSpawns) && childSpawns.Count > 0)
                {
                    finalSpawns = childSpawns;
                    usingEntranceChildMarkers = true;
                }
            }
        }

        // previously, when no child SpawnMarker_* transforms were present under the entrance prefab,
        // standalone SpawnMarker_* GameObjects were spawned into the scene as visual helpers.
        // these are no longer needed — we keep using finalSpawns for spawn positions, but avoid
        // generating extra marker objects so only the configured SpawnArea prefab is used.

        // Store final spawn positions for non-master clients
        sharedSpawnPositions.Clear();
        sharedSpawnPositions.AddRange(finalSpawns);

        // Sync spawn positions to non-master clients via RPC
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && photonView != null)
        {
            float[] positions = new float[finalSpawns.Count * 3];
            for (int i = 0; i < finalSpawns.Count; i++)
            {
                positions[i * 3] = finalSpawns[i].x;
                positions[i * 3 + 1] = finalSpawns[i].y;
                positions[i * 3 + 2] = finalSpawns[i].z;
            }
            photonView.RPC("RPC_SyncSpawnPositions", RpcTarget.OthersBuffered, finalSpawns.Count, positions);
        }

        if (logSummary)
        {
            Debug.Log($"MapResourcesGenerator: placed {spawned.Count} objects (remnants+guards+herbs+plants+quest+spawns)");
        }

        lastSpawnPlacementMetrics = new SpawnPlacementMetrics
        {
            spawnSlotsDesired = maxPlayers,
            spawnSlotsCandidateCount = spawnCandidateCount,
            spawnSlotsFinalCount = finalSpawns != null ? finalSpawns.Count : 0,
            usingEntranceChildMarkers = usingEntranceChildMarkers,
            entranceDesired = entranceDesired,
            entrancePlaced = entrancePlaced,
            entranceReusedExisting = entranceReusedExisting,
            entranceCandidateTries = entranceCandidateTries
        };

        FinalizeAfterGenerationPass();
    }

    private IEnumerator ApplyGenerateLocalDecorOnlyCoroutine()
    {
        ResolveActiveTerrainAndGenerator();

        if (terrain == null || terrain.terrainData == null || terrainGenerator == null)
        {
            Debug.LogWarning("MapResourcesGenerator: missing Terrain/TerrainGenerator for local decor generation");
            yield break;
        }

        CleanupLocalDecorOnly();

        if (occupied != null)
            occupied.Clear();
        occupied = new List<Vector3>(4096);

        var td = terrain.terrainData;
        int aw = td.alphamapWidth;
        int ah = td.alphamapHeight;
        cachedAlphamap = td.GetAlphamaps(0, 0, aw, ah);
        cachedHeightNorm = new float[ah, aw];
        cachedSlope = new float[ah, aw];
        float sizeY = td.size.y;
        for (int ay = 0; ay < ah; ay++)
        {
            for (int ax = 0; ax < aw; ax++)
            {
                float nx = aw > 1 ? ax / (float)(aw - 1) : 0f;
                float nz = ah > 1 ? ay / (float)(ah - 1) : 0f;
                cachedHeightNorm[ay, ax] = td.GetInterpolatedHeight(nx, nz) / sizeY;
                cachedSlope[ay, ax] = td.GetSteepness(nx, nz);
            }
            if (ay > 0 && (ay % 32) == 0) yield return null;
        }

        spatialGrid = new Dictionary<int, List<Vector3>>();
        _itemsPlacedThisFrame = 0;
        _attemptsThisFrame = 0;
        EnsureDecorCullingManager();

        yield return ScatterDeterministicDecor();
        yield return ScatterPlants();
        FinalizeLocalDecorPass();
    }

    private IEnumerator ScatterDeterministicDecor()
    {
        int seedToUse = hasDecorSeed ? decorSeed : ComputeDecorSeed(terrainGenerator != null ? terrainGenerator.seed : 1, terrainGenerator != null ? terrainGenerator.LastGenerationId : 1);
        var previousRandomState = Random.state;
        Random.InitState(seedToUse);

        yield return ScatterRocks();
        yield return ScatterTrees();
        yield return ScatterFerns();

        Random.state = previousRandomState;
    }

    private void FinalizeAfterGenerationPass()
    {
        // Generation routine completed; clear handle.
        generateRoutine = null;

        // Mark completion for test runners (pairs with TerrainGenerator.LastGenerationId).
        if (terrainGenerator != null && terrainGenerator.LastGenerationId > 0)
            LastCompletedTerrainGenerationId = terrainGenerator.LastGenerationId;

        // Release caches that are only needed during generation.
        spatialGrid = null;
        cachedAlphamap = null;
        cachedHeightNorm = null;
        cachedSlope = null;

        if (disableComponentAfterGeneration)
        {
            enabled = false;

            if (logSummary)
                Debug.Log("[MapResourcesGenerator] Generation finished; component disabled for performance.");
        }
    }

    private void FinalizeLocalDecorPass()
    {
        spatialGrid = null;
        cachedAlphamap = null;
        cachedHeightNorm = null;
        cachedSlope = null;

        if (terrainGenerator != null && terrainGenerator.LastGenerationId > 0)
            LastCompletedTerrainGenerationId = terrainGenerator.LastGenerationId;
    }

    /// <summary>Sample normalized height from cache (or terrain if cache missing).</summary>
    private float SampleHeightCached(float nx, float nz)
    {
        if (cachedHeightNorm != null && terrain != null && terrain.terrainData != null)
        {
            int aw = cachedHeightNorm.GetLength(1);
            int ah = cachedHeightNorm.GetLength(0);
            int ax = Mathf.Clamp(Mathf.RoundToInt(nx * (aw - 1)), 0, aw - 1);
            int ay = Mathf.Clamp(Mathf.RoundToInt(nz * (ah - 1)), 0, ah - 1);
            return cachedHeightNorm[ay, ax];
        }
        var td = terrain != null ? terrain.terrainData : null;
        if (td != null) return td.GetInterpolatedHeight(nx, nz) / td.size.y;
        return 0f;
    }

    private void ComputeIslandFootprintFromCache()
    {
        hasIslandFootprint = false;
        islandCenterLocalXZ = Vector2.zero;
        islandRadiusLocal = 0f;

        if (cachedHeightNorm == null || terrain == null || terrain.terrainData == null || terrainGenerator == null)
            return;

        var td = terrain.terrainData;
        Vector3 size = td.size;
        int aw = cachedHeightNorm.GetLength(1);
        int ah = cachedHeightNorm.GetLength(0);

        float sand = Mathf.Max(0.001f, terrainGenerator.sandThreshold);
        float landMin = sand + 0.015f; // small buffer above sand/water

        // sample coarsely for speed; still stable for centroid/radius
        int stepX = Mathf.Max(1, aw / 128);
        int stepY = Mathf.Max(1, ah / 128);

        double sumX = 0.0, sumZ = 0.0;
        int count = 0;
        for (int ay = 0; ay < ah; ay += stepY)
        {
            for (int ax = 0; ax < aw; ax += stepX)
            {
                float h = cachedHeightNorm[ay, ax];
                if (h < landMin) continue;
                float nx = aw > 1 ? ax / (float)(aw - 1) : 0f;
                float nz = ah > 1 ? ay / (float)(ah - 1) : 0f;
                sumX += nx * size.x;
                sumZ += nz * size.z;
                count++;
            }
        }

        if (count < 32)
            return;

        islandCenterLocalXZ = new Vector2((float)(sumX / count), (float)(sumZ / count));

        // compute radius as the farthest land sample from centroid (clamped so we never hit the terrain border)
        float maxR = 0f;
        for (int ay = 0; ay < ah; ay += stepY)
        {
            for (int ax = 0; ax < aw; ax += stepX)
            {
                float h = cachedHeightNorm[ay, ax];
                if (h < landMin) continue;
                float nx = aw > 1 ? ax / (float)(aw - 1) : 0f;
                float nz = ah > 1 ? ay / (float)(ah - 1) : 0f;
                float lx = nx * size.x;
                float lz = nz * size.z;
                float d = Vector2.Distance(new Vector2(lx, lz), islandCenterLocalXZ);
                if (d > maxR) maxR = d;
            }
        }

        float clampMax = Mathf.Min(size.x, size.z) * 0.49f;
        islandRadiusLocal = Mathf.Clamp(maxR, 5f, clampMax);
        hasIslandFootprint = islandRadiusLocal > 5f;
    }

    private Vector3 FindIslandShorePosition(float spacing, float slopeLimit, float minHeightOverride = -1f)
    {
        var list = FindMultipleIslandShorePositions(1, spacing, slopeLimit, minHeightOverride);
        return list.Count > 0 ? list[0] : (terrain != null ? terrain.transform.position : Vector3.zero);
    }

    private Vector3 FindIslandShorePositionFarFromPoints(List<Vector3> avoidPoints, float spacing, float slopeLimit, float minDistance, float minHeightOverride = -1f)
    {
        if (avoidPoints == null || avoidPoints.Count == 0)
            return FindIslandShorePosition(spacing, slopeLimit, minHeightOverride);

        var candidates = FindMultipleIslandShorePositions(96, spacing, slopeLimit, minHeightOverride);
        if (candidates == null || candidates.Count == 0)
            return FindIslandShorePosition(spacing, slopeLimit, minHeightOverride);

        float minDistSqr = minDistance * minDistance;
        Vector3 best = candidates[0];
        float bestNearest = -1f;

        for (int i = 0; i < candidates.Count; i++)
        {
            float nearest = float.MaxValue;
            for (int j = 0; j < avoidPoints.Count; j++)
            {
                float d = (avoidPoints[j] - candidates[i]).sqrMagnitude;
                if (d < nearest) nearest = d;
            }

            if (nearest >= minDistSqr)
                return candidates[i];

            if (nearest > bestNearest)
            {
                bestNearest = nearest;
                best = candidates[i];
            }
        }

        return best;
    }

    private Vector3 SnapTowardIslandShore(Vector3 world, float slopeLimit, float minH, float maxH)
    {
        if (!hasIslandFootprint || terrain == null || terrain.terrainData == null)
            return world;

        var td = terrain.terrainData;
        Vector3 size = td.size;
        Vector3 local3 = world - terrain.transform.position;
        Vector2 local = new Vector2(local3.x, local3.z);
        Vector2 dir = local - islandCenterLocalXZ;
        if (dir.sqrMagnitude < 0.001f) dir = Vector2.right;
        dir.Normalize();

        // walk inward from the island radius until we hit the beach band
        for (int i = 0; i < 14; i++)
        {
            float t = i / 13f;
            float r = Mathf.Lerp(islandRadiusLocal * 0.98f, islandRadiusLocal * 0.70f, t);
            Vector2 p = islandCenterLocalXZ + dir * r;
            if (p.x < 0f || p.y < 0f || p.x > size.x || p.y > size.z)
                continue;

            float nx = Mathf.Clamp01(p.x / size.x);
            float nz = Mathf.Clamp01(p.y / size.z);
            float hNorm = SampleHeightCached(nx, nz);
            if (hNorm < minH || hNorm > maxH)
                continue;
            float slope = SampleSlopeCached(nx, nz);
            if (slope > slopeLimit)
                continue;

            Vector3 w = new Vector3(p.x, 0f, p.y) + terrain.transform.position;
            w.y = GroundYTerrainOnly(w);
            return w;
        }

        world.y = GroundYTerrainOnly(world);
        return world;
    }

    private List<Vector3> FindMultipleIslandShorePositions(int count, float spacing, float slopeLimit, float minHeightOverride = -1f)
    {
        var positions = new List<Vector3>(count);
        if (terrain == null || terrain.terrainData == null || terrainGenerator == null)
            return positions;

        var td = terrain.terrainData;
        Vector3 size = td.size;

        float sand = Mathf.Max(0.001f, terrainGenerator.sandThreshold);
        float beach = terrainGenerator != null ? terrainGenerator.beachWidth : 0.08f;
        float minH = minHeightOverride >= 0f ? Mathf.Max(sand, Mathf.Clamp01(minHeightOverride)) : sand + Mathf.Max(0.005f, edgeBandAboveSand);
        float maxH = Mathf.Max(minH + 0.01f, sand + beach * 1.05f);

        // If we haven't computed the island footprint, fall back to the old edge logic.
        if (!hasIslandFootprint)
            return FindMultipleEdgePositions(count, spacing, slopeLimit, minH);

        float minSpacingSqr = spacing * spacing;
        int attempts = 0;
        int maxAttempts = count * 2000;

        float ringOuter = islandRadiusLocal * 0.98f;
        float ringInner = Mathf.Max(5f, islandRadiusLocal * 0.70f);
        float outwardProbe = Mathf.Max(6f, edgeOuterNeighborCheckRadius * 0.35f);
        float waterBand = sand + Mathf.Max(0.005f, edgeBandAboveSand);

        while (positions.Count < count && attempts < maxAttempts)
        {
            attempts++;
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(ringInner, ringOuter);
            Vector2 localXZ = islandCenterLocalXZ + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;

            if (localXZ.x < 0f || localXZ.y < 0f || localXZ.x > size.x || localXZ.y > size.z)
                continue;

            float nx = Mathf.Clamp01(localXZ.x / size.x);
            float nz = Mathf.Clamp01(localXZ.y / size.z);
            float hNorm = SampleHeightCached(nx, nz);
            if (hNorm < minH || hNorm > maxH) continue;

            float slope = SampleSlopeCached(nx, nz);
            if (slope > slopeLimit) continue;

            // Require water outside the island in the radial outward direction.
            Vector2 dir = (localXZ - islandCenterLocalXZ);
            if (dir.sqrMagnitude < 0.001f) dir = Vector2.right;
            dir.Normalize();
            Vector2 probeXZ = localXZ + dir * outwardProbe;
            if (probeXZ.x >= 0f && probeXZ.y >= 0f && probeXZ.x <= size.x && probeXZ.y <= size.z)
            {
                float pnx = Mathf.Clamp01(probeXZ.x / size.x);
                float pnz = Mathf.Clamp01(probeXZ.y / size.z);
                float probeH = SampleHeightCached(pnx, pnz);
                if (probeH > waterBand)
                    continue;
            }

            Vector3 world = new Vector3(localXZ.x, 0f, localXZ.y) + terrain.transform.position;
            world.y = GroundYTerrainOnly(world);
            world = ClampAboveWater(world, spawnAboveWaterOffset);
            if (IsBelowWater(world)) continue;

            bool tooClose = false;
            for (int i = 0; i < positions.Count; i++)
            {
                if ((positions[i] - world).sqrMagnitude < minSpacingSqr) { tooClose = true; break; }
            }
            if (tooClose) continue;

            positions.Add(world);
        }

        if (positions.Count > 0)
            return positions;

        return FindMultipleEdgePositions(count, spacing, slopeLimit, minH);
    }

    /// <summary>Sample slope in degrees from cache (or terrain if cache missing).</summary>
    private float SampleSlopeCached(float nx, float nz)
    {
        if (cachedSlope != null && terrain != null && terrain.terrainData != null)
        {
            int aw = cachedSlope.GetLength(1);
            int ah = cachedSlope.GetLength(0);
            int ax = Mathf.Clamp(Mathf.RoundToInt(nx * (aw - 1)), 0, aw - 1);
            int ay = Mathf.Clamp(Mathf.RoundToInt(nz * (ah - 1)), 0, ah - 1);
            return cachedSlope[ay, ax];
        }
        var td = terrain != null ? terrain.terrainData : null;
        if (td != null) return td.GetSteepness(nx, nz);
        return 0f;
    }

    /// <summary>Sample cached alphamap. Unity order is [y, x, layer]. Returns (sandW, grassW).</summary>
    private bool SampleAlphamapCached(int ax, int ay, out float sandW, out float grassW)
    {
        sandW = 0f; grassW = 0f;
        if (cachedAlphamap == null) return false;
        int ah = cachedAlphamap.GetLength(0); // y
        int aw = cachedAlphamap.GetLength(1); // x
        int layers = cachedAlphamap.GetLength(2);
        ax = Mathf.Clamp(ax, 0, aw - 1);
        ay = Mathf.Clamp(ay, 0, ah - 1);
        sandW = layers > 0 ? cachedAlphamap[ay, ax, 0] : 0f;
        grassW = 0f;
        for (int i = 1; i < layers && i < 4; i++) grassW += cachedAlphamap[ay, ax, i];
        return true;
    }

    private int GetSpatialCellKey(Vector3 pos)
    {
        int cx = Mathf.FloorToInt(pos.x / spatialCellSize);
        int cz = Mathf.FloorToInt(pos.z / spatialCellSize);
        return cx * 3191 + cz; // simple hash
    }

    private void SpatialAdd(Vector3 pos)
    {
        if (spatialGrid == null) return;
        int k = GetSpatialCellKey(pos);
        if (!spatialGrid.TryGetValue(k, out var list))
        {
            list = new List<Vector3>(8);
            spatialGrid[k] = list;
        }
        list.Add(pos);
    }

    private bool SpatialTooClose(Vector3 pos, float minDistSqr)
    {
        if (spatialGrid == null) return false;
        int cx = Mathf.FloorToInt(pos.x / spatialCellSize);
        int cz = Mathf.FloorToInt(pos.z / spatialCellSize);
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int k = (cx + dx) * 3191 + (cz + dz);
                if (!spatialGrid.TryGetValue(k, out var list)) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    if ((list[i] - pos).sqrMagnitude < minDistSqr) return true;
                }
            }
        }
        return false;
    }

    private bool YieldIfNeeded()
    {
        if (itemsPerFrame <= 0) return false;
        _itemsPlacedThisFrame++;
        if (_itemsPlacedThisFrame >= itemsPerFrame)
        {
            _itemsPlacedThisFrame = 0;
            _attemptsThisFrame = 0;
            return true;
        }
        return false;
    }

    /// <summary>Yield if too many rejected attempts accumulated this frame (prevents lag spikes in scatter loops).</summary>
    private bool YieldIfTooManyAttempts()
    {
        if (maxAttemptsPerFrame <= 0) return false;
        _attemptsThisFrame++;
        if (_attemptsThisFrame >= maxAttemptsPerFrame)
        {
            _attemptsThisFrame = 0;
            _itemsPlacedThisFrame = 0;
            return true;
        }
        return false;
    }

    private int GetDecorDesiredCount(int baseCount)
    {
        float scale = Mathf.Clamp(globalDecorDensityScale, 0.05f, 1f);
        if (reduceDecorDensityInMultiplayer && PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
            scale *= Mathf.Clamp(multiplayerDecorDensityScale, 0.05f, 1f);
        return Mathf.Max(0, Mathf.RoundToInt(baseCount * scale));
    }

    private int ApplyMultiplayerDecorCap(int count, int multiplayerCap)
    {
        if (!capDecorCountInMultiplayer) return count;
        if (!(PhotonNetwork.IsConnected && PhotonNetwork.InRoom)) return count;
        if (multiplayerCap <= 0) return 0;
        return Mathf.Min(count, multiplayerCap);
    }

    private void ReserveOccupied(Vector3 world)
    {
        occupied?.Add(world);
        SpatialAdd(world);
    }

    private bool IsBlockedBySceneObjects(Vector3 world, float clearanceRadius)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(world + Vector3.up * 0.6f, clearanceRadius, _spawnBlockerBuffer, groundRaycastMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            var col = _spawnBlockerBuffer[i];
            if (col == null) continue;
            if (col is TerrainCollider) continue;
            return true;
        }
        return false;
    }

    private bool TryResolveClearPlacement(Vector3 desiredWorld, float clearanceRadius, out Vector3 resolvedWorld)
    {
        resolvedWorld = desiredWorld;
        resolvedWorld.y = GroundYTerrainOnly(resolvedWorld);
        float minDistSqr = clearanceRadius * clearanceRadius;

        bool IsClear(Vector3 p)
        {
            if (SpatialTooClose(p, minDistSqr)) return false;
            if (IsBlockedBySceneObjects(p, clearanceRadius)) return false;
            return true;
        }

        if (IsClear(resolvedWorld)) return true;

        float step = Mathf.Max(1.25f, clearanceRadius * 1.05f);
        for (int ring = 1; ring <= 6; ring++)
        {
            int samples = 8 + ring * 4;
            float radius = ring * step;
            for (int i = 0; i < samples; i++)
            {
                float a = (Mathf.PI * 2f * i) / samples;
                Vector3 c = desiredWorld + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                c.y = GroundYTerrainOnly(c);
                if (IsClear(c))
                {
                    resolvedWorld = c;
                    return true;
                }
            }
        }
        return false;
    }

    private void EnsureDecorCullingManager()
    {
        if (!enableRuntimeDecorCulling)
        {
            decorCullingManager = null;
            return;
        }

        if (decorCullingManager == null)
        {
            // look under GeneratedEnvironment first, then scene-wide for legacy objects
            var generatedRoot = GameObject.Find(GeneratedRootName);
            if (generatedRoot == null)
                generatedRoot = new GameObject(GeneratedRootName);

            Transform existingChild = generatedRoot.transform.Find(DecorCullingManagerName);
            GameObject existing = existingChild != null ? existingChild.gameObject : GameObject.Find(DecorCullingManagerName);
            if (existing != null)
                decorCullingManager = existing.GetComponent<GeneratedDecorCullingManager>();

            if (decorCullingManager == null)
            {
                var go = existing != null ? existing : new GameObject(DecorCullingManagerName);
                decorCullingManager = go.GetComponent<GeneratedDecorCullingManager>();
                if (decorCullingManager == null)
                    decorCullingManager = go.AddComponent<GeneratedDecorCullingManager>();
            }

            // ensure it lives under GeneratedEnvironment
            if (decorCullingManager.transform.parent != generatedRoot.transform)
                decorCullingManager.transform.SetParent(generatedRoot.transform, false);
        }

        if (decorCullingManager != null)
        {
            decorCullingManager.Configure(
                skipLodGroupObjectsInRuntimeCulling,
                decorHideDistance,
                decorShowHysteresis,
                decorColliderDisableDistance,
                decorCullingUpdateInterval,
                decorMaxTogglesPerTick,
                decorCullingAlsoDisablesColliders,
                showDecorCullingDebugOverlay);
            decorCullingManager.ClearAll();
        }
    }

    private void RegisterDecorForCulling(Transform decorTransform)
    {
        if (!enableRuntimeDecorCulling || decorTransform == null) return;
        if (decorCullingManager == null) return;
        decorCullingManager.Register(decorTransform);
    }

    [PunRPC]
    private void RPC_SyncSpawnPositions(int count, float[] positions)
    {
        if (count <= 0 || positions == null || positions.Length < count * 3)
            return;

        if (HasEquivalentSpawnMarkers(count, positions))
        {
            sharedSpawnPositions.Clear();
            for (int i = 0; i < count; i++)
                sharedSpawnPositions.Add(new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]));

            if (logSummary) Debug.Log($"[MapResourcesGenerator] Spawn markers already in sync ({count}), skipping recreate");
            return;
        }

        RemoveExistingSpawnMarkers();

        sharedSpawnPositions.Clear();
        var markerRoot = GetSceneRoot("SpawnMarkers");
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
            sharedSpawnPositions.Add(pos);

            // create spawn marker locally for non-master clients
            var marker = CreateFallbackSpawnMarker(pos, i + 1);
            if (marker != null)
                marker.SetParent(markerRoot, true);
        }

        if (logSummary) Debug.Log($"[MapResourcesGenerator] Received {count} spawn positions from master client");
    }

    [PunRPC]
    private void RPC_RequestNetworkDestroy(int viewId)
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient)
            return;

        if (viewId <= 0)
            return;

        PhotonView targetPv = PhotonView.Find(viewId);
        if (targetPv == null || targetPv.gameObject == null)
            return;

        PhotonNetwork.Destroy(targetPv.gameObject);
    }

    [PunRPC]
    private void RPC_SetGeneratedParent(int childViewId, string generatedRootName)
    {
        if (childViewId <= 0 || string.IsNullOrWhiteSpace(generatedRootName))
            return;

        PhotonView childView = PhotonView.Find(childViewId);
        if (childView == null || childView.transform == null)
            return;

        Transform parentRoot = GetSceneRoot(generatedRootName);
        if (parentRoot == null)
            return;

        childView.transform.SetParent(parentRoot, true);
    }

    [PunRPC]
    private void RPC_ApplyCutsceneCameraUprightOverride(int entranceViewId)
    {
        if (entranceViewId <= 0)
            return;

        PhotonView entranceView = PhotonView.Find(entranceViewId);
        if (entranceView == null || entranceView.transform == null)
            return;

        ApplyCutsceneCameraUprightOverride(entranceView.transform);
    }

    public static List<Vector3> GetSharedSpawnPositions()
    {
        return sharedSpawnPositions;
    }

    void OnDestroy()
    {
        TerrainGenerator.OnGenerationCompleted -= HandleTerrainGenerationCompleted;

        // Stop all coroutines
        if (generateRoutine != null)
        {
            StopCoroutine(generateRoutine);
            generateRoutine = null;
        }
        // Cleanup spawned objects
        CleanupAll();
    }

    /// <summary>
    /// Cleanup all spawned objects and clear collections
    /// </summary>
    public void CleanupAll()
    {
        CleanupOldObjects();

        if (spawned != null)
            spawned.Clear();
        if (occupied != null)
            occupied.Clear();

        // Clear static shared positions
        if (sharedSpawnPositions != null)
            sharedSpawnPositions.Clear();
    }

    public override void OnDisable()
    {
        base.OnDisable();
        TerrainGenerator.OnGenerationCompleted -= HandleTerrainGenerationCompleted;
        generationTriggeredByEvent = false;

        if (generateRoutine != null)
        {
            StopCoroutine(generateRoutine);
            generateRoutine = null;
        }
        spawned.Clear();
        if (occupied != null)
            occupied.Clear();

        // release generation caches when disabled
        spatialGrid = null;
        cachedAlphamap = null;
        cachedHeightNorm = null;
        cachedSlope = null;
    }

    private IEnumerator ScatterHerbs()
    {
        if (herbResources == null || herbResources.Length == 0)
            yield break;

        // Reset only herb/understory-related metrics here.
        // Ferns/trees/rocks and camp placement are generated earlier by ScatterDeterministicDecor()
        // and SpawnGeneratedEnemyCamps(), so resetting them would wipe the runtime counters the
        // testing window/reporting exports.
        lastHerbScatterMetrics = null;
        lastPlantScatterMetrics = default;

        var td = terrain.terrainData; Vector3 size = td.size;
        float sand = Mathf.Max(0.001f, terrainGenerator.sandThreshold);
        var plantsRoot = GetSceneRoot("Plants");
        // Put all herb spawns under a dedicated root so they don't mix with other plants.
        Transform herbsRoot = plantsRoot.Find("Herbs");
        if (herbsRoot == null)
        {
            var go = new GameObject("Herbs");
            go.transform.SetParent(plantsRoot, false);
            herbsRoot = go.transform;
        }

        // Clear only herbs (don't wipe other plant categories).
        for (int i = herbsRoot.childCount - 1; i >= 0; i--)
        {
            var go = herbsRoot.GetChild(i).gameObject;
            SafeDestroyWithPhotonView(go);
        }

        foreach (var cfg in herbResources)
        {
            if (cfg == null || string.IsNullOrEmpty(cfg.photonPrefab) && cfg.localPrefab == null)
                continue;
            if (cfg.desiredCount <= 0)
                continue;

            float minDistSqr = cfg.minSpacing * cfg.minSpacing;
            int placed = 0;
            int attempts = 0;
            int maxAttempts = cfg.desiredCount * 40;
            int perlinRejected = 0;
            int heightRejected = 0;
            int slopeRejected = 0;
            int biomeRejected = 0;
            int waterRejected = 0;
            int spacingRejected = 0;
            int validCandidates = 0;
            while (placed < cfg.desiredCount && attempts < maxAttempts)
            {
                attempts++;
                if (YieldIfTooManyAttempts()) yield return null;
                float nx = Random.value; float nz = Random.value;
                float vx = (nx * size.x) * cfg.perlinScale + perlinOffset.x;
                float vz = (nz * size.z) * cfg.perlinScale + perlinOffset.y;
                if (Mathf.PerlinNoise(vx, vz) < cfg.perlinThreshold) { perlinRejected++; continue; }
                float hNorm = SampleHeightCached(nx, nz);
                if (hNorm <= sand + 0.01f) { heightRejected++; continue; }
                float slope = SampleSlopeCached(nx, nz);
                if (slope > maxSlope) { slopeRejected++; continue; }
                Vector3 world = new Vector3(nx * size.x, 0f, nz * size.z) + terrain.transform.position;
                world.y = GroundYTerrainOnly(world);
                if (waterTransform != null && world.y < WaterSurfaceY() + herbAboveWaterOffset) { waterRejected++; continue; }
                validCandidates++;
                if (SpatialTooClose(world, minDistSqr)) { spacingRejected++; continue; }
                SpatialAdd(world);
                var herb = SpawnPrefab(cfg.photonPrefab, cfg.localPrefab, world, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), true);
                if (herb != null)
                {
                    herb.SetParent(herbsRoot, true);
                    spawned.Add(herb);
                    occupied.Add(world);
                    placed++;
                    if (YieldIfNeeded()) yield return null;
                }
            }

            // store per-config metrics for the testing window/reporting
            var list = new List<ResourceScatterMetrics>(lastHerbScatterMetrics != null ? lastHerbScatterMetrics.Length + 1 : 4);
            if (lastHerbScatterMetrics != null && lastHerbScatterMetrics.Length > 0)
                list.AddRange(lastHerbScatterMetrics);
            list.Add(new ResourceScatterMetrics
            {
                label = string.IsNullOrEmpty(cfg.photonPrefab) ? (cfg.localPrefab != null ? cfg.localPrefab.name : "Herb") : cfg.photonPrefab,
                desiredCount = cfg.desiredCount,
                placed = placed,
                attempts = attempts,
                perlinScale = cfg.perlinScale,
                perlinThreshold = cfg.perlinThreshold,
                minSpacing = cfg.minSpacing,
                perlinRejected = perlinRejected,
                heightRejected = heightRejected,
                slopeRejected = slopeRejected,
                biomeRejected = biomeRejected,
                waterRejected = waterRejected,
                spacingRejected = spacingRejected,
                validCandidates = validCandidates
            });
            lastHerbScatterMetrics = list.ToArray();
        }
    }

    private IEnumerator SpawnGeneratedEnemyCamps()
    {
        if (!enableCampGeneration || campCount <= 0)
            yield break;

        bool hasLocal = campLocalPrefabs != null && campLocalPrefabs.Length > 0;
        bool hasPhoton = campPhotonPrefabs != null && campPhotonPrefabs.Length > 0;
        if (!hasLocal && !hasPhoton)
            yield break;

        var campsRoot = GetSceneRoot("Camps");
        int localLen = hasLocal ? campLocalPrefabs.Length : 0;
        int photonLen = hasPhoton ? campPhotonPrefabs.Length : 0;
        int maxLen = Mathf.Max(localLen, photonLen);
        if (maxLen <= 0)
            yield break;

        int targetCount = Mathf.Max(0, campCount);
        List<Vector3> candidates = FindInlandPositions(Mathf.Max(targetCount * 6, targetCount), campInlandAboveSand, campMinSpacing);
        int placed = 0;
        int attempts = 0;
        int clearanceRejected = 0;

        for (int i = 0; i < candidates.Count && placed < targetCount; i++)
        {
            attempts++;
            Vector3 pos = candidates[i];
            if (!TryResolveClearPlacement(pos, Mathf.Max(1f, campClearanceRadius), out pos))
            {
                clearanceRejected++;
                continue;
            }

            int idx = Random.Range(0, maxLen);
            GameObject localPrefab = (idx < localLen) ? campLocalPrefabs[idx] : null;
            string photonName = ResolveCampPhotonName(idx, localPrefab, photonLen);
            Quaternion rot = randomizeCampYaw ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) : Quaternion.identity;

            Transform camp = SpawnCampPrefab(photonName, localPrefab, pos, rot);
            if (camp == null)
                continue;

            camp.SetParent(campsRoot, true);
            TrySyncGeneratedParentForNetwork(camp, "Camps");
            spawned.Add(camp);
            ReserveOccupied(pos);
            placed++;

            if (YieldIfNeeded())
                yield return null;
        }

        // Relaxed pass: if strict spacing/clearance rejected too many spots, still try to fulfill count.
        int relaxedAttempts = 0;
        int relaxedMaxAttempts = Mathf.Max(20, targetCount * 16);
        while (placed < targetCount && relaxedAttempts++ < relaxedMaxAttempts)
        {
            attempts++;
            int idx = Random.Range(0, maxLen);
            GameObject localPrefab = (idx < localLen) ? campLocalPrefabs[idx] : null;
            string photonName = ResolveCampPhotonName(idx, localPrefab, photonLen);
            Vector3 pos = candidates.Count > 0 ? candidates[Random.Range(0, candidates.Count)] : terrain.transform.position;

            if (!TryResolveClearPlacement(pos, Mathf.Max(1f, campClearanceRadius * 0.6f), out pos))
            {
                pos.y = GroundYTerrainOnly(pos);
            }

            Quaternion rot = randomizeCampYaw ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) : Quaternion.identity;
            Transform camp = SpawnCampPrefab(photonName, localPrefab, pos, rot);
            if (camp == null)
                continue;

            camp.SetParent(campsRoot, true);
            TrySyncGeneratedParentForNetwork(camp, "Camps");
            spawned.Add(camp);
            ReserveOccupied(pos);
            placed++;

            if (YieldIfNeeded())
                yield return null;
        }

        lastCampPlacementMetrics = new ResourceScatterMetrics
        {
            // Keep label aligned with the testing/reporting UI categories.
            label = "Camps",
            desiredCount = targetCount,
            placed = placed,
            attempts = attempts,
            perlinScale = 0f,
            perlinThreshold = 0f,
            minSpacing = campMinSpacing,
            perlinRejected = 0,
            heightRejected = 0,
            slopeRejected = 0,
            biomeRejected = 0,
            waterRejected = 0,
            spacingRejected = clearanceRejected,
            validCandidates = Mathf.Max(0, attempts)
        };

        if (logSummary)
        {
            Debug.Log($"[MapResourcesGenerator] Camps placed: {placed}/{targetCount}");
            if (placed < targetCount)
                Debug.LogWarning("[MapResourcesGenerator] Some camps failed to spawn. Check campPhotonPrefabs paths (Resources/Enemies/Camps/...) and Photon prefab registration.");
        }
    }

    private string ResolveCampPhotonName(int index, GameObject localPrefab, int photonLen)
    {
        if (index < photonLen)
        {
            string named = campPhotonPrefabs[index];
            if (!string.IsNullOrWhiteSpace(named))
                return named;
        }

        if (localPrefab != null)
            return localPrefab.name;

        return null;
    }

    private Transform SpawnCampPrefab(string photonName, GameObject localPrefab, Vector3 position, Quaternion rotation)
    {
        bool shouldNetwork = PhotonNetwork.IsConnected && PhotonNetwork.InRoom;

        List<string> candidates = BuildCampPhotonCandidates(photonName, localPrefab);

        if (shouldNetwork)
        {
            if (!PhotonNetwork.IsMasterClient)
                return null;

            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                try
                {
                    GameObject go = PhotonNetwork.InstantiateRoomObject(candidate, position, rotation);
                    return go != null ? go.transform : null;
                }
                catch
                {
                    // Try next candidate path.
                }
            }

            if (logSummary)
                Debug.LogWarning($"[MapResourcesGenerator] Camp Photon instantiate failed. Candidates: {string.Join(", ", candidates)}");
            return null;
        }

        if (localPrefab != null)
        {
            GameObject go = Instantiate(localPrefab, position, rotation);
            return go != null ? go.transform : null;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            string candidate = candidates[i];
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            GameObject loaded = Resources.Load<GameObject>(candidate);
            if (loaded != null)
            {
                GameObject go = Instantiate(loaded, position, rotation);
                return go != null ? go.transform : null;
            }
        }

        return null;
    }

    private List<string> BuildCampPhotonCandidates(string configuredName, GameObject localPrefab)
    {
        var result = new List<string>(6);
        string baseName = !string.IsNullOrWhiteSpace(configuredName)
            ? configuredName.Trim()
            : (localPrefab != null ? localPrefab.name : null);

        if (string.IsNullOrWhiteSpace(baseName))
            return result;

        result.Add(baseName);
        if (!baseName.StartsWith("Enemies/Camps/"))
            result.Add($"Enemies/Camps/{baseName}");
        if (!baseName.StartsWith("Camps/"))
            result.Add($"Camps/{baseName}");

        if (baseName.StartsWith("Enemies/Camps/"))
            result.Add(baseName.Replace("Enemies/Camps/", ""));
        else if (baseName.StartsWith("Camps/"))
            result.Add(baseName.Replace("Camps/", ""));

        // de-dup while preserving order
        var deduped = new List<string>(result.Count);
        for (int i = 0; i < result.Count; i++)
        {
            string c = result[i];
            if (string.IsNullOrWhiteSpace(c))
                continue;
            if (!deduped.Contains(c))
                deduped.Add(c);
        }
        return deduped;
    }

    private void TrySyncGeneratedParentForNetwork(Transform child, string generatedRootName)
    {
        if (child == null || string.IsNullOrWhiteSpace(generatedRootName))
            return;
        if (!(PhotonNetwork.IsConnected && PhotonNetwork.InRoom))
            return;
        if (photonView == null)
            return;

        PhotonView childView = child.GetComponent<PhotonView>();
        if (childView == null || childView.ViewID <= 0)
            return;

        photonView.RPC(nameof(RPC_SetGeneratedParent), RpcTarget.Others, childView.ViewID, generatedRootName);
    }

    private void TrySyncCutsceneCameraUprightOverrideForNetwork(Transform entrance)
    {
        if (entrance == null)
            return;
        if (!(PhotonNetwork.IsConnected && PhotonNetwork.InRoom))
            return;
        if (photonView == null)
            return;

        PhotonView entranceView = entrance.GetComponent<PhotonView>();
        if (entranceView == null || entranceView.ViewID <= 0)
            return;

        photonView.RPC(nameof(RPC_ApplyCutsceneCameraUprightOverride), RpcTarget.Others, entranceView.ViewID);
    }

    private void ApplyCutsceneCameraUprightOverride(Transform entranceRoot)
    {
        if (entranceRoot == null)
            return;

        // keep every camera in the entrance prefab upright regardless of entrance tilt
        var cameras = entranceRoot.GetComponentsInChildren<Camera>(true);
        foreach (var cam in cameras)
        {
            Vector3 desiredForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (desiredForward.sqrMagnitude < 0.0001f)
                desiredForward = Vector3.ProjectOnPlane(entranceRoot.forward, Vector3.up);
            if (desiredForward.sqrMagnitude < 0.0001f)
                desiredForward = Vector3.forward;

            cam.transform.rotation = Quaternion.LookRotation(desiredForward.normalized, Vector3.up);
        }
    }

    private Transform FindChildRecursiveByName(Transform root, string targetName)
    {
        if (root == null || string.IsNullOrEmpty(targetName))
            return null;
        if (root.name == targetName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            Transform found = FindChildRecursiveByName(child, targetName);
            if (found != null)
                return found;
        }

        return null;
    }

    private bool SpawnGuardForRemnant(int remnantIndex, Vector3 remnantPosition, Transform parentRoot)
    {
        if (remnantIndex < 0)
            return false;

        string guardPhoton = (remnantGuardPhotonPrefabs != null && remnantIndex < remnantGuardPhotonPrefabs.Length)
            ? remnantGuardPhotonPrefabs[remnantIndex]
            : null;
        GameObject guardLocal = (remnantGuardLocalPrefabs != null && remnantIndex < remnantGuardLocalPrefabs.Length)
            ? remnantGuardLocalPrefabs[remnantIndex]
            : null;

        if (string.IsNullOrWhiteSpace(guardPhoton) && guardLocal == null)
            return false;

        Vector3 dir = Random.insideUnitSphere;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector3.forward;
        dir.Normalize();

        Vector3 guardPos = remnantPosition + dir * Mathf.Max(1f, remnantGuardOffset);
        if (!TryResolveClearPlacement(guardPos, Mathf.Max(0.5f, remnantGuardClearanceRadius), out guardPos))
        {
            guardPos = remnantPosition + dir * (Mathf.Max(1f, remnantGuardOffset) * 0.6f);
            guardPos.y = GroundYTerrainOnly(guardPos);
        }

        Vector3 lookDir = remnantPosition - guardPos;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude < 0.01f)
            lookDir = -dir;

        Transform guard = SpawnPrefab(guardPhoton, guardLocal, guardPos, Quaternion.LookRotation(lookDir.normalized), true);
        if (guard == null)
            return false;

        guard.name = $"GeneratedRemnantGuard_{remnantIndex + 1}";
        guard.SetParent(parentRoot, true);
        TrySyncGeneratedParentForNetwork(guard, "RemnantGuards");
        spawned.Add(guard);
        ReserveOccupied(guardPos);
        return true;
    }

    private void ClearChildren(Transform root)
    {
        if (root == null)
            return;

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child == null)
                continue;

            SafeDestroyWithPhotonView(child.gameObject);
        }
    }

    private IEnumerator ScatterPlants()
    {
        var previousRandomState = Random.state;
        int generationId = terrainGenerator != null ? terrainGenerator.LastGenerationId : 1;
        int baseSeed = hasDecorSeed ? decorSeed : ComputeDecorSeed(terrainGenerator != null ? terrainGenerator.seed : 1, generationId);
        unchecked
        {
            Random.InitState(baseSeed ^ 0x51F2A37);
        }

        bool hasLocal = plantLocalPrefabs != null && plantLocalPrefabs.Length > 0;
        bool hasPhoton = plantPhotonPrefabs != null && plantPhotonPrefabs.Length > 0;
        if (!hasLocal && !hasPhoton)
        {
            Random.state = previousRandomState;
            yield break;
        }
        if (plantDesiredCount <= 0)
        {
            Random.state = previousRandomState;
            yield break;
        }
        bool isNetworkedSession = PhotonNetwork.IsConnected && PhotonNetwork.InRoom;
        bool isAuthoritative = IsAuthoritativeGenerator();
        var td = terrain.terrainData; Vector3 size = td.size;
        int aw = td.alphamapWidth; int ah = td.alphamapHeight;
        float minDistSqr = plantMinSpacing * plantMinSpacing;
        int localLen = hasLocal ? plantLocalPrefabs.Length : 0;
        int photonLen = hasPhoton ? plantPhotonPrefabs.Length : 0;
        int maxLen = Mathf.Max(localLen, photonLen);
        if (maxLen <= 0)
        {
            Random.state = previousRandomState;
            yield break;
        }

        List<int> networkCapableIndices = null;
        if (isNetworkedSession)
        {
            networkCapableIndices = new List<int>(maxLen);
            for (int i = 0; i < maxLen; i++)
            {
                if (i < photonLen && !string.IsNullOrWhiteSpace(plantPhotonPrefabs[i]))
                    networkCapableIndices.Add(i);
            }

            if (networkCapableIndices.Count == 0)
            {
                Debug.LogWarning("[MapResourcesGenerator] ScatterPlants skipped in multiplayer: no plantPhotonPrefabs configured. Add Photon prefab names for interactable plants.");
                Random.state = previousRandomState;
                yield break;
            }
        }

        var plantsRoot = GetSceneRoot("Plants");
        // Keep Herbs (and other future sub-categories) intact by using a dedicated sub-root for understory plants.
        Transform understoryRoot = plantsRoot.Find("Understory");
        if (understoryRoot == null)
        {
            var go = new GameObject("Understory");
            go.transform.SetParent(plantsRoot, false);
            understoryRoot = go.transform;
        }

        for (int i = understoryRoot.childCount - 1; i >= 0; i--)
        {
            var go = understoryRoot.GetChild(i).gameObject;
            SafeDestroyWithPhotonView(go);
        }

        int placed = 0;
        int attempts = 0; int maxAttempts = plantDesiredCount * 40;
        int heightRejected = 0;
        int slopeRejected = 0;
        int biomeRejected = 0;
        int spacingRejected = 0;
        int validCandidates = 0;
        while (placed < plantDesiredCount && attempts < maxAttempts)
        {
            attempts++;
            if (YieldIfTooManyAttempts()) yield return null;
            float nx = Random.value; float nz = Random.value;
            float hNorm = SampleHeightCached(nx, nz);
            if (hNorm < minGrassHeight) { heightRejected++; continue; }
            float slope = SampleSlopeCached(nx, nz); if (slope > Mathf.Min(24f, maxSlope)) { slopeRejected++; continue; }
            int ax = Mathf.Clamp(Mathf.RoundToInt(nx * (aw - 1)), 0, aw - 1);
            int ay = Mathf.Clamp(Mathf.RoundToInt(nz * (ah - 1)), 0, ah - 1);
            if (!SampleAlphamapCached(ax, ay, out float sandW, out float grassW) || sandW > 0.02f || grassW < 0.85f) { biomeRejected++; continue; }
            Vector3 world = new Vector3(nx * size.x, 0f, nz * size.z) + terrain.transform.position;
            world.y = GroundYTerrainOnly(world);
            validCandidates++;
            if (SpatialTooClose(world, minDistSqr)) { spacingRejected++; continue; }

            // Decide whether this particular plant instance really needs to be networked.
            // We cap the number of network-instantiated plants per session to avoid running
            // out of Photon ViewIDs on large maps.
            bool shouldNetworkThisPlant = isNetworkedSession &&
                                          networkCapableIndices != null &&
                                          networkCapableIndices.Count > 0 &&
                                          placed < maxNetworkedPlants;

            int idx = shouldNetworkThisPlant
                ? networkCapableIndices[Random.Range(0, networkCapableIndices.Count)]
                : Random.Range(0, maxLen);
            string photonName = (hasPhoton && idx < photonLen) ? plantPhotonPrefabs[idx] : null;
            GameObject localPrefab = (hasLocal && idx < localLen) ? plantLocalPrefabs[idx] : null;

            if (shouldNetworkThisPlant && !isAuthoritative)
            {
                // this slot is expected to arrive from master via Photon instantiate.
                // keep deterministic spacing progression in sync without creating local duplicates.
                occupied.Add(world);
                SpatialAdd(world);
                placed++;
                if (YieldIfNeeded()) yield return null;
                continue;
            }

            var t = SpawnPrefab(photonName, localPrefab, world, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), shouldNetworkThisPlant);
            if (t != null)
            {
                if (!plantKeepPrefabScale)
                {
                    float s = Mathf.Clamp(plantScaleMin, 0.01f, 10f);
                    t.localScale *= s;
                }
                SnapToGround(t, GroundSnapMode.TerrainOnly);
                t.SetParent(understoryRoot);
                spawned.Add(t);
                occupied.Add(world);
                SpatialAdd(world);
                placed++;
                if (YieldIfNeeded()) yield return null;
            }
        }

        lastPlantScatterMetrics = new ResourceScatterMetrics
        {
            label = "UnderstoryPlants",
            desiredCount = plantDesiredCount,
            placed = placed,
            attempts = attempts,
            perlinScale = 0f,
            perlinThreshold = 0f,
            minSpacing = plantMinSpacing,
            perlinRejected = 0,
            heightRejected = heightRejected,
            slopeRejected = slopeRejected,
            biomeRejected = biomeRejected,
            waterRejected = 0,
            spacingRejected = spacingRejected,
            validCandidates = validCandidates
        };

        Random.state = previousRandomState;
    }

    private IEnumerator ScatterFerns()
    {
        bool hasLocal = fernLocalPrefabs != null && fernLocalPrefabs.Length > 0;
        bool hasPhoton = fernPhotonPrefabs != null && fernPhotonPrefabs.Length > 0;
        if (!hasLocal && !hasPhoton) yield break;
        int desiredCount = GetDecorDesiredCount(fernDesiredCount);
        desiredCount = ApplyMultiplayerDecorCap(desiredCount, multiplayerMaxFerns);
        if (desiredCount <= 0) yield break;
        var td = terrain.terrainData; Vector3 size = td.size;
        int aw = td.alphamapWidth; int ah = td.alphamapHeight;
        float minDistSqr = fernMinSpacing * fernMinSpacing;
        int localLen = hasLocal ? fernLocalPrefabs.Length : 0;
        int photonLen = hasPhoton ? fernPhotonPrefabs.Length : 0;
        int maxLen = Mathf.Max(localLen, photonLen);
        if (maxLen <= 0) yield break;

        var fernsRoot = GetSceneRoot("Ferns");
        for (int i = fernsRoot.childCount - 1; i >= 0; i--) { var go = fernsRoot.GetChild(i).gameObject; if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }

        int placed = 0;
        int attempts = 0; int maxAttempts = Mathf.Max(desiredCount * 40, 1500);
        int heightRejected = 0;
        int slopeRejected = 0;
        int biomeRejected = 0;
        int spacingRejected = 0;
        int validCandidates = 0;
        while (placed < desiredCount && attempts < maxAttempts)
        {
            attempts++;
            if (YieldIfTooManyAttempts()) yield return null;
            float nx = Random.value; float nz = Random.value;
            float hNorm = SampleHeightCached(nx, nz); if (hNorm < minGrassHeight) { heightRejected++; continue; }
            float slope = SampleSlopeCached(nx, nz); if (slope > Mathf.Min(24f, maxSlope)) { slopeRejected++; continue; }
            int ax = Mathf.Clamp(Mathf.RoundToInt(nx * (aw - 1)), 0, aw - 1);
            int ay = Mathf.Clamp(Mathf.RoundToInt(nz * (ah - 1)), 0, ah - 1);
            if (!SampleAlphamapCached(ax, ay, out float sandW, out float grassW) || sandW > 0.02f || grassW < 0.85f) { biomeRejected++; continue; }
            Vector3 world = new Vector3(nx * size.x, 0f, nz * size.z) + terrain.transform.position;
            world.y = GroundYTerrainOnly(world);
            validCandidates++;
            if (SpatialTooClose(world, minDistSqr)) { spacingRejected++; continue; }
            int idx = Random.Range(0, maxLen);
            string photon = (photonLen > 0 && idx < photonLen) ? fernPhotonPrefabs[idx] : null;
            GameObject local = (localLen > 0 && idx < localLen) ? fernLocalPrefabs[idx] : null;
            var t = SpawnPrefab(photon, local, world, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), false);
            if (t != null)
            {
                float s = Mathf.Clamp(Random.Range(fernScaleMin, fernScaleMax), 0.05f, 10f);
                t.localScale *= s;
                t.SetParent(fernsRoot);
                occupied.Add(world); spawned.Add(t);
                SpatialAdd(world);
                RegisterDecorForCulling(t);
                placed++;
                if (YieldIfNeeded()) yield return null;
            }
        }

        lastFernScatterMetrics = new ResourceScatterMetrics
        {
            label = "Ferns",
            desiredCount = desiredCount,
            placed = placed,
            attempts = attempts,
            perlinScale = 0f,
            perlinThreshold = 0f,
            minSpacing = fernMinSpacing,
            perlinRejected = 0,
            heightRejected = heightRejected,
            slopeRejected = slopeRejected,
            biomeRejected = biomeRejected,
            waterRejected = 0,
            spacingRejected = spacingRejected,
            validCandidates = validCandidates
        };
    }

    private IEnumerator ScatterTrees()
    {
        bool hasLocal = treeLocalPrefabs != null && treeLocalPrefabs.Length > 0;
        bool hasPhoton = treePhotonPrefabs != null && treePhotonPrefabs.Length > 0;
        if (!hasLocal && !hasPhoton) yield break;
        int desiredCount = GetDecorDesiredCount(treeDesiredCount);
        desiredCount = ApplyMultiplayerDecorCap(desiredCount, multiplayerMaxTrees);
        if (desiredCount <= 0) yield break;
        var td = terrain.terrainData; Vector3 size = td.size;
        int aw = td.alphamapWidth; int ah = td.alphamapHeight;
        float minDistSqr = treeMinSpacing * treeMinSpacing;
        int localLen = hasLocal ? treeLocalPrefabs.Length : 0;
        int photonLen = hasPhoton ? treePhotonPrefabs.Length : 0;
        int maxLen = Mathf.Max(localLen, photonLen);
        if (maxLen <= 0) yield break;

        var treesRoot = GetSceneRoot("Trees");
        for (int i = treesRoot.childCount - 1; i >= 0; i--) { var go = treesRoot.GetChild(i).gameObject; if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }

        int placed = 0;
        int attempts = 0; int maxAttempts = desiredCount * 40;
        int heightRejected = 0;
        int slopeRejected = 0;
        int biomeRejected = 0;
        int spacingRejected = 0;
        int validCandidates = 0;
        while (placed < desiredCount && attempts < maxAttempts)
        {
            attempts++;
            if (YieldIfTooManyAttempts()) yield return null;
            float nx = Random.value; float nz = Random.value;
            float hNorm = SampleHeightCached(nx, nz);
            if (hNorm < minGrassHeight) { heightRejected++; continue; }
            float slope = SampleSlopeCached(nx, nz); if (slope > maxSlope) { slopeRejected++; continue; }
            int ax = Mathf.Clamp(Mathf.RoundToInt(nx * (aw - 1)), 0, aw - 1);
            int ay = Mathf.Clamp(Mathf.RoundToInt(nz * (ah - 1)), 0, ah - 1);
            if (!SampleAlphamapCached(ax, ay, out float sandW, out float grassW) || sandW > 0.02f || grassW < 0.75f) { biomeRejected++; continue; }

            Vector3 world = new Vector3(nx * size.x, 0f, nz * size.z) + terrain.transform.position;
            world.y = GroundYTerrainOnly(world);
            validCandidates++;
            if (SpatialTooClose(world, minDistSqr)) { spacingRejected++; continue; }

            int idx = Random.Range(0, maxLen);
            string photonName = (hasPhoton && idx < photonLen) ? treePhotonPrefabs[idx] : null; GameObject localPrefab = (hasLocal && idx < localLen) ? treeLocalPrefabs[idx] : null;
            var t = SpawnPrefab(photonName, localPrefab, world, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), false);
            if (t != null)
            {
                float s = Random.Range(Mathf.Min(treeScaleMin, treeScaleMax), Mathf.Max(treeScaleMin, treeScaleMax));
                t.localScale *= s;
                SnapToGround(t, GroundSnapMode.TerrainOnly);
                t.SetParent(treesRoot);
                occupied.Add(world); spawned.Add(t);
                SpatialAdd(world);
                RegisterDecorForCulling(t);
                placed++;
                if (YieldIfNeeded()) yield return null;
            }
        }

        lastTreeScatterMetrics = new ResourceScatterMetrics
        {
            label = "Trees",
            desiredCount = desiredCount,
            placed = placed,
            attempts = attempts,
            perlinScale = 0f,
            perlinThreshold = 0f,
            minSpacing = treeMinSpacing,
            perlinRejected = 0,
            heightRejected = heightRejected,
            slopeRejected = slopeRejected,
            biomeRejected = biomeRejected,
            waterRejected = 0,
            spacingRejected = spacingRejected,
            validCandidates = validCandidates
        };
    }

    private IEnumerator ScatterRocks()
    {
        var td = terrain.terrainData; Vector3 size = td.size;
        int aw = td.alphamapWidth; int ah = td.alphamapHeight;
        int desiredSmall = GetDecorDesiredCount(smallRockDesiredCount);
        int desiredBig = GetDecorDesiredCount(bigRockDesiredCount);
        desiredSmall = ApplyMultiplayerDecorCap(desiredSmall, multiplayerMaxSmallRocks);
        desiredBig = ApplyMultiplayerDecorCap(desiredBig, multiplayerMaxBigRocks);

        // Mirror the herb / plants parenting pattern:
        // - find (or create) a stable Rocks root under the generated environment
        // - keep size categories in their own child containers
        var rocksRoot = GetSceneRoot("Rocks");

        // Ensure dedicated children for small / big rocks
        var smallRoot = rocksRoot.Find("_Small");
        if (smallRoot == null)
        {
            var go = new GameObject("_Small");
            go.transform.SetParent(rocksRoot, false);
            smallRoot = go.transform;
        }

        var bigRoot = rocksRoot.Find("_Big");
        if (bigRoot == null)
        {
            var go = new GameObject("_Big");
            go.transform.SetParent(rocksRoot, false);
            bigRoot = go.transform;
        }

        // Clear only previously generated small / big rocks so other manual children
        // under Rocks (if any) are preserved, just like how plants only clear Herbs.
        for (int i = smallRoot.childCount - 1; i >= 0; i--)
        {
            var go = smallRoot.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        for (int i = bigRoot.childCount - 1; i >= 0; i--)
        {
            var go = bigRoot.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        bool ValidGrass(float nx, float nz)
        {
            int ax = Mathf.Clamp(Mathf.RoundToInt(nx * (aw - 1)), 0, aw - 1);
            int ay = Mathf.Clamp(Mathf.RoundToInt(nz * (ah - 1)), 0, ah - 1);
            return SampleAlphamapCached(ax, ay, out float sandW, out float grassW) && sandW <= 0.05f && grassW >= 0.6f;
        }

        float smallDistSqr = smallRockMinSpacing * smallRockMinSpacing;
        float bigDistSqr = bigRockMinSpacing * bigRockMinSpacing;

        int smallLocalLen = smallRockLocalPrefabs != null ? smallRockLocalPrefabs.Length : 0;
        int smallPhotonLen = smallRockPhotonPrefabs != null ? smallRockPhotonPrefabs.Length : 0;
        int smallMaxLen = Mathf.Max(smallLocalLen, smallPhotonLen);

        if (desiredSmall > 0 && ((smallRockLocalPrefabs != null && smallRockLocalPrefabs.Length > 0) || (smallRockPhotonPrefabs != null && smallRockPhotonPrefabs.Length > 0)))
        {
            int placed = 0; int attempts = 0; int maxAttempts = desiredSmall * 40;
            int heightRejected = 0;
            int slopeRejected = 0;
            int biomeRejected = 0;
            int spacingRejected = 0;
            int validCandidates = 0;
            while (placed < desiredSmall && attempts < maxAttempts)
            {
                attempts++;
                if (YieldIfTooManyAttempts()) yield return null;
                float nx = Random.value; float nz = Random.value;
                float hNorm = SampleHeightCached(nx, nz); if (hNorm < minGrassHeight) { heightRejected++; continue; }
                float slope = SampleSlopeCached(nx, nz); if (slope > maxSlope) { slopeRejected++; continue; }
                if (!ValidGrass(nx, nz)) { biomeRejected++; continue; }
                Vector3 world = new Vector3(nx * size.x, 0f, nz * size.z) + terrain.transform.position;
                world.y = GroundYTerrainOnly(world);
                validCandidates++;
                if (SpatialTooClose(world, smallDistSqr)) { spacingRejected++; continue; }
                int idx = Random.Range(0, smallMaxLen);
                string photon = (smallPhotonLen > 0 && idx < smallPhotonLen) ? smallRockPhotonPrefabs[idx] : null;
                GameObject local = (smallLocalLen > 0 && idx < smallLocalLen) ? smallRockLocalPrefabs[idx] : null;
                var t = SpawnPrefab(photon, local, world, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), false);
                if (t != null)
                {
                    SnapToGround(t, GroundSnapMode.TerrainOnly);
                    // Keep world position and parent under GeneratedEnvironment/Rocks/_Small,
                    // matching how plants are grouped under Plants/Herbs.
                    t.SetParent(smallRoot, true);
                    occupied.Add(world); spawned.Add(t);
                    SpatialAdd(world);
                    RegisterDecorForCulling(t);
                    placed++;
                    if (YieldIfNeeded()) yield return null;
                }
            }

            lastSmallRockScatterMetrics = new ResourceScatterMetrics
            {
                label = "Rocks_Small",
                desiredCount = desiredSmall,
                placed = placed,
                attempts = attempts,
                perlinScale = 0f,
                perlinThreshold = 0f,
                minSpacing = smallRockMinSpacing,
                perlinRejected = 0,
                heightRejected = heightRejected,
                slopeRejected = slopeRejected,
                biomeRejected = biomeRejected,
                waterRejected = 0,
                spacingRejected = spacingRejected,
                validCandidates = validCandidates
            };
        }

        int bigLocalLen = bigRockLocalPrefabs != null ? bigRockLocalPrefabs.Length : 0;
        int bigPhotonLen = bigRockPhotonPrefabs != null ? bigRockPhotonPrefabs.Length : 0;
        int bigMaxLen = Mathf.Max(bigLocalLen, bigPhotonLen);

        if (desiredBig > 0 && ((bigRockLocalPrefabs != null && bigRockLocalPrefabs.Length > 0) || (bigRockPhotonPrefabs != null && bigRockPhotonPrefabs.Length > 0)))
        {
            int placed = 0; int attempts = 0; int maxAttempts = desiredBig * 50;
            int heightRejected = 0;
            int slopeRejected = 0;
            int biomeRejected = 0;
            int spacingRejected = 0;
            int validCandidates = 0;
            while (placed < desiredBig && attempts < maxAttempts)
            {
                attempts++;
                if (YieldIfTooManyAttempts()) yield return null;
                float nx = Random.value; float nz = Random.value;
                float hNorm = SampleHeightCached(nx, nz); if (hNorm < minGrassHeight) { heightRejected++; continue; }
                float slope = SampleSlopeCached(nx, nz); if (slope > maxSlope) { slopeRejected++; continue; }
                if (!ValidGrass(nx, nz)) { biomeRejected++; continue; }
                Vector3 world = new Vector3(nx * size.x, 0f, nz * size.z) + terrain.transform.position;
                world.y = GroundYTerrainOnly(world);
                validCandidates++;
                if (SpatialTooClose(world, bigDistSqr)) { spacingRejected++; continue; }
                int idx = Random.Range(0, bigMaxLen);
                string photon = (bigPhotonLen > 0 && idx < bigPhotonLen) ? bigRockPhotonPrefabs[idx] : null;
                GameObject local = (bigLocalLen > 0 && idx < bigLocalLen) ? bigRockLocalPrefabs[idx] : null;
                var t = SpawnPrefab(photon, local, world, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), false);
                if (t != null)
                {
                    SnapToGround(t, GroundSnapMode.TerrainOnly);
                    // Same grouping for big rocks.
                    t.SetParent(bigRoot, true);
                    occupied.Add(world); spawned.Add(t);
                    SpatialAdd(world);
                    RegisterDecorForCulling(t);
                    placed++;
                    if (YieldIfNeeded()) yield return null;
                }
            }

            lastBigRockScatterMetrics = new ResourceScatterMetrics
            {
                label = "Rocks_Big",
                desiredCount = desiredBig,
                placed = placed,
                attempts = attempts,
                perlinScale = 0f,
                perlinThreshold = 0f,
                minSpacing = bigRockMinSpacing,
                perlinRejected = 0,
                heightRejected = heightRejected,
                slopeRejected = slopeRejected,
                biomeRejected = biomeRejected,
                waterRejected = 0,
                spacingRejected = spacingRejected,
                validCandidates = validCandidates
            };
        }
    }

    private void CleanupLocalDecorOnly()
    {
        var roots = new[] { "Ferns", "Trees", "Rocks" };
        for (int r = 0; r < roots.Length; r++)
        {
            var root = GameObject.Find(GeneratedRootName + "/" + roots[r]);
            if (root == null)
            {
                var generatedRoot = GameObject.Find(GeneratedRootName);
                if (generatedRoot != null)
                {
                    var t = generatedRoot.transform.Find(roots[r]);
                    if (t != null) root = t.gameObject;
                }
            }

            if (root == null) continue;
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                var child = root.transform.GetChild(i);
                if (child == null) continue;
                Destroy(child.gameObject);
            }
        }
    }

    private List<Vector3> FindInlandPositions(int count, float aboveSand, float spacing)
    {
        var td = terrain.terrainData; Vector3 size = td.size;
        float sand = Mathf.Max(0.001f, terrainGenerator.sandThreshold);
        var positions = new List<Vector3>(count);
        int attempts = 0; int maxAttempts = count * 2000;
        while (positions.Count < count && attempts < maxAttempts)
        {
            attempts++;
            float nx = Random.value; float nz = Random.value;
            float hNorm = SampleHeightCached(nx, nz);
            if (hNorm <= sand + Mathf.Max(0.02f, aboveSand)) continue; // ensure clearly inland
            float slope = SampleSlopeCached(nx, nz); if (slope > maxSlope) continue;
            // local neighborhood must also be inland (edge buffer)
            if (!NeighborhoodInland(td, nx, nz, sand + Mathf.Max(0.02f, aboveSand), 5)) continue;
            Vector3 world = new Vector3(nx * size.x, 0f, nz * size.z) + terrain.transform.position;
            world.y = GroundYTerrainOnly(world);
            bool tooClose = false; foreach (var p in positions) { if ((p - world).sqrMagnitude < spacing * spacing) { tooClose = true; break; } }
            if (tooClose) continue;
            positions.Add(world);
        }
        // Fallback: if still short, relax constraints and try again to guarantee placement
        while (positions.Count < count)
        {
            float nx = Random.value; float nz = Random.value;
            Vector3 world = new Vector3(nx * size.x, 0f, nz * size.z) + terrain.transform.position;
            world.y = GroundYTerrainOnly(world);
            positions.Add(world);
        }
        return positions;
    }

    // Returns true if a small ring around (nx,nz) is also above minH, used to keep far from edges
    private bool NeighborhoodInland(TerrainData td, float nx, float nz, float minH, int ring)
    {
        int w = td.heightmapResolution - 1; int h = w;
        int x = Mathf.Clamp(Mathf.RoundToInt(nx * w), 0, w);
        int z = Mathf.Clamp(Mathf.RoundToInt(nz * h), 0, h);
        int step = Mathf.Max(1, w / 256);
        for (int dz = -ring; dz <= ring; dz++)
        {
            for (int dx = -ring; dx <= ring; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                float qx = Mathf.Clamp01((x + dx * step) / (float)w);
                float qz = Mathf.Clamp01((z + dz * step) / (float)h);
                float hn = SampleHeightCached(qx, qz);
                if (hn < minH) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// compute the minimum normalized height for beach spawns based on the terrain generator's
    /// sand threshold and beach width, offset by spawnBeachPosition (0=water edge, 1=grass line).
    /// </summary>
    private float ComputeBeachSpawnMinHeight()
    {
        float sand = terrainGenerator != null ? terrainGenerator.sandThreshold : 0.02f;
        float beach = terrainGenerator != null ? terrainGenerator.beachWidth : 0.08f;
        // lerp across the beach band; clamp position so spawns are never right at water edge
        float t = Mathf.Clamp(spawnBeachPosition, 0.1f, 1f);
        return sand + beach * t;
    }

    /// <summary>
    /// returns the world-space Y of the water surface, or negative infinity if no water assigned.
    /// </summary>
    private float WaterSurfaceY()
    {
        if (waterTransform == null)
            return float.NegativeInfinity;

        if (waterUseRendererBounds)
        {
            // Prefer renderer bounds if available (many water prefabs have pivot below/above the surface).
            var r = waterTransform.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                // Use bounds.max.y as a "surface" approximation for water meshes/planes.
                // If the water is a flat plane, max.y == min.y == surface.
                return r.bounds.max.y;
            }
        }

        // Recommended for a simple reference cube: place the transform at the desired water Y.
        return waterTransform.position.y;
    }

    /// <summary>
    /// returns true if the world position is below the water surface.
    /// </summary>
    private bool IsBelowWater(Vector3 world)
    {
        if (waterTransform == null) return false;
        return world.y < WaterSurfaceY();
    }

    /// <summary>
    /// clamps a position's Y to be at least at the water surface + a small offset.
    /// </summary>
    private Vector3 ClampAboveWater(Vector3 world, float offset)
    {
        if (waterTransform == null) return world;
        float minY = WaterSurfaceY() + offset;
        if (world.y < minY)
            world.y = minY;
        return world;
    }

    private Vector3 FindEdgePosition(float spacing, float slopeLimit, float minHeightOverride = -1f)
    {
        var list = FindMultipleEdgePositions(1, spacing, slopeLimit, minHeightOverride);
        return list.Count > 0 ? list[0] : terrain.transform.position;
    }

    private Quaternion GetBrokenShipShoreFacingRotation(Vector3 worldPos)
    {
        if (terrain == null || terrain.terrainData == null)
            return Quaternion.Euler(0f, 180f + brokenShipFacingYawOffset, 0f);

        Vector3 islandCenter = terrain.transform.position + new Vector3(terrain.terrainData.size.x * 0.5f, 0f, terrain.terrainData.size.z * 0.5f);
        Vector3 lookDir = islandCenter - worldPos; // edge -> center means face inland/shore side
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude < 0.0001f)
            lookDir = Vector3.forward;

        // base rotation faces inland; add 180° so the broken bow faces back toward water by default,
        // then allow small designer offset via brokenShipFacingYawOffset.
        Quaternion inlandFacing = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
        return inlandFacing * Quaternion.Euler(0f, 180f + brokenShipFacingYawOffset, 0f);
    }

    // Pick an edge position that is at least minDistance away from 'avoid'. If none meet the threshold,
    // choose the farthest among a larger candidate set.
    private Vector3 FindEdgePositionFarFrom(Vector3 avoid, float spacing, float slopeLimit, float minDistance, float minHeightOverride = -1f)
    {
        var candidates = FindMultipleEdgePositions(48, spacing, slopeLimit, minHeightOverride);
        if (candidates.Count == 0) return FindEdgePosition(spacing, slopeLimit, minHeightOverride);
        float minDistSqr = minDistance * minDistance;
        Vector3 best = candidates[0];
        float bestDist = -1f;
        for (int i = 0; i < candidates.Count; i++)
        {
            float d = (candidates[i] - avoid).sqrMagnitude;
            if (d >= minDistSqr) return candidates[i];
            if (d > bestDist) { best = candidates[i]; bestDist = d; }
        }
        return best;
    }

    private Vector3 FindEdgePositionFarFromPoints(List<Vector3> avoidPoints, float spacing, float slopeLimit, float minDistance, float minHeightOverride = -1f)
    {
        if (avoidPoints == null || avoidPoints.Count == 0)
            return FindEdgePosition(spacing, slopeLimit, minHeightOverride);

        var candidates = FindMultipleEdgePositions(96, spacing, slopeLimit, minHeightOverride);
        if (candidates.Count == 0)
            return FindEdgePosition(spacing, slopeLimit, minHeightOverride);

        float minDistSqr = minDistance * minDistance;
        Vector3 best = candidates[0];
        float bestNearest = -1f;

        for (int i = 0; i < candidates.Count; i++)
        {
            float nearest = float.MaxValue;
            for (int j = 0; j < avoidPoints.Count; j++)
            {
                float d = (avoidPoints[j] - candidates[i]).sqrMagnitude;
                if (d < nearest) nearest = d;
            }

            if (nearest >= minDistSqr)
                return candidates[i];

            if (nearest > bestNearest)
            {
                bestNearest = nearest;
                best = candidates[i];
            }
        }

        return best;
    }

    private List<Vector3> ResolveSpawnPositions(List<Vector3> candidates, float requiredSpacing, float clearanceRadius, float minHeightOverride = -1f)
    {
        var result = new List<Vector3>(candidates != null ? candidates.Count : 0);
        if (candidates == null || candidates.Count == 0) return result;

        float requiredMinHeight = minHeightOverride >= 0f ? Mathf.Clamp01(minHeightOverride) : -1f;
        float minSpacingSqr = requiredSpacing * requiredSpacing;
        for (int i = 0; i < candidates.Count; i++)
        {
            Vector3 c = candidates[i];
            if (!TryResolveClearPlacement(c, clearanceRadius, out c))
                continue;

            // Ensure spawns are above the water plane (avoid underwater spawn points).
            c.y = GroundYTerrainOnly(c);
            c = ClampAboveWater(c, spawnAboveWaterOffset);
            if (IsBelowWater(c)) continue;

            if (requiredMinHeight >= 0f && terrain != null && terrain.terrainData != null)
            {
                Vector3 local = c - terrain.transform.position;
                float nx = Mathf.Clamp01(local.x / terrain.terrainData.size.x);
                float nz = Mathf.Clamp01(local.z / terrain.terrainData.size.z);
                float hNorm = SampleHeightCached(nx, nz);
                if (hNorm < requiredMinHeight)
                    continue;
            }

            bool tooClose = false;
            for (int r = 0; r < result.Count; r++)
            {
                if ((result[r] - c).sqrMagnitude < minSpacingSqr)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;
            result.Add(c);
        }

        if (result.Count > 0)
            return result;

        if (requiredMinHeight >= 0f)
            return FindMultipleIslandShorePositions(Mathf.Max(1, maxPlayers), requiredSpacing, maxSlope, requiredMinHeight);

        return candidates;
    }

    private Vector3 FindBoatShorePositionFarFromSpawns(List<Vector3> spawnPoints, Vector3 avoidPoint, float minDistanceFromSpawns, float spacing)
    {
        if (spawnPoints == null || spawnPoints.Count == 0)
            return FindEdgePosition(spacing, maxSlope);

        var candidates = FindMultipleEdgePositions(96, spacing, maxSlope);
        if (candidates == null || candidates.Count == 0)
            return FindEdgePosition(spacing, maxSlope);

        float minSpawnDistSqr = minDistanceFromSpawns * minDistanceFromSpawns;
        float minBrokenDistSqr = Mathf.Max(10f, minBrokenShipToSpawnDistance * 0.6f);
        minBrokenDistSqr *= minBrokenDistSqr;

        Vector3 best = candidates[0];
        float bestScore = float.MinValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            Vector3 c = candidates[i];
            c = SnapTowardOuterShore(c, maxSlope);

            float nearestSpawnSqr = float.MaxValue;
            for (int s = 0; s < spawnPoints.Count; s++)
            {
                float ds = (spawnPoints[s] - c).sqrMagnitude;
                if (ds < nearestSpawnSqr)
                    nearestSpawnSqr = ds;
            }

            float brokenDistSqr = (avoidPoint - c).sqrMagnitude;
            bool farEnough = nearestSpawnSqr >= minSpawnDistSqr && brokenDistSqr >= minBrokenDistSqr;

            // prioritize keeping distance from spawn markers, then from broken ship area.
            float score = nearestSpawnSqr + brokenDistSqr * 0.25f;
            if (!farEnough)
                score *= 0.5f;

            if (score > bestScore)
            {
                bestScore = score;
                best = c;
            }

            if (farEnough)
            {
                best = c;
                break;
            }
        }

        best.y = GroundYTerrainOnly(best);
        return best;
    }

    private bool IsInOuterIslandBand(Vector3 world)
    {
        if (terrain == null || terrain.terrainData == null)
            return false;

        var td = terrain.terrainData;
        Vector3 size = td.size;
        Vector3 local = world - terrain.transform.position;
        Vector2 center = new Vector2(size.x * 0.5f, size.z * 0.5f);
        float maxRadius = Mathf.Min(size.x, size.z) * 0.5f;
        if (maxRadius <= 0.01f)
            return false;

        Vector2 p = new Vector2(local.x, local.z);
        float radius01 = Vector2.Distance(p, center) / maxRadius;
        return radius01 >= edgeOuterRingMinRadius;
    }

    private bool HasOuterWaterNeighborhood(Vector3 world)
    {
        if (terrain == null || terrain.terrainData == null)
            return false;

        var td = terrain.terrainData;
        Vector3 size = td.size;
        float sand = Mathf.Max(0.001f, terrainGenerator.sandThreshold);
        float waterBand = sand + Mathf.Max(0.005f, edgeBandAboveSand);
        float radius = Mathf.Max(1f, edgeOuterNeighborCheckRadius);
        int checks = 12;
        int waterLike = 0;
        int insideBounds = 0;

        for (int i = 0; i < checks; i++)
        {
            float a = (Mathf.PI * 2f * i) / checks;
            Vector3 probe = world + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            Vector3 local = probe - terrain.transform.position;
            if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z)
                continue;

            insideBounds++;
            float nx = Mathf.Clamp01(local.x / size.x);
            float nz = Mathf.Clamp01(local.z / size.z);
            if (SampleHeightCached(nx, nz) <= waterBand)
                waterLike++;
        }

        if (insideBounds == 0)
            return false;

        float ratio = waterLike / (float)insideBounds;
        return ratio >= edgeOuterWaterNeighborRatio;
    }

    private Vector3 SnapTowardOuterShore(Vector3 world, float slopeLimit, float overrideMinH = -1f, float overrideMaxH = -1f)
    {
        if (terrain == null || terrain.terrainData == null)
            return world;

        if (IsInOuterIslandBand(world) && HasOuterWaterNeighborhood(world))
        {
            world.y = GroundYTerrainOnly(world);
            return world;
        }

        var td = terrain.terrainData;
        Vector3 size = td.size;
        Vector3 local = world - terrain.transform.position;
        Vector2 center = new Vector2(size.x * 0.5f, size.z * 0.5f);
        Vector2 dir = new Vector2(local.x, local.z) - center;
        if (dir.sqrMagnitude < 0.0001f)
            dir = new Vector2(1f, 0f);
        dir.Normalize();

        float maxRadius = Mathf.Min(size.x, size.z) * 0.5f;
        float sand = Mathf.Max(0.001f, terrainGenerator.sandThreshold);
        float minH = overrideMinH >= 0f ? overrideMinH : sand + Mathf.Max(0.005f, edgeBandAboveSand);
        float maxH = overrideMaxH >= 0f ? overrideMaxH : minH + Mathf.Max(0.01f, edgeBandWidth);

        Vector3 best = world;
        for (int i = 0; i < 14; i++)
        {
            float r01 = Mathf.Lerp(edgeOuterRingMinRadius, 0.98f, i / 13f);
            Vector2 p = center + dir * (r01 * maxRadius);
            if (p.x < 0f || p.y < 0f || p.x > size.x || p.y > size.z)
                continue;

            float nx = Mathf.Clamp01(p.x / size.x);
            float nz = Mathf.Clamp01(p.y / size.z);
            float hNorm = SampleHeightCached(nx, nz);
            if (hNorm < minH || hNorm > maxH)
                continue;

            float slope = SampleSlopeCached(nx, nz);
            if (slope > slopeLimit)
                continue;

            Vector3 candidate = new Vector3(p.x, 0f, p.y) + terrain.transform.position;
            candidate.y = GroundYTerrainOnly(candidate);
            if (IsInOuterIslandBand(candidate) && HasOuterWaterNeighborhood(candidate))
                return candidate;

            best = candidate;
        }

        best.y = GroundYTerrainOnly(best);
        return best;
    }

    private List<Vector3> FindMultipleEdgePositions(int count, float spacing, float slopeLimit, float minHeightOverride = -1f)
    {
        var td = terrain.terrainData; Vector3 size = td.size;
        float sand = Mathf.Max(0.001f, terrainGenerator.sandThreshold);
        float minH = sand + Mathf.Max(0.005f, edgeBandAboveSand);
        if (minHeightOverride >= 0f)
            minH = Mathf.Max(minH, Mathf.Clamp01(minHeightOverride));
        float maxH = minH + Mathf.Max(0.01f, edgeBandWidth);
        var positions = new List<Vector3>(count);
        int attempts = 0; int maxAttempts = count * 1200;
        while (positions.Count < count && attempts < maxAttempts)
        {
            attempts++;
            float nx = Random.value; float nz = Random.value;
            float hNorm = SampleHeightCached(nx, nz);
            if (hNorm < minH || hNorm > maxH) continue; // edge band
            float slope = SampleSlopeCached(nx, nz); if (slope > slopeLimit) continue;
            Vector3 world = new Vector3(nx * size.x, 0f, nz * size.z) + terrain.transform.position;
            world.y = GroundYTerrainOnly(world);
            if (IsBelowWater(world)) continue; // reject underwater positions
            if (!IsInOuterIslandBand(world)) continue;
            if (!HasOuterWaterNeighborhood(world)) continue;
            bool tooClose = false; foreach (var p in positions) { if ((p - world).sqrMagnitude < spacing * spacing) { tooClose = true; break; } }
            if (tooClose) continue;
            positions.Add(world);
        }
        // fallback: choose points on an outer ring so spawns/boat never drift into interior valleys.
        // use the same height band so fallback spawns are also on dry beach, not underwater
        while (positions.Count < count)
        {
            Vector2 center = new Vector2(size.x * 0.5f, size.z * 0.5f);
            float maxRadius = Mathf.Min(size.x, size.z) * 0.5f;
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Random.Range(edgeOuterRingMinRadius, 0.98f) * maxRadius;
            Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            p.x = Mathf.Clamp(p.x, 0f, size.x);
            p.y = Mathf.Clamp(p.y, 0f, size.z);
            Vector3 world = new Vector3(p.x, 0f, p.y) + terrain.transform.position;
            world = SnapTowardOuterShore(world, slopeLimit, minH, maxH);
            world.y = GroundYTerrainOnly(world);
            world = ClampAboveWater(world, spawnAboveWaterOffset);
            positions.Add(world);
        }
        return positions;
    }

    // Produces a clustered set of edge spawn points around a provided base position
    private List<Vector3> GenerateClusteredEdgeSpawnsFromBase(Vector3 basePos, int count, float spacing, float slopeLimit)
    {
        var result = new List<Vector3>(count);
        Vector3 normal = ApproxTerrainNormal(basePos);
        Vector3 tangent = Vector3.Cross(normal, Vector3.up);
        if (tangent.sqrMagnitude < 1e-3f) tangent = Vector3.right;
        tangent.Normalize();
        float step = Mathf.Max(1f, spacing * 0.6f);
        // center the cluster around basePos
        int leftCount = (count - 1) / 2;
        int rightCount = count - 1 - leftCount;
        for (int i = leftCount; i > 0; i--)
        {
            Vector3 p = basePos - tangent * (i * step);
            p = SnapTowardIslandShore(p, slopeLimit, ComputeBeachSpawnMinHeight(), (terrainGenerator != null ? terrainGenerator.sandThreshold : 0.02f) + (terrainGenerator != null ? terrainGenerator.beachWidth : 0.08f) * 1.05f);
            p.y = GroundYTerrainOnly(p);
            p = ClampAboveWater(p, spawnAboveWaterOffset);
            result.Add(p);
        }
        result.Add(ClampAboveWater(basePos, spawnAboveWaterOffset));
        for (int i = 1; i <= rightCount; i++)
        {
            Vector3 p = basePos + tangent * (i * step);
            p = SnapTowardIslandShore(p, slopeLimit, ComputeBeachSpawnMinHeight(), (terrainGenerator != null ? terrainGenerator.sandThreshold : 0.02f) + (terrainGenerator != null ? terrainGenerator.beachWidth : 0.08f) * 1.05f);
            p.y = GroundYTerrainOnly(p);
            p = ClampAboveWater(p, spawnAboveWaterOffset);
            result.Add(p);
        }
        return result;
    }

    private float GroundY(Vector3 world)
    {
        float y = terrain.SampleHeight(world) + terrain.transform.position.y;
        Ray ray = new Ray(new Vector3(world.x, y + 50f, world.z), Vector3.down);
        if (Physics.Raycast(ray, out var hit, 120f, ~0, QueryTriggerInteraction.Ignore))
            y = hit.point.y;
        return y;
    }

    // Terrain-only height (ignores trees/rocks). Use for player spawns, markers, Nuno and boat.
    private float GroundYTerrainOnly(Vector3 world)
    {
        return terrain.SampleHeight(world) + terrain.transform.position.y;
    }

    private Vector3 ApproxTerrainNormal(Vector3 world)
    {
        var td = terrain.terrainData; Vector3 size = td.size;
        Vector3 local = world - terrain.transform.position;
        float nx = Mathf.Clamp01(local.x / size.x);
        float nz = Mathf.Clamp01(local.z / size.z);
        Vector3 normal = td.GetInterpolatedNormal(nx, nz);
        if (normal.sqrMagnitude < 1e-3f) normal = Vector3.up;
        return normal;
    }

    private Transform SpawnPrefab(string photonName, GameObject localPrefab, Vector3 position, Quaternion rotation, bool needsNetworking = true)
    {
        GameObject go = null;
        bool shouldNetwork = needsNetworking && PhotonNetwork.IsConnected && PhotonNetwork.InRoom;

        if (shouldNetwork)
        {
            if (!PhotonNetwork.IsMasterClient)
                return null;

            string networkPrefabName = !string.IsNullOrWhiteSpace(photonName)
                ? photonName
                : (localPrefab != null ? localPrefab.name : null);

            if (string.IsNullOrWhiteSpace(networkPrefabName))
            {
                if (logSummary) Debug.LogWarning("[MapResourcesGenerator] Refusing to network-spawn a generated object without a prefab name.");
                return null;
            }

            try
            {
                go = PhotonNetwork.InstantiateRoomObject(networkPrefabName, position, rotation);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MapResourcesGenerator] Failed to instantiate room object {networkPrefabName}: {e.Message}. Skipping spawn to avoid host/client desync.");
                return null;
            }

            return go != null ? go.transform : null;
        }

        // fallback / offline path: if networking is not requested, instantiate the local prefab instead.
        if (localPrefab != null)
        {
            go = Instantiate(localPrefab, position, rotation);
        }
        else if (!string.IsNullOrEmpty(photonName))
        {
            var res = Resources.Load<GameObject>(photonName);
            if (res != null) go = Instantiate(res, position, rotation);
        }

        return go != null ? go.transform : null;
    }

    private void ResolveActiveTerrainAndGenerator()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        var terrains = FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (terrains != null && terrains.Length > 0)
        {
            Terrain preferred = null;
            for (int i = 0; i < terrains.Length; i++)
            {
                if (terrains[i] != null && terrains[i].gameObject.scene == activeScene)
                {
                    preferred = terrains[i];
                    break;
                }
            }
            terrain = preferred != null ? preferred : terrains[0];
        }

        if (terrainGenerator == null || terrainGenerator.gameObject.scene != activeScene)
        {
            var generators = FindObjectsByType<TerrainGenerator>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            TerrainGenerator preferredGen = null;
            for (int i = 0; i < generators.Length; i++)
            {
                if (generators[i] != null && generators[i].gameObject.scene == activeScene)
                {
                    preferredGen = generators[i];
                    break;
                }
            }
            if (preferredGen != null)
                terrainGenerator = preferredGen;
            else if (generators != null && generators.Length > 0)
                terrainGenerator = generators[0];
        }

        // Prefer the TerrainGenerator-resolved terrain when available; this prevents us from
        // accidentally sampling a different Terrain (e.g., additive scenes / recovery scenes).
        if (terrainGenerator != null && terrainGenerator.terrain != null && terrainGenerator.terrain.terrainData != null)
        {
            terrain = terrainGenerator.terrain;
        }
        else if (terrainGenerator != null && terrain != null && terrain.terrainData != null && terrainGenerator.terrain != terrain)
        {
            // keep generator in sync if it doesn't already have a valid terrain reference
            terrainGenerator.terrain = terrain;
        }
    }

    private bool CanDestroyNetworkObject(PhotonView pv)
    {
        if (pv == null) return false;
        if (pv.ViewID <= 0) return true;
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return true;
        return PhotonNetwork.IsMasterClient;
    }

    // safely destroy an object that may have a photon view.
    // For runtime-generated network objects (ViewID > 0), prefer PhotonNetwork.Destroy when we have authority
    // so PUN can free the allocated ViewID and prevent "out of viewIDs" errors on repeated generations.
    // For scene views / local-only (ViewID <= 0) we do local cleanup only.
    private void SafeDestroyWithPhotonView(GameObject go)
    {
        if (go == null) return;

        if (!Application.isPlaying)
        {
            DestroyImmediate(go);
            return;
        }

        PhotonView pv = go.GetComponent<PhotonView>();
        if (pv == null)
        {
            Destroy(go);
            return;
        }

        // scene/local object: never network-destroy
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom || pv.ViewID <= 0)
        {
            Destroy(go);
            return;
        }

        if (CanDestroyNetworkObject(pv))
        {
            PhotonNetwork.Destroy(go);
            return;
        }

        // generated room-owned objects should be destroyed only by master during cleanup passes
        if (logSummary)
            Debug.Log($"[MapResourcesGenerator] skipped non-master destroy for '{go.name}' (ViewID {pv.ViewID}); waiting for authoritative cleanup.");
    }

    private void CleanupOldObjects()
    {
        RemoveExistingSpawnMarkers();

        if (spawned != null && spawned.Count > 0)
        {
            foreach (var t in spawned)
            {
                if (t != null)
                    SafeDestroyWithPhotonView(t.gameObject);
            }
            spawned.Clear();
        }

        var roots = new[] { "Plants", "Ferns", "Trees", "Rocks" };
        var generatedParent = GameObject.Find(GeneratedRootName);
        foreach (var rootName in roots)
        {
            // find under GeneratedEnvironment first, then fall back to scene-wide search
            Transform rootTransform = generatedParent != null ? generatedParent.transform.Find(rootName) : null;
            GameObject root = rootTransform != null ? rootTransform.gameObject : GameObject.Find(GeneratedRootName + "/" + rootName);
            if (root != null)
            {
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    var child = root.transform.GetChild(i);
                    if (child == null) continue;
                    // recurse into sub-containers (_Small, _Big, etc.)
                    for (int j = child.childCount - 1; j >= 0; j--)
                    {
                        var grandchild = child.GetChild(j);
                        if (grandchild == null) continue;
                        SafeDestroyWithPhotonView(grandchild.gameObject);
                    }
                    // destroy the container/child itself
                    SafeDestroyWithPhotonView(child.gameObject);
                }
            }
        }

        // cleanup generated environment root if present
        var generatedRoot = GameObject.Find(GeneratedRootName);
        if (generatedRoot != null)
        {
            for (int i = generatedRoot.transform.childCount - 1; i >= 0; i--)
            {
                var child = generatedRoot.transform.GetChild(i);
                if (child == null) continue;
                for (int j = child.childCount - 1; j >= 0; j--)
                {
                    var c = child.GetChild(j);
                    if (c == null) continue;
                    SafeDestroyWithPhotonView(c.gameObject);
                }
            }
        }
    }

    // Snap modes for different categories
    private enum GroundSnapMode { TerrainOnly, AnyColliderPreferTerrain }

    // Backwards-compatible default: prefer colliders but avoid self, then fall back to terrain
    private void SnapToGround(Transform t) => SnapToGround(t, GroundSnapMode.AnyColliderPreferTerrain);

    // Ensure spawned objects sit exactly on ground
    private void SnapToGround(Transform t, GroundSnapMode mode)
    {
        if (t == null) return;
        Vector3 pos = t.position;

        // Terrain baseline height first (always available)
        float terrainY = pos.y;
        var td = terrain != null ? terrain.terrainData : null;
        if (td != null)
        {
            Vector3 size = td.size; Vector3 local = pos - terrain.transform.position;
            float nx = Mathf.Clamp01(local.x / size.x); float nz = Mathf.Clamp01(local.z / size.z);
            terrainY = td.GetInterpolatedHeight(nx, nz) + terrain.transform.position.y;
        }

        float pivotOffsetBase = GetPivotBottomOffset(t);

        if (mode == GroundSnapMode.TerrainOnly)
        {
            pos.y = terrainY + pivotOffsetBase;
            t.position = pos;
            return;
        }

        // AnyColliderPreferTerrain: try colliders (ignoring self), then compare to terrain
        Ray ray = new Ray(pos + Vector3.up * 100f, Vector3.down);
        var hits = Physics.RaycastAll(ray, 300f, groundRaycastMask, QueryTriggerInteraction.Ignore);
        if (hits != null && hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].transform == null) continue;
                if (hits[i].transform == t || hits[i].transform.IsChildOf(t)) continue;
                // Prefer TerrainCollider or anything below a reasonable height above terrain
                bool isTerrain = hits[i].collider is TerrainCollider || hits[i].transform == terrain?.transform;
                float candidateY = hits[i].point.y;
                if (!isTerrain)
                {
                    // If this hit is much higher than terrain (likely a tree), ignore it
                    if (candidateY - terrainY > 1.0f) continue;
                }
                pos.y = candidateY + pivotOffsetBase;
                t.position = pos;
                return;
            }
        }

        // Fallback to terrain
        pos.y = terrainY + pivotOffsetBase;
        t.position = pos;
    }

    // Returns how far the transform's pivot is above its lowest renderer bound in world space
    private float GetPivotBottomOffset(Transform t)
    {
        var renderers = t.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0) return 0f;
        float minY = float.MaxValue;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            minY = Mathf.Min(minY, renderers[i].bounds.min.y);
        }
        if (minY == float.MaxValue) return 0f;
        return t.position.y - minY;
    }

    // Find or create a top-level scene root (e.g., "Trees", "Rocks", "Ferns", "Plants")
    private Transform GetSceneRoot(string name)
    {
        var generatedRoot = GameObject.Find(GeneratedRootName);
        if (generatedRoot == null)
            generatedRoot = new GameObject(GeneratedRootName);

        Transform existing = generatedRoot.transform.Find(name);
        if (existing != null)
            return existing;

        var go = new GameObject(name);
        go.transform.SetParent(generatedRoot.transform, false);
        return go.transform;
    }

    private Transform CreateFallbackSpawnMarker(Vector3 position, int markerIndex)
    {
        // lightweight marker object without primitive collider overhead
        var marker = new GameObject($"SpawnMarker_{markerIndex}");
        marker.transform.position = position;

        var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = "Visual";
        visual.transform.SetParent(marker.transform, false);
        visual.transform.localScale = new Vector3(0.6f, 1.2f, 0.6f);
        var col = visual.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var mr = visual.GetComponent<MeshRenderer>();
        if (mr != null) mr.material.color = new Color(0.2f, 0.8f, 1f, 0.9f);

        return marker.transform;
    }

    private bool TryCollectSpawnMarkersFromEntrance(Transform entranceRoot, int maxCount, out List<Vector3> spawnPositions)
    {
        spawnPositions = new List<Vector3>();
        if (entranceRoot == null) return false;

        var found = new List<(int index, Transform tf)>();
        var allChildren = entranceRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < allChildren.Length; i++)
        {
            Transform tf = allChildren[i];
            if (tf == null || tf == entranceRoot) continue;

            string n = tf.name;
            if (string.IsNullOrEmpty(n)) continue;
            if (!n.StartsWith("SpawnMarker")) continue;

            int numericIndex = ParseTrailingInt(n, found.Count + 1);
            found.Add((numericIndex, tf));
        }

        if (found.Count == 0) return false;

        found.Sort((a, b) => a.index.CompareTo(b.index));
        int take = Mathf.Min(Mathf.Max(1, maxCount), found.Count);
        for (int i = 0; i < take; i++)
        {
            Transform tf = found[i].tf;
            if (tf == null) continue;
            tf.name = $"SpawnMarker_{i + 1}";
            Vector3 p = tf.position;
            p.y = GroundYTerrainOnly(p);
            p = ClampAboveWater(p, spawnAboveWaterOffset);
            tf.position = p; // Move marker to ground so players spawn on terrain, not on entrance model
            spawnPositions.Add(p);
            ReserveOccupied(p);
        }

        return spawnPositions.Count > 0;
    }

    private int ParseTrailingInt(string value, int fallback)
    {
        if (string.IsNullOrEmpty(value)) return fallback;
        int end = value.Length - 1;
        while (end >= 0 && char.IsDigit(value[end])) end--;
        int start = end + 1;
        if (start >= value.Length) return fallback;
        string digits = value.Substring(start);
        if (int.TryParse(digits, out int parsed) && parsed > 0)
            return parsed;
        return fallback;
    }

    private void RemoveExistingSpawnMarkers()
    {
        var allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allObjects.Length; i++)
        {
            var obj = allObjects[i];
            if (obj == null || !obj.name.StartsWith("SpawnMarker_")) continue;
            SafeDestroyWithPhotonView(obj);
        }
    }

    private bool HasEquivalentSpawnMarkers(int count, float[] positions)
    {
        if (count <= 0 || positions == null || positions.Length < count * 3)
            return false;

        var allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var markers = new List<(int index, Transform tf)>();
        for (int i = 0; i < allObjects.Length; i++)
        {
            var obj = allObjects[i];
            if (obj == null || !obj.name.StartsWith("SpawnMarker_"))
                continue;

            markers.Add((ParseTrailingInt(obj.name, markers.Count + 1), obj.transform));
        }

        if (markers.Count != count)
            return false;

        markers.Sort((a, b) => a.index.CompareTo(b.index));
        const float toleranceSqr = 0.2f * 0.2f;
        for (int i = 0; i < count; i++)
        {
            Transform tf = markers[i].tf;
            if (tf == null)
                return false;

            Vector3 expected = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
            if ((tf.position - expected).sqrMagnitude > toleranceSqr)
                return false;
        }

        return true;
    }

    private void RemoveExistingBrokenShipQuest()
    {
        var allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < allObjects.Length; i++)
        {
            var obj = allObjects[i];
            if (obj == null || obj.name != "BrokenShipQuest") continue;
            SafeDestroyWithPhotonView(obj);
        }
    }

}


