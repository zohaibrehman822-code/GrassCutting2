using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class GrassCollectUI : MonoBehaviour
{
    public static GrassCollectUI Instance { get; private set; }

    [Header("References")]
    [SerializeField] private Canvas canvas;
    [SerializeField] private RectTransform bucketTarget;

    [Tooltip("Prefab containing ONLY the flying grass Image.")]
    [SerializeField] private RectTransform grassIconPrefab;

    [Header("Animation")]
    [SerializeField] private float animationDuration = 0.5f;
    [SerializeField] private float startScale = 0.8f;
    [SerializeField] private float endScale = 0.25f;

    public Ease easeEffect;

    private Camera mainCamera;

    private void Awake()
    {
        Instance = this;
        mainCamera = Camera.main;
    }

    public void ShowGrassCollected(Vector3 worldPosition)
    {
        if (canvas == null ||
            bucketTarget == null ||
            grassIconPrefab == null)
        {
            return;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        // Convert 3D grass position to screen position.
        Vector3 screenPosition =
            mainCamera.WorldToScreenPoint(worldPosition);

        // Grass is behind the camera.
        if (screenPosition.z <= 0f)
        {
            return;
        }

        // Create the icon ONLY when grass is collected.
        RectTransform icon =
            Instantiate(grassIconPrefab, canvas.transform);

        // Make sure it is visible above the other UI.
        icon.SetAsLastSibling();

        // Put it at the grass position.
        icon.position = screenPosition;

        // Starting size.
        icon.localScale =
            Vector3.one * startScale;

        // Make sure any previous tweens are gone.
        icon.DOKill();

        Sequence sequence = DOTween.Sequence();

        // Fly toward bucket.
        sequence.Join(
            icon.DOMove(
                bucketTarget.position,
                animationDuration
            )
            .SetEase(easeEffect)
        );

        // Shrink while flying.
        sequence.Join(
            icon.DOScale(
                Vector3.one * endScale,
                animationDuration
            )
            .SetEase(easeEffect)
        );

        sequence.OnComplete(() =>
        {
            Destroy(icon.gameObject);
        });
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}