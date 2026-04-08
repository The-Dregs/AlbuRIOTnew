using UnityEngine;
using Photon.Pun;
using System.Collections;

public class EnemyProjectile : MonoBehaviourPun
{
    [Header("Projectile Settings")]
    public int damage = 10;
    public GameObject owner;
    public LayerMask hitMask = 0; // if zero, defaults to Player layer at runtime
    public bool destroyOnHit = true;

    [Header("Projectile Movement")]
    public float speed = 10f;
    public float lifetime = 2f;
    public float maxDistance = 50f;
    public float destroyDelay = 0.05f;
    public bool useHoming = false;
    public Transform homingTarget;
    public float homingTurnRateDeg = 360f;
    [Tooltip("Keep projectile at fixed height above terrain")]
    public bool followTerrain = false;
    [Tooltip("Height offset above terrain when following")]
    public float terrainHeightOffset = 0.1f;

    private Vector3 startPosition;
    private bool initialized = false;
    private Coroutine lifetimeCoroutine;
    private Terrain cachedTerrain;
    private PhotonTransformViewClassic transformView;

    public void Initialize(GameObject newOwner, int newDamage, float newSpeed, float newLifetime, Transform target = null)
    {
        owner = newOwner;
        damage = newDamage;
        speed = newSpeed;
        lifetime = newLifetime;
        homingTarget = target;
        useHoming = target != null;
        initialized = true;
    }

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
                // Ensure ObservedComponents includes our transformView
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

        if ((hitMask.value == 0))
        {
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0) hitMask = 1 << playerLayer;
        }
        if (owner == null)
        {
            var parentAI = GetComponentInParent<MonoBehaviour>();
            if (parentAI != null)
                owner = parentAI.gameObject;
        }
        startPosition = transform.position;
        
        // Cache terrain reference if terrain-following is enabled
        if (followTerrain && cachedTerrain == null)
            cachedTerrain = FindFirstObjectByType<Terrain>();
    }
    
    private void Start()
    {
        // Only MasterClient starts coroutines (authority-based)
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
            return;
        lifetimeCoroutine = StartCoroutine(DestroyAfterLifetime());
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
            
        if (useHoming && homingTarget != null)
        {
            Vector3 to = homingTarget.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(to.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, homingTurnRateDeg * Time.deltaTime);
            }
        }
        transform.position += transform.forward * speed * Time.deltaTime;
        
        // Follow terrain height if enabled
        if (followTerrain && cachedTerrain != null)
        {
            Vector3 pos = transform.position;
            float terrainHeight = cachedTerrain.SampleHeight(pos) + cachedTerrain.transform.position.y;
            pos.y = terrainHeight + terrainHeightOffset;
            transform.position = pos;
        }

        if (maxDistance > 0f)
        {
            if (Vector3.SqrMagnitude(transform.position - startPosition) > (maxDistance * maxDistance))
            {
                if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                    PhotonNetwork.Destroy(gameObject);
                else
                    Destroy(gameObject);
            }
        }
    }

    private IEnumerator DestroyWithDelay()
    {
        yield return new WaitForSeconds(destroyDelay);
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            PhotonNetwork.Destroy(gameObject);
        else
            Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        // Only MasterClient handles collisions
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
            return;
            
        if ((hitMask.value & (1 << other.gameObject.layer)) == 0) return;
        var ps = other.GetComponentInParent<PlayerStats>();
        if (ps != null)
        {
            DamageRelay.ApplyToPlayer(ps.gameObject, damage);
            
            if (destroyOnHit)
            {
                StartCoroutine(DestroyWithDelay());
            }
        }
    }
}
