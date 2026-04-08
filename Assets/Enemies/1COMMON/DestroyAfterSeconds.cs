using UnityEngine;

/// <summary>
/// Destroys the GameObject after a delay. Used for temporary VFX that should clean up automatically.
/// </summary>
public class DestroyAfterSeconds : MonoBehaviour
{
    public float seconds = 3f;

    private void Start()
    {
        Destroy(gameObject, seconds);
    }
}
