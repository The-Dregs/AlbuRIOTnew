#if false
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.IO;
using UnityEditor;
using UnityEngine;
public class AlbuRIOTTestingWindow : EditorWindow
{
    [MenuItem("AlbuRIOT/Testing Window")]
    public static void Open()
    {
        var w = GetWindow<AlbuRIOTTestingWindow>();
        w.titleContent = new GUIContent("AlbuRIOT Testing");
        w.Show();
    }
    enum Tab { Terrain, Spawner, BehaviorTree }
    private Tab _tab = Tab.Terrain;
    // Shared
    private int _runs = 10;
    private int _baseSeed = 12345;
    private bool _useSequentialSeeds = true;
    private string _seedListCsv = "12345,12346,12347";
    private Vector2 _scroll;
    // Terrain
    private TerrainGenerator _terrainGenerator;
    private bool _terrainUsePlayModeValues = true;
    private readonly List<TerrainRunResult> _terrainResults = new List<TerrainRunResult>();
    // Resource scatter (MapResourcesGenerator)
    private MapResourcesGenerator _resourcesGenerator;
    private bool _resourcesUsePlayModeRuns = true;
    private int _resourcesSampleResolution = 128;
    private readonly List<ResourceRunResult> _resourceResults = new List<ResourceRunResult>();
    private bool _resourceRunInProgress = false;
    private bool _resourceSpeedUpDuringRun = true;
    private int _prevItemsPerFrame;
    private int _prevMaxAttemptsPerFrame;
    private Queue<int> _resourceSeedQueue;
    private int _resourceCurrentSeed;
    private double _resourceSeedStartTime;
    private bool _resourceMutedTerrainLogs;
    private bool _resourcePrevTerrainLogSetting;
    // Behavior Tree
    private EnemyBehaviorMetricsRecorder _aiRecorder;
    private float _aiObserveSeconds = 10f;
    private float _aiSampleInterval = 0.1f;
    private readonly List<AIRunResult> _aiResults = new List<AIRunResult>();
    private struct TerrainRunResult
    {
        public int seed;
        public float landPercent; // Formula 17
        public float sandPercent;
        public float meanHeight; // Formula 19
        public float stdDevHeight; // Formula 18
        public float radialCorrelation; // Formula 21
        public double totalMs;
        public double heightMs;
        public double splatMs;
        public float persistence;
        public float lacunarity;
        public float hurstExponent; // Formula 20
        public string interpretation;
    }
    private struct ResourceRunResult
    {
        public int seed;
        public string label;
        public int desired;
        public int placed;
        public int attempts;
        public float attemptsPerPlacement; // Formula 28
        public float pPerlin; // Formula 23
        public float pHeight; // Formula 24
        public float pSlope;  // Formula 25
        public float pAccept; // Formula 26
        public float expectedPlaced; // Formula 27
        public float efficiency; // Formula 30
        public float estAttemptsPerPlacement; // Formula 29
        public float heightRejectionRate; // Formula 32
        public float slopeRejectionRate; // Formula 33
        public float spacingRejectionRate; // Formula 34
        public float constraintEffectiveness; // Formula 35
        public string interpretation;
    }
    private struct AIRunResult
    {
        public float elapsedSeconds;
        public float btTickRate; // Formula 36
        public int totalAIs;
        public int activeAIs;
        public float aiEfficiency; // Formula 38
        public float avgReactionLatency; // Formula 37
    }
    void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        EditorGUILayout.Space(6);
        _tab = (Tab)GUILayout.Toolbar((int)_tab, new[] { "Terrain", "Spawner", "Behavior Tree" });
        EditorGUILayout.Space(8);
        DrawSharedControls();
        EditorGUILayout.Space(8);
        switch (_tab)
        {
            case Tab.Terrain:
                DrawTerrainTab();
                break;
            case Tab.Spawner:
                DrawResourcesTab();
                break;
            case Tab.BehaviorTree:
                DrawBehaviorTreeTab();
                break;
        }
        EditorGUILayout.EndScrollView();
    }
    private void DrawSharedControls()
    {
        EditorGUILayout.LabelField("Run Settings", EditorStyles.boldLabel);
        _runs = Mathf.Clamp(EditorGUILayout.IntField("How many runs", _runs), 1, 500);
        _useSequentialSeeds = EditorGUILayout.ToggleLeft("Use sequential seeds", _useSequentialSeeds);
        if (_useSequentialSeeds)
        {
            _baseSeed = EditorGUILayout.IntField("Base seed", _baseSeed);
        }
        else
        {
            _seedListCsv = EditorGUILayout.TextField("Seed list (CSV)", _seedListCsv);
        }
        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Play Mode", GUILayout.Width(70));
            if (EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("Running (recommended for accurate runtime metrics).", MessageType.Info);
            else
                EditorGUILayout.HelpBox("Not running. Some tests require Play Mode.", MessageType.Warning);
        }
    }
    private List<int> GetSeedsForRuns()
    {
        if (_useSequentialSeeds)
        {
            var list = new List<int>(_runs);
            for (int i = 0; i < _runs; i++) list.Add(unchecked(_baseSeed + i));
            return list;
        }
        var seeds = new List<int>();
        var parts = _seedListCsv.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (int.TryParse(p.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int s))
                seeds.Add(s);
        }
        if (seeds.Count == 0)
            seeds.Add(_baseSeed);
        // If user provided fewer seeds than runs, cycle.
        var expanded = new List<int>(_runs);
        for (int i = 0; i < _runs; i++) expanded.Add(seeds[i % seeds.Count]);
        return expanded;
    }
    // ---------------- Terrain ----------------
    private void DrawTerrainTab()
    {
        EditorGUILayout.LabelField("Terrain (Formulas 8.0, 9.0–9.1, 10.0, 10.1)", EditorStyles.boldLabel);
        _terrainGenerator = (TerrainGenerator)EditorGUILayout.ObjectField("TerrainGenerator", _terrainGenerator, typeof(TerrainGenerator), true);
        _terrainUsePlayModeValues = EditorGUILayout.ToggleLeft("Use real TerrainGenerator metrics (Play Mode)", _terrainUsePlayModeValues);
        // Show reference values from the inspector for comparison
        if (_terrainGenerator != null)
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Inspector reference", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField($"Target land %", $"{_terrainGenerator.targetLandPercent:P1}");
                EditorGUILayout.LabelField($"Roughness", _terrainGenerator.roughness.ToString("F3"));
                EditorGUILayout.LabelField($"Island size", _terrainGenerator.islandSize.ToString("F3"));
                EditorGUILayout.LabelField($"Center bias", _terrainGenerator.centerBias.ToString("F3"));
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Find in scene", GUILayout.Width(120)))
            {
                _terrainGenerator = FindFirstObjectByType<TerrainGenerator>();
            }
            if (GUILayout.Button("Run terrain tests"))
            {
                RunTerrainTests();
            }
            if (GUILayout.Button("Export terrain report"))
            {
                ExportTerrainReport();
            }
            if (GUILayout.Button("Export terrain CSV"))
            {
                ExportTerrainCsv();
            }
            if (GUILayout.Button("Clear results", GUILayout.Width(120)))
            {
                _terrainResults.Clear();
            }
        }
        EditorGUILayout.Space(6);
        DrawTerrainResults();
    }
    private void RunTerrainTests()
    {
        _terrainResults.Clear();
        if (_terrainGenerator == null)
        {
            Debug.LogError("[AlbuRIOTTestingWindow] TerrainGenerator is not assigned.");
            return;
        }
        if (_terrainUsePlayModeValues && !EditorApplication.isPlaying)
        {
            Debug.LogWarning("[AlbuRIOTTestingWindow] Terrain test set to Play Mode metrics, but Play Mode is not running.");
        }
        var seeds = GetSeedsForRuns();
        foreach (int seed in seeds)
        {
            _terrainGenerator.seed = seed;
            _terrainGenerator.ForceRegenerate();
            var m = _terrainGenerator.lastMetrics;
            // Use the exact persistence/lacunarity/H stored in TerrainMetrics when available
            float persistence = m.persistence != 0f ? m.persistence : Mathf.Lerp(0.35f, 0.6f, Mathf.Clamp01(_terrainGenerator.roughness));
            float lacunarity = m.lacunarity != 0f ? m.lacunarity : Mathf.Lerp(1.8f, 2.6f, Mathf.Clamp01(_terrainGenerator.roughness));
            float H = m.hurstExponent;
            if (H == 0f && persistence > 0f && lacunarity > 0f && persistence != 1f && lacunarity != 1f)
            {
                H = -Mathf.Log(persistence) / Mathf.Log(lacunarity);
            }
            string interp = InterpretTerrainRun(m, persistence, lacunarity, H, _terrainGenerator);
            _terrainResults.Add(new TerrainRunResult
            {
                seed = seed,
                landPercent = m.landPercent,
                sandPercent = m.sandPercent,
                meanHeight = m.meanHeight,
                stdDevHeight = m.stdDevHeight,
                radialCorrelation = m.radialCorrelation,
                totalMs = m.totalMs,
                heightMs = m.msHeightCompute + m.msApplyHeight,
                splatMs = m.msSplatCompute + m.msApplySplat,
                persistence = persistence,
                lacunarity = lacunarity,
                hurstExponent = H,
                interpretation = interp
            });
        }
        Repaint();
    }
    private void DrawTerrainResults()
    {
        if (_terrainResults.Count == 0)
        {
            EditorGUILayout.HelpBox("No terrain results yet.", MessageType.Info);
            return;
        }
        var avgLand = _terrainResults.Average(r => r.landPercent);
        var avgStd = _terrainResults.Average(r => r.stdDevHeight);
        var avgCorr = _terrainResults.Average(r => r.radialCorrelation);
        var avgMs = _terrainResults.Average(r => r.totalMs);
        var avgH = _terrainResults.Average(r => r.hurstExponent);
        EditorGUILayout.LabelField($"Runs: {_terrainResults.Count} | Avg land {avgLand:P1} | Avg stdDev {avgStd:F3} | Avg corr {avgCorr:F2} | Avg H {avgH:F3} | Avg total {avgMs:F1} ms");
        using (new EditorGUILayout.VerticalScope("box"))
        {
            foreach (var r in _terrainResults.Take(50))
            {
                EditorGUILayout.LabelField(
                    $"Seed {r.seed} | land {r.landPercent:P1} sand {r.sandPercent:P1} | mean {r.meanHeight:F3} std {r.stdDevHeight:F3} | corr {r.radialCorrelation:F2} | H {r.hurstExponent:F3} | {r.totalMs:F1} ms"
                );
                if (!string.IsNullOrEmpty(r.interpretation))
                {
                    EditorGUILayout.LabelField("  → " + r.interpretation, EditorStyles.miniLabel);
                }
            }
            if (_terrainResults.Count > 50)
                EditorGUILayout.LabelField($"… ({_terrainResults.Count - 50} more)");
        }
    }
    private static string InterpretTerrainRun(TerrainGenerator.TerrainMetrics m, float persistence, float lacunarity, float hurst, TerrainGenerator generator)
    {
        var sb = new StringBuilder();
        // Land coverage vs target (Formula 17)
        float targetLand = generator != null ? generator.targetLandPercent : 0f;
        if (targetLand > 0f)
        {
            float diff = m.landPercent - targetLand;
            float diffAbs = Mathf.Abs(diff);
            if (diffAbs <= 0.05f)
                sb.Append($"Land coverage within ±5% of target ({m.landPercent:P1} vs {targetLand:P1}) [F17]. ");
            else if (diff < 0f)
                sb.Append($"Land below target by {diffAbs:P1} (more water than intended) [F17]. ");
            else
                sb.Append($"Land above target by {diffAbs:P1} (less water than intended) [F17]. ");
        }
        // Roughness (std dev) heuristic bands (Formula 18)
        float std = m.stdDevHeight;
        if (std < 0.07f)
            sb.Append("Terrain is very smooth (low roughness) [F18]. ");
        else if (std < 0.18f)
            sb.Append("Terrain roughness is in a moderate band [F18]. ");
        else
            sb.Append("Terrain is highly rough (check cliffs / noise) [F18]. ");
        // Radial correlation (Formula 20.1)
        float corr = m.radialCorrelation;
        if (corr >= 0.8f)
            sb.Append("Strong coastal gradient (height decreases cleanly toward edges) [F20.1]. ");
        else if (corr >= 0.6f)
            sb.Append("Moderate center-to-edge height correlation [F20.1]. ");
        else
            sb.Append("Weak height vs. core correlation (coastal falloff may be inconsistent) [F20.1]. ");
        // Hurst exponent validity (Formula 20)
        if (hurst > 0f && hurst < 1.2f)
            sb.Append($"Hurst exponent H={hurst:F3} in expected range for fBM terrain [F20]. ");
        else
            sb.Append($"Hurst exponent H={hurst:F3} is outside typical (0,1) band; review persistence/lacunarity [F20]. ");
        // Time budget comparison (10s target from TESTING.md)
        if (m.totalMs <= 10000.0)
            sb.Append($"Generation time {m.totalMs:F1} ms is within the 10 s target.");
        else
            sb.Append($"Generation time {m.totalMs:F1} ms exceeds the 10 s target.");
        return sb.ToString();
    }
    private void ExportTerrainReport()
    {
        if (_terrainResults.Count == 0)
        {
            Debug.LogWarning("[AlbuRIOTTestingWindow] No terrain results to export.");
            return;
        }
        string root = Application.dataPath;
        string dir = Path.Combine(root, "AlbuRIOT_Reports");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string fileName = $"terrain_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        string path = Path.Combine(dir, fileName);
        var sb = new StringBuilder();
        sb.AppendLine("AlbuRIOT Terrain Testing Report");
        sb.AppendLine($"Generated: {DateTime.Now}");
        if (_terrainGenerator != null)
        {
            sb.AppendLine("Inspector parameters:");
            sb.AppendLine($"  targetLandPercent = {_terrainGenerator.targetLandPercent:P1}");
            sb.AppendLine($"  roughness = {_terrainGenerator.roughness:F3}");
            sb.AppendLine($"  islandSize = {_terrainGenerator.islandSize:F3}");
            sb.AppendLine($"  centerBias = {_terrainGenerator.centerBias:F3}");
        }
        sb.AppendLine();
        float avgLand = (float)_terrainResults.Average(r => r.landPercent);
        float avgStd = (float)_terrainResults.Average(r => r.stdDevHeight);
        float avgCorr = (float)_terrainResults.Average(r => r.radialCorrelation);
        float avgH = (float)_terrainResults.Average(r => r.hurstExponent);
        double avgTotalMs = _terrainResults.Average(r => r.totalMs);
        sb.AppendLine($"Summary over {_terrainResults.Count} runs:");
        sb.AppendLine($"  Avg land = {avgLand:P1}");
        sb.AppendLine($"  Avg stdDevHeight = {avgStd:F3}");
        sb.AppendLine($"  Avg radialCorrelation = {avgCorr:F2}");
        sb.AppendLine($"  Avg Hurst exponent H = {avgH:F3}");
        sb.AppendLine($"  Avg total generation time = {avgTotalMs:F1} ms");
        sb.AppendLine();
        sb.AppendLine("Per-run details:");
        foreach (var r in _terrainResults)
        {
            sb.AppendLine(
                $"Seed {r.seed} | land {r.landPercent:P1} sand {r.sandPercent:P1} | mean {r.meanHeight:F3} std {r.stdDevHeight:F3} | corr {r.radialCorrelation:F2} | H {r.hurstExponent:F3} | {r.totalMs:F1} ms"
            );
            if (!string.IsNullOrEmpty(r.interpretation))
            {
                sb.AppendLine("  " + r.interpretation);
            }
        }
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[AlbuRIOTTestingWindow] Terrain report written to {path}");
    }
    private void ExportTerrainCsv()
    {
        if (_terrainResults.Count == 0)
        {
            Debug.LogWarning("[AlbuRIOTTestingWindow] No terrain results to export.");
            return;
        }
        string root = Application.dataPath;
        string dir = Path.Combine(root, "AlbuRIOT_Reports");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string fileName = $"terrain_runs_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string path = Path.Combine(dir, fileName);
        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string D(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.AppendLine("seed,landPercent,sandPercent,meanHeight,stdDevHeight,radialCorrelation,hurstExponent,totalMs");
        foreach (var r in _terrainResults)
        {
            sb.AppendLine(string.Join(",",
                r.seed.ToString(CultureInfo.InvariantCulture),
                F(r.landPercent),
                F(r.sandPercent),
                F(r.meanHeight),
                F(r.stdDevHeight),
                F(r.radialCorrelation),
                F(r.hurstExponent),
                D(r.totalMs)
            ));
        }
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[AlbuRIOTTestingWindow] Terrain CSV written to {path}");
    }
    // ---------------- Spawner ----------------
    private void DrawResourcesTab()
    {
        EditorGUILayout.LabelField("Map Resources (Perlin scatter) (Formulas 11.0–13.3)", EditorStyles.boldLabel);
        _resourcesGenerator = (MapResourcesGenerator)EditorGUILayout.ObjectField("MapResourcesGenerator", _resourcesGenerator, typeof(MapResourcesGenerator), true);
        _resourcesUsePlayModeRuns = EditorGUILayout.ToggleLeft("Execute real scatter (Play Mode)", _resourcesUsePlayModeRuns);
        _resourcesSampleResolution = Mathf.Clamp(EditorGUILayout.IntField("Sampling resolution", _resourcesSampleResolution), 32, 512);
        _resourceSpeedUpDuringRun = EditorGUILayout.ToggleLeft("Speed-up scatter during test", _resourceSpeedUpDuringRun);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Find in scene", GUILayout.Width(120)))
            {
                _resourcesGenerator = FindMapResourcesGeneratorIncludingDisabled();
            }
            if (GUILayout.Button("Run resource scatter tests"))
            {
                RunResourceTests();
            }
            if (GUILayout.Button("Export resource report"))
            {
                ExportResourceReport();
            }
            if (GUILayout.Button("Export resource CSV"))
            {
                ExportResourceCsv();
            }
            if (GUILayout.Button("Clear results", GUILayout.Width(120)))
            {
                _resourceResults.Clear();
            }
        }
        // Live status indicator (green light while running)
        if (_resourceRunInProgress)
        {
            double elapsed = EditorApplication.timeSinceStartup - _resourceSeedStartTime;
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.2f, 0.9f, 0.2f, 0.25f);
            EditorGUILayout.LabelField(
                $"Resource tests: RUNNING | seed {_resourceCurrentSeed} | elapsed {elapsed:F1}s",
                EditorStyles.boldLabel);
            GUI.backgroundColor = prevBg;
        }
        EditorGUILayout.Space(6);
        DrawResourceResults();
    }
    private void RunResourceTests()
    {
        if (_resourceRunInProgress)
        {
            Debug.LogWarning("[AlbuRIOTTestingWindow] Resource run already in progress.");
            return;
        }
        _resourceResults.Clear();
        if (_resourcesGenerator == null)
        {
            Debug.LogError("[AlbuRIOTTestingWindow] MapResourcesGenerator is not assigned.");
            return;
        }
        if (_resourcesUsePlayModeRuns && !EditorApplication.isPlaying)
        {
            Debug.LogWarning("[AlbuRIOTTestingWindow] Resource scatter tests require Play Mode.");
            return;
        }
        _resourceSeedQueue = new Queue<int>(GetSeedsForRuns());
        _resourceRunInProgress = true;
        EditorApplication.update += ResourceRunTick;
        StartNextResourceSeed();
    }
    private void StartNextResourceSeed()
    {
        if (_resourceSeedQueue == null || _resourceSeedQueue.Count == 0)
        {
            EndResourceRun();
            return;
        }
        _resourceCurrentSeed = _resourceSeedQueue.Dequeue();
        _resourceSeedStartTime = EditorApplication.timeSinceStartup;
        var terrainGen = FindFirstObjectByType<TerrainGenerator>();
        if (terrainGen != null)
        {
            // Mute terrain metric spam while resource tests run (terrain is regenerated per seed).
            if (!_resourceMutedTerrainLogs)
            {
                _resourceMutedTerrainLogs = true;
                _resourcePrevTerrainLogSetting = terrainGen.logTerrainMetrics;
                terrainGen.logTerrainMetrics = false;
            }
            terrainGen.seed = _resourceCurrentSeed;
            terrainGen.ForceRegenerate();
        }
        // Ensure generator enabled + prevent auto-disable for the duration of the run.
        _resourcesGenerator.DisableComponentAfterGeneration = false;
        _resourcesGenerator.enabled = true;
        if (_resourceSpeedUpDuringRun)
        {
            _prevItemsPerFrame = _resourcesGenerator.TestingItemsPerFrame;
            _prevMaxAttemptsPerFrame = _resourcesGenerator.TestingMaxAttemptsPerFrame;
            _resourcesGenerator.TestingItemsPerFrame = 200;
            _resourcesGenerator.TestingMaxAttemptsPerFrame = 5000;
        }
        _resourcesGenerator.GenerateAll();
    }
    private void ResourceRunTick()
    {
        if (!_resourceRunInProgress || _resourcesGenerator == null) return;
        // Wait until generator finishes its coroutine and marks completion for this terrain generation.
        double elapsed = EditorApplication.timeSinceStartup - _resourceSeedStartTime;
        if (elapsed > 60.0)
        {
            // Fallback: don't block the entire queue forever. Still export what we have.
            Debug.LogWarning($"[AlbuRIOTTestingWindow] Resource generation timed out for seed {_resourceCurrentSeed} (elapsed={elapsed:F1}s). Exporting best-effort metrics.");
            AppendResourceRowsForSeed(_resourceCurrentSeed);
            Repaint();
            StartNextResourceSeed();
            return;
        }
        bool generating = _resourcesGenerator.IsGenerating;
        if (generating) return;
        // Generation finished: export runtime metrics (attempts/rejections captured by MapResourcesGenerator).
        AppendResourceRowsForSeed(_resourceCurrentSeed);
        Repaint();
        StartNextResourceSeed();
    }
    private bool HaveAnyResourceMetricsWritten()
    {
        // Scatter metrics (herbs are an array)
        if (_resourcesGenerator.LastHerbScatterMetrics != null && _resourcesGenerator.LastHerbScatterMetrics.Count > 0)
            return true;
        // Other scatter metrics (structs): treat any non-zero placement or attempts as readiness.
        if (_resourcesGenerator.LastPlantScatterMetrics.placed > 0 || _resourcesGenerator.LastPlantScatterMetrics.attempts > 0) return true;
        if (_resourcesGenerator.LastFernScatterMetrics.placed > 0 || _resourcesGenerator.LastFernScatterMetrics.attempts > 0) return true;
        if (_resourcesGenerator.LastTreeScatterMetrics.placed > 0 || _resourcesGenerator.LastTreeScatterMetrics.attempts > 0) return true;
        if (_resourcesGenerator.LastSmallRockScatterMetrics.placed > 0 || _resourcesGenerator.LastSmallRockScatterMetrics.attempts > 0) return true;
        if (_resourcesGenerator.LastBigRockScatterMetrics.placed > 0 || _resourcesGenerator.LastBigRockScatterMetrics.attempts > 0) return true;
        // Placement/POI metrics
        var rem = _resourcesGenerator.LastRemnantPlacementMetrics;
        if (rem.remnantsPlaced > 0 || rem.remnantAttempts > 0) return true;
        var bs = _resourcesGenerator.LastBrokenShipPlacementMetrics;
        if (bs.placed > 0 || bs.attempts > 0) return true;
        var sp = _resourcesGenerator.LastSpawnPlacementMetrics;
        if (sp.spawnSlotsFinalCount > 0 || sp.entrancePlaced > 0 || sp.entranceCandidateTries > 0) return true;
        return false;
    }
    private void AppendResourceRowsForSeed(int seed)
    {
        // Herbs (Perlin-based)
        var herb = _resourcesGenerator.LastHerbScatterMetrics;
        if (herb != null)
        {
            for (int i = 0; i < herb.Count; i++)
                AddRowFromRuntime(seed, herb[i], isPerlin: true);
        }
        // Other scatters (not Perlin-thresholded; still report attempts/constraints)
        AddRowFromRuntime(seed, _resourcesGenerator.LastPlantScatterMetrics, isPerlin: false);
        AddRowFromRuntime(seed, _resourcesGenerator.LastFernScatterMetrics, isPerlin: false);
        AddRowFromRuntime(seed, _resourcesGenerator.LastTreeScatterMetrics, isPerlin: false);
        AddRowFromRuntime(seed, _resourcesGenerator.LastSmallRockScatterMetrics, isPerlin: false);
        AddRowFromRuntime(seed, _resourcesGenerator.LastBigRockScatterMetrics, isPerlin: false);
        // Camps placement
        AddRowFromRuntime(seed, _resourcesGenerator.LastCampPlacementMetrics, isPerlin: false);
        // Remnants/guards, broken ship, spawnpoints/entrance as placement rows
        AddPlacementRowFromRemnants(seed, _resourcesGenerator.LastRemnantPlacementMetrics);
        AddPlacementRowFromBrokenShip(seed, _resourcesGenerator.LastBrokenShipPlacementMetrics);
        AddPlacementRowFromSpawns(seed, _resourcesGenerator.LastSpawnPlacementMetrics);
        // Removed scene inventory snapshot: only record actual scatter attempts and placements for proper metrics.
    }
    private void AppendSceneInventoryRowsForSeed(int seed)
    {
        const string generatedRootName = "GeneratedEnvironment";
        var rootGO = GameObject.Find(generatedRootName);
        if (rootGO == null)
        {
            _resourceResults.Add(new ResourceRunResult
            {
                seed = seed,
                label = "SceneInventory",
                desired = 0,
                placed = 0,
                attempts = 0,
                attemptsPerPlacement = 0f,
                pPerlin = float.NaN,
                pHeight = float.NaN,
                pSlope = float.NaN,
                pAccept = float.NaN,
                expectedPlaced = 0f,
                efficiency = 0f,
                estAttemptsPerPlacement = 0f,
                heightRejectionRate = 0f,
                slopeRejectionRate = 0f,
                spacingRejectionRate = 0f,
                constraintEffectiveness = 0f,
                interpretation = "GeneratedEnvironment root not found in scene."
            });
            return;
        }
        Transform root = rootGO.transform;
        int CountByPath(string path)
        {
            var t = root.Find(path);
            return t != null ? t.childCount : 0;
        }
        int herbsCount = CountByPath("Plants/Herbs");
        int understoryCount = CountByPath("Plants/Understory");
        int treesCount = CountByPath("Trees");
        int fernsCount = CountByPath("Ferns");
        int rocksSmallCount = CountByPath("Rocks/_Small");
        int rocksBigCount = CountByPath("Rocks/_Big");
        int remnantsCount = CountByPath("Remnants");
        int remnantGuardsCount = CountByPath("RemnantGuards");
        int campsCount = CountByPath("Camps");
        int brokenShipPlaced = root.Find("Quest/BrokenShipQuest") != null ? 1 : 0;
        int entrancePlaced = root.Find("SpawnArea/SpawnEntrance") != null ? 1 : 0;
        // Spawn markers may exist either under SpawnMarkers, or under SpawnEntrance children.
        int spawnMarkerRootCount = CountByPath("SpawnMarkers");
        int entranceChildSpawnMarkers = 0;
        var entrance = root.Find("SpawnArea/SpawnEntrance");
        if (entrance != null)
        {
            var spawnMarkerTransforms = entrance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < spawnMarkerTransforms.Length; i++)
            {
                var tr = spawnMarkerTransforms[i];
                if (tr != null && tr.name != null && tr.name.StartsWith("SpawnMarker_", StringComparison.Ordinal))
                    entranceChildSpawnMarkers++;
            }
        }
        void AddInventoryRow(string label, int placed, string interp)
        {
            _resourceResults.Add(new ResourceRunResult
            {
                seed = seed,
                label = label,
                desired = 0,
                placed = placed,
                attempts = 0,
                attemptsPerPlacement = 0f,
                pPerlin = float.NaN,
                pHeight = float.NaN,
                pSlope = float.NaN,
                pAccept = float.NaN,
                expectedPlaced = 0f,
                efficiency = 0f,
                estAttemptsPerPlacement = 0f,
                heightRejectionRate = 0f,
                slopeRejectionRate = 0f,
                spacingRejectionRate = 0f,
                constraintEffectiveness = 0f,
                interpretation = interp
            });
        }
        AddInventoryRow("Scene/Herbs", herbsCount, "Plants/Herbs child count");
        AddInventoryRow("Scene/UnderstoryPlants", understoryCount, "Plants/Understory child count");
        AddInventoryRow("Scene/Trees", treesCount, "Trees child count");
        AddInventoryRow("Scene/Ferns", fernsCount, "Ferns child count");
        AddInventoryRow("Scene/Rocks_Small", rocksSmallCount, "Rocks/_Small child count");
        AddInventoryRow("Scene/Rocks_Big", rocksBigCount, "Rocks/_Big child count");
        AddInventoryRow("Scene/Remnants", remnantsCount, "Remnants child count");
        AddInventoryRow("Scene/RemnantGuards", remnantGuardsCount, "RemnantGuards child count");
        AddInventoryRow("Scene/Camps", campsCount, "Camps child count");
        AddInventoryRow("Scene/BrokenShipQuest", brokenShipPlaced, "Quest/BrokenShipQuest existence");
        AddInventoryRow("Scene/SpawnEntrance", entrancePlaced, "SpawnArea/SpawnEntrance existence");
        AddInventoryRow("Scene/SpawnMarkers_Root", spawnMarkerRootCount, "SpawnMarkers child count");
        AddInventoryRow("Scene/SpawnMarkers_EntranceChildren", entranceChildSpawnMarkers, "SpawnEntrance SpawnMarker_* children count");
    }
    private void AddPlacementRowFromRemnants(int seed, MapResourcesGenerator.RemnantPlacementMetrics m)
    {
        if (m.remnantsDesired <= 0 && m.guardsDesired <= 0) return;
        _resourceResults.Add(new ResourceRunResult
        {
            seed = seed,
            label = "Remnants",
            desired = m.remnantsDesired,
            placed = m.remnantsPlaced,
            attempts = m.remnantAttempts,
            attemptsPerPlacement = m.remnantsPlaced > 0 ? (float)m.remnantAttempts / m.remnantsPlaced : 0f,
            pPerlin = float.NaN,
            pHeight = float.NaN,
            pSlope = float.NaN,
            pAccept = float.NaN,
            expectedPlaced = m.remnantsDesired,
            efficiency = m.remnantsDesired > 0 ? (float)m.remnantsPlaced / m.remnantsDesired : 0f,
            estAttemptsPerPlacement = 0f,
            heightRejectionRate = 0f,
            slopeRejectionRate = 0f,
            spacingRejectionRate = 0f,
            constraintEffectiveness = m.remnantAttempts > 0 ? 1f - ((float)(m.remnantAttempts - m.remnantsPlaced) / m.remnantAttempts) : 0f,
            interpretation = $"Remnants {m.remnantsPlaced}/{m.remnantsDesired} | guards {m.guardsPlaced}/{m.guardsDesired} | clearanceRejected {m.clearanceRejected} spawnFailed {m.spawnFailed} guardFailed {m.guardSpawnFailed}"
        });
        // Emit a dedicated guard row so it maps 1:1 with the remnant guard prefab lists.
        if (m.guardsDesired > 0 || m.guardsPlaced > 0 || m.guardSpawnFailed > 0)
        {
            int guardAttempts = m.guardsPlaced + m.guardSpawnFailed;
            _resourceResults.Add(new ResourceRunResult
            {
                seed = seed,
                label = "RemnantGuards",
                desired = m.guardsDesired,
                placed = m.guardsPlaced,
                // One guard spawn attempt per desired guard slot; guardSpawnFailed counts the failures.
                attempts = guardAttempts,
                attemptsPerPlacement = m.guardsPlaced > 0 ? (float)guardAttempts / m.guardsPlaced : 0f,
                pPerlin = float.NaN,
                pHeight = float.NaN,
                pSlope = float.NaN,
                pAccept = float.NaN,
                expectedPlaced = m.guardsDesired,
                efficiency = m.guardsDesired > 0 ? (float)m.guardsPlaced / m.guardsDesired : 0f,
                estAttemptsPerPlacement = 0f,
                heightRejectionRate = 0f,
                slopeRejectionRate = 0f,
                spacingRejectionRate = 0f,
                constraintEffectiveness = guardAttempts > 0 ? 1f - ((float)(guardAttempts - m.guardsPlaced) / guardAttempts) : 0f,
                interpretation = $"Guards {m.guardsPlaced}/{m.guardsDesired} | guardSpawnFailed {m.guardSpawnFailed}"
            });
        }
    }
    private void AddPlacementRowFromBrokenShip(int seed, MapResourcesGenerator.BrokenShipPlacementMetrics m)
    {
        if (m.desired <= 0) return;
        _resourceResults.Add(new ResourceRunResult
        {
            seed = seed,
            label = "BrokenShipQuest",
            desired = m.desired,
            placed = m.placed,
            attempts = m.attempts,
            attemptsPerPlacement = m.placed > 0 ? (float)m.attempts / m.placed : 0f,
            pPerlin = float.NaN,
            pHeight = float.NaN,
            pSlope = float.NaN,
            pAccept = float.NaN,
            expectedPlaced = 1f,
            efficiency = m.desired > 0 ? (float)m.placed / m.desired : 0f,
            estAttemptsPerPlacement = 0f,
            heightRejectionRate = 0f,
            slopeRejectionRate = 0f,
            spacingRejectionRate = 0f,
            constraintEffectiveness = m.attempts > 0 ? 1f - ((float)(m.attempts - m.placed) / m.attempts) : 0f,
            interpretation = $"Placed={m.placed} clearanceRejected={m.clearanceRejected} spawnFailed={m.spawnFailed} minDistFromSpawns={m.minDistanceFromSpawns:F1}m"
        });
    }
    private void AddPlacementRowFromSpawns(int seed, MapResourcesGenerator.SpawnPlacementMetrics m)
    {
        // Emit an explicit entrance row to match the spawn entrance prefab slot in the inspector.
        if (m.entranceDesired > 0 || m.entrancePlaced > 0 || m.entranceCandidateTries > 0)
        {
            int entranceAttempts = Mathf.Max(0, m.entranceCandidateTries);
            _resourceResults.Add(new ResourceRunResult
            {
                seed = seed,
                label = "SpawnEntrance",
                desired = m.entranceDesired,
                placed = m.entrancePlaced,
                attempts = entranceAttempts,
                attemptsPerPlacement = m.entrancePlaced > 0 ? (float)entranceAttempts / m.entrancePlaced : 0f,
                pPerlin = float.NaN,
                pHeight = float.NaN,
                pSlope = float.NaN,
                pAccept = float.NaN,
                expectedPlaced = m.entranceDesired,
                efficiency = m.entranceDesired > 0 ? (float)m.entrancePlaced / m.entranceDesired : 0f,
                estAttemptsPerPlacement = 0f,
                heightRejectionRate = 0f,
                slopeRejectionRate = 0f,
                spacingRejectionRate = 0f,
                constraintEffectiveness = entranceAttempts > 0 ? 1f - ((float)(entranceAttempts - m.entrancePlaced) / entranceAttempts) : 0f,
                interpretation = $"Placed={m.entrancePlaced}/{m.entranceDesired} entranceCandidateTries={m.entranceCandidateTries} reusedExisting={m.entranceReusedExisting} | usingEntranceChildMarkers={m.usingEntranceChildMarkers}"
            });
        }
        if (m.spawnSlotsDesired <= 0) return;
        _resourceResults.Add(new ResourceRunResult
        {
            seed = seed,
            label = "SpawnSlots",
            desired = m.spawnSlotsDesired,
            placed = m.spawnSlotsFinalCount,
            attempts = 0,
            attemptsPerPlacement = 0f,
            pPerlin = float.NaN,
            pHeight = float.NaN,
            pSlope = float.NaN,
            pAccept = float.NaN,
            expectedPlaced = m.spawnSlotsDesired,
            efficiency = m.spawnSlotsDesired > 0 ? (float)m.spawnSlotsFinalCount / m.spawnSlotsDesired : 0f,
            estAttemptsPerPlacement = 0f,
            heightRejectionRate = 0f,
            slopeRejectionRate = 0f,
            spacingRejectionRate = 0f,
            constraintEffectiveness = 0f,
            interpretation = $"Candidates={m.spawnSlotsCandidateCount} Final={m.spawnSlotsFinalCount} usingEntranceChildMarkers={m.usingEntranceChildMarkers} | entrance {m.entrancePlaced}/{m.entranceDesired} reusedExisting={m.entranceReusedExisting} candidateTries={m.entranceCandidateTries}"
        });
    }
    private void AddRowFromRuntime(int seed, MapResourcesGenerator.ResourceScatterMetrics m, bool isPerlin)
    {
        if (m.desiredCount <= 0 && m.placed <= 0 && m.attempts <= 0) return;
        int placed = m.placed;
        int attempts = m.attempts;
        float attemptsPerPlacement = placed > 0 ? (float)attempts / placed : 0f;
        float pPerlin = float.NaN, pHeight = float.NaN, pSlope = float.NaN, pAccept = float.NaN;
        float expectedPlaced = 0f;
        float efficiency = 0f;
        float estAttemptsPerPlacement = 0f;
        if (isPerlin)
        {
            var probs = ResourceSampling.EstimateProbabilities(_resourcesGenerator, m, _resourcesSampleResolution);
            pPerlin = probs.pPerlin;
            pHeight = probs.pHeight;
            pSlope = probs.pSlope;
            pAccept = probs.pAccept;
            float epsilon = 1e-6f;
            estAttemptsPerPlacement = 1f / Mathf.Max(epsilon, pAccept);
            expectedPlaced = ResourceSampling.EstimateExpectedPlaced(_resourcesGenerator, m);
            efficiency = expectedPlaced > 0f ? placed / expectedPlaced : 0f;
        }
        float heightRejectionRate = float.IsNaN(pHeight) ? 0f : (1f - pHeight);
        float slopeRejectionRate = float.IsNaN(pSlope) ? 0f : (1f - pSlope);
        float spacingRejectionRate = m.validCandidates > 0 ? (float)m.spacingRejected / m.validCandidates : 0f;
        float constraintEffectiveness = attempts > 0 ? 1f - ((float)(attempts - placed) / attempts) : 0f;
        string interp = InterpretResourceRun(m, default, expectedPlaced, efficiency);
        _resourceResults.Add(new ResourceRunResult
        {
            seed = seed,
            label = m.label,
            desired = m.desiredCount,
            placed = placed,
            attempts = attempts,
            attemptsPerPlacement = attemptsPerPlacement,
            pPerlin = pPerlin,
            pHeight = pHeight,
            pSlope = pSlope,
            pAccept = pAccept,
            expectedPlaced = expectedPlaced,
            efficiency = efficiency,
            estAttemptsPerPlacement = estAttemptsPerPlacement,
            heightRejectionRate = heightRejectionRate,
            slopeRejectionRate = slopeRejectionRate,
            spacingRejectionRate = spacingRejectionRate,
            constraintEffectiveness = constraintEffectiveness,
            interpretation = interp
        });
    }
    private void EndResourceRun()
    {
        _resourceRunInProgress = false;
        EditorApplication.update -= ResourceRunTick;
        _resourceSeedQueue = null;
        if (_resourceSpeedUpDuringRun && _resourcesGenerator != null)
        {
            _resourcesGenerator.TestingItemsPerFrame = _prevItemsPerFrame;
            _resourcesGenerator.TestingMaxAttemptsPerFrame = _prevMaxAttemptsPerFrame;
        }
        // Restore TerrainGenerator logging if we muted it.
        var terrainGen = FindFirstObjectByType<TerrainGenerator>();
        if (_resourceMutedTerrainLogs && terrainGen != null)
        {
            terrainGen.logTerrainMetrics = _resourcePrevTerrainLogSetting;
        }
        _resourceMutedTerrainLogs = false;
        Debug.Log("[AlbuRIOTTestingWindow] Resource scatter run complete.");
    }
    private static MapResourcesGenerator FindMapResourcesGeneratorIncludingDisabled()
    {
        // In Play Mode, include disabled components so the test tool can still locate it
        // even when MapResourcesGenerator disables itself after generation.
        var all = FindObjectsByType<MapResourcesGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (all != null && all.Length > 0) return all[0];
        return null;
    }
    private void DrawResourceResults()
    {
        if (_resourceResults.Count == 0)
        {
            EditorGUILayout.HelpBox("No resource results yet.", MessageType.Info);
            return;
        }
        // Avoid confusing inventory snapshots (they do not track attempts).
        var displayResults = _resourceResults.Where(r => string.IsNullOrEmpty(r.label) || !r.label.StartsWith("Scene/", StringComparison.Ordinal)).ToList();
        if (displayResults.Count == 0) displayResults = _resourceResults; // fallback for unexpected legacy labels
        float avgPlaced = (float)displayResults.Average(r => r.placed);
        float avgAttemptsPer = (float)displayResults.Average(r => r.attemptsPerPlacement);
        float avgPAccept = (float)displayResults.Average(r => r.pAccept);
        float avgEff = (float)displayResults.Average(r => r.efficiency);
        EditorGUILayout.LabelField($"Rows: {displayResults.Count} | Avg placed {avgPlaced:F1} | Avg attempts/place {avgAttemptsPer:F2} | Avg pAccept {avgPAccept:F2} | Avg efficiency {avgEff:F2}");
        using (new EditorGUILayout.VerticalScope("box"))
        {
            foreach (var r in displayResults.Take(50))
            {
                string pAcceptStr = float.IsNaN(r.pAccept) ? "pAccept=NA" : $"pAccept {r.pAccept:F2}";
                string effTxt = float.IsNaN(r.efficiency) ? "eff=NA" : $"eff {r.efficiency:F2}";
                EditorGUILayout.LabelField(
                    $"Seed {r.seed} | {r.label} | placed {r.placed}/{r.desired} attempts {r.attempts} ({r.attemptsPerPlacement:F2}/place) | {pAcceptStr} | expected {r.expectedPlaced:F1} {effTxt}"
                );
                if (!string.IsNullOrEmpty(r.interpretation))
                    EditorGUILayout.LabelField("  → " + r.interpretation, EditorStyles.miniLabel);
            }
            if (displayResults.Count > 50)
                EditorGUILayout.LabelField($"… ({displayResults.Count - 50} more)");
        }
    }
    // ---------------- Behavior Tree ----------------
    private void DrawBehaviorTreeTab()
    {
        EditorGUILayout.LabelField("Behavior Tree (Formulas 14.0–16.2)", EditorStyles.boldLabel);
        _aiRecorder = (EnemyBehaviorMetricsRecorder)EditorGUILayout.ObjectField("Recorder", _aiRecorder, typeof(EnemyBehaviorMetricsRecorder), true);
        _aiObserveSeconds = Mathf.Clamp(EditorGUILayout.FloatField("Observe seconds", _aiObserveSeconds), 1f, 600f);
        _aiSampleInterval = Mathf.Clamp(EditorGUILayout.FloatField("Sample interval (s)", _aiSampleInterval), 0f, 1f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Ensure recorder in scene", GUILayout.Width(180)))
            {
                EnsureRecorder();
            }
            if (GUILayout.Button("Run AI observe pass"))
            {
                RunAIObservePass();
            }
            if (GUILayout.Button("Export AI CSV", GUILayout.Width(120)))
            {
                ExportAICsv();
            }
            if (GUILayout.Button("Clear results", GUILayout.Width(120)))
            {
                _aiResults.Clear();
            }
        }
        EditorGUILayout.Space(6);
        DrawAIResults();
    }
    private void EnsureRecorder()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[AlbuRIOTTestingWindow] Recorder requires Play Mode to run sampling.");
        }
        if (_aiRecorder != null) return;
        var existing = FindFirstObjectByType<EnemyBehaviorMetricsRecorder>();
        if (existing != null)
        {
            _aiRecorder = existing;
            return;
        }
        var go = new GameObject("EnemyBehaviorMetricsRecorder");
        _aiRecorder = go.AddComponent<EnemyBehaviorMetricsRecorder>();
        _aiRecorder.sampleIntervalSeconds = _aiSampleInterval;
        _aiRecorder.onlyAuthoritativeAIs = true;
        _aiRecorder.autoStopAfterSeconds = 0f;
    }
    private void RunAIObservePass()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[AlbuRIOTTestingWindow] AI observe pass requires Play Mode.");
            return;
        }
        EnsureRecorder();
        if (_aiRecorder == null)
        {
            Debug.LogError("[AlbuRIOTTestingWindow] Failed to create/find EnemyBehaviorMetricsRecorder.");
            return;
        }
        _aiRecorder.sampleIntervalSeconds = _aiSampleInterval;
        _aiRecorder.autoStopAfterSeconds = _aiObserveSeconds;
        _aiRecorder.StartRecording();
        // Note: the recorder stops itself after autoStopAfterSeconds. We capture a snapshot immediately as a "start",
        // and the user can click again to capture another run after it stops.
        EditorApplication.delayCall += () =>
        {
            // Wait one editor delay; results will be meaningful once the window duration elapses in play mode.
            // We record current global metrics now, and compute reaction latency once some targets are observed.
            var g = _aiRecorder.ComputeGlobalMetrics();
            float avgLatency = ComputeAvgReactionLatency(_aiRecorder);
            _aiResults.Add(new AIRunResult
            {
                elapsedSeconds = 0f,
                btTickRate = g.btTickRate,
                totalAIs = g.totalAIs,
                activeAIs = g.activeAIs,
                aiEfficiency = g.aiEfficiency,
                avgReactionLatency = avgLatency
            });
            Repaint();
        };
        EditorApplication.delayCall += () =>
        {
            // Schedule a completion snapshot slightly after the observe window.
            double start = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            void Tick()
            {
                if (EditorApplication.timeSinceStartup - start < _aiObserveSeconds + 0.1f) return;
                EditorApplication.update -= Tick;
                var g2 = _aiRecorder.ComputeGlobalMetrics();
                float avgLatency2 = ComputeAvgReactionLatency(_aiRecorder);
                _aiResults.Add(new AIRunResult
                {
                    elapsedSeconds = _aiObserveSeconds,
                    btTickRate = g2.btTickRate,
                    totalAIs = g2.totalAIs,
                    activeAIs = g2.activeAIs,
                    aiEfficiency = g2.aiEfficiency,
                    avgReactionLatency = avgLatency2
                });
                Repaint();
            }
        };
    }
    private float ComputeAvgReactionLatency(EnemyBehaviorMetricsRecorder rec)
    {
        if (rec == null) return 0f;
        var all = rec.GetAllEnemyMetrics();
        if (all == null || all.Count == 0) return 0f;
        float sum = 0f;
        int n = 0;
        foreach (var m in all)
        {
            if (float.IsNaN(m.firstTargetEnterTime) || float.IsNaN(m.firstTargetAcquireTime)) continue;
            float latency = m.firstTargetAcquireTime - m.firstTargetEnterTime;
            if (latency < 0f) continue;
            sum += latency;
            n++;
        }
        return n > 0 ? sum / n : 0f;
    }
    private void DrawAIResults()
    {
        if (_aiResults.Count == 0)
        {
            EditorGUILayout.HelpBox("No AI results yet. Enter Play Mode, ensure enemies exist, then run an observe pass.", MessageType.Info);
            return;
        }
        using (new EditorGUILayout.VerticalScope("box"))
        {
            foreach (var r in _aiResults)
            {
                EditorGUILayout.LabelField(
                    $"t={r.elapsedSeconds:F1}s | BT tick {r.btTickRate:F1}/s | active {r.activeAIs}/{r.totalAIs} => eff {r.aiEfficiency:F2} | avg reaction {r.avgReactionLatency:F3}s"
                );
            }
        }
    }
    // ---------------- Resource sampling helpers ----------------
    private static class ResourceSampling
    {
        public struct ProbResult
        {
            public int samples;
            public float pPerlin;
            public float pHeight;
            public float pSlope;
            public float pAccept;
        }
        public static ProbResult EstimateProbabilities(MapResourcesGenerator gen, MapResourcesGenerator.ResourceScatterMetrics cfg, int res)
        {
            int w = Mathf.Max(32, res);
            int h = w;
            int samples = w * h;
            int passPerlin = 0, passHeight = 0, passSlope = 0, passAll = 0;
            // Use active Terrain in scene; MapResourcesGenerator caches it internally but it's private.
            // Sampling uses Terrain API; if no terrain, fall back to probability estimates only for perlin.
            var terrain = FindFirstObjectByType<Terrain>();
            var td = terrain != null ? terrain.terrainData : null;
            Vector3 size = td != null ? td.size : new Vector3(w, 1f, h);
            var prng = new System.Random(12345);
            float offX = prng.Next(-100000, 100000);
            float offY = prng.Next(-100000, 100000);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = x / Mathf.Max(1f, (float)(w - 1));
                    float nz = y / Mathf.Max(1f, (float)(h - 1));
                    float vx = (nx * size.x) * cfg.perlinScale + offX;
                    float vz = (nz * size.z) * cfg.perlinScale + offY;
                    float p = Mathf.PerlinNoise(vx, vz);
                    bool okPerlin = p >= cfg.perlinThreshold;
                    if (okPerlin) passPerlin++;
                    bool okHeight = true;
                    bool okSlope = true;
                    if (td != null)
                    {
                        float hNorm = td.GetInterpolatedHeight(nx, nz) / Mathf.Max(0.0001f, size.y);
                        // Match herb placement: must be above sand + 0.01 (sand threshold comes from TerrainGenerator)
                        var tg = FindFirstObjectByType<TerrainGenerator>();
                        float sand = tg != null ? Mathf.Max(0.001f, tg.sandThreshold) : 0.02f;
                        okHeight = hNorm > sand + 0.01f;
                        float slope = td.GetSteepness(nx, nz);
                        // MapResourcesGenerator uses its own maxSlope field; sample it if available via reflection fallback
                        float maxSlope = 28f;
                        if (gen != null)
                        {
                            var f = typeof(MapResourcesGenerator).GetField("maxSlope", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (f != null) maxSlope = (float)f.GetValue(gen);
                        }
                        okSlope = slope <= maxSlope;
                    }
                    if (okHeight) passHeight++;
                    if (okSlope) passSlope++;
                    if (okPerlin && okHeight && okSlope) passAll++;
                }
            }
            return new ProbResult
            {
                samples = samples,
                pPerlin = passPerlin / Mathf.Max(1f, samples),
                pHeight = passHeight / Mathf.Max(1f, samples),
                pSlope = passSlope / Mathf.Max(1f, samples),
                pAccept = passAll / Mathf.Max(1f, samples),
            };
        }
        public static float EstimateExpectedPlaced(MapResourcesGenerator gen, MapResourcesGenerator.ResourceScatterMetrics cfg)
        {
            // capacity definition aligned with Assets/Scripts/Map/TerrainMetricsReporter.cs
            float area = 1f;
            var terrain = FindFirstObjectByType<Terrain>();
            if (terrain != null && terrain.terrainData != null)
            {
                var size = terrain.terrainData.size;
                area = size.x * size.z;
            }
            float r = Mathf.Max(0.1f, cfg.minSpacing);
            float capacity = area / (Mathf.PI * r * r) * 0.82f;
            return Mathf.Min(cfg.desiredCount, capacity);
        }
    }
    private static string InterpretResourceRun(MapResourcesGenerator.ResourceScatterMetrics runtime, ResourceSampling.ProbResult probs, float expectedPlaced, float efficiency)
    {
        var sb = new StringBuilder();
        if (runtime.placed >= runtime.desiredCount)
            sb.Append("Meets desired count. ");
        else
            sb.Append($"Below desired by {runtime.desiredCount - runtime.placed}. ");
        if (probs.pAccept >= 0.25f)
            sb.Append("Acceptance probability is healthy. ");
        else if (probs.pAccept >= 0.1f)
            sb.Append("Acceptance probability is moderate; constraints may be tight. ");
        else
            sb.Append("Acceptance probability is low; expect many retries. ");
        if (efficiency >= 0.9f)
            sb.Append("Efficiency near expected capacity. ");
        else if (efficiency >= 0.6f)
            sb.Append("Efficiency moderate vs expected capacity. ");
        else
            sb.Append("Efficiency low vs expected capacity (likely spacing/water/height constraints). ");
        // Highlight dominant rejection reason from runtime counters
        int max = runtime.perlinRejected;
        string reason = "perlin";
        if (runtime.heightRejected > max) { max = runtime.heightRejected; reason = "height"; }
        if (runtime.slopeRejected > max) { max = runtime.slopeRejected; reason = "slope"; }
        if (runtime.waterRejected > max) { max = runtime.waterRejected; reason = "water"; }
        if (runtime.spacingRejected > max) { max = runtime.spacingRejected; reason = "spacing"; }
        sb.Append($"Dominant rejection: {reason}.");
        return sb.ToString();
    }
    private void ExportResourceReport()
    {
        if (_resourceResults.Count == 0)
        {
            Debug.LogWarning("[AlbuRIOTTestingWindow] No resource results to export.");
            return;
        }
        string dir = Path.Combine(Application.dataPath, "AlbuRIOT_Reports");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string fileName = $"resource_scatter_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        string path = Path.Combine(dir, fileName);
        var sb = new StringBuilder();
        sb.AppendLine("AlbuRIOT Resource Scatter Report (MapResourcesGenerator)");
        sb.AppendLine($"Generated: {DateTime.Now}");
        // Keep "Rows:" consistent with filtering below.
        sb.AppendLine();
        // Avoid exporting inventory snapshots (they show attempts=0 even when runtime placement attempted).
        var exportResults = _resourceResults.Where(r => string.IsNullOrEmpty(r.label) || !r.label.StartsWith("Scene/", StringComparison.Ordinal));
        sb.AppendLine($"Rows: {exportResults.Count()}");
        foreach (var r in exportResults)
        {
            string pAcceptStr = float.IsNaN(r.pAccept) ? "pAccept=NA" : $"pAccept {r.pAccept:F2}";
            string effStr = float.IsNaN(r.efficiency) ? "eff=NA" : $"eff {r.efficiency:F2}";
            sb.AppendLine(
                $"Seed {r.seed} | {r.label} | placed {r.placed}/{r.desired} attempts {r.attempts} ({r.attemptsPerPlacement:F2}/place) | {pAcceptStr} | expected {r.expectedPlaced:F1} {effStr}"
            );
            if (!string.IsNullOrEmpty(r.interpretation))
                sb.AppendLine("  " + r.interpretation);
        }
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[AlbuRIOTTestingWindow] Resource report written to {path}");
    }
    private void ExportResourceCsv()
    {
        if (_resourceResults.Count == 0)
        {
            Debug.LogWarning("[AlbuRIOTTestingWindow] No resource results to export.");
            return;
        }
        string dir = Path.Combine(Application.dataPath, "AlbuRIOT_Reports");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string fileName = $"resource_scatter_runs_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string path = Path.Combine(dir, fileName);
        static string F(float v) => float.IsNaN(v) ? "" : v.ToString("0.###", CultureInfo.InvariantCulture);
        static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.AppendLine("seed,label,desired,placed,attempts,attemptsPerPlacement,pPerlin,pHeight,pSlope,pAccept,expectedPlaced,efficiency,estAttemptsPerPlacement,heightRejectionRate,slopeRejectionRate,spacingRejectionRate,constraintEffectiveness");
        var exportResults = _resourceResults.Where(r => string.IsNullOrEmpty(r.label) || !r.label.StartsWith(\"Scene/\", StringComparison.Ordinal));
        foreach (var r in exportResults)
        {
            string label = (r.label ?? \"\").Replace(\"\\\"\", \"\\\"\\\"\");
            sb.AppendLine(string.Join(\",\",
                I(r.seed),
                $\"\\\"{label}\\\"\",
                I(r.desired),
                I(r.placed),
                I(r.attempts),
                F(r.attemptsPerPlacement),
                F(r.pPerlin),
                F(r.pHeight),
                F(r.pSlope),
                F(r.pAccept),
                F(r.expectedPlaced),
                F(r.efficiency),
                F(r.estAttemptsPerPlacement),
                F(r.heightRejectionRate),
                F(r.slopeRejectionRate),
                F(r.spacingRejectionRate),
                F(r.constraintEffectiveness)
            ));
        }
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[AlbuRIOTTestingWindow] Resource CSV written to {path}");
    }
    private void ExportAICsv()
    {
        if (_aiResults.Count == 0)
        {
            Debug.LogWarning("[AlbuRIOTTestingWindow] No AI results to export.");
            return;
        }
        string dir = Path.Combine(Application.dataPath, "AlbuRIOT_Reports");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string fileName = $"behavior_tree_ai_runs_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string path = Path.Combine(dir, fileName);
        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        // Use min/max columns so callers can also store ranges.
        // When exporting from runtime snapshots (single values), min=max=value.
        sb.AppendLine("elapsedSeconds,btTickRateMin,btTickRateMax,totalAIs,activeAIs,aiEfficiency,reactionLatencyMin,reactionLatencyMax");
        foreach (var r in _aiResults)
        {
            sb.AppendLine(string.Join(",",
                F(r.elapsedSeconds),
                F(r.btTickRate),
                F(r.btTickRate),
                I(r.totalAIs),
                I(r.activeAIs),
                F(r.aiEfficiency),
                F(r.avgReactionLatency),
                F(r.avgReactionLatency)
            ));
        }
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[AlbuRIOTTestingWindow] AI CSV written to {path}");
    }
}
#endif
