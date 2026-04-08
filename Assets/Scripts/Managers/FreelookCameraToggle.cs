using UnityEngine;
using Photon.Pun;

/// <summary>
/// Toggles the freelook camera on/off when pressing the 0 key.
/// Disables player camera and controls when freelook is active.
/// </summary>
public class FreelookCameraToggle : MonoBehaviour
{
    [Header("Camera References")]
    [Tooltip("The freelook camera GameObject (should have FreestyleCameraController or Camera component)")]
    public GameObject freelookCamera;
    
    [Tooltip("The player's main camera (will be disabled when freelook is active)")]
    public Camera playerCamera;
    
    [Header("Player References")]
    [Tooltip("The player GameObject (will disable controls when freelook is active)")]
    public GameObject player;
    
    [Header("Settings")]
    [Tooltip("Should the freelook camera be disabled by default?")]
    public bool startDisabled = true;
    
    [Tooltip("Disable player movement when freelook is active?")]
    public bool disablePlayerMovement = true;
    
    [Header("Debug")]
    public bool enableDebugLogs = true;
    
    private bool isFreelookActive = false;
    private FreestyleCameraController freelookController;
    private Camera freelookCameraComponent;
    
    void Start()
    {
        // Auto-find freelook camera if not assigned
        if (freelookCamera == null)
        {
            freelookCamera = GameObject.Find("FreelookCamera");
            if (freelookCamera == null)
            {
                // Try to find by component
                FreestyleCameraController found = FindFirstObjectByType<FreestyleCameraController>();
                if (found != null)
                {
                    freelookCamera = found.gameObject;
                }
            }
        }
        
        // Get freelook components
        if (freelookCamera != null)
        {
            freelookController = freelookCamera.GetComponent<FreestyleCameraController>();
            freelookCameraComponent = freelookCamera.GetComponent<Camera>();
            
            if (startDisabled)
            {
                SetFreelookActive(false);
            }
            else
            {
                SetFreelookActive(true);
            }
        }
        else
        {
            Debug.LogWarning("[FreelookCameraToggle] Freelook camera not found! Assign it in the inspector.");
        }
        
        // Auto-find player camera if not assigned
        if (playerCamera == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                playerCamera = playerObj.GetComponentInChildren<Camera>();
                if (player == null)
                {
                    player = playerObj;
                }
            }
        }
    }
    
    void Update()
    {
        // Only allow toggle for local player in multiplayer
        if (player != null)
        {
            PhotonView pv = player.GetComponent<PhotonView>();
            if (pv != null && !pv.IsMine)
            {
                return; // Not local player, don't process
            }
        }
        
        // Toggle with 0 key (Alpha0)
        if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            ToggleFreelook();
        }
    }
    
    public void ToggleFreelook()
    {
        if (freelookCamera == null)
        {
            if (enableDebugLogs) Debug.LogWarning("[FreelookCameraToggle] Cannot toggle: freelook camera not assigned!");
            return;
        }
        
        isFreelookActive = !isFreelookActive;
        SetFreelookActive(isFreelookActive);
        
        if (enableDebugLogs)
        {
            Debug.Log($"[FreelookCameraToggle] Freelook camera {(isFreelookActive ? "ENABLED" : "DISABLED")}");
        }
    }
    
    private void SetFreelookActive(bool active)
    {
        // Enable/disable freelook camera
        if (freelookCamera != null)
        {
            freelookCamera.SetActive(active);
        }
        
        // Enable/disable freelook camera component
        if (freelookCameraComponent != null)
        {
            freelookCameraComponent.enabled = active;
        }
        
        // Enable/disable freelook controller
        if (freelookController != null)
        {
            freelookController.enabled = active;
        }
        
        // Disable player camera when freelook is active
        if (playerCamera != null)
        {
            playerCamera.enabled = !active;
        }
        
        // Disable player movement when freelook is active
        if (disablePlayerMovement && player != null)
        {
            var controller = player.GetComponent<ThirdPersonController>();
            if (controller != null)
            {
                controller.SetCanMove(!active);
                controller.SetCanControl(!active);
            }
            
            // Also disable player camera orbit
            var cameraOrbit = player.GetComponentInChildren<ThirdPersonCameraOrbit>();
            if (cameraOrbit != null)
            {
                cameraOrbit.SetCameraControlActive(!active);
            }
        }
        
        // Update cursor state
        if (active)
        {
            // Freelook active - unlock cursor for camera control
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            // Freelook inactive - restore gameplay cursor
            LocalInputLocker.Ensure()?.ForceGameplayCursor();
        }
    }
    
    public bool IsFreelookActive()
    {
        return isFreelookActive;
    }
}

