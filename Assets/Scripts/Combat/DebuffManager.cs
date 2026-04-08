using UnityEngine;
using Photon.Pun;
using System;
using System.Collections.Generic;

public class DebuffManager : MonoBehaviourPun
{
    [Header("Debuff Configuration")]
    public float debuffTickInterval = 0.5f;
    
    [Header("Visual Effects")]
    public GameObject debuffVFXPrefab;
    public Transform vfxSpawnPoint;
    
    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip[] debuffSounds;
    
    // Active debuffs
    private List<ActiveDebuff> activeDebuffs = new List<ActiveDebuff>();
    
    // Events
    public System.Action<DebuffData> OnDebuffApplied;
    public System.Action<DebuffData> OnDebuffRemoved;
    public System.Action<DebuffData> OnDebuffTick;
    
    // Components
    private PlayerStats playerStats;
    private MovesetManager movesetManager;
    
    void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        movesetManager = GetComponent<MovesetManager>();
        
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }
    
    void Update()
    {
        if (photonView != null && !photonView.IsMine) return;
        
        TickDebuffs();
    }
    
    public void ApplyDebuff(DebuffData debuffData, float duration, float magnitude = 1f)
    {
        if (debuffData == null) return;
        
        // Check if debuff already exists
        ActiveDebuff existingDebuff = GetActiveDebuff(debuffData.debuffType);
        
        if (existingDebuff != null)
        {
            // Refresh duration or stack
            if (debuffData.canStack)
            {
                existingDebuff.magnitude += magnitude;
                existingDebuff.duration = Mathf.Max(existingDebuff.duration, duration);
            }
            else
            {
                existingDebuff.duration = Mathf.Max(existingDebuff.duration, duration);
                existingDebuff.magnitude = Mathf.Max(existingDebuff.magnitude, magnitude);
            }
        }
        else
        {
            // Add new debuff
            ActiveDebuff newDebuff = new ActiveDebuff
            {
                debuffData = debuffData,
                duration = duration,
                magnitude = magnitude,
                timeRemaining = duration,
                lastTickTime = Time.time
            };
            
            activeDebuffs.Add(newDebuff);
        }
        
        // Apply immediate effects
        ApplyDebuffEffects(debuffData, magnitude);
        
        // Play VFX and audio
        PlayDebuffVFX(debuffData);
        PlayDebuffSound(debuffData);
        
        Debug.Log($"Debuff applied: {debuffData.debuffName} for {duration}s");
        OnDebuffApplied?.Invoke(debuffData);
        
        // Sync with other players
        if (photonView != null && photonView.IsMine)
        {
            photonView.RPC("RPC_ApplyDebuff", RpcTarget.Others, debuffData.debuffName, duration, magnitude);
        }
    }
    
    [PunRPC]
    public void RPC_ApplyDebuff(string debuffName, float duration, float magnitude)
    {
        if (photonView != null && !photonView.IsMine) return;
        
        DebuffData debuffData = GetDebuffDataByName(debuffName);
        if (debuffData != null)
        {
            ApplyDebuff(debuffData, duration, magnitude);
        }
    }
    
    private void TickDebuffs()
    {
        for (int i = activeDebuffs.Count - 1; i >= 0; i--)
        {
            ActiveDebuff debuff = activeDebuffs[i];
            
            // Update time remaining
            debuff.timeRemaining -= Time.deltaTime;
            
            // Check for tick damage/effects
            if (Time.time - debuff.lastTickTime >= debuffTickInterval)
            {
                ApplyDebuffTick(debuff);
                debuff.lastTickTime = Time.time;
            }
            
            // Remove expired debuffs
            if (debuff.timeRemaining <= 0f)
            {
                RemoveDebuff(debuff);
            }
        }
    }
    
    private void ApplyDebuffEffects(DebuffData debuffData, float magnitude)
    {
        if (playerStats == null) return;
        
        switch (debuffData.debuffType)
        {
            case DebuffType.Slow:
                playerStats.slowPercent = Mathf.Max(playerStats.slowPercent, debuffData.effectMagnitude * magnitude);
                break;
                
            case DebuffType.Root:
                playerStats.rootRemaining = Mathf.Max(playerStats.rootRemaining, debuffData.effectMagnitude * magnitude);
                break;
                
            case DebuffType.Silence:
                playerStats.silenceRemaining = Mathf.Max(playerStats.silenceRemaining, debuffData.effectMagnitude * magnitude);
                break;
                
            case DebuffType.Stun:
                playerStats.stunRemaining = Mathf.Max(playerStats.stunRemaining, debuffData.effectMagnitude * magnitude);
                break;
                
            case DebuffType.DefenseDown:
                playerStats.defenseDownBonus = Mathf.Max(playerStats.defenseDownBonus, debuffData.effectMagnitude * magnitude);
                break;
                
            case DebuffType.Bleed:
                playerStats.bleedPerTick = Mathf.Max(playerStats.bleedPerTick, debuffData.effectMagnitude * magnitude);
                playerStats.bleedRemaining = Mathf.Max(playerStats.bleedRemaining, debuffData.effectMagnitude * magnitude);
                break;
                
            case DebuffType.StaminaBurn:
                playerStats.staminaBurnPerTick = Mathf.Max(playerStats.staminaBurnPerTick, debuffData.effectMagnitude * magnitude);
                playerStats.staminaBurnRemaining = Mathf.Max(playerStats.staminaBurnRemaining, debuffData.effectMagnitude * magnitude);
                break;
                
            case DebuffType.Poison:
                // Custom poison effect
                break;
                
            case DebuffType.Curse:
                // Custom curse effect
                break;
        }
    }
    
    private void ApplyDebuffTick(ActiveDebuff debuff)
    {
        if (playerStats == null) return;
        
        switch (debuff.debuffData.debuffType)
        {
            case DebuffType.Bleed:
                int bleedDamage = Mathf.RoundToInt(debuff.debuffData.tickDamage * debuff.magnitude);
                DamageRelay.ApplyToPlayer(playerStats.gameObject, bleedDamage);
                break;
                
            case DebuffType.StaminaBurn:
                int staminaBurn = Mathf.RoundToInt(debuff.debuffData.tickDamage * debuff.magnitude);
                playerStats.UseStamina(staminaBurn);
                break;
                
            case DebuffType.Poison:
                int poisonDamage = Mathf.RoundToInt(debuff.debuffData.tickDamage * debuff.magnitude);
                DamageRelay.ApplyToPlayer(playerStats.gameObject, poisonDamage);
                break;
        }
        
        OnDebuffTick?.Invoke(debuff.debuffData);
    }
    
    private void RemoveDebuff(ActiveDebuff debuff)
    {
        activeDebuffs.Remove(debuff);
        
        // Remove effects
        RemoveDebuffEffects(debuff.debuffData);
        
        Debug.Log($"Debuff removed: {debuff.debuffData.debuffName}");
        OnDebuffRemoved?.Invoke(debuff.debuffData);
    }
    
    private void RemoveDebuffEffects(DebuffData debuffData)
    {
        if (playerStats == null) return;
        
        switch (debuffData.debuffType)
        {
            case DebuffType.Slow:
                playerStats.slowPercent = 0f;
                break;
                
            case DebuffType.DefenseDown:
                playerStats.defenseDownBonus = 0f;
                break;
                
            case DebuffType.Bleed:
                playerStats.bleedPerTick = 0f;
                playerStats.bleedRemaining = 0f;
                break;
                
            case DebuffType.StaminaBurn:
                playerStats.staminaBurnPerTick = 0f;
                playerStats.staminaBurnRemaining = 0f;
                break;
        }
    }
    
    private void PlayDebuffVFX(DebuffData debuffData)
    {
        if (debuffData.vfxPrefab != null && vfxSpawnPoint != null)
        {
            GameObject vfx = Instantiate(debuffData.vfxPrefab, vfxSpawnPoint.position, vfxSpawnPoint.rotation);
            
            // Auto-destroy VFX after duration
            if (debuffData.vfxDuration > 0f)
            {
                Destroy(vfx, debuffData.vfxDuration);
            }
        }
    }
    
    private void PlayDebuffSound(DebuffData debuffData)
    {
        if (audioSource != null && debuffData.soundEffect != null)
        {
            audioSource.PlayOneShot(debuffData.soundEffect);
        }
    }
    
    private ActiveDebuff GetActiveDebuff(DebuffType type)
    {
        return activeDebuffs.Find(d => d.debuffData.debuffType == type);
    }
    
    private DebuffData GetDebuffDataByName(string name)
    {
        // This would integrate with a DebuffManager or database
        // For now, return null - you'll need to implement this
        return null;
    }
    
    // Public getters
    public List<ActiveDebuff> GetActiveDebuffs() => activeDebuffs;
    public bool HasDebuff(DebuffType type) => GetActiveDebuff(type) != null;
    public float GetDebuffTimeRemaining(DebuffType type)
    {
        ActiveDebuff debuff = GetActiveDebuff(type);
        return debuff != null ? debuff.timeRemaining : 0f;
    }
    
    // Clear all debuffs
    public void ClearAllDebuffs()
    {
        for (int i = activeDebuffs.Count - 1; i >= 0; i--)
        {
            RemoveDebuff(activeDebuffs[i]);
        }
    }
}

[System.Serializable]
public class ActiveDebuff
{
    public DebuffData debuffData;
    public float duration;
    public float timeRemaining;
    public float magnitude;
    public float lastTickTime;
}

