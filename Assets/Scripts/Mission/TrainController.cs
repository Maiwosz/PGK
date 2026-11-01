using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pociąg jadący po TrackPath: lokomotywa i wagony ustawiane są na ścieżce parami wózków, więc na łukach
/// i wzniesieniach wagony poprawnie się wychylają. Prędkość = prędkość bazowa (km/h) × modyfikator.
/// Wagony mają kinematyczne kolizje – rower, który w nie wjedzie, ulega wypadkowi.
/// </summary>
public class TrainController : MonoBehaviour
{
    [SerializeField] TrackPath path;
    [SerializeField] GameObject locomotivePrefab;
    [SerializeField] GameObject wagonPrefab;
    [SerializeField] Material[] wagonBodyMaterials;
    [SerializeField] int wagonCount = 6;

    [Header("Wymiary")]
    [SerializeField] float carLength = 18f;
    [SerializeField] float gap = 1.2f;
    [Tooltip("Odległość między wózkami wagonu (m) – określa, jak wagon układa się na łuku.")]
    [SerializeField] float bogieSpacing = 11f;
    [Tooltip("Wysokość pivotu wagonu nad osią toru (szyny wystają ok. 0,3 m ponad podsypkę).")]
    [SerializeField] float railHeight = 0.3f;

    [Header("Ruch")]
    [Tooltip("Prędkość pociągu w grze oryginalnej (ok. 90 km/h).")]
    [SerializeField] float baseSpeedKmh = 90f;
    [Tooltip("Mnożnik prędkości – do strojenia trudności (1 = jak w oryginale).")]
    [SerializeField, Range(0.05f, 1.5f)] float speedMultiplier = 0.3f;
    [SerializeField] float acceleration = 0.8f;
    [Tooltip("Po ilu sekundach od startu pociąg rusza.")]
    [SerializeField] float startDelay = 3f;
    [Tooltip("Zapas (m) między końcem składu a początkiem trasy przy starcie.")]
    [SerializeField] float startMargin = 8f;

    readonly List<Transform> cars = new List<Transform>();
    readonly List<Rigidbody> bodies = new List<Rigidbody>();
    float frontDistance;
    float speed;
    float elapsed;

    public float Speed => speed;
    public float SpeedKmh => speed * 3.6f;
    public float TargetSpeed => baseSpeedKmh / 3.6f * speedMultiplier;
    public float TrainLength => (wagonCount + 1) * carLength + wagonCount * gap;
    /// <summary>Dystans czoła pociągu od początku ścieżki (m).</summary>
    public float FrontDistance => frontDistance;
    public float RemainingDistance => path != null ? Mathf.Max(0f, path.Length - frontDistance) : 0f;
    public float SpeedMultiplier { get => speedMultiplier; set => speedMultiplier = Mathf.Clamp(value, 0.05f, 1.5f); }

    void Start()
    {
        if (path == null || locomotivePrefab == null || wagonPrefab == null) { enabled = false; return; }

        frontDistance = TrainLength + startMargin;
        for (int i = 0; i <= wagonCount; i++)
        {
            var go = Instantiate(i == 0 ? locomotivePrefab : wagonPrefab, transform);
            go.name = i == 0 ? "Locomotive" : "Wagon" + i;
            if (i > 0 && wagonBodyMaterials != null && wagonBodyMaterials.Length > 0)
            {
                var body = go.transform.Find("Body");
                if (body != null) body.GetComponent<Renderer>().sharedMaterial = wagonBodyMaterials[(i - 1) % wagonBodyMaterials.Length];
            }
            cars.Add(go.transform);
            bodies.Add(go.GetComponent<Rigidbody>());
        }
        Place(false);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        elapsed += dt;
        if (elapsed >= startDelay) speed = Mathf.MoveTowards(speed, TargetSpeed, acceleration * dt);

        float max = path.Length;
        frontDistance = Mathf.Min(frontDistance + speed * dt, max);
        if (frontDistance >= max) speed = 0f;      // koniec trasy (za Frederick Bridge) – pociąg staje
        Place(true);
    }

    void Place(bool physics)
    {
        for (int i = 0; i < cars.Count; i++)
        {
            float center = frontDistance - carLength * 0.5f - i * (carLength + gap);
            path.SampleAtDistance(center + bogieSpacing * 0.5f, out Vector3 front, out _);
            path.SampleAtDistance(center - bogieSpacing * 0.5f, out Vector3 rear, out _);
            Vector3 d = front - rear;
            if (d.sqrMagnitude < 1e-4f) continue;
            Vector3 pos = (front + rear) * 0.5f + Vector3.up * railHeight;
            Quaternion rot = Quaternion.LookRotation(d.normalized, Vector3.up);
            if (physics && bodies[i] != null) { bodies[i].MovePosition(pos); bodies[i].MoveRotation(rot); }
            else cars[i].SetPositionAndRotation(pos, rot);
        }
    }

    /// <summary>Najmniejsza odległość (m) od punktu do środka któregokolwiek wagonu.</summary>
    public float DistanceTo(Vector3 point)
    {
        float best = float.MaxValue;
        foreach (var c in cars) best = Mathf.Min(best, Vector3.Distance(point, c.position));
        return best == float.MaxValue ? 0f : best;
    }
}
