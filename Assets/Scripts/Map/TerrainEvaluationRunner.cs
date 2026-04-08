using UnityEngine;

// Drop this on an empty GameObject in a test scene with a Terrain + TerrainGenerator assigned.
// It will run multiple generations (changing seed each time) and log aggregate metrics for analysis.
public class TerrainEvaluationRunner : MonoBehaviour
{
    public TerrainGenerator generator;
    [Range(1,50)] public int runs = 5;
    public bool runOnStart = true;

    void Start()
    {
        if (runOnStart) RunEvaluation();
    }

    [ContextMenu("Run Evaluation")]
    public void RunEvaluation()
    {
        if (generator == null)
        {
            generator = FindFirstObjectByType<TerrainGenerator>();
        }
        if (generator == null || generator.terrain == null)
        {
            Debug.LogError("TerrainEvaluationRunner: Assign a TerrainGenerator with a Terrain.");
            return;
        }

        double totalMs = 0, totalHeightMs = 0, totalSplatMs = 0, totalApplyHeight = 0, totalApplySplat = 0, totalObjects = 0, totalDetails = 0;
        double sumCorr = 0; double sumCorr2 = 0;
        double sumLand = 0; double sumSand = 0;
        double sumMeanH = 0; double sumStdH = 0;
        double sumDeltaMount = 0;

        for (int i = 0; i < runs; i++)
        {
            generator.GenerateTerrain();
            var m = generator.lastMetrics;
            totalMs += m.totalMs; totalHeightMs += m.msHeightCompute; totalSplatMs += m.msSplatCompute; totalApplyHeight += m.msApplyHeight; totalApplySplat += m.msApplySplat; totalObjects += m.msObjects; totalDetails += m.msDetails;
            sumCorr += m.radialCorrelation; sumCorr2 += m.radialCorrelation * m.radialCorrelation;
            sumLand += m.landPercent; sumSand += m.sandPercent;
            sumMeanH += m.meanHeight; sumStdH += m.stdDevHeight;
            sumDeltaMount += (m.meanHeightMountain - m.meanHeightNonMountain);
        }

        double n = runs;
        double avgTotal = totalMs / n;
        double avgHeight = totalHeightMs / n;
        double avgSplat = totalSplatMs / n;
        double avgApplyH = totalApplyHeight / n;
        double avgApplyS = totalApplySplat / n;
        double avgObj = totalObjects / n;
        double avgDet = totalDetails / n;
        double meanCorr = sumCorr / n; double varCorr = System.Math.Max(0, sumCorr2 / n - meanCorr * meanCorr);
        double avgLand = sumLand / n; double avgSand = sumSand / n;
        double avgMeanH = sumMeanH / n; double avgStdH = sumStdH / n;
        double avgDeltaMount = sumDeltaMount / n;

        Debug.Log($"Terrain Eval ({runs} runs) -> Avg Total {avgTotal:F1}ms | Height {avgHeight:F1}+{avgApplyH:F1} | Splat {avgSplat:F1}+{avgApplyS:F1} | Objects {avgObj:F1} | Details {avgDet:F1} | land {avgLand:P1} sand {avgSand:P1} | radial corr {meanCorr:F2} (var {varCorr:F3}) | mountain meanΔ {avgDeltaMount:F3} | meanH {avgMeanH:F3} stdH {avgStdH:F3}");
    }
}
