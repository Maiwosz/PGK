using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Pedałowanie na przytrzymanie: A = lewa noga, D = prawa noga, naprzemiennie.
/// Jeden skok pedału (pół obrotu korby) trwa tak długo, jak kręci się korba przy
/// aktualnej prędkości i biegu: kadencja = prędkość / (przełożenie * promień koła).
/// Wyższy bieg lub niższa prędkość = wolniejszy obrót korby = dłuższe trzymanie klawisza.
/// Siła napędu = moment na korbie / (przełożenie * promień koła); moment maleje
/// wraz z kadencją (przy zbyt szybkim kręceniu rowerzysta "nie ma czym naciskać").
/// Napęd działa tylko dopóki klawisz jest trzymany i skok nie dobiegł końca.
/// Skok trzeba dokończyć: puszczony w połowie zostawia korbę w miejscu i wolno go kontynuować
/// tylko tą samą nogą, a trzymanie drugiej nogi naciska pedał z tyłu i cofa korbę. Puszczenie klawisza
/// tuż przed końcem (okno łaski) zalicza skok i pozwala od razu przejść na drugą nogę.
/// </summary>
public class PedalDrive : MonoBehaviour
{
    public enum Foot { None, Left, Right }

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
    [SerializeField] float minStrokeTime = 0.07f;
    [SerializeField] float maxStrokeTime = 1.6f;

    [Header("Zasady skoku")]
    [Tooltip("Okno łaski (s): puszczenie klawisza na tyle przed końcem skoku nadal go zalicza.")]
    [SerializeField] float graceTime = 0.12f;
    [Tooltip("Jak szybko druga noga cofa korbę niedokończonego skoku (1 = tak szybko, jak by szła do przodu).")]
    [SerializeField] float reverseSpeedFactor = 1f;

    [Header("Płynność napędu")]
    [Tooltip("Wybieg napędu: po ukończonym skoku (też puszczonym w oknie łaski) napęd nie urywa się, tylko zanika w tym czasie (s). 0 = wyłączony.")]
    [SerializeField] float tailTime = 0.10f;
    [Tooltip("Najmniejszy poziom wybiegu po ukończonym skoku (profil siły: średnia skoku = 1, szczyt ok. 1,57).")]
    [SerializeField] float tailFloor = 0.5f;
    [Tooltip("Bufor następnej nogi (s): druga noga naciśnięta tyle (plus okno łaski) przed końcem skoku nie cofa korby, tylko rusza następny skok od razu po zakończeniu bieżącego. 0 = wyłączony.")]
    [SerializeField] float bufferTime = 0.12f;

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

    Foot last = Foot.None;
    Foot active = Foot.None;
    float crankBase;        // kąt korby (stopnie) na początku bieżącego skoku
    float progress;
    float angle;
    float missTimer;
    Foot missFoot = Foot.None;
    Foot reversing = Foot.None;   // noga, która trzyma klawisz i cofa niedokończony skok
    Foot queued = Foot.None;      // noga naciśnięta tuż przed końcem skoku – wystartuje, gdy bieżący się skończy
    float tailLevel;              // poziom wybiegu napędu (w jednostkach profilu siły), zanika wykładniczo
    float stamina = 1f;
    float lastEffortTime = float.NegativeInfinity;
    int frontIndex;
    int rearIndex;

    /// <summary>Aktualna prędkość roweru (m/s) – ustawia BikeController.</summary>
    public float Speed { get; set; }
    public bool Pushing { get; private set; }

    public Foot ActiveFoot => active;
    /// <summary>Postęp bieżącego skoku 0..1.</summary>
    public float Progress => progress;
    /// <summary>Noga, która wykonała (lub właśnie wykonuje) ostatni skok.</summary>
    public Foot StrokeFoot => last;
    /// <summary>Skok rozpoczęty, ale niedokończony (korba stoi w połowie) – tylko ta sama noga może go dokończyć.</summary>
    public bool StrokeUnfinished => last != Foot.None && progress < 1f && !InGrace;
    /// <summary>Którą nogą można teraz pchnąć: po dokończonym skoku drugą, po niedokończonym tą samą.</summary>
    public bool Expects(Foot foot) => StrokeUnfinished ? foot == last : foot != last;
    /// <summary>Noga, której ostatnie naciśnięcie było błędne.</summary>
    public Foot MissFoot => missFoot;
    /// <summary>Noga, która właśnie cofa korbę niedokończonego skoku (trzyma klawisz w złym momencie).</summary>
    public Foot ReversingFoot => reversing;
    public float MissFlash => Mathf.Clamp01(missTimer / 0.25f);

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

    /// <summary>Ile m/s przybywa za jeden pełny skok przy danej prędkości i obecnym biegu.</summary>
    public float StrokeGain(float v) => DriveAccelerationAt(GearRatio, v) * PedalSpeedFactor * StrokeTime(v);

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
        // Krótkie "klepnięcie" klawisza zostaje na początku skoku, gdzie siła jest bliska zeru, i prawie nic nie daje.
        // Wybieg (tailLevel) dochodzi tylko po ukończonym skoku, więc klepaniem go nie wykorzystasz.
        float level = (Pushing ? ProfileAt(progress) : 0f) + tailLevel;
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
        Emit($"tail {level:0.00}");
    }

    /// <summary>Przerwanie pedałowania (np. upadek): czyści stan skoku, ale zostawia staminę pomniejszoną o staminaLoss.</summary>
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
        active = Foot.None;
        reversing = Foot.None;
        queued = Foot.None;
        tailLevel = 0f;
        progress = 0f;
        Pushing = false;
        stamina = 1f;
        StaminaTrend = 0f;
        lastEffortTime = float.NegativeInfinity;
    }

    /// <summary>Szybkość postępu skoku (skoki/s) – wynika z prędkości roweru i biegu.</summary>
    float StrokeRate(float v) =>
        Mathf.Clamp(CrankOmega(v) / Mathf.PI, 1f / maxStrokeTime, 1f / minStrokeTime) * PedalSpeedFactor;

    /// <summary>Skok jest "w oknie łaski": do końca zostało nie więcej niż graceTime sekund.</summary>
    bool InGrace => progress < 1f && (1f - progress) / StrokeRate(Speed) <= graceTime;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb[leftKey].wasPressedThisFrame) Press(Foot.Left);
            if (kb[rightKey].wasPressedThisFrame) Press(Foot.Right);

            // Puszczenie klawisza tuż przed końcem skoku (okno łaski) dokańcza skok.
            // Puszczenie wcześniej zostawia korbę w połowie – trzeba kontynuować tą samą nogą.
            if (active != Foot.None && !kb[KeyOf(active)].isPressed)
            {
                if (InGrace)
                {
                    StartTail(Mathf.Max(ProfileAt(progress), tailFloor));
                    progress = 1f;
                    Emit("complete_grace");
                }
                else if (progress < 1f)
                {
                    Emit($"release_early p={progress:0.00}");
                    // Skok puszczony za wcześnie: zakolejkowana noga naciska pedał z tyłu, więc cofa korbę (jak bez bufora).
                    if (queued != Foot.None)
                    {
                        Foot q = queued;
                        queued = Foot.None;
                        if (kb[KeyOf(q)].isPressed)
                        {
                            reversing = q;
                            Miss(q);
                            Emit($"reverse {Tag(q)} p={progress:0.00}");
                        }
                    }
                }
                active = Foot.None;
            }

            // Zła noga puszczona (albo skok się już skończył) – przestaje cofać.
            if (reversing != Foot.None && (!kb[KeyOf(reversing)].isPressed || !StrokeUnfinished))
                reversing = Foot.None;

            // Gdy zła noga nie cofa, a właściwa wciąż trzyma klawisz, skok rusza dalej sam.
            if (reversing == Foot.None && active == Foot.None && StrokeUnfinished && kb[KeyOf(last)].isPressed)
                active = last;
        }
        else
        {
            active = Foot.None;
            reversing = Foot.None;
        }

        Pushing = false;
        float rate = StrokeRate(Speed);
        if (reversing != Foot.None)
        {
            // Naciskanie pedału, który jest jeszcze z tyłu, cofa korbę.
            progress = Mathf.Max(0f, progress - rate * reverseSpeedFactor * Time.deltaTime);
        }
        else if (active != Foot.None && progress < 1f)
        {
            // Korba obraca się z prędkością wynikającą z prędkości roweru i biegu.
            progress = Mathf.Min(1f, progress + rate * Time.deltaTime);
            Pushing = true;
            if (progress >= 1f)
            {
                Emit("complete");
                StartTail(tailFloor);
            }
        }

        // Zakolejkowana noga startuje dokładnie w chwili, gdy bieżący skok się kończy – bez przerwy w napędzie.
        if (queued != Foot.None && progress >= 1f)
        {
            Foot next = queued;
            queued = Foot.None;
            if (kb != null && kb[KeyOf(next)].isPressed)
            {
                crankBase += 180f;
                last = next;
                active = next;
                progress = 0f;
                Emit($"queue_start {Tag(next)}");
            }
        }

        // Wybieg napędu zanika wykładniczo.
        if (tailLevel > 0f)
        {
            tailLevel *= Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, tailTime));
            if (tailLevel < 0.01f) tailLevel = 0f;
        }

        UpdateStamina(Time.deltaTime);

        if (missTimer > 0f) missTimer -= Time.deltaTime;

        // Korba obraca się tylko o faktyczny postęp skoku – krótkie klepnięcie to tylko drgnięcie.
        float target = crankBase + 180f * progress;
        angle = Mathf.MoveTowards(angle, target, crankFollowSpeed * Time.deltaTime);
        transform.localRotation = Quaternion.Euler(angle, 0f, 0f);

        // Pedały pozostają poziomo niezależnie od obrotu korby.
        Quaternion level = Quaternion.Euler(-angle, 0f, 0f);
        if (leftPedal != null) leftPedal.localRotation = level;
        if (rightPedal != null) rightPedal.localRotation = level;
    }

    void Press(Foot foot)
    {
        bool unfinished = last != Foot.None && progress < 1f && !InGrace;

        if (unfinished)
        {
            if (foot == last)
            {
                // Kontynuacja niedokończonego skoku tą samą nogą – korba rusza z miejsca, w którym stanęła.
                active = foot;
                reversing = Foot.None;
                Emit($"resume {Tag(foot)} p={progress:0.00}");
            }
            else if (bufferTime > 0f && active == last && reversing == Foot.None
                     && (1f - progress) / StrokeRate(Speed) <= graceTime + bufferTime)
            {
                // Bufor: skok trwa i kończy się za chwilę – druga noga czeka w kolejce zamiast cofać korbę.
                queued = foot;
                Emit($"queue {Tag(foot)} p={progress:0.00}");
            }
            else
            {
                // Druga noga naciska pedał, który jest jeszcze z tyłu – cofa korbę, dopóki trzyma klawisz.
                active = Foot.None;
                reversing = foot;
                Miss(foot);
                Emit($"reverse {Tag(foot)} p={progress:0.00}");
            }
            return;
        }

        if (foot == last)
        {
            // Poprzedni skok skończony, ale ta noga właśnie go wykonała – trzeba zmienić nogę.
            Miss(foot);
            Emit($"miss_same_foot {Tag(foot)}");
            return;
        }

        // Dokończenie skoku z okna łaski i start nowego od miejsca, w którym stanęła korba.
        if (last != Foot.None)
        {
            crankBase += 180f;
            if (progress < 1f)
            {
                StartTail(Mathf.Max(ProfileAt(progress), tailFloor));
                Emit("complete_grace");
            }
        }
        queued = Foot.None;

        last = foot;
        active = foot;
        progress = 0f;
        Emit($"begin {Tag(foot)}");
    }

    Key KeyOf(Foot foot) => foot == Foot.Left ? leftKey : rightKey;
    static string Tag(Foot foot) => foot == Foot.Left ? "L" : "R";

    /// <summary>
    /// Stamina zależy od wydajności pedałowania przy obecnej kadencji: w zielonej strefie (>= sweetPowerFraction)
    /// się nie męczysz, a im bliżej środka strefy, tym szybciej odpoczywasz. Poza strefą ubywa ci sił,
    /// tym szybciej, im dalej od niej. Gdy nie pedałujesz, regenerujesz się powoli niezależnie od biegu.
    /// </summary>
    void UpdateStamina(float dt)
    {
        if (Pushing || active != Foot.None || reversing != Foot.None)
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

    void Miss(Foot foot)
    {
        missTimer = 0.25f;
        missFoot = foot;
    }
}
