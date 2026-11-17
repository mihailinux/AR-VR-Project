using System.Collections.Generic;
using UnityEngine;

public class StrapSpawner : MonoBehaviour
{
    public GameObject strapRollPrefab;
    public Transform supplyTable;
    public Vector3 tableOffset = new Vector3(-0.35f, 0.04f, 0f); // Spot on SupplyTable

    private MiniStrapRoll currentTableRoll;
    private readonly List<MiniStrapRoll> allStraps = new List<MiniStrapRoll>();

    void Start()
    {
        SpawnFreshRoll();
    }

    public void SpawnFreshRoll()
    {
        if (strapRollPrefab == null || supplyTable == null) return;

        Vector3 topCenter = supplyTable.position + Vector3.up * (supplyTable.lossyScale.y * 0.5f);
        Vector3 spawnPos = topCenter + supplyTable.TransformDirection(tableOffset);
        float yaw = BedFrame.Mini != null ? BedFrame.Mini.Yaw : 0f;

        var go = Instantiate(strapRollPrefab, spawnPos, Quaternion.Euler(0f, yaw, 0f), transform);
        currentTableRoll = go.GetComponent<MiniStrapRoll>();
        currentTableRoll.spawner = this;
        allStraps.Add(currentTableRoll);
    }

    // Called automatically by MiniStrapRoll the moment you pick it up from the table
    public void NotifyRollTaken(MiniStrapRoll takenRoll)
    {
        if (takenRoll == currentTableRoll)
        {
            currentTableRoll = null;
            SpawnFreshRoll();
        }
    }

    // Called by ResetButton to wipe all placed straps off MiniBed and TruckBed_Full
    public void ResetAllStraps()
    {
        foreach (var s in allStraps)
        {
            if (s != null) Destroy(s.gameObject);
        }
        allStraps.Clear();
        currentTableRoll = null;
        SpawnFreshRoll();
    }
}