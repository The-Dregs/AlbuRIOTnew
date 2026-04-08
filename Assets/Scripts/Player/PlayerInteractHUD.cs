using UnityEngine;
using TMPro;

// attach this to a child under the player that holds a screen-space or world-space prompt UI
public class PlayerInteractHUD : MonoBehaviour
{
    [Tooltip("root GameObject for the prompt UI (panel)")] public GameObject root;
    [Tooltip("TMP text for the prompt message")] public TextMeshProUGUI text;

    private Photon.Pun.PhotonView pv;

    void Awake()
    {
        pv = GetComponentInParent<Photon.Pun.PhotonView>();
        Hide();
    }

    public void Show(string message)
    {
        // local player only if photon is used
        if (pv != null && Photon.Pun.PhotonNetwork.IsConnected && !pv.IsMine) return;
        if (text != null) text.text = message;
        if (root != null) root.SetActive(true);
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
    }
}
