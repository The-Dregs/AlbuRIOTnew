using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class MinimapController : MonoBehaviour
{
    public static MinimapController Instance { get; private set; }

    [Header("Input")]
    public KeyCode toggleFullMapKey = KeyCode.M;

    [Header("Panels")]
    [Tooltip("Small minimap shown in the top-right corner.")]
    public GameObject miniMapRoot;
    [Tooltip("Large map shown when pressing M.")]
    public GameObject fullMapRoot;

    [Header("Map Images")]
    public RawImage miniMapImage;
    public RawImage fullMapImage;
    public RawImage miniFogImage;
    public RawImage fullFogImage;

    [Header("Marker Containers")]
    public RectTransform miniMarkerContainer;
    public RectTransform fullMarkerContainer;
    [Tooltip("If enabled, marker positioning/parenting follows map image rects, preventing container mismatch offsets.")]
    public bool useMapImageRectsForMarkers = true;

    [Header("Camera")]
    public Camera minimapCamera;
    [Tooltip("Optional dedicated camera for fullscreen map. If null, minimapCamera is reused.")]
    public Camera fullMapCamera;
    [Tooltip("If enabled, fullscreen camera renders only terrain/water layers for maximum performance.")]
    public bool enforceTerrainWaterOnlyFullMapRendering = true;
    [Tooltip("Optional explicit terrain/water layer mask. If empty and auto detect is enabled, known layer names are used.")]
    public LayerMask terrainWaterLayerMask = 0;
    [Tooltip("Auto-detect terrain/water-like layers (Terrain/Water/Ground/Map/GeneratedTerrain) when explicit mask is empty.")]
    public bool autoDetectTerrainWaterLayers = true;
    public Color fullMapBackgroundColor = new Color(0.12f, 0.2f, 0.35f, 1f);
    [Tooltip("If enabled, fullscreen camera culling mask is overridden so you can hide expensive layers.")]
    public bool useCustomFullMapCullingMask = true;
    public LayerMask fullMapCullingMask = ~0;
    [Tooltip("Optional override for icon projection on fullscreen map. Use this if FullMapImage is rendered by a different camera.")]
    public Camera fullMapMarkerCamera;
    [Tooltip("If true, minimap/fullmap cameras are detached from the player prefab at runtime to prevent parent motion jitter.")]
    public bool detachMapCamerasFromParentOnStart = false;
    [Min(10f)] public float cameraHeight = 100f;
    [Min(0f)] public float cameraFollowSmoothing = 14f;
    public Vector3 cameraOffset = Vector3.zero;
    [Tooltip("If enabled, minimap rotates with local player heading.")]
    public bool rotateCameraWithLocalPlayer = false;
    [Tooltip("If enabled, opening the fullscreen map (M) frames the whole map.")]
    public bool fullMapShowsWholeMap = true;
    [Tooltip("Fixed Y height for fullscreen map camera when framing the map. Keep low to avoid heavy distance fog washout.")]
    public float fullMapCameraFixedHeight = 12f;
    [Min(0f)] public float wholeMapPaddingWorld = 24f;
    [Tooltip("If enabled, the fullscreen map uses a dedicated runtime camera cloned from the assigned camera so it cannot be moved by the player camera rig.")]
    public bool useDedicatedRuntimeFullMapCamera = true;

    [Header("World Bounds")]
    [Tooltip("If enabled, world bounds are read from active terrain.")]
    public bool useActiveTerrainBounds = true;
    [Tooltip("If enabled and no active terrain is present, world bounds are auto-derived from generated scene geometry and players.")]
    public bool autoComputeWorldBoundsWithoutTerrain = true;
    public Vector2 manualWorldCenterXZ = Vector2.zero;
    [Min(1f)] public float manualWorldSizeX = 512f;
    [Min(1f)] public float manualWorldSizeZ = 512f;

    [Header("Marker Prefabs")]
    public RectTransform markerPrefab;
    public Sprite defaultPlayerSprite;
    public Sprite defaultWorldSprite;
    public Color localPlayerColor = new Color(0.2f, 1f, 0.4f, 1f);
    public Color remotePlayerColor = new Color(1f, 0.9f, 0.2f, 1f);
    [Min(4f)] public float playerMarkerSizeMini = 14f;
    [Min(4f)] public float playerMarkerSizeFull = 22f;
    [Min(4f)] public float worldMarkerSizeMini = 12f;
    [Min(4f)] public float worldMarkerSizeFull = 18f;
    public bool rotatePlayerMarkers = true;
    public bool showPlayerNameLabels = true;
    public Color playerNameLabelColor = Color.white;
    [Min(8)] public int playerNameFontSizeMini = 10;
    [Min(10)] public int playerNameFontSizeFull = 14;
    public Vector2 playerNameLabelOffsetMini = new Vector2(0f, -12f);
    public Vector2 playerNameLabelOffsetFull = new Vector2(0f, -16f);

    [Header("Auto Scene Markers")]
    [Tooltip("Auto-create colored minimap icons for generated resources and enemies when no MinimapIcon component is present.")]
    public bool autoGenerateSceneMarkers = true;
    [Tooltip("If enabled, auto markers are scanned only under GeneratedEnvironment root from MapResourcesGenerator.")]
    public bool autoScanGeneratedEnvironmentOnly = true;
    [Min(0.2f)] public float autoSceneScanInterval = 2.5f;
    [Min(0.1f)] public float dynamicEnemyRefreshInterval = 0.5f;
    [Tooltip("If enabled, only enemies and interactable objects receive auto markers.")]
    public bool autoMarkersInteractablesOnly = true;
    public bool includeAutoCampIcons = true;
    public bool includeAutoRemnantIcons = true;
    public bool includeAutoSpawnIcons = true;
    public bool includeAutoPlantIcons = false;
    public bool includeAutoRockIcons = false;
    public bool includeAutoDefaultInteractableIcons = true;
    [Min(4f)] public float autoSceneMarkerSizeMini = 10f;
    [Min(4f)] public float autoSceneMarkerSizeFull = 16f;
    [Min(4f)] public float autoEnemyMarkerSizeMini = 12f;
    [Min(4f)] public float autoEnemyMarkerSizeFull = 18f;
    [Min(4f)] public float autoCampMarkerSizeMini = 11f;
    [Min(4f)] public float autoCampMarkerSizeFull = 17f;
    [Min(4f)] public float autoPlantMarkerSizeMini = 9f;
    [Min(4f)] public float autoPlantMarkerSizeFull = 14f;
    [Min(4f)] public float autoRockMarkerSizeMini = 10f;
    [Min(4f)] public float autoRockMarkerSizeFull = 15f;
    [Min(4f)] public float autoRemnantMarkerSizeMini = 10f;
    [Min(4f)] public float autoRemnantMarkerSizeFull = 16f;
    [Min(4f)] public float autoSpawnMarkerSizeMini = 13f;
    [Min(4f)] public float autoSpawnMarkerSizeFull = 20f;
    public Color autoEnemyColor = new Color(1f, 0.28f, 0.28f, 1f);
    public Color autoCampColor = new Color(1f, 0.55f, 0.2f, 1f);
    public Color autoPlantColor = new Color(0.35f, 0.9f, 0.35f, 1f);
    public Color autoRockColor = new Color(0.75f, 0.75f, 0.75f, 1f);
    public Color autoRemnantColor = new Color(0.2f, 0.85f, 1f, 1f);
    public Color autoSpawnColor = new Color(1f, 0.85f, 0.1f, 1f);
    public Color autoDefaultColor = new Color(1f, 1f, 1f, 1f);

    [Header("Fog Of War")]
    public bool enableFogOfWar = true;
    [Tooltip("If enabled, fog reveal data is used to hide markers in unrevealed areas while terrain remains fully visible.")]
    public bool fogAffectsOnlyIcons = true;
    [Tooltip("If enabled, fullscreen map still draws a translucent fog layer while icons are additionally gated by reveal.")]
    public bool showFogVisualOnFullMap = true;
    public Color fullMapFogTint = new Color(0f, 0f, 0f, 0.55f);
    [Tooltip("If enabled, reveal uses all active players' positions so exploration is shared in multiplayer.")]
    public bool shareFogRevealAcrossPlayers = true;
    [Range(0f, 1f)] public float iconRevealThreshold = 0.95f;
    [Range(64, 2048)] public int fogTextureSize = 512;
    [Min(1f)] public float revealRadiusWorld = 18f;
    [Range(0f, 0.95f)] public float revealSoftness = 0.55f;
    [Min(0.02f)] public float revealTickInterval = 0.08f;

    [Header("Full Map Pan/Zoom")]
    public bool enableFullMapPanZoom = true;
    [Tooltip("If enabled, full-map icons are positioned by world bounds UV (same mapping as fog). Disable to project from full-map camera viewport.")]
    public bool fullMapMarkersUseWorldBoundsUV = true;
    public KeyCode fullMapPanMouseButton = KeyCode.Mouse2;
    [Min(0.01f)] public float fullMapPanSpeed = 1.0f;
    [Min(0.01f)] public float fullMapZoomSpeed = 120f;
    [Min(1f)] public float fullMapMinOrthographicSize = 30f;
    [Min(1f)] public float fullMapMaxOrthographicSize = 500f;
    public KeyCode fullMapResetViewKey = KeyCode.Home;

    [Header("Performance")]
    [Tooltip("If disabled, world icons rely on MinimapIcon OnEnable/OnDisable registration and skip expensive periodic scene scans.")]
    public bool periodicWorldIconRescan = false;
    [Min(0.2f)] public float worldIconRescanInterval = 2f;
    [Tooltip("If enabled, expensive auto/world marker rescans run only when opening full map (M) instead of periodic updates.")]
    public bool rescanMarkersOnlyOnFullMapOpen = true;
    [Tooltip("If true, perform world/auto marker rescan on full map open. Disable to avoid open-time spikes.")]
    public bool performRescanWhenOpeningFullMap = false;

    private readonly Dictionary<int, MarkerPair> playerMarkers = new Dictionary<int, MarkerPair>(8);
    private readonly Dictionary<MinimapIcon, MarkerPair> worldMarkers = new Dictionary<MinimapIcon, MarkerPair>(64);
    private readonly Dictionary<Transform, MarkerPair> autoSceneMarkers = new Dictionary<Transform, MarkerPair>(256);

    private readonly List<int> playerIdsScratch = new List<int>(8);
    private readonly List<MinimapIcon> worldIconsScratch = new List<MinimapIcon>(64);
    private readonly List<Transform> autoSceneScratch = new List<Transform>(256);
    private readonly List<Transform> autoSceneResolvedScratch = new List<Transform>(256);

    private Transform localPlayer;
    private Bounds worldBounds;
    private bool hasWorldBounds;
    private bool isFullMapOpen;
    private bool fullMapViewInitialized;
    private int fullMapInputLockToken;

    private Texture2D fogTexture;
    private Color32[] fogPixels;
    private float revealTimer;
    private float playerRefreshTimer;
    private float iconRefreshTimer;
    private float autoSceneRefreshTimer;
    private float dynamicEnemyRefreshTimer;

    private Sprite runtimeFallbackPlayerSprite;
    private Sprite runtimeFallbackWorldSprite;
    private RenderTexture runtimeFullMapTexture;
    private Camera runtimeFullMapCamera;
    private Camera fullMapCameraTemplate;

    private const string FullMapOwnerTag = "MinimapFullMap";

    private class MarkerPair
    {
        public RectTransform mini;
        public RectTransform full;
        public Image miniImage;
        public Image fullImage;
        public Text miniLabel;
        public Text fullLabel;
        public Transform target;
        public MinimapIcon worldIcon;
        public bool isPlayer;
        public AutoCategory autoCategory;
        public bool rotateWithTarget;
        public float miniSize;
        public float fullSize;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void Start()
    {
        EnsureFallbackSprites();

        if (fullMapCamera == null)
            fullMapCamera = minimapCamera;

        if (useDedicatedRuntimeFullMapCamera)
            EnsureDedicatedFullMapCamera();

        bool isMultiplayer = PhotonNetwork.IsConnected && PhotonNetwork.InRoom;
        if (detachMapCamerasFromParentOnStart && !isMultiplayer)
        {
            DetachCameraFromParent(minimapCamera);
            if (fullMapCamera != minimapCamera)
                DetachCameraFromParent(fullMapCamera);
        }

        if (minimapCamera != null)
            minimapCamera.rect = new Rect(0f, 0f, 1f, 1f);
        if (fullMapCamera != null)
            fullMapCamera.rect = new Rect(0f, 0f, 1f, 1f);

        if (miniMapRoot != null)
            miniMapRoot.SetActive(true);
        if (fullMapRoot != null)
            fullMapRoot.SetActive(false);

        ResolveWorldBounds();
        ApplyFullMapCameraRenderSettings();
        RefreshFullMapImageSource();
        InitializeFog();
        ResolveLocalPlayer();
        RefreshPlayerMarkers();
        RefreshWorldIcons();
        RefreshAutoSceneMarkers();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        CloseFullMap();
        ClearAllMarkers();

        if (runtimeFullMapTexture != null)
        {
            if (fullMapCamera != null && fullMapCamera.targetTexture == runtimeFullMapTexture)
                fullMapCamera.targetTexture = null;
            Destroy(runtimeFullMapTexture);
            runtimeFullMapTexture = null;
        }

        if (runtimeFullMapCamera != null)
        {
            Destroy(runtimeFullMapCamera.gameObject);
            runtimeFullMapCamera = null;
        }
    }

    void Update()
    {
        ResolveLocalPlayer();
        HandleFullMapToggle();
        FollowLocalPlayerWithMinimapCamera();
        UpdateFullMapCamera();

        playerRefreshTimer += Time.deltaTime;
        if (playerRefreshTimer >= 0.33f)
        {
            playerRefreshTimer = 0f;
            RefreshPlayerMarkers();
        }

        iconRefreshTimer += Time.deltaTime;
        if (!rescanMarkersOnlyOnFullMapOpen && periodicWorldIconRescan && iconRefreshTimer >= worldIconRescanInterval)
        {
            iconRefreshTimer = 0f;
            RefreshWorldIcons();
        }

        if (!rescanMarkersOnlyOnFullMapOpen && autoGenerateSceneMarkers)
        {
            autoSceneRefreshTimer += Time.deltaTime;
            if (autoSceneRefreshTimer >= autoSceneScanInterval)
            {
                autoSceneRefreshTimer = 0f;
                RefreshAutoSceneMarkers();
            }
        }

        if (autoGenerateSceneMarkers)
        {
            dynamicEnemyRefreshTimer += Time.deltaTime;
            if (dynamicEnemyRefreshTimer >= dynamicEnemyRefreshInterval)
            {
                dynamicEnemyRefreshTimer = 0f;
                RefreshDynamicEnemyMarkers();
            }
        }

        UpdateMarkerPositions();
        TickFogReveal();
    }

    public void RegisterIcon(MinimapIcon icon)
    {
        if (icon == null || worldMarkers.ContainsKey(icon))
            return;

        float baseSize = Mathf.Max(4f, icon.iconSize);
        MarkerPair pair = CreateMarkerPair(icon.iconSprite != null ? icon.iconSprite : defaultWorldSprite,
                                           icon.iconColor,
                                           baseSize,
                                           baseSize * (worldMarkerSizeFull / Mathf.Max(1f, worldMarkerSizeMini)));
        pair.worldIcon = icon;
        pair.target = icon.transform;
        pair.rotateWithTarget = icon.rotateWithTarget;
        pair.miniSize = baseSize;
        pair.fullSize = baseSize * (worldMarkerSizeFull / Mathf.Max(1f, worldMarkerSizeMini));

        worldMarkers[icon] = pair;
    }

    public void UnregisterIcon(MinimapIcon icon)
    {
        if (icon == null)
            return;

        if (!worldMarkers.TryGetValue(icon, out MarkerPair pair))
            return;

        DestroyMarkerPair(pair);
        worldMarkers.Remove(icon);
    }

    private void HandleFullMapToggle()
    {
        if (!Input.GetKeyDown(toggleFullMapKey))
            return;

        if (!isFullMapOpen)
            OpenFullMap();
        else
            CloseFullMap();
    }

    private void OpenFullMap()
    {
        if (isFullMapOpen)
            return;

        LocalUIManager uiManager = LocalUIManager.Ensure();
        if (uiManager != null && !uiManager.TryOpen(FullMapOwnerTag))
            return;

        if (fullMapRoot != null)
            fullMapRoot.SetActive(true);

        if (fullMapCamera != null && !fullMapCamera.enabled)
            fullMapCamera.enabled = true;

        // run expensive marker discovery only when full map is opened
        if (rescanMarkersOnlyOnFullMapOpen && performRescanWhenOpeningFullMap)
        {
            RefreshWorldIcons();
            if (autoGenerateSceneMarkers)
                RefreshAutoSceneMarkers();
            iconRefreshTimer = 0f;
            autoSceneRefreshTimer = 0f;
        }

        ApplyFullMapCameraRenderSettings();
        RefreshFullMapImageSource();

        LocalInputLocker locker = LocalInputLocker.Ensure();
        if (locker != null && fullMapInputLockToken == 0)
            fullMapInputLockToken = locker.Acquire(FullMapOwnerTag, lockMovement: false, lockCombat: false, lockCamera: true, cursorUnlock: true);

        fullMapViewInitialized = false;
        isFullMapOpen = true;
    }

    private void CloseFullMap()
    {
        if (fullMapRoot != null)
            fullMapRoot.SetActive(false);

        if (fullFogImage != null)
            fullFogImage.uvRect = new Rect(0f, 0f, 1f, 1f);

        if (isFullMapOpen && LocalUIManager.Instance != null)
            LocalUIManager.Instance.Close(FullMapOwnerTag);

        if (fullMapInputLockToken != 0)
        {
            LocalInputLocker locker = LocalInputLocker.Ensure();
            if (locker != null)
                locker.Release(fullMapInputLockToken);
            fullMapInputLockToken = 0;
        }

        if (isFullMapOpen)
            LocalInputLocker.Ensure()?.ForceGameplayCursor();

        isFullMapOpen = false;
        fullMapViewInitialized = false;
    }

    private void ResolveLocalPlayer()
    {
        if (localPlayer != null && localPlayer.gameObject.activeInHierarchy)
        {
            PhotonView currentPv = localPlayer.GetComponent<PhotonView>();
            if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom)
                return;

            Scene activeScene = SceneManager.GetActiveScene();
            if (currentPv != null && currentPv.IsMine && localPlayer.gameObject.scene == activeScene)
                return;
        }

        localPlayer = FindStrictLocalPlayerTransform();
    }

    private Transform FindStrictLocalPlayerTransform()
    {
        // in multiplayer, never fall back to non-owned players
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            Transform anyLocal = null;

            IReadOnlyList<PlayerStats> players = PlayerRegistry.All;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerStats p = players[i];
                if (p == null || !p.gameObject.activeInHierarchy)
                    continue;

                PhotonView pv = p.GetComponent<PhotonView>();
                if (pv != null && pv.IsMine)
                {
                    if (p.gameObject.scene == activeScene)
                        return p.transform;

                    if (anyLocal == null)
                        anyLocal = p.transform;
                }
            }

            // fallback strict scan without tag fallback
            PlayerStats[] stats = FindObjectsByType<PlayerStats>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < stats.Length; i++)
            {
                PlayerStats p = stats[i];
                if (p == null || !p.gameObject.activeInHierarchy)
                    continue;

                PhotonView pv = p.GetComponent<PhotonView>();
                if (pv != null && pv.IsMine)
                {
                    if (p.gameObject.scene == activeScene)
                        return p.transform;

                    if (anyLocal == null)
                        anyLocal = p.transform;
                }
            }

            return anyLocal;
        }

        // offline/singleplayer fallback remains permissive
        return PlayerRegistry.GetLocalPlayerTransform();
    }

    private void FollowLocalPlayerWithMinimapCamera()
    {
        if (minimapCamera == null)
            return;

        // avoid fighting full-map framing/pan when both modes share one camera
        if (isFullMapOpen && fullMapCamera == minimapCamera)
            return;

        if (localPlayer == null)
            return;

        Vector3 targetPos = localPlayer.position + cameraOffset;
        targetPos.y = localPlayer.position.y + cameraHeight + cameraOffset.y;

        if (cameraFollowSmoothing <= 0f)
        {
            minimapCamera.transform.position = targetPos;
        }
        else
        {
            float t = Mathf.Clamp01(Time.deltaTime * cameraFollowSmoothing);
            minimapCamera.transform.position = Vector3.Lerp(minimapCamera.transform.position, targetPos, t);
        }

        Quaternion targetRot = rotateCameraWithLocalPlayer
            ? Quaternion.Euler(90f, localPlayer.eulerAngles.y, 0f)
            : Quaternion.Euler(90f, 0f, 0f);
        minimapCamera.transform.rotation = targetRot;
    }

    private void UpdateFullMapCamera()
    {
        if (!isFullMapOpen)
            return;
        if (fullMapCamera == null)
            return;

        // keep camera render constraints active in case external systems modify them at runtime
        ApplyFullMapCameraRenderSettings();

        if (!fullMapViewInitialized)
        {
            if (fullMapShowsWholeMap)
            {
                ResolveWorldBounds();
                FrameWholeMapOnCamera(fullMapCamera);
            }
            fullMapViewInitialized = true;
        }

        if (enableFullMapPanZoom)
            HandleFullMapPanZoom(fullMapCamera);

        // keep fog overlay aligned after framing/pan/zoom updates in the same frame
        UpdateFullMapFogUvRectFromCamera();

        if (Input.GetKeyDown(fullMapResetViewKey) && fullMapShowsWholeMap)
            FrameWholeMapOnCamera(fullMapCamera);
    }

    private void EnsureDedicatedFullMapCamera()
    {
        if (fullMapCameraTemplate == null)
            fullMapCameraTemplate = fullMapCamera != null ? fullMapCamera : minimapCamera;

        if (runtimeFullMapCamera != null)
        {
            fullMapCamera = runtimeFullMapCamera;
            return;
        }

        if (fullMapCameraTemplate == null)
            return;

        GameObject cameraObject = new GameObject("RuntimeFullMapCamera", typeof(Camera));
        cameraObject.hideFlags = HideFlags.HideAndDontSave;
        runtimeFullMapCamera = cameraObject.GetComponent<Camera>();

        CopyCameraSettings(fullMapCameraTemplate, runtimeFullMapCamera);
        runtimeFullMapCamera.enabled = false;
        runtimeFullMapCamera.rect = new Rect(0f, 0f, 1f, 1f);
        runtimeFullMapCamera.transform.position = fullMapCameraTemplate.transform.position;
        runtimeFullMapCamera.transform.rotation = fullMapCameraTemplate.transform.rotation;
        runtimeFullMapCamera.transform.SetParent(null, true);

        fullMapCamera = runtimeFullMapCamera;
    }

    private static void CopyCameraSettings(Camera source, Camera target)
    {
        if (source == null || target == null)
            return;

        target.clearFlags = source.clearFlags;
        target.backgroundColor = source.backgroundColor;
        target.cullingMask = source.cullingMask;
        target.orthographic = source.orthographic;
        target.orthographicSize = source.orthographicSize;
        target.fieldOfView = source.fieldOfView;
        target.nearClipPlane = source.nearClipPlane;
        target.farClipPlane = source.farClipPlane;
        target.depth = source.depth;
        target.rect = new Rect(0f, 0f, 1f, 1f);
        target.allowHDR = source.allowHDR;
        target.allowMSAA = source.allowMSAA;
    }

    private void UpdateFullMapFogUvRectFromCamera()
    {
        if (fullFogImage == null)
            return;

        if (!enableFogOfWar || fogTexture == null || !hasWorldBounds)
        {
            fullFogImage.uvRect = new Rect(0f, 0f, 1f, 1f);
            return;
        }

        Camera cam = fullMapCamera != null ? fullMapCamera : minimapCamera;
        if (cam == null || !cam.orthographic)
        {
            fullFogImage.uvRect = new Rect(0f, 0f, 1f, 1f);
            return;
        }

        float minX = worldBounds.min.x;
        float maxX = worldBounds.max.x;
        float minZ = worldBounds.min.z;
        float maxZ = worldBounds.max.z;

        float halfZ = cam.orthographicSize;
        float halfX = halfZ * Mathf.Max(0.01f, cam.aspect);
        Vector3 c = cam.transform.position;

        float uMin = Mathf.Clamp01(Mathf.InverseLerp(minX, maxX, c.x - halfX));
        float uMax = Mathf.Clamp01(Mathf.InverseLerp(minX, maxX, c.x + halfX));
        float vMin = Mathf.Clamp01(Mathf.InverseLerp(minZ, maxZ, c.z - halfZ));
        float vMax = Mathf.Clamp01(Mathf.InverseLerp(minZ, maxZ, c.z + halfZ));

        fullFogImage.uvRect = new Rect(
            Mathf.Min(uMin, uMax),
            Mathf.Min(vMin, vMax),
            Mathf.Max(0.0001f, Mathf.Abs(uMax - uMin)),
            Mathf.Max(0.0001f, Mathf.Abs(vMax - vMin)));
    }

    private void FrameWholeMapOnCamera(Camera targetCamera)
    {
        if (targetCamera == null)
            return;
        if (!hasWorldBounds)
            return;

        Vector3 center = worldBounds.center;
        float halfX = worldBounds.extents.x + wholeMapPaddingWorld;
        float halfZ = worldBounds.extents.z + wholeMapPaddingWorld;

        if (!targetCamera.orthographic)
            targetCamera.orthographic = true;

        float safeAspect = Mathf.Max(0.01f, targetCamera.aspect);
        float orthographicHalfHeightForX = halfX / safeAspect;
        float orthographicHalfHeight = Mathf.Max(halfZ, orthographicHalfHeightForX);
        targetCamera.orthographicSize = Mathf.Max(1f, orthographicHalfHeight);

        float groundY = worldBounds.center.y;
        Terrain active = Terrain.activeTerrain;
        if (active != null && active.terrainData != null)
        {
            Vector3 samplePos = new Vector3(center.x, 0f, center.z);
            groundY = active.SampleHeight(samplePos) + active.transform.position.y;
        }
        else
        {
            groundY = worldBounds.min.y;
        }

        // Keep map camera close to the ground plane so scene fog doesn't wash out the full map.
        float heightAboveGround = Mathf.Clamp(fullMapCameraFixedHeight, 2f, 30f);
        float y = groundY + heightAboveGround;
        targetCamera.transform.position = new Vector3(center.x, y, center.z);
        targetCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private void ResolveWorldBounds()
    {
        if (useActiveTerrainBounds && Terrain.activeTerrain != null && Terrain.activeTerrain.terrainData != null)
        {
            Terrain t = Terrain.activeTerrain;
            Vector3 size = t.terrainData.size;
            Vector3 center = t.transform.position + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            worldBounds = new Bounds(center, new Vector3(Mathf.Max(1f, size.x), 1000f, Mathf.Max(1f, size.z)));
            hasWorldBounds = true;
            return;
        }

        if (autoComputeWorldBoundsWithoutTerrain && TryResolveSceneWorldBounds(out Bounds sceneBounds))
        {
            worldBounds = sceneBounds;
            hasWorldBounds = true;
            return;
        }

        worldBounds = new Bounds(new Vector3(manualWorldCenterXZ.x, 0f, manualWorldCenterXZ.y),
                                 new Vector3(Mathf.Max(1f, manualWorldSizeX), 1000f, Mathf.Max(1f, manualWorldSizeZ)));
        hasWorldBounds = true;
    }

    private void RefreshPlayerMarkers()
    {
        playerIdsScratch.Clear();

        IReadOnlyList<PlayerStats> players = PlayerRegistry.All;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerStats stats = players[i];
            if (stats == null || !stats.gameObject.activeInHierarchy)
                continue;

            PhotonView pv = stats.GetComponent<PhotonView>();
            int id = GetPlayerMarkerId(stats, pv);
            playerIdsScratch.Add(id);

            bool isLocal = pv == null || pv.IsMine;
            if (!playerMarkers.TryGetValue(id, out MarkerPair pair))
            {
                pair = CreateMarkerPair(GetFallbackPlayerSprite(), isLocal ? localPlayerColor : remotePlayerColor, playerMarkerSizeMini, playerMarkerSizeFull);
                pair.isPlayer = true;
                playerMarkers[id] = pair;
            }

            pair.target = stats.transform;
            pair.rotateWithTarget = rotatePlayerMarkers;

            Color playerColor = isLocal ? localPlayerColor : remotePlayerColor;
            if (pair.miniImage != null) pair.miniImage.color = playerColor;
            if (pair.fullImage != null) pair.fullImage.color = playerColor;

            ApplyMarkerSize(pair, playerMarkerSizeMini, playerMarkerSizeFull);

            bool showNames = ShouldShowPlayerNameLabels();
            if (showNames)
            {
                EnsurePlayerNameLabels(pair);
                string label = ResolvePlayerDisplayName(stats, pv, id);
                UpdatePlayerNameLabels(pair, label);
            }
            else
            {
                if (pair.miniLabel != null) pair.miniLabel.gameObject.SetActive(false);
                if (pair.fullLabel != null) pair.fullLabel.gameObject.SetActive(false);
            }

            // minimap should never show player names
            if (pair.miniLabel != null)
                pair.miniLabel.gameObject.SetActive(false);
        }

        List<int> keysToRemove = null;
        foreach (KeyValuePair<int, MarkerPair> kv in playerMarkers)
        {
            if (playerIdsScratch.Contains(kv.Key))
                continue;

            if (keysToRemove == null)
                keysToRemove = new List<int>(4);
            keysToRemove.Add(kv.Key);
        }

        if (keysToRemove != null)
        {
            for (int i = 0; i < keysToRemove.Count; i++)
            {
                int key = keysToRemove[i];
                if (playerMarkers.TryGetValue(key, out MarkerPair pair))
                {
                    DestroyMarkerPair(pair);
                    playerMarkers.Remove(key);
                }
            }
        }
    }

    private void RefreshWorldIcons()
    {
        MinimapIcon[] icons = FindObjectsByType<MinimapIcon>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        worldIconsScratch.Clear();

        for (int i = 0; i < icons.Length; i++)
        {
            MinimapIcon icon = icons[i];
            if (icon == null || !icon.isActiveAndEnabled)
                continue;

            worldIconsScratch.Add(icon);
            if (!worldMarkers.ContainsKey(icon))
                RegisterIcon(icon);
            else
                UpdateWorldIconStyle(icon);
        }

        List<MinimapIcon> removeIcons = null;
        foreach (KeyValuePair<MinimapIcon, MarkerPair> kv in worldMarkers)
        {
            if (worldIconsScratch.Contains(kv.Key))
                continue;

            if (removeIcons == null)
                removeIcons = new List<MinimapIcon>(8);
            removeIcons.Add(kv.Key);
        }

        if (removeIcons != null)
        {
            for (int i = 0; i < removeIcons.Count; i++)
                UnregisterIcon(removeIcons[i]);
        }
    }

    private void RefreshAutoSceneMarkers()
    {
        if (!autoGenerateSceneMarkers)
        {
            ClearAutoSceneMarkers();
            return;
        }

        autoSceneScratch.Clear();

        Transform generatedRoot = null;
        if (autoScanGeneratedEnvironmentOnly)
        {
            GameObject rootObj = GameObject.Find("GeneratedEnvironment");
            if (rootObj != null)
                generatedRoot = rootObj.transform;
        }

        if (generatedRoot != null)
        {
            CollectAutoMarkerCandidates(generatedRoot);
        }
        else
        {
            Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (IsValidAutoSceneTarget(t))
                    autoSceneScratch.Add(t);
            }
        }

        for (int i = 0; i < autoSceneScratch.Count; i++)
        {
            Transform t = ResolveAutoMarkerTargetRoot(autoSceneScratch[i]);
            if (t == null || autoSceneResolvedScratch.Contains(t))
                continue;

            autoSceneResolvedScratch.Add(t);

            if (autoSceneMarkers.ContainsKey(t))
            {
                MarkerPair existing = autoSceneMarkers[t];
                AutoCategory existingCategory = ClassifyAutoCategory(t);
                existing.autoCategory = existingCategory;
                if (existing.miniImage != null)
                    existing.miniImage.color = GetAutoCategoryColor(existingCategory);
                if (existing.fullImage != null)
                    existing.fullImage.color = GetAutoCategoryColor(existingCategory);
                existing.miniSize = GetAutoCategoryMiniSize(existingCategory);
                existing.fullSize = GetAutoCategoryFullSize(existingCategory);
                ApplyMarkerSize(existing, existing.miniSize, existing.fullSize);
                continue;
            }

            AutoCategory category = ClassifyAutoCategory(t);
            MarkerPair pair = CreateMarkerPair(GetFallbackWorldSprite(), GetAutoCategoryColor(category), GetAutoCategoryMiniSize(category), GetAutoCategoryFullSize(category));
            pair.target = t;
            pair.rotateWithTarget = false;
            pair.autoCategory = category;
            pair.miniSize = GetAutoCategoryMiniSize(category);
            pair.fullSize = GetAutoCategoryFullSize(category);
            autoSceneMarkers[t] = pair;
        }

        List<Transform> toRemove = null;
        foreach (KeyValuePair<Transform, MarkerPair> kv in autoSceneMarkers)
        {
            Transform t = kv.Key;
            if (t != null && t.gameObject.activeInHierarchy && autoSceneResolvedScratch.Contains(t))
                continue;

            if (toRemove == null)
                toRemove = new List<Transform>(16);
            toRemove.Add(t);
        }

        autoSceneResolvedScratch.Clear();

        if (toRemove != null)
        {
            for (int i = 0; i < toRemove.Count; i++)
            {
                Transform t = toRemove[i];
                if (!autoSceneMarkers.TryGetValue(t, out MarkerPair pair))
                    continue;

                DestroyMarkerPair(pair);
                autoSceneMarkers.Remove(t);
            }
        }

        if (autoComputeWorldBoundsWithoutTerrain && (Terrain.activeTerrain == null || Terrain.activeTerrain.terrainData == null))
        {
            if (TryResolveSceneWorldBounds(out Bounds sceneBounds))
            {
                worldBounds = sceneBounds;
                hasWorldBounds = true;
            }
        }
    }

    private bool TryResolveSceneWorldBounds(out Bounds bounds)
    {
        bounds = default;
        bool initialized = false;

        Transform generatedRoot = null;
        GameObject generatedObj = GameObject.Find("GeneratedEnvironment");
        if (generatedObj != null)
            generatedRoot = generatedObj.transform;

        if (generatedRoot != null)
        {
            Renderer[] renderers = generatedRoot.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled)
                    continue;

                if (!initialized)
                {
                    bounds = r.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            if (!initialized)
            {
                Collider[] colliders = generatedRoot.GetComponentsInChildren<Collider>(false);
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider c = colliders[i];
                    if (c == null || !c.enabled)
                        continue;

                    if (!initialized)
                    {
                        bounds = c.bounds;
                        initialized = true;
                    }
                    else
                    {
                        bounds.Encapsulate(c.bounds);
                    }
                }
            }
        }

        IReadOnlyList<PlayerStats> players = PlayerRegistry.All;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerStats p = players[i];
            if (p == null || !p.gameObject.activeInHierarchy)
                continue;

            Vector3 pos = p.transform.position;
            if (!initialized)
            {
                bounds = new Bounds(pos, Vector3.one);
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(pos);
            }
        }

        if (!initialized)
            return false;

        Vector3 size = bounds.size;
        size.x = Mathf.Max(32f, size.x);
        size.z = Mathf.Max(32f, size.z);
        size.y = Mathf.Max(100f, size.y);
        bounds.size = size;
        return true;
    }

    private void CollectAutoMarkerCandidates(Transform root)
    {
        if (root == null)
            return;

        // prioritize known interactable/destructible roots so all plant variants get markers
        DestructiblePlant[] destructiblePlants = root.GetComponentsInChildren<DestructiblePlant>(false);
        for (int i = 0; i < destructiblePlants.Length; i++)
        {
            DestructiblePlant p = destructiblePlants[i];
            if (p == null)
                continue;
            Transform t = p.transform;
            if (IsValidAutoSceneTarget(t) && !autoSceneScratch.Contains(t))
                autoSceneScratch.Add(t);
        }

        ItemPickup[] itemPickups = root.GetComponentsInChildren<ItemPickup>(false);
        for (int i = 0; i < itemPickups.Length; i++)
        {
            ItemPickup p = itemPickups[i];
            if (p == null)
                continue;
            Transform t = p.transform;
            if (IsValidAutoSceneTarget(t) && !autoSceneScratch.Contains(t))
                autoSceneScratch.Add(t);
        }

        Transform[] networkPickupCandidates = root.GetComponentsInChildren<Transform>(false);
        for (int i = 0; i < networkPickupCandidates.Length; i++)
        {
            Transform t = networkPickupCandidates[i];
            if (t == null)
                continue;
            if (t.GetComponent("NetworkItemPickup") == null)
                continue;
            if (IsValidAutoSceneTarget(t) && !autoSceneScratch.Contains(t))
                autoSceneScratch.Add(t);
        }

        Transform[] children = root.GetComponentsInChildren<Transform>(false);
        for (int i = 0; i < children.Length; i++)
        {
            Transform t = children[i];
            if (IsValidAutoSceneTarget(t) && !autoSceneScratch.Contains(t))
                autoSceneScratch.Add(t);
        }
    }

    private bool IsValidAutoSceneTarget(Transform t)
    {
        if (t == null)
            return false;
        if (!t.gameObject.activeInHierarchy)
            return false;
        if (t.GetComponentInParent<PlayerStats>() != null)
            return false;
        if (t.GetComponent<MinimapIcon>() != null)
            return false;

        bool isEnemy = t.GetComponentInParent<BaseEnemyAI>() != null;
        if (isEnemy)
            return true;

        bool hasRenderable = t.GetComponent<Renderer>() != null
            || t.GetComponent<Collider>() != null
            || t.GetComponentInChildren<Renderer>(true) != null
            || t.GetComponentInChildren<Collider>(true) != null;
        if (!hasRenderable)
            return false;

        string n = t.name.ToLowerInvariant();
        if (n.Contains("generatedenvironment") || n.Contains("generateddecorcullingmanager"))
            return false;

        AutoCategory category = ClassifyAutoCategory(t);
        bool hasInteractableHint = HasInteractableHint(t);

        if (autoMarkersInteractablesOnly && !hasInteractableHint && category != AutoCategory.Enemy && category != AutoCategory.Spawn)
            return false;

        if (category == AutoCategory.Plant && hasInteractableHint)
            return true;

        if (autoMarkersInteractablesOnly && category == AutoCategory.Default)
            return includeAutoDefaultInteractableIcons;

        return IsAutoCategoryEnabled(category) && MightBeMapResource(n);
    }

    private static Transform ResolveAutoMarkerTargetRoot(Transform t)
    {
        if (t == null)
            return null;

        DestructiblePlant destructiblePlant = t.GetComponentInParent<DestructiblePlant>();
        if (destructiblePlant != null)
            return destructiblePlant.transform;

        BaseEnemyAI enemy = t.GetComponentInParent<BaseEnemyAI>();
        if (enemy != null)
            return enemy.transform;

        Transform spawnRoot = null;
        Transform walker = t;
        while (walker != null)
        {
            string n = walker.name.ToLowerInvariant();
            if (n.Contains("spawnmarker") || n.Contains("spawn_marker") || n.StartsWith("spawnmarker_"))
                spawnRoot = walker;
            walker = walker.parent;
        }
        if (spawnRoot != null)
            return spawnRoot;

        Transform node = t;
        while (node != null)
        {
            if (node.GetComponent("ItemPickup") != null
                || node.GetComponent("NetworkItemPickup") != null
                || node.GetComponent("DestructiblePlant") != null
                || node.GetComponent("Interactable") != null
                || node.GetComponent("Interaction") != null)
                return node;

            node = node.parent;
        }

        return t;
    }

    private bool IsAutoCategoryEnabled(AutoCategory category)
    {
        switch (category)
        {
            case AutoCategory.Enemy: return true;
            case AutoCategory.Camp: return includeAutoCampIcons;
            case AutoCategory.Plant: return includeAutoPlantIcons;
            case AutoCategory.Rock: return includeAutoRockIcons;
            case AutoCategory.Remnant: return includeAutoRemnantIcons;
            case AutoCategory.Spawn: return includeAutoSpawnIcons;
            default: return includeAutoDefaultInteractableIcons;
        }
    }

    private static bool HasInteractableHint(Transform t)
    {
        if (t == null)
            return false;

        if (t.GetComponentInParent<DestructiblePlant>() != null) return true;
        if (t.GetComponentInParent<ItemPickup>() != null) return true;
        if (HasComponentInParentByName(t, "NetworkItemPickup")) return true;

        if (t.GetComponent("ItemPickup") != null) return true;
        if (t.GetComponent("NetworkItemPickup") != null) return true;
        if (t.GetComponent("DestructiblePlant") != null) return true;
        if (t.GetComponent("NunoShopManager") != null) return true;
        if (t.GetComponent("NPCDialogueManager") != null) return true;
        if (t.GetComponent("QuestNPC") != null) return true;
        if (t.GetComponent("Interactable") != null) return true;
        if (t.GetComponent("Interaction") != null) return true;

        if (string.Equals(t.tag, "Interactable", System.StringComparison.Ordinal))
            return true;

        string lowerName = t.name.ToLowerInvariant();
        return lowerName.Contains("pickup")
            || lowerName.Contains("loot")
            || lowerName.Contains("chest")
            || lowerName.Contains("shop")
            || lowerName.Contains("vendor")
            || lowerName.Contains("npc")
            || lowerName.Contains("quest")
            || lowerName.Contains("portal")
            || lowerName.Contains("door")
            || lowerName.Contains("interact")
            || lowerName.Contains("herb")
            || lowerName.Contains("geranium")
            || lowerName.Contains("aloe")
            || lowerName.Contains("mint")
            || lowerName.Contains("flower");
    }

    private static bool HasComponentInParentByName(Transform t, string componentTypeName)
    {
        Transform node = t;
        while (node != null)
        {
            if (node.GetComponent(componentTypeName) != null)
                return true;
            node = node.parent;
        }

        return false;
    }

    private static bool MightBeMapResource(string lowerName)
    {
        return lowerName.Contains("tree")
            || lowerName.Contains("fern")
            || lowerName.Contains("plant")
            || lowerName.Contains("herb")
            || lowerName.Contains("geranium")
            || lowerName.Contains("aloe")
            || lowerName.Contains("mint")
            || lowerName.Contains("flower")
            || lowerName.Contains("rock")
            || lowerName.Contains("boulder")
            || lowerName.Contains("ore")
            || lowerName.Contains("camp")
            || lowerName.Contains("remnant")
            || lowerName.Contains("brokenship")
            || lowerName.Contains("spawn")
            || lowerName.Contains("entrance")
            || lowerName.Contains("marker")
            || lowerName.Contains("enemy")
            || lowerName.Contains("guard");
    }

    private enum AutoCategory
    {
        Default,
        Enemy,
        Camp,
        Plant,
        Rock,
        Remnant,
        Spawn
    }

    private AutoCategory ClassifyAutoCategory(Transform t)
    {
        if (t == null)
            return AutoCategory.Default;

        if (t.GetComponentInParent<DestructiblePlant>() != null)
            return AutoCategory.Plant;

        if (t.GetComponentInParent<BaseEnemyAI>() != null)
            return AutoCategory.Enemy;

        string n = t.name.ToLowerInvariant();
        if (n.Contains("camp")) return AutoCategory.Camp;
        if (n.Contains("enemy") || n.Contains("guard")) return AutoCategory.Enemy;
        if (n.Contains("tree") || n.Contains("fern") || n.Contains("plant") || n.Contains("herb") || n.Contains("aloe") || n.Contains("geranium") || n.Contains("mint") || n.Contains("flower")) return AutoCategory.Plant;
        if (n.Contains("rock") || n.Contains("boulder") || n.Contains("ore")) return AutoCategory.Rock;
        if (n.Contains("remnant") || n.Contains("brokenship") || n.Contains("ship")) return AutoCategory.Remnant;
        if (n.Contains("spawn") || n.Contains("entrance") || n.Contains("marker")) return AutoCategory.Spawn;
        return AutoCategory.Default;
    }

    private Color GetAutoCategoryColor(AutoCategory category)
    {
        switch (category)
        {
            case AutoCategory.Enemy: return autoEnemyColor;
            case AutoCategory.Camp: return autoCampColor;
            case AutoCategory.Plant: return autoPlantColor;
            case AutoCategory.Rock: return autoRockColor;
            case AutoCategory.Remnant: return autoRemnantColor;
            case AutoCategory.Spawn: return Color.white;
            default: return autoDefaultColor;
        }
    }

    private void RefreshDynamicEnemyMarkers()
    {
        BaseEnemyAI[] enemies = FindObjectsByType<BaseEnemyAI>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        autoSceneResolvedScratch.Clear();

        for (int i = 0; i < enemies.Length; i++)
        {
            BaseEnemyAI enemy = enemies[i];
            if (enemy == null || !enemy.gameObject.activeInHierarchy)
                continue;

            Transform t = enemy.transform;
            if (autoSceneResolvedScratch.Contains(t))
                continue;

            autoSceneResolvedScratch.Add(t);

            if (!autoSceneMarkers.TryGetValue(t, out MarkerPair pair))
            {
                pair = CreateMarkerPair(GetFallbackWorldSprite(), GetAutoCategoryColor(AutoCategory.Enemy), GetAutoCategoryMiniSize(AutoCategory.Enemy), GetAutoCategoryFullSize(AutoCategory.Enemy));
                pair.target = t;
                pair.rotateWithTarget = false;
                pair.autoCategory = AutoCategory.Enemy;
                pair.miniSize = GetAutoCategoryMiniSize(AutoCategory.Enemy);
                pair.fullSize = GetAutoCategoryFullSize(AutoCategory.Enemy);
                autoSceneMarkers[t] = pair;
            }
            else
            {
                pair.autoCategory = AutoCategory.Enemy;
                if (pair.miniImage != null) pair.miniImage.color = GetAutoCategoryColor(AutoCategory.Enemy);
                if (pair.fullImage != null) pair.fullImage.color = GetAutoCategoryColor(AutoCategory.Enemy);
                pair.miniSize = GetAutoCategoryMiniSize(AutoCategory.Enemy);
                pair.fullSize = GetAutoCategoryFullSize(AutoCategory.Enemy);
                ApplyMarkerSize(pair, pair.miniSize, pair.fullSize);
            }
        }

        List<Transform> remove = null;
        foreach (KeyValuePair<Transform, MarkerPair> kv in autoSceneMarkers)
        {
            Transform t = kv.Key;
            MarkerPair p = kv.Value;
            if (p != null && p.autoCategory == AutoCategory.Enemy && (t == null || !t.gameObject.activeInHierarchy || !autoSceneResolvedScratch.Contains(t)))
            {
                if (remove == null) remove = new List<Transform>(8);
                remove.Add(t);
            }
        }

        if (remove != null)
        {
            for (int i = 0; i < remove.Count; i++)
            {
                Transform t = remove[i];
                if (!autoSceneMarkers.TryGetValue(t, out MarkerPair p))
                    continue;
                DestroyMarkerPair(p);
                autoSceneMarkers.Remove(t);
            }
        }
    }

    private bool ShouldShowPlayerNameLabels()
    {
        if (!showPlayerNameLabels)
            return false;
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
            return false;
        return PhotonNetwork.CurrentRoom.PlayerCount > 1;
    }

    private void UpdateWorldIconStyle(MinimapIcon icon)
    {
        if (icon == null || !worldMarkers.TryGetValue(icon, out MarkerPair pair))
            return;

        pair.target = icon.transform;
        pair.rotateWithTarget = icon.rotateWithTarget;
        float miniSize = Mathf.Max(4f, icon.iconSize);
        float fullSize = miniSize * (worldMarkerSizeFull / Mathf.Max(1f, worldMarkerSizeMini));
        pair.miniSize = miniSize;
        pair.fullSize = fullSize;

        Sprite sprite = icon.iconSprite != null ? icon.iconSprite : GetFallbackWorldSprite();
        if (pair.miniImage != null)
        {
            pair.miniImage.sprite = sprite;
            pair.miniImage.color = icon.iconColor;
        }
        if (pair.fullImage != null)
        {
            pair.fullImage.sprite = sprite;
            pair.fullImage.color = icon.iconColor;
        }

        ApplyMarkerSize(pair, miniSize, fullSize);
    }

    private void UpdateMarkerPositions()
    {
        foreach (KeyValuePair<int, MarkerPair> kv in playerMarkers)
            UpdateMarkerPairPosition(kv.Value);

        foreach (KeyValuePair<MinimapIcon, MarkerPair> kv in worldMarkers)
            UpdateMarkerPairPosition(kv.Value);

        foreach (KeyValuePair<Transform, MarkerPair> kv in autoSceneMarkers)
            UpdateMarkerPairPosition(kv.Value);

        EnsurePlayerMarkersOnTop();
    }

    private void EnsurePlayerMarkersOnTop()
    {
        foreach (KeyValuePair<int, MarkerPair> kv in playerMarkers)
        {
            MarkerPair pair = kv.Value;
            if (pair == null)
                continue;

            if (pair.mini != null)
                pair.mini.SetAsLastSibling();
            if (pair.full != null)
                pair.full.SetAsLastSibling();
        }
    }

    private void UpdateMarkerPairPosition(MarkerPair pair)
    {
        if (pair == null)
            return;

        RectTransform miniRect = GetMiniMarkerRect();
        RectTransform fullRect = GetFullMarkerRect();
        EnsureMarkerParent(pair.mini, miniRect);
        EnsureMarkerParent(pair.full, fullRect);

        Vector3 worldPos;
        if (pair.worldIcon != null)
            worldPos = pair.worldIcon.WorldPosition;
        else if (pair.target != null)
            worldPos = pair.target.position;
        else
        {
            SetMarkerActive(pair, false);
            return;
        }

        if (!TryWorldToNormalized(worldPos, out Vector2 uv))
        {
            SetMarkerActive(pair, false);
            return;
        }

        bool miniInView = SetMarkerPositionFromCamera(pair.mini, minimapCamera, miniRect, worldPos);

        bool fullInView;
        bool shouldUseFullUvAnchoring = fullMapMarkersUseWorldBoundsUV && !(isFullMapOpen && enableFullMapPanZoom);
        if (shouldUseFullUvAnchoring)
        {
            if (TryWorldToNormalized(worldPos, out Vector2 fullUv))
            {
                SetMarkerAnchoredPositionFromUV(pair.full, fullRect, fullUv);
                fullInView = true;
            }
            else
            {
                fullInView = false;
            }
        }
        else
        {
            Camera fullCam = GetFullMapMarkerCamera();
            fullInView = SetMarkerPositionFromCamera(pair.full, fullCam, fullRect, worldPos, pair.isPlayer);
        }

        if (pair.rotateWithTarget && pair.target != null)
        {
            float z = -pair.target.eulerAngles.y;
            if (pair.mini != null) pair.mini.localEulerAngles = new Vector3(0f, 0f, z);
            if (pair.full != null) pair.full.localEulerAngles = new Vector3(0f, 0f, z);
        }
        else
        {
            if (pair.mini != null) pair.mini.localEulerAngles = Vector3.zero;
            if (pair.full != null) pair.full.localEulerAngles = Vector3.zero;
        }

        bool miniVisible = miniInView;
        bool fullVisible = fullInView;

        if (enableFogOfWar && fogAffectsOnlyIcons && !pair.isPlayer)
        {
            fullVisible = fullVisible && IsRevealedAt(uv);
        }

        SetMiniMarkerActive(pair, miniVisible);
        SetFullMarkerActive(pair, fullVisible);
    }

    private Camera GetFullMapMarkerCamera()
    {
        if (fullMapMarkerCamera != null)
            return fullMapMarkerCamera;

        RenderTexture fullRt = fullMapImage != null ? fullMapImage.texture as RenderTexture : null;
        if (fullRt != null)
        {
            if (fullMapCamera != null && fullMapCamera.targetTexture == fullRt)
                return fullMapCamera;

            if (minimapCamera != null && minimapCamera.targetTexture == fullRt)
                return minimapCamera;
        }

        return fullMapCamera != null ? fullMapCamera : minimapCamera;
    }

    private void ApplyFullMapCameraRenderSettings()
    {
        if (fullMapCamera == null)
            return;

        fullMapCamera.rect = new Rect(0f, 0f, 1f, 1f);
        fullMapCamera.clearFlags = CameraClearFlags.SolidColor;
        fullMapCamera.backgroundColor = fullMapBackgroundColor;

        if (enforceTerrainWaterOnlyFullMapRendering)
        {
            int mask = terrainWaterLayerMask.value;
            if (mask == 0 && autoDetectTerrainWaterLayers)
                mask = BuildTerrainWaterLayerMask();

            if (mask != 0)
            {
                fullMapCamera.cullingMask = mask;
                return;
            }
        }

        if (useCustomFullMapCullingMask)
            fullMapCamera.cullingMask = fullMapCullingMask;
    }

    private static int BuildTerrainWaterLayerMask()
    {
        int mask = 0;
        string[] names = new[] { "Terrain", "Water", "Ground", "Map", "GeneratedTerrain" };
        for (int i = 0; i < names.Length; i++)
        {
            int layer = LayerMask.NameToLayer(names[i]);
            if (layer >= 0)
                mask |= 1 << layer;
        }

        // force-include whatever layers current terrain objects actually use
        Terrain[] terrains = FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain t = terrains[i];
            if (t == null)
                continue;

            int terrainLayer = t.gameObject.layer;
            if (terrainLayer >= 0 && terrainLayer < 32)
                mask |= 1 << terrainLayer;
        }

        if (Terrain.activeTerrain != null)
        {
            int activeLayer = Terrain.activeTerrain.gameObject.layer;
            if (activeLayer >= 0 && activeLayer < 32)
                mask |= 1 << activeLayer;
        }

        return mask;
    }

    private void RefreshFullMapImageSource()
    {
        if (fullMapImage == null)
            return;

        // if full-map image already has a render texture, bind full-map camera to it.
        RenderTexture fullImageRt = fullMapImage.texture as RenderTexture;
        if (fullMapCamera != null && fullImageRt != null)
        {
            if (fullMapCamera.targetTexture != fullImageRt)
                fullMapCamera.targetTexture = fullImageRt;
            fullMapImage.texture = fullImageRt;
            return;
        }

        Camera sourceCamera = fullMapCamera != null ? fullMapCamera : minimapCamera;
        if (sourceCamera != null && sourceCamera.targetTexture != null)
        {
            fullMapImage.texture = sourceCamera.targetTexture;
            return;
        }

        // fallback: create a dedicated runtime texture for full-map camera to avoid reuse artifacts.
        if (fullMapCamera != null)
        {
            int width = 1024;
            int height = 1024;

            RenderTexture miniRt = miniMapImage != null ? miniMapImage.texture as RenderTexture : null;
            if (miniRt != null)
            {
                width = Mathf.Max(256, miniRt.width);
                height = Mathf.Max(256, miniRt.height);
            }

            if (runtimeFullMapTexture == null || runtimeFullMapTexture.width != width || runtimeFullMapTexture.height != height)
            {
                if (runtimeFullMapTexture != null)
                    Destroy(runtimeFullMapTexture);

                runtimeFullMapTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
                {
                    name = "Runtime_FullMap_RT",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                runtimeFullMapTexture.Create();
            }

            fullMapCamera.targetTexture = runtimeFullMapTexture;
            fullMapImage.texture = runtimeFullMapTexture;
            return;
        }

        // final fallback to minimap texture
        if (miniMapImage != null && miniMapImage.texture != null)
            fullMapImage.texture = miniMapImage.texture;
    }

    private void TickFogReveal()
    {
        if (!enableFogOfWar || fogTexture == null)
            return;

        revealTimer += Time.deltaTime;
        if (revealTimer < revealTickInterval)
            return;

        revealTimer = 0f;

        if (shareFogRevealAcrossPlayers)
        {
            IReadOnlyList<PlayerStats> players = PlayerRegistry.All;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerStats stats = players[i];
                if (stats == null || !stats.gameObject.activeInHierarchy)
                    continue;

                RevealAt(stats.transform.position);
            }
            return;
        }

        if (localPlayer != null)
            RevealAt(localPlayer.position);
    }

    private void HandleFullMapPanZoom(Camera cam)
    {
        if (cam == null || !cam.orthographic)
            return;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.001f)
        {
            float next = cam.orthographicSize - scroll * fullMapZoomSpeed * Time.unscaledDeltaTime;
            cam.orthographicSize = Mathf.Clamp(next, fullMapMinOrthographicSize, fullMapMaxOrthographicSize);
        }

        if (Input.GetKey(fullMapPanMouseButton))
        {
            float dx = -Input.GetAxisRaw("Mouse X");
            float dy = -Input.GetAxisRaw("Mouse Y");
            Vector3 move = new Vector3(dx, 0f, dy) * fullMapPanSpeed * cam.orthographicSize * 0.02f;
            cam.transform.position += move;
            ClampFullMapCameraToBounds(cam);
        }
    }

    private void ClampFullMapCameraToBounds(Camera cam)
    {
        if (cam == null || !hasWorldBounds)
            return;

        float halfZ = cam.orthographicSize;
        float halfX = cam.orthographicSize * Mathf.Max(0.01f, cam.aspect);

        float minX = worldBounds.min.x + halfX;
        float maxX = worldBounds.max.x - halfX;
        float minZ = worldBounds.min.z + halfZ;
        float maxZ = worldBounds.max.z - halfZ;

        Vector3 p = cam.transform.position;
        p.x = minX > maxX ? worldBounds.center.x : Mathf.Clamp(p.x, minX, maxX);
        p.z = minZ > maxZ ? worldBounds.center.z : Mathf.Clamp(p.z, minZ, maxZ);
        cam.transform.position = p;
    }

    private void InitializeFog()
    {
        if (!enableFogOfWar)
        {
            if (miniFogImage != null) miniFogImage.gameObject.SetActive(false);
            if (fullFogImage != null) fullFogImage.gameObject.SetActive(false);
            return;
        }

        int size = Mathf.Clamp(fogTextureSize, 64, 2048);
        fogTexture = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        fogPixels = new Color32[size * size];
        Color32 fog = new Color32(255, 255, 255, 255);
        for (int i = 0; i < fogPixels.Length; i++)
            fogPixels[i] = fog;

        fogTexture.SetPixels32(fogPixels);
        fogTexture.Apply(false, false);

        if (miniFogImage != null)
        {
            // top-right minimap should not show fog
            miniFogImage.gameObject.SetActive(false);
        }

        if (fullFogImage != null)
        {
            fullFogImage.texture = fogTexture;
            fullFogImage.color = fullMapFogTint;
            fullFogImage.gameObject.SetActive(showFogVisualOnFullMap);
        }
    }

    private static void DetachCameraFromParent(Camera cam)
    {
        if (cam == null)
            return;
        Transform t = cam.transform;
        if (t.parent == null)
            return;

        t.SetParent(null, true);
    }

    private bool IsRevealedAt(Vector2 uv)
    {
        if (fogPixels == null || fogTexture == null)
            return true;

        int texSize = fogTexture.width;
        int x = Mathf.Clamp(Mathf.RoundToInt(uv.x * (texSize - 1)), 0, texSize - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(uv.y * (texSize - 1)), 0, texSize - 1);
        int idx = y * texSize + x;
        if (idx < 0 || idx >= fogPixels.Length)
            return true;

        // alpha 1 = fully hidden, 0 = fully revealed
        float hidden01 = fogPixels[idx].a / 255f;
        float revealed01 = 1f - hidden01;
        return revealed01 >= iconRevealThreshold;
    }

    private void RevealAt(Vector3 worldPosition)
    {
        if (fogTexture == null || fogPixels == null || !hasWorldBounds)
            return;

        if (!TryWorldToNormalized(worldPosition, out Vector2 uv))
            return;

        int texSize = fogTexture.width;
        int cx = Mathf.RoundToInt(uv.x * (texSize - 1));
        int cy = Mathf.RoundToInt(uv.y * (texSize - 1));

        float radiusPxX = (revealRadiusWorld / Mathf.Max(1f, worldBounds.size.x)) * texSize;
        float radiusPxY = (revealRadiusWorld / Mathf.Max(1f, worldBounds.size.z)) * texSize;
        int rx = Mathf.Max(1, Mathf.CeilToInt(radiusPxX));
        int ry = Mathf.Max(1, Mathf.CeilToInt(radiusPxY));

        int minX = Mathf.Max(0, cx - rx);
        int maxX = Mathf.Min(texSize - 1, cx + rx);
        int minY = Mathf.Max(0, cy - ry);
        int maxY = Mathf.Min(texSize - 1, cy + ry);

        bool changed = false;

        for (int y = minY; y <= maxY; y++)
        {
            float ny = (y - cy) / (float)ry;
            for (int x = minX; x <= maxX; x++)
            {
                float nx = (x - cx) / (float)rx;
                float dist = Mathf.Sqrt(nx * nx + ny * ny);
                if (dist > 1f)
                    continue;

                float alpha01 = Mathf.Clamp01((dist - revealSoftness) / Mathf.Max(0.001f, 1f - revealSoftness));
                byte desiredAlpha = (byte)Mathf.RoundToInt(alpha01 * 255f);

                int idx = y * texSize + x;
                if (desiredAlpha < fogPixels[idx].a)
                {
                    Color32 c = fogPixels[idx];
                    c.a = desiredAlpha;
                    fogPixels[idx] = c;
                    changed = true;
                }
            }
        }

        if (!changed)
            return;

        fogTexture.SetPixels32(fogPixels);
        fogTexture.Apply(false, false);
    }

    private bool TryWorldToNormalized(Vector3 worldPosition, out Vector2 uv)
    {
        uv = Vector2.zero;

        if (!hasWorldBounds)
            return false;

        float minX = worldBounds.min.x;
        float maxX = worldBounds.max.x;
        float minZ = worldBounds.min.z;
        float maxZ = worldBounds.max.z;

        if (maxX - minX <= 0.001f || maxZ - minZ <= 0.001f)
            return false;

        float u = Mathf.InverseLerp(minX, maxX, worldPosition.x);
        float v = Mathf.InverseLerp(minZ, maxZ, worldPosition.z);

        if (u < 0f || u > 1f || v < 0f || v > 1f)
            return false;

        uv = new Vector2(u, v);
        return true;
    }

    private bool SetMarkerPositionFromCamera(RectTransform marker, Camera cam, RectTransform container, Vector3 worldPos, bool clampToEdges = false)
    {
        if (marker == null || cam == null || container == null)
            return false;

        Vector3 viewport = cam.WorldToViewportPoint(worldPos);
        if (viewport.z <= 0f)
            return false;

        if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
        {
            if (!clampToEdges)
                return false;

            viewport.x = Mathf.Clamp01(viewport.x);
            viewport.y = Mathf.Clamp01(viewport.y);
        }

        Vector2 uv = new Vector2(viewport.x, viewport.y);
        Rect rect = container.rect;
        marker.anchoredPosition = new Vector2((uv.x - 0.5f) * rect.width, (uv.y - 0.5f) * rect.height);
        return true;
    }

    private void SetMarkerAnchoredPositionFromUV(RectTransform marker, RectTransform container, Vector2 uv)
    {
        if (marker == null || container == null)
            return;

        Rect rect = container.rect;
        marker.anchoredPosition = new Vector2((uv.x - 0.5f) * rect.width, (uv.y - 0.5f) * rect.height);
    }

    private float GetAutoCategoryMiniSize(AutoCategory category)
    {
        switch (category)
        {
            case AutoCategory.Enemy: return autoEnemyMarkerSizeMini;
            case AutoCategory.Camp: return autoCampMarkerSizeMini;
            case AutoCategory.Plant: return autoPlantMarkerSizeMini;
            case AutoCategory.Rock: return autoRockMarkerSizeMini;
            case AutoCategory.Remnant: return autoRemnantMarkerSizeMini;
            case AutoCategory.Spawn: return autoSpawnMarkerSizeMini;
            default: return autoSceneMarkerSizeMini;
        }
    }

    private float GetAutoCategoryFullSize(AutoCategory category)
    {
        switch (category)
        {
            case AutoCategory.Enemy: return autoEnemyMarkerSizeFull;
            case AutoCategory.Camp: return autoCampMarkerSizeFull;
            case AutoCategory.Plant: return autoPlantMarkerSizeFull;
            case AutoCategory.Rock: return autoRockMarkerSizeFull;
            case AutoCategory.Remnant: return autoRemnantMarkerSizeFull;
            case AutoCategory.Spawn: return autoSpawnMarkerSizeFull;
            default: return autoSceneMarkerSizeFull;
        }
    }

    private MarkerPair CreateMarkerPair(Sprite sprite, Color color, float miniSize, float fullSize)
    {
        MarkerPair pair = new MarkerPair();
        pair.mini = CreateMarkerTransform(GetMiniMarkerRect(), out pair.miniImage);
        pair.full = CreateMarkerTransform(GetFullMarkerRect(), out pair.fullImage);

        Sprite resolvedSprite = sprite != null ? sprite : GetFallbackWorldSprite();

        if (pair.miniImage != null)
        {
            pair.miniImage.sprite = resolvedSprite;
            pair.miniImage.color = color;
        }

        if (pair.fullImage != null)
        {
            pair.fullImage.sprite = resolvedSprite;
            pair.fullImage.color = color;
        }

        ApplyMarkerSize(pair, miniSize, fullSize);
        return pair;
    }

    private static Font GetDefaultUiFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
            return font;

        // fallback for older/newer editor variants
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private void EnsurePlayerNameLabels(MarkerPair pair)
    {
        if (pair == null)
            return;

        if (pair.full != null && pair.fullLabel == null)
            pair.fullLabel = CreatePlayerLabel(pair.full, playerNameFontSizeFull, playerNameLabelOffsetFull);
    }

    private Text CreatePlayerLabel(RectTransform marker, int fontSize, Vector2 offset)
    {
        GameObject go = new GameObject("PlayerName", typeof(RectTransform), typeof(Text));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(marker, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = offset;
        rt.sizeDelta = new Vector2(140f, 24f);

        Text text = go.GetComponent<Text>();
        text.font = GetDefaultUiFont();
        text.fontSize = fontSize;
        text.alignment = TextAnchor.UpperCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.color = playerNameLabelColor;
        return text;
    }

    private string ResolvePlayerDisplayName(PlayerStats stats, PhotonView pv, int id)
    {
        if (pv != null && pv.Owner != null)
        {
            string nick = pv.Owner.NickName;
            if (!string.IsNullOrWhiteSpace(nick))
                return nick;
            if (!string.IsNullOrWhiteSpace(pv.Owner.UserId))
                return pv.Owner.UserId;
            return "Player " + pv.Owner.ActorNumber;
        }

        if (stats != null)
            return stats.gameObject.name;

        return "Player " + id;
    }

    private void UpdatePlayerNameLabels(MarkerPair pair, string label)
    {
        if (pair == null)
            return;

        if (pair.fullLabel != null)
        {
            pair.fullLabel.gameObject.SetActive(true);
            pair.fullLabel.text = label;
            pair.fullLabel.fontSize = playerNameFontSizeFull;
            pair.fullLabel.color = playerNameLabelColor;
            RectTransform rt = pair.fullLabel.rectTransform;
            rt.anchoredPosition = playerNameLabelOffsetFull;
            rt.localEulerAngles = Vector3.zero;
        }
    }

    private RectTransform GetMiniMarkerRect()
    {
        if (useMapImageRectsForMarkers && miniMapImage != null)
            return miniMapImage.rectTransform;
        return miniMarkerContainer;
    }

    private RectTransform GetFullMarkerRect()
    {
        if (useMapImageRectsForMarkers && fullMapImage != null)
            return fullMapImage.rectTransform;
        return fullMarkerContainer;
    }

    private static void EnsureMarkerParent(RectTransform marker, RectTransform parent)
    {
        if (marker == null || parent == null)
            return;
        if (marker.parent == parent)
            return;

        marker.SetParent(parent, false);
        marker.anchorMin = new Vector2(0.5f, 0.5f);
        marker.anchorMax = new Vector2(0.5f, 0.5f);
        marker.pivot = new Vector2(0.5f, 0.5f);
    }

    private RectTransform CreateMarkerTransform(RectTransform parent, out Image image)
    {
        image = null;
        if (parent == null)
            return null;

        RectTransform instance;
        if (markerPrefab != null)
        {
            instance = Instantiate(markerPrefab, parent);
            image = instance.GetComponent<Image>();
            if (image == null)
                image = instance.gameObject.AddComponent<Image>();
        }
        else
        {
            GameObject go = new GameObject("MinimapMarker", typeof(RectTransform), typeof(Image));
            instance = go.GetComponent<RectTransform>();
            instance.SetParent(parent, false);
            image = go.GetComponent<Image>();
        }

        instance.anchorMin = new Vector2(0.5f, 0.5f);
        instance.anchorMax = new Vector2(0.5f, 0.5f);
        instance.pivot = new Vector2(0.5f, 0.5f);
        return instance;
    }

    private void ApplyMarkerSize(MarkerPair pair, float miniSize, float fullSize)
    {
        Vector2 mini = new Vector2(miniSize, miniSize);
        Vector2 full = new Vector2(fullSize, fullSize);
        if (pair.mini != null) pair.mini.sizeDelta = mini;
        if (pair.full != null) pair.full.sizeDelta = full;
    }

    private void SetMarkerActive(MarkerPair pair, bool active)
    {
        SetMiniMarkerActive(pair, active);
        SetFullMarkerActive(pair, active);
    }

    private static void SetMiniMarkerActive(MarkerPair pair, bool active)
    {
        if (pair.mini != null && pair.mini.gameObject.activeSelf != active)
            pair.mini.gameObject.SetActive(active);
    }

    private static void SetFullMarkerActive(MarkerPair pair, bool active)
    {
        if (pair.full != null && pair.full.gameObject.activeSelf != active)
            pair.full.gameObject.SetActive(active);
    }

    private void DestroyMarkerPair(MarkerPair pair)
    {
        if (pair == null)
            return;

        if (pair.mini != null)
            Destroy(pair.mini.gameObject);
        if (pair.full != null)
            Destroy(pair.full.gameObject);
    }

    private void ClearAllMarkers()
    {
        foreach (KeyValuePair<int, MarkerPair> kv in playerMarkers)
            DestroyMarkerPair(kv.Value);
        playerMarkers.Clear();

        foreach (KeyValuePair<MinimapIcon, MarkerPair> kv in worldMarkers)
            DestroyMarkerPair(kv.Value);
        worldMarkers.Clear();

        ClearAutoSceneMarkers();
    }

    private void ClearAutoSceneMarkers()
    {
        foreach (KeyValuePair<Transform, MarkerPair> kv in autoSceneMarkers)
            DestroyMarkerPair(kv.Value);
        autoSceneMarkers.Clear();
    }

    private static int GetPlayerMarkerId(PlayerStats stats, PhotonView pv)
    {
        if (pv != null && pv.OwnerActorNr > 0)
            return pv.OwnerActorNr;

        return stats != null ? stats.GetInstanceID() : Random.Range(int.MinValue, int.MaxValue);
    }

    private void EnsureFallbackSprites()
    {
        if (defaultPlayerSprite == null)
            runtimeFallbackPlayerSprite = CreateTriangleSprite(32);
        if (defaultWorldSprite == null)
            runtimeFallbackWorldSprite = CreateCircleSprite(32);
    }

    private Sprite GetFallbackPlayerSprite()
    {
        if (defaultPlayerSprite != null)
            return defaultPlayerSprite;
        if (runtimeFallbackPlayerSprite == null)
            runtimeFallbackPlayerSprite = CreateTriangleSprite(32);
        return runtimeFallbackPlayerSprite;
    }

    private Sprite GetFallbackWorldSprite()
    {
        if (defaultWorldSprite != null)
            return defaultWorldSprite;
        if (runtimeFallbackWorldSprite == null)
            runtimeFallbackWorldSprite = CreateCircleSprite(32);
        return runtimeFallbackWorldSprite;
    }

    private static Sprite CreateCircleSprite(int size)
    {
        int s = Mathf.Clamp(size, 8, 128);
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "MinimapFallbackCircle"
        };

        Color32 clear = new Color32(255, 255, 255, 0);
        Color32 fill = new Color32(255, 255, 255, 255);
        Vector2 c = new Vector2((s - 1) * 0.5f, (s - 1) * 0.5f);
        float r = (s - 1) * 0.45f;
        float rSq = r * r;

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float dx = x - c.x;
                float dy = y - c.y;
                tex.SetPixel(x, y, dx * dx + dy * dy <= rSq ? fill : clear);
            }
        }

        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite CreateTriangleSprite(int size)
    {
        int s = Mathf.Clamp(size, 8, 128);
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            name = "MinimapFallbackTriangle"
        };

        Color32 clear = new Color32(255, 255, 255, 0);
        Color32 fill = new Color32(255, 255, 255, 255);

        Vector2 a = new Vector2(s * 0.5f, s * 0.9f);
        Vector2 b = new Vector2(s * 0.12f, s * 0.12f);
        Vector2 c = new Vector2(s * 0.88f, s * 0.12f);

        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                tex.SetPixel(x, y, PointInTriangle(p, a, b, c) ? fill : clear);
            }
        }

        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float s1 = Sign(p, a, b);
        float s2 = Sign(p, b, c);
        float s3 = Sign(p, c, a);
        bool hasNeg = (s1 < 0f) || (s2 < 0f) || (s3 < 0f);
        bool hasPos = (s1 > 0f) || (s2 > 0f) || (s3 > 0f);
        return !(hasNeg && hasPos);
    }

    private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }
}
