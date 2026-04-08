using UnityEngine;
using Photon.Pun;

/// <summary>
/// Tornado that slowly follows the nearest player (or average of all players).
/// Damage is handled by EnemyDamageZone (tick mode) so you can control size via the collider.
/// Add a child with Collider + EnemyDamageZone, or add both to this GameObject.
/// </summary>
public class TornadoFollower : MonoBehaviour
{
    public float moveSpeed = 3f;
    public float lifetime = 5f;

    private float spawnTime;

    /// <summary>
    /// Call after instantiation. Owner is the Bakunawa (for DamageRelay).
    /// Damage zone must have EnemyDamageZone + trigger Collider on this object or a child.
    /// </summary>
    public void Initialize(GameObject newOwner, int damage, float newLifetime, float speed = 3f, float tickIntervalSec = 0.4f)
    {
        moveSpeed = speed;
        lifetime = newLifetime;
        spawnTime = Time.time;

        var damageZone = GetComponentInChildren<EnemyDamageZone>();
        if (damageZone != null)
            damageZone.Initialize(newOwner, damage, newLifetime, 0f, tickIntervalSec);
        else if (Application.isPlaying)
            Debug.LogWarning("[TornadoFollower] No EnemyDamageZone found on tornado prefab. Add a Collider + EnemyDamageZone to control damage size.");

        var rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && GetComponent<PhotonView>() != null)
            Invoke(nameof(DestroyTornado), lifetime);
        else
            Destroy(gameObject, lifetime);
    }

    private void DestroyTornado()
    {
        if (this == null || !gameObject) return;
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            var pv = GetComponent<PhotonView>();
            if (pv != null && pv.IsMine)
                PhotonNetwork.Destroy(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
            return;

        if (Time.time - spawnTime >= lifetime) return;

        Vector3 targetPos = GetTargetPosition();
        if (targetPos != transform.position)
        {
            Vector3 dir = (targetPos - transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
            {
                dir.Normalize();
                transform.position += dir * moveSpeed * Time.deltaTime;
            }
        }
    }

    private Vector3 GetTargetPosition()
    {
        var players = FindObjectsByType<PlayerStats>(FindObjectsSortMode.None);
        if (players == null || players.Length == 0)
            return transform.position;

        if (players.Length == 1)
            return players[0].transform.position;

        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var p in players)
        {
            if (p == null) continue;
            sum += p.transform.position;
            count++;
        }
        if (count == 0) return transform.position;
        return sum / count;
    }
}
