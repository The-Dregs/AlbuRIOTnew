using Photon.Pun;
using UnityEngine;

// attach to the player prefab so server/master can attribute quest progress to the correct client
public class PlayerQuestRelay : MonoBehaviourPun
{
    [SerializeField] private bool enableDebugLogs = false;
    private QuestManager cachedQuestManager;

    private QuestManager GetQuestManager()
    {
        if (cachedQuestManager == null)
            cachedQuestManager = FindFirstObjectByType<QuestManager>();
        return cachedQuestManager;
    }

    [PunRPC]
    public void RPC_AddKillProgress(string enemyName)
    {
        // only execute on owning client
        if (photonView != null && !photonView.IsMine) return;
        var qm = GetQuestManager();
        if (qm != null)
        {
            qm.AddProgress_Kill(enemyName);
            if (enableDebugLogs) Debug.Log($"quest kill progress (rpc): {enemyName}");
        }
    }

    [PunRPC]
    public void RPC_AddReachProgress(string areaId)
    {
        if (photonView != null && !photonView.IsMine) return;
        var qm = GetQuestManager();
        if (qm != null)
        {
            // Prefer new multi-objective system when available, fall back to legacy quest fields otherwise.
            var q = qm.GetCurrentQuest();
            if (q != null)
            {
                var obj = q.GetCurrentObjective();
                if (obj != null)
                {
                    if (obj.objectiveType == ObjectiveType.FindArea)
                    {
                        qm.AddProgress_FindArea(areaId);
                        if (enableDebugLogs) Debug.Log($"quest find area progress (rpc, objective): {areaId}");
                    }
                    else
                    {
                        qm.AddProgress_ReachArea(areaId);
                        if (enableDebugLogs) Debug.Log($"quest reach progress (rpc, objective): {areaId}");
                    }
                }
                else
                {
                    // Legacy single-objective quests (no objectives array)
                    if (q.objectiveType == ObjectiveType.FindArea)
                    {
                        qm.AddProgress_FindArea(areaId);
                        if (enableDebugLogs) Debug.Log($"quest find area progress (rpc, legacy): {areaId}");
                    }
                    else
                    {
                        qm.AddProgress_ReachArea(areaId);
                        if (enableDebugLogs) Debug.Log($"quest reach progress (rpc, legacy): {areaId}");
                    }
                }
            }
            else
            {
                qm.AddProgress_ReachArea(areaId);
                if (enableDebugLogs) Debug.Log($"quest reach progress (rpc, no quest): {areaId}");
            }
        }
    }
}
