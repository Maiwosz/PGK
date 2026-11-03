using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;

/// <summary>
/// Wypowiada tekst syntezatorem mowy systemu (bez żadnych plików dźwiękowych): Windows – SAPI przez PowerShell,
/// macOS – "say", Linux – "espeak" (jeśli jest). Mowa idzie w osobnym procesie, więc nie blokuje gry;
/// nowa wypowiedź przerywa poprzednią. Brak syntezatora nie psuje gry – zostają napisy.
/// </summary>
public static class SpeechSynth
{
    static Process current;
    static bool hooked;

    /// <param name="text">Tekst (angielski – wybieramy angielski głos).</param>
    /// <param name="voices">Preferowane fragmenty nazw głosów, np. "David", "Mark"; potem dowolny męski angielski.</param>
    /// <param name="rate">Tempo -10..10 (0 = normalne).</param>
    public static void Speak(string text, string[] voices = null, int rate = 0)
    {
        Stop();
        if (!hooked) { hooked = true; Application.quitting += Stop; }
        try
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            string names = voices == null ? "" : string.Join(",", Array.ConvertAll(voices, v => "'" + v.Replace("'", "''") + "'"));
            string script =
                "Add-Type -AssemblyName System.Speech\n" +
                "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer\n" +
                "$all = @($s.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -like 'en-*' })\n" +
                "$pick = $null\n" +
                "foreach ($n in @(" + names + ")) { $pick = $all | Where-Object { $_.VoiceInfo.Name -like ('*' + $n + '*') } | Select-Object -First 1; if ($pick) { break } }\n" +
                "if (-not $pick) { $pick = $all | Where-Object { $_.VoiceInfo.Gender -eq 'Male' } | Select-Object -First 1 }\n" +
                "if (-not $pick) { $pick = $all | Select-Object -First 1 }\n" +
                "if ($pick) { $s.SelectVoice($pick.VoiceInfo.Name) }\n" +
                "$s.Rate = " + Mathf.Clamp(rate, -10, 10) + "\n" +
                "$s.Volume = 100\n" +
                "$s.Speak('" + text.Replace("'", "''") + "')\n";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            current = Process.Start(psi);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            var psi = new ProcessStartInfo("say", "-r " + (175 + rate * 10) + " \"" + text.Replace("\"", "") + "\"") { CreateNoWindow = true, UseShellExecute = false };
            current = Process.Start(psi);
#else
            var psi = new ProcessStartInfo("espeak", "-s " + (160 + rate * 10) + " \"" + text.Replace("\"", "") + "\"") { CreateNoWindow = true, UseShellExecute = false };
            current = Process.Start(psi);
#endif
        }
        catch (Exception e)
        {
            UnityEngine.Debug.Log("[SpeechSynth] Brak syntezatora mowy: " + e.Message);
            current = null;
        }
    }

    /// <summary>Przerywa trwającą wypowiedź.</summary>
    public static void Stop()
    {
        try
        {
            if (current != null && !current.HasExited) current.Kill();
        }
        catch { /* proces mógł się już zakończyć */ }
        current = null;
    }
}
