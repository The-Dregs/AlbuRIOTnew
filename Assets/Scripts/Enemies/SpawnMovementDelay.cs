using System.Collections;
using UnityEngine;

/// <summary>
/// Temporarily disables enemy movement after spawn. Add to spawned enemies
/// and call Begin(duration) to block movement for that many seconds.
/// </summary>
public class SpawnMovementDelay : MonoBehaviour
{
    private CharacterController controller;
    private BaseEnemyAI enemyAI;
    private Coroutine delayRoutine;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (controller == null)
            controller = GetComponentInChildren<CharacterController>();
        enemyAI = GetComponent<BaseEnemyAI>();
        if (enemyAI == null)
            enemyAI = GetComponentInChildren<BaseEnemyAI>();
    }

    public void Begin(float delaySeconds)
    {
        if (delaySeconds <= 0f) return;
        if (delayRoutine != null) StopCoroutine(delayRoutine);
        if (controller != null)
            controller.enabled = false;
        delayRoutine = StartCoroutine(CoDelay(delaySeconds));
    }

    private IEnumerator CoDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (controller != null && (enemyAI == null || !enemyAI.IsDead))
            controller.enabled = true;

        delayRoutine = null;
        Destroy(this);
    }
}
