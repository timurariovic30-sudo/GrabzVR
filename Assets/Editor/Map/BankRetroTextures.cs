using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Процедурные ретро-текстуры для карты Банка (HeistLevel_Test) в стиле Gorilla Tag / PS1 — генерируются кодом:
/// мрамор с прожилками и трещинами (светлый и тёмный), известняковые блоки холла, строгие обои и деревянные панели офисов,
/// офисный ковролин, бронированные стальные плиты и рифлёный пол хранилища, потёртый асфальт и бетон гаража,
/// паркет «ёлочкой» и штукатурка галереи, потёртая жёлтая краска разметки.
/// Каждая — BaseMap (PNG 256/128 px, Bilinear, мипмапы) + лёгкая NormalMap из карты высот. Бесшовные (тайлятся).
/// Папка: Assets/Art/Materials/Bank/Textures. Используется BankVisualOverhaul.
/// </summary>
public static class BankRetroTextures
{
    public const string Dir = "Assets/Art/Materials/Bank/Textures";

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

    /// Случайные извилистые трещины (бесшовно, с переносом через край).
    static float[] Cracks(int S, int count, int minLen, int maxLen, int seed)
    {
        var m = new float[S * S];
        for (int k = 0; k < count; k++)
        {
            float x = Hash(k, 1, seed) * S, y = Hash(k, 2, seed) * S;
            float ang = Hash(k, 3, seed) * Mathf.PI * 2;
            int len = minLen + (int)(Hash(k, 4, seed) * (maxLen - minLen));
            for (int i = 0; i < len; i++)
            {
                ang += (Hash(k, i, seed + 7) - 0.5f) * 0.8f;
                x += Mathf.Cos(ang); y += Mathf.Sin(ang);
                int ix = Mod((int)x, S), iy = Mod((int)y, S);
                m[iy * S + ix] = 1f;
                if (i % 4 == 0) m[iy * S + Mod(ix + 1, S)] = 0.5f;
            }
        }
        return m;
    }

    public delegate void PixelFn(int x, int y, out Color c, out float h);

    // ------------------------------------------------------------------ мрамор
    static Color marbleA, marbleB, marbleVein;
    static float[] marbleCracks;
    /// Плиты 2×2 (по 128 px): прожилки (искривлённый синус по fbm), лёгкие тона плит, швы, тонкие трещины.
    static void Marble(int x, int y, out Color c, out float h)
    {
        const int S = 256, T = 128;
        float u = x / (float)S, v = y / (float)S;
        int tile = (x / T) + (y / T) * 2;
        float n = Fbm(u, v, 4, 5, 101 + tile);
        float warp = Fbm(u, v, 3, 4, 107) * 6f;
        float vein = Mathf.Abs(Mathf.Sin((u + v * 0.6f) * Mathf.PI * 4 + warp));
        float veinMask = Mathf.Pow(1f - vein, 10f);
        float veinThin = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin((u * 0.4f - v) * Mathf.PI * 6 + warp * 1.4f)), 22f);
        c = Color.Lerp(marbleA, marbleB, n * 0.9f + Hash(tile, 0, 103) * 0.25f);
        c = Color.Lerp(c, marbleVein, Mathf.Clamp01(veinMask * 0.85f + veinThin * 0.55f));
        h = 0.7f + 0.05f * n;
        float cr = marbleCracks[y * S + x];
        if (cr > 0) { c *= 1f - 0.4f * cr; h -= 0.3f * cr; }
        int lx = x % T, ly = y % T;
        if (lx < 2 || ly < 2) { c *= 0.62f; h = 0.15f; }                         // шов между плитами
        c.a = 1;
    }

    // ------------------------------------------------------------------ известняковые блоки холла
    /// Кладка из крупных блоков 128×64 с перевязкой, тонкий шов, пористость, лёгкая грязь снизу блока.
    static void Limestone(int x, int y, out Color c, out float h)
    {
        const int S = 256, bw = 128, bh = 64, mortar = 3;
        float u = x / (float)S, v = y / (float)S;
        int row = y / bh;
        int ox = Mod(x + (row % 2) * (bw / 2), S);
        int col = ox / bw, lx = ox % bw, ly = y % bh;
        int id = row * 4 + col;
        float n = Fbm(u, v, 16, 4, 121);
        float big = Fbm(u, v, 4, 2, 127);
        if (lx < mortar || ly < mortar)
        {
            c = new Color(0.55f, 0.52f, 0.46f) * (0.85f + 0.15f * n);
            h = 0.2f;
        }
        else
        {
            c = Color.Lerp(new Color(0.80f, 0.74f, 0.62f), new Color(0.90f, 0.85f, 0.73f), Hash(id, 2, 131));
            c *= 0.86f + 0.2f * n + 0.08f * big;
            if (Hash(x, y, 137) < 0.035f) c *= 0.82f;                           // поры
            c *= 1f - 0.12f * Mathf.Clamp01((12 - ly) / 12f);                    // потемнение у нижнего края блока
            float edge = Mathf.Min(Mathf.Min(lx - mortar, bw - lx), Mathf.Min(ly - mortar, bh - ly));
            h = 0.7f + 0.15f * n - (edge < 3 ? 0.2f : 0f);
        }
        c.a = 1;
    }

    // ------------------------------------------------------------------ штукатурка
    static Color plasterTint;
    static void Plaster(int x, int y, out Color c, out float h)
    {
        const int S = 256;
        float u = x / (float)S, v = y / (float)S;
        float n = Fbm(u, v, 8, 5, 141);
        float trowel = Fbm(u * 1f, v, 3, 3, 149);
        c = plasterTint * (0.9f + 0.12f * n + 0.06f * trowel);
        if (Hash(x, y, 151) < 0.02f) c *= 0.93f;
        h = 0.5f + 0.25f * n;
        c.a = 1;
    }

    // ------------------------------------------------------------------ обои офисов
    /// Строгие обои: тёмно-зелёные вертикальные полосы, тонкая золотистая нить, мелкий узор-ромбик, выцветание.
    static void Wallpaper(int x, int y, out Color c, out float h)
    {
        const int S = 256;
        float u = x / (float)S, v = y / (float)S;
        int stripe = x / 32, lx = x % 32;
        var dark = new Color(0.12f, 0.24f, 0.19f); var light = new Color(0.17f, 0.31f, 0.24f);
        c = stripe % 2 == 0 ? dark : light;
        if (lx == 0 || lx == 31) c = new Color(0.55f, 0.45f, 0.24f);              // золотистая нить
        if (stripe % 2 == 1)
        {
            int dx = Mathf.Abs((lx % 16) - 8), dy = Mathf.Abs((y % 16) - 8);
            if (dx + dy == 4) c = Color.Lerp(c, new Color(0.45f, 0.40f, 0.24f), 0.6f); // ромбик
        }
        float fade = Fbm(u, v, 4, 3, 161);
        c *= 0.85f + 0.25f * fade;
        h = 0.5f + (lx == 0 || lx == 31 ? 0.1f : 0f);
        c.a = 1;
    }

    // ------------------------------------------------------------------ деревянные панели (wainscot)
    /// Тёмный орех: рамка-филёнка 128×128, волокна, фаска.
    static void WoodPanel(int x, int y, out Color c, out float h)
    {
        const int S = 256, P = 128, frame = 18;
        float u = x / (float)S, v = y / (float)S;
        int lx = x % P, ly = y % P;
        int d = Mathf.Min(Mathf.Min(lx, P - 1 - lx), Mathf.Min(ly, P - 1 - ly));
        bool inFrame = d < frame;
        float warp = Fbm(u, v, 4, 3, 171) * 8f;
        float grain = inFrame && (lx < frame || lx > P - frame - 1) && !(ly < frame || ly > P - frame - 1)
            ? 0.5f + 0.5f * Mathf.Sin(x * 0.9f + warp * 2f)                       // вертикальные стойки
            : 0.5f + 0.5f * Mathf.Sin(y * 0.6f + warp * 2f + Mathf.Sin(u * 18f));
        c = Color.Lerp(new Color(0.20f, 0.11f, 0.06f), new Color(0.33f, 0.19f, 0.10f), grain * 0.7f + Fbm(u, v, 16, 2, 173) * 0.3f);
        h = 0.6f + 0.1f * grain;
        if (d == frame || d == frame + 1) { c *= 0.55f; h = 0.3f; }               // фаска филёнки
        else if (d < 2) { c *= 0.5f; h = 0.2f; }
        else if (!inFrame) { c *= 1.08f; h = 0.75f; }
        c.a = 1;
    }

    // ------------------------------------------------------------------ ковролин
    /// Офисная ковровая плитка 128 px: 2×2 плитки с разным направлением ворса, крапинка, шов.
    static void Carpet(int x, int y, out Color c, out float h)
    {
        const int S = 128, T = 64;
        float u = x / (float)S, v = y / (float)S;
        int tile = (x / T) + (y / T) * 2;
        bool vert = tile == 0 || tile == 3;
        float rib = 0.5f + 0.5f * Mathf.Sin((vert ? x : y) * 1.6f);
        c = new Color(0.20f, 0.23f, 0.32f) * (0.85f + 0.12f * rib + 0.15f * Fbm(u, v, 16, 2, 181));
        float sp = Hash(x, y, 187);
        if (sp < 0.06f) c = new Color(0.32f, 0.33f, 0.40f); else if (sp > 0.96f) c *= 0.6f;
        h = 0.5f + 0.2f * rib;
        if (x % T == 0 || y % T == 0) { c *= 0.7f; h = 0.3f; }
        c.a = 1;
    }

    // ------------------------------------------------------------------ бронеплиты хранилища
    static Color steelTint;
    /// Стальные плиты 2×2 (128 px): фаска, заклёпки по периметру, шлифовка, потёки и царапины.
    static void ArmorPlate(int x, int y, out Color c, out float h)
    {
        const int S = 256, P = 128;
        float u = x / (float)S, v = y / (float)S;
        int lx = x % P, ly = y % P, plate = (x / P) + (y / P) * 2;
        int d = Mathf.Min(Mathf.Min(lx, P - 1 - lx), Mathf.Min(ly, P - 1 - ly));
        float brush = Fbm(u * 0.1f, v, 64, 2, 191 + plate);
        float n = Fbm(u, v, 8, 4, 193);
        c = steelTint * (0.82f + 0.18f * brush + 0.15f * n + 0.08f * Hash(plate, 1, 197));
        h = 0.7f + 0.05f * brush;
        // потёки снизу вверх и грязь
        float streak = Fbm(u * 4f, v * 0.5f, 8, 2, 199);
        c *= 1f - 0.25f * Mathf.Clamp01((streak - 0.55f) * 3f);
        if (Hash(x, y, 211) < 0.01f) c *= 1.25f;                                  // царапины-искры
        if (d < 3) { c *= 0.45f; h = 0.1f; }                                     // шов
        else if (d < 6) { c *= 1.15f; h = 0.85f; }                               // фаска
        // заклёпки: каждые 16 px по периметру на отступе 10
        int rx = lx % 16 - 8, ry = ly % 16 - 8;
        bool rivetRow = (Mathf.Abs(lx - 10) < 4 || Mathf.Abs(lx - (P - 11)) < 4 || Mathf.Abs(ly - 10) < 4 || Mathf.Abs(ly - (P - 11)) < 4);
        if (rivetRow)
        {
            int cx = Mathf.Abs(lx - 10) < 4 ? lx - 10 : Mathf.Abs(lx - (P - 11)) < 4 ? lx - (P - 11) : rx;
            int cy = Mathf.Abs(ly - 10) < 4 ? ly - 10 : Mathf.Abs(ly - (P - 11)) < 4 ? ly - (P - 11) : ry;
            float rr = cx * cx + cy * cy;
            if (rr < 9) { c = steelTint * (1.25f - rr * 0.06f); h = 1f - rr * 0.05f; }
            else if (rr < 13) { c *= 0.6f; h = 0.5f; }
        }
        c.a = 1;
    }

    /// Рифлёный стальной пол (ромбы), потёртый в центре.
    static void TreadPlate(int x, int y, out Color c, out float h)
    {
        const int S = 256, C = 32;
        float u = x / (float)S, v = y / (float)S;
        int cx = x / C, cy = y / C;
        float lx = (x % C) - C / 2f, ly = (y % C) - C / 2f;
        bool flip = (cx + cy) % 2 == 0;
        // ромб-рифль: вытянутый эллипс под 45°
        float a = flip ? (lx + ly) * 0.707f : (lx - ly) * 0.707f, b = flip ? (lx - ly) * 0.707f : (lx + ly) * 0.707f;
        float e = (a * a) / 100f + (b * b) / 6f;
        float n = Fbm(u, v, 8, 4, 221);
        c = new Color(0.36f, 0.38f, 0.41f) * (0.8f + 0.3f * n);
        h = 0.4f;
        if (e < 1f) { c *= 1.25f + 0.2f * (1 - e); h = 0.9f - 0.3f * e; }
        float wear = Fbm(u, v, 2, 3, 227);
        c *= 1f - 0.2f * Mathf.Clamp01((wear - 0.6f) * 3f);
        if (Hash(x, y, 229) < 0.03f) c *= 0.8f;
        c.a = 1;
    }

    // ------------------------------------------------------------------ асфальт и бетон гаража
    static float[] asphaltCracks;
    static void Asphalt(int x, int y, out Color c, out float h)
    {
        const int S = 256;
        float u = x / (float)S, v = y / (float)S;
        float n = Fbm(u, v, 16, 4, 241);
        float patch = Fbm(u, v, 2, 3, 243);
        c = new Color(0.20f, 0.20f, 0.21f) * (0.8f + 0.35f * n);
        float sp = Hash(x, y, 247);
        if (sp < 0.08f) c = new Color(0.36f, 0.35f, 0.33f) * (0.8f + 0.4f * n);  // щебень
        else if (sp > 0.97f) c *= 0.55f;
        c *= 1f - 0.35f * Mathf.Clamp01((patch - 0.58f) * 4f);                   // масляные пятна
        if (patch < 0.3f) c *= 1.12f;                                            // выгоревшие участки
        h = 0.5f + 0.3f * n + (sp < 0.08f ? 0.15f : 0f);
        float cr = asphaltCracks[y * S + x];
        if (cr > 0) { c *= 1f - 0.6f * cr; h -= 0.4f * cr; }
        c.a = 1;
    }

    static float[] concreteCracks;
    /// Бетон с отпечатками опалубки: горизонтальные доски, дырки от стяжек, пятна, трещины.
    static void FormConcrete(int x, int y, out Color c, out float h)
    {
        const int S = 256;
        float u = x / (float)S, v = y / (float)S;
        int board = y / 32;
        float n = Fbm(u, v, 8, 5, 251);
        float grain = Fbm(u * 0.25f, v, 32, 2, 253 + board);
        c = new Color(0.50f, 0.49f, 0.47f) * (0.8f + 0.25f * n + 0.08f * grain + 0.05f * Hash(board, 0, 257));
        h = 0.55f + 0.2f * n + 0.05f * grain;
        if (y % 32 < 1) { c *= 0.75f; h = 0.35f; }                               // стык досок опалубки
        int tx = Mod(x - 64, 128) - 64, ty = Mod(y - 64, 128) - 64;
        float tr = tx * tx + ty * ty;
        if (tr < 20) { c *= 0.35f; h = 0.05f; }                                   // отверстия стяжек
        else if (tr < 40) { c *= 0.8f; h = 0.45f; }
        float stain = Fbm(u, v, 2, 3, 259);
        c *= 1f - 0.25f * Mathf.Clamp01((stain - 0.55f) * 3f);
        float cr = concreteCracks[y * S + x];
        if (cr > 0) { c *= 1f - 0.5f * cr; h -= 0.3f * cr; }
        c.a = 1;
    }

    // ------------------------------------------------------------------ паркет «ёлочкой»
    static void Herringbone(int x, int y, out Color c, out float h)
    {
        // штучный паркет «плетёнка»: квадраты 64 px по 4 планки, направление чередуется шахматкой
        const int S = 256, Q = 64, W = 16;
        float u = x / (float)S, v = y / (float)S;
        int qx = x / Q, qy = y / Q;
        int k = (qx + qy) % 2;
        int lx = x % Q, ly = y % Q;
        int lw = k == 0 ? ly % W : lx % W;                                        // поперёк планки
        int ll = k == 0 ? lx : ly;                                                // вдоль планки
        int plank = (qx * 4 + qy * 16) * 4 + (k == 0 ? ly / W : lx / W);
        float tone = Hash(plank, k, 271);
        var baseC = Color.Lerp(new Color(0.45f, 0.28f, 0.14f), new Color(0.62f, 0.42f, 0.22f), tone);
        float warp = Fbm(u, v, 4, 2, 277) * 4f;
        float grain = 0.5f + 0.5f * Mathf.Sin((k == 0 ? y : x) * 1.3f + warp + plank);
        c = baseC * (0.82f + 0.2f * grain);
        h = 0.6f + 0.1f * grain;
        if (lw == 0 || ll == 0) { c *= 0.45f; h = 0.15f; }                       // стыки планок
        c *= 0.92f + 0.12f * Fbm(u, v, 8, 2, 281);
        c.a = 1;
    }

    // ------------------------------------------------------------------ потёртая жёлтая краска
    static void PaintWorn(int x, int y, out Color c, out float h)
    {
        const int S = 128;
        float u = x / (float)S, v = y / (float)S;
        float wear = Fbm(u, v, 8, 4, 291);
        var paint = new Color(0.95f, 0.72f, 0.10f) * (0.85f + 0.2f * Fbm(u, v, 32, 2, 293));
        var under = new Color(0.22f, 0.22f, 0.22f);
        float t = Mathf.Clamp01((wear - 0.62f) * 5f);
        if (Hash(x, y, 297) < 0.05f) t = Mathf.Max(t, 0.7f);
        c = Color.Lerp(paint, under, t);
        h = 0.6f - 0.3f * t;
        c.a = 1;
    }

    // ------------------------------------------------------------------ публичное API
    public static (Texture2D, Texture2D) MarbleLight()
    {
        marbleA = new Color(0.86f, 0.84f, 0.80f); marbleB = new Color(0.95f, 0.94f, 0.91f); marbleVein = new Color(0.45f, 0.44f, 0.46f);
        marbleCracks = Cracks(256, 3, 40, 110, 109);
        return Make("Marble", 256, Marble, 2f);
    }
    public static (Texture2D, Texture2D) MarbleDark()
    {
        marbleA = new Color(0.08f, 0.12f, 0.10f); marbleB = new Color(0.15f, 0.21f, 0.18f); marbleVein = new Color(0.70f, 0.62f, 0.42f);
        marbleCracks = Cracks(256, 2, 40, 90, 113);
        return Make("MarbleDark", 256, Marble, 2f);
    }
    public static (Texture2D, Texture2D) Limestone() => Make("Limestone", 256, Limestone, 2.5f);
    public static (Texture2D, Texture2D) Plaster(string name, Color tint) { plasterTint = tint; return Make(name, 256, Plaster, 1.5f); }
    public static (Texture2D, Texture2D) Wallpaper() => Make("Wallpaper", 256, Wallpaper, 1f);
    public static (Texture2D, Texture2D) WoodPanel() => Make("WoodPanel", 256, WoodPanel, 2.5f);
    public static (Texture2D, Texture2D) Carpet() => Make("OfficeCarpet", 128, Carpet, 1.5f);
    public static (Texture2D, Texture2D) ArmorPlate() { steelTint = new Color(0.42f, 0.46f, 0.50f); return Make("ArmorPlate", 256, ArmorPlate, 3f); }
    public static (Texture2D, Texture2D) TreadPlate() => Make("TreadPlate", 256, TreadPlate, 3f);
    public static (Texture2D, Texture2D) Asphalt() { asphaltCracks = Cracks(256, 5, 60, 160, 249); return Make("Asphalt", 256, Asphalt, 2.5f); }
    public static (Texture2D, Texture2D) Concrete() { concreteCracks = Cracks(256, 3, 50, 120, 261); return Make("FormConcrete", 256, FormConcrete, 2.5f); }
    public static (Texture2D, Texture2D) Parquet() => Make("Herringbone", 256, Herringbone, 2f);
    public static (Texture2D, Texture2D) PaintYellow() => Make("PaintWorn", 128, PaintWorn, 1.5f);

    // ------------------------------------------------------------------ сохранение
    public static (Texture2D baseMap, Texture2D normal) Make(string name, int size, PixelFn fn, float normalStrength)
    {
        MapKit.EnsureFolder(Dir);
        var cols = new Color[size * size]; var hs = new float[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                fn(x, y, out var c, out var h);
                cols[y * size + x] = c; hs[y * size + x] = h;
            }
        string basePath = $"{Dir}/T_Bank_{name}.png", nPath = $"{Dir}/T_Bank_{name}_N.png";
        WritePng(basePath, size, cols);
        WritePng(nPath, size, HeightToNormal(hs, size, normalStrength));
        AssetDatabase.ImportAsset(basePath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(nPath, ImportAssetOptions.ForceSynchronousImport);
        Configure(basePath, size, false);
        Configure(nPath, size, true);
        return (AssetDatabase.LoadAssetAtPath<Texture2D>(basePath), AssetDatabase.LoadAssetAtPath<Texture2D>(nPath));
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
