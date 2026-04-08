using UnityEngine;

// attach to a trigger collider; when the local player enters, auto-start a dialogue (one-shot by default)
[RequireComponent(typeof(Collider))]
public class DialogueAreaTrigger : MonoBehaviour
{
    [Header("dialogue")]
    public NPCDialogueData dialogue;
    public bool oneShot = true;

    [Header("optional quest gating")]
    public bool requireMatchingQuestStep = false;
    public ObjectiveType requiredType = ObjectiveType.Custom;
    [Tooltip("for TalkTo/ReachArea: id string; for Collect: item name; for Kill: enemy name")] public string requiredTargetId;

    private Collider _col;

    void Awake()
    {
        _col = GetComponent<Collider>();
        if (_col != null) _col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        var playerRoot = GetPlayerRoot(other);
        if (playerRoot == null) return;
        if (!IsLocalPlayer(playerRoot)) return;
        if (requireMatchingQuestStep && !IsQuestMatch()) return;

        StartDialogue(playerRoot.transform);
        if (oneShot && _col != null) _col.enabled = false;
    }

    private void StartDialogue(Transform player)
    {
        if (dialogue == null) return;
        var dm = FindFirstObjectByType<NPCDialogueManager>();
        if (dm == null)
        {
            var go = new GameObject("NPCDialogueManager_Auto");
            dm = go.AddComponent<NPCDialogueManager>();
        }
        dm.StartDialogue(dialogue, player, transform);
    }

    private bool IsQuestMatch()
    {
        var qm = FindFirstObjectByType<QuestManager>();
        if (qm == null) return false;
        var q = qm.GetCurrentQuest();
        if (q == null || q.isCompleted) return false;
        if (q.objectiveType != requiredType) return false;
        if (!string.IsNullOrEmpty(requiredTargetId))
        {
            return string.Equals(q.targetId, requiredTargetId, System.StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

    private GameObject GetPlayerRoot(Collider other)
    {
        var ps = other.GetComponentInParent<PlayerStats>();
        return ps != null ? ps.gameObject : null;
    }

    private bool IsLocalPlayer(GameObject go)
    {
        var pv = go.GetComponentInParent<Photon.Pun.PhotonView>();
        if (pv == null) return true; // offline
        return pv.IsMine;
    }
}
