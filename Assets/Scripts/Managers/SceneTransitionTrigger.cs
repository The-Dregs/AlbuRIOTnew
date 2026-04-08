using UnityEngine;
using Photon.Pun;

public class SceneTransitionTrigger : MonoBehaviour
{
    [Header("Next Scene Name")]
    public string nextSceneName;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            // Use PhotonNetwork to load the next scene for all players in multiplayer
            if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
            {
                PhotonNetwork.LoadLevel(nextSceneName);
            }
            else
            {
                // Single player fallback
                UnityEngine.SceneManagement.SceneManager.LoadScene(nextSceneName);
            }
        }
    }
}
