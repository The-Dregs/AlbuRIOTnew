using UnityEngine;
using UnityEngine.SceneManagement;
// Explicitly reference PlayerSpawnManager static class

public class ProloguePauseMenu : MonoBehaviour
{
    public GameObject pauseMenuUI;
    public string mainMenuSceneName = "MainMenu";
    public string mainSceneName = "MAIN";
    [Header("Scene Shortcuts")]
    public string testingSceneName = "TESTING";
    public string firstMapSceneName = "FIRSTMAP";
    public Transform player;
    public Vector3 mainSceneSpawnPosition = new Vector3(0, 2, 0); // Set as needed

    private bool isPaused = false;

    // Deprecated input handling moved to PauseMenuController to avoid conflicts.
    // This script now exposes only button methods and no longer listens for Escape each frame.

    public void Resume()
    {
        if (pauseMenuUI != null) pauseMenuUI.SetActive(false);
        Time.timeScale = 1f;
        isPaused = false;
        if (LocalUIManager.Instance != null && LocalUIManager.Instance.IsOwner("PauseMenu"))
            LocalUIManager.Instance.Close("PauseMenu");
        if (LocalInputLocker.Instance != null)
            LocalInputLocker.Instance.ReleaseAllForOwner("PauseMenu");
    }

    void Pause()
    {
        if (pauseMenuUI != null) pauseMenuUI.SetActive(true);
        Time.timeScale = 0f;
        isPaused = true;
        LocalUIManager.Ensure().TryOpen("PauseMenu");
        if (LocalInputLocker.Instance != null)
            LocalInputLocker.Instance.Acquire("PauseMenu", lockMovement:true, lockCombat:true, lockCamera:true, cursorUnlock:true);
    }

    public void LoadMainMenu()
    {
        Time.timeScale = 1f;
        // ensure menu has a visible cursor
        if (LocalUIManager.Instance != null) LocalUIManager.Instance.ForceClose();
        LocalInputLocker.Ensure().EnterMenuMode();
        Photon.Pun.PhotonNetwork.LoadLevel(mainMenuSceneName);
    }

    public void TeleportToMainScene()
    {
    // Store the desired spawn position for the MAIN scene
    PlayerSpawnManager.nextSpawnPosition = mainSceneSpawnPosition;
    Time.timeScale = 1f;
        // gameplay scene: keep gameplay cursor lock handled by controller on spawn
        if (LocalUIManager.Instance != null) LocalUIManager.Instance.ForceClose();
        if (LocalInputLocker.Instance != null) LocalInputLocker.Instance.ReleaseAllForOwner("PauseMenu");
        Photon.Pun.PhotonNetwork.LoadLevel(mainSceneName);
    }

    public void QuitGame()
    {
        Application.Quit();
    }

    public void LoadTesting()
    {
        // gameplay scene: let the controller lock the cursor again after spawn
        Time.timeScale = 1f;
        if (LocalUIManager.Instance != null) LocalUIManager.Instance.ForceClose();
        if (LocalInputLocker.Instance != null) LocalInputLocker.Instance.ReleaseAllForOwner("PauseMenu");
        Photon.Pun.PhotonNetwork.LoadLevel(testingSceneName);
    }

    public void LoadFirstMap()
    {
        Time.timeScale = 1f;
        if (LocalUIManager.Instance != null) LocalUIManager.Instance.ForceClose();
        if (LocalInputLocker.Instance != null) LocalInputLocker.Instance.ReleaseAllForOwner("PauseMenu");
        Photon.Pun.PhotonNetwork.LoadLevel(firstMapSceneName);
    }
}
