using UnityEngine;

public class BedFrame : MonoBehaviour
{
    public static BedFrame Mini { get; private set; }

    void Awake() { Mini = this; }

    public Vector3 Forward => transform.forward;   // toward the front wall
    public float Yaw => transform.eulerAngles.y;
}