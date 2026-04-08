using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class AswangUnitAI : BaseEnemyAI
{
    [Header("Basic Attack Exhausted")]
    [Tooltip("How long the Aswang is locked (exhausted) after a basic attack")]
    public float basicAttackExhaustedTime = 0.5f;

    [Header("Pounce Attack (Leap)")]
    public int pounceDamage = 24;
    public float pounceWindup = 0.4f;
    public float pounceCooldown = 5.5f;
    public float pounceLeapDistance = 6f;
    public float pounceLeapDuration = 0.5f;
    public float pounceLeapHeight = 1.2f;
    public float pounceHitRadius = 1.5f;
    public GameObject pounceWindupVFX;
    public GameObject pounceImpactVFX;
    public Vector3 pounceWindupVFXOffset = Vector3.zero;
    public float pounceWindupVFXScale = 1.0f;
    public Vector3 pounceImpactVFXOffset = Vector3.zero;
    public float pounceImpactVFXScale = 1.0f;
    public AudioClip pounceWindupSFX;
    public AudioClip pounceImpactSFX;
    public string pounceWindupTrigger = "PounceWindup";
    public string pounceTrigger = "Pounce";
    public string skillStoppageTrigger = "SkillStoppage";

    [Header("Pounce Exhausted")]
    [Tooltip("How long the Aswang is locked (exhausted) after a pounce")]
    public float pounceExhaustedTime = 1f;
    [Tooltip("After exhausted: recovery phase before full speed")]
    public float pounceRecoveryTime = 0.5f;

    // Runtime state
    private float lastPounceTime = -9999f;
    private float lastAnySkillRecoveryEnd = -9999f;
    private float lastAnySkillRecoveryStart = -9999f;
    private Coroutine activeAbility;
    private Coroutine basicRoutine;

    // Debug accessors
    public float PounceCooldownRemaining => Mathf.Max(0f, pounceCooldown - (Time.time - lastPounceTime));

    protected override void InitializeEnemy() { }

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTarget, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");
        var targetInAttack = new ConditionNode(blackboard, TargetInAttackRangeAndFacing, "in_attack_range_facing");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");
        var canPounce = new ConditionNode(blackboard, CanPounce, "can_pounce");
        var doPounce = new ActionNode(blackboard, () => { StartPounce(); return NodeState.Success; }, "pounce");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "pounce_seq").Add(canPounce, doPounce),
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
        EndAction();
        if (basicAttackExhaustedTime > 0f)
            globalBusyTimer = Mathf.Max(globalBusyTimer, basicAttackExhaustedTime);
    }

    protected override bool TrySpecialAbilities()
    {
        return false;
    }

    #region Pounce Attack

    private bool CanPounce()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (Time.time - lastPounceTime < pounceCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance >= pounceLeapDistance * 0.5f && distance <= pounceLeapDistance * 1.5f;
    }

    private void StartPounce()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoPounce());
    }

    private IEnumerator CoPounce()
    {
        BeginAction(AIState.Special1);

        // Capture leap direction before windup (where to leap)
        var target = blackboard.Get<Transform>("target");
        Vector3 leapDirection = transform.forward; // Default to current forward
        if (target != null)
        {
            Vector3 toTarget = new Vector3(target.position.x, transform.position.y, target.position.z) - transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                leapDirection = toTarget.normalized;
                // Set rotation once before windup
                transform.rotation = Quaternion.LookRotation(leapDirection);
            }
        }

        // Windup animation trigger
        if (HasTrigger(pounceWindupTrigger)) SetTriggerSync(pounceWindupTrigger);
        // Windup VFX/SFX
        if (audioSource != null && pounceWindupSFX != null)
        {
            audioSource.clip = pounceWindupSFX;
            audioSource.loop = false;
            audioSource.Play();
        }
        GameObject windupFx = null;
        if (pounceWindupVFX != null)
        {
            windupFx = Instantiate(pounceWindupVFX, transform);
            windupFx.transform.localPosition = pounceWindupVFXOffset;
            if (pounceWindupVFXScale > 0f) windupFx.transform.localScale = Vector3.one * pounceWindupVFXScale;
        }

        // Windup phase - lock rotation (don't face player)
        float windup = pounceWindup;
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            // Lock rotation during windup
            transform.rotation = Quaternion.LookRotation(leapDirection);
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }

        // End windup visuals/audio and play activation VFX/SFX
        if (audioSource != null && audioSource.clip == pounceWindupSFX)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }
        if (windupFx != null) Destroy(windupFx);
        if (pounceImpactVFX != null)
        {
            var fx = Instantiate(pounceImpactVFX, transform);
            fx.transform.localPosition = pounceImpactVFXOffset;
            if (pounceImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * pounceImpactVFXScale;
        }
        PlaySfx(pounceImpactSFX);

        // Pounce animation trigger
        if (HasTrigger(pounceTrigger)) SetTriggerSync(pounceTrigger);

        // Forward leap (parabolic arc)
        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + leapDirection * pounceLeapDistance;
        endPos.y = startPos.y; // Keep Y level for landing

        float leapTime = 0f;
        HashSet<PlayerStats> hitPlayers = new HashSet<PlayerStats>(); // Track players already hit
        while (leapTime < pounceLeapDuration)
        {
            leapTime += Time.deltaTime;
            float progress = leapTime / pounceLeapDuration;

            // Parabolic arc for jump
            float height = Mathf.Sin(progress * Mathf.PI) * pounceLeapHeight;
            Vector3 currentPos = Vector3.Lerp(startPos, endPos, progress);
            currentPos.y = startPos.y + height;

            // Move with CharacterController
            if (controller != null && controller.enabled)
            {
                Vector3 move = currentPos - transform.position;
                controller.Move(move);
            }
            else
            {
                transform.position = currentPos;
            }

            // Lock rotation during leap
            transform.rotation = Quaternion.LookRotation(leapDirection);

            // Check for hits during leap - ONE DAMAGE PER PLAYER
            var hitColliders = Physics.OverlapSphere(transform.position, pounceHitRadius);
            foreach (var hit in hitColliders)
            {
                if (hit.CompareTag("Player"))
                {
                    var playerStats = hit.GetComponent<PlayerStats>();
                    if (playerStats != null && !hitPlayers.Contains(playerStats))
                    {
                        DamageRelay.ApplyToPlayer(playerStats.gameObject, pounceDamage);
                        hitPlayers.Add(playerStats);
                    }
                }
            }

            yield return null;
        }

        // Ensure we're at end position
        if (controller != null && controller.enabled)
        {
            controller.enabled = false;
            transform.position = endPos;
            controller.enabled = true;
        }
        else
        {
            transform.position = endPos;
        }

        // Stoppage recovery (AI frozen after attack)
        if (pounceExhaustedTime > 0f)
        {
            if (HasTrigger(skillStoppageTrigger)) SetTriggerSync(skillStoppageTrigger);

            float stopTimer = pounceExhaustedTime;
            float quarterStoppage = pounceExhaustedTime * 0.75f;

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
        if (pounceRecoveryTime > 0f)
        {
            lastAnySkillRecoveryStart = Time.time;
            float recovery = pounceRecoveryTime;
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
        lastPounceTime = Time.time;
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
