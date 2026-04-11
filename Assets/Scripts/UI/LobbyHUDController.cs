using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;

/// <summary>
/// Handles in-game lobby HUD:
/// - Hold TAB to show current room players.
/// - Show room/lobby name in HUD and optional escape panel text.
/// - Show transient notifications for join/leave and host migration.
/// </summary>
public class LobbyHUDController : MonoBehaviourPunCallbacks
{
    [Header("Input")]
    public KeyCode holdPlayersKey = KeyCode.Tab;

    [Header("Player List HUD (Hold TAB)")]
    [Tooltip("Panel shown while TAB is held.")]
    public GameObject playerListPanel;
    [Tooltip("Container for player list row instances.")]
    public Transform playerListContainer;
    [Tooltip("Template row with TextMeshProUGUI. It will be hidden and cloned.")]
    public GameObject playerRowTemplate;

    [Header("Lobby Name UI")]
    [Tooltip("Optional in-game HUD text for room name.")]
    public TextMeshProUGUI lobbyNameHudText;
    [Tooltip("Optional text inside Escape/Pause panel for room name.")]
    public TextMeshProUGUI lobbyNameEscapePanelText;

    [Header("Escape Panel Player List")]
    [Tooltip("Optional container for player rows in Escape panel.")]
    public Transform escapePlayerListContainer;
    [Tooltip("Template row for Escape panel list. It will be hidden and cloned.")]
    public GameObject escapePlayerRowTemplate;

    [Header("Leave/Status Notification HUD")]
    [Tooltip("Optional text used for transient notifications.")]
    public TextMeshProUGUI notificationText;
    public float notificationSeconds = 3f;

    [Header("Auto-Bind (Player Prefab Friendly)")]
    [Tooltip("When enabled, missing UI references are auto-resolved from this object's children first, then scene.")]
    public bool autoBindReferences = true;
    [Tooltip("Optional child name hint for TAB panel.")]
    public string playerListPanelNameHint = "TabPlayerListPanel";
    [Tooltip("Optional child name hint for player list row template.")]
    public string playerRowTemplateNameHint = "PlayerRowTemplate";
    [Tooltip("Optional child name hint for in-game lobby name text.")]
    public string lobbyNameHudTextNameHint = "LobbyNameHUDText";
    [Tooltip("Optional child name hint for pause/escape lobby name text.")]
    public string lobbyNameEscapeTextNameHint = "LobbyNameEscapeText";
    [Tooltip("Optional child name hint for notification text.")]
    public string notificationTextNameHint = "LobbyNotificationText";

    private readonly List<GameObject> spawnedRows = new List<GameObject>();
    private readonly List<GameObject> spawnedEscapeRows = new List<GameObject>();
    private readonly Dictionary<int, string> knownPlayerNames = new Dictionary<int, string>();
    private readonly HashSet<int> leftActorNumbers = new HashSet<int>();
    private Coroutine notificationRoutine;

    private void Start()
    {
        if (autoBindReferences)
            AutoBindMissingReferences();

        if (playerListPanel != null)
            playerListPanel.SetActive(false);

        if (playerRowTemplate != null)
            playerRowTemplate.SetActive(false);

        if (escapePlayerRowTemplate != null)
            escapePlayerRowTemplate.SetActive(false);

        RefreshAllUi();
    }

    private void Update()
    {
        HandleHoldTabPanel();
    }

    private void HandleHoldTabPanel()
    {
        if (playerListPanel == null)
            return;

        bool shouldShow = PhotonNetwork.InRoom && Input.GetKey(holdPlayersKey);
        if (playerListPanel.activeSelf != shouldShow)
        {
            playerListPanel.SetActive(shouldShow);
        }

        if (shouldShow)
        {
            RefreshTabPlayerListRows();
        }
    }

    private void RefreshAllUi()
    {
        SyncKnownPlayersFromRoom();
        RefreshLobbyNameText();
        RefreshTabPlayerListRows();
        RefreshEscapePlayerListRows();
    }

    private void RefreshLobbyNameText()
    {
        string lobbyLabel;
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
        {
            lobbyLabel = "Lobby: " + PhotonNetwork.CurrentRoom.Name;
        }
        else if (PhotonNetwork.OfflineMode)
        {
            lobbyLabel = "Lobby: OFFLINE";
        }
        else
        {
            lobbyLabel = "Lobby: Not in room";
        }

        if (lobbyNameHudText != null)
            lobbyNameHudText.text = lobbyLabel;

        if (lobbyNameEscapePanelText != null)
            lobbyNameEscapePanelText.text = lobbyLabel;
    }

    private void RefreshTabPlayerListRows()
    {
        if (playerListContainer == null || playerRowTemplate == null)
            return;

        ClearRows();

        List<PlayerEntryView> entries = BuildPlayerEntryViews();
        if (entries.Count == 0)
        {
            CreateRow("Waiting for players...");
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            CreateRow(BuildRichEntryText(entries[i]));
        }
    }

    private void RefreshEscapePlayerListRows()
    {
        if (escapePlayerListContainer == null || escapePlayerRowTemplate == null)
            return;

        ClearEscapeRows();

        List<PlayerEntryView> entries = BuildPlayerEntryViews();
        if (entries.Count == 0)
        {
            CreateEscapeRow("Waiting for players...");
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            CreateEscapeRow(BuildRichEntryText(entries[i]));
        }
    }

    private void AutoBindMissingReferences()
    {
        if (playerListPanel == null)
        {
            playerListPanel = FindChildOrSceneObject(playerListPanelNameHint);
        }

        if (playerListContainer == null && playerListPanel != null)
        {
            playerListContainer = playerListPanel.transform;
        }

        if (playerRowTemplate == null)
        {
            playerRowTemplate = FindChildOrSceneObject(playerRowTemplateNameHint);
        }

        if (lobbyNameHudText == null)
        {
            lobbyNameHudText = FindTmpByNameHint(lobbyNameHudTextNameHint);
        }

        if (lobbyNameEscapePanelText == null)
        {
            lobbyNameEscapePanelText = FindTmpByNameHint(lobbyNameEscapeTextNameHint);
        }

        if (notificationText == null)
        {
            notificationText = FindTmpByNameHint(notificationTextNameHint);
        }

        if (escapePlayerListContainer == null)
        {
            GameObject container = FindChildOrSceneObject("EscapePlayerListContainer");
            if (container != null)
                escapePlayerListContainer = container.transform;
        }

        if (escapePlayerRowTemplate == null)
        {
            escapePlayerRowTemplate = FindChildOrSceneObject("EscapePlayerRowTemplate");
        }
    }

    private GameObject FindChildOrSceneObject(string nameHint)
    {
        if (string.IsNullOrWhiteSpace(nameHint))
            return null;

        Transform child = transform.Find(nameHint);
        if (child != null)
            return child.gameObject;

        Transform[] allChildren = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < allChildren.Length; i++)
        {
            if (allChildren[i] != null && allChildren[i].name == nameHint)
                return allChildren[i].gameObject;
        }

        GameObject sceneObj = GameObject.Find(nameHint);
        return sceneObj;
    }

    private TextMeshProUGUI FindTmpByNameHint(string nameHint)
    {
        if (string.IsNullOrWhiteSpace(nameHint))
            return null;

        Transform[] allChildren = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < allChildren.Length; i++)
        {
            Transform t = allChildren[i];
            if (t == null || t.name != nameHint)
                continue;

            TextMeshProUGUI tmpChild = t.GetComponent<TextMeshProUGUI>();
            if (tmpChild != null)
                return tmpChild;
        }

        GameObject sceneObj = GameObject.Find(nameHint);
        if (sceneObj != null)
        {
            TextMeshProUGUI sceneTmp = sceneObj.GetComponent<TextMeshProUGUI>();
            if (sceneTmp != null)
                return sceneTmp;
        }

        return null;
    }

    private void CreateRow(string textValue)
    {
        GameObject row = Instantiate(playerRowTemplate, playerListContainer);
        row.SetActive(true);

        TextMeshProUGUI rowText = row.GetComponent<TextMeshProUGUI>();
        if (rowText == null)
            rowText = row.GetComponentInChildren<TextMeshProUGUI>(true);

        if (rowText != null)
        {
            rowText.richText = true;
            rowText.text = textValue;
        }

        spawnedRows.Add(row);
    }

    private void CreateEscapeRow(string textValue)
    {
        if (escapePlayerRowTemplate == null)
            return;

        GameObject row = Instantiate(escapePlayerRowTemplate, escapePlayerListContainer);
        row.SetActive(true);

        TextMeshProUGUI rowText = row.GetComponent<TextMeshProUGUI>();
        if (rowText == null)
            rowText = row.GetComponentInChildren<TextMeshProUGUI>(true);

        if (rowText != null)
        {
            rowText.richText = true;
            rowText.text = textValue;
        }

        spawnedEscapeRows.Add(row);
    }

    private void ClearRows()
    {
        for (int i = 0; i < spawnedRows.Count; i++)
        {
            if (spawnedRows[i] != null)
                Destroy(spawnedRows[i]);
        }
        spawnedRows.Clear();
    }

    private void ClearEscapeRows()
    {
        for (int i = 0; i < spawnedEscapeRows.Count; i++)
        {
            if (spawnedEscapeRows[i] != null)
                Destroy(spawnedEscapeRows[i]);
        }
        spawnedEscapeRows.Clear();
    }

    private struct PlayerEntryView
    {
        public int ActorNumber;
        public string Name;
        public bool IsHost;
        public bool IsLeft;
    }

    private List<PlayerEntryView> BuildPlayerEntryViews()
    {
        var entries = new List<PlayerEntryView>();
        if (knownPlayerNames.Count == 0)
            return entries;

        var actorNumbers = new List<int>(knownPlayerNames.Keys);
        actorNumbers.Sort();

        for (int i = 0; i < actorNumbers.Count; i++)
        {
            int actor = actorNumbers[i];
            bool inRoom = IsActorInRoom(actor);
            bool isLeft = leftActorNumbers.Contains(actor) || !inRoom;
            bool isHost = !isLeft && PhotonNetwork.MasterClient != null && PhotonNetwork.MasterClient.ActorNumber == actor;

            entries.Add(new PlayerEntryView
            {
                ActorNumber = actor,
                Name = knownPlayerNames[actor],
                IsHost = isHost,
                IsLeft = isLeft
            });
        }

        return entries;
    }

    private string BuildRichEntryText(PlayerEntryView entry)
    {
        string safeName = string.IsNullOrWhiteSpace(entry.Name) ? ("Player " + entry.ActorNumber) : entry.Name;
        if (entry.IsLeft)
            return "<color=#FF4D4D>" + safeName + " (LEFT)</color>";
        if (entry.IsHost)
            return safeName + " (HOST)";
        return safeName;
    }

    private void SyncKnownPlayersFromRoom()
    {
        Player[] players = PhotonNetwork.PlayerList;
        if (players == null)
            return;

        for (int i = 0; i < players.Length; i++)
        {
            Player p = players[i];
            if (p == null)
                continue;

            knownPlayerNames[p.ActorNumber] = GetSafeName(p);
            leftActorNumbers.Remove(p.ActorNumber);
        }
    }

    private bool IsActorInRoom(int actorNumber)
    {
        Player[] players = PhotonNetwork.PlayerList;
        if (players == null)
            return false;

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null && players[i].ActorNumber == actorNumber)
                return true;
        }

        return false;
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
        yield return new WaitForSeconds(notificationSeconds);
        notificationText.gameObject.SetActive(false);
        notificationRoutine = null;
    }

    private static string GetSafeName(Player p)
    {
        if (p == null)
            return "Player";
        if (!string.IsNullOrWhiteSpace(p.NickName))
            return p.NickName;
        return "Player " + p.ActorNumber;
    }

    public override void OnJoinedRoom()
    {
        knownPlayerNames.Clear();
        leftActorNumbers.Clear();
        SyncKnownPlayersFromRoom();
        RefreshAllUi();
        ShowNotification("Joined " + PhotonNetwork.CurrentRoom.Name);
    }

    public override void OnLeftRoom()
    {
        knownPlayerNames.Clear();
        leftActorNumbers.Clear();
        RefreshAllUi();
        ShowNotification("You left the lobby");
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (newPlayer != null)
        {
            knownPlayerNames[newPlayer.ActorNumber] = GetSafeName(newPlayer);
            leftActorNumbers.Remove(newPlayer.ActorNumber);
        }
        RefreshAllUi();
        ShowNotification(GetSafeName(newPlayer) + " joined the lobby");
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (otherPlayer != null)
        {
            knownPlayerNames[otherPlayer.ActorNumber] = GetSafeName(otherPlayer);
            leftActorNumbers.Add(otherPlayer.ActorNumber);
        }
        RefreshAllUi();
        ShowNotification(GetSafeName(otherPlayer) + " left the lobby");
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        RefreshAllUi();
        ShowNotification("Host transferred to " + GetSafeName(newMasterClient));
    }
}
