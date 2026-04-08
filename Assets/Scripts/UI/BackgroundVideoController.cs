using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

// controls a background video on a RawImage, creating a render texture at runtime
// minimal dependencies; logs helpful debug messages
public class BackgroundVideoController : MonoBehaviour
{
    [Header("references")]
    public VideoPlayer videoPlayer; // on BACKGROUND VIDEO (or child)
    public RawImage rawImage;       // the RawImage that should display the video

    [Header("options")]
    public bool playOnAwake = true;
    public bool loop = true;
    public bool mute = true;
    public bool setTargetTextureAtRuntime = true;
    public int renderTextureWidth = 1920;
    public int renderTextureHeight = 1080;
    public RenderTextureFormat renderTextureFormat = RenderTextureFormat.ARGB32;

    [Header("fallback")]
    // optional fallback texture to show when video is missing or fails to play
    public Texture2D fallbackTexture;
    // if no fallbackTexture is provided, RawImage will be set to this color
    public Color fallbackColor = Color.black;

    private RenderTexture _rt;

    void Awake()
    {
        if (videoPlayer == null)
        {
            videoPlayer = GetComponentInChildren<VideoPlayer>(true);
        }
        if (rawImage == null)
        {
            rawImage = GetComponent<RawImage>();
            if (rawImage == null)
                rawImage = GetComponentInChildren<RawImage>(true);
        }

        if (videoPlayer == null || rawImage == null)
        {
            Debug.LogWarning("[BackgroundVideo] missing references. assign VideoPlayer and RawImage in inspector.");
            return;
        }

        videoPlayer.playOnAwake = false; // we control manually
        videoPlayer.isLooping = loop;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        videoPlayer.SetDirectAudioMute(0, mute);

        if (setTargetTextureAtRuntime)
        {
            TryCreateAndBindRenderTexture();
        }

        // prepare and optionally autoplay
        videoPlayer.errorReceived += OnVideoError;
        videoPlayer.prepareCompleted += OnVideoPrepared;
        StartCoroutine(PrepareAndMaybePlay());
    }

    IEnumerator PrepareAndMaybePlay()
    {
        if (videoPlayer == null) yield break;
        if (!videoPlayer.isPrepared)
        {
            Debug.Log("[BackgroundVideo] preparing video...");
            videoPlayer.Prepare();
            while (!videoPlayer.isPrepared)
            {
                yield return null;
            }
        }
        if (rawImage != null)
        {
            if (videoPlayer.targetTexture != null)
            {
                rawImage.texture = videoPlayer.targetTexture;
            }
            else if (videoPlayer.texture != null)
            {
                rawImage.texture = videoPlayer.texture;
            }
            // if still no texture assigned, use fallback
            if (rawImage.texture == null)
            {
                AssignFallback();
            }
        }

        if (playOnAwake)
        {
            TryPlay();
        }
    }

    void TryCreateAndBindRenderTexture()
    {
        if (_rt != null)
        {
            if (!ReferenceEquals(videoPlayer.targetTexture, _rt))
                videoPlayer.targetTexture = _rt;
            if (!ReferenceEquals(rawImage.texture, _rt))
                rawImage.texture = _rt;
            return;
        }

        int w = renderTextureWidth > 0 ? renderTextureWidth : 1920;
        int h = renderTextureHeight > 0 ? renderTextureHeight : 1080;
        _rt = new RenderTexture(w, h, 0, renderTextureFormat)
        {
            name = "BG_Video_RT",
            useMipMap = false,
            autoGenerateMips = false,
            antiAliasing = 1
        };
        _rt.Create();

        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = _rt;
        rawImage.texture = _rt;

        Debug.Log($"[BackgroundVideo] created RT {w}x{h} format {renderTextureFormat}");
    }

    void TryPlay()
    {
        if (videoPlayer == null) return;
        if (!videoPlayer.isPrepared)
        {
            Debug.Log("[BackgroundVideo] not prepared yet, delaying play...");
            return;
        }
        if (!videoPlayer.isPlaying)
        {
            videoPlayer.Play();
            Debug.Log("[BackgroundVideo] play");
        }
    }

    void OnVideoError(VideoPlayer source, string message)
    {
        Debug.LogError($"[BackgroundVideo] Video error: {message}");
        // fallback: if direct texture available, assign it
        if (rawImage != null && source != null && source.texture != null)
        {
            rawImage.texture = source.texture;
            return;
        }

        // otherwise use configured fallback
        AssignFallback();
    }

    void OnVideoPrepared(VideoPlayer source)
    {
        Debug.Log("[BackgroundVideo] prepared");
        if (rawImage != null)
        {
            if (source.targetTexture != null)
                rawImage.texture = source.targetTexture;
            else if (source.texture != null)
                rawImage.texture = source.texture;
        }
        if (playOnAwake)
        {
            TryPlay();
        }
    }

    void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.errorReceived -= OnVideoError;
            videoPlayer.prepareCompleted -= OnVideoPrepared;
        }
        if (_rt != null)
        {
            if (rawImage != null && ReferenceEquals(rawImage.texture, _rt)) rawImage.texture = null;
            if (videoPlayer != null && ReferenceEquals(videoPlayer.targetTexture, _rt)) videoPlayer.targetTexture = null;
            _rt.Release();
            Destroy(_rt);
        }
    }

    // set the fallback on the RawImage: prefer the texture, otherwise set a solid color
    void AssignFallback()
    {
        if (rawImage == null) return;
        if (fallbackTexture != null)
        {
            rawImage.texture = fallbackTexture;
            rawImage.color = Color.white;
        }
        else
        {
            rawImage.texture = null;
            rawImage.color = fallbackColor;
        }
    }
}
