using UnityEngine;

/// <summary>
/// Automatyczna wieżyczka: "drugi człowiek" siedzący za rowerzystą (za kamerą i nad nią). Sama namierza najbliższego gangstera,
/// do którego ma linię strzału i który jest w zasięgu, obraca się ku niemu i namierza. Namierzenie pozwala oddać krótką serię,
/// po której trzeba namierzać od nowa. Tempo namierzania i celność zależą od stabilności jazdy (zakręty, szarpanie kierownicą,
/// wyboje, hamowanie i gwałtowne zmiany prędkości rozstrajają celowanie). Strzały mają losowy rozrzut – im dalej cel,
/// tym mniejsza szansa trafienia. Smugi pocisków są tylko wizualne.
/// </summary>
public class AutoTurret : MonoBehaviour
{
    public enum TurretState { Standby, OutOfRange, Blocked, Acquiring, Firing, Reloading, Off }

    [Header("Montaż")]
    [Tooltip("Obiekt, względem którego ustawiona jest wieżyczka (kamera roweru).")]
    [SerializeField] Transform anchor;
    [Tooltip("Pozycja lufy w układzie kamery: x w prawo, y w górę, z do przodu (ujemne = za kamerą).")]
    [SerializeField] Vector3 muzzleOffset = new Vector3(0.3f, 0.6f, -0.8f);
    [SerializeField] Transform bikeRoot;
    [Tooltip("Rower, którego stabilność jazdy wpływa na namierzanie.")]
    [SerializeField] BikeController bike;

    [Header("Namierzanie")]
    [SerializeField] float range = 55f;
    [Tooltip("Czas (s) od wybrania nowego celu do pierwszego strzału.")]
    [SerializeField] float acquireTime = 0.7f;
    [Tooltip("Prędkość obrotu lufy (stopnie/s).")]
    [SerializeField] float slewSpeed = 150f;
    [Tooltip("Maksymalny błąd celowania (stopnie), przy którym wieżyczka już strzela.")]
    [SerializeField] float fireCone = 4f;
    [Tooltip("Po utracie linii strzału wieżyczka trzyma cel jeszcze tyle sekund.")]
    [SerializeField] float lostGrace = 0.25f;

    [Header("Serie i stabilność jazdy")]
    [Tooltip("Liczba strzałów w serii po jednym namierzeniu (losowo od–do).")]
    [SerializeField] int burstMin = 4;
    [SerializeField] int burstMax = 7;
    [Tooltip("Przerwa (s) po serii, zanim zacznie się nowe namierzanie.")]
    [SerializeField] float burstPause = 0.35f;
    [Tooltip("Stabilność jazdy (0..1) poniżej tej wartości zrywa namierzenie i przerywa serię.")]
    [SerializeField, Range(0f, 1f)] float breakStability = 0.12f;
    [Tooltip("Jak szybko (ułamek normalnego tempa) namierza się przy zupełnie niestabilnej jeździe.")]
    [SerializeField, Range(0.02f, 1f)] float minLockRate = 0.12f;
    [Tooltip("Mnożnik szansy trafienia przy zupełnie niestabilnej jeździe.")]
    [SerializeField, Range(0.05f, 1f)] float minAccuracyFactor = 0.3f;

    [Header("Ogień")]
    [SerializeField] float fireInterval = 0.09f;
    [SerializeField] int magazine = 30;
    [SerializeField] float reloadTime = 1.8f;
    [SerializeField] float damage = 7f;
    [Tooltip("Szansa trafienia z bliska i z maksymalnego zasięgu.")]
    [SerializeField, Range(0f, 1f)] float hitChanceNear = 0.85f;
    [SerializeField, Range(0f, 1f)] float hitChanceFar = 0.3f;
    [Tooltip("O ile (m) pocisk mija cel przy chybieniu.")]
    [SerializeField] float missSpread = 2.2f;

    [Header("Efekty")]
    [SerializeField] Material tracerMaterial;
    [SerializeField] float tracerLife = 0.09f;
    [SerializeField, Range(0f, 1f)] float volume = 0.35f;

    // ---- stan
    readonly RaycastHit[] hits = new RaycastHit[24];
    Gangster target;
    Vector3 aimDir = Vector3.forward;
    float lockProgress;          // 0..1 – postęp namierzania; pełne = można oddać serię
    int burstLeft;               // ile strzałów zostało w bieżącej serii
    float pause;                 // przerwa po serii
    // stabilność jazdy
    float stability = 1f;        // 1 = płynna jazda, 0 = rower mocno rzuca
    float disturbance;
    float prevSteer, prevSpeed, prevVy;
    bool havePrev;
    Rigidbody bikeBody;
    HandlebarSteering steering;
    float lostTimer;
    float fireTimer;
    int ammo;
    float reloadTimer;
    bool reloading;
    float scanTimer;
    bool anyAlive, anyInRange, anyVisible;
    float nearestDistance;
    bool active = true;
    TurretState state = TurretState.Standby;

    LineRenderer[] tracers;
    float[] tracerTimers;
    int nextTracer;
    AudioSource audioSource;
    AudioClip shotClip, hitClip, killClip;

    public TurretState State => state;
    public Gangster Target => target;
    public bool Active { get => active; set { active = value; if (!value) { target = null; state = TurretState.Off; } } }
    public float Range => range;
    public int Ammo => ammo;
    public int Magazine => magazine;
    public bool Reloading => reloading;
    public float ReloadProgress => reloading ? 1f - reloadTimer / Mathf.Max(0.01f, reloadTime) : 1f;
    public float AcquireProgress => target != null ? lockProgress : 0f;
    /// <summary>Stabilność jazdy 0..1 (1 = płynna); wpływa na tempo namierzania i celność.</summary>
    public float Stability => stability;
    public bool InBurst => burstLeft > 0;
    public float NearestDistance => nearestDistance;
    public Vector3 Muzzle => anchor != null ? anchor.TransformPoint(muzzleOffset) : transform.position;

    // statystyki
    public int ShotsFired { get; private set; }
    public int Hits { get; private set; }
    public int Kills { get; private set; }
    public float Accuracy => ShotsFired > 0 ? Hits / (float)ShotsFired : 0f;

    public string StatusText
    {
        get
        {
            switch (state)
            {
                case TurretState.Off: return "WYŁĄCZONA";
                case TurretState.Standby: return anyAlive ? "SZUKA CELU" : "BRAK CELÓW";
                case TurretState.OutOfRange: return "POZA ZASIĘGIEM";
                case TurretState.Blocked: return "LINIA STRZAŁU ZASŁONIĘTA";
                case TurretState.Acquiring: return stability < 0.45f ? "NAMIERZANIE ZAKŁÓCONE" : "NAMIERZA…";
                case TurretState.Firing: return "OGIEŃ";
                default: return "PRZEŁADOWANIE";
            }
        }
    }

    void Awake()
    {
        ammo = magazine;
        if (anchor != null) aimDir = anchor.forward;
        if (bike == null) bike = FindFirstObjectByType<BikeController>();
        if (bike != null)
        {
            bikeBody = bike.GetComponent<Rigidbody>();
            steering = bike.GetComponentInChildren<HandlebarSteering>();
        }

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 0f;
        audioSource.playOnAwake = false;
        shotClip = MakeNoiseClip(0.11f, 14f, 0.9f);
        hitClip = MakeNoiseClip(0.05f, 40f, 0.5f);
        killClip = MakeNoiseClip(0.18f, 9f, 0.6f);

        tracers = new LineRenderer[10];
        tracerTimers = new float[tracers.Length];
        for (int i = 0; i < tracers.Length; i++)
        {
            var go = new GameObject("Tracer" + i);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.05f;
            lr.sharedMaterial = tracerMaterial;
            lr.startColor = new Color(1f, 1f, 0.7f, 1f);
            lr.endColor = new Color(1f, 0.55f, 0.1f, 0.2f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            tracers[i] = lr;
        }
    }

    /// <summary>Przywraca stan początkowy (restart misji).</summary>
    public void ResetTurret()
    {
        target = null;
        lockProgress = 0f;
        burstLeft = 0;
        pause = 0f;
        disturbance = 0f;
        stability = 1f;
        havePrev = false;
        lostTimer = 0f;
        fireTimer = 0f;
        ammo = magazine;
        reloading = false;
        reloadTimer = 0f;
        ShotsFired = Hits = Kills = 0;
        active = true;
        state = TurretState.Standby;
        if (anchor != null) aimDir = anchor.forward;
        foreach (var t in tracers) t.enabled = false;
    }

    // Stabilność liczymy w kroku fizyki: rower ustawia prędkość w FixedUpdate, więc różniczkowanie co klatkę dawałoby szum.
    void FixedUpdate() => UpdateStability(Time.fixedDeltaTime);

    void Update()
    {
        float dt = Time.deltaTime;
        UpdateTracers(dt);
        if (!active || anchor == null) return;

        if (target != null && !target.Alive) { target = null; scanTimer = 0f; }   // cel zabity – od razu szukamy następnego
        scanTimer -= dt;
        if (scanTimer <= 0f) { scanTimer = 0.1f; Scan(); }

        // Obrót lufy ku celowi (bez celu wraca do kierunku jazdy).
        Vector3 desired = target != null ? (target.AimPosition - Muzzle).normalized : anchor.forward;
        aimDir = Vector3.RotateTowards(aimDir, desired, slewSpeed * Mathf.Deg2Rad * dt, 0f);

        if (reloading)
        {
            reloadTimer -= dt;
            if (reloadTimer <= 0f) { reloading = false; ammo = magazine; }
            state = TurretState.Reloading;
            return;
        }

        if (target == null)
        {
            lockProgress = 0f;
            burstLeft = 0;
            pause = 0f;
            state = anyVisible ? TurretState.Acquiring : anyInRange ? TurretState.Blocked : anyAlive ? TurretState.OutOfRange : TurretState.Standby;
            return;
        }

        float err = Vector3.Angle(aimDir, desired);
        if (burstLeft <= 0)
        {
            // Namierzanie: postęp rośnie tym szybciej, im stabilniej jedziemy; przy zupełnym rozstrojeniu zanika.
            if (pause > 0f) pause -= dt;
            else if (err <= fireCone * 3f)
            {
                lockProgress += Mathf.Lerp(minLockRate, 1f, stability) / Mathf.Max(0.05f, acquireTime) * dt;
                if (stability < breakStability) lockProgress -= 2f * dt / Mathf.Max(0.05f, acquireTime);
                lockProgress = Mathf.Clamp01(lockProgress);
                if (lockProgress >= 1f)
                {
                    burstLeft = Random.Range(burstMin, burstMax + 1);
                    fireTimer = 0f;
                }
            }
            state = TurretState.Acquiring;
            if (burstLeft <= 0) return;
        }

        // Seria: gwałtowne szarpnięcie rowerem zrywa namierzenie w środku serii.
        state = TurretState.Firing;
        if (stability < breakStability || err > fireCone * 3f) { EndBurst(); state = TurretState.Acquiring; return; }
        fireTimer -= dt;
        if (fireTimer <= 0f)
        {
            fireTimer = fireInterval;
            Shoot();
            burstLeft--;
            if (burstLeft <= 0) EndBurst();
            if (ammo <= 0) { EndBurst(); reloading = true; reloadTimer = reloadTime; }
        }
    }

    /// <summary>Koniec serii: przerwa i namierzanie od zera.</summary>
    void EndBurst()
    {
        burstLeft = 0;
        lockProgress = 0f;
        pause = burstPause;
    }

    /// <summary>
    /// Stabilność jazdy: zakręty (przyspieszenie boczne), szarpanie kierownicą, wyboje (zmiany prędkości pionowej),
    /// gwałtowne przyspieszanie i hamowanie, cofanie i upadek rozstrajają celowanie. Zaburzenie rośnie szybko,
    /// a opada wolno – po szarpnięciu celowanie dochodzi do siebie przez ułamek sekundy.
    /// </summary>
    void UpdateStability(float dt)
    {
        if (bike == null || dt <= 0f) return;
        float raw;
        if (bike.Fallen) raw = 1f;
        else
        {
            float lat = Mathf.Clamp01(Mathf.Abs(bike.Speed * bike.YawRate) / 5f);
            float angle = steering != null ? steering.Angle : 0f;
            float vy = bikeBody != null ? bikeBody.linearVelocity.y : 0f;
            float steer = 0f, vert = 0f, lon = 0f;
            if (havePrev)
            {
                steer = Mathf.Clamp01(Mathf.Abs(angle - prevSteer) / dt / 90f);
                vert = Mathf.Clamp01(Mathf.Abs(vy - prevVy) / dt / 9f);
                lon = Mathf.Clamp01(Mathf.Abs(bike.Speed - prevSpeed) / dt / 3.5f);
            }
            prevSteer = angle; prevSpeed = bike.Speed; prevVy = vy; havePrev = true;
            float brake = bike.FrontBrake || bike.RearBrake ? 0.2f : 0f;
            raw = Mathf.Clamp01(0.6f * lat + 0.4f * steer + 0.4f * vert + 0.35f * lon + brake + (bike.Reversing ? 0.4f : 0f));
        }
        float rate = raw > disturbance ? 12f : 2.2f;
        disturbance = Mathf.Lerp(disturbance, raw, 1f - Mathf.Exp(-rate * dt));
        stability = 1f - disturbance;
    }

    /// <summary>Wybiera cel: trzyma obecny, dopóki widoczny; w przeciwnym razie najbliższy widoczny w zasięgu.</summary>
    void Scan()
    {
        anyAlive = anyInRange = anyVisible = false;
        nearestDistance = float.MaxValue;
        Vector3 muzzle = Muzzle;
        Gangster best = null;
        float bestDist = float.MaxValue;
        bool targetVisible = false;

        foreach (var g in Gangster.All)
        {
            if (!g.Alive) continue;
            anyAlive = true;
            float d = Vector3.Distance(muzzle, g.AimPosition);
            nearestDistance = Mathf.Min(nearestDistance, d);
            if (d > range) continue;
            anyInRange = true;
            if (!HasLineOfSight(g, muzzle)) continue;
            anyVisible = true;
            if (g == target) targetVisible = true;
            if (d < bestDist) { bestDist = d; best = g; }
        }

        if (target != null && (!target.Alive)) { target = null; }
        if (target != null && targetVisible) { lostTimer = 0f; return; }       // trzymamy cel
        if (target != null)
        {
            lostTimer += 0.1f;
            if (lostTimer < lostGrace) return;
        }
        if (best != target) { target = best; lockProgress = 0f; burstLeft = 0; }
        lostTimer = 0f;
    }

    bool HasLineOfSight(Gangster g, Vector3 from)
    {
        Vector3 to = g.AimPosition;
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist < 0.01f) return true;
        int n = Physics.RaycastNonAlloc(from, dir / dist, hits, dist + 0.2f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        Collider nearestCol = null;
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            var col = h.collider;
            if (bikeRoot != null && col.transform.IsChildOf(bikeRoot)) continue;
            var owner = col.GetComponentInParent<Gangster>();
            // Wagon, na którym stoi cel, nie zasłania go (dach i pudło kolizji są pod nogami gangstera).
            if (owner == null && g.Carrier != null && col.attachedRigidbody == g.Carrier) continue;
            if (owner != null && !owner.Alive) continue;
            if (h.distance < nearest) { nearest = h.distance; nearestCol = col; }
        }
        return nearestCol != null && nearestCol.GetComponentInParent<Gangster>() == g;
    }

    void Shoot()
    {
        ammo--;
        ShotsFired++;
        Vector3 from = Muzzle;
        float d = Vector3.Distance(from, target.AimPosition);
        float chance = Mathf.Lerp(hitChanceNear, hitChanceFar, Mathf.Clamp01(d / range)) * Mathf.Lerp(minAccuracyFactor, 1f, stability);
        bool hit = Random.value < chance;
        Vector3 end;
        if (hit)
        {
            end = target.AimPosition + Random.insideUnitSphere * 0.15f;
            Hits++;
            bool killed = target.TakeDamage(damage);
            if (killed) { Kills++; Play(killClip, 1f); }
            else Play(hitClip, 0.8f);
        }
        else
        {
            Vector3 miss = Random.onUnitSphere;
            end = target.AimPosition + miss * Random.Range(0.9f, missSpread) * Mathf.Lerp(1.8f, 1f, stability);
        }
        // Smuga leci w stronę celu i kończy się za nim (przy trafieniu – na nim).
        Vector3 dir = (end - from).normalized;
        if (!hit) end += dir * 6f;
        SpawnTracer(from, end);
        Play(shotClip, 1f);
    }

    void SpawnTracer(Vector3 a, Vector3 b)
    {
        var lr = tracers[nextTracer];
        tracerTimers[nextTracer] = tracerLife;
        nextTracer = (nextTracer + 1) % tracers.Length;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        lr.enabled = true;
    }

    void UpdateTracers(float dt)
    {
        for (int i = 0; i < tracers.Length; i++)
        {
            if (!tracers[i].enabled) continue;
            tracerTimers[i] -= dt;
            if (tracerTimers[i] <= 0f) tracers[i].enabled = false;
        }
    }

    void Play(AudioClip clip, float gain)
    {
        if (clip == null) return;
        audioSource.pitch = Random.Range(0.92f, 1.08f);
        audioSource.PlayOneShot(clip, volume * gain);
    }

    /// <summary>Krótki szum z szybko gasnącą obwiednią (strzał / trafienie) – bez plików dźwiękowych.</summary>
    internal static AudioClip MakeNoiseClip(float seconds, float decay, float lowpass)
    {
        const int rate = 22050;
        int n = Mathf.CeilToInt(rate * seconds);
        var data = new float[n];
        var rng = new System.Random(1234 + n);
        float prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            prev = Mathf.Lerp(noise, prev, lowpass);
            data[i] = prev * Mathf.Exp(-t * decay * 10f / Mathf.Max(0.05f, seconds * 10f)) * 0.9f;
        }
        var clip = AudioClip.Create("sfx", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
