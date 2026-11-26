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
/// Чёрный миди-автобус вместо старого джипа в MainHub, салон внутри в 1.7 раза длиннее кузова.
///
/// Кузов строится кодом: боковой силуэт (скруглённый многоугольник с колёсными арками) вытягивается
/// поперёк со скруглёнными рёбрами. Дверей снаружи нет, вход — проём в корме.
///
/// «Внутри больше, чем снаружи» — через Stencil (Render Objects в Mobile_Renderer и PC_Renderer):
///  1) слой BusPortal  — маска проёма (и маска салона изнутри) пишет Stencil = 1, до непрозрачных;
///  2) слой BusInterior — салон, рисуется только где Stencil == 1, до непрозрачных;
///  3) слой BusExterior — кузов, колёса и северная стена хаба, рисуются только где Stencil != 1.
/// Салон физически уходит сквозь северную стену наружу: снаружи его не видно (стена и кузов),
/// в проёме не видно стену и кузов. Коллайдеры стены разрезаны под коридор салона.
///
/// Идемпотентный: пересоздаёт только корень сцены «BlackBus» и точечно правит VanPortal,
/// Van_Countdown, коллайдеры Wall_N и разметку VanDecor. MainHubBuilder не вызывается.
/// Меню: Tools/Heist/Hub/Black Bus. Batchmode: -executeMethod BlackBusBuilder.RunBatch
/// </summary>
public static class BlackBusBuilder
{
    const string HubPath = "Assets/Scenes/MainHub.unity";
    const string MatDir = "Assets/Art/Materials/Bus";
    const string MeshDir = "Assets/Art/Meshes/Bus";
    const string TexDir = "Assets/Art/Textures/Bus";
    const string ShaderPath = "Assets/Shaders/PortalStencilMask.shader";
    public const string RootName = "BlackBus";
    public const string LayerPortal = "BusPortal", LayerInterior = "BusInterior", LayerExterior = "BusExterior";

    // ---- габариты, локальные оси автобуса: +Z к кабине, X вправо, Y вверх, y = 0 — пол хаба
    const float L = 5.6f;                 // новый компактный автобус Bus_Modern.glb (короче и приземистее)
    static float W = 2.52f, H = 2.69f;    // ширина, высота — по пропорциям модели (пересчитываются при сборке)
    const string ModelPath = "Assets/Art/GeneratedModels/Bus_Modern.glb";
    const float Rb = 0.12f;               // радиус скругления продольных рёбер
    const float Bottom = 0.2f;            // клиренс (низ кузова = пол салона − Rb)
    const float RearZ = -L / 2f, FrontZ = L / 2f;
    const float FloorY = Bottom + Rb, CeilY = 2.43f, InHalfW = 1.5f;    // пол салона 0.32; салон на 20% шире прежнего (1.25)
    const float InteriorLen = L * 1.7f;   // 10.88 м
    static float DoorZ = RearZ;           // плоскость проёма в корме (по поверхности модели)
    static float InteriorEndZ => DoorZ + InteriorLen;
    const float DoorHalfW = 0.65f, DoorTopY = 2.32f;   // просвет 2.0 м: проходит и ПК-игрок (CharacterController 1.8 м)
    const float RearWheelZ = -1.9f, FrontWheelZ = 1.95f, WheelR = 0.45f, ArchR = 0.56f;
    const float NorthWallInnerZ = 13.8f;
    static readonly Vector3 BusPos = new Vector3(4.2f, 0f, NorthWallInnerZ - 0.12f - FrontZ); // нос в 12 см от стены

    // ---- синяя подсветка салона: насыщенный глубокий сапфир/кобальт, на 25–30% темнее первой версии
    // (была #1E90FF, 2.4). Цвет лампы — оттенок #07358C, приведённый к полной яркости (иначе тёмный цвет
    // лампы гасит свет вдвое сильнее, чем нужно); яркость −28%. Неон — #07358C с HDR-запасом под Bloom.
    static readonly Color LightCobalt = new Color(0.05f, 0.38f, 1f);    // тон #07358C (0.027, 0.208, 0.549) / 0.549
    const float LightIntensity = 1.73f;                                // 2.4 × 0.72
    static readonly Color NeonCobalt = new Color(0.027f, 0.208f, 0.549f) * 1.6f;   // макс. канал 0.88 (было 1.25) → −30%

    static readonly StringBuilder log = new StringBuilder();
    static Material mBody, mGlass, mTire, mRim, mLed, mTail, mWall, mFloor, mNeon, mMask, mStep;

    [MenuItem("Tools/Heist/Hub/Black Bus")]
    static void Menu() => Debug.Log(Run());

    public static void RunBatch()
    {
        string r;
        try { r = Run(); }
        catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); return; }
        Debug.Log(r);
        System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "claude", "bus_build.txt"), r);
        EditorApplication.Exit(r.Contains("ERROR") ? 1 : 0);
    }

    public static string Run()
    {
        log.Clear().AppendLine("[BlackBusBuilder]");
        MapKit.EnsureFolder(MatDir); MapKit.EnsureFolder(MeshDir); MapKit.EnsureFolder(TexDir);
        EnsureLayers();
        int lPortal = LayerMask.NameToLayer(LayerPortal), lInt = LayerMask.NameToLayer(LayerInterior), lExt = LayerMask.NameToLayer(LayerExterior);
        if (lPortal < 0 || lInt < 0 || lExt < 0) { log.AppendLine("ERROR: layers not created"); return log.ToString(); }
        SetupRenderers(lPortal, lInt, lExt);
        BuildMaterials();

        var scene = EditorSceneManager.OpenScene(HubPath, OpenSceneMode.Single);
        foreach (var g in scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray()) Object.DestroyImmediate(g);
        var map = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "MainHubMap");
        if (!map) { log.AppendLine("ERROR: MainHubMap not found"); return log.ToString(); }

        // старый джип
        foreach (var t in map.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Hub_Van") && t.parent && t.parent.name == "Static").ToArray())
        { log.AppendLine($"  removed old {t.name}"); Object.DestroyImmediate(t.gameObject); }

        var root = new GameObject(RootName).transform;
        root.SetPositionAndRotation(BusPos, Quaternion.identity);
        var portal = root.gameObject.AddComponent<BusPortal>();

        BuildExteriorFromModel(root, lExt);
        var insideMask = BuildInterior(root, lInt, lPortal);
        portal.insideMask = insideMask;
        portal.doorZ = RearZ; portal.interiorEndZ = InteriorEndZ; portal.halfWidth = InHalfW;
        portal.floorY = FloorY; portal.ceilingY = CeilY; portal.doorHalfWidth = DoorHalfW; portal.doorTopY = DoorTopY;
        BuildDepartureRig(root, lExt, lInt, lPortal);
        BuildColliders(root);

        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.allowOcclusionWhenDynamic = false;

        FixNorthWall(map.transform, lExt);
        FixVanPortal(map.transform, root);
        FixVanDecor(scene, root);
        log.AppendLine(HubTunnelBuilder.Build(map.transform, root, InteriorEndZ, H, InHalfW + 0.14f));

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) log.AppendLine("ERROR: save failed");
        AssetDatabase.SaveAssets();
        log.AppendLine(TestColliders(root));
        return log.ToString();
    }

    // =========================================================================== слои и рендер
    static void EnsureLayers()
    {
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        foreach (var name in new[] { LayerPortal, LayerInterior, LayerExterior })
        {
            bool has = false;
            for (int i = 0; i < layers.arraySize; i++) if (layers.GetArrayElementAtIndex(i).stringValue == name) has = true;
            if (has) continue;
            for (int i = 10; i < layers.arraySize; i++)
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) { layers.GetArrayElementAtIndex(i).stringValue = name; log.AppendLine($"  layer {i} = {name}"); break; }
        }
        tm.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetupRenderers(int lPortal, int lInt, int lExt)
    {
        int busMask = (1 << lPortal) | (1 << lInt) | (1 << lExt);
        foreach (var path in new[] { "Assets/Settings/Mobile_Renderer.asset", "Assets/Settings/PC_Renderer.asset" })
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (!data) { log.AppendLine("ERROR: no renderer " + path); continue; }
            data.opaqueLayerMask = data.opaqueLayerMask & ~busMask;
            data.transparentLayerMask = data.transparentLayerMask & ~busMask;
            Feature(data, "Bus 1 Portal Mask", RenderPassEvent.BeforeRenderingOpaques, 1 << lPortal, null);
            Feature(data, "Bus 2 Interior", RenderPassEvent.BeforeRenderingOpaques, 1 << lInt, CompareFunction.Equal);
            Feature(data, "Bus 3 Exterior", RenderPassEvent.AfterRenderingOpaques, 1 << lExt, CompareFunction.NotEqual);
            // прозрачное в салоне (табло отсчёта, ники горилл внутри) — тоже только сквозь проём и изнутри
            Feature(data, "Bus 4 Interior Transparent", RenderPassEvent.AfterRenderingTransparents, 1 << lInt, CompareFunction.Equal, RenderQueueType.Transparent);
            data.SetDirty();
            EditorUtility.SetDirty(data);
            log.AppendLine($"  renderer {System.IO.Path.GetFileNameWithoutExtension(path)}: features = {string.Join(", ", data.rendererFeatures.Where(f => f).Select(f => f.name))}");
        }
    }

    static void Feature(UniversalRendererData data, string name, RenderPassEvent evt, int mask, CompareFunction? stencil, RenderQueueType queue = RenderQueueType.Opaque)
    {
        var f = data.rendererFeatures.OfType<RenderObjects>().FirstOrDefault(x => x && x.name == name);
        if (!f)
        {
            f = ScriptableObject.CreateInstance<RenderObjects>();
            f.name = name;
            AssetDatabase.AddObjectToAsset(f, data);
            data.rendererFeatures.Add(f);
            var so = new SerializedObject(data);
            var map = so.FindProperty("m_RendererFeatureMap");
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out string _, out long id);
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        var s = f.settings;
        s.passTag = name;
        s.Event = evt;
        s.filterSettings.RenderQueueType = queue;
        s.filterSettings.LayerMask = mask;
        s.filterSettings.PassNames = new string[0];
        s.overrideMaterial = null;
        s.overrideDepthState = false;
        s.stencilSettings.overrideStencilState = stencil.HasValue;
        s.stencilSettings.stencilReference = 1;
        s.stencilSettings.stencilCompareFunction = stencil ?? CompareFunction.Always;
        s.stencilSettings.passOperation = StencilOp.Keep;
        s.stencilSettings.failOperation = StencilOp.Keep;
        s.stencilSettings.zFailOperation = StencilOp.Keep;
        f.SetActive(true);
        f.Create();
        EditorUtility.SetDirty(f);
    }

    // =========================================================================== материалы
    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    static void BuildMaterials()
    {
        // кузов: матово-сатиновый чёрный; отражения неба выключены (в помещении они делают его серым),
        // блики от ламп остаются — по ним читается форма
        mBody = Lit("M_Bus_Body", Hex("#0D0D0F"), 0.1f, 0.45f, envReflections: false);
        // тонировка: почти чёрное глянцевое стекло, видно только блики
        mGlass = Lit("M_Bus_Glass", Hex("#030304"), 0.2f, 0.95f);
        mTire = Lit("M_Bus_Tire", Hex("#121214"), 0f, 0.22f, envReflections: false);
        mRim = Lit("M_Bus_Rim", Hex("#0A0A0B"), 0.6f, 0.55f, envReflections: false);
        mStep = Lit("M_Bus_Step", Hex("#141417"), 0.3f, 0.3f, envReflections: false);
        // светящиеся элементы — Unlit с HDR-цветом (не зависят от света и ключевых слов Lit, ловят Bloom)
        mLed = Glow("M_Bus_LED", new Color(3.2f, 3.2f, 3.4f));
        mTail = Glow("M_Bus_TailLED", new Color(2.4f, 0.06f, 0.05f));
        // #0077FF / #1E90FF с запасом яркости под Bloom; зелёный канал приглушён, иначе тонмаппинг уводит в бирюзу
        // глубокий сапфирово-кобальтовый (#07358C) и на ~30% темнее прежнего (было (0.02, 0.38, 1) × 1.25)
        mNeon = Glow("M_Bus_NeonBlue", NeonCobalt);
        mWall = Lit("M_Bus_InteriorWall", Hex("#101014"), 0.1f, 0.5f, envReflections: false);
        mFloor = Lit("M_Bus_InteriorFloor", Color.white, 0.2f, 0.35f, null, FloorTexture(), new Vector2(2f, 2f), envReflections: false);

        string mp = $"{MatDir}/M_Bus_PortalMask.mat";
        mMask = AssetDatabase.LoadAssetAtPath<Material>(mp);
        var sh = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath) ?? Shader.Find("Grabz/PortalStencilMask");
        if (!sh) { log.AppendLine("ERROR: PortalStencilMask shader not found"); return; }
        if (!mMask) { mMask = new Material(sh); AssetDatabase.CreateAsset(mMask, mp); }
        mMask.shader = sh;
        mMask.SetFloat("_StencilRef", 1);
        EditorUtility.SetDirty(mMask);
    }

    static Material Glow(string name, Color hdr)
    {
        string path = $"{MatDir}/{name}.mat";
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetColor("_BaseColor", hdr);
        m.SetTexture("_BaseMap", null);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Lit(string name, Color c, float metal, float smooth, Color? emission = null, Texture2D tex = null, Vector2? tiling = null, bool envReflections = true)
    {
        string path = $"{MatDir}/{name}.mat";
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Metallic", metal);
        m.SetFloat("_Smoothness", smooth);
        m.SetTexture("_BaseMap", tex);
        m.SetTextureScale("_BaseMap", tiling ?? Vector2.one);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
        m.SetFloat("_EnvironmentReflections", envReflections ? 1f : 0f);
        if (envReflections) m.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); else m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        EditorUtility.SetDirty(m);
        return m;
    }

    /// Тёмный рифлёный пол: продольные рёбра (полоса на 0.5 м при тайлинге 2).
    static Texture2D FloorTexture()
    {
        string path = $"{TexDir}/T_Bus_FloorRibs.png";
        if (!System.IO.File.Exists(path))
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGB24, false);
            var baseC = Hex("#17171B");
            for (int x = 0; x < N; x++)
            {
                float u = (x % 16) / 16f;                          // 4 ребра на тайл
                float rib = u < 0.18f ? 0.55f : (u < 0.28f ? 0.85f : (u < 0.34f ? -0.25f : 0f));
                for (int y = 0; y < N; y++)
                {
                    float noise = (Mathf.PerlinNoise(x * 0.31f, y * 0.29f) - 0.5f) * 0.12f;
                    float k = 1f + rib * 0.6f + noise;
                    tex.SetPixel(x, y, new Color(baseC.r * k, baseC.g * k, baseC.b * k * 1.05f));
                }
            }
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat; imp.filterMode = FilterMode.Bilinear; imp.maxTextureSize = 64; imp.mipmapEnabled = true;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // =========================================================================== силуэт кузова
    static float Ang(Vector2 v) => Mathf.Atan2(v.y, v.x);
    static Vector2 Dir(float a) => new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    /// Скругляет углы многоугольника (обход против часовой, точки (z, y)).
    static List<Vector2> Fillet(IList<Vector2> poly, IList<float> radii, float stepRad = 0.16f)
    {
        var res = new List<Vector2>();
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 P = poly[i], A = poly[(i + n - 1) % n], B = poly[(i + 1) % n];
            float r = radii[i];
            Vector2 d1 = (A - P).normalized, d2 = (B - P).normalized;
            float ang = Mathf.Acos(Mathf.Clamp(Vector2.Dot(d1, d2), -1f, 1f));
            if (r <= 0f || ang > Mathf.PI - 0.01f) { res.Add(P); continue; }
            float t = r / Mathf.Tan(ang / 2f);
            float tMax = 0.45f * Mathf.Min((A - P).magnitude, (B - P).magnitude);
            if (t > tMax) { t = tMax; r = t * Mathf.Tan(ang / 2f); }
            Vector2 C = P + (d1 + d2).normalized * (r / Mathf.Sin(ang / 2f));
            Vector2 s = P + d1 * t, e = P + d2 * t;
            float a0 = Ang(s - C);
            float da = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, Ang(e - C) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(da) / stepRad));
            for (int k = 0; k <= steps; k++) res.Add(C + r * Dir(a0 + da * k / steps));
        }
        return res;
    }

    /// Колёсная арка с плавными входами (вогнутая дуга над колесом + два выпуклых скругления).
    static IEnumerable<Vector2> Arch(float cz, float rf = 0.15f, int seg = 16)
    {
        var C = new Vector2(cz, WheelR);
        float dy = Bottom + rf - WheelR;
        float d = Mathf.Sqrt((ArchR + rf) * (ArchR + rf) - dy * dy);
        var F1 = new Vector2(cz - d, Bottom + rf);
        var F2 = new Vector2(cz + d, Bottom + rf);
        float g1 = Ang(C - F1);
        for (int k = 0; k <= 5; k++) yield return F1 + rf * Dir(Mathf.Lerp(-Mathf.PI / 2f, g1, k / 5f));
        float b0 = Ang(F1 - C); if (b0 < 0) b0 += 2f * Mathf.PI;     // ~181°
        float b1 = Ang(F2 - C);                                        // ~-2°  → дуга идёт ВЕРХОМ
        for (int k = 1; k < seg; k++) yield return C + ArchR * Dir(Mathf.Lerp(b0, b1, k / (float)seg));
        float g2 = Ang(C - F2);
        for (int k = 0; k <= 5; k++) yield return F2 + rf * Dir(Mathf.Lerp(g2, 1.5f * Mathf.PI, k / 5f));
    }

    static List<Vector2> BodyOutline()
    {
        var poly = new[] { new Vector2(RearZ, Bottom), new Vector2(3.12f, Bottom), new Vector2(FrontZ + 0.05f, 0.75f),
                           new Vector2(3.24f, 1.1f), new Vector2(2.25f, H - 0.02f), new Vector2(RearZ, H) };   // лобовое под ~35°
        var radii = new[] { Rb, 0.2f, 0.25f, 0.2f, 0.55f, 0.15f };   // задний верхний угол 0.15 — под высокий проём
        var o = Fillet(poly, radii);
        // колёсные арки — в длинную нижнюю кромку
        int k = -1;
        for (int i = 0; i < o.Count - 1; i++)
            if (Mathf.Abs(o[i].y - Bottom) < 1e-4f && Mathf.Abs(o[i + 1].y - Bottom) < 1e-4f && o[i + 1].x - o[i].x > 2f) { k = i; break; }
        if (k < 0) { log.AppendLine("ERROR: bottom edge not found"); return o; }
        o.InsertRange(k + 1, Arch(RearWheelZ).Concat(Arch(FrontWheelZ)));
        // точка излома проёма на задней стенке (верх проёма), задняя кромка — замыкающее ребро
        o.Add(new Vector2(RearZ, DoorTopY));
        return Dedupe(o, true);
    }

    static List<Vector2> Dedupe(List<Vector2> pts, bool closed)
    {
        var r = new List<Vector2>();
        foreach (var p in pts) if (r.Count == 0 || (p - r[r.Count - 1]).sqrMagnitude > 1e-8f) r.Add(p);
        if (closed && r.Count > 1 && (r[0] - r[r.Count - 1]).sqrMagnitude < 1e-8f) r.RemoveAt(r.Count - 1);
        return r;
    }

    static Vector2[] OutlineNormals(List<Vector2> o)
    {
        int n = o.Count;
        var res = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 a = o[(i + n - 1) % n], p = o[i], b = o[(i + 1) % n];
            Vector2 e1 = (p - a).normalized, e2 = (b - p).normalized;
            Vector2 n1 = new Vector2(e1.y, -e1.x), n2 = new Vector2(e2.y, -e2.x);   // наружу при обходе против часовой
            res[i] = (n1 + n2).normalized;
        }
        return res;
    }

    // =========================================================================== «лофт»: силуэт → кузов со скруглёнными рёбрами
    struct CrossSample { public float x, inset, phi, side; }

    static List<CrossSample> CrossSamples(float width, float rb, bool doorway, int bev = 6)
    {
        float mid = width / 2f - rb;
        var c = new List<CrossSample>();
        for (int k = bev; k >= 1; k--) { float phi = k / (float)bev * Mathf.PI / 2f; c.Add(new CrossSample { x = -mid - rb * Mathf.Sin(phi), inset = rb * (1 - Mathf.Cos(phi)), phi = phi, side = -1 }); }
        c.Add(new CrossSample { x = -mid });
        if (doorway) { c.Add(new CrossSample { x = -DoorHalfW }); c.Add(new CrossSample { x = DoorHalfW }); }
        c.Add(new CrossSample { x = mid });
        for (int k = 1; k <= bev; k++) { float phi = k / (float)bev * Mathf.PI / 2f; c.Add(new CrossSample { x = mid + rb * Mathf.Sin(phi), inset = rb * (1 - Mathf.Cos(phi)), phi = phi, side = 1 }); }
        return c;
    }

    static Vector3 LoftPoint(Vector2 o, Vector2 n, CrossSample c, float offset = 0f)
    {
        Vector2 yz = o - n * c.inset;
        var p = new Vector3(c.x, yz.y, yz.x);
        if (offset != 0f) p += LoftNormal(n, c) * offset;
        return p;
    }

    static Vector3 LoftNormal(Vector2 n, CrossSample c)
    {
        float cs = Mathf.Cos(c.phi), sn = Mathf.Sin(c.phi);
        return new Vector3(c.side * sn, n.y * cs, n.x * cs).normalized;
    }

    /// Замкнутый кузов: лофт по силуэту + две плоские боковины. doorway — вырезать проём в корме.
    static Mesh BodyMesh(string name, List<Vector2> o, float width, float rb, bool doorway)
    {
        var nrm = OutlineNormals(o);
        var cross = CrossSamples(width, rb, doorway);
        int n = o.Count, m = cross.Count;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        for (int i = 0; i < n; i++)
            for (int c = 0; c < m; c++) { v.Add(LoftPoint(o[i], nrm[i], cross[c])); uv.Add(new Vector2(cross[c].x, i / (float)n)); }
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            bool doorEdge = doorway && Mathf.Abs(o[i].x - RearZ) < 1e-3f && Mathf.Abs(o[j].x - RearZ) < 1e-3f
                            && Mathf.Min(o[i].y, o[j].y) >= FloorY - 1e-3f && Mathf.Max(o[i].y, o[j].y) <= DoorTopY + 1e-3f;
            for (int c = 0; c < m - 1; c++)
            {
                if (doorEdge && Mathf.Abs(cross[c].x + DoorHalfW) < 1e-4f) continue;   // проём
                int a = i * m + c, b = j * m + c, d = j * m + c + 1, e = i * m + c + 1;
                tri.AddRange(new[] { a, b, d, a, d, e });
            }
        }
        // боковины
        var side = Dedupe(o.Select((p, i) => p - nrm[i] * rb).ToList(), true);
        side = RemoveCollinear(side);
        var ear = EarClip(side, name);
        foreach (float sx in new[] { -1f, 1f })
        {
            int b0 = v.Count;
            foreach (var p in side) { v.Add(new Vector3(sx * width / 2f, p.y, p.x)); uv.Add(new Vector2(p.x, p.y)); }
            for (int t = 0; t < ear.Count; t += 3)
            {
                int i0 = b0 + ear[t], i1 = b0 + ear[t + 1], i2 = b0 + ear[t + 2];
                var nn = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
                if (nn.x * sx < 0) tri.AddRange(new[] { i0, i2, i1 }); else tri.AddRange(new[] { i0, i1, i2 });
            }
        }
        var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
        // ориентация лофта: квад на крыше должен смотреть вверх
        OrientLoft(mesh, n * m);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    /// Все квады лофта (вершины &lt; loftVerts) построены одинаково: проверяем самый высокий
    /// не-боковой треугольник (крыша) и при надобности разворачиваем весь лофт. Боковины уже ориентированы.
    static void OrientLoft(Mesh mesh, int loftVerts)
    {
        var v = mesh.vertices; var t = mesh.triangles;
        int best = -1; float bestY = float.MinValue;
        for (int i = 0; i < t.Length; i += 3)
        {
            if (t[i] >= loftVerts) continue;
            var nn = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            if (nn.sqrMagnitude < 1e-12f || Mathf.Abs(nn.x) > 0.5f * nn.magnitude) continue;
            float y = (v[t[i]].y + v[t[i + 1]].y + v[t[i + 2]].y) / 3f;
            if (y > bestY) { bestY = y; best = i; }
        }
        if (best < 0) return;
        var bn = Vector3.Cross(v[t[best + 1]] - v[t[best]], v[t[best + 2]] - v[t[best]]);
        if (bn.y >= 0) return;
        for (int i = 0; i < t.Length; i += 3)
            if (t[i] < loftVerts) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
        mesh.triangles = t;
    }

    static List<Vector2> RemoveCollinear(List<Vector2> p)
    {
        var r = new List<Vector2>(p);
        bool changed = true;
        while (changed && r.Count > 3)
        {
            changed = false;
            for (int i = 0; i < r.Count; i++)
            {
                Vector2 a = r[(i + r.Count - 1) % r.Count], b = r[i], c = r[(i + 1) % r.Count];
                if (Mathf.Abs(Cross(b - a, c - b)) < 1e-6f && Vector2.Dot(b - a, c - b) > 0) { r.RemoveAt(i); changed = true; break; }
            }
        }
        return r;
    }

    static List<int> EarClip(List<Vector2> pts, string who = "")
    {
        var idx = Enumerable.Range(0, pts.Count).ToList();
        float area = 0; for (int i = 0; i < pts.Count; i++) area += Cross(pts[i], pts[(i + 1) % pts.Count]);
        if (area < 0) idx.Reverse();
        var tris = new List<int>();
        int guard = 0;
        while (idx.Count > 3 && guard++ < 20000)
        {
            bool found = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int a = idx[(i + idx.Count - 1) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                Vector2 A = pts[a], B = pts[b], C = pts[c];
                if (Cross(B - A, C - B) <= 1e-9f) continue;
                bool inside = false;
                foreach (int k in idx)
                {
                    if (k == a || k == b || k == c) continue;
                    Vector2 P = pts[k];
                    if (Cross(B - A, P - A) >= 0 && Cross(C - B, P - B) >= 0 && Cross(A - C, P - C) >= 0) { inside = true; break; }
                }
                if (inside) continue;
                tris.Add(a); tris.Add(b); tris.Add(c);
                idx.RemoveAt(i); found = true; break;
            }
            if (!found) { log.AppendLine($"  WARN: ear clipping {who} stopped with {idx.Count} verts"); break; }
        }
        if (idx.Count == 3) tris.AddRange(idx);
        return tris;
    }

    /// Кусок лофта (стекло): точки силуэта [i0..i1], поперёк — cross[c0..c1], приподнят над кузовом на offset.
    static Mesh LoftPatch(string name, List<Vector2> o, int i0, int i1, List<CrossSample> cross, int c0, int c1, float offset)
    {
        var nrm = OutlineNormals(o);
        var v = new List<Vector3>(); var tri = new List<int>();
        int m = c1 - c0 + 1;
        for (int i = i0; i <= i1; i++) for (int c = c0; c <= c1; c++) v.Add(LoftPoint(o[i], nrm[i], cross[c], offset));
        for (int i = 0; i < i1 - i0; i++)
            for (int c = 0; c < m - 1; c++)
            {
                int a = i * m + c, b = (i + 1) * m + c, d = (i + 1) * m + c + 1, e = i * m + c + 1;
                tri.AddRange(new[] { a, b, d, a, d, e });
            }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v); mesh.SetTriangles(tri, 0);
        // наружу = вперёд/вверх
        var n0 = Vector3.Cross(v[tri[1]] - v[tri[0]], v[tri[2]] - v[tri[0]]);
        Vector2 on = nrm[(i0 + i1) / 2];
        if (Vector3.Dot(n0, new Vector3(0, on.y, on.x)) < 0) { var t = mesh.triangles; for (int k = 0; k < t.Length; k += 3) (t[k + 1], t[k + 2]) = (t[k + 2], t[k + 1]); mesh.triangles = t; }
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh FlatPoly(string name, List<Vector2> zy, float x, Vector3 normal)
    {
        var v = zy.Select(p => new Vector3(x, p.y, p.x)).ToList();
        var ear = EarClip(zy, name);
        var t = new List<int>();
        for (int i = 0; i < ear.Count; i += 3)
        {
            var nn = Vector3.Cross(v[ear[i + 1]] - v[ear[i]], v[ear[i + 2]] - v[ear[i]]);
            if (Vector3.Dot(nn, normal) < 0) t.AddRange(new[] { ear[i], ear[i + 2], ear[i + 1] }); else t.AddRange(new[] { ear[i], ear[i + 1], ear[i + 2] });
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v); mesh.SetTriangles(t, 0);
        mesh.SetNormals(Enumerable.Repeat(normal, v.Count).ToList());
        mesh.RecalculateBounds();
        return mesh;
    }

    /// Z передней поверхности кузова (по силуэту) на высоте y.
    static float FrontZAt(List<Vector2> o, float y)
    {
        float best = float.MinValue;
        for (int i = 0; i < o.Count; i++)
        {
            Vector2 a = o[i], b = o[(i + 1) % o.Count];
            if (a.x < 1.5f && b.x < 1.5f) continue;
            if ((a.y - y) * (b.y - y) > 0 || Mathf.Approximately(a.y, b.y)) continue;
            float z = Mathf.Lerp(a.x, b.x, (y - a.y) / (b.y - a.y));
            best = Mathf.Max(best, z);
        }
        return best;
    }

    static Mesh SaveMesh(Mesh mesh)
    {
        string path = $"{MeshDir}/{mesh.name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing)
        {
            existing.Clear();
            existing.indexFormat = mesh.indexFormat;
            existing.SetVertices(mesh.vertices); existing.SetNormals(mesh.normals);
            if (mesh.uv.Length == mesh.vertexCount) existing.SetUVs(0, mesh.uv);
            existing.subMeshCount = mesh.subMeshCount;
            for (int s = 0; s < mesh.subMeshCount; s++) existing.SetTriangles(mesh.GetTriangles(s), s);
            existing.RecalculateBounds();
            if (mesh.uv.Length == mesh.vertexCount) existing.RecalculateTangents();
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }
        if (mesh.uv.Length == mesh.vertexCount) mesh.RecalculateTangents();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // =========================================================================== снаружи
    static GameObject Part(Transform p, string name, Mesh mesh, Material mat, int layer, bool shadows = true)
    {
        var go = new GameObject(name);
        go.transform.SetParent(p, false);
        go.layer = layer;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        return go;
    }

    static GameObject Prim(PrimitiveType type, Transform p, string name, Vector3 pos, Vector3 scale, Quaternion rot, Material mat, int layer, bool shadows = true)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(p, false);
        go.transform.localPosition = pos; go.transform.localRotation = rot; go.transform.localScale = scale;
        go.layer = layer;
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        return go;
    }

    static void BuildExterior(Transform root, int lExt)
    {
        var ext = new GameObject("Exterior").transform;
        ext.SetParent(root, false);
        var o = BodyOutline();
        var body = SaveMesh(BodyMesh("Bus_Body", o, W, Rb, true));
        Part(ext, "Body", body, mBody, lExt);
        log.AppendLine($"  body: {body.vertexCount} verts, {body.triangles.Length / 3} tris, bounds {body.bounds.size:F2}");

        // панорамное лобовое стекло: передний скат силуэта, захватывает скругления до 75°
        var cross = CrossSamples(W, Rb, false);
        int i0 = -1, i1 = -1;
        for (int i = 0; i < o.Count; i++)
            if (o[i].x > 1.6f && o[i].y > 1.2f && o[i].y < H - 0.08f) { if (i0 < 0) i0 = i; i1 = i; }
        if (i0 >= 0 && i1 > i0)
            Part(ext, "Windshield", SaveMesh(LoftPatch("Bus_Windshield", o, i0, i1, cross, 1, cross.Count - 2, 0.006f)), mGlass, lExt, false);
        else log.AppendLine("ERROR: windshield range not found");

        // два небольших боковых окна (водитель/пассажир) — параллельны скату лобового
        var win = Fillet(new[] { new Vector2(1.45f, 1.30f), new Vector2(2.87f, 1.30f), new Vector2(2.32f, 2.08f), new Vector2(1.45f, 2.08f) },
                         new[] { 0.07f, 0.07f, 0.07f, 0.07f });
        foreach (float sx in new[] { -1f, 1f })
            Part(ext, sx < 0 ? "SideWindow_L" : "SideWindow_R", SaveMesh(FlatPoly(sx < 0 ? "Bus_SideWindow_L" : "Bus_SideWindow_R", win, sx * (W / 2f + 0.004f), new Vector3(sx, 0, 0))), mGlass, lExt, false);

        // узкие LED-фары и тонкая полоса ДХО под лобовым
        float zf = FrontZAt(o, 0.82f);
        foreach (float sx in new[] { -1f, 1f })
        {
            Prim(PrimitiveType.Cube, ext, "LED_Headlight", new Vector3(sx * 0.86f, 0.82f, zf - 0.006f), new Vector3(0.56f, 0.04f, 0.03f), Quaternion.Euler(0, 0, sx * 6f), mLed, lExt, false);
            Prim(PrimitiveType.Cube, ext, "LED_Lower", new Vector3(sx * 1.02f, 0.6f, FrontZAt(o, 0.6f) - 0.008f), new Vector3(0.16f, 0.025f, 0.03f), Quaternion.identity, mLed, lExt, false);
        }
        // тонкие красные фонари по бокам кормы (у задних углов, проём не задевают)
        foreach (float sx in new[] { -1f, 1f })
            Prim(PrimitiveType.Cube, ext, "LED_Tail", new Vector3(sx * 1.08f, 1.55f, RearZ - 0.012f), new Vector3(0.035f, 0.55f, 0.03f), Quaternion.identity, mTail, lExt, false);

        // колёса: шина + чёрный диск + колпак
        foreach (float wz in new[] { RearWheelZ, FrontWheelZ })
            foreach (float sx in new[] { -1f, 1f })
            {
                var rot = Quaternion.Euler(0, 0, 90);
                Prim(PrimitiveType.Cylinder, ext, "Tire", new Vector3(sx * (W / 2f - 0.19f), WheelR, wz), new Vector3(WheelR * 2, 0.15f, WheelR * 2), rot, mTire, lExt);
                Prim(PrimitiveType.Cylinder, ext, "Rim", new Vector3(sx * (W / 2f - 0.05f), WheelR, wz), new Vector3(0.6f, 0.02f, 0.6f), rot, mRim, lExt, false);
                Prim(PrimitiveType.Cylinder, ext, "HubCap", new Vector3(sx * (W / 2f - 0.03f), WheelR, wz), new Vector3(0.17f, 0.012f, 0.17f), rot, mRim, lExt, false);
            }

        // обтекаемый блок кондиционера на крыше
        var acO = Dedupe(Fillet(new[] { new Vector2(-1.1f, 0f), new Vector2(1.1f, 0f), new Vector2(0.9f, 0.2f), new Vector2(-0.9f, 0.2f) },
                                new[] { 0.05f, 0.05f, 0.1f, 0.1f }), true);   // радиусы ≥ скругления рёбер (0.05)
        var ac = Part(ext, "RoofAC", SaveMesh(BodyMesh("Bus_RoofAC", acO, 1.5f, 0.05f, false)), mBody, lExt);
        ac.transform.localPosition = new Vector3(0, H - 0.015f, -0.6f);

        // вход: синяя LED-рамка проёма и ступенька (обычный слой — видны всегда, и изнутри в проёме тоже)
        var door = new GameObject("Doorway").transform;
        door.SetParent(root, false);
        float dz = RearZ - 0.02f, dh = DoorTopY - FloorY;
        foreach (float sx in new[] { -1f, 1f })
            Prim(PrimitiveType.Cube, door, "DoorNeon_Side", new Vector3(sx * (DoorHalfW + 0.025f), FloorY + dh / 2f, dz), new Vector3(0.035f, dh + 0.05f, 0.035f), Quaternion.identity, mNeon, 0, false);
        Prim(PrimitiveType.Cube, door, "DoorNeon_Top", new Vector3(0, DoorTopY + 0.025f, dz), new Vector3(DoorHalfW * 2 + 0.085f, 0.035f, 0.035f), Quaternion.identity, mNeon, 0, false);
        var step = Prim(PrimitiveType.Cube, door, "Step", new Vector3(0, 0.1f, RearZ - 0.16f), new Vector3(DoorHalfW * 2 + 0.2f, 0.2f, 0.3f), Quaternion.identity, mStep, 0);
        step.AddComponent<BoxCollider>();
        MapKit.Font = MapKit.Font ? MapKit.Font : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MapKit.FontPath);
        var label = MapKit.Text(door, "DoorLabel", "ВХОД · НА ДЕЛО", 0.9f, new Color(0.55f, 0.8f, 1f), new Vector3(0, 0.1f, RearZ - 0.311f), MapKit.Facing(Vector3.back), new Vector2(1.4f, 0.16f));   // на ступеньке: над высоким проёмом места нет
        label.GetComponent<Renderer>().allowOcclusionWhenDynamic = false;
    }

    // =========================================================================== кузов из Bus_Modern.glb
    /// Кузов — модель Tripo (по 4 ракурсам референса). Масштаб до длины L, нос к +Z, низ колёс на полу.
    /// Материалы: кузов — матово-сатиновый чёрный металл, стёкла (лобовое + боковые у водителя и пассажира)
    /// — глухая глянцевая тонировка. Стёкла отделяются по текстуре модели (тёмные пиксели в зоне кабины).
    /// В корме точно вырезается проём (треугольники обрезаются по прямоугольнику проёма).
    static void BuildExteriorFromModel(Transform root, int lExt)
    {
        var ext = new GameObject("Exterior").transform;
        ext.SetParent(root, false);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var mf = model ? model.GetComponentInChildren<MeshFilter>() : null;
        if (!mf) { log.AppendLine("ERROR: " + ModelPath + " not found"); return; }
        var src = mf.sharedMesh;
        var sv = src.vertices; var sn = src.normals; var suv = src.uv; var st = src.triangles;

        // оси модели: длина по X, нос — у +X (крыша там ниже — скат лобового)
        var wv = sv.Select(p => mf.transform.TransformPoint(p)).ToArray();
        var b = new Bounds(wv[0], Vector3.zero); foreach (var p in wv) b.Encapsulate(p);
        float scale = L / b.size.x;
        Vector3 ToBus(Vector3 p) => new Vector3(-(p.z - b.center.z), p.y - b.min.y, p.x - b.center.x) * scale;   // +X модели → +Z автобуса
        Vector3 DirToBus(Vector3 d) => new Vector3(-d.z, d.y, d.x);
        var v = wv.Select(ToBus).ToList();
        var n = sn.Select(d => DirToBus(mf.transform.TransformDirection(d)).normalized).ToList();
        var uv = suv.ToList();
        W = b.size.z * scale; H = b.size.y * scale;
        log.AppendLine($"  model: {src.vertexCount} verts, {st.Length / 3} tris, scale {scale:F2} → L {L:F2} × W {W:F2} × H {H:F2}");

        // плоскость проёма: средняя Z задней стенки в зоне проёма
        var rearZs = new List<float>();
        for (int i = 0; i < v.Count; i++)
            if (v[i].z < RearZ + 0.4f && Mathf.Abs(v[i].x) < DoorHalfW && v[i].y > FloorY && v[i].y < DoorTopY && n[i].z < -0.6f) rearZs.Add(v[i].z);
        DoorZ = rearZs.Count > 0 ? rearZs.Average() : RearZ;
        log.AppendLine($"  door plane z = {DoorZ:F3} (rear verts {rearZs.Count})");

        // ---- текстура (для поиска стёкол)
        var tex = ReadableTexture(model);
        float Lum(Vector2 t)
        {
            if (tex == null) return 0.5f;
            var c = tex.GetPixelBilinear(t.x, t.y);
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        }

        // ---- треугольники: проём + разделение на кузов / стекло
        var body = new List<int>(); var glass = new List<int>();
        int AddVert(Vector3 p, Vector3 nn, Vector2 t) { v.Add(p); n.Add(nn); uv.Add(t); return v.Count - 1; }
        var candLum = new List<float>(); var bodyLum = new List<float>();
        var tris = new List<(int a, int b, int c, bool cand, float lum)>();
        int clipped = 0, removed = 0;
        for (int i = 0; i < st.Length; i += 3)
        {
            int a = st[i], bb = st[i + 1], c = st[i + 2];
            Vector3 ctr = (v[a] + v[bb] + v[c]) / 3f;
            Vector3 fn = Vector3.Cross(v[bb] - v[a], v[c] - v[a]).normalized;
            // задняя стенка в зоне проёма → вырезать прямоугольник
            if (ctr.z < DoorZ + 0.3f && fn.z < -0.55f && TriTouchesDoor(v[a], v[bb], v[c]))
            {
                var pieces = ClipOutsideDoor(new List<(Vector3 p, Vector3 n, Vector2 t)> { (v[a], n[a], uv[a]), (v[bb], n[bb], uv[bb]), (v[c], n[c], uv[c]) });
                clipped++;
                if (pieces.Count == 0) removed++;
                foreach (var poly in pieces)
                {
                    int i0 = AddVert(poly[0].p, poly[0].n, poly[0].t);
                    for (int k = 1; k < poly.Count - 1; k++)
                    {
                        int i1 = AddVert(poly[k].p, poly[k].n, poly[k].t), i2 = AddVert(poly[k + 1].p, poly[k + 1].n, poly[k + 1].t);
                        tris.Add((i0, i1, i2, false, 1f));
                    }
                }
                continue;
            }
            // кандидаты в стёкла: лобовое (смотрит вперёд, верх кабины) и боковые окна кабины
            bool windshield = ctr.z > FrontZ - 1.5f && fn.z > 0.25f && ctr.y > 0.48f * H && ctr.y < 0.95f * H;
            bool sideWin = Mathf.Abs(fn.x) > 0.6f && ctr.z > FrontZ - 1.75f && ctr.z < FrontZ - 0.2f && ctr.y > 0.5f * H && ctr.y < 0.9f * H;
            float lum = (Lum(uv[a]) + Lum(uv[bb]) + Lum(uv[c]) + Lum((uv[a] + uv[bb] + uv[c]) / 3f)) / 4f;
            bool cand = windshield || sideWin;
            (cand ? candLum : bodyLum).Add(lum);
            tris.Add((a, bb, c, cand, lum));
        }
        float bodyMed = bodyLum.Count > 0 ? bodyLum.OrderBy(x => x).ElementAt(bodyLum.Count / 2) : 0.5f;
        float thr = bodyMed * 0.8f;
        foreach (var t in tris) (t.cand && t.lum < thr ? glass : body).AddRange(new[] { t.a, t.b, t.c });
        log.AppendLine($"  doorway: {clipped} rear tris clipped ({removed} fully inside); glass {glass.Count / 3} tris (candidates {candLum.Count}, body lum median {bodyMed:F3}, threshold {thr:F3}, texture {(tex ? "ok" : "none")})");
        if (tex) Object.DestroyImmediate(tex);

        var mesh = new Mesh { name = "Bus_Modern_Body", indexFormat = IndexFormat.UInt32, subMeshCount = 2 };
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv);
        mesh.SetTriangles(body, 0); mesh.SetTriangles(glass, 1);
        mesh.RecalculateBounds();
        mesh = SaveMesh(mesh);
        var go = Part(ext, "Body", mesh, mBody, lExt);
        go.GetComponent<MeshRenderer>().sharedMaterials = new[] { mBody, mGlass };

        // ---- узкие LED-фары и задние фонари (поверх модели)
        float FrontAt(float y, float x) => v.Where(p => Mathf.Abs(p.y - y) < 0.1f && Mathf.Abs(Mathf.Abs(p.x) - x) < 0.18f).Select(p => p.z).DefaultIfEmpty(FrontZ).Max();
        float RearAt(float y, float x) => v.Where(p => Mathf.Abs(p.y - y) < 0.12f && Mathf.Abs(Mathf.Abs(p.x) - x) < 0.1f).Select(p => p.z).DefaultIfEmpty(RearZ).Min();
        float hy = 0.34f * H;
        var leds = new List<Renderer>();
        foreach (float sx in new[] { -1f, 1f })
        {
            float x = W / 2f - 0.42f;
            var led = Prim(PrimitiveType.Cube, ext, "LED_Headlight", new Vector3(sx * x, hy, FrontAt(hy, x) + 0.004f), new Vector3(0.42f, 0.03f, 0.03f), Quaternion.Euler(0, -sx * 12f, sx * 7f), mLed, lExt, false);
            leds.Add(led.GetComponent<Renderer>());
            float tx = W / 2f - 0.07f, ty = 0.47f * H;
            Prim(PrimitiveType.Cube, ext, "LED_Tail", new Vector3(sx * tx, ty, RearAt(ty, tx) - 0.01f), new Vector3(0.04f, 0.42f, 0.03f), Quaternion.identity, mTail, lExt, false);
        }
        headlightLeds = leds.ToArray();

        // ---- вход: синяя рамка проёма и ступенька (обычный слой — видны и изнутри в проёме)
        var door = new GameObject("Doorway").transform;
        door.SetParent(ext, false);
        float dz = DoorZ - 0.02f, dh = DoorTopY - FloorY;
        foreach (float sx in new[] { -1f, 1f })
            Prim(PrimitiveType.Cube, door, "DoorNeon_Side", new Vector3(sx * (DoorHalfW + 0.025f), FloorY + dh / 2f, dz), new Vector3(0.035f, dh + 0.05f, 0.035f), Quaternion.identity, mNeon, 0, false);
        Prim(PrimitiveType.Cube, door, "DoorNeon_Top", new Vector3(0, DoorTopY + 0.025f, dz), new Vector3(DoorHalfW * 2 + 0.085f, 0.035f, 0.035f), Quaternion.identity, mNeon, 0, false);
        Prim(PrimitiveType.Cube, door, "Step", new Vector3(0, 0.1f, DoorZ - 0.16f), new Vector3(DoorHalfW * 2 + 0.2f, 0.2f, 0.3f), Quaternion.identity, mStep, 0);
        MapKit.Font = MapKit.Font ? MapKit.Font : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MapKit.FontPath);
        var label = MapKit.Text(door, "DoorLabel", "ВХОД · НА ДЕЛО", 0.9f, new Color(0.55f, 0.8f, 1f), new Vector3(0, 0.1f, DoorZ - 0.311f), MapKit.Facing(Vector3.back), new Vector2(1.4f, 0.16f));
        label.GetComponent<Renderer>().allowOcclusionWhenDynamic = false;
    }

    static Renderer[] headlightLeds;

    static bool TriTouchesDoor(Vector3 a, Vector3 b, Vector3 c)
    {
        float minX = Mathf.Min(a.x, b.x, c.x), maxX = Mathf.Max(a.x, b.x, c.x), minY = Mathf.Min(a.y, b.y, c.y), maxY = Mathf.Max(a.y, b.y, c.y);
        return maxX > -DoorHalfW && minX < DoorHalfW && maxY > FloorY && minY < DoorTopY;
    }

    /// Части треугольника вне прямоугольника проёма (в проекции XY): режем по 4 прямым, отрезанное «снаружи» — оставляем.
    static List<List<(Vector3 p, Vector3 n, Vector2 t)>> ClipOutsideDoor(List<(Vector3 p, Vector3 n, Vector2 t)> tri)
    {
        var result = new List<List<(Vector3, Vector3, Vector2)>>();
        var pieces = new List<List<(Vector3 p, Vector3 n, Vector2 t)>> { tri };
        var cuts = new System.Func<Vector3, float>[]
        {
            p => -p.x - DoorHalfW, p => p.x - DoorHalfW, p => FloorY - p.y, p => p.y - DoorTopY,
        };
        foreach (var f in cuts)
        {
            var next = new List<List<(Vector3 p, Vector3 n, Vector2 t)>>();
            foreach (var poly in pieces)
            {
                Split(poly, f, out var outside, out var inside);
                if (outside.Count >= 3) result.Add(outside.Select(x => (x.p, x.n, x.t)).ToList());
                if (inside.Count >= 3) next.Add(inside);
            }
            pieces = next;
        }
        return result.Select(r => r.Select(x => (x.Item1, x.Item2, x.Item3)).ToList()).ToList();
    }

    /// Делит выпуклый многоугольник прямой f(p) = 0: outside — f > 0, inside — f ≤ 0.
    static void Split(List<(Vector3 p, Vector3 n, Vector2 t)> poly, System.Func<Vector3, float> f,
                      out List<(Vector3 p, Vector3 n, Vector2 t)> outside, out List<(Vector3 p, Vector3 n, Vector2 t)> inside)
    {
        outside = new List<(Vector3, Vector3, Vector2)>(); inside = new List<(Vector3, Vector3, Vector2)>();
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            float fa = f(a.p), fb = f(b.p);
            if (fa > 0) outside.Add(a); else inside.Add(a);
            if ((fa > 0) != (fb > 0))
            {
                float t = fa / (fa - fb);
                var m = (Vector3.Lerp(a.p, b.p, t), Vector3.Lerp(a.n, b.n, t).normalized, Vector2.Lerp(a.t, b.t, t));
                outside.Add(m); inside.Add(m);
            }
        }
    }

    /// Базовая текстура модели в читаемом виде (через RenderTexture, 512 px).
    static Texture2D ReadableTexture(GameObject model)
    {
        var mat = model.GetComponentInChildren<Renderer>()?.sharedMaterial;
        Texture src = null;
        if (mat) foreach (var name in mat.GetTexturePropertyNames()) { var t = mat.GetTexture(name); if (t && (name.ToLower().Contains("base") || name == "_MainTex")) { src = t; break; } }
        if (!src) src = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Texture2D>().FirstOrDefault();
        if (!src) return null;
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { log.AppendLine("  WARN: no GPU (batchmode -nographics) — glass by geometry only"); return null; }
        var rt = RenderTexture.GetTemporary(512, 512, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(512, 512, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); tex.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    // ---- рольставня входа, внутренняя дверь, фары с лучами, BusDeparture
    static void BuildDepartureRig(Transform root, int lExt, int lInt, int lPortal)
    {
        var ext = root.Find("Exterior");
        var dep = root.gameObject.AddComponent<BusDeparture>();
        dep.exterior = ext;
        float dh = DoorTopY - FloorY, dw = DoorHalfW * 2 + 0.08f;
        var shutterMat = Lit("M_Bus_Shutter", Hex("#16161A"), 0.6f, 0.4f, envReflections: false);

        // снаружи: рольставня (опора у верха проёма, тянется вниз)
        var pivot = new GameObject("RearShutter").transform;
        pivot.SetParent(ext.Find("Doorway"), false);
        pivot.localPosition = new Vector3(0, DoorTopY + 0.02f, DoorZ - 0.035f);
        int slats = 14;
        for (int i = 0; i < slats; i++)
            Prim(PrimitiveType.Cube, pivot, "Slat", new Vector3(0, -(i + 0.5f) * (dh + 0.04f) / slats, 0), new Vector3(dw, (dh + 0.04f) / slats * 0.92f, 0.025f), Quaternion.identity, shutterMat, 0, false);
        dep.outerDoor = pivot;
        dep.doorOpenLocal = dep.doorClosedLocal = pivot.localPosition;
        dep.doorOpenScale = new Vector3(1, 0.02f, 1); dep.doorClosedScale = Vector3.one;
        pivot.localScale = dep.doorOpenScale;
        dep.doorMask = root.Find("PortalMasks/DoorMask")?.GetComponent<Renderer>();

        // изнутри: закрытая дверь салона + маска (видна только из салона)
        var inner = new GameObject("InnerDoor").transform;
        inner.SetParent(root, false);
        var maskMb = new MB(1);
        maskMb.Quad(new Vector3(-DoorHalfW, FloorY, DoorZ + 0.002f), Vector3.right * 2 * DoorHalfW, Vector3.up * dh, Vector3.forward, 0);
        Part(inner, "InnerDoorMask", SaveMesh(maskMb.Build("Bus_InnerDoorMask")), mMask, lPortal, false);
        var panel = Prim(PrimitiveType.Cube, inner, "InnerDoorPanel", new Vector3(0, FloorY + dh / 2f, DoorZ + 0.015f), new Vector3(DoorHalfW * 2, dh, 0.02f), Quaternion.identity, mWall, lInt, false);
        panel.GetComponent<Renderer>().receiveShadows = false;
        inner.gameObject.SetActive(false);
        dep.innerDoor = inner.gameObject;

        // фары: мощные холодные LED-споты + мягкие объёмные лучи
        var lights = new List<Light>(); var beams = new List<Renderer>();
        var beamMat = BeamMaterial();
        var beamMesh = SaveMesh(ConeMesh("Bus_HeadlightBeam", 9f, 0.12f, 1.7f));
        float hy = 0.34f * H;
        foreach (var led in headlightLeds)
        {
            var p = led.transform.localPosition;
            var lgo = new GameObject("Headlight");
            lgo.transform.SetParent(ext, false);
            lgo.transform.localPosition = new Vector3(p.x, hy, p.z + 0.05f);
            lgo.transform.localRotation = Quaternion.Euler(4f, 0, 0);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Spot; l.color = new Color(0.82f, 0.9f, 1f); l.range = 34f; l.spotAngle = 52f; l.innerSpotAngle = 20f;
            l.shadows = LightShadows.None; l.intensity = 0f; l.enabled = false;
            lights.Add(l);
            var beam = Part(lgo.transform, "LightBeam", beamMesh, beamMat, 0, false);
            beam.GetComponent<Renderer>().enabled = false;
            beams.Add(beam.GetComponent<Renderer>());
        }
        dep.headlights = lights.ToArray(); dep.beams = beams.ToArray(); dep.ledStrips = headlightLeds;
        foreach (var r in headlightLeds) r.enabled = false;
        log.AppendLine("  departure: rear shutter, inner door, 2 headlights + beams");
    }

    static Material BeamMaterial()
    {
        string path = $"{MatDir}/M_Bus_HeadlightBeam.mat";
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetColor("_BaseColor", new Color(0.05f, 0.058f, 0.07f, 1f));   // аддитивно: мягкий холодный объём
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
        m.SetFloat("_SrcBlend", (float)BlendMode.One); m.SetFloat("_DstBlend", (float)BlendMode.One);
        m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", (float)CullMode.Off);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        return m;
    }

    /// Открытый конус вдоль +Z (луч фары): у фары — r0, на конце — r1; вершинный цвет гаснет к концу.
    static Mesh ConeMesh(string name, float len, float r0, float r1, int seg = 18)
    {
        var v = new List<Vector3>(); var col = new List<Color>(); var t = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2 / seg;
            var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
            v.Add(d * r0); col.Add(Color.white);
            v.Add(d * r1 + Vector3.forward * len); col.Add(Color.black);
        }
        for (int i = 0; i < seg; i++) { int a = i * 2; t.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 }); }
        var m = new Mesh { name = name };
        m.SetVertices(v); m.SetColors(col); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    // =========================================================================== салон
    class MB
    {
        public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector3> n = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int>[] t;
        public MB(int sub) { t = Enumerable.Range(0, sub).Select(_ => new List<int>()).ToArray(); }

        /// Прямоугольник по двум осям (a — от p0, b — от p0), нормаль normal; UV в метрах.
        public void Quad(Vector3 p0, Vector3 a, Vector3 b, Vector3 normal, int sub)
        {
            int i = v.Count;
            Vector3[] q = { p0, p0 + a, p0 + a + b, p0 + b };
            Vector3 ua = a.normalized, ub = b.normalized;
            foreach (var p in q) { v.Add(p); n.Add(normal); uv.Add(new Vector2(Vector3.Dot(p, ua), Vector3.Dot(p, ub))); }
            if (Vector3.Dot(Vector3.Cross(a, b), normal) > 0) t[sub].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else t[sub].AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name, subMeshCount = t.Length };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            for (int s = 0; s < t.Length; s++) m.SetTriangles(t[s], s);
            m.RecalculateBounds();
            return m;
        }
    }

    /// Коробка салона гранями внутрь: пол (submesh 0), стены/потолок/торцы (submesh 1 или 0, если single).
    static void Shell(MB mb, float z0, float z1, bool rear, bool far, bool single)
    {
        int walls = single ? 0 : 1;
        float hw = InHalfW, len = z1 - z0, h = CeilY - FloorY;
        mb.Quad(new Vector3(-hw, FloorY, z0), Vector3.right * 2 * hw, Vector3.forward * len, Vector3.up, 0);
        mb.Quad(new Vector3(-hw, CeilY, z0), Vector3.right * 2 * hw, Vector3.forward * len, Vector3.down, walls);
        mb.Quad(new Vector3(-hw, FloorY, z0), Vector3.forward * len, Vector3.up * h, Vector3.right, walls);
        mb.Quad(new Vector3(hw, FloorY, z0), Vector3.forward * len, Vector3.up * h, Vector3.left, walls);
        if (far) mb.Quad(new Vector3(-hw, FloorY, z1), Vector3.right * 2 * hw, Vector3.up * h, Vector3.back, walls);
        if (rear)
        {
            mb.Quad(new Vector3(-hw, FloorY, z0), Vector3.right * (hw - DoorHalfW), Vector3.up * h, Vector3.forward, walls);
            mb.Quad(new Vector3(DoorHalfW, FloorY, z0), Vector3.right * (hw - DoorHalfW), Vector3.up * h, Vector3.forward, walls);
            mb.Quad(new Vector3(-DoorHalfW, DoorTopY, z0), Vector3.right * 2 * DoorHalfW, Vector3.up * (CeilY - DoorTopY), Vector3.forward, walls);
        }
    }

    static Renderer BuildInterior(Transform root, int lInt, int lPortal)
    {
        var inside = new GameObject("Interior").transform;
        inside.SetParent(root, false);

        // 3 секции (у каждой свои ближайшие лампы — лимит доп. источников на объект на Quest)
        float[] zs = { DoorZ, DoorZ + InteriorLen / 3f, DoorZ + InteriorLen * 2f / 3f, InteriorEndZ };
        for (int s = 0; s < 3; s++)
        {
            var mb = new MB(2);
            Shell(mb, zs[s], zs[s + 1], s == 0, s == 2, false);
            var go = Part(inside, $"Shell_{s}", SaveMesh(mb.Build($"Bus_InteriorShell_{s}")), mFloor, lInt, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterials = new[] { mFloor, mWall };
            r.receiveShadows = false;
        }

        // синие LED-профили по всем рёбрам салона
        var led = new GameObject("NeonEdges").transform;
        led.SetParent(inside, false);
        const float s0 = 0.05f, e = s0 / 2f;
        float hw = InHalfW, len = InteriorLen, h = CeilY - FloorY, zc = (DoorZ + InteriorEndZ) / 2f;
        void Bar(string n, Vector3 c, Vector3 size) => Prim(PrimitiveType.Cube, led, n, c, size, Quaternion.identity, mNeon, lInt, false).GetComponent<Renderer>().receiveShadows = false;
        foreach (float sx in new[] { -1f, 1f })
        {
            Bar("Neon_FloorEdge", new Vector3(sx * (hw - e), FloorY + e, zc), new Vector3(s0, s0, len));
            Bar("Neon_CeilEdge", new Vector3(sx * (hw - e), CeilY - e, zc), new Vector3(s0, s0, len));
            Bar("Neon_FarCorner", new Vector3(sx * (hw - e), FloorY + h / 2f, InteriorEndZ - e), new Vector3(s0, h, s0));
            Bar("Neon_RearCorner", new Vector3(sx * (hw - e), FloorY + h / 2f, DoorZ + e), new Vector3(s0, h, s0));
            Bar("Neon_RearFloor", new Vector3(sx * (hw + DoorHalfW) / 2f, FloorY + e, DoorZ + e), new Vector3(hw - DoorHalfW, s0, s0));
            Bar("Neon_DoorInside", new Vector3(sx * (DoorHalfW + e), FloorY + (DoorTopY - FloorY) / 2f, DoorZ + e), new Vector3(s0, DoorTopY - FloorY, s0));
        }
        Bar("Neon_FarFloor", new Vector3(0, FloorY + e, InteriorEndZ - e), new Vector3(2 * hw, s0, s0));
        Bar("Neon_FarCeil", new Vector3(0, CeilY - e, InteriorEndZ - e), new Vector3(2 * hw, s0, s0));
        Bar("Neon_RearCeil", new Vector3(0, CeilY - e, DoorZ + e), new Vector3(2 * hw, s0, s0));
        Bar("Neon_DoorTopInside", new Vector3(0, DoorTopY + e, DoorZ + e), new Vector3(2 * DoorHalfW + 2 * s0, s0, s0));

        // мягкий синий свет: только салон и игроки (на соседний пол хаба не «протекает»)
        int mask = (1 << lInt) | (1 << LayerMask.NameToLayer("Player")) | (1 << LayerMask.NameToLayer("LocalHead"));
        int li = 0;
        foreach (float lz in new[] { -1.9f, 0.9f, 3.7f, 6.5f })
        {
            var l = MapKit.PointLight(inside, $"BlueLight_{li++}", new Vector3(0, CeilY - 0.3f, lz), LightCobalt, LightIntensity, 3.4f);
            l.cullingMask = mask;
            l.renderMode = LightRenderMode.Auto;
        }

        // надпись-табло на передней стенке салона (туда переезжает отсчёт VanPortal)
        // маски Stencil
        var masks = new GameObject("PortalMasks").transform;
        masks.SetParent(root, false);
        var doorMb = new MB(1);
        doorMb.Quad(new Vector3(-DoorHalfW, FloorY, DoorZ - 0.003f), Vector3.right * 2 * DoorHalfW, Vector3.up * (DoorTopY - FloorY), Vector3.back, 0);
        Part(masks, "DoorMask", SaveMesh(doorMb.Build("Bus_DoorMask")), mMask, lPortal, false).GetComponent<Renderer>().receiveShadows = false;
        var inMb = new MB(1);
        Shell(inMb, DoorZ, InteriorEndZ, true, true, true);
        var im = Part(masks, "InsideMask", SaveMesh(inMb.Build("Bus_InsideMask")), mMask, lPortal, false).GetComponent<Renderer>();
        im.receiveShadows = false;
        return im;
    }

    // =========================================================================== коллайдеры
    static void BuildColliders(Transform root)
    {
        var col = new GameObject("Colliders").transform;
        col.SetParent(root, false);
        col.gameObject.layer = 0;
        float outer = Outer, zEnd = InteriorEndZ + 0.12f, zc = (DoorZ + zEnd) / 2f, len = zEnd - DoorZ;
        void B(string n, Vector3 c, Vector3 s)
        {
            var go = new GameObject(n); go.transform.SetParent(col, false); go.transform.localPosition = c;
            go.AddComponent<BoxCollider>().size = s;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }
        float sideW = outer - InHalfW;
        foreach (float sx in new[] { -1f, 1f })
            B(sx < 0 ? "Side_L" : "Side_R", new Vector3(sx * (InHalfW + sideW / 2f), H / 2f, zc), new Vector3(sideW, H, len));
        B("Roof", new Vector3(0, (CeilY + H + 0.05f) / 2f, zc), new Vector3(outer * 2, H + 0.05f - CeilY, len));
        B("Floor", new Vector3(0, (FloorY - 0.2f) / 2f, zc), new Vector3(outer * 2, FloorY + 0.2f, len));
        B("FarWall", new Vector3(0, H / 2f, InteriorEndZ + 0.06f), new Vector3(outer * 2, H, 0.12f));
        float rw = outer - DoorHalfW;
        foreach (float sx in new[] { -1f, 1f })
            B(sx < 0 ? "Rear_L" : "Rear_R", new Vector3(sx * (DoorHalfW + rw / 2f), H / 2f, DoorZ - 0.01f), new Vector3(rw, H, 0.1f));
        B("Rear_Top", new Vector3(0, (DoorTopY + H) / 2f, DoorZ - 0.01f), new Vector3(DoorHalfW * 2, H - DoorTopY, 0.1f));
        B("Step", new Vector3(0, 0.1f, DoorZ - 0.16f), new Vector3(DoorHalfW * 2 + 0.2f, 0.2f, 0.3f));
    }

    /// Внешний габарит коллайдеров: салон (на 20% шире) шире кузова — стенки салона чуть выступают за борта.
    static float Outer => Mathf.Max(W / 2f, InHalfW + 0.12f) + 0.02f;

    /// Проверка проходимости: от ступеньки сквозь проём до конца салона, стены держат, пол везде.
    static string TestColliders(Transform root)
    {
        Physics.SyncTransforms();
        var sb = new StringBuilder("  collider test:");
        int mask = ~(1 << 2);   // как у GorillaLocomotion: всё, кроме Ignore Raycast
        Vector3 W2(float x, float y, float z) => root.TransformPoint(new Vector3(x, y, z));
        // 1) капсула «гориллы» проходит центром салона от проёма до дальней стенки
        int blocked = 0; float firstBlock = float.NaN;
        for (float z = DoorZ + 0.4f; z < InteriorEndZ - 0.3f; z += 0.25f)
            if (Physics.CheckCapsule(W2(0, FloorY + 0.35f, z), W2(0, FloorY + 1.55f, z), 0.3f, mask, QueryTriggerInteraction.Ignore))
            { blocked++; if (float.IsNaN(firstBlock)) firstBlock = z; }
        sb.Append(blocked == 0 ? " path=OK" : $" path=BLOCKED x{blocked} (first z={firstBlock:F2}) ERROR");
        // 2) сквозь проём снаружи: луч на высоте головы проходит внутрь до дальней стенки
        var from = W2(0, FloorY + 1.4f, DoorZ - 1.5f);
        bool hit = Physics.Raycast(from, root.forward, out var h, 30f, mask, QueryTriggerInteraction.Ignore);
        float depth = hit ? root.InverseTransformPoint(h.point).z : float.NaN;
        sb.Append(hit && Mathf.Abs(depth - InteriorEndZ) < 0.05f ? $" door→far wall OK ({depth - DoorZ:F2} m)" : $" door ray hit z={depth:F2} ({(hit ? h.collider.name : "nothing")}) ERROR");
        // 3) мимо проёма корма держит
        bool rearHit = Physics.Raycast(W2(-1.0f, 1.2f, DoorZ - 1f), root.forward, out var rh, 2f, mask, QueryTriggerInteraction.Ignore);
        sb.Append(rearHit ? $" rear wall OK ({rh.collider.name})" : " rear wall MISSING ERROR");
        // 4) пол под ногами по всей длине (в том числе за северной стеной хаба)
        int noFloor = 0;
        for (float z = DoorZ + 0.2f; z < InteriorEndZ; z += 0.5f)
            if (!Physics.Raycast(W2(0.6f, 1.5f, z), Vector3.down, out var fh, 2f, mask, QueryTriggerInteraction.Ignore) || Mathf.Abs(fh.point.y - FloorY) > 0.02f) noFloor++;
        sb.Append(noFloor == 0 ? " floor OK" : $" floor gaps x{noFloor} ERROR");
        // 5) боковые стенки держат изнутри
        bool sideHit = Physics.Raycast(W2(0, 1.2f, InteriorEndZ - 1f), root.right, out var sh, 3f, mask, QueryTriggerInteraction.Ignore);
        sb.Append(sideHit && Mathf.Abs(root.InverseTransformPoint(sh.point).x - InHalfW) < 0.03f ? " side walls OK" : " side wall ERROR");
        // 6) северная стена хаба вне коридора по-прежнему твёрдая
        bool wallHit = Physics.Raycast(new Vector3(0f, 1.2f, 12.5f), Vector3.forward, out var wh, 3f, mask, QueryTriggerInteraction.Ignore);
        sb.Append(wallHit ? $" north wall OK ({wh.collider.name})" : " north wall MISSING ERROR");
        return sb.ToString();
    }

    // =========================================================================== правки сцены
    /// Северная стена хаба: рисуется слоем BusExterior (в проёме автобуса не видна), а коллайдер
    /// разрезан на три части — оставлен коридор под салон.
    static void FixNorthWall(Transform map, int lExt)
    {
        var wall = map.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Wall_N");
        if (!wall) { log.AppendLine("ERROR: Wall_N not found"); return; }
        wall.gameObject.layer = lExt;
        foreach (var bc in wall.GetComponents<BoxCollider>()) Object.DestroyImmediate(bc);
        var old = wall.parent.Find("Wall_N_Colliders");
        if (old) Object.DestroyImmediate(old.gameObject);
        var cols = new GameObject("Wall_N_Colliders").transform;
        cols.SetParent(wall.parent, false);
        cols.position = Vector3.zero; cols.rotation = Quaternion.identity; cols.localScale = Vector3.one;
        var b = wall.GetComponent<Renderer>().bounds;
        // въезд в тоннель (HubTunnelBuilder) — проём в стене; по бокам от автобуса и над ним — невидимые барьеры
        float mx0 = BusPos.x - HubTunnelBuilder.HalfW, mx1 = BusPos.x + HubTunnelBuilder.HalfW, mTop = HubTunnelBuilder.Height;
        float gx0 = BusPos.x - Outer, gx1 = BusPos.x + Outer, gTop = H + 0.05f;
        void C(Vector3 min, Vector3 max)
        {
            var bc = cols.gameObject.AddComponent<BoxCollider>();
            bc.center = cols.InverseTransformPoint((min + max) / 2f);
            bc.size = max - min;
        }
        C(new Vector3(b.min.x, b.min.y, b.min.z), new Vector3(mx0, b.max.y, b.max.z));          // стена слева от въезда
        C(new Vector3(mx1, b.min.y, b.min.z), new Vector3(b.max.x, b.max.y, b.max.z));          // справа
        C(new Vector3(mx0, mTop, b.min.z), new Vector3(mx1, b.max.y, b.max.z));                 // над въездом
        C(new Vector3(mx0, b.min.y, b.min.z), new Vector3(gx0, mTop, b.max.z));                 // барьер слева от автобуса
        C(new Vector3(gx1, b.min.y, b.min.z), new Vector3(mx1, mTop, b.max.z));                 // справа
        C(new Vector3(gx0, gTop, b.min.z), new Vector3(gx1, mTop, b.max.z));                    // над автобусом
        GameObjectUtility.SetStaticEditorFlags(cols.gameObject, StaticEditorFlags.BatchingStatic);
        log.AppendLine($"  Wall_N → layer {LayerExterior}, tunnel mouth x {mx0:F2}..{mx1:F2} y < {mTop:F2}, bus corridor x {gx0:F2}..{gx1:F2} y < {gTop:F2}");
    }

    /// Зона посадки — салон (от 1 м за проёмом до конца), табло отсчёта — на передней стенке салона.
    static void FixVanPortal(Transform map, Transform bus)
    {
        var vp = map.GetComponentsInChildren<VanPortal>(true).FirstOrDefault();
        if (!vp) { log.AppendLine("ERROR: VanPortal not found"); return; }
        float z0 = DoorZ + 1.0f, z1 = InteriorEndZ - 0.05f;
        vp.transform.SetPositionAndRotation(bus.TransformPoint(new Vector3(0, (FloorY + CeilY) / 2f, (z0 + z1) / 2f)), bus.rotation);
        vp.transform.localScale = Vector3.one;
        var box = vp.GetComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(InHalfW * 2f, CeilY - FloorY + 0.6f, z1 - z0);
        vp.bus = bus.GetComponent<BusDeparture>();
        EditorUtility.SetDirty(vp); EditorUtility.SetDirty(box);

        var txt = vp.countdownText;
        if (txt)
        {
            txt.text = "ВСЕ В САЛОН — ЕДЕМ НА ДЕЛО!";
            var rt = txt.rectTransform;
            var world = bus.TransformPoint(new Vector3(0, 1.75f, InteriorEndZ - 0.02f));
            var local = rt.parent ? rt.parent.InverseTransformPoint(world) : world;
            rt.anchoredPosition3D = local; rt.localPosition = local;
            rt.rotation = bus.rotation * MapKit.Facing(Vector3.back);
            rt.sizeDelta = new Vector2(InHalfW * 2f - 0.2f, 0.5f);
            txt.fontSize = 2.2f; txt.fontSizeMax = 2.2f;
            txt.color = new Color(0.55f, 0.85f, 1f);
            txt.GetComponent<Renderer>().allowOcclusionWhenDynamic = false;
            txt.gameObject.layer = LayerMask.NameToLayer(LayerInterior);   // висит в тоннеле — видно только из салона/сквозь проём
            EditorUtility.SetDirty(txt);
        }
        var hint = map.GetComponentsInChildren<TextMeshPro>(true).FirstOrDefault(t => t.name == "ModeBoard_Hint");
        if (hint && (hint.text.Contains("ФУРГОН") || hint.text.Contains("ЧЁРНЫЙ АВТОБУС")))
        {
            hint.text = hint.text.Replace("ЗАПРЫГНИ В ФУРГОН", "ЗАЙДИ В АВТОБУС").Replace("ЗАЙДИ В ЧЁРНЫЙ АВТОБУС", "ЗАЙДИ В АВТОБУС");
            EditorUtility.SetDirty(hint);
        }
        // стартовая табличка у спавна тоже говорила про фургон
        foreach (var t in map.GetComponentsInChildren<TextMeshPro>(true).Where(t => t.text.Contains("ЗАПРЫГНИ В ФУРГОН")))
        {
            t.text = t.text.Replace("ЗАПРЫГНИ В ФУРГОН", "ЗАЙДИ В АВТОБУС");
            EditorUtility.SetDirty(t);
        }
        log.AppendLine($"  VanPortal zone {box.size:F2} at {vp.transform.position:F2}; countdown on the far wall");
    }

    /// Разметка парковки под новый автобус; фары/фонари старого джипа больше не нужны.
    static void FixVanDecor(UnityEngine.SceneManagement.Scene scene, Transform bus)
    {
        var decor = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "HubDecor");
        var vd = decor ? decor.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "VanDecor") : null;
        if (!vd) { log.AppendLine("  VanDecor not found — skipped"); return; }
        foreach (var t in vd.Cast<Transform>().Where(t => t.name == "Headlight" || t.name == "Taillight").ToArray()) Object.DestroyImmediate(t.gameObject);
        Bounds b = BusFootprint(bus);
        float m = 0.45f, y = 0.008f;
        float x0 = b.min.x - m, x1 = b.max.x + m, z0 = b.min.z - m - 0.3f, z1 = Mathf.Min(b.max.z + m, 13.6f);
        int dash = 0;
        foreach (Transform t in vd)
        {
            switch (t.name)
            {
                case "Park_L": t.position = new Vector3(x0, y, (z0 + z1) / 2); t.localScale = new Vector3(0.14f, 0.01f, z1 - z0); break;
                case "Park_R": t.position = new Vector3(x1, y, (z0 + z1) / 2); t.localScale = new Vector3(0.14f, 0.01f, z1 - z0); break;
                case "Park_F": t.position = new Vector3((x0 + x1) / 2, y, z0); t.localScale = new Vector3(x1 - x0, 0.01f, 0.14f); break;
                case "Lane_Dash": t.position = new Vector3((x0 + x1) / 2, y, z0 - 1.2f - dash++ * 1.3f); break;
            }
        }
        log.AppendLine($"  VanDecor: parking frame x {x0:F2}..{x1:F2}, z {z0:F2}..{z1:F2}");
    }

    /// Габарит кузова в мире (без салона, который уходит за стену).
    public static Bounds BusFootprint(Transform bus)
    {
        var body = bus.Find("Exterior/Body");
        return body ? body.GetComponent<Renderer>().bounds : new Bounds(bus.position + Vector3.up * H / 2f, new Vector3(W, H, L));
    }

    // =========================================================================== превью
    /// Снимки автобуса (снаружи, в проём, изнутри) — проверка без шлема. Batchmode без -nographics.
    public static void CapturePreview()
    {
        EditorSceneManager.OpenScene(HubPath, OpenSceneMode.Single);
        var bus = GameObject.Find(RootName).transform;
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "claude", "bus_preview");
        System.IO.Directory.CreateDirectory(dir);
        Vector3 P(float x, float y, float z) => bus.TransformPoint(new Vector3(x, y, z));
        var shots = new (string name, Vector3 pos, Vector3 look)[]
        {
            ("01_rear_door", P(0.4f, 1.75f, DoorZ - 4.5f), P(0, 1.3f, DoorZ + 3f)),
            ("02_side_front", P(-5.5f, 1.8f, 1.5f), P(0, 1.1f, 1.2f)),
            ("03_threequarter", P(-4.5f, 2.2f, -6.5f), P(0, 1.0f, 0.5f)),
            ("04_doorway_close", P(-0.3f, 1.6f, DoorZ - 1.2f), P(0.2f, 1.2f, InteriorEndZ)),
            ("05_inside_forward", P(0f, 1.85f, DoorZ + 1.2f), P(0, 1.3f, InteriorEndZ)),
            ("06_inside_back", P(0.2f, 1.85f, InteriorEndZ - 1.0f), P(0, 1.2f, DoorZ)),
            ("07_north_wall_side", P(-3.5f, 1.8f, 0f), P(0, 1.5f, FrontZ + 2f)),
            ("08_top", P(-6f, 6.5f, -7f), P(0, 0.5f, 1f)),
            ("09_front_corner", P(-3.4f, 1.5f, 1.6f), P(0f, 1.5f, 3.0f)),
            ("12_tunnel_mouth", P(-1.5f, 3.2f, -9f), P(0f, 2f, FrontZ + 4f)),
            ("13_tunnel_inside", P(0.8f, 3.4f, FrontZ + 1.0f), P(0f, 1.5f, FrontZ + 25f)),
        };
        var go = new GameObject("BusPreviewCam");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 75f; cam.nearClipPlane = 0.03f; cam.farClipPlane = 100f; cam.allowHDR = true;
        cam.cullingMask = ~(1 << 9);
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
        // выезд: ставня закрыта, фары горят, кузов уехал на 7 м в тоннель (только для снимка, сцена не сохраняется)
        var dep = bus.GetComponent<BusDeparture>();
        if (dep)
        {
            if (dep.outerDoor) dep.outerDoor.localScale = Vector3.one;
            foreach (var l in dep.headlights) { l.enabled = true; l.intensity = dep.headlightIntensity; }
            foreach (var r in dep.beams) r.enabled = true;
            foreach (var r in dep.ledStrips) r.enabled = true;
            var mask = bus.Find("PortalMasks/DoorMask")?.GetComponent<Renderer>(); if (mask) mask.enabled = false;
            foreach (var (n, z) in new[] { ("10_departure_start", 0f), ("11_departure_tunnel", 7f) })
            {
                dep.exterior.localPosition = new Vector3(0, 0, z);
                var pos = P(-2.2f, 2.0f, DoorZ - 5.5f); var look = P(0, 1.2f, FrontZ + 6f);
                go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
                cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, n + ".png"), tex.EncodeToPNG());
            }
        }
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(go); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        Debug.Log("[BusPreview] saved to " + dir);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
