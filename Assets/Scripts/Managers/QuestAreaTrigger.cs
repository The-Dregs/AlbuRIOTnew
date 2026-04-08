using UnityEngine;
using UnityEngine.Playables;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;
using TMPro;

// Attach this to a quest area root; child colliders can be used for the actual trigger volume.
[DefaultExecutionOrder(500)] // run after LocalInputLocker (default 0) so LateUpdate cursor wins
public class QuestAreaTrigger : MonoBehaviourPunCallbacks
{
    [Tooltip("identifier used by quest objectives for reach-area tasks")] public string areaId;
    [Tooltip("if true, trigger will only fire once then disable itself once completed")] public bool oneShot = true;

    [Header("Cutscene on Complete (optional)")]
    [Tooltip("Timeline/PlayableDirector to play when this area objective is completed. Leave empty for no cutscene.")]
    public PlayableDirector onCompletePlayable;
    [Tooltip("GameObjects to enable before cutscene plays; they are disabled again when it ends.")]
    public GameObject[] cutsceneEnableObjects;
    [Tooltip("Fully hides all players (model, HUD, camera) while the area cutscene is playing")]
    public bool hidePlayersDuringCutscene = true;
    [Tooltip("Optional skip button shown bottom-right during the area cutscene")]
    public GameObject skipButton;
    [Tooltip("Seconds before the skip button appears")]
    public float skipButtonDelay = 2f;
    [Tooltip("Optional CanvasGroup used to fade the screen in/out at the start and end of the cutscene")]
    public CanvasGroup fadeOverlay;
    [Tooltip("Duration of each fade in seconds")]
    public float fadeDuration = 0.5f;

    [Header("multiplayer gating")]
    [Tooltip("when true, requires all players to be inside before completing (overridden by quest objective.requireAllPlayers when quest matches)")] public bool requireAllPlayers = true;
    [Tooltip("updates a world-space counter (X/Y) when there are >1 players")] public TextMeshPro counterText;
    public bool billboardCounter = true;
    public float billboardHeight = 2f;

    [Header("Quest Outline")]
    [Tooltip("Optional GameObject (e.g. square outline on ground) to show when this area is the active quest target. Hidden when all players present or completed.")]
    public GameObject questSquareOutline;
    [Header("Quest Area Start/Complete Toggle")]
    [Tooltip("Enabled while this area is the active quest target; disabled when quest completes or moves to another objective.")]
    [SerializeField] private GameObject[] enableWhenQuestActive;
    [Tooltip("Disabled while this area is the active quest target; re-enabled when quest completes or moves to another objective.")]
    [SerializeField] private GameObject[] disableWhenQuestActive;
    [Tooltip("Enabled only after this area objective is completed.")]
    [SerializeField] private GameObject[] enableWhenQuestCompleted;
    [Tooltip("Disabled only after this area objective is completed.")]
    [SerializeField] private GameObject[] disableWhenQuestCompleted;
    [Header("Enemy Spawn On Complete")]
    [Tooltip("Optional enemy prefab to spawn once when this area objective is completed.")]
    [SerializeField] private GameObject spawnEnemyWhenQuestCompleted;
    [Tooltip("Optional Resources path used to spawn when EnemyManager is unavailable or prefab is not assigned (example: Enemies/Kapre).")]
    [SerializeField] private string spawnEnemyResourcePathWhenQuestCompleted;
    [Tooltip("Spawn point used for the completed-quest enemy.")]
    [SerializeField] private Transform completedQuestEnemySpawnPoint;

    [Header("Area Children (auto)")]
    [Tooltip("Optional particle/visual root for this quest area. If not assigned, the first child with a ParticleSystem will be used.")]
    public GameObject areaParticleRoot;
    [Tooltip("Optional explicit colliders for this quest area. If empty, all Colliders under this GameObject (including children) are used.")]
    public Collider[] areaColliders;
    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = false;
    [SerializeField, Range(0.1f, 3f)] private float relayCacheRefreshInterval = 0.75f;
    [Header("Performance")]
    [Tooltip("How often to refresh quest-active state (colliders/outline/particles/toggles). Lower = cheaper CPU if many triggers exist.")]
    [SerializeField, Range(0.05f, 2f)] private float questActiveRefreshInterval = 0.25f;
    [Tooltip("How often to update billboard counter positioning/rotation when enabled in multiplayer.")]
    [SerializeField, Range(0.02f, 0.5f)] private float billboardRefreshInterval = 0.08f;

    private Collider _col;
    private Rigidbody _rb;
    private Camera _cam;
    private bool _cutsceneActive = false;
    private Coroutine _presenceUpdateRoutine;
    private bool? _pendingPresenceState;
    private QuestManager _cachedQuestManager;
    private PlayerQuestRelay[] _cachedQuestRelays = System.Array.Empty<PlayerQuestRelay>();
    private float _nextRelayCacheRefreshTime = 0f;
    private float _nextQuestActiveRefreshTime = 0f;
    private float _nextBillboardRefreshTime = 0f;
    private bool _lastMultiState;
    private bool _hasCachedQuestVisualState;
    private bool _lastIsCompleted;
    private bool _lastIsActiveQuest;
    private bool _lastOutlineShown;
    private bool _lastCollidersEnabled;
    private bool _counterWasEverActiveForQuest;
    private bool _spawnCompletedEnemyAfterCutscene;

    private string InAreaKey => $"InArea_{areaId}";
    private string AreaDoneKey => $"AreaDone_{areaId}";
    private string AreaEnemySpawnedKey => $"AreaEnemySpawned_{areaId}";

    private QuestManager GetQuestManager()
    {
        if (_cachedQuestManager == null)
            _cachedQuestManager = FindFirstObjectByType<QuestManager>();
        return _cachedQuestManager;
    }

    private PlayerQuestRelay[] GetQuestRelaysCached()
    {
        if (Time.time >= _nextRelayCacheRefreshTime || _cachedQuestRelays == null || _cachedQuestRelays.Length == 0)
        {
            _cachedQuestRelays = FindObjectsByType<PlayerQuestRelay>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            _nextRelayCacheRefreshTime = Time.time + Mathf.Max(0.1f, relayCacheRefreshInterval);
        }
        return _cachedQuestRelays;
    }

    private void Awake()
    {
        _col = GetComponent<Collider>();
        if (_col != null) _col.isTrigger = true;
        // ensure a rigidbody exists so trigger callbacks fire when players use CharacterController
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
        {
            _rb = gameObject.AddComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;
        }
        _cam = Camera.main;
    }

    private void Start()
    {
        // Auto-resolve world-space counter text if not assigned in inspector.
        if (counterText == null)
        {
            counterText = GetComponentInChildren<TextMeshPro>(true);
        }
        UpdateCounterText();
        // Auto-resolve area colliders if not assigned
        if (areaColliders == null || areaColliders.Length == 0)
        {
            areaColliders = GetComponentsInChildren<Collider>(true);
        }

        // Auto-resolve particle root if not assigned
        if (areaParticleRoot == null)
        {
            var ps = GetComponentInChildren<ParticleSystem>(true);
            if (ps != null) areaParticleRoot = ps.gameObject;
        }

        if (questSquareOutline != null) questSquareOutline.SetActive(false);
        if (areaParticleRoot != null) areaParticleRoot.SetActive(false);
        UpdateQuestAreaActivation();
        _nextQuestActiveRefreshTime = Time.time + Mathf.Max(0.05f, questActiveRefreshInterval);
        _nextBillboardRefreshTime = Time.time + Mathf.Max(0.02f, billboardRefreshInterval);
    }

    private void OnDestroy()
    {
        // In edit mode, avoid touching runtime singletons / coroutines (can create hidden objects and crash editor).
        if (Application.isPlaying)
        {
            StopAllCoroutines();
            // If destroyed mid-cutscene, restore players and release locks so the game isn't left in a broken state
            if (_cutsceneActive)
            {
                if (hidePlayersDuringCutscene) ShowPlayers();
                UnlockLocalPlayers();
                LocalInputLocker.Instance?.ReleaseAllForOwner("AreaCutscene");
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
        _cutsceneActive = false;
        _hiddenRenderers.Clear();
        _hiddenCanvases.Clear();
        _hiddenCameras.Clear();
        _lockedCombats.Clear();
        _lockedControllers.Clear();
        if (questSquareOutline != null) questSquareOutline.SetActive(false);
        if (areaParticleRoot != null) areaParticleRoot.SetActive(false);
    }

    private void LateUpdate()
    {
        // Enforce cursor last — runs after LocalInputLocker.LateUpdate() due to DefaultExecutionOrder(500)
        if (_cutsceneActive)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void Update()
    {
        float now = Time.time;

        // Refresh quest-active state (colliders/outline/particles/toggles) on a small tick.
        if (now >= _nextQuestActiveRefreshTime)
        {
            _nextQuestActiveRefreshTime = now + Mathf.Max(0.05f, questActiveRefreshInterval);
            UpdateQuestAreaActivation();
        }

        if (counterText == null) return;

        bool questActive = IsLocalQuestMatchingArea() && !IsAlreadyCompleted();
        bool multi = questActive && PhotonNetwork.IsConnected && PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.PlayerCount > 1;
        if (multi != _lastMultiState)
        {
            _lastMultiState = multi;
            counterText.gameObject.SetActive(multi);
        }

        if (!multi) return;

        if (billboardCounter && now >= _nextBillboardRefreshTime)
        {
            _nextBillboardRefreshTime = now + Mathf.Max(0.02f, billboardRefreshInterval);

            if (_cam == null) _cam = Camera.main;
            if (_cam != null)
            {
                counterText.transform.position = transform.position + Vector3.up * billboardHeight;
                counterText.transform.rotation = Quaternion.LookRotation(counterText.transform.position - _cam.transform.position, Vector3.up);
            }
        }
    }

    private bool IsPlayerCollider(Collider other)
    {
        // accept if this collider OR any parent is tagged Player, or if a PlayerQuestRelay exists up the chain
        if (other.CompareTag("Player")) return true;
        Transform t = other.transform;
        while (t != null)
        {
            if (t.CompareTag("Player") || t.GetComponent<PlayerQuestRelay>() != null) return true;
            t = t.parent;
        }
        return false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayerCollider(other)) return;

        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            // only the local player should write their own presence
            var pv = other.GetComponentInParent<PhotonView>();
            if (pv != null && pv.IsMine)
            {
                if (GetRequireAllPlayersForCurrentQuest())
                {
                    // in coop count presence regardless of local objective so all players can be counted
                    SetLocalPresence(true);
                    // only the master will actually complete, gated by their objective in EvaluateAndMaybeComplete
                    EvaluateAndMaybeComplete();
                }
                else
                {
                    if (IsLocalQuestMatchingArea())
                    {
                        SetLocalPresence(true);
                        // multiplayer but not requiring all players: treat like offline for the local owner
                        var qm = GetQuestManager();
                        if (qm != null)
                        {
                            AddProgressForAreaType(qm, areaId);
                            if (enableDebugLogs) Debug.Log($"quest area progress updated (mp solo) for area: {areaId}");
                            if (oneShot && _col != null) _col.enabled = false;
                            NotifyQuestAreaCompleted();
                            TriggerCutsceneIfSet();
                        }
                        else
                        {
                            if (enableDebugLogs) Debug.LogWarning($"QuestManager not found while processing reach area '{areaId}' in mp solo mode.");
                        }
                    }
                    else
                    {
                        // not our objective, don't count for solo mode
                        SetLocalPresence(false);
                        if (enableDebugLogs) Debug.Log($"ignored area enter for {areaId}: not current reach objective");
                    }
                }
            }
        }
        else
        {
            // offline/single-player fallback
            if (IsLocalQuestMatchingArea())
            {
                var qm = GetQuestManager();
                if (qm != null)
                {
                    AddProgressForAreaType(qm, areaId);
                    if (enableDebugLogs) Debug.Log($"quest area progress updated (offline) for area: {areaId}");
                    if (oneShot && _col != null) _col.enabled = false;
                    NotifyQuestAreaCompleted();
                    TriggerCutsceneIfSet();
                }
            }
            else
            {
                if (enableDebugLogs) Debug.Log($"ignored area enter (offline) for {areaId}: not current reach objective");
            }
        }
        UpdateCounterText();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayerCollider(other)) return;
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            var pv = other.GetComponentInParent<PhotonView>();
            if (pv != null && pv.IsMine)
            {
                // clear presence on exit regardless; re-evaluate coop completion possibility
                SetLocalPresence(false);
                EvaluateAndMaybeComplete();
            }
        }
        UpdateCounterText();
    }

    private void SetLocalPresence(bool inside)
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return;

        // Coalesce rapid enter/exit trigger updates into one safe network write.
        _pendingPresenceState = inside;
        if (_presenceUpdateRoutine == null)
        {
            _presenceUpdateRoutine = StartCoroutine(Co_ApplyPresenceUpdate());
        }
    }

    private System.Collections.IEnumerator Co_ApplyPresenceUpdate()
    {
        while (_pendingPresenceState.HasValue)
        {
            bool inside = _pendingPresenceState.Value;
            _pendingPresenceState = null;

            // wait one frame so we don't mutate Photon props during internal callback enumeration
            yield return null;

            if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null)
                continue;

            if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(InAreaKey, out var currentVal) &&
                currentVal is bool currentBool && currentBool == inside)
            {
                continue;
            }

            Hashtable props = new Hashtable { [InAreaKey] = inside };
            bool retry = false;
            try
            {
                PhotonNetwork.LocalPlayer.SetCustomProperties(props);
            }
            catch (System.InvalidOperationException ex)
            {
                retry = true;
                if (enableDebugLogs) Debug.LogWarning($"[QuestAreaTrigger] Deferred SetCustomProperties retry for {InAreaKey}: {ex.Message}");
            }

            if (retry)
            {
                _pendingPresenceState = inside;
                yield return null;
            }
        }

        _presenceUpdateRoutine = null;
    }

    private bool IsAllPlayersPresent()
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return false;
        var players = PhotonNetwork.PlayerList;
        if (players == null || players.Length == 0) return false;
        foreach (var p in players)
        {
            if (!p.CustomProperties.TryGetValue(InAreaKey, out var val)) return false;
            if (!(val is bool b) || !b) return false;
        }
        return true;
    }

    private bool IsAlreadyCompleted()
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return false;
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(AreaDoneKey, out var val))
        {
            return val is bool b && b;
        }
        return false;
    }

    private void MarkCompleted()
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return;
        Hashtable roomProps = new Hashtable { [AreaDoneKey] = true };
        PhotonNetwork.CurrentRoom.SetCustomProperties(roomProps);
    }

    private void EvaluateAndMaybeComplete()
    {
        if (!GetRequireAllPlayersForCurrentQuest())
        {
            // handled in OnTriggerEnter for mp-solo path
            return;
        }

        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return;

        // only master evaluates and distributes completion
        if (!PhotonNetwork.IsMasterClient) return;
        if (IsAlreadyCompleted()) return;

        // gate: master only completes this area if their local quest matches this area
        // this prevents disabling areas out-of-order when different players are on different steps
        if (!IsLocalQuestMatchingArea())
        {
            return;
        }

        if (IsAllPlayersPresent())
        {
            // send progress to each owning client via their player quest relay
            var relays = GetQuestRelaysCached();
            if (relays != null && relays.Length > 0)
            {
                foreach (var relay in relays)
                {
                    var pv = relay.GetComponent<PhotonView>();
                    if (pv != null && pv.Owner != null)
                    {
                        pv.RPC("RPC_AddReachProgress", pv.Owner, areaId);
                    }
                }
            }
            else
            {
                // fallback: if no relay exists, at least progress the master's local quest
                var qm = GetQuestManager();
                if (qm != null)
                {
                    AddProgressForAreaType(qm, areaId);
                    if (enableDebugLogs) Debug.LogWarning($"no PlayerQuestRelay found; progressed master's quest for area {areaId} as fallback");
                }
            }
            if (enableDebugLogs) Debug.Log($"quest reach area completed by all players: {areaId}");
            MarkCompleted();
            if (oneShot && _col != null) _col.enabled = false;
            NotifyQuestAreaCompleted();
            TriggerCutsceneIfSet();
        }
        UpdateCounterText();
    }

    private void NotifyQuestAreaCompleted()
    {
        bool hasPrefabFallback = spawnEnemyWhenQuestCompleted != null;
        bool hasResourceFallback = !string.IsNullOrWhiteSpace(spawnEnemyResourcePathWhenQuestCompleted);
        if (!hasPrefabFallback && !hasResourceFallback)
            return;

        if (onCompletePlayable != null)
        {
            _spawnCompletedEnemyAfterCutscene = true;
            return;
        }

        if (completedQuestEnemySpawnPoint == null)
        {
            Debug.LogWarning($"[QuestAreaTrigger] Missing completed quest enemy spawn point for area '{areaId}'.");
            return;
        }

        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                TrySpawnCompletedQuestEnemy();
                return;
            }

            if (photonView != null && photonView.gameObject == gameObject)
            {
                photonView.RPC(nameof(RPC_RequestCompletedQuestEnemySpawn), RpcTarget.MasterClient);
            }
            else
            {
                Debug.LogWarning($"[QuestAreaTrigger] PhotonView must be on the same GameObject to request master enemy spawn for area '{areaId}'.");
            }

            return;
        }

        TrySpawnCompletedQuestEnemy();
    }

    [PunRPC]
    private void RPC_RequestCompletedQuestEnemySpawn()
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom)
            return;

        if (!PhotonNetwork.IsMasterClient)
            return;

        TrySpawnCompletedQuestEnemy();
    }

    private bool IsCompletedEnemyAlreadySpawned()
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom)
            return false;

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(AreaEnemySpawnedKey, out var val))
            return val is bool b && b;

        return false;
    }

    private void MarkCompletedEnemySpawned()
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom)
            return;

        Hashtable roomProps = new Hashtable { [AreaEnemySpawnedKey] = true };
        PhotonNetwork.CurrentRoom.SetCustomProperties(roomProps);
    }

    private void TrySpawnCompletedQuestEnemy()
    {
        bool hasPrefabFallback = spawnEnemyWhenQuestCompleted != null;
        bool hasResourceFallback = !string.IsNullOrWhiteSpace(spawnEnemyResourcePathWhenQuestCompleted);
        if ((!hasPrefabFallback && !hasResourceFallback) || completedQuestEnemySpawnPoint == null)
            return;

        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            if (!PhotonNetwork.IsMasterClient)
                return;

            if (IsCompletedEnemyAlreadySpawned())
                return;
        }

        bool spawned = false;

        EnemyManager enemyManager = FindFirstObjectByType<EnemyManager>();
        if (enemyManager != null && spawnEnemyWhenQuestCompleted != null)
        {
            enemyManager.SpawnEnemy(spawnEnemyWhenQuestCompleted, completedQuestEnemySpawnPoint.position, completedQuestEnemySpawnPoint.rotation);
            spawned = true;
        }
        else
        {
            spawned = SpawnCompletedQuestEnemyFromResources();
            if (!spawned && enemyManager == null)
            {
                Debug.LogWarning($"[QuestAreaTrigger] EnemyManager not found and Resources fallback failed for area '{areaId}'.");
            }
        }

        if (spawned && PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            MarkCompletedEnemySpawned();
        }
    }

    private void FlushQueuedCompletedQuestEnemySpawn()
    {
        if (!_spawnCompletedEnemyAfterCutscene)
            return;

        _spawnCompletedEnemyAfterCutscene = false;
        TrySpawnCompletedQuestEnemy();
    }

    private bool SpawnCompletedQuestEnemyFromResources()
    {
        string rawPath = (spawnEnemyResourcePathWhenQuestCompleted ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rawPath) && spawnEnemyWhenQuestCompleted != null)
            rawPath = spawnEnemyWhenQuestCompleted.name;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            Debug.LogWarning($"[QuestAreaTrigger] Missing resources path for completed quest enemy in area '{areaId}'.");
            return false;
        }

        string normalizedPath = rawPath;
        if (!normalizedPath.StartsWith("Enemies/", System.StringComparison.OrdinalIgnoreCase))
            normalizedPath = $"Enemies/{normalizedPath}";

        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode)
        {
            try
            {
                PhotonNetwork.Instantiate(normalizedPath, completedQuestEnemySpawnPoint.position, completedQuestEnemySpawnPoint.rotation);
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[QuestAreaTrigger] Photon instantiate failed for '{normalizedPath}' in area '{areaId}': {ex.Message}");
                return false;
            }
        }

        GameObject localPrefab = Resources.Load<GameObject>(normalizedPath);
        if (localPrefab == null)
        {
            Debug.LogWarning($"[QuestAreaTrigger] Resources prefab not found at '{normalizedPath}' for area '{areaId}'.");
            return false;
        }

        Instantiate(localPrefab, completedQuestEnemySpawnPoint.position, completedQuestEnemySpawnPoint.rotation);
        return true;
    }

    private void AddProgressForAreaType(QuestManager qm, string areaId)
    {
        var q = qm.GetCurrentQuest();
        if (q == null) return;
        
        var obj = q.GetCurrentObjective();
        if (obj != null)
        {
            if (obj.objectiveType == ObjectiveType.FindArea)
            {
                qm.AddProgress_FindArea(areaId);
            }
            else if (obj.objectiveType == ObjectiveType.ReachArea)
            {
                qm.AddProgress_ReachArea(areaId);
            }
        }
        else
        {
            // Legacy fallback
            if (q.objectiveType == ObjectiveType.FindArea)
            {
                qm.AddProgress_FindArea(areaId);
            }
            else if (q.objectiveType == ObjectiveType.ReachArea)
            {
                qm.AddProgress_ReachArea(areaId);
            }
        }
    }
    
    private bool IsLocalQuestMatchingArea()
    {
        var qm = GetQuestManager();
        if (qm == null) return false;
        var q = qm.GetCurrentQuest();
        if (q == null || q.isCompleted) return false;
        if (string.IsNullOrEmpty(areaId)) return false;
        // Prefer new multi-objective system
        var obj = q.GetCurrentObjective();
        if (obj != null)
        {
            if (obj.objectiveType != ObjectiveType.ReachArea && obj.objectiveType != ObjectiveType.FindArea) return false;
            var a1 = (obj.targetId ?? string.Empty).Trim();
            var b1 = (areaId ?? string.Empty).Trim();
            if (a1.Length == 0 || b1.Length == 0) return false;
            return string.Equals(a1, b1, System.StringComparison.OrdinalIgnoreCase);
        }
        // Legacy fallback
        if (q.objectiveType != ObjectiveType.ReachArea && q.objectiveType != ObjectiveType.FindArea) return false;
        var a = (q.targetId ?? string.Empty).Trim();
        var b = (areaId ?? string.Empty).Trim();
        if (a.Length == 0 || b.Length == 0) return false;
        return string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns true if this area requires all players. Uses objective.requireAllPlayers when quest matches, else trigger's.</summary>
    public bool GetRequireAllPlayersForCurrentQuest()
    {
        var qm = GetQuestManager();
        if (qm == null) return requireAllPlayers;
        var q = qm.GetCurrentQuest();
        if (q == null || q.isCompleted) return requireAllPlayers;
        var obj = q.GetCurrentObjective();
        if (obj != null && (obj.objectiveType == ObjectiveType.ReachArea || obj.objectiveType == ObjectiveType.FindArea)
            && string.Equals((obj.targetId ?? "").Trim(), (areaId ?? "").Trim(), System.StringComparison.OrdinalIgnoreCase))
        {
            return obj.requireAllPlayers;
        }
        return requireAllPlayers;
    }

    // ---- cutscene helpers ----

    private bool _areaCutsceneSkipped;

    // component snapshots so we restore exactly what was enabled before hiding
    private readonly System.Collections.Generic.List<(Renderer r, bool was)>   _hiddenRenderers = new System.Collections.Generic.List<(Renderer, bool)>();
    private readonly System.Collections.Generic.List<(Canvas c, bool was)>     _hiddenCanvases  = new System.Collections.Generic.List<(Canvas, bool)>();
    private readonly System.Collections.Generic.List<(Camera cam, bool was)>   _hiddenCameras   = new System.Collections.Generic.List<(Camera, bool)>();

    private void TriggerCutsceneIfSet()
    {
        if (onCompletePlayable == null) return;
        
        // Ensure the PhotonView we are using actually lives on the same GameObject as this
        // QuestAreaTrigger, otherwise the RPC will be sent to a different object that does
        // not have RPC_TriggerCutscene and will throw an error.
        bool hasLocalPhotonView = photonView != null 
                                  && photonView.gameObject == gameObject;

        if (hasLocalPhotonView && PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            photonView.RPC(nameof(RPC_TriggerCutscene), RpcTarget.AllBuffered);
        }
        else
        {
            // Fallback: play cutscene locally without RPC to avoid crashes when the
            // PhotonView is misconfigured or lives on a parent object.
            if (enableDebugLogs && photonView != null && photonView.gameObject != gameObject)
            {
                Debug.LogWarning($"[QuestAreaTrigger] PhotonView is on a different GameObject ({photonView.gameObject.name}); playing cutscene locally instead of via RPC.");
            }
            StartCoroutine(PlayCutsceneCoroutine());
        }
    }

    [PunRPC]
    private void RPC_TriggerCutscene()
    {
        StartCoroutine(PlayCutsceneCoroutine());
    }

    private System.Collections.IEnumerator PlayCutsceneCoroutine()
    {
        if (onCompletePlayable == null) yield break;

        _areaCutsceneSkipped = false;
        _cutsceneActive = true;

        // Pause global systems (day/night clock and ambient enemy spawns) while the
        // area cutscene is active so time and threat do not advance during story beats.
        if (DayNightCycleManager.Instance != null)
        {
            DayNightCycleManager.Instance.isPaused = true;
        }
        var mapEnemyDirector = FindFirstObjectByType<MapEnemyDirector>();
        bool hadEnemyDirector = mapEnemyDirector != null;
        bool prevNightSpawning = false;
        if (hadEnemyDirector)
        {
            // use reflection-safe pattern: cache original, then restore after cutscene
            var field = typeof(MapEnemyDirector).GetField("enableNightDynamicSpawning", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                prevNightSpawning = (bool)field.GetValue(mapEnemyDirector);
                field.SetValue(mapEnemyDirector, false);
            }
        }

        // ── Instant blackout on the very first frame ─────────────────────────────
        // Slam the overlay to opaque BEFORE any yield so the first rendered frame
        // never shows the transition into the cutscene camera.
        if (fadeOverlay != null)
        {
            fadeOverlay.gameObject.SetActive(true);
            fadeOverlay.alpha = 1f; // fully black — holds until fade-in below
        }

        // ── One frame pause so Unity flushes the hide/blackout to the renderer ──
        yield return null;

        // Block pause menu and all other UIs while the cutscene runs
        bool uiOpened = LocalUIManager.Ensure().TryOpen("AreaCutscene");

        // Lock all local player input
        LockLocalPlayers();
        int lockToken = LocalInputLocker.Ensure().Acquire("AreaCutscene", lockMovement: true, lockCombat: true, lockCamera: true, cursorUnlock: true);

        // Cursor is enforced visible every LateUpdate while _cutsceneActive; set it once now too
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        foreach (var go in cutsceneEnableObjects)
            if (go != null) go.SetActive(true);

        if (!onCompletePlayable.gameObject.activeSelf)
            onCompletePlayable.gameObject.SetActive(true);

        // IMPORTANT: only hide player visuals / cameras AFTER the cutscene camera
        // is active, so we never have a frame with no enabled camera.
        if (hidePlayersDuringCutscene)
            HidePlayers();

        // Fade in: black screen fades out to reveal the cutscene
        if (fadeOverlay != null)
        {
            float ft = 0f;
            while (ft < fadeDuration)
            {
                ft += Time.deltaTime;
                fadeOverlay.alpha = Mathf.Lerp(1f, 0f, ft / fadeDuration);
                yield return null;
            }
            fadeOverlay.alpha = 0f;
        }

        // wire skip button (only host sees it; host's skip RPCs to all)
        if (skipButton != null)
        {
            skipButton.SetActive(false);
            var btn = skipButton.GetComponent<UnityEngine.UI.Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(SkipAreaCutscene);
            }
            StartCoroutine(ShowSkipButtonDelayed());
        }

        onCompletePlayable.Play();

        float timeout = (float)onCompletePlayable.duration + 5f;
        float elapsed = 0f;
        while (onCompletePlayable.state == PlayState.Playing && !_areaCutsceneSkipped && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        onCompletePlayable.Stop();

        if (skipButton != null) skipButton.SetActive(false);

        // Fade out: fade to black before restoring gameplay
        if (fadeOverlay != null)
        {
            fadeOverlay.alpha = 0f;
            float ft = 0f;
            while (ft < fadeDuration)
            {
                ft += Time.deltaTime;
                fadeOverlay.alpha = Mathf.Lerp(0f, 1f, ft / fadeDuration);
                yield return null;
            }
            fadeOverlay.alpha = 1f;
        }

        if (hidePlayersDuringCutscene)
            ShowPlayers();

        foreach (var go in cutsceneEnableObjects)
            if (go != null) go.SetActive(false);

        onCompletePlayable.gameObject.SetActive(false);

        if (fadeOverlay != null)
        {
            fadeOverlay.alpha = 0f;
            fadeOverlay.gameObject.SetActive(false);
        }

        // Release all locks and close UI owner BEFORE stopping cursor enforcement
        if (uiOpened) LocalUIManager.Instance?.Close("AreaCutscene");
        LocalInputLocker.Ensure().Release(lockToken);
        UnlockLocalPlayers();
        _cutsceneActive = false;

        // After the area cutscene fully finishes, ensure any "completed" toggles
        // (e.g. enabling Enemy_Kapre) are applied even when the area isn't using
        // the multiplayer AreaDone room flag.
        SetObjectsActive(enableWhenQuestCompleted, true);
        SetObjectsActive(disableWhenQuestCompleted, false);
        FlushQueuedCompletedQuestEnemySpawn();

        // Resume systems paused at the start of the cutscene.
        if (DayNightCycleManager.Instance != null)
        {
            DayNightCycleManager.Instance.isPaused = false;
        }
        if (hadEnemyDirector)
        {
            var field = typeof(MapEnemyDirector).GetField("enableNightDynamicSpawning", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(mapEnemyDirector, prevNightSpawning);
            }
        }

        // Re-lock cursor for gameplay
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void SkipAreaCutscene()
    {
        bool isHost = PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
        if (!isHost) return;
        var pv = GetComponent<Photon.Pun.PhotonView>();
        if (pv != null && PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
            pv.RPC(nameof(RPC_SkipAreaCutscene), RpcTarget.AllBuffered);
        else
            _areaCutsceneSkipped = true;
    }

    [PunRPC]
    private void RPC_SkipAreaCutscene()
    {
        _areaCutsceneSkipped = true;
    }

    private System.Collections.IEnumerator ShowSkipButtonDelayed()
    {
        yield return new WaitForSeconds(skipButtonDelay);
        if (skipButton != null && !_areaCutsceneSkipped)
        {
            bool isHost = PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient;
            if (isHost)
                skipButton.SetActive(true);
        }
    }

    // Direct input lock — bypasses LocalInputLocker binding and guarantees zero input leakage.
    private readonly System.Collections.Generic.List<(PlayerCombat pc, bool was)>          _lockedCombats     = new System.Collections.Generic.List<(PlayerCombat, bool)>();
    private readonly System.Collections.Generic.List<(ThirdPersonController tc, bool was)> _lockedControllers = new System.Collections.Generic.List<(ThirdPersonController, bool)>();

    private void LockLocalPlayers()
    {
        _lockedCombats.Clear();
        _lockedControllers.Clear();

        var allStats = Object.FindObjectsByType<PlayerStats>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var s in allStats)
        {
            // only lock the local player; remote players should not be affected
            var pv = s.GetComponent<Photon.Pun.PhotonView>();
            if (pv != null && !pv.IsMine) continue;

            var pc = s.GetComponentInChildren<PlayerCombat>(true) ?? s.GetComponentInParent<PlayerCombat>();
            if (pc != null)
            {
                _lockedCombats.Add((pc, pc.enabled));
                pc.SetCanControl(false);
                pc.enabled = false;
            }

            var tc = s.GetComponentInChildren<ThirdPersonController>(true) ?? s.GetComponentInParent<ThirdPersonController>();
            if (tc != null)
            {
                _lockedControllers.Add((tc, tc.enabled));
                tc.SetCanMove(false);
                tc.SetCanControl(false);
            }
        }
    }

    private void UnlockLocalPlayers()
    {
        for (int i = 0; i < _lockedCombats.Count; i++)
        {
            var (pc, was) = _lockedCombats[i];
            if (pc == null) continue;
            pc.enabled = was;
            pc.SetCanControl(true);
        }
        for (int i = 0; i < _lockedControllers.Count; i++)
        {
            var (tc, was) = _lockedControllers[i];
            if (tc == null) continue;
            tc.SetCanMove(true);
            tc.SetCanControl(true);
        }
        _lockedCombats.Clear();
        _lockedControllers.Clear();
    }

    // Snapshot-based hide: disables Renderer, Canvas, and Camera on every player,
    // remembering their exact enabled state so ShowPlayers() restores it perfectly.
    private void HidePlayers()
    {
        _hiddenRenderers.Clear();
        _hiddenCanvases.Clear();
        _hiddenCameras.Clear();

        // Include inactive so we still hide late-joined / temporarily-disabled player roots
        var players = Object.FindObjectsByType<PlayerStats>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var p in players)
        {
            foreach (var r in p.GetComponentsInChildren<Renderer>(true))
            {
                _hiddenRenderers.Add((r, r.enabled));
                r.enabled = false;
            }
            foreach (var c in p.GetComponentsInChildren<Canvas>(true))
            {
                _hiddenCanvases.Add((c, c.enabled));
                c.enabled = false;
            }
            foreach (var cam in p.GetComponentsInChildren<Camera>(true))
            {
                _hiddenCameras.Add((cam, cam.enabled));
                cam.enabled = false;
            }
        }
    }

    private void ShowPlayers()
    {
        for (int i = 0; i < _hiddenRenderers.Count; i++)
        {
            var (r, was) = _hiddenRenderers[i];
            if (r != null) r.enabled = was;
        }
        for (int i = 0; i < _hiddenCanvases.Count; i++)
        {
            var (c, was) = _hiddenCanvases[i];
            if (c != null) c.enabled = was;
        }
        for (int i = 0; i < _hiddenCameras.Count; i++)
        {
            var (cam, was) = _hiddenCameras[i];
            if (cam != null) cam.enabled = was;
        }

        _hiddenRenderers.Clear();
        _hiddenCanvases.Clear();
        _hiddenCameras.Clear();
    }

    private void UpdateQuestAreaActivation()
    {
        // Quest active for this area (per local player's current objective)
        bool isCompleted = IsAlreadyCompleted();
        bool isActiveQuest = IsLocalQuestMatchingArea() && !isCompleted;
        // For visual toggles (enableWhenQuestCompleted / disableWhenQuestCompleted), delay
        // completion effects until after any area cutscene has finished.
        bool completedForVisuals = isCompleted && !_cutsceneActive;

        bool stateChanged = !_hasCachedQuestVisualState
                            || isCompleted != _lastIsCompleted
                            || isActiveQuest != _lastIsActiveQuest;

        // Enable/disable colliders so players can only trigger the area when the quest actually targets it
        if (areaColliders != null && areaColliders.Length > 0 && (!_hasCachedQuestVisualState || _lastCollidersEnabled != isActiveQuest))
        {
            foreach (var c in areaColliders)
            {
                if (c != null) c.enabled = isActiveQuest;
            }
            _lastCollidersEnabled = isActiveQuest;
        }

        // Particle / visual root: only visible while this area is the active quest target
        if (areaParticleRoot != null && (stateChanged || areaParticleRoot.activeSelf != isActiveQuest))
        {
            areaParticleRoot.SetActive(isActiveQuest);
        }

        // Outline: visible while quest is active; for require-all, hide once everyone is present
        if (questSquareOutline != null)
        {
            bool shouldShow = isActiveQuest;
            if (shouldShow && GetRequireAllPlayersForCurrentQuest())
                shouldShow = !IsAllPlayersPresent();
            if (!_hasCachedQuestVisualState || _lastOutlineShown != shouldShow || questSquareOutline.activeSelf != shouldShow)
            {
                questSquareOutline.SetActive(shouldShow);
                _lastOutlineShown = shouldShow;
            }
        }

        if (stateChanged)
        {
            SetObjectsActive(enableWhenQuestActive, isActiveQuest);
            SetObjectsActive(disableWhenQuestActive, !isActiveQuest);
            SetObjectsActive(enableWhenQuestCompleted, completedForVisuals);
            SetObjectsActive(disableWhenQuestCompleted, !completedForVisuals);
        }

        if (isActiveQuest) _counterWasEverActiveForQuest = true;

        // Keep the counter hidden unless this area is the active quest target.
        if (!isActiveQuest && counterText != null)
        {
            counterText.gameObject.SetActive(false);
        }

        // Destroy the counter once this quest area is no longer relevant (completed OR quest moved on),
        // so the text can never show again later.
        if ((isCompleted || (_counterWasEverActiveForQuest && !isActiveQuest)) && counterText != null)
        {
            Destroy(counterText.gameObject);
            counterText = null;
        }

        _hasCachedQuestVisualState = true;
        _lastIsCompleted = isCompleted;
        _lastIsActiveQuest = isActiveQuest;
    }

    private static void SetObjectsActive(GameObject[] objects, bool shouldBeActive)
    {
        if (objects == null || objects.Length == 0) return;
        for (int i = 0; i < objects.Length; i++)
        {
            var go = objects[i];
            if (go != null && go.activeSelf != shouldBeActive)
            {
                go.SetActive(shouldBeActive);
            }
        }
    }

    private void UpdateCounterText()
    {
        if (counterText == null) return;
        // Only show the counter while this area is the active quest target.
        if (!IsLocalQuestMatchingArea() || IsAlreadyCompleted())
        {
            counterText.gameObject.SetActive(false);
            return;
        }
        if (!(PhotonNetwork.IsConnected && PhotonNetwork.InRoom))
        {
            counterText.gameObject.SetActive(false);
            return;
        }
        int total = PhotonNetwork.CurrentRoom.PlayerCount;
        if (total <= 1)
        {
            counterText.gameObject.SetActive(false);
            return;
        }
        int present = 0;
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (p.CustomProperties.TryGetValue(InAreaKey, out var val) && val is bool b && b) present++;
        }
        counterText.text = $"{present}/{total}";
        counterText.gameObject.SetActive(true);
    }

    // --- photon callbacks to keep counts fresh ---
    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (changedProps.ContainsKey(InAreaKey))
        {
            EvaluateAndMaybeComplete();
            UpdateCounterText();
        }
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        UpdateCounterText();
    }

    public override void OnJoinedRoom()
    {
        // Ensure initial multiplayer display (e.g. 0/2) is correct right after joining.
        UpdateCounterText();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        // if someone left, re-evaluate (players remaining may all be present now)
        EvaluateAndMaybeComplete();
        UpdateCounterText();
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        // new master should re-evaluate state
        EvaluateAndMaybeComplete();
        UpdateCounterText();
    }
}
