using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class WakwakAI : BaseEnemyAI
{
    [Header("Silent Descent (dive strike)")]
    public int descentDamage = 40;
    public float descentHitRadius = 1.6f;
    public float descentWindup = 0.5f;
    public float descentCooldown = 8f;
    public float descentSpeed = 18f;
    public float descentDuration = 0.8f;
    public GameObject descentWindupVFX;
    public GameObject descentImpactVFX;
    public Vector3 descentWindupVFXOffset = Vector3.zero;
    public float descentWindupVFXScale = 1.0f;
    public Vector3 descentImpactVFXOffset = Vector3.zero;
    public float descentImpactVFXScale = 1.0f;
    public AudioClip descentWindupSFX;
    public AudioClip descentImpactSFX;
    public string descentWindupTrigger = "DescentWindup";
    public string descentTrigger = "Descent";
    public string skillStoppageTrigger = "SkillStoppage";

    [Header("Skill Selection Tuning")]
    public float descentStoppageTime = 1f;
    public float descentRecoveryTime = 0.5f;

    private float lastDescentTime = -9999f;
    private float lastAnySkillRecoveryEnd = -9999f;
    private float lastAnySkillRecoveryStart = -9999f;
    private Coroutine activeAbility;
    private Coroutine basicRoutine;

    // Debug accessors
    public float DescentCooldownRemaining => Mathf.Max(0f, descentCooldown - (Time.time - lastDescentTime));

    protected override void InitializeEnemy() { }

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTarget, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");
        var targetInAttack = new ConditionNode(blackboard, TargetInAttackRangeAndFacing, "in_attack_range_facing");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");
        var canDescent = new ConditionNode(blackboard, CanDescent, "can_descent");
        var doDescent = new ActionNode(blackboard, () => { StartDescent(); return NodeState.Success; }, "descent");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "descent_seq").Add(canDescent, doDescent),
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

    #region Silent Descent

    private bool CanDescent()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (Time.time - lastDescentTime < descentCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance >= 5f && distance <= 12f;
    }

    private void StartDescent()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoDescent());
    }

    private IEnumerator CoDescent()
    {
        BeginAction(AIState.Special1);

        // Capture dive direction before windup
        var target = blackboard.Get<Transform>("target");
        Vector3 diveDirection = transform.forward;
        if (target != null)
        {
            Vector3 toTarget = new Vector3(target.position.x, transform.position.y, target.position.z) - transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                diveDirection = toTarget.normalized;
                transform.rotation = Quaternion.LookRotation(diveDirection);
            }
        }

        // Windup animation trigger (sync to network)
        if (HasTrigger(descentWindupTrigger)) SetTriggerSync(descentWindupTrigger);
        else if (HasTrigger(descentTrigger)) SetTriggerSync(descentTrigger);
        PlaySfx(descentWindupSFX);
        GameObject wind = null;
        if (descentWindupVFX != null)
        {
            wind = Instantiate(descentWindupVFX, transform);
            wind.transform.localPosition = descentWindupVFXOffset;
            if (descentWindupVFXScale > 0f) wind.transform.localScale = Vector3.one * descentWindupVFXScale;
        }

        // Windup phase - lock rotation
        float windup = Mathf.Max(0f, descentWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            transform.rotation = Quaternion.LookRotation(diveDirection);
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        if (wind != null) Destroy(wind);

        // Descent animation trigger (sync to network)
        if (HasTrigger(descentTrigger)) SetTriggerSync(descentTrigger);

        // Descend forward - move and check for hits
        float travel = Mathf.Max(0.05f, descentDuration);
        HashSet<PlayerStats> hitPlayers = new HashSet<PlayerStats>();
        while (travel > 0f)
        {
            travel -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.Move(diveDirection * descentSpeed * Time.deltaTime);

            // Check for hits during descent - ONE DAMAGE PER PLAYER
            var hits = Physics.OverlapSphere(transform.position, descentHitRadius, LayerMask.GetMask("Player"));
            foreach (var h in hits)
            {
                var ps = h.GetComponentInParent<PlayerStats>();
                if (ps != null && !hitPlayers.Contains(ps))
                {
                    DamageRelay.ApplyToPlayer(ps.gameObject, descentDamage);
                    hitPlayers.Add(ps);
                }
            }

            yield return null;
        }

        // Impact VFX/SFX
        if (descentImpactVFX != null)
        {
            var fx = Instantiate(descentImpactVFX, transform);
            fx.transform.localPosition = descentImpactVFXOffset;
            if (descentImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * descentImpactVFXScale;
        }
        PlaySfx(descentImpactSFX);

        // Stoppage recovery (AI frozen after attack)
        if (descentStoppageTime > 0f)
        {
            if (HasTrigger(skillStoppageTrigger)) SetTriggerSync(skillStoppageTrigger);

            float stopTimer = descentStoppageTime;
            float quarterStoppage = descentStoppageTime * 0.75f;

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
        if (descentRecoveryTime > 0f)
        {
            lastAnySkillRecoveryStart = Time.time;
            float recovery = descentRecoveryTime;
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
        lastDescentTime = Time.time;
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
