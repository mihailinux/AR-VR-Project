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
    public float respawnBelowBedM = 0.5f;
    public float stillNeededS = 0.15f;

    [Header("Placement preview")]
    public Material previewMaterial;           // transparent material
    public float flushMagnetM = 0.01f;         // closes gaps up to 1 cm (mini)

    [Header("Debug color (optional)")]
    public Renderer rend;

    [Header("Result (read-only)")]
    public bool isHeld;
    public bool isHovered;
    public bool onBed;
    public bool onMat;
    public float matCoverage;
    public bool formFitFront;
    public bool onSupply;
    public bool placedOnBed;

    public event System.Action Settled;

    // NEW: all boxes in the scene, so a box can see where the others are
    static readonly List<CargoItem> all = new List<CargoItem>();

    // NEW: footprint of a box on the bed, in bed coordinates
    struct Foot { public float x, z, ex, ez; }
    readonly List<Foot> feet = new List<Foot>();

    Rigidbody rb;
    XRGrabInteractable grab;
    BoxCollider box;
    Vector3 startPos;
    Quaternion startRot;
    float nextEval;
    float stillTime;
    bool settledSent;

    Transform ghost;
    Renderer ghostRend;
    bool previewValid;
    bool previewBlocked;                       // NEW: over the bed but no free spot
    Vector3 previewPos;
    Quaternion previewRot;
    int placeMask;

    Vector3 FrontDir => BedFrame.Mini != null ? BedFrame.Mini.Forward : Vector3.forward;
    float BaseYaw => BedFrame.Mini != null ? BedFrame.Mini.Yaw : 0f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        grab = GetComponent<XRGrabInteractable>();
        box = GetComponent<BoxCollider>();
        transform.localScale = realSizeM * miniScale;
        placeMask = LayerMask.GetMask("Truck", "Cargo");
    }

    // the spawner sets the real size and mass right after creating the box
    public void Configure(Vector3 realSize, float mass)
    {
        realSizeM = realSize;
        massKg = mass;
        transform.localScale = realSizeM * miniScale;
    }

    void OnEnable()
    {
        all.Add(this);                                           // NEW
        grab.selectEntered.AddListener(OnGrabbed);
        grab.selectExited.AddListener(OnReleased);
        grab.hoverEntered.AddListener(OnHoverIn);
        grab.hoverExited.AddListener(OnHoverOut);
    }

    void OnDisable()
    {
        all.Remove(this);                                        // NEW
        grab.selectEntered.RemoveListener(OnGrabbed);
        grab.selectExited.RemoveListener(OnReleased);
        grab.hoverEntered.RemoveListener(OnHoverIn);
        grab.hoverExited.RemoveListener(OnHoverOut);
    }

    void Start()
    {
        startPos = transform.position;
        startRot = transform.rotation;
        CreateGhost();
        Invoke(nameof(Evaluate), settleDelay);
    }

    void OnDestroy()
    {
        if (ghost != null) Destroy(ghost.gameObject);
    }

    // freeze the box where it is (only grabbing moves it again)
    void LockInPlace()
    {
        if (rb.isKinematic) return;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
    }

    void Update()
    {
        // tell listeners (the twin) when the box has stopped moving, then fix it in place
        if (!isHeld)
        {
            bool still = rb.linearVelocity.sqrMagnitude < 0.0025f
                      && rb.angularVelocity.sqrMagnitude < 0.01f;
            stillTime = still ? stillTime + Time.deltaTime : 0f;
            if (stillTime == 0f) settledSent = false;
            if (stillTime >= stillNeededS && !settledSent)
            {
                settledSent = true;
                LockInPlace();
                Settled?.Invoke();
            }
        }

        // re-check twice a second while resting
        if (!isHeld && Time.time > nextEval && rb.linearVelocity.sqrMagnitude < 0.01f)
        {
            Evaluate();
            nextEval = Time.time + 0.5f;
        }

        // fell off the table: back to the start spot
        float bedY = BedFrame.Mini != null ? BedFrame.Mini.transform.position.y : 0f;
        if (transform.position.y < bedY - respawnBelowBedM)
            ReturnToStart();                                     // CHANGED
    }

    // NEW: back to the spot on the supply table
    void ReturnToStart()
    {
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = startPos;
        rb.rotation = startRot;
        transform.SetPositionAndRotation(startPos, startRot);
        Physics.SyncTransforms();
        CancelInvoke(nameof(Evaluate));
        Invoke(nameof(Evaluate), settleDelay);
    }

    // update the ghost while the box is in the hand
    void LateUpdate()
    {
        if (!isHeld || ghost == null) return;

        bool overBed = ComputePlacement(out Vector3 pos, out Quaternion rot, out bool flush, out bool blocked);
        previewValid = overBed && !blocked;
        previewBlocked = overBed && blocked;                     // NEW
        previewPos = pos;
        previewRot = rot;

        if (!overBed) { ghost.gameObject.SetActive(false); return; }

        ghost.gameObject.SetActive(true);
        ghost.SetPositionAndRotation(pos, rot);
        Color c = blocked ? new Color(0.9f, 0.2f, 0.2f, 0.45f)  // red: no free spot
                : flush ? new Color(0.25f, 0.85f, 0.35f, 0.45f)  // green: flush
                : new Color(0.95f, 0.95f, 0.95f, 0.4f);
        if (ghostRend.material.color != c) ghostRend.material.color = c;
    }

    void OnHoverIn(HoverEnterEventArgs args) { isHovered = true; UpdateColor(); }
    void OnHoverOut(HoverExitEventArgs args) { isHovered = false; UpdateColor(); }

    void OnGrabbed(SelectEnterEventArgs args)
    {
        stillTime = 0f;
        settledSent = false;
        isHeld = true;
        previewValid = false;
        previewBlocked = false;                                  // NEW
        CancelInvoke(nameof(Evaluate));
        onBed = onMat = formFitFront = false;   // placedOnBed stays, so the twin stays visible
        matCoverage = 0f;
        UpdateColor();
    }

    void OnReleased(SelectExitEventArgs args)
    {
        isHeld = false;
        if (ghost != null) ghost.gameObject.SetActive(false);
        if (previewValid) ApplyPlacement();          // jump to the ghost right now
        else if (previewBlocked) ReturnToStart();    // NEW: no free spot on the bed
        StartCoroutine(SquareAfterRelease());
    }

    // jump to the previewed spot and stay there
    void ApplyPlacement()
    {
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        rb.position = previewPos;
        rb.rotation = previewRot;
        transform.SetPositionAndRotation(previewPos, previewRot);
        Physics.SyncTransforms();
        LockInPlace();

        settledSent = true;
        Evaluate();
        Settled?.Invoke();                       // twin updates immediately
        CancelInvoke(nameof(Evaluate));
        Invoke(nameof(Evaluate), 0.3f);
    }

    IEnumerator SquareAfterRelease()
    {
        yield return new WaitForFixedUpdate();   // let XRI finish releasing first
        if (isHeld) yield break;

        if (previewValid)
        {
            rb.position = previewPos;            // XRI may have touched the body while letting go
            rb.rotation = previewRot;
            LockInPlace();
        }
        else
        {
            // not placed on the bed: straighten it and let it fall
            rb.isKinematic = false;
            float yaw = BaseYaw + Mathf.Round((rb.rotation.eulerAngles.y - BaseYaw) / 90f) * 90f;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.rotation = Quaternion.Euler(0f, yaw, 0f);
            CancelInvoke(nameof(Evaluate));
            Invoke(nameof(Evaluate), settleDelay);
        }
        previewValid = false;
        previewBlocked = false;
    }

    // ---------- placement preview ----------

    void CreateGhost()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name + "_Preview";
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = transform.lossyScale;
        ghostRend = go.GetComponent<Renderer>();
        if (previewMaterial != null) ghostRend.material = new Material(previewMaterial);
        ghostRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.SetActive(false);
        ghost = go.transform;
    }

    // yaw of the held box, even when the hand tilts it
    float HeldYaw()
    {
        Vector3 f = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (f.sqrMagnitude < 0.25f)
        {
            Vector3 r = Vector3.ProjectOnPlane(transform.right, Vector3.up);
            return Mathf.Atan2(r.x, r.z) * Mathf.Rad2Deg - 90f;
        }
        return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    static float ClampAxis(float v, float halfBed, float ext)
    {
        float lim = halfBed - ext - 0.0005f;
        return lim <= 0f ? 0f : Mathf.Clamp(v, -lim, lim);
    }

    // NEW: is this footprint free of all the other boxes?
    static bool IsFree(float cx, float cz, float ex, float ez, List<Foot> others)
    {
        foreach (var o in others)
            if (Mathf.Abs(cx - o.x) < ex + o.ex - 0.0002f && Mathf.Abs(cz - o.z) < ez + o.ez - 0.0002f)
                return false;
        return true;
    }

    // NEW: find the closest free spot next to the boxes in the way (inside the bed)
    static bool FindFreeSpot(ref float cx, ref float cz, float ex, float ez,
                             float halfW, float halfL, List<Foot> others)
    {
        const float eps = 0.0005f;
        float limX = Mathf.Max(0f, halfW - ex) + 0.0006f;
        float limZ = Mathf.Max(0f, halfL - ez) + 0.0006f;
        float bestD = float.MaxValue, bx = cx, bz = cz;
        bool found = false;

        foreach (var o in others)
        {
            float[] px = { o.x + o.ex + ex + eps, o.x - o.ex - ex - eps, cx, cx };
            float[] pz = { cz, cz, o.z + o.ez + ez + eps, o.z - o.ez - ez - eps };
            for (int k = 0; k < 4; k++)
            {
                if (Mathf.Abs(px[k]) > limX || Mathf.Abs(pz[k]) > limZ) continue;   // off the bed
                if (!IsFree(px[k], pz[k], ex, ez, others)) continue;                 // another box there
                float d = (px[k] - cx) * (px[k] - cx) + (pz[k] - cz) * (pz[k] - cz);
                if (d < bestD) { bestD = d; bx = px[k]; bz = pz[k]; found = true; }
            }
        }
        if (found) { cx = bx; cz = bz; }
        return found;
    }

    // returns false if no part of the box is above the bed
    bool ComputePlacement(out Vector3 pos, out Quaternion rot, out bool flush, out bool blocked)
    {
        pos = transform.position;
        rot = transform.rotation;
        flush = false;
        blocked = false;
        if (BedFrame.Mini == null) return false;

        Transform bed = BedFrame.Mini.transform;
        Vector3 half = transform.lossyScale * 0.5f;
        float halfW = BedFrame.Mini.HalfWidth;
        float halfL = BedFrame.Mini.HalfLength;

        // 1) straighten: nearest 90 degrees relative to the bed
        float yaw = BaseYaw + Mathf.Round((HeldYaw() - BaseYaw) / 90f) * 90f;
        rot = Quaternion.Euler(0f, yaw, 0f);
        bool swapped = Mathf.Abs(Mathf.RoundToInt(Mathf.DeltaAngle(BaseYaw, yaw) / 90f)) % 2 == 1;
        float extX = swapped ? half.z : half.x;   // half size along the bed width
        float extZ = swapped ? half.x : half.z;   // half size along the bed length

        // 2) is any part of the box above the bed?
        Vector3 local = bed.InverseTransformPoint(transform.position);
        bool overBed = Mathf.Abs(local.x) < halfW + extX
                    && Mathf.Abs(local.z) < halfL + extZ
                    && local.y > -0.05f;
        if (!overBed) return false;

        // 3) keep it inside the bed edges
        float cx = ClampAxis(local.x, halfW, extX);
        float cz = ClampAxis(local.z, halfL, extZ);

        // 4) footprints of the boxes already on the bed (NEW)
        feet.Clear();
        foreach (var o in all)
        {
            if (o == this || !o.placedOnBed || o.isHeld) continue;
            Vector3 oh = o.transform.lossyScale * 0.5f;
            bool aligned = Mathf.Abs(Vector3.Dot(o.transform.right, bed.right)) > 0.7f;
            Vector3 ol = bed.InverseTransformPoint(o.transform.position);
            feet.Add(new Foot { x = ol.x, z = ol.z, ex = aligned ? oh.x : oh.z, ez = aligned ? oh.z : oh.x });
        }

        // 5) inside another box? slide to the side (NEW)
        if (!IsFree(cx, cz, extX, extZ, feet))
            if (!FindFreeSpot(ref cx, ref cz, extX, extZ, halfW, halfL, feet))
                blocked = true;

        // 6) flush magnet: front wall, boxes in front, boxes at the sides (CHANGED: now in 2D)
        if (!blocked)
        {
            if (halfL - (cz + extZ) <= flushMagnetM) { cz = halfL - extZ - 0.0005f; flush = true; }
            foreach (var o in feet)
            {
                bool overlapX = Mathf.Abs(cx - o.x) < extX + o.ex - 0.0002f;
                bool overlapZ = Mathf.Abs(cz - o.z) < extZ + o.ez - 0.0002f;
                if (overlapX)
                {
                    float gap = (o.z - o.ez) - (cz + extZ);          // box in front of us
                    if (gap >= 0f && gap <= flushMagnetM) { cz += Mathf.Max(0f, gap - 0.0005f); flush = true; }
                }
                if (overlapZ)
                {
                    float gapR = (o.x - o.ex) - (cx + extX);         // box on the right
                    if (gapR >= 0f && gapR <= flushMagnetM) { cx += Mathf.Max(0f, gapR - 0.0005f); flush = true; }
                    float gapL = (cx - extX) - (o.x + o.ex);         // box on the left
                    if (gapL >= 0f && gapL <= flushMagnetM) { cx -= Mathf.Max(0f, gapL - 0.0005f); flush = true; }
                }
            }
        }

        // 7) always on the bed floor (CHANGED: no more stacking)
        pos = bed.TransformPoint(new Vector3(cx, half.y, cz));
        return true;
    }

    // ---------- detection ----------

    public void Evaluate()
    {
        Vector3 h = transform.lossyScale * 0.5f;
        float bottom = box.bounds.min.y;
        int bedSamples = 0, matSamples = 0, supplySamples = 0;

        // 3x3 grid of rays under the box
        for (int i = -1; i <= 1; i++)
        for (int j = -1; j <= 1; j++)
        {
            Vector3 p = transform.position
                      + transform.right * (i * h.x * 0.8f)
                      + transform.forward * (j * h.z * 0.8f);
            p.y = bottom + 0.02f;

            bool bed = false, mat = false, supply = false;
            foreach (var hit in Physics.RaycastAll(p, Vector3.down, 0.06f, ~0, QueryTriggerInteraction.Collide))
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.collider.CompareTag("TruckBed")) bed = true;
                if (hit.collider.CompareTag("AntiSlipMat")) mat = true;
                if (hit.collider.CompareTag("SupplyTable")) supply = true;
            }
            if (bed) bedSamples++;
            if (mat) matSamples++;
            if (supply) supplySamples++;
        }

        onBed = bedSamples >= 5;
        onSupply = supplySamples >= 5;
        placedOnBed = onBed;
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

        var hits = Physics.RaycastAll(transform.position, f, dist, placeMask, QueryTriggerInteraction.Collide);
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
        Color c = isHeld ? new Color(0.9f, 0.2f, 0.9f)                   // magenta: in the hand
                : isHovered ? new Color(1f, 0.9f, 0.2f)                  // yellow: hand is touching it
                : onSupply ? new Color(0.7f, 0.7f, 0.75f)                // grey: on the supply table
                : !onBed ? new Color(0.85f, 0.25f, 0.25f)                // red: not on the bed
                : formFitFront ? new Color(0.3f, 0.75f, 0.35f)           // green: form fit to front
                : onMat ? new Color(0.3f, 0.5f, 0.9f)                    // blue: on an anti-slip mat
                : new Color(0.85f, 0.7f, 0.5f);                          // brown: just on the bed
        rend.material.color = c;
    }
}