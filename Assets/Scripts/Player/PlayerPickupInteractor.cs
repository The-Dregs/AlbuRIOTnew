using UnityEngine;

public class PlayerPickupInteractor : MonoBehaviour
{
    public float pickupRadius = 2f;
    public LayerMask pickupLayer;
    [SerializeField] private float pickupScanInterval = 0.08f;

    private PlayerInteractHUD playerHUD;
    private ItemPickup nearbyPickup;
    private Photon.Pun.PhotonView cachedPhotonView;
    private float nextScanTime;
    private readonly Collider[] pickupBuffer = new Collider[32];

    void Start()
    {
        playerHUD = GetComponentInChildren<PlayerInteractHUD>(true);
        cachedPhotonView = GetComponent<Photon.Pun.PhotonView>();
    }

    void Update()
    {
        // Only allow local player to interact in multiplayer
        if (cachedPhotonView != null && !cachedPhotonView.IsMine) return;
        
        // Don't allow pickups if any UI or dialogue is open
        if (LocalUIManager.Instance != null && LocalUIManager.Instance.IsAnyOpen)
        {
            if (nearbyPickup != null && playerHUD != null)
            {
                playerHUD.Hide();
                nearbyPickup = null;
            }
            return;
        }

        if (Time.time >= nextScanTime)
        {
            CheckNearbyPickups();
            nextScanTime = Time.time + Mathf.Max(0.03f, pickupScanInterval);
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (nearbyPickup != null && !nearbyPickup.IsPickedUp)
            {
                nearbyPickup.ForcePickup(gameObject);
                // Hide the HUD immediately after pickup (notification system handles the pickup message)
                if (playerHUD != null)
                {
                    playerHUD.Hide();
                }
            }
        }
    }

    void CheckNearbyPickups()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, pickupRadius, pickupBuffer, pickupLayer, QueryTriggerInteraction.Collide);
        ItemPickup closest = null;
        float minDistSqr = float.MaxValue;
        
        for (int i = 0; i < count; i++)
        {
            var col = pickupBuffer[i];
            if (col == null) continue;
            var pickup = col.GetComponent<ItemPickup>();
            if (pickup == null || pickup.IsPickedUp) continue;
            float distSqr = (transform.position - col.transform.position).sqrMagnitude;
            if (distSqr < minDistSqr)
            {
                minDistSqr = distSqr;
                closest = pickup;
            }
        }

        if (closest != nearbyPickup)
        {
            if (playerHUD != null)
            {
                CancelInvoke(nameof(HideHUD));
                if (nearbyPickup != null)
                {
                    playerHUD.Hide();
                }
            }

            nearbyPickup = closest;

            if (nearbyPickup != null && playerHUD != null && !nearbyPickup.IsPickedUp)
            {
                string itemName = nearbyPickup.ItemData != null ? nearbyPickup.ItemData.itemName : "item";
                playerHUD.Show($"Press E to pick up {itemName}");
            }
        }
    }

    void HideHUD()
    {
        if (playerHUD != null)
        {
            playerHUD.Hide();
        }
    }
}
