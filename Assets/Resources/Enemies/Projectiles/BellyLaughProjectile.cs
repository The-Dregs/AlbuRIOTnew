using UnityEngine;
using Photon.Pun;
using System.Collections;

public class BellyLaughProjectile : MonoBehaviourPun
{
    [Header("Projectile Settings")]
    public int damage = 15;
    public BungisngisAI owner;

    [Header("Movement")]
    public float speed = 18f;
    public float lifetime = 2.5f;
    private PhotonTransformViewClassic transformView;

    private void Awake()
    {
        // Only setup network sync if in multiplayer mode
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            // Ensure there is a PhotonTransformViewClassic and it's registered for syncing
            var pv = photonView != null ? photonView : GetComponent<PhotonView>();
            transformView = GetComponent<PhotonTransformViewClassic>();
            if (transformView == null)
            {
                transformView = gameObject.AddComponent<PhotonTransformViewClassic>();
            }
            
            // Configure sync to true for position/rotation using the model objects
            transformView.m_PositionModel.SynchronizeEnabled = true;
            transformView.m_RotationModel.SynchronizeEnabled = true;
            
            // Set interpolation options for smooth movement
            transformView.m_PositionModel.InterpolateOption = PhotonTransformViewPositionModel.InterpolateOptions.EstimatedSpeed;
            transformView.m_RotationModel.InterpolateOption = PhotonTransformViewRotationModel.InterpolateOptions.RotateTowards;

            if (pv != null)
            {
                if (pv.ObservedComponents == null)
                {
                    pv.ObservedComponents = new System.Collections.Generic.List<Component>();
                }
                if (!pv.ObservedComponents.Contains(transformView))
                {
                    pv.ObservedComponents.Add(transformView);
                }
            }
        }
    }
    private void Start()
    {
        // Only MasterClient starts coroutines (authority-based)
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
            return;
        StartCoroutine(DestroyAfterLifetime());
    }

    private IEnumerator DestroyAfterLifetime()
    {
        yield return new WaitForSeconds(lifetime);
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            PhotonNetwork.Destroy(gameObject);
        else
            Destroy(gameObject);
    }

    private void Update()
    {
        // Only MasterClient moves projectiles (authority-based movement)
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
            return;
            
        // Move forward each frame
        transform.position += transform.forward * speed * Time.deltaTime;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Only MasterClient handles collisions
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
            return;
            
        var ps = other.GetComponentInParent<PlayerStats>();
        if (ps != null)
        {
            DamageRelay.ApplyToPlayer(ps.gameObject, damage);
            
            // Network-safe destruction
            if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                PhotonNetwork.Destroy(gameObject);
            else
                Destroy(gameObject);
        }
    }
}