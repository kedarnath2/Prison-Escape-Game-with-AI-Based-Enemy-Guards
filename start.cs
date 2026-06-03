using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem; // Import the new Input System namespace

public class start : MonoBehaviour
{
    [Header("Menu UI Settings")]
    [Tooltip("Drag the MainMenu panel GameObject here to deactivate it when the game starts.")]
    public GameObject mainMenuPanel;

    [Header("Scene Loading Settings (Optional)")]
    [Tooltip("Enable this if you want to load a new scene (like a gameplay scene) instead of just hiding the menu.")]
    public bool loadSceneOnStart = false;

    [Tooltip("The name of the scene to load when starting the game.")]
    public string gameplaySceneName = "";

    void Update()
    {
        // Detect if the V key is pressed using Unity's new Input System
        if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
        {
            StartGame();
        }
    }

    /// <summary>
    /// Starts the game by either loading a gameplay scene or hiding the main menu panel.
    /// You can also link this method to your UI Button's OnClick() event in the inspector.
    /// </summary>
    public void StartGame()
    {
        if (loadSceneOnStart)
        {
            if (!string.IsNullOrEmpty(gameplaySceneName))
            {
                SceneManager.LoadScene(gameplaySceneName);
            }
            else
            {
                // Fallback: Try to load the next scene in the Build Settings list
                int nextSceneIndex = SceneManager.GetActiveScene().buildIndex + 1;
                if (nextSceneIndex < SceneManager.sceneCountInBuildSettings)
                {
                    SceneManager.LoadScene(nextSceneIndex);
                }
                else
                {
                    Debug.LogWarning("Gameplay scene name is empty and there is no next scene in Build Settings. Disabling menu instead.");
                    DeactivateMenu();
                }
            }
        }
        else
        {
            DeactivateMenu();
        }
    }

    private void DeactivateMenu()
    {
        // Deactivate the main menu UI panel
        if (mainMenuPanel != null)
        {
            mainMenuPanel.SetActive(false);
        }
        else
        {
            // If not assigned, deactivate the GameObject this script is attached to as a fallback
            gameObject.SetActive(false);
        }

        // Lock and hide the cursor for first-person/third-person gameplay
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
