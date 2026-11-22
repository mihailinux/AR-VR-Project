using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable))]
public class MiniStrapRoll : MonoBehaviour
{
    [HideInInspector] public StrapSpawner spawner;

    [Header("Materials")]
    public Material strapMaterial;
    public Material previewMaterial;

    [Header("Strap Dimensions (Mini Scale)")]
    public float miniBeltWidth = 0.035f;
    public float hoverOffset = 0.02f;
    public float sideClearance = 0.012f;
    public int raySamples = 40;

    [Header("Forearm Z-Spin Settings")]
    public float wristRollThreshold = 28f; // Forearm spin angle (degrees) to snap into diagonal mode

    [Header("Ratchet & Tension Specs")]
    public float maxPretensionN = 5000f;   // 500 daN
    public float forcePerStrokeN = 1000f;
    public float currentPretensionN = 0f;

    [Header("Live Status (Read by Validator)")]
    public bool isPlacedOnBed = false;
    public bool isDiagonalMode = false;
    public List<CargoItem> securedCargo = new List<CargoItem>();

    public static readonly List<MiniStrapRoll> allStraps = new List<MiniStrapRoll>();

    private Rigidbody rb;
    private XRGrabInteractable grab;
    private MeshRenderer rollRenderer;
    private Collider rollCollider;
    private bool notifiedSpawner = false;
    private IXRSelectInteractor holdingHand;

    private Vector3 localStartAnchor;
    private Vector3 localEndAnchor;

    private LineRenderer ghostLine;
    private GameObject placedRoot;
    private LineRenderer miniLine;
    private Transform crankPivot;
    private TextMeshPro tensionText;
    private IXRSelectInteractor crankHand;
    private float prevHandAngle;
    private float strokeAccumulator;

    private GameObject twinRoot;
    private LineRenderer twinLine;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        grab = GetComponent<XRGrabInteractable>();
        rollRenderer = GetComponent<MeshRenderer>();
        rollCollider = GetComponent<Collider>();

        CreateGhostLine();
    }

    void OnEnable()
    {
        allStraps.Add(this);
        grab.selectEntered.AddListener(OnRollGrabbed);
        grab.selectExited.AddListener(OnRollReleased);
    }

    void OnDisable()
    {
        allStraps.Remove(this);
        grab.selectEntered.RemoveListener(OnRollGrabbed);
        grab.selectExited.RemoveListener(OnRollReleased);
    }

    private void OnDestroy()
    {
        if (ghostLine != null) Destroy(ghostLine.gameObject);
        if (placedRoot != null) Destroy(placedRoot);
        if (twinRoot != null) Destroy(twinRoot);
    }

    // ---------- 1. GRAB, RESPAWN & FOREARM Z-SPIN DIAGONAL ----------

    private void CreateGhostLine()
    {
        var go = new GameObject(name + "_Ghost");
        ghostLine = go.AddComponent<LineRenderer>();
        ghostLine.useWorldSpace = true;
        ghostLine.alignment = LineAlignment.View;
        ghostLine.numCornerVertices = 4;
        ghostLine.startWidth = ghostLine.endWidth = miniBeltWidth;
        if (previewMaterial != null)
        {
            ghostLine.material = new Material(previewMaterial);
            ghostLine.material.color = new Color(0.25f, 0.85f, 0.35f, 0.5f);
        }
        go.SetActive(false);
    }

    private void OnRollGrabbed(SelectEnterEventArgs args)
    {
        holdingHand = args.interactorObject;

        if (!notifiedSpawner && spawner != null)
        {
            notifiedSpawner = true;
            spawner.NotifyRollTaken(this);
        }
    }

    private void OnRollReleased(SelectExitEventArgs args)
    {
        if (ghostLine != null) ghostLine.gameObject.SetActive(false);

        if (EvaluatePlacement(out Vector3 lStart, out Vector3 lEnd, out bool diag))
        {
            holdingHand = null;
            PlaceStrapAt(lStart, lEnd, diag);
        }
        else
        {
            holdingHand = null;
            Destroy(gameObject);
        }
    }

    // Measures forearm pronation/supination (spinning around controller Z-axis like a fan)
    // Returns -1 (left spin), 0 (upright/straight), or +1 (right spin) — no in-betweens
    private int GetWristRollSign()
    {
        Transform wrist = holdingHand != null ? holdingHand.transform : transform;
        Vector3 fwd = wrist.forward.normalized;

        Vector3 refUp = Vector3.ProjectOnPlane(Vector3.up, fwd);
        if (refUp.sqrMagnitude < 0.01f)
            refUp = Vector3.ProjectOnPlane(Vector3.forward, fwd);
        refUp.Normalize();

        float rollDeg = Vector3.SignedAngle(refUp, wrist.up, fwd);

        if (Mathf.Abs(rollDeg) < wristRollThreshold)
            return 0;

        return rollDeg > 0f ? 1 : -1;
    }

    // Finds the box in the hand's Z-row across the width of MiniBed
    private CargoItem FindTargetBox(Vector3 bedLocalPos)
    {
        Transform bed = BedFrame.Mini.transform;
        CargoItem bestBox = null;
        float bestScore = float.MaxValue;

        foreach (var item in CargoItem.all)
        {
            if (item == null || !item.placedOnBed || item.isHeld) continue;

            Vector3 ol = bed.InverseTransformPoint(item.transform.position);
            Vector3 oh = item.transform.lossyScale * 0.5f;
            bool aligned = Mathf.Abs(Vector3.Dot(item.transform.right, bed.right)) > 0.7f;
            float extZ = aligned ? oh.z : oh.x;

            float zDist = Mathf.Abs(bedLocalPos.z - ol.z);
            if (zDist <= extZ + 0.08f)
            {
                float score = Mathf.Abs(bedLocalPos.x - ol.x) + ol.y * 0.5f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestBox = item;
                }
            }
        }
        return bestBox;
    }

    private bool EvaluatePlacement(out Vector3 lStart, out Vector3 lEnd, out bool diag)
    {
        lStart = Vector3.zero;
        lEnd = Vector3.zero;
        diag = false;

        if (BedFrame.Mini == null) return false;

        Transform bed = BedFrame.Mini.transform;
        Vector3 local = bed.InverseTransformPoint(transform.position);
        float halfW = BedFrame.Mini.HalfWidth;
        float halfL = BedFrame.Mini.HalfLength;

        if (Mathf.Abs(local.x) > halfW + 0.1f || Mathf.Abs(local.z) > halfL || local.y <= -0.05f)
            return false;

        int rollSign = GetWristRollSign();
        CargoItem targetBox = FindTargetBox(local);

        // Diagonal mode: spans the full width of the bed (-halfW to +halfW),
        // but locks stationary to the target box's Z-boundaries [ol.z - extZ, ol.z + extZ]
        // so it never goes too far left/right into neighboring boxes.
        if (rollSign != 0 && targetBox != null)
        {
            diag = true;
            Vector3 ol = bed.InverseTransformPoint(targetBox.transform.position);
            Vector3 oh = targetBox.transform.lossyScale * 0.5f;
            bool aligned = Mathf.Abs(Vector3.Dot(targetBox.transform.right, bed.right)) > 0.7f;
            float extZ = aligned ? oh.z : oh.x;

            float zStart = Mathf.Clamp(ol.z - rollSign * extZ, -halfL + 0.01f, halfL - 0.01f);
            float zEnd   = Mathf.Clamp(ol.z + rollSign * extZ, -halfL + 0.01f, halfL - 0.01f);

            lStart = new Vector3(-halfW, 0.005f, zStart);
            lEnd   = new Vector3( halfW, 0.005f, zEnd);
            return true;
        }

        // Straight mode: across full width of MiniBed at hand's Z position
        float clampedZ = Mathf.Clamp(local.z, -halfL + 0.03f, halfL - 0.03f);
        lStart = new Vector3(-halfW, 0.005f, clampedZ);
        lEnd   = new Vector3( halfW, 0.005f, clampedZ);
        return true;
    }

    void LateUpdate()
    {
        if (!isPlacedOnBed && grab.isSelected && EvaluatePlacement(out Vector3 lStart, out Vector3 lEnd, out _))
        {
            ghostLine.gameObject.SetActive(true);
            List<Vector3> pts = ComputeContourPoints(BedFrame.Mini.transform, lStart, lEnd, 1f, false);
            ApplyPointsToLine(ghostLine, pts, 0f);
        }
        else if (ghostLine != null && ghostLine.gameObject.activeSelf)
        {
            ghostLine.gameObject.SetActive(false);
        }

        if (isPlacedOnBed)
        {
            UpdatePlacedStrapAndTwin();
            UpdateCranking();
            FaceTextTowardCamera();
        }
    }

    // ---------- 2. PLACE STRAP, CRANK, REMOVE TAB & FULL TWIN ----------

    private void PlaceStrapAt(Vector3 lStart, Vector3 lEnd, bool diag)
    {
        isPlacedOnBed = true;
        isDiagonalMode = diag;
        localStartAnchor = lStart;
        localEndAnchor = lEnd;
        currentPretensionN = 0f;

        grab.enabled = false;
        rollRenderer.enabled = false;
        rollCollider.enabled = false;
        rb.isKinematic = true;

        Transform bed = BedFrame.Mini.transform;

        placedRoot = new GameObject(name + "_Placed");
        placedRoot.transform.SetParent(bed, false);
        placedRoot.transform.localPosition = localEndAnchor;

        miniLine = placedRoot.AddComponent<LineRenderer>();
        miniLine.useWorldSpace = true;
        miniLine.alignment = LineAlignment.View;
        miniLine.numCornerVertices = 4;
        miniLine.startWidth = miniLine.endWidth = miniBeltWidth;
        if (strapMaterial != null) miniLine.material = new Material(strapMaterial);

        BuildVisualCrank(placedRoot.transform);
        BuildRemoveButton(placedRoot.transform);

        if (TruckFrame.Instance != null)
        {
            float scaleRatio = TruckFrame.Instance.HalfWidth / BedFrame.Mini.HalfWidth;
            twinRoot = new GameObject(name + "_Twin");
            twinRoot.transform.SetParent(TruckFrame.Instance.twinParent, false);
            twinRoot.transform.localPosition = localEndAnchor * scaleRatio;

            twinLine = twinRoot.AddComponent<LineRenderer>();
            twinLine.useWorldSpace = true;
            twinLine.alignment = LineAlignment.View;
            twinLine.numCornerVertices = 4;
            twinLine.startWidth = twinLine.endWidth = miniBeltWidth * scaleRatio;
            if (strapMaterial != null) twinLine.material = new Material(strapMaterial);
        }

        UpdatePlacedStrapAndTwin();
    }

    private void BuildRemoveButton(Transform parent)
    {
        var removeBtn = GameObject.CreatePrimitive(PrimitiveType.Cube);
        removeBtn.name = "RemoveStrapButton";
        removeBtn.transform.SetParent(parent, false);
        removeBtn.transform.localPosition = new Vector3(0.025f, 0.02f, 0.045f);
        removeBtn.transform.localScale = new Vector3(0.025f, 0.025f, 0.025f);
        removeBtn.GetComponent<Renderer>().material.color = new Color(0.85f, 0.15f, 0.15f);

        removeBtn.GetComponent<BoxCollider>().size = new Vector3(1.5f, 1.5f, 1.5f);

        var simple = removeBtn.AddComponent<XRSimpleInteractable>();
        simple.selectEntered.AddListener(_ => RemoveStrapFromBed());
    }

    public void RemoveStrapFromBed()
    {
        Destroy(gameObject);
    }

    private void BuildVisualCrank(Transform parent)
    {
        var crankBase = new GameObject("RatchetCrank");
        crankBase.transform.SetParent(parent, false);
        crankBase.transform.localPosition = new Vector3(0.025f, 0.02f, 0f);
        crankBase.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        var pivotGo = new GameObject("CrankPivot");
        pivotGo.transform.SetParent(crankBase.transform, false);
        crankPivot = pivotGo.transform;

        var handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        handle.name = "CrankHandle";
        handle.transform.SetParent(crankPivot, false);
        handle.transform.localPosition = new Vector3(0f, 0.045f, 0f);
        handle.transform.localScale = new Vector3(0.015f, 0.09f, 0.015f);
        handle.GetComponent<Renderer>().material.color = new Color(0.2f, 0.2f, 0.22f);

        var boxCol = handle.GetComponent<BoxCollider>();
        boxCol.size = new Vector3(2.0f, 1.3f, 2.0f);

        var simpleInteractable = handle.AddComponent<XRSimpleInteractable>();
        simpleInteractable.selectEntered.AddListener(args =>
        {
            crankHand = args.interactorObject;
            prevHandAngle = GetCrankHandAngle();
        });
        simpleInteractable.selectExited.AddListener(args =>
        {
            crankHand = null;
            crankPivot.localRotation = Quaternion.identity;
        });

        var textGo = new GameObject("TensionReadout");
        textGo.transform.SetParent(crankBase.transform, false);
        textGo.transform.localPosition = new Vector3(-0.03f, 0.13f, 0f);

        tensionText = textGo.AddComponent<TextMeshPro>();
        tensionText.fontSize = 0.35f;
        tensionText.alignment = TextAlignmentOptions.Center;
        tensionText.rectTransform.sizeDelta = new Vector2(0.4f, 0.15f);
        UpdateTensionVisuals();
    }

    private void FaceTextTowardCamera()
    {
        if (tensionText == null || Camera.main == null) return;
        Vector3 toCam = tensionText.transform.position - Camera.main.transform.position;
        toCam.y = 0f;
        if (toCam.sqrMagnitude > 0.001f)
            tensionText.transform.rotation = Quaternion.LookRotation(toCam);
    }

    private void UpdateCranking()
    {
        if (crankHand == null || crankPivot == null) return;

        float angle = Mathf.Clamp(GetCrankHandAngle(), 0f, 75f);
        crankPivot.localRotation = Quaternion.Euler(0f, 0f, angle);

        float delta = angle - prevHandAngle;
        if (delta > 0f && currentPretensionN < maxPretensionN)
        {
            strokeAccumulator += delta;
            if (strokeAccumulator >= 14f)
            {
                strokeAccumulator -= 14f;
                currentPretensionN = Mathf.Min(currentPretensionN + (forcePerStrokeN / 5f), maxPretensionN);

                if (crankHand is XRBaseInputInteractor input)
                    input.SendHapticImpulse(0.65f, 0.04f);

                UpdateTensionVisuals();
            }
        }
        prevHandAngle = angle;
    }

    private float GetCrankHandAngle()
    {
        Vector3 localHand = crankPivot.parent.InverseTransformPoint(crankHand.transform.position);
        return Mathf.Atan2(-localHand.x, localHand.y) * Mathf.Rad2Deg;
    }

    private void UpdateTensionVisuals()
    {
        float ratio = Mathf.Clamp01(currentPretensionN / maxPretensionN);
        Color beltColor = Color.Lerp(new Color(1f, 0.55f, 0.05f), new Color(0.15f, 0.85f, 0.25f), ratio);

        if (miniLine != null) miniLine.material.color = beltColor;
        if (twinLine != null) twinLine.material.color = beltColor;

        if (tensionText != null)
        {
            float daN = currentPretensionN * 0.1f;
            float maxDaN = maxPretensionN * 0.1f;
            tensionText.text = $"{daN:0} / {maxDaN:0} daN";
            tensionText.color = beltColor;
        }
    }

    // ---------- 3. MULTI-BOX RAYCAST & CONVEX HULL CONTOUR ----------

    private void UpdatePlacedStrapAndTwin()
    {
        Transform miniBed = BedFrame.Mini.transform;
        float slackSag = Mathf.Lerp(0.012f, 0f, currentPretensionN / maxPretensionN);

        List<Vector3> miniPts = ComputeContourPoints(miniBed, localStartAnchor, localEndAnchor, 1f, true);
        ApplyPointsToLine(miniLine, miniPts, slackSag);

        if (twinLine != null && TruckFrame.Instance != null)
        {
            Transform fullTruck = TruckFrame.Instance.transform;
            float scaleRatio = TruckFrame.Instance.HalfWidth / BedFrame.Mini.HalfWidth;

            twinLine.positionCount = miniLine.positionCount;
            for (int i = 0; i < miniLine.positionCount; i++)
            {
                Vector3 localPt = miniBed.InverseTransformPoint(miniLine.GetPosition(i)) * scaleRatio;
                twinLine.SetPosition(i, fullTruck.TransformPoint(localPt));
            }
        }
    }

    private List<Vector3> ComputeContourPoints(Transform bed, Vector3 lStart, Vector3 lEnd, float scale, bool recordCargo)
    {
        if (recordCargo) securedCargo.Clear();

        Vector3 startAnchor = bed.TransformPoint(lStart);
        Vector3 endAnchor = bed.TransformPoint(lEnd);

        Vector3 beltDir = (endAnchor - startAnchor);
        beltDir.y = 0f;
        beltDir.Normalize();

        List<Vector3> cargoHits = new List<Vector3>();
        int cargoMask = LayerMask.GetMask("Cargo");

        for (int i = 1; i < raySamples; i++)
        {
            float t = (float)i / raySamples;
            Vector3 basePt = Vector3.Lerp(startAnchor, endAnchor, t);
            Vector3 rayTop = basePt + bed.up * (1.2f * scale);

            if (Physics.Raycast(rayTop, -bed.up, out RaycastHit hit, 1.2f * scale, cargoMask, QueryTriggerInteraction.Ignore))
            {
                cargoHits.Add(hit.point + bed.up * (hoverOffset * scale));

                if (recordCargo)
                {
                    var cargo = hit.collider.GetComponentInParent<CargoItem>();
                    if (cargo != null && !securedCargo.Contains(cargo))
                        securedCargo.Add(cargo);
                }
            }
        }

        if (cargoHits.Count > 0)
        {
            cargoHits[0] -= beltDir * (sideClearance * scale);
            cargoHits[cargoHits.Count - 1] += beltDir * (sideClearance * scale);
        }

        List<Vector3> rawPts = new List<Vector3> { startAnchor };
        rawPts.AddRange(cargoHits);
        rawPts.Add(endAnchor);

        return MakeUpperConvexHull(rawPts, bed, startAnchor, beltDir);
    }

    private List<Vector3> MakeUpperConvexHull(List<Vector3> pts, Transform bed, Vector3 origin, Vector3 horizDir)
    {
        if (pts.Count <= 2) return pts;

        List<Vector3> hull = new List<Vector3>();
        foreach (var p in pts)
        {
            float xCur = Vector3.Dot(p - origin, horizDir);
            float yCur = bed.InverseTransformPoint(p).y;

            while (hull.Count >= 2)
            {
                Vector3 p1 = hull[hull.Count - 1];
                Vector3 p2 = hull[hull.Count - 2];

                float x1 = Vector3.Dot(p1 - origin, horizDir);
                float y1 = bed.InverseTransformPoint(p1).y;
                float x2 = Vector3.Dot(p2 - origin, horizDir);
                float y2 = bed.InverseTransformPoint(p2).y;

                float cross = (x1 - x2) * (yCur - y2) - (y1 - y2) * (xCur - x2);
                if (cross >= 0.00001f) hull.RemoveAt(hull.Count - 1);
                else break;
            }
            hull.Add(p);
        }
        return hull;
    }

    private void ApplyPointsToLine(LineRenderer lr, List<Vector3> pts, float slackSag)
    {
        lr.positionCount = pts.Count;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 p = pts[i];
            if (slackSag > 0.0001f && i > 0 && i < pts.Count - 1)
            {
                float edgeFactor = Mathf.Sin((float)i / (pts.Count - 1) * Mathf.PI);
                p -= Vector3.up * (slackSag * edgeFactor * 0.4f);
            }
            lr.SetPosition(i, p);
        }
    }
}