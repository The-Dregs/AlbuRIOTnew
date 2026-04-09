using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class BakunawaAI : BaseEnemyAI
{
    [Header("Multi-Part Boss - Animators")]
    [Tooltip("Primary/body animator for movement (Speed) and Tsunami Roar")]
    public Animator bodyAnimator;
    [Tooltip("Head animator for Head Slam and Beam Spit")]
    public Animator headAnimator;
    [Tooltip("Tail animator for Tail Whip")]
    public Animator tailAnimator;

    [Header("Head Slam (Basic Bite)")]
    public int headSlamDamage = 35;
    public float headSlamWindup = 0.6f;
    public float headSlamRange = 6f;
    public float headSlamCooldown = 2.5f;
    public float headSlamStoppageTime = 0.5f;
    [Tooltip("Exhausted duration after Head Slam finishes.")]
    public float headSlamExhaustedTime = 1.5f;
    public float headSlamZoneLifetime = 0.5f;
    [Tooltip("Prefab with EnemyDamageZone + trigger Collider. Damage zone is spawned when attack lands.")]
    public GameObject headSlamDamageZonePrefab;
    public Vector3 headSlamZoneOffset = new Vector3(0f, 0f, 4f);
    public Vector3 headSlamZoneScale = Vector3.one;
    public GameObject headSlamWindupVFX;
    public GameObject headSlamImpactVFX;
    public AudioClip headSlamSFX;
    public string headSlamTrigger = "HeadSlam";

    [Header("Tail Whip (Submerge → Emerge → Windup → Tornado → Exhausted)")]
    public float tailWhipCooldown = 12f;
    [Tooltip("Windup delay after emerging before damage zone and tornado spawn.")]
    public float tailWhipWindup = 0.5f;
    [Tooltip("Duration for body/tail to submerge (move down). No animation needed—moves segments downward.")]
    public float tailWhipSubmergeTime = 1.2f;
    [Tooltip("How far down the body and tail move when submerging (world Y offset).")]
    public float tailWhipSubmergeDepth = 8f;
    [Tooltip("How long the Bakunawa stays submerged before emerging and doing the tail whip.")]
    public float tailWhipStaySubmergedTime = 1f;
    [Tooltip("Duration for body/tail to emerge (move back up).")]
    public float tailWhipEmergeTime = 1f;
    [Tooltip("Distance from player when emerging backwards. Bakunawa positions itself this far from target.")]
    public float tailWhipEmergeDistance = 6f;
    [Tooltip("Exhausted duration after Tail Whip (before second submerge).")]
    public float tailWhipExhaustedTime = 1.5f;
    public string tailWhipSubmergeTrigger = "Submerge";
    public string tailWhipEmergeTrigger = "Emerge";
    [Header("Tail Whip Damage Zone (spawns when tail whip happens)")]
    [Tooltip("Damage for the tail whip damage zone (DZ_TailWhip).")]
    public int tailWhipDamageZoneDamage = 45;
    [Header("Tail Whip Tornado")]
    [Tooltip("Damage per tick for the tornado.")]
    public int tailWhipTornadoDamage = 10;
    [Tooltip("Tornado prefab with TornadoFollower. Spawns after emerge, follows players.")]
    public GameObject tailWhipTornadoPrefab;
    public string tailWhipTornadoResourcePath = "Enemies/Bakunawa/BAKUNAWA TORNADO";
    public float tailWhipTornadoSpeed = 3f;
    public float tailWhipTornadoLifetime = 5f;
    public float tailWhipTornadoTickInterval = 0.4f;
    public Vector3 tailWhipTornadoSpawnOffset = new Vector3(0f, 0f, 0f);
    [Header("Tail Whip Damage Zone Prefab")]
    [Tooltip("Damage zone prefab (e.g. DZ_TailWhip). Spawns at tail position when the tail whip occurs, then tornado spawns.")]
    public GameObject tailWhipDamageZonePrefab;
    public Vector3 tailWhipZoneOffset = new Vector3(0f, 0f, 0f);
    public Vector3 tailWhipZoneScale = new Vector3(8f, 2f, 8f);
    [Tooltip("How long the tail whip damage zone stays active.")]
    public float tailWhipZoneLifetime = 1f;
    [Header("Tail Whip VFX/SFX")]
    [Tooltip("VFX when Bakunawa submerges (dives into ground/water).")]
    public GameObject tailWhipSubmergeVFX;
    public Vector3 tailWhipSubmergeVFXOffset = Vector3.zero;
    [Tooltip("VFX spawned at emerge position before Bakunawa emerges (telegraph where it will surface).")]
    public GameObject tailWhipEmergenceIndicatorVFX;
    public Vector3 tailWhipEmergenceIndicatorOffset = Vector3.zero;
    [Tooltip("Extra seconds the emergence indicator stays visible after Bakunawa emerges (so it doesn't vanish abruptly).")]
    public float tailWhipEmergenceIndicatorExtraDelay = 1f;
    [Tooltip("VFX when Bakunawa emerges from ground/water.")]
    public GameObject tailWhipEmergeVFX;
    public Vector3 tailWhipEmergeVFXOffset = Vector3.zero;
    public GameObject tailWhipWindupVFX;
    public GameObject tailWhipImpactVFX;
    public Vector3 tailWhipVFXOffset = Vector3.zero;
    public AudioClip tailWhipSFX;
    public string tailWhipTrigger = "TailWhip";

    [Header("Tsunami Roar (Tidal Wave)")]
    public int tsunamiDamage = 40;
    public float tsunamiWindup = 1.2f;
    public float tsunamiCooldown = 15f;
    public float tsunamiStoppageTime = 1.5f;
    public float tsunamiZoneLifetime = 1f;
    [Tooltip("Prefab with EnemyDamageZone + trigger Collider. Position/scale defines damage area.")]
    public GameObject tsunamiDamageZonePrefab;
    public Vector3 tsunamiZoneOffset = new Vector3(0f, 0f, 7f);
    public Vector3 tsunamiZoneScale = new Vector3(14f, 2f, 14f);
    public GameObject tsunamiWindupVFX;
    public GameObject tsunamiImpactVFX;
    public AudioClip tsunamiWindupSFX;
    public AudioClip tsunamiImpactSFX;
    public string tsunamiTrigger = "TsunamiRoar";

    [Header("Beam Spit (Projectile)")]
    public int beamDamage = 30;
    [Tooltip("Beam prefab with VFX + EnemyDamageZone. Damage zone is initialized on spawn.")]
    public GameObject beamProjectilePrefab;
    [Tooltip("Resources path for Photon. Required when beam prefab is used in multiplayer.")]
    public string beamProjectileResourcePath = "Enemies/Bakunawa/BakunawaBeam";
    public Vector3 beamSpawnOffset = new Vector3(0f, 2f, 4f);
    [HideInInspector] public float beamSpeed = 18f;
    [Tooltip("Phase 1: Windup duration before beam spawns.")]
    public float beamWindup = 0.8f;
    [Tooltip("Phase 2: How long the beam stays active (stationary, dealing damage).")]
    public float beamLifetime = 3f;
    [Tooltip("Seconds after spawn before damage zone can hit. 0 = immediate.")]
    public float beamDamageDelay = 0f;
    public float beamCooldown = 10f;
    [Tooltip("Stoppage: boss frozen while the beam attack is active.")]
    public float beamStoppageTime = 0.8f;
    [Tooltip("Exhausted duration after Beam Spit finishes.")]
    public float beamExhaustedTime = 1.5f;
    public GameObject beamWindupVFX;
    public AudioClip beamSFX;
    public string beamTrigger = "BeamSpit";

    [Header("Head Exhausted (animator param)")]
    [Tooltip("Animator Bool parameter name. Set true when attack finishes, false when timer expires.")]
    public string headExhaustedBoolParam = "Exhausted";

    [Header("Target Selection (anti-kiting)")]
    [Tooltip("New target must score this much better than current to switch. Higher = less flip-flopping.")]
    [Range(1f, 2f)]
    public float targetSwitchThreshold = 1.35f;
    [Tooltip("Weight for distance in target score (0-1). Higher = prefer closer players.")]
    [Range(0f, 1f)]
    public float targetDistanceWeight = 0.6f;
    [Tooltip("Weight for facing angle in target score (0-1). Higher = prefer players already in front.")]
    [Range(0f, 1f)]
    public float targetFacingWeight = 0.4f;

    [Header("Skill Selection Tuning")]
    public float tailWhipPreferredMinDistance = 4f;
    public float tailWhipPreferredMaxDistance = 10f;
    public float tsunamiPreferredMinDistance = 6f;
    public float tsunamiPreferredMaxDistance = 14f;
    public float beamPreferredMinDistance = 8f;
    public float beamPreferredMaxDistance = 18f;

    [Header("Segment Follow (body/tail lag behind head)")]
    [Tooltip("Transform of the body segment (e.g. GameObject with body Animator). If set, its rotation will smoothly follow the root (head) so the body lags when turning.")]
    public Transform bodySegment;
    [Tooltip("Transform of the tail segment (e.g. GameObject with tail Animator). If set, its rotation will smoothly follow the body (or head if body is null).")]
    public Transform tailSegment;
    [Tooltip("How quickly body rotation catches up to head. Lower = more lag/slither. Typical 2–5.")]
    public float bodyFollowSpeed = 3f;
    [Tooltip("How quickly tail rotation catches up to body. Lower = more lag. Typical 1.5–3.")]
    public float tailFollowSpeed = 2f;
    [Tooltip("If true, body rotates in the opposite direction to the head: when head turns right, body turns left (opposite rotation direction).")]
    public bool bodyRotationInverted = true;
    [Tooltip("Max degrees the body can rotate away from the head. Prevents the body from over-rotating. Lower = subtler S-curve. Typical 30–45.")]
    [Range(15f, 90f)]
    public float maxBodyAngleFromHead = 36f;
    [Tooltip("Max degrees the tail can rotate away from what it follows (head or body). Prevents tail kinking. Typical 30–50.")]
    [Range(15f, 90f)]
    public float maxTailAngleFromTarget = 40f;

    [Header("Swim Splash VFX")]
    [Tooltip("Splash GameObject under Head. Enabled when moving, disabled when idle.")]
    public GameObject swimSplashHead;
    [Tooltip("Splash GameObject under Body. Enabled when moving, disabled when idle.")]
    public GameObject swimSplashBody;
    [Tooltip("Splash GameObject under Tail. Enabled when moving, disabled when idle.")]
    public GameObject swimSplashTail;
    [Tooltip("Minimum movement speed to show splash (0.5 = typical).")]
    public float swimSplashMinSpeed = 0.5f;

    [Header("Swim Sway (body left-right when moving)")]
    [Tooltip("Degrees of left-right sway on the body when swimming. 0 = no sway.")]
    [Range(0f, 25f)]
    public float swimSwayAmplitudeDeg = 10f;
    [Tooltip("Speed of the sway cycle (radians per second). Higher = faster wiggle.")]
    [Range(1f, 8f)]
    public float swimSwayFrequency = 3.5f;
    [Tooltip("Phase offset for tail sway (radians). Negative = tail lags behind body (body moves first). -2.2 = more pronounced body-leads wave.")]
    [Range(-3.15f, 3.15f)]
    public float swimSwayTailPhase = -2.2f;

    [Header("Debug")]
    [Tooltip("Log to console when Bakunawa starts a move and when AI state changes.")]
    public bool showBakunawaDebugLogs = false;

    private Quaternion bodyTargetWorldRot;
    private Quaternion tailTargetWorldRot;
    private float prevHeadYaw;
    private float bodyTargetYaw;
    private bool segmentFollowInitialized;
    private float headExhaustedEndTime = -1f;
    private Quaternion exhaustedFrozenRotation;
    private int exhaustedByAttack; // 0=none, 1=HeadSlam, 2=Beam
    private AIState lastLoggedState = (AIState)(-1);
    private string lastLoggedActivity = "";
    private string lastLoggedStateStr = "";

    private Coroutine activeAbility;
    private Coroutine basicRoutine;
    private float lastHeadSlamTime = -9999f;
    private float lastTailWhipTime = -9999f;
    private float lastTsunamiTime = -9999f;
    private float lastBeamTime = -9999f;
    private float lastAnySkillRecoveryEnd = -9999f;
    private float lastAnySkillRecoveryStart = -9999f;
    private float lastBasicRoutineStart = -9999f;

    protected override void InitializeEnemy()
    {
        if (bodyAnimator == null) bodyAnimator = animator;
        if (animator == null && bodyAnimator != null) animator = bodyAnimator;
    }

    protected override void Awake()
    {
        base.Awake();
    }

    private void Start()
    {
        // intentionally left empty
    }

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTargetSmart, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");

        var inRangeForSpecialNotFacing = new ConditionNode(blackboard, InRangeForSpecialNotFacing, "in_special_range_not_facing");
        var faceTargetForSpecial = new ActionNode(blackboard, FaceTargetForSpecial, "face_for_special");

        var canTailWhip = new ConditionNode(blackboard, CanTailWhip, "can_tail_whip");
        var doTailWhip = new ActionNode(blackboard, () => { StartTailWhip(); return NodeState.Success; }, "tail_whip");
        var canTsunami = new ConditionNode(blackboard, CanTsunamiRoar, "can_tsunami");
        var doTsunami = new ActionNode(blackboard, () => { StartTsunamiRoar(); return NodeState.Success; }, "tsunami");
        var canBeam = new ConditionNode(blackboard, CanBeamSpit, "can_beam");
        var doBeam = new ActionNode(blackboard, () => { StartBeamSpit(); return NodeState.Success; }, "beam");
        var targetInHeadSlamRange = new ConditionNode(blackboard, TargetInHeadSlamRange, "in_head_slam_range");
        var isExhausted = new ConditionNode(blackboard, IsHeadExhausted, "is_exhausted");
        var exhaustedStandStill = new ActionNode(blackboard, ExhaustedStandStill, "exhausted_stand_still");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "exhausted").Add(isExhausted, exhaustedStandStill),
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "face_or_attack").Add(
                        new Sequence(blackboard, "face_before_special").Add(inRangeForSpecialNotFacing, faceTargetForSpecial),
                        new Selector(blackboard, "attack_opts").Add(
                            new Sequence(blackboard, "tail_whip_seq").Add(canTailWhip, doTailWhip),
                            new Sequence(blackboard, "tsunami_seq").Add(canTsunami, doTsunami),
                            new Sequence(blackboard, "beam_seq").Add(canBeam, doBeam),
                            new Sequence(blackboard, "basic_seq").Add(targetInHeadSlamRange, basicAttack),
                            moveToTarget
                        )
                    )
                ),
                new ActionNode(blackboard, Patrol, "patrol")
            );
    }

    protected override bool TrySpecialAbilities()
    {
        return false;
    }

    protected override void Update()
    {
        if (isDead) return;
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
            return;

        // Clear exhausted and reset movement-blocking state BEFORE base.Update so behavior tree runs with clean state
        if (headExhaustedEndTime >= 0f && Time.time >= headExhaustedEndTime)
        {
            float now = Time.time;
            headExhaustedEndTime = -1f;
            if (exhaustedByAttack == 1)
            {
                lastHeadSlamTime = now;
                lastAnySkillRecoveryEnd = now;
            }
            else if (exhaustedByAttack == 2)
            {
                lastBeamTime = now;
                lastAnySkillRecoveryEnd = now;
            }
            else if (exhaustedByAttack == 3)
            {
                lastTailWhipTime = now;
                lastAnySkillRecoveryEnd = now;
            }
            exhaustedByAttack = 0;
            SetHeadExhausted(false);
            globalBusyTimer = 0f;
            attackLockTimer = 0f;
            isBusy = false;
            activeAbility = null;
            basicRoutine = null;
            if (animator != null && HasBool("Busy"))
                animator.SetBool("Busy", false);
            var target = blackboard?.Get<Transform>("target");
            if (target != null)
                aiState = AIState.Chase;
            else
                aiState = AIState.Idle;
            if (showBakunawaDebugLogs)
                Debug.Log("[Bakunawa] Exhausted ended → State: " + aiState + (target != null ? " (has target)" : " (no target)"));
            lastLoggedActivity = "";
            lastLoggedStateStr = "";
        }

        // Watchdog: force recovery if stuck in ability/basic for too long (coroutine died, object disabled, etc.)
        if (!isDead && (activeAbility != null || basicRoutine != null))
        {
            float stuckTime = activeAbility != null ? (Time.time - lastAnySkillRecoveryStart) : (Time.time - lastBasicRoutineStart);
            if (stuckTime > 12f)
            {
                if (showBakunawaDebugLogs)
                    Debug.LogWarning("[Bakunawa] Watchdog: stuck in ability for " + stuckTime.ToString("F1") + "s - forcing recovery");
                activeAbility = null;
                basicRoutine = null;
                isBusy = false;
                globalBusyTimer = 0f;
                attackLockTimer = 0f;
                headExhaustedEndTime = -1f;
                exhaustedByAttack = 0;
                SetHeadExhausted(false);
                if (animator != null && HasBool("Busy"))
                    animator.SetBool("Busy", false);
                aiState = blackboard?.Get<Transform>("target") != null ? AIState.Chase : AIState.Idle;
            }
        }

        // When exhausted: full lock - no behavior tree, no rotation, no movement, Exhausted stays true until timer expires
        if (IsHeadExhausted())
        {
            ApplyHeadExhaustedAnimator(true);
            globalBusyTimer = 0f;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            transform.rotation = exhaustedFrozenRotation;
            UpdateAnimation();
            UpdateAttackLock();
            if (globalBusyTimer > 0f) globalBusyTimer -= Time.deltaTime;
            // Apply frozen rotation last so nothing overwrites it this frame
            transform.rotation = exhaustedFrozenRotation;
            return;
        }

        base.Update();

        // do not emit per-state console logs from Update (too noisy in gameplay).
    }

    public override string GetEffectiveStateForDebug()
    {
        return GetCurrentActivityString();
    }

    private string GetCurrentActivityString()
    {
        if (IsHeadExhausted())
            return "Exhausted (" + (headExhaustedEndTime - Time.time).ToString("F1") + "s left)";
        if (basicRoutine != null) return "HeadSlam";
        if (activeAbility != null) return "Special (TailWhip/Tsunami/Beam)";
        if (isBusy || globalBusyTimer > 0f) return "Busy";
        var target = blackboard?.Get<Transform>("target");
        if (target != null) return "Chasing";
        return "Idle/Patrol";
    }

    private bool TargetInHeadSlamRange()
    {
        if (IsHeadExhausted()) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        return dist <= headSlamRange + 1f;
    }

    /// <summary>
    /// Smart target selection: closest player by distance + facing angle. Stickiness prevents flip-flopping when players take turns getting closer.
    /// </summary>
    private NodeState UpdateTargetSmart()
    {
        if (enemyData == null) return NodeState.Success;

        // Drop target if out of chase range
        if (targetPlayer != null)
        {
            float dist = Vector3.Distance(transform.position, targetPlayer.position);
            if (dist > enemyData.chaseLoseRange || !IsPlayerValid(targetPlayer))
                targetPlayer = null;
        }

        PlayerStats[] players = FindObjectsByType<PlayerStats>(FindObjectsSortMode.None);
        Transform best = null;
        float bestScore = float.MinValue;
        float currentTargetScore = float.MinValue;

        Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z);
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        else fwd.Normalize();

        foreach (var player in players)
        {
            if (player == null) continue;
            Transform t = player.transform;
            float d = Vector3.Distance(transform.position, t.position);
            if (d > enemyData.detectionRange) continue;

            Vector3 to = t.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) continue;
            to.Normalize();
            float angle = Vector3.Angle(fwd, to);

            // Score: prefer closer + more in front. Normalize distance (0-1, 1=closest) and angle (0-1, 1=directly in front).
            float distScore = 1f - Mathf.Clamp01(d / enemyData.detectionRange);
            float angleScore = 1f - Mathf.Clamp01(angle / 180f);
            float score = distScore * targetDistanceWeight + angleScore * targetFacingWeight;

            if (t == targetPlayer)
                currentTargetScore = score;
            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        // Stickiness: only switch if new target is significantly better (prevents kiting back-and-forth)
        if (targetPlayer != null && best != null && best != targetPlayer)
        {
            if (bestScore <= currentTargetScore * targetSwitchThreshold)
                best = targetPlayer;
        }

        if (best != null && Vector3.Distance(transform.position, best.position) <= enemyData.detectionRange)
        {
            targetPlayer = best;
            blackboard.Set("target", best);
        }
        else
        {
            targetPlayer = null;
            blackboard.Remove("target");
        }
        return NodeState.Success;
    }

    private bool InRangeForSpecialNotFacing()
    {
        var target = blackboard.Get<Transform>("target");
        if (target == null || IsHeadExhausted()) return false;
        if (activeAbility != null || basicRoutine != null || isBusy) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        bool inTail = dist >= tailWhipPreferredMinDistance && dist <= tailWhipPreferredMaxDistance + 2f;
        bool inTsunami = dist >= tsunamiPreferredMinDistance && dist <= tsunamiPreferredMaxDistance + 2f;
        bool inBeam = dist >= beamPreferredMinDistance && dist <= beamPreferredMaxDistance + 2f;
        if (!inTail && !inTsunami && !inBeam) return false;
        return !IsFacingTarget(target, SpecialFacingAngle);
    }

    private NodeState FaceTargetForSpecial()
    {
        var target = blackboard.Get<Transform>("target");
        if (target == null) return NodeState.Success;
        if (IsFacingTarget(target, SpecialFacingAngle)) return NodeState.Success;
        FaceTarget(target);
        return NodeState.Running;
    }

    private bool IsFacingTarget(Transform target, float maxAngle)
    {
        if (target == null) return false;
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return false;
        float angle = Vector3.Angle(new Vector3(transform.forward.x, 0f, transform.forward.z).normalized, to.normalized);
        return angle <= Mathf.Clamp(maxAngle, 1f, 60f);
    }

    private void FaceTarget(Transform target)
    {
        if (target == null) return;
        Vector3 look = new Vector3(target.position.x, transform.position.y, target.position.z);
        Vector3 dir = look - transform.position;
        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, RotationSpeed * Time.deltaTime);
        }
        if (controller != null && controller.enabled)
            controller.SimpleMove(Vector3.zero);
    }

    protected override void PerformBasicAttack()
    {
        if (basicRoutine != null) return;
        if (activeAbility != null) return;
        if (isBusy || globalBusyTimer > 0f) return;
        if (enemyData == null) return;
        if (IsHeadExhausted()) return;
        if (Time.time - lastHeadSlamTime < headSlamCooldown) return;

        var target = blackboard.Get<Transform>("target");
        if (target == null) return;
        if (!IsFacingTarget(target, SpecialFacingAngle))
        {
            FaceTarget(target);
            return;
        }

        basicRoutine = StartCoroutine(CoHeadSlam(target));
    }

    private Animator GetHeadAnimator()
    {
        return headAnimator != null ? headAnimator : animator;
    }

    private Animator GetBodyAnimator()
    {
        return bodyAnimator != null ? bodyAnimator : animator;
    }

    private Animator GetTailAnimator()
    {
        return tailAnimator != null ? tailAnimator : animator;
    }

    private bool HasTriggerOn(Animator a, string param)
    {
        if (a == null || string.IsNullOrEmpty(param)) return false;
        foreach (var p in a.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == param) return true;
        return false;
    }

    private bool HasBoolOn(Animator a, string param)
    {
        if (a == null || string.IsNullOrEmpty(param)) return false;
        foreach (var p in a.parameters)
            if (p.type == AnimatorControllerParameterType.Bool && p.name == param) return true;
        return false;
    }

    /// <summary>
    /// ONLY call when Head Slam, Beam Spit, or Tail Whip finishes. Sets Exhausted=true and starts the timer.
    /// exhaustedBy: 1=HeadSlam, 2=Beam, 3=TailWhip. Cooldown for that attack starts when exhausted ends.
    /// </summary>
    private void SetHeadExhaustedForAttack(float exhaustedDuration, int exhaustedBy)
    {
        float duration = Mathf.Max(0.1f, exhaustedDuration);
        headExhaustedEndTime = Time.time + duration;
        exhaustedFrozenRotation = transform.rotation;
        exhaustedByAttack = exhaustedBy;
        ApplyHeadExhaustedAnimator(true);
        if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            photonView.RPC("RPC_BakunawaSetHeadExhausted", RpcTarget.Others, true);
        if (showBakunawaDebugLogs)
            Debug.Log("[Bakunawa] Head Exhausted = true (for " + duration.ToString("F1") + "s)");
    }

    /// <summary>
    /// Apply Exhausted to head animator. Called when entering exhausted (attack) and when timer expires.
    /// </summary>
    private void ApplyHeadExhaustedAnimator(bool exhausted)
    {
        Animator head = GetHeadAnimator();
        if (head == null || string.IsNullOrEmpty(headExhaustedBoolParam) || !HasBoolOn(head, headExhaustedBoolParam))
        {
            if (showBakunawaDebugLogs && exhausted)
                Debug.LogWarning("[Bakunawa] Cannot set Exhausted: head animator missing or param '" + headExhaustedBoolParam + "' not found.");
            return;
        }
        head.SetBool(headExhaustedBoolParam, exhausted);
    }

    private void SetHeadExhausted(bool exhausted)
    {
        ApplyHeadExhaustedAnimator(exhausted);
        if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            photonView.RPC("RPC_BakunawaSetHeadExhausted", RpcTarget.Others, exhausted);
    }

    [PunRPC]
    private void RPC_BakunawaSetHeadExhausted(bool exhausted)
    {
        Animator head = GetHeadAnimator();
        if (head == null || string.IsNullOrEmpty(headExhaustedBoolParam) || !HasBoolOn(head, headExhaustedBoolParam))
            return;
        // Always apply - no check for current value, keeps remote clients in sync
        head.SetBool(headExhaustedBoolParam, exhausted);
    }

    private void SetTriggerOnAnimator(Animator a, string triggerName)
    {
        if (a == null || string.IsNullOrEmpty(triggerName) || !HasTriggerOn(a, triggerName)) return;
        a.SetTrigger(triggerName);
        if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            int idx = (a == headAnimator) ? 1 : (a == tailAnimator) ? 2 : 0;
            photonView.RPC("RPC_BakunawaSetTrigger", RpcTarget.Others, idx, triggerName);
        }
    }

    [PunRPC]
    private void RPC_BakunawaSetTrigger(int animIndex, string triggerName)
    {
        Animator a = animIndex == 1 ? headAnimator : (animIndex == 2 ? tailAnimator : (bodyAnimator ?? animator));
        if (a != null && HasTriggerOn(a, triggerName)) a.SetTrigger(triggerName);
    }

    private void SpawnDamageZone(GameObject prefab, Vector3 localOffset, Vector3 scale, int damage, float lifetime)
    {
        if (prefab == null) return;
        Vector3 pos = transform.position + transform.rotation * localOffset;
        Quaternion rot = transform.rotation;
        GameObject zone = Instantiate(prefab, pos, rot);
        zone.transform.localScale = scale;
        TryCallInitialize(zone, gameObject, damage, lifetime);
    }

    private void SpawnVFXAt(GameObject prefab, Vector3 worldPos, Quaternion rotation, Vector3 localOffset, float destroyAfterSeconds = 1f)
    {
        if (prefab == null) return;
        Vector3 pos = worldPos + rotation * localOffset;
        GameObject fx = Instantiate(prefab, pos, rotation);
        if (destroyAfterSeconds > 0f)
        {
            var destroy = fx.GetComponent<DestroyAfterSeconds>();
            if (destroy == null) destroy = fx.AddComponent<DestroyAfterSeconds>();
            destroy.seconds = destroyAfterSeconds;
        }
    }

    private static void TryCallInitialize(GameObject obj, GameObject owner, params object[] args)
    {
        foreach (var mb in obj.GetComponents<MonoBehaviour>())
        {
            if (mb == null) continue;
            var method = mb.GetType().GetMethod("Initialize",
                BindingFlags.Public | BindingFlags.Instance);
            if (method == null) continue;
            var parameters = method.GetParameters();
            if (parameters.Length < 2 || parameters[0].ParameterType != typeof(GameObject)) continue;
            var fullArgs = new object[parameters.Length];
            fullArgs[0] = owner;
            for (int i = 1; i < parameters.Length; i++)
            {
                if (i - 1 < args.Length)
                    fullArgs[i] = args[i - 1];
                else if (parameters[i].HasDefaultValue)
                    fullArgs[i] = parameters[i].DefaultValue;
                else
                    goto next;
            }
            try { method.Invoke(mb, fullArgs); } catch { }
            return;
            next:;
        }
    }

    #region Head Slam (Basic Attack)

    private IEnumerator CoHeadSlam(Transform target)
    {
        lastBasicRoutineStart = Time.time;
        BeginAction(AIState.BasicAttack);
        if (showBakunawaDebugLogs) Debug.Log("[Bakunawa] Move: Head Slam | State: BasicAttack");

        try
        {

        Vector3 chargeDir = (target.position - transform.position);
        chargeDir.y = 0f;
        if (chargeDir.sqrMagnitude > 0.0001f) chargeDir.Normalize();
        else chargeDir = transform.forward;

        if (chargeDir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(chargeDir);

        SetTriggerOnAnimator(GetHeadAnimator(), headSlamTrigger);
        PlayAttackWindupSfx();
        PlaySfx(headSlamSFX);
        if (headSlamWindupVFX != null)
        {
            var fx = Instantiate(headSlamWindupVFX, transform);
            fx.transform.localPosition = Vector3.forward * 3f;
        }

        float windup = headSlamWindup;
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            if (target != null)
            {
                Vector3 to = target.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(to.normalized);
            }
            yield return null;
        }

        if (headSlamImpactVFX != null)
        {
            var fx = Instantiate(headSlamImpactVFX, transform.position + transform.forward * (headSlamRange * 0.5f), transform.rotation);
        }
        PlayAttackImpactSfx();

        SpawnDamageZone(headSlamDamageZonePrefab, headSlamZoneOffset, headSlamZoneScale, headSlamDamage, headSlamZoneLifetime);

        float stop = headSlamStoppageTime;
        while (stop > 0f)
        {
            stop -= Time.deltaTime;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            if (target != null)
            {
                Vector3 to = target.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(to.normalized);
            }
            yield return null;
        }

        SetHeadExhaustedForAttack(headSlamExhaustedTime, 1);
        EndAction();
        }
        finally
        {
            basicRoutine = null;
            if (isBusy) EndAction();
        }
    }

    #endregion

    #region Tail Whip

    private bool CanTailWhip()
    {
        if (activeAbility != null || basicRoutine != null) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (IsHeadExhausted()) return false;
        if (Time.time - lastTailWhipTime < tailWhipCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false;

        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        if (!IsFacingTarget(target, SpecialFacingAngle)) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        return dist >= tailWhipPreferredMinDistance && dist <= tailWhipPreferredMaxDistance + 2f;
    }

    private void StartTailWhip()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoTailWhip());
    }

    private IEnumerator CoTailWhip()
    {
        lastAnySkillRecoveryStart = Time.time;
        BeginAction(AIState.Special1);
        if (showBakunawaDebugLogs) Debug.Log("[Bakunawa] Move: Tail Whip | State: Special1");

        try
        {
            var target = blackboard.Get<Transform>("target");
            if (target == null)
            {
                EndAction();
                yield break;
            }

            Animator headAnim = GetHeadAnimator();

            // Phase 1: Submerge – head (root), body and tail move down
            Vector3 rootStartPos = transform.position;
            Vector3 bodyStartPos = bodySegment != null ? bodySegment.localPosition : Vector3.zero;
            Vector3 tailStartPos = tailSegment != null ? tailSegment.localPosition : Vector3.zero;
            if (headAnim != null && HasTriggerOn(headAnim, tailWhipSubmergeTrigger))
            {
                SetTriggerOnAnimator(headAnim, tailWhipSubmergeTrigger);
                if (showBakunawaDebugLogs) Debug.Log("[Bakunawa] Tail Whip: Submerging");
            }
            SpawnVFXAt(tailWhipSubmergeVFX, rootStartPos, transform.rotation, tailWhipSubmergeVFXOffset);
            float submergeTimer = tailWhipSubmergeTime;
            while (submergeTimer > 0f)
            {
                submergeTimer -= Time.deltaTime;
                float t = 1f - submergeTimer / tailWhipSubmergeTime;
                if (controller != null) controller.enabled = false;
                transform.position = Vector3.Lerp(rootStartPos, rootStartPos + Vector3.down * tailWhipSubmergeDepth, t);
                if (controller != null) controller.enabled = true;
                if (bodySegment != null)
                    bodySegment.localPosition = Vector3.Lerp(bodyStartPos, bodyStartPos + Vector3.down * tailWhipSubmergeDepth, t);
                if (tailSegment != null)
                    tailSegment.localPosition = Vector3.Lerp(tailStartPos, tailStartPos + Vector3.down * tailWhipSubmergeDepth, t);
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                yield return null;
            }

            // Phase 1b: Stay submerged – compute emerge position and spawn indicator VFX at surface
            Vector3 rootSubmergedPos = rootStartPos + Vector3.down * tailWhipSubmergeDepth;
            Vector3 toPlayer = target.position - transform.position;
            toPlayer.y = 0f;
            Vector3 awayFromPlayer = toPlayer.sqrMagnitude > 0.0001f ? -toPlayer.normalized : -transform.forward;
            Vector3 emergePos = target.position + awayFromPlayer * tailWhipEmergeDistance;
            emergePos.y = rootSubmergedPos.y;
            Vector3 indicatorSurfacePos = emergePos + Vector3.up * tailWhipSubmergeDepth; // surface where boss will emerge
            Quaternion emergeRot = Quaternion.LookRotation(awayFromPlayer);
            float indicatorDuration = tailWhipStaySubmergedTime + tailWhipEmergeTime + tailWhipEmergenceIndicatorExtraDelay;
            SpawnVFXAt(tailWhipEmergenceIndicatorVFX, indicatorSurfacePos, emergeRot, tailWhipEmergenceIndicatorOffset, indicatorDuration);

            float stayTimer = tailWhipStaySubmergedTime;
            while (stayTimer > 0f)
            {
                stayTimer -= Time.deltaTime;
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                yield return null;
            }

            // Phase 2: Emerge backwards - position at distance, face AWAY so tail faces player
            toPlayer = target.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.0001f)
            {
                awayFromPlayer = -toPlayer.normalized;
                emergePos = target.position + awayFromPlayer * tailWhipEmergeDistance;
                emergePos.y = rootSubmergedPos.y;

                // CharacterController ignores transform.position; must disable to teleport
                if (controller != null)
                {
                    controller.enabled = false;
                    transform.position = emergePos;
                    transform.rotation = Quaternion.LookRotation(awayFromPlayer);
                    controller.enabled = true;
                }
                else
                {
                    transform.position = emergePos;
                    transform.rotation = Quaternion.LookRotation(awayFromPlayer);
                }
            }

            if (headAnim != null && HasTriggerOn(headAnim, tailWhipEmergeTrigger))
            {
                SetTriggerOnAnimator(headAnim, tailWhipEmergeTrigger);
                if (showBakunawaDebugLogs) Debug.Log("[Bakunawa] Tail Whip: Emerging backwards (tail toward player)");
            }
            PlaySfx(tailWhipSFX);
            Vector3 emergeSurfacePos = transform.position + Vector3.up * tailWhipSubmergeDepth; // surface where boss emerges
            SpawnVFXAt(tailWhipEmergeVFX, emergeSurfacePos, transform.rotation, tailWhipEmergeVFXOffset);

            float emergeTimer = tailWhipEmergeTime;
            Vector3 emergeStartPos = transform.position;
            Vector3 emergeEndPos = emergeStartPos + Vector3.up * tailWhipSubmergeDepth;
            Vector3 bodySubmergedPos = bodyStartPos + Vector3.down * tailWhipSubmergeDepth;
            Vector3 tailSubmergedPos = tailStartPos + Vector3.down * tailWhipSubmergeDepth;
            while (emergeTimer > 0f)
            {
                emergeTimer -= Time.deltaTime;
                float t = 1f - emergeTimer / tailWhipEmergeTime;
                if (controller != null) controller.enabled = false;
                transform.position = Vector3.Lerp(emergeStartPos, emergeEndPos, t);
                if (controller != null) controller.enabled = true;
                if (bodySegment != null)
                    bodySegment.localPosition = Vector3.Lerp(bodySubmergedPos, bodyStartPos, t);
                if (tailSegment != null)
                    tailSegment.localPosition = Vector3.Lerp(tailSubmergedPos, tailStartPos, t);
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                yield return null;
            }

            // Phase 3a: Tail whip animation plays during windup; damage zones spawn after windup
            Animator tailAnim = GetTailAnimator();
            if (tailAnim != null && HasTriggerOn(tailAnim, tailWhipTrigger))
            {
                SetTriggerOnAnimator(tailAnim, tailWhipTrigger);
                if (showBakunawaDebugLogs) Debug.Log("[Bakunawa] Tail Whip: Triggering tail whip (animation plays during windup)");
            }
            SpawnVFXAt(tailWhipWindupVFX, transform.position, transform.rotation, tailWhipVFXOffset);
            float windupTimer = Mathf.Max(0f, tailWhipWindup);
            while (windupTimer > 0f)
            {
                windupTimer -= Time.deltaTime;
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                yield return null;
            }

            // Phase 3b: Damage zone and tornado spawn after windup
            if (tailWhipDamageZonePrefab != null)
            {
                SpawnDamageZone(tailWhipDamageZonePrefab, tailWhipZoneOffset, tailWhipZoneScale, tailWhipDamageZoneDamage, tailWhipZoneLifetime);
                if (showBakunawaDebugLogs) Debug.Log("[Bakunawa] Tail Whip: Spawned damage zone");
            }
            SpawnVFXAt(tailWhipImpactVFX, transform.position, transform.rotation, tailWhipVFXOffset);
            SpawnTailWhipTornado();

            EndAction();
            SetHeadExhaustedForAttack(tailWhipExhaustedTime, 3);
            if (showBakunawaDebugLogs) Debug.Log("[Bakunawa] Tail Whip done, exhausted until " + headExhaustedEndTime.ToString("F1"));
        }
        finally
        {
            activeAbility = null;
            if (isBusy) EndAction();
        }
    }

    private void SpawnTailWhipTornado()
    {
        if (tailWhipTornadoPrefab == null && string.IsNullOrEmpty(tailWhipTornadoResourcePath))
            return;

        Vector3 spawnPos = transform.position + transform.rotation * tailWhipTornadoSpawnOffset;
        Quaternion spawnRot = Quaternion.identity;

        GameObject tornadoObj = null;
        string path = string.IsNullOrEmpty(tailWhipTornadoResourcePath) && tailWhipTornadoPrefab != null
            ? tailWhipTornadoPrefab.name : tailWhipTornadoResourcePath;

        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !string.IsNullOrEmpty(path))
            tornadoObj = PhotonNetwork.Instantiate(path, spawnPos, spawnRot);
        else if (tailWhipTornadoPrefab != null)
            tornadoObj = Instantiate(tailWhipTornadoPrefab, spawnPos, spawnRot);
        else
        {
            var prefab = Resources.Load<GameObject>(path);
            if (prefab != null) tornadoObj = Instantiate(prefab, spawnPos, spawnRot);
        }

        if (tornadoObj != null)
        {
            var follower = tornadoObj.GetComponent<TornadoFollower>();
            if (follower != null)
                follower.Initialize(gameObject, tailWhipTornadoDamage, tailWhipTornadoLifetime, tailWhipTornadoSpeed, tailWhipTornadoTickInterval);
        }
    }

    #endregion

    #region Tsunami Roar

    private bool CanTsunamiRoar()
    {
        if (activeAbility != null || basicRoutine != null) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (IsHeadExhausted()) return false;
        if (Time.time - lastTsunamiTime < tsunamiCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false;

        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        if (!IsFacingTarget(target, SpecialFacingAngle)) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        return dist >= tsunamiPreferredMinDistance && dist <= tsunamiPreferredMaxDistance + 2f;
    }

    private void StartTsunamiRoar()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoTsunamiRoar());
    }

    private IEnumerator CoTsunamiRoar()
    {
        lastAnySkillRecoveryStart = Time.time;
        BeginAction(AIState.Special2);
        if (showBakunawaDebugLogs) Debug.Log("[Bakunawa] Move: Tsunami Roar | State: Special2");

        try
        {
        var target = blackboard.Get<Transform>("target");
        if (target != null)
        {
            Vector3 to = target.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(to.normalized);
        }

        SetTriggerOnAnimator(GetBodyAnimator(), tsunamiTrigger);
        PlaySfx(tsunamiWindupSFX);
        if (tsunamiWindupVFX != null)
        {
            var fx = Instantiate(tsunamiWindupVFX, transform);
        }

        float windup = tsunamiWindup;
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            if (target != null)
            {
                Vector3 to = target.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(to.normalized);
            }
            yield return null;
        }

        PlaySfx(tsunamiImpactSFX);
        if (tsunamiImpactVFX != null)
        {
            var fx = Instantiate(tsunamiImpactVFX, transform);
        }

        SpawnDamageZone(tsunamiDamageZonePrefab, tsunamiZoneOffset, tsunamiZoneScale, tsunamiDamage, tsunamiZoneLifetime);

        float stopTimer = tsunamiStoppageTime;
        while (stopTimer > 0f)
        {
            stopTimer -= Time.deltaTime;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            if (target != null)
            {
                Vector3 to = target.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(to.normalized);
            }
            yield return null;
        }

        EndAction();
        lastTsunamiTime = Time.time;
        lastAnySkillRecoveryEnd = Time.time;
        }
        finally
        {
            activeAbility = null;
            if (isBusy) EndAction();
        }
    }

    #endregion

    #region Beam Spit

    private bool CanBeamSpit()
    {
        if (activeAbility != null || basicRoutine != null) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (IsHeadExhausted()) return false;
        if (Time.time - lastBeamTime < beamCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false;

        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        if (!IsFacingTarget(target, SpecialFacingAngle)) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        return dist >= beamPreferredMinDistance && dist <= beamPreferredMaxDistance + 2f;
    }

    private void StartBeamSpit()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoBeamSpit());
    }

    private IEnumerator CoBeamSpit()
    {
        lastAnySkillRecoveryStart = Time.time;
        BeginAction(AIState.Special1);
        if (showBakunawaDebugLogs) Debug.Log("[Bakunawa] Move: Beam Spit | State: Special1");

        try
        {
            var target = blackboard.Get<Transform>("target");
            if (target != null)
            {
                Vector3 to = target.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(to.normalized);
            }
            Quaternion lockedBeamRot = transform.rotation;

            SetTriggerOnAnimator(GetHeadAnimator(), beamTrigger);
            PlaySfx(beamSFX);
            if (beamWindupVFX != null)
            {
                var fx = Instantiate(beamWindupVFX, transform);
                fx.transform.localPosition = beamSpawnOffset;
            }

            float windup = beamWindup;
            while (windup > 0f)
            {
                windup -= Time.deltaTime;
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                transform.rotation = lockedBeamRot;
                yield return null;
            }

            Vector3 mouthPos = transform.position + lockedBeamRot * beamSpawnOffset;

            if (beamProjectilePrefab != null || !string.IsNullOrEmpty(beamProjectileResourcePath))
            {
                string photonPath = string.IsNullOrEmpty(beamProjectileResourcePath) && beamProjectilePrefab != null
                    ? beamProjectilePrefab.name : beamProjectileResourcePath;

                GameObject beamObj = null;
                try
                {
                    if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !string.IsNullOrEmpty(photonPath))
                        beamObj = PhotonNetwork.Instantiate(photonPath, mouthPos, lockedBeamRot);
                    else if (beamProjectilePrefab != null)
                        beamObj = Instantiate(beamProjectilePrefab, mouthPos, lockedBeamRot);
                    else
                    {
                        var prefab = Resources.Load<GameObject>(photonPath);
                        if (prefab != null) beamObj = Instantiate(prefab, mouthPos, lockedBeamRot);
                    }
                }
                catch (System.Exception ex)
                {
                    if (showBakunawaDebugLogs)
                        Debug.LogWarning("[Bakunawa] Beam spawn failed: " + ex.Message);
                }

                if (beamObj != null)
                {
                    var zone = beamObj.GetComponentInChildren<EnemyDamageZone>();
                    if (zone != null)
                        zone.Initialize(gameObject, beamDamage, beamLifetime, beamDamageDelay);

                    var mover = beamObj.GetComponent<BeamMover>();
                    if (mover != null) Destroy(mover);

                    var destroy = beamObj.GetComponent<DestroyAfterSeconds>();
                    if (destroy == null) destroy = beamObj.AddComponent<DestroyAfterSeconds>();
                    destroy.seconds = beamLifetime;
                }
            }

            float stopTimer = beamStoppageTime;
            while (stopTimer > 0f)
            {
                stopTimer -= Time.deltaTime;
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                transform.rotation = lockedBeamRot;
                yield return null;
            }

            EndAction();
            SetHeadExhaustedForAttack(beamExhaustedTime, 2);
            if (showBakunawaDebugLogs) Debug.Log("[Bakunawa] Beam exhausted until " + headExhaustedEndTime.ToString("F1"));
        }
        finally
        {
            activeAbility = null;
            if (isBusy) EndAction();
        }
    }

    #endregion

    private NodeState ExhaustedStandStill()
    {
        // Don't call SimpleMove(Vector3.zero) - it can interfere with movement after exhausted ends.
        // Movement is already blocked: we're in exhausted branch so MoveTowardsTarget never runs.
        return NodeState.Running;
    }

    private bool IsHeadExhausted()
    {
        return headExhaustedEndTime > 0f && Time.time < headExhaustedEndTime;
    }

    protected override float GetMoveSpeed()
    {
        if (IsHeadExhausted()) return 0f;
        if (isBusy || globalBusyTimer > 0f || activeAbility != null || basicRoutine != null)
            return 0f;
        // Match Berberoka/base: no aiState check - allow movement when not busy
        float baseSpeed = base.GetMoveSpeed();
        if (Time.time >= lastAnySkillRecoveryStart && Time.time <= lastAnySkillRecoveryEnd && lastAnySkillRecoveryStart >= 0f)
        {
            float duration = lastAnySkillRecoveryEnd - lastAnySkillRecoveryStart;
            if (duration > 0f)
            {
                float elapsed = Time.time - lastAnySkillRecoveryStart;
                float progress = Mathf.Clamp01(elapsed / duration);
                return baseSpeed * Mathf.Lerp(0.3f, 1f, progress);
            }
        }
        return baseSpeed;
    }

    private static float DeltaAngleDegrees(float from, float to)
    {
        float delta = Mathf.Repeat(to - from + 180f, 360f) - 180f;
        return delta;
    }

    private void LateUpdate()
    {
        UpdateSwimSplashVFX();

        if (bodySegment == null && tailSegment == null) return;

        // Freeze rotation when exhausted - no segment follow or swim sway; lock body/tail to frozen rotation
        if (IsHeadExhausted())
        {
            transform.rotation = exhaustedFrozenRotation;
            if (bodySegment != null) bodySegment.rotation = exhaustedFrozenRotation;
            if (tailSegment != null)
            {
                Transform tailParent = tailSegment.parent;
                if (tailParent != null)
                    tailSegment.localRotation = Quaternion.Inverse(tailParent.rotation) * exhaustedFrozenRotation;
                else
                    tailSegment.rotation = exhaustedFrozenRotation;
            }
            return;
        }

        Quaternion headRot = transform.rotation;
        float headYaw = headRot.eulerAngles.y;

        if (!segmentFollowInitialized)
        {
            prevHeadYaw = headYaw;
            bodyTargetYaw = headYaw;
            bodyTargetWorldRot = Quaternion.Euler(0f, bodyTargetYaw, 0f);
            tailTargetWorldRot = headRot;
            segmentFollowInitialized = true;
        }

        if (bodySegment != null)
        {
            float deltaHeadYaw = DeltaAngleDegrees(prevHeadYaw, headYaw);
            if (bodyRotationInverted)
                bodyTargetYaw -= deltaHeadYaw;
            else
                bodyTargetYaw = headYaw;
            bodyTargetYaw = Mathf.Repeat(bodyTargetYaw, 360f);
            prevHeadYaw = headYaw;

            float bodyDiff = DeltaAngleDegrees(headYaw, bodyTargetYaw);
            bodyDiff = Mathf.Clamp(bodyDiff, -maxBodyAngleFromHead, maxBodyAngleFromHead);
            bodyTargetYaw = Mathf.Repeat(headYaw + bodyDiff, 360f);

            Quaternion bodyTarget = Quaternion.Euler(0f, bodyTargetYaw, 0f);
            bodyTargetWorldRot = Quaternion.Slerp(bodyTargetWorldRot, bodyTarget, bodyFollowSpeed * Time.deltaTime);
            float appliedBodyYaw = bodyTargetWorldRot.eulerAngles.y;
            float bodyClamp = DeltaAngleDegrees(headYaw, appliedBodyYaw);
            bodyClamp = Mathf.Clamp(bodyClamp, -maxBodyAngleFromHead, maxBodyAngleFromHead);
            bodyTargetWorldRot = Quaternion.Euler(0f, Mathf.Repeat(headYaw + bodyClamp, 360f), 0f);

            float moveSpeed = GetMoveSpeed();
            if (moveSpeed > 0.01f && swimSwayAmplitudeDeg > 0f)
            {
                float sway = Mathf.Sin(Time.time * swimSwayFrequency) * swimSwayAmplitudeDeg;
                bodyTargetWorldRot = Quaternion.Euler(0f, Mathf.Repeat(bodyTargetWorldRot.eulerAngles.y + sway, 360f), 0f);
            }
            bodySegment.rotation = bodyTargetWorldRot;
        }
        else
        {
            prevHeadYaw = headYaw;
        }

        if (tailSegment != null)
        {
            Quaternion tailFollowTarget = bodyRotationInverted ? headRot : (bodySegment != null ? bodyTargetWorldRot : headRot);
            tailTargetWorldRot = Quaternion.Slerp(tailTargetWorldRot, tailFollowTarget, tailFollowSpeed * Time.deltaTime);
            float targetYaw = tailFollowTarget.eulerAngles.y;
            float tailYaw = tailTargetWorldRot.eulerAngles.y;
            float tailClamp = DeltaAngleDegrees(targetYaw, tailYaw);
            tailClamp = Mathf.Clamp(tailClamp, -maxTailAngleFromTarget, maxTailAngleFromTarget);
            tailTargetWorldRot = Quaternion.Euler(0f, Mathf.Repeat(targetYaw + tailClamp, 360f), 0f);

            float moveSpeed = GetMoveSpeed();
            if (moveSpeed > 0.01f && swimSwayAmplitudeDeg > 0f)
            {
                // Body sways first (phase 0); negative swimSwayTailPhase = tail lags behind body
                float tailSway = Mathf.Sin(Time.time * swimSwayFrequency + swimSwayTailPhase) * swimSwayAmplitudeDeg * 0.85f;
                tailTargetWorldRot = Quaternion.Euler(0f, Mathf.Repeat(tailTargetWorldRot.eulerAngles.y + tailSway, 360f), 0f);
            }

            Transform tailParent = tailSegment.parent;
            if (tailParent != null)
                tailSegment.localRotation = Quaternion.Inverse(tailParent.rotation) * tailTargetWorldRot;
        }
    }

    /// <summary>
    /// Enables/disables splash GameObjects based on movement. Runs on all clients.
    /// </summary>
    private void UpdateSwimSplashVFX()
    {
        float speed = 0f;
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
        {
            Animator a = GetBodyAnimator() ?? animator;
            if (a != null && HasFloatParam(speedParam)) speed = a.GetFloat(speedParam);
        }
        else
            speed = GetMoveSpeed();

        bool shouldShow = speed >= swimSplashMinSpeed && !IsHeadExhausted();

        if (swimSplashHead != null && swimSplashHead.activeSelf != shouldShow)
            swimSplashHead.SetActive(shouldShow);
        if (swimSplashBody != null && swimSplashBody.activeSelf != shouldShow)
            swimSplashBody.SetActive(shouldShow);
        if (swimSplashTail != null && swimSplashTail.activeSelf != shouldShow)
            swimSplashTail.SetActive(shouldShow);
    }

    /// <summary>
    /// Bakunawa: Exhausted is on head animator, not main animator.
    /// </summary>
    protected override void SerializeAnimatorStateForPhoton(out float speed, out bool isBusyState, out bool isExhausted)
    {
        speed = 0f;
        isBusyState = false;
        isExhausted = false;
        if (controller != null)
        {
            var v = controller.velocity;
            speed = new Vector3(v.x, 0f, v.z).magnitude;
        }
        if (animator != null && HasBool("Busy"))
            isBusyState = animator.GetBool("Busy");
        Animator head = GetHeadAnimator();
        if (head != null && HasBoolOn(head, headExhaustedBoolParam))
            isExhausted = head.GetBool(headExhaustedBoolParam);
    }

    /// <summary>
    /// Bakunawa: Exhausted lives on head animator.
    /// </summary>
    protected override void DeserializeAnimatorStateForPhoton(float speed, bool isBusyState, bool isExhausted)
    {
        if (animator != null)
        {
            if (HasFloatParam(speedParam))
                animator.SetFloat(speedParam, speed);
            if (HasBool("Busy"))
                animator.SetBool("Busy", isBusyState);
        }
        Animator head = GetHeadAnimator();
        if (head != null && HasBoolOn(head, headExhaustedBoolParam))
            head.SetBool(headExhaustedBoolParam, isExhausted);
    }
}
