using UnityEngine;
using Photon.Pun;
using System.Collections;

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
                spawnPos = Vector3.zero;
                Debug.LogWarning("[TutorialSpawnManager] No spawnPoints assigned. Spawning at origin; PlayerSpawnCoordinator should reposition to SpawnMarker_*. ");
            }
            GameObject prefab = GetPlayerPrefabForSpawnIndex(prefabSlotIndex);
            int actor = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;
            Debug.Log($"[TutorialSpawnManager] Spawning player (network) actor={actor}, modelSlot={prefabSlotIndex}, spawnIndex={spawnIndex}, position={spawnPos}, prefab={prefab.name}");
            player = PhotonNetwork.Instantiate(prefab.name, spawnPos, Quaternion.identity);
        }
        else
        {
            prefabSlotIndex = 0;
            GameObject prefab = GetPlayerPrefabForSpawnIndex(prefabSlotIndex);
            Debug.Log($"[TutorialSpawnManager] Spawning player (offline/local) at index {spawnIndex}, position {spawnPos}, prefab {prefab.name}");
            player = Instantiate(prefab, spawnPos, Quaternion.identity);
        }
        
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
        switch (spawnIndex)
        {
            case 1: return playerPrefab2 != null ? playerPrefab2 : playerPrefab;
            case 2: return playerPrefab3 != null ? playerPrefab3 : playerPrefab;
            case 3: return playerPrefab4 != null ? playerPrefab4 : playerPrefab;
            default: return playerPrefab;
        }
    }

    private GameObject FindExistingLocalPlayer()
    {
        var t = PlayerRegistry.GetLocalPlayerTransform();
        if (t != null) return t.gameObject;

        var tagged = GameObject.FindGameObjectWithTag("Player");
        if (tagged != null)
        {
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
}
