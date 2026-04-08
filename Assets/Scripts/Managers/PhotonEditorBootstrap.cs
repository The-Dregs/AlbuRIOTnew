using Photon.Pun;
using UnityEngine;

// Ensures a smooth Play-in-Editor experience: forces Photon OfflineMode before scenes load
// so that scripts can safely call RPCs locally without being connected to a server.
// This only runs in the Unity Editor.
public static class PhotonEditorBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureOfflineInEditor()
    {
        if (!Application.isEditor) return; // only affect editor play mode

        // If not connected and not already in OfflineMode, enable OfflineMode
        if (!PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.OfflineMode = true;
            Debug.Log("[PhotonEditorBootstrap] Editor Play Mode: Photon OfflineMode enabled.");
        }
    }
}
