using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class BungisngisAI : BaseEnemyAI
{
    [Header("Belly Laugh (Cone)")]
    public int laughDamage = 15;
    [Range(0f,180f)] public float laughConeAngle = 80f;
    [Tooltip("Damage radius for cone attack - sphere around enemy")]
    public float laughRadius = 7f;
    public float laughWindup = 0.5f;
    public float laughCooldown = 20f;
    public GameObject laughWindupVFX;
    public GameObject laughImpactVFX;
    public Vector3 laughWindupVFXOffset = Vector3.zero;
    public float laughWindupVFXScale = 1.0f;
    public Vector3 laughImpactVFXOffset = Vector3.zero;
    public float laughImpactVFXScale = 1.0f;
    public AudioClip laughWindupSFX;
    public AudioClip laughImpactSFX;
    public string laughWindupTrigger = "LaughWindup";
    public string laughMainTrigger = "LaughMain";

    [Header("Ground Pound (Strip Shockwave)")]
    public int poundDamage = 22;
    public float poundWidth = 2.0f;
    public float poundRadius = 6f;
    public float poundWindup = 0.45f;
    public float poundCooldown = 30f;
    public GameObject poundWindupVFX;
    public GameObject poundImpactVFX;
    public Vector3 poundWindupVFXOffset = Vector3.zero;
    public float poundWindupVFXScale = 1.0f;
    public Vector3 poundImpactVFXOffset = Vector3.zero;
    public float poundImpactVFXScale = 1.0f;
    public AudioClip poundWindupSFX;
    public AudioClip poundImpactSFX;
    public string poundWindupTrigger = "PoundWindup";
    public string poundMainTrigger = "PoundMain";

    [Header("Skill Selection Tuning")]
    public float laughPreferredMinDistance = 3f;
    public float laughPreferredMaxDistance = 8f;
    [Range(0f, 1f)] public float laughSkillWeight = 0.7f;
    public float laughStoppageTime = 1f;
    public float laughRecoveryTime = 0.5f;
    public float poundPreferredMinDistance = 5f;
    public float poundPreferredMaxDistance = 12f;
    [Range(0f, 1f)] public float poundSkillWeight = 0.8f;
    public float poundStoppageTime = 1f;
    public float poundRecoveryTime = 0.5f;

    [Header("Laugh Projectile")]
    public GameObject laughProjectilePrefab;
    // Photon DefaultPool loads by Resources path. Keep a configurable path to avoid name-only lookup failures.
    public string laughProjectileResourcePath = "Enemies/Projectiles/Belly Laugh";
    public Vector3 laughProjectileSpawnOffset = new Vector3(0f,1.2f,1.8f);
    public float laughProjectileSpeed = 18f;
    public float laughProjectileLifetime = 2.5f;
    [Range(0f, 40f)] public float laughProjectileSpreadAngle = 0f;
    [Range(1,10)] public int laughProjectileCount = 1;

    private float lastLaughTime = -9999f;
    private float lastPoundTime = -9999f;
    private float lastAnySkillRecoveryEnd = -9999f; // Track when ANY skill's recovery ended
    private float lastAnySkillRecoveryStart = -9999f; // Track when recovery phase starts
    private Coroutine activeAbility;
    private Coroutine basicRoutine;

    // Debug accessors
    public float LaughCooldownRemaining => Mathf.Max(0f, laughCooldown - (Time.time - lastLaughTime));
    public float PoundCooldownRemaining => Mathf.Max(0f, poundCooldown - (Time.time - lastPoundTime));

    protected override void InitializeEnemy() { }

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTarget, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");
        var targetInAttack = new ConditionNode(blackboard, TargetInAttackRangeAndFacing, "in_attack_range_facing");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");
        var canLaugh = new ConditionNode(blackboard, CanLaugh, "can_laugh");
        var doLaugh = new ActionNode(blackboard, () => { StartLaugh(); return NodeState.Success; }, "laugh");
        var canPound = new ConditionNode(blackboard, CanPound, "can_pound");
        var doPound = new ActionNode(blackboard, () => { StartPound(); return NodeState.Success; }, "pound");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "laugh_seq").Add(canLaugh, doLaugh),
                        new Sequence(blackboard, "pound_seq").Add(canPound, doPound),
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
        if (activeAbility != null) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        bool facingTarget = IsFacingTarget(target, SpecialFacingAngle);
        bool inLaughRange = dist >= laughPreferredMinDistance && dist <= laughPreferredMaxDistance;
        bool inPoundRange = dist >= poundPreferredMinDistance && dist <= poundPreferredMaxDistance;
        float laughMid = (laughPreferredMinDistance + laughPreferredMaxDistance) * 0.5f;
        float poundMid = (poundPreferredMinDistance + poundPreferredMaxDistance) * 0.5f;
        float laughScore = (inLaughRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(laughMid - dist) / 7f)) * laughSkillWeight : 0f;
        float poundScore = (inPoundRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(poundMid - dist) / 7f)) * poundSkillWeight : 0f;
        if (CanLaugh() && laughScore >= poundScore && laughScore > 0.15f) { StartLaugh(); return true; }
        if (CanPound() && poundScore > laughScore && poundScore > 0.15f) { StartPound(); return true; }
        return false;
    }

    private bool CanLaugh()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false; // Don't interrupt basic attacks
        if (isBusy || globalBusyTimer > 0f) return false; // Don't interrupt basic attacks or other actions
        if (Time.time - lastLaughTime < laughCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false; // 4 second lock after any skill recovery
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        return Vector3.Distance(transform.position, target.position) <= laughRadius + 0.5f;
    }

    private void StartLaugh()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoLaugh());
    }

    private IEnumerator CoLaugh()
    {
        BeginAction(AIState.Special1);
        
        // Windup phase - separate trigger, VFX, and SFX (sync to network)
        if (HasTrigger(laughWindupTrigger))
            SetTriggerSync(laughWindupTrigger);
        PlaySfx(laughWindupSFX);
        GameObject wind = null;
        if (laughWindupVFX != null)
        {
            Vector3 scale = laughWindupVFXScale > 0f ? Vector3.one * laughWindupVFXScale : Vector3.one;
            wind = SpawnVFXSync(laughWindupVFX, laughWindupVFXOffset, scale, true);
        }
        yield return new WaitForSeconds(Mathf.Max(0f, laughWindup));
        if (wind != null) Destroy(wind);
        
        // Impact phase - separate trigger, VFX, and SFX (sync to network)
        if (HasTrigger(laughMainTrigger))
            SetTriggerSync(laughMainTrigger);
        if (laughImpactVFX != null)
        {
            Vector3 scale = laughImpactVFXScale > 0f ? Vector3.one * laughImpactVFXScale : Vector3.one;
            SpawnVFXSync(laughImpactVFX, laughImpactVFXOffset, scale, true);
        }
        PlaySfx(laughImpactSFX);
        // Shoot projectiles forward
        if (laughProjectilePrefab != null && laughProjectileCount > 0)
        {
            float step = (laughProjectileCount > 1) ? laughProjectileSpreadAngle / (laughProjectileCount - 1) : 0f;
            float startYaw = -laughProjectileSpreadAngle * 0.5f;
            for (int i = 0; i < laughProjectileCount; i++)
            {
                float angle = startYaw + step * i;
                Quaternion rot = transform.rotation * Quaternion.Euler(0f, angle, 0f);
                Vector3 spawnPos = transform.position + rot * laughProjectileSpawnOffset;
                
                // Network-safe instantiation
                GameObject projObj;
                if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                {
                    // Use Resources path for Photon DefaultPool; fall back to prefab.name if path not set
                    string prefabPath = string.IsNullOrEmpty(laughProjectileResourcePath)
                        ? (laughProjectilePrefab != null ? laughProjectilePrefab.name : "")
                        : laughProjectileResourcePath;
                    projObj = PhotonNetwork.Instantiate(prefabPath, spawnPos, rot);
                }
                else
                {
                    projObj = Instantiate(laughProjectilePrefab, spawnPos, rot);
                }
                
                // Optional configuration when a projectile script is present.
                // Avoid direct type reference to keep assembly boundaries flexible.
                var mb = projObj.GetComponent<MonoBehaviour>();
                if (mb != null)
                {
                    var t = mb.GetType();
                    var fDamage = t.GetField("damage");
                    var fOwner = t.GetField("owner");
                    var fSpeed = t.GetField("speed");
                    var fLifetime = t.GetField("lifetime");
                    if (fDamage != null) fDamage.SetValue(mb, laughDamage);
                    if (fOwner != null) fOwner.SetValue(mb, this);
                    if (fSpeed != null) fSpeed.SetValue(mb, laughProjectileSpeed);
                    if (fLifetime != null) fLifetime.SetValue(mb, laughProjectileLifetime);
                }
            }
        }

        var all = Physics.OverlapSphere(transform.position, laughRadius, LayerMask.GetMask("Player"));
        Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
        float halfAngle = Mathf.Clamp(laughConeAngle * 0.5f, 0f, 90f);
        foreach (var c in all)
        {
            Vector3 to = c.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) continue;
            float angle = Vector3.Angle(fwd, to.normalized);
            if (angle <= halfAngle)
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, laughDamage);
            }
        }

        // Stoppage recovery (AI frozen after attack)
        if (laughStoppageTime > 0f)
        {
            float stopTimer = laughStoppageTime;
            float quarterStoppage = laughStoppageTime * 0.75f;
            
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

        // Recovery time (AI can move but skill still on cooldown)
        if (laughRecoveryTime > 0f)
        {
            lastAnySkillRecoveryStart = Time.time; // Mark recovery start for gradual speed
            float recovery = laughRecoveryTime;
            while (recovery > 0f)
            {
                recovery -= Time.deltaTime;
                yield return null;
            }
        }

        activeAbility = null;
        lastLaughTime = Time.time; // Set cooldown timer after all recovery is done
        lastAnySkillRecoveryEnd = Time.time; // Set global lock timer after recovery ends
        EndAction();
    }

    private bool CanPound()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false; // Don't interrupt basic attacks
        if (isBusy || globalBusyTimer > 0f) return false; // Don't interrupt basic attacks or other actions
        if (Time.time - lastPoundTime < poundCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false; // 4 second lock after any skill recovery
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        return Vector3.Distance(transform.position, target.position) <= poundRadius + 0.5f;
    }

    private void StartPound()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoPound());
    }

    private IEnumerator CoPound()
    {
        BeginAction(AIState.Special2);
        
        // Windup phase - separate trigger, VFX, and SFX (sync to network)
        if (HasTrigger(poundWindupTrigger))
            SetTriggerSync(poundWindupTrigger);
        PlaySfx(poundWindupSFX);
        GameObject wind = null;
        if (poundWindupVFX != null)
        {
            Vector3 scale = poundWindupVFXScale > 0f ? Vector3.one * poundWindupVFXScale : Vector3.one;
            wind = SpawnVFXSync(poundWindupVFX, poundWindupVFXOffset, scale, true);
        }
        yield return new WaitForSeconds(Mathf.Max(0f, poundWindup));
        if (wind != null) Destroy(wind);
        
        // Impact phase - separate trigger, VFX, and SFX (sync to network)
        if (HasTrigger(poundMainTrigger))
            SetTriggerSync(poundMainTrigger);
        if (poundImpactVFX != null)
        {
            Vector3 scale = poundImpactVFXScale > 0f ? Vector3.one * poundImpactVFXScale : Vector3.one;
            SpawnVFXSync(poundImpactVFX, poundImpactVFXOffset, scale, true);
        }
        PlaySfx(poundImpactSFX);

        // strip: project forward; hit players within width band
        var all = Physics.OverlapSphere(transform.position + transform.forward * (poundRadius * 0.5f), poundRadius, LayerMask.GetMask("Player"));
        Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
        foreach (var c in all)
        {
            Vector3 rel = c.transform.position - transform.position;
            rel.y = 0f;
            float along = Vector3.Dot(rel, fwd);
            float across = Vector3.Cross(fwd, rel.normalized).magnitude * rel.magnitude;
            if (along >= 0f && along <= poundRadius && Mathf.Abs(across) <= (poundWidth * 0.5f))
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, poundDamage);
            }
        }

        // Stoppage recovery (AI frozen after attack)
        if (poundStoppageTime > 0f)
        {
            float stopTimer = poundStoppageTime;
            float quarterStoppage = poundStoppageTime * 0.75f;
            
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

        // Recovery time (AI can move but skill still on cooldown)
        if (poundRecoveryTime > 0f)
        {
            lastAnySkillRecoveryStart = Time.time; // Mark recovery start for gradual speed
            float recovery = poundRecoveryTime;
            while (recovery > 0f)
            {
                recovery -= Time.deltaTime;
                yield return null;
            }
        }

        activeAbility = null;
        lastPoundTime = Time.time; // Set cooldown timer after all recovery is done
        lastAnySkillRecoveryEnd = Time.time; // Set global lock timer after recovery ends
        EndAction();
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
                float speedMultiplier = Mathf.Lerp(0.3f, 1.0f, progress); // Start at 30% speed, lerp to 100%
                return baseSpeed * speedMultiplier;
            }
        }
        
        return baseSpeed;
    }
}