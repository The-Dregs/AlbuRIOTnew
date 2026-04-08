using UnityEngine;
using Photon.Pun;
using System.Collections;

public class DestructiblePlant : MonoBehaviourPun, IEnemyDamageable
{
    [Header("Health")]
    [Tooltip("Base max hits (used when unarmed). Armed players take fewer hits (2).")]
    [SerializeField] private int maxHits = 4;
    [SerializeField] private int currentHits = 0;
    
    [Header("Hitbox")]
    [Tooltip("Collider GameObject for the hitbox. If null, will use collider on this object or its children.")]
    [SerializeField] private GameObject hitboxObject;
    [Tooltip("Plant visual model. If null, will use this object's transform. This is what pops when hit.")]
    [SerializeField] private Transform plantModel;
    
    [Header("Hit Effect")]
    [SerializeField] private float hitPopScale = 1.4f;
    [SerializeField] private float hitPopDuration = 0.2f;
    
    [Header("Item Drops")]
    [SerializeField] private ItemData[] dropItems;
    [Tooltip("Min/Max quantity range for each drop item. X = minimum, Y = maximum. Random value between min and max (inclusive) will be dropped.")]
    [SerializeField] private Vector2Int[] dropQuantityRanges;
    
    [Header("VFX")]
    [SerializeField] private GameObject hitEffect;
    [SerializeField] private GameObject destroyEffect;
    
    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string hitTrigger = "Hit";
    [SerializeField] private string destroyTrigger = "Destroy";
    
    private CameraShake cameraShake;
    
    [Header("Destroy Effect")]
    [Tooltip("Time it takes for the model to sink through the ground when destroyed")]
    [SerializeField] private float sinkDuration = 0.2f;
    [Tooltip("How far below ground the model sinks before being destroyed")]
    [SerializeField] private float sinkDistance = 1.5f;
    
    [Header("Outline/Highlight")]
    [Tooltip("Distance at which outline appears when player is nearby")]
    [SerializeField] private float outlineProximityDistance = 5f;
    [Tooltip("Color of outline when plant is destructible (yellow)")]
    [SerializeField] private Color destructibleOutlineColor = new Color(1f, 1f, 0f, 1f);
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
    
    private bool isDestroyed = false;
    private GameObject lastHitSource = null;
    private Vector3 originalModelScale;
    private Coroutine hitPopCoroutine;
    private Coroutine sinkCoroutine;
    
    private Collider hitboxCollider;
    
    // Outline system
    private Renderer[] plantRenderers;
    private Material[] originalMaterials;
    private Material[] outlineMaterials;
    private bool playerInRange = false;
    private float currentOutlineIntensity = 0f;
    private float pulsationTime = 0f;
    private int playersInRange = 0;
    private float nextProximityCheckTime = 0f;

    private void LogVerbose(string message)
    {
        if (enableDebugLogs) Debug.Log(message);
    }
    
    void Start()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        
        if (plantModel == null)
            plantModel = transform;
        
        originalModelScale = plantModel.localScale;
        
        // stagger proximity checks so 372 plants don't all check on the same frame
        nextProximityCheckTime = Time.time + Random.Range(0f, proximityCheckInterval);
        SetupHitbox();
        SetupOutlineMaterials();
            
        if (dropItems == null || dropItems.Length == 0)
        {
            Debug.LogWarning($"[DestructiblePlant] {gameObject.name} has no drop items configured!");
        }
        
        if (dropQuantityRanges == null || dropQuantityRanges.Length != dropItems.Length)
        {
            dropQuantityRanges = new Vector2Int[dropItems != null ? dropItems.Length : 0];
            for (int i = 0; i < dropQuantityRanges.Length; i++)
            {
                dropQuantityRanges[i] = new Vector2Int(1, 1);
            }
        }
    }
    
    private void FindCameraShake()
    {
        // Search in scene for CameraShake component
        cameraShake = FindFirstObjectByType<CameraShake>();
    }
    
    void Update()
    {
        if (isDestroyed) return;

        if (Time.time >= nextProximityCheckTime)
        {
            CheckPlayerProximity();
            nextProximityCheckTime = Time.time + Mathf.Max(0.05f, proximityCheckInterval);
        }

        // Skip expensive material updates if fully faded and no one nearby.
        if (!playerInRange && currentOutlineIntensity <= 0.001f)
            return;

        UpdateOutlineIntensity();
    }
    
    private void SetupOutlineMaterials()
    {
        // Get all renderers on the plant model
        plantRenderers = plantModel.GetComponentsInChildren<Renderer>();
        if (plantRenderers == null || plantRenderers.Length == 0)
        {
            plantRenderers = GetComponentsInChildren<Renderer>();
        }
        
        if (plantRenderers == null || plantRenderers.Length == 0)
        {
            Debug.LogWarning($"[DestructiblePlant] {gameObject.name} has no Renderer components for outline effect!");
            return;
        }
        
        originalMaterials = new Material[plantRenderers.Length];
        outlineMaterials = new Material[plantRenderers.Length];
        
        for (int i = 0; i < plantRenderers.Length; i++)
        {
            if (plantRenderers[i] != null && plantRenderers[i].material != null)
            {
                originalMaterials[i] = plantRenderers[i].material;
                // Create instance material for outline
                outlineMaterials[i] = new Material(originalMaterials[i]);
                
                // IMPORTANT: Clear any existing emission to prevent additive glow
                outlineMaterials[i].DisableKeyword("_EMISSION");
                outlineMaterials[i].SetColor("_EmissionColor", Color.black);
                
                // Keep original material properties - don't change transparency mode
                // Only emission will be controlled for the glow effect
                
                plantRenderers[i].material = outlineMaterials[i];
            }
        }
    }
    
    private void CheckPlayerProximity()
    {
        Transform localPlayer = PlayerRegistry.GetLocalPlayerTransform();
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
    
    private void UpdateOutlineIntensity()
    {
        if (outlineMaterials == null || plantRenderers == null) return;
        
        // Smoothly fade in/out the base intensity based on player proximity
        float targetBaseIntensity = (playerInRange && !isDestroyed) ? 1f : 0f;
        currentOutlineIntensity = Mathf.Lerp(currentOutlineIntensity, targetBaseIntensity, Time.deltaTime * outlineTransitionSpeed);
        
        // Update pulsation timer only when player is in range
        if (playerInRange && !isDestroyed && currentOutlineIntensity > 0.01f)
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
            // Sine wave oscillates between -1 and 1, we want 0 to maxPulsationIntensity
            float sineValue = (Mathf.Sin(pulsationTime * Mathf.PI * 2f) + 1f) * 0.5f; // 0 to 1
            // Apply pulsation multiplier to the faded base intensity
            float pulsatedIntensity = Mathf.Lerp(0f, maxPulsationIntensity, sineValue);
            finalIntensity = outlineIntensity * currentOutlineIntensity * pulsatedIntensity;
        }
        
        // Clamp to ensure it never exceeds the maximum pulsation intensity
        float maxAllowedIntensity = outlineIntensity * maxPulsationIntensity;
        finalIntensity = Mathf.Clamp(finalIntensity, 0f, maxAllowedIntensity);
        
        // Calculate emission color - limit intensity so glow is never too bright (max 50% of base intensity)
        float maxEmissionIntensity = outlineIntensity * 0.5f;
        float clampedIntensity = Mathf.Clamp(finalIntensity, 0f, maxEmissionIntensity);
        
        Color emissionColor = destructibleOutlineColor * clampedIntensity;
        
        for (int i = 0; i < plantRenderers.Length && i < outlineMaterials.Length; i++)
        {
            if (plantRenderers[i] != null && outlineMaterials[i] != null)
            {
                if (finalIntensity > 0.001f)
                {
                    // Enable emission and set color ONLY when intensity is above threshold
                    outlineMaterials[i].EnableKeyword("_EMISSION");
                    outlineMaterials[i].SetColor("_EmissionColor", emissionColor);
                }
                else
                {
                    // Disable emission completely when not visible to prevent any glow
                    outlineMaterials[i].DisableKeyword("_EMISSION");
                    outlineMaterials[i].SetColor("_EmissionColor", Color.black);
                }
            }
        }
    }
    
    private void SetupHitbox()
    {
        hitboxCollider = null;
        
        if (hitboxObject != null)
        {
            hitboxCollider = hitboxObject.GetComponent<Collider>();
            if (hitboxCollider == null)
            {
                hitboxCollider = hitboxObject.GetComponentInChildren<Collider>();
            }
        }
        
        if (hitboxCollider == null)
        {
            hitboxCollider = GetComponent<Collider>();
            if (hitboxCollider == null)
            {
                hitboxCollider = GetComponentInChildren<Collider>();
            }
        }
        
        if (hitboxCollider == null)
        {
            Debug.LogError($"[DestructiblePlant] {gameObject.name} has no collider found! Damage detection will not work. Make sure the plant has a collider (CapsuleCollider, SphereCollider, or BoxCollider) on this GameObject or a child.");
            return;
        }

        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer >= 0)
        {
            // Ensure plant and its hitbox use the Enemy layer so player attacks always detect it
            if (gameObject.layer != enemyLayer)
            {
                gameObject.layer = enemyLayer;
            }
            if (hitboxObject != null && hitboxObject.layer != enemyLayer)
            {
                hitboxObject.layer = enemyLayer;
            }
        }
    }
    
    private void ApplyHitboxTransform()
    {
        // Intentionally empty: collider shape/position are authored directly on the Collider component.
    }
    
    public void TakeEnemyDamage(int amount, GameObject source)
    {
        LogVerbose($"[DestructiblePlant] TakeEnemyDamage called on {gameObject.name} - Amount: {amount}, Source: {(source != null ? source.name : "null")}, IsDestroyed: {isDestroyed}");
        
        if (isDestroyed)
        {
            Debug.LogWarning($"[DestructiblePlant] {gameObject.name} is already destroyed, ignoring damage");
            return;
        }
        
        bool isNetworked = photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom;
        LogVerbose($"[DestructiblePlant] Networked: {isNetworked}, IsMasterClient: {(isNetworked ? PhotonNetwork.IsMasterClient.ToString() : "N/A")}");
        
        if (isNetworked)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                LogVerbose($"[DestructiblePlant] MasterClient processing hit directly");
                ApplyHit(source);
            }
            else if (photonView != null)
            {
                int sourceViewId = -1;
                if (source != null)
                {
                    var srcPv = source.GetComponent<PhotonView>();
                    if (srcPv != null) sourceViewId = srcPv.ViewID;
                }
                LogVerbose($"[DestructiblePlant] Non-Master sending RPC_ApplyHit to MasterClient (sourceViewId: {sourceViewId})");
                photonView.RPC("RPC_ApplyHit", RpcTarget.MasterClient, sourceViewId);
            }
            else
            {
                Debug.LogError($"[DestructiblePlant] Networked but photonView is null! Cannot send RPC.");
            }
        }
        else
        {
            LogVerbose($"[DestructiblePlant] Offline mode, processing hit directly");
            ApplyHit(source);
        }
    }
    
    [PunRPC]
    public void RPC_ApplyHit(int sourceViewId)
    {
        LogVerbose($"[DestructiblePlant] RPC_ApplyHit received on {gameObject.name} (sourceViewId: {sourceViewId})");
        GameObject source = null;
        if (sourceViewId >= 0)
        {
            var srcPv = PhotonView.Find(sourceViewId);
            if (srcPv != null) source = srcPv.gameObject;
            LogVerbose($"[DestructiblePlant] Resolved source: {(source != null ? source.name : "null")}");
        }
        ApplyHit(source);
    }
    
    private int GetEffectiveMaxHits(GameObject source)
    {
        if (source == null) return maxHits;
        
        var equipmentManager = source.GetComponent<EquipmentManager>();
        if (equipmentManager != null && equipmentManager.equippedItem != null)
        {
            return 2; // Armed: 2 hits
        }
        return maxHits; // Unarmed: uses base maxHits (3)
    }
    
    private void ApplyHit(GameObject source)
    {
        LogVerbose($"[DestructiblePlant] ApplyHit on {gameObject.name} - Current hits: {currentHits}, Source: {(source != null ? source.name : "null")}");
        
        if (isDestroyed)
        {
            Debug.LogWarning($"[DestructiblePlant] {gameObject.name} already destroyed in ApplyHit");
            return;
        }
        
        // Determine effective max hits based on whether player is armed
        int effectiveMaxHits = GetEffectiveMaxHits(source);
        LogVerbose($"[DestructiblePlant] Effective max hits: {effectiveMaxHits} (base: {maxHits})");
        
        currentHits++;
        lastHitSource = source;
        LogVerbose($"[DestructiblePlant] Hit count now: {currentHits}/{effectiveMaxHits}");
        
        if (hitEffect != null)
        {
            GameObject fx = Instantiate(hitEffect, transform.position, Quaternion.identity);
            Destroy(fx, 2f);
        }
        
        if (animator != null && !string.IsNullOrEmpty(hitTrigger))
        {
            animator.SetTrigger(hitTrigger);
        }
        
        StartHitPopEffect();
        
        // Camera shake on plant hit (only for local player)
        if (source != null)
        {
            var sourcePV = source.GetComponent<Photon.Pun.PhotonView>();
            if (sourcePV != null && sourcePV.IsMine)
            {
                if (cameraShake == null)
                    FindCameraShake();
                
                if (cameraShake != null)
                    cameraShake.ShakeHitPlant();
            }
        }
        
        if (currentHits >= effectiveMaxHits)
        {
            LogVerbose($"[DestructiblePlant] Max hits reached! Destroying plant {gameObject.name}");
            DestroyPlant();
        }
        
        if (photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
        {
            int sourceViewId = -1;
            if (source != null)
            {
                var srcPv = source.GetComponent<PhotonView>();
                if (srcPv != null) sourceViewId = srcPv.ViewID;
            }
            photonView.RPC("RPC_SyncHitState", RpcTarget.Others, currentHits, sourceViewId);
        }
    }
    
    private void StartHitPopEffect()
    {
        if (hitPopCoroutine != null)
            StopCoroutine(hitPopCoroutine);
        hitPopCoroutine = StartCoroutine(CoHitPopEffect());
        
        if (photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            photonView.RPC("RPC_HitPopEffect", RpcTarget.Others);
        }
    }
    
    [PunRPC]
    private void RPC_HitPopEffect()
    {
        if (hitPopCoroutine != null)
            StopCoroutine(hitPopCoroutine);
        hitPopCoroutine = StartCoroutine(CoHitPopEffect());
    }
    
    private IEnumerator CoHitPopEffect()
    {
        float elapsed = 0f;
        float halfDuration = hitPopDuration * 0.5f;
        
        while (elapsed < hitPopDuration)
        {
            elapsed += Time.deltaTime;
            
            if (elapsed < halfDuration)
            {
                float t = elapsed / halfDuration;
                float scale = Mathf.Lerp(1f, hitPopScale, t);
                plantModel.localScale = originalModelScale * scale;
            }
            else
            {
                float t = (elapsed - halfDuration) / halfDuration;
                float scale = Mathf.Lerp(hitPopScale, 1f, t);
                plantModel.localScale = originalModelScale * scale;
            }
            
            yield return null;
        }
        
        plantModel.localScale = originalModelScale;
        hitPopCoroutine = null;
    }
    
    [PunRPC]
    private void RPC_SyncHitState(int hits, int sourceViewId)
    {
        currentHits = hits;
        
        GameObject source = null;
        if (sourceViewId >= 0)
        {
            var srcPv = PhotonView.Find(sourceViewId);
            if (srcPv != null)
            {
                source = srcPv.gameObject;
                lastHitSource = source;
            }
        }
        
        if (animator != null && !string.IsNullOrEmpty(hitTrigger))
        {
            animator.SetTrigger(hitTrigger);
        }
        
        // Check effective max hits based on the source player's equipment status
        int effectiveMaxHits = GetEffectiveMaxHits(source);
        if (hits >= effectiveMaxHits && !isDestroyed)
        {
            // Only MasterClient (or offline) proceeds to destroy; joiners wait for RPC_StartSinkAnimation
            bool isMasterClient = photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient;
            bool isOffline = photonView == null || !PhotonNetwork.IsConnected || !PhotonNetwork.InRoom;

            if (isMasterClient || isOffline)
            {
                DestroyPlant();
            }
        }
    }
    
    private void DestroyPlant()
    {
        LogVerbose($"[DestructiblePlant] DestroyPlant called on {gameObject.name}");
        
        if (isDestroyed)
        {
            Debug.LogWarning($"[DestructiblePlant] {gameObject.name} already destroyed, ignoring DestroyPlant call");
            return;
        }
        isDestroyed = true;
        
        // Disable outline when destroyed
        currentOutlineIntensity = 0f;
        pulsationTime = 0f;
        UpdateOutlineIntensity();
        
        if (destroyEffect != null)
        {
            GameObject fx = Instantiate(destroyEffect, transform.position, Quaternion.identity);
            Destroy(fx, 2f);
        }
        
        if (animator != null && !string.IsNullOrEmpty(destroyTrigger))
        {
            animator.SetTrigger(destroyTrigger);
        }
        
        bool isMasterClient = photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient;
        bool isOffline = photonView == null || !PhotonNetwork.IsConnected || !PhotonNetwork.InRoom;
        
        LogVerbose($"[DestructiblePlant] DestroyPlant - IsMasterClient: {isMasterClient}, IsOffline: {isOffline}, DropItems will be called: {isMasterClient || isOffline}");
        
        if (isMasterClient || isOffline)
        {
            DropItems();
        }
        else
        {
            Debug.LogWarning($"[DestructiblePlant] Non-MasterClient destroyed plant, but DropItems only called on Master/Offline. This plant may not drop items!");
        }
        
        StartCoroutine(SinkAndDestroy());
        
        if (photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            photonView.RPC("RPC_StartSinkAnimation", RpcTarget.Others);
        }
    }
    
    [PunRPC]
    private void RPC_StartSinkAnimation()
    {
        if (isDestroyed) return;
        isDestroyed = true;
        
        // Disable outline when destroyed
        currentOutlineIntensity = 0f;
        pulsationTime = 0f;
        UpdateOutlineIntensity();
        
        if (destroyEffect != null)
        {
            GameObject fx = Instantiate(destroyEffect, transform.position, Quaternion.identity);
            Destroy(fx, 2f);
        }
        
        if (animator != null && !string.IsNullOrEmpty(destroyTrigger))
        {
            animator.SetTrigger(destroyTrigger);
        }
        
        StartCoroutine(SinkAndDestroyWithoutNetwork());
    }
    
    // Sink animation without network destroy - used by non-MasterClient to play effects but not destroy
    private IEnumerator SinkAndDestroyWithoutNetwork()
    {
        if (hitboxCollider != null)
            hitboxCollider.enabled = false;
        
        Vector3 startPos = plantModel.position;
        Vector3 endPos = startPos - Vector3.up * sinkDistance;
        float elapsed = 0f;
        
        while (elapsed < sinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / sinkDuration;
            plantModel.position = Vector3.Lerp(startPos, endPos, t);
            yield return null;
        }
        
        plantModel.position = endPos;
        // Do not destroy locally; MasterClient will network-destroy and remove this object for everyone
        
        // Cleanup outline materials
        CleanupOutlineMaterials();
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
        
        // Restore original materials if possible
        if (plantRenderers != null && originalMaterials != null)
        {
            for (int i = 0; i < plantRenderers.Length && i < originalMaterials.Length; i++)
            {
                if (plantRenderers[i] != null && originalMaterials[i] != null)
                {
                    plantRenderers[i].material = originalMaterials[i];
                }
            }
        }
    }
    
    private IEnumerator SinkAndDestroy()
    {
        if (hitboxCollider != null)
            hitboxCollider.enabled = false;
        
        Vector3 startPos = plantModel.position;
        Vector3 endPos = startPos - Vector3.up * sinkDistance;
        float elapsed = 0f;
        
        while (elapsed < sinkDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / sinkDuration;
            plantModel.position = Vector3.Lerp(startPos, endPos, t);
            yield return null;
        }
        
        plantModel.position = endPos;
        
        // Cleanup outline materials before destroying
        CleanupOutlineMaterials();
        
        if (photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            // Only destroy if we're the MasterClient
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
    
    void OnDestroy()
    {
        CleanupOutlineMaterials();
    }
    
    private void DropItems()
    {
        LogVerbose($"[DestructiblePlant] DropItems called on {gameObject.name}");
        LogVerbose($"[DestructiblePlant] Drop items count: {(dropItems != null ? dropItems.Length : 0)}, Last hit source: {(lastHitSource != null ? lastHitSource.name : "null")}");
        
        if (dropItems == null || dropItems.Length == 0)
        {
            Debug.LogWarning($"[DestructiblePlant] {gameObject.name} has no drop items configured!");
            return;
        }
        
        GameObject targetPlayer = ResolvePlayerFromSource(lastHitSource);
        LogVerbose($"[DestructiblePlant] Resolved player from source: {(targetPlayer != null ? targetPlayer.name : "null")}");
        
        if (targetPlayer == null)
        {
            LogVerbose($"[DestructiblePlant] Trying to find nearest player as fallback");
            targetPlayer = FindNearestPlayer();
            LogVerbose($"[DestructiblePlant] Nearest player: {(targetPlayer != null ? targetPlayer.name : "null")}");
        }
        
        if (targetPlayer == null)
        {
            Debug.LogWarning("[DestructiblePlant] No player found to grant items to! Falling back to spawning pickups.");
            SpawnItemsAsPickups();
            return;
        }
        
        Inventory inventory = targetPlayer.GetComponent<Inventory>();
        if (inventory == null)
        {
            Debug.LogWarning($"[DestructiblePlant] Inventory not found on resolved player {targetPlayer.name}. Spawning pickups instead.");
            SpawnItemsAsPickups();
            return;
        }
        
        LogVerbose($"[DestructiblePlant] Found inventory on {targetPlayer.name}, proceeding to grant items");
        
        bool isNetworked = photonView != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom;
        bool isMaster = isNetworked && PhotonNetwork.IsMasterClient;
        
        for (int i = 0; i < dropItems.Length; i++)
        {
            var item = dropItems[i];
            if (item == null) continue;
            
            Vector2Int range = (i < dropQuantityRanges.Length) ? dropQuantityRanges[i] : new Vector2Int(1, 1);
            int quantity = Random.Range(range.x, range.y + 1);
            
            if (isNetworked && isMaster)
            {
                // Networked: use RPC to grant items to the player's inventory
                var playerPV = targetPlayer.GetComponent<PhotonView>();
                if (playerPV != null && playerPV.Owner != null)
                {
                    // Find inventory on the target player (should be on same GameObject or child)
                    var targetInventory = targetPlayer.GetComponentInChildren<Inventory>();
                    if (targetInventory != null && targetInventory.photonView != null)
                    {
                        // Verify the inventory's PhotonView ownership matches the player's
                        if (targetInventory.photonView.Owner == playerPV.Owner)
                        {
                            // Send RPC to the inventory's PhotonView, targeting the player's owner
                            // Include questId/itemName for quest tracking
                            string questId = !string.IsNullOrEmpty(item.questId) ? item.questId : item.itemName;
                            targetInventory.photonView.RPC("RPC_GrantItem", playerPV.Owner, item.itemName, quantity, false);
                            LogVerbose($"[DestructiblePlant] Granted (RPC) {item.itemName} x{quantity} to {targetPlayer.name} (Actor {playerPV.Owner.ActorNumber}, InvPV ID: {targetInventory.photonView.ViewID})");
                            
                            // Quest progress will be handled by Inventory's OnItemAdded event on the receiving client
                        }
                        else
                        {
                            Debug.LogWarning($"[DestructiblePlant] Inventory PV owner mismatch! Player owner: {playerPV.Owner?.ActorNumber}, Inv owner: {targetInventory.photonView.Owner?.ActorNumber}");
                            // Fallback to pickup
                            SpawnItemPickup(item, quantity);
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[DestructiblePlant] Could not find inventory with PhotonView on {targetPlayer.name}");
                        SpawnItemPickup(item, quantity);
                    }
                }
                else
                {
                    Debug.LogWarning($"[DestructiblePlant] Invalid player PhotonView on {targetPlayer.name}");
                    SpawnItemPickup(item, quantity);
                }
            }
            else if (!isNetworked)
            {
                // Offline: direct inventory addition
                bool added = inventory.AddItem(item, quantity);
                if (added)
                {
                    // Update quest progress for collected items
                    UpdateQuestProgress(item, quantity, targetPlayer);
                }
                else
                {
                    if (ItemManager.Instance != null)
                    {
                        Vector3 dropPosition = transform.position + Vector3.up * 0.5f + Random.insideUnitSphere * 0.5f;
                        ItemManager.Instance.SpawnItem(item, dropPosition, quantity);
                    }
                }
            }
        }
    }
    
    private void UpdateQuestProgress(ItemData item, int quantity, GameObject player)
    {
        if (item == null || player == null) return;
        
        var questManager = QuestManager.Instance ?? FindFirstObjectByType<QuestManager>();
        if (questManager != null)
        {
            // Use questId if provided, otherwise fall back to itemName
            string identifier = !string.IsNullOrEmpty(item.questId) ? item.questId : item.itemName;
            questManager.AddProgress_Collect(identifier, quantity);
            LogVerbose($"[DestructiblePlant] Updated quest progress: {identifier} x{quantity}");
        }
    }

    // Attempts to resolve the actual player GameObject that dealt the hit
    private GameObject ResolvePlayerFromSource(GameObject source)
    {
        if (source == null) return null;

        // 1) Direct or parent Inventory
        var inv = source.GetComponentInParent<Inventory>();
        if (inv != null) return inv.gameObject;

        // 2) Use PhotonView owner to match a player root that has Inventory/PlayerStats
        var srcPv = source.GetComponentInParent<PhotonView>();
        if (srcPv != null)
        {
            var allPlayers = PlayerRegistry.All;
            for (int i = 0; i < allPlayers.Count; i++)
            {
                var p = allPlayers[i];
                if (p == null) continue;
                var pPv = p.GetComponent<PhotonView>();
                if (pPv != null && pPv.Owner != null && srcPv.Owner != null && pPv.Owner == srcPv.Owner)
                {
                    return p.gameObject;
                }
            }
        }

        return null;
    }
    
    private GameObject FindNearestPlayer()
    {
        var players = PlayerRegistry.All;
        if (players.Count == 0) return null;
        
        GameObject nearest = null;
        float nearestDist = float.MaxValue;
        
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player == null) continue;
            float dist = (transform.position - player.transform.position).sqrMagnitude;
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = player.gameObject;
            }
        }
        
        return nearest;
    }
    
    private void SpawnItemsAsPickups()
    {
        if (ItemManager.Instance == null) return;
        
        Vector3 dropPosition = transform.position + Vector3.up * 0.5f;
        
        for (int i = 0; i < dropItems.Length; i++)
        {
            if (dropItems[i] != null)
            {
                Vector2Int range = (i < dropQuantityRanges.Length) ? dropQuantityRanges[i] : new Vector2Int(1, 1);
                int quantity = Random.Range(range.x, range.y + 1);
                SpawnItemPickup(dropItems[i], quantity);
                
                Vector3 offset = Random.insideUnitCircle * 0.5f;
                offset.z = offset.y;
                offset.y = 0;
                dropPosition += offset;
            }
        }
    }
    
    private void SpawnItemPickup(ItemData item, int quantity)
    {
        if (ItemManager.Instance != null)
        {
            Vector3 dropPosition = transform.position + Vector3.up * 0.5f + Random.insideUnitSphere * 0.5f;
            ItemManager.Instance.SpawnItem(item, dropPosition, quantity);
        }
    }
    
    
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        
        // Draw exactly what the Collider is, with no extra offsets or scaling from the script.
        Collider col = hitboxCollider;
        if (col == null && hitboxObject != null)
            col = hitboxObject.GetComponent<Collider>();
        if (col == null)
            col = GetComponentInChildren<Collider>();
        if (col == null) return;
        
        if (col is SphereCollider sc)
        {
            Gizmos.DrawWireSphere(sc.transform.TransformPoint(sc.center),
                                  sc.radius * Mathf.Max(sc.transform.lossyScale.x, sc.transform.lossyScale.y, sc.transform.lossyScale.z));
        }
        else if (col is BoxCollider bc)
        {
            Gizmos.matrix = bc.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(bc.center, bc.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
        else if (col is CapsuleCollider cc)
        {
            // Approximate capsule with spheres at ends
            Vector3 center = cc.transform.TransformPoint(cc.center);
            float radius = cc.radius * Mathf.Max(cc.transform.lossyScale.x, cc.transform.lossyScale.z);
            float height = Mathf.Max(cc.height * cc.transform.lossyScale.y, radius * 2f);
            Vector3 up = Vector3.up * (height * 0.5f - radius);
            Vector3 top = center + up;
            Vector3 bottom = center - up;
            Gizmos.DrawWireSphere(top, radius);
            Gizmos.DrawWireSphere(bottom, radius);
        }
    }
}

