using UnityEngine;

[CreateAssetMenu(fileName = "New Special Move", menuName = "AlbuRIOT/Special Move Data")]
public class SpecialMoveData : ScriptableObject
{
    [Header("Move Information")]
    public string moveName;
    [TextArea] public string description;
    public KeyCode inputKey = KeyCode.Q;
    
    [Header("Costs")]
    public int staminaCost = 30;
    public int healthCost = 0;
    
    [Header("Effects")]
    public int damage = 0;
    public float range = 0f;
    public int healAmount = 0;
    public float effectDuration = 0f;
    
    [Header("Animation")]
    public string animationTrigger = "";
    public float animationDuration = 1f;
    
    [Header("Status Effects")]
    public StatusEffectData[] statusEffects;
    public DebuffData[] debuffs;
    
    [Header("VFX")]
    public GameObject vfxPrefab;
    public float vfxDuration = 1f;
    public Vector3 vfxScale = Vector3.one;
    public Color vfxColor = Color.white;
    
    [Header("Audio")]
    public AudioClip castSound;
    public AudioClip impactSound;
    
    [Header("Cooldown")]
    public float cooldown = 5f;
    
    [Header("Targeting")]
    public MoveTargetType targetType = MoveTargetType.Self;
    public LayerMask targetLayers;
}

