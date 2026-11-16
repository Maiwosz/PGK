using UnityEditor;

/// <summary>Przełącznik nagrywania telemetrii jazdy (Tools/Telemetry/Recording). Działa od następnego wejścia w Play mode.</summary>
public static class TelemetryMenu
{
    const string Path = "Tools/Telemetry/Recording";

    [MenuItem(Path)]
    static void Toggle() => BikeTelemetry.Enabled = !BikeTelemetry.Enabled;

    [MenuItem(Path, true)]
    static bool Validate()
    {
        Menu.SetChecked(Path, BikeTelemetry.Enabled);
        return !EditorApplication.isPlaying;
    }
}
