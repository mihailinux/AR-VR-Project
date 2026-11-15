using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(LineRenderer))]
public class PullableStrap : MonoBehaviour
{
    [Header("1. Strap Parts")]
    public Transform leftAnchor;               // Fixed start point on the left
    public XRGrabInteractable grabbableHook;   // The hook the player pulls
    public XRSocketInteractor rightSocket;     // The target socket on the right

    [Header("2. Crate Slot Being Strapped")]
    public XRSocketInteractor crateSlot;       // Slot_Rear_Crate (or Slot_Front_Crate)

    [Header("3. Status (Read by Validator)")]
    public bool isStrappedDown = false;

    private LineRenderer line;
    private Vector3 hookStartPos;
    private Quaternion hookStartRot;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.useWorldSpace = true;

        if (grabbableHook != null)
        {
            hookStartPos = grabbableHook.transform.position;
            hookStartRot = grabbableHook.transform.rotation;
        }
    }

    void OnEnable()
    {
        if (rightSocket != null)
        {
            rightSocket.selectEntered.AddListener(OnHookSnapped);
            rightSocket.selectExited.AddListener(OnHookRemoved);
        }
        if (grabbableHook != null)
        {
            grabbableHook.selectExited.AddListener(OnPlayerLetGoOfHook);
        }
    }

    void OnDisable()
    {
        if (rightSocket != null)
        {
            rightSocket.selectEntered.RemoveListener(OnHookSnapped);
            rightSocket.selectExited.RemoveListener(OnHookRemoved);
        }
        if (grabbableHook != null)
        {
            grabbableHook.selectExited.RemoveListener(OnPlayerLetGoOfHook);
        }
    }

    private void OnHookSnapped(SelectEnterEventArgs args)
    {
        isStrappedDown = true;
    }

    private void OnHookRemoved(SelectExitEventArgs args)
    {
        isStrappedDown = false;
    }

    private void OnPlayerLetGoOfHook(SelectExitEventArgs args)
    {
        // Wait a tiny fraction of a second to see if it snapped into the right socket;
        // if not, snap the hook back to the left anchor like a retractable seatbelt!
        Invoke(nameof(CheckIfHookNeedsReset), 0.1f);
    }

    private void CheckIfHookNeedsReset()
    {
        if (rightSocket != null && !rightSocket.hasSelection && grabbableHook != null && !grabbableHook.isSelected)
        {
            grabbableHook.transform.SetPositionAndRotation(hookStartPos, hookStartRot);
        }
    }

    void Update()
    {
        if (leftAnchor == null || grabbableHook == null) return;

        // Check if there is a crate sitting inside this slot AND the hook is pulled past the crate
        bool cratePresent = crateSlot != null && crateSlot.hasSelection;
        bool hookPulledOver = grabbableHook.transform.position.x > leftAnchor.position.x + 0.15f;

        if (cratePresent && (hookPulledOver || isStrappedDown))
        {
            // Wrap the strap neatly over the top-left and top-right edges of the crate!
            Transform crateTransform = crateSlot.GetOldestInteractableSelected().transform;
            Collider crateCol = crateTransform.GetComponent<Collider>();

            if (crateCol != null)
            {
                Bounds b = crateCol.bounds;
                Vector3 topLeftEdge = new Vector3(b.min.x, b.max.y + 0.01f, crateTransform.position.z);
                Vector3 topRightEdge = new Vector3(b.max.x, b.max.y + 0.01f, crateTransform.position.z);

                line.positionCount = 4;
                line.SetPosition(0, leftAnchor.position);
                line.SetPosition(1, topLeftEdge);
                line.SetPosition(2, topRightEdge);
                line.SetPosition(3, grabbableHook.transform.position);
                return;
            }
        }

        // Otherwise, draw a straight line from the left anchor to the hook in your hand
        line.positionCount = 2;
        line.SetPosition(0, leftAnchor.position);
        line.SetPosition(1, grabbableHook.transform.position);
    }
}