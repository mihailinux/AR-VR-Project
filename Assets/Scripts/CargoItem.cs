using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable), typeof(BoxCollider))]
public class CargoItem : MonoBehaviour
{
    [Header("Real-world values (meters / kg)")]
    public Vector3 realSizeM = new Vector3(0.8f, 0.9f, 1.2f); // X width, Y height, Z length
    public float massKg = 850f;
    public float miniScale = 0.33333f;


    [Header("Detection")]
    public float formFitGapM = 0.08f;
    public float matCoverageNeeded = 0.33f;
    public float settleDelay = 0.6f;
    public float respawnBelowBedM = 0.5f;          // CHANGED (was respawnBelowY)

    [Header("Debug color (optional)")]
    public Renderer rend;

    [Header("Result (read-only)")]
    public bool isHeld;
    public bool onBed;
    public bool onMat;
    public float matCoverage;
    public bool formFitFront;

    Rigidbody rb;
    XRGrabInteractable grab;
    BoxCollider box;
    Vector3 startPos;
    Quaternion startRot;

    Vector3 FrontDir => BedFrame.Mini != null ? BedFrame.Mini.Forward : Vector3.forward;
    float BaseYaw => BedFrame.Mini != null ? BedFrame.Mini.Yaw : 0f;
    float nextEval;                                                  // NEW (next to the other fields)    
    public event System.Action Settled;                          // NEW
    public float stillNeededS = 0.15f;   // NEW
    float stillTime;                     // NEW
    bool settledSent;                    // NEW

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        grab = GetComponent<XRGrabInteractable>();
        box = GetComponent<BoxCollider>();
        transform.localScale = realSizeM * miniScale;   // size comes from the real size
    }

    void OnEnable()
    {
        grab.selectEntered.AddListener(OnGrabbed);
        grab.selectExited.AddListener(OnReleased);
    }

    void OnDisable()
    {
        grab.selectEntered.RemoveListener(OnGrabbed);
        grab.selectExited.RemoveListener(OnReleased);
    }

    void Start()
    {
        startPos = transform.position;
        startRot = transform.rotation;
        Invoke(nameof(Evaluate), settleDelay);
    }

        void Update()
    {
        if (!isHeld)                                                                      // NEW block
        {
            bool still = rb.linearVelocity.sqrMagnitude < 0.0025f
                      && rb.angularVelocity.sqrMagnitude < 0.01f;
            stillTime = still ? stillTime + Time.deltaTime : 0f;
            if (stillTime == 0f) settledSent = false;
            if (stillTime >= stillNeededS && !settledSent)
            {
                settledSent = true;
                Settled?.Invoke();
            }
        }

        if (!isHeld && Time.time > nextEval && rb.linearVelocity.sqrMagnitude < 0.01f)
        {
            Evaluate();
            nextEval = Time.time + 0.5f;
        }

        float bedY = BedFrame.Mini != null ? BedFrame.Mini.transform.position.y : 0f;
        if (transform.position.y < bedY - respawnBelowBedM)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = startPos;
            rb.rotation = startRot;
            CancelInvoke(nameof(Evaluate));
            Invoke(nameof(Evaluate), settleDelay);
        }
    }

    void OnGrabbed(SelectEnterEventArgs args)
    {
        stillTime = 0f; settledSent = false;
        isHeld = true;
        CancelInvoke(nameof(Evaluate));
        onBed = onMat = formFitFront = false;
        matCoverage = 0f;
        UpdateColor();
    }

    void OnReleased(SelectExitEventArgs args)
    {
        isHeld = false;
        StartCoroutine(SquareAfterRelease());
    }

    // Snap yaw to the nearest 90 degrees (relative to the bed), remove tilt and speed
    IEnumerator SquareAfterRelease()
    {
        yield return new WaitForFixedUpdate();   // let XRI finish releasing first
        if (isHeld) yield break;

        float yaw = rb.rotation.eulerAngles.y;
        float snapped = BaseYaw + Mathf.Round((yaw - BaseYaw) / 90f) * 90f;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.rotation = Quaternion.Euler(0f, snapped, 0f);

        CancelInvoke(nameof(Evaluate));
        Invoke(nameof(Evaluate), settleDelay);   // wait until the box has settled
    }

    public void Evaluate()
    {
        Vector3 h = transform.lossyScale * 0.5f;
        float bottom = box.bounds.min.y;
        int bedSamples = 0, matSamples = 0;

        // 3x3 grid of rays under the box
        for (int i = -1; i <= 1; i++)
        for (int j = -1; j <= 1; j++)
        {
            Vector3 p = transform.position
                      + transform.right * (i * h.x * 0.8f)
                      + transform.forward * (j * h.z * 0.8f);
            p.y = bottom + 0.02f;

            bool bed = false, mat = false;
            foreach (var hit in Physics.RaycastAll(p, Vector3.down, 0.06f, ~0, QueryTriggerInteraction.Collide))
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.collider.CompareTag("TruckBed")) bed = true;
                if (hit.collider.CompareTag("AntiSlipMat")) mat = true;
            }
            if (bed) bedSamples++;
            if (mat) matSamples++;
        }

        onBed = bedSamples >= 5;
        matCoverage = matSamples / 9f;
        onMat = onBed && matCoverage >= matCoverageNeeded;
        formFitFront = onBed && CheckFormFitFront(new HashSet<CargoItem>());
        UpdateColor();
    }

    // Touching the front wall, or touching another box that does (within the gap)
    bool CheckFormFitFront(HashSet<CargoItem> visited)
    {
        if (!visited.Add(this)) return false;

        Vector3 f = FrontDir;
        Vector3 h = transform.lossyScale * 0.5f;
        float ext = Mathf.Abs(Vector3.Dot(f, transform.right)) * h.x
                  + Mathf.Abs(Vector3.Dot(f, transform.forward)) * h.z;
        float dist = ext + formFitGapM * miniScale;

        var hits = Physics.RaycastAll(transform.position, f, dist, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.collider.CompareTag("FrontWall")) return true;
            var other = hit.collider.GetComponentInParent<CargoItem>();
            if (other != null) return other.CheckFormFitFront(visited);
            return false;   // something else is in the way
        }
        return false;
    }

    void UpdateColor()
    {
        if (rend == null) return;
        Color c = !onBed ? new Color(0.85f, 0.25f, 0.25f)         // red: not on the bed
                : formFitFront ? new Color(0.3f, 0.75f, 0.35f)    // green: form fit to front
                : onMat ? new Color(0.3f, 0.5f, 0.9f)             // blue: on an anti-slip mat
                : new Color(0.85f, 0.7f, 0.5f);                   // brown: just on the bed
        rend.material.color = c;
        
    }
}