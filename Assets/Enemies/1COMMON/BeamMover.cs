using UnityEngine;
using Photon.Pun;

/// <summary>
/// Moves the GameObject forward at a constant speed and destroys it after lifetime.
/// Used for beam/projectile VFX that travel in a direction. Damage is handled by
/// a separate EnemyDamageZone child.
/// </summary>
public class BeamMover : MonoBehaviour
{
    public float speed = 18f;
    public float lifetime = 3f;

    private float spawnTime;

    private void Start()
    {
        spawnTime = Time.time;
    }

    private void Update()
    {
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
            return;

        transform.position += transform.forward * speed * Time.deltaTime;

        if (Time.time - spawnTime >= lifetime)
        {
            if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                Photon.Pun.PhotonNetwork.Destroy(gameObject);
            else
                Destroy(gameObject);
        }
    }

    /// <summary>
    /// Call after instantiation to configure the beam.
    /// </summary>
    public void Initialize(float newSpeed, float newLifetime)
    {
        speed = newSpeed;
        lifetime = newLifetime;
    }
}
