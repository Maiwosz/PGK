using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Mapa misji (prefab Assets/Mission/Generated/Resources/MissionMap.prefab) nie jest w repozytorium – powstaje z przepisu
/// (Assets/Mission/Recipe) przez MissionMapBuilder. Ten skrypt buduje ją sam:
///  - po sklonowaniu projektu (brak prefabu mapy),
///  - gdy zmienił się przepis albo kod budujący mapę (np. po pullu) – porównanie odcisku zapisanego w Library/.
/// Budowa nie dotyka scen gry (tylko zasoby w Generated), więc nic nie pyta o zapis.
/// Wymuszenie ręczne: Tools/Mission/Rebuild Map albo przycisk na obiekcie MissionMap. Wyłączenie automatu: Tools/Mission/Auto-build Map.
/// </summary>
[InitializeOnLoad]
public static class MissionMapAutoBuild
{
    const string RecipeDir = "Assets/Mission/Recipe";
    const string AutoPref = "PGK_MissionAutoBuild";
    const string TriedKey = "PGK_MissionAutoBuildTried";
    const string StampFile = "Library/PGK_MissionMapStamp.txt";

    // Kod, od którego zależy zawartość mapy.
    static readonly string[] SourceDirs = { "Assets/Editor/Map", "Assets/Scripts/Map" };

    static MissionMapAutoBuild()
    {
        if (Application.isBatchMode) return;
        EditorApplication.update += WaitAndCheck;
    }

    public static bool AutoEnabled
    {
        get => EditorPrefs.GetBool(AutoPref, true);
        set => EditorPrefs.SetBool(AutoPref, value);
    }

    [MenuItem("Tools/Mission/Auto-build Map")]
    static void ToggleAuto() => AutoEnabled = !AutoEnabled;

    [MenuItem("Tools/Mission/Auto-build Map", true)]
    static bool ToggleAutoValidate()
    {
        Menu.SetChecked("Tools/Mission/Auto-build Map", AutoEnabled);
        return true;
    }

    [MenuItem("Tools/Mission/Rebuild Map")]
    public static void ForceRebuild() => Run("wymuszone ręcznie");

    static void WaitAndCheck()
    {
        // Czekamy, aż edytor skończy import i kompilację (po klonie trwa to dłuższą chwilę).
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EditorApplication.update -= WaitAndCheck;

        string reason = Reason();
        if (reason == null) return;
        if (!AutoEnabled)
        {
            Debug.LogWarning($"[MissionMapAutoBuild] Mapa wymaga przebudowy ({reason}), ale automat jest wyłączony – użyj Tools/Mission/Rebuild Map.");
            return;
        }
        // Jedna próba na sesję edytora: błąd budowy nie może zapętlać się po każdym przeładowaniu skryptów.
        if (SessionState.GetBool(TriedKey, false)) return;
        SessionState.SetBool(TriedKey, true);
        Run(reason);
    }

    static void Run(string reason)
    {
        if (!Directory.Exists(RecipeDir))
        {
            Debug.LogError("[MissionMapAutoBuild] Brak przepisu mapy (" + RecipeDir + ").");
            return;
        }
        Debug.Log($"[MissionMapAutoBuild] Buduję mapę misji ({reason}) – potrwa około dwóch minut...");
        EditorUtility.DisplayProgressBar("Mapa misji", "Generowanie mapy z przepisu GTA... (około 2 min)", 0.3f);
        try { MissionMapBuilder.BuildAll(); }
        finally { EditorUtility.ClearProgressBar(); }
    }

    /// <summary>Powód przebudowy albo null, gdy mapa jest aktualna.</summary>
    public static string Reason()
    {
        if (!Directory.Exists(RecipeDir)) return null;     // brak przepisu – nie ma z czego budować
        if (!File.Exists(MissionMapBuilder.MapPrefabPath)) return "brak wygenerowanej mapy";
        if (!File.Exists(StampFile)) return "brak odcisku poprzedniej budowy";
        if (File.ReadAllText(StampFile).Trim() != ComputeStamp()) return "zmienił się przepis albo kod mapy";
        return null;
    }

    /// <summary>Zapisuje odcisk przepisu i kodu mapy – wołane po udanej budowie.</summary>
    public static void WriteStamp()
    {
        try { File.WriteAllText(StampFile, ComputeStamp()); }
        catch (System.Exception e) { Debug.LogWarning("[MissionMapAutoBuild] Nie zapisano odcisku: " + e.Message); }
    }

    static string ComputeStamp()
    {
        using var md5 = MD5.Create();
        var parts = new StringBuilder();
        foreach (var f in Files(RecipeDir, "*")) parts.Append(Path.GetFileName(f)).Append(':').Append(Hash(md5, File.ReadAllBytes(f), f.EndsWith(".json"))).Append(';');
        foreach (var dir in SourceDirs)
            foreach (var f in Files(dir, "*.cs")) parts.Append(Path.GetFileName(f)).Append(':').Append(Hash(md5, File.ReadAllBytes(f), true)).Append(';');
        return Hash(md5, Encoding.UTF8.GetBytes(parts.ToString()), false);
    }

    static IEnumerable<string> Files(string dir, string pattern)
    {
        if (!Directory.Exists(dir)) yield break;
        var files = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly);
        System.Array.Sort(files, System.StringComparer.Ordinal);
        foreach (var f in files)
            if (!f.EndsWith(".meta")) yield return f;
    }

    // Dla tekstu pomijamy '\r', żeby CRLF/LF (autocrlf w gicie) nie zmieniały odcisku między komputerami.
    static string Hash(MD5 md5, byte[] data, bool text)
    {
        if (text)
        {
            var list = new List<byte>(data.Length);
            foreach (byte b in data) if (b != 13) list.Add(b);
            data = list.ToArray();
        }
        var h = md5.ComputeHash(data);
        var sb = new StringBuilder(h.Length * 2);
        foreach (byte b in h) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
