using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class AmomongoAI : BaseEnemyAI
{
    [Header("Savage Slam")]
    public int slamDamage = 30;
    public int slamDamageBerserk = 39;
    public float slamRadius = 3.5f;
    public float slamWindup = 0.45f;
    public float slamCooldown = 6.5f;
    public GameObject slamVFX;
    public Vector3 slamVFXOffset = Vector3.zero;
    public float slamVFXScale = 1.0f;
    public GameObject slamImpactVFX;
    public Vector3 slamImpactVFXOffset = Vector3.zero;
    public float slamImpactVFXScale = 1.0f;
    public AudioClip slamImpactSFX;
    public AudioClip slamSFX;

    [Header("Berserk Frenzy")]
    public float berserkDuration = 4.0f;
    public float berserkDamageMultiplier = 1.3f;
    public float berserkMoveMultiplier = 1.25f;
    public float berserkWindup = 0.4f;
    public float berserkCooldown = 12f;
    public GameObject berserkActivateVFX;
    public Vector3 berserkActivateVFXOffset = Vector3.zero;
    public float berserkActivateVFXScale = 1.0f;
    public GameObject berserkImpactVFX;
    public Vector3 berserkImpactVFXOffset = Vector3.zero;
    public float berserkImpactVFXScale = 1.0f;
    public AudioClip berserkImpactSFX;
    public AudioClip berserkSFX;
    public GameObject berserkActiveVFX;

    [Header("Slam Lunge")]
    public float slamLungeDistance = 1.5f;
    public float slamLungeDuration = 0.2f;
    public float slamStopDuration = 0.25f;

    [Header("Basic Attack Tuning")]
    [Tooltip("Override basic windup. If 0, uses enemyData.attackWindup")]
    public float basicWindup = 0f; // 0 = use enemyData.attackWindup
    public float basicPostStop = 0.2f;

    [Header("Animation")]
    public string slamTrigger = "Slam";
    public string berserkTrigger = "Berserk";
    public string busyBool = "Busy";

    // Runtime state
    private float lastSlamTime = -9999f;
    private float lastBerserkTime = -9999f;
    private bool isBerserk = false;
    private float berserkTimer = 0f;
    private Coroutine activeAbility;
    private Coroutine basicRoutine;

    // Debug accessors
    public bool IsBerserk => isBerserk;
    public float BerserkTimeRemaining => Mathf.Max(0f, berserkTimer);
    public float SlamCooldownRemaining => Mathf.Max(0f, slamCooldown - (Time.time - lastSlamTime));
    public float BerserkCooldownRemaining => Mathf.Max(0f, berserkCooldown - (Time.time - lastBerserkTime));

    #region Initialization

    protected override void InitializeEnemy() { }

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTarget, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");
        var targetInAttack = new ConditionNode(blackboard, TargetInAttackRangeAndFacing, "in_attack_range_facing");
        var basicAttack = new ActionNode(blackboard, BasicAttackNode, "basic");

        var canSlam = new ConditionNode(blackboard, CanSlam, "can_slam");
        var doSlam = new ActionNode(blackboard, () => { StartSlam(); return NodeState.Success; }, "slam");
        var canBerserk = new ConditionNode(blackboard, CanBerserk, "can_berserk");
        var doBerserk = new ActionNode(blackboard, () => { StartBerserk(); return NodeState.Success; }, "berserk");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "slam_seq").Add(canSlam, doSlam),
                        new Sequence(blackboard, "berserk_seq").Add(canBerserk, doBerserk),
                        new Sequence(blackboard, "basic_seq").Add(targetInAttack, basicAttack),
            moveToTarget
                    )
                ),
                new ActionNode(blackboard, Patrol, "patrol")
            );
    }

    #endregion

    // Movement boost during berserk
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
        return isBerserk ? baseSpeed * berserkMoveMultiplier : baseSpeed;
    }

    #region BaseEnemyAI Overrides

    protected override void PerformBasicAttack()
    {
        if (basicRoutine != null) return;
        var target = blackboard.Get<Transform>("target");
        if (target == null || enemyData == null) return;
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

        float wind = Mathf.Max(0f, basicWindup > 0f ? basicWindup : enemyData.attackWindup);
        while (wind > 0f)
        {
            wind -= Time.deltaTime;
            transform.rotation = lockedRotation;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }

        // Impact animation trigger (sync to network)
        if (HasTrigger(attackImpactTrigger))
            SetTriggerSync(attackImpactTrigger);
        PlayAttackImpactSfx();

        // Apply damage
        float radius = Mathf.Max(0.8f, enemyData.attackRange);
        Vector3 center = transform.position + transform.forward * (enemyData.attackRange * 0.5f);
        var cols = Physics.OverlapSphere(center, radius, LayerMask.GetMask("Player"));
        int baseDmg = enemyData.basicDamage;
        int dmg = isBerserk ? Mathf.RoundToInt(baseDmg * berserkDamageMultiplier) : baseDmg;
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
        if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, dmg);
        }

        // Exhausted phase - lock position, rotation, set Exhausted animator
        float post = Mathf.Max(0f, basicPostStop);
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
        if (CanSlam()) { StartSlam(); return true; }
        if (CanBerserk()) { StartBerserk(); return true; }
        return false;
    }

    #endregion

    // Return Success only when a basic attack actually fired.
    private NodeState BasicAttackNode()
    {
        if (enemyData == null) return NodeState.Failure;
        if (basicRoutine != null) return NodeState.Running;
        if (Time.time - lastAttackTime < enemyData.attackCooldown) return NodeState.Failure;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return NodeState.Failure;
        PerformBasicAttack();
        return NodeState.Running;
    }

    #region Savage Slam

    private bool CanSlam()
    {
        if (activeAbility != null) return false;
        if (globalBusyTimer > 0f) return false; // respect global post-action busy
        if (Time.time - lastSlamTime < slamCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance <= slamRadius + 2f;
    }

    private void StartSlam()
    {
        if (activeAbility != null) return;
        lastSlamTime = Time.time;
        // Also gate basic attacks while special is in progress
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoSlam());
    }

    private IEnumerator CoSlam()
    {
        BeginAction(AIState.Special1);
        if (HasBool(busyBool)) SetBoolSync(busyBool, true);
        if (HasTrigger(slamTrigger)) SetTriggerSync(slamTrigger);

        // Windup SFX (stoppable)
        if (audioSource != null && slamSFX != null)
        {
            audioSource.clip = slamSFX;
            audioSource.loop = false;
            audioSource.Play();
        }
        // Windup VFX
        GameObject slamWindupFx = null;
        if (slamVFX != null)
        {
            slamWindupFx = Instantiate(slamVFX, transform);
            slamWindupFx.transform.localPosition = slamVFXOffset;
            if (slamVFXScale > 0f) slamWindupFx.transform.localScale = Vector3.one * slamVFXScale;
        }

        float windup = Mathf.Max(0f, slamWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        // End windup visuals/audio and play activation VFX/SFX aligned with attack execution
        if (slamWindupFx != null) Destroy(slamWindupFx);
        if (audioSource != null && audioSource.clip == slamSFX)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }
        // (Activation FX/SFX will be played exactly at impact below)

        // Short forward lunge before applying damage
        float lungeTime = Mathf.Max(0f, slamLungeDuration);
        if (lungeTime > 0f)
        {
            float lungeSpeed = (slamLungeDistance / lungeTime);
            while (lungeTime > 0f)
            {
                lungeTime -= Time.deltaTime;
                if (controller != null && controller.enabled)
                {
                    controller.SimpleMove(transform.forward * lungeSpeed);
                }
                yield return null;
            }
        }

        // Apply AOE damage (play impact FX/SFX exactly at this moment)
        if (slamImpactVFX != null)
        {
            // Spawn unparented at world position so impact effect stays in place
            Vector3 worldPos = transform.position + transform.TransformDirection(slamImpactVFXOffset);
            var fx = Instantiate(slamImpactVFX, worldPos, transform.rotation);
            if (slamImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * slamImpactVFXScale;
        }
        PlaySfx(slamImpactSFX);
        
        int damage = isBerserk ? slamDamageBerserk : slamDamage;
        var cols = Physics.OverlapSphere(transform.position, slamRadius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
        if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, damage);
        }

        // Post-slam stoppage
        float postStop = Mathf.Max(0f, slamStopDuration);
        while (postStop > 0f)
        {
            postStop -= Time.deltaTime;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }

        // Recovery
        float recovery = enemyData != null ? Mathf.Max(0.1f, enemyData.attackMoveLock) : 0.3f;
        while (recovery > 0f)
        {
            recovery -= Time.deltaTime;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }

        if (HasBool(busyBool)) SetBoolSync(busyBool, false);
        activeAbility = null;
        lastAttackTime = Time.time;
        attackLockTimer = enemyData != null ? enemyData.attackMoveLock : 0.25f;
        EndAction();
    }

    #endregion

    #region Berserk Frenzy

    private bool CanBerserk()
    {
        if (activeAbility != null) return false;
        if (globalBusyTimer > 0f) return false; // respect global post-action busy
        if (isBerserk) return false;
        if (Time.time - lastBerserkTime < berserkCooldown) return false;
        return true;
    }

    private void StartBerserk()
    {
        if (activeAbility != null) return;
        lastBerserkTime = Time.time;
        // Also gate basic attacks while special is in progress
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoBerserk());
    }

    private IEnumerator CoBerserk()
    {
        BeginAction(AIState.Special2);
        if (HasBool(busyBool)) SetBoolSync(busyBool, true);
        if (HasTrigger(berserkTrigger)) SetTriggerSync(berserkTrigger);

        // Windup SFX (stoppable)
        if (audioSource != null && berserkSFX != null)
        {
            audioSource.clip = berserkSFX;
            audioSource.loop = false;
            audioSource.Play();
        }
        // Windup VFX
        GameObject berserkWindupFx = null;
        if (berserkActivateVFX != null)
        {
            berserkWindupFx = Instantiate(berserkActivateVFX, transform);
            berserkWindupFx.transform.localPosition = berserkActivateVFXOffset;
            if (berserkActivateVFXScale > 0f) berserkWindupFx.transform.localScale = Vector3.one * berserkActivateVFXScale;
        }

        float windup = Mathf.Max(0f, berserkWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        // End windup visuals/audio and play activation VFX/SFX when buff applies
        if (audioSource != null && audioSource.clip == berserkSFX)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }
        if (berserkWindupFx != null)
        {
            Destroy(berserkWindupFx);
            berserkWindupFx = null;
        }
        if (berserkImpactVFX != null)
        {
            var fx = Instantiate(berserkImpactVFX, transform);
            fx.transform.localPosition = berserkImpactVFXOffset;
            if (berserkImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * berserkImpactVFXScale;
        }
        PlaySfx(berserkImpactSFX);

        // Apply berserk state
        isBerserk = true;
        berserkTimer = berserkDuration;
        // centralized buff VFX: show all buffs granted by berserk (damage + speed)
        SetBuffVfx(BuffType.Damage, true, Vector3.zero, 1f);
        SetBuffVfx(BuffType.Speed, true, Vector3.zero, 1f);

        if (HasBool(busyBool)) SetBoolSync(busyBool, false);
        activeAbility = null;
        lastAttackTime = Time.time;
        attackLockTimer = enemyData != null ? enemyData.attackMoveLock : 0.25f;
        EndAction();

        // Maintain berserk state visuals/effects
        StartCoroutine(CoMaintainBerserk());
    }

    private IEnumerator CoMaintainBerserk()
    {
        while (isBerserk && berserkTimer > 0f)
        {
            berserkTimer -= Time.deltaTime;
            yield return null;
        }
        
        isBerserk = false;
        SetBuffVfx(BuffType.Damage, false, Vector3.zero);
        SetBuffVfx(BuffType.Speed, false, Vector3.zero);
        EndAction();
    }

    #endregion

    #region Cleanup

    void OnDestroy()
    {
        SetBuffVfx(BuffType.Damage, false, Vector3.zero);
    }

    #endregion
}