using UnityEngine;

public class ObjectiveArrow : MonoBehaviour
{
    public Transform target;
    public float height = 2f;

    void Update()
    {
        if (target != null)
        {
            Vector3 targetPos = target.position + Vector3.up * height;
            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * 8f);
            transform.LookAt(target.position);
        }
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}
