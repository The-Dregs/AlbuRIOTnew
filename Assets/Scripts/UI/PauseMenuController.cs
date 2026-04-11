using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;
using Photon.Pun;

/// <summary>
/// Escape / pause menu (and dev sub-panel) on the local player prefab.
/// Scene-change buttons should go through <see cref="LoadGameplaySceneFromEscapePanel"/> so spawn, HUD, and input init match gameplay transitions.
/// Note: class name is <c>PauseMenuControllers</c> (filename is PauseMenuController.cs) — keep Unity references in sync if you rename.
/// </summary>
public class PauseMenuControllers : MonoBehaviour
{
    [Header("UI Panel")]
    public GameObject pausePanel;
    [Tooltip("Optional: sub-panel to show Options while paused")]
    public GameObject optionsPanel;

    [Header("Dev Panel")]
    [Tooltip("Panel containing dev/debug buttons (spawn, teleport, cheats, etc.)")]
    public GameObject devPanel;
    [Tooltip("If false, dev panel hotkey only works in editor or development builds")]
    public bool allowDevPanelInReleaseBuild = false;

    [Header("Custom UI Panels")]
    [Tooltip("Assign HUD/stats/skills panels to activate with button")] public GameObject[] customUIPanels;

    [Header("Local Components")]
    public ThirdPersonController playerController;
    public PlayerCombat playerCombat;
    public ThirdPersonCameraOrbit cameraOrbit;
    
    [Header("Freelook Camera")]
    [Tooltip("Freelook camera GameObject (auto-found if not assigned)")]
    public GameObject freelookCamera;
    [Tooltip("Freelook camera toggle script (auto-found if not assigned)")]
    public FreelookCameraToggle freelookToggle;

    private bool isOpen = false;
    private int _inputLockToken = 0;
    private int currentRemnantIndex = 0;
    private int currentCommonEnemyIndex = 0;
    // We do not change Time.timeScale in multiplayer; gameplay keeps running.
    [Header("Scenes")]
    public string mainMenuSceneName = "MainMenu"; // used by LeaveGame
    public string testingSceneName = "TESTING";
    public string firstMapSceneName = "FIRSTMAP";
    public string secondMapSceneName = "SECONDMAP";
    public string thirdMapSceneName = "THIRDMAP";
    public string fourthMapSceneName = "FOURTHMAP";
    public string bakunawaBossFightSceneName = "BAKUNAWABOSSFIGHT";

    [Header("Terrain Generation")]
    public TerrainGenerator terrainGenerator;
    public MapResourcesGenerator mapResourcesGenerator;

    private PhotonView _ownerPhotonView;

    void Start()
    {
        ResolveOwnerPhotonView();

        if (pausePanel != null) pausePanel.SetActive(false);
        if (optionsPanel != null) optionsPanel.SetActive(false);
        if (devPanel != null) devPanel.SetActive(false);

        // ensure remote-player instances never show local pause ui
        if (!CanControlLocalPause())
        {
            if (pausePanel != null) pausePanel.SetActive(false);
            if (optionsPanel != null) optionsPanel.SetActive(false);
            if (devPanel != null) devPanel.SetActive(false);
            gameObject.SetActive(false);
            return;
        }

        // Auto-find freelook camera if not assigned
        if (freelookCamera == null)
        {
            freelookCamera = GameObject.Find("FreelookCamera");
            if (freelookCamera == null)
            {
                FreestyleCameraController found = FindFirstObjectByType<FreestyleCameraController>();
                if (found != null)
                {
                    freelookCamera = found.gameObject;
                }
            }
        }
        
        // Auto-find freelook toggle if not assigned
        if (freelookToggle == null)
        {
            freelookToggle = FindFirstObjectByType<FreelookCameraToggle>();
        }
    }

    void Update()
    {
        ResolveOwnerPhotonView();
        if (!CanControlLocalPause())
        {
            if (isOpen)
            {
                isOpen = false;
                if (pausePanel != null) pausePanel.SetActive(false);
                if (optionsPanel != null) optionsPanel.SetActive(false);
            }
            return;
        }

        if (Input.GetKeyDown(KeyCode.F12))
        {
            ToggleDevPanelFromHotkey();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (!isOpen)
            {
                if (!LocalUIManager.Ensure().TryOpen("PauseMenu")) return;
                isOpen = true;
                if (pausePanel != null) pausePanel.SetActive(true);
                // lock combat and camera, but allow movement; cursor unlocked
                if (_inputLockToken == 0)
                    _inputLockToken = LocalInputLocker.Ensure().Acquire("PauseMenu", lockMovement:false, lockCombat:true, lockCamera:true, cursorUnlock:true);
            }
            else
            {
                isOpen = false;
                if (pausePanel != null) pausePanel.SetActive(false);
                LocalUIManager.Instance.Close("PauseMenu");
                if (_inputLockToken != 0)
                {
                    LocalInputLocker.Ensure().Release(_inputLockToken);
                    _inputLockToken = 0;
                }
                LocalInputLocker.Ensure().ForceGameplayCursor();
                if (optionsPanel != null) optionsPanel.SetActive(false);
            }
        }
    }

    private void ToggleDevPanelFromHotkey()
    {
        if (!CanUseDevPanel() || devPanel == null) return;
        devPanel.SetActive(!devPanel.activeSelf);
    }

    private bool CanUseDevPanel()
    {
        if (allowDevPanelInReleaseBuild) return true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
    }

    private void ResolveOwnerPhotonView()
    {
        if (_ownerPhotonView != null) return;

        if (playerController != null)
            _ownerPhotonView = playerController.GetComponentInParent<PhotonView>();
        if (_ownerPhotonView == null && playerCombat != null)
            _ownerPhotonView = playerCombat.GetComponentInParent<PhotonView>();
        if (_ownerPhotonView == null && cameraOrbit != null)
            _ownerPhotonView = cameraOrbit.GetComponentInParent<PhotonView>();
        if (_ownerPhotonView == null)
            _ownerPhotonView = GetComponentInParent<PhotonView>();
    }

    private bool CanControlLocalPause()
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom)
            return true;
        if (_ownerPhotonView == null)
            return true;
        return _ownerPhotonView.IsMine;
    }

    // UI Buttons
    public void OnResumeButton()
    {
        if (!isOpen) return;
        isOpen = false;
        if (pausePanel != null) pausePanel.SetActive(false);
        if (optionsPanel != null) optionsPanel.SetActive(false);
        if (LocalUIManager.Instance != null) LocalUIManager.Instance.Close("PauseMenu");
        if (_inputLockToken != 0)
        {
            LocalInputLocker.Ensure().Release(_inputLockToken);
            _inputLockToken = 0;
        }
        LocalInputLocker.Ensure().ForceGameplayCursor();
    }

    public void OnOptionsButton()
    {
        if (!isOpen) return;
        if (optionsPanel != null)
        {
            // Toggle an options subpanel while staying paused
            optionsPanel.SetActive(!optionsPanel.activeSelf);
        }
    }

    public void OnToggleDevPanelButton()
    {
        ToggleDevPanelFromHotkey();
    }

    public void OnLeaveGameButton()
    {
        // fully close pause/dev ui and clear local state before switching to homescreen
        isOpen = false;
        if (pausePanel != null) pausePanel.SetActive(false);
        if (optionsPanel != null) optionsPanel.SetActive(false);
        if (devPanel != null) devPanel.SetActive(false);

        if (LocalUIManager.Instance != null)
            LocalUIManager.Instance.ForceClose();

        var locker = LocalInputLocker.Ensure();
        if (locker != null)
        {
            if (_inputLockToken != 0)
            {
                locker.Release(_inputLockToken);
                _inputLockToken = 0;
            }
            locker.ReleaseAllForOwner("PauseMenu");
            locker.ReleaseAllForOwner("DevPanel");
            // switch into menu mode so homescreen always has a free cursor
            locker.EnterMenuMode();
        }

        // guard against old pause scripts that may have set timescale
        Time.timeScale = 1f;

        // IMPORTANT: never use PhotonNetwork.LoadLevel for "Leave Game".
        // With AutomaticallySyncScene enabled, host would pull everyone back to menu.
        // We want local exit only, so leave/disconnect locally and then load menu locally.
        NetworkManager.ForceDisconnectAndCleanup("[PauseMenuController] OnLeaveGameButton");
        SceneManager.LoadScene(mainMenuSceneName);
    }

    public void OnLoadTestingButton()
    {
        LoadGameplaySceneFromEscapePanel(testingSceneName);
    }

    public void OnLoadFirstMapButton()
    {
        LoadGameplaySceneFromEscapePanel(firstMapSceneName);
    }

    public void OnLoadBakunawaBossFightButton()
    {
        if (string.IsNullOrWhiteSpace(bakunawaBossFightSceneName))
        {
            Debug.LogWarning("[PauseMenuController] Bakunawa boss scene name is empty.");
            return;
        }

        bool inNetworkRoom = PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode;
        if (inNetworkRoom && !PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("[PauseMenuController] Only the master client can start a scene transition.");
            return;
        }

        PrepareEscapePanelBeforeSceneChange();

        if (inNetworkRoom)
        {
            if (!NetworkManager.BeginSceneTransition(bakunawaBossFightSceneName))
                MapTransitionManager.BeginTransition(bakunawaBossFightSceneName);
        }
        else if (NetworkManager.Instance != null)
            NetworkManager.Instance.BeginLocalDevSceneLoad(bakunawaBossFightSceneName);
        else
            MapTransitionManager.BeginTransition(bakunawaBossFightSceneName);
    }

    // New methods for scene loading
    public void GoToMainMenu() { SceneManager.LoadScene(mainMenuSceneName); }
    public void GoToTestingScene() { LoadGameplaySceneFromEscapePanel(testingSceneName); }
    public void GoToFirstMap() { LoadGameplaySceneFromEscapePanel(firstMapSceneName); }
    public void GoToSecondMap() { LoadGameplaySceneFromEscapePanel(secondMapSceneName); }
    public void GoToThirdMap() { LoadGameplaySceneFromEscapePanel(thirdMapSceneName); }
    public void GoToFourthMap() { LoadGameplaySceneFromEscapePanel(fourthMapSceneName); }

    /// <summary>
    /// Escape / dev panel: use the same transition + spawn init as gameplay so local player, HUD, and input are correct.
    /// Online room: host only, uses <see cref="NetworkManager.BeginSceneTransition"/>.
    /// Not in a room: local load + spawn coordinator on <see cref="NetworkManager"/>.
    /// </summary>
    private void LoadGameplaySceneFromEscapePanel(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("[PauseMenuController] Scene name is empty.");
            return;
        }

        PrepareEscapePanelBeforeSceneChange();

        bool inRoom = PhotonNetwork.InRoom;
        bool onlineMultiplayer = PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && inRoom;

        if (inRoom)
        {
            if (onlineMultiplayer && !PhotonNetwork.IsMasterClient)
            {
                Debug.LogWarning("[PauseMenuController] Only the host can load a scene for everyone. Joiners stay in the current scene.");
                return;
            }

            if (!NetworkManager.BeginSceneTransition(sceneName))
                Debug.LogError($"[PauseMenuController] BeginSceneTransition failed for '{sceneName}'.");
            return;
        }

        if (NetworkManager.Instance != null)
            NetworkManager.Instance.BeginLocalDevSceneLoad(sceneName);
        else
            SceneManager.LoadScene(sceneName);
    }

    private void PrepareEscapePanelBeforeSceneChange()
    {
        isOpen = false;
        if (pausePanel != null) pausePanel.SetActive(false);
        if (optionsPanel != null) optionsPanel.SetActive(false);
        if (devPanel != null) devPanel.SetActive(false);

        if (LocalUIManager.Instance != null)
            LocalUIManager.Instance.ForceClose();

        var locker = LocalInputLocker.Ensure();
        if (locker != null)
        {
            if (_inputLockToken != 0)
            {
                locker.Release(_inputLockToken);
                _inputLockToken = 0;
            }
            locker.ReleaseAllForOwner("PauseMenu");
            locker.ReleaseAllForOwner("DevPanel");
            locker.ReleaseAllForOwner("Inventory");
            locker.ReleaseAllForOwner("QuestList");
            locker.ReleaseAllForOwner("CutsceneManager");
            locker.ReleaseAllForOwner("MinimapFullMap");
            locker.ForceGameplayCursor();
        }

        Time.timeScale = 1f;
    }

    // Example: Show/hide pause panel
    public void ShowPausePanel(bool show)
    {
        if (pausePanel != null) pausePanel.SetActive(show);
    }
    public void ShowOptionsPanel(bool show)
    {
        if (optionsPanel != null) optionsPanel.SetActive(show);
    }

    // Terrain generation function for button
    public void GenerateTerrain()
    {
        if (terrainGenerator != null)
        {
            terrainGenerator.GenerateTerrain();
        }
    }

    // Regenerate terrain and all resources
    public void RegenerateTerrainAndResources()
    {
        if (terrainGenerator == null)
        {
            terrainGenerator = FindFirstObjectByType<TerrainGenerator>();
        }
        if (mapResourcesGenerator == null)
        {
            mapResourcesGenerator = FindFirstObjectByType<MapResourcesGenerator>();
        }

        if (terrainGenerator == null)
        {
            Debug.LogWarning("[PauseMenuController] TerrainGenerator not found! Cannot regenerate terrain.");
            return;
        }

        Debug.Log("[PauseMenuController] Starting terrain and resource regeneration...");

        // Change seed to ensure different terrain
        int oldSeed = terrainGenerator.seed;
        terrainGenerator.seed = Random.Range(int.MinValue, int.MaxValue);
        
        Debug.Log($"[PauseMenuController] Regenerating terrain with seed: {terrainGenerator.seed}");
        
        // Force regeneration using the new method
        terrainGenerator.ForceRegenerate();
        
        // Restore original seed
        terrainGenerator.seed = oldSeed;

        // resources regenerate automatically when terrain generation completes.
        Debug.Log("[PauseMenuController] Terrain regeneration triggered. MapResourcesGenerator will regenerate after terrain completion.");
    }

    // Button to activate custom UI panels (HUD/stats/skills)
    public void OnActivateCustomUIPanelsButton()
    {
        if (customUIPanels != null)
        {
            foreach (var panel in customUIPanels)
            {
                if (panel != null) panel.SetActive(true);
            }
        }
    }
    
    /// <summary>
    /// Teleports the freelook camera to the next ship remnant location.
    /// Cycles through all found remnants.
    /// </summary>
    public void OnTeleportCameraToRemnantsButton()
    {
        if (freelookCamera == null)
        {
            Debug.LogWarning("[PauseMenuController] Freelook camera not found! Cannot teleport.");
            return;
        }
        
        // Find all ship remnants in the scene
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        List<GameObject> remnants = new List<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (obj != null && obj.name.Contains("ShipRemnants"))
            {
                remnants.Add(obj);
            }
        }
        
        if (remnants.Count == 0)
        {
            Debug.LogWarning("[PauseMenuController] No ship remnants found in the scene!");
            return;
        }
        
        // Sort by name for consistent ordering
        remnants.Sort((a, b) => string.Compare(a.name, b.name));
        
        // Cycle to next remnant
        GameObject targetRemnant = remnants[currentRemnantIndex % remnants.Count];
        currentRemnantIndex = (currentRemnantIndex + 1) % remnants.Count;
        
        Vector3 targetPosition = targetRemnant.transform.position;
        Vector3 cameraPosition = targetPosition;
        cameraPosition.y += 3f; // Offset upward for better view
        
        // Enable freelook camera if it's not active
        if (!freelookCamera.activeSelf)
        {
            if (freelookToggle != null)
            {
                if (!freelookToggle.IsFreelookActive())
                {
                    freelookToggle.ToggleFreelook();
                }
            }
            else
            {
                freelookCamera.SetActive(true);
                FreestyleCameraController controller = freelookCamera.GetComponent<FreestyleCameraController>();
                if (controller != null) controller.enabled = true;
                Camera cam = freelookCamera.GetComponent<Camera>();
                if (cam != null) cam.enabled = true;
            }
        }
        
        freelookCamera.transform.position = cameraPosition;
        
        // Look at the remnant
        Vector3 lookDirection = (targetRemnant.transform.position - cameraPosition).normalized;
        if (lookDirection != Vector3.zero)
        {
            freelookCamera.transform.rotation = Quaternion.LookRotation(lookDirection);
        }
        
        Debug.Log($"[PauseMenuController] Teleported freelook camera to {targetRemnant.name} at {cameraPosition}");
    }
    
    /// <summary>
    /// Teleports the player to the next ship remnant location.
    /// Cycles through all found remnants.
    /// </summary>
    public void OnTeleportPlayerToRemnantsButton()
    {
        // Find all ship remnants in the scene
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        List<GameObject> remnants = new List<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (obj != null && obj.name.Contains("ShipRemnants"))
            {
                remnants.Add(obj);
            }
        }
        
        if (remnants.Count == 0)
        {
            Debug.LogWarning("[PauseMenuController] No ship remnants found in the scene!");
            return;
        }
        
        // Sort by name for consistent ordering
        remnants.Sort((a, b) => string.Compare(a.name, b.name));
        
        // Cycle to next remnant
        GameObject targetRemnant = remnants[currentRemnantIndex % remnants.Count];
        currentRemnantIndex = (currentRemnantIndex + 1) % remnants.Count;
        
        Vector3 targetPosition = targetRemnant.transform.position;
        
        // Teleport player to remnant position
        TeleportPlayerToPosition(targetPosition);
        
        Debug.Log($"[PauseMenuController] Teleported player to {targetRemnant.name} at {targetPosition}");
    }
    
    /// <summary>
    /// Teleports the freelook camera to the broken ship location.
    /// </summary>
    public void OnTeleportCameraToBrokenShipButton()
    {
        if (freelookCamera == null)
        {
            Debug.LogWarning("[PauseMenuController] Freelook camera not found! Cannot teleport.");
            return;
        }
        
        // Find the broken ship in the scene
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        GameObject brokenShip = null;
        
        foreach (GameObject obj in allObjects)
        {
            if (obj != null && (obj.name.Contains("BrokenShip") || obj.name.Contains("brokenShip") || 
                (obj.name.Contains("Ship") && obj.name.Contains("Broken")) ||
                (obj.name.Contains("ship") && obj.name.Contains("broken"))))
            {
                brokenShip = obj;
                break; // Found it, no need to continue
            }
        }
        
        if (brokenShip == null)
        {
            Debug.LogWarning("[PauseMenuController] Broken ship not found in the scene!");
            return;
        }
        
        Vector3 targetPosition = brokenShip.transform.position;
        Vector3 cameraPosition = targetPosition;
        cameraPosition.y += 5f; // Offset upward for better view (broken ship might be larger)
        
        // Enable freelook camera if it's not active
        if (!freelookCamera.activeSelf)
        {
            if (freelookToggle != null)
            {
                if (!freelookToggle.IsFreelookActive())
                {
                    freelookToggle.ToggleFreelook();
                }
            }
            else
            {
                freelookCamera.SetActive(true);
                FreestyleCameraController controller = freelookCamera.GetComponent<FreestyleCameraController>();
                if (controller != null) controller.enabled = true;
                Camera cam = freelookCamera.GetComponent<Camera>();
                if (cam != null) cam.enabled = true;
            }
        }
        
        freelookCamera.transform.position = cameraPosition;
        
        // Look at the broken ship
        Vector3 lookDirection = (brokenShip.transform.position - cameraPosition).normalized;
        if (lookDirection != Vector3.zero)
        {
            freelookCamera.transform.rotation = Quaternion.LookRotation(lookDirection);
        }
        
        Debug.Log($"[PauseMenuController] Teleported freelook camera to {brokenShip.name} at {cameraPosition}");
    }
    
    /// <summary>
    /// Teleports the player to the broken ship location.
    /// </summary>
    public void OnTeleportPlayerToBrokenShipButton()
    {
        // Find the broken ship in the scene
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        GameObject brokenShip = null;
        
        foreach (GameObject obj in allObjects)
        {
            if (obj != null && (obj.name.Contains("BrokenShip") || obj.name.Contains("brokenShip") || 
                (obj.name.Contains("Ship") && obj.name.Contains("Broken")) ||
                (obj.name.Contains("ship") && obj.name.Contains("broken"))))
            {
                brokenShip = obj;
                break; // Found it, no need to continue
            }
        }
        
        if (brokenShip == null)
        {
            Debug.LogWarning("[PauseMenuController] Broken ship not found in the scene!");
            return;
        }
        
        Vector3 targetPosition = brokenShip.transform.position;
        
        // Teleport player to broken ship position
        TeleportPlayerToPosition(targetPosition);
        
        Debug.Log($"[PauseMenuController] Teleported player to {brokenShip.name} at {targetPosition}");
    }
    
    /// <summary>
    /// Helper method to teleport the local player to a position.
    /// </summary>
    private void TeleportPlayerToPosition(Vector3 targetPosition)
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            Debug.LogWarning("[PauseMenuController] Player not found! Cannot teleport.");
            return;
        }
        
        PhotonView pv = player.GetComponent<PhotonView>();
        if (pv != null && !pv.IsMine)
        {
            Debug.LogWarning("[PauseMenuController] Player is not local! Cannot teleport.");
            return;
        }
        
        // Raycast down to find ground level first
        RaycastHit hit;
        Vector3 groundPosition = targetPosition;
        if (Physics.Raycast(targetPosition + Vector3.up * 10f, Vector3.down, out hit, 30f))
        {
            groundPosition.y = hit.point.y;
        }
        
        // Teleport player 20 units above the ground
        targetPosition.y = groundPosition.y + 20f;
        
        // Teleport player using CharacterController pattern
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.enabled = false;
            player.transform.position = targetPosition;
            cc.enabled = true;
        }
        else
        {
            player.transform.position = targetPosition;
        }
        
        Debug.Log($"[PauseMenuController] Player teleported to {targetPosition}");
    }
    
    /// <summary>
    /// Toggles god mode for the local player (invincibility and unlimited stamina).
    /// </summary>
    public void OnToggleGodModeButton()
    {
        Transform localPlayerTransform = PlayerRegistry.GetLocalPlayerTransform();
        if (localPlayerTransform == null)
        {
            Debug.LogWarning("[PauseMenuController] Local player not found! Cannot toggle god mode.");
            return;
        }

        GameObject player = localPlayerTransform.gameObject;
        PhotonView pv = player.GetComponent<PhotonView>();
        if (pv != null && !pv.IsMine)
        {
            Debug.LogWarning("[PauseMenuController] Player is not local! Cannot toggle god mode.");
            return;
        }
        
        PlayerStats stats = player.GetComponent<PlayerStats>();
        if (stats == null)
        {
            Debug.LogWarning("[PauseMenuController] PlayerStats component not found! Cannot toggle god mode.");
            return;
        }
        
        stats.godMode = !stats.godMode;
        if (stats.godMode)
        {
            stats.currentStamina = stats.maxStamina;
        }
        string status = stats.godMode ? "ENABLED" : "DISABLED";
        Debug.Log($"[PauseMenuController] God mode {status}");
    }
    
    /// <summary>
    /// Spawns an enemy in front of the local player.
    /// </summary>
    private void SpawnEnemyInFrontOfPlayer(string enemyPrefabName)
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            Debug.LogWarning("[PauseMenuController] Player not found! Cannot spawn enemy.");
            return;
        }
        
        PhotonView pv = player.GetComponent<PhotonView>();
        if (pv != null && !pv.IsMine)
        {
            Debug.LogWarning("[PauseMenuController] Player is not local! Cannot spawn enemy.");
            return;
        }
        
        // Try to find EnemyManager first
        EnemyManager enemyManager = FindFirstObjectByType<EnemyManager>();
        
        // Calculate spawn position in front of player
        Vector3 spawnPosition = player.transform.position + player.transform.forward * 5f;
        spawnPosition.y = player.transform.position.y; // Keep at same height
        
        // Raycast down to ground level
        RaycastHit hit;
        if (Physics.Raycast(spawnPosition + Vector3.up * 10f, Vector3.down, out hit, 20f))
        {
            spawnPosition.y = hit.point.y;
        }
        
        Quaternion spawnRotation = Quaternion.LookRotation(player.transform.position - spawnPosition);
        
        // Try using EnemyManager if available
        if (enemyManager != null)
        {
            enemyManager.SpawnEnemy(enemyPrefabName, spawnPosition, spawnRotation);
            Debug.Log($"[PauseMenuController] Spawned {enemyPrefabName} via EnemyManager at {spawnPosition}");
            return;
        }
        
        // Fallback: spawn directly using Resources
        GameObject enemyPrefab = Resources.Load<GameObject>($"Enemies/{enemyPrefabName}");
        if (enemyPrefab == null)
        {
            Debug.LogError($"[PauseMenuController] Enemy prefab '{enemyPrefabName}' not found in Resources/Enemies/!");
            return;
        }
        
        GameObject enemyInstance = null;
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            enemyInstance = PhotonNetwork.Instantiate($"Enemies/{enemyPrefabName}", spawnPosition, spawnRotation);
        }
        else
        {
            enemyInstance = Instantiate(enemyPrefab, spawnPosition, spawnRotation);
        }
        
        if (enemyInstance != null)
        {
            Debug.Log($"[PauseMenuController] Spawned {enemyPrefabName} directly at {spawnPosition}");
        }
        else
        {
            Debug.LogError($"[PauseMenuController] Failed to spawn {enemyPrefabName}!");
        }
    }
    
    public void OnSpawnAmomongoButton()
    {
        SpawnEnemyInFrontOfPlayer("Enemy_Amomongo");
    }
    
    public void OnSpawnBerberokaButton()
    {
        SpawnEnemyInFrontOfPlayer("Enemy_Berberoka");
    }
    
    public void OnSpawnBungisngisButton()
    {
        SpawnEnemyInFrontOfPlayer("Enemy_Bungisngis");
    }
    
    public void OnSpawnKapreButton()
    {
        SpawnEnemyInFrontOfPlayer("Enemy_Kapre");
    }
    
    public void OnSpawnShadowDiwataButton()
    {
        SpawnEnemyInFrontOfPlayer("Enemy_ShadowDiwata");
    }
    
    public void OnSpawnTikbalangButton()
    {
        SpawnEnemyInFrontOfPlayer("Enemy_Tikbalang");
    }

    public void OnSpawnBakunawaBossButton()
    {
        bool inNetworkRoom = PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode;
        if (inNetworkRoom && !PhotonNetwork.IsMasterClient)
        {
            Debug.LogWarning("[PauseMenuController] Only the master client can spawn boss enemies.");
            return;
        }

        SpawnEnemyInFrontOfPlayer("Boss_Bakunawa");
    }
    
    /// <summary>
    /// Teleports the freelook camera to the next common enemy location.
    /// Cycles through Enemy_AswangUnit, Enemy_Manananggal, Enemy_Tiyanak, Enemy_Sigbin.
    /// </summary>
    public void OnTeleportCameraToCommonEnemiesButton()
    {
        if (freelookCamera == null)
        {
            Debug.LogWarning("[PauseMenuController] Freelook camera not found! Cannot teleport.");
            return;
        }
        
        string[] commonEnemyNames = { "Enemy_AswangUnit", "Enemy_Manananggal", "Enemy_Tiyanak", "Enemy_Sigbin" };
        List<GameObject> foundEnemies = new List<GameObject>();
        
        // Find all common enemies in the scene
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        
        foreach (GameObject obj in allObjects)
        {
            if (obj != null)
            {
                foreach (string enemyName in commonEnemyNames)
                {
                    if (obj.name.Contains(enemyName))
                    {
                        foundEnemies.Add(obj);
                        break;
                    }
                }
            }
        }
        
        if (foundEnemies.Count == 0)
        {
            Debug.LogWarning("[PauseMenuController] No common enemies found in the scene!");
            return;
        }
        
        // Sort by name for consistent ordering
        foundEnemies.Sort((a, b) => string.Compare(a.name, b.name));
        
        // Group enemies by type and cycle through types
        Dictionary<string, List<GameObject>> enemiesByType = new Dictionary<string, List<GameObject>>();
        foreach (GameObject enemy in foundEnemies)
        {
            string enemyType = null;
            foreach (string typeName in commonEnemyNames)
            {
                if (enemy.name.Contains(typeName))
                {
                    enemyType = typeName;
                    break;
                }
            }
            
            if (enemyType != null)
            {
                if (!enemiesByType.ContainsKey(enemyType))
                {
                    enemiesByType[enemyType] = new List<GameObject>();
                }
                enemiesByType[enemyType].Add(enemy);
            }
        }
        
        // Cycle through enemy types
        string[] sortedTypes = new List<string>(enemiesByType.Keys).ToArray();
        System.Array.Sort(sortedTypes);
        
        if (sortedTypes.Length == 0)
        {
            Debug.LogWarning("[PauseMenuController] No valid common enemy types found!");
            return;
        }
        
        string targetType = sortedTypes[currentCommonEnemyIndex % sortedTypes.Length];
        currentCommonEnemyIndex = (currentCommonEnemyIndex + 1) % sortedTypes.Length;
        
        // Get the first enemy of this type
        GameObject targetEnemy = enemiesByType[targetType][0];
        Vector3 targetPosition = targetEnemy.transform.position;
        Vector3 cameraPosition = targetPosition;
        cameraPosition.y += 3f; // Offset upward for better view
        
        // Enable freelook camera if it's not active
        if (!freelookCamera.activeSelf)
        {
            if (freelookToggle != null)
            {
                if (!freelookToggle.IsFreelookActive())
                {
                    freelookToggle.ToggleFreelook();
                }
            }
            else
            {
                freelookCamera.SetActive(true);
                FreestyleCameraController controller = freelookCamera.GetComponent<FreestyleCameraController>();
                if (controller != null) controller.enabled = true;
                Camera cam = freelookCamera.GetComponent<Camera>();
                if (cam != null) cam.enabled = true;
            }
        }
        
        freelookCamera.transform.position = cameraPosition;
        
        // Look at the enemy
        Vector3 lookDirection = (targetEnemy.transform.position - cameraPosition).normalized;
        if (lookDirection != Vector3.zero)
        {
            freelookCamera.transform.rotation = Quaternion.LookRotation(lookDirection);
        }
        
        Debug.Log($"[PauseMenuController] Teleported freelook camera to {targetEnemy.name} at {cameraPosition}");
    }
    
    /// <summary>
    /// Teleports the player to the next common enemy location.
    /// Cycles through Enemy_AswangUnit, Enemy_Manananggal, Enemy_Tiyanak, Enemy_Sigbin.
    /// </summary>
    public void OnTeleportPlayerToCommonEnemiesButton()
    {
        string[] commonEnemyNames = { "Enemy_AswangUnit", "Enemy_Manananggal", "Enemy_Tiyanak", "Enemy_Sigbin" };
        List<GameObject> foundEnemies = new List<GameObject>();
        
        // Find all common enemies in the scene
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        
        foreach (GameObject obj in allObjects)
        {
            if (obj != null)
            {
                foreach (string enemyName in commonEnemyNames)
                {
                    if (obj.name.Contains(enemyName))
                    {
                        foundEnemies.Add(obj);
                        break;
                    }
                }
            }
        }
        
        if (foundEnemies.Count == 0)
        {
            Debug.LogWarning("[PauseMenuController] No common enemies found in the scene!");
            return;
        }
        
        // Sort by name for consistent ordering
        foundEnemies.Sort((a, b) => string.Compare(a.name, b.name));
        
        // Group enemies by type and cycle through types
        Dictionary<string, List<GameObject>> enemiesByType = new Dictionary<string, List<GameObject>>();
        foreach (GameObject enemy in foundEnemies)
        {
            string enemyType = null;
            foreach (string typeName in commonEnemyNames)
            {
                if (enemy.name.Contains(typeName))
                {
                    enemyType = typeName;
                    break;
                }
            }
            
            if (enemyType != null)
            {
                if (!enemiesByType.ContainsKey(enemyType))
                {
                    enemiesByType[enemyType] = new List<GameObject>();
                }
                enemiesByType[enemyType].Add(enemy);
            }
        }
        
        // Cycle through enemy types
        string[] sortedTypes = new List<string>(enemiesByType.Keys).ToArray();
        System.Array.Sort(sortedTypes);
        
        if (sortedTypes.Length == 0)
        {
            Debug.LogWarning("[PauseMenuController] No valid common enemy types found!");
            return;
        }
        
        string targetType = sortedTypes[currentCommonEnemyIndex % sortedTypes.Length];
        currentCommonEnemyIndex = (currentCommonEnemyIndex + 1) % sortedTypes.Length;
        
        // Get the first enemy of this type
        GameObject targetEnemy = enemiesByType[targetType][0];
        Vector3 targetPosition = targetEnemy.transform.position;
        
        // Teleport player to enemy position
        TeleportPlayerToPosition(targetPosition);
        
        Debug.Log($"[PauseMenuController] Teleported player to {targetEnemy.name} at {targetPosition}");
    }
}