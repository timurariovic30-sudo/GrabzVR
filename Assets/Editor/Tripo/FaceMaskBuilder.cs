using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Чёрная лицевая маска грабителя (косметика RobberMask, слот FaceMask) по форме лица Gorilla_Final.
///
/// Маска закрывает только переднюю часть лица: от середины лба до подбородка; макушка, затылок,
/// верх лба и уши открыты (корона на макушке её не задевает). Прорези узкие: овалы ровно вокруг
/// белков глаз и щель по линии губ. По краям и вокруг прорезей — окантовка-шов.
///
/// Как строится: сетка в плоскости лица (оси корня префаба, +Z — вперёд) проецируется лучами на
/// запечённую голову игрока, приподнимается на 5 мм и сглаживается; вершины у краёв прорезей
/// притягиваются к контуру, чтобы край был ровным. Меш хранится в осях кости Head — поэтому одна
/// и та же маска подходит всем копиям гориллы (VR-игрок, ПК-игрок в сценах, сетевая горилла).
///
/// Идемпотентный: заменяет только содержимое объекта RobberMask (и образец на витрине в хабе).
/// Меню: Tools/Heist/Player/Face Mask. Batchmode: -executeMethod FaceMaskBuilder.RunBatch
/// </summary>
public static class FaceMaskBuilder
{
    const string VRPrefab = "Assets/Prefabs/Player/VRGorillaPlayer.prefab";
    const string NetPrefab = "Assets/Prefabs/Network/NetworkGorillaAvatar.prefab";
    const string HubPath = "Assets/Scenes/MainHub.unity";
    const string HeistPath = "Assets/Scenes/HeistLevel_Test.unity";
    const string MeshDir = "Assets/Art/Meshes/Cosmetics";
    const string MatDir = "Assets/Art/Materials/Player";
    const string TexDir = "Assets/Art/Textures/Cosmetics";

    /// Два цвета вязаной балаклавы (оба — слот FaceMask, носятся с любым головным убором).
    static readonly (string id, Color fabric, Color seam, string matKey)[] Variants =
    {
        ("RobberMask", new Color32(0x16, 0x16, 0x1A, 0xFF), new Color32(0x3A, 0x3A, 0x42, 0xFF), "Black"),
        ("RobberMaskWhite", new Color32(0xEA, 0xEA, 0xEF, 0xFF), new Color32(0xC4, 0xC4, 0xCC, 0xFF), "White"),
    };

    // ---- разметка лица в осях корня префаба VRGorillaPlayer (снята с фронтального снимка головы)
    const float Cx = 0.004f;                                   // середина лица по X
    static readonly Vector2 EyeL = new Vector2(Cx - 0.0625f, 1.464f), EyeR = new Vector2(Cx + 0.0625f, 1.464f);
    const float EyeA = 0.043f, EyeB = 0.026f;                  // полуоси прорези глаза (белок 0.040 × 0.024)
    static readonly Vector2 Mouth = new Vector2(Cx, 1.314f);
    const float MouthA = 0.084f, MouthB = 0.019f;              // щель по линии губ
    const float Lift = 0.0075f;                                // ткань над кожей: объёмно и без клиппинга
    const float Step = 0.006f;                                 // шаг сетки
    const float RimR = 0.0048f;                                // радиус прошитой окантовки
    const float RibTiling = 5f;                                // рубчик ~4 мм (UV-единица = 1/6 м, в текстуре 8 рёбер)

    static readonly StringBuilder log = new StringBuilder();

    [MenuItem("Tools/Heist/Player/Face Mask (robber, fits with crown)")]
    static void Menu() => Debug.Log(Run());

    public static void RunBatch()
    {
        string r;
        try { r = Run(); } catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); return; }
        Debug.Log(r);
        EditorApplication.Exit(r.Contains("ERROR") ? 1 : 0);
    }

    public static string Run()
    {
        log.Clear().AppendLine("[FaceMaskBuilder]");
        MapKit.EnsureFolder(MeshDir); MapKit.EnsureFolder(TexDir);
        var mat = Variants.Select(v => Materials(v.matKey, v.fabric, v.seam)).ToArray();
        Mesh headLocalMesh = null, faceMesh = null;

        // 1) меш маски — по голове VR-игрока
        var root = PrefabUtility.LoadPrefabContents(VRPrefab);
        try
        {
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var rig = root.GetComponentInChildren<GorillaFinalRig>(true);
            var head = rig.headBone;
            var hsmr = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => s.name == "Head");
            BuildMeshes(hsmr, head, out headLocalMesh, out faceMesh);
            headLocalMesh = Save(headLocalMesh); faceMesh = Save(faceMesh);
            Apply(rig, headLocalMesh, mat, "VRGorillaPlayer");
            PrefabUtility.SaveAsPrefabAsset(root, VRPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        // 2) сетевая горилла
        var net = PrefabUtility.LoadPrefabContents(NetPrefab);
        try
        {
            var rig = net.GetComponentInChildren<GorillaFinalRig>(true);
            if (rig) { Apply(rig, headLocalMesh, mat, "NetworkGorillaAvatar"); PrefabUtility.SaveAsPrefabAsset(net, NetPrefab); }
            else log.AppendLine("  ERROR: network avatar rig not found");
        }
        finally { PrefabUtility.UnloadPrefabContents(net); }

        // 3) ПК-гориллы в сценах + образец на витрине хаба
        foreach (var path in new[] { HubPath, HeistPath })
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            foreach (var rig in Object.FindObjectsByType<GorillaFinalRig>(FindObjectsInactive.Include))
                if (!PrefabUtility.IsPartOfPrefabInstance(rig) || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(rig) != VRPrefab)
                    Apply(rig, headLocalMesh, mat, $"{scene.name}/{rig.name}");
            if (path == HubPath) ShowcaseSample(faceMesh, mat);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) log.AppendLine("  ERROR: save failed " + path);
        }
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // =========================================================================== материалы: вязаный рубчик + прошитый шов
    /// [0] — ткань (вертикальный трикотажный рубчик: albedo + normal map), [1] — окантовка со стежками.
    static Material[] Materials(string key, Color fabric, Color seam)
    {
        var knitA = Tex("T_Knit_Rib_Albedo", false, KnitAlbedo);
        var knitN = Tex("T_Knit_Rib_Normal", true, KnitNormal);
        var seamA = Tex("T_Knit_Seam_Albedo", false, SeamAlbedo);
        var seamN = Tex("T_Knit_Seam_Normal", true, SeamNormal);
        return new[]
        {
            Lit($"M_Cos_Balaclava_{key}", fabric, knitA, knitN, new Vector2(RibTiling, RibTiling), 0.9f, 0.12f),
            Lit($"M_Cos_Balaclava_{key}_Seam", seam, seamA, seamN, new Vector2(10f, 1f), 0.7f, 0.18f),
        };
    }

    static Material Lit(string name, Color c, Texture2D albedo, Texture2D normal, Vector2 tiling, float bump, float smooth)
    {
        string path = $"{MatDir}/{name}.mat";
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetColor("_BaseColor", c);
        m.SetTexture("_BaseMap", albedo); m.SetTextureScale("_BaseMap", tiling);
        m.SetTexture("_BumpMap", normal); m.SetTextureScale("_BumpMap", tiling);
        m.SetFloat("_BumpScale", bump); m.EnableKeyword("_NORMALMAP");
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Cull", 0f);                                        // двусторонняя: край ткани не просвечивает
        m.doubleSidedGI = true;
        m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        EditorUtility.SetDirty(m);
        return m;
    }

    const int TexN = 128;

    // высота трикотажа: 8 вертикальных рёбер, в каждом — «косички» петель (V-образный рисунок вязки)
    static float KnitHeight(float u, float v)
    {
        float rib = u * 8f, f = rib - Mathf.Floor(rib);                 // 0..1 поперёк ребра
        float ribH = Mathf.Sin(f * Mathf.PI);                           // выпуклое ребро
        float loop = v * 32f + Mathf.Abs(f - 0.5f) * 1.6f;              // V-петли вдоль ребра
        float loopH = 0.5f + 0.5f * Mathf.Cos(loop * Mathf.PI * 2f);
        return ribH * (0.75f + 0.25f * loopH);
    }

    static Color KnitAlbedo(float u, float v)
    {
        float h = KnitHeight(u, v);
        float k = 0.62f + 0.38f * h;                                    // ложбинки темнее → полоски читаются при любом свете
        return new Color(k, k, k, 1);
    }

    static Color KnitNormal(float u, float v) => HeightToNormal(KnitHeight, u, v, 2.2f);

    // шов: тёмная ложбинка вдоль трубки + короткие светлые стежки поперёк
    static float SeamHeight(float u, float v)
    {
        float s = u - Mathf.Floor(u);
        float stitch = s > 0.15f && s < 0.75f ? 1f : 0f;
        return stitch * (0.6f + 0.4f * Mathf.Sin(v * Mathf.PI));
    }

    static Color SeamAlbedo(float u, float v) { float k = 0.55f + 0.45f * SeamHeight(u, v); return new Color(k, k, k, 1); }
    static Color SeamNormal(float u, float v) => HeightToNormal(SeamHeight, u, v, 1.5f);

    static Color HeightToNormal(System.Func<float, float, float> h, float u, float v, float strength)
    {
        float e = 1f / TexN;
        float dx = (h(u + e, v) - h(u - e, v)) * strength;
        float dy = (h(u, v + e) - h(u, v - e)) * strength;
        var n = new Vector3(-dx, -dy, 1f).normalized;
        return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1);
    }

    static Texture2D Tex(string name, bool normal, System.Func<float, float, Color> fn)
    {
        string path = $"{TexDir}/{name}.png";
        var t = new Texture2D(TexN, TexN, TextureFormat.RGB24, false, normal);
        for (int y = 0; y < TexN; y++)
            for (int x = 0; x < TexN; x++) t.SetPixel(x, y, fn((x + 0.5f) / TexN, (y + 0.5f) / TexN));
        t.Apply();
        System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        imp.sRGBTexture = !normal;
        imp.wrapMode = TextureWrapMode.Repeat; imp.filterMode = FilterMode.Bilinear;
        imp.maxTextureSize = TexN; imp.mipmapEnabled = true;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // =========================================================================== контуры (плоскость лица)
    static List<Vector2> Chaikin(List<Vector2> p, int it)
    {
        for (int k = 0; k < it; k++)
        {
            var r = new List<Vector2>();
            for (int i = 0; i < p.Count; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % p.Count];
                r.Add(Vector2.Lerp(a, b, 0.25f)); r.Add(Vector2.Lerp(a, b, 0.75f));
            }
            p = r;
        }
        return p;
    }

    /// Внешний край маски: широкий у висков (поверх бровей), сужается к подбородку, уши не трогает.
    static List<Vector2> Outline()
    {
        var p = new List<Vector2>
        {
            new Vector2(0, 1.574f), new Vector2(0.075f, 1.570f), new Vector2(0.122f, 1.552f), new Vector2(0.142f, 1.515f),
            new Vector2(0.146f, 1.45f), new Vector2(0.142f, 1.38f), new Vector2(0.13f, 1.30f), new Vector2(0.105f, 1.252f),
            new Vector2(0.055f, 1.228f), new Vector2(0, 1.222f),
        };
        var full = new List<Vector2>(p);
        for (int i = p.Count - 2; i >= 1; i--) full.Add(new Vector2(-p[i].x, p[i].y));
        return Chaikin(full.Select(v => new Vector2(v.x + Cx, v.y)).ToList(), 3);
    }

    /// Прорезь глаза — миндалевидная (внутренний угол чуть ниже внешнего), строго вокруг белка.
    static List<Vector2> Eye(Vector2 c, float side)
    {
        var r = new List<Vector2>();
        for (int i = 0; i < 48; i++)
        {
            float a = i * Mathf.PI * 2 / 48;
            float x = Mathf.Cos(a), y = Mathf.Sin(a);
            float tilt = -0.12f * x * side;                            // наружный угол чуть выше
            r.Add(c + new Vector2(EyeA * x, EyeB * y * (1f - 0.18f * x * x) + EyeB * tilt * 0.5f));
        }
        return r;
    }

    static List<Vector2> Ellipse(Vector2 c, float a, float b, int n = 56)
    {
        var r = new List<Vector2>();
        for (int i = 0; i < n; i++) { float t = i * Mathf.PI * 2 / n; r.Add(c + new Vector2(a * Mathf.Cos(t), b * Mathf.Sin(t))); }
        return r;
    }

    static bool InPoly(List<Vector2> poly, Vector2 p)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) c = !c;
        return c;
    }

    static Vector2 Nearest(List<Vector2> poly, Vector2 p, out float d)
    {
        d = float.MaxValue; Vector2 best = p;
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Count], ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-12f, ab.sqrMagnitude));
            var q = a + ab * t; float dd = (q - p).magnitude;
            if (dd < d) { d = dd; best = q; }
        }
        return best;
    }

    // =========================================================================== проекция на голову
    struct Tri { public Vector3 a, b, c, na, nb, nc; }

    static bool Raycast(List<Tri> tris, Vector3 o, Vector3 dir, out Vector3 hit, out Vector3 normal)
    {
        float best = float.MaxValue; hit = normal = Vector3.zero;
        foreach (var t in tris)
        {
            Vector3 e1 = t.b - t.a, e2 = t.c - t.a, pv = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, pv);
            if (Mathf.Abs(det) < 1e-10f) continue;
            float inv = 1f / det; Vector3 tv = o - t.a;
            float u = Vector3.Dot(tv, pv) * inv; if (u < 0 || u > 1) continue;
            Vector3 qv = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(dir, qv) * inv; if (v < 0 || u + v > 1) continue;
            float d = Vector3.Dot(e2, qv) * inv;
            if (d > 0 && d < best)
            {
                best = d; hit = o + dir * d;
                normal = (t.na * (1 - u - v) + t.nb * u + t.nc * v).normalized;
            }
        }
        return best < float.MaxValue;
    }

    static void BuildMeshes(SkinnedMeshRenderer hsmr, Transform headBone, out Mesh headLocal, out Mesh faceSpace)
    {
        var baked = new Mesh();
        hsmr.BakeMesh(baked, true);
        var wv = baked.vertices.Select(p => hsmr.transform.TransformPoint(p)).ToArray();
        var wn = baked.normals.Select(n => hsmr.transform.TransformDirection(n).normalized).ToArray();
        var tris = new List<Tri>();
        var ti = baked.triangles;
        for (int i = 0; i < ti.Length; i += 3)
            tris.Add(new Tri { a = wv[ti[i]], b = wv[ti[i + 1]], c = wv[ti[i + 2]], na = wn[ti[i]], nb = wn[ti[i + 1]], nc = wn[ti[i + 2]] });
        Object.DestroyImmediate(baked);

        var outline = Outline();
        var holes = new[] { Eye(EyeL, -1), Eye(EyeR, 1), Ellipse(Mouth, MouthA, MouthB) };
        bool Inside(Vector2 p) => InPoly(outline, p) && !holes.Any(h => InPoly(h, p));
        Vector2 Snap(Vector2 p, out float d)
        {
            var best = Nearest(outline, p, out d);
            foreach (var h in holes) { var q = Nearest(h, p, out float dh); if (dh < d) { d = dh; best = q; } }
            return best;
        }

        // ---- сетка
        float x0 = outline.Min(p => p.x) - Step, x1 = outline.Max(p => p.x) + Step;
        float y0 = outline.Min(p => p.y) - Step, y1 = outline.Max(p => p.y) + Step;
        int nx = Mathf.CeilToInt((x1 - x0) / Step) + 1, ny = Mathf.CeilToInt((y1 - y0) / Step) + 1;
        var p2 = new Vector2[nx, ny]; var valid = new bool[nx, ny];
        var pos = new Vector3[nx, ny]; var skin = new Vector3[nx, ny]; var nrm = new Vector3[nx, ny];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                var p = new Vector2(x0 + i * Step, y0 + j * Step);
                if (!Inside(p)) { var q = Snap(p, out float d); if (d > Step * 0.75f) continue; p = q; }
                if (!Raycast(tris, new Vector3(p.x, p.y, 0.6f), Vector3.back, out var hit, out var n)) continue;
                p2[i, j] = p; valid[i, j] = true; skin[i, j] = hit; nrm[i, j] = n; pos[i, j] = hit + n * Lift;
            }

        // ---- сглаживание ткани (низкополигональная голова не проступает гранями), без утопания в кожу
        for (int it = 0; it < 3; it++)
        {
            var next = (Vector3[,])pos.Clone();
            for (int i = 1; i < nx - 1; i++)
                for (int j = 1; j < ny - 1; j++)
                {
                    if (!valid[i, j]) continue;
                    Vector3 sum = Vector3.zero; int c = 0;
                    foreach (var (a, b) in new[] { (i - 1, j), (i + 1, j), (i, j - 1), (i, j + 1) })
                        if (valid[a, b]) { sum += pos[a, b]; c++; }
                    if (c < 4) continue;                                    // края не двигаем — они на контуре
                    var s = Vector3.Lerp(pos[i, j], sum / c, 0.5f);
                    // не ниже кожи + 3 мм
                    float above = Vector3.Dot(s - skin[i, j], nrm[i, j]);
                    if (above < 0.003f) s += nrm[i, j] * (0.003f - above);
                    next[i, j] = s;
                }
            pos = next;
        }

        // ---- треугольники
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tri = new List<int>();
        var index = new int[nx, ny];
        for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) index[i, j] = -1;
        int V(int i, int j)
        {
            if (index[i, j] < 0) { index[i, j] = verts.Count; verts.Add(pos[i, j]); uvs.Add((p2[i, j] - new Vector2(x0, y0)) * 6f); }
            return index[i, j];
        }
        for (int i = 0; i < nx - 1; i++)
            for (int j = 0; j < ny - 1; j++)
            {
                if (!(valid[i, j] && valid[i + 1, j] && valid[i, j + 1] && valid[i + 1, j + 1])) continue;
                var c = (p2[i, j] + p2[i + 1, j] + p2[i, j + 1] + p2[i + 1, j + 1]) / 4f;
                if (!Inside(c)) continue;
                int a = V(i, j), b = V(i + 1, j), d = V(i + 1, j + 1), e = V(i, j + 1);
                // лицевая сторона смотрит вперёд (+Z)
                tri.AddRange(new[] { a, b, d, a, d, e });
            }
        int fabricVerts = verts.Count, fabricTris = tri.Count;

        // ---- окантовка по краю и вокруг прорезей
        int rims = 0;
        foreach (var loop in new[] { outline }.Concat(holes))
        {
            var ring = Resample(loop, 0.004f);
            var pts = new List<Vector3>(); var ns = new List<Vector3>();
            foreach (var p in ring)
                if (Raycast(tris, new Vector3(p.x, p.y, 0.6f), Vector3.back, out var hit, out var n)) { pts.Add(hit + n * (Lift + 0.0015f)); ns.Add(n); }
            if (pts.Count < 8) { log.AppendLine("  WARN: rim loop skipped"); continue; }
            Tube(pts, ns, RimR, verts, uvs, tri);
            rims++;
        }

        var face = new Mesh { name = "Cos_FaceMask_Face", indexFormat = IndexFormat.UInt32 };
        face.SetVertices(verts); face.SetUVs(0, uvs);
        face.subMeshCount = 2;                                           // 0 — вязаная ткань, 1 — прошитая окантовка
        face.SetTriangles(tri.GetRange(0, fabricTris), 0);
        face.SetTriangles(tri.GetRange(fabricTris, tri.Count - fabricTris), 1);
        face.RecalculateNormals(); face.RecalculateBounds();
        log.AppendLine($"  mask: grid {nx}×{ny}, fabric verts {fabricVerts}, total verts {verts.Count}, tris {tri.Count / 3}, rims {rims}, bounds {face.bounds.min:F3}..{face.bounds.max:F3}");

        // в осях кости головы
        headLocal = Object.Instantiate(face);
        headLocal.name = "Cos_FaceMask";
        headLocal.SetVertices(verts.Select(v => headBone.InverseTransformPoint(v)).ToList());
        headLocal.RecalculateNormals(); headLocal.RecalculateBounds();
        // для витрины — центр лица в нуле
        var center = face.bounds.center;
        face.SetVertices(verts.Select(v => v - center).ToList());
        face.RecalculateBounds();
        faceSpace = face;
    }

    static List<Vector2> Resample(List<Vector2> loop, float step)
    {
        var r = new List<Vector2>();
        for (int i = 0; i < loop.Count; i++)
        {
            Vector2 a = loop[i], b = loop[(i + 1) % loop.Count];
            int n = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / step));
            for (int k = 0; k < n; k++) r.Add(Vector2.Lerp(a, b, k / (float)n));
        }
        return r;
    }

    /// Замкнутая трубка вдоль точек (6 граней) — шов по краю ткани.
    static void Tube(List<Vector3> pts, List<Vector3> ns, float radius, List<Vector3> verts, List<Vector2> uvs, List<int> tri)
    {
        const int Sides = 6;
        int n = pts.Count, b0 = verts.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 t = (pts[(i + 1) % n] - pts[(i + n - 1) % n]).normalized;
            Vector3 up = Vector3.ProjectOnPlane(ns[i], t).normalized;
            if (up.sqrMagnitude < 0.5f) up = Vector3.Cross(t, Vector3.right).normalized;
            Vector3 side = Vector3.Cross(t, up);
            for (int s = 0; s < Sides; s++)
            {
                float a = s * Mathf.PI * 2 / Sides;
                verts.Add(pts[i] + (up * Mathf.Cos(a) + side * Mathf.Sin(a)) * radius);
                uvs.Add(new Vector2(i * 0.1f, s / (float)Sides));
            }
        }
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            for (int s = 0; s < Sides; s++)
            {
                int s1 = (s + 1) % Sides;
                int a = b0 + i * Sides + s, b = b0 + j * Sides + s, c = b0 + j * Sides + s1, d = b0 + i * Sides + s1;
                tri.AddRange(new[] { a, c, b, a, d, c });   // наружу
            }
        }
    }

    static Mesh Save(Mesh mesh)
    {
        string path = $"{MeshDir}/{mesh.name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing)
        {
            existing.Clear();
            existing.indexFormat = mesh.indexFormat;
            existing.SetVertices(mesh.vertices); existing.SetNormals(mesh.normals); existing.SetUVs(0, mesh.uv);
            existing.subMeshCount = mesh.subMeshCount;
            for (int s = 0; s < mesh.subMeshCount; s++) existing.SetTriangles(mesh.GetTriangles(s), s);
            existing.RecalculateBounds(); existing.RecalculateTangents();
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }
        mesh.RecalculateTangents();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // =========================================================================== установка
    /// RobberMask под костью Head: старые части (повязка, узел) убираются, ставится новая маска.
    /// Чёрная (RobberMask) и белая (RobberMaskWhite) балаклавы под костью Head; белая создаётся рядом
    /// с чёрной (тот же слой) и добавляется в PlayerCosmetics.items.
    static void Apply(GorillaFinalRig rig, Mesh mesh, Material[][] mats, string who)
    {
        var head = rig.headBone;
        var cos = rig.GetComponent<PlayerCosmetics>();
        var black = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "RobberMask");
        if (!black || !head) { log.AppendLine($"  ERROR: {who}: RobberMask/head not found"); return; }
        int layer = black.gameObject.layer;
        for (int vi = 0; vi < Variants.Length; vi++)
        {
            var id = Variants[vi].id;
            var t = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == id);
            if (!t) { t = new GameObject(id).transform; t.gameObject.SetActive(false); }
            bool wasActive = t.gameObject.activeSelf;
            t.SetParent(head, false);
            t.gameObject.layer = layer;
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            foreach (Transform c in t.Cast<Transform>().ToArray()) Object.DestroyImmediate(c.gameObject);
            var go = new GameObject("FaceMask");
            go.transform.SetParent(t, false);
            go.layer = layer;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats[vi];
            r.shadowCastingMode = ShadowCastingMode.Off;
            t.gameObject.SetActive(wasActive);
            if (cos)
            {
                var items = (cos.items ?? new GameObject[0]).Where(x => x).ToList();
                if (!items.Contains(t.gameObject)) { items.Add(t.gameObject); cos.items = items.ToArray(); EditorUtility.SetDirty(cos); }
            }
        }
        // проверка посадки: корона и маска не пересекаются
        var crown = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Crown");
        string fit = "";
        if (crown)
        {
            float crownBottom = float.MaxValue;
            foreach (var mf in crown.GetComponentsInChildren<MeshFilter>(true))
                foreach (var v in mf.sharedMesh.vertices) crownBottom = Mathf.Min(crownBottom, mf.transform.TransformPoint(v).y);
            float maskTop = mesh.vertices.Max(v => head.TransformPoint(v).y);
            fit = $", gap crown↔mask {(crownBottom - maskTop) * 100f:F1} cm" + (crownBottom - maskTop < 0.005f ? " ERROR: overlap" : "");
        }
        log.AppendLine($"  {who}: mask installed (layer {LayerMask.LayerToName(layer)}){fit}");
    }

    /// Образцы на витрине косметики в хабе: Sample_1 — чёрная, Sample_3 — белая балаклава (уменьшенные).
    static void ShowcaseSample(Mesh face, Material[][] mats)
    {
        foreach (var (sampleName, vi) in new[] { ("Sample_1", 0), ("Sample_3", 1) })
        {
            var sample = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).FirstOrDefault(t => t.name == sampleName && t.parent && t.parent.name == "CosmeticShowcase");
            if (!sample) { log.AppendLine($"  showcase {sampleName} not found — skipped"); continue; }
            foreach (Transform c in sample.Cast<Transform>().ToArray()) Object.DestroyImmediate(c.gameObject);
            var go = new GameObject("FaceMaskSample");
            go.transform.SetParent(sample, false);
            go.transform.localPosition = new Vector3(0, 0.05f, 0);
            go.transform.localScale = Vector3.one * 0.6f;
            go.AddComponent<MeshFilter>().sharedMesh = face;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats[vi];
            log.AppendLine($"  showcase {sampleName}: {Variants[vi].id}");
        }
    }

    // =========================================================================== превью
    /// Снимки VR-гориллы с маской и короной (спереди, 3/4, сбоку) — проверка посадки.
    public static void CapturePreview()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VRPrefab);
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var rig = go.GetComponentInChildren<GorillaFinalRig>(true);
            foreach (var t in rig.GetComponentsInChildren<Transform>(true).Where(x => x.name == "Crown" || x.name == "RobberMask")) t.gameObject.SetActive(true);
            var lgo = new GameObject("L"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lgo, scene);
            var l = lgo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f;
            var l2go = new GameObject("L2"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(l2go, scene);
            var l2 = l2go.AddComponent<Light>(); l2.type = LightType.Directional; l2.intensity = 0.5f; l2go.transform.rotation = Quaternion.Euler(10, -150, 0);
            var cgo = new GameObject("C"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cgo, scene);
            var cam = cgo.AddComponent<Camera>(); cam.scene = scene; cam.fieldOfView = 30f; cam.nearClipPlane = 0.01f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            var rt = new RenderTexture(900, 900, 24); cam.targetTexture = rt;
            var tex = new Texture2D(900, 900, TextureFormat.RGB24, false);
            var c = new Vector3(Cx, 1.43f, -0.1f);
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "claude", "mask_preview");
            System.IO.Directory.CreateDirectory(dir);
            foreach (var (n, d) in new[] { ("front", new Vector3(0, 0.05f, 1)), ("threequarter", new Vector3(0.8f, 0.15f, 1)), ("side", new Vector3(1, 0.05f, 0.05f)), ("top", new Vector3(0.1f, 1.2f, 0.6f)) })
            {
                var dn = d.normalized;
                cgo.transform.position = c + dn * 1.25f; cgo.transform.rotation = Quaternion.LookRotation(-dn);
                lgo.transform.rotation = Quaternion.LookRotation(-dn + Vector3.down * 0.5f + Vector3.right * 0.3f);
                cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 900, 900), 0, 0); tex.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, n + ".png"), tex.EncodeToPNG());
            }
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            Debug.Log("[MaskPreview] " + dir);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
