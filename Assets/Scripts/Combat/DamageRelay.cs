using Photon.Pun;
using UnityEngine;

/// <summary>
/// Central entry point for ALL player damage. Use this from enemies, projectiles, damage zones, debuffs, etc.
/// Ensures damage overlay and other UI always trigger. Never bypass this — call ApplyToPlayer for any player damage.
/// </summary>
public static class DamageRelay
{
    public static void ApplyToPlayer(GameObject player, int amount)
    {
        if (player == null || amount <= 0) return;
        var pv = player.GetComponent<PhotonView>();
        var stats = player.GetComponent<PlayerStats>() ?? player.GetComponentInChildren<PlayerStats>(true);
        // Only route via RPC when actually in a room (or offline mode). Otherwise fall back to local damage to
        // support single-player/editor testing where Photon may be connected but not joined.
        bool canUseRpc = pv != null && (PhotonNetwork.OfflineMode || (PhotonNetwork.IsConnected && PhotonNetwork.InRoom));
        if (canUseRpc)
        {
            if (pv.Owner != null && stats != null)
            {
                // Apply directly when target is local player so damage UI (red overlay) runs immediately
                if (pv.Owner.IsLocal)
                {
                    stats.TakeDamage(amount);
                }
                else
                {
                    // Use All so owner's client always receives; RPC_TakeDamage checks IsMine before applying
                    pv.RPC("RPC_TakeDamage", RpcTarget.All, amount);
                }
            }
            else
            {
                // no known owner (e.g., not properly networked); apply locally as a safe fallback
                if (stats != null)
                {
                    stats.TakeDamage(amount);
                }
            }
        }
        else if (stats != null)
        {
            // local/offline execution path
            stats.TakeDamage(amount);
        }
    }
}
