using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Okno ze sterowaniem i krótkim opisem mechanik (IMGUI). Pokazuje się na początku gry, a F1 otwiera je ponownie.
/// Dopóki jest otwarte, gra jest zatrzymana (Time.timeScale = 0), a skrypty wejścia (rower, pedałowanie, kierownica, broń)
/// ignorują klawisze i mysz. Zamknięcie: Enter, Esc, F1 albo kliknięcie przycisku.
/// Tworzy się samo, gdy w scenie jest rower – nie wymaga obiektu w scenie.
/// </summary>
public class HelpPopup : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    /// <summary>Klatka, w której okno zostało zamknięte (Esc zamykający pomoc nie ma od razu otwierać menu ustawień).</summary>
    public static int ClosedFrame { get; private set; } = -1;

    PedalDrive pedals;
    GUIStyle label, wrap;

    static readonly Color Gold = new Color(1f, 0.85f, 0.2f);
    static readonly Color Dim = new Color(0.72f, 0.72f, 0.72f);
    static readonly Color Cyan = new Color(0.35f, 0.95f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<BikeController>() == null) return;
        if (FindFirstObjectByType<HelpPopup>() != null) return;
        new GameObject("HelpPopup").AddComponent<HelpPopup>();
    }

    void Start() => Show();

    void OnDestroy()
    {
        if (IsOpen) Hide();
    }

    public static void Toggle()
    {
        var popup = FindFirstObjectByType<HelpPopup>();
        if (popup == null) return;
        if (IsOpen) popup.Hide(); else popup.Show();
    }

    void Show()
    {
        if (IsOpen) return;
        GamePause.Push();
        IsOpen = true;
    }

    void Hide()
    {
        if (!IsOpen) return;
        GamePause.Pop();
        IsOpen = false;
        ClosedFrame = Time.frameCount;
    }

    /// <summary>Zamyka okno (np. z narzędzi testowych).</summary>
    public static void Close()
    {
        var popup = FindFirstObjectByType<HelpPopup>();
        if (popup != null) popup.Hide();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (PauseMenu.IsOpen) return;
        if (kb.f1Key.wasPressedThisFrame) { if (IsOpen) Hide(); else Show(); return; }
        if (IsOpen && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
            Hide();
    }

    void OnGUI()
    {
        if (!IsOpen) return;
        GUI.depth = -100;     // na wierzchu całego HUD
        label ??= new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow };
        wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };

        if (pedals == null) pedals = FindFirstObjectByType<PedalDrive>();
        PedalingMode pm = pedals != null ? pedals.Mode : null;

        float u = Mathf.Max(16f, Screen.height / 32f);
        Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.92f));

        float w = Mathf.Min(Screen.width - u * 2f, u * 52f);
        float x = (Screen.width - w) * 0.5f;
        float y = u * 1.2f;

        Text(new Rect(x, y, w, u * 2f), "WRONG SIDE OF THE TRACKS", u * 1.6f, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
        y += u * 2.1f;
        y += Paragraph(x, y, w, "Dogoń pociąg na rowerze i zastrzel gangsterów na pierwszym wagonie. Przegrasz, gdy pociąg oddali się o ponad 90 m albo dojedzie do końca mostu.", u * 0.85f, Color.white, TextAnchor.UpperCenter);
        y += u * 0.6f;

        // Dwie kolumny: sterowanie i mechaniki.
        float gap = u * 1.2f;
        float colW = (w - gap) * 0.5f;
        float yTop = y;

        float yl = yTop;
        Text(new Rect(x, yl, colW, u * 1.2f), "STEROWANIE", u * 1.0f, Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
        yl += u * 1.4f;
        string[,] keys =
        {
            { GameSettings.KeyName(GameAction.PedalLeft) + " / " + GameSettings.KeyName(GameAction.PedalRight), pm != null ? pm.ControlsHelp : "pedałuj (naprzemiennie)" },
            { GameSettings.KeyName(GameAction.GearHeavier) + " / " + GameSettings.KeyName(GameAction.GearLighter), "bieg cięższy / lżejszy" },
            { GameSettings.KeyName(GameAction.Brake), "hamulec (po upadku: przytrzymaj, aby wstać)" },
            { "LPM na uchwycie", "kierowanie – przytrzymaj i obracaj myszką" },
            { GameSettings.KeyName(GameAction.Reverse), "cofanie (na postoju)" },
            { "LPM na broni", "chwyć broń i celuj (kursor); PPM – strzał" },
            { GameSettings.KeyName(GameAction.Restart), "restart misji" },
            { "F1", "to okno (zatrzymuje grę)" },
            { "Esc", "ustawienia: dźwięk, tryb pedałowania, klawisze (zatrzymuje grę)" },
        };
        for (int i = 0; i < keys.GetLength(0); i++)
        {
            Text(new Rect(x, yl, u * 8.5f, u * 1.1f), keys[i, 0], u * 0.85f, Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
            yl += Paragraph(x + u * 8.7f, yl, colW - u * 8.7f, keys[i, 1], u * 0.85f, Color.white, TextAnchor.UpperLeft);
            yl += u * 0.3f;
        }

        float xr = x + colW + gap;
        float yr = yTop;
        Text(new Rect(xr, yr, colW, u * 1.2f), "MECHANIKI", u * 1.0f, Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
        yr += u * 1.4f;
        string[] mech =
        {
            pm != null ? pm.MechanicsHelp.Replace("A albo D", GameSettings.KeyName(GameAction.PedalLeft) + " albo " + GameSettings.KeyName(GameAction.PedalRight)).Replace("A i D", GameSettings.KeyName(GameAction.PedalLeft) + " i " + GameSettings.KeyName(GameAction.PedalRight)) : "",
            "<b>Biegi.</b> Pasek przy dolnej krawędzi pokazuje wszystkie biegi od najlżejszego (z lewej) do najcięższego (z prawej). Trzymaj biały wskaźnik w zielonym – tam napęd jest najmocniejszy. Optimum przesuwa się w prawo razem z prędkością.",
            "<b>Zmęczenie.</b> Błędy w pedałowaniu męczą, seria dobrych ruchów pozwala odpocząć. Zmęczenie zwęża pole widzenia i osłabia pedałowanie.",
            "<b>Strzelanie.</b> Big Smoke z wieżyczki namierza gangsterów sam, ale celuje tym lepiej, im stabilniej jedziesz – bez szarpania kierownicą i gwałtownego hamowania.",
        };
        for (int i = 0; i < mech.Length; i++)
        {
            yr += Paragraph(xr, yr, colW, mech[i], u * 0.8f, Dim, TextAnchor.UpperLeft);
            yr += u * 0.5f;
        }

        float yb = Mathf.Max(yl, yr) + u * 0.8f;
        var btn = new Rect(Screen.width * 0.5f - u * 6f, Mathf.Min(yb, Screen.height - u * 3f), u * 12f, u * 2f);
        Fill(btn, new Color(Gold.r, Gold.g, Gold.b, 0.18f));
        Frame(btn, 2f, Gold);
        Text(btn, "Graj  (Enter)", u * 1.0f, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            Vector2 p = mouse.position.ReadValue();
            p.y = Screen.height - p.y;
            if (btn.Contains(p)) Hide();
        }
    }

    float Paragraph(float x, float y, float w, string s, float size, Color c, TextAnchor anchor)
    {
        wrap.fontSize = Mathf.RoundToInt(size);
        wrap.normal.textColor = c;
        wrap.alignment = anchor;
        float h = wrap.CalcHeight(new GUIContent(s), w);
        GUI.Label(new Rect(x, y, w, h), s, wrap);
        return h;
    }

    void Text(Rect r, string s, float size, Color c, TextAnchor anchor, FontStyle style)
    {
        label.fontSize = Mathf.RoundToInt(size);
        label.normal.textColor = c;
        label.alignment = anchor;
        label.fontStyle = style;
        GUI.Label(r, s, label);
    }

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
}
