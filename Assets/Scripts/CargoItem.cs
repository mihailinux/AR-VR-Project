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
    public float stackOverlapNeeded = 0.5f;    // NEW: share of the box that must be over another box to stack

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
    public CargoItem stackedOn;                // NEW: the box this one sits on (null = on the bed floor)

    public event System.Action Settled;

    // all boxes in the scene, so a box can see where the others are
    static readonly List<CargoItem> all = new List<CargoItem>();

    // footprint and height of a box on the bed, in bed coordinates
    struct Foot { public float x, z, ex, ez, y, hy; public CargoItem who; }   // CHANGED: y, hy, who added
    readonly List<Foot> feet = new List<Foot>();
    readonly List<Foot> level = new List<Foot>();                              // NEW

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
    bool previewBlocked;
    Vector3 previewPos;
    Quaternion previewRot;
    CargoItem previewSupport;                  // NEW
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
        all.Add(this);
        grab.selectEntered.AddListener(OnGrabbed);
        grab.selectExited.AddListener(OnReleased);
        grab.hoverEntered.AddListener(OnHoverIn);
        grab.hoverExited.AddListener(OnHoverOut);
    }

    void OnDisable()
    {
        all.Remove(this);
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

    // NEW: is another box sitting on this one?
    bool HasBoxOnTop()
    {
        foreach (var o in all)
            if (o != this && o.stackedOn == this) return true;
        return false;
    }

    void Update()
    {
        // NEW: a box with something on top cannot be picked up
        bool covered = HasBoxOnTop();
        if (grab.enabled == covered) grab.enabled = !covered;

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
            ReturnToStart();
    }

    // back to the spot on the supply table
    void ReturnToStart()
    {
        stackedOn = null;                                        // NEW
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

        bool overBed = ComputePlacement(out Vector3 pos, out Quaternion rot, out bool flush,
                                        out bool blocked, out CargoItem support);   // CHANGED
        previewValid = overBed && !blocked;
        previewBlocked = overBed && blocked;
        previewPos = pos;
        previewRot = rot;
        previewSupport = support;                                // NEW

        if (!overBed) { ghost.gameObject.SetActive(false); return; }

        ghost.gameObject.SetActive(true);
        ghost.SetPositionAndRotation(pos, rot);
        Color c = blocked ? new Color(0.9f, 0.2f, 0.2f, 0.45f)  // red: no free spot
                : flush ? new Color(0.25f, 0.85f, 0.35f, 0.45f)  // green: flush / clipped to an edge
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
        previewBlocked = false;
        stackedOn = null;                                        // NEW: it is in the hand now
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
        else if (previewBlocked) ReturnToStart();    // no free spot on the bed
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
        stackedOn = previewSupport;                              // NEW

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

    // is this footprint free of all the other boxes?
    static bool IsFree(float cx, float cz, float ex, float ez, List<Foot> others)
    {
        foreach (var o in others)
            if (Mathf.Abs(cx - o.x) < ex + o.ex - 0.0002f && Mathf.Abs(cz - o.z) < ez + o.ez - 0.0002f)
                return false;
        return true;
    }

    // NEW: where the center of a box may be so it rests fully on the box below
    // (centered if it is bigger than the box below)
    static void SupportRange(float lowerC, float lowerExt, float ext, out float min, out float max)
    {
        float s = lowerExt - ext;
        if (s > 0f) { min = lowerC - s; max = lowerC + s; }
        else { min = max = lowerC; }
    }

    // find the closest free spot next to the boxes in the way (within the allowed range)
    static bool FindFreeSpot(ref float cx, ref float cz, float ex, float ez,
                             float minX, float maxX, float minZ, float maxZ, List<Foot> others)   // CHANGED
    {
        const float eps = 0.0005f, tol = 0.0006f;
        float bestD = float.MaxValue, bx = cx, bz = cz;
        bool found = false;

        foreach (var o in others)
        {
            float[] px = { o.x + o.ex + ex + eps, o.x - o.ex - ex - eps, cx, cx };
            float[] pz = { cz, cz, o.z + o.ez + ez + eps, o.z - o.ez - ez - eps };
            for (int k = 0; k < 4; k++)
            {
                if (px[k] < minX - tol || px[k] > maxX + tol || pz[k] < minZ - tol || pz[k] > maxZ + tol) continue;
                if (!IsFree(px[k], pz[k], ex, ez, others)) continue;
                float d = (px[k] - cx) * (px[k] - cx) + (pz[k] - cz) * (pz[k] - cz);
                if (d < bestD) { bestD = d; bx = px[k]; bz = pz[k]; found = true; }
            }
        }
        if (found) { cx = bx; cz = bz; }
        return found;
    }

    // returns false if no part of the box is above the bed
    bool ComputePlacement(out Vector3 pos, out Quaternion rot, out bool flush,
                          out bool blocked, out CargoItem support)                    // CHANGED
    {
        pos = transform.position;
        rot = transform.rotation;
        flush = false;
        blocked = false;
        support = null;
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

        // 3) keep the center inside the bed
        float bedMaxX = Mathf.Max(0f, halfW - extX - 0.0005f);
        float bedMaxZ = Mathf.Max(0f, halfL - extZ - 0.0005f);
        float cx = Mathf.Clamp(local.x, -bedMaxX, bedMaxX);
        float cz = Mathf.Clamp(local.z, -bedMaxZ, bedMaxZ);

        // 4) boxes already on the bed
        feet.Clear();
        foreach (var o in all)
        {
            if (o == this || !o.placedOnBed || o.isHeld) continue;
            Vector3 oh = o.transform.lossyScale * 0.5f;
            bool aligned = Mathf.Abs(Vector3.Dot(o.transform.right, bed.right)) > 0.7f;
            Vector3 ol = bed.InverseTransformPoint(o.transform.position);
            feet.Add(new Foot
            {
                x = ol.x, z = ol.z,
                ex = aligned ? oh.x : oh.z, ez = aligned ? oh.z : oh.x,
                y = ol.y, hy = oh.y, who = o
            });
        }

        // 5) NEW: stacking. If enough of the box is over another box, sit on top of the highest one.
        float baseY = 0f;                         // height of the surface it lands on
        float minX = -bedMaxX, maxX = bedMaxX, minZ = -bedMaxZ, maxZ = bedMaxZ;
        int bi = -1;
        float bestTop = float.MinValue;
        float area = 4f * extX * extZ;
        for (int i = 0; i < feet.Count; i++)
        {
            var f = feet[i];
            float ox = Mathf.Max(0f, Mathf.Min(cx + extX, f.x + f.ex) - Mathf.Max(cx - extX, f.x - f.ex));
            float oz = Mathf.Max(0f, Mathf.Min(cz + extZ, f.z + f.ez) - Mathf.Max(cz - extZ, f.z - f.ez));
            if (ox * oz / area < stackOverlapNeeded) continue;
            float top = f.y + f.hy;
            if (top > bestTop) { bestTop = top; bi = i; }
        }

        if (bi >= 0)
        {
            var f = feet[bi];
            support = f.who;
            baseY = bestTop;

            SupportRange(f.x, f.ex, extX, out float sMinX, out float sMaxX);
            SupportRange(f.z, f.ez, extZ, out float sMinZ, out float sMaxZ);
            minX = Mathf.Max(minX, sMinX); maxX = Mathf.Min(maxX, sMaxX);
            minZ = Mathf.Max(minZ, sMinZ); maxZ = Mathf.Min(maxZ, sMaxZ);

            if (minX > maxX || minZ > maxZ)
            {
                blocked = true;                   // would hang over the edge of the bed
            }
            else
            {
                float nx = Mathf.Clamp(cx, minX, maxX);
                float nz = Mathf.Clamp(cz, minZ, maxZ);
                if (Mathf.Abs(nx - cx) > 0.0001f || Mathf.Abs(nz - cz) > 0.0001f) flush = true;   // clipped to the edge
                cx = nx; cz = nz;
            }
        }

        // 6) other boxes at the same height as where it will sit
        level.Clear();
        float topY = baseY + 2f * half.y;
        foreach (var f in feet)
            if (f.y - f.hy < topY - 0.0005f && f.y + f.hy > baseY + 0.0005f) level.Add(f);

        // inside another box at that height? slide to the side
        if (!blocked && !IsFree(cx, cz, extX, extZ, level))
            if (!FindFreeSpot(ref cx, ref cz, extX, extZ, minX, maxX, minZ, maxZ, level))
                blocked = true;

        // 7) flush magnet: front wall and boxes at the same height
        if (!blocked)
        {
            float target = halfL - extZ - 0.0005f;
            if (target <= maxZ + 0.00001f && target >= cz && target - cz <= flushMagnetM)
            {
                cz = target;
                flush = true;
            }

            foreach (var o in level)
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
            cx = Mathf.Clamp(cx, minX, maxX);
            cz = Mathf.Clamp(cz, minZ, maxZ);
        }

        // 8) final position: on the bed floor, or on top of the box below
        pos = bed.TransformPoint(new Vector3(cx, baseY + half.y, cz));
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

                // NEW: resting on another box that is on the bed counts as on the bed
                var under = hit.collider.GetComponentInParent<CargoItem>();
                if (under != null && under != this && under.placedOnBed) bed = true;
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