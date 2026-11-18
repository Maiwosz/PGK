using UnityEngine;

/// <summary>
/// HUD (IMGUI): prędkość, kierownica, hamulec i teksty pomocy. Wskaźniki rytmu, biegów i zmęczenia
/// są na krawędziach ekranu, w wizji peryferyjnej – patrz RhythmHud.
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
        if (bike.Fallen) DrawFallen(u);

        Label(new Rect(u * 2.3f, Screen.height - u * 3.1f, 600f, u * 1.0f), "F1 – sterowanie i zasady", u * 0.7f, Dim, TextAnchor.MiddleLeft);
    }

    // ------------------------------------------------------------ lewy górny róg

    void DrawTopLeft(float u)
    {
        float y = u;
        Label(new Rect(u * 2.3f, y, 600f, u * 1.6f), $"{bike.SpeedKmh:0.0} km/h", u * 1.4f, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
        y += u * 1.7f;

        if (steering != null)
        {
            string state = steering.IsGrabbed ? "trzymana" : "puszczona";
            Label(new Rect(u * 2.3f, y, 600f, u * 1.2f), $"Kierownica: {steering.Angle:+0;-0;0}° ({state})", u * 0.85f, Color.white, TextAnchor.MiddleLeft);
            y += u * 1.3f;
        }

        if (bike.Braking)
        {
            Label(new Rect(u * 2.3f, y, 600f, u * 1.2f), "HAMULEC", u * 0.85f, new Color(1f, 0.4f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
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
