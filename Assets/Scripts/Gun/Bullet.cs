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

    // Linia strzału (opcjonalna): od wylotu lufy do bieżącej pozycji pocisku, po zakończeniu lotu gaśnie.
    LineRenderer line;
    Color lineColor;
    float lineFade;
    float fadeLeft;
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
    /// Włącza linię strzału: od miejsca wystrzału do pocisku (przy lufie bledsza). Po trafieniu albo po czasie życia
    /// pocisk znika, a linia zostaje jeszcze <paramref name="fadeTime"/> sekund i gaśnie.
    /// </summary>
    public void ShowShotLine(Material material, float width, Color color, float fadeTime)
    {
        var go = new GameObject("ShotLine");
        go.transform.SetParent(transform, false);
        line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.widthMultiplier = width;
        line.sharedMaterial = material;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.SetPosition(0, transform.position);
        line.SetPosition(1, transform.position);
        lineColor = color;
        lineFade = fadeTime;
        SetLineAlpha(1f);
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
        if (line != null) line.SetPosition(1, transform.position);
    }

    /// <summary>Koniec lotu: bez linii pocisk od razu znika; z linią – chowa się, a linia gaśnie.</summary>
    void Finish()
    {
        if (line == null || lineFade <= 0f) { Destroy(gameObject); return; }

        finished = true;
        fadeLeft = lineFade;
        line.SetPosition(1, transform.position);
        foreach (var r in GetComponentsInChildren<Renderer>())
            if (r != line) r.enabled = false;
    }

    void UpdateFade(float dt)
    {
        fadeLeft -= dt;
        if (fadeLeft <= 0f) { Destroy(gameObject); return; }
        SetLineAlpha(fadeLeft / lineFade);
    }

    void SetLineAlpha(float a)
    {
        Color end = lineColor;
        end.a *= a;
        Color start = end;
        start.a *= 0.25f;
        line.startColor = start;   // przy lufie
        line.endColor = end;       // przy pocisku / w miejscu trafienia
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

        transform.position = hit.point;
        Finish();
    }
}
