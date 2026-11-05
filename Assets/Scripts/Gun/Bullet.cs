using UnityEngine;

/// <summary>
/// Pocisk z broni ręcznej (<see cref="Gun"/>). Leci po prostej z zadaną prędkością; trafienie wykrywa promieniem
/// wzdłuż odcinka przebytego w danej klatce, więc nie przelatuje przez cienkie obiekty. Trafiony gangster
/// dostaje obrażenia, trafione ciało sztywne – lekki impuls. Po trafieniu albo po czasie życia pocisk znika.
/// </summary>
public class Bullet : MonoBehaviour
{
    [Tooltip("Impuls (N·s) przekazywany trafionemu ciału sztywnemu.")]
    [SerializeField] float hitImpulse = 2f;

    Vector3 velocity;
    float damage;
    float life;
    Collider[] ignored;
    Gun owner;
    readonly RaycastHit[] hits = new RaycastHit[8];

    // Ogonek (opcjonalny): odcinek o stałej długości ciągnący się za pociskiem. Po zakończeniu lotu jego koniec
    // dalej "dolatuje" do miejsca trafienia, a ogonek przy tym gaśnie.
    LineRenderer line;
    Color lineColor;
    float lineFade;
    float fadeLeft;
    float tailLength;
    Vector3 origin;      // miejsce wystrzału – ogonek nie sięga dalej wstecz niż wylot lufy
    float travelled;     // droga przebyta przez pocisk od wylotu
    float tailBack;      // droga przebyta przez koniec ogonka (po zakończeniu lotu goni czoło)
    bool finished;

    /// <summary>
    /// Wystrzeliwuje pocisk; <paramref name="ignore"/> – collidery broni, z której padł strzał;
    /// <paramref name="owner"/> – broń, której zgłaszane są trafienia (statystyki).
    /// </summary>
    public void Launch(Vector3 velocity, float damage, float lifetime, Collider[] ignore, Gun owner = null)
    {
        this.velocity = velocity;
        this.damage = damage;
        life = lifetime;
        ignored = ignore;
        this.owner = owner;
    }

    /// <summary>
    /// Włącza ogonek za pociskiem: odcinek o długości <paramref name="length"/> (m) od pocisku wstecz wzdłuż toru,
    /// zwężający się i przezroczysty na końcu. Tuż po strzale jest krótszy – nie wystaje za wylot lufy.
    /// Po trafieniu albo po czasie życia pocisk znika, a ogonek dolatuje do miejsca trafienia i gaśnie
    /// w ciągu <paramref name="fadeTime"/> sekund.
    /// </summary>
    public void ShowShotLine(Material material, float width, Color color, float fadeTime, float length)
    {
        var go = new GameObject("ShotLine");
        go.transform.SetParent(transform, false);
        line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.widthMultiplier = width;
        line.widthCurve = AnimationCurve.Linear(0f, 0.25f, 1f, 1f); // koniec ogonka cieńszy niż czoło
        line.sharedMaterial = material;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        lineColor = color;
        lineFade = fadeTime;
        tailLength = Mathf.Max(0f, length);
        origin = transform.position;
        travelled = 0f;
        tailBack = 0f;
        UpdateLine();
        SetLineAlpha(1f);
    }

    /// <summary>Ustawia ogonek: od punktu na torze (najdalej <see cref="tailLength"/> za czołem) do pocisku.</summary>
    void UpdateLine()
    {
        Vector3 dir = velocity.sqrMagnitude > 1e-8f ? velocity.normalized : transform.forward;
        float back = Mathf.Max(tailBack, travelled - tailLength);
        line.SetPosition(0, origin + dir * back);   // koniec ogonka
        line.SetPosition(1, origin + dir * travelled); // czoło (pocisk / miejsce trafienia)
    }

    /// <summary>Domyślny wygląd pocisku, gdy broń nie ma przypisanego prefabu: mała kula bez kolizji.</summary>
    public static Bullet CreateDefault(Vector3 position, Quaternion rotation)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Bullet";
        var col = go.GetComponent<Collider>();
        col.enabled = false;
        Destroy(col);
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = Vector3.one * 0.04f;
        return go.AddComponent<Bullet>();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (finished) { UpdateFade(dt); return; }

        life -= dt;
        if (life <= 0f) { Finish(); return; }

        Vector3 step = velocity * dt;
        float dist = step.magnitude;
        if (dist < 1e-6f) return;

        if (TryHit(transform.position, step / dist, dist, out RaycastHit hit))
        {
            OnHit(hit);
            return;
        }
        transform.position += step;
        travelled += dist;
        if (line != null) UpdateLine();
    }

    /// <summary>Koniec lotu: bez ogonka pocisk od razu znika; z ogonkiem – chowa się, a ogonek dolatuje i gaśnie.</summary>
    void Finish()
    {
        if (line == null || lineFade <= 0f) { Destroy(gameObject); return; }

        finished = true;
        fadeLeft = lineFade;
        tailBack = Mathf.Max(0f, travelled - tailLength);
        UpdateLine();
        foreach (var r in GetComponentsInChildren<Renderer>())
            if (r != line) r.enabled = false;
    }

    void UpdateFade(float dt)
    {
        fadeLeft -= dt;
        // Koniec ogonka leci dalej z prędkością pocisku, aż dogoni miejsca trafienia.
        tailBack += velocity.magnitude * dt;
        if (fadeLeft <= 0f || tailBack >= travelled) { Destroy(gameObject); return; }
        UpdateLine();
        SetLineAlpha(fadeLeft / lineFade);
    }

    void SetLineAlpha(float a)
    {
        Color head = lineColor;
        head.a *= a;
        Color tail = head;
        tail.a = 0f;
        line.startColor = tail;   // koniec ogonka – całkiem przezroczysty
        line.endColor = head;     // czoło przy pocisku / w miejscu trafienia
    }

    bool TryHit(Vector3 from, Vector3 dir, float dist, out RaycastHit best)
    {
        best = default;
        int count = Physics.RaycastNonAlloc(from, dir, hits, dist, ~0, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Collider c = hits[i].collider;
            if (hits[i].distance >= bestDist) continue;
            if (c.transform.IsChildOf(transform)) continue;                       // własny collider (prefab pocisku)
            if (ignored != null && System.Array.IndexOf(ignored, c) >= 0) continue; // broń, z której padł strzał
            best = hits[i];
            bestDist = hits[i].distance;
            found = true;
        }
        return found;
    }

    void OnHit(RaycastHit hit)
    {
        var gangster = hit.collider.GetComponentInParent<Gangster>();
        if (gangster != null)
        {
            // Trafienie liczy się tylko w żywego gangstera (ciało po śmierci nie ma już aktywnych colliderów).
            if (gangster.Alive)
            {
                bool killed = gangster.TakeDamage(damage);
                if (owner != null) owner.RegisterHit(killed);
            }
        }
        else if (hit.rigidbody != null)
            hit.rigidbody.AddForceAtPosition(velocity.normalized * hitImpulse, hit.point, ForceMode.Impulse);

        travelled += Vector3.Distance(transform.position, hit.point);
        transform.position = hit.point;
        Finish();
    }
}
