using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Niskopoligonowa roślinność: proceduralne siatki drzew liściastych, iglastych, palm i krzaków
/// oraz rozmieszczenie ich na terenie według przepisu (plants.json). Używane przez MissionMapBuilder.
/// </summary>
public static class MissionVegetation
{
    const string RecipeDir = "Assets/Mission/Recipe";
    const string MeshDir = "Assets/Mission/Generated/Meshes";
    const string MatDir = "Assets/Materials/Mission";

    // Wymiary modeli wzorcowych (m) – plant jest skalowany do wysokości i promienia korony z przepisu.
    static readonly float[] RefHeight = { 10.3f, 10f, 10.8f, 1.55f };
    static readonly float[] RefRadius = { 3.0f, 2.8f, 3.8f, 1.1f };
    static readonly string[] TypeNames = { "Broadleaf", "Conifer", "Palm", "Bush" };

    // ------------------------------------------------------------ siatka niskopoligonowa (płaskie cieniowanie)

    class Poly
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<int>[] t = { new List<int>(), new List<int>() };

        /// <summary>Trójkąt zwrócony ścianą przednią od punktu wewnętrznego (interior).</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, int sub, Vector3 interior)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, (a + b + c) / 3f - interior) < 0f) { var tmp = b; b = c; c = tmp; }
            Add(a, b, c, sub);
        }

        /// <summary>Trójkąt widoczny z obu stron (liście palmy).</summary>
        public void TriBoth(Vector3 a, Vector3 b, Vector3 c, int sub)
        {
            Add(a, b, c, sub);
            Add(a, c, b, sub);
        }

        void Add(Vector3 a, Vector3 b, Vector3 c, int sub)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            t[sub].Add(i); t[sub].Add(i + 1); t[sub].Add(i + 2);
        }

        /// <summary>Ścięty stożek (pień / warstwa choinki) między dwoma środkami.</summary>
        public void Frustum(Vector3 bottom, float r0, Vector3 top, float r1, int sides, int sub)
        {
            Vector3 axis = (top - bottom);
            Vector3 right = Vector3.Cross(axis.normalized, Vector3.forward);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            right.Normalize();
            Vector3 fwd = Vector3.Cross(right, axis.normalized).normalized;
            Vector3 mid = (bottom + top) * 0.5f;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 b0 = bottom + (right * Mathf.Cos(a0) + fwd * Mathf.Sin(a0)) * r0;
                Vector3 b1 = bottom + (right * Mathf.Cos(a1) + fwd * Mathf.Sin(a1)) * r0;
                Vector3 t0 = top + (right * Mathf.Cos(a0) + fwd * Mathf.Sin(a0)) * r1;
                Vector3 t1 = top + (right * Mathf.Cos(a1) + fwd * Mathf.Sin(a1)) * r1;
                Tri(b0, b1, t1, sub, mid);
                if (r1 > 0.06f) Tri(b0, t1, t0, sub, mid);
                Tri(bottom, b1, b0, sub, mid);       // spód (niewidoczny, ale zamyka bryłę)
            }
        }

        /// <summary>Ikosfera (80 trójkątów) z lekkim losowym zaburzeniem – korona drzewa / krzak.</summary>
        public void Ico(Vector3 center, Vector3 radii, int sub, int seed)
        {
            float tt = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new Vector3(-1, tt, 0), new Vector3(1, tt, 0), new Vector3(-1, -tt, 0), new Vector3(1, -tt, 0),
                new Vector3(0, -1, tt), new Vector3(0, 1, tt), new Vector3(0, -1, -tt), new Vector3(0, 1, -tt),
                new Vector3(tt, 0, -1), new Vector3(tt, 0, 1), new Vector3(-tt, 0, -1), new Vector3(-tt, 0, 1),
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            var faces = new List<int[]>
            {
                new[]{0,11,5}, new[]{0,5,1}, new[]{0,1,7}, new[]{0,7,10}, new[]{0,10,11},
                new[]{1,5,9}, new[]{5,11,4}, new[]{11,10,2}, new[]{10,7,6}, new[]{7,1,8},
                new[]{3,9,4}, new[]{3,4,2}, new[]{3,2,6}, new[]{3,6,8}, new[]{3,8,9},
                new[]{4,9,5}, new[]{2,4,11}, new[]{6,2,10}, new[]{8,6,7}, new[]{9,8,1},
            };
            var cache = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int idx)) return idx;
                verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                cache[key] = verts.Count - 1;
                return verts.Count - 1;
            }
            var sub1 = new List<int[]>();
            foreach (var f in faces)
            {
                int a = Mid(f[0], f[1]), b = Mid(f[1], f[2]), c = Mid(f[2], f[0]);
                sub1.Add(new[] { f[0], a, c }); sub1.Add(new[] { f[1], b, a });
                sub1.Add(new[] { f[2], c, b }); sub1.Add(new[] { a, b, c });
            }
            var rng = new System.Random(seed);
            var pos = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                float j = 1f + ((float)rng.NextDouble() - 0.5f) * 0.22f;
                pos[i] = center + Vector3.Scale(verts[i] * j, radii);
            }
            foreach (var f in sub1) Tri(pos[f[0]], pos[f[1]], pos[f[2]], sub, center);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, subMeshCount = 2 };
            mesh.SetVertices(v);
            mesh.SetTriangles(t[0], 0);
            mesh.SetTriangles(t[1], 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    static Mesh SaveMesh(Mesh mesh)
    {
        string path = MeshDir + "/Plant_" + mesh.name + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static Mesh BuildBroadleaf()
    {
        var p = new Poly();
        p.Frustum(Vector3.zero, 0.38f, new Vector3(0f, 4.4f, 0f), 0.24f, 6, 0);
        p.Ico(new Vector3(0f, 7.2f, 0f), new Vector3(3.0f, 3.1f, 3.0f), 1, 11);
        p.Ico(new Vector3(1.3f, 6.0f, 0.8f), new Vector3(2.0f, 1.8f, 2.0f), 1, 12);
        return SaveMesh(p.ToMesh("Broadleaf"));
    }

    static Mesh BuildConifer()
    {
        var p = new Poly();
        p.Frustum(Vector3.zero, 0.32f, new Vector3(0f, 2.2f, 0f), 0.24f, 6, 0);
        p.Frustum(new Vector3(0f, 1.5f, 0f), 2.8f, new Vector3(0f, 5.6f, 0f), 0.05f, 8, 1);
        p.Frustum(new Vector3(0f, 3.8f, 0f), 2.2f, new Vector3(0f, 7.7f, 0f), 0.05f, 8, 1);
        p.Frustum(new Vector3(0f, 6.2f, 0f), 1.5f, new Vector3(0f, 10f, 0f), 0.05f, 8, 1);
        return SaveMesh(p.ToMesh("Conifer"));
    }

    static Mesh BuildPalm()
    {
        var p = new Poly();
        var pts = new[]
        {
            new Vector3(0f, 0f, 0f), new Vector3(0.1f, 2.6f, 0f), new Vector3(0.35f, 5.6f, 0f),
            new Vector3(0.8f, 8.3f, 0f), new Vector3(1.4f, 10.3f, 0f),
        };
        var radii = new[] { 0.36f, 0.30f, 0.26f, 0.22f, 0.18f };
        for (int i = 0; i < pts.Length - 1; i++) p.Frustum(pts[i], radii[i], pts[i + 1], radii[i + 1], 5, 0);

        Vector3 top = pts[pts.Length - 1];
        const int fronds = 9;
        for (int i = 0; i < fronds; i++)
        {
            float a = i * Mathf.PI * 2f / fronds;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            Vector3 mid = top + dir * 1.9f + Vector3.up * 0.9f;
            Vector3 tip = top + dir * 3.8f - Vector3.up * 0.9f;
            p.TriBoth(top, mid + side * 0.55f, mid - side * 0.55f, 1);
            p.TriBoth(mid + side * 0.55f, tip, mid - side * 0.55f, 1);
        }
        return SaveMesh(p.ToMesh("Palm"));
    }

    static Mesh BuildBush()
    {
        var p = new Poly();
        p.Ico(new Vector3(0f, 0.75f, 0f), new Vector3(1.1f, 0.8f, 1.1f), 1, 21);
        p.Ico(new Vector3(0.7f, 0.5f, 0.4f), new Vector3(0.7f, 0.55f, 0.7f), 1, 22);
        return SaveMesh(p.ToMesh("Bush"));
    }

    // ------------------------------------------------------------ materiały

    static Material Mat(string name, Color color)
    {
        string path = MatDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Smoothness", 0.05f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------ rozmieszczenie

    public static void Build(TerrainMeta meta, Terrain[] terrains)
    {
        string plantsPath = RecipeDir + "/plants.json";
        if (!File.Exists(plantsPath)) { Debug.LogWarning("[MissionVegetation] Brak plants.json – pomijam roślinność."); return; }
        var list = JsonUtility.FromJson<PlantList>(File.ReadAllText(plantsPath));
        byte[] classes = File.ReadAllBytes(RecipeDir + "/terrain_class.bin");

        var meshes = new[] { BuildBroadleaf(), BuildConifer(), BuildPalm(), BuildBush() };
        var trunk = Mat("TreeTrunk", new Color(0.32f, 0.22f, 0.14f));
        var foliage = new[]
        {
            new[] { Mat("LeafBroadA", new Color(0.22f, 0.45f, 0.17f)), Mat("LeafBroadB", new Color(0.30f, 0.50f, 0.16f)), Mat("LeafBroadC", new Color(0.18f, 0.38f, 0.20f)) },
            new[] { Mat("LeafConiferA", new Color(0.10f, 0.30f, 0.17f)), Mat("LeafConiferB", new Color(0.14f, 0.35f, 0.18f)) },
            new[] { Mat("LeafPalmA", new Color(0.30f, 0.55f, 0.18f)), Mat("LeafPalmB", new Color(0.38f, 0.58f, 0.22f)) },
            new[] { Mat("LeafBushA", new Color(0.26f, 0.42f, 0.18f)), Mat("LeafBushB", new Color(0.34f, 0.44f, 0.20f)), Mat("LeafBushC", new Color(0.40f, 0.40f, 0.18f)) },
        };

        var root = new GameObject("Vegetation").transform;
        var groups = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            groups[i] = new GameObject(TypeNames[i]).transform;
            groups[i].SetParent(root, false);
        }

        int placed = 0, skipped = 0;
        var rng = new System.Random(5);
        for (int i = 0; i + 4 < list.d.Length; i += 5)
        {
            int type = Mathf.Clamp((int)list.d[i], 0, 3);
            float x = list.d[i + 1], z = list.d[i + 2], h = list.d[i + 3], r = list.d[i + 4];

            // Nie sadzimy na jezdniach, w wodzie ani poza terenem.
            int cx = Mathf.RoundToInt((x - meta.minX) / meta.cell), cy = Mathf.RoundToInt((z - meta.minY) / meta.cell);
            if (cx < 0 || cy < 0 || cx >= meta.samplesX || cy >= meta.samplesY) { skipped++; continue; }
            if (classes[cy * meta.samplesX + cx] == 1) { skipped++; continue; }
            float y = GroundHeight(terrains, x, z, out bool ok);
            if (!ok || y < 0.4f) { skipped++; continue; }

            float sy = h / RefHeight[type];
            float sxz = Mathf.Clamp(r / RefRadius[type], 0.6f * sy, 1.8f * sy);
            sy *= 0.9f + (float)rng.NextDouble() * 0.2f;

            var go = new GameObject(TypeNames[type]);
            go.transform.SetParent(groups[type], false);
            go.transform.SetPositionAndRotation(new Vector3(x, y - 0.1f, z), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
            go.transform.localScale = new Vector3(sxz, sy, sxz);
            go.AddComponent<MeshFilter>().sharedMesh = meshes[type];
            var mats = foliage[type];
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { trunk, mats[rng.Next(mats.Length)] };

            if (type != 3)
            {
                // Pień jest przeszkodą (wjechanie w drzewo = upadek); korona nie ma kolizji.
                var cap = go.AddComponent<CapsuleCollider>();
                cap.radius = 0.4f;
                cap.height = Mathf.Min(RefHeight[type] * 0.6f, 6f);
                cap.center = new Vector3(0f, cap.height * 0.5f, 0f);
            }
            go.isStatic = true;
            placed++;
        }
        Debug.Log("[MissionVegetation] Posadzono: " + placed + ", pominięto: " + skipped);
    }

    static float GroundHeight(Terrain[] terrains, float x, float z, out bool ok)
    {
        foreach (var t in terrains)
        {
            Vector3 o = t.transform.position, sz = t.terrainData.size;
            if (x >= o.x && x <= o.x + sz.x && z >= o.z && z <= o.z + sz.z)
            {
                ok = true;
                return t.SampleHeight(new Vector3(x, 0f, z)) + o.y;
            }
        }
        ok = false;
        return 0f;
    }
}
