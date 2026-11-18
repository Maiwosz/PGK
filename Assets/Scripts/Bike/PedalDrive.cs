using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Rdzeń pedałowania na obiekcie roweru (wspólny dla wszystkich trybów sterowania):
///  - biegi: 3 x 7 kombinacji ustawionych w jednej kolejności od najlżejszej do najcięższej, wydajność biegu przy prędkości,
///  - model napędu: moment na korbie maleje z kadencją, siła = moment / (przełożenie * promień koła),
///  - wytrzymałość (stamina), wybieg napędu po skoku, obrót korby i pedałów (obiekt Crank to tylko wizualizacja),
///  - wspólny stan skoku (noga, postęp, siła), który zapisuje aktywny tryb.
/// Samo "jak się pedałuje" (rytm albo przytrzymanie) robi tryb – osobny komponent dziedziczący po <see cref="PedalingMode"/>
/// (RhythmPedaling, HoldPedaling). Aktywny tryb wskazuje pole Mode; zmiana w inspektorze działa od razu, także w Play.
/// Każdy tryb jest samowystarczalny, więc można go usunąć razem z jego HUD, nie ruszając rdzenia.
/// </summary>
public class PedalDrive : MonoBehaviour
{
    public enum Foot { None, Left, Right }

    /// <summary>Ocena kliknięcia. Start = pierwszy klik (rower stoi albo seria się zerwała); Early/Late = poza oknem łaski.</summary>
    public enum Judgement { None, Start, Perfect, Good, Early, Late, Mash, WrongFoot }

    [Header("Tryb sterowania")]
    [Tooltip("Aktywny tryb pedałowania: przeciągnij tu komponent trybu z tego samego obiektu (RhythmPedaling albo HoldPedaling).")]
    [SerializeField] PedalingMode mode;

    [Header("Biegi (kombinacje tarcza x zębatka w jednej kolejności od najlżejszej do najcięższej)")]
    [Tooltip("Liczba zębów tarcz z przodu.")]
    [SerializeField] int[] chainrings = { 28, 38, 48 };
    [Tooltip("Liczba zębów zębatek kasety z tyłu.")]
    [SerializeField] int[] cogs = { 32, 28, 24, 21, 18, 16, 14 };
    [Tooltip("Ile biegów ma gracz: z 21 kombinacji wybieramy tyle, rozłożonych równomiernie (geometrycznie) na pełnym zakresie przełożeń, od najlżejszej do najcięższej kombinacji.")]
    [SerializeField, Range(2, 21)] int gearCount = 7;
    [Tooltip("Rower startuje (i wstaje po upadku) na biegu optymalnym dla tej prędkości (m/s) – typowej dla ruszania. " +
             "Ta sama prędkość służy pasku biegów na postoju, żeby optimum było widać od startu.")]
    [SerializeField, Range(1.5f, 8f)] float startOptimumSpeed = 3f;

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
    [SerializeField] float minStrokeTime = 0.07f;
    [SerializeField] float maxStrokeTime = 1.6f;

    [Header("Optimum biegu")]
    [Tooltip("Wydajność (ułamek szczytowej mocy przy danej kadencji), od której bieg uznajemy za optymalny (zielony na pasku biegów). " +
             "Bieg nie wpływa na zmęczenie, tylko na moc napędu.")]
    [SerializeField, Range(0.5f, 0.99f)] float sweetPowerFraction = 0.95f;

    [Header("Wytrzymałość (stamina) – zależy tylko od jakości pedałowania")]
    [Tooltip("Regeneracja na sekundę podczas serii dobrych trafień.")]
    [SerializeField] float staminaRegen = 0.05f;
    [Tooltip("Ile kolejnych dobrych trafień trzeba, żeby zaczęła się regeneracja.")]
    [SerializeField] int regenStreak = 3;
    [Tooltip("Regeneracja na sekundę, gdy nie pedałujesz (koasting, postój).")]
    [SerializeField] float staminaCoastRegen = 0.03f;
    [Tooltip("Poniżej tego poziomu stamina zaczyna spowalniać pedałowanie.")]
    [SerializeField, Range(0.05f, 1f)] float penaltyStartStamina = 0.5f;
    [Tooltip("Mnożnik szybkości pedałowania (i siły) przy pustej staminie.")]
    [SerializeField, Range(0.1f, 1f)] float minPedalSpeedFactor = 0.35f;

    [Header("Płynność napędu")]
    [Tooltip("Wybieg napędu: po ukończonym skoku napęd nie urywa się, tylko zanika w tym czasie (s). 0 = wyłączony.")]
    [SerializeField] float tailTime = 0.10f;
    [Tooltip("Poziom wybiegu po ukończonym skoku (profil siły: średnia skoku = 1, szczyt ok. 1,57), mnożony przez jakość skoku.")]
    [SerializeField] float tailFloor = 0.5f;

    [Header("Wizualizacja korby")]
    [Tooltip("Obiekt korby obracany razem ze skokiem (tylko wizualizacja; logika siedzi na rowerze).")]
    [SerializeField] Transform crank;
    [SerializeField] float crankFollowSpeed = 1500f;
    [SerializeField] Transform leftPedal;
    [SerializeField] Transform rightPedal;

    // ---- wspólny stan skoku (zapisuje go aktywny tryb)
    float crankBase;               // kąt korby (stopnie) na początku bieżącego skoku
    float angle;
    float tailLevel;               // poziom wybiegu napędu (w jednostkach profilu siły), zanika wykładniczo
    float stamina = 1f;
    float lastEffortTime = float.NegativeInfinity;
    int frontIndex;
    int rearIndex;
    int[] order;                   // kombinacje biegów (tarcza * 100 + zębatka) posortowane od najlżejszej do najcięższej
    int gearPos;
    PedalingMode appliedMode;

    /// <summary>Aktywny tryb pedałowania.</summary>
    public PedalingMode Mode => mode;
    /// <summary>Klawisze nóg: przypisuje gracz w menu ustawień (Esc).</summary>
    public Key LeftKey => GameSettings.GetKey(GameAction.PedalLeft);
    public Key RightKey => GameSettings.GetKey(GameAction.PedalRight);
    public Key KeyOf(Foot foot) => foot == Foot.Left ? LeftKey : RightKey;

    /// <summary>Zmienia aktywny tryb pedałowania (stan skoku czyści się w następnej klatce).</summary>
    public void SetMode(PedalingMode newMode) { if (newMode != null) mode = newMode; }
    public static Foot Opposite(Foot f) => f == Foot.Left ? Foot.Right : f == Foot.Right ? Foot.Left : Foot.None;

    /// <summary>Aktualna prędkość roweru (m/s) – ustawia BikeController.</summary>
    public float Speed { get; set; }
    /// <summary>Noga naciska pedał (trwa skok) – napęd aktywny.</summary>
    public bool Pushing { get; set; }
    /// <summary>Noga ostatniego skoku (None, gdy seria jeszcze się nie zaczęła).</summary>
    public Foot StrokeFoot { get; set; }
    /// <summary>Noga, która właśnie wykonuje skok (None, gdy skok się skończył).</summary>
    public Foot ActiveFoot => Pushing ? StrokeFoot : Foot.None;
    /// <summary>Postęp bieżącego skoku 0..1 (1 = skończony).</summary>
    public float Progress { get; set; } = 1f;
    /// <summary>Mnożnik siły bieżącego skoku (ocena trafienia).</summary>
    public float Quality { get; set; } = 1f;
    /// <summary>Trwa seria pedałowania (inaczej pierwszy klik zaczyna nową bez oceny).</summary>
    public bool Engaged { get; set; }
    /// <summary>Ile kolejnych kliknięć było dobrych (idealnych, w oknie łaski lub startowych).</summary>
    public int HitStreak { get; set; }

    /// <summary>Ostatnia ocena kliknięcia, jej noga, czas (Time.time) i błąd (s; ujemny = za wcześnie).</summary>
    public Judgement LastJudgement { get; private set; }
    public Foot LastJudgedFoot { get; private set; }
    public float LastJudgementTime { get; private set; } = float.NegativeInfinity;
    public float LastError { get; private set; }
    /// <summary>Każda ocena kliknięcia: noga, ocena, błąd w sekundach (ujemny = za wcześnie), mnożnik mocy skoku.</summary>
    public event System.Action<Foot, Judgement, float, float> Judged;
    /// <summary>Zdarzenia pedałowania dla telemetrii (np. "judge_perfect L ...", "stroke_end").</summary>
    public event System.Action<string> Logged;

    /// <summary>Zgłasza ocenę kliknięcia (wywołuje tryb).</summary>
    public void Report(Foot foot, Judgement judgement, float error, float multiplier)
    {
        LastJudgement = judgement;
        LastJudgedFoot = foot;
        LastJudgementTime = Time.time;
        LastError = error;
        Log($"judge_{judgement.ToString().ToLowerInvariant()} {(foot == Foot.Left ? "L" : "R")} err={error * 1000f:+0;-0;0}ms x{multiplier:0.00}");
        Judged?.Invoke(foot, judgement, error, multiplier);
    }

    public void Log(string message) => Logged?.Invoke(message);

    // ---- biegi

    public int FrontGear => frontIndex + 1;
    public int RearGear => rearIndex + 1;
    /// <summary>Liczba biegów w jednej kolejności (od najlżejszego do najcięższego).</summary>
    public int GearCount => order != null ? order.Length : chainrings.Length * cogs.Length;
    /// <summary>Miejsce aktualnego biegu w kolejności: 0 = najlżejszy.</summary>
    public int GearPosition => gearPos;
    /// <summary>Numer biegu od 1 (1 = najlżejszy).</summary>
    public int GearNumber => gearPos + 1;
    /// <summary>Obroty koła na jeden obrót korby (większe = cięższy bieg).</summary>
    public float GearRatio => RatioAtPosition(gearPos);

    /// <summary>Przełożenie biegu o danym miejscu w kolejności (0 = najlżejszy).</summary>
    public float RatioAtPosition(int pos)
    {
        int code = order[Mathf.Clamp(pos, 0, order.Length - 1)];
        return (float)chainrings[code / 100] / cogs[code % 100];
    }

    /// <summary>Wydajność pedałowania 0..1 na biegu o danym miejscu przy prędkości v.</summary>
    public float EfficiencyAtPosition(int pos, float v)
    {
        float x = Mathf.Clamp01(OmegaFor(RatioAtPosition(pos), v) / maxCrankOmega);
        return 4f * x * (1f - x);
    }

    /// <summary>Próg wydajności, od którego bieg jest optymalny (zielony).</summary>
    public float OptimumThreshold => sweetPowerFraction;

    /// <summary>Prędkość, względem której oceniamy biegi: na postoju i przy ruszaniu bierzemy prędkość startową.</summary>
    public float GearReferenceSpeed(float v) => Mathf.Max(v, startOptimumSpeed);

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

    void Awake()
    {
        // Wszystkie kombinacje tarcza x zębatka w jednej kolejności od najlżejszej do najcięższej.
        var codes = new System.Collections.Generic.List<int>();
        for (int f = 0; f < chainrings.Length; f++)
            for (int r = 0; r < cogs.Length; r++)
                codes.Add(f * 100 + r);
        codes.Sort((a, b) => ((float)chainrings[a / 100] / cogs[a % 100]).CompareTo((float)chainrings[b / 100] / cogs[b % 100]));
        order = PickGears(codes, Mathf.Clamp(gearCount, 2, codes.Count));
        SelectStartGear();
        if (mode == null) mode = GetComponent<PedalingMode>();
        var chosen = GameSettings.ChosenMode(this);     // wybór gracza z menu ma pierwszeństwo przed inspektorem
        if (chosen != null) mode = chosen;
    }

    /// <summary>
    /// Z posortowanych kombinacji wybiera count biegów: skrajne (najlżejszy i najcięższy) plus pośrednie o przełożeniach
    /// rozłożonych równomiernie w skali logarytmicznej (każdy bieg zmienia kadencję o podobny ułamek), bez powtórzeń.
    /// </summary>
    int[] PickGears(System.Collections.Generic.List<int> sorted, int count)
    {
        float Ratio(int code) => (float)chainrings[code / 100] / cogs[code % 100];
        if (count >= sorted.Count) return sorted.ToArray();
        float lo = Mathf.Log(Ratio(sorted[0])), hi = Mathf.Log(Ratio(sorted[sorted.Count - 1]));
        var picked = new System.Collections.Generic.List<int>();
        int from = 0;
        for (int i = 0; i < count; i++)
        {
            float target = lo + (hi - lo) * i / (count - 1);
            int best = -1;
            float bestDist = float.MaxValue;
            // Szukamy najbliższej kombinacji, zostawiając miejsce na pozostałe biegi (bez powtórzeń).
            int last = sorted.Count - (count - i);
            for (int k = from; k <= last; k++)
            {
                float d = Mathf.Abs(Mathf.Log(Ratio(sorted[k])) - target);
                if (d < bestDist) { bestDist = d; best = k; }
            }
            picked.Add(sorted[best]);
            from = best + 1;
        }
        return picked.ToArray();
    }

    /// <summary>Ustawia bieg o największej wydajności przy prędkości startowej.</summary>
    void SelectStartGear()
    {
        if (order == null) return;
        int best = 0;
        float bestEff = -1f;
        for (int i = 0; i < order.Length; i++)
        {
            float e = EfficiencyAtPosition(i, startOptimumSpeed);
            if (e > bestEff + 1e-4f) { bestEff = e; best = i; }
        }
        gearPos = best;
        ApplyGear();
    }

    void ApplyGear()
    {
        frontIndex = order[gearPos] / 100;
        rearIndex = order[gearPos] % 100;
    }

    /// <summary>Zmiana biegu o jedno miejsce: +1 cięższy, -1 lżejszy.</summary>
    public void Shift(int direction)
    {
        int before = gearPos;
        gearPos = Mathf.Clamp(gearPos + direction, 0, order.Length - 1);
        ApplyGear();
        Log(gearPos == before ? $"shift_blocked {GearNumber}" : $"shift {before + 1}->{GearNumber}");
    }

    // ---- stamina

    /// <summary>Stamina 0..1.</summary>
    public float Stamina => stamina;
    /// <summary>Zmiana staminy na sekundę w tej chwili.</summary>
    public float StaminaTrend { get; private set; }
    /// <summary>Mnożnik szybkości pedałowania i siły: 1 przy dobrej staminie, mniej, gdy się zmęczysz.</summary>
    public float PedalSpeedFactor =>
        Mathf.Lerp(minPedalSpeedFactor, 1f, Mathf.Clamp01(stamina / penaltyStartStamina));

    /// <summary>Zmienia staminę (ujemne = koszt błędu, dodatnie = nagroda).</summary>
    public void AddStamina(float delta) => stamina = Mathf.Clamp01(stamina + delta);

    /// <summary>
    /// Stamina zależy tylko od jakości pedałowania: błędy kosztują siły (tryb wywołuje AddStamina), seria dobrych trafień
    /// pozwala odpoczywać, a bez pedałowania regenerujesz się powoli. Bieg wpływa wyłącznie na moc napędu.
    /// </summary>
    void UpdateStamina(float dt)
    {
        if (Pushing || Engaged)
            lastEffortTime = Time.time;

        float trend = 0f;
        if (Time.time - lastEffortTime > 0.5f) trend = staminaCoastRegen;
        else if (Engaged && HitStreak >= regenStreak) trend = staminaRegen;

        StaminaTrend = trend;
        stamina = Mathf.Clamp01(stamina + trend * dt);
    }

    // ---- tempo i napęd

    /// <summary>Szybkość postępu skoku (skoki/s) – wynika z prędkości roweru, biegu i tempa trybu.</summary>
    public float StrokeRate(float v)
    {
        float tempo = mode != null ? mode.TempoScale : 1f;
        return Mathf.Clamp(CrankOmega(v) / Mathf.PI * tempo, 1f / maxStrokeTime, 1f / minStrokeTime) * PedalSpeedFactor;
    }

    /// <summary>Ile trwa jeden skok pedału (s) przy danej prędkości i obecnym biegu (z karą za zmęczenie).</summary>
    public float StrokeTime(float v) => 1f / StrokeRate(v);

    /// <summary>Przyspieszenie (m/s^2) od nogi na pedale w tej chwili; 0 gdy nikt nie pcha.</summary>
    public float GetDriveAcceleration(float speed)
    {
        // Siła skoku wynika z jego profilu i oceny trafienia; po końcu skoku napęd jeszcze chwilę zanika (wybieg).
        float level = (Pushing ? ProfileAt(Progress) * Quality : 0f) + tailLevel;
        if (level <= 0f) return 0f;
        // Zmęczony rowerzysta kręci wolniej i słabiej; skok trwa dłużej, ale za jednostkę czasu daje mniej.
        return DriveAccelerationAt(GearRatio, speed) * level * PedalSpeedFactor;
    }

    /// <summary>
    /// Siła na pedale rośnie od zera na szczycie, osiąga maksimum w połowie skoku i znów spada
    /// (średnia po całym skoku = 1).
    /// </summary>
    public static float ProfileAt(float p) => Mathf.Sin(Mathf.PI * Mathf.Clamp01(p)) * (Mathf.PI * 0.5f);

    /// <summary>Uruchamia wybieg napędu po ukończonym skoku (nie zmniejsza już trwającego wybiegu); level = poziom wybiegu.</summary>
    public void StartTail(float level)
    {
        if (tailTime <= 0f || level <= tailLevel) return;
        tailLevel = level;
    }

    /// <summary>Poziom wybiegu po zwykłym ukończeniu skoku (tailFloor razy jakość).</summary>
    public float DefaultTailLevel => tailFloor * Quality;
    public float TailFloor => tailFloor;
    /// <summary>Kasuje wybieg (np. przy starcie nowego skoku).</summary>
    public void ClearTail() => tailLevel = 0f;

    /// <summary>Korba przechodzi do kolejnego pół obrotu (start nowego skoku).</summary>
    public void AdvanceCrank() => crankBase += 180f;

    /// <summary>Przerwanie pedałowania (np. upadek): czyści stan skoku, ale zostawia staminę pomniejszoną o staminaLoss.</summary>
    public void Interrupt(float staminaLoss)
    {
        float keep = stamina;
        ResetState();
        stamina = Mathf.Clamp01(keep - staminaLoss);
        Log("interrupted");
    }

    public void ResetState()
    {
        SelectStartGear();
        StrokeFoot = Foot.None;
        Engaged = false;
        tailLevel = 0f;
        Progress = 1f;
        Quality = 1f;
        Pushing = false;
        stamina = 1f;
        StaminaTrend = 0f;
        lastEffortTime = float.NegativeInfinity;
        LastJudgement = Judgement.None;
        LastJudgementTime = float.NegativeInfinity;
        HitStreak = 0;
        if (mode != null) mode.ResetMode();
    }

    void Update()
    {
        if (Time.timeScale == 0f) return;     // pauza (np. okno pomocy): bez klawiszy i bez zmian stanu
        float dt = Time.deltaTime;

        if (mode == null) mode = GetComponent<PedalingMode>();
        // Zmiana trybu w inspektorze (także w Play): czysty stan, żeby nie został skok z poprzedniego trybu.
        if (mode != appliedMode) { appliedMode = mode; ResetState(); }

        if (mode != null) mode.Tick(dt, Keyboard.current);

        // Wybieg napędu zanika wykładniczo.
        if (tailLevel > 0f)
        {
            tailLevel *= Mathf.Exp(-dt / Mathf.Max(0.01f, tailTime));
            if (tailLevel < 0.01f) tailLevel = 0f;
        }

        UpdateStamina(dt);
        UpdateCrank(dt);
    }

    /// <summary>Korba obraca się o faktyczny postęp skoku; pedały zostają poziomo.</summary>
    void UpdateCrank(float dt)
    {
        float target = crankBase + 180f * Progress;
        angle = Mathf.MoveTowards(angle, target, crankFollowSpeed * dt);
        if (crank != null) crank.localRotation = Quaternion.Euler(angle, 0f, 0f);

        Quaternion level = Quaternion.Euler(-angle, 0f, 0f);
        if (leftPedal != null) leftPedal.localRotation = level;
        if (rightPedal != null) rightPedal.localRotation = level;
    }
}
