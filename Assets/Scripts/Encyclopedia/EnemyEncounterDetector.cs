using UnityEngine;
using Photon.Pun;

/// <summary>
/// Detects when a player first encounters an enemy and triggers the first encounter dialogue.
/// Attach this to enemy GameObjects.
/// </summary>
public class EnemyEncounterDetector : MonoBehaviourPun
{
    [Header("Detection Settings")]
    [Tooltip("Range at which enemy is considered 'encountered'")]
    public float encounterRange = 15f;
    [Tooltip("Require line of sight for encounter")]
    public bool requireLineOfSight = true;
    [Tooltip("Layer mask for line of sight checks")]
    public LayerMask lineOfSightLayers = -1;
    
    [Header("Enemy Info")]
    [Tooltip("Enemy ID (should match EncyclopediaEntry.enemyId). If empty, uses EnemyData.enemyName")]
    public string enemyIdOverride = "";
    
    private BaseEnemyAI enemyAI;
    private EnemyData enemyData;
    private bool hasBeenEncountered = false;
    private Transform playerTransform;
    
    void Start()
    {
        enemyAI = GetComponent<BaseEnemyAI>();
        if (enemyAI != null)
            enemyData = enemyAI.enemyData;
        
        // Find local player
        FindLocalPlayer();
    }
    
    void Update()
    {
        // Run on every client and evaluate against that client's local player.
        // Encounter progression is intentionally per-player and should not depend
        // on enemy PhotonView ownership.
        if (hasBeenEncountered) return;
        if (EncyclopediaManager.Instance == null) return;
        
        // Find player if not found
        if (playerTransform == null)
        {
            FindLocalPlayer();
            if (playerTransform == null) return;
        }
        
        // Check distance
        float distance = Vector3.Distance(transform.position, playerTransform.position);
        if (distance > encounterRange) return;
        
        // Check line of sight if required
        if (requireLineOfSight)
        {
            Vector3 direction = (playerTransform.position - transform.position).normalized;
            float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            RaycastHit hit;
            
            if (Physics.Raycast(transform.position + Vector3.up, direction, out hit, distanceToPlayer, lineOfSightLayers))
            {
                // Check if we hit the player
                if (hit.collider.transform != playerTransform && !hit.collider.transform.IsChildOf(playerTransform))
                {
                    return; // Something is blocking line of sight
                }
            }
        }
        
        // Enemy encountered!
        RegisterEncounter();
    }
    
    private void FindLocalPlayer()
    {
        playerTransform = PlayerRegistry.GetLocalPlayerTransform();
    }
    
    private void RegisterEncounter()
    {
        hasBeenEncountered = true;
        
        string enemyId = GetEnemyId();
        if (string.IsNullOrEmpty(enemyId))
        {
            Debug.LogWarning($"[EnemyEncounterDetector] No enemy ID found for {gameObject.name}");
            return;
        }
        
        // Register encounter with encyclopedia manager
        EncyclopediaProgressEvents.ReportEncounter(enemyId);
    }
    
    private string GetEnemyId()
    {
        // Use override if set
        if (!string.IsNullOrEmpty(enemyIdOverride))
            return enemyIdOverride;
        
        // Try to get from EnemyData
        if (enemyData != null && !string.IsNullOrEmpty(enemyData.enemyName))
            return enemyData.enemyName;
        
        // Fallback to game object name
        return gameObject.name;
    }
    
    void OnDrawGizmosSelected()
    {
        // Draw encounter range
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, encounterRange);
    }
}

