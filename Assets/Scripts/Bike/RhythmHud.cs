using UnityEngine;

/// <summary>
/// Wskaźniki pedałowania w takt, zaprojektowane tak, żeby wystarczyło raz na nie spojrzeć, a potem grać wzrokiem peryferyjnym.
/// Po lewej i prawej krawędzi ekranu, na pełną wysokość, jest pionowy pasek dla lewej i prawej nogi:
///  - pasek nogi, która ma teraz kliknąć, kurczy się symetrycznie od góry i od dołu do środka (cel = idealny moment),
///    a po środku (gdy gracz się spóźnia) odrasta na zewnątrz, więc długość = jak daleko jest idealny moment,
///  - jego segmenty mają kolory skutków kliknięcia: od środka cyjanowy (idealne), zielony (okno łaski), pomarańczowy
///    (poza oknem – słabszy skok i strata sił), czerwony (źle: klepanie albo duże spóźnienie). Kolor końcówki paska
///    to więc to, co da kliknięcie w tej chwili,
///  - pasek drugiej nogi jest wyblakłym torem (legenda widoczna zawsze),
///  - poświata za paskiem ma kolor końcówki, a po kliknięciu jest błysk (cyjanowy szeroki = idealne, biały = dobre,
///    pomarańczowy = poza oknem, ciemnoczerwony = odrzucone),
///  - zmęczenie (stamina) zwęża pole widzenia: ciemna winieta od krawędzi i lekko mniejszy kąt widzenia kamery.
/// Bez liter i liczb – dźwięk dojdzie osobno.
/// </summary>
public class RhythmHud : MonoBehaviour
{
    [SerializeField] PedalDrive pedals;
    [SerializeField] BikeController bike;
    [SerializeField] Camera cam;

    [Header("Pasek")]
    [Tooltip("Szerokość paska w jednostkach HUD (u = wysokość ekranu / 32). Szeroki, by był widoczny kątem oka.")]
    [SerializeField, Range(0.4f, 3f)] float barWidth = 1.8f;
    [Tooltip("Margines paska od góry i dołu ekranu (część wysokości).")]
    [SerializeField, Range(0f, 0.05f)] float verticalMargin = 0.01f;

    [Header("Poświata i błysk")]
    [Tooltip("Szerokość poświaty za paskiem (część szerokości ekranu): najmniejsza i największa (przy idealnym momencie).")]
    [SerializeField, Range(0.01f, 0.3f)] float glowMinWidth = 0.14f;
    [SerializeField, Range(0.02f, 0.4f)] float glowMaxWidth = 0.22f;
    [Tooltip("Jak długo trwa błysk po kliknięciu (s).")]
    [SerializeField] float flashTime = 0.28f;

    [Header("Zmęczenie")]
    [Tooltip("Poniżej tej staminy zaczyna się zwężanie pola widzenia.")]
    [SerializeField, Range(0.1f, 1f)] float vignetteStartStamina = 0.6f;
    [Tooltip("Stamina, przy której zwężenie jest największe.")]
    [SerializeField, Range(0f, 0.5f)] float vignetteFullStamina = 0.05f;
    [Tooltip("Największa ciemność winiety (0..1).")]
    [SerializeField, Range(0.3f, 1f)] float vignetteMaxAlpha = 0.9f;
    [Tooltip("O ile stopni zmniejsza się kąt widzenia kamery przy największym zmęczeniu.")]
    [SerializeField] float fovNarrowing = 7f;
    [Tooltip("Jak szybko winieta i kąt widzenia nadążają za zmianą staminy.")]
    [SerializeField] float smoothing = 3f;

    // Kolory skutków (rosnąca jasność od złego do idealnego – czytelne też bez rozróżniania barw).
    static readonly Color PerfectColor = new Color(0.25f, 0.95f, 1.00f);
    static readonly Color GoodColor = new Color(0.35f, 0.85f, 0.30f);
    static readonly Color PoorColor = new Color(1.00f, 0.52f, 0.12f);
    static readonly Color BadColor = new Color(0.75f, 0.10f, 0.10f);
    static readonly Color FlashWhite = new Color(1.00f, 0.97f, 0.88f);

    Texture2D glow, vignette;
    MissionManager mission;
    float baseFov;
    float fatigue;       // wygładzone zwężenie 0..1

    void Awake()
    {
        if (pedals == null) pedals = FindFirstObjectByType<PedalDrive>();
        if (bike == null) bike = FindFirstObjectByType<BikeController>();
        if (cam == null && bike != null) cam = bike.GetComponentInChildren<Camera>();
        if (cam != null) baseFov = cam.fieldOfView;
    }

    void OnDestroy()
    {
        if (glow != null) Destroy(glow);
        if (vignette != null) Destroy(vignette);
        if (cam != null && baseFov > 0f) cam.fieldOfView = baseFov;
    }

    void LateUpdate()
    {
        if (pedals == null) return;
        float target = EndedOrDown() ? 0f
            : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(vignetteStartStamina, vignetteFullStamina, pedals.Stamina));
        fatigue = Mathf.MoveTowards(fatigue, target, smoothing * 0.5f * Time.unscaledDeltaTime);
        if (cam != null && baseFov > 0f) cam.fieldOfView = baseFov - fovNarrowing * fatigue;
    }

    bool EndedOrDown()
    {
        if (mission == null) mission = MissionManager.Instance;
        return mission != null && mission.CurrentPhase != MissionManager.Phase.Running;
    }

    void OnGUI()
    {
        if (pedals == null || Event.current.type != EventType.Repaint) return;
        GUI.depth = 10;     // za resztą HUD

        if (fatigue > 0.01f) DrawVignette(fatigue);
        if (EndedOrDown() || (bike != null && bike.Fallen)) return;

        EnsureTextures();
        float u = Mathf.Max(16f, Screen.height / 32f);
        var zones = ComputeZones();
        DrawSide(PedalDrive.Foot.Left, u, zones);
        DrawSide(PedalDrive.Foot.Right, u, zones);
    }

    // ------------------------------------------------------------ strefy czasowe

    /// <summary>Granice stref jako ułamki długości skoku, liczone od idealnego momentu: idealna, łaski, poza oknem, koniec (czerwień).</summary>
    struct Zones { public float perfect, grace, poor; }

    Zones ComputeZones()
    {
        float t = Mathf.Max(0.05f, pedals.StrokeTime(pedals.Speed));
        float poor = Mathf.Clamp(pedals.EarlyRejectFraction, 0.2f, 1f);
        float grace = Mathf.Min(pedals.GraceWindow / t, poor - 0.02f);
        float perfect = Mathf.Min(pedals.PerfectWindow / t, grace - 0.01f);
        return new Zones { perfect = Mathf.Max(perfect, 0.01f), grace = Mathf.Max(grace, 0.02f), poor = poor };
    }

    static Color ZoneColor(float d, Zones z)
    {
        if (d <= z.perfect) return PerfectColor;
        if (d <= z.grace) return GoodColor;
        if (d <= z.poor) return PoorColor;
        return BadColor;
    }

    /// <summary>Kolor z krótkim łagodnym przejściem na granicach stref (do poświaty, nie do segmentów).</summary>
    static Color SmoothZoneColor(float d, Zones z)
    {
        float k = 0.04f;
        Color c = BadColor;
        c = Color.Lerp(c, PoorColor, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(z.poor + k, z.poor - k, d)));
        c = Color.Lerp(c, GoodColor, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(z.grace + k, z.grace - k, d)));
        c = Color.Lerp(c, PerfectColor, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(z.perfect + k, z.perfect - k, d)));
        return c;
    }

    static float ZoneStrength(float d, Zones z)
    {
        if (d <= z.perfect) return 1f;
        if (d <= z.grace) return 0.6f;
        if (d <= z.poor) return 0.45f;
        return 0.3f;
    }

    // ------------------------------------------------------------ jedna strona

    void DrawSide(PedalDrive.Foot foot, float u, Zones z)
    {
        float w = u * barWidth;
        float x = foot == PedalDrive.Foot.Left ? u * 0.1f : Screen.width - u * 0.1f - w;
        float margin = Screen.height * verticalMargin;
        float half = (Screen.height - margin * 2f) * 0.5f;
        float cy = Screen.height * 0.5f;

        // Tło paska i wyblakły tor ze strefami (legenda).
        Fill(new Rect(x, margin, w, half * 2f), new Color(0f, 0f, 0f, 0.28f));
        DrawZones(x, w, cy, half, 1f, z, 0.22f);

        // Czy ta strona jest teraz aktywna i jak długi jest pasek (0 = idealny moment, 1 = cała długość = cały skok).
        bool active = false;
        float d = 0f, pulse = 1f;
        PedalDrive.Foot expected = pedals.ExpectedFoot;
        if (!pedals.InRhythm)
        {
            // Rytm jeszcze się nie zaczął (postój): obie strony pokazują pełny, pulsujący pasek – kliknij dowolną nogą.
            active = true;
            d = 1f;
            pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f));
        }
        else if (expected == foot)
        {
            active = true;
            // Przed idealnym momentem pasek kurczy się z postępem skoku; po nim odrasta z czasem spóźnienia.
            float stroke = Mathf.Max(0.05f, pedals.StrokeTime(pedals.Speed));
            d = pedals.Progress < 1f ? 1f - pedals.Progress : Mathf.Clamp01(-pedals.TimeToBeat / stroke);
        }

        if (active)
        {
            DrawZones(x, w, cy, half, d, z, 0.95f * pulse);
            // Cienka kreska w miejscu celu (środek) – widoczna także gdy pasek jest już krótki.
            Fill(new Rect(x - 1f, cy - 1f, w + 2f, 2f), new Color(1f, 1f, 1f, 0.9f * pulse));

            // Poświata o kolorze końcówki.
            Color tip = pedals.InRhythm ? SmoothZoneColor(d, z) : GoodColor;
            float strength = pedals.InRhythm ? ZoneStrength(d, z) : 0.35f;
            float gw = Mathf.Lerp(glowMinWidth, glowMaxWidth, strength);
            DrawGlow(foot, tip, gw, Mathf.Lerp(0.7f, 1.0f, strength) * pulse);
        }

        else
        {
            // Strona nieaktywna (ta noga właśnie pchnęła): stała, słaba poświata, żeby krawędź ekranu nie gasła.
            DrawGlow(foot, Color.white, glowMinWidth, 0.15f);
        }

        // Błysk po kliknięciu tą nogą.
        if (pedals.LastJudgedFoot == foot)
        {
            float age = Time.time - pedals.LastJudgementTime;
            if (age >= 0f && age < flashTime)
            {
                float k = 1f - age / flashTime;
                k *= k;
                GetFlash(pedals.LastJudgement, out Color fc, out float fw, out float fa);
                DrawGlow(foot, fc, fw, fa * k);
                Fill(new Rect(x, margin, w, half * 2f), new Color(fc.r, fc.g, fc.b, fa * k * 0.85f));
            }
        }
    }

    /// <summary>Rysuje segmenty od środka na zewnątrz do długości d (0..1) – góra i dół symetrycznie.</summary>
    void DrawZones(float x, float w, float cy, float half, float d, Zones z, float alpha)
    {
        float[] bounds = { 0f, z.perfect, z.grace, z.poor, 1f };
        Color[] colors = { PerfectColor, GoodColor, PoorColor, BadColor };
        for (int i = 0; i < 4; i++)
        {
            float a = bounds[i], b = Mathf.Min(bounds[i + 1], d);
            if (b <= a) continue;
            float y0 = a * half, y1 = b * half;
            var c = new Color(colors[i].r, colors[i].g, colors[i].b, alpha);
            Fill(new Rect(x, cy - y1, w, y1 - y0), c);   // górna połowa
            Fill(new Rect(x, cy + y0, w, y1 - y0), c);   // dolna połowa
        }
    }

    static void GetFlash(PedalDrive.Judgement j, out Color color, out float width, out float alpha)
    {
        switch (j)
        {
            case PedalDrive.Judgement.Perfect: color = PerfectColor; width = 0.22f; alpha = 1.0f; break;
            case PedalDrive.Judgement.Good: color = FlashWhite; width = 0.15f; alpha = 0.75f; break;
            case PedalDrive.Judgement.Start: color = FlashWhite; width = 0.11f; alpha = 0.5f; break;
            case PedalDrive.Judgement.Early:
            case PedalDrive.Judgement.Late: color = PoorColor; width = 0.09f; alpha = 0.6f; break;
            default: color = BadColor; width = 0.08f; alpha = 0.75f; break;      // Mash, WrongFoot
        }
    }

    void DrawGlow(PedalDrive.Foot foot, Color color, float widthFrac, float alpha)
    {
        if (alpha <= 0.005f) return;
        float gw = Screen.width * widthFrac;
        Color prev = GUI.color;
        GUI.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
        if (foot == PedalDrive.Foot.Left)
            GUI.DrawTexture(new Rect(0f, 0f, gw, Screen.height), glow);
        else
            GUI.DrawTextureWithTexCoords(new Rect(Screen.width - gw, 0f, gw, Screen.height), glow, new Rect(1f, 0f, -1f, 1f));
        GUI.color = prev;
    }

    static void Fill(Rect r, Color c)
    {
        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    // ------------------------------------------------------------ zwężenie pola widzenia

    void DrawVignette(float amount)
    {
        EnsureTextures();
        // Tekstura: środek przezroczysty, ciemność rośnie ku brzegom i na środku krawędzi osiąga pełną.
        // Skala > 1 wypycha ciemność poza ekran (słaby efekt), < 1 przybliża ją do środka; to, co poza teksturą, wypełnia czerń.
        float scale = Mathf.Lerp(3.2f, 0.85f, amount);
        float w = Screen.width * scale, h = Screen.height * scale;
        var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        float a = vignetteMaxAlpha * Mathf.Clamp01(amount * 1.4f);

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, a);
        GUI.DrawTexture(r, vignette);
        // Gdy tekstura jest mniejsza od ekranu, resztę pokrywa jednolita czerń.
        if (w < Screen.width || h < Screen.height)
        {
            Texture2D px = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Mathf.Max(0f, r.y)), px);
            GUI.DrawTexture(new Rect(0f, r.yMax, Screen.width, Mathf.Max(0f, Screen.height - r.yMax)), px);
            GUI.DrawTexture(new Rect(0f, r.y, Mathf.Max(0f, r.x), r.height), px);
            GUI.DrawTexture(new Rect(r.xMax, r.y, Mathf.Max(0f, Screen.width - r.xMax), r.height), px);
        }
        GUI.color = prev;
    }

    // ------------------------------------------------------------ tekstury (generowane w kodzie)

    void EnsureTextures()
    {
        if (glow == null)
        {
            // Poświata przy lewej krawędzi: najjaśniej przy brzegu, wygasa ku środkowi i łagodnie ku górze/dołowi.
            const int W = 96, H = 128;
            glow = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float vy = Mathf.Abs(y / (H - 1f) * 2f - 1f);
                float fy = 1f - Mathf.Pow(vy, 3.5f);       // prawie cała wysokość, wygaszana dopiero przy rogach
                for (int x = 0; x < W; x++)
                {
                    float fx = 1f - x / (W - 1f);
                    float a = Mathf.Pow(fx, 2.2f) * Mathf.Clamp01(fy);
                    px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            glow.SetPixels32(px);
            glow.Apply(false, true);
        }
        if (vignette == null)
        {
            const int N = 128;
            vignette = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x / (N - 1f) * 2f - 1f, dy = y / (N - 1f) * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;     // 0 w środku, 1 w rogu, 0,707 na środku krawędzi
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.70f, r));
                    px[y * N + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                }
            vignette.SetPixels32(px);
            vignette.Apply(false, true);
        }
    }
}
