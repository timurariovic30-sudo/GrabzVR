using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Визуальный апгрейд Главного Хаба в стиле Gorilla Tag / GILLA — ПОВЕРХ существующей сцены MainHub
/// (MainHubBuilder НЕ вызывается, сетевые объекты, GRABZ OS, SkinButton, VanPortal, зеркало и баскетбол не трогаются):
///  1) ретро-текстуры (HubRetroTextures) → отдельные материалы M_Hub_* (общие M_*.mat карты ограбления не меняются);
///     у примитивов архитектуры (кубы/цилиндры) UV пересчитываются в метры — текстура тайлится, а не растягивается;
///  2) Tripo-пропсы Hub_* из Assets/Art/GeneratedModels (если .glb уже скачан);
///  3) декор: деревянный балкон-тайник (2-й ярус) с перилами, трубы-ступени, ящики, поддоны, окна с решётками
///     и лучами света, вытяжки и короб под потолком, плинтус, ковры, разметка парковки фургона, фары фургона;
///  4) свет: видимые абажуры над зонами, тёплые споты над компьютером и диваном, приглушённый эмбиент,
///     лёгкий Post-Processing (Bloom + цветокоррекция) на камерах игрока.
/// Всё новое лежит в корне сцены «HubDecor» — повторный запуск пересоздаёт только его (идемпотентно).
/// Коллайдеры платформ/ящиков — BoxCollider на слое Default (за них цепляются руки GorillaLocomotion).
/// Меню: Tools/Heist/Hub/Visual Overhaul. Batchmode: -executeMethod HubVisualOverhaul.RunBatch
/// </summary>
public static class HubVisualOverhaul
{
    const string HubPath = "Assets/Scenes/MainHub.unity";
    const string MatDir = "Assets/Art/Materials/Hub";
    const string MeshDir = "Assets/Art/Meshes/HubUV";
    const string Gen = "Assets/Art/GeneratedModels";
    const string ProfilePath = "Assets/Settings/HubPostFXProfile.asset";
    const string DecorRoot = "HubDecor";

    static readonly Color Warm = new Color(1f, 0.74f, 0.45f);
    static Material brick, concrete, ceiling, wood, woodDark, rust, rustDark, steel, rugRed, rugBlue, rugGreen,
                    bulb, shaft, sky, paint, headlight, taillight, shade;
    static readonly StringBuilder log = new StringBuilder();

    [MenuItem("Tools/Heist/Hub/Visual Overhaul")]
    static void Menu() => Debug.Log(Run());

    public static void RunBatch()
    {
        string r;
        try { r = Run(); }
        catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); return; }
        Debug.Log(r);
        EditorApplication.Exit(r.Contains("ERROR") ? 1 : 0);
    }

    public static string Run()
    {
        log.Clear().AppendLine("[HubVisualOverhaul]");
        AssetDatabase.Refresh();
        ProcessTripoModels();
        BuildMaterials();

        var scene = EditorSceneManager.OpenScene(HubPath, OpenSceneMode.Single);
        foreach (var g in scene.GetRootGameObjects().Where(g => g.name == DecorRoot).ToArray()) Object.DestroyImmediate(g);
        var map = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "MainHubMap");
        if (!map) { log.AppendLine("ERROR: MainHubMap not found"); return log.ToString(); }

        Retexture(map.transform);
        var root = new GameObject(DecorRoot).transform;
        var st = Group(root, "Static");
        BuildBalcony(st);
        BuildClimbPipes(st);
        BuildPallets(st);
        BuildWindows(st);
        BuildVents(st);
        BuildTrim(st);
        BuildRugs(st);
        BuildVanDecor(st, map.transform);
        BuildTripoProps(st);
        BuildLighting(root, map.transform);
        BuildPostFX(root, scene);

        foreach (var t in st.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) log.AppendLine("ERROR: save failed");
        AssetDatabase.SaveAssets();
        log.AppendLine($"  decor objects: {root.GetComponentsInChildren<Transform>(true).Length}, colliders: {root.GetComponentsInChildren<Collider>(true).Length}, lights: {root.GetComponentsInChildren<Light>(true).Length}");
        return log.ToString();
    }

    // =========================================================================== превью
    /// Снимки Хаба с нескольких ракурсов (с постобработкой) в папку HubPreview рядом с проектом — для проверки без шлема.
    /// Batchmode (без -nographics): -executeMethod HubVisualOverhaul.CapturePreview
    [MenuItem("Tools/Heist/Hub/Capture Preview")]
    public static void CapturePreview()
    {
        EditorSceneManager.OpenScene(HubPath, OpenSceneMode.Single);
        string dir = System.IO.Path.GetFullPath("HubPreview");
        System.IO.Directory.CreateDirectory(dir);
        var shots = new (string name, Vector3 pos, Vector3 look)[]
        {
            ("01_spawn", new Vector3(1.5f, 1.7f, -5.5f), new Vector3(0, 1.4f, 4f)),
            ("02_sofa", new Vector3(-3.2f, 1.7f, 6.5f), new Vector3(-6.4f, 0.8f, 10.2f)),
            ("03_balcony", new Vector3(-4.5f, 2.2f, -3.5f), new Vector3(-9.5f, 2.6f, -8.5f)),
            ("04_van", new Vector3(1.5f, 1.8f, 3.5f), new Vector3(5f, 1.2f, 11f)),
            ("05_wide_up", new Vector3(6.5f, 4.6f, -8.8f), new Vector3(-6f, 2.5f, 10f)),
            ("06_wardrobe", new Vector3(-6.5f, 1.7f, -1.5f), new Vector3(-11.5f, 1.5f, 3f)),
        };
        var go = new GameObject("PreviewCam");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 80f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100f; cam.allowHDR = true;
        go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        foreach (var s in shots)
        {
            go.transform.SetPositionAndRotation(s.pos, Quaternion.LookRotation(s.look - s.pos));
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, s.name + ".png"), tex.EncodeToPNG());
        }
        RenderTexture.active = null;
        Object.DestroyImmediate(go); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        Debug.Log("[HubPreview] saved to " + dir);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    // =========================================================================== материалы
    static void BuildMaterials()
    {
        MapKit.EnsureFolder(MatDir);
        var (bB, bN) = HubRetroTextures.Brick();
        var (cB, cN) = HubRetroTextures.Concrete();
        var (wB, wN) = HubRetroTextures.Wood();
        var (mB, mN) = HubRetroTextures.RustMetal();
        var (rrB, rrN) = HubRetroTextures.Rug("RugRed", new Color(0.46f, 0.07f, 0.08f), new Color(0.08f, 0.10f, 0.26f), new Color(0.80f, 0.58f, 0.20f));
        var (rbB, rbN) = HubRetroTextures.Rug("RugBlue", new Color(0.10f, 0.16f, 0.36f), new Color(0.40f, 0.06f, 0.07f), new Color(0.82f, 0.62f, 0.25f));
        var (rgB, rgN) = HubRetroTextures.Rug("RugGreen", new Color(0.10f, 0.28f, 0.15f), new Color(0.30f, 0.15f, 0.07f), new Color(0.85f, 0.70f, 0.35f));

        brick = Lit("Brick", bB, bN, Color.white);
        concrete = Lit("ConcreteFloor", cB, cN, new Color(0.95f, 0.93f, 0.9f));
        ceiling = Lit("ConcreteCeiling", cB, cN, new Color(0.45f, 0.43f, 0.42f));
        wood = Lit("Wood", wB, wN, Color.white);
        woodDark = Lit("WoodDark", wB, wN, new Color(0.62f, 0.55f, 0.5f));
        rust = Lit("RustMetal", mB, mN, Color.white);
        rustDark = Lit("RustMetalDark", mB, mN, new Color(0.5f, 0.48f, 0.47f));
        steel = Lit("SteelWorn", mB, mN, new Color(0.85f, 0.9f, 0.95f));
        rugRed = Lit("RugRed", rrB, rrN, Color.white);
        rugBlue = Lit("RugBlue", rbB, rbN, Color.white);
        rugGreen = Lit("RugGreen", rgB, rgN, Color.white);
        shade = Lit("LampShade", mB, mN, new Color(0.35f, 0.6f, 0.4f));          // зелёная эмаль, как у старых ламп
        paint = Lit("FloorPaint", cB, cN, new Color(1.9f, 1.45f, 0.25f));          // жёлтая разметка, потёртая по бетону
        bulb = Emissive("Bulb", new Color(1f, 0.78f, 0.5f), 4f);
        headlight = Emissive("Headlight", new Color(1f, 0.95f, 0.8f), 5f);
        taillight = Emissive("Taillight", new Color(1f, 0.08f, 0.05f), 4f);
        sky = Unlit("WindowSky", new Color(0.85f, 0.95f, 1.15f));
        shaft = Additive("LightShaft", new Color(0.075f, 0.062f, 0.045f, 1f));   // чистое сложение: яркость задаёт сам цвет
        AssetDatabase.SaveAssets();
    }

    static Material GetMat(string name, string shader)
    {
        string path = $"{MatDir}/M_Hub_{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader.name != shader) m.shader = Shader.Find(shader);
        return m;
    }

    static Material Lit(string name, Texture2D baseMap, Texture2D normal, Color tint)
    {
        var m = GetMat(name, "Universal Render Pipeline/Lit");
        m.SetTexture("_BaseMap", baseMap);
        m.SetColor("_BaseColor", tint);
        if (normal) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", 0.6f); m.EnableKeyword("_NORMALMAP"); }
        TripoAssetPostprocessor.MakeMatte(m);                                    // Smoothness 0, без бликов и отражений
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Emissive(string name, Color c, float intensity)
    {
        var m = GetMat(name, "Universal Render Pipeline/Lit");
        m.SetColor("_BaseColor", c);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * intensity);                             // HDR > 1 → мягкий Bloom
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        TripoAssetPostprocessor.MakeMatte(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Unlit(string name, Color c)
    {
        var m = GetMat(name, "Universal Render Pipeline/Unlit");
        m.SetColor("_BaseColor", c);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// Аддитивный полупрозрачный материал для лучей света из окон (без записи глубины, двусторонний).
    static Material Additive(string name, Color c)
    {
        var m = GetMat(name, "Universal Render Pipeline/Unlit");
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 2f);
        m.SetFloat("_SrcBlend", (float)BlendMode.One);
        m.SetFloat("_DstBlend", (float)BlendMode.One);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", (float)CullMode.Off);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        return m;
    }

    // =========================================================================== перетекстурирование архитектуры
    static readonly string[] Untouchable = { "Mirror", "Hoop", "Skin", "Computer", "PC_", "Van", "Wardrobe", "Basket", "Ball", "Button", "Mode_", "Lamp_" };

    static void Retexture(Transform map)
    {
        var dyn = map.Find("Dynamic");
        var byMat = new Dictionary<string, (Material mat, float tile)>
        {
            { "M_Brick", (brick, 1.6f) }, { "M_Hub_Brick", (brick, 1.6f) },
            { "M_WoodLight", (wood, 1.2f) }, { "M_Hub_Wood", (wood, 1.2f) },
            { "M_WoodDark", (woodDark, 1.2f) }, { "M_Hub_WoodDark", (woodDark, 1.2f) },
            { "M_DarkMetal", (rustDark, 1.5f) }, { "M_Hub_RustMetalDark", (rustDark, 1.5f) },
            { "M_Metal", (steel, 1.5f) }, { "M_Hub_SteelWorn", (steel, 1.5f) },
            { "M_ConcreteDark", (concrete, 2.5f) }, { "M_Hub_ConcreteFloor", (concrete, 2.5f) }, { "M_Hub_ConcreteCeiling", (ceiling, 2.5f) },
            { "M_BarrelRed", (rust, 1.2f) }, { "M_Hub_RustMetal", (rust, 1.2f) },
        };
        var rugs = new Dictionary<string, Material> { { "M_CarpetRed", rugRed }, { "M_CarpetBlue", rugBlue }, { "M_Hub_RugRed", rugRed }, { "M_Hub_RugBlue", rugBlue } };

        int n = 0, skipped = 0;
        foreach (var r in map.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (dyn && r.transform.IsChildOf(dyn)) continue;
            if (PrefabUtility.IsPartOfPrefabInstance(r)) continue;               // Tripo-модели и префабы не трогаем
            if (Untouchable.Any(k => r.name.Contains(k))) { skipped++; continue; }
            var mf = r.GetComponent<MeshFilter>();
            var src = r.sharedMaterial;
            if (!mf || !mf.sharedMesh || !src) continue;
            string kind = MeshKind(mf.sharedMesh);
            if (kind == null) continue;

            if (rugs.TryGetValue(src.name, out var rugMat)) { r.sharedMaterial = rugMat; n++; continue; } // ковёр: узор целиком
            if (!byMat.TryGetValue(src.name, out var e)) continue;
            var mat = e.mat;
            if (r.name == "Ceiling") mat = ceiling;
            r.sharedMaterial = mat;
            mf.sharedMesh = kind == "box" ? BoxMesh(r.transform.lossyScale, e.tile) : CylMesh(r.transform.lossyScale, e.tile);
            n++;
        }
        log.AppendLine($"  retextured {n} architecture renderers (skipped {skipped} protected)");
    }

    static string MeshKind(Mesh m)
    {
        if (m.name == "Cube" || m.name.StartsWith("HubBox_")) return "box";
        if (m.name == "Cylinder" || m.name.StartsWith("HubCyl_")) return "cyl";
        return null;
    }

    static Mesh builtinCyl;

    /// Единичный куб, у которого UV в метрах (по размеру объекта / tile) — текстура тайлится без растяжения.
    internal static Mesh BoxMesh(Vector3 scale, float tile)
    {
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        string key = $"HubBox_{scale.x:0.00}_{scale.y:0.00}_{scale.z:0.00}_{tile:0.0}".Replace(',', '.');
        string path = $"{MeshDir}/{key}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing) return existing;
        MapKit.EnsureFolder(MeshDir);

        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        void Face(Vector3 n, Vector3 a, Vector3 b, float sa, float sb)
        {
            // a, b — оси грани; углы: n*0.5 ± a*0.5 ± b*0.5
            int i0 = v.Count;
            for (int k = 0; k < 4; k++)
            {
                float ua = (k == 1 || k == 2) ? 0.5f : -0.5f, ub = k >= 2 ? 0.5f : -0.5f;
                v.Add(n * 0.5f + a * ua + b * ub);
                uv.Add(new Vector2((ua + 0.5f) * sa / tile, (ub + 0.5f) * sb / tile));
            }
            tri.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
        }
        Face(Vector3.forward, Vector3.left, Vector3.up, scale.x, scale.y);
        Face(Vector3.back, Vector3.right, Vector3.up, scale.x, scale.y);
        Face(Vector3.right, Vector3.forward, Vector3.up, scale.z, scale.y);
        Face(Vector3.left, Vector3.back, Vector3.up, scale.z, scale.y);
        Face(Vector3.up, Vector3.right, Vector3.forward, scale.x, scale.z);
        Face(Vector3.down, Vector3.left, Vector3.forward, scale.x, scale.z);
        var m = new Mesh { name = key };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
        FixWinding(m);
        m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// Нормали граней должны смотреть наружу: разворачиваем треугольники, где это не так.
    static void FixWinding(Mesh m)
    {
        var v = m.vertices; var t = m.triangles;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
            var n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, (a + b + c) / 3f) < 0) { (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]); }
        }
        m.triangles = t;
    }

    static Mesh CylMesh(Vector3 scale, float tile)
    {
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        string key = $"HubCyl_{scale.x:0.00}_{scale.y:0.00}_{tile:0.0}".Replace(',', '.');
        string path = $"{MeshDir}/{key}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing) return existing;
        MapKit.EnsureFolder(MeshDir);
        if (!builtinCyl) builtinCyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        var m = Object.Instantiate(builtinCyl);
        m.name = key;
        float around = Mathf.PI * scale.x, along = 2f * scale.y;
        var uv = m.uv;
        for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(uv[i].x * around / tile, uv[i].y * along / tile);
        m.uv = uv;
        m.RecalculateTangents();
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // =========================================================================== примитивы
    static Transform Group(Transform parent, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    static GameObject Box(Transform p, string name, Vector3 pos, Vector3 size, Material mat, float tile, bool collider = true, Quaternion? rot = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p, false);
        go.transform.SetPositionAndRotation(pos, rot ?? Quaternion.identity);
        go.transform.localScale = size;
        go.AddComponent<MeshFilter>().sharedMesh = tile > 0 ? BoxMesh(size, tile) : Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        if (collider) go.AddComponent<BoxCollider>();
        return go;
    }

    static GameObject Cyl(Transform p, string name, Vector3 pos, float radius, float height, Material mat, float tile, bool collider = true, Quaternion? rot = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p, false);
        go.transform.SetPositionAndRotation(pos, rot ?? Quaternion.identity);
        var s = new Vector3(radius * 2, height / 2, radius * 2);
        go.transform.localScale = s;
        go.AddComponent<MeshFilter>().sharedMesh = tile > 0 ? CylMesh(s, tile) : Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        if (collider) { var c = go.AddComponent<CapsuleCollider>(); c.direction = 1; c.height = 2f; c.radius = 0.5f; }
        return go;
    }

    static GameObject Sphere(Transform p, string name, Vector3 pos, Vector3 size, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p, false);
        go.transform.position = pos;
        go.transform.localScale = size;
        go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    /// Балка/труба между двумя точками.
    static GameObject Bar(Transform p, string name, Vector3 a, Vector3 b, float radius, Material mat, bool collider = true)
    {
        var dir = b - a;
        return Cyl(p, name, (a + b) * 0.5f, radius, dir.magnitude, mat, 1.2f, collider, Quaternion.FromToRotation(Vector3.up, dir.normalized));
    }

    static TextMeshPro Label(Transform p, string name, string text, float size, Color color, Vector3 pos, Vector3 facing, Vector2 rect)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-facing, Vector3.up));
        var tmp = go.AddComponent<TextMeshPro>();                                 // 3D World Space, не Canvas
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MapKit.FontPath);
        if (font) tmp.font = font;
        tmp.text = text; tmp.fontSize = size; tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.rectTransform.sizeDelta = rect;
        return tmp;
    }

    // =========================================================================== 2-й ярус: балкон-тайник
    /// Деревянный балкон в юго-западном углу (над входом к GRABZ OS не нависает), перила с двух сторон,
    /// открытая сторона над стопкой ящиков — туда залезают руками.
    static void BuildBalcony(Transform st)
    {
        var g = Group(st, "Balcony");
        const float x0 = -11.8f, x1 = -6.8f, z0 = -9.8f, z1 = -7.3f, top = 3.3f;
        float cx = (x0 + x1) / 2, cz = (z0 + z1) / 2, w = x1 - x0, d = z1 - z0;
        Box(g, "Deck", new Vector3(cx, top - 0.1f, cz), new Vector3(w, 0.2f, d), wood, 1.2f);
        // балки под настилом и подкосы к стене
        for (float x = x0 + 0.6f; x < x1; x += 1.4f)
            Box(g, "Joist", new Vector3(x, top - 0.32f, cz), new Vector3(0.14f, 0.24f, d), woodDark, 1.2f);
        Box(g, "EdgeBeam", new Vector3(cx, top - 0.32f, z1 - 0.07f), new Vector3(w, 0.26f, 0.16f), woodDark, 1.2f);
        foreach (float x in new[] { x1 - 0.15f, -9.2f })
            Bar(g, "Brace", new Vector3(x, 1.7f, z0 + 0.05f), new Vector3(x, top - 0.4f, z1 - 0.3f), 0.06f, rustDark);
        // стойки до пола (по краям, не мешают ящикам)
        foreach (var p in new[] { new Vector3(x1 - 0.12f, 0, z1 - 0.12f), new Vector3(x0 + 0.15f, 0, z1 - 0.12f) })
            Box(g, "Post", p + Vector3.up * (top - 0.2f) / 2, new Vector3(0.18f, top - 0.2f, 0.18f), woodDark, 1.2f);
        // перила: вдоль переднего края (кроме проёма над ящиками x -11.2…-8.4) и с восточной стороны
        void Rail(Vector3 a, Vector3 b)
        {
            float len = (b - a).magnitude; int posts = Mathf.Max(2, Mathf.CeilToInt(len / 0.8f) + 1);
            for (int i = 0; i < posts; i++)
            {
                var p = Vector3.Lerp(a, b, i / (float)(posts - 1));
                Box(g, "RailPost", p + Vector3.up * (top + 0.5f), new Vector3(0.07f, 1f, 0.07f), woodDark, 1.2f);
            }
            var mid = (a + b) / 2; var dir = (b - a).normalized;
            var rot = Quaternion.LookRotation(dir);
            Box(g, "RailTop", mid + Vector3.up * (top + 1.0f), new Vector3(0.1f, 0.08f, len + 0.1f), wood, 1.2f, true, rot);
            Box(g, "RailMid", mid + Vector3.up * (top + 0.5f), new Vector3(0.05f, 0.05f, len), woodDark, 1.2f, true, rot);
        }
        Rail(new Vector3(-8.4f, 0, z1 - 0.05f), new Vector3(x1 - 0.05f, 0, z1 - 0.05f));
        Rail(new Vector3(x1 - 0.05f, 0, z1 - 0.05f), new Vector3(x1 - 0.05f, 0, z0 + 0.1f));
        // вывеска тайника (3D-текст)
        Label(g, "StashSign", "ТАЙНИК", 3.2f, new Color(1f, 0.62f, 0.15f), new Vector3(-9.3f, top + 1.6f, z0 + 0.22f), Vector3.forward, new Vector2(3f, 0.6f));
        Box(g, "StashSignBack", new Vector3(-9.3f, top + 1.6f, z0 + 0.24f), new Vector3(2.6f, 0.55f, 0.04f), rustDark, 1.5f, false);
        // ступени: ящик + (Tripo-стопка ящиков или примитивы) под открытым краем
        Box(g, "StepCrate", new Vector3(-7.9f, 0.45f, -6.7f), Vector3.one * 0.9f, wood, 1.2f, true, Quaternion.Euler(0, 12, 0));
        if (!Prefab("Hub_CrateStack"))
        {
            Box(g, "StepCrate2a", new Vector3(-9.7f, 0.6f, -6.75f), Vector3.one * 1.2f, wood, 1.2f);
            Box(g, "StepCrate2b", new Vector3(-9.7f, 1.8f, -6.75f), Vector3.one * 1.2f, wood, 1.2f);
        }
    }

    /// Трубы-ступени на южной стене рядом с балконом (вертикальный стояк + горизонтальные «перекладины»).
    static void BuildClimbPipes(Transform st)
    {
        var g = Group(st, "ClimbPipes");
        Bar(g, "Riser", new Vector3(-6.35f, 0f, -9.5f), new Vector3(-6.35f, 7.9f, -9.5f), 0.1f, rust);
        for (int i = 0; i < 4; i++)
        {
            float y = 0.8f + i * 0.75f;
            Bar(g, "Rung", new Vector3(-6.35f, y, -9.5f), new Vector3(-7.6f, y + 0.12f, -9.5f), 0.065f, steel);
            Cyl(g, "Clamp", new Vector3(-7.6f, y + 0.12f, -9.62f), 0.09f, 0.14f, rustDark, 0, false, Quaternion.Euler(90, 0, 0));
        }
        for (float y = 0.5f; y < 7.8f; y += 1.3f)
            Cyl(g, "RiserClamp", new Vector3(-6.35f, y, -9.5f), 0.13f, 0.08f, rustDark, 0, false);
    }

    static void Pallet(Transform p, string name, Vector3 pos, float yaw, int stack)
    {
        var g = Group(p, name);
        g.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        for (int s = 0; s < stack; s++)
        {
            float y0 = s * 0.15f;
            foreach (float x in new[] { -0.5f, 0f, 0.5f })
                Box(g, "Stringer", g.TransformPoint(new Vector3(x, y0 + 0.045f, 0)), new Vector3(0.1f, 0.09f, 1.2f), woodDark, 1.2f, false, g.rotation);
            for (int i = 0; i < 6; i++)
                Box(g, "Slat", g.TransformPoint(new Vector3(0, y0 + 0.115f, -0.55f + i * 0.22f)), new Vector3(1.2f, 0.05f, 0.14f), wood, 1.2f, false, g.rotation);
        }
        var col = g.gameObject.AddComponent<BoxCollider>();                       // один коллайдер на стопку
        col.center = new Vector3(0, stack * 0.075f, 0);
        col.size = new Vector3(1.2f, stack * 0.15f, 1.2f);
    }

    static void BuildPallets(Transform st)
    {
        var g = Group(st, "Pallets");
        Pallet(g, "PalletStack_Balcony", new Vector3(-10.9f, 0, -9.0f), 8f, 3);
        Pallet(g, "PalletStack_North", new Vector3(-3.6f, 0, 12.7f), -6f, 4);
        Pallet(g, "Pallet_Van", new Vector3(6.9f, 0, 7.6f), 20f, 2);
    }

    // =========================================================================== окна с решётками и лучами
    static void Window(Transform p, string name, Vector3 wallPoint, Vector3 inward, Vector3 floorTarget)
    {
        var g = Group(p, name);
        var rot = Quaternion.LookRotation(inward);
        g.SetPositionAndRotation(wallPoint, rot);
        Vector3 L(float x, float y, float z) => g.TransformPoint(new Vector3(x, y, z));
        const float w = 1.6f, h = 1.0f;
        Box(g, "Sky", L(0, 0, 0.03f), new Vector3(w, h, 0.02f), sky, 0, false, rot);
        Box(g, "FrameT", L(0, h / 2 + 0.06f, 0.08f), new Vector3(w + 0.24f, 0.12f, 0.16f), rustDark, 1.5f, true, rot);
        Box(g, "FrameB", L(0, -h / 2 - 0.06f, 0.1f), new Vector3(w + 0.24f, 0.12f, 0.2f), rustDark, 1.5f, true, rot);
        Box(g, "FrameL", L(-w / 2 - 0.06f, 0, 0.08f), new Vector3(0.12f, h, 0.16f), rustDark, 1.5f, true, rot);
        Box(g, "FrameR", L(w / 2 + 0.06f, 0, 0.08f), new Vector3(0.12f, h, 0.16f), rustDark, 1.5f, true, rot);
        for (int i = 1; i <= 5; i++)
            Cyl(g, "Bar", L(-w / 2 + i * w / 6, 0, 0.1f), 0.025f, h, steel, 0, false, rot);
        Box(g, "CrossBar", L(0, 0, 0.1f), new Vector3(w, 0.04f, 0.04f), steel, 0, false, rot);
        // луч света: вытянутый аддитивный объём от окна к полу
        var from = L(0, 0, 0.2f);
        var dir = floorTarget - from;
        var shaftGo = Box(g, "LightShaft", from + dir * 0.5f, new Vector3(w * 0.9f, h * 0.9f, dir.magnitude), shaft, 0, false,
            Quaternion.LookRotation(dir, Vector3.up));
        shaftGo.GetComponent<MeshRenderer>().sharedMaterial = shaft;
    }

    static void BuildWindows(Transform st)
    {
        var g = Group(st, "Windows");
        // окно левее стартовой таблички: его луч больше не засвечивает её текст (HubSpawnWindowFix)
        Window(g, "Window_S", HubSpawnWindowFix.WallPoint, Vector3.forward, HubSpawnWindowFix.FloorTarget);
        foreach (float x in new[] { 3f, 8f })
            Window(g, "Window_S", new Vector3(x, 6.7f, -9.78f), Vector3.forward, new Vector3(x + 0.8f, 0f, -5.2f));
        Window(g, "Window_W", new Vector3(-11.78f, 6.7f, 10f), Vector3.right, new Vector3(-7.4f, 0f, 10.6f));
        Window(g, "Window_E", new Vector3(11.78f, 6.7f, 10f), Vector3.left, new Vector3(7.5f, 3.5f, 9.4f));
    }

    // =========================================================================== вытяжки и вентиляция
    static void Fan(Transform p, string name, Vector3 wallPoint, Vector3 inward)
    {
        var g = Group(p, name);
        var rot = Quaternion.LookRotation(inward);
        g.SetPositionAndRotation(wallPoint, rot);
        Vector3 L(float x, float y, float z) => g.TransformPoint(new Vector3(x, y, z));
        Box(g, "Housing", L(0, 0, 0.12f), new Vector3(1.3f, 1.3f, 0.24f), rustDark, 1.5f, true, rot);
        Cyl(g, "Ring", L(0, 0, 0.26f), 0.55f, 0.06f, steel, 0, false, rot * Quaternion.Euler(90, 0, 0));
        Cyl(g, "Hub", L(0, 0, 0.3f), 0.12f, 0.08f, rust, 0, false, rot * Quaternion.Euler(90, 0, 0));
        for (int i = 0; i < 4; i++)
            Box(g, "Blade", L(0, 0, 0.29f), new Vector3(0.95f, 0.16f, 0.02f), steel, 0, false, rot * Quaternion.Euler(0, 0, i * 45 + 10) * Quaternion.Euler(18, 0, 0));
        for (int i = -2; i <= 2; i++)
            Box(g, "Grille", L(i * 0.2f, 0, 0.34f), new Vector3(0.02f, 1.1f, 0.02f), rustDark, 0, false, rot);
    }

    static void BuildVents(Transform st)
    {
        var g = Group(st, "Vents");
        Fan(g, "ExhaustFan_N1", new Vector3(8.2f, 5.6f, 13.78f), Vector3.back);
        Fan(g, "ExhaustFan_N2", new Vector3(-10.4f, 5.6f, 13.78f), Vector3.back);
        Fan(g, "ExhaustFan_W", new Vector3(-11.78f, 5.2f, -1.5f), Vector3.right);
        // короб вентиляции под потолком (над балками) с решётками-отводами
        Box(g, "Duct", new Vector3(0, 7.62f, 4.1f), new Vector3(22.8f, 0.5f, 0.7f), steel, 1.5f);
        foreach (float x in new[] { -8f, -2.5f, 2.5f, 8f })
        {
            Box(g, "DuctVent", new Vector3(x, 7.33f, 4.1f), new Vector3(0.6f, 0.1f, 0.6f), rustDark, 1.5f, false);
            for (int i = -2; i <= 2; i++) Box(g, "DuctSlat", new Vector3(x + i * 0.11f, 7.27f, 4.1f), new Vector3(0.03f, 0.03f, 0.55f), steel, 0, false);
        }
        foreach (float x in new[] { -10.5f, -3.5f, 3.5f, 10.5f })
            Box(g, "DuctHanger", new Vector3(x, 7.95f, 4.1f), new Vector3(0.05f, 0.2f, 0.8f), rustDark, 0, false);
        // вытяжной зонт над столом планирования
        Box(g, "HoodTop", new Vector3(0, 7.55f, 2.3f), new Vector3(1.2f, 0.7f, 1.2f), steel, 1.5f, false);
        Box(g, "HoodSkirt", new Vector3(0, 7.1f, 2.3f), new Vector3(1.8f, 0.25f, 1.8f), rustDark, 1.5f, false);
    }

    // =========================================================================== плинтус, ковры, разметка
    static void BuildTrim(Transform st)
    {
        var g = Group(st, "Baseboard");
        // деревянный плинтус-кромка поверх полосатой полосы
        Box(g, "Base_N", new Vector3(0, 0.33f, 13.75f), new Vector3(24f, 0.07f, 0.1f), woodDark, 1.2f, false);
        Box(g, "Base_S", new Vector3(0, 0.33f, -9.75f), new Vector3(24f, 0.07f, 0.1f), woodDark, 1.2f, false);
        Box(g, "Base_W", new Vector3(-11.75f, 0.33f, 2f), new Vector3(0.1f, 0.07f, 24f), woodDark, 1.2f, false);
        Box(g, "Base_E", new Vector3(11.75f, 0.33f, 2f), new Vector3(0.1f, 0.07f, 24f), woodDark, 1.2f, false);
        // карниз под потолком
        Box(g, "Cornice_N", new Vector3(0, 7.9f, 13.72f), new Vector3(24f, 0.2f, 0.16f), rustDark, 1.5f, false);
        Box(g, "Cornice_S", new Vector3(0, 7.9f, -9.72f), new Vector3(24f, 0.2f, 0.16f), rustDark, 1.5f, false);
        Box(g, "Cornice_W", new Vector3(-11.72f, 7.9f, 2f), new Vector3(0.16f, 0.2f, 24f), rustDark, 1.5f, false);
        Box(g, "Cornice_E", new Vector3(11.72f, 7.9f, 2f), new Vector3(0.16f, 0.2f, 24f), rustDark, 1.5f, false);
    }

    static void BuildRugs(Transform st)
    {
        var g = Group(st, "Rugs");
        Box(g, "Rug_Sofa", new Vector3(-6.4f, 0.014f, 8.7f), new Vector3(3.2f, 0.02f, 2.3f), rugGreen, 0, false);
        Box(g, "Rug_Mirror", new Vector3(-10.5f, 0.026f, 1.2f), new Vector3(1.4f, 0.02f, 1.9f), rugRed, 0, false);
    }

    static void BuildVanDecor(Transform st, Transform map)
    {
        var van = map.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Hub_Van") && PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject));
        // чёрный автобус (BlackBusBuilder) заменил джип: у него свои LED-фары, рамка шире на ступеньку у входа
        var bus = map.gameObject.scene.GetRootGameObjects().FirstOrDefault(r => r.name == BlackBusBuilder.RootName);
        var g = Group(st, "VanDecor");
        Bounds b;
        if (bus) b = BlackBusBuilder.BusFootprint(bus.transform);
        else if (van)
        {
            var rs = van.GetComponentsInChildren<Renderer>();
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        }
        else { b = new Bounds(new Vector3(4.2f, 1.1f, 10.6f), new Vector3(2.2f, 2.2f, 4f)); log.AppendLine("  van not found — using default bounds"); }

        // фары (спереди, к северной стене) и задние фонари (к игрокам) — светятся в Bloom
        float hy = b.min.y + b.size.y * 0.3f;
        foreach (float sx in bus ? new float[0] : new[] { -1f, 1f })
        {
            float x = b.center.x + sx * b.extents.x * 0.68f;
            Sphere(g, "Headlight", new Vector3(x, hy, b.max.z - 0.02f), new Vector3(0.28f, 0.2f, 0.06f), headlight);
            Sphere(g, "Taillight", new Vector3(x, hy + 0.1f, b.min.z + 0.02f), new Vector3(0.14f, 0.2f, 0.05f), taillight);
        }
        // разметка парковки: жёлтая рамка вокруг фургона + стрелка-полосы к нему
        float m = 0.45f, y = 0.008f;
        float x0 = b.min.x - m, x1 = b.max.x + m, z0 = b.min.z - m - (bus ? 0.3f : 0f), z1 = Mathf.Min(b.max.z + m, 13.6f);
        Box(g, "Park_L", new Vector3(x0, y, (z0 + z1) / 2), new Vector3(0.14f, 0.01f, z1 - z0), paint, 2.5f, false);
        Box(g, "Park_R", new Vector3(x1, y, (z0 + z1) / 2), new Vector3(0.14f, 0.01f, z1 - z0), paint, 2.5f, false);
        Box(g, "Park_F", new Vector3((x0 + x1) / 2, y, z0), new Vector3(x1 - x0, 0.01f, 0.14f), paint, 2.5f, false);
        for (int i = 0; i < 4; i++)
            Box(g, "Lane_Dash", new Vector3((x0 + x1) / 2, y, z0 - 1.2f - i * 1.3f), new Vector3(0.14f, 0.01f, 0.7f), paint, 2.5f, false);
    }

    // =========================================================================== Tripo-пропсы
    static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Gen}/{name}/{name}.prefab");

    static void ProcessTripoModels()
    {
        foreach (var n in new[] { "Hub_OldSofa", "Hub_CrateStack", "Hub_VentPipes", "Hub_Boombox", "Hub_TiresAndBarrels", "Hub_MoneyBags" })
        {
            string glb = $"{Gen}/{n}.glb";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(glb) == null) { log.AppendLine($"  tripo {n}: no .glb yet"); continue; }
            // postprocessor обычно делает это сам (delayCall), но в batchmode до него не доходит — вызываем явно
            var p = TripoAssetPostprocessor.Process(glb);
            log.AppendLine($"  tripo {n}: {p}");
        }
    }

    /// Ставит Tripo-префаб: масштаб по высоте, низ на floorPos.y, центр по XZ (или спиной к стене при wallZ/wallX).
    static GameObject Place(Transform p, string name, Vector3 floorPos, float yaw, float height, float? backToWallZ = null)
    {
        var prefab = Prefab(name);
        if (!prefab) { log.AppendLine($"  skip {name}: no prefab (generate in Tripo first)"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, p);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, yaw, 0));
        go.transform.localScale = Vector3.one;
        float h0 = WorldBounds(go).size.y;
        if (h0 > 0.0001f) go.transform.localScale = Vector3.one * (height / h0);
        var b = WorldBounds(go);
        float z = backToWallZ.HasValue ? backToWallZ.Value - b.min.z : floorPos.z - b.center.z;
        go.transform.position = new Vector3(floorPos.x - b.center.x, floorPos.y - b.min.y, z);
        // точные коллайдеры по мешу (руки GorillaLocomotion), слой Default
        foreach (var bc in go.GetComponentsInChildren<BoxCollider>()) Object.DestroyImmediate(bc);
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            if (!mf.TryGetComponent<MeshCollider>(out _)) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        log.AppendLine($"  placed {name} h={height} at {floorPos}");
        return go;
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.5f);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static void BuildTripoProps(Transform st)
    {
        var g = Group(st, "TripoProps");
        // зона отдыха: диван лицом в комнату, бумбокс на ящике-столике
        Place(g, "Hub_OldSofa", new Vector3(-6.4f, 0, 10.5f), 180f, 1.15f);
        Box(g, "SideCrate", new Vector3(-4.55f, 0.3f, 10.4f), new Vector3(0.7f, 0.6f, 0.6f), wood, 1.2f, true, Quaternion.Euler(0, 8, 0));
        Place(g, "Hub_Boombox", new Vector3(-4.55f, 0.6f, 10.4f), 200f, 0.42f);
        // стопка ящиков — ступенька на балкон
        Place(g, "Hub_CrateStack", new Vector3(-9.7f, 0, -6.75f), 12f, 2.4f);
        // трубы и щиток на южной стене
        Place(g, "Hub_VentPipes", new Vector3(5.2f, 0.4f, 0), 0f, 2.8f, backToWallZ: -9.78f);
        // покрышки и бочки у фургона
        Place(g, "Hub_TiresAndBarrels", new Vector3(7.3f, 0, 12.5f), 25f, 1.3f);
        // мешки с деньгами: в тайнике на балконе и в углу у баскетбола
        Place(g, "Hub_MoneyBags", new Vector3(-10.3f, 3.3f, -8.75f), 40f, 0.95f);
        Place(g, "Hub_MoneyBags", new Vector3(10.7f, 0, -9.0f), -35f, 0.85f);
    }

    // =========================================================================== свет
    /// Лампа с видимым абажуром: шнур от потолка, зелёный эмалевый абажур, светящаяся лампочка.
    static void Fixture(Transform p, string name, Vector3 bulbPos)
    {
        var g = Group(p, name);
        Cyl(g, "Cord", new Vector3(bulbPos.x, (bulbPos.y + 0.3f + 7.9f) / 2, bulbPos.z), 0.012f, 7.9f - bulbPos.y - 0.3f, rustDark, 0, false);
        Cyl(g, "ShadeTop", bulbPos + Vector3.up * 0.26f, 0.1f, 0.12f, shade, 0, false);
        Cyl(g, "Shade", bulbPos + Vector3.up * 0.13f, 0.32f, 0.16f, shade, 0, false);
        Cyl(g, "ShadeRim", bulbPos + Vector3.up * 0.05f, 0.36f, 0.03f, rustDark, 0, false);
        Sphere(g, "Bulb", bulbPos, Vector3.one * 0.16f, bulb);
    }

    static Light Spot(Transform p, string name, Vector3 pos, Vector3 target, float intensity, float range, float angle)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot; l.color = Warm; l.intensity = intensity; l.range = range;
        l.spotAngle = angle; l.innerSpotAngle = angle * 0.55f;
        l.shadows = LightShadows.None;
        return l;
    }

    static void BuildLighting(Transform root, Transform map)
    {
        var g = Group(root, "Lighting");
        // видимые абажуры у существующих источников (сами источники уже есть — новых не добавляем)
        foreach (var (light, offset) in new[] { ("TableSpot", 0.25f), ("WardrobeLight", 0.25f), ("VanLight", 0.25f), ("HoopLight", 0.25f) })
        {
            var l = map.GetComponentsInChildren<Light>(true).FirstOrDefault(x => x.name == light);
            if (l) Fixture(g, "Fixture_" + light, l.transform.position + Vector3.up * offset);
        }
        // новые тёплые конусы: GRABZ OS, диван, тайник (всего 3 — бюджет света Quest)
        Fixture(g, "Fixture_Computer", new Vector3(-5.5f, 3.3f, -8.1f));
        Spot(g, "ComputerSpot", new Vector3(-5.5f, 3.15f, -8.1f), new Vector3(-5.5f, 0.8f, -8.9f), 2.2f, 6f, 75f);
        Fixture(g, "Fixture_Sofa", new Vector3(-6.4f, 3.4f, 9.4f));
        Spot(g, "SofaSpot", new Vector3(-6.4f, 3.25f, 9.4f), new Vector3(-6.4f, 0f, 9.6f), 2.4f, 6.5f, 85f);
        var stash = new GameObject("StashLight").AddComponent<Light>();
        stash.transform.SetParent(g, false);
        stash.transform.position = new Vector3(-9.6f, 4.9f, -8.6f);
        stash.type = LightType.Point; stash.color = Warm; stash.intensity = 1.3f; stash.range = 4.5f; stash.shadows = LightShadows.None;

        // общий свет — глубже и приглушённее: верхние лампы слабее, эмбиент темнее, туман чуть плотнее
        foreach (var l in map.GetComponentsInChildren<Light>(true).Where(x => x.name == "WarmLamp")) { l.intensity = 2.4f; EditorUtility.SetDirty(l); }
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.30f, 0.25f, 0.21f);
        RenderSettings.ambientEquatorColor = new Color(0.22f, 0.17f, 0.13f);
        RenderSettings.ambientGroundColor = new Color(0.10f, 0.08f, 0.07f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.014f;
        RenderSettings.fogColor = new Color(0.13f, 0.10f, 0.08f);
    }

    // =========================================================================== Post-Processing
    static T Comp<T>(VolumeProfile p) where T : VolumeComponent
    {
        if (!p.TryGet<T>(out var c))
        {
            c = p.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, p);
        }
        return c;
    }

    static void BuildPostFX(Transform root, UnityEngine.SceneManagement.Scene scene)
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
        // мягкий Bloom (четверть разрешения — дёшево для Quest): светятся экран GRABZ OS, кнопки, лампочки, фары
        var bloom = Comp<Bloom>(profile);
        bloom.threshold.Override(0.95f);
        bloom.intensity.Override(0.35f);
        bloom.scatter.Override(0.5f);
        bloom.tint.Override(new Color(1f, 0.92f, 0.82f));
        bloom.highQualityFiltering.Override(false);
        bloom.downscale.Override(BloomDownscaleMode.Quarter);
        bloom.maxIterations.Override(5);
        // лёгкая тёплая цветокоррекция
        var ca = Comp<ColorAdjustments>(profile);
        ca.postExposure.Override(0.15f);
        ca.contrast.Override(12f);
        ca.saturation.Override(10f);
        ca.colorFilter.Override(new Color(1f, 0.96f, 0.9f));
        EditorUtility.SetDirty(profile);
        foreach (var c in profile.components) EditorUtility.SetDirty(c);

        var volGo = new GameObject("HubPostFX");
        volGo.transform.SetParent(root, false);
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true; vol.priority = 1; vol.sharedProfile = profile;

        // камеры игроков (VR и ПК) — включить постобработку (переопределение в экземпляре префаба только в MainHub)
        int cams = 0;
        foreach (var go in scene.GetRootGameObjects())
            foreach (var cam in go.GetComponentsInChildren<Camera>(true))
            {
                if (cam.targetTexture != null || cam.name.Contains("Mirror")) continue; // зеркало и прочие RT — без постобработки
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(data);
                if (PrefabUtility.IsPartOfPrefabInstance(data)) PrefabUtility.RecordPrefabInstancePropertyModifications(data);
                cams++;
            }
        log.AppendLine($"  post-processing: Bloom + ColorAdjustments, {cams} camera(s)");
    }
}
