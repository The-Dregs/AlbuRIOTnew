using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public abstract class BaseEnemyAI : MonoBehaviourPun, IEnemyDamageable, IPunObservable
{
    [Header("Enemy Data")]
    public EnemyData enemyData;
    
    [Header("Animation")]
    public Animator animator;
    public string speedParam = "Speed";
    public string attackTrigger = "Attack"; // Legacy: single trigger OR use separate windup/impact triggers
    public string attackWindupTrigger = "AttackWindup"; // Optional: separate windup trigger
    public string attackImpactTrigger = "AttackImpact"; // Optional: separate impact trigger
    public string hitTrigger = "Hit";
    public string dieTrigger = "Die";
    public string isDeadBool = "IsDead";
    
    [Header("Debug")]
    public bool showDebugInfo = false;

    [Header("SFX")]
    [Tooltip("Plays randomly during idle, patrol, or chase (ambient)")]
    public AudioClip idleSFX;
    [Tooltip("Min–max seconds between random idle SFX plays")]
    public Vector2 idleSfxInterval = new Vector2(3f, 8f);
    [Tooltip("Volume (1 = clip's native level)")]
    [Range(0f, 1f)] public float idleSfxVolume = 1f;
    [Tooltip("Plays when walking (patrol)")]
    public AudioClip walkSFX;
    [Tooltip("Volume (1 = clip's native level)")]
    [Range(0f, 1f)] public float walkSfxVolume = 1f;
    [Tooltip("Seconds between walk footsteps (lower = faster)")]
    [Range(0.2f, 1.5f)] public float walkSfxInterval = 0.5f;
    [Range(0.5f, 2f)] public float walkSfxPitch = 1f;
    [Tooltip("Plays when chasing target")]
    public AudioClip chaseSFX;
    [Tooltip("Volume (1 = clip's native level)")]
    [Range(0f, 1f)] public float chaseSfxVolume = 1f;
    [Tooltip("Seconds between chase footsteps (lower = faster)")]
    [Range(0.2f, 1.5f)] public float chaseSfxInterval = 0.4f;
    [Range(0.5f, 2f)] public float chaseSfxPitch = 1f;
    [Tooltip("Duration of fade-out when leaving walk/chase state")]
    [Range(0.05f, 0.5f)] public float movementSfxFadeOutDuration = 0.15f;
    [Tooltip("Plays at basic attack windup")]
    public AudioClip basicAttackWindupSFX;
    [Tooltip("Plays at basic attack impact")]
    public AudioClip basicAttackImpactSFX;
    [Range(0f, 1f)] public float attackSfxVolume = 1f;
    [Range(0f, 1f)] public float hitSfxVolume = 1f;
    [Range(0f, 1f)] public float deathSfxVolume = 1f;

    // Core components
    protected AudioSource audioSource;
    protected AudioSource oneShotAudioSource;
    protected CharacterController controller;
    protected int currentHealth;
    protected int baseMaxHealth; // Store original max health for scaling
    protected bool isDead = false;
    protected bool isAttacking = false;
    protected bool isBusy = false;
    // After any attack/ability completes, we apply a short global busy window
    // so the enemy can only do one thing at a time and won't chain attacks instantly.
    protected float globalBusyTimer = 0f;
    
    // AI State
    protected Blackboard blackboard;
    protected Node behaviorTree;
    protected Transform targetPlayer;
    protected float targetLastSeenTime = -999f;
    protected Vector3 spawnPoint;
    protected float lastAttackTime;
    protected float attackLockTimer = 0f;
    
    
    // Movement
    protected Vector3 patrolTarget;
    protected float patrolWaitTimer;
    // Note: Speed Settings and Chase properties are now in EnemyData

    [Header("Buff VFX Prefabs (optional)")]
    public GameObject buffDamageVFXPrefab;
    public GameObject buffSpeedVFXPrefab;
    public GameObject buffStaminaVFXPrefab;
    public GameObject buffHealthVFXPrefab;

    [Header("Buff VFX Settings")]
    public Vector3 buffVfxOffset = Vector3.zero;
    public float buffVfxScale = 1.5f;

    [Header("Power Steal")]
    [SerializeField] private string powerStealEnemyName = ""; // canonical name used by PowerStealManager
    
    [Header("Hit Knockback Effect")]
    [Tooltip("How far the enemy is pushed back when hit (0 = no knockback). Keep low for subtle effect.")]
    [Range(0f, 2f)] public float knockbackForce = 0.15f;
    [Tooltip("Duration of the knockback push (in seconds)")]
    [Range(0.03f, 0.3f)] public float knockbackDuration = 0.08f;

    private readonly Dictionary<BuffType, GameObject> activeBuffVfx = new Dictionary<BuffType, GameObject>();
    [System.NonSerialized] private GameObject lastHitSource; // track killer to award power (not serialized)
    private Coroutine knockbackCoroutine;
    private Coroutine movementSfxFadeCoroutine;
    private float lastWalkSfxTime = -999f;
    private float lastChaseSfxTime = -999f;
    private float lastIdleSfxTime = -999f;
    private float nextIdleSfxTime = -999f; // random next play time
    private AIState prevAiStateForIdleSfx = AIState.Idle; // detect transition into Idle
    private readonly HashSet<string> animatorFloatParams = new HashSet<string>();
    private readonly HashSet<string> animatorTriggerParams = new HashSet<string>();
    private readonly HashSet<string> animatorBoolParams = new HashSet<string>();
    
    // Events
    public System.Action<BaseEnemyAI> OnEnemyDied;
    public System.Action<BaseEnemyAI, int> OnEnemyTookDamage;
    
    #region Unity Lifecycle
    
#if UNITY_EDITOR
    void Reset()
    {
        // Unity should automatically apply default references when Reset() is called
        // This happens when you add the component or click Reset in inspector
        // If defaults still aren't applied, use Resources fallback in OnValidate
    }
    
    void OnValidate()
    {
        // In editor, try to load default prefabs from Resources if fields are null
        // This provides a fallback if Unity's default references weren't applied
        if (buffDamageVFXPrefab == null)
            buffDamageVFXPrefab = Resources.Load<GameObject>("BuffVFX/DamageBuff");
        if (buffSpeedVFXPrefab == null)
            buffSpeedVFXPrefab = Resources.Load<GameObject>("BuffVFX/SpeedBuff");
        if (buffStaminaVFXPrefab == null)
            buffStaminaVFXPrefab = Resources.Load<GameObject>("BuffVFX/StaminaBuff");
        if (buffHealthVFXPrefab == null)
            buffHealthVFXPrefab = Resources.Load<GameObject>("BuffVFX/HealthBuff");
    }
#endif
    
    protected virtual void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = GetComponentInChildren<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        oneShotAudioSource = CreateOrGetOneShotAudioSource();
        EnsureEnemyAudioSource3D();
        CacheAnimatorParameters();
        
        spawnPoint = transform.position;
        
        baseMaxHealth = enemyData != null ? enemyData.maxHealth : 100;
        currentHealth = baseMaxHealth;
        
        ApplyMultiplayerScaling();
        
        blackboard = new Blackboard { owner = gameObject };
        BuildBehaviorTree();
        
        InitializeEnemy();
        
        // Play spawn SFX if set in EnemyData
        if (enemyData != null && enemyData.spawnSFX != null)
            PlaySfx(enemyData.spawnSFX);
        
        // Runtime fallback: try Resources if defaults weren't applied (works in editor and builds)
        if (buffDamageVFXPrefab == null)
            buffDamageVFXPrefab = Resources.Load<GameObject>("BuffVFX/DamageBuff");
        if (buffSpeedVFXPrefab == null)
            buffSpeedVFXPrefab = Resources.Load<GameObject>("BuffVFX/SpeedBuff");
        if (buffStaminaVFXPrefab == null)
            buffStaminaVFXPrefab = Resources.Load<GameObject>("BuffVFX/StaminaBuff");
        if (buffHealthVFXPrefab == null)
            buffHealthVFXPrefab = Resources.Load<GameObject>("BuffVFX/HealthBuff");
        
        // Ensure PhotonView knows about this component for IPunObservable
        if (photonView != null)
        {
            photonView.FindObservables(true);
        }

        // Ensure debug overlay exists and is enabled for all enemies (F8 toggles visibility)
        var debugOverhead = GetComponent<EnemyDebugOverhead>();
        if (debugOverhead == null)
            debugOverhead = gameObject.AddComponent<EnemyDebugOverhead>();
        debugOverhead.SetEnable(true);
    }
    
    protected virtual void Update()
    {
        if (isDead) return;
        
        // Network authority: only master client (or offline) drives AI
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
            return;
        
            
        UpdateAnimation();
        UpdateAttackLock();
        UpdateSfxLoop();
        // decay global busy timer
        if (globalBusyTimer > 0f)
            globalBusyTimer -= Time.deltaTime;
        
        if (showDebugInfo)
            LogDebugState();
        
        if (behaviorTree != null)
        {
            behaviorTree.Tick();
        }
    }
    
    private float _lastDebugLogTime = -999f;
    private const float DebugLogInterval = 0.5f;
    private void LogDebugState()
    {
        if (Time.time - _lastDebugLogTime < DebugLogInterval) return;
        _lastDebugLogTime = Time.time;
        bool exhausted = IsRotationLocked();
        string tgt = targetPlayer != null ? targetPlayer.name : "null";
        float dist = targetPlayer != null ? Vector3.Distance(transform.position, targetPlayer.position) : -1f;
        string extra = GetExtraDebugInfo();
        Debug.Log($"[{gameObject.name}] State:{GetEffectiveStateForDebug()} (aiState:{aiState}) isBusy:{isBusy} globalBusy:{globalBusyTimer:F1}s attackLock:{attackLockTimer:F1}s exhausted:{exhausted} target:{tgt} dist:{dist:F1}{extra}");
    }

    /// <summary>Override in derived classes to append skill cooldowns etc. to debug logs. Include leading space if non-empty.</summary>
    protected virtual string GetExtraDebugInfo() => "";

    /// <summary>Ensure enemy AudioSource uses 3D spatial audio so sounds attenuate with distance.</summary>
    private void EnsureEnemyAudioSource3D()
    {
        if (audioSource == null) return;
        audioSource.spatialBlend = 1f; // 3D - attenuates with distance
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 1f;
        audioSource.maxDistance = 50f; // Attenuate beyond this so distant enemies aren't heard

        if (oneShotAudioSource != null)
        {
            oneShotAudioSource.spatialBlend = 1f;
            oneShotAudioSource.rolloffMode = AudioRolloffMode.Linear;
            oneShotAudioSource.minDistance = audioSource.minDistance;
            oneShotAudioSource.maxDistance = audioSource.maxDistance;
            oneShotAudioSource.dopplerLevel = 0f;
            oneShotAudioSource.playOnAwake = false;
            oneShotAudioSource.loop = false;
        }
    }

    private AudioSource CreateOrGetOneShotAudioSource()
    {
        const string oneShotChildName = "EnemyOneShotAudio";
        Transform oneShotChild = transform.Find(oneShotChildName);
        if (oneShotChild == null)
        {
            var go = new GameObject(oneShotChildName);
            oneShotChild = go.transform;
            oneShotChild.SetParent(transform, false);
            oneShotChild.localPosition = Vector3.zero;
        }

        var src = oneShotChild.GetComponent<AudioSource>();
        if (src == null) src = oneShotChild.gameObject.AddComponent<AudioSource>();
        return src;
    }

    /// <summary>Play a one-shot SFX using the auto-found AudioSource. Safe to call with null clip.</summary>
    protected void PlaySfx(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null) return;
        AudioSource source = oneShotAudioSource != null ? oneShotAudioSource : audioSource;
        if (source != null)
            source.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
    }

    /// <summary>Get SFX: component first, fallback to EnemyData.</summary>
    protected AudioClip GetSfx(AudioClip componentClip, System.Func<EnemyData, AudioClip> fromData)
    {
        if (componentClip != null) return componentClip;
        return enemyData != null && fromData != null ? fromData(enemyData) : null;
    }

    /// <summary>Play basic attack windup SFX (component or EnemyData fallback).</summary>
    protected void PlayAttackWindupSfx()
    {
        PlaySfx(GetSfx(basicAttackWindupSFX, d => d.attackWindupSFX), attackSfxVolume);
    }

    /// <summary>Play basic attack impact SFX (component or EnemyData fallback).</summary>
    protected void PlayAttackImpactSfx()
    {
        PlaySfx(GetSfx(basicAttackImpactSFX, d => d.attackImpactSFX), attackSfxVolume);
    }

    /// <summary>Play walk/chase SFX on main channel so it can be faded when state ends.</summary>
    private void PlayMovementSfx(AudioClip clip, float volume, float pitch)
    {
        if (audioSource == null || clip == null) return;
        if (movementSfxFadeCoroutine != null)
        {
            StopCoroutine(movementSfxFadeCoroutine);
            movementSfxFadeCoroutine = null;
        }
        audioSource.clip = clip;
        audioSource.volume = Mathf.Clamp01(volume);
        audioSource.pitch = pitch;
        audioSource.loop = false;
        audioSource.Play();
    }

    /// <summary>Fade out walk/chase SFX when movement state ends (smooth transition).</summary>
    private void StopMovementSfx()
    {
        if (audioSource == null) return;
        AudioClip w = GetSfx(walkSFX, d => d.walkSFX);
        AudioClip c = GetSfx(chaseSFX, d => d.chaseSFX);
        if ((audioSource.clip == w || audioSource.clip == c) && audioSource.isPlaying)
        {
            if (movementSfxFadeCoroutine != null)
                StopCoroutine(movementSfxFadeCoroutine);
            movementSfxFadeCoroutine = StartCoroutine(CoFadeOutMovementSfx());
        }
    }

    private System.Collections.IEnumerator CoFadeOutMovementSfx()
    {
        if (audioSource == null) { movementSfxFadeCoroutine = null; yield break; }
        float duration = Mathf.Max(0.05f, movementSfxFadeOutDuration);
        float startVol = audioSource.volume;
        float elapsed = 0f;
        while (elapsed < duration && audioSource != null)
        {
            elapsed += Time.deltaTime;
            audioSource.volume = Mathf.Lerp(startVol, 0f, elapsed / duration);
            yield return null;
        }
        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.clip = null;
            audioSource.volume = 1f;
            audioSource.pitch = 1f;
        }
        movementSfxFadeCoroutine = null;
    }

    private void UpdateSfxLoop()
    {
        if (audioSource == null) return;
        if (isBusy || globalBusyTimer > 0f)
        {
            StopMovementSfx();
            return;
        }
        float t = Time.time;
        float planarSpeed = controller != null ? new Vector3(controller.velocity.x, 0f, controller.velocity.z).magnitude : 0f;
        bool moving = planarSpeed > 0.3f;

        AudioClip walkClip = GetSfx(walkSFX, d => d.walkSFX);
        AudioClip chaseClip = GetSfx(chaseSFX, d => d.chaseSFX);
        AudioClip idleClip = GetSfx(idleSFX, d => d.idleSFX);

        bool shouldPlayWalk = aiState == AIState.Patrol && moving && walkClip != null;
        bool shouldPlayChase = aiState == AIState.Chase && moving && chaseClip != null;

        if (shouldPlayWalk && t - lastWalkSfxTime >= walkSfxInterval)
        {
            lastWalkSfxTime = t;
            PlayMovementSfx(walkClip, walkSfxVolume, walkSfxPitch);
        }
        else if (shouldPlayChase && t - lastChaseSfxTime >= chaseSfxInterval)
        {
            lastChaseSfxTime = t;
            PlayMovementSfx(chaseClip, chaseSfxVolume, chaseSfxPitch);
        }

        // Stop walk/chase SFX when state ends (e.g. stopped moving, attacking, exhausted)
        if (!shouldPlayWalk && !shouldPlayChase)
        {
            StopMovementSfx();
        }

        // Idle SFX: randomly play when in Idle, Patrol, or Chase (ambient sounds). Never play at the start of an Idle transition.
        bool inIdleOrMovingState = aiState == AIState.Idle || aiState == AIState.Patrol || aiState == AIState.Chase;
        float minI = Mathf.Min(idleSfxInterval.x, idleSfxInterval.y);
        float maxI = Mathf.Max(idleSfxInterval.x, idleSfxInterval.y);
        if (idleClip != null && inIdleOrMovingState && maxI > 0f)
        {
            // When transitioning TO Idle, reset timer so we don't play at the start of the transition
            if (aiState == AIState.Idle && prevAiStateForIdleSfx != AIState.Idle)
            {
                nextIdleSfxTime = t + UnityEngine.Random.Range(minI, maxI);
            }
            else if (nextIdleSfxTime < 0f)
            {
                nextIdleSfxTime = t + UnityEngine.Random.Range(minI, maxI);
            }
            if (t >= nextIdleSfxTime)
            {
                lastIdleSfxTime = t;
                PlaySfx(idleClip, idleSfxVolume);
                nextIdleSfxTime = t + UnityEngine.Random.Range(minI, maxI);
            }
        }
        else
        {
            nextIdleSfxTime = -1f; // reset when not in valid state
        }
        prevAiStateForIdleSfx = aiState;
    }
    
    #endregion

    #region State Machine
    public enum AIState { Idle, Patrol, ReturnToPatrol, Chase, BasicAttack, Special1, Special2 }
    protected AIState aiState = AIState.Idle;

    protected void BeginAction(AIState state, bool setAnimatorBusy = true)
    {
        isBusy = true;
        aiState = state;
        if (setAnimatorBusy && animator != null && HasBool("Busy"))
        {
            animator.SetBool("Busy", true);
            // Sync to other clients
            if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            {
                photonView.RPC("RPC_SetBool", RpcTarget.Others, "Busy", true);
            }
        }
    }

    protected void EndAction(bool clearAnimatorBusy = true)
    {
        if (clearAnimatorBusy && animator != null && HasBool("Busy"))
        {
            animator.SetBool("Busy", false);
            // Sync to other clients
            if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            {
                photonView.RPC("RPC_SetBool", RpcTarget.Others, "Busy", false);
            }
        }
        isBusy = false;
        aiState = AIState.Idle;
        // Apply a post-action busy equal to basic attack cooldown
        // to prevent immediate follow-up actions.
        if (enemyData != null)
        {
            float postBusy = Mathf.Max(0f, enemyData.attackCooldown);
            if (postBusy > globalBusyTimer) globalBusyTimer = postBusy;
        }
    }
    #endregion
    
    #region Abstract Methods (Override in specific enemy types)
    
    /// <summary>
    /// Initialize enemy-specific data and behaviors
    /// </summary>
    protected abstract void InitializeEnemy();
    
    /// <summary>
    /// Build the behavior tree for this specific enemy type
    /// </summary>
    protected abstract void BuildBehaviorTree();
    
    /// <summary>
    /// Perform the basic attack
    /// </summary>
    protected abstract void PerformBasicAttack();
    
    /// <summary>
    /// Check if special abilities are available and execute them
    /// </summary>
    protected abstract bool TrySpecialAbilities();
    
    // (generic skill helpers removed per request)
    
    #endregion
    
    #region Common AI Behaviors
    
    protected virtual void UpdateAnimation()
    {
        if (animator != null && HasFloatParam(speedParam))
        {
            // Always check GetMoveSpeed() first - it returns 0 when AI should be stopped
            // This catches: isBusy, globalBusyTimer, activeAbility, basicRoutine, aiState==Idle (in derived classes)
            float expectedSpeed = GetMoveSpeed();
            if (expectedSpeed <= 0f)
            {
                animator.SetFloat(speedParam, 0f);
                return;
            }
            
            // If AI should be moving, use actual velocity for smooth animation
            Vector3 velocity = controller != null ? controller.velocity : Vector3.zero;
            float planarSpeed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
            
            // If actual velocity is very low or AI is idle, clamp to 0
            if (planarSpeed < 0.01f || aiState == AIState.Idle)
            {
                animator.SetFloat(speedParam, 0f);
            }
            else
            {
                animator.SetFloat(speedParam, planarSpeed);
            }
        }
    }
    
    protected virtual void UpdateAttackLock()
    {
        if (attackLockTimer > 0f)
            attackLockTimer -= Time.deltaTime;
    }

    
    
    protected NodeState UpdateTarget()
    {
        Transform bestTarget = FindBestTarget();

        if (bestTarget != targetPlayer)
        {
            targetPlayer = bestTarget;
            if (targetPlayer == null)
                targetLastSeenTime = -999f;
        }

        if (targetPlayer != null && IsPlayerValid(targetPlayer) && IsTargetVisible(targetPlayer))
            targetLastSeenTime = Time.time;
        
        if (targetPlayer != null)
            blackboard.Set("target", targetPlayer);
        else
            blackboard.Remove("target");
        
        return NodeState.Success;
    }
    
    protected bool HasTarget()
    {
        var target = blackboard.Get<Transform>("target");
        if (target == null || !IsPlayerValid(target)) return false;
        if (enemyData != null)
        {
            float loseRange = enemyData.chaseLoseRange;
            float distSqr = (transform.position - target.position).sqrMagnitude;
            // hard drop if beyond chase lose range
            if (distSqr > loseRange * loseRange)
            {
                targetPlayer = null;
                blackboard.Remove("target");
                return false;
            }
        }
        return true;
    }
    
    protected bool TargetInDetectionRange()
    {
        var target = blackboard.Get<Transform>("target");
        if (target == null || enemyData == null) return false;
        float range = enemyData.detectionRange;
        return (transform.position - target.position).sqrMagnitude <= range * range;
    }
    
    protected bool TargetInAttackRange()
    {
        var target = blackboard.Get<Transform>("target");
        if (target == null || enemyData == null) return false;
        float range = enemyData.attackRange;
        return (transform.position - target.position).sqrMagnitude <= range * range;
    }

    /// <summary>True when in attack range AND facing the target. Use for attacks that require facing.</summary>
    protected bool TargetInAttackRangeAndFacing()
    {
        var target = blackboard.Get<Transform>("target");
        if (target == null || enemyData == null) return false;
        if (!TargetInAttackRange()) return false;
        return IsFacingTarget(target, SpecialFacingAngle);
    }
    
    protected NodeState MoveTowardsTarget()
    {
        // Don't overwrite aiState when busy (e.g. during Lunge, Dive) - preserves correct debug state
        if (isBusy || globalBusyTimer > 0f) return NodeState.Running;
        var target = blackboard.Get<Transform>("target");
        if (target == null || controller == null || enemyData == null) return NodeState.Failure;
        // lose aggro and bail if target got too far
        float lose = enemyData.chaseLoseRange;
        float currentDistanceSqr = (transform.position - target.position).sqrMagnitude;
        if (currentDistanceSqr > lose * lose)
        {
            targetPlayer = null;
            blackboard.Remove("target");
            return NodeState.Failure;
        }
        
        // Use target position at same height as enemy for horizontal direction (avoids chasing wrong point if target root is offset)
        Vector3 targetPos = new Vector3(target.position.x, transform.position.y, target.position.z);
        Vector3 direction = (targetPos - transform.position);
        float distance = direction.magnitude;
        aiState = AIState.Chase; // Set before GetMoveSpeed so derived classes (e.g. Sigbin) return non-zero when chasing
        // Only block when in attack range (prevents attack spam). Chase movement allowed even with attack lock.
        if (distance <= enemyData.attackRange && (attackLockTimer > 0f || isBusy || globalBusyTimer > 0f))
            return NodeState.Running;
        // If within attack range but on cooldown, stand still until ready
        if (distance <= enemyData.attackRange)
        {
            bool cooldownReady = (Time.time - lastAttackTime) >= enemyData.attackCooldown;
            float desired = Mathf.Clamp01(DesiredAttackDistanceFraction) * enemyData.attackRange;
            // push closer before attacking to avoid edge-of-range stalls
            if (distance > desired)
            {
                Vector3 dirNorm = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
                if (controller != null && controller.enabled)
                {
                    float speed = GetMoveSpeed();
                    if (speed <= 0f) speed = 0f;
                    controller.SimpleMove(dirNorm * speed);
                }
                if (!IsRotationLocked())
                {
                    Vector3 pushLookTarget = new Vector3(target.position.x, transform.position.y, target.position.z);
                    Vector3 pushDirToLook = (pushLookTarget - transform.position);
                    if (pushDirToLook.sqrMagnitude > 0.0001f)
                    {
                        Quaternion targetRot = Quaternion.LookRotation(pushDirToLook);
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, AttackRotationSpeed * Time.deltaTime);
                    }
                }
                aiState = AIState.Chase;
                return NodeState.Running;
            }
            if (!cooldownReady)
            {
                // In range but on cooldown: stand still and wait
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                aiState = AIState.Chase;
                return NodeState.Running;
            }
            // In range and cooldown ready: must face target before attacking (prevents attacking backwards)
            if (!IsFacingTarget(target, SpecialFacingAngle))
            {
                if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
                if (!IsRotationLocked())
                {
                    Vector3 lookTarget = new Vector3(target.position.x, transform.position.y, target.position.z);
                    Vector3 dirToLook = (lookTarget - transform.position);
                    if (dirToLook.sqrMagnitude > 0.0001f)
                    {
                        Quaternion targetRot = Quaternion.LookRotation(dirToLook);
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, AttackRotationSpeed * Time.deltaTime);
                    }
                }
                aiState = AIState.Chase;
                return NodeState.Running;
            }
            aiState = AIState.Chase;
            return NodeState.Failure;
        }
        
        direction.Normalize();
        if (controller != null && controller.enabled)
        {
            float speed = GetMoveSpeed();
            if (speed <= 0f) speed = 0f; // Ensure movement respects busy state
            controller.SimpleMove(direction * speed);
        }
            
        if (!IsRotationLocked())
        {
            Vector3 lookTarget = new Vector3(target.position.x, transform.position.y, target.position.z);
            Vector3 dirToLook = (lookTarget - transform.position);
            if (dirToLook.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dirToLook);
                float rotSpeed = enemyData != null ? enemyData.rotationSpeedDegrees : 360f;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotSpeed * Time.deltaTime);
            }
        }
        
        aiState = AIState.Chase;
        return NodeState.Running;
    }
    
    protected NodeState AttackTarget()
    {
        if (enemyData == null) return NodeState.Failure;
        if (Time.time - lastAttackTime < enemyData.attackCooldown) return NodeState.Running;
        if (isBusy || globalBusyTimer > 0f) return NodeState.Running;
        
        var target = blackboard.Get<Transform>("target");
        if (target == null) return NodeState.Failure;
        
        // Try special abilities first
        if (TrySpecialAbilities())
        {
            return NodeState.Success;
        }
        
        // Fall back to basic attack
        PerformBasicAttack();
        lastAttackTime = Time.time;
        attackLockTimer = enemyData.attackMoveLock;
        
        aiState = AIState.BasicAttack;
        return NodeState.Success;
    }
    
    protected NodeState Patrol()
    {
        if (enemyData == null || !enemyData.enablePatrol) return NodeState.Failure;
        if (isBusy) return NodeState.Running;
        
        // Pick a target point if none or reached
        Vector3 flatPos = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatTarget = new Vector3(patrolTarget.x, 0f, patrolTarget.z);
        
        if (patrolTarget == Vector3.zero || (flatPos - flatTarget).sqrMagnitude < 0.25f)
        {
            // During wait phase, stop movement and set state to Idle
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            
            if (patrolWaitTimer <= 0f)
            {
                patrolTarget = spawnPoint + Random.insideUnitSphere * enemyData.patrolRadius;
                patrolTarget.y = transform.position.y;
                patrolWaitTimer = enemyData.patrolWait;
            }
            else
            {
                patrolWaitTimer -= Time.deltaTime;
                aiState = AIState.Idle; // Set to Idle during wait so speed is 0
                return NodeState.Running;
            }
        }
        
        if (controller != null)
        {
            Vector3 direction = (patrolTarget - transform.position);
            direction.y = 0f;
            if (direction.magnitude > 0.1f)
            {
                direction.Normalize();
                aiState = AIState.Patrol; // Set before GetMoveSpeed so derived classes (e.g. Sigbin) return non-zero when patrolling
                if (controller != null && controller.enabled)
                {
                    float pSpeed = GetMoveSpeed() * 0.75f;
                    if (pSpeed <= 0f) 
                    {
                        pSpeed = 0f; // Ensure patrol respects busy state
                        controller.SimpleMove(Vector3.zero); // Explicitly stop if speed is 0
                    }
                    else
                    {
                        if (PatrolSpeed > 0f) pSpeed = Mathf.Min(pSpeed, PatrolSpeed); // Use patrolSpeed as max if set
                        controller.SimpleMove(direction * pSpeed);
                    }
                }
                Vector3 look = new Vector3(patrolTarget.x, transform.position.y, patrolTarget.z);
                Vector3 lookDir = (look - transform.position);
                if (lookDir.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(lookDir);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, RotationSpeed * Time.deltaTime);
                }
                aiState = AIState.Patrol;
                return NodeState.Running;
            }
            else
            {
                // Reached target, stop movement
                if (controller != null && controller.enabled)
                    controller.SimpleMove(Vector3.zero);
                aiState = AIState.Idle; // Set to Idle when at destination
            }
        }
        return NodeState.Success;
    }
    
    #endregion
    
    #region Damage and Death
    
    public virtual void TakeEnemyDamage(int amount, GameObject source)
    {
        if (isDead) return;
        
        currentHealth -= amount;
        OnEnemyTookDamage?.Invoke(this, amount);
        if (source != null) lastHitSource = source;
        
        if (showDebugInfo)
        {
            Debug.Log($"[{gameObject.name}] Took {amount} damage. Health: {currentHealth}/{enemyData.maxHealth}");
        }
        
        // Trigger hit animation (sync to all clients)
        if (animator != null && HasTrigger(hitTrigger))
        {
            animator.SetTrigger(hitTrigger);
            // Sync animation to other clients
            if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            {
                photonView.RPC("RPC_SetTrigger", RpcTarget.Others, hitTrigger);
            }
        }
        
        // Spawn hit VFX if available
        if (enemyData != null && enemyData.hitVFXPrefab != null)
        {
            SpawnHitVFX();
        }
        
        // Play hit SFX when enemy takes damage (from EnemyData)
        if (enemyData != null && enemyData.hitSFX != null)
            PlaySfx(enemyData.hitSFX, hitSfxVolume);
        
        // Trigger hit knockback effect
        if (knockbackForce > 0f && knockbackDuration > 0f && controller != null && controller.enabled)
        {
            StartKnockback(source);
        }
        
        if (currentHealth <= 0)
        {
            Die();
        }
    }
    
    /// <summary>
    /// Push the enemy away from the hit source when hit (visual feedback, adjustable).
    /// </summary>
    private void StartKnockback(GameObject source)
    {
        if (knockbackCoroutine != null)
            StopCoroutine(knockbackCoroutine);
        knockbackCoroutine = StartCoroutine(CoKnockback(source));
    }

    private IEnumerator CoKnockback(GameObject source)
    {
        if (controller == null || !controller.enabled || source == null)
        {
            knockbackCoroutine = null;
            yield break;
        }

        Vector3 dir = (transform.position - source.transform.position);
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = -transform.forward;
        dir.Normalize();

        float elapsed = 0f;
        float speed = knockbackForce / Mathf.Max(0.01f, knockbackDuration);

        while (elapsed < knockbackDuration && controller != null && controller.enabled)
        {
            float progress = elapsed / knockbackDuration;
            float decay = 1f - progress; // Linear decay for natural feel
            controller.Move(dir * speed * decay * Time.deltaTime);
            elapsed += Time.deltaTime;
            yield return null;
        }

        knockbackCoroutine = null;
    }
    
    [PunRPC]
    public void RPC_EnemyTakeDamage(int amount, int sourceViewId)
    {
        // RPC entry point for network damage
        GameObject source = sourceViewId >= 0 && PhotonView.Find(sourceViewId) != null ? PhotonView.Find(sourceViewId).gameObject : null;
        TakeEnemyDamage(amount, source);
    }
    
    protected virtual void Die()
    {
        if (isDead) return;
        
        isDead = true;
        currentHealth = 0;
        
        if (showDebugInfo)
        {
            Debug.Log($"[{gameObject.name}] Died");
        }
        
        // Trigger death animation (sync to all clients)
        if (animator != null)
        {
            if (HasBool(isDeadBool))
            {
                animator.SetBool(isDeadBool, true);
                // Sync bool to other clients
                if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                {
                    photonView.RPC("RPC_SetBool", RpcTarget.Others, isDeadBool, true);
                }
            }
            if (HasTrigger(dieTrigger))
            {
                animator.SetTrigger(dieTrigger);
                // Sync trigger to other clients
                if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                {
                    photonView.RPC("RPC_SetTrigger", RpcTarget.Others, dieTrigger);
                }
            }
        }
        
        // Spawn death VFX if available
        if (enemyData != null && enemyData.deathVFXPrefab != null)
        {
            SpawnDeathVFX();
        }
        
        // Play death SFX (from EnemyData)
        if (enemyData != null && enemyData.deathSFX != null)
            PlaySfx(enemyData.deathSFX, deathSfxVolume);
        
        // Stop any movement SFX (walk/chase)
        StopMovementSfx();
        
        // Disable movement
        if (controller != null)
            controller.enabled = false;
        
        OnEnemyDied?.Invoke(this);
        
        // Award power steal to killer (MasterClient authority or offline)
        TryAwardPowerToKiller();
        
        // Destroy after delay
        StartCoroutine(DestroyAfterDelay(5f));
    }
    
    /// <summary>
    /// Spawn hit VFX and sync to other clients
    /// </summary>
    private void SpawnHitVFX()
    {
        if (enemyData == null || enemyData.hitVFXPrefab == null) return;
        
        Vector3 spawnPos = transform.position + Vector3.up * 0.5f;
        
        // Only MasterClient spawns and syncs (enemies are MasterClient authority)
        bool isMasterOrOffline = PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode || !PhotonNetwork.IsConnected;
        if (isMasterOrOffline)
        {
            GameObject vfxInstance = Instantiate(enemyData.hitVFXPrefab, spawnPos, Quaternion.identity);
            
            // Sync to other clients
            if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            {
                string prefabPath = GetPrefabResourcePath(enemyData.hitVFXPrefab);
                if (string.IsNullOrEmpty(prefabPath))
                    prefabPath = enemyData.hitVFXPrefab.name;
                
                photonView.RPC("RPC_SpawnVFXAtPosition", RpcTarget.Others, prefabPath, spawnPos, Quaternion.identity, Vector3.one);
            }
        }
    }
    
    /// <summary>
    /// Spawn death VFX and sync to other clients
    /// </summary>
    private void SpawnDeathVFX()
    {
        if (enemyData == null || enemyData.deathVFXPrefab == null) return;
        
        Vector3 spawnPos = transform.position + Vector3.up * 0.5f;
        
        // Only MasterClient spawns and syncs (enemies are MasterClient authority)
        bool isMasterOrOffline = PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode || !PhotonNetwork.IsConnected;
        if (isMasterOrOffline)
        {
            GameObject vfxInstance = Instantiate(enemyData.deathVFXPrefab, spawnPos, Quaternion.identity);
            
            // Sync to other clients
            if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            {
                string prefabPath = GetPrefabResourcePath(enemyData.deathVFXPrefab);
                if (string.IsNullOrEmpty(prefabPath))
                    prefabPath = enemyData.deathVFXPrefab.name;
                
                photonView.RPC("RPC_SpawnVFXAtPosition", RpcTarget.Others, prefabPath, spawnPos, Quaternion.identity, Vector3.one);
            }
        }
    }

    private void TryAwardPowerToKiller()
    {
        if (string.IsNullOrEmpty(powerStealEnemyName))
        {
            // Fallback: try to use EnemyData name if available
            if (enemyData != null && !string.IsNullOrEmpty(enemyData.enemyName))
                powerStealEnemyName = enemyData.enemyName;
        }
        if (string.IsNullOrEmpty(powerStealEnemyName)) return;
        if (lastHitSource == null) return;

        // Only the MasterClient (or offline mode) should trigger the award
        bool online = PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode;
        if (online && !PhotonNetwork.IsMasterClient) return;

        var killerPv = lastHitSource.GetComponent<PhotonView>();
        if (online)
        {
            if (killerPv != null)
            {
                // Send directly to the owning client so it grants locally and syncs to others via PowerStealManager
                killerPv.RPC("RPC_StealPower", killerPv.Owner, powerStealEnemyName, transform.position);
            }
        }
        else
        {
            // Offline/local
            var psm = lastHitSource.GetComponent<PowerStealManager>();
            if (psm != null)
            {
                psm.StealPowerFromEnemy(powerStealEnemyName, transform.position);
            }
        }
    }
    
    private IEnumerator DestroyAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.Destroy(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    #endregion
    
    #region Helper Methods
    
    protected Transform FindNearestPlayer()
    {
        return PlayerRegistry.FindNearest(transform.position);
    }

    protected Transform FindBestTarget()
    {
        if (enemyData == null)
            return FindNearestPlayer();

        float detectionRange = Mathf.Max(0f, enemyData.detectionRange);
        float chaseLoseRange = Mathf.Max(detectionRange, enemyData.chaseLoseRange);
        float detectionRangeSqr = detectionRange * detectionRange;
        float chaseLoseRangeSqr = chaseLoseRange * chaseLoseRange;
        float switchBuffer = Mathf.Max(0f, enemyData.targetSwitchBuffer);
        float currentDistanceSqr = targetPlayer != null ? (transform.position - targetPlayer.position).sqrMagnitude : float.MaxValue;

        Transform closestVisible = null;
        float closestVisibleSqr = float.MaxValue;

        var players = PlayerRegistry.All;
        for (int i = 0; i < players.Count; i++)
        {
            var playerStats = players[i];
            Transform player = playerStats != null ? playerStats.transform : null;
            if (!IsPlayerValid(player)) continue;

            float distanceSqr = (transform.position - player.position).sqrMagnitude;
            bool isCurrentTarget = player == targetPlayer;

            if (distanceSqr > chaseLoseRangeSqr && !isCurrentTarget)
                continue;

            bool withinDetection = distanceSqr <= detectionRangeSqr;
            bool visible = IsTargetVisible(player);

            if (!withinDetection && !isCurrentTarget)
                continue;

            if (visible && distanceSqr < closestVisibleSqr)
            {
                closestVisibleSqr = distanceSqr;
                closestVisible = player;
            }
        }

        if (closestVisible != null)
        {
            if (targetPlayer != null && IsPlayerValid(targetPlayer) && IsTargetVisible(targetPlayer))
            {
                float switchBufferSqr = switchBuffer * switchBuffer;
                if (closestVisible != targetPlayer && closestVisibleSqr + switchBufferSqr >= currentDistanceSqr)
                    return targetPlayer;
            }

            return closestVisible;
        }

        if (targetPlayer != null && IsPlayerValid(targetPlayer))
        {
            float currentDistance = (transform.position - targetPlayer.position).sqrMagnitude;
            if (currentDistance <= chaseLoseRangeSqr)
            {
                bool withinMemory = Time.time - targetLastSeenTime <= AggroMemoryDuration;
                if (!enemyData.requireLineOfSight || withinMemory || IsTargetVisible(targetPlayer))
                    return targetPlayer;
            }
        }

        return null;
    }

    protected bool IsTargetVisible(Transform target)
    {
        if (target == null) return false;
        if (enemyData == null || !enemyData.requireLineOfSight) return true;

        Vector3 origin = transform.position + Vector3.up;
        Vector3 aimPoint = target.position + Vector3.up * 0.5f;
        Vector3 direction = aimPoint - origin;
        float distance = direction.magnitude;
        if (distance <= 0.001f) return true;

        direction /= distance;
        RaycastHit hit;
        if (Physics.Raycast(origin, direction, out hit, distance, LineOfSightLayers))
        {
            Transform hitTransform = hit.transform;
            if (hitTransform == target || hitTransform.IsChildOf(target))
                return true;
            return false;
        }

        return true;
    }
    
    protected bool IsPlayerValid(Transform player)
    {
        if (player == null) return false;
        PlayerStats stats = player.GetComponent<PlayerStats>();
        // Fallback: if PlayerStats does not expose an alive flag, treat presence as valid
        return stats != null;
    }

    /// <summary>Trigger camera shake for nearby players (e.g. from enemy skills). Only affects local player in multiplayer.</summary>
    protected void TriggerCameraShakeForNearbyPlayers(Vector3 position, float radius, float intensity, float duration)
    {
        if (intensity <= 0f || duration <= 0f) return;
        var players = PlayerRegistry.All;
        float rSq = radius * radius;
        for (int i = 0; i < players.Count; i++)
        {
            var ps = players[i];
            if (ps == null) continue;
            float distSq = (ps.transform.position - position).sqrMagnitude;
            if (distSq <= rSq)
                ps.TriggerCameraShake(intensity, duration);
        }
    }

    /// <summary>True if enemy is facing the target within maxAngle degrees. Used to require facing before attacks.</summary>
    protected bool IsFacingTarget(Transform target, float maxAngle)
    {
        if (target == null) return false;
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return false;
        float angle = Vector3.Angle(new Vector3(transform.forward.x, 0f, transform.forward.z).normalized, to.normalized);
        return angle <= Mathf.Clamp(maxAngle, 1f, 180f);
    }

    /// <summary>True when enemy should not rotate (e.g. exhausted). Override for custom logic (e.g. Bakunawa).</summary>
    protected virtual bool IsRotationLocked()
    {
        return animator != null && HasBool("Exhausted") && animator.GetBool("Exhausted");
    }

    protected bool HasFloatParam(string param)
    {
        return animator != null && !string.IsNullOrEmpty(param) && animatorFloatParams.Contains(param);
    }
    
    protected bool HasTrigger(string param)
    {
        return animator != null && !string.IsNullOrEmpty(param) && animatorTriggerParams.Contains(param);
    }
    
    protected bool HasBool(string param)
    {
        return animator != null && !string.IsNullOrEmpty(param) && animatorBoolParams.Contains(param);
    }

    private void CacheAnimatorParameters()
    {
        animatorFloatParams.Clear();
        animatorTriggerParams.Clear();
        animatorBoolParams.Clear();
        if (animator == null) return;

        var parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            switch (p.type)
            {
                case AnimatorControllerParameterType.Float:
                    animatorFloatParams.Add(p.name);
                    break;
                case AnimatorControllerParameterType.Trigger:
                    animatorTriggerParams.Add(p.name);
                    break;
                case AnimatorControllerParameterType.Bool:
                    animatorBoolParams.Add(p.name);
                    break;
            }
        }
    }

    // Helper properties for common EnemyData values
    protected float RotationSpeed => enemyData != null ? enemyData.rotationSpeedDegrees : 540f;
    protected float AttackRotationSpeed => enemyData != null && enemyData.attackRotationSpeedDegrees > 0f ? enemyData.attackRotationSpeedDegrees : 900f;
    protected float SpecialFacingAngle => enemyData != null ? enemyData.specialFacingAngle : 20f;
    protected float PreferredDistance => enemyData != null ? enemyData.preferredDistance : 3.0f;
    protected float BackoffSpeedMultiplier => enemyData != null ? enemyData.backoffSpeedMultiplier : 0.6f;
    protected float PatrolSpeed => enemyData != null ? enemyData.patrolSpeed : 2f;
    protected float DesiredAttackDistanceFraction => enemyData != null ? enemyData.desiredAttackDistanceFraction : 0.85f;
    protected float AggroMemoryDuration => enemyData != null ? Mathf.Max(0f, enemyData.aggroMemoryDuration) : 0f;
    protected LayerMask LineOfSightLayers => enemyData != null ? enemyData.lineOfSightLayers : ~0;

    protected virtual float GetMoveSpeed()
    {
        // Exhausted: full lock - no movement until timer ends
        if (IsRotationLocked()) return 0f;
        // Use chaseSpeed from EnemyData, fallback to moveSpeed if chaseSpeed is 0 or not set
        if (enemyData != null)
        {
            float chaseSpd = enemyData.chaseSpeed;
            return chaseSpd > 0f ? chaseSpd : enemyData.moveSpeed;
        }
        return 3.5f;
    }

    protected void SetBuffVfx(BuffType type, bool enabled, Vector3 localOffset, float scale = 1f)
    {
        if (!enabled)
        {
            if (activeBuffVfx.TryGetValue(type, out var inst) && inst != null)
            {
                Destroy(inst);
                // Sync removal to other clients
                if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                {
                    photonView.RPC("RPC_RemoveBuffVFX", RpcTarget.Others, (int)type);
                }
            }
            activeBuffVfx.Remove(type);
            return;
        }

        if (activeBuffVfx.ContainsKey(type) && activeBuffVfx[type] != null)
        {
            return; // already active
        }

        GameObject prefab = null;
        switch (type)
        {
            case BuffType.Damage: prefab = buffDamageVFXPrefab; break;
            case BuffType.Speed: prefab = buffSpeedVFXPrefab; break;
            case BuffType.Stamina: prefab = buffStaminaVFXPrefab; break;
            case BuffType.Health: prefab = buffHealthVFXPrefab; break;
        }
        if (prefab == null) return;
        
        Vector3 finalOffset = localOffset + buffVfxOffset;
        Vector3 finalScale = Vector3.one * Mathf.Max(0.01f, scale * Mathf.Max(0.01f, buffVfxScale));
        
        // Only MasterClient spawns and syncs (enemies are MasterClient authority)
        bool isMasterOrOffline = PhotonNetwork.IsMasterClient || PhotonNetwork.OfflineMode || !PhotonNetwork.IsConnected;
        if (isMasterOrOffline)
        {
            // Spawn locally
            var go = SpawnVFXLocal(prefab, finalOffset, finalScale, true);
            if (go != null)
            {
                activeBuffVfx[type] = go;
            }
            
            // Sync to other clients
            if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            {
                string prefabPath = GetPrefabResourcePath(prefab);
                if (string.IsNullOrEmpty(prefabPath))
                    prefabPath = prefab.name;
                
                Vector3 worldPos = transform.position + finalOffset;
                Quaternion worldRot = transform.rotation;
                photonView.RPC("RPC_SpawnVFX", RpcTarget.Others, prefabPath, worldPos, worldRot, finalScale, true);
            }
        }
    }
    
    /// <summary>
    /// RPC to remove buff VFX on remote clients
    /// </summary>
    [PunRPC]
    public void RPC_RemoveBuffVFX(int buffTypeInt)
    {
        BuffType type = (BuffType)buffTypeInt;
        if (activeBuffVfx.TryGetValue(type, out var inst) && inst != null)
        {
            Destroy(inst);
        }
        activeBuffVfx.Remove(type);
    }
    
    #endregion
    
    #region Public Properties
    
    public bool IsDead => isDead;
    public int CurrentHealth => currentHealth;
    public int MaxHealth 
    { 
        get 
        {
            if (baseMaxHealth > 0)
            {
                return GetScaledMaxHealth();
            }
            return enemyData != null ? enemyData.maxHealth : 100;
        }
    }
    
    private int GetScaledMaxHealth()
    {
        var scalingType = System.Type.GetType("MultiplayerScalingManager");
        if (scalingType != null)
        {
            var instanceProp = scalingType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (instanceProp != null)
            {
                var instance = instanceProp.GetValue(null);
                if (instance != null)
                {
                    var method = scalingType.GetMethod("GetScaledHealth", new[] { typeof(int) });
                    if (method != null)
                    {
                        return (int)method.Invoke(instance, new object[] { baseMaxHealth });
                    }
                }
            }
        }
        return baseMaxHealth;
    }
    
    private void ApplyMultiplayerScaling()
    {
        int scaledHealth = GetScaledMaxHealth();
        if (scaledHealth != baseMaxHealth)
        {
            float healthPercent = baseMaxHealth > 0 ? (float)currentHealth / baseMaxHealth : 1f;
            currentHealth = Mathf.RoundToInt(scaledHealth * healthPercent);
        }
    }
    public float HealthPercentage => MaxHealth > 0 ? (float)currentHealth / MaxHealth : 0f;
    public Transform Target => targetPlayer;
    public bool IsAttacking => isAttacking;
    public bool IsBusy => isBusy;
    public float BasicCooldownRemaining => Mathf.Max(0f, attackLockTimer);
    public float BasicCooldownTime => enemyData != null ? Mathf.Max(0f, enemyData.attackCooldown - (Time.time - lastAttackTime)) : 0f;
    public AIState CurrentState => aiState;

    /// <summary>Returns the actual state for debug display. Override in derived classes to show skill names (e.g. "Lunge" instead of "Special1").</summary>
    public virtual string GetEffectiveStateForDebug()
    {
        return aiState.ToString();
    }

    // Debug: why movement might be blocked (MoveTowardsTarget returns early)
    public bool DebugIsMoveBlocked => attackLockTimer > 0f || isBusy || globalBusyTimer > 0f;
    public string DebugBlockReason
    {
        get
        {
            if (attackLockTimer > 0f) return "atkLock";
            if (isBusy) return "busy";
            if (globalBusyTimer > 0f) return "globalBusy";
            return "";
        }
    }
    public float DebugVelocityMagnitude => controller != null ? new Vector3(controller.velocity.x, 0f, controller.velocity.z).magnitude : 0f;
    public bool DebugIsExhausted => IsRotationLocked();
    public string DebugDetailString => $"busy:{isBusy} gBusy:{globalBusyTimer:F1}s atkLock:{attackLockTimer:F1}s exhausted:{DebugIsExhausted}";

    #endregion

    #region Network Synchronization RPCs
    
    /// <summary>
    /// RPC to sync animator trigger across network
    /// </summary>
    [PunRPC]
    public void RPC_SetTrigger(string triggerName)
    {
        if (animator != null && HasTrigger(triggerName))
        {
            animator.SetTrigger(triggerName);
        }
    }
    
    /// <summary>
    /// Helper method to trigger animation and sync to network. Use this for attack animations.
    /// </summary>
    protected void SetTriggerSync(string triggerName)
    {
        if (animator != null && HasTrigger(triggerName))
        {
            animator.SetTrigger(triggerName);
            // Sync to other clients
            if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            {
                photonView.RPC("RPC_SetTrigger", RpcTarget.Others, triggerName);
            }
        }
    }
    
    /// <summary>
    /// RPC to sync animator bool across network
    /// </summary>
    [PunRPC]
    public void RPC_SetBool(string boolName, bool value)
    {
        if (animator != null && HasBool(boolName))
        {
            animator.SetBool(boolName, value);
        }
    }
    
    /// <summary>
    /// Helper method to set animator bool and sync to network. Use this for boolean animator changes.
    /// </summary>
    protected void SetBoolSync(string boolName, bool value)
    {
        if (animator != null && HasBool(boolName))
        {
            animator.SetBool(boolName, value);
            // Sync to other clients
            if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            {
                photonView.RPC("RPC_SetBool", RpcTarget.Others, boolName, value);
            }
        }
    }
    
    private int vfxIdCounter = 0;
    
    /// <summary>
    /// Helper method to spawn VFX locally and sync spawn command to other clients.
    /// Uses resource path or prefab name for network synchronization.
    /// Returns the spawned GameObject instance.
    /// </summary>
    protected GameObject SpawnVFXSync(GameObject vfxPrefab, Vector3 localOffset, Vector3 scale, bool parentToTransform = true, string vfxId = null, float destroyAfterSeconds = 0f)
    {
        if (vfxPrefab == null) return null;
        
        // Generate unique ID if not provided
        if (string.IsNullOrEmpty(vfxId))
        {
            vfxId = $"vfx_{photonView.ViewID}_{vfxIdCounter++}_{Time.time}";
        }
        
        // Spawn locally first
        GameObject localInstance = SpawnVFXLocal(vfxPrefab, localOffset, scale, parentToTransform);
        
        // Track instance for scale synchronization
        TrackVFXInstance(vfxId, localInstance);
        
        if (destroyAfterSeconds > 0f)
        {
            var d = localInstance.GetComponent<DestroyAfterSeconds>();
            if (d == null) d = localInstance.AddComponent<DestroyAfterSeconds>();
            d.seconds = destroyAfterSeconds;
        }
        
        // Sync to other clients via RPC
        if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            string prefabPath = GetPrefabResourcePath(vfxPrefab);
            if (string.IsNullOrEmpty(prefabPath))
            {
                // Fallback: try prefab name
                prefabPath = vfxPrefab.name;
            }
            
            Vector3 worldPos = parentToTransform ? transform.position + transform.rotation * localOffset : transform.position + localOffset;
            Quaternion worldRot = parentToTransform ? transform.rotation : Quaternion.identity;
            photonView.RPC("RPC_SpawnVFX", RpcTarget.Others, prefabPath, worldPos, worldRot, scale, parentToTransform, vfxId, destroyAfterSeconds);
        }
        
        return localInstance;
    }
    
    /// <summary>
    /// Spawn VFX locally without network sync
    /// </summary>
    private GameObject SpawnVFXLocal(GameObject vfxPrefab, Vector3 localOffset, Vector3 scale, bool parentToTransform)
    {
        if (vfxPrefab == null) return null;
        
        GameObject vfxInstance;
        if (parentToTransform)
        {
            vfxInstance = Instantiate(vfxPrefab, transform);
            vfxInstance.transform.localPosition = localOffset;
        }
        else
        {
            vfxInstance = Instantiate(vfxPrefab, transform.position + localOffset, transform.rotation);
        }
        if (scale != Vector3.zero && scale != Vector3.one)
        {
            vfxInstance.transform.localScale = scale;
        }
        
        return vfxInstance;
    }
    
    /// <summary>
    /// Get resource path for a prefab, or return null if not in Resources
    /// </summary>
    private string GetPrefabResourcePath(GameObject prefab)
    {
        if (prefab == null) return null;
        
        // Try to find resource path by checking common resource locations
        // Priority: EnemyVFX first (most likely for enemy VFX), then others
        string[] resourcePaths = {
            $"EnemyVFX/{prefab.name}",
            $"VFX/{prefab.name}",
            $"BuffVFX/{prefab.name}",
            prefab.name
        };
        
        foreach (string path in resourcePaths)
        {
            if (Resources.Load<GameObject>(path) != null)
            {
                return path;
            }
        }
        
        // If not found, return the name anyway - RPC will try alternative paths
        return prefab.name;
    }
    
    /// <summary>
    /// RPC to spawn VFX on remote clients
    /// </summary>
    [PunRPC]
    public void RPC_SpawnVFX(string prefabPath, Vector3 position, Quaternion rotation, Vector3 scale, bool parentToTransform, string vfxId = "", float destroyAfterSeconds = 0f)
    {
        // Execute on all remote clients (enemy is owned by MasterClient, so joiners need to spawn VFX)
        // Don't check IsMine - we want all clients except the sender to execute
        // Since we use RpcTarget.Others, the sender won't receive this
        
        GameObject prefab = Resources.Load<GameObject>(prefabPath);
        if (prefab == null)
        {
            // Try alternative paths
            string[] altPaths = {
                $"EnemyVFX/{prefabPath}",
                $"VFX/{prefabPath}",
                $"BuffVFX/{prefabPath}"
            };
            foreach (string altPath in altPaths)
            {
                prefab = Resources.Load<GameObject>(altPath);
                if (prefab != null) break;
            }
        }
        
        if (prefab == null)
        {
            Debug.LogWarning($"[BaseEnemyAI] Could not load VFX prefab from path: {prefabPath}. Tried: {prefabPath}, EnemyVFX/{prefabPath}, VFX/{prefabPath}, BuffVFX/{prefabPath}");
            return;
        }
        
        GameObject vfxInstance;
        if (parentToTransform)
        {
            vfxInstance = Instantiate(prefab, transform);
            vfxInstance.transform.localPosition = transform.InverseTransformPoint(position);
        }
        else
        {
            vfxInstance = Instantiate(prefab, position, rotation);
        }
        
        if (scale != Vector3.zero && scale != Vector3.one)
        {
            vfxInstance.transform.localScale = scale;
        }
        
        // Track instance if ID provided
        if (!string.IsNullOrEmpty(vfxId))
        {
            TrackVFXInstance(vfxId, vfxInstance);
        }
        
        if (destroyAfterSeconds > 0f)
        {
            var d = vfxInstance.GetComponent<DestroyAfterSeconds>();
            if (d == null) d = vfxInstance.AddComponent<DestroyAfterSeconds>();
            d.seconds = destroyAfterSeconds;
        }
    }
    
    /// <summary>
    /// RPC to spawn VFX at world position (for death/hit effects)
    /// </summary>
    [PunRPC]
    public void RPC_SpawnVFXAtPosition(string prefabPath, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        // Execute on all remote clients (enemy is owned by MasterClient, so joiners need to spawn VFX)
        // Don't check IsMine - we want all clients except the sender to execute
        
        GameObject prefab = Resources.Load<GameObject>(prefabPath);
        if (prefab == null)
        {
            // Try alternative paths
            string[] altPaths = {
                $"EnemyVFX/{prefabPath}",
                $"VFX/{prefabPath}",
                $"BuffVFX/{prefabPath}"
            };
            foreach (string altPath in altPaths)
            {
                prefab = Resources.Load<GameObject>(altPath);
                if (prefab != null) break;
            }
        }
        
        if (prefab == null)
        {
            Debug.LogWarning($"[BaseEnemyAI] Could not load VFX prefab from path: {prefabPath}. Tried: {prefabPath}, EnemyVFX/{prefabPath}, VFX/{prefabPath}, BuffVFX/{prefabPath}");
            return;
        }
        
        GameObject vfxInstance = Instantiate(prefab, position, rotation);
        if (scale != Vector3.zero && scale != Vector3.one)
        {
            vfxInstance.transform.localScale = scale;
        }
    }
    
    // Track spawned indicator VFX instances for scale synchronization
    private Dictionary<string, GameObject> trackedVFXInstances = new Dictionary<string, GameObject>();
    
    /// <summary>
    /// Track a VFX instance for scale synchronization
    /// </summary>
    protected void TrackVFXInstance(string id, GameObject instance)
    {
        if (instance != null && !string.IsNullOrEmpty(id))
        {
            trackedVFXInstances[id] = instance;
        }
    }
    
    void OnDestroy()
    {
        // Stop knockback coroutine
        if (knockbackCoroutine != null)
        {
            StopCoroutine(knockbackCoroutine);
            knockbackCoroutine = null;
        }
        if (movementSfxFadeCoroutine != null)
        {
            StopCoroutine(movementSfxFadeCoroutine);
            movementSfxFadeCoroutine = null;
        }
        
        // Stop all coroutines
        StopAllCoroutines();
        
        // Cleanup VFX instances
        if (trackedVFXInstances != null)
        {
            foreach (var vfx in trackedVFXInstances.Values)
            {
                if (vfx != null)
                    Destroy(vfx);
            }
            trackedVFXInstances.Clear();
        }
        
        // Cleanup buff VFX
        if (activeBuffVfx != null)
        {
            foreach (var vfx in activeBuffVfx.Values)
            {
                if (vfx != null)
                    Destroy(vfx);
            }
            activeBuffVfx.Clear();
        }
        
        // Clear event subscriptions
        OnEnemyDied = null;
        OnEnemyTookDamage = null;
    }
    
    /// <summary>
    /// Sync VFX scale change to remote clients
    /// </summary>
    protected void SyncVFXScale(string vfxId, Vector3 scale)
    {
        if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            photonView.RPC("RPC_UpdateVFXScale", RpcTarget.Others, vfxId, scale);
        }
    }
    
    /// <summary>
    /// RPC to update VFX scale on remote clients
    /// </summary>
    [PunRPC]
    public void RPC_UpdateVFXScale(string vfxId, Vector3 scale)
    {
        if (trackedVFXInstances.TryGetValue(vfxId, out GameObject vfx) && vfx != null)
        {
            vfx.transform.localScale = scale;
        }
    }
    
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            SerializeAnimatorStateForPhoton(out float speed, out bool isBusyState, out bool isExhausted);
            stream.SendNext(speed);
            stream.SendNext(isBusyState);
            stream.SendNext(isExhausted);
        }
        else
        {
            float speed = (float)stream.ReceiveNext();
            bool isBusyState = (bool)stream.ReceiveNext();
            bool isExhausted = (bool)stream.ReceiveNext();
            DeserializeAnimatorStateForPhoton(speed, isBusyState, isExhausted);
        }
    }

    /// <summary>
    /// Override to customize animator sync (e.g. Bakunawa uses headAnimator for Exhausted).
    /// </summary>
    protected virtual void SerializeAnimatorStateForPhoton(out float speed, out bool isBusyState, out bool isExhausted)
    {
        speed = 0f;
        isBusyState = false;
        isExhausted = false;
        if (animator != null)
        {
            if (controller != null)
            {
                var v = controller.velocity;
                speed = new Vector3(v.x, 0f, v.z).magnitude;
            }
            if (HasBool("Busy"))
                isBusyState = animator.GetBool("Busy");
            if (HasBool("Exhausted"))
                isExhausted = animator.GetBool("Exhausted");
        }
    }

    /// <summary>
    /// Override to customize animator sync (e.g. Bakunawa applies Exhausted to headAnimator).
    /// </summary>
    protected virtual void DeserializeAnimatorStateForPhoton(float speed, bool isBusyState, bool isExhausted)
    {
        if (animator != null)
        {
            if (HasFloatParam(speedParam))
                animator.SetFloat(speedParam, speed);
            if (HasBool("Busy"))
                animator.SetBool("Busy", isBusyState);
            if (HasBool("Exhausted"))
                animator.SetBool("Exhausted", isExhausted);
        }
    }
    
    #endregion
}

public enum BuffType { Damage, Speed, Stamina, Health }
