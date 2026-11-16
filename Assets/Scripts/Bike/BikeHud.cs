using UnityEngine;

/// <summary>
/// HUD (IMGUI): prędkość i kierownica w rogu, na dole panele biegów (lewy 1–3, prawy 1–7) ze słupkami
/// "ile da pedałowanie w tym biegu przy obecnej prędkości". Wskaźniki pedałowania (rytm, zmęczenie)
/// są po bokach ekranu, w wizji peryferyjnej – patrz RhythmHud.
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
    MissionManager mission;

    void OnGUI()
    {
        if (bike == null) return;
        text ??= new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow };

        float u = Mathf.Max(16f, Screen.height / 32f);
        DrawTopLeft(u);
        if (train == null) train = Object.FindFirstObjectByType<TrainController>();
        if (mission == null) mission = MissionManager.Instance;
        if (mission != null && mission.CurrentPhase != MissionManager.Phase.Running) return;   // ekran końcowy misji przejmuje HUD
        if (train != null)
            Label(new Rect(Screen.width * 0.5f - 300f, u, 600f, u * 1.4f),
                $"POCIĄG  {train.SpeedKmh:0} km/h   •   {train.DistanceTo(bike.transform.position):0} m od Ciebie   •   do końca mostu {(mission != null ? mission.DistanceToBridgeEnd : train.RemainingDistance):0} m",
                u * 0.9f, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
        if (pedals != null) DrawBottom(u);
        if (bike.Fallen) DrawFallen(u);

        Label(new Rect(u * 2.3f, Screen.height - u * 2.6f, Screen.width - u * 4.6f, u * 1.2f),
            "A / D – pedałuj w takt (klikaj naprzemiennie, gdy pasek z boku zejdzie do środka – cyjan = idealnie)  |  LPM na uchwycie – kierowanie  |  S – cofanie (na postoju)  |  po upadku: przytrzymaj Spację  |  R – restart misji",
            u * 0.7f, Dim, TextAnchor.MiddleLeft);
        Label(new Rect(u * 2.3f, Screen.height - u * 1.5f, Screen.width - u * 4.6f, u * 1.2f),
            "Na trzymanym uchwycie:  Spacja – hamulec (lewy = przód, prawy = tył)  |  Shift / Ctrl – bieg wyżej / niżej (lewy 1–3, prawy 1–7)",
            u * 0.7f, Dim, TextAnchor.MiddleLeft);
    }

    // ------------------------------------------------------------ lewy górny róg

    void DrawTopLeft(float u)
    {
        float y = u;
        Label(new Rect(u * 2.3f, y, 600f, u * 1.6f), $"{bike.SpeedKmh:0.0} km/h", u * 1.4f, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
        y += u * 1.7f;

        if (steering != null)
        {
            string state = steering.IsGrabbed ? (steering.LeftHeld ? "lewy uchwyt" : "prawy uchwyt") : "puszczona";
            Label(new Rect(u * 2.3f, y, 600f, u * 1.2f), $"Kierownica: {steering.Angle:+0;-0;0}° ({state})", u * 0.85f, Color.white, TextAnchor.MiddleLeft);
            y += u * 1.3f;
        }

        if (bike.FrontBrake || bike.RearBrake)
        {
            string s = bike.FrontBrake && bike.RearBrake ? "HAMULEC: przedni + tylny"
                     : bike.FrontBrake ? "HAMULEC: przedni (lewy)" : "HAMULEC: tylny (prawy)";
            Label(new Rect(u * 2.3f, y, 600f, u * 1.2f), s, u * 0.85f, new Color(1f, 0.4f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
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

        var r = new Rect(u * 2.3f, y + u * 0.15f, u * 9f, u * 0.7f);
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
        float gap = u * 0.6f;
        float bottom = Screen.height - u * 3.4f;
        float v = bike.Speed;

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
        var frontRect = new Rect(cx - gap - frontW, bottom - panelH, frontW, panelH);
        var rearRect = new Rect(cx + gap, bottom - panelH, rearW, panelH);

        DrawGearPanel(frontRect, "LEWA · przód", steering != null && steering.LeftHeld, pedals.FrontIndex, front, u);
        DrawGearPanel(rearRect, "PRAWA · tył", steering != null && steering.RightHeld, pedals.RearIndex, rear, u);
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
