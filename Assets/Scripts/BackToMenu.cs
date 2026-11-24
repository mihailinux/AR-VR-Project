using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRSimpleInteractable))]
public class BackToMenu : MonoBehaviour
{
    [SerializeField] string menuSceneName = "MainMenu";

    void Awake()
    {
        var interactable = GetComponent<XRSimpleInteractable>();
        interactable.selectEntered.AddListener(_ => ScreenFade.Instance.LoadScene(menuSceneName));
    }
}