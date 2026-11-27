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
/// Новый чистый банк SUNSET TRUST из CC0-ассетов Quaternius (House Interior, Buildings, Street, Cars, Nature)
/// и модульного кита из Blender (Assets/Art/GeneratedModels/Bank/Modular, исходник — Source/build_bank_modular.py).
/// ЭТАП 1 (этот скрипт): собирает префаб Assets/Art/GeneratedModels/Bank/BankQ/BankQ.prefab — архитектура, декор, свет,
/// коллайдеры (слой Default) и пустые якоря Anchor_* для игровых объектов (двери, лазеры, камеры, лут, эвакуация, спавн),
/// превью-сцену BankQ_Preview.unity (в Build Settings не добавляется) и снимки HubPreview/BankQ_*.png.
/// HeistLevel_Test НЕ трогает — перенос в игру отдельным шагом после одобрения скриншотов.
/// Меню: Tools/Heist/Bank/Quaternius Rebuild (Preview). Batchmode (без -nographics): -executeMethod BankQuaterniusBuilder.RunBatch
/// Повторный запуск пересоздаёт только префаб и превью-сцену.
/// Координаты: X — восток, Z — север, улица на юге (z &lt; 0), пол банка на высоте FL.
/// </summary>
public static class BankQuaterniusBuilder
{
    const string Mod = "Assets/Art/GeneratedModels/Bank/Modular";
    const string Q = "Assets/Art/ThirdParty/Quaternius";
    const string OutDir = "Assets/Art/GeneratedModels/Bank/BankQ";
    const string PrefabPath = OutDir + "/BankQ.prefab";
    const string PreviewScene = OutDir + "/BankQ_Preview.unity";
    public const float FL = 0.72f;          // пол банка (4 ступени по 0.18)
    const float HallH = 7f, LowH = 3.5f;

    static readonly StringBuilder log = new StringBuilder();
    static Transform root, anchors;
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    [MenuItem("Tools/Heist/Bank/Quaternius Rebuild (Preview)")]
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
        log.Clear(); log.AppendLine("[BankQuaterniusBuilder]");
        if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/Art/GeneratedModels/Bank", "BankQ");
        SetupModelImport();
        CreateMaterials();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        root = new GameObject("BankQ").transform;
        anchors = new GameObject("Anchors").transform; anchors.SetParent(root, false);

        BuildStreet();
        BuildFacade();
        BuildHall();
        BuildTellers();
        BuildServiceWing();
        BuildVaultWing();
        BuildGarage();
        BuildDetails();
        BuildLights();
        AddColliders();

        PrefabUtility.SaveAsPrefabAssetAndConnect(root.gameObject, PrefabPath, InteractionMode.AutomatedAction);
        log.AppendLine($"  prefab {PrefabPath}: renderers {root.GetComponentsInChildren<Renderer>().Length}, colliders {root.GetComponentsInChildren<Collider>().Length}, anchors {anchors.childCount}");

        SetupPreviewEnvironment();
        EditorSceneManager.SaveScene(scene, PreviewScene);
        RenderShots();
        return log.ToString();
    }

    // ================================================================== импорт и материалы
    static void SetupModelImport()
    {
        int n = 0;
        foreach (var dir in new[] { Mod, Q + "/HouseInterior", Q + "/Buildings", Q + "/Street", Q + "/Cars" })
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { dir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(p) is not ModelImporter mi) continue;
                bool dirty = false;
                if (mi.importCameras || mi.importLights) { mi.importCameras = false; mi.importLights = false; dirty = true; }
                if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
                if (!mi.isReadable) { mi.isReadable = true; dirty = true; }   // MeshCollider в билде
                if (dirty) { mi.SaveAndReimport(); n++; }
            }
        log.AppendLine($"  model import settings updated: {n}");
    }

    static Material M(string name) => mats.TryGetValue(name, out var m) ? m : mats["M_Wall"];

    static Material MakeMat(string name, Color c, float smooth = 0.15f, float metal = 0f, Color? emission = null, bool transparent = false)
    {
        string path = $"{OutDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
        if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        if (transparent)
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = (int)RenderQueue.Transparent;
        }
        EditorUtility.SetDirty(m);
        mats[name] = m; return m;
    }

    static void CreateMaterials()
    {
        MakeMat("M_Wall", new Color(0.95f, 0.88f, 0.78f));          // тёплый кремовый (Майами)
        MakeMat("M_WallTeal", new Color(0.36f, 0.72f, 0.70f));
        MakeMat("M_WallPeach", new Color(0.98f, 0.76f, 0.66f));
        MakeMat("M_Trim", new Color(0.99f, 0.97f, 0.92f));
        MakeMat("M_Marble", new Color(0.93f, 0.91f, 0.87f), 0.45f);
        MakeMat("M_Gold", new Color(0.86f, 0.64f, 0.22f), 0.55f, 0.75f);
        MakeMat("M_Steel", new Color(0.58f, 0.61f, 0.65f), 0.5f, 0.6f);
        MakeMat("M_DarkSteel", new Color(0.2f, 0.22f, 0.25f), 0.35f, 0.4f);
        MakeMat("M_Wood", new Color(0.42f, 0.25f, 0.15f), 0.25f);
        MakeMat("M_Velvet", new Color(0.55f, 0.1f, 0.17f), 0.05f);
        MakeMat("M_Glass", new Color(0.65f, 0.88f, 0.95f, 0.28f), 0.9f, 0f, null, true);
        MakeMat("M_Asphalt", new Color(0.22f, 0.22f, 0.25f), 0.1f);
        MakeMat("M_Sidewalk", new Color(0.78f, 0.74f, 0.70f), 0.05f);
        MakeMat("M_Line", new Color(0.96f, 0.92f, 0.75f), 0.1f);
        MakeMat("M_Concrete", new Color(0.55f, 0.55f, 0.56f), 0.05f);
        MakeMat("M_Hazard", new Color(0.98f, 0.8f, 0.15f), 0.2f);
        MakeMat("M_Laser", new Color(1f, 0.1f, 0.1f), 0.2f, 0f, new Color(3f, 0.2f, 0.2f));
        MakeMat("M_LedGreen", new Color(0.2f, 1f, 0.5f), 0.2f, 0f, new Color(0.4f, 2f, 0.8f));
        MakeMat("M_Neon", new Color(1f, 0.45f, 0.75f), 0.2f, 0f, new Color(3f, 0.9f, 2f));
        MakeMat("M_Carpet", new Color(0.17f, 0.45f, 0.47f), 0f);
        MakeMat("M_Cash", new Color(0.42f, 0.68f, 0.38f), 0.1f);
        MakeMat("M_CashBand", new Color(0.93f, 0.88f, 0.62f), 0.1f);
        MakeMat("M_Red", new Color(0.82f, 0.12f, 0.12f), 0.3f);
        MakeMat("M_Orange", new Color(1f, 0.5f, 0.1f), 0.2f);
        MakeMat("M_Black", new Color(0.06f, 0.06f, 0.07f), 0.3f);
        MakeMat("M_Paper", new Color(0.97f, 0.96f, 0.92f), 0f);
        MakeMat("M_Burlap", new Color(0.72f, 0.6f, 0.42f), 0f);
        MakeMat("M_Screen", new Color(0.1f, 0.3f, 0.5f), 0.6f, 0f, new Color(0.3f, 0.9f, 1.6f));
        MakeMat("M_ExitGreen", new Color(0.1f, 0.8f, 0.3f), 0.2f, 0f, new Color(0.2f, 2.2f, 0.6f));
        MakeMat("M_Amber", new Color(1f, 0.6f, 0.1f), 0.2f, 0f, new Color(2.2f, 1.2f, 0.2f));
        MakeMat("M_Diamond", new Color(0.75f, 0.95f, 1f), 0.95f, 0f, new Color(0.6f, 1.4f, 1.8f));
        MakeMat("M_Canvas_Sunset", new Color(0.98f, 0.55f, 0.55f), 0f);
        MakeMat("M_Canvas_Sea", new Color(0.25f, 0.65f, 0.75f), 0f);
        MakeMat("M_Canvas_Palm", new Color(0.35f, 0.6f, 0.3f), 0f);
        MakeMat("M_Canvas_Night", new Color(0.3f, 0.2f, 0.5f), 0f);
        MakeMat("M_Canvas_Sun", new Color(1f, 0.82f, 0.3f), 0f);
        // пол-шахматка из мрамора с тонкой золотой затиркой (генерируется кодом)
        string texPath = OutDir + "/T_BankQ_Floor.png";
        if (!File.Exists(texPath))
        {
            var t = new Texture2D(256, 256, TextureFormat.RGB24, false);
            var a = new Color(0.95f, 0.93f, 0.89f); var b = new Color(0.80f, 0.86f, 0.84f); var g = new Color(0.82f, 0.66f, 0.3f);
            var rnd = new System.Random(5);
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
            {
                bool checker = (x / 128 + y / 128) % 2 == 0; var c = checker ? a : b;
                float vein = Mathf.PerlinNoise(x * 0.03f + (checker ? 0 : 7), y * 0.11f) ; if (vein > 0.72f) c *= 0.93f;
                c *= 0.985f + (float)rnd.NextDouble() * 0.03f;
                if (x % 128 < 2 || y % 128 < 2) c = g;
                t.SetPixel(x, y, c);
            }
            File.WriteAllBytes(texPath, t.EncodeToPNG()); AssetDatabase.ImportAsset(texPath);
        }
        var floorMat = MakeMat("M_FloorMarble", Color.white, 0.5f);
        floorMat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        floorMat.SetTextureScale("_BaseMap", new Vector2(8, 5));
    }

    // ================================================================== утилиты размещения
    static Transform Group(string name, Transform parent = null) { var g = new GameObject(name).transform; g.SetParent(parent ? parent : root, false); return g; }

    static GameObject Put(string path, Vector3 pos, float yaw, Transform parent, float targetHeight = 0f, bool remapKit = false)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!prefab) { log.AppendLine("  ERROR: missing " + path); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        // у FBX Quaternius в корне поворот −90° по X (Z-up → Y-up) — сохраняем его и добавляем только поворот по Y
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0) * prefab.transform.localRotation);
        if (targetHeight > 0f)
        {
            var b = Bounds(go);
            if (b.size.y > 0.001f) go.transform.localScale *= targetHeight / b.size.y;
            b = Bounds(go); go.transform.position += Vector3.up * (pos.y - b.min.y);   // ставим на пол
        }
        if (remapKit)
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(m => m ? M(m.name.Split(' ')[0].Split('.')[0]) : M("M_Wall")).ToArray();
        return go;
    }
    static GameObject Kit(string name, Vector3 pos, float yaw, Transform parent) => Put($"{Mod}/{name}.fbx", pos, yaw, parent, 0f, true);
    static GameObject House(string name, Vector3 pos, float yaw, Transform parent, float h) => Put($"{Q}/HouseInterior/{name}.fbx", pos, yaw, parent, h);

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
    }

    static GameObject Block(string name, Vector3 center, Vector3 size, string mat, Transform parent, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; go.transform.SetParent(parent, false);
        go.transform.position = center; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = M(mat);
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static Transform Anchor(string name, Vector3 pos, float yaw = 0f)
    {
        var t = new GameObject("Anchor_" + name).transform; t.SetParent(anchors, false);
        t.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0)); return t;
    }

    /// Ряд модульных стен по 4 м от a к b (по оси X или Z); pattern[i] — имя модуля для i-го пролёта (или null = обычная стена).
    static void WallRun(Vector3 a, Vector3 b, string wall, Transform parent, Dictionary<int, string> special = null)
    {
        var dir = b - a; float len = dir.magnitude; dir /= len;
        int n = Mathf.RoundToInt(len / 4f);
        float yaw = Mathf.Atan2(-dir.z, dir.x) * Mathf.Rad2Deg;     // модуль вдоль своего X
        for (int i = 0; i < n; i++)
        {
            var c = a + dir * (i * 4f + 2f);
            string name = special != null && special.TryGetValue(i, out var s) ? s : wall;
            if (name == "") continue;
            Kit(name, c, yaw, parent);
        }
    }

    // ================================================================== улица Майами
    static void BuildStreet()
    {
        var g = Group("Street");
        Block("Road", new Vector3(0, -0.05f, -12f), new Vector3(140, 0.1f, 12), "M_Asphalt", g);
        Block("Sidewalk_N", new Vector3(0, 0.07f, -3.5f), new Vector3(140, 0.14f, 5), "M_Sidewalk", g);
        Block("Sidewalk_S", new Vector3(0, 0.07f, -20.5f), new Vector3(140, 0.14f, 5), "M_Sidewalk", g);
        Block("Ground", new Vector3(0, -0.2f, 30f), new Vector3(140, 0.3f, 70), "M_Concrete", g);
        for (float x = -66; x < 70; x += 6) Block("Lane", new Vector3(x, 0.01f, -12f), new Vector3(3, 0.02f, 0.25f), "M_Line", g, false);
        Block("Curb_N", new Vector3(0, 0.08f, -6.05f), new Vector3(140, 0.16f, 0.2f), "M_Trim", g);
        // дома через дорогу (лицом на север) и по бокам
        var houses = new[] { "2Story_Columns_Mat", "3Story_Balcony_Mat", "2Story_Sign_Mat", "4Story_Center_Mat", "2Story_Wide_Mat", "3Story_Slim_Mat", "2Story_Balcony_Mat", "4Story_Mat" };
        float x0 = -54;
        for (int i = 0; i < houses.Length; i++)
        {
            var h = Put($"{Q}/Buildings/{houses[i]}.fbx", new Vector3(x0, 0.14f, -27f), 180f, g);
            if (!h) continue;
            var b = Bounds(h);
            float s = Mathf.Clamp(9f / Mathf.Max(0.1f, b.size.y) * (houses[i].StartsWith("4") ? 1.5f : houses[i].StartsWith("3") ? 1.25f : 1f), 0.2f, 20f);
            h.transform.localScale *= s;
            b = Bounds(h); h.transform.position += new Vector3(x0 - b.center.x, 0.14f - b.min.y, -23.5f - b.max.z);
            x0 += Bounds(h).size.x + 1.5f;
        }
        foreach (var (x, name) in new[] { (-34f, "3Story_Small_Mat"), (-25f, "2Story_Slim_Mat"), (42f, "2Story_Stairs_Mat"), (52f, "3Story_Balcony_Mat") })
        {
            var h = Put($"{Q}/Buildings/{name}.fbx", new Vector3(x, 0.14f, 2f), 0f, g);
            if (!h) continue;
            var b = Bounds(h); h.transform.localScale *= 9f / Mathf.Max(0.1f, b.size.y) * (name.StartsWith("3") ? 1.2f : 1f);
            b = Bounds(h); h.transform.position += new Vector3(x - b.center.x, 0.14f - b.min.y, -0.9f - b.min.z);
        }
        // фонари, светофор, машины, деревья
        for (float x = -60; x <= 60; x += 15) { Put($"{Q}/Street/Streetlight_Single.fbx", new Vector3(x, 0.14f, -5.3f), 180f, g, 5.5f); Put($"{Q}/Street/Streetlight_Single.fbx", new Vector3(x + 7, 0.14f, -18.7f), 0f, g, 5.5f); }
        Put($"{Q}/Street/TrafficLight.fbx", new Vector3(22f, 0.14f, -5.6f), 180f, g, 4.5f);
        foreach (var (name, x, z, yaw) in new[] { ("Taxi", 14f, -8.2f, 90f), ("BasicCar", 26f, -8.2f, 90f), ("SimpleCarShort", -28f, -15.8f, -90f), ("CopCar", -40f, -8.2f, 90f), ("SimpleCarHigh", 44f, -15.8f, -90f) })
            Put($"{Q}/Cars/{name}.fbx", new Vector3(x, 0f, z), yaw, g, 1.55f);
        var trees = AssetDatabase.FindAssets("t:Model CommonTree", new[] { Q + "/NatureMegaKit/FBX" }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
        for (int i = 0; i < 10; i++)
        {
            float x = -58 + i * 13 + (i % 2) * 3; if (Mathf.Abs(x) < 20) continue;
            if (trees.Length > 0) Put(trees[i % trees.Length], new Vector3(x, 0.14f, -1.8f), i * 37f, g, 6.5f);
            Block("Planter", new Vector3(x, 0.35f, -1.8f), new Vector3(1.4f, 0.5f, 1.4f), "M_Trim", g);
        }
        Anchor("VanPark", new Vector3(-6f, 0f, -9f), 90f);
        Anchor("VanExit", new Vector3(-6f, 0.14f, -4.5f), 0f);
        Anchor("VanPath_0", new Vector3(-6f, 0f, -9f)); Anchor("VanPath_1", new Vector3(30f, 0f, -9f)); Anchor("VanPath_2", new Vector3(80f, 0f, -9f));
        Anchor("TeleportStreet", new Vector3(0f, 0.14f, -4.5f), 0f);
    }

    // ================================================================== фасад с портиком
    static void BuildFacade()
    {
        var g = Group("Facade");
        // подиум и ступени
        Block("Podium", new Vector3(0, FL / 2, -1.5f), new Vector3(20, FL, 3), "M_Marble", g);
        for (int i = 0; i < 4; i++) Block("Step" + i, new Vector3(0, 0.09f + i * 0.18f, -3.0f - (3 - i) * 0.4f - 0.2f), new Vector3(20, 0.18f * (i + 1), 0.4f), "M_Marble", g);
        Block("Plinth", new Vector3(0, FL / 2, 16f), new Vector3(33, FL, 32), "M_Trim", g);
        // фасадная стена: окна по краям, две двери по центру
        WallRun(new Vector3(-16, FL, 0), new Vector3(16, FL, 0), "Bank_WallWindow_4x7", g,
            new Dictionary<int, string> { [3] = "Bank_WallDoor_4x7", [4] = "Bank_WallDoor_4x7" });
        foreach (float x in new[] { -6f, -2f, 2f, 6f }) { }
        // колонны портика и антаблемент
        foreach (float x in new[] { -7.5f, -2.5f, 2.5f, 7.5f }) Kit("Bank_Column_7", new Vector3(x, FL, -2.2f), 0f, g);
        Kit("Bank_Entablature", new Vector3(0, FL + HallH, -2.2f), 0f, g);
        Block("PorticoRoof", new Vector3(0, FL + HallH + 1.55f, -1.2f), new Vector3(17, 0.3f, 2.4f), "M_Trim", g);
        Block("Parapet", new Vector3(0, FL + HallH + 0.85f, 0f), new Vector3(32.6f, 1.7f, 0.5f), "M_Trim", g);
        Block("ParapetGold", new Vector3(0, FL + HallH + 0.9f, -0.26f), new Vector3(32.6f, 0.12f, 0.04f), "M_Gold", g, false);
        // стекло окон и дверей
        for (int i = 0; i < 8; i++)
        {
            float x = -14 + i * 4;
            if (i == 3 || i == 4) { Block("DoorGlass" + i, new Vector3(x, FL + 1.8f, 0), new Vector3(2.7f, 3.5f, 0.04f), "M_Glass", g, false); continue; }
            Block("WindowGlass" + i, new Vector3(x, FL + 3.2f, 0), new Vector3(2.6f, 4.4f, 0.04f), "M_Glass", g, false);
            for (int k = 1; k < 3; k++) Block("Mullion", new Vector3(x, FL + 1f + k * 1.47f, 0), new Vector3(2.6f, 0.06f, 0.1f), "M_Gold", g, false);
        }
        // вывеска
        var sign = new GameObject("Sign_SunsetTrust", typeof(TextMeshPro)); sign.transform.SetParent(g, false);
        var tmp = sign.GetComponent<TextMeshPro>();
        tmp.text = "SUNSET TRUST BANK"; tmp.fontSize = 9; tmp.alignment = TextAlignmentOptions.Center; tmp.color = new Color(0.86f, 0.64f, 0.22f);
        tmp.fontStyle = FontStyles.Bold; tmp.rectTransform.sizeDelta = new Vector2(15, 1.2f);
        sign.transform.SetPositionAndRotation(new Vector3(0, FL + HallH + 1.0f, -2.98f), Quaternion.identity);
        Block("NeonStrip", new Vector3(0, FL + HallH + 0.42f, -2.97f), new Vector3(14f, 0.06f, 0.04f), "M_Neon", g, false);
        // таблички «ОТКРЫТО» и пальмы-кадки у входа
        foreach (float x in new[] { -9.5f, 9.5f }) { House("Houseplant_7", new Vector3(x, FL, -1.2f), 0, g, 2.4f); }
    }

    // ================================================================== операционный зал
    static void BuildHall()
    {
        var g = Group("Hall");
        var floor = Block("HallFloor", new Vector3(0, FL - 0.05f, 10f), new Vector3(32, 0.1f, 20), "M_FloorMarble", g);
        // стены: запад — окна, север — двери в служебное крыло, восток — двери в гараж и коридор хранилища
        WallRun(new Vector3(-16, FL, 20), new Vector3(-16, FL, 0), "Bank_WallWindow_4x7", g);
        WallRun(new Vector3(-16, FL, 20), new Vector3(16, FL, 20), "Bank_Wall_4x7", g,
            new Dictionary<int, string> { [1] = "Bank_WallDoor_4x7", [6] = "Bank_WallDoor_4x7" });
        WallRun(new Vector3(16, FL, 0), new Vector3(16, FL, 20), "Bank_Wall_4x7", g, new Dictionary<int, string> { [2] = "Bank_WallDoor_4x7" });
        for (int i = 0; i < 5; i++) Block("WestGlass" + i, new Vector3(-16, FL + 3.2f, 2 + i * 4), new Vector3(0.04f, 4.4f, 2.6f), "M_Glass", g, false);
        // колонны зала и кессонный потолок
        foreach (float x in new[] { -9f, 9f }) foreach (float z in new[] { 5f, 11f }) Kit("Bank_Column_7", new Vector3(x, FL, z), 0f, g);
        for (int ix = 0; ix < 8; ix++) for (int iz = 0; iz < 5; iz++) Kit("Bank_CeilingCoffer", new Vector3(-14 + ix * 4, FL + HallH, 2 + iz * 4), 0f, g);
        Block("CeilingCap", new Vector3(0, FL + HallH + 0.4f, 10), new Vector3(32.4f, 0.3f, 20.4f), "M_Trim", g);
        // люстры (Quaternius), ковры, зона ожидания
        foreach (var p in new[] { new Vector3(-4.5f, 0, 8f), new Vector3(4.5f, 0, 8f), new Vector3(0, 0, 3.5f) })
        {
            var ch = House("Light_Chandelier", new Vector3(p.x, FL + HallH - 2.2f, p.z), 0, g, 1.8f);
            if (ch) { var b = Bounds(ch); ch.transform.position += Vector3.up * (FL + HallH - 0.3f - b.max.y); }
            Block("ChainCh", new Vector3(p.x, FL + HallH - 0.15f, p.z), new Vector3(0.05f, 0.3f, 0.05f), "M_Gold", g, false);
        }
        Block("Runner", new Vector3(0, FL + 0.01f, 6f), new Vector3(4.5f, 0.02f, 12f), "M_Carpet", g, false);
        Block("RunnerGoldL", new Vector3(-2.3f, FL + 0.015f, 6f), new Vector3(0.12f, 0.02f, 12f), "M_Gold", g, false);
        Block("RunnerGoldR", new Vector3(2.3f, FL + 0.015f, 6f), new Vector3(0.12f, 0.02f, 12f), "M_Gold", g, false);
        foreach (float s in new[] { -1f, 1f })
        {
            float x = s * 12.5f;
            Block("WaitRug", new Vector3(x, FL + 0.01f, 6.5f), new Vector3(5f, 0.02f, 5.5f), "M_Carpet", g, false);
            Block("WaitRugGold", new Vector3(x, FL + 0.012f, 6.5f), new Vector3(4.4f, 0.02f, 4.9f), "M_Velvet", g, false);
            House("Couch_Large1", new Vector3(x, FL, 3.2f), 0f, g, 0.95f);
            House("Couch_Large1", new Vector3(x, FL, 9.8f), 180f, g, 0.95f);
            House("Table_RoundSmall", new Vector3(x, FL, 6.5f), 0, g, 0.55f);
            House("Houseplant_" + (s < 0 ? 4 : 6), new Vector3(s * 15f, FL, 1.2f), 0, g, 2.0f);
            House("Houseplant_2", new Vector3(s * 15f, FL, 13.5f), 0, g, 1.6f);
            House("Light_Floor2", new Vector3(s * 15.2f, FL, 6.5f), 0, g, 2.0f);
        }
        // стол для бланков посередине
        Block("FormDesk", new Vector3(0, FL + 0.55f, 9.5f), new Vector3(3.2f, 1.1f, 0.9f), "M_Wood", g);
        Block("FormDeskTop", new Vector3(0, FL + 1.12f, 9.5f), new Vector3(3.4f, 0.06f, 1.0f), "M_Marble", g, false);
        House("Light_Desk", new Vector3(-1.2f, FL + 1.15f, 9.5f), 0, g, 0.5f);
        // часы и табло
        Anchor("Scoreboard_0", new Vector3(0, FL + 5.2f, 19.75f), 180f);
        Anchor("Scoreboard_1", new Vector3(-15.75f, FL + 4.6f, 10f), 90f);
        Anchor("Scoreboard_2", new Vector3(15.75f, FL + 4.6f, 4f), -90f);
        Anchor("Camera_0", new Vector3(-15.4f, FL + 6.3f, 0.6f), 45f);
        Anchor("Camera_1", new Vector3(15.4f, FL + 6.3f, 0.6f), -45f);
        Anchor("Camera_2", new Vector3(0f, FL + 6.4f, 19.4f), 180f);
        Anchor("PlayerSpawn", new Vector3(0, FL, 3f), 0f);
    }

    // ================================================================== кассы
    static void BuildTellers()
    {
        var g = Group("Tellers");
        for (int i = 0; i < 6; i++)
        {
            var x = -7.5f + i * 3f;
            Kit("Bank_TellerCounter", new Vector3(x, FL, 15f), 0f, g);
            Anchor("Glass_" + i, new Vector3(x, FL + 1.85f, 15f));
            // рабочее место кассира
            Block("CashierDesk" + i, new Vector3(x, FL + 0.38f, 16.2f), new Vector3(2.2f, 0.76f, 0.7f), "M_Wood", g);
            House("Chair_2", new Vector3(x, FL, 17.1f), 180f, g, 0.95f);
            Block("Monitor" + i, new Vector3(x - 0.5f, FL + 1.0f, 16.35f), new Vector3(0.55f, 0.35f, 0.04f), "M_DarkSteel", g, false);
            Block("Till" + i, new Vector3(x + 0.5f, FL + 0.88f, 16.1f), new Vector3(0.45f, 0.22f, 0.35f), "M_DarkSteel", g, false);
            if (i < 4) Anchor("Cashier_" + (i + 1), new Vector3(x, FL, 16.9f), 180f);
        }
        // торцы кассовой зоны — низкие перегородки с калиткой (горилла перепрыгнет)
        foreach (float s in new[] { -1f, 1f })
        {
            Block("TellerEnd", new Vector3(s * 9.6f, FL + 0.55f, 17.5f), new Vector3(0.3f, 1.1f, 5f), "M_Wood", g);
            Block("TellerEndTop", new Vector3(s * 9.6f, FL + 1.12f, 17.5f), new Vector3(0.4f, 0.06f, 5.1f), "M_Marble", g, false);
        }
        // очередь со столбиками и бархатными канатами
        for (int i = 0; i < 6; i++)
        {
            float x = -7.5f + i * 3f;
            foreach (float dx in new[] { -0.9f, 0.9f })
            {
                Kit("Bank_Stanchion", new Vector3(x + dx, FL, 12.2f), 0f, g);
                Kit("Bank_Stanchion", new Vector3(x + dx, FL, 13.7f), 0f, g);
                Block("Rope", new Vector3(x + dx, FL + 0.9f, 12.95f), new Vector3(0.05f, 0.05f, 1.5f), "M_Velvet", g, false);
            }
        }
        // шкафы с документами вдоль задней стены
        for (int i = 0; i < 4; i++) House(i % 2 == 0 ? "Drawer_3" : "Bookshelf", new Vector3(-6f + i * 4f, FL, 19.45f), 180f, g, i % 2 == 0 ? 1.2f : 2.2f);
    }

    // ================================================================== служебное крыло: коридор + 3 комнаты
    static void BuildServiceWing()
    {
        var g = Group("ServiceWing");
        Block("CorridorFloor", new Vector3(0, FL - 0.05f, 22f), new Vector3(32, 0.1f, 4), "M_Concrete", g);
        Block("RoomsFloor", new Vector3(0, FL - 0.05f, 28f), new Vector3(32, 0.1f, 8), "M_Wood", g);
        Block("WingCeiling", new Vector3(0, FL + LowH + 0.1f, 26f), new Vector3(32.4f, 0.2f, 12.4f), "M_Trim", g);
        Block("WingRoof", new Vector3(0, FL + HallH, 26f), new Vector3(32.4f, 0.3f, 12.4f), "M_Concrete", g);
        WallRun(new Vector3(-16, FL, 24), new Vector3(16, FL, 24), "Bank_Wall_4x3_5", g,
            new Dictionary<int, string> { [1] = "Bank_WallDoor_4x3_5", [3] = "Bank_WallDoor_4x3_5", [6] = "Bank_WallDoor_4x3_5" });
        WallRun(new Vector3(-16, FL, 32), new Vector3(16, FL, 32), "Bank_Wall_4x3_5", g);
        WallRun(new Vector3(-16, FL, 20), new Vector3(-16, FL, 32), "Bank_Wall_4x3_5", g);
        // перегородки комнат: x = -6 и x = 4
        WallRun(new Vector3(-6, FL, 24), new Vector3(-6, FL, 32), "Bank_Wall_4x3_5", g);
        WallRun(new Vector3(6, FL, 24), new Vector3(6, FL, 32), "Bank_Wall_4x3_5", g);
        // восточный торец коридора — дверь в крыло хранилища
        WallRun(new Vector3(16, FL, 20), new Vector3(16, FL, 32), "Bank_Wall_4x3_5", g, new Dictionary<int, string> { [0] = "Bank_WallDoor_4x3_5" });
        Block("UpperWall", new Vector3(0, FL + (LowH + HallH) / 2, 32f), new Vector3(32.3f, HallH - LowH, 0.3f), "M_Wall", g);
        Block("UpperWallW", new Vector3(-16, FL + (LowH + HallH) / 2, 26f), new Vector3(0.3f, HallH - LowH, 12f), "M_Wall", g);
        Anchor("ServiceDoorB", new Vector3(-10f, FL, 20f), 0f);
        Anchor("ServiceDoorA", new Vector3(10f, FL, 20f), 0f);
        Anchor("RoomDoor_Director", new Vector3(-10f, FL, 24f), 0f);
        Anchor("RoomDoor_BreakRoom", new Vector3(-2f, FL, 24f), 0f);
        Anchor("RoomDoor_ServerRoom", new Vector3(10f, FL, 24f), 0f);
        Anchor("Camera_3", new Vector3(-15.5f, FL + 3.1f, 20.6f), 90f);
        // коридор: светильники, кулер, растение
        for (int i = 0; i < 4; i++) House("Light_CeilingSingle", new Vector3(-12 + i * 8, FL + LowH - 0.6f, 22f), 0, g, 0.5f);
        House("Houseplant_3", new Vector3(-15.2f, FL, 23.2f), 0, g, 1.4f);
        House("Trashcan_Cylindric", new Vector3(14.8f, FL, 23.3f), 0, g, 0.6f);
        // кабинет директора (запад)
        Block("DirectorCarpet", new Vector3(-11f, FL + 0.01f, 28.2f), new Vector3(6f, 0.02f, 5f), "M_Velvet", g, false);
        Block("DirectorDesk", new Vector3(-11f, FL + 0.4f, 29.3f), new Vector3(3f, 0.8f, 1.2f), "M_Wood", g);
        Block("DirectorDeskTop", new Vector3(-11f, FL + 0.82f, 29.3f), new Vector3(3.1f, 0.05f, 1.3f), "M_Gold", g, false);
        House("Chair_1", new Vector3(-11f, FL, 30.4f), 180f, g, 1.15f);
        House("Bookshelf", new Vector3(-14.9f, FL, 29f), 90f, g, 2.4f);
        House("Light_Desk", new Vector3(-12f, FL + 0.85f, 29.3f), 0, g, 0.5f);
        House("Couch_Medium1", new Vector3(-8f, FL, 26.2f), -90f, g, 0.9f);
        Anchor("LaserControl", new Vector3(-6.3f, FL + 1.4f, 30.5f), -90f);
        Anchor("Loot_DirectorSafe", new Vector3(-14.5f, FL + 1.2f, 31.2f));
        // комната отдыха (центр)
        House("Kitchen_2Drawers", new Vector3(-3.5f, FL, 31.5f), 180f, g, 0.95f);
        House("Kitchen_Sink", new Vector3(-1.5f, FL, 31.5f), 180f, g, 0.95f);
        House("Kitchen_Fridge", new Vector3(0.4f, FL, 31.4f), 180f, g, 2.0f);
        House("Couch_L", new Vector3(2.5f, FL, 26.5f), 0f, g, 0.9f);
        House("Table_RoundSmall2", new Vector3(-2f, FL, 27.5f), 0, g, 0.75f);
        House("Chair_3", new Vector3(-3f, FL, 27.5f), 90f, g, 0.95f);
        House("Chair_3", new Vector3(-1f, FL, 27.5f), -90f, g, 0.95f);
        Anchor("Loot_KeyCard", new Vector3(-2f, FL + 0.8f, 27.5f));
        Anchor("Loot_BreakCash", new Vector3(-3.5f, FL + 1.0f, 31.3f));
        // серверная (восток)
        for (int i = 0; i < 4; i++)
        {
            float x = 8f + i * 2f;
            Block("Rack" + i, new Vector3(x, FL + 1.1f, 30.8f), new Vector3(1.1f, 2.2f, 1f), "M_DarkSteel", g);
            for (int k = 0; k < 6; k++) Block("Led", new Vector3(x - 0.3f + (k % 3) * 0.3f, FL + 0.6f + k / 3 * 0.9f, 30.28f), new Vector3(0.12f, 0.04f, 0.02f), "M_LedGreen", g, false);
        }
        Block("VentHatch", new Vector3(14.5f, FL + 0.02f, 26f), new Vector3(1.2f, 0.04f, 1.2f), "M_Steel", g, false);
        Anchor("Camera_4", new Vector3(15.5f, FL + 3.1f, 31.5f), -135f);
    }

    // ================================================================== крыло хранилища: тамбур с лазерами + сейфовая
    static void BuildVaultWing()
    {
        var g = Group("VaultWing");
        Block("AnteFloor", new Vector3(20f, FL - 0.05f, 24f), new Vector3(8, 0.1f, 8), "M_Concrete", g);
        Block("VaultFloor", new Vector3(28f, FL - 0.05f, 24f), new Vector3(8, 0.1f, 8), "M_FloorMarble", g);
        Block("WingCeiling", new Vector3(24f, FL + 4.1f, 24f), new Vector3(16.4f, 0.2f, 8.4f), "M_DarkSteel", g);
        Block("Hazard", new Vector3(17.2f, FL + 0.005f, 24f), new Vector3(0.4f, 0.02f, 7.6f), "M_Hazard", g, false);
        // внешние стены крыла (бронированные)
        Block("N", new Vector3(24f, FL + 2.05f, 28.15f), new Vector3(16.3f, 4.1f, 0.3f), "M_DarkSteel", g);
        Block("S", new Vector3(24f, FL + 2.05f, 19.85f), new Vector3(16.3f, 4.1f, 0.3f), "M_DarkSteel", g);
        Block("E", new Vector3(32.15f, FL + 2.05f, 24f), new Vector3(0.3f, 4.1f, 8.6f), "M_DarkSteel", g);
        Block("WestTop", new Vector3(16f, FL + 3.8f, 24f), new Vector3(0.3f, 0.6f, 8f), "M_Wall", g);
        // стена с круглой дверью хранилища на x = 24
        Kit("Bank_VaultFrame", new Vector3(24f, FL, 24f), 90f, g);
        Block("VaultWallN", new Vector3(24f, FL + 2.05f, 27.5f), new Vector3(0.8f, 4.1f, 1.0f), "M_DarkSteel", g);
        Block("VaultWallS", new Vector3(24f, FL + 2.05f, 20.5f), new Vector3(0.8f, 4.1f, 1.0f), "M_DarkSteel", g);
        Anchor("VaultDoor", new Vector3(24f, FL + 1.65f, 24f), 90f);
        Anchor("Camera_5", new Vector3(17.4f, FL + 3.6f, 27.6f), 135f);
        // лазерные стойки и 13 лучей зигзагом
        var laser = Group("Lasers", g);
        for (int i = 0; i < 4; i++) { Kit("Bank_LaserPost", new Vector3(18.5f + i * 1.6f, FL, 20.4f), 0f, laser); Kit("Bank_LaserPost", new Vector3(18.5f + i * 1.6f, FL, 27.6f), 0f, laser); }
        float[] hs = { 0.39f, 0.79f, 1.19f, 1.59f };
        int n = 0;
        for (int i = 0; i < 4 && n < 13; i++)
            for (int k = 0; k < 4 && n < 13; k++)
            {
                if ((i + k) % 2 == 1 && n > 8) continue;
                float x = 18.5f + i * 1.6f, y = FL + hs[(k + i) % 4];
                var a = Anchor("Laser_" + n, new Vector3(x, y, 24f));
                a.localScale = new Vector3(0.025f, 0.025f, 7.1f);          // ориентир: луч вдоль Z, длина 7.1 м
                Block("LaserPreview_" + n, a.position, a.localScale, "M_Laser", laser, false).tag = "EditorOnly";
                n++;
            }
        // сейфовая: ячейки, золото, пьедесталы под лут
        // дверцы ячеек — на стороне +Z модуля: поворачиваем лицом в комнату
        Kit("Bank_DepositBoxes", new Vector3(28f, FL, 27.7f), 0f, g);
        Kit("Bank_DepositBoxes", new Vector3(31.7f, FL, 24f), 90f, g);
        Kit("Bank_DepositBoxes", new Vector3(28f, FL, 20.3f), 180f, g);
        // круглая дверь хранилища (в превью — приоткрыта; в игре её поворачивает NetworkVaultDoor через петлю)
        var door = Kit("Bank_VaultDoor", new Vector3(24f, FL + 1.65f, 25.3f), 90f + 100f, g);
        if (door) { door.name = "VaultDoorModel"; door.tag = "EditorOnly"; }
        int lootN = 0;
        foreach (var p in new[] { new Vector3(26.5f, 0, 22.4f), new Vector3(29.5f, 0, 22.4f), new Vector3(26.5f, 0, 25.6f), new Vector3(29.5f, 0, 25.6f), new Vector3(28f, 0, 24f) })
        {
            Block("Pedestal", new Vector3(p.x, FL + 0.45f, p.z), new Vector3(1f, 0.9f, 1f), "M_Marble", g);
            Block("PedestalGold", new Vector3(p.x, FL + 0.91f, p.z), new Vector3(1.05f, 0.04f, 1.05f), "M_Gold", g, false);
            Anchor("Loot_Vault_" + lootN++, new Vector3(p.x, FL + 0.95f, p.z));
            if (lootN != 5) Kit("Bank_GoldStack", new Vector3(p.x, FL + 0.93f, p.z), 0f, g).tag = "EditorOnly";
        }
        Anchor("TeleportVault", new Vector3(26f, FL, 24f), 90f);
        Anchor("Camera_6", new Vector3(31.6f, FL + 3.6f, 27.6f), -135f);
    }

    // ================================================================== гараж с машиной эвакуации
    static void BuildGarage()
    {
        var g = Group("Garage");
        Block("GarageFloor", new Vector3(24f, FL - 0.05f, 10f), new Vector3(16, 0.1f, 20), "M_Concrete", g);
        Block("GarageCeiling", new Vector3(24f, FL + 5.1f, 10f), new Vector3(16.4f, 0.2f, 20.4f), "M_Concrete", g);
        Block("GE", new Vector3(32.15f, FL + 2.55f, 12f), new Vector3(0.3f, 5.1f, 16f), "M_WallPeach", g);
        Block("GS_L", new Vector3(18f, FL + 2.55f, 0f), new Vector3(4f, 5.1f, 0.3f), "M_WallPeach", g);
        Block("GS_R", new Vector3(30f, FL + 2.55f, 0f), new Vector3(4.3f, 5.1f, 0.3f), "M_WallPeach", g);
        Block("GS_Top", new Vector3(24f, FL + 4.55f, 0f), new Vector3(8f, 1.1f, 0.3f), "M_WallPeach", g);
        Block("GN_Upper", new Vector3(24f, FL + 4.6f, 19.85f), new Vector3(16f, 1f, 0.3f), "M_WallPeach", g);
        Block("GE_Corner", new Vector3(32.15f, FL + 2.55f, 2f), new Vector3(0.3f, 5.1f, 4f), "M_WallPeach", g);
        Block("Ramp", new Vector3(24f, FL / 2, -1.6f), new Vector3(8f, 0.1f, 3.4f), "M_Concrete", g).transform.rotation = Quaternion.Euler(-12f, 0, 0);
        for (int i = 0; i < 4; i++) Block("ParkLine", new Vector3(19 + i * 3.4f, FL + 0.01f, 12f), new Vector3(0.15f, 0.02f, 6f), "M_Hazard", g, false);
        Block("EvacPad", new Vector3(24f, FL + 0.012f, 12f), new Vector3(6f, 0.02f, 7.5f), "M_LedGreen", g, false);
        Put($"{Q}/Cars/SimpleCarHigh.fbx", new Vector3(24f, FL, 13f), 0f, g, 1.9f);
        House("Shelf_Large", new Vector3(31.3f, FL, 6f), -90f, g, 2.2f);
        House("Trashcan_Large", new Vector3(17f, FL, 2f), 0f, g, 1.1f);
        Anchor("Evacuation", new Vector3(24f, FL, 12f));
        Anchor("Camera_7", new Vector3(31.5f, FL + 4.4f, 0.6f), -45f);
    }

    // ================================================================== детализация: предметы во всех зонах
    // мелочь без коллайдеров (руки за неё не цепляются), крупное — с коллайдерами (AddColliders)
    static void Painting(Transform g, Vector3 c, float yaw, float w, float h, string canvas, string accent)
    {
        var rot = Quaternion.Euler(0, yaw, 0);
        var f = Block("PaintFrame", c, new Vector3(w + 0.16f, h + 0.16f, 0.06f), "M_Gold", g, false); f.transform.rotation = rot;
        var cv = Block("PaintCanvas", c + rot * new Vector3(0, 0, 0.035f), new Vector3(w, h, 0.02f), canvas, g, false); cv.transform.rotation = rot;
        // простая «картина»: горизонт и солнце/пальма
        var a = Block("PaintBand", c + rot * new Vector3(0, -h * 0.25f, 0.05f), new Vector3(w, h * 0.3f, 0.01f), accent, g, false); a.transform.rotation = rot;
        var s = Block("PaintSun", c + rot * new Vector3(w * 0.18f, h * 0.12f, 0.055f), new Vector3(h * 0.28f, h * 0.28f, 0.01f), "M_Canvas_Sun", g, false); s.transform.rotation = rot;
    }
    static GameObject Rot(GameObject go, float yaw) { go.transform.rotation = Quaternion.Euler(0, yaw, 0); return go; }
    static void CashStack(Transform g, Vector3 p, int layers = 2)
    {
        for (int l = 0; l < layers; l++)
            for (int i = 0; i < 2; i++)
            {
                Block("Cash", p + new Vector3(i * 0.18f - 0.09f, 0.04f + l * 0.08f, 0), new Vector3(0.16f, 0.07f, 0.08f), "M_Cash", g, false);
                Block("CashBand", p + new Vector3(i * 0.18f - 0.09f, 0.04f + l * 0.08f, 0), new Vector3(0.05f, 0.075f, 0.085f), "M_CashBand", g, false);
            }
    }
    static void MoneyBag(Transform g, Vector3 p, float s = 1f)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Sphere); b.name = "MoneyBag"; b.transform.SetParent(g, false);
        b.transform.position = p + Vector3.up * 0.28f * s; b.transform.localScale = new Vector3(0.55f, 0.6f, 0.5f) * s;
        b.GetComponent<Renderer>().sharedMaterial = M("M_Burlap");
        Block("BagTie", p + Vector3.up * 0.6f * s, new Vector3(0.14f, 0.12f, 0.14f) * s, "M_Burlap", g, false);
        Block("BagSign", p + new Vector3(0, 0.3f * s, -0.26f * s), new Vector3(0.16f, 0.16f, 0.01f) * s, "M_Cash", g, false);
    }
    static void Extinguisher(Transform g, Vector3 p)
    {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder); c.name = "Extinguisher"; c.transform.SetParent(g, false);
        c.transform.position = p + Vector3.up * 0.3f; c.transform.localScale = new Vector3(0.16f, 0.3f, 0.16f);
        c.GetComponent<Renderer>().sharedMaterial = M("M_Red"); Object.DestroyImmediate(c.GetComponent<Collider>());
        Block("ExtTop", p + Vector3.up * 0.65f, new Vector3(0.08f, 0.1f, 0.08f), "M_Black", g, false);
    }
    /// yaw: Quaternion.Euler(0, yaw, 0) * forward указывает туда, где стоит читающий.
    static void ExitSign(Transform g, Vector3 p, float yaw)
    {
        Rot(Block("ExitSign", p, new Vector3(0.6f, 0.22f, 0.06f), "M_ExitGreen", g, false), yaw);
        var v = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
        var t = new GameObject("ExitText", typeof(TextMeshPro)); t.transform.SetParent(g, false);
        var tmp = t.GetComponent<TextMeshPro>(); tmp.text = "ВЫХОД"; tmp.fontSize = 1.4f; tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white;
        tmp.rectTransform.sizeDelta = new Vector2(0.6f, 0.22f);
        t.transform.SetPositionAndRotation(p + v * 0.035f, Quaternion.LookRotation(-v));
    }
    static void Atm(Transform g, Vector3 p, float yaw)
    {
        var rot = Quaternion.Euler(0, yaw, 0);
        Rot(Block("ATM", p + Vector3.up * 0.95f, new Vector3(0.8f, 1.9f, 0.6f), "M_Steel", g), yaw);
        Rot(Block("ATMTop", p + Vector3.up * 1.95f, new Vector3(0.9f, 0.18f, 0.66f), "M_Canvas_Sea", g, false), yaw);
        Rot(Block("ATMScreen", p + rot * new Vector3(0, 1.35f, -0.31f), new Vector3(0.45f, 0.32f, 0.02f), "M_Screen", g, false), yaw);
        Rot(Block("ATMKeys", p + rot * new Vector3(0, 1.0f, -0.34f), new Vector3(0.4f, 0.04f, 0.12f), "M_DarkSteel", g, false), yaw);
        Rot(Block("ATMSlot", p + rot * new Vector3(0, 0.82f, -0.31f), new Vector3(0.3f, 0.04f, 0.02f), "M_Amber", g, false), yaw);
    }
    static void Bench(Transform g, Vector3 p, float yaw, float len = 2f)
    {
        var rot = Quaternion.Euler(0, yaw, 0);
        Rot(Block("BenchSeat", p + Vector3.up * 0.45f, new Vector3(len, 0.08f, 0.5f), "M_Wood", g), yaw);
        Rot(Block("BenchBack", p + rot * new Vector3(0, 0.8f, 0.24f), new Vector3(len, 0.5f, 0.06f), "M_Wood", g, false), yaw);
        foreach (float dx in new[] { -len / 2 + 0.1f, len / 2 - 0.1f }) Rot(Block("BenchLeg", p + rot * new Vector3(dx, 0.22f, 0), new Vector3(0.08f, 0.45f, 0.5f), "M_DarkSteel", g, false), yaw);
    }

    static void BuildDetails()
    {
        var g = Group("Details");
        // ---------- зал: пилястры между окнами, картины, часы, табло курсов, банкоматы, охрана, буклеты, лавки
        foreach (float z in new[] { 4f, 8f, 12f, 16f }) Kit("Bank_Pilaster_7", new Vector3(-15.85f, FL, z), -90f, g);
        foreach (float z in new[] { 4f, 16f }) Kit("Bank_Pilaster_7", new Vector3(15.85f, FL, z), 90f, g);
        Painting(g, new Vector3(15.8f, FL + 3.4f, 2f), -90f, 2.2f, 1.5f, "M_Canvas_Sunset", "M_Canvas_Sea");
        Painting(g, new Vector3(15.8f, FL + 3.4f, 18f), -90f, 2.2f, 1.5f, "M_Canvas_Palm", "M_Canvas_Sea");
        Painting(g, new Vector3(-12f, FL + 4.8f, 19.8f), 180f, 3f, 2f, "M_Canvas_Night", "M_Canvas_Sunset");
        Painting(g, new Vector3(12f, FL + 4.8f, 19.8f), 180f, 3f, 2f, "M_Canvas_Sea", "M_Canvas_Palm");
        // часы над кассами
        var clock = GameObject.CreatePrimitive(PrimitiveType.Cylinder); clock.name = "WallClock"; clock.transform.SetParent(g, false);
        clock.transform.SetPositionAndRotation(new Vector3(-5.5f, FL + 5.4f, 19.78f), Quaternion.Euler(90, 0, 0)); clock.transform.localScale = new Vector3(1.3f, 0.06f, 1.3f);
        clock.GetComponent<Renderer>().sharedMaterial = M("M_Gold"); Object.DestroyImmediate(clock.GetComponent<Collider>());
        Block("ClockFace", new Vector3(-5.5f, FL + 5.4f, 19.72f), new Vector3(1.0f, 1.0f, 0.01f), "M_Paper", g, false);
        Rot(Block("ClockHandH", new Vector3(-5.5f, FL + 5.52f, 19.7f), new Vector3(0.05f, 0.3f, 0.01f), "M_Black", g, false), 0);
        Block("ClockHandM", new Vector3(-5.35f, FL + 5.4f, 19.7f), new Vector3(0.36f, 0.04f, 0.01f), "M_Black", g, false);
        // табло курсов валют
        Block("RatesBoard", new Vector3(5.5f, FL + 5.4f, 19.78f), new Vector3(3.2f, 1.4f, 0.08f), "M_Black", g, false);
        for (int r = 0; r < 4; r++) Block("RateRow", new Vector3(5.5f, FL + 5.85f - r * 0.3f, 19.73f), new Vector3(2.8f, 0.12f, 0.01f), r == 0 ? "M_ExitGreen" : "M_Amber", g, false);
        // банкоматы у восточной стены за кассовой зоной
        Atm(g, new Vector3(15.45f, FL, 15.2f), 90f);
        Atm(g, new Vector3(15.45f, FL, 16.4f), 90f);
        Block("AtmSign", new Vector3(15.8f, FL + 2.6f, 15.8f), new Vector3(0.05f, 0.35f, 2f), "M_Canvas_Sea", g, false);
        // пост охраны у входа
        Block("GuardDesk", new Vector3(-6.5f, FL + 0.55f, 2.4f), new Vector3(2.6f, 1.1f, 0.7f), "M_Wood", g);
        Block("GuardDeskSide", new Vector3(-7.65f, FL + 0.55f, 3.2f), new Vector3(0.6f, 1.1f, 1.6f), "M_Wood", g);
        Block("GuardTop", new Vector3(-6.7f, FL + 1.12f, 2.7f), new Vector3(3.0f, 0.05f, 1.4f), "M_Marble", g, false);
        for (int i = 0; i < 3; i++) Rot(Block("GuardMonitor", new Vector3(-7.3f + i * 0.6f, FL + 1.45f, 3.0f), new Vector3(0.5f, 0.35f, 0.04f), "M_Screen", g, false), 180f);
        House("Chair_2", new Vector3(-6.6f, FL, 3.6f), 180f, g, 1.05f);
        House("Light_Desk", new Vector3(-5.5f, FL + 1.14f, 2.6f), 0, g, 0.5f);
        // стойки с буклетами у стола для бланков
        foreach (float x in new[] { -2.4f, 2.4f })
        {
            Block("BrochureStand", new Vector3(x, FL + 0.6f, 9.5f), new Vector3(0.5f, 1.2f, 0.3f), "M_Gold", g);
            for (int k = 0; k < 3; k++) Block("Brochure", new Vector3(x, FL + 0.5f + k * 0.3f, 9.33f), new Vector3(0.4f, 0.22f, 0.03f), k == 1 ? "M_Canvas_Sunset" : "M_Canvas_Sea", g, false);
        }
        Bench(g, new Vector3(-5.2f, FL, 6.5f), 90f, 2.2f);
        Bench(g, new Vector3(5.2f, FL, 6.5f), -90f, 2.2f);
        foreach (float s in new[] { -1f, 1f })
        {
            House("Trashcan_Small1", new Vector3(s * 14.4f, FL, 11f), 0, g, 0.6f);
            House("Houseplant_" + (s < 0 ? 1 : 5), new Vector3(s * 10.6f, FL, 1.0f), 0, g, 1.5f);
            House("Houseplant_8", new Vector3(s * 9.0f, FL, 13.6f), 0, g, 1.3f);
            Block("CoffeeTableMag", new Vector3(s * 12.5f, FL + 0.6f, 6.5f), new Vector3(0.35f, 0.02f, 0.25f), "M_Canvas_Sunset", g, false);
        }
        Block("FloorMedallion", new Vector3(0, FL + 0.013f, 13.2f), new Vector3(2.4f, 0.01f, 0.5f), "M_Gold", g, false);
        // ---------- кассы: на стойках ручки и таблички, за стойками деньги, счётчики купюр, тележка
        for (int i = 0; i < 6; i++)
        {
            float x = -7.5f + i * 3f;
            Block("PenHolder", new Vector3(x + 1.1f, FL + 1.2f, 14.7f), new Vector3(0.08f, 0.15f, 0.08f), "M_Gold", g, false);
            Block("TellerPlate", new Vector3(x - 1.0f, FL + 1.2f, 14.62f), new Vector3(0.5f, 0.14f, 0.04f), "M_Canvas_Sea", g, false);
            Block("CountMachine", new Vector3(x + 0.1f, FL + 0.85f, 16.15f), new Vector3(0.35f, 0.18f, 0.3f), "M_Paper", g, false);
            CashStack(g, new Vector3(x - 0.8f, FL + 0.76f, 16.0f), (i % 3) + 1);
            House("Light_Desk", new Vector3(x + 0.9f, FL + 0.77f, 16.4f), 0, g, 0.45f);
        }
        foreach (float x in new[] { -8.6f, 8.6f }) House("Drawer_2", new Vector3(x, FL, 18.9f), 180f, g, 1.1f);
        Block("CashCart", new Vector3(0, FL + 0.45f, 18.4f), new Vector3(1.2f, 0.7f, 0.7f), "M_DarkSteel", g);
        CashStack(g, new Vector3(-0.3f, FL + 0.8f, 18.4f), 3); CashStack(g, new Vector3(0.3f, FL + 0.8f, 18.4f), 2);
        // ---------- коридор: огнетушители, таблички «ВЫХОД», картины, лавка, кулер
        Extinguisher(g, new Vector3(-6.4f, FL, 23.7f)); Extinguisher(g, new Vector3(6.6f, FL, 20.35f));
        ExitSign(g, new Vector3(15.8f, FL + 3.0f, 22f), -90f);
        ExitSign(g, new Vector3(-10f, FL + 3.25f, 20.2f), 0f);
        Painting(g, new Vector3(-5f, FL + 1.8f, 20.2f), 0f, 1.6f, 1.0f, "M_Canvas_Sea", "M_Canvas_Palm");
        Painting(g, new Vector3(3f, FL + 1.8f, 20.2f), 0f, 1.6f, 1.0f, "M_Canvas_Sunset", "M_Canvas_Night");
        Painting(g, new Vector3(6f, FL + 1.8f, 23.8f), 180f, 1.2f, 0.9f, "M_Canvas_Palm", "M_Canvas_Sea");
        Bench(g, new Vector3(1.5f, FL, 23.45f), 0f, 2f);
        Block("NoticeBoard", new Vector3(-13.5f, FL + 1.7f, 23.82f), new Vector3(1.6f, 1.0f, 0.05f), "M_Wood", g, false);
        for (int i = 0; i < 5; i++) Block("Note", new Vector3(-14.1f + i * 0.3f, FL + 1.6f + (i % 2) * 0.25f, 23.78f), new Vector3(0.22f, 0.26f, 0.01f), i % 2 == 0 ? "M_Paper" : "M_Canvas_Sun", g, false);
        Block("Cooler", new Vector3(14.6f, FL + 0.55f, 20.55f), new Vector3(0.45f, 1.1f, 0.45f), "M_Paper", g);
        var bottle = GameObject.CreatePrimitive(PrimitiveType.Cylinder); bottle.name = "CoolerBottle"; bottle.transform.SetParent(g, false);
        bottle.transform.position = new Vector3(14.6f, FL + 1.35f, 20.55f); bottle.transform.localScale = new Vector3(0.32f, 0.25f, 0.32f);
        bottle.GetComponent<Renderer>().sharedMaterial = M("M_Glass"); Object.DestroyImmediate(bottle.GetComponent<Collider>());
        // ---------- кабинет директора: сейф с циферблатом, глобус, кресла, картина, торшер
        Block("Safe", new Vector3(-14.6f, FL + 0.55f, 31.2f), new Vector3(1.1f, 1.1f, 0.9f), "M_DarkSteel", g);
        Block("SafeDial", new Vector3(-14.6f, FL + 0.65f, 30.73f), new Vector3(0.2f, 0.2f, 0.03f), "M_Gold", g, false);
        Block("SafeHandle", new Vector3(-14.3f, FL + 0.45f, 30.73f), new Vector3(0.05f, 0.25f, 0.04f), "M_Steel", g, false);
        var globe = GameObject.CreatePrimitive(PrimitiveType.Sphere); globe.name = "Globe"; globe.transform.SetParent(g, false);
        globe.transform.position = new Vector3(-7.3f, FL + 1.05f, 31f); globe.transform.localScale = Vector3.one * 0.55f;
        globe.GetComponent<Renderer>().sharedMaterial = M("M_Canvas_Sea"); Object.DestroyImmediate(globe.GetComponent<Collider>());
        Block("GlobeStand", new Vector3(-7.3f, FL + 0.38f, 31f), new Vector3(0.08f, 0.76f, 0.08f), "M_Gold", g, false);
        House("Chair_4", new Vector3(-12f, FL, 27.6f), 0f, g, 1.0f);
        House("Chair_4", new Vector3(-10f, FL, 27.6f), 0f, g, 1.0f);
        Painting(g, new Vector3(-11f, FL + 2.2f, 31.8f), 180f, 2.0f, 1.2f, "M_Canvas_Sun", "M_Canvas_Sunset");
        House("Light_Floor1", new Vector3(-15.2f, FL, 25f), 0, g, 1.9f);
        House("Houseplant_6", new Vector3(-6.8f, FL, 24.8f), 0, g, 1.4f);
        // ---------- комната отдыха: торговый автомат, шкафчики, микроволновка, кофемашина, телевизор
        Block("Vending", new Vector3(4.9f, FL + 1.0f, 31.2f), new Vector3(1.0f, 2.0f, 0.8f), "M_Red", g);
        Block("VendingGlass", new Vector3(4.9f, FL + 1.2f, 30.79f), new Vector3(0.7f, 1.3f, 0.02f), "M_Screen", g, false);
        for (int i = 0; i < 4; i++)
        {
            Block("Locker", new Vector3(5.6f, FL + 0.95f, 25.0f + i * 0.62f), new Vector3(0.5f, 1.9f, 0.6f), "M_Canvas_Sea", g);
            Block("LockerVent", new Vector3(5.34f, FL + 1.6f, 25.0f + i * 0.62f), new Vector3(0.02f, 0.2f, 0.4f), "M_DarkSteel", g, false);
        }
        Block("Microwave", new Vector3(-3.5f, FL + 1.12f, 31.5f), new Vector3(0.55f, 0.32f, 0.38f), "M_Paper", g, false);
        Block("CoffeeMachine", new Vector3(-2.8f, FL + 1.15f, 31.55f), new Vector3(0.3f, 0.38f, 0.3f), "M_Black", g, false);
        Block("TV", new Vector3(2.5f, FL + 2.1f, 24.25f), new Vector3(1.6f, 0.95f, 0.06f), "M_Screen", g, false);
        House("Trashcan_Green", new Vector3(-5.4f, FL, 25f), 0, g, 0.7f);
        Block("Cup", new Vector3(-1.8f, FL + 0.8f, 27.4f), new Vector3(0.08f, 0.1f, 0.08f), "M_Paper", g, false);
        // ---------- серверная: второй ряд стоек, рабочее место, кондиционер, кабельный лоток
        for (int i = 0; i < 3; i++)
        {
            float x = 9f + i * 2f;
            Block("Rack2_" + i, new Vector3(x, FL + 1.1f, 27.4f), new Vector3(1.1f, 2.2f, 1f), "M_DarkSteel", g);
            for (int k = 0; k < 6; k++) Block("Led", new Vector3(x - 0.3f + (k % 3) * 0.3f, FL + 0.6f + k / 3 * 0.9f, 26.88f), new Vector3(0.12f, 0.04f, 0.02f), k == 4 ? "M_Amber" : "M_LedGreen", g, false);
        }
        Block("ServerDesk", new Vector3(14.8f, FL + 0.38f, 25.2f), new Vector3(1.4f, 0.76f, 1.6f), "M_Wood", g);
        for (int i = 0; i < 2; i++) Rot(Block("ServerMonitor", new Vector3(15.2f, FL + 1.0f, 24.8f + i * 0.7f), new Vector3(0.05f, 0.4f, 0.6f), "M_Screen", g, false), 0);
        Block("AC", new Vector3(15.7f, FL + 2.7f, 28.5f), new Vector3(0.4f, 0.6f, 1.6f), "M_Paper", g, false);
        Block("CableTray", new Vector3(11f, FL + 3.1f, 29f), new Vector3(8f, 0.06f, 0.4f), "M_Steel", g, false);
        // ---------- тамбур: пульт у двери хранилища, предупреждающие полосы
        Block("VaultPanel", new Vector3(23.5f, FL + 1.3f, 26.6f), new Vector3(0.12f, 0.7f, 0.5f), "M_DarkSteel", g, false);
        for (int i = 0; i < 3; i++) Block("PanelBtn", new Vector3(23.43f, FL + 1.45f - i * 0.18f, 26.6f), new Vector3(0.02f, 0.1f, 0.1f), i == 0 ? "M_Red" : "M_Amber", g, false);
        Block("WarnStripe", new Vector3(23.55f, FL + 3.4f, 24f), new Vector3(0.06f, 0.18f, 6f), "M_Hazard", g, false);
        // ---------- хранилище: мешки с деньгами, паллеты купюр, витрина с бриллиантом
        MoneyBag(g, new Vector3(25.0f, FL, 21.1f)); MoneyBag(g, new Vector3(25.5f, FL, 21.4f), 0.8f); MoneyBag(g, new Vector3(25.0f, FL, 26.9f), 1.1f);
        foreach (var p in new[] { new Vector3(31.0f, FL, 21.0f), new Vector3(31.0f, FL, 27.0f) })
        {
            Block("Pallet", p + Vector3.up * 0.07f, new Vector3(1.1f, 0.14f, 1.1f), "M_Wood", g);
            for (int l = 0; l < 4; l++) for (int i = 0; i < 2; i++) for (int k = 0; k < 2; k++)
                Block("CashBrick", p + new Vector3(-0.25f + i * 0.5f, 0.24f + l * 0.2f, -0.25f + k * 0.5f), new Vector3(0.46f, 0.18f, 0.46f), (l + i + k) % 2 == 0 ? "M_Cash" : "M_CashBand", g, false);
        }
        Block("GemCase", new Vector3(28f, FL + 1.25f, 24f), new Vector3(0.6f, 0.6f, 0.6f), "M_Glass", g, false);
        var gem = GameObject.CreatePrimitive(PrimitiveType.Sphere); gem.name = "Diamond"; gem.transform.SetParent(g, false);
        gem.transform.SetPositionAndRotation(new Vector3(28f, FL + 1.2f, 24f), Quaternion.Euler(0, 45, 0)); gem.transform.localScale = new Vector3(0.25f, 0.32f, 0.25f);
        gem.GetComponent<Renderer>().sharedMaterial = M("M_Diamond"); Object.DestroyImmediate(gem.GetComponent<Collider>()); gem.tag = "EditorOnly";
        // ---------- гараж: конусы, ящики, бочки, покрышки, полосы на стенах
        foreach (var p in new[] { new Vector3(20f, FL, 6f), new Vector3(20.8f, FL, 4.5f), new Vector3(28f, FL, 4.8f) })
        {
            var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cone.name = "Cone"; cone.transform.SetParent(g, false);
            cone.transform.position = p + Vector3.up * 0.35f; cone.transform.localScale = new Vector3(0.3f, 0.35f, 0.3f);
            cone.GetComponent<Renderer>().sharedMaterial = M("M_Orange"); Object.DestroyImmediate(cone.GetComponent<Collider>());
            Block("ConeBase", p + Vector3.up * 0.03f, new Vector3(0.45f, 0.06f, 0.45f), "M_Black", g, false);
        }
        foreach (var (p, s) in new[] { (new Vector3(30.8f, FL, 17.8f), 1.1f), (new Vector3(29.6f, FL, 18.2f), 0.9f), (new Vector3(30.8f, FL + 1.1f, 17.8f), 0.8f) })
        {
            Block("Crate", p + Vector3.up * s / 2, Vector3.one * s, "M_Wood", g);
            Block("CrateBand", p + Vector3.up * s / 2, new Vector3(s + 0.02f, 0.1f, s + 0.02f), "M_DarkSteel", g, false);
        }
        foreach (var p in new[] { new Vector3(17.2f, FL, 17.5f), new Vector3(17.9f, FL, 18.4f) })
        {
            var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder); d.name = "Barrel"; d.transform.SetParent(g, false);
            d.transform.position = p + Vector3.up * 0.45f; d.transform.localScale = new Vector3(0.6f, 0.45f, 0.6f);
            d.GetComponent<Renderer>().sharedMaterial = M("M_Canvas_Sea");
        }
        for (int i = 0; i < 3; i++)
        {
            var t = GameObject.CreatePrimitive(PrimitiveType.Cylinder); t.name = "Tire"; t.transform.SetParent(g, false);
            t.transform.position = new Vector3(17.2f, FL + 0.12f + i * 0.24f, 14f); t.transform.localScale = new Vector3(0.75f, 0.12f, 0.75f);
            t.GetComponent<Renderer>().sharedMaterial = M("M_Black"); Object.DestroyImmediate(t.GetComponent<Collider>());
        }
        Block("WallStripeE", new Vector3(31.98f, FL + 0.5f, 10f), new Vector3(0.02f, 0.3f, 19f), "M_Hazard", g, false);
        Block("OilStain", new Vector3(22.5f, FL + 0.006f, 8f), new Vector3(1.4f, 0.005f, 1.0f), "M_Black", g, false);
        ExitSign(g, new Vector3(24f, FL + 4.3f, 0.2f), 0f);
        // ---------- улица: лавки, гидранты, урны, газетный автомат, клумбы, пешеходный переход, остановка
        Bench(g, new Vector3(-12f, 0.14f, -4.6f), 0f, 2.2f);
        Bench(g, new Vector3(12f, 0.14f, -4.6f), 0f, 2.2f);
        foreach (float x in new[] { -18f, 15.5f })
        {
            Block("Hydrant", new Vector3(x, 0.5f, -5.4f), new Vector3(0.3f, 0.7f, 0.3f), "M_Red", g);
            Block("HydrantCap", new Vector3(x, 0.9f, -5.4f), new Vector3(0.36f, 0.1f, 0.36f), "M_Red", g, false);
        }
        House("Trashcan_Cylindric", new Vector3(-10.4f, 0.14f, -5.3f), 0, g, 0.9f);
        House("Trashcan_Cylindric", new Vector3(10.4f, 0.14f, -5.3f), 0, g, 0.9f);
        Block("NewsBox", new Vector3(-14f, 0.65f, -5.4f), new Vector3(0.5f, 1.0f, 0.45f), "M_Canvas_Sea", g);
        Block("NewsBoxWin", new Vector3(-14f, 0.85f, -5.64f), new Vector3(0.35f, 0.3f, 0.02f), "M_Paper", g, false);
        for (int i = 0; i < 7; i++) Block("Crosswalk", new Vector3(19f, 0.012f, -7.5f - i * 0.9f), new Vector3(3.2f, 0.01f, 0.45f), "M_Line", g, false);
        foreach (float x in new[] { -4.5f, 4.5f }) House("Houseplant_4", new Vector3(x, 0.14f, -5.2f), 0, g, 1.2f);
        // остановка на той стороне улицы
        Block("BusShelterRoof", new Vector3(-30f, 2.6f, -19.6f), new Vector3(4f, 0.1f, 1.6f), "M_Canvas_Sea", g, false);
        foreach (float dx in new[] { -1.9f, 1.9f }) Block("BusShelterPost", new Vector3(-30f + dx, 1.35f, -19.0f), new Vector3(0.08f, 2.5f, 0.08f), "M_Steel", g);
        Block("BusShelterBack", new Vector3(-30f, 1.4f, -20.3f), new Vector3(3.8f, 2.1f, 0.04f), "M_Glass", g, false);
        Bench(g, new Vector3(-30f, 0.14f, -19.9f), 180f, 2.6f);
        Block("BusAd", new Vector3(-28.6f, 1.4f, -20.27f), new Vector3(1f, 1.6f, 0.02f), "M_Canvas_Sunset", g, false);
    }

    // ================================================================== свет (Mobile URP: мало источников, без теней)
    static void BuildLights()
    {
        var g = Group("Lights");
        void Pt(string n, Vector3 p, Color c, float range, float intensity)
        {
            var l = new GameObject(n, typeof(Light)).GetComponent<Light>(); l.transform.SetParent(g, false); l.transform.position = p;
            l.type = LightType.Point; l.color = c; l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
        }
        var warm = new Color(1f, 0.86f, 0.66f);
        Pt("HallLight_W", new Vector3(-5f, FL + 5f, 8f), warm, 16f, 2.2f);
        Pt("HallLight_E", new Vector3(5f, FL + 5f, 8f), warm, 16f, 2.2f);
        Pt("TellerLight", new Vector3(0f, FL + 4f, 16f), warm, 12f, 1.6f);
        Pt("CorridorLight", new Vector3(0f, FL + 2.9f, 22f), new Color(1f, 0.95f, 0.85f), 18f, 1.4f);
        Pt("DirectorLight", new Vector3(-11f, FL + 2.8f, 28f), warm, 8f, 1.4f);
        Pt("BreakLight", new Vector3(-1f, FL + 2.8f, 28f), warm, 8f, 1.4f);
        Pt("ServerLight", new Vector3(11f, FL + 2.8f, 28f), new Color(0.6f, 0.85f, 1f), 8f, 1.3f);
        Pt("AnteLight", new Vector3(20f, FL + 3.3f, 24f), new Color(1f, 0.5f, 0.5f), 8f, 1.2f);
        Pt("VaultLight", new Vector3(28f, FL + 3.3f, 24f), new Color(1f, 0.85f, 0.55f), 9f, 2f);
        Pt("GarageLight", new Vector3(24f, FL + 4.3f, 10f), new Color(1f, 0.78f, 0.45f), 14f, 1.6f);
        Pt("PorticoLight", new Vector3(0f, FL + 6f, -2f), new Color(1f, 0.6f, 0.7f), 10f, 1.4f);
    }

    // ================================================================== коллайдеры для рук GorillaLocomotion
    static void AddColliders()
    {
        int mc = 0;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            var go = mf.gameObject;
            if (go.GetComponent<Collider>() || go.CompareTag("EditorOnly") || !mf.sharedMesh) continue;
            if (go.name.Contains("Glass") || go.name.StartsWith("Lane") || go.name.StartsWith("Led") || go.name.StartsWith("Rope") || go.name.StartsWith("Chain") || go.name.StartsWith("Laser")) continue;
            if (go.transform.IsChildOf(root.Find("VaultWing/Lasers"))) continue;    // в зоне лазеров коллайдеров нет (кроме стоек снизу)
            var b = mf.sharedMesh.bounds.size; var s = go.transform.lossyScale;
            if (Mathf.Abs(b.x * s.x * b.y * s.y * b.z * s.z) < 0.002f) continue;    // мелочь (ложки, лампочки)
            var c = go.AddComponent<MeshCollider>(); c.sharedMesh = mf.sharedMesh; mc++;
            go.layer = 0;
        }
        log.AppendLine($"  mesh colliders added: {mc}");
    }

    // ================================================================== превью
    static void SetupPreviewEnvironment()
    {
        var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Bank/M_Bank_SunsetSky.mat");
        if (sky) RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.85f, 0.7f, 0.75f);
        RenderSettings.ambientEquatorColor = new Color(0.65f, 0.55f, 0.5f);
        RenderSettings.ambientGroundColor = new Color(0.3f, 0.27f, 0.25f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 60; RenderSettings.fogEndDistance = 220;
        RenderSettings.fogColor = new Color(0.98f, 0.72f, 0.68f);
        var sun = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
        sun.type = LightType.Directional; sun.color = new Color(1f, 0.78f, 0.6f); sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(30f, 35f, 0f);    // низкое закатное солнце с юго-запада
        RenderSettings.sun = sun;
        RenderSettings.reflectionIntensity = 0f;   // в пустой сцене отражения берутся со стандартного синего неба
        DynamicGI.UpdateEnvironment();      // иначе новая сцена освещается стандартным синим небом
    }

    static void RenderShots()
    {
        Directory.CreateDirectory("HubPreview");
        var shots = new (string name, Vector3 pos, Vector3 look, float fov)[]
        {
            ("BankQ_1_Street",   new Vector3(-5f, 2.4f, -16.5f), new Vector3(0f, 5f, 0f), 66f),
            ("BankQ_2_Hall",     new Vector3(-12f, FL + 1.7f, 1.2f), new Vector3(4f, FL + 2.2f, 16f), 70f),
            ("BankQ_3_Tellers",  new Vector3(4f, FL + 1.6f, 10.5f), new Vector3(-2f, FL + 1.4f, 16f), 62f),
            ("BankQ_4_Corridor", new Vector3(-14.5f, FL + 1.6f, 22f), new Vector3(16f, FL + 1.4f, 22f), 68f),
            ("BankQ_5_Lasers",   new Vector3(16.6f, FL + 1.6f, 21f), new Vector3(24f, FL + 1.5f, 24f), 72f),
            ("BankQ_6_Vault",    new Vector3(24.8f, FL + 2.0f, 21.2f), new Vector3(30.5f, FL + 1.0f, 25.5f), 74f),
            ("BankQ_7_Director", new Vector3(-7f, FL + 1.8f, 25f), new Vector3(-13f, FL + 0.9f, 30.5f), 74f),
            ("BankQ_8_BreakRoom",new Vector3(-5.2f, FL + 1.8f, 24.8f), new Vector3(3f, FL + 0.9f, 30f), 74f),
            ("BankQ_9_Garage",   new Vector3(17f, FL + 2.4f, 1.5f), new Vector3(27f, FL + 0.6f, 14f), 70f),
        };
        var camGo = new GameObject("ShotCam"); var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox; cam.nearClipPlane = 0.05f; cam.farClipPlane = 600f;
        var rt = new RenderTexture(1600, 900, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
        // первый кадр сессии в batchmode выходит без освещения (чёрный) — прогреваем рендер холостыми кадрами
        cam.transform.SetPositionAndRotation(shots[0].pos, Quaternion.LookRotation(shots[0].look - shots[0].pos));
        for (int i = 0; i < 2; i++) cam.Render();
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
