using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using Photon.Pun;

public class TerrainGenerator : MonoBehaviour
{
    public static int LastCompletedGenerationId { get; private set; }
    public static event System.Action<int> OnGenerationCompleted;

    private static int generationSequence = 0;
    public bool IsGenerationInProgress { get; private set; }
    public bool IsGenerationComplete { get; private set; }
    public int LastGenerationId { get; private set; }

    [Header("Generation Control")]
    public bool generateOnStart = true;
    public bool allowManualRegenerate = false; // keep false to block button-based re-gen
    private bool hasGenerated = false;
    [Header("Terrain Size")]
    public int width = 256;
    public int height = 256;
    public float heightMultiplier = 30f;

    [Header("Simple Controls")]
    [Tooltip("0 = random each run")] public int seed = 0;
    [Range(40f, 400f)] public float islandScale = 160f; // feature size – bigger = fewer, larger landmasses
    [Header("Island Size (shape falloff)")]
    [Tooltip("Overall island footprint. 1 = fills map, 0.05 = tiny island. Independent of Perlin detail.")]
    [Range(0.05f, 1f)] public float islandSize = 0.85f;
    [Range(0.25f, 0.85f)] public float targetLandPercent = 0.6f; // desired land coverage
    [Range(0.01f, 0.2f)] public float beachWidth = 0.08f; // thickness of the sand band in normalized height space
    [Range(0f, 1f)] public float mountainAmount = 0.35f; // fraction of land that becomes mountain
    [Range(0f, 1f)] public float roughness = 0.55f; // higher = more fragmented terrain

    [Header("Elevation Controls")]
    [Range(0f, 0.2f)] public float underwaterDepth = 0.02f; // height mapped for underwater sand
    [Range(0f, 0.6f)] public float shorelineLow = 0.05f; // start of beach ramp
    [Range(0.05f, 0.6f)] public float shorelineHigh = 0.3f; // end of beach ramp (before grass)
    [Range(0.15f, 0.7f)] public float inlandBase = 0.38f; // inland base elevation
    [Range(0.05f, 0.45f)] public float sandHeightThreshold = 0.25f; // physical height threshold - below this is sand (in normalized [0,1] after normalization)
    [Range(0f, 3f)] public float maxMountainHeight = 2.0f; // max height above inlandBase (allows mountains above 1.0)
    [Range(0f, 1f)] public float mountainIntensity = 0.5f; // strength of mountain generation
    [Range(20f, 200f)] public float mountainScale = 80f; // feature size for mountains/hills
    [Range(10f, 120f)] public float hillScale = 40f; // smaller scale for hills (layered on top of mountains)
    [Range(0f, 1f)] public float hillIntensity = 0.3f; // strength of hill layer (adds detail to mountains)

    [Header("Beach Tuning")]
    [Range(0f, 0.12f)] public float duneAmplitude = 0.06f;
    [Range(0.05f, 0.6f)] public float duneScale = 0.25f; // relative scale factor vs islandScale
    [Range(0f, 0.1f)] public float shoreDrop = 0.03f; // small lowering near water line

    [Header("Shape & Smoothness")]
    [Range(0f, 1f)] public float centerBias = 0.65f; // 1 = strong island center, 0 = no bias
    [Range(0, 10)] public int smoothIterations = 5; // stronger blur for smoother shoreline
    [Range(0f, 0.35f)] public float cavityAmount = 0.08f; // depth of inland holes/lakes
    [Range(0.2f, 2.5f)] public float cavityScale = 0.9f; // relative to islandScale
    [Range(0.3f, 0.95f)] public float smoothnessStrength = 0.75f; // strength of smoothing passes
    [Range(0f, 1f)] public float circularFalloff = 0.8f; // 1 = circular, 0 = square (corners trimmed)
    [Range(0.15f, 0.6f)] public float falloffStartDistance = 0.35f; // where falloff begins (0.35 = starts 65% from center)
    [Range(0.5f, 2f)] public float falloffSteepness = 1.2f; // how steep the falloff curve is (higher = steeper transition)
    [Range(2, 6)] public int finalSmoothPasses = 3; // extra smoothing passes after normalization for ultra-smooth terrain

    [Header("Compatibility (read-only where possible)")]
    [Range(0.001f, 0.2f)] public float sandThreshold = 0.02f; // kept for other scripts; auto-derived from beachWidth

    [Header("Terrain Layers")]
    public TerrainLayer sandLayer;
    public TerrainLayer grassLayer1;
    public TerrainLayer grassLayer2;
    public TerrainLayer grassLayer3;

    [Header("Grass Probabilities (0-1)")]
    [Range(0f, 1f)] public float grass1Prob = 0.4f;
    [Range(0f, 1f)] public float grass2Prob = 0.4f;
    [Range(0f, 1f)] public float grass3Prob = 0.2f;
    [Range(0.01f, 4f)] public float grassNoiseScale = 1.2f; // higher = more variation tiles

    // TerrainGenerator now handles only terrain; resources (trees/rocks/props) are spawned by MapResourcesGenerator

    [Header("Terrain Details (Painted Grass)")]
    public bool paintTerrainDetails = true;
    public bool autoCreateDetailPrototypes = true;
    public Texture2D[] grassDetailTextures; // optional: auto-create from 2D textures
    [Range(64, 2048)] public int detailResolution = 512;
    [Range(8, 128)] public int detailResolutionPerPatch = 32;
    [Range(0f, 1f)] public float detailGrassMinWeight = 0.85f; // require strong grass splat presence
    [Range(0f, 1f)] public float detailSandMaxWeight = 0.03f;  // exclude sand influence
    [Range(0f, 60f)] public float detailMaxSlope = 28f;
    [Range(0.1f, 8f)] public float detailDensity = 1.0f; // global density multiplier
    [Range(0.1f, 8f)] public float grassyAreaGrassSpawnDensity = 1.5f; // extra slider for visible grass amount on grassy areas

    public Terrain terrain;

    [System.Serializable]
    public struct TerrainMetrics
    {
        public int seed;
        public int width; public int height;
        public int landBlocks; public int sandBlocks; public int totalBlocks;
        public float landPercent; public float sandPercent;
        public float meanHeight; public float stdDevHeight;
        public float mountainFraction; public float meanHeightMountain; public float meanHeightNonMountain;
        public float radialCorrelation; // correlation between height and radial island core factor
        public float persistence; public float lacunarity; public float hurstExponent;
        public double msHeightCompute; public double msSplatCompute; public double msObjects; public double msDetails; public double msApplyHeight; public double msApplySplat; public double totalMs;
    }

    public TerrainMetrics lastMetrics;

    [Header("Debug")]
    public bool logTerrainMetrics = true;

    void Start()
    {
        if (generateOnStart)
        {
            // in multiplayer, only master client generates terrain on start.
            // joiners must wait for the seed sync RPC from MapResourcesGenerator.
            if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
            {
                Debug.Log("[TerrainGenerator] Joiner: waiting for terrain seed from master client.");
                return;
            }

            GenerateTerrain();
        }
    }

    /// <summary>
    /// Called by MapResourcesGenerator RPC on joiners to set the master's seed and generate identical terrain.
    /// </summary>
    public void GenerateWithSeed(int syncedSeed)
    {
        if (IsGenerationInProgress)
        {
            Debug.Log("[TerrainGenerator] Seed sync ignored because terrain generation is already running.");
            return;
        }

        if (IsGenerationComplete && hasGenerated && seed == syncedSeed && LastGenerationId > 0)
        {
            Debug.Log($"[TerrainGenerator] Seed sync ignored; terrain already generated for seed {syncedSeed}.");
            return;
        }

        seed = syncedSeed;
        hasGenerated = false;
        allowManualRegenerate = true;
        Debug.Log($"[TerrainGenerator] Joiner generating terrain with master seed {syncedSeed}");
        GenerateTerrain();
    }

    // No runtime coroutines; MapResourcesGenerator manages resource placement

    // Force regeneration by resetting hasGenerated flag
    public void ForceRegenerate()
    {
        hasGenerated = false;
        allowManualRegenerate = true;
        GenerateTerrain();
    }

    /// <summary>
    /// finds the correct terrain in the active scene.
    /// resolves stale/wrong inspector references that could cause grass to paint on the wrong terrain.
    /// </summary>
    private void ResolveActiveTerrain()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (terrain != null && terrain.gameObject.scene == activeScene && terrain.terrainData != null)
            return;

        // try same gameobject first
        terrain = GetComponent<Terrain>();
        if (terrain != null && terrain.terrainData != null)
            return;

        // search active scene for any terrain
        var terrains = FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < terrains.Length; i++)
        {
            if (terrains[i] != null && terrains[i].gameObject.scene == activeScene && terrains[i].terrainData != null)
            {
                terrain = terrains[i];
                Debug.Log($"[TerrainGenerator] resolved terrain to '{terrain.name}' in active scene via scene search");
                return;
            }
        }

        // last resort: any terrain with data
        if (terrains.Length > 0 && terrains[0] != null)
        {
            terrain = terrains[0];
            Debug.LogWarning($"[TerrainGenerator] no terrain found in active scene, falling back to '{terrain.name}'");
        }
    }

    // generate terrain heightmap and paint textures and grass details
    public void GenerateTerrain()
    {
        if (IsGenerationInProgress)
        {
            Debug.Log("terrain generator: generation skipped (already in progress)");
            return;
        }

        if (hasGenerated && !allowManualRegenerate)
        {
            Debug.Log("terrain generator: generation skipped (already generated and manual regenerate disabled)");
            return;
        }

        IsGenerationInProgress = true;
        IsGenerationComplete = false;
        int generationId = ++generationSequence;

        ResolveActiveTerrain();
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        var sw = new System.Diagnostics.Stopwatch();
        int useSeed = seed != 0 ? seed : Random.Range(int.MinValue, int.MaxValue);
        if (seed == 0) seed = useSeed; // lock in for future runs
        System.Random prng = new System.Random(useSeed);
        float offX = prng.Next(-100000, 100000);
        float offY = prng.Next(-100000, 100000);

        float[,] finalHeightMap = new float[width, height];
        int landBlocks = 0;
        int sandBlocks = 0;
        // Height statistics (Formulas 9.0/9.1) computed over the final noise-space height field.
        // We compute these after smoothing so they reflect the evaluated terrain signal.
        double sumH = 0.0;
        double sumH2 = 0.0;
        // Precompute simple octaves based on roughness
        int baseOctaves = Mathf.RoundToInt(Mathf.Lerp(2, 5, Mathf.Clamp01(roughness)));
        float persistence = Mathf.Lerp(0.35f, 0.6f, Mathf.Clamp01(roughness));
        float lacunarity = Mathf.Lerp(1.8f, 2.6f, Mathf.Clamp01(roughness));

        // Falloff produces an islandy shape (circular mask softened towards the edges)
        // derive falloff sharpness from centerBias to avoid harsh edges
        float aFall = Mathf.Lerp(0.9f, 1.8f, Mathf.Clamp01(centerBias));
        float bFall = Mathf.Lerp(1.4f, 2.6f, Mathf.Clamp01(centerBias));
        float[,] falloffMap = GenerateFalloffMap(width, height, aFall, bFall, circularFalloff, islandSize);

        sw.Restart();
        // First pass: create a continuous height field
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = (x + offX) / islandScale;
                float ny = (y + offY) / islandScale;
                float baseNoise = FractalNoise(nx, ny, baseOctaves, persistence, lacunarity);
                // Apply falloff more aggressively - scale it by centerBias but ensure it affects edges
                float rawFall = falloffMap[x, y];
                // Remap falloff to start later and be steeper (edge softening)
                float remappedFall = Mathf.Max(0f, (rawFall - falloffStartDistance) / (1f - falloffStartDistance));
                remappedFall = Mathf.Pow(remappedFall, 1f / Mathf.Max(0.1f, falloffSteepness));
                // Island-size mask is baked into falloffMap; enforce it independent of centerBias
                float islandMask = rawFall; // 0 center .. 1 edges, already scaled by islandSize
                islandMask = Mathf.Clamp01(islandMask);
                // Combine: hard island boundary + optional center bias softening
                float fall = Mathf.Max(islandMask, remappedFall * Mathf.Clamp01(centerBias));
                float h = Mathf.Clamp01(baseNoise - fall);
                // pre-smooth the primary height signal to avoid harsh terraces
                h = Mathf.SmoothStep(0f, 1f, h);
                // carve inland cavities/holes
                if (cavityAmount > 0f)
                {
                    float cx = (x + offX * 0.21f) / (islandScale * Mathf.Max(0.05f, cavityScale));
                    float cy = (y + offY * 0.21f) / (islandScale * Mathf.Max(0.05f, cavityScale));
                    float cav = Mathf.PerlinNoise(cx, cy);
                    float cavity = Mathf.Max(0f, (cav - 0.5f) * 2f) * cavityAmount;
                    h = Mathf.Clamp01(h - cavity * (1f - fall)); // less cavities near border
                }
                finalHeightMap[x, y] = h;
            }
        }
        sw.Stop();
        double msCompute = sw.Elapsed.TotalMilliseconds;

        // Optional smoothing to improve transitions
        if (smoothIterations > 0)
        {
            SmoothHeightsInPlace(finalHeightMap, width, height, Mathf.Clamp(smoothIterations, 3, 10));
            // relax edges where falloff is high to guarantee a wide shoreline ramp
            EdgeRelax(finalHeightMap, falloffMap, width, height, 0.45f, 5, Mathf.Clamp01(smoothnessStrength * 0.95f));
        }

        // Compute height summary stats over the smoothed noise-space map.
        // meanHeight = sum(h)/(W*H), stdDevHeight = sqrt(max(0, sum(h^2)/(W*H) - mean^2))
        sumH = 0.0;
        sumH2 = 0.0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double hv = finalHeightMap[x, y];
                sumH += hv;
                sumH2 += hv * hv;
            }
        }

        // Auto-threshold to hit target land percent; compute classification threshold in height space
        float thresh = FindHeightThreshold(finalHeightMap, targetLandPercent);
        // beach (sand) sits just below land threshold using beachWidth
        sandThreshold = Mathf.Clamp(thresh, 0.01f, 0.3f);
        float grassStart = sandThreshold + beachWidth;

        TerrainData terrainData = terrain.terrainData;
        terrainData.heightmapResolution = width + 1;
        terrainData.alphamapResolution = width;
        terrainData.size = new Vector3(width, heightMultiplier, height);

        // assign terrain layers only if they are non-null to avoid engine issues
        var layerList = new System.Collections.Generic.List<TerrainLayer>(4);
        if (sandLayer != null) layerList.Add(sandLayer);
        if (grassLayer1 != null) layerList.Add(grassLayer1);
        if (grassLayer2 != null) layerList.Add(grassLayer2);
        if (grassLayer3 != null) layerList.Add(grassLayer3);
        if (layerList.Count > 0)
        {
            terrainData.terrainLayers = layerList.ToArray();
        }
        else
        {
            Debug.LogWarning("terrain generator: no TerrainLayers assigned; skipping layer assignment");
        }

        // Compose final physical heights: continuous C1 mapping around shoreline to avoid steps
        float[,] physical = new float[width, height];
        sw.Restart();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float h = finalHeightMap[x, y];
                // base elevation curve
                float baseCurve = Mathf.SmoothStep(0f, 1f, h);
                float edgeFactor = Mathf.Clamp01(falloffMap[x, y]);

                // signed distance to shoreline in noise space
                float d = h - sandThreshold;
                float heightNorm;
                if (d < 0f)
                {
                    // underwater: smooth step from deep to shoreline with C1 continuity
                    float t = Mathf.SmoothStep(-beachWidth, 0f, d);
                    heightNorm = Mathf.Lerp(0f, underwaterDepth, t);
                }
                else if (d < beachWidth)
                {
                    // beach band: smooth ramp
                    float t = Mathf.SmoothStep(0f, 1f, d / Mathf.Max(1e-5f, beachWidth));
                    heightNorm = Mathf.Lerp(shorelineLow, shorelineHigh, t);

                    // dunes near edge
                    float duneNoise = Mathf.PerlinNoise((x + offX * 0.5f) / (islandScale * Mathf.Max(0.05f, duneScale)), (y + offY * 0.5f) / (islandScale * Mathf.Max(0.05f, duneScale)));
                    float dune = (duneNoise - 0.5f) * 2f;
                    heightNorm += dune * (duneAmplitude * Mathf.SmoothStep(0f, 1f, edgeFactor));

                    float drop = shoreDrop * Mathf.SmoothStep(0.6f, 1.0f, edgeFactor);
                    heightNorm = Mathf.Max(0f, heightNorm - drop * (1f - t));
                }
                else
                {
                    // inland: continuous extension from beach using base curve, plus subtle highland modulation
                    float t = Mathf.InverseLerp(grassStart, 1f, h);
                    t = t * t * (3f - 2f * t); // smootherstep
                    float baseHeight = Mathf.Lerp(inlandBase, 1.0f, t * baseCurve);

                    // add micro-terrain variation only in highlands so it doesn't affect beaches
                    float hx = (x + offX * 0.63f) / (islandScale * 0.42f);
                    float hy = (y + offY * 0.63f) / (islandScale * 0.42f);
                    float highland = FractalNoise(hx, hy, 4, 0.53f, 2.15f) - 0.5f; // [-0.5,0.5]
                    float amp = Mathf.Lerp(0.02f, 0.12f, Mathf.Clamp01(roughness));
                    heightNorm = baseHeight + highland * amp * t;

                    // Add mountains and hills on top of grass area - can exceed 1.0
                    if (mountainIntensity > 0f && t > 0.2f) // only on inland areas
                    {
                        // Base mountain layer - larger features
                        float mountainX = (x + offX * 0.31f) / (Mathf.Max(20f, mountainScale));
                        float mountainY = (y + offY * 0.31f) / (Mathf.Max(20f, mountainScale));
                        float mountainNoise = FractalNoise(mountainX, mountainY, 5, 0.6f, 2.3f);

                        // Second layer - hills with smaller scale for detail
                        float hillX = (x + offX * 0.47f) / (Mathf.Max(10f, hillScale));
                        float hillY = (y + offY * 0.47f) / (Mathf.Max(10f, hillScale));
                        float hillNoise = FractalNoise(hillX, hillY, 4, 0.55f, 2.2f);

                        // Create mountain peaks that vary by height - stronger in higher areas
                        float mountainMask = Mathf.SmoothStep(0.3f, 0.85f, t); // stronger mountains in high areas

                        // Base mountain elevation
                        float mountainElevation = (mountainNoise - 0.3f) * mountainMask; // bias toward positive
                        mountainElevation = Mathf.Max(0f, mountainElevation); // only add, don't subtract
                        mountainElevation *= mountainIntensity * maxMountainHeight;

                        // Layer hills on top - scaled relative to mountain height for natural variation
                        float hillMask = mountainMask * Mathf.Clamp01(hillIntensity);
                        float hillElevation = (hillNoise - 0.2f) * hillMask; // hills can add detail
                        hillElevation = Mathf.Max(0f, hillElevation);
                        // Hills scale with mountain elevation - create peaks and valleys
                        hillElevation *= maxMountainHeight * 0.4f * hillIntensity;

                        // Combine both layers
                        heightNorm += mountainElevation + hillElevation;
                    }
                }

                // Enforce a world-edge ramp using the falloff map (prevents cliffs regardless of noise)
                // fall=0 center, 1 at edges. Blend targetEdge curve as fall approaches 1
                float fall = edgeFactor;

                // Remap falloff to start earlier and transition more smoothly to underwater
                float remappedFall = Mathf.Max(0f, (fall - falloffStartDistance) / (1f - falloffStartDistance));
                remappedFall = Mathf.Pow(remappedFall, 1f / Mathf.Max(0.1f, falloffSteepness));

                // Start blending earlier for smoother transition
                float rampT = Mathf.SmoothStep(falloffStartDistance * 0.8f, 0.99f, fall);

                // Calculate target height at this falloff distance
                // Near center (fall < falloffStartDistance): keep original height
                // At edges (fall = 1): go to underwaterDepth
                // In between: smooth transition from shorelineLow to underwaterDepth
                float edgeProgression = Mathf.SmoothStep(0f, 1f, remappedFall);
                float intermediateHeight = Mathf.Lerp(shorelineLow, underwaterDepth, edgeProgression * 0.7f); // transition zone
                float targetEdge = Mathf.Lerp(intermediateHeight, underwaterDepth, edgeProgression * edgeProgression); // stronger pull at edges

                // Blend more strongly as we approach edges
                float blendStrength = Mathf.SmoothStep(0f, 1f, remappedFall);
                heightNorm = Mathf.Lerp(heightNorm, targetEdge, blendStrength * rampT);

                // Allow heights above 1.0 for mountains, but clamp negative values
                physical[x, y] = Mathf.Max(0f, heightNorm);
            }
        }
        // extra shoreline softening pass: blur only low-to-mid elevations to avoid sharp cliffs
        ShorelineSoftenInPlace(physical, width, height, grassStart, 4);
        AdaptiveLowlandSmoothing(physical, width, height, Mathf.Max(shorelineHigh + 0.02f, grassStart + 0.04f), 10, Mathf.Clamp01(smoothnessStrength));
        // ensure continuity exactly around elevation control thresholds regardless of values
        IsocontourSmoothing(physical, finalHeightMap, width, height, sandThreshold, grassStart, beachWidth * 1.35f, 4);

        // Normalize heights to [0,1] for Unity terrain, preserving mountain variations
        float maxHeight = 0f;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                maxHeight = Mathf.Max(maxHeight, physical[x, y]);

        if (maxHeight > 0.0001f)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    physical[x, y] = physical[x, y] / maxHeight;
        }

        // Final smoothing pass after normalization to eliminate any stepped artifacts
        // This is critical for smooth terrain, especially with high heightMultiplier
        if (finalSmoothPasses > 0)
        {
            SmoothHeightsInPlace(physical, width, height, finalSmoothPasses);
            // Additional gentle pass for ultra-smooth transitions
            AdaptiveLowlandSmoothing(physical, width, height, 1.0f, 2, 0.4f); // smooth everywhere gently
        }

        terrainData.SetHeights(0, 0, physical);
        // Ensure height changes are fully applied before sampling for object placement
        if (terrain != null) terrain.Flush();
        sw.Stop();
        double msApplyHeight = sw.Elapsed.TotalMilliseconds;
        int totalBlocks = width * height;

        // Calculate sand threshold in normalized physical height space (before splatmap generation)
        float sandPhysicalThreshold = sandHeightThreshold;
        float grassPhysicalStart = sandPhysicalThreshold + (beachWidth * 0.5f); // transition zone

        sw.Restart();
        float[,,] splatmap = new float[width, width, 4];

        for (int y = 0; y < width; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Use physical height for texture assignment (not noise value)
                float physicalH = physical[x, y];
                float[] weights = new float[4];

                if (physicalH < sandPhysicalThreshold)
                {
                    weights[0] = 1f; // sand below threshold
                }
                else if (physicalH < grassPhysicalStart)
                {
                    // smooth beach->grass transition
                    float t = Mathf.InverseLerp(sandPhysicalThreshold, grassPhysicalStart, physicalH);
                    float edgeFactor = Mathf.Clamp01(falloffMap[x, y]);
                    float elevatedSandBoost = 0.15f * Mathf.SmoothStep(0.6f, 1.0f, edgeFactor);
                    weights[0] = Mathf.Clamp01((1f - t) * 0.95f + elevatedSandBoost);

                    // stochastic grass selector
                    float gnx = (x + offX * 0.71f) / (islandScale * Mathf.Max(0.15f, grassNoiseScale));
                    float gny = (y + offY * 0.71f) / (islandScale * Mathf.Max(0.15f, grassNoiseScale));
                    float r = FractalNoise(gnx, gny, 2, 0.55f, 2.15f);
                    float p1 = Mathf.Clamp01(grass1Prob);
                    float p2 = Mathf.Clamp01(grass2Prob);
                    float p3 = Mathf.Clamp01(grass3Prob);
                    float sumP = Mathf.Max(0.0001f, p1 + p2 + p3);
                    r *= sumP;
                    if (r < p1) weights[1] = t; else if (r < p1 + p2) weights[2] = t; else weights[3] = t;
                }
                else
                {
                    // interior: random grass with bias, plus a small fraction of rocky patches
                    float mx = (x + offX * 0.37f) / (islandScale * 0.6f);
                    float my = (y + offY * 0.37f) / (islandScale * 0.6f);
                    float mNoise = FractalNoise(mx, my, baseOctaves + 1, persistence, lacunarity);
                    // rocky zones appear sparsely and mainly in mid/highlands
                    bool rocky = (mNoise > 0.82f) && (physicalH > grassPhysicalStart + 0.05f);
                    float p1 = Mathf.Clamp01(grass1Prob);
                    float p2 = Mathf.Clamp01(grass2Prob);
                    float p3 = Mathf.Clamp01(grass3Prob * 0.6f);
                    float sumP = Mathf.Max(0.0001f, p1 + p2 + p3);
                    if (rocky) { weights[3] = 1f; }
                    else
                    {
                        float gnx = (x + offX * 0.71f) / (islandScale * Mathf.Max(0.15f, grassNoiseScale));
                        float gny = (y + offY * 0.71f) / (islandScale * Mathf.Max(0.15f, grassNoiseScale));
                        float r = FractalNoise(gnx, gny, 2, 0.55f, 2.15f) * sumP;
                        if (r < p1) weights[1] = 1f; else if (r < p1 + p2) weights[2] = 1f; else weights[3] = 1f;
                    }
                }
                float total = weights[0] + weights[1] + weights[2] + weights[3];
                for (int i = 0; i < 4; i++)
                    splatmap[x, y, i] = weights[i] / total;
            }
        }
        sw.Stop(); double msSplatCompute = sw.Elapsed.TotalMilliseconds;
        sw.Restart(); terrainData.SetAlphamaps(0, 0, splatmap); sw.Stop(); double msApplySplat = sw.Elapsed.TotalMilliseconds;
        // Ensure splat changes are applied too before object placement
        if (terrain != null) terrain.Flush();
        double msObjects = 0.0;

        // Paint Unity terrain details (ONLY on grass areas)
        if (paintTerrainDetails)
        {
            // Optionally auto-create DetailPrototypes from provided 2D textures
            if (autoCreateDetailPrototypes && grassDetailTextures != null && grassDetailTextures.Length > 0)
            {
                var list = new System.Collections.Generic.List<DetailPrototype>();
                foreach (var tex in grassDetailTextures)
                {
                    if (tex == null) continue;
                    var dp = new DetailPrototype
                    {
                        usePrototypeMesh = false,
                        renderMode = DetailRenderMode.GrassBillboard,
                        prototypeTexture = tex,
                        healthyColor = new Color(0.85f, 0.95f, 0.85f, 1f),
                        dryColor = new Color(0.75f, 0.7f, 0.55f, 1f),
                        minWidth = 0.6f,
                        maxWidth = 1.2f,
                        minHeight = 0.8f,
                        maxHeight = 1.6f,
                        noiseSpread = 0.4f,
                    };
                    list.Add(dp);
                }
                if (list.Count > 0)
                {
                    terrainData.detailPrototypes = list.ToArray();
                    int res = Mathf.Clamp(detailResolution, 64, 1024); // cap to avoid huge allocations
                    int perPatch = Mathf.Clamp(detailResolutionPerPatch, 8, 128);
                    terrainData.SetDetailResolution(res, perPatch);
                }
            }

            var prototypes = terrainData.detailPrototypes;
            if (prototypes != null && prototypes.Length > 0)
            {
                int dRes = Mathf.Clamp(terrainData.detailResolution, 64, 1024);
                int perPatchMax = Mathf.Clamp(detailResolutionPerPatch, 8, 128);
                int layerCount = prototypes.Length;
                // Build per-layer int maps
                var detailLayers = new int[layerCount][,];
                for (int l = 0; l < layerCount; l++) detailLayers[l] = new int[dRes, dRes];

                for (int y = 0; y < dRes; y++)
                {
                    float ny = y / Mathf.Max(1f, (float)(dRes - 1));
                    for (int x = 0; x < dRes; x++)
                    {
                        float nx = x / Mathf.Max(1f, (float)(dRes - 1));
                        // map to alpha/splat resolution
                        int sW = splatmap.GetLength(0); int sH = splatmap.GetLength(1);
                        int sx = Mathf.Clamp(Mathf.RoundToInt(nx * (sW - 1)), 0, sW - 1);
                        int sy = Mathf.Clamp(Mathf.RoundToInt(ny * (sH - 1)), 0, sH - 1);

                        float sandW = splatmap[sx, sy, 0];
                        float g1 = splatmap[sx, sy, 1]; float g2 = splatmap[sx, sy, 2]; float g3 = splatmap[sx, sy, 3];
                        float grassW = g1 + g2 + g3;

                        // Old logic required grassW >= detailGrassMinWeight, which could leave visible
                        // holes in areas visually painted as grass due to small amounts of sand/rock.
                        // New logic: treat any cell where a grass layer is the dominant splat as "grass area"
                        // and then modulate density by grassW and the existing sliders.
                        int dominantIndex = 0;
                        float dominantWeight = sandW;
                        if (g1 > dominantWeight) { dominantWeight = g1; dominantIndex = 1; }
                        if (g2 > dominantWeight) { dominantWeight = g2; dominantIndex = 2; }
                        if (g3 > dominantWeight) { dominantWeight = g3; dominantIndex = 3; }

                        bool dominantIsGrass = dominantIndex != 0 && grassW > 0.05f;
                        bool mostlySand = sandW > detailSandMaxWeight && sandW >= grassW;

                        if (dominantIsGrass && !mostlySand)
                        {
                            // Height and slope checks
                            float hNorm = terrainData.GetInterpolatedHeight(nx, ny) / Mathf.Max(0.0001f, terrainData.size.y);
                            if (hNorm >= sandHeightThreshold + 0.02f)
                            {
                                float slope = terrainData.GetSteepness(nx, ny);
                                if (slope <= detailMaxSlope)
                                {
                                    // Distribute across all prototypes; scale by per-patch capacity and grass weight.
                                    // Ensure every grass texel gets at least 1 blade to avoid visible "bald" patches.
                                    for (int l = 0; l < layerCount; l++)
                                    {
                                        // Terrain detail values are ints; range ~0..perPatchMax
                                        float jitter = Mathf.PerlinNoise(nx * 64f + l * 7.1f, ny * 64f + l * 3.3f); // 0..1
                                        float grassFactor = Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(grassW)); // keep density high on any grass texel
                                        float densityFactor = detailDensity * grassyAreaGrassSpawnDensity;
                                        float baseVal = perPatchMax * densityFactor * (0.4f + 0.6f * jitter) * grassFactor;
                                        int val = Mathf.Max(1, Mathf.RoundToInt(baseVal));
                                        detailLayers[l][y, x] = Mathf.Clamp(val, 0, perPatchMax);
                                    }
                                }
                            }
                        }
                    }
                }

                // Apply layers
                for (int l = 0; l < layerCount; l++)
                {
                    terrainData.SetDetailLayer(0, 0, l, detailLayers[l]);
                }
                // flush detail changes so they're visible immediately
                if (terrain != null) terrain.Flush();
            }
            else
            {
                Debug.Log("terrain generator: no DetailPrototypes found; skipping detail painting.");
            }
        }

        // disable terrain tree/rock prototypes and instances to avoid collider creation and native crashes
        // this clears any existing prototypes/instances and skips placement entirely
        sw.Restart();
        terrainData.treePrototypes = System.Array.Empty<TreePrototype>();
        terrainData.treeInstances = System.Array.Empty<TreeInstance>();
        sw.Stop(); msObjects = sw.Elapsed.TotalMilliseconds;

        // Resource placement is handled by MapResourcesGenerator after terrain is ready


        swTotal.Stop();
        // compute summary stats
        int totalBlocks2 = width * height;
        // recompute land/sand percent for metrics using the tuned threshold and final heights
        int landCount = 0, sandCount = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float h = finalHeightMap[x, y];
                if (h < sandThreshold) sandCount++; else landCount++;
            }
        }
        float landPercent = (float)landCount / Mathf.Max(1, totalBlocks2);
        float sandPercent = (float)sandCount / Mathf.Max(1, totalBlocks2);
        double mean = sumH / totalBlocks2;
        double variance = Mathf.Max(0f, (float)(sumH2 / totalBlocks2 - mean * mean));
        float stdDev = Mathf.Sqrt((float)variance);
        float mountainFraction = mountainAmount;
        float meanMount = 0f;
        float meanNonMount = 0f;

        // radial correlation between height and island-core factor r (1 at center -> 0 at border)
        double sumX = 0, sumY = 0, sumXX = 0, sumYY = 0, sumXY = 0; int n = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx2 = x / (float)width * 2 - 1;
                float ny2 = y / (float)height * 2 - 1;
                float rCore = 1f - Mathf.Max(Mathf.Abs(nx2), Mathf.Abs(ny2)); // 1 center .. 0 edges
                float hv = finalHeightMap[x, y];
                double X = rCore; double Y = hv;
                sumX += X; sumY += Y; sumXX += X * X; sumYY += Y * Y; sumXY += X * Y; n++;
            }
        }
        double denom = System.Math.Sqrt(System.Math.Max(1e-6, (n * sumXX - sumX * sumX) * (n * sumYY - sumY * sumY)));
        float corr = denom > 0 ? (float)((n * sumXY - sumX * sumY) / denom) : 0f;

        // Hurst exponent from current fBM parameters (Formula 10.0 in TESTING.md)
        float hurst = 0f;
        if (persistence > 0f && lacunarity > 0f && persistence != 1f && lacunarity != 1f)
        {
            hurst = -Mathf.Log(persistence) / Mathf.Log(lacunarity);
        }

        lastMetrics = new TerrainMetrics
        {
            seed = seed,
            width = width,
            height = height,
            landBlocks = landBlocks,
            sandBlocks = sandBlocks,
            totalBlocks = totalBlocks,
            landPercent = landPercent,
            sandPercent = sandPercent,
            meanHeight = (float)mean,
            stdDevHeight = stdDev,
            mountainFraction = mountainFraction,
            meanHeightMountain = meanMount,
            meanHeightNonMountain = meanNonMount,
            radialCorrelation = corr,
            persistence = persistence,
            lacunarity = lacunarity,
            hurstExponent = hurst,
            msHeightCompute = msCompute,
            msSplatCompute = msSplatCompute,
            msObjects = msObjects,
            msApplyHeight = msApplyHeight,
            msApplySplat = msApplySplat,
            totalMs = swTotal.Elapsed.TotalMilliseconds
        };

        if (logTerrainMetrics)
        {
            Debug.Log($"terrain metrics: total {lastMetrics.totalMs:F1}ms | height {lastMetrics.msHeightCompute:F1}ms + apply {lastMetrics.msApplyHeight:F1}ms | splat {lastMetrics.msSplatCompute:F1}ms + apply {lastMetrics.msApplySplat:F1}ms | objects {lastMetrics.msObjects:F1}ms | details {lastMetrics.msDetails:F1}ms | land {lastMetrics.landPercent:P1} sand {lastMetrics.sandPercent:P1} corr {lastMetrics.radialCorrelation:F2}");
        }

        LastGenerationId = generationId;
        LastCompletedGenerationId = generationId;
        IsGenerationInProgress = false;
        IsGenerationComplete = true;
        OnGenerationCompleted?.Invoke(generationId);
        hasGenerated = true;
    }

    // Helper: compute threshold for desired land % over [0..1] height map
    float FindHeightThreshold(float[,] heights, float targetLand)
    {
        // sample histogram to choose threshold
        int buckets = 256;
        int[] hist = new int[buckets];
        int total = heights.GetLength(0) * heights.GetLength(1);
        for (int y = 0; y < heights.GetLength(1); y++)
            for (int x = 0; x < heights.GetLength(0); x++)
                hist[Mathf.Clamp(Mathf.FloorToInt(heights[x, y] * (buckets - 1)), 0, buckets - 1)]++;
        int landTarget = Mathf.RoundToInt(total * targetLand);
        int running = 0;
        for (int i = buckets - 1; i >= 0; i--)
        {
            running += hist[i];
            if (running >= landTarget)
            {
                return (float)i / (buckets - 1);
            }
        }
        return 0.5f;
    }

    // In-place separable 5-tap Gaussian-like smoothing to soften steps/cliffs
    void SmoothHeightsInPlace(float[,] map, int w, int h, int iterations)
    {
        float[,] temp = new float[w, h];
        float smoothWeight = Mathf.Clamp01(smoothnessStrength);

        for (int it = 0; it < iterations; it++)
        {
            // horizontal pass (kernel [1,4,6,4,1]/16) - wider kernel for better smoothing
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float a2 = map[Mathf.Max(0, x - 2), y];
                    float a1 = map[Mathf.Max(0, x - 1), y];
                    float b0 = map[x, y];
                    float c1 = map[Mathf.Min(w - 1, x + 1), y];
                    float c2 = map[Mathf.Min(w - 1, x + 2), y];
                    float smoothed = (a2 + 4f * a1 + 6f * b0 + 4f * c1 + c2) / 16f;

                    // Use full smoothWeight for stronger smoothing, especially on later iterations
                    float weight = it < iterations - 1 ? smoothWeight : Mathf.Min(1f, smoothWeight * 1.1f);
                    temp[x, y] = Mathf.Lerp(b0, smoothed, weight);
                }
            }
            // vertical pass back into map (same kernel)
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float a2 = temp[x, Mathf.Max(0, y - 2)];
                    float a1 = temp[x, Mathf.Max(0, y - 1)];
                    float b0 = temp[x, y];
                    float c1 = temp[x, Mathf.Min(h - 1, y + 1)];
                    float c2 = temp[x, Mathf.Min(h - 1, y + 2)];
                    float smoothed = (a2 + 4f * a1 + 6f * b0 + 4f * c1 + c2) / 16f;

                    // Use full smoothWeight for stronger smoothing, especially on later iterations
                    float weight = it < iterations - 1 ? smoothWeight : Mathf.Min(1f, smoothWeight * 1.1f);
                    map[x, y] = Mathf.Lerp(b0, smoothed, weight);
                }
            }
        }
    }

    // Light blur focused on shoreline and lowlands to remove cliffy look
    void ShorelineSoftenInPlace(float[,] map, int w, int h, float grassStart, int iterations)
    {
        if (iterations <= 0) return;
        float[,] tmp = new float[w, h];
        for (int it = 0; it < iterations; it++)
        {
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    float v = map[x, y];
                    // only soften shoreline/lowlands
                    if (v <= grassStart + 0.05f)
                    {
                        float sum = 0f;
                        sum += map[x - 1, y]; sum += map[x + 1, y];
                        sum += map[x, y - 1]; sum += map[x, y + 1];
                        sum += v * 4f;
                        tmp[x, y] = sum / 8f; // gentle
                    }
                    else tmp[x, y] = v;
                }
            }
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                    map[x, y] = tmp[x, y];
        }
    }

    // Diffusion-like smoothing focused on lowlands and shoreline with adjustable strength
    void AdaptiveLowlandSmoothing(float[,] map, int w, int h, float maxHeight, int iterations, float strength)
    {
        strength = Mathf.Clamp01(strength);
        float[,] tmp = new float[w, h];
        for (int it = 0; it < iterations; it++)
        {
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    float v = map[x, y];
                    if (v <= maxHeight)
                    {
                        float avg = (map[x - 1, y] + map[x + 1, y] + map[x, y - 1] + map[x, y + 1] + v) / 5f;
                        tmp[x, y] = Mathf.Lerp(v, avg, strength);
                    }
                    else tmp[x, y] = v;
                }
            }
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                    map[x, y] = tmp[x, y];
        }
    }

    // Smoothing constrained around specific isocontours of the source noise (pre-threshold map)
    void IsocontourSmoothing(float[,] outHeights, float[,] srcNoise, int w, int h, float t1, float t2, float band, int iterations)
    {
        if (iterations <= 0) return;
        band = Mathf.Max(0.001f, band);
        float[,] tmp = new float[w, h];
        for (int it = 0; it < iterations; it++)
        {
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    float v = outHeights[x, y];
                    float s = srcNoise[x, y];
                    float d1 = Mathf.Abs(s - t1);
                    float d2 = Mathf.Abs(s - t2);
                    float near = Mathf.Min(d1, d2);
                    if (near <= band)
                    {
                        float avg = (outHeights[x - 1, y] + outHeights[x + 1, y] + outHeights[x, y - 1] + outHeights[x, y + 1] + v) / 5f;
                        float wgt = Mathf.SmoothStep(band, 0f, near); // stronger right at the contour
                        tmp[x, y] = Mathf.Lerp(v, avg, 0.6f * wgt);
                    }
                    else tmp[x, y] = v;
                }
            }
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                    outHeights[x, y] = tmp[x, y];
        }
    }

    // Specifically relax steep edges near the border defined by falloff
    void EdgeRelax(float[,] map, float[,] fall, int w, int h, float threshold, int iterations, float strength)
    {
        strength = Mathf.Clamp01(strength);
        float[,] tmp = new float[w, h];
        for (int it = 0; it < iterations; it++)
        {
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    if (fall[x, y] >= threshold)
                    {
                        float v = map[x, y];
                        float avg = (map[x - 1, y] + map[x + 1, y] + map[x, y - 1] + map[x, y + 1] + v) / 5f;
                        tmp[x, y] = Mathf.Lerp(v, avg, strength);
                    }
                    else tmp[x, y] = map[x, y];
                }
            }
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                    map[x, y] = tmp[x, y];
        }
    }

    // fractal noise (fBm) == richer terrain details
    float FractalNoise(float x, float y, int octaves, float persistence, float lacunarity)
    {
        float total = 0f;
        float frequency = 1f;
        float amplitude = 1f;
        float maxValue = 0f;
        for (int i = 0; i < octaves; i++)
        {
            total += Mathf.PerlinNoise(x * frequency, y * frequency) * amplitude;
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }
        return total / maxValue;
    }

    // generates a falloff map to create island shapes (circular with trimmed corners)
    float[,] GenerateFalloffMap(int width, int height, float a, float b, float circularity, float islandSizeParam)
    {
        float[,] map = new float[width, height];
        circularity = Mathf.Clamp01(circularity);
        float innerRadius = Mathf.Clamp01(islandSizeParam); // larger value = larger island footprint

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = x / (float)width * 2 - 1;
                float ny = y / (float)height * 2 - 1;

                // Calculate both square (Chebyshev) and circular (Euclidean) distances
                float squareDist = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny)); // creates square/diamond
                float circularDist = Mathf.Sqrt(nx * nx + ny * ny) / Mathf.Sqrt(2f); // normalized circular

                // Blend between square and circular based on circularity parameter
                // Higher circularity = more circular, trims corners
                float value = Mathf.Lerp(squareDist, circularDist, circularity);

                // Apply an island footprint scale that does NOT affect the noise detail.
                // We remap distance so that everything inside 'innerRadius' is treated as 0 falloff,
                // and then ramp to 1 as we approach the border. This simply shrinks/expands the island.
                float distRemap = 0f;
                if (innerRadius <= 0.0001f) distRemap = value;
                else distRemap = Mathf.Clamp01((value - innerRadius) / Mathf.Max(1e-5f, 1f - innerRadius));

                // Apply falloff curve - make it more aggressive near edges
                float falloff = Mathf.Pow(distRemap, a) / (Mathf.Pow(distRemap, a) + Mathf.Pow(b - b * distRemap, a));

                // Boost falloff near the edges to ensure proper trimming
                if (falloff > 0.5f)
                {
                    float edgeBoost = Mathf.SmoothStep(0.5f, 1f, falloff);
                    falloff = Mathf.Lerp(falloff, 1f, edgeBoost * 0.3f); // boost edges by up to 30%
                }

                map[x, y] = Mathf.Clamp01(falloff);
            }
        }
        return map;
    }

    // No object placement here; MapResourcesGenerator handles all props/resources
}