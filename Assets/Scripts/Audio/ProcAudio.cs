using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generator dźwięków w kodzie (bez żadnych plików): krótkie klipy (dzwonek, stuk, strzał) i strumienie do pętli
/// (silnik, wiatr). Klipy buduje się raz i trzyma w pamięci podręcznej pod nazwą.
/// </summary>
public static class ProcAudio
{
    public const int Rate = 44100;
    static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => cache.Clear();

    /// <summary>Klip z pamięci podręcznej albo zbudowany przez fabrykę.</summary>
    public static AudioClip Clip(string name, Func<AudioClip> make)
    {
        if (cache.TryGetValue(name, out var c) && c != null) return c;
        c = make();
        cache[name] = c;
        return c;
    }

    static AudioClip Make(string name, float seconds, Func<float, int, float> sample)
    {
        int n = Mathf.CeilToInt(seconds * Rate);
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = sample(i / (float)Rate, i);
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>
    /// Zgrzyt (błąd pedałowania): jasny szum poszatkowany szorstkim pulsowaniem (rough Hz) z nałożonymi metalicznymi, nieharmonicznymi
    /// tonami – jak łańcuch przeskakujący po zębatce. tone 0..1 – jasność szumu, level – głośność.
    /// </summary>
    public static AudioClip Grind(float seconds, float rough, float tone, float level)
    {
        var rng = new System.Random(Mathf.RoundToInt(rough * 10f));
        float prev = 0f, lp = 0f, phase = 0f;
        return Make("grind", seconds, (t, i) =>
        {
            float n = (float)(rng.NextDouble() * 2.0 - 1.0);
            float hp = n - prev; prev = n;
            lp += (hp - lp) * Mathf.Clamp(tone, 0.05f, 1f);
            phase += rough * (1f + 0.15f * Mathf.Sin(t * 53f)) / Rate;
            if (phase >= 1f) phase -= 1f;
            float chop = phase < 0.5f ? 1f : 0.2f;
            float metal = (Mathf.Sin(2f * Mathf.PI * 187f * t) > 0f ? 1f : -1f) * 0.22f
                        + (Mathf.Sin(2f * Mathf.PI * 263f * t) > 0f ? 1f : -1f) * 0.16f;
            float env = Mathf.Min(1f, t / 0.005f) * Mathf.Exp(-3.2f * t / seconds);
            return Mathf.Clamp((lp * 1.5f + metal) * chop * env * level, -1f, 1f);
        });
    }

    /// <summary>Niski, tępy "klunk" z szumem (zła noga, kliknięcie bez sensu) – bez wyraźnej wysokości, wyraźnie różny od dzwonka.</summary>
    public static AudioClip Thud(float seconds)
    {
        var rng = new System.Random(77);
        float lp = 0f;
        return Make("thud", seconds, (t, i) =>
        {
            float f = Mathf.Lerp(120f, 48f, Mathf.Clamp01(t / 0.1f));
            float tone = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 14f);
            lp += ((float)(rng.NextDouble() * 2.0 - 1.0) - lp) * 0.25f;
            return (tone * 0.9f + lp * 0.5f * Mathf.Exp(-t * 40f)) * Mathf.Min(1f, t / 0.002f) * 0.7f;
        });
    }

    /// <summary>Krótki suchy klik (np. koniec skoku w trybie przytrzymania).</summary>
    public static AudioClip Tick(float seconds)
    {
        var rng = new System.Random(31);
        float prev = 0f;
        return Make("tick", seconds, (t, i) =>
        {
            float n = (float)(rng.NextDouble() * 2.0 - 1.0);
            float hp = n - prev; prev = n;            // prosty filtr górnoprzepustowy: jasny trzask
            return hp * Mathf.Exp(-t * 180f) * 0.5f;
        });
    }

    /// <summary>
    /// Strzał: trzask z jasnego szumu, niski "kop" (sinus opadający od 160 do 45 Hz) i ogon. punch 0..1 – siła kopa.
    /// </summary>
    public static AudioClip Gunshot(float seconds, float punch)
    {
        var rng = new System.Random(4242);
        float lp = 0f, prev = 0f;
        return Make("gunshot", seconds, (t, i) =>
        {
            float n = (float)(rng.NextDouble() * 2.0 - 1.0);
            float crack = (n - prev) * Mathf.Exp(-t * 90f); prev = n;
            lp += (n - lp) * 0.12f;
            float tail = lp * Mathf.Exp(-t * 14f) * 0.8f;
            float f = Mathf.Lerp(160f, 45f, Mathf.Clamp01(t / 0.08f));
            float kick = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 22f) * punch;
            return Mathf.Clamp((crack * 0.7f + tail + kick) * 0.6f, -1f, 1f);
        });
    }

    /// <summary>Nieskończony strumień (pętla) generowany w locie przez callback wątku audio.</summary>
    public static AudioClip Stream(string name, AudioClip.PCMReaderCallback fill)
        => AudioClip.Create(name, Rate, 1, Rate, true, fill);
}
