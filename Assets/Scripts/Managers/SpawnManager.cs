using UnityEngine;
using Photon.Pun;
using System.Collections;
using UnityEngine.SceneManagement;

public class TutorialSpawnManager : MonoBehaviourPunCallbacks
{
    [Header("Assign 4 spawn points in inspector")]
    public Transform[] spawnPoints; // Size 4, assign in inspector
    [Header("Assign your player prefabs (must be in Resources)")]
    public GameObject playerPrefab;
    [Tooltip("Optional. Player 2 prefab for 2nd spawn.")]
    public GameObject playerPrefab2;
    [Tooltip("Optional. Player 3 prefab for 3rd spawn.")]
    public GameObject playerPrefab3;
    [Tooltip("Optional. Player 4 prefab for 4th spawn.")]
    public GameObject playerPrefab4;
    private Coroutine pendingNetworkSpawnRoutine;

    [PunRPC]
    public void RPC_SpawnPlayerForThisClient()
    {
        ApplySpawnPlayerForThisClient();
    }
    
    private void ApplySpawnPlayerForThisClient()
    {
        Debug.Log($"[TutorialSpawnManager] ApplySpawnPlayerForThisClient called. Connected={PhotonNetwork.IsConnected}, InRoom={PhotonNetwork.InRoom}, OfflineMode={PhotonNetwork.OfflineMode}");

        CleanupStaleLocalPlayersOutsideActiveScene();

        bool onlineConnected = PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode;
        if (onlineConnected && !PhotonNetwork.InRoom)
        {
            if (pendingNetworkSpawnRoutine == null)
            {
                pendingNetworkSpawnRoutine = StartCoroutine(Co_WaitForRoomThenSpawn());
            }
            else
            {
                Debug.Log("[TutorialSpawnManager] Spawn request queued; already waiting for room join.");
            }
            return;
        }

        GameObject existingPlayer = FindExistingLocalPlayer();
        if (existingPlayer != null)
        {
            Debug.Log("[TutorialSpawnManager] Local player already exists (preserved from previous scene), skipping spawn.");
            return;
        }
        
        int spawnIndex = 0;
        int prefabSlotIndex = 0;
        Vector3 spawnPos = (spawnPoints != null && spawnPoints.Length > 0) ? spawnPoints[spawnIndex].position : Vector3.zero;
        Quaternion spawnRotation = Quaternion.identity;
        GameObject player = null;
        
        // Unified spawn logic for both online and offline
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            prefabSlotIndex = PlayerRegistry.GetLocalJoinOrderIndex(4);

            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                spawnIndex = PlayerRegistry.GetLocalJoinOrderIndex(spawnPoints.Length);
                spawnPos = spawnPoints[spawnIndex] != null ? spawnPoints[spawnIndex].position : Vector3.zero;
            }
            else
            {
                spawnIndex = 0;
                // FIRSTMAP has TutorialSpawnManager.spawnPoints empty; use the same spawn-marker selection
                // logic as PlayerSpawnCoordinator so we instantiate directly at SpawnMarker_*.
                if (PlayerSpawnCoordinator.TryGetBestSpawnPosition(out Vector3 bestPos, out Vector3 faceDir, out string source, requireSpawnMarkers: true))
                {
                    spawnPos = bestPos;
                    Vector3 flatDir = faceDir;
                    flatDir.y = 0f;
                    if (flatDir.sqrMagnitude > 0.0001f)
                        spawnRotation = Quaternion.LookRotation(flatDir.normalized, Vector3.up);

                    Debug.Log($"[TutorialSpawnManager] No spawnPoints assigned. Spawning at {source} directly.");
                }
                else
                {
                    spawnPos = Vector3.zero;
                    spawnRotation = Quaternion.identity;
                    Debug.LogWarning("[TutorialSpawnManager] No spawnPoints assigned and SpawnMarker_* not ready. Spawning at origin.");
                }
            }
            GameObject prefab = GetPlayerPrefabForSpawnIndex(prefabSlotIndex);
            int actor = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;
            Debug.Log($"[TutorialSpawnManager] Spawning player (network) actor={actor}, modelSlot={prefabSlotIndex}, spawnIndex={spawnIndex}, position={spawnPos}, prefab={prefab.name}");
            player = PhotonNetwork.Instantiate(prefab.name, spawnPos, spawnRotation);
        }
        else
        {
            prefabSlotIndex = 0;
            GameObject prefab = GetPlayerPrefabForSpawnIndex(prefabSlotIndex);
            Debug.Log($"[TutorialSpawnManager] Spawning player (offline/local) at index {spawnIndex}, position {spawnPos}, prefab {prefab.name}");
            player = Instantiate(prefab, spawnPos, spawnRotation);
        }

        player = EnsureSpawnedPlayerHasRequiredLocalUI(player, spawnPos, spawnRotation);
        
        // camera setup after spawn
        if (player != null)
        {
            Camera cam = player.transform.Find("Camera")?.GetComponent<Camera>();
            if (cam != null)
            {
                cam.enabled = true;
                cam.tag = "MainCamera";
            }
            // assign camera orbit target if needed
            var cameraOrbit = player.GetComponentInChildren<ThirdPersonCameraOrbit>();
            if (cameraOrbit != null)
            {
                Transform cameraPivot = player.transform.Find("Camera/CameraPivot/TPCamera");
                if (cameraPivot != null)
                {
                    cameraOrbit.AssignTargets(player.transform, cameraPivot);
                }
            }
        }
        else
        {
            Debug.LogError("[TutorialSpawnManager] Failed to spawn player! Player prefab is null or spawn failed.");
        }
    }

    private IEnumerator Co_WaitForRoomThenSpawn()
    {
        float elapsed = 0f;
        const float timeout = 15f;
        while (!PhotonNetwork.InRoom && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        pendingNetworkSpawnRoutine = null;

        if (!PhotonNetwork.InRoom)
        {
            Debug.LogError("[TutorialSpawnManager] Spawn aborted: connected online but never joined room before timeout.");
            yield break;
        }

        ApplySpawnPlayerForThisClient();
    }

	// public entrypoint used by other managers; triggers RPC so each client spawns themselves
	public void SpawnPlayerForThisClient()
	{
	    // this API is local-only by design. PhotonNetwork.Instantiate already syncs spawned object to others.
	    ApplySpawnPlayerForThisClient();
	}

    private GameObject GetPlayerPrefabForSpawnIndex(int spawnIndex)
    {
        GameObject selected = playerPrefab;
        switch (spawnIndex)
        {
            case 1: selected = playerPrefab2 != null ? playerPrefab2 : playerPrefab; break;
            case 2: selected = playerPrefab3 != null ? playerPrefab3 : playerPrefab; break;
            case 3: selected = playerPrefab4 != null ? playerPrefab4 : playerPrefab; break;
            default: selected = playerPrefab; break;
        }

        // Safety: if variant prefab is missing core gameplay/UI wiring, fall back to Player1.
        if (selected != null && playerPrefab != null && selected != playerPrefab && !HasRequiredPlayerComponents(selected))
        {
            Debug.LogWarning($"[TutorialSpawnManager] Selected prefab '{selected.name}' is missing required player components/UI. Falling back to '{playerPrefab.name}'.");
            return playerPrefab;
        }

        return selected;
    }

    private bool HasRequiredPlayerComponents(GameObject prefab)
    {
        if (prefab == null) return false;

        bool hasController = prefab.GetComponent<ThirdPersonController>() != null
            || prefab.GetComponentInChildren<ThirdPersonController>(true) != null;
        bool hasStats = prefab.GetComponent<PlayerStats>() != null
            || prefab.GetComponentInChildren<PlayerStats>(true) != null;
        bool hasInventoryUI = prefab.GetComponentInChildren<InventoryUI>(true) != null;
        bool hasQuestListUI = prefab.GetComponentInChildren<QuestListUI>(true) != null;

        return hasController && hasStats && hasInventoryUI && hasQuestListUI;
    }

    private GameObject EnsureSpawnedPlayerHasRequiredLocalUI(GameObject spawnedPlayer, Vector3 spawnPos, Quaternion spawnRotation)
    {
        if (spawnedPlayer == null || playerPrefab == null)
            return spawnedPlayer;

        var pv = spawnedPlayer.GetComponent<PhotonView>();
        bool isLocalOwner = pv == null || pv.IsMine;
        if (!isLocalOwner)
            return spawnedPlayer;

        if (HasRequiredPlayerComponents(spawnedPlayer))
            return spawnedPlayer;

        Debug.LogWarning($"[TutorialSpawnManager] Spawned player '{spawnedPlayer.name}' is missing required local UI/gameplay components. Re-spawning with '{playerPrefab.name}'.");

        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            if (pv != null && pv.IsMine)
                PhotonNetwork.Destroy(spawnedPlayer);
            return PhotonNetwork.Instantiate(playerPrefab.name, spawnPos, spawnRotation);
        }

        Destroy(spawnedPlayer);
        return Instantiate(playerPrefab, spawnPos, spawnRotation);
    }

    private GameObject FindExistingLocalPlayer()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        var t = PlayerRegistry.GetLocalPlayerTransform();
        if (t != null && t.gameObject.scene == activeScene) return t.gameObject;

        var tagged = GameObject.FindGameObjectWithTag("Player");
        if (tagged != null)
        {
            if (tagged.scene != activeScene)
                return null;

            var pv = tagged.GetComponent<PhotonView>();
            bool requireNetworkOwnership = PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode;
            if (requireNetworkOwnership)
            {
                if (pv != null && pv.IsMine)
                    return tagged;

                if (pv == null)
                    Debug.LogWarning("[TutorialSpawnManager] Ignoring non-network tagged Player while in room; spawning Photon player instead.");
            }
            else if (pv == null || !PhotonNetwork.IsConnected || pv.IsMine)
                return tagged;
        }

        return null;
    }

    private void CleanupStaleLocalPlayersOutsideActiveScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        var allViews = FindObjectsByType<PhotonView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allViews.Length; i++)
        {
            PhotonView view = allViews[i];
            if (view == null || !view.IsMine) continue;
            if (view.gameObject.scene == activeScene) continue;
            if (!view.CompareTag("Player")) continue;

            Debug.Log($"[TutorialSpawnManager] Removing stale local player from non-active scene before spawn: {view.gameObject.name}");
            Destroy(view.gameObject);
        }
    }
}
