using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Buduje prototyp: materiały, prosty model roweru (prefab) i scenę testową.
/// Uruchom z menu Tools/Bike/Build Prototype albo przez Unity CLI.
/// </summary>
public static class BikePrototypeBuilder
{
    const string MatDir = "Assets/Materials/Bike";
    const string PrefabDir = "Assets/Prefabs";
    const string PrefabPath = PrefabDir + "/Bike.prefab";
    const string ScenePath = "Assets/Scenes/BikePrototype.unity";

    // Geometria (lokalnie, +Z = przód, początek = punkt między kołami na ziemi).
    const float WheelRadius = 0.34f;
    const float TireHalfThickness = 0.0175f;                 // opona to segmenty o grubości 0,035 m
    const float TireOuterRadius = WheelRadius + TireHalfThickness;   // tyle ma widoczne koło – sfera kolizji ma być równa
    const float RearAxleZ = -0.55f;
    const float FrontAxleZ = 0.57f;

    class Mats
    {
        public Material Frame, Metal, Rubber, Grip, Saddle, Marker, Ground, Cone, Pillar;
    }

    [MenuItem("Tools/Bike/Build Prototype")]
    public static void BuildAll()
    {
        EnsureFolder("Assets/Materials");
        EnsureFolder(MatDir);
        EnsureFolder(PrefabDir);
        EnsureFolder("Assets/Physics");

        Mats mats = CreateMaterials();
        GameObject prefab = CreateBikePrefab(mats);
        CreateScene(prefab, mats);
        AssetDatabase.SaveAssets();
        Debug.Log("[BikePrototypeBuilder] Gotowe: " + ScenePath);
    }

    // ---------------------------------------------------------------- materiały

    static Mats CreateMaterials()
    {
        return new Mats
        {
            Frame = Mat("Frame", new Color(0.78f, 0.1f, 0.1f), 0.5f, 0.6f),
            Metal = Mat("Metal", new Color(0.7f, 0.7f, 0.72f), 0.9f, 0.6f),
            Rubber = Mat("Rubber", new Color(0.05f, 0.05f, 0.05f), 0f, 0.2f),
            Grip = Mat("Grip", new Color(0.08f, 0.08f, 0.08f), 0f, 0.3f),
            Saddle = Mat("Saddle", new Color(0.1f, 0.07f, 0.05f), 0f, 0.3f),
            Marker = Mat("Marker", new Color(1f, 0.5f, 0.05f), 0f, 0.5f),
            Cone = Mat("Cone", new Color(1f, 0.45f, 0.05f), 0f, 0.4f),
            Pillar = Mat("Pillar", new Color(0.25f, 0.45f, 0.75f), 0f, 0.3f),
            Ground = CreateGroundMaterial(),
        };
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

    static Material CreateGroundMaterial()
    {
        // Szachownica z lekkim szumem – bez tekstury nie widać prędkości ani skrętu.
        string texPath = MatDir + "/Checker.png";
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var rng = new System.Random(7);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool a = (x < size / 2) ^ (y < size / 2);
                float baseV = a ? 0.42f : 0.30f;
                float noise = (float)(rng.NextDouble() - 0.5) * 0.06f;
                float v = baseV + noise;
                tex.SetPixel(x, y, new Color(v, v * 1.05f, v * 0.95f, 1f));
            }
        }
        File.WriteAllBytes(texPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(texPath);

        var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Bilinear;
        importer.anisoLevel = 8;
        importer.SaveAndReimport();

        Material m = Mat("Ground", Color.white, 0f, 0.1f);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        m.SetTexture("_BaseMap", texture);
        m.SetTextureScale("_BaseMap", new Vector2(150f, 150f));
        return m;
    }

    // ---------------------------------------------------------------- rower

    static GameObject CreateBikePrefab(Mats m)
    {
        // Korzeń: fizyka + sterowanie.
        var root = new GameObject("Bike");
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 15f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        var noFriction = CreateNoFrictionMaterial();
        AddSphereCollider(root, new Vector3(0f, WheelRadius, RearAxleZ), TireOuterRadius, noFriction);
        AddSphereCollider(root, new Vector3(0f, WheelRadius, FrontAxleZ), TireOuterRadius, noFriction);

        var controller = root.AddComponent<BikeController>();

        // Część wizualna – przechyla się w zakrętach.
        var visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);

        BuildFrame(visual, m);
        Transform rearWheel = BuildWheel(visual, "RearWheel", new Vector3(0f, WheelRadius, RearAxleZ), m);

        // Korba + pedały.
        PedalDrive pedals = BuildCrank(visual, m);

        // Zespół kierownicy (obraca się wokół osi pionowej w główce ramy).
        var steeringGo = new GameObject("SteeringAssembly");
        steeringGo.transform.SetParent(visual, false);
        Vector3 pivot = new Vector3(0f, 0.92f, 0.45f);
        steeringGo.transform.localPosition = pivot;
        Transform frontWheel = BuildSteeringAssembly(steeringGo.transform, pivot, m, out GripHandle[] grips);
        var steering = steeringGo.AddComponent<HandlebarSteering>();

        // Kamera z perspektywy rowerzysty, patrząca lekko w dół na kierownicę.
        var camRig = new GameObject("CameraRig").transform;
        camRig.SetParent(visual, false); // kamera przechyla się razem z rowerem
        camRig.localPosition = new Vector3(0f, 1.68f, -0.4f);
        camRig.localRotation = Quaternion.Euler(24f, 0f, 0f);

        var camGo = new GameObject("BikeCamera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(camRig, false);
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 70f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 400f;
        camGo.AddComponent<AudioListener>();

        // Okablowanie referencji.
        SetRef(steering, "viewCamera", cam);
        SetRefs(steering, "grips", grips);
        SetRef(pedals, "leftPedal", pedals.transform.Find("PedalLeft"));
        SetRef(pedals, "rightPedal", pedals.transform.Find("PedalRight"));

        SetRef(controller, "pedals", pedals);
        SetRef(controller, "steering", steering);
        SetRef(controller, "leanPivot", visual);
        SetRefs(controller, "spinningWheels", new Object[] { rearWheel, frontWheel });

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static PhysicsMaterial CreateNoFrictionMaterial()
    {
        const string path = "Assets/Physics/BikeNoFriction.physicMaterial";
        var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (pm == null)
        {
            pm = new PhysicsMaterial("BikeNoFriction");
            AssetDatabase.CreateAsset(pm, path);
        }
        pm.dynamicFriction = 0f;
        pm.staticFriction = 0f;
        pm.frictionCombine = PhysicsMaterialCombine.Minimum;
        pm.bounciness = 0f;
        pm.bounceCombine = PhysicsMaterialCombine.Minimum;
        EditorUtility.SetDirty(pm);
        return pm;
    }

    static void AddSphereCollider(GameObject go, Vector3 center, float radius, PhysicsMaterial pm)
    {
        var c = go.AddComponent<SphereCollider>();
        c.center = center;
        c.radius = radius;
        c.sharedMaterial = pm;
    }

    static void BuildFrame(Transform visual, Mats m)
    {
        var frame = new GameObject("Frame").transform;
        frame.SetParent(visual, false);

        Vector3 rearAxle = new Vector3(0f, WheelRadius, RearAxleZ);
        Vector3 bb = new Vector3(0f, 0.30f, 0f);
        Vector3 seatTop = new Vector3(0f, 0.97f, -0.28f);
        Vector3 headTop = new Vector3(0f, 0.95f, 0.45f);
        Vector3 headBottom = new Vector3(0f, 0.84f, 0.46f);

        Bar(frame, "TopTube", new Vector3(0f, 0.93f, -0.27f), new Vector3(0f, 0.93f, 0.45f), 0.016f, m.Frame);
        Bar(frame, "DownTube", bb, headBottom, 0.019f, m.Frame);
        Bar(frame, "SeatTube", bb, seatTop, 0.016f, m.Frame);
        Bar(frame, "HeadTube", headBottom, headTop, 0.022f, m.Frame);
        foreach (float side in new[] { -1f, 1f })
        {
            Bar(frame, "ChainStay", bb, rearAxle + new Vector3(0.05f * side, 0f, 0f), 0.01f, m.Frame);
            Bar(frame, "SeatStay", seatTop - new Vector3(0f, 0.04f, 0f), rearAxle + new Vector3(0.05f * side, 0f, 0f), 0.009f, m.Frame);
        }

        Bar(frame, "SeatPost", seatTop - new Vector3(0f, 0.05f, 0f), seatTop + new Vector3(0f, 0.1f, 0f), 0.013f, m.Metal);
        Prim(PrimitiveType.Cube, "Saddle", frame, seatTop + new Vector3(0f, 0.12f, -0.02f), new Vector3(0.13f, 0.04f, 0.28f), m.Saddle);
    }

    static Transform BuildWheel(Transform parent, string name, Vector3 center, Mats m)
    {
        var wheel = new GameObject(name).transform;
        wheel.SetParent(parent, false);
        wheel.localPosition = center;

        const int segments = 28;
        float r = WheelRadius;
        float segLength = 2f * Mathf.PI * r / segments * 1.08f;
        for (int i = 0; i < segments; i++)
        {
            float theta = i * 360f / segments;
            float rad = theta * Mathf.Deg2Rad;
            var seg = Prim(PrimitiveType.Cube, "Tire", wheel,
                new Vector3(0f, r * Mathf.Cos(rad), r * Mathf.Sin(rad)),
                new Vector3(0.045f, 0.035f, segLength), m.Rubber);
            seg.transform.localRotation = Quaternion.Euler(theta, 0f, 0f);
        }

        for (int i = 0; i < 4; i++)
        {
            float rad = i * 45f * Mathf.Deg2Rad;
            Vector3 a = new Vector3(0f, (r - 0.02f) * Mathf.Cos(rad), (r - 0.02f) * Mathf.Sin(rad));
            Bar(wheel, "Spoke", a, -a, 0.003f, m.Metal);
        }

        var hub = Prim(PrimitiveType.Cylinder, "Hub", wheel, Vector3.zero, new Vector3(0.04f, 0.045f, 0.04f), m.Metal);
        hub.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

        // Pomarańczowy znacznik na obręczy – widać obrót koła.
        Prim(PrimitiveType.Cube, "Marker", wheel, new Vector3(0f, r - 0.03f, 0f), new Vector3(0.06f, 0.07f, 0.03f), m.Marker);
        return wheel;
    }

    static PedalDrive BuildCrank(Transform visual, Mats m)
    {
        var crank = new GameObject("Crank").transform;
        crank.SetParent(visual, false);
        crank.localPosition = new Vector3(0f, 0.30f, 0f);

        const float arm = 0.17f;
        var ring = Prim(PrimitiveType.Cylinder, "Chainring", crank, new Vector3(0.045f, 0f, 0f), new Vector3(0.2f, 0.004f, 0.2f), m.Metal);
        ring.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

        Bar(crank, "CrankArmRight", new Vector3(0.07f, 0f, 0f), new Vector3(0.07f, arm, 0f), 0.012f, m.Metal);
        Bar(crank, "CrankArmLeft", new Vector3(-0.07f, 0f, 0f), new Vector3(-0.07f, -arm, 0f), 0.012f, m.Metal);

        var right = new GameObject("PedalRight").transform;
        right.SetParent(crank, false);
        right.localPosition = new Vector3(0.07f, arm, 0f);
        Prim(PrimitiveType.Cube, "PedalBody", right, new Vector3(0.06f, 0f, 0f), new Vector3(0.1f, 0.02f, 0.1f), m.Rubber);

        var left = new GameObject("PedalLeft").transform;
        left.SetParent(crank, false);
        left.localPosition = new Vector3(-0.07f, -arm, 0f);
        Prim(PrimitiveType.Cube, "PedalBody", left, new Vector3(-0.06f, 0f, 0f), new Vector3(0.1f, 0.02f, 0.1f), m.Rubber);

        return crank.gameObject.AddComponent<PedalDrive>();
    }

    static Transform BuildSteeringAssembly(Transform asm, Vector3 pivot, Mats m, out GripHandle[] grips)
    {
        // Wszystko poniżej w lokalnych współrzędnych zespołu (początek = punkt obrotu).
        Bar(asm, "Stem", new Vector3(0f, -0.02f, 0f), new Vector3(0f, 0.18f, 0f), 0.014f, m.Metal);
        Bar(asm, "Handlebar", new Vector3(-0.26f, 0.18f, 0f), new Vector3(0.26f, 0.18f, 0f), 0.011f, m.Metal);

        // Widły.
        Vector3 axle = new Vector3(0f, WheelRadius - pivot.y, FrontAxleZ - pivot.z);
        Bar(asm, "ForkCrown", new Vector3(-0.045f, -0.06f, 0f), new Vector3(0.045f, -0.06f, 0f), 0.012f, m.Metal);
        Bar(asm, "ForkLeft", new Vector3(-0.045f, -0.06f, 0f), axle + new Vector3(-0.045f, 0f, 0f), 0.011f, m.Metal);
        Bar(asm, "ForkRight", new Vector3(0.045f, -0.06f, 0f), axle + new Vector3(0.045f, 0f, 0f), 0.011f, m.Metal);

        Transform wheel = BuildWheel(asm, "FrontWheel", axle, m);

        // Chwyty: cienki widoczny model + gruby trigger-collider do łapania myszką.
        grips = new[]
        {
            BuildGrip(asm, "GripLeft", -0.31f, m),
            BuildGrip(asm, "GripRight", 0.31f, m),
        };
        return wheel;
    }

    static GripHandle BuildGrip(Transform asm, string name, float x, Mats m)
    {
        var go = new GameObject(name);
        go.transform.SetParent(asm, false);
        go.transform.localPosition = new Vector3(x, 0.18f, 0f);

        var visual = Prim(PrimitiveType.Cylinder, "Mesh", go.transform, Vector3.zero, new Vector3(0.036f, 0.06f, 0.036f), m.Grip);
        visual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

        var col = go.AddComponent<CapsuleCollider>();
        col.direction = 0; // oś X
        col.radius = 0.06f;
        col.height = 0.2f;
        col.isTrigger = true;

        var grip = go.AddComponent<GripHandle>();
        SetRef(grip, "targetRenderer", visual.GetComponent<Renderer>());
        return grip;
    }

    // ---------------------------------------------------------------- scena

    static void CreateScene(GameObject bikePrefab, Mats m)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Domyślna kamera niepotrzebna – kamera jest częścią roweru.
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            Object.DestroyImmediate(cam.gameObject);

        // Ziemia.
        var ground = Prim(PrimitiveType.Cube, "Ground", null, new Vector3(0f, -0.5f, 0f), new Vector3(300f, 1f, 300f), m.Ground, keepCollider: true);
        ground.isStatic = true;

        // Slalom z pachołków.
        var course = new GameObject("Slalom").transform;
        for (int i = 0; i < 10; i++)
        {
            float x = (i % 2 == 0) ? -2.2f : 2.2f;
            var cone = Prim(PrimitiveType.Cylinder, $"Cone{i}", course, new Vector3(x, 0.5f, 18f + i * 8f), new Vector3(0.45f, 0.5f, 0.45f), m.Cone, keepCollider: true);
            cone.isStatic = true;
        }

        // Filary dookoła – punkty orientacyjne.
        var pillars = new GameObject("Pillars").transform;
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI * 2f / 16f;
            float r = 55f;
            float h = 4f + (i % 4) * 2f;
            var p = Prim(PrimitiveType.Cube, $"Pillar{i}", pillars, new Vector3(Mathf.Sin(a) * r, h * 0.5f, Mathf.Cos(a) * r), new Vector3(2f, h, 2f), m.Pillar, keepCollider: true);
            p.isStatic = true;
        }

        // Rower.
        var bike = (GameObject)PrefabUtility.InstantiatePrefab(bikePrefab);
        bike.transform.position = new Vector3(0f, 0.05f, 0f);
        bike.transform.rotation = Quaternion.identity;

        // HUD.
        var hud = new GameObject("HUD").AddComponent<BikeHud>();
        SetRef(hud, "bike", bike.GetComponent<BikeController>());
        SetRef(hud, "pedals", bike.GetComponentInChildren<PedalDrive>());
        SetRef(hud, "steering", bike.GetComponentInChildren<HandlebarSteering>());

        EditorSceneManager.SaveScene(scene, ScenePath);

        // Dodaj scenę na początek Build Settings.
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        list.RemoveAll(s => s.path == ScenePath);
        list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    // ---------------------------------------------------------------- pomocnicze

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool keepCollider = false)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    /// <summary>Cylinder rozpięty między dwoma punktami (w przestrzeni lokalnej rodzica).</summary>
    static GameObject Bar(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material mat)
    {
        Vector3 d = b - a;
        var go = Prim(PrimitiveType.Cylinder, name, parent, (a + b) * 0.5f, new Vector3(radius * 2f, d.magnitude * 0.5f, radius * 2f), mat);
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
        return go;
    }

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
        for (int i = 0; i < values.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
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
