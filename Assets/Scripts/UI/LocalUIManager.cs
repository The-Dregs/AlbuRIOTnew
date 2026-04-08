using UnityEngine;

public class LocalUIManager : MonoBehaviour
{
    public static LocalUIManager Instance { get; private set; }

    public string CurrentOwner { get; private set; } = null;
    public bool IsAnyOpen => !string.IsNullOrEmpty(CurrentOwner);

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

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static LocalUIManager Ensure()
    {
        if (Instance == null)
        {
            // Never auto-spawn singleton while not in play mode (prevents editor crashes / scene pollution).
            if (!Application.isPlaying)
                return null;

            var go = new GameObject("LocalUIManager");
            Instance = go.AddComponent<LocalUIManager>();
        }
        return Instance;
    }

    public bool TryOpen(string owner)
    {
        // In edit mode, do nothing (prevents creating singletons when scrubbing timelines / editing scenes).
        if (!Application.isPlaying && Instance == null) return false;
        Ensure();

        // During tutorial dialogue, only allow opening the pause menu via Escape.
        if (TutorialManager.IsDialogueBlockingUI && !string.Equals(owner, "PauseMenu", System.StringComparison.Ordinal))
            return false;

        if (IsAnyOpen) return false;
        CurrentOwner = owner;
        return true;
    }

    public void Close(string owner)
    {
        if (!IsAnyOpen) return;
        if (CurrentOwner != owner) return;
        CurrentOwner = null;
    }

    public void ForceClose()
    {
        CurrentOwner = null;
    }

    public bool IsOwner(string owner)
    {
        return IsAnyOpen && CurrentOwner == owner;
    }
}
