using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Фургон грабителей у банка — модель GRABZ_BlackBus.glb (чёрный автобус с синим неоном у задней двери, LED-фарами
/// и рольставней). У модели только внешняя оболочка, поэтому скрипт:
///  • вырезает в копии меша кузова задний дверной проём (GRABZ_BlackBus_BodyOpen.asset, исходный .glb не меняется);
///  • строит внутри салон (пол, стены, потолок, перегородку к кабине, скамьи, плафон, табличку «ВЫХОДИ»);
///  • делает простые коллайдеры кузова с проходом в проёме;
///  • рольставня в начале свёрнута, при отъезде опускается у всех (NetworkHeistStartTrigger.shutter).
/// Логика сетевого старта и отъезда — та же (NetworkHeistStartTrigger, Master Client Object).
/// Модель: нос (фары) — +Z, задний проём — −Z, пол на y = 0.
/// </summary>
public static partial class BankVisualOverhaul
{
    const string BusPath = Gen + "/GRABZ_BlackBus.glb";
    const string BusOpenMeshPath = Gen + "/GRABZ_BlackBus_BodyOpen.asset";

    static (Transform spawn, Transform exit) BuildArrivalBus(Transform root)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(BusPath);
        if (!model) { log.AppendLine("ERROR: GRABZ_BlackBus.glb not imported"); return (null, null); }

        var parked = new Vector3(0f, RoadY, -13.4f);
        var vr = new GameObject("GetawayVan_Arrival").transform;
        vr.SetParent(root, false);
        vr.SetPositionAndRotation(parked, Quaternion.Euler(0, 180, 0));          // нос — от банка, задний проём — к ступенькам
        var van = Group(vr, "Van");                                               // движется и прячется (корень NetworkObject остаётся)
        Vector3 W(float x, float y, float z) => van.TransformPoint(new Vector3(x, y, z));
        Quaternion R(float x = 0, float y = 0, float z = 0) => van.rotation * Quaternion.Euler(x, y, z);

        // модель: нос на +Z (проверяем по фаре — если импорт развернул, доворачиваем)
        var bus = (GameObject)PrefabUtility.InstantiatePrefab(model, van);
        bus.name = "GRABZ_BlackBus";
        bus.transform.localPosition = Vector3.zero; bus.transform.localRotation = Quaternion.identity; bus.transform.localScale = Vector3.one;
        var head = bus.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("HeadlightModule"));
        if (head && van.InverseTransformPoint(head.position).z < 0) bus.transform.localRotation = Quaternion.Euler(0, 180, 0);
        foreach (var c in bus.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (var r in bus.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // настоящий проём в задней стенке кузова
        var body = bus.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m => m.name == "Body");
        if (body) body.sharedMesh = DoorwayCutMesh(body);
        // рольставня свёрнута вверх (опускается при отъезде)
        var shutter = bus.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "RearShutter");
        if (shutter) shutter.localScale = new Vector3(1f, 0.04f, 1f);

        // салон (локально: проём на −Z, пол 0.35, потолок 2.3, перегородка к кабине на z = 1.3)
        const float fl = 0.35f, ceil = 2.3f, hw = 1.02f, zr = -2.74f, zf = 1.3f, mid = (zr + zf) / 2, len = zf - zr;
        var cabin = Group(van, "Cabin");
        Box(cabin, "Floor", W(0, fl - 0.05f, mid), new Vector3(2 * hw, 0.1f, len), tread, false, R());
        Box(cabin, "Wall_L", W(-hw - 0.02f, (fl + ceil) / 2, mid), new Vector3(0.04f, ceil - fl, len), vanPaint, false, R());
        Box(cabin, "Wall_R", W(hw + 0.02f, (fl + ceil) / 2, mid), new Vector3(0.04f, ceil - fl, len), vanPaint, false, R());
        Box(cabin, "Ceiling", W(0, ceil + 0.02f, mid), new Vector3(2 * hw + 0.08f, 0.04f, len), vanPaint, false, R());
        Box(cabin, "Bulkhead", W(0, (fl + ceil) / 2, zf), new Vector3(2 * hw + 0.08f, ceil - fl, 0.04f), vanPaint, false, R());
        // задняя стенка вокруг проёма (изнутри и снаружи — чёрная, как кузов)
        Box(cabin, "RearWall_L", W(-0.88f, (fl + ceil) / 2, zr - 0.03f), new Vector3(0.44f, ceil - fl, 0.04f), blackMetal, false, R());
        Box(cabin, "RearWall_R", W(0.88f, (fl + ceil) / 2, zr - 0.03f), new Vector3(0.44f, ceil - fl, 0.04f), blackMetal, false, R());
        foreach (var sx in new[] { -0.78f, 0.78f })
        {
            Box(cabin, "Bench", W(sx, 0.75f, mid), new Vector3(0.44f, 0.08f, len - 0.6f), woodPanel, false, R());
            Box(cabin, "BenchSupport", W(sx, 0.55f, mid), new Vector3(0.38f, 0.4f, 0.06f), blackMetal, false, R());
        }
        Box(cabin, "NeonStrip_L", W(-hw + 0.03f, ceil - 0.08f, mid), new Vector3(0.03f, 0.03f, len - 0.3f), neonCyanMesh, false, R());
        Box(cabin, "NeonStrip_R", W(hw - 0.03f, ceil - 0.08f, mid), new Vector3(0.03f, 0.03f, len - 0.3f), neonCyanMesh, false, R());
        Box(cabin, "Plafond", W(0, ceil - 0.01f, mid), new Vector3(0.9f, 0.03f, 0.35f), lampBulb, false, R());
        var cargoLight = new GameObject("CargoLight").AddComponent<Light>();
        cargoLight.transform.SetParent(van, false);
        cargoLight.transform.localPosition = new Vector3(0, ceil - 0.25f, mid);
        cargoLight.type = LightType.Point; cargoLight.color = new Color(0.75f, 0.85f, 1f); cargoLight.intensity = 1.6f; cargoLight.range = 4.5f;
        cargoLight.shadows = LightShadows.None;
        var status = Label(cabin, "StatusText", "ВЫХОДИ ИЗ ФУРГОНА — В БАНК!", 1.6f, Color.white, W(0, 1.75f, zf - 0.03f), van.TransformDirection(Vector3.back), new Vector2(1.9f, 0.35f));
        status.fontSharedMaterial = neonCyan;

        // коллайдеры кузова (без рендеров): пол, борта, крыша, нос, задняя стенка с проходом, ступенька
        void Col(string n, Vector3 c, Vector3 s)
        {
            var go = new GameObject(n);
            go.transform.SetParent(van, false);
            go.transform.localPosition = c;
            go.AddComponent<BoxCollider>().size = s;
        }
        Col("Col_Floor", new Vector3(0, fl - 0.1f, 0), new Vector3(2.3f, 0.2f, 5.6f));
        Col("Col_Side_L", new Vector3(-1.19f, 1.4f, 0), new Vector3(0.12f, 2.4f, 5.6f));
        Col("Col_Side_R", new Vector3(1.19f, 1.4f, 0), new Vector3(0.12f, 2.4f, 5.6f));
        Col("Col_Roof", new Vector3(0, 2.6f, 0), new Vector3(2.5f, 0.2f, 5.6f));
        Col("Col_Cab", new Vector3(0, 1.4f, 2.05f), new Vector3(2.3f, 2.2f, 1.5f));
        Col("Col_Rear_L", new Vector3(-0.92f, 1.4f, -2.77f), new Vector3(0.52f, 2.4f, 0.08f));
        Col("Col_Rear_R", new Vector3(0.92f, 1.4f, -2.77f), new Vector3(0.52f, 2.4f, 0.08f));
        Col("Col_Rear_Top", new Vector3(0, 2.47f, -2.77f), new Vector3(1.4f, 0.26f, 0.08f));
        Col("Col_Step", new Vector3(0, 0.1f, -2.93f), new Vector3(1.5f, 0.2f, 0.3f));

        // фары (LED-модули модели светятся сами; плюс два прожектора на дорогу)
        var heads = new List<Light> { cargoLight };
        foreach (var sx in new[] { -0.81f, 0.81f })
        {
            var lg = new GameObject("HeadlightBeam");
            lg.transform.SetParent(van, false);
            lg.transform.SetPositionAndRotation(W(sx, 0.92f, 2.62f), R(8, 0, 0));
            var l = lg.AddComponent<Light>();
            l.type = LightType.Spot; l.color = new Color(0.92f, 0.95f, 1f); l.intensity = 4f; l.range = 16f;
            l.spotAngle = 55f; l.innerSpotAngle = 30f; l.shadows = LightShadows.None;
            heads.Add(l);
        }

        // звук
        var engine = van.gameObject.AddComponent<AudioSource>();
        engine.playOnAwake = false; engine.loop = true; engine.spatialBlend = 1f; engine.volume = 0.55f; engine.minDistance = 2f; engine.maxDistance = 35f;
        var doorSrcGo = new GameObject("DoorAudio"); doorSrcGo.transform.SetParent(van, false); doorSrcGo.transform.localPosition = new Vector3(0, 1.4f, -2.8f);
        var doorSrc = doorSrcGo.AddComponent<AudioSource>();
        doorSrc.playOnAwake = false; doorSrc.spatialBlend = 1f; doorSrc.minDistance = 2f; doorSrc.maxDistance = 25f;

        // точки
        var spawn = new GameObject("PlayerSpawn_Van").transform;
        spawn.SetParent(root, false);
        spawn.SetPositionAndRotation(W(0, fl + 0.01f, -0.6f), Quaternion.identity);   // лицом к проёму и банку (+Z мира)
        var exit = new GameObject("TeleportPoint_Street").transform;
        exit.SetParent(root, false);
        exit.SetPositionAndRotation(new Vector3(1.2f, SidewalkY, -8.3f), Quaternion.identity);

        // сеть
        SetMasterFlags(vr.gameObject.AddComponent<NetworkObject>());
        var trig = vr.gameObject.AddComponent<NetworkHeistStartTrigger>();
        trig.van = van;
        trig.doorHinges = new Transform[0];
        trig.closedEuler = new Vector3[0];
        trig.shutter = shutter;
        trig.shutterOpenScaleY = 0.04f;
        trig.cargoCenter = new Vector3(0, (fl + ceil) / 2, mid);
        trig.cargoSize = new Vector3(2 * hw, ceil - fl + 0.4f, len);
        trig.exitPoint = exit;
        float y = RoadY;
        trig.path = new[]
        {
            parked, new Vector3(0, y, -15.4f), new Vector3(0.7f, y, -16.8f), new Vector3(2.2f, y, -17.6f), new Vector3(5f, y, -17.9f),
            new Vector3(38f, y, -17.9f), new Vector3(40.4f, y, -18.6f), new Vector3(41.6f, y, -20.2f), new Vector3(41.8f, y, -23f), new Vector3(41.8f, y, -40f),
        };
        trig.driveDuration = 7f;
        trig.teamWait = 10f;
        trig.engine = engine;
        trig.doorSource = doorSrc;
        trig.doorSlamClips = MapKit.Clips(ImpactDir, "impact_metal_heavy");
        trig.headlights = heads.ToArray();
        trig.statusText = status;
        foreach (var t in vr.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        log.AppendLine("  arrival bus: GRABZ_BlackBus with cut doorway, cabin and shutter (NetworkHeistStartTrigger)");
        return (spawn, exit);
    }

    /// Копия меша кузова без треугольников в заднем дверном проёме (между неоновыми рамками).
    static Mesh DoorwayCutMesh(MeshFilter body)
    {
        var src = AssetDatabase.LoadAssetAtPath<Mesh>(BusOpenMeshPath) is Mesh existing && body.sharedMesh == existing ? null : body.sharedMesh;
        if (src == null) return body.sharedMesh;                                  // уже вырезан при прошлом запуске
        var m = Object.Instantiate(src);
        m.name = "GRABZ_BlackBus_BodyOpen";
        var v = m.vertices;
        // задний торец — с противоположной фарам стороны: проверяем по тому, где у кузова «нос» (ниже по высоте спереди не важен — берём знак из позиции родителя)
        float minZ = v.Min(p => p.z), maxZ = v.Max(p => p.z);
        bool rearIsMin = body.transform.parent && body.transform.root.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith("HeadlightModule")).All(t => body.transform.InverseTransformPoint(t.position).z > 0);
        int removed = 0;
        for (int s = 0; s < m.subMeshCount; s++)
        {
            var tri = m.GetTriangles(s);
            var keep = new List<int>(tri.Length);
            for (int i = 0; i < tri.Length; i += 3)
            {
                var c = (v[tri[i]] + v[tri[i + 1]] + v[tri[i + 2]]) / 3f;
                bool atRear = rearIsMin ? c.z < minZ + 0.1f : c.z > maxZ - 0.1f;
                if (atRear && Mathf.Abs(c.x) < 0.65f && c.y > 0.3f && c.y < 2.33f) { removed++; continue; }
                keep.Add(tri[i]); keep.Add(tri[i + 1]); keep.Add(tri[i + 2]);
            }
            m.SetTriangles(keep, s);
        }
        m.RecalculateBounds();
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(BusOpenMeshPath);
        if (old) { EditorUtility.CopySerialized(m, old); Object.DestroyImmediate(m); m = old; EditorUtility.SetDirty(old); }
        else AssetDatabase.CreateAsset(m, BusOpenMeshPath);
        log.AppendLine($"  bus doorway: removed {removed} triangles from the rear wall");
        return m;
    }
}
