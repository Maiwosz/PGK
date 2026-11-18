using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Build wydaniowy (Windows 64-bit): tylko scena Game, bez trybu deweloperskiego, do Builds/Release/PGK.exe.
/// Menu: Tools/Build/Release (Windows). Wynik zapisuje też do Logs/build_release.txt.
/// Mapa misji powstaje z przepisu (Assets/Mission/Recipe) jako prefab Resources/MissionMap, który trafia do buildu –
/// przed budową sprawdzamy, że ten prefab istnieje (inaczej gra uruchomiłaby się bez mapy).
/// </summary>
public static class ReleaseBuild
{
    const string Scene = "Assets/Scenes/Game.unity";
    const string OutDir = "Builds/Release";
    const string MapPrefab = "Assets/Mission/Generated/Resources/MissionMap.prefab";

    [MenuItem("Tools/Build/Release (Windows)")]
    public static void Build() => BuildAndReport();

    /// <summary>Zwraca podsumowanie (też do wywołania z linii poleceń / narzędzi).</summary>
    public static string BuildAndReport()
    {
        var log = new StringBuilder();
        string result = Run(log);
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/build_release.txt", log.ToString());
        Debug.Log("[ReleaseBuild] " + result);
        return result;
    }

    static string Run(StringBuilder log)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "BŁĄD: wyjdź z trybu Play przed budową.";
        if (!File.Exists(MapPrefab)) return "BŁĄD: brak " + MapPrefab + " – uruchom Tools/Mission/Rebuild Map.";
        if (!File.Exists(Scene)) return "BŁĄD: brak sceny " + Scene;

        if (Directory.Exists(OutDir)) Directory.Delete(OutDir, true);
        Directory.CreateDirectory(OutDir);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { Scene },
            locationPathName = OutDir + "/PGK.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary s = report.summary;
        log.AppendLine($"wynik: {s.result}, czas: {s.totalTime.TotalSeconds:0} s, rozmiar: {s.totalSize / 1048576f:0.0} MB, błędy: {s.totalErrors}, ostrzeżenia: {s.totalWarnings}");
        log.AppendLine("wyjście: " + Path.GetFullPath(OutDir));
        foreach (var step in report.steps)
            foreach (var msg in step.messages)
                if (msg.type == LogType.Error || msg.type == LogType.Exception)
                    log.AppendLine("  [" + msg.type + "] " + msg.content);
        return s.result == BuildResult.Succeeded
            ? $"OK: {s.totalSize / 1048576f:0.0} MB, {s.totalTime.TotalSeconds:0} s, {Path.GetFullPath(OutDir)}"
            : $"BŁĄD: {s.result}, błędy {s.totalErrors} (szczegóły w Logs/build_release.txt)";
    }
}
