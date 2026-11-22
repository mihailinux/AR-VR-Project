using System.Collections.Generic;
using UnityEngine;

public class StrapSpawner : MonoBehaviour
{
    public GameObject strapRollPrefab;
    public Transform supplyTable;
    public Vector3 tableOffset = new Vector3(-0.35f, 0.04f, 0f);

    private MiniStrapRoll currentTableRoll;
    private readonly List<MiniStrapRoll> allStraps = new List<MiniStrapRoll>();

    void Start()
    {
        if (supplyTable == null)
        {
            var found = GameObject.Find("SupplyTable");
            if (found != null) supplyTable = found.transform;
        }
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
        if (currentTableRoll != null)
        {
            currentTableRoll.spawner = this;
            allStraps.Add(currentTableRoll);
        }
    }

    public void NotifyRollTaken(MiniStrapRoll takenRoll)
    {
        if (takenRoll == currentTableRoll)
        {
            currentTableRoll = null;
            SpawnFreshRoll();
        }
    }

    public void ResetAllStraps()
    {
        foreach (var s in allStraps)
        {
            if (s != null) Destroy(s.gameObject);
        }
        allStraps.Clear();

        // Catch any stray rolls in the scene
        foreach (var stray in FindObjectsByType<MiniStrapRoll>(FindObjectsSortMode.None))
        {
            if (stray != null) Destroy(stray.gameObject);
        }

        currentTableRoll = null;
        SpawnFreshRoll();
    }
}