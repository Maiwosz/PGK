using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Buduje scenę misji z "przepisu" (Assets/Mission/Recipe, wygenerowanego przez Tools/GtaExtract):
/// teren z mapy wysokości (2 kafelki), tor z prefabów wzdłuż ścieżki, obiekty jako proste bryły, morze i rower na starcie.
/// Menu: Tools/Mission/Build Map. Nadpisuje scenę Assets/Scenes/MissionMap.unity.
/// </summary>
public static class MissionMapBuilder
{
    const string RecipeDir = "Assets/Mission/Recipe";
    internal const string GenDir = "Assets/Mission/Generated";
    const string MatDir = "Assets/Materials/Mission";
    const string ScenePath = "Assets/Scenes/MissionMap.unity";
    const string BikePrefabPath = "Assets/Prefabs/Bike.prefab";
    const string GunPrefabPath = "Assets/Prefabs/Gun.prefab";

    const float SegmentLength = 4f;

    // Niskie stopnie (krawężniki, progi) dostają w kolizji skos, żeby rower mógł na nie wjechać zamiast uderzać w ścianę.
    const float StepMaxHeight = 0.22f;      // wyższe ścianki zostają pionowe
    const float StepMinHeight = 0.03f;      // niższych nie ruszamy
    const float StepRampAngle = 30f;        // nachylenie skosu (stopnie)
    static readonly string[] ClassNames = { "Neutral", "Asphalt", "Grass", "Dirt", "Sand", "Rock" };
    static readonly Color[] ClassColors =
    {
        new Color(0.46f, 0.46f, 0.40f), new Color(0.20f, 0.20f, 0.22f), new Color(0.27f, 0.45f, 0.20f),
        new Color(0.50f, 0.38f, 0.24f), new Color(0.80f, 0.72f, 0.50f), new Color(0.42f, 0.40f, 0.40f),
    };

    class Mats { public Material TrainLoco, TrainStripe, TrainDark, TrainRoof; public Material[] TrainWagons; public Material BridgeSteel, Ballast, Sleeper, Steel, Concrete, TunnelConcrete, Fence, Station, Water; public Material[] Building; }

    [MenuItem("Tools/Mission/Build Map")]
    public static void BuildAll()
    {
        EnsureFolder(GenDir);
        EnsureFolder(GenDir + "/Terrain");
        EnsureFolder(GenDir + "/Meshes");
        EnsureFolder(GenDir + "/Prefabs");
        EnsureFolder(GenDir + "/Textures");
        EnsureFolder(MatDir);

        var route = JsonUtility.FromJson<RouteData>(File.ReadAllText(RecipeDir + "/route.json"));
        var meta = JsonUtility.FromJson<TerrainMeta>(File.ReadAllText(RecipeDir + "/terrain.json"));

        Mats mats = CreateMaterials();
        GameObject rail = CreateRailPrefab(mats);
        GameObject deck = CreateBridgePrefab(mats);
        GameObject tunnel = CreateTunnelPrefab(mats);
        Terrain[] terrains = null;
        TerrainData[] terrainData = CreateTerrainData(meta, route);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            Object.DestroyImmediate(cam.gameObject);

        terrains = CreateTerrains(meta, terrainData);

        // Morze.
        var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "Sea";
        Object.DestroyImmediate(water.GetComponent<Collider>());
        water.transform.position = new Vector3(meta.minX + meta.tile * meta.cell * 0.5f, 0f, meta.minY + meta.tile * meta.cell);
        water.transform.localScale = new Vector3(meta.tile * meta.cell * 1.6f / 10f, 1f, meta.tile * meta.cell * 2.6f / 10f);
        water.GetComponent<Renderer>().sharedMaterial = mats.Water;
        water.isStatic = true;

        // Ścieżka toru + generator.
        var trackGo = new GameObject("Track");
        var path = trackGo.AddComponent<TrackPath>();
        int n = route.pts.Length / 3;
        var pts = new Vector3[n];
        var kinds = new byte[n];
        for (int i = 0; i < n; i++)
        {
            pts[i] = new Vector3(route.pts[i * 3], route.pts[i * 3 + 1], route.pts[i * 3 + 2]);
            kinds[i] = (byte)route.kinds[i];
        }
        path.Set(pts, kinds);

        var gen = trackGo.AddComponent<RailTrackGenerator>();
        SetRef(gen, "path", path);
        SetRef(gen, "railPrefab", rail);
        // Most i tunel pochodzą teraz z kolizji GTA (grupa "tory"); nasz generator układa tylko szyny jako wizualizację.

        // Drzewa i krzaki (niskopoligonowe).
        MissionVegetation.Build(meta, terrains);

        // Siatki kolizji GTA: obiekty (przełącznik F5 z naszymi pudełkami) i tory/mosty/tunele (kolizja jazdy). Roślinność zostaje nasza.
        var gtaTracks = BuildGtaVariants(mats, trackGo, terrains, meta);

        // Nasze szyny to tylko wizualizacja: ścieżkę dopasowujemy do powierzchni kolizji (GTA albo teren), potem z niej układamy tor.
        SnapPathToCollision(path, gtaTracks, deck, CreateSupportFloorPrefab(mats));
        gen.Generate();

        // Pociąg.
        CreateTrain(path, mats);

        // Rower na starcie, obok toru.
        // Prefab roweru bywa trzymany w pamięci edytora ze starymi wartościami pól (np. po zmianie domyślnych w kodzie).
        AssetDatabase.ImportAsset(BikePrefabPath, ImportAssetOptions.ForceUpdate);
        var bikePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BikePrefabPath);
        if (bikePrefab == null)
        {
            Debug.LogError("Brak prefabu roweru – uruchom najpierw Tools/Bike/Build Prototype.");
        }
        else
        {
            Vector3 fwd = (pts[Mathf.Min(6, n - 1)] - pts[0]);
            fwd.y = 0f;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 start = pts[0] + right * 3.2f;
            start.y = TerrainHeight(terrains, start) + 0.3f;

            var bike = (GameObject)PrefabUtility.InstantiatePrefab(bikePrefab);
            bike.transform.SetPositionAndRotation(start, Quaternion.LookRotation(fwd, Vector3.up));
            foreach (var cam in bike.GetComponentsInChildren<Camera>()) cam.farClipPlane = 3000f;

            var hud = new GameObject("HUD").AddComponent<BikeHud>();
            SetRef(hud, "bike", bike.GetComponent<BikeController>());
            SetRef(hud, "pedals", bike.GetComponentInChildren<PedalDrive>());
            SetRef(hud, "steering", bike.GetComponentInChildren<HandlebarSteering>());

            // Broń ręczna gracza (sama przypina się do GunHoldPoint roweru).
            CreateGun(bike);

            // Rozgrywka: gangsterzy na pierwszym wagonie, wieżyczka, menedżer misji i HUD misji.
            MissionGameplayBuilder.Apply();
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!list.Exists(s => s.path == ScenePath)) list.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();

        AssetDatabase.SaveAssets();
        // Po wielokrotnym nadpisywaniu meshy i prefabów edytor potrafi trzymać w pamięci stare, puste referencje
        // (tory bez meshy). Wymuszamy reimport wygenerowanych assetów i wczytujemy scenę z dysku.
        AssetDatabase.ImportAsset(GenDir + "/Meshes", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
        AssetDatabase.ImportAsset(GenDir + "/Prefabs", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Debug.Log($"[MissionMapBuilder] Gotowe: {ScenePath} (tor {route.lengthM / 1000f:0.00} km)");
    }

    // ---------------------------------------------------------------- materiały

    static Mats CreateMaterials()
    {
        var m = new Mats
        {
            TrainLoco = Mat("TrainLoco", new Color(0.10f, 0.22f, 0.48f), 0.2f, 0.4f),
            TrainStripe = Mat("TrainStripe", new Color(0.95f, 0.78f, 0.12f), 0f, 0.4f),
            TrainDark = Mat("TrainDark", new Color(0.12f, 0.12f, 0.13f), 0.3f, 0.3f),
            TrainRoof = Mat("TrainRoof", new Color(0.36f, 0.36f, 0.38f), 0.2f, 0.3f),
            TrainWagons = new[]
            {
                Mat("TrainWagonRust", new Color(0.52f, 0.22f, 0.14f), 0f, 0.25f),
                Mat("TrainWagonBrown", new Color(0.38f, 0.27f, 0.18f), 0f, 0.25f),
                Mat("TrainWagonGrey", new Color(0.45f, 0.47f, 0.50f), 0.1f, 0.3f),
                Mat("TrainWagonGreen", new Color(0.18f, 0.34f, 0.24f), 0f, 0.25f),
            },
            BridgeSteel = Mat("BridgeSteel", new Color(0.50f, 0.58f, 0.55f), 0.4f, 0.4f),
            Ballast = Mat("Ballast", new Color(0.36f, 0.35f, 0.33f), 0f, 0.1f),
            Sleeper = Mat("Sleeper", new Color(0.33f, 0.22f, 0.13f), 0f, 0.2f),
            Steel = Mat("Steel", new Color(0.62f, 0.64f, 0.67f), 0.9f, 0.6f),
            Concrete = Mat("BridgeConcrete", new Color(0.62f, 0.62f, 0.60f), 0f, 0.15f),
            TunnelConcrete = Mat("TunnelConcrete", new Color(0.45f, 0.45f, 0.44f), 0f, 0.1f),
            Fence = Mat("Fence", new Color(0.55f, 0.55f, 0.58f), 0.3f, 0.3f),
            Station = Mat("Station", new Color(0.8f, 0.78f, 0.7f), 0f, 0.2f),
            Water = Mat("Sea", new Color(0.08f, 0.25f, 0.45f), 0.1f, 0.85f),
            Building = new[]
            {
                Mat("BuildingA", new Color(0.78f, 0.70f, 0.58f), 0f, 0.1f),
                Mat("BuildingB", new Color(0.70f, 0.55f, 0.48f), 0f, 0.1f),
                Mat("BuildingC", new Color(0.62f, 0.65f, 0.68f), 0f, 0.1f),
                Mat("BuildingD", new Color(0.82f, 0.80f, 0.74f), 0f, 0.1f),
            },
        };
        return m;
    }

    internal static Material Mat(string name, Color color, float metallic, float smoothness)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------------------------------------------------------------- prefaby toru

    struct BoxDef
    {
        public Vector3 Center, Size;
        public Quaternion Rot;
        public BoxDef(Vector3 c, Vector3 s) { Center = c; Size = s; Rot = Quaternion.identity; }
        public BoxDef(Vector3 c, Vector3 s, Quaternion r) { Center = c; Size = s; Rot = r; }
    }

    /// <summary>Skleja listę pudełek w jedną siatkę (jeden podmesh).</summary>
    static Mesh CombineBoxes(List<BoxDef> boxes)
    {
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var combine = new CombineInstance[boxes.Count];
        for (int i = 0; i < boxes.Count; i++)
        {
            combine[i].mesh = cube;
            combine[i].transform = Matrix4x4.TRS(boxes[i].Center, boxes[i].Rot, boxes[i].Size);
        }
        var mesh = new Mesh();
        mesh.CombineMeshes(combine, true, true);
        return mesh;
    }

    /// <summary>Siatka z kilku grup pudełek: każda grupa to osobny podmesh (osobny materiał).</summary>
    static Mesh CombineGroups(string name, params List<BoxDef>[] groups)
    {
        var parts = new CombineInstance[groups.Length];
        for (int i = 0; i < groups.Length; i++)
        {
            parts[i].mesh = CombineBoxes(groups[i]);
            parts[i].transform = Matrix4x4.identity;
        }
        var mesh = new Mesh { name = name };
        mesh.CombineMeshes(parts, false, false);
        mesh.RecalculateBounds();
        string path = $"{GenDir}/Meshes/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static GameObject SavePrefab(GameObject go, string name)
    {
        string path = $"{GenDir}/Prefabs/{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static void AddBoxCollider(GameObject go, Vector3 center, Vector3 size)
    {
        var c = go.AddComponent<BoxCollider>();
        c.center = center;
        c.size = size;
    }

    /// <summary>Odcinek toru (4 m wzdłuż Z, górna krawędź podsypki na y = 0), tylko wizualny: niska podsypka, 7 podkładów, 2 szyny.</summary>
    static GameObject CreateRailPrefab(Mats m)
    {
        // Tor jest tylko wizualizacją (jeździ się po kolizji GTA albo po terenie): niska podsypka, której górna krawędź to y = 0.
        var ballast = new List<BoxDef> { new BoxDef(new Vector3(0f, -0.1f, 0f), new Vector3(3.2f, 0.2f, SegmentLength)) };
        var sleepers = new List<BoxDef>();
        for (int i = 0; i < 7; i++)
        {
            float z = -SegmentLength * 0.5f + (i + 0.5f) * (SegmentLength / 7f);
            sleepers.Add(new BoxDef(new Vector3(0f, 0.07f, z), new Vector3(2.6f, 0.14f, 0.26f)));
        }
        var rails = new List<BoxDef>
        {
            new BoxDef(new Vector3(-0.7175f, 0.22f, 0f), new Vector3(0.08f, 0.16f, SegmentLength)),
            new BoxDef(new Vector3(0.7175f, 0.22f, 0f), new Vector3(0.08f, 0.16f, SegmentLength)),
        };
        var mesh = CombineGroups("RailSegment", ballast, sleepers, rails);

        var go = new GameObject("RailSegment");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = new[] { m.Ballast, m.Sleeper, m.Steel };
        // Bez colliderów – tor jest wyłącznie wizualizacją.
        return SavePrefab(go, "RailSegment");
    }

    /// <summary>Płyta mostu pod torem z niskimi barierkami po bokach.</summary>
    static GameObject CreateBridgePrefab(Mats m)
    {
        var parts = new List<BoxDef>
        {
            new BoxDef(new Vector3(0f, -1.0f, 0f), new Vector3(6.6f, 1.0f, SegmentLength)),
            new BoxDef(new Vector3(-3.1f, 0.15f, 0f), new Vector3(0.4f, 1.3f, SegmentLength)),
            new BoxDef(new Vector3(3.1f, 0.15f, 0f), new Vector3(0.4f, 1.3f, SegmentLength)),
        };
        var mesh = CombineGroups("BridgeDeck", parts);
        var go = new GameObject("BridgeDeck");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m.Concrete;
        foreach (var b in parts) AddBoxCollider(go, b.Center, b.Size);
        return SavePrefab(go, "BridgeDeck");
    }

    /// <summary>Obudowa tunelu: dwie ściany, strop i podłoga.</summary>
    static GameObject CreateTunnelPrefab(Mats m)
    {
        var parts = new List<BoxDef>
        {
            new BoxDef(new Vector3(-4.0f, 3.15f, 0f), new Vector3(0.8f, 7.3f, SegmentLength)),
            new BoxDef(new Vector3(4.0f, 3.15f, 0f), new Vector3(0.8f, 7.3f, SegmentLength)),
            new BoxDef(new Vector3(0f, 6.4f, 0f), new Vector3(8.8f, 0.8f, SegmentLength)),
            new BoxDef(new Vector3(0f, -0.75f, 0f), new Vector3(7.6f, 0.5f, SegmentLength)),   // podłoga (górna powierzchnia na poziomie płyty mostu)
        };
        var mesh = CombineGroups("TunnelSegment", parts);
        var go = new GameObject("TunnelSegment");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m.TunnelConcrete;
        foreach (var b in parts) AddBoxCollider(go, b.Center, b.Size);
        return SavePrefab(go, "TunnelSegment");
    }

    // ---------------------------------------------------------------- teren

    static TerrainData[] CreateTerrainData(TerrainMeta meta, RouteData route)
    {
        byte[] hb = File.ReadAllBytes(RecipeDir + "/terrain.bin");
        byte[] cb = File.ReadAllBytes(RecipeDir + "/terrain_class.bin");
        int nx = meta.samplesX, ny = meta.samplesY, tile = meta.tile;
        int tiles = (ny - 1) / tile;
        float tileSize = tile * meta.cell;

        var layers = CreateTerrainLayers();
        var result = new TerrainData[tiles];
        for (int t = 0; t < tiles; t++)
        {
            // Asset tworzymy przed wypełnieniem – dzięki temu tekstury splat (alphamapy) zapisują się razem z nim.
            string path = $"{GenDir}/Terrain/TerrainTile{t}.asset";
            AssetDatabase.DeleteAsset(path);
            var td = new TerrainData { heightmapResolution = tile + 1, alphamapResolution = tile };
            AssetDatabase.CreateAsset(td, path);
            td.size = new Vector3(tileSize, meta.zMax - meta.zMin, tileSize);
            td.terrainLayers = layers;

            var h = new float[tile + 1, tile + 1];
            for (int y = 0; y <= tile; y++)
            {
                int row = (t * tile + y) * nx;
                for (int x = 0; x <= tile; x++)
                    h[y, x] = System.BitConverter.ToUInt16(hb, (row + x) * 2) / 65535f;
            }
            td.SetHeights(0, 0, h);
            TunnelHoles(td, meta, route, t, h);

            var a = new float[tile, tile, ClassNames.Length];
            for (int y = 0; y < tile; y++)
            {
                int row = (t * tile + y) * nx;
                for (int x = 0; x < tile; x++)
                {
                    int cls = Mathf.Clamp(cb[row + x], 0, ClassNames.Length - 1);
                    a[y, x, cls] = 1f;
                }
            }
            td.SetAlphamaps(0, 0, a);
            EditorUtility.SetDirty(td);
            result[t] = td;
        }
        return result;
    }

    /// <summary>
    /// Heightfield nie ma „wnętrza”: przy wejściu do tunelu nasyp nad obudową zaczyna się stromym uskokiem, który
    /// przecinałby otwór portalu. Komórki z takim uskokiem tuż przy końcach tunelu zamieniamy na dziury (przykrywa je ściana portalu).
    /// </summary>
    static void TunnelHoles(TerrainData td, TerrainMeta meta, RouteData route, int t, float[,] h)
    {
        int tile = meta.tile;
        int n = route.pts.Length / 3;
        var near = new List<Vector2>();
        for (int i = 0; i < n; i++)
        {
            if (route.kinds[i] != 2) continue;
            bool end = i == 0 || i == n - 1 || route.kinds[i - 1] != 2 || route.kinds[i + 1] != 2;
            if (!end) continue;
            // punkty trasy w okolicy końca tunelu (±24 m)
            for (int j = Mathf.Max(0, i - 12); j <= Mathf.Min(n - 1, i + 12); j++)
                near.Add(new Vector2(route.pts[j * 3], route.pts[j * 3 + 2]));
        }
        if (near.Count == 0) return;

        int res = td.holesResolution;
        var holes = new bool[res, res];
        for (int y = 0; y < res; y++) for (int x = 0; x < res; x++) holes[y, x] = true;
        float height = meta.zMax - meta.zMin;
        int count = 0;
        for (int y = 0; y < tile; y++)
            for (int x = 0; x < tile; x++)
            {
                float a = h[y, x], b = h[y, x + 1], c = h[y + 1, x], d = h[y + 1, x + 1];
                float range = (Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d)) - Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d))) * height;
                if (range < 2.5f) continue;
                var center = new Vector2(meta.minX + (x + 0.5f) * meta.cell, meta.minY + (t * tile + y + 0.5f) * meta.cell);
                bool close = false;
                foreach (var p in near) if ((p - center).sqrMagnitude < 5.5f * 5.5f) { close = true; break; }
                if (!close) continue;
                int hy = y * res / tile, hx = x * res / tile;
                holes[hy, hx] = false;
                count++;
            }
        td.SetHoles(0, 0, holes);
        Debug.Log($"[MissionMapBuilder] TunnelHoles tile {t}: {count} dziur (holesResolution {res})");
    }

    static TerrainLayer[] CreateTerrainLayers()
    {
        var layers = new TerrainLayer[ClassNames.Length];
        for (int i = 0; i < ClassNames.Length; i++)
        {
            string texPath = $"{GenDir}/Textures/Terrain_{ClassNames[i]}.png";
            var tex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            var rng = new System.Random(100 + i);
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    float noise = (float)(rng.NextDouble() - 0.5f) * 0.12f;
                    Color c = ClassColors[i];
                    tex.SetPixel(x, y, new Color(Mathf.Clamp01(c.r + noise), Mathf.Clamp01(c.g + noise), Mathf.Clamp01(c.b + noise), 1f));
                }
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);

            string layerPath = $"{GenDir}/Terrain/Layer_{ClassNames[i]}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, layerPath);
            }
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            layer.tileSize = new Vector2(6f, 6f);
            layer.smoothness = 0f;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            layers[i] = layer;
        }
        return layers;
    }

    static Terrain[] CreateTerrains(TerrainMeta meta, TerrainData[] data)
    {
        var terrains = new Terrain[data.Length];
        var parent = new GameObject("Terrain").transform;
        float tileSize = meta.tile * meta.cell;
        for (int t = 0; t < data.Length; t++)
        {
            var go = Terrain.CreateTerrainGameObject(data[t]);
            go.name = $"TerrainTile{t}";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(meta.minX, meta.zMin, meta.minY + t * tileSize);
            go.isStatic = true;
            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 1500f;
            terrains[t] = terrain;
        }
        return terrains;
    }

    static float TerrainHeight(Terrain[] terrains, Vector3 p)
    {
        foreach (var t in terrains)
        {
            Vector3 o = t.transform.position;
            if (p.x >= o.x && p.x <= o.x + t.terrainData.size.x && p.z >= o.z && p.z <= o.z + t.terrainData.size.z)
                return t.SampleHeight(p) + o.y;
        }
        return p.y;
    }

    // ---------------------------------------------------------------- wariant "GTA": siatki kolizji z gry

    static Material MatTwoSided(string name, Color color)
    {
        var m = Mat(name, color, 0f, 0.15f);
        m.SetFloat("_Cull", 0f);          // siatki kolizji bywają jednościenne
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>
    /// Tworzy siatki z meshes.bin (kształty kolizji GTA, cieniowanie płaskie), ustawia je jako instancje w trzech grupach
    /// (obiekty / roślinność / tory) i dodaje przełącznik wariantów. Nasze elementy zostają w scenie.
    /// </summary>
    static GameObject gtaGroundRoot;

    static GameObject BuildGtaVariants(Mats mats, GameObject trackGo, Terrain[] terrains, TerrainMeta meta)
    {
        string meshesJson = RecipeDir + "/meshes.json";
        if (!File.Exists(meshesJson)) { Debug.Log("[MissionMapBuilder] Brak meshes.json – pomijam wariant GTA."); return null; }
        var models = JsonUtility.FromJson<MeshModelList>(File.ReadAllText(meshesJson)).models;
        var instances = JsonUtility.FromJson<MeshInstanceList>(File.ReadAllText(RecipeDir + "/mesh_instances.json")).instances;
        byte[] bin = File.ReadAllBytes(RecipeDir + "/meshes.bin");

        AssetDatabase.DeleteAsset(GenDir + "/Meshes/Gta");
        EnsureFolder(GenDir + "/Meshes/Gta");
        var meshes = new Mesh[models.Length];
        var colMeshes = new Mesh[models.Length];   // kolizja: ściany dwustronne (null = taka sama jak siatka widoczna)
        int doubledWalls = 0, rampTris = 0, skirtTris = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < models.Length; i++)
            {
                var md = models[i];
                var v = new Vector3[md.nv];
                for (int j = 0; j < md.nv; j++)
                    v[j] = new Vector3(System.BitConverter.ToSingle(bin, md.vo + j * 12), System.BitConverter.ToSingle(bin, md.vo + j * 12 + 4), System.BitConverter.ToSingle(bin, md.vo + j * 12 + 8));
                var verts = new Vector3[md.nt * 3];
                var idx = new int[md.nt * 3];
                for (int k = 0; k < md.nt * 3; k++)
                {
                    int vi = System.BitConverter.ToUInt16(bin, md.io + k * 2);
                    verts[k] = v[vi < md.nv ? vi : 0];
                    idx[k] = k;
                }
                var mesh = new Mesh { name = md.n, indexFormat = verts.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                mesh.vertices = verts;
                if (md.sub != null && md.sub.Length > 0)
                {
                    // grunt: podsiatki wg klasy powierzchni (puste pomijamy)
                    int subs = 0; foreach (int c in md.sub) if (c > 0) subs++;
                    mesh.subMeshCount = Mathf.Max(1, subs);
                    int off = 0, si = 0;
                    for (int cl = 0; cl < md.sub.Length; cl++)
                    {
                        if (md.sub[cl] == 0) continue;
                        var part = new int[md.sub[cl] * 3];
                        System.Array.Copy(idx, off * 3, part, 0, part.Length);
                        mesh.SetTriangles(part, si++);
                        off += md.sub[cl];
                    }
                }
                else mesh.triangles = idx;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, $"{GenDir}/Meshes/Gta/{i}_{md.n}.asset");
                meshes[i] = mesh;

                // Siatki kolizji GTA mają dowolne uzwojenie trójkątów, a MeshCollider w PhysX przepuszcza ciało dynamiczne
                // przez ścianę od strony "tylnej" (rower wjeżdżał w krawędź i wpadał pod mapę). Do kolizji dokładamy
                // odwrócone kopie stromych trójkątów, żeby ściany blokowały z obu stron.
                // Dodatkowo niskie ścianki (stopnie do StepMaxHeight) dostają po obu stronach skos, po którym da się wjechać
                // (dolna krawędź przesunięta poziomo o wysokość / tan(kąt)); kierunek niskiej strony nie jest znany, więc
                // skos powstaje z obu stron – ten po stronie wysokiej leży wewnątrz bryły i jest nieosiągalny.
                List<int> colIdx = null;
                List<Vector3> colVerts = null;
                float rampRun = 1f / Mathf.Tan(StepRampAngle * Mathf.Deg2Rad);
                for (int k = 0; k < verts.Length; k += 3)
                {
                    Vector3 a = verts[k], b = verts[k + 1], c = verts[k + 2];
                    Vector3 nrm = Vector3.Cross(b - a, c - a);
                    float len = nrm.magnitude;
                    if (len < 1e-8f) continue;
                    float ny = Mathf.Abs(nrm.y) / len;
                    if (ny >= 0.7f) continue;
                    if (colIdx == null) { colIdx = new List<int>(idx.Length + 64); colIdx.AddRange(idx); colVerts = new List<Vector3>(verts); }
                    colIdx.Add(k); colIdx.Add(k + 2); colIdx.Add(k + 1);
                    doubledWalls++;

                    if (ny >= 0.5f) continue;
                    float yMax = Mathf.Max(a.y, Mathf.Max(b.y, c.y)), yMin = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
                    float h = yMax - yMin;
                    var nh = new Vector3(nrm.x, 0f, nrm.z);
                    float nl = nh.magnitude;
                    if (h < StepMinHeight || h > StepMaxHeight || nl < 1e-6f) continue;
                    nh /= nl;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int bi = colVerts.Count;
                        colVerts.Add(a + nh * (side * rampRun * (yMax - a.y)));
                        colVerts.Add(b + nh * (side * rampRun * (yMax - b.y)));
                        colVerts.Add(c + nh * (side * rampRun * (yMax - c.y)));
                        colIdx.Add(bi); colIdx.Add(bi + 1); colIdx.Add(bi + 2);     // oba uzwojenia: skos działa z każdej strony
                        colIdx.Add(bi); colIdx.Add(bi + 2); colIdx.Add(bi + 1);
                        rampTris++;
                    }
                }
                // Wolne krawędzie poziomych płacht (GTA często pomija pionową ściankę stopnia – jest tylko wyższa płachta nad niższą):
                // dokładamy "spódniczkę", skos opadający od krawędzi na zewnątrz i w dół o StepMaxHeight. Jeśli niżej nic nie ma,
                // jest nieszkodliwa; jeśli jest niższa powierzchnia, tworzy z nią wjazd na stopień.
                {
                    var vertId = new Dictionary<Vector3Int, int>(verts.Length);
                    var ids = new int[verts.Length];
                    for (int k = 0; k < verts.Length; k++)
                    {
                        var q = new Vector3Int(Mathf.RoundToInt(verts[k].x * 500f), Mathf.RoundToInt(verts[k].y * 500f), Mathf.RoundToInt(verts[k].z * 500f));
                        if (!vertId.TryGetValue(q, out int id)) { id = vertId.Count; vertId[q] = id; }
                        ids[k] = id;
                    }
                    var edgeCount = new Dictionary<long, int>(verts.Length);
                    for (int k = 0; k < verts.Length; k += 3)
                        for (int e = 0; e < 3; e++)
                        {
                            long key = EdgeKey(ids[k + e], ids[k + (e + 1) % 3]);
                            edgeCount[key] = edgeCount.TryGetValue(key, out int cnt) ? cnt + 1 : 1;
                        }
                    float skirtDrop = StepMaxHeight, skirtRun = StepMaxHeight * rampRun;
                    for (int k = 0; k < verts.Length; k += 3)
                    {
                        Vector3 a = verts[k], b = verts[k + 1], c = verts[k + 2];
                        Vector3 nrm = Vector3.Cross(b - a, c - a);
                        float len = nrm.magnitude;
                        if (len < 1e-8f || Mathf.Abs(nrm.y) / len < 0.9f) continue;
                        for (int e = 0; e < 3; e++)
                        {
                            if (edgeCount[EdgeKey(ids[k + e], ids[k + (e + 1) % 3])] != 1) continue;
                            Vector3 e0 = verts[k + e], e1 = verts[k + (e + 1) % 3], p3 = verts[k + (e + 2) % 3];
                            Vector3 dEdge = new Vector3(e1.x - e0.x, 0f, e1.z - e0.z);
                            if (dEdge.sqrMagnitude < 1e-6f) continue;
                            Vector3 perp = Vector3.Cross(Vector3.up, dEdge).normalized;
                            if (Vector3.Dot(perp, p3 - e0) > 0f) perp = -perp;          // na zewnątrz płachty
                            if (colIdx == null) { colIdx = new List<int>(idx.Length + 64); colIdx.AddRange(idx); colVerts = new List<Vector3>(verts); }
                            Vector3 off = perp * skirtRun + Vector3.down * skirtDrop;
                            int bi = colVerts.Count;
                            colVerts.Add(e0); colVerts.Add(e1); colVerts.Add(e1 + off); colVerts.Add(e0 + off);
                            colIdx.Add(bi); colIdx.Add(bi + 1); colIdx.Add(bi + 2); colIdx.Add(bi); colIdx.Add(bi + 2); colIdx.Add(bi + 3);
                            colIdx.Add(bi); colIdx.Add(bi + 2); colIdx.Add(bi + 1); colIdx.Add(bi); colIdx.Add(bi + 3); colIdx.Add(bi + 2);
                            skirtTris += 2;
                        }
                    }
                }
                if (colIdx != null)
                {
                    var cm = new Mesh { name = md.n + "_col", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    cm.SetVertices(colVerts);
                    cm.triangles = colIdx.ToArray();
                    AssetDatabase.CreateAsset(cm, $"{GenDir}/Meshes/Gta/{i}_{md.n}_col.asset");
                    colMeshes[i] = cm;
                }
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }

        var buildingMats = new Material[mats.Building.Length];
        for (int i = 0; i < buildingMats.Length; i++) buildingMats[i] = MatTwoSided("GtaObj" + i, mats.Building[i].GetColor("_BaseColor"));
        // kategorie: 0 budynek, 1 ogrodzenie, 2 stacja, 3 most, 4 tunel, 5 szyny, 6 roślinność
        var catMats = new Material[]
        {
            null,
            MatTwoSided("GtaFence", mats.Fence.GetColor("_BaseColor")),
            MatTwoSided("GtaStation", mats.Station.GetColor("_BaseColor")),
            MatTwoSided("GtaBridge", mats.Concrete.GetColor("_BaseColor")),
            MatTwoSided("GtaTunnel", mats.TunnelConcrete.GetColor("_BaseColor")),
            MatTwoSided("GtaRail", new Color(0.30f, 0.27f, 0.25f)),
            MatTwoSided("GtaVeg", new Color(0.30f, 0.38f, 0.20f)),
        };

        // klasy gruntu GTA: jak teren (0..5) + chodnik/beton (6)
        string[] groundNames = { "Neutral", "Asphalt", "Grass", "Dirt", "Sand", "Rock", "Pavement" };
        var groundMats = new Material[groundNames.Length];
        for (int i = 0; i < groundMats.Length; i++)
            groundMats[i] = MatTwoSided("GtaGround" + groundNames[i], i < ClassColors.Length ? ClassColors[i] : new Color(0.58f, 0.57f, 0.53f));
        var roots = new[] { new GameObject("GtaObjects"), null, new GameObject("GtaTracks"), new GameObject("GtaGround") };   // roślinność z GTA nie jest używana
        gtaGroundRoot = roots[3];
        var counts = new int[4];
        foreach (var inst in instances)
        {
            if (roots[inst.g] == null) continue;
            var md = models[inst.m];
            var go = new GameObject(md.n);
            go.transform.SetParent(roots[inst.g].transform, false);
            go.transform.SetPositionAndRotation(new Vector3(inst.p[0], inst.p[1], inst.p[2]), new Quaternion(inst.q[0], inst.q[1], inst.q[2], inst.q[3]));
            go.AddComponent<MeshFilter>().sharedMesh = meshes[inst.m];
            var mr = go.AddComponent<MeshRenderer>();
            if (inst.g == 3 && md.sub != null && md.sub.Length > 0)
            {
                var list = new List<Material>();
                for (int cl = 0; cl < md.sub.Length; cl++) if (md.sub[cl] > 0) list.Add(groundMats[cl]);
                mr.sharedMaterials = list.ToArray();
            }
            else mr.sharedMaterial = inst.c == 0 ? buildingMats[Mathf.Abs(md.n.GetHashCode()) % buildingMats.Length] : catMats[inst.c];
            go.AddComponent<MeshCollider>().sharedMesh = colMeshes[inst.m] != null ? colMeshes[inst.m] : meshes[inst.m];
            go.isStatic = true;
            counts[inst.g]++;
        }

        // Pod siatkami gruntu GTA teren (heightfield) ma dziury – powierzchnię przejmują siatki.
        ApplyGroundHoles(terrains, meta);
        Debug.Log($"[MissionMapBuilder] Siatki GTA: modeli {models.Length}, instancji obiekty {counts[0]}, tory {counts[2]}, grunt {counts[3]}, dwustronnych ścian kolizji {doubledWalls}, skosów przy niskich stopniach {rampTris}, spódniczek przy krawędziach płacht {skirtTris}");
        return roots[2];
    }

    static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    /// <summary>
    /// Wycina w terenie komórki, które zastępują siatki gruntu GTA (maska ground_holes.bin: jeden bit na komórkę, wiersz globalny = kafelek * tile + y);
    /// dziury z TunnelHoles zostają. Stan zapisuje się w TerrainData (generowanym przy budowie mapy).
    /// </summary>
    static void ApplyGroundHoles(Terrain[] terrains, TerrainMeta meta)
    {
        string p = RecipeDir + "/ground_holes.bin";
        if (!File.Exists(p)) return;
        byte[] mask = File.ReadAllBytes(p);
        for (int t = 0; t < terrains.Length; t++)
        {
            var td = terrains[t].terrainData;
            int res = td.holesResolution;
            var open = td.GetHoles(0, 0, res, res);
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    if (!open[y, x]) continue;
                    int i = (y * meta.tile / res + t * meta.tile) * meta.tile + x * meta.tile / res;
                    if ((mask[i >> 3] & (1 << (i & 7))) != 0) open[y, x] = false;
                }
            td.SetHoles(0, 0, open);
            EditorUtility.SetDirty(td);
        }
    }

    // ---------------------------------------------------------------- dopasowanie toru do kolizji

    /// <summary>Podłoga tunelu (widoczna i z kolizją): górna powierzchnia 0,5 m pod początkiem prefabu, jak płyta mostu.</summary>
    static GameObject CreateSupportFloorPrefab(Mats m)
    {
        var go = new GameObject("TunnelFloor");
        Prim(PrimitiveType.Cube, "Floor", go.transform, new Vector3(0f, -0.75f, 0f), new Vector3(7.6f, 0.5f, SegmentLength), m.TunnelConcrete, true);
        return SavePrefab(go, "TunnelFloor");
    }

    /// <summary>
    /// Ustawia wysokość ścieżki na powierzchnię, po której faktycznie się jeździ: kolizje GTA z grupy "tory" albo teren
    /// (lekko wygładzone: mediana + średnia), plus podniesienie podsypki o 12 cm. Szyny i pociąg układane są już z tej ścieżki.
    /// Tam, gdzie na moście lub w tunelu GTA nie ma kolizji, układamy nasze podparcie (płyta mostu / podłoga tunelu)
    /// na pierwotnej wysokości trasy.
    /// </summary>
    static void SnapPathToCollision(TrackPath path, GameObject tracksRoot, GameObject deckPrefab, GameObject floorPrefab)
    {
        Physics.SyncTransforms();
        int n = path.Count;
        var orig = new Vector3[n];
        var kinds = new byte[n];
        for (int i = 0; i < n; i++) { orig[i] = path.PointAt(i); kinds[i] = (byte)path.KindAt(i); }

        var surf = new float[n];
        var gtaHit = new bool[n];
        int onGta = 0, onTerrain = 0;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = orig[i];
            var hits = Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            bool gta = false;
            foreach (var h in hits)
            {
                bool isGta = tracksRoot != null && h.collider.transform.IsChildOf(tracksRoot.transform);
                bool isGround = h.collider is TerrainCollider || (gtaGroundRoot != null && h.collider.transform.IsChildOf(gtaGroundRoot.transform));
                if (!isGta && !isGround) continue;
                if (h.point.y > best) { best = h.point.y; gta = isGta; }
            }
            surf[i] = best > float.NegativeInfinity ? best : p.y - 0.53f;
            gtaHit[i] = gta;
            if (gta) onGta++; else onTerrain++;
        }

        // Most / tunel bez kolizji GTA wymaga naszego podparcia; zamykamy krótkie luki (do 4 punktów) i dodajemy po 1 punkcie z każdej strony.
        var support = new bool[n];
        for (int i = 0; i < n; i++) support[i] = kinds[i] != 0 && !gtaHit[i];
        var closed = (bool[])support.Clone();
        for (int i = 0; i < n; i++)
        {
            if (support[i]) continue;
            int a = i - 1; while (a >= 0 && !support[a] && i - a <= 4) a--;
            int b = i + 1; while (b < n && !support[b] && b - i <= 4) b++;
            if (a >= 0 && b < n && support[a] && support[b] && b - a <= 5) closed[i] = true;
        }
        var dilated = (bool[])closed.Clone();
        for (int i = 0; i < n; i++) if (closed[i]) { if (i > 0) dilated[i - 1] = true; if (i < n - 1) dilated[i + 1] = true; }
        support = dilated;
        for (int i = 0; i < n; i++) if (support[i]) surf[i] = orig[i].y - 0.5f;

        // Mediana (okno 7) usuwa pojedyncze skoki na złączach kolizji, średnia (okno 5) wygładza resztę; podparte punkty zostają bez zmian.
        var med = new float[n];
        var buf = new float[7];
        for (int i = 0; i < n; i++)
        {
            int c = 0;
            for (int k = -3; k <= 3; k++) buf[c++] = surf[Mathf.Clamp(i + k, 0, n - 1)];
            System.Array.Sort(buf);
            med[i] = buf[3];
        }
        var pts = new Vector3[n];
        float maxDelta = 0f, sumDelta = 0f;
        int supported = 0;
        for (int i = 0; i < n; i++)
        {
            float y;
            if (support[i]) { y = surf[i] + 0.12f; supported++; }
            else
            {
                float sum = 0f;
                for (int k = -2; k <= 2; k++) sum += med[Mathf.Clamp(i + k, 0, n - 1)];
                y = sum / 5f + 0.12f;
            }
            float d = y - orig[i].y;
            maxDelta = Mathf.Max(maxDelta, Mathf.Abs(d));
            sumDelta += d;
            pts[i] = new Vector3(orig[i].x, y, orig[i].z);
        }
        path.Set(pts, kinds);
        SpawnSupports(orig, kinds, support, deckPrefab, floorPrefab);
        Debug.Log($"[MissionMapBuilder] Tor dopasowany do kolizji: GTA {onGta}, teren {onTerrain}, nasze podparcie {supported} pkt (z {n}); średnia zmiana wysokości {sumDelta / n:0.00} m, max {maxDelta:0.00} m");
    }

    /// <summary>Płyta mostu (kolizja + widok) i podłoga tunelu co 4 m wzdłuż punktów oznaczonych jako podparte; pozycje z pierwotnej trasy.</summary>
    static void SpawnSupports(Vector3[] orig, byte[] kinds, bool[] support, GameObject deckPrefab, GameObject floorPrefab)
    {
        var root = new GameObject("TrackSupports").transform;
        int bridges = 0, floors = 0;
        for (int i = 0; i < orig.Length - 1; i += 2)
        {
            int j = Mathf.Min(i + 2, orig.Length - 1);
            if (!(support[i] || support[j])) continue;
            byte kind = 0;
            for (int k = i; k <= j; k++) if (support[k] && kinds[k] > kind) kind = kinds[k];
            if (kind == 0) continue;
            Vector3 d = orig[j] - orig[i];
            float len = d.magnitude;
            if (len < 0.2f) continue;
            var prefab = kind == 2 ? floorPrefab : deckPrefab;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            go.transform.SetPositionAndRotation((orig[i] + orig[j]) * 0.5f, Quaternion.LookRotation(d / len, Vector3.up));
            go.transform.localScale = new Vector3(1f, 1f, len / SegmentLength);
            go.isStatic = true;
            if (kind == 2) floors++; else bridges++;
        }
        Debug.Log($"[MissionMapBuilder] Podparcie: płyty mostu {bridges}, podłoga tunelu {floors}");
    }

    // ---------------------------------------------------------------- portale tunelu

    /// <summary>Ściana czołowa (betonowa) na obu końcach każdego tunelu – zasłania krawędź nasypu nad obudową.</summary>
    static void BuildTunnelPortals(TrackPath path, Mats m)
    {
        var root = new GameObject("TunnelPortals");
        int n = path.Count;
        float spacing = path.Length / Mathf.Max(1, n - 1);
        int step = Mathf.Max(1, Mathf.RoundToInt(SegmentLength / Mathf.Max(0.1f, spacing)));
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            bool isTunnel = path.KindAt(i) == TrackPath.Kind.Tunnel;
            bool prevTunnel = i > 0 && path.KindAt(i - 1) == TrackPath.Kind.Tunnel;
            bool nextTunnel = i < n - 1 && path.KindAt(i + 1) == TrackPath.Kind.Tunnel;
            // granice odcinków generatora: segmenty zaczynają się od indeksów 0, step, 2*step... i są tunelowe, gdy zawierają punkt tunelu
            int boundary;
            float inward;
            if (isTunnel && !prevTunnel) { boundary = (i % step == 0) ? i - step : i - (i % step); inward = 1f; }
            else if (isTunnel && !nextTunnel) { boundary = (i % step == 0) ? i + step : i - (i % step) + step; inward = -1f; }
            else continue;
            boundary = Mathf.Clamp(boundary, 0, n - 1);
            Vector3 p = path.PointAt(boundary);
            Vector3 d = path.PointAt(Mathf.Min(n - 1, boundary + 1)) - path.PointAt(Mathf.Max(0, boundary - 1));
            if (d.sqrMagnitude < 1e-4f) continue;
            d *= inward;
            var portal = new GameObject("Portal" + (++count));
            portal.transform.SetParent(root.transform, false);
            portal.transform.SetPositionAndRotation(p, Quaternion.LookRotation(d.normalized, Vector3.up));
            Prim(PrimitiveType.Cube, "Left", portal.transform, new Vector3(-6.3f, 4.75f, 0f), new Vector3(5.4f, 10.5f, 2.4f), m.TunnelConcrete, true);
            Prim(PrimitiveType.Cube, "Right", portal.transform, new Vector3(6.3f, 4.75f, 0f), new Vector3(5.4f, 10.5f, 2.4f), m.TunnelConcrete, true);
            Prim(PrimitiveType.Cube, "Lintel", portal.transform, new Vector3(0f, 8.0f, 0f), new Vector3(7.2f, 4.0f, 2.4f), m.TunnelConcrete, true);
            Prim(PrimitiveType.Cube, "Cap", portal.transform, new Vector3(0f, 8.2f, 5.0f), new Vector3(13f, 3.6f, 10f), m.TunnelConcrete, true);
            foreach (var r in portal.GetComponentsInChildren<Renderer>()) r.gameObject.isStatic = true;
        }
    }

    // ---------------------------------------------------------------- broń

    /// <summary>
    /// Wstawia prefab broni z ustawieniami misji. Położenie w scenie nie ma znaczenia – w Start broń sama
    /// przypina się do GunHoldPoint roweru, a MissionManager znajduje ją do statystyk.
    /// </summary>
    static void CreateGun(GameObject bike)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GunPrefabPath);
        if (prefab == null) { Debug.LogWarning($"[MissionMapBuilder] Brak prefabu broni ({GunPrefabPath}) – pomijam broń."); return; }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = "Gun";
        go.transform.SetPositionAndRotation(bike.transform.position, bike.transform.rotation);

        var so = new SerializedObject(go.GetComponent<Gun>());
        so.FindProperty("aimReference").enumValueIndex = (int)Gun.AimReference.ScreenCenter;
        so.FindProperty("maxYaw").floatValue = 120f;
        so.FindProperty("maxPitch").floatValue = 120f;
        so.FindProperty("rotateCamera").boolValue = true;
        so.FindProperty("cameraAimFactor").floatValue = 0.4f;
        so.FindProperty("shotVolume").floatValue = 1f;
        so.FindProperty("showShotLine").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------------------------------------------------------------- pociąg

    static void CreateTrain(TrackPath path, Mats m)
    {
        GameObject loco = CreateTrainCar("TrainLocomotive", true, m);
        GameObject wagon = CreateTrainCar("TrainWagon", false, m);

        var go = new GameObject("Train");
        var train = go.AddComponent<TrainController>();
        SetRef(train, "path", path);
        SetRef(train, "locomotivePrefab", loco);
        SetRef(train, "wagonPrefab", wagon);
        SetRefs(train, "wagonBodyMaterials", m.TrainWagons);
    }

    /// <summary>Wagon / lokomotywa z prymitywów: pivot w środku, dołem na szynach, długość 18 m wzdłuż Z.</summary>
    static GameObject CreateTrainCar(string name, bool locomotive, Mats m)
    {
        var root = new GameObject(name);
        // Podwozie i wózki.
        Prim(PrimitiveType.Cube, "Floor", root.transform, new Vector3(0f, 1.25f, 0f), new Vector3(2.9f, 0.35f, 18f), m.TrainDark, false);
        foreach (float z in new[] { -5.5f, 5.5f })
        {
            Prim(PrimitiveType.Cube, "Bogie", root.transform, new Vector3(0f, 0.55f, z), new Vector3(2.4f, 0.9f, 3.4f), m.TrainDark, false);
            Prim(PrimitiveType.Cube, "Wheels", root.transform, new Vector3(0f, 0.45f, z), new Vector3(3.0f, 0.9f, 2.6f), m.TrainDark, false);
        }

        if (locomotive)
        {
            Prim(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, 2.95f, -1.0f), new Vector3(2.9f, 2.8f, 16f), m.TrainLoco, false);
            Prim(PrimitiveType.Cube, "Cab", root.transform, new Vector3(0f, 4.75f, 5.2f), new Vector3(2.9f, 1.0f, 5.2f), m.TrainLoco, false);
            Prim(PrimitiveType.Cube, "Nose", root.transform, new Vector3(0f, 2.75f, 8.35f), new Vector3(2.9f, 2.4f, 1.3f), m.TrainLoco, false);
            Prim(PrimitiveType.Cube, "Stripe", root.transform, new Vector3(0f, 2.3f, -1.0f), new Vector3(2.96f, 0.35f, 16.05f), m.TrainStripe, false);
            Prim(PrimitiveType.Cube, "Roof", root.transform, new Vector3(0f, 4.45f, -3.2f), new Vector3(2.7f, 0.2f, 11f), m.TrainRoof, false);
            Prim(PrimitiveType.Cube, "Light", root.transform, new Vector3(0f, 3.4f, 9.05f), new Vector3(1.2f, 0.4f, 0.1f), m.TrainStripe, false);
        }
        else
        {
            Prim(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, 3.0f, 0f), new Vector3(2.9f, 3.2f, 17.6f), m.TrainWagons[0], false);
            Prim(PrimitiveType.Cube, "Roof", root.transform, new Vector3(0f, 4.7f, 0f), new Vector3(3.0f, 0.2f, 17.8f), m.TrainRoof, false);
            Prim(PrimitiveType.Cube, "Door", root.transform, new Vector3(1.47f, 2.7f, 0f), new Vector3(0.1f, 2.4f, 3.2f), m.TrainDark, false);
        }

        // Jedna kolizja na cały wagon, kinematyczne ciało (porusza je TrainController).
        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 2.65f, 0f);
        box.size = new Vector3(3.0f, 4.7f, 18f);
        var rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        string path = GenDir + "/Prefabs/" + name + ".prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    // ---------------------------------------------------------------- łuki mostów

    /// <summary>
    /// Dla długich odcinków "most" (≥ 150 m) dokłada konstrukcję łukową: dwa parabolicznie wygięte łuki po bokach płyty,
    /// pionowe wieszaki i filary między przęsłami (jak w Frederick Bridge – most łukowy z jazdą dołem).
    /// Łuki są tylko wizualne (bez kolizji), żeby nie blokować roweru; filary mają kolizję.
    /// </summary>
    static void BuildBridgeArches(TrackPath path, Mats m)
    {
        int n = path.Count;
        var root = new GameObject("BridgeArches").transform;
        int i = 0;
        while (i < n)
        {
            if (path.KindAt(i) != TrackPath.Kind.Bridge) { i++; continue; }
            int start = i;
            while (i < n && path.KindAt(i) == TrackPath.Kind.Bridge) i++;
            int end = i - 1;

            float startDist = 0f;
            for (int k = 1; k <= start; k++) startDist += Vector3.Distance(path.PointAt(k - 1), path.PointAt(k));
            float len = 0f;
            for (int k = start + 1; k <= end; k++) len += Vector3.Distance(path.PointAt(k - 1), path.PointAt(k));
            if (len < 150f) continue;

            int spans = Mathf.Max(1, Mathf.RoundToInt(len / 100f));
            float span = len / spans;
            for (int s = 0; s <= spans; s++)
            {
                path.SampleAtDistance(startDist + s * span, out Vector3 pos, out Vector3 fwd);
                var pier = Prim(PrimitiveType.Cube, "Pier" + s, root, pos + Vector3.up * (-1.5f - 7f), new Vector3(5.2f, 14f, 2.4f), m.Concrete, true);
                pier.transform.rotation = Quaternion.LookRotation(new Vector3(fwd.x, 0f, fwd.z).normalized, Vector3.up);
                pier.isStatic = true;
            }
            for (int s = 0; s < spans; s++)
                BuildArch(path, root, startDist + s * span, span, m);
        }
    }

    static void BuildArch(TrackPath path, Transform parent, float d0, float span, Mats m)
    {
        path.SampleAtDistance(d0 + span * 0.5f, out Vector3 mid, out Vector3 fwd);
        fwd = new Vector3(fwd.x, 0f, fwd.z).normalized;
        var rot = Quaternion.LookRotation(fwd, Vector3.up);
        var arch = new GameObject("Arch").transform;
        arch.SetParent(parent, false);
        arch.SetPositionAndRotation(mid, rot);

        float height = Mathf.Min(24f, span * 0.24f);
        const int N = 20;
        var pts = new Vector3[N + 1];
        for (int k = 0; k <= N; k++)
        {
            float t = k / (float)N;
            pts[k] = new Vector3(0f, 0.5f + height * 4f * t * (1f - t), (t - 0.5f) * span);
        }
        foreach (float x in new[] { -3.5f, 3.5f })
        {
            for (int k = 0; k < N; k++)
            {
                Vector3 a = pts[k] + Vector3.right * x, b = pts[k + 1] + Vector3.right * x;
                var seg = Prim(PrimitiveType.Cube, "rib", arch, (a + b) * 0.5f, new Vector3(0.9f, 1.3f, (b - a).magnitude + 0.15f), m.BridgeSteel, false);
                seg.transform.localRotation = Quaternion.LookRotation((b - a).normalized, Vector3.up);
                seg.isStatic = true;
            }
            for (int k = 2; k < N - 1; k += 2)
            {
                float top = pts[k].y;
                var hanger = Prim(PrimitiveType.Cube, "hanger", arch, new Vector3(x, (top + 0.6f) * 0.5f, pts[k].z), new Vector3(0.22f, top - 0.6f, 0.22f), m.BridgeSteel, false);
                hanger.isStatic = true;
            }
        }
        // Poprzeczki między łukami – tylko tam, gdzie jest nad nimi miejsce na przejazd.
        for (int k = 3; k < N - 2; k += 3)
        {
            if (pts[k].y < 7f) continue;
            var beam = Prim(PrimitiveType.Cube, "brace", arch, new Vector3(0f, pts[k].y, pts[k].z), new Vector3(7f, 0.5f, 0.5f), m.BridgeSteel, false);
            beam.isStatic = true;
        }
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    // ---------------------------------------------------------------- pomocnicze

    internal static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    internal static void SetRefs(Object target, string field, Object[] values)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
