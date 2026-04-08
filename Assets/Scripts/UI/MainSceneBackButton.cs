using UnityEngine;
using UnityEngine.SceneManagement;

public class MainSceneBackButton : MonoBehaviour
{
    public string prologueSceneName = "Prologue";

    public void GoBackToPrologue()
    {
    Photon.Pun.PhotonNetwork.LoadLevel(prologueSceneName);
    }
}
