using UnityEngine;

public class TruckFrame : MonoBehaviour
{
    public static TruckFrame Instance { get; private set; }
    public Transform floor;        // drag Floor_Full here
    public Transform twinParent;   // drag Cargo_Twins here

    public float HalfWidth => floor != null ? floor.lossyScale.x * 0.5f : 1.2f;

    void Awake() { Instance = this; }
}