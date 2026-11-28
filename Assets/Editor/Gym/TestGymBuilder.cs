using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Fusion;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 1) Tools/Heist/Player/Locomotion Physics (GT feel) — эталонная лёгкая физика GorillaLocomotion в префабе VRGorillaPlayer
///    (идемпотентно: только числа, материал без трения на теле/голове, коллайдеры игрока вне слоёв локомоции).
/// 2) Tools/Heist/Gym/Build Test Gym — сцена-полигон Assets/Scenes/TestGym.unity (+ Build Settings, индекс 2).
///    Сцена ЦЕЛИКОМ генерируется этим скриптом: повторный запуск пересоздаёт её (ручные правки в TestGym пропадут).
///    Основа — копия MainHub (игроки, PlayerModeSwitcher, XR-симулятор, FusionNetworkManager, зеркало), всё остальное
///    из хаба удаляется. Зоны: полоса препятствий, стенд физики (± прыжок/скорость/гравитация), зона ограбления
///    (дрель + сейф, щиток с 3 рубильниками, лазеры, охранник с шокером, лут разного веса), зеркало, портал в MainHub.
/// Batchmode (без -nographics): -executeMethod TestGymBuilder.RunBatch
/// </summary>
public static class TestGymBuilder
{
    const string HubPath = "Assets/Scenes/MainHub.unity", GymPath = "Assets/Scenes/TestGym.unity";
    const string PlayerPrefab = "Assets/Prefabs/Player/VRGorillaPlayer.prefab";
    const string MatDir = "Assets/Art/Materials/Gym", TexDir = "Assets/Art/Textures/Gym", MeshDir = "Assets/Art/Meshes/Gym";
    const string BodyMatPath = "Assets/Art/Physics/PM_PlayerBody.physicMaterial";
    static readonly StringBuilder log = new StringBuilder();
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
    static AudioClip[] clickClips;
    static Transform root;
    static int ignoreRaycast;

    [MenuItem("Tools/Heist/Player/Locomotion Physics (GT feel)")]
    static void MenuPhysics() => Debug.Log(ApplyLocomotion());

    [MenuItem("Tools/Heist/Gym/Build Test Gym")]
    static void MenuGym() => Debug.Log(Run());

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
        log.Clear(); mats.Clear(); meshes.Clear();
        log.AppendLine("[TestGymBuilder]");
        log.Append(ApplyLocomotion());
        BuildGym();
        return log.ToString();
    }

    // ================================================================== 2. физика передвижения в префабе
    public static string ApplyLocomotion()
    {
        var sb = new StringBuilder("[Locomotion GT feel]\n");
        if (!AssetDatabase.IsValidFolder("Assets/Art/Physics")) AssetDatabase.CreateFolder("Assets/Art", "Physics");
        var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BodyMatPath);
        if (!pm) { pm = new PhysicsMaterial("PM_PlayerBody"); AssetDatabase.CreateAsset(pm, BodyMatPath); }
        pm.dynamicFriction = 0f; pm.staticFriction = 0f; pm.bounciness = 0f;
        pm.frictionCombine = PhysicsMaterialCombine.Minimum; pm.bounceCombine = PhysicsMaterialCombine.Minimum;
        EditorUtility.SetDirty(pm);

        var go = PrefabUtility.LoadPrefabContents(PlayerPrefab);
        try
        {
            var p = go.GetComponentInChildren<GorillaLocomotion.Player>(true);
            if (!p) { sb.AppendLine("  ERROR: no GorillaLocomotion.Player in prefab"); return sb.ToString(); }
            sb.AppendLine($"  before: history {p.velocityHistorySize}, jump ×{p.jumpMultiplier}, maxJumpSpeed {p.maxJumpSpeed}, hand sphere r {p.minimumRaycastDistance}, velocityLimit {p.velocityLimit}");
            p.velocityHistorySize = 4;                      // было 8: толчок усреднялся по 8 кадрам (с нулевыми) — «вешу 500 кг»
            p.jumpMultiplier = LocomotionTuning.DefaultJump;
            p.maxJumpSpeed = LocomotionTuning.DefaultSpeed;
            p.minimumRaycastDistance = 0.075f;              // радиус сферы руки
            p.velocityLimit = 0.3f;
            p.gravityScale = LocomotionTuning.DefaultGravity;
            p.groundDamping = 8f; p.groundCheckDistance = 0.08f; p.applySavedTuning = true;
            sb.AppendLine($"  after:  history {p.velocityHistorySize}, jump ×{p.jumpMultiplier}, maxJumpSpeed {p.maxJumpSpeed}, hand sphere r {p.minimumRaycastDistance}, groundDamping {p.groundDamping}");
            var rb = p.GetComponent<Rigidbody>();
            if (rb) { rb.linearDamping = 0f; rb.angularDamping = 0.05f; }
            if (p.bodyCollider) p.bodyCollider.sharedMaterial = pm;
            if (p.headCollider) p.headCollider.sharedMaterial = pm;
            // руки не должны цепляться за коллайдеры самого игрока: всё твёрдое внутри игрока — вне слоёв локомоции
            int playerLayer = LayerMask.NameToLayer("Player"), moved = 0;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue;
                if ((p.locomotionEnabledLayers.value & (1 << c.gameObject.layer)) == 0) continue;
                sb.AppendLine($"  moved solid collider {c.name} from layer {LayerMask.LayerToName(c.gameObject.layer)} to Player");
                c.gameObject.layer = playerLayer; moved++;
            }
            var solids = go.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).Select(c => $"{c.name}[{LayerMask.LayerToName(c.gameObject.layer)}]");
            sb.AppendLine($"  solid colliders: {string.Join(", ", solids)}; locomotion layers mask {p.locomotionEnabledLayers.value}; moved {moved}");
            PrefabUtility.SaveAsPrefabAsset(go, PlayerPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(go); }
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    // ================================================================== 3. полигон
    static void BuildGym()
    {
        foreach (var d in new[] { MatDir, TexDir }) EnsureFolder(d);
        if (AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.DeleteAsset(MeshDir);
        EnsureFolder(MeshDir);
        ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        CreateMaterials();

        if (File.Exists(GymPath)) AssetDatabase.DeleteAsset(GymPath);
        if (!AssetDatabase.CopyAsset(HubPath, GymPath)) { log.AppendLine("  ERROR: copy MainHub → TestGym failed"); return; }
        var scene = EditorSceneManager.OpenScene(GymPath, OpenSceneMode.Single);

        var anyButton = Object.FindObjectsByType<PushButton>(FindObjectsInactive.Include).FirstOrDefault(b => b.clickClips != null && b.clickClips.Length > 0);
        clickClips = anyButton ? anyButton.clickClips : new AudioClip[0];
        var mirror = GameObject.Find("MainHubMap/Dynamic/Mirror");
        if (mirror) mirror.transform.SetParent(null, true); else log.AppendLine("  ERROR: hub mirror not found");

        var keep = new HashSet<string> { "Directional Light", "DesktopTestPlayer", "VRGorillaPlayer", "XR Device Simulator", "PlayerModeSwitcher", "FusionNetworkManager", "Mirror" };
        int removed = 0;
        foreach (var g in scene.GetRootGameObjects()) if (!keep.Contains(g.name)) { Object.DestroyImmediate(g); removed++; }
        log.AppendLine($"  scene: copied from MainHub, removed {removed} hub roots, kept {string.Join(", ", scene.GetRootGameObjects().Select(g => g.name))}");

        root = new GameObject("TestGym").transform;
        Environment();
        Arena();
        SpawnAndPortal(mirror);
        TuningStand();
        ObstacleCourse();
        HeistZone();

        foreach (var p in new[] { "DesktopTestPlayer", "VRGorillaPlayer" })
        {
            var g = scene.GetRootGameObjects().FirstOrDefault(x => x.name == p);
            if (g) g.transform.SetPositionAndRotation(new Vector3(0f, 0.05f, -6f), Quaternion.identity); else log.AppendLine("  ERROR: player missing " + p);
        }

        // сетевые объекты сцены — Master Client Object (хост комнаты держит состояние)
        foreach (var nb in Object.FindObjectsByType<NetworkBehaviour>(FindObjectsInactive.Include).Where(b => b is SafeDrill || b is BreakerPanel))
        {
            var no = nb.GetComponent<NetworkObject>();
            var f = no.Flags.SetCurrentVersion(); f |= NetworkObjectFlags.MasterClientObject;
            f &= ~NetworkObjectFlags.AllowStateAuthorityOverride; f &= ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
            no.Flags = f; EditorUtility.SetDirty(no);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorApplication.ExecuteMenuItem("Tools/Fusion/Scene/Bake Scene Objects");
        if (!EditorSceneManager.SaveScene(scene)) log.AppendLine("  ERROR: TestGym save failed");
        var nos = Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include);
        log.AppendLine($"  NetworkObjects {nos.Length}, baked {nos.Count(n => n.SortKey != 0)}");
        if (nos.Any(n => n.SortKey == 0)) log.AppendLine("  ERROR: not all scene NetworkObjects are baked");

        AddToBuildSettings();
        log.AppendLine($"  TestGym: renderers {root.GetComponentsInChildren<Renderer>().Length}, colliders {root.GetComponentsInChildren<Collider>().Length}, lights {Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude).Length}");
        RenderShots();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/'); EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static void AddToBuildSettings()
    {
        var list = EditorBuildSettings.scenes.ToList();
        if (!list.Any(s => s.path == GymPath)) { list.Add(new EditorBuildSettingsScene(GymPath, true)); EditorBuildSettings.scenes = list.ToArray(); }
        log.AppendLine("  build settings: " + string.Join(", ", EditorBuildSettings.scenes.Select((s, i) => $"{i}:{Path.GetFileNameWithoutExtension(s.path)}{(s.enabled ? "" : "(off)")}")));
    }

    // ================================================================== материалы и текстура «лабораторной клетки»
    static Texture2D GridTexture()
    {
        string path = $"{TexDir}/T_Gym_Grid.png";
        const int N = 512;                                       // 1 текстура = 1 × 1 м
        var tex = new Texture2D(N, N, TextureFormat.RGB24, true);
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float v = 0.86f;
                bool major = x < 5 || y < 5 || x >= N - 5 || y >= N - 5;
                bool minor = Mathf.Abs(x - N / 2) < 2 || Mathf.Abs(y - N / 2) < 2;
                bool quarter = (x % (N / 4) < 1 || y % (N / 4) < 1);
                if (major) v = 0.52f; else if (minor) v = 0.7f; else if (quarter) v = 0.8f;
                byte b = (byte)(v * 255); px[y * N + x] = new Color32(b, b, b, 255);
            }
        tex.SetPixels32(px); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.wrapMode = TextureWrapMode.Repeat; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 8; ti.mipmapEnabled = true; ti.maxTextureSize = 512;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Mat(string name, Color c, Texture tex, Color? emission = null, float smooth = 0.1f, float metal = 0f, bool unlitShadowless = false)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c); m.SetTexture("_BaseMap", tex); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
        if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        else m.DisableKeyword("_EMISSION");
        m.enableInstancing = true; EditorUtility.SetDirty(m); mats[name] = m; return m;
    }
    static Material M(string n) => mats[n];

    static void CreateMaterials()
    {
        var grid = GridTexture();
        Mat("Floor", new Color(0.92f, 0.93f, 0.95f), grid);
        Mat("Wall", new Color(0.97f, 0.97f, 0.98f), grid);
        Mat("Orange", new Color(1f, 0.58f, 0.22f), grid);
        Mat("Blue", new Color(0.42f, 0.66f, 1f), grid);
        Mat("Green", new Color(0.45f, 0.86f, 0.5f), grid);
        Mat("Yellow", new Color(1f, 0.86f, 0.3f), grid);
        Mat("Purple", new Color(0.7f, 0.5f, 0.95f), grid);
        Mat("Dark", new Color(0.18f, 0.19f, 0.22f), null, null, 0.3f);
        Mat("Steel", new Color(0.55f, 0.57f, 0.6f), null, null, 0.45f, 0.7f);
        Mat("SafeBody", new Color(0.25f, 0.3f, 0.33f), null, null, 0.4f, 0.6f);
        Mat("Bounce", new Color(0.2f, 0.75f, 0.95f), grid, new Color(0.05f, 0.25f, 0.35f));
        Mat("Laser", new Color(1f, 0.1f, 0.08f), null, new Color(6f, 0.4f, 0.3f));
        Mat("Lamp", new Color(0.2f, 1f, 0.3f), null, new Color(0.4f, 2f, 0.6f));
        Mat("Portal", new Color(0.35f, 0.75f, 1f), null, new Color(0.6f, 1.8f, 3f));
        Mat("Taser", new Color(0.3f, 0.6f, 1f), null, new Color(0.6f, 1.2f, 3f));
        Mat("Navy", new Color(0.12f, 0.17f, 0.32f), null);
        Mat("Skin", new Color(0.86f, 0.66f, 0.52f), null);
        Mat("Badge", new Color(1f, 0.8f, 0.25f), null, null, 0.6f, 0.8f);
        Mat("Mark", new Color(0.1f, 0.1f, 0.12f), null);
        // частицы дыма
        string smokePath = $"{MatDir}/Smoke.mat";
        var sm = AssetDatabase.LoadAssetAtPath<Material>(smokePath);
        if (!sm) { sm = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(sm, smokePath); }
        sm.SetColor("_BaseColor", new Color(0.55f, 0.55f, 0.58f, 0.55f)); sm.SetFloat("_Surface", 1f); sm.SetFloat("_Blend", 0f);
        sm.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); sm.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); sm.SetFloat("_ZWrite", 0f);
        sm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); sm.renderQueue = (int)RenderQueue.Transparent; EditorUtility.SetDirty(sm); mats["Smoke"] = sm;
        // небо — светлое процедурное
        string skyPath = $"{MatDir}/Sky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
        if (!sky) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, skyPath); }
        sky.SetFloat("_SunSize", 0.03f); sky.SetFloat("_AtmosphereThickness", 0.7f); sky.SetColor("_SkyTint", new Color(0.62f, 0.72f, 0.86f));
        sky.SetColor("_GroundColor", new Color(0.72f, 0.74f, 0.76f)); sky.SetFloat("_Exposure", 1.25f); EditorUtility.SetDirty(sky); mats["Sky"] = sky;
    }

    // ================================================================== геометрия
    /// Куб с развёрткой в метрах (клетка 1 м не растягивается), меш сохраняется ассетом по размеру.
    static Mesh MeterBox(Vector3 s)
    {
        string key = $"{s.x:0.###}x{s.y:0.###}x{s.z:0.###}";
        if (meshes.TryGetValue(key, out var cached)) return cached;
        var h = s / 2f; var v = new List<Vector3>(); var uv = new List<Vector2>(); var n = new List<Vector3>(); var tri = new List<int>();
        void Face(Vector3 normal, Vector3 u, Vector3 w, float su, float sw)
        {
            int i = v.Count; var c = Vector3.Scale(normal, h);
            var hu = Vector3.Scale(u, h).magnitude; var hw = Vector3.Scale(w, h).magnitude;
            v.Add(c - u * hu - w * hw); v.Add(c + u * hu - w * hw); v.Add(c + u * hu + w * hw); v.Add(c - u * hu + w * hw);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(su, 0)); uv.Add(new Vector2(su, sw)); uv.Add(new Vector2(0, sw));
            for (int k = 0; k < 4; k++) n.Add(normal);
            if (Vector3.Dot(Vector3.Cross(u, w), normal) > 0) tri.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            else tri.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }
        Face(Vector3.up, Vector3.right, Vector3.forward, s.x, s.z); Face(Vector3.down, Vector3.right, Vector3.forward, s.x, s.z);
        Face(Vector3.forward, Vector3.right, Vector3.up, s.x, s.y); Face(Vector3.back, Vector3.right, Vector3.up, s.x, s.y);
        Face(Vector3.right, Vector3.forward, Vector3.up, s.z, s.y); Face(Vector3.left, Vector3.forward, Vector3.up, s.z, s.y);
        var m = new Mesh { name = "GymBox_" + key }; m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
        m.RecalculateBounds(); m.RecalculateTangents();
        AssetDatabase.CreateAsset(m, $"{MeshDir}/GymBox_{key}.asset");
        meshes[key] = m; return m;
    }

    static Transform Group(string name, Transform parent = null) { var g = new GameObject(name).transform; g.SetParent(parent ? parent : root, false); return g; }

    static GameObject Block(string name, Vector3 center, Vector3 size, string mat, Transform parent, Quaternion? rot = null, bool collider = true)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false); go.transform.SetPositionAndRotation(center, rot ?? Quaternion.identity);
        go.GetComponent<MeshFilter>().sharedMesh = MeterBox(size);
        go.GetComponent<MeshRenderer>().sharedMaterial = M(mat);
        if (collider) go.AddComponent<BoxCollider>().size = size;
        return go;
    }

    static GameObject Prim(PrimitiveType type, string name, Vector3 pos, Vector3 scale, string mat, Transform parent, Quaternion? rot = null, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot ?? Quaternion.identity); go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = M(mat);
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    /// Горизонтальная перекладина/труба от a до b.
    static GameObject Bar(string name, Vector3 a, Vector3 b, float r, string mat, Transform parent)
    {
        var d = b - a;
        return Prim(PrimitiveType.Cylinder, name, (a + b) / 2f, new Vector3(r * 2, d.magnitude / 2f, r * 2), mat, parent, Quaternion.FromToRotation(Vector3.up, d.normalized));
    }

    static TextMeshPro Text(string name, string text, Vector3 pos, Quaternion rot, float size, Color c, Transform parent, Vector2 rect, bool bold = true)
    {
        var go = new GameObject(name, typeof(TextMeshPro)); go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshPro>(); t.text = text; t.fontSize = size; t.color = c; t.alignment = TextAlignmentOptions.Center;
        t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal; t.rectTransform.sizeDelta = rect; t.textWrappingMode = TextWrappingModes.Normal;
        go.transform.SetPositionAndRotation(pos, rot); return t;
    }
    /// Поворот надписи, которую читают, глядя в направлении viewDir.
    static Quaternion Facing(Vector3 viewDir) => Quaternion.LookRotation(viewDir, Vector3.up);

    /// Физическая 3D-кнопка: основание + шляпка (PushButton.cap), триггер на Ignore Raycast. normal — к игроку.
    static PushButton Button(string name, string label, Vector3 pos, Vector3 normal, string capMat, Transform parent, float size = 1f)
    {
        var btn = new GameObject("Btn_" + name); btn.transform.SetParent(parent, false);
        btn.transform.SetPositionAndRotation(pos, Quaternion.FromToRotation(Vector3.up, normal));
        btn.layer = ignoreRaycast;
        Prim(PrimitiveType.Cylinder, "Base", btn.transform.position, new Vector3(0.16f, 0.015f, 0.16f) * size, "Dark", btn.transform, btn.transform.rotation, false);
        var cap = Prim(PrimitiveType.Cylinder, "Cap", btn.transform.position + normal * 0.03f * size, new Vector3(0.11f, 0.02f, 0.11f) * size, capMat, btn.transform, btn.transform.rotation, false);
        var trig = btn.AddComponent<BoxCollider>(); trig.isTrigger = true; trig.center = new Vector3(0, 0.04f, 0) * size; trig.size = new Vector3(0.15f, 0.08f, 0.15f) * size;
        btn.AddComponent<AudioSource>();
        var pb = btn.AddComponent<PushButton>(); pb.label = label; pb.cap = cap.transform; pb.clickClips = clickClips; pb.pressDepth = 0.015f * size;
        var view = -normal; var up = Vector3.ProjectOnPlane(Vector3.up, normal).sqrMagnitude > 0.01f ? Vector3.ProjectOnPlane(Vector3.up, normal).normalized : Vector3.forward;
        Text("Label", label, pos - up * 0.13f * size + normal * 0.01f, Quaternion.LookRotation(view, up), 0.55f * size, new Color(0.1f, 0.1f, 0.12f), btn.transform, new Vector2(0.4f, 0.1f) * size);
        return pb;
    }
    static void OnPress(PushButton b, UnityEngine.Events.UnityAction action) { UnityEventTools.AddPersistentListener(b.onPressed, action); EditorUtility.SetDirty(b); }

    static void PointLight(string name, Vector3 pos, Color c, float range, float intensity, Transform parent)
    {
        var l = new GameObject(name, typeof(Light)).GetComponent<Light>(); l.transform.SetParent(parent, false); l.transform.position = pos;
        l.type = LightType.Point; l.color = c; l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
    }

    // ================================================================== окружение
    static void Environment()
    {
        RenderSettings.skybox = M("Sky");
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.8f, 0.84f, 0.9f);
        RenderSettings.ambientEquatorColor = new Color(0.68f, 0.7f, 0.74f);
        RenderSettings.ambientGroundColor = new Color(0.42f, 0.42f, 0.44f);
        RenderSettings.fog = false; RenderSettings.reflectionIntensity = 0.6f;
        var sunGo = GameObject.Find("Directional Light");
        if (sunGo && sunGo.TryGetComponent<Light>(out var sun))
        {
            sun.color = new Color(1f, 0.97f, 0.92f); sun.intensity = 1.25f; sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.55f;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f); RenderSettings.sun = sun;
        }
        else log.AppendLine("  ERROR: no Directional Light");
        DynamicGI.UpdateEnvironment();
    }

    static void Arena()
    {
        var g = Group("Arena");
        Block("Floor", new Vector3(0, -0.25f, 10f), new Vector3(80f, 0.5f, 72f), "Floor", g);
        Block("Wall_N", new Vector3(0, 4f, 46.25f), new Vector3(80.5f, 8f, 0.5f), "Wall", g);
        Block("Wall_S", new Vector3(0, 4f, -26.25f), new Vector3(80.5f, 8f, 0.5f), "Wall", g);
        Block("Wall_W", new Vector3(-40.25f, 4f, 10f), new Vector3(0.5f, 8f, 72f), "Wall", g);
        Block("Wall_E", new Vector3(40.25f, 4f, 10f), new Vector3(0.5f, 8f, 72f), "Wall", g);
        // разделительные полосы зон на полу
        Block("Stripe_W", new Vector3(-5f, 0.005f, 10f), new Vector3(0.25f, 0.01f, 70f), "Orange", g, null, false);
        Block("Stripe_E", new Vector3(7f, 0.005f, 10f), new Vector3(0.25f, 0.01f, 70f), "Blue", g, null, false);
        Text("Title_W", "ПОЛОСА ПРЕПЯТСТВИЙ", new Vector3(-20f, 7.2f, 46f), Facing(Vector3.forward), 14f, new Color(1f, 0.5f, 0.15f), g, new Vector2(30, 2));
        Text("Title_E", "ЗОНА ОГРАБЛЕНИЯ", new Vector3(22f, 7.2f, 46f), Facing(Vector3.forward), 14f, new Color(0.25f, 0.5f, 1f), g, new Vector2(30, 2));
        Text("Title_S", "GRABZ · ТЕСТОВЫЙ ПОЛИГОН", new Vector3(0f, 6.6f, -26f), Facing(Vector3.back), 12f, new Color(0.2f, 0.22f, 0.26f), g, new Vector2(30, 2));
    }

    static void SpawnAndPortal(GameObject mirror)
    {
        var g = Group("Spawn");
        Block("SpawnPad", new Vector3(0, 0.006f, -6f), new Vector3(3f, 0.012f, 3f), "Green", g, null, false);
        Text("SpawnHint", "← ЗЕРКАЛО   ·   ПОЛОСА ←   ·   → СТЕНД ФИЗИКИ И ОГРАБЛЕНИЕ", new Vector3(0, 2.6f, -1.5f), Facing(Vector3.forward), 2.2f, new Color(0.15f, 0.16f, 0.2f), g, new Vector2(9, 0.6f));
        Block("HintBoard", new Vector3(0, 2.6f, -1.42f), new Vector3(9.4f, 0.8f, 0.08f), "Wall", g);
        Prim(PrimitiveType.Cylinder, "HintPostL", new Vector3(-4.2f, 1.1f, -1.4f), new Vector3(0.12f, 1.1f, 0.12f), "Dark", g);
        Prim(PrimitiveType.Cylinder, "HintPostR", new Vector3(4.2f, 1.1f, -1.4f), new Vector3(0.12f, 1.1f, 0.12f), "Dark", g);
        // зеркало из хаба — слева от точки появления, лицом к ней
        if (mirror)
        {
            var b = Bounds(mirror);
            var target = new Vector3(-3.6f, b.center.y, -6f);
            mirror.transform.position += target - b.center;
            mirror.transform.SetParent(g, true);
            Block("MirrorBack", new Vector3(-3.75f - 0.05f, 1.5f, -6f), new Vector3(0.12f, 3.2f, b.size.z + 0.4f), "Dark", g);
            Text("MirrorTitle", "ЗЕРКАЛО", new Vector3(-3.6f, 3.3f, -6f), Facing(Vector3.left), 2.4f, new Color(0.15f, 0.16f, 0.2f), g, new Vector2(3, 0.5f));
        }
        // портал в хаб — за спиной у точки появления
        var p = Group("Portal_ToHub", g);
        var c = new Vector3(0f, 0f, -12.5f);
        Block("PostL", c + new Vector3(-1.3f, 1.5f, 0), new Vector3(0.4f, 3f, 0.4f), "Dark", p);
        Block("PostR", c + new Vector3(1.3f, 1.5f, 0), new Vector3(0.4f, 3f, 0.4f), "Dark", p);
        Block("Top", c + new Vector3(0, 3.2f, 0), new Vector3(3f, 0.4f, 0.4f), "Dark", p);
        var glow = Block("Glow", c + new Vector3(0, 1.5f, 0), new Vector3(2.2f, 3f, 0.05f), "Portal", p, null, false);
        glow.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        var trig = new GameObject("Trigger", typeof(BoxCollider)); trig.transform.SetParent(p, false); trig.transform.position = c + new Vector3(0, 1.4f, 0);
        var bc = trig.GetComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(2.2f, 2.8f, 0.8f); trig.layer = ignoreRaycast;
        trig.AddComponent<ScenePortal>().sceneName = GameSession.HubScene;
        Text("PortalTitle", "В ХАБ", c + new Vector3(0, 3.75f, 0.25f), Facing(Vector3.back), 3f, new Color(0.2f, 0.5f, 0.95f), p, new Vector2(3, 0.6f));
        Text("PortalTitleB", "В ХАБ", c + new Vector3(0, 3.75f, -0.25f), Facing(Vector3.forward), 3f, new Color(0.2f, 0.5f, 0.95f), p, new Vector2(3, 0.6f));
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
    }

    // ================================================================== стенд физики (справа от точки появления, лицом к ней)
    static void TuningStand()
    {
        var g = Group("TuningStand");
        float x = 4.6f, z0 = -6f;
        Block("Board", new Vector3(x + 0.1f, 1.45f, z0), new Vector3(0.12f, 2.1f, 2.4f), "Wall", g);
        Block("Frame", new Vector3(x + 0.18f, 1.45f, z0), new Vector3(0.06f, 2.3f, 2.6f), "Dark", g);
        Block("Legs", new Vector3(x + 0.2f, 0.2f, z0), new Vector3(0.3f, 0.4f, 2.2f), "Dark", g);
        var stand = g.gameObject.AddComponent<LocomotionTuningStand>();
        var view = Quaternion.LookRotation(Vector3.right, Vector3.up);     // игрок смотрит на +X
        Text("Title", "СТЕНД ФИЗИКИ", new Vector3(x + 0.02f, 2.62f, z0), view, 2.4f, new Color(0.95f, 0.45f, 0.1f), g, new Vector2(2.4f, 0.3f));
        stand.statusText = Text("Status", "", new Vector3(x + 0.02f, 2.36f, z0), view, 1.3f, new Color(0.2f, 0.2f, 0.25f), g, new Vector2(2.4f, 0.2f));
        var n = Vector3.left;                                              // нормаль панели — к игроку
        var rows = new (string label, float y, System.Action<TextMeshPro> setText, UnityEngine.Events.UnityAction minus, UnityEngine.Events.UnityAction plus, string mat)[]
        {
            ("ПРЫЖОК", 2.0f, t => stand.jumpText = t, stand.JumpDown, stand.JumpUp, "Orange"),
            ("СКОРОСТЬ", 1.6f, t => stand.speedText = t, stand.SpeedDown, stand.SpeedUp, "Blue"),
            ("ГРАВИТАЦИЯ", 1.2f, t => stand.gravityText = t, stand.GravityDown, stand.GravityUp, "Purple"),
        };
        foreach (var r in rows)
        {
            OnPress(Button(r.label + "_Minus", "−", new Vector3(x, r.y, z0 + 0.8f), n, r.mat, g, 1.2f), r.minus);
            OnPress(Button(r.label + "_Plus", "+", new Vector3(x, r.y, z0 - 0.8f), n, r.mat, g, 1.2f), r.plus);
            r.setText(Text("Value_" + r.label, r.label, new Vector3(x + 0.02f, r.y, z0), view, 1.4f, new Color(0.1f, 0.1f, 0.14f), g, new Vector2(1.1f, 0.34f)));
        }
        OnPress(Button("Save", "СОХРАНИТЬ", new Vector3(x, 0.95f, z0 + 0.6f), n, "Green", g, 1.2f), stand.SaveValues);
        OnPress(Button("Reset", "СБРОС", new Vector3(x, 0.95f, z0 - 0.6f), n, "Orange", g, 1.2f), stand.ResetValues);
        Text("Help", "− / + меняют физику сразу (VR-игрок).\nСОХРАНИТЬ — запомнить на этом шлеме.", new Vector3(x + 0.02f, 0.5f, z0), view, 0.8f, new Color(0.25f, 0.25f, 0.3f), g, new Vector2(2.3f, 0.25f), false);
    }

    // ================================================================== полоса препятствий (запад)
    static void ObstacleCourse()
    {
        var g = Group("ObstacleCourse");
        // 1. рампы 15° / 25° / 35°
        float[] angles = { 15f, 25f, 35f };
        for (int i = 0; i < 3; i++)
        {
            float a = angles[i], L = 6f, x = -9f - i * 3.6f;
            var rot = Quaternion.Euler(-a, 0, 0);
            var center = new Vector3(x, Mathf.Sin(a * Mathf.Deg2Rad) * L / 2f, 0f + Mathf.Cos(a * Mathf.Deg2Rad) * L / 2f);
            Block($"Ramp_{a}", center, new Vector3(3f, 0.3f, L), i == 0 ? "Green" : i == 1 ? "Yellow" : "Orange", g, rot);
            float topY = Mathf.Sin(a * Mathf.Deg2Rad) * L, topZ = Mathf.Cos(a * Mathf.Deg2Rad) * L;
            Block($"RampTop_{a}", new Vector3(x, topY / 2f, topZ + 1f), new Vector3(3f, topY, 2f), "Wall", g);
            Text($"RampLabel_{a}", $"РАМПА {a}°", new Vector3(x, 0.4f, -0.6f), Facing(Vector3.forward), 2f, new Color(0.2f, 0.2f, 0.25f), g, new Vector2(3, 0.4f));
        }
        // 2. ущелья для pinch climbing: пары стен 6 м с зазором 0.9 / 1.3 / 1.8 м
        float[] gaps = { 0.9f, 1.3f, 1.8f }; float gx = -22f;
        foreach (var gap in gaps)
        {
            float off = gap / 2f + 0.2f;
            Block($"Gorge_{gap}_L", new Vector3(gx - off, 3f, 4f), new Vector3(0.4f, 6f, 5f), "Blue", g);
            Block($"Gorge_{gap}_R", new Vector3(gx + off, 3f, 4f), new Vector3(0.4f, 6f, 5f), "Blue", g);
            Text($"GorgeLabel_{gap}", $"ЩЕЛЬ {gap:0.0} м", new Vector3(gx, 0.5f, 1.2f), Facing(Vector3.forward), 1.8f, new Color(0.2f, 0.2f, 0.25f), g, new Vector2(2.2f, 0.4f));
            gx -= gap + 2.6f;
        }
        Block("GorgeTop", new Vector3(-26f, 6.1f, 7.5f), new Vector3(12f, 0.2f, 2f), "Wall", g);
        // 3. стена высоты с отметками каждые 0.5 м — мерить прыжки
        var wx = -38.5f;
        Block("HeightWall", new Vector3(wx, 4f, 4f), new Vector3(0.6f, 8f, 8f), "Wall", g);
        for (int i = 1; i <= 15; i++)
        {
            float y = i * 0.5f; bool major = i % 2 == 0;
            Block($"Tick_{y}", new Vector3(wx + 0.31f, y, 4f), new Vector3(0.02f, major ? 0.05f : 0.025f, major ? 8f : 3f), "Mark", g, null, false);
            if (major) Text($"TickLabel_{y}", $"{y:0} м", new Vector3(wx + 0.33f, y + 0.18f, 1.4f), Facing(Vector3.left), 2f, new Color(0.1f, 0.1f, 0.12f), g, new Vector2(1.2f, 0.35f));
        }
        // 4. рукоход (перекладины на высоте 2.6 м)
        float y0 = 2.6f, zc = 16f;
        Block("BarsStart", new Vector3(-10.5f, 0.6f, zc), new Vector3(2f, 1.2f, 2.4f), "Yellow", g);
        for (int i = 0; i <= 10; i++) Bar($"Rung_{i}", new Vector3(-12f - i * 1.1f, y0, zc - 0.9f), new Vector3(-12f - i * 1.1f, y0, zc + 0.9f), 0.04f, "Steel", g);
        Bar("RailL", new Vector3(-11.5f, y0 + 0.05f, zc - 0.95f), new Vector3(-23.5f, y0 + 0.05f, zc - 0.95f), 0.05f, "Dark", g);
        Bar("RailR", new Vector3(-11.5f, y0 + 0.05f, zc + 0.95f), new Vector3(-23.5f, y0 + 0.05f, zc + 0.95f), 0.05f, "Dark", g);
        foreach (var px in new[] { -11.5f, -23.5f }) foreach (var pz in new[] { zc - 0.95f, zc + 0.95f })
            Prim(PrimitiveType.Cylinder, "BarPost", new Vector3(px, y0 / 2f, pz), new Vector3(0.12f, y0 / 2f, 0.12f), "Dark", g);
        Block("BarsEnd", new Vector3(-25f, 1.2f, zc), new Vector3(2f, 2.4f, 2.4f), "Yellow", g);
        Text("BarsLabel", "РУКОХОД", new Vector3(-17.5f, 3.3f, zc - 1f), Facing(Vector3.forward), 2.5f, new Color(0.2f, 0.2f, 0.25f), g, new Vector2(4, 0.5f));
        // 5. высокие платформы по спирали: 1 → 2 → 3.2 → 4.5 → 6 м
        var plats = new (Vector3 c, float h)[] { (new Vector3(-29f, 0, 13f), 1f), (new Vector3(-32f, 0, 15.5f), 2f), (new Vector3(-35.5f, 0, 14f), 3.2f), (new Vector3(-36.5f, 0, 18.5f), 4.5f), (new Vector3(-33f, 0, 21f), 6f) };
        for (int i = 0; i < plats.Length; i++)
        {
            var (c, h) = plats[i];
            Block($"Platform_{h}", c + Vector3.up * h / 2f, new Vector3(2.2f, h, 2.2f), i % 2 == 0 ? "Purple" : "Blue", g);
            Text($"PlatLabel_{h}", $"{h:0.#} м", c + new Vector3(0, h + 0.01f, 0), Quaternion.Euler(90, 0, 0), 3f, new Color(1, 1, 1), g, new Vector2(2, 0.6f));
        }
        Bar("TopRail", new Vector3(-34.1f, 7f, 20f), new Vector3(-31.9f, 7f, 20f), 0.04f, "Dark", g);
        // лестница на 6-метровую платформу
        Ladder(g, new Vector3(-33f, 0, 22.15f), 6f, Vector3.forward);
        // 6. батуты и вышка 5 м, на которую с них можно запрыгнуть
        foreach (var (p, mult) in new[] { (new Vector3(-11f, 0, 27f), 2.2f), (new Vector3(-15f, 0, 27f), 3f) })
            Trampoline(g, p, mult);
        Block("TrampTower", new Vector3(-13f, 2.5f, 31.5f), new Vector3(4f, 5f, 2.5f), "Orange", g);
        Text("TrampLabel", "БАТУТЫ ×2.2 · ×3", new Vector3(-13f, 0.5f, 25f), Facing(Vector3.forward), 2f, new Color(0.2f, 0.2f, 0.25f), g, new Vector2(4, 0.4f));
        // 7. стена с лестницей 6 м и V-образные стены для отскоков
        Block("LadderWall", new Vector3(-21f, 3f, 32f), new Vector3(4f, 6f, 1f), "Wall", g);
        Ladder(g, new Vector3(-21f, 0, 31.45f), 6f, Vector3.back);
        Block("LadderTop", new Vector3(-21f, 6.1f, 33f), new Vector3(4f, 0.2f, 3f), "Yellow", g);
        Block("WallBounce_L", new Vector3(-29f, 3.5f, 33f), new Vector3(0.4f, 7f, 6f), "Green", g, Quaternion.Euler(0, 25f, 0));
        Block("WallBounce_R", new Vector3(-33.5f, 3.5f, 33f), new Vector3(0.4f, 7f, 6f), "Green", g, Quaternion.Euler(0, -25f, 0));
        Text("WallBounceLabel", "ОТСКОК ОТ СТЕН", new Vector3(-31.2f, 0.5f, 29.5f), Facing(Vector3.forward), 2f, new Color(0.2f, 0.2f, 0.25f), g, new Vector2(4, 0.4f));
        // 8. дорожка дальности прыжка: старт 1.5 м, отметки 1–10 м
        Block("LongJumpStart", new Vector3(-8f, 0.75f, 40f), new Vector3(2f, 1.5f, 2f), "Orange", g);
        for (int i = 1; i <= 10; i++)
        {
            Block($"Dist_{i}", new Vector3(-9f - i, 0.006f, 40f), new Vector3(0.06f, 0.012f, 3f), "Mark", g, null, false);
            Text($"DistLabel_{i}", $"{i}", new Vector3(-9f - i, 0.02f, 38.2f), Quaternion.Euler(90, 0, 0), 3f, new Color(0.1f, 0.1f, 0.12f), g, new Vector2(1, 0.6f));
        }
    }

    static void Ladder(Transform g, Vector3 baseCenter, float height, Vector3 outward)
    {
        var side = Vector3.Cross(Vector3.up, outward).normalized * 0.3f;
        Block("LadderRailL", baseCenter - side + Vector3.up * height / 2f, new Vector3(0.06f, height, 0.06f), "Dark", g);
        Block("LadderRailR", baseCenter + side + Vector3.up * height / 2f, new Vector3(0.06f, height, 0.06f), "Dark", g);
        for (float y = 0.3f; y < height; y += 0.3f) Bar("Rung", baseCenter - side + Vector3.up * y, baseCenter + side + Vector3.up * y, 0.025f, "Steel", g);
    }

    static void Trampoline(Transform g, Vector3 p, float mult)
    {
        var t = Group($"Trampoline_x{mult}", g);
        Block("Frame", p + Vector3.up * 0.15f, new Vector3(2.8f, 0.3f, 2.8f), "Dark", t);
        var pad = Block("Pad", p + Vector3.up * 0.33f, new Vector3(2.4f, 0.06f, 2.4f), "Bounce", t);
        pad.AddComponent<GorillaLocomotion.Surface>().bounceMultiplier = mult;
        pad.AddComponent<AudioSource>(); pad.AddComponent<Trampoline>().launchSpeed = 7f + mult * 2f;
        var trig = new GameObject("LaunchTrigger", typeof(BoxCollider)); trig.transform.SetParent(t, false); trig.transform.position = p + Vector3.up * 0.7f;
        var bc = trig.GetComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(2.2f, 0.6f, 2.2f); trig.layer = ignoreRaycast;
        trig.AddComponent<AudioSource>(); trig.AddComponent<Trampoline>().launchSpeed = 7f + mult * 2f;
    }

    // ================================================================== зона ограбления (восток)
    static void HeistZone()
    {
        var g = Group("HeistZone");
        Safe(g, new Vector3(14f, 0f, 8f));
        var lasers = Lasers(g, new Vector3(27f, 0f, 6f));
        Breakers(g, new Vector3(20f, 0f, 8.2f), lasers);
        Guard(g);
        LootTable(g, new Vector3(19f, 0f, 19f));
        // тревога (локальная: в полигоне нет HeistNetworkState) — красная мигалка над лазерами
        var alarmGo = new GameObject("AlarmSystem"); alarmGo.transform.SetParent(g, false);
        var alarm = alarmGo.AddComponent<AlarmSystem>();
        Prim(PrimitiveType.Cylinder, "BeaconPole", new Vector3(31f, 1.5f, 1f), new Vector3(0.1f, 1.5f, 0.1f), "Dark", g);
        var beacon = Prim(PrimitiveType.Sphere, "Beacon", new Vector3(31f, 3.15f, 1f), new Vector3(0.35f, 0.35f, 0.35f), "Laser", g, null, false);
        var al = new GameObject("AlarmLight", typeof(Light)).GetComponent<Light>(); al.transform.SetParent(g, false); al.transform.position = new Vector3(31f, 3.6f, 1f);
        al.type = LightType.Point; al.color = new Color(1f, 0.1f, 0.05f); al.range = 16f; al.intensity = 0f; al.shadows = LightShadows.None;
        alarm.alarmLights = new[] { al }; alarm.beacons = new Renderer[] { beacon.GetComponent<Renderer>() };
        EditorUtility.SetDirty(alarm);
    }

    static void Safe(Transform g, Vector3 c)
    {
        var s = Group("Safe_Drill", g);
        // корпус сейфа (полый — внутри лут), фасад смотрит на −Z
        float w = 1.4f, h = 1.6f, d = 1.1f, t = 0.1f;
        Block("Plinth", c + Vector3.up * 0.1f, new Vector3(w + 0.2f, 0.2f, d + 0.2f), "Dark", s);
        var b0 = c + Vector3.up * 0.2f;
        Block("Bottom", b0 + new Vector3(0, t / 2, 0), new Vector3(w, t, d), "SafeBody", s);
        Block("Top", b0 + new Vector3(0, h - t / 2, 0), new Vector3(w, t, d), "SafeBody", s);
        Block("Back", b0 + new Vector3(0, h / 2, d / 2 - t / 2), new Vector3(w, h, t), "SafeBody", s);
        Block("SideL", b0 + new Vector3(-w / 2 + t / 2, h / 2, 0), new Vector3(t, h, d), "SafeBody", s);
        Block("SideR", b0 + new Vector3(w / 2 - t / 2, h / 2, 0), new Vector3(t, h, d), "SafeBody", s);
        Block("Shelf", b0 + new Vector3(0, h * 0.5f, 0.05f), new Vector3(w - 2 * t, 0.04f, d - t - 0.05f), "Steel", s);
        // дверь на петле (левый передний угол), открывается наружу
        var hinge = new GameObject("DoorHinge").transform; hinge.SetParent(s, false); hinge.position = b0 + new Vector3(-w / 2, h / 2, -d / 2 - 0.01f);
        var door = Block("Door", hinge.position + new Vector3(w / 2, 0, -0.06f), new Vector3(w, h, 0.12f), "SafeBody", hinge);
        Prim(PrimitiveType.Cylinder, "Wheel", hinge.position + new Vector3(w / 2 + 0.35f, 0.2f, -0.14f), new Vector3(0.3f, 0.02f, 0.3f), "Steel", hinge, Quaternion.Euler(90, 0, 0), false);
        // дрель на двери
        var drillGo = new GameObject("Drill"); drillGo.transform.SetParent(hinge, false);
        drillGo.transform.position = hinge.position + new Vector3(w / 2 - 0.15f, -0.1f, -0.42f);
        var dp = drillGo.transform.position;
        Prim(PrimitiveType.Cylinder, "Body", dp, new Vector3(0.24f, 0.22f, 0.24f), "Yellow", drillGo.transform, Quaternion.Euler(90, 0, 0), false);
        Prim(PrimitiveType.Cube, "Handle", dp + new Vector3(0, -0.2f, 0.05f), new Vector3(0.08f, 0.25f, 0.1f), "Dark", drillGo.transform, null, false);
        Prim(PrimitiveType.Cube, "Mount", dp + new Vector3(0, 0.2f, 0.2f), new Vector3(0.1f, 0.08f, 0.3f), "Steel", drillGo.transform, null, false);
        var bitPivot = new GameObject("BitPivot").transform; bitPivot.SetParent(drillGo.transform, false); bitPivot.position = dp + new Vector3(0, 0, 0.28f);
        Prim(PrimitiveType.Cylinder, "Bit", bitPivot.position, new Vector3(0.04f, 0.08f, 0.04f), "Steel", bitPivot, Quaternion.Euler(90, 0, 0), false);
        Prim(PrimitiveType.Cube, "BitFlute", bitPivot.position, new Vector3(0.07f, 0.012f, 0.14f), "Steel", bitPivot, null, false);
        var lamp = Prim(PrimitiveType.Sphere, "HeatLamp", dp + new Vector3(0, 0.17f, -0.05f), new Vector3(0.08f, 0.08f, 0.08f), "Lamp", drillGo.transform, null, false);
        var hit = new GameObject("HitZone", typeof(BoxCollider)); hit.transform.SetParent(drillGo.transform, false); hit.transform.position = dp + new Vector3(0, 0, -0.02f);
        var hbc = hit.GetComponent<BoxCollider>(); hbc.isTrigger = true; hbc.size = new Vector3(0.4f, 0.5f, 0.55f); hit.layer = ignoreRaycast;
        // дым
        var smokeGo = new GameObject("Smoke", typeof(ParticleSystem)); smokeGo.transform.SetParent(drillGo.transform, false); smokeGo.transform.position = dp + new Vector3(0, 0.1f, 0.25f);
        var ps = smokeGo.GetComponent<ParticleSystem>(); var main = ps.main;
        main.startLifetime = 1.6f; main.startSpeed = 0.6f; main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f); main.maxParticles = 60; main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = -0.05f;
        var em = ps.emission; em.rateOverTime = 0f;
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 20f; shape.radius = 0.05f;
        smokeGo.transform.rotation = Quaternion.Euler(-90, 0, 0);
        var psr = smokeGo.GetComponent<ParticleSystemRenderer>(); psr.sharedMaterial = M("Smoke"); psr.shadowCastingMode = ShadowCastingMode.Off;
        // логика
        var drillRoot = s.gameObject; drillRoot.AddComponent<NetworkObject>();
        var motor = drillRoot.AddComponent<AudioSource>(); var fx = drillGo.AddComponent<AudioSource>();
        var drill = drillRoot.AddComponent<SafeDrill>();
        drill.bit = bitPivot; drill.doorHinge = hinge; drill.doorOpenAngle = 110f; drill.heatLamp = lamp.GetComponent<Renderer>(); drill.smoke = ps; drill.motor = motor; drill.fx = fx;
        drill.drillSeconds = 30f; drill.heatSeconds = 9f;
        drill.display = Text("DrillDisplay", "ДРЕЛЬ", c + new Vector3(0, 2.45f, -0.5f), Facing(Vector3.forward), 2.4f, new Color(0.1f, 0.1f, 0.12f), s, new Vector2(2.6f, 0.9f));
        Block("DisplayBoard", c + new Vector3(0, 2.45f, -0.44f), new Vector3(2.8f, 1.0f, 0.06f), "Wall", s);
        hit.AddComponent<DrillHitZone>().drill = drill;
        // лут внутри сейфа (не внутри сетевого объекта дрели: вложенные NetworkObject в сцене не нужны)
        PlaceLoot("Assets/Prefabs/Loot/Loot_GoldBar.prefab", b0 + new Vector3(-0.25f, t, 0.05f), g, 0f);
        PlaceLoot("Assets/Prefabs/Loot/Loot_GoldBar.prefab", b0 + new Vector3(0.25f, t, 0.05f), g, 0f);
        PlaceLoot("Assets/Art/GeneratedModels/Bank/Loot_CashStack.prefab", b0 + new Vector3(0f, h * 0.5f + 0.02f, 0.1f), g, 0f);
        // пульт дрели
        var post = new Vector3(c.x - 1.6f, 0, c.z - 1.2f);
        Block("PultPost", post + Vector3.up * 0.5f, new Vector3(0.3f, 1f, 0.3f), "Dark", s);
        Block("PultTop", post + Vector3.up * 1.05f, new Vector3(0.7f, 0.1f, 0.4f), "Wall", s);
        OnPress(Button("DrillStart", "ПУСК ДРЕЛИ", post + new Vector3(-0.17f, 1.11f, 0), Vector3.up, "Green", s), drill.PressStart);
        OnPress(Button("DrillReset", "СБРОС", post + new Vector3(0.17f, 1.11f, 0), Vector3.up, "Orange", s), drill.PressReset);
        Text("DrillHelp", "ПЕРЕГРЕЛАСЬ — БЕЙ РУКОЙ ПО ДРЕЛИ", c + new Vector3(0, 0.05f, -1.6f), Quaternion.Euler(90, 0, 0), 2f, new Color(0.85f, 0.2f, 0.1f), s, new Vector2(4, 0.4f));
    }

    static void PlaceLoot(string prefabPath, Vector3 bottomCenter, Transform parent, float yaw)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (!prefab) { log.AppendLine("  ERROR: missing loot prefab " + prefabPath); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.rotation = Quaternion.Euler(0, yaw, 0) * prefab.transform.rotation;
        var b = Bounds(go);
        go.transform.position += bottomCenter + Vector3.up * 0.005f - new Vector3(b.center.x, b.min.y, b.center.z);
    }

    static GameObject[] Lasers(Transform g, Vector3 c)
    {
        var s = Group("LaserCorridor", g);
        // коридор вдоль Z шириной 4 м, низкие стенки по бокам
        Block("SideW", c + new Vector3(-2.2f, 0.8f, 0), new Vector3(0.3f, 1.6f, 9f), "Wall", s);
        Block("SideE", c + new Vector3(2.2f, 0.8f, 0), new Vector3(0.3f, 1.6f, 9f), "Wall", s);
        Text("LaserLabel", "ЛАЗЕРЫ · НЕ ЗАДЕНЬ", c + new Vector3(0, 0.02f, -5.2f), Quaternion.Euler(90, 0, 0), 2.4f, new Color(0.85f, 0.15f, 0.1f), s, new Vector2(4, 0.5f));
        var gridA = LaserGrid(s, "Grid_UpDown", c + new Vector3(0, 0, -2.5f), LaserAlarm.MoveAxis.UpDown, 0.25f, new[] { (0f, 0.45f), (0f, 1.1f), (1.4f, 0.75f), (1.4f, 1.45f) });
        var gridB = LaserGrid(s, "Grid_Sweep", c + new Vector3(0, 0, 2f), LaserAlarm.MoveAxis.ForwardBack, 0.8f, new[] { (0f, 0.35f), (0f, 0.95f), (0f, 1.55f) });
        return new[] { gridA, gridB };
    }

    static GameObject LaserGrid(Transform parent, string name, Vector3 origin, LaserAlarm.MoveAxis axis, float amplitude, (float dz, float y)[] beams)
    {
        var grid = new GameObject(name); grid.transform.SetParent(parent, false); grid.transform.position = origin;
        int i = 0;
        foreach (var (dz, y) in beams)
        {
            var b = Prim(PrimitiveType.Cube, "Beam_" + i++, origin + new Vector3(0, y, dz), new Vector3(4.1f, 0.025f, 0.025f), "Laser", grid.transform);
            b.GetComponent<Collider>().isTrigger = true; b.layer = ignoreRaycast;
            b.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var rb = b.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            b.AddComponent<LaserBeam>();
        }
        var la = grid.AddComponent<LaserAlarm>(); la.axis = axis; la.amplitude = amplitude; la.speed = 1.1f;
        return grid;
    }

    static void Breakers(Transform g, Vector3 c, GameObject[] powered)
    {
        var s = Group("BreakerPanel", g);
        Block("Wall", c + new Vector3(0, 1.3f, 0.2f), new Vector3(2.6f, 2.6f, 0.3f), "Wall", s);
        Block("Box", c + new Vector3(0, 1.35f, -0.02f), new Vector3(1.8f, 1.1f, 0.18f), "Steel", s);
        var panel = s.gameObject; panel.AddComponent<NetworkObject>();
        var bp = panel.AddComponent<BreakerPanel>(); bp.fx = panel.AddComponent<AudioSource>(); bp.poweredObjects = powered;
        bp.status = Text("Status", "ЛАЗЕРЫ ПОД ТОКОМ", c + new Vector3(0, 2.25f, -0.06f), Facing(Vector3.forward), 1.6f, new Color(0.1f, 0.1f, 0.12f), s, new Vector2(2.4f, 0.4f));
        Text("Title", "ЩИТОК · ОПУСТИ ВСЕ 3 → ЛАЗЕРЫ ГАСНУТ", c + new Vector3(0, 2.72f, 0.03f), Facing(Vector3.forward), 1.3f, new Color(0.2f, 0.2f, 0.25f), s, new Vector2(2.6f, 0.3f));
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 0.55f;
            var pivot = new GameObject($"Lever_{i}").transform; pivot.SetParent(s, false); pivot.position = c + new Vector3(x, 1.45f, -0.12f);
            Prim(PrimitiveType.Cube, "Arm", pivot.position + new Vector3(0, 0.15f, 0), new Vector3(0.05f, 0.3f, 0.05f), "Dark", pivot, null, false);
            Prim(PrimitiveType.Cube, "Grip", pivot.position + new Vector3(0, 0.3f, -0.03f), new Vector3(0.18f, 0.06f, 0.06f), "Orange", pivot, null, false);
            bp.levers[i] = pivot;
            bp.lamps[i] = Prim(PrimitiveType.Sphere, $"Lamp_{i}", c + new Vector3(x, 1.08f, -0.13f), new Vector3(0.07f, 0.07f, 0.07f), "Laser", s, null, false).GetComponent<Renderer>();
            var btn = Button($"Breaker_{i}", $"РУБИЛЬНИК {i + 1}", c + new Vector3(x, 0.9f, -0.12f), Vector3.back, "Orange", s);
            UnityEngine.Events.UnityAction act = bp.Toggle2; if (i == 0) act = bp.Toggle0; else if (i == 1) act = bp.Toggle1;
            OnPress(btn, act);
        }
        EditorUtility.SetDirty(bp);
    }

    static void Guard(Transform g)
    {
        var s = Group("Guard", g);
        var path = Group("PatrolPath", s);
        var pts = new[] { new Vector3(10.5f, 0, -1f), new Vector3(33.5f, 0, -1f), new Vector3(33.5f, 0, 14f), new Vector3(10.5f, 0, 14f) };
        var wps = new List<Transform>();
        for (int i = 0; i < pts.Length; i++)
        {
            var w = new GameObject("WP_" + i).transform; w.SetParent(path, false); w.position = pts[i]; wps.Add(w);
            Block($"PathMark_{i}", pts[i] + Vector3.up * 0.006f, new Vector3(0.6f, 0.012f, 0.6f), "Yellow", path, null, false);
        }
        for (int i = 0; i < pts.Length; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % pts.Length]; var mid = (a + b) / 2f;
            Block($"PathLine_{i}", mid + Vector3.up * 0.004f, new Vector3(Mathf.Abs(b.x - a.x) + 0.08f, 0.008f, Mathf.Abs(b.z - a.z) + 0.08f), "Yellow", path, null, false);
        }
        var body = new GameObject("GuardBody").transform; body.SetParent(s, false); body.position = pts[0];
        Prim(PrimitiveType.Capsule, "Torso", pts[0] + Vector3.up * 0.95f, new Vector3(0.6f, 0.62f, 0.45f), "Navy", body);
        Prim(PrimitiveType.Sphere, "Head", pts[0] + Vector3.up * 1.68f, new Vector3(0.32f, 0.34f, 0.32f), "Skin", body, null, false);
        Prim(PrimitiveType.Cylinder, "Cap", pts[0] + Vector3.up * 1.86f, new Vector3(0.34f, 0.05f, 0.34f), "Navy", body, null, false);
        Prim(PrimitiveType.Cube, "Visor", pts[0] + new Vector3(0, 1.82f, 0.17f), new Vector3(0.3f, 0.03f, 0.14f), "Dark", body, null, false);
        Prim(PrimitiveType.Cube, "Badge", pts[0] + new Vector3(-0.15f, 1.25f, 0.23f), new Vector3(0.08f, 0.1f, 0.02f), "Badge", body, null, false);
        foreach (float dx in new[] { -0.08f, 0.08f }) Prim(PrimitiveType.Sphere, "Eye", pts[0] + new Vector3(dx, 1.72f, 0.15f), new Vector3(0.05f, 0.05f, 0.03f), "Dark", body, null, false);
        var arm = new GameObject("TaserArm").transform; arm.SetParent(body, false); arm.position = pts[0] + new Vector3(0.36f, 1.35f, 0.05f);
        Prim(PrimitiveType.Cube, "Arm", arm.position + new Vector3(0, -0.25f, 0.05f), new Vector3(0.12f, 0.5f, 0.12f), "Navy", arm, null, false);
        Prim(PrimitiveType.Cube, "Taser", arm.position + new Vector3(0, -0.52f, 0.12f), new Vector3(0.07f, 0.07f, 0.22f), "Dark", arm, null, false);
        var tip = Prim(PrimitiveType.Sphere, "Tip", arm.position + new Vector3(0, -0.52f, 0.25f), new Vector3(0.06f, 0.06f, 0.06f), "Taser", arm, null, false);
        var zl = new GameObject("ZapLight", typeof(Light)).GetComponent<Light>(); zl.transform.SetParent(arm, false); zl.transform.position = tip.transform.position;
        zl.type = LightType.Point; zl.color = new Color(0.4f, 0.7f, 1f); zl.range = 3f; zl.intensity = 5f; zl.shadows = LightShadows.None;
        Text("GuardTag", "ОХРАННИК", pts[0] + new Vector3(0, 2.15f, 0.01f), Facing(Vector3.forward), 1.4f, new Color(0.15f, 0.25f, 0.6f), body, new Vector2(1.6f, 0.3f));
        Text("GuardTagBack", "ОХРАННИК", pts[0] + new Vector3(0, 2.15f, -0.01f), Facing(Vector3.back), 1.4f, new Color(0.15f, 0.25f, 0.6f), body, new Vector2(1.6f, 0.3f));
        var pg = s.gameObject.AddComponent<PatrolGuard>();
        pg.waypoints = wps.ToArray(); pg.body = body; pg.taserArm = arm; pg.taserTip = tip.GetComponent<Renderer>(); pg.zapLight = zl; pg.fx = body.gameObject.AddComponent<AudioSource>();
        EditorUtility.SetDirty(pg);
    }

    static void LootTable(Transform g, Vector3 c)
    {
        var s = Group("LootTable", g);
        Block("Table", c + Vector3.up * 0.45f, new Vector3(11f, 0.9f, 1.3f), "Wall", s);
        Text("Title", "ЛУТ РАЗНОГО ВЕСА · ТЯЖЁЛЫЙ = −40% СКОРОСТИ", c + new Vector3(0, 2.2f, 0.4f), Facing(Vector3.forward), 2f, new Color(0.2f, 0.2f, 0.25f), s, new Vector2(10, 0.4f));
        Block("TitleBoard", c + new Vector3(0, 2.2f, 0.5f), new Vector3(10.4f, 0.6f, 0.06f), "Wall", s);
        var items = new (string path, float x)[]
        {
            ("Assets/Prefabs/Loot/Loot_GoldBar.prefab", -4.6f), ("Assets/Art/GeneratedModels/Bank/Loot_CashStack.prefab", -3.2f), ("Assets/Prefabs/Loot/Loot_Diamond.prefab", -1.9f),
            ("Assets/Prefabs/Loot/Loot_Crown.prefab", -0.6f), ("Assets/Prefabs/Loot/Loot_Vase.prefab", 0.9f), ("Assets/Art/GeneratedModels/Bank/Loot_GoldenStatue.prefab", 2.7f),
        };
        foreach (var (path, x) in items)
        {
            PlaceLoot(path, c + new Vector3(x, 0.9f, 0), s, 0f);
            var li = AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponent<LootItem>();
            string kind = li == null ? "" : li.isHeavy ? "<color=#d33>ТЯЖЁЛЫЙ</color>" : li.isFragile ? "<color=#e80>ХРУПКИЙ</color>" : "<color=#2a2>ЛЁГКИЙ</color>";
            Text("Label", li ? $"{li.itemName}\n${li.value} · {kind}" : path, c + new Vector3(x, 0.55f, -0.67f), Facing(Vector3.forward), 0.9f, new Color(0.1f, 0.1f, 0.12f), s, new Vector2(1.3f, 0.35f));
        }
        PlaceLoot("Assets/Prefabs/Loot/Loot_Safe.prefab", c + new Vector3(6.6f, 0f, 0), s, 0f);
        Text("SafeLabel", "Сейф · ТЯЖЁЛЫЙ", c + new Vector3(6.6f, 0.05f, -1.1f), Quaternion.Euler(90, 0, 0), 1.6f, new Color(0.8f, 0.2f, 0.15f), s, new Vector2(2, 0.4f));
        var actions = s.gameObject.AddComponent<GymActions>();
        OnPress(Button("LootReset", "ЛУТ НА МЕСТО", c + new Vector3(-6.2f, 1.05f, 0), Vector3.up, "Yellow", s), actions.ResetLoot);
        Block("ResetPost", c + new Vector3(-6.2f, 0.5f, 0), new Vector3(0.3f, 1f, 0.3f), "Dark", s);
    }

    // ================================================================== снимки
    static void RenderShots()
    {
        Directory.CreateDirectory("production/qa/evidence");
        var shots = new (string name, Vector3 pos, Vector3 look, float fov)[]
        {
            ("gym_1_overview", new Vector3(0f, 26f, -24f), new Vector3(0f, 0f, 12f), 65f),
            ("gym_2_spawn_stand", new Vector3(1.2f, 1.7f, -7.5f), new Vector3(4.6f, 1.4f, -6f), 70f),
            ("gym_3_obstacles", new Vector3(-6f, 4.5f, -4f), new Vector3(-22f, 2f, 14f), 70f),
            ("gym_4_heist", new Vector3(10f, 3.5f, -4f), new Vector3(20f, 1f, 8f), 70f),
            ("gym_5_drill", new Vector3(13f, 1.7f, 5.2f), new Vector3(14f, 1.1f, 8f), 60f),
            ("gym_6_mirror_portal", new Vector3(1.5f, 1.7f, -4f), new Vector3(-2f, 1.4f, -10f), 75f),
        };
        var camGo = new GameObject("ShotCam"); var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f; cam.farClipPlane = 300f;
        var rt = new RenderTexture(1600, 900, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
        cam.transform.position = shots[0].pos; cam.transform.LookAt(shots[0].look);
        for (int i = 0; i < 2; i++) cam.Render();
        foreach (var s in shots)
        {
            cam.fieldOfView = s.fov; cam.transform.position = s.pos; cam.transform.LookAt(s.look); cam.Render(); RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            File.WriteAllBytes($"production/qa/evidence/{s.name}.png", tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        }
        RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(camGo);
        log.AppendLine($"  shots: {shots.Length} → production/qa/evidence/gym_*.png");
    }
}
