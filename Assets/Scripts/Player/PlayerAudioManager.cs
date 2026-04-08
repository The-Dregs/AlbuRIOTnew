using UnityEngine;
using System.Collections;
using Photon.Pun;

/// <summary>
/// Centralized audio manager for all player action sound effects.
/// Handles hit sounds, movement sounds, and attack sounds.
/// Syncs audio to remote players via Photon RPCs.
/// </summary>
public class PlayerAudioManager : MonoBehaviourPun
{
    [Header("Audio Source")]
    [Tooltip("Single audio source for all player sounds")]
    public AudioSource mainAudioSource;
    
    [Header("Hit Sounds")]
    public AudioClip[] hitSounds; // Array of hit sounds (randomly selected)
    [Range(0f, 1f)] public float hitSoundVolume = 1f;
    
    [Header("Movement Sounds")]
    public AudioClip jumpSound;
    public AudioClip landSound;
    public AudioClip rollSound;
    [Range(0f, 1f)] public float movementSoundVolume = 0.7f;
    
    [Header("Running Sounds")]
    public AudioClip[] runningFootstepSounds; // Array of footstep sounds
    [Tooltip("Time between footsteps while running")]
    public float runningFootstepInterval = 0.4f;
    [Range(0f, 1f)] public float footstepVolume = 0.5f;
    
    [Header("Walking Sounds")]
    public AudioClip[] walkingFootstepSounds; // Array of footstep sounds
    [Tooltip("Time between footsteps while walking")]
    public float walkingFootstepInterval = 0.6f;
    
    [Header("Attack Sounds - Unarmed")]
    public AudioClip[] unarmedAttackSounds; // Array of unarmed attack sounds (for combo hits)
    [Range(0f, 1f)] public float unarmedAttackVolume = 0.8f;
    
    [Header("Attack Sounds - Armed")]
    public AudioClip[] armedAttackSounds; // Array of armed attack sounds (for combo hits)
    [Range(0f, 1f)] public float armedAttackVolume = 0.9f;
    
    [Header("Combat Sounds")]
    public AudioClip comboCompleteSound;
    [Range(0f, 1f)] public float combatSoundVolume = 1f;
    
    [Header("Impact Sounds")]
    [Tooltip("SFX when the player bumps into or hits an object (walls, obstacles, etc.)")]
    public AudioClip hitObjectSound;
    [Range(0f, 1f)] public float hitObjectVolume = 0.6f;
    
    [Header("Fade Settings")]
    [Tooltip("Duration of fade-out when leaving walk/run state")]
    [Range(0.05f, 0.5f)] public float movementSfxFadeOutDuration = 0.15f;
    
    private CharacterController characterController;
    private ThirdPersonController playerController;
    private PlayerStats playerStats;
    private PlayerCombat playerCombat;
    private EffectsManager effectsManager;
    private Coroutine movementFadeCoroutine;
    private bool wasMoving = false;
    private float lastFootstepTime = -999f;
    private bool wasRunning = false;
    private float lastHitObjectTime = -999f;
    private const float HitObjectCooldown = 0.35f;
    
    // true only for the local player who owns this object
    private bool isLocalPlayer => photonView != null ? photonView.IsMine : true;
    
    void Awake()
    {
        effectsManager = GetComponent<EffectsManager>();
        if (effectsManager == null) effectsManager = GetComponent<VFXManager>();
        // find audio source on this object if not assigned, but never auto-create
        if (mainAudioSource == null)
        {
            mainAudioSource = GetComponent<AudioSource>();
            if (mainAudioSource == null)
                Debug.LogWarning("PlayerAudioManager: No AudioSource assigned or found. Assign one in the inspector.", this);
        }
        
        // Get components
        characterController = GetComponent<CharacterController>();
        playerController = GetComponent<ThirdPersonController>();
        playerStats = GetComponent<PlayerStats>();
        playerCombat = GetComponent<PlayerCombat>();
    }
    
    void Start()
    {
        // Subscribe to events if components exist
        if (playerStats != null)
        {
            // We'll need to hook into TakeDamage - will do via method call
        }
        
        if (playerCombat != null)
        {
            playerCombat.OnComboHit += OnComboHit;
            playerCombat.OnComboComplete += OnComboComplete;
        }
    }
    
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!isLocalPlayer) return;
        if (mainAudioSource == null) return;
        if (hit.collider == null || hit.collider.isTrigger) return;
        // Skip ground - we only want impact when bumping into walls/obstacles
        if (hit.normal.y > 0.85f) return;
        // If no impact clip is assigned, do nothing on collision
        if (hitObjectSound == null) return;
        float t = Time.time;
        if (t - lastHitObjectTime < HitObjectCooldown) return;
        lastHitObjectTime = t;
        // Only play the impact/bump sound, never the combat hit sound
        PlayHitObjectSound();
    }
    
    void Update()
    {
        // only the local player drives audio timing; remotes receive RPCs
        if (!isLocalPlayer) return;
        if (playerController == null || characterController == null) return;
        
        bool isRunning = playerController.IsRunning;
        Vector3 v = characterController.velocity;
        bool isMoving = (v.x * v.x + v.z * v.z) > 0.01f;
        bool isGrounded = characterController.isGrounded;
        float t = Time.time;
        
        bool shouldWalk = !isRunning && isMoving && isGrounded;
        bool shouldRun = isRunning && isMoving && isGrounded;
        
        if (shouldRun)
        {
            float interval = effectsManager != null ? effectsManager.footstepIntervalRun : runningFootstepInterval;
            if (t - lastFootstepTime >= interval)
            {
                lastFootstepTime = t;
                PlayFootstepLocal(true);
                SendFootstepRPC(true);
            }
            wasMoving = true;
            wasRunning = true;
        }
        else if (shouldWalk)
        {
            float interval = effectsManager != null ? effectsManager.footstepIntervalWalk : walkingFootstepInterval;
            if (t - lastFootstepTime >= interval)
            {
                lastFootstepTime = t;
                PlayFootstepLocal(false);
                SendFootstepRPC(false);
            }
            wasMoving = true;
            wasRunning = false;
        }
        else
        {
            // not moving or not grounded - stop movement sfx
            if (wasMoving)
            {
                StopMovementSfx();
                wasMoving = false;
            }
            lastFootstepTime = -999f;
        }
    }
    
    private void PlayFootstepLocal(bool isRunning)
    {
        if (effectsManager != null)
            effectsManager.PlayFootstepSound(isRunning);
        else
            PlayMovementSfx(isRunning);
    }
    
    // plays footsteps on the main audio source channel (not PlayOneShot)
    // so it can be stopped/faded properly when movement ends
    private void PlayMovementSfx(bool isRunning)
    {
        if (mainAudioSource == null) return;
        
        AudioClip[] clips = isRunning ? runningFootstepSounds : walkingFootstepSounds;
        if (clips == null || clips.Length == 0) return;
        
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;
        
        // cancel any fade in progress
        if (movementFadeCoroutine != null)
        {
            StopCoroutine(movementFadeCoroutine);
            movementFadeCoroutine = null;
        }
        
        mainAudioSource.clip = clip;
        mainAudioSource.volume = footstepVolume;
        mainAudioSource.loop = false;
        mainAudioSource.Play();
    }
    
    // fade out and stop movement sfx, matching enemy AI pattern
    private void StopMovementSfx()
    {
        if (mainAudioSource == null) return;
        if (mainAudioSource.isPlaying && mainAudioSource.clip != null)
        {
            if (movementFadeCoroutine != null)
                StopCoroutine(movementFadeCoroutine);
            movementFadeCoroutine = StartCoroutine(CoFadeOutMovementSfx());
        }
    }
    
    private IEnumerator CoFadeOutMovementSfx()
    {
        if (mainAudioSource == null) { movementFadeCoroutine = null; yield break; }
        float duration = Mathf.Max(0.05f, movementSfxFadeOutDuration);
        float startVol = mainAudioSource.volume;
        float elapsed = 0f;
        while (elapsed < duration && mainAudioSource != null)
        {
            elapsed += Time.deltaTime;
            mainAudioSource.volume = Mathf.Lerp(startVol, 0f, elapsed / duration);
            yield return null;
        }
        if (mainAudioSource != null)
        {
            mainAudioSource.Stop();
            mainAudioSource.clip = null;
            mainAudioSource.volume = 1f;
        }
        movementFadeCoroutine = null;
    }
    
    public void PlayHitSound()
    {
        if (mainAudioSource == null || hitSounds == null || hitSounds.Length == 0) return;
        
        int idx = Random.Range(0, hitSounds.Length);
        PlayHitSoundLocal(idx);
        if (isLocalPlayer && PhotonNetwork.IsConnected)
            photonView.RPC(nameof(RPC_PlayHitSound), RpcTarget.Others, idx);
    }
    
    private void PlayHitSoundLocal(int idx)
    {
        if (mainAudioSource == null || hitSounds == null || idx < 0 || idx >= hitSounds.Length) return;
        AudioClip clip = hitSounds[idx];
        if (clip != null)
            mainAudioSource.PlayOneShot(clip, hitSoundVolume);
    }
    
    public void PlayJumpSound()
    {
        if (mainAudioSource == null || jumpSound == null) return;
        mainAudioSource.PlayOneShot(jumpSound, movementSoundVolume);
        if (isLocalPlayer && PhotonNetwork.IsConnected)
            photonView.RPC(nameof(RPC_PlayJumpSound), RpcTarget.Others);
    }
    
    public void PlayLandSound()
    {
        if (mainAudioSource == null || landSound == null) return;
        mainAudioSource.PlayOneShot(landSound, movementSoundVolume);
        if (isLocalPlayer && PhotonNetwork.IsConnected)
            photonView.RPC(nameof(RPC_PlayLandSound), RpcTarget.Others);
    }
    
    public void PlayRollSound()
    {
        if (mainAudioSource == null || rollSound == null) return;
        mainAudioSource.PlayOneShot(rollSound, movementSoundVolume);
        if (isLocalPlayer && PhotonNetwork.IsConnected)
            photonView.RPC(nameof(RPC_PlayRollSound), RpcTarget.Others);
    }
    
    // kept for external callers
    public void PlayFootstepSound(bool isRunning)
    {
        PlayMovementSfx(isRunning);
    }
    
    public void PlayAttackSound(bool isArmed, int comboIndex)
    {
        if (mainAudioSource == null) return;
        
        PlayAttackSoundLocal(isArmed);
        if (isLocalPlayer && PhotonNetwork.IsConnected)
            photonView.RPC(nameof(RPC_PlayAttackSound), RpcTarget.Others, isArmed);
    }
    
    private void PlayAttackSoundLocal(bool isArmed)
    {
        if (mainAudioSource == null) return;
        AudioClip[] clips = isArmed ? armedAttackSounds : unarmedAttackSounds;
        if (clips == null || clips.Length == 0) return;
        
        float volume = isArmed ? armedAttackVolume : unarmedAttackVolume;
        foreach (AudioClip clip in clips)
        {
            if (clip != null)
                mainAudioSource.PlayOneShot(clip, volume);
        }
    }
    
    public void PlayComboCompleteSound()
    {
        if (mainAudioSource == null || comboCompleteSound == null) return;
        mainAudioSource.PlayOneShot(comboCompleteSound, combatSoundVolume);
        if (isLocalPlayer && PhotonNetwork.IsConnected)
            photonView.RPC(nameof(RPC_PlayComboComplete), RpcTarget.Others);
    }
    
    /// <summary>
    /// Plays the hit-object SFX when the player bumps into something.
    /// Called from OnControllerColliderHit.
    /// </summary>
    public void PlayHitObjectSound()
    {
        if (mainAudioSource == null || hitObjectSound == null) return;
        mainAudioSource.PlayOneShot(hitObjectSound, hitObjectVolume);
        if (isLocalPlayer && PhotonNetwork.IsConnected)
            photonView.RPC(nameof(RPC_PlayHitObjectSound), RpcTarget.Others);
    }
    
    private void OnComboHit(int comboCount)
    {
        if (playerCombat == null) return;
        bool armed = playerCombat.IsArmed;
        if (effectsManager != null)
            effectsManager.PlayAttackSound(armed, comboCount - 1);
        else
            PlayAttackSound(armed, comboCount - 1);
    }
    
    private void OnComboComplete()
    {
        if (effectsManager != null)
            effectsManager.PlayComboCompleteSound();
        else
            PlayComboCompleteSound();
    }
    
    #region Photon RPCs
    
    private void SendFootstepRPC(bool isRunning)
    {
        if (!PhotonNetwork.IsConnected || photonView == null) return;
        photonView.RPC(nameof(RPC_PlayFootstep), RpcTarget.Others, isRunning);
    }
    
    [PunRPC]
    private void RPC_PlayFootstep(bool isRunning)
    {
        PlayFootstepLocal(isRunning);
    }
    
    [PunRPC]
    private void RPC_PlayHitSound(int idx)
    {
        PlayHitSoundLocal(idx);
    }
    
    [PunRPC]
    private void RPC_PlayJumpSound()
    {
        if (mainAudioSource != null && jumpSound != null)
            mainAudioSource.PlayOneShot(jumpSound, movementSoundVolume);
    }
    
    [PunRPC]
    private void RPC_PlayLandSound()
    {
        if (mainAudioSource != null && landSound != null)
            mainAudioSource.PlayOneShot(landSound, movementSoundVolume);
    }
    
    [PunRPC]
    private void RPC_PlayRollSound()
    {
        if (mainAudioSource != null && rollSound != null)
            mainAudioSource.PlayOneShot(rollSound, movementSoundVolume);
    }
    
    [PunRPC]
    private void RPC_PlayAttackSound(bool isArmed)
    {
        PlayAttackSoundLocal(isArmed);
    }
    
    [PunRPC]
    private void RPC_PlayComboComplete()
    {
        if (mainAudioSource != null && comboCompleteSound != null)
            mainAudioSource.PlayOneShot(comboCompleteSound, combatSoundVolume);
    }
    
    [PunRPC]
    private void RPC_PlayHitObjectSound()
    {
        if (mainAudioSource != null && hitObjectSound != null)
            mainAudioSource.PlayOneShot(hitObjectSound, hitObjectVolume);
    }
    
    #endregion
    
    void OnDestroy()
    {
        if (mainAudioSource != null)
            mainAudioSource.Stop();
        
        // Unsubscribe from events
        if (playerCombat != null)
        {
            playerCombat.OnComboHit -= OnComboHit;
            playerCombat.OnComboComplete -= OnComboComplete;
        }
    }
}

