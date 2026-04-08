using UnityEngine;

[CreateAssetMenu(fileName = "EnemyData", menuName = "AlbuRIOT/Enemies/Enemy Data")]
public class EnemyData : ScriptableObject
{
    [Header("Info")]
    public EnemyType enemyType = EnemyType.Aswang;
    
    [Header("Basic Stats")]
    public string enemyName = "Enemy";
    public int maxHealth = 100;
    public int basicDamage = 10;
    // Only globalized stats here:
    [Header("Movement/Combat")]
    public float moveSpeed = 3.5f;
    public float chaseSpeed = 2.5f;
    public float patrolSpeed = 2f;
    public float rotationSpeedDegrees = 540f;
    [Tooltip("Faster rotation when in attack range - prevents kiting from behind")]
    public float attackRotationSpeedDegrees = 900f;
    public float backoffSpeedMultiplier = 0.6f;
    [Header("Chase")]
    [Range(0.5f, 1f)] public float desiredAttackDistanceFraction = 0.85f;
    [Header("Combat")]
    public float attackRange = 2.2f;
    public float attackWindup = 0.3f;
    public float attackCooldown = 1.25f;
    public float attackMoveLock = 0.35f;
    public float detectionRange = 12f;
    public float chaseLoseRange = 15f;
    [Header("Detection")]
    public bool requireLineOfSight = true;
    public LayerMask lineOfSightLayers = -1;
    [Min(0f)] public float aggroMemoryDuration = 1f;
    [Min(0f)] public float targetSwitchBuffer = 0.75f;
    [Header("Patrol")]
    public bool enablePatrol = true;
    public float patrolRadius = 8f;
    public float patrolWait = 1.5f;
    [Header("AI Selection")]
    public float specialFacingAngle = 20f;
    public float preferredDistance = 3.0f;
    
    [Header("AI Behavior")]
    [Range(0f, 1f)]
    public float aggressionLevel = 0.5f; // How likely to attack vs patrol
    [Range(0f, 1f)]
    public float intelligenceLevel = 0.5f; // How smart the AI decisions are
    
    [Header("Visual & Audio")]
    public GameObject deathVFXPrefab;
    public GameObject hitVFXPrefab;
    
    [Header("SFX (per-enemy-type defaults)")]
    [Tooltip("Plays when enemy takes damage")]
    public AudioClip hitSFX;
    [Tooltip("Plays when enemy dies")]
    public AudioClip deathSFX;
    [Tooltip("Plays when walking (patrol)")]
    public AudioClip walkSFX;
    [Tooltip("Plays when chasing target")]
    public AudioClip chaseSFX;
    [Tooltip("Plays randomly during idle/patrol/chase")]
    public AudioClip idleSFX;
    [Tooltip("Plays at basic attack windup")]
    public AudioClip attackWindupSFX;
    [Tooltip("Plays at basic attack impact")]
    public AudioClip attackImpactSFX;
    [Tooltip("Plays when enemy spawns")]
    public AudioClip spawnSFX;
    
    [Header("Networking")]
    public bool syncOverNetwork = true;
    public float networkUpdateRate = 10f; // Updates per second
    
    [Header("Debug")]
    public bool showDebugGizmos = false;
    public Color debugColor = Color.red;
}
