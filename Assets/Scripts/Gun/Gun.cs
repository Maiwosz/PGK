using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Broń chwytana myszką i przypięta do roweru.
/// - Bez chwytu broń wisi na uchwycie (GunHoldPoint w GunHolder na rowerze) i jedzie razem z rowerem.
/// - Przytrzymanie LPM na broni "łapie" ją: broń przechodzi do układu ramy roweru (obiekt Visual – ten sam,
///   w którym siedzi kamera), więc jej pozycja jest liczona względem roweru i nie zostaje w tyle podczas jazdy.
///   Ruch myszy przesuwa ją po płaszczyźnie równoległej do ekranu, a obrót zależy od odległości kursora
///   od punktu odniesienia (środek ekranu albo środek roweru na ekranie): na środku 0°, na krawędzi
///   maksymalnie <see cref="maxYaw"/> w poziomie i <see cref="maxPitch"/> w pionie.
/// - Podczas trzymania kamera płynnie obraca się w stronę celowania (część wychylenia broni), po puszczeniu wraca.
/// - Po puszczeniu LPM broń wraca na uchwyt – natychmiast albo płynnie (<see cref="instantReturn"/>).
/// - PPM oddaje jeden strzał na kliknięcie, tylko gdy broń jest trzymana; pocisk leci zgodnie z aktualnym obrotem lufy.
/// Logika działa w LateUpdate, czyli po pochyleniu roweru i obrocie kierownicy w danej klatce – bez drgań.
/// </summary>
public class Gun : MonoBehaviour
{
    public enum State { Idle, Hover, Grabbed }
    public enum AimReference { ScreenCenter, BikeCenter }

    [SerializeField] Camera viewCamera;
    [Tooltip("Zasięg promienia do łapania broni (m).")]
    [SerializeField] float reachDistance = 10f;
    [Tooltip("Jak szybko broń dogania kursor i docelowy obrót (większe = sztywniej).")]
    [SerializeField] float followSharpness = 30f;

    [Header("Mocowanie na rowerze")]
    [Tooltip("Uchwyt, na którym wisi broń. Puste = obiekt 'GunHoldPoint' (albo 'GunHolder') w rowerze ze sceny.")]
    [SerializeField] Transform holder;
    [Tooltip("Układ roweru, względem którego liczona jest pozycja i obrót trzymanej broni. Puste = 'Visual' roweru (rama z kamerą).")]
    [SerializeField] Transform bikeFrame;

    [Header("Powrót po puszczeniu")]
    [Tooltip("Zaznaczone = broń od razu wraca na uchwyt; odznaczone = płynna animacja.")]
    [SerializeField] bool instantReturn = false;
    [Tooltip("Czas (s) płynnego powrotu na uchwyt.")]
    [SerializeField] float returnTime = 0.3f;

    [Header("Celowanie (podczas trzymania)")]
    [Tooltip("Od czego liczona jest odległość kursora: od środka ekranu albo od środka roweru widocznego na ekranie.")]
    [SerializeField] AimReference aimReference = AimReference.ScreenCenter;
    [Tooltip("Punkt 'środka roweru' dla trybu BikeCenter. Puste = kierownica roweru.")]
    [SerializeField] Transform bikeCenter;
    [Tooltip("Maksymalne wychylenie w poziomie (stopnie) – kursor przy lewej/prawej krawędzi ekranu.")]
    [SerializeField, Range(0f, 360f)] float maxYaw = 45f;
    [Tooltip("Maksymalne wychylenie w pionie (stopnie) – kursor przy górnej/dolnej krawędzi ekranu.")]
    [SerializeField, Range(0f, 360f)] float maxPitch = 30f;

    [Header("Kamera podczas celowania")]
    [Tooltip("Czy kamera obraca się w stronę celowania, gdy gracz trzyma broń.")]
    [SerializeField] bool rotateCamera = true;
    [Tooltip("Jaką część wychylenia broni przejmuje kamera (0 = wcale, 1 = patrzy tam, gdzie lufa).")]
    [SerializeField, Range(0f, 1f)] float cameraAimFactor = 0.6f;
    [Tooltip("Jak szybko kamera nadąża za celowaniem i wraca po puszczeniu broni (większe = szybciej).")]
    [SerializeField] float cameraSharpness = 8f;

    [Header("Strzał")]
    [Tooltip("Wylot lufy: pozycja = miejsce wylotu, oś Z = kierunek strzału. Puste = przód obiektu 'Nozzle' (albo samej broni).")]
    [SerializeField] Transform muzzle;
    [Tooltip("Opcjonalny prefab pocisku; puste = mała kula tworzona w locie.")]
    [SerializeField] Bullet bulletPrefab;
    [SerializeField] float bulletSpeed = 60f;
    [SerializeField] float bulletDamage = 34f;
    [Tooltip("Po ilu sekundach pocisk znika, jeśli w nic nie trafi.")]
    [SerializeField] float bulletLifetime = 3f;

    [Header("Dźwięk wystrzału")]
    [Tooltip("Dźwięk strzału. Puste = krótki szum generowany w kodzie (jak strzały wieżyczki, bez plików dźwiękowych).")]
    [SerializeField] AudioClip shotSound;
    [SerializeField, Range(0f, 1f)] float shotVolume = 0.6f;
    [Tooltip("Losowy rozrzut wysokości dźwięku (±), żeby kolejne strzały nie brzmiały identycznie.")]
    [SerializeField, Range(0f, 0.3f)] float shotPitchJitter = 0.08f;

    [Header("Linia strzału")]
    [Tooltip("Rysuje linię za pociskiem – od wylotu lufy do pocisku / miejsca trafienia.")]
    [SerializeField] bool showShotLine = false;
    [Tooltip("Materiał linii (np. Materials/Mission/Tracer). Puste = materiał z shaderem Sprites/Default.")]
    [SerializeField] Material shotLineMaterial;
    [SerializeField] float shotLineWidth = 0.02f;
    [SerializeField] Color shotLineColor = new Color(1f, 0.9f, 0.5f, 1f);
    [Tooltip("Ile sekund linia gaśnie po trafieniu albo końcu lotu pocisku.")]
    [SerializeField] float shotLineFade = 0.4f;

    [Header("Podświetlenie")]
    [SerializeField] Renderer targetRenderer;
    [SerializeField] Color idleColor = new Color(0.08f, 0.08f, 0.08f);
    [SerializeField] Color hoverColor = new Color(1f, 0.85f, 0.2f);
    [SerializeField] Color grabbedColor = new Color(0.25f, 0.95f, 0.35f);

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    MaterialPropertyBlock block;
    State state = State.Idle;

    // Miejsce spoczynku: uchwyt na rowerze (lub – bez roweru – pozycja startowa w scenie).
    Transform homeParent;
    Vector3 homeLocalPos;
    Quaternion homeLocalRot;
    // Płynny powrót (w układzie uchwytu, więc powrót "dogania" jadący rower).
    bool returning;
    float returnT;
    Vector3 returnFromPos;
    Quaternion returnFromRot;
    // Przeciąganie: przesunięcie broni względem punktu pod kursorem, w układzie rodzica (ramy roweru).
    Vector3 grabOffset;
    // Kamera: obrót spoczynkowy (lokalny) i bieżące, wygładzone wychylenie (stopnie: x = poziom, y = pion).
    Transform camTf;
    Quaternion camRestLocal;
    Vector2 camAim;

    Transform muzzleRef;
    Vector3 muzzleLocal;
    Collider[] ownColliders;
    Collider[] bulletIgnore;
    readonly RaycastHit[] hits = new RaycastHit[16];

    public State Current => state;
    public bool IsGrabbed => state == State.Grabbed;
    // statystyki (jak w AutoTurret)
    public int ShotsFired { get; private set; }
    public int Hits { get; private set; }
    public int Kills { get; private set; }
    public float Accuracy => ShotsFired > 0 ? Hits / (float)ShotsFired : 0f;
    /// <summary>Bieżące wychylenie celowania (stopnie): x = poziom (+ w prawo), y = pion (+ w górę).</summary>
    public Vector2 AimAngles { get; private set; }

    void Awake()
    {
        homeParent = transform.parent;
        homeLocalPos = transform.localPosition;
        homeLocalRot = transform.localRotation;
        ownColliders = GetComponentsInChildren<Collider>(true);
        bulletIgnore = ownColliders;

        if (muzzle != null)
        {
            muzzleRef = muzzle;
            muzzleLocal = Vector3.zero;
        }
        else
        {
            // Bez wskazanego wylotu: przednia ścianka lufy (sześcian o boku 1 w układzie 'Nozzle').
            Transform nozzle = transform.Find("Nozzle");
            muzzleRef = nozzle != null ? nozzle : transform;
            muzzleLocal = nozzle != null ? new Vector3(0f, 0f, 0.5f) : new Vector3(0f, 0f, 0.2f);
        }

        Apply();
    }

    void Start()
    {
        var bike = FindFirstObjectByType<BikeController>();
        if (bike != null)
        {
            if (holder == null) holder = FindDeep(bike.transform, "GunHoldPoint");
            if (holder == null) holder = FindDeep(bike.transform, "GunHolder");
            if (bikeFrame == null) bikeFrame = bike.transform.Find("Visual");
            if (bikeFrame == null) bikeFrame = bike.transform;
            if (bikeCenter == null)
            {
                var steering = bike.GetComponentInChildren<HandlebarSteering>();
                bikeCenter = steering != null ? steering.transform : bike.transform;
            }

            // Pociski nie zatrzymują się na własnym rowerze.
            var ignore = new List<Collider>(ownColliders);
            ignore.AddRange(bike.GetComponentsInChildren<Collider>(true));
            bulletIgnore = ignore.ToArray();
        }

        Camera cam = viewCamera != null ? viewCamera : Camera.main;
        if (cam != null)
        {
            camTf = cam.transform;
            camRestLocal = camTf.localRotation;
        }

        if (holder != null)
        {
            // Broń wisi pod rowerem (jego Rigidbody), więc jej collidery stają się wyzwalaczami – inaczej weszłyby
            // do złożonego collidera roweru i zmieniały jego fizykę. Łapanie myszką nadal działa (raycast uwzględnia wyzwalacze),
            // a zapytania fizyki roweru i wieżyczki wyzwalacze pomijają.
            foreach (var c in ownColliders) c.isTrigger = true;

            homeParent = holder;
            homeLocalPos = Vector3.zero;
            homeLocalRot = Quaternion.identity;
            transform.SetParent(holder, false);
            transform.SetLocalPositionAndRotation(homeLocalPos, homeLocalRot);
        }
    }

    void LateUpdate()
    {
        var mouse = Mouse.current;
        Camera cam = viewCamera != null ? viewCamera : Camera.main;
        if (mouse == null || cam == null) return;

        Vector2 mousePos = mouse.position.ReadValue();

        // Najpierw celowanie i obrót kamery, potem promień myszy – żeby przeciąganie liczyło się już z nowym widokiem.
        bool holding = state == State.Grabbed && mouse.leftButton.isPressed;
        AimAngles = holding ? ComputeAim(cam, mousePos) : Vector2.zero;
        UpdateCamera(AimAngles);

        Ray ray = cam.ScreenPointToRay(mousePos);

        if (state != State.Grabbed)
        {
            bool under = IsUnderCursor(ray, out RaycastHit hit);
            SetState(under ? State.Hover : State.Idle);
            if (under && mouse.leftButton.wasPressedThisFrame)
                BeginGrab(hit);
        }
        else if (!mouse.leftButton.isPressed)
        {
            EndGrab();
        }
        else
        {
            Drag(ray, cam);
            // Strzał tylko przy trzymanej broni; wasPressedThisFrame = jeden strzał na jedno kliknięcie.
            if (mouse.rightButton.wasPressedThisFrame)
                Fire();
        }

        if (returning) UpdateReturn();
    }

    void OnDisable()
    {
        if (state == State.Grabbed) EndGrab();
        SetState(State.Idle);
        // Nie zostawiamy obróconej kamery, gdy broń zostanie wyłączona.
        if (camTf != null && camAim != Vector2.zero) camTf.localRotation = camRestLocal;
        camAim = Vector2.zero;
    }

    public void SetState(State newState)
    {
        if (state == newState) return;
        state = newState;
        Apply();
    }

    bool IsUnderCursor(Ray ray, out RaycastHit best)
    {
        best = default;
        int count = Physics.RaycastNonAlloc(ray, hits, reachDistance, ~0, QueryTriggerInteraction.Collide);
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].distance >= bestDist) continue;
            if (hits[i].collider.GetComponentInParent<Gun>() != this) continue;
            best = hits[i];
            bestDist = hits[i].distance;
            found = true;
        }
        return found;
    }

    void BeginGrab(RaycastHit hit)
    {
        returning = false;
        // Trzymana broń żyje w układzie ramy roweru: jedzie razem z nim, a kierownica już jej nie obraca.
        if (bikeFrame != null && transform.parent != bikeFrame) transform.SetParent(bikeFrame, true);
        grabOffset = transform.localPosition - ToParent(hit.point);
        SetState(State.Grabbed);
    }

    void EndGrab()
    {
        SetState(State.Idle);
        AimAngles = Vector2.zero;
        if (transform.parent != homeParent) transform.SetParent(homeParent, true);

        if (instantReturn || returnTime <= 0f)
        {
            returning = false;
            transform.SetLocalPositionAndRotation(homeLocalPos, homeLocalRot);
        }
        else
        {
            returning = true;
            returnT = 0f;
            returnFromPos = transform.localPosition;
            returnFromRot = transform.localRotation;
        }
    }

    /// <summary>
    /// Pozycja: promień myszy rzutowany na płaszczyznę prostopadłą do kierunku patrzenia, przechodzącą przez
    /// punkt chwytu; wynik zapisywany w układzie ramy roweru. Obrót: wychylenie proporcjonalne do odległości
    /// kursora od punktu odniesienia (<see cref="AimAngles"/>, liczone w <see cref="ComputeAim"/>), względem przodu
    /// ramy roweru. Wygładzanie odbywa się w układzie lokalnym, więc ruch roweru przenosi się na broń od razu, bez opóźnienia.
    /// </summary>
    void Drag(Ray ray, Camera cam)
    {
        float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
        Vector3 targetPos = transform.localPosition;

        Vector3 grabWorld = FromParent(transform.localPosition - grabOffset);
        var plane = new Plane(cam.transform.forward, grabWorld);
        if (plane.Raycast(ray, out float enter))
            targetPos = ToParent(ray.GetPoint(enter)) + grabOffset;

        // Dodatni obrót wokół X w Unity opuszcza lufę, więc kursor wyżej = ujemny kąt X.
        Quaternion targetRot = Quaternion.Euler(-AimAngles.y, AimAngles.x, 0f);

        transform.SetLocalPositionAndRotation(
            Vector3.Lerp(transform.localPosition, targetPos, k),
            Quaternion.Slerp(transform.localRotation, targetRot, k));
    }

    /// <summary>
    /// Wychylenie celowania (stopnie) z odległości kursora od punktu odniesienia: na środku 0, przy krawędzi ekranu
    /// <see cref="maxYaw"/> / <see cref="maxPitch"/>. Punkt "środka roweru" jest rzutowany przy kamerze w pozycji
    /// spoczynkowej – inaczej obrót kamery przesuwałby punkt odniesienia i nakręcał wychylenie.
    /// </summary>
    Vector2 ComputeAim(Camera cam, Vector2 mousePos)
    {
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        if (aimReference == AimReference.BikeCenter && bikeCenter != null)
        {
            bool restore = camTf == cam.transform;
            Quaternion current = cam.transform.localRotation;
            if (restore) cam.transform.localRotation = camRestLocal;
            Vector3 sp = cam.WorldToScreenPoint(bikeCenter.position);
            if (restore) cam.transform.localRotation = current;
            if (sp.z > 0f) center = sp;
        }
        float nx = Mathf.Clamp((mousePos.x - center.x) / Mathf.Max(1f, Screen.width * 0.5f), -1f, 1f);
        float ny = Mathf.Clamp((mousePos.y - center.y) / Mathf.Max(1f, Screen.height * 0.5f), -1f, 1f);
        return new Vector2(nx * maxYaw, ny * maxPitch);
    }

    /// <summary>
    /// Obraca kamerę w stronę celowania o część wychylenia broni (<see cref="cameraAimFactor"/>), a po puszczeniu
    /// broni płynnie wraca do obrotu spoczynkowego. Poziomo obraca wokół osi pionowej roweru, pionowo wokół osi
    /// "w prawo" kamery, więc obraz się nie przekrzywia. Zmieniany jest tylko lokalny obrót kamery – jej pozycję
    /// nadal ustawia rower. W spoczynku kamera nie jest w ogóle dotykana.
    /// </summary>
    void UpdateCamera(Vector2 aim)
    {
        if (camTf == null) return;
        Vector2 target = rotateCamera ? aim * cameraAimFactor : Vector2.zero;
        if (target == Vector2.zero && camAim == Vector2.zero) return;

        camAim = Vector2.Lerp(camAim, target, 1f - Mathf.Exp(-cameraSharpness * Time.deltaTime));
        if (target == Vector2.zero && camAim.sqrMagnitude < 1e-4f) camAim = Vector2.zero;

        Transform parent = camTf.parent;
        Quaternion parentRot = parent != null ? parent.rotation : Quaternion.identity;
        Vector3 up = bikeFrame != null ? bikeFrame.up : Vector3.up;
        Quaternion world = Quaternion.AngleAxis(camAim.x, up) * (parentRot * camRestLocal) * Quaternion.Euler(-camAim.y, 0f, 0f);
        camTf.localRotation = Quaternion.Inverse(parentRot) * world;
    }

    void UpdateReturn()
    {
        returnT += Time.deltaTime / returnTime;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(returnT));
        transform.SetLocalPositionAndRotation(
            Vector3.Lerp(returnFromPos, homeLocalPos, t),
            Quaternion.Slerp(returnFromRot, homeLocalRot, t));
        if (returnT >= 1f) returning = false;
    }

    void Fire()
    {
        // Kierunek z aktualnego obrotu lufy (po celowaniu w tej klatce).
        Vector3 pos = muzzleRef.TransformPoint(muzzleLocal);
        Vector3 dir = muzzleRef.forward;
        Quaternion rot = Quaternion.LookRotation(dir);

        Bullet bullet = bulletPrefab != null ? Instantiate(bulletPrefab, pos, rot) : Bullet.CreateDefault(pos, rot);
        bullet.Launch(dir * bulletSpeed, bulletDamage, bulletLifetime, bulletIgnore, this);
        PlayShot();
        if (showShotLine) bullet.ShowShotLine(ShotLineMaterial(), shotLineWidth, shotLineColor, shotLineFade);
        ShotsFired++;
    }

    AudioSource audioSource;
    static AudioClip generatedShotClip;

    /// <summary>Odtwarza dźwięk wystrzału (2D, jak dźwięki wieżyczki – broń jest tuż przy kamerze).</summary>
    void PlayShot()
    {
        AudioClip clip = shotSound;
        if (clip == null)
        {
            // Mocniejszy i dłuższy od serii wieżyczki: pojedynczy strzał z ręki.
            if (generatedShotClip == null) generatedShotClip = AutoTurret.MakeNoiseClip(0.16f, 11f, 0.8f);
            clip = generatedShotClip;
        }

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0f;
            audioSource.playOnAwake = false;
        }
        audioSource.pitch = Random.Range(1f - shotPitchJitter, 1f + shotPitchJitter);
        audioSource.PlayOneShot(clip, shotVolume);
    }

    static Material defaultShotLineMaterial;

    /// <summary>Materiał linii strzału; bez przypisanego – wspólny materiał Sprites/Default (jak smugi wieżyczki).</summary>
    Material ShotLineMaterial()
    {
        if (shotLineMaterial != null) return shotLineMaterial;
        if (defaultShotLineMaterial == null)
            defaultShotLineMaterial = new Material(Shader.Find("Sprites/Default")) { name = "ShotLine (runtime)" };
        return defaultShotLineMaterial;
    }

    /// <summary>Wywoływane przez pocisk z tej broni po trafieniu gangstera.</summary>
    public void RegisterHit(bool killed)
    {
        Hits++;
        if (killed) Kills++;
    }

    /// <summary>Zeruje statystyki strzałów (restart misji).</summary>
    public void ResetStats()
    {
        ShotsFired = Hits = Kills = 0;
    }

    Vector3 ToParent(Vector3 world) => transform.parent != null ? transform.parent.InverseTransformPoint(world) : world;
    Vector3 FromParent(Vector3 local) => transform.parent != null ? transform.parent.TransformPoint(local) : local;

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    void Apply()
    {
        if (targetRenderer == null) return;
        block ??= new MaterialPropertyBlock();

        Color c = state switch
        {
            State.Hover => hoverColor,
            State.Grabbed => grabbedColor,
            _ => idleColor
        };

        targetRenderer.GetPropertyBlock(block);
        block.SetColor(BaseColorId, c);
        block.SetColor(ColorId, c);
        targetRenderer.SetPropertyBlock(block);
    }
}
