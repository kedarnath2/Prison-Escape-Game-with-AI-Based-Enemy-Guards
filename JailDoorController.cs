#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine;

public class JailDoorController : MonoBehaviour
{
    [Header("Door Settings")]
    [Tooltip("The moving door object. If left empty, the script will automatically search the scene for 'Portao'.")]
    public Transform doorTransform;

    [Tooltip("How many degrees the door rotates when opened.")]
    public float openRotationAngle = -90f;

    [Tooltip("How fast the door rotates.")]
    public float rotationSpeed = 3f;

    [Header("Interaction Settings")]
    [Tooltip("Tag of the player GameObject.")]
    public string playerTag = "Player";

    [Tooltip("If true, the door opens when player is close and closes when player walks away. If false, player must press E.")]
    public bool automaticDoor = false;

    [Tooltip("Show the screen prompt when the player is near the door (only active in manual mode).")]
    public bool showInteractionPrompt = true;

    [Header("Distance Detection (Recommended)")]
    [Tooltip("If checked, the script will calculate distance to the player instead of relying on trigger colliders.")]
    public bool useDistanceCheck = true;

    [Tooltip("Distance threshold within which the door opens.")]
    public float detectionRange = 4f;

    private bool isPlayerNear = false;
    private bool isOpen = false;

    private Quaternion closedRotation;
    private Quaternion openRotation;
    private Transform playerTransform;

    // Cooldown timer for locating the player dynamically if they spawn/change at runtime
    private float searchCooldown = 1.0f;
    private float searchTimer = 0.0f;
    private float logTimer = 0.0f;

    // Static UI references shared by all doors to prevent duplicates
    private static GameObject globalCanvasInstance;
    private static GameObject globalPromptPanel;
    private static UnityEngine.UI.Text globalPromptText;
    private static JailDoorController currentlyFocusedDoor = null;
    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = FindFirstObjectByType<Camera>();
        }
        LocateDoor();
        FindPlayer();
        PrintScenePositions();
        SetupInteractionUI();

        // Self-healing check: if distance check is disabled but no trigger collider exists,
        // automatically enable distance check fallback so the door still functions.
        if (!useDistanceCheck)
        {
            Collider myCollider = GetComponent<Collider>();
            Collider doorCollider = doorTransform != null ? doorTransform.GetComponent<Collider>() : null;
            
            bool hasTrigger = (myCollider != null && myCollider.isTrigger) || (doorCollider != null && doorCollider.isTrigger);
            if (!hasTrigger)
            {
                Debug.LogWarning("[JailDoorController] 'Use Distance Check' was unchecked in the inspector, but no trigger collider was found on this object or the door. Automatically enabled Distance Check fallback.");
                useDistanceCheck = true;
            }
        }
    }

    private void LocateDoor()
    {
        if (doorTransform == null)
        {
            // Try to find Portao in children
            Transform portao = transform.Find("Portao");
            if (portao != null)
            {
                doorTransform = portao;
                Debug.Log($"[JailDoorController] Auto-located door mesh '{doorTransform.name}' as child.");
            }
            else
            {
                // Try to find any MeshRenderer in children as a fallback
                MeshRenderer mr = GetComponentInChildren<MeshRenderer>();
                if (mr != null)
                {
                    doorTransform = mr.transform;
                    Debug.Log($"[JailDoorController] Auto-located door mesh '{doorTransform.name}' via MeshRenderer.");
                }
                else
                {
                    // Search the entire scene for a GameObject named "Portao"
                    GameObject portaoObj = GameObject.Find("Portao");
                    if (portaoObj != null)
                    {
                        doorTransform = portaoObj.transform;
                        Debug.Log($"[JailDoorController] Auto-located door mesh '{doorTransform.name}' in the scene.");
                    }
                    else
                    {
                        doorTransform = transform;
                        Debug.LogWarning("[JailDoorController] 'Portao' door transform not found. Operating on self.");
                    }
                }
            }
        }

        if (doorTransform != null)
        {
            InitializeRotations();
        }
    }

    private void InitializeRotations()
    {
        // Use world space rotation to rotate around the global vertical Y-axis
        closedRotation = doorTransform.rotation;
        openRotation = Quaternion.Euler(0, openRotationAngle, 0) * closedRotation;
        Debug.Log($"[JailDoorController] Initialized door world rotations: Closed={closedRotation.eulerAngles}, Open={openRotation.eulerAngles}");
    }

    void Update()
    {
        // Re-locate doorTransform if it is null (e.g. after hot-reload in play mode)
        if (doorTransform == null)
        {
            LocateDoor();
        }

        // Re-initialize rotations if they were lost during a domain reload
        if (doorTransform != null && closedRotation == Quaternion.identity && openRotation == Quaternion.identity)
        {
            InitializeRotations();
        }

        // Periodic check to locate player if not found yet (e.g. spawned dynamically)
        if (playerTransform == null)
        {
            searchTimer += Time.deltaTime;
            if (searchTimer >= searchCooldown)
            {
                searchTimer = 0f;
                FindPlayer();
            }
        }

        // Distance-based automatic opening
        if (playerTransform != null && doorTransform != null)
        {
            Collider[] doorColliders = doorTransform.GetComponentsInChildren<Collider>();
            float distance = float.MaxValue;
            Vector3 doorPosition = doorTransform.position;

            if (doorColliders.Length > 0)
            {
                foreach (Collider col in doorColliders)
                {
                    if (col.isTrigger) continue; // Skip trigger zones if any
                    
                    Vector3 closestPoint;
                    if (col is MeshCollider meshCol && !meshCol.convex)
                    {
                        // Fallback for non-convex MeshCollider: use closest point on bounds to prevent Unity errors
                        closestPoint = col.bounds.ClosestPoint(playerTransform.position);
                    }
                    else
                    {
                        closestPoint = col.ClosestPoint(playerTransform.position);
                    }

                    float d = Vector3.Distance(closestPoint, playerTransform.position);
                    if (d < distance)
                    {
                        distance = d;
                        doorPosition = closestPoint;
                    }
                }
            }

            // Fallback if no colliders are active/found
            if (distance == float.MaxValue)
            {
                if (doorTransform.parent != null && (doorTransform.parent.name.Contains("Door") || doorTransform.parent.name.ToLower().Contains("grades")))
                {
                    doorPosition = doorTransform.parent.position;
                }
                distance = Vector3.Distance(doorPosition, playerTransform.position);
            }

            bool wasNear = isPlayerNear;
            isPlayerNear = (distance <= detectionRange);

            // Periodically log the calculated distance for troubleshooting
            logTimer += Time.deltaTime;
            if (logTimer >= 1.0f)
            {
                logTimer = 0f;
                Debug.Log($"[JailDoorController] Distance to player: {distance:F2}m (Detection Range: {detectionRange}m, playerPos: {playerTransform.position}, doorPos: {doorPosition})");
            }

            if (useDistanceCheck && automaticDoor)
            {
                bool nextOpenState = isPlayerNear;
                if (nextOpenState != isOpen)
                {
                    isOpen = nextOpenState;
                    Debug.Log($"[JailDoorController] Distance check triggered door {(isOpen ? "OPEN" : "CLOSE")} (Distance: {distance:F2}m)");
                }
                
                // Trigger growth when first entering range
                if (isPlayerNear && !wasNear)
                {
                    TriggerPlayerGrowth(playerTransform.gameObject);
                }
            }
        }

        // Manual interaction mode (only active if not in automatic mode)
        if (!automaticDoor)
        {
            bool isLookingAtDoor = isPlayerNear && CheckPlayerLookingAtDoor();

            if (isLookingAtDoor)
            {
                if (currentlyFocusedDoor == null || currentlyFocusedDoor == this)
                {
                    currentlyFocusedDoor = this;
                    if (globalPromptPanel != null && globalPromptText != null)
                    {
                        string actionText = isOpen ? "Close" : "Open";
                        globalPromptText.text = $"Press <color=#54B4D3><b>[E]</b></color> to {actionText} Door";
                        globalPromptPanel.SetActive(true);
                    }
                }

                // Detect interaction input
                bool interactPressed = false;
#if ENABLE_INPUT_SYSTEM
                if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                {
                    interactPressed = true;
                }
#else
                if (Input.GetKeyDown(KeyCode.E))
                {
                    interactPressed = true;
                }
#endif

                if (interactPressed)
                {
                    isOpen = !isOpen;
                    Debug.Log($"[JailDoorController] Manual E key pressed! Door {(isOpen ? "OPEN" : "CLOSE")}");

                    // Immediately update prompt text
                    if (globalPromptText != null)
                    {
                        string actionText = isOpen ? "Close" : "Open";
                        globalPromptText.text = $"Press <color=#54B4D3><b>[E]</b></color> to {actionText} Door";
                    }
                }
            }
            else
            {
                if (currentlyFocusedDoor == this)
                {
                    currentlyFocusedDoor = null;
                    if (globalPromptPanel != null)
                    {
                        globalPromptPanel.SetActive(false);
                    }
                }
            }
        }

        // Smoothly rotate the door towards its target rotation in world space
        if (doorTransform != null)
        {
            Quaternion targetRotation = isOpen ? openRotation : closedRotation;
            doorTransform.rotation = Quaternion.Slerp(doorTransform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        }
    }

    /// <summary>
    /// Searches for the player character using multiple fallback strategies.
    /// Walks up the hierarchy to resolve the moving root player object rather than static children.
    /// </summary>
    private void FindPlayer()
    {
        Transform foundTransform = null;

        // 1. Try finding by tag
        if (!string.IsNullOrEmpty(playerTag))
        {
            try
            {
                GameObject playerObj = GameObject.FindWithTag(playerTag);
                if (playerObj != null)
                {
                    foundTransform = playerObj.transform;
                }
            }
            catch (System.Exception) { }
        }

        // 2. Try finding by component type names (reflection-safe string match to prevent compile errors if scripts are missing/moved)
        if (foundTransform == null)
        {
            Component[] allComponents = FindObjectsByType<Component>(FindObjectsSortMode.None);
            foreach (Component comp in allComponents)
            {
                if (comp != null)
                {
                    string typeName = comp.GetType().Name;
                    if (typeName == "ThirdPersonController" || typeName == "PlayerMovementController" || typeName == "PlayerController")
                    {
                        foundTransform = comp.transform;
                        break;
                    }
                }
            }
        }

        // 3. Fallback: Find by CharacterController component
        if (foundTransform == null)
        {
            CharacterController cc = FindFirstObjectByType<CharacterController>();
            if (cc != null)
            {
                foundTransform = cc.transform;
            }
        }

        // 4. Fallback: Find by name (case-insensitive search for 'player', 'armature', 'thief')
        if (foundTransform == null)
        {
            GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (GameObject go in allObjects)
            {
                if (go != null && go != gameObject)
                {
                    string lowerName = go.name.ToLower();
                    if (lowerName.Contains("player") || lowerName.Contains("armature") || lowerName.Contains("thief"))
                    {
                        // Ensure it is a physical entity/character, not a camera or UI group
                        if (go.GetComponent<CharacterController>() != null || go.GetComponent<Animator>() != null || go.GetComponent<Collider>() != null)
                        {
                            foundTransform = go.transform;
                            break;
                        }
                    }
                }
            }
        }

        // If we located any part of the player, walk up the hierarchy to resolve the moving root character!
        if (foundTransform != null)
        {
            Transform current = foundTransform;
            while (current.parent != null)
            {
                if (current.parent.GetComponent<CharacterController>() != null ||
                    current.parent.GetComponent("ThirdPersonController") != null ||
                    current.parent.GetComponent("PlayerMovementController") != null ||
                    current.parent.GetComponent("PlayerController") != null)
                {
                    current = current.parent;
                }
                else
                {
                    break;
                }
            }
            playerTransform = current;
            Debug.Log($"[JailDoorController] Successfully located player root transform: '{playerTransform.name}' at global position {playerTransform.position} (originally found '{foundTransform.name}' at {foundTransform.position})");
            AutoHealPlayerInputs(playerTransform.gameObject);
        }
    }

    private void AutoHealPlayerInputs(GameObject playerObj)
    {
        bool addedComponent = false;

        // 1. Check and add StarterAssetsInputs
        Component saInputs = playerObj.GetComponent("StarterAssetsInputs");
        if (saInputs == null)
        {
            System.Type saType = System.Type.GetType("StarterAssets.StarterAssetsInputs, Assembly-CSharp");
            if (saType != null)
            {
                playerObj.AddComponent(saType);
                Debug.LogWarning("[JailDoorController] [Auto-Heal] Added missing StarterAssetsInputs to Player.");
                addedComponent = true;
            }
        }

        // 2. Check and add PlayerInput (New Input System)
        #if ENABLE_INPUT_SYSTEM
        UnityEngine.InputSystem.PlayerInput pi = playerObj.GetComponent<UnityEngine.InputSystem.PlayerInput>();
        if (pi == null)
        {
            pi = playerObj.AddComponent<UnityEngine.InputSystem.PlayerInput>();
            Debug.LogWarning("[JailDoorController] [Auto-Heal] Added missing PlayerInput component to Player.");
            addedComponent = true;

            #if UNITY_EDITOR
            // Auto-locate and assign the input actions asset
            string[] guids = UnityEditor.AssetDatabase.FindAssets("StarterAssets t:InputActionAsset");
            if (guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                pi.actions = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(path);
                Debug.LogWarning($"[JailDoorController] [Auto-Heal] Auto-assigned input actions asset from path: {path}");
            }
            #endif
        }
        #endif

        if (addedComponent)
        {
            Debug.LogWarning("[JailDoorController] [Auto-Heal] Restored missing input components on player. Please restart Play mode to apply changes!");
        }
    }

    #if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && gameObject != null && !Application.isPlaying)
                {
                    FindPlayer();
                }
            };
        }
    }
    #endif

    /// <summary>
    /// Helper to verify if a collision object corresponds to the player.
    /// </summary>
    private bool IsPlayerObject(GameObject obj)
    {
        if (obj.CompareTag(playerTag)) return true;
        if (playerTransform != null && (obj.transform == playerTransform || obj.transform.IsChildOf(playerTransform))) return true;
        if (obj.GetComponent<PlayerMovementController>() != null) return true;
        if (obj.GetComponent<CharacterController>() != null && (obj.name.ToLower().Contains("player") || obj.name.ToLower().Contains("armature"))) return true;
        return false;
    }

    private void TriggerPlayerGrowth(GameObject playerObj)
    {
        // Resolve the root player object first to make sure we scale the correct object
        Transform rootPlayer = playerObj.transform;
        while (rootPlayer.parent != null &&
               (rootPlayer.parent.GetComponent<CharacterController>() != null ||
                rootPlayer.parent.GetComponent("ThirdPersonController") != null ||
                rootPlayer.parent.GetComponent("PlayerMovementController") != null ||
                rootPlayer.parent.GetComponent("PlayerController") != null))
        {
            rootPlayer = rootPlayer.parent;
        }

        PlayerMovementController player = rootPlayer.GetComponent<PlayerMovementController>();
        if (player == null)
        {
            player = rootPlayer.GetComponentInChildren<PlayerMovementController>();
        }

        if (player != null)
        {
            player.GrowCharacter();
            Debug.Log($"[JailDoorController] Growing player '{rootPlayer.name}' via PlayerMovementController.");
        }
        else
        {
            // Fallback: Scale player transform directly if PlayerMovementController is not present
            StartCoroutine(SmoothScalePlayer(rootPlayer.gameObject));
        }
    }

    private System.Collections.IEnumerator SmoothScalePlayer(GameObject playerObj)
    {
        float targetMultiplier = 1.5f;
        float speed = 2f;
        
        Vector3 startScale = playerObj.transform.localScale;
        Vector3 targetScale = startScale * targetMultiplier;
        
        // Prevent scaling infinitely if already scaled
        if (Mathf.Abs(startScale.x - targetScale.x) < 0.1f) yield break;

        Debug.Log($"[JailDoorController] Growing player '{playerObj.name}' directly from scale {startScale} to {targetScale}.");
        
        float progress = 0f;
        while (progress < 1f && playerObj != null)
        {
            playerObj.transform.localScale = Vector3.Lerp(startScale, targetScale, progress);
            progress += Time.deltaTime * speed;
            yield return null;
        }

        if (playerObj != null)
        {
            playerObj.transform.localScale = targetScale;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!useDistanceCheck && IsPlayerObject(other.gameObject))
        {
            isPlayerNear = true;
            
            if (playerTransform == null)
            {
                playerTransform = other.transform;
            }

            if (automaticDoor)
            {
                isOpen = true; // Auto-open the door
                Debug.Log($"[JailDoorController] Trigger collider entered! Door OPEN (Player: {other.name})");
            }

            TriggerPlayerGrowth(other.gameObject);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!useDistanceCheck && IsPlayerObject(other.gameObject))
        {
            isPlayerNear = false;

            if (automaticDoor)
            {
                isOpen = false; // Auto-close the door
                Debug.Log($"[JailDoorController] Trigger collider exited! Door CLOSE");
            }
        }
    }



    // Visualize the detection range sphere in the editor Scene view
    void OnDrawGizmosSelected()
    {
        if (doorTransform != null)
        {
            Vector3 doorPosition = doorTransform.position;
            if (doorTransform.parent != null && (doorTransform.parent.name.Contains("Door") || doorTransform.parent.name.ToLower().Contains("grades")))
            {
                doorPosition = doorTransform.parent.position;
            }
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(doorPosition, detectionRange);
        }
        else
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRange);
        }
    }

    private void PrintScenePositions()
    {
        Debug.Log("[JailDoorController] === PRINTING SCENE POSITIONS ===");
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (GameObject go in allObjects)
        {
            if (go == null) continue;
            string lowerName = go.name.ToLower();
            if (lowerName.Contains("portao") || lowerName.Contains("grades") || lowerName.Contains("armature") || lowerName.Contains("player") || lowerName.Contains("maincamera"))
            {
                Debug.Log($"[JailDoorController] GameObject '{go.name}' at Global Position: {go.transform.position}, Tag: {go.tag}, Active: {go.activeInHierarchy}");
            }
        }
    }

    private void SetupInteractionUI()
    {
        if (globalCanvasInstance == null)
        {
            Canvas existingCanvas = FindFirstObjectByType<Canvas>();
            if (existingCanvas != null && existingCanvas.gameObject.activeInHierarchy)
            {
                globalCanvasInstance = existingCanvas.gameObject;
            }
            else
            {
                globalCanvasInstance = new GameObject("InteractionCanvas");
                Canvas canvas = globalCanvasInstance.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                globalCanvasInstance.AddComponent<UnityEngine.UI.CanvasScaler>();
                globalCanvasInstance.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                DontDestroyOnLoad(globalCanvasInstance);
            }
        }

        if (globalPromptPanel == null && globalCanvasInstance != null)
        {
            globalPromptPanel = new GameObject("InteractionPromptPanel");
            globalPromptPanel.transform.SetParent(globalCanvasInstance.transform, false);

            RectTransform rect = globalPromptPanel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(0, 0);
            rect.pivot = new Vector2(0, 0);
            rect.anchoredPosition = new Vector2(40, 40); // Bottom left position
            rect.sizeDelta = new Vector2(300, 60);

            UnityEngine.UI.Image bgImage = globalPromptPanel.AddComponent<UnityEngine.UI.Image>();
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.12f, 0.85f));
            tex.Apply();
            bgImage.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            bgImage.color = Color.white;

            GameObject textObj = new GameObject("PromptText");
            textObj.transform.SetParent(globalPromptPanel.transform, false);

            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0, 0);
            textRect.anchorMax = new Vector2(1, 1);
            textRect.offsetMin = new Vector2(15, 5);
            textRect.offsetMax = new Vector2(-15, -5);

            globalPromptText = textObj.AddComponent<UnityEngine.UI.Text>();
            
            Font defaultFont = null;
            try
            {
                defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (System.Exception) {}

            if (defaultFont == null)
            {
                try
                {
                    defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                catch (System.Exception) {}
            }
            globalPromptText.font = defaultFont;
            globalPromptText.fontSize = 18;
            globalPromptText.fontStyle = FontStyle.Normal;
            globalPromptText.alignment = TextAnchor.MiddleLeft;
            globalPromptText.color = Color.white;
            globalPromptText.supportRichText = true;

            globalPromptPanel.SetActive(false);
        }
    }

    private bool CheckPlayerLookingAtDoor()
    {
        if (playerTransform == null || doorTransform == null) return false;

        Vector3 rayStart = playerTransform.position + Vector3.up * 1.2f;
        Camera cam = mainCamera != null ? mainCamera : Camera.main;
        Vector3 rayDir = cam != null ? cam.transform.forward : playerTransform.forward;

        float maxDistance = detectionRange;

        // Cast from player's body forward, ignoring player colliders
        if (CheckRaycastHitIgnorePlayer(rayStart, rayDir, maxDistance))
        {
            return true;
        }

        // Fallback: Cast from camera center, ignoring player colliders
        if (cam != null)
        {
            Vector3 cameraStart = cam.transform.position;
            float cameraDistanceLimit = maxDistance + Vector3.Distance(cam.transform.position, playerTransform.position);
            if (CheckRaycastHitIgnorePlayer(cameraStart, rayDir, cameraDistanceLimit))
            {
                return true;
            }
        }

        return false;
    }

    private bool CheckRaycastHitIgnorePlayer(Vector3 start, Vector3 direction, float maxDistance)
    {
        RaycastHit[] hits = Physics.RaycastAll(start, direction, maxDistance);
        
        // Sort hits by distance
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));

        foreach (RaycastHit hit in hits)
        {
            // Ignore if hit is the player itself or any child of the player
            if (hit.transform == playerTransform || hit.transform.IsChildOf(playerTransform))
            {
                continue;
            }

            // Ignore if hit is a guard (tagged "Guard" or has "Guard" in parent)
            if (hit.transform.CompareTag("Guard") || (hit.transform.parent != null && hit.transform.parent.CompareTag("Guard")))
            {
                continue;
            }

            // Check if it hit the door or its sub-meshes / parent object
            if (hit.transform == doorTransform || 
                hit.transform.IsChildOf(doorTransform) || 
                (doorTransform.parent != null && (hit.transform == doorTransform.parent || hit.transform.IsChildOf(doorTransform.parent))))
            {
                return true;
            }

            // If we hit any other static obstacle (like a solid wall) before the door, 
            // we should stop to prevent interacting through walls, unless it's a trigger collider!
            if (!hit.collider.isTrigger)
            {
                return false;
            }
        }
        return false;
    }

    void OnDisable()
    {
        if (currentlyFocusedDoor == this)
        {
            currentlyFocusedDoor = null;
            if (globalPromptPanel != null)
            {
                globalPromptPanel.SetActive(false);
            }
        }
    }

    void OnDestroy()
    {
        if (currentlyFocusedDoor == this)
        {
            currentlyFocusedDoor = null;
            if (globalPromptPanel != null)
            {
                globalPromptPanel.SetActive(false);
            }
        }
    }

    public void SetDoorOpen(bool open)
    {
        isOpen = open;
        Debug.Log($"[JailDoorController] Door state forced to: {(isOpen ? "OPEN" : "CLOSE")}");

        // Immediately update prompt text if active
        if (currentlyFocusedDoor == this && globalPromptText != null)
        {
            string actionText = isOpen ? "Close" : "Open";
            globalPromptText.text = $"Press <color=#54B4D3><b>[E]</b></color> to {actionText} Door";
        }
    }
}
