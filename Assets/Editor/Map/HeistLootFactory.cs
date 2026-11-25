using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static MapKit;

/// <summary>6 видов лута из составных деталей. Префабы — в Assets/Prefabs/Loot/. Точка опоры — по центру низа (у картины — центр).</summary>
public static class HeistLootFactory
{
    public const string Dir = "Assets/Prefabs/Loot";
    const string ImpactDir = "Assets/Audio/Kenney_ImpactSounds";

    public static Dictionary<string, GameObject> BuildAll()
    {
        EnsureFolder(Dir);
        var d = new Dictionary<string, GameObject>
        {
            ["GoldBar"] = GoldBar(),
            ["Crown"] = Crown(),
            ["Painting"] = Painting(),
            ["Vase"] = Vase(),
            ["Diamond"] = Diamond(),
            ["Safe"] = Safe(),
        };
        // старые/лишние префабы лута из прошлых версий не трогаем — новые перезаписывают одноимённые
        return d;
    }

    static GameObject Root(string name, string id, string itemName, int value, float mass, Vector3 colCenter, Vector3 colSize, bool small)
    {
        var root = new GameObject(name) { tag = "Loot" };
        var col = root.AddComponent<BoxCollider>();
        col.center = colCenter; col.size = colSize;
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = small ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.ContinuousSpeculative;
        var li = root.AddComponent<LootItem>();
        li.lootId = id; li.itemName = itemName; li.value = value;
        return root;
    }

    static GameObject Save(GameObject root)
    {
        var p = PrefabUtility.SaveAsPrefabAsset(root, $"{Dir}/{root.name}.prefab");
        Object.DestroyImmediate(root);
        return p;
    }

    // ---------------------------------------------------------------- $150
    static GameObject GoldBar()
    {
        var r = Root("Loot_GoldBar", "GoldBar", "Золотой слиток", 150, 1f, new Vector3(0, 0.037f, 0), new Vector3(0.24f, 0.075f, 0.11f), true);
        Box(r.transform, "Base", new Vector3(0, 0.025f, 0), new Vector3(0.24f, 0.05f, 0.11f), "GoldGlow", false);
        Box(r.transform, "Top", new Vector3(0, 0.0625f, 0), new Vector3(0.2f, 0.025f, 0.08f), "GoldGlow", false);
        Box(r.transform, "Stamp", new Vector3(0, 0.076f, 0), new Vector3(0.07f, 0.004f, 0.04f), "Brass", false);
        var li = r.GetComponent<LootItem>(); li.spinUntilGrabbed = true; li.glowColor = new Color(1f, 0.7f, 0.2f);
        return Save(r);
    }

    // ---------------------------------------------------------------- $400
    static GameObject Crown()
    {
        var r = Root("Loot_Crown", "Crown", "Корона", 400, 2f, new Vector3(0, 0.1f, 0), new Vector3(0.27f, 0.2f, 0.27f), true);
        var t = r.transform;
        Sph(t, "Cap", new Vector3(0, 0.07f, 0), new Vector3(0.2f, 0.13f, 0.2f), "Velvet");
        const int n = 12; const float rad = 0.115f;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2 / n;
            var p = new Vector3(Mathf.Cos(a) * rad, 0.045f, Mathf.Sin(a) * rad);
            Box(t, "Band_" + i, p, new Vector3(0.064f, 0.09f, 0.018f), "GoldGlow", false, Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90, 0));
        }
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI * 2 / 6;
            var p = new Vector3(Mathf.Cos(a) * rad, 0.12f, Mathf.Sin(a) * rad);
            var rot = Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90, 0) * Quaternion.Euler(0, 0, 45);
            Box(t, "Spike_" + i, p, new Vector3(0.05f, 0.05f, 0.016f), "GoldGlow", false, rot);
            Sph(t, "Gem_" + i, p + Vector3.up * 0.042f, Vector3.one * 0.026f, i % 2 == 0 ? "GemRed" : "GemBlue");
        }
        Sph(t, "FrontGem", new Vector3(0, 0.045f, -rad - 0.012f), new Vector3(0.04f, 0.04f, 0.02f), "GemRed");
        var li = r.GetComponent<LootItem>(); li.spinUntilGrabbed = true; li.glowColor = new Color(1f, 0.6f, 0.3f);
        return Save(r);
    }

    // ---------------------------------------------------------------- $600
    static GameObject Painting()
    {
        var r = Root("Loot_Painting", "Painting", "Картина", 600, 5f, Vector3.zero, new Vector3(0.9f, 0.7f, 0.06f), false);
        var t = r.transform;
        const float w = 0.9f, h = 0.7f, f = 0.07f;
        Box(t, "Canvas", new Vector3(0, 0, 0.005f), new Vector3(w - 2 * f, h - 2 * f, 0.02f), "Art1", false);
        Box(t, "Back", new Vector3(0, 0, 0.02f), new Vector3(w - 0.02f, h - 0.02f, 0.01f), "WoodDark", false);
        Box(t, "Frame_T", new Vector3(0, (h - f) / 2, 0), new Vector3(w, f, 0.06f), "GoldGlow", false);
        Box(t, "Frame_B", new Vector3(0, -(h - f) / 2, 0), new Vector3(w, f, 0.06f), "GoldGlow", false);
        Box(t, "Frame_L", new Vector3(-(w - f) / 2, 0, 0), new Vector3(f, h - 2 * f, 0.06f), "GoldGlow", false);
        Box(t, "Frame_R", new Vector3((w - f) / 2, 0, 0), new Vector3(f, h - 2 * f, 0.06f), "GoldGlow", false);
        foreach (var (x, y) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
            Sph(t, "Ornament", new Vector3(x * (w - f) / 2, y * (h - f) / 2, -0.03f), Vector3.one * 0.06f, "GoldGlow");
        var li = r.GetComponent<LootItem>(); li.spinUntilGrabbed = false; li.glowColor = new Color(1f, 0.85f, 0.5f);
        return Save(r);
    }

    // ---------------------------------------------------------------- $300 (хрупкая)
    static GameObject Vase()
    {
        var r = Root("Loot_Vase", "Vase", "Ваза", 300, 3f, new Vector3(0, 0.235f, 0), new Vector3(0.27f, 0.47f, 0.27f), false);
        var t = r.transform;
        Cyl(t, "Foot", new Vector3(0, 0.015f, 0), 0.075f, 0.03f, "Porcelain", false);
        Sph(t, "Body", new Vector3(0, 0.18f, 0), new Vector3(0.26f, 0.3f, 0.26f), "Porcelain");
        Cyl(t, "BandMid", new Vector3(0, 0.18f, 0), 0.132f, 0.035f, "PorcelainBlue", false);
        Sph(t, "Shoulder", new Vector3(0, 0.3f, 0), new Vector3(0.2f, 0.12f, 0.2f), "Porcelain");
        Cyl(t, "Neck", new Vector3(0, 0.38f, 0), 0.05f, 0.13f, "Porcelain", false);
        Cyl(t, "BandNeck", new Vector3(0, 0.37f, 0), 0.053f, 0.025f, "PorcelainBlue", false);
        Cyl(t, "Lip", new Vector3(0, 0.455f, 0), 0.075f, 0.025f, "Porcelain", false);
        var li = r.GetComponent<LootItem>();
        li.spinUntilGrabbed = false; li.isFragile = true; li.breakImpactSpeed = 5.5f;
        li.breakClips = Clips(ImpactDir, "impact_glass_heavy", "impact_plate_heavy");
        li.shardMaterial = M("Porcelain");
        li.glowColor = new Color(0.6f, 0.75f, 1f);
        return Save(r);
    }

    // ---------------------------------------------------------------- $1000
    static GameObject Diamond()
    {
        var r = Root("Loot_Diamond", "Diamond", "Бриллиант", 1000, 0.5f, new Vector3(0, 0.11f, 0), new Vector3(0.24f, 0.22f, 0.24f), true);
        MeshObj(r.transform, "Gem", GemMesh, new Vector3(0, 0.12f, 0), Vector3.one * 0.12f, "GemDiamond");
        var li = r.GetComponent<LootItem>();
        li.spinUntilGrabbed = true; li.spinSpeed = 60f; li.glowLight = true; li.glowRange = 1.6f; li.glowColor = new Color(0.6f, 0.9f, 1f);
        return Save(r);
    }

    // ---------------------------------------------------------------- $2500 (тяжёлый)
    static GameObject Safe()
    {
        var r = Root("Loot_Safe", "Safe", "Сейф", 2500, 20f, new Vector3(0, 0.36f, 0), new Vector3(0.62f, 0.72f, 0.62f), false);
        var t = r.transform;
        Box(t, "Body", new Vector3(0, 0.38f, 0), new Vector3(0.6f, 0.68f, 0.6f), "SafeSteel", false);
        Box(t, "Door", new Vector3(0, 0.38f, -0.305f), new Vector3(0.5f, 0.58f, 0.02f), "SafeSteel", false);
        Cyl(t, "DialRing", new Vector3(0.08f, 0.46f, -0.32f), 0.07f, 0.015f, "Chrome", false, Quaternion.Euler(90, 0, 0));
        Cyl(t, "Dial", new Vector3(0.08f, 0.46f, -0.33f), 0.045f, 0.025f, "GoldGlow", false, Quaternion.Euler(90, 0, 0));
        Cyl(t, "HandleHub", new Vector3(0.08f, 0.28f, -0.325f), 0.025f, 0.03f, "Chrome", false, Quaternion.Euler(90, 0, 0));
        for (int i = 0; i < 3; i++)
            Box(t, "Spoke_" + i, new Vector3(0.08f, 0.28f, -0.34f), new Vector3(0.16f, 0.018f, 0.018f), "Chrome", false, Quaternion.Euler(0, 0, i * 60));
        foreach (float y in new[] { 0.2f, 0.56f })
            Cyl(t, "Hinge", new Vector3(-0.255f, y, -0.315f), 0.018f, 0.1f, "Chrome", false);
        foreach (var (x, z) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
            Box(t, "Foot", new Vector3(x * 0.25f, 0.02f, z * 0.25f), new Vector3(0.07f, 0.04f, 0.07f), "DarkMetal", false);
        Box(t, "Plate", new Vector3(-0.08f, 0.6f, -0.317f), new Vector3(0.16f, 0.05f, 0.005f), "GoldGlow", false);
        var li = r.GetComponent<LootItem>();
        li.isHeavy = true; li.spinUntilGrabbed = false; li.glowLight = true; li.glowRange = 2.2f; li.glowColor = new Color(1f, 0.55f, 0.15f);
        return Save(r);
    }
}
