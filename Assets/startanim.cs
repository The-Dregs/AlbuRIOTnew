using UnityEngine;
using System.Collections;  // For coroutines

public class DelayedAnimation : MonoBehaviour
{
    private Animator animator;

    // EXACT name from your Animator (case-sensitive!)
    public string animationStateName = "Armature|ArmatureAction";

    void Start()
    {
        animator = GetComponent<Animator>();

        // DISABLE to stop immediate play (your dragon freezes in T-pose)
        animator.enabled = false;

        // Start delay
        StartCoroutine(PlayAfterDelay());
    }

    IEnumerator PlayAfterDelay()
    {
        yield return new WaitForSeconds(4f);  // Wait 4 seconds

        // ENABLE + Play from start
        animator.enabled = true;
        animator.Play(animationStateName, -1, 0f);  // -1=layer, 0f=frame 0
    }
}