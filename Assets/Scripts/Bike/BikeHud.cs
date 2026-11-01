using UnityEngine;

/// <summary>
/// HUD (IMGUI): prędkość i kierownica w rogu, na dole klawisze A/D ze skokiem pedału,
/// po bokach panele biegów (lewy 1–3, prawy 1–7) ze słupkami "ile da pedałowanie w tym biegu
/// przy obecnej prędkości" oraz pasek kadencji ze strefą, w której rowerzysta oddaje najwięcej mocy.
/// </summary>
public class BikeHud : MonoBehaviour
{
    [SerializeField] BikeController bike;
    [SerializeField] PedalDrive pedals;
    [SerializeField] HandlebarSteering steering;

    static readonly Color Panel = new Color(0f, 0f, 0f, 0.55f);
    static readonly Color Gold = new Color(1f, 0.85f, 0.2f);
    static readonly Color Green = new Color(0.35f, 0.85f, 0.4f);
    static readonly Color Dim = new Color(0.55f, 0.55f, 0.55f);

    GUIStyle text;
    TrainController train;

    void OnGUI()
    {
        if (bike == null) return;
        text ??= new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow };

        float u = Mathf.Max(16f, Screen.height / 32f);
        DrawTopLeft(u);
        if (train == null) train = Object.FindFirstObjectByType<TrainController>();
        if (train != null)
            Label(new Rect(Screen.width * 0.5f - 300f, u, 600f, u * 1.4f),
                $"POCIĄG  {train.SpeedKmh:0} km/h   •   {train.DistanceTo(bike.transform.position):0} m od Ciebie   •   do mostu {train.RemainingDistance:0} m",
                u * 0.9f, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
        if (pedals != null) DrawBottom(u);
        if (bike.Fallen) DrawFallen(u);

        Label(new Rect(u, Screen.height - u * 2.6f, Screen.width - u * 2f, u * 1.2f),
            "A / D – pedałuj (trzymaj do końca skoku, naprzemiennie)  |  LPM na uchwycie – kierowanie  |  S – cofanie (na postoju)  |  po upadku: przytrzymaj Spację  |  R – reset",
            u * 0.7f, Dim, TextAnchor.MiddleLeft);
        Label(new Rect(u, Screen.height - u * 1.5f, Screen.width - u * 2f, u * 1.2f),
            "Na trzymanym uchwycie:  Spacja – hamulec (lewy = przód, prawy = tył)  |  Shift / Ctrl – bieg wyżej / niżej (lewy 1–3, prawy 1–7)",
            u * 0.7f, Dim, TextAnchor.MiddleLeft);
    }

    // ------------------------------------------------------------ lewy górny róg

    void DrawTopLeft(float u)
    {
        float y = u;
        Label(new Rect(u, y, 600f, u * 1.6f), $"{bike.SpeedKmh:0.0} km/h", u * 1.4f, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
        y += u * 1.7f;

        if (steering != null)
        {
            string state = steering.IsGrabbed ? (steering.LeftHeld ? "lewy uchwyt" : "prawy uchwyt") : "puszczona";
            Label(new Rect(u, y, 600f, u * 1.2f), $"Kierownica: {steering.Angle:+0;-0;0}° ({state})", u * 0.85f, Color.white, TextAnchor.MiddleLeft);
            y += u * 1.3f;
        }

        if (bike.FrontBrake || bike.RearBrake)
        {
            string s = bike.FrontBrake && bike.RearBrake ? "HAMULEC: przedni + tylny"
                     : bike.FrontBrake ? "HAMULEC: przedni (lewy)" : "HAMULEC: tylny (prawy)";
            Label(new Rect(u, y, 600f, u * 1.2f), s, u * 0.85f, new Color(1f, 0.4f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
            y += u * 1.3f;
        }

        DrawGrip(u, y);
    }

    /// <summary>Pasek obciążenia przyczepności (zakręt + hamowanie); pojawia się, gdy robi się ślisko.</summary>
    void DrawGrip(float u, float y)
    {
        if (bike.Fallen) return;
        float load = bike.GripLoad;
        float risk = bike.FallRisk;
        if (load < 0.6f && risk < 0.02f) return;

        var r = new Rect(u, y + u * 0.15f, u * 9f, u * 0.7f);
        Fill(r, Panel);
        Color c = load < 0.8f ? Green : load < 1f ? Gold : new Color(1f, 0.3f, 0.25f);
        Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(load / 1.25f), r.height), c);
        Fill(new Rect(r.x + r.width * 0.8f - 1f, r.y - 2f, 2f, r.height + 4f), Color.white);   // granica poślizgu
        Frame(r, 1f, Dim);

        string label = risk > 0.02f ? $"{bike.RiskLabel}! {risk * 100f:0}%" : "przyczepność";
        Label(new Rect(r.xMax + u * 0.5f, y, 600f, u * 1.0f), label, u * 0.8f,
            risk > 0.02f ? new Color(1f, 0.4f, 0.4f) : Dim, TextAnchor.MiddleLeft, risk > 0.02f ? FontStyle.Bold : FontStyle.Normal);
    }

    /// <summary>Ekran leżenia: opis upadku i pasek wstawania (przytrzymaj Spację).</summary>
    void DrawFallen(float u)
    {
        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.33f;

        Label(new Rect(cx - 400f, cy - u * 2.2f, 800f, u * 2.4f), "UPADEK", u * 2.2f,
            new Color(1f, 0.35f, 0.3f), TextAnchor.MiddleCenter, FontStyle.Bold);
        Label(new Rect(cx - 400f, cy, 800f, u * 1.2f), bike.FallText, u * 0.9f, Color.white, TextAnchor.MiddleCenter);

        bool ready = bike.CanStandUp;
        string prompt = ready ? "Przytrzymaj SPACJĘ, aby wstać"
                              : bike.Speed > 0.5f ? "Ślizgasz się…" : "Leżysz…";
        Label(new Rect(cx - 400f, cy + u * 1.5f, 800f, u * 1.2f), prompt, u * 1.0f,
            ready ? Gold : Dim, TextAnchor.MiddleCenter, FontStyle.Bold);

        var bar = new Rect(cx - u * 6f, cy + u * 3f, u * 12f, u * 0.8f);
        Fill(bar, Panel);
        Fill(new Rect(bar.x, bar.y, bar.width * bike.StandProgress, bar.height), Green);
        Frame(bar, 1f, Dim);
    }

    // ------------------------------------------------------------ dół ekranu

    void DrawBottom(float u)
    {
        float cx = Screen.width * 0.5f;
        float box = u * 3.4f;
        float gap = u * 0.5f;
        float bottom = Screen.height - u * 3.4f;
        float v = bike.Speed;

        var a = new Rect(cx - box - gap * 0.5f, bottom - box, box, box);
        var d = new Rect(cx + gap * 0.5f, bottom - box, box, box);
        DrawKey(a, "A", PedalDrive.Foot.Left, u);
        DrawKey(d, "D", PedalDrive.Foot.Right, u);

        // Ile da pedałowanie w każdym z biegów przy obecnej prędkości (przyspieszenie od napędu).
        int fc = pedals.FrontGearCount, rc = pedals.RearGearCount;
        var front = new float[fc];
        var rear = new float[rc];
        float max = 1e-4f;
        for (int f = 0; f < fc; f++)
            for (int r = 0; r < rc; r++)
                max = Mathf.Max(max, pedals.DriveAccelerationAt(pedals.RatioAt(f, r), v));
        for (int f = 0; f < fc; f++)
            front[f] = pedals.DriveAccelerationAt(pedals.RatioAt(f, pedals.RearIndex), v) / max;
        for (int r = 0; r < rc; r++)
            rear[r] = pedals.DriveAccelerationAt(pedals.RatioAt(pedals.FrontIndex, r), v) / max;

        float panelH = u * 4.2f;
        float frontW = PanelWidth(u, fc);
        float rearW = PanelWidth(u, rc);
        var frontRect = new Rect(a.x - u * 1.2f - frontW, bottom - panelH, frontW, panelH);
        var rearRect = new Rect(d.xMax + u * 1.2f, bottom - panelH, rearW, panelH);

        DrawStamina(new Rect(frontRect.x - u * 0.5f - u * 1.1f, frontRect.y, u * 1.1f, panelH), u);

        DrawGearPanel(frontRect, "LEWA · przód", steering != null && steering.LeftHeld, pedals.FrontIndex, front, u);
        DrawGearPanel(rearRect, "PRAWA · tył", steering != null && steering.RightHeld, pedals.RearIndex, rear, u);

        DrawCadence(new Rect(a.x, a.y, d.xMax - a.x, box), u);
    }

    /// <summary>
    /// Pionowy pasek staminy po lewej od paneli biegów. Strzałka pod nim pokazuje, czy siły ubywają, czy wracają,
    /// a procent obok – ile aktualnie tracisz na szybkości pedałowania przez zmęczenie.
    /// </summary>
    void DrawStamina(Rect r, float u)
    {
        float s = pedals.Stamina;
        float factor = pedals.PedalSpeedFactor;
        float trend = pedals.StaminaTrend;

        Color level = s > 0.5f ? Green : s > 0.25f ? new Color(0.95f, 0.65f, 0.15f) : new Color(0.95f, 0.3f, 0.25f);

        Label(new Rect(r.x - u, r.y - u * 1.0f, r.width + u * 2f, u * 0.9f), "SIŁY", u * 0.7f, Dim, TextAnchor.MiddleCenter);

        Fill(r, Panel);
        float h = (r.height - 6f) * s;
        Fill(new Rect(r.x + 3f, r.yMax - 3f - h, r.width - 6f, h), level);
        Frame(r, factor < 0.995f ? 2f : 1f, factor < 0.995f ? level : new Color(1f, 1f, 1f, 0.2f));

        string arrow = trend > 0.002f ? "▲" : trend < -0.002f ? "▼" : "–";
        Color arrowColor = trend > 0.002f ? Green : trend < -0.002f ? new Color(0.95f, 0.4f, 0.3f) : Dim;
        Label(new Rect(r.x - u, r.yMax + u * 0.05f, r.width + u * 2f, u * 0.9f), arrow, u * 0.8f, arrowColor, TextAnchor.MiddleCenter);

        if (factor < 0.995f)
            Label(new Rect(r.x - u * 2f, r.yMax - u * 1.1f, u * 1.8f, u * 1.0f), $"−{(1f - factor) * 100f:0}%",
                u * 0.7f, level, TextAnchor.MiddleRight, FontStyle.Bold);
    }

    static float PanelWidth(float u, int count) => u * (1f + count * 1.3f + (count - 1) * 0.35f);

    void DrawGearPanel(Rect r, string title, bool held, int current, float[] values, float u)
    {
        Fill(r, Panel);
        Frame(r, held ? 2f : 1f, held ? Gold : new Color(1f, 1f, 1f, 0.2f));

        // Tytuł; gdy ręka jest na tym uchwycie, zamienia się w podpowiedź klawiszy.
        Label(new Rect(r.x, r.y + u * 0.15f, r.width, u * 0.9f),
            held ? "Shift ▲   Ctrl ▼" : title, u * 0.7f, held ? Gold : Dim, TextAnchor.MiddleCenter, held ? FontStyle.Bold : FontStyle.Normal);

        float pipW = u * 1.3f, pipGap = u * 0.35f;
        float areaH = u * 2.0f;
        float top = r.y + u * 1.1f;
        float baseY = top + areaH;
        float x = r.x + u * 0.5f;

        for (int i = 0; i < values.Length; i++)
        {
            Fill(new Rect(x, top, pipW, areaH), new Color(1f, 1f, 1f, 0.07f));

            // Sześcian zwiększa kontrast: różnice między biegami są wyraźniejsze, gdy wszystkie są "niezłe".
            float val = Mathf.Pow(Mathf.Clamp01(values[i]), 3f);
            float h = Mathf.Lerp(u * 0.12f, areaH, val);
            Fill(new Rect(x, baseY - h, pipW, h), Color.Lerp(new Color(0.35f, 0.35f, 0.38f), Green, val));

            bool cur = i == current;
            if (cur) Frame(new Rect(x - 2f, top - 2f, pipW + 4f, areaH + 4f), 2f, Color.white);

            Label(new Rect(x - pipGap, baseY + u * 0.1f, pipW + pipGap * 2f, u * 0.9f),
                (i + 1).ToString(), u * 0.8f, cur ? Color.white : Dim, TextAnchor.MiddleCenter, cur ? FontStyle.Bold : FontStyle.Normal);

            x += pipW + pipGap;
        }
    }

    /// <summary>
    /// Pasek kadencji: lewo = ciężko (wolny obrót korby), prawo = lekko (kręcisz bardzo szybko).
    /// Zielona strefa to kadencja, przy której rowerzysta oddaje najwięcej mocy.
    /// </summary>
    void DrawCadence(Rect keys, float u)
    {
        float v = bike.Speed;
        float rpm = pedals.CadenceRpm(v);
        float maxRpm = pedals.MaxCadenceRpm;
        Vector2 sweet = pedals.SweetCadenceRpm;

        float w = u * 14f, h = u * 0.5f;
        var strip = new Rect(keys.center.x - w * 0.5f, keys.y - u * 1.1f - h, w, h);

        Fill(strip, Panel);
        Fill(new Rect(strip.x + strip.width * sweet.x / maxRpm, strip.y, strip.width * (sweet.y - sweet.x) / maxRpm, h),
            new Color(Green.r, Green.g, Green.b, 0.5f));
        Frame(strip, 1f, new Color(1f, 1f, 1f, 0.2f));

        float mx = strip.x + strip.width * Mathf.Clamp01(rpm / maxRpm);
        Fill(new Rect(mx - 2f, strip.y - u * 0.25f, 4f, h + u * 0.5f), Color.white);

        Label(new Rect(strip.x - u * 4f, strip.y - u * 0.3f, u * 3.7f, h + u * 0.6f), "ciężko", u * 0.7f, Dim, TextAnchor.MiddleRight);
        Label(new Rect(strip.xMax + u * 0.3f, strip.y - u * 0.3f, u * 3.7f, h + u * 0.6f), "lekko", u * 0.7f, Dim, TextAnchor.MiddleLeft);

        // Ile daje jeden skok przy tej prędkości i biegu.
        float stroke = pedals.StrokeTime(v);
        float gain = pedals.StrokeGain(v) * 3.6f;
        Label(new Rect(strip.x, strip.y - u * 1.45f, strip.width, u * 1.1f),
            $"{rpm:0} obr/min   ·   skok ≈ {stroke:0.00} s   ·   +{gain:0.0} km/h na skok",
            u * 0.8f, Color.white, TextAnchor.MiddleCenter);
    }

    void DrawKey(Rect rect, string letter, PedalDrive.Foot foot, float u)
    {
        bool expected = pedals.Expects(foot);
        bool active = pedals.ActiveFoot == foot;
        float miss = pedals.ReversingFoot == foot ? 1f
                   : pedals.MissFoot == foot ? pedals.MissFlash : 0f;

        Color bg = miss > 0f ? Color.Lerp(new Color(0.25f, 0.25f, 0.25f), Color.red, miss)
                 : expected ? new Color(0.9f, 0.75f, 0.2f)
                 : new Color(0.25f, 0.25f, 0.25f);
        Fill(rect, bg);

        // Pasek postępu skoku rośnie od dołu. Gdy skok jest niedokończony i klawisz puszczony,
        // pasek zostaje na swojej wysokości (pomarańczowy) – korba stoi w połowie.
        bool unfinishedHere = pedals.StrokeUnfinished && pedals.StrokeFoot == foot;
        if (active || unfinishedHere)
        {
            float p = pedals.Progress;
            Color fill = pedals.Pushing && active ? Green : new Color(0.95f, 0.55f, 0.15f);
            Fill(new Rect(rect.x, rect.yMax - rect.height * p, rect.width, rect.height * p), fill);
        }

        Label(rect, letter, u * 2f, expected || active ? Color.black : new Color(0.7f, 0.7f, 0.7f),
            TextAnchor.MiddleCenter, FontStyle.Bold);
    }

    // ------------------------------------------------------------ rysowanie

    static void Fill(Rect r, Color c)
    {
        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    static void Frame(Rect r, float t, Color c)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    void Label(Rect r, string s, float fontSize, Color c, TextAnchor anchor, FontStyle style = FontStyle.Normal)
    {
        text.fontSize = Mathf.RoundToInt(fontSize);
        text.normal.textColor = c;
        text.alignment = anchor;
        text.fontStyle = style;
        GUI.Label(r, s, text);
    }
}
