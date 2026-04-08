using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class LocalInputLocker : MonoBehaviour
{
    public static LocalInputLocker Instance { get; private set; }

    [Header("Cursor behavior on lock")]
    public bool unlockCursorOnLock = true;
    public CursorLockMode unlockedMode = CursorLockMode.None;
    public bool showCursorOnLock = true;

    [Header("Auto-detect local components")]
    public bool autoDetectOnFirstLock = true;
    [SerializeField] private bool manageOtherCameraControllers = false;
    [SerializeField] private bool enableDebugLogs = false;

    [Tooltip("Manually assign if auto-detection is unreliable")]
    [SerializeField] private ThirdPersonController playerController;
    [SerializeField] private PlayerCombat playerCombat;
    [SerializeField] private ThirdPersonCameraOrbit cameraOrbit;
    // We keep camera following the player; we only lock rotation when requested.

    // track other camera controller scripts (imported assets or examples) that may also respond to mouse input
    private readonly string[] _otherCameraControllerNames = new[] { "CameraControl", "FlyCameraControl", "CameraController", "FreestyleCameraController", "LB_CameraMove", "ProceduralController" };
    private readonly System.Collections.Generic.List<MonoBehaviour> _disabledOtherCameraControllers = new System.Collections.Generic.List<MonoBehaviour>();
    private readonly System.Collections.Generic.List<MonoBehaviour> _cachedOtherCameraControllers = new System.Collections.Generic.List<MonoBehaviour>(16);
    // guard: only search/restore camera controllers when the locked state actually changes
    private bool _cameraLockApplied = false;

    private class LockRequest
    {
        public string owner;
        public bool lockMovement;
        public bool lockCombat;
        public bool lockCamera;
        public bool requestCursorUnlock;
    }

    private readonly Dictionary<int, LockRequest> _owners = new Dictionary<int, LockRequest>();
    private int _nextToken = 1;
    private bool _applied;
    private bool _resolved;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // Only persist while actually playing. In edit mode, avoid leaving hidden objects around.
        if (Application.isPlaying)
        {
            DontDestroyOnLoad(gameObject);
        }
#if UNITY_EDITOR
        else
        {
            hideFlags = HideFlags.HideAndDontSave;
        }
#endif
    }

    public static LocalInputLocker Ensure()
    {
        if (Instance == null)
        {
            // Never auto-spawn singleton while not in play mode (prevents editor crashes / scene pollution).
            if (!Application.isPlaying)
                return null;

            var go = new GameObject("LocalInputLocker");
            Instance = go.AddComponent<LocalInputLocker>();
        }
        return Instance;
    }

#if UNITY_EDITOR
    // In the editor, ensure the singleton is cleaned up when exiting play mode to avoid
    // "Some objects were not cleaned up when closing the scene" warnings.
    static LocalInputLocker()
    {
        // Avoid duplicate subscriptions across recompiles / domain reload variations.
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && Instance != null)
        {
            // Destroy immediately so nothing leaks between play sessions in the editor.
            Object.DestroyImmediate(Instance.gameObject);
            Instance = null;
        }
    }
#endif

    public bool IsLocked => _owners.Count > 0;

    public int Acquire(string ownerTag)
    {
        if (!_resolved && autoDetectOnFirstLock)
            ResolveLocalRefs();
        int token = _nextToken++;
        _owners[token] = new LockRequest
        {
            owner = string.IsNullOrEmpty(ownerTag) ? "unknown" : ownerTag,
            lockMovement = true,
            lockCombat = true,
            lockCamera = true,
            requestCursorUnlock = true
        };
        ApplyStateIfNeeded();
        return token;
    }

    // partial lock: choose which subsystems to lock for this request
    public int Acquire(string ownerTag, bool lockMovement, bool lockCombat, bool lockCamera, bool cursorUnlock)
    {
        if (!_resolved && autoDetectOnFirstLock)
            ResolveLocalRefs();
        int token = _nextToken++;
        _owners[token] = new LockRequest
        {
            owner = string.IsNullOrEmpty(ownerTag) ? "unknown" : ownerTag,
            lockMovement = lockMovement,
            lockCombat = lockCombat,
            lockCamera = lockCamera,
            requestCursorUnlock = cursorUnlock
        };
        ApplyStateIfNeeded();
        return token;
    }

    public void Release(int token)
    {
        if (_owners.Remove(token))
        {
            ApplyStateIfNeeded();
        }
    }

    public void ReleaseAllForOwner(string ownerTag)
    {
        if (string.IsNullOrEmpty(ownerTag)) return;
        var toRemove = new List<int>();
        foreach (var kv in _owners)
        {
            if (kv.Value.owner == ownerTag) toRemove.Add(kv.Key);
        }
        foreach (var token in toRemove)
            _owners.Remove(token);
        ApplyStateIfNeeded();
    }

    private void ApplyStateIfNeeded()
    {
        // if we need to apply locks but don't yet have local refs, try to resolve now
        if ((_owners.Count > 0) && (!_resolved || playerController == null || playerCombat == null || cameraOrbit == null))
        {
            ResolveLocalRefs();
        }

        bool any = IsLocked;
        // aggregate desired locks across all active requests
        bool wantLockMovement = false, wantLockCombat = false, wantLockCamera = false, wantCursorUnlock = false;
        if (any)
        {
            foreach (var kv in _owners)
            {
                var req = kv.Value;
                wantLockMovement |= req.lockMovement;
                wantLockCombat |= req.lockCombat;
                wantLockCamera |= req.lockCamera;
                wantCursorUnlock |= req.requestCursorUnlock;
            }
        }

        // apply component enables based on aggregate
        if (playerController != null)
        {
            // do not disable the controller; just block input while allowing gravity/anim to continue
            playerController.SetCanMove(!wantLockMovement);
        }
        if (playerCombat != null)
        {
            // block combat input without disabling the component
            playerCombat.SetCanControl(!wantLockCombat);
        }
        if (cameraOrbit != null)
        {
            cameraOrbit.enabled = true;
            cameraOrbit.SetRotationLocked(wantLockCamera);
        }
        // also attempt to disable any other camera controllers that might read mouse axes (even if cameraOrbit is null)
        UpdateOtherCameraControllersState(wantLockCamera);

        // cursor handling only if any request wants unlock and global setting allows
        if (unlockCursorOnLock)
        {
            if (wantCursorUnlock)
            {
                Cursor.lockState = unlockedMode;
                Cursor.visible = showCursorOnLock;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        _applied = any; // tracks that we have some lock active
    }

    // find and disable/enable other camera controller MonoBehaviours by name
    private void UpdateOtherCameraControllersState(bool disable)
    {
        if (!manageOtherCameraControllers) return;
        // only do expensive search when the desired state actually changes
        if (disable == _cameraLockApplied) return;
        _cameraLockApplied = disable;

        if (disable)
        {
            // discover potentially interfering camera controller components once, then reuse cache
            if (_cachedOtherCameraControllers.Count == 0)
            {
                var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var mb in all)
                {
                    if (mb == null) continue;
                    if (mb is ThirdPersonCameraOrbit) continue; // skip our main orbit
                    var tname = mb.GetType().Name;
                    for (int i = 0; i < _otherCameraControllerNames.Length; i++)
                    {
                        if (tname == _otherCameraControllerNames[i])
                        {
                            _cachedOtherCameraControllers.Add(mb);
                            break;
                        }
                    }
                }
            }

            for (int i = 0; i < _cachedOtherCameraControllers.Count; i++)
            {
                var mb = _cachedOtherCameraControllers[i];
                if (mb == null || !mb.enabled) continue;
                try
                {
                    mb.enabled = false;
                    _disabledOtherCameraControllers.Add(mb);
                    if (enableDebugLogs) Debug.Log($"[LocalInputLocker] disabled other camera controller: {mb.GetType().Name} on {mb.gameObject.name}");
                }
                catch { }
            }
        }
        else
        {
            // re-enable ones we disabled earlier (best-effort)
            for (int i = _disabledOtherCameraControllers.Count - 1; i >= 0; i--)
            {
                var mb = _disabledOtherCameraControllers[i];
                if (mb != null)
                {
                    try
                    {
                        mb.enabled = true;
                        if (enableDebugLogs) Debug.Log($"[LocalInputLocker] re-enabled camera controller: {mb.GetType().Name} on {mb.gameObject.name}");
                    }
                    catch { }
                }
                _disabledOtherCameraControllers.RemoveAt(i);
            }
        }
    }

    // Sometimes Unity can leave the cursor visible after UI closes; call this to enforce gameplay lock.
    public void ForceGameplayCursor()
    {
        // only act if a local player controller is present; otherwise, leave menu scenes alone
        if (_owners.Count == 0 && playerController != null && playerController.isActiveAndEnabled)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // Call this when switching to a menu/homescreen scene so the cursor stays visible
    public void EnterMenuMode()
    {
        // clear all active locks and local bindings
        _owners.Clear();
        playerController = null;
        playerCombat = null;
        cameraOrbit = null;
        _resolved = false;
        _cameraLockApplied = false;
        _disabledOtherCameraControllers.Clear();
        _cachedOtherCameraControllers.Clear();
        // ensure cursor is unlocked and visible for menus
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnDestroy()
    {
        // Release all component references so scene objects can be GC'd
        playerController = null;
        playerCombat = null;
        cameraOrbit = null;
        _owners.Clear();
        _disabledOtherCameraControllers.Clear();
        _cachedOtherCameraControllers.Clear();
        _cameraLockApplied = false;
    }

    private void ResolveLocalRefs()
    {
        _resolved = true;
        // find a local player controller first (prefer Photon local if present)
        var controllers = Object.FindObjectsByType<ThirdPersonController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var c in controllers)
        {
            var pv = c.GetComponentInParent<Photon.Pun.PhotonView>();
            if (pv == null || pv.IsMine)
            {
                playerController = c;
                playerCombat = c.GetComponentInChildren<PlayerCombat>(true) ?? c.GetComponentInParent<PlayerCombat>(true);
                break;
            }
        }
        if (cameraOrbit == null)
        {
            // Find local player's camera (same as playerController logic)
            if (Camera.main != null)
            {
                var mainCamOrbit = Camera.main.GetComponent<ThirdPersonCameraOrbit>();
                if (mainCamOrbit != null)
                {
                    // Check if this camera belongs to local player
                    var camPlayer = Camera.main.transform.GetComponentInParent<Photon.Pun.PhotonView>();
                    if (camPlayer == null || camPlayer.IsMine)
                    {
                        cameraOrbit = mainCamOrbit;
                    }
                }
            }
            // Fallback: find any camera orbit that belongs to local player
            if (cameraOrbit == null)
            {
                var allOrbits = Object.FindObjectsByType<ThirdPersonCameraOrbit>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var orbit in allOrbits)
                {
                    var pv = orbit.GetComponentInParent<Photon.Pun.PhotonView>();
                    if (pv == null || pv.IsMine)
                    {
                        cameraOrbit = orbit;
                        break;
                    }
                }
            }
        }
    }

    void LateUpdate()
    {
        // if locks are requested and we still haven't bound to a local controller (e.g., player spawned after UI), retry resolution
        if (IsLocked && (playerController == null || playerCombat == null) && autoDetectOnFirstLock)
        {
            ResolveLocalRefs();
            // re-apply with new refs
            ApplyStateIfNeeded();
        }
        // Only enforce gameplay cursor when we actually have a local player controller resolved.
        // This prevents locking the cursor on menu scenes (e.g., HOMESCREEN) where no player exists.
        if (playerController != null && playerController.isActiveAndEnabled)
        {
            if (!IsLocked && unlockCursorOnLock)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }
}
