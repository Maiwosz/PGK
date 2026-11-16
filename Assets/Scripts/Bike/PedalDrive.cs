using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Pedałowanie w takt (rytmicznie): A = lewa noga, D = prawa noga, naprzemiennie, jedno kliknięcie = jeden skok pedału
/// (pół obrotu korby). Tempo wyznacza rower: skok trwa tyle, ile kręci się korba przy aktualnej prędkości i biegu
/// (kadencja = prędkość / (przełożenie * promień koła)), więc na ciężkim biegu klika się rzadko, a na lekkim często.
/// Kolejne kliknięcie ma wypaść w chwili, gdy skok się kończy:
///  - idealne trafienie (wąskie okno) daje wyraźnie mocniejszy skok,
///  - trafienie w oknie łaski daje normalny skok,
///  - za wcześnie albo za późno poza oknem łaski – skok jest tym słabszy i kosztuje siły, im większy błąd,
///  - klik dużo za wcześnie (klepanie w klawisze) i zła noga są odrzucane i kosztują siły.
/// Za późne kliknięcie to po prostu przerwa w napędzie; po dłuższej przerwie rytm się zrywa i kolejny klik zaczyna nowy.
/// Siła napędu = moment na korbie / (przełożenie * promień koła); moment maleje wraz z kadencją.
/// </summary>
public class PedalDrive : MonoBehaviour
{
    public enum Foot { None, Left, Right }

    /// <summary>Ocena kliknięcia. Start = pierwszy klik (rower stoi albo rytm się zerwał), Early/Late = trafienie poza oknem łaski.</summary>
    public enum Judgement { None, Start, Perfect, Good, Early, Late, Mash, WrongFoot }

    [SerializeField] Key leftKey = Key.A;
    [SerializeField] Key rightKey = Key.D;

    [Header("Przerzutki 3 x 7")]
    [Tooltip("Liczba zębów tarcz z przodu: bieg 1 = najmniejsza (najlżejsza).")]
    [SerializeField] int[] chainrings = { 28, 38, 48 };
    [Tooltip("Liczba zębów zębatek kasety z tyłu: bieg 1 = największa (najlżejsza).")]
    [SerializeField] int[] cogs = { 32, 28, 24, 21, 18, 16, 14 };
    [Tooltip("Startowy bieg przedni i tylny (numeracja od 1).")]
    [SerializeField] int startFrontGear = 2;
    [SerializeField] int startRearGear = 3;

    [Header("Rowerzysta")]
    [SerializeField] float wheelRadius = 0.34f;
    [SerializeField] float totalMass = 85f;
    [Tooltip("Maksymalny moment na korbie (Nm) przy kadencji bliskiej zeru.")]
    [SerializeField] float peakTorque = 60f;
    [Tooltip("Prędkość kątowa korby (rad/s), przy której moment spada do zera.")]
    [SerializeField] float maxCrankOmega = 18f;
    [SerializeField] float drivetrainEfficiency = 0.97f;
    [Tooltip("Dolna granica prędkości używana do obliczeń przy ruszaniu (m/s).")]
    [SerializeField] float minEffectiveSpeed = 1.5f;
    [Tooltip("Mnożnik tempa rytmu: 1 = tempo wynikające z prędkości i biegu, mniej = rzadsze kliknięcia (np. 0.6 = o 40% wolniej). Nie zmienia siły napędu na skok, tylko jak długo trwa.")]
    [SerializeField, Range(0.2f, 1.5f)] float tempoScale = 0.6f;
    [SerializeField] float minStrokeTime = 0.07f;
    [SerializeField] float maxStrokeTime = 1.6f;

    [Header("Rytm – okna trafienia")]
    [Tooltip("Idealne trafienie: najmniej tyle sekund od chwili końca skoku (w obie strony)...")]
    [SerializeField] float perfectWindow = 0.045f;
    [Tooltip("...albo tyle długości skoku – liczy się większa wartość (przy szybkim tempie okno nie robi się niemożliwie wąskie).")]
    [SerializeField, Range(0f, 0.5f)] float perfectFraction = 0.10f;
    [Tooltip("Okno łaski (normalny skok, bez kary): najmniej tyle sekund w obie strony...")]
    [SerializeField] float graceWindow = 0.09f;
    [SerializeField, Range(0f, 0.5f)] float graceFraction = 0.22f;
    [Tooltip("Kliknięcie wcześniej niż ta część skoku przed jego końcem to klepanie w klawisze: odrzucane i kosztuje siły.")]
    [SerializeField, Range(0.2f, 1f)] float earlyRejectFraction = 0.5f;
    [Tooltip("Po tylu sekundach od końca skoku (co najmniej) rytm się zrywa i następny klik zaczyna nowy bez kary...")]
    [SerializeField] float lateLimit = 0.5f;
    [Tooltip("...albo po tej części długości skoku – liczy się większa wartość.")]
    [SerializeField] float lateLimitFraction = 0.9f;

    [Header("Rytm – skutki")]
    [Tooltip("Mnożnik mocy skoku przy idealnym trafieniu.")]
    [SerializeField] float perfectMultiplier = 1.5f;
    [Tooltip("Mnożnik mocy skoku w oknie łaski.")]
    [SerializeField] float goodMultiplier = 1.0f;
    [Tooltip("Mnożnik mocy przy największym błędzie, który jeszcze zalicza skok (rośnie liniowo od okna łaski).")]
    [SerializeField] float poorMultiplier = 0.4f;
    [Tooltip("Ubytek sił za największy błąd lub odrzucone kliknięcie (skalowany błędem).")]
    [SerializeField] float missStamina = 0.04f;
    [Tooltip("Zysk sił za idealne trafienie.")]
    [SerializeField] float perfectStamina = 0.006f;
    [Tooltip("Korekta opóźnienia (s): dodatnia – klik oceniany jako późniejszy; do kalibracji sprzętu.")]
    [SerializeField] float timingOffset = 0f;

    [Header("Płynność napędu")]
    [Tooltip("Wybieg napędu: po ukończonym skoku napęd nie urywa się, tylko zanika w tym czasie (s). 0 = wyłączony.")]
    [SerializeField] float tailTime = 0.10f;
    [Tooltip("Poziom wybiegu po ukończonym skoku (profil siły: średnia skoku = 1, szczyt ok. 1,57), mnożony przez jakość skoku.")]
    [SerializeField] float tailFloor = 0.5f;

    [Header("Wytrzymałość (stamina)")]
    [Tooltip("Wydajność (ułamek szczytowej mocy przy danej kadencji), od której nie męczysz się, a powyżej której odpoczywasz. " +
             "To ta sama zielona strefa, którą pokazuje pasek kadencji w HUD.")]
    [SerializeField, Range(0.5f, 0.99f)] float sweetPowerFraction = 0.85f;
    [Tooltip("Regeneracja na sekundę przy pedałowaniu w samym środku strefy (wydajność 100%).")]
    [SerializeField] float staminaRegen = 0.05f;
    [Tooltip("Regeneracja na sekundę, gdy nie pedałujesz (koasting, postój).")]
    [SerializeField] float staminaCoastRegen = 0.03f;
    [Tooltip("Ubytek na sekundę przy wydajności 0% (np. kręcenie na pusto albo ruszanie na najcięższym biegu).")]
    [SerializeField] float staminaDrain = 0.12f;
    [Tooltip("Im większy wykładnik, tym łagodniej karany jest bieg tylko trochę poza strefą.")]
    [SerializeField] float drainExponent = 1.5f;
    [Tooltip("Poniżej tego poziomu stamina zaczyna spowalniać pedałowanie.")]
    [SerializeField, Range(0.05f, 1f)] float penaltyStartStamina = 0.5f;
    [Tooltip("Mnożnik szybkości pedałowania (i siły) przy pustej staminie.")]
    [SerializeField, Range(0.1f, 1f)] float minPedalSpeedFactor = 0.35f;

    [Header("Wizualizacja korby")]
    [SerializeField] float crankFollowSpeed = 1500f;
    [SerializeField] Transform leftPedal;
    [SerializeField] Transform rightPedal;

    Foot last = Foot.None;         // noga ostatniego skoku
    bool inRhythm;                 // trwa rytm: kolejny klik jest oceniany względem końca skoku
    float crankBase;               // kąt korby (stopnie) na początku bieżącego skoku
    float progress = 1f;           // postęp bieżącego skoku 0..1 (1 = skończony)
    float timeSinceEnd;            // ile sekund minęło od końca skoku (idealnej chwili następnego kliknięcia)
    float quality = 1f;            // mnożnik mocy bieżącego skoku (ocena trafienia)
    float angle;
    float tailLevel;               // poziom wybiegu napędu (w jednostkach profilu siły), zanika wykładniczo
    float stamina = 1f;
    float lastEffortTime = float.NegativeInfinity;
    int frontIndex;
    int rearIndex;

    /// <summary>Aktualna prędkość roweru (m/s) – ustawia BikeController.</summary>
    public float Speed { get; set; }
    /// <summary>Noga naciska pedał (trwa skok) – napęd aktywny.</summary>
    public bool Pushing { get; private set; }

    /// <summary>Noga, która właśnie wykonuje skok (None, gdy skok się skończył).</summary>
    public Foot ActiveFoot => Pushing ? last : Foot.None;
    /// <summary>Postęp bieżącego skoku 0..1.</summary>
    public float Progress => progress;
    /// <summary>Noga ostatniego skoku.</summary>
    public Foot StrokeFoot => last;
    /// <summary>Czy trwa rytm (inaczej pierwszy klik zaczyna nowy bez oceny).</summary>
    public bool InRhythm => inRhythm;
    /// <summary>Noga, która ma kliknąć następna; None, gdy rytm się nie zaczął (można dowolną).</summary>
    public Foot ExpectedFoot => inRhythm ? Opposite(last) : Foot.None;
    /// <summary>"Naładowanie" do następnego kliknięcia 0..1: rośnie przez skok, po jego końcu zostaje 1.</summary>
    public float BeatCharge => inRhythm ? Mathf.Clamp01(progress) : 0f;
    /// <summary>Za ile sekund wypada idealne kliknięcie (ujemne = już minęło). 0, gdy rytm się nie zaczął.</summary>
    public float TimeToBeat => !inRhythm ? 0f : progress < 1f ? (1f - progress) * StrokeTime(Speed) : -timeSinceEnd;
    /// <summary>Połowa okna idealnego trafienia (s) przy obecnym tempie.</summary>
    public float PerfectWindow => Mathf.Max(perfectWindow, perfectFraction * StrokeTime(Speed));
    /// <summary>Ułamek skoku przed jego końcem, od którego kliknięcie to klepanie (odrzucane).</summary>
    public float EarlyRejectFraction => earlyRejectFraction;
    /// <summary>Połowa okna łaski (s) przy obecnym tempie.</summary>
    public float GraceWindow => Mathf.Max(graceWindow, graceFraction * StrokeTime(Speed));

    /// <summary>Ostatnia ocena kliknięcia, jej noga, czas (Time.time) i błąd (s; ujemny = za wcześnie).</summary>
    public Judgement LastJudgement { get; private set; }
    public Foot LastJudgedFoot { get; private set; }
    public float LastJudgementTime { get; private set; } = float.NegativeInfinity;
    public float LastError { get; private set; }
    /// <summary>Ile kolejnych kliknięć było idealnych lub w oknie łaski.</summary>
    public int HitStreak { get; private set; }
    /// <summary>Każda ocena kliknięcia: noga, ocena, błąd w sekundach (ujemny = za wcześnie), mnożnik mocy skoku.</summary>
    public event System.Action<Foot, Judgement, float, float> Judged;

    public int FrontGear => frontIndex + 1;
    public int FrontGearCount => chainrings.Length;
    public int RearGear => rearIndex + 1;
    public int RearGearCount => cogs.Length;
    /// <summary>Obroty koła na jeden obrót korby (większe = cięższy bieg).</summary>
    public float GearRatio => (float)chainrings[frontIndex] / cogs[rearIndex];

    public int FrontIndex => frontIndex;
    public int RearIndex => rearIndex;
    /// <summary>Przełożenie dla dowolnej kombinacji biegów (indeksy od 0), np. do wizualizacji w HUD.</summary>
    public float RatioAt(int front, int rear) => (float)chainrings[front] / cogs[rear];

    float OmegaFor(float ratio, float v) => Mathf.Max(v, minEffectiveSpeed) / (ratio * wheelRadius);
    float CrankOmega(float v) => OmegaFor(GearRatio, v);
    public float CadenceRpm(float v) => CrankOmega(v) * 60f / (2f * Mathf.PI);
    /// <summary>Kadencja (obr/min), przy której moment spada do zera.</summary>
    public float MaxCadenceRpm => maxCrankOmega * 60f / (2f * Mathf.PI);

    /// <summary>Zakres kadencji (obr/min), w którym rowerzysta oddaje ≥ sweetPowerFraction szczytowej mocy.</summary>
    public Vector2 SweetCadenceRpm
    {
        get
        {
            // Moc ~ 4x(1-x), x = omega / omegaMax; 4x(1-x) >= s  =>  x = 0.5 ± sqrt(1 - s) / 2
            float half = Mathf.Sqrt(1f - sweetPowerFraction) * 0.5f;
            return new Vector2((0.5f - half) * MaxCadenceRpm, (0.5f + half) * MaxCadenceRpm);
        }
    }

    /// <summary>Jaką część maksymalnego momentu rowerzysta może wygenerować przy tej kadencji (0..1).</summary>
    public float TorqueFactor(float v) => Mathf.Clamp01(1f - CrankOmega(v) / maxCrankOmega);

    /// <summary>Przyspieszenie od napędu (m/s^2) przy zadanym przełożeniu i prędkości, o ile ktoś pcha pedał.</summary>
    public float DriveAccelerationAt(float ratio, float v)
    {
        float torque = peakTorque * Mathf.Clamp01(1f - OmegaFor(ratio, v) / maxCrankOmega);
        return torque * drivetrainEfficiency / (ratio * wheelRadius) / totalMass;
    }

    /// <summary>Wydajność pedałowania 0..1 (ułamek szczytowej mocy) przy obecnym biegu i danej prędkości.</summary>
    public float PowerFraction(float v)
    {
        float x = Mathf.Clamp01(CrankOmega(v) / maxCrankOmega);
        return 4f * x * (1f - x);
    }

    // ---- stamina

    /// <summary>Stamina 0..1.</summary>
    public float Stamina => stamina;
    /// <summary>Zmiana staminy na sekundę w tej chwili (do HUD).</summary>
    public float StaminaTrend { get; private set; }
    /// <summary>Mnożnik szybkości pedałowania i siły: 1 przy dobrej staminie, mniej, gdy się zmęczysz.</summary>
    public float PedalSpeedFactor =>
        Mathf.Lerp(minPedalSpeedFactor, 1f, Mathf.Clamp01(stamina / penaltyStartStamina));

    /// <summary>Ile trwa jeden skok pedału (s) przy danej prędkości i obecnym biegu (z karą za zmęczenie).</summary>
    public float StrokeTime(float v) => 1f / StrokeRate(v);

    void Awake()
    {
        frontIndex = Mathf.Clamp(startFrontGear - 1, 0, chainrings.Length - 1);
        rearIndex = Mathf.Clamp(startRearGear - 1, 0, cogs.Length - 1);
    }

    /// <summary>Lewa manetka (przerzutka przednia, 1–3). direction: +1 wyżej, -1 niżej.</summary>
    public void ShiftFront(int direction)
    {
        int before = frontIndex;
        frontIndex = Mathf.Clamp(frontIndex + direction, 0, chainrings.Length - 1);
        Emit(frontIndex == before ? $"shift_front_blocked {FrontGear}" : $"shift_front {before + 1}->{FrontGear}");
    }

    /// <summary>Prawa manetka (przerzutka tylna, 1–7). direction: +1 wyżej, -1 niżej.</summary>
    public void ShiftRear(int direction)
    {
        int before = rearIndex;
        rearIndex = Mathf.Clamp(rearIndex + direction, 0, cogs.Length - 1);
        Emit(rearIndex == before ? $"shift_rear_blocked {RearGear}" : $"shift_rear {before + 1}->{RearGear}");
    }

    /// <summary>Zdarzenia pedałowania dla telemetrii (np. "begin L", "complete", "release_early p=0.42").</summary>
    public event System.Action<string> Logged;
    void Emit(string message) => Logged?.Invoke(message);

    /// <summary>Przyspieszenie (m/s^2) od nogi na pedale w tej chwili; 0 gdy nikt nie pcha.</summary>
    public float GetDriveAcceleration(float speed)
    {
        // Siła skoku wynika z jego profilu i oceny trafienia; po końcu skoku napęd jeszcze chwilę zanika (wybieg).
        float level = (Pushing ? ProfileAt(progress) * quality : 0f) + tailLevel;
        if (level <= 0f) return 0f;
        // Zmęczony rowerzysta kręci wolniej i słabiej; skok trwa dłużej, ale za jednostkę czasu daje mniej.
        return DriveAccelerationAt(GearRatio, speed) * level * PedalSpeedFactor;
    }

    /// <summary>
    /// Siła na pedale rośnie od zera na szczycie, osiąga maksimum w połowie skoku i znów spada
    /// (średnia po całym skoku = 1).
    /// </summary>
    static float ProfileAt(float p) => Mathf.Sin(Mathf.PI * Mathf.Clamp01(p)) * (Mathf.PI * 0.5f);

    /// <summary>Uruchamia wybieg napędu po ukończonym skoku (nie zmniejsza już trwającego wybiegu).</summary>
    void StartTail(float level)
    {
        if (tailTime <= 0f || level <= tailLevel) return;
        tailLevel = level;
    }

    /// <summary>Przerwanie pedałowania (np. upadek): czyści stan skoku i rytm, ale zostawia staminę pomniejszoną o staminaLoss.</summary>
    public void Interrupt(float staminaLoss)
    {
        float keep = stamina;
        ResetState();
        stamina = Mathf.Clamp01(keep - staminaLoss);
        Emit("interrupted");
    }

    public void ResetState()
    {
        last = Foot.None;
        inRhythm = false;
        tailLevel = 0f;
        progress = 1f;
        timeSinceEnd = 0f;
        quality = 1f;
        Pushing = false;
        stamina = 1f;
        StaminaTrend = 0f;
        lastEffortTime = float.NegativeInfinity;
        LastJudgement = Judgement.None;
        LastJudgementTime = float.NegativeInfinity;
        HitStreak = 0;
    }

    /// <summary>Szybkość postępu skoku (skoki/s) – wynika z prędkości roweru i biegu.</summary>
    float StrokeRate(float v) =>
        Mathf.Clamp(CrankOmega(v) / Mathf.PI * tempoScale, 1f / maxStrokeTime, 1f / minStrokeTime) * PedalSpeedFactor;

    static Foot Opposite(Foot f) => f == Foot.Left ? Foot.Right : f == Foot.Right ? Foot.Left : Foot.None;

    void Update()
    {
        float dt = Time.deltaTime;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb[leftKey].wasPressedThisFrame) Tap(Foot.Left, TapOffset(kb, dt));
            if (kb[rightKey].wasPressedThisFrame) Tap(Foot.Right, TapOffset(kb, dt));
        }

        Advance(dt);
        UpdateStamina(dt);

        // Korba obraca się o faktyczny postęp skoku.
        float target = crankBase + 180f * progress;
        angle = Mathf.MoveTowards(angle, target, crankFollowSpeed * dt);
        transform.localRotation = Quaternion.Euler(angle, 0f, 0f);

        // Pedały pozostają poziomo niezależnie od obrotu korby.
        Quaternion level = Quaternion.Euler(-angle, 0f, 0f);
        if (leftPedal != null) leftPedal.localRotation = level;
        if (rightPedal != null) rightPedal.localRotation = level;
    }

    /// <summary>
    /// Kiedy w tej klatce (sekundy od jej początku) naprawdę wciśnięto klawisz: z czasu zdarzenia wejścia, żeby ocena
    /// nie zależała od długości klatki. Poza zakresem klatki (np. inny zegar w edytorze) przycina się do jej końca.
    /// </summary>
    static float TapOffset(Keyboard kb, float dt)
    {
        float age = (float)(InputState.currentTime - kb.lastUpdateTime);
        if (float.IsNaN(age)) age = 0f;
        return dt - Mathf.Clamp(age, 0f, dt);
    }

    /// <summary>Błąd kliknięcia w sekundach względem końca skoku: ujemny = za wcześnie, dodatni = za późno.</summary>
    float TimingError(float offset)
    {
        float t = offset + timingOffset;
        return progress < 1f ? t - (1f - progress) * StrokeTime(Speed) : timeSinceEnd + t;
    }

    void Tap(Foot foot, float offset)
    {
        if (!inRhythm)
        {
            // Pierwszy klik (postój albo zerwany rytm): zawsze zalicza się jako normalny skok, bez oceny.
            Begin(foot, Judgement.Start, goodMultiplier, 0f);
            return;
        }

        if (foot == last)
        {
            // Ta noga właśnie pchnęła – trzeba zmienić nogę.
            Fail(foot, Judgement.WrongFoot, TimingError(offset), 1f);
            return;
        }

        float stroke = StrokeTime(Speed);
        float err = TimingError(offset);
        float abs = Mathf.Abs(err);
        float perfect = PerfectWindow, grace = GraceWindow;

        if (abs <= perfect) { Begin(foot, Judgement.Perfect, perfectMultiplier, err); return; }
        if (abs <= grace) { Begin(foot, Judgement.Good, goodMultiplier, err); return; }

        if (err < 0f && -err > earlyRejectFraction * stroke)
        {
            // Dużo za wcześnie – klepanie w klawisze nie przyspiesza.
            Fail(foot, Judgement.Mash, err, 1f);
            return;
        }

        // Poza oknem łaski: skok się zalicza, ale tym słabszy, im większy błąd; kosztuje też siły.
        float span = Mathf.Max(0.5f * stroke - grace, 0.05f);
        float severity = Mathf.Clamp01((abs - grace) / span);
        Begin(foot, err < 0f ? Judgement.Early : Judgement.Late, Mathf.Lerp(goodMultiplier, poorMultiplier, severity), err);
        stamina = Mathf.Clamp01(stamina - missStamina * severity);
    }

    void Begin(Foot foot, Judgement judgement, float multiplier, float err)
    {
        // Wczesne kliknięcie ucina resztę poprzedniego skoku (jego niewykorzystana siła przepada), późne zostawia przerwę.
        crankBase += 180f;
        last = foot;
        inRhythm = true;
        progress = 0f;
        timeSinceEnd = 0f;
        quality = multiplier;
        tailLevel = 0f;
        Pushing = true;

        if (judgement == Judgement.Perfect) stamina = Mathf.Clamp01(stamina + perfectStamina);
        bool hit = judgement == Judgement.Perfect || judgement == Judgement.Good || judgement == Judgement.Start;
        HitStreak = hit ? HitStreak + 1 : 0;
        Report(foot, judgement, err, multiplier);
    }

    void Fail(Foot foot, Judgement judgement, float err, float severity)
    {
        stamina = Mathf.Clamp01(stamina - missStamina * severity);
        HitStreak = 0;
        Report(foot, judgement, err, 0f);
    }

    void Report(Foot foot, Judgement judgement, float err, float multiplier)
    {
        LastJudgement = judgement;
        LastJudgedFoot = foot;
        LastJudgementTime = Time.time;
        LastError = err;
        Emit($"judge_{judgement.ToString().ToLowerInvariant()} {(foot == Foot.Left ? "L" : "R")} err={err * 1000f:+0;-0;0}ms x{multiplier:0.00}");
        Judged?.Invoke(foot, judgement, err, multiplier);
    }

    /// <summary>Postęp skoku i odliczanie przerwy po jego końcu; zbyt długa przerwa zrywa rytm.</summary>
    void Advance(float dt)
    {
        if (!inRhythm) { Pushing = false; return; }

        if (progress < 1f)
        {
            float rate = StrokeRate(Speed);
            float need = (1f - progress) / rate;
            Pushing = true;
            if (dt < need)
            {
                progress += rate * dt;
            }
            else
            {
                // Skok skończył się w tej klatce; reszta klatki to już przerwa przed następnym kliknięciem.
                progress = 1f;
                timeSinceEnd = dt - need;
                StartTail(tailFloor * quality);
                Emit("stroke_end");
            }
        }
        else
        {
            Pushing = false;
            timeSinceEnd += dt;
            if (timeSinceEnd > Mathf.Max(lateLimit, lateLimitFraction * StrokeTime(Speed)))
            {
                inRhythm = false;
                HitStreak = 0;
                Emit("rhythm_lost");
            }
        }

        // Wybieg napędu zanika wykładniczo.
        if (tailLevel > 0f)
        {
            tailLevel *= Mathf.Exp(-dt / Mathf.Max(0.01f, tailTime));
            if (tailLevel < 0.01f) tailLevel = 0f;
        }
    }

    /// <summary>
    /// Stamina zależy od wydajności pedałowania przy obecnej kadencji: w zielonej strefie (>= sweetPowerFraction)
    /// się nie męczysz, a im bliżej środka strefy, tym szybciej odpoczywasz. Poza strefą ubywa ci sił,
    /// tym szybciej, im dalej od niej. Gdy nie pedałujesz, regenerujesz się powoli niezależnie od biegu.
    /// Dodatkowo kosztują siły błędy rytmu (patrz Tap/Fail).
    /// </summary>
    void UpdateStamina(float dt)
    {
        if (Pushing || inRhythm)
            lastEffortTime = Time.time;

        float trend;
        if (Time.time - lastEffortTime > 0.5f)
        {
            trend = staminaCoastRegen;
        }
        else
        {
            float e = PowerFraction(Speed);
            trend = e >= sweetPowerFraction
                ? staminaRegen * (e - sweetPowerFraction) / (1f - sweetPowerFraction)
                : -staminaDrain * Mathf.Pow((sweetPowerFraction - e) / sweetPowerFraction, drainExponent);
        }

        StaminaTrend = trend;
        stamina = Mathf.Clamp01(stamina + trend * dt);
    }
}
