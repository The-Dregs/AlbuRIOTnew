using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class DiwataPunsoAI : BaseEnemyAI
{
    [Header("Roots (snare hit)")]
    public int rootsDamage = 10;
    public float rootsRange = 7f;
    public float rootsWindup = 0.35f;
    public float rootsCooldown = 7f;
    public GameObject rootsWindupVFX;
    public GameObject rootsImpactVFX;
    public Vector3 rootsVFXOffset = Vector3.zero;
    public float rootsVFXScale = 1.0f;
    public AudioClip rootsWindupSFX;
    public AudioClip rootsImpactSFX;
    public string rootsTrigger = "Roots";

    [Header("Nature Bolt (projectile)")]
    public string boltProjectilePrefabPath = "Enemies/Projectiles/Bolt Projectile"; // Resources path
    public Transform boltMuzzle;
    public int boltDamage = 15;
    public float boltSpeed = 14f;
    public float boltLifetime = 1.2f;
    public float boltCooldown = 4f;
    public float boltRange = 20f;
    public GameObject boltWindupVFX;
    public Vector3 boltVFXOffset = Vector3.zero;
    public float boltVFXScale = 1.0f;
    public AudioClip boltWindupSFX;
    public AudioClip boltFireSFX;
    public string boltTrigger = "Bolt";

    [Header("Skill Selection Tuning")]
    public float rootsPreferredMinDistance = 3.2f;
    public float rootsPreferredMaxDistance = 8.5f;
    [Range(0f, 1f)] public float rootsSkillWeight = 0.7f;
    public float rootsStoppageTime = 1f;
    public float rootsRecoveryTime = 0.5f;
    public float boltPreferredMinDistance = 7f;
    public float boltPreferredMaxDistance = 20f;
    [Range(0f, 1f)] public float boltSkillWeight = 0.8f;
    public float boltStoppageTime = 1f;
    public float boltRecoveryTime = 0.5f;

    private float lastRootsTime = -9999f;
    private float lastBoltTime = -9999f;
    private Coroutine activeAbility;

    protected override void InitializeEnemy() { }

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTarget, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");
        var targetInAttack = new ConditionNode(blackboard, TargetInAttackRangeAndFacing, "in_attack_range_facing");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");
        var canRoots = new ConditionNode(blackboard, CanRoots, "can_roots");
        var doRoots = new ActionNode(blackboard, () => { StartRoots(); return NodeState.Success; }, "roots");
        var canBolt = new ConditionNode(blackboard, CanBolt, "can_bolt");
        var doBolt = new ActionNode(blackboard, () => { StartBolt(); return NodeState.Success; }, "bolt");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "roots_seq").Add(canRoots, doRoots),
                        new Sequence(blackboard, "bolt_seq").Add(canBolt, doBolt),
                        new Sequence(blackboard, "basic_seq").Add(targetInAttack, basicAttack),
                        moveToTarget
                    )
                ),
                new ActionNode(blackboard, Patrol, "patrol")
            );
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
        PlayAttackImpactSfx();
        var cols = Physics.OverlapSphere(center, radius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, enemyData.basicDamage);
        }

        lastAttackTime = Time.time;
        attackLockTimer = enemyData.attackMoveLock;
    }

    protected override bool TrySpecialAbilities()
    {
        if (isBusy || activeAbility != null) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        bool facingTarget = IsFacingTarget(target, SpecialFacingAngle);
        bool inRootsRange = dist >= rootsPreferredMinDistance && dist <= rootsPreferredMaxDistance;
        bool inBoltRange = dist >= boltPreferredMinDistance && dist <= boltPreferredMaxDistance;
        float rootsMid = (rootsPreferredMinDistance + rootsPreferredMaxDistance) * 0.5f;
        float boltMid = (boltPreferredMinDistance + boltPreferredMaxDistance) * 0.5f;
        float rootsScore = (inRootsRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(rootsMid - dist) / 5f)) * rootsSkillWeight : 0f;
        float boltScore = (inBoltRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(boltMid - dist) / 6f)) * boltSkillWeight : 0f;
        if (CanRoots() && rootsScore >= boltScore && rootsScore > 0.15f) { StartRoots(); return true; }
        if (CanBolt() && boltScore > rootsScore && boltScore > 0.15f) { StartBolt(); return true; }
        return false;
    }

    private bool CanRoots()
    {
        if (Time.time - lastRootsTime < rootsCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        return Vector3.Distance(transform.position, target.position) <= rootsRange + 0.5f;
    }

    private void StartRoots()
    {
        lastRootsTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoRoots());
    }

    private IEnumerator CoRoots()
    {
        BeginAction(AIState.Special1);
        Quaternion lockedRotation = transform.rotation;
        if (HasTrigger(rootsTrigger)) SetTriggerSync(rootsTrigger);
        PlaySfx(rootsWindupSFX);
        GameObject wind = null;
        if (rootsWindupVFX != null)
        {
            wind = Instantiate(rootsWindupVFX, transform);
            wind.transform.localPosition = rootsVFXOffset;
            if (rootsVFXScale > 0f) wind.transform.localScale = Vector3.one * rootsVFXScale;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, rootsWindup));
        if (wind != null) Destroy(wind);
        if (rootsImpactVFX != null)
        {
            var fx = Instantiate(rootsImpactVFX, transform);
            fx.transform.localPosition = rootsVFXOffset;
            if (rootsVFXScale > 0f) fx.transform.localScale = Vector3.one * rootsVFXScale;
        }
        PlaySfx(rootsImpactSFX);

        var target = blackboard.Get<Transform>("target");
        if (target != null)
        {
            // simple LoS check
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            Vector3 dir = (target.position + Vector3.up * 0.5f) - origin;
            if (Physics.Raycast(origin, dir.normalized, out var hit, rootsRange))
            {
                var ps = hit.collider.GetComponentInParent<PlayerStats>();
                if (ps != null)
                {
                    DamageRelay.ApplyToPlayer(ps.gameObject, rootsDamage);
                    // snare effect can be handled by status system if available
                }
            }
        }
        if (rootsStoppageTime > 0f)
        {
            float stopTimer = rootsStoppageTime;
            float quarterStoppage = rootsStoppageTime * 0.75f;
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
        if (rootsRecoveryTime > 0f)
            yield return new WaitForSeconds(rootsRecoveryTime);
        activeAbility = null;
    }

    private bool CanBolt()
    {
        if (Time.time - lastBoltTime < boltCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null;
    }

    private void StartBolt()
    {
        lastBoltTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoBolt());
    }

    private IEnumerator CoBolt()
    {
        BeginAction(AIState.Special2);
        Quaternion lockedRotation = transform.rotation;
        if (HasTrigger(boltTrigger)) SetTriggerSync(boltTrigger);
        PlaySfx(boltWindupSFX);
        GameObject wind = null;
        if (boltWindupVFX != null)
        {
            wind = Instantiate(boltWindupVFX, transform);
            wind.transform.localPosition = boltVFXOffset;
            if (boltVFXScale > 0f) wind.transform.localScale = Vector3.one * boltVFXScale;
        }
        yield return new WaitForSeconds(0.1f);
        if (wind != null) Destroy(wind);
        PlaySfx(boltFireSFX);

        var target = blackboard.Get<Transform>("target");
        Vector3 muzzlePos = boltMuzzle != null ? boltMuzzle.position : (transform.position + transform.forward * 1.0f);
        Vector3 dir = target != null ? (target.position - muzzlePos) : transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) dir.Normalize(); else dir = transform.forward;

        if (!string.IsNullOrEmpty(boltProjectilePrefabPath))
        {
            // Network-safe instantiation
            GameObject projObj;
            if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                projObj = PhotonNetwork.Instantiate(boltProjectilePrefabPath, muzzlePos, Quaternion.LookRotation(dir));
            else
            {
                // Load prefab from Resources for offline mode
                GameObject prefab = Resources.Load<GameObject>(boltProjectilePrefabPath);
                if (prefab != null)
                    projObj = Instantiate(prefab, muzzlePos, Quaternion.LookRotation(dir));
                else
                {
                    Debug.LogError($"[DiwataPunsoAI] Failed to load projectile prefab from path: {boltProjectilePrefabPath}");
                    yield break;
                }
            }
            
            var proj = projObj.GetComponent<EnemyProjectile>();
            if (proj != null)
            {
                proj.Initialize(gameObject, boltDamage, boltSpeed, boltLifetime, null);
                proj.maxDistance = boltRange;
            }
        }
        else
        {
            // fallback instant line hit
            if (Physics.Raycast(muzzlePos, dir, out var rh, boltRange, ~0))
            {
                var ps = rh.collider.GetComponentInParent<PlayerStats>();
        if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, boltDamage);
            }
        }
        yield return null;
        if (boltStoppageTime > 0f)
        {
            float stopTimer = boltStoppageTime;
            float quarterStoppage = boltStoppageTime * 0.75f;
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
        if (boltRecoveryTime > 0f)
            yield return new WaitForSeconds(boltRecoveryTime);
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