using UnityEngine;

/// <summary>
/// Attach to any prefab with a trigger Collider to damage players on contact.
/// Always uses DamageRelay so the damage overlay and all UI trigger correctly.
/// Use this for hazards, traps, environmental damage, etc.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PlayerDamageOnTrigger : MonoBehaviour
{
    [Header("Damage")]
    public int damage = 10;
    [Tooltip("If true, each player takes damage only once per trigger enter")]
    public bool oneHitPerPlayer = false;

    private System.Collections.Generic.HashSet<PlayerStats> hitPlayers;

    private void OnTriggerEnter(Collider other)
    {
        TryApplyDamage(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryApplyDamage(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision != null && collision.collider != null)
            TryApplyDamage(collision.collider);
    }

    private void TryApplyDamage(Collider other)
    {
        if (other == null) return;

        var ps = other.GetComponentInParent<PlayerStats>();
        if (ps == null) return;

        if (oneHitPerPlayer)
        {
            hitPlayers ??= new System.Collections.Generic.HashSet<PlayerStats>();
            if (hitPlayers.Contains(ps)) return;
            hitPlayers.Add(ps);
        }

        DamageRelay.ApplyToPlayer(ps.gameObject, damage);
    }
}
