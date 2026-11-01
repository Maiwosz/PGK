using UnityEngine;

/// <summary>
/// Ścieżka toru: gęsta łamana (punkty co kilka metrów) z rodzajem odcinka (zwykły / most / tunel).
/// Korzystają z niej generator toru, a później pociąg i warunki misji.
/// </summary>
public class TrackPath : MonoBehaviour
{
    public enum Kind : byte { Ground = 0, Bridge = 1, Tunnel = 2 }

    [SerializeField] Vector3[] points = new Vector3[0];
    [SerializeField] byte[] kinds = new byte[0];

    float[] cumulative;

    public int Count => points.Length;
    public Vector3 PointAt(int i) => points[Mathf.Clamp(i, 0, points.Length - 1)];
    public Kind KindAt(int i) => (Kind)kinds[Mathf.Clamp(i, 0, kinds.Length - 1)];

    public float Length
    {
        get
        {
            EnsureCumulative();
            return cumulative.Length > 0 ? cumulative[cumulative.Length - 1] : 0f;
        }
    }

    /// <summary>Punkty w przestrzeni świata – ścieżka zapisana jest w współrzędnych świata.</summary>
    public void Set(Vector3[] worldPoints, byte[] pointKinds)
    {
        points = worldPoints;
        kinds = pointKinds;
        cumulative = null;
    }

    /// <summary>Styczna (kierunek jazdy) w punkcie i.</summary>
    public Vector3 TangentAt(int i)
    {
        int a = Mathf.Clamp(i - 1, 0, points.Length - 1);
        int b = Mathf.Clamp(i + 1, 0, points.Length - 1);
        Vector3 d = points[b] - points[a];
        return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
    }

    /// <summary>Pozycja i kierunek po przejechaniu dystansu d (m) od początku ścieżki.</summary>
    public void SampleAtDistance(float distance, out Vector3 position, out Vector3 forward)
    {
        EnsureCumulative();
        int n = points.Length;
        if (n < 2) { position = n == 1 ? points[0] : Vector3.zero; forward = Vector3.forward; return; }
        distance = Mathf.Clamp(distance, 0f, cumulative[n - 1]);

        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (cumulative[mid] <= distance) lo = mid; else hi = mid;
        }
        float seg = cumulative[hi] - cumulative[lo];
        float t = seg > 1e-5f ? (distance - cumulative[lo]) / seg : 0f;
        position = Vector3.Lerp(points[lo], points[hi], t);
        forward = (points[hi] - points[lo]).normalized;
    }

    void EnsureCumulative()
    {
        if (cumulative != null && cumulative.Length == points.Length) return;
        cumulative = new float[points.Length];
        for (int i = 1; i < points.Length; i++)
            cumulative[i] = cumulative[i - 1] + Vector3.Distance(points[i - 1], points[i]);
    }
}
