using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class BladeCutTrigger : MonoBehaviour
{
    [SerializeField] private string wheatTag = "SmallYellow";
    [SerializeField] private ParticleSystem cutParticles;
    [SerializeField] private int emitCount = 16;
    [SerializeField] private float cutShrinkDuration = 0.12f;
    [SerializeField] private bool debugLogs;

    private Transform _cutParticlesTransform;
    private ParticleSystem[] _cutParticleSystems;
    private CutterJoystickController _cutter;
    private float _nextDebugLogTime;

    private void Awake()
    {
        _cutter = GetComponentInParent<CutterJoystickController>();

        if (cutParticles == null)
            cutParticles = FindCutParticles(transform.root);

        CacheParticleSystems();
    }

    private void CacheParticleSystems()
    {
        if (cutParticles == null)
            return;

        _cutParticlesTransform = cutParticles.transform;
        _cutParticleSystems = cutParticles.GetComponentsInChildren<ParticleSystem>(true);

        for (int i = 0; i < _cutParticleSystems.Length; i++)
        {
            if (!_cutParticleSystems[i].gameObject.activeSelf)
                _cutParticleSystems[i].gameObject.SetActive(true);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleWheatOverlap(other, true);
    }

    private void OnTriggerStay(Collider other)
    {
        HandleWheatOverlap(other, false);
    }

    private void HandleWheatOverlap(Collider other, bool isEnter)
    {
        if (other == null || !other.CompareTag(wheatTag))
            return;

        WheatCuttable wheat = other.GetComponent<WheatCuttable>();
        if (wheat == null)
            wheat = other.GetComponentInParent<WheatCuttable>();

        if (wheat == null)
            return;

        if (!wheat.IsCut && _cutter != null)
            _cutter.ReportUncutGrassContact();

        if (_cutter != null && !_cutter.AreBladesSpinning)
        {
            if (debugLogs && Time.unscaledTime >= _nextDebugLogTime)
            {
                _nextDebugLogTime = Time.unscaledTime + 0.5f;
                Debug.Log("[BladeCut] overlap wheat but blades idle enter=" + isEnter, this);
            }
            return;
        }

        if (wheat.IsCut)
            return;

        Vector3 hitPoint = other.ClosestPoint(transform.position);
        PlayCutParticles(hitPoint);
        wheat.Cut(cutShrinkDuration);

        if (_cutter != null)
            _cutter.ReportCuttingActivity();

        if (debugLogs)
            Debug.Log("[BladeCut] CUT " + wheat.name + " enter=" + isEnter + " cuttingGrass=" + _cutter.IsCuttingGrass, this);
    }

    private void PlayCutParticles(Vector3 pos)
    {
        if (cutParticles == null)
            return;

        if (_cutParticleSystems == null || _cutParticleSystems.Length == 0)
            CacheParticleSystems();

        _cutParticlesTransform.position = pos + Vector3.up * 0.15f;
        cutParticles.gameObject.SetActive(true);

        for (int i = 0; i < _cutParticleSystems.Length; i++)
        {
            ParticleSystem ps = _cutParticleSystems[i];
            if (ps == null)
                continue;
            if (!ps.gameObject.activeSelf)
                ps.gameObject.SetActive(true);
            if (emitCount > 0)
                ps.Emit(emitCount);
            else
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
            }
        }
    }

    private static ParticleSystem FindCutParticles(Transform root)
    {
        if (root == null)
            return null;

        ParticleSystem particelle = null;
        ParticleSystem cutGrass = null;
        ParticleSystem nova = null;
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            string n = all[i].name;
            ParticleSystem ps;
            if (n == "Particelle")
            {
                if (all[i].TryGetComponent(out ps))
                    particelle = ps;
            }
            else if (n == "CutGrassParticles")
            {
                if (all[i].TryGetComponent(out ps))
                    cutGrass = ps;
            }
            else if (n == "ExplosionNovaBlue")
            {
                if (all[i].TryGetComponent(out ps))
                    nova = ps;
            }
        }

        if (particelle != null)
            return particelle;
        if (cutGrass != null)
            return cutGrass;
        return nova;
    }
}
