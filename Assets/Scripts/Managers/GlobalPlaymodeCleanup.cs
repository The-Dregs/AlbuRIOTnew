using UnityEngine;
using Photon.Pun;
using System;
using System.Collections;

[DefaultExecutionOrder(-10000)]
public class GlobalPlaymodeCleanup : MonoBehaviour
{
    public static bool IsQuitting { get; private set; }
    public static event Action OnQuitting;

    // tracks whether we already ran cleanup (prevents double-run)
    private bool cleanupDone;
    // when true, wantsToQuit intercepted the quit and is waiting for photon disconnect
    private bool waitingForDisconnect;
    // safety timeout so the game always closes even if photon hangs
    private const float DisconnectTimeout = 3f;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.wantsToQuit += OnWantsToQuit;
        Application.quitting += HandleQuitting;
    }

    void OnDestroy()
    {
        Application.wantsToQuit -= OnWantsToQuit;
        Application.quitting -= HandleQuitting;
    }

    /// <summary>
    /// Called before Application.quitting — fires on Alt+F4, taskbar close, Application.Quit(), etc.
    /// Returning false delays the quit so Photon can disconnect cleanly.
    /// </summary>
    private bool OnWantsToQuit()
    {
        // already finished cleanup, allow quit
        if (cleanupDone) return true;

        // already waiting for disconnect, keep blocking
        if (waitingForDisconnect) return false;

        Debug.Log("[GlobalPlaymodeCleanup] Intercepted quit request, starting clean shutdown...");
        IsQuitting = true;

        // run the main cleanup logic (everything except the final quit)
        PerformCleanup();

        // if photon is still connected, delay quit until disconnect completes
        if (PhotonNetwork.IsConnected)
        {
            waitingForDisconnect = true;
            try { PhotonNetwork.Disconnect(); } catch { }
            StartCoroutine(WaitForDisconnectThenQuit());
            return false; // block quit for now
        }

        // photon already disconnected, allow quit immediately
        cleanupDone = true;
        return true;
    }

    private IEnumerator WaitForDisconnectThenQuit()
    {
        float elapsed = 0f;
        while (PhotonNetwork.IsConnected && elapsed < DisconnectTimeout)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (elapsed >= DisconnectTimeout)
            Debug.LogWarning("[GlobalPlaymodeCleanup] Photon disconnect timed out, forcing quit.");
        else
            Debug.Log("[GlobalPlaymodeCleanup] Photon disconnected cleanly.");

        cleanupDone = true;
        waitingForDisconnect = false;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// Fallback — fires after wantsToQuit returns true.
    /// Ensures cleanup runs even if wantsToQuit was bypassed.
    /// </summary>
    private void HandleQuitting()
    {
        if (this == null) return;
        PerformCleanup();
        FinalizeQuit();
    }

    /// <summary>
    /// MonoBehaviour callback — extra safety net for forced shutdowns.
    /// </summary>
    void OnApplicationQuit()
    {
        if (this == null) return;
        PerformCleanup();
        FinalizeQuit();
    }

    private void PerformCleanup()
    {
        if (IsQuitting && cleanupDone) return; // already ran
        IsQuitting = true;

        Debug.Log("[GlobalPlaymodeCleanup] Application quitting, performing cleanup...");

        try { OnQuitting?.Invoke(); } catch { }

        // cleanup procedural generation
        if (MemoryCleanupManager.Instance != null)
        {
            try { MemoryCleanupManager.Instance.CleanupProceduralGeneration(); } catch { }
            try { MemoryCleanupManager.Instance.PerformFullCleanup(true); } catch { }
        }

        // ---- Destroy all DontDestroyOnLoad GameObjects ----
        // Each singleton's OnDestroy already nulls its own static Instance.
        // Without explicit destruction, these objects persist as zombie objects
        // between editor play sessions (especially with domain reload disabled).
        DestroyDontDestroyOnLoadObjects();

        // cleanup audio
        try { AudioListener.pause = true; } catch { }

        // unload resources
        try { Resources.UnloadUnusedAssets(); } catch { }

        // force garbage collection
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        catch { }

        Debug.Log("[GlobalPlaymodeCleanup] Cleanup complete.");
    }

    private void FinalizeQuit()
    {
        // disconnect from photon if still connected (safety fallback)
        if (PhotonNetwork.IsConnected)
        {
            try { PhotonNetwork.Disconnect(); } catch { }
        }

        cleanupDone = true;

        // reset quit flag so next editor play session starts clean
        IsQuitting = false;
        OnQuitting = null;
    }

    private void DestroyDontDestroyOnLoadObjects()
    {
        // DontDestroyOnLoad objects live in a hidden scene; find them through all root objects
        // of gameObject.scene (which is the DDOL scene for this object)
        try
        {
            if (this == null) return;
            var ddolScene = gameObject.scene;
            if (!ddolScene.IsValid()) return;
            var roots = ddolScene.GetRootGameObjects();
            foreach (var root in roots)
            {
                if (root == null) continue;
                if (root == gameObject) continue; // destroy ourselves last
                // use Destroy instead of DestroyImmediate to avoid inspector NullRefs
                try { Destroy(root); } catch { }
            }
        }
        catch { }
    }
}
