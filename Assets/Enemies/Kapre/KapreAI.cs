using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class KapreAI : BaseEnemyAI
{
    private const string SfxEventVanishWindup = "vanish_windup";
    private const string SfxEventVanishImpact = "vanish_impact";
    private const string SfxEventTreeSlamWindup = "treeslam_windup";
    private const string SfxEventTreeSlamImpact = "treeslam_impact";

    [Header("Basic Attack")]
    [Tooltip("How long locked (exhausted) after a basic attack. If 0, skips exhausted; if < 0, uses Stoppage + Recovery.")]
    public float basicAttackExhaustedTime = -1f;
    public float basicAttackStoppageTime = 0.8f;
    public float basicAttackRecoveryTime = 0f;

    [Header("Smoke Vanish → Strike")]
    public int vanishStrikeDamage = 30;
    public float vanishStrikeRadius = 1.6f;
    public float vanishWindup = 0.5f;
    public float vanishCooldown = 9f;
    public float vanishTeleportBehindDistance = 1.2f;
    public GameObject vanishWindupVFX;
    public GameObject vanishImpactVFX;
    public Vector3 vanishWindupVFXOffset = Vector3.zero;
    public float vanishWindupVFXScale = 1.0f;
    public Vector3 vanishImpactVFXOffset = Vector3.zero;
    public float vanishImpactVFXScale = 1.0f;
    public AudioClip vanishWindupSFX;
    [Range(0f, 1f)] public float vanishWindupSfxVolume = 1f;
    public AudioClip vanishImpactSFX;
    [Range(0f, 1f)] public float vanishImpactSfxVolume = 1f;
    public string vanishWindupTrigger = "VanishWindup";
    public string vanishMainTrigger = "VanishMain";

    [Header("Tree Slam (frontal AOE)")]
    public int treeSlamDamage = 35;
    public float treeSlamRadius = 2.6f;
    public float treeSlamWindup = 0.6f;
    public float treeSlamCooldown = 10f;
    public float treeSlamLeapDistance = 5f;
    public float treeSlamLeapDuration = 0.5f;
    public float treeSlamLeapHeight = 1.5f;
    public GameObject treeSlamWindupVFX;
    public GameObject treeSlamImpactVFX;
    public Vector3 treeSlamWindupVFXOffset = Vector3.zero;
    public float treeSlamWindupVFXScale = 1.0f;
    public Vector3 treeSlamImpactVFXOffset = Vector3.zero;
    public float treeSlamImpactVFXScale = 1.0f;
    public AudioClip treeSlamWindupSFX;
    [Range(0f, 1f)] public float treeSlamWindupSfxVolume = 1f;
    public AudioClip treeSlamImpactSFX;
    [Range(0f, 1f)] public float treeSlamImpactSfxVolume = 1f;
    public string treeSlamWindupTrigger = "TreeSlamWindup";
    public string treeSlamMainTrigger = "TreeSlamMain";

    [Header("Skill Selection Tuning")]
    public float vanishPreferredMinDistance = 2f;
    public float vanishPreferredMaxDistance = 7f;
    [Range(0f, 1f)] public float vanishSkillWeight = 0.7f;
    [Tooltip("How long locked (exhausted) after Vanish. If 0, skips exhausted; if < 0, uses Stoppage + Recovery.")]
    public float vanishExhaustedTime = -1f;
    public float vanishStoppageTime = 1f;
    public float vanishRecoveryTime = 0.5f;
    public float treeSlamPreferredMinDistance = 3f;
    public float treeSlamPreferredMaxDistance = 9f;
    [Range(0f, 1f)] public float treeSlamSkillWeight = 0.8f;
    [Tooltip("How long locked (exhausted) after Tree Slam. If 0, skips exhausted; if < 0, uses Stoppage + Recovery.")]
    public float treeSlamExhaustedTime = -1f;
    public float treeSlamStoppageTime = 1f;
    public float treeSlamRecoveryTime = 0.5f;

    // Runtime state
    private float lastVanishTime = -9999f;
    private float lastTreeSlamTime = -9999f;
    private Coroutine activeAbility;
    private Coroutine basicRoutine;
    private bool isExhausted = false;
    
    // Debug accessors
    public float VanishCooldownRemaining => Mathf.Max(0f, vanishCooldown - (Time.time - lastVanishTime));
    public float TreeSlamCooldownRemaining => Mathf.Max(0f, treeSlamCooldown - (Time.time - lastTreeSlamTime));

    public override string GetEffectiveStateForDebug()
    {
        if (activeAbility != null) return currentSkillName ?? "Special";
        if (basicRoutine != null) return "BasicAttack";
        return base.GetEffectiveStateForDebug();
    }
    private string currentSkillName;

    protected override void InitializeEnemy() { }

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTarget, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");
        var targetInAttack = new ConditionNode(blackboard, TargetInAttackRangeAndFacing, "in_attack_range_facing");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");
        var canVanish = new ConditionNode(blackboard, CanVanishStrike, "can_vanish");
        var doVanish = new ActionNode(blackboard, () => { StartVanishStrike(); return NodeState.Success; }, "vanish");
        var canTreeSlam = new ConditionNode(blackboard, CanTreeSlam, "can_treeslam");
        var doTreeSlam = new ActionNode(blackboard, () => { StartTreeSlam(); return NodeState.Success; }, "treeslam");
        var exhaustedGate = new ActionNode(blackboard, () => isExhausted ? NodeState.Running : NodeState.Success, "exhausted_gate");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    exhaustedGate,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "vanish_seq").Add(canVanish, doVanish),
                        new Sequence(blackboard, "treeslam_seq").Add(canTreeSlam, doTreeSlam),
                        new Sequence(blackboard, "basic_seq").Add(targetInAttack, basicAttack),
                        moveToTarget
                    )
                ),
                new ActionNode(blackboard, Patrol, "patrol")
            );
    }

    private void PlayKapreAbilitySfxSynced(string eventKey, AudioClip clip, float volume)
    {
        float clampedVolume = Mathf.Clamp01(volume);
        PlaySfx(clip, clampedVolume);
        if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            photonView.RPC(nameof(RPC_PlayKapreAbilitySfx), RpcTarget.Others, eventKey, clampedVolume);
    }

    [PunRPC]
    private void RPC_PlayKapreAbilitySfx(string eventKey, float volume)
    {
        float clampedVolume = Mathf.Clamp01(volume);
        switch (eventKey)
        {
            case SfxEventVanishWindup:
                PlaySfx(vanishWindupSFX, clampedVolume);
                break;
            case SfxEventVanishImpact:
                PlaySfx(vanishImpactSFX, clampedVolume);
                break;
            case SfxEventTreeSlamWindup:
                PlaySfx(treeSlamWindupSFX, clampedVolume);
                break;
            case SfxEventTreeSlamImpact:
                PlaySfx(treeSlamImpactSFX, clampedVolume);
                break;
        }
    }

    protected override void PerformBasicAttack()
    {
        if (basicRoutine != null) return;
        if (activeAbility != null) return;
        if (isExhausted) return;
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

        float basicExhaustedTotal = GetExhaustedDuration(basicAttackExhaustedTime, basicAttackStoppageTime, basicAttackRecoveryTime);
        attackLockTimer = Mathf.Max(enemyData.attackMoveLock, basicExhaustedTotal);

        EndAction();

        isExhausted = true;
        if (HasBool("Exhausted")) SetBoolSync("Exhausted", true);
        yield return RunExhaustedPhase(lockedRotation, basicAttackExhaustedTime, basicAttackStoppageTime, basicAttackRecoveryTime);
        if (HasBool("Exhausted")) SetBoolSync("Exhausted", false);
        isExhausted = false;

        lastAttackTime = Time.time;
        basicRoutine = null;
        if (basicExhaustedTotal > 0f)
            globalBusyTimer = Mathf.Max(globalBusyTimer, basicExhaustedTotal);
    }

    protected override bool TrySpecialAbilities()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false;
        if (isExhausted) return false;
        if (isBusy) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        bool facingTarget = IsFacingTarget(target, SpecialFacingAngle);
        bool inVanishRange = dist >= vanishPreferredMinDistance && dist <= vanishPreferredMaxDistance;
        bool inTreeSlamRange = dist >= treeSlamPreferredMinDistance && dist <= treeSlamPreferredMaxDistance;
        float vanishMid = (vanishPreferredMinDistance + vanishPreferredMaxDistance) * 0.5f;
        float treeMid = (treeSlamPreferredMinDistance + treeSlamPreferredMaxDistance) * 0.5f;
        float vanishScore = (inVanishRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(vanishMid - dist) / 5f)) * vanishSkillWeight : 0f;
        float treeSlamScore = (inTreeSlamRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(treeMid - dist) / 6f)) * treeSlamSkillWeight : 0f;
        if (CanVanishStrike() && vanishScore >= treeSlamScore && vanishScore > 0.15f) { StartVanishStrike(); return true; }
        if (CanTreeSlam() && treeSlamScore > vanishScore && treeSlamScore > 0.15f) { StartTreeSlam(); return true; }
        return false;
    }

    private bool CanVanishStrike()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false;
        if (isExhausted) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (Time.time - lastVanishTime < vanishCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        return Vector3.Distance(transform.position, target.position) <= enemyData.detectionRange;
    }

    private void StartVanishStrike()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        
        // Capture target position when ability is initiated (FIRST LOCATED)
        var target = blackboard.Get<Transform>("target");
        Vector3 capturedPos = target != null ? target.position : transform.position;
        Vector3 capturedForward = target != null ? target.forward : Vector3.forward;
        
        currentSkillName = "VanishStrike";
        activeAbility = StartCoroutine(CoVanishStrike(capturedPos, capturedForward));
    }

    private IEnumerator CoVanishStrike(Vector3 capturedPos, Vector3 capturedForward)
    {
        BeginAction(AIState.Special1);
        
        // Windup phase - separate trigger, VFX, and SFX (sync to network)
        if (HasTrigger(vanishWindupTrigger))
            SetTriggerSync(vanishWindupTrigger);
        PlayKapreAbilitySfxSynced(SfxEventVanishWindup, vanishWindupSFX, vanishWindupSfxVolume);
        GameObject wind = null;
        if (vanishWindupVFX != null)
        {
            wind = Instantiate(vanishWindupVFX, transform);
            wind.transform.localPosition = vanishWindupVFXOffset;
            if (vanishWindupVFXScale > 0f) 
                wind.transform.localScale = Vector3.one * vanishWindupVFXScale;
        }
        
        // Windup phase - freeze movement (no continuous facing for teleport attacks)
        float windup = Mathf.Max(0f, vanishWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            if (controller != null && controller.enabled) 
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        if (wind != null) Destroy(wind);

        // Teleport behind captured position (where player was FIRST LOCATED)
        Vector3 behind = capturedPos - capturedForward * Mathf.Max(0.2f, vanishTeleportBehindDistance);
        behind.y = transform.position.y;
        
        // Disable CharacterController to allow position change
        if (controller != null)
        {
            controller.enabled = false;
            transform.position = behind;
            controller.enabled = true;
        }
        else
        {
            transform.position = behind;
        }
        
        // Face captured position
        Vector3 dir = (capturedPos - transform.position);
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir.normalized);
        
        // Impact phase - separate trigger, VFX, and SFX (sync to network)
        if (HasTrigger(vanishMainTrigger))
            SetTriggerSync(vanishMainTrigger);
            
        if (vanishImpactVFX != null)
        {
            var fx = Instantiate(vanishImpactVFX, transform);
            fx.transform.localPosition = vanishImpactVFXOffset;
            if (vanishImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * vanishImpactVFXScale;
        }
        PlayKapreAbilitySfxSynced(SfxEventVanishImpact, vanishImpactSFX, vanishImpactSfxVolume);

        var cols = Physics.OverlapSphere(transform.position, vanishStrikeRadius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
        if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, vanishStrikeDamage);
        }

        float exhaustedTotal = GetExhaustedDuration(vanishExhaustedTime, vanishStoppageTime, vanishRecoveryTime);
        EndAction();

        isExhausted = true;
        if (HasBool("Exhausted")) SetBoolSync("Exhausted", true);
        yield return RunExhaustedPhase(transform.rotation, vanishExhaustedTime, vanishStoppageTime, vanishRecoveryTime);
        if (HasBool("Exhausted")) SetBoolSync("Exhausted", false);
        isExhausted = false;

        activeAbility = null;
        currentSkillName = null;
        lastVanishTime = Time.time;
        if (exhaustedTotal > 0f)
            globalBusyTimer = Mathf.Max(globalBusyTimer, exhaustedTotal);
    }

    private bool CanTreeSlam()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false;
        if (isExhausted) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (Time.time - lastTreeSlamTime < treeSlamCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null;
    }

    private void StartTreeSlam()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        currentSkillName = "TreeSlam";
        activeAbility = StartCoroutine(CoTreeSlam());
    }

    private IEnumerator CoTreeSlam()
    {
        BeginAction(AIState.Special2);
        
        // Windup phase - separate trigger, VFX, and SFX (sync to network)
        if (HasTrigger(treeSlamWindupTrigger))
            SetTriggerSync(treeSlamWindupTrigger);
        PlayKapreAbilitySfxSynced(SfxEventTreeSlamWindup, treeSlamWindupSFX, treeSlamWindupSfxVolume);
        GameObject wind = null;
        if (treeSlamWindupVFX != null)
        {
            wind = Instantiate(treeSlamWindupVFX, transform);
            wind.transform.localPosition = treeSlamWindupVFXOffset;
            if (treeSlamWindupVFXScale > 0f) 
                wind.transform.localScale = Vector3.one * treeSlamWindupVFXScale;
        }
        
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
        
        // Windup phase - lock rotation (don't face player)
        float windup = Mathf.Max(0f, treeSlamWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            // Lock rotation during windup
            transform.rotation = Quaternion.LookRotation(leapDirection);
            if (controller != null && controller.enabled) 
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        if (wind != null) Destroy(wind);
        
        // Forward leap (locked rotation, jumps forward)
        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + leapDirection * treeSlamLeapDistance;
        endPos.y = startPos.y; // Keep Y level for landing
        
        float leapTime = 0f;
        while (leapTime < treeSlamLeapDuration)
        {
            leapTime += Time.deltaTime;
            float progress = leapTime / treeSlamLeapDuration;
            
            // Parabolic arc for jump
            float height = Mathf.Sin(progress * Mathf.PI) * treeSlamLeapHeight;
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
        
        // Impact phase - separate trigger, VFX, and SFX (sync to network)
        if (HasTrigger(treeSlamMainTrigger))
            SetTriggerSync(treeSlamMainTrigger);
        
        if (treeSlamImpactVFX != null)
        {
            var fx = Instantiate(treeSlamImpactVFX, transform);
            fx.transform.localPosition = treeSlamImpactVFXOffset;
            if (treeSlamImpactVFXScale > 0f) 
                fx.transform.localScale = Vector3.one * treeSlamImpactVFXScale;
        }
        PlayKapreAbilitySfxSynced(SfxEventTreeSlamImpact, treeSlamImpactSFX, treeSlamImpactSfxVolume);

        // frontal AOE based on radius ahead
        var all = Physics.OverlapSphere(transform.position + transform.forward * (treeSlamRadius * 0.75f), treeSlamRadius, LayerMask.GetMask("Player"));
        foreach (var c in all)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
        if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, treeSlamDamage);
        }

        float exhaustedTotal = GetExhaustedDuration(treeSlamExhaustedTime, treeSlamStoppageTime, treeSlamRecoveryTime);
        EndAction();

        isExhausted = true;
        if (HasBool("Exhausted")) SetBoolSync("Exhausted", true);
        yield return RunExhaustedPhase(transform.rotation, treeSlamExhaustedTime, treeSlamStoppageTime, treeSlamRecoveryTime);
        if (HasBool("Exhausted")) SetBoolSync("Exhausted", false);
        isExhausted = false;

        activeAbility = null;
        currentSkillName = null;
        lastTreeSlamTime = Time.time;
        if (exhaustedTotal > 0f)
            globalBusyTimer = Mathf.Max(globalBusyTimer, exhaustedTotal);
    }

    private IEnumerator RunExhaustedPhase(Quaternion lockedRotation, float exhaustedTime, float stoppageTime, float recoveryTime)
    {
        if (exhaustedTime > 0f)
        {
            float t = exhaustedTime;
            while (t > 0f)
            {
                t -= Time.deltaTime;
                transform.rotation = lockedRotation;
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                yield return null;
            }
            yield break;
        }

        if (Mathf.Approximately(exhaustedTime, 0f))
            yield break;

        if (stoppageTime > 0f)
        {
            float t = stoppageTime;
            while (t > 0f)
            {
                t -= Time.deltaTime;
                transform.rotation = lockedRotation;
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                yield return null;
            }
        }

        if (recoveryTime > 0f)
        {
            float t = recoveryTime;
            while (t > 0f)
            {
                t -= Time.deltaTime;
                transform.rotation = lockedRotation;
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                yield return null;
            }
        }
    }

    private float GetExhaustedDuration(float exhaustedTime, float stoppageTime, float recoveryTime)
    {
        if (exhaustedTime > 0f) return exhaustedTime;
        if (Mathf.Approximately(exhaustedTime, 0f)) return 0f;
        return Mathf.Max(0f, stoppageTime) + Mathf.Max(0f, recoveryTime);
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
        if (isBusy || isExhausted || globalBusyTimer > 0f || activeAbility != null || basicRoutine != null)
        {
            return 0f;
        }
        
        // If AI is idle (not patrolling or chasing), return 0
        if (aiState == AIState.Idle)
        {
            return 0f;
        }
        
        return base.GetMoveSpeed();
    }
}