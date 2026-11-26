using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Процедурные ретро-текстуры для Главного Хаба в стиле Gorilla Tag / PS1 (генерируются кодом, без внешних файлов):
/// старый кирпич, потёртый бетон с трещинами и швами плит, дощатое дерево с сучками, ржавый рифлёный металл, ковры.
/// Каждая — BaseMap (PNG 256/128 px, Bilinear, мипмапы) + лёгкая NormalMap из карты высот. Бесшовные (тайлятся).
/// Папка: Assets/Art/Textures/Hub. Используется HubVisualOverhaul.
/// </summary>
public static class HubRetroTextures
{
    public const string Dir = "Assets/Art/Textures/Hub";

    // ------------------------------------------------------------------ шум (бесшовный)
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

    /// Value noise с периодом period клеток (бесшовный на [0,1)).
    static float VNoise(float u, float v, int period, int seed)
    {
        float x = u * period, y = v * period;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int ax = Mod(x0, period), bx = Mod(x0 + 1, period), ay = Mod(y0, period), by = Mod(y0 + 1, period);
        float a = Hash(ax, ay, seed), b = Hash(bx, ay, seed), c = Hash(ax, by, seed), d = Hash(bx, by, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static float Fbm(float u, float v, int period, int octaves, int seed)
    {
        float s = 0, amp = 0.5f, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            s += amp * VNoise(u, v, period << i, seed + i * 31);
            norm += amp; amp *= 0.5f;
        }
        return s / norm;
    }

    // ------------------------------------------------------------------ генераторы
    public delegate void PixelFn(int x, int y, out Color c, out float h);

    static void Render(int size, PixelFn fn, out Color[] cols, out float[] hs)
    {
        cols = new Color[size * size]; hs = new float[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                fn(x, y, out var c, out var h);
                cols[y * size + x] = c; hs[y * size + x] = h;
            }
    }

    /// Старый кирпич: 4×8 кирпичей с перевязкой, раствор, сколы, копоть.
    static void Brick(int x, int y, out Color c, out float h)
    {
        const int S = 256, bw = 64, bh = 32, mortar = 4;
        float u = x / (float)S, v = y / (float)S;
        int row = y / bh;
        int ox = Mod(x + (row % 2) * (bw / 2), S);
        int col = ox / bw, lx = ox % bw, ly = y % bh;
        int id = row * 8 + col;
        float edge = Mathf.Min(Mathf.Min(lx - mortar, bw - lx), Mathf.Min(ly - mortar, bh - ly));
        float n = Fbm(u, v, 16, 4, 7);
        float grime = Fbm(u, v, 3, 3, 11);
        bool chip = edge < 6 && Fbm(u, v, 24, 2, 5) > 0.62f;
        if (lx < mortar || ly < mortar || chip)
        {
            float m = 0.78f + 0.2f * n;
            c = new Color(0.50f, 0.47f, 0.42f) * m;
            h = 0.15f + 0.1f * n;
        }
        else
        {
            float r = Hash(id, 3, 99);
            var a = new Color(0.50f, 0.22f, 0.15f); var b = new Color(0.66f, 0.33f, 0.21f);
            c = Color.Lerp(a, b, r);
            if (Hash(id, 9, 17) < 0.15f) c = new Color(0.33f, 0.16f, 0.12f);   // обожжённые кирпичи
            c *= 0.82f + 0.36f * n;
            if (Hash(x, y, 3) < 0.04f) c *= 0.75f;                              // крапинки
            h = 0.75f + 0.2f * n - (edge < 3 ? 0.15f : 0f);
        }
        c *= 1f - 0.35f * Mathf.Clamp01((grime - 0.45f) * 2f);                  // копоть и грязь
        c.a = 1;
    }

    /// Потёртый бетон: пятна, крапинка, трещины, швы плит по краю (плита = вся текстура).
    static float[] crackMask;
    static void BuildCracks(int S)
    {
        crackMask = new float[S * S];
        for (int k = 0; k < 4; k++)
        {
            float x = Hash(k, 1, 51) * S, y = Hash(k, 2, 51) * S;
            float ang = Hash(k, 3, 51) * Mathf.PI * 2;
            int len = 90 + (int)(Hash(k, 4, 51) * 120);
            for (int i = 0; i < len; i++)
            {
                ang += (Hash(k, i, 77) - 0.5f) * 0.9f;
                x += Mathf.Cos(ang); y += Mathf.Sin(ang);
                int ix = Mod((int)x, S), iy = Mod((int)y, S);
                crackMask[iy * S + ix] = 1f;
                if (i % 3 == 0) crackMask[iy * S + Mod(ix + 1, S)] = 0.6f;
            }
        }
    }

    static void Concrete(int x, int y, out Color c, out float h)
    {
        const int S = 256;
        float u = x / (float)S, v = y / (float)S;
        float n = Fbm(u, v, 8, 5, 21);
        float stain = Fbm(u, v, 2, 3, 23);
        c = new Color(0.47f, 0.46f, 0.44f) * (0.78f + 0.4f * n);
        c *= 1f - 0.3f * Mathf.Clamp01((stain - 0.55f) * 3f);                   // масляные пятна
        float sp = Hash(x, y, 29);
        if (sp < 0.03f) c *= 0.7f; else if (sp > 0.985f) c *= 1.2f;              // крапинка заполнителя
        h = 0.5f + 0.25f * n;
        float cr = crackMask[y * S + x];
        if (cr > 0) { c *= 1f - 0.55f * cr; h -= 0.35f * cr; }
        if (x < 2 || y < 2) { c *= 0.55f; h = 0.1f; }                            // шов между плитами
        c.a = 1;
    }

    /// Доски: 4 доски вдоль U, волокна, сучки, щели, стыки с гвоздями.
    static void Wood(int x, int y, out Color c, out float h)
    {
        const int S = 256, ph = 64;
        float u = x / (float)S, v = y / (float)S;
        int plank = y / ph, ly = y % ph;
        float tone = Hash(plank, 1, 41);
        var baseC = Color.Lerp(new Color(0.42f, 0.27f, 0.15f), new Color(0.58f, 0.39f, 0.22f), tone);
        float warp = Fbm(u, v, 4, 3, 43 + plank) * 10f;
        float grain = 0.5f + 0.5f * Mathf.Sin((ly * 0.45f) + warp * 3f + Mathf.Sin(u * Mathf.PI * 2 * 3 + plank) * 1.5f);
        c = baseC * (0.8f + 0.25f * grain);
        h = 0.6f + 0.15f * grain;
        // сучки
        for (int k = 0; k < 2; k++)
        {
            float kx = Hash(plank, k, 47) * S, ky = plank * ph + 14 + Hash(plank, k + 5, 47) * (ph - 28);
            float dx = Mathf.Min(Mathf.Abs(x - kx), S - Mathf.Abs(x - kx)), dy = (y - ky) * 1.8f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d < 11f)
            {
                float ring = 0.5f + 0.5f * Mathf.Sin(d * 1.6f);
                c = Color.Lerp(c, new Color(0.22f, 0.12f, 0.06f), (1f - d / 11f) * 0.6f + ring * 0.2f);
                h -= (1f - d / 11f) * 0.2f;
            }
        }
        // стык досок с гвоздями
        int jx = (int)(Hash(plank, 9, 53) * S);
        int dj = Mathf.Min(Mathf.Abs(x - jx), S - Mathf.Abs(x - jx));
        if (dj < 2) { c *= 0.45f; h = 0.2f; }
        else if (dj > 4 && dj < 8 && (Mathf.Abs(ly - 14) < 2 || Mathf.Abs(ly - ph + 14) < 2)) { c = new Color(0.25f, 0.24f, 0.23f); h = 0.9f; }
        if (ly < 3) { c *= 0.3f; h = 0.05f; }                                    // щель между досками
        c *= 0.9f + 0.15f * Fbm(u, v, 16, 2, 59);
        c.a = 1;
    }

    /// Ржавый рифлёный металл: гофра вдоль V, облезлая краска, потёки ржавчины.
    static void RustMetal(int x, int y, out Color c, out float h)
    {
        const int S = 256;
        float u = x / (float)S, v = y / (float)S;
        float ridge = 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 2 * 8);
        float n = Fbm(u, v, 8, 4, 61);
        float streak = Fbm(u * 1f, v, 16, 2, 67) * 0.6f + Fbm(u, v * 0.25f, 4, 3, 71) * 0.4f;
        float rust = Mathf.Clamp01((n * 0.6f + streak * 0.7f - 0.55f) * 4f);
        var paint = new Color(0.33f, 0.39f, 0.40f) * (0.85f + 0.3f * ridge);
        var rustC = Color.Lerp(new Color(0.30f, 0.13f, 0.06f), new Color(0.55f, 0.27f, 0.11f), Fbm(u, v, 32, 2, 73));
        c = Color.Lerp(paint, rustC, rust);
        if (Hash(x, y, 79) < 0.05f) c *= 0.8f;
        h = 0.35f + 0.5f * ridge - 0.1f * rust;
        c.a = 1;
    }

    /// Ковёр 128 px: кайма, золотая полоса, ромб-медальон, мелкий узор, ворс.
    static Color rugMain, rugBorder, rugGold;
    static void Rug(int x, int y, out Color c, out float h)
    {
        const int S = 128;
        float u = x / (float)S, v = y / (float)S;
        int d = Mathf.Min(Mathf.Min(x, S - 1 - x), Mathf.Min(y, S - 1 - y));
        float med = Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f);
        if (d < 3) c = rugGold * 0.8f;
        else if (d < 14) c = ((x / 4 + y / 4) % 2 == 0 && d > 6 && d < 11) ? rugGold : rugBorder;
        else if (d < 16) c = rugGold;
        else if (med < 0.13f) c = rugGold;
        else if (med < 0.2f) c = rugBorder;
        else if (med < 0.23f) c = rugGold;
        else c = ((x / 8 + y / 8) % 3 == 0 && Hash(x / 8, y / 8, 83) > 0.5f) ? Color.Lerp(rugMain, rugGold, 0.35f) : rugMain;
        float fuzz = Fbm(u, v, 32, 2, 89);
        c *= 0.8f + 0.3f * fuzz;
        if (Hash(x, y, 97) < 0.06f) c *= 0.85f;
        h = 0.5f + 0.2f * fuzz;
        c.a = 1;
    }

    // ------------------------------------------------------------------ сохранение
    public static (Texture2D baseMap, Texture2D normal) Make(string name, int size, PixelFn fn, float normalStrength = 2.5f)
    {
        MapKit.EnsureFolder(Dir);
        Render(size, fn, out var cols, out var hs);
        string basePath = $"{Dir}/T_Hub_{name}.png", nPath = $"{Dir}/T_Hub_{name}_N.png";
        WritePng(basePath, size, cols);
        WritePng(nPath, size, HeightToNormal(hs, size, normalStrength));
        AssetDatabase.ImportAsset(basePath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(nPath, ImportAssetOptions.ForceSynchronousImport);
        Configure(basePath, size, false);
        Configure(nPath, size, true);
        return (AssetDatabase.LoadAssetAtPath<Texture2D>(basePath), AssetDatabase.LoadAssetAtPath<Texture2D>(nPath));
    }

    public static (Texture2D, Texture2D) Brick() => Make("Brick", 256, Brick);
    public static (Texture2D, Texture2D) Concrete() { BuildCracks(256); return Make("Concrete", 256, Concrete); }
    public static (Texture2D, Texture2D) Wood() => Make("Wood", 256, Wood);
    public static (Texture2D, Texture2D) RustMetal() => Make("RustMetal", 256, RustMetal, 3f);
    public static (Texture2D, Texture2D) Rug(string name, Color main, Color border, Color gold)
    {
        rugMain = main; rugBorder = border; rugGold = gold;
        return Make(name, 128, Rug, 1.5f);
    }

    static Color[] HeightToNormal(float[] h, int s, float strength)
    {
        var o = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = h[y * s + Mod(x + 1, s)] - h[y * s + Mod(x - 1, s)];
                float dy = h[Mod(y + 1, s) * s + x] - h[Mod(y - 1, s) * s + x];
                var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                o[y * s + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        return o;
    }

    static void WritePng(string path, int size, Color[] cols)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        t.SetPixels(cols);
        t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
    }

    static void Configure(string path, int size, bool normal)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = !normal;
        ti.maxTextureSize = size;
        ti.filterMode = FilterMode.Bilinear;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.mipmapEnabled = true;
        ti.anisoLevel = 0;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.SaveAndReimport();
    }
}
