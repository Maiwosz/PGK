using UnityEngine;

/// <summary>
/// HUD misji (IMGUI): pierścienie na gangsterach (namierzanie Big Smoke'a, drżenie od niestabilnej jazdy), pasek pociągu z ikonami
/// gangsterów u góry, czerwona poświata, gdy pociąg ucieka, oraz ekran końcowy ze statusem i statystykami.
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

    struct KillMark { public Vector3 pos; public float time; }

    GUIStyle text;
    Camera cam;
    Texture2D ringTex, edgeTex;
    readonly System.Collections.Generic.List<KillMark> kills = new System.Collections.Generic.List<KillMark>();

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
            EnsureTextures();
            DrawRings(u);
            DrawTopBar(u);
            if (mission.Waiting) DrawWaiting(u);
            else DrawEdgeWarning(u);
            if (mission.LosingDisabled)
                Label(new Rect(Screen.width * 0.5f - 400f, u * 2.4f, 800f, u * 0.8f), "DEBUG: przegrana misji wyłączona (F9)", u * 0.7f, Bad, TextAnchor.MiddleCenter, FontStyle.Bold);
        }
        else DrawEndScreen(u);
    }

    /// <summary>Misja czeka na pierwszy skok pedału.</summary>
    void DrawWaiting(float u)
    {
        float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 3f);
        var c = new Color(1f, 0.85f, 0.2f, pulse);
        Label(new Rect(Screen.width * 0.5f - 600f, Screen.height * 0.3f, 1200f, u * 2f), "Zacznij pedałować, aby ruszyć", u * 1.6f, c, TextAnchor.MiddleCenter, FontStyle.Bold);
        Label(new Rect(Screen.width * 0.5f - 600f, Screen.height * 0.3f + u * 2.1f, 1200f, u * 1.2f), "Pociąg stoi i misja się nie zaczęła – czas ruszy z pierwszym skokiem pedału (A / D).", u * 0.85f, Dim, TextAnchor.MiddleCenter);
    }

    // ------------------------------------------------------------ pierścienie na gangsterach

    /// <summary>
    /// Wokół namierzanego gangstera zaciska się pierścień (postęp namierzania); drży tym mocniej, im gorsza stabilność jazdy.
    /// Strzał, trafienie i zabicie dają błysk. Pozostali żywi gangsterzy mają cienki, przygaszony pierścień.
    /// </summary>
    void DrawRings(float u)
    {
        if (cam == null || turret == null) return;
        float now = Time.time;
        foreach (var g in Gangster.All)
        {
            if (!g.Alive) continue;
            Vector3 sp = cam.WorldToScreenPoint(g.AimPosition);
            if (sp.z <= 0.5f) continue;
            float dist = Vector3.Distance(turret.Muzzle, g.AimPosition);
            if (dist > 260f) continue;
            float scale = Mathf.Clamp(35f / Mathf.Max(8f, dist), 0.55f, 1.6f);
            float r = u * 1.5f * scale;
            float x = sp.x, y = Screen.height - sp.y;

            if (turret.Target != g)
            {
                bool inRange = dist <= turret.Range;
                Ring(x, y, r * 0.8f, inRange ? new Color(Gang.r, Gang.g, Gang.b, 0.55f) : new Color(1f, 1f, 1f, 0.25f));
                continue;
            }

            bool firing = turret.State == AutoTurret.TurretState.Firing;
            float lockP = firing ? 1f : turret.AcquireProgress;
            float shake = (1f - turret.Stability) * r * 0.9f;
            x += (Mathf.PerlinNoise(now * 28f, 1.3f) - 0.5f) * 2f * shake;
            y += (Mathf.PerlinNoise(now * 28f, 7.9f) - 0.5f) * 2f * shake;

            Color c = firing ? Bad : Color.Lerp(Gang, Color.white, lockP * lockP);
            Ring(x, y, r, c);
            if (!firing) Ring(x, y, r * Mathf.Lerp(2.4f, 1f, lockP), new Color(c.r, c.g, c.b, 0.55f + 0.4f * lockP));
            Fill(new Rect(x - 2f, y - 2f, 4f, 4f), c);

            float shot = now - turret.LastShotTime;
            if (shot >= 0f && shot < 0.12f)
                Ring(x, y, r * (1f + shot * 3f), new Color(1f, 1f, 1f, (1f - shot / 0.12f) * (turret.LastShotHit ? 0.9f : 0.35f)));
            float hit = now - turret.LastHitTime;
            if (hit >= 0f && hit < 0.25f)
                Ring(x, y, r * (1.2f + hit * 5f), new Color(1f, 0.55f, 0.2f, 1f - hit / 0.25f));
        }

        // Zabicie: złoty pierścień rozszerzający się w miejscu gangstera.
        for (int i = kills.Count - 1; i >= 0; i--)
        {
            float age = now - kills[i].time;
            if (age > 0.7f) { kills.RemoveAt(i); continue; }
            Vector3 sp = cam.WorldToScreenPoint(kills[i].pos);
            if (sp.z <= 0.5f) continue;
            float k = age / 0.7f;
            Ring(sp.x, Screen.height - sp.y, u * (1.2f + 4f * k), new Color(1f, 0.85f, 0.2f, 1f - k));
            Ring(sp.x, Screen.height - sp.y, u * (0.6f + 2.5f * k), new Color(1f, 1f, 1f, 1f - k));
        }
    }

    void Ring(float cx, float cy, float radius, Color c)
    {
        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(new Rect(cx - radius, cy - radius, radius * 2f, radius * 2f), ringTex);
        GUI.color = prev;
    }

    // ------------------------------------------------------------ pasek pociągu u góry

    /// <summary>Ikona pociągu jedzie w prawo do ikony mostu; na pociągu są ikony gangsterów (wypełnienie = życie).</summary>
    void DrawTopBar(float u)
    {
        var train = mission.Train;
        if (train == null || mission.BridgeEndDistance <= 1f || mission.BridgeEndDistance > 1e8f) return;
        float w = Screen.width * 0.4f;
        float x0 = Screen.width * 0.5f - w * 0.5f;
        float y = u * 1.9f;
        float prog = Mathf.Clamp01(train.FrontDistance / mission.BridgeEndDistance);

        // tor
        Fill(new Rect(x0, y + u * 0.35f, w, u * 0.12f), new Color(1f, 1f, 1f, 0.35f));

        // most (prawy koniec): pomost na filarach
        float bx = x0 + w;
        var bridgeCol = new Color(0.85f, 0.85f, 0.85f, 0.9f);
        Fill(new Rect(bx - u * 0.9f, y + u * 0.1f, u * 1.8f, u * 0.2f), bridgeCol);
        for (int i = 0; i < 3; i++) Fill(new Rect(bx - u * 0.8f + i * u * 0.75f, y + u * 0.3f, u * 0.2f, u * 0.55f), bridgeCol);

        // pociąg: lokomotywa z przodu, wagon z gangsterami za nią
        int total = Mathf.Max(1, mission.TotalGangsters);
        float slot = u * 0.95f, pad = u * 0.15f;
        float carW = total * (slot + pad) + pad;
        float locoW = u * 1.5f, h = u * 1.2f;
        float right = Mathf.Lerp(x0 + carW + locoW, x0 + w, prog);
        float ty = y - u * 0.25f;
        var locoCol = new Color(0.3f, 0.3f, 0.32f, 0.95f);
        Rect loco = new Rect(right - locoW, ty + u * 0.25f, locoW, h - u * 0.25f);
        Fill(loco, locoCol);
        Fill(new Rect(loco.x + u * 0.15f, ty, u * 0.7f, u * 0.3f), locoCol);
        Rect car = new Rect(loco.x - carW - u * 0.1f, ty, carW, h);
        Fill(car, new Color(0.08f, 0.08f, 0.08f, 0.85f));
        Frame(car, 1f, new Color(1f, 1f, 1f, 0.45f));
        for (int i = 0; i < Gangster.All.Count && i < total; i++)
        {
            var g = Gangster.All[i];
            var box = new Rect(car.x + pad + i * (slot + pad), car.y + (h - slot) * 0.5f, slot, slot);
            Fill(box, new Color(0.22f, 0.22f, 0.22f, 0.9f));
            float f = g.Alive ? Mathf.Max(0.12f, g.Health01) : 0f;
            Fill(new Rect(box.x, box.yMax - slot * f, slot, slot * f), Gang);
            Frame(box, 1f, new Color(1f, 1f, 1f, g.Alive ? 0.6f : 0.2f));
        }
    }

    // ------------------------------------------------------------ czerwona poświata przy górnej krawędzi

    /// <summary>Gdy pociąg się oddala: górna krawędź ekranu rumieni się na czerwono; za granicą porażki pulsuje.</summary>
    void DrawEdgeWarning(float u)
    {
        if (mission.CurrentPhase != MissionManager.Phase.Running || mission.LosingDisabled) return;
        float d = mission.TrainDistance;
        if (d < mission.WarnDistance) return;
        bool critical = d >= mission.LoseDistance;
        float k = Mathf.InverseLerp(mission.WarnDistance, mission.LoseDistance, d);
        float a = critical ? 0.7f + 0.3f * Mathf.Sin(Time.time * 9f) : Mathf.Lerp(0.15f, 0.55f, k);
        Color prev = GUI.color;
        GUI.color = new Color(1f, 0.1f, 0.05f, Mathf.Clamp01(a));
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height * (critical ? 0.28f : 0.2f)), edgeTex);
        GUI.color = prev;
    }

    void EnsureTextures()
    {
        if (ringTex == null)
        {
            const int N = 128;
            ringTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Mathf.Sqrt(Mathf.Pow((x + 0.5f) / N * 2f - 1f, 2f) + Mathf.Pow((y + 0.5f) / N * 2f - 1f, 2f));
                    float a = Mathf.Clamp01((d - 0.88f) / 0.04f) * Mathf.Clamp01((1f - d) / 0.04f);   // pierścień o grubości ~0,1 promienia
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            ringTex.SetPixels32(px);
            ringTex.Apply(false, true);
        }
        if (edgeTex == null)
        {
            const int H = 64;
            edgeTex = new Texture2D(2, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[2 * H];
            for (int y = 0; y < H; y++)
            {
                // wiersz 0 tekstury to jej dół, więc najjaśniejszy (górna krawędź ekranu) ma indeks H-1
                float a = Mathf.Pow(y / (H - 1f), 2.2f);
                px[y * 2] = px[y * 2 + 1] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            edgeTex.SetPixels32(px);
            edgeTex.Apply(false, true);
        }
    }

    void OnEnable() => Gangster.AnyDied += OnGangsterDied;

    void OnDisable() => Gangster.AnyDied -= OnGangsterDied;

    void OnDestroy()
    {
        if (ringTex != null) Destroy(ringTex);
        if (edgeTex != null) Destroy(edgeTex);
    }

    void OnGangsterDied(Gangster g) => kills.Add(new KillMark { pos = g.AimPosition, time = Time.time });

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
