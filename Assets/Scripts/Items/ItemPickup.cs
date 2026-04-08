using UnityEngine;
using Photon.Pun;

public class ItemPickup : MonoBehaviourPun
{
    [Header("Item Configuration")]
    public ItemData itemData;
    public int quantity = 1;
    
    [Header("Visual Effects")]
    public GameObject pickupEffect;
    public float rotationSpeed = 50f;
    public float bobSpeed = 2f;
    public float bobHeight = 0.5f;
    
    [Header("Outline/Highlight")]
    [Tooltip("Distance at which outline appears when player is nearby")]
    [SerializeField] private float outlineProximityDistance = 5f;
    [Tooltip("Color of outline when item is pickable (yellow)")]
    [SerializeField] private Color outlineColor = new Color(1f, 1f, 0f, 1f);
    [Tooltip("Base intensity of the outline emission (this is the maximum possible)")]
    [SerializeField] private float outlineIntensity = 2f;
    [Tooltip("Smooth transition speed for outline appearance")]
    [SerializeField] private float outlineTransitionSpeed = 5f;
    [Tooltip("Pulsation speed (cycles per second)")]
    [SerializeField] private float pulsationSpeed = 1f;
    [Tooltip("Maximum intensity multiplier for pulsation (0.15 = 15% max, never reaches full brightness)")]
    [SerializeField] private float maxPulsationIntensity = 0.15f;

    [Header("Performance")]
    [SerializeField, Range(0.05f, 2f)] private float proximityCheckInterval = 0.2f;
    [SerializeField, Range(0.1f, 2f)] private float sharedPlayerRefreshInterval = 0.5f;
    [SerializeField] private bool enableDebugLogs = false;
    
    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip pickupSound;
    
    private Vector3 startPosition;
    private bool isPickedUp = false;
    
    // Outline system
    private Renderer[] itemRenderers;
    private Material[] originalMaterials;
    private Material[] outlineMaterials;
    private bool playerInRange = false;
    private float currentOutlineIntensity = 0f;
    private float pulsationTime = 0f;
    private int playersInRange = 0;
    private float nextProximityCheckTime = 0f;
    private static Transform cachedLocalPlayer;
    private static float nextSharedPlayerRefreshTime = 0f;
    
    // Events
    public System.Action<ItemPickup> OnPickedUp;

    private void LogVerbose(string message)
    {
        if (enableDebugLogs) Debug.Log(message);
    }
    
    void Start()
    {
        startPosition = transform.position;
        
        // Set up visual representation
        SetupVisuals();
        
        // Auto-find audio source
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        
        // Setup outline materials
        SetupOutlineMaterials();
    }
    
    void Update()
    {
        if (isPickedUp) return;
        
        // Rotate the item
        transform.Rotate(0, rotationSpeed * Time.deltaTime, 0);
        
        // Bob up and down
        float bobOffset = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = startPosition + Vector3.up * bobOffset;
        
        // Update outline proximity at a lower frequency to reduce CPU cost with many pickups.
        if (Time.time >= nextProximityCheckTime)
        {
            CheckPlayerProximity();
            nextProximityCheckTime = Time.time + Mathf.Max(0.05f, proximityCheckInterval);
        }

        if (!playerInRange && currentOutlineIntensity <= 0.001f)
            return;

        UpdateOutlineIntensity();
    }
    
    void OnDestroy()
    {
        CleanupOutlineMaterials();
    }
    
    private void SetupVisuals()
    {
        if (itemData != null && itemData.icon != null)
        {
            // Set up sprite renderer or mesh renderer based on your setup
            var spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.sprite = itemData.icon;
            }
        }
    }
    
    public void SetItem(ItemData item, int qty)
    {
        itemData = item;
        quantity = qty;
        SetupVisuals();
    }
    
    // (Remove/Comment) void OnTriggerEnter(Collider other)
    // The pickup logic will be triggered explicitly from PlayerPickupInteractor.cs (E key)
    
    private void PickupItem(GameObject player)
    {
        if (isPickedUp || itemData == null) return;
        
        LogVerbose($"[ItemPickup] PickupItem called for {itemData.itemName} by {player.name}");
        
        // Get player's inventory
        var inventory = player.GetComponent<Inventory>();
        if (inventory == null)
        {
            Debug.LogWarning("Player doesn't have an inventory component!");
            return;
        }
        
        // Try to add item to inventory
        if (inventory.AddItem(itemData, quantity))
        {
            LogVerbose($"[ItemPickup] Successfully added {itemData.itemName} x{quantity} to inventory");
            
            // Play pickup sound locally
            PlayPickupSound();
            
            // Update quest progress locally
            UpdateQuestProgress();
            
            // Notify listeners locally
            OnPickedUp?.Invoke(this);
            
            // Network pickup: MasterClient handles sync and destruction
            bool isNetworked = photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom;
            
            if (isNetworked)
            {
                if (PhotonNetwork.IsMasterClient)
                {
                    LogVerbose($"[ItemPickup] MasterClient processing pickup for {itemData.itemName}");
                    ProcessPickupOnMaster();
                }
                else
                {
                    int playerViewId = -1;
                    var playerPv = player.GetComponent<PhotonView>();
                    if (playerPv != null) playerViewId = playerPv.ViewID;
                    
                    LogVerbose($"[ItemPickup] Non-Master sending RPC_RequestPickup to MasterClient (playerViewId: {playerViewId})");
                    photonView.RPC("RPC_RequestPickup", RpcTarget.MasterClient, playerViewId);
                }
            }
            else
            {
                LogVerbose($"[ItemPickup] Offline mode, processing pickup directly");
                ProcessPickupOnMaster();
            }
        }
        else
        {
            LogVerbose("Inventory full! Cannot pick up item.");
        }
    }
    
    [PunRPC]
    private void RPC_RequestPickup(int playerViewId)
    {
        LogVerbose($"[ItemPickup] RPC_RequestPickup received on {gameObject.name} (playerViewId: {playerViewId})");
        
        if (!PhotonNetwork.IsMasterClient)
            return;
            
        if (isPickedUp)
        {
            Debug.LogWarning($"[ItemPickup] {gameObject.name} already picked up, ignoring request");
            return;
        }
        
        ProcessPickupOnMaster();
    }
    
    private void ProcessPickupOnMaster()
    {
        if (isPickedUp)
        {
            Debug.LogWarning($"[ItemPickup] {gameObject.name} already picked up in ProcessPickupOnMaster");
            return;
        }
        
        isPickedUp = true;
        
        // Disable visuals immediately
        DisableVisuals();
        
        // Sync pickup state to all clients
        if (photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            photonView.RPC("RPC_SyncPickupState", RpcTarget.Others);
        }
        
        // Play pickup effect
        PlayPickupEffect();
        
        // Destroy the pickup
        StartCoroutine(DestroyPickupDelayed());
    }
    
    [PunRPC]
    private void RPC_SyncPickupState()
    {
        if (isPickedUp) return;
        
        LogVerbose($"[ItemPickup] RPC_SyncPickupState received on {gameObject.name}");
        isPickedUp = true;
        
        // Disable visuals immediately
        DisableVisuals();
        
        // Play pickup effect on remote clients
        PlayPickupEffect();
    }
    
    private void DisableVisuals()
    {
        var renderer = GetComponent<Renderer>();
        if (renderer != null)
            renderer.enabled = false;
        
        var collider = GetComponent<Collider>();
        if (collider != null)
            collider.enabled = false;
        
        // Disable outline when picked up
        currentOutlineIntensity = 0f;
        pulsationTime = 0f;
        UpdateOutlineIntensity();
    }
    
    private void SetupOutlineMaterials()
    {
        itemRenderers = GetComponentsInChildren<Renderer>();
        if (itemRenderers == null || itemRenderers.Length == 0)
        {
            itemRenderers = GetComponents<Renderer>();
        }
        
        if (itemRenderers == null || itemRenderers.Length == 0)
        {
            Debug.LogWarning($"[ItemPickup] {gameObject.name} has no Renderer components for outline effect!");
            return;
        }
        
        originalMaterials = new Material[itemRenderers.Length];
        outlineMaterials = new Material[itemRenderers.Length];
        
        for (int i = 0; i < itemRenderers.Length; i++)
        {
            if (itemRenderers[i] != null && itemRenderers[i].material != null)
            {
                originalMaterials[i] = itemRenderers[i].material;
                outlineMaterials[i] = new Material(originalMaterials[i]);
                
                outlineMaterials[i].DisableKeyword("_EMISSION");
                outlineMaterials[i].SetColor("_EmissionColor", Color.black);
                
                itemRenderers[i].material = outlineMaterials[i];
            }
        }
    }
    
    private void CheckPlayerProximity()
    {
        Transform localPlayer = GetSharedLocalPlayerTransform();
        if (localPlayer == null)
        {
            playersInRange = 0;
            playerInRange = false;
            return;
        }

        float maxDistSqr = outlineProximityDistance * outlineProximityDistance;
        float distSqr = (transform.position - localPlayer.position).sqrMagnitude;
        bool inRange = distSqr <= maxDistSqr;

        playersInRange = inRange ? 1 : 0;
        playerInRange = inRange;
    }

    private Transform GetSharedLocalPlayerTransform()
    {
        if (cachedLocalPlayer != null && Time.time < nextSharedPlayerRefreshTime)
            return cachedLocalPlayer;

        cachedLocalPlayer = PlayerRegistry.GetLocalPlayerTransform();
        nextSharedPlayerRefreshTime = Time.time + Mathf.Max(0.1f, sharedPlayerRefreshInterval);
        return cachedLocalPlayer;
    }
    
    private void UpdateOutlineIntensity()
    {
        if (outlineMaterials == null || itemRenderers == null) return;
        
        // Smoothly fade in/out the base intensity based on player proximity
        float targetBaseIntensity = (playerInRange && !isPickedUp) ? 1f : 0f;
        currentOutlineIntensity = Mathf.Lerp(currentOutlineIntensity, targetBaseIntensity, Time.deltaTime * outlineTransitionSpeed);
        
        // Update pulsation timer only when player is in range
        if (playerInRange && !isPickedUp && currentOutlineIntensity > 0.01f)
        {
            pulsationTime += Time.deltaTime * pulsationSpeed;
        }
        else
        {
            pulsationTime = 0f;
        }
        
        // Calculate pulsating intensity - apply pulsation to the faded-in base intensity
        float finalIntensity = 0f;
        if (currentOutlineIntensity > 0.001f)
        {
            float sineValue = (Mathf.Sin(pulsationTime * Mathf.PI * 2f) + 1f) * 0.5f;
            float pulsatedIntensity = Mathf.Lerp(0f, maxPulsationIntensity, sineValue);
            finalIntensity = outlineIntensity * currentOutlineIntensity * pulsatedIntensity;
        }
        
        float maxAllowedIntensity = outlineIntensity * maxPulsationIntensity;
        finalIntensity = Mathf.Clamp(finalIntensity, 0f, maxAllowedIntensity);
        
        float maxEmissionIntensity = outlineIntensity * 0.5f;
        float clampedIntensity = Mathf.Clamp(finalIntensity, 0f, maxEmissionIntensity);
        
        Color emissionColor = outlineColor * clampedIntensity;
        
        for (int i = 0; i < itemRenderers.Length && i < outlineMaterials.Length; i++)
        {
            if (itemRenderers[i] != null && outlineMaterials[i] != null)
            {
                if (finalIntensity > 0.001f)
                {
                    outlineMaterials[i].EnableKeyword("_EMISSION");
                    outlineMaterials[i].SetColor("_EmissionColor", emissionColor);
                }
                else
                {
                    outlineMaterials[i].DisableKeyword("_EMISSION");
                    outlineMaterials[i].SetColor("_EmissionColor", Color.black);
                }
            }
        }
    }
    
    private void CleanupOutlineMaterials()
    {
        if (outlineMaterials != null)
        {
            for (int i = 0; i < outlineMaterials.Length; i++)
            {
                if (outlineMaterials[i] != null)
                {
                    Destroy(outlineMaterials[i]);
                }
            }
            outlineMaterials = null;
        }
        
        if (itemRenderers != null && originalMaterials != null)
        {
            for (int i = 0; i < itemRenderers.Length && i < originalMaterials.Length; i++)
            {
                if (itemRenderers[i] != null && originalMaterials[i] != null)
                {
                    itemRenderers[i].material = originalMaterials[i];
                }
            }
        }
    }
    
    private System.Collections.IEnumerator DestroyPickupDelayed()
    {
        yield return new WaitForSeconds(0.1f);
        
        if (photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                PhotonNetwork.Destroy(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private void PlayPickupSound()
    {
        if (audioSource != null)
        {
            AudioClip soundToPlay = pickupSound != null ? pickupSound : itemData.pickupSound;
            if (soundToPlay != null)
            {
                audioSource.PlayOneShot(soundToPlay);
            }
        }
    }
    
    private void PlayPickupEffect()
    {
        GameObject effectToPlay = pickupEffect != null ? pickupEffect : itemData.pickupEffect;
        if (effectToPlay != null)
        {
            Instantiate(effectToPlay, transform.position, Quaternion.identity);
        }
    }
    
    private void UpdateQuestProgress()
    {
        if (itemData == null) return;
        
        // Update quest progress for item collection
        var questManager = FindFirstObjectByType<QuestManager>();
        if (questManager != null)
        {
            // Use questId if provided, otherwise fall back to itemName
            string identifier = !string.IsNullOrEmpty(itemData.questId) ? itemData.questId : itemData.itemName;
            questManager.AddProgress_Collect(identifier, quantity);
        }
    }
    
    
    // Public getters
    public ItemData ItemData => itemData;
    public int Quantity => quantity;
    public bool IsPickedUp => isPickedUp;
    
    // Method to manually trigger pickup (for testing or special cases)
    public void ForcePickup(GameObject player)
    {
        PickupItem(player);
    }
}