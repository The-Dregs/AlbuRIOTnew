using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;

public class PlayerStatsUI : MonoBehaviourPun
{
    public PlayerStats playerStats;
    public Slider healthSlider;
    public Slider staminaSlider;
    [Header("cooldown ui (optional)")]
    public Slider attackCooldownSlider;
    [Header("debug text ui (UGUI Text)")]
    public Text speedText;
    public Text attackingText;
    public Text staminaRegenText;
    public Text staminaDelayText;
    public Text healthRegenText;
    public Text healthDelayText;

    [Header("debug text ui (TMP Text)")]
    public TMP_Text speedTMP;
    public TMP_Text attackingTMPTxt;
    public TMP_Text staminaRegenTMP;
    public TMP_Text staminaDelayTMP;
    public TMP_Text healthRegenTMP;
    public TMP_Text healthDelayTMP;

    private ThirdPersonController controller;
    private PlayerCombat combat;
    private PhotonView targetPV; // photon view of the bound player (not of this UI)
    [SerializeField, Range(0.1f, 2f)] private float bindingRetryInterval = 0.5f;
    [SerializeField, Range(0.02f, 0.5f)] private float statsRefreshInterval = 0.05f;
    [SerializeField, Range(0.05f, 1f)] private float debugTextRefreshInterval = 0.15f;
    private float nextBindingRetryTime = 0f;
    private float nextStatsRefreshTime = 0f;
    private float nextDebugTextRefreshTime = 0f;
    private int lastHealth = -1;
    private int lastMaxHealth = -1;
    private int lastStamina = -1;
    private int lastMaxStamina = -1;
    private float lastAttackCooldown = -1f;

    void Start()
    {
        // try direct parent first (for per-player HUD under the player)
        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();
        // if still null or bound to a remote player, resolve to the local player's stats
        ResolveBindingIfNeeded();
        controller = playerStats != null ? playerStats.GetComponent<ThirdPersonController>() : null;
        combat = playerStats != null ? playerStats.GetComponent<PlayerCombat>() : null;
    }

    void Update()
    {
        // if we have a player target and it's not ours, do not drive UI (prevents remote players affecting our HUD)
        if (targetPV != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !targetPV.IsMine)
        {
            return;
        }

        // if binding is missing or lost (e.g., scene spawn order), try to resolve again
        if ((playerStats == null || targetPV == null) && Time.time >= nextBindingRetryTime)
        {
            ResolveBindingIfNeeded();
            controller = playerStats != null ? playerStats.GetComponent<ThirdPersonController>() : controller;
            combat = playerStats != null ? playerStats.GetComponent<PlayerCombat>() : combat;
            nextBindingRetryTime = Time.time + Mathf.Max(0.1f, bindingRetryInterval);
        }

        if (playerStats != null)
        {
            if (Time.time >= nextStatsRefreshTime)
            {
                nextStatsRefreshTime = Time.time + Mathf.Max(0.02f, statsRefreshInterval);
                if (healthSlider != null && playerStats.maxHealth > 0)
                {
                    if (lastMaxHealth != playerStats.maxHealth)
                    {
                        healthSlider.maxValue = playerStats.maxHealth;
                        lastMaxHealth = playerStats.maxHealth;
                    }
                    if (lastHealth != playerStats.currentHealth)
                    {
                        healthSlider.SetValueWithoutNotify(playerStats.currentHealth);
                        lastHealth = playerStats.currentHealth;
                    }
                }
                if (staminaSlider != null && playerStats.maxStamina > 0)
                {
                    if (lastMaxStamina != playerStats.maxStamina)
                    {
                        staminaSlider.maxValue = playerStats.maxStamina;
                        lastMaxStamina = playerStats.maxStamina;
                    }
                    if (lastStamina != playerStats.currentStamina)
                    {
                        staminaSlider.SetValueWithoutNotify(playerStats.currentStamina);
                        lastStamina = playerStats.currentStamina;
                    }
                }

                if (combat != null && attackCooldownSlider != null)
                {
                    float cd = combat.AttackCooldownProgress;
                    if (!Mathf.Approximately(lastAttackCooldown, cd))
                    {
                        attackCooldownSlider.SetValueWithoutNotify(cd);
                        lastAttackCooldown = cd;
                    }
                }
            }

            if (Time.time >= nextDebugTextRefreshTime)
            {
                nextDebugTextRefreshTime = Time.time + Mathf.Max(0.05f, debugTextRefreshInterval);
                if (controller != null)
                {
                    var charCtrl = controller.GetComponent<CharacterController>();
                    float speed = charCtrl != null ? new Vector3(charCtrl.velocity.x, 0f, charCtrl.velocity.z).magnitude : 0f;
                    string speedLabel = $"speed: {speed:F2}";
                    if (speedText != null && !string.Equals(speedText.text, speedLabel)) speedText.text = speedLabel;
                    if (speedTMP != null && !string.Equals(speedTMP.text, speedLabel)) speedTMP.text = speedLabel;
                }

                if (combat != null)
                {
                    string atk = combat.IsAttacking ? "attacking: yes" : "attacking: no";
                    if (attackingText != null && !string.Equals(attackingText.text, atk)) attackingText.text = atk;
                    if (attackingTMPTxt != null && !string.Equals(attackingTMPTxt.text, atk)) attackingTMPTxt.text = atk;
                }
                {
                    string regenState = playerStats.IsStaminaRegenerating ? "regenerating" : (playerStats.IsStaminaRegenBlocked ? "blocked" : (playerStats.currentStamina >= playerStats.maxStamina ? "full" : "waiting"));
                    string value = $"stamina: {regenState}";
                    if (staminaRegenText != null && !string.Equals(staminaRegenText.text, value)) staminaRegenText.text = value;
                    if (staminaRegenTMP != null && !string.Equals(staminaRegenTMP.text, value)) staminaRegenTMP.text = value;
                }
                {
                    string delay = $"regen delay: {playerStats.StaminaRegenDelayRemaining:F2}s";
                    if (staminaDelayText != null && !string.Equals(staminaDelayText.text, delay)) staminaDelayText.text = delay;
                    if (staminaDelayTMP != null && !string.Equals(staminaDelayTMP.text, delay)) staminaDelayTMP.text = delay;
                }
                // optional: health regen state
                {
                    string regenState = playerStats.IsHealthRegenerating ? "regenerating" : (playerStats.currentHealth >= playerStats.maxHealth ? "full" : (playerStats.HealthRegenDelayRemaining > 0f ? "waiting" : "paused"));
                    string value = $"health: {regenState}";
                    if (healthRegenText != null && !string.Equals(healthRegenText.text, value)) healthRegenText.text = value;
                    if (healthRegenTMP != null && !string.Equals(healthRegenTMP.text, value)) healthRegenTMP.text = value;
                }
                {
                    string delay = $"health delay: {playerStats.HealthRegenDelayRemaining:F2}s";
                    if (healthDelayText != null && !string.Equals(healthDelayText.text, delay)) healthDelayText.text = delay;
                    if (healthDelayTMP != null && !string.Equals(healthDelayTMP.text, delay)) healthDelayTMP.text = delay;
                }
            }
        }
    }

    private void ResolveBindingIfNeeded()
    {
        // prefer already assigned but ensure it belongs to the local player when networked
        if (playerStats != null)
        {
            targetPV = playerStats.GetComponent<PhotonView>();
            if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && targetPV != null && !targetPV.IsMine)
            {
                // assigned stats belong to a remote player; rebind to local
                playerStats = null;
                targetPV = null;
            }
        }

        if (playerStats == null)
        {
            // find local player's stats (works in both offline mode and connected rooms)
            var localT = PlayerRegistry.GetLocalPlayerTransform();
            PlayerStats local = localT != null ? localT.GetComponent<PlayerStats>() : null;
            if (local != null)
            {
                playerStats = local;
                targetPV = playerStats.GetComponent<PhotonView>();
            }
        }
    }
}
