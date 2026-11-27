using System.Linq;
using Fusion;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Улица перед Банком и кинематографичный старт (часть BankVisualOverhaul):
///  • тестовое Убежище-шлюз (Z1_Hub_Bunker) прячется — на его месте ночная улица: тротуар, бордюр, асфальт с разметкой,
///    дома напротив с горящими окнами и неоном, фонари, гидранты, урны, газетные автоматы (Tripo);
///  • классический фасад Банка: облицовка, цоколь, карнизы, окна, портик с 6 колоннами (по ним можно залезть),
///    парадная лестница, распахнутые двери и золотая неоновая вывеска GRABZ BANK (3D TextMeshPro);
///  • фургон грабителей GetawayVan_Arrival у ступенек: открытый кузов, фары, двигатель, сетевой
///    NetworkHeistStartTrigger (Master Client Object). Игроки появляются в кузове;
///  • ночное небо, туман, лунный свет.
/// </summary>
public static partial class BankVisualOverhaul
{
    const float SidewalkY = -0.9f, RoadY = -1.05f;
    const string ImpactDir = "Assets/Audio/Kenney_ImpactSounds";
    const string UiDir = "Assets/Audio/Kenney_InterfaceSounds";

    static Material pavement, brick, windowWarm, windowCool, windowDark, vanPaint, vanGlass, headlightMat, tailMat, rubber, chromeMat,
                    paintWhite, lampBulb, neonGold, neonPink, neonCyan, nightSky,
                    stuccoWhite, stuccoPink, stuccoTeal, stuccoMint, stuccoPeach, marbleGold, marbleTeal, sunsetGlass, sunsetGlassUnlit,
                    neonSun, lightShaft, serviceWall, breakTile, sunsetSky;

    static void BuildExteriorMaterials()
    {
        var (pB, pN) = BankRetroTextures.Pavement();
        pavement = Lit("Pavement", pB, pN, new Color(1f, 0.9f, 0.86f), 2f);      // тёплая плитка Майами
        // Майами: штукатурка Ар-деко пастельных цветов, мрамор с золотом, закатное стекло
        var pl = (Texture2D)plaster.GetTexture("_BaseMap"); var pln = (Texture2D)plaster.GetTexture("_BumpMap");
        stuccoWhite = Lit("StuccoWhite", pl, pln, new Color(1.12f, 1.1f, 1.06f), 2.5f);
        stuccoPink = Lit("StuccoPink", pl, pln, new Color(1.15f, 0.78f, 0.84f), 2.5f);
        stuccoTeal = Lit("StuccoTeal", pl, pln, new Color(0.55f, 0.95f, 0.95f), 2.5f);
        stuccoMint = Lit("StuccoMint", pl, pln, new Color(0.75f, 1.08f, 0.88f), 2.5f);
        stuccoPeach = Lit("StuccoPeach", pl, pln, new Color(1.16f, 0.9f, 0.7f), 2.5f);
        serviceWall = Lit("ServiceWall", pl, pln, new Color(0.88f, 1.0f, 0.95f), 2.5f);
        var (mgB, mgN) = BankRetroTextures.MarbleGold();
        marbleGold = Lit("MarbleGold", mgB, mgN, Color.white, 1.6f);
        var (mtB, mtN) = BankRetroTextures.MarbleTeal();
        marbleTeal = Lit("MarbleTeal", mtB, mtN, Color.white, 1.4f);
        breakTile = Lit("BreakTile", (Texture2D)tread.GetTexture("_BaseMap"), (Texture2D)tread.GetTexture("_BumpMap"), new Color(1.1f, 0.85f, 0.8f), 1.0f);
        var (sgB, _) = BankRetroTextures.Sunset();
        sunsetGlass = GetMat("SunsetGlass", "Universal Render Pipeline/Lit");
        sunsetGlass.SetTexture("_BaseMap", sgB); sunsetGlass.SetColor("_BaseColor", new Color(0.9f, 0.9f, 0.95f));
        TripoAssetPostprocessor.MakeMatte(sunsetGlass);
        sunsetGlass.EnableKeyword("_EMISSION"); sunsetGlass.SetTexture("_EmissionMap", sgB); sunsetGlass.SetColor("_EmissionColor", Color.white * 0.45f);
        EditorUtility.SetDirty(sunsetGlass);
        sunsetGlassUnlit = GetMat("SunsetGlassUnlit", "Universal Render Pipeline/Unlit");
        sunsetGlassUnlit.SetTexture("_BaseMap", sgB); sunsetGlassUnlit.SetColor("_BaseColor", new Color(1.25f, 1.15f, 1.1f));
        EditorUtility.SetDirty(sunsetGlassUnlit);
        neonSun = Emissive("NeonSun", new Color(1f, 0.45f, 0.35f), 3.2f);
        lightShaft = GetMat("LightShaft", "Universal Render Pipeline/Unlit");     // аддитивные лучи заката из окон
        lightShaft.SetColor("_BaseColor", new Color(0.09f, 0.05f, 0.045f, 1f));
        lightShaft.SetFloat("_Surface", 1f); lightShaft.SetFloat("_Blend", 2f);
        lightShaft.SetFloat("_SrcBlend", (float)BlendMode.One); lightShaft.SetFloat("_DstBlend", (float)BlendMode.One);
        lightShaft.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); lightShaft.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        lightShaft.SetFloat("_ZWrite", 0f); lightShaft.SetFloat("_Cull", (float)CullMode.Off);
        lightShaft.SetOverrideTag("RenderType", "Transparent"); lightShaft.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        lightShaft.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(lightShaft);
        sunsetSky = GetMat("SunsetSky", "Skybox/Panoramic");                   // закат Майами: своя панорама-градиент
        sunsetSky.SetTexture("_MainTex", BankRetroTextures.SunsetPanorama());
        sunsetSky.SetFloat("_Mapping", 1f); sunsetSky.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT"); sunsetSky.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
        sunsetSky.SetFloat("_ImageType", 0f);
        sunsetSky.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f));
        sunsetSky.SetFloat("_Exposure", 1.0f);
        EditorUtility.SetDirty(sunsetSky);
        var hb = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/Hub/T_Hub_Brick.png");
        var hn = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/Hub/T_Hub_Brick_N.png");
        brick = Lit("BrickNight", hb, hn, new Color(0.72f, 0.66f, 0.64f), 1.6f);
        vanPaint = Lit("VanPaint", (Texture2D)armor.GetTexture("_BaseMap"), (Texture2D)armor.GetTexture("_BumpMap"), new Color(0.3f, 0.34f, 0.48f), 1.5f);
        windowWarm = Emissive("WindowWarm", new Color(1f, 0.72f, 0.38f), 1.8f);
        windowCool = Emissive("WindowCool", new Color(0.55f, 0.72f, 1f), 1.3f);
        windowDark = Emissive("WindowDark", new Color(0.04f, 0.05f, 0.08f), 0f);
        vanGlass = Emissive("VanGlass", new Color(0.08f, 0.12f, 0.18f), 0.3f);
        headlightMat = Emissive("Headlight", new Color(1f, 0.95f, 0.8f), 5f);
        tailMat = Emissive("Taillight", new Color(1f, 0.08f, 0.05f), 4f);
        lampBulb = Emissive("LampBulb", new Color(1f, 0.78f, 0.45f), 4f);
        rubber = Emissive("Rubber", new Color(0.06f, 0.06f, 0.06f), 0f);
        chromeMat = Emissive("Chrome", new Color(0.6f, 0.62f, 0.66f), 0f);
        paintWhite = Emissive("PaintWhite", new Color(0.82f, 0.82f, 0.78f), 0f);
        neonGold = TmpNeon("NeonGold", new Color(1f, 0.7f, 0.2f) * 3.2f);
        neonPink = TmpNeon("NeonPink", new Color(1f, 0.2f, 0.6f) * 3f);
        neonCyan = TmpNeon("NeonCyan", new Color(0.2f, 0.9f, 1f) * 3f);
        neonCyanMesh = Emissive("NeonCyanTube", new Color(0.2f, 0.95f, 1f), 3.2f);

        nightSky = GetMat("NightSky", "Skybox/Procedural");
        nightSky.SetFloat("_SunDisk", 0f);
        nightSky.EnableKeyword("_SUNDISK_NONE"); nightSky.DisableKeyword("_SUNDISK_SIMPLE"); nightSky.DisableKeyword("_SUNDISK_HIGH_QUALITY");
        nightSky.SetFloat("_AtmosphereThickness", 0.55f);
        nightSky.SetColor("_SkyTint", new Color(0.16f, 0.2f, 0.42f));
        nightSky.SetColor("_GroundColor", new Color(0.03f, 0.03f, 0.05f));
        nightSky.SetFloat("_Exposure", 0.32f);
        EditorUtility.SetDirty(nightSky);
    }

    /// Копия материала шрифта TMP с HDR-цветом лица: буквы ярче 1 → светятся в Bloom (неон).
    static Material TmpNeon(string name, Color hdr)
    {
        string path = $"{MatDir}/M_Bank_{name}.mat";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MapKit.FontPath);
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(font.material); AssetDatabase.CreateAsset(m, path); }
        else { m.shader = font.material.shader; m.CopyPropertiesFromMaterial(font.material); }
        m.SetColor("_FaceColor", hdr);
        EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject Cyl(Transform p, string name, Vector3 pos, float radius, float height, Material mat, bool collider, Quaternion? rot = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p, false);
        go.transform.SetPositionAndRotation(pos, rot ?? Quaternion.identity);
        var s = new Vector3(radius * 2, height / 2, radius * 2);
        go.transform.localScale = s;
        go.AddComponent<MeshFilter>().sharedMesh = tileOf.TryGetValue(mat, out var tile) ? CylMesh(s, tile) : Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        if (collider) { var c = go.AddComponent<CapsuleCollider>(); c.direction = 1; c.height = 2f; c.radius = 0.5f; }
        return go;
    }

    static GameObject Ball(Transform p, string name, Vector3 pos, Vector3 size, Material mat)
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

    // =========================================================================== главная точка
    static void BuildExterior(Transform root, Transform st, Transform map, UnityEngine.SceneManagement.Scene scene)
    {
        // тестовое Убежище со шлюзом больше не нужно: на его месте улица
        var bunker = map.Find("Z1_Hub_Bunker");
        if (bunker && bunker.gameObject.activeSelf) { bunker.gameObject.SetActive(false); EditorUtility.SetDirty(bunker.gameObject); }
        log.AppendLine("  hid test bunker / airlock (Z1_Hub_Bunker)");
        var gm = Object.FindAnyObjectByType<HeistGameManager>(FindObjectsInactive.Include);
        if (gm) { gm.airlock = null; EditorUtility.SetDirty(gm); }

        var ex = Group(st, "Exterior");
        BuildFacade(ex);
        BuildStreet(ex);
        BuildCityBlocks(ex);
        BuildStreetProps(ex, root);
        var (spawn, exit) = BuildArrivalVan(root);
        PlacePlayers(scene, spawn);

        var dbg = Object.FindAnyObjectByType<HeistDebugActions>(FindObjectsInactive.Include);
        if (dbg) { dbg.hubPoint = exit; EditorUtility.SetDirty(dbg); }
        foreach (var t in map.GetComponentsInChildren<TextMeshPro>(true).Where(t => t.text == "ТЕЛЕПОРТ В ХАБ"))
        { t.text = "ТЕЛЕПОРТ НА УЛИЦУ"; EditorUtility.SetDirty(t); }
        foreach (var sb in Object.FindObjectsByType<Scoreboard>(FindObjectsInactive.Include))
        { sb.lobbyHint = "ВЫЙДИ ИЗ ФУРГОНА — СТАРТ!"; EditorUtility.SetDirty(sb); }

        // закат Майами: розово-фиолетовое небо, низкое тёплое солнце с юго-запада (светит на фасад), тёплый туман
        RenderSettings.skybox = sunsetSky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.72f, 0.58f, 0.74f);
        RenderSettings.ambientEquatorColor = new Color(0.68f, 0.52f, 0.5f);
        RenderSettings.ambientGroundColor = new Color(0.26f, 0.18f, 0.2f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.01f;
        RenderSettings.fogColor = new Color(0.92f, 0.62f, 0.66f);
        var sun = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)).FirstOrDefault(l => l.type == LightType.Directional);
        if (sun)
        {
            sun.color = new Color(1f, 0.82f, 0.7f); sun.intensity = 1.0f; sun.shadows = LightShadows.None;
            sun.transform.rotation = Quaternion.Euler(12f, 28f, 0f);
            RenderSettings.sun = sun;
            EditorUtility.SetDirty(sun); EditorUtility.SetDirty(sun.transform);
        }
        log.AppendLine("  exterior: street, facade, city blocks, arrival van, night sky");
    }

    // =========================================================================== фасад
    static void FacadeWindow(Transform p, float x, float y, float w, float h, Material pane)
    {
        const float z = -0.17f;
        Box(p, "Window", new Vector3(x, y + h / 2, z - 0.01f), new Vector3(w, h, 0.02f), pane);
        Box(p, "WindowFrame_T", new Vector3(x, y + h + 0.08f, z - 0.05f), new Vector3(w + 0.3f, 0.16f, 0.12f), marbleDark, true);
        Box(p, "WindowSill", new Vector3(x, y - 0.06f, z - 0.08f), new Vector3(w + 0.3f, 0.12f, 0.18f), marbleDark, true);
        Box(p, "WindowMullion", new Vector3(x, y + h / 2, z - 0.025f), new Vector3(0.06f, h, 0.04f), marbleDark);
    }

    static void BuildFacade(Transform ex)
    {
        var f = Group(ex, "Facade");
        const float z = -0.155f, T = 0.01f;
        Panel(f, "Facade_Offices", false, z, -34, -14, 0, 9.2f, T, stuccoPink);
        Panel(f, "Facade_Hall", false, z, -14, 14, 0, 10.2f, T, stuccoWhite, (-1.5f, 1.5f, 0f, 3f));
        Panel(f, "Facade_Gallery", false, z, 14, 34, 0, 6.2f, T, stuccoMint);
        Box(f, "Plinth", new Vector3(0, -0.45f, -0.3f), new Vector3(68.4f, 0.9f, 0.3f), marbleTeal, true);
        Box(f, "Cornice_Hall", new Vector3(0, 10.4f, -0.35f), new Vector3(28.4f, 0.4f, 0.5f), stuccoTeal, true);
        Box(f, "Cornice_Offices", new Vector3(-24, 9.35f, -0.3f), new Vector3(20.2f, 0.3f, 0.4f), stuccoWhite, true);
        Box(f, "Cornice_Gallery", new Vector3(24, 6.35f, -0.3f), new Vector3(20.2f, 0.3f, 0.4f), stuccoWhite, true);
        Box(f, "Band_Offices", new Vector3(-24, 4.4f, -0.22f), new Vector3(20f, 0.2f, 0.14f), stuccoWhite, true);
        // золотые вертикальные рёбра Ар-деко на углах секций фасада
        foreach (float x in new[] { -33.4f, -14.6f, 14.6f, 33.4f })
            Box(f, "DecoFin", new Vector3(x, 4.6f, -0.3f), new Vector3(0.3f, 9.2f, 0.3f), brass, true);

        // окна: офисы (2 ряда), галерея, высокие окна Холла за колоннами
        int k = 0;
        Material Pane() { k++; return sunsetGlass; }
        foreach (float x in new[] { -31f, -27f, -23f, -19f })
        {
            FacadeWindow(f, x, 1.2f, 1.6f, 2.2f, Pane());
            FacadeWindow(f, x, 5.4f, 1.6f, 2.2f, Pane());
        }
        foreach (float x in new[] { 17.5f, 21.5f, 25.5f, 29.5f }) FacadeWindow(f, x, 1.4f, 1.8f, 2.6f, sunsetGlass);
        foreach (float x in new[] { -9.4f, -5.3f, 5.3f, 9.4f }) FacadeWindow(f, x, 4.0f, 1.8f, 4.0f, sunsetGlass);   // панорамные окна Холла
        Box(f, "Transom", new Vector3(0, 3.45f, -0.17f), new Vector3(3f, 0.7f, 0.02f), sunsetGlass);

        // портик: площадка, 5 ступеней, 6 колонн с кольцами-зацепами, антаблемент, аттик
        Box(f, "Portico", new Vector3(0, -0.45f, -2.2f), new Vector3(26f, 0.9f, 4.1f), marbleGold, true);
        for (int i = 0; i < 5; i++)
        {
            float top = -0.15f * (i + 1), z0 = -4.25f - 0.5f * (i + 1);
            Box(f, "Step", new Vector3(0, (top + SidewalkY) / 2, z0 + 0.25f), new Vector3(26f + 0.4f * (i + 1), top - SidewalkY, 0.5f), stuccoWhite, true);
        }
        foreach (float x in new[] { -11.6f, -7.4f, -3.2f, 3.2f, 7.4f, 11.6f })
        {
            var c = new Vector3(x, 0, -2.6f);
            Cyl(f, "ColumnBase", c + Vector3.up * 0.2f, 0.75f, 0.4f, marbleTeal, true);
            Cyl(f, "ColumnShaft", c + Vector3.up * 4.9f, 0.48f, 9.0f, stuccoWhite, true);
            Box(f, "ColumnCapital", c + Vector3.up * 9.65f, new Vector3(1.3f, 0.5f, 1.3f), brass, true);
            foreach (float y in new[] { 2.6f, 5.0f, 7.4f }) Cyl(f, "ColumnRing", c + Vector3.up * y, 0.56f, 0.12f, brass, true);
        }
        Box(f, "Entablature", new Vector3(0, 10.25f, -1.8f), new Vector3(26.4f, 0.7f, 3.3f), stuccoWhite, true);
        Box(f, "EntablatureStripe", new Vector3(0, 9.95f, -3.46f), new Vector3(26.4f, 0.1f, 0.02f), stuccoTeal);
        Box(f, "Attic", new Vector3(0, 10.95f, -1.6f), new Vector3(24f, 0.7f, 2.9f), stuccoPink, true);
        Box(f, "Pediment", new Vector3(0, 11.6f, -1.4f), new Vector3(12f, 0.6f, 2.4f), stuccoWhite, true);

        // неоновая вывеска SUNSET TRUST BANK (3D TextMeshPro + HDR-материал → Bloom) и логотип: ретро-солнце с пальмой
        Box(f, "SignBoard", new Vector3(0, 10.25f, -3.47f), new Vector3(13f, 0.62f, 0.02f), windowDark);
        var sign = Label(f, "Sign_SunsetTrust", "SUNSET TRUST BANK", 5.2f, Color.white, new Vector3(0, 10.25f, -3.49f), Vector3.back, new Vector2(13f, 0.8f));
        sign.fontSharedMaterial = neonPink; sign.characterSpacing = 10f;
        var sub = Label(f, "Sign_Since", "MIAMI · EST. 1923", 1.8f, Color.white, new Vector3(0, 10.95f, -3.07f), Vector3.back, new Vector2(10f, 0.3f));
        sub.fontSharedMaterial = neonCyan;
        Logo(f, new Vector3(0, 12.75f, -2.62f), 1.0f);
        var enter = Label(f, "Sign_Enter", "ВХОД", 2.2f, Color.white, new Vector3(0, 3.45f, -0.2f), Vector3.back, new Vector2(2.5f, 0.4f));
        enter.fontSharedMaterial = neonCyan;

        // распахнутые парадные двери у проёма
        foreach (var sx in new[] { -1f, 1f })
        {
            Box(f, "DoorLeaf", new Vector3(sx * 2.25f, 1.5f, -0.22f), new Vector3(1.5f, 3f, 0.08f), woodPanel, true);
            Box(f, "DoorHandle", new Vector3(sx * 1.65f, 1.2f, -0.28f), new Vector3(0.05f, 0.5f, 0.05f), brass);
        }
    }

    /// Логотип банка из примитивов: ретро-солнце (диск с тёмными прорезями) и бирюзовая неоновая пальма. Лицом к −Z.
    static void Logo(Transform p, Vector3 c, float r)
    {
        var g = Group(p, "SunsetLogo");
        Cyl(g, "Sun", c, r, 0.06f, neonSun, false, Quaternion.Euler(90, 0, 0));
        for (int i = 0; i < 3; i++)
            Box(g, "SunStripe", c + new Vector3(0, -r * (0.25f + 0.22f * i), -0.04f), new Vector3(r * 2.1f, r * 0.07f * (i + 1), 0.02f), windowDark);
        var trunkBase = c + new Vector3(r * 0.45f, -r * 1.05f, -0.08f);
        var top = trunkBase + new Vector3(-r * 0.25f, r * 1.75f, 0);
        var mid = (trunkBase + top) / 2 + new Vector3(r * 0.12f, 0, 0);
        foreach (var (a, b) in new[] { (trunkBase, mid), (mid, top) })
            Box(g, "PalmTrunk", (a + b) / 2, new Vector3(r * 0.09f, (b - a).magnitude, 0.03f), neonCyanMesh, false, Quaternion.FromToRotation(Vector3.up, (b - a).normalized));
        foreach (float ang in new[] { -150f, -110f, -60f, -20f, 20f, 60f })
        {
            var dir = Quaternion.Euler(0, 0, ang) * Vector3.right;
            Box(g, "PalmFrond", top + dir * r * 0.4f, new Vector3(r * 0.8f, r * 0.08f, 0.03f), neonCyanMesh, false, Quaternion.Euler(0, 0, ang));
        }
    }

    static Material neonCyanMesh;

    // =========================================================================== улица
    static void BuildStreet(Transform ex)
    {
        var s = Group(ex, "Street");
        Box(s, "Sidewalk", new Vector3(0, SidewalkY - 0.075f, -5.175f), new Vector3(104f, 0.15f, 10.05f), pavement, true);
        Box(s, "Curb", new Vector3(0, -0.965f, -10.3f), new Vector3(104f, 0.17f, 0.2f), concrete, true);
        Box(s, "Road", new Vector3(0, RoadY - 0.1f, -15.75f), new Vector3(104f, 0.2f, 10.7f), asphalt, true);
        Box(s, "Curb_Far", new Vector3(-7.5f, -0.965f, -21.2f), new Vector3(89f, 0.17f, 0.2f), concrete, true);
        Box(s, "Sidewalk_Far", new Vector3(-7.5f, SidewalkY - 0.075f, -22.75f), new Vector3(89f, 0.15f, 2.9f), pavement, true);
        Box(s, "Road_Side", new Vector3(41.5f, RoadY - 0.1f, -30.6f), new Vector3(9f, 0.2f, 19f), asphalt, true);

        const float my = RoadY + 0.006f;
        for (float x = -50f; x < 50f; x += 3.2f) Box(s, "Lane_Dash", new Vector3(x, my, -15.75f), new Vector3(1.6f, 0.01f, 0.15f), paint);
        for (float x = -26f; x <= -19f; x += 1.1f) Box(s, "Zebra", new Vector3(x, my, -15.75f), new Vector3(0.55f, 0.01f, 10f), paintWhite);
        Box(s, "StopLine", new Vector3(-17.8f, my, -13f), new Vector3(0.3f, 0.01f, 5f), paintWhite);

        // невидимая граница в конце бокового переулка (фургон проезжает сквозь — она не мешает анимации)
        var wall = new GameObject("Bound_SideStreet");
        wall.transform.SetParent(s, false);
        wall.transform.position = new Vector3(41.5f, 3f, -27f);
        wall.layer = LayerMask.NameToLayer("Ignore Raycast");                     // не цепляется руками, но не пускает
        wall.AddComponent<BoxCollider>().size = new Vector3(9f, 8f, 0.4f);
    }

    // =========================================================================== дома вокруг
    /// Кирпичный дом: коробка от тротуара вверх + сетка окон на фасаде, обращённом к улице (normalZ = ±1).
    /// Отель / дом в стиле Ар-деко Майами: пастельная штукатурка, белые «брови» над рядами окон, золотое ребро по центру,
    /// закатное стекло в окнах и неоновая вывеска над первым этажом (normalZ = ±1 — фасад к улице).
    static void Building(Transform p, string name, float x0, float x1, float z0, float z1, float h, float normalZ, int seed, string neon = null, Material neonMat = null, Material wall = null)
    {
        var g = Group(p, name);
        wall ??= stuccoWhite;
        Box(g, "Block", new Vector3((x0 + x1) / 2, SidewalkY + h / 2, (z0 + z1) / 2), new Vector3(x1 - x0, h, z1 - z0), wall, true);
        Box(g, "Roof", new Vector3((x0 + x1) / 2, SidewalkY + h + 0.2f, (z0 + z1) / 2), new Vector3(x1 - x0 + 0.4f, 0.4f, z1 - z0 + 0.4f), stuccoWhite, true);
        Box(g, "RoofTier", new Vector3((x0 + x1) / 2, SidewalkY + h + 0.9f, (z0 + z1) / 2), new Vector3((x1 - x0) * 0.4f, 1.0f, (z1 - z0) * 0.6f), wall, true);
        if (normalZ == 0) return;
        float fz = (normalZ > 0 ? z1 : z0) + normalZ * 0.02f;
        Box(g, "DecoFin", new Vector3((x0 + x1) / 2, SidewalkY + h / 2 + 1.5f, fz + normalZ * 0.2f), new Vector3(0.5f, h - 3f, 0.4f), brass, true);
        for (float y = SidewalkY + 1.4f; y < SidewalkY + h - 1.8f; y += 3.2f)
            if (y > SidewalkY + 2f) Box(g, "Eyebrow", new Vector3((x0 + x1) / 2, y + 1.65f, fz + normalZ * 0.25f), new Vector3(x1 - x0 - 0.6f, 0.12f, 0.5f), stuccoWhite, true);
        for (float x = x0 + 1.6f; x < x1 - 1.2f; x += 3f)
            for (float y = SidewalkY + 1.4f; y < SidewalkY + h - 1.8f; y += 3.2f)
            {
                if (Mathf.Abs(x - (x0 + x1) / 2) < 0.8f) continue;               // место под золотое ребро
                bool shop = y < SidewalkY + 2f;
                Box(g, shop ? "ShopWindow" : "Window", new Vector3(x, y + (shop ? 0.9f : 0.75f), fz), new Vector3(shop ? 2.2f : 1.1f, shop ? 1.8f : 1.5f, 0.02f), shop ? windowWarm : sunsetGlass);
            }
        if (neon != null)
        {
            var t = Label(g, "Neon", neon, 3.2f, Color.white, new Vector3((x0 + x1) / 2, SidewalkY + 3.6f, fz + normalZ * 0.03f), new Vector3(0, 0, normalZ), new Vector2(x1 - x0 - 2f, 0.5f));
            t.fontSharedMaterial = neonMat;
        }
    }

    static void BuildCityBlocks(Transform ex)
    {
        var c = Group(ex, "CityBlocks");
        // по сторонам от Банка (закрывают его боковые стены)
        Building(c, "Bldg_W", -52f, -34.3f, -0.45f, 14f, 13f, -1, 3, "PALM CAFE", neonCyan, stuccoPeach);
        Building(c, "Bldg_E", 34.3f, 52f, -0.45f, 14f, 11f, -1, 7, "FLAMINGO", neonPink, stuccoTeal);
        // напротив, через дорогу
        Building(c, "Bldg_S1", -52f, -28f, -36f, -24.2f, 12f, 1, 11, "OCEAN HOTEL", neonCyan, stuccoPink);
        Building(c, "Bldg_S2", -28f, -6f, -36f, -24.2f, 9f, 1, 13, "VICE MOTEL", neonPink, stuccoMint);
        Building(c, "Bldg_S3", -6f, 16f, -36f, -24.2f, 14f, 1, 17, "SUNRISE HOTEL", neonPink, stuccoWhite);
        Building(c, "Bldg_S4", 16f, 37f, -36f, -24.2f, 10f, 1, 19, "BEACH CLUB", neonCyan, stuccoPeach);
        // концы улицы
        Building(c, "Bldg_EndW", -58f, -52f, -36f, 14f, 12f, 0, 23, null, null, stuccoMint);
        Building(c, "Bldg_EndE", 52f, 58f, -21.1f, 14f, 12f, 0, 29, null, null, stuccoPink);
        Building(c, "Bldg_Corner", 46f, 58f, -40f, -21.3f, 10f, 0, 31, null, null, stuccoTeal);
    }

    // =========================================================================== уличные мелочи (Tripo) и фонари
    static void BuildStreetProps(Transform ex, Transform root)
    {
        var g = Group(ex, "StreetProps");
        var lights = Group(root, "StreetLights");
        // закат: фонари ещё не горят (без источников света — экономия для Quest)
        foreach (float x in new[] { -24f, -12f, 12f, 24f }) Place(g, "Bank_StreetLamp", new Vector3(x, SidewalkY, -9.7f), 0f, 4.8f);
        foreach (float x in new[] { -30f, -6f, 18f }) Place(g, "Bank_StreetLamp", new Vector3(x, SidewalkY, -21.8f), 180f, 4.8f);
        // пальмы в кадках вдоль тротуаров и у входа
        if (Prefab("Bank_PalmPlanter"))
        {
            foreach (float x in new[] { -40f, -32f, -18f, 18f, 30f, 40f }) Place(g, "Bank_PalmPlanter", new Vector3(x, SidewalkY, -9.4f), x * 7f, 4.6f);
            foreach (float x in new[] { -44f, -36f, -26f, -14f, -2f, 10f, 22f, 33f }) Place(g, "Bank_PalmPlanter", new Vector3(x, SidewalkY, -22.6f), x * 5f, 4.2f);
            foreach (float x in new[] { -15f, 15f }) Place(g, "Bank_PalmPlanter", new Vector3(x, SidewalkY, -5.2f), 0f, 4.2f);   // подальше от фасада — кроны не лезут в стену
        }
        Place(g, "Bank_FireHydrant", new Vector3(-21.5f, SidewalkY, -9.6f), 0f, 0.8f);
        Place(g, "Bank_FireHydrant", new Vector3(31f, SidewalkY, -21.9f), 180f, 0.8f);
        Place(g, "Bank_TrashCan", new Vector3(-9f, SidewalkY, -9.6f), 0f, 1.0f);
        Place(g, "Bank_TrashCan", new Vector3(15.5f, SidewalkY, -9.6f), 0f, 1.0f);
        Place(g, "Bank_TrashCan", new Vector3(-20f, SidewalkY, -22f), 0f, 1.0f);
        Place(g, "Bank_NewsBox", new Vector3(8.8f, SidewalkY, -9.4f), 180f, 1.25f);
        Place(g, "Bank_NewsBox", new Vector3(-28f, SidewalkY, -22f), 0f, 1.25f);
        // у входа в банк: растения в кадках и банкомат на фасаде
        // банкомат на фасаде (из примитивов), спиной к цоколю
        Atm(g, new Vector3(17.5f, SidewalkY, -0.78f), 0f);
    }

    // =========================================================================== фургон грабителей
    static (Transform spawn, Transform exit) BuildArrivalVan(Transform root)
    {
        var parked = new Vector3(0f, RoadY, -13.4f);
        var vr = new GameObject("GetawayVan_Arrival").transform;
        vr.SetParent(root, false);
        vr.SetPositionAndRotation(parked, Quaternion.Euler(0, 180, 0));           // нос — от банка, задние двери — к ступенькам
        var van = Group(vr, "Van");                                               // движется и прячется (корень NetworkObject остаётся на месте)
        Vector3 W(float x, float y, float z) => van.TransformPoint(new Vector3(x, y, z));
        Quaternion R(float x = 0, float y = 0, float z = 0) => van.rotation * Quaternion.Euler(x, y, z);

        // кузов (локально: нос +Z, кузов z ∈ [−3, 1.2], кабина z ∈ [1.2, 3])
        Box(van, "Floor", W(0, 0.5f, -0.9f), new Vector3(2.3f, 0.1f, 4.2f), tread, true, R());
        Box(van, "Wall_L", W(-1.12f, 1.6f, -0.9f), new Vector3(0.06f, 2.1f, 4.2f), vanPaint, true, R());
        Box(van, "Wall_R", W(1.12f, 1.6f, -0.9f), new Vector3(0.06f, 2.1f, 4.2f), vanPaint, true, R());
        Box(van, "Roof", W(0, 2.68f, -0.9f), new Vector3(2.3f, 0.08f, 4.2f), vanPaint, true, R());
        Box(van, "Bulkhead", W(0, 1.6f, 1.18f), new Vector3(2.3f, 2.1f, 0.06f), vanPaint, true, R());
        foreach (var sx in new[] { -0.85f, 0.85f }) Box(van, "Bench", W(sx, 0.85f, -1.2f), new Vector3(0.5f, 0.1f, 2.4f), woodPanel, false, R());
        Box(van, "CargoStrip", W(0, 2.6f, -0.9f), new Vector3(0.08f, 0.04f, 3.8f), lampBulb, false, R());
        var cargoLight = new GameObject("CargoLight").AddComponent<Light>();     // тёплый плафон в кузове (гаснет вместе с фургоном)
        cargoLight.transform.SetParent(van, false);
        cargoLight.transform.localPosition = new Vector3(0, 2.35f, -0.9f);
        cargoLight.type = LightType.Point; cargoLight.color = new Color(1f, 0.78f, 0.5f); cargoLight.intensity = 1.6f; cargoLight.range = 4.5f;
        cargoLight.shadows = LightShadows.None;
        Box(van, "Stripe_L", W(-1.16f, 1.2f, -0.9f), new Vector3(0.02f, 0.18f, 4.2f), paint, false, R());
        Box(van, "Stripe_R", W(1.16f, 1.2f, -0.9f), new Vector3(0.02f, 0.18f, 4.2f), paint, false, R());
        Box(van, "RearBumper", W(0, 0.35f, -3.05f), new Vector3(2.3f, 0.2f, 0.12f), chromeMat, false, R());
        Box(van, "RearStep", W(0, 0.4f, -3.2f), new Vector3(1.6f, 0.06f, 0.35f), tread, true, R());
        // кабина и капот
        Box(van, "Cab", W(0, 1.425f, 1.9f), new Vector3(2.3f, 1.75f, 1.4f), vanPaint, true, R());
        Box(van, "Hood", W(0, 0.975f, 2.825f), new Vector3(2.25f, 0.85f, 0.45f), vanPaint, true, R());
        Box(van, "Windshield", W(0, 1.9f, 2.61f), new Vector3(2.0f, 0.6f, 0.04f), vanGlass, false, R());
        foreach (var sx in new[] { -1.16f, 1.16f }) Box(van, "CabWindow", W(sx, 1.85f, 1.9f), new Vector3(0.02f, 0.5f, 1.0f), vanGlass, false, R());
        Box(van, "FrontBumper", W(0, 0.45f, 3.07f), new Vector3(2.3f, 0.22f, 0.12f), chromeMat, false, R());
        Box(van, "Chassis", W(0, 0.42f, 0f), new Vector3(2.1f, 0.25f, 5.8f), rubber, false, R());
        foreach (var wz in new[] { -2.2f, 2.2f })
            foreach (var sx in new[] { -1.05f, 1.05f })
            {
                Cyl(van, "Wheel", W(sx, 0.42f, wz), 0.42f, 0.3f, rubber, false, R(0, 0, 90));
                Cyl(van, "Hubcap", W(sx * 1.16f, 0.42f, wz), 0.2f, 0.04f, chromeMat, false, R(0, 0, 90));
            }
        // фары и задние фонари
        var heads = new Light[2];
        for (int i = 0; i < 2; i++)
        {
            float sx = i == 0 ? -0.75f : 0.75f;
            Ball(van, "Headlight", W(sx, 1.0f, 3.06f), new Vector3(0.28f, 0.22f, 0.06f), headlightMat);
            var lg = new GameObject("HeadlightBeam");
            lg.transform.SetParent(van, false);
            lg.transform.SetPositionAndRotation(W(sx, 1.0f, 3.1f), R(8, 0, 0));
            var l = lg.AddComponent<Light>();
            l.type = LightType.Spot; l.color = new Color(1f, 0.95f, 0.82f); l.intensity = 4f; l.range = 16f;
            l.spotAngle = 55f; l.innerSpotAngle = 30f; l.shadows = LightShadows.None;
            heads[i] = l;
            Box(van, "Taillight", W(i == 0 ? -0.95f : 0.95f, 1.1f, -3.04f), new Vector3(0.18f, 0.3f, 0.04f), tailMat, false, R());
        }
        // задние двери на петлях (стоят распахнутыми, при старте захлопываются)
        var hinges = new Transform[2];
        var closed = new Vector3[2];
        for (int i = 0; i < 2; i++)
        {
            float sx = i == 0 ? -1.15f : 1.15f, dir = i == 0 ? 1f : -1f;
            var h = new GameObject(i == 0 ? "DoorHinge_L" : "DoorHinge_R").transform;
            h.SetParent(van, false);
            h.localPosition = new Vector3(sx, 1.6f, -3.0f);
            h.localRotation = Quaternion.identity;
            Box(h, "Door", h.TransformPoint(new Vector3(dir * 0.575f, 0, 0)), new Vector3(1.15f, 2.1f, 0.06f), vanPaint, true, h.rotation);
            Box(h, "DoorWindow", h.TransformPoint(new Vector3(dir * 0.575f, 0.45f, -0.035f)), new Vector3(0.6f, 0.4f, 0.01f), vanGlass, false, h.rotation);
            h.localRotation = Quaternion.Euler(0, i == 0 ? 110f : -110f, 0);   // распахнута наружу
            hinges[i] = h; closed[i] = Vector3.zero;
        }
        // табличка внутри кузова (на перегородке, видна из кузова)
        var status = Label(van, "StatusText", "ВЫХОДИ ИЗ ФУРГОНА — В БАНК!", 1.6f, new Color(0.3f, 1f, 0.3f), W(0, 2.05f, 1.14f), van.TransformDirection(Vector3.back), new Vector2(2.1f, 0.4f));
        status.fontSharedMaterial = neonCyan;

        // звук
        var engine = van.gameObject.AddComponent<AudioSource>();
        engine.playOnAwake = false; engine.loop = true; engine.spatialBlend = 1f; engine.volume = 0.55f; engine.minDistance = 2f; engine.maxDistance = 35f;
        var doorSrcGo = new GameObject("DoorAudio"); doorSrcGo.transform.SetParent(van, false); doorSrcGo.transform.localPosition = new Vector3(0, 1.4f, -3f);
        var doorSrc = doorSrcGo.AddComponent<AudioSource>();
        doorSrc.playOnAwake = false; doorSrc.spatialBlend = 1f; doorSrc.minDistance = 2f; doorSrc.maxDistance = 25f;

        // точки
        var spawn = new GameObject("PlayerSpawn_Van").transform;
        spawn.SetParent(root, false);
        spawn.SetPositionAndRotation(W(0, 0.56f, -0.4f), Quaternion.identity);   // лицом к задним дверям и банку (+Z мира)
        var exit = new GameObject("TeleportPoint_Street").transform;
        exit.SetParent(root, false);
        exit.SetPositionAndRotation(new Vector3(1.2f, SidewalkY, -8.3f), Quaternion.identity);

        // сеть
        var no = vr.gameObject.AddComponent<NetworkObject>();
        SetMasterFlags(no);
        var trig = vr.gameObject.AddComponent<NetworkHeistStartTrigger>();
        trig.van = van;
        trig.doorHinges = hinges;
        trig.closedEuler = closed;
        trig.cargoCenter = new Vector3(0, 1.55f, -0.9f);
        trig.cargoSize = new Vector3(2.24f, 2.3f, 4.1f);
        trig.exitPoint = exit;
        float y = RoadY;
        trig.path = new[]
        {
            parked, new Vector3(0, y, -15.4f), new Vector3(0.7f, y, -16.8f), new Vector3(2.2f, y, -17.6f), new Vector3(5f, y, -17.9f),
            new Vector3(38f, y, -17.9f), new Vector3(40.4f, y, -18.6f), new Vector3(41.6f, y, -20.2f), new Vector3(41.8f, y, -23f), new Vector3(41.8f, y, -40f),
        };
        trig.driveDuration = 7f;
        trig.teamWait = 10f;
        trig.engine = engine;
        trig.doorSource = doorSrc;
        trig.doorSlamClips = MapKit.Clips(ImpactDir, "impact_metal_heavy");
        trig.headlights = heads;
        trig.statusText = status;
        foreach (var t in vr.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        log.AppendLine("  arrival van: GetawayVan_Arrival (NetworkHeistStartTrigger, Master Client Object)");
        return (spawn, exit);
    }

    static void SetMasterFlags(NetworkObject no)
    {
        var f = no.Flags.SetCurrentVersion();
        f |= NetworkObjectFlags.MasterClientObject;
        f &= ~NetworkObjectFlags.AllowStateAuthorityOverride;
        f &= ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
        no.Flags = f;
    }

    /// Игроки (VR и ПК) стоят в кузове фургона — переопределение позиции экземпляров в этой сцене, префабы не меняются.
    static void PlacePlayers(UnityEngine.SceneManagement.Scene scene, Transform spawn)
    {
        foreach (var n in new[] { "VRGorillaPlayer", "DesktopTestPlayer" })
        {
            var p = scene.GetRootGameObjects().FirstOrDefault(g => g.name == n);
            if (!p) { log.AppendLine($"  WARN: {n} not found"); continue; }
            p.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            EditorUtility.SetDirty(p.transform);
            if (PrefabUtility.IsPartOfPrefabInstance(p)) PrefabUtility.RecordPrefabInstancePropertyModifications(p.transform);
        }
    }
}
