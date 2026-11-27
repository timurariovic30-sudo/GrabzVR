using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Мебель банка, собранная вручную из примитивов (без Tripo) — точные размеры, ничего не торчит сквозь стены.
/// Каждый предмет — группа с началом в центре пола под ним; «лицо» (сторона к человеку) смотрит в локальную −Z.
/// Поворот yaw: 0 — лицом на −Z (на юг), 90 — на −X, −90 — на +X, 180 — на +Z.
/// Коллайдеры — BoxCollider/CapsuleCollider на слое Default (руки GorillaLocomotion цепляются, ПК-игрок упирается).
/// </summary>
public static partial class BankVisualOverhaul
{
    static Material glassMat, leather, leatherLight, beige, blackMetal, ledGreen, waterBlue, mahogany, paper;

    static void BuildFurnitureMaterials()
    {
        glassMat = GetMat("Glass", "Universal Render Pipeline/Lit");                   // бронестекло: прозрачное, слегка голубое
        glassMat.SetColor("_BaseColor", new Color(0.75f, 0.92f, 1f, 0.22f));
        glassMat.SetFloat("_Surface", 1f); glassMat.SetFloat("_Blend", 0f);
        glassMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); glassMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        glassMat.SetFloat("_ZWrite", 0f);
        glassMat.SetOverrideTag("RenderType", "Transparent"); glassMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        glassMat.renderQueue = (int)RenderQueue.Transparent;
        TripoAssetPostprocessor.MakeMatte(glassMat);
        EditorUtility.SetDirty(glassMat);
        leather = Emissive("Leather", new Color(0.5f, 0.2f, 0.14f), 0f);
        leatherLight = Emissive("LeatherLight", new Color(0.62f, 0.27f, 0.18f), 0f);
        beige = Emissive("BeigePlastic", new Color(0.86f, 0.8f, 0.68f), 0f);
        blackMetal = Emissive("BlackMetal", new Color(0.09f, 0.09f, 0.1f), 0f);
        ledGreen = Emissive("LedGreen", new Color(0.25f, 1f, 0.35f), 3f);
        waterBlue = Emissive("WaterBlue", new Color(0.35f, 0.65f, 1f), 0.4f);
        mahogany = Lit("Mahogany", (Texture2D)woodPanel.GetTexture("_BaseMap"), (Texture2D)woodPanel.GetTexture("_BumpMap"), new Color(0.95f, 0.62f, 0.55f), 1.0f);
        paper = Emissive("Paper", new Color(0.95f, 0.94f, 0.9f), 0f);
    }

    // ------------------------------------------------------------------ помощники локальных координат
    static Transform G(Transform parent, string name, Vector3 floorPos, float yaw)
    {
        var g = Group(parent, name);
        g.SetPositionAndRotation(floorPos, Quaternion.Euler(0, yaw, 0));
        return g;
    }

    static GameObject P(Transform g, string name, Vector3 c, Vector3 size, Material m, bool col = false, Vector3? euler = null)
        => Box(g, name, g.TransformPoint(c), size, m, col, g.rotation * Quaternion.Euler(euler ?? Vector3.zero));

    static GameObject Pc(Transform g, string name, Vector3 c, float r, float h, Material m, bool col = false, Vector3? euler = null)
        => Cyl(g, name, g.TransformPoint(c), r, h, m, col, g.rotation * Quaternion.Euler(euler ?? Vector3.zero));

    static TextMeshPro Pt(Transform g, string name, string text, Vector3 c, float size, Vector2 rect, Material neon, Color? color = null)
    {
        var t = Label(g, name, text, size, color ?? Color.white, g.TransformPoint(c), g.TransformDirection(Vector3.back), rect);
        if (neon) t.fontSharedMaterial = neon;
        return t;
    }

    // ------------------------------------------------------------------ кассовая стойка
    /// Длинная кассовая стойка Майами: бирюзовый корпус с золотыми полосами, мраморная столешница, бронестекло
    /// с окошками и лотками для денег, логотип SUNSET TRUST BANK на фасаде. Клиенты — со стороны −Z.
    static Transform TellerDesk(Transform parent, Vector3 pos, float yaw, float L)
    {
        var g = G(parent, "TellerDesk", pos, yaw);
        P(g, "Body", new Vector3(0, 0.5f, 0.05f), new Vector3(L, 1.0f, 0.7f), stuccoTeal, true);
        P(g, "Plinth", new Vector3(0, 0.05f, -0.29f), new Vector3(L, 0.1f, 0.04f), brass);
        foreach (float y in new[] { 0.3f, 0.8f }) P(g, "GoldStripe", new Vector3(0, y, -0.31f), new Vector3(L, 0.04f, 0.02f), brass);
        int n = Mathf.Max(2, Mathf.RoundToInt(L / 1.45f));
        float w = L / n;
        for (int i = 0; i <= n; i++) P(g, "GoldFin", new Vector3(-L / 2 + i * w, 0.5f, -0.31f), new Vector3(0.05f, 1.0f, 0.03f), brass);
        P(g, "Top", new Vector3(0, 1.03f, -0.02f), new Vector3(L + 0.08f, 0.06f, 0.9f), marbleGold, true);
        // бронестекло: стойки, верхняя перекладина, стёкла с прорезью для лотка
        for (int i = 0; i <= n; i++) P(g, "GlassPost", new Vector3(-L / 2 + i * w, 1.56f, 0.02f), new Vector3(0.06f, 1.0f, 0.06f), brass, true);
        P(g, "GlassRail", new Vector3(0, 2.09f, 0.02f), new Vector3(L + 0.06f, 0.07f, 0.09f), brass, true);
        for (int i = 0; i < n; i++)
        {
            float cx = -L / 2 + (i + 0.5f) * w;
            P(g, "Glass", new Vector3(cx, 1.6f, 0.02f), new Vector3(w - 0.07f, 0.9f, 0.02f), glassMat, true);
            P(g, "MoneyTray", new Vector3(cx, 1.075f, -0.12f), new Vector3(0.42f, 0.04f, 0.3f), chromeMat);
            Pc(g, "SpeakGrille", new Vector3(cx, 1.55f, 0.0f), 0.035f, 0.012f, chromeMat, false, new Vector3(90, 0, 0));
            P(g, "WindowNumber", new Vector3(cx, 2.09f, -0.03f), new Vector3(0.22f, 0.05f, 0.005f), neonCyanMesh);
        }
        // логотип на фасаде
        P(g, "LogoPlate", new Vector3(0, 0.55f, -0.325f), new Vector3(Mathf.Min(L * 0.62f, 2.8f), 0.3f, 0.01f), windowDark);
        Pt(g, "Logo", "SUNSET TRUST BANK", new Vector3(0, 0.55f, -0.335f), 2.2f, new Vector2(Mathf.Min(L * 0.6f, 2.7f), 0.26f), neonPink);
        // со стороны кассира: кассовый аппарат и монитор на столешнице
        CashRegister(g, new Vector3(-L / 4, 1.06f, 0.24f), 180f);
        Monitor(g, new Vector3(L / 4, 1.06f, 0.26f), 180f);
        return g;
    }

    /// Справочная стойка у входа (без стекла), с логотипом.
    static Transform InfoDesk(Transform parent, Vector3 pos, float yaw, float L)
    {
        var g = G(parent, "InfoDesk", pos, yaw);
        P(g, "Body", new Vector3(0, 0.55f, 0.05f), new Vector3(L, 1.1f, 0.7f), stuccoPink, true);
        P(g, "Top", new Vector3(0, 1.13f, -0.02f), new Vector3(L + 0.08f, 0.06f, 0.85f), marbleGold, true);
        foreach (float y in new[] { 0.25f, 0.9f }) P(g, "GoldStripe", new Vector3(0, y, -0.31f), new Vector3(L, 0.04f, 0.02f), brass);
        P(g, "LogoPlate", new Vector3(0, 0.58f, -0.315f), new Vector3(L * 0.75f, 0.32f, 0.01f), windowDark);
        Pt(g, "Logo", "SUNSET TRUST BANK", new Vector3(0, 0.58f, -0.325f), 2.2f, new Vector2(L * 0.72f, 0.28f), neonPink);
        Monitor(g, new Vector3(-L / 4, 1.16f, 0.2f), 180f);
        P(g, "Bell", new Vector3(L / 4, 1.19f, -0.25f), new Vector3(0.1f, 0.06f, 0.1f), brass);
        return g;
    }

    static Transform CashRegister(Transform parent, Vector3 localPos, float yaw)
    {
        var g = Group(parent, "CashRegister");
        g.localPosition = localPos; g.localRotation = Quaternion.Euler(0, yaw, 0);
        P(g, "Drawer", new Vector3(0, 0.06f, 0), new Vector3(0.42f, 0.12f, 0.36f), stuccoPink);
        P(g, "DrawerLine", new Vector3(0, 0.06f, -0.185f), new Vector3(0.38f, 0.015f, 0.01f), blackMetal);
        P(g, "Keypad", new Vector3(0, 0.15f, -0.04f), new Vector3(0.36f, 0.05f, 0.22f), beige, false, new Vector3(-15, 0, 0));
        for (int r = 0; r < 3; r++) for (int c = 0; c < 4; c++)
            P(g, "Key", new Vector3(-0.12f + c * 0.08f, 0.185f + r * 0.012f, -0.1f + r * 0.05f), new Vector3(0.05f, 0.015f, 0.035f), r == 0 && c == 3 ? stripRed : stuccoWhite, false, new Vector3(-15, 0, 0));
        P(g, "DisplayPost", new Vector3(0, 0.2f, 0.13f), new Vector3(0.05f, 0.15f, 0.05f), beige);
        P(g, "Display", new Vector3(0, 0.3f, 0.13f), new Vector3(0.22f, 0.08f, 0.05f), blackMetal);
        P(g, "DisplayDigits", new Vector3(0, 0.3f, 0.103f), new Vector3(0.18f, 0.05f, 0.005f), ledGreen);
        return g;
    }

    static Transform Monitor(Transform parent, Vector3 localPos, float yaw)
    {
        var g = Group(parent, "CRTMonitor");
        g.localPosition = localPos; g.localRotation = Quaternion.Euler(0, yaw, 0);
        P(g, "Stand", new Vector3(0, 0.02f, 0.02f), new Vector3(0.22f, 0.04f, 0.2f), beige);
        P(g, "Case", new Vector3(0, 0.22f, 0.04f), new Vector3(0.4f, 0.34f, 0.34f), beige);
        P(g, "Screen", new Vector3(0, 0.23f, -0.135f), new Vector3(0.31f, 0.24f, 0.01f), neonCyanMesh);
        P(g, "Keyboard", new Vector3(0, 0.015f, -0.26f), new Vector3(0.38f, 0.03f, 0.13f), beige);
        return g;
    }

    // ------------------------------------------------------------------ сиденья
    static Transform OfficeChair(Transform parent, Vector3 pos, float yaw)
    {
        var g = G(parent, "OfficeChair", pos, yaw);
        foreach (float a in new[] { 0f, 72f, 144f, 216f, 288f })
        {
            var d = Quaternion.Euler(0, a, 0) * Vector3.forward;
            P(g, "Leg", d * 0.16f + Vector3.up * 0.06f, new Vector3(0.05f, 0.04f, 0.32f), chromeMat, false, new Vector3(0, a, 0));
            P(g, "Wheel", d * 0.3f + Vector3.up * 0.03f, new Vector3(0.06f, 0.06f, 0.06f), blackMetal);
        }
        Pc(g, "Stem", new Vector3(0, 0.27f, 0), 0.03f, 0.4f, chromeMat);
        P(g, "Seat", new Vector3(0, 0.5f, 0), new Vector3(0.5f, 0.09f, 0.48f), stuccoTeal, true);
        P(g, "BackPost", new Vector3(0, 0.66f, 0.22f), new Vector3(0.05f, 0.3f, 0.04f), chromeMat);
        P(g, "Back", new Vector3(0, 0.88f, 0.24f), new Vector3(0.46f, 0.42f, 0.08f), stuccoTeal, true, new Vector3(-6, 0, 0));
        return g;
    }

    static Transform Sofa(Transform parent, Vector3 pos, float yaw, float L)
    {
        var g = G(parent, "Sofa", pos, yaw);
        P(g, "Base", new Vector3(0, 0.2f, 0), new Vector3(L, 0.3f, 0.85f), leather, true);
        P(g, "Back", new Vector3(0, 0.6f, 0.32f), new Vector3(L, 0.55f, 0.2f), leather, true);
        foreach (float sx in new[] { -1f, 1f }) P(g, "Arm", new Vector3(sx * (L / 2 - 0.1f), 0.45f, 0), new Vector3(0.2f, 0.5f, 0.85f), leather, true);
        int n = 3; float cw = (L - 0.4f) / n;
        for (int i = 0; i < n; i++)
        {
            float cx = -L / 2 + 0.2f + (i + 0.5f) * cw;
            P(g, "SeatCushion", new Vector3(cx, 0.41f, -0.06f), new Vector3(cw - 0.03f, 0.13f, 0.66f), leatherLight);
            P(g, "BackCushion", new Vector3(cx, 0.68f, 0.2f), new Vector3(cw - 0.03f, 0.38f, 0.12f), leatherLight, false, new Vector3(-8, 0, 0));
        }
        foreach (float sx in new[] { -1f, 1f }) foreach (float sz in new[] { -1f, 1f })
                P(g, "Foot", new Vector3(sx * (L / 2 - 0.1f), 0.025f, sz * 0.35f), new Vector3(0.06f, 0.05f, 0.06f), mahogany);
        return g;
    }

    static Transform Bench(Transform parent, Vector3 pos, float yaw, float L)
    {
        var g = G(parent, "Bench", pos, yaw);
        foreach (float sx in new[] { -1f, 1f }) P(g, "Leg", new Vector3(sx * (L / 2 - 0.15f), 0.2f, 0), new Vector3(0.08f, 0.4f, 0.5f), brass, true);
        P(g, "Frame", new Vector3(0, 0.38f, 0), new Vector3(L, 0.06f, 0.55f), mahogany, true);
        P(g, "Cushion", new Vector3(0, 0.46f, 0), new Vector3(L - 0.06f, 0.1f, 0.5f), leather, true);
        for (int i = 1; i < 4; i++) P(g, "Tuft", new Vector3(-L / 2 + i * L / 4, 0.515f, 0), new Vector3(0.04f, 0.01f, 0.4f), leatherLight);
        return g;
    }

    // ------------------------------------------------------------------ комната отдыха
    static Transform Kitchenette(Transform parent, Vector3 pos, float yaw, float L)
    {
        var g = G(parent, "Kitchenette", pos, yaw);
        P(g, "Cabinet", new Vector3(0, 0.45f, 0), new Vector3(L, 0.9f, 0.6f), stuccoMint, true);
        int doors = Mathf.RoundToInt(L / 0.55f);
        for (int i = 1; i < doors; i++) P(g, "DoorSeam", new Vector3(-L / 2 + i * L / doors, 0.45f, -0.305f), new Vector3(0.015f, 0.8f, 0.01f), blackMetal);
        for (int i = 0; i < doors; i++) P(g, "Knob", new Vector3(-L / 2 + (i + 0.5f) * L / doors + 0.18f, 0.7f, -0.32f), new Vector3(0.03f, 0.03f, 0.03f), brass);
        P(g, "Counter", new Vector3(0, 0.92f, -0.01f), new Vector3(L + 0.04f, 0.04f, 0.64f), marbleGold, true);
        // микроволновка
        P(g, "Microwave", new Vector3(-L / 4, 1.1f, 0.03f), new Vector3(0.5f, 0.3f, 0.38f), stuccoWhite, true);
        P(g, "MicrowaveWindow", new Vector3(-L / 4 - 0.05f, 1.1f, -0.165f), new Vector3(0.3f, 0.2f, 0.01f), blackMetal);
        P(g, "MicrowavePanel", new Vector3(-L / 4 + 0.18f, 1.1f, -0.165f), new Vector3(0.09f, 0.22f, 0.01f), ledGreen);
        // кофемашина и кружки
        P(g, "CoffeeBody", new Vector3(L / 4, 1.15f, 0.05f), new Vector3(0.32f, 0.42f, 0.3f), blackMetal, true);
        P(g, "CoffeeTop", new Vector3(L / 4, 1.38f, 0.05f), new Vector3(0.3f, 0.05f, 0.28f), chromeMat);
        P(g, "CoffeeSpout", new Vector3(L / 4, 1.2f, -0.12f), new Vector3(0.06f, 0.08f, 0.06f), chromeMat);
        Pc(g, "Mug", new Vector3(L / 4, 0.985f, -0.12f), 0.045f, 0.09f, stuccoPink);
        Pc(g, "Mug", new Vector3(L / 4 + 0.3f, 0.985f, -0.1f), 0.045f, 0.09f, stuccoTeal);
        // верхние шкафчики
        P(g, "UpperCabinet", new Vector3(0, 1.85f, 0.12f), new Vector3(L, 0.6f, 0.36f), stuccoMint, true);
        for (int i = 1; i < doors; i++) P(g, "UpperSeam", new Vector3(-L / 2 + i * L / doors, 1.85f, -0.065f), new Vector3(0.015f, 0.55f, 0.01f), blackMetal);
        return g;
    }

    static Transform Lockers(Transform parent, Vector3 pos, float yaw, int count)
    {
        float w = 0.5f, L = w * count;
        var g = G(parent, "Lockers", pos, yaw);
        P(g, "Body", new Vector3(0, 0.95f, 0), new Vector3(L, 1.9f, 0.5f), stuccoTeal, true);
        P(g, "TopTrim", new Vector3(0, 1.91f, -0.02f), new Vector3(L + 0.02f, 0.04f, 0.5f), blackMetal);
        for (int i = 0; i < count; i++)
        {
            float cx = -L / 2 + (i + 0.5f) * w;
            P(g, "Seam", new Vector3(cx - w / 2, 0.95f, -0.255f), new Vector3(0.015f, 1.85f, 0.01f), blackMetal);
            for (int v = 0; v < 3; v++) P(g, "Vent", new Vector3(cx, 1.6f + v * 0.06f, -0.255f), new Vector3(0.3f, 0.02f, 0.01f), blackMetal);
            P(g, "Handle", new Vector3(cx + 0.16f, 1.0f, -0.27f), new Vector3(0.03f, 0.14f, 0.03f), chromeMat);
            P(g, "Padlock", new Vector3(cx + 0.16f, 0.88f, -0.275f), new Vector3(0.05f, 0.06f, 0.03f), brass);
            P(g, "NameTag", new Vector3(cx, 1.4f, -0.256f), new Vector3(0.14f, 0.05f, 0.005f), paper);
        }
        return g;
    }

    static Transform WaterCooler(Transform parent, Vector3 pos, float yaw)
    {
        var g = G(parent, "WaterCooler", pos, yaw);
        P(g, "Cabinet", new Vector3(0, 0.5f, 0), new Vector3(0.34f, 1.0f, 0.34f), stuccoWhite, true);
        P(g, "TrayRecess", new Vector3(0, 0.75f, -0.171f), new Vector3(0.24f, 0.18f, 0.01f), blackMetal);
        P(g, "TapCold", new Vector3(-0.06f, 0.82f, -0.18f), new Vector3(0.04f, 0.04f, 0.04f), waterBlue);
        P(g, "TapHot", new Vector3(0.06f, 0.82f, -0.18f), new Vector3(0.04f, 0.04f, 0.04f), stripRed);
        Pc(g, "Bottle", new Vector3(0, 1.24f, 0), 0.15f, 0.45f, waterBlue);
        Pc(g, "BottleNeck", new Vector3(0, 1.0f, 0), 0.05f, 0.06f, waterBlue);
        return g;
    }

    static Transform RoundTable(Transform parent, Vector3 pos)
    {
        var g = G(parent, "RoundTable", pos, 0);
        Pc(g, "Foot", new Vector3(0, 0.02f, 0), 0.3f, 0.04f, chromeMat, true);
        Pc(g, "Leg", new Vector3(0, 0.39f, 0), 0.05f, 0.72f, chromeMat, true);
        Pc(g, "Top", new Vector3(0, 0.76f, 0), 0.6f, 0.04f, marbleGold, true);
        return g;
    }

    // ------------------------------------------------------------------ кабинеты
    static Transform DirectorDesk(Transform parent, Vector3 pos, float yaw)
    {
        var g = G(parent, "DirectorDesk", pos, yaw);
        P(g, "Top", new Vector3(0, 0.76f, 0), new Vector3(2.2f, 0.06f, 1.0f), mahogany, true);
        P(g, "LeatherInlay", new Vector3(0, 0.792f, 0.05f), new Vector3(1.5f, 0.005f, 0.6f), Emissive("LeatherGreen", new Color(0.12f, 0.35f, 0.22f), 0f));
        foreach (float sx in new[] { -1f, 1f })
        {
            P(g, "Pedestal", new Vector3(sx * 0.78f, 0.365f, 0.02f), new Vector3(0.6f, 0.73f, 0.92f), mahogany, true);
            for (int d = 0; d < 3; d++) P(g, "DrawerHandle", new Vector3(sx * 0.78f, 0.15f + d * 0.22f, 0.49f), new Vector3(0.2f, 0.025f, 0.02f), brass);
        }
        P(g, "ModestyPanel", new Vector3(0, 0.45f, -0.44f), new Vector3(0.96f, 0.55f, 0.04f), mahogany, true);
        P(g, "GoldTrim", new Vector3(0, 0.72f, -0.505f), new Vector3(2.2f, 0.03f, 0.01f), brass);
        // лампа, телефон, бумаги
        Pc(g, "LampBase", new Vector3(-0.8f, 0.81f, 0.25f), 0.08f, 0.03f, brass);
        Pc(g, "LampStem", new Vector3(-0.8f, 0.98f, 0.25f), 0.012f, 0.34f, brass);
        P(g, "LampShade", new Vector3(-0.8f, 1.16f, 0.2f), new Vector3(0.32f, 0.08f, 0.14f), Emissive("LampGreen", new Color(0.2f, 0.6f, 0.35f), 1.2f));
        P(g, "Phone", new Vector3(0.75f, 0.84f, 0.2f), new Vector3(0.22f, 0.08f, 0.2f), blackMetal);
        P(g, "Handset", new Vector3(0.75f, 0.9f, 0.2f), new Vector3(0.24f, 0.04f, 0.06f), blackMetal);
        for (int i = 0; i < 3; i++) P(g, "Paper", new Vector3(0.35f, 0.8f + i * 0.012f, 0.1f), new Vector3(0.3f, 0.01f, 0.22f), paper, false, new Vector3(0, i * 9, 0));
        return g;
    }

    static Transform OfficeDesk(Transform parent, Vector3 pos, float yaw)
    {
        var g = G(parent, "OfficeDesk", pos, yaw);
        P(g, "Top", new Vector3(0, 0.75f, 0), new Vector3(1.6f, 0.05f, 0.8f), mahogany, true);
        foreach (float sx in new[] { -1f, 1f }) P(g, "Side", new Vector3(sx * 0.77f, 0.37f, 0), new Vector3(0.05f, 0.73f, 0.76f), mahogany, true);
        P(g, "Back", new Vector3(0, 0.45f, 0.36f), new Vector3(1.5f, 0.5f, 0.03f), mahogany);
        Monitor(g, new Vector3(-0.35f, 0.775f, 0.12f), 0f);
        P(g, "Folder", new Vector3(0.45f, 0.79f, 0.05f), new Vector3(0.3f, 0.03f, 0.24f), stuccoPink);
        return g;
    }

    /// Пульт охраны: консоль и 2 ряда по 3 ЭЛТ-монитора с картинкой камер.
    static Transform SecurityConsole(Transform parent, Vector3 pos, float yaw)
    {
        var g = G(parent, "SecurityConsole", pos, yaw);
        P(g, "Console", new Vector3(0, 0.4f, 0), new Vector3(2.0f, 0.8f, 0.7f), blackMetal, true);
        P(g, "ConsoleTop", new Vector3(0, 0.82f, -0.05f), new Vector3(2.02f, 0.04f, 0.72f), chromeMat, true);
        P(g, "Keyboard", new Vector3(-0.3f, 0.86f, -0.2f), new Vector3(0.6f, 0.03f, 0.2f), beige);
        Pc(g, "Joystick", new Vector3(0.5f, 0.92f, -0.2f), 0.025f, 0.14f, blackMetal);
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 3; c++)
            {
                var m = new Vector3(-0.62f + c * 0.62f, 1.07f + r * 0.48f, 0.15f);
                P(g, "MonitorCase", m, new Vector3(0.58f, 0.44f, 0.38f), beige, r == 0);
                P(g, "MonitorScreen", m + new Vector3(0, 0, -0.195f), new Vector3(0.46f, 0.33f, 0.01f), (r + c) % 2 == 0 ? neonCyanMesh : ledGreen);
            }
        return g;
    }

    static Transform ServerRack(Transform parent, Vector3 pos, float yaw)
    {
        var g = G(parent, "ServerRack", pos, yaw);
        P(g, "Cabinet", new Vector3(0, 1.0f, 0), new Vector3(0.6f, 2.0f, 0.8f), blackMetal, true);
        P(g, "Door", new Vector3(0, 1.0f, -0.405f), new Vector3(0.54f, 1.9f, 0.01f), Emissive("RackGlass", new Color(0.05f, 0.08f, 0.1f), 0f));
        for (int i = 0; i < 9; i++)
        {
            float y = 0.25f + i * 0.19f;
            P(g, "Unit", new Vector3(0, y, -0.39f), new Vector3(0.48f, 0.15f, 0.01f), Emissive("RackUnit", new Color(0.16f, 0.17f, 0.19f), 0f));
            P(g, "Led", new Vector3(-0.18f + (i % 3) * 0.06f, y + 0.03f, -0.412f), new Vector3(0.025f, 0.025f, 0.005f), i % 2 == 0 ? ledGreen : neonCyanMesh);
        }
        return g;
    }

    static Transform Atm(Transform parent, Vector3 pos, float yaw)
    {
        var g = G(parent, "ATM", pos, yaw);
        P(g, "Body", new Vector3(0, 0.85f, 0), new Vector3(0.8f, 1.7f, 0.6f), chromeMat, true);
        P(g, "Front", new Vector3(0, 0.85f, -0.302f), new Vector3(0.72f, 1.6f, 0.01f), stuccoTeal);
        P(g, "Topper", new Vector3(0, 1.85f, -0.05f), new Vector3(0.8f, 0.3f, 0.5f), stuccoPink, true);
        Pc(g, "TopperSun", new Vector3(0, 1.85f, -0.31f), 0.11f, 0.01f, neonSun, false, new Vector3(90, 0, 0));
        P(g, "Hood", new Vector3(0, 1.5f, -0.36f), new Vector3(0.66f, 0.04f, 0.14f), chromeMat);
        P(g, "Screen", new Vector3(0, 1.3f, -0.31f), new Vector3(0.46f, 0.32f, 0.01f), neonCyanMesh, false, new Vector3(-8, 0, 0));
        P(g, "Shelf", new Vector3(0, 0.98f, -0.4f), new Vector3(0.66f, 0.05f, 0.22f), chromeMat, true);
        for (int r = 0; r < 4; r++) for (int c = 0; c < 3; c++)
            P(g, "Key", new Vector3(-0.08f + c * 0.08f, 1.012f, -0.46f + r * 0.045f), new Vector3(0.06f, 0.015f, 0.035f), beige);
        P(g, "CardSlot", new Vector3(0.24f, 1.1f, -0.31f), new Vector3(0.12f, 0.02f, 0.02f), blackMetal);
        P(g, "CashSlot", new Vector3(0, 0.78f, -0.31f), new Vector3(0.4f, 0.04f, 0.02f), blackMetal);
        return g;
    }

    /// Кованая решётка-перегородка вдоль локальной X: прутья с пиками и две поперечины.
    static Transform IronBarsPanel(Transform parent, Vector3 pos, float yaw, float L, float H)
    {
        var g = G(parent, "IronBars", pos, yaw);
        var iron = blackMetal;
        P(g, "Rail_B", new Vector3(0, 0.15f, 0), new Vector3(L, 0.06f, 0.06f), iron, true);
        P(g, "Rail_T", new Vector3(0, H - 0.25f, 0), new Vector3(L, 0.06f, 0.06f), iron, true);
        int n = Mathf.RoundToInt(L / 0.16f);
        for (int i = 0; i <= n; i++)
        {
            float x = -L / 2 + i * L / n;
            P(g, "Bar", new Vector3(x, H / 2 - 0.06f, 0), new Vector3(0.03f, H - 0.12f, 0.03f), iron, i % 4 == 0);
            P(g, "Spike", new Vector3(x, H - 0.06f, 0), new Vector3(0.05f, 0.1f, 0.05f), brass, false, new Vector3(0, 45, 45));
        }
        foreach (float sx in new[] { -1f, 1f }) P(g, "Post", new Vector3(sx * L / 2, H / 2, 0), new Vector3(0.08f, H, 0.08f), iron, true);
        return g;
    }
}
