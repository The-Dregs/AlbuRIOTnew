using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

/// <summary>
/// Binds a UI slider (0..1) to an exposed AudioMixer parameter in decibels.
/// Use for global master volume and/or music/sfx sliders.
/// </summary>
public class GlobalAudioVolumeSettings : MonoBehaviour
{
    [Header("Mixer")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private string exposedVolumeParameter = "MasterVolume";

    [Header("UI")]
    [SerializeField] private Slider volumeSlider;

    [Header("Persistence")]
    [SerializeField] private string playerPrefsKey = "MasterVolume01";
    [SerializeField] private float defaultVolume = 1f;

    private const float MinLinearVolume = 0.0001f;

    private void Awake()
    {
        if (volumeSlider == null || audioMixer == null)
        {
            Debug.LogWarning("[GlobalAudioVolumeSettings] Missing slider or audio mixer reference.");
            return;
        }

        float saved = PlayerPrefs.GetFloat(playerPrefsKey, Mathf.Clamp(defaultVolume, MinLinearVolume, 1f));
        volumeSlider.SetValueWithoutNotify(saved);
        ApplyVolume(saved);
        volumeSlider.onValueChanged.AddListener(ApplyVolume);
    }

    private void OnDestroy()
    {
        if (volumeSlider != null)
        {
            volumeSlider.onValueChanged.RemoveListener(ApplyVolume);
        }
    }

    public void ApplyVolume(float linearVolume)
    {
        if (audioMixer == null) return;

        linearVolume = Mathf.Clamp(linearVolume, MinLinearVolume, 1f);
        float decibels = Mathf.Log10(linearVolume) * 20f;
        audioMixer.SetFloat(exposedVolumeParameter, decibels);

        if (!string.IsNullOrWhiteSpace(playerPrefsKey))
        {
            PlayerPrefs.SetFloat(playerPrefsKey, linearVolume);
            PlayerPrefs.Save();
        }
    }
}
