using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRSimpleInteractable))]
public class ResetButton : MonoBehaviour
{
    public BoxSpawner spawner;
    public StrapSpawner strapSpawner;
    XRSimpleInteractable inter;

    void Awake()
    {
        inter = GetComponent<XRSimpleInteractable>();
        if (spawner == null) spawner = FindFirstObjectByType<BoxSpawner>();
        if (strapSpawner == null) strapSpawner = FindFirstObjectByType<StrapSpawner>();
    }

    void OnEnable() { inter.selectEntered.AddListener(OnPressed); }
    void OnDisable() { inter.selectEntered.RemoveListener(OnPressed); }

    void OnPressed(SelectEnterEventArgs args)
    {
        if (spawner != null) spawner.SpawnAll();
        if (strapSpawner != null) strapSpawner.ResetAllStraps();
    }
}