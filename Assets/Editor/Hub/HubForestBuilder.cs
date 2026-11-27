using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// MainHub → «Лесной двор-убежище банды» под закатным небом. Меняет ТОЛЬКО Assets/Scenes/MainHub.unity.
/// Повторный запуск безопасен: пересоздаётся лишь корень сцены «HubForest», старая коробка прячется (SetActive false),
/// стол планирования переносится к компьютеру один раз.
/// Сохраняет и вписывает: GRABZ OS (44 клавиши), зеркало, 6 SkinButton, витрину масок/шапок, сейф гардероба,
/// баскетбол (кольцо, мяч, табло), VanPortal + чёрный автобус + тоннель (Stencil-портал, Wall_N, Wall_N_Colliders),
/// табло режима (как арка ворот), FusionNetworkManager, PlayFab, HubVanSync, игроков.
/// Ассеты: CC0 Quaternius Stylized Nature MegaKit (деревья, камни, трава) и кит Blender
/// Assets/Art/GeneratedModels/Hub/Forest (Source/build_hub_forest_kit.py).
/// Все поверхности для паркура — с коллайдерами на слое Default.
/// Меню: Tools/Heist/Hub/Forest Hideout. Batchmode (без -nographics): -executeMethod HubForestBuilder.RunBatch
/// </summary>
public static class HubForestBuilder
{
    const string ScenePath = "Assets/Scenes/MainHub.unity";
    const string Kit = "Assets/Art/GeneratedModels/Hub/Forest";
    const string Nature = "Assets/Art/ThirdParty/Quaternius/NatureMegaKit/FBX";
    const string MatDir = "Assets/Art/Materials/Hub/Forest";
    const string Grain = "Assets/Art/Models/PlayerFur/T_GillaFur.png";
    const string NatureTex = "Assets/Art/ThirdParty/Quaternius/NatureMegaKit/Textures";
    static readonly StringBuilder log = new StringBuilder();
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static Transform root;
    static System.Random rnd;

    [MenuItem("Tools/Heist/Hub/Forest Hideout")]
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
        log.Clear(); log.AppendLine("[HubForestBuilder]");
        rnd = new System.Random(42);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Art/Materials/Hub", "Forest");
        SetupKitImport();
        CreateMaterials();

        var old = GameObject.Find("HubForest"); if (old) Object.DestroyImmediate(old);
        root = new GameObject("HubForest").transform;

        HideOldBox();
        MovePlanningToHQ();
        Atmosphere();
        Ground();
        Boundary();
        Wardrobe();
        HeadQuarters();
        Departure();
        Campfire();
        Basketball();
        Watchtower();
        Parkour();
        Greenery();
        AddColliders();

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) log.AppendLine("  ERROR: scene save failed");
        log.AppendLine($"  HubForest: renderers {root.GetComponentsInChildren<Renderer>().Length}, colliders {root.GetComponentsInChildren<Collider>().Length}, lights {root.GetComponentsInChildren<Light>().Length}");
        Checks();
        RenderShots();
        return log.ToString();
    }

    // ================================================================== импорт и материалы
    static void SetupKitImport()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Kit }))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(p) is not ModelImporter mi) continue;
            if (mi.importCameras || mi.importLights || mi.importAnimation || !mi.isReadable)
            { mi.importCameras = false; mi.importLights = false; mi.importAnimation = false; mi.isReadable = true; mi.SaveAndReimport(); }
        }
    }

    static Material Mat(string name, Color c, Texture tex = null, float tiling = 1f, Color? emission = null, float smooth = 0f)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c); m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", Vector2.one * tiling);
        m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f); m.enableInstancing = true;
        if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        EditorUtility.SetDirty(m); mats[name] = m; return m;
    }
    static Material M(string n) => mats.TryGetValue(n, out var m) ? m : mats["M_HubF_Grass"];

    static void CreateMaterials()
    {
        var grain = AssetDatabase.LoadAssetAtPath<Texture2D>(Grain);
        var rocks = AssetDatabase.LoadAssetAtPath<Texture2D>($"{NatureTex}/Rocks_Diffuse.png");
        var path = AssetDatabase.LoadAssetAtPath<Texture2D>($"{NatureTex}/PathRocks_Diffuse.png");
        Mat("M_HubF_Grass", new Color(0.36f, 0.52f, 0.24f), grain, 40f);
        Mat("M_HubF_Hill", new Color(0.34f, 0.48f, 0.23f), grain, 12f);
        Mat("M_HubF_Dirt", new Color(0.52f, 0.38f, 0.26f), grain, 10f);
        Mat("M_HubF_Gravel", new Color(0.62f, 0.58f, 0.52f), path ? path : grain, 3f);
        Mat("M_HubF_Asphalt", new Color(0.24f, 0.24f, 0.27f), grain, 10f);
        Mat("M_HubF_Cliff", new Color(0.55f, 0.53f, 0.52f), rocks, 2f);
        Mat("M_HubF_Rock", new Color(0.62f, 0.6f, 0.6f), rocks, 1f);
        Mat("M_HubF_Stone", new Color(0.55f, 0.54f, 0.52f), grain, 2f);
        Mat("M_HubF_Ember", new Color(1f, 0.42f, 0.1f), null, 1f, new Color(2.4f, 0.9f, 0.15f));
        Mat("M_HubF_Flame", new Color(1f, 0.45f, 0.1f), null, 1f, new Color(2.6f, 0.9f, 0.12f));
        Mat("M_HubF_Wire", new Color(0.08f, 0.08f, 0.08f));
        Mat("M_HubF_Cork", new Color(0.66f, 0.47f, 0.3f), grain, 2f);
        Mat("M_HubF_Paper", new Color(0.93f, 0.9f, 0.8f));
        Mat("M_HubF_MapInk", new Color(0.2f, 0.32f, 0.55f));
        Mat("M_HubF_Red", new Color(0.8f, 0.15f, 0.12f));
        Mat("M_HubF_Line", new Color(0.95f, 0.92f, 0.8f));
        mats["M_Wood"] = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Hub/M_Hub_Wood.mat");
        mats["M_WoodDark"] = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Hub/M_Hub_WoodDark.mat");
        mats["M_Brick"] = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Hub/M_Hub_Brick.mat");
        mats["M_Bulb"] = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Hub/M_Hub_Bulb.mat");
        mats["M_Bark"] = mats["M_WoodDark"]; mats["M_Stone"] = mats["M_HubF_Stone"]; mats["M_Ember"] = mats["M_HubF_Ember"]; mats["M_Wire"] = mats["M_HubF_Wire"];
        foreach (var k in new[] { "M_Wood", "M_WoodDark", "M_Brick", "M_Bulb" }) if (!mats[k]) log.AppendLine("  ERROR: missing material " + k);
    }

    // ================================================================== утилиты
    static Transform Group(string name, Transform parent = null) { var g = new GameObject(name).transform; g.SetParent(parent ? parent : root, false); return g; }

    static GameObject Put(string path, Vector3 pos, float yaw, Transform parent, float height = 0f, float uniform = 1f, bool remap = false)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!prefab) { log.AppendLine("  ERROR: missing " + path); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0) * prefab.transform.localRotation);
        go.transform.localScale = prefab.transform.localScale * uniform;
        if (height > 0f)
        {
            var b = Bounds(go); if (b.size.y > 0.001f) go.transform.localScale *= height / b.size.y;
            b = Bounds(go); go.transform.position += Vector3.up * (pos.y - b.min.y);
        }
        if (remap)
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(m => m ? M(m.name.Split(' ')[0].Split('.')[0]) : M("M_Wood")).ToArray();
        return go;
    }
    static GameObject K(string name, Vector3 pos, float yaw, Transform parent, Vector3? scale = null)
    {
        var go = Put($"{Kit}/{name}.fbx", pos, yaw, parent, 0f, 1f, true);
        if (go && scale.HasValue) go.transform.localScale = Vector3.Scale(go.transform.localScale, scale.Value);
        return go;
    }
    static GameObject N(string name, Vector3 pos, float yaw, Transform parent, float height)
    {
        var go = Put($"{Nature}/{name}.fbx", pos, yaw, parent, height);
        if (go && name.StartsWith("Rock_"))
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterials = r.sharedMaterials.Select(_ => M("M_HubF_Rock")).ToArray();
        return go;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
    }

    /// Куб с тайлингом текстуры по метрам (через MaterialPropertyBlock), с BoxCollider.
    static GameObject Box(string name, Vector3 center, Vector3 size, string mat, Transform parent, bool collider = true, float tilePerM = 0.5f, Quaternion? rot = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(center, rot ?? Quaternion.identity); go.transform.localScale = size;
        var r = go.GetComponent<Renderer>(); r.sharedMaterial = M(mat);
        if (tilePerM > 0f)
        {
            var mpb = new MaterialPropertyBlock();
            float u = Mathf.Max(size.x, size.z) * tilePerM, v = Mathf.Max(0.2f, size.y) * tilePerM;
            if (size.y < 0.3f) { u = size.x * tilePerM; v = size.z * tilePerM; }
            mpb.SetVector("_BaseMap_ST", new Vector4(Mathf.Max(0.2f, u), Mathf.Max(0.2f, v), 0, 0)); r.SetPropertyBlock(mpb);
        }
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.layer = 0; return go;
    }
    static GameObject Blob(string name, Vector3 center, Vector3 size, string mat, Transform parent, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name; go.transform.SetParent(parent, false);
        go.transform.position = center; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = M(mat);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
        return go;
    }
    static TextMeshPro Text(string name, string text, Vector3 pos, Quaternion rot, float size, Color c, Transform parent, Vector2 rect)
    {
        var go = new GameObject(name, typeof(TextMeshPro)); go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshPro>(); t.text = text; t.fontSize = size; t.color = c; t.alignment = TextAlignmentOptions.Center;
        t.fontStyle = FontStyles.Bold; t.rectTransform.sizeDelta = rect; go.transform.SetPositionAndRotation(pos, rot); return t;
    }
    static void PointLight(string name, Vector3 pos, Color c, float range, float intensity, Transform parent)
    {
        var l = new GameObject(name, typeof(Light)).GetComponent<Light>(); l.transform.SetParent(parent, false); l.transform.position = pos;
        l.type = LightType.Point; l.color = c; l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
    }
    static float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
    /// Запрет на коридор салона автобуса (BlackBusBuilder): x 2.8…5.6, z 7.3…18.2, y 0…2.6.
    static bool BlocksBus(Bounds b) =>
        b.max.x > 2.6f && b.min.x < 5.8f && b.max.z > 13.6f && b.min.z < 18.6f && b.min.y < 2.8f && b.max.y > 0.05f   // салон за стеной
        || b.max.x > 0.6f && b.min.x < 7.8f && b.max.z > 6.5f && b.min.z < 13.8f && b.max.y > 0.05f;                 // ворота и площадка автобуса
    static bool InBusCorridor(Vector3 p, float r = 0.5f) => p.x > 2.8f - r && p.x < 5.6f + r && p.z > 7.3f - r && p.z < 18.2f + r;

    // ================================================================== 1. зачистка старой коробки
    static void HideOldBox()
    {
        var keepStatic = new HashSet<string> { "Wall_N", "Wall_N_Colliders", "Hub_BasketballHoop", "HoopTitle", "HoopScoreBack", "FreeThrowLine", "BallStand", "BallCup",
            "ModeBoard_Back", "ModeBoard_FrameT", "ModeBoard_FrameB", "Hub_PlanningTable", "ModeSign_Back", "ModeSign_Post", "ModeSign", "Hub_WardrobeSafe", "HubTitle", "HubTitleBack", "RulesSign" };
        int hidden = 0;
        var st = GameObject.Find("MainHubMap/Static");
        if (st) foreach (Transform c in st.transform) if (!keepStatic.Contains(c.name) && c.gameObject.activeSelf) { c.gameObject.SetActive(false); hidden++; }
        var lights = GameObject.Find("MainHubMap/Lights"); if (lights && lights.activeSelf) { lights.SetActive(false); hidden++; }
        foreach (var p in new[] { "HubDecor/Static", "HubDecor/Lighting" }) { var g = GameObject.Find(p); if (g && g.activeSelf) { g.SetActive(false); hidden++; } }
        // кабели табло висели на потолке — потолка больше нет
        if (st) foreach (Transform c in st.transform) if (c.name == "ModeBoard_Cable") c.gameObject.SetActive(false);
        // табличка FreeThrowLine — поверх нового асфальта
        var ftl = st ? st.transform.Find("FreeThrowLine") : null; if (ftl) ftl.position = new Vector3(ftl.position.x, 0.035f, ftl.position.z);
        // Post-FX: без «кислотного» Bloom
        var fx = GameObject.Find("HubDecor/HubPostFX");
        if (fx && fx.TryGetComponent<Volume>(out var vol)) vol.weight = 0.45f;
        log.AppendLine($"  old box hidden: {hidden} groups/objects (kept: bus tunnel wall + colliders, hoop, mode board, planning table, wardrobe safe)");
    }

    // стол планирования (ModeConsole + стол + табличка режима) — к компьютеру, лицом в двор
    static void MovePlanningToHQ()
    {
        var console = GameObject.Find("MainHubMap/Dynamic/ModeConsole");
        if (!console) { log.AppendLine("  ERROR: ModeConsole not found"); return; }
        if (console.transform.position.z < -5f) { log.AppendLine("  planning table already in HQ"); return; }
        var st = GameObject.Find("MainHubMap/Static").transform;
        var group = new List<Transform> { console.transform };
        foreach (Transform c in st) if (c.name == "Hub_PlanningTable" || c.name.StartsWith("ModeSign")) group.Add(c);
        Vector3 pivotOld = new Vector3(0, 0, 1.95f), pivotNew = new Vector3(-1.3f, 0, -9.55f);
        var rot = Quaternion.Euler(0, 180, 0);
        foreach (var t in group) { t.position = pivotNew + rot * (t.position - pivotOld); t.rotation = rot * t.rotation; }
        log.AppendLine($"  planning table moved to HQ ({group.Count} objects), console now at {console.transform.position}");
    }

    // ================================================================== 2. атмосфера
    static void Atmosphere()
    {
        var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Bank/M_Bank_SunsetSky.mat");
        if (sky) RenderSettings.skybox = sky; else log.AppendLine("  ERROR: sunset sky material missing");
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.78f, 0.62f, 0.78f);
        RenderSettings.ambientEquatorColor = new Color(0.66f, 0.5f, 0.46f);
        RenderSettings.ambientGroundColor = new Color(0.22f, 0.2f, 0.18f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.72f, 0.52f, 0.6f); RenderSettings.fogStartDistance = 30f; RenderSettings.fogEndDistance = 120f;
        RenderSettings.reflectionIntensity = 0.4f;
        var sunGo = GameObject.Find("Directional Light");
        var sun = sunGo ? sunGo.GetComponent<Light>() : null;
        if (sun)
        {
            sun.color = new Color(1f, 0.7f, 0.48f); sun.intensity = 1.15f; sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.65f;
            sun.transform.rotation = Quaternion.Euler(16f, 58f, 0f);       // низкое солнце с юго-запада, тёплые длинные тени
            RenderSettings.sun = sun;
        }
        else log.AppendLine("  ERROR: Directional Light not found");
        DynamicGI.UpdateEnvironment();
    }

    // ================================================================== земля, дорожки
    static void Ground()
    {
        var g = Group("Ground");
        Box("Grass", new Vector3(0, -0.2f, -2f), new Vector3(130, 0.4f, 130), "M_HubF_Grass", g, true, 0f);
        Box("Path_SpawnToFire", new Vector3(0, 0.012f, -3f), new Vector3(2.2f, 0.02f, 8.5f), "M_HubF_Gravel", g, false, 0.6f);
        var toGate = Box("Path_FireToGate", new Vector3(2.2f, 0.012f, 5.6f), new Vector3(2.4f, 0.02f, 6.4f), "M_HubF_Gravel", g, false, 0.6f);
        toGate.transform.rotation = Quaternion.Euler(0, 20f, 0);
        Box("BusPad", new Vector3(4.2f, 0.01f, 11f), new Vector3(7f, 0.02f, 6.4f), "M_HubF_Gravel", g, false, 0.6f);
        Box("Path_ToWardrobe", new Vector3(-5f, 0.011f, 1.5f), new Vector3(8f, 0.02f, 1.8f), "M_HubF_Dirt", g, false, 0.5f);
        Box("Path_ToCourt", new Vector3(4.5f, 0.011f, -3.5f), new Vector3(5f, 0.02f, 1.6f), "M_HubF_Dirt", g, false, 0.5f).transform.rotation = Quaternion.Euler(0, -25f, 0);
        Box("WardrobeFloor", new Vector3(-10f, 0.06f, 2f), new Vector3(4.6f, 0.12f, 13.4f), "M_Wood", g, true, 0.5f);
        Box("HQFloor", new Vector3(-4.6f, 0.04f, -9.6f), new Vector3(12.2f, 0.08f, 3.6f), "M_HubF_Stone", g, true, 0.5f);
    }

    // ================================================================== граница: скала с тоннелем, холмы, камни
    static void Boundary()
    {
        var g = Group("Boundary");
        // северная стена с тоннелем — это скала (стена нужна Stencil-порталу автобуса, её не трогаем, только материал)
        var wall = GameObject.Find("BusTunnel/NorthWallWithMouth");
        if (wall) foreach (var r in wall.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = r.sharedMaterials.Select(_ => M("M_HubF_Cliff")).ToArray();
        // большие валуны: по бокам тоннеля, на гребне скалы и кольцом вокруг двора
        string[] rocks = { "Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3" };
        var ridge = new List<(Vector3 p, float h)>
        {
            (new Vector3(-16f, 0, 13.5f), 9f), (new Vector3(-21f, 0, 9f), 8f), (new Vector3(16f, 0, 13.5f), 9f), (new Vector3(21.5f, 0, 9f), 8.5f),
            (new Vector3(-8f, 6.5f, 15f), 4.5f), (new Vector3(-2f, 7f, 15.5f), 3.5f), (new Vector3(10f, 6.5f, 15f), 4.5f), (new Vector3(-12.5f, 0, 15f), 8f), (new Vector3(13f, 0, 15.5f), 8.5f),
        };
        for (int i = 0; i < 26; i++)
        {
            float a = Mathf.Lerp(-20f, 200f, i / 25f) * Mathf.Deg2Rad;       // юг и бока (север — скала)
            float rx = 24f + R(-1.5f, 2f), rz = 22f + R(-1.5f, 2f);
            var p = new Vector3(Mathf.Cos(a + Mathf.PI) * rx, 0, Mathf.Sin(a + Mathf.PI) * rz - 2f);
            if (p.z > 8f) continue;
            ridge.Add((p, R(4f, 8.5f)));
        }
        int n = 0;
        foreach (var (p, h) in ridge)
        {
            if (InBusCorridor(p, 2f)) continue;
            var r = N(rocks[n % 3], p, R(0, 360), g, h);
            if (r) r.transform.localScale = Vector3.Scale(r.transform.localScale, new Vector3(R(1.0f, 1.35f), 1f, R(1.0f, 1.3f)));
            n++;
        }
        // холмы за камнями — край карты не виден
        for (int i = 0; i < 14; i++)
        {
            float a = i / 14f * Mathf.PI * 2f;
            var p = new Vector3(Mathf.Cos(a) * 38f, -3f, Mathf.Sin(a) * 34f - 2f);
            Blob("Hill", p, new Vector3(R(22, 34), R(10, 16), R(18, 28)), "M_HubF_Hill", g, false);
        }
        Blob("Hill_N", new Vector3(0, -2f, 30f), new Vector3(70, 22, 22), "M_HubF_Hill", g, false);
        int nudged = 0;
        foreach (Transform t in g)
        {
            // коридор салона автобуса + проезд к воротам (x 0…8.5, z < 13.6) держим свободными: двигаем валун в сторону от оси x = 4.2
            for (int k = 0; k < 40 && BlocksBus(Bounds(t.gameObject)); k++) { t.position += new Vector3(t.position.x < 4.2f ? -0.75f : 0.75f, 0, 0); if (k == 0) nudged++; }
        }
        log.AppendLine($"  boundary: nudged away from bus {nudged};");
        log.AppendLine($"  boundary: {n} boulders, 15 hills, cliff material on tunnel wall: {(wall ? "ok" : "ERROR no NorthWallWithMouth")}");
    }

    // ================================================================== 3. зона гардероба (запад): стена, навес, гирлянда
    static void Wardrobe()
    {
        var g = Group("Wardrobe");
        Box("BackWall", new Vector3(-12.35f, 1.85f, 2f), new Vector3(0.3f, 3.7f, 13.4f), "M_Wood", g, true, 0.5f);
        Box("BackWallTrim", new Vector3(-12.3f, 3.72f, 2f), new Vector3(0.45f, 0.14f, 13.6f), "M_WoodDark", g, true, 0.5f);
        foreach (float z in new[] { -4.6f, -0.2f, 4.2f, 8.6f }) K("Hub_Post_3", new Vector3(-7.85f, 0.12f, z), 0, g, new Vector3(1, 1.08f, 1));
        Box("FrontBeam", new Vector3(-7.85f, 3.28f, 2f), new Vector3(0.24f, 0.24f, 13.6f), "M_WoodDark", g, true, 0.5f);
        for (int i = 0; i < 5; i++)
        {
            var roof = K("Hub_Roof_3x3", new Vector3(-10.1f, 3.55f, -3.6f + i * 2.8f), 90f, g, new Vector3(1f, 1f, 1.55f));
            if (roof) roof.transform.rotation = Quaternion.Euler(0, 90f, -9f) * roof.transform.rotation;
        }
        for (int i = 0; i < 3; i++) K("Hub_StringLights_4", new Vector3(-7.95f, 3.1f, -2.6f + i * 4.2f), 90f, g);
        Text("WardrobeTitle", "ГАРДЕРОБ", new Vector3(-7.72f, 3.28f, 2f), Quaternion.Euler(0, -90, 0), 3f, new Color(1f, 0.85f, 0.55f), g, new Vector2(4, 0.4f));
        PointLight("WardrobeLight", new Vector3(-9.6f, 2.9f, 2f), new Color(1f, 0.78f, 0.5f), 10f, 1.6f, g);
        K("Hub_Crate", new Vector3(-11.6f, 0.12f, -4.0f), 15f, g); K("Hub_Crate", new Vector3(-11.5f, 0.92f, -3.9f), 40f, g, new Vector3(0.7f, 0.7f, 0.7f));
    }

    // ================================================================== Штаб планирования (юго-запад): кирпичная стена, навес, доска
    static void HeadQuarters()
    {
        var g = Group("HQ");
        Box("BrickWall", new Vector3(-4.6f, 1.7f, -11.45f), new Vector3(12.4f, 3.4f, 0.35f), "M_Brick", g, true, 0.5f);
        Box("BrickCap", new Vector3(-4.6f, 3.47f, -11.45f), new Vector3(12.6f, 0.14f, 0.5f), "M_HubF_Stone", g, true, 0.5f);
        foreach (float x in new[] { -10.5f, -6.8f, -2.6f, 1.3f }) K("Hub_Post_3", new Vector3(x, 0.08f, -7.85f), 0, g);
        Box("FrontBeam", new Vector3(-4.6f, 3.0f, -7.85f), new Vector3(12.4f, 0.24f, 0.24f), "M_WoodDark", g, true, 0.5f);
        for (int i = 0; i < 4; i++)
        {
            var roof = K("Hub_Roof_3x3", new Vector3(-9.1f + i * 3.1f, 3.3f, -9.6f), 0f, g, new Vector3(1.05f, 1f, 1.25f));
            if (roof) roof.transform.rotation = Quaternion.Euler(8f, 0, 0) * roof.transform.rotation;
        }
        // доска с картой банка (план ограбления)
        Box("Board", new Vector3(-9.0f, 1.9f, -11.22f), new Vector3(2.6f, 1.5f, 0.06f), "M_HubF_Cork", g, true, 0.6f);
        Box("BoardFrame", new Vector3(-9.0f, 1.9f, -11.25f), new Vector3(2.8f, 1.7f, 0.04f), "M_WoodDark", g, false, 0.5f);
        Box("Map", new Vector3(-9.25f, 1.85f, -11.18f), new Vector3(1.5f, 1.05f, 0.01f), "M_HubF_Paper", g, false, 0f);
        foreach (var (x, y, w, h) in new[] { (-9.6f, 2.05f, 0.6f, 0.35f), (-8.95f, 2.05f, 0.5f, 0.35f), (-9.3f, 1.6f, 1.1f, 0.3f), (-8.75f, 1.6f, 0.2f, 0.3f) })
            Box("MapRoom", new Vector3(x, y, -11.17f), new Vector3(w, h, 0.005f), "M_HubF_MapInk", g, false, 0f);
        Box("TargetPin", new Vector3(-8.8f, 1.58f, -11.16f), new Vector3(0.08f, 0.08f, 0.02f), "M_HubF_Red", g, false, 0f);
        for (int i = 0; i < 3; i++) Box("Photo", new Vector3(-8.1f, 2.3f - i * 0.38f, -11.18f), new Vector3(0.36f, 0.28f, 0.01f), "M_HubF_Paper", g, false, 0f);
        Text("BoardTitle", "ПЛАН: SUNSET TRUST BANK", new Vector3(-9.0f, 2.52f, -11.17f), Quaternion.Euler(0, 180, 0), 1.6f, new Color(0.15f, 0.1f, 0.08f), g, new Vector2(2.4f, 0.3f));
        Text("HQTitle", "ШТАБ", new Vector3(-4.6f, 3.0f, -7.71f), Quaternion.Euler(0, 180, 0), 3f, new Color(1f, 0.85f, 0.55f), g, new Vector2(3, 0.4f));
        PointLight("HQLight", new Vector3(-4.4f, 2.7f, -9.4f), new Color(1f, 0.8f, 0.55f), 9f, 1.5f, g);
        K("Hub_Crate", new Vector3(0.9f, 0.08f, -10.7f), 10f, g);
    }

    // ================================================================== Зона выезда: ворота с табло режима, забор, автобус у тоннеля
    static void Departure()
    {
        var g = Group("Departure");
        K("Hub_Post_3", new Vector3(1.45f, 0, 8.95f), 0, g, new Vector3(1.3f, 1.5f, 1.3f));
        K("Hub_Post_3", new Vector3(6.95f, 0, 8.95f), 0, g, new Vector3(1.3f, 1.5f, 1.3f));
        Box("GateBeam", new Vector3(4.2f, 4.42f, 8.95f), new Vector3(6.1f, 0.26f, 0.3f), "M_WoodDark", g, true, 0.5f);
        Text("GateTitle", "НА ДЕЛО", new Vector3(4.2f, 4.42f, 8.79f), Quaternion.identity, 2.2f, new Color(1f, 0.85f, 0.5f), g, new Vector2(5, 0.3f));
        // забор вдоль скалы, с проходом к воротам
        for (float x = -11f; x <= -0.5f; x += 2f) K("Hub_Fence_2", new Vector3(x, 0, 9.7f), 0, g);
        for (float x = 9f; x <= 13f; x += 2f) K("Hub_Fence_2", new Vector3(x, 0, 9.7f), 0, g);
        foreach (var p in new[] { new Vector3(0.2f, 0, 8.3f), new Vector3(8.2f, 0, 8.4f) }) N("Bush_Common", p, R(0, 360), g, 1.1f);
    }

    // ================================================================== Зона отдыха: костёр с брёвнами
    static void Campfire()
    {
        var g = Group("Campfire");
        var c = new Vector3(0f, 0f, 1.6f);
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder); disc.name = "DirtCircle"; disc.transform.SetParent(g, false);
        disc.transform.position = c + Vector3.up * 0.01f; disc.transform.localScale = new Vector3(6.6f, 0.01f, 6.6f);
        disc.GetComponent<Renderer>().sharedMaterial = M("M_HubF_Dirt"); Object.DestroyImmediate(disc.GetComponent<Collider>());
        var fire = K("Hub_Campfire", c, 0, g);
        // язычки пламени (эмиссия, без теней)
        var flames = new List<Renderer>();
        for (int i = 0; i < 3; i++)
        {
            var f = GameObject.CreatePrimitive(PrimitiveType.Sphere); f.name = "Flame"; f.transform.SetParent(g, false);
            f.transform.position = c + new Vector3(Mathf.Cos(i * 2.1f) * 0.1f, 0.35f + i * 0.08f, Mathf.Sin(i * 2.1f) * 0.1f);
            f.transform.localScale = new Vector3(0.16f - i * 0.03f, 0.42f - i * 0.08f, 0.16f - i * 0.03f);
            var fr = f.GetComponent<Renderer>(); fr.sharedMaterial = M("M_HubF_Flame"); fr.shadowCastingMode = ShadowCastingMode.Off;
            Object.DestroyImmediate(f.GetComponent<Collider>()); flames.Add(fr);
        }
        var lightGo = new GameObject("FireLight", typeof(Light)); lightGo.transform.SetParent(g, false); lightGo.transform.position = c + Vector3.up * 0.9f;
        var l = lightGo.GetComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.22f); l.range = 8f; l.intensity = 2.2f; l.shadows = LightShadows.None;
        var flick = g.gameObject.AddComponent<CampfireFlicker>(); flick.fireLight = l;
        flick.emberRenderers = flames.Concat(fire ? fire.GetComponentsInChildren<Renderer>() : new Renderer[0]).ToArray();
        // брёвна-лавки вокруг
        for (int i = 0; i < 4; i++)
        {
            float a = (i * 90f + 45f) * Mathf.Deg2Rad;
            var p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 2.3f;
            K("Hub_Log_3", p, -(i * 90f + 45f) + 90f, g, new Vector3(0.75f, 1f, 1f));
        }
    }

    // ================================================================== баскетбол: асфальт и щит табло
    static void Basketball()
    {
        var g = Group("Court");
        Box("Asphalt", new Vector3(9.4f, 0.015f, -6.7f), new Vector3(6.4f, 0.03f, 7.2f), "M_HubF_Asphalt", g, true, 0.3f);
        Box("CourtLineL", new Vector3(6.3f, 0.034f, -6.7f), new Vector3(0.08f, 0.01f, 7.0f), "M_HubF_Line", g, false, 0f);
        Box("CourtLineB", new Vector3(9.4f, 0.034f, -10.25f), new Vector3(6.2f, 0.01f, 0.08f), "M_HubF_Line", g, false, 0f);
        Box("ScoreBoardBack", new Vector3(12.05f, 4.75f, -6f), new Vector3(0.2f, 2.3f, 5.4f), "M_WoodDark", g, true, 0.5f);
        K("Hub_Post_3", new Vector3(12.1f, 0, -8.5f), 0, g, new Vector3(1, 1.3f, 1));
        K("Hub_Post_3", new Vector3(12.1f, 0, -3.5f), 0, g, new Vector3(1, 1.3f, 1));
    }

    // ================================================================== паркур-вышка в северо-восточном углу
    static void Watchtower()
    {
        var g = Group("Watchtower");
        var c = new Vector3(15f, 0, 3.5f);
        foreach (var (dx, dz) in new[] { (-1.2f, -1.2f), (1.2f, -1.2f), (-1.2f, 1.2f), (1.2f, 1.2f) })
            K("Hub_Post_3", c + new Vector3(dx, 0, dz), 0, g, new Vector3(1.2f, 2.45f, 1.2f));
        K("Hub_Deck_2x2", c + new Vector3(0, 2.4f, 0), 0, g, new Vector3(1.3f, 1f, 1.3f));
        K("Hub_Deck_2x2", c + new Vector3(0, 5.0f, 0), 0, g, new Vector3(1.3f, 1f, 1.3f));
        // перила верхней площадки
        foreach (var (p, yaw) in new[] { (new Vector3(0, 5.2f, 1.25f), 0f), (new Vector3(1.25f, 5.2f, 0), 90f), (new Vector3(-1.25f, 5.2f, 0), 90f) })
            K("Hub_Fence_2", c + p, yaw, g, new Vector3(1.25f, 1f, 1f));
        // лестницы (с юга: земля → 1-я площадка, 1-я → 2-я)
        K("Hub_Ladder_3", c + new Vector3(0.4f, 0, -1.45f), 0, g, new Vector3(1, 0.95f, 1));
        K("Hub_Ladder_3", c + new Vector3(-0.4f, 2.6f, -1.45f), 0, g, new Vector3(1, 0.95f, 1));
        // раскосы — за них удобно цепляться руками
        foreach (var (a, b) in new[] { (new Vector3(-1.2f, 0.3f, -1.2f), new Vector3(1.2f, 2.3f, -1.2f)), (new Vector3(1.2f, 0.3f, 1.2f), new Vector3(-1.2f, 2.3f, 1.2f)),
                                       (new Vector3(-1.2f, 0.3f, 1.2f), new Vector3(-1.2f, 2.3f, -1.2f)), (new Vector3(1.2f, 2.8f, -1.2f), new Vector3(1.2f, 4.9f, 1.2f)) })
        {
            var p0 = c + a; var p1 = c + b; var mid = (p0 + p1) / 2;
            Box("Brace", mid, new Vector3(0.12f, 0.12f, (p1 - p0).magnitude), "M_WoodDark", g, true, 0.5f, Quaternion.LookRotation(p1 - p0));
        }
        var roof = K("Hub_Roof_3x3", c + new Vector3(0, 7.45f, 0), 0, g, new Vector3(1.1f, 1f, 1.05f));
        Box("Lantern", c + new Vector3(0, 6.9f, 0), new Vector3(0.25f, 0.35f, 0.25f), "M_Bulb", g, false, 0f);
        Text("TowerSign", "ДОЗОР", c + new Vector3(0, 2.15f, -1.38f), Quaternion.identity, 1.6f, new Color(1f, 0.85f, 0.55f), g, new Vector2(2, 0.3f));
    }

    // ================================================================== паркур: настилы, брёвна, каменные уступы
    static void Parkour()
    {
        var g = Group("Parkour");
        // настилы на столбах (северо-запад, между гардеробом и скалой)
        foreach (var (p, h) in new[] { (new Vector3(-5.5f, 0, 6.2f), 1.1f), (new Vector3(-2.8f, 0, 7.6f), 1.9f), (new Vector3(-8.6f, 0, 11.2f), 2.6f) })
        {
            K("Hub_Deck_2x2", p + Vector3.up * h, 0, g);
            foreach (var (dx, dz) in new[] { (-0.85f, -0.85f), (0.85f, -0.85f), (-0.85f, 0.85f), (0.85f, 0.85f) })
                K("Hub_Post_3", p + new Vector3(dx, 0, dz), 0, g, new Vector3(0.7f, (h + 0.05f) / 3f, 0.7f));
        }
        // поваленные брёвна: с земли на настил, между настилами, у костра
        LogBetween(g, new Vector3(-6.5f, 0.05f, 3.6f), new Vector3(-5.5f, 1.15f, 5.4f));
        LogBetween(g, new Vector3(-4.6f, 1.2f, 6.6f), new Vector3(-3.6f, 1.95f, 7.4f));
        LogBetween(g, new Vector3(-2.2f, 1.95f, 8.4f), new Vector3(-7.7f, 2.6f, 10.7f));
        LogBetween(g, new Vector3(5.8f, 0.1f, 4.6f), new Vector3(8.4f, 0.1f, 2.2f));
        // каменные уступы к вышке и вдоль восточного края
        foreach (var (p, h) in new[] { (new Vector3(12.5f, 0, 0.2f), 0.9f), (new Vector3(13.2f, 0, -1.6f), 1.6f), (new Vector3(11.4f, 0, 6.8f), 1.3f), (new Vector3(-13.8f, 0, 10.6f), 2.2f), (new Vector3(-11.4f, 0, 12.2f), 3.4f) })
            N(new[] { "Rock_Medium_1", "Rock_Medium_2", "Rock_Medium_3" }[(int)(h * 10) % 3], p, R(0, 360), g, h);
    }
    static void LogBetween(Transform g, Vector3 a, Vector3 b)
    {
        var log3 = K("Hub_Log_3", (a + b) / 2 - Vector3.up * 0.21f, 0, g);
        if (!log3) return;
        var d = b - a; float len = d.magnitude;
        log3.transform.rotation = Quaternion.FromToRotation(Vector3.right, d.normalized) * log3.transform.rotation;
        log3.transform.localScale = Vector3.Scale(log3.transform.localScale, new Vector3(len / 3f, 1, 1));
    }

    // ================================================================== лес и мелкая зелень
    static void Greenery()
    {
        var g = Group("Forest");
        var props = Group("Plants");
        string[] pines = { "Pine_1", "Pine_2", "Pine_3", "Pine_4", "Pine_5" }, commons = { "CommonTree_1", "CommonTree_2", "CommonTree_3", "CommonTree_4", "CommonTree_5" };
        int trees = 0;
        for (int ring = 0; ring < 3; ring++)
            for (int i = 0; i < 26 + ring * 6; i++)
            {
                float a = (i + R(-0.3f, 0.3f)) / (26f + ring * 6) * Mathf.PI * 2f;
                float rx = 27f + ring * 6f + R(-2f, 2f), rz = 24f + ring * 6f + R(-2f, 2f);
                var p = new Vector3(Mathf.Cos(a) * rx, 0, Mathf.Sin(a) * rz - 2f);
                if (p.z > 11f && Mathf.Abs(p.x) < 14f) p.z += 8f;                // за скалой — подальше
                if (InBusCorridor(p, 3f)) continue;
                bool pine = (i + ring) % 3 != 0;
                var t = N(pine ? pines[i % 5] : commons[i % 5], p, R(0, 360), g, pine ? R(10f, 16f) : R(8f, 12f));
                if (t) { t.name = (pine ? "Pine" : "Tree") + "_ring"; trees++; }
            }
        // несколько деревьев внутри двора (на них тоже можно залезть)
        foreach (var (p, name, h) in new[] { (new Vector3(-15.5f, 0, -6f), "CommonTree_2", 9f), (new Vector3(-16f, 0, 4f), "Pine_2", 12f), (new Vector3(17.5f, 0, -9f), "CommonTree_4", 8.5f),
                                             (new Vector3(6.5f, 0, -14.5f), "TwistedTree_1", 7.5f), (new Vector3(-7f, 0, -15f), "Pine_4", 11f), (new Vector3(17f, 0, 9.5f), "TwistedTree_3", 8f) })
        { var t = N(name, p, R(0, 360), g, h); if (t) { t.name = "Tree_yard"; trees++; } }
        // кусты, папоротники, трава, цветы — по краям двора (без коллайдеров)
        string[] small = { "Bush_Common", "Bush_Common_Flowers", "Fern_1", "Grass_Common_Tall", "Grass_Wispy_Tall", "Flower_3_Group", "Flower_4_Group", "Plant_1", "Plant_7", "Mushroom_Common" };
        float[] hs = { 1.0f, 1.0f, 0.8f, 0.45f, 0.5f, 0.35f, 0.35f, 0.6f, 0.6f, 0.25f };
        int plants = 0;
        for (int i = 0; i < 140; i++)
        {
            float a = R(0, Mathf.PI * 2f), rr = R(12.5f, 22f);
            var p = new Vector3(Mathf.Cos(a) * rr * 1.05f, 0, Mathf.Sin(a) * rr - 2f);
            if (p.z > 9.5f || InBusCorridor(p, 1f)) continue;
            int k = i % small.Length;
            var pl = N(small[k], p, R(0, 360), props, hs[k] * R(0.8f, 1.4f)); if (pl) plants++;
        }
        // грибы и травка у костра, папоротники у гардероба
        foreach (var p in new[] { new Vector3(-2.9f, 0, 3.9f), new Vector3(2.7f, 0, -0.8f), new Vector3(-7.4f, 0, -5.4f), new Vector3(-7.4f, 0, 9.2f) }) { N("Fern_1", p, R(0, 360), props, 0.9f); plants++; }
        log.AppendLine($"  forest: {trees} trees, {plants} small plants");
    }

    // ================================================================== коллайдеры (слой Default)
    static void AddColliders()
    {
        int n = 0;
        var plants = root.Find("Plants");
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            var go = mf.gameObject;
            if (go.GetComponent<Collider>() || !mf.sharedMesh) continue;
            if (plants && go.transform.IsChildOf(plants)) continue;           // трава и кусты — без физики
            if (go.name.StartsWith("Flame") || go.GetComponentInParent<TextMeshPro>()) continue;
            bool skip = false;
            for (var t = go.transform; t; t = t.parent)
                if (t.name.EndsWith("_ring") || t.name.StartsWith("Hub_StringLights") || t.name == "Hill") skip = true;   // дальние деревья — капсулы ниже
            if (skip) continue;
            var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; go.layer = 0; n++;
        }
        // дальние деревья: капсула на ствол (на всякий случай, если игрок перелезет камни)
        foreach (Transform t in root.Find("Forest"))
        {
            if (!t.name.EndsWith("_ring")) continue;
            var b = Bounds(t.gameObject);
            var cap = t.gameObject.AddComponent<CapsuleCollider>();
            cap.center = t.InverseTransformPoint(new Vector3(b.center.x, b.min.y + b.size.y * 0.3f, b.center.z));
            cap.radius = 0.4f / Mathf.Max(0.01f, t.lossyScale.x); cap.height = b.size.y * 0.6f / Mathf.Max(0.01f, t.lossyScale.y); n++;
        }
        log.AppendLine($"  colliders added: {n}");
    }

    // ================================================================== проверки
    static void Checks()
    {
        var keys = Object.FindObjectsByType<ComputerKey>(FindObjectsInactive.Include).Length;
        var skins = Object.FindObjectsByType<SkinButton>(FindObjectsInactive.Include).Length;
        bool comp = Object.FindAnyObjectByType<GrabzComputer>(FindObjectsInactive.Include);
        bool van = Object.FindAnyObjectByType<VanPortal>(FindObjectsInactive.Include);
        bool sync = Object.FindAnyObjectByType<HubVanSync>(FindObjectsInactive.Include);
        bool fusion = Object.FindAnyObjectByType<FusionNetworkManager>(FindObjectsInactive.Include);
        bool hoop = Object.FindAnyObjectByType<BasketballHoop>(FindObjectsInactive.Include);
        var mirror = GameObject.Find("MainHubMap/Dynamic/Mirror"); var showcase = GameObject.Find("MainHubMap/Dynamic/CosmeticShowcase");
        log.AppendLine($"  check: computer {comp}, keys {keys}, skin buttons {skins}, mirror {(bool)mirror}, showcase {(bool)showcase}, VanPortal {van}, HubVanSync {sync}, Fusion {fusion}, hoop {hoop}");
        if (keys != 44 || skins != 6 || !comp || !van || !sync || !fusion || !hoop || !mirror || !showcase) log.AppendLine("  ERROR: a functional object is missing");
        // ничего не попало в коридор салона автобуса
        var badR = root.GetComponentsInChildren<Renderer>().Where(r => r.bounds.max.y > 0.05f && r.bounds.min.y < 2.6f && r.bounds.max.x > 2.8f && r.bounds.min.x < 5.6f && r.bounds.max.z > 13.9f && r.bounds.min.z < 18.2f).ToList();
        int bad = badR.Count;
        log.AppendLine($"  bus corridor intrusions (behind the wall): {bad}" + (bad > 0 ? "  ERROR " + string.Join(", ", badR.Select(r => $"{r.transform.parent?.name}/{r.name} {r.bounds.min}..{r.bounds.max}")) : ""));
        // все игровые объекты активны
        foreach (var go in new[] { mirror, showcase, GameObject.Find("MainHubMap/Dynamic/GrabzComputer"), GameObject.Find("MainHubMap/Dynamic/SkinTerminal"), GameObject.Find("MainHubMap/Dynamic/ModeConsole") })
            if (go && !go.activeInHierarchy) log.AppendLine($"  ERROR: {go.name} is inactive");
    }

    // ================================================================== 6 обзорных снимков
    static void RenderShots()
    {
        Directory.CreateDirectory("HubPreview");
        var shots = new (string name, Vector3 pos, Vector3 look, float fov)[]
        {
            ("Forest_1_Overview", new Vector3(16f, 22f, -26f), new Vector3(0f, 0f, 0f), 55f),
            ("Forest_2_Wardrobe_HQ", new Vector3(-1.5f, 2.2f, -3.5f), new Vector3(-10f, 1.4f, -4f), 70f),
            ("Forest_3_Computer", new Vector3(-3.2f, 1.7f, -6.2f), new Vector3(-5.5f, 1.0f, -9.4f), 65f),
            ("Forest_4_Campfire", new Vector3(3.6f, 1.7f, -2.2f), new Vector3(0f, 0.5f, 1.6f), 62f),
            ("Forest_5_Tower", new Vector3(7.5f, 2.2f, -4.5f), new Vector3(15f, 3.8f, 3.5f), 62f),
            ("Forest_6_BusGate", new Vector3(1.0f, 1.7f, -1.5f), new Vector3(4.2f, 2.2f, 10.5f), 64f),
        };
        var camGo = new GameObject("ShotCam"); var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox; cam.nearClipPlane = 0.05f; cam.farClipPlane = 400f;
        var rt = new RenderTexture(1600, 900, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
        cam.transform.SetPositionAndRotation(shots[0].pos, Quaternion.LookRotation(shots[0].look - shots[0].pos));
        for (int i = 0; i < 2; i++) cam.Render();      // прогрев: первый кадр в batchmode без света
        foreach (var s in shots)
        {
            cam.fieldOfView = s.fov; cam.transform.position = s.pos; cam.transform.LookAt(s.look);
            cam.Render(); RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            File.WriteAllBytes($"HubPreview/{s.name}.png", tex.EncodeToPNG()); Object.DestroyImmediate(tex);
            log.AppendLine($"  shot HubPreview/{s.name}.png");
        }
        RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(camGo);
    }
}
