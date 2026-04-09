using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

/// <summary>
/// static registry of all active PlayerStats instances.
/// eliminates FindObjectsByType<PlayerStats> calls across the codebase.
/// PlayerStats auto-registers on enable and unregisters on disable.
/// </summary>
public static class PlayerRegistry
{
    private static readonly List<PlayerStats> allPlayers = new List<PlayerStats>(4);
    private static Transform cachedLocalPlayer;
    private static bool localPlayerDirty = true;

    public static IReadOnlyList<PlayerStats> All => allPlayers;

    public static void Register(PlayerStats player)
    {
        if (player != null && !allPlayers.Contains(player))
        {
            allPlayers.Add(player);
            localPlayerDirty = true;
        }
    }

    public static void Unregister(PlayerStats player)
    {
        if (allPlayers.Remove(player))
            localPlayerDirty = true;
    }

    /// <summary>
    /// returns the local player transform (PhotonView.IsMine or no PhotonView).
    /// cached until the registry changes.
    /// </summary>
    public static Transform GetLocalPlayerTransform()
    {
        if (!localPlayerDirty && IsValidLocalPlayer(cachedLocalPlayer))
            return cachedLocalPlayer;

        cachedLocalPlayer = null;
        for (int i = 0; i < allPlayers.Count; i++)
        {
            var p = allPlayers[i];
            if (p == null) continue;
            if (IsValidLocalPlayer(p.transform))
            {
                cachedLocalPlayer = p.transform;
                break;
            }
        }

        if (cachedLocalPlayer == null)
        {
            var tagged = GameObject.FindWithTag("Player");
            if (IsValidLocalPlayer(tagged != null ? tagged.transform : null))
                cachedLocalPlayer = tagged.transform;
        }

        localPlayerDirty = false;
        return cachedLocalPlayer;
    }

    /// <summary>
    /// finds the nearest player with a CharacterController (actual player bodies).
    /// </summary>
    public static Transform FindNearest(Vector3 from)
    {
        Transform nearest = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < allPlayers.Count; i++)
        {
            var p = allPlayers[i];
            if (p == null) continue;
            if (p.GetComponent<CharacterController>() == null) continue;
            float dSqr = (from - p.transform.position).sqrMagnitude;
            if (dSqr < bestSqr)
            {
                bestSqr = dSqr;
                nearest = p.transform;
            }
        }
        return nearest;
    }

    /// <summary>
    /// returns all player transforms as an array (allocates — use sparingly).
    /// for hot paths, iterate All directly.
    /// </summary>
    public static PlayerStats[] ToArray()
    {
        return allPlayers.ToArray();
    }

    /// <summary>
    /// clears the registry. called on scene unload to avoid stale references.
    /// </summary>
    public static void Clear()
    {
        allPlayers.Clear();
        cachedLocalPlayer = null;
        localPlayerDirty = true;
    }

    /// <summary>
    /// returns a stable join-order index for the local player.
    /// uses sorted active room players by actor number, avoiding gaps from rejoins.
    /// </summary>
    public static int GetLocalJoinOrderIndex(int count)
    {
        if (count <= 0)
            return 0;

        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer != null)
        {
            Player[] roomPlayers = PhotonNetwork.PlayerList;
            if (roomPlayers != null && roomPlayers.Length > 0)
            {
                List<Player> sorted = new List<Player>(roomPlayers);
                sorted.Sort((a, b) => a.ActorNumber.CompareTo(b.ActorNumber));

                int localActorNumber = PhotonNetwork.LocalPlayer.ActorNumber;
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (sorted[i] != null && sorted[i].ActorNumber == localActorNumber)
                        return Mathf.Clamp(i, 0, count - 1);
                }
            }

            // fallback for unexpected player list state
            return Mathf.Clamp(PhotonNetwork.LocalPlayer.ActorNumber - 1, 0, count - 1);
        }

        return 0;
    }

    private static bool IsValidLocalPlayer(Transform playerTransform)
    {
        if (playerTransform == null || !playerTransform.gameObject.activeInHierarchy)
            return false;

        var pv = playerTransform.GetComponent<PhotonView>();
        bool requireNetworkOwnership = PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode;
        if (requireNetworkOwnership)
        {
            if (pv == null || !pv.IsMine)
                return false;

            // In network rooms, never resolve a carried-over DontDestroyOnLoad player as "local".
            return playerTransform.gameObject.scene == SceneManager.GetActiveScene();
        }

        return pv == null || pv.IsMine;
    }
}
