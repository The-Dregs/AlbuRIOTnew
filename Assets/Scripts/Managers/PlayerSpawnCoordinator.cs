using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

public static class PlayerSpawnCoordinator
{
    private const string WaterLayerName = "Water";
    private const float SpawnHeightOffset = 1.25f;
    private const float ShorelineHeightThreshold = 0.22f;

    public static IEnumerator EnsureLocalPlayerAtSpawn(
        float maxWaitSeconds = 15f,
        bool waitForSpawnMarkers = true,
        bool enableDebugLogs = true,
        string logPrefix = "[PlayerSpawnCoordinator]")
    {
        float elapsed = 0f;
        float nextTutorialSpawnAttempt = 0f;
        float nextNetworkSpawnAttempt = 0.75f;
        const float retryInterval = 1.5f;

        while (elapsed < maxWaitSeconds)
        {
            GameObject player = FindLocalPlayer();

            if (player == null)
            {
                if (elapsed >= nextTutorialSpawnAttempt)
                {
                    var spawnMgr = Object.FindFirstObjectByType<TutorialSpawnManager>();
                    if (spawnMgr != null)
                    {
                        if (enableDebugLogs) Debug.Log($"{logPrefix} No local player, requesting spawn via TutorialSpawnManager.");
                        spawnMgr.SpawnPlayerForThisClient();
                    }
                    nextTutorialSpawnAttempt = elapsed + retryInterval;
                }

                if (elapsed >= nextNetworkSpawnAttempt)
                {
                    var netMgr = NetworkManager.Instance != null ? NetworkManager.Instance : Object.FindFirstObjectByType<NetworkManager>();
                    if (netMgr != null)
                    {
                        if (enableDebugLogs) Debug.Log($"{logPrefix} No local player, requesting NetworkManager.SpawnPlayer().");
                        netMgr.SpawnPlayer();
                    }
                    nextNetworkSpawnAttempt = elapsed + retryInterval;
                }
            }
            else
            {
                if (TryGetBestSpawnPosition(out Vector3 spawnPosition, out Vector3 faceDirection, out string source, waitForSpawnMarkers))
                {
                    TeleportAndSetupPlayer(player, spawnPosition, faceDirection);
                    PlayerSpawnManager.hasTeleportedByLoader = true;
                    if (enableDebugLogs) Debug.Log($"{logPrefix} Local player placed at {spawnPosition} (source: {source}).");
                    yield break;
                }

                if (enableDebugLogs)
                {
                    Debug.Log($"{logPrefix} Player exists but spawn markers are not ready yet, waiting...");
                }
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        GameObject fallbackPlayer = FindLocalPlayer();
        if (fallbackPlayer != null)
        {
            if (!TryGetBestSpawnPosition(out Vector3 fallbackPosition, out Vector3 fallbackFace, out string _, false))
            {
                fallbackPosition = fallbackPlayer.transform.position;
                fallbackFace = fallbackPlayer.transform.forward;
            }

            TeleportAndSetupPlayer(fallbackPlayer, fallbackPosition, fallbackFace);
            PlayerSpawnManager.hasTeleportedByLoader = true;
            if (enableDebugLogs) Debug.LogWarning($"{logPrefix} Timed out waiting for ideal spawn data. Used fallback position {fallbackPosition}.");
        }
        else if (enableDebugLogs)
        {
            Debug.LogWarning($"{logPrefix} Timed out waiting for local player spawn.");
        }
    }

    public static bool TryGetBestSpawnPosition(out Vector3 spawnPosition, out Vector3 faceDirection, out string source, bool requireSpawnMarkers)
    {
        if (PlayerSpawnManager.nextSpawnPosition.HasValue)
        {
            ResolveDryShoreSpawn(PlayerSpawnManager.nextSpawnPosition.Value, out spawnPosition, out faceDirection);
            PlayerSpawnManager.nextSpawnPosition = null;
            source = "PlayerSpawnManager.nextSpawnPosition";
            return true;
        }

        if (TryGetSpawnMarkerPosition(out spawnPosition))
        {
            ResolveDryShoreSpawn(spawnPosition, out spawnPosition, out faceDirection);
            source = "SpawnMarker_*";
            return true;
        }

        if (requireSpawnMarkers)
        {
            source = string.Empty;
            spawnPosition = Vector3.zero;
            faceDirection = Vector3.forward;
            return false;
        }

        if (TryGetTutorialSpawnPosition(out spawnPosition))
        {
            ResolveDryShoreSpawn(spawnPosition, out spawnPosition, out faceDirection);
            source = "TutorialSpawnManager.spawnPoints";
            return true;
        }

        if (TryGetNetworkSpawnPosition(out spawnPosition))
        {
            ResolveDryShoreSpawn(spawnPosition, out spawnPosition, out faceDirection);
            source = "NetworkManager.spawnPoints";
            return true;
        }

        if (TryGetTerrainFallbackPosition(out spawnPosition))
        {
            ResolveDryShoreSpawn(spawnPosition, out spawnPosition, out faceDirection);
            source = "TerrainCenter";
            return true;
        }

        spawnPosition = Vector3.up * 2f;
        faceDirection = Vector3.forward;
        source = "Default";
        return true;
    }

    public static GameObject FindLocalPlayer()
    {
        var t = PlayerRegistry.GetLocalPlayerTransform();
        if (t != null) return t.gameObject;

        var tagged = GameObject.FindWithTag("Player");
        if (tagged != null)
        {
            var pv = tagged.GetComponent<PhotonView>();
            if (pv == null || pv.IsMine)
                return tagged;
        }

        if (PhotonNetwork.IsConnected)
        {
            var allViews = Object.FindObjectsByType<PhotonView>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var view in allViews)
            {
                if (view != null && view.IsMine && view.CompareTag("Player"))
                    return view.gameObject;
            }

            // fallback for prefabs that don't have Player tag/PlayerStats yet
            foreach (var view in allViews)
            {
                if (view == null || !view.IsMine) continue;
                if (view.GetComponent<ThirdPersonController>() != null ||
                    view.GetComponentInChildren<ThirdPersonController>(true) != null ||
                    view.GetComponent<Inventory>() != null ||
                    view.GetComponentInChildren<Inventory>(true) != null)
                {
                    return view.gameObject;
                }
            }
        }
        else
        {
            // offline fallback: any player-like object
            var controllers = Object.FindObjectsByType<ThirdPersonController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (controllers != null && controllers.Length > 0 && controllers[0] != null)
                return controllers[0].gameObject;
        }

        return null;
    }

    public static void TeleportAndSetupPlayer(GameObject player, Vector3 spawnPosition, Vector3? faceDirection = null)
    {
        if (player == null) return;

        var pv = player.GetComponent<PhotonView>();
        if (pv != null && PhotonNetwork.IsConnected && !pv.IsMine) return;

        var cc = player.GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.enabled = false;
            player.transform.position = spawnPosition;
            cc.enabled = true;
        }
        else
        {
            player.transform.position = spawnPosition;
        }

        if (faceDirection.HasValue && faceDirection.Value.sqrMagnitude > 0.0001f)
        {
            Vector3 dir = faceDirection.Value;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                player.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
        }

        var controller = player.GetComponent<ThirdPersonController>();
        if (controller != null)
        {
            controller.SetCanMove(true);
            controller.SetCanControl(true);
        }

        Camera cam = player.transform.Find("Camera")?.GetComponent<Camera>();
        if (cam != null)
        {
            cam.enabled = true;
            cam.tag = "MainCamera";
        }

        var cameraOrbit = player.GetComponentInChildren<ThirdPersonCameraOrbit>();
        if (cameraOrbit != null)
        {
            Transform cameraPivot = player.transform.Find("Camera/CameraPivot/TPCamera");
            if (cameraPivot != null)
            {
                cameraOrbit.AssignTargets(player.transform, cameraPivot);
            }
            cameraOrbit.SetRotationLocked(false);

            // snap camera to player position instantly so there is no smooth-damp swoosh
            cameraOrbit.SnapToTarget();

            // reassign the controller's cameraPivot so camera-relative movement works
            if (controller != null && cameraOrbit.transform != null)
            {
                controller.cameraPivot = cameraOrbit.transform;
            }
        }

        LocalInputLocker.Ensure()?.ForceGameplayCursor();

        // Ensure cached inventory from previous map is restored on the newly spawned local player.
        var inventory = player.GetComponent<Inventory>() ?? player.GetComponentInChildren<Inventory>(true);
        if (inventory != null)
        {
            inventory.TryRestoreCachedInventoryIfLocalOwner();
        }

        // Restore cached equipped item from previous map.
        var equipMgr = player.GetComponent<EquipmentManager>() ?? player.GetComponentInChildren<EquipmentManager>(true);
        if (equipMgr != null)
        {
            // ensure the equipment manager has a reference to the player's inventory
            if (equipMgr.playerInventory == null && inventory != null)
                equipMgr.playerInventory = inventory;

            equipMgr.TryRestoreCachedEquipment();
        }
    }

    private static bool TryGetSpawnMarkerPosition(out Vector3 spawnPosition)
    {
        List<Transform> markerTransforms = new List<Transform>();

        var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var obj in allObjects)
        {
            if (obj != null && obj.name.StartsWith("SpawnMarker_"))
            {
                markerTransforms.Add(obj.transform);
            }
        }

        if (markerTransforms.Count > 0)
        {
            markerTransforms.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));
            int markerIndex = GetLocalSpawnIndex(markerTransforms.Count);
            Transform marker = markerTransforms[Mathf.Clamp(markerIndex, 0, markerTransforms.Count - 1)];
            if (marker != null)
            {
                spawnPosition = marker.position;
                return true;
            }
        }

        List<Vector3> sharedPositions = new List<Vector3>();
        if (sharedPositions.Count == 0)
        {
            var shared = MapResourcesGenerator.GetSharedSpawnPositions();
            if (shared != null && shared.Count > 0)
            {
                sharedPositions.AddRange(shared);
            }
        }

        if (sharedPositions.Count == 0)
        {
            spawnPosition = Vector3.zero;
            return false;
        }

        int index = GetLocalSpawnIndex(sharedPositions.Count);
        spawnPosition = sharedPositions[Mathf.Clamp(index, 0, sharedPositions.Count - 1)];
        return true;
    }

    private static bool TryGetTutorialSpawnPosition(out Vector3 spawnPosition)
    {
        var spawnManager = Object.FindFirstObjectByType<TutorialSpawnManager>();
        if (spawnManager != null && spawnManager.spawnPoints != null && spawnManager.spawnPoints.Length > 0)
        {
            int idx = GetLocalSpawnIndex(spawnManager.spawnPoints.Length);
            Transform point = spawnManager.spawnPoints[Mathf.Clamp(idx, 0, spawnManager.spawnPoints.Length - 1)];
            if (point != null)
            {
                spawnPosition = point.position;
                return true;
            }
        }

        spawnPosition = Vector3.zero;
        return false;
    }

    private static bool TryGetNetworkSpawnPosition(out Vector3 spawnPosition)
    {
        var networkManager = NetworkManager.Instance != null ? NetworkManager.Instance : Object.FindFirstObjectByType<NetworkManager>();
        if (networkManager != null && networkManager.spawnPoints != null && networkManager.spawnPoints.Length > 0)
        {
            int idx = GetLocalSpawnIndex(networkManager.spawnPoints.Length);
            Transform point = networkManager.spawnPoints[Mathf.Clamp(idx, 0, networkManager.spawnPoints.Length - 1)];
            if (point != null)
            {
                spawnPosition = point.position;
                return true;
            }
        }

        spawnPosition = Vector3.zero;
        return false;
    }

    private static bool TryGetTerrainFallbackPosition(out Vector3 spawnPosition)
    {
        var terrain = Object.FindFirstObjectByType<Terrain>();
        if (terrain != null && terrain.terrainData != null)
        {
            Vector3 terrainSize = terrain.terrainData.size;
            spawnPosition = terrain.transform.position + new Vector3(terrainSize.x * 0.5f, 0f, terrainSize.z * 0.5f);
            spawnPosition.y = terrain.SampleHeight(spawnPosition) + terrain.transform.position.y + 2f;
            return true;
        }

        spawnPosition = Vector3.zero;
        return false;
    }

    private static int GetLocalSpawnIndex(int count)
    {
        return PlayerRegistry.GetLocalJoinOrderIndex(count);
    }

    private static void ResolveDryShoreSpawn(Vector3 candidate, out Vector3 position, out Vector3 faceDirection)
    {
        Terrain terrain = Object.FindFirstObjectByType<Terrain>();
        position = candidate;
        faceDirection = Vector3.forward;

        if (terrain != null && terrain.terrainData != null)
        {
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y + SpawnHeightOffset;

            Vector3 terrainCenter = terrain.transform.position + new Vector3(
                terrain.terrainData.size.x * 0.5f,
                0f,
                terrain.terrainData.size.z * 0.5f);

            Vector3 toCenter = terrainCenter - position;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude < 0.0001f)
            {
                toCenter = Vector3.forward;
            }
            toCenter.Normalize();
            faceDirection = toCenter;

            if (!IsOverWater(position) && !IsInShallowWater(position, terrain))
            {
                return;
            }

            float step = 3f;
            int maxSteps = 50;
            for (int i = 1; i <= maxSteps; i++)
            {
                Vector3 probe = candidate + toCenter * (i * step);
                probe.y = terrain.SampleHeight(probe) + terrain.transform.position.y + SpawnHeightOffset;
                if (!IsOverWater(probe) && !IsInShallowWater(probe, terrain))
                {
                    position = probe;
                    return;
                }
            }
        }
        else
        {
            position.y += SpawnHeightOffset;
        }
    }

    private static bool IsInShallowWater(Vector3 worldPosition, Terrain terrain)
    {
        if (terrain == null || terrain.terrainData == null) return false;
        float height = terrain.SampleHeight(worldPosition);
        float terrainMin = terrain.transform.position.y;
        float terrainMax = terrainMin + terrain.terrainData.size.y;
        float hNorm = (height - terrainMin) / Mathf.Max(0.0001f, terrainMax - terrainMin);
        return hNorm < ShorelineHeightThreshold;
    }

    private static bool IsOverWater(Vector3 worldPosition)
    {
        int waterLayer = LayerMask.NameToLayer(WaterLayerName);
        if (waterLayer < 0)
        {
            return false;
        }

        int mask = 1 << waterLayer;

        Vector3 top = worldPosition + Vector3.up * 0.9f;
        Vector3 bottom = worldPosition + Vector3.up * 0.1f;
        if (Physics.CheckCapsule(top, bottom, 0.45f, mask, QueryTriggerInteraction.Collide))
        {
            return true;
        }

        if (Physics.Raycast(worldPosition + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 15f, mask, QueryTriggerInteraction.Collide))
        {
            return hit.collider != null;
        }

        return false;
    }
}