using UnityEngine;

[DisallowMultipleComponent]
public class WheatCuttable : MonoBehaviour
{
    private static readonly Vector3 DefaultScale = new Vector3(2f, 2f, 2f);

    private bool _isCut;
    private bool _isAnimating;
    private Collider[] _colliders;
    private Renderer[] _renderers;
    private Vector3 _baseScale;
    private WheatFieldManager _field;

    public bool IsCut { get { return _isCut; } }
    public Vector3 BaseScale
    {
        get
        {
            if (_baseScale.sqrMagnitude < 0.0001f)
                _baseScale = DefaultScale;
            return _baseScale;
        }
    }

    private void Awake()
    {
        _colliders = GetComponentsInChildren<Collider>();
        _renderers = GetComponentsInChildren<Renderer>();
        _baseScale = transform.localScale.sqrMagnitude > 0.0001f
            ? transform.localScale
            : DefaultScale;
        _field = GetComponentInParent<WheatFieldManager>();
    }

    public void Cut(float shrinkDuration)
    {
        if (_isCut || _isAnimating)
            return;
        _isCut = true;

        for (int i = 0; i < _colliders.Length; i++)
        {
            if (_colliders[i] != null)
                _colliders[i].enabled = false;
        }

        if (shrinkDuration <= 0.01f)
        {
            FinishCut();
            return;
        }

        StartCoroutine(ShrinkAndFinish(shrinkDuration));
    }

    public void BeginGrow(float duration, float delay)
    {
        if (_isAnimating)
            StopAllCoroutines();

        gameObject.SetActive(true);
        _isCut = true;
        _isAnimating = true;
        transform.localScale = Vector3.zero;

        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
                _renderers[i].enabled = true;
        }

        for (int i = 0; i < _colliders.Length; i++)
        {
            if (_colliders[i] != null)
                _colliders[i].enabled = false;
        }

        StartCoroutine(GrowRoutine(duration, delay));
    }

    private System.Collections.IEnumerator ShrinkAndFinish(float duration)
    {
        _isAnimating = true;
        float t = 0f;
        Vector3 start = transform.localScale;
        Vector3 end = new Vector3(start.x * 0.15f, 0.01f, start.z * 0.15f);
        while (t < duration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / duration);
            a = a * a;
            transform.localScale = Vector3.LerpUnclamped(start, end, a);
            yield return null;
        }

        FinishCut();
    }

    private System.Collections.IEnumerator GrowRoutine(float duration, float delay)
    {
        float wait = 0f;
        while (wait < delay)
        {
            wait += Time.deltaTime;
            yield return null;
        }

        Vector3 target = BaseScale;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01(t / duration);
            a = EaseOutBack(a);
            transform.localScale = target * a;
            yield return null;
        }

        transform.localScale = target;
        for (int i = 0; i < _colliders.Length; i++)
        {
            if (_colliders[i] != null)
                _colliders[i].enabled = true;
        }

        _isCut = false;
        _isAnimating = false;
    }

    private void FinishCut()
    {
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
                _renderers[i].enabled = false;
        }

        transform.localScale = BaseScale;
        _isAnimating = false;

        if (_field == null)
            _field = GetComponentInParent<WheatFieldManager>();

        if (_field != null)
        {
            // Stay active (renderers off) so field can grow blades back after full clear.
            gameObject.SetActive(true);
            _field.NotifyWheatCut(this);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float inv = x - 1f;
        return 1f + c3 * inv * inv * inv + c1 * inv * inv;
    }
}
