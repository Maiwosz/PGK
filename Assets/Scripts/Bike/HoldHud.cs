using UnityEngine;

/// <summary>
/// HUD trybu HoldPedaling (niezależny od RhythmHud, własny wygląd). Po lewej i prawej krawędzi ekranu, na pełną wysokość:
///  - pasek nogi, która TRZYMA klawisz, rośnie od środka ku górze i dołowi razem z postępem skoku. Cały pasek zmienia płynnie kolor:
///    długo czerwony (puszczenie teraz zepsuje skok), tuż przed końcem pomarańczowy i zielony (można puścić); na końcu skoku jest biały błysk,
///  - pasek nogi, która ma nacisnąć następna, to pulsujący znacznik po środku ("gotowa"), który rozjaśnia się, im bliżej
///    końca skoku i okna kolejki (naciśnij teraz, a ruszy bez przerwy),
///  - skok niedokończony (puszczony za wcześnie) zostaje na pasku pulsując na pomarańczowo – dokończ go tą samą nogą,
///  - poświata za paskiem ma ten sam kolor co pasek, a po naciśnięciu jest błysk według oceny (cyjan = bez przerwy,
///    biały = dobrze, pomarańczowy = za wcześnie lub spóźnione, ciemnoczerwony = zła noga).
/// Rysuje się tylko, gdy aktywnym trybem jest HoldPedaling. Aby zrezygnować z tego trybu, usuń ten plik razem z HoldPedaling.
/// </summary>
public class HoldHud : MonoBehaviour
{
    [SerializeField] PedalDrive pedals;
    [SerializeField] HoldPedaling hold;
    [SerializeField] BikeController bike;

    [Tooltip("Szerokość paska w jednostkach HUD (u = wysokość ekranu / 32).")]
    [SerializeField, Range(0.4f, 3f)] float barWidth = 1.8f;
    [Tooltip("Margines paska od góry i dołu ekranu (część wysokości).")]
    [SerializeField, Range(0f, 0.05f)] float verticalMargin = 0.01f;
    [Tooltip("Szerokość poświaty za paskiem (część szerokości ekranu): najmniejsza i największa.")]
    [SerializeField, Range(0.01f, 0.3f)] float glowMinWidth = 0.12f;
    [SerializeField, Range(0.02f, 0.4f)] float glowMaxWidth = 0.22f;
    [Tooltip("Jak długo trwa błysk (s).")]
    [SerializeField] float flashTime = 0.3f;

    // Kolory paska trzymania (wyraźnie różne): długo czerwony, tuż przed końcem pomarańczowy, w oknie łaski zielony.
    static readonly Color Red = new Color(0.92f, 0.07f, 0.07f);
    static readonly Color Orange = new Color(1.00f, 0.55f, 0.04f);
    static readonly Color Green = new Color(0.10f, 1.00f, 0.25f);
    static readonly Color ReadyColor = new Color(0.35f, 0.95f, 1.00f);
    static readonly Color ChainColor = new Color(0.25f, 0.95f, 1.00f);
    static readonly Color GoodFlash = new Color(1.00f, 0.97f, 0.88f);
    static readonly Color PoorFlash = new Color(1.00f, 0.52f, 0.12f);
    static readonly Color BadFlash = new Color(0.75f, 0.10f, 0.10f);

    Texture2D glow;

    void Awake()
    {
        if (pedals == null) pedals = FindFirstObjectByType<PedalDrive>();
        if (hold == null) hold = FindFirstObjectByType<HoldPedaling>();
        if (bike == null) bike = FindFirstObjectByType<BikeController>();
    }

    void OnDestroy()
    {
        if (glow != null) Destroy(glow);
    }

    /// <summary>
    /// Kolor paska dla postępu skoku p (0..1) przy oknie łaski o długości grace (część skoku): czerwony przez większość skoku,
    /// potem szybkie, płynne przejście przez pomarańczowy do zieleni, która zaczyna się z oknem łaski (od tej chwili można puścić).
    /// </summary>
    static Color Ramp(float p, float grace)
    {
        float greenStart = Mathf.Clamp(1f - grace, 0.35f, 0.97f);
        float orangeStart = Mathf.Max(0.3f, greenStart - 0.14f);
        float mid = (orangeStart + greenStart) * 0.5f;
        if (p <= orangeStart) return Red;
        if (p < mid) return Color.Lerp(Red, Orange, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(orangeStart, mid, p)));
        if (p < greenStart) return Color.Lerp(Orange, Green, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(mid, greenStart, p)));
        return Green;
    }

    void OnGUI()
    {
        if (pedals == null || hold == null || Event.current.type != EventType.Repaint) return;
        if (pedals.Mode != hold || HudDraw.Hidden(bike)) return;
        GUI.depth = 10;
        EnsureTexture();

        float u = Mathf.Max(16f, Screen.height / 32f);
        DrawSide(PedalDrive.Foot.Left, u);
        DrawSide(PedalDrive.Foot.Right, u);
    }

    void DrawSide(PedalDrive.Foot foot, float u)
    {
        float w = u * barWidth;
        float x = foot == PedalDrive.Foot.Left ? u * 0.1f : Screen.width - u * 0.1f - w;
        float margin = Screen.height * verticalMargin;
        float half = (Screen.height - margin * 2f) * 0.5f;
        float cy = Screen.height * 0.5f;

        // Pusty tor.
        HudDraw.Fill(new Rect(x, margin, w, half * 2f), new Color(0f, 0f, 0f, 0.3f));

        bool holding = hold.HoldingFoot == foot && pedals.Progress < 1f;
        bool finishedHeld = hold.HoldingFoot == foot && pedals.Progress >= 1f;   // skok skończony, klawisz jeszcze wciśnięty
        bool unfinished = hold.Unfinished && pedals.StrokeFoot == foot && hold.HoldingFoot != foot;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);

        Color glowColor = Color.white;
        float glowStrength = 0.15f;   // 0..1: szerokość i jasność poświaty

        if (holding || finishedHeld)
        {
            float p = finishedHeld ? 1f : pedals.Progress;
            Color c = Ramp(p, hold.GraceFraction);
            DrawGrowingBar(x, w, cy, half, p, c, 0.97f);
            glowColor = c;
            glowStrength = Mathf.Lerp(0.55f, 1f, p);
        }
        else if (unfinished)
        {
            // Skok puszczony za wcześnie: pasek zostaje i pulsuje na pomarańczowo – dokończ go tą samą nogą.
            Color c = Color.Lerp(Orange, Red, pulse);
            DrawGrowingBar(x, w, cy, half, pedals.Progress, c, 0.6f + 0.35f * pulse);
            glowColor = c;
            glowStrength = 0.6f;
        }
        else if (hold.ExpectedFoot == foot || hold.ExpectedFoot == PedalDrive.Foot.None)
        {
            // Ta noga ma teraz nacisnąć: pulsujący znacznik po środku, jaśniejszy w oknie kolejki przy końcu skoku.
            float near = 0f;
            if (pedals.Progress < 1f && pedals.Engaged)
                near = 1f - Mathf.Clamp01((1f - pedals.Progress) / Mathf.Max(0.05f, hold.PressWindowFraction));
            else near = 1f;
            float k = 0.25f + 0.75f * near;
            float len = Mathf.Lerp(0.03f, 0.12f, k * (0.6f + 0.4f * pulse));
            DrawGrowingBar(x, w, cy, half, len, ReadyColor, Mathf.Lerp(0.35f, 0.95f, k));
            glowColor = ReadyColor;
            glowStrength = Mathf.Lerp(0.25f, 0.9f, k * (0.5f + 0.5f * pulse));
        }

        DrawGlow(foot, glowColor, Mathf.Lerp(glowMinWidth, glowMaxWidth, glowStrength), Mathf.Lerp(0.3f, 1f, glowStrength));

        // Błysk na końcu skoku (pasek pełny) tej nogi.
        if (hold.LastCompletedFoot == foot)
        {
            float age = Time.time - hold.LastCompletedTime;
            if (age >= 0f && age < flashTime)
            {
                float k = 1f - age / flashTime;
                k *= k;
                DrawGlow(foot, Color.white, glowMaxWidth * 1.3f, k);
                HudDraw.Fill(new Rect(x, margin, w, half * 2f), new Color(1f, 1f, 1f, 0.8f * k));
            }
        }

        // Błysk po naciśnięciu tą nogą (ocena).
        if (pedals.LastJudgedFoot == foot)
        {
            float age = Time.time - pedals.LastJudgementTime;
            if (age >= 0f && age < flashTime)
            {
                float k = 1f - age / flashTime;
                k *= k;
                GetFlash(pedals.LastJudgement, out Color fc, out float fw, out float fa);
                DrawGlow(foot, fc, fw, fa * k);
                HudDraw.Fill(new Rect(x, margin, w, half * 2f), new Color(fc.r, fc.g, fc.b, fa * k * 0.7f));
            }
        }
    }

    /// <summary>Pasek rosnący od środka: fill (0..1) = jaka część połowy wysokości jest wypełniona (góra i dół symetrycznie).</summary>
    static void DrawGrowingBar(float x, float w, float cy, float half, float fill, Color c, float alpha)
    {
        float len = Mathf.Clamp01(fill) * half;
        if (len < 1f) len = 1f;
        var col = new Color(c.r, c.g, c.b, alpha);
        HudDraw.Fill(new Rect(x, cy - len, w, len * 2f), col);
        HudDraw.Fill(new Rect(x - 1f, cy - 1f, w + 2f, 2f), new Color(1f, 1f, 1f, 0.7f * alpha));   // znacznik środka
    }

    static void GetFlash(PedalDrive.Judgement j, out Color color, out float width, out float alpha)
    {
        switch (j)
        {
            case PedalDrive.Judgement.Perfect: color = ChainColor; width = 0.22f; alpha = 1.0f; break;
            case PedalDrive.Judgement.Good: color = GoodFlash; width = 0.15f; alpha = 0.75f; break;
            case PedalDrive.Judgement.Start: color = GoodFlash; width = 0.11f; alpha = 0.5f; break;
            case PedalDrive.Judgement.Early:
            case PedalDrive.Judgement.Late: color = PoorFlash; width = 0.09f; alpha = 0.6f; break;
            default: color = BadFlash; width = 0.08f; alpha = 0.75f; break;      // zła noga
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

    void EnsureTexture()
    {
        if (glow != null) return;
        const int W = 96, H = 128;
        glow = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float vy = Mathf.Abs(y / (H - 1f) * 2f - 1f);
            float fy = 1f - Mathf.Pow(vy, 3.5f);
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
}
