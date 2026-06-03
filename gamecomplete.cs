using UnityEngine;
using UnityEngine.SceneManagement;

public class gamecomplete : MonoBehaviour
{
    [Header("Detection Settings")]
    [Tooltip("If checked, calculates the 3D distance between this object and the target to trigger the end game.")]
    public bool useDistanceCheck = true;

    [Tooltip("The range (in meters) within which the game will end.")]
    public float detectionRange = 2.0f;

    [Tooltip("If checked, uses OnTriggerEnter to detect proximity. Requires a Collider with 'Is Trigger' enabled on this object.")]
    public bool useTriggerCollider = true;

    [Tooltip("The tag of the object that triggers the completion screen (usually 'Player').")]
    public string targetTag = "Player";

    [Tooltip("If true, any object with a collider/movement will trigger the completion screen.")]
    public bool triggerByAnyObject = false;

    [Header("UI Message Settings")]
    [Tooltip("The message text displayed on the victory screen.")]
    [TextArea(3, 5)]
    public string victoryMessage = "DIAMOND STOLEN!\n\nGame Complete!";

    [Tooltip("Optional: Drag and drop a UI Panel GameObject from your Canvas here to enable it when game is completed.")]
    public GameObject victoryUIPanel;

    [Header("Game State Control")]
    [Tooltip("Should we pause the game (freeze time) when completed?")]
    public bool pauseGameOnComplete = true;

    [Tooltip("Should we unlock and show the cursor when completed?")]
    public bool unlockCursor = true;

    private Transform targetTransform;
    private bool isCompleted = false;
    private float searchCooldown = 1.0f;
    private float searchTimer = 0.0f;
    private float logTimer = 0.0f;

    void Awake()
    {
        // Automatically add a trigger collider at runtime if useTriggerCollider is enabled but no collider exists
        if (useTriggerCollider)
        {
            Collider col = GetComponent<Collider>();
            if (col == null)
            {
                Debug.LogWarning("[GameComplete] 'Use Trigger Collider' is enabled but no Collider component was found on this GameObject. Automatically adding a SphereCollider.");
                SphereCollider sphere = gameObject.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                
                // Adjust sphere radius based on local scale to keep it matching detectionRange in world units
                float maxScale = Mathf.Max(transform.localScale.x, transform.localScale.y, transform.localScale.z);
                if (maxScale > 0.01f)
                {
                    sphere.radius = detectionRange / maxScale;
                }
                else
                {
                    sphere.radius = detectionRange;
                }
            }
            else if (!col.isTrigger)
            {
                Debug.LogWarning("[GameComplete] A Collider was found but 'Is Trigger' was not checked. Setting it to true.");
                col.isTrigger = true;
            }

            // IMPORTANT: In Unity, for trigger collisions to be detected, at least one of the 
            // colliding objects MUST have a Rigidbody component.
            // Since character controllers or static models might not have one, we add a kinematic
            // Rigidbody to this object so that Unity's physics engine fires OnTriggerEnter.
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                Debug.Log("[GameComplete] Adding a kinematic Rigidbody to this object to enable trigger collision detection.");
                rb = gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }
    }

    void Start()
    {
        FindPlayer();
    }

    void Update()
    {
        if (isCompleted) return;

        // Proximity detection
        if (useDistanceCheck)
        {
            // If player hasn't been found yet, retry searching periodically
            if (targetTransform == null)
            {
                searchTimer += Time.deltaTime;
                if (searchTimer >= searchCooldown)
                {
                    searchTimer = 0f;
                    FindPlayer();
                }
            }

            // Track distance to the player
            if (targetTransform != null)
            {
                float distance = Vector3.Distance(transform.position, targetTransform.position);
                
                // Print a log every 1 second showing the current distance to the player
                logTimer += Time.deltaTime;
                if (logTimer >= 1.0f)
                {
                    Debug.Log($"[GameComplete] Distance to player: {distance:F2}m (Trigger range: {detectionRange}m)");
                    logTimer = 0.0f;
                }

                if (distance <= detectionRange)
                {
                    Debug.Log($"[GameComplete] Player entered range! Distance: {distance:F2}m (Threshold: {detectionRange}m)");
                    CompleteGame();
                }
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (isCompleted) return;

        if (useTriggerCollider)
        {
            // Check if it's the target tag, any object is allowed, or if it is the player controller
            if (triggerByAnyObject || 
                other.CompareTag(targetTag) || 
                other.GetComponent<PlayerMovementController>() != null || 
                other.GetComponent<CharacterController>() != null ||
                other.name.ToLower().Contains("player") ||
                other.name.ToLower().Contains("armature"))
            {
                Debug.Log($"[GameComplete] Trigger collision detected with: {other.name}");
                CompleteGame();
            }
        }
    }

    /// <summary>
    /// Search for the player character in the scene using multiple fallback strategies.
    /// </summary>
    private void FindPlayer()
    {
        // 1. Try finding by tag
        if (!string.IsNullOrEmpty(targetTag))
        {
            try
            {
                GameObject playerObj = GameObject.FindWithTag(targetTag);
                if (playerObj != null)
                {
                    targetTransform = playerObj.transform;
                    Debug.Log($"[GameComplete] Successfully located player by tag '{targetTag}': {targetTransform.name}");
                    return;
                }
            }
            catch (System.Exception) { }
        }

        // 2. Try finding by PlayerMovementController component
        PlayerMovementController pm = FindFirstObjectByType<PlayerMovementController>();
        if (pm != null)
        {
            targetTransform = pm.transform;
            Debug.Log($"[GameComplete] Successfully located player via PlayerMovementController component: {targetTransform.name}");
            return;
        }

        // 3. Try finding by CharacterController component
        CharacterController cc = FindFirstObjectByType<CharacterController>();
        if (cc != null)
        {
            targetTransform = cc.transform;
            Debug.Log($"[GameComplete] Successfully located player via CharacterController component: {targetTransform.name}");
            return;
        }

        // 4. Try finding by name (case-insensitive search for 'player', 'armature', 'thief')
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (GameObject go in allObjects)
        {
            string lowerName = go.name.ToLower();
            if (go != gameObject && (lowerName.Contains("player") || lowerName.Contains("armature") || lowerName.Contains("thief")))
            {
                // Ensure it is a physical entity/character, not a camera or UI group
                if (go.GetComponent<CharacterController>() != null || go.GetComponent<Animator>() != null || go.GetComponent<Collider>() != null)
                {
                    targetTransform = go.transform;
                    Debug.Log($"[GameComplete] Successfully located player by name match: {go.name}");
                    return;
                }
            }
        }
    }

    public void CompleteGame()
    {
        if (isCompleted) return;
        isCompleted = true;

        Debug.Log("[GameComplete] Victory condition met! Freezing game and showing completion screen.");

        // 1. Show custom UI canvas if assigned
        if (victoryUIPanel != null)
        {
            victoryUIPanel.SetActive(true);
        }

        // 2. Unlock cursor so user can click UI or exit
        if (unlockCursor)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // 3. Pause game time
        if (pauseGameOnComplete)
        {
            Time.timeScale = 0f;
        }
    }

    void OnGUI()
    {
        // Render fallback UI overlay only if custom UI panel is not assigned
        if (isCompleted && victoryUIPanel == null)
        {
            // Background box style (semi-transparent dark overlay)
            GUIStyle bgStyle = new GUIStyle(GUI.skin.box);
            Texture2D bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.12f, 0.9f));
            bgTex.Apply();
            bgStyle.normal.background = bgTex;

            // Message text style
            GUIStyle messageStyle = new GUIStyle(GUI.skin.label);
            messageStyle.fontSize = 28;
            messageStyle.fontStyle = FontStyle.Bold;
            messageStyle.alignment = TextAnchor.MiddleCenter;
            messageStyle.normal.textColor = new Color(0.12f, 0.85f, 0.52f); // Vibrant emerald green for diamonds/success

            // Button style
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 18;
            buttonStyle.fontStyle = FontStyle.Bold;
            buttonStyle.normal.textColor = Color.white;
            
            // Texture for buttons
            Texture2D btnTex = new Texture2D(1, 1);
            btnTex.SetPixel(0, 0, new Color(0.15f, 0.45f, 0.85f, 0.95f));
            btnTex.Apply();
            buttonStyle.normal.background = btnTex;

            // Define window sizes dynamically based on screen resolution
            float width = 450;
            float height = 300;
            float x = (Screen.width - width) / 2;
            float y = (Screen.height - height) / 2;

            // Draw container box
            GUI.Box(new Rect(x, y, width, height), "", bgStyle);

            // Draw victory message text
            GUI.Label(new Rect(x + 20, y + 30, width - 40, 100), victoryMessage, messageStyle);

            // Draw buttons
            float buttonWidth = 180;
            float buttonHeight = 45;
            
            // Reload/Restart Level Button
            if (GUI.Button(new Rect(x + (width / 2) - buttonWidth - 10, y + 180, buttonWidth, buttonHeight), "Play Again", buttonStyle))
            {
                ResetTimeScale();
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            }

            // Exit / Quit Button
            if (GUI.Button(new Rect(x + (width / 2) + 10, y + 180, buttonWidth, buttonHeight), "Exit Game", buttonStyle))
            {
                ResetTimeScale();
                #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
                #else
                Application.Quit();
                #endif
            }
        }
    }

    private void ResetTimeScale()
    {
        if (pauseGameOnComplete)
        {
            Time.timeScale = 1f;
        }
    }

    // Reset game completion status (helpful if script state is reused)
    public void ResetGameStatus()
    {
        isCompleted = false;
        ResetTimeScale();
        if (unlockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (victoryUIPanel != null)
        {
            victoryUIPanel.SetActive(false);
        }
    }

    // Draw a visual wireframe sphere in the Unity editor scene view to see the detection range
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
    }
}
