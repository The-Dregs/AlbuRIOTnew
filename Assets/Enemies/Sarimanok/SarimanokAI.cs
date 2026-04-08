using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class SarimanokAI : BaseEnemyAI
{
    [Header("Despair Cry (DoT)")]
    public int cryTickDamage = 4;
    public float cryTickInterval = 0.5f;
    public int cryTicks = 6;
    public float cryRadius = 5f;
    public float cryWindup = 0.6f;
    public float cryCooldown = 10f;
    public string cryTrigger = "Cry";
    public AudioClip cryWindupSFX;
    public AudioClip cryImpactSFX;

    [Header("Blazing Feathers (rays)")]
    public string featherProjectilePrefabPath = "Enemies/Projectiles/Feather Projectile"; // Resources path
    public Transform featherMuzzle;
    public int featherDamage = 10;
    public int featherCount = 5;
    public float featherSpreadDeg = 12f;
    public float featherRange = 10f;
    public float featherWindup = 0.5f;
    public float featherCooldown = 9f;
    public float featherSpeed = 18f;
    public string featherTrigger = "Feathers";
    public AudioClip featherWindupSFX;
    public AudioClip featherImpactSFX;

    [Header("Skill Selection Tuning")]
    public float cryPreferredMinDistance = 2.5f;
    public float cryPreferredMaxDistance = 7.5f;
    [Range(0f, 1f)] public float crySkillWeight = 0.8f;
    public float cryStoppageTime = 1f;
    public float cryRecoveryTime = 0.5f;
    public float feathersPreferredMinDistance = 4f;
    public float feathersPreferredMaxDistance = 15f;
    [Range(0f, 1f)] public float feathersSkillWeight = 0.7f;
    public float feathersStoppageTime = 1f;
    public float feathersRecoveryTime = 0.5f;

    private float lastCryTime = -9999f;
    private float lastFeatherTime = -9999f;
    private Coroutine activeAbility;

    protected override void InitializeEnemy()
    {
        // No special initialization for now
    }

    protected override void PerformBasicAttack()
    {
        if (enemyData == null) return;
        if (Time.time - lastAttackTime < enemyData.attackCooldown) return;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return;
        if (HasTrigger(attackTrigger)) SetTriggerSync(attackTrigger);
        PlayAttackWindupSfx();
        float radius = Mathf.Max(0.8f, enemyData.attackRange);
        Vector3 center = transform.position + transform.forward * (enemyData.attackRange * 0.5f);
        var cols = Physics.OverlapSphere(center, radius, LayerMask.GetMask("Player"));
        PlayAttackImpactSfx();
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, enemyData.basicDamage);
        }
        lastAttackTime = Time.time;
        attackLockTimer = enemyData.attackMoveLock;
    }

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTarget, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");
        var targetInAttack = new ConditionNode(blackboard, TargetInAttackRangeAndFacing, "in_attack_range_facing");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");
        var canCry = new ConditionNode(blackboard, CanCry, "can_cry");
        var doCry = new ActionNode(blackboard, () => { StartCry(); return NodeState.Success; }, "cry");
        var canFeathers = new ConditionNode(blackboard, CanFeathers, "can_feathers");
        var doFeathers = new ActionNode(blackboard, () => { StartFeathers(); return NodeState.Success; }, "feathers");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "cry_seq").Add(canCry, doCry),
                        new Sequence(blackboard, "feathers_seq").Add(canFeathers, doFeathers),
                        new Sequence(blackboard, "basic_seq").Add(targetInAttack, basicAttack),
                        moveToTarget
                    )
                ),
                new ActionNode(blackboard, Patrol, "patrol")
            );
    }

    protected override bool TrySpecialAbilities()
    {
        if (isBusy || activeAbility != null) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        bool facingTarget = IsFacingTarget(target, SpecialFacingAngle);
        bool inCryRange = dist >= cryPreferredMinDistance && dist <= cryPreferredMaxDistance;
        bool inFeathersRange = dist >= feathersPreferredMinDistance && dist <= feathersPreferredMaxDistance;
        float cryMid = (cryPreferredMinDistance + cryPreferredMaxDistance) * 0.5f;
        float feathersMid = (feathersPreferredMinDistance + feathersPreferredMaxDistance) * 0.5f;
        float cryScore = (inCryRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(cryMid - dist) / 5f)) * crySkillWeight : 0f;
        float featherScore = (inFeathersRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(feathersMid - dist) / 10f)) * feathersSkillWeight : 0f;
        if (CanCry() && cryScore >= featherScore && cryScore > 0.15f) { StartCry(); return true; }
        if (CanFeathers() && featherScore > cryScore && featherScore > 0.15f) { StartFeathers(); return true; }
        return false;
    }

    private bool CanCry()
    {
        if (Time.time - lastCryTime < cryCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null && Vector3.Distance(transform.position, target.position) <= cryRadius + 1f;
    }
    private void StartCry()
    {
        lastCryTime = Time.time;
        activeAbility = StartCoroutine(CoCry());
    }
    private IEnumerator CoCry()
    {
        BeginAction(AIState.Special1);
        Quaternion lockedRotation = transform.rotation;
        if (HasTrigger(cryTrigger)) SetTriggerSync(cryTrigger);
        PlaySfx(cryWindupSFX);
        yield return new WaitForSeconds(Mathf.Max(0f, cryWindup));
        PlaySfx(cryImpactSFX);
        int ticks = Mathf.Max(1, cryTicks);
        float interval = Mathf.Max(0.1f, cryTickInterval);
        for (int i = 0; i < ticks; i++)
        {
            var cols = Physics.OverlapSphere(transform.position, cryRadius, LayerMask.GetMask("Player"));
            foreach (var c in cols)
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, cryTickDamage);
            }
            yield return new WaitForSeconds(interval);
        }
        if (cryStoppageTime > 0f)
        {
            float stopTimer = cryStoppageTime;
            float quarterStoppage = cryStoppageTime * 0.75f;
            while (stopTimer > 0f)
            {
                stopTimer -= Time.deltaTime;
                transform.rotation = lockedRotation;
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                if (stopTimer <= quarterStoppage && HasBool("Exhausted")) SetBoolSync("Exhausted", true);
                yield return null;
            }
            if (HasBool("Exhausted")) SetBoolSync("Exhausted", false);
        }
        EndAction();
        if (cryRecoveryTime > 0f)
            yield return new WaitForSeconds(cryRecoveryTime);
        activeAbility = null;
    }

    private bool CanFeathers()
    {
        if (Time.time - lastFeatherTime < featherCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null;
    }
    private void StartFeathers()
    {
        lastFeatherTime = Time.time;
        activeAbility = StartCoroutine(CoFeathers());
    }
    private IEnumerator CoFeathers()
    {
        BeginAction(AIState.Special2);
        Quaternion lockedRotation = transform.rotation;
        if (HasTrigger(featherTrigger)) SetTriggerSync(featherTrigger);
        PlaySfx(featherWindupSFX);
        yield return new WaitForSeconds(Mathf.Max(0f, featherWindup));
        PlaySfx(featherImpactSFX);
        Vector3 muzzlePos = featherMuzzle != null ? featherMuzzle.position : (transform.position + transform.forward * 1.2f);
        for (int i = 0; i < Mathf.Max(1, featherCount); i++)
        {
            float t = (featherCount <= 1) ? 0f : (i / (float)(featherCount - 1));
            float angle = -featherSpreadDeg * 0.5f + (featherSpreadDeg * t);
            Quaternion rot = Quaternion.AngleAxis(angle, Vector3.up) * transform.rotation;
            if (!string.IsNullOrEmpty(featherProjectilePrefabPath))
            {
                // Network-safe instantiation
                GameObject projObj;
                if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                    projObj = PhotonNetwork.Instantiate(featherProjectilePrefabPath, muzzlePos, rot);
                else
                {
                    // Load prefab from Resources for offline mode
                    GameObject prefab = Resources.Load<GameObject>(featherProjectilePrefabPath);
                    if (prefab != null)
                        projObj = Instantiate(prefab, muzzlePos, rot);
                    else
                    {
                        Debug.LogError($"[SarimanokAI] Failed to load projectile prefab from path: {featherProjectilePrefabPath}");
                        yield break;
                    }
                }
                
                var proj = projObj.GetComponent<EnemyProjectile>();
                if (proj != null)
                {
                    proj.Initialize(gameObject, featherDamage, featherSpeed, featherRange / Mathf.Max(0.1f, featherSpeed), null);
                    proj.maxDistance = featherRange;
                }
            }
        }
        yield return null;
        if (feathersStoppageTime > 0f)
        {
            float stopTimer = feathersStoppageTime;
            float quarterStoppage = feathersStoppageTime * 0.75f;
            while (stopTimer > 0f)
            {
                stopTimer -= Time.deltaTime;
                transform.rotation = lockedRotation;
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                if (stopTimer <= quarterStoppage && HasBool("Exhausted")) SetBoolSync("Exhausted", true);
                yield return null;
            }
            if (HasBool("Exhausted")) SetBoolSync("Exhausted", false);
        }
        EndAction();
        if (feathersRecoveryTime > 0f)
            yield return new WaitForSeconds(feathersRecoveryTime);
        activeAbility = null;
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
}