using System;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

/// <summary>
/// Runtime recorder for Behavior Tree metrics (Formulas 14–16 in Assets/docs/TESTING.md).
/// Attach in Play Mode (safe in multiplayer: computes locally; authority considerations are handled in aggregation).
/// </summary>
public class EnemyBehaviorMetricsRecorder : MonoBehaviour
{
    [Header("Sampling")]
    [Tooltip("Seconds between samples. 0 = per-frame sampling.")]
    [Range(0f, 1f)] public float sampleIntervalSeconds = 0f;

    [Tooltip("If true, only count enemies whose AI ticks are authoritative on this client (MasterClient or offline).")]
    public bool onlyAuthoritativeAIs = true;

    [Header("Window")]
    [Tooltip("If > 0, auto-stop after this many seconds.")]
    [Min(0f)] public float autoStopAfterSeconds = 0f;

    private bool _isRunning;
    private float _startedAt;
    private float _lastSampleAt;

    public struct EnemySnapshot
    {
        public BaseEnemyAI.AIState state;
        public bool isActive;
        public bool isAuthoritative;
        public bool hasTarget;
    }

    public class EnemyMetrics
    {
        public BaseEnemyAI enemy;
        public float totalObservedTime;
        public Dictionary<BaseEnemyAI.AIState, float> timeInState = new Dictionary<BaseEnemyAI.AIState, float>();
        public Dictionary<(BaseEnemyAI.AIState from, BaseEnemyAI.AIState to), int> transitions = new Dictionary<(BaseEnemyAI.AIState, BaseEnemyAI.AIState), int>();
        public BaseEnemyAI.AIState? lastState;

        public float firstTargetEnterTime = float.NaN;
        public float firstTargetAcquireTime = float.NaN;
    }

    private readonly Dictionary<int, EnemyMetrics> _metricsByInstanceId = new Dictionary<int, EnemyMetrics>();

    public bool IsRunning => _isRunning;
    public float ElapsedSeconds => _isRunning ? (Time.time - _startedAt) : 0f;

    public void StartRecording()
    {
        _metricsByInstanceId.Clear();
        _isRunning = true;
        _startedAt = Time.time;
        _lastSampleAt = Time.time;
    }

    public void StopRecording()
    {
        _isRunning = false;
    }

    void Update()
    {
        if (!_isRunning) return;

        if (autoStopAfterSeconds > 0f && (Time.time - _startedAt) >= autoStopAfterSeconds)
        {
            StopRecording();
            return;
        }

        float dt;
        if (sampleIntervalSeconds <= 0f)
        {
            dt = Time.deltaTime;
            SampleAll(dt);
            return;
        }

        float now = Time.time;
        if (now - _lastSampleAt < sampleIntervalSeconds) return;

        dt = now - _lastSampleAt;
        _lastSampleAt = now;
        SampleAll(dt);
    }

    private void SampleAll(float dt)
    {
        // Note: FindObjectsByType is supported in newer Unity, but the project uses FindFirstObjectByType elsewhere,
        // so we use FindObjectsByType for consistency with Unity 2023+.
        var enemies = FindObjectsByType<BaseEnemyAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (enemies == null) return;

        bool online = PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode;
        bool authoritativeOnThisClient = !online || PhotonNetwork.IsMasterClient;

        for (int i = 0; i < enemies.Length; i++)
        {
            var e = enemies[i];
            if (e == null) continue;

            bool isAuthoritative = authoritativeOnThisClient;
            if (onlyAuthoritativeAIs && !isAuthoritative) continue;

            int id = e.GetInstanceID();
            if (!_metricsByInstanceId.TryGetValue(id, out var m))
            {
                m = new EnemyMetrics { enemy = e };
                _metricsByInstanceId[id] = m;
            }

            var state = e.CurrentState;
            m.totalObservedTime += dt;
            if (!m.timeInState.ContainsKey(state)) m.timeInState[state] = 0f;
            m.timeInState[state] += dt;

            if (m.lastState.HasValue && m.lastState.Value != state)
            {
                var key = (m.lastState.Value, state);
                m.transitions.TryGetValue(key, out int count);
                m.transitions[key] = count + 1;
            }
            m.lastState = state;

            // Reaction latency (Formula 16.1): track first time target is within detection range vs when AI acquires it.
            // With current BaseEnemyAI implementation, acquisition typically occurs on the same tick as entering range,
            // but we still track these timestamps for consistency with the paper metric definition.
            var target = e.Target;
            if (target != null && float.IsNaN(m.firstTargetAcquireTime))
            {
                m.firstTargetAcquireTime = Time.time;
            }

            if (float.IsNaN(m.firstTargetEnterTime))
            {
                // Estimate "enter range" by checking nearest player distance vs detectionRange.
                // This does not mutate AI state; it's an observational metric.
                var nearest = PlayerRegistry.FindNearest(e.transform.position);
                if (nearest != null && e.enemyData != null)
                {
                    float det = e.enemyData.detectionRange;
                    float dSqr = (e.transform.position - nearest.position).sqrMagnitude;
                    if (dSqr <= det * det)
                    {
                        m.firstTargetEnterTime = Time.time;
                    }
                }
            }
        }
    }

    // --- Formula helpers (computed from recorded data) ---

    public struct ComputedAIMetrics
    {
        public int totalAIs;
        public int activeAIs;
        public float aiEfficiency; // Formula 16.2
        public float btTickRate; // Formula 16.0 (global)
    }

    public ComputedAIMetrics ComputeGlobalMetrics()
    {
        var enemies = FindObjectsByType<BaseEnemyAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int total = enemies != null ? enemies.Length : 0;

        bool online = PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode;
        bool authoritativeOnThisClient = !online || PhotonNetwork.IsMasterClient;

        int active = 0;
        if (enemies != null)
        {
            for (int i = 0; i < enemies.Length; i++)
            {
                var e = enemies[i];
                if (e == null) continue;
                if (e.IsDead) continue;
                if (!e.enabled) continue;
                if (onlyAuthoritativeAIs && !authoritativeOnThisClient) continue;
                active++;
            }
        }

        float eff = total > 0 ? (float)active / total : 0f;
        float tick = 1f / Mathf.Max(0.000001f, Time.smoothDeltaTime);
        return new ComputedAIMetrics { totalAIs = total, activeAIs = active, aiEfficiency = eff, btTickRate = tick };
    }

    public IReadOnlyCollection<EnemyMetrics> GetAllEnemyMetrics()
    {
        return _metricsByInstanceId.Values;
    }
}

