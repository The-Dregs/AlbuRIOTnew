using UnityEngine;
using UnityEditor;

/// <summary>
/// Draws a prominent wire capsule for CharacterController in Scene view when the GameObject is selected.
/// </summary>
[CustomEditor(typeof(CharacterController))]
public class CharacterControllerGizmoDrawer : Editor
{
    private static readonly Color GizmoColor = new Color(0f, 1f, 0.5f, 0.7f);

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
    }

    private void OnSceneGUI()
    {
        var cc = (CharacterController)target;
        if (cc == null) return;
        Vector3 center = cc.transform.position + cc.transform.TransformDirection(cc.center);
        DrawWireCapsule(center, cc.radius, cc.height, cc.transform, GizmoColor);
    }

    private static void DrawWireCapsule(Vector3 center, float radius, float height, Transform tr, Color color)
    {
        Handles.color = color;
        float halfHeight = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 up = tr != null ? tr.up : Vector3.up;
        Vector3 top = center + up * halfHeight;
        Vector3 bottom = center - up * halfHeight;

        int segments = 24;
        for (int i = 0; i < segments; i++)
        {
            float a1 = (i / (float)segments) * Mathf.PI * 2f;
            float a2 = ((i + 1) / (float)segments) * Mathf.PI * 2f;
            Vector3 right = tr != null ? tr.right : Vector3.right;
            Vector3 fwd = tr != null ? tr.forward : Vector3.forward;
            Vector3 p1 = top + (right * Mathf.Cos(a1) + fwd * Mathf.Sin(a1)) * radius;
            Vector3 p2 = top + (right * Mathf.Cos(a2) + fwd * Mathf.Sin(a2)) * radius;
            Handles.DrawLine(p1, p2);
            p1 = bottom + (right * Mathf.Cos(a1) + fwd * Mathf.Sin(a1)) * radius;
            p2 = bottom + (right * Mathf.Cos(a2) + fwd * Mathf.Sin(a2)) * radius;
            Handles.DrawLine(p1, p2);
        }
        for (int i = 0; i < segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 right = tr != null ? tr.right : Vector3.right;
            Vector3 fwd = tr != null ? tr.forward : Vector3.forward;
            Vector3 dir = right * Mathf.Cos(a) + fwd * Mathf.Sin(a);
            Handles.DrawLine(top + dir * radius, bottom + dir * radius);
        }
    }

}
