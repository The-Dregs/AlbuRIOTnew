using UnityEngine;
using Photon.Pun;
using System.Collections;

/// <summary>
/// Handles player resurrection in multiplayer. Other players can interact with downed players to revive them.
/// </summary>
public class PlayerResurrection : MonoBehaviourPun
{
    [Header("Resurrection Settings")]
    [SerializeField] private float resurrectionRange = 2.5f;
    [SerializeField] private float resurrectionDuration = 3f;
    [SerializeField] private LayerMask playerLayer;
    [Tooltip("Visual feedback: show progress bar")]
    [SerializeField] private bool showProgressBar = true;
    [SerializeField, Range(0.05f, 1f)] private float downedScanInterval = 0.15f;
    
    [Header("UI")]
    [SerializeField] private PlayerInteractHUD interactHUD;
    
    [Header("Audio")]
    [Tooltip("Optional sound to play when revival starts")]
    [SerializeField] private AudioClip reviveStartSound;
    [Tooltip("Optional sound to play when revival completes")]
    [SerializeField] private AudioClip reviveCompleteSound;
    [Tooltip("Optional sound to play when revival is cancelled")]
    [SerializeField] private AudioClip reviveCancelSound;
    
    private PlayerStats playerStats;
    private PlayerStats nearbyDownedPlayer;
    private float resurrectionProgress = 0f;
    private bool isResurrecting = false;
    private Coroutine resurrectionCoroutine;
    private AudioSource audioSource;
    private float nextDownedScanTime = 0f;
    private static PlayerStats[] cachedPlayers = System.Array.Empty<PlayerStats>();
    private static float nextPlayersRefreshTime = 0f;
    
    void Awake()
    {
        playerStats = GetComponent<PlayerStats>();
        if (interactHUD == null)
            interactHUD = GetComponentInChildren<PlayerInteractHUD>(true);
        
        if (playerLayer == 0)
            playerLayer = LayerMask.GetMask("Player");
            
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
    }
    
    void Update()
    {
        // Only local player can interact
        if (photonView == null || !photonView.IsMine) return;
        
        // Don't allow resurrection if player is dead or downed themselves
        if (playerStats == null || playerStats.IsDead || playerStats.IsDowned) return;
        
        // Check for nearby downed players at a throttled rate.
        if (Time.time >= nextDownedScanTime)
        {
            CheckForDownedPlayers();
            nextDownedScanTime = Time.time + Mathf.Max(0.05f, downedScanInterval);
        }
        
        // Handle resurrection input
        if (nearbyDownedPlayer != null)
        {
            if (Input.GetKey(KeyCode.E) && !isResurrecting)
            {
                if (resurrectionCoroutine == null)
                {
                    resurrectionCoroutine = StartCoroutine(CoResurrect(nearbyDownedPlayer));
                    if (reviveStartSound != null && audioSource != null)
                        audioSource.PlayOneShot(reviveStartSound);
                }
            }
            else if (!Input.GetKey(KeyCode.E) && isResurrecting)
            {
                CancelResurrection();
            }
        }
        else if (isResurrecting)
        {
            CancelResurrection();
        }
    }
    
    void CheckForDownedPlayers()
    {
        if (Time.time >= nextPlayersRefreshTime || cachedPlayers == null || cachedPlayers.Length == 0)
        {
            cachedPlayers = PlayerRegistry.ToArray();
            nextPlayersRefreshTime = Time.time + Mathf.Max(0.1f, downedScanInterval * 2f);
        }

        PlayerStats[] allPlayers = cachedPlayers;
        PlayerStats closestDowned = null;
        float closestDistSqr = float.MaxValue;
        float rangeSqr = resurrectionRange * resurrectionRange;
        
        foreach (var player in allPlayers)
        {
            if (player == null || player == playerStats) continue;
            if (!player.IsDowned || player.IsDead) continue;
            
            float distSqr = (transform.position - player.transform.position).sqrMagnitude;
            if (distSqr < rangeSqr && distSqr < closestDistSqr)
            {
                closestDistSqr = distSqr;
                closestDowned = player;
            }
        }
        
        if (closestDowned != nearbyDownedPlayer)
        {
            nearbyDownedPlayer = closestDowned;
            
            if (nearbyDownedPlayer != null && interactHUD != null && !isResurrecting)
            {
                interactHUD.Show("Hold E to revive teammate");
            }
            else if (interactHUD != null && !isResurrecting)
            {
                interactHUD.Hide();
            }
        }
    }
    
    private void CancelResurrection()
    {
        if (resurrectionCoroutine != null)
        {
            StopCoroutine(resurrectionCoroutine);
            resurrectionCoroutine = null;
        }
        isResurrecting = false;
        resurrectionProgress = 0f;
        if (interactHUD != null)
            interactHUD.Hide();
        if (reviveCancelSound != null && audioSource != null)
            audioSource.PlayOneShot(reviveCancelSound);
    }
    
    IEnumerator CoResurrect(PlayerStats targetPlayer)
    {
        isResurrecting = true;
        resurrectionProgress = 0f;
        
        while (resurrectionProgress < 1f)
        {
            if (!Input.GetKey(KeyCode.E) || targetPlayer == null || !targetPlayer.IsDowned || targetPlayer.IsDead)
            {
                CancelResurrection();
                yield break;
            }
            
            float distSqr = (transform.position - targetPlayer.transform.position).sqrMagnitude;
            if (distSqr > resurrectionRange * resurrectionRange)
            {
                if (interactHUD != null)
                    interactHUD.Show("Too far away - get closer");
                CancelResurrection();
                yield break;
            }
            
            resurrectionProgress += Time.deltaTime / resurrectionDuration;
            resurrectionProgress = Mathf.Clamp01(resurrectionProgress);
            
            if (interactHUD != null)
            {
                int percent = Mathf.RoundToInt(resurrectionProgress * 100f);
                string progressBar = showProgressBar ? GetProgressBar(percent) : "";
                interactHUD.Show($"Reviving teammate... {percent}%{progressBar}");
            }
            
            yield return null;
        }
        
        // Resurrection complete
        isResurrecting = false;
        resurrectionProgress = 0f;
        
        if (reviveCompleteSound != null && audioSource != null)
            audioSource.PlayOneShot(reviveCompleteSound);
        
        // Send RPC to revive the player
        var targetPv = targetPlayer.GetComponent<PhotonView>();
        if (targetPv != null)
        {
            targetPv.RPC("RPC_Revive", targetPv.Owner);
        }
        
        if (interactHUD != null)
            interactHUD.Hide();
        
        resurrectionCoroutine = null;
    }
    
    private string GetProgressBar(int percent)
    {
        int barLength = 20;
        int filled = Mathf.RoundToInt(barLength * percent / 100f);
        string bar = "[";
        for (int i = 0; i < barLength; i++)
        {
            if (i < filled)
                bar += "=";
            else
                bar += " ";
        }
        bar += "]";
        return "\n" + bar;
    }
}

