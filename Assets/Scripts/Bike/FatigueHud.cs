using UnityEngine;

/// <summary>
/// Zmęczenie (stamina) jako zwężenie pola widzenia (niezależne od trybu pedałowania): ciemna winieta od krawędzi
/// i lekko mniejszy kąt widzenia kamery. Bez liter i liczb.
/// </summary>
public class FatigueHud : MonoBehaviour
{
    [SerializeField] PedalDrive pedals;
    [SerializeField] BikeController bike;
    [SerializeField] Camera cam;

    [Tooltip("Poniżej tej staminy zaczyna się zwężanie pola widzenia.")]
    [SerializeField, Range(0.1f, 1f)] float startStamina = 0.6f;
    [Tooltip("Stamina, przy której zwężenie jest największe.")]
    [SerializeField, Range(0f, 0.5f)] float fullStamina = 0.05f;
    [Tooltip("Największa ciemność winiety (0..1).")]
    [SerializeField, Range(0.3f, 1f)] float maxAlpha = 0.9f;
    [Tooltip("O ile stopni zmniejsza się kąt widzenia kamery przy największym zmęczeniu.")]
    [SerializeField] float fovNarrowing = 7f;
    [Tooltip("Jak szybko winieta i kąt widzenia nadążają za zmianą staminy.")]
    [SerializeField] float smoothing = 3f;

    Texture2D vignette;
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
        if (vignette != null) Destroy(vignette);
        if (cam != null && baseFov > 0f) cam.fieldOfView = baseFov;
    }

    void LateUpdate()
    {
        if (pedals == null) return;
        float target = HudDraw.MissionOver() ? 0f
            : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(startStamina, fullStamina, pedals.Stamina));
        fatigue = Mathf.MoveTowards(fatigue, target, smoothing * 0.5f * Time.unscaledDeltaTime);
        if (cam != null && baseFov > 0f) cam.fieldOfView = baseFov - fovNarrowing * fatigue;
    }

    void OnGUI()
    {
        if (fatigue <= 0.01f || Event.current.type != EventType.Repaint) return;
        GUI.depth = 11;     // za resztą HUD
        EnsureTexture();

        // Tekstura: środek przezroczysty, ciemność rośnie ku brzegom. Skala > 1 wypycha ciemność poza ekran (słaby efekt),
        // < 1 przybliża ją do środka; to, co poza teksturą, wypełnia czerń.
        float scale = Mathf.Lerp(3.2f, 0.85f, fatigue);
        float w = Screen.width * scale, h = Screen.height * scale;
        var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        float a = maxAlpha * Mathf.Clamp01(fatigue * 1.4f);

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, a);
        GUI.DrawTexture(r, vignette);
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

    void EnsureTexture()
    {
        if (vignette != null) return;
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
