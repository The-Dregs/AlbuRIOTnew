using UnityEngine;

public class EquipmentPickup : MonoBehaviour
{
    public ItemData itemData;
    public GameObject pickupPrompt; // Assign your UI text GameObject here
    
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

    private bool playerInRange = false;
    private GameObject player;
    private PlayerInteractHUD playerHUD; // optional HUD on player
    
    // Outline system
    private Renderer[] itemRenderers;
    private Material[] originalMaterials;
    private Material[] outlineMaterials;
    private bool outlinePlayerInRange = false;
    private float currentOutlineIntensity = 0f;
    private float pulsationTime = 0f;

    void Start()
    {
        SetupOutlineMaterials();
    }
    
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            player = other.gameObject;
            if (pickupPrompt != null)
                pickupPrompt.SetActive(true);
            // show player HUD prompt if available (local player check lives inside HUD)
            playerHUD = player.GetComponentInChildren<PlayerInteractHUD>(true);
            if (playerHUD != null)
            {
                string name = (itemData != null ? itemData.itemName : "item");
                playerHUD.Show($"Press E to pick up {name}");
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            player = null;
            if (pickupPrompt != null)
                pickupPrompt.SetActive(false);
            if (playerHUD != null)
            {
                playerHUD.Hide();
                playerHUD = null;
            }
        }
    }

    void Update()
    {
        // Don't allow pickups if any UI or dialogue is open
        if (LocalUIManager.Instance != null && LocalUIManager.Instance.IsAnyOpen)
        {
            if (playerHUD != null) playerHUD.Hide();
            playerInRange = false;
            return;
        }

        if (playerInRange && Input.GetKeyDown(KeyCode.E))
        {
            EquipmentManager manager = player.GetComponent<EquipmentManager>();
            if (manager != null && itemData != null)
            {
                Debug.Log($"[pickup] EquipmentPickup | HandlePickup | item={(itemData != null ? itemData.itemName : "null")}");
                manager.HandlePickup(itemData);
                if (pickupPrompt != null)
                    pickupPrompt.SetActive(false);
                if (playerHUD != null) playerHUD.Hide();
                Destroy(gameObject);
            }
        }
        
        // Update outline
        CheckPlayerProximity();
        UpdateOutlineIntensity();
    }
    
    void OnDestroy()
    {
        CleanupOutlineMaterials();
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
            Debug.LogWarning($"[EquipmentPickup] {gameObject.name} has no Renderer components for outline effect!");
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
        var players = PlayerRegistry.All;
        if (players.Count == 0)
        {
            outlinePlayerInRange = false;
            return;
        }
        
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player == null) continue;
            
            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance <= outlineProximityDistance)
            {
                outlinePlayerInRange = true;
                return;
            }
        }
        
        outlinePlayerInRange = false;
    }
    
    private void UpdateOutlineIntensity()
    {
        if (outlineMaterials == null || itemRenderers == null) return;
        
        // Smoothly fade in/out the base intensity based on player proximity
        float targetBaseIntensity = outlinePlayerInRange ? 1f : 0f;
        currentOutlineIntensity = Mathf.Lerp(currentOutlineIntensity, targetBaseIntensity, Time.deltaTime * outlineTransitionSpeed);
        
        // Update pulsation timer only when player is in range
        if (outlinePlayerInRange && currentOutlineIntensity > 0.01f)
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
}
