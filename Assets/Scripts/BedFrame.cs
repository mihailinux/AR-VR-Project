using UnityEngine;

public class BedFrame : MonoBehaviour
{
    public static BedFrame Mini { get; private set; }

    public Transform floor;           // drag MiniBed_Floor here
    public float halfWidthM = 0.4f;   // only used if Floor is empty
    public float halfLengthM = 1.0f;

    public float HalfWidth { get; private set; }
    public float HalfLength { get; private set; }

    public Vector3 Forward => transform.forward;   // toward the front wall
    public Vector3 Right => transform.right;
    public float Yaw => transform.eulerAngles.y;

    void Awake()
    {
        Mini = this;
        HalfWidth = floor != null ? floor.lossyScale.x * 0.5f : halfWidthM;
        HalfLength = floor != null ? floor.lossyScale.z * 0.5f : halfLengthM;
    }
}