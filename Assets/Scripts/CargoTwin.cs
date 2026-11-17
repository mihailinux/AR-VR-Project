using UnityEngine;

[RequireComponent(typeof(CargoItem))]
public class CargoTwin : MonoBehaviour
{
    public bool liveFollow = false;      // tick: twin follows the box all the time, even while held
    public bool hideWhileHeld = false;   // tick: hide the twin while you carry the box

    CargoItem item;
    Transform twin;
    Renderer twinRend, miniRend;
    bool following;
    bool wasHeld;

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
        go.SetActive(false);
        Destroy(go.GetComponent<Collider>());
        twin = go.transform;
        twin.SetParent(TruckFrame.Instance.twinParent, false);
        twin.localScale = item.realSizeM;

        twinRend = go.GetComponent<Renderer>();
        miniRend = item.rend;
        if (miniRend != null) twinRend.material = new Material(miniRend.sharedMaterial);

        item.Settled += OnSettled;
    }

    void Update()
    {
        if (twin == null) return;

        if (wasHeld && !item.isHeld)
        {
            following = true;
            Sync();
        }
        if (item.isHeld) following = false;
        wasHeld = item.isHeld;

        // CHANGED: visible only if the box was last placed on the bed
        bool show = item.placedOnBed && !(hideWhileHeld && item.isHeld);
        if (twin.gameObject.activeSelf != show) twin.gameObject.SetActive(show);

        if (miniRend != null && twinRend.material.color != miniRend.material.color)
            twinRend.material.color = miniRend.material.color;
    }

    void LateUpdate()
    {
        if (liveFollow || following) Sync();
    }

    void OnSettled()
    {
        Sync();
        following = false;
    }

    void Sync()
    {
        if (twin == null) return;

        Transform bed = BedFrame.Mini.transform;
        Transform truck = TruckFrame.Instance.transform;

        Vector3 local = bed.InverseTransformPoint(transform.position) / item.miniScale;
        twin.position = truck.TransformPoint(local);
        twin.rotation = truck.rotation * (Quaternion.Inverse(bed.rotation) * transform.rotation);
    }

    void OnDestroy()
    {
        if (item != null) item.Settled -= OnSettled;
        if (twin != null) Destroy(twin.gameObject);
    }
}