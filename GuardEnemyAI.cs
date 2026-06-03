using UnityEngine;
using System.Collections;

public class GuardEnemyAI : MonoBehaviour
{
    public enum GuardState { Patrolling, Chasing, Resetting }

    [Header("Movement Settings")]
    [Tooltip("Speed while patrolling.")]
    public float patrolSpeed = 2.5f;

    [Tooltip("Speed when chasing the player.")]
    public float chaseSpeed = 5.5f;

    [Tooltip("How long the guard waits at each waypoint.")]
    public float waypointWaitTime = 2.0f;

    [Header("Detection Settings")]
    [Tooltip("Visual range of the guard (meters).")]
    public float detectionRange = 15.0f;

    [Tooltip("Field of View angle of the guard (degrees).")]
    public float fieldOfViewAngle = 90.0f;

    [Tooltip("Distance at which the guard catches the player.")]
    public float catchDistance = 1.5f;

    [Tooltip("Height offset of the guard's eyes for raycasting.")]
    public float eyeHeightOffset = 1.6f;

    [Header("Patrol Path (Optional)")]
    [Tooltip("Drag waypoint transforms here to define a custom path. If empty, the guard will automatically patrol back and forth from their start position.")]
    public Transform[] patrolWaypoints;

    [Header("Jail Settings")]
    [Tooltip("The Transform (empty GameObject) representing the jail spawn location where the player is thrown when caught.")]
    public Transform jailSpawnPoint;

    [Tooltip("Fallback coordinates if no jailSpawnPoint is assigned in the Inspector.")]
    public Vector3 fallbackJailPosition = new Vector3(-84.0f, 0.0f, 44.5f);

    [Header("Visual Effects")]
    [Tooltip("If true, a spotlight is spawned representing the guard's field of view. Turns red during chase!")]
    public bool enableFlashlight = true;

    private GuardState currentState = GuardState.Patrolling;
    private Transform playerTransform;
    private Vector3 startingPosition;
    private Light flashlightComponent;

    // Patrol variables
    private int currentWaypointIndex = 0;
    private bool isWaitingAtWaypoint = false;
    private Vector3 autoPatrolTarget;
    private bool autoPatrollingForward = true;

    // Static UI references for Caught Screen Overlay
    private static GameObject globalCanvasInstance;
    private static GameObject globalCaughtPanel;
    private static UnityEngine.UI.Image globalCaughtOverlay;
    private static UnityEngine.UI.Text globalCaughtTitleText;
    private static UnityEngine.UI.Text globalCaughtSubText;
    private static bool isCaughtScreenActive = false;

    void Start()
    {
        startingPosition = transform.position;
        autoPatrolTarget = startingPosition + transform.forward * 6.0f; // Default 6m patrol path forward if no waypoints

        FindPlayer();
        SetupCaughtUI();

        // Spotlight visual cone disabled per user request
        /*
        if (enableFlashlight)
        {
            CreateFlashlight();
        }
        */
    }

    void Update()
    {
        if (playerTransform == null)
        {
            FindPlayer();
            return;
        }

        // Run perception check every frame to see if we spot the player
        bool playerSpotted = CheckPlayerInSight();

        if (playerSpotted && currentState != GuardState.Resetting)
        {
            if (currentState != GuardState.Chasing)
            {
                currentState = GuardState.Chasing;
                Debug.Log("[GuardEnemyAI] Spotted Player! Starting chase!");
                if (flashlightComponent != null)
                {
                    flashlightComponent.color = Color.red;
                    flashlightComponent.intensity = 3.0f;
                }
            }
        }
        else if (!playerSpotted && currentState == GuardState.Chasing)
        {
            // Player escaped or broke line of sight
            currentState = GuardState.Patrolling;
            Debug.Log("[GuardEnemyAI] Player lost. Resuming patrol.");
            if (flashlightComponent != null)
            {
                flashlightComponent.color = Color.yellow;
                flashlightComponent.intensity = 1.5f;
            }
        }

        // Execute behavior based on current state
        switch (currentState)
        {
            case GuardState.Patrolling:
                PatrolBehavior();
                break;
            case GuardState.Chasing:
                ChaseBehavior();
                break;
            case GuardState.Resetting:
                ResetBehavior();
                break;
        }
    }

    private void PatrolBehavior()
    {
        Vector3 targetPos;

        if (patrolWaypoints != null && patrolWaypoints.Length > 0)
        {
            // Waypoint patrol
            targetPos = patrolWaypoints[currentWaypointIndex].position;

            if (Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(targetPos.x, 0, targetPos.z)) < 0.5f)
            {
                if (!isWaitingAtWaypoint)
                {
                    StartCoroutine(WaitAndMoveToNextWaypoint());
                }
                return;
            }
        }
        else
        {
            // Automatic back-and-forth patrol from start position
            targetPos = autoPatrollingForward ? autoPatrolTarget : startingPosition;

            if (Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(targetPos.x, 0, targetPos.z)) < 0.5f)
            {
                if (!isWaitingAtWaypoint)
                {
                    StartCoroutine(WaitAndReverseAutoPatrol());
                }
                return;
            }
        }

        MoveTowardsTarget(targetPos, patrolSpeed);
    }

    private IEnumerator WaitAndMoveToNextWaypoint()
    {
        isWaitingAtWaypoint = true;
        yield return new WaitForSeconds(waypointWaitTime);
        currentWaypointIndex = (currentWaypointIndex + 1) % patrolWaypoints.Length;
        isWaitingAtWaypoint = false;
    }

    private IEnumerator WaitAndReverseAutoPatrol()
    {
        isWaitingAtWaypoint = true;
        yield return new WaitForSeconds(waypointWaitTime);
        autoPatrollingForward = !autoPatrollingForward;
        isWaitingAtWaypoint = false;
    }

    private void ChaseBehavior()
    {
        Vector3 playerPos = playerTransform.position;
        MoveTowardsTarget(playerPos, chaseSpeed);

        // Check if player is caught (radius-aware and scale-aware)
        float playerRadius = 0.5f;
        float guardRadius = 0.5f;
        
        CharacterController playerCC = playerTransform.GetComponent<CharacterController>();
        if (playerCC != null) playerRadius = playerCC.radius * playerTransform.lossyScale.x;
        
        CharacterController guardCC = GetComponent<CharacterController>();
        if (guardCC != null)
        {
            guardRadius = guardCC.radius * transform.lossyScale.x;
        }
        else
        {
            CapsuleCollider guardCapsule = GetComponent<CapsuleCollider>();
            if (guardCapsule != null)
            {
                guardRadius = guardCapsule.radius * transform.lossyScale.x;
            }
            else
            {
                guardRadius = 0.5f * transform.lossyScale.x; // Fallback to scale-based default
            }
        }
        
        float minTouchDistance = playerRadius + guardRadius;
        float actualCatchDistance = Mathf.Max(catchDistance * transform.lossyScale.y, minTouchDistance + 0.5f);

        float distance = Vector3.Distance(transform.position, playerPos);
        if (distance <= actualCatchDistance && !isCaughtScreenActive)
        {
            StartCoroutine(CatchPlayerSequence());
        }
    }

    private void ResetBehavior()
    {
        // Smoothly return back to starting position or waypoints
        Vector3 targetPos = (patrolWaypoints != null && patrolWaypoints.Length > 0) ? patrolWaypoints[0].position : startingPosition;
        MoveTowardsTarget(targetPos, patrolSpeed);

        if (Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(targetPos.x, 0, targetPos.z)) < 0.5f)
        {
            currentState = GuardState.Patrolling;
            if (flashlightComponent != null)
            {
                flashlightComponent.color = Color.yellow;
                flashlightComponent.intensity = 1.5f;
            }
        }
    }

    private void MoveTowardsTarget(Vector3 targetPos, float speed)
    {
        Vector3 direction = (targetPos - transform.position);
        direction.y = 0; // Rotate only horizontally

        if (direction.magnitude > 0.1f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 6.0f);
        }

        // Apply translation
        transform.position = Vector3.MoveTowards(transform.position, targetPos, speed * Time.deltaTime);
    }

    private bool CheckPlayerInSight()
    {
        if (playerTransform == null) return false;

        float guardScale = transform.lossyScale.y;
        float playerScale = playerTransform.lossyScale.y;

        Vector3 eyesPosition = transform.position + Vector3.up * (eyeHeightOffset * guardScale);
        Vector3 playerChestPosition = playerTransform.position + Vector3.up * (1.0f * playerScale);

        Vector3 directionToPlayer = playerChestPosition - eyesPosition;
        float distanceToPlayer = directionToPlayer.magnitude;

        // 1. Distance check (scale detectionRange by guard's scale so vision reaches as far as their size demands)
        float actualDetectionRange = detectionRange * guardScale;
        if (distanceToPlayer > actualDetectionRange) return false;

        // 2. Field of View Angle check
        float angle = Vector3.Angle(transform.forward, directionToPlayer.normalized);
        if (angle > fieldOfViewAngle / 2.0f) return false;

        // 3. Line of sight check: check if any solid obstacle blocks the view to the player
        RaycastHit[] hits = Physics.RaycastAll(eyesPosition, directionToPlayer.normalized, distanceToPlayer);

        foreach (RaycastHit hit in hits)
        {
            // Ignore if hit is this guard, its children, or another guard tagged "Guard"
            if (hit.transform == transform || hit.transform.IsChildOf(transform) ||
                hit.transform.CompareTag("Guard") || (hit.transform.parent != null && hit.transform.parent.CompareTag("Guard")))
            {
                continue;
            }

            // Ignore triggers
            if (hit.collider.isTrigger) continue;

            // Ignore player components
            if (hit.transform == playerTransform || hit.transform.IsChildOf(playerTransform))
            {
                continue;
            }

            // If we hit any solid obstacle (wall, box, terrain) closer than the player, the view is blocked!
            if (hit.distance < distanceToPlayer)
            {
                return false;
            }
        }

        // If no solid obstacles are blocking the line of sight, the guard can see the player!
        return true;
    }

    private IEnumerator CatchPlayerSequence()
    {
        isCaughtScreenActive = true;
        currentState = GuardState.Resetting;

        Debug.LogWarning("[GuardEnemyAI] Caught the player! Throwing back to jail...");

        // 1. Trigger caught overlay fade-in
        if (globalCaughtPanel != null)
        {
            globalCaughtPanel.SetActive(true);
        }

        // Fade UI alpha in
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * 4f;
            if (globalCaughtOverlay != null)
            {
                globalCaughtOverlay.color = new Color(0.12f, 0.03f, 0.03f, Mathf.Lerp(0, 0.92f, t));
            }
            if (globalCaughtTitleText != null)
            {
                globalCaughtTitleText.color = new Color(0.9f, 0.1f, 0.1f, Mathf.Lerp(0, 1.0f, t));
            }
            if (globalCaughtSubText != null)
            {
                globalCaughtSubText.color = new Color(0.8f, 0.8f, 0.8f, Mathf.Lerp(0, 1.0f, t));
            }
            yield return null;
        }

        yield return new WaitForSeconds(1.0f);

        // 2. Teleport player to jail
        CharacterController cc = playerTransform.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false; // Disable character controller to allow direct transform teleporting
        
        Vector3 targetTeleportPos = fallbackJailPosition;
        if (jailSpawnPoint != null)
        {
            targetTeleportPos = jailSpawnPoint.position;
            playerTransform.rotation = jailSpawnPoint.rotation; // Match jail orientation
        }
        playerTransform.position = targetTeleportPos;

        if (cc != null) cc.enabled = true;

        // 3. Close the jail doors
        JailDoorController[] doors = FindObjectsByType<JailDoorController>(FindObjectsSortMode.None);
        foreach (JailDoorController door in doors)
        {
            door.SetDoorOpen(false);
        }

        yield return new WaitForSeconds(1.0f);

        // 4. Fade UI alpha out
        t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * 3f;
            if (globalCaughtOverlay != null)
            {
                globalCaughtOverlay.color = new Color(0.12f, 0.03f, 0.03f, Mathf.Lerp(0.92f, 0, t));
            }
            if (globalCaughtTitleText != null)
            {
                globalCaughtTitleText.color = new Color(0.9f, 0.1f, 0.1f, Mathf.Lerp(1.0f, 0, t));
            }
            if (globalCaughtSubText != null)
            {
                globalCaughtSubText.color = new Color(0.8f, 0.8f, 0.8f, Mathf.Lerp(1.0f, 0, t));
            }
            yield return null;
        }

        if (globalCaughtPanel != null)
        {
            globalPromptPanelSetStatus(false);
        }

        isCaughtScreenActive = false;
    }

    private void globalPromptPanelSetStatus(bool active)
    {
        if (globalCaughtPanel != null)
        {
            globalCaughtPanel.SetActive(active);
        }
    }

    private void FindPlayer()
    {
        // Locate player movement script
        PlayerMovementController pm = FindFirstObjectByType<PlayerMovementController>();
        if (pm != null)
        {
            playerTransform = pm.transform;
            return;
        }

        // Try finding by CharacterController component
        CharacterController cc = FindFirstObjectByType<CharacterController>();
        if (cc != null)
        {
            playerTransform = cc.transform;
            return;
        }

        // Fallback: Find by player tag
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
        }
    }

    private void CreateFlashlight()
    {
        GameObject lightObj = new GameObject("GuardFlashlight");
        lightObj.transform.SetParent(transform, false);
        
        float scaleMultiplier = transform.lossyScale.y;
        lightObj.transform.localPosition = new Vector3(0.0f, eyeHeightOffset * scaleMultiplier, 0.2f * scaleMultiplier);
        lightObj.transform.localRotation = Quaternion.Euler(15.0f, 0.0f, 0.0f); // Point downwards to project cone on ground

        flashlightComponent = lightObj.AddComponent<Light>();
        flashlightComponent.type = LightType.Spot;
        flashlightComponent.range = Mathf.Min(detectionRange * scaleMultiplier, 35.0f); // Clamp range to prevent map washout
        flashlightComponent.spotAngle = Mathf.Min(fieldOfViewAngle, 65.0f); // Keep cone focused
        flashlightComponent.color = Color.yellow;
        flashlightComponent.intensity = 1.5f; // Gentle brightness
        flashlightComponent.shadows = LightShadows.None; // Disable shadows to prevent URP shadow atlas overload
    }

    private void SetupCaughtUI()
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
                globalCanvasInstance = new GameObject("StealthGameCanvas");
                Canvas canvas = globalCanvasInstance.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                globalCanvasInstance.AddComponent<UnityEngine.UI.CanvasScaler>();
                globalCanvasInstance.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                DontDestroyOnLoad(globalCanvasInstance);
            }
        }

        if (globalCaughtPanel == null && globalCanvasInstance != null)
        {
            // Panel container
            globalCaughtPanel = new GameObject("CaughtOverlayPanel");
            globalCaughtPanel.transform.SetParent(globalCanvasInstance.transform, false);

            RectTransform panelRect = globalCaughtPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0, 0);
            panelRect.anchorMax = new Vector2(1, 1);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            // Overlay Image
            globalCaughtOverlay = globalCaughtPanel.AddComponent<UnityEngine.UI.Image>();
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, new Color(0.12f, 0.03f, 0.03f, 0f));
            tex.Apply();
            globalCaughtOverlay.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            globalCaughtOverlay.color = Color.white;

            // Title Text GameObject
            GameObject titleObj = new GameObject("CaughtTitleText");
            titleObj.transform.SetParent(globalCaughtPanel.transform, false);

            RectTransform titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.1f, 0.5f);
            titleRect.anchorMax = new Vector2(0.9f, 0.7f);
            titleRect.anchoredPosition = Vector2.zero;

            globalCaughtTitleText = titleObj.AddComponent<UnityEngine.UI.Text>();
            
            // SubText GameObject
            GameObject subObj = new GameObject("CaughtSubText");
            subObj.transform.SetParent(globalCaughtPanel.transform, false);

            RectTransform subRect = subObj.AddComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0.1f, 0.35f);
            subRect.anchorMax = new Vector2(0.9f, 0.5f);
            subRect.anchoredPosition = Vector2.zero;

            globalCaughtSubText = subObj.AddComponent<UnityEngine.UI.Text>();

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

            globalCaughtTitleText.font = defaultFont;
            globalCaughtTitleText.fontSize = 42;
            globalCaughtTitleText.fontStyle = FontStyle.Bold;
            globalCaughtTitleText.alignment = TextAnchor.MiddleCenter;
            globalCaughtTitleText.color = new Color(0.9f, 0.1f, 0.1f, 0f);

            globalCaughtSubText.font = defaultFont;
            globalCaughtSubText.fontSize = 20;
            globalCaughtSubText.fontStyle = FontStyle.Normal;
            globalCaughtSubText.alignment = TextAnchor.MiddleCenter;
            globalCaughtSubText.color = new Color(0.8f, 0.8f, 0.8f, 0f);
            globalCaughtSubText.text = "Sent back to the jail cell...";

            // Assign title message
            globalCaughtTitleText.text = "CAUGHT BY THE GUARD!";

            globalCaughtPanel.SetActive(false);
        }
    }

    public void SetFlashlightActive(bool active)
    {
        if (flashlightComponent != null)
        {
            flashlightComponent.gameObject.SetActive(active);
        }
    }

    public void ResetGuardState()
    {
        currentState = GuardState.Patrolling;
        transform.position = startingPosition;
        isWaitingAtWaypoint = false;
        if (flashlightComponent != null)
        {
            flashlightComponent.gameObject.SetActive(enableFlashlight);
            flashlightComponent.color = Color.yellow;
            flashlightComponent.intensity = 1.5f;
        }
    }

    void OnDrawGizmosSelected()
    {
        // Draw the visibility range and field of view in scene editor
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Vector3 eyesPosition = transform.position + Vector3.up * eyeHeightOffset;
        Vector3 fovLeft = Quaternion.AngleAxis(-fieldOfViewAngle / 2.0f, Vector3.up) * transform.forward * detectionRange;
        Vector3 fovRight = Quaternion.AngleAxis(fieldOfViewAngle / 2.0f, Vector3.up) * transform.forward * detectionRange;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(eyesPosition, eyesPosition + fovLeft);
        Gizmos.DrawLine(eyesPosition, eyesPosition + fovRight);
    }
}
