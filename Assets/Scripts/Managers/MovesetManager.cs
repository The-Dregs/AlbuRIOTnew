using UnityEngine;
using Photon.Pun;
using System;
using System.Collections.Generic;

public class MovesetManager : MonoBehaviourPun
{
    [Header("Moveset Configuration")]
    public MovesetData[] availableMovesets;
    public MovesetData currentMoveset;
    
    [Header("Combat Integration")]
    public PlayerCombat playerCombat;
    public PlayerStats playerStats;
    public Animator animator;
    
    [Header("VFX Integration")]
    public VFXManager vfxManager;
    [SerializeField] private bool enableDebugLogs = false;

    [Header("UI References")]
    public TMPro.TextMeshProUGUI movesetNameText;
    // power steal UI moved to PowerStealManager
    
    // Events
    public event Action<MovesetData> OnMovesetChanged;
    // power steal events centralized in PowerStealManager
    
    private PowerStealManager cachedPowerStealManager;

    void Awake()
    {
        // Auto-find components
        if (playerCombat == null)
            playerCombat = GetComponent<PlayerCombat>();
        if (playerStats == null)
            playerStats = GetComponent<PlayerStats>();
        if (animator == null)
            animator = GetComponent<Animator>();
        if (vfxManager == null)
            vfxManager = GetComponent<VFXManager>();
            
        // Set default moveset
        if (availableMovesets != null && availableMovesets.Length > 0 && currentMoveset == null)
        {
            SetMoveset(availableMovesets[0]);
        }
    }

    void OnDestroy()
    {
        OnMovesetChanged = null;
    }
    
    void Update()
    {
        if (photonView != null && !photonView.IsMine) return;
        
        // Handle moveset-specific input
        if (currentMoveset != null)
        {
            HandleMovesetInput();
        }
    }
    
    public void SetMoveset(MovesetData moveset)
    {
        if (moveset == null) return;
        
        ApplySetMoveset(moveset);
        
        // Sync with other players (buffered for persistence)
        photonView.RPC("RPC_SetMoveset", RpcTarget.AllBuffered, moveset.movesetName);
    }

    private void ApplySetMoveset(MovesetData moveset)
    {
        if (moveset == null) return;

        currentMoveset = moveset;
        
        // Update combat parameters
        if (playerCombat != null)
        {
            playerCombat.attackCooldown = moveset.attackCooldown;
            playerCombat.attackRange = moveset.attackRange;
            playerCombat.attackStaminaCost = moveset.attackStaminaCost;
        }
        
        // Update stats
        if (playerStats != null)
        {
            playerStats.baseDamage = moveset.baseDamage;
            playerStats.baseSpeed = moveset.baseSpeed;
        }
        
        if (enableDebugLogs) Debug.Log($"Moveset changed to: {moveset.movesetName}");
        OnMovesetChanged?.Invoke(moveset);
        UpdateMovesetUI();
    }
    
    [PunRPC]
    public void RPC_SetMoveset(string movesetName)
    {
        MovesetData moveset = GetMovesetByName(movesetName);
        if (moveset != null)
        {
            ApplySetMoveset(moveset);
        }
    }
    
    [Obsolete("Use PowerStealManager.StealPowerFromEnemy(enemyName, position) instead. This method will forward to the central manager.")]
    public void StealPowerFromEnemy(string enemyName)
    {
        if (cachedPowerStealManager == null)
            cachedPowerStealManager = FindFirstObjectByType<PowerStealManager>();
        var psm = cachedPowerStealManager;
        if (psm != null)
        {
            psm.StealPowerFromEnemy(enemyName, transform.position);
        }
        else
        {
            if (enableDebugLogs) Debug.LogWarning("MovesetManager.StealPowerFromEnemy called but no PowerStealManager found in scene.");
        }
    }
    
    private void HandleMovesetInput()
    {
        if (currentMoveset == null) return;
        
        // Handle special moves
        for (int i = 0; i < currentMoveset.specialMoves.Length; i++)
        {
            var move = currentMoveset.specialMoves[i];
            if (move != null && Input.GetKeyDown(move.inputKey))
            {
                ExecuteSpecialMove(move);
            }
        }
    }
    
    private void ExecuteSpecialMove(SpecialMoveData move)
    {
        if (move == null || playerStats == null) return;
        
        // Check stamina cost
        if (!playerStats.UseStamina(move.staminaCost))
        {
            if (enableDebugLogs) Debug.Log("Not enough stamina for special move!");
            return;
        }
        
        // Execute the move
        if (enableDebugLogs) Debug.Log($"Executing special move: {move.moveName}");
        
        // Play animation
        if (animator != null && !string.IsNullOrEmpty(move.animationTrigger))
        {
            animator.SetTrigger(move.animationTrigger);
        }
        
        // Play VFX
        if (vfxManager != null && currentMoveset != null)
        {
            vfxManager.PlayMovesetVFX(currentMoveset.movesetName, move.moveName, transform.position, transform.rotation);
        }
        
        // Apply move effects
        ApplyMoveEffects(move);
        
        // Sync with other players (transient; not buffered)
        photonView.RPC("RPC_ExecuteSpecialMove", RpcTarget.All, move.moveName);
    }
    
    [PunRPC]
    public void RPC_ExecuteSpecialMove(string moveName)
    {
        SpecialMoveData move = GetSpecialMoveByName(moveName);
        if (move != null)
        {
            // Play animation
            if (animator != null && !string.IsNullOrEmpty(move.animationTrigger))
            {
                animator.SetTrigger(move.animationTrigger);
            }
        }
    }
    
    private void ApplyMoveEffects(SpecialMoveData move)
    {
        // Apply damage to enemies in range
        if (move.damage > 0 && move.range > 0)
        {
            Collider[] hitEnemies = Physics.OverlapSphere(transform.position, move.range, playerCombat.enemyLayers);

            // dedupe by enemy root object
            var seen = new HashSet<GameObject>();
            foreach (var enemy in hitEnemies)
            {
                var dmg = enemy.GetComponentInParent<IEnemyDamageable>();
                var mb = dmg as MonoBehaviour;
                if (mb != null) seen.Add(mb.gameObject);
            }
            foreach (var go in seen)
            {
                EnemyDamageRelay.Apply(go, move.damage, gameObject);
            }
        }
        
        // Apply self effects
        if (move.healAmount > 0 && playerStats != null)
        {
            playerStats.Heal(move.healAmount);
        }
        
        // Apply buffs/debuffs
        if (move.statusEffects != null)
        {
            foreach (var effect in move.statusEffects)
            {
                ApplyStatusEffect(effect);
            }
        }
    }
    
    private void ApplyStatusEffect(StatusEffectData effect)
    {
        // This would integrate with your existing status effect system
        // For now, just log the effect
        if (enableDebugLogs) Debug.Log($"Applied status effect: {effect.effectType} for {effect.duration} seconds");
    }
    
    public MovesetData GetMovesetByName(string name)
    {
        if (availableMovesets == null) return null;
        
        foreach (var moveset in availableMovesets)
        {
            if (string.Equals(moveset.movesetName, name, StringComparison.OrdinalIgnoreCase))
                return moveset;
        }
        return null;
    }
    
    private SpecialMoveData GetSpecialMoveByName(string name)
    {
        if (currentMoveset == null || currentMoveset.specialMoves == null) return null;
        
        foreach (var move in currentMoveset.specialMoves)
        {
            if (move != null && string.Equals(move.moveName, name, StringComparison.OrdinalIgnoreCase))
                return move;
        }
        return null;
    }
    
    private void UpdateMovesetUI()
    {
        if (currentMoveset != null && movesetNameText != null)
        {
            movesetNameText.text = currentMoveset.movesetName;
        }
    }
    
    // Public getters
    public MovesetData CurrentMoveset => currentMoveset;
}


[System.Serializable]
public class StatusEffectData
{
    public StatusEffectRelay.EffectType effectType;
    public float magnitude;
    public float duration;
}
