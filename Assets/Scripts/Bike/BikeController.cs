using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Prosta dynamika roweru: prędkość pochodzi z pedałowania (PedalDrive) minus opory,
/// a skręt z kąta kierownicy (HandlebarSteering) wg modelu rowerowego (yaw = v / L * tan(delta)).
/// Przechył (lean) jest tylko wizualny i wynika z przyspieszenia dośrodkowego.
/// Rower może się przewrócić (uderzenie w przeszkodę, poślizg w zakręcie / przy hamowaniu w zakręcie,
/// przewrócenie przez kierownicę przy mocnym hamowaniu przednim z dużej prędkości); wstaje się Spacją.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BikeController : MonoBehaviour
{
    public enum FallKind { None, Collision, Slip, Endo }

    [Header("Referencje")]
    [SerializeField] PedalDrive pedals;
    [SerializeField] HandlebarSteering steering;
    [SerializeField] Transform leanPivot;
    [SerializeField] Transform[] spinningWheels;

    [Header("Geometria")]
    [SerializeField] float wheelbase = 1.12f;
    [SerializeField] float wheelRadius = 0.34f;

    [Header("Opory i hamulec")]
    [SerializeField] float rollingResistance = 0.08f;   // m/s^2
    [SerializeField] float airDrag = 0.0035f;           // m/s^2 na (m/s)^2
    [SerializeField] float maxSpeed = 25f;

    [Header("Hamulce (uchwyt trzymany myszką + Spacja)")]
    [Tooltip("Lewy uchwyt + Spacja (jak w Polsce i reszcie kontynentalnej Europy / USA).")]
    [SerializeField] float frontBrakeDeceleration = 6.5f;  // m/s^2
    [Tooltip("Prawy uchwyt + Spacja.")]
    [SerializeField] float rearBrakeDeceleration = 3.5f;   // m/s^2

    [Header("Cofanie (S)")]
    [Tooltip("Cofanie włącza się tylko na postoju / przy prawie zerowej prędkości (np. po wjechaniu w przeszkodę).")]
    [SerializeField] float reverseEngageSpeed = 0.3f;      // m/s
    [SerializeField] float reverseAcceleration = 1.2f;     // m/s^2
    [SerializeField] float maxReverseSpeed = 1.6f;         // m/s (~5,8 km/h)
    [Tooltip("Wytracanie ruchu wstecz po puszczeniu S.")]
    [SerializeField] float reverseDrag = 2.5f;             // m/s^2

    [Header("Teren")]
    [Tooltip("Jak mocno nachylenie terenu hamuje (pod górę) i napędza (z góry) rower: 1 = prawdziwa grawitacja.")]
    [SerializeField, Range(0f, 1.5f)] float slopeInfluence = 0.5f;
    [Tooltip("Ile (m/s^2) stoku potrafi utrzymać stojący rowerzysta nogami; bardziej stromy stok zaczyna zsuwać rower w tył. Hamulec dodaje swoje.")]
    [SerializeField] float standstillHold = 1.2f;
    [Tooltip("Jak daleko przed rowerem (m) sprawdzamy teren, by wjechać na próg zgodnie z jego nachyleniem.")]
    [SerializeField] float lookAhead = 1.1f;
    [Tooltip("Jak daleko za rowerem (m) – razem z lookAhead wyznacza bazę, z której liczone jest nachylenie dla grawitacji.")]
    [SerializeField] float lookBehind = 0.9f;
    [Tooltip("Maksymalna prędkość zjazdu tyłem ze stoku (m/s).")]
    [SerializeField] float maxRollBackSpeed = 8f;

    [Header("Skręt")]
    [Tooltip("Jaka część obrotu kierownicy przechodzi na koło (reszta to 'luz' geometrii).")]
    [SerializeField] float steerRatio = 0.5f;
    [Tooltip("Im większe, tym słabszy skręt przy dużej prędkości.")]
    [SerializeField] float speedSteerDamping = 0.06f;
    [Tooltip("Limit przyspieszenia bocznego (m/s^2) – powyżej rower 'nie dowiezie' zakrętu.")]
    [SerializeField] float maxLateralAccel = 7f;

    [Header("Przechył")]
    [SerializeField] float maxLean = 35f;
    [SerializeField] float leanSharpness = 6f;

    [Header("Upadki – uderzenie")]
    [Tooltip("Ile prędkości (m/s) rower traci w jednym kroku fizyki przy zderzeniu, żeby gracz się przewrócił (3,5 m/s ≈ 12,6 km/h).")]
    [SerializeField] float crashSpeedLoss = 3.5f;

    [Header("Upadki – poślizg")]
    [Tooltip("Łączne obciążenie przyczepności (m/s^2): zakręt i hamowanie dzielą jedną przyczepność. Powyżej – poślizg.")]
    [SerializeField] float slipLimit = 9f;
    [Tooltip("Jak długo (s) trzeba być ponad limitem, żeby się przewrócić (szybciej przy większym przeciążeniu).")]
    [SerializeField] float slipTime = 0.45f;
    [SerializeField] float slipRecoverTime = 0.3f;
    [Tooltip("Poniżej tej prędkości (m/s) nie ma poślizgu.")]
    [SerializeField] float minSlipSpeed = 2f;

    [Header("Upadki – przewrócenie przez kierownicę")]
    [Tooltip("Mocne hamowanie przednim hamulcem powyżej tej prędkości (m/s) zaczyna zagrażać przewróceniem.")]
    [SerializeField] float endoMinSpeed = 4.5f;
    [Tooltip("Przy endoMinSpeed tyle sekund hamowania daje przewrócenie; przy większej prędkości szybciej (~v²).")]
    [SerializeField] float endoTime = 1f;

    [Header("Upadki – skutki i wstawanie")]
    [Tooltip("Hamowanie o podłoże (m/s^2), gdy rower leży.")]
    [SerializeField] float slideDeceleration = 4f;
    [Tooltip("Ile staminy (0..1) kosztuje upadek.")]
    [SerializeField] float fallStaminaLoss = 0.15f;
    [Tooltip("Tyle sekund trzeba poleżeć, zanim da się zacząć wstawać.")]
    [SerializeField] float minDownTime = 0.8f;
    [Tooltip("Ile sekund trzymać Spację, żeby wstać.")]
    [SerializeField] float standUpTime = 1.2f;
    [SerializeField] float fallRoll = 75f;    // docelowy przechył leżącego roweru (stopnie)
    [Tooltip("Czas (s) przewracania na bok przy poślizgu. Ruch przyspiesza jak swobodny spadek, od aktualnego przechyłu.")]
    [SerializeField] float fallDuration = 0.55f;
    [Header("Upadki – stanie na przednim kole (uderzenie / przewrócenie przez kierownicę)")]
    [Tooltip("Pochylenie roweru do przodu (stopnie) przy najsilniejszym uderzeniu; rower obraca się wokół przedniej opony.")]
    [SerializeField] float frontStandPitch = 42f;
    [Tooltip("Pochylenie przy najsłabszym uderzeniu, które jeszcze przewraca.")]
    [SerializeField] float frontStandMinPitch = 22f;
    [Tooltip("Czas (s) unoszenia tyłu roweru (zwalnia pod koniec jak rzut w górę).")]
    [SerializeField] float frontStandTime = 0.3f;
    [Tooltip("Czas (s) przewracania się na bok po staniu na przednim kole (przyspiesza jak spadek).")]
    [SerializeField] float toppleTime = 0.55f;
    [Tooltip("Pozycja przedniej opony przy podłożu (z), wokół niej obraca się rower.")]
    [SerializeField] float frontContactZ = 0.57f;
    [Tooltip("Prędkość uderzenia (m/s), przy której pochylenie jest maksymalne.")]
    [SerializeField] float fullSeverityImpactSpeed = 10f;
    [Tooltip("Promień (m) kamery przy sprawdzaniu kolizji z przeszkodami podczas upadku.")]
    [SerializeField] float cameraRadius = 0.15f;

    Rigidbody rb;
    float speed;
    float yaw;
    float yawRate;              // rad/s, + = w prawo
    float lean;                 // stopnie, + = w prawo
    Vector3 lastForward;
    Vector3 groundTangent = Vector3.forward;
    Vector3 groundNormal = Vector3.up;
    float slopeAccel;
    float slopePitch;
    bool grounded;
    Vector3 startPosition;
    float startYaw;
    bool reverseHeld;

    // upadki
    bool fallen;
    FallKind fallKind;
    float fallSide = 1f;        // + = w prawo
    float fallAmount;           // 0..1 – ogólny stopień wizualnego przewrócenia (dla kamery)
    float pitchAmt;             // 0..1 – udział maksymalnego pochylenia do przodu
    float rollAmt;              // 0..1 – udział przewrócenia na bok
    float fallClock;            // s od początku upadku
    float fallSeverity;         // 0..1 – siła uderzenia (skaluje pochylenie)
    float standSmooth;          // wygładzony postęp wstawania
    float downTimer;
    float standProgress;
    float slip;
    float endo;
    float gripLoad;
    string fallText = "";

    // kamera (dziecko leanPivot) – podczas upadku nie może wchodzić w przeszkody
    Transform cameraTf;
    Vector3 cameraBaseLocalPos;
    Vector3 cameraRestLocalToRoot;
    readonly RaycastHit[] camHits = new RaycastHit[8];

    /// <summary>Prędkość do przodu (m/s) – przy cofaniu 0.</summary>
    public float Speed => Mathf.Max(0f, speed);
    /// <summary>Prędkość ze znakiem (km/h) – ujemna przy cofaniu.</summary>
    public float SpeedKmh => speed * 3.6f;
    public bool Reversing => speed < -0.05f;
    public float Lean => lean;
    public bool FrontBrake { get; private set; }
    public bool RearBrake { get; private set; }

    public bool Fallen => fallen;
    public FallKind LastFallKind => fallKind;
    /// <summary>Opis ostatniego upadku (do HUD).</summary>
    public string FallText => fallText;
    /// <summary>Postęp wstawania 0..1.</summary>
    public float StandProgress => standProgress;
    /// <summary>Można już zacząć wstawać (poleżało się dość długo i rower prawie stoi).</summary>
    public bool CanStandUp => fallen && downTimer >= minDownTime && Mathf.Abs(speed) < 0.5f;
    /// <summary>Obciążenie przyczepności: 1 = granica poślizgu.</summary>
    public float GripLoad => gripLoad;
    /// <summary>Jak blisko upadku jesteśmy (0..1): max z poślizgu i przewrócenia przez przedni hamulec.</summary>
    public float FallRisk => Mathf.Clamp01(Mathf.Max(slip, endo));
    public string RiskLabel => endo > slip ? "PRZEWRÓCENIE" : "POŚLIZG";

    /// <summary>Zdarzenia roweru dla telemetrii ("fall Slip ...", "stand_up ...").</summary>
    public event System.Action<string> Logged;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        startPosition = transform.position;
        startYaw = transform.eulerAngles.y;
        yaw = startYaw;
        lastForward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        var cam = GetComponentInChildren<Camera>();
        if (cam != null)
        {
            cameraTf = cam.transform;
            cameraBaseLocalPos = cameraTf.localPosition;
            cameraRestLocalToRoot = transform.InverseTransformPoint(cameraTf.position);
        }
    }

    /// <summary>Wyznacza nachylenie podłoża pod rowerem: kierunek jazdy wzdłuż stoku i przyspieszenie od grawitacji.</summary>
    void SampleGround()
    {
        Vector3 flat = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        slopeAccel = 0f;
        groundTangent = flat;
        if (Physics.Raycast(rb.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 1.2f, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 t = Vector3.ProjectOnPlane(flat, hit.normal);
            if (t.sqrMagnitude > 1e-4f)
            {
                t.Normalize();
                Vector3 accelDir = t;

                // Teren z siatki 2 m ma ostre „fasetki” (krótka górka = nagły próg 15–20°). Dwa dodatkowe promienie:
                //  - przed rowerem: jeśli tam teren wznosi się szybciej niż pod środkiem, kierujemy prędkość już
                //    wzdłuż tego wzniesienia, żeby przednie koło nie uderzało w próg i nie traciło prędkości,
                //  - z tyłu: nachylenie dla grawitacji liczymy ze „średniej” między tyłem a przodem (ok. 2 m),
                //    więc krótki próg nie hamuje roweru jak ściana.
                bool hasFront = Physics.Raycast(rb.position + flat * lookAhead + Vector3.up * 0.5f, Vector3.down,
                    out RaycastHit front, 1.2f + lookAhead * 0.7f, ~0, QueryTriggerInteraction.Ignore);
                bool hasRear = Physics.Raycast(rb.position - flat * lookBehind + Vector3.up * 0.5f, Vector3.down,
                    out RaycastHit rear, 1.2f + lookBehind * 0.7f, ~0, QueryTriggerInteraction.Ignore);
                if (hasFront)
                {
                    Vector3 ahead = (front.point - hit.point);
                    if (ahead.sqrMagnitude > 0.04f)
                    {
                        ahead.Normalize();
                        if (ahead.y > t.y) t = ahead;
                    }
                    if (hasRear)
                    {
                        Vector3 span = front.point - rear.point;
                        if (span.sqrMagnitude > 0.25f) accelDir = span.normalized;
                    }
                }
                groundTangent = t;
                slopeAccel = Vector3.Dot(Physics.gravity, accelDir) * slopeInfluence;   // < 0 pod górę
            }
            groundNormal = hit.normal;
            grounded = true;
        }
        else grounded = false;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        SampleGround();

        // Kolizje: jeśli fizyka wyhamowała ruch wzdłuż kierunku jazdy, przejmujemy tę prędkość.
        float actual = Vector3.Dot(rb.linearVelocity, lastForward);
        float before = speed;
        if (speed >= 0f) speed = Mathf.Min(speed, Mathf.Max(0f, actual) + 0.01f);
        else speed = Mathf.Max(speed, Mathf.Min(0f, actual) - 0.01f);

        // Uderzenie z dużą utratą prędkości = upadek.
        if (!fallen && before - speed > crashSpeedLoss)
            Fall(FallKind.Collision, 1f, $"uderzenie, utrata {(before - speed) * 3.6f:0} km/h", before);

        if (fallen)
        {
            // Leżący rower ślizga się po podłożu aż do zatrzymania; bez napędu i skrętu.
            speed = Mathf.MoveTowards(speed, 0f, slideDeceleration * dt);
            yawRate = 0f;
            gripLoad = 0f;
            Move(dt);
            return;
        }

        float drive = pedals != null ? pedals.GetDriveAcceleration(Mathf.Max(0f, speed)) : 0f;
        float brake = (FrontBrake ? frontBrakeDeceleration : 0f) + (RearBrake ? rearBrakeDeceleration : 0f);

        if (reverseHeld && speed <= reverseEngageSpeed && drive <= 0.01f)
        {
            // Cofanie (S): tylko z postoju – pedałowanie i przejazd do przodu mają pierwszeństwo.
            speed = Mathf.Max(speed - reverseAcceleration * dt, -maxReverseSpeed);
        }
        else if (speed < 0f)
        {
            // Toczenie się do tyłu (po puszczeniu S albo zjazd tyłem ze stoku): tarcie / hamulec / pedałowanie
            // wyhamowują ruch, a stok pod górę nadal ciągnie rower w tył.
            float pull = Mathf.Max(0f, -(drive + slopeAccel));
            float resist = brake + Mathf.Max(rollingResistance, reverseDrag - pull);
            speed = Mathf.Min(0f, speed + (drive + slopeAccel + resist) * dt);
            speed = Mathf.Max(speed, -maxRollBackSpeed);
        }
        else
        {
            float decel = rollingResistance + airDrag * speed * speed + brake;
            float next = speed + (drive - decel + slopeAccel) * dt;

            // Pod górę: gdy napęd nie wystarcza, a stok ciągnie mocniej niż trzyma rowerzysta (nogi, hamulec),
            // rower zatrzymuje się i zaczyna zjeżdżać tyłem.
            float pull = Mathf.Max(0f, -(drive + slopeAccel));
            if (next < 0f && pull > rollingResistance + brake + standstillHold)
                speed = Mathf.Max(next, -maxRollBackSpeed);
            else
                speed = Mathf.Clamp(next, 0f, maxSpeed);
        }

        // Model rowerowy: yaw rate = v / L * tan(delta). Przy cofaniu v < 0 odwraca kierunek skrętu (jak w prawdziwym rowerze).
        float absSpeed = Mathf.Abs(speed);
        float handlebar = steering != null ? steering.Angle : 0f;
        float delta = handlebar * steerRatio / (1f + absSpeed * speedSteerDamping);
        float rawRate = speed / wheelbase * Mathf.Tan(delta * Mathf.Deg2Rad);
        float rate = rawRate;

        if (absSpeed > 0.1f)
        {
            float limit = maxLateralAccel / absSpeed;
            rate = Mathf.Clamp(rate, -limit, limit);
        }
        yawRate = rate;

        UpdateFallRisk(dt, rawRate, brake);
        if (fallen) { Move(dt); return; }

        yaw += yawRate * Mathf.Rad2Deg * dt;
        Move(dt);
    }

    /// <summary>Poślizg (zakręt + hamowanie dzielą przyczepność) i przewrócenie przez przedni hamulec.</summary>
    void UpdateFallRisk(float dt, float rawRate, float brake)
    {
        // Poślizg: zapotrzebowanie na przyspieszenie boczne (bez ucinania) + hamowanie, jako okrąg tarcia.
        if (speed > minSlipSpeed)
        {
            float lateral = speed * Mathf.Abs(rawRate);
            gripLoad = Mathf.Sqrt(lateral * lateral + brake * brake) / slipLimit;
        }
        else gripLoad = 0f;

        if (gripLoad > 1f) slip += dt / slipTime * Mathf.Min(gripLoad, 2f);
        else slip = Mathf.Max(0f, slip - dt / slipRecoverTime);

        // Przewrócenie przez kierownicę: mocne hamowanie przednie z dużej prędkości.
        if (FrontBrake && speed > endoMinSpeed)
        {
            float k = speed / endoMinSpeed;
            endo += dt * k * k / endoTime;
        }
        else endo = Mathf.Max(0f, endo - dt * 1.5f);

        if (endo >= 1f)
            Fall(FallKind.Endo, 1f, "przewrócenie przez kierownicę (przedni hamulec)", speed);
        else if (slip >= 1f)
            Fall(FallKind.Slip, rawRate, gripLoad > 1f && brake > 0f ? "poślizg (zakręt + hamowanie)" : "poślizg w zakręcie", speed);
    }

    void Move(float dt)
    {
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 forward = rotation * Vector3.forward;

        rb.MoveRotation(rotation);
        rb.angularVelocity = Vector3.zero;

        // Na ziemi jedziemy wzdłuż stoku (prędkość ma składową pionową), w powietrzu zostaje fizyka.
        // Grawitację fizyki wyłączamy na ziemi: wpływ nachylenia liczymy sami (slopeAccel), a bez tego
        // grawitacja byłaby policzona dwa razy (pod górę rower tracił prędkość, a stojący zsuwał się po stoku).
        rb.useGravity = !grounded;
        Vector3 dir = grounded ? groundTangent : forward;
        Vector3 v = dir * speed;
        if (grounded) v -= groundNormal * 0.3f;     // lekki docisk do podłoża, żeby nie odbijać się na nierównościach
        else v.y = rb.linearVelocity.y;
        rb.linearVelocity = v;
        lastForward = dir;
    }

    void Fall(FallKind kind, float side, string description, float atSpeed)
    {
        if (kind != FallKind.Slip)
        {
            // Rower staje na przednim kole i przewraca się na stronę, w którą była skręcona kierownica / przechylony,
            // a przy jeździe na wprost – na losową.
            float steer = steering != null ? steering.Angle : 0f;
            if (Mathf.Abs(steer) > 3f) side = steer;
            else if (Mathf.Abs(lean) > 3f) side = lean;
            else side = Random.value < 0.5f ? -1f : 1f;
        }

        fallen = true;
        fallKind = kind;
        fallSide = side < 0f ? -1f : 1f;
        fallSeverity = Mathf.Clamp01((atSpeed - crashSpeedLoss) / Mathf.Max(0.1f, fullSeverityImpactSpeed - crashSpeedLoss));
        downTimer = 0f;
        standProgress = 0f;
        standSmooth = 0f;
        fallClock = 0f;
        slip = 0f;
        endo = 0f;
        gripLoad = 0f;
        FrontBrake = RearBrake = false;
        reverseHeld = false;
        fallText = description;

        if (pedals != null)
        {
            pedals.Interrupt(fallStaminaLoss);
            pedals.enabled = false;
        }
        if (steering != null)
        {
            steering.ResetSteering();
            steering.enabled = false;
        }

        Logged?.Invoke($"fall {kind} v={atSpeed * 3.6f:0.0}km/h {description}");
    }

    void StandUp()
    {
        fallen = false;
        standProgress = 0f;
        // lean zostaje: wygasa płynnie razem z wizualnym podnoszeniem się
        if (pedals != null) pedals.enabled = true;
        if (steering != null) steering.enabled = true;
        Logged?.Invoke($"stand_up lay={downTimer:0.0}s");
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (steering != null) steering.Speed = Mathf.Abs(speed);
        if (pedals != null) pedals.Speed = Mathf.Max(0f, speed);

        var kb = Keyboard.current;
        reverseHeld = !fallen && kb != null && kb.sKey.isPressed;

        if (fallen)
        {
            // Wstawanie: przytrzymaj Spację (po minimalnym czasie leżenia i gdy rower prawie stoi).
            downTimer += dt;
            bool hold = kb != null && kb.spaceKey.isPressed && CanStandUp;
            if (hold) standProgress += dt / standUpTime;
            else standProgress = Mathf.Max(0f, standProgress - dt / standUpTime * 1.5f);
            if (standProgress >= 1f) StandUp();
        }
        else
        {
            bool leftHeld = steering != null && steering.LeftHeld;
            bool rightHeld = steering != null && steering.RightHeld;

            // Hamulec i manetka działają tylko na uchwycie, który aktualnie trzyma "ręka" (LPM).
            // Lewy: hamulec przedni + przerzutka przednia (1–3). Prawy: hamulec tylny + przerzutka tylna (1–7).
            bool space = kb != null && kb.spaceKey.isPressed;
            FrontBrake = leftHeld && space;
            RearBrake = rightHeld && space;

            if (kb != null && pedals != null && (leftHeld || rightHeld))
            {
                int dir = 0;
                if (kb.leftShiftKey.wasPressedThisFrame) dir += 1;   // bieg w górę (cięższy)
                if (kb.leftCtrlKey.wasPressedThisFrame) dir -= 1;    // bieg w dół (lżejszy)
                if (dir != 0)
                {
                    if (leftHeld) pedals.ShiftFront(dir);
                    else pedals.ShiftRear(dir);
                }
            }
        }

        if (kb != null && kb.rKey.wasPressedThisFrame) ResetBike();
        if (transform.position.y < -10f) ResetBike();

        // Obrót kół.
        float spin = speed / wheelRadius * Mathf.Rad2Deg * dt;
        for (int i = 0; i < spinningWheels.Length; i++)
            if (spinningWheels[i] != null) spinningWheels[i].Rotate(spin, 0f, 0f, Space.Self);

        // Przechył wynikający z przyspieszenia dośrodkowego: tan(lean) = v * omega / g
        // Podczas leżenia przechył jest "zamrożony" – przewracanie startuje płynnie od aktualnego przechyłu.
        if (!fallen)
        {
            float targetLean = Mathf.Atan(speed * yawRate / 9.81f) * Mathf.Rad2Deg;
            targetLean = Mathf.Clamp(targetLean, -maxLean, maxLean);
            lean = Mathf.Lerp(lean, targetLean, 1f - Mathf.Exp(-leanSharpness * dt));
        }

        // Wizualne przewrócenie.
        //  • Poślizg: przechył płynnie dochodzi do leżenia na boku (przyspiesza jak swobodny spadek).
        //  • Uderzenie / przewrócenie przez kierownicę: rower staje na przedniej oponie (tył unosi się, ruch zwalnia
        //    przy szczycie), po czym przewraca się na bok (przyspiesza), a pochylenie do przodu częściowo wraca.
        // Wstawanie unosi widok do połowy drogi, a po wstaniu wszystko płynnie wraca do pionu.
        if (fallen)
        {
            fallClock += dt;
            standSmooth = Mathf.Lerp(standSmooth, standProgress, 1f - Mathf.Exp(-10f * dt));
            float stand = 1f - 0.45f * standSmooth;

            if (fallKind == FallKind.Slip)
            {
                float t = Mathf.Clamp01(fallClock / Mathf.Max(0.05f, fallDuration));
                pitchAmt = 0f;
                rollAmt = t * t * stand;
            }
            else
            {
                float up = Mathf.Clamp01(fallClock / Mathf.Max(0.05f, frontStandTime));
                up = 1f - (1f - up) * (1f - up);
                float topple = Mathf.Clamp01((fallClock - frontStandTime * 0.9f) / Mathf.Max(0.05f, toppleTime));
                float rolled = topple * topple;
                pitchAmt = up * (1f - 0.7f * rolled) * stand;
                rollAmt = rolled * stand;
            }
        }
        else
        {
            float k = 1f - Mathf.Exp(-5f * dt);
            standSmooth = Mathf.Lerp(standSmooth, 0f, k);
            pitchAmt = Mathf.Lerp(pitchAmt, 0f, k);
            rollAmt = Mathf.Lerp(rollAmt, 0f, k);
            if (pitchAmt < 0.001f) pitchAmt = 0f;
            if (rollAmt < 0.001f) rollAmt = 0f;
        }
        fallAmount = Mathf.Max(pitchAmt, rollAmt);

        float maxPitch = Mathf.Lerp(frontStandMinPitch, frontStandPitch, fallSeverity);
        // Pochylenie roweru na stoku (łagodzone, żeby kamera nie "pływała"): pod górę patrzymy w górę.
        float slopeTarget = grounded && !fallen ? -Mathf.Asin(Mathf.Clamp(groundTangent.y, -1f, 1f)) * Mathf.Rad2Deg * 0.6f : 0f;
        slopePitch = Mathf.Lerp(slopePitch, slopeTarget, 1f - Mathf.Exp(-4f * dt));
        float pitch = (fallKind == FallKind.Slip ? 0f : maxPitch * pitchAmt) + slopePitch;
        float tilt = Mathf.Lerp(lean, fallSide * fallRoll, rollAmt);

        // Dodatni lean = w prawo = ujemny obrót wokół osi Z (układ lewoskrętny Unity).
        // Kamera jest dzieckiem leanPivot, więc przechyla się razem z rowerem
        // i kierownica stoi w tym samym miejscu na ekranie.
        if (leanPivot != null)
        {
            Quaternion q = Quaternion.Euler(pitch, 0f, -tilt);
            leanPivot.localRotation = q;

            // Przy staniu na przednim kole obrót odbywa się wokół przedniej opony (nie środka roweru).
            if (fallKind != FallKind.Slip && fallKind != FallKind.None && fallAmount > 0f)
            {
                var pivotPoint = new Vector3(0f, 0f, frontContactZ);
                leanPivot.localPosition = pivotPoint - q * pivotPoint;
            }
            else leanPivot.localPosition = Vector3.zero;
        }
    }

    void LateUpdate()
    {
        if (cameraTf == null) return;

        // Kamera wraca na swoje miejsce, a podczas upadku jest cofana przed przeszkodą
        // (bez tego głowa przenika przez ścianę, gdy rower przechyla się do przodu).
        cameraTf.localPosition = cameraBaseLocalPos;
        if (fallAmount < 0.01f) return;

        Vector3 origin = transform.TransformPoint(cameraRestLocalToRoot);
        Vector3 desired = cameraTf.position;
        Vector3 toCam = desired - origin;
        float dist = toCam.magnitude;
        if (dist < 0.01f) return;

        Vector3 dir = toCam / dist;
        int n = Physics.SphereCastNonAlloc(origin, cameraRadius, dir, camHits, dist, ~0, QueryTriggerInteraction.Ignore);
        float nearest = dist;
        for (int i = 0; i < n; i++)
        {
            var h = camHits[i];
            if (h.collider.attachedRigidbody == rb || h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance < nearest) nearest = h.distance;
        }
        if (nearest < dist) cameraTf.position = origin + dir * nearest;
    }

    public void ResetBike()
    {
        speed = 0f;
        reverseHeld = false;
        yaw = startYaw;
        yawRate = 0f;
        lean = 0f;
        fallen = false;
        fallAmount = pitchAmt = rollAmt = 0f;
        fallClock = 0f;
        standSmooth = 0f;
        standProgress = 0f;
        slip = endo = gripLoad = 0f;
        FrontBrake = RearBrake = false;
        lastForward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        rb.position = startPosition;
        rb.rotation = Quaternion.Euler(0f, yaw, 0f);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        if (pedals != null) { pedals.enabled = true; pedals.ResetState(); }
        if (steering != null) { steering.enabled = true; steering.ResetSteering(); }
    }
}
