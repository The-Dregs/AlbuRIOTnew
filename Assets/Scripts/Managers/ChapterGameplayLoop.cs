using UnityEngine;
using Photon.Pun;
using System.Collections;
using System.Collections.Generic;

public enum ChapterType
{
    Prologue,
    Chapter2_Revelation,
    Chapter3_Lunar,
    Chapter4_Radiant,
    Chapter5_Creator
}

[System.Serializable]
public class ChapterWave
{
    [Header("Wave Settings")]
    public string waveName;
    public float waveStartDelay = 0f;
    public int enemyCount = 5;
    public string[] enemyTypes;
    public float spawnInterval = 2f;
    public bool waitForAllKilled = true;
    
    [Header("Conditions")]
    public bool requiresNightPhase = true;
    public int minPlayerCount = 1;
}

public class ChapterGameplayLoop : MonoBehaviourPun
{
    [Header("Chapter Configuration")]
    public ChapterType chapterType;
    [Tooltip("Duration of day phase in seconds")]
    public float dayPhaseDuration = 120f;
    [Tooltip("Duration of night phase in seconds")]
    public float nightPhaseDuration = 180f;
    
    [Header("Enemy Waves")]
    public ChapterWave[] enemyWaves;
    
    [Header("Objectives")]
    [Tooltip("Objectives that must be completed before enemies spawn")]
    public string[] requiredObjectives;
    
    [Header("References")]
    public EnemyManager enemyManager;
    public QuestManager questManager;
    public DayNightCycleManager dayNightManager;
    
    private int currentWaveIndex = 0;
    private bool isWaveActive = false;
    private bool chapterCompleted = false;
    private List<BaseEnemyAI> currentWaveEnemies = new List<BaseEnemyAI>();
    
    void Start()
    {
        if (enemyManager == null)
            enemyManager = FindFirstObjectByType<EnemyManager>();
        if (questManager == null)
            questManager = FindFirstObjectByType<QuestManager>();
        if (dayNightManager == null)
            dayNightManager = FindFirstObjectByType<DayNightCycleManager>();
        
        if (dayNightManager != null)
        {
            dayNightManager.OnPhaseChanged += OnDayNightPhaseChanged;
        }
        
        if (questManager != null)
        {
            questManager.OnObjectiveCompleted += OnObjectiveCompleted;
        }
        
        StartChapterLoop();
    }
    
    void OnDestroy()
    {
        if (dayNightManager != null)
        {
            dayNightManager.OnPhaseChanged -= OnDayNightPhaseChanged;
        }
        
        if (questManager != null)
        {
            questManager.OnObjectiveCompleted -= OnObjectiveCompleted;
        }
    }
    
    private void StartChapterLoop()
    {
        if (PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode)
        {
            StartCoroutine(ChapterLoopCoroutine());
        }
    }
    
    private IEnumerator ChapterLoopCoroutine()
    {
        // wait for any start-scene cutscene to finish before starting gameplay
        yield return StartCoroutine(WaitForStartCutsceneComplete());

        yield return new WaitForSeconds(2f);
        
        while (!chapterCompleted)
        {
            if (CanStartNextWave())
            {
                yield return StartCoroutine(ExecuteWave(enemyWaves[currentWaveIndex]));
                currentWaveIndex++;
                
                if (currentWaveIndex >= enemyWaves.Length)
                {
                    currentWaveIndex = 0;
                    yield return new WaitForSeconds(10f);
                }
            }
            else
            {
                yield return new WaitForSeconds(5f);
            }
        }
    }
    
    private bool CanStartNextWave()
    {
        if (currentWaveIndex >= enemyWaves.Length) return false;
        if (isWaveActive) return false;
        if (chapterCompleted) return false;
        
        var wave = enemyWaves[currentWaveIndex];
        
        if (wave.requiresNightPhase && dayNightManager != null)
        {
            if (!dayNightManager.IsNight()) return false;
        }
        
        int playerCount = MultiplayerScalingManager.Instance != null 
            ? MultiplayerScalingManager.Instance.GetPlayerCount() 
            : 1;
        if (playerCount < wave.minPlayerCount) return false;
        
        if (requiredObjectives != null && requiredObjectives.Length > 0)
        {
            foreach (var objName in requiredObjectives)
            {
                var objective = questManager?.GetCurrentObjective();
                if (objective == null || objective.objectiveName != objName || !objective.IsCompleted)
                    return false;
            }
        }
        
        return true;
    }
    
    private IEnumerator ExecuteWave(ChapterWave wave)
    {
        isWaveActive = true;
        currentWaveEnemies.Clear();
        
        yield return new WaitForSeconds(wave.waveStartDelay);
        
        Debug.Log($"[ChapterLoop] Starting wave: {wave.waveName}");
        
        int scaledCount = MultiplayerScalingManager.Instance != null
            ? MultiplayerScalingManager.Instance.GetScaledSpawnCount(wave.enemyCount)
            : wave.enemyCount;
        
        for (int i = 0; i < scaledCount; i++)
        {
            if (wave.enemyTypes == null || wave.enemyTypes.Length == 0) break;
            
            string enemyType = wave.enemyTypes[Random.Range(0, wave.enemyTypes.Length)];
            
            if (enemyManager != null && enemyManager.spawnPoints != null && enemyManager.spawnPoints.Length > 0)
            {
                enemyManager.SpawnEnemyAtRandomPoint(enemyType);
            }
            
            yield return new WaitForSeconds(wave.spawnInterval);
        }
        
        if (wave.waitForAllKilled)
        {
            yield return StartCoroutine(WaitForWaveCompletion());
        }
        
        isWaveActive = false;
        Debug.Log($"[ChapterLoop] Wave completed: {wave.waveName}");
    }
    
    private IEnumerator WaitForWaveCompletion()
    {
        while (currentWaveEnemies.Count > 0)
        {
            currentWaveEnemies.RemoveAll(e => e == null || e.IsDead);
            yield return new WaitForSeconds(1f);
        }
    }
    
    private void OnDayNightPhaseChanged(DayNightCycleManager.TimePhase phase)
    {
        if (phase == DayNightCycleManager.TimePhase.Night)
        {
            Debug.Log("[ChapterLoop] Night phase started - enemies can spawn");
        }
        else if (phase == DayNightCycleManager.TimePhase.Day)
        {
            Debug.Log("[ChapterLoop] Day phase started - enemies stop spawning");
        }
    }

    private IEnumerator WaitForStartCutsceneComplete()
    {
        // look for any active start-scene cutscene manager
        CutsceneManager cm = null;
        var all = FindObjectsByType<CutsceneManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].cutsceneMode == CutsceneMode.StartScene)
            {
                cm = all[i];
                break;
            }
        }

        if (cm == null) yield break;

        float elapsed = 0f;
        const float maxWait = 90f;
        while (elapsed < maxWait && cm != null && !cm.IsStartSequenceComplete)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        Debug.Log("[ChapterLoop] Start cutscene completed, beginning gameplay loop.");
    }
    
    private void OnObjectiveCompleted(QuestObjective objective)
    {
        Debug.Log($"[ChapterLoop] Objective completed: {objective.objectiveName}");
    }
    
    public void OnEnemySpawned(BaseEnemyAI enemy)
    {
        if (isWaveActive && enemy != null)
        {
            currentWaveEnemies.Add(enemy);
            enemy.OnEnemyDied += OnWaveEnemyDied;
        }
    }
    
    private void OnWaveEnemyDied(BaseEnemyAI enemy)
    {
        if (currentWaveEnemies.Contains(enemy))
        {
            currentWaveEnemies.Remove(enemy);
        }
    }
    
    public void CompleteChapter()
    {
        chapterCompleted = true;
        StopAllCoroutines();
        
        if (enemyManager != null)
        {
            enemyManager.ClearAllEnemies();
        }
    }
}

