using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fusion;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Визуальный и геймплейный апгрейд карты Банка (HeistLevel_Test) в стиле Gorilla Tag / PS1 — ПОВЕРХ существующей сцены
/// (HeistMapBuilder НЕ вызывается, сетевые объекты, лут, лазеры, камеры и кнопки остаются на месте):
///  1) ретро-текстуры (BankRetroTextures) → отдельные материалы M_Bank_* по зонам: мрамор в Холле, обои и панели в Офисах,
///     бронеплиты в Хранилище, асфальт и бетон в Гараже, паркет в Галерее; общие M_*.mat не меняются.
///     У примитивов UV пересчитываются в метры (тайлинг без растяжения). С обратной стороны общих стен — тонкая облицовка,
///     чтобы из соседней зоны не было видно «чужой» текстуры;
///  2) Tripo-модели Bank_* (Assets/Art/GeneratedModels/Bank): дверь хранилища, стойка ресепшена, броневик эвакуации,
///     золотая статуя основателя — новый сетевой лут за $1500 (Loot_GoldenStatue);
///  3) свет по зонам (тёплый Холл, холодная Галерея, красно-синие Хранилище и Гараж) без лишних источников
///     + лёгкий Bloom (четверть разрешения) и цветокоррекция на камерах игроков.
/// Всё новое — в корне сцены «BankDecor» (повторный запуск пересоздаёт только его; идемпотентно).
/// Меню: Tools/Heist/Bank/Visual Overhaul. Batchmode: -executeMethod BankVisualOverhaul.RunBatch
/// </summary>
public static class BankVisualOverhaul
{
    const string ScenePath = "Assets/Scenes/HeistLevel_Test.unity";
    const string MatDir = "Assets/Art/Materials/Bank";
    const string Gen = "Assets/Art/GeneratedModels/Bank";
    const string MeshDir = Gen + "/UVMeshes";
    const string ProfilePath = MatDir + "/BankPostFXProfile.asset";
    const string StatueLootPath = Gen + "/Loot_GoldenStatue.prefab";
    const string DecorRoot = "BankDecor";
    static readonly string[] TripoNames = { "Bank_VaultDoor", "Bank_GoldenStatue", "Bank_ReceptionDesk", "Bank_GetawayTruck" };

    static readonly StringBuilder log = new StringBuilder();
    static Material marble, marbleDark, limestone, plaster, plasterCool, wallpaper, woodPanel, carpet, armor, armorLight, tread,
                    asphalt, concrete, concreteCeil, parquet, paint, stripRed, stripBlue, brass, statueGold;
    static readonly Dictionary<Material, float> tileOf = new Dictionary<Material, float>();

    [MenuItem("Tools/Heist/Bank/Visual Overhaul")]
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
        log.Clear().AppendLine("[BankVisualOverhaul]");
        AssetDatabase.Refresh();
        ProcessTripoModels();
        BuildMaterials();
        var statueLoot = BuildStatueLoot();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var g in scene.GetRootGameObjects().Where(g => g.name == DecorRoot).ToArray()) Object.DestroyImmediate(g);
        var mapGo = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "HeistMap");
        if (!mapGo) { log.AppendLine("ERROR: HeistMap not found"); return log.ToString(); }
        var map = mapGo.transform;

        Retexture(map);
        var root = new GameObject(DecorRoot).transform;
        var st = Group(root, "Static");
        BuildCladding(st);
        BuildOfficeWainscot(st);
        BuildGarageMarkings(st);
        BuildAccentStrips(st, map);
        BuildTripoProps(st, map);
        BuildStatue(root, st, statueLoot, scene);
        BuildLighting(root, map);
        BuildPostFX(root, scene);

        foreach (var t in st.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorApplication.ExecuteMenuItem("Tools/Fusion/Scene/Bake Scene Objects");
        if (!EditorSceneManager.SaveScene(scene)) log.AppendLine("ERROR: save failed");
        AssetDatabase.SaveAssets();

        var nos = Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include);
        int baked = nos.Count(n => n.SortKey != 0);
        log.AppendLine($"  network objects: {nos.Length}, baked {baked}");
        if (baked != nos.Length) log.AppendLine("ERROR: not all scene NetworkObjects are baked");
        log.AppendLine($"  decor objects: {root.GetComponentsInChildren<Transform>(true).Length}, colliders: {root.GetComponentsInChildren<Collider>(true).Length}, new lights: {root.GetComponentsInChildren<Light>(true).Length}");
        return log.ToString();
    }

    // =========================================================================== превью
    /// Снимки Банка с нескольких ракурсов (с постобработкой) в HubPreview/Bank_* (папка в .gitignore) — проверка без шлема.
    /// Batchmode (без -nographics): -executeMethod BankVisualOverhaul.CapturePreview
    [MenuItem("Tools/Heist/Bank/Capture Preview")]
    public static void CapturePreview()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        string dir = System.IO.Path.GetFullPath("HubPreview");
        System.IO.Directory.CreateDirectory(dir);
        var shots = new (string name, Vector3 pos, Vector3 look)[]
        {
            ("Bank_01_hall", new Vector3(0, 1.7f, 1.5f), new Vector3(0, 3f, 20f)),   // первый кадр в batchmode без прогретого света — снимаем дважды
            ("Bank_01_hall", new Vector3(0, 1.7f, 1.5f), new Vector3(0, 3f, 20f)),
            ("Bank_02_reception", new Vector3(11.5f, 1.8f, 9.5f), new Vector3(8.6f, 1f, 3.9f)),
            ("Bank_03_gallery", new Vector3(20f, 1.8f, 9.5f), new Vector3(24f, 1.1f, 16.5f)),
            ("Bank_04_statue", new Vector3(24f, 1.6f, 14.2f), new Vector3(24f, 1.2f, 16.5f)),
            ("Bank_05_offices", new Vector3(-15.2f, 2f, 15f), new Vector3(-33f, 1.2f, 3f)),
            ("Bank_06_vault", new Vector3(-1f, 1.7f, 24.8f), new Vector3(-4f, 1.2f, 38f)),
            ("Bank_07_vaultdoor", new Vector3(-0.5f, 1.7f, 28.5f), new Vector3(4.4f, 1.4f, 25.4f)),
            ("Bank_08_garage", new Vector3(30f, -1.3f, 29.5f), new Vector3(19f, -2.4f, 37f)),
            ("Bank_09_truck", new Vector3(26.5f, -1.4f, 33f), new Vector3(20f, -2f, 36f)),
        };
        var go = new GameObject("PreviewCam");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 80f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 120f; cam.allowHDR = true;
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
        Debug.Log("[BankPreview] saved to " + dir);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    // =========================================================================== материалы
    static void BuildMaterials()
    {
        MapKit.EnsureFolder(MatDir);
        tileOf.Clear();
        var (mB, mN) = BankRetroTextures.MarbleLight();
        var (mdB, mdN) = BankRetroTextures.MarbleDark();
        var (lB, lN) = BankRetroTextures.Limestone();
        var (pB, pN) = BankRetroTextures.Plaster("Plaster", new Color(0.86f, 0.82f, 0.74f));
        var (pcB, pcN) = BankRetroTextures.Plaster("PlasterCool", new Color(0.80f, 0.83f, 0.86f));
        var (wB, wN) = BankRetroTextures.Wallpaper();
        var (wpB, wpN) = BankRetroTextures.WoodPanel();
        var (cB, cN) = BankRetroTextures.Carpet();
        var (aB, aN) = BankRetroTextures.ArmorPlate();
        var (tB, tN) = BankRetroTextures.TreadPlate();
        var (asB, asN) = BankRetroTextures.Asphalt();
        var (coB, coN) = BankRetroTextures.Concrete();
        var (qB, qN) = BankRetroTextures.Parquet();
        var (yB, yN) = BankRetroTextures.PaintYellow();

        marble = Lit("Marble", mB, mN, Color.white, 2.0f);
        marbleDark = Lit("MarbleDark", mdB, mdN, Color.white, 1.5f);
        limestone = Lit("Limestone", lB, lN, Color.white, 2.0f);
        plaster = Lit("Plaster", pB, pN, Color.white, 3.0f);
        plasterCool = Lit("PlasterCool", pcB, pcN, Color.white, 2.5f);
        wallpaper = Lit("Wallpaper", wB, wN, Color.white, 1.6f);
        woodPanel = Lit("WoodPanel", wpB, wpN, Color.white, 1.2f);
        carpet = Lit("OfficeCarpet", cB, cN, Color.white, 1.0f);
        armor = Lit("ArmorPlate", aB, aN, Color.white, 2.0f);
        armorLight = Lit("ArmorPlateLight", aB, aN, new Color(1.25f, 1.25f, 1.25f), 1.2f);
        tread = Lit("TreadPlate", tB, tN, Color.white, 1.2f);
        asphalt = Lit("Asphalt", asB, asN, Color.white, 3.0f);
        concrete = Lit("Concrete", coB, coN, Color.white, 2.5f);
        concreteCeil = Lit("ConcreteCeiling", coB, coN, new Color(0.55f, 0.55f, 0.55f), 2.5f);
        parquet = Lit("Parquet", qB, qN, Color.white, 1.6f);
        paint = Lit("PaintYellow", yB, yN, Color.white, 1.0f);
        brass = Emissive("Brass", new Color(0.78f, 0.58f, 0.22f), 0f);
        stripRed = Emissive("StripRed", new Color(1f, 0.08f, 0.05f), 4f);
        stripBlue = Emissive("StripBlue", new Color(0.15f, 0.35f, 1f), 4f);
        AssetDatabase.SaveAssets();
    }

    static Material GetMat(string name, string shader)
    {
        string path = $"{MatDir}/M_Bank_{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader.name != shader) m.shader = Shader.Find(shader);
        return m;
    }

    static Material Lit(string name, Texture2D baseMap, Texture2D normal, Color tint, float tile)
    {
        var m = GetMat(name, "Universal Render Pipeline/Lit");
        m.SetTexture("_BaseMap", baseMap);
        m.SetColor("_BaseColor", tint);
        if (normal) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", 0.6f); m.EnableKeyword("_NORMALMAP"); }
        TripoAssetPostprocessor.MakeMatte(m);                                    // Smoothness 0, без бликов и отражений
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        tileOf[m] = tile;
        return m;
    }

    static Material Emissive(string name, Color c, float intensity)
    {
        var m = GetMat(name, "Universal Render Pipeline/Lit");
        m.SetColor("_BaseColor", c);
        if (intensity > 0)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * intensity);                         // HDR > 1 → мягкий Bloom
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        TripoAssetPostprocessor.MakeMatte(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    // =========================================================================== перетекстурирование по зонам
    struct Rule { public string zone, src, name; public Material dst; public bool disable; }

    static Rule R(string zone, string src, Material dst, string name = null) => new Rule { zone = zone, src = src, dst = dst, name = name };

    static void Retexture(Transform map)
    {
        // порядок важен: правила с именем объекта — раньше общих
        var rules = new List<Rule>
        {
            R("Z1_Hub_Bunker", "M_ConcreteDark", concreteCeil, "Ceiling"), R("Z1_Hub_Bunker", "M_Concrete", concreteCeil, "Ceiling"),
            R("Z1_Hub_Bunker", "M_ConcreteDark", concrete), R("Z1_Hub_Bunker", "M_Concrete", concrete),

            R("Z2_MainHall", "M_Plaster", plaster, "Ceiling"),
            R("Z2_MainHall", "M_Plaster", limestone), R("Z2_MainHall", "M_Marble", marble), R("Z2_MainHall", "M_MarbleDark", marbleDark),

            new Rule { zone = "Z3_Gallery", src = "M_WoodDark", name = "Parquet", disable = true },   // старые полоски «паркета»
            R("Z3_Gallery", "M_WoodLight", parquet), R("Z3_Gallery", "M_PlasterCool", plasterCool),
            R("Z3_Gallery", "M_Marble", marble), R("Z3_Gallery", "M_MarbleDark", marbleDark),

            R("Z4_Offices", "M_Plaster", plaster, "Ceiling"),
            R("Z4_Offices", "M_Plaster", wallpaper), R("Z4_Offices", "M_Concrete", carpet, "Floor"), R("Z4_Offices", "M_CarpetBlue", carpet),

            R("Z5_Vault", "M_MarbleDark", tread, "Floor"), R("Z5_Vault", "M_ConcreteDark", armor),

            R("Z6_VentDuct", "M_Metal", armorLight),

            R("Z7_Garage", "M_Concrete", asphalt, "Floor"), R("Z7_Garage", "M_ConcreteDark", asphalt, "Ramp"),
            R("Z7_Garage", "M_ConcreteDark", concreteCeil, "Ceiling"), R("Z7_Garage", "M_Concrete", concrete),
            R("Z7_Garage", "M_PaintYellow", paint),
        };

        int n = 0, off = 0;
        foreach (var zoneName in rules.Select(r => r.zone).Distinct())
        {
            var zone = map.Find(zoneName);
            var st = zone ? zone.Find("Static") : null;
            if (!st) { log.AppendLine($"  WARN: zone {zoneName}/Static not found"); continue; }
            foreach (var r in st.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(r)) continue;
                var mf = r.GetComponent<MeshFilter>();
                var src = r.sharedMaterial;
                if (!mf || !mf.sharedMesh || !src) continue;
                string kind = MeshKind(mf.sharedMesh);
                if (kind == null) continue;

                Material dst = null;
                if (src.name.StartsWith("M_Bank_") && tileOf.Keys.Any(k => k.name == src.name))
                    dst = tileOf.Keys.First(k => k.name == src.name);              // повторный запуск: материал уже наш
                else
                {
                    var rule = rules.FirstOrDefault(x => x.zone == zoneName && x.src == src.name && (x.name == null || r.name.StartsWith(x.name)));
                    if (rule.zone == null) continue;
                    if (rule.disable) { r.enabled = false; off++; continue; }
                    dst = rule.dst;
                }
                r.sharedMaterial = dst;
                mf.sharedMesh = kind == "box" ? BoxMesh(r.transform.lossyScale, tileOf[dst]) : CylMesh(r.transform.lossyScale, tileOf[dst]);
                EditorUtility.SetDirty(r); EditorUtility.SetDirty(mf);
                n++;
            }
        }
        log.AppendLine($"  retextured {n} architecture renderers, hid {off} old parquet strips");
    }

    static string MeshKind(Mesh m)
    {
        if (m.name == "Cube" || m.name.StartsWith("BankBox_")) return "box";
        if (m.name == "Cylinder" || m.name.StartsWith("BankCyl_")) return "cyl";
        return null;
    }

    static Mesh builtinCyl;

    /// Единичный куб с UV в метрах (по размеру объекта / tile) — текстура тайлится без растяжения.
    static Mesh BoxMesh(Vector3 scale, float tile)
    {
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        string key = $"BankBox_{scale.x:0.00}_{scale.y:0.00}_{scale.z:0.00}_{tile:0.0}".Replace(',', '.');
        string path = $"{MeshDir}/{key}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing) return existing;
        MapKit.EnsureFolder(MeshDir);

        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        void Face(Vector3 n, Vector3 a, Vector3 b, float sa, float sb)
        {
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

    static void FixWinding(Mesh m)
    {
        var v = m.vertices; var t = m.triangles;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f) < 0) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
        }
        m.triangles = t;
    }

    static Mesh CylMesh(Vector3 scale, float tile)
    {
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        string key = $"BankCyl_{scale.x:0.00}_{scale.y:0.00}_{tile:0.0}".Replace(',', '.');
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

    static GameObject Box(Transform p, string name, Vector3 pos, Vector3 size, Material mat, bool collider = false, Quaternion? rot = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p, false);
        go.transform.SetPositionAndRotation(pos, rot ?? Quaternion.identity);
        go.transform.localScale = size;
        go.AddComponent<MeshFilter>().sharedMesh = tileOf.TryGetValue(mat, out var tile) ? BoxMesh(size, tile) : Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        if (collider) go.AddComponent<BoxCollider>();
        return go;
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

    /// Плоская облицовка на стене: прямоугольник [u0,u1]×[y0,y1] за вычетом проёмов (по оси U проёмы не пересекаются).
    /// axisX = true: плоскость x = c, U = z; иначе плоскость z = c, U = x.
    static void Panel(Transform p, string name, bool axisX, float c, float u0, float u1, float y0, float y1, float thick, Material mat,
                      params (float u0, float u1, float y0, float y1)[] holes)
    {
        void Piece(float a0, float a1, float b0, float b1)
        {
            if (a1 - a0 < 0.01f || b1 - b0 < 0.01f) return;
            var pos = axisX ? new Vector3(c, (b0 + b1) / 2, (a0 + a1) / 2) : new Vector3((a0 + a1) / 2, (b0 + b1) / 2, c);
            var size = axisX ? new Vector3(thick, b1 - b0, a1 - a0) : new Vector3(a1 - a0, b1 - b0, thick);
            Box(p, name, pos, size, mat);
        }
        float cur = u0;
        foreach (var h in holes.OrderBy(h => h.u0))
        {
            Piece(cur, h.u0, y0, y1);
            Piece(h.u0, h.u1, y0, Mathf.Max(y0, h.y0));
            Piece(h.u0, h.u1, Mathf.Min(y1, h.y1), y1);
            cur = h.u1;
        }
        Piece(cur, u1, y0, y1);
    }

    // =========================================================================== облицовка общих стен
    /// Общие стены зон (толщина 0.3, лицо на ±0.15) с «чужой» стороны получают тонкую облицовку своей зоны.
    static void BuildCladding(Transform st)
    {
        var g = Group(st, "Cladding");
        const float T = 0.01f;
        // Офисы ← стена Холла x = −14 (сторона офисов)
        Panel(g, "Clad_Office_HallWall", true, -14.155f, 0, 24, 0, 9, T, wallpaper, (4f, 6.5f, 0f, 2.8f), (14f, 16.5f, 4.8f, 7.3f));
        // Хранилище ← стена офисов x = −14 (сторона хранилища) и стена Холла z = 24 (сторона хранилища, проём решётки)
        Panel(g, "Clad_Vault_OfficeWall", true, -13.845f, 24, 40, 0, 6, T, armor);
        Panel(g, "Clad_Vault_HallWall", false, 24.155f, -14, 10, 0, 6, T, armor, (-1.5f, 1.5f, 0f, 3f));
        // Гараж ← стена хранилища x = 10 и стена галереи z = 24 (проём пандуса)
        Panel(g, "Clad_Garage_VaultWall", true, 10.155f, 24, 40, -3, 2.5f, T, concrete);
        Panel(g, "Clad_Garage_GalleryWall", false, 24.155f, 14, 34, -3, 2.5f, T, concrete, (28f, 33f, 0f, 2.5f));
        // Галерея ← стена Холла x = 14 (сторона галереи, арка)
        Panel(g, "Clad_Gallery_HallWall", true, 14.155f, 0, 24, 0, 6, T, plasterCool, (8f, 12f, 0f, 3.5f));
        // Убежище ← стена Холла z = 0 (сторона убежища, шлюз)
        Panel(g, "Clad_Bunker_HallWall", false, -0.155f, -7, 7, 0, 4, T, concrete, (-1.5f, 1.5f, 0f, 3f));
    }

    // =========================================================================== офисы: деревянные панели
    static void BuildOfficeWainscot(Transform st)
    {
        var g = Group(st, "OfficeWainscot");
        const float T = 0.04f;
        void Level(float y0, float h, float zFrom, params (float u0, float u1, float y0, float y1)[] eastHoles)
        {
            float y1 = y0 + h;
            Panel(g, "Wainscot_W", true, -33.83f, zFrom, 40, y0, y1, T, woodPanel);
            Panel(g, "Wainscot_N", false, 39.83f, -34, -14, y0, y1, T, woodPanel);
            if (zFrom < 1) Panel(g, "Wainscot_S", false, 0.17f, -34, -14, y0, y1, T, woodPanel);
            Panel(g, "Wainscot_E_Hall", true, -14.18f, zFrom, 24, y0, y1, T, woodPanel, eastHoles);
            Panel(g, "Wainscot_E_Vault", true, -14.17f, 24, 40, y0, y1, T, woodPanel);
            // планка-поручень сверху
            Box(g, "Rail_W", new Vector3(-33.8f, y1, (zFrom + 40) / 2), new Vector3(0.07f, 0.06f, 40 - zFrom), woodPanel);
            Box(g, "Rail_N", new Vector3(-24, y1, 39.8f), new Vector3(20, 0.06f, 0.07f), woodPanel);
        }
        Level(0f, 1.2f, 0f, (4f, 6.5f, 0f, 2.8f));               // первый этаж (архив), проём в Холл
        Level(4.5f, 1.0f, 12.2f, (14f, 16.5f, 4.5f, 7.3f));      // антресоль, проём на балкон Холла
    }

    // =========================================================================== гараж: разметка
    static void BuildGarageMarkings(Transform st)
    {
        var g = Group(st, "GarageMarkings");
        const float y = -2.994f;
        for (float x = 11.2f; x < 27.5f; x += 2.2f)                                 // осевая пунктирная линия проезда
            Box(g, "Lane_Dash", new Vector3(x + 0.6f, y, 32.6f), new Vector3(1.2f, 0.01f, 0.14f), paint);
        Box(g, "Ramp_StopLine", new Vector3(30.5f, y, 32.7f), new Vector3(4.6f, 0.01f, 0.3f), paint);
        foreach (float x in new[] { 15f, 18f, 21f, 24f, 27f })                     // парковочные места у северной стены
            Box(g, "Bay_Line", new Vector3(x, y, 42.3f), new Vector3(0.12f, 0.01f, 3f), paint);
        Box(g, "Bay_Back", new Vector3(21f, y, 43.75f), new Vector3(12.1f, 0.01f, 0.12f), paint);
        Box(g, "Edge_E", new Vector3(33.6f, y, 37f), new Vector3(0.12f, 0.01f, 9f), paint);
        // жёлтая полоса-отбойник внизу стен
        Box(g, "Curb_N", new Vector3(22f, -2.8f, 43.84f), new Vector3(23.4f, 0.4f, 0.01f), paint);
        Box(g, "Curb_E", new Vector3(33.84f, -2.8f, 37f), new Vector3(0.01f, 0.4f, 13.6f), paint);
    }

    // =========================================================================== красно-синие акценты (без новых источников)
    static void BuildAccentStrips(Transform st, Transform map)
    {
        var g = Group(st, "AccentStrips");
        // хранилище: красная полоса по северной стене, синие — по боковым
        Box(g, "Vault_Strip_N", new Vector3(-2, 5.6f, 39.8f), new Vector3(23.4f, 0.06f, 0.06f), stripRed);
        Box(g, "Vault_Strip_W", new Vector3(-13.8f, 5.6f, 32), new Vector3(0.06f, 0.06f, 15.4f), stripBlue);
        Box(g, "Vault_Strip_E", new Vector3(9.8f, 5.6f, 32), new Vector3(0.06f, 0.06f, 15.4f), stripBlue);
        // гараж: светящиеся полосы на колоннах
        foreach (var (p, i) in new[] { new Vector3(13.5f, -3, 29), new Vector3(25, -3, 29), new Vector3(25, -3, 40.5f), new Vector3(13.5f, -3, 40.5f) }.Select((p, i) => (p, i)))
            Box(g, "Pillar_Strip", p + new Vector3(0, 2.1f, 0), new Vector3(0.74f, 0.07f, 0.74f), i % 2 == 0 ? stripRed : stripBlue);

        // светильники хранилища и гаража — в цвет новых ламп (меняем только материал, геометрия от билдера)
        int k = 0;
        foreach (var r in FindRenderers(map, "Z5_Vault", "CeilingStrip")) { r.sharedMaterial = k++ % 2 == 0 ? stripBlue : stripRed; EditorUtility.SetDirty(r); }
        k = 0;
        foreach (var r in FindRenderers(map, "Z7_Garage", "SodiumFixture")) { r.sharedMaterial = k++ % 2 == 0 ? stripRed : stripBlue; EditorUtility.SetDirty(r); }
    }

    static IEnumerable<MeshRenderer> FindRenderers(Transform map, string zone, string name)
    {
        var z = map.Find(zone);
        if (!z) return Enumerable.Empty<MeshRenderer>();
        return z.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name == name).OrderBy(r => r.transform.position.x).ThenBy(r => r.transform.position.z);
    }

    static Transform FindIn(Transform map, string zone, string name)
    {
        var z = map.Find(zone);
        return z ? z.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name) : null;
    }

    // =========================================================================== Tripo
    static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Gen}/{name}/{name}.prefab");

    static void ProcessTripoModels()
    {
        foreach (var n in TripoNames)
        {
            string glb = $"{Gen}/{n}.glb";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(glb) == null) { log.AppendLine($"  tripo {n}: no .glb yet"); continue; }
            var p = TripoAssetPostprocessor.Process(glb);                           // в batchmode delayCall не срабатывает — вызываем явно
            log.AppendLine($"  tripo {n}: {p}");
        }
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.5f);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// Ставит Tripo-префаб: масштаб по высоте (или по длине, если length > 0), низ на floorPos.y, центр по XZ.
    static GameObject Place(Transform p, string name, Vector3 floorPos, float yaw, float height, float length = 0, bool meshColliders = true, float stretchY = 1f)
    {
        var prefab = Prefab(name);
        if (!prefab) { log.AppendLine($"  skip {name}: no prefab (generate in Tripo first)"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, p);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, yaw, 0));
        go.transform.localScale = Vector3.one;
        var b0 = WorldBounds(go);
        float s = length > 0 ? length / Mathf.Max(b0.size.x, b0.size.z) : height / Mathf.Max(0.0001f, b0.size.y);
        go.transform.localScale = new Vector3(s, s * stretchY, s);
        var b = WorldBounds(go);
        go.transform.position = new Vector3(floorPos.x - b.center.x, floorPos.y - b.min.y, floorPos.z - b.center.z);
        foreach (var bc in go.GetComponentsInChildren<BoxCollider>()) Object.DestroyImmediate(bc);
        if (meshColliders)
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                if (!mf.TryGetComponent<MeshCollider>(out _)) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;   // Default — за него цепляются руки
        log.AppendLine($"  placed {name} scale {s:0.00} size {WorldBounds(go).size}");
        return go;
    }

    static void Hide(Transform t, string why)
    {
        if (!t) return;
        if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); EditorUtility.SetDirty(t.gameObject); }
        log.AppendLine($"  hid old {t.name} ({why})");
    }

    static void BuildTripoProps(Transform st, Transform map)
    {
        var g = Group(st, "TripoProps");

        // Дверь хранилища: вместо собранной из цилиндров — распахнутая бронедверь у входа, лицом к центру хранилища
        if (Prefab("Bank_VaultDoor"))
        {
            Hide(FindIn(map, "Z5_Vault", "VaultDoorDecor"), "replaced by Bank_VaultDoor");
            Place(g, "Bank_VaultDoor", new Vector3(4.6f, 0, 25.5f), -44f, 3.0f);
        }

        // Стойка ресепшена: на месте старой, фасадом ко входу (−Z)
        if (Prefab("Bank_ReceptionDesk"))
        {
            Hide(FindIn(map, "Z2_MainHall", "ReceptionDesk"), "replaced by Bank_ReceptionDesk");
            // модель низкая и широкая (1 : 0.33): ставим по ширине 4.2 м и вытягиваем по высоте, чтобы стекло было выше головы клиента
            var desk = Place(g, "Bank_ReceptionDesk", new Vector3(8.6f, 0, 3.9f), DeskYaw, 0, 4.2f, stretchY: 1.35f);
            if (desk) PutGoldBarOnDesk(map, desk);
        }

        // Броневик эвакуации: на месте фургона, кабиной на −X, задние двери на +X (к проезду)
        if (Prefab("Bank_GetawayTruck")) BuildTruck(g, map);
    }

    /// Поворот стойки: мониторы — к кассиру (на север, +Z), фасад — ко входу в Холл.
    /// После импорта .glb длинная сторона модели лежит вдоль Z, поэтому поворот 90°.
    const float DeskYaw = 90f;

    /// Слиток на стойке ресепшена: опускаем/поднимаем на поверхность новой стойки (луч сверху вниз).
    static void PutGoldBarOnDesk(Transform map, GameObject desk)
    {
        var hall = map.Find("Z2_MainHall");
        var bar = hall ? hall.GetComponentsInChildren<LootItem>(true).Where(l => l.lootId == "GoldBar")
                              .OrderBy(l => (l.transform.position - new Vector3(9.4f, 1.15f, 3.2f)).sqrMagnitude).FirstOrDefault() : null;
        if (!bar) return;
        Physics.SyncTransforms();
        var db = WorldBounds(desk);
        // ищем полку перед стеклом: несколько лучей по фасаду, берём самую низкую поверхность выше 0.8 м
        float bestY = float.MaxValue; Vector3 best = Vector3.zero;
        for (float x = db.min.x + 0.3f; x < db.max.x - 0.2f; x += 0.25f)
            for (float z = db.min.z + 0.05f; z < db.max.z - 0.05f; z += 0.1f)
                if (Physics.Raycast(new Vector3(x, db.max.y + 0.5f, z), Vector3.down, out var hit, db.size.y + 1f) && hit.collider.transform.IsChildOf(desk.transform)
                    && hit.point.y > 0.8f && hit.point.y < 1.4f && hit.normal.y > 0.9f && hit.point.y < bestY - 0.01f)
                { bestY = hit.point.y; best = hit.point; }
        if (bestY == float.MaxValue) { log.AppendLine("  WARN: no counter surface found for GoldBar"); return; }
        Undo.RecordObject(bar.transform, "bar");
        bar.transform.position = best + Vector3.up * 0.005f;
        EditorUtility.SetDirty(bar.transform);
        log.AppendLine($"  GoldBar moved onto reception counter at {best}");
    }

    static void BuildTruck(Transform g, Transform map)
    {
        var van = FindIn(map, "Z7_Garage", "Van");
        var evac = FindIn(map, "Z7_Garage", "EvacZone_VanCargo");
        if (!van || !evac) { log.AppendLine("ERROR: Van / EvacZone_VanCargo not found"); return; }

        // старый фургон: прячем кузов и коллайдеры, оставляем надписи, зелёный свет и разметку «лут сюда»
        foreach (var r in van.GetComponentsInChildren<MeshRenderer>(true)) if (r.name != "Floor_Marker" && !r.GetComponent<TMP_Text>()) { r.enabled = false; EditorUtility.SetDirty(r); }
        foreach (var c in van.GetComponentsInChildren<Collider>(true)) { c.enabled = false; EditorUtility.SetDirty(c); }
        foreach (var t in van.GetComponentsInChildren<TMP_Text>(true)) { var tr = t.GetComponent<MeshRenderer>(); if (tr) { tr.enabled = true; EditorUtility.SetDirty(tr); } }

        var truck = Place(g, "Bank_GetawayTruck", new Vector3(19.2f, -3f, 36f), TruckYaw(), 0, 6.6f, meshColliders: false);
        if (!truck) return;
        var b = WorldBounds(truck);
        // простые коллайдеры: кабина и кузов (за них цепляются руки, на крышу можно залезть), задние двери закрыты
        var body = new GameObject("Truck_BodyCollider");
        body.transform.SetParent(g, false);
        body.transform.SetPositionAndRotation(new Vector3(b.center.x, b.center.y + 0.15f, b.center.z), Quaternion.identity);
        var bc = body.AddComponent<BoxCollider>(); bc.size = new Vector3(b.size.x, b.size.y - 0.3f, b.size.z * 0.92f);

        float rear = b.max.x, top = b.max.y, zc = b.center.z;
        // зона сдачи лута — у задних дверей и чуть за ними (лут бросают в двери / кладут на площадку)
        Undo.RecordObject(evac, "evac");
        evac.position = new Vector3(rear + 0.55f, -3f + 1.1f, zc);
        evac.rotation = Quaternion.identity;
        var trig = evac.GetComponent<BoxCollider>();
        Undo.RecordObject(trig, "evac");
        trig.center = Vector3.zero; trig.size = new Vector3(1.6f, 2.2f, b.size.z * 0.95f);
        EditorUtility.SetDirty(evac); EditorUtility.SetDirty(trig);

        void Move(string child, Vector3 pos, Quaternion? rot = null)
        {
            var t = van.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == child);
            if (!t) return;
            t.SetPositionAndRotation(pos, rot ?? t.rotation);
            EditorUtility.SetDirty(t);
        }
        Move("Floor_Marker", new Vector3(rear + 0.95f, -2.993f, zc));
        Move("Sign_Back", new Vector3(rear + 0.06f, top + 0.3f, zc), MapKit.Facing(Vector3.right));
        Move("Sign_Side", new Vector3(b.center.x, top - 0.55f, b.min.z - 0.04f), MapKit.Facing(Vector3.back));
        Move("VanLight", new Vector3(rear + 0.8f, top - 0.2f, zc));
        log.AppendLine($"  truck bounds {b.min}..{b.max}; evac zone moved behind rear doors");
    }

    /// Поворот броневика так, чтобы кабина смотрела на −X. Кабина ниже кузова: сравниваем высоту вершин
    /// у двух концов длинной оси модели.
    static float TruckYaw()
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("Bank_GetawayTruck"));
        try
        {
            var pts = new List<Vector3>();
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                pts.AddRange(mf.sharedMesh.vertices.Select(v => mf.transform.TransformPoint(v)));
            var b = WorldBounds(inst);
            bool alongZ = b.size.z >= b.size.x;
            float Axis(Vector3 p) => alongZ ? p.z : p.x;
            float lo = alongZ ? b.min.z : b.min.x, len = alongZ ? b.size.z : b.size.x;
            float topMin = pts.Where(p => Axis(p) < lo + len * 0.2f).Select(p => p.y).DefaultIfEmpty(0).Max();
            float topMax = pts.Where(p => Axis(p) > lo + len * 0.8f).Select(p => p.y).DefaultIfEmpty(0).Max();
            bool cabAtMin = topMin < topMax;
            // поворот θ переводит (0,0,−1) в (−sinθ,0,−cosθ), (1,0,0) в (cosθ,0,−sinθ)
            float yaw = alongZ ? (cabAtMin ? 90f : -90f) : (cabAtMin ? 0f : 180f);
            log.AppendLine($"  truck: long axis {(alongZ ? "Z" : "X")}, cab at {(cabAtMin ? "min" : "max")} end (tops {topMin:0.00}/{topMax:0.00}) → yaw {yaw}");
            return yaw;
        }
        finally { Object.DestroyImmediate(inst); }
    }

    // =========================================================================== золотая статуя — сетевой лут $1500
    static GameObject BuildStatueLoot()
    {
        var model = Prefab("Bank_GoldenStatue");
        if (!model) { log.AppendLine("  skip statue loot: no Bank_GoldenStatue prefab"); return null; }

        // материал: текстура Tripo + эмиссия по той же текстуре (LootItem пульсирует _EmissionColor → золото мягко светится в Bloom)
        var src = model.GetComponentInChildren<Renderer>().sharedMaterial;
        statueGold = GetMat("StatueGold", "Universal Render Pipeline/Lit");
        var tex = src.GetTexture("_BaseMap");
        statueGold.SetTexture("_BaseMap", tex);
        statueGold.SetColor("_BaseColor", Color.white);
        statueGold.EnableKeyword("_EMISSION");
        statueGold.SetTexture("_EmissionMap", tex);
        statueGold.SetColor("_EmissionColor", Color.black);
        statueGold.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        TripoAssetPostprocessor.MakeMatte(statueGold);
        EditorUtility.SetDirty(statueGold);

        var root = new GameObject("Loot_GoldenStatue") { tag = "Loot" };
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        inst.name = "Model";
        inst.transform.SetParent(root.transform, false);
        inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
        var b0 = WorldBounds(inst);
        float s = 1.2f / b0.size.y;                                                 // статуя ~1.2 м — тяжёлая, носят двумя руками
        inst.transform.localScale = Vector3.one * s;
        var b = WorldBounds(inst);
        inst.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
        {
            r.sharedMaterials = Enumerable.Repeat(statueGold, r.sharedMaterials.Length).ToArray();
            r.shadowCastingMode = ShadowCastingMode.Off;
        }
        b = WorldBounds(inst);

        var col = root.AddComponent<BoxCollider>();
        col.center = b.center; col.size = b.size;
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 25f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        var li = root.AddComponent<LootItem>();
        li.lootId = "GoldenStatue"; li.itemName = "Золотая статуя основателя"; li.value = 1500;
        li.isHeavy = true; li.spinUntilGrabbed = false;
        li.glowLight = false;                                                       // без лишнего источника света (бюджет Quest)
        li.glowColor = new Color(1f, 0.7f, 0.25f);

        // сеть: как у остального лута (CoopSyncSetup)
        var no = root.AddComponent<NetworkObject>();
        var f = no.Flags.SetCurrentVersion();
        f &= ~NetworkObjectFlags.MasterClientObject;
        f |= NetworkObjectFlags.AllowStateAuthorityOverride;
        f &= ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
        no.Flags = f;
        var nt = root.AddComponent<NetworkTransform>();
        var ps = nt.PhysicsSettings; ps.SetForecastDisabledProxyRigidbodiesToKinematic = true; nt.PhysicsSettings = ps;
        nt.ConfigFlags |= NetworkTransform.NetworkTransformFlags.DisableSharedModeInterpolation;
        root.AddComponent<NetworkLootItem>();

        MapKit.EnsureFolder(Gen);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, StatueLootPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        EditorApplication.ExecuteMenuItem("Tools/Fusion/Rebuild Prefab Table");
        log.AppendLine($"  loot prefab {StatueLootPath}: $1500, heavy, size {b.size}");
        return prefab;
    }

    static void BuildStatue(Transform root, Transform st, GameObject lootPrefab, UnityEngine.SceneManagement.Scene scene)
    {
        var center = new Vector3(24f, 0, 16.5f);
        var g = Group(st, "StatuePedestal");
        Box(g, "Pedestal", center + Vector3.up * 0.3f, new Vector3(0.95f, 0.6f, 0.95f), marbleDark, true);
        Box(g, "PedestalTop", center + Vector3.up * 0.615f, new Vector3(1.05f, 0.03f, 1.05f), brass, true);
        Box(g, "PedestalFoot", center + Vector3.up * 0.04f, new Vector3(1.1f, 0.08f, 1.1f), marble, true);
        Box(g, "Plaque", center + new Vector3(0, 0.33f, -0.48f), new Vector3(0.6f, 0.2f, 0.01f), brass);
        Label(g, "Plaque_Text", "ОСНОВАТЕЛЬ БАНКА\n$1500", 0.45f, new Color(0.15f, 0.1f, 0.03f), center + new Vector3(0, 0.33f, -0.487f), Vector3.back, new Vector2(0.58f, 0.2f));
        // музейный бархатный барьер — не мешает взять, просто красиво
        foreach (var dx in new[] { -0.9f, 0.9f })
            foreach (var dz in new[] { -0.9f, 0.9f })
                Box(g, "Post", center + new Vector3(dx, 0.45f, dz), new Vector3(0.06f, 0.9f, 0.06f), brass);

        if (!lootPrefab) return;
        var lootRoot = Group(root, "Loot");
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(lootPrefab, lootRoot);
        inst.transform.SetPositionAndRotation(center + Vector3.up * 0.63f, Quaternion.Euler(0, 180, 0));   // лицом к скамье и входу (−Z)
        // справочник лута (для билдеров): добавить статую, если её ещё нет
        var reg = Object.FindAnyObjectByType<LootRegistry>(FindObjectsInactive.Include);
        if (reg && (reg.prefabs == null || reg.prefabs.All(e => e.id != "GoldenStatue")))
        {
            var list = (reg.prefabs ?? new LootRegistry.Entry[0]).ToList();
            list.Add(new LootRegistry.Entry { id = "GoldenStatue", prefab = lootPrefab });
            reg.prefabs = list.ToArray();
            EditorUtility.SetDirty(reg);
        }
        log.AppendLine("  golden statue loot placed in gallery");
    }

    // =========================================================================== свет
    static void BuildLighting(Transform root, Transform map)
    {
        var lights = map.GetComponentsInChildren<Light>(true);
        void Set(string name, Color c, float intensity, float? range = null)
        {
            foreach (var l in lights.Where(x => x.name == name)) { l.color = c; l.intensity = intensity; if (range.HasValue) l.range = range.Value; l.shadows = LightShadows.None; EditorUtility.SetDirty(l); }
        }
        // Холл — тёплый
        Set("HallLight", new Color(1f, 0.76f, 0.5f), 3.0f);
        Set("BalconyLight", new Color(1f, 0.72f, 0.45f), 1.6f);
        // Галерея — холодный музейный
        Set("MuseumSpot", new Color(0.76f, 0.87f, 1f), 7f);
        Set("GalleryFill", new Color(0.66f, 0.78f, 1f), 1.3f);
        // Хранилище — синий спереди, красный в глубине
        Set("VaultLight", new Color(0.3f, 0.45f, 1f), 3.0f, 15f);
        Set("VaultLightBack", new Color(1f, 0.16f, 0.1f), 2.4f, 11f);
        // Гараж — чередуем красный и синий
        int k = 0;
        foreach (var l in lights.Where(x => x.name == "SodiumLight").OrderBy(x => x.transform.position.x).ThenBy(x => x.transform.position.z))
        {
            bool red = k++ % 2 == 0;
            l.color = red ? new Color(1f, 0.2f, 0.14f) : new Color(0.28f, 0.42f, 1f);
            l.intensity = red ? 2.4f : 2.8f;
            l.shadows = LightShadows.None;
            EditorUtility.SetDirty(l);
        }

        // единственный новый источник: узкий холодный спот на золотую статую
        var g = Group(root, "Lighting");
        var spotPos = new Vector3(24f, 5.85f, 16.5f);
        Box(g, "TrackLight_Statue", spotPos + Vector3.up * 0.08f, new Vector3(0.18f, 0.2f, 0.18f), armor);
        var go = new GameObject("StatueSpot");
        go.transform.SetParent(g, false);
        go.transform.SetPositionAndRotation(spotPos, Quaternion.LookRotation(Vector3.down + Vector3.forward * 0.001f));
        var s = go.AddComponent<Light>();
        s.type = LightType.Spot; s.color = new Color(0.95f, 0.95f, 1f); s.intensity = 6f; s.range = 7.5f;
        s.spotAngle = 34f; s.innerSpotAngle = 20f; s.shadows = LightShadows.None;
        log.AppendLine("  lighting: warm hall, cold gallery, red/blue vault & garage, +1 statue spot");
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
        // мягкий Bloom в четверть разрешения: светятся лазеры, огоньки камер, лампы, полосы, пульсирующее золото лута
        var bloom = Comp<Bloom>(profile);
        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(0.45f);
        bloom.scatter.Override(0.55f);
        bloom.tint.Override(Color.white);
        bloom.highQualityFiltering.Override(false);
        bloom.downscale.Override(BloomDownscaleMode.Quarter);
        bloom.maxIterations.Override(5);
        var ca = Comp<ColorAdjustments>(profile);
        ca.postExposure.Override(0.1f);
        ca.contrast.Override(14f);
        ca.saturation.Override(8f);
        ca.colorFilter.Override(new Color(1f, 0.98f, 0.95f));
        EditorUtility.SetDirty(profile);
        foreach (var c in profile.components) EditorUtility.SetDirty(c);

        var volGo = new GameObject("BankPostFX");
        volGo.transform.SetParent(root, false);
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true; vol.priority = 1; vol.sharedProfile = profile;

        // камеры игроков в этой сцене (переопределение в экземпляре префаба — сам префаб игрока не меняется)
        int cams = 0;
        foreach (var go in scene.GetRootGameObjects())
            foreach (var cam in go.GetComponentsInChildren<Camera>(true))
            {
                if (cam.targetTexture != null || cam.name.Contains("Mirror")) continue;
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(data);
                if (PrefabUtility.IsPartOfPrefabInstance(data)) PrefabUtility.RecordPrefabInstancePropertyModifications(data);
                cams++;
            }
        log.AppendLine($"  post-processing: Bloom + ColorAdjustments, {cams} camera(s)");
    }
}
