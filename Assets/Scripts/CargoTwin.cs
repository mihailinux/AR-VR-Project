using UnityEngine;

[RequireComponent(typeof(CargoItem))]
public class CargoTwin : MonoBehaviour
{
    public bool liveFollow = false;      // tick: twin follows the box all the time
    public bool hideWhileHeld = false;   // tick: hide the twin while you carry the box

    CargoItem item;
    Transform twin;
    Renderer twinRend, miniRend;

    void Start()
    {
        item = GetComponent<CargoItem>();
        if (BedFrame.Mini == null || TruckFrame.Instance == null)
        {
            Debug.LogWarning("CargoTwin: BedFrame or TruckFrame missing in the scene");
            enabled = false;
            return;
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name + "_Twin";
        go.SetActive(false);                           // hidden until the first placement
        Destroy(go.GetComponent<Collider>());          // twin is visual only
        twin = go.transform;
        twin.SetParent(TruckFrame.Instance.twinParent, false);
        twin.localScale = item.realSizeM;              // real size in meters

        twinRend = go.GetComponent<Renderer>();
        miniRend = item.rend;
        if (miniRend != null) twinRend.material = new Material(miniRend.sharedMaterial);

        item.Settled += Sync;                          // update when the box has stopped
    }

    void Update()
    {
        if (twin == null) return;

        if (hideWhileHeld && item.isHeld)
            twin.gameObject.SetActive(false);

        // color follows every frame so it never shows an old state
        if (miniRend != null && twinRend.material.color != miniRend.material.color)
            twinRend.material.color = miniRend.material.color;
    }

    void LateUpdate()
    {
        if (liveFollow) Sync();
    }

    void Sync()
    {
        if (twin == null) return;
        twin.gameObject.SetActive(true);

        Transform bed = BedFrame.Mini.transform;
        Transform truck = TruckFrame.Instance.transform;

        // position relative to the mini bed, scaled up to real size
        Vector3 local = bed.InverseTransformPoint(transform.position) / item.miniScale;
        twin.position = truck.TransformPoint(local);
        twin.rotation = truck.rotation * (Quaternion.Inverse(bed.rotation) * transform.rotation);
    }

    void OnDestroy()
    {
        if (item != null) item.Settled -= Sync;
        if (twin != null) Destroy(twin.gameObject);
    }
}