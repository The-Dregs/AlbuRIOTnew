using Photon.Pun;
using UnityEngine;

// utility for applying status effects to players in a network-safe way
public static class StatusEffectRelay
{
    public enum EffectType
    {
        Slow = 0,
        Root = 1,
        Silence = 2,
        Stun = 3,
        DefenseDown = 4,
        Bleed = 5,
        StaminaBurn = 6
    }

    // generic entry
    public static void Apply(GameObject player, EffectType type, float magnitude, float duration)
    {
        if (player == null) return;
        var pv = player.GetComponent<PhotonView>();
        var stats = player.GetComponent<PlayerStats>();
        bool canUseRpc = pv != null && (PhotonNetwork.OfflineMode || (PhotonNetwork.IsConnected && PhotonNetwork.InRoom));
        if (canUseRpc)
        {
            if (pv.Owner != null)
            {
                pv.RPC("RPC_ApplyDebuff", pv.Owner, (int)type, magnitude, duration);
            }
            else if (stats != null)
            {
                stats.ApplyDebuff((int)type, magnitude, duration);
            }
        }
        else if (stats != null)
        {
            stats.ApplyDebuff((int)type, magnitude, duration);
        }
    }

    // convenience helpers
    public static void ApplySlow(GameObject player, float slowPercent, float duration) => Apply(player, EffectType.Slow, Mathf.Clamp01(slowPercent), duration);
    public static void ApplyRoot(GameObject player, float duration) => Apply(player, EffectType.Root, 1f, duration);
    public static void ApplySilence(GameObject player, float duration) => Apply(player, EffectType.Silence, 1f, duration);
    public static void ApplyStun(GameObject player, float duration) => Apply(player, EffectType.Stun, 1f, duration);
    // defense down magnitude: 0.2 => take 20% more damage
    public static void ApplyDefenseDown(GameObject player, float percentMoreDamage, float duration) => Apply(player, EffectType.DefenseDown, Mathf.Max(0f, percentMoreDamage), duration);
    // bleed magnitude is damage per tick (0.5s)
    public static void ApplyBleed(GameObject player, float damagePerTick, float duration) => Apply(player, EffectType.Bleed, Mathf.Max(0f, damagePerTick), duration);
    // stamina burn magnitude is stamina per tick (0.5s)
    public static void ApplyStaminaBurn(GameObject player, float staminaPerTick, float duration) => Apply(player, EffectType.StaminaBurn, Mathf.Max(0f, staminaPerTick), duration);
}
