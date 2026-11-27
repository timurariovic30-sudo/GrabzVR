using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Пол главного зала MainHub: тёмное матовое состаренное дерево (венге / тёмный орех).
/// Идемпотентный: генерирует текстуры T_Hub_FloorDarkWood(+_N), создаёт/обновляет материал
/// M_Hub_FloorDarkWood и назначает его ТОЛЬКО рендереру пола HubMap/Static/Floor.
/// Геометрию, коллайдер, меш (UV), позицию и другие объекты не трогает.
///
/// Масштаб: у меша пола (HubBox_*_2.5) UV в метрах / 2.5, т.е. одна текстура = 2.5 × 2.5 м.
/// В текстуре 16 досок по ~15.6 см шириной вдоль оси U (= мировая X, «горизонтально»),
/// длина досок 1.25–2.5 м, стыки вразбежку. Tiling материала 1:1.
/// HubVisualOverhaul этот материал не переназначает (его нет в таблице перетекстурирования).
/// </summary>
public static class HubFloorDarkWood
{
    const string TexDir = "Assets/Art/Textures/Hub";
    const string MatPath = "Assets/Art/Materials/Hub/M_Hub_FloorDarkWood.mat";
    const int S = 512;            // 512 px на 2.5 м ≈ 5 мм на пиксель
    const int Planks = 16;        // досок на текстуру → 2.5 м / 16 = 15.6 см
    const float Smoothness = 0.2f;

    // Палитра (sRGB) из задания
    static readonly Color Base = Hex(0x1E130C);
    static readonly Color Vein = Hex(0x2E1D12);
    static readonly Color Seam = Hex(0x0A0604);

    static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

    [MenuItem("Tools/Heist/Hub/Floor Dark Wood")]
    public static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "MainHub") { EditorUtility.DisplayDialog("Floor Dark Wood", "Откройте сцену MainHub.", "OK"); return; }

        var floor = FindFloor();
        if (!floor) { Debug.LogError("[FloorDarkWood] HubMap/Static/Floor не найден"); return; }

        var (baseMap, normal) = BuildTextures();
        var mat = BuildMaterial(baseMap, normal);

        var r = floor.GetComponent<MeshRenderer>();
        if (r.sharedMaterial != mat)
        {
            Undo.RecordObject(r, "Floor Dark Wood");
            r.sharedMaterial = mat;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        Debug.Log($"[FloorDarkWood] готово: {GetPath(floor.transform)} → {mat.name} (Smoothness {Smoothness}, {Planks} досок на 2.5 м)");
    }

    /// Пол основного зала: объект Floor в HubMap/Static с мешем HubBox_* и BoxCollider.
    static GameObject FindFloor()
    {
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r.name != "Floor" || !r.transform.parent || r.transform.parent.name != "Static") continue;
            var mf = r.GetComponent<MeshFilter>();
            if (mf && mf.sharedMesh && mf.sharedMesh.name.StartsWith("HubBox_") && r.GetComponent<BoxCollider>())
                return r.gameObject;
        }
        return null;
    }

    static string GetPath(Transform t) => t.parent ? GetPath(t.parent) + "/" + t.name : t.name;

    // ------------------------------------------------------------------ шум (бесшовный, анизотропный)
    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    static int Mod(int a, int m) => ((a % m) + m) % m;

    /// Value noise с отдельными периодами по U и V (бесшовный на [0,1)).
    static float VNoise(float u, float v, int pu, int pv, int seed)
    {
        float x = u * pu, y = v * pv;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int ax = Mod(x0, pu), bx = Mod(x0 + 1, pu), ay = Mod(y0, pv), by = Mod(y0 + 1, pv);
        return Mathf.Lerp(Mathf.Lerp(Hash(ax, ay, seed), Hash(bx, ay, seed), fx),
                          Mathf.Lerp(Hash(ax, by, seed), Hash(bx, by, seed), fx), fy);
    }

    static float Fbm(float u, float v, int pu, int pv, int oct, int seed)
    {
        float s = 0, a = 0.5f, n = 0;
        for (int i = 0; i < oct; i++) { s += a * VNoise(u, v, pu << i, pv << i, seed + i * 31); n += a; a *= 0.5f; }
        return s / n;
    }

    // ------------------------------------------------------------------ доски
    static void Pixel(int x, int y, out Color c, out float h)
    {
        const int ph = S / Planks;                       // 32 px на доску
        float u = x / (float)S, v = y / (float)S;
        int plank = y / ph, ly = y % ph;

        // Стыки торцов: 1 или 2 на ряд, вразбежку → доски 2.5 м или 1.25 м
        int joints = Hash(plank, 1, 7) < 0.5f ? 1 : 2;
        int j0 = (int)(Hash(plank, 2, 7) * S);
        int seg = 0, dj = S;
        for (int k = 0; k < joints; k++)
        {
            int jx = Mod(j0 + k * S / joints, S);
            int d = Mathf.Min(Mod(x - jx, S), Mod(jx - x, S));
            if (d < dj) dj = d;
            if (Mod(x - jx, S) < S / joints) seg = k;
        }
        int board = plank * 4 + seg;                     // id отдельной доски для тона и рисунка

        // Тон доски: лёгкий разброс, обожжённые/выцветшие доски
        float tone = Hash(board, 3, 11);
        var col = Base * (0.85f + 0.3f * tone);

        // Древесный рисунок: тянется вдоль U, волнистые прожилки + мелкие волокна
        float pu = (int)Mathf.Lerp(2, 4, Hash(board, 4, 13));
        float warp = Fbm(u, v, (int)pu, Planks * 2, 3, 17 + board) * 6f;
        float ring = Mathf.Abs(Mathf.Sin(ly * 0.32f + warp + Hash(board, 5, 13) * 6.28f));
        float veins = Mathf.Pow(1f - ring, 6f);          // тонкие тёплые прожилки
        float fibers = Fbm(u, v, 8, S / 4, 2, 19);      // волокна: длинные вдоль U, частые по V
        col = Color.Lerp(col, Vein * (1.0f + 0.25f * tone), Mathf.Clamp01(veins * 0.9f + (fibers - 0.5f) * 0.8f));
        col *= 0.9f + 0.2f * Fbm(u, v, 6, 48, 2, 23);    // крупные пятна потёртости
        h = 0.6f + 0.12f * fibers - 0.06f * veins;

        // Сучки (редко)
        if (Hash(board, 6, 29) < 0.35f)
        {
            float kx = Hash(board, 7, 29) * S, ky = plank * ph + 8 + Hash(board, 8, 29) * (ph - 16);
            float dx = Mathf.Min(Mathf.Abs(x - kx), S - Mathf.Abs(x - kx)) * 0.55f, dy = y - ky;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d < 7f)
            {
                float k = 1f - d / 7f;
                col = Color.Lerp(col, Seam * 1.6f, k * 0.7f + 0.15f * Mathf.Sin(d * 2.2f));
                h -= k * 0.15f;
            }
        }

        // Царапины (состаренность): короткие светлые штрихи вдоль досок
        if (Fbm(u, v, 64, 4, 1, 31) > 0.82f && Hash(x / 6, y, 37) > 0.4f) col *= 1.12f;

        // Фаска к краям доски и тёмные швы между досками
        int edge = Mathf.Min(ly, ph - 1 - ly);
        if (edge < 3) { float k = (3 - edge) / 3f; col = Color.Lerp(col, Seam * 1.3f, 0.45f * k); h -= 0.25f * k; }
        if (ly == 0 || ly == ph - 1) { col = Seam; h = 0.05f; }
        if (dj < 2) { col = Seam; h = 0.08f; }
        else if (dj < 4) { col = Color.Lerp(col, Seam, 0.4f); h -= 0.15f; }

        col.a = 1;
        c = col;
    }

    // ------------------------------------------------------------------ текстуры и материал
    static (Texture2D, Texture2D) BuildTextures()
    {
        if (!AssetDatabase.IsValidFolder(TexDir)) Directory.CreateDirectory(TexDir);
        var cols = new Color[S * S]; var hs = new float[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++) { Pixel(x, y, out var c, out var h); cols[y * S + x] = c; hs[y * S + x] = h; }

        var nrm = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = hs[y * S + Mod(x + 1, S)] - hs[y * S + Mod(x - 1, S)];
                float dy = hs[Mod(y + 1, S) * S + x] - hs[Mod(y - 1, S) * S + x];
                var n = new Vector3(-dx * 3f, -dy * 3f, 1f).normalized;
                nrm[y * S + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }

        string bp = $"{TexDir}/T_Hub_FloorDarkWood.png", np = $"{TexDir}/T_Hub_FloorDarkWood_N.png";
        Write(bp, cols); Write(np, nrm);
        Configure(bp, false); Configure(np, true);
        return (AssetDatabase.LoadAssetAtPath<Texture2D>(bp), AssetDatabase.LoadAssetAtPath<Texture2D>(np));
    }

    static void Write(string path, Color[] cols)
    {
        var t = new Texture2D(S, S, TextureFormat.RGBA32, false, true);   // linear: значения пишем как есть (sRGB-коды палитры)
        t.SetPixels(cols); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
    }

    static void Configure(string path, bool normal)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = !normal;
        ti.maxTextureSize = S;
        ti.filterMode = FilterMode.Trilinear;
        ti.anisoLevel = 2;                                // пол виден под острым углом — меньше «мыла» вдали
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.mipmapEnabled = true;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.SaveAndReimport();
    }

    static Material BuildMaterial(Texture2D baseMap, Texture2D normal)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, MatPath); }
        else if (m.shader != shader) m.shader = shader;

        m.SetTexture("_BaseMap", baseMap);
        m.SetTextureScale("_BaseMap", Vector2.one);       // UV меша уже в метрах (1 = 2.5 м)
        m.SetColor("_BaseColor", Color.white);
        m.SetTexture("_BumpMap", normal);
        m.SetFloat("_BumpScale", 0.7f);
        m.EnableKeyword("_NORMALMAP");
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Smoothness", Smoothness);           // матовое дерево, мягкий широкий блик
        m.SetFloat("_SpecularHighlights", 1f);
        m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
        m.SetFloat("_EnvironmentReflections", 0f);       // без отражений, как у остальных M_Hub_*
        m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
        return m;
    }
}
