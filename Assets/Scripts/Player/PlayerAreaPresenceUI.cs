using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using TMPro;

// shows a ground indicator (e.g., glowing triangle) and a counter above the local player
// when they are inside a quest reach-area that requires all players
public class PlayerAreaPresenceUI : MonoBehaviourPunCallbacks
{
    [Header("references")]
    [Tooltip("child GameObject with your triangle/ground indicator mesh or VFX")] public GameObject indicatorRoot;
    [Tooltip("world-space TextMeshPro for X/Y count above the player")] public TextMeshPro counterText;
    [Tooltip("height offset for the counter over the player")] public float counterHeight = 2f;
    [Tooltip("make counter face the camera each frame")] public bool billboardCounter = true;

    [Header("behavior")]
    [Tooltip("hide counter when only 1 player")] public bool hideCounterWhenSolo = true;

    private Camera _cam;

    private void Awake()
    {
        _cam = Camera.main;
        SetVisible(false);
    }

    private void Update()
    {
        if (counterText != null && billboardCounter)
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam != null)
            {
                var pos = transform.position + Vector3.up * counterHeight;
                counterText.transform.position = pos;
                counterText.transform.rotation = Quaternion.LookRotation(counterText.transform.position - _cam.transform.position, Vector3.up);
            }
        }
    }

    private void SetVisible(bool v)
    {
        if (indicatorRoot != null) indicatorRoot.SetActive(v);
        if (counterText != null) counterText.gameObject.SetActive(v);
    }

    private string GetActiveAreaId()
    {
        // find first InArea_* flag that is true for local player and not marked done in room
        if (!(PhotonNetwork.IsConnected && PhotonNetwork.InRoom)) return null;
        var lp = PhotonNetwork.LocalPlayer;
        foreach (var kv in lp.CustomProperties)
        {
            var key = kv.Key as string;
            if (string.IsNullOrEmpty(key)) continue;
            if (!key.StartsWith("InArea_")) continue;
            if (kv.Value is bool b && b)
            {
                string areaId = key.Substring("InArea_".Length);
                string doneKey = $"AreaDone_{areaId}";
                if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(doneKey, out var doneVal) && doneVal is bool done && done)
                    continue; // skip completed
                return areaId;
            }
        }
        return null;
    }

    private void Refresh()
    {
        // only show on owning client for their own character
        if (photonView != null && !photonView.IsMine)
        {
            SetVisible(false);
            return;
        }

        if (!(PhotonNetwork.IsConnected && PhotonNetwork.InRoom))
        {
            // offline: hide since multiplayer gating does not apply
            SetVisible(false);
            return;
        }

        string areaId = GetActiveAreaId();
        if (string.IsNullOrEmpty(areaId))
        {
            SetVisible(false);
            return;
        }

        // multi present/total
        int total = PhotonNetwork.CurrentRoom.PlayerCount;
        int present = 0;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (p.CustomProperties.TryGetValue($"InArea_{areaId}", out var val) && val is bool b && b) present++;
        }

        bool showCounter = !(hideCounterWhenSolo && total <= 1);
        if (indicatorRoot != null) indicatorRoot.SetActive(true);
        if (counterText != null)
        {
            counterText.text = showCounter ? $"{present}/{total}" : string.Empty;
            counterText.gameObject.SetActive(showCounter);
        }
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        // whenever any InArea_* flag changes, refresh
        foreach (var k in changedProps.Keys)
        {
            var ks = k as string;
            if (!string.IsNullOrEmpty(ks) && ks.StartsWith("InArea_"))
            {
                Refresh();
                return;
            }
        }
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        foreach (var k in propertiesThatChanged.Keys)
        {
            var ks = k as string;
            if (!string.IsNullOrEmpty(ks) && ks.StartsWith("AreaDone_"))
            {
                Refresh();
                return;
            }
        }
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Refresh();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Refresh();
    }

    public override void OnEnable()
    {
        Refresh();
    }
}
