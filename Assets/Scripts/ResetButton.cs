using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRSimpleInteractable))]
public class ResetButton : MonoBehaviour
{
    public BoxSpawner spawner;
    XRSimpleInteractable inter;

    void Awake() { inter = GetComponent<XRSimpleInteractable>(); }
    void OnEnable() { inter.selectEntered.AddListener(OnPressed); }
    void OnDisable() { inter.selectEntered.RemoveListener(OnPressed); }

    void OnPressed(SelectEnterEventArgs args)
    {
        if (spawner != null) spawner.SpawnAll();   // clears everything and puts new boxes on the table
    }
}