using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Akcje z przypisywalnymi klawiszami (klawisze zarezerwowane: Esc – menu, F1 – pomoc, F9 – debug).</summary>
public enum GameAction { PedalLeft, PedalRight, GearHeavier, GearLighter, Brake, Reverse, Restart }

/// <summary>
/// Ustawienia gracza zapisywane w PlayerPrefs: głośności, wybrany tryb pedałowania i przypisanie klawiszy.
/// Reszta gry pyta o nie tutaj (np. <see cref="GetKey"/>, <see cref="EffectsGain"/>), więc zmiany w menu działają od razu.
/// </summary>
public static class GameSettings
{
    /// <summary>Kategorie głośności: ogólna plus cztery grupy dźwięków.</summary>
    public enum Volume { Master, Effects, Pedaling, Ambient, Voice }

    static readonly string[] VolumeKeys = { "vol_master", "vol_effects", "vol_pedaling", "vol_ambient", "vol_voice" };
    static readonly float[] VolumeDefaults = { 0.8f, 0.8f, 0.8f, 0.6f, 0.9f };
    static readonly float[] volumes = new float[5];

    static readonly Key[] KeyDefaults = { Key.A, Key.D, Key.LeftShift, Key.LeftCtrl, Key.Space, Key.S, Key.R };
    static readonly Key[] keys = new Key[KeyDefaults.Length];
    const string KeyPrefix = "key_";
    const string ModeKey = "pedal_mode";
    static bool loaded;

    /// <summary>Zmiana dowolnego ustawienia (do odświeżenia dźwięków, opisów).</summary>
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => loaded = false;

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        for (int i = 0; i < volumes.Length; i++) volumes[i] = PlayerPrefs.GetFloat(VolumeKeys[i], VolumeDefaults[i]);
        for (int i = 0; i < keys.Length; i++)
        {
            Key k = (Key)PlayerPrefs.GetInt(KeyPrefix + i, (int)KeyDefaults[i]);
            keys[i] = k == Key.None ? KeyDefaults[i] : k;
        }
    }

    // ---- głośność

    public static float Get(Volume v) { Load(); return volumes[(int)v]; }

    public static void Set(Volume v, float value)
    {
        Load();
        volumes[(int)v] = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(VolumeKeys[(int)v], volumes[(int)v]);
        Changed?.Invoke();
    }

    /// <summary>Głośność wypadkowa kategorii (ogólna x kategoria).</summary>
    public static float Gain(Volume v) => Get(Volume.Master) * Get(v);
    public static float EffectsGain => Gain(Volume.Effects);
    public static float PedalingGain => Gain(Volume.Pedaling);
    public static float AmbientGain => Gain(Volume.Ambient);
    public static float VoiceGain => Gain(Volume.Voice);

    public static string Label(Volume v)
    {
        switch (v)
        {
            case Volume.Master: return "Głośność ogólna";
            case Volume.Effects: return "Strzały i efekty";
            case Volume.Pedaling: return "Dźwięki pedałowania";
            case Volume.Ambient: return "Rower i pociąg";
            default: return "Głos Big Smoke'a";
        }
    }

    // ---- tryb pedałowania

    /// <summary>-1 = bez wyboru gracza (decyduje inspektor), 0 = przytrzymanie, 1 = rytm.</summary>
    public static int PedalModeChoice
    {
        get => PlayerPrefs.GetInt(ModeKey, -1);
        set { PlayerPrefs.SetInt(ModeKey, value); Changed?.Invoke(); }
    }

    /// <summary>Tryb wybrany przez gracza na danym rowerze albo null, gdy gracz niczego nie wybierał.</summary>
    public static PedalingMode ChosenMode(PedalDrive drive)
    {
        int c = PedalModeChoice;
        if (c == 0) return drive.GetComponent<HoldPedaling>();
        if (c == 1) return drive.GetComponent<RhythmPedaling>();
        return null;
    }

    // ---- klawisze

    public static Key GetKey(GameAction a) { Load(); return keys[(int)a]; }

    /// <summary>Przypisuje klawisz; jeśli miała go inna akcja, obie się zamieniają.</summary>
    public static void SetKey(GameAction a, Key key)
    {
        Load();
        int idx = (int)a;
        for (int i = 0; i < keys.Length; i++)
            if (i != idx && keys[i] == key) { keys[i] = keys[idx]; PlayerPrefs.SetInt(KeyPrefix + i, (int)keys[i]); }
        keys[idx] = key;
        PlayerPrefs.SetInt(KeyPrefix + idx, (int)key);
        Changed?.Invoke();
    }

    public static void ResetKeys()
    {
        Load();
        for (int i = 0; i < keys.Length; i++) { keys[i] = KeyDefaults[i]; PlayerPrefs.DeleteKey(KeyPrefix + i); }
        Changed?.Invoke();
    }

    public static void Save() => PlayerPrefs.Save();

    public static bool IsPressed(Keyboard kb, GameAction a) => kb != null && kb[GetKey(a)].isPressed;
    public static bool WasPressed(Keyboard kb, GameAction a) => kb != null && kb[GetKey(a)].wasPressedThisFrame;

    /// <summary>Klawisze, których nie wolno przypisać (zarezerwowane).</summary>
    public static bool IsReserved(Key k) => k == Key.Escape || k == Key.F1 || k == Key.F9 || k == Key.None || k == Key.IMESelected;

    public static string Label(GameAction a)
    {
        switch (a)
        {
            case GameAction.PedalLeft: return "Pedał lewy";
            case GameAction.PedalRight: return "Pedał prawy";
            case GameAction.GearHeavier: return "Bieg cięższy";
            case GameAction.GearLighter: return "Bieg lżejszy";
            case GameAction.Brake: return "Hamulec / wstawanie";
            case GameAction.Reverse: return "Cofanie";
            default: return "Restart misji";
        }
    }

    /// <summary>Czytelna nazwa klawisza po polsku (dla popularnych) albo nazwa z Input Systemu.</summary>
    public static string KeyName(Key k)
    {
        switch (k)
        {
            case Key.Space: return "Spacja";
            case Key.LeftShift: return "Lewy Shift";
            case Key.RightShift: return "Prawy Shift";
            case Key.LeftCtrl: return "Lewy Ctrl";
            case Key.RightCtrl: return "Prawy Ctrl";
            case Key.LeftAlt: return "Lewy Alt";
            case Key.RightAlt: return "Prawy Alt";
            case Key.Enter: return "Enter";
            case Key.Tab: return "Tab";
            case Key.Backspace: return "Backspace";
            case Key.UpArrow: return "Strzałka w górę";
            case Key.DownArrow: return "Strzałka w dół";
            case Key.LeftArrow: return "Strzałka w lewo";
            case Key.RightArrow: return "Strzałka w prawo";
        }
        string s = k.ToString();
        if (s.StartsWith("Digit")) return s.Substring(5);
        return s.Length == 1 ? s : s;
    }

    public static string KeyName(GameAction a) => KeyName(GetKey(a));
}
