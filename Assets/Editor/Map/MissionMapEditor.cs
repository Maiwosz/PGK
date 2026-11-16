using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Inspektor obiektu MissionMap: stan mapy i przycisk wymuszający regenerację.</summary>
[CustomEditor(typeof(MissionMap))]
public class MissionMapEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var map = (MissionMap)target;
        bool generated = File.Exists(MissionMapBuilder.MapPrefabPath);
        string reason = MissionMapAutoBuild.Reason();

        if (!generated)
            EditorGUILayout.HelpBox("Mapa nie została jeszcze wygenerowana. Zbuduje się sama (Tools/Mission/Auto-build Map) albo użyj przycisku poniżej.", MessageType.Warning);
        else if (reason != null)
            EditorGUILayout.HelpBox("Mapa jest nieaktualna: " + reason + ".", MessageType.Info);
        else
            EditorGUILayout.HelpBox("Mapa jest aktualna" + (map.HasContent ? " (podgląd wstawiony do sceny, nie zapisuje się z nią)." : "."), MessageType.None);

        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button(generated ? "Wygeneruj mapę ponownie" : "Wygeneruj mapę", GUILayout.Height(28)))
                MissionMapAutoBuild.ForceRebuild();
        }

        bool auto = MissionMapAutoBuild.AutoEnabled;
        bool next = EditorGUILayout.ToggleLeft("Buduj mapę automatycznie (po klonie i zmianie przepisu/kodu)", auto);
        if (next != auto) MissionMapAutoBuild.AutoEnabled = next;
    }
}
