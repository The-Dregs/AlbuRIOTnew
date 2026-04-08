using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class PugotAI : BaseEnemyAI
{
    [Header("Head Throw (projectile + curse field)")]
    public string headProjectilePrefabPath = "Enemies/Projectiles/Head Projectile"; // Resources path
    public Transform headProjectileMuzzle;
    public int headDamage = 22;
    public float headRange = 8f;
    public float headWindup = 0.6f;
    public float headCooldown = 9f;
    public float headProjectileSpeed = 16f;
    public float headProjectileLifetime = 1.2f;
    public GameObject headWindupVFX;
    public Vector3 headVFXOffset = Vector3.zero;
    public float headVFXScale = 1.0f;
    public AudioClip headWindupSFX;
    public AudioClip headThrowSFX;
    public string headThrowTrigger = "HeadThrow";

    [Header("Grave Step (AOE stomp)")]
    public int graveDamage = 14;
    public float graveRadius = 2.5f;
    public float graveWindup = 0.5f;
    public float graveCooldown = 7f;
    public GameObject graveWindupVFX;
    public GameObject graveImpactVFX;
    public Vector3 graveVFXOffset = Vector3.zero;
    public float graveVFXScale = 1.0f;
    public AudioClip graveWindupSFX;
    public AudioClip graveImpactSFX;
    public string graveStepTrigger = "GraveStep";

    [Header("Skill Selection Tuning")]
    public float headThrowPreferredMinDistance = 5f;
    public float headThrowPreferredMaxDistance = 16f;
    [Range(0f, 1f)] public float headThrowSkillWeight = 0.7f;
    public float headThrowStoppageTime = 1f;
    public float headThrowRecoveryTime = 0.5f;
    public float graveStepPreferredMinDistance = 2f;
    public float graveStepPreferredMaxDistance = 6.5f;
    [Range(0f, 1f)] public float graveStepSkillWeight = 0.85f;
    public float graveStepStoppageTime = 1f;
    public float graveStepRecoveryTime = 0.5f;

    private float lastHeadTime = -9999f;
    private float lastGraveTime = -9999f;
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
        var canHead = new ConditionNode(blackboard, CanHeadThrow, "can_head");
        var doHead = new ActionNode(blackboard, () => { StartHeadThrow(); return NodeState.Success; }, "head_throw");
        var canGrave = new ConditionNode(blackboard, CanGraveStep, "can_grave");
        var doGrave = new ActionNode(blackboard, () => { StartGraveStep(); return NodeState.Success; }, "grave_step");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "head_seq").Add(canHead, doHead),
                        new Sequence(blackboard, "grave_seq").Add(canGrave, doGrave),
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
        bool inHeadRange = dist >= headThrowPreferredMinDistance && dist <= headThrowPreferredMaxDistance;
        bool inGraveRange = dist >= graveStepPreferredMinDistance && dist <= graveStepPreferredMaxDistance;
        float headMid = (headThrowPreferredMinDistance + headThrowPreferredMaxDistance) * 0.5f;
        float graveMid = (graveStepPreferredMinDistance + graveStepPreferredMaxDistance) * 0.5f;
        float headScore = (inHeadRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(headMid - dist) / 10f)) * headThrowSkillWeight : 0f;
        float graveScore = (inGraveRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(graveMid - dist) / 6f)) * graveStepSkillWeight : 0f;
        if (CanHeadThrow() && headScore >= graveScore && headScore > 0.15f) { StartHeadThrow(); return true; }
        if (CanGraveStep() && graveScore > headScore && graveScore > 0.15f) { StartGraveStep(); return true; }
        return false;
    }

    private bool CanHeadThrow()
    {
        if (Time.time - lastHeadTime < headCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float d = Vector3.Distance(transform.position, target.position);
        return d <= headRange + 1f;
    }

    private void StartHeadThrow()
    {
        lastHeadTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoHeadThrow());
    }

    private IEnumerator CoHeadThrow()
    {
        BeginAction(AIState.Special1);
        Quaternion lockedRotation = transform.rotation;
        if (HasTrigger(headThrowTrigger)) SetTriggerSync(headThrowTrigger);
        PlaySfx(headWindupSFX);
        GameObject wind = null;
        if (headWindupVFX != null)
        {
            wind = Instantiate(headWindupVFX, transform);
            wind.transform.localPosition = headVFXOffset;
            if (headVFXScale > 0f) wind.transform.localScale = Vector3.one * headVFXScale;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, headWindup));
        if (wind != null) Destroy(wind);
        PlaySfx(headThrowSFX);

        var target = blackboard.Get<Transform>("target");
        Vector3 dir = target != null ? (target.position - (headProjectileMuzzle != null ? headProjectileMuzzle.position : transform.position)) : transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) dir.Normalize(); else dir = transform.forward;

        if (!string.IsNullOrEmpty(headProjectilePrefabPath))
        {
            Vector3 spawnPos = headProjectileMuzzle != null ? headProjectileMuzzle.position : (transform.position + transform.forward * 1.2f);
            Quaternion rot = Quaternion.LookRotation(dir);
            
            // Network-safe instantiation
            GameObject projObj;
            if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                projObj = PhotonNetwork.Instantiate(headProjectilePrefabPath, spawnPos, rot);
            else
            {
                // Load prefab from Resources for offline mode
                GameObject prefab = Resources.Load<GameObject>(headProjectilePrefabPath);
                if (prefab != null)
                    projObj = Instantiate(prefab, spawnPos, rot);
                else
                {
                    Debug.LogError($"[PugotAI] Failed to load projectile prefab from path: {headProjectilePrefabPath}");
                    yield break;
                }
            }
            
            var proj = projObj.GetComponent<EnemyProjectile>();
            if (proj != null)
            {
                proj.Initialize(gameObject, headDamage, headProjectileSpeed, headProjectileLifetime, null);
                proj.maxDistance = headRange + 2f;
            }
        }
        else
        {
            // fallback instant hit
            var cols = Physics.OverlapSphere(transform.position + transform.forward * (headRange * 0.5f), 1.0f, LayerMask.GetMask("Player"));
            foreach (var c in cols)
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null) { DamageRelay.ApplyToPlayer(ps.gameObject, headDamage); break; }
            }
        }
        if (headThrowStoppageTime > 0f)
        {
            float stopTimer = headThrowStoppageTime;
            float quarterStoppage = headThrowStoppageTime * 0.75f;
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
        if (headThrowRecoveryTime > 0f)
            yield return new WaitForSeconds(headThrowRecoveryTime);
        activeAbility = null;
    }

    private bool CanGraveStep()
    {
        if (Time.time - lastGraveTime < graveCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null;
    }

    private void StartGraveStep()
    {
        lastGraveTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoGraveStep());
    }

    private IEnumerator CoGraveStep()
    {
        BeginAction(AIState.Special2);
        Quaternion lockedRotation = transform.rotation;
        if (HasTrigger(graveStepTrigger)) SetTriggerSync(graveStepTrigger);
        PlaySfx(graveWindupSFX);
        GameObject wind = null;
        if (graveWindupVFX != null)
        {
            wind = Instantiate(graveWindupVFX, transform);
            wind.transform.localPosition = graveVFXOffset;
            if (graveVFXScale > 0f) wind.transform.localScale = Vector3.one * graveVFXScale;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, graveWindup));
        if (wind != null) Destroy(wind);
        if (graveImpactVFX != null)
        {
            var fx = Instantiate(graveImpactVFX, transform);
            fx.transform.localPosition = graveVFXOffset;
            if (graveVFXScale > 0f) fx.transform.localScale = Vector3.one * graveVFXScale;
        }
        PlaySfx(graveImpactSFX);

        var cols = Physics.OverlapSphere(transform.position, graveRadius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
        if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, graveDamage);
        }
        if (graveStepStoppageTime > 0f)
        {
            float stopTimer = graveStepStoppageTime;
            float quarterStoppage = graveStepStoppageTime * 0.75f;
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
        if (graveStepRecoveryTime > 0f)
            yield return new WaitForSeconds(graveStepRecoveryTime);
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