using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class SigbinAI : BaseEnemyAI
{
    [Header("Backstep Slash")]
    public int backstepDamage = 28;
    public float backstepRange = 1.4f;
    public float backstepWindup = 0.35f;
    public float backstepCooldown = 7f;
    public float backstepSpeed = 7f;
    public float backstepDuration = 0.2f;
    public GameObject backstepWindupVFX;
    public GameObject backstepImpactVFX;
    public Vector3 backstepWindupVFXOffset = Vector3.zero;
    public float backstepWindupVFXScale = 1.0f;
    public Vector3 backstepImpactVFXOffset = Vector3.zero;
    public float backstepImpactVFXScale = 1.0f;
    public AudioClip backstepWindupSFX;
    public AudioClip backstepImpactSFX;
    public string backstepWindupTrigger = "BackstepWindup";
    public string backstepTrigger = "Backstep";
    public string skillStoppageTrigger = "SkillStoppage";

    [Header("Skill Selection Tuning")]
    public float backstepStoppageTime = 1f;
    public float backstepRecoveryTime = 0.5f;

    private float lastBackstepTime = -9999f;
    private float lastAnySkillRecoveryEnd = -9999f;
    private float lastAnySkillRecoveryStart = -9999f;
    private Coroutine activeAbility;
    private Coroutine basicRoutine;
    private float activeAbilityFailSafeUntil = -1f;
    private float basicAttackFailSafeUntil = -1f;

    public float BackstepCooldownRemaining => Mathf.Max(0f, backstepCooldown - (Time.time - lastBackstepTime));

    // Debug: expose state for overhead display
    public bool DebugHasActiveAbility => activeAbility != null;
    public bool DebugHasBasicRoutine => basicRoutine != null;
    public float DebugMoveSpeed => GetMoveSpeed();

    public override string GetEffectiveStateForDebug()
    {
        if (activeAbility != null) return "Backstep";
        if (basicRoutine != null) return "BasicAttack";
        return base.GetEffectiveStateForDebug();
    }

    protected override void InitializeEnemy() { }

    protected override void Update()
    {
        base.Update();
        if (isDead) return;
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient) return;
        ApplyFailSafeRecovery();
    }

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTarget, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");
        var targetInAttack = new ConditionNode(blackboard, TargetInAttackRangeAndFacing, "in_attack_range_facing");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");
        var canBackstep = new ConditionNode(blackboard, CanBackstep, "can_backstep");
        var doBackstep = new ActionNode(blackboard, () => { StartBackstep(); return NodeState.Success; }, "backstep");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "backstep_seq").Add(canBackstep, doBackstep),
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

        basicAttackFailSafeUntil = Time.time + Mathf.Max(2f, enemyData.attackWindup + enemyData.attackMoveLock + 2f);
        basicRoutine = StartCoroutine(CoBasicAttack(target));
    }

    private IEnumerator CoBasicAttack(Transform target)
    {
        BeginAction(AIState.BasicAttack);
        Quaternion lockedRotation = transform.rotation;

        if (HasTrigger(attackWindupTrigger))
            SetTriggerSync(attackWindupTrigger);
        else if (HasTrigger(attackTrigger))
            SetTriggerSync(attackTrigger);
        PlayAttackWindupSfx();

        float windup = Mathf.Max(0f, enemyData.attackWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            transform.rotation = lockedRotation;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }

        if (HasTrigger(attackImpactTrigger))
            SetTriggerSync(attackImpactTrigger);
        PlayAttackImpactSfx();

        float radius = Mathf.Max(0.8f, enemyData.attackRange);
        Vector3 center = transform.position + transform.forward * (enemyData.attackRange * 0.5f);
        var cols = Physics.OverlapSphere(center, radius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, enemyData.basicDamage);
        }

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
        basicAttackFailSafeUntil = -1f;
        EndAction();
    }

    protected override bool TrySpecialAbilities()
    {
        return false;
    }

    private bool CanBackstep()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (Time.time - lastBackstepTime < backstepCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;

        float distance = Vector3.Distance(transform.position, target.position);
        return distance <= backstepRange + 2.5f;
    }

    private void StartBackstep()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbilityFailSafeUntil = Time.time + Mathf.Max(3f, backstepWindup + backstepDuration + backstepStoppageTime + backstepRecoveryTime + 2f);
        activeAbility = StartCoroutine(CoBackstep());
    }

    private IEnumerator CoBackstep()
    {
        BeginAction(AIState.Special1);
        bool actionEnded = false;
        try
        {
            // Capture backstep direction before windup (match Tiyanak pattern)
            var target = blackboard.Get<Transform>("target");
            Vector3 backstepDirection = -transform.forward;
            Vector3 faceDirection = transform.forward;
            if (target != null)
            {
                Vector3 toTarget = new Vector3(target.position.x, transform.position.y, target.position.z) - transform.position;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    faceDirection = toTarget.normalized;
                    backstepDirection = -faceDirection;
                    transform.rotation = Quaternion.LookRotation(faceDirection);
                }
            }

            if (HasTrigger(backstepWindupTrigger)) SetTriggerSync(backstepWindupTrigger);
            else if (HasTrigger(backstepTrigger)) SetTriggerSync(backstepTrigger);
            PlaySfx(backstepWindupSFX);
            GameObject wind = null;
            if (backstepWindupVFX != null)
            {
                wind = Instantiate(backstepWindupVFX, transform);
                wind.transform.localPosition = backstepWindupVFXOffset;
                if (backstepWindupVFXScale > 0f) wind.transform.localScale = Vector3.one * backstepWindupVFXScale;
            }

            // Windup phase - lock rotation (match Tiyanak)
            float windup = Mathf.Max(0f, backstepWindup);
            while (windup > 0f)
            {
                windup -= Time.deltaTime;
                transform.rotation = Quaternion.LookRotation(faceDirection);
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                yield return null;
            }
            if (wind != null) Destroy(wind);

            if (HasTrigger(backstepTrigger)) SetTriggerSync(backstepTrigger);

            // Backstep movement (match Tiyanak lunge pattern - controller.Move with world-space direction)
            float t = Mathf.Max(0.05f, backstepDuration);
            HashSet<PlayerStats> hitPlayers = new HashSet<PlayerStats>();
            while (t > 0f)
            {
                t -= Time.deltaTime;
                if (controller != null && controller.enabled)
                    controller.Move(backstepDirection * backstepSpeed * Time.deltaTime);

                if (target != null && Vector3.Distance(transform.position, target.position) <= backstepRange)
                {
                    var ps = target.GetComponent<PlayerStats>();
                    if (ps != null && !hitPlayers.Contains(ps))
                    {
                        DamageRelay.ApplyToPlayer(ps.gameObject, backstepDamage);
                        hitPlayers.Add(ps);
                    }
                }
                yield return null;
            }

            if (backstepImpactVFX != null)
            {
                var fx = Instantiate(backstepImpactVFX, transform);
                fx.transform.localPosition = backstepImpactVFXOffset;
                if (backstepImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * backstepImpactVFXScale;
            }
            PlaySfx(backstepImpactSFX);

            if (backstepStoppageTime > 0f)
            {
                if (HasTrigger(skillStoppageTrigger)) SetTriggerSync(skillStoppageTrigger);
                SetBoolSync("Exhausted", true);
                float stopTimer = backstepStoppageTime;
                while (stopTimer > 0f)
                {
                    stopTimer -= Time.deltaTime;
                    if (controller != null && controller.enabled)
                        controller.SimpleMove(Vector3.zero);
                    yield return null;
                }
                SetBoolSync("Exhausted", false);
            }

            EndAction();
            actionEnded = true;

            if (backstepRecoveryTime > 0f)
            {
                lastAnySkillRecoveryStart = Time.time;
                float recovery = backstepRecoveryTime;
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

            lastBackstepTime = Time.time;
        }
        finally
        {
            SetBoolSync("Exhausted", false);
            if (!actionEnded && isBusy)
                EndAction();
            activeAbility = null;
            activeAbilityFailSafeUntil = -1f;
        }
    }

    protected override float GetMoveSpeed()
    {
        if (isBusy || globalBusyTimer > 0f || activeAbility != null || basicRoutine != null)
        {
            return 0f;
        }

        if (aiState == AIState.Idle)
        {
            return 0f;
        }

        float baseSpeed = base.GetMoveSpeed();

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

    private void ApplyFailSafeRecovery()
    {
        if (isBusy && activeAbility == null && basicRoutine == null)
        {
            SetBoolSync("Exhausted", false);
            EndAction();
        }

        if (basicRoutine != null && basicAttackFailSafeUntil > 0f && Time.time > basicAttackFailSafeUntil)
        {
            StopCoroutine(basicRoutine);
            basicRoutine = null;
            basicAttackFailSafeUntil = -1f;
            if (isBusy) EndAction();
        }

        if (activeAbility != null && activeAbilityFailSafeUntil > 0f && Time.time > activeAbilityFailSafeUntil)
        {
            StopCoroutine(activeAbility);
            activeAbility = null;
            activeAbilityFailSafeUntil = -1f;
            SetBoolSync("Exhausted", false);
            if (isBusy) EndAction();
            lastAnySkillRecoveryEnd = Time.time;
        }
    }
}
