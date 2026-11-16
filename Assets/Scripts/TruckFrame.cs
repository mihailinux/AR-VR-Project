using UnityEngine;

public class TruckFrame : MonoBehaviour
{
    public static TruckFrame Instance { get; private set; }
    public Transform twinParent;   // drag Cargo_Twins here

    void Awake() { Instance = this; }
}