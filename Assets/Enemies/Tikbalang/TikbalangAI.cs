using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class TikbalangAI : BaseEnemyAI
{
    [Header("Charge Attack")]
    public int chargeDamage = 35;
    public float chargeCooldown = 15f;
    public float chargeWindup = 2f;
    public float chargeDuration = 2f;
    public float chargeSpeed = 14f;
    public float chargeHitRadius = 1.7f;
    public float chargeMinDistance = 2f;
    public float chargeMaxDistance = 24f;
    public GameObject chargeVFX; // windup
    public AudioClip chargeSFX;  // windup
    public GameObject chargeImpactVFX; // activation
    public Vector3 chargeVFXOffset = Vector3.zero;
    public float chargeVFXScale = 1.0f;
    public Vector3 chargeImpactVFXOffset = Vector3.zero;
    public float chargeImpactVFXScale = 1.0f;
    public AudioClip chargeImpactSFX;

    [Header("Stomp Attack")]
    public int stompDamage = 25;
    public float stompRadius = 4f;
    public float stompCooldown = 10f;
    public float stompWindup = 0.3f;
    public float stompMinDistance = 4f;
    public float stompMaxDistance = 6f;
    public GameObject stompVFX; // windup
    public AudioClip stompSFX;  // windup
    public GameObject stompImpactVFX; // activation
    public Vector3 stompVFXOffset = Vector3.zero;
    public float stompVFXScale = 1.0f;
    public Vector3 stompImpactVFXOffset = Vector3.zero;
    public float stompImpactVFXScale = 1.0f;
    public AudioClip stompImpactSFX;

    [Header("Animation")]
    public string chargeWindupTrigger = "ChargeWindup";
    public string chargeTrigger = "Charge";
    public string stompWindupTrigger = "StompWindup";
    public string stompTrigger = "Stomp";
    public string skillStoppageTrigger = "SkillStoppage";

    [Header("Skill Selection Tuning")]
    public float chargePreferredMinDistance = 10f;
    public float chargePreferredMaxDistance = 20f;
    [Range(0f, 1f)] public float chargeSkillWeight = 0.8f;
    public float chargeStoppageTime = 1f;
    public float chargeRecoveryTime = 0.5f;
    public float stompPreferredMinDistance = 2.0f;
    public float stompPreferredMaxDistance = 5.5f;
    [Range(0f, 1f)] public float stompSkillWeight = 0.7f;
    public float stompStoppageTime = 1f;
    public float stompRecoveryTime = 0.5f;

    // Runtime state
    private float lastChargeTime = -9999f;
    private float lastStompTime = -9999f;
    private float lastAnySkillRecoveryEnd = -9999f; // When current recovery phase ends
    private float lastAnySkillRecoveryStart = -9999f; // When recovery phase started
    private Coroutine activeAbility;
    private Coroutine basicRoutine;
    private bool isExhausted = false; // Blocks all actions during stoppage; cleared when recovery starts
    private bool inRecoveryPhase = false; // True during recovery - allows movement with gradual speed

    // Debug accessors
    public float ChargeCooldownRemaining => Mathf.Max(0f, chargeCooldown - (Time.time - lastChargeTime));
    public float StompCooldownRemaining => Mathf.Max(0f, stompCooldown - (Time.time - lastStompTime));

    #region Initialization

    protected override void InitializeEnemy() { }

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTarget, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");
        var targetInAttack = new ConditionNode(blackboard, TargetInAttackRangeAndFacing, "in_attack_range_facing");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");

        var exhaustedGate = new ActionNode(blackboard, () => isExhausted ? NodeState.Running : NodeState.Success, "exhausted_gate");
        var canCharge = new ConditionNode(blackboard, CanCharge, "can_charge");
        var doCharge = new ActionNode(blackboard, () => { StartCharge(); return NodeState.Success; }, "charge");
        var canStomp = new ConditionNode(blackboard, CanStomp, "can_stomp");
        var doStomp = new ActionNode(blackboard, () => { StartStomp(); return NodeState.Success; }, "stomp");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    exhaustedGate,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "charge_seq").Add(canCharge, doCharge),
                        new Sequence(blackboard, "stomp_seq").Add(canStomp, doStomp),
                        new Sequence(blackboard, "basic_seq").Add(targetInAttack, basicAttack),
            moveToTarget
                    )
                ),
                new ActionNode(blackboard, Patrol, "patrol")
            );
    }

    #endregion

    #region BaseEnemyAI Overrides

    protected override void PerformBasicAttack()
    {
        if (basicRoutine != null || activeAbility != null || isExhausted) return;
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
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
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

        // Exhausted phase: distinct from Busy, blocks all actions
        float post = Mathf.Max(0.1f, enemyData.attackMoveLock);
        basicRoutine = null;
        EndAction();
        isExhausted = true;
        if (post > 0f && HasBool("Exhausted")) SetBoolSync("Exhausted", true);
        while (post > 0f)
        {
            post -= Time.deltaTime;
            transform.rotation = lockedRotation;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        if (HasBool("Exhausted")) SetBoolSync("Exhausted", false);
        isExhausted = false;

        lastAttackTime = Time.time;
        attackLockTimer = enemyData.attackMoveLock;
    }

    protected override bool TrySpecialAbilities()
    {
        if (activeAbility != null || isExhausted) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        bool facingTarget = IsFacingTarget(target, SpecialFacingAngle);
        bool inChargeRange = dist >= chargePreferredMinDistance && dist <= chargePreferredMaxDistance;
        bool inStompRange = dist >= stompPreferredMinDistance && dist <= stompPreferredMaxDistance;
        float chargeMid = (chargePreferredMinDistance + chargePreferredMaxDistance) * 0.5f;
        float stompMid = (stompPreferredMinDistance + stompPreferredMaxDistance) * 0.5f;
        float chargeScore = (inChargeRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(chargeMid - dist) / 15f)) * chargeSkillWeight : 0f;
        float stompScore = (inStompRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(stompMid - dist) / 5f)) * stompSkillWeight : 0f;
        if (CanCharge() && chargeScore >= stompScore && chargeScore > 0.15f) { StartCharge(); return true; }
        if (CanStomp() && stompScore > chargeScore && stompScore > 0.15f) { StartStomp(); return true; }
        return false;
    }

    private bool IsFacingTarget(Transform target, float maxAngle)
    {
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return false;
        float angle = Vector3.Angle(new Vector3(transform.forward.x, 0f, transform.forward.z).normalized, to.normalized);
        return angle <= Mathf.Clamp(maxAngle, 1f, 60f);
    }
    private void FaceTarget(Transform target)
    {
        Vector3 look = new Vector3(target.position.x, transform.position.y, target.position.z);
        Vector3 dir = (look - transform.position);
        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, RotationSpeed * Time.deltaTime);
        }
        if (controller != null && controller.enabled)
            controller.SimpleMove(Vector3.zero);
    }

    #endregion

    #region Charge Attack

    private bool IsWithinDistanceBand(Transform target, float minDistance, float maxDistance)
    {
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        if (distance < Mathf.Max(0f, minDistance)) return false;
        if (maxDistance > 0f && distance > maxDistance) return false;
        return true;
    }

    private bool CanCharge()
    {
        if (activeAbility != null || basicRoutine != null || isExhausted) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (Time.time - lastChargeTime < chargeCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false; // 4 second lock after any skill recovery
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        if (!IsFacingTarget(target, SpecialFacingAngle)) return false;
        float maxDistance = chargeMaxDistance > 0f ? chargeMaxDistance : chargePreferredMaxDistance;
        return IsWithinDistanceBand(target, chargeMinDistance, maxDistance);
    }

    private void StartCharge()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoCharge());
    }

    private IEnumerator CoCharge()
    {
        BeginAction(AIState.Special1);
        
        // Capture charge direction before windup (where to charge)
        var target = blackboard.Get<Transform>("target");
        Vector3 chargeDirection = transform.forward; // Default to current forward
        if (target != null)
        {
            Vector3 toTarget = new Vector3(target.position.x, transform.position.y, target.position.z) - transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                chargeDirection = toTarget.normalized;
                // Set rotation once before windup
                transform.rotation = Quaternion.LookRotation(chargeDirection);
            }
        }
        
        // Windup animation (sync to network)
        if (HasTrigger(chargeWindupTrigger)) SetTriggerSync(chargeWindupTrigger);
        // windup SFX (stoppable)
        if (audioSource != null && chargeSFX != null)
        {
            audioSource.clip = chargeSFX;
            audioSource.loop = false;
            audioSource.Play();
        }
        // windup VFX
        GameObject chargeWindupFx = null;
        if (chargeVFX != null)
        {
            chargeWindupFx = Instantiate(chargeVFX, transform);
            chargeWindupFx.transform.localPosition = chargeVFXOffset;
            if (chargeVFXScale > 0f) chargeWindupFx.transform.localScale = Vector3.one * chargeVFXScale;
        }
        
        // Windup phase - lock rotation (don't face player)
        float windup = chargeWindup;
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            // Lock rotation during windup
            transform.rotation = Quaternion.LookRotation(chargeDirection);
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }

        // End windup visuals/audio and play activation impact VFX/SFX
        if (audioSource != null && audioSource.clip == chargeSFX)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }
        if (chargeWindupFx != null) Destroy(chargeWindupFx);
        if (chargeImpactVFX != null)
        {
            var fx = Instantiate(chargeImpactVFX, transform);
            fx.transform.localPosition = chargeImpactVFXOffset;
            if (chargeImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * chargeImpactVFXScale;
        }
        PlaySfx(chargeImpactSFX);

        // Charge animation trigger (sync to network)
        if (HasTrigger(chargeTrigger)) SetTriggerSync(chargeTrigger);

        // Charge in locked direction (set before windup)
        float chargeTime = chargeDuration;
        HashSet<PlayerStats> hitPlayers = new HashSet<PlayerStats>(); // Track players already hit
        
        while (chargeTime > 0f)
        {
            if (controller != null && controller.enabled)
            {
                controller.Move(chargeDirection * chargeSpeed * Time.deltaTime);
            }

            // Check for hits during charge
            var hitColliders = Physics.OverlapSphere(transform.position, chargeHitRadius);
            foreach (var hit in hitColliders)
            {
                if (hit.CompareTag("Player"))
                {
                    var playerStats = hit.GetComponent<PlayerStats>();
                    if (playerStats != null && !hitPlayers.Contains(playerStats))
                    {
                        DamageRelay.ApplyToPlayer(playerStats.gameObject, chargeDamage);
                        hitPlayers.Add(playerStats);
                    }
                }
            }

            chargeTime -= Time.deltaTime;
            yield return null;
        }

        // Stoppage: AI frozen, blocks all actions
        if (chargeStoppageTime > 0f)
        {
            isExhausted = true;
            if (HasTrigger(skillStoppageTrigger)) SetTriggerSync(skillStoppageTrigger);
            float stopTimer = chargeStoppageTime;
            float quarterStoppage = chargeStoppageTime * 0.75f;
            while (stopTimer > 0f)
            {
                stopTimer -= Time.deltaTime;
                if (controller != null && controller.enabled)
                    controller.SimpleMove(Vector3.zero);
                if (stopTimer <= quarterStoppage)
                    SetBoolSync("Exhausted", true);
                yield return null;
            }
            SetBoolSync("Exhausted", false);
        }

        // Recovery: release for movement, gradual speed; block new skills via activeAbility
        if (chargeRecoveryTime > 0f)
        {
            isExhausted = false;
            EndAction();
            lastAnySkillRecoveryStart = Time.time;
            lastAnySkillRecoveryEnd = Time.time + chargeRecoveryTime;
            inRecoveryPhase = true;
            float recovery = chargeRecoveryTime;
            while (recovery > 0f)
            {
                recovery -= Time.deltaTime;
                yield return null;
            }
            inRecoveryPhase = false;
        }
        else
        {
            isExhausted = false;
            EndAction();
        }

        activeAbility = null;
        lastChargeTime = Time.time;
        lastAnySkillRecoveryEnd = Time.time;
    }

    #endregion

    #region Stomp Attack

    private bool CanStomp()
    {
        if (activeAbility != null || basicRoutine != null || isExhausted) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (Time.time - lastStompTime < stompCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false; // 4 second lock after any skill recovery
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        if (!IsFacingTarget(target, SpecialFacingAngle)) return false;
        float maxDistance = stompMaxDistance > 0f ? stompMaxDistance : Mathf.Max(stompRadius + 0.5f, stompPreferredMaxDistance);
        return IsWithinDistanceBand(target, stompMinDistance, maxDistance);
    }

    private void StartStomp()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoStomp());
    }

    private IEnumerator CoStomp()
    {
        BeginAction(AIState.Special2);
        
        // Windup animation (sync to network)
        if (HasTrigger(stompWindupTrigger)) SetTriggerSync(stompWindupTrigger);
        // windup sfx (stoppable)
        if (audioSource != null && stompSFX != null)
        {
            audioSource.clip = stompSFX;
            audioSource.loop = false;
            audioSource.Play();
        }
        // windup vfx
        GameObject stompWindupFx = null;
        if (stompVFX != null)
        {
            stompWindupFx = Instantiate(stompVFX, transform);
            stompWindupFx.transform.localPosition = stompVFXOffset;
            if (stompVFXScale > 0f) stompWindupFx.transform.localScale = Vector3.one * stompVFXScale;
        }

        // Windup wait
        yield return new WaitForSeconds(stompWindup);

        // end windup visuals/audio and play activation vfx/sfx
        if (audioSource != null && audioSource.clip == stompSFX)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }
        if (stompWindupFx != null) Destroy(stompWindupFx);
        if (stompImpactVFX != null)
        {
            var fx = Instantiate(stompImpactVFX, transform);
            fx.transform.localPosition = stompImpactVFXOffset;
            if (stompImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * stompImpactVFXScale;
        }
        PlaySfx(stompImpactSFX);

        // Stomp animation trigger (sync to network)
        if (HasTrigger(stompTrigger)) SetTriggerSync(stompTrigger);

        var hitColliders = Physics.OverlapSphere(transform.position, stompRadius);
        foreach (var hit in hitColliders)
        {
            if (hit.CompareTag("Player"))
            {
                var playerStats = hit.GetComponent<PlayerStats>();
        if (playerStats != null) DamageRelay.ApplyToPlayer(playerStats.gameObject, stompDamage);
            }
        }

        // Stoppage: AI frozen, blocks all actions
        if (stompStoppageTime > 0f)
        {
            isExhausted = true;
            if (HasTrigger(skillStoppageTrigger)) SetTriggerSync(skillStoppageTrigger);
            float stopTimer = stompStoppageTime;
            float quarterStoppage = stompStoppageTime * 0.75f;
            while (stopTimer > 0f)
            {
                stopTimer -= Time.deltaTime;
                if (controller != null && controller.enabled)
                    controller.SimpleMove(Vector3.zero);
                if (stopTimer <= quarterStoppage)
                    SetBoolSync("Exhausted", true);
                yield return null;
            }
            SetBoolSync("Exhausted", false);
        }

        // Recovery: release for movement, gradual speed; block new skills via activeAbility
        if (stompRecoveryTime > 0f)
        {
            isExhausted = false;
            EndAction();
            lastAnySkillRecoveryStart = Time.time;
            lastAnySkillRecoveryEnd = Time.time + stompRecoveryTime;
            inRecoveryPhase = true;
            float recovery = stompRecoveryTime;
            while (recovery > 0f)
            {
                recovery -= Time.deltaTime;
                yield return null;
            }
            inRecoveryPhase = false;
        }
        else
        {
            isExhausted = false;
            EndAction();
        }

        activeAbility = null;
        lastStompTime = Time.time;
        lastAnySkillRecoveryEnd = Time.time;
    }

    #endregion

    protected override float GetMoveSpeed()
    {
        // During recovery phase: allow movement with gradual speed (stoppage already ended)
        if (inRecoveryPhase && lastAnySkillRecoveryEnd > lastAnySkillRecoveryStart)
        {
            float baseSpeed = base.GetMoveSpeed();
            float recoveryDuration = lastAnySkillRecoveryEnd - lastAnySkillRecoveryStart;
            float elapsed = Time.time - lastAnySkillRecoveryStart;
            float progress = Mathf.Clamp01(elapsed / recoveryDuration);
            float speedMultiplier = Mathf.Lerp(0.3f, 1.0f, progress);
            return baseSpeed * speedMultiplier;
        }

        // Return 0 if busy, exhausted, or has active ability
        if (isBusy || globalBusyTimer > 0f || isExhausted || activeAbility != null || basicRoutine != null)
            return 0f;

        if (aiState == AIState.Idle)
            return 0f;

        return base.GetMoveSpeed();
    }
}