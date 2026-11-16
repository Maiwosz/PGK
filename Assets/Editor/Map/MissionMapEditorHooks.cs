using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Podgląd mapy w edytorze: obiekt MissionMap w otwartej scenie dostaje kopię wygenerowanego prefabu (MapInstance),
/// ale oznaczoną DontSaveInEditor – więc nie trafia do pliku sceny i scena nie ma odwołań do lokalnych, generowanych zasobów.
/// Podgląd odnawia się po otwarciu sceny, przeładowaniu skryptów, wyjściu z trybu Play i po przebudowie mapy.
/// </summary>
[InitializeOnLoad]
public static class MissionMapEditorHooks
{
    static MissionMapEditorHooks()
    {
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += RefreshPreviews;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.delayCall += RefreshPreviews;     // po przeładowaniu domeny / starcie edytora
    }

    static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += RefreshPreviews;
    }

    /// <summary>Wstawia (albo odnawia) podgląd mapy we wszystkich otwartych scenach z obiektem MissionMap.</summary>
    public static void RefreshPreviews()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        foreach (var map in Object.FindObjectsByType<MissionMap>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!map.gameObject.scene.IsValid() || EditorUtility.IsPersistent(map)) continue;   // tylko obiekty w scenach
            bool had = map.transform.Find(MissionMap.InstanceName) != null;
            if (!map.Load()) continue;
            if (!had) Hide(map.Content);
        }
    }

    /// <summary>Zdejmuje podglądy (np. na czas budowy, żeby ich kolidery nie przeszkadzały).</summary>
    public static void RemovePreviews()
    {
        foreach (var map in Object.FindObjectsByType<MissionMap>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!map.gameObject.scene.IsValid() || EditorUtility.IsPersistent(map)) continue;
            map.Unload();
        }
    }

    /// <summary>Każdy obiekt podglądu osobno (flaga nie dziedziczy się po rodzicu), żeby nic nie wpadło do pliku sceny.</summary>
    static void Hide(Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            t.gameObject.hideFlags |= HideFlags.DontSaveInEditor;
    }
}
