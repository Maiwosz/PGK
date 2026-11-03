using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Dokłada do otwartej sceny MissionMap rozgrywkę: gangsterów na pierwszym wagonie (prefab ładunku przypisany do pociągu),
/// wieżyczkę za kamerą roweru, menedżera misji i HUD misji. Wywoływane też na końcu budowy mapy;
/// osobno (Tools/Mission/Add Gameplay) pozwala poprawiać rozgrywkę bez przebudowy całej mapy.
/// </summary>
public static class MissionGameplayBuilder
{
    const int GangsterCount = 4;

    [MenuItem("Tools/Mission/Add Gameplay")]
    static void AddToOpenScene()
    {
        Apply();
        EditorSceneManager.SaveOpenScenes();
    }

    public static void Apply()
    {
        var train = Object.FindFirstObjectByType<TrainController>();
        var bike = Object.FindFirstObjectByType<BikeController>();
        var path = Object.FindFirstObjectByType<TrackPath>();
        if (train == null || bike == null || path == null)
        {
            Debug.LogError("[MissionGameplayBuilder] Brak pociągu, roweru lub ścieżki – otwórz MissionMap albo zbuduj mapę.");
            return;
        }

        var yellow = MissionMapBuilder.Mat("GangYellow", new Color(1f, 0.85f, 0.1f), 0f, 0.25f);
        var dark = MissionMapBuilder.Mat("GangDark", new Color(0.08f, 0.08f, 0.09f), 0f, 0.2f);
        var tracer = MakeTracerMaterial();
        var squad = CreateSquadPrefab(yellow, dark);
        MissionMapBuilder.SetRef(train, "cargoPrefab", squad);

        // Stare obiekty misji (przy ponownym uruchomieniu).
        var old = GameObject.Find("Mission");
        if (old != null) Object.DestroyImmediate(old);

        var go = new GameObject("Mission");
        var cam = bike.GetComponentInChildren<Camera>();

        var turret = go.AddComponent<AutoTurret>();
        MissionMapBuilder.SetRef(turret, "anchor", cam != null ? cam.transform : bike.transform);
        MissionMapBuilder.SetRef(turret, "bikeRoot", bike.transform);
        MissionMapBuilder.SetRef(turret, "bike", bike);
        MissionMapBuilder.SetRef(turret, "tracerMaterial", tracer);

        var mission = go.AddComponent<MissionManager>();
        MissionMapBuilder.SetRef(mission, "bike", bike);
        MissionMapBuilder.SetRef(mission, "train", train);
        MissionMapBuilder.SetRef(mission, "turret", turret);
        MissionMapBuilder.SetRef(mission, "path", path);

        var hud = go.AddComponent<MissionHud>();
        MissionMapBuilder.SetRef(hud, "mission", mission);
        MissionMapBuilder.SetRef(hud, "turret", turret);
        MissionMapBuilder.SetRef(hud, "bike", bike);

        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[MissionGameplayBuilder] Dodano misję: wieżyczka, menedżer, HUD, gangsterzy w prefabie ładunku.");
    }

    static Material MakeTracerMaterial()
    {
        const string path = "Assets/Materials/Mission/Tracer.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = Color.white;
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Czterech żółtych "gangsterów" (prostopadłościany) na dachu wagonu; pivot prefabu = środek wagonu.</summary>
    static GameObject CreateSquadPrefab(Material yellow, Material dark)
    {
        var root = new GameObject("GangSquad");
        const float roofTop = 4.8f;                      // górna krawędź dachu wagonu (patrz CreateTrainCar)
        float[] zs = { -6.5f, -2.2f, 2.2f, 6.5f };

        for (int i = 0; i < GangsterCount; i++)
        {
            var g = new GameObject("Gangster" + (i + 1));
            g.transform.SetParent(root.transform, false);
            g.transform.localPosition = new Vector3(i % 2 == 0 ? 0.55f : -0.55f, roofTop, zs[i % zs.Length]);

            var body = Prim(g.transform, "Body", new Vector3(0f, 0.85f, 0f), new Vector3(0.55f, 1.7f, 0.45f), yellow);
            var head = Prim(g.transform, "Head", new Vector3(0f, 1.82f, 0f), new Vector3(0.34f, 0.3f, 0.34f), yellow);
            var gun = Prim(g.transform, "Gun", new Vector3(0.32f, 1.15f, 0.3f), new Vector3(0.1f, 0.12f, 0.6f), dark);

            var aim = new GameObject("AimPoint").transform;
            aim.SetParent(g.transform, false);
            aim.localPosition = new Vector3(0f, 1.2f, 0f);

            var col = g.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.95f, 0f);
            col.size = new Vector3(0.6f, 1.9f, 0.5f);

            var gangster = g.AddComponent<Gangster>();
            MissionMapBuilder.SetRef(gangster, "aimPoint", aim);
            MissionMapBuilder.SetRefs(gangster, "renderers", new Object[] { body.GetComponent<Renderer>(), head.GetComponent<Renderer>(), gun.GetComponent<Renderer>() });
            MissionMapBuilder.SetRefs(gangster, "colliders", new Object[] { col });
        }

        string prefabPath = MissionMapBuilder.GenDir + "/Prefabs/GangSquad.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject Prim(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }
}
