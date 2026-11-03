using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class TruckLoadValidator : MonoBehaviour
{
    [Header("1. Front Truck Slots (Heavy + Mat)")]
    public XRSocketInteractor frontMatSlot;
    public XRSocketInteractor frontCrateSlot;
    public XRSocketInteractor frontStrapSlot;

    [Header("2. Rear Truck Slots (Light + Strap)")]
    public XRSocketInteractor rearMatSlot;
    public XRSocketInteractor rearCrateSlot;
    public XRSocketInteractor rearStrapSlot;

    [Header("3. Grabbable Objects")]
    public XRGrabInteractable heavyCrate;
    public XRGrabInteractable lightCrate;
    public XRGrabInteractable antiSlipMat;
    public XRGrabInteractable strapObject;

    [Header("4. Check Button & Status Light")]
    public XRSimpleInteractable checkButton;
    public MeshRenderer statusLightRenderer;

    [Header("5. Light Colors")]
    public Color waitingColor = Color.gray;
    public Color passColor = Color.green;
    public Color failColor = Color.red;

    private XRSocketInteractor[] allSlots;

    void Awake()
    {
        allSlots = new XRSocketInteractor[] {
            frontMatSlot, frontCrateSlot, frontStrapSlot,
            rearMatSlot, rearCrateSlot, rearStrapSlot
        };
    }

    void Start()
    {
        if (statusLightRenderer != null)
            statusLightRenderer.material.color = waitingColor;
    }

    void OnEnable()
    {
        if (checkButton != null)
            checkButton.selectEntered.AddListener(OnCheckButtonPressed);

        // Whenever anything is placed or removed in any slot, reset light to grey
        foreach (var slot in allSlots)
        {
            if (slot != null)
            {
                slot.selectEntered.AddListener(ResetLightToGrey);
                slot.selectExited.AddListener(ResetLightToGrey);
            }
        }
    }

    void OnDisable()
    {
        if (checkButton != null)
            checkButton.selectEntered.RemoveListener(OnCheckButtonPressed);

        foreach (var slot in allSlots)
        {
            if (slot != null)
            {
                slot.selectEntered.RemoveListener(ResetLightToGrey);
                slot.selectExited.RemoveListener(ResetLightToGrey);
            }
        }
    }

    private void ResetLightToGrey(BaseInteractionEventArgs args)
    {
        if (statusLightRenderer != null)
            statusLightRenderer.material.color = waitingColor;
    }

    private void OnCheckButtonPressed(SelectEnterEventArgs args)
    {
        EvaluateTruckLoad();
    }

    public void EvaluateTruckLoad()
    {
        // Check Front: Needs Heavy Crate + Anti-Slip Mat (and NO Strap)
        bool heavyInFront = HasSpecificObject(frontCrateSlot, heavyCrate);
        bool matInFront   = HasSpecificObject(frontMatSlot, antiSlipMat);
        bool noStrapFront = !frontStrapSlot.hasSelection;

        // Check Rear: Needs Light Crate + Strap Object (and NO Mat)
        bool lightInRear  = HasSpecificObject(rearCrateSlot, lightCrate);
        bool strapInRear  = HasSpecificObject(rearStrapSlot, strapObject);
        bool noMatRear    = !rearMatSlot.hasSelection;

        // Pass ONLY if every single condition is met!
        if (heavyInFront && matInFront && noStrapFront && lightInRear && strapInRear && noMatRear)
        {
            statusLightRenderer.material.color = passColor;
            Debug.Log("PASSED! Heavy+Mat in Front, Light+Strap in Rear.");
        }
        else
        {
            statusLightRenderer.material.color = failColor;
            Debug.Log($"FAILED! HeavyFront:{heavyInFront}, MatFront:{matInFront}, LightRear:{lightInRear}, StrapRear:{strapInRear}");
        }
    }

    private bool HasSpecificObject(XRSocketInteractor slot, XRGrabInteractable expectedObj)
    {
        if (slot == null || expectedObj == null || !slot.hasSelection) return false;
        return slot.GetOldestInteractableSelected().transform == expectedObj.transform;
    }
}