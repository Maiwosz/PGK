using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Tryb pedałowania w takt: A = lewa noga, D = prawa noga, naprzemiennie, jedno kliknięcie = jeden skok pedału
/// (pół obrotu korby). Tempo wyznacza rower: skok trwa tyle, ile kręci się korba przy aktualnej prędkości i biegu,
/// więc na ciężkim biegu klika się rzadko, a na lekkim często. Kolejne kliknięcie ma wypaść w chwili, gdy skok się kończy:
///  - idealne trafienie (wąskie okno) daje wyraźnie mocniejszy skok,
///  - trafienie w oknie łaski daje normalny skok,
///  - za wcześnie albo za późno poza oknem łaski – skok jest tym słabszy i kosztuje siły, im większy błąd,
///  - klik dużo za wcześnie (klepanie w klawisze) i zła noga są odrzucane i kosztują siły.
/// Za późne kliknięcie to przerwa w napędzie; po dłuższej przerwie rytm się zrywa i kolejny klik zaczyna nowy.
/// Własny HUD: RhythmHud. Aby zrezygnować z tego trybu, usuń ten komponent, RhythmHud i oba pliki.
/// </summary>
public class RhythmPedaling : PedalingMode
{
    [Header("Tempo")]
    [Tooltip("Mnożnik tempa rytmu: 1 = tempo wynikające z prędkości i biegu, mniej = rzadsze kliknięcia (np. 0.6 = o 40% wolniej). Nie zmienia siły napędu na skok, tylko jak długo trwa.")]
    [SerializeField, Range(0.2f, 1.5f)] float tempoScale = 0.6f;

    [Header("Okna trafienia")]
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
    [SerializeField, Range(0.2f, 3f)] float lateLimitFraction = 0.9f;

    [Header("Skutki")]
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

    float timeSinceEnd;            // ile sekund minęło od końca skoku (idealnej chwili następnego kliknięcia)

    public override float TempoScale => tempoScale;

    public override string ControlsHelp => "pedałuj w takt (naprzemiennie)";

    public override string MechanicsHelp =>
        "<b>Rytm.</b> Jedno kliknięcie to jeden skok pedału. Klikaj A i D naprzemiennie, gdy pasek z boku ekranu zejdzie do środka. " +
        "Kolor końca paska mówi, co da kliknięcie teraz: <color=#59F2FF>cyjan</color> – idealnie (mocniejszy skok), " +
        "<color=#59D94D>zielony</color> – dobrze, <color=#FF8A1F>pomarańczowy</color> – za wcześnie lub za późno (słabszy skok), " +
        "<color=#D91A1A>czerwony</color> – źle (klepanie i zła noga są odrzucane).";

    // ---- stan dla HUD

    /// <summary>Noga, która ma kliknąć następna; None, gdy rytm się nie zaczął (można dowolną).</summary>
    public PedalDrive.Foot ExpectedFoot => drive.Engaged ? PedalDrive.Opposite(drive.StrokeFoot) : PedalDrive.Foot.None;
    /// <summary>Za ile sekund wypada idealne kliknięcie (ujemne = już minęło). 0, gdy rytm się nie zaczął.</summary>
    public float TimeToBeat => !drive.Engaged ? 0f : drive.Progress < 1f ? (1f - drive.Progress) * StrokeTime : -timeSinceEnd;
    /// <summary>Ile trwa teraz jeden skok (s).</summary>
    public float StrokeTime => drive.StrokeTime(drive.Speed);
    /// <summary>Połowa okna idealnego trafienia (s) przy obecnym tempie.</summary>
    public float PerfectWindow => Mathf.Max(perfectWindow, perfectFraction * StrokeTime);
    /// <summary>Połowa okna łaski (s) przy obecnym tempie.</summary>
    public float GraceWindow => Mathf.Max(graceWindow, graceFraction * StrokeTime);
    /// <summary>Ułamek skoku przed jego końcem, od którego kliknięcie to klepanie (odrzucane).</summary>
    public float EarlyRejectFraction => earlyRejectFraction;

    public override void ResetMode() => timeSinceEnd = 0f;

    public override void Tick(float dt, Keyboard kb)
    {
        if (kb != null)
        {
            if (kb[drive.LeftKey].wasPressedThisFrame) Tap(PedalDrive.Foot.Left, TapOffset(kb, dt));
            if (kb[drive.RightKey].wasPressedThisFrame) Tap(PedalDrive.Foot.Right, TapOffset(kb, dt));
        }
        Advance(dt);
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
        return drive.Progress < 1f ? t - (1f - drive.Progress) * StrokeTime : timeSinceEnd + t;
    }

    void Tap(PedalDrive.Foot foot, float offset)
    {
        if (!drive.Engaged)
        {
            // Pierwszy klik (postój albo zerwany rytm): zawsze zalicza się jako normalny skok, bez oceny.
            Begin(foot, PedalDrive.Judgement.Start, goodMultiplier, 0f);
            return;
        }

        if (foot == drive.StrokeFoot)
        {
            // Ta noga właśnie pchnęła – trzeba zmienić nogę.
            Fail(foot, PedalDrive.Judgement.WrongFoot, TimingError(offset));
            return;
        }

        float stroke = StrokeTime;
        float err = TimingError(offset);
        float abs = Mathf.Abs(err);
        float perfect = PerfectWindow, grace = GraceWindow;

        if (abs <= perfect) { Begin(foot, PedalDrive.Judgement.Perfect, perfectMultiplier, err); return; }
        if (abs <= grace) { Begin(foot, PedalDrive.Judgement.Good, goodMultiplier, err); return; }

        if (err < 0f && -err > earlyRejectFraction * stroke)
        {
            // Dużo za wcześnie – klepanie w klawisze nie przyspiesza.
            Fail(foot, PedalDrive.Judgement.Mash, err);
            return;
        }

        // Poza oknem łaski: skok się zalicza, ale tym słabszy, im większy błąd; kosztuje też siły.
        float span = Mathf.Max(0.5f * stroke - grace, 0.05f);
        float severity = Mathf.Clamp01((abs - grace) / span);
        Begin(foot, err < 0f ? PedalDrive.Judgement.Early : PedalDrive.Judgement.Late, Mathf.Lerp(goodMultiplier, poorMultiplier, severity), err);
        drive.AddStamina(-missStamina * severity);
    }

    void Begin(PedalDrive.Foot foot, PedalDrive.Judgement judgement, float multiplier, float err)
    {
        // Wczesne kliknięcie ucina resztę poprzedniego skoku (jego niewykorzystana siła przepada), późne zostawia przerwę.
        drive.AdvanceCrank();
        drive.StrokeFoot = foot;
        drive.Engaged = true;
        drive.Progress = 0f;
        drive.Quality = multiplier;
        drive.ClearTail();
        drive.Pushing = true;
        timeSinceEnd = 0f;

        if (judgement == PedalDrive.Judgement.Perfect) drive.AddStamina(perfectStamina);
        bool hit = judgement == PedalDrive.Judgement.Perfect || judgement == PedalDrive.Judgement.Good || judgement == PedalDrive.Judgement.Start;
        drive.HitStreak = hit ? drive.HitStreak + 1 : 0;
        drive.Report(foot, judgement, err, multiplier);
    }

    void Fail(PedalDrive.Foot foot, PedalDrive.Judgement judgement, float err)
    {
        drive.AddStamina(-missStamina);
        drive.HitStreak = 0;
        drive.Report(foot, judgement, err, 0f);
    }

    /// <summary>Postęp skoku i odliczanie przerwy po jego końcu; zbyt długa przerwa zrywa rytm.</summary>
    void Advance(float dt)
    {
        if (!drive.Engaged) { drive.Pushing = false; return; }

        if (drive.Progress < 1f)
        {
            float rate = drive.StrokeRate(drive.Speed);
            float need = (1f - drive.Progress) / rate;
            drive.Pushing = true;
            if (dt < need)
            {
                drive.Progress += rate * dt;
            }
            else
            {
                // Skok skończył się w tej klatce; reszta klatki to już przerwa przed następnym kliknięciem.
                drive.Progress = 1f;
                timeSinceEnd = dt - need;
                drive.StartTail(drive.DefaultTailLevel);
                drive.Log("stroke_end");
            }
        }
        else
        {
            drive.Pushing = false;
            timeSinceEnd += dt;
            if (timeSinceEnd > Mathf.Max(lateLimit, lateLimitFraction * StrokeTime))
            {
                drive.Engaged = false;
                drive.HitStreak = 0;
                drive.Log("rhythm_lost");
            }
        }
    }
}
