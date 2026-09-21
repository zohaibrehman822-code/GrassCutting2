using UnityEngine;
using TMPro;

[DisallowMultipleComponent]
public class CuttingDistanceTracker : MonoBehaviour
{
    [SerializeField] private CutterJoystickController cutter;
    [SerializeField] private TextMeshProUGUI distanceText;
    [SerializeField] private string suffix = " m";
    [SerializeField] private bool resetOnAwake = true;
    [SerializeField] private bool debugLogs = true;
    [SerializeField] private float debugLogInterval = 0.35f;

    private float _distanceMeters;
    private int _displayedMeters = -1;
    private Vector3 _lastPos;
    private bool _hasLastPos;
    private float _nextDebugLogTime;

    public float DistanceMeters { get { return _distanceMeters; } }

    private void Awake()
    {
        if (cutter == null)
            TryGetComponent(out cutter);
        if (cutter == null)
            cutter = GetComponentInParent<CutterJoystickController>();

        if (resetOnAwake)
            _distanceMeters = 0f;

        _hasLastPos = false;
        RefreshText(true);

        if (debugLogs)
            Debug.Log("[CutDist] Awake cutter=" + (cutter != null) + " text=" + (distanceText != null), this);
    }

    private void LateUpdate()
    {
        if (cutter == null)
            return;

        Vector3 pos = cutter.transform.position;
        if (!_hasLastPos)
        {
            _lastPos = pos;
            _hasLastPos = true;
            return;
        }

        float dx = pos.x - _lastPos.x;
        float dz = pos.z - _lastPos.z;
        float step = Mathf.Sqrt(dx * dx + dz * dz);

        bool spinning = cutter.AreBladesSpinning;
        bool touching = cutter.IsTouchingUncutGrass;
        bool cutting = cutter.IsCuttingGrass;

        if (cutting && step > 0f)
            _distanceMeters += step;

        if (debugLogs && Time.unscaledTime >= _nextDebugLogTime)
        {
            _nextDebugLogTime = Time.unscaledTime + debugLogInterval;
            Debug.Log(
                "[CutDist] spin=" + spinning +
                " touchGrass=" + touching +
                " cutting=" + cutting +
                " step=" + step.ToString("F3") +
                " dist=" + _distanceMeters.ToString("F2") +
                " ui=" + _displayedMeters + " m",
                this);
        }

        _lastPos = pos;
        RefreshText(false);
    }

    public void ResetDistance()
    {
        _distanceMeters = 0f;
        _displayedMeters = -1;
        RefreshText(true);
    }

    private void RefreshText(bool force)
    {
        if (distanceText == null)
            return;

        int meters = Mathf.FloorToInt(_distanceMeters);
        if (!force && meters == _displayedMeters)
            return;

        _displayedMeters = meters;
        distanceText.SetText("{0} m", meters);
    }
}
