using System;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public class DayNightCycleManager : MonoBehaviourPunCallbacks, IPunObservable
{
    [Header("Cycle")]
    [Tooltip("Full cycle duration (seconds): midnight -> midnight")]
    [Min(5f)] public float dayDuration = 300f;
    [Tooltip("Portion of the cycle considered daytime (0..1)")]
    [Range(0.1f, 0.9f)] public float dayPhaseRatio = 0.4f;
    [Tooltip("0 = midnight, 0.5 = noon, 1 = midnight")]
    [Range(0f, 1f)] public float timeOfDay = 0.5f;
    [SerializeField] private bool persistAcrossScenes = true;

    [Header("Network Sync")]
    [Tooltip("How quickly clients smooth toward the master's clock")]
    [Range(0.5f, 20f)] public float syncSmoothing = 8f;

    [Header("Lighting")]
    public Light sunLight;
    public Light moonLight;
    [Tooltip("Color of the moon light. Cool blue works well for night.")]
    public Color moonLightColor = new Color(0.7f, 0.8f, 1f);
    [Tooltip("Moon intensity range during night phase.")]
    [Range(0.1f, 1.5f)] public float moonIntensityMin = 0.35f;
    [Range(0.2f, 2f)] public float moonIntensityMax = 0.7f;
    [Tooltip("Ambient intensity multiplier. Lower at night for more contrast.")]
    [Range(0.1f, 1f)] public float ambientIntensityNight = 0.4f;
    public Gradient skyColorGradient;
    public Gradient fogColorGradient;

    [Header("Performance")]
    [Tooltip("How often to apply environment updates (RenderSettings/skybox/VFX). Lower = cheaper CPU. Clock/network sync still runs every frame.")]
    [Min(0.02f)] public float environmentUpdateInterval = 0.1f;
    [Tooltip("Minimum time-of-day delta required to trigger an OnTimeOfDayChanged event. Helps avoid expensive per-frame listeners.")]
    [Range(0f, 0.05f)] public float timeOfDayEventThreshold = 0.001f;

    [Header("Night Fog")]
    [Tooltip("If enabled, increases fog density during dusk/night for stronger atmosphere.")]
    public bool useNightFogDensity = true;
    [Min(0f)] public float dayFogDensity = 0.003f;
    [Min(0f)] public float nightFogDensity = 0.018f;
    [Range(0.1f, 20f)] public float fogDensitySmoothing = 4f;
    [Tooltip("Fog mode used when applying fog density. (Density only affects Exponential/Exp2)")]
    public FogMode fogDensityMode = FogMode.ExponentialSquared;

    [Header("Skybox")]
    [Tooltip("Skybox used for daytime.")]
    public Material daySkybox;
    [Tooltip("Skybox used for nighttime.")]
    public Material nightSkybox;
    [Tooltip("When true, attempts to blend common skybox properties between day and night materials.")]
    public bool smoothSkyboxBlend = true;
    [Tooltip("Use custom panoramic skybox crossfade shader when both skyboxes have _MainTex.")]
    public bool usePanoramicCrossfadeShader = true;

    [Header("VFX")]
    public ParticleSystem nightParticles;
    public ParticleSystem dayParticles;

    [Header("Audio")]
    [Tooltip("Looping ambient clip for daytime.")]
    public AudioClip dayAmbientSFX;
    [Tooltip("Looping ambient clip for nighttime.")]
    public AudioClip nightAmbientSFX;
    [Tooltip("Looping SFX that plays only during Night phase. If unset, nightAmbientSFX is used.")]
    public AudioClip nightLoopSFX;
    [Tooltip("One-shot SFX played when transitioning to night (dusk).")]
    public AudioClip transitionToNightSFX;
    [Tooltip("Volume for ambient loops.")]
    [Range(0f, 1f)] public float ambientVolume = 0.3f;
    [Tooltip("Volume for the night-only looping SFX.")]
    [Range(0f, 1f)] public float nightLoopVolume = 0.3f;
    [Tooltip("How quickly ambient audio crossfades between day/night.")]
    [Range(0.1f, 5f)] public float ambientCrossfadeSpeed = 1.5f;

    public static DayNightCycleManager Instance { get; private set; }

    /// <summary>When true the clock stops advancing (useful during cutscenes).</summary>
    public bool isPaused;

    public enum TimePhase { Dawn, Day, Dusk, Night }
    public TimePhase CurrentPhase { get; private set; }

    /// <summary>Current day number (starts at 1, increments each dawn).</summary>
    public int CurrentDay { get; private set; } = 1;

    public event Action<TimePhase> OnPhaseChanged;
    public event Action<float> OnTimeOfDayChanged;
    /// <summary>Fired when a new day begins. Passes the new day number.</summary>
    public event Action<int> OnNewDay;

    private TimePhase _lastPhase;
    private bool _hasNetworkBaseline;
    private float _networkBaselineTimeOfDay;
    private double _networkBaselineTimestamp;
    private Material _runtimeSkybox;
    private Material _runtimePanoramicBlendSkybox;
    private Shader _panoramicBlendShader;

    // audio
    private AudioSource _dayAudioSource;
    private AudioSource _nightAudioSource;
    private AudioSource _transitionAudioSource;
    private bool _transitionSFXPlayed;

    // perf caches
    private float _environmentUpdateTimer;
    private float _environmentDtForLastApply;
    private float _lastTimeOfDayEventValue = -999f;
    private bool _phaseChangedThisFrame;
    private float _lastAppliedAmbientIntensity = float.NaN;
    private Color _lastAppliedAmbientSkyColor = new Color(float.NaN, float.NaN, float.NaN, float.NaN);
    private Color _lastAppliedFogColor = new Color(float.NaN, float.NaN, float.NaN, float.NaN);
    private float _lastAppliedFogDensity = float.NaN;
    private bool _lastAppliedFogEnabled;
    private bool _lastNightLikeForVfx;
    private bool _lastDayLikeForVfx;

    // shader property ids (avoid string lookups)
    private static readonly int PropTexA = Shader.PropertyToID("_TexA");
    private static readonly int PropTexB = Shader.PropertyToID("_TexB");
    private static readonly int PropBlend = Shader.PropertyToID("_Blend");
    private static readonly int PropTintA = Shader.PropertyToID("_TintA");
    private static readonly int PropTintB = Shader.PropertyToID("_TintB");
    private static readonly int PropExposureA = Shader.PropertyToID("_ExposureA");
    private static readonly int PropExposureB = Shader.PropertyToID("_ExposureB");
    private static readonly int PropRotationA = Shader.PropertyToID("_RotationA");
    private static readonly int PropRotationB = Shader.PropertyToID("_RotationB");

    private bool IsAuthority
    {
        get
        {
            if (!PhotonNetwork.IsConnected || PhotonNetwork.OfflineMode) return true;
            if (!PhotonNetwork.InRoom) return true;
            return PhotonNetwork.IsMasterClient;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (persistAcrossScenes) DontDestroyOnLoad(gameObject);

        CurrentPhase = ComputePhase(timeOfDay);
        _lastPhase = CurrentPhase;

        _panoramicBlendShader = usePanoramicCrossfadeShader ? Shader.Find("Custom/SkyboxBlendPanoramic") : null;

        InitAudioSources();

        // seed caches to avoid redundant RenderSettings churn on the first tick
        _lastAppliedAmbientIntensity = RenderSettings.ambientIntensity;
        _lastAppliedAmbientSkyColor = RenderSettings.ambientSkyColor;
        _lastAppliedFogColor = RenderSettings.fogColor;
        _lastAppliedFogDensity = RenderSettings.fogDensity;
        _lastAppliedFogEnabled = RenderSettings.fog;
        _lastNightLikeForVfx = CurrentPhase == TimePhase.Night || CurrentPhase == TimePhase.Dusk;
        _lastDayLikeForVfx = CurrentPhase == TimePhase.Day || CurrentPhase == TimePhase.Dawn;

        // Ensure PhotonView observes this for IPunObservable sync
        var pv = GetComponent<PhotonView>();
        if (pv != null) pv.FindObservables(true);
    }

    private void Start()
    {
        // Force an immediate environment apply on scene start so fog/lighting match the current phase
        // without waiting for the first environmentUpdateInterval tick.
        _lastAppliedFogDensity = float.NaN;
        _lastAppliedFogEnabled = !RenderSettings.fog;
        _environmentDtForLastApply = Mathf.Max(0.02f, environmentUpdateInterval);
        _environmentUpdateTimer = environmentUpdateInterval;
        UpdateLighting();
        UpdateVisuals();
    }

    private void OnDestroy()
    {
        if (_runtimeSkybox != null)
        {
            Destroy(_runtimeSkybox);
            _runtimeSkybox = null;
        }
        if (_runtimePanoramicBlendSkybox != null)
        {
            Destroy(_runtimePanoramicBlendSkybox);
            _runtimePanoramicBlendSkybox = null;
        }
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!isPaused)
        {
            if (IsAuthority)
            {
                AdvanceClock(Time.deltaTime);
            }
            else
            {
                FollowNetworkClock();
            }
        }

        UpdatePhaseAndEvents();

        _environmentUpdateTimer += Time.deltaTime;
        if (_phaseChangedThisFrame || _environmentUpdateTimer >= Mathf.Max(0.02f, environmentUpdateInterval))
        {
            _environmentDtForLastApply = Mathf.Max(Time.deltaTime, _environmentUpdateTimer);
            _environmentUpdateTimer = 0f;
            UpdateLighting();
            UpdateVisuals();
        }

        UpdateAudio();
        TryFireTimeOfDayChanged();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (!IsAuthority || photonView == null || !PhotonNetwork.IsConnected) return;
        photonView.RPC(nameof(RPC_ForceClockSync), newPlayer, timeOfDay, PhotonNetwork.Time);
    }

    [PunRPC]
    private void RPC_ForceClockSync(float syncedTimeOfDay, double networkTimestamp)
    {
        _networkBaselineTimeOfDay = Mathf.Repeat(syncedTimeOfDay, 1f);
        _networkBaselineTimestamp = networkTimestamp;
        _hasNetworkBaseline = true;
        _noBaselineElapsed = 0f;
        timeOfDay = _networkBaselineTimeOfDay;
    }

    private void AdvanceClock(float dt)
    {
        float safeDuration = Mathf.Max(5f, dayDuration);
        timeOfDay = Mathf.Repeat(timeOfDay + (dt / safeDuration), 1f);
    }

    private float _noBaselineElapsed;

    private void FollowNetworkClock()
    {
        if (!_hasNetworkBaseline)
        {
            _noBaselineElapsed += Time.deltaTime;
            if (_noBaselineElapsed > 3f)
            {
                _hasNetworkBaseline = true;
                _networkBaselineTimeOfDay = timeOfDay;
                _networkBaselineTimestamp = PhotonNetwork.Time;
            }
            return;
        }
        _noBaselineElapsed = 0f;

        float safeDuration = Mathf.Max(5f, dayDuration);
        float elapsed = (float)Math.Max(0.0, PhotonNetwork.Time - _networkBaselineTimestamp);
        float predicted = Mathf.Repeat(_networkBaselineTimeOfDay + (elapsed / safeDuration), 1f);
        timeOfDay = LerpWrapped01(timeOfDay, predicted, Mathf.Clamp01(Time.deltaTime * syncSmoothing));
    }

    private void UpdatePhaseAndEvents()
    {
        _phaseChangedThisFrame = false;
        CurrentPhase = ComputePhase(timeOfDay);
        if (CurrentPhase != _lastPhase)
        {
            var previous = _lastPhase;
            _lastPhase = CurrentPhase;
            _phaseChangedThisFrame = true;
            OnPhaseChanged?.Invoke(CurrentPhase);

            // new day when transitioning into dawn
            if (CurrentPhase == TimePhase.Dawn && previous == TimePhase.Night)
            {
                CurrentDay++;
                OnNewDay?.Invoke(CurrentDay);
            }

            // reset transition sfx flag when leaving dusk
            if (CurrentPhase != TimePhase.Dusk)
                _transitionSFXPlayed = false;

            // play transition-to-night sfx once on entering dusk
            if (CurrentPhase == TimePhase.Dusk && !_transitionSFXPlayed)
            {
                _transitionSFXPlayed = true;
                PlayTransitionSFX();
            }
        }
    }

    // computes phase boundaries centered on noon (t=0.5) and midnight (t=0/1)
    // Exposed so UI/other systems can present accurate phase timers.
    public void GetPhaseBoundaries(out float dawnStart, out float dayStart, out float dayEnd, out float duskEnd)
    {
        float dayRatio = Mathf.Clamp01(dayPhaseRatio);
        float nightHalf = (1f - dayRatio) * 0.5f;
        float transW = dayRatio * 0.15f;
        dawnStart = nightHalf;
        dayStart = nightHalf + transW;
        dayEnd = 1f - nightHalf - transW;
        duskEnd = 1f - nightHalf;
    }

    private TimePhase ComputePhase(float t)
    {
        GetPhaseBoundaries(out float dawnStart, out float dayStart, out float dayEnd, out float duskEnd);

        if (t < dawnStart || t >= duskEnd) return TimePhase.Night;
        if (t < dayStart) return TimePhase.Dawn;
        if (t < dayEnd) return TimePhase.Day;
        return TimePhase.Dusk;
    }

    private void UpdateLighting()
    {
        GetPhaseBoundaries(out float dawnStart, out float dayStart, out float dayEnd, out float duskEnd);
        float t = timeOfDay;

        float sunIntensity;
        float moonIntensity;
        // linear sun rotation: t=0 is nadir (midnight), t=0.5 is zenith (noon)
        float sunAngle = t * 360f;

        if (t < dawnStart || t >= duskEnd) // night (wraps around midnight)
        {
            sunIntensity = 0f;
            float nightLen = dawnStart + (1f - duskEnd);
            float nightP;
            if (t >= duskEnd)
                nightP = (t - duskEnd) / Mathf.Max(0.0001f, nightLen);
            else
                nightP = ((1f - duskEnd) + t) / Mathf.Max(0.0001f, nightLen);
            moonIntensity = Mathf.Lerp(moonIntensityMin, moonIntensityMax, Mathf.Sin(nightP * Mathf.PI));
        }
        else if (t < dayStart) // dawn
        {
            float p = (t - dawnStart) / Mathf.Max(0.0001f, dayStart - dawnStart);
            sunIntensity = Mathf.Lerp(0f, 0.5f, p);
            moonIntensity = Mathf.Lerp(moonIntensityMin, 0f, p);
        }
        else if (t < dayEnd) // day
        {
            float p = (t - dayStart) / Mathf.Max(0.0001f, dayEnd - dayStart);
            sunIntensity = Mathf.Lerp(0.5f, 1f, Mathf.Sin(p * Mathf.PI));
            moonIntensity = 0f;
        }
        else // dusk
        {
            float p = (t - dayEnd) / Mathf.Max(0.0001f, duskEnd - dayEnd);
            sunIntensity = Mathf.Lerp(0.5f, 0f, p);
            moonIntensity = Mathf.Lerp(0f, moonIntensityMin, p);
        }

        if (sunLight != null)
        {
            sunLight.intensity = sunIntensity;
            sunLight.transform.rotation = Quaternion.Euler(sunAngle, 0f, 0f);
        }

        if (moonLight != null)
        {
            moonLight.intensity = moonIntensity;
            moonLight.color = moonLightColor;
            moonLight.transform.rotation = Quaternion.Euler(sunAngle + 180f, 0f, 0f);
        }

        if (skyColorGradient != null)
        {
            Color target = skyColorGradient.Evaluate(t);
            if (!Approximately(_lastAppliedAmbientSkyColor, target))
            {
                RenderSettings.ambientSkyColor = target;
                _lastAppliedAmbientSkyColor = target;
            }
        }

        float nightWeight = ComputeNightWeight(t, dawnStart, dayStart, dayEnd, duskEnd);
        float ambientMult = Mathf.Lerp(1f, ambientIntensityNight, nightWeight);
        if (!Approximately(_lastAppliedAmbientIntensity, ambientMult))
        {
            RenderSettings.ambientIntensity = ambientMult;
            _lastAppliedAmbientIntensity = ambientMult;
        }

        if (fogColorGradient != null)
        {
            Color targetFogColor = fogColorGradient.Evaluate(t);
            if (!Approximately(_lastAppliedFogColor, targetFogColor))
            {
                RenderSettings.fogColor = targetFogColor;
                _lastAppliedFogColor = targetFogColor;
            }
        }

        if (useNightFogDensity)
        {
            bool nightLike = IsNight();
            float targetFogDensity = nightLike ? SanitizeFogDensity(nightFogDensity) : SanitizeFogDensity(dayFogDensity);

            if (RenderSettings.fogMode != fogDensityMode)
                RenderSettings.fogMode = fogDensityMode;

            bool wantFog = targetFogDensity > 0.00001f;
            if (RenderSettings.fog != wantFog)
            {
                RenderSettings.fog = wantFog;
                _lastAppliedFogEnabled = wantFog;
            }

            if (wantFog)
            {
                float current = RenderSettings.fogDensity;
                float dt = _environmentDtForLastApply > 0f ? _environmentDtForLastApply : Time.deltaTime;
                float next = Mathf.Lerp(current, targetFogDensity, Mathf.Clamp01(dt * fogDensitySmoothing));
                if (!Approximately(_lastAppliedFogDensity, next))
                {
                    RenderSettings.fogDensity = next;
                    _lastAppliedFogDensity = next;
                }
            }
        }

        UpdateSkybox(t, dawnStart, dayStart, dayEnd, duskEnd);
    }

    private static float SanitizeFogDensity(float v)
    {
        v = Mathf.Max(0f, v);
        // Users often type "4" or "10" in the inspector expecting "more fog".
        // Fog density in Unity is typically small (e.g. 0.001..0.05).
        if (v > 1f) v *= 0.001f;
        return v;
    }

    private float ComputeNightWeight(float t, float dawnStart, float dayStart, float dayEnd, float duskEnd)
    {
        if (t < dawnStart || t >= duskEnd)
            return 1f; // night
        if (t < dayStart)
        {
            float p = (t - dawnStart) / Mathf.Max(0.0001f, dayStart - dawnStart);
            return 1f - p; // dawn: night -> day
        }
        if (t < dayEnd)
            return 0f; // day
        // dusk: day -> night
        float dp = (t - dayEnd) / Mathf.Max(0.0001f, duskEnd - dayEnd);
        return dp;
    }

    private void UpdateSkybox(float t, float dawnStart, float dayStart, float dayEnd, float duskEnd)
    {
        if (daySkybox == null && nightSkybox == null) return;
        if (daySkybox == null || nightSkybox == null)
        {
            RenderSettings.skybox = daySkybox != null ? daySkybox : nightSkybox;
            return;
        }

        float nightWeight = ComputeNightWeight(t, dawnStart, dayStart, dayEnd, duskEnd);

        if (usePanoramicCrossfadeShader && TryUpdatePanoramicSkyboxBlend(nightWeight))
        {
            return;
        }

        if (!smoothSkyboxBlend || daySkybox.shader != nightSkybox.shader)
        {
            RenderSettings.skybox = nightWeight >= 0.5f ? nightSkybox : daySkybox;
            return;
        }

        if (_runtimeSkybox == null || _runtimeSkybox.shader != daySkybox.shader)
        {
            _runtimeSkybox = new Material(daySkybox);
            _runtimeSkybox.name = "RuntimeSkyboxBlend";
        }

        // Blend common properties if present on shader.
        if (_runtimeSkybox.HasProperty("_Exposure") && daySkybox.HasProperty("_Exposure") && nightSkybox.HasProperty("_Exposure"))
            _runtimeSkybox.SetFloat("_Exposure", Mathf.Lerp(daySkybox.GetFloat("_Exposure"), nightSkybox.GetFloat("_Exposure"), nightWeight));

        if (_runtimeSkybox.HasProperty("_Tint") && daySkybox.HasProperty("_Tint") && nightSkybox.HasProperty("_Tint"))
            _runtimeSkybox.SetColor("_Tint", Color.Lerp(daySkybox.GetColor("_Tint"), nightSkybox.GetColor("_Tint"), nightWeight));

        if (_runtimeSkybox.HasProperty("_SkyTint") && daySkybox.HasProperty("_SkyTint") && nightSkybox.HasProperty("_SkyTint"))
            _runtimeSkybox.SetColor("_SkyTint", Color.Lerp(daySkybox.GetColor("_SkyTint"), nightSkybox.GetColor("_SkyTint"), nightWeight));

        if (_runtimeSkybox.HasProperty("_GroundColor") && daySkybox.HasProperty("_GroundColor") && nightSkybox.HasProperty("_GroundColor"))
            _runtimeSkybox.SetColor("_GroundColor", Color.Lerp(daySkybox.GetColor("_GroundColor"), nightSkybox.GetColor("_GroundColor"), nightWeight));

        // For texture-based skyboxes, switch texture set near midpoint.
        if (_runtimeSkybox.HasProperty("_Tex") && daySkybox.HasProperty("_Tex") && nightSkybox.HasProperty("_Tex"))
            _runtimeSkybox.SetTexture("_Tex", nightWeight >= 0.5f ? nightSkybox.GetTexture("_Tex") : daySkybox.GetTexture("_Tex"));

        if (_runtimeSkybox.HasProperty("_MainTex") && daySkybox.HasProperty("_MainTex") && nightSkybox.HasProperty("_MainTex"))
            _runtimeSkybox.SetTexture("_MainTex", nightWeight >= 0.5f ? nightSkybox.GetTexture("_MainTex") : daySkybox.GetTexture("_MainTex"));

        RenderSettings.skybox = _runtimeSkybox;
    }

    private bool TryUpdatePanoramicSkyboxBlend(float nightWeight)
    {
        if (daySkybox == null || nightSkybox == null) return false;
        if (!daySkybox.HasProperty("_MainTex") || !nightSkybox.HasProperty("_MainTex")) return false;

        Texture texA = daySkybox.GetTexture("_MainTex");
        Texture texB = nightSkybox.GetTexture("_MainTex");
        if (texA == null || texB == null) return false;

        if (_panoramicBlendShader == null) return false;

        if (_runtimePanoramicBlendSkybox == null || _runtimePanoramicBlendSkybox.shader != _panoramicBlendShader)
        {
            _runtimePanoramicBlendSkybox = new Material(_panoramicBlendShader);
            _runtimePanoramicBlendSkybox.name = "RuntimePanoramicSkyboxBlend";
        }

        _runtimePanoramicBlendSkybox.SetTexture(PropTexA, texA);
        _runtimePanoramicBlendSkybox.SetTexture(PropTexB, texB);
        _runtimePanoramicBlendSkybox.SetFloat(PropBlend, Mathf.Clamp01(nightWeight));
        _runtimePanoramicBlendSkybox.SetColor(PropTintA, daySkybox.HasProperty("_Tint") ? daySkybox.GetColor("_Tint") : Color.white);
        _runtimePanoramicBlendSkybox.SetColor(PropTintB, nightSkybox.HasProperty("_Tint") ? nightSkybox.GetColor("_Tint") : Color.white);
        _runtimePanoramicBlendSkybox.SetFloat(PropExposureA, daySkybox.HasProperty("_Exposure") ? daySkybox.GetFloat("_Exposure") : 1f);
        _runtimePanoramicBlendSkybox.SetFloat(PropExposureB, nightSkybox.HasProperty("_Exposure") ? nightSkybox.GetFloat("_Exposure") : 1f);
        _runtimePanoramicBlendSkybox.SetFloat(PropRotationA, daySkybox.HasProperty("_Rotation") ? daySkybox.GetFloat("_Rotation") : 0f);
        _runtimePanoramicBlendSkybox.SetFloat(PropRotationB, nightSkybox.HasProperty("_Rotation") ? nightSkybox.GetFloat("_Rotation") : 0f);

        RenderSettings.skybox = _runtimePanoramicBlendSkybox;
        return true;
    }

    private void UpdateVisuals()
    {
        bool nightLike = CurrentPhase == TimePhase.Night || CurrentPhase == TimePhase.Dusk;
        bool dayLike = CurrentPhase == TimePhase.Day || CurrentPhase == TimePhase.Dawn;

        if (nightParticles != null)
        {
            if (nightLike != _lastNightLikeForVfx)
            {
                var emission = nightParticles.emission;
                emission.enabled = nightLike;
                _lastNightLikeForVfx = nightLike;
            }
        }
        if (dayParticles != null)
        {
            if (dayLike != _lastDayLikeForVfx)
            {
                var emission = dayParticles.emission;
                emission.enabled = dayLike;
                _lastDayLikeForVfx = dayLike;
            }
        }
    }

    public bool IsNight()
    {
        return CurrentPhase == TimePhase.Night || CurrentPhase == TimePhase.Dusk;
    }

    public bool IsDay()
    {
        return CurrentPhase == TimePhase.Day || CurrentPhase == TimePhase.Dawn;
    }

    public float GetTimeUntilNight()
    {
        if (IsNight()) return 0f;
        GetPhaseBoundaries(out _, out _, out _, out float duskEnd);
        float remaining = Mathf.Repeat(duskEnd - timeOfDay, 1f);
        return remaining * Mathf.Max(5f, dayDuration);
    }

    public float GetTimeUntilDay()
    {
        if (IsDay()) return 0f;
        GetPhaseBoundaries(out float dawnStart, out _, out _, out _);
        float remaining = Mathf.Repeat(dawnStart - timeOfDay, 1f);
        return remaining * Mathf.Max(5f, dayDuration);
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(timeOfDay);
            stream.SendNext(PhotonNetwork.Time);
        }
        else
        {
            _networkBaselineTimeOfDay = Mathf.Repeat((float)stream.ReceiveNext(), 1f);
            _networkBaselineTimestamp = (double)stream.ReceiveNext();
            _hasNetworkBaseline = true;
            _noBaselineElapsed = 0f;
        }
    }

    private float LerpWrapped01(float from, float to, float t)
    {
        float delta = Mathf.Repeat((to - from) + 0.5f, 1f) - 0.5f;
        return Mathf.Repeat(from + delta * Mathf.Clamp01(t), 1f);
    }

    private void TryFireTimeOfDayChanged()
    {
        if (OnTimeOfDayChanged == null) return;

        float threshold = Mathf.Max(0f, timeOfDayEventThreshold);
        if (_lastTimeOfDayEventValue < -100f)
        {
            _lastTimeOfDayEventValue = timeOfDay;
            OnTimeOfDayChanged.Invoke(timeOfDay);
            return;
        }

        float delta = Mathf.Abs(Mathf.Repeat((timeOfDay - _lastTimeOfDayEventValue) + 0.5f, 1f) - 0.5f);
        if (delta >= threshold)
        {
            _lastTimeOfDayEventValue = timeOfDay;
            OnTimeOfDayChanged.Invoke(timeOfDay);
        }
    }

    private static bool Approximately(float a, float b)
    {
        return Mathf.Abs(a - b) <= 0.0005f;
    }

    private static bool Approximately(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) <= 0.002f
               && Mathf.Abs(a.g - b.g) <= 0.002f
               && Mathf.Abs(a.b - b.b) <= 0.002f
               && Mathf.Abs(a.a - b.a) <= 0.002f;
    }

    #region Audio

    private void InitAudioSources()
    {
        _dayAudioSource = CreateLoopSource("DayAmbient");
        _nightAudioSource = CreateLoopSource("NightAmbient");
        _transitionAudioSource = gameObject.AddComponent<AudioSource>();
        _transitionAudioSource.playOnAwake = false;
        _transitionAudioSource.spatialBlend = 0f;
    }

    private AudioSource CreateLoopSource(string label)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.volume = 0f;
        return src;
    }

    private void UpdateAudio()
    {
        bool wantDay = IsDay();
        bool wantNight = CurrentPhase == TimePhase.Night;
        float targetDay = wantDay ? ambientVolume : 0f;
        float targetNight = wantNight ? nightLoopVolume : 0f;
        float speed = ambientCrossfadeSpeed * Time.deltaTime;

        if (_dayAudioSource != null)
        {
            SyncClip(_dayAudioSource, dayAmbientSFX);
            _dayAudioSource.volume = Mathf.MoveTowards(_dayAudioSource.volume, targetDay, speed);
        }
        if (_nightAudioSource != null)
        {
            // prefer the dedicated night-only loop clip, with a fallback for existing setups
            AudioClip activeNightClip = nightLoopSFX != null ? nightLoopSFX : nightAmbientSFX;
            if (activeNightClip == null)
            {
                if (_nightAudioSource.isPlaying) _nightAudioSource.Stop();
            }
            else
            {
                if (_nightAudioSource.clip != activeNightClip)
                {
                    _nightAudioSource.Stop();
                    _nightAudioSource.clip = activeNightClip;
                }

                if (wantNight)
                {
                    if (!_nightAudioSource.isPlaying) _nightAudioSource.Play();
                }
                else if (_nightAudioSource.volume <= 0.0001f && _nightAudioSource.isPlaying)
                {
                    _nightAudioSource.Stop();
                }
            }

            _nightAudioSource.volume = Mathf.MoveTowards(_nightAudioSource.volume, targetNight, speed);
        }
    }

    private static void SyncClip(AudioSource src, AudioClip clip)
    {
        if (clip == null)
        {
            if (src.isPlaying) src.Stop();
            return;
        }
        if (src.clip != clip)
        {
            src.clip = clip;
            src.Play();
        }
        else if (!src.isPlaying)
        {
            src.Play();
        }
    }

    private void PlayTransitionSFX()
    {
        if (transitionToNightSFX == null || _transitionAudioSource == null) return;
        _transitionAudioSource.PlayOneShot(transitionToNightSFX, ambientVolume);
    }

    #endregion
}

