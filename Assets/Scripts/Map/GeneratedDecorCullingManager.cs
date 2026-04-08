using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

[DisallowMultipleComponent]
public class GeneratedDecorCullingManager : MonoBehaviour
{
    [SerializeField] private bool skipObjectsWithLodGroup = true;
    [SerializeField] private float hideDistance = 120f;
    [SerializeField] private float showHysteresis = 0.85f;
    [SerializeField] private float colliderDisableDistance = 70f;
    [SerializeField] private float updateInterval = 0.2f;
    [SerializeField] private int maxTogglesPerTick = 250;
    [SerializeField] private bool includeColliders = false;
    [SerializeField] private bool showDebugOverlay = false;

    private readonly List<DecorEntry> entries = new List<DecorEntry>(2048);
    private readonly List<int> deadIndices = new List<int>(128);
    private Transform target;
    private float nextTickTime;
    private int roundRobinIndex;
    private int visibleCount;
    private int lastTickToggles;
    private float lastTickMs;
    private GUIStyle overlayStyle;
    private float nextTargetResolveTime;
    [SerializeField, Range(0.1f, 3f)] private float targetResolveInterval = 0.75f;

    private sealed class DecorEntry
    {
        public Transform root;
        public Renderer[] renderers;
        public Collider[] colliders;
        public bool hasLodGroup;
        public bool visible = true;
        public bool collidersEnabled = true;
    }

    public void Configure(
        bool skipLodGroups,
        float hideDist,
        float showHysteresisFactor,
        float colliderDist,
        float tickInterval,
        int maxToggles,
        bool toggleColliders,
        bool enableOverlay)
    {
        skipObjectsWithLodGroup = skipLodGroups;
        hideDistance = Mathf.Max(20f, hideDist);
        showHysteresis = Mathf.Clamp(showHysteresisFactor, 0.5f, 0.99f);
        colliderDisableDistance = Mathf.Max(15f, colliderDist);
        updateInterval = Mathf.Max(0.05f, tickInterval);
        maxTogglesPerTick = Mathf.Clamp(maxToggles, 20, 4000);
        includeColliders = toggleColliders;
        showDebugOverlay = enableOverlay;
    }

    public void ClearAll()
    {
        entries.Clear();
        deadIndices.Clear();
        roundRobinIndex = 0;
        visibleCount = 0;
        lastTickToggles = 0;
        lastTickMs = 0f;
    }

    public void Register(Transform root)
    {
        if (root == null) return;
        var entry = new DecorEntry
        {
            root = root,
            renderers = root.GetComponentsInChildren<Renderer>(true),
            colliders = includeColliders ? root.GetComponentsInChildren<Collider>(true) : null,
            hasLodGroup = root.GetComponentInChildren<LODGroup>() != null
        };
        entries.Add(entry);
        visibleCount++;
    }

    private void Update()
    {
        if (entries.Count == 0) return;
        if (Time.unscaledTime < nextTickTime) return;
        nextTickTime = Time.unscaledTime + updateInterval;
        float tickStart = Time.realtimeSinceStartup;

        ResolveTarget();
        if (target == null) return;

        float hideDistSqr = hideDistance * hideDistance;
        float showDist = hideDistance * showHysteresis;
        float showDistSqr = showDist * showDist;
        float colDisableSqr = colliderDisableDistance * colliderDisableDistance;
        float colEnableDist = colliderDisableDistance * showHysteresis;
        float colEnableSqr = colEnableDist * colEnableDist;

        int toggles = 0;
        int processed = 0;
        int count = entries.Count;

        while (processed < count && toggles < maxTogglesPerTick)
        {
            if (roundRobinIndex >= count) roundRobinIndex = 0;
            var e = entries[roundRobinIndex];
            processed++;
            roundRobinIndex++;

            if (e == null || e.root == null)
            {
                deadIndices.Add(roundRobinIndex - 1);
                continue;
            }

            if (skipObjectsWithLodGroup && e.hasLodGroup)
                continue;

            float d2 = (e.root.position - target.position).sqrMagnitude;

            bool shouldShow = e.visible ? d2 <= hideDistSqr : d2 <= showDistSqr;
            if (shouldShow != e.visible)
            {
                SetRenderersEnabled(e.renderers, shouldShow);
                if (shouldShow) visibleCount++;
                else visibleCount--;
                e.visible = shouldShow;
                toggles++;
            }

            if (includeColliders && e.colliders != null && e.colliders.Length > 0)
            {
                bool shouldEnableColliders = e.collidersEnabled ? d2 <= colDisableSqr : d2 <= colEnableSqr;
                if (shouldEnableColliders != e.collidersEnabled)
                {
                    SetCollidersEnabled(e.colliders, shouldEnableColliders);
                    e.collidersEnabled = shouldEnableColliders;
                    toggles++;
                }
            }
        }

        if (deadIndices.Count > 0)
        {
            for (int i = deadIndices.Count - 1; i >= 0; i--)
            {
                int idx = deadIndices[i];
                if (idx >= 0 && idx < entries.Count)
                {
                    var dead = entries[idx];
                    if (dead != null && dead.visible)
                        visibleCount = Mathf.Max(0, visibleCount - 1);
                    entries.RemoveAt(idx);
                }
            }
            deadIndices.Clear();
            if (roundRobinIndex >= entries.Count)
                roundRobinIndex = 0;
        }

        lastTickToggles = toggles;
        lastTickMs = (Time.realtimeSinceStartup - tickStart) * 1000f;
    }

    private void ResolveTarget()
    {
        if (target != null) return;
        if (Time.unscaledTime < nextTargetResolveTime) return;
        nextTargetResolveTime = Time.unscaledTime + Mathf.Max(0.1f, targetResolveInterval);

        target = PlayerRegistry.GetLocalPlayerTransform();
        if (target != null) return;

        if (Camera.main != null)
            target = Camera.main.transform;
    }

    private void OnDestroy()
    {
        ClearAll();
        target = null;
        overlayStyle = null;
    }

    private static void SetRenderersEnabled(Renderer[] renderers, bool enabled)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r != null) r.enabled = enabled;
        }
    }

    private static void SetCollidersEnabled(Collider[] colliders, bool enabled)
    {
        if (colliders == null) return;
        for (int i = 0; i < colliders.Length; i++)
        {
            var c = colliders[i];
            if (c != null) c.enabled = enabled;
        }
    }

    private void OnGUI()
    {
        if (!showDebugOverlay) return;

        if (overlayStyle == null)
        {
            overlayStyle = new GUIStyle(GUI.skin.box);
            overlayStyle.alignment = TextAnchor.UpperLeft;
            overlayStyle.fontSize = 12;
            overlayStyle.normal.textColor = Color.white;
        }

        int total = entries.Count;
        int visible = Mathf.Clamp(visibleCount, 0, total);
        int hidden = Mathf.Max(0, total - visible);

        string text =
            $"Decor Culling\n" +
            $"Registered: {total}\n" +
            $"Visible: {visible}  Hidden: {hidden}\n" +
            $"Last Tick: {lastTickMs:F2} ms  Toggles: {lastTickToggles}\n" +
            $"Interval: {updateInterval:F2}s";

        GUI.Box(new Rect(12f, 12f, 260f, 105f), text, overlayStyle);
    }
}
