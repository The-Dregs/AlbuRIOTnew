using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;

public class PlayerStats : MonoBehaviourPun, IPunObservable
{
    [Header("Performance/Debug")]
    [SerializeField] private bool enableDebugLogs = false;
    public int maxHealth = 100;
    public int currentHealth;
    public int maxStamina = 100;
    public int currentStamina;
    public float staminaRegenRate = 10f;
    public int baseDamage = 25;
    public float baseSpeed = 6f;
    public float speedModifier = 0f;
    public int staminaCostModifier = 0;

    // when true, stamina will not regenerate (e.g., while running)
    private bool staminaRegenBlocked = false;
    // delay before stamina starts regenerating after use or when unblocking
    [Header("stamina regen")]
    public float staminaRegenDelay = 1.0f;
    private float staminaRegenDelayTimer = 0f;
    private float staminaRegenAccumulator = 0f;

    // --- health regeneration ---
    [Header("health regen")]
    [Tooltip("enable or disable passive health regeneration")] public bool enableHealthRegen = true;
    [Tooltip("health regenerated per second (use small values for slow regen, e.g., 0.25..2.0)")] public float healthRegenPerSecond = 0.5f;
    [Tooltip("delay after taking damage before regen starts")] public float healthRegenDelay = 5.0f;
    [Tooltip("when true, health regen pauses while bleeding is active")] public bool blockRegenWhileBleeding = true;
    private float healthRegenDelayTimer = 0f;
    private float healthRegenAccumulator = 0f;
    private bool healthRegenWasActive = false; // for debug logs

    // --- exhausted state (stamina-based) ---
    [Header("exhausted state")]
    [Tooltip("Stamina threshold to exit exhausted state (as fraction of max stamina, e.g., 0.125 = 1/8)")] 
    public float exhaustedRecoveryThreshold = 0.125f; // 1/8 = 12.5%
    private bool isExhausted = false;
    
    // --- status effects / debuffs ---
    [Header("status effects")]
    [Tooltip("applies a percent slowdown (0..1) to movement speed")] public float slowPercent = 0f; // cumulative (max wins)
    [Tooltip("when > 0, player cannot move (rooted)")] public float rootRemaining = 0f;
    [Tooltip("when > 0, player cannot use abilities/attacks")] public float silenceRemaining = 0f;
    [Tooltip("when > 0, player cannot move or act")] public float stunRemaining = 0f;
    [Tooltip("damage taken multiplier bonus, e.g., 0.2 => take +20% damage")] public float defenseDownBonus = 0f; // cumulative (max wins)
    [Tooltip("bleed damage per 0.5s tick while active")] public float bleedPerTick = 0f; public float bleedRemaining = 0f; private float bleedAcc = 0f;
    [Tooltip("stamina burn per 0.5s tick while active")] public float staminaBurnPerTick = 0f; public float staminaBurnRemaining = 0f; private float staminaBurnAcc = 0f;

    // --- god mode (debug/cheat) ---
    [Header("god mode")]
    [Tooltip("When enabled, player cannot take damage and has unlimited stamina")]
    public bool godMode = false;
    [Header("god mode buffs")]
    [Tooltip("VFX prefabs for god mode buffs (4=Health, 5=Stamina, 6=Damage, 7=Speed)")]
    public GameObject[] godModeBuffVFX = new GameObject[4];
    [Tooltip("Buff values for god mode keys")]
    public int godModeHealthBonus = 50;
    public int godModeStaminaBonus = 50;
    public int godModeDamageBonus = 25;
    public float godModeSpeedBonus = 2.0f;
    public float godModeBuffDuration = 10f;
    
    private int godModeHealthBuff = 0;
    private int godModeStaminaBuff = 0;
    private int godModeDamageBuff = 0;
    private float godModeSpeedBuff = 0f;
    private System.Collections.IEnumerator[] activeBuffCoroutines = new System.Collections.IEnumerator[4];
    private CharacterController characterController;
    private readonly HashSet<string> animatorParamCache = new HashSet<string>();

    void OnEnable() => PlayerRegistry.Register(this);
    void OnDisable() => PlayerRegistry.Unregister(this);

    void Awake()
    {
        currentHealth = maxHealth;
        currentStamina = maxStamina;
        animator = GetComponent<Animator>();
        controller = GetComponent<ThirdPersonController>();
        characterController = GetComponent<CharacterController>();
        combat = GetComponent<PlayerCombat>();
        inventory = GetComponent<Inventory>();
        effectsManager = GetComponent<EffectsManager>();
        if (effectsManager == null) effectsManager = GetComponent<VFXManager>();
        audioManager = GetComponent<PlayerAudioManager>();
        CacheAnimatorParameters();
        
        // Find camera shake component
        FindCameraShake();
    }

    private void CacheAnimatorParameters()
    {
        animatorParamCache.Clear();
        if (animator == null) return;
        var parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
            animatorParamCache.Add(parameters[i].name);
    }
    
    private void FindCameraShake()
    {
        // Try to find CameraShake on camera or camera rig
        if (controller != null && controller.cameraPivot != null)
        {
            cameraShake = controller.cameraPivot.GetComponentInChildren<CameraShake>();
        }
        
        // Fallback: search in scene
        if (cameraShake == null)
        {
            cameraShake = FindFirstObjectByType<CameraShake>();
        }
    }

    /// <summary>Trigger camera shake for this player (e.g. from nearby enemy skills). Only affects local player.</summary>
    public void TriggerCameraShake(float intensity, float duration)
    {
        if (intensity <= 0f || duration <= 0f) return;
        if (cameraShake == null) FindCameraShake();
        if (cameraShake != null && (photonView == null || photonView.IsMine))
            cameraShake.Shake(intensity, duration);
    }
    
    void Start()
    {
        // Store spawn position after object is positioned
        if (spawnPosition == Vector3.zero)
        {
            spawnPosition = transform.position;
        }
    }

    void Update()
    {
        if (photonView != null && !photonView.IsMine) return;
        TickDebuffs();
        
        if (godMode)
        {
            HandleGodModeBuffs();
        }
        
        // Handle downed state timer (multiplayer only)
        if (isDowned && !isDead)
        {
            downedTimer -= Time.deltaTime;
            if (downedTimer <= 0f)
            {
                // Time ran out, fully die
                FullyDie();
            }

            // Toggle crawling loop based on movement speed while downed
            if (animator != null && AnimatorHasParameter(crawlingBoolName))
            {
                float horizSpeed = 0f;
                var cc = characterController;
                if (cc != null)
                {
                    var v = cc.velocity; v.y = 0f; horizSpeed = v.magnitude;
                }
                bool isCrawling = horizSpeed > 0.05f; // small deadzone
                animator.SetBool(crawlingBoolName, isCrawling);
            }
        }
        
        // Update exhausted state based on stamina (only if not dead/downed)
        if (!isDead && !isDowned)
        {
            UpdateExhaustedState();
        }
        
        // countdown delay timer
        if (staminaRegenDelayTimer > 0f)
        {
            staminaRegenDelayTimer -= Time.deltaTime;
        }
        // regenerate only if not blocked and delay timer elapsed; use accumulator for smooth, framerate-independent regen
        // god mode: keep stamina at max
        if (godMode)
        {
            currentStamina = maxStamina;
        }
        else if (currentStamina < maxStamina && !staminaRegenBlocked && staminaRegenDelayTimer <= 0f)
        {
            staminaRegenAccumulator += staminaRegenRate * Time.deltaTime;
            if (staminaRegenAccumulator >= 1f)
            {
                int toAdd = Mathf.FloorToInt(staminaRegenAccumulator);
                int room = maxStamina - currentStamina;
                int applied = Mathf.Min(toAdd, room);
                currentStamina += applied;
                staminaRegenAccumulator -= applied;
            }
        }

        // health regen timers
        if (healthRegenDelayTimer > 0f)
        {
            healthRegenDelayTimer -= Time.deltaTime;
        }

        // slow, configurable health regeneration (owner only)
        bool canRegenHealth = enableHealthRegen && !isDead && !isDowned && currentHealth < maxHealth && healthRegenDelayTimer <= 0f && (!blockRegenWhileBleeding || bleedRemaining <= 0f);
        if (canRegenHealth && healthRegenPerSecond > 0f)
        {
            healthRegenAccumulator += Mathf.Max(0f, healthRegenPerSecond) * Time.deltaTime;
            if (healthRegenAccumulator >= 1f)
            {
                int toAdd = Mathf.FloorToInt(healthRegenAccumulator);
                int room = maxHealth - currentHealth;
                int applied = Mathf.Min(toAdd, room);
                if (applied > 0)
                {
                    currentHealth += applied;
                    healthRegenAccumulator -= applied;
                    if (!healthRegenWasActive)
                    {
                        if (enableDebugLogs) Debug.Log($"health regen started ({healthRegenPerSecond}/s)");
                        healthRegenWasActive = true;
                    }
                }
            }
        }
        else
        {
            // if regen was active but now blocked/full/dead, log once
            if (healthRegenWasActive)
            {
                if (enableDebugLogs) Debug.Log("health regen paused");
                healthRegenWasActive = false;
            }
        }
    }

    public void TakeDamage(int amount)
    {
        if (isDead || isDowned || isDeathSequenceRunning) return;
        // god mode: ignore all damage
        if (godMode)
        {
            if (enableDebugLogs) Debug.Log("damage ignored: god mode");
            return;
        }
        // invulnerable while rolling/dashing
        var controllerCmp = controller != null ? controller : GetComponent<ThirdPersonController>();
        if (controllerCmp != null && controllerCmp.IsRolling)
        {
            if (enableDebugLogs) Debug.Log("damage ignored: rolling");
            return;
        }
    // defense down increases incoming damage
    float mult = 1f + Mathf.Max(0f, defenseDownBonus);
    int finalAmount = Mathf.RoundToInt(amount * mult);
    currentHealth -= finalAmount;
        if (currentHealth < 0) currentHealth = 0;

        // reset health regen delay when taking damage
        if (finalAmount > 0)
        {
            healthRegenDelayTimer = healthRegenDelay;
            healthRegenAccumulator = 0f;
            // also mark as not currently regenerating for debug
            if (healthRegenWasActive)
            {
                if (enableDebugLogs) Debug.Log($"health regen delayed for {healthRegenDelay:F1}s due to damage");
                healthRegenWasActive = false;
            }
        }

        // always pulse damage overlay for any damage (including killing blow)
        if (finalAmount > 0)
        {
            PulseDamageOverlay(finalAmount);
        }

        // play hit reaction if still alive
        if (currentHealth > 0 && finalAmount > 0)
        {
            PlayHitFX();
            ShowDamageIndicator(finalAmount);
            
            // Play hit sound (prefer EffectsManager, fallback to PlayerAudioManager)
            if (effectsManager != null)
                effectsManager.PlayHitSound();
            else if (audioManager != null)
                audioManager.PlayHitSound();
            
            // Camera shake on hit
            if (cameraShake != null && (photonView == null || photonView.IsMine))
                cameraShake.ShakeGetHit();
        }
        else if (currentHealth <= 0)
        {
            // trigger death
            Die();
        }
    }

    // network entry point for enemy damage; only the owning client applies (for correct damage UI)
    [PunRPC]
    public void RPC_TakeDamage(int amount)
    {
        if (photonView != null && !photonView.IsMine) return;
        TakeDamage(amount);
    }

    public bool UseStamina(int amount)
    {
        // god mode: unlimited stamina, always return true
        if (godMode)
        {
            return true;
        }
        if (currentStamina >= amount)
        {
            currentStamina -= amount;
            // reset regen delay on stamina spend
            staminaRegenDelayTimer = staminaRegenDelay;
            staminaRegenAccumulator = 0f;
            return true;
        }
        return false;
    }

    public void Heal(int amount)
    {
        currentHealth += amount;
        if (currentHealth > maxHealth) currentHealth = maxHealth;
    }

    public void RestoreStamina(int amount)
    {
        currentStamina += amount;
        if (currentStamina > maxStamina) currentStamina = maxStamina;
    }

    // Update exhausted state based on stamina
    private void UpdateExhaustedState()
    {
        if (isDead || isDowned) return; // Can't be exhausted when dead or downed
        
        bool shouldBeExhausted = currentStamina <= 0;
        float recoveryThreshold = maxStamina * exhaustedRecoveryThreshold;
        bool canExitExhausted = currentStamina >= recoveryThreshold;
        
        // Enter exhausted state when stamina hits 0
        if (shouldBeExhausted && !isExhausted)
        {
            isExhausted = true;
            // Update animator
            if (animator != null && AnimatorHasParameter("IsExhausted"))
            {
                animator.SetBool("IsExhausted", true);
            }
            if (enableDebugLogs) Debug.Log("[PlayerStats] Player exhausted (stamina = 0)");
        }
        // Maintain exhausted state and slow while stamina is below threshold
        else if (isExhausted && !canExitExhausted)
        {
            // Maintain exhausted slow (20% speed reduction) while exhausted
            if (slowPercent < 0.2f) slowPercent = 0.2f;
        }
        // Exit exhausted state when stamina recovers to 1/8 threshold
        else if (!shouldBeExhausted && isExhausted && canExitExhausted)
        {
            isExhausted = false;
            // Remove exhausted slow (only if no other slow debuffs)
            if (slowPercent <= 0.2f && rootRemaining <= 0f)
            {
                slowPercent = 0f;
            }
            
            // Update animator
            if (animator != null && AnimatorHasParameter("IsExhausted"))
            {
                animator.SetBool("IsExhausted", false);
            }
            if (enableDebugLogs) Debug.Log($"[PlayerStats] Player recovered from exhaustion (stamina = {currentStamina}, threshold = {recoveryThreshold})");
        }
    }
    
    // debuff ticking and application
    private void TickDebuffs()
    {
        float dt = Time.deltaTime;
        if (rootRemaining > 0f) rootRemaining = Mathf.Max(0f, rootRemaining - dt);
        if (silenceRemaining > 0f) silenceRemaining = Mathf.Max(0f, silenceRemaining - dt);
        if (stunRemaining > 0f) stunRemaining = Mathf.Max(0f, stunRemaining - dt);

        if (bleedRemaining > 0f)
        {
            bleedRemaining = Mathf.Max(0f, bleedRemaining - dt);
            bleedAcc += dt;
            if (bleedAcc >= 0.5f)
            {
                bleedAcc -= 0.5f;
                int dmg = Mathf.RoundToInt(Mathf.Max(0f, bleedPerTick));
                if (dmg > 0) TakeDamage(dmg);
            }
            if (bleedRemaining <= 0f) bleedPerTick = 0f;
        }
        if (staminaBurnRemaining > 0f)
        {
            staminaBurnRemaining = Mathf.Max(0f, staminaBurnRemaining - dt);
            staminaBurnAcc += dt;
            if (staminaBurnAcc >= 0.5f)
            {
                staminaBurnAcc -= 0.5f;
                int burn = Mathf.RoundToInt(Mathf.Max(0f, staminaBurnPerTick));
                if (burn > 0 && !godMode) // god mode: ignore stamina burn
                {
                    currentStamina = Mathf.Max(0, currentStamina - burn);
                    staminaRegenDelayTimer = Mathf.Max(staminaRegenDelayTimer, 0.5f);
                }
            }
            if (staminaBurnRemaining <= 0f) staminaBurnPerTick = 0f;
        }

        // sanitize slow
        if (slowPercent < 0f) slowPercent = 0f;
        if (slowPercent > 0.9f) slowPercent = 0.9f;
    }

    [PunRPC]
    public void RPC_ApplyDebuff(int type, float magnitude, float duration)
    {
        ApplyDebuff(type, magnitude, duration);
    }

    public void ApplyDebuff(int type, float magnitude, float duration)
    {
        var t = (StatusEffectRelay.EffectType)type;
        switch (t)
        {
            case StatusEffectRelay.EffectType.Slow:
                slowPercent = Mathf.Max(slowPercent, Mathf.Clamp01(magnitude));
                StartCoroutine(ClearAfter(duration, () => slowPercent = 0f));
                break;
            case StatusEffectRelay.EffectType.Root:
                rootRemaining = Mathf.Max(rootRemaining, duration);
                break;
            case StatusEffectRelay.EffectType.Silence:
                silenceRemaining = Mathf.Max(silenceRemaining, duration);
                break;
            case StatusEffectRelay.EffectType.Stun:
                stunRemaining = Mathf.Max(stunRemaining, duration);
                break;
            case StatusEffectRelay.EffectType.DefenseDown:
                defenseDownBonus = Mathf.Max(defenseDownBonus, Mathf.Max(0f, magnitude));
                StartCoroutine(ClearAfter(duration, () => defenseDownBonus = 0f));
                break;
            case StatusEffectRelay.EffectType.Bleed:
                bleedPerTick = Mathf.Max(bleedPerTick, magnitude);
                bleedRemaining = Mathf.Max(bleedRemaining, duration);
                bleedAcc = 0f;
                break;
            case StatusEffectRelay.EffectType.StaminaBurn:
                staminaBurnPerTick = Mathf.Max(staminaBurnPerTick, magnitude);
                staminaBurnRemaining = Mathf.Max(staminaBurnRemaining, duration);
                staminaBurnAcc = 0f;
                break;
        }
    }

    private System.Collections.IEnumerator ClearAfter(float seconds, System.Action onClear)
    {
        if (seconds <= 0f) { onClear?.Invoke(); yield break; }
        yield return new WaitForSeconds(seconds);
        onClear?.Invoke();
    }

    public void ApplyEquipment(ItemData item)
    {
        maxHealth += item.healthModifier;
        maxStamina += item.staminaModifier;
        baseDamage += item.damageModifier;
        speedModifier += item.speedModifier;
        staminaCostModifier += item.staminaCostModifier;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        currentStamina = Mathf.Min(currentStamina, maxStamina);
    }

    public void RemoveEquipment(ItemData item)
    {
        maxHealth -= item.healthModifier;
        maxStamina -= item.staminaModifier;
        baseDamage -= item.damageModifier;
        speedModifier -= item.speedModifier;
        staminaCostModifier -= item.staminaCostModifier;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        currentStamina = Mathf.Min(currentStamina, maxStamina);
    }

    // external controllers can toggle stamina regen based on movement state
    public void SetStaminaRegenBlocked(bool blocked)
    {
        if (staminaRegenBlocked == blocked) return;
        // when unblocking, start delay so regen doesn't kick in immediately
        if (staminaRegenBlocked && !blocked)
        {
            staminaRegenDelayTimer = staminaRegenDelay;
        }
        staminaRegenBlocked = blocked;
        if (blocked)
        {
            // clear accumulator so regen resumes cleanly later
            staminaRegenAccumulator = 0f;
        }
    }

    // ui/debug helpers
    public bool IsStaminaRegenBlocked => staminaRegenBlocked;
    public bool IsStaminaRegenerating => currentStamina < maxStamina && !staminaRegenBlocked && staminaRegenDelayTimer <= 0f;
    public float StaminaRegenDelayRemaining => Mathf.Max(0f, staminaRegenDelayTimer);

    // health regen status
    public bool IsHealthRegenerating => enableHealthRegen && !isDead && !isDowned && currentHealth < maxHealth && healthRegenDelayTimer <= 0f && (!blockRegenWhileBleeding || bleedRemaining <= 0f);
    public float HealthRegenDelayRemaining => Mathf.Max(0f, healthRegenDelayTimer);

    // --- hit / death animation integration ---
    [Header("hit/death animation")]
    public string hitTriggerName = "Hit";
    public string deathTriggerName = "Die";
    public string crawlDeathTriggerName = "DieCrawl"; // alternate death when downed/crawling
    public string respawnTriggerName = "Respawn";     // single-player respawn animation
    public string isDeadBoolName = "IsDead";
    [Header("downed/crawl animation")]
    public string downTriggerName = "Down";         // stand -> down transition
    public string getUpTriggerName = "GetUp";       // revive transition
    public string crawlingBoolName = "IsCrawling";  // loop while moving when downed

    private Animator animator;
    private ThirdPersonController controller;
    private PlayerCombat combat;
    private Inventory inventory;
    private bool isDead = false;
    private bool isDowned = false;
    private bool isDeathSequenceRunning = false; // prevents multiple death triggers while dying
    private float downedTimer = 0f;
    private const float DOWNTIME_DURATION = 30f;
    private Vector3 spawnPosition = Vector3.zero;
    
    public bool IsDead => isDead;
    public bool IsDowned => isDowned;
    public float DownedTimeRemaining => Mathf.Max(0f, downedTimer);

    private bool AnimatorHasParameter(string name)
    {
        return animator != null && !string.IsNullOrEmpty(name) && animatorParamCache.Contains(name);
    }

    private void PlayHitFX()
    {
        if (photonView != null && (Photon.Pun.PhotonNetwork.IsConnected || Photon.Pun.PhotonNetwork.OfflineMode))
        {
            photonView.RPC(nameof(RPC_PlayHit), RpcTarget.All);
        }
        else
        {
            // not connected: play locally
            RPC_PlayHit();
        }
    }

    private void Die()
    {
        if (isDead || isDowned || isDeathSequenceRunning) return;
        isDeathSequenceRunning = true;
        
        bool isMultiplayer = photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode;
        
        if (isMultiplayer)
        {
            // Multiplayer: Go to downed state
            SetDowned();
        }
        else
        {
            // Singleplayer: play death anim, fade to black, then respawn
            StartCoroutine(CoSingleplayerDeathSequence());
        }
    }

    // singleplayer death flow: wait for anim, fade out, respawn, fade in
    private System.Collections.IEnumerator CoSingleplayerDeathSequence()
    {
        // play death locally
        RPC_PlayDeath();
        
        // small wait to allow animation to play
        float animHold = 1.0f; // tweakable if needed
        yield return new WaitForSeconds(animHold);
        
        // fade to black
        var fader = ScreenFader.Instance;
        if (fader == null)
        {
            // create one if none exists
            var go = new GameObject("ScreenFader_Auto");
            fader = go.AddComponent<ScreenFader>();
        }
        // faster fade out, then hold FULL BLACK for exactly 1.0s (not counting fade time)
        float fullBlackHoldSeconds = 0.4f; // precise full-black duration after fade reaches 100%
        float fadeOutDuration = 0.2f;      // time to reach full black
        fader.FadeOut(fadeOutDuration);
        yield return new WaitForSeconds(fadeOutDuration);
        yield return new WaitForSeconds(fullBlackHoldSeconds);

        // respawn and reset
        RespawnAtSpawnPoint();

        // ensure fully alive and controllable after respawn
        isDead = false;
        isDowned = false;
        if (animator != null && AnimatorHasParameter(isDeadBoolName)) animator.SetBool(isDeadBoolName, false);
        // trigger respawn animation (plays under black, then visible as we fade in)
        if (animator != null && AnimatorHasParameter(respawnTriggerName)) animator.SetTrigger(respawnTriggerName);
        // Lock movement for 2 seconds after spawn to let respawn anim settle
        yield return StartCoroutine(CoSpawnMotionLock(3f));
        if (combat != null)
        {
            combat.SetCanControl(true);
        }
        SetStaminaRegenBlocked(false);

        // immediately fade in while respawn anim plays (player remains input-locked)
        fader.FadeIn(0.8f);
        isDeathSequenceRunning = false; // allow future deaths
    }

    private System.Collections.IEnumerator CoSpawnMotionLock(float seconds)
    {
        // Disable player inputs/movement/attacks but keep camera control ON
        ThirdPersonCameraOrbit cam = null;
        if (controller != null && controller.cameraPivot != null)
            cam = controller.cameraPivot.GetComponent<ThirdPersonCameraOrbit>();

        if (cam != null) cam.SetCameraControlActive(true);
        if (controller != null)
        {
            controller.SetCanControl(false);
            controller.SetCanMove(false);
        }
        if (combat != null) combat.SetCanControl(false);

        float end = Time.time + Mathf.Max(0f, seconds);
        while (Time.time < end)
        {
            // ensure camera stays active during lock
            if (cam != null && !cam.cameraControlActive) cam.SetCameraControlActive(true);
            yield return null;
        }

        if (controller != null)
        {
            controller.SetCanControl(true);
            controller.SetCanMove(true);
        }
        if (combat != null) combat.SetCanControl(true);
    }
    
    private void SetDowned()
    {
        isDowned = true;
        isDeathSequenceRunning = false; // entering downed; allow future flow
        downedTimer = DOWNTIME_DURATION;
        currentHealth = 0;
        
        // Sync downed state to all clients
        if (photonView != null)
        {
            photonView.RPC(nameof(RPC_SetDowned), RpcTarget.All);
        }
        else
        {
            // Offline: apply locally
            RPC_SetDowned();
        }
        
        // Clear debuffs
        slowPercent = 0f; rootRemaining = 0f; silenceRemaining = 0f; stunRemaining = 0f; defenseDownBonus = 0f; 
        bleedPerTick = 0f; bleedRemaining = 0f; staminaBurnPerTick = 0f; staminaBurnRemaining = 0f;
    }
    
    private void FullyDie()
    {
        if (isDead) return;
        isDead = true;
        isDowned = false;
        
        // Hide downed overlay when fully dying
        if (photonView == null || photonView.IsMine)
        {
            HideDownedOverlay();
        }
        
        if (photonView != null && (Photon.Pun.PhotonNetwork.IsConnected || Photon.Pun.PhotonNetwork.OfflineMode))
        {
            if (isDowned)
                photonView.RPC(nameof(RPC_PlayCrawlDeath), RpcTarget.All);
            else
                photonView.RPC(nameof(RPC_PlayDeath), RpcTarget.All);
        }
        else
        {
            if (isDowned) RPC_PlayCrawlDeath(); else RPC_PlayDeath();
        }
    }
    
    private void RespawnAtSpawnPoint()
    {
        // Lose some random items (20-30% of inventory)
        if (inventory != null)
        {
            int itemsToLose = Random.Range(2, 4); // Lose 2-3 random items
            List<int> slotsToClear = new List<int>();
            
            // Collect non-empty slots
            for (int i = 0; i < Inventory.SLOT_COUNT; i++)
            {
                var slot = inventory.GetSlot(i);
                if (slot != null && !slot.IsEmpty)
                {
                    slotsToClear.Add(i);
                }
            }
            
            // Randomly remove items
            for (int i = 0; i < itemsToLose && slotsToClear.Count > 0; i++)
            {
                int randomIndex = Random.Range(0, slotsToClear.Count);
                int slotIndex = slotsToClear[randomIndex];
                slotsToClear.RemoveAt(randomIndex);
                
                var slot = inventory.GetSlot(slotIndex);
                if (slot != null)
                {
                    // Remove half the quantity (rounded up)
                    int quantityToRemove = Mathf.Max(1, (slot.quantity + 1) / 2);
                    inventory.RemoveItem(slot.item, quantityToRemove);
                }
            }
        }
        
        // Respawn at spawn point
        if (photonView != null && photonView.IsMine)
        {
            // Teleport player
            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
                transform.position = spawnPosition;
                cc.enabled = true;
            }
            else
            {
                transform.position = spawnPosition;
            }
            
            // Reset health and stamina
            currentHealth = maxHealth;
            currentStamina = maxStamina;
            
            // Clear debuffs
            slowPercent = 0f; rootRemaining = 0f; silenceRemaining = 0f; stunRemaining = 0f; defenseDownBonus = 0f;
            bleedPerTick = 0f; bleedRemaining = 0f; staminaBurnPerTick = 0f; staminaBurnRemaining = 0f;
        }
    }
    
    [PunRPC]
    private void RPC_SetDowned()
    {
        isDowned = true;
        downedTimer = DOWNTIME_DURATION;
        
        // Animation flags for downed state: stand -> down
        if (animator != null)
        {
            if (AnimatorHasParameter(downTriggerName)) animator.SetTrigger(downTriggerName);
            // ensure crawling is off until there is movement
            if (AnimatorHasParameter(crawlingBoolName)) animator.SetBool(crawlingBoolName, false);
        }
        
        // Disable control for local player
        if (photonView == null || photonView.IsMine)
        {
            if (controller != null)
            {
                controller.SetCanControl(false);
                controller.SetCanMove(false);
            }
            if (combat != null)
            {
                combat.SetCanControl(false);
            }
            SetStaminaRegenBlocked(true);
            
            // Show downed overlay
            ShowDownedOverlay();
        }
    }
    
    private DownedOverlayUI cachedDownedOverlay;
    
    private void ShowDownedOverlay()
    {
        if (photonView != null && !photonView.IsMine) return;
        if (cachedDownedOverlay == null)
            cachedDownedOverlay = FindFirstObjectByType<DownedOverlayUI>();
        if (cachedDownedOverlay != null)
        {
            // Overlay will handle showing itself in Update()
        }
    }
    
    [PunRPC]
    public void RPC_Revive()
    {
        if (!isDowned || isDead) return;
        
        isDowned = false;
        downedTimer = 0f;
        currentHealth = maxHealth / 2; // Revive with half health
        currentStamina = maxStamina / 2; // Revive with half stamina
        
        // Re-enable control
        if (photonView == null || photonView.IsMine)
        {
            if (controller != null)
            {
                controller.SetCanControl(true);
                controller.SetCanMove(true);
            }
            if (combat != null)
            {
                combat.SetCanControl(true);
            }
            SetStaminaRegenBlocked(false);
        }
        
        // Reset animation flags: play get-up transition
        if (animator != null)
        {
            if (AnimatorHasParameter(getUpTriggerName)) animator.SetTrigger(getUpTriggerName);
            if (AnimatorHasParameter(isDeadBoolName)) animator.SetBool(isDeadBoolName, false);
            if (AnimatorHasParameter(crawlingBoolName)) animator.SetBool(crawlingBoolName, false);
        }
        
        // Sync revive to all clients
        if (photonView != null)
        {
            photonView.RPC(nameof(RPC_OnRevived), RpcTarget.All);
        }
    }
    
    [PunRPC]
    private void RPC_OnRevived()
    {
        isDowned = false;
        downedTimer = 0f;
        
        // Re-enable control for local player
        if (photonView == null || photonView.IsMine)
        {
            if (controller != null)
            {
                controller.SetCanControl(true);
                controller.SetCanMove(true);
            }
            if (combat != null)
            {
                combat.SetCanControl(true);
            }
            SetStaminaRegenBlocked(false);
            
            // Hide downed overlay
            HideDownedOverlay();
        }
        
        // Reset animation flags: play get-up transition
        if (animator != null)
        {
            if (AnimatorHasParameter(getUpTriggerName)) animator.SetTrigger(getUpTriggerName);
            if (AnimatorHasParameter(isDeadBoolName)) animator.SetBool(isDeadBoolName, false);
            if (AnimatorHasParameter(crawlingBoolName)) animator.SetBool(crawlingBoolName, false);
        }
    }
    
    private void HideDownedOverlay()
    {
        if (photonView != null && !photonView.IsMine) return;
        if (cachedDownedOverlay == null)
            cachedDownedOverlay = FindFirstObjectByType<DownedOverlayUI>();
        if (cachedDownedOverlay != null)
        {
            // Overlay will handle hiding itself in Update()
        }
    }
    
    void OnDestroy()
    {
        // Stop all coroutines
        StopAllCoroutines();
        
        // Clear cached references
        cachedDamageOverlay = null;
        cachedDownedOverlay = null;
    }

    [PunRPC]
    private void RPC_PlayHit()
    {
        if (animator != null && AnimatorHasParameter(hitTriggerName))
        {
            animator.SetTrigger(hitTriggerName);
        }
    }

    [PunRPC]
    private void RPC_PlayDeath()
    {
        if (effectsManager != null)
            effectsManager.PlayDeathSound();
        // animation flags
        if (animator != null)
        {
            if (AnimatorHasParameter(isDeadBoolName)) animator.SetBool(isDeadBoolName, true);
            if (AnimatorHasParameter(deathTriggerName)) animator.SetTrigger(deathTriggerName);
        }
        // disable control for local player so they stop moving/attacking
        if (photonView == null || photonView.IsMine)
        {
            if (controller != null)
            {
                controller.SetCanControl(false);
                controller.SetCanMove(false);
            }
            if (combat != null)
            {
                combat.SetCanControl(false);
            }
            // block stamina regen and spending
            SetStaminaRegenBlocked(true);
        }
    }

    [PunRPC]
    private void RPC_PlayCrawlDeath()
    {
        if (effectsManager != null)
            effectsManager.PlayDeathSound();
        // animation flags for crawl-specific death
        if (animator != null)
        {
            if (AnimatorHasParameter(isDeadBoolName)) animator.SetBool(isDeadBoolName, true);
            if (AnimatorHasParameter(crawlDeathTriggerName)) animator.SetTrigger(crawlDeathTriggerName);
        }
        // disable control for local player so they stop moving/attacking
        if (photonView == null || photonView.IsMine)
        {
            if (controller != null)
            {
                controller.SetCanControl(false);
                controller.SetCanMove(false);
            }
            if (combat != null)
            {
                combat.SetCanControl(false);
            }
            // block stamina regen and spending
            SetStaminaRegenBlocked(true);
        }
    }

    [Header("ui damage indicator")]
    [Tooltip("Enable damage text floating above player when taking damage")]
    public bool enableDamageText = false;
    public GameObject damageTextPrefab; // optional, shows numbers like enemies
    public Transform damageTextSpawnPoint;
    
    [Header("Audio")]
    private EffectsManager effectsManager;
    private PlayerAudioManager audioManager;
    private CameraShake cameraShake;

    private void ShowDamageIndicator(int amount)
    {
        if (enableDebugLogs) Debug.Log($"[PlayerStats] ShowDamageIndicator called with amount={amount}, enableDamageText={enableDamageText}, prefab={damageTextPrefab}");
        if (!enableDamageText) 
        {
            if (enableDebugLogs) Debug.Log("[PlayerStats] Damage text disabled, returning");
            return;
        }
        if (damageTextPrefab == null) 
        {
            Debug.LogWarning("[PlayerStats] Damage text prefab is null!");
            return;
        }
        Vector3 spawnPos = damageTextSpawnPoint != null ? damageTextSpawnPoint.position : transform.position + Vector3.up * 2f;
        var go = Instantiate(damageTextPrefab, spawnPos, Quaternion.identity);
        var dmg = go.GetComponent<DamageText>();
        if (enableDebugLogs) Debug.Log($"[PlayerStats] Instantiated prefab, DamageText component: {dmg}");
        if (dmg != null) dmg.ShowDamage(amount);
        else Debug.LogError("[PlayerStats] DamageText component not found on instantiated prefab!");
    }

    [Header("Damage Overlay (optional)")]
    [Tooltip("Assign directly for reliable damage flash. If unset, will auto-search.")]
    public DamageOverlayUI damageOverlayUI;
    private DamageOverlayUI cachedDamageOverlay;
    
    private void PulseDamageOverlay(int amount)
    {
        if (photonView != null && !photonView.IsMine) return;

        DamageOverlayUI overlay = ResolveDamageOverlay();
        if (overlay != null)
        {
            float proportion = maxHealth > 0 ? (float)amount / (float)maxHealth : 0.2f;
            overlay.Pulse(Mathf.Clamp01(proportion * 3f));
        }
        else if (enableDebugLogs)
        {
            Debug.LogWarning("[PlayerStats] PulseDamageOverlay: no DamageOverlayUI found. Assign damageOverlayUI in Inspector for reliability.");
        }
    }

    private DamageOverlayUI ResolveDamageOverlay()
    {
        // 1. Direct assignment (most reliable)
        if (damageOverlayUI != null) return damageOverlayUI;

        // 2. Validate cache (cleared when destroyed)
        if (cachedDamageOverlay != null) return cachedDamageOverlay;

        // 3. Player's own hierarchy (includes Camera/Canvas)
        cachedDamageOverlay = GetComponentInChildren<DamageOverlayUI>(true);

        // 4. Camera pivot (HUD often lives here)
        if (cachedDamageOverlay == null && controller != null && controller.cameraPivot != null)
            cachedDamageOverlay = controller.cameraPivot.GetComponentInChildren<DamageOverlayUI>(true);

        // 5. Root transform (covers nested prefab structures)
        if (cachedDamageOverlay == null && transform.root != null)
            cachedDamageOverlay = transform.root.GetComponentInChildren<DamageOverlayUI>(true);

        // 6. Scene-wide — prefer overlay belonging to local player
        if (cachedDamageOverlay == null)
        {
            var all = FindObjectsByType<DamageOverlayUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (all != null && all.Length > 0)
            {
                foreach (var d in all)
                {
                    var pv = d.GetComponentInParent<PhotonView>();
                    if (pv == null || pv.IsMine)
                    {
                        cachedDamageOverlay = d;
                        break;
                    }
                }
                if (cachedDamageOverlay == null)
                    cachedDamageOverlay = all[0];
            }
        }

        return cachedDamageOverlay;
    }

    // convenience accessors for controllers
    public bool IsExhausted => isExhausted;
    public bool IsStunned => stunRemaining > 0f;
    public bool IsRooted => rootRemaining > 0f || IsStunned;
    public bool IsSilenced => silenceRemaining > 0f || IsStunned;

    // network sync for essential stat values
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(currentHealth);
            stream.SendNext(maxHealth);
            stream.SendNext(currentStamina);
            stream.SendNext(maxStamina);
        }
        else
        {
            currentHealth = (int)stream.ReceiveNext();
            maxHealth = (int)stream.ReceiveNext();
            currentStamina = (int)stream.ReceiveNext();
            maxStamina = (int)stream.ReceiveNext();
        }
    }
    
    private void HandleGodModeBuffs()
    {
        if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            ApplyGodModeBuff(0, godModeHealthBonus, 0, 0, 0f);
        }
        if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            ApplyGodModeBuff(1, 0, godModeStaminaBonus, 0, 0f);
        }
        if (Input.GetKeyDown(KeyCode.Alpha6))
        {
            ApplyGodModeBuff(2, 0, 0, godModeDamageBonus, 0f);
        }
        if (Input.GetKeyDown(KeyCode.Alpha7))
        {
            ApplyGodModeBuff(3, 0, 0, 0, godModeSpeedBonus);
        }
    }
    
    private void ApplyGodModeBuff(int buffIndex, int healthBonus, int staminaBonus, int damageBonus, float speedBonus)
    {
        if (activeBuffCoroutines[buffIndex] != null)
        {
            StopCoroutine(activeBuffCoroutines[buffIndex]);
        }
        
        activeBuffCoroutines[buffIndex] = ApplyGodModeBuffCoroutine(buffIndex, healthBonus, staminaBonus, damageBonus, speedBonus);
        StartCoroutine(activeBuffCoroutines[buffIndex]);
    }
    
    private System.Collections.IEnumerator ApplyGodModeBuffCoroutine(int buffIndex, int healthBonus, int staminaBonus, int damageBonus, float speedBonus)
    {
        if (healthBonus > 0)
        {
            maxHealth += healthBonus;
            godModeHealthBuff += healthBonus;
            currentHealth = Mathf.Min(currentHealth + healthBonus, maxHealth);
        }
        if (staminaBonus > 0)
        {
            maxStamina += staminaBonus;
            godModeStaminaBuff += staminaBonus;
            currentStamina = Mathf.Min(currentStamina + staminaBonus, maxStamina);
        }
        if (damageBonus > 0)
        {
            baseDamage += damageBonus;
            godModeDamageBuff += damageBonus;
        }
        if (speedBonus > 0f)
        {
            speedModifier += speedBonus;
            godModeSpeedBuff += speedBonus;
        }
        
        SpawnGodModeBuffVFX(buffIndex);
        if (enableDebugLogs) Debug.Log($"[PlayerStats] God mode buff {buffIndex} applied for {godModeBuffDuration}s");
        
        yield return new WaitForSeconds(godModeBuffDuration);
        
        if (healthBonus > 0)
        {
            maxHealth -= healthBonus;
            godModeHealthBuff -= healthBonus;
            currentHealth = Mathf.Min(currentHealth, maxHealth);
        }
        if (staminaBonus > 0)
        {
            maxStamina -= staminaBonus;
            godModeStaminaBuff -= staminaBonus;
            currentStamina = Mathf.Min(currentStamina, maxStamina);
        }
        if (damageBonus > 0)
        {
            baseDamage -= damageBonus;
            godModeDamageBuff -= damageBonus;
        }
        if (speedBonus > 0f)
        {
            speedModifier -= speedBonus;
            godModeSpeedBuff -= speedBonus;
        }
        
        activeBuffCoroutines[buffIndex] = null;
        if (enableDebugLogs) Debug.Log($"[PlayerStats] God mode buff {buffIndex} expired");
    }
    
    private void SpawnGodModeBuffVFX(int buffIndex)
    {
        if (buffIndex < 0 || buffIndex >= godModeBuffVFX.Length || godModeBuffVFX[buffIndex] == null) return;
        
        Vector3 spawnPos = transform.position + Vector3.up * 1.5f;
        GameObject vfx = Instantiate(godModeBuffVFX[buffIndex], spawnPos, Quaternion.identity);
        
        if (vfx != null)
        {
            vfx.transform.SetParent(transform);
            vfx.transform.localPosition = Vector3.up * 1.5f;
            
            ParticleSystem ps = vfx.GetComponent<ParticleSystem>();
            if (ps != null && !ps.main.loop)
            {
                Destroy(vfx, ps.main.duration + ps.main.startLifetime.constantMax);
            }
            else
            {
                Destroy(vfx, godModeBuffDuration);
            }
            
            if (photonView != null && photonView.IsMine)
            {
                string prefabPath = GetBuffVFXResourcePath(godModeBuffVFX[buffIndex]);
                if (!string.IsNullOrEmpty(prefabPath))
                {
                    photonView.RPC("RPC_SpawnGodModeBuffVFX", RpcTarget.Others, prefabPath, transform.position, buffIndex);
                }
            }
        }
    }
    
    private string GetBuffVFXResourcePath(GameObject prefab)
    {
        if (prefab == null) return null;
        
        string[] paths = { $"BuffVFX/{prefab.name}", $"VFX/{prefab.name}", prefab.name };
        foreach (string path in paths)
        {
            if (Resources.Load<GameObject>(path) != null) return path;
        }
        return prefab.name;
    }
    
    [PunRPC]
    private void RPC_SpawnGodModeBuffVFX(string prefabPath, Vector3 position, int buffIndex)
    {
        GameObject prefab = Resources.Load<GameObject>(prefabPath);
        if (prefab == null)
        {
            string[] altPaths = { $"BuffVFX/{prefabPath}", $"VFX/{prefabPath}", prefabPath };
            foreach (string altPath in altPaths)
            {
                prefab = Resources.Load<GameObject>(altPath);
                if (prefab != null) break;
            }
        }
        
        if (prefab != null)
        {
            GameObject vfx = Instantiate(prefab, transform);
            vfx.transform.localPosition = Vector3.up * 1.5f;
            
            ParticleSystem ps = vfx.GetComponent<ParticleSystem>();
            if (ps != null && !ps.main.loop)
            {
                Destroy(vfx, ps.main.duration + ps.main.startLifetime.constantMax);
            }
            else
            {
                Destroy(vfx, godModeBuffDuration);
            }
        }
    }
}
