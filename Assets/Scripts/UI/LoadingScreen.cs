using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

public class LoadingScreen : MonoBehaviour
{
    [Header("Loading UI")]
    [SerializeField] private Image loadingImage;
    [SerializeField] private TMP_Text loadingText;

    [Header("Loading Settings")]
    [SerializeField] private float loadingDuration = 5f;

    [Header("Scene Settings")]
    [SerializeField] private int sceneBuildIndex = 1;

    private void Start()
    {
        StartCoroutine(LoadScene());
    }

    private IEnumerator LoadScene()
    {
        float timer = 0f;

        loadingImage.fillAmount = 0f;
        loadingText.text = "Loading 0%";

        while (timer < loadingDuration)
        {
            timer += Time.deltaTime;

            float progress = Mathf.Clamp01(timer / loadingDuration);
            int percent = Mathf.RoundToInt(progress * 100f);

            loadingImage.fillAmount = progress;
            loadingText.text = percent + "%";

            yield return null;
        }

        // Make sure it reaches exactly 100%
        loadingImage.fillAmount = 1f;
        loadingText.text = "100%";

        yield return new WaitForSeconds(0.2f);

        // Load Scene Build Index 2
        SceneManager.LoadScene(sceneBuildIndex);
    }
}