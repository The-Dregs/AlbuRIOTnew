using UnityEditor;
using UnityEngine;

#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
#endif

// This inspector exists to keep the Unity Inspector stable in edge-cases where
// third-party editor scripts can throw during domain reload / selection changes.
// It intentionally draws a basic inspector only.
[CanEditMultipleObjects]
#if PHOTON_UNITY_NETWORKING
[CustomEditor(typeof(PhotonView), true)]
#endif
internal sealed class SafePhotonViewInspector : Editor
{
    public override void OnInspectorGUI()
    {
#if PHOTON_UNITY_NETWORKING
        // Avoid hard-casts; Unity can invoke editors with destroyed/invalid targets
        // during recompiles or selection teardown.
        if (targets == null || targets.Length == 0)
        {
            EditorGUILayout.HelpBox("No valid PhotonView target selected.", MessageType.Info);
            return;
        }

        // DrawDefaultInspector() is the safest/most compatible here.
        DrawDefaultInspector();
#else
        EditorGUILayout.HelpBox("Photon PUN not present (PHOTON_UNITY_NETWORKING not defined).", MessageType.Info);
#endif
    }
}

