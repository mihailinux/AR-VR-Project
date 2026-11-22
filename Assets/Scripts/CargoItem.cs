using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable), typeof(BoxCollider))]
public class CargoItem : MonoBehaviour
{
    [Header("Real-world values (meters / kg)")]
    public Vector3 realSizeM = new Vector3(0.8f, 0.9f, 1.2f);
    public float massKg = 850f;
    public float miniScale = 0.33333f;

    [Header("Detection")]
    public float formFitGapM = 0.08f;
    public float matCoverageNeeded = 0.33f;
    public float settleDelay = 0.6f;
    public float respawnBelowBedM = 0.5f;
    public float stillNeededS = 0.15f;

    [Header("Placement preview")]
    public Material previewMaterial;
    public float flushMagnetM = 0.01f;
    public float stackOverlapNeeded = 0.5f;

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
    public CargoItem stackedOn;

    public event System.Action Settled;

    public static readonly List<CargoItem> all = new List<CargoItem>();

    struct Foot { public float x, z, ex, ez, y, hy; public CargoItem who; }
    readonly List<Foot> feet = new List<Foot>();
    readonly List<Foot> level = new List<Foot>();

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
    CargoItem previewSupport;
    int placeMask;

    // Floating hover/grab spec tooltip
    Transform tooltipRoot;
    TextMeshPro tooltipText;
    float nextTooltipRefresh;

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
        CreateTooltip();
        Invoke(nameof(Evaluate), settleDelay);
    }

    void OnDestroy()
    {
        if (ghost != null) Destroy(ghost.gameObject);
        if (tooltipRoot != null) Destroy(tooltipRoot.gameObject);
    }

    void LockInPlace()
    {
        if (rb.isKinematic) return;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
    }

    bool HasBoxOnTop()
    {
        foreach (var o in all)
            if (o != this && o.stackedOn == this) return true;
        return false;
    }

    void Update()
    {
        bool covered = HasBoxOnTop();
        if (grab.enabled == covered) grab.enabled = !covered;

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

        if (!isHeld && Time.time > nextEval && rb.linearVelocity.sqrMagnitude < 0.01f)
        {
            Evaluate();
            nextEval = Time.time + 0.5f;
        }

        float bedY = BedFrame.Mini != null ? BedFrame.Mini.transform.position.y : 0f;
        if (transform.position.y < bedY - respawnBelowBedM)
            ReturnToStart();

        UpdateTooltip();
    }

    void ReturnToStart()
    {
        stackedOn = null;
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

    void LateUpdate()
    {
        if (!isHeld || ghost == null) return;

        bool overBed = ComputePlacement(out Vector3 pos, out Quaternion rot, out bool flush,
                                        out bool blocked, out CargoItem support);
        previewValid = overBed && !blocked;
        previewBlocked = overBed && blocked;
        previewPos = pos;
        previewRot = rot;
        previewSupport = support;

        if (!overBed) { ghost.gameObject.SetActive(false); return; }

        ghost.gameObject.SetActive(true);
        ghost.SetPositionAndRotation(pos, rot);
        Color c = blocked ? new Color(0.9f, 0.2f, 0.2f, 0.45f)
                : flush ? new Color(0.25f, 0.85f, 0.35f, 0.45f)
                : new Color(0.95f, 0.95f, 0.95f, 0.4f);
        if (ghostRend.material.color != c) ghostRend.material.color = c;
    }

    void OnHoverIn(HoverEnterEventArgs args) { isHovered = true; UpdateColor(); RefreshTooltipText(); }
    void OnHoverOut(HoverExitEventArgs args) { isHovered = false; UpdateColor(); }

    void OnGrabbed(SelectEnterEventArgs args)
    {
        stillTime = 0f;
        settledSent = false;
        isHeld = true;
        previewValid = false;
        previewBlocked = false;
        stackedOn = null;
        CancelInvoke(nameof(Evaluate));
        onBed = onMat = formFitFront = false;
        matCoverage = 0f;
        UpdateColor();
        RefreshTooltipText();
    }

    void OnReleased(SelectExitEventArgs args)
    {
        isHeld = false;
        if (ghost != null) ghost.gameObject.SetActive(false);
        if (previewValid) ApplyPlacement();
        else if (previewBlocked) ReturnToStart();
        StartCoroutine(SquareAfterRelease());
    }

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
        stackedOn = previewSupport;

        settledSent = true;
        Evaluate();
        Settled?.Invoke();
        CancelInvoke(nameof(Evaluate));
        Invoke(nameof(Evaluate), 0.3f);
    }

    IEnumerator SquareAfterRelease()
    {
        yield return new WaitForFixedUpdate();
        if (isHeld) yield break;

        if (previewValid)
        {
            rb.position = previewPos;
            rb.rotation = previewRot;
            LockInPlace();
        }
        else
        {
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

    // ---------- Floating Hover / Grab Tooltip ----------

    void CreateTooltip()
    {
        var go = new GameObject(name + "_Tooltip");
        tooltipRoot = go.transform;

        // Dark backing plate for crisp legibility in VR
        var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "Bg";
        Destroy(bg.GetComponent<Collider>());
        bg.transform.SetParent(tooltipRoot, false);
        bg.transform.localPosition = new Vector3(0f, 0f, 0.002f);
        bg.transform.localScale = new Vector3(0.32f, 0.13f, 1f);
        var bgMat = new Material(Shader.Find("Unlit/Color"));
        bgMat.color = new Color(0.08f, 0.09f, 0.12f);
        bg.GetComponent<Renderer>().material = bgMat;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(tooltipRoot, false);
        tooltipText = textGo.AddComponent<TextMeshPro>();
        tooltipText.fontSize = 0.24f;
        tooltipText.alignment = TextAlignmentOptions.Center;
        tooltipText.rectTransform.sizeDelta = new Vector2(0.30f, 0.12f);
        tooltipText.color = Color.white;

        go.SetActive(false);
    }

    void UpdateTooltip()
    {
        if (tooltipRoot == null) return;

        bool show = isHovered || isHeld;
        if (tooltipRoot.gameObject.activeSelf != show)
        {
            tooltipRoot.gameObject.SetActive(show);
            if (show) RefreshTooltipText();
        }
        if (!show) return;

        // Float slightly above the top center of the box
        tooltipRoot.position = new Vector3(transform.position.x, box.bounds.max.y + 0.085f, transform.position.z);

        if (Camera.main != null)
        {
            Vector3 toCam = tooltipRoot.position - Camera.main.transform.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.001f)
                tooltipRoot.rotation = Quaternion.LookRotation(toCam);
        }

        if (Time.time >= nextTooltipRefresh)
        {
            RefreshTooltipText();
            nextTooltipRefresh = Time.time + 0.25f;
        }
    }

    void RefreshTooltipText()
    {
        if (tooltipText == null) return;

        GetStrapSummary(out int strapCount, out float totalDaN);
        string statusLine;

        if (isHeld)
            statusLine = "<color=#E879F9>Moving Cargo...</color>";
        else if (onSupply)
            statusLine = "<color=#94A3B8>Ready on Supply Table</color>";
        else if (!onBed)
            statusLine = "<color=#F87171>Off Truck Bed</color>";
        else
        {
            List<string> tags = new List<string>();
            if (formFitFront) tags.Add("<color=#4ADE80>Form-Fit ✓</color>");
            if (strapCount > 0)
            {
                string col = totalDaN >= 200f ? "#4ADE80" : "#FACC15";
                tags.Add($"<color={col}>{strapCount} Strap ({totalDaN:0} daN)</color>");
            }
            if (stackedOn != null) tags.Add($"<color=#38BDF8>On: {stackedOn.name}</color>");

            statusLine = tags.Count > 0
                ? string.Join(" | ", tags)
                : "<color=#FB923C>Unsecured on Bed</color>";
        }

        tooltipText.text = $"<b>{gameObject.name}</b>\n"
                         + $"<color=#CBD5E1>{massKg:0} kg  •  {realSizeM.x:0.0}×{realSizeM.y:0.0}×{realSizeM.z:0.0} m</color>\n"
                         + statusLine;
    }

    public void GetStrapSummary(out int strapCount, out float totalDaN)
    {
        strapCount = 0;
        totalDaN = 0f;
        foreach (var s in MiniStrapRoll.allStraps)
        {
            if (s != null && s.isPlacedOnBed && s.securedCargo.Contains(this))
            {
                strapCount++;
                totalDaN += s.currentPretensionN * 0.1f;
            }
        }
    }

    // ---------- Placement Preview ----------

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

    static bool IsFree(float cx, float cz, float ex, float ez, List<Foot> others)
    {
        foreach (var o in others)
            if (Mathf.Abs(cx - o.x) < ex + o.ex - 0.0002f && Mathf.Abs(cz - o.z) < ez + o.ez - 0.0002f)
                return false;
        return true;
    }

    static void SupportRange(float lowerC, float lowerExt, float ext, out float min, out float max)
    {
        float s = lowerExt - ext;
        if (s > 0f) { min = lowerC - s; max = lowerC + s; }
        else { min = max = lowerC; }
    }

    static bool FindFreeSpot(ref float cx, ref float cz, float ex, float ez,
                             float minX, float maxX, float minZ, float maxZ, List<Foot> others)
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

    bool ComputePlacement(out Vector3 pos, out Quaternion rot, out bool flush,
                          out bool blocked, out CargoItem support)
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

        float yaw = BaseYaw + Mathf.Round((HeldYaw() - BaseYaw) / 90f) * 90f;
        rot = Quaternion.Euler(0f, yaw, 0f);
        bool swapped = Mathf.Abs(Mathf.RoundToInt(Mathf.DeltaAngle(BaseYaw, yaw) / 90f)) % 2 == 1;
        float extX = swapped ? half.z : half.x;
        float extZ = swapped ? half.x : half.z;

        Vector3 local = bed.InverseTransformPoint(transform.position);
        bool overBed = Mathf.Abs(local.x) < halfW + extX
                    && Mathf.Abs(local.z) < halfL + extZ
                    && local.y > -0.05f;
        if (!overBed) return false;

        float bedMaxX = Mathf.Max(0f, halfW - extX - 0.0005f);
        float bedMaxZ = Mathf.Max(0f, halfL - extZ - 0.0005f);
        float cx = Mathf.Clamp(local.x, -bedMaxX, bedMaxX);
        float cz = Mathf.Clamp(local.z, -bedMaxZ, bedMaxZ);

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

        float baseY = 0f;
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
                blocked = true;
            }
            else
            {
                float nx = Mathf.Clamp(cx, minX, maxX);
                float nz = Mathf.Clamp(cz, minZ, maxZ);
                if (Mathf.Abs(nx - cx) > 0.0001f || Mathf.Abs(nz - cz) > 0.0001f) flush = true;
                cx = nx; cz = nz;
            }
        }

        level.Clear();
        float topY = baseY + 2f * half.y;
        foreach (var f in feet)
            if (f.y - f.hy < topY - 0.0005f && f.y + f.hy > baseY + 0.0005f) level.Add(f);

        if (!blocked && !IsFree(cx, cz, extX, extZ, level))
            if (!FindFreeSpot(ref cx, ref cz, extX, extZ, minX, maxX, minZ, maxZ, level))
                blocked = true;

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
                    float gap = (o.z - o.ez) - (cz + extZ);
                    if (gap >= 0f && gap <= flushMagnetM) { cz += Mathf.Max(0f, gap - 0.0005f); flush = true; }
                }
                if (overlapZ)
                {
                    float gapR = (o.x - o.ex) - (cx + extX);
                    if (gapR >= 0f && gapR <= flushMagnetM) { cx += Mathf.Max(0f, gapR - 0.0005f); flush = true; }
                    float gapL = (cx - extX) - (o.x + o.ex);
                    if (gapL >= 0f && gapL <= flushMagnetM) { cx -= Mathf.Max(0f, gapL - 0.0005f); flush = true; }
                }
            }
            cx = Mathf.Clamp(cx, minX, maxX);
            cz = Mathf.Clamp(cz, minZ, maxZ);
        }

        pos = bed.TransformPoint(new Vector3(cx, baseY + half.y, cz));
        return true;
    }

    // ---------- Detection ----------

    public void Evaluate()
    {
        Vector3 h = transform.lossyScale * 0.5f;
        float bottom = box.bounds.min.y;
        int bedSamples = 0, matSamples = 0, supplySamples = 0;

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
            return false;
        }
        return false;
    }

    void UpdateColor()
    {
        if (rend == null) return;
        Color c = isHeld ? new Color(0.9f, 0.2f, 0.9f)
                : isHovered ? new Color(1f, 0.9f, 0.2f)
                : onSupply ? new Color(0.7f, 0.7f, 0.75f)
                : !onBed ? new Color(0.85f, 0.25f, 0.25f)
                : formFitFront ? new Color(0.3f, 0.75f, 0.35f)
                : onMat ? new Color(0.3f, 0.5f, 0.9f)
                : new Color(0.85f, 0.7f, 0.5f);
        rend.material.color = c;
    }
}