using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class HomeScreenMediaController : MonoBehaviour
{
    [Header("Video Background")]
    public VideoPlayer videoPlayer;      // Assign your VideoPlayer component
    public RawImage videoImage;          // Assign your RawImage for display
    public VideoClip backgroundClip;     // Assign your VideoClip in Inspector

    [Header("Background Music")]
    public AudioSource audioSource;      // Assign your AudioSource component
    public AudioClip backgroundMusic;    // Assign your AudioClip in Inspector

    void Start()
    {
        // Setup and play video
        if (videoPlayer != null && backgroundClip != null)
        {
            videoPlayer.clip = backgroundClip;
            videoPlayer.isLooping = true;
            videoPlayer.Play();

            // Assign video texture to RawImage after preparation
            videoPlayer.prepareCompleted += (vp) =>
            {
                if (videoImage != null)
                    videoImage.texture = vp.texture;
            };
            videoPlayer.Prepare();
        }

        // Setup and play music
        if (audioSource != null && backgroundMusic != null)
        {
            audioSource.clip = backgroundMusic;
            audioSource.loop = true;
            audioSource.Play();
        }
    }
}