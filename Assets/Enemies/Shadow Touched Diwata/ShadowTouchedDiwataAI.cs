using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class ShadowTouchedDiwataAI : BaseEnemyAI
{
    [Header("Eclipse Veil (DoT Pull - like Water Vortex)")]
    public int eclipseVeilTickDamage = 5;
    public float eclipseVeilTickInterval = 0.5f;
    public int eclipseVeilTicks = 16; // 40 total = 8s at 0.5s intervals (16 ticks)
    public float eclipseVeilRadius = 5f;
    public float eclipseVeilWindup = 0.7f;
    public float eclipseVeilCooldown = 12f;
    public float eclipseVeilPullStrength = 6f;
    [Header("Eclipse Veil VFX/SFX")]
    public GameObject eclipseVeilWindupVFX;
    public GameObject eclipseVeilActiveVFX;
    public Vector3 eclipseVeilWindupVFXOffset = Vector3.zero;
    public float eclipseVeilWindupVFXScale = 1.0f;
    public Vector3 eclipseVeilActiveVFXOffset = Vector3.zero;
    public float eclipseVeilActiveVFXScale = 1.0f;
    public AudioClip eclipseVeilWindupSFX;
    public AudioClip eclipseVeilActiveSFX;
    public string eclipseVeilWindupTrigger = "EclipseVeilWindup";
    public string eclipseVeilMainTrigger = "EclipseVeilMain";
    [Header("Eclipse Veil Indicator")]
    public GameObject eclipseVeilIndicatorPrefab;
    public Vector3 eclipseVeilIndicatorOffset = new Vector3(0f, -0.1f, 0f);
    public Vector3 eclipseVeilIndicatorScale = new Vector3(3f, 1f, 3f);
    public bool eclipseVeilIndicatorRotate90X = true;

    [Header("Lament of the Void (Forward Melee Slash)")]
    public int lamentDamage = 28;
    public float lamentRange = 4f; // forward melee range
    public float lamentWidth = 2.5f; // width of slash area
    public float lamentWindup = 0.6f;
    public float lamentCooldown = 10f;
    public GameObject lamentWindupVFX;
    public GameObject lamentImpactVFX;
    public Vector3 lamentWindupVFXOffset = Vector3.zero;
    public float lamentWindupVFXScale = 1.0f;
    public Vector3 lamentImpactVFXOffset = Vector3.zero;
    public float lamentImpactVFXScale = 1.0f;
    public AudioClip lamentWindupSFX;
    public AudioClip lamentImpactSFX;
    public string lamentWindupTrigger = "LamentWindup";
    public string lamentMainTrigger = "LamentMain";

    [Header("Skill Selection Tuning")]
    public float eclipseVeilPreferredMinDistance = 3f;
    public float eclipseVeilPreferredMaxDistance = 6f;
    [Range(0f, 1f)] public float eclipseVeilSkillWeight = 0.7f;
    public float eclipseVeilStoppageTime = 1f;
    public float eclipseVeilRecoveryTime = 0.5f;
    public float lamentPreferredMinDistance = 4f;
    public float lamentPreferredMaxDistance = 8f;
    [Range(0f, 1f)] public float lamentSkillWeight = 0.8f;
    public float lamentStoppageTime = 1f;
    public float lamentRecoveryTime = 0.5f;
    [Header("Animation")]
    public string skillStoppageTrigger = "Exhausted";

    // Runtime state
    private float lastEclipseVeilTime = -9999f;
    private float lastLamentTime = -9999f;
    private float lastAnySkillRecoveryEnd = -9999f;
    private float lastAnySkillRecoveryStart = -9999f;
    private Coroutine activeAbility;
    private Coroutine basicRoutine;
    private bool eclipseVeilActive = false; // Allow movement while active but block other specials

    // Debug accessors
    public float EclipseVeilCooldownRemaining => Mathf.Max(0f, eclipseVeilCooldown - (Time.time - lastEclipseVeilTime));
    public float LamentCooldownRemaining => Mathf.Max(0f, lamentCooldown - (Time.time - lastLamentTime));

    protected override void InitializeEnemy() { }

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTarget, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");
        var targetInAttack = new ConditionNode(blackboard, TargetInAttackRangeAndFacing, "in_attack_range_facing");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");
        var canEclipseVeil = new ConditionNode(blackboard, CanEclipseVeil, "can_eclipseveil");
        var doEclipseVeil = new ActionNode(blackboard, () => { StartEclipseVeil(); return NodeState.Success; }, "eclipseveil");
        var canLament = new ConditionNode(blackboard, CanLament, "can_lament");
        var doLament = new ActionNode(blackboard, () => { StartLament(); return NodeState.Success; }, "lament");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "eclipseveil_seq").Add(canEclipseVeil, doEclipseVeil),
                        new Sequence(blackboard, "lament_seq").Add(canLament, doLament),
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
        // Allow basic attacks while Eclipse Veil is active (but not during other abilities)
        if (activeAbility != null && !eclipseVeilActive) return;
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

        // Windup animation trigger
        if (animator != null)
        {
            if (HasTrigger(attackWindupTrigger))
                SetTriggerSync(attackWindupTrigger);
            else if (HasTrigger(attackTrigger))
                SetTriggerSync(attackTrigger);
        }
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

        // Impact animation trigger
        if (animator != null && HasTrigger(attackImpactTrigger))
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
        if (activeAbility != null || eclipseVeilActive) return false;
        if (basicRoutine != null) return false;
        if (isBusy) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        bool facingTarget = IsFacingTarget(target, SpecialFacingAngle);
        bool inEclipseVeilRange = dist >= eclipseVeilPreferredMinDistance && dist <= eclipseVeilPreferredMaxDistance;
        bool inLamentRange = dist >= lamentPreferredMinDistance && dist <= lamentPreferredMaxDistance;
        float eclipseVeilMid = (eclipseVeilPreferredMinDistance + eclipseVeilPreferredMaxDistance) * 0.5f;
        float lamentMid = (lamentPreferredMinDistance + lamentPreferredMaxDistance) * 0.5f;
        float eclipseVeilScore = (inEclipseVeilRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(eclipseVeilMid - dist) / 5f)) * eclipseVeilSkillWeight : 0f;
        float lamentScore = (inLamentRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(lamentMid - dist) / 6f)) * lamentSkillWeight : 0f;
        // Check Lament first if it has a higher or equal score (gives Lament priority on ties)
        // Then check Eclipse Veil if it has a strictly higher score
        if (CanLament() && lamentScore >= eclipseVeilScore && lamentScore > 0.15f) { StartLament(); return true; }
        if (CanEclipseVeil() && eclipseVeilScore > lamentScore && eclipseVeilScore > 0.15f) { StartEclipseVeil(); return true; }
        return false;
    }

    private bool CanEclipseVeil()
    {
        if (isBusy || eclipseVeilActive || activeAbility != null) return false;
        if (basicRoutine != null) return false;
        if (Time.time - lastEclipseVeilTime < eclipseVeilCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        if (!IsFacingTarget(target, SpecialFacingAngle)) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        // Check if target is within Eclipse Veil radius AND preferred distance range
        return distance <= eclipseVeilRadius && distance >= eclipseVeilPreferredMinDistance && distance <= eclipseVeilPreferredMaxDistance;
    }

    private void StartEclipseVeil()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoEclipseVeil());
    }

    private IEnumerator CoEclipseVeil()
    {
        BeginAction(AIState.Special1);
        if (animator != null && HasTrigger(eclipseVeilWindupTrigger)) SetTriggerSync(eclipseVeilWindupTrigger);
        if (audioSource != null && eclipseVeilWindupSFX != null)
        {
            audioSource.clip = eclipseVeilWindupSFX;
            audioSource.loop = false;
            audioSource.Play();
        }
        GameObject windupFx = null;
        if (eclipseVeilWindupVFX != null)
        {
            windupFx = Instantiate(eclipseVeilWindupVFX, transform);
            windupFx.transform.localPosition = eclipseVeilWindupVFXOffset;
            if (eclipseVeilWindupVFXScale > 0f) windupFx.transform.localScale = Vector3.one * eclipseVeilWindupVFXScale;
        }
        // Indicator appears at windup start and grows to full radius
        GameObject indicatorWindup = null;
        Vector3 indicatorTarget = new Vector3(
            Mathf.Max(0.01f, eclipseVeilIndicatorScale.x),
            Mathf.Max(0.01f, eclipseVeilIndicatorScale.y),
            Mathf.Max(0.01f, eclipseVeilIndicatorScale.z)
        );
        if (eclipseVeilIndicatorPrefab != null)
        {
            indicatorWindup = Instantiate(eclipseVeilIndicatorPrefab, transform);
            indicatorWindup.transform.localPosition = eclipseVeilIndicatorOffset;
            if (eclipseVeilIndicatorRotate90X) indicatorWindup.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            indicatorWindup.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
        }
        float wind = Mathf.Max(0f, eclipseVeilWindup);
        float indicatorGrowTime = Mathf.Max(0.01f, eclipseVeilWindup * 0.5f);
        float indicatorTimer = 0f;
        while (wind > 0f)
        {
            wind -= Time.deltaTime;
            // Freeze movement during windup
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            // Face target during windup
            var t = blackboard.Get<Transform>("target");
            if (t != null)
            {
                Vector3 look = new Vector3(t.position.x, transform.position.y, t.position.z);
                Vector3 dir = (look - transform.position);
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion r = Quaternion.LookRotation(dir);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, r, RotationSpeed * Time.deltaTime);
                }
            }
            // Grow indicator
            if (indicatorWindup != null && indicatorTimer < indicatorGrowTime)
            {
                indicatorTimer += Time.deltaTime;
                float pct = Mathf.Clamp01(indicatorTimer / indicatorGrowTime);
                Vector3 s = Vector3.Lerp(new Vector3(0.01f, 0.01f, 0.01f), indicatorTarget, pct);
                indicatorWindup.transform.localScale = s;
            }
            yield return null;
        }
        if (audioSource != null && audioSource.clip == eclipseVeilWindupSFX)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }
        if (windupFx != null) Destroy(windupFx);
        // Replace windup indicator with active one
        if (indicatorWindup != null) Destroy(indicatorWindup);
        PlaySfx(eclipseVeilActiveSFX);
        GameObject activeFx = null;
        GameObject indicatorFx = null;
        if (eclipseVeilActiveVFX != null)
        {
            activeFx = Instantiate(eclipseVeilActiveVFX, transform);
            activeFx.transform.localPosition = eclipseVeilActiveVFXOffset;
            if (eclipseVeilActiveVFXScale > 0f) activeFx.transform.localScale = Vector3.one * eclipseVeilActiveVFXScale;
        }
        if (eclipseVeilIndicatorPrefab != null)
        {
            indicatorFx = Instantiate(eclipseVeilIndicatorPrefab, transform);
            indicatorFx.transform.localPosition = eclipseVeilIndicatorOffset;
            if (eclipseVeilIndicatorRotate90X) indicatorFx.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            indicatorFx.transform.localScale = new Vector3(
                Mathf.Max(0.01f, eclipseVeilIndicatorScale.x),
                Mathf.Max(0.01f, eclipseVeilIndicatorScale.y),
                Mathf.Max(0.01f, eclipseVeilIndicatorScale.z)
            );
        }
        // Allow movement while veil is active
        eclipseVeilActive = true;
        EndAction();
        if (animator != null && HasTrigger(eclipseVeilMainTrigger)) SetTriggerSync(eclipseVeilMainTrigger);
        
        int ticks = Mathf.Max(1, eclipseVeilTicks);
        float interval = Mathf.Max(0.05f, eclipseVeilTickInterval);
        for (int i = 0; i < ticks; i++)
        {
            var cols = Physics.OverlapSphere(transform.position, eclipseVeilRadius, LayerMask.GetMask("Player"));
            foreach (var c in cols)
            {
                var ps = c.GetComponentInParent<PlayerStats>();
        if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, eclipseVeilTickDamage);
                // Pull towards center
                var rb = c.attachedRigidbody;
                if (rb != null)
                {
                    Vector3 dir = (transform.position - c.transform.position);
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.0001f)
                        rb.AddForce(dir.normalized * eclipseVeilPullStrength, ForceMode.Acceleration);
                }
            }
            yield return new WaitForSeconds(interval);
        }
        if (activeFx != null) Destroy(activeFx);
        if (indicatorFx != null) Destroy(indicatorFx);
        eclipseVeilActive = false;
        
        if (eclipseVeilStoppageTime > 0f)
        {
            if (animator != null && HasTrigger(skillStoppageTrigger)) SetTriggerSync(skillStoppageTrigger);
            
            float stopTimer = eclipseVeilStoppageTime;
            float quarterStoppage = eclipseVeilStoppageTime * 0.75f;
            
            while (stopTimer > 0f)
            {
                stopTimer -= Time.deltaTime;
                if (controller != null && controller.enabled)
                    controller.SimpleMove(Vector3.zero);
                
                // Set Exhausted boolean parameter when 75% of stoppage time remains (skills only)
                if (stopTimer <= quarterStoppage && animator != null && !animator.GetBool("Exhausted"))
                {
                    SetBoolSync("Exhausted", true);
                }
                
                yield return null;
            }
            
            // Clear Exhausted boolean parameter
            if (animator != null) SetBoolSync("Exhausted", false);
        }
        
        // Recovery time (AI can move but skill still on cooldown, gradual speed recovery)
        if (eclipseVeilRecoveryTime > 0f)
        {
            lastAnySkillRecoveryStart = Time.time;
            float recovery = eclipseVeilRecoveryTime;
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
        lastEclipseVeilTime = Time.time;
    }

    private bool CanLament()
    {
        if (activeAbility != null) return false;
        if (eclipseVeilActive) return false; // Cannot use Lament while Eclipse Veil is active
        if (basicRoutine != null) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (Time.time - lastLamentTime < lamentCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        // Check if target is within the actual attack range (lamentRange)
        // Preferred min/max distances are used in TrySpecialAbilities() for skill selection scoring
        return distance <= lamentRange;
    }

    private void StartLament()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        
        // Capture target direction and lock rotation before windup
        var target = blackboard.Get<Transform>("target");
        Vector3 attackDirection = transform.forward; // Default to current forward
        if (target != null)
        {
            Vector3 toTarget = new Vector3(target.position.x, transform.position.y, target.position.z) - transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                attackDirection = toTarget.normalized;
                // Lock rotation immediately before windup starts
                transform.rotation = Quaternion.LookRotation(attackDirection);
            }
        }
        
        activeAbility = StartCoroutine(CoLament(attackDirection));
    }

    private IEnumerator CoLament(Vector3 attackDirection)
    {
        BeginAction(AIState.Special2);

        // Windup phase - separate trigger, VFX, and SFX
        if (animator != null && HasTrigger(lamentWindupTrigger))
            SetTriggerSync(lamentWindupTrigger);
        PlaySfx(lamentWindupSFX);
        GameObject wind = null;
        if (lamentWindupVFX != null)
        {
            wind = Instantiate(lamentWindupVFX, transform);
            wind.transform.localPosition = lamentWindupVFXOffset;
            if (lamentWindupVFXScale > 0f)
                wind.transform.localScale = Vector3.one * lamentWindupVFXScale;
        }

        // Windup phase - freeze movement and lock rotation (don't face player)
        float windup = Mathf.Max(0f, lamentWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            // Lock rotation during windup (rotation was set before coroutine started)
            transform.rotation = Quaternion.LookRotation(attackDirection);
            yield return null;
        }
        if (wind != null) Destroy(wind);

        // Impact/Main phase - separate trigger, VFX, and SFX
        if (animator != null && HasTrigger(lamentMainTrigger))
            SetTriggerSync(lamentMainTrigger);

        if (lamentImpactVFX != null)
        {
            var fx = Instantiate(lamentImpactVFX, transform);
            fx.transform.localPosition = lamentImpactVFXOffset;
            if (lamentImpactVFXScale > 0f)
                fx.transform.localScale = Vector3.one * lamentImpactVFXScale;
        }
        PlaySfx(lamentImpactSFX);

        // Forward melee slash damage (strip/rectangle area in front)
        Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
        Vector3 center = transform.position + fwd * (lamentRange * 0.5f);
        var all = Physics.OverlapSphere(center, Mathf.Max(lamentRange, lamentWidth), LayerMask.GetMask("Player"));
        foreach (var c in all)
        {
            Vector3 rel = c.transform.position - transform.position;
            rel.y = 0f;
            float along = Vector3.Dot(rel, fwd);
            float across = Vector3.Cross(fwd, rel.normalized).magnitude * rel.magnitude;
            // Check if player is within forward range and width
            if (along >= 0f && along <= lamentRange && Mathf.Abs(across) <= (lamentWidth * 0.5f))
            {
                var ps = c.GetComponentInParent<PlayerStats>();
        if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, lamentDamage);
            }
        }

        // Stoppage recovery (AI frozen after attack)
        if (lamentStoppageTime > 0f)
        {
            if (animator != null && HasTrigger(skillStoppageTrigger)) SetTriggerSync(skillStoppageTrigger);

            float stopTimer = lamentStoppageTime;
            float quarterStoppage = lamentStoppageTime * 0.75f;

            while (stopTimer > 0f)
            {
                stopTimer -= Time.deltaTime;
                if (controller != null && controller.enabled)
                    controller.SimpleMove(Vector3.zero);

                // Set Exhausted boolean parameter when 75% of stoppage time remains (skills only)
                if (stopTimer <= quarterStoppage && animator != null && !animator.GetBool("Exhausted"))
                {
                    SetBoolSync("Exhausted", true);
                }

                yield return null;
            }

            // Clear Exhausted boolean parameter
            if (animator != null) SetBoolSync("Exhausted", false);
        }

        // End busy state so AI can move during recovery
        EndAction();

        // Recovery time (AI can move but skill still on cooldown, gradual speed recovery)
        if (lamentRecoveryTime > 0f)
        {
            lastAnySkillRecoveryStart = Time.time;
            float recovery = lamentRecoveryTime;
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
        lastLamentTime = Time.time;
    }

    private bool IsFacingTarget(Transform target, float maxAngle)
    {
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return false;
        float angle = Vector3.Angle(new Vector3(transform.forward.x, 0f, transform.forward.z).normalized, to.normalized);
        return angle <= Mathf.Clamp(maxAngle, 1f, 60f);
    }

    protected override float GetMoveSpeed()
    {
        // Return 0 if AI is busy or has active ability (should be stopped)
        // BUT allow movement if Eclipse Veil is active (like Berberoka's vortex)
        if (isBusy || globalBusyTimer > 0f || basicRoutine != null)
        {
            return 0f;
        }
        
        // Only block movement if activeAbility is running AND eclipseVeil is not active
        if (activeAbility != null && !eclipseVeilActive)
        {
            return 0f;
        }

        // If AI is idle (not patrolling or chasing), return 0
        if (aiState == AIState.Idle)
        {
            return 0f;
        }

        float baseSpeed = base.GetMoveSpeed();

        // Reduce speed while Eclipse Veil is active
        if (eclipseVeilActive)
        {
            return baseSpeed * 0.5f; // 50% speed while veil is active
        }

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

    void OnDestroy()
    {
        // Cleanup handled in coroutines
    }
}
