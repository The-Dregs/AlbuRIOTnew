using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class BerberokaAI : BaseEnemyAI
{
    [Header("Basic Attack")]
    [Tooltip("How long locked (exhausted) after a basic attack. If 0, uses Stoppage + Recovery.")]
    public float basicAttackExhaustedTime = 0.5f;
    public float basicAttackStoppageTime = 0f;
    public float basicAttackRecoveryTime = 0f;
    [Tooltip("Camera shake for nearby players when basic attack hits (0 = none)")]
    [Range(0f, 1f)] public float basicAttackCameraShakeIntensity = 0f;
    [Range(0f, 0.5f)] public float basicAttackCameraShakeDuration = 0.1f;
    public float basicAttackCameraShakeRadius = 8f;

    [Header("Water Vortex (DoT Pull)")]
    public int vortexTickDamage = 6;
    public float vortexTickInterval = 0.25f;
    public int vortexTicks = 10;
    public float vortexRadius = 5.5f;
    public float vortexWindup = 0.6f;
    public float vortexCooldown = 10f;
    public float vortexPullStrength = 6f;
    [Tooltip("How long locked (exhausted) after Vortex. If > 0, uses this; else uses Stoppage + Recovery.")]
    public float vortexExhaustedTime = 0f;
    public float vortexStoppageTime = 1f;
    public float vortexRecoveryTime = 0.5f;
    public GameObject vortexWindupVFX;
    public GameObject vortexActiveVFX;
    public Vector3 vortexVFXOffset = Vector3.zero;
    public float vortexVFXScale = 1.0f;
    public AudioClip vortexWindupSFX;
    public AudioClip vortexActiveSFX;
    public string vortexTrigger = "Vortex";
    [Tooltip("Camera shake for nearby players when vortex activates (0 = none)")]
    [Range(0f, 1f)] public float vortexCameraShakeIntensity = 0.15f;
    [Range(0f, 0.5f)] public float vortexCameraShakeDuration = 0.15f;
    public float vortexCameraShakeRadius = 12f;
    public GameObject vortexIndicatorPrefab;
    public Vector3 vortexIndicatorOffset = new Vector3(0f, -0.1f, 0f);
    public Vector3 vortexIndicatorScale = new Vector3(3f, 1f, 3f);
    public bool vortexIndicatorRotate90X = true;

    [Header("Flood Crash (AoE + Projectile)")]
    public int floodCrashDamage = 35;
    [Range(0f,180f)] public float floodCrashConeAngle = 60f;
    public float floodCrashRange = 6.5f;
    public float floodCrashWindup = 0.5f;
    public float floodCrashCooldown = 8f;
    [Tooltip("How long locked (exhausted) after Flood Crash. If > 0, uses this; else uses Stoppage + Recovery.")]
    public float floodExhaustedTime = 0f;
    public float floodStoppageTime = 1f;
    public float floodRecoveryTime = 0.5f;
    public GameObject floodWindupVFX;
    public GameObject floodImpactVFX;
    public Vector3 floodVFXOffset = Vector3.zero;
    public float floodVFXScale = 1.0f;
    public AudioClip floodWindupSFX;
    public AudioClip floodImpactSFX;
    public string floodTrigger = "Flood";
    [Tooltip("Camera shake for nearby players when flood crash hits (0 = none)")]
    [Range(0f, 1f)] public float floodCameraShakeIntensity = 0.15f;
    [Range(0f, 0.5f)] public float floodCameraShakeDuration = 0.15f;
    public float floodCameraShakeRadius = 10f;
    public GameObject floodIndicatorPrefab;
    public Vector3 floodIndicatorOffset = new Vector3(0f, -0.1f, 0f);
    public Vector3 floodIndicatorScale = new Vector3(3.5f, 1f, 3.5f);
    public bool floodIndicatorRotate90X = true;
    public string floodProjectilePrefabPath = "Enemies/Projectiles/Flood Crash";
    public Vector3 floodProjectileSpawnOffset = new Vector3(0f,1f,1.5f);
    public float projectileSpeed = 14f;
    public float projectileLifetime = 2.5f;
    [Range(1,10)] public int projectileCount = 3;
    [Range(1f, 120f)] public float projectileSpreadAngle = 30f;

    [Header("Skill Selection Tuning")]
    public float vortexPreferredMinDistance = 6f;
    public float vortexPreferredMaxDistance = 12f;
    public float floodPreferredMinDistance = 3f;
    public float floodPreferredMaxDistance = 7f;
    [Range(0f, 1f)] public float vortexSkillWeight = 0.6f;
    [Range(0f, 1f)] public float floodSkillWeight = 0.8f;

    private float lastVortexTime = -9999f;
    private float lastFloodTime = -9999f;
    private bool vortexActive = false; // allow movement while active but block other specials
    private bool isExhausted = false; // blocks all state transitions until exhausted phase ends
    private Coroutine basicRoutine;


    protected override void InitializeEnemy() { }

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTarget, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");
        var maintainSpace = new ActionNode(blackboard, MaintainSpacing, "maintain_space");
        var targetInAttackFacing = new ConditionNode(blackboard, TargetInAttackRangeFacing, "in_attack_range_facing");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");
        var canVortex = new ConditionNode(blackboard, CanVortex, "can_vortex");
        var doVortex = new ActionNode(blackboard, () => { StartVortex(); return NodeState.Success; }, "vortex");
        var canFlood = new ConditionNode(blackboard, CanFloodCrash, "can_flood");
        var doFlood = new ActionNode(blackboard, () => { StartFloodCrash(); return NodeState.Success; }, "flood");

        var exhaustedGate = new ActionNode(blackboard, () => isExhausted ? NodeState.Running : NodeState.Success, "exhausted_gate");
        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    exhaustedGate,
                    maintainSpace,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "vortex_seq").Add(canVortex, doVortex),
                        new Sequence(blackboard, "flood_seq").Add(canFlood, doFlood),
                        new Sequence(blackboard, "basic_seq").Add(targetInAttackFacing, basicAttack),
                        moveToTarget
                    )
                ),
                new ActionNode(blackboard, Patrol, "patrol")
            );
    }

    protected override void PerformBasicAttack()
    {
        if (basicRoutine != null || isExhausted) return;
        if (enemyData == null) return;
        if (Time.time - lastAttackTime < enemyData.attackCooldown) return;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return;
        if (!IsFacingTarget(target, SpecialFacingAngle))
        {
            FaceTarget(target);
            return; // wait until faced
        }
        basicRoutine = StartCoroutine(CoBasicAttack(target));
    }

    private IEnumerator CoBasicAttack(Transform target)
    {
        BeginAction(AIState.BasicAttack);
        Quaternion lockedRotation = transform.rotation;

        // Windup phase
        if (HasTrigger(attackWindupTrigger))
            SetTriggerSync(attackWindupTrigger);
        else if (HasTrigger(attackTrigger))
            SetTriggerSync(attackTrigger);
        PlayAttackWindupSfx();

        float windup = Mathf.Max(0f, enemyData.attackWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            transform.rotation = lockedRotation;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }

        // Impact phase - damage after windup
        if (HasTrigger(attackImpactTrigger))
            SetTriggerSync(attackImpactTrigger);
        PlayAttackImpactSfx();

        float radius = Mathf.Max(0.8f, enemyData.attackRange);
        Vector3 center = transform.position + transform.forward * (enemyData.attackRange * 0.5f);
        var cols = Physics.OverlapSphere(center, radius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, enemyData.basicDamage);
        }
        if (basicAttackCameraShakeIntensity > 0f)
            TriggerCameraShakeForNearbyPlayers(center, basicAttackCameraShakeRadius, basicAttackCameraShakeIntensity, basicAttackCameraShakeDuration);

        float basicExhaustedTotal = GetBasicExhaustedDuration();
        attackLockTimer = Mathf.Max(enemyData.attackMoveLock, basicExhaustedTotal);

        // End attack state before exhausted so Busy and Exhausted don't overlap
        EndAction();

        // Exhausted phase: distinct state, blocks all transitions; cooldown starts only after exhausted ends
        isExhausted = true;
        if (HasBool("Exhausted")) SetBoolSync("Exhausted", true);
        yield return RunExhaustedPhase(lockedRotation, basicAttackExhaustedTime, basicAttackStoppageTime, basicAttackRecoveryTime);
        if (HasBool("Exhausted")) SetBoolSync("Exhausted", false);
        isExhausted = false;

        basicRoutine = null;
        lastAttackTime = Time.time; // cooldown regenerates only after exhausted
        if (basicExhaustedTotal > 0f)
            globalBusyTimer = Mathf.Max(globalBusyTimer, basicExhaustedTotal);
    }

    protected override bool TrySpecialAbilities()
    {
        if (isBusy) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        // Range gating
        bool inVortexRange = dist >= vortexPreferredMinDistance && dist <= vortexPreferredMaxDistance;
        bool inFloodRange = dist >= floodPreferredMinDistance && dist <= floodPreferredMaxDistance;
        // Scoring based on distance from preferred mid range
        float vortexMid = (vortexPreferredMinDistance + vortexPreferredMaxDistance) * 0.5f;
        float floodMid = (floodPreferredMinDistance + floodPreferredMaxDistance) * 0.5f;
        float vortexDistScore = 1f - Mathf.Clamp01(Mathf.Abs(vortexMid - dist) / 10f);
        float floodDistScore = 1f - Mathf.Clamp01(Mathf.Abs(floodMid - dist) / 10f);
        float vortexScore = inVortexRange ? vortexDistScore * vortexSkillWeight : 0f;
        float floodScore = inFloodRange ? floodDistScore * floodSkillWeight : 0f;
        if (CanVortex() && vortexScore >= floodScore && vortexScore > 0.15f) { StartVortex(); return true; }
        if (CanFloodCrash() && floodScore > vortexScore && floodScore > 0.15f) { StartFloodCrash(); return true; }
        return false;
    }

    // Water Vortex (DoT pull)
    private bool CanVortex()
    {
        if (basicRoutine != null || isBusy || vortexActive || isExhausted) return false;
        if (Time.time - lastVortexTime < vortexCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        if (!IsFacingTarget(target, SpecialFacingAngle)) return false;
        return Vector3.Distance(transform.position, target.position) <= vortexRadius + 1.5f;
    }

    private void StartVortex()
    {
        StartCoroutine(CoVortex());
    }

    private IEnumerator CoVortex()
    {
        BeginAction(AIState.Special1);
        if (HasTrigger(vortexTrigger)) SetTriggerSync(vortexTrigger);
        if (audioSource != null && vortexWindupSFX != null)
        {
            audioSource.clip = vortexWindupSFX;
            audioSource.loop = false;
            audioSource.Play();
        }
        GameObject windupFx = null;
        if (vortexWindupVFX != null)
        {
            Vector3 scale = vortexVFXScale > 0f ? Vector3.one * vortexVFXScale : Vector3.one;
            windupFx = SpawnVFXSync(vortexWindupVFX, vortexVFXOffset, scale, true);
        }
        // Indicator appears at windup start and grows to full radius
        GameObject indicatorWindup = null;
        string indicatorId = $"vortex_indicator_{Time.time}";
        Vector3 indicatorTarget = new Vector3(
            Mathf.Max(0.01f, vortexIndicatorScale.x),
            Mathf.Max(0.01f, vortexIndicatorScale.y),
            Mathf.Max(0.01f, vortexIndicatorScale.z)
        );
        if (vortexIndicatorPrefab != null)
        {
            Vector3 initialScale = new Vector3(0.01f, 0.01f, 0.01f);
            indicatorWindup = SpawnVFXSync(vortexIndicatorPrefab, vortexIndicatorOffset, initialScale, true, indicatorId);
            if (indicatorWindup != null && vortexIndicatorRotate90X) indicatorWindup.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
        float wind = Mathf.Max(0f, vortexWindup);
        float indicatorGrowTime = Mathf.Max(0.01f, vortexWindup * 0.2f); // indicator emerges in 50% of windup
        float indicatorTimer = 0f;
        while (wind > 0f)
        {
            wind -= Time.deltaTime;
            // freeze movement during windup
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            // face target during windup
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
            // grow indicator
            if (indicatorWindup != null && indicatorTimer < indicatorGrowTime)
            {
                indicatorTimer += Time.deltaTime;
                float pct = Mathf.Clamp01(indicatorTimer / indicatorGrowTime);
                Vector3 s = Vector3.Lerp(new Vector3(0.01f, 0.01f, 0.01f), indicatorTarget, pct);
                indicatorWindup.transform.localScale = s;
                // Sync scale to remote clients
                SyncVFXScale(indicatorId, s);
            }
            yield return null;
        }
        if (audioSource != null && audioSource.clip == vortexWindupSFX)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }
        if (windupFx != null) Destroy(windupFx);
        // replace windup indicator with active one (keeps same scale)
        if (indicatorWindup != null) Destroy(indicatorWindup);
        PlaySfx(vortexActiveSFX);
        if (vortexCameraShakeIntensity > 0f)
            TriggerCameraShakeForNearbyPlayers(transform.position, vortexCameraShakeRadius, vortexCameraShakeIntensity, vortexCameraShakeDuration);
        GameObject activeFx = null;
        GameObject indicatorFx = null;
        if (vortexActiveVFX != null)
        {
            Vector3 scale = vortexVFXScale > 0f ? Vector3.one * vortexVFXScale : Vector3.one;
            activeFx = SpawnVFXSync(vortexActiveVFX, vortexVFXOffset, scale, true);
        }
        if (vortexIndicatorPrefab != null)
        {
            Vector3 indicatorScale = new Vector3(
                Mathf.Max(0.01f, vortexIndicatorScale.x),
                Mathf.Max(0.01f, vortexIndicatorScale.y),
                Mathf.Max(0.01f, vortexIndicatorScale.z)
            );
            indicatorFx = SpawnVFXSync(vortexIndicatorPrefab, vortexIndicatorOffset, indicatorScale, true);
            if (indicatorFx != null && vortexIndicatorRotate90X) indicatorFx.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
        vortexActive = true;
        EndAction(); // Release enemy so he can move and attack during vortex (vortex is a buff)
        int ticks = Mathf.Max(1, vortexTicks);
        float interval = Mathf.Max(0.05f, vortexTickInterval);
        for (int i = 0; i < ticks; i++)
        {
            var cols = Physics.OverlapSphere(transform.position, vortexRadius, LayerMask.GetMask("Player"));
            foreach (var c in cols)
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, vortexTickDamage);
                // pull towards center
                var rb = c.attachedRigidbody;
                if (rb != null)
                {
                    Vector3 dir = (transform.position - c.transform.position);
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.0001f)
                        rb.AddForce(dir.normalized * vortexPullStrength, ForceMode.Acceleration);
                }
            }
            yield return new WaitForSeconds(interval);
        }
        if (activeFx != null) Destroy(activeFx);
        if (indicatorFx != null) Destroy(indicatorFx);
        vortexActive = false;
        lastVortexTime = Time.time; // no exhausted - vortex is a buff, Berberoka can keep fighting
    }

    // Flood Crash (cone)
    private bool CanFloodCrash()
    {
        if (basicRoutine != null || isBusy || isExhausted) return false;
        if (Time.time - lastFloodTime < floodCrashCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        if (!IsFacingTarget(target, SpecialFacingAngle)) return false;
        return Vector3.Distance(transform.position, target.position) <= floodCrashRange + 0.5f;
    }

    private void StartFloodCrash()
    {
        StartCoroutine(CoFloodCrash());
    }

    private IEnumerator CoFloodCrash()
    {
        BeginAction(AIState.Special2);
        if (HasTrigger(floodTrigger)) SetTriggerSync(floodTrigger);
        PlaySfx(floodWindupSFX);
        GameObject windFx = null;
        if (floodWindupVFX != null)
        {
            Vector3 scale = floodVFXScale > 0f ? Vector3.one * floodVFXScale : Vector3.one;
            windFx = SpawnVFXSync(floodWindupVFX, floodVFXOffset, scale, true);
        }
        // Flood indicator during windup
        GameObject floodIndicator = null;
        string floodIndicatorId = $"flood_indicator_{Time.time}";
        if (floodIndicatorPrefab != null)
        {
            Vector3 initialScale = new Vector3(0.01f, 0.01f, 0.01f);
            floodIndicator = SpawnVFXSync(floodIndicatorPrefab, floodIndicatorOffset, initialScale, true, floodIndicatorId);
            if (floodIndicator != null && floodIndicatorRotate90X) floodIndicator.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
        Vector3 floodTargetScale = new Vector3(
            Mathf.Max(0.01f, floodIndicatorScale.x),
            Mathf.Max(0.01f, floodIndicatorScale.y),
            Mathf.Max(0.01f, floodIndicatorScale.z)
        );
        float windTime = Mathf.Max(0f, floodCrashWindup);
        float floodIndicatorGrowTime = Mathf.Max(0.01f, floodCrashWindup * 0.9f);
        float floodIndicatorTimer = 0f;
        while (windTime > 0f)
        {
            windTime -= Time.deltaTime;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
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
            if (floodIndicator != null && floodIndicatorTimer < floodIndicatorGrowTime)
            {
                floodIndicatorTimer += Time.deltaTime;
                float t01 = Mathf.Clamp01(floodIndicatorTimer / floodIndicatorGrowTime);
                Vector3 s = Vector3.Lerp(new Vector3(0.01f,0.01f,0.01f), floodTargetScale, t01);
                floodIndicator.transform.localScale = s;
                // Sync scale to remote clients
                SyncVFXScale(floodIndicatorId, s);
            }
            yield return null;
        }
        if (windFx != null) Destroy(windFx);
        if (floodImpactVFX != null)
        {
            Vector3 scale = floodVFXScale > 0f ? Vector3.one * floodVFXScale : Vector3.one;
            SpawnVFXSync(floodImpactVFX, floodVFXOffset, scale, true);
        }
        PlaySfx(floodImpactSFX);
        if (floodIndicator != null) Destroy(floodIndicator);
        if (floodCameraShakeIntensity > 0f)
            TriggerCameraShakeForNearbyPlayers(transform.position, floodCameraShakeRadius, floodCameraShakeIntensity, floodCameraShakeDuration);

        // cone damage
        var all = Physics.OverlapSphere(transform.position, floodCrashRange, LayerMask.GetMask("Player"));
        Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
        float halfAngle = Mathf.Clamp(floodCrashConeAngle * 0.5f, 0f, 90f);
        foreach (var c in all)
        {
            Vector3 to = c.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) continue;
            float angle = Vector3.Angle(fwd, to.normalized);
            if (angle <= halfAngle)
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null) DamageRelay.ApplyToPlayer(ps.gameObject, floodCrashDamage);
            }
        }
        if (!string.IsNullOrEmpty(floodProjectilePrefabPath) && projectileCount > 0)
        {
            float step = (projectileCount > 1) ? projectileSpreadAngle / (projectileCount - 1) : 0f;
            float startYaw = -projectileSpreadAngle * 0.5f;
            for (int i = 0; i < projectileCount; i++)
            {
                float angle = startYaw + step * i;
                Quaternion rot = transform.rotation * Quaternion.Euler(0f, angle, 0f);
                Vector3 spawnPos = transform.position + rot * floodProjectileSpawnOffset;
                
                // Network-safe instantiation
                GameObject projObj;
                if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                    projObj = PhotonNetwork.Instantiate(floodProjectilePrefabPath, spawnPos, rot);
                else
                {
                    // Load prefab from Resources for offline mode
                    GameObject prefab = Resources.Load<GameObject>(floodProjectilePrefabPath);
                    if (prefab != null)
                        projObj = Instantiate(prefab, spawnPos, rot);
                    else
                    {
                        Debug.LogError($"[BerberokaAI] Failed to load projectile prefab from path: {floodProjectilePrefabPath}");
                        continue;
                    }
                }
                
                var proj = projObj.GetComponent<EnemyProjectile>();
                if (proj != null)
                    proj.Initialize(gameObject, floodCrashDamage, projectileSpeed, projectileLifetime);
            }
        }

        // Exhausted: distinct phase after flood, no overlap with Busy
        EndAction();
        isExhausted = true;
        if (HasBool("Exhausted")) SetBoolSync("Exhausted", true);
        yield return RunExhaustedPhase(transform.rotation, floodExhaustedTime, floodStoppageTime, floodRecoveryTime);
        if (HasBool("Exhausted")) SetBoolSync("Exhausted", false);
        lastFloodTime = Time.time; // cooldown regenerates only after exhausted
        isExhausted = false;
    }

    // Keep distance from target; if too close, back off or strafe
    private NodeState MaintainSpacing()
    {
        var target = blackboard.Get<Transform>("target");
        if (target == null || controller == null || enemyData == null) return NodeState.Success;
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist < Mathf.Max(0.1f, PreferredDistance))
        {
            if (attackLockTimer > 0f || isBusy || isExhausted) return NodeState.Running;
            Vector3 dir = -to.normalized;
            float speed = GetMoveSpeed() * Mathf.Clamp(BackoffSpeedMultiplier, 0.1f, 2f);
            if (controller != null && controller.enabled) controller.SimpleMove(dir * speed);
            // face target while backing off
            Vector3 lookTarget = new Vector3(target.position.x, transform.position.y, target.position.z);
            Vector3 dirToLook = (lookTarget - transform.position);
            if (dirToLook.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dirToLook);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, RotationSpeed * Time.deltaTime);
            }
            aiState = AIState.Chase;
            return NodeState.Running;
        }
        return NodeState.Success;
    }

    /// <summary>Runs exhausted phase: if exhaustedTime > 0 uses that; else if stoppage+recovery > 0 uses those. ExhaustedTime=0 means no exhausted.</summary>
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
        }
        else if (exhaustedTime == 0f)
        {
            // ExhaustedTime=0 means no exhausted; skip even if stoppage/recovery are set
            yield break;
        }
        else if (stoppageTime > 0f || recoveryTime > 0f)
        {
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
    }

    private float GetBasicExhaustedDuration()
    {
        if (basicAttackExhaustedTime > 0f) return basicAttackExhaustedTime;
        return basicAttackStoppageTime + basicAttackRecoveryTime;
    }

    public float VortexCooldownRemaining => Mathf.Max(0f, vortexCooldown - (Time.time - lastVortexTime));
    public float FloodCooldownRemaining => Mathf.Max(0f, floodCrashCooldown - (Time.time - lastFloodTime));

    protected override string GetExtraDebugInfo()
    {
        float basic = enemyData != null ? Mathf.Max(0f, enemyData.attackCooldown - (Time.time - lastAttackTime)) : 0f;
        return $" CD basic:{basic:F1} vortex:{VortexCooldownRemaining:F1} flood:{FloodCooldownRemaining:F1}";
    }

    private bool IsFacingTarget(Transform target, float maxAngle)
    {
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return false;
        float angle = Vector3.Angle(new Vector3(transform.forward.x, 0f, transform.forward.z).normalized, to.normalized);
        return angle <= Mathf.Clamp(maxAngle, 1f, 60f);
    }

    private bool TargetInAttackRangeFacing()
    {
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        if (dist > enemyData.attackRange + 0.5f) return false;
        return IsFacingTarget(target, SpecialFacingAngle);
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
        // Pause movement when not facing
        if (controller != null && controller.enabled)
            controller.SimpleMove(Vector3.zero);
    }
}