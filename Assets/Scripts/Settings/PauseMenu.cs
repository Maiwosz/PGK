using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Menu ustawień (Esc): pauzuje grę i pozwala zmienić głośności, tryb pedałowania oraz przypisanie klawiszy.
/// Tworzy się samo, gdy w scenie jest rower. Całość rysowana w IMGUI w wirtualnym układzie 1080 px wysokości;
/// wejście obsługuje Update (nie OnGUI), żeby kliknięcie nie liczyło się wielokrotnie w jednej klatce.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    const float PanelW = 1500f;
    static readonly Color Gold = new Color(1f, 0.85f, 0.2f);
    static readonly Color Dim = new Color(0.72f, 0.72f, 0.72f);
    static readonly Color Cyan = new Color(0.35f, 0.95f, 1f);

    PedalDrive pedals;
    GUIStyle label;
    AudioSource preview;
    int dragSlider = -1;
    int listening = -1;          // indeks akcji, której klawisza teraz słuchamy (-1 = nie)
    Vector2 virt;                // rozmiar wirtualnego ekranu
    float scale;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<BikeController>() == null) return;
        if (FindFirstObjectByType<PauseMenu>() != null) return;
        new GameObject("PauseMenu").AddComponent<PauseMenu>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => IsOpen = false;

    void OnDestroy()
    {
        if (IsOpen) Close();
    }

    public static void Open()
    {
        var m = FindFirstObjectByType<PauseMenu>();
        if (m == null || IsOpen) return;
        IsOpen = true;
        m.listening = -1;
        m.dragSlider = -1;
        GamePause.Push();
        Cursor.visible = true;
    }

    public static void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        GameSettings.Save();
        GamePause.Pop();
    }

    // ------------------------------------------------------------ układ (wspólny dla wejścia i rysowania)

    float X0 => (virt.x - PanelW) * 0.5f;
    Rect SliderRect(int i) => new Rect(X0 + 300f, 215f + i * 70f, 340f, 36f);
    Rect ModeRect(int i) => new Rect(X0 + 40f + i * 320f, 650f, 300f, 62f);
    Rect KeyRect(int i) => new Rect(X0 + 1090f, 205f + i * 62f, 330f, 50f);
    Rect ResetKeysRect => new Rect(X0 + 1090f, 205f + 7 * 62f + 15f, 330f, 50f);
    Rect ResumeRect => new Rect(virt.x * 0.5f - 410f, 900f, 400f, 70f);
    Rect HelpRect => new Rect(virt.x * 0.5f + 10f, 900f, 400f, 70f);

    void ComputeScale()
    {
        scale = Screen.height / 1080f;
        virt = new Vector2(Screen.width / scale, 1080f);
    }

    // ------------------------------------------------------------ wejście

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (pedals == null) pedals = FindFirstObjectByType<PedalDrive>();

        if (!IsOpen)
        {
            if (kb.escapeKey.wasPressedThisFrame && !HelpPopup.IsOpen && HelpPopup.ClosedFrame != Time.frameCount) Open();
            return;
        }
        ComputeScale();
        Cursor.visible = true;

        if (listening >= 0) { ListenForKey(kb); return; }
        if (kb.escapeKey.wasPressedThisFrame) { Close(); return; }

        var mouse = Mouse.current;
        if (mouse == null) return;
        Vector2 p = mouse.position.ReadValue() / scale;
        p.y = virt.y - p.y;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            for (int i = 0; i < 5; i++)
                if (Grow(SliderRect(i), 14f).Contains(p)) { dragSlider = i; break; }
            for (int i = 0; i < 2; i++)
                if (ModeRect(i).Contains(p)) SetMode(i);
            for (int i = 0; i < 7; i++)
                if (KeyRect(i).Contains(p)) listening = i;
            if (ResetKeysRect.Contains(p)) GameSettings.ResetKeys();
            if (ResumeRect.Contains(p)) { Close(); return; }
            if (HelpRect.Contains(p)) { Close(); HelpPopup.Toggle(); return; }
        }

        if (dragSlider >= 0)
        {
            var r = SliderRect(dragSlider);
            GameSettings.Set((GameSettings.Volume)dragSlider, Mathf.Clamp01((p.x - r.x) / r.width));
            if (!mouse.leftButton.isPressed)
            {
                PlayPreview((GameSettings.Volume)dragSlider);
                dragSlider = -1;
            }
        }
    }

    static Rect Grow(Rect r, float m) => new Rect(r.x - m, r.y - m, r.width + m * 2f, r.height + m * 2f);

    void ListenForKey(Keyboard kb)
    {
        foreach (var k in kb.allKeys)
        {
            if (!k.wasPressedThisFrame) continue;
            Key key = k.keyCode;
            if (key == Key.Escape) { listening = -1; return; }          // anuluj
            if (GameSettings.IsReserved(key)) continue;                  // F1 i F9 są zarezerwowane
            GameSettings.SetKey((GameAction)listening, key);
            listening = -1;
            return;
        }
    }

    void SetMode(int choice)
    {
        GameSettings.PedalModeChoice = choice;
        if (pedals == null) return;
        PedalingMode m = choice == 0 ? (PedalingMode)pedals.GetComponent<HoldPedaling>() : pedals.GetComponent<RhythmPedaling>();
        if (m != null) pedals.SetMode(m);
    }

    /// <summary>Krótki dźwięk po puszczeniu suwaka – żeby od razu słyszeć nową głośność (źródło ignoruje pauzę).</summary>
    void PlayPreview(GameSettings.Volume v)
    {
        if (v == GameSettings.Volume.Voice)
        {
            SpeechSynth.Speak("Hold it steady, CJ!", new[] { "David", "Mark" }, 0);
            return;
        }
        if (preview == null)
        {
            preview = gameObject.AddComponent<AudioSource>();
            preview.spatialBlend = 0f;
            preview.playOnAwake = false;
            preview.ignoreListenerPause = true;
        }
        AudioClip clip;
        float gain;
        switch (v)
        {
            case GameSettings.Volume.Effects: clip = ProcAudio.Clip("shot", () => ProcAudio.Gunshot(0.28f, 1f)); gain = GameSettings.EffectsGain; break;
            case GameSettings.Volume.Ambient: clip = ProcAudio.Clip("thud", () => ProcAudio.Thud(0.3f)); gain = GameSettings.AmbientGain; break;
            case GameSettings.Volume.Pedaling: clip = ProcAudio.Clip("grind_hard", () => ProcAudio.Grind(0.3f, 55f, 0.5f, 0.75f)); gain = GameSettings.PedalingGain; break;
            default: clip = ProcAudio.Clip("thud", () => ProcAudio.Thud(0.3f)); gain = GameSettings.Gain(GameSettings.Volume.Master); break;
        }
        preview.PlayOneShot(clip, Mathf.Clamp01(gain));
    }

    // ------------------------------------------------------------ rysowanie

    void OnGUI()
    {
        GUI.depth = -200;     // nad oknem pomocy (ustawiane przy każdym zdarzeniu OnGUI)
        if (!IsOpen || Event.current.type != EventType.Repaint) return;
        label ??= new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow };
        ComputeScale();

        Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.9f));
        var prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        float x0 = X0;
        Text(new Rect(x0, 40f, PanelW, 70f), "USTAWIENIA", 56, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
        Text(new Rect(x0, 105f, PanelW, 40f), "Gra jest wstrzymana", 26, Dim, TextAnchor.MiddleCenter, FontStyle.Normal);

        // dźwięk
        Text(new Rect(x0 + 40f, 160f, 600f, 40f), "DŹWIĘK", 32, Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
        for (int i = 0; i < 5; i++)
        {
            var v = (GameSettings.Volume)i;
            var r = SliderRect(i);
            Text(new Rect(x0 + 40f, r.y - 4f, 260f, 44f), GameSettings.Label(v), 26, Color.white, TextAnchor.MiddleLeft, FontStyle.Normal);
            float val = GameSettings.Get(v);
            Fill(new Rect(r.x, r.y + 14f, r.width, 8f), new Color(1f, 1f, 1f, 0.2f));
            Fill(new Rect(r.x, r.y + 14f, r.width * val, 8f), Cyan);
            Fill(new Rect(r.x + r.width * val - 9f, r.y + 2f, 18f, 32f), dragSlider == i ? Color.white : Gold);
            Text(new Rect(r.xMax + 20f, r.y - 4f, 90f, 44f), Mathf.RoundToInt(val * 100f) + "%", 26, Dim, TextAnchor.MiddleLeft, FontStyle.Normal);
        }

        // tryb pedałowania
        Text(new Rect(x0 + 40f, 600f, 600f, 40f), "TRYB PEDAŁOWANIA", 32, Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
        bool hold = pedals != null && pedals.Mode is HoldPedaling;
        Button(ModeRect(0), "Przytrzymanie", hold, false);
        Button(ModeRect(1), "Rytm", !hold, false);
        string desc = hold ? "Trzymaj klawisz nogi do końca skoku (jak w oryginale)." : "Klikaj w takt, gdy pasek z boku ekranu zejdzie do środka.";
        Text(new Rect(x0 + 40f, 722f, 620f, 40f), desc, 22, Dim, TextAnchor.MiddleLeft, FontStyle.Normal);

        // klawisze
        Text(new Rect(x0 + 1090f, 160f, 400f, 40f), "KLAWISZE", 32, Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
        for (int i = 0; i < 7; i++)
        {
            var a = (GameAction)i;
            var r = KeyRect(i);
            Text(new Rect(x0 + 760f, r.y, 320f, r.height), GameSettings.Label(a), 26, Color.white, TextAnchor.MiddleRight, FontStyle.Normal);
            Button(r, listening == i ? "naciśnij klawisz…" : GameSettings.KeyName(a), listening == i, true);
        }
        Button(ResetKeysRect, "Przywróć domyślne", false, false);
        Text(new Rect(x0 + 760f, 205f + 7 * 62f + 15f, 320f, 50f), listening >= 0 ? "Esc – anuluj" : "Kliknij klawisz, by go zmienić", 20, Dim, TextAnchor.MiddleRight, FontStyle.Normal);
        Text(new Rect(x0 + 760f, 205f + 8 * 62f + 20f, 660f, 40f), "Stałe: Esc – menu, F1 – pomoc, myszka – kierownica i broń.", 20, Dim, TextAnchor.MiddleRight, FontStyle.Normal);

        Button(ResumeRect, "Wznów  (Esc)", true, false);
        Button(HelpRect, "Pomoc  (F1)", false, false);

        GUI.matrix = prevMatrix;
    }

    void Button(Rect r, string text, bool active, bool small)
    {
        Fill(r, active ? new Color(Gold.r, Gold.g, Gold.b, 0.22f) : new Color(1f, 1f, 1f, 0.08f));
        Frame(r, 2f, active ? Gold : new Color(1f, 1f, 1f, 0.35f));
        Text(r, text, small ? 24 : 28, active ? Gold : Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
    }

    void Text(Rect r, string s, int size, Color c, TextAnchor anchor, FontStyle style)
    {
        label.fontSize = size;
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
