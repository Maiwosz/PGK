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
    const string GenDir = "Assets/Mission/Generated";
    const string MatDir = "Assets/Materials/Mission";
    const string ScenePath = "Assets/Scenes/MissionMap.unity";
    const string BikePrefabPath = "Assets/Prefabs/Bike.prefab";

    const float SegmentLength = 4f;
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
        var objs = JsonUtility.FromJson<MapObjectList>(File.ReadAllText(RecipeDir + "/objects.json"));
        var meta = JsonUtility.FromJson<TerrainMeta>(File.ReadAllText(RecipeDir + "/terrain.json"));

        Mats mats = CreateMaterials();
        GameObject rail = CreateRailPrefab(mats);
        GameObject deck = CreateBridgePrefab(mats);
        GameObject tunnel = CreateTunnelPrefab(mats);
        Terrain[] terrains = null;
        TerrainData[] terrainData = CreateTerrainData(meta);

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
        SetRef(gen, "bridgePrefab", deck);
        SetRef(gen, "tunnelPrefab", tunnel);
        gen.Generate();

        // Obiekty jako bryły.
        BuildObjects(objs, mats, terrains);

        // Drzewa i krzaki (niskopoligonowe).
        MissionVegetation.Build(meta, terrains);

        // Pociąg.
        CreateTrain(path, mats);

        // Łuki i filary długich mostów (Frederick Bridge).
        BuildBridgeArches(path, mats);

        // Rower na starcie, obok toru.
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
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!list.Exists(s => s.path == ScenePath)) list.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();

        AssetDatabase.SaveAssets();
        Debug.Log($"[MissionMapBuilder] Gotowe: {ScenePath} (tor {route.lengthM / 1000f:0.00} km, obiektów {objs.objects.Length})");
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

    static Material Mat(string name, Color color, float metallic, float smoothness)
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
        public BoxDef(Vector3 c, Vector3 s) { Center = c; Size = s; }
    }

    /// <summary>Skleja listę pudełek w jedną siatkę (jeden podmesh).</summary>
    static Mesh CombineBoxes(List<BoxDef> boxes)
    {
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var combine = new CombineInstance[boxes.Count];
        for (int i = 0; i < boxes.Count; i++)
        {
            combine[i].mesh = cube;
            combine[i].transform = Matrix4x4.TRS(boxes[i].Center, Quaternion.identity, boxes[i].Size);
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

    /// <summary>Odcinek toru (4 m wzdłuż Z, górna krawędź podsypki na y = 0): podsypka, 7 podkładów, 2 szyny.</summary>
    static GameObject CreateRailPrefab(Mats m)
    {
        var ballast = new List<BoxDef> { new BoxDef(new Vector3(0f, -0.25f, 0f), new Vector3(3.6f, 0.5f, SegmentLength)) };
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
        // Tylko podsypka ma kolizję – szyn nie traktujemy jako przeszkody.
        AddBoxCollider(go, new Vector3(0f, -0.25f, 0f), new Vector3(3.6f, 0.5f, SegmentLength));
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

    /// <summary>Obudowa tunelu: dwie ściany i strop.</summary>
    static GameObject CreateTunnelPrefab(Mats m)
    {
        var parts = new List<BoxDef>
        {
            new BoxDef(new Vector3(-4.0f, 3.15f, 0f), new Vector3(0.8f, 7.3f, SegmentLength)),
            new BoxDef(new Vector3(4.0f, 3.15f, 0f), new Vector3(0.8f, 7.3f, SegmentLength)),
            new BoxDef(new Vector3(0f, 6.4f, 0f), new Vector3(8.8f, 0.8f, SegmentLength)),
        };
        var mesh = CombineGroups("TunnelSegment", parts);
        var go = new GameObject("TunnelSegment");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = m.TunnelConcrete;
        foreach (var b in parts) AddBoxCollider(go, b.Center, b.Size);
        return SavePrefab(go, "TunnelSegment");
    }

    // ---------------------------------------------------------------- teren

    static TerrainData[] CreateTerrainData(TerrainMeta meta)
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

    // ---------------------------------------------------------------- obiekty

    static void BuildObjects(MapObjectList list, Mats m, Terrain[] terrains)
    {
        var root = new GameObject("MapObjects").transform;
        var groups = new Dictionary<string, Transform>();
        int count = 0;
        foreach (var o in list.objects)
        {
            // Mosty i tunele na trasie powstają z danych ścieżki (płyta mostu / obudowa tunelu).
            if (o.c == "bridge" || o.c == "tunnel") continue;

            if (!groups.TryGetValue(o.c, out var parent))
            {
                parent = new GameObject(o.c).transform;
                parent.SetParent(root, false);
                groups[o.c] = parent;
            }

            Material mat = o.c == "fence" ? m.Fence : o.c == "station" ? m.Station
                         : m.Building[Mathf.Abs(o.n.GetHashCode()) % m.Building.Length];
            var rotation = new Quaternion(o.q[0], o.q[1], o.q[2], o.q[3]);

            if (o.b != null && o.b.Length >= 6 && o.o != null)
            {
                // Bryła złożona z kilku pudełek odwzorowujących rzut budynku.
                var holder = new GameObject(o.n);
                holder.transform.SetParent(parent, false);
                holder.transform.SetPositionAndRotation(new Vector3(o.o[0], o.o[1], o.o[2]), rotation);
                holder.isStatic = true;
                for (int i = 0; i + 5 < o.b.Length; i += 6)
                {
                    var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    part.name = "part";
                    part.transform.SetParent(holder.transform, false);
                    part.transform.localPosition = new Vector3(o.b[i], o.b[i + 1], o.b[i + 2]);
                    part.transform.localScale = new Vector3(Mathf.Max(0.2f, o.b[i + 3]), Mathf.Max(0.2f, o.b[i + 4]), Mathf.Max(0.2f, o.b[i + 5]));
                    part.GetComponent<Renderer>().sharedMaterial = mat;
                    Ground(part.transform, terrains);
                    part.isStatic = true;
                }
            }
            else
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = o.n;
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(new Vector3(o.p[0], o.p[1], o.p[2]), rotation);
                go.transform.localScale = new Vector3(Mathf.Max(0.2f, o.s[0]), Mathf.Max(0.2f, o.s[1]), Mathf.Max(0.2f, o.s[2]));
                go.isStatic = true;
                go.GetComponent<Renderer>().sharedMaterial = mat;
                Ground(go.transform, terrains);
            }
            count++;
        }
        Debug.Log($"[MissionMapBuilder] Obiekty: {count}");
    }

    /// <summary>
    /// Pudełka, które wiszą nad terenem (bo teren różni się od oryginalnego), przedłużamy w dół do ziemi.
    /// Dotyczy tylko dystansu do 12 m – wysoko zawieszone konstrukcje zostają bez zmian.
    /// </summary>
    static void Ground(Transform t, Terrain[] terrains)
    {
        float minH = float.MaxValue;
        for (int ix = -1; ix <= 1; ix++)
            for (int iz = -1; iz <= 1; iz++)
            {
                Vector3 w = t.TransformPoint(new Vector3(ix * 0.5f, 0f, iz * 0.5f));
                minH = Mathf.Min(minH, TerrainHeight(terrains, w));
            }
        float bottom = t.position.y - t.lossyScale.y * 0.5f;
        float gapToGround = bottom - minH;
        if (gapToGround <= 0.25f || gapToGround > 12f) return;
        float extend = gapToGround + 0.3f;
        float parentScaleY = t.parent != null ? Mathf.Max(0.01f, t.parent.lossyScale.y) : 1f;
        t.localScale = new Vector3(t.localScale.x, t.localScale.y + extend / parentScaleY, t.localScale.z);
        t.position -= Vector3.up * (extend * 0.5f);
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

    static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetRefs(Object target, string field, Object[] values)
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
