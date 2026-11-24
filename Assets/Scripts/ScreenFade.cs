using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CanvasGroup))]
public class ScreenFade : MonoBehaviour
{
    public static ScreenFade Instance { get; private set; }
    [SerializeField] float fadeTime = 0.4f;
    CanvasGroup group;

    void Awake()
    {
        Instance = this;
        group = GetComponent<CanvasGroup>();
        group.alpha = 1f;               // start black
    }

    void Start() => StartCoroutine(Fade(0f));   // fade in

    public void LoadScene(string sceneName) => StartCoroutine(FadeAndLoad(sceneName));

    IEnumerator FadeAndLoad(string sceneName)
    {
        yield return Fade(1f);
        SceneManager.LoadScene(sceneName);
    }

    IEnumerator Fade(float target)
    {
        float start = group.alpha;
        for (float t = 0; t < fadeTime; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(start, target, t / fadeTime);
            yield return null;
        }
        group.alpha = target;
    }
}