using UnityEngine;
using TMPro;
using System.Text;

/// <summary>
/// Displays real-time game timing info in a TextMeshProUGUI: paused state, timeScale,
/// scaled/unscaled time, delta times, FPS (based on unscaledDeltaTime), frame count, and fixed step.
/// Attach this to any GameObject and assign a TMP text (or let it auto-find one in children).
/// </summary>
public class GameTimeDebug : MonoBehaviour
{
    [Header("Target Text")]
    public TextMeshProUGUI targetText;
    [Tooltip("If enabled and no target is assigned, will search children for a TextMeshProUGUI")] public bool autoFindInChildren = true;

    [Header("Update Settings")]
    [Tooltip("How often to refresh the text (in seconds, unscaled time)")]
    [Range(0.05f, 1f)] public float updateInterval = 0.2f;
    [Tooltip("Smoothing factor for FPS (0 = no smoothing, 1 = instant)")]
    [Range(0f, 1f)] public float fpsSmoothing = 0.1f;

    private float _timer;
    private float _fps;

    void Awake()
    {
        if (autoFindInChildren && targetText == null)
        {
            targetText = GetComponentInChildren<TextMeshProUGUI>(true);
        }
    }

    void Update()
    {
        float unscaledDelta = Time.unscaledDeltaTime;
        // Update smoothed FPS based on unscaled delta so we get a value while paused animations/UI still tick
        float currentFps = (unscaledDelta > 1e-6f) ? (1f / unscaledDelta) : 0f;
        if (_fps <= 0f) _fps = currentFps; else _fps = Mathf.Lerp(_fps, currentFps, Mathf.Clamp01(fpsSmoothing));

        _timer += unscaledDelta;
        if (_timer >= Mathf.Max(0.01f, updateInterval))
        {
            UpdateText();
            _timer = 0f;
        }
    }

    public void ForceUpdate()
    {
        UpdateText();
    }

    private void UpdateText()
    {
        if (targetText == null) return;

        bool paused = Mathf.Approximately(Time.timeScale, 0f);
        var sb = new StringBuilder(256);
        sb.Append("Paused: ").Append(paused)
          .Append("    timeScale: ").Append(Time.timeScale.ToString("0.00"));
        sb.AppendLine();
        sb.Append("GameTime: ").Append(Time.time.ToString("0.00"))
          .Append("    Unscaled: ").Append(Time.unscaledTime.ToString("0.00"));
        sb.AppendLine();
        sb.Append("Delta: ").Append((Time.deltaTime * 1000f).ToString("0.0")).Append(" ms")
          .Append("    UnscaledDelta: ").Append((Time.unscaledDeltaTime * 1000f).ToString("0.0")).Append(" ms");
        sb.AppendLine();
        sb.Append("FPS (unscaled): ").Append(_fps.ToString("0."))
          .Append("    FrameCount: ").Append(Time.frameCount);
        sb.AppendLine();
        sb.Append("FixedDelta: ").Append(Time.fixedDeltaTime.ToString("0.000"))
          .Append("    FixedTime: ").Append(Time.fixedTime.ToString("0.00"));

        targetText.text = sb.ToString();
    }
}
