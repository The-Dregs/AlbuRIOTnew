using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// Lightweight HUD that shows current player count and short join/leave notifications.
/// Attach this once in a scene; it will auto-create its own UI if none is assigned.
/// </summary>
public class NetworkStatusHUD : MonoBehaviourPunCallbacks
{
    [Header("Targets")]
    [Tooltip("Optional canvas to parent HUD elements under. If null, a ScreenSpace-Overlay canvas will be created.")]
    public Canvas targetCanvas;
    [Tooltip("Optional text field showing current player count.")]
    public TextMeshProUGUI playerCountText;
    [Tooltip("Optional text field for transient notifications (joins/leaves).")]
    public TextMeshProUGUI notificationText;

    [Header("Behavior")]
    [Tooltip("Seconds a join/leave notification stays visible.")]
    public float notificationDuration = 3f;

    private Coroutine notificationRoutine;

    void Start()
    {
        EnsureUI();
        RefreshPlayerCountLabel();
    }

    #region Photon Callbacks

    public override void OnJoinedRoom()
    {
        RefreshPlayerCountLabel();
        ShowNotification($"Joined room ({GetSafePlayerCount()} player{(GetSafePlayerCount() == 1 ? "" : "s")})");
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        RefreshPlayerCountLabel();
        ShowNotification($"{SafeName(newPlayer)} joined ({GetSafePlayerCount()} player{(GetSafePlayerCount() == 1 ? "" : "s")})");
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        RefreshPlayerCountLabel();
        ShowNotification($"{SafeName(otherPlayer)} left ({GetSafePlayerCount()} player{(GetSafePlayerCount() == 1 ? "" : "s")})");
    }

    public override void OnLeftRoom()
    {
        RefreshPlayerCountLabel();
        ShowNotification("Left room");
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        RefreshPlayerCountLabel();
        ShowNotification($"Disconnected: {cause}");
    }

    #endregion

    private void EnsureUI()
    {
        if (playerCountText != null && notificationText != null)
            return;

        if (targetCanvas == null)
        {
            var existing = FindFirstObjectByType<Canvas>();
            if (existing != null && existing.isRootCanvas)
            {
                targetCanvas = existing;
            }
            else
            {
                var go = new GameObject("NetworkStatusCanvas");
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                go.AddComponent<CanvasScaler>();
                go.AddComponent<GraphicRaycaster>();
                targetCanvas = canvas;
            }
        }

        if (playerCountText == null)
        {
            playerCountText = CreateLabel("PlayerCountText", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(10f, -10f), TextAlignmentOptions.TopLeft);
        }

        if (notificationText == null)
        {
            // Use center alignment at the top of the screen (horizontal center, top vertical)
            notificationText = CreateLabel("NetworkNotificationText", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -40f), TextAlignmentOptions.Center);
        }
    }

    private TextMeshProUGUI CreateLabel(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(targetCanvas.transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = anchorMin;
        rt.anchoredPosition = anchoredPos;
        if (rt.sizeDelta.x < 10f) rt.sizeDelta = new Vector2(600f, 60f);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = 24f;
        tmp.alignment = alignment;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        return tmp;
    }

    private void RefreshPlayerCountLabel()
    {
        if (playerCountText == null)
            return;

        int count = GetSafePlayerCount();
        if (PhotonNetwork.InRoom)
        {
            playerCountText.text = $"Players in room: {count}";
        }
        else if (PhotonNetwork.OfflineMode)
        {
            playerCountText.text = "Offline (single-player)";
        }
        else
        {
            playerCountText.text = "Not connected";
        }
    }

    private int GetSafePlayerCount()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
            return Mathf.Max(1, PhotonNetwork.CurrentRoom.PlayerCount);
        if (PhotonNetwork.OfflineMode)
            return 1;
        return 0;
    }

    private void ShowNotification(string message)
    {
        if (notificationText == null || string.IsNullOrEmpty(message))
            return;

        if (notificationRoutine != null)
            StopCoroutine(notificationRoutine);

        notificationRoutine = StartCoroutine(Co_ShowNotification(message));
    }

    private IEnumerator Co_ShowNotification(string message)
    {
        notificationText.gameObject.SetActive(true);
        notificationText.text = message;

        float t = 0f;
        while (t < notificationDuration)
        {
            t += Time.deltaTime;
            yield return null;
        }

        notificationText.gameObject.SetActive(false);
        notificationRoutine = null;
    }

    private static string SafeName(Player p)
    {
        if (p == null) return "Player";
        if (!string.IsNullOrEmpty(p.NickName)) return p.NickName;
        return $"Player {p.ActorNumber}";
    }
}

