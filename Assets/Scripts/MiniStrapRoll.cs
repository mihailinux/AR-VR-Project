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
    public float miniBeltWidth = 0.035f;   // Thicker belt (3.5cm mini -> ~10.5cm full truck)
    public float hoverOffset = 0.02f;      // > half belt width so it never clips into box tops
    public float sideClearance = 0.015f;   // Pushes outer corners out so diagonal segments clear box sides
    public int raySamples = 40;

    [Header("Ratchet & Tension Specs")]
    public float maxPretensionN = 5000f;   // 500 daN
    public float forcePerStrokeN = 1000f;
    public float currentPretensionN = 0f;

    [Header("Live Status (Read by Validator)")]
    public bool isPlacedOnBed = false;
    public float bedLocalZ = 0f;
    public List<CargoItem> securedCargo = new List<CargoItem>();

    private Rigidbody rb;
    private XRGrabInteractable grab;
    private MeshRenderer rollRenderer;
    private Collider rollCollider;
    private bool notifiedSpawner = false;

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
        grab.selectEntered.AddListener(OnRollGrabbed);
        grab.selectExited.AddListener(OnRollReleased);
    }

    void OnDisable()
    {
        grab.selectEntered.RemoveListener(OnRollGrabbed);
        grab.selectExited.RemoveListener(OnRollReleased);
    }

    private void OnDestroy()
    {
        if (ghostLine != null) Destroy(ghostLine.gameObject);
        if (placedRoot != null) Destroy(placedRoot);
        if (twinRoot != null) Destroy(twinRoot);
    }

    // ---------- 1. GRAB, RESPAWN & GHOST PREVIEW ----------

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
        if (!notifiedSpawner && spawner != null)
        {
            notifiedSpawner = true;
            spawner.NotifyRollTaken(this);
        }
    }

    private void OnRollReleased(SelectExitEventArgs args)
    {
        if (ghostLine != null) ghostLine.gameObject.SetActive(false);

        if (IsOverMiniBed(out float localZ))
        {
            PlaceStrapAt(localZ);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private bool IsOverMiniBed(out float localZ)
    {
        localZ = 0f;
        if (BedFrame.Mini == null) return false;

        Transform bed = BedFrame.Mini.transform;
        Vector3 local = bed.InverseTransformPoint(transform.position);
        float halfW = BedFrame.Mini.HalfWidth;
        float halfL = BedFrame.Mini.HalfLength;

        if (Mathf.Abs(local.x) <= halfW + 0.1f && Mathf.Abs(local.z) <= halfL && local.y > -0.05f)
        {
            localZ = Mathf.Clamp(local.z, -halfL + 0.03f, halfL - 0.03f);
            return true;
        }
        return false;
    }

    void LateUpdate()
    {
        if (!isPlacedOnBed && grab.isSelected && IsOverMiniBed(out float localZ))
        {
            ghostLine.gameObject.SetActive(true);
            List<Vector3> pts = ComputeContourPoints(BedFrame.Mini.transform, BedFrame.Mini.HalfWidth, localZ, 1f, false);
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

    private void PlaceStrapAt(float localZ)
    {
        isPlacedOnBed = true;
        bedLocalZ = localZ;
        currentPretensionN = 0f;

        grab.enabled = false;
        rollRenderer.enabled = false;
        rollCollider.enabled = false;
        rb.isKinematic = true;

        Transform bed = BedFrame.Mini.transform;
        float halfW = BedFrame.Mini.HalfWidth;

        placedRoot = new GameObject(name + "_Placed");
        placedRoot.transform.SetParent(bed, false);
        placedRoot.transform.localPosition = new Vector3(0f, 0f, localZ);

        miniLine = placedRoot.AddComponent<LineRenderer>();
        miniLine.useWorldSpace = true;
        miniLine.alignment = LineAlignment.View;
        miniLine.numCornerVertices = 4;
        miniLine.startWidth = miniLine.endWidth = miniBeltWidth;
        if (strapMaterial != null) miniLine.material = new Material(strapMaterial);

        BuildVisualCrank(placedRoot.transform, halfW);
        BuildRemoveButton(placedRoot.transform, halfW);

        if (TruckFrame.Instance != null)
        {
            float scaleRatio = TruckFrame.Instance.HalfWidth / halfW;
            twinRoot = new GameObject(name + "_Twin");
            twinRoot.transform.SetParent(TruckFrame.Instance.twinParent, false);
            twinRoot.transform.localPosition = new Vector3(0f, 0f, localZ * scaleRatio);

            twinLine = twinRoot.AddComponent<LineRenderer>();
            twinLine.useWorldSpace = true;
            twinLine.alignment = LineAlignment.View;
            twinLine.numCornerVertices = 4;
            twinLine.startWidth = twinLine.endWidth = miniBeltWidth * scaleRatio;
            if (strapMaterial != null) twinLine.material = new Material(strapMaterial);
        }

        UpdatePlacedStrapAndTwin();
    }

    private void BuildRemoveButton(Transform parent, float halfW)
    {
        var removeBtn = GameObject.CreatePrimitive(PrimitiveType.Cube);
        removeBtn.name = "RemoveStrapButton";
        removeBtn.transform.SetParent(parent, false);
        removeBtn.transform.localPosition = new Vector3(halfW + 0.025f, 0.02f, 0.045f);
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

    private void BuildVisualCrank(Transform parent, float halfW)
    {
        var crankBase = new GameObject("RatchetCrank");
        crankBase.transform.SetParent(parent, false);
        crankBase.transform.localPosition = new Vector3(halfW + 0.025f, 0.02f, 0f);
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

        // 0° = straight UP, 75° = pulled DOWN toward the player
        float angle = Mathf.Clamp(GetCrankHandAngle(), 0f, 75f);
        crankPivot.localRotation = Quaternion.Euler(0f, 0f, angle);

        // Pulling the crank DOWN (angle increasing) tightens the ratchet
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
        // Local +Y is UP, local -X is OUTWARD toward the player.
        // Atan2(-localHand.x, localHand.y) = 0° when hand is UP, and increases positively as hand moves DOWN.
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
        float miniHalfW = BedFrame.Mini.HalfWidth;
        float slackSag = Mathf.Lerp(0.012f, 0f, currentPretensionN / maxPretensionN);

        List<Vector3> miniPts = ComputeContourPoints(miniBed, miniHalfW, bedLocalZ, 1f, true);
        ApplyPointsToLine(miniLine, miniPts, slackSag);

        if (twinLine != null && TruckFrame.Instance != null)
        {
            Transform fullTruck = TruckFrame.Instance.transform;
            float scaleRatio = TruckFrame.Instance.HalfWidth / miniHalfW;

            twinLine.positionCount = miniLine.positionCount;
            for (int i = 0; i < miniLine.positionCount; i++)
            {
                Vector3 localPt = miniBed.InverseTransformPoint(miniLine.GetPosition(i)) * scaleRatio;
                twinLine.SetPosition(i, fullTruck.TransformPoint(localPt));
            }
        }
    }

    private List<Vector3> ComputeContourPoints(Transform bed, float halfW, float localZ, float scale, bool recordCargo)
    {
        if (recordCargo) securedCargo.Clear();

        Vector3 leftAnchor = bed.TransformPoint(new Vector3(-halfW, 0.005f, localZ));
        Vector3 rightAnchor = bed.TransformPoint(new Vector3(halfW, 0.005f, localZ));

        List<Vector3> cargoHits = new List<Vector3>();
        int cargoMask = LayerMask.GetMask("Cargo");

        for (int i = 1; i < raySamples; i++)
        {
            float t = (float)i / raySamples;
            float localX = Mathf.Lerp(-halfW, halfW, t);
            Vector3 rayTop = bed.TransformPoint(new Vector3(localX, 1.2f * scale, localZ));

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

        // Push the outer cargo edge points slightly outward along X so the strap clears vertical box walls cleanly
        if (cargoHits.Count > 0)
        {
            cargoHits[0] -= bed.right * (sideClearance * scale);
            cargoHits[cargoHits.Count - 1] += bed.right * (sideClearance * scale);
        }

        List<Vector3> rawPts = new List<Vector3> { leftAnchor };
        rawPts.AddRange(cargoHits);
        rawPts.Add(rightAnchor);

        return MakeUpperConvexHull(rawPts, bed);
    }

    private List<Vector3> MakeUpperConvexHull(List<Vector3> pts, Transform bed)
    {
        if (pts.Count <= 2) return pts;

        List<Vector3> hull = new List<Vector3>();
        foreach (var p in pts)
        {
            Vector3 lp = bed.InverseTransformPoint(p);
            while (hull.Count >= 2)
            {
                Vector3 l1 = bed.InverseTransformPoint(hull[hull.Count - 1]);
                Vector3 l2 = bed.InverseTransformPoint(hull[hull.Count - 2]);

                float cross = (l1.x - l2.x) * (lp.y - l2.y) - (l1.y - l2.y) * (lp.x - l2.x);
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