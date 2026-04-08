using UnityEngine;
using Photon.Pun;
using System.Collections;
using System;

public class PlayerCombat : MonoBehaviourPun
{
    [Header("Combat Configuration")]
    public float attackCooldown = 1.0f; // seconds
    private float attackCooldownTimer = 0f;
    public float AttackCooldownProgress => Mathf.Clamp01(attackCooldownTimer / attackCooldown);

    public float attackRange = 2f;
    public float attackRate = 1f;
    public int attackStaminaCost = 20;
    public LayerMask enemyLayers;
    [Tooltip("Optional origin for attack range (use this transform's position/forward instead of the player's)")]
    public Transform attackRangeOrigin;
    [Tooltip("Forward offset in meters from the origin to center the attack sphere (use to make range less deep)")]
    public float attackForwardOffset = 0.0f;

    [Header("Combo System")]
    [SerializeField] private float comboWindow = 1.0f; // Time to continue combo after hit (increased for better feel)
    [SerializeField] private float comboInputDelay = 0.2f; // Minimum delay before next hit input can be registered (reduced for responsiveness)
    [SerializeField] private float comboInputBuffer = 0.3f; // Buffer time to accept input before it's valid (for smoother combos)
    [SerializeField] private float[] comboDamageMultipliers = { 1.0f, 1.2f, 1.5f }; // Damage multiplier per hit
    [SerializeField] private bool enableComboInputBuffering = true; // Buffer inputs during attack for smoother combos
    
    [Header("Attack Durations")]
    [SerializeField] private float[] unarmedAttackDurations = { 0.4f, 0.4f }; // Duration of each unarmed attack (2 hits)
    [SerializeField] private float[] armedAttackDurations = { 0.4f, 0.4f, 0.5f }; // Duration of each armed attack (3 hits)
    
    [Header("Hit Stop Effect")]
    [SerializeField] private float hitStopDuration = 0.05f; // Duration of hit-stop effect when hitting enemies
    [SerializeField] private float[] comboHitStopDurations = { 0.05f, 0.08f, 0.12f }; // Hit-stop duration per combo hit (increases with combo)
    
    [Header("Combo VFX/Audio")]
    [Tooltip("VFX prefabs for each combo hit (optional)")]
    [SerializeField] private GameObject[] comboHitVFX = new GameObject[3];
    [Tooltip("Audio clips for each combo hit (optional - deprecated, use PlayerAudioManager instead)")]
    [SerializeField] private AudioClip[] comboHitSounds = new AudioClip[3];
    [Tooltip("Audio clip for combo completion (optional - deprecated, use PlayerAudioManager instead)")]
    [SerializeField] private AudioClip comboCompleteSound;
    
    [Header("Hit Impact VFX")]
    [Tooltip("VFX prefab to spawn when hitting an enemy. Spawns at the hit location on the enemy.")]
    [SerializeField] private GameObject hitImpactVFX;
    [Tooltip("Height offset for hit VFX spawn position (relative to enemy position)")]
    [SerializeField] private float hitVFXHeightOffset = 0.5f;
    [Tooltip("Use raycast to find exact hit point on enemy collider (more accurate but slightly more expensive)")]
    [SerializeField] private bool useRaycastForHitPoint = true;
    
    [Header("Hit Impact SFX")]
    [Tooltip("Generic SFX played when a hit actually connects (enemy or destructible plant). Used as fallback when specific clips are not set.")]
    [SerializeField] private AudioClip hitImpactSfx;
    [SerializeField, Range(0f, 1f)] private float hitImpactSfxVolume = 0.85f;
    [Tooltip("SFX played when an UNARMED hit connects. If set, overrides the generic hit SFX for unarmed attacks.")]
    [SerializeField] private AudioClip unarmedHitImpactSfx;
    [SerializeField, Range(0f, 1f)] private float unarmedHitImpactSfxVolume = 0.85f;
    [Tooltip("SFX played when an ARMED hit connects. If set, overrides the generic hit SFX for armed attacks.")]
    [SerializeField] private AudioClip armedHitImpactSfx;
    [SerializeField, Range(0f, 1f)] private float armedHitImpactSfxVolume = 0.85f;
    private AudioSource audioSource;
    private PlayerAudioManager audioManager;
    
    [Header("Attack Rotation")]
    [SerializeField] private float attackRotationSpeed = 720f; // Degrees per second - fast and snappy rotation towards camera
    
    [Header("VFX Integration")]
    public EffectsManager effectsManager;
    public VFXManager vfxManager; // backwards compat
    public PowerStealManager powerStealManager;
    
    [Header("Managers")]
    public MovesetManager movesetManager;
    private EquipmentManager equipmentManager;
    
    [Header("Camera")]
    public Transform cameraPivot; // Camera pivot transform for camera-relative attacks
    private CameraShake cameraShake;
    
    private float nextAttackTime = 0f;
    private PlayerStats stats;
    private Animator animator;
    private float isAttackingTimer = 0f;
    public bool IsAttacking => isAttackingTimer > 0f;

    // Combo state
    private int currentComboIndex = 0;
    public int CurrentComboIndex => currentComboIndex;
    public int ComboCount => currentComboIndex + 1; // Public accessor for UI
    public bool IsArmed => equipmentManager != null && equipmentManager.equippedItem != null;
    private float comboWindowTimer = 0f;
    private float comboInputDelayTimer = 0f;
    private float comboInputBufferTimer = 0f; // Buffer timer for queued inputs
    private bool isPerformingCombo = false;
    private bool hasBufferedInput = false; // Track if input was buffered
    private Coroutine currentAttackCoroutine = null;
    // Ensures we never trigger attack animation without enough stamina
    private bool hasPaidForCurrentHit = false;
    
    // Events for UI feedback
    public System.Action<int> OnComboHit; // Fired when a combo hit connects
    public System.Action<int> OnComboProgress; // Fired when combo progresses (hitNumber)
    public System.Action OnComboReset; // Fired when combo resets
    public System.Action OnComboComplete; // Fired when full combo completes

    // track last damaged enemy root to attribute kills for power stealing
    public Transform LastHitEnemyRoot { get; private set; }
    
    // Hit-stop state
    private Coroutine hitStopCoroutine = null;
    private ThirdPersonController playerController;
    
    // Store attack start position/direction to keep damage area fixed (not affected by root motion)
    private Vector3 attackStartPosition;
    private Vector3 attackStartForward;

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        animator = GetComponent<Animator>();
        
        // Auto-find moveset manager
        if (movesetManager == null)
            movesetManager = GetComponent<MovesetManager>();
            
        // Auto-find Effects manager (VFX + SFX)
        if (effectsManager == null)
            effectsManager = GetComponent<EffectsManager>();
        if (effectsManager == null)
            effectsManager = GetComponent<VFXManager>();
        if (vfxManager == null)
            vfxManager = GetComponent<VFXManager>();
            
        // Auto-find power steal manager
        if (powerStealManager == null)
            powerStealManager = GetComponent<PowerStealManager>();
            
        // Auto-find equipment manager
        if (equipmentManager == null)
            equipmentManager = GetComponent<EquipmentManager>();
            
        // Get player controller for hit-stop
        playerController = GetComponent<ThirdPersonController>();
        
        // Find camera shake component
        FindCameraShake();
    }
    
    private void FindCameraShake()
    {
        // Try to find CameraShake on camera or camera rig
        if (cameraPivot != null)
        {
            cameraShake = cameraPivot.GetComponentInChildren<CameraShake>();
        }
        
        // Fallback: search in scene
        if (cameraShake == null)
        {
            cameraShake = FindFirstObjectByType<CameraShake>();
        }
        
        // Get or create audio source (fallback if PlayerAudioManager not available)
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        
        // Get PlayerAudioManager for better audio handling
        audioManager = GetComponent<PlayerAudioManager>();
        
        // Auto-find camera pivot from ThirdPersonController
        if (cameraPivot == null && playerController != null)
        {
            var controllerType = playerController.GetType();
            var cameraPivotField = controllerType.GetField("cameraPivot", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (cameraPivotField != null)
            {
                cameraPivot = cameraPivotField.GetValue(playerController) as Transform;
            }
        }
    }
    
    private bool canControl = true;

    public void SetCanControl(bool value)
    {
        canControl = value;
    }

    void Update()
    {
        var photonView = GetComponent<Photon.Pun.PhotonView>();
        if (photonView != null && !photonView.IsMine) return;
        if (!canControl) return;
        if (stats != null && (stats.IsSilenced || stats.IsStunned || stats.IsExhausted)) return;
        
        // Update timers
        if (attackCooldownTimer > 0f)
            attackCooldownTimer -= Time.deltaTime;
        if (isAttackingTimer > 0f)
            isAttackingTimer -= Time.deltaTime;
            
        // Update combo window timer
        if (comboWindowTimer > 0f)
        {
            comboWindowTimer -= Time.deltaTime;
            if (comboWindowTimer <= 0f)
            {
                // Combo window expired, reset combo
                Debug.Log("[Combo] Combo window expired - Combo reset!");
                ResetCombo();
            }
        }
        
        // Update combo input delay timer
        if (comboInputDelayTimer > 0f)
        {
            comboInputDelayTimer -= Time.deltaTime;
        }
        
        // Update combo input buffer timer
        if (comboInputBufferTimer > 0f)
        {
            comboInputBufferTimer -= Time.deltaTime;
        }

        // Set stance bools when combo is active (performing attack or in combo window)
        bool comboActive = comboWindowTimer > 0f || isPerformingCombo;
        if (animator != null)
        {
            if (comboActive)
            {
                if (IsArmed)
                {
                    if (AnimatorHasParameter(animator, "IsArmedStance")) animator.SetBool("IsArmedStance", true);
                    if (AnimatorHasParameter(animator, "IsUnarmedStance")) animator.SetBool("IsUnarmedStance", false);
                }
                else
                {
                    if (AnimatorHasParameter(animator, "IsUnarmedStance")) animator.SetBool("IsUnarmedStance", true);
                    if (AnimatorHasParameter(animator, "IsArmedStance")) animator.SetBool("IsArmedStance", false);
                }
            }
            else
            {
                if (AnimatorHasParameter(animator, "IsUnarmedStance")) animator.SetBool("IsUnarmedStance", false);
                if (AnimatorHasParameter(animator, "IsArmedStance")) animator.SetBool("IsArmedStance", false);
            }
        }

        // Handle attack input
        bool inputPressed = Input.GetMouseButtonDown(0);
        
        // Prevent input spam: only allow ONE buffered input at a time
        if (inputPressed && enableComboInputBuffering)
        {
            if (IsAttacking || isPerformingCombo)
            {
                // Only buffer if we don't already have a buffered input
                // This prevents spam-clicking from queuing multiple attacks
                if (!hasBufferedInput)
                {
                    hasBufferedInput = true;
                    comboInputBufferTimer = comboInputBuffer;
                    Debug.Log("[Combo] Input buffered - waiting for attack to complete");
                }
                else
                {
                    // Already have buffered input, ignore this spam click
                    Debug.Log("[Combo] Input spam ignored - already have buffered input");
                }
            }
        }
        
        // Check if we can process attack (either immediate or buffered)
        // IMPORTANT: Also check if hit-stop is active (hitStopCoroutine != null)
        bool isHitStopActive = hitStopCoroutine != null;
        bool canProcessInput = !isPerformingCombo && !IsAttacking && !isHitStopActive && comboInputDelayTimer <= 0f && Time.time >= nextAttackTime;
        bool hasValidInput = inputPressed || (hasBufferedInput && comboInputBufferTimer > 0f);
        
        if (hasValidInput && canProcessInput)
        {
            // Clear buffered input
            hasBufferedInput = false;
            comboInputBufferTimer = 0f;
            
            var controller = GetComponent<ThirdPersonController>();
            bool groundedOk = controller != null && controller.CanAttack;
            
            if (groundedOk)
            {
                // Check if we're in combo window (continue combo) or starting new combo
                if (comboWindowTimer > 0f && attackCooldownTimer <= 0f)
                {
                    // Continue existing combo
                    int finalStaminaCost = attackStaminaCost;
                    if (stats != null)
                        finalStaminaCost = Mathf.Max(1, attackStaminaCost + stats.staminaCostModifier);
                    if (stats.UseStamina(finalStaminaCost))
                    {
                        hasPaidForCurrentHit = true;
                        ContinueCombo();
                    }
                }
                else if (attackCooldownTimer <= 0f)
                {
                    // Start new combo
                    int finalStaminaCost = attackStaminaCost;
                    if (stats != null)
                        finalStaminaCost = Mathf.Max(1, attackStaminaCost + stats.staminaCostModifier);
                    if (stats.UseStamina(finalStaminaCost))
                    {
                        hasPaidForCurrentHit = true;
                        StartComboAttack();
                    }
                    else
                    {
                        Debug.Log("Not enough stamina to attack!");
                    }
                }
            }
        }
    }

    private int GetMaxComboCount()
    {
        // Check if player is armed
        bool isArmed = equipmentManager != null && equipmentManager.equippedItem != null;
        return isArmed ? 3 : 2;
    }
    
    private void StartComboAttack()
    {
        currentComboIndex = 0;
        bool isArmed = equipmentManager != null && equipmentManager.equippedItem != null;
        int maxCombo = GetMaxComboCount();
        Debug.Log($"[Combo] Starting combo attack - Mode: {(isArmed ? "Armed" : "Unarmed")}, Max Hits: {maxCombo}");
        
        // Fire combo progress event
        OnComboProgress?.Invoke(1);
        
        PerformComboHit();
    }
    
    private void ContinueCombo()
    {
        int maxCombo = GetMaxComboCount();
        
        // Only continue if we haven't reached max combo yet
        if (currentComboIndex >= maxCombo - 1)
        {
            Debug.Log($"[Combo] Cannot continue - combo already at max ({maxCombo} hits). Resetting.");
            ResetCombo();
            StartComboAttack(); // Start a new combo instead
            return;
        }
        
        currentComboIndex++;
        int hitNumber = currentComboIndex + 1;
        Debug.Log($"[Combo] Continuing combo - Hit {hitNumber}/{maxCombo}");
        
        // Fire combo progress event
        OnComboProgress?.Invoke(hitNumber);
        
        PerformComboHit();
    }
    
    private void PerformComboHit()
    {
        if (currentAttackCoroutine != null)
        {
            StopCoroutine(currentAttackCoroutine);
        }
        currentAttackCoroutine = StartCoroutine(CoPerformComboHit());
    }
    
    private IEnumerator CoPerformComboHit()
    {
        isPerformingCombo = true;
        if (!hasPaidForCurrentHit)
        {
            // Safety: never trigger animation if stamina wasn't paid for this swing
            isPerformingCombo = false;
            yield break;
        }
        
        // Set combo index parameter for animator to track which attack in combo
        int hitNumber = currentComboIndex + 1;
        int maxCombo = GetMaxComboCount();
        bool isArmed = equipmentManager != null && equipmentManager.equippedItem != null;
        
        Debug.Log($"[Combo] Performing hit {hitNumber}/{maxCombo} - ComboIndex: {currentComboIndex}, Armed: {isArmed}");
        
        // Rotate player to face camera direction (fast and snappy)
        Quaternion targetRotation = transform.rotation; // Default to current rotation
        if (cameraPivot != null)
        {
            Vector3 cameraForward = cameraPivot.forward;
            cameraForward.y = 0f;
            if (cameraForward.sqrMagnitude > 0.0001f)
            {
                cameraForward.Normalize();
                targetRotation = Quaternion.LookRotation(cameraForward, Vector3.up);
                
                // Fast and snappy rotation towards camera
                float rotationTime = 0f;
                float maxRotationTime = 0.15f; // Max time to complete rotation (very snappy)
                Quaternion startRotation = transform.rotation;
                
                while (rotationTime < maxRotationTime)
                {
                    rotationTime += Time.deltaTime;
                    float t = rotationTime / maxRotationTime;
                    // Use smoothstep for smooth but snappy rotation
                    t = t * t * (3f - 2f * t);
                    
                    transform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
                    
                    // Check if we're close enough to target (early exit for snappiness)
                    float angle = Quaternion.Angle(transform.rotation, targetRotation);
                    if (angle < 2f) break; // Close enough, snap to target
                    
                    yield return null;
                }
                
                // Ensure we're exactly facing the target
                transform.rotation = targetRotation;
            }
        }
        
        // Store attack start position and forward direction for damage detection
        attackStartPosition = transform.position;
        attackStartForward = transform.forward;
        
        // Store locked rotation to maintain camera-facing direction during entire attack
        Quaternion lockedRotation = transform.rotation;
        
        // Set animator parameters for combo system (only after stamina paid)
        if (animator != null)
        {
            // Set combo index (0, 1, 2 for 1st, 2nd, 3rd hit)
            if (AnimatorHasParameter(animator, "ComboIndex"))
                animator.SetInteger("ComboIndex", currentComboIndex);
            
            // Set armed state
            if (AnimatorHasParameter(animator, "IsArmed"))
                animator.SetBool("IsArmed", isArmed);
            
            // Single Attack trigger for all combo hits
            if (AnimatorHasParameter(animator, "Attack"))
                animator.SetTrigger("Attack");
        }
        
        // Get attack duration for this combo hit (use armed or unarmed durations)
        float[] durations = isArmed ? armedAttackDurations : unarmedAttackDurations;
        float attackDuration = (currentComboIndex < durations.Length) 
            ? durations[currentComboIndex] 
            : (durations.Length > 0 ? durations[0] : 0.4f);
        
        // Calculate impact time (when damage should be applied - typically 60-70% through animation)
        float impactTime = attackDuration * 0.65f;
        bool damageApplied = false;
            
        isAttackingTimer = attackDuration;
        nextAttackTime = Time.time + attackDuration;
        
        // Lock rotation during entire attack to prevent weird turning
        float elapsed = 0f;
        while (elapsed < attackDuration)
        {
            elapsed += Time.deltaTime;
            
            // Apply damage at impact time (more responsive feel)
            if (!damageApplied && elapsed >= impactTime)
            {
                ApplyComboDamage();
                damageApplied = true;
                
                // Play combo hit VFX/Audio
                PlayComboHitEffects(currentComboIndex);
            }
            
            // Keep rotation locked to camera-facing direction throughout attack
            transform.rotation = lockedRotation;
            yield return null;
        }
        
        // Ensure damage is applied even if impact time wasn't reached (safety)
        if (!damageApplied)
        {
            ApplyComboDamage();
            PlayComboHitEffects(currentComboIndex);
        }
        
        // Wait for hit-stop to complete before allowing next input
        // This ensures the hit-stop effect doesn't interfere with combo flow
        while (hitStopCoroutine != null)
        {
            yield return null;
        }
        
        // Set input delay timer before allowing next input
        comboInputDelayTimer = comboInputDelay;
        
        // Check if combo is complete
        if (currentComboIndex >= maxCombo - 1)
        {
            Debug.Log($"[Combo] Combo complete! Setting {(isArmed ? "Armed" : "Unarmed")} stance.");
            
            // Fire combo complete event
            OnComboComplete?.Invoke();
            
            // Play combo complete sound (prefer EffectsManager, then PlayerAudioManager)
            if (effectsManager != null)
                effectsManager.PlayComboCompleteSound();
            else if (audioManager != null)
                audioManager.PlayComboCompleteSound();
            else if (comboCompleteSound != null && audioSource != null)
            {
                // Fallback to direct audio source
                audioSource.PlayOneShot(comboCompleteSound);
            }
            
            ResetCombo();
        }
        else
        {
            // Set combo window timer for next hit
            comboWindowTimer = comboWindow;
            Debug.Log($"[Combo] Combo window opened ({comboWindow}s) - Next hit available in {comboInputDelay}s");
        }
        
        // End combo performance only after attack timer fully expires (allows next input after delay)
        isPerformingCombo = false;
        currentAttackCoroutine = null;
        hasPaidForCurrentHit = false; // require payment for next hit
    }
    
    private void ApplyComboDamage()
    {
        // Use stored attack start position and direction to keep damage area fixed in front
        // This prevents the attack area from moving with root motion animations
        Vector3 originPos = attackStartPosition;
        Vector3 originFwd = attackStartForward;
        if (attackRangeOrigin != null)
        {
            originPos = attackRangeOrigin.position;
            originFwd = attackRangeOrigin.forward;
        }
        float radius = attackRange * 0.5f;
        Vector3 damageCenter = originPos + originFwd * (radius + attackForwardOffset);
        Collider[] hitEnemies = Physics.OverlapSphere(damageCenter, radius, enemyLayers);
        
        // Deduplicate targets
        var uniqueEnemies = new System.Collections.Generic.HashSet<GameObject>();
        foreach (var enemy in hitEnemies)
        {
            var dmg = enemy.GetComponentInParent<IEnemyDamageable>();
            var mb = dmg as MonoBehaviour;
            if (mb != null) uniqueEnemies.Add(mb.gameObject);
        }

        // fallback: if layer mask missed an enemy (e.g. collider on a different layer),
        // run a second pass over all colliders and still look for IEnemyDamageable.
        if (uniqueEnemies.Count == 0)
        {
            Collider[] allHits = Physics.OverlapSphere(damageCenter, radius);
            foreach (var enemy in allHits)
            {
                var dmg = enemy.GetComponentInParent<IEnemyDamageable>();
                var mb = dmg as MonoBehaviour;
                if (mb != null) uniqueEnemies.Add(mb.gameObject);
            }
        }

        // Calculate base damage
        int baseDamage = stats.baseDamage;
        if (movesetManager != null && movesetManager.CurrentMoveset != null)
        {
            baseDamage = movesetManager.CurrentMoveset.baseDamage;
        }
        
        // Apply combo damage multiplier
        float multiplier = (currentComboIndex < comboDamageMultipliers.Length) 
            ? comboDamageMultipliers[currentComboIndex] 
            : 1.0f;
        int finalDamage = Mathf.RoundToInt(baseDamage * multiplier);

        Debug.Log($"[Combo] Hit {currentComboIndex + 1} - Base Damage: {baseDamage}, Multiplier: {multiplier}x, Final Damage: {finalDamage}, Targets: {uniqueEnemies.Count}");

        bool hitEnemy = false;
        foreach (var go in uniqueEnemies)
        {
            EnemyDamageRelay.Apply(go, finalDamage, gameObject);
            LastHitEnemyRoot = go.transform;
            hitEnemy = true;
            
            // Spawn hit impact VFX at hit location
            SpawnHitImpactVFX(go, originPos, originFwd);
        }

        if (hitEnemy && photonView != null && photonView.IsMine)
            PlayHitImpactSfx();
        
        // Trigger hit-stop effect if we hit an enemy
        if (hitEnemy && hitStopDuration > 0f && photonView != null && photonView.IsMine)
        {
            // Stop any existing hit-stop (shouldn't happen, but safety check)
            if (hitStopCoroutine != null)
                StopCoroutine(hitStopCoroutine);
            
            // Use combo-specific hit-stop duration if available
            float stopDuration = hitStopDuration;
            if (comboHitStopDurations != null && currentComboIndex < comboHitStopDurations.Length)
            {
                stopDuration = comboHitStopDurations[currentComboIndex];
            }
            
            // Start hit-stop coroutine (will be tracked by hitStopCoroutine)
            hitStopCoroutine = StartCoroutine(CoHitStop(uniqueEnemies, stopDuration));
            
            // Camera shake on hit
            if (cameraShake != null)
                cameraShake.ShakeHitEnemy();
            
            // Fire combo hit event for UI feedback
            OnComboHit?.Invoke(ComboCount);
        }
    }

    private void PlayHitImpactSfx()
    {
        if (audioSource == null) return;

        // choose specific armed/unarmed clip when available, otherwise fall back to generic hit sfx
        bool isArmed = equipmentManager != null && equipmentManager.equippedItem != null;
        AudioClip clipToPlay = hitImpactSfx;
        float volume = hitImpactSfxVolume;

        if (isArmed && armedHitImpactSfx != null)
        {
            clipToPlay = armedHitImpactSfx;
            volume = armedHitImpactSfxVolume;
        }
        else if (!isArmed && unarmedHitImpactSfx != null)
        {
            clipToPlay = unarmedHitImpactSfx;
            volume = unarmedHitImpactSfxVolume;
        }

        if (clipToPlay == null) return;
        audioSource.PlayOneShot(clipToPlay, volume);
    }
    
    private IEnumerator CoHitStop(System.Collections.Generic.HashSet<GameObject> hitEnemies, float duration)
    {
        if (hitEnemies == null || hitEnemies.Count == 0) yield break;
        
        // Store original states
        float playerAnimatorSpeed = 1f;
        if (animator != null)
        {
            playerAnimatorSpeed = animator.speed;
            animator.speed = 0f; // Pause player animation
        }
        
        bool wasPlayerMovable = true;
        if (playerController != null)
        {
            wasPlayerMovable = true; // We'll check this via SetCanMove
            playerController.SetCanMove(false); // Stop player movement
        }
        
        // Pause enemy animations and movement
        System.Collections.Generic.List<BaseEnemyAI> pausedEnemies = new System.Collections.Generic.List<BaseEnemyAI>();
        System.Collections.Generic.List<Animator> enemyAnimators = new System.Collections.Generic.List<Animator>();
        System.Collections.Generic.List<CharacterController> enemyControllers = new System.Collections.Generic.List<CharacterController>();
        
        foreach (var enemyGo in hitEnemies)
        {
            if (enemyGo == null) continue;
            
            // Get enemy AI component
            var enemyAI = enemyGo.GetComponent<BaseEnemyAI>();
            if (enemyAI != null)
            {
                pausedEnemies.Add(enemyAI);
                
                // Pause enemy animator
                if (enemyAI.animator != null)
                {
                    enemyAnimators.Add(enemyAI.animator);
                    enemyAI.animator.speed = 0f;
                }
            }
            else
            {
                // Fallback: try to find animator directly
                var anim = enemyGo.GetComponentInChildren<Animator>();
                if (anim != null)
                {
                    enemyAnimators.Add(anim);
                    anim.speed = 0f;
                }
            }
            
            // Stop enemy movement
            var enemyController = enemyGo.GetComponent<CharacterController>();
            if (enemyController != null && enemyController.enabled)
            {
                enemyControllers.Add(enemyController);
                enemyController.SimpleMove(Vector3.zero);
            }
            else
            {
                // Try to find in children
                var childController = enemyGo.GetComponentInChildren<CharacterController>();
                if (childController != null && childController.enabled)
                {
                    enemyControllers.Add(childController);
                    childController.SimpleMove(Vector3.zero);
                }
            }
        }
        
        // Wait for hit-stop duration while keeping enemies stopped
        float elapsed = 0f;
        while (elapsed < duration)
        {
            // Continuously stop enemy movement during hit-stop
            foreach (var enemyController in enemyControllers)
            {
                if (enemyController != null && enemyController.enabled)
                {
                    enemyController.SimpleMove(Vector3.zero);
                }
            }
            
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        // Restore player state
        if (animator != null)
        {
            animator.speed = playerAnimatorSpeed;
        }
        
        if (playerController != null && wasPlayerMovable)
        {
            playerController.SetCanMove(true);
        }
        
        // Restore enemy animations
        foreach (var enemyAnim in enemyAnimators)
        {
            if (enemyAnim != null)
            {
                enemyAnim.speed = 1f;
            }
        }
        
        // Enemy movement will resume naturally through their AI update
        
        // Clear hit-stop coroutine reference (important for input blocking)
        hitStopCoroutine = null;
    }
    
    private void ResetCombo()
    {
        if (currentComboIndex > 0 || comboWindowTimer > 0f)
        {
            Debug.Log("[Combo] Combo reset");
            OnComboReset?.Invoke();
        }
        currentComboIndex = 0;
        comboWindowTimer = 0f;
        hasBufferedInput = false;
        comboInputBufferTimer = 0f;
        
        // Stop any ongoing attack coroutine
        if (currentAttackCoroutine != null)
        {
            StopCoroutine(currentAttackCoroutine);
            currentAttackCoroutine = null;
        }
        
        // Ensure combo state is cleared
        isPerformingCombo = false;
        
        // Reset animator combo index
        if (animator != null && AnimatorHasParameter(animator, "ComboIndex"))
        {
            animator.SetInteger("ComboIndex", 0);
        }
    }
    
    private void PlayComboHitEffects(int comboIndex)
    {
        // Play VFX — broadcast to all clients
        if (comboHitVFX != null && comboIndex < comboHitVFX.Length && comboHitVFX[comboIndex] != null)
        {
            Vector3 spawnPos = attackRangeOrigin != null ? attackRangeOrigin.position : transform.position;
            spawnPos += transform.forward * (attackRange * 0.5f);

            if (photonView != null && PhotonNetwork.IsConnected)
            {
                photonView.RPC(nameof(RpcSpawnComboHitVFX), RpcTarget.All, comboIndex, spawnPos, transform.rotation);
            }
            else
            {
                SpawnComboHitVFXLocal(comboIndex, spawnPos, transform.rotation);
            }
        }
        
        // Play audio (prefer EffectsManager, then PlayerAudioManager)
        bool isArmed = equipmentManager != null && equipmentManager.equippedItem != null;
        if (effectsManager != null)
            effectsManager.PlayAttackSound(isArmed, comboIndex);
        else if (audioManager != null)
            audioManager.PlayAttackSound(isArmed, comboIndex);
        else if (comboHitSounds != null && comboIndex < comboHitSounds.Length && comboHitSounds[comboIndex] != null && audioSource != null)
        {
            // Fallback to direct audio source
            audioSource.PlayOneShot(comboHitSounds[comboIndex]);
        }
    }
    
    void Attack()
    {
        // Legacy method - ensure stamina check before starting
        int finalStaminaCost = attackStaminaCost;
        if (stats != null)
            finalStaminaCost = Mathf.Max(1, attackStaminaCost + stats.staminaCostModifier);
        if (stats != null && stats.UseStamina(finalStaminaCost))
        {
            hasPaidForCurrentHit = true;
            StartComboAttack();
        }
    }
    
    // power stealing is granted by enemy death logic (PowerDropOnDeath) and quest updates handled there

    private void SpawnHitImpactVFX(GameObject enemy, Vector3 attackOrigin, Vector3 attackDirection)
    {
        if (hitImpactVFX == null) return;
        
        Vector3 hitPosition;
        Vector3 hitNormal;
        
        // Find the hit point on the enemy
        if (useRaycastForHitPoint)
        {
            // Use raycast to find exact hit point on enemy collider
            Vector3 rayOrigin = attackOrigin;
            Vector3 rayDirection = (enemy.transform.position - attackOrigin).normalized;
            
            // Try to find the closest point on the enemy's collider
            Collider enemyCollider = enemy.GetComponent<Collider>();
            if (enemyCollider == null)
                enemyCollider = enemy.GetComponentInChildren<Collider>();
            
            if (enemyCollider != null)
            {
                // Get closest point on collider bounds to attack origin
                Vector3 closestPoint = enemyCollider.ClosestPoint(attackOrigin);
                
                // Raycast from attack origin to closest point to get surface normal
                RaycastHit hit;
                Vector3 rayToClosest = (closestPoint - attackOrigin).normalized;
                float distance = Vector3.Distance(attackOrigin, closestPoint);
                
                if (Physics.Raycast(attackOrigin, rayToClosest, out hit, distance + 0.5f, enemyLayers))
                {
                    if (hit.collider != null && (hit.collider.gameObject == enemy || hit.collider.transform.IsChildOf(enemy.transform)))
                    {
                        hitPosition = hit.point;
                        hitNormal = hit.normal;
                    }
                    else
                    {
                        // Fallback: use closest point with default normal
                        hitPosition = closestPoint;
                        hitNormal = -rayToClosest;
                    }
                }
                else
                {
                    // Fallback: use closest point with default normal
                    hitPosition = closestPoint;
                    hitNormal = -rayToClosest;
                }
            }
            else
            {
                // No collider found, use enemy position with offset
                hitPosition = enemy.transform.position + Vector3.up * hitVFXHeightOffset;
                hitNormal = -attackDirection.normalized;
            }
        }
        else
        {
            // Simple approach: use enemy position with height offset
            hitPosition = enemy.transform.position + Vector3.up * hitVFXHeightOffset;
            hitNormal = -attackDirection.normalized;
        }
        
        // Calculate rotation to face away from impact (or towards player)
        Quaternion hitRotation = Quaternion.LookRotation(hitNormal);
        
        // broadcast to all clients so every player sees the hit vfx
        if (photonView != null && PhotonNetwork.IsConnected)
        {
            photonView.RPC(nameof(RpcSpawnHitImpactVFX), RpcTarget.All, hitPosition, hitRotation);
        }
        else
        {
            SpawnHitImpactVFXLocal(hitPosition, hitRotation);
        }
    }

    [PunRPC]
    private void RpcSpawnHitImpactVFX(Vector3 position, Quaternion rotation)
    {
        SpawnHitImpactVFXLocal(position, rotation);
    }

    private void SpawnHitImpactVFXLocal(Vector3 position, Quaternion rotation)
    {
        if (hitImpactVFX == null) return;

        GameObject vfxInstance = Instantiate(hitImpactVFX, position, rotation);
        if (vfxInstance != null)
        {
            float destroyTime = 2f;
            var ps = vfxInstance.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                destroyTime = ps.main.duration + ps.main.startLifetime.constantMax + 0.5f;
            }
            Destroy(vfxInstance, destroyTime);
        }
    }
    
    [PunRPC]
    private void RpcSpawnComboHitVFX(int comboIndex, Vector3 position, Quaternion rotation)
    {
        SpawnComboHitVFXLocal(comboIndex, position, rotation);
    }

    private void SpawnComboHitVFXLocal(int comboIndex, Vector3 position, Quaternion rotation)
    {
        if (comboHitVFX == null || comboIndex >= comboHitVFX.Length || comboHitVFX[comboIndex] == null) return;
        Instantiate(comboHitVFX[comboIndex], position, rotation);
    }

    private bool AnimatorHasParameter(Animator anim, string paramName)
    {
        if (anim == null) return false;
        foreach (var p in anim.parameters)
        {
            if (p.name == paramName) return true;
        }
        return false;
    }

    void OnDestroy()
    {
        // Stop all coroutines to prevent leaks
        if (currentAttackCoroutine != null)
        {
            StopCoroutine(currentAttackCoroutine);
            currentAttackCoroutine = null;
        }
        if (hitStopCoroutine != null)
        {
            StopCoroutine(hitStopCoroutine);
            hitStopCoroutine = null;
        }
        StopAllCoroutines();
        
        // Clear event subscriptions
        OnComboHit = null;
        OnComboProgress = null;
        OnComboReset = null;
        OnComboComplete = null;
    }
    
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 gPos = transform.position;
        Vector3 gFwd = transform.forward;
        if (attackRangeOrigin != null)
        {
            gPos = attackRangeOrigin.position;
            gFwd = attackRangeOrigin.forward;
        }
        float r = attackRange * 0.5f;
        Gizmos.DrawWireSphere(gPos + gFwd * (r + attackForwardOffset), r);
    }
}
