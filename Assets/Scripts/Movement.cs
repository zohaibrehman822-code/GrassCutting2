using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Rigidbody))]
public class Movement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 2f;

    [Header("Floating Joystick")]
    [Tooltip("Maximum distance, in pixels, the joystick knob can move from its floating origin.")]
    [SerializeField, Min(1f)] private float joystickRadius = 150f;

    [Tooltip("Small finger movement ignored as accidental input.")]
    [SerializeField, Range(0f, 0.3f)] private float touchDeadZone = 0.05f;

    [Header("Joystick Image Placeholders")]
    [Tooltip("Optional future joystick background image. Leave empty for an invisible joystick.")]
    [SerializeField] private Image joystickBackgroundImage;

    [Tooltip("Optional future joystick knob image. Leave empty for an invisible joystick.")]
    [SerializeField] private Image joystickKnobImage;

    [Header("Movement Smoothing")]
    [Tooltip("How quickly the player accelerates toward the desired speed.")]
    [SerializeField, Min(0.1f)] private float acceleration = 8f;

    [Tooltip("How quickly the player slows down when input is released.")]
    [SerializeField, Min(0.1f)] private float braking = 12f;

    [Tooltip("How quickly the player changes direction when dragging opposite.")]
    [SerializeField, Min(0.1f)] private float turnAcceleration = 10f;

    [Header("Movement Particles")]
    [SerializeField] private ParticleSystem movementParticles;

    [Tooltip("Minimum movement speed required before particles start.")]
    [SerializeField, Min(0f)] private float particleStartSpeed = 0.05f;

    [Tooltip("Particle emission rate at full movement speed.")]
    [SerializeField, Min(0f)] private float maxParticleRate = 30f;

    [Tooltip("How quickly particle emission changes with movement speed.")]
    [SerializeField, Min(0.1f)] private float particleSpeedMultiplier = 1f;

    private Rigidbody rb;
    private RotateObject rotateObject;

    private InputAction moveAction;
    private InputAction pointerPositionAction;
    private InputAction pointerPressAction;

    private Vector2 joystickOrigin;
    private Vector2 joystickKnobPosition;

    private Vector3 desiredDirection;
    private Vector3 currentVelocity;

    public Vector3 CurrentMoveDirection => smoothedDirection;

    private Vector3 smoothedDirection;

    private float fixedYPosition;
    private bool canMove = true;
    private bool pointerWasPressed;

    private ParticleSystem.EmissionModule particleEmission;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rotateObject = GetComponent<RotateObject>();

        fixedYPosition = rb.position.y;

        rb.constraints =
            RigidbodyConstraints.FreezePositionY |
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;

        if (movementParticles != null)
        {
            particleEmission = movementParticles.emission;
            particleEmission.rateOverTime = 0f;

            movementParticles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear
            );
        }

        SetJoystickVisible(false);
        CreateInputActions();
    }

    private void OnEnable()
    {
        moveAction.Enable();
        pointerPositionAction.Enable();
        pointerPressAction.Enable();
    }

    private void OnDisable()
    {
        moveAction.Disable();
        pointerPositionAction.Disable();
        pointerPressAction.Disable();

        pointerWasPressed = false;
        desiredDirection = Vector3.zero;

        SetJoystickVisible(false);
        StopMovement();
    }

    private void OnDestroy()
    {
        moveAction.Dispose();
        pointerPositionAction.Dispose();
        pointerPressAction.Dispose();
    }

    private void Update()
    {
        if (!canMove)
            return;

        ReadInput();
    }

    private void FixedUpdate()
    {
        if (!canMove)
        {
            StopMovement();
            return;
        }

        UpdateVelocity();

        Vector3 targetPosition =
            rb.position +
            currentVelocity * Time.fixedDeltaTime;

        targetPosition.y = fixedYPosition;

        rb.MovePosition(targetPosition);

        if (moveSpeed > 0.001f)
        {
            smoothedDirection =
                currentVelocity / moveSpeed;
        }
        else
        {
            smoothedDirection = Vector3.zero;
        }

        smoothedDirection = Vector3.ClampMagnitude(
            smoothedDirection,
            1f
        );

        bool isMoving =
            currentVelocity.sqrMagnitude > 0.001f;

        if (rotateObject != null)
        {
            rotateObject.SetMoving(isMoving);
        }

        UpdateMovementParticles();
    }

    private void UpdateVelocity()
    {
        Vector3 targetVelocity =
            desiredDirection * moveSpeed;

        float rate;

        // No input = slow down.
        if (desiredDirection.sqrMagnitude < 0.0001f)
        {
            rate = braking;
        }
        // Input is opposite to current movement.
        else if (
            currentVelocity.sqrMagnitude > 0.0001f &&
            Vector3.Dot(
                currentVelocity.normalized,
                desiredDirection.normalized
            ) < 0f
        )
        {
            rate = turnAcceleration;
        }
        // Normal acceleration.
        else
        {
            rate = acceleration;
        }

        currentVelocity = Vector3.MoveTowards(
            currentVelocity,
            targetVelocity,
            rate * Time.fixedDeltaTime
        );
    }

    public void SetCanMove(bool value)
    {
        canMove = value;

        if (!value)
        {
            pointerWasPressed = false;
            desiredDirection = Vector3.zero;

            SetJoystickVisible(false);
            StopMovement();
        }
    }

    private void ReadInput()
    {
        // Preserve keyboard/gamepad movement.
        Vector2 input =
            moveAction.ReadValue<Vector2>();

        bool pointerIsPressed =
            pointerPressAction.IsPressed();

        if (pointerIsPressed)
        {
            Vector2 pointerPosition =
                pointerPositionAction.ReadValue<Vector2>();

            // Finger just touched the screen: the joystick "floats"
            // to wherever the finger first touched.
            if (!pointerWasPressed)
            {
                joystickOrigin = pointerPosition;
                joystickKnobPosition = joystickOrigin;

                SetJoystickVisible(true);
                UpdateJoystickVisual();
                input = Vector2.zero;
            }
            else
            {
                Vector2 offset =
                    pointerPosition - joystickOrigin;

                float distance =
                    offset.magnitude;

                float deadZoneDistance =
                    joystickRadius * touchDeadZone;

                if (distance <= deadZoneDistance)
                {
                    input = Vector2.zero;
                }
                else
                {
                    // The knob follows the finger until it reaches
                    // the edge of the joystick.
                    Vector2 clampedOffset =
                        Vector2.ClampMagnitude(
                            offset,
                            joystickRadius
                        );

                    joystickKnobPosition =
                        joystickOrigin + clampedOffset;

                    float strength =
                        Mathf.Clamp01(
                            clampedOffset.magnitude /
                            Mathf.Max(joystickRadius, 0.001f)
                        );

                    Vector2 direction =
                        clampedOffset.sqrMagnitude > 0.0001f
                            ? clampedOffset.normalized
                            : Vector2.zero;

                    input = direction * strength;

                    // Floating behavior: once the knob reaches the
                    // edge, the joystick origin follows the finger
                    // while preserving the maximum-radius knob offset.
                    if (distance > joystickRadius)
                    {
                        joystickOrigin =
                            pointerPosition - direction * joystickRadius;

                        joystickKnobPosition =
                            joystickOrigin + clampedOffset;

                        UpdateJoystickVisual();
                    }
                }

                UpdateJoystickVisual();
            }
        }
        else
        {
            joystickKnobPosition = joystickOrigin;
            SetJoystickVisible(false);
        }

        pointerWasPressed = pointerIsPressed;

        input = Vector2.ClampMagnitude(
            input,
            1f
        );

        desiredDirection = new Vector3(
            input.x,
            0f,
            input.y
        );
    }

    private void SetJoystickVisible(bool visible)
    {
        // The joystick is intentionally invisible for now.
        // Assign your future UI Images in the Inspector when ready.
        if (joystickBackgroundImage != null)
            joystickBackgroundImage.enabled = visible;

        if (joystickKnobImage != null)
            joystickKnobImage.enabled = visible;
    }

    private void UpdateJoystickVisual()
    {
        // These are only placeholders for future joystick artwork.
        // The movement system works without assigning either Image.
        if (joystickBackgroundImage != null)
            joystickBackgroundImage.rectTransform.position = joystickOrigin;

        if (joystickKnobImage != null)
            joystickKnobImage.rectTransform.position = joystickKnobPosition;
    }

    private void StopMovement()
    {
        desiredDirection = Vector3.zero;
        currentVelocity = Vector3.zero;
        smoothedDirection = Vector3.zero;

        Vector3 velocity =
            rb.linearVelocity;

        velocity.x = 0f;
        velocity.z = 0f;

        rb.linearVelocity = velocity;

        if (rotateObject != null)
        {
            rotateObject.SetMoving(false);
        }

        StopMovementParticles();
    }

    private void UpdateMovementParticles()
    {
        if (movementParticles == null)
            return;

        float movementAmount =
            currentVelocity.magnitude /
            Mathf.Max(moveSpeed, 0.001f);

        if (movementAmount <= particleStartSpeed)
        {
            StopMovementParticles();
            return;
        }

        if (!movementParticles.isPlaying)
        {
            movementParticles.Play();
        }

        float normalizedSpeed =
            Mathf.Clamp01(
                movementAmount *
                particleSpeedMultiplier
            );

        float emissionRate =
            normalizedSpeed *
            maxParticleRate;

        particleEmission.rateOverTime =
            emissionRate;
    }

    private void StopMovementParticles()
    {
        if (movementParticles == null)
            return;

        particleEmission.rateOverTime = 0f;

        if (movementParticles.isPlaying)
        {
            movementParticles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmitting
            );
        }
    }

    private void CreateInputActions()
    {
        moveAction = new InputAction(
            "Move",
            InputActionType.Value
        );

        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");

        moveAction.AddBinding(
            "<Gamepad>/leftStick"
        );

        pointerPositionAction = new InputAction(
            "Pointer Position",
            InputActionType.Value,
            "<Pointer>/position"
        );

        pointerPressAction = new InputAction(
            "Pointer Press",
            InputActionType.Button,
            "<Pointer>/press"
        );
    }

    public void SetGroundHeight(float height)
    {
        fixedYPosition = height;

        Vector3 position =
            rb.position;

        position.y = height;

        rb.position = position;
    }
}
