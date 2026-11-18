using UnityEngine;

/// <summary>Drobne wspólne narzędzia rysowania dla komponentów HUD (IMGUI).</summary>
public static class HudDraw
{
    public static void Fill(Rect r, Color c)
    {
        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    /// <summary>Wskaźniki mają być ukryte: koniec misji (ekran końcowy przejmuje HUD) albo rowerzysta leży po upadku.</summary>
    public static bool Hidden(BikeController bike) => MissionOver() || (bike != null && bike.Fallen);

    /// <summary>Misja zakończona (wygrana albo przegrana).</summary>
    public static bool MissionOver()
    {
        var mission = MissionManager.Instance;
        return mission != null && mission.CurrentPhase != MissionManager.Phase.Running;
    }
}
