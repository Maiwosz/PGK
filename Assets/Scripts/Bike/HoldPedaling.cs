using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tryb pedałowania na przytrzymanie (jak w pierwszej wersji gry): A / D naprzemiennie, klawisz trzeba trzymać do końca skoku
/// (tyle, ile kręci się korba przy aktualnej prędkości i biegu).
///  - puszczenie w ostatnich graceTime sekundach skoku nadal go zalicza,
///  - puszczenie wcześniej zostawia skok niedokończony: wolno go dokończyć tylko tą samą nogą,
///  - druga noga naciśnięta w ostatnich graceTime + bufferTime sekundach skoku czeka w kolejce i rusza skok bez przerwy,
///    wcześniej naciśnięta cofa korbę (pedał jest jeszcze z tyłu),
///  - ta sama noga dwa razy pod rząd to błąd.
/// Oceny (kolory błysków) zależą od przerwy między końcem skoku a startem następnego: bez przerwy – idealnie.
/// Własny HUD: HoldHud. Aby zrezygnować z tego trybu, usuń ten komponent, HoldHud i oba pliki.
/// </summary>
public class HoldPedaling : PedalingMode
{
    [Header("Tempo")]
    [Tooltip("Mnożnik tempa skoku: 1 = tempo wynikające z prędkości i biegu (jak w pierwszej wersji).")]
    [SerializeField, Range(0.2f, 1.5f)] float tempoScale = 1f;

    [Header("Zasady skoku")]
    [Tooltip("Okno łaski (s): puszczenie klawisza na tyle przed końcem skoku nadal go zalicza.")]
    [SerializeField] float graceTime = 0.12f;
    [Tooltip("Jak szybko druga noga cofa korbę niedokończonego skoku (1 = tak szybko, jak by szła do przodu).")]
    [SerializeField] float reverseSpeedFactor = 1f;
    [Tooltip("Bufor następnej nogi (s): druga noga naciśnięta tyle (plus okno łaski) przed końcem skoku czeka w kolejce zamiast cofać korbę. 0 = wyłączony.")]
    [SerializeField] float bufferTime = 0.12f;
    [Tooltip("Po tylu sekundach bezczynności (co najmniej) seria się zrywa i następne naciśnięcie zaczyna nową...")]
    [SerializeField] float lateLimit = 0.5f;
    [Tooltip("...albo po tej części długości skoku – liczy się większa wartość.")]
    [SerializeField, Range(0.2f, 3f)] float lateLimitFraction = 0.9f;

    [Header("Oceny i skutki")]
    [Tooltip("Następny skok rozpoczęty najwyżej tyle sekund po końcu poprzedniego (albo z bufora) to idealny łańcuch.")]
    [SerializeField] float chainWindow = 0.08f;
    [Tooltip("Przerwa do tylu sekund po końcu skoku to jeszcze dobre trafienie; dłuższa to spóźnienie.")]
    [SerializeField] float goodGap = 0.35f;
    [Tooltip("Ubytek sił za zły ruch (zła noga, cofanie korby); wcześniejsze puszczenie kosztuje połowę.")]
    [SerializeField] float missStamina = 0.04f;
    [Tooltip("Zysk sił za idealny łańcuch.")]
    [SerializeField] float perfectStamina = 0.006f;

    PedalDrive.Foot active;        // noga trzymająca teraz klawisz skoku
    PedalDrive.Foot reversing;     // noga cofająca korbę niedokończonego skoku
    PedalDrive.Foot queued;        // noga czekająca w kolejce na koniec bieżącego skoku
    float idle;                    // jak długo nikt nie trzyma klawisza
    float timeSinceEnd;            // ile sekund minęło od końca skoku

    public override float TempoScale => tempoScale;

    public override string ControlsHelp => "pedałuj: trzymaj klawisz do końca skoku (naprzemiennie)";

    public override string MechanicsHelp =>
        "<b>Przytrzymanie.</b> Naciśnij A albo D i trzymaj, aż pasek tej nogi urośnie do pełna – tyle trwa jeden skok pedału. " +
        "Puść w ostatniej chwili (okno łaski), a skok się zalicza; puszczony wcześniej zostaje niedokończony i dokończyć go może tylko ta sama noga. " +
        "Drugą nogę naciśnij tuż przed końcem skoku, a ruszy bez przerwy; naciśnięta za wcześnie cofa korbę. Ta sama noga dwa razy pod rząd to błąd. " +
        "Pasek jest długo czerwony (puszczenie teraz zepsuje skok), tuż przed końcem skoku robi się pomarańczowy, potem zielony (można puścić), a na końcu jest biały błysk; " +
        "błyski: <color=#59F2FF>cyjan</color> – bez przerwy, <color=#FF8A1F>pomarańczowy</color> – puszczone za wcześnie lub spóźnione, " +
        "<color=#D91A1A>czerwony</color> – zła noga.";

    // ---- stan dla HUD

    /// <summary>Noga, która teraz trzyma klawisz skoku (None, gdy nikt).</summary>
    public PedalDrive.Foot HoldingFoot => active;
    /// <summary>Noga cofająca korbę (zły ruch), None, gdy żadna.</summary>
    public PedalDrive.Foot ReversingFoot => reversing;
    /// <summary>Część skoku (0..1) z oknem łaski przy jego końcu.</summary>
    public float GraceFraction => graceTime / Mathf.Max(0.05f, drive.StrokeTime(drive.Speed));
    /// <summary>Część skoku (0..1) z oknem łaski i buforem następnej nogi.</summary>
    public float PressWindowFraction => (graceTime + bufferTime) / Mathf.Max(0.05f, drive.StrokeTime(drive.Speed));
    /// <summary>Skok rozpoczęty, ale niedokończony (korba stoi w połowie) – tylko ta sama noga może go dokończyć.</summary>
    public bool Unfinished => drive.StrokeFoot != PedalDrive.Foot.None && drive.Progress < 1f && !InGrace;
    /// <summary>Noga, którą można teraz pchnąć: po dokończonym skoku drugą, po niedokończonym tą samą.</summary>
    public PedalDrive.Foot ExpectedFoot => drive.StrokeFoot == PedalDrive.Foot.None ? PedalDrive.Foot.None
        : Unfinished ? drive.StrokeFoot : PedalDrive.Opposite(drive.StrokeFoot);
    /// <summary>Czas (Time.time) i noga ostatniego ukończonego skoku – do błysku na końcu paska.</summary>
    public float LastCompletedTime { get; private set; } = float.NegativeInfinity;
    public PedalDrive.Foot LastCompletedFoot { get; private set; }

    bool InGrace => drive.Progress < 1f && (1f - drive.Progress) / drive.StrokeRate(drive.Speed) <= graceTime;

    public override void ResetMode()
    {
        active = reversing = queued = PedalDrive.Foot.None;
        idle = timeSinceEnd = 0f;
        LastCompletedTime = float.NegativeInfinity;
    }

    public override void Tick(float dt, Keyboard kb)
    {
        if (kb != null)
        {
            if (kb[drive.LeftKey].wasPressedThisFrame) Press(PedalDrive.Foot.Left);
            if (kb[drive.RightKey].wasPressedThisFrame) Press(PedalDrive.Foot.Right);

            // Puszczenie klawisza tuż przed końcem skoku (okno łaski) dokańcza skok, wcześniejsze zostawia go niedokończonym.
            if (active != PedalDrive.Foot.None && !kb[drive.KeyOf(active)].isPressed)
            {
                if (InGrace)
                {
                    drive.StartTail(Mathf.Max(PedalDrive.ProfileAt(drive.Progress), drive.TailFloor));
                    drive.Progress = 1f;
                    timeSinceEnd = 0f;
                    Complete();
                    drive.Log("complete_grace");
                }
                else if (drive.Progress < 1f)
                {
                    ReleaseEarly();
                    // Zakolejkowana noga naciska pedał z tyłu, więc cofa korbę.
                    if (queued != PedalDrive.Foot.None)
                    {
                        var q = queued;
                        queued = PedalDrive.Foot.None;
                        if (kb[drive.KeyOf(q)].isPressed)
                        {
                            reversing = q;
                            Fail(q, PedalDrive.Judgement.WrongFoot, 1f);
                        }
                    }
                }
                active = PedalDrive.Foot.None;
            }

            // Zła noga puszczona (albo skok się już skończył) – przestaje cofać.
            if (reversing != PedalDrive.Foot.None && (!kb[drive.KeyOf(reversing)].isPressed || !Unfinished))
                reversing = PedalDrive.Foot.None;

            // Gdy zła noga nie cofa, a właściwa wciąż trzyma klawisz, skok rusza dalej sam.
            if (reversing == PedalDrive.Foot.None && active == PedalDrive.Foot.None && Unfinished && kb[drive.KeyOf(drive.StrokeFoot)].isPressed)
                active = drive.StrokeFoot;
        }
        else
        {
            active = reversing = PedalDrive.Foot.None;
        }

        drive.Pushing = false;
        float rate = drive.StrokeRate(drive.Speed);
        if (reversing != PedalDrive.Foot.None)
        {
            drive.Progress = Mathf.Max(0f, drive.Progress - rate * reverseSpeedFactor * dt);
        }
        else if (active != PedalDrive.Foot.None && drive.Progress < 1f)
        {
            float need = (1f - drive.Progress) / rate;
            drive.Progress = Mathf.Min(1f, drive.Progress + rate * dt);
            drive.Pushing = true;
            if (drive.Progress >= 1f)
            {
                timeSinceEnd = Mathf.Max(0f, dt - need);
                drive.StartTail(drive.DefaultTailLevel);
                Complete();
                drive.Log("stroke_end");
            }
        }
        else if (drive.StrokeFoot != PedalDrive.Foot.None && drive.Progress >= 1f)
        {
            timeSinceEnd += dt;
        }

        // Zakolejkowana noga startuje dokładnie w chwili, gdy bieżący skok się kończy – bez przerwy w napędzie.
        if (queued != PedalDrive.Foot.None && drive.Progress >= 1f)
        {
            var next = queued;
            queued = PedalDrive.Foot.None;
            if (kb != null && kb[drive.KeyOf(next)].isPressed)
            {
                Begin(next, PedalDrive.Judgement.Perfect, 0f);
                drive.Log("queue_start");
            }
        }

        // Długa bezczynność kończy serię: następne naciśnięcie zaczyna od nowa, bez oceny.
        bool held = active != PedalDrive.Foot.None || reversing != PedalDrive.Foot.None;
        if (drive.StrokeFoot != PedalDrive.Foot.None && !held)
        {
            idle += dt;
            if (idle > Mathf.Max(lateLimit, lateLimitFraction * drive.StrokeTime(drive.Speed)))
            {
                drive.StrokeFoot = PedalDrive.Foot.None;
                drive.Progress = 1f;
                queued = PedalDrive.Foot.None;
                drive.HitStreak = 0;
                drive.Log("rhythm_lost");
            }
        }
        else idle = 0f;

        drive.Engaged = drive.StrokeFoot != PedalDrive.Foot.None;
    }

    void Complete()
    {
        LastCompletedTime = Time.time;
        LastCompletedFoot = drive.StrokeFoot;
    }

    void Press(PedalDrive.Foot foot)
    {
        if (Unfinished)
        {
            if (foot == drive.StrokeFoot)
            {
                // Kontynuacja niedokończonego skoku tą samą nogą – korba rusza z miejsca, w którym stanęła.
                active = foot;
                reversing = PedalDrive.Foot.None;
                drive.Log($"resume {(foot == PedalDrive.Foot.Left ? "L" : "R")} p={drive.Progress:0.00}");
            }
            else if (bufferTime > 0f && active == drive.StrokeFoot && reversing == PedalDrive.Foot.None
                     && (1f - drive.Progress) / drive.StrokeRate(drive.Speed) <= graceTime + bufferTime)
            {
                // Bufor: skok kończy się za chwilę – druga noga czeka w kolejce zamiast cofać korbę.
                queued = foot;
                drive.Log($"queue {(foot == PedalDrive.Foot.Left ? "L" : "R")} p={drive.Progress:0.00}");
            }
            else
            {
                // Druga noga naciska pedał, który jest jeszcze z tyłu – cofa korbę, dopóki trzyma klawisz.
                active = PedalDrive.Foot.None;
                reversing = foot;
                Fail(foot, PedalDrive.Judgement.WrongFoot, 1f);
            }
            return;
        }

        if (drive.StrokeFoot == PedalDrive.Foot.None)
        {
            Begin(foot, PedalDrive.Judgement.Start, 0f);
            return;
        }

        if (foot == drive.StrokeFoot)
        {
            // Poprzedni skok skończony, ale ta noga właśnie go wykonała – trzeba zmienić nogę.
            Fail(foot, PedalDrive.Judgement.WrongFoot, 1f);
            return;
        }

        // Skok skończony albo w oknie łaski (kończymy go): oceniamy przerwę do startu następnego.
        float gap;
        if (drive.Progress < 1f)
        {
            gap = -(1f - drive.Progress) / drive.StrokeRate(drive.Speed);
            drive.StartTail(Mathf.Max(PedalDrive.ProfileAt(drive.Progress), drive.TailFloor));
        }
        else gap = timeSinceEnd;

        var j = gap <= chainWindow ? PedalDrive.Judgement.Perfect : gap <= goodGap ? PedalDrive.Judgement.Good : PedalDrive.Judgement.Late;
        Begin(foot, j, gap);
    }

    void Begin(PedalDrive.Foot foot, PedalDrive.Judgement judgement, float gap)
    {
        drive.AdvanceCrank();
        drive.StrokeFoot = foot;
        active = foot;
        reversing = PedalDrive.Foot.None;
        queued = PedalDrive.Foot.None;
        drive.Progress = 0f;
        drive.Quality = 1f;
        drive.ClearTail();
        drive.Pushing = true;
        drive.Engaged = true;
        timeSinceEnd = 0f;
        idle = 0f;

        if (judgement == PedalDrive.Judgement.Perfect) drive.AddStamina(perfectStamina);
        bool hit = judgement == PedalDrive.Judgement.Perfect || judgement == PedalDrive.Judgement.Good || judgement == PedalDrive.Judgement.Start;
        drive.HitStreak = hit ? drive.HitStreak + 1 : 0;
        drive.Report(foot, judgement, gap, 1f);
    }

    void ReleaseEarly()
    {
        drive.AddStamina(-missStamina * 0.5f);
        drive.HitStreak = 0;
        drive.Report(drive.StrokeFoot, PedalDrive.Judgement.Early, -(1f - drive.Progress) / drive.StrokeRate(drive.Speed), 0f);
        drive.Log($"release_early p={drive.Progress:0.00}");
    }

    void Fail(PedalDrive.Foot foot, PedalDrive.Judgement judgement, float severity)
    {
        drive.AddStamina(-missStamina * severity);
        drive.HitStreak = 0;
        drive.Report(foot, judgement, 0f, 0f);
    }
}
