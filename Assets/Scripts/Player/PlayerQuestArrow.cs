using UnityEngine;
using Photon.Pun;

// places/rotates an arrow (quad/mesh) around the local player pointing to the current quest target
public class PlayerQuestArrow : MonoBehaviour
{
    [Header("arrow visuals")]
    [Tooltip("child GameObject with your arrow quad/mesh/VFX")]
    public Transform arrowRoot;
    [Tooltip("radius offset from player center for the arrow visual")]
    public float radius = 0.8f;
    [Tooltip("vertical offset from ground")]
    public float height = 0.05f;
    [Tooltip("smoothing for rotation and repositioning")]
    public float smooth = 12f;
    [Tooltip("hide arrow if no target found")]
    public bool hideWhenNoTarget = true;

    [Header("target refresh")]
    [Tooltip("how often (seconds) to re-scan scene to find the target transform for the current quest")]
    public float recheckInterval = 1.0f;

    private Transform target;
    private QuestManager questManager;
    private float recheckTimer = 0f;
    private PhotonView pv;
    private QuestAreaTrigger[] cachedQuestAreas = System.Array.Empty<QuestAreaTrigger>();
    private NPCDialogueTrigger[] cachedNpcTriggers = System.Array.Empty<NPCDialogueTrigger>();
    private float nextAreaCacheRefreshTime = 0f;
    private float nextNpcCacheRefreshTime = 0f;
    [SerializeField, Range(0.25f, 5f)] private float targetCacheRefreshInterval = 1.5f;

    void Awake()
    {
        pv = GetComponent<PhotonView>();
        questManager = FindFirstObjectByType<QuestManager>();
        
        if (arrowRoot == null)
        {
            Transform child = transform.Find("QuestArrow/ArrowRoot");
            if (child != null) arrowRoot = child;
        }
    }

    void Start()
    {
        if (arrowRoot != null && arrowRoot.gameObject != null)
        {
            arrowRoot.gameObject.SetActive(true);
        }
    }

    void OnEnable()
    {
        questManager = FindFirstObjectByType<QuestManager>();
        ResolveTarget();
        UpdateVisibility();
        SubscribeToQuestEvents(true);
    }

    void LateUpdate()
    {
        // local player only (if Photon is present)
        if (pv != null && PhotonNetwork.IsConnected && !pv.IsMine)
        {
            if (arrowRoot != null) arrowRoot.gameObject.SetActive(false);
            return;
        }

        // periodically refresh target based on current quest
        recheckTimer -= Time.deltaTime;
        if (recheckTimer <= 0f)
        {
            recheckTimer = recheckInterval;
            ResolveTarget();
            UpdateVisibility();
        }

        if (arrowRoot == null)
        {
            Transform child = transform.Find("QuestArrow/ArrowRoot");
            if (child != null) arrowRoot = child;
            if (arrowRoot == null) return;
        }
        
        if (target == null)
        {
            if (hideWhenNoTarget && arrowRoot != null) arrowRoot.gameObject.SetActive(false);
            return;
        }

        // Ensure arrow is active when target exists
        if (arrowRoot != null && !arrowRoot.gameObject.activeSelf)
        {
            arrowRoot.gameObject.SetActive(true);
        }

        // compute direction on horizontal plane
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f)
        {
            // target is basically on top; keep visible but no rotation change
            return;
        }

        // world direction toward target
        Vector3 dir = to.normalized;
        // convert to local space to avoid jitter from parent movement/rotation
        Vector3 localDir = transform.InverseTransformDirection(dir);
        // set local position directly (no smoothing) to eliminate wobble
        arrowRoot.localPosition = localDir * radius + Vector3.up * height;
        // rotate in world to face the target smoothly
        Quaternion desiredRot = Quaternion.LookRotation(dir, Vector3.up);
        if (smooth > 0f)
            arrowRoot.rotation = Quaternion.Slerp(arrowRoot.rotation, desiredRot, Time.deltaTime * smooth);
        else
            arrowRoot.rotation = desiredRot;
    }

    // allows manual overriding of the target if needed by other systems
    public void SetTarget(Transform t)
    {
        target = t;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (arrowRoot == null) return;
        bool show = target != null || !hideWhenNoTarget;
        arrowRoot.gameObject.SetActive(show);
    }

    private void ResolveTarget()
    {
        if (questManager == null) 
        {
            questManager = FindFirstObjectByType<QuestManager>();
            if (questManager == null) questManager = QuestManager.Instance;
        }
        if (questManager == null)
        {
            target = null; return;
        }
        var q = questManager.GetCurrentQuest();
        if (q == null)
        {
            target = null; return;
        }
        if (q.isCompleted)
        {
            // hide if the current quest is already completed
            target = null; return;
        }
        // Prefer new multi-objective system
        var obj = q.GetCurrentObjective();
        if (obj != null)
        {
            switch (obj.objectiveType)
            {
                case ObjectiveType.ReachArea:
                    target = FindQuestAreaTransform(obj.targetId);
                    return;
                case ObjectiveType.FindArea:
                    target = null;
                    return;
                case ObjectiveType.TalkTo:
                    target = FindNpcTransform(obj.targetId);
                    return;
                default:
                    target = null; return;
            }
        }
        // Legacy single-objective fallback
        switch (q.objectiveType)
        {
            case ObjectiveType.ReachArea:
                target = FindQuestAreaTransform(q.targetId);
                break;
            case ObjectiveType.FindArea:
                target = null;
                break;
            case ObjectiveType.TalkTo:
                target = FindNpcTransform(q.targetId);
                break;
            default:
                target = null;
                break;
        }
    }

    private Transform FindQuestAreaTransform(string areaId)
    {
        if (string.IsNullOrEmpty(areaId)) return null;
        if (Time.time >= nextAreaCacheRefreshTime || cachedQuestAreas == null || cachedQuestAreas.Length == 0)
        {
            cachedQuestAreas = FindObjectsByType<QuestAreaTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            nextAreaCacheRefreshTime = Time.time + Mathf.Max(0.25f, targetCacheRefreshInterval);
        }
        var areas = cachedQuestAreas;
        Transform best = null; float bestDist = float.MaxValue;
        foreach (var a in areas)
        {
            if (a != null && string.Equals(a.areaId, areaId, System.StringComparison.OrdinalIgnoreCase))
            {
                float d = (a.transform.position - transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = a.transform; }
            }
        }
        return best;
    }

    private Transform FindNpcTransform(string npcId)
    {
        if (string.IsNullOrEmpty(npcId)) return null;
        if (Time.time >= nextNpcCacheRefreshTime || cachedNpcTriggers == null || cachedNpcTriggers.Length == 0)
        {
            cachedNpcTriggers = FindObjectsByType<NPCDialogueTrigger>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            nextNpcCacheRefreshTime = Time.time + Mathf.Max(0.25f, targetCacheRefreshInterval);
        }
        var npcs = cachedNpcTriggers;
        Transform best = null; float bestDist = float.MaxValue;
        foreach (var n in npcs)
        {
            if (n != null && string.Equals(n.npcId, npcId, System.StringComparison.OrdinalIgnoreCase))
            {
                float d = (n.transform.position - transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = n.transform; }
            }
        }
        return best;
    }

    private void SubscribeToQuestEvents(bool subscribe)
    {
        if (questManager == null) questManager = FindFirstObjectByType<QuestManager>();
        if (questManager == null) return;
        if (subscribe)
        {
            questManager.OnQuestStarted += OnQuestChanged;
            questManager.OnQuestUpdated += OnQuestChanged;
            questManager.OnQuestCompleted += OnQuestChanged;
        }
        else
        {
            questManager.OnQuestStarted -= OnQuestChanged;
            questManager.OnQuestUpdated -= OnQuestChanged;
            questManager.OnQuestCompleted -= OnQuestChanged;
        }
    }

    private void OnQuestChanged(Quest q)
    {
        // whenever quest state changes, resolve target and update visibility
        ResolveTarget();
        UpdateVisibility();
    }

    void OnDisable()
    {
        SubscribeToQuestEvents(false);
    }
}
