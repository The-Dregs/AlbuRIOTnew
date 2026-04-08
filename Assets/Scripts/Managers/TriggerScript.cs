using UnityEngine;

public class TriggerScript : MonoBehaviour
{
    [Header("Assign the GameObject to activate")]
    public GameObject objectToActivate;

    [Header("Assign GameObjects to deactivate")]
    public GameObject[] objectsToDeactivate;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            if (objectToActivate != null)
                objectToActivate.SetActive(true);

            if (objectsToDeactivate != null)
            {
                foreach (var go in objectsToDeactivate)
                {
                    if (go != null)
                        go.SetActive(false);
                }
            }
        }
    }
}