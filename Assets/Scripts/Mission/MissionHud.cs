using UnityEngine;

/// <summary>
/// HUD misji (IMGUI): stan wieżyczki (Big Smoke), znaczniki i paski życia gangsterów, ostrzeżenie o ucieczce pociągu
/// oraz ekran końcowy ze statusem i statystykami.
/// </summary>
public class MissionHud : MonoBehaviour
{
    [SerializeField] MissionManager mission;
    [SerializeField] AutoTurret turret;
    [SerializeField] BikeController bike;

    static readonly Color Panel = new Color(0f, 0f, 0f, 0.55f);
    static readonly Color Gang = new Color(1f, 0.85f, 0.15f);
    static readonly Color Good = new Color(0.35f, 0.85f, 0.4f);
    static readonly Color Bad = new Color(1f, 0.35f, 0.3f);
    static readonly Color Dim = new Color(0.65f, 0.65f, 0.65f);

    GUIStyle text;
    Camera cam;

    void Awake()
    {
        if (mission == null) mission = FindFirstObjectByType<MissionManager>();
        if (turret == null) turret = FindFirstObjectByType<AutoTurret>();
        if (bike == null) bike = FindFirstObjectByType<BikeController>();
    }

    void OnGUI()
    {
        if (mission == null) return;
        text ??= new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow, richText = false };
        if (cam == null && bike != null) cam = bike.GetComponentInChildren<Camera>();

        float u = Mathf.Max(16f, Screen.height / 32f);
        if (mission.CurrentPhase == MissionManager.Phase.Running)
        {
            DrawMarkers(u);
            DrawTurretPanel(u);
            DrawWarning(u);
        }
        else DrawEndScreen(u);
    }

    // ------------------------------------------------------------ wieżyczka

    void DrawTurretPanel(float u)
    {
        if (turret == null) return;
        float w = u * 15f, h = u * 7.9f;
        var r = new Rect(Screen.width - w - u, u * 2.8f, w, h);   // pod paskiem pociągu u góry ekranu
        Fill(r, Panel);
        Frame(r, 1f, new Color(1f, 1f, 1f, 0.2f));

        Color c = StateColor(turret.State);
        Label(new Rect(r.x + u * 0.6f, r.y + u * 0.2f, w, u * 0.9f), "BIG SMOKE", u * 0.7f, Dim, TextAnchor.MiddleLeft);
        Label(new Rect(r.x + u * 0.6f, r.y + u * 1.0f, w - u, u * 1.2f), turret.StatusText, u * 0.95f, c, TextAnchor.MiddleLeft, FontStyle.Bold);

        // amunicja / przeładowanie / namierzanie
        var bar = new Rect(r.x + u * 0.6f, r.y + u * 2.5f, w - u * 1.2f, u * 0.6f);
        Fill(bar, new Color(1f, 1f, 1f, 0.08f));
        if (turret.Reloading)
            Fill(new Rect(bar.x, bar.y, bar.width * turret.ReloadProgress, bar.height), new Color(0.95f, 0.6f, 0.15f));
        else if (turret.State == AutoTurret.TurretState.Acquiring)
            Fill(new Rect(bar.x, bar.y, bar.width * turret.AcquireProgress, bar.height), Gang);
        else
            Fill(new Rect(bar.x, bar.y, bar.width * turret.Ammo / Mathf.Max(1f, turret.Magazine), bar.height), Good);
        Frame(bar, 1f, new Color(1f, 1f, 1f, 0.2f));
        string barLabel = turret.Reloading ? "przeładowanie" : turret.State == AutoTurret.TurretState.Acquiring ? "namierzanie" : $"amunicja {turret.Ammo}/{turret.Magazine}";
        Label(new Rect(bar.x, bar.yMax + u * 0.05f, bar.width, u * 0.8f), barLabel, u * 0.65f, Dim, TextAnchor.MiddleLeft);

        // stabilność jazdy: im wyższa, tym szybciej i celniej Big Smoke namierza
        float st = turret.Stability;
        Color stColor = st > 0.7f ? Good : st > 0.4f ? Gang : Bad;
        var sbar = new Rect(r.x + u * 0.6f, r.y + u * 4.5f, w - u * 1.2f, u * 0.6f);
        Fill(sbar, new Color(1f, 1f, 1f, 0.08f));
        Fill(new Rect(sbar.x, sbar.y, sbar.width * st, sbar.height), stColor);
        Frame(sbar, 1f, new Color(1f, 1f, 1f, 0.2f));
        Label(new Rect(sbar.x, sbar.yMax + u * 0.05f, sbar.width, u * 0.8f), $"stabilność jazdy {st * 100f:0}%", u * 0.65f, st > 0.7f ? Dim : stColor, TextAnchor.MiddleLeft);

        // gangsterzy: kwadraty (żywy = żółty, martwy = ciemny)
        Label(new Rect(r.x + u * 0.6f, r.y + u * 6.2f, u * 6f, u * 1.2f), "GANGSTERZY", u * 0.7f, Dim, TextAnchor.MiddleLeft);
        int total = Mathf.Max(1, mission.TotalGangsters);
        for (int i = 0; i < total; i++)
        {
            var box = new Rect(r.xMax - u * 0.6f - (total - i) * u * 1.2f, r.y + u * 6.35f, u * 0.95f, u * 0.95f);
            bool alive = i < mission.AliveGangsters;
            Fill(box, alive ? Gang : new Color(0.25f, 0.25f, 0.25f));
            Frame(box, 1f, new Color(1f, 1f, 1f, 0.3f));
        }
    }

    static Color StateColor(AutoTurret.TurretState s)
    {
        switch (s)
        {
            case AutoTurret.TurretState.Firing: return Bad;
            case AutoTurret.TurretState.Acquiring: return Gang;
            case AutoTurret.TurretState.Reloading: return new Color(0.95f, 0.6f, 0.15f);
            case AutoTurret.TurretState.Blocked: return new Color(0.7f, 0.7f, 0.95f);
            case AutoTurret.TurretState.OutOfRange: return Dim;
            default: return Color.white;
        }
    }

    // ------------------------------------------------------------ znaczniki gangsterów

    void DrawMarkers(float u)
    {
        if (cam == null || turret == null) return;
        foreach (var g in Gangster.All)
        {
            if (!g.Alive) continue;
            Vector3 world = g.HeadPosition + Vector3.up * 0.45f;
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0.5f) continue;
            float dist = Vector3.Distance(turret.Muzzle, g.AimPosition);
            if (dist > 260f) continue;

            float x = sp.x, y = Screen.height - sp.y;
            float scale = Mathf.Clamp(35f / Mathf.Max(8f, dist), 0.55f, 1.6f);
            bool isTarget = turret.Target == g;
            bool inRange = dist <= turret.Range;

            Color tint = isTarget ? StateColor(turret.State) : (inRange ? Gang : Dim);
            float w = u * 3.2f * scale, h = u * 0.4f * scale;
            var bar = new Rect(x - w * 0.5f, y - h, w, h);
            Fill(bar, Panel);
            Fill(new Rect(bar.x, bar.y, bar.width * g.Health01, bar.height), Gang);
            Frame(bar, 1f, tint);
            Label(new Rect(x - w, y - h - u * 0.9f * scale, w * 2f, u * 0.8f * scale), $"{dist:0} m", u * 0.65f * scale, tint, TextAnchor.MiddleCenter);

            if (isTarget)
            {
                // celownik: narożniki wokół tułowia
                Vector3 chest = cam.WorldToScreenPoint(g.AimPosition);
                if (chest.z > 0.5f)
                {
                    float cx = chest.x, cy = Screen.height - chest.y, s = u * 1.8f * scale, t = 2f, l = s * 0.45f;
                    DrawCorners(new Rect(cx - s, cy - s, s * 2f, s * 2f), l, t, tint);
                }
            }
        }
    }

    static void DrawCorners(Rect r, float len, float t, Color c)
    {
        Fill(new Rect(r.x, r.y, len, t), c); Fill(new Rect(r.x, r.y, t, len), c);
        Fill(new Rect(r.xMax - len, r.y, len, t), c); Fill(new Rect(r.xMax - t, r.y, t, len), c);
        Fill(new Rect(r.x, r.yMax - t, len, t), c); Fill(new Rect(r.x, r.yMax - len, t, len), c);
        Fill(new Rect(r.xMax - len, r.yMax - t, len, t), c); Fill(new Rect(r.xMax - t, r.yMax - len, t, len), c);
    }

    // ------------------------------------------------------------ ostrzeżenie i napisy

    void DrawWarning(float u)
    {
        if (mission.CurrentPhase != MissionManager.Phase.Running) return;
        float d = mission.TrainDistance;
        if (d < mission.WarnDistance) return;

        bool critical = d >= mission.LoseDistance;
        string msg = critical ? $"POCIĄG UCIEKA! DOGOŃ GO – {mission.LoseCountdown:0.0} s" : $"Pociąg się oddala ({d:0} m) – dociśnij!";
        float pulse = critical ? 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 7f)) : 1f;
        Color c = critical ? new Color(Bad.r, Bad.g, Bad.b, pulse) : Gang;
        Label(new Rect(Screen.width * 0.5f - 500f, u * 3.2f, 1000f, u * 1.6f), msg, u * (critical ? 1.5f : 1.1f), c, TextAnchor.MiddleCenter, FontStyle.Bold);
    }

    // ------------------------------------------------------------ ekran końcowy

    void DrawEndScreen(float u)
    {
        bool won = mission.CurrentPhase == MissionManager.Phase.Won;
        Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.7f));

        float cx = Screen.width * 0.5f;
        float y = Screen.height * 0.14f;
        Label(new Rect(cx - 600f, y, 1200f, u * 3f), won ? "MISJA ZALICZONA" : "MISJA NIEUDANA", u * 3f,
            won ? Good : Bad, TextAnchor.MiddleCenter, FontStyle.Bold);
        y += u * 3.4f;
        string sub = won ? "Wszyscy gangsterzy pokonani." : mission.ReasonText;
        Label(new Rect(cx - 600f, y, 1200f, u * 1.4f), sub, u * 1.2f, Color.white, TextAnchor.MiddleCenter);
        y += u * 2.4f;

        // tabela statystyk
        float colW = u * 12f;
        float x0 = cx - colW;
        int rows = 0;
        void Row(string k, string v)
        {
            float ry = y + rows * u * 1.35f;
            Label(new Rect(x0, ry, colW, u * 1.2f), k, u * 0.95f, Dim, TextAnchor.MiddleLeft);
            Label(new Rect(x0 + colW, ry, colW, u * 1.2f), v, u * 0.95f, Color.white, TextAnchor.MiddleRight, FontStyle.Bold);
            rows++;
        }
        Row("Podejście", $"#{mission.Attempt}");
        Row("Czas misji", Format(mission.MissionTime));
        Row("Pokonani gangsterzy", $"{mission.TotalGangsters - mission.AliveGangsters} / {mission.TotalGangsters}");
        for (int i = 0; i < mission.KillTimes.Count; i++)
            Row($"   zabity #{i + 1}", Format(mission.KillTimes[i]));
        Row("Strzały / trafienia", $"{mission.ShotsFired} / {mission.Hits}  ({mission.Accuracy * 100f:0}%)");
        if (mission.HasGun)
        {
            Row("   wieżyczka", $"{mission.TurretShots} / {mission.TurretHits}");
            Row("   broń ręczna", $"{mission.GunShots} / {mission.GunHits}");
        }
        Row("Przejechany dystans", $"{mission.DistanceRidden:0} m");
        Row("Maksymalna prędkość", $"{mission.MaxSpeedKmh:0.0} km/h");
        Row("Upadki", mission.Falls.ToString());
        Row("Najbliżej pociągu", mission.ClosestApproach < 1e6f ? $"{mission.ClosestApproach:0.0} m" : "–");
        if (won && mission.BestTime > 0f)
            Row(mission.NewRecord ? "NOWY REKORD" : "Rekord", Format(mission.BestTime));

        float py = y + rows * u * 1.35f + u * 1.2f;
        Label(new Rect(cx - 400f, py, 800f, u * 1.6f), "Naciśnij R, aby spróbować ponownie", u * 1.3f, Gang, TextAnchor.MiddleCenter, FontStyle.Bold);
    }

    static string Format(float seconds)
    {
        int m = (int)(seconds / 60f);
        return $"{m}:{seconds - m * 60f:00.0}";
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
