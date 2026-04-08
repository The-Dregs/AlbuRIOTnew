using UnityEngine;

[CreateAssetMenu(fileName = "New Moveset", menuName = "AlbuRIOT/Moveset Data")]
public class MovesetData : ScriptableObject
{
    [Header("Enemy Reference")]
    public Transform enemyTransform; // assign enemy transform in inspector for VFX spawn
    [Header("Moveset Information")]
    public string movesetName;
    [TextArea] public string description;
    public Sprite movesetIcon;
    
    [Header("Combat Stats")]
    public float attackCooldown = 1.0f;
    public float attackRange = 2.0f;
    public int attackStaminaCost = 20;
    public int baseDamage = 25;
    public float baseSpeed = 6.0f;
    
    [Header("Special Moves")]
    public SpecialMoveData[] specialMoves;
    
    [Header("Animation")]
    public string idleAnimation = "Idle";
    public string walkAnimation = "Walk";
    public string runAnimation = "Run";
    public string attackAnimation = "Attack";
    
    [Header("VFX")]
    public MovesetVFXData[] vfxData;
    
    [Header("Audio")]
    public AudioClip[] moveSounds;
    public AudioClip[] impactSounds;
    
    [Header("Power Stealing")]
    public bool canBeStolen = true;
    public float stealDuration = 30f;
    public string associatedEnemy = "";
}

