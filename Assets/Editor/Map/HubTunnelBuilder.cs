using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Тёмный подземный бетонный тоннель прямо перед капотом чёрного автобуса (за северной стеной хаба).
/// Реалистично, без неона: грубый тёмно-серый бетон с рёбрами жёсткости (пилоны + балки потолка),
/// асфальт с потёртой разметкой, служебный тротуар-бордюр, редкие тусклые холодно-белые/янтарные
/// светильники у пола (гаснут в глубине) и плотный графитовый «туман» — полупрозрачные слои, начиная
/// сразу за концом удлинённого салона, так что дальний конец полностью растворяется в черноте.
///
/// Северная стена получает въезд: Wall_N скрыт, вместо него три куска (слева, справа, над въездом).
/// Вызывается из BlackBusBuilder.Run (идемпотентно, корень сцены «BusTunnel»).
/// </summary>
public static class HubTunnelBuilder
{
    public const string RootName = "BusTunnel";
    public const float HalfW = 3.0f, Height = 4.2f;      // въезд 6 × 4.2 м
    const float WallZ0 = 13.8f, WallZ1 = 14.2f, EndZ = 74f;
    const string MatDir = "Assets/Art/Materials/Tunnel";
    const string TexDir = "Assets/Art/Textures/Tunnel";

    static Material concrete, concreteDark, asphalt, paint, black, fog, lensCold, lensAmber, steel;

    public static string Build(Transform map, Transform bus, float interiorEndLocalZ, float busH, float interiorOuterHalfW)
    {
        var log = new System.Text.StringBuilder("  [Tunnel]");
        MapKit.EnsureFolder(MatDir); MapKit.EnsureFolder(TexDir);
        Materials();
        var scene = map.gameObject.scene;
        foreach (var g in scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray()) Object.DestroyImmediate(g);
        var root = new GameObject(RootName).transform;
        float cx = bus.position.x;
        float L0 = WallZ1, len = EndZ - L0, zc = (L0 + EndZ) / 2f;

        // ---- стена хаба с въездом
        var wall = map.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Wall_N");
        if (wall)
        {
            var wr = wall.GetComponent<MeshRenderer>();
            var wb = wr.bounds;
            wr.enabled = false;                                                     // целая стена → три куска
            var brick = wr.sharedMaterial;
            int layer = wall.gameObject.layer;
            var pieces = new GameObject("NorthWallWithMouth").transform; pieces.SetParent(root, false);
            void Piece(string n, float x0, float x1, float y0, float y1)
            {
                var size = new Vector3(x1 - x0, y1 - y0, wb.size.z);
                var go = new GameObject(n);
                go.transform.SetParent(pieces, false);
                go.transform.position = new Vector3((x0 + x1) / 2f, (y0 + y1) / 2f, wb.center.z);
                go.transform.localScale = size;
                go.layer = layer;
                go.AddComponent<MeshFilter>().sharedMesh = HubVisualOverhaul.BoxMesh(size, 1.6f);
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = brick; r.shadowCastingMode = ShadowCastingMode.Off;
            }
            Piece("Wall_N_Left", wb.min.x, cx - HalfW, wb.min.y, wb.max.y);
            Piece("Wall_N_Right", cx + HalfW, wb.max.x, wb.min.y, wb.max.y);
            Piece("Wall_N_Top", cx - HalfW, cx + HalfW, Height, wb.max.y);
        }

        // ---- портал: бетонная рамка + стальная балка
        var portal = Group(root, "Portal");
        foreach (float sx in new[] { -1f, 1f })
            Box(portal, "PortalPier", new Vector3(cx + sx * (HalfW + 0.2f), Height / 2f, WallZ0 - 0.12f), new Vector3(0.45f, Height, 0.3f), concrete, 1.2f);
        Box(portal, "PortalLintel", new Vector3(cx, Height + 0.25f, WallZ0 - 0.12f), new Vector3(HalfW * 2 + 0.85f, 0.5f, 0.3f), concrete, 1.2f);
        Box(portal, "PortalBeam", new Vector3(cx, Height - 0.1f, WallZ0 - 0.02f), new Vector3(HalfW * 2, 0.2f, 0.25f), steel, 1.5f);

        // ---- тоннель
        var t = Group(root, "Tube");
        Box(t, "Asphalt", new Vector3(cx, -0.1f, zc), new Vector3(HalfW * 2, 0.2f, len), asphalt, 3f);
        foreach (float sx in new[] { -1f, 1f })
        {
            Box(t, "Wall", new Vector3(cx + sx * (HalfW + 0.2f), Height / 2f, zc), new Vector3(0.4f, Height, len), concrete, 2.5f);
            Box(t, "Curb", new Vector3(cx + sx * (HalfW - 0.3f), 0.12f, zc), new Vector3(0.6f, 0.24f, len), concreteDark, 1.5f);
            Box(t, "EdgeLine", new Vector3(cx + sx * (HalfW - 0.75f), 0.005f, zc), new Vector3(0.12f, 0.01f, len), paint, 0);
        }
        Box(t, "Ceiling", new Vector3(cx, Height + 0.2f, zc), new Vector3(HalfW * 2 + 0.8f, 0.4f, len), concreteDark, 2.5f);
        Box(t, "EndWall", new Vector3(cx, Height / 2f, EndZ + 0.2f), new Vector3(HalfW * 2 + 0.8f, Height + 0.4f, 0.4f), black, 0);
        // осевая прерывистая разметка
        for (float z = L0 + 1.5f; z < EndZ - 2f; z += 6f)
            Box(t, "CenterDash", new Vector3(cx, 0.005f, z), new Vector3(0.12f, 0.01f, 3f), paint, 0);
        // рёбра жёсткости: пилоны у стен + балки потолка каждые 4 м
        for (float z = L0 + 2f; z < EndZ; z += 4f)
        {
            foreach (float sx in new[] { -1f, 1f })
                Box(t, "Rib", new Vector3(cx + sx * (HalfW - 0.12f), Height / 2f, z), new Vector3(0.25f, Height, 0.4f), concreteDark, 1.2f);
            Box(t, "CeilingRib", new Vector3(cx, Height - 0.2f, z), new Vector3(HalfW * 2, 0.4f, 0.4f), concreteDark, 1.2f);
        }

        // ---- служебные светильники у пола: редкие, тусклые, гаснут в глубине
        var lamps = Group(root, "ServiceLights");
        int li = 0, real = 0;
        for (float z = L0 + 3f; z < EndZ - 6f; z += 7f, li++)
        {
            float sx = li % 2 == 0 ? -1f : 1f;
            bool cold = li % 3 != 2;
            float fade = Mathf.Clamp01(1f - (z - L0) / 42f);
            var pos = new Vector3(cx + sx * (HalfW - 0.62f), 0.55f, z);
            Box(lamps, "LampHousing", pos + new Vector3(sx * 0.02f, 0, 0), new Vector3(0.06f, 0.16f, 0.36f), steel, 0);
            var lens = Box(lamps, "LampLens", pos + new Vector3(-sx * 0.015f, 0, 0), new Vector3(0.02f, 0.08f, 0.28f), cold ? lensCold : lensAmber, 0);
            var mpb = new MaterialPropertyBlock();
            Color baseC = cold ? new Color(0.75f, 0.85f, 1f) : new Color(1f, 0.6f, 0.25f);
            mpb.SetColor("_BaseColor", baseC * Mathf.Lerp(0.08f, 1.3f, fade));
            lens.GetComponent<MeshRenderer>().SetPropertyBlock(mpb);
            if (real < 4 && fade > 0.3f)
            {
                var l = MapKit.PointLight(lamps, "ServiceLight", pos + new Vector3(-sx * 0.25f, 0.1f, 0), baseC, 0.9f * fade, 5f);
                l.renderMode = LightRenderMode.ForcePixel;
                real++;
            }
        }

        // ---- графитовый туман: слои начинаются сразу за концом удлинённого салона
        float fogStart = bus.TransformPoint(new Vector3(0, 0, interiorEndLocalZ)).z + 0.6f;
        var fogG = Group(root, "Fog");
        int cards = 0;
        for (float z = Mathf.Max(fogStart, L0 + 1f); z < EndZ; z += 2.5f, cards++)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = "FogLayer";
            q.transform.SetParent(fogG, false);
            q.transform.SetPositionAndRotation(new Vector3(cx, Height / 2f, z), Quaternion.identity);
            q.transform.localScale = new Vector3(HalfW * 2, Height, 1);
            var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = fog; r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // декор у стены, который загораживал въезд, — в сторону
        var tires = map.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(x => x.name == "Hub_TiresAndBarrels");
        if (tires && tires.position.x < cx + HalfW + 1.6f) { tires.position = new Vector3(cx + HalfW + 2.1f, tires.position.y, tires.position.z); log.Append(" moved Hub_TiresAndBarrels;"); }

        foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            if (!tr.GetComponent<Light>() && tr.name != "FogLayer") GameObjectUtility.SetStaticEditorFlags(tr.gameObject, StaticEditorFlags.BatchingStatic);
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.allowOcclusionWhenDynamic = false;
        log.Append($" mouth x {cx - HalfW:F1}..{cx + HalfW:F1} h {Height}, length {len:F0} m, ribs every 4 m, service lights {li} ({real} real), fog layers {cards} from z {fogStart:F1}");
        return log.ToString();
    }

    // =========================================================================== материалы
    static void Materials()
    {
        var conTex = Noise("T_Tunnel_Concrete", 128, 0.42f, 0.10f, 6f, 0.035f, true);
        var asphTex = Noise("T_Tunnel_Asphalt", 128, 0.20f, 0.06f, 22f, 0.05f, false);
        concrete = Lit("M_Tunnel_Concrete", new Color(0.36f, 0.36f, 0.37f), conTex, 0.08f);
        concreteDark = Lit("M_Tunnel_ConcreteDark", new Color(0.24f, 0.24f, 0.25f), conTex, 0.06f);
        asphalt = Lit("M_Tunnel_Asphalt", new Color(0.55f, 0.55f, 0.56f), asphTex, 0.18f);
        paint = Lit("M_Tunnel_RoadPaint", new Color(0.42f, 0.41f, 0.36f), null, 0.12f);   // потёртая, не яркая
        steel = Lit("M_Tunnel_Steel", new Color(0.16f, 0.16f, 0.17f), null, 0.35f, 0.6f);
        black = Unlit("M_Tunnel_Black", Color.black, false);
        lensCold = Unlit("M_Tunnel_LampCold", new Color(0.75f, 0.85f, 1f), false);
        lensAmber = Unlit("M_Tunnel_LampAmber", new Color(1f, 0.6f, 0.25f), false);
        fog = Unlit("M_Tunnel_Fog", new Color(0.022f, 0.024f, 0.028f, 0.2f), true);
    }

    static Material Lit(string name, Color c, Texture2D tex, float smooth, float metal = 0f)
    {
        string path = $"{MatDir}/{name}.mat";
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetColor("_BaseColor", c); m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
        m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Unlit(string name, Color c, bool transparent)
    {
        string path = $"{MatDir}/{name}.mat";
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetColor("_BaseColor", c);
        if (transparent)
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    /// Шумовая текстура: бетон (пятна + горизонтальные швы опалубки) или асфальт (мелкое зерно).
    static Texture2D Noise(string name, int n, float baseV, float amp, float freq, float grain, bool formwork)
    {
        string path = $"{TexDir}/{name}.png";
        var t = new Texture2D(n, n, TextureFormat.RGB24, false);
        var rnd = new System.Random(name.GetHashCode());
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float k = baseV + (Mathf.PerlinNoise(u * freq + 3.1f, v * freq + 7.7f) - 0.5f) * amp * 2f
                          + (Mathf.PerlinNoise(u * freq * 4f, v * freq * 4f) - 0.5f) * amp + ((float)rnd.NextDouble() - 0.5f) * grain;
                if (formwork && (y % 32 == 0 || y % 32 == 1)) k *= 0.8f;            // швы опалубки
                t.SetPixel(x, y, new Color(k, k, k * 1.02f));
            }
        t.Apply();
        System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = TextureWrapMode.Repeat; imp.filterMode = FilterMode.Bilinear; imp.maxTextureSize = n;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // =========================================================================== примитивы
    static Transform Group(Transform p, string n) { var g = new GameObject(n).transform; g.SetParent(p, false); return g; }

    static GameObject Box(Transform p, string n, Vector3 pos, Vector3 size, Material m, float tile)
    {
        var go = new GameObject(n);
        go.transform.SetParent(p, false);
        go.transform.position = pos; go.transform.localScale = size;
        go.AddComponent<MeshFilter>().sharedMesh = tile > 0 ? HubVisualOverhaul.BoxMesh(size, tile) : Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }
}
