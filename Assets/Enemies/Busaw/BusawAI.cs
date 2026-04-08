using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class BusawAI : BaseEnemyAI
{
    [Header("Graveyard Grasp (AOE)")]
    public int graspDamage = 15;
    public float graspRadius = 4.0f;
    public float graspWindup = 0.6f;
    public float graspCooldown = 9f;
    public GameObject graspWindupVFX;
    public GameObject graspImpactVFX;
    public Vector3 graspWindupVFXOffset = Vector3.zero;
    public float graspWindupVFXScale = 1.0f;
    public Vector3 graspImpactVFXOffset = Vector3.zero;
    public float graspImpactVFXScale = 1.0f;
    public AudioClip graspWindupSFX;
    public AudioClip graspImpactSFX;
    public string graspWindupTrigger = "GraspWindup";
    public string graspMainTrigger = "GraspMain";
    public string skillStoppageTrigger = "SkillStoppage";

    [Header("Skill Selection Tuning")]
    public float graspStoppageTime = 1f;
    public float graspRecoveryTime = 0.5f;

    private float lastGraspTime = -9999f;
    private float lastAnySkillRecoveryEnd = -9999f;
    private float lastAnySkillRecoveryStart = -9999f;
    private Coroutine activeAbility;
    private Coroutine basicRoutine;

    // Debug accessors
    public float GraspCooldownRemaining => Mathf.Max(0f, graspCooldown - (Time.time - lastGraspTime));

    protected override void InitializeEnemy() { }

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTarget, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");
        var targetInAttack = new ConditionNode(blackboard, TargetInAttackRangeAndFacing, "in_attack_range_facing");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");
        var canGrasp = new ConditionNode(blackboard, CanGrasp, "can_grasp");
        var doGrasp = new ActionNode(blackboard, () => { StartGrasp(); return NodeState.Success; }, "grasp");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "grasp_seq").Add(canGrasp, doGrasp),
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

    #region Graveyard Grasp

    private bool CanGrasp()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (Time.time - lastGraspTime < graspCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance <= graspRadius + 1f;
    }

    private void StartGrasp()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoGrasp());
    }

    private IEnumerator CoGrasp()
    {
        BeginAction(AIState.Special1);

        // Windup phase - separate trigger, VFX, and SFX (sync to network)
        if (HasTrigger(graspWindupTrigger))
            SetTriggerSync(graspWindupTrigger);
        else if (HasTrigger(graspMainTrigger))
            SetTriggerSync(graspMainTrigger);
        PlaySfx(graspWindupSFX);
        GameObject wind = null;
        if (graspWindupVFX != null)
        {
            wind = Instantiate(graspWindupVFX, transform);
            wind.transform.localPosition = graspWindupVFXOffset;
            if (graspWindupVFXScale > 0f)
                wind.transform.localScale = Vector3.one * graspWindupVFXScale;
        }

        // Windup phase - freeze movement (NO facing for regular attacks)
        float windup = Mathf.Max(0f, graspWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        if (wind != null) Destroy(wind);

        // Impact/Main phase - separate trigger, VFX, and SFX (sync to network)
        if (HasTrigger(graspMainTrigger))
            SetTriggerSync(graspMainTrigger);

        if (graspImpactVFX != null)
        {
            var fx = Instantiate(graspImpactVFX, transform);
            fx.transform.localPosition = graspImpactVFXOffset;
            if (graspImpactVFXScale > 0f)
                fx.transform.localScale = Vector3.one * graspImpactVFXScale;
        }
        PlaySfx(graspImpactSFX);

        // Apply damage
        var cols = Physics.OverlapSphere(transform.position, graspRadius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
        if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, graspDamage);
        }

        // Stoppage recovery (AI frozen after attack)
        if (graspStoppageTime > 0f)
        {
            if (HasTrigger(skillStoppageTrigger)) SetTriggerSync(skillStoppageTrigger);

            float stopTimer = graspStoppageTime;
            float quarterStoppage = graspStoppageTime * 0.75f;

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
        if (graspRecoveryTime > 0f)
        {
            lastAnySkillRecoveryStart = Time.time;
            float recovery = graspRecoveryTime;
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
        lastGraspTime = Time.time;
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
