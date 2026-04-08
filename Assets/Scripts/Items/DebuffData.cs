using UnityEngine;

[CreateAssetMenu(fileName = "New Debuff", menuName = "AlbuRIOT/Debuff Data")]
public class DebuffData : ScriptableObject
{
    [Header("Debuff Information")]
    public string debuffName;
    [TextArea] public string description;
    public DebuffType debuffType;
    public Sprite icon;
    
    [Header("Effects")]
    public float effectMagnitude = 1f;
    public float tickDamage = 0f;
    public bool canStack = false;
    
    [Header("Visual Effects")]
    public GameObject vfxPrefab;
    public float vfxDuration = 0f;
    
    [Header("Audio")]
    public AudioClip soundEffect;
    
    [Header("Resistance")]
    public DebuffType[] resistedBy;
    public float resistanceChance = 0f;
}

public enum DebuffType
{
    Slow,
    Root,
    Silence,
    Stun,
    DefenseDown,
    Bleed,
    StaminaBurn,
    Poison,
    Curse,
    Custom
}

