using UnityEngine;
using UnityEngine.SceneManagement;

public class PrologueManager : MonoBehaviour
{
    [Header("Scene Settings")]
    public string mainGameScene = "MAIN";
    public float transitionDelay = 2f;
    
    [Header("Tutorial Messages")]
    public string[] tutorialMessages = new string[]
    {
        "Welcome to AlbuRIOT! Press SPACE to begin the tutorial.",
        "Use WASD keys to move around. Try moving now!",
        "Move your mouse to look around and explore the area.",
        "Press SPACE to jump. Try jumping to reach higher areas!",
        "Great! You've learned the basics. Press SPACE to continue to the main game."
    };
    
    [Header("UI References")]
    public GameObject tutorialPanel;
    public TMPro.TextMeshProUGUI tutorialText;
    
    // Movement removed for clean slate
    
    void Start()
    {
        // Movement hookup removed; tutorial messages can be wired when new movement exists
        
        // Show initial tutorial message
        if (tutorialText != null && tutorialMessages.Length > 0)
        {
            tutorialText.text = tutorialMessages[0];
        }
    }
    
    // Called when tutorial is completed
    public void OnTutorialComplete()
    {
        Debug.Log("Prologue tutorial completed! Transitioning to main game...");
        
        // Show completion message
        if (tutorialText != null)
        {
            tutorialText.text = "Tutorial completed! Loading main game...";
        }
        
        // Transition to main game after delay
        Invoke("LoadMainGame", transitionDelay);
    }
    
    void LoadMainGame()
    {
    Photon.Pun.PhotonNetwork.LoadLevel(mainGameScene);
    }
    
    // Public method to skip prologue
    public void SkipPrologue()
    {
    LoadMainGame(); // Already uses PhotonNetwork.LoadLevel
    }
}
