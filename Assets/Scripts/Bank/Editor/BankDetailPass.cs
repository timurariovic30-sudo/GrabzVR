using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Детальный проход по Банку Майами (HeistLevel_Test): «жизнь», свет, паркур.
/// Идемпотентный: пересоздаёт только корень сцены <c>BankDetail</c> (и прячет старую люстру холла).
/// BankVisualOverhaul / HeistMapBuilder не вызывает, общие материалы M_*.mat и M_Bank_* не меняет —
/// всё новое лежит в Assets/Art/Materials/Bank/Detail (M_BankDetail_*) и Assets/Art/GeneratedModels/Bank/Detail.
///
/// Что добавляет (координаты — из плана level-designer, палитра — art-director, бюджеты — unity-specialist):
///  • улица: 11 пастельных машин (Tripo Bank_MiamiCar, 3 цвета), скамейки, светофоры, неоновые вывески отелей,
///    пальмы у портика, световые пятна под фонарями, розовый свет у входа;
///  • холл: стойки с бархатными канатами у касс, табло курсов валют, картины, кондиционеры, карниз,
///    золотой кант балконов, 5 латунных люстр (Tripo Bank_Chandelier) — «лианы» для паркура над холлом,
///    тёплые пятна света под люстрами и лампами;
///  • кассы: банковские лампы, счётчики купюр, телефоны, штампы, стаканы с ручками;
///  • служебное крыло: терраццо в коридоре, кабель-лоток (паркур над лестницей), молдинг, фото, огнетушители,
///    таблички «ТОЛЬКО ДЛЯ ПЕРСОНАЛА»; комната отдыха и кабинет директора обставлены;
///  • подвал и хранилище: бетон вместо «досок», тёмный потолок, трубы-турник, клетка с золотом (Tripo Bank_GoldPallet),
///    тележки, стеллажи с деньгами, стены банковских ячеек, золотой прожектор, янтарный свет у гаража.
/// Производительность (Quest): статический батчинг в проекте выключен, поэтому видимые примитивы каждой зоны
/// склеиваются в один меш на материал (Detail/Meshes); тени выключены; колайдеры — отдельные BoxCollider на слое Default;
/// новых источников света 4, без теней; свет «подделан» аддитивными пятнами и эмиссией под Bloom.
/// Меню: Tools/Heist/Bank/Detail Pass (props, light, parkour).
/// </summary>
public static class BankDetailPass
{
    const string ScenePath = "Assets/Scenes/HeistLevel_Test.unity";
    const string RootName = "BankDetail";
    const string MatDir = "Assets/Art/Materials/Bank/Detail";
    const string GenDir = "Assets/Art/GeneratedModels/Bank/Detail";
    const string MeshDir = GenDir + "/Meshes";
    const string BankGen = "Assets/Art/GeneratedModels/Bank";
    const float ServiceCeil = 3.4f, BasementY = -3f;

    static readonly StringBuilder log = new StringBuilder();
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static Mesh cube, cyl, sphere, quad;
    static int colliders, props;

    [MenuItem("Tools/Heist/Bank/Detail Pass (props, light, parkour)")]
    static void Menu() => Debug.Log(Run());

    public static string Run()
    {
        log.Clear().AppendLine("[BankDetailPass]");
        colliders = props = 0;
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (scene.isDirty) return "ERROR: active scene has unsaved changes — save it first";
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        foreach (var g in scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray()) Object.DestroyImmediate(g);
        MapKit.EnsureFolder(MatDir);
        if (AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.DeleteAsset(MeshDir);
        MapKit.EnsureFolder(MeshDir);
        cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        BuildMaterials();

        var root = new GameObject(RootName).transform;
        Street(Area(root, "Street"));
        Hall(Area(root, "Hall"));
        Tellers(Area(root, "Tellers"));
        Corridor(Area(root, "Corridor"));
        BreakRoom(Area(root, "BreakRoom"));
        Director(Area(root, "Director"));
        Basement(Area(root, "Basement"));
        Vault(Area(root, "Vault"));
        Lights(Group(root, "Lights"));
        HideOldChandelier();

        foreach (Transform area in root) if (area.Find("Visual")) Combine(area);
        CheckLaserZone(root);
        log.AppendLine($"  props {props}, colliders {colliders}, combined meshes {root.GetComponentsInChildren<MeshFilter>().Count(m => m.name.StartsWith("Combined"))}, " +
                       $"renderers total {root.GetComponentsInChildren<Renderer>().Length}, lights {root.GetComponentsInChildren<Light>().Length}");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // =========================================================================== материалы
    static void BuildMaterials()
    {
        mats.Clear();
        Lit("Brass", Hex("D9A43A"), 0.35f);
        Lit("Velvet", Hex("A3122A"), 0.1f);
        Lit("Steel", Hex("6E7780"), 0.3f);
        Lit("DarkSteel", Hex("2F3438"), 0.2f);
        Lit("WoodDark", Hex("3A2416"), 0f);
        Lit("BenchWood", Hex("7A4A2A"), 0f);
        Lit("FrameGold", Hex("B8892E"), 0.3f);
        Lit("Paper", Hex("F2EEE2"), 0f);
        Lit("Plastic", Hex("26292E"), 0.15f);
        Lit("PlasticBeige", Hex("D8CDB4"), 0.1f);
        Lit("Green", Hex("1F6B45"), 0.2f);
        Lit("Red", Hex("C8202A"), 0.2f);
        Lit("Mint", Hex("8FE3C8"), 0f);
        Lit("Pink", Hex("F49AC1"), 0f);
        Lit("Turquoise", Hex("1F9E93"), 0f);
        Lit("Cream", Hex("EFE3C8"), 0f);
        Lit("Rug", Hex("7C1F2B"), 0f);
        Lit("RugGold", Hex("C99A3C"), 0f);
        Lit("Leather", Hex("5A2A18"), 0.25f);
        Lit("Book1", Hex("7A1E22"), 0f); Lit("Book2", Hex("1E4A6B"), 0f); Lit("Book3", Hex("2F5E2C"), 0f); Lit("Book4", Hex("C49A3A"), 0f);
        Lit("Leaf", Hex("2E7D3A"), 0f);
        Lit("Pot", Hex("C96A3D"), 0f);
        Lit("Cash", Hex("6E9B5A"), 0f);
        Lit("Gold", Hex("FFC845"), 0.5f, Hex("FFE27A"), 0.35f);
        Lit("Crate", Hex("8A6A44"), 0f);
        Lit("Hazard", Hex("E8B21C"), 0f);
        Lit("HazardBlack", Hex("1C1C1C"), 0f);
        Lit("Globe", Hex("3C7FB5"), 0.2f);
        Lit("Concrete", Hex("8C8A84"), 0f, default, 0f, ConcreteTex());
        Lit("ConcreteDark", Hex("3A3A3C"), 0f, default, 0f, ConcreteTex());
        Lit("Terrazzo", Color.white, 0.15f, default, 0f, TerrazzoTex());
        Lit("Deposit", Color.white, 0.35f, default, 0f, DepositTex());
        Lit("Glass", Hex("BFE8E4"), 0.6f);
        // неон и лампы: Unlit, HDR-цвет выше порога Bloom (0.9)
        Unlit("NeonPink", Hex("FF3FA4") * 1.8f);
        Unlit("NeonCyan", Hex("22E5D4") * 1.6f);
        Unlit("NeonGold", Hex("FFD08A") * 1.5f);
        Unlit("LampWarm", Hex("FFD08A") * 1.4f);
        Unlit("LampGreen", Hex("2FBF71") * 1.2f);
        Unlit("ScreenTeal", Hex("2FD6C8") * 1.3f);
        Unlit("ExitGreen", Hex("3BFF7A") * 1.5f);
        Unlit("LedRed", Hex("FF3030") * 1.8f);
        Unlit("LedAmber", Hex("FFB020") * 1.6f);
        Unlit("VendingFront", Hex("9FE8FF") * 1.1f);
        Unlit("BoardDark", Hex("101418"));
        // аддитивные подделки света (ZWrite off, без теней)
        var pool = RadialTex();
        Additive("PoolWarm", Hex("FFB070"), 0.32f, pool);
        Additive("PoolGold", Hex("FFC845"), 0.45f, pool);
        Additive("PoolPink", Hex("FF6FB0"), 0.35f, pool);
        Additive("PoolAmber", Hex("FF9A3C"), 0.4f, pool);
        Additive("PoolCool", Hex("9FE8FF"), 0.25f, pool);
        // перекрашенные машины (оттенок текстуры Tripo сдвигается — розовая → мятная / лимонная)
        CarMaterials();
    }

    static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

    static Material NewMat(string name, string shader)
    {
        string path = $"{MatDir}/M_BankDetail_{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find(shader);
        mats[name] = m;
        return m;
    }

    static Material Lit(string name, Color c, float smooth, Color emission = default, float emit = 0f, Texture2D tex = null)
    {
        var m = NewMat(name, "Universal Render Pipeline/Lit");
        m.SetColor("_BaseColor", c);
        m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        if (emit > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission * emit); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        else m.DisableKeyword("_EMISSION");
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Unlit(string name, Color c)
    {
        var m = NewMat(name, "Universal Render Pipeline/Unlit");
        m.SetColor("_BaseColor", c);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Additive(string name, Color c, float alpha, Texture2D tex)
    {
        var m = NewMat(name, "Universal Render Pipeline/Unlit");
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", c * alpha);
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
        m.SetFloat("_SrcBlend", (float)BlendMode.One); m.SetFloat("_DstBlend", (float)BlendMode.One);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", (float)CullMode.Back);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetOverrideTag("RenderType", "Transparent");
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---- процедурные текстуры (сохраняются ассетами, Bilinear, небольшие — под Quest)
    static Texture2D SaveTex(string name, int size, System.Func<int, int, Color> fn, TextureWrapMode wrap = TextureWrapMode.Repeat)
    {
        string path = $"{MatDir}/T_BankDetail_{name}.asset";
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        bool create = !t || t.width != size;
        if (create) { if (t) AssetDatabase.DeleteAsset(path); t = new Texture2D(size, size, TextureFormat.RGBA32, true); }
        var px = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) px[y * size + x] = fn(x, y);
        t.SetPixels(px); t.wrapMode = wrap; t.filterMode = FilterMode.Bilinear; t.Apply(true);
        if (create) AssetDatabase.CreateAsset(t, path); else EditorUtility.SetDirty(t);
        return t;
    }

    static float Hash(int x, int y, int s) { unchecked { int h = x * 374761393 + y * 668265263 + s * 982451653; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xFFFF) / 65535f; } }

    static Texture2D RadialTex() => SaveTex("Pool", 64, (x, y) =>
    {
        float dx = (x + 0.5f) / 32f - 1f, dy = (y + 0.5f) / 32f - 1f;
        float v = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)); v = v * v * (3f - 2f * v);
        return new Color(v, v, v, v);
    }, TextureWrapMode.Clamp);

    static Texture2D ConcreteTex() => SaveTex("Concrete", 128, (x, y) =>
    {
        float n = 0.86f + 0.1f * Hash(x / 4, y / 4, 3) + 0.06f * Hash(x, y, 7);
        if ((x % 64 < 2) || (y % 64 < 2)) n *= 0.62f;                          // швы между плитами 2×2 м
        return new Color(n, n, n * 1.01f, 1);
    });

    static Texture2D TerrazzoTex() => SaveTex("Terrazzo", 128, (x, y) =>
    {
        bool check = ((x / 32) + (y / 32)) % 2 == 0;                            // шахматка крем / бирюза
        var c = check ? new Color(0.93f, 0.88f, 0.78f) : new Color(0.38f, 0.70f, 0.66f);
        float h = Hash(x, y, 11);
        if (h > 0.93f) c = Color.Lerp(c, new Color(0.95f, 0.62f, 0.66f), 0.7f);  // розовая крошка
        else if (h < 0.05f) c *= 0.8f;
        if (x % 32 == 0 || y % 32 == 0) c = new Color(0.78f, 0.62f, 0.3f);    // латунная жилка
        return c;
    });

    static Texture2D DepositTex() => SaveTex("Deposit", 128, (x, y) =>
    {
        int cx = x % 32, cy = y % 21;                                            // ячейки 4×6 на текстуру
        if (cx < 2 || cy < 2) return new Color(0.22f, 0.22f, 0.24f);           // щели
        var c = new Color(0.72f, 0.56f, 0.27f);                                  // латунные дверцы
        if (cx > 22 && cx < 28 && cy > 8 && cy < 13) c = new Color(0.25f, 0.2f, 0.12f); // замочная скважина / ручка
        c *= 0.9f + 0.1f * Hash(x / 32, y / 21, 5);
        return c;
    });

    static void CarMaterials()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{BankGen}/Bank_MiamiCar/Bank_MiamiCar.prefab");
        var src = prefab ? prefab.GetComponentInChildren<Renderer>().sharedMaterial : null;
        if (!src) { log.AppendLine("  WARN: Bank_MiamiCar prefab missing — cars skipped"); return; }
        mats["CarPink"] = src;
        var srcTex = src.GetTexture("_BaseMap") as Texture2D;
        string texPath = srcTex ? AssetDatabase.GetAssetPath(srcTex) : null;
        if (texPath == null) { mats["CarMint"] = mats["CarLemon"] = src; return; }
        var raw = new Texture2D(2, 2);
        raw.LoadImage(System.IO.File.ReadAllBytes(texPath));
        foreach (var (name, hue) in new[] { ("CarMint", 0.45f), ("CarLemon", 0.14f) })
        {
            int n = raw.width;
            var px = raw.GetPixels();
            var outPx = new Color[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                Color.RGBToHSV(px[i], out float h, out float s, out float v);
                bool pink = s > 0.18f && (h > 0.82f || h < 0.04f);                 // только розовый кузов, хром и шины не трогаем
                outPx[i] = pink ? Color.HSVToRGB(hue, Mathf.Min(1f, s * 1.05f), Mathf.Min(1f, v * 1.02f)) : px[i];
            }
            var tex = SaveTex(name, n, (x, y) => outPx[y * n + x]);
            var m = NewMat(name, src.shader.name);
            m.CopyPropertiesFromMaterial(src);
            m.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(m);
        }
    }

    // =========================================================================== постройка
    static Transform Group(Transform p, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(p, false);
        return t;
    }

    /// Зона: Visual (склеится), Colliders (BoxCollider, Default), Texts (TMP), Glow (аддитивные пятна), Tripo (модели).
    static Transform Area(Transform root, string name)
    {
        var a = Group(root, name);
        foreach (var n in new[] { "Visual", "Colliders", "Texts", "Glow", "Tripo" }) Group(a, n);
        return a;
    }

    static Transform V(Transform area) => area.Find("Visual");

    static GameObject Prim(Transform area, Mesh mesh, string name, Vector3 pos, Vector3 scale, string mat, Quaternion? rot = null, bool collider = false, Vector3? colSize = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(V(area), false);
        go.transform.SetPositionAndRotation(pos, rot ?? Quaternion.identity);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mats[mat];
        r.shadowCastingMode = ShadowCastingMode.Off;
        props++;
        if (collider)
        {
            var c = new GameObject(name + "_Col");
            c.transform.SetParent(area.Find("Colliders"), false);
            c.transform.SetPositionAndRotation(pos, rot ?? Quaternion.identity);
            c.transform.localScale = scale;
            var bc = c.AddComponent<BoxCollider>();
            bc.size = colSize ?? (mesh == cyl ? new Vector3(1f, 2f, 1f) : Vector3.one);
            c.layer = 0;                                                            // Default — за него цепляются руки
            GameObjectUtility.SetStaticEditorFlags(c, StaticEditorFlags.BatchingStatic);
            colliders++;
        }
        return go;
    }

    static GameObject Box(Transform a, string n, Vector3 pos, Vector3 size, string mat, bool col = false, Quaternion? rot = null) => Prim(a, cube, n, pos, size, mat, rot, col);
    /// Цилиндр по центру, радиусу и высоте (ось Y, или повёрнутая rot).
    static GameObject Cyl(Transform a, string n, Vector3 pos, float r, float h, string mat, bool col = false, Quaternion? rot = null) => Prim(a, cyl, n, pos, new Vector3(2 * r, h / 2, 2 * r), mat, rot, col);
    static GameObject Ball(Transform a, string n, Vector3 pos, Vector3 size, string mat) => Prim(a, sphere, n, pos, size, mat);

    /// Труба/стержень между двумя точками.
    static GameObject Rod(Transform a, string n, Vector3 from, Vector3 to, float r, string mat, bool col = false)
    {
        var d = to - from;
        return Cyl(a, n, (from + to) / 2, r, d.magnitude, mat, col, Quaternion.FromToRotation(Vector3.up, d.normalized));
    }

    /// Плоская плитка с UV в метрах (текстура повторяется каждые tile м, а не растягивается).
    /// facing — куда смотрит лицевая сторона, up — «верх» плитки на поверхности.
    static GameObject Tiled(Transform area, string n, Vector3 center, Vector3 facing, Vector3 up, float w, float h, string mat, float tile)
    {
        var right = Vector3.Cross(up, -facing).normalized;
        var m = new Mesh { name = n };
        Vector3 R = right * (w / 2), U = up * (h / 2);
        m.vertices = new[] { -R - U, R - U, R + U, -R + U };
        m.uv = new[] { new Vector2(0, 0), new Vector2(w / tile, 0), new Vector2(w / tile, h / tile), new Vector2(0, h / tile) };
        m.normals = new[] { facing, facing, facing, facing };
        m.triangles = new[] { 0, 3, 2, 0, 2, 1 };
        m.RecalculateBounds();
        return Prim(area, m, n, center, Vector3.one, mat);
    }

    /// Колайдер без видимой части (например, диск люстры для паркура).
    static void AddCollider(Transform area, string n, Vector3 pos, Vector3 size, Quaternion? rot = null)
    {
        var c = new GameObject(n + "_Col");
        c.transform.SetParent(area.Find("Colliders"), false);
        c.transform.SetPositionAndRotation(pos, rot ?? Quaternion.identity);
        c.AddComponent<BoxCollider>().size = size;
        c.layer = 0;
        colliders++;
    }

    static void Pool(Transform area, string n, Vector3 pos, float radius, string mat, Vector3? normal = null)
    {
        var go = new GameObject(n);
        go.transform.SetParent(area.Find("Glow"), false);
        var nrm = normal ?? Vector3.up;
        go.transform.SetPositionAndRotation(pos + nrm * 0.015f, Quaternion.LookRotation(-nrm, Mathf.Abs(nrm.y) > 0.9f ? Vector3.forward : Vector3.up));
        go.transform.localScale = new Vector3(radius * 2, radius * 2, 1);
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mats[mat];
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        props++;
    }

    static TextMeshPro Text(Transform area, string n, string text, Vector3 pos, Vector3 facing, float size, Vector2 rect, Color color, bool bold = true)
    {
        var go = new GameObject(n);
        go.transform.SetParent(area.Find("Texts"), false);
        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-facing, Vector3.up));
        var tmp = go.AddComponent<TextMeshPro>();                                   // 3D World Space, не Canvas
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MapKit.FontPath);
        if (font) tmp.font = font;
        tmp.text = text; tmp.color = color; tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.rectTransform.sizeDelta = rect;
        tmp.enableAutoSizing = true; tmp.fontSizeMax = size; tmp.fontSizeMin = size * 0.2f;
        if (!text.Contains("\n")) tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Truncate;
        var mr = go.GetComponent<MeshRenderer>(); mr.shadowCastingMode = ShadowCastingMode.Off;
        return tmp;
    }

    /// Табличка: тёмная плашка с латунной рамкой и текстом.
    static void Plate(Transform area, string n, string text, Vector3 pos, Vector3 facing, float w, float h, string textMat = null, Color? color = null)
    {
        var rot = Quaternion.LookRotation(-facing, Vector3.up);
        Box(area, n + "_Frame", pos - facing * 0.025f, new Vector3(w + 0.06f, h + 0.06f, 0.03f), "Brass", false, rot);
        Box(area, n + "_Plate", pos - facing * 0.005f, new Vector3(w, h, 0.02f), textMat ?? "BoardDark", false, rot);
        Text(area, n + "_Text", text, pos + facing * 0.01f, facing, h * 9f, new Vector2(w * 0.94f, h * 0.9f), color ?? Hex("FFD27A"));
    }

    static GameObject Tripo(Transform area, string model, string n, Vector3 pos, float yaw, float size, int axis, string matOverride = null, bool keepCollider = true)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{BankGen}/{model}/{model}.prefab");
        if (!prefab) { log.AppendLine($"  WARN: {model} prefab missing"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, area.Find("Tripo"));
        go.name = n;
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        var b = Bounds(go);
        float k = size / Mathf.Max(1e-4f, b.size[axis]);
        go.transform.localScale = Vector3.one * k;
        b = Bounds(go);
        go.transform.position = pos + new Vector3(-b.center.x, -b.min.y, -b.center.z);   // центр по XZ, дном на pos.y
        go.transform.RotateAround(pos, Vector3.up, yaw);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            if (matOverride != null && mats.TryGetValue(matOverride, out var m)) r.sharedMaterial = m;
        }
        foreach (var c in go.GetComponentsInChildren<Collider>()) { if (!keepCollider) Object.DestroyImmediate(c); else c.gameObject.layer = 0; }
        if (keepCollider) colliders += go.GetComponentsInChildren<Collider>().Length;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        props++;
        return go;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // =========================================================================== улица
    static void Street(Transform a)
    {
        // пастельные машины у обочин (не у зебры x −26…−18, не в полосе автобуса)
        string[] colors = { "CarPink", "CarMint", "CarLemon" };
        int ci = 0;
        if (mats.ContainsKey("CarPink"))
        {
            foreach (float x in new[] { -36f, -30f, -15f, -9f, 9f, 16f, 28f })
                Tripo(a, "Bank_MiamiCar", "ParkedCar", new Vector3(x, -1.05f, -11.5f), 90f, 4.4f, 2, colors[ci++ % 3]);
            foreach (float x in new[] { -40f, -33f, -12f, -6f })
                Tripo(a, "Bank_MiamiCar", "ParkedCar", new Vector3(x, -1.05f, -20.25f), 270f, 4.4f, 2, colors[ci++ % 3]);
        }
        // скамейки
        foreach (var (p, yaw) in new[]
        {
            (new Vector3(-29f, -0.9f, -1.0f), 0f), (new Vector3(-21f, -0.9f, -1.0f), 0f), (new Vector3(22f, -0.9f, -1.0f), 0f), (new Vector3(29f, -0.9f, -1.0f), 0f),
            (new Vector3(-10f, -0.9f, -23.5f), 180f), (new Vector3(4f, -0.9f, -23.5f), 180f), (new Vector3(27f, -0.9f, -23.5f), 180f),
        })
            Bench(a, p, yaw);
        // пальмы у портика
        foreach (float x in new[] { -12.2f, 12.2f }) Tripo(a, "Bank_PalmPlanter", "PorticoPalm", new Vector3(x, 0f, -0.9f), 0f, 2.6f, 1);
        // светофоры у зебры
        TrafficLight(a, new Vector3(-26.8f, -0.9f, -10.0f), 180f);
        TrafficLight(a, new Vector3(-18.2f, -0.9f, -21.6f), 0f);
        // неоновые вывески-«лопасти» на отелях (бренды — латиницей)
        NeonBlade(a, "HOTEL", new Vector3(-40f, 5.5f, -23.0f), "NeonPink", Hex("FF3FA4"));
        NeonBlade(a, "MOTEL", new Vector3(-17f, 4.5f, -23.0f), "NeonCyan", Hex("22E5D4"));
        NeonBlade(a, "OPEN 24", new Vector3(5f, 6.0f, -23.0f), "NeonPink", Hex("FF3FA4"));
        // тёплые пятна света под уличными фонарями (без настоящих источников)
        int lamps = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
            if (t.name.StartsWith("Bank_StreetLamp") && t.parent && !t.parent.name.StartsWith("Bank_StreetLamp"))
            { Pool(a, "LampPool", new Vector3(t.position.x, t.position.y, t.position.z), 2.2f, "PoolWarm"); lamps++; }
        Pool(a, "EntrancePool", new Vector3(0f, 0f, -2.2f), 3.2f, "PoolPink");
        log.AppendLine($"  street: cars {ci}, lamp pools {lamps}");
    }

    static void Bench(Transform a, Vector3 p, float yaw)
    {
        var q = Quaternion.Euler(0, yaw, 0);
        Vector3 W(float x, float y, float z) => p + q * new Vector3(x, y, z);
        for (int i = 0; i < 3; i++) Box(a, "BenchSlat", W(0, 0.45f, -0.12f + i * 0.12f), new Vector3(2.0f, 0.05f, 0.1f), "BenchWood", false, q);
        for (int i = 0; i < 2; i++) Box(a, "BenchBack", W(0, 0.62f + i * 0.14f, 0.22f), new Vector3(2.0f, 0.1f, 0.04f), "BenchWood", false, q * Quaternion.Euler(-10, 0, 0));
        foreach (float sx in new[] { -0.85f, 0.85f })
        {
            Box(a, "BenchLeg", W(sx, 0.21f, 0f), new Vector3(0.06f, 0.42f, 0.5f), "DarkSteel", false, q);
            Box(a, "BenchArm", W(sx, 0.6f, 0f), new Vector3(0.06f, 0.05f, 0.5f), "DarkSteel", false, q);
        }
        AddCollider(a, "Bench", W(0, 0.45f, 0.05f), new Vector3(2.0f, 0.9f, 0.6f), q);
    }

    static void TrafficLight(Transform a, Vector3 p, float yaw)
    {
        var q = Quaternion.Euler(0, yaw, 0);
        Cyl(a, "TL_Pole", p + Vector3.up * 1.75f, 0.06f, 3.5f, "DarkSteel", true);
        var head = p + Vector3.up * 3.2f + q * new Vector3(0, 0, -0.15f);
        Box(a, "TL_Head", head, new Vector3(0.32f, 0.9f, 0.25f), "HazardBlack", false, q);
        string[] l = { "LedRed", "LedAmber", "ExitGreen" };
        for (int i = 0; i < 3; i++) Ball(a, "TL_Lamp", head + Vector3.up * (0.27f - i * 0.27f) + q * new Vector3(0, 0, -0.13f), Vector3.one * 0.18f, i == 2 ? "ExitGreen" : l[i]);
    }

    static void NeonBlade(Transform a, string word, Vector3 c, string neon, Color col)
    {
        Box(a, "Blade_" + word, c, new Vector3(0.3f, 4f, 1.2f), "BoardDark");
        Box(a, "BladeArm", c + new Vector3(0, 1.6f, -0.75f), new Vector3(0.08f, 0.08f, 0.5f), "DarkSteel");
        // неоновая окантовка по краю
        foreach (float sx in new[] { -0.16f, 0.16f })
        {
            Box(a, "BladeTube", c + new Vector3(sx, 1.97f, 0), new Vector3(0.03f, 0.03f, 1.16f), neon);
            Box(a, "BladeTube", c + new Vector3(sx, -1.97f, 0), new Vector3(0.03f, 0.03f, 1.16f), neon);
            Box(a, "BladeTube", c + new Vector3(sx, 0, 0.57f), new Vector3(0.03f, 3.9f, 0.03f), neon);
            Box(a, "BladeTube", c + new Vector3(sx, 0, -0.57f), new Vector3(0.03f, 3.9f, 0.03f), neon);
        }
        string vertical = string.Join("\n", word.Replace(" ", "").ToCharArray());
        foreach (var f in new[] { Vector3.right, Vector3.left })
            Text(a, "BladeText_" + word, vertical, c + f * 0.16f, f, 30f, new Vector2(1.0f, 3.7f), col * 1.6f);
    }

    // =========================================================================== холл
    static void Hall(Transform a)
    {
        // очередь к кассам: латунные столбики с бархатным канатом, проход 1.2 м посередине
        foreach (float xc in new[] { -9.8f, -5.2f, 5.2f, 9.8f })
        {
            var posts = new[] { xc - 1.8f, xc - 0.6f, xc + 0.6f, xc + 1.8f };
            foreach (float x in posts) Stanchion(a, new Vector3(x, 0f, 15.5f));
            RopeSag(a, new Vector3(xc - 1.8f, 0.86f, 15.5f), new Vector3(xc - 0.6f, 0.86f, 15.5f));
            RopeSag(a, new Vector3(xc + 0.6f, 0.86f, 15.5f), new Vector3(xc + 1.8f, 0.86f, 15.5f));
        }
        // табло курсов валют (лицом ко входу, −Z)
        foreach (float x in new[] { -2.2f, 2.2f })
        {
            var p = new Vector3(x, 0f, 15.4f);
            Box(a, "RateStand_Base", p + new Vector3(0, 0.05f, 0), new Vector3(0.6f, 0.1f, 0.4f), "Brass");
            Box(a, "RateStand_Leg", p + new Vector3(0, 0.55f, 0), new Vector3(0.08f, 1.0f, 0.08f), "Brass");
            Box(a, "RateStand_Board", p + new Vector3(0, 1.3f, 0), new Vector3(0.75f, 0.7f, 0.06f), "BoardDark", true);
            Box(a, "RateStand_Frame", p + new Vector3(0, 1.3f, 0.01f), new Vector3(0.81f, 0.76f, 0.05f), "FrameGold");
            Text(a, "RateText", "КУРСЫ ВАЛЮТ\n<color=#7CFFB2>USD  1.00</color>\n<color=#7CFFB2>EUR  0.92</color>\n<color=#FFB070>GBP  0.79</color>\n<color=#FFD27A>GOLD 2 450</color>",
                 p + new Vector3(0, 1.3f, -0.04f), Vector3.back, 1.4f, new Vector2(0.68f, 0.62f), Hex("FFD27A"));
        }
        // картины на стенах (без колайдеров)
        string[] art = { "M_Art1", "M_Art2", "M_Art3" };
        int ai = 0;
        void Painting(Vector3 p, Vector3 facing, float w, float h)
        {
            var q = Quaternion.LookRotation(-facing, Vector3.up);
            Box(a, "PaintingFrame", p - facing * 0.03f, new Vector3(w + 0.16f, h + 0.16f, 0.05f), "FrameGold", false, q);
            var canvas = Box(a, "PaintingCanvas", p, new Vector3(w, h, 0.02f), "Cream", false, q);
            var m = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/Materials/Map/{art[ai++ % art.Length]}.mat");
            if (m) canvas.GetComponent<MeshRenderer>().sharedMaterial = m;
        }
        foreach (var z in new[] { 9.5f, 19.5f }) Painting(new Vector3(-13.8f, 2.4f, z), Vector3.right, 1.4f, 1.0f);
        foreach (var z in new[] { 6f, 19f }) Painting(new Vector3(-13.83f, 6.6f, z), Vector3.right, 1.6f, 1.1f);
        foreach (var z in new[] { 4.5f, 14.5f }) Painting(new Vector3(13.8f, 2.5f, z), Vector3.left, 1.4f, 1.0f);
        foreach (var z in new[] { 6f, 18f }) Painting(new Vector3(13.83f, 6.6f, z), Vector3.left, 1.6f, 1.1f);
        foreach (var x in new[] { -7f, 7f }) Painting(new Vector3(x, 6.0f, 23.82f), Vector3.back, 2.0f, 1.3f);
        // кондиционеры высоко на боковых стенах (за них можно схватиться с балкона)
        foreach (float sx in new[] { -1f, 1f })
            foreach (float z in new[] { 4f, 20f })
            {
                var p = new Vector3(sx * 13.75f, 8.3f, z);
                Box(a, "AC_Body", p, new Vector3(0.25f, 0.32f, 1.0f), "Cream", true);
                for (int i = 0; i < 4; i++) Box(a, "AC_Grille", p + new Vector3(-sx * 0.13f, -0.1f + i * 0.04f, 0), new Vector3(0.01f, 0.012f, 0.9f), "Plastic");
                Box(a, "AC_Led", p + new Vector3(-sx * 0.13f, 0.1f, 0.4f), new Vector3(0.01f, 0.02f, 0.02f), "ExitGreen");
            }
        // карниз по периметру (паркур под потолком) и золотой кант балконов
        Box(a, "Cornice_W", new Vector3(-13.85f, 9.75f, 12f), new Vector3(0.3f, 0.25f, 24f), "Cream", true);
        Box(a, "Cornice_E", new Vector3(13.85f, 9.75f, 12f), new Vector3(0.3f, 0.25f, 24f), "Cream", true);
        Box(a, "Cornice_N", new Vector3(0f, 9.75f, 23.85f), new Vector3(28f, 0.25f, 0.3f), "Cream", true);
        Box(a, "Cornice_S", new Vector3(0f, 9.75f, 0.15f), new Vector3(28f, 0.25f, 0.3f), "Cream", true);
        foreach (var (p, s) in new[] { (new Vector3(-13.68f, 9.6f, 12f), new Vector3(0.04f, 0.04f, 24f)), (new Vector3(13.68f, 9.6f, 12f), new Vector3(0.04f, 0.04f, 24f)),
                                       (new Vector3(0f, 9.6f, 23.68f), new Vector3(28f, 0.04f, 0.04f)), (new Vector3(0f, 9.6f, 0.32f), new Vector3(28f, 0.04f, 0.04f)) })
            Box(a, "CorniceGold", p, s, "Brass");
        Box(a, "BalconyGold_W", new Vector3(-10.95f, 4.2f, 10.5f), new Vector3(0.05f, 0.08f, 21f), "Brass");
        Box(a, "BalconyGold_E", new Vector3(10.95f, 4.2f, 10.5f), new Vector3(0.05f, 0.08f, 21f), "Brass");
        Box(a, "BalconyGold_N", new Vector3(0f, 4.2f, 20.95f), new Vector3(22f, 0.08f, 0.05f), "Brass");

        // маршрут R1: балкон → люстры ↔ кольца колонн → балкон (z = 12), латунные люстры Tripo
        foreach (var (x, y, r) in new[] { (-8.8f, 6.3f, 0.9f), (-3.4f, 6.3f, 0.9f), (0f, 6.5f, 1.4f), (3.4f, 6.3f, 0.9f), (8.8f, 6.3f, 0.9f) })
        {
            var p = new Vector3(x, y, 12f);
            float h = r * 2.1f;
            var ch = Tripo(a, "Bank_Chandelier", "Chandelier", p + Vector3.down * h * 0.45f, 0f, h, 1, null, false);
            float top = ch ? Bounds(ch).max.y : y + h * 0.55f;
            Rod(a, "ChandelierRod", new Vector3(x, top - 0.05f, 12f), new Vector3(x, 10f, 12f), 0.035f, "Brass");
            AddCollider(a, "ChandelierRing", p, new Vector3(r * 2f, 0.08f, r * 2f));            // диск-«ветка» для рук
            AddCollider(a, "ChandelierRod", new Vector3(x, (top + 10f) / 2, 12f), new Vector3(0.08f, 10f - top, 0.08f)); // «лиана»
            Pool(a, "ChandelierPool", new Vector3(x, 0.01f, 12f), r * 3.2f, "PoolWarm");
            Pool(a, "ChandelierCeilGlow", new Vector3(x, 9.98f, 12f), r * 2.2f, "PoolWarm", Vector3.down);
        }
    }

    static void Stanchion(Transform a, Vector3 p)
    {
        Cyl(a, "StanchionBase", p + Vector3.up * 0.02f, 0.15f, 0.04f, "Brass");
        Cyl(a, "StanchionPost", p + Vector3.up * 0.48f, 0.03f, 0.92f, "Brass", true);
        Ball(a, "StanchionTop", p + Vector3.up * 0.96f, Vector3.one * 0.08f, "Brass");
    }

    /// Бархатный канат с провисом: два отрезка (без колайдера — руки за тонкое не цепляются).
    static void RopeSag(Transform a, Vector3 p0, Vector3 p1)
    {
        var mid = (p0 + p1) / 2 + Vector3.down * 0.12f;
        Rod(a, "VelvetRope", p0, mid, 0.022f, "Velvet");
        Rod(a, "VelvetRope", mid, p1, 0.022f, "Velvet");
    }

    // =========================================================================== кассы
    static void Tellers(Transform a)
    {
        void Lamp(float x)
        {
            var p = new Vector3(x, 0.95f, 20.55f);
            Box(a, "BankerLamp_Base", p + Vector3.up * 0.015f, new Vector3(0.18f, 0.03f, 0.12f), "Brass");
            Box(a, "BankerLamp_Stem", p + Vector3.up * 0.15f, new Vector3(0.02f, 0.26f, 0.02f), "Brass");
            Cyl(a, "BankerLamp_Shade", p + new Vector3(0, 0.3f, -0.03f), 0.09f, 0.32f, "Green", false, Quaternion.Euler(0, 0, 90));
            Box(a, "BankerLamp_Glow", p + new Vector3(0, 0.255f, -0.03f), new Vector3(0.28f, 0.01f, 0.1f), "LampWarm");
            Pool(a, "BankerLampPool", p + new Vector3(0, 0.005f, -0.08f), 0.45f, "PoolWarm");
        }
        void Phone(float x, float y, float z)
        {
            var p = new Vector3(x, y, z);
            Box(a, "Phone_Body", p + Vector3.up * 0.04f, new Vector3(0.2f, 0.08f, 0.16f), "Plastic");
            Cyl(a, "Phone_Handset", p + new Vector3(0, 0.1f, 0), 0.025f, 0.22f, "Plastic", false, Quaternion.Euler(0, 0, 90));
        }
        void Counter(float x, float y, float z)
        {
            var p = new Vector3(x, y, z);
            Box(a, "BillCounter_Body", p + Vector3.up * 0.07f, new Vector3(0.26f, 0.14f, 0.24f), "PlasticBeige");
            Box(a, "BillCounter_Screen", p + new Vector3(0, 0.11f, -0.122f), new Vector3(0.12f, 0.04f, 0.005f), "ScreenTeal");
            Box(a, "BillCounter_Cash", p + new Vector3(0, 0.155f, 0.03f), new Vector3(0.16f, 0.03f, 0.07f), "Cash");
        }
        foreach (float x in new[] { -11.65f, -4.75f, 3.45f, 9.85f }) Lamp(x);
        foreach (float x in new[] { -10.45f, -3.45f, 11.15f }) Phone(x, 0.95f, 20.55f);
        foreach (float x in new[] { -6.05f, 2.15f, 8.55f }) Counter(x, 0.95f, 20.55f);
        Box(a, "Stamp", new Vector3(-2.15f, 1.0f, 20.55f), new Vector3(0.06f, 0.1f, 0.06f), "WoodDark");
        foreach (float xc in new[] { -9.8f, -5.2f, 5.2f, 9.8f })
        {
            Counter(xc, 1.06f, 17.45f);
            foreach (float dx in new[] { -0.73f, 0.73f })
            {
                Cyl(a, "PenCup", new Vector3(xc + dx, 1.11f, 16.88f), 0.035f, 0.1f, "Brass");
                Rod(a, "Pen", new Vector3(xc + dx, 1.1f, 16.88f), new Vector3(xc + dx + 0.02f, 1.22f, 16.9f), 0.006f, "Plastic");
            }
            // номер окна — латунная табличка над стеклом
            Plate(a, "WindowNo", (System.Array.IndexOf(new[] { -9.8f, -5.2f, 5.2f, 9.8f }, xc) + 1).ToString(), new Vector3(xc, 2.35f, 16.95f), Vector3.back, 0.36f, 0.3f);
        }
    }

    // =========================================================================== коридор служебного крыла
    static void Corridor(Transform a)
    {
        // терраццо (крем/бирюза с латунной жилкой) поверх бетона — вокруг лестничного проёма x −3.5…2.5
        foreach (var (x0, x1) in new[] { (-14f, -3.5f), (2.5f, 10f) })
            Tiled(a, "Terrazzo", new Vector3((x0 + x1) / 2, 0.004f, 26.035f), Vector3.up, Vector3.forward, x1 - x0, 3.77f, "Terrazzo", 1.6f);
        // кабель-лоток R3 (паркур над лестницей) + вертикальные гофры на концах
        Box(a, "CableTray", new Vector3(-2f, 2.75f, 25.4f), new Vector3(23f, 0.08f, 0.4f), "Steel", true);
        foreach (float sx in new[] { -0.19f, 0.19f }) Box(a, "CableTrayLip", new Vector3(-2f, 2.82f, 25.4f + sx), new Vector3(23f, 0.06f, 0.02f), "Steel");
        for (int i = 0; i < 6; i++) Box(a, "Cable", new Vector3(-2f, 2.8f, 25.27f + i * 0.05f), new Vector3(22.8f, 0.03f, 0.03f), i % 2 == 0 ? "Red" : "Plastic");
        for (float x = -13f; x <= 9.5f; x += 2f) Rod(a, "TrayHanger", new Vector3(x, 2.79f, 25.4f), new Vector3(x, ServiceCeil, 25.4f), 0.01f, "Steel");
        Rod(a, "Conduit", new Vector3(-13.8f, 0.05f, 25.4f), new Vector3(-13.8f, 2.8f, 25.4f), 0.05f, "Steel", true);
        Rod(a, "Conduit", new Vector3(9.8f, 0.05f, 25.4f), new Vector3(9.8f, 2.8f, 25.4f), 0.05f, "Steel", true);
        // молдинг на стенах (без проёмов дверей)
        foreach (var (x0, x1) in new[] { (-13.9f, -9.6f), (-7.8f, 5.0f), (6.8f, 9.9f) })
            Box(a, "ChairRail", new Vector3((x0 + x1) / 2, 0.95f, 24.19f), new Vector3(x1 - x0, 0.06f, 0.04f), "WoodDark");
        foreach (var (x0, x1) in new[] { (-13.9f, -11.6f), (-9.8f, -5.8f), (-4.2f, 3.9f), (5.7f, 9.9f) })
            Box(a, "ChairRail", new Vector3((x0 + x1) / 2, 0.95f, 27.9f), new Vector3(x1 - x0, 0.06f, 0.04f), "WoodDark");
        // фото сотрудников в рамках
        foreach (float x in new[] { -8f, 0f, 7.5f })
        {
            Box(a, "PhotoFrame", new Vector3(x, 1.7f, 27.89f), new Vector3(0.5f, 0.38f, 0.03f), "WoodDark");
            Box(a, "Photo", new Vector3(x, 1.7f, 27.87f), new Vector3(0.42f, 0.3f, 0.01f), x > 0 ? "Pink" : "Mint");
        }
        foreach (float x in new[] { -12f, -5.5f, 3.5f, 8.5f })
        {
            Box(a, "PhotoFrame", new Vector3(x, 1.7f, 24.2f), new Vector3(0.5f, 0.38f, 0.03f), "WoodDark");
            Box(a, "Photo", new Vector3(x, 1.7f, 24.22f), new Vector3(0.42f, 0.3f, 0.01f), x > 0 ? "Mint" : "Pink");
        }
        // огнетушители
        foreach (var x in new[] { -13.75f, 9.75f })
        {
            Cyl(a, "Extinguisher", new Vector3(x, 0.75f, 26.6f), 0.08f, 0.5f, "Red");
            Box(a, "ExtinguisherHead", new Vector3(x, 1.04f, 26.6f), new Vector3(0.08f, 0.08f, 0.12f), "Plastic");
            Box(a, "ExtinguisherBracket", new Vector3(x, 0.75f, 26.6f), new Vector3(0.04f, 0.3f, 0.2f), "DarkSteel");
        }
        // таблички над служебными дверями со стороны холла
        Plate(a, "Sign_StaffA", "ТОЛЬКО ДЛЯ ПЕРСОНАЛА", new Vector3(-8.7f, 2.82f, 23.8f), Vector3.back, 1.5f, 0.24f);
        Plate(a, "Sign_StaffB", "ТОЛЬКО ДЛЯ ПЕРСОНАЛА", new Vector3(5.9f, 2.82f, 23.8f), Vector3.back, 1.5f, 0.24f);
    }

    // =========================================================================== комната отдыха
    static void BreakRoom(Transform a)
    {
        Box(a, "Rug", new Vector3(-10f, 0.02f, 33.5f), new Vector3(3f, 0.01f, 2.2f), "Rug");
        Box(a, "RugBorder", new Vector3(-10f, 0.018f, 33.5f), new Vector3(3.2f, 0.01f, 2.4f), "RugGold");
        foreach (var (p, yaw) in new[] { (new Vector3(-10f, 0f, 32.6f), 0f), (new Vector3(-9.1f, 0f, 34f), -120f), (new Vector3(-10.9f, 0f, 34f), 120f) })
            Chair(a, p, yaw, "Mint");
        foreach (var p in new[] { new Vector3(-9.7f, 0.79f, 33.25f), new Vector3(-10.35f, 0.79f, 33.75f) })
        {
            Cyl(a, "CoffeeCup", p + Vector3.up * 0.05f, 0.04f, 0.1f, "Cream");
            Cyl(a, "Coffee", p + Vector3.up * 0.099f, 0.034f, 0.004f, "WoodDark");
        }
        // кофемашина на кухне
        var cm = new Vector3(-11.0f, 0.94f, 39.6f);
        Box(a, "CoffeeMachine", cm + Vector3.up * 0.2f, new Vector3(0.32f, 0.4f, 0.3f), "Plastic");
        Box(a, "CoffeeMachine_Led", cm + new Vector3(0.08f, 0.33f, -0.152f), new Vector3(0.05f, 0.02f, 0.005f), "LedRed");
        Cyl(a, "CoffeeMachine_Pot", cm + new Vector3(0, 0.08f, -0.06f), 0.06f, 0.14f, "Glass");
        // журнальный столик, вендинговый автомат, ещё шкафчики
        Box(a, "CoffeeTable", new Vector3(-12.2f, 0.25f, 34f), new Vector3(1.0f, 0.5f, 0.4f), "WoodDark", true);
        var vm = new Vector3(-13.55f, 0f, 31f);
        Box(a, "Vending_Body", vm + Vector3.up * 0.95f, new Vector3(0.8f, 1.9f, 0.9f), "Red", true);
        Box(a, "Vending_Front", vm + new Vector3(0.405f, 1.15f, -0.1f), new Vector3(0.01f, 1.2f, 0.55f), "VendingFront");
        for (int r = 0; r < 4; r++) for (int c = 0; c < 3; c++)
            Box(a, "Vending_Snack", vm + new Vector3(0.39f, 0.7f + r * 0.28f, -0.3f + c * 0.18f), new Vector3(0.02f, 0.12f, 0.1f), (r + c) % 2 == 0 ? "Pink" : "Hazard");
        Box(a, "Vending_Slot", vm + new Vector3(0.405f, 0.3f, 0f), new Vector3(0.01f, 0.18f, 0.6f), "HazardBlack");
        Pool(a, "VendingGlow", vm + new Vector3(1.0f, 0.01f, -0.1f), 1.1f, "PoolCool");
        for (int i = 0; i < 3; i++) Locker(a, new Vector3(-6.35f, 0f, 36.0f + i * 0.55f));
        foreach (var p in new[] { new Vector3(-13.4f, 0f, 39.4f), new Vector3(-6.6f, 0f, 39.4f) }) Plant(a, p, 1.3f);
        // доска объявлений и часы
        Box(a, "NoticeBoard", new Vector3(-7.5f, 1.6f, 28.1f), new Vector3(1.2f, 0.8f, 0.03f), "Crate");
        foreach (var (dx, dy, m) in new[] { (-0.35f, 0.15f, "Paper"), (0.05f, 0.2f, "Pink"), (0.35f, -0.05f, "Paper"), (-0.15f, -0.2f, "Mint") })
            Box(a, "Note", new Vector3(-7.5f + dx, 1.6f + dy, 28.12f), new Vector3(0.22f, 0.25f, 0.005f), m);
        Clock(a, new Vector3(-13.84f, 2.4f, 36f), Vector3.right);
    }

    static void Chair(Transform a, Vector3 p, float yaw, string seat)
    {
        var q = Quaternion.Euler(0, yaw, 0);
        Vector3 W(float x, float y, float z) => p + q * new Vector3(x, y, z);
        Box(a, "ChairSeat", W(0, 0.45f, 0), new Vector3(0.45f, 0.06f, 0.45f), seat, false, q);
        Box(a, "ChairBack", W(0, 0.75f, -0.21f), new Vector3(0.45f, 0.55f, 0.05f), seat, false, q);
        foreach (var (sx, sz) in new[] { (-0.19f, -0.19f), (0.19f, -0.19f), (-0.19f, 0.19f), (0.19f, 0.19f) })
            Box(a, "ChairLeg", W(sx, 0.21f, sz), new Vector3(0.03f, 0.42f, 0.03f), "DarkSteel", false, q);
        AddCollider(a, "Chair", W(0, 0.45f, 0), new Vector3(0.45f, 0.9f, 0.45f), q);
    }

    static void Locker(Transform a, Vector3 p)
    {
        Box(a, "Locker", p + Vector3.up * 0.95f, new Vector3(0.5f, 1.9f, 0.5f), "Turquoise", true);
        for (int i = 0; i < 3; i++) Box(a, "LockerVent", p + new Vector3(0.255f, 1.6f + i * 0.05f, 0), new Vector3(0.01f, 0.015f, 0.3f), "DarkSteel");
        Box(a, "LockerHandle", p + new Vector3(0.26f, 1.0f, 0.17f), new Vector3(0.02f, 0.12f, 0.02f), "Brass");
    }

    static void Plant(Transform a, Vector3 p, float h)
    {
        Cyl(a, "PlantPot", p + Vector3.up * 0.2f, 0.22f, 0.4f, "Pot", true);
        Ball(a, "PlantLeaves", p + Vector3.up * (0.4f + h * 0.35f), new Vector3(0.7f, h * 0.7f, 0.7f), "Leaf");
        Ball(a, "PlantLeaves", p + new Vector3(0.12f, 0.4f + h * 0.62f, -0.05f), new Vector3(0.45f, h * 0.45f, 0.45f), "Leaf");
    }

    static void Clock(Transform a, Vector3 p, Vector3 facing)
    {
        var q = Quaternion.LookRotation(facing) * Quaternion.Euler(90, 0, 0);
        Cyl(a, "ClockRim", p, 0.2f, 0.05f, "Brass", false, q);
        Cyl(a, "ClockFace", p + facing * 0.026f, 0.18f, 0.005f, "Paper", false, q);
        Box(a, "ClockHand", p + facing * 0.032f + Vector3.up * 0.06f, new Vector3(0.012f, 0.12f, 0.004f), "Plastic", false, Quaternion.LookRotation(-facing));
        Box(a, "ClockHand", p + facing * 0.034f + Quaternion.LookRotation(-facing) * new Vector3(0.04f, 0f, 0f), new Vector3(0.09f, 0.012f, 0.004f), "Plastic", false, Quaternion.LookRotation(-facing));
    }

    // =========================================================================== кабинет директора
    static void Director(Transform a)
    {
        Box(a, "Rug", new Vector3(5.5f, 0.02f, 33.2f), new Vector3(3.2f, 0.01f, 2.4f), "Rug");
        Box(a, "RugBorder", new Vector3(5.5f, 0.018f, 33.2f), new Vector3(3.4f, 0.01f, 2.6f), "RugGold");
        foreach (var (p, yaw) in new[] { (new Vector3(2.4f, 0f, 39.6f), 0f), (new Vector3(8.6f, 0f, 39.6f), 0f), (new Vector3(9.6f, 0f, 36.5f), 90f) })
            Bookshelf(a, p, yaw);
        // кожаный диван и журнальный столик
        var sp = new Vector3(1.5f, 0f, 35.5f);
        Box(a, "Sofa_Seat", sp + new Vector3(0.05f, 0.25f, 0), new Vector3(0.8f, 0.5f, 2.0f), "Leather", true);
        Box(a, "Sofa_Back", sp + new Vector3(-0.28f, 0.7f, 0), new Vector3(0.25f, 0.6f, 2.0f), "Leather", true);
        foreach (float dz in new[] { -1.05f, 1.05f }) Box(a, "Sofa_Arm", sp + new Vector3(0.05f, 0.55f, dz), new Vector3(0.8f, 0.3f, 0.15f), "Leather", true);
        Box(a, "CoffeeTable", new Vector3(2.6f, 0.22f, 35.5f), new Vector3(0.6f, 0.44f, 1.0f), "WoodDark", true);
        Box(a, "CigarBox", new Vector3(2.6f, 0.47f, 35.3f), new Vector3(0.22f, 0.06f, 0.14f), "Leather");
        // глобус-бар
        var gp = new Vector3(8.3f, 0f, 36.8f);
        Cyl(a, "Globe_Stand", gp + Vector3.up * 0.3f, 0.05f, 0.6f, "WoodDark", true);
        Ball(a, "Globe", gp + Vector3.up * 0.85f, Vector3.one * 0.6f, "Globe");
        Rod(a, "Globe_Ring", gp + new Vector3(0, 0.5f, 0), gp + new Vector3(0, 1.2f, 0), 0.015f, "Brass");
        foreach (var p in new[] { new Vector3(9.4f, 0f, 38.3f), new Vector3(1.6f, 0f, 29.0f) }) Plant(a, p, 1.5f);
        // на столе: лампа, телефон, табличка
        var top = 0.79f;
        Box(a, "DeskLamp_Base", new Vector3(4.6f, top + 0.015f, 35.45f), new Vector3(0.18f, 0.03f, 0.12f), "Brass");
        Box(a, "DeskLamp_Stem", new Vector3(4.6f, top + 0.15f, 35.45f), new Vector3(0.02f, 0.26f, 0.02f), "Brass");
        Cyl(a, "DeskLamp_Shade", new Vector3(4.6f, top + 0.3f, 35.42f), 0.09f, 0.32f, "Green", false, Quaternion.Euler(0, 0, 90));
        Box(a, "DeskLamp_Glow", new Vector3(4.6f, top + 0.255f, 35.42f), new Vector3(0.28f, 0.01f, 0.1f), "LampWarm");
        Box(a, "Phone_Body", new Vector3(6.2f, top + 0.04f, 35.1f), new Vector3(0.2f, 0.08f, 0.16f), "Plastic");
        Box(a, "NamePlate", new Vector3(4.9f, top + 0.04f, 34.95f), new Vector3(0.3f, 0.07f, 0.03f), "Brass", false, Quaternion.Euler(-20, 0, 0));
        // портрет директора на западной стене (смотрит в комнату, +X)
        Box(a, "PortraitFrame", new Vector3(1.1f, 2.0f, 37.5f), new Vector3(0.05f, 1.36f, 1.06f), "FrameGold");
        var portrait = Box(a, "Portrait", new Vector3(1.13f, 2.0f, 37.5f), new Vector3(0.01f, 1.2f, 0.9f), "Cream");
        var art = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Map/M_Art3.mat");
        if (art) portrait.GetComponent<MeshRenderer>().sharedMaterial = art;
        Pool(a, "DeskLampPool", new Vector3(5.2f, top + 0.065f, 35.3f), 0.7f, "PoolWarm");
    }

    static void Bookshelf(Transform a, Vector3 p, float yaw)
    {
        var q = Quaternion.Euler(0, yaw, 0);
        Vector3 W(float x, float y, float z) => p + q * new Vector3(x, y, z);
        Box(a, "Shelf_Back", W(0, 1.1f, 0.18f), new Vector3(1.2f, 2.2f, 0.04f), "WoodDark", false, q);
        foreach (float sx in new[] { -0.58f, 0.58f }) Box(a, "Shelf_Side", W(sx, 1.1f, 0), new Vector3(0.04f, 2.2f, 0.4f), "WoodDark", false, q);
        string[] books = { "Book1", "Book2", "Book3", "Book4" };
        for (int s = 0; s < 5; s++)
        {
            float y = 0.05f + s * 0.48f;
            Box(a, "Shelf_Board", W(0, y, 0), new Vector3(1.16f, 0.04f, 0.38f), "WoodDark", false, q);
            if (s == 4) continue;
            float x = -0.52f;
            int k = 0;
            while (x < 0.5f)
            {
                float w = 0.05f + Hash(s, k, 3) * 0.05f, h = 0.26f + Hash(k, s, 9) * 0.12f;
                Box(a, "Book", W(x + w / 2, y + 0.02f + h / 2, 0.02f), new Vector3(w, h, 0.26f), books[(s + k) % 4], false, q);
                x += w + 0.006f; k++;
            }
        }
        AddCollider(a, "Bookshelf", W(0, 1.1f, 0), new Vector3(1.2f, 2.2f, 0.4f), q);
    }

    // =========================================================================== подвал
    static void Basement(Transform a)
    {
        float y = BasementY;
        // бетон поверх «досок» в предбаннике и тёмный потолок (вокруг лестничного проёма)
        Tiled(a, "BasementFloor", new Vector3(-2f, y + 0.004f, 27.475f), Vector3.up, Vector3.forward, 24f, 6.65f, "Concrete", 4f);
        foreach (var (x0, x1) in new[] { (-14f, -3.5f), (2.5f, 10f) })
            Tiled(a, "BasementCeiling", new Vector3((x0 + x1) / 2, -0.205f, 27.475f), Vector3.down, Vector3.forward, x1 - x0, 6.65f, "ConcreteDark", 4f);
        Tiled(a, "BasementCeiling", new Vector3(-0.5f, -0.205f, 29.2f), Vector3.down, Vector3.forward, 6f, 3.2f, "ConcreteDark", 4f);
        // жёлтая разметка по краям прохода и у двери хранилища
        Box(a, "FloorLine", new Vector3(-2f, y + 0.007f, 30.55f), new Vector3(24f, 0.004f, 0.12f), "Hazard");
        for (int i = 0; i < 9; i++)
            Box(a, "Chevron", new Vector3(-2.9f + i * 0.4f, y + 0.008f, 30.2f), new Vector3(0.18f, 0.004f, 0.5f), i % 2 == 0 ? "Hazard" : "HazardBlack", false, Quaternion.Euler(0, 35, 0));
        // трубы-турник под потолком + стояки (R4)
        foreach (float z in new[] { 30.4f, 29.0f })
        {
            Rod(a, "Pipe", new Vector3(-13.7f, -0.5f, z), new Vector3(9.7f, -0.5f, z), 0.07f, "Steel", true);
            for (float x = -12f; x < 9.5f; x += 3f)
            {
                Box(a, "PipeClamp", new Vector3(x, -0.5f, z), new Vector3(0.06f, 0.18f, 0.18f), "DarkSteel");
                Rod(a, "PipeHanger", new Vector3(x, -0.42f, z), new Vector3(x, -0.21f, z), 0.012f, "DarkSteel");
            }
        }
        Box(a, "PipeValve", new Vector3(-6f, -0.5f, 30.4f), new Vector3(0.12f, 0.12f, 0.2f), "Red");
        Cyl(a, "PipeValveWheel", new Vector3(-6f, -0.5f, 30.2f), 0.12f, 0.02f, "Red", false, Quaternion.Euler(90, 0, 0));
        foreach (float x in new[] { -13.75f, 9.75f })
        {
            Rod(a, "Riser", new Vector3(x, y, 29.7f), new Vector3(x, -0.2f, 29.7f), 0.08f, "Steel", true);
            Rod(a, "RiserElbow", new Vector3(x, -0.5f, 29.0f), new Vector3(x, -0.5f, 30.4f), 0.07f, "Steel");
        }
        // клетка с золотом (декор заперт — не путается с настоящим лутом)
        float cx0 = -13.85f, cx1 = -10f, cz0 = 27.6f, cz1 = 30.5f, ch = 2.4f;
        for (float x = cx0; x <= cx1 + 0.01f; x += 0.22f) { Box(a, "CageBar", new Vector3(x, y + ch / 2, cz0), new Vector3(0.03f, ch, 0.03f), "DarkSteel"); }
        for (float z = cz0; z <= cz1 + 0.01f; z += 0.22f) { Box(a, "CageBar", new Vector3(cx1, y + ch / 2, z), new Vector3(0.03f, ch, 0.03f), "DarkSteel"); }
        foreach (float yy in new[] { 0.05f, 1.2f, ch })
        {
            Box(a, "CageRail", new Vector3((cx0 + cx1) / 2, y + yy, cz0), new Vector3(cx1 - cx0, 0.05f, 0.05f), "DarkSteel");
            Box(a, "CageRail", new Vector3(cx1, y + yy, (cz0 + cz1) / 2), new Vector3(0.05f, 0.05f, cz1 - cz0), "DarkSteel");
        }
        Box(a, "CageTop", new Vector3((cx0 + cx1) / 2, y + ch, (cz0 + cz1) / 2), new Vector3(cx1 - cx0, 0.03f, cz1 - cz0), "DarkSteel");
        AddCollider(a, "CageWall_S", new Vector3((cx0 + cx1) / 2, y + ch / 2, cz0), new Vector3(cx1 - cx0, ch, 0.06f));
        AddCollider(a, "CageWall_E", new Vector3(cx1, y + ch / 2, (cz0 + cz1) / 2), new Vector3(0.06f, ch, cz1 - cz0));
        AddCollider(a, "CageTop", new Vector3((cx0 + cx1) / 2, y + ch, (cz0 + cz1) / 2), new Vector3(cx1 - cx0, 0.04f, cz1 - cz0));
        Plate(a, "Sign_Cage", "ЗОЛОТОЙ ЗАПАС", new Vector3(-11.9f, y + 2.05f, cz0 - 0.04f), Vector3.back, 1.1f, 0.22f);
        Tripo(a, "Bank_GoldPallet", "GoldPallet", new Vector3(-12.6f, y, 29.6f), 20f, 1.2f, 0, null, false);
        CashShelf(a, new Vector3(-11.0f, y, 30.15f), 180f);
        Pool(a, "CageGlow", new Vector3(-12.4f, y + 0.01f, 29.2f), 1.6f, "PoolGold");
        // тележки с ящиками (не на пути в гараж x 2.5…10, z 24.5…28.5)
        GoldCart(a, new Vector3(4.2f, y, 29.8f), 0f);
        GoldCart(a, new Vector3(9.2f, y, 29.9f), 90f);
        // выход в гараж: «ВЫХОД», полосатая рамка, янтарный свет за проёмом
        Box(a, "ExitBox", new Vector3(9.83f, -0.6f, 26.6f), new Vector3(0.06f, 0.26f, 0.9f), "ExitGreen");
        Text(a, "ExitText", "ВЫХОД", new Vector3(9.79f, -0.6f, 26.6f), Vector3.left, 2f, new Vector2(0.85f, 0.22f), Color.white);
        for (int i = 0; i < 6; i++)
            foreach (float z in new[] { 24.95f, 28.25f })
                Box(a, "HazardFrame", new Vector3(9.82f, y + 0.2f + i * 0.36f, z), new Vector3(0.04f, 0.18f, 0.12f), i % 2 == 0 ? "Hazard" : "HazardBlack", false, Quaternion.Euler(45, 0, 0));
        Pool(a, "GarageAmber", new Vector3(11.6f, y + 0.01f, 26.6f), 2.2f, "PoolAmber");
        Plate(a, "Sign_Vault", "ХРАНИЛИЩЕ", new Vector3(-4.3f, -1.0f, 30.78f), Vector3.back, 1.6f, 0.4f);
    }

    static void CashShelf(Transform a, Vector3 p, float yaw)
    {
        var q = Quaternion.Euler(0, yaw, 0);
        Vector3 W(float x, float y, float z) => p + q * new Vector3(x, y, z);
        foreach (float sx in new[] { -0.78f, 0.78f }) Box(a, "Rack_Post", W(sx, 1.0f, 0), new Vector3(0.05f, 2.0f, 0.5f), "Steel", false, q);
        for (int s = 0; s < 4; s++)
        {
            float y = 0.1f + s * 0.6f;
            Box(a, "Rack_Shelf", W(0, y, 0), new Vector3(1.6f, 0.04f, 0.5f), "Steel", false, q);
            if (s == 3) continue;
            for (int i = 0; i < 6; i++) for (int j = 0; j < 2; j++)
                Box(a, "CashStack", W(-0.62f + i * 0.25f, y + 0.07f + j * 0.1f, 0), new Vector3(0.2f, 0.09f, 0.32f), "Cash", false, q);
        }
        AddCollider(a, "CashShelf", W(0, 1.0f, 0), new Vector3(1.6f, 2.0f, 0.5f), q);
    }

    static void GoldCart(Transform a, Vector3 p, float yaw)
    {
        var q = Quaternion.Euler(0, yaw, 0);
        Vector3 W(float x, float y, float z) => p + q * new Vector3(x, y, z);
        Box(a, "Cart_Bed", W(0, 0.3f, 0), new Vector3(1.0f, 0.05f, 0.6f), "Steel", false, q);
        foreach (var (sx, sz) in new[] { (-0.42f, -0.24f), (0.42f, -0.24f), (-0.42f, 0.24f), (0.42f, 0.24f) })
            Cyl(a, "Cart_Wheel", W(sx, 0.08f, sz), 0.08f, 0.05f, "HazardBlack", false, q * Quaternion.Euler(90, 0, 0));
        Rod(a, "Cart_Handle", W(-0.5f, 0.3f, -0.27f), W(-0.5f, 0.95f, -0.27f), 0.02f, "Steel");
        Rod(a, "Cart_Handle", W(-0.5f, 0.3f, 0.27f), W(-0.5f, 0.95f, 0.27f), 0.02f, "Steel");
        Rod(a, "Cart_Handle", W(-0.5f, 0.95f, -0.27f), W(-0.5f, 0.95f, 0.27f), 0.02f, "Steel");
        Box(a, "Cart_Crate", W(0.05f, 0.53f, 0), new Vector3(0.7f, 0.42f, 0.5f), "Crate", false, q);
        Box(a, "Cart_CrateBand", W(0.05f, 0.53f, 0), new Vector3(0.72f, 0.06f, 0.52f), "DarkSteel", false, q);
        Box(a, "Cart_CrateLabel", W(0.05f, 0.6f, -0.26f), new Vector3(0.22f, 0.16f, 0.005f), "Hazard", false, q);
        AddCollider(a, "GoldCart", W(0, 0.45f, 0), new Vector3(1.0f, 0.9f, 0.6f), q);
    }

    // =========================================================================== хранилище (только за лазерами, z ≥ 36.9)
    static void Vault(Transform a)
    {
        float y = BasementY;
        // стены банковских ячеек — заподлицо (≤ 6 см), без колайдеров
        Box(a, "DepositBack_N", new Vector3(7.4f, -1.75f, 39.83f), new Vector3(4.4f, 2.3f, 0.04f), "DarkSteel");
        Tiled(a, "DepositWall_N", new Vector3(7.4f, -1.75f, 39.8f), Vector3.back, Vector3.up, 4.4f, 2.3f, "Deposit", 1.2f);
        Box(a, "DepositBack_W", new Vector3(-13.83f, -1.75f, 38.3f), new Vector3(0.04f, 2.3f, 2.6f), "DarkSteel");
        Tiled(a, "DepositWall_W", new Vector3(-13.8f, -1.75f, 38.3f), Vector3.right, Vector3.up, 2.6f, 2.3f, "Deposit", 1.2f);
        Box(a, "DepositBack_E", new Vector3(9.83f, -1.75f, 38.25f), new Vector3(0.04f, 2.3f, 2.7f), "DarkSteel");
        Tiled(a, "DepositWall_E", new Vector3(9.8f, -1.75f, 38.25f), Vector3.left, Vector3.up, 2.7f, 2.3f, "Deposit", 1.2f);
        foreach (var (p, s) in new[] { (new Vector3(7.4f, -0.58f, 39.8f), new Vector3(4.5f, 0.05f, 0.08f)), (new Vector3(7.4f, -2.92f, 39.8f), new Vector3(4.5f, 0.05f, 0.08f)) })
            Box(a, "DepositTrim", p, s, "Brass");
        // стеллажи с пачками долларов у северной стены
        CashShelf(a, new Vector3(0.0f, y, 39.45f), 0f);
        CashShelf(a, new Vector3(1.8f, y, 39.45f), 0f);
        // паллета золота под прожектором + тележка
        Tripo(a, "Bank_GoldPallet", "GoldPallet", new Vector3(4.2f, y, 38.6f), -15f, 1.3f, 0, null, false);
        AddCollider(a, "GoldPallet", new Vector3(4.2f, y + 0.5f, 38.6f), new Vector3(1.3f, 1.0f, 1.3f));
        GoldCart(a, new Vector3(6.5f, y, 37.6f), 0f);
        Pool(a, "VaultGoldPool", new Vector3(4.2f, y + 0.01f, 38.6f), 2.0f, "PoolGold");
        // труба под потолком за лазерами
        Rod(a, "Pipe", new Vector3(-13.7f, -0.4f, 39.5f), new Vector3(9.7f, -0.4f, 39.5f), 0.07f, "Steel", true);
    }

    // =========================================================================== свет (4 новых источника, без теней)
    static void Lights(Transform g)
    {
        void L(string n, LightType t, Vector3 pos, Color c, float i, float range, float angle = 0f, Vector3? dir = null)
        {
            var go = new GameObject(n);
            go.transform.SetParent(g, false);
            go.transform.position = pos;
            if (dir.HasValue) go.transform.rotation = Quaternion.LookRotation(dir.Value);
            var l = go.AddComponent<Light>();
            l.type = t; l.color = c; l.intensity = i; l.range = range; l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.Auto;
            if (t == LightType.Spot) { l.spotAngle = angle; l.innerSpotAngle = angle * 0.5f; }
        }
        L("EntrancePink", LightType.Point, new Vector3(0f, 3.0f, -2.6f), Hex("FF6FB0"), 1.5f, 6f);
        L("DirectorDeskLamp", LightType.Point, new Vector3(4.6f, 1.15f, 35.3f), Hex("FFC27A"), 1.2f, 3.5f);
        L("VendingGlow", LightType.Point, new Vector3(-12.9f, 1.2f, 31f), Hex("9FE8FF"), 0.8f, 2.5f);
        L("VaultGoldSpot", LightType.Spot, new Vector3(4.2f, -0.35f, 38.6f), Hex("FFC845"), 3f, 4f, 55f, Vector3.down);
    }

    // =========================================================================== прочее
    /// Старая люстра холла (0, 8.4, 12) без колайдера — вместо неё латунная центральная (R1). Прячется, не удаляется.
    static void HideOldChandelier()
    {
        int n = 0;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (r.transform.IsChildOf(GameObject.Find(RootName).transform)) continue;
            var c = r.bounds.center;
            if (Mathf.Abs(c.x) < 1.6f && Mathf.Abs(c.z - 12f) < 1.6f && c.y > 7.4f && c.y < 9.6f && r.bounds.size.y < 2.5f)
            {
                r.gameObject.SetActive(false);
                EditorUtility.SetDirty(r.gameObject);
                n++;
            }
        }
        log.AppendLine($"  old hall chandelier parts hidden: {n}");
    }

    /// Склейка: видимые примитивы зоны → один меш на материал (Quest: статический батчинг в проекте выключен).
    static void Combine(Transform area)
    {
        var visual = area.Find("Visual");
        var groups = visual.GetComponentsInChildren<MeshFilter>()
            .GroupBy(mf => mf.GetComponent<MeshRenderer>().sharedMaterial);
        foreach (var grp in groups)
        {
            var list = grp.ToList();
            var ci = list.Select(mf => new CombineInstance { mesh = mf.sharedMesh, transform = mf.transform.localToWorldMatrix }).ToArray();
            int verts = list.Sum(mf => mf.sharedMesh.vertexCount);
            var mesh = new Mesh { name = $"BankDetail_{area.name}_{grp.Key.name}", indexFormat = verts > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.CombineMeshes(ci, true, true);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            AssetDatabase.CreateAsset(mesh, $"{MeshDir}/{mesh.name}.asset");
            var go = new GameObject($"Combined_{grp.Key.name}");
            go.transform.SetParent(area, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = grp.Key;
            r.shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }
        Object.DestroyImmediate(visual.gameObject);
        foreach (var t in area.Find("Colliders").GetComponentsInChildren<Transform>())
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
    }

    /// Доп. ракурсы проверки (люстры, паркур, кабинет, хранилище за лазерами, машины) → HubPreview/Detail_*.png
    [MenuItem("Tools/Heist/Bank/Detail Pass Preview")]
    public static string CapturePreview()
    {
        var shots = new (string name, Vector3 pos, Vector3 look)[]
        {
            ("Detail_00_warmup", new Vector3(0f, 1.7f, 9f), new Vector3(0f, 1.6f, 22f)),
            ("Detail_01_street_cars", new Vector3(-24f, 0.6f, -14.5f), new Vector3(-10f, -0.4f, -11.5f)),
            ("Detail_02_hall_chandeliers", new Vector3(-11f, 5.9f, 4f), new Vector3(4f, 6.3f, 13f)),
            ("Detail_03_queue_rates", new Vector3(0f, 1.6f, 11.5f), new Vector3(-6f, 0.9f, 16.5f)),
            ("Detail_04_corridor_tray", new Vector3(8.5f, 1.8f, 26.6f), new Vector3(-6f, 2.4f, 25.4f)),
            ("Detail_05_director", new Vector3(4.8f, 1.7f, 29f), new Vector3(5.5f, 0.9f, 38f)),
            ("Detail_06_break_room", new Vector3(-7f, 1.7f, 29.2f), new Vector3(-12f, 0.8f, 35f)),
            ("Detail_07_cage", new Vector3(-6.5f, -1.4f, 26.4f), new Vector3(-12.5f, -2.3f, 29.4f)),
            ("Detail_08_vault_back", new Vector3(-3f, -1.2f, 37.3f), new Vector3(6f, -2f, 39.5f)),
            ("Detail_09_basement_pipes", new Vector3(8.5f, -2.2f, 25.2f), new Vector3(-8f, -0.6f, 30f)),
        };
        string dir = System.IO.Path.GetFullPath("HubPreview");
        System.IO.Directory.CreateDirectory(dir);
        var go = new GameObject("PreviewCam");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 80f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 120f; cam.allowHDR = true;
        go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing = true;
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
        return "saved " + shots.Length + " to " + dir;
    }

    /// Ни одного нового колайдера в зоне лазеров хранилища (z 32.3…36.8) — иначе руки цепляются за лучи.
    static void CheckLaserZone(Transform root)
    {
        var zone = new Bounds(new Vector3(-2f, -1.6f, 34.55f), new Vector3(24f, 2.8f, 4.5f));
        var bad = root.GetComponentsInChildren<Collider>().Where(c => c.bounds.Intersects(zone)).Select(c => c.name).ToList();
        log.AppendLine(bad.Count == 0 ? "  laser zone: clear" : "  ERROR: colliders in laser zone: " + string.Join(", ", bad));
    }
}
