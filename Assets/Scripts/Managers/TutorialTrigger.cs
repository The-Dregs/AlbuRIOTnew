using UnityEngine;
using Photon.Pun;

[RequireComponent(typeof(Collider))]
public class TutorialTrigger : MonoBehaviour
{
    [Header("Tutorial Settings")]
    [Tooltip("Index in TutorialManager.tutorialSteps array, or legacy dialoguePanels array")]
    public int tutorialIndex = 0;
    
    [Tooltip("Optional: Activate this GameObject on trigger (multiplayer-safe)")]
    public GameObject objectToActivate;

    [Tooltip("One-shot: disable trigger after first use")]
    public bool oneShot = true;

    [Header("Optional References")]
    [Tooltip("Assign if you have multiple TutorialManagers or want to specify one")]
    public TutorialManager tutorialManager;

    private Collider triggerCollider;
    private bool hasTriggered = false;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Check if already triggered (for one-shot)
        if (hasTriggered && oneShot) return;

        // Get player root
        var playerRoot = GetPlayerRoot(other);
        if (playerRoot == null) return;

        // Only trigger for local player in multiplayer
        if (!IsLocalPlayer(playerRoot)) return;

        // Find tutorial manager if not assigned
        if (tutorialManager == null)
        {
            tutorialManager = FindFirstObjectByType<TutorialManager>();
        }

        if (tutorialManager != null)
        {
            // Show tutorial only for the triggering player
            tutorialManager.ShowTutorialForPlayer(playerRoot, tutorialIndex);
        }

        // Activate object if specified (only for local player)
        if (objectToActivate != null)
        {
            objectToActivate.SetActive(true);
        }

        // Mark as triggered and disable if one-shot
        hasTriggered = true;
        if (oneShot && triggerCollider != null)
        {
            triggerCollider.enabled = false;
        }
    }

    private GameObject GetPlayerRoot(Collider other)
    {
        if (other == null) return null;
        if (other.CompareTag("Player")) return other.gameObject;
        
        // Check parent hierarchy for Player tag
        Transform current = other.transform;
        int maxDepth = 5; // Safety limit
        int depth = 0;
        
        while (current != null && depth < maxDepth)
        {
            if (current.CompareTag("Player"))
                return current.gameObject;
            current = current.parent;
            depth++;
        }
        
        return null;
    }

    private bool IsLocalPlayer(GameObject go)
    {
        var pv = go.GetComponent<PhotonView>();
        if (pv == null) return true; // Offline mode - always local
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return true; // Offline
        return pv.IsMine;
    }

    // Public method to reset trigger (useful for testing or respawns)
    public void ResetTrigger()
    {
        hasTriggered = false;
        if (triggerCollider != null)
        {
            triggerCollider.enabled = true;
        }
    }
}