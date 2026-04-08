using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class BadDiwataPunsoAI : BaseEnemyAI
{
    [Header("Roots Attack")]
    public int rootsDamage = 10;
    public float rootsRange = 7.0f;
    public float rootsWindup = 0.35f;
    public float rootsCooldown = 7f;
    public GameObject rootsVFX;
    public AudioClip rootsSFX;

    [Header("Nature Bolt")]
    public int natureBoltDamage = 15;
    public float natureBoltProjectileSpeed = 14f;
    public float natureBoltWindup = 0.2f;
    public float natureBoltCooldown = 4f;
    public GameObject natureBoltVFX;
    public AudioClip natureBoltSFX;

    [Header("Animation")]
    public string rootsTrigger = "Roots";
    public string natureBoltTrigger = "NatureBolt";

    // Runtime state
    private float lastRootsTime = -9999f;
    private float lastNatureBoltTime = -9999f;

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

        var canRoots = new ConditionNode(blackboard, CanRoots, "can_roots");
        var doRoots = new ActionNode(blackboard, () => { StartRoots(); return NodeState.Success; }, "roots");
        var canNatureBolt = new ConditionNode(blackboard, CanNatureBolt, "can_nature_bolt");
        var doNatureBolt = new ActionNode(blackboard, () => { StartNatureBolt(); return NodeState.Success; }, "nature_bolt");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "roots_seq").Add(canRoots, doRoots),
                        new Sequence(blackboard, "nature_bolt_seq").Add(canNatureBolt, doNatureBolt),
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
        if (enemyData == null) return;
        if (Time.time - lastAttackTime < enemyData.attackCooldown) return;

        var target = blackboard.Get<Transform>("target");
        if (target == null) return;

        if (animator != null && HasTrigger(attackTrigger)) SetTriggerSync(attackTrigger);
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
        if (CanRoots()) { StartRoots(); return true; }
        if (CanNatureBolt()) { StartNatureBolt(); return true; }
        return false;
    }

    #endregion

    #region Roots Attack

    private bool CanRoots()
    {
        if (Time.time - lastRootsTime < rootsCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance <= rootsRange;
    }

    private void StartRoots()
    {
        lastRootsTime = Time.time;
        StartCoroutine(CoRoots());
    }

    private IEnumerator CoRoots()
    {
        if (animator != null && HasTrigger(rootsTrigger)) SetTriggerSync(rootsTrigger);
        PlaySfx(rootsSFX);
        if (rootsVFX != null) Instantiate(rootsVFX, transform.position, transform.rotation);

        yield return new WaitForSeconds(rootsWindup);

        var hitColliders = Physics.OverlapSphere(transform.position, rootsRange);
        foreach (var hit in hitColliders)
        {
            if (hit.CompareTag("Player"))
            {
                var playerStats = hit.GetComponent<PlayerStats>();
                if (playerStats != null) DamageRelay.ApplyToPlayer(playerStats.gameObject, rootsDamage);
            }
        }
    }

    #endregion

    #region Nature Bolt

    private bool CanNatureBolt()
    {
        if (Time.time - lastNatureBoltTime < natureBoltCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance <= natureBoltProjectileSpeed * 2f; // Reasonable range for projectile
    }

    private void StartNatureBolt()
    {
        lastNatureBoltTime = Time.time;
        StartCoroutine(CoNatureBolt());
    }

    private IEnumerator CoNatureBolt()
    {
        if (animator != null && HasTrigger(natureBoltTrigger)) SetTriggerSync(natureBoltTrigger);
        PlaySfx(natureBoltSFX);
        if (natureBoltVFX != null) Instantiate(natureBoltVFX, transform.position, transform.rotation);

        yield return new WaitForSeconds(natureBoltWindup);

        // Simple projectile damage (could be enhanced with actual projectile)
        var target = blackboard.Get<Transform>("target");
        if (target != null)
        {
            float distance = Vector3.Distance(transform.position, target.position);
            if (distance <= natureBoltProjectileSpeed * 2f)
            {
                var playerStats = target.GetComponent<PlayerStats>();
        if (playerStats != null) DamageRelay.ApplyToPlayer(playerStats.gameObject, natureBoltDamage);
            }
        }
    }

    #endregion
}