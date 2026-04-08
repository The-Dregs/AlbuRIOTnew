using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Attach to a GameObject with a trigger Collider.
/// When players enter the trigger, applies damage. Destroys itself after lifetime.
/// Damage area is defined by the GameObject's collider shape.
/// Supports one-hit-per-player or tick damage (damage every N seconds while in zone).
/// </summary>
[RequireComponent(typeof(Collider))]
public class EnemyDamageZone : MonoBehaviour
{
    [Header("Setup (set via Initialize)")]
    [SerializeField] private GameObject owner;
    [SerializeField] private int damage = 1;
    [SerializeField] private float lifetime = 1f;

    [Header("Options")]
    [Tooltip("If true (and tickInterval=0), each player takes damage only once while in the zone")]
    [SerializeField] private bool oneHitPerPlayer = true;

    private HashSet<PlayerStats> hitPlayers = new HashSet<PlayerStats>();
    private Dictionary<PlayerStats, float> lastTickByPlayer = new Dictionary<PlayerStats, float>();
    private float damageActiveTime = 0f;
    private float spawnTime;
    private float tickInterval;

    /// <summary>
    /// Call after instantiating the damage zone. Sets owner, damage, lifetime, and optional damage delay.
    /// </summary>
    /// <param name="damageDelay">Seconds before damage can be applied. 0 = immediate.</param>
    /// <param name="tickIntervalSec">When > 0, applies damage every N seconds per player (tick damage). When 0, uses oneHitPerPlayer.</param>
    public void Initialize(GameObject newOwner, int newDamage, float newLifetime, float damageDelay = 0f, float tickIntervalSec = 0f)
    {
        owner = newOwner;
        damage = newDamage;
        lifetime = newLifetime;
        damageActiveTime = Time.time + damageDelay;
        spawnTime = Time.time;
        tickInterval = tickIntervalSec;

        Collider col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
            col.isTrigger = true;

        var rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        Destroy(gameObject, lifetime);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryApplyDamage(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryApplyDamage(other);
    }

    private void TryApplyDamage(Collider other)
    {
        if (other == null) return;
        if (Time.time < damageActiveTime) return;

        var ps = other.GetComponentInParent<PlayerStats>();
        if (ps == null) return;

        if (tickInterval > 0f)
        {
            float now = Time.time;
            if (lastTickByPlayer.TryGetValue(ps, out float last) && now - last < tickInterval)
                return;
            lastTickByPlayer[ps] = now;
        }
        else if (oneHitPerPlayer && hitPlayers.Contains(ps))
        {
            return;
        }
        else if (oneHitPerPlayer)
        {
            hitPlayers.Add(ps);
        }

        DamageRelay.ApplyToPlayer(ps.gameObject, damage);
    }
}
