using UnityEngine;
using ControlFreak2;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class CutterJoystickController : MonoBehaviour
{
    [Header("CF2 Joystick Only")]
    [SerializeField] private string horizontalAxis = "Horizontal";
    [SerializeField] private string verticalAxis = "Vertical";
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float rotationSmooth = 14f;
    [SerializeField] private float stickDeadZone = 0.12f;
    [SerializeField] private float gravity = -20f;
    [SerializeField] private bool invertVertical = false;
    [SerializeField] private bool invertHorizontal = false;

    [Header("Blade")]
    [SerializeField] private Transform[] blades;
    [SerializeField] private Vector3 bladeLocalAxis = Vector3.up;
    [SerializeField] private float bladeSpinSpeed = 720f;
    [SerializeField] private float minStickForSpin = 0.15f;

    [Header("Wheels")]
    [SerializeField] private Transform[] wheels;
    [SerializeField] private Vector3 wheelLocalAxis = Vector3.forward;
    [SerializeField] private float wheelSpinSpeed = 360f;

    [Header("Camera")]
    [SerializeField] private Transform cameraTransform;

    [Header("Debug")]
    [SerializeField] private bool debugLogs;
    [SerializeField] private float debugLogInterval = 0.35f;

    private CharacterController _cc;
    private Transform _cameraTransform;
    private float _verticalVelocity;
    private float _nextDebugLogTime;
    private Vector3 _lastPos;
    private float _lastYaw;
    private bool _bladesSpinning;
    private float _grassContactUntil;

    public bool AreBladesSpinning { get { return _bladesSpinning; } }
    public bool IsTouchingUncutGrass { get { return Time.time <= _grassContactUntil; } }
    public bool IsCuttingGrass { get { return _bladesSpinning && IsTouchingUncutGrass; } }

    public void ReportUncutGrassContact()
    {
        _grassContactUntil = Time.time + (Time.fixedDeltaTime * 2f);
    }

    public void ReportCuttingActivity()
    {
        _grassContactUntil = Time.time + 0.25f;
    }

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        if (blades == null || blades.Length == 0)
            blades = FindBladeTransforms();
        if (wheels == null || wheels.Length == 0)
            wheels = FindWheelTransforms();

        if (cameraTransform != null)
            _cameraTransform = cameraTransform;
        else if (Camera.main != null)
            _cameraTransform = Camera.main.transform;

        _lastPos = transform.position;
        _lastYaw = transform.eulerAngles.y;

        if (debugLogs)
        {
            Debug.Log(
                "[CutterCtrl] Awake mode=StickDirection(video) " +
                "moveSpeed=" + moveSpeed +
                " blades=" + (blades != null ? blades.Length : 0) +
                " wheels=" + (wheels != null ? wheels.Length : 0),
                this);
        }
    }

    private void Update()
    {
        float rawX = CF2Input.GetAxis(horizontalAxis);
        float rawY = CF2Input.GetAxis(verticalAxis);
        float x = invertHorizontal ? -rawX : rawX;
        float y = invertVertical ? -rawY : rawY;

        Vector2 stick = new Vector2(x, y);
        float mag = stick.magnitude;

        if (mag < stickDeadZone)
        {
            ApplyGravityOnly();
            _bladesSpinning = false;
            SpinBlades(0f);
            if (debugLogs && Time.unscaledTime >= _nextDebugLogTime)
            {
                _nextDebugLogTime = Time.unscaledTime + debugLogInterval;
                LogDeep(rawX, rawY, 0f, 0f, Vector3.zero);
            }
            return;
        }

        stick /= mag;
        Vector3 moveDir = ResolveMoveDirection(stick);
        if (moveDir.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRot,
                1f - Mathf.Exp(-rotationSmooth * Time.deltaTime));
        }

        Vector3 velocity = moveDir * (moveSpeed * mag);
        if (_cc.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;
        _verticalVelocity += gravity * Time.deltaTime;
        velocity.y = _verticalVelocity;
        _cc.Move(velocity * Time.deltaTime);

        float spin = mag >= minStickForSpin ? bladeSpinSpeed : 0f;
        _bladesSpinning = spin > 0f;
        SpinBlades(spin);
        SpinWheels(mag);

        if (debugLogs && Time.unscaledTime >= _nextDebugLogTime)
        {
            _nextDebugLogTime = Time.unscaledTime + debugLogInterval;
            LogDeep(rawX, rawY, x, y, moveDir);
        }
    }

    private Vector3 ResolveMoveDirection(Vector2 stick)
    {
        // Screen/camera-relative on XZ — matches fixed top-down video control.
        Transform cam = _cameraTransform;
        if (cam == null && Camera.main != null)
            cam = Camera.main.transform;

        if (cam == null)
            return new Vector3(stick.x, 0f, stick.y);

        Vector3 forward = cam.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = cam.right;
        right.y = 0f;
        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.right;
        right.Normalize();

        Vector3 dir = right * stick.x + forward * stick.y;
        if (dir.sqrMagnitude < 0.0001f)
            return Vector3.zero;
        return dir.normalized;
    }

    private void ApplyGravityOnly()
    {
        if (_cc.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;
        _verticalVelocity += gravity * Time.deltaTime;
        _cc.Move(new Vector3(0f, _verticalVelocity, 0f) * Time.deltaTime);
    }

    private void LogDeep(float rawX, float rawY, float x, float y, Vector3 moveDir)
    {
        Vector3 pos = transform.position;
        float yaw = transform.eulerAngles.y;
        Vector3 delta = pos - _lastPos;
        _lastPos = pos;
        _lastYaw = yaw;

        Debug.Log(
            "[CutterCtrl] raw=(" + rawX.ToString("F2") + "," + rawY.ToString("F2") + ") " +
            "adj=(" + x.ToString("F2") + "," + y.ToString("F2") + ") " +
            "dir=" + moveDir.ToString("F2") + " " +
            "spin=" + _bladesSpinning + " cutting=" + IsCuttingGrass + " " +
            "deltaXZ=" + new Vector3(delta.x, 0f, delta.z).magnitude.ToString("F2"),
            this);
    }

    private void SpinBlades(float degreesPerSecond)
    {
        if (blades == null || degreesPerSecond <= 0f)
            return;

        float step = degreesPerSecond * Time.deltaTime;
        for (int i = 0; i < blades.Length; i++)
        {
            Transform blade = blades[i];
            if (blade == null || !blade.gameObject.activeInHierarchy)
                continue;
            blade.Rotate(bladeLocalAxis, step, Space.Self);
        }
    }

    private void SpinWheels(float stickMag)
    {
        if (wheels == null || stickMag < stickDeadZone)
            return;

        float step = stickMag * wheelSpinSpeed * Time.deltaTime;
        for (int i = 0; i < wheels.Length; i++)
        {
            Transform wheel = wheels[i];
            if (wheel == null || !wheel.gameObject.activeInHierarchy)
                continue;
            wheel.Rotate(wheelLocalAxis, step, Space.Self);
        }
    }

    private Transform[] FindBladeTransforms()
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);
        int count = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (IsBladeName(all[i].name))
                count++;
        }

        Transform[] found = new Transform[count];
        int idx = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (!IsBladeName(all[i].name))
                continue;
            found[idx++] = all[i];
        }

        return found;
    }

    private Transform[] FindWheelTransforms()
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);
        int count = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (IsWheelName(all[i].name))
                count++;
        }

        Transform[] found = new Transform[count];
        int idx = 0;
        for (int i = 0; i < all.Length; i++)
        {
            if (!IsWheelName(all[i].name))
                continue;
            found[idx++] = all[i];
        }

        return found;
    }

    private static bool IsBladeName(string n)
    {
        return n == "Blade-1"
            || n == "Blade-2"
            || n.StartsWith("Blade-", System.StringComparison.Ordinal);
    }

    private static bool IsWheelName(string n)
    {
        return n == "Tire"
            || n.StartsWith("Tire", System.StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Wheel", System.StringComparison.OrdinalIgnoreCase);
    }
}
