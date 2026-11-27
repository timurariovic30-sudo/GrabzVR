using System.Collections.Generic;
using System.Linq;
using Fusion;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;

/// <summary>
/// Интерьер настоящего Банка (часть BankVisualOverhaul) — Tripo-модели вместо примитивов:
///  • камеры видеонаблюдения: корпус Bank_SecurityCamera внутри поворотной части (SecurityCamera и сетевая тревога
///    AlarmSystem остаются как были — меняется только вид);
///  • операционный зал: ряд касс Bank_TellerCounter, кованые решётки Bank_IronBars, банкоматы Bank_ATM,
///    кожаные скамьи Bank_LeatherBench, растения Bank_PottedPlant;
///  • офисы: столы менеджеров Bank_OfficeDesk; хранилище: решётка перед ячейками;
///  • круглая бронедверь хранилища Bank_VaultDoor во входе — сетевая NetworkVaultDoor (взлом снаружи, кнопка изнутри),
///    старая решётка VaultGate (SlidingDoor, без сети) спрятана, её кнопки переподключены к двери.
/// Коллайдеры моделей — MeshCollider на слое Default (руки GorillaLocomotion цепляются), у камер коллайдеров нет.
/// </summary>
public static partial class BankVisualOverhaul
{
    static void BuildInteriorProps(Transform root, Transform st, Transform map, UnityEngine.SceneManagement.Scene scene)
    {
        var g = Group(st, "InteriorProps");
        ReplaceCameras(map);
        ReplaceAtms(g, map);
        ReplaceSofasWithBenches(g, map);
        ReplacePlants(g, map);
        ReplaceOfficeDesks(g, map);
        // ряд касс, решётки и дверь хранилища теперь строит BankServiceWing (кассы Майами + подземное хранилище)
    }

    static IEnumerable<Transform> Named(Transform map, string zone, string name)
    {
        var z = map.Find(zone);
        return z ? z.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToArray() : new Transform[0];
    }

    static void HideQuiet(Transform t)
    {
        if (t && t.gameObject.activeSelf) { t.gameObject.SetActive(false); EditorUtility.SetDirty(t.gameObject); }
    }

    // =========================================================================== камеры
    static void ReplaceCameras(Transform map)
    {
        var prefab = Prefab("Bank_SecurityCamera");
        if (!prefab) { log.AppendLine("  skip cameras: no Bank_SecurityCamera"); return; }
        int n = 0;
        foreach (var sc in map.GetComponentsInChildren<SecurityCamera>(true))
        {
            var pivot = sc.pivot ? sc.pivot : sc.transform;
            foreach (var r in sc.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name is "Bracket" or "Body" or "Hood" or "Lens" or "Led") { r.enabled = false; EditorUtility.SetDirty(r); }
            foreach (var old in pivot.Cast<Transform>().Where(t => t.name == "TripoCam").ToArray()) Object.DestroyImmediate(old.gameObject);

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pivot);
            inst.name = "TripoCam";
            inst.transform.localRotation = Quaternion.identity;                   // объектив модели смотрит по +Z — как и поворотная часть
            inst.transform.localScale = Vector3.one;
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);   // камера не должна заслонять свой же луч обзора
            var b = LocalBounds(inst, pivot);
            float s = 0.55f / Mathf.Max(0.001f, b.size.z);
            inst.transform.localScale = Vector3.one * s;
            b = LocalBounds(inst, pivot);
            inst.transform.localPosition += new Vector3(-b.center.x, -b.center.y, 0.06f - b.center.z);
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
            n++;
        }
        log.AppendLine($"  cameras: {n} SecurityCamera(s) now use Bank_SecurityCamera (alarm logic unchanged)");
    }

    static Bounds LocalBounds(GameObject go, Transform space)
    {
        bool first = true; var b = new Bounds();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1));
                var p = space.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
            }
        }
        return b;
    }

    // =========================================================================== операционный зал
    static void ReplaceAtms(Transform g, Transform map)
    {
        if (!Prefab("Bank_ATM")) return;
        foreach (var atm in Named(map, "Z2_MainHall", "ATM"))
        {
            HideQuiet(atm);
            Place(g, "Bank_ATM", new Vector3(atm.position.x, 0, atm.position.z), atm.eulerAngles.y, 1.9f);
        }
    }

    static void ReplaceSofasWithBenches(Transform g, Transform map)
    {
        if (!Prefab("Bank_LeatherBench")) return;
        foreach (var sofa in Named(map, "Z2_MainHall", "Sofa")) HideQuiet(sofa);
        // скамьи для клиентов между колоннами (у касс места нет — там очередь)
        foreach (var (x, z, yaw) in new[] { (-3.6f, 10.5f, 90f), (3.6f, 10.5f, -90f), (-3.6f, 6.5f, 90f), (3.6f, 6.5f, -90f) })
            Place(g, "Bank_LeatherBench", new Vector3(x, 0, z), yaw, 0, 2.6f);
    }

    static void ReplacePlants(Transform g, Transform map)
    {
        if (!Prefab("Bank_PottedPlant")) return;
        foreach (var zone in new[] { "Z2_MainHall", "Z4_Offices" })
        {
            foreach (var pot in Named(map, zone, "Pot"))
            {
                HideQuiet(pot);
                Place(g, "Bank_PottedPlant", new Vector3(pot.position.x, pot.position.y - 0.3f, pot.position.z), 0f, 1.7f);
            }
            foreach (var f in Named(map, zone, "Foliage")) HideQuiet(f);
        }
        Place(g, "Bank_PottedPlant", new Vector3(12.6f, 0, 1.4f), 0f, 1.7f);
    }

    static void ReplaceOfficeDesks(Transform g, Transform map)
    {
        if (!Prefab("Bank_OfficeDesk")) return;
        var desks = new List<GameObject>();
        foreach (var d in Named(map, "Z4_Offices", "Desk"))
        {
            HideQuiet(d);
            var nd = Place(g, "Bank_OfficeDesk", d.position, d.eulerAngles.y, 0, 1.8f);
            if (nd) desks.Add(nd);
        }
        // слиток, лежавший на старом столе, — на столешницу нового
        var zone = map.Find("Z4_Offices");
        var bar = zone ? zone.GetComponentsInChildren<LootItem>(true).FirstOrDefault(l => l.lootId == "GoldBar") : null;
        if (bar && desks.Count > 0)
        {
            var desk = desks.OrderBy(d => (WorldBounds(d).center - bar.transform.position).sqrMagnitude).First();
            PutOnTop(bar.transform, desk, 4.5f + 0.6f, 4.5f + 1.1f);
        }
    }

    /// Кладёт объект на верхнюю поверхность модели (лучи сверху вниз по её площади, берём ближайшую к центру точку в диапазоне высот).
    static void PutOnTop(Transform item, GameObject surface, float minY, float maxY)
    {
        Physics.SyncTransforms();
        var b = WorldBounds(surface);
        Vector3 best = Vector3.zero; float bestD = float.MaxValue;
        for (float x = b.min.x + 0.1f; x < b.max.x - 0.1f; x += 0.1f)
            for (float z = b.min.z + 0.1f; z < b.max.z - 0.1f; z += 0.1f)
                if (Physics.Raycast(new Vector3(x, b.max.y + 0.5f, z), Vector3.down, out var hit, b.size.y + 1f) && hit.collider.transform.IsChildOf(surface.transform)
                    && hit.point.y > minY && hit.point.y < maxY && hit.normal.y > 0.9f)
                {
                    float d = (new Vector2(x, z) - new Vector2(b.center.x, b.center.z)).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = hit.point; }
                }
        if (bestD == float.MaxValue) { log.AppendLine($"  WARN: no surface for {item.name} on {surface.name}"); return; }
        item.position = best + Vector3.up * 0.005f;
        EditorUtility.SetDirty(item);
        log.AppendLine($"  {item.name} placed on {surface.name} at {best}");
    }

    /// Ряд касс у северной стены (за ними — служебная зона и дверь хранилища) + кованые решётки по бокам центрального прохода.
    static void BuildTellerRow(Transform g)
    {
        if (Prefab("Bank_TellerCounter"))
            foreach (float x in new[] { -10f, -7.2f, -4.4f, 4.4f, 7.2f, 10f })
                Place(g, "Bank_TellerCounter", new Vector3(x, 0, 21.3f), 180f, 0, 2.5f);   // окошком к клиентам (−Z)
        if (Prefab("Bank_IronBars"))
        {
            foreach (float x in new[] { -3.1f, 3.1f, -11.75f, 11.75f })
                Place(g, "Bank_IronBars", new Vector3(x, 0, 21.9f), BarsYaw(alongX: false), 0, 3.8f, targetHeight: 2.4f);
            // в хранилище: перед стеной депозитных ячеек
            Place(g, "Bank_IronBars", new Vector3(-6.7f, 0, 39.2f), BarsYaw(alongX: true), 0, 6.6f, targetHeight: 2.6f);
        }
    }

    /// Решётка — тонкая панель; поворачиваем так, чтобы она тянулась вдоль нужной оси.
    static float BarsYaw(bool alongX)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(Prefab("Bank_IronBars"));
        try
        {
            var b = WorldBounds(inst);
            bool modelAlongX = b.size.x >= b.size.z;
            return modelAlongX == alongX ? 0f : 90f;
        }
        finally { Object.DestroyImmediate(inst); }
    }

    // =========================================================================== сетевая дверь хранилища
    static void BuildVaultDoor(Transform root, Transform map)
    {
        var prefab = Prefab("Bank_VaultDoor");
        if (!prefab) { log.AppendLine("ERROR: no Bank_VaultDoor prefab"); return; }
        var gate = Named(map, "Z2_MainHall", "VaultGate").FirstOrDefault();
        HideQuiet(gate);

        // модель: высота 3.4 м, толщина сплющена до ~0.45 м, штурвалом в Холл (−Z), в проёме северной стены Холла (z = 23.85)
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var b0 = WorldBounds(model);
        float s = 3.4f / b0.size.y;
        float depthK = 0.45f / (b0.size.z * s);
        model.transform.localScale = new Vector3(s, s, s * depthK);
        model.transform.rotation = Quaternion.Euler(0, 180, 0);
        var b = WorldBounds(model);
        float zc = 23.85f - b.size.z / 2 - 0.02f;
        model.transform.position += new Vector3(-b.center.x, -b.min.y, zc - b.center.z);
        b = WorldBounds(model);
        foreach (var bc in model.GetComponentsInChildren<BoxCollider>()) Object.DestroyImmediate(bc);
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;

        var doorRoot = new GameObject("VaultDoor_Net").transform;
        doorRoot.SetParent(root, false);
        doorRoot.SetPositionAndRotation(new Vector3(b.min.x, 0, zc), Quaternion.identity);   // петля — западный край двери
        var hinge = Group(doorRoot, "Hinge");
        model.transform.SetParent(hinge, true);
        foreach (var t in doorRoot.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;

        var status = Label(doorRoot, "VaultStatus", "ХРАНИЛИЩЕ ЗАКРЫТО", 2.2f, new Color(1f, 0.85f, 0.4f), new Vector3(0, 3.85f, 23.82f), Vector3.back, new Vector2(3.6f, 0.4f));
        var lamp = Ball(doorRoot, "VaultLamp", new Vector3(2.3f, 3.85f, 23.8f), Vector3.one * 0.2f, stripRed);
        var src = doorRoot.gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false; src.spatialBlend = 1f; src.minDistance = 2f; src.maxDistance = 30f;

        var no = doorRoot.gameObject.AddComponent<NetworkObject>();
        SetMasterFlags(no);
        var door = doorRoot.gameObject.AddComponent<NetworkVaultDoor>();
        door.hinge = hinge;
        door.openAngle = 105f;                                                    // распахивается в Холл
        door.hackSeconds = 5f;
        door.statusText = status;
        door.statusLamp = lamp.GetComponent<Renderer>();
        door.audioSource = src;
        door.openClips = MapKit.Clips(UiDir, "open", "maximize");
        door.closeClips = MapKit.Clips(ImpactDir, "impact_metal_heavy");
        door.hackClips = MapKit.Clips(UiDir, "click");

        // кнопки бывшей решётки → дверь: в Холле «ВЗЛОМ», в хранилище «ДВЕРЬ»
        Rebind(map, "GatePlate_Hall", door.PressHack, "ВЗЛОМ");
        Rebind(map, "GatePlate_Vault", door.PressToggle, "ДВЕРЬ");
        log.AppendLine($"  vault door: networked NetworkVaultDoor at z {zc:0.00}, width {b.size.x:0.00}");
    }

    static void Rebind(Transform map, string plateName, UnityEngine.Events.UnityAction action, string label)
    {
        var plate = map.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == plateName);
        var btn = plate ? plate.GetComponentInChildren<PushButton>(true) : null;
        if (!btn) { log.AppendLine($"ERROR: button {plateName} not found"); return; }
        while (btn.onPressed.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(btn.onPressed, 0);
        UnityEventTools.AddVoidPersistentListener(btn.onPressed, action);
        btn.label = label;
        EditorUtility.SetDirty(btn);
        foreach (var t in plate.GetComponentsInChildren<TextMeshPro>(true)) { t.text = label; EditorUtility.SetDirty(t); }
    }
}
