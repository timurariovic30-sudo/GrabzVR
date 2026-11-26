using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Инструменты генератора карты: материалы, процедурные текстуры и меши, примитивы, стены с проёмами, тексты, свет.
/// </summary>
public static class MapKit
{
    public const string MatDir = "Assets/Art/Materials/Map";
    public const string TexDir = "Assets/Art/Textures/Map";
    public const string MeshDir = "Assets/Art/Models/Generated";
    public const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    public const float WallT = 0.3f;

    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    public static TMP_FontAsset Font;
    public static Mesh ConeMesh, GemMesh;
    public static int IgnoreRaycast => LayerMask.NameToLayer("Ignore Raycast");

    // ================================================================== materials

    public static Material M(string name)
    {
        if (mats.TryGetValue(name, out var m) && m) return m;
        throw new Exception("Material not initialised: " + name);
    }

    public static void Init()
    {
        EnsureFolder(MatDir); EnsureFolder(TexDir); EnsureFolder(MeshDir);
        mats.Clear();
        Font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);

        // Окружение
        Lit("Marble", C(0.86f, 0.84f, 0.80f), 0f, 0.6f);
        Lit("MarbleDark", C(0.22f, 0.23f, 0.25f), 0f, 0.6f);
        Lit("Concrete", C(0.45f, 0.45f, 0.46f), 0f, 0.08f);
        Lit("ConcreteDark", C(0.26f, 0.26f, 0.27f), 0f, 0.08f);
        Lit("Plaster", C(0.86f, 0.81f, 0.72f), 0f, 0.12f);
        Lit("PlasterCool", C(0.80f, 0.83f, 0.87f), 0f, 0.12f);
        Lit("WoodLight", C(0.62f, 0.44f, 0.27f), 0f, 0.3f);
        Lit("WoodDark", C(0.30f, 0.18f, 0.10f), 0f, 0.35f);
        Lit("CarpetRed", C(0.42f, 0.06f, 0.08f), 0f, 0f);
        Lit("CarpetBlue", C(0.10f, 0.15f, 0.32f), 0f, 0f);
        Lit("Metal", C(0.56f, 0.58f, 0.61f), 0.9f, 0.5f);
        Lit("DarkMetal", C(0.15f, 0.16f, 0.18f), 0.85f, 0.45f);
        Lit("Chrome", C(0.86f, 0.87f, 0.9f), 1f, 0.88f);
        Lit("Brass", C(0.80f, 0.60f, 0.25f), 1f, 0.6f);
        Lit("Leather", C(0.33f, 0.11f, 0.07f), 0f, 0.35f);
        Lit("Fabric", C(0.18f, 0.27f, 0.36f), 0f, 0f);
        Lit("Foliage", C(0.14f, 0.38f, 0.16f), 0f, 0.2f);
        Lit("Rubber", C(0.05f, 0.05f, 0.06f), 0f, 0.3f);
        Lit("VanBody", C(0.90f, 0.90f, 0.92f), 0.2f, 0.65f);
        Lit("VanStripe", C(0.10f, 0.22f, 0.50f), 0.2f, 0.6f);
        Lit("Pad", C(0.10f, 0.50f, 1.00f), 0f, 0.4f);
        Lit("Velvet", C(0.40f, 0.04f, 0.12f), 0f, 0f, emissionKeyword: true);
        Lit("Cork", C(0.70f, 0.55f, 0.36f), 0f, 0.05f);
        Lit("Paper", C(0.95f, 0.94f, 0.90f), 0f, 0.1f);
        Lit("PaintYellow", C(1.00f, 0.78f, 0.10f), 0f, 0.2f);
        Lit("BarrelRed", C(0.65f, 0.10f, 0.08f), 0.3f, 0.4f);
        Emissive("Screen", C(0.20f, 0.75f, 1.00f), 1.5f);
        Emissive("LampWarm", C(1.00f, 0.80f, 0.50f), 3f);
        Emissive("LampCold", C(0.85f, 0.92f, 1.00f), 3f);
        Emissive("LampSodium", C(1.00f, 0.62f, 0.28f), 3f);
        Emissive("Neon", C(0.25f, 1.00f, 0.15f), 3f);
        Emissive("LaserRed", C(1.00f, 0.05f, 0.04f), 4f);
        Lit("Beacon", C(0.6f, 0.05f, 0.03f), 0f, 0.6f, emissionKeyword: true);
        Transparent("Glass", new Color(0.75f, 0.90f, 1.00f, 0.18f), 0.95f);
        Transparent("ConeView", new Color(0.2f, 1f, 0.3f, 0.12f), 0f, unlit: true); // Cull Back: изнутри конус не виден

        // Лут (у всех включена эмиссия — для подсветки «до первого взятия»)
        Lit("GoldGlow", C(1.00f, 0.77f, 0.22f), 1f, 0.78f, emissionKeyword: true);
        Lit("Porcelain", C(0.95f, 0.95f, 0.93f), 0f, 0.85f, emissionKeyword: true);
        Lit("PorcelainBlue", C(0.12f, 0.22f, 0.65f), 0f, 0.85f, emissionKeyword: true);
        Lit("SafeSteel", C(0.30f, 0.32f, 0.35f), 0.85f, 0.5f, emissionKeyword: true);
        Lit("GemRed", C(0.85f, 0.04f, 0.10f), 0f, 0.95f, emissionKeyword: true);
        Lit("GemBlue", C(0.10f, 0.30f, 0.95f), 0f, 0.95f, emissionKeyword: true);
        var diamond = Transparent("GemDiamond", new Color(0.75f, 0.95f, 1.00f, 0.6f), 1f, cullOff: true); // двусторонний — грани видны всегда
        diamond.EnableKeyword("_EMISSION"); diamond.SetColor("_EmissionColor", C(0.15f, 0.35f, 0.45f));

        // Текстурные
        var hazard = Lit("Hazard", Color.white, 0f, 0.3f);
        hazard.SetTexture("_BaseMap", HazardTexture());
        var blueprint = Lit("Blueprint", Color.white, 0f, 0.1f);
        blueprint.SetTexture("_BaseMap", BlueprintTexture());
        for (int i = 1; i <= 3; i++)
        {
            var art = Lit("Art" + i, Color.white, 0f, 0.15f, emissionKeyword: true);
            art.SetTexture("_BaseMap", ArtTexture(i));
        }

        foreach (var m in mats.Values) EditorUtility.SetDirty(m);
        BuildConeMesh();
        BuildGemMesh();
        AssetDatabase.SaveAssets();
        // Переимпорт и повторная загрузка: иначе префабы, сохранённые в этом же проходе, могут получить «пустую» ссылку на меш
        foreach (var n in new[] { "ViewCone", "Gem" }) AssetDatabase.ImportAsset($"{MeshDir}/{n}.asset", ImportAssetOptions.ForceUpdate);
        ConeMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{MeshDir}/ViewCone.asset");
        GemMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{MeshDir}/Gem.asset");
    }

    static Color C(float r, float g, float b) => new Color(r, g, b);

    static Material GetOrCreate(string name, Shader shader)
    {
        string path = $"{MatDir}/M_{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        mats[name] = m;
        return m;
    }

    static Material Lit(string name, Color c, float metallic, float smooth, bool emissionKeyword = false)
    {
        var m = GetOrCreate(name, Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smooth);
        if (emissionKeyword) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        else m.DisableKeyword("_EMISSION");
        return m;
    }

    static Material Emissive(string name, Color c, float intensity)
    {
        var m = Lit(name, c, 0f, 0.5f, emissionKeyword: true);
        m.SetColor("_EmissionColor", c * intensity);
        return m;
    }

    static Material Transparent(string name, Color c, float smooth, bool unlit = false, bool cullOff = false)
    {
        var m = GetOrCreate(name, Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        if (!unlit) { m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f); }
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", cullOff ? 0f : 2f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)RenderQueue.Transparent;
        return m;
    }

    // ================================================================== textures

    static Texture2D SaveTex(string name, Texture2D t, bool repeat = true)
    {
        string path = $"{TexDir}/{name}.png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        ti.mipmapEnabled = true;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Texture2D HazardTexture()
    {
        const int N = 128;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var yellow = new Color(1f, 0.78f, 0.1f); var black = new Color(0.08f, 0.08f, 0.08f);
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            t.SetPixel(x, y, ((x + y) / 32) % 2 == 0 ? yellow : black);
        t.Apply();
        return SaveTex("T_Hazard", t);
    }

    static Texture2D BlueprintTexture()
    {
        const int N = 256;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var bg = new Color(0.10f, 0.28f, 0.62f); var grid = new Color(0.25f, 0.45f, 0.80f); var line = new Color(0.92f, 0.96f, 1f);
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
            t.SetPixel(x, y, (x % 16 == 0 || y % 16 == 0) ? grid : bg);
        // «План здания»: прямоугольники комнат и пунктир маршрута
        void Rect(int x0, int y0, int x1, int y1) { for (int x = x0; x <= x1; x++) { t.SetPixel(x, y0, line); t.SetPixel(x, y1, line); } for (int y = y0; y <= y1; y++) { t.SetPixel(x0, y, line); t.SetPixel(x1, y, line); } }
        Rect(20, 20, 236, 236); Rect(20, 20, 100, 120); Rect(100, 20, 180, 140); Rect(180, 20, 236, 140); Rect(60, 140, 180, 236); Rect(180, 140, 236, 236);
        for (int i = 0; i < 160; i += 6) { int x = 40 + i, y = 60 + i / 2; t.SetPixel(x, y, Color.red); t.SetPixel(x + 1, y, Color.red); t.SetPixel(x, y + 1, Color.red); }
        // крестик «цель»
        for (int i = -8; i <= 8; i++) { t.SetPixel(120 + i, 190 + i, Color.red); t.SetPixel(120 + i, 190 - i, Color.red); }
        t.Apply();
        return SaveTex("T_Blueprint", t);
    }

    /// Оригинальные абстрактные «картины» (круги, полосы, градиенты), разные для seed 1..3.
    static Texture2D ArtTexture(int seed)
    {
        const int N = 256;
        var rnd = new System.Random(seed * 7919);
        float R() => (float)rnd.NextDouble();
        var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        Color c0 = Color.HSVToRGB(R(), 0.5f, 0.9f), c1 = Color.HSVToRGB(R(), 0.6f, 0.5f);
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) t.SetPixel(x, y, Color.Lerp(c0, c1, y / (float)N));
        int shapes = 6 + seed * 2;
        for (int s = 0; s < shapes; s++)
        {
            var col = Color.HSVToRGB(R(), 0.7f + 0.3f * R(), 0.6f + 0.4f * R());
            int cx = (int)(R() * N), cy = (int)(R() * N), r = 12 + (int)(R() * 50);
            bool circle = R() > 0.45f;
            for (int y = Math.Max(0, cy - r); y < Math.Min(N, cy + r); y++)
            for (int x = Math.Max(0, cx - r); x < Math.Min(N, cx + r); x++)
                if (!circle || (x - cx) * (x - cx) + (y - cy) * (y - cy) < r * r) t.SetPixel(x, y, col);
        }
        t.Apply();
        return SaveTex("T_Art" + seed, t, repeat: false);
    }

    // ================================================================== meshes

    static Mesh SaveMesh(Mesh m)
    {
        string path = $"{MeshDir}/{m.name}.asset";
        // Обновляем существующий ассет на месте: удаление+создание по тому же пути рвёт ссылки в префабах
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(m, existing);
            UnityEngine.Object.DestroyImmediate(m);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// Конус обзора камеры: вершина в (0,0,0), основание радиуса 1 на z = 1.
    static Mesh BuildConeMesh()
    {
        const int seg = 20;
        var v = new List<Vector3> { Vector3.zero };
        for (int i = 0; i <= seg; i++) { float a = i * Mathf.PI * 2 / seg; v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 1f)); }
        var tris = new List<int>();
        for (int i = 1; i <= seg; i++) tris.AddRange(new[] { 0, i + 1, i }); // нормали наружу
        var m = new Mesh { name = "ViewCone" };
        m.SetVertices(v); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return SaveMesh(m);
    }

    /// Огранённый бриллиант (low-poly, плоские грани): площадка сверху, рундист, шип снизу.
    static Mesh BuildGemMesh()
    {
        const int seg = 8;
        var ring = new List<Vector3>(); var table = new List<Vector3>();
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2 / seg;
            ring.Add(new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a)));
            table.Add(new Vector3(Mathf.Cos(a + Mathf.PI / seg) * 0.55f, 0.45f, Mathf.Sin(a + Mathf.PI / seg) * 0.55f));
        }
        var tip = new Vector3(0, -0.9f, 0); var top = new Vector3(0, 0.45f, 0);
        var verts = new List<Vector3>(); var tris = new List<int>();
        void Tri(Vector3 a, Vector3 b, Vector3 c) { int k = verts.Count; verts.Add(a); verts.Add(b); verts.Add(c); tris.AddRange(new[] { k, k + 1, k + 2 }); }
        for (int i = 0; i < seg; i++)
        {
            int j = (i + 1) % seg;
            Tri(ring[j], ring[i], tip);                 // низ (павильон)
            Tri(ring[i], ring[j], table[i]);            // корона
            Tri(table[i], ring[j], table[j]);
            Tri(top, table[i], table[j]);               // площадка
        }
        var m = new Mesh { name = "Gem" };
        m.SetVertices(verts); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return SaveMesh(m);
    }

    // ================================================================== primitives

    public static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);
        return go.transform;
    }

    /// Куб в ЛОКАЛЬНЫХ координатах родителя.
    public static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, string mat, bool collider = true, Quaternion? rot = null)
        => Prim(PrimitiveType.Cube, parent, name, pos, size, mat, collider, rot);

    /// Цилиндр: radius, height; по умолчанию вертикальный.
    public static GameObject Cyl(Transform parent, string name, Vector3 pos, float radius, float height, string mat, bool collider = true, Quaternion? rot = null)
        => Prim(PrimitiveType.Cylinder, parent, name, pos, new Vector3(radius * 2, height / 2, radius * 2), mat, collider, rot);

    public static GameObject Sph(Transform parent, string name, Vector3 pos, Vector3 size, string mat, bool collider = false)
        => Prim(PrimitiveType.Sphere, parent, name, pos, size, mat, collider, null);

    public static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, string mat, bool collider, Quaternion? rot)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot ?? Quaternion.identity;
        go.transform.localScale = scale;
        if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = M(mat);
        go.layer = parent ? parent.gameObject.layer : 0;
        return go;
    }

    public static GameObject MeshObj(Transform parent, string name, Mesh mesh, Vector3 pos, Vector3 scale, string mat, Quaternion? rot = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localRotation = rot ?? Quaternion.identity;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = M(mat);
        go.layer = parent ? parent.gameObject.layer : 0;
        return go;
    }

    /// Проём в стене: center — координата вдоль стены (мировая X или Z), width, bottom/top — высоты.
    public struct Opening { public float center, width, bottom, top; public Opening(float c, float w, float b, float t) { center = c; width = w; bottom = b; top = t; } }

    /// Стена по оси X или Z (мировые координаты, родитель без поворота/масштаба) с прямоугольными проёмами.
    public static void Wall(Transform parent, string name, float x0, float z0, float x1, float z1, float y0, float y1, string mat, params Opening[] openings)
    {
        bool alongX = Mathf.Abs(x1 - x0) > Mathf.Abs(z1 - z0);
        float a0 = alongX ? Mathf.Min(x0, x1) : Mathf.Min(z0, z1);
        float a1 = alongX ? Mathf.Max(x0, x1) : Mathf.Max(z0, z1);
        float fixedC = alongX ? z0 : x0;
        var ops = openings.OrderBy(o => o.center).ToList();

        int k = 0;
        void Seg(float s0, float s1, float b, float t)
        {
            if (s1 - s0 < 0.01f || t - b < 0.01f) return;
            float c = (s0 + s1) / 2, len = s1 - s0, h = t - b;
            var pos = alongX ? new Vector3(c, b + h / 2, fixedC) : new Vector3(fixedC, b + h / 2, c);
            var size = alongX ? new Vector3(len, h, WallT) : new Vector3(WallT, h, len);
            Box(parent, $"{name}_{k++}", pos, size, mat);
        }

        float cur = a0 - (alongX ? 0 : 0);
        foreach (var o in ops)
        {
            float l = o.center - o.width / 2, r = o.center + o.width / 2;
            Seg(cur, l, y0, y1);
            Seg(l, r, y0, o.bottom);
            Seg(l, r, o.top, y1);
            cur = r;
        }
        Seg(cur, a1, y0, y1);
    }

    // ================================================================== text & light

    public static TextMeshPro Text(Transform parent, string name, string text, float size, Color color, Vector3 pos, Quaternion rot, Vector2 rect, TextAlignmentOptions align = TextAlignmentOptions.Center, bool bold = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshPro>();
        var rt = tmp.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = rect;
        // У RectTransform в сцену сохраняется anchoredPosition, а не localPosition: если задать только
        // localPosition, после перезагрузки сцены X/Y сбрасываются в 0 и надписи «падают» на пол.
        // +1 см к зрителю (TMP читается со стороны -forward): текст не утоплен в табличку → нет z-fighting
        // и «двоения» в стерео, когда каждый глаз видит табличку под своим углом
        pos += rot * Vector3.back * 0.01f;
        rt.anchoredPosition3D = pos;
        rt.localPosition = pos;
        go.transform.localRotation = rot;
        tmp.font = Font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.1f; tmp.fontSizeMax = size;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.alignment = align;
        tmp.color = color;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        return tmp;
    }

    /// Поворот текста так, чтобы его лицевая сторона смотрела в направлении facing (мировой вектор).
    public static Quaternion Facing(Vector3 facing) => Quaternion.LookRotation(-facing, Vector3.up);

    public static Light PointLight(Transform parent, string name, Vector3 pos, Color color, float intensity, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point; l.color = color; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
        return l;
    }

    public static Light SpotLight(Transform parent, string name, Vector3 pos, Vector3 lookAt, Color color, float intensity, float range, float angle)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.rotation = Quaternion.LookRotation(lookAt - pos);
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot; l.color = color; l.intensity = intensity; l.range = range; l.spotAngle = angle; l.innerSpotAngle = angle * 0.6f; l.shadows = LightShadows.None;
        return l;
    }

    // ================================================================== misc

    public static void MarkStatic(Transform root)
    {
        var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
    }

    public static void SetLayer(GameObject go, int layer)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    public static AudioClip[] Clips(string dir, params string[] prefixes) =>
        AssetDatabase.FindAssets("t:AudioClip", new[] { dir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => prefixes.Any(pr => Path.GetFileNameWithoutExtension(p).StartsWith(pr)))
            .OrderBy(p => p).Select(AssetDatabase.LoadAssetAtPath<AudioClip>).ToArray();

    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
