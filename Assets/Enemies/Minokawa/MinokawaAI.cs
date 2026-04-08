using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class MinokawaAI : BaseEnemyAI
{
    private enum MinokawaSkill { None, Spitfire, Moon, Dive }

    [Header("Spitfire (Projectile Burst)")]
    public string spitfireProjectilePrefabPath = "Enemies/Projectiles/Belly Laugh";
    public Transform spitfireMuzzle;
    public int spitfireDamage = 14;
    public int spitfireProjectileCount = 3;
    public float spitfireSpreadDeg = 18f;
    public float spitfireProjectileSpeed = 18f;
    public float spitfireProjectileLifetime = 2.5f;
    public float spitfireProjectileRange = 16f;
    public float spitfireWindup = 0.55f;
    public float spitfireCooldown = 9f;
    public string spitfireWindupTrigger = "SpitfireWindup";
    public string spitfireTrigger = "Spitfire";
    public AudioClip spitfireWindupSFX;
    public AudioClip spitfireImpactSFX;
    public GameObject spitfireWindupVFX;
    public GameObject spitfireImpactVFX;
    public Vector3 spitfireVFXOffset = Vector3.zero;
    public float spitfireVFXScale = 1f;
    public float spitfireStoppageTime = 0.5f;
    public float spitfireRecoveryTime = 0.25f;

    [Header("Moon Attack (Ground AoE)")]
    public int moonTickDamage = 12;
    public float moonRadius = 5f;
    public int moonTicks = 6;
    public float moonTickInterval = 0.35f;
    public float moonWindup = 0.7f;
    public float moonCooldown = 10f;
    public string moonWindupTrigger = "MoonWindup";
    public string moonTrigger = "MoonAttack";
    public AudioClip moonWindupSFX;
    public AudioClip moonImpactSFX;
    public GameObject moonTelegraphVFX;
    public GameObject moonImpactVFX;
    public float moonStoppageTime = 0.7f;
    public float moonRecoveryTime = 0.35f;

    [Header("Dive Attack")]
    public int diveDamage = 28;
    public float diveHitRadius = 1.8f;
    public float diveWindup = 0.55f;
    public float diveCooldown = 12f;
    public float diveAscendTime = 0.35f;
    public float diveAscendSpeed = 4f;
    public float diveDescendSpeed = 18f;
    public float diveTravelTime = 0.8f;
    public string diveWindupTrigger = "DiveWindup";
    public string diveTrigger = "Dive";
    public AudioClip diveWindupSFX;
    public AudioClip diveImpactSFX;
    public GameObject diveWindupVFX;
    public GameObject diveImpactVFX;
    public Vector3 diveVFXOffset = Vector3.zero;
    public float diveVFXScale = 1f;
    public float diveStoppageTime = 0.9f;
    public float diveRecoveryTime = 0.4f;

    [Header("Skill Selection (Distance/Weight)")]
    public float spitfirePreferredMinDistance = 6f;
    public float spitfirePreferredMaxDistance = 14f;
    [Range(0f, 1f)] public float spitfireSkillWeight = 0.8f;
    public float moonPreferredMinDistance = 3f;
    public float moonPreferredMaxDistance = 8f;
    [Range(0f, 1f)] public float moonSkillWeight = 0.85f;
    public float divePreferredMinDistance = 5f;
    public float divePreferredMaxDistance = 12f;
    [Range(0f, 1f)] public float diveSkillWeight = 0.75f;
    [Range(0f, 1f)] public float minSkillScoreToCast = 0.15f;

    private float lastSpitfireTime = -9999f;
    private float lastMoonTime = -9999f;
    private float lastDiveTime = -9999f;
    private Coroutine activeAbility;
    private MinokawaSkill queuedSkill = MinokawaSkill.None;

    protected override void InitializeEnemy()
    {
        // no special initialization required
    }

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTarget, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var canSkill = new ConditionNode(blackboard, CanCastWeightedSkill, "can_weighted_skill");
        var doSkill = new ActionNode(blackboard, ExecuteWeightedSkill, "do_weighted_skill");
        var targetInAttack = new ConditionNode(blackboard, TargetInAttackRangeAndFacing, "in_attack_range_facing");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "skill_seq").Add(canSkill, doSkill),
                        new Sequence(blackboard, "basic_seq").Add(targetInAttack, basicAttack),
                        moveToTarget
                    )
                ),
                new ActionNode(blackboard, Patrol, "patrol")
            );
    }

    protected override void PerformBasicAttack()
    {
        if (activeAbility != null || isBusy) return;
        if (enemyData == null) return;
        if (Time.time - lastAttackTime < enemyData.attackCooldown) return;

        var target = blackboard.Get<Transform>("target");
        if (target == null) return;

        if (animator != null && HasTrigger(attackTrigger))
            SetTriggerSync(attackTrigger);
        PlayAttackWindupSfx();

        float radius = Mathf.Max(0.8f, enemyData.attackRange);
        Vector3 center = transform.position + transform.forward * (enemyData.attackRange * 0.5f);
        Collider[] cols = Physics.OverlapSphere(center, radius, LayerMask.GetMask("Player"));

        PlayAttackImpactSfx();
        for (int i = 0; i < cols.Length; i++)
        {
            PlayerStats ps = cols[i].GetComponentInParent<PlayerStats>();
            if (ps != null)
                DamageRelay.ApplyToPlayer(ps.gameObject, enemyData.basicDamage);
        }

        lastAttackTime = Time.time;
        attackLockTimer = enemyData.attackMoveLock;
    }

    protected override bool TrySpecialAbilities()
    {
        return false;
    }

    private bool CanCastWeightedSkill()
    {
        queuedSkill = MinokawaSkill.None;
        if (isBusy || activeAbility != null) return false;

        Transform target = blackboard.Get<Transform>("target");
        if (target == null) return false;

        float distance = Vector3.Distance(transform.position, target.position);
        float bestScore = minSkillScoreToCast;

        if (CanSpitfire())
        {
            float score = ComputeRangeScore(distance, spitfirePreferredMinDistance, spitfirePreferredMaxDistance) * spitfireSkillWeight;
            if (score > bestScore)
            {
                bestScore = score;
                queuedSkill = MinokawaSkill.Spitfire;
            }
        }

        if (CanMoonAttack())
        {
            float score = ComputeRangeScore(distance, moonPreferredMinDistance, moonPreferredMaxDistance) * moonSkillWeight;
            if (score > bestScore)
            {
                bestScore = score;
                queuedSkill = MinokawaSkill.Moon;
            }
        }

        if (CanDive())
        {
            float score = ComputeRangeScore(distance, divePreferredMinDistance, divePreferredMaxDistance) * diveSkillWeight;
            if (score > bestScore)
            {
                bestScore = score;
                queuedSkill = MinokawaSkill.Dive;
            }
        }

        return queuedSkill != MinokawaSkill.None;
    }

    private NodeState ExecuteWeightedSkill()
    {
        switch (queuedSkill)
        {
            case MinokawaSkill.Spitfire:
                StartSpitfire();
                break;
            case MinokawaSkill.Moon:
                StartMoonAttack();
                break;
            case MinokawaSkill.Dive:
                StartDive();
                break;
            default:
                return NodeState.Failure;
        }

        queuedSkill = MinokawaSkill.None;
        return NodeState.Success;
    }

    private float ComputeRangeScore(float distance, float minDistance, float maxDistance)
    {
        if (maxDistance <= minDistance)
            return 0f;
        if (distance < minDistance || distance > maxDistance)
            return 0f;

        float mid = (minDistance + maxDistance) * 0.5f;
        float half = Mathf.Max(0.01f, (maxDistance - minDistance) * 0.5f);
        return 1f - Mathf.Clamp01(Mathf.Abs(distance - mid) / half);
    }

    private bool CanSpitfire()
    {
        if (Time.time - lastSpitfireTime < spitfireCooldown) return false;
        Transform target = blackboard.Get<Transform>("target");
        return target != null && IsFacingTarget(target, SpecialFacingAngle);
    }

    private void StartSpitfire()
    {
        if (activeAbility != null) return;
        lastSpitfireTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoSpitfire());
    }

    private IEnumerator CoSpitfire()
    {
        BeginAction(AIState.Special1);
        Quaternion lockedRotation = transform.rotation;

        try
        {
            if (HasTrigger(spitfireWindupTrigger)) SetTriggerSync(spitfireWindupTrigger);
            else if (HasTrigger(spitfireTrigger)) SetTriggerSync(spitfireTrigger);
            PlaySfx(spitfireWindupSFX);
            SpawnTimedVFX(spitfireWindupVFX, spitfireVFXOffset, spitfireVFXScale, spitfireWindup + 0.25f);

            float windup = Mathf.Max(0f, spitfireWindup);
            while (windup > 0f)
            {
                windup -= Time.deltaTime;
                transform.rotation = lockedRotation;
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                yield return null;
            }

            if (HasTrigger(spitfireTrigger)) SetTriggerSync(spitfireTrigger);
            PlaySfx(spitfireImpactSFX);
            SpawnTimedVFX(spitfireImpactVFX, spitfireVFXOffset, spitfireVFXScale, 1.2f);

            Vector3 spawnPos = spitfireMuzzle != null ? spitfireMuzzle.position : (transform.position + transform.forward * 1.5f + Vector3.up);
            int count = Mathf.Max(1, spitfireProjectileCount);
            float step = count > 1 ? spitfireSpreadDeg / (count - 1) : 0f;
            float startYaw = -spitfireSpreadDeg * 0.5f;

            for (int i = 0; i < count; i++)
            {
                float yaw = startYaw + step * i;
                Quaternion rot = transform.rotation * Quaternion.Euler(0f, yaw, 0f);
                GameObject projectile = SpawnProjectile(spitfireProjectilePrefabPath, spawnPos, rot);
                if (projectile == null) continue;

                EnemyProjectile projectileComp = projectile.GetComponent<EnemyProjectile>();
                if (projectileComp == null) continue;

                projectileComp.Initialize(gameObject, spitfireDamage, spitfireProjectileSpeed, spitfireProjectileLifetime, null);
                projectileComp.maxDistance = spitfireProjectileRange;
            }

            yield return StartCoroutine(DoStoppageAndRecovery(lockedRotation, spitfireStoppageTime, spitfireRecoveryTime));
        }
        finally
        {
            if (isBusy) EndAction();
            activeAbility = null;
        }
    }

    private bool CanMoonAttack()
    {
        if (Time.time - lastMoonTime < moonCooldown) return false;
        return blackboard.Get<Transform>("target") != null;
    }

    private void StartMoonAttack()
    {
        if (activeAbility != null) return;
        lastMoonTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoMoonAttack());
    }

    private IEnumerator CoMoonAttack()
    {
        BeginAction(AIState.Special2);
        Quaternion lockedRotation = transform.rotation;
        Transform target = blackboard.Get<Transform>("target");
        Vector3 castPosition = target != null ? target.position : transform.position;

        try
        {
            if (HasTrigger(moonWindupTrigger)) SetTriggerSync(moonWindupTrigger);
            else if (HasTrigger(moonTrigger)) SetTriggerSync(moonTrigger);
            PlaySfx(moonWindupSFX);
            SpawnWorldTimedVFX(moonTelegraphVFX, castPosition, Quaternion.identity, moonWindup + 0.35f);

            float windup = Mathf.Max(0f, moonWindup);
            while (windup > 0f)
            {
                windup -= Time.deltaTime;
                transform.rotation = lockedRotation;
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                yield return null;
            }

            if (HasTrigger(moonTrigger)) SetTriggerSync(moonTrigger);
            PlaySfx(moonImpactSFX);
            SpawnWorldTimedVFX(moonImpactVFX, castPosition, Quaternion.identity, Mathf.Max(1f, moonTicks * moonTickInterval));

            int tickCount = Mathf.Max(1, moonTicks);
            float tickInterval = Mathf.Max(0.05f, moonTickInterval);
            for (int i = 0; i < tickCount; i++)
            {
                Collider[] hits = Physics.OverlapSphere(castPosition, moonRadius, LayerMask.GetMask("Player"));
                for (int h = 0; h < hits.Length; h++)
                {
                    PlayerStats ps = hits[h].GetComponentInParent<PlayerStats>();
                    if (ps != null)
                        DamageRelay.ApplyToPlayer(ps.gameObject, moonTickDamage);
                }

                yield return new WaitForSeconds(tickInterval);
            }

            yield return StartCoroutine(DoStoppageAndRecovery(lockedRotation, moonStoppageTime, moonRecoveryTime));
        }
        finally
        {
            if (isBusy) EndAction();
            activeAbility = null;
        }
    }

    private bool CanDive()
    {
        if (Time.time - lastDiveTime < diveCooldown) return false;
        return blackboard.Get<Transform>("target") != null;
    }

    private void StartDive()
    {
        if (activeAbility != null) return;
        lastDiveTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoDive());
    }

    private IEnumerator CoDive()
    {
        BeginAction(AIState.Special1);
        Transform target = blackboard.Get<Transform>("target");
        Vector3 diveDirection = transform.forward;

        if (target != null)
        {
            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                diveDirection = toTarget.normalized;
                transform.rotation = Quaternion.LookRotation(diveDirection);
            }
        }

        try
        {
            if (HasTrigger(diveWindupTrigger)) SetTriggerSync(diveWindupTrigger);
            else if (HasTrigger(diveTrigger)) SetTriggerSync(diveTrigger);
            PlaySfx(diveWindupSFX);
            SpawnTimedVFX(diveWindupVFX, diveVFXOffset, diveVFXScale, diveWindup + 0.25f);

            float windup = Mathf.Max(0f, diveWindup);
            while (windup > 0f)
            {
                windup -= Time.deltaTime;
                transform.rotation = Quaternion.LookRotation(diveDirection);
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                yield return null;
            }

            float ascend = Mathf.Max(0f, diveAscendTime);
            while (ascend > 0f)
            {
                ascend -= Time.deltaTime;
                if (controller != null && controller.enabled && diveAscendSpeed > 0f)
                    controller.Move(Vector3.up * diveAscendSpeed * Time.deltaTime);
                yield return null;
            }

            if (target != null)
            {
                Vector3 toTarget = target.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f)
                    diveDirection = toTarget.normalized;
            }

            if (HasTrigger(diveTrigger)) SetTriggerSync(diveTrigger);
            HashSet<PlayerStats> hitPlayers = new HashSet<PlayerStats>();
            float travel = Mathf.Max(0.1f, diveTravelTime);
            while (travel > 0f)
            {
                travel -= Time.deltaTime;
                transform.rotation = Quaternion.LookRotation(diveDirection);
                if (controller != null && controller.enabled)
                    controller.Move(diveDirection * diveDescendSpeed * Time.deltaTime);

                Collider[] hits = Physics.OverlapSphere(transform.position, diveHitRadius, LayerMask.GetMask("Player"));
                for (int i = 0; i < hits.Length; i++)
                {
                    PlayerStats ps = hits[i].GetComponentInParent<PlayerStats>();
                    if (ps == null || hitPlayers.Contains(ps)) continue;
                    hitPlayers.Add(ps);
                    DamageRelay.ApplyToPlayer(ps.gameObject, diveDamage);
                }

                yield return null;
            }

            PlaySfx(diveImpactSFX);
            SpawnTimedVFX(diveImpactVFX, diveVFXOffset, diveVFXScale, 1.2f);

            Quaternion lockedRotation = transform.rotation;
            yield return StartCoroutine(DoStoppageAndRecovery(lockedRotation, diveStoppageTime, diveRecoveryTime));
        }
        finally
        {
            if (isBusy) EndAction();
            activeAbility = null;
        }
    }

    private IEnumerator DoStoppageAndRecovery(Quaternion lockedRotation, float stoppageTime, float recoveryTime)
    {
        if (stoppageTime > 0f)
        {
            float timer = stoppageTime;
            float exhaustedStart = stoppageTime * 0.75f;
            while (timer > 0f)
            {
                timer -= Time.deltaTime;
                transform.rotation = lockedRotation;
                if (controller != null && controller.enabled)
                    controller.SimpleMove(Vector3.zero);

                if (timer <= exhaustedStart && HasBool("Exhausted"))
                    SetBoolSync("Exhausted", true);

                yield return null;
            }
            if (HasBool("Exhausted"))
                SetBoolSync("Exhausted", false);
        }

        if (recoveryTime > 0f)
            yield return new WaitForSeconds(recoveryTime);
    }

    private GameObject SpawnProjectile(string resourcePath, Vector3 position, Quaternion rotation)
    {
        if (string.IsNullOrEmpty(resourcePath))
            return null;

        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            return PhotonNetwork.Instantiate(resourcePath, position, rotation);

        GameObject prefab = Resources.Load<GameObject>(resourcePath);
        if (prefab == null)
        {
            Debug.LogWarning("[MinokawaAI] Missing projectile prefab at Resources path: " + resourcePath);
            return null;
        }

        return Instantiate(prefab, position, rotation);
    }

    private void SpawnTimedVFX(GameObject prefab, Vector3 localOffset, float scale, float lifetime)
    {
        if (prefab == null) return;
        GameObject fx = Instantiate(prefab, transform);
        fx.transform.localPosition = localOffset;
        fx.transform.localRotation = Quaternion.identity;
        if (scale > 0f)
            fx.transform.localScale = Vector3.one * scale;
        Destroy(fx, Mathf.Max(0.1f, lifetime));
    }

    private void SpawnWorldTimedVFX(GameObject prefab, Vector3 worldPos, Quaternion worldRot, float lifetime)
    {
        if (prefab == null) return;
        GameObject fx = Instantiate(prefab, worldPos, worldRot);
        Destroy(fx, Mathf.Max(0.1f, lifetime));
    }
}
