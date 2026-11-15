using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(LineRenderer))]
public class LashingStrap : MonoBehaviour
{
    [Header("1. Strap Anchors & Hook")]
    public Transform leftAnchor;
    public XRGrabInteractable grabbableHook;
    public XRSocketInteractor rightRailSocket;

   [Header("2. Ratchet Crank")]
    public GameObject ratchetAssembly;
    public Transform leverPivot;
    public XRBaseInteractable handleGrab; // Changed from XRGrabInteractable to XRBaseInteractable
    public TextMeshPro tensionReadout;

    [Header("3. Tension Specs")]
    public float maxPretensionNewtons = 5000f; // 500 daN
    public float forcePerFullStroke = 1000f;
    public float currentPretensionNewtons = 0f;

    [Header("4. Contour Raycast Settings")]
    public int samplePoints = 24;
    public float maxBoxHeight = 2.0f;
    public float strapHoverOffset = 0.012f;
    public LayerMask cargoLayerMask = ~0; // Uncheck StrapLayer in Inspector!

    [Header("5. Live Status")]
    public bool isHookedIn = false;
    public List<CargoItem> strappedCargo = new List<CargoItem>();

    private LineRenderer line;
    private Vector3 hookStartPos;
    private Quaternion hookStartRot;
    private IXRSelectInteractor crankingHand;
    private float previousAngle = 0f;
    private float accumulatedStrokeAngle = 0f;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.TransformZ;

        if (grabbableHook != null)
        {
            hookStartPos = grabbableHook.transform.position;
            hookStartRot = grabbableHook.transform.rotation;
        }

        if (ratchetAssembly != null)
            ratchetAssembly.SetActive(false);
    }

    void OnEnable()
    {
        if (rightRailSocket != null)
        {
            rightRailSocket.selectEntered.AddListener(OnHookLocked);
            rightRailSocket.selectExited.AddListener(OnHookUnlocked);
        }
        if (grabbableHook != null)
            grabbableHook.selectExited.AddListener(OnHookReleased);

        if (handleGrab != null)
        {
            handleGrab.selectEntered.AddListener(OnRatchetGrabbed);
            handleGrab.selectExited.AddListener(OnRatchetReleased);
        }
    }

    void OnDisable()
    {
        if (rightRailSocket != null)
        {
            rightRailSocket.selectEntered.RemoveListener(OnHookLocked);
            rightRailSocket.selectExited.RemoveListener(OnHookUnlocked);
        }
        if (grabbableHook != null)
            grabbableHook.selectExited.RemoveListener(OnHookReleased);

        if (handleGrab != null)
        {
            handleGrab.selectEntered.RemoveListener(OnRatchetGrabbed);
            handleGrab.selectExited.RemoveListener(OnRatchetReleased);
        }
    }

    private void OnHookLocked(SelectEnterEventArgs args)
    {
        isHookedIn = true;
        currentPretensionNewtons = 0f;
        if (ratchetAssembly != null)
        {
            ratchetAssembly.SetActive(true);
            UpdateTensionUI();
        }
    }

    private void OnHookUnlocked(SelectExitEventArgs args)
    {
        isHookedIn = false;
        currentPretensionNewtons = 0f;
        strappedCargo.Clear();
        if (ratchetAssembly != null)
            ratchetAssembly.SetActive(false);
    }

    private void OnHookReleased(SelectExitEventArgs args)
    {
        Invoke(nameof(ResetHookIfNotSnapped), 0.1f);
    }

    private void ResetHookIfNotSnapped()
    {
        if (rightRailSocket != null && !rightRailSocket.hasSelection && grabbableHook != null && !grabbableHook.isSelected)
        {
            grabbableHook.transform.SetPositionAndRotation(hookStartPos, hookStartRot);
        }
    }

    private void OnRatchetGrabbed(SelectEnterEventArgs args)
    {
        crankingHand = args.interactorObject;
        previousAngle = GetHandAngle();
    }

    private void OnRatchetReleased(SelectExitEventArgs args)
    {
        crankingHand = null;
        if (leverPivot != null)
            leverPivot.localRotation = Quaternion.identity;
    }

    void Update()
    {
        UpdateStrapVisuals();
        UpdateRatchetCranking();
    }

    private void UpdateRatchetCranking()
    {
        if (!isHookedIn || crankingHand == null || leverPivot == null) return;

        float currentAngle = Mathf.Clamp(GetHandAngle(), 0f, 65f);
        leverPivot.localRotation = Quaternion.Euler(currentAngle, 0f, 0f);

        float delta = currentAngle - previousAngle;
        if (delta > 0f && currentPretensionNewtons < maxPretensionNewtons)
        {
            accumulatedStrokeAngle += delta;
            if (accumulatedStrokeAngle >= 13f)
            {
                accumulatedStrokeAngle -= 13f;
                currentPretensionNewtons = Mathf.Min(
                    currentPretensionNewtons + (forcePerFullStroke / 5f),
                    maxPretensionNewtons
                );

                if (crankingHand is XRBaseInputInteractor inputInteractor)
                    inputInteractor.SendHapticImpulse(0.7f, 0.04f);

                UpdateTensionUI();
            }
        }
        previousAngle = currentAngle;
    }

    private float GetHandAngle()
    {
        Vector3 localHandPos = ratchetAssembly.transform.InverseTransformPoint(crankingHand.transform.position);
        return Mathf.Atan2(localHandPos.y, localHandPos.z) * Mathf.Rad2Deg;
    }

    private void UpdateTensionUI()
    {
        if (tensionReadout == null) return;
        float daN = currentPretensionNewtons * 0.1f;
        float maxDaN = maxPretensionNewtons * 0.1f;
        Color c = (currentPretensionNewtons >= 3000f) ? Color.green : Color.yellow;
        tensionReadout.text = $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{daN:0} / {maxDaN:0} daN</color>";
    }

    private void UpdateStrapVisuals()
    {
        if (leftAnchor == null || grabbableHook == null) return;

        Vector3 startPos = leftAnchor.position;
        Vector3 endPos = isHookedIn && rightRailSocket != null ? rightRailSocket.transform.position : grabbableHook.transform.position;

        if (Vector3.Distance(startPos, endPos) < 0.2f)
        {
            line.positionCount = 2;
            line.SetPosition(0, startPos);
            line.SetPosition(1, endPos);
            strappedCargo.Clear();
            return;
        }

        strappedCargo.Clear();
        List<Vector3> rawPoints = new List<Vector3> { startPos };
        float floorY = Mathf.Min(startPos.y, endPos.y);

        for (int i = 1; i < samplePoints; i++)
        {
            float t = (float)i / samplePoints;
            Vector3 horizontalPt = Vector3.Lerp(startPos, endPos, t);
            Vector3 rayOrigin = new Vector3(horizontalPt.x, floorY + maxBoxHeight, horizontalPt.z);

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, maxBoxHeight, cargoLayerMask, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.CompareTag("TruckBed") && hit.point.y > floorY + 0.05f)
                {
                    rawPoints.Add(hit.point + Vector3.up * strapHoverOffset);

                    if (isHookedIn && hit.collider.TryGetComponent<CargoItem>(out CargoItem cargo))
                    {
                        if (!strappedCargo.Contains(cargo))
                            strappedCargo.Add(cargo);
                    }
                }
            }
        }

        rawPoints.Add(endPos);
        List<Vector3> tautPoints = MakeStrapTaut(rawPoints);

        line.positionCount = tautPoints.Count;
        for (int i = 0; i < tautPoints.Count; i++)
            line.SetPosition(i, tautPoints[i]);
    }

    private List<Vector3> MakeStrapTaut(List<Vector3> pts)
    {
        if (pts.Count <= 2) return pts;

        List<Vector3> hull = new List<Vector3>();
        Vector3 start = pts[0];
        Vector3 horizontalDir = pts[pts.Count - 1] - start;
        horizontalDir.y = 0f;
        if (horizontalDir.magnitude < 0.001f) return pts;
        horizontalDir.Normalize();

        foreach (var p in pts)
        {
            float xCurrent = Vector3.Dot(p - start, horizontalDir);
            float yCurrent = p.y;

            while (hull.Count >= 2)
            {
                Vector3 prev1 = hull[hull.Count - 1];
                Vector3 prev2 = hull[hull.Count - 2];

                float x1 = Vector3.Dot(prev1 - start, horizontalDir);
                float y1 = prev1.y;
                float x2 = Vector3.Dot(prev2 - start, horizontalDir);
                float y2 = prev2.y;

                float cross = (x1 - x2) * (yCurrent - y2) - (y1 - y2) * (xCurrent - x2);
                if (cross >= 0.0001f)
                    hull.RemoveAt(hull.Count - 1);
                else
                    break;
            }
            hull.Add(p);
        }

        return hull;
    }
}