using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Pomiary jazdy do strojenia: w Play mode samo zapisuje do Logs/Telemetry/ (obok folderu Assets):
///   session_*_samples.csv  – próbki 10 razy na sekundę (prędkość, bieg, kadencja, stamina, stan skoku...),
///   session_*_events.csv   – zdarzenia (początek/koniec skoku, błędy, zmiany biegów, markery),
///   session_*_summary.txt  – podsumowanie liczone po wyjściu z Play mode.
/// F9 wstawia marker w pliku zdarzeń (np. "tu było za ciężko").
/// Tworzy się automatycznie, gdy w scenie jest BikeController. Aby wyłączyć, ustaw Enabled = false.
/// </summary>
public class BikeTelemetry : MonoBehaviour
{
    public static bool Enabled = true;

    const float SampleInterval = 0.1f;
    const float RidingWindow = 1.0f;    // jak długo po ostatnim pchnięciu uznajemy, że "pedałujemy"

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    class GearStat
    {
        public float Time, RidingTime, PushTime, SpeedTime, EffTime, MaxSpeed;
    }

    BikeController bike;
    PedalDrive pedals;
    HandlebarSteering steering;

    StreamWriter samples, events;
    string basePath;
    float t0, nextSample, nextFlush, lastPushTime = float.NegativeInfinity;
    bool closed;
    System.DateTime startedAt;

    // Zbiorcze statystyki.
    float total, distance, maxSpeed, movingTime, movingSpeedTime;
    float ridingTime, pushTime, zoneTime, heavyTime, spinTime;
    float staminaMin = 1f, below50Time, drained, regenerated;
    readonly List<float> movingSpeeds = new List<float>();
    readonly Dictionary<int, GearStat> gears = new Dictionary<int, GearStat>();

    // Statystyki skoków.
    readonly Dictionary<string, int> counts = new Dictionary<string, int>();
    float strokeBeginT = -1f, lastCompleteT = -1f;
    float strokeDurSum, gapSum;
    int strokeDurN, gapN;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Enabled) return;
        if (FindFirstObjectByType<BikeController>() == null) return;
        if (FindFirstObjectByType<BikeTelemetry>() != null) return;
        new GameObject("BikeTelemetry").AddComponent<BikeTelemetry>();
    }

    void Start()
    {
        bike = FindFirstObjectByType<BikeController>();
        pedals = FindFirstObjectByType<PedalDrive>();
        steering = FindFirstObjectByType<HandlebarSteering>();
        if (bike == null || pedals == null)
        {
            enabled = false;
            return;
        }

        startedAt = System.DateTime.Now;
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Telemetry"));
        Directory.CreateDirectory(dir);
        basePath = Path.Combine(dir, "session_" + startedAt.ToString("yyyyMMdd_HHmmss", Inv));

        samples = new StreamWriter(basePath + "_samples.csv", false, new UTF8Encoding(false));
        events = new StreamWriter(basePath + "_events.csv", false, new UTF8Encoding(false));
        samples.WriteLine("t,speed_kmh,gear_f,gear_r,ratio,cadence_rpm,power_frac,stamina,stamina_trend,pedal_factor," +
                          "pushing,key_foot,reversing,progress,unfinished,brake_f,brake_r,steer_deg,x,z");
        events.WriteLine("t,event,speed_kmh,gear_f,gear_r,stamina");

        t0 = Time.time;
        pedals.Logged += OnPedalEvent;
        Log("session_start");
    }

    void Update()
    {
        if (closed || bike == null) return;

        float dt = Time.deltaTime;
        float t = Time.time - t0;
        float v = bike.Speed;
        float e = pedals.PowerFraction(v);
        float rpm = pedals.CadenceRpm(v);

        if (pedals.Pushing) lastPushTime = Time.time;
        bool riding = Time.time - lastPushTime < RidingWindow;

        // ---- zbiorcze
        total += dt;
        distance += v * dt;
        maxSpeed = Mathf.Max(maxSpeed, v);
        if (v > 0.5f)
        {
            movingTime += dt;
            movingSpeedTime += v * dt;
        }

        if (riding)
        {
            ridingTime += dt;
            if (pedals.Pushing) pushTime += dt;

            Vector2 sweet = pedals.SweetCadenceRpm;
            if (rpm < sweet.x) heavyTime += dt;
            else if (rpm > sweet.y) spinTime += dt;
            else zoneTime += dt;
        }

        float s = pedals.Stamina;
        staminaMin = Mathf.Min(staminaMin, s);
        if (s < 0.5f) below50Time += dt;
        float trend = pedals.StaminaTrend;
        if (trend < 0f) drained += -trend * dt; else regenerated += trend * dt;

        int key = pedals.FrontGear * 10 + pedals.RearGear;
        if (!gears.TryGetValue(key, out var g)) gears[key] = g = new GearStat();
        g.Time += dt;
        g.SpeedTime += v * dt;
        g.MaxSpeed = Mathf.Max(g.MaxSpeed, v);
        if (riding)
        {
            g.RidingTime += dt;
            g.EffTime += e * dt;
            if (pedals.Pushing) g.PushTime += dt;
        }

        // ---- marker
        var kb = Keyboard.current;
        if (kb != null && kb.f9Key.wasPressedThisFrame)
        {
            Log("MARKER");
            counts["MARKER"] = C("MARKER") + 1;
        }

        // ---- próbki
        if (t >= nextSample)
        {
            nextSample = t + SampleInterval;
            if (v > 0.5f) movingSpeeds.Add(v * 3.6f);

            Vector3 p = bike.transform.position;
            samples.WriteLine(string.Join(",",
                F(t, "0.00"), F(bike.SpeedKmh, "0.0"),
                pedals.FrontGear.ToString(Inv), pedals.RearGear.ToString(Inv),
                F(pedals.GearRatio, "0.00"), F(rpm, "0"), F(e, "0.00"),
                F(s, "0.000"), F(trend, "0.000"), F(pedals.PedalSpeedFactor, "0.00"),
                pedals.Pushing ? "1" : "0", ((int)pedals.ActiveFoot).ToString(Inv),
                ((int)pedals.ReversingFoot).ToString(Inv), F(pedals.Progress, "0.00"),
                pedals.StrokeUnfinished ? "1" : "0",
                bike.FrontBrake ? "1" : "0", bike.RearBrake ? "1" : "0",
                F(steering != null ? steering.Angle : 0f, "0"), F(p.x, "0.0"), F(p.z, "0.0")));
        }

        if (t >= nextFlush)
        {
            nextFlush = t + 3f;
            samples.Flush();
            events.Flush();
        }
    }

    void OnPedalEvent(string message)
    {
        Log(message);

        string tag = message.Split(' ')[0];
        counts[tag] = counts.TryGetValue(tag, out int n) ? n + 1 : 1;

        float t = Time.time - t0;
        if (tag == "begin")
        {
            strokeBeginT = t;
            if (lastCompleteT >= 0f && t - lastCompleteT < 2f)
            {
                gapSum += t - lastCompleteT;
                gapN++;
            }
        }
        else if (tag == "complete" || tag == "complete_grace")
        {
            if (strokeBeginT >= 0f)
            {
                strokeDurSum += t - strokeBeginT;
                strokeDurN++;
            }
            lastCompleteT = t;
        }
    }

    void Log(string message)
    {
        if (events == null || closed) return;
        float v = bike != null ? bike.SpeedKmh : 0f;
        events.WriteLine(string.Join(",",
            F(Time.time - t0, "0.00"), message.Replace(',', ';'), F(v, "0.0"),
            pedals.FrontGear.ToString(Inv), pedals.RearGear.ToString(Inv), F(pedals.Stamina, "0.00")));
    }

    void OnDisable() => Close();
    void OnApplicationQuit() => Close();

    void Close()
    {
        if (closed || samples == null) return;
        if (pedals != null) pedals.Logged -= OnPedalEvent;
        Log("session_end");
        closed = true;

        samples.Dispose();
        events.Dispose();
        File.WriteAllText(basePath + "_summary.txt", BuildSummary(), new UTF8Encoding(false));
        Debug.Log("[BikeTelemetry] Zapisano pomiary: " + basePath + "_*");
    }

    // ------------------------------------------------------------------ podsumowanie

    string BuildSummary()
    {
        var sb = new StringBuilder();
        void Line(string s = "") => sb.AppendLine(s);

        Line("BIKE TELEMETRY – podsumowanie sesji");
        Line($"start: {startedAt:yyyy-MM-dd HH:mm:ss}   czas: {total:0.0} s   dystans: {distance:0} m");
        Line();

        Line("== PRĘDKOŚĆ ==");
        Line($"maksymalna: {maxSpeed * 3.6f:0.0} km/h");
        Line($"średnia w ruchu (>1,8 km/h): {(movingTime > 0 ? movingSpeedTime / movingTime * 3.6f : 0f):0.0} km/h   (czas w ruchu: {movingTime:0.0} s)");
        if (movingSpeeds.Count > 0)
        {
            movingSpeeds.Sort();
            Line($"percentyle prędkości w ruchu: p50 {Pct(50):0.0}, p90 {Pct(90):0.0}, p99 {Pct(99):0.0} km/h");
        }
        Line();

        Line("== PEDAŁOWANIE ==");
        Line($"czas pedałowania (ostatnie pchnięcie <{RidingWindow:0.0} s temu): {ridingTime:0.0} s ({Share(ridingTime, total)})");
        Line($"napęd aktywny (siła przykładana do pedału): {pushTime:0.0} s = {Share(pushTime, ridingTime)} czasu pedałowania   <- 'duty'");
        Line($"kadencja podczas pedałowania:  w strefie {Share(zoneTime, ridingTime)}  |  za wolna ('ciężko') {Share(heavyTime, ridingTime)}  |  za szybka ('na pusto') {Share(spinTime, ridingTime)}");
        Line();
        Line($"skoki: rozpoczęte {C("begin")}, dokończone {C("complete")} + w oknie łaski {C("complete_grace")}, przerwane za wcześnie {C("release_early")}");
        Line($"       wznowione {C("resume")}, cofnięcia złą nogą {C("reverse")}, ta sama noga dwa razy {C("miss_same_foot")}");
        Line($"średni czas od naciśnięcia do końca skoku: {(strokeDurN > 0 ? strokeDurSum / strokeDurN : 0f):0.00} s   (n={strokeDurN})");
        Line($"średnia przerwa między skokami: {(gapN > 0 ? gapSum / gapN : 0f):0.00} s   (n={gapN})");
        Line($"zmiany biegów: przód {C("shift_front")} (zablokowane {C("shift_front_blocked")}), tył {C("shift_rear")} (zablokowane {C("shift_rear_blocked")})");
        Line($"markery F9: {C("MARKER")}");
        Line();

        Line("== STAMINA ==");
        Line($"minimum: {staminaMin:0.00}   czas poniżej 50%: {below50Time:0.0} s");
        Line($"łączny ubytek: {drained:0.00}   łączna regeneracja: {regenerated:0.00}");
        Line();

        Line("== BIEGI (przód/tył) ==");
        var keys = new List<int>(gears.Keys);
        keys.Sort();
        foreach (int k in keys)
        {
            var g = gears[k];
            if (g.Time < 0.5f) continue;
            Line($"P{k / 10}/T{k % 10}: łącznie {g.Time,5:0.0} s, pedałowanie {g.RidingTime,5:0.0} s, " +
                 $"śr. prędkość {(g.Time > 0 ? g.SpeedTime / g.Time * 3.6f : 0f),5:0.0} km/h, max {g.MaxSpeed * 3.6f,5:0.0} km/h, " +
                 $"śr. wydajność {(g.RidingTime > 0 ? g.EffTime / g.RidingTime : 0f):0.00}, duty {Share(g.PushTime, g.RidingTime)}");
        }
        return sb.ToString();
    }

    float Pct(int p) => movingSpeeds[Mathf.Clamp(Mathf.RoundToInt((movingSpeeds.Count - 1) * p / 100f), 0, movingSpeeds.Count - 1)];
    int C(string tag) => counts.TryGetValue(tag, out int n) ? n : 0;
    static string Share(float part, float whole) => whole > 0.001f ? $"{part / whole * 100f:0}%" : "–";
    static string F(float value, string format) => value.ToString(format, Inv);

    // ------------------------------------------------------------------ wskaźnik w grze

    void OnGUI()
    {
        if (closed || samples == null) return;
        int size = Mathf.Max(12, Screen.height / 45);
        var style = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = TextAnchor.UpperRight };
        style.normal.textColor = new Color(1f, 0.35f, 0.3f);
        GUI.Label(new Rect(Screen.width - size * 14f, size * 0.6f, size * 13f, size * 2f),
            $"● REC {Time.time - t0:0}s   F9 = marker", style);
    }
}
