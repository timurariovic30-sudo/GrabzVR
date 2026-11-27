using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Импорт CC0-китов Quaternius (Modular Sci-Fi MegaKit, Stylized Nature MegaKit) из Assets/Art/ThirdParty/Quaternius.
/// Идемпотентный и ничего не трогает в игровых сценах:
///  • текстуры — максимум 1024 (Android 512), нормали помечены как Normal map;
///  • у каждого FBX встроенные материалы переназначены на URP Lit материалы кита (Materials/M_Q_*), текстуры подобраны по имени;
///  • превью-сцена Assets/Art/ThirdParty/Quaternius/QuaterniusKitPreview.unity (в Build Settings не добавляется) и снимки HubPreview/Kit_*.png.
/// Меню: Tools/ThirdParty/Quaternius Import + Preview. Batchmode (без -nographics, нужны снимки): -executeMethod QuaterniusKitImport.RunBatch
/// </summary>
public static class QuaterniusKitImport
{
    const string Root = "Assets/Art/ThirdParty/Quaternius";
    const string PreviewScene = Root + "/QuaterniusKitPreview.unity";

    [MenuItem("Tools/ThirdParty/Quaternius Import + Preview")]
    static void Menu() => Debug.Log(Run());

    public static void RunBatch()
    {
        string report;
        try { report = Run(); }
        catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); return; }
        Debug.Log(report);
        EditorApplication.Exit(report.Contains("ERROR") ? 1 : 0);
    }

    public static string Run()
    {
        var sb = new StringBuilder("[QuaterniusKitImport]\n");
        foreach (var kit in new[] { "SciFiMegaKit", "NatureMegaKit" })
        {
            SetupTextures($"{Root}/{kit}/Textures", sb);
            SetupModels(kit, sb);
        }
        AssetDatabase.SaveAssets();
        BuildPreview(sb);
        return sb.ToString();
    }

    // ------------------------------------------------------------------ текстуры
    static void SetupTextures(string dir, StringBuilder sb)
    {
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not TextureImporter ti) continue;
            string name = Path.GetFileNameWithoutExtension(path);
            bool normal = name.EndsWith("_Normal");
            bool linear = name.EndsWith("_ORM") || name.EndsWith("_DetailMask");
            bool changed = false;
            void Set<T>(T cur, T val, System.Action<T> apply) { if (!EqualityComparer<T>.Default.Equals(cur, val)) { apply(val); changed = true; } }
            Set(ti.textureType, normal ? TextureImporterType.NormalMap : TextureImporterType.Default, v => ti.textureType = v);
            Set(ti.sRGBTexture, !(normal || linear), v => ti.sRGBTexture = v);
            Set(ti.maxTextureSize, 1024, v => ti.maxTextureSize = v);
            Set(ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
            var android = ti.GetPlatformTextureSettings("Android");
            if (!android.overridden || android.maxTextureSize != 512 || android.format != TextureImporterFormat.ASTC_6x6)
            {
                android.overridden = true; android.maxTextureSize = 512; android.format = TextureImporterFormat.ASTC_6x6;
                ti.SetPlatformTextureSettings(android); changed = true;
            }
            if (changed) { ti.SaveAndReimport(); n++; }
        }
        sb.AppendLine($"  textures {dir}: reimported {n}");
    }

    // ------------------------------------------------------------------ модели и материалы
    static void SetupModels(string kit, StringBuilder sb)
    {
        string matDir = $"{Root}/{kit}/Materials";
        if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder($"{Root}/{kit}", "Materials");
        var textures = AssetDatabase.FindAssets("t:Texture2D", new[] { $"{Root}/{kit}/Textures" })
            .Select(AssetDatabase.GUIDToAssetPath).ToDictionary(p => Path.GetFileNameWithoutExtension(p), p => p);

        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var unmatched = new HashSet<string>();
        var made = new Dictionary<string, Material>();
        int models = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { $"{Root}/{kit}/FBX" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is not ModelImporter mi) continue;
            bool dirty = false;
            if (mi.materialImportMode != ModelImporterMaterialImportMode.ImportStandard) { mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; dirty = true; }
            if (mi.materialLocation != ModelImporterMaterialLocation.InPrefab) { mi.materialLocation = ModelImporterMaterialLocation.InPrefab; dirty = true; }
            if (mi.importCameras || mi.importLights) { mi.importCameras = false; mi.importLights = false; dirty = true; }
            if (dirty) mi.SaveAndReimport();

            // встроенные материалы (первый запуск) + уже переназначенные (повторный)
            var names = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(m => m.name)
                .Concat(mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).Select(k => k.name))
                .Distinct().ToList();
            bool remapped = false;
            foreach (var matName in names)
            {
                if (!made.TryGetValue(matName, out var mat))
                {
                    string mpath = $"{matDir}/M_Q_{Sanitize(matName)}.mat";
                    mat = AssetDatabase.LoadAssetAtPath<Material>(mpath);
                    if (!mat) { mat = new Material(lit); AssetDatabase.CreateAsset(mat, mpath); }
                    if (!FillMaterial(mat, matName, kit, textures)) unmatched.Add(matName);
                    EditorUtility.SetDirty(mat);
                    made[matName] = mat;
                }
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), matName);
                if (!mi.GetExternalObjectMap().TryGetValue(id, out var cur) || cur != mat) { mi.AddRemap(id, mat); remapped = true; }
            }
            if (remapped) mi.SaveAndReimport();
            models++;
        }
        sb.AppendLine($"  {kit}: models {models}, materials {made.Count} [{string.Join(", ", made.Keys.OrderBy(k => k))}]");
        if (unmatched.Count > 0) sb.AppendLine($"  {kit}: no texture match (flat colour kept) for: {string.Join(", ", unmatched.OrderBy(k => k))}");
    }

    static string Sanitize(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());

    /// Подобрать текстуры по имени материала. false — подходящей не нашлось (остаётся цвет из FBX).
    static bool FillMaterial(Material mat, string matName, string kit, Dictionary<string, string> tex)
    {
        Texture2D T(string n) => n != null && tex.TryGetValue(n, out var p) ? AssetDatabase.LoadAssetAtPath<Texture2D>(p) : null;
        string lower = matName.ToLowerInvariant();
        mat.SetFloat("_Smoothness", 0.15f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Back);

        if (kit == "SciFiMegaKit")
        {
            string key = lower.Contains("trim_01") || lower.Contains("trim01") || lower.Contains("trim1") ? "T_Trim_01"
                : lower.Contains("trim_02") || lower.Contains("trim02") || lower.Contains("trim2") ? "T_Trim_02"
                : lower.Contains("trim_03") || lower.Contains("trim03") || lower.Contains("trim3") ? "T_Trim_03"
                : lower.Contains("padd") ? "T_PaddedWall" : lower.Contains("decal") ? "T_Decals" : null;
            if (key == null)
            {
                // материалы без текстур — свой цвет
                mat.SetTexture("_BaseMap", null);
                if (lower == "m_black") { mat.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.06f)); return true; }
                if (lower.Contains("glass"))
                {
                    mat.SetColor("_BaseColor", new Color(0.55f, 0.75f, 0.85f, 0.35f));
                    mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f);
                    mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0f); mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.SetOverrideTag("RenderType", "Transparent"); mat.renderQueue = (int)RenderQueue.Transparent;
                    mat.SetFloat("_Smoothness", 0.8f);
                    return true;
                }
                if (lower.Contains("light"))
                {
                    Color c = lower.Contains("red") ? new Color(1f, 0.15f, 0.1f) : new Color(1f, 0.95f, 0.85f);
                    mat.SetColor("_BaseColor", c); mat.SetColor("_EmissionColor", c * 2f); mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    return true;
                }
                return false;
            }
            string colorVariant = lower.Contains("red") ? "_Red" : lower.Contains("blue") ? "_Blue" : "";
            var baseTex = T(key + "_BaseColor" + colorVariant) ?? T(key + "_BaseColor") ?? T(key);
            mat.SetTexture("_BaseMap", baseTex);
            mat.SetColor("_BaseColor", Color.white);
            var n = T(key + "_Normal");
            if (n) { mat.SetTexture("_BumpMap", n); mat.EnableKeyword("_NORMALMAP"); }
            var e = T(key + "_Emissive") ?? (key == "T_PaddedWall" ? T("T_PaddingWall_Emissive") : null);
            if (e) { mat.SetTexture("_EmissionMap", e); mat.SetColor("_EmissionColor", Color.white); mat.EnableKeyword("_EMISSION"); mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            if (key == "T_Decals") SetAlphaClip(mat);
            return baseTex != null;
        }
        else
        {
            // Nature: имена материалов совпадают с текстурами (Bark_NormalTree, Leaves_NormalTree, Rocks…)
            string best = tex.Keys.Where(k => !k.EndsWith("_Normal"))
                .OrderByDescending(k => Score(lower, k.ToLowerInvariant())).FirstOrDefault();
            if (best == null || Score(lower, best.ToLowerInvariant()) <= 0) return false;
            // у листвы серые маски (их красит шейдер автора); цветная версия — с суффиксом _C
            if (tex.ContainsKey(best + "_C")) best += "_C";
            mat.SetTexture("_BaseMap", T(best));
            mat.SetColor("_BaseColor", best == "Leaves" ? new Color(0.42f, 0.62f, 0.25f) : Color.white);
            var n = T(best + "_Normal");
            if (n) { mat.SetTexture("_BumpMap", n); mat.EnableKeyword("_NORMALMAP"); }
            if (lower.Contains("leaf") || lower.Contains("leaves") || lower.Contains("flower") || lower.Contains("grass") || lower.Contains("pine"))
            {
                SetAlphaClip(mat);
                mat.SetFloat("_Cull", (float)CullMode.Off);
            }
            return true;
        }
    }

    static int Score(string mat, string tex)
    {
        int s = 0;
        foreach (var part in tex.Split('_')) if (part.Length > 1 && part != "diffuse" && part != "c" && mat.Contains(part)) s += part.Length;
        return s;
    }

    static void SetAlphaClip(Material mat)
    {
        mat.SetFloat("_AlphaClip", 1f);
        mat.SetFloat("_Cutoff", 0.5f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.renderQueue = (int)RenderQueue.AlphaTest;
    }

    // ------------------------------------------------------------------ превью
    static void BuildPreview(StringBuilder sb)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.75f, 0.8f, 0.9f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.55f, 0.6f);
        RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.26f);
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.3f; sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

        float row = 0f;
        foreach (var (kit, folder, spacing) in new[] {
            ("SciFiMegaKit", "Walls", 4.5f), ("SciFiMegaKit", "Platforms", 4.5f), ("SciFiMegaKit", "Columns", 4f),
            ("SciFiMegaKit", "Props", 3f), ("NatureMegaKit", "", 6f) })
        {
            var dir = string.IsNullOrEmpty(folder) ? $"{Root}/{kit}/FBX" : $"{Root}/{kit}/FBX/{folder}";
            var paths = AssetDatabase.FindAssets("t:Model", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToList();
            var parent = new GameObject($"{kit}_{folder}").transform;
            int cols = 12;
            for (int i = 0; i < paths.Count; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.transform.position = new Vector3((i % cols) * spacing, 0f, row + (i / cols) * spacing);
            }
            row += Mathf.CeilToInt(paths.Count / (float)cols) * spacing + 6f;
            sb.AppendLine($"  preview {kit}/{folder}: {paths.Count}");
        }

        // небольшой «собранный» уголок хранилища из Sci-Fi кита — как он будет выглядеть в игре
        BuildVaultVignette(new Vector3(-30f, 0f, 0f), sb);
        BuildCampVignette(new Vector3(-30f, 0f, 40f), sb);

        EditorSceneManager.SaveScene(scene, PreviewScene);

        Directory.CreateDirectory("HubPreview");
        (string, Vector3, Vector3) Frame(string shot, string group, float height, float side = -1f)
        {
            var g = GameObject.Find(group);
            var rs = g ? g.GetComponentsInChildren<Renderer>() : new Renderer[0];
            if (rs.Length == 0) return (shot, Vector3.up * 10f, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float dist = Mathf.Max(b.extents.x, b.extents.z) * 1.25f + 4f;
            return (shot, b.center + new Vector3(0f, height + dist * 0.35f, side * dist), b.center);
        }
        var shots = new (string name, Vector3 pos, Vector3 look)[]
        {
            Frame("Kit_SciFi_Platforms", "SciFiMegaKit_Platforms", 3f),
            Frame("Kit_SciFi_Props", "SciFiMegaKit_Props", 1f),
            Frame("Kit_Nature", "NatureMegaKit_", 3f),
            ("Kit_VaultCorner", new Vector3(-30f, 1.7f, -5f), new Vector3(-30f, 1.4f, 8f)),
            ("Kit_CampCorner", new Vector3(-30f, 2f, 26f), new Vector3(-30f, 1.5f, 42f)),
        };
        var camGo = new GameObject("ShotCam");
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 60f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.65f, 0.78f);
        cam.farClipPlane = 400f;
        var rt = new RenderTexture(1280, 720, 24) { antiAliasing = 4 };
        cam.targetTexture = rt;
        foreach (var s in shots)
        {
            cam.transform.position = s.pos;
            cam.transform.LookAt(s.look);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes($"HubPreview/{s.name}.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            sb.AppendLine($"  shot HubPreview/{s.name}.png");
        }
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        EditorSceneManager.SaveScene(scene, PreviewScene);
    }

    static GameObject Place(string kitPath, Vector3 pos, float yaw, Transform parent)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/{kitPath}.fbx");
        if (!prefab) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        return go;
    }

    static void BuildVaultVignette(Vector3 o, StringBuilder sb)
    {
        var root = new GameObject("Vignette_Vault").transform;
        // пол из плит, стены по краям; размер клетки кита — 4 м (проверяется по bounds первой плиты)
        var probe = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/SciFiMegaKit/FBX/Platforms/Platform_Metal.fbx");
        float cell = 4f;
        if (probe)
        {
            var r = probe.GetComponentsInChildren<MeshFilter>().Select(m => m.sharedMesh.bounds.size).FirstOrDefault();
            if (r.x > 0.5f) cell = Mathf.Round(r.x * probe.transform.localScale.x);
        }
        sb.AppendLine($"  vault vignette: cell {cell} m");
        for (int x = -1; x <= 1; x++)
            for (int z = 0; z < 3; z++)
                Place("SciFiMegaKit/FBX/Platforms/" + ((x + z) % 2 == 0 ? "Platform_Metal" : "Platform_DarkPlates"), o + new Vector3(x * cell, 0, z * cell), 0, root);
        var walls = AssetDatabase.FindAssets("t:Model", new[] { $"{Root}/SciFiMegaKit/FBX/Walls" }).Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.Contains("Straight")).OrderBy(p => p).ToList();
        for (int i = 0; i < 3 && walls.Count > 0; i++)
        {
            string w = walls[(i * 7) % walls.Count].Replace($"{Root}/", "").Replace(".fbx", "");
            Place(w, o + new Vector3((i - 1) * cell, 0, 3 * cell - cell * 0.5f), 180f, root);
        }
        Place("SciFiMegaKit/FBX/Props/Prop_Chest", o + new Vector3(-2f, 0, 6f), 20f, root);
        Place("SciFiMegaKit/FBX/Props/Prop_Crate3", o + new Vector3(2.5f, 0, 5f), -15f, root);
        Place("SciFiMegaKit/FBX/Props/Prop_Computer", o + new Vector3(0.5f, 0, 8f), 180f, root);
        Place("SciFiMegaKit/FBX/Columns/Column_Pipes", o + new Vector3(-cell, 0, 2f * cell), 0, root);
        Place("SciFiMegaKit/FBX/Columns/Column_Pipes", o + new Vector3(cell, 0, 2f * cell), 0, root);
    }

    static void BuildCampVignette(Vector3 o, StringBuilder sb)
    {
        var root = new GameObject("Vignette_Camp").transform;
        var models = AssetDatabase.FindAssets("t:Model", new[] { $"{Root}/NatureMegaKit/FBX" }).Select(AssetDatabase.GUIDToAssetPath).ToList();
        string Pick(string prefix, int i) { var l = models.Where(p => Path.GetFileName(p).StartsWith(prefix)).OrderBy(p => p).ToList(); return l.Count == 0 ? null : l[i % l.Count].Replace($"{Root}/", "").Replace(".fbx", ""); }
        var rnd = new System.Random(7);
        for (int i = 0; i < 14; i++)
        {
            float a = i / 14f * Mathf.PI * 2f, d = 9f + (float)rnd.NextDouble() * 6f;
            var p = Pick(i % 3 == 0 ? "Pine" : "CommonTree", i);
            if (p != null) Place(p, o + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d), rnd.Next(360), root);
        }
        for (int i = 0; i < 20; i++)
        {
            float a = (float)rnd.NextDouble() * Mathf.PI * 2f, d = 2f + (float)rnd.NextDouble() * 7f;
            var p = Pick(i % 4 == 0 ? "Rock" : i % 4 == 1 ? "Bush" : i % 4 == 2 ? "Fern" : "Grass", i);
            if (p != null) Place(p, o + new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d), rnd.Next(360), root);
        }
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground"; ground.transform.SetParent(root); ground.transform.position = o; ground.transform.localScale = Vector3.one * 4f;
        var gm = new Material(Shader.Find("Universal Render Pipeline/Lit")); gm.SetColor("_BaseColor", new Color(0.33f, 0.45f, 0.22f)); gm.SetFloat("_Smoothness", 0f);
        ground.GetComponent<Renderer>().sharedMaterial = gm;
        sb.AppendLine($"  camp vignette: {root.childCount} objects");
    }
}
