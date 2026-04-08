using UnityEngine;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

// Drop this on any GameObject in the scene to print the latest TerrainGenerator and PerlinEnemySpawner metrics at runtime.
public class TerrainMetricsReporter : MonoBehaviour
{
    public TerrainGenerator generator;
    public PerlinEnemySpawner spawner;
    public bool reportOnStart = true;
    public bool reportOnKey = true;
    public KeyCode key = KeyCode.M;

    [Header("sampling for simulations")]
    [Tooltip("resolution used for analytical sampling (lower = faster)")] public int sampleResolution = 128;
    [Tooltip("assumed player base speed when estimating chase/charge success")] public float assumedPlayerSpeed = 6f;
    [Tooltip("time horizon in seconds for a charge to complete before being considered a failure")] public float chargeTimeHorizon = 3.0f;

    void Start()
    {
        if (generator == null) generator = FindFirstObjectByType<TerrainGenerator>();
        if (spawner == null) spawner = FindFirstObjectByType<PerlinEnemySpawner>();
        if (reportOnStart) Report();
    }

    void Update()
    {
        if (reportOnKey && Input.GetKeyDown(key)) Report();
    }

    public void Report()
    {
        var sb = new StringBuilder();
        if (generator != null)
        {
            var m = generator.lastMetrics;
            // derived terrain simulation (analytical sampling at lower res)
            var sim = SimulateTerrain(generator, sampleResolution);
            sb.AppendLine(
                $"Terrain Metrics => Seed {m.seed}, Size {m.width}x{m.height}, Land {m.landPercent:P1}, Sand {m.sandPercent:P1}, StdDev {m.stdDevHeight:F3}, MountainFrac {m.mountainFraction:P1}, RadialCorr {m.radialCorrelation:F2}"
            );
            sb.AppendLine(
                $"  Timing ms: height {m.msHeightCompute:F1} + apply {m.msApplyHeight:F1} | splat {m.msSplatCompute:F1} + apply {m.msApplySplat:F1} | objects {m.msObjects:F1} | details {m.msDetails:F1} | total {m.totalMs:F1}"
            );
            sb.AppendLine(
                $"  Simulation => Land≈{sim.landPercent:P1} | RoughnessIdx≈{sim.roughnessIndex:F3} | FractalDim≈{sim.fractalDimension:F2} | MountainFrac≈{sim.mountainFraction:P1} | RadialCorr≈{sim.radialCorrelation:F2}"
            );
        }
        else
        {
            sb.AppendLine("TerrainMetricsReporter: no TerrainGenerator found");
        }

        if (spawner != null)
        {
            var sim = SimulateSpawner(generator, spawner, sampleResolution);
            float attemptsPerPlacement = spawner.lastPlaced > 0 ? (float)spawner.lastAttempts / spawner.lastPlaced : 0f;
            sb.AppendLine(
                $"Spawner Metrics => Desired {spawner.desiredCount}, Placed {spawner.lastPlaced}, Attempts {spawner.lastAttempts} (≈{attemptsPerPlacement:F2}/place), Scale {spawner.perlinScale:F3}, Threshold {spawner.perlinThreshold:F2}, Spacing {spawner.minSpawnSpacing:F1}m"
            );
            sb.AppendLine(
                $"  Simulation => p(perlin)≈{sim.pPerlin:F2} * p(height)≈{sim.pHeight:F2} * p(slope)≈{sim.pSlope:F2} => p(accept)≈{sim.pAccept:F2} | capacity≈{sim.capacity:F0} | expectedPlaced≈{sim.expectedPlaced:F0} | estAttempts/place≈{(sim.pAccept>0?1f/sim.pAccept:0f):F2}"
            );
        }
        else
        {
            sb.AppendLine("TerrainMetricsReporter: no PerlinEnemySpawner found");
        }

        // AI behavior tree metrics
        // var aiSim = SimulateAIMetrics();
        // if (aiSim.count > 0)
        // {
        //     sb.AppendLine(
        //         $"AI Metrics ({aiSim.count} enemies) => BT Tick≈{aiSim.btTickRate:F1}/s | TargetScanInterval≈{aiSim.scanInterval:F2}s | AvgMoveSpeed≈{aiSim.avgMoveSpeed:F1} m/s | AttackCD≈{aiSim.avgAttackCD:F2}s | BusyWindow≈{aiSim.avgBusyWindow:F2}s | ChargeSuccess≈{aiSim.chargeSuccessRate:P0}"
        //     );
        // }
        // else
        // {
        //     sb.AppendLine("AI Metrics => no EnemyAIController instances found");
        // }

        Debug.Log(sb.ToString());
    }

    // --- Terrain simulation formulas ---
    struct TerrainSim
    {
        public float landPercent; public float mountainFraction; public float roughnessIndex; public float fractalDimension; public float radialCorrelation;
    }
    TerrainSim SimulateTerrain(TerrainGenerator g, int res)
    {
        if (g == null) return default;
        int w = Mathf.Max(32, res); int h = w;
        // generate falloff similar to generator defaults
        float[,] fall = GenFalloff(w, h, 1.35f, 2.0f);
        // height sampling (low res) matching simplified generator
        System.Random prng = new System.Random(12345);
        float offsetX = prng.Next(-100000, 100000);
        float offsetY = prng.Next(-100000, 100000);
        int baseOctaves = Mathf.RoundToInt(Mathf.Lerp(2, 5, Mathf.Clamp01(g.roughness)));
        float persistence = Mathf.Lerp(0.35f, 0.6f, Mathf.Clamp01(g.roughness));
        float lacunarity = Mathf.Lerp(1.8f, 2.6f, Mathf.Clamp01(g.roughness));

        double land=0, mountain=0; double sum=0, sum2=0; double sumX=0,sumY=0,sumXX=0,sumYY=0,sumXY=0; int n=0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // base noise
                float nx = (x + offsetX) / g.islandScale;
                float ny = (y + offsetY) / g.islandScale;
                float val = FractalNoise(nx, ny, baseOctaves, persistence, lacunarity);
                float hval = Mathf.Clamp01(val - fall[x, y]);
                hval = Mathf.SmoothStep(0f, 1f, hval);

                bool isLand = hval >= g.sandThreshold;
                if (isLand) land++;
                // crude mountain fraction proxy using a second noise sample
                float mx = (x + offsetX * 0.37f) / (g.islandScale * 0.6f);
                float my = (y + offsetY * 0.37f) / (g.islandScale * 0.6f);
                float mNoise = FractalNoise(mx, my, baseOctaves + 1, persistence, lacunarity);
                if (mNoise > 1f - g.mountainAmount) mountain++;

                sum += hval; sum2 += hval * hval;
                float nx2 = x / (float)w * 2 - 1; float ny2 = y / (float)h * 2 - 1; float rCore = 1f - Mathf.Max(Mathf.Abs(nx2), Mathf.Abs(ny2));
                double X = rCore; double Y = hval;
                sumX += X; sumY += Y; sumXX += X*X; sumYY += Y*Y; sumXY += X*Y; n++;
            }
        }
        float landP = (float)(land / System.Math.Max(1, n));
        float mountP = (float)(mountain / System.Math.Max(1, n));
        double mean = sum / System.Math.Max(1, n);
        double var = System.Math.Max(0.0, (sum2 / System.Math.Max(1, n)) - mean * mean);
        float roughIdx = Mathf.Sqrt((float)var);
        // theoretical fractal dimension approximation for fBm: D = 3 - H, H = log(persistence)/log(lacunarity)
        float H = Mathf.Log(Mathf.Lerp(0.35f, 0.6f, Mathf.Clamp01(g.roughness))) / Mathf.Log(Mathf.Lerp(1.8f, 2.6f, Mathf.Clamp01(g.roughness)));
        float fractalD = 3f - H;
        double denom = System.Math.Sqrt(System.Math.Max(1e-6, (n*sumXX - sumX*sumX) * (n*sumYY - sumY*sumY)));
        float corr = denom > 0 ? (float)((n*sumXY - sumX*sumY) / denom) : 0f;
        return new TerrainSim { landPercent = landP, mountainFraction = mountP, roughnessIndex = roughIdx, fractalDimension = fractalD, radialCorrelation = corr };
    }

    // --- Spawner simulation formulas ---
    struct SpawnerSim { public float pPerlin, pHeight, pSlope, pAccept; public float capacity; public float expectedPlaced; }
    SpawnerSim SimulateSpawner(TerrainGenerator g, PerlinEnemySpawner s, int res)
    {
        if (s == null) return default;
        // perlin acceptance ~ fraction above threshold; sample grid using same scale as spawner
        int w = Mathf.Max(32, res), h = w; int samples = w*h; int passPerlin = 0, passHeight = 0, passSlope = 0, passAll = 0;
        // build a simple height map for slope/height checks if terrain generator present
        float[,] heights = null;
        if (g != null)
        {
            heights = new float[w, h];
            var fall = GenFalloff(w, h, 1.35f, 2.0f);
            System.Random prng = new System.Random(4567);
            float offX = prng.Next(-100000, 100000);
            float offY = prng.Next(-100000, 100000);
            int baseOctaves = Mathf.RoundToInt(Mathf.Lerp(2, 5, Mathf.Clamp01(g.roughness)));
            float persistence = Mathf.Lerp(0.35f, 0.6f, Mathf.Clamp01(g.roughness));
            float lacunarity = Mathf.Lerp(1.8f, 2.6f, Mathf.Clamp01(g.roughness));
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float nx = (x + offX) / g.islandScale;
                float ny = (y + offY) / g.islandScale;
                float val = FractalNoise(nx, ny, baseOctaves, persistence, lacunarity);
                float hval = Mathf.Clamp01(val - fall[x, y]);
                hval = Mathf.SmoothStep(0f, 1f, hval);
                heights[x, y] = hval;
            }
        }

        // approximate slope from height map gradients (in degrees)
        System.Random prng2 = new System.Random(8901);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            // perlin at spawner scale
            float vx = (x / (float)w * (s.terrain != null ? s.terrain.terrainData.size.x : w)) * s.perlinScale + (float)prng2.NextDouble()*2f;
            float vz = (y / (float)h * (s.terrain != null ? s.terrain.terrainData.size.z : h)) * s.perlinScale + (float)prng2.NextDouble()*2f;
            float p = Mathf.PerlinNoise(vx, vz);
            bool okPerlin = p >= s.perlinThreshold; if (okPerlin) passPerlin++;

            bool okHeight = true; bool okSlope = true;
            if (heights != null)
            {
                float hval = heights[x, y];
                okHeight = hval >= Mathf.Max(s.minNormalizedHeight, g != null ? g.sandThreshold + 0.02f : s.minNormalizedHeight);
                if (x>0 && y>0 && x<w-1 && y<h-1)
                {
                    float dx = (heights[x+1,y] - heights[x-1,y]) * 0.5f;
                    float dy = (heights[x,y+1] - heights[x,y-1]) * 0.5f;
                    float slopeRad = Mathf.Atan(Mathf.Sqrt(dx*dx + dy*dy));
                    float slopeDeg = slopeRad * Mathf.Rad2Deg;
                    okSlope = slopeDeg <= s.maxSlope;
                }
            }
            if (okHeight) passHeight++; if (okSlope) passSlope++; if (okPerlin && okHeight && okSlope) passAll++;
        }

        float pPerlin = passPerlin / (float)samples;
        float pHeight = passHeight / (float)samples;
        float pSlope = passSlope / (float)samples;
        float pAccept = passAll / (float)samples;

        // capacity estimate based on hard-disk packing approximation with hex efficiency
        float r = Mathf.Max(0.1f, s.minSpawnSpacing);
        // use hex packing efficiency; in world-space units we don't know exact width here, but we can provide relative capacity (scale with terrain size if available)
        float terrainArea = 1f;
        if (s.terrain != null) terrainArea = s.terrain.terrainData.size.x * s.terrain.terrainData.size.z;
        float capacity = (terrainArea) / (Mathf.PI * r * r) * 0.82f; // 0.82 for realistic packing below hex optimum
        float expectedPlaced = Mathf.Min(s.desiredCount, capacity);
        return new SpawnerSim { pPerlin = pPerlin, pHeight = pHeight, pSlope = pSlope, pAccept = pAccept, capacity = capacity, expectedPlaced = expectedPlaced };
    }

//     // --- AI simulation formulas ---
//     struct AISim { public int count; public float btTickRate; public float scanInterval; public float avgMoveSpeed; public float avgAttackCD; public float avgBusyWindow; public float chargeSuccessRate; }
//     AISim SimulateAIMetrics()
//     {
// #if UNITY_2023_1_OR_NEWER
//     var enemies = FindObjectsByType<EnemyAIController>(FindObjectsSortMode.None);
// #else
//     var enemies = FindObjectsOfType<EnemyAIController>();
// #endif
//         if (enemies == null || enemies.Length == 0) return default;
//         float fps = 1f / Mathf.Max(0.0001f, Time.smoothDeltaTime);
//         float sumMove = 0f, sumCD = 0f, sumBusy = 0f; int n=0; float scanInterval = 0.5f; int scanFound=0;
//         foreach (var e in enemies)
//         {
//             if (e == null) continue;
//             var stats = e.stats;
//             if (stats != null)
//             {
//                 sumMove += stats.moveSpeed; sumCD += stats.attackCooldown; n++;
//             }
//             // reflect private targetScanInterval
//             var f = typeof(EnemyAIController).GetField("targetScanInterval", BindingFlags.NonPublic | BindingFlags.Instance);
//             if (f != null)
//             {
//                 scanInterval = (float)f.GetValue(e); scanFound++;
//             }
//             // busy window heuristic: animation lock + small recovery buffer
//             var fldLock = typeof(EnemyAIController).GetField("attackMoveLock", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
//             float lockVal = fldLock != null ? (float)fldLock.GetValue(e) : 0.35f;
//             sumBusy += lockVal + 0.25f; // + recovery buffer
//         }
//         float avgMove = n>0 ? sumMove/n : 0f; float avgCD = n>0 ? sumCD/n : 0f; float busy = enemies.Length>0 ? sumBusy/enemies.Length : 0.6f;
//         // charge success approximation: player runs away at assumed speed; success if enemy can close distance within horizon
//         // assume uniform initial distance d in [attackRange, detectionRange]
//         float success = 0f; int m=0;
//         foreach (var e in enemies)
//         {
//             if (e == null || e.stats == null) continue;
//             float vE = Mathf.Max(0.01f, e.stats.moveSpeed);
//             float vP = Mathf.Max(0.01f, assumedPlayerSpeed);
//             float vDelta = vE - vP;
//             float a = e.stats.attackRange; float D = e.stats.detectionRange;
//             if (D <= a) { success += 1f; m++; continue; }
//             if (vDelta <= 0)
//             {
//                 // can only succeed if player doesn't flee; assume 35% stationary/opportunistic
//                 success += 0.35f; m++; continue;
//             }
//             float tCatchMax = chargeTimeHorizon;
//             // success for d where (d - a) / vDelta <= tCatchMax  => d <= a + vDelta * tCatchMax
//             float dLimit = a + vDelta * tCatchMax;
//             float p = Mathf.Clamp01((dLimit - a) / Mathf.Max(0.0001f, (D - a)));
//             success += p; m++;
//         }
//         float charge = m>0 ? success/m : 0.5f;
//         return new AISim { count = enemies.Length, btTickRate = fps, scanInterval = scanFound>0?scanInterval:0.5f, avgMoveSpeed = avgMove, avgAttackCD = avgCD, avgBusyWindow = busy, chargeSuccessRate = charge };
//     }

    // helpers: minimal copies of generator internals (no side effects)
    float[,] GenFalloff(int width, int height, float a, float b)
    {
        float[,] map = new float[width, height];
        a = Mathf.Max(0.5f, a); b = Mathf.Max(0.5f, b);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float nx = x / (float)width * 2 - 1f;
            float ny = y / (float)height * 2 - 1f;
            float value = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny));
            float falloff = Mathf.Pow(value, a) / (Mathf.Pow(value, a) + Mathf.Pow(b - b * value, a));
            map[x, y] = falloff;
        }
        return map;
    }

    float FractalNoise(float x, float y, int octaves, float persistence, float lacunarity)
    {
        float total = 0f, frequency = 1f, amplitude = 1f, maxValue = 0f;
        for (int i = 0; i < octaves; i++)
        {
            total += Mathf.PerlinNoise(x * frequency, y * frequency) * amplitude;
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }
        return maxValue > 0f ? total / maxValue : 0f;
    }
}
