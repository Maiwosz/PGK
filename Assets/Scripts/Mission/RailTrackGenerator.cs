using UnityEngine;

/// <summary>
/// Układa tor z powtarzalnych prefabów wzdłuż TrackPath: odcinek szyn z podsypką i podkładami,
/// płyta mostu pod torem na odcinkach "most" oraz obudowa tunelu na odcinkach "tunel".
/// Prefaby mają długość segmentLength wzdłuż osi Z (środek w pivocie); na łukach segmenty
/// są rozciągane do rzeczywistej długości cięciwy.
/// </summary>
[ExecuteAlways]
public class RailTrackGenerator : MonoBehaviour
{
    [SerializeField] TrackPath path;
    [SerializeField] GameObject railPrefab;
    [SerializeField] GameObject bridgePrefab;
    [SerializeField] GameObject tunnelPrefab;
    [Tooltip("Długość prefabu odcinka (m) – musi zgadzać się z prefabem.")]
    [SerializeField] float segmentLength = 4f;

    const string RootName = "Generated";

    [ContextMenu("Generate")]
    public void Generate()
    {
        if (path == null || railPrefab == null || path.Count < 2) return;

        Clear();
        var root = new GameObject(RootName).transform;
        root.SetParent(transform, false);

        int n = path.Count;
        // krok w indeksach tak, by odcinek miał ok. segmentLength
        float spacing = path.Length / Mathf.Max(1, n - 1);
        int step = Mathf.Max(1, Mathf.RoundToInt(segmentLength / Mathf.Max(0.1f, spacing)));

        for (int i = 0; i < n - 1; i += step)
        {
            int j = Mathf.Min(i + step, n - 1);
            Vector3 a = path.PointAt(i), b = path.PointAt(j);
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.2f) continue;

            Vector3 center = (a + b) * 0.5f;
            Quaternion rot = Quaternion.LookRotation(d / len, Vector3.up);
            Vector3 scale = new Vector3(1f, 1f, len / segmentLength);

            // rodzaj odcinka: najwyższy z punktów (tunel > most > zwykły)
            TrackPath.Kind kind = TrackPath.Kind.Ground;
            for (int k = i; k <= j; k++)
            {
                var kk = path.KindAt(k);
                if (kk == TrackPath.Kind.Tunnel) kind = TrackPath.Kind.Tunnel;
                else if (kk == TrackPath.Kind.Bridge && kind == TrackPath.Kind.Ground) kind = TrackPath.Kind.Bridge;
            }

            Spawn(railPrefab, root, center, rot, scale);
            if (kind == TrackPath.Kind.Bridge && bridgePrefab != null) Spawn(bridgePrefab, root, center, rot, scale);
            if (kind == TrackPath.Kind.Tunnel && tunnelPrefab != null) Spawn(tunnelPrefab, root, center, rot, scale);
        }
    }

    public void Clear()
    {
        var old = transform.Find(RootName);
        if (old == null) return;
        if (Application.isPlaying) Destroy(old.gameObject);
        else DestroyImmediate(old.gameObject);
    }

    static void Spawn(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        GameObject go;
#if UNITY_EDITOR
        go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
#else
        go = Instantiate(prefab, parent);
#endif
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = scale;
        go.isStatic = true;
    }
}
