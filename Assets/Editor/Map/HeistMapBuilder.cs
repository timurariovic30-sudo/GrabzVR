using System;
using System.Collections.Generic;
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
/// Генератор большой карты ограбления в HeistLevel_Test:
/// Убежище (хаб) → шлюз → Главный холл ⇄ Галерея ⇄ Офисы/архив ⇄ Хранилище ⇄ Подземный гараж с фургоном.
/// Меню: Tools > Heist > Build Heist Map (Full). Модель игрока и SampleScene не трогает.
/// </summary>
public static class HeistMapBuilder
{
    const string ScenePath = "Assets/Scenes/HeistLevel_Test.unity";
    const string ImpactDir = "Assets/Audio/Kenney_ImpactSounds";
    const string UiDir = "Assets/Audio/Kenney_InterfaceSounds";

    class Ctx
    {
        public Transform map;
        public Dictionary<string, GameObject> loot;
        public readonly List<Light> alarmLights = new List<Light>();
        public readonly List<Renderer> beacons = new List<Renderer>();
        public HeistDebugActions debug;
        public SlidingDoor airlock;
        public Transform spawn, vaultPoint;
        public AudioClip[] click, glass, open, close, boing;
    }

    static readonly Color Warm = new Color(1f, 0.78f, 0.52f);
    static readonly Color WarmWhite = new Color(1f, 0.92f, 0.8f);
    static readonly Color Cold = new Color(0.82f, 0.9f, 1f);
    static readonly Color Sodium = new Color(1f, 0.64f, 0.3f);

    [MenuItem("Tools/Heist/Build Heist Map (Full)")]
    public static void Build()
    {
        try
        {
            Init();
            var ctx = new Ctx { loot = HeistLootFactory.BuildAll() };
            ctx.click = Clips(UiDir, "click");
            ctx.glass = Clips(ImpactDir, "impact_glass_heavy", "impact_glass_medium");
            ctx.open = Clips(UiDir, "open", "maximize");
            ctx.close = Clips(UiDir, "close", "minimize");
            ctx.boing = Clips(UiDir, "bong", "pluck");

            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            GameObject Root(string n) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == n);

            foreach (var old in new[] { "HeistLevel", "Lobby", "HeistMap" })
            {
                var o = Root(old);
                if (o) UnityEngine.Object.DestroyImmediate(o);
            }
            var mapGo = new GameObject("HeistMap");
            EditorSceneManager.MoveGameObjectToScene(mapGo, scene);
            ctx.map = mapGo.transform;

            // Менеджеры — до зон (кнопки ссылаются на их методы)
            var gmGo = Root("HeistGameManager") ?? new GameObject("HeistGameManager");
            if (gmGo.scene != scene) EditorSceneManager.MoveGameObjectToScene(gmGo, scene);
            var gm = Get<HeistGameManager>(gmGo);
            var alarm = Get<AlarmSystem>(gmGo);
            var registry = Get<LootRegistry>(gmGo);
            ctx.debug = Get<HeistDebugActions>(gmGo);

            BuildHub(ctx);
            BuildHall(ctx);
            BuildGallery(ctx);
            BuildOffice(ctx);
            BuildVault(ctx);
            BuildDuct(ctx);
            BuildGarage(ctx);

            // Связи геймплея
            gm.roundDuration = 180f;
            gm.targetLoot = 4000;
            gm.airlock = ctx.airlock.GetComponent<AirlockDoor>();
            alarm.alarmLights = ctx.alarmLights.ToArray();
            alarm.beacons = ctx.beacons.ToArray();
            registry.prefabs = ctx.loot.Select(kv => new LootRegistry.Entry { id = kv.Key, prefab = kv.Value }).ToArray();
            var switcher = Root("PlayerModeSwitcher")?.GetComponent<PlayerModeSwitcher>();
            ctx.debug.switcher = switcher;
            ctx.debug.lootRegistry = registry;
            ctx.debug.hubPoint = ctx.spawn;
            ctx.debug.vaultPoint = ctx.vaultPoint;
            ctx.debug.hubMaxZ = 0f;
            foreach (var c in new Component[] { gm, alarm, registry, ctx.debug }) EditorUtility.SetDirty(c);

            // Игроки — в Убежище
            foreach (var n in new[] { "DesktopTestPlayer", "VRGorillaPlayer" })
            {
                var p = Root(n);
                if (p) { p.transform.SetPositionAndRotation(ctx.spawn.position, ctx.spawn.rotation); EditorUtility.SetDirty(p.transform); }
            }

            SetupLighting(scene);

            // Static для всей неподвижной геометрии
            foreach (var t in ctx.map.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Static").ToArray())
                MarkStatic(t);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.SaveAssets();
            int objects = ctx.map.GetComponentsInChildren<Transform>(true).Length;
            Debug.Log($"[HeistMap] Done. Objects: {objects}, lights: {ctx.map.GetComponentsInChildren<Light>(true).Length}");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    static T Get<T>(GameObject go) where T : Component => go.TryGetComponent<T>(out var c) ? c : go.AddComponent<T>();

    static (Transform zone, Transform st, Transform dy) Zone(Ctx ctx, string name)
    {
        var z = Group(ctx.map, name);
        return (z, Group(z, "Static"), Group(z, "Dynamic"));
    }

    // =====================================================================================
    // УБЕЖИЩЕ (Хаб): x[-7,7] z[-14,0], потолок 4
    // =====================================================================================
    static void BuildHub(Ctx ctx)
    {
        var (z, st, dy) = Zone(ctx, "Z1_Hub_Bunker");
        Box(st, "Floor", new Vector3(0, -0.1f, -7), new Vector3(14, 0.2f, 14), "ConcreteDark");
        Box(st, "Rug", new Vector3(0, 0.01f, -7.5f), new Vector3(5, 0.02f, 6), "CarpetRed", false);
        Box(st, "Ceiling", new Vector3(0, 4.1f, -7), new Vector3(14.6f, 0.2f, 14.6f), "Concrete");
        Wall(st, "Wall_W", -7, -14, -7, 0, 0, 4, "Concrete");
        Wall(st, "Wall_E", 7, -14, 7, 0, 0, 4, "Concrete");
        Wall(st, "Wall_S", -7, -14, 7, -14, 0, 4, "Concrete");
        // трубы под потолком
        Pipe(st, new Vector3(-5.6f, 3.65f, -13.6f), new Vector3(-5.6f, 3.65f, -0.4f), 0.08f, "Metal");
        Pipe(st, new Vector3(5.6f, 3.65f, -13.6f), new Vector3(5.6f, 3.65f, -0.4f), 0.08f, "Metal");
        Pipe(st, new Vector3(-6.6f, 3.4f, -13.5f), new Vector3(6.6f, 3.4f, -13.5f), 0.1f, "DarkMetal");

        // Точка спавна
        ctx.spawn = Group(z, "PlayerSpawn");
        ctx.spawn.SetPositionAndRotation(new Vector3(0, 0, -9f), Quaternion.identity);

        // Тёплые лампы
        foreach (var p in new[] { new Vector3(-3, 0, -4), new Vector3(3, 0, -4), new Vector3(0, 0, -10.5f) })
        {
            HangingLamp(st, new Vector3(p.x, 4f, p.z), "LampWarm");
            PointLight(z, "WarmLamp", new Vector3(p.x, 3.3f, p.z), Warm, 3.4f, 8.5f);
        }

        Workbench(st, new Vector3(6.1f, 0, -6.5f));
        PlanningBoard(st, new Vector3(1.6f, 2.05f, -13.83f));
        Rack(st, new Vector3(-6.35f, 0, -12.3f), 90f);
        Rack(st, new Vector3(6.35f, 0, -12.3f), -90f);
        Sofa(st, new Vector3(-3.8f, 0, -13.1f), 0f, 2.4f);
        Plant(st, new Vector3(-6.3f, 0, -0.8f));

        Mirror(ctx, st, dy, new Vector3(-6.83f, 1.3f, -5.2f));
        SkinTerminal(ctx, dy, new Vector3(-6.45f, 0, -8.3f));
        DebugConsole(ctx, dy, new Vector3(3.5f, 0, -1.25f));
        Airlock(ctx, st, dy);
        Scoreboard(dy, "Scoreboard_Hub", new Vector3(0, 3.6f, -0.17f), Vector3.back, 2.6f, 0.72f);
        AlarmBeacon(ctx, dy, new Vector3(0, 3.85f, -6.5f));
    }

    // =====================================================================================
    // ГЛАВНЫЙ ХОЛЛ: x[-14,14] z[0,24], потолок 10, балконы 2-го этажа на высоте 4.5
    // =====================================================================================
    static void BuildHall(Ctx ctx)
    {
        var (z, st, dy) = Zone(ctx, "Z2_MainHall");
        Box(st, "Floor", new Vector3(0, -0.1f, 12), new Vector3(28, 0.2f, 24), "Marble");
        Box(st, "Runner", new Vector3(0, 0.01f, 12), new Vector3(2.4f, 0.02f, 24), "CarpetRed", false);
        Box(st, "Border", new Vector3(0, 0.005f, 12), new Vector3(27.4f, 0.01f, 23.4f), "MarbleDark", false);
        Box(st, "BorderInner", new Vector3(0, 0.008f, 12), new Vector3(26.4f, 0.01f, 22.4f), "Marble", false);
        Box(st, "Ceiling", new Vector3(0, 10.1f, 12), new Vector3(28.6f, 0.2f, 24.6f), "Plaster");

        Wall(st, "Wall_S", -14, 0, 14, 0, 0, 10, "Plaster", new Opening(0, 3, 0, 3));
        Wall(st, "Wall_W", -14, 0, -14, 24, 0, 10, "Plaster", new Opening(5.25f, 2.5f, 0, 2.8f), new Opening(15.25f, 2.5f, 4.8f, 7.3f));
        Wall(st, "Wall_E", 14, 0, 14, 24, 0, 10, "Plaster", new Opening(10, 4, 0, 3.5f));
        Wall(st, "Wall_N", -14, 24, 14, 24, -3, 10, "Plaster", new Opening(0, 3, 0, 3));

        // Балконы (верх пола = 4.5): запад, север, восток + перила
        Box(st, "Balcony_W", new Vector3(-12.5f, 4.35f, 12), new Vector3(3, 0.3f, 24), "MarbleDark");
        Box(st, "Balcony_N", new Vector3(0, 4.35f, 22.5f), new Vector3(22, 0.3f, 3), "MarbleDark");
        Box(st, "Balcony_E", new Vector3(12.5f, 4.35f, 12), new Vector3(3, 0.3f, 24), "MarbleDark");
        Railing(st, new Vector3(-11, 4.5f, 0.3f), new Vector3(-11, 4.5f, 21));
        Railing(st, new Vector3(-11, 4.5f, 21), new Vector3(11, 4.5f, 21));
        Railing(st, new Vector3(11, 4.5f, 21), new Vector3(11, 4.5f, 0.3f));
        // кронштейны под балконами
        for (float zz = 3; zz < 22; zz += 6)
        {
            Box(st, "Corbel_W", new Vector3(-11.4f, 3.9f, zz), new Vector3(0.5f, 0.6f, 0.3f), "MarbleDark");
            Box(st, "Corbel_E", new Vector3(11.4f, 3.9f, zz), new Vector3(0.5f, 0.6f, 0.3f), "MarbleDark");
        }

        // Колонны — на них можно забираться (кольца = зацепы)
        foreach (float x in new[] { -6f, 6f })
            foreach (float zz in new[] { 5f, 12f, 19f })
                Column(st, new Vector3(x, 0, zz), 10f);

        // Балки под потолком (для раскачивания) и карнизы
        foreach (float zz in new[] { 8.5f, 15.5f }) IBeam(st, new Vector3(0, 8.3f, zz), 28f);
        Box(st, "Ledge_S_Low", new Vector3(0, 3.6f, 0.35f), new Vector3(27.4f, 0.15f, 0.4f), "MarbleDark");
        Box(st, "Ledge_S_High", new Vector3(0, 7.0f, 0.35f), new Vector3(27.4f, 0.15f, 0.4f), "MarbleDark");
        Box(st, "Ledge_N_High", new Vector3(0, 7.2f, 23.65f), new Vector3(27.4f, 0.15f, 0.4f), "MarbleDark");
        // Трубы вдоль стен + вертикальные трубы-«лианы» в углах
        Pipe(st, new Vector3(-13.6f, 5.4f, 0.45f), new Vector3(13.6f, 5.4f, 0.45f), 0.1f, "Metal");
        Pipe(st, new Vector3(-13.6f, 5.85f, 0.45f), new Vector3(13.6f, 5.85f, 0.45f), 0.07f, "Brass");
        foreach (var p in new[] { new Vector3(13.5f, 0, 0.5f), new Vector3(-13.5f, 0, 0.5f), new Vector3(13.5f, 0, 23.5f), new Vector3(-13.5f, 0, 23.5f) })
            Pipe(st, p, p + Vector3.up * 10f, 0.12f, "Metal");

        // Мебель
        ReceptionDesk(st, new Vector3(8.6f, 0, 3.6f));
        Sofa(st, new Vector3(-3.6f, 0, 15.5f), 90f, 2.6f);
        Sofa(st, new Vector3(3.6f, 0, 15.5f), -90f, 2.6f);
        Atm(st, new Vector3(13.45f, 0, 17f), Vector3.left);
        Atm(st, new Vector3(13.45f, 0, 19.4f), Vector3.left);
        foreach (var p in new[] { new Vector3(-12.6f, 0, 1.4f), new Vector3(12.6f, 0, 22.6f), new Vector3(-12.6f, 0, 22.6f) }) Plant(st, p);
        Chandelier(st, new Vector3(0, 8.4f, 12));

        // Вывески
        SignPlate(st, "Sign_Bank", "БАНК «ЗОЛОТОЙ БАНАН»", new Vector3(0, 5.0f, 0.17f), Vector3.forward, 7f, 1f, new Color(1f, 0.8f, 0.25f));
        SignPlate(st, "Sign_Gallery", "ГАЛЕРЕЯ →", new Vector3(13.83f, 4.2f, 10), Vector3.left, 3.2f, 0.6f, Color.white);
        SignPlate(st, "Sign_Office", "← АРХИВ · ОФИСЫ", new Vector3(-13.83f, 3.4f, 5.25f), Vector3.right, 3.2f, 0.5f, Color.white);
        SignPlate(st, "Sign_Vault", "ХРАНИЛИЩЕ", new Vector3(0, 3.45f, 23.83f), Vector3.back, 2.6f, 0.5f, new Color(1f, 0.3f, 0.25f));

        // Батут к западному балкону
        Trampoline(ctx, dy, new Vector3(-9.2f, 0, 5f));

        // Решётка хранилища + кнопки с обеих сторон
        var gate = VaultGate(ctx, dy, new Vector3(0, 0, 24));
        var plateHall = WallPlate(dy, "GatePlate_Hall", new Vector3(2.6f, 1.3f, 23.83f), Vector3.back, new Vector2(0.5f, 0.6f));
        Bind(Button(ctx, plateHall, Vector3.zero, "РЕШЁТКА", new Color(0.2f, 0.9f, 0.3f), 0.2f, labelAbove: true), gate.Toggle);

        Scoreboard(dy, "Scoreboard_Hall", new Vector3(0, 7.9f, 23.83f), Vector3.back, 4.2f, 1.3f);
        SecurityCam(dy, new Vector3(13.4f, 6.8f, 0.6f), new Vector3(2, 0, 12));
        SecurityCam(dy, new Vector3(-13.4f, 6.8f, 23.4f), new Vector3(-2, 0, 12));

        foreach (var p in new[] { new Vector3(-6, 9.2f, 6), new Vector3(6, 9.2f, 6), new Vector3(-6, 9.2f, 18), new Vector3(6, 9.2f, 18) })
            PointLight(z, "HallLight", p, WarmWhite, 3.2f, 14f);
        PointLight(z, "BalconyLight", new Vector3(0, 6.5f, 21.5f), WarmWhite, 1.5f, 10f);
        AlarmBeacon(ctx, dy, new Vector3(0, 9.7f, 12));
        Place(ctx, "GoldBar", dy, new Vector3(9.4f, 1.15f, 3.2f), Quaternion.Euler(0, 20, 0));
    }

    // =====================================================================================
    // ГАЛЕРЕЯ: x[14,34] z[0,24], потолок 6 — витрины (разбиваются), вазы, картины
    // =====================================================================================
    static void BuildGallery(Ctx ctx)
    {
        var (z, st, dy) = Zone(ctx, "Z3_Gallery");
        Box(st, "Floor", new Vector3(24, -0.1f, 12), new Vector3(20, 0.2f, 24), "WoodLight");
        for (float zz = 1; zz < 24; zz += 2) Box(st, "Parquet", new Vector3(24, 0.003f, zz), new Vector3(20, 0.006f, 0.04f), "WoodDark", false);
        Box(st, "Ceiling", new Vector3(24, 6.1f, 12), new Vector3(20.6f, 0.2f, 24.6f), "PlasterCool");
        Wall(st, "Wall_S", 14, 0, 34, 0, 0, 6, "PlasterCool");
        Wall(st, "Wall_E", 34, 0, 34, 24, 0, 6, "PlasterCool");
        Wall(st, "Wall_N", 14, 24, 34, 24, -3, 6, "PlasterCool", new Opening(30.5f, 5, 0, 3));
        Box(st, "Skirting_S", new Vector3(24, 0.1f, 0.17f), new Vector3(19.7f, 0.2f, 0.05f), "MarbleDark", false);
        Box(st, "Skirting_E", new Vector3(33.83f, 0.1f, 12), new Vector3(0.05f, 0.2f, 23.7f), "MarbleDark", false);

        // Витрины
        Showcase(ctx, st, dy, new Vector3(24, 0, 12), 1.1f, new Vector3(0.9f, 0.8f, 0.9f), "Diamond");
        Showcase(ctx, st, dy, new Vector3(19, 0, 18), 0.95f, new Vector3(0.7f, 0.6f, 0.7f), "Crown");
        Showcase(ctx, st, dy, new Vector3(29, 0, 18), 0.95f, new Vector3(0.7f, 0.6f, 0.7f), "Crown");
        Showcase(ctx, st, dy, new Vector3(19, 0, 6), 0.95f, new Vector3(0.8f, 0.5f, 0.6f), "GoldBar2");
        // Вазы на постаментах
        Plinth(st, new Vector3(29, 0, 6), 1.0f); Place(ctx, "Vase", dy, new Vector3(29, 1.0f, 6), Quaternion.identity);
        Plinth(st, new Vector3(31.5f, 0, 13), 1.0f); Place(ctx, "Vase", dy, new Vector3(31.5f, 1.0f, 13), Quaternion.Euler(0, 40, 0));
        // Картины на стенах
        var p1 = Place(ctx, "Painting", dy, new Vector3(20, 1.9f, 0.2f), Quaternion.Euler(0, 180, 0));
        var p2 = Place(ctx, "Painting", dy, new Vector3(28, 1.9f, 0.2f), Quaternion.Euler(0, 180, 0)); SetArt(p2, "Art2");
        var p3 = Place(ctx, "Painting", dy, new Vector3(33.8f, 1.9f, 12), Quaternion.Euler(0, 90, 0)); SetArt(p3, "Art3");

        // Ограждение вокруг бриллианта
        var posts = new[] { new Vector3(22.4f, 0, 10.4f), new Vector3(25.6f, 0, 10.4f), new Vector3(25.6f, 0, 13.6f), new Vector3(22.4f, 0, 13.6f) };
        for (int i = 0; i < 4; i++)
        {
            Cyl(st, "Stanchion", posts[i] + Vector3.up * 0.5f, 0.04f, 1f, "Brass");
            Sph(st, "StanchionTop", posts[i] + Vector3.up * 1.03f, Vector3.one * 0.1f, "Brass");
            Cyl(st, "StanchionBase", posts[i] + Vector3.up * 0.02f, 0.15f, 0.04f, "Brass", false);
            var a = posts[i] + Vector3.up * 0.9f; var b = posts[(i + 1) % 4] + Vector3.up * 0.9f;
            if (i != 0) Pipe(st, a, b, 0.02f, "Velvet", collider: false);
        }
        Bench(st, new Vector3(24, 0, 3.8f), 0f);
        Bench(st, new Vector3(24, 0, 20.4f), 0f);
        Sculpture(st, new Vector3(31.8f, 0, 21.6f));
        SignPlate(st, "Sign_Garage", "↓ ГАРАЖ · ФУРГОН", new Vector3(30.5f, 3.45f, 23.83f), Vector3.back, 3.4f, 0.5f, new Color(0.3f, 1f, 0.2f));
        SignPlate(st, "Sign_GalleryTitle", "ГАЛЕРЕЯ СОВРЕМЕННОГО ИСКУССТВА", new Vector3(24, 4.6f, 0.17f), Vector3.forward, 7f, 0.6f, Color.white);

        // Холодный музейный свет: споты над витринами и картинами
        foreach (var t in new[] { new Vector3(24, 0, 12), new Vector3(19, 0, 18), new Vector3(29, 0, 18), new Vector3(19, 0, 6), new Vector3(24, 1.9f, 0.5f), new Vector3(33.5f, 1.9f, 12) })
        {
            var from = new Vector3(t.x < 33 ? t.x : 31.5f, 5.85f, t.z < 1 ? 3f : t.z);
            Cyl(st, "TrackLight", from + Vector3.up * 0.08f, 0.08f, 0.2f, "DarkMetal", false);
            SpotLight(z, "MuseumSpot", from, t, Cold, 7f, 9f, 55f);
        }
        PointLight(z, "GalleryFill", new Vector3(24, 5.5f, 12), Cold, 1.6f, 16f);
        SecurityCam(dy, new Vector3(14.6f, 5.3f, 23.4f), new Vector3(24, 0, 12));
        SecurityCam(dy, new Vector3(33.4f, 5.3f, 0.6f), new Vector3(24, 0, 12));
        AlarmBeacon(ctx, dy, new Vector3(24, 5.8f, 12));
    }

    // =====================================================================================
    // ОФИСЫ: x[-34,-14] z[0,40], потолок 9. Внизу архив (шкафы), антресоль-офис на высоте 4.5 (z 12..40)
    // =====================================================================================
    static void BuildOffice(Ctx ctx)
    {
        var (z, st, dy) = Zone(ctx, "Z4_Offices");
        Box(st, "Floor", new Vector3(-24, -0.1f, 20), new Vector3(20, 0.2f, 40), "Concrete");
        Box(st, "Ceiling", new Vector3(-24, 9.1f, 20), new Vector3(20.6f, 0.2f, 40.6f), "Plaster");
        Wall(st, "Wall_S", -34, 0, -14, 0, 0, 9, "Plaster");
        Wall(st, "Wall_W", -34, 0, -34, 40, 0, 9, "Plaster");
        Wall(st, "Wall_N", -34, 40, -14, 40, 0, 9, "Plaster");
        Wall(st, "Wall_E_Vault", -14, 24, -14, 40, 0, 9, "Plaster", new Opening(32, 1.5f, 6.1f, 7.7f)); // проём вентиляции

        // Антресоль (офисный этаж) + перила с проходом над «лестницей» из шкафов
        Box(st, "Mezzanine", new Vector3(-24, 4.35f, 26), new Vector3(20, 0.3f, 28), "CarpetBlue");
        Box(st, "MezzanineEdge", new Vector3(-24, 4.35f, 12.05f), new Vector3(20, 0.32f, 0.12f), "DarkMetal");
        Railing(st, new Vector3(-33.7f, 4.5f, 12.15f), new Vector3(-24.2f, 4.5f, 12.15f));
        Railing(st, new Vector3(-20.8f, 4.5f, 12.15f), new Vector3(-14.3f, 4.5f, 12.15f));

        // Архив: ряды высоких шкафов под антресолью
        foreach (float zz in new[] { 18f, 26f, 34f })
            for (int i = 0; i < 6; i++)
                FilingCabinet(st, new Vector3(-31.5f + i * 2.6f, 0, zz), new Vector3(1.2f, 2.2f, 0.6f));
        // «Лестница» из шкафов на антресоль: 1 → 2 → 3 → 4 м
        for (int i = 0; i < 4; i++)
            FilingCabinet(st, new Vector3(-22.5f, 0, 7f + i * 1.3f), new Vector3(1.2f, 1f + i, 1.2f));
        // Вертикальные трубы — можно подтянуться
        Pipe(st, new Vector3(-16f, 0, 11.6f), new Vector3(-16f, 9f, 11.6f), 0.13f, "Metal");
        Pipe(st, new Vector3(-31.6f, 0, 11.6f), new Vector3(-31.6f, 9f, 11.6f), 0.13f, "Metal");
        Pipe(st, new Vector3(-33.6f, 3.2f, 0.5f), new Vector3(-14.5f, 3.2f, 0.5f), 0.09f, "Metal");
        Trampoline(ctx, dy, new Vector3(-28f, 0, 7.2f));
        Crate(st, new Vector3(-17f, 0, 3f), 1.0f); Crate(st, new Vector3(-17f, 1.0f, 3f), 0.9f); Crate(st, new Vector3(-18.2f, 0, 2.2f), 0.8f);

        // Офисная мебель на антресоли
        float floorY = 4.5f;
        foreach (float zz in new[] { 18f, 24f })
            foreach (float xx in new[] { -30f, -25f, -20f })
                Desk(st, new Vector3(xx, floorY, zz), 0f);
        WaterCooler(st, new Vector3(-15f, floorY, 13.2f));
        Plant(st, new Vector3(-33.2f, floorY, 39.2f));
        Plant(st, new Vector3(-15f, floorY, 39.2f));
        // Шкаф-ступенька под вентиляцией
        FilingCabinet(st, new Vector3(-19.3f, floorY, 32f), new Vector3(1.2f, 1.4f, 1.2f));
        SignPlate(st, "Sign_Vent", "ВЕНТИЛЯЦИЯ → ?", new Vector3(-19.3f, 7.9f, 32.62f), Vector3.back, 1.8f, 0.35f, new Color(0.3f, 1f, 0.2f));

        Place(ctx, "GoldBar", dy, new Vector3(-25f, floorY + 0.78f, 24.2f), Quaternion.Euler(0, -15, 0));
        Place(ctx, "Painting", dy, new Vector3(-33.8f, 6.3f, 28f), Quaternion.Euler(0, -90, 0));

        foreach (var p in new[] { new Vector3(-28, 8.7f, 20), new Vector3(-20, 8.7f, 20), new Vector3(-24, 8.7f, 33), new Vector3(-24, 8.7f, 5) })
        {
            Box(st, "CeilingPanel", p + Vector3.up * 0.25f, new Vector3(1.8f, 0.05f, 0.6f), "LampCold", false);
            PointLight(z, "OfficeLight", p, Cold, 2.6f, 11f);
        }
        PointLight(z, "ArchiveLight", new Vector3(-24, 3.9f, 26), Cold, 1.4f, 10f);
        SecurityCam(dy, new Vector3(-14.6f, 8.4f, 39.4f), new Vector3(-24, 4.5f, 26));
        SecurityCam(dy, new Vector3(-33.4f, 3.8f, 0.6f), new Vector3(-24, 0, 6));
        AlarmBeacon(ctx, dy, new Vector3(-24, 8.8f, 20));
    }

    // =====================================================================================
    // ХРАНИЛИЩЕ: x[-14,10] z[24,40], потолок 6 (с люком вентиляции). Лазерные решётки, сейф, золото
    // =====================================================================================
    static void BuildVault(Ctx ctx)
    {
        var (z, st, dy) = Zone(ctx, "Z5_Vault");
        Box(st, "Floor", new Vector3(-2, -0.1f, 32), new Vector3(24, 0.2f, 16), "MarbleDark");
        // потолок с люком x[-5.4,-3.8] z[31.4,32.6]
        Box(st, "Ceiling_A", new Vector3((-14 - 5.4f) / 2, 6.1f, 32), new Vector3(8.6f, 0.2f, 16.3f), "ConcreteDark");
        Box(st, "Ceiling_B", new Vector3((-3.8f + 10) / 2, 6.1f, 32), new Vector3(13.8f, 0.2f, 16.3f), "ConcreteDark");
        Box(st, "Ceiling_C", new Vector3(-4.6f, 6.1f, (24 + 31.4f) / 2), new Vector3(1.6f, 0.2f, 7.4f), "ConcreteDark");
        Box(st, "Ceiling_D", new Vector3(-4.6f, 6.1f, (32.6f + 40) / 2), new Vector3(1.6f, 0.2f, 7.4f), "ConcreteDark");
        Wall(st, "Wall_N", -14, 40, 10, 40, 0, 6, "ConcreteDark");
        Wall(st, "Wall_E", 10, 24, 10, 40, -3, 6, "ConcreteDark");
        // ячейки депозитария на северной стене
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 10; col++)
            {
                var p = new Vector3(-9.5f + col * 0.62f, 0.9f + row * 0.45f, 39.82f);
                Box(st, "DepositBox", p, new Vector3(0.58f, 0.41f, 0.04f), "Brass", false);
                Cyl(st, "Keyhole", p + new Vector3(0.18f, 0, -0.03f), 0.025f, 0.02f, "DarkMetal", false, Quaternion.Euler(90, 0, 0));
            }
        // открытая круглая дверь сейфа (декор)
        var door = Group(st, "VaultDoorDecor");
        door.position = new Vector3(4.4f, 1.5f, 25.4f); door.rotation = Quaternion.Euler(0, -55, 0);
        Cyl(door, "Disc", Vector3.zero, 1.4f, 0.4f, "SafeSteel", true, Quaternion.Euler(90, 0, 0));
        Cyl(door, "Rim", Vector3.zero, 1.45f, 0.3f, "Chrome", false, Quaternion.Euler(90, 0, 0));
        for (int i = 0; i < 10; i++) { float a = i * 36f * Mathf.Deg2Rad; Cyl(door, "Bolt", new Vector3(Mathf.Cos(a) * 1.15f, Mathf.Sin(a) * 1.15f, -0.25f), 0.07f, 0.15f, "Chrome", false, Quaternion.Euler(90, 0, 0)); }
        for (int i = 0; i < 3; i++) Box(door, "Wheel", new Vector3(0, 0, -0.32f), new Vector3(1.2f, 0.07f, 0.07f), "Chrome", false, Quaternion.Euler(0, 0, i * 60));

        // Сейф на постаменте + золото на стеллажах
        Box(st, "SafePedestal", new Vector3(-2, 0.25f, 37.6f), new Vector3(1.3f, 0.5f, 1.3f), "Marble");
        Box(st, "SafePedestalTrim", new Vector3(-2, 0.52f, 37.6f), new Vector3(1.4f, 0.04f, 1.4f), "Brass", false);
        Place(ctx, "Safe", dy, new Vector3(-2, 0.54f, 37.6f), Quaternion.identity); // дверца (−Z) смотрит на вход
        foreach (float x in new[] { -12.6f, 8.4f })
        {
            GoldShelf(st, new Vector3(x, 0, 37.6f));
            Place(ctx, "GoldBar", dy, new Vector3(x - 0.3f, 1.25f, 37.6f), Quaternion.Euler(0, 90, 0));
            Place(ctx, "GoldBar", dy, new Vector3(x + 0.3f, 1.25f, 37.6f), Quaternion.Euler(0, 90, 0));
        }

        // Лазерные решётки
        LaserGrid(dy, "LaserGrid_1_UpDown", new Vector3(-2, 0, 27.5f), LaserAlarm.MoveAxis.UpDown, 0.3f, 1.2f, 2.1f, false,
            new[] { new Vector3(0, 0.35f, 0), new Vector3(0, 0.95f, 0), new Vector3(0, 1.55f, 0) }, new Vector3(23.6f, 0.035f, 0.035f));
        var vertical = new List<Vector3>();
        for (float x = -10f; x <= 6f; x += 2.5f) vertical.Add(new Vector3(x + 2, 1.5f, 0));
        LaserGrid(dy, "LaserGrid_2_Sweep", new Vector3(-2, 0, 31f), LaserAlarm.MoveAxis.LeftRight, 1.1f, 0.9f, 0f, true,
            vertical.ToArray(), new Vector3(0.035f, 3.0f, 0.035f));
        LaserGrid(dy, "LaserGrid_3_Wave", new Vector3(-2, 0, 35f), LaserAlarm.MoveAxis.ForwardBack, 1.0f, 0.7f, 1.5f, false,
            new[] { new Vector3(0, 0.5f, 0), new Vector3(0, 1.2f, 0), new Vector3(0, 1.9f, 0) }, new Vector3(23.6f, 0.035f, 0.035f));
        // излучатели на стенах для горизонтальных решёток
        foreach (float zz in new[] { 27.5f, 35f })
            foreach (float x in new[] { -13.8f, 9.8f })
                Box(st, "LaserEmitter", new Vector3(x, 1.0f, zz), new Vector3(0.12f, 2.2f, 0.25f), "DarkMetal", false);
        Box(st, "LaserRail", new Vector3(-2, 0.01f, 31f), new Vector3(23.6f, 0.02f, 0.2f), "Hazard", false);

        // Кнопки: решётка (изнутри) и телепорт назад в Хаб
        var gate = ctx.map.GetComponentsInChildren<SlidingDoor>(true).First(d => d.name == "VaultGate");
        var plateGate = WallPlate(dy, "GatePlate_Vault", new Vector3(-2.6f, 1.3f, 24.17f), Vector3.forward, new Vector2(0.5f, 0.6f));
        Bind(Button(ctx, plateGate, Vector3.zero, "РЕШЁТКА", new Color(0.2f, 0.9f, 0.3f), 0.2f, labelAbove: true), gate.Toggle);
        var plateTp = WallPlate(dy, "TeleportPlate_Vault", new Vector3(-5.0f, 1.3f, 24.17f), Vector3.forward, new Vector2(0.5f, 0.6f));
        Bind(Button(ctx, plateTp, Vector3.zero, "ТЕЛЕПОРТ В ХАБ", new Color(0.2f, 0.5f, 1f), 0.2f, labelAbove: true), ctx.debug.TeleportToggle);

        ctx.vaultPoint = Group(z, "TeleportPoint_Vault");
        ctx.vaultPoint.SetPositionAndRotation(new Vector3(-2, 0, 25.6f), Quaternion.identity);

        for (float x = -11; x <= 8; x += 6.3f) Box(st, "CeilingStrip", new Vector3(x, 5.95f, 32), new Vector3(0.15f, 0.05f, 14f), "LampCold", false);
        PointLight(z, "VaultLight", new Vector3(-2, 5.5f, 31), new Color(0.75f, 0.85f, 1f), 2.4f, 15f);
        PointLight(z, "VaultLightBack", new Vector3(-2, 5.5f, 38), new Color(0.75f, 0.85f, 1f), 1.4f, 9f);
        SecurityCam(dy, new Vector3(9.4f, 5.6f, 24.6f), new Vector3(-2, 0, 33));
        AlarmBeacon(ctx, dy, new Vector3(-2, 5.8f, 30));
    }

    // Вентиляционная шахта: из офиса (x=-18, y 6.2) через стену в «потолок» хранилища, люк вниз над x[-5.4,-3.8]
    static void BuildDuct(Ctx ctx)
    {
        var (z, st, dy) = Zone(ctx, "Z6_VentDuct");
        const float zc = 32f, half = 0.65f, y0 = 6.2f, y1 = 7.5f, t = 0.08f;
        float xs = -18f, xe = -3.8f, hole = -5.4f;
        Box(st, "Duct_Floor", new Vector3((xs + hole) / 2, y0 - t / 2, zc), new Vector3(hole - xs, t, 2 * half + 2 * t), "Metal");
        Box(st, "Duct_Roof", new Vector3((xs + xe) / 2, y1 + t / 2, zc), new Vector3(xe - xs, t, 2 * half + 2 * t), "Metal");
        Box(st, "Duct_SideS", new Vector3((xs + xe) / 2, (y0 + y1) / 2, zc - half - t / 2), new Vector3(xe - xs, y1 - y0 + 2 * t, t), "Metal");
        Box(st, "Duct_SideN", new Vector3((xs + xe) / 2, (y0 + y1) / 2, zc + half + t / 2), new Vector3(xe - xs, y1 - y0 + 2 * t, t), "Metal");
        Box(st, "Duct_End", new Vector3(xe + t / 2, (y0 + y1) / 2, zc), new Vector3(t, y1 - y0 + 2 * t, 2 * half + 2 * t), "Metal");
        // рёбра жёсткости (за них приятно цепляться)
        for (float x = xs + 1; x < xe; x += 2f)
        {
            // рамка-ребро по периметру сечения (внутри шахты свободно)
            Box(st, "Duct_Rib_T", new Vector3(x, y1 + 0.1f, zc), new Vector3(0.06f, 0.06f, 2 * half + 0.3f), "DarkMetal", false);
            Box(st, "Duct_Rib_B", new Vector3(x, y0 - 0.1f, zc), new Vector3(0.06f, 0.06f, 2 * half + 0.3f), "DarkMetal", false);
            Box(st, "Duct_Rib_S", new Vector3(x, (y0 + y1) / 2, zc - half - 0.12f), new Vector3(0.06f, y1 - y0 + 0.26f, 0.06f), "DarkMetal", false);
            Box(st, "Duct_Rib_N", new Vector3(x, (y0 + y1) / 2, zc + half + 0.12f), new Vector3(0.06f, y1 - y0 + 0.26f, 0.06f), "DarkMetal", false);
        }
        // решётка-рамка входа
        Box(st, "Grille_T", new Vector3(xs - 0.02f, y1 + 0.05f, zc), new Vector3(0.06f, 0.1f, 1.6f), "DarkMetal");
        Box(st, "Grille_B", new Vector3(xs - 0.02f, y0 - 0.05f, zc), new Vector3(0.06f, 0.1f, 1.6f), "DarkMetal");
        // жёлто-чёрная рамка люка в хранилище
        Box(st, "Hatch_Frame", new Vector3((hole + xe) / 2, 5.99f, zc), new Vector3(1.7f, 0.03f, 1.3f), "Hazard", false);
        Box(st, "Duct_LightStrip", new Vector3((xs + xe) / 2, y1 - 0.02f, zc), new Vector3(xe - xs - 0.4f, 0.02f, 0.08f), "Neon", false);
        foreach (float x in new[] { -15f, -10f, -6f })
            PointLight(z, "DuctLight", new Vector3(x, 7.2f, zc), new Color(0.5f, 1f, 0.6f), 1.2f, 4.5f);
    }

    // =====================================================================================
    // ПОДЗЕМНЫЙ ГАРАЖ: x[10,34] z[24,44], пол -3, потолок 2.5. Пандус из галереи, фургон-эвакуация
    // =====================================================================================
    static void BuildGarage(Ctx ctx)
    {
        var (z, st, dy) = Zone(ctx, "Z7_Garage");
        Box(st, "Floor", new Vector3(22, -3.1f, 34), new Vector3(24, 0.2f, 20), "Concrete");
        Box(st, "Ceiling", new Vector3(22, 2.6f, 34), new Vector3(24.6f, 0.2f, 20.6f), "ConcreteDark");
        Wall(st, "Wall_N", 10, 44, 34, 44, -3, 2.5f, "Concrete");
        Wall(st, "Wall_E", 34, 24, 34, 44, -3, 2.5f, "Concrete");
        Wall(st, "Wall_W", 10, 40, 10, 44, -3, 2.5f, "Concrete");

        // Пандус из галереи (z=24, y=0) вниз до (z=32, y=-3)
        float ang = Mathf.Atan2(3f, 8f) * Mathf.Rad2Deg, len = Mathf.Sqrt(64 + 9);
        Box(st, "Ramp", new Vector3(30.5f, -1.6f, 28f), new Vector3(5f, 0.2f, len), "ConcreteDark", true, Quaternion.Euler(ang, 0, 0));
        Box(st, "RampRail_W", new Vector3(27.95f, -1.15f, 28f), new Vector3(0.1f, 0.7f, len), "PaintYellow", true, Quaternion.Euler(ang, 0, 0));
        for (float zz = 24.6f; zz < 32; zz += 1.2f)
        {
            float y = -3f * (zz - 24f) / 8f;
            Box(st, "RampStripe", new Vector3(30.5f, y + 0.01f, zz), new Vector3(4.6f, 0.01f, 0.25f), "PaintYellow", false, Quaternion.Euler(ang, 0, 0));
        }

        // Колонны и разметка
        foreach (var p in new[] { new Vector3(13.5f, -3, 29), new Vector3(25, -3, 29), new Vector3(25, -3, 40.5f), new Vector3(13.5f, -3, 40.5f) })
        {
            Box(st, "Pillar", p + Vector3.up * 2.75f, new Vector3(0.7f, 5.5f, 0.7f), "Concrete");
            Box(st, "PillarStripe", p + Vector3.up * 0.4f, new Vector3(0.72f, 0.6f, 0.72f), "Hazard", false);
        }
        for (float x = 12; x < 27; x += 3f) Box(st, "ParkingLine", new Vector3(x, -2.995f, 26.5f), new Vector3(0.1f, 0.01f, 4f), "PaintYellow", false);

        Van(ctx, st, dy, new Vector3(19.5f, -3f, 36f));

        Crate(st, new Vector3(31.5f, -3, 41.5f), 1.2f); Crate(st, new Vector3(31.5f, -1.8f, 41.5f), 1.0f); Crate(st, new Vector3(30.1f, -3, 42.3f), 1.0f);
        for (int i = 0; i < 3; i++) Cyl(st, "Barrel", new Vector3(11.2f + i * 0.75f, -2.55f, 43.1f), 0.32f, 0.9f, "BarrelRed");
        for (int i = 0; i < 4; i++) Cyl(st, "Tire", new Vector3(32.8f, -2.88f + i * 0.24f, 26.5f), 0.4f, 0.22f, "Rubber");

        Scoreboard(dy, "Scoreboard_Garage", new Vector3(22, 0.4f, 43.83f), Vector3.back, 4.2f, 1.3f);
        foreach (var p in new[] { new Vector3(16, 2.2f, 30), new Vector3(28, 2.2f, 37), new Vector3(18, 2.2f, 41.5f) })
        {
            Box(st, "SodiumFixture", p + Vector3.up * 0.25f, new Vector3(1.2f, 0.08f, 0.3f), "LampSodium", false);
            PointLight(z, "SodiumLight", p, Sodium, 2.6f, 11f);
        }
        SecurityCam(dy, new Vector3(33.4f, 2.1f, 43.4f), new Vector3(22, -3, 34));
        AlarmBeacon(ctx, dy, new Vector3(22, 2.3f, 33));
    }

    // =====================================================================================
    // ИНТЕРАКТИВ
    // =====================================================================================

    static void Bind(PushButton b, UnityAction action)
    {
        UnityEventTools.AddVoidPersistentListener(b.onPressed, action);
        EditorUtility.SetDirty(b);
    }

    /// Пластина на стене: локальная Y = нормаль (facing), локальная −Z = мировой «верх».
    static Transform WallPlate(Transform parent, string name, Vector3 pos, Vector3 facing, Vector2 size)
    {
        var plate = Group(parent, name);
        plate.SetPositionAndRotation(pos, Quaternion.LookRotation(Vector3.down, facing));
        Box(plate, "Plate", new Vector3(0, -0.01f, 0), new Vector3(size.x, 0.02f, size.y), "DarkMetal", false);
        return plate;
    }

    /// Большая физическая кнопка на «панели» (локальная Y — нормаль панели).
    static PushButton Button(Ctx ctx, Transform panel, Vector3 local, string label, Color color, float diameter, bool labelAbove = false)
    {
        var btn = Group(panel, "Button_" + label);
        btn.localPosition = local;
        btn.gameObject.layer = IgnoreRaycast;
        Cyl(btn, "Housing", new Vector3(0, 0.01f, 0), diameter * 0.65f, 0.02f, "DarkMetal", false);
        var capMat = ColorMat("Btn_" + ColorUtility.ToHtmlStringRGB(color), color, 0.6f);
        var cap = Cyl(btn, "Cap", new Vector3(0, 0.04f, 0), diameter / 2, 0.04f, null, false);
        cap.GetComponent<MeshRenderer>().sharedMaterial = capMat;
        var trig = btn.gameObject.AddComponent<BoxCollider>();
        trig.isTrigger = true;
        trig.center = new Vector3(0, 0.06f, 0);
        trig.size = new Vector3(diameter * 1.15f, 0.1f, diameter * 1.15f);
        var src = btn.gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 1f;
        var pb = btn.gameObject.AddComponent<PushButton>();
        pb.label = label; pb.cap = cap.transform; pb.clickClips = ctx.click;
        float off = diameter * 0.75f + 0.05f;
        Text(panel, "Label_" + label, label, 0.9f, Color.white, local + new Vector3(0, 0.025f, labelAbove ? -off : off),
            Quaternion.LookRotation(Vector3.down, Vector3.back), new Vector2(Mathf.Max(0.45f, diameter * 2.5f), 0.15f));
        return pb;
    }

    /// Цветной светящийся материал колпачка кнопки (создаётся один раз и переиспользуется).
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

    static void DebugConsole(Ctx ctx, Transform parent, Vector3 pos)
    {
        var root = Group(parent, "DebugConsole");
        root.SetPositionAndRotation(pos, Quaternion.Euler(0, 180, 0)); // локальная +Z смотрит на игрока (юг)
        Box(root, "Desk", new Vector3(0, 0.5f, 0), new Vector3(2.6f, 1.0f, 0.7f), "DarkMetal");
        Box(root, "DeskTrim", new Vector3(0, 1.0f, 0.36f), new Vector3(2.6f, 0.06f, 0.04f), "Hazard", false);
        var panel = Group(root, "Panel");
        panel.localPosition = new Vector3(0, 1.18f, 0.05f);
        panel.localRotation = Quaternion.Euler(35, 0, 0);
        Box(panel, "PanelBoard", Vector3.zero, new Vector3(2.6f, 0.05f, 0.75f), "MarbleDark");
        var btns = new (string label, Color c, UnityAction a)[]
        {
            ("СТАРТ ОГРАБЛЕНИЯ (3 МИН)", new Color(0.2f, 1f, 0.3f), ctx.debug.StartHeist),
            ("ТЕСТ: ТРЕВОГА / ЛАЗЕРЫ", new Color(1f, 0.15f, 0.1f), ctx.debug.ToggleAlarm),
            ("ТЕСТ: СБРОСИТЬ ВЕСЬ ЛУТ", new Color(1f, 0.8f, 0.15f), ctx.debug.ResetLoot),
            ("ТЕСТ: ТЕЛЕПОРТ ХРАНИЛИЩЕ / ХАБ", new Color(0.25f, 0.5f, 1f), ctx.debug.TeleportToggle),
        };
        for (int i = 0; i < btns.Length; i++)
            Bind(Button(ctx, panel, new Vector3(-0.96f + i * 0.64f, 0.025f, -0.06f), btns[i].label, btns[i].c, 0.24f), btns[i].a);
        Text(root, "Title", "ПУЛЬТ ТЕСТИРОВАНИЯ", 2f, new Color(1f, 0.8f, 0.2f), new Vector3(0, 1.85f, -0.2f), Quaternion.Euler(0, 180, 0), new Vector2(2.6f, 0.3f));
        Box(root, "TitleBack", new Vector3(0, 1.85f, -0.22f), new Vector3(2.7f, 0.36f, 0.03f), "DarkMetal", false);
        Box(root, "TitlePost", new Vector3(0, 1.45f, -0.25f), new Vector3(0.08f, 0.5f, 0.05f), "DarkMetal", false);
    }

    static readonly (string name, Color color)[] SkinColors =
    {
        ("ЧЁРНЫЙ",  new Color(0.07f, 0.07f, 0.08f)),
        ("ЗОЛОТОЙ", new Color(0.95f, 0.70f, 0.18f)),
        ("КРАСНЫЙ", new Color(0.80f, 0.10f, 0.10f)),
        ("НЕОН",    new Color(0.30f, 1.00f, 0.12f)),
        ("СИНИЙ",   new Color(0.15f, 0.35f, 0.95f)),
        ("ФИОЛЕТ",  new Color(0.55f, 0.20f, 0.80f)),
    };

    static void SkinTerminal(Ctx ctx, Transform parent, Vector3 pos)
    {
        var term = Group(parent, "SkinTerminal");
        term.SetPositionAndRotation(pos, Quaternion.Euler(0, 90, 0)); // локальная +Z → восток (к игроку)
        Box(term, "Pedestal", new Vector3(0, 0.5f, 0), new Vector3(0.9f, 1.0f, 0.45f), "WoodDark");
        var panel = Group(term, "Panel");
        panel.localPosition = new Vector3(0, 1.2f, 0.05f);
        panel.localRotation = Quaternion.Euler(35, 0, 0);
        Box(panel, "PanelBoard", Vector3.zero, new Vector3(0.95f, 0.04f, 0.55f), "MarbleDark");
        Text(term, "Title", "ВЫБЕРИ ЦВЕТ", 1.6f, new Color(0.3f, 1f, 0.2f), new Vector3(0, 1.72f, -0.15f), Quaternion.Euler(0, 180, 0), new Vector2(1.2f, 0.3f));
        for (int i = 0; i < SkinColors.Length; i++)
        {
            var (label, color) = SkinColors[i];
            var local = new Vector3((i % 3 - 1) * 0.28f, 0.02f, i / 3 == 0 ? -0.1f : 0.14f);
            var btn = Group(panel, "SkinButton_" + i);
            btn.localPosition = local; btn.gameObject.layer = IgnoreRaycast;
            Cyl(btn, "Base", new Vector3(0, 0.005f, 0), 0.075f, 0.01f, "DarkMetal", false);
            var capMat = ColorMat("SkinCap_" + i, color, 0.35f);
            var cap = Cyl(btn, "Cap", new Vector3(0, 0.03f, 0), 0.06f, 0.04f, null, false);
            cap.GetComponent<MeshRenderer>().sharedMaterial = capMat;
            var trig = btn.gameObject.AddComponent<BoxCollider>(); trig.isTrigger = true; trig.center = new Vector3(0, 0.05f, 0); trig.size = new Vector3(0.14f, 0.08f, 0.14f);
            var src = btn.gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 1f;
            var sb = btn.gameObject.AddComponent<SkinButton>();
            sb.colorName = label; sb.furColor = color; sb.cap = cap.transform; sb.clickClips = ctx.click;
            Text(panel, "Label_" + i, label, 0.55f, Color.white, local + new Vector3(0, 0.025f, 0.095f), Quaternion.LookRotation(Vector3.down, Vector3.back), new Vector2(0.26f, 0.06f));
        }
    }

    static void Mirror(Ctx ctx, Transform st, Transform dy, Vector3 pos)
    {
        var mirror = Group(dy, "Mirror");
        mirror.SetPositionAndRotation(pos, Quaternion.Euler(0, 90, 0)); // локальная +Z = мировая +X (в комнату)
        string rtPath = $"{MatDir}/RT_HubMirror.renderTexture";
        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(rtPath);
        if (rt == null) { rt = new RenderTexture(720, 800, 24) { name = "RT_HubMirror", antiAliasing = 2 }; AssetDatabase.CreateAsset(rt, rtPath); }
        string matPath = $"{MatDir}/M_HubMirror.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(mat, matPath); }
        mat.SetTexture("_BaseMap", rt);
        mat.SetTextureScale("_BaseMap", new Vector2(-1, 1));
        mat.SetTextureOffset("_BaseMap", new Vector2(1, 0));
        EditorUtility.SetDirty(mat);
        const float w = 2.2f, h = 2.4f;
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "MirrorSurface";
        UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
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

    static void Airlock(Ctx ctx, Transform st, Transform dy)
    {
        // рама с предупреждающими полосами (со стороны Хаба)
        Box(st, "Airlock_PostL", new Vector3(-1.68f, 1.55f, -0.25f), new Vector3(0.36f, 3.1f, 0.25f), "Hazard");
        Box(st, "Airlock_PostR", new Vector3(1.68f, 1.55f, -0.25f), new Vector3(0.36f, 3.1f, 0.25f), "Hazard");
        Box(st, "Airlock_Lintel", new Vector3(0, 3.08f, -0.25f), new Vector3(3.72f, 0.16f, 0.25f), "Hazard");
        Text(st, "Airlock_Sign", "ШЛЮЗ · ВХОД В БАНК", 1.2f, new Color(1f, 0.8f, 0.2f), new Vector3(0, 2.75f, -0.39f), Facing(Vector3.back), new Vector2(2.8f, 0.25f));

        var door = Group(dy, "AirlockDoor");
        door.position = Vector3.zero;
        var leafL = Group(door, "Leaf_L"); leafL.localPosition = new Vector3(-0.75f, 1.5f, 0);
        var leafR = Group(door, "Leaf_R"); leafR.localPosition = new Vector3(0.75f, 1.5f, 0);
        foreach (var leaf in new[] { leafL, leafR })
        {
            Box(leaf, "Panel", Vector3.zero, new Vector3(1.5f, 3f, 0.16f), "DarkMetal");
            Box(leaf, "Stripe_Hub", new Vector3(0, -0.6f, -0.09f), new Vector3(1.5f, 0.3f, 0.02f), "Hazard", false);
            Box(leaf, "Stripe_Hall", new Vector3(0, -0.6f, 0.09f), new Vector3(1.5f, 0.3f, 0.02f), "Hazard", false);
            Box(leaf, "Window", new Vector3(0, 0.6f, 0), new Vector3(0.4f, 0.5f, 0.17f), "Glass", false);
        }
        var lamp = Sph(dy, "Airlock_Lamp", new Vector3(0, 3.35f, -0.35f), Vector3.one * 0.2f, "Beacon");
        var sd = door.gameObject.AddComponent<SlidingDoor>();
        sd.leaves = new[] { new SlidingDoor.Leaf { part = leafL, openOffset = new Vector3(-1.5f, 0, 0) }, new SlidingDoor.Leaf { part = leafR, openOffset = new Vector3(1.5f, 0, 0) } };
        sd.duration = 1.6f; sd.openClips = ctx.open; sd.closeClips = ctx.close; sd.statusLamp = lamp.GetComponent<Renderer>();
        door.gameObject.AddComponent<AirlockDoor>();
        ctx.airlock = sd;
    }

    static SlidingDoor VaultGate(Ctx ctx, Transform dy, Vector3 pos)
    {
        var gate = Group(dy, "VaultGate");
        gate.position = pos;
        var bars = Group(gate, "Bars"); bars.localPosition = new Vector3(0, 1.5f, 0);
        var col = bars.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(3f, 3f, 0.12f);
        for (float x = -1.4f; x <= 1.41f; x += 0.2f) Cyl(bars, "Bar", new Vector3(x, 0, 0), 0.03f, 3f, "Chrome", false);
        foreach (float y in new[] { -1.2f, 0f, 1.2f }) Box(bars, "Cross", new Vector3(0, y, 0), new Vector3(3f, 0.08f, 0.06f), "DarkMetal", false);
        var lamp = Sph(gate, "GateLamp", new Vector3(0, 3.25f, -0.25f), Vector3.one * 0.18f, "Beacon");
        var sd = gate.gameObject.AddComponent<SlidingDoor>();
        sd.leaves = new[] { new SlidingDoor.Leaf { part = bars, openOffset = new Vector3(0, 3.05f, 0) } };
        sd.duration = 1.8f; sd.openClips = ctx.open; sd.closeClips = ctx.close; sd.statusLamp = lamp.GetComponent<Renderer>();
        return sd;
    }

    static void LaserGrid(Transform parent, string name, Vector3 pos, LaserAlarm.MoveAxis axis, float amp, float speed, float phaseStep, bool asOne, Vector3[] beamLocals, Vector3 beamSize)
    {
        var grid = Group(parent, name);
        grid.position = pos;
        var la = grid.gameObject.AddComponent<LaserAlarm>();
        la.axis = axis; la.amplitude = amp; la.speed = speed; la.phaseStep = phaseStep; la.moveAsOne = asOne;
        for (int i = 0; i < beamLocals.Length; i++)
        {
            var b = Box(grid, "Beam_" + i, beamLocals[i], beamSize, "LaserRed");
            b.layer = IgnoreRaycast;
            b.GetComponent<Collider>().isTrigger = true;
            b.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var rb = b.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.Interpolate;
            b.AddComponent<LaserBeam>();
        }
    }

    static void Showcase(Ctx ctx, Transform st, Transform dy, Vector3 pos, float baseH, Vector3 glass, string lootId)
    {
        Box(st, "Showcase_Base", pos + Vector3.up * baseH / 2, new Vector3(glass.x + 0.2f, baseH, glass.z + 0.2f), "MarbleDark");
        Box(st, "Showcase_Trim", pos + Vector3.up * (baseH + 0.02f), new Vector3(glass.x + 0.24f, 0.04f, glass.z + 0.24f), "Brass", false);
        Box(st, "Showcase_Cushion", pos + Vector3.up * (baseH + 0.07f), new Vector3(glass.x * 0.6f, 0.06f, glass.z * 0.6f), "Velvet", false);

        var sc = Group(dy, "Showcase_" + lootId);
        sc.position = pos + Vector3.up * (baseH + 0.04f);
        var rb = sc.gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false; // коллайдеры стёкол = составной, события удара — на корень
        float gx = glass.x, gy = glass.y, gz = glass.z, t = 0.02f;
        var panes = new List<GameObject>
        {
            Box(sc, "Pane_Top", new Vector3(0, gy, 0), new Vector3(gx, t, gz), "Glass"),
            Box(sc, "Pane_N", new Vector3(0, gy / 2, gz / 2), new Vector3(gx, gy, t), "Glass"),
            Box(sc, "Pane_S", new Vector3(0, gy / 2, -gz / 2), new Vector3(gx, gy, t), "Glass"),
            Box(sc, "Pane_E", new Vector3(gx / 2, gy / 2, 0), new Vector3(t, gy, gz), "Glass"),
            Box(sc, "Pane_W", new Vector3(-gx / 2, gy / 2, 0), new Vector3(t, gy, gz), "Glass"),
        };
        foreach (var (x, z) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
            panes.Add(Box(sc, "Edge", new Vector3(x * gx / 2, gy / 2, z * gz / 2), new Vector3(0.025f, gy, 0.025f), "Brass", false));
        var bg = sc.gameObject.AddComponent<BreakableGlass>();
        bg.panes = panes.ToArray(); bg.breakClips = ctx.glass; bg.shardMaterial = M("Glass");

        float top = baseH + 0.1f;
        if (lootId == "GoldBar2")
        {
            Place(ctx, "GoldBar", dy, pos + new Vector3(-0.15f, top, 0), Quaternion.Euler(0, 10, 0));
            Place(ctx, "GoldBar", dy, pos + new Vector3(0.15f, top, 0), Quaternion.Euler(0, -10, 0));
        }
        else Place(ctx, lootId, dy, pos + Vector3.up * top, Quaternion.identity);
    }

    static void Trampoline(Ctx ctx, Transform dy, Vector3 pos)
    {
        var tr = Group(dy, "Trampoline");
        tr.position = pos;
        const float s = 1.9f, h = 0.45f;
        foreach (var (x, z) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
            Cyl(tr, "Leg", new Vector3(x * (s / 2 - 0.05f), h / 2, z * (s / 2 - 0.05f)), 0.04f, h, "DarkMetal", false);
        Box(tr, "Frame_N", new Vector3(0, h, s / 2), new Vector3(s + 0.1f, 0.08f, 0.1f), "Metal", false);
        Box(tr, "Frame_S", new Vector3(0, h, -s / 2), new Vector3(s + 0.1f, 0.08f, 0.1f), "Metal", false);
        Box(tr, "Frame_E", new Vector3(s / 2, h, 0), new Vector3(0.1f, 0.08f, s), "Metal", false);
        Box(tr, "Frame_W", new Vector3(-s / 2, h, 0), new Vector3(0.1f, 0.08f, s), "Metal", false);
        var pad = Box(tr, "Pad", new Vector3(0, h, 0), new Vector3(s - 0.1f, 0.06f, s - 0.1f), "Pad");
        var surf = pad.AddComponent<GorillaLocomotion.Surface>(); surf.bounceMultiplier = 2.4f; surf.slipPercentage = 0f;
        pad.AddComponent<AudioSource>();
        pad.AddComponent<global::Trampoline>().boingClips = ctx.boing;
        var trig = new GameObject("LaunchTrigger");
        trig.transform.SetParent(tr, false);
        trig.transform.localPosition = new Vector3(0, h + 0.35f, 0);
        trig.layer = IgnoreRaycast;
        var bc = trig.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(s - 0.2f, 0.6f, s - 0.2f);
        trig.AddComponent<AudioSource>();
        trig.AddComponent<global::Trampoline>().boingClips = ctx.boing;
        Text(tr, "Label", "БАТУТ ↑", 1f, new Color(0.3f, 0.7f, 1f), new Vector3(0, h + 0.04f, 0), Quaternion.Euler(90, 0, 0), new Vector2(1.4f, 0.4f));
    }

    static void SecurityCam(Transform parent, Vector3 pos, Vector3 lookAt)
    {
        var root = Group(parent, "SecurityCamera");
        root.position = pos;
        var dir = lookAt - pos;
        var flat = new Vector3(dir.x, 0, dir.z).normalized;
        root.rotation = Quaternion.LookRotation(flat);
        Box(root, "Bracket", new Vector3(0, 0.12f, -0.25f), new Vector3(0.08f, 0.3f, 0.08f), "DarkMetal", false);
        var pivot = Group(root, "Pivot");
        pivot.localRotation = Quaternion.LookRotation(Quaternion.Inverse(root.rotation) * dir.normalized);
        Box(pivot, "Body", new Vector3(0, 0, 0.05f), new Vector3(0.18f, 0.16f, 0.36f), "VanBody", false);
        Box(pivot, "Hood", new Vector3(0, 0.1f, 0.08f), new Vector3(0.22f, 0.03f, 0.42f), "DarkMetal", false);
        Cyl(pivot, "Lens", new Vector3(0, 0, 0.24f), 0.055f, 0.04f, "Rubber", false, Quaternion.Euler(90, 0, 0));
        Sph(pivot, "Led", new Vector3(0.06f, -0.05f, 0.23f), Vector3.one * 0.025f, "LaserRed");
        const float range = 8f, half = 22f;
        float r = Mathf.Tan(half * Mathf.Deg2Rad) * range;
        var cone = MeshObj(pivot, "ViewCone", ConeMesh, new Vector3(0, 0, 0.26f), new Vector3(r, r, range), "ConeView");
        cone.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        var sc = root.gameObject.AddComponent<SecurityCamera>();
        sc.pivot = pivot; sc.cone = cone.GetComponent<Renderer>(); sc.range = range; sc.viewAngle = half;
    }

    static void AlarmBeacon(Ctx ctx, Transform dy, Vector3 pos)
    {
        var b = Group(dy, "AlarmBeacon");
        b.position = pos;
        Cyl(b, "Base", new Vector3(0, 0.08f, 0), 0.12f, 0.06f, "DarkMetal", false);
        var dome = Sph(b, "Dome", Vector3.zero, new Vector3(0.22f, 0.16f, 0.22f), "Beacon");
        ctx.beacons.Add(dome.GetComponent<Renderer>());
        var l = PointLight(b, "AlarmLight", new Vector3(0, -0.4f, 0), new Color(1f, 0.08f, 0.05f), 0f, 18f);
        l.enabled = false;
        ctx.alarmLights.Add(l);
    }

    static void Scoreboard(Transform parent, string name, Vector3 pos, Vector3 facing, float w, float h)
    {
        var root = Group(parent, name);
        root.SetPositionAndRotation(pos, Quaternion.LookRotation(-facing)); // локальная −Z = лицевая сторона
        Box(root, "Back", Vector3.zero, new Vector3(w, h, 0.06f), "DarkMetal", false);
        Box(root, "Frame_T", new Vector3(0, h / 2, -0.02f), new Vector3(w + 0.06f, 0.04f, 0.06f), "Neon", false);
        Box(root, "Frame_B", new Vector3(0, -h / 2, -0.02f), new Vector3(w + 0.06f, 0.04f, 0.06f), "Neon", false);
        var sb = root.gameObject.AddComponent<global::Scoreboard>();
        sb.lootText = Text(root, "LootText", "LOOT: $0 / $4000", h * 2.6f, new Color(1f, 0.8f, 0.2f), new Vector3(0, h * 0.27f, -0.04f), Quaternion.identity, new Vector2(w * 0.95f, h * 0.36f));
        sb.timerText = Text(root, "TimerText", "ВРЕМЯ: 3:00", h * 2.2f, Color.white, new Vector3(0, -h * 0.03f, -0.04f), Quaternion.identity, new Vector2(w * 0.95f, h * 0.28f));
        sb.hintText = Text(root, "HintText", "ЖМИ «СТАРТ» НА ПУЛЬТЕ", h * 1.8f, new Color(0.3f, 1f, 0.2f), new Vector3(0, -h * 0.32f, -0.04f), Quaternion.identity, new Vector2(w * 0.95f, h * 0.26f));
        foreach (var t in new[] { sb.lootText, sb.timerText, sb.hintText }) t.textWrappingMode = TextWrappingModes.NoWrap;
    }

    static GameObject Place(Ctx ctx, string id, Transform parent, Vector3 pos, Quaternion rot)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(ctx.loot[id], parent);
        go.transform.SetPositionAndRotation(pos, rot);
        return go;
    }

    static void SetArt(GameObject painting, string art)
    {
        var canvas = painting.transform.Find("Canvas");
        if (canvas) canvas.GetComponent<MeshRenderer>().sharedMaterial = M(art);
    }

    // =====================================================================================
    // ДЕКОР (составные low-poly модели)
    // =====================================================================================

    static void Pipe(Transform parent, Vector3 a, Vector3 b, float radius, string mat, bool collider = true)
    {
        var d = b - a;
        Cyl(parent, "Pipe", (a + b) / 2, radius, d.magnitude, mat, collider, Quaternion.FromToRotation(Vector3.up, d.normalized));
    }

    static void Railing(Transform parent, Vector3 a, Vector3 b)
    {
        var d = b - a; float len = d.magnitude; var dir = d / len;
        int n = Mathf.Max(1, Mathf.RoundToInt(len / 1.5f));
        for (int i = 0; i <= n; i++) Cyl(parent, "RailPost", a + dir * (len * i / n) + Vector3.up * 0.5f, 0.035f, 1f, "Brass", false);
        Pipe(parent, a + Vector3.up * 1.0f, b + Vector3.up * 1.0f, 0.045f, "Brass");
        Pipe(parent, a + Vector3.up * 0.5f, b + Vector3.up * 0.5f, 0.025f, "Brass", false);
    }

    static void Column(Transform parent, Vector3 p, float h)
    {
        Cyl(parent, "ColumnBase", p + Vector3.up * 0.2f, 0.8f, 0.4f, "MarbleDark");
        Cyl(parent, "ColumnShaft", p + Vector3.up * h / 2, 0.55f, h, "Marble");
        Cyl(parent, "ColumnCapital", p + Vector3.up * (h - 0.25f), 0.8f, 0.5f, "MarbleDark");
        foreach (float y in new[] { 2.5f, 4.5f, 6.5f, 8f })
            Cyl(parent, "ColumnRing", p + Vector3.up * y, 0.64f, 0.15f, "Brass");
    }

    static void IBeam(Transform parent, Vector3 c, float len)
    {
        Box(parent, "Beam_Top", c + Vector3.up * 0.24f, new Vector3(len, 0.06f, 0.36f), "DarkMetal");
        Box(parent, "Beam_Web", c, new Vector3(len, 0.42f, 0.07f), "DarkMetal");
        Box(parent, "Beam_Bottom", c - Vector3.up * 0.24f, new Vector3(len, 0.06f, 0.36f), "DarkMetal");
    }

    static void HangingLamp(Transform parent, Vector3 ceiling, string bulb)
    {
        Cyl(parent, "LampCord", ceiling + Vector3.down * 0.3f, 0.01f, 0.6f, "DarkMetal", false);
        Cyl(parent, "LampShade", ceiling + Vector3.down * 0.65f, 0.22f, 0.16f, "DarkMetal", false);
        Sph(parent, "LampBulb", ceiling + Vector3.down * 0.75f, Vector3.one * 0.14f, bulb);
    }

    static void Chandelier(Transform parent, Vector3 c)
    {
        Cyl(parent, "ChandelierRod", c + Vector3.up * 0.8f, 0.03f, 1.6f, "Brass", false);
        Cyl(parent, "ChandelierRing", c, 1.4f, 0.06f, "Brass", false);
        for (int i = 0; i < 10; i++)
        {
            float a = i * Mathf.PI * 2 / 10;
            Sph(parent, "ChandelierBulb", c + new Vector3(Mathf.Cos(a) * 1.4f, -0.12f, Mathf.Sin(a) * 1.4f), Vector3.one * 0.18f, "LampWarm");
        }
    }

    static void Workbench(Transform parent, Vector3 p)
    {
        var g = Group(parent, "Workbench"); g.position = p;
        Box(g, "Top", new Vector3(0, 0.92f, 0), new Vector3(1.2f, 0.08f, 2.6f), "WoodLight");
        foreach (var (x, z) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
            Box(g, "Leg", new Vector3(x * 0.52f, 0.44f, z * 1.2f), new Vector3(0.08f, 0.88f, 0.08f), "DarkMetal");
        Box(g, "Shelf", new Vector3(0, 0.25f, 0), new Vector3(1.1f, 0.04f, 2.4f), "WoodDark");
        Box(g, "Pegboard", new Vector3(0.68f, 1.65f, 0), new Vector3(0.04f, 1.2f, 2.6f), "WoodDark");
        for (int i = 0; i < 5; i++) Box(g, "Tool", new Vector3(0.62f, 1.4f + (i % 2) * 0.4f, -1f + i * 0.5f), new Vector3(0.04f, 0.3f, 0.06f), i % 2 == 0 ? "Metal" : "BarrelRed", false);
        Box(g, "Blueprint_1", new Vector3(-0.1f, 0.965f, -0.4f), new Vector3(0.7f, 0.005f, 0.9f), "Blueprint", false, Quaternion.Euler(0, 8, 0));
        Box(g, "Blueprint_2", new Vector3(0.05f, 0.97f, 0.5f), new Vector3(0.6f, 0.005f, 0.8f), "Blueprint", false, Quaternion.Euler(0, -12, 0));
        Cyl(g, "BlueprintRoll", new Vector3(-0.35f, 1.0f, 1.0f), 0.04f, 0.7f, "Paper", false, Quaternion.Euler(0, 0, 90));
        Cyl(g, "DeskLampBase", new Vector3(0.35f, 0.98f, -1.05f), 0.08f, 0.03f, "DarkMetal", false);
        Cyl(g, "DeskLampArm", new Vector3(0.35f, 1.2f, -1.05f), 0.015f, 0.45f, "DarkMetal", false);
        Sph(g, "DeskLampBulb", new Vector3(0.3f, 1.42f, -1.0f), Vector3.one * 0.08f, "LampWarm");
        Box(g, "Laptop", new Vector3(-0.15f, 0.975f, 1.0f), new Vector3(0.35f, 0.02f, 0.25f), "DarkMetal", false);
        Box(g, "LaptopScreen", new Vector3(-0.3f, 1.1f, 1.0f), new Vector3(0.02f, 0.24f, 0.35f), "Screen", false, Quaternion.Euler(0, 0, 15));
    }

    static void PlanningBoard(Transform parent, Vector3 c)
    {
        var g = Group(parent, "PlanningBoard"); g.SetPositionAndRotation(c, Quaternion.LookRotation(Vector3.back)); // лицо (−Z) → север, в комнату
        Box(g, "Cork", Vector3.zero, new Vector3(4.2f, 2.0f, 0.04f), "Cork", false);
        Box(g, "Frame_T", new Vector3(0, 1.03f, 0), new Vector3(4.3f, 0.08f, 0.06f), "WoodDark", false);
        Box(g, "Frame_B", new Vector3(0, -1.03f, 0), new Vector3(4.3f, 0.08f, 0.06f), "WoodDark", false);
        Box(g, "Frame_L", new Vector3(-2.13f, 0, 0), new Vector3(0.08f, 2.1f, 0.06f), "WoodDark", false);
        Box(g, "Frame_R", new Vector3(2.13f, 0, 0), new Vector3(0.08f, 2.1f, 0.06f), "WoodDark", false);
        Text(g, "Title", "ПЛАН ОГРАБЛЕНИЯ", 2.4f, new Color(0.8f, 0.05f, 0.05f), new Vector3(0, 0.82f, -0.03f), Quaternion.identity, new Vector2(4f, 0.3f));
        Box(g, "RulesPaper", new Vector3(-0.85f, -0.12f, -0.025f), new Vector3(2.3f, 1.5f, 0.005f), "Paper", false);
        Text(g, "Rules",
            "1. Жми <b>СТАРТ</b> на пульте — шлюз откроется, пойдёт таймер 3:00\n" +
            "2. Укради лут и донеси до <b>фургона</b> в подземном гараже\n" +
            "3. Цель — <b>$4000</b>. Сейф тяжёлый: −40% к скорости\n" +
            "4. Лазеры, камеры и разбитое стекло = <b>ТРЕВОГА</b> (−15 сек)\n" +
            "5. Ваза хрупкая — не швыряй её!\n" +
            "6. Секрет: вентиляция из офисов ведёт прямо в хранилище",
            0.9f, new Color(0.1f, 0.1f, 0.12f), new Vector3(-0.85f, -0.12f, -0.03f), Quaternion.identity, new Vector2(2.15f, 1.4f), TextAlignmentOptions.TopLeft, bold: false);
        Box(g, "Blueprint", new Vector3(1.25f, -0.1f, -0.025f), new Vector3(1.5f, 1.3f, 0.005f), "Blueprint", false);
        foreach (var (x, y, m) in new[] { (0.75f, 0.45f, "Art1"), (1.75f, 0.5f, "Art2"), (1.65f, -0.65f, "Art3") })
            Box(g, "Photo", new Vector3(x, y, -0.03f), new Vector3(0.3f, 0.22f, 0.005f), m, false, Quaternion.Euler(0, 0, (x * 37) % 10 - 5));
        Pipe(g, new Vector3(0.75f, 0.45f, -0.035f), new Vector3(1.0f, -0.2f, -0.035f), 0.006f, "CarpetRed", false);
        Pipe(g, new Vector3(1.75f, 0.5f, -0.035f), new Vector3(1.4f, -0.2f, -0.035f), 0.006f, "CarpetRed", false);
    }

    static void Rack(Transform parent, Vector3 p, float yaw)
    {
        var g = Group(parent, "Rack"); g.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
        foreach (var (x, z) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
            Box(g, "Upright", new Vector3(x * 1.15f, 1.1f, z * 0.32f), new Vector3(0.06f, 2.2f, 0.06f), "DarkMetal");
        for (int i = 0; i < 4; i++) Box(g, "Shelf", new Vector3(0, 0.3f + i * 0.6f, 0), new Vector3(2.4f, 0.04f, 0.7f), "Metal");
        var rnd = new System.Random((int)(p.x * 100 + p.z));
        for (int i = 0; i < 4; i++)
            for (int k = 0; k < 3; k++)
            {
                if (rnd.NextDouble() < 0.3) continue;
                float s = 0.25f + (float)rnd.NextDouble() * 0.2f;
                bool bag = rnd.NextDouble() < 0.35;
                var pos = new Vector3(-0.8f + k * 0.8f, 0.32f + i * 0.6f + s / 2, 0);
                if (bag) Sph(g, "DuffelBag", pos, new Vector3(s * 2f, s, s * 1.2f), "Fabric");
                else Box(g, "Box", pos, new Vector3(s * 1.4f, s, s * 1.1f), rnd.NextDouble() < 0.5 ? "WoodLight" : "Cork", false);
            }
    }

    static void Sofa(Transform parent, Vector3 p, float yaw, float len)
    {
        var g = Group(parent, "Sofa"); g.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
        Box(g, "Base", new Vector3(0, 0.22f, 0), new Vector3(len, 0.3f, 0.9f), "Leather");
        Box(g, "Back", new Vector3(0, 0.6f, -0.38f), new Vector3(len, 0.6f, 0.18f), "Leather");
        Box(g, "Arm_L", new Vector3(-len / 2 + 0.1f, 0.45f, 0), new Vector3(0.2f, 0.4f, 0.9f), "Leather");
        Box(g, "Arm_R", new Vector3(len / 2 - 0.1f, 0.45f, 0), new Vector3(0.2f, 0.4f, 0.9f), "Leather");
        int n = Mathf.Max(2, Mathf.RoundToInt(len / 0.9f));
        float cw = (len - 0.45f) / n;
        for (int i = 0; i < n; i++) Box(g, "Cushion", new Vector3(-len / 2 + 0.22f + cw * (i + 0.5f), 0.43f, 0.05f), new Vector3(cw - 0.04f, 0.12f, 0.72f), "Leather", false);
        foreach (var (x, z) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
            Cyl(g, "Leg", new Vector3(x * (len / 2 - 0.1f), 0.035f, z * 0.35f), 0.03f, 0.07f, "WoodDark", false);
    }

    static void Bench(Transform parent, Vector3 p, float yaw)
    {
        var g = Group(parent, "Bench"); g.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
        Box(g, "Seat", new Vector3(0, 0.45f, 0), new Vector3(2.4f, 0.12f, 0.6f), "Leather");
        Box(g, "Leg_L", new Vector3(-1f, 0.2f, 0), new Vector3(0.08f, 0.4f, 0.5f), "Chrome");
        Box(g, "Leg_R", new Vector3(1f, 0.2f, 0), new Vector3(0.08f, 0.4f, 0.5f), "Chrome");
    }

    static void Plant(Transform parent, Vector3 p)
    {
        Cyl(parent, "Pot", p + Vector3.up * 0.3f, 0.28f, 0.6f, "Concrete");
        Sph(parent, "Foliage", p + Vector3.up * 1.0f, new Vector3(0.8f, 1.0f, 0.8f), "Foliage");
        Sph(parent, "Foliage", p + new Vector3(0.2f, 1.35f, 0.1f), new Vector3(0.5f, 0.6f, 0.5f), "Foliage");
    }

    static void ReceptionDesk(Transform parent, Vector3 p)
    {
        var g = Group(parent, "ReceptionDesk"); g.position = p;
        Box(g, "Front", new Vector3(0, 0.55f, -0.4f), new Vector3(3.2f, 1.1f, 0.2f), "WoodDark");
        Box(g, "Side", new Vector3(1.5f, 0.55f, 0.4f), new Vector3(0.2f, 1.1f, 1.6f), "WoodDark");
        Box(g, "Top", new Vector3(0, 1.12f, -0.4f), new Vector3(3.4f, 0.06f, 0.45f), "Marble");
        Box(g, "TopSide", new Vector3(1.5f, 1.12f, 0.4f), new Vector3(0.45f, 0.06f, 1.8f), "Marble");
        Box(g, "Inner", new Vector3(0, 0.75f, 0.1f), new Vector3(2.8f, 0.05f, 0.6f), "WoodLight");
        Box(g, "Monitor", new Vector3(-0.6f, 1.05f, 0.1f), new Vector3(0.5f, 0.32f, 0.03f), "Screen", false);
        Box(g, "Bell", new Vector3(0.6f, 1.17f, -0.4f), new Vector3(0.08f, 0.05f, 0.08f), "Brass", false);
        Text(g, "Sign", "РЕСЕПШН", 1.4f, new Color(1f, 0.8f, 0.25f), new Vector3(0, 0.7f, -0.515f), Quaternion.identity, new Vector2(2.4f, 0.3f));
    }

    static void Atm(Transform parent, Vector3 p, Vector3 facing)
    {
        var g = Group(parent, "ATM"); g.SetPositionAndRotation(p, Quaternion.LookRotation(facing)); // +Z → к игроку
        Box(g, "Body", new Vector3(0, 0.85f, 0), new Vector3(0.8f, 1.7f, 0.6f), "DarkMetal");
        Box(g, "Topper", new Vector3(0, 1.85f, 0.05f), new Vector3(0.8f, 0.3f, 0.5f), "VanStripe");
        Text(g, "Logo", "БАНКОМАТ", 1.2f, Color.white, new Vector3(0, 1.85f, 0.31f), Quaternion.Euler(0, 180, 0), new Vector2(0.75f, 0.2f));
        Box(g, "Screen", new Vector3(0, 1.3f, 0.31f), new Vector3(0.5f, 0.35f, 0.02f), "Screen", false, Quaternion.Euler(-10, 0, 0));
        Box(g, "Shelf", new Vector3(0, 0.95f, 0.4f), new Vector3(0.7f, 0.05f, 0.25f), "Metal");
        for (int r = 0; r < 4; r++) for (int c = 0; c < 3; c++)
            Box(g, "Key", new Vector3(-0.08f + c * 0.08f, 0.985f, 0.33f + r * 0.045f), new Vector3(0.06f, 0.015f, 0.035f), "Chrome", false);
        Box(g, "CardSlot", new Vector3(0.22f, 1.08f, 0.31f), new Vector3(0.12f, 0.02f, 0.02f), "Rubber", false);
        Box(g, "CashSlot", new Vector3(0, 0.75f, 0.31f), new Vector3(0.4f, 0.04f, 0.02f), "Rubber", false);
    }

    static void Desk(Transform parent, Vector3 p, float yaw)
    {
        var g = Group(parent, "Desk"); g.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
        Box(g, "Top", new Vector3(0, 0.75f, 0), new Vector3(1.6f, 0.05f, 0.8f), "WoodLight");
        Box(g, "Leg_L", new Vector3(-0.75f, 0.37f, 0), new Vector3(0.05f, 0.74f, 0.75f), "DarkMetal");
        Box(g, "Leg_R", new Vector3(0.75f, 0.37f, 0), new Vector3(0.05f, 0.74f, 0.75f), "DarkMetal");
        Box(g, "Drawers", new Vector3(0.5f, 0.45f, 0), new Vector3(0.45f, 0.55f, 0.7f), "WoodDark");
        Box(g, "Monitor", new Vector3(0, 1.05f, 0.25f), new Vector3(0.6f, 0.36f, 0.03f), "Screen", false);
        Box(g, "MonitorBack", new Vector3(0, 1.05f, 0.27f), new Vector3(0.64f, 0.4f, 0.02f), "DarkMetal", false);
        Box(g, "MonitorStand", new Vector3(0, 0.83f, 0.27f), new Vector3(0.05f, 0.12f, 0.05f), "DarkMetal", false);
        Box(g, "Keyboard", new Vector3(0, 0.785f, -0.1f), new Vector3(0.45f, 0.02f, 0.15f), "DarkMetal", false);
        // кресло
        Cyl(g, "ChairBase", new Vector3(0, 0.05f, -0.75f), 0.28f, 0.05f, "DarkMetal", false);
        Cyl(g, "ChairPost", new Vector3(0, 0.25f, -0.75f), 0.03f, 0.4f, "Chrome", false);
        Box(g, "ChairSeat", new Vector3(0, 0.47f, -0.75f), new Vector3(0.5f, 0.08f, 0.5f), "Fabric");
        Box(g, "ChairBack", new Vector3(0, 0.8f, -1.0f), new Vector3(0.48f, 0.55f, 0.07f), "Fabric");
    }

    static void WaterCooler(Transform parent, Vector3 p)
    {
        Box(parent, "CoolerBody", p + Vector3.up * 0.55f, new Vector3(0.4f, 1.1f, 0.4f), "VanBody");
        Cyl(parent, "CoolerBottle", p + Vector3.up * 1.32f, 0.15f, 0.45f, "Glass", false);
    }

    static void FilingCabinet(Transform parent, Vector3 p, Vector3 size)
    {
        Box(parent, "Cabinet", p + Vector3.up * size.y / 2, size, "DarkMetal");
        int drawers = Mathf.Max(1, Mathf.RoundToInt(size.y / 0.5f));
        for (int i = 0; i < drawers; i++)
        {
            float y = p.y + (i + 0.5f) * size.y / drawers;
            Box(parent, "Drawer", new Vector3(p.x, y, p.z - size.z / 2 - 0.005f), new Vector3(size.x * 0.9f, size.y / drawers - 0.05f, 0.01f), "Metal", false);
            Box(parent, "Handle", new Vector3(p.x, y + 0.05f, p.z - size.z / 2 - 0.02f), new Vector3(0.2f, 0.03f, 0.02f), "Chrome", false);
        }
    }

    static void Crate(Transform parent, Vector3 p, float s)
    {
        Box(parent, "Crate", p + Vector3.up * s / 2, Vector3.one * s, "WoodLight");
        foreach (float y in new[] { 0.08f, s - 0.08f })
            Box(parent, "CrateBand", p + Vector3.up * y, new Vector3(s + 0.02f, 0.08f, s + 0.02f), "WoodDark", false);
    }

    static void Plinth(Transform parent, Vector3 p, float h)
    {
        Box(parent, "Plinth", p + Vector3.up * h / 2, new Vector3(0.55f, h, 0.55f), "Marble");
        Box(parent, "PlinthTop", p + Vector3.up * (h - 0.02f), new Vector3(0.65f, 0.04f, 0.65f), "MarbleDark");
        Box(parent, "PlinthFoot", p + Vector3.up * 0.04f, new Vector3(0.65f, 0.08f, 0.65f), "MarbleDark");
    }

    static void GoldShelf(Transform parent, Vector3 p)
    {
        foreach (var (x, z) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
            Box(parent, "ShelfUpright", p + new Vector3(x * 0.75f, 1.0f, z * 0.3f), new Vector3(0.06f, 2.0f, 0.06f), "Brass");
        foreach (float y in new[] { 0.4f, 1.2f, 1.9f })
            Box(parent, "ShelfBoard", p + Vector3.up * y, new Vector3(1.6f, 0.05f, 0.7f), "DarkMetal");
    }

    static void Sculpture(Transform parent, Vector3 p)
    {
        Plinth(parent, p, 0.8f);
        Box(parent, "Sculpt_Cube", p + new Vector3(0, 1.0f, 0), Vector3.one * 0.3f, "Pad", false, Quaternion.Euler(35, 45, 0));
        Sph(parent, "Sculpt_Sphere", p + new Vector3(0, 1.32f, 0), Vector3.one * 0.25f, "Chrome");
        Cyl(parent, "Sculpt_Ring", p + new Vector3(0, 1.32f, 0), 0.22f, 0.02f, "GoldGlow", false, Quaternion.Euler(70, 0, 0));
    }

    static void SignPlate(Transform parent, string name, string text, Vector3 pos, Vector3 facing, float w, float h, Color color)
    {
        var g = Group(parent, name); g.SetPositionAndRotation(pos, Quaternion.LookRotation(-facing));
        Box(g, "Plate", new Vector3(0, 0, 0.01f), new Vector3(w, h, 0.03f), "DarkMetal", false);
        Text(g, "Text", text, h * 3.2f, color, new Vector3(0, 0, -0.01f), Quaternion.identity, new Vector2(w * 0.94f, h * 0.85f)).textWrappingMode = TextWrappingModes.NoWrap;
    }

    static void Van(Ctx ctx, Transform st, Transform dy, Vector3 p)
    {
        // Фургон стоит вдоль X, кабина на западе, задние двери (открыты) смотрят на восток — к пандусу
        var g = Group(st, "Van"); g.position = p;
        const float L = 4.2f, W = 2.2f, y0 = 0.5f, H = 1.95f, t = 0.06f; // кузов: x[-1.6, 2.6]
        float bx0 = -1.6f, bx1 = bx0 + L;
        Box(g, "Cargo_Floor", new Vector3((bx0 + bx1) / 2, y0, 0), new Vector3(L, 0.1f, W), "DarkMetal");
        Box(g, "Cargo_Roof", new Vector3((bx0 + bx1) / 2, y0 + H, 0), new Vector3(L, t, W), "VanBody");
        Box(g, "Cargo_SideS", new Vector3((bx0 + bx1) / 2, y0 + H / 2, -W / 2), new Vector3(L, H, t), "VanBody");
        Box(g, "Cargo_SideN", new Vector3((bx0 + bx1) / 2, y0 + H / 2, W / 2), new Vector3(L, H, t), "VanBody");
        Box(g, "Cargo_Front", new Vector3(bx0, y0 + H / 2, 0), new Vector3(t, H, W), "VanBody");
        Box(g, "Stripe_S", new Vector3((bx0 + bx1) / 2, y0 + 0.6f, -W / 2 - 0.01f), new Vector3(L, 0.25f, 0.01f), "VanStripe", false);
        Box(g, "Stripe_N", new Vector3((bx0 + bx1) / 2, y0 + 0.6f, W / 2 + 0.01f), new Vector3(L, 0.25f, 0.01f), "VanStripe", false);
        // кабина
        Box(g, "Cab", new Vector3(bx0 - 0.85f, y0 + 0.65f, 0), new Vector3(1.7f, 1.3f, W), "VanBody");
        Box(g, "Hood", new Vector3(bx0 - 1.55f, y0 + 0.35f, 0), new Vector3(0.5f, 0.7f, W - 0.1f), "VanBody");
        Box(g, "Windshield", new Vector3(bx0 - 1.32f, y0 + 1.05f, 0), new Vector3(0.05f, 0.55f, W - 0.3f), "Glass", false, Quaternion.Euler(0, 0, -25));
        Box(g, "Window_S", new Vector3(bx0 - 0.7f, y0 + 1.0f, -W / 2 - 0.005f), new Vector3(0.9f, 0.45f, 0.01f), "Glass", false);
        Box(g, "Window_N", new Vector3(bx0 - 0.7f, y0 + 1.0f, W / 2 + 0.005f), new Vector3(0.9f, 0.45f, 0.01f), "Glass", false);
        Box(g, "Bumper", new Vector3(bx0 - 1.83f, y0 + 0.05f, 0), new Vector3(0.1f, 0.2f, W), "Chrome", false);
        foreach (float z in new[] { -0.7f, 0.7f }) Sph(g, "Headlight", new Vector3(bx0 - 1.8f, y0 + 0.4f, z), new Vector3(0.05f, 0.18f, 0.25f), "LampCold");
        foreach (float x in new[] { bx0 - 1.0f, bx1 - 0.8f })
            foreach (float z in new[] { -W / 2 + 0.05f, W / 2 - 0.05f })
            {
                Cyl(g, "Wheel", new Vector3(x, 0.4f, z), 0.4f, 0.3f, "Rubber", true, Quaternion.Euler(90, 0, 0));
                Cyl(g, "Hubcap", new Vector3(x, 0.4f, z + Mathf.Sign(z) * 0.16f), 0.2f, 0.02f, "Chrome", false, Quaternion.Euler(90, 0, 0));
            }
        // открытые задние двери
        foreach (var (hz, sign) in new[] { (-W / 2, 1f), (W / 2, -1f) })
        {
            var hinge = Group(g, "DoorHinge");
            hinge.localPosition = new Vector3(bx1, y0 + H / 2, hz);
            hinge.localRotation = Quaternion.Euler(0, sign > 0 ? 100f : -100f, 0); // распахнуты наружу
            Box(hinge, "Door", new Vector3(0, 0, sign * W / 4), new Vector3(0.05f, H, W / 2), "VanBody");
            Box(hinge, "DoorWindow", new Vector3(0.03f, 0.45f, sign * W / 4), new Vector3(0.01f, 0.4f, W / 2 - 0.25f), "Glass", false);
        }
        // неон в кузове + знак
        Box(g, "NeonStrip_S", new Vector3((bx0 + bx1) / 2, y0 + H - 0.1f, -W / 2 + 0.05f), new Vector3(L - 0.2f, 0.04f, 0.04f), "Neon", false);
        Box(g, "NeonStrip_N", new Vector3((bx0 + bx1) / 2, y0 + H - 0.1f, W / 2 - 0.05f), new Vector3(L - 0.2f, 0.04f, 0.04f), "Neon", false);
        Text(g, "Sign_Side", "ЭВАКУАЦИЯ ЛУТА", 2f, new Color(0.3f, 1f, 0.2f), new Vector3((bx0 + bx1) / 2, y0 + 1.3f, -W / 2 - 0.03f), Facing(Vector3.back), new Vector2(3.6f, 0.4f));
        Text(g, "Sign_Back", "↓ ЛУТ СЮДА ↓", 1.6f, new Color(0.3f, 1f, 0.2f), new Vector3(bx1 + 0.05f, y0 + H + 0.25f, 0), Facing(Vector3.right), new Vector2(2f, 0.3f));
        Box(g, "Floor_Marker", new Vector3(bx1 + 1.4f, 0.006f, 0), new Vector3(2.2f, 0.01f, W), "Hazard", false);
        PointLight(g, "VanLight", new Vector3((bx0 + bx1) / 2, y0 + H - 0.4f, 0), new Color(0.3f, 1f, 0.3f), 2f, 5f);

        // Триггер эвакуации (кузов)
        var evac = new GameObject("EvacZone_VanCargo");
        evac.transform.SetParent(dy, false);
        evac.transform.position = g.TransformPoint(new Vector3((bx0 + bx1) / 2, y0 + H / 2, 0));
        evac.layer = IgnoreRaycast;
        var bc = evac.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(L - 0.2f, H - 0.2f, W - 0.2f);
        evac.AddComponent<AudioSource>();
        var ez = evac.AddComponent<EvacuationZone>();
        ez.depositClips = Clips(UiDir, "confirmation");
        ez.coinClips = Clips(ImpactDir, "impact_metal_light", "impact_plate_light");
        ez.heavyClips = Clips(ImpactDir, "impact_metal_heavy", "impact_bell_heavy");
    }

    // =====================================================================================
    // СВЕТ / ТУМАН
    // =====================================================================================
    static void SetupLighting(UnityEngine.SceneManagement.Scene scene)
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.014f;
        RenderSettings.fogColor = new Color(0.10f, 0.11f, 0.14f);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.34f, 0.36f, 0.42f);
        RenderSettings.ambientEquatorColor = new Color(0.24f, 0.23f, 0.23f);
        RenderSettings.ambientGroundColor = new Color(0.11f, 0.10f, 0.09f);
        var sun = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)).FirstOrDefault(l => l.type == LightType.Directional);
        if (sun)
        {
            // Внутри здания (с потолками) солнце только слегка подсвечивает — без теней, дёшево для VR
            sun.intensity = 0.35f; sun.color = new Color(0.75f, 0.82f, 1f); sun.shadows = LightShadows.None;
            EditorUtility.SetDirty(sun);
        }
    }
}
