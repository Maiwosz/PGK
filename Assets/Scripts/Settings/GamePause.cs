using UnityEngine;

/// <summary>
/// Wspólna pauza gry: okno pomocy i menu ustawień mogą być otwarte jednocześnie, a gra wznawia się dopiero,
/// gdy zamknie się ostatnie z nich. Pauza zatrzymuje czas (Time.timeScale = 0) i wycisza dźwięki gry
/// (źródła z ignoreListenerPause grają dalej – np. podgląd głośności w menu).
/// </summary>
public static class GamePause
{
    static int holders;
    static float previousScale = 1f;

    public static bool IsPaused => holders > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { holders = 0; AudioListener.pause = false; }

    public static void Push()
    {
        if (holders++ > 0) return;
        previousScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        AudioListener.pause = true;
    }

    public static void Pop()
    {
        if (holders == 0) return;
        if (--holders > 0) return;
        Time.timeScale = previousScale;
        AudioListener.pause = false;
    }
}
