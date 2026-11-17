using System.Collections.Generic;
using UnityEngine;

public class BoxSpawner : MonoBehaviour
{
    [System.Serializable]
    public class BoxType
    {
        public string label;
        public Vector3 realSizeM;   // X width, Y height, Z length (meters)
        public float massKg;
        public BoxType(string l, Vector3 s, float m) { label = l; realSizeM = s; massKg = m; }
    }

    public GameObject boxPrefab;      // the Cargo_Box prefab
    public Transform supplyTable;     // the SupplyTable cube
    public int columns = 3;
    public float spacingX = 0.5f;
    public float spacingZ = 0.6f;

    public BoxType[] types =
    {
        new BoxType("Palette Kartons", new Vector3(0.8f, 1.0f, 1.2f), 600f),
        new BoxType("Holzkiste",       new Vector3(0.8f, 0.9f, 1.2f), 850f),
        new BoxType("Hohe Kiste",      new Vector3(0.6f, 1.6f, 0.8f), 300f),
        new BoxType("Gitterbox",       new Vector3(1.0f, 1.0f, 1.2f), 400f),
        new BoxType("Karton mittel",   new Vector3(0.4f, 0.4f, 0.6f), 20f),
    };

    readonly List<GameObject> spawned = new List<GameObject>();

    void Start() { SpawnAll(); }

    public void SpawnAll()
    {
        foreach (var g in spawned) if (g != null) Destroy(g);
        spawned.Clear();

        float miniScale = boxPrefab.GetComponent<CargoItem>().miniScale;
        Vector3 top = supplyTable.position + Vector3.up * (supplyTable.lossyScale.y * 0.5f);
        float yaw = BedFrame.Mini != null ? BedFrame.Mini.Yaw : 0f;
        int rows = Mathf.CeilToInt(types.Length / (float)columns);

        for (int i = 0; i < types.Length; i++)
        {
            int col = i % columns, row = i / columns;
            float dx = (col - (columns - 1) / 2f) * spacingX;
            float dz = (row - (rows - 1) / 2f) * spacingZ;
            float height = types[i].realSizeM.y * miniScale;

            Vector3 pos = new Vector3(top.x + dx, top.y + height * 0.5f + 0.005f, top.z + dz);
            var go = Instantiate(boxPrefab, pos, Quaternion.Euler(0f, yaw, 0f), transform);
            go.name = types[i].label;
            go.GetComponent<CargoItem>().Configure(types[i].realSizeM, types[i].massKg);
            spawned.Add(go);
        }
    }
}