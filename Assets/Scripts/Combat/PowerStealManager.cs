using UnityEngine;
using Photon.Pun;
using System;
using System.Collections.Generic;

public class PowerStealManager : MonoBehaviourPun
{
    [Header("Power Steal Configuration")]
    public PowerStealData[] powerStealData;

    [Header("Power Steal Icons (Optional)")]
    [Tooltip("Manually assign icons here. If not set, will try to load from Resources/PowerStealIcons/{enemyName}.png")]
    public Sprite[] powerStealIcons;
    public string[] powerStealIconNames; // Match enemy names to icons array

    // Example inspector setup for Amomongo's Berserk Frenzy power (set these fields in Unity Inspector)
    // Add this to the powerStealData array in the Inspector:
    // enemyName: "Amomongo"
    // powerName: "Berserk Frenzy"
    // description: "Gain Amomongo's Berserk Frenzy: temporarily boosts your damage and speed."
    // icon: [Assign Berserk icon sprite]
    // duration: 30
    // canBeStolen: true
    // stealChance: 100
    // damageBonus: 10
    // speedBonus: 2.0
    // healthBonus: 0
    // staminaBonus: 0
    // movesetData: [Optional: assign moveset for Berserk]
    // specialAbilities: [Add a SpecialAbilityData with abilityName "Berserk Frenzy", type Active, effectMagnitude 1.3, effectDuration 4.0]
    // stealVFX: [Assign VFX prefab]
    // activeVFX: [Assign VFX prefab]
    // lostVFX: [Assign VFX prefab]
    // stealSound: [Assign audio clip]
    // activeSound: [Assign audio clip]
    // lostSound: [Assign audio clip]
    // isQuestObjective: false
    // questId: ""
    public float defaultStealDuration = 30f;

    [Header("Visual Effects")]
    public GameObject powerStealVFXPrefab;
    public Transform vfxSpawnPoint;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip powerStealSound;
    public AudioClip powerLostSound;

    [Header("UI")]
    public TMPro.TextMeshProUGUI powerStealTimerText;
    public UnityEngine.UI.Image powerStealIcon;

    // Active power steal tracking (no duration, just tracks what powers are granted)
    private HashSet<string> grantedPowers = new HashSet<string>();

    // Events
    public System.Action<string> OnPowerStolen;
    public System.Action<string> OnPowerLost;
    public System.Action<string> OnPowerExpired;

    // Components
    private MovesetManager movesetManager;
    private VFXManager vfxManager;
    private QuestManager questManager;

    void Awake()
    {
        movesetManager = GetComponent<MovesetManager>();
        vfxManager = GetComponent<VFXManager>();
        questManager = FindFirstObjectByType<QuestManager>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        // Auto-populate defaults if none provided or if some are missing
        EnsureDefaultCatalog();
    }

    void Update()
    {
        if (photonView != null && !photonView.IsMine) return;
        UpdateUI();
    }

    public void StealPowerFromEnemy(string enemyName, Vector3 position)
    {
        PowerStealData powerData = GetPowerStealData(enemyName);
        if (powerData == null)
        {
            Debug.LogWarning($"No power steal data found for enemy: {enemyName}");
            return;
        }

        // Check if power can be stolen
        if (!CanStealPower(enemyName))
        {
            Debug.Log($"Cannot steal power from {enemyName}");
            return;
        }

        // Only allow granting once per enemy
        if (grantedPowers.Contains(enemyName))
        {
            Debug.Log($"[PowerStealManager] Power from {enemyName} already granted.");
            return;
        }
        grantedPowers.Add(enemyName);

        // Play VFX and audio
        PlayPowerStealVFX(enemyName, position);
        PlayPowerStealAudio();

        // Update quest progress
        if (questManager != null)
        {
            questManager.AddProgress_PowerSteal(enemyName);
        }

        Debug.Log($"[PowerStealManager] Power stolen from {enemyName}: {powerData.powerName} for player: {gameObject.name}");
        // Only assign to local player in multiplayer
        var pv = GetComponent<PhotonView>();
        if (pv == null || pv.IsMine)
        {
            var skillSlots = GetComponent<PlayerSkillSlots>();
            if (skillSlots != null)
            {
                Debug.Log($"[PowerStealManager] Assigning {powerData.powerName} to skill slots for player: {gameObject.name}");
                skillSlots.OnPowerStolen(powerData, position);
            }
            else
            {
                Debug.LogWarning($"[PowerStealManager] PlayerSkillSlots not found on {gameObject.name}");
            }
        }
        else
        {
            Debug.Log($"[PowerStealManager] Skipping skill slot assignment for remote player: {gameObject.name}");
        }
        OnPowerStolen?.Invoke(enemyName);

        // publish encyclopedia progression through the dedicated event channel
        EncyclopediaProgressEvents.ReportEncounterAndKill(enemyName);

        // Sync with other players
        if (photonView != null && photonView.IsMine)
        {
            photonView.RPC("RPC_StealPower", RpcTarget.Others, enemyName, position);
        }
    }

    [PunRPC]
    public void RPC_StealPower(string enemyName, Vector3 position)
    {
        if (photonView != null && !photonView.IsMine) return;

        StealPowerFromEnemy(enemyName, position);
    }

    private void UpdatePowerStealTimers()
    {
        // No timer logic; powers persist until used
    }

    private void RemovePower(string enemyName)
    {
        // No removal logic; powers persist until used
    }

    [PunRPC]
    // RemovePower RPC not needed; powers persist until used



    private void PlayPowerStealVFX(string enemyName, Vector3 position)
    {
        // if a uniform Power Steal VFX prefab is assigned on this manager, instantiate it here
        if (powerStealVFXPrefab != null)
        {
            Vector3 spawnPos = (vfxSpawnPoint != null) ? vfxSpawnPoint.position : position;
            var vfx = Instantiate(powerStealVFXPrefab, spawnPos, Quaternion.identity);
            // destroy after a short lifetime (use effectDuration if provided, otherwise 4s)
            float life =  (/* try to use a reasonable default */ 4f);
            if (vfx != null) Destroy(vfx, life);

            // sync to other clients by RPC so they also play the same uniform prefab
            if (photonView != null && photonView.IsMine)
            {
                photonView.RPC("RPC_PlayUniformPowerStealVFX", RpcTarget.Others, spawnPos);
            }
            return;
        }

        // fallback to VFXManager per-enemy vfx if no uniform prefab assigned
        if (vfxManager != null)
        {
            vfxManager.PlayPowerStealVFX(enemyName, position, Quaternion.identity);
        }
    }

    [PunRPC]
    public void RPC_PlayUniformPowerStealVFX(Vector3 position)
    {
        if (powerStealVFXPrefab == null) return;
        Vector3 spawnPos = (vfxSpawnPoint != null) ? vfxSpawnPoint.position : position;
        var vfx = Instantiate(powerStealVFXPrefab, spawnPos, Quaternion.identity);
        if (vfx != null) Destroy(vfx, 4f);
    }

    private void PlayPowerStealAudio()
    {
        if (audioSource != null && powerStealSound != null)
        {
            audioSource.PlayOneShot(powerStealSound);
        }
    }

    private void PlayPowerLostAudio()
    {
        if (audioSource != null && powerLostSound != null)
        {
            audioSource.PlayOneShot(powerLostSound);
        }
    }

    private void UpdateUI()
    {
        if (powerStealTimerText != null)
        {
            powerStealTimerText.text = "";
        }
        if (powerStealIcon != null)
        {
            powerStealIcon.gameObject.SetActive(false);
        }
    }

    private PowerStealData GetPowerStealData(string enemyName)
    {
        if (powerStealData == null) return null;

        foreach (var data in powerStealData)
        {
            if (data.enemyName == enemyName)
            {
                return data;
            }
        }
        return null;
    }

    private bool CanStealPower(string enemyName)
    {
        // Check if power is already granted
        if (grantedPowers.Contains(enemyName))
        {
            return false;
        }

        // Check if enemy has power to steal
        PowerStealData powerData = GetPowerStealData(enemyName);
        if (powerData == null)
        {
            return false;
        }

        // Check if power can be stolen
        if (!powerData.canBeStolen)
        {
            return false;
        }

        return true;
    }

    // Public getters
    public bool HasPower(string enemyName) => grantedPowers.Contains(enemyName);
    // No timer or expiration logic; powers persist until used
    public HashSet<string> GetGrantedPowers() => grantedPowers;

    // Removed unused method GetActivePowers

    // Clear all powers
    public void ClearAllPowers()
    {
        grantedPowers.Clear();
        // Optionally clear skill slots here if needed
    }

    // Inject default entries for known enemies from Assets/docs/POWERSTEALING_MOVES.md
    private void EnsureDefaultCatalog()
    {
        // Build a dictionary for quick lookups of existing entries by enemy name
        var existing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (powerStealData != null)
        {
            for (int i = 0; i < powerStealData.Length; i++)
            {
                if (powerStealData[i] != null && !string.IsNullOrEmpty(powerStealData[i].enemyName))
                {
                    existing[powerStealData[i].enemyName] = i;
                }
            }
        }

        var defaults = new List<PowerStealData>();

        // Helper to add if missing and assign icon
        void AddDefaultIfMissing(PowerStealData d)
        {
            if (d == null || string.IsNullOrEmpty(d.enemyName)) return;
            if (!existing.ContainsKey(d.enemyName))
            {
                // Try to load icon if not already set
                if (d.icon == null)
                {
                    d.icon = LoadPowerStealIcon(d.enemyName);
                }
                defaults.Add(d);
            }
        }

        // Names must match what enemies pass when calling StealPowerFromEnemy()
        // Common Enemies – these are the "stolen skills" the player can use in slots 1/2/3.
        // NOTE: These defaults are ONLY used when there is no matching entry
        // defined in the PowerStealData array in the inspector. If you add an
        // entry there with the same enemyName, that inspector value completely
        // overrides the defaults here and becomes editable in the inspector.
        // Aswang: forward leap attack
        AddDefaultIfMissing(new PowerStealData
        {
            enemyName = "Aswang",
            powerName = "Aswang Leap",
            description = "A long, lunging leap that slashes enemies in front of you.",
            canBeStolen = true,
            powerType = PowerStealData.PowerType.Mobility,
            cooldown = 20f,
            attackDamage = 30,
            // slightly larger hit area to match the longer leap
            attackRadius = 3.0f,
            // noticeably bigger leap distance
            dashDistance = 9f,
            // keep speed modest so it's readable
            dashSpeed = 1.3f,
            animationTriggers = new[] { "AswangLeap" }
        });

        // Manananggal: dive / glide strike
        AddDefaultIfMissing(new PowerStealData
        {
            enemyName = "Manananggal",
            powerName = "Manananggal Dive",
            description = "Launch into the air briefly then dive toward your target.",
            canBeStolen = true,
            powerType = PowerStealData.PowerType.Mobility,
            cooldown = 18f,
            attackDamage = 28,
            attackRadius = 2.5f,
            // farther and a bit slower to feel like a controlled dive
            dashDistance = 10f,
            dashSpeed = 1.2f,
            glideDuration = 0.8f,
            animationTriggers = new[] { "ManananggalDive" }
        });

        // Tiyanak: short leap / lunge
        AddDefaultIfMissing(new PowerStealData
        {
            enemyName = "Tiyanak",
            powerName = "Tiyanak Leap",
            description = "A quick forward leap that bites enemies in a small radius.",
            canBeStolen = true,
            powerType = PowerStealData.PowerType.Attack,
            cooldown = 14f,
            attackDamage = 24,
            attackRadius = 2.0f,
            // slightly longer leap so it feels like the enemy's pounce, but not as far as Manananggal
            dashDistance = 5.5f,
            dashSpeed = 1.4f,
            animationTriggers = new[] { "TiyanakLeap" }
        });

        AddDefaultIfMissing(new PowerStealData
        {
            enemyName = "Sigbin",
            powerName = "Shadow Veil",
            description = "Brief invisibility; next attack deals bonus damage",
            canBeStolen = true,
            powerType = PowerStealData.PowerType.Stealth,
            cooldown = 25f,
            stealthDuration = 5f,
            stealthDamageBonus = 0.5f
        });

        // Level 2
        AddDefaultIfMissing(new PowerStealData
        {
            enemyName = "Berberoka",
            powerName = "Berberoka Vortex",
            description = "Pull nearby enemies into a drowning vortex around you.",
            canBeStolen = true,
            powerType = PowerStealData.PowerType.Control,
            cooldown = 24f,
            attackDamage = 26,
            attackRadius = 5.5f,
            animationTriggers = new[] { "BerberokaVortex" }
        });

        // Bungisngis: frontal shout + stomp
        AddDefaultIfMissing(new PowerStealData
        {
            enemyName = "Bungisngis",
            powerName = "Bungisngis Shout",
            description = "Let out a painful laugh and stomp, damaging enemies around you.",
            canBeStolen = true,
            powerType = PowerStealData.PowerType.Control,
            cooldown = 22f,
            attackDamage = 32,
            attackRadius = 4.5f,
            animationTriggers = new[] { "BungisngisShout" }
        });

        AddDefaultIfMissing(new PowerStealData
        {
            enemyName = "Tikbalang",
            powerName = "Tikbalang Stomp",
            description = "Slam the ground and damage enemies in a wide area around you.",
            canBeStolen = true,
            powerType = PowerStealData.PowerType.Attack,
            cooldown = 18f,
            attackRadius = 5f,
            attackDamage = 40,
            effectDuration = 0f,
            speedBonus = 0f,
            animationTriggers = new[] { "TikbalangStomp" }
        });

        // Level 3
        AddDefaultIfMissing(new PowerStealData
        {
            enemyName = "Kapre",
            powerName = "Kapre Strength",
            description = "+damage; ground-slam cone",
            canBeStolen = true,
            powerType = PowerStealData.PowerType.Buff,
            cooldown = 25f,
            buffDuration = 10f,
            damageBonus = 15,
            attackRadius = 6f
        });

        AddDefaultIfMissing(new PowerStealData
        {
            enemyName = "ShadowDiwata",
            powerName = "Spirit Bloom",
            description = "Healing field; cleanses 1 minor debuff",
            canBeStolen = true,
            powerType = PowerStealData.PowerType.Heal,
            cooldown = 35f,
            healOverTime = 10f,
            healFieldDuration = 6f,
            cleanseDebuffs = 1f
        });

        // Level 4
        AddDefaultIfMissing(new PowerStealData
        {
            enemyName = "Amomongo",
            powerName = "Primal Surge",
            description = "+melee speed/damage; minor self-heal on hit",
            canBeStolen = true,
            powerType = PowerStealData.PowerType.Buff,
            cooldown = 30f,
            buffDuration = 8f,
            damageBonus = 10,
            speedBonus = 1.3f,
            healOverTime = 0f
        });

        if (defaults.Count == 0) return;

        if (powerStealData == null || powerStealData.Length == 0)
        {
            powerStealData = defaults.ToArray();
        }
        else
        {
            var merged = new List<PowerStealData>(powerStealData.Length + defaults.Count);
            merged.AddRange(powerStealData);
            merged.AddRange(defaults);
            powerStealData = merged.ToArray();
        }

        // After merging, ensure all entries have icons loaded
        if (powerStealData != null)
        {
            for (int i = 0; i < powerStealData.Length; i++)
            {
                if (powerStealData[i] != null && powerStealData[i].icon == null)
                {
                    powerStealData[i].icon = LoadPowerStealIcon(powerStealData[i].enemyName);
                }
            }
        }
    }

    private Sprite LoadPowerStealIcon(string enemyName)
    {
        if (string.IsNullOrEmpty(enemyName)) return null;

        // First, check manual assignment array
        if (powerStealIcons != null && powerStealIconNames != null)
        {
            for (int i = 0; i < powerStealIconNames.Length && i < powerStealIcons.Length; i++)
            {
                if (string.Equals(powerStealIconNames[i], enemyName, StringComparison.OrdinalIgnoreCase))
                {
                    return powerStealIcons[i];
                }
            }
        }

        // Fallback: Try loading from Resources
        string resourcePath = $"PowerStealIcons/{enemyName}";
        Sprite loaded = Resources.Load<Sprite>(resourcePath);
        if (loaded != null)
        {
            return loaded;
        }

        // Try alternative paths
        string[] alternativePaths = {
            $"Icons/PowerSteal/{enemyName}",
            $"Items/1ItemData/Icons/{enemyName}",
            $"PowerStealIcons/Icon_{enemyName}"
        };

        foreach (var path in alternativePaths)
        {
            loaded = Resources.Load<Sprite>(path);
            if (loaded != null) return loaded;
        }

        return null; // No icon found
    }
}

// PowerStealInstance removed; powers are now one-time use active skills

[System.Serializable]
public class PowerStealData
{
    [Header("Skill Effects")]
    public bool stopPlayerOnActivate = false; // if true, player movement is stopped when skill is used
    public string[] animationTriggers; // list of animation trigger names to activate on player
    [Header("Power Information")]
    public string enemyName;
    public string powerName;
    // PowerType enum and powerType field defined only once below
    [TextArea] public string description;
    public Sprite icon;

    [Header("Power Properties")]
    public bool canBeStolen = true;
    public int stealChance = 100; // Percentage

    [Header("Stat Modifications")]
    public int damageBonus = 0;
    public float speedBonus = 0f;
    public int healthBonus = 0;
    public int staminaBonus = 0;

    public enum PowerType { Attack, Buff, Utility, Mobility, Stealth, Defensive, Control, Ultimate, Heal }
    public PowerType powerType = PowerType.Attack;
    
    [Header("Cooldown and Charges")]
    public float cooldown = 30f; // Base cooldown in seconds
    public int maxCharges = 1; // Number of charges (1 = normal, >1 = multi-use) - DEPRECATED: Use maxUsages instead
    public float chargeRechargeTime = 0f; // If >0, charges recharge over time - DEPRECATED
    
    [Header("Usage System")]
    [Tooltip("Maximum number of times this skill can be used before being consumed. Set to 0 for unlimited uses. Auto-set based on power type if autoSetUsagesByType is true.")]
    public int maxUsages = 0; // 0 = unlimited, 1 = single use (high benefit), 2-3 = multi-use (normal skills)
    [Tooltip("If true, automatically sets maxUsages based on power type and power level:\n- Ultimate = 1 usage\n- High-power Buffs/Heals = 1 usage\n- Normal skills = 2-3 usages")]
    public bool autoSetUsagesByType = true;
    
    void OnValidate()
    {
        // Auto-set usages when power data is modified in inspector
        if (autoSetUsagesByType && maxUsages == 0)
        {
            if (powerType == PowerType.Ultimate)
            {
                maxUsages = 1; // High benefit = 1 use
            }
            else if (powerType == PowerType.Buff && (damageBonus >= 15 || speedBonus >= 3f || healthBonus >= 50))
            {
                maxUsages = 1; // Very powerful buffs = 1 use
            }
            else if (powerType == PowerType.Heal && healAmount >= 50)
            {
                maxUsages = 1; // Large heals = 1 use
            }
            else if (powerType == PowerType.Attack && attackDamage >= 50)
            {
                maxUsages = 1; // High damage attacks = 1 use
            }
            else
            {
                // Normal skills = 2-3 uses (default to 2)
                maxUsages = 2;
            }
        }
    }
    
    [Header("Attack Properties")]
    public float attackRadius = 0f; // for AOE
    public int attackDamage = 0; // Damage dealt
    public float projectileSpeed = 0f; // for projectiles
    public float knockbackForce = 0f; // Knockback strength
    
    [Header("Buff Properties")]
    public float effectDuration = 0f; // for buffs/DoT duration
    public float buffDuration = 0f; // Duration of stat buffs
    public bool isPassive = false; // If true, buff is always active
    
    [Header("Mobility Properties")]
    public float dashDistance = 0f; // Dash distance
    public float dashSpeed = 0f; // Dash speed multiplier
    public float glideDuration = 0f; // Glide time
    public float fallDamageReduction = 0f; // Percentage reduction (0-1)
    
    [Header("Stealth Properties")]
    public float stealthDuration = 0f; // Invisibility duration
    public float stealthDamageBonus = 0f; // Damage multiplier on next attack
    
    [Header("Defensive Properties")]
    public float shieldDuration = 0f; // Shield/bubble duration
    public float damageReduction = 0f; // Damage reduction (0-1)
    public bool reflectProjectiles = false; // Can reflect projectiles
    
    [Header("Control Properties")]
    public float slowPercentage = 0f; // Slow percentage (0-1)
    public float rootDuration = 0f; // Root duration
    public float fearDuration = 0f; // Fear/confuse duration
    public float weaknessAmount = 0f; // Attack power reduction on enemies
    
    [Header("Heal Properties")]
    public int healAmount = 0; // Instant heal
    public float healOverTime = 0f; // Heal per second
    public float healFieldDuration = 0f; // Healing field duration
    public float cleanseDebuffs = 0f; // Number of debuffs to cleanse
    
    [Header("Ultimate Properties")]
    public float channelDuration = 0f; // Channel time before activation
    public float ultimateRadius = 0f; // Ultimate AoE radius
    public bool requiresChannel = false; // If true, player must channel
    
    [Header("Stop / Timing")]
    public float stopDuration = 0.5f; // duration to stop player when activating this power

    [Header("Moveset")]
    public MovesetData movesetData;

    // All powers are now active skills; no special abilities or passive/trigger logic

    [Header("VFX")]
    public GameObject stealVFX;
    public GameObject activeVFX;
    public GameObject lostVFX;

    [Header("Audio")]
    public AudioClip stealSound;
    public AudioClip activeSound;
    public AudioClip lostSound;

    [Header("Quest Integration")]
    public bool isQuestObjective = false;
    public string questId = "";
}


