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
/// Чистый лофт-гараж вместо «нейрослопа» в MainHub (стиль Gorilla Tag / GILLA): только чистые модульные
/// формы и строго 5 материалов — тёплый красный кирпич, гладкий серый бетон (+ жёлтая разметка тем же
/// материалом с подкраской), тёплое тёмное дерево, матовый чёрный металл, тёплое неоновое свечение.
///
///  1) Зачистка: корень HubDecor (старые Tripo-пропсы, трубы, поддоны, вытяжки), Tripo-модели и кривая
///     архитектура из MainHubMap/Static, старые лампы. Рабочие системы НЕ трогаются: GRABZ OS, зеркало,
///     SkinTerminal, витрина косметики, стол режимов, баскетбол, VanPortal/автобус/тоннель, сеть, игроки.
///  2) Архитектура: тот же объём комнаты (от него зависят автобус и тоннель), стены/пол разбиты на секции
///     (на Quest ≤ 4 доп. источника на объект), высокие лофт-окна с закатным светом, балкон второго яруса
///     с лестницей, балки и уступы для паркура (все коллайдеры — BoxCollider на слое Default).
///  3) Зоны: Гардероб (зеркало в деревянной раме, полки с масками, кнопки цвета), Планирование и сеть
///     (командный стол, GRABZ OS рядом, карта банка и доска рекордов на стене), Выезд (гаражные ворота,
///     автобус, разметка), Отдых (геометрический диван, ковёр, столик), Баскетбол (чистый щит и кольцо).
///  4) Свет: тёплые лампы над зонами + мягкие споты от окон, без Bloom (90 FPS на Quest).
/// Идемпотентный: пересоздаёт только корень «HubLoft». Меню: Tools/Heist/Hub/Clean Loft Overhaul
/// </summary>
public static class HubLoftBuilder
{
    const string HubPath = "Assets/Scenes/MainHub.unity";
    const string MatDir = "Assets/Art/Materials/Hub";
    const string TexDir = "Assets/Art/Textures/Hub";
    const string RootName = "HubLoft";
    const float X0 = -11.8f, X1 = 11.8f, Z0 = -9.8f, Z1 = 13.8f, Hc = 8f;   // внутренние грани комнаты
    const float DeckY = 3.5f, DeckZ = -6.9f, DeckX1 = 7.8f;                // балкон над спавном

    static Material brick, concrete, wood, metal, glow;
    static readonly StringBuilder log = new StringBuilder();
    static Transform root;
    static int colliders;

    [MenuItem("Tools/Heist/Hub/Clean Loft Overhaul")]
    static void Menu() => Debug.Log(Run());

    public static string Run()
    {
        log.Clear().AppendLine("[HubLoftBuilder]");
        colliders = 0;
        Materials();
        var scene = EditorSceneManager.GetActiveScene().path == HubPath ? EditorSceneManager.GetActiveScene() : EditorSceneManager.OpenScene(HubPath, OpenSceneMode.Single);
        var map = scene.GetRootGameObjects().First(g => g.name == "MainHubMap").transform;

        // Post-FX без Bloom (переносим объём из удаляемого HubDecor)
        var oldDecor = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "HubDecor");
        foreach (var g in scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray()) Object.DestroyImmediate(g);
        root = new GameObject(RootName).transform;
        var fx = oldDecor ? oldDecor.transform.Find("HubPostFX") : null;
        if (fx) fx.SetParent(root, true);
        else fx = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Volume>(true)).Select(v => v.transform).FirstOrDefault();
        SoftenPostFX(fx);

        Clean(map, oldDecor);
        Shell(map);
        Windows();
        Balcony();
        Parkour();
        Wardrobe(map);
        Planning(map);
        ExitZone(map);
        RestZone();
        Basketball(map);
        Lighting(map);
        Restyle(map, scene);

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (!t.GetComponent<Light>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) log.AppendLine("ERROR: save failed");
        AssetDatabase.SaveAssets();
        log.AppendLine($"  loft: {root.GetComponentsInChildren<Renderer>(true).Length} renderers, {colliders} colliders, {root.GetComponentsInChildren<Light>(true).Length} lights; materials: 5");
        return log.ToString();
    }

    // =========================================================================== 1. зачистка
    static readonly string[] KeepStatic =
    {
        "Floor", "Ceiling", "Wall_N", "Wall_S", "Wall_W", "Wall_E", "Wall_N_Colliders", "HubTitle", "HubTitleBack", "RulesSign",
        "ModeBoard_Back", "ModeBoard_FrameT", "ModeBoard_FrameB", "ModeBoard_Cable", "ModeSign", "WardrobeSign", "HoopTitle", "HoopScoreBack",
    };

    static void Clean(Transform map, GameObject oldDecor)
    {
        int removed = 0;
        if (oldDecor) { Object.DestroyImmediate(oldDecor); removed++; }
        var st = map.Find("Static");
        foreach (Transform t in st.Cast<Transform>().ToArray())
            if (!KeepStatic.Contains(t.name)) { Object.DestroyImmediate(t.gameObject); removed++; }
        var lights = map.Find("Lights");
        if (lights) foreach (Transform t in lights.Cast<Transform>().ToArray()) { Object.DestroyImmediate(t.gameObject); removed++; }
        // старые визуалы пола/стен прячем (коллайдеры комнаты остаются), вместо них — секции
        foreach (var n in new[] { "Floor", "Wall_S", "Wall_W", "Wall_E" })
        {
            var r = st.Find(n)?.GetComponent<MeshRenderer>();
            if (r) r.enabled = false;
        }
        log.AppendLine($"  cleaned: {removed} old objects (HubDecor, Tripo props, crooked architecture, old lamps)");
    }

    // =========================================================================== материалы (5 шт.)
    static void Materials()
    {
        MapKit.EnsureFolder(MatDir); MapKit.EnsureFolder(TexDir);
        brick = Lit("M_Hub_Loft_Brick", new Color(1f, 1f, 1f), Tex("T_Hub_Loft_Brick", Brick), 0.05f, 0f);
        concrete = Lit("M_Hub_Loft_Concrete", new Color(1f, 1f, 1f), Tex("T_Hub_Loft_Concrete", Concrete), 0.18f, 0f);
        wood = Lit("M_Hub_Loft_Wood", new Color(1f, 1f, 1f), Tex("T_Hub_Loft_Wood", Wood), 0.2f, 0f);
        metal = Lit("M_Hub_Loft_Metal", new Color(1f, 1f, 1f), Tex("T_Hub_Loft_Metal", Metal), 0.25f, 0.4f);
        string gp = $"{MatDir}/M_Hub_Loft_Glow.mat";
        glow = AssetDatabase.LoadAssetAtPath<Material>(gp);
        if (!glow) { glow = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(glow, gp); }
        glow.shader = Shader.Find("Universal Render Pipeline/Unlit");
        glow.SetColor("_BaseColor", new Color(1f, 0.62f, 0.3f));          // тёплый неон (подкраска — MaterialPropertyBlock)
        EditorUtility.SetDirty(glow);
    }

    static Material Lit(string name, Color c, Texture2D tex, float smooth, float metallic)
    {
        string path = $"{MatDir}/{name}.mat";
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetColor("_BaseColor", c); m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metallic);
        m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        m.SetFloat("_SpecularHighlights", 1f);
        EditorUtility.SetDirty(m);
        return m;
    }

    const int N = 256;
    static float Hash(int x, int y) { unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffff) / 65535f; } }
    static float Noise(float u, float v, float f) => Mathf.PerlinNoise(u * f + 11.3f, v * f + 7.1f);

    // текстура = 1 м (UV в метрах, тайлинг 1)
    static Color Brick(float u, float v)
    {
        // кирпич 0.25 × 0.0625 м (4 × 16 на метр), шов 1 см, ряды вразбежку
        float rows = 16f, cols = 4f;
        int row = Mathf.FloorToInt(v * rows);
        float uu = u * cols + (row % 2) * 0.5f;
        int col = Mathf.FloorToInt(uu);
        float fu = uu - col, fv = v * rows - row;
        bool mortar = fu < 0.04f || fv < 0.16f;
        if (mortar) { float m = 0.62f + (Noise(u, v, 40) - 0.5f) * 0.06f; return new Color(m * 0.9f, m * 0.86f, m * 0.8f); }
        float k = 0.88f + (Hash(col, row) - 0.5f) * 0.18f + (Noise(u, v, 22) - 0.5f) * 0.08f;
        return new Color(0.62f * k, 0.25f * k, 0.18f * k);                         // тёплый красный
    }

    static Color Concrete(float u, float v)
    {
        float k = 0.56f + (Noise(u, v, 6) - 0.5f) * 0.06f + (Noise(u, v, 40) - 0.5f) * 0.03f;   // гладкий, без грязи
        return new Color(k, k, k * 1.01f);
    }

    static Color Wood(float u, float v)
    {
        // доски 0.2 м шириной вдоль V, тонкий тёмный шов, мягкие волокна
        float planks = 5f;
        int p = Mathf.FloorToInt(u * planks);
        float fu = u * planks - p;
        if (fu < 0.03f) return new Color(0.11f, 0.06f, 0.035f);
        float grain = Mathf.Sin((v * 18f + Noise(u, v, 6) * 3f + p * 1.7f) * Mathf.PI) * 0.5f + 0.5f;
        float k = 0.85f + (Hash(p, 3) - 0.5f) * 0.16f + grain * 0.1f;
        return new Color(0.36f * k, 0.2f * k, 0.11f * k);                          // тёплое тёмное дерево
    }

    static Color Metal(float u, float v)
    {
        float k = 0.075f + (Noise(u, v, 30) - 0.5f) * 0.015f;
        return new Color(k, k, k * 1.05f);                                        // матовый чёрный
    }

    static Texture2D Tex(string name, System.Func<float, float, Color> fn)
    {
        string path = $"{TexDir}/{name}.png";
        var t = new Texture2D(N, N, TextureFormat.RGB24, true);
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) t.SetPixel(x, y, fn((x + 0.5f) / N, (y + 0.5f) / N));
        t.Apply();
        System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = TextureWrapMode.Repeat; imp.filterMode = FilterMode.Bilinear; imp.maxTextureSize = N; imp.mipmapEnabled = true;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // =========================================================================== примитивы
    static Transform Group(Transform p, string n) { var g = new GameObject(n).transform; g.SetParent(p, false); return g; }

    /// Куб с UV в метрах (текстура тайлится, а не тянется). collider — BoxCollider на слое Default.
    static GameObject Box(Transform p, string n, Vector3 pos, Vector3 size, Material m, bool collider = true, Quaternion? rot = null, Color? tint = null)
    {
        var go = new GameObject(n);
        go.transform.SetParent(p, false);
        go.transform.SetPositionAndRotation(pos, rot ?? Quaternion.identity);
        go.transform.localScale = size;
        go.layer = 0;
        go.AddComponent<MeshFilter>().sharedMesh = m == glow ? Resources.GetBuiltinResource<Mesh>("Cube.fbx") : HubVisualOverhaul.BoxMesh(size, 1f);
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        if (tint.HasValue) Tint(r, tint.Value);
        if (collider) { go.AddComponent<BoxCollider>(); colliders++; }
        return go;
    }

    static void Tint(Renderer r, Color c)
    {
        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", c);
        r.SetPropertyBlock(mpb);
    }

    static TextMeshPro Text(Transform p, string n, string text, float size, Color c, Vector3 pos, Quaternion rot, Vector2 rect, bool bold = true)
    {
        MapKit.Font = MapKit.Font ? MapKit.Font : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MapKit.FontPath);
        return MapKit.Text(p, n, text, size, c, pos, rot, rect, TextAlignmentOptions.Center, bold);
    }

    static Light PointLamp(Transform p, string n, Vector3 pos, float intensity, float range)
    {
        // подвесная лампа: шнур, чёрный плафон-конус, тёплая лампочка + источник света без теней
        var g = Group(p, n);
        Box(g, "Cord", new Vector3(pos.x, (pos.y + Hc) / 2f + 0.2f, pos.z), new Vector3(0.02f, Hc - pos.y - 0.4f, 0.02f), metal, false);
        Box(g, "Shade", pos + Vector3.up * 0.12f, new Vector3(0.55f, 0.18f, 0.55f), metal, false);
        Box(g, "ShadeRim", pos + Vector3.up * 0.02f, new Vector3(0.62f, 0.04f, 0.62f), metal, false);
        Box(g, "Bulb", pos - Vector3.up * 0.02f, new Vector3(0.18f, 0.1f, 0.18f), glow, false, null, new Color(1f, 0.78f, 0.5f));
        var lgo = new GameObject("WarmLight"); lgo.transform.SetParent(g, false); lgo.transform.position = pos - Vector3.up * 0.15f;
        var l = lgo.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.76f, 0.5f); l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
        return l;
    }

    // =========================================================================== 2. оболочка
    static void Shell(Transform map)
    {
        var g = Group(root, "Shell");
        // пол: бетон секциями 4 × 4 м (у каждой — свои ближайшие лампы)
        for (float x = X0; x < X1 - 0.01f; x += 4f)
            for (float z = Z0; z < Z1 - 0.01f; z += 4f)
            {
                float w = Mathf.Min(4f, X1 - x), d = Mathf.Min(4f, Z1 - z);
                Box(g, "Floor", new Vector3(x + w / 2f, -0.05f, z + d / 2f), new Vector3(w, 0.1f, d), concrete, false);
            }
        // стены кирпичом секциями по 4 м (северная — куски вокруг въезда в тоннель)
        void WallX(string n, float z, float face)   // вдоль X
        {
            for (float x = X0; x < X1 - 0.01f; x += 4f) { float w = Mathf.Min(4f, X1 - x); Box(g, n, new Vector3(x + w / 2f, Hc / 2f, z + face * 0.03f), new Vector3(w, Hc, 0.06f), brick, false); }
        }
        void WallZ(string n, float x, float face)   // вдоль Z
        {
            for (float z = Z0; z < Z1 - 0.01f; z += 4f) { float d = Mathf.Min(4f, Z1 - z); Box(g, n, new Vector3(x + face * 0.03f, Hc / 2f, z + d / 2f), new Vector3(0.06f, Hc, d), brick, false); }
        }
        WallX("Wall_S", Z0, 1f); WallZ("Wall_W", X0, 1f); WallZ("Wall_E", X1, -1f);
        // плинтус — чёрный металл
        Box(g, "Base_S", new Vector3(0, 0.08f, Z0 + 0.08f), new Vector3(X1 - X0, 0.16f, 0.04f), metal, false);
        Box(g, "Base_W", new Vector3(X0 + 0.08f, 0.08f, (Z0 + Z1) / 2f), new Vector3(0.04f, 0.16f, Z1 - Z0), metal, false);
        Box(g, "Base_E", new Vector3(X1 - 0.08f, 0.08f, (Z0 + Z1) / 2f), new Vector3(0.04f, 0.16f, Z1 - Z0), metal, false);
        // потолок — тёмное дерево, широкие балки
        var ceil = map.Find("Static/Ceiling")?.GetComponent<MeshRenderer>(); if (ceil) ceil.sharedMaterial = wood;
        log.AppendLine("  shell: floor 4×4 m tiles, brick walls in 4 m sections, metal baseboard, wood ceiling");
    }

    // ---- высокие лофт-окна с закатом снаружи (запад, восток, юг над балконом)
    static readonly List<(Vector3 center, Vector3 inward, float w, float h)> windowList = new List<(Vector3, Vector3, float, float)>();

    static void Windows()
    {
        windowList.Clear();
        var g = Group(root, "LoftWindows");
        foreach (float z in new[] { -2.6f, 2.4f, 7.4f }) windowList.Add((new Vector3(X0, 5.9f, z), Vector3.right, 3.2f, 3.0f));
        foreach (float z in new[] { 0.4f, 5.4f, 10.0f }) windowList.Add((new Vector3(X1, 5.9f, z), Vector3.left, 3.2f, 3.0f));
        foreach (float x in new[] { -7.6f, -2.0f, 3.6f }) windowList.Add((new Vector3(x, 5.95f, Z0), Vector3.forward, 4.0f, 2.9f));
        int i = 0;
        foreach (var (c, inw, w, h) in windowList)
        {
            var wg = Group(g, $"Window_{i++}");
            var rot = Quaternion.LookRotation(inw);
            Vector3 right = rot * Vector3.right;
            Vector3 P(float a, float b, float d) => c + right * a + Vector3.up * b + inw * d;
            // закатное «небо» снаружи: тёплый градиент полосами (тот же неон, подкраска)
            for (int s = 0; s < 3; s++)
            {
                float y0 = -h / 2f + s * h / 3f;
                var col = Color.Lerp(new Color(1f, 0.52f, 0.28f), new Color(0.55f, 0.36f, 0.52f), s / 2f) * 0.85f;
                Box(wg, "Sky", P(0, y0 + h / 6f, 0.085f), new Vector3(w, h / 3f, 0.02f), glow, false, rot, col);
            }
            // рама и переплёт — матовый чёрный металл
            Box(wg, "FrameT", P(0, h / 2f + 0.06f, 0.12f), new Vector3(w + 0.24f, 0.12f, 0.12f), metal, false, rot);
            Box(wg, "FrameB", P(0, -h / 2f - 0.06f, 0.14f), new Vector3(w + 0.24f, 0.12f, 0.16f), metal, false, rot);
            foreach (float a in new[] { -w / 2f - 0.06f, w / 2f + 0.06f }) Box(wg, "FrameSide", P(a, 0, 0.12f), new Vector3(0.12f, h + 0.24f, 0.12f), metal, false, rot);
            for (int k = 1; k < 4; k++) Box(wg, "Mullion", P(-w / 2f + k * w / 4f, 0, 0.11f), new Vector3(0.05f, h, 0.06f), metal, false, rot);
            for (int k = 1; k < 3; k++) Box(wg, "Transom", P(0, -h / 2f + k * h / 3f, 0.11f), new Vector3(w, 0.05f, 0.06f), metal, false, rot);
        }
        log.AppendLine($"  loft windows: {windowList.Count} (west, east, south above balcony)");
    }

    // =========================================================================== балкон второго яруса
    static void Balcony()
    {
        var g = Group(root, "Balcony");
        float d = DeckZ - Z0, w = DeckX1 - X0, cx = (X0 + DeckX1) / 2f, cz = (Z0 + DeckZ) / 2f;
        Box(g, "Deck", new Vector3(cx, DeckY - 0.1f, cz), new Vector3(w, 0.2f, d), wood);
        Box(g, "DeckEdge", new Vector3(cx, DeckY - 0.25f, DeckZ - 0.08f), new Vector3(w, 0.3f, 0.16f), metal, false);
        for (float x = X0 + 1.9f; x < DeckX1; x += 3.9f)
            Box(g, "Column", new Vector3(x, (DeckY - 0.4f) / 2f, DeckZ - 0.15f), new Vector3(0.18f, DeckY - 0.4f, 0.18f), metal);
        // перила (проём под лестницу у x = -7.8…-6.8)
        void Rail(float xa, float xb)
        {
            float rw = xb - xa, rx = (xa + xb) / 2f;
            Box(g, "RailTop", new Vector3(rx, DeckY + 1.0f, DeckZ - 0.05f), new Vector3(rw, 0.06f, 0.06f), metal);
            Box(g, "RailMid", new Vector3(rx, DeckY + 0.5f, DeckZ - 0.05f), new Vector3(rw, 0.04f, 0.04f), metal);
            for (float x = xa; x <= xb + 0.01f; x += 1.2f) Box(g, "RailPost", new Vector3(Mathf.Min(x, xb), DeckY + 0.5f, DeckZ - 0.05f), new Vector3(0.05f, 1.0f, 0.05f), metal, false);
        }
        Rail(X0, -7.8f); Rail(-6.8f, DeckX1);
        Box(g, "RailEnd", new Vector3(DeckX1, DeckY + 0.5f, (Z0 + DeckZ) / 2f), new Vector3(0.06f, 1.0f, d), metal);
        // лестница вдоль фронта балкона: снизу у x = -1.0, сверху площадка у x = -7.8…-6.8
        const int steps = 14; float run = 5.8f / steps, rise = DeckY / steps;
        for (int i = 0; i < steps; i++)
        {
            float x = -1.0f - (i + 0.5f) * run, y = (i + 1) * rise;
            Box(g, "Step", new Vector3(x, y - rise / 2f, DeckZ + 0.45f), new Vector3(run + 0.02f, rise, 0.9f), wood);
        }
        Box(g, "Landing", new Vector3(-7.3f, DeckY - 0.1f, DeckZ + 0.45f), new Vector3(1.0f, 0.2f, 0.9f), wood);
        Box(g, "StairStringer", new Vector3(-3.9f, DeckY / 2f - 0.1f, DeckZ + 0.92f), new Vector3(5.9f, 0.12f, 0.06f), metal, false, Quaternion.Euler(0, 0, -Mathf.Atan2(DeckY, 5.8f) * Mathf.Rad2Deg));
        log.AppendLine($"  balcony: deck y {DeckY} over spawn (x {X0}…{DeckX1}, z {Z0}…{DeckZ}), stairs 14 steps, rails");
    }

    // ---- балки, перекладины и уступы для паркура на руках
    static void Parkour()
    {
        var g = Group(root, "Parkour");
        foreach (float z in new[] { -4f, 0f, 4f, 8f, 12f })
            Box(g, "CeilingBeam", new Vector3(0, 6.6f, z), new Vector3(X1 - X0, 0.35f, 0.3f), wood);
        foreach (float x in new[] { -4f, 4f })
            Box(g, "SpineBeam", new Vector3(x, 7.0f, 2f), new Vector3(0.3f, 0.35f, Z1 - Z0), wood);
        // турник с балкона: перекладины к первой балке
        for (int i = 0; i < 5; i++)
            Box(g, "MonkeyBar", new Vector3(-9.5f + i * 0.6f, 4.9f, -5.9f), new Vector3(0.05f, 0.05f, 2.2f), metal);
        Box(g, "MonkeyBarRail", new Vector3(-8.3f, 4.95f, -7.0f), new Vector3(2.6f, 0.06f, 0.06f), metal);
        Box(g, "MonkeyBarRail", new Vector3(-8.3f, 4.95f, -4.8f), new Vector3(2.6f, 0.06f, 0.06f), metal);
        // уступы на восточной стене: от пола к балке (z 1…5)
        for (int i = 0; i < 5; i++)
            Box(g, "WallLedge", new Vector3(X1 - 0.2f, 1.1f + i * 1.05f, 1.2f + i * 0.9f), new Vector3(0.4f, 0.14f, 1.0f), wood);
        // уступы в углу у дивана (северо-запад): к балкам
        for (int i = 0; i < 4; i++)
            Box(g, "WallLedge", new Vector3(X0 + 0.2f, 2.2f + i * 1.1f, 12.6f - i * 1.0f), new Vector3(0.4f, 0.14f, 0.9f), wood);
        log.AppendLine("  parkour: ceiling beams, monkey bars, wall ledges (all BoxCollider, Default)");
    }

    // =========================================================================== 3. зоны
    static void Wardrobe(Transform map)
    {
        var g = Group(root, "Zone_Wardrobe");
        // большое зеркало в деревянной раме (зеркало 2.2 × 2.4 м у западной стены)
        var mirror = map.Find("Dynamic/Mirror");
        if (mirror)
        {
            var c = mirror.position; float w = 2.2f, h = 2.4f, x = X0 + 0.06f;
            Box(g, "MirrorFrameT", new Vector3(x, c.y + h / 2f + 0.08f, c.z), new Vector3(0.12f, 0.16f, w + 0.32f), wood, false);
            Box(g, "MirrorFrameB", new Vector3(x, c.y - h / 2f - 0.08f, c.z), new Vector3(0.16f, 0.16f, w + 0.32f), wood, false);
            foreach (float s in new[] { -1f, 1f }) Box(g, "MirrorFrameSide", new Vector3(x, c.y, c.z + s * (w / 2f + 0.08f)), new Vector3(0.12f, h + 0.32f, 0.16f), wood, false);
            Box(g, "MirrorGlowLine", new Vector3(x + 0.07f, c.y + h / 2f + 0.17f, c.z), new Vector3(0.02f, 0.03f, w + 0.3f), glow, false, null, new Color(1f, 0.7f, 0.4f));
        }
        // полки с масками (над витриной косметики)
        var faceMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Meshes/Cosmetics/Cos_FaceMask_Face.asset");
        var maskMats = new[] { "Black", "White", "Gray" }.Select(k => new[] {
            AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/Materials/Player/M_Cos_Balaclava_{k}.mat"),
            AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/Materials/Player/M_Cos_Balaclava_{k}_Seam.mat") }).ToArray();
        foreach (float y in new[] { 2.55f, 3.15f })
        {
            Box(g, "Shelf", new Vector3(X0 + 0.2f, y, 3.75f), new Vector3(0.36f, 0.05f, 2.4f), wood);
            foreach (float z in new[] { 2.75f, 4.75f }) Box(g, "ShelfBracket", new Vector3(X0 + 0.12f, y - 0.1f, z), new Vector3(0.2f, 0.15f, 0.04f), metal, false);
        }
        if (faceMesh)
            for (int i = 0; i < 3; i++)
            {
                var m = new GameObject("ShelfMask_" + i);
                m.transform.SetParent(g, false);
                m.transform.SetPositionAndRotation(new Vector3(X0 + 0.24f, 2.55f + 0.14f, 3.0f + i * 0.75f), Quaternion.LookRotation(Vector3.right));
                m.transform.localScale = Vector3.one * 0.55f;
                m.AddComponent<MeshFilter>().sharedMesh = faceMesh;
                m.AddComponent<MeshRenderer>().sharedMaterials = maskMats[i];
            }
        Box(g, "Rug", new Vector3(-9.9f, 0.006f, 1.0f), new Vector3(2.6f, 0.012f, 6.4f), concrete, false, null, new Color(0.28f, 0.42f, 0.44f));
        log.AppendLine("  zone wardrobe: mirror wood frame, 2 shelves with 3 balaclavas, rug (mirror, SkinTerminal, showcase kept)");
    }

    static void Planning(Transform map)
    {
        var g = Group(root, "Zone_Planning");
        // GRABZ OS — рядом с командным столом, экраном к игрокам (как стол режимов)
        var pc = map.Find("Dynamic/GrabzComputer");
        if (pc) { pc.SetPositionAndRotation(new Vector3(-3.5f, 0f, 0.95f), Quaternion.Euler(0, 180, 0)); EditorUtility.SetDirty(pc); }
        // стена планирования: тёмное дерево, карта банка и доска рекордов
        float wz = 4.4f, wx0 = -5.4f, wx1 = 1.6f;
        Box(g, "PlanWall", new Vector3((wx0 + wx1) / 2f, 1.6f, wz), new Vector3(wx1 - wx0, 3.2f, 0.2f), wood);
        Box(g, "PlanWallCap", new Vector3((wx0 + wx1) / 2f, 3.24f, wz), new Vector3(wx1 - wx0 + 0.1f, 0.08f, 0.26f), metal, false);
        var face = Quaternion.LookRotation(Vector3.forward);   // текст читается со стороны -Z (от стола)
        // карта банка: тёмная доска, контуры зон неоном
        var mapC = new Vector3(-3.0f, 1.75f, wz - 0.12f);
        Box(g, "BankMapBoard", mapC, new Vector3(2.9f, 1.9f, 0.04f), metal, false);
        void Zone(string label, float x, float y, float w, float h, Color c)
        {
            var p = mapC + new Vector3(x, y, -0.03f);
            Box(g, "MapLine", p + new Vector3(0, h / 2f, 0), new Vector3(w, 0.02f, 0.01f), glow, false, null, c);
            Box(g, "MapLine", p - new Vector3(0, h / 2f, 0), new Vector3(w, 0.02f, 0.01f), glow, false, null, c);
            Box(g, "MapLine", p + new Vector3(w / 2f, 0, 0), new Vector3(0.02f, h, 0.01f), glow, false, null, c);
            Box(g, "MapLine", p - new Vector3(w / 2f, 0, 0), new Vector3(0.02f, h, 0.01f), glow, false, null, c);
            Text(g, "MapLabel", label, 1.0f, c, p + new Vector3(0, 0, -0.005f), face, new Vector2(w - 0.06f, h - 0.06f));
        }
        Text(g, "MapTitle", "КАРТА: SUNSET TRUST BANK", 0.9f, new Color(1f, 0.8f, 0.45f), mapC + new Vector3(0, 0.82f, -0.03f), face, new Vector2(2.7f, 0.2f));
        var warm = new Color(1f, 0.7f, 0.4f); var red = new Color(1f, 0.35f, 0.3f); var teal = new Color(0.4f, 0.9f, 0.85f);
        Zone("УЛИЦА · ВХОД", 0f, -0.62f, 2.5f, 0.3f, teal);
        Zone("ОПЕРАЦИОННЫЙ ЗАЛ", -0.45f, -0.08f, 1.55f, 0.62f, warm);
        Zone("СЛУЖЕБНОЕ КРЫЛО", 0.78f, -0.08f, 0.85f, 0.62f, warm);
        Zone("ПОДВАЛ · ХРАНИЛИЩЕ", -0.45f, 0.5f, 1.55f, 0.38f, red);
        Zone("ГАРАЖ", 0.78f, 0.5f, 0.85f, 0.38f, teal);
        // доска рекордов
        var recC = new Vector3(0.2f, 1.75f, wz - 0.12f);
        Box(g, "RecordsBoard", recC, new Vector3(2.2f, 1.9f, 0.04f), metal, false);
        Box(g, "RecordsGlow", recC + new Vector3(0, 0.95f, -0.02f), new Vector3(2.2f, 0.03f, 0.02f), glow, false, null, warm);
        Text(g, "RecordsTitle", "РЕКОРДЫ БАНДЫ", 1.0f, new Color(1f, 0.8f, 0.45f), recC + new Vector3(0, 0.72f, -0.03f), face, new Vector2(2.0f, 0.22f));
        Text(g, "RecordsBody", "ЛУЧШИЙ КУШ ........ —\nБЫСТРЕЙШЕЕ ДЕЛО ... —\nХАРДКОР БЕЗ ТРЕВОГИ — —\n\n<size=70%>ПРИВЕЗИ ДОБЫЧУ — ВПИШЕМ!</size>", 0.75f, Color.white, recC + new Vector3(0, -0.1f, -0.03f), face, new Vector2(2.0f, 1.3f), false);
        // табличка стола режимов — на верх стены
        var sign = map.Find("Static/ModeSign")?.GetComponent<TextMeshPro>();
        if (sign)
        {
            var rt = sign.rectTransform; var p = new Vector3(-1.9f, 2.95f, wz - 0.12f);
            rt.anchoredPosition3D = p; rt.localPosition = p; rt.rotation = face; rt.sizeDelta = new Vector2(6.4f, 0.32f);
            EditorUtility.SetDirty(sign);
        }
        Box(g, "Rug", new Vector3(-1.0f, 0.006f, 0.5f), new Vector3(7.0f, 0.012f, 3.6f), concrete, false, null, new Color(0.48f, 0.2f, 0.16f));
        log.AppendLine("  zone planning: GRABZ OS moved next to the command table, plan wall with bank map + records board");
    }

    static void ExitZone(Transform map)
    {
        var g = Group(root, "Zone_Exit");
        var bus = GameObject.Find("BlackBus")?.transform;
        float cx = bus ? bus.position.x : 4.2f;
        // гаражные ворота: короб поднятой рольставни над въездом, жёлто-чёрная разметка на полу
        Box(g, "ShutterBox", new Vector3(cx, 4.65f, Z1 - 0.3f), new Vector3(6.6f, 0.55f, 0.55f), metal, false);
        Box(g, "ShutterGuideL", new Vector3(cx - 3.15f, 2.2f, Z1 - 0.12f), new Vector3(0.12f, 4.4f, 0.2f), metal, false);
        Box(g, "ShutterGuideR", new Vector3(cx + 3.15f, 2.2f, Z1 - 0.12f), new Vector3(0.12f, 4.4f, 0.2f), metal, false);
        var yellow = new Color(1f, 0.78f, 0.15f) * 0.75f;
        for (int i = 0; i < 12; i++)
            Box(g, "HazardStripe", new Vector3(cx - 2.75f + i * 0.5f, 0.004f, Z1 - 0.5f), new Vector3(0.25f, 0.008f, 0.5f), concrete, false, Quaternion.Euler(0, 35, 0), yellow);
        // парковка автобуса: жёлтая рамка вокруг кузова (под днищем салона — ниже его пола)
        float x0 = cx - 1.75f, x1 = cx + 1.75f, z0 = 6.6f, z1 = 13.2f;
        Box(g, "ParkLine", new Vector3(x0, 0.004f, (z0 + z1) / 2f), new Vector3(0.12f, 0.008f, z1 - z0), concrete, false, null, yellow);
        Box(g, "ParkLine", new Vector3(x1, 0.004f, (z0 + z1) / 2f), new Vector3(0.12f, 0.008f, z1 - z0), concrete, false, null, yellow);
        Box(g, "ParkLine", new Vector3(cx, 0.004f, z0), new Vector3(x1 - x0, 0.008f, 0.12f), concrete, false, null, yellow);
        // дорожка от спавна к автобусу
        for (int i = 0; i < 7; i++) Box(g, "WalkDash", new Vector3(cx - 0.6f, 0.004f, -3.8f + i * 1.45f), new Vector3(0.12f, 0.008f, 0.7f), concrete, false, null, yellow);
        log.AppendLine("  zone exit: garage gate (shutter box + guides), hazard stripes, bus parking frame, walk line");
    }

    static void RestZone()
    {
        var g = Group(root, "Zone_Rest");
        // стилизованный диван из геометрических подушек вдоль северной стены (вне коридора автобуса)
        float sx = -7.6f, sz = 12.9f, len = 3.6f;
        Box(g, "SofaBase", new Vector3(sx, 0.22f, sz), new Vector3(len + 0.3f, 0.44f, 1.0f), wood);
        var cushion = new Color(0.36f, 0.62f, 0.66f);
        for (int i = 0; i < 3; i++)
        {
            float x = sx - len / 2f + (i + 0.5f) * len / 3f;
            Box(g, "SeatCushion", new Vector3(x, 0.55f, sz - 0.05f), new Vector3(len / 3f - 0.06f, 0.22f, 0.86f), concrete, false, null, cushion);
            Box(g, "BackCushion", new Vector3(x, 0.92f, sz + 0.36f), new Vector3(len / 3f - 0.06f, 0.55f, 0.22f), concrete, false, null, cushion * 0.85f);
        }
        foreach (float s in new[] { -1f, 1f }) Box(g, "Armrest", new Vector3(sx + s * (len / 2f + 0.12f), 0.62f, sz), new Vector3(0.22f, 0.44f, 1.0f), wood);
        Box(g, "Rug", new Vector3(sx, 0.006f, 10.6f), new Vector3(4.6f, 0.012f, 3.2f), concrete, false, null, new Color(0.5f, 0.24f, 0.17f));
        Box(g, "CoffeeTable", new Vector3(sx, 0.38f, 10.7f), new Vector3(1.6f, 0.06f, 0.8f), wood);
        foreach (float a in new[] { -0.7f, 0.7f }) foreach (float b in new[] { -0.32f, 0.32f })
            Box(g, "TableLeg", new Vector3(sx + a, 0.18f, 10.7f + b), new Vector3(0.06f, 0.36f, 0.06f), metal, false);
        // торшер: чёрная стойка + тёплый плафон
        Box(g, "FloorLampPole", new Vector3(-10.3f, 0.9f, 12.9f), new Vector3(0.05f, 1.8f, 0.05f), metal, false);
        Box(g, "FloorLampShade", new Vector3(-10.3f, 1.85f, 12.9f), new Vector3(0.36f, 0.3f, 0.36f), glow, false, null, new Color(1f, 0.72f, 0.42f));
        log.AppendLine("  zone rest: geometric sofa (3 seat + 3 back cushions), rug, coffee table, floor lamp");
    }

    static void Basketball(Transform map)
    {
        var g = Group(root, "Zone_Basketball");
        var trig = map.Find("Dynamic/HoopScoreTrigger");
        var tb = trig ? trig.GetComponent<BoxCollider>() : null;
        Vector3 rim = trig ? trig.TransformPoint(tb.center + Vector3.up * tb.size.y * 0.5f) : new Vector3(9.8f, 2.6f, -6f);
        float ringR = 0.24f;
        // щит и стойка — матовый чёрный металл; кольцо — тёплый неон
        Box(g, "Backboard", new Vector3(rim.x + ringR + 0.38f, rim.y + 0.45f, rim.z), new Vector3(0.05f, 1.05f, 1.8f), metal);
        Box(g, "BackboardSquare", new Vector3(rim.x + ringR + 0.35f, rim.y + 0.25f, rim.z), new Vector3(0.01f, 0.45f, 0.6f), glow, false, null, new Color(1f, 0.6f, 0.3f));
        Box(g, "HoopArm", new Vector3((rim.x + ringR + X1) / 2f + 0.2f, rim.y + 0.15f, rim.z), new Vector3(X1 - rim.x - ringR - 0.4f, 0.08f, 0.08f), metal, false);
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI * 2 / 16;
            Box(g, "Rim", rim + new Vector3(Mathf.Cos(a) * ringR, 0, Mathf.Sin(a) * ringR), new Vector3(0.1f, 0.025f, 0.025f), glow, false, Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90, 0), new Color(1f, 0.45f, 0.15f));
        }
        // разметка площадки
        var paint = new Color(1f, 0.78f, 0.15f) * 0.75f;
        Box(g, "FreeThrow", new Vector3(rim.x - 2.6f, 0.004f, rim.z), new Vector3(0.1f, 0.008f, 2.4f), concrete, false, null, paint);
        Box(g, "LaneL", new Vector3((rim.x - 2.6f + X1) / 2f, 0.004f, rim.z - 1.2f), new Vector3(X1 - rim.x + 2.6f, 0.008f, 0.1f), concrete, false, null, paint);
        Box(g, "LaneR", new Vector3((rim.x - 2.6f + X1) / 2f, 0.004f, rim.z + 1.2f), new Vector3(X1 - rim.x + 2.6f, 0.008f, 0.1f), concrete, false, null, paint);
        // стойка для мяча (там же, где точка возврата мяча)
        var resp = map.Find("Dynamic/BallRespawn");
        var bp = resp ? resp.position : new Vector3(8.2f, 1.1f, -8.6f);
        Box(g, "BallStand", new Vector3(bp.x, (bp.y - 0.15f) / 2f, bp.z), new Vector3(0.12f, bp.y - 0.15f, 0.12f), metal);
        Box(g, "BallCup", new Vector3(bp.x, bp.y - 0.14f, bp.z), new Vector3(0.34f, 0.04f, 0.34f), metal);
        log.AppendLine($"  zone basketball: clean backboard + neon rim at {rim:F2}, court lines, ball stand");
    }

    // =========================================================================== 4. свет и Post-FX
    static void Lighting(Transform map)
    {
        var g = Group(root, "Lighting");
        // тёплые лампы над каждой зоной (без теней)
        PointLamp(g, "Lamp_Wardrobe", new Vector3(-9.6f, 4.4f, 1.2f), 2.4f, 9f);
        PointLamp(g, "Lamp_Planning", new Vector3(-1.4f, 4.6f, 1.6f), 2.6f, 10f);
        PointLamp(g, "Lamp_Exit", new Vector3(1.6f, 5.0f, 6.0f), 2.2f, 9f);
        PointLamp(g, "Lamp_Rest", new Vector3(-7.6f, 4.4f, 11.0f), 2.2f, 9f);
        PointLamp(g, "Lamp_Basketball", new Vector3(8.4f, 4.8f, -5.2f), 2.4f, 10f);
        // под балконом (спавн) — две плоские потолочные лампы без шнура
        foreach (float x in new[] { -4.0f, 3.0f })
        {
            Box(g, "DeckLampPanel", new Vector3(x, DeckY - 0.23f, -8.4f), new Vector3(0.8f, 0.03f, 0.3f), glow, false, null, new Color(1f, 0.8f, 0.55f));
            var l = new GameObject("DeckLight").AddComponent<Light>(); l.transform.SetParent(g, false); l.transform.position = new Vector3(x, DeckY - 0.4f, -8.4f);
            l.type = LightType.Point; l.color = new Color(1f, 0.78f, 0.52f); l.intensity = 1.5f; l.range = 6.5f; l.shadows = LightShadows.None;
        }
        // мягкий закатный свет из лофт-окон: широкие споты с плавным спадом, по одному на стену
        foreach (var (c, inw, w, h) in new[] { windowList[1], windowList[4], windowList[7] })
        {
            var l = new GameObject("WindowSun").AddComponent<Light>(); l.transform.SetParent(g, false);
            l.transform.SetPositionAndRotation(c + inw * 0.6f, Quaternion.LookRotation(inw + Vector3.down * 0.9f));
            l.type = LightType.Spot; l.color = new Color(1f, 0.66f, 0.42f); l.intensity = 1.7f; l.range = 13f; l.spotAngle = 100f; l.innerSpotAngle = 25f; l.shadows = LightShadows.None;
        }
        // солнце: тёплый низкий закат, мягко
        var sun = Object.FindObjectsByType<Light>(FindObjectsInactive.Include).FirstOrDefault(x => x.type == LightType.Directional);
        if (sun)
        {
            sun.color = new Color(1f, 0.72f, 0.5f); sun.intensity = 0.35f; sun.shadows = LightShadows.None;
            sun.transform.rotation = Quaternion.Euler(28f, 115f, 0f);
            EditorUtility.SetDirty(sun);
        }
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.52f, 0.47f, 0.47f);
        RenderSettings.ambientEquatorColor = new Color(0.44f, 0.36f, 0.3f);
        RenderSettings.ambientGroundColor = new Color(0.24f, 0.2f, 0.17f);
        RenderSettings.fog = false;
        log.AppendLine("  lighting: 5 zone pendants + 2 under-deck lights + 3 soft window spots, warm low sun, trilight ambient, no shadows");
    }

    /// Мягкая картинка без «вырвиглазного» Bloom: Bloom выключен, лёгкая тёплая цветокоррекция остаётся.
    static void SoftenPostFX(Transform fx)
    {
        var vol = fx ? fx.GetComponent<Volume>() : null;
        var profile = vol ? vol.sharedProfile : null;
        if (!profile) { log.AppendLine("  post-fx: no volume"); return; }
        if (profile.TryGet<Bloom>(out var bloom)) { bloom.active = false; }
        EditorUtility.SetDirty(profile);
        log.AppendLine($"  post-fx: '{profile.name}' — Bloom off (90 FPS on Quest)");
    }

    // =========================================================================== перекраска уцелевшего в 5 материалов
    static void Restyle(Transform map, UnityEngine.SceneManagement.Scene scene)
    {
        var toWood = new HashSet<string> { "M_WoodDark", "M_WoodLight", "M_Hub_Wood", "M_Hub_WoodDark" };
        var toMetal = new HashSet<string> { "M_DarkMetal", "M_Metal", "M_Hub_RustMetal", "M_Hub_RustMetalDark", "M_Hub_SteelWorn", "M_MarbleDark", "M_Brass", "M_Chrome", "M_Tunnel_Concrete", "M_Tunnel_ConcreteDark" };
        var toBrick = new HashSet<string> { "M_Brick", "M_Hub_Brick" };
        var toConcrete = new HashSet<string> { "M_ConcreteDark", "M_Hub_ConcreteFloor", "M_Hub_ConcreteCeiling" };
        int n = 0;
        var targets = new List<Renderer>();
        targets.AddRange(map.GetComponentsInChildren<Renderer>(true));
        var tunnel = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "BusTunnel");
        if (tunnel)
            foreach (var part in new[] { "NorthWallWithMouth", "Portal" })
                if (tunnel.transform.Find(part)) targets.AddRange(tunnel.transform.Find(part).GetComponentsInChildren<Renderer>(true));
        foreach (var r in targets)
        {
            if (r is SkinnedMeshRenderer || r.GetComponent<TMP_Text>()) continue;
            var mats = r.sharedMaterials; bool ch = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!mats[i]) continue;
                string mn = mats[i].name;
                Material to = toWood.Contains(mn) ? wood : toMetal.Contains(mn) ? metal : toBrick.Contains(mn) ? brick : toConcrete.Contains(mn) ? concrete : null;
                if (to) { mats[i] = to; ch = true; }
            }
            if (ch)
            {
                r.sharedMaterials = mats; n++;
                var mf = r.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh && (mf.sharedMesh.name == "Cube" || mf.sharedMesh.name.StartsWith("HubBox_"))) mf.sharedMesh = HubVisualOverhaul.BoxMesh(r.transform.lossyScale, 1f);
            }
        }
        log.AppendLine($"  restyled {n} kept renderers (desks, showcase, signs, garage gate) into the 5-material palette");
    }

    // =========================================================================== снимки
    public static void Capture()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "claude", "loft_preview");
        System.IO.Directory.CreateDirectory(dir);
        var shots = new (string n, Vector3 pos, Vector3 look)[]
        {
            ("1_spawn", new Vector3(0f, 1.6f, -8.0f), new Vector3(1f, 1.4f, 6f)),
            ("2_wardrobe", new Vector3(-6.5f, 1.7f, 1.0f), new Vector3(-11.8f, 1.6f, 1.8f)),
            ("3_planning", new Vector3(-1.2f, 1.75f, -2.6f), new Vector3(-1.5f, 1.3f, 4.4f)),
            ("4_exit", new Vector3(0.5f, 2.0f, 2.5f), new Vector3(4.2f, 1.6f, 12f)),
            ("5_rest_balcony", new Vector3(2.5f, 2.4f, 4.5f), new Vector3(-8.5f, 1.5f, 11.5f)),
            ("6_overview", new Vector3(6.5f, 4.6f, -8.6f), new Vector3(-3f, 1.2f, 6f)),
        };
        var go = new GameObject("LoftCam");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 75f; cam.nearClipPlane = 0.03f; cam.farClipPlane = 120f; cam.allowHDR = true; cam.cullingMask = ~(1 << 9);
        go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        foreach (var s in shots)
        {
            go.transform.SetPositionAndRotation(s.pos, Quaternion.LookRotation(s.look - s.pos));
            cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, s.n + ".png"), tex.EncodeToPNG());
        }
        RenderTexture.active = null; cam.targetTexture = null;
        Object.DestroyImmediate(go); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
    }
}
