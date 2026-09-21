using UnityEngine;

[DisallowMultipleComponent]
public class WheatFieldManager : MonoBehaviour
{
    [SerializeField] private float growDuration = 0.55f;
    [SerializeField] private float staggerDelay = 0.008f;
    [SerializeField] private float respawnPause = 0.35f;
    [SerializeField] private bool debugLogs = true;

    private WheatCuttable[] _wheats;
    private int _remaining;
    private bool _respawning;

    private void Awake()
    {
        CacheWheats();
    }

    private void CacheWheats()
    {
        _wheats = GetComponentsInChildren<WheatCuttable>(true);
        _remaining = 0;
        for (int i = 0; i < _wheats.Length; i++)
        {
            if (_wheats[i] != null && !_wheats[i].IsCut)
                _remaining++;
        }

        if (debugLogs)
            Debug.Log("[WheatField] cached=" + _wheats.Length + " remaining=" + _remaining + " baseScale check first=" +
                      (_wheats.Length > 0 ? _wheats[0].BaseScale.ToString() : "n/a"), this);
    }

    public void NotifyWheatCut(WheatCuttable wheat)
    {
        if (_respawning || wheat == null)
            return;

        _remaining--;
        if (_remaining < 0)
            _remaining = 0;

        if (debugLogs)
            Debug.Log("[WheatField] cut -> remaining=" + _remaining, this);

        if (_remaining <= 0)
            StartCoroutine(RespawnAllRoutine());
    }

    private System.Collections.IEnumerator RespawnAllRoutine()
    {
        if (_respawning)
            yield break;

        _respawning = true;

        // Let every FinishCut settle before growing.
        float pause = 0f;
        while (pause < respawnPause)
        {
            pause += Time.deltaTime;
            yield return null;
        }

        if (_wheats == null || _wheats.Length == 0)
            CacheWheats();

        if (debugLogs)
            Debug.Log("[WheatField] respawn scale-up start count=" + _wheats.Length, this);

        for (int i = 0; i < _wheats.Length; i++)
        {
            WheatCuttable wheat = _wheats[i];
            if (wheat == null)
                continue;
            wheat.BeginGrow(growDuration, i * staggerDelay);
        }

        float totalWait = growDuration + (_wheats.Length * staggerDelay) + 0.05f;
        float waited = 0f;
        while (waited < totalWait)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        _remaining = 0;
        for (int i = 0; i < _wheats.Length; i++)
        {
            if (_wheats[i] != null && !_wheats[i].IsCut)
                _remaining++;
        }

        _respawning = false;

        if (debugLogs)
            Debug.Log("[WheatField] respawn done remaining=" + _remaining, this);
    }
}
