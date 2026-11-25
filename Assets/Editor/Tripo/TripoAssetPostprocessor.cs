using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Retro "soapy" Gorilla-Tag style for everything dropped into Assets/Art/GeneratedModels/.
///
///  * Any texture file inside the folder (and its subfolders): Max Size 128, bilinear, mipmaps, compressed.
///  * Any model (.glb/.gltf/.fbx/.obj) dropped into the folder gets a ready-to-use retro copy:
///      GeneratedModels/&lt;Name&gt;/Textures/*.png   - base color downscaled to 128 px (glTF textures are
///                                                embedded in the .glb, so they are extracted first)
///      GeneratedModels/&lt;Name&gt;/Materials/*.mat  - URP Lit, Smoothness 0, Metallic 0, no specular/reflections
///      GeneratedModels/&lt;Name&gt;/&lt;Name&gt;.prefab     - the model with those materials + a BoxCollider
///    Use the PREFAB in scenes. Re-importing the model refreshes everything (GUIDs are kept).
///  * Menu: Tools/Heist/Tripo/Reprocess Generated Models.
/// </summary>
public class TripoAssetPostprocessor : AssetPostprocessor
{
    public const string Folder = "Assets/Art/GeneratedModels";
    public const int MaxTextureSize = 128;
    static readonly string[] ModelExt = { ".glb", ".gltf", ".fbx", ".obj" };

    static bool InFolder(string path) => path.Replace('\\', '/').StartsWith(Folder + "/");

    /// Модель игрока (Gorilla_Final) собирается отдельным билдером: текстура 512 px (атлас мелко
    /// нарезан — на 128 px шерсть «протекает» на живот), авто-префаб не нужен.
    public const string PlayerFolder = Folder + "/Gorilla_Final/";
    static bool IsPlayerAsset(string path) => path.Replace('\\', '/').StartsWith(PlayerFolder);

    static bool IsModel(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return System.Array.IndexOf(ModelExt, ext) >= 0;
    }

    // ---------------------------------------------------------------- textures
    void OnPreprocessTexture()
    {
        if (!InFolder(assetPath)) return;
        var ti = (TextureImporter)assetImporter;
        int max = IsPlayerAsset(assetPath) ? 512 : MaxTextureSize;
        ti.maxTextureSize = max;
        ti.mipmapEnabled = true;
        ti.filterMode = FilterMode.Bilinear;            // soft, "soapy" look
        ti.anisoLevel = 0;
        ti.textureCompression = TextureImporterCompression.Compressed;
        foreach (var platform in new[] { "Android", "Standalone" })
        {
            var ps = ti.GetPlatformTextureSettings(platform);
            if (ps.overridden && ps.maxTextureSize > max)
            {
                ps.maxTextureSize = max;
                ti.SetPlatformTextureSettings(ps);
            }
        }
    }

    // FBX/OBJ materials that Unity imports as editable assets
    void OnPostprocessMaterial(Material m)
    {
        if (InFolder(assetPath)) MakeMatte(m);
    }

    // ------------------------------------------------------------------ models
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        var todo = new List<string>();
        foreach (var p in imported)
            if (InFolder(p) && IsModel(p) && !IsPlayerAsset(p)) todo.Add(p);
        foreach (var p in moved)
            if (InFolder(p) && IsModel(p) && !IsPlayerAsset(p) && !todo.Contains(p)) todo.Add(p);
        if (todo.Count == 0) return;
        // creating assets inside the import callback is unsafe -> do it right after
        EditorApplication.delayCall += () => { foreach (var p in todo) Process(p); };
    }

    [MenuItem("Tools/Heist/Tripo/Reprocess Generated Models")]
    static void ReprocessAll()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new[] { Folder }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (IsModel(p) && !IsPlayerAsset(p)) Process(p);
        }
    }

    public static string Process(string modelPath)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null) { Debug.LogWarning($"[Tripo] could not load model {modelPath}"); return null; }

        string name = Path.GetFileNameWithoutExtension(modelPath);
        string dir = $"{Path.GetDirectoryName(modelPath).Replace('\\', '/')}/{name}";
        EnsureFolder(dir); EnsureFolder(dir + "/Textures"); EnsureFolder(dir + "/Materials");

        // 1) retro material for every source material
        var map = new Dictionary<Material, Material>();
        int texCount = 0;
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            foreach (var src in r.sharedMaterials)
            {
                if (src == null || map.ContainsKey(src)) continue;
                string baseName = Sanitize($"{name}_Mat{map.Count}");

                Texture tex = GetTex(src, "baseColorTexture", "_BaseMap", "_MainTex");
                Texture2D small = null;
                if (tex != null)
                {
                    small = SaveSmallTexture(tex, $"{dir}/Textures/{baseName}_BaseColor.png");
                    texCount++;
                }

                string matPath = $"{dir}/Materials/{baseName}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                bool isNew = mat == null;
                if (isNew) mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetTexture("_BaseMap", small);
                mat.SetColor("_BaseColor", GetColor(src, "baseColorFactor", "_BaseColor", "_Color"));
                MakeMatte(mat);
                if (isNew) AssetDatabase.CreateAsset(mat, matPath); else EditorUtility.SetDirty(mat);
                map[src] = mat;
            }

        // 2) prefab (variant of the model) with retro materials and a simple collider
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        int tris = 0;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && map.TryGetValue(mats[i], out var m)) mats[i] = m;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // cheaper on Quest
            if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh)
                tris += mf.sharedMesh.triangles.Length / 3;
            else if (r is SkinnedMeshRenderer smr && smr.sharedMesh)
                tris += smr.sharedMesh.triangles.Length / 3;
        }
        if (!go.TryGetComponent<Collider>(out _))
        {
            var b = WorldBounds(go);
            var box = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(b.center);
            box.size = b.size;
        }
        string prefabPath = $"{dir}/{name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();

        Debug.Log($"[Tripo] {name}: {tris} triangles, {map.Count} retro material(s), {texCount} texture(s) " +
                  $"@ <= {MaxTextureSize}px -> {prefabPath}");
        return prefabPath;
    }

    // ----------------------------------------------------------------- helpers
    public static void MakeMatte(Material m)
    {
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0f);
        if (m.HasProperty("roughnessFactor")) m.SetFloat("roughnessFactor", 1f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        if (m.HasProperty("metallicFactor")) m.SetFloat("metallicFactor", 0f);
        if (m.HasProperty("_SpecularHighlights"))
        {
            m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        }
        if (m.HasProperty("_EnvironmentReflections"))
        {
            m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }
    }

    static Texture GetTex(Material m, params string[] props)
    {
        foreach (var p in props)
            if (m.HasProperty(p) && m.GetTexture(p) != null) return m.GetTexture(p);
        return null;
    }

    static Color GetColor(Material m, params string[] props)
    {
        foreach (var p in props)
            if (m.HasProperty(p)) return m.GetColor(p);
        return Color.white;
    }

    /// Renders the (possibly non-readable, embedded) texture into a <=128 px PNG and imports it.
    static Texture2D SaveSmallTexture(Texture src, string path)
    {
        float scale = Mathf.Min(1f, (float)MaxTextureSize / Mathf.Max(src.width, src.height));
        int w = Mathf.Max(4, Mathf.RoundToInt(src.width * scale));
        int h = Mathf.Max(4, Mathf.RoundToInt(src.height * scale));

        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Graphics.Blit(src, rt);
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);   // -> OnPreprocessTexture: 128
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.5f);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static string Sanitize(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
