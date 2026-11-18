using UnityEngine;

/// <summary>
/// Pasek biegów przy dolnej krawędzi ekranu (niezależny od trybu pedałowania): wszystkie biegi od najlżejszego z lewej
/// do najcięższego z prawej. Kolor segmentu to wydajność pedałowania na tym biegu przy obecnej prędkości:
/// jasna zieleń = optimum (z białą kreską u góry), żółty = prawie, pomarańczowy = słabo, ciemnoczerwony = źle.
/// Biała ramka pokazuje bieżący bieg, a poświata przy dolnej krawędzi ma kolor wydajności bieżącego biegu.
/// Optimum wędruje po pasku wraz z prędkością.
/// </summary>
public class GearBarHud : MonoBehaviour
{
    [SerializeField] PedalDrive pedals;
    [SerializeField] BikeController bike;

    [Tooltip("Wysokość paska biegów (jednostki HUD u = wysokość ekranu / 32).")]
    [SerializeField, Range(0.5f, 2.5f)] float barHeight = 1.1f;
    [Tooltip("Wysokość poświaty przy dolnej krawędzi (część wysokości ekranu).")]
    [SerializeField, Range(0.05f, 0.4f)] float glowHeight = 0.18f;
    [Tooltip("Jasność poświaty biegu (0..1).")]
    [SerializeField, Range(0.1f, 1f)] float glowAlpha = 0.8f;
    [Tooltip("Jak szybko wskaźnik biegu nadąża za zmianą (1/s).")]
    [SerializeField] float smoothing = 10f;

    static readonly Color Optimum = new Color(0.15f, 0.95f, 0.25f);
    static readonly Color Near = new Color(1.00f, 0.90f, 0.15f);
    static readonly Color Poor = new Color(1.00f, 0.48f, 0.08f);
    static readonly Color Bad = new Color(0.50f, 0.06f, 0.10f);

    Texture2D glowTex;
    float shownGear, shownSpeed;
    Color glowColor;
    bool init;

    void Awake()
    {
        if (pedals == null) pedals = FindFirstObjectByType<PedalDrive>();
        if (bike == null) bike = FindFirstObjectByType<BikeController>();
    }

    void OnDestroy()
    {
        if (glowTex != null) Destroy(glowTex);
    }

    Color EfficiencyColor(float eff)
    {
        float thr = pedals.OptimumThreshold;
        if (eff >= thr) return Optimum;
        if (eff >= thr - 0.10f) return Near;
        if (eff >= thr - 0.35f) return Poor;
        return Bad;
    }

    void OnGUI()
    {
        if (pedals == null || Event.current.type != EventType.Repaint) return;
        int n = pedals.GearCount;
        if (n <= 0 || HudDraw.Hidden(bike)) return;
        GUI.depth = 10;
        EnsureTexture();

        float u = Mathf.Max(16f, Screen.height / 32f);
        float dt = Time.unscaledDeltaTime;
        float refSpeed = pedals.GearReferenceSpeed(pedals.Speed);
        if (!init) { shownGear = pedals.GearPosition; shownSpeed = refSpeed; init = true; }
        shownGear = Mathf.Lerp(shownGear, pedals.GearPosition, 1f - Mathf.Exp(-smoothing * dt));
        shownSpeed = Mathf.Lerp(shownSpeed, refSpeed, 1f - Mathf.Exp(-3f * dt));   // prędkość wygładzona wolniej: optimum nie skacze

        float h = u * barHeight;
        float x0 = u * 2.4f, x1 = Screen.width - u * 2.4f;
        float y = Screen.height - h - u * 0.4f;
        float segW = (x1 - x0) / n;

        // Poświata przy dolnej krawędzi o kolorze wydajności bieżącego biegu (zmienia się płynnie).
        Color target = EfficiencyColor(pedals.EfficiencyAtPosition(pedals.GearPosition, shownSpeed));
        glowColor = glowColor.a > 0f ? Color.Lerp(glowColor, target, 1f - Mathf.Exp(-8f * dt)) : target;
        float gh = Screen.height * glowHeight;
        Color prev = GUI.color;
        GUI.color = new Color(glowColor.r, glowColor.g, glowColor.b, glowAlpha);
        GUI.DrawTexture(new Rect(0f, Screen.height - gh, Screen.width, gh), glowTex);
        GUI.color = prev;

        HudDraw.Fill(new Rect(x0 - 2f, y - 2f, x1 - x0 + 4f, h + 4f), new Color(0f, 0f, 0f, 0.45f));
        for (int i = 0; i < n; i++)
        {
            Color c = EfficiencyColor(pedals.EfficiencyAtPosition(i, shownSpeed));
            HudDraw.Fill(new Rect(x0 + i * segW + 1f, y, segW - 2f, h), new Color(c.r, c.g, c.b, 0.95f));
            if (c == Optimum) HudDraw.Fill(new Rect(x0 + i * segW + 1f, y, segW - 2f, 4f), Color.white);   // optimum dodatkowo oznaczone u góry
        }

        // Wskaźnik bieżącego biegu: biała ramka segmentu z wystającymi znacznikami.
        float cx = x0 + (shownGear + 0.5f) * segW;
        float mw = Mathf.Max(segW, u * 0.5f);
        var box = new Rect(cx - mw * 0.5f, y - u * 0.25f, mw, h + u * 0.5f);
        HudDraw.Fill(new Rect(box.x, box.y, box.width, 3f), Color.white);
        HudDraw.Fill(new Rect(box.x, box.yMax - 3f, box.width, 3f), Color.white);
        HudDraw.Fill(new Rect(box.x, box.y, 3f, box.height), Color.white);
        HudDraw.Fill(new Rect(box.xMax - 3f, box.y, 3f, box.height), Color.white);
    }

    void EnsureTexture()
    {
        if (glowTex != null) return;
        // Poświata przy dolnej krawędzi: najjaśniej przy brzegu, wygasa ku górze i ku bokom.
        const int W = 128, H = 64;
        glowTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float fy = Mathf.Pow(1f - y / (H - 1f), 2.2f);     // wiersz 0 = dół
            for (int x = 0; x < W; x++)
            {
                float vx = Mathf.Abs(x / (W - 1f) * 2f - 1f);
                float a = fy * Mathf.Clamp01(1f - Mathf.Pow(vx, 3.5f));
                px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        glowTex.SetPixels32(px);
        glowTex.Apply(false, true);
    }
}
