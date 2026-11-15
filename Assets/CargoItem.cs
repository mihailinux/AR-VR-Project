using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(Rigidbody), typeof(Collider), typeof(XRGrabInteractable))]
public class CargoItem : MonoBehaviour
{
    [Header("Cargo Specs (From School CSV / JSON)")]
    public string itemName = "Euro Pallet";
    public float massKg = 500f;
    public float baseFriction = 0.3f;
    public float matFriction = 0.6f;

    [Header("Form-Fit Settings")]
    public float maxAllowedGapMeters = 0.08f; // Max 8cm gap to count as Form Fit

    [Header("Live Status (Read-Only in Inspector)")]
    public bool isOnTruckBed = false;
    public bool isOnAntiSlipMat = false;
    public bool hasFormFit = false;
    public float currentFriction = 0.3f;

    private Collider col;
    private Rigidbody rb;
    private XRGrabInteractable grab;

    void Awake()
    {
        col = GetComponent<Collider>();
        rb = GetComponent<Rigidbody>();
        grab = GetComponent<XRGrabInteractable>();

        // Keep physics stable when stacking or pushing boxes
        rb.mass = Mathf.Clamp(massKg * 0.02f, 1f, 20f); // Scaled for comfortable VR lifting
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    void OnEnable()
    {
        grab.selectExited.AddListener(OnBoxReleased);
    }

    void OnDisable()
    {
        grab.selectExited.RemoveListener(OnBoxReleased);
    }

    private void OnBoxReleased(SelectExitEventArgs args)
    {
        // Assist for VR beginners: gently square up the box's Y rotation to the nearest 90 degrees
        // so they can push boxes flush against walls without frustrating 3-degree crooked gaps!
        Vector3 euler = transform.eulerAngles;
        euler.x = 0f;
        euler.z = 0f;
        euler.y = Mathf.Round(euler.y / 90f) * 90f;
        transform.eulerAngles = euler;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    void FixedUpdate()
    {
        EvaluatePlacement();
    }

    public void EvaluatePlacement()
    {
        Bounds b = col.bounds;

        // 1. Raycast DOWN from bottom corners & center to check for Anti-Slip Mat or Truck Bed
        isOnTruckBed = false;
        isOnAntiSlipMat = false;

        Vector3[] bottomCheckPoints = new Vector3[]
        {
            new Vector3(b.center.x, b.min.y + 0.02f, b.center.z),
            new Vector3(b.min.x + 0.05f, b.min.y + 0.02f, b.min.z + 0.05f),
            new Vector3(b.max.x - 0.05f, b.min.y + 0.02f, b.max.z - 0.05f)
        };

        int matHits = 0;
        foreach (var pt in bottomCheckPoints)
        {
            if (Physics.Raycast(pt, Vector3.down, out RaycastHit hit, 0.12f))
            {
                if (hit.collider.CompareTag("AntiSlipMat"))
                {
                    matHits++;
                    isOnTruckBed = true;
                }
                else if (hit.collider.CompareTag("TruckBed") || hit.collider.GetComponent<CargoItem>() != null)
                {
                    isOnTruckBed = true;
                }
            }
        }

        // Counts as on a mat if at least the center/corners rest on an Anti-Slip Mat
        isOnAntiSlipMat = (matHits >= 1);
        currentFriction = isOnAntiSlipMat ? matFriction : baseFriction;

        // 2. Raycast FORWARD (+Z toward truck cab) to check Form Fit (Formschluss)
        hasFormFit = false;
        Vector3 frontFaceCenter = new Vector3(b.center.x, b.center.y, b.max.z - 0.01f);

        if (Physics.Raycast(frontFaceCenter, Vector3.forward, out RaycastHit frontHit, maxAllowedGapMeters + 0.02f))
        {
            // Valid Form Fit if touching the Front Wall OR touching another box that itself has Form Fit!
            if (frontHit.collider.CompareTag("FrontWall"))
            {
                hasFormFit = true;
            }
            else if (frontHit.collider.TryGetComponent<CargoItem>(out CargoItem frontBox))
            {
                hasFormFit = frontBox.hasFormFit;
            }
        }
    }
}