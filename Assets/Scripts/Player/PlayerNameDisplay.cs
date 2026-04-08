using UnityEngine;
using TMPro;
using Photon.Pun;

// shows the player's photon nickname above their head.
// only visible on remote players — hidden for the local player.
// fades in/out based on distance to the local player.
public class PlayerNameDisplay : MonoBehaviourPun
{
    [Header("reference")]
    [Tooltip("assign the TextMeshPro component placed above the player")]
    public TextMeshProUGUI nameText;

    [Header("billboard")]
    [Tooltip("keep the text facing the camera each frame")]
    public bool faceCamera = true;

    [Header("distance visibility")]
    [Tooltip("max distance from the local player at which the nametag is fully visible")]
    public float showDistance = 15f;
    [Tooltip("distance over which the nametag fades out (beyond showDistance)")]
    public float fadeRange = 3f;

    private Transform cameraTransform;
    private Transform localPlayer;
    private bool isRemote;
    private CanvasGroup canvasGroup;

    private void Start()
    {
        // hide on local player
        if (photonView.IsMine || !PhotonNetwork.IsConnected)
        {
            if (nameText != null)
                nameText.gameObject.SetActive(false);
            return;
        }

        isRemote = true;

        // set the nickname from the photon owner
        if (nameText != null)
        {
            string nick = photonView.Owner != null ? photonView.Owner.NickName : "Player";
            nameText.text = string.IsNullOrEmpty(nick) ? "Player" : nick;
            nameText.gameObject.SetActive(true);

            // ensure a canvas group for fading
            canvasGroup = nameText.GetComponentInParent<CanvasGroup>();
            if (canvasGroup == null)
            {
                var canvas = nameText.GetComponentInParent<Canvas>();
                if (canvas != null)
                    canvasGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            }
        }
    }

    private void LateUpdate()
    {
        if (!isRemote || nameText == null)
            return;

        if (cameraTransform == null)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            cameraTransform = cam.transform;
        }

        // find local player transform for distance check
        if (localPlayer == null)
        {
            var localPV = GetLocalPhotonView();
            if (localPV != null)
                localPlayer = localPV.transform;
        }

        // distance-based visibility
        if (localPlayer != null)
        {
            float dist = Vector3.Distance(localPlayer.position, transform.position);
            float maxDist = showDistance + fadeRange;

            if (dist > maxDist)
            {
                if (nameText.gameObject.activeSelf)
                    nameText.gameObject.SetActive(false);
                return;
            }

            if (!nameText.gameObject.activeSelf)
                nameText.gameObject.SetActive(true);

            // smooth alpha fade in the fade range
            if (canvasGroup != null)
            {
                float alpha = dist <= showDistance ? 1f : 1f - Mathf.Clamp01((dist - showDistance) / fadeRange);
                canvasGroup.alpha = alpha;
            }
        }

        if (!nameText.gameObject.activeSelf)
            return;

        // billboard: face the camera so the text is always readable
        if (faceCamera)
        {
            nameText.transform.rotation = Quaternion.LookRotation(
                nameText.transform.position - cameraTransform.position);
        }
    }

    private PhotonView GetLocalPhotonView()
    {
        foreach (var pv in FindObjectsOfType<PhotonView>())
        {
            if (pv.IsMine && pv.GetComponent<PlayerNameDisplay>() != null)
                return pv;
        }
        return null;
    }
}
