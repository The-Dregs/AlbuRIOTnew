using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
// add this if PowerType is in PowerStealManager.cs
using static PowerStealManager;

public class PlayerSkillSlots : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = false;

    private void LogVerbose(string message)
    {
        if (enableDebugLogs) Debug.Log(message);
    }

    void Update()
    {
        UpdateCooldowns();
        
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            UseSkillSlot(0);
        }
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            UseSkillSlot(1);
        }
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            UseSkillSlot(2);
        }
    }
    
    private void UpdateCooldowns()
    {
        for (int i = 0; i < skillSlots.Length; i++)
        {
            if (skillSlots[i] != null)
            {
                // Update cooldown timer
                if (slotCooldownTimers.ContainsKey(i) && slotCooldownTimers[i] > 0f)
                {
                    slotCooldownTimers[i] -= Time.deltaTime;
                }
                
                // Update charge recharge
                if (skillSlots[i].maxCharges > 1 && skillSlots[i].chargeRechargeTime > 0f)
                {
                    if (!slotCurrentCharges.ContainsKey(i))
                        slotCurrentCharges[i] = skillSlots[i].maxCharges;
                    
                    if (slotCurrentCharges[i] < skillSlots[i].maxCharges)
                    {
                        if (!slotChargeRechargeTimers.ContainsKey(i))
                            slotChargeRechargeTimers[i] = 0f;
                        
                        slotChargeRechargeTimers[i] += Time.deltaTime;
                        if (slotChargeRechargeTimers[i] >= skillSlots[i].chargeRechargeTime)
                        {
                            slotCurrentCharges[i]++;
                            slotChargeRechargeTimers[i] = 0f;
                            LogVerbose($"[PlayerSkillSlots] Slot {i + 1} ({skillSlots[i].powerName}) regained charge: {slotCurrentCharges[i]}/{skillSlots[i].maxCharges}");
                        }
                    }
                }
            }
        }
    }
    
    private bool IsSkillReady(int slotIndex)
    {
        if (skillSlots[slotIndex] == null) return false;
        
        // Check cooldown
        if (slotCooldownTimers.ContainsKey(slotIndex) && slotCooldownTimers[slotIndex] > 0f)
            return false;
        
        // Check usages (new system - consumes skill)
        if (slotCurrentUsages.ContainsKey(slotIndex) && slotCurrentUsages[slotIndex] <= 0)
            return false;
        
        // Check charges (legacy system - for backward compatibility)
        if (skillSlots[slotIndex].maxCharges > 1)
        {
            if (!slotCurrentCharges.ContainsKey(slotIndex))
                slotCurrentCharges[slotIndex] = skillSlots[slotIndex].maxCharges;
            
            if (slotCurrentCharges[slotIndex] <= 0)
                return false;
        }
        
        return true;
    }
    
    private float GetCooldownRemaining(int slotIndex)
    {
        if (slotCooldownTimers.ContainsKey(slotIndex))
            return slotCooldownTimers[slotIndex];
        return 0f;
    }

    void Awake()
    {
        // Set all backgrounds to empty on start
        for (int i = 0; i < skillSlotBgImages.Length; i++)
        {
            if (skillSlotBgImages[i] != null)
                skillSlotBgImages[i].sprite = bgEmptySprite;
        }

        // Hide all skill images on start
        for (int i = 0; i < skillSlotSkillImages.Length; i++)
        {
            if (skillSlotSkillImages[i] != null)
                skillSlotSkillImages[i].gameObject.SetActive(false); // hide in game until skill is assigned
        }
    }
    [Header("Skill Slot Data")]
    public PowerStealData[] skillSlots = new PowerStealData[3];
    
    // Cooldown and charge tracking
    private Dictionary<int, float> slotCooldownTimers = new Dictionary<int, float>();
    private Dictionary<int, int> slotCurrentCharges = new Dictionary<int, int>();
    private Dictionary<int, float> slotChargeRechargeTimers = new Dictionary<int, float>();
    
    // Usage tracking (consumes skill after maxUsages)
    private Dictionary<int, int> slotCurrentUsages = new Dictionary<int, int>();

    [Header("Skill Slot UI")]
    public Image[] skillSlotBgImages = new Image[3]; // assign in inspector
    public Image[] skillSlotSkillImages = new Image[3]; // assign in inspector

    [Header("Background Sprites")]
    public Sprite bgEmptySprite; // assign in inspector
    public Sprite bgFilledSprite; // assign in inspector

    // Assign a stolen power to the first available slot
    public void AssignPowerToSlot(PowerStealData powerData)
    {
        for (int i = 0; i < skillSlots.Length; i++)
        {
            if (skillSlots[i] == null || skillSlots[i].powerName == null || skillSlots[i].powerName == "")
            {
                skillSlots[i] = powerData;
                
                // Initialize usage count
                int maxUsages = GetMaxUsagesForPower(powerData);
                slotCurrentUsages[i] = maxUsages;
                
                Debug.Log($"[PlayerSkillSlots] Assigned {powerData.powerName} to slot {i + 1} with {maxUsages} usage(s)");
                UpdateSkillSlotUI(i);
                for (int j = 0; j < skillSlots.Length; j++)
                {
                    Debug.Log($"[PlayerSkillSlots] Slot {j + 1}: {(skillSlots[j] != null ? skillSlots[j].powerName : "<empty>")}");
                }
                return;
            }
        }
        Debug.LogWarning("[PlayerSkillSlots] No empty skill slots available!");
    }
    
    private int GetMaxUsagesForPower(PowerStealData power)
    {
        if (power == null) return 0;

        // For key power-steal enemies, each stolen skill is a single-use ability.
        // These names should match PowerStealData.enemyName / EnemyData.enemyName.
        if (!string.IsNullOrEmpty(power.enemyName))
        {
            string id = power.enemyName;
            if (id == "Tiyanak" ||
                id == "Aswang" ||
                id == "Tikbalang" ||
                id == "Manananggal" ||
                id == "Berberoka" ||
                id == "Bungisngis")
            {
                return 1;
            }
        }
        
        // If maxUsages is explicitly set and > 0, use it
        if (power.maxUsages > 0)
            return power.maxUsages;
        
        // If autoSetUsagesByType is enabled, set based on power type
        if (power.autoSetUsagesByType)
        {
            // High benefit skills (Ultimate) = 1 usage
            if (power.powerType == PowerStealData.PowerType.Ultimate)
                return 1;
            
            // Very powerful buffs/heals = 1 usage
            if (power.powerType == PowerStealData.PowerType.Buff && 
                (power.damageBonus >= 15 || power.speedBonus >= 3f || power.healthBonus >= 50))
                return 1;
            
            if (power.powerType == PowerStealData.PowerType.Heal && power.healAmount >= 50)
                return 1;
            
            // Normal skills = 2-3 usages (randomize for variety, or use 2 as default)
            return 2;
        }
        
        // Default: unlimited (0) or fallback to maxCharges for backward compatibility
        return power.maxCharges > 0 ? power.maxCharges : 0;
    }

    // Use a skill from a slot (call from UI button)
    public void UseSkillSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= skillSlots.Length)
        {
            Debug.LogWarning($"[PlayerSkillSlots] Invalid slot index: {slotIndex}");
            return;
        }
        
        var power = skillSlots[slotIndex];
        if (power == null)
        {
            Debug.Log($"Skill slot {slotIndex + 1} is empty.");
            return;
        }
        
        // Check if skill is ready
        if (!IsSkillReady(slotIndex))
        {
            float cdRemaining = GetCooldownRemaining(slotIndex);
            int usagesRemaining = slotCurrentUsages.ContainsKey(slotIndex) ? slotCurrentUsages[slotIndex] : 0;
            
            if (usagesRemaining <= 0)
            {
                Debug.Log($"[PlayerSkillSlots] {power.powerName} has no usages remaining!");
                return;
            }
            
            Debug.Log($"[PlayerSkillSlots] {power.powerName} on cooldown: {cdRemaining:F1}s remaining");
            return;
        }
        
        // Consume usage (new system)
        if (slotCurrentUsages.ContainsKey(slotIndex))
        {
            slotCurrentUsages[slotIndex]--;
            int remaining = slotCurrentUsages[slotIndex];
            Debug.Log($"[PlayerSkillSlots] {power.powerName} used. Remaining usages: {remaining}");
            
            // If no usages left, mark for removal after execution
            if (remaining <= 0)
            {
                Debug.Log($"[PlayerSkillSlots] {power.powerName} has been consumed! Will be removed after use.");
            }
        }
        
        // Consume charge if multi-charge power (legacy system - backward compatibility)
        if (power.maxCharges > 1)
        {
            if (!slotCurrentCharges.ContainsKey(slotIndex))
                slotCurrentCharges[slotIndex] = power.maxCharges;
            
            slotCurrentCharges[slotIndex]--;
            Debug.Log($"[PlayerSkillSlots] {power.powerName} charges: {slotCurrentCharges[slotIndex]}/{power.maxCharges}");
        }
        
        // Start cooldown
        if (!slotCooldownTimers.ContainsKey(slotIndex))
            slotCooldownTimers[slotIndex] = 0f;
        slotCooldownTimers[slotIndex] = power.cooldown;
        
        Debug.Log($"Using skill: {power.powerName}");
        var player = gameObject;
        var stats = player.GetComponent<PlayerStats>();
        var animator = player.GetComponent<Animator>();
        // stop player movement if requested (temporary)
        ThirdPersonController controller = null;
        if (power.stopPlayerOnActivate)
        {
            controller = player.GetComponent<ThirdPersonController>();
            if (controller != null)
            {
                controller.enabled = false;
                Debug.Log("[PlayerSkillSlots] Player movement stopped for skill activation.");
            }
        }
        // trigger animation parameters
        if (animator != null && power.animationTriggers != null)
        {
            foreach (var trigger in power.animationTriggers)
            {
                animator.SetTrigger(trigger);
                Debug.Log($"[PlayerSkillSlots] Triggered animation: {trigger}");
            }
        }
        // play VFX on skill activation (auto-destroy after stopDuration or effectDuration)
        GameObject activeVfxInstance = null;
        if (power.activeVFX != null)
        {
            activeVfxInstance = Instantiate(power.activeVFX, player.transform.position, Quaternion.identity);
            float vfxLifetime = power.effectDuration > 0f ? power.effectDuration : power.stopDuration > 0f ? power.stopDuration : 2f;
            if (activeVfxInstance != null) Destroy(activeVfxInstance, vfxLifetime);
            Debug.Log("[PlayerSkillSlots] Played active VFX on skill use.");
        }
        // Execute power based on type
        StartCoroutine(ExecutePower(power, slotIndex, stats, controller));
    }

    // Clear a slot (e.g., when power expires or is consumed)
    public void ClearSkillSlot(int slotIndex)
    {
        skillSlots[slotIndex] = null;
        
        // Clear usage tracking
        if (slotCurrentUsages.ContainsKey(slotIndex))
            slotCurrentUsages.Remove(slotIndex);
        
        // Clear charge tracking
        if (slotCurrentCharges.ContainsKey(slotIndex))
            slotCurrentCharges.Remove(slotIndex);
        
        // Clear cooldown
        if (slotCooldownTimers.ContainsKey(slotIndex))
            slotCooldownTimers.Remove(slotIndex);
        
        UpdateSkillSlotUI(slotIndex);
    }
    
    public int GetRemainingUsages(int slotIndex)
    {
        if (!slotCurrentUsages.ContainsKey(slotIndex))
            return 0;
        return slotCurrentUsages[slotIndex];
    }
    
    public int GetMaxUsages(int slotIndex)
    {
        if (skillSlots[slotIndex] == null) return 0;
        return GetMaxUsagesForPower(skillSlots[slotIndex]);
    }

    // Update UI icon for a slot
    public void UpdateSkillSlotUI(int slotIndex)
    {
        // Update background image
        if (skillSlotBgImages != null && slotIndex < skillSlotBgImages.Length)
        {
            var bgImg = skillSlotBgImages[slotIndex];
            if (bgImg != null)
            {
                if (skillSlots[slotIndex] != null)
                    bgImg.sprite = bgFilledSprite;
                else
                    bgImg.sprite = bgEmptySprite;
            }
        }
        else
        {
            Debug.LogWarning($"[PlayerSkillSlots] skillSlotBgImages array not assigned or index out of range: {slotIndex}");
        }

        // Update skill image
        if (skillSlotSkillImages != null && slotIndex < skillSlotSkillImages.Length)
        {
            var skillImg = skillSlotSkillImages[slotIndex];
            var slot = skillSlots[slotIndex];
            if (skillImg == null)
            {
                Debug.LogWarning($"[PlayerSkillSlots] Skill slot skill image {slotIndex + 1} is not assigned!");
                return;
            }
            if (slot != null && slot.icon != null)
            {
                skillImg.sprite = slot.icon;
                skillImg.enabled = true;
                skillImg.gameObject.SetActive(true);
                Debug.Log($"[PlayerSkillSlots] Updated skill image for slot {slotIndex + 1}: icon={slot.icon.name}, enabled=true");
            }
            else
            {
                skillImg.sprite = null;
                skillImg.enabled = false;
                skillImg.gameObject.SetActive(true);
                Debug.Log($"[PlayerSkillSlots] Updated skill image for slot {slotIndex + 1}: icon=<none>, enabled=false");
            }
        }
        else
        {
            Debug.LogWarning($"[PlayerSkillSlots] skillSlotSkillImages array not assigned or index out of range: {slotIndex}");
        }
        
        // Update usage UI if available
        UpdateUsageUI(slotIndex);
    }
    
    private void UpdateUsageUI(int slotIndex)
    {
        // Find SkillUsageUI components in the skill slot UI hierarchy
        // This will be called by the SkillUsageUI component itself via Update()
        // But we can trigger updates here if needed
    }


    private IEnumerator ExecutePower(PowerStealData power, int slotIndex, PlayerStats stats, ThirdPersonController controller)
    {
        // Handle channeling for ultimates
        if (power.requiresChannel && power.channelDuration > 0f)
        {
            // TODO: Show channel UI, allow cancel
            yield return new WaitForSeconds(power.channelDuration);
        }
        
        // Execute based on power type
        switch (power.powerType)
        {
            case PowerStealData.PowerType.Attack:
                ExecuteAttackPower(power, stats);
                break;
            case PowerStealData.PowerType.Buff:
                ExecuteBuffPower(power, stats);
                break;
            case PowerStealData.PowerType.Mobility:
                ExecuteMobilityPower(power, stats, controller);
                break;
            case PowerStealData.PowerType.Stealth:
                ExecuteStealthPower(power, stats);
                break;
            case PowerStealData.PowerType.Defensive:
                ExecuteDefensivePower(power, stats);
                break;
            case PowerStealData.PowerType.Control:
                ExecuteControlPower(power, stats);
                break;
            case PowerStealData.PowerType.Heal:
                ExecuteHealPower(power, stats);
                break;
            case PowerStealData.PowerType.Ultimate:
                ExecuteUltimatePower(power, stats);
                break;
            case PowerStealData.PowerType.Utility:
                ExecuteUtilityPower(power, stats);
                break;
        }
        
        // Restore movement after duration if we stopped it
        if (controller != null && power.stopPlayerOnActivate)
        {
            float restoreDelay = power.stopDuration > 0f ? power.stopDuration : 0.5f;
            yield return new WaitForSeconds(restoreDelay);
            controller.enabled = true;
            Debug.Log("[PlayerSkillSlots] Player movement restored after skill activation.");
        }
        
        // Check if skill should be consumed (no usages remaining)
        if (slotCurrentUsages.ContainsKey(slotIndex) && slotCurrentUsages[slotIndex] <= 0)
        {
            Debug.Log($"[PlayerSkillSlots] {power.powerName} consumed! Removing from slot {slotIndex + 1}.");
            ClearSkillSlot(slotIndex);
        }
    }
    
    private void ExecuteAttackPower(PowerStealData power, PlayerStats stats)
    {
        if (power.attackRadius > 0f)
        {
            // AoE Attack
            Vector3 center = transform.position;
            Collider[] hits = Physics.OverlapSphere(center, power.attackRadius, LayerMask.GetMask("Enemy"));
            var seen = new HashSet<GameObject>();
            foreach (var c in hits)
            {
                var dmg = c.GetComponentInParent<IEnemyDamageable>();
                if (dmg != null)
                {
                    var mb = dmg as MonoBehaviour;
                    if (mb != null) seen.Add(mb.gameObject);
                }
            }
            foreach (var go in seen)
            {
                EnemyDamageRelay.Apply(go, power.attackDamage, gameObject);
            }
            Debug.Log($"[PlayerSkillSlots] {power.powerName} hit {seen.Count} enemies");
        }
        else if (power.projectileSpeed > 0f)
        {
            // TODO: Spawn projectile
            Debug.Log($"[PlayerSkillSlots] {power.powerName} projectile (not yet implemented)");
        }
    }
    
    private void ExecuteBuffPower(PowerStealData power, PlayerStats stats)
    {
        if (stats == null) return;
        
        if (power.isPassive)
        {
            // Passive buff - always active
            stats.maxHealth += power.healthBonus;
            stats.maxStamina += power.staminaBonus;
            stats.baseDamage += power.damageBonus;
            stats.speedModifier += power.speedBonus;
            Debug.Log($"[PlayerSkillSlots] {power.powerName} passive buff applied");
        }
        else if (power.buffDuration > 0f)
        {
            // Temporary buff
            StartCoroutine(ApplyTemporaryBuff(power, stats));
        }
    }
    
    private IEnumerator ApplyTemporaryBuff(PowerStealData power, PlayerStats stats)
    {
        // Apply buffs
        if (power.healthBonus > 0) stats.maxHealth += power.healthBonus;
        if (power.staminaBonus > 0) stats.maxStamina += power.staminaBonus;
        if (power.damageBonus > 0) stats.baseDamage += power.damageBonus;
        if (power.speedBonus > 0f) stats.speedModifier += power.speedBonus;
        
        Debug.Log($"[PlayerSkillSlots] {power.powerName} buff active for {power.buffDuration}s");
        
        yield return new WaitForSeconds(power.buffDuration);
        
        // Remove buffs
        if (power.healthBonus > 0) stats.maxHealth -= power.healthBonus;
        if (power.staminaBonus > 0) stats.maxStamina -= power.staminaBonus;
        if (power.damageBonus > 0) stats.baseDamage -= power.damageBonus;
        if (power.speedBonus > 0f) stats.speedModifier -= power.speedBonus;
        
        Debug.Log($"[PlayerSkillSlots] {power.powerName} buff expired");
    }
    
    private void ExecuteMobilityPower(PowerStealData power, PlayerStats stats, ThirdPersonController controller)
    {
        if (power.dashDistance > 0f || power.dashSpeed > 0f)
        {
            // Dash / leap-style movement
            // Prefer an explicit controller if provided, otherwise try to find one on the player.
            var moveController = controller != null ? controller : GetComponent<ThirdPersonController>();

            Vector3 dashDir = transform.forward;
            float dashDist = power.dashDistance > 0f ? power.dashDistance : 5f;
            float dashSpeedMult = power.dashSpeed > 0f ? power.dashSpeed : 1.0f;

            // If this mobility power also has attack values, perform a leap
            // and then apply its damage/area effect once the leap finishes.
            if (power.attackRadius > 0f || power.attackDamage > 0)
            {
                StartCoroutine(DashAndStrike(power, stats, moveController, dashDir, dashDist, dashSpeedMult));
            }
            else
            {
                StartCoroutine(DashMovement(moveController, dashDir, dashDist, dashSpeedMult));
            }
        }
        else if (power.glideDuration > 0f)
        {
            // Glide
            StartCoroutine(ApplyGlide(power, stats, controller));
        }
    }
    
    private IEnumerator DashMovement(ThirdPersonController controller, Vector3 direction, float distance, float speedMult)
    {
        // Temporarily disable normal movement so the leap is clean.
        bool hadController = controller != null;
        if (hadController)
            controller.enabled = false;

        float elapsed = 0f;
        float dashTime = 0.3f / Mathf.Max(0.1f, speedMult); // Faster speedMult = quicker dash
        Vector3 startPos = transform.position;
        Vector3 targetPos = startPos + direction.normalized * distance;
        
        while (elapsed < dashTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / dashTime;
            transform.position = Vector3.Lerp(startPos, targetPos, t);
            yield return null;
        }

        if (hadController && controller != null)
            controller.enabled = true;
    }

    private IEnumerator DashAndStrike(PowerStealData power, PlayerStats stats, ThirdPersonController controller, Vector3 direction, float distance, float speedMult)
    {
        // perform the leap first
        yield return DashMovement(controller, direction, distance, speedMult);

        // then apply the attack effect if any
        ExecuteAttackPower(power, stats);
    }
    
    private IEnumerator ApplyGlide(PowerStealData power, PlayerStats stats, ThirdPersonController controller)
    {
        // TODO: Implement glide mechanics
        Debug.Log($"[PlayerSkillSlots] {power.powerName} glide for {power.glideDuration}s");
        yield return new WaitForSeconds(power.glideDuration);
    }
    
    private void ExecuteStealthPower(PowerStealData power, PlayerStats stats)
    {
        StartCoroutine(ApplyStealth(power, stats));
    }
    
    private IEnumerator ApplyStealth(PowerStealData power, PlayerStats stats)
    {
        // TODO: Make player invisible
        Debug.Log($"[PlayerSkillSlots] {power.powerName} stealth for {power.stealthDuration}s");
        yield return new WaitForSeconds(power.stealthDuration);
    }
    
    private void ExecuteDefensivePower(PowerStealData power, PlayerStats stats)
    {
        if (power.shieldDuration > 0f)
        {
            StartCoroutine(ApplyShield(power, stats));
        }
    }
    
    private IEnumerator ApplyShield(PowerStealData power, PlayerStats stats)
    {
        // TODO: Apply damage reduction
        Debug.Log($"[PlayerSkillSlots] {power.powerName} shield for {power.shieldDuration}s ({power.damageReduction * 100f}% reduction)");
        yield return new WaitForSeconds(power.shieldDuration);
    }
    
    private void ExecuteControlPower(PowerStealData power, PlayerStats stats)
    {
        if (power.attackRadius <= 0f) return;

        Vector3 center = transform.position;
        Collider[] hits = Physics.OverlapSphere(center, power.attackRadius, LayerMask.GetMask("Enemy"));
        var seen = new HashSet<GameObject>();
        foreach (var c in hits)
        {
            var dmg = c.GetComponentInParent<IEnemyDamageable>();
            if (dmg != null)
            {
                var mb = dmg as MonoBehaviour;
                if (mb != null) seen.Add(mb.gameObject);
            }
        }
        foreach (var go in seen)
        {
            int dmgAmount = power.attackDamage > 0 ? power.attackDamage : 10;
            EnemyDamageRelay.Apply(go, dmgAmount, gameObject);
        }
        Debug.Log($"[PlayerSkillSlots] {power.powerName} control effect hit {seen.Count} enemies");
    }
    
    private void ExecuteHealPower(PowerStealData power, PlayerStats stats)
    {
        if (stats == null) return;
        
        if (power.healAmount > 0)
        {
            stats.Heal(power.healAmount);
            Debug.Log($"[PlayerSkillSlots] {power.powerName} healed {power.healAmount} HP");
        }
        
        if (power.healOverTime > 0f && power.healFieldDuration > 0f)
        {
            StartCoroutine(HealOverTime(power, stats));
        }
        
        if (power.cleanseDebuffs > 0)
        {
            // TODO: Cleanse debuffs from player
            Debug.Log($"[PlayerSkillSlots] {power.powerName} cleansing debuffs");
        }
    }
    
    private IEnumerator HealOverTime(PowerStealData power, PlayerStats stats)
    {
        float elapsed = 0f;
        while (elapsed < power.healFieldDuration)
        {
            elapsed += Time.deltaTime;
            if (elapsed >= 1f) // Heal every second
            {
                stats.Heal(Mathf.RoundToInt(power.healOverTime));
                elapsed = 0f;
            }
            yield return null;
        }
    }
    
    private void ExecuteUltimatePower(PowerStealData power, PlayerStats stats)
    {
        if (power.ultimateRadius > 0f)
        {
            Vector3 center = transform.position;
            Collider[] hits = Physics.OverlapSphere(center, power.ultimateRadius, LayerMask.GetMask("Enemy"));
            var seen = new HashSet<GameObject>();
            foreach (var c in hits)
            {
                var dmg = c.GetComponentInParent<IEnemyDamageable>();
                if (dmg != null)
                {
                    var mb = dmg as MonoBehaviour;
                    if (mb != null) seen.Add(mb.gameObject);
                }
            }
            foreach (var go in seen)
            {
                EnemyDamageRelay.Apply(go, power.attackDamage, gameObject);
                // TODO: Apply knockback if power.knockbackForce > 0
            }
            Debug.Log($"[PlayerSkillSlots] {power.powerName} ultimate hit {seen.Count} enemies");
        }
    }
    
    private void ExecuteUtilityPower(PowerStealData power, PlayerStats stats)
    {
        // Custom utility logic based on specific power
        Debug.Log($"[PlayerSkillSlots] {power.powerName} utility power executed");
    }
    
    private IEnumerator RestoreMovementAfter(ThirdPersonController controller, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (controller != null)
        {
            controller.enabled = true;
            Debug.Log("[PlayerSkillSlots] Player movement restored after skill activation.");
        }
    }
    
    // Initialize charges when power is assigned
    public void OnPowerStolen(PowerStealData powerData, Vector3 enemyPosition)
    {
        AssignPowerToSlot(powerData);
        
        // Initialize charges for multi-charge powers (legacy - backward compatibility)
        for (int i = 0; i < skillSlots.Length; i++)
        {
            if (skillSlots[i] == powerData && powerData.maxCharges > 1)
            {
                slotCurrentCharges[i] = powerData.maxCharges;
            }
        }
        
        // Usages are already initialized in AssignPowerToSlot
    }
}
