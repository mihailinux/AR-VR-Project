using UnityEngine;

public class MenuController : MonoBehaviour
{
    [SerializeField] GameObject panelMain;
    [SerializeField] GameObject panelLevels;
    [SerializeField] string gameSceneName = "SampleScene";   // change to your scene name

    public void ShowMain()   { panelMain.SetActive(true);  panelLevels.SetActive(false); }
    public void ShowLevels() { panelMain.SetActive(false); panelLevels.SetActive(true);  }

    public void SetPhase(int phase) => SessionState.Phase = (Phase)phase;

    public void StartLevel(int level)
    {
        SessionState.Level = level;
        ScreenFade.Instance.LoadScene(gameSceneName);
    }

    public void Play() => StartLevel(1);

    public void Quit() => Application.Quit();
}