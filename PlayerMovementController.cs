#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class PlayerMovementController : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("How fast the character moves.")]
    public float moveSpeed = 5f;

    [Tooltip("How fast the character rotates to face their movement direction.")]
    public float rotationSpeed = 10f;

    [Tooltip("Gravity acceleration force.")]
    public float gravity = -9.81f;

    [Header("Animation Settings")]
    [Tooltip("The name of the float parameter in your Animator Controller Blend Tree.")]
    public string animatorParameterName = "Blend";

    [Tooltip("Dampening time to smooth out transitions in animations.")]
    public float animationDampTime = 0.1f;

    private CharacterController controller;
    private Animator animator;
    private Camera mainCamera;
    private float verticalVelocity;

    [Header("Scaling Settings")]
    [Tooltip("Target scale multiplier when growing.")]
    public float targetScaleMultiplier = 1.5f;

    [Tooltip("How fast the character grows.")]
    public float scaleSpeed = 2f;

    private Vector3 originalScale;
    private Vector3 targetScale;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        mainCamera = Camera.main;

        // If the main camera is not tagged as MainCamera, try to find any camera in the scene
        if (mainCamera == null)
        {
            mainCamera = FindFirstObjectByType<Camera>();
        }

        originalScale = transform.localScale;
        targetScale = originalScale;
    }

    void Update()
    {
        // Get movement inputs (WASD or Arrow keys)
        float horizontal = 0f;
        float vertical = 0f;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) vertical += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) vertical -= 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) horizontal -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) horizontal += 1f;
        }
#else
        horizontal = Input.GetAxisRaw("Horizontal");
        vertical = Input.GetAxisRaw("Vertical");
#endif

        // Troubleshoot player movement
        if (horizontal != 0f || vertical != 0f)
        {
            Debug.Log($"[PlayerMovement] Input: Horiz={horizontal}, Vert={vertical} | Controller Active={controller.enabled} | Position={transform.position}");
        }

        Vector3 inputDirection = new Vector3(horizontal, 0f, vertical).normalized;

        Vector3 moveDirection = Vector3.zero;

        // Only calculate movement if we have input
        if (inputDirection.magnitude >= 0.1f)
        {
            // Calculate movement direction relative to the camera's orientation
            if (mainCamera != null)
            {
                Vector3 cameraForward = mainCamera.transform.forward;
                Vector3 cameraRight = mainCamera.transform.right;
                
                // Keep movement on the horizontal plane
                cameraForward.y = 0f;
                cameraRight.y = 0f;
                
                cameraForward.Normalize();
                cameraRight.Normalize();

                moveDirection = (cameraForward * inputDirection.z + cameraRight * inputDirection.x).normalized;
            }
            else
            {
                // Fallback to world space if no camera is available
                moveDirection = inputDirection;
            }

            // Smoothly rotate player to face the direction of movement
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        // Apply gravity forces
        if (controller.isGrounded)
        {
            verticalVelocity = -0.5f; // Small constant downward force to ensure isGrounded stays accurate
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        // Combine movement velocity with gravity
        Vector3 finalVelocity = (moveDirection * moveSpeed) + new Vector3(0, verticalVelocity, 0);

        // Apply movement to CharacterController
        controller.Move(finalVelocity * Time.deltaTime);

        // Update Animator parameter (0 = Idle, 1 = Moving)
        // inputDirection.magnitude is 1 if moving, 0 if idle.
        float targetBlendValue = inputDirection.magnitude;
        animator.SetFloat(animatorParameterName, targetBlendValue, animationDampTime, Time.deltaTime);

        // Smoothly interpolate scale if target scale has changed
        if (transform.localScale != targetScale)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * scaleSpeed);
        }
    }

    /// <summary>
    /// Smoothly increases the character's size to the target scale multiplier.
    /// </summary>
    public void GrowCharacter()
    {
        targetScale = originalScale * targetScaleMultiplier;
    }
}
