using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class SirenaAI : BaseEnemyAI
{
    [Header("Water Burst (AOE)")]
    public int burstDamage = 12;
    public float burstRadius = 3.5f;
    public float burstWindup = 0.5f;
    public float burstCooldown = 7f;
    public GameObject burstWindupVFX;
    public GameObject burstImpactVFX;
    public Vector3 burstWindupVFXOffset = Vector3.zero;
    public float burstWindupVFXScale = 1.0f;
    public Vector3 burstImpactVFXOffset = Vector3.zero;
    public float burstImpactVFXScale = 1.0f;
    public AudioClip burstWindupSFX;
    public AudioClip burstImpactSFX;
    public string burstWindupTrigger = "BurstWindup";
    public string burstMainTrigger = "BurstMain";
    public string skillStoppageTrigger = "SkillStoppage";

    [Header("Skill Selection Tuning")]
    public float burstStoppageTime = 1f;
    public float burstRecoveryTime = 0.5f;

    private float lastBurstTime = -9999f;
    private float lastAnySkillRecoveryEnd = -9999f;
    private float lastAnySkillRecoveryStart = -9999f;
    private Coroutine activeAbility;
    private Coroutine basicRoutine;

    // Debug accessors
    public float BurstCooldownRemaining => Mathf.Max(0f, burstCooldown - (Time.time - lastBurstTime));

    protected override void InitializeEnemy() { }

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTarget, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");
        var targetInAttack = new ConditionNode(blackboard, TargetInAttackRangeAndFacing, "in_attack_range_facing");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");
        var canBurst = new ConditionNode(blackboard, CanBurst, "can_burst");
        var doBurst = new ActionNode(blackboard, () => { StartBurst(); return NodeState.Success; }, "burst");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "burst_seq").Add(canBurst, doBurst),
                        new Sequence(blackboard, "basic_seq").Add(targetInAttack, basicAttack),
                        moveToTarget
                    )
                ),
                new ActionNode(blackboard, Patrol, "patrol")
            );
    }

    protected override void PerformBasicAttack()
    {
        if (basicRoutine != null) return;
        if (activeAbility != null) return;
        if (isBusy || globalBusyTimer > 0f) return;
        if (enemyData == null) return;
        if (Time.time - lastAttackTime < enemyData.attackCooldown) return;

        var target = blackboard.Get<Transform>("target");
        if (target == null) return;

        basicRoutine = StartCoroutine(CoBasicAttack(target));
    }

    private IEnumerator CoBasicAttack(Transform target)
    {
        BeginAction(AIState.BasicAttack);
        Quaternion lockedRotation = transform.rotation;

        // Windup animation trigger (sync to network)
        if (HasTrigger(attackWindupTrigger))
            SetTriggerSync(attackWindupTrigger);
        else if (HasTrigger(attackTrigger))
            SetTriggerSync(attackTrigger);
        PlayAttackWindupSfx();

        // Windup phase - lock position and rotation
        float windup = Mathf.Max(0f, enemyData.attackWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            transform.rotation = lockedRotation;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }

        // Impact animation trigger (sync to network)
        if (HasTrigger(attackImpactTrigger))
            SetTriggerSync(attackImpactTrigger);
        PlayAttackImpactSfx();

        // Apply damage after windup
        float radius = Mathf.Max(0.8f, enemyData.attackRange);
        Vector3 center = transform.position + transform.forward * (enemyData.attackRange * 0.5f);
        var cols = Physics.OverlapSphere(center, radius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, enemyData.basicDamage);
        }

        // Exhausted phase - lock position, rotation, set Exhausted animator
        float post = Mathf.Max(0.1f, enemyData.attackMoveLock);
        if (post > 0f && HasBool("Exhausted")) SetBoolSync("Exhausted", true);
        while (post > 0f)
        {
            post -= Time.deltaTime;
            transform.rotation = lockedRotation;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        if (HasBool("Exhausted")) SetBoolSync("Exhausted", false);

        lastAttackTime = Time.time;
        attackLockTimer = enemyData.attackMoveLock;
        basicRoutine = null;
        EndAction();
    }

    protected override bool TrySpecialAbilities()
    {
        return false;
    }

    #region Water Burst

    private bool CanBurst()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (Time.time - lastBurstTime < burstCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance <= burstRadius;
    }

    private void StartBurst()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoBurst());
    }

    private IEnumerator CoBurst()
    {
        BeginAction(AIState.Special1);

        // Windup phase - separate trigger, VFX, and SFX (sync to network)
        if (HasTrigger(burstWindupTrigger))
            SetTriggerSync(burstWindupTrigger);
        PlaySfx(burstWindupSFX);
        GameObject wind = null;
        if (burstWindupVFX != null)
        {
            wind = Instantiate(burstWindupVFX, transform);
            wind.transform.localPosition = burstWindupVFXOffset;
            if (burstWindupVFXScale > 0f)
                wind.transform.localScale = Vector3.one * burstWindupVFXScale;
        }

        // Windup phase - freeze movement (NO facing for regular attacks)
        float windup = Mathf.Max(0f, burstWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        if (wind != null) Destroy(wind);

        // Impact/Main phase - separate trigger, VFX, and SFX (sync to network)
        if (HasTrigger(burstMainTrigger))
            SetTriggerSync(burstMainTrigger);

        if (burstImpactVFX != null)
        {
            var fx = Instantiate(burstImpactVFX, transform);
            fx.transform.localPosition = burstImpactVFXOffset;
            if (burstImpactVFXScale > 0f)
                fx.transform.localScale = Vector3.one * burstImpactVFXScale;
        }
        PlaySfx(burstImpactSFX);

        // Apply damage
        var cols = Physics.OverlapSphere(transform.position, burstRadius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
        if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, burstDamage);
        }

        // Stoppage recovery (AI frozen after attack)
        if (burstStoppageTime > 0f)
        {
            if (HasTrigger(skillStoppageTrigger)) SetTriggerSync(skillStoppageTrigger);

            float stopTimer = burstStoppageTime;
            float quarterStoppage = burstStoppageTime * 0.75f;

            while (stopTimer > 0f)
            {
                stopTimer -= Time.deltaTime;
                if (controller != null && controller.enabled)
                    controller.SimpleMove(Vector3.zero);

                // Set Exhausted boolean parameter when 75% of stoppage time remains (skills only)
                if (stopTimer <= quarterStoppage)
                {
                    SetBoolSync("Exhausted", true);
                }

                yield return null;
            }

            // Clear Exhausted boolean parameter
            SetBoolSync("Exhausted", false);
        }

        // End busy state so AI can move during recovery
        EndAction();

        // Recovery time (AI can move but skill still on cooldown, gradual speed recovery)
        if (burstRecoveryTime > 0f)
        {
            lastAnySkillRecoveryStart = Time.time;
            float recovery = burstRecoveryTime;
            while (recovery > 0f)
            {
                recovery -= Time.deltaTime;
                yield return null;
            }
            lastAnySkillRecoveryEnd = Time.time;
        }
        else
        {
            lastAnySkillRecoveryEnd = Time.time;
        }

        activeAbility = null;
        lastBurstTime = Time.time;
    }

    #endregion

    protected override float GetMoveSpeed()
    {
        // Return 0 if AI is busy or has active ability (should be stopped)
        if (isBusy || globalBusyTimer > 0f || activeAbility != null || basicRoutine != null)
        {
            return 0f;
        }

        // If AI is idle (not patrolling or chasing), return 0
        if (aiState == AIState.Idle)
        {
            return 0f;
        }

        float baseSpeed = base.GetMoveSpeed();

        // If we're in recovery phase, gradually increase speed from 0.3 to 1.0
        if (Time.time >= lastAnySkillRecoveryStart && Time.time <= lastAnySkillRecoveryEnd && lastAnySkillRecoveryStart >= 0f)
        {
            float recoveryDuration = lastAnySkillRecoveryEnd - lastAnySkillRecoveryStart;
            if (recoveryDuration > 0f)
            {
                float elapsed = Time.time - lastAnySkillRecoveryStart;
                float progress = Mathf.Clamp01(elapsed / recoveryDuration);
                float speedMultiplier = Mathf.Lerp(0.3f, 1.0f, progress);
                return baseSpeed * speedMultiplier;
            }
        }

        return baseSpeed;
    }
}
