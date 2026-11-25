using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using static MapKit;

/// <summary>
/// Главный Хаб (Assets/Scenes/MainHub.unity) — убежище грабителей в стиле GILLA / Gorilla Tag:
/// кирпичный ангар 24×24 м, антресоль и мостик (2-й уровень), балки под потолком для паркура,
/// Гардероб (Tripo-сейф, зеркало, цвета, витрина косметики), Стол Выбора Режимов (Tripo-стол + 4 кнопки),
/// Фургон-портал на миссию, баскетбол со счётчиком голов, кучи денег.
/// Меню: Tools/Heist/Build Main Hub. Игроков берёт из HeistLevel_Test (копия сцены), карту ограбления удаляет.
/// </summary>
public static class MainHubBuilder
{
    const string HubPath = "Assets/Scenes/MainHub.unity";
    const string HeistPath = "Assets/Scenes/HeistLevel_Test.unity";
    const string Gen = "Assets/Art/GeneratedModels";
    const string ImpactDir = "Assets/Audio/Kenney_ImpactSounds";
    const string UiDir = "Assets/Audio/Kenney_InterfaceSounds";

    static readonly Color Warm = new Color(1f, 0.76f, 0.48f);
    static AudioClip[] click;
    static Material brick, buttonLamp;

    static readonly (string name, Color color)[] SkinColors =
    {
        ("ЧЁРНЫЙ",  new Color(0.07f, 0.07f, 0.08f)),
        ("ЗОЛОТОЙ", new Color(0.95f, 0.70f, 0.18f)),
        ("КРАСНЫЙ", new Color(0.80f, 0.10f, 0.10f)),
        ("НЕОН",    new Color(0.30f, 1.00f, 0.12f)),
        ("СИНИЙ",   new Color(0.15f, 0.35f, 0.95f)),
        ("ФИОЛЕТ",  new Color(0.55f, 0.20f, 0.80f)),
    };

    [MenuItem("Tools/Heist/Build Main Hub")]
    public static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        Init();
        click = Clips(UiDir, "click");
        brick = BrickMaterial();
        buttonLamp = LampMaterial();

        // 1) Сцена: копия HeistLevel_Test (там уже настроены игроки, XR-симулятор и переключатель режимов)
        EditorSceneManager.SaveOpenScenes();
        var heist = EditorSceneManager.OpenScene(HeistPath, OpenSceneMode.Single);
        foreach (var g in heist.GetRootGameObjects().Where(g => g.name == "GeneratedProps").ToArray())
            Object.DestroyImmediate(g); // тестовый терминал больше не нужен
        EditorSceneManager.SaveScene(heist);
        if (!File.Exists(HubPath)) AssetDatabase.CopyAsset(HeistPath, HubPath);
        var scene = EditorSceneManager.OpenScene(HubPath, OpenSceneMode.Single);
        foreach (var g in scene.GetRootGameObjects().Where(g => new[] { "HeistMap", "HeistGameManager", "GeneratedProps", "MainHubMap" }.Contains(g.name)).ToArray())
            Object.DestroyImmediate(g);
        GameObject Root(string n) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == n);
        var switcher = Root("PlayerModeSwitcher")?.GetComponent<PlayerModeSwitcher>();

        var map = new GameObject("MainHubMap").transform;
        var st = Group(map, "Static");
        var dy = Group(map, "Dynamic");
        var hub = dy.gameObject.AddComponent<HubController>();

        BuildRoom(st);
        BuildSecondLevel(st);
        BuildParkour(st);
        BuildModeTable(st, dy, hub);
        BuildVan(st, dy, hub, switcher);
        BuildWardrobe(st, dy, hub);
        BuildBasketball(st, dy);
        BuildDecor(st);
        BuildLights(map);

        // Игроки — на точку спавна, лицом к столу
        var spawn = new Vector3(0, 0, -7f);
        foreach (var n in new[] { "DesktopTestPlayer", "VRGorillaPlayer" })
        {
            var p = Root(n);
            if (p) { p.transform.SetPositionAndRotation(spawn, Quaternion.identity); EditorUtility.SetDirty(p.transform); }
        }
        var sun = Root("Directional Light")?.GetComponent<Light>();
        if (sun) { sun.intensity = 0.25f; sun.color = new Color(1f, 0.85f, 0.7f); sun.shadows = LightShadows.None; sun.transform.rotation = Quaternion.Euler(55, 35, 0); }
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.011f;
        RenderSettings.fogColor = new Color(0.16f, 0.12f, 0.09f);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.42f, 0.34f, 0.27f);
        RenderSettings.ambientEquatorColor = new Color(0.30f, 0.23f, 0.17f);
        RenderSettings.ambientGroundColor = new Color(0.14f, 0.11f, 0.09f);

        MarkStatic(st);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        // Build Settings: MainHub первой, HeistLevel_Test второй, остальные (SampleScene напарника) — следом
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(HubPath, true), new EditorBuildSettingsScene(HeistPath, true) };
        list.AddRange(EditorBuildSettings.scenes.Where(s => s.path != HubPath && s.path != HeistPath));
        EditorBuildSettings.scenes = list.ToArray();
        AssetDatabase.SaveAssets();

        int objects = map.GetComponentsInChildren<Transform>(true).Length;
        return $"[MainHub] Built: {objects} objects, {map.GetComponentsInChildren<Light>(true).Length} lights; build scenes: " +
               string.Join(", ", EditorBuildSettings.scenes.Select(s => Path.GetFileNameWithoutExtension(s.path)));
    }

    // =========================================================================== комната
    static void BuildRoom(Transform st)
    {
        Box(st, "Floor", new Vector3(0, -0.1f, 2), new Vector3(24.4f, 0.2f, 24.4f), "ConcreteDark");
        Box(st, "Ceiling", new Vector3(0, 8.1f, 2), new Vector3(24.4f, 0.2f, 24.4f), "ConcreteDark");
        B(st, "Wall_N", new Vector3(0, 4, 14.0f), new Vector3(24.4f, 8, 0.4f), brick);
        B(st, "Wall_S", new Vector3(0, 4, -10.0f), new Vector3(24.4f, 8, 0.4f), brick);
        B(st, "Wall_W", new Vector3(-12.0f, 4, 2), new Vector3(0.4f, 8, 24.4f), brick);
        B(st, "Wall_E", new Vector3(12.0f, 4, 2), new Vector3(0.4f, 8, 24.4f), brick);
        // плинтус-полоса и ковры
        foreach (var (n, p, s) in new[] {
            ("Skirt_N", new Vector3(0, 0.15f, 13.78f), new Vector3(24f, 0.3f, 0.05f)), ("Skirt_S", new Vector3(0, 0.15f, -9.78f), new Vector3(24f, 0.3f, 0.05f)),
            ("Skirt_W", new Vector3(-11.78f, 0.15f, 2), new Vector3(0.05f, 0.3f, 24f)), ("Skirt_E", new Vector3(11.78f, 0.15f, 2), new Vector3(0.05f, 0.3f, 24f)) })
            Box(st, n, p, s, "Hazard", false);
        Box(st, "Rug_Table", new Vector3(0, 0.012f, 1.6f), new Vector3(5.5f, 0.02f, 4.4f), "CarpetRed", false);
        Box(st, "Rug_Wardrobe", new Vector3(-9.6f, 0.012f, 2.2f), new Vector3(4f, 0.02f, 11f), "CarpetBlue", false);
        Box(st, "Rug_Spawn", new Vector3(0, 0.012f, -7f), new Vector3(3f, 0.02f, 3f), "CarpetRed", false);

        // Большая вывеска над мостиком
        Text(st, "HubTitle", "GRABZ · УБЕЖИЩЕ ГРАБИТЕЛЕЙ", 6f, new Color(1f, 0.62f, 0.15f), new Vector3(-2f, 6.0f, 13.76f), Facing(Vector3.back), new Vector2(13f, 1.3f));
        Box(st, "HubTitleBack", new Vector3(-2f, 6.0f, 13.79f), new Vector3(13.4f, 1.5f, 0.02f), "DarkMetal", false);

        // Табличка у спавна
        var sign = Group(st, "RulesSign");
        sign.SetPositionAndRotation(new Vector3(-2.8f, 0, -5.2f), Quaternion.LookRotation(new Vector3(0.45f, 0, -1f)));
        Box(sign, "Post", new Vector3(0, 0.8f, 0.05f), new Vector3(0.08f, 1.6f, 0.08f), "DarkMetal");
        Box(sign, "Board", new Vector3(0, 1.55f, 0), new Vector3(1.9f, 1.1f, 0.05f), "Cork", false);
        Text(sign, "Rules", "<b>ДОБРО ПОЖАЛОВАТЬ В УБЕЖИЩЕ!</b>\n1. СТОЛ ПЛАНИРОВАНИЯ — ВЫБЕРИ РЕЖИМ\n2. ГАРДЕРОБ СЛЕВА — ЦВЕТ, ЗЕРКАЛО, КОСМЕТИКА\n3. ЗАПРЫГНИ В ФУРГОН — ПОЕХАЛИ НА ДЕЛО!\n4. БАСКЕТБОЛ СПРАВА — РАЗМИНКА",
            1.1f, new Color(0.15f, 0.08f, 0.03f), new Vector3(0, 1.55f, 0.03f), Quaternion.Euler(0, 180, 0), new Vector2(1.75f, 0.95f), TextAlignmentOptions.Left, false);
    }

    static void BuildSecondLevel(Transform st)
    {
        // Антресоль вдоль восточной стены (над баскетболом — свободно)
        Box(st, "Mezz_Deck", new Vector3(10.2f, 3.475f, 6.4f), new Vector3(3.2f, 0.25f, 14.8f), "WoodDark");
        foreach (float z in new[] { 0f, 6f, 12f }) Cyl(st, "Mezz_Column", new Vector3(8.85f, 1.68f, z), 0.15f, 3.35f, "DarkMetal");
        for (float z = 4.6f; z <= 13.6f; z += 1.5f) Box(st, "Mezz_Post", new Vector3(8.7f, 4.1f, z), new Vector3(0.06f, 1f, 0.06f), "Metal", false);
        Box(st, "Mezz_Rail", new Vector3(8.7f, 4.6f, 9.1f), new Vector3(0.08f, 0.08f, 9.2f), "Metal");
        // Ступени-ящики на антресоль
        Box(st, "Crate_1", new Vector3(7.6f, 0.6f, 0.4f), Vector3.one * 1.2f, "WoodLight");
        Box(st, "Crate_2a", new Vector3(7.6f, 0.6f, 1.7f), Vector3.one * 1.2f, "WoodLight");
        Box(st, "Crate_2b", new Vector3(7.6f, 1.8f, 1.7f), Vector3.one * 1.2f, "WoodLight");
        for (int i = 0; i < 3; i++) Box(st, "Crate_3" + i, new Vector3(7.6f, 0.6f + i * 1.2f, 3.0f), Vector3.one * 1.2f, "WoodLight");
        // Мостик вдоль северной стены
        Box(st, "Catwalk_Deck", new Vector3(-1.6f, 3.475f, 13.1f), new Vector3(20.4f, 0.25f, 1.4f), "DarkMetal");
        for (float x = -11.4f; x <= 8.4f; x += 1.65f) Box(st, "Catwalk_Post", new Vector3(x, 4.1f, 12.45f), new Vector3(0.06f, 1f, 0.06f), "Metal", false);
        Box(st, "Catwalk_Rail", new Vector3(-1.6f, 4.6f, 12.45f), new Vector3(20.2f, 0.08f, 0.08f), "Metal");
        foreach (float x in new[] { -10f, -5f }) Cyl(st, "Catwalk_Column", new Vector3(x, 1.68f, 12.7f), 0.12f, 3.35f, "DarkMetal");
    }

    static void BuildParkour(Transform st)
    {
        // Балки под потолком: поперечные и две продольные пониже
        foreach (float z in new[] { -6f, -2f, 2f, 6f, 10f }) Box(st, "Beam_X", new Vector3(0, 6.8f, z), new Vector3(23.6f, 0.35f, 0.3f), "DarkMetal");
        foreach (float x in new[] { -4f, 4f }) Box(st, "Beam_Z", new Vector3(x, 6.45f, 2f), new Vector3(0.3f, 0.35f, 23.6f), "Metal");
        // Колонны с латунными кольцами-зацепами (по ним можно залезть на балки)
        foreach (var p in new[] { new Vector3(-6f, 0, -3f), new Vector3(6f, 0, -3f), new Vector3(-6f, 0, 7f) })
        {
            Cyl(st, "Column", p + Vector3.up * 3.3f, 0.2f, 6.6f, "DarkMetal");
            for (float y = 1.0f; y < 6.4f; y += 1.1f) Cyl(st, "GripRing", p + Vector3.up * y, 0.27f, 0.09f, "Brass");
        }
        // Трубы вдоль южной стены и стояки в углах
        foreach (float y in new[] { 2.6f, 5.0f })
            Cyl(st, "Pipe_S", new Vector3(0, y, -9.65f), 0.09f, 23.2f, "Metal", true, Quaternion.Euler(0, 0, 90));
        foreach (var c in new[] { new Vector3(-11.6f, 3.9f, -9.6f), new Vector3(11.6f, 3.9f, -9.6f), new Vector3(-11.6f, 3.9f, 13.6f) })
            Cyl(st, "Pipe_Riser", c, 0.11f, 7.8f, "Metal");
        // Выступы-полки на западной стене (ступеньками к балкам)
        for (int i = 0; i < 4; i++) Box(st, "Ledge_W", new Vector3(-11.55f, 1.4f + i * 1.35f, -7.5f + i * 1.3f), new Vector3(0.5f, 0.15f, 1.1f), "WoodDark");
    }

    // =========================================================================== стол режимов
    static void BuildModeTable(Transform st, Transform dy, HubController hub)
    {
        PlaceProp(st, "Hub_PlanningTable", new Vector3(0, 0, 2.3f), 0f, 1.55f);

        var console = Group(dy, "ModeConsole");
        console.SetPositionAndRotation(new Vector3(0, 0, 0.75f), Quaternion.LookRotation(Vector3.back)); // локальная +Z → к игроку
        Box(console, "Desk", new Vector3(0, 0.475f, 0), new Vector3(3.5f, 0.95f, 0.6f), "WoodDark");
        Box(console, "DeskTrim", new Vector3(0, 0.95f, 0.31f), new Vector3(3.5f, 0.05f, 0.03f), "Brass", false);
        var panel = Group(console, "Panel");
        panel.localPosition = new Vector3(0, 1.1f, 0.03f);
        panel.localRotation = Quaternion.Euler(35, 0, 0);
        Box(panel, "PanelBoard", Vector3.zero, new Vector3(3.5f, 0.05f, 0.66f), "MarbleDark");

        var modes = new (UnityAction act, Color c)[]
        {
            (hub.SelectClassic, new Color(0.2f, 1f, 0.3f)),
            (hub.SelectPoliceChase, new Color(0.25f, 0.5f, 1f)),
            (hub.SelectHardcore, new Color(1f, 0.15f, 0.1f)),
            (hub.SelectParkour, new Color(1f, 0.8f, 0.15f)),
        };
        var lamps = new Renderer[modes.Length];
        for (int i = 0; i < modes.Length; i++)
        {
            var local = new Vector3(1.29f - i * 0.86f, 0.025f, -0.08f); // консоль развёрнута к игроку → слева направо: классика … паркур
            var pb = MakeButton(panel, "Mode_" + i, local, modes[i].c, 0.26f);
            pb.label = GameSession.ModeTitles[i];
            UnityEventTools.AddVoidPersistentListener(pb.onPressed, modes[i].act);
            Text(panel, "Label_" + i, GameSession.ModeTitles[i], 1.0f, Color.white, local + new Vector3(0, 0.03f, 0.23f),
                Quaternion.LookRotation(Vector3.down, Vector3.back), new Vector2(0.8f, 0.2f));
            var lamp = Sph(panel, "Lamp_" + i, local + new Vector3(0, 0.03f, -0.22f), Vector3.one * 0.07f, null);
            lamp.GetComponent<MeshRenderer>().sharedMaterial = buttonLamp;
            lamps[i] = lamp.GetComponent<Renderer>();
        }
        hub.modeLamps = lamps;

        // Вывеска над столом
        Box(st, "ModeSign_Back", new Vector3(0, 2.75f, 3.45f), new Vector3(3.4f, 0.6f, 0.05f), "DarkMetal", false);
        foreach (float x in new[] { -1.6f, 1.6f }) Box(st, "ModeSign_Post", new Vector3(x, 1.4f, 3.47f), new Vector3(0.07f, 2.8f, 0.07f), "DarkMetal");
        Text(st, "ModeSign", "СТОЛ ПЛАНИРОВАНИЯ · ВЫБЕРИ РЕЖИМ", 2.4f, new Color(1f, 0.82f, 0.3f), new Vector3(0, 2.75f, 3.42f), Facing(Vector3.back), new Vector2(3.2f, 0.5f));
    }

    // =========================================================================== фургон-портал
    static void BuildVan(Transform st, Transform dy, HubController hub, PlayerModeSwitcher switcher)
    {
        var van = PlaceProp(st, "Hub_Van", new Vector3(4.2f, 0, 10.6f), 180f, 2.2f); // открытый кузов (+Z модели) → к игроку (−Z)
        // Зона кузова (в осях модели фургона: кузов — задняя половина, +Z)
        var zone = new GameObject("CargoZone");
        zone.transform.SetParent(van.transform, false);
        zone.transform.localPosition = new Vector3(0, 0.3f, 0.24f);
        zone.layer = IgnoreRaycast;
        var box = zone.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(0.34f, 0.52f, 0.5f);
        GameObjectUtility.SetStaticEditorFlags(zone, 0);

        // Табло режима над фургоном
        Vector3 boardPos = new Vector3(4.2f, 3.55f, 8.9f);
        Box(st, "ModeBoard_Back", boardPos + new Vector3(0, 0, 0.04f), new Vector3(4.8f, 1.35f, 0.06f), "DarkMetal", false);
        Box(st, "ModeBoard_FrameT", boardPos + new Vector3(0, 0.7f, 0), new Vector3(4.9f, 0.05f, 0.05f), "Neon", false);
        Box(st, "ModeBoard_FrameB", boardPos + new Vector3(0, -0.7f, 0), new Vector3(4.9f, 0.05f, 0.05f), "Neon", false);
        foreach (float x in new[] { -2.2f, 2.2f }) Box(st, "ModeBoard_Cable", new Vector3(4.2f + x, 5.6f, 8.94f), new Vector3(0.03f, 2.8f, 0.03f), "DarkMetal", false);
        hub.modeBoardTitle = Text(dy, "ModeBoard_Title", "РЕЖИМ: " + GameSession.ModeTitles[0], 3f, new Color(0.35f, 1f, 0.35f), boardPos + new Vector3(0, 0.3f, -0.01f), Facing(Vector3.back), new Vector2(4.5f, 0.5f));
        hub.modeBoardHint = Text(dy, "ModeBoard_Hint", GameSession.ModeHints[0] + "\nЗАПРЫГНИ В ФУРГОН, ЧТОБЫ ПОЕХАТЬ НА ДЕЛО!", 1.6f, Color.white, boardPos + new Vector3(0, -0.28f, -0.01f), Facing(Vector3.back), new Vector2(4.5f, 0.6f));

        var countdown = Text(dy, "Van_Countdown", "ЗАПРЫГНИ В КУЗОВ!", 2.2f, new Color(1f, 0.8f, 0.2f), new Vector3(4.2f, 2.55f, 7.75f), Facing(Vector3.back), new Vector2(3f, 0.4f));

        var portalGo = new GameObject("VanPortal");
        portalGo.transform.SetParent(dy, false);
        var engine = portalGo.AddComponent<AudioSource>();
        engine.playOnAwake = false; engine.spatialBlend = 1f; engine.minDistance = 3f; engine.maxDistance = 25f; engine.volume = 0.9f;
        // VanPortal должен знать зону: копируем коллайдер в объект портала (позиция/масштаб как у CargoZone)
        portalGo.transform.SetPositionAndRotation(zone.transform.position, zone.transform.rotation);
        portalGo.transform.localScale = van.transform.lossyScale;
        portalGo.layer = IgnoreRaycast;
        var pbox = portalGo.AddComponent<BoxCollider>(); pbox.isTrigger = true; pbox.size = box.size;
        Object.DestroyImmediate(zone);
        var portal = portalGo.AddComponent<VanPortal>();
        portal.switcher = switcher;
        portal.countdownText = countdown;
        portal.engineSource = engine;
        portal.fadeMaterial = FadeMaterial();
    }

    // =========================================================================== гардероб
    static void BuildWardrobe(Transform st, Transform dy, HubController hub)
    {
        PlaceProp(st, "Hub_WardrobeSafe", new Vector3(-10.35f, 0, 6.3f), 90f, 2.6f); // дверца (+Z модели) → в комнату (+X)
        Text(st, "WardrobeSign", "ГАРДЕРОБ", 3f, new Color(1f, 0.82f, 0.3f), new Vector3(-11.76f, 4.4f, 2.2f), Facing(Vector3.right), new Vector2(4f, 0.7f));
        Mirror(dy, new Vector3(-11.75f, 1.45f, 1.2f));
        SkinTerminal(dy, new Vector3(-10.6f, 0, -2.6f));
        CosmeticShowcase(dy, hub, new Vector3(-10.75f, 0, 3.75f));
    }

    static void Mirror(Transform dy, Vector3 pos)
    {
        var mirror = Group(dy, "Mirror");
        mirror.SetPositionAndRotation(pos, Quaternion.Euler(0, 90, 0)); // локальная +Z = мировая +X
        string rtPath = $"{MatDir}/RT_MainHubMirror.renderTexture";
        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(rtPath);
        if (rt == null) { rt = new RenderTexture(720, 800, 24) { name = "RT_MainHubMirror", antiAliasing = 2 }; AssetDatabase.CreateAsset(rt, rtPath); }
        string matPath = $"{MatDir}/M_MainHubMirror.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(mat, matPath); }
        mat.SetTexture("_BaseMap", rt);
        mat.SetTextureScale("_BaseMap", new Vector2(-1, 1));
        mat.SetTextureOffset("_BaseMap", new Vector2(1, 0));
        EditorUtility.SetDirty(mat);
        const float w = 2.2f, h = 2.4f;
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "MirrorSurface";
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(mirror, false);
        quad.transform.localRotation = Quaternion.Euler(0, 180, 0);
        quad.transform.localScale = new Vector3(w, h, 1);
        quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
        Box(mirror, "Frame_T", new Vector3(0, h / 2 + 0.05f, 0.02f), new Vector3(w + 0.2f, 0.1f, 0.06f), "Brass", false);
        Box(mirror, "Frame_B", new Vector3(0, -h / 2 - 0.05f, 0.02f), new Vector3(w + 0.2f, 0.1f, 0.06f), "Brass", false);
        Box(mirror, "Frame_L", new Vector3(-w / 2 - 0.05f, 0, 0.02f), new Vector3(0.1f, h, 0.06f), "Brass", false);
        Box(mirror, "Frame_R", new Vector3(w / 2 + 0.05f, 0, 0.02f), new Vector3(0.1f, h, 0.06f), "Brass", false);
        var camGo = new GameObject("MirrorCamera");
        camGo.transform.SetParent(mirror, false);
        camGo.transform.localPosition = new Vector3(0, 0, 0.05f);
        var cam = camGo.AddComponent<Camera>();
        cam.targetTexture = rt; cam.fieldOfView = 72; cam.nearClipPlane = 0.1f; cam.farClipPlane = 25f; cam.cullingMask = ~0;
        camGo.AddComponent<ProximityCameraEnabler>().distance = 8f;
    }

    static void SkinTerminal(Transform dy, Vector3 pos)
    {
        var term = Group(dy, "SkinTerminal");
        term.SetPositionAndRotation(pos, Quaternion.Euler(0, 90, 0));
        Box(term, "Pedestal", new Vector3(0, 0.5f, 0), new Vector3(0.9f, 1.0f, 0.45f), "WoodDark");
        var panel = Group(term, "Panel");
        panel.localPosition = new Vector3(0, 1.2f, 0.05f);
        panel.localRotation = Quaternion.Euler(35, 0, 0);
        Box(panel, "PanelBoard", Vector3.zero, new Vector3(0.95f, 0.04f, 0.55f), "MarbleDark");
        Text(term, "Title", "ЦВЕТ ШЕРСТИ", 1.6f, new Color(0.3f, 1f, 0.2f), new Vector3(0, 1.72f, -0.15f), Quaternion.Euler(0, 180, 0), new Vector2(1.2f, 0.3f));
        for (int i = 0; i < SkinColors.Length; i++)
        {
            var (label, color) = SkinColors[i];
            var local = new Vector3((i % 3 - 1) * 0.28f, 0.02f, i / 3 == 0 ? -0.1f : 0.14f);
            var btn = Group(panel, "SkinButton_" + i);
            btn.localPosition = local; btn.gameObject.layer = IgnoreRaycast;
            Cyl(btn, "Base", new Vector3(0, 0.005f, 0), 0.075f, 0.01f, "DarkMetal", false);
            var cap = Cyl(btn, "Cap", new Vector3(0, 0.03f, 0), 0.06f, 0.04f, null, false);
            cap.GetComponent<MeshRenderer>().sharedMaterial = ColorMat("SkinCap_" + i, color, 0.35f);
            var trig = btn.gameObject.AddComponent<BoxCollider>(); trig.isTrigger = true; trig.center = new Vector3(0, 0.05f, 0); trig.size = new Vector3(0.14f, 0.08f, 0.14f);
            var src = btn.gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 1f;
            var sb = btn.gameObject.AddComponent<SkinButton>();
            sb.colorName = label; sb.furColor = color; sb.cap = cap.transform; sb.clickClips = click;
            Text(panel, "Label_" + i, label, 0.55f, Color.white, local + new Vector3(0, 0.025f, 0.095f), Quaternion.LookRotation(Vector3.down, Vector3.back), new Vector2(0.26f, 0.06f));
        }
    }

    static void CosmeticShowcase(Transform dy, HubController hub, Vector3 pos)
    {
        var root = Group(dy, "CosmeticShowcase");
        root.SetPositionAndRotation(pos, Quaternion.LookRotation(Vector3.right)); // локальная +Z → в комнату
        Box(root, "Cabinet", new Vector3(0, 0.45f, 0), new Vector3(2.2f, 0.9f, 0.7f), "WoodDark");
        Box(root, "CabinetTrim", new Vector3(0, 0.9f, 0.36f), new Vector3(2.2f, 0.04f, 0.03f), "Brass", false);
        Box(root, "BackBoard", new Vector3(0, 1.55f, -0.33f), new Vector3(2.3f, 1.3f, 0.05f), "DarkMetal", false);
        Text(root, "Title", "ДОНАТ-КОСМЕТИКА · СКОРО В МАГАЗИНЕ", 1.6f, new Color(1f, 0.8f, 0.2f), new Vector3(0, 2.0f, -0.3f), Quaternion.Euler(0, 180, 0), new Vector2(2.2f, 0.3f));
        hub.cosmeticStatus = Text(root, "Status", "ПРИМЕРЬ!", 1.1f, Color.white, new Vector3(0, 1.72f, -0.3f), Quaternion.Euler(0, 180, 0), new Vector2(2.2f, 0.2f));

        var gold = CosMat("M_Cos_Gold"); var black = CosMat("M_Cos_Black"); var ruby = CosMat("M_Cos_Ruby");
        var items = new (string label, UnityAction act, Color c)[]
        {
            ("КОРОНА", hub.TryOnCrown, new Color(1f, 0.78f, 0.2f)),
            ("МАСКА ГРАБИТЕЛЯ", hub.TryOnRobberMask, new Color(0.15f, 0.15f, 0.18f)),
            ("ЗОЛОТАЯ ЦЕПЬ", hub.TryOnGoldChain, new Color(1f, 0.6f, 0.1f)),
        };
        var panel = Group(root, "Panel");
        panel.localPosition = new Vector3(0, 0.93f, 0.2f);
        panel.localRotation = Quaternion.Euler(30, 0, 0);
        Box(panel, "PanelBoard", Vector3.zero, new Vector3(2.2f, 0.03f, 0.28f), "MarbleDark");
        for (int i = 0; i < items.Length; i++)
        {
            float x = (1 - i) * 0.7f; // слева направо: корона, маска, цепь
            // постамент + образец
            Cyl(root, "Stand_" + i, new Vector3(x, 0.98f, -0.13f), 0.15f, 0.16f, "Velvet", false);
            var sample = Group(root, "Sample_" + i);
            sample.localPosition = new Vector3(x, 1.12f, -0.13f);
            if (i == 0)
            {
                Prim(PrimitiveType.Cylinder, sample, "Band", Vector3.zero, new Vector3(0.22f, 0.035f, 0.22f), null, false, null).GetComponent<MeshRenderer>().sharedMaterial = gold;
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI / 3;
                    Prim(PrimitiveType.Cube, sample, "Spike", new Vector3(Mathf.Cos(a) * 0.1f, 0.06f, Mathf.Sin(a) * 0.1f), new Vector3(0.04f, 0.04f, 0.02f), null, false, Quaternion.Euler(0, -a * Mathf.Rad2Deg, 45)).GetComponent<MeshRenderer>().sharedMaterial = gold;
                }
            }
            else if (i == 1)
            {
                Prim(PrimitiveType.Cylinder, sample, "Band", Vector3.zero, new Vector3(0.26f, 0.03f, 0.2f), null, false, null).GetComponent<MeshRenderer>().sharedMaterial = black;
                foreach (float ex in new[] { -0.05f, 0.05f })
                    Prim(PrimitiveType.Sphere, sample, "EyeHole", new Vector3(ex, 0, 0.1f), new Vector3(0.05f, 0.035f, 0.02f), "Paper", false, null);
            }
            else
            {
                for (int k = 0; k < 14; k++)
                {
                    float a = k * Mathf.PI * 2 / 14;
                    Prim(PrimitiveType.Sphere, sample, "Link", new Vector3(Mathf.Cos(a) * 0.11f, 0, Mathf.Sin(a) * 0.11f), Vector3.one * 0.03f, null, false, null).GetComponent<MeshRenderer>().sharedMaterial = gold;
                }
                Prim(PrimitiveType.Cylinder, sample, "Medal", new Vector3(0, -0.01f, 0.13f), new Vector3(0.07f, 0.006f, 0.07f), null, false, Quaternion.Euler(90, 0, 0)).GetComponent<MeshRenderer>().sharedMaterial = ruby;
            }
            var pb = MakeButton(panel, "TryOn_" + i, new Vector3(x, 0.015f, 0f), items[i].c, 0.17f);
            pb.label = items[i].label;
            UnityEventTools.AddVoidPersistentListener(pb.onPressed, items[i].act);
            Text(root, "PriceTag_" + i, items[i].label + "\n<size=70%>ПРИМЕРИТЬ БЕСПЛАТНО</size>", 0.9f, Color.white, new Vector3(x, 1.38f, -0.3f), Quaternion.Euler(0, 180, 0), new Vector2(0.65f, 0.2f));
        }
        PointLight(root, "ShowcaseLight", new Vector3(0, 2.4f, 0.6f), Warm, 1.2f, 3f);
    }

    // =========================================================================== баскетбол
    static void BuildBasketball(Transform st, Transform dy)
    {
        const float hoopH = 3.4f;
        var hoop = PlaceProp(st, "Hub_BasketballHoop", new Vector3(10.55f, 0, -6f), 0f, hoopH); // кольцо смотрит в −X (в комнату)
        float s = hoop.transform.localScale.x;
        Vector3 rim = hoop.transform.TransformPoint(new Vector3(-0.215f, 0.69f, 0f)); // центр обода (замерено по мешу)

        var trigGo = new GameObject("HoopScoreTrigger");
        trigGo.transform.SetParent(dy, false);
        trigGo.transform.position = rim + Vector3.down * 0.25f;
        trigGo.layer = IgnoreRaycast;
        var tc = trigGo.AddComponent<BoxCollider>(); tc.isTrigger = true; tc.size = new Vector3(0.13f * s * 1.6f, 0.16f, 0.13f * s * 1.6f);
        var src = trigGo.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 1f; src.minDistance = 4f;
        var scorer = trigGo.AddComponent<BasketballHoop>();
        scorer.scoreClips = Clips(UiDir, "confirmation");
        scorer.scoreText = Text(dy, "HoopScore", "ГОЛЫ: 0", 5f, new Color(1f, 0.55f, 0.1f), new Vector3(11.76f, 4.4f, -6f), Facing(Vector3.left), new Vector2(3.2f, 0.9f));
        Text(st, "HoopTitle", "БАСКЕТБОЛ · БРОСЬ МЯЧ В КОЛЬЦО", 2f, Color.white, new Vector3(11.76f, 5.2f, -6f), Facing(Vector3.left), new Vector2(4.5f, 0.4f));
        Box(st, "HoopScoreBack", new Vector3(11.78f, 4.75f, -6f), new Vector3(0.02f, 1.7f, 4.8f), "DarkMetal", false);
        Box(st, "FreeThrowLine", new Vector3(7.4f, 0.015f, -6f), new Vector3(0.08f, 0.02f, 3f), "Paper", false);

        // Стойка с мячом
        var standPos = new Vector3(8.2f, 0, -8.6f);
        Cyl(st, "BallStand", standPos + Vector3.up * 0.45f, 0.18f, 0.9f, "DarkMetal");
        Cyl(st, "BallCup", standPos + Vector3.up * 0.92f, 0.16f, 0.04f, "Brass");
        var respawn = Group(dy, "BallRespawn");
        respawn.position = standPos + Vector3.up * 1.08f;

        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Basketball";
        ball.transform.SetParent(dy, false);
        ball.transform.position = respawn.position;
        ball.transform.localScale = Vector3.one * 0.24f;
        ball.GetComponent<MeshRenderer>().sharedMaterial = BallMaterial();
        var pm = new PhysicsMaterial("BallBounce") { bounciness = 0.72f, dynamicFriction = 0.5f, staticFriction = 0.6f, bounceCombine = PhysicsMaterialCombine.Maximum };
        string pmPath = $"{MatDir}/PM_Basketball.physicMaterial";
        var existing = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(pmPath);
        if (existing == null) { AssetDatabase.CreateAsset(pm, pmPath); existing = pm; }
        ball.GetComponent<SphereCollider>().sharedMaterial = existing;
        foreach (var rot in new[] { Quaternion.identity, Quaternion.Euler(90, 0, 0) })
        {
            var seam = Prim(PrimitiveType.Cylinder, ball.transform, "Seam", Vector3.zero, new Vector3(1.01f, 0.012f, 1.01f), "Rubber", false, rot);
            seam.layer = 0;
        }
        var rb = ball.AddComponent<Rigidbody>(); rb.mass = 0.6f; rb.linearDamping = 0.05f; rb.angularDamping = 0.3f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; rb.interpolation = RigidbodyInterpolation.Interpolate;
        var loot = ball.AddComponent<LootItem>();
        loot.lootId = "Basketball"; loot.itemName = "Мяч"; loot.value = 0;
        loot.kinematicUntilGrabbed = false; loot.spinUntilGrabbed = false; loot.glowUntilGrabbed = false;
        ball.AddComponent<AudioSource>().spatialBlend = 1f;
        var bb = ball.AddComponent<Basketball>();
        bb.respawnPoint = respawn;
        bb.bounceClips = Clips(ImpactDir, "impact_soft_medium", "impact_generic_light");
    }

    // =========================================================================== декор и свет
    static void BuildDecor(Transform st)
    {
        PlaceProp(st, "Hub_LootPile", new Vector3(0.6f, 0, 11.8f), 200f, 1.1f);
        PlaceProp(st, "Hub_LootPile", new Vector3(10.3f, 3.6f, 10.6f), 20f, 1.0f);
        foreach (var p in new[] { new Vector3(-7.5f, 0, 12.9f), new Vector3(-8.3f, 0, 12.6f), new Vector3(11f, 0, 1f) })
            Cyl(st, "Barrel", p + Vector3.up * 0.55f, 0.35f, 1.1f, "BarrelRed");
        Box(st, "Crate_Deco1", new Vector3(-5.8f, 0.5f, 12.9f), Vector3.one, "WoodLight");
        Box(st, "Crate_Deco2", new Vector3(-5.9f, 1.4f, 12.9f), Vector3.one * 0.8f, "WoodLight", true, Quaternion.Euler(0, 20, 0));
        Box(st, "Crate_Deco3", new Vector3(11f, 0.4f, -1.5f), Vector3.one * 0.8f, "WoodLight");
    }

    static void BuildLights(Transform map)
    {
        var lights = Group(map, "Lights");
        foreach (var p in new[] { new Vector3(-4, 0, -2), new Vector3(4, 0, -2), new Vector3(-4, 0, 6), new Vector3(4, 0, 6) })
        {
            Cyl(lights, "LampCord", new Vector3(p.x, 6.0f, p.z), 0.01f, 0.6f, "DarkMetal", false);
            Cyl(lights, "LampShade", new Vector3(p.x, 5.65f, p.z), 0.28f, 0.16f, "DarkMetal", false);
            Sph(lights, "LampBulb", new Vector3(p.x, 5.52f, p.z), Vector3.one * 0.2f, "LampWarm");
            PointLight(lights, "WarmLamp", new Vector3(p.x, 5.3f, p.z), Warm, 3.2f, 11f);
        }
        SpotLight(lights, "TableSpot", new Vector3(0, 5.6f, 0.2f), new Vector3(0, 1f, 2f), Warm, 4f, 9f, 55f);
        PointLight(lights, "WardrobeLight", new Vector3(-9.3f, 3.4f, 1.5f), Warm, 2.2f, 8f);
        PointLight(lights, "VanLight", new Vector3(4.2f, 4.6f, 7.4f), new Color(1f, 0.85f, 0.6f), 2f, 8f);
        PointLight(lights, "HoopLight", new Vector3(8.5f, 4.5f, -6.5f), Warm, 1.8f, 8f);
    }

    // =========================================================================== helpers
    static GameObject PlaceProp(Transform parent, string name, Vector3 floorPos, float yaw, float height)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Gen}/{name}/{name}.prefab") ?? throw new System.Exception("No prefab " + name);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, yaw, 0));
        go.transform.localScale = Vector3.one;
        float h0 = Bounds(go).size.y;
        go.transform.localScale = Vector3.one * (height / h0);
        var b = Bounds(go);
        go.transform.position = new Vector3(floorPos.x - b.center.x, floorPos.y - b.min.y, floorPos.z - b.center.z);
        // Точные коллайдеры по мешу (за них можно цепляться руками, в кузов фургона можно залезть)
        foreach (var bc in go.GetComponents<BoxCollider>()) Object.DestroyImmediate(bc);
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            if (!mf.TryGetComponent<MeshCollider>(out _)) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        return go;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static GameObject B(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
    {
        var go = Box(parent, name, pos, size, "Concrete");
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static PushButton MakeButton(Transform panel, string id, Vector3 local, Color color, float d)
    {
        var btn = Group(panel, "Button_" + id);
        btn.localPosition = local;
        btn.gameObject.layer = IgnoreRaycast;
        Cyl(btn, "Housing", new Vector3(0, 0.01f, 0), d * 0.65f, 0.02f, "DarkMetal", false);
        var cap = Cyl(btn, "Cap", new Vector3(0, 0.04f, 0), d / 2, 0.04f, null, false);
        cap.GetComponent<MeshRenderer>().sharedMaterial = ColorMat("Btn_" + ColorUtility.ToHtmlStringRGB(color), color, 0.6f);
        var trig = btn.gameObject.AddComponent<BoxCollider>();
        trig.isTrigger = true; trig.center = new Vector3(0, 0.06f, 0); trig.size = new Vector3(d * 1.15f, 0.1f, d * 1.15f);
        var src = btn.gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 1f;
        var pb = btn.gameObject.AddComponent<PushButton>();
        pb.cap = cap.transform; pb.clickClips = click;
        return pb;
    }

    static Material ColorMat(string key, Color color, float emission)
    {
        string path = $"{MatDir}/M_{key}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", 0.5f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", color * emission);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material CosMat(string name) =>
        AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/Materials/Player/{name}.mat") ?? M("GoldGlow");

    static Material LampMaterial()
    {
        string path = $"{MatDir}/M_ModeLamp.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", new Color(0.08f, 0.1f, 0.08f));
        m.SetFloat("_Smoothness", 0.6f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", Color.black);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material BallMaterial()
    {
        string path = $"{MatDir}/M_Basketball.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", new Color(0.92f, 0.42f, 0.1f));
        m.SetFloat("_Smoothness", 0.15f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material FadeMaterial()
    {
        string path = $"{MatDir}/M_ScreenFade.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = 4000;
        m.SetColor("_BaseColor", new Color(0, 0, 0, 0));
        EditorUtility.SetDirty(m);
        return m;
    }

    /// Ретро-кирпич 128×128 (мыльный, как в Gorilla Tag): 8 рядов по 4 кирпича, швы светлее.
    static Material BrickMaterial()
    {
        string texPath = $"{TexDir}/T_Brick.png";
        if (!File.Exists(texPath))
        {
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGB24, false);
            var rnd = new System.Random(11);
            var brickCol = new Color[8, 5];
            for (int r = 0; r < 8; r++) for (int c = 0; c < 5; c++)
                {
                    float v = (float)rnd.NextDouble();
                    brickCol[r, c] = Color.Lerp(new Color(0.55f, 0.24f, 0.16f), new Color(0.68f, 0.36f, 0.22f), v) * (0.85f + 0.15f * (float)rnd.NextDouble());
                }
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int row = y / 16, yy = y % 16;
                    int off = row % 2 == 0 ? 0 : 16;
                    int xs = (x + off) % N, col = (x + off) / 32, xx = xs % 32;
                    bool mortar = yy < 2 || xx < 2;
                    float n = 0.92f + 0.08f * (float)rnd.NextDouble();
                    var c = mortar ? new Color(0.42f, 0.39f, 0.35f) : brickCol[row, col % 5];
                    tex.SetPixel(x, y, c * n);
                }
            tex.Apply();
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
            ti.maxTextureSize = 128; ti.filterMode = FilterMode.Bilinear; ti.wrapMode = TextureWrapMode.Repeat; ti.SaveAndReimport();
        }
        string matPath = $"{MatDir}/M_Brick.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, matPath); }
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        m.SetTextureScale("_BaseMap", new Vector2(12f, 4f)); // кирпич ≈ 0.5 × 0.25 м на стене 24×8 м
        m.SetColor("_BaseColor", Color.white);
        TripoAssetPostprocessor.MakeMatte(m);
        EditorUtility.SetDirty(m);
        return m;
    }
}
