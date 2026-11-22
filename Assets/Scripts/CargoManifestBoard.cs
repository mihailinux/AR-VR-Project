using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TMPro;

public class CargoManifestBoard : MonoBehaviour
{
    public static CargoManifestBoard Instance { get; private set; }

    [Header("Refresh Rate")]
    public float refreshInterval = 0.25f;

    private TextMeshPro boardText;
    private float nextRefresh;

    // Automatically spawns the board on scene load—no Unity Editor setup required!
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreateBoard()
    {
        if (Instance != null || BedFrame.Mini == null) return;

        var go = new GameObject("CargoManifestBoard");
        go.AddComponent<CargoManifestBoard>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildBoardGeometry();
    }

    private void BuildBoardGeometry()
    {
        Transform bed = BedFrame.Mini.transform;

        // Place at the cab/front edge of MiniBed, facing back toward the cargo bed & player
        transform.SetParent(bed, false);
        transform.localPosition = new Vector3(0f, 0.26f, BedFrame.Mini.HalfLength + 0.06f);
        transform.localRotation = Quaternion.Euler(15f, 180f, 0f); // Angled upward like a lectern

        // Outer metallic border frame
        var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
        frame.name = "BoardFrame";
        Destroy(frame.GetComponent<Collider>());
        frame.transform.SetParent(transform, false);
        frame.transform.localScale = new Vector3(0.68f, 0.44f, 0.015f);
        var frameMat = new Material(Shader.Find("Unlit/Color"));
        frameMat.color = new Color(0.18f, 0.20f, 0.24f);
        frame.GetComponent<Renderer>().material = frameMat;

        // Dark matte screen surface
        var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "BoardScreen";
        Destroy(screen.GetComponent<Collider>());
        screen.transform.SetParent(transform, false);
        screen.transform.localPosition = new Vector3(0f, 0f, -0.0085f);
        screen.transform.localScale = new Vector3(0.64f, 0.40f, 1f);
        var screenMat = new Material(Shader.Find("Unlit/Color"));
        screenMat.color = new Color(0.06f, 0.07f, 0.10f);
        screen.GetComponent<Renderer>().material = screenMat;

        // TextMeshPro display surface
        var textGo = new GameObject("ManifestText");
        textGo.transform.SetParent(transform, false);
        textGo.transform.localPosition = new Vector3(0f, 0f, -0.010f);

        boardText = textGo.AddComponent<TextMeshPro>();
        boardText.fontSize = 0.19f;
        boardText.alignment = TextAlignmentOptions.TopLeft;
        boardText.rectTransform.sizeDelta = new Vector2(0.60f, 0.36f);
        boardText.color = Color.white;
    }

    void Update()
    {
        if (boardText == null || Time.time < nextRefresh) return;
        nextRefresh = Time.time + refreshInterval;
        RefreshManifest();
    }

    public void RefreshManifest()
    {
        var items = CargoItem.all;
        int totalBoxes = items.Count;
        int loadedBoxes = 0;
        float totalLoadedKg = 0f;

        int activeStraps = 0;
        int uncrankedStraps = 0;
        foreach (var s in MiniStrapRoll.allStraps)
        {
            if (s != null && s.isPlacedOnBed)
            {
                activeStraps++;
                if (s.currentPretensionN < 500f) uncrankedStraps++;
            }
        }

        foreach (var item in items)
        {
            if (item != null && item.onBed)
            {
                loadedBoxes++;
                totalLoadedKg += item.massKg;
            }
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"<b><color=#38BDF8>CARGO MANIFEST & LOAD STATUS</color></b>");
        sb.AppendLine($"<color=#94A3B8>Loaded: {loadedBoxes}/{totalBoxes} Boxes ({totalLoadedKg:0} kg)   |   Straps: {activeStraps}</color>");
        sb.AppendLine("<color=#334155>------------------------------------------------------------</color>");

        List<string> warnings = new List<string>();

        foreach (var item in items)
        {
            if (item == null) continue;

            item.GetStrapSummary(out int strapCount, out float totalDaN);
            string specs = $"{item.massKg:0}kg ({item.realSizeM.x:0.0}×{item.realSizeM.y:0.0}×{item.realSizeM.z:0.0}m)";
            string status;

            if (item.isHeld)
            {
                status = "<color=#E879F9>[In Hand]</color>";
            }
            else if (item.onSupply)
            {
                status = "<color=#94A3B8>On Table</color>";
            }
            else if (!item.onBed)
            {
                status = "<color=#F87171>Off Bed</color>";
            }
            else
            {
                List<string> badges = new List<string>();
                if (item.formFitFront) badges.Add("<color=#4ADE80>Form-Fit ✓</color>");
                if (strapCount > 0)
                {
                    string c = totalDaN >= 200f ? "#4ADE80" : "#FACC15";
                    badges.Add($"<color={c}>Strap {totalDaN:0}daN</color>");
                }
                if (item.stackedOn != null) badges.Add("<color=#38BDF8>Stacked</color>");

                if (badges.Count == 0)
                {
                    status = "<color=#FB923C>UNSECURED</color>";
                    warnings.Add($"• <b>{item.name}</b> is loose on bed (needs Form-Fit or Strap)");
                }
                else
                {
                    status = string.Join(" + ", badges);
                }

                // Geometric & handling callouts
                if (item.realSizeM.y > item.realSizeM.x * 2.0f && strapCount == 0)
                    warnings.Add($"• <b>{item.name}</b> is tall ({item.realSizeM.y:0.0}m) — high tipping risk without strap");

                if (item.stackedOn != null && item.massKg > item.stackedOn.massKg * 1.5f)
                    warnings.Add($"• <b>{item.name}</b> ({item.massKg:0}kg) is heavier than <b>{item.stackedOn.name}</b> below it");
            }

            sb.AppendLine($"<b>{item.name}</b>  <color=#CBD5E1>{specs}</color>  →  {status}");
        }

        if (uncrankedStraps > 0)
            warnings.Insert(0, $"• <b>{uncrankedStraps} strap(s)</b> at 0 daN — pump ratchet crank on right rail");

        if (warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("<color=#FACC15><b>LOAD CHECKLIST ALERTS:</b></color>");
            int maxAlerts = Mathf.Min(warnings.Count, 3);
            for (int i = 0; i < maxAlerts; i++)
                sb.AppendLine($"<color=#FDE047>{warnings[i]}</color>");
        }
        else if (loadedBoxes > 0)
        {
            sb.AppendLine();
            sb.AppendLine("<color=#4ADE80><b>✓ All loaded cargo secured — ready for FAHRTEST!</b></color>");
        }

        boardText.text = sb.ToString();
    }
}