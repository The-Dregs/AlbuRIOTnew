using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class ManananggalAI : BaseEnemyAI
{
    [Header("Shadow Dive (heavy dive strike)")]
    public int diveDamage = 45;
    public float diveHitRadius = 1.6f;
    public float diveWindup = 0.6f;
    public float diveCooldown = 8f;
    public float diveAscendTime = 0.35f;
    [Tooltip("How fast the Manananggal rises during the ascend phase (vertical speed). 0 = no vertical rise.")]
    public float diveAscendSpeed = 4f;
    public float diveDescendSpeed = 18f;
    public GameObject diveWindupVFX;
    public GameObject diveImpactVFX;
    public Vector3 diveVFXOffset = Vector3.zero;
    public float diveVFXScale = 1.0f;
    public AudioClip diveWindupSFX;
    public AudioClip diveImpactSFX;
    [Tooltip("Volume multiplier for Shadow Dive SFX (windup/impact).")]
    [Range(0f, 1f)] public float diveSfxVolume = 1f;
    public string diveWindupTrigger = "DiveWindup";
    public string diveTrigger = "Dive";
    public string skillStoppageTrigger = "SkillStoppage";

    [Header("Exhausted Timers")]
    [Tooltip("How long the Manananggal is locked (exhausted) after a basic attack")]
    public float basicAttackExhaustedTime = 0.5f;
    [Tooltip("How long the Manananggal is locked (exhausted) in the dive landing pose after a dive")]
    public float diveExhaustedTime = 1f;

    [Header("Skill Selection Tuning")]
    public float diveStoppageTime = 1f;
    [Tooltip("After the exhausted phase: how long the Manananggal moves at reduced speed (0.3x→1.0x ramp) before full speed. During this time they can move but are still 'recovering'.")]
    public float diveRecoveryTime = 0.5f;

    private float lastDiveTime = -9999f;
    private float lastAnySkillRecoveryEnd = -9999f;
    private float lastAnySkillRecoveryStart = -9999f;
    private Coroutine activeAbility;
    private Coroutine basicRoutine;
    private float activeAbilityFailSafeUntil = -1f;
    private float basicAttackFailSafeUntil = -1f;
    private Coroutine diveSfxFadeCoroutine;

    // Debug accessors
    public float DiveCooldownRemaining => Mathf.Max(0f, diveCooldown - (Time.time - lastDiveTime));

    public override string GetEffectiveStateForDebug()
    {
        if (activeAbility != null) return "Dive";
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
        var canDive = new ConditionNode(blackboard, CanDive, "can_dive");
        var doDive = new ActionNode(blackboard, () => { StartDive(); return NodeState.Success; }, "dive");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "dive_seq").Add(canDive, doDive),
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

        basicAttackFailSafeUntil = Time.time + Mathf.Max(2f, enemyData.attackWindup + basicAttackExhaustedTime + 2f);
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
        float exhausted = Mathf.Max(0.1f, basicAttackExhaustedTime);
        if (exhausted > 0f && HasBool("Exhausted")) SetBoolSync("Exhausted", true);
        while (exhausted > 0f)
        {
            exhausted -= Time.deltaTime;
            transform.rotation = lockedRotation;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        if (HasBool("Exhausted")) SetBoolSync("Exhausted", false);

        lastAttackTime = Time.time;
        attackLockTimer = basicAttackExhaustedTime;
        basicRoutine = null;
        basicAttackFailSafeUntil = -1f;
        EndAction();
        if (basicAttackExhaustedTime > 0f)
            globalBusyTimer = Mathf.Max(globalBusyTimer, basicAttackExhaustedTime);
    }

    protected override bool TrySpecialAbilities()
    {
        return false;
    }

    #region Shadow Dive

    private bool CanDive()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (Time.time - lastDiveTime < diveCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance >= 6f && distance <= 12f;
    }

    private void StartDive()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbilityFailSafeUntil = Time.time + Mathf.Max(3f, diveWindup + diveAscendTime + 0.8f + diveExhaustedTime + diveRecoveryTime + 2f);
        activeAbility = StartCoroutine(CoDive());
    }

    private IEnumerator CoDive()
    {
        BeginAction(AIState.Special1);
        bool actionEnded = false;
        try
        {
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
            if (HasTrigger(diveWindupTrigger)) SetTriggerSync(diveWindupTrigger);
            else if (HasTrigger(diveTrigger)) SetTriggerSync(diveTrigger);
            PlaySfx(diveWindupSFX, diveSfxVolume);
            GameObject wind = null;
            if (diveWindupVFX != null)
            {
                wind = Instantiate(diveWindupVFX, transform);
                wind.transform.localPosition = diveVFXOffset;
                if (diveVFXScale > 0f) wind.transform.localScale = Vector3.one * diveVFXScale;
            }

            // Windup phase - lock rotation
            float windup = Mathf.Max(0f, diveWindup);
            while (windup > 0f)
            {
                windup -= Time.deltaTime;
                transform.rotation = Quaternion.LookRotation(diveDirection);
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                yield return null;
            }
            if (wind != null) Destroy(wind);

            // Ascend phase - fly up
            float ascend = Mathf.Max(0f, diveAscendTime);
            while (ascend > 0f)
            {
                ascend -= Time.deltaTime;
                if (controller != null && controller.enabled && diveAscendSpeed > 0f)
                    controller.Move(Vector3.up * diveAscendSpeed * Time.deltaTime);
                yield return null;
            }

            // Update dive direction to current target position
            if (target != null)
            {
                Vector3 toTarget = new Vector3(target.position.x, transform.position.y, target.position.z) - transform.position;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    diveDirection = toTarget.normalized;
                }
            }

            // Dive animation trigger (sync to network)
            if (HasTrigger(diveTrigger)) SetTriggerSync(diveTrigger);

            // Descend phase - move forward, lock rotation, and check for hits
            float travel = 0.8f;
            HashSet<PlayerStats> hitPlayers = new HashSet<PlayerStats>();
            while (travel > 0f)
            {
                travel -= Time.deltaTime;
                transform.rotation = Quaternion.LookRotation(diveDirection);
                if (controller != null && controller.enabled)
                    controller.Move(diveDirection * diveDescendSpeed * Time.deltaTime);

                // Check for hits during dive - ONE DAMAGE PER PLAYER
                var hits = Physics.OverlapSphere(transform.position, diveHitRadius, LayerMask.GetMask("Player"));
                foreach (var h in hits)
                {
                    var ps = h.GetComponentInParent<PlayerStats>();
                    if (ps != null && !hitPlayers.Contains(ps))
                    {
                        DamageRelay.ApplyToPlayer(ps.gameObject, diveDamage);
                        hitPlayers.Add(ps);
                    }
                }

                yield return null;
            }

            // Impact VFX/SFX
            if (diveImpactVFX != null)
            {
                var fx = Instantiate(diveImpactVFX, transform);
                fx.transform.localPosition = diveVFXOffset;
                if (diveVFXScale > 0f) fx.transform.localScale = Vector3.one * diveVFXScale;
            }
            PlaySfx(diveImpactSFX, diveSfxVolume);

            // Exhausted phase - lock position and model in dive landing pose
            float exhaustedDuration = Mathf.Max(0f, diveExhaustedTime > 0f ? diveExhaustedTime : diveStoppageTime);
            if (exhaustedDuration > 0f)
            {
                SetBoolSync("Exhausted", true);
                Quaternion lockedRotation = transform.rotation;
                float stopTimer = exhaustedDuration;
                while (stopTimer > 0f)
                {
                    stopTimer -= Time.deltaTime;
                    transform.rotation = lockedRotation;
                    if (controller != null && controller.enabled)
                        controller.SimpleMove(Vector3.zero);
                    yield return null;
                }
                SetBoolSync("Exhausted", false);
            }

            // End busy state so AI can move during recovery
            EndAction();
            actionEnded = true;

            // Recovery time (AI can move but skill still on cooldown, gradual speed recovery)
            if (diveRecoveryTime > 0f)
            {
                lastAnySkillRecoveryStart = Time.time;
                float recovery = diveRecoveryTime;
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

            lastDiveTime = Time.time;
            FadeOutDiveSfx();
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

    private void FadeOutDiveSfx(float duration = 0.25f)
    {
        if (diveSfxFadeCoroutine != null)
        {
            StopCoroutine(diveSfxFadeCoroutine);
        }
        diveSfxFadeCoroutine = StartCoroutine(CoFadeOutDiveSfx(duration));
    }

    private IEnumerator CoFadeOutDiveSfx(float duration)
    {
        AudioSource src = oneShotAudioSource != null ? oneShotAudioSource : audioSource;
        if (src == null)
        {
            diveSfxFadeCoroutine = null;
            yield break;
        }

        duration = Mathf.Max(0.05f, duration);
        float startVol = src.volume;
        float elapsed = 0f;
        while (elapsed < duration && src != null)
        {
            elapsed += Time.deltaTime;
            src.volume = Mathf.Lerp(startVol, 0f, elapsed / duration);
            yield return null;
        }

        if (src != null)
        {
            src.Stop();
            src.volume = startVol;
        }

        diveSfxFadeCoroutine = null;
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

    private void ApplyFailSafeRecovery()
    {
        // Kapre-style unlock: if no action coroutine is active, never stay busy.
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
