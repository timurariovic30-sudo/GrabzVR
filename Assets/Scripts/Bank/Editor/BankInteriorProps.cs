using System.Collections.Generic;
using System.Linq;
using Fusion;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;

/// <summary>
/// Интерьер настоящего Банка (часть BankVisualOverhaul) — Tripo-модели вместо примитивов:
///  • камеры видеонаблюдения — исходные из примитивов билдера (тревога AlarmSystem как была);
///  • операционный зал: банкоматы и скамьи из примитивов (BankFurniture), растения билдера;
///  • офисы: столы менеджеров из примитивов (на одном — слиток).
/// Кнопки бывшей решётки хранилища переподключает к сетевой двери подвала BankServiceWing (Rebind).
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
    /// Камеры видеонаблюдения — снова исходные из примитивов билдера (модель Tripo торчала из стен): включаем старый корпус.
    static void ReplaceCameras(Transform map)
    {
        int n = 0;
        foreach (var sc in map.GetComponentsInChildren<SecurityCamera>(true))
        {
            var pivot = sc.pivot ? sc.pivot : sc.transform;
            foreach (var old in pivot.Cast<Transform>().Where(t => t.name == "TripoCam").ToArray()) Object.DestroyImmediate(old.gameObject);
            foreach (var r in sc.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name is "Bracket" or "Body" or "Hood" or "Lens" or "Led") { r.enabled = true; EditorUtility.SetDirty(r); }
            n++;
        }
        log.AppendLine($"  cameras: {n} SecurityCamera(s) use the original primitive bodies");
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
        foreach (var atm in Named(map, "Z2_MainHall", "ATM"))
        {
            HideQuiet(atm);
            Atm(g, new Vector3(13.5f, 0, atm.position.z), 90f);                     // спиной к восточной стене, лицом в зал
        }
    }

    static void ReplaceSofasWithBenches(Transform g, Transform map)
    {
        foreach (var sofa in Named(map, "Z2_MainHall", "Sofa")) HideQuiet(sofa);
        // скамьи для клиентов между колоннами
        foreach (var (x, z) in new[] { (-3.6f, 10.5f), (3.6f, 10.5f), (-3.6f, 6.5f), (3.6f, 6.5f) })
            Bench(g, new Vector3(x, 0, z), 90f, 2.4f);
    }

    /// Растения — исходные из билдера (горшок + крона): модели Tripo убраны.
    static void ReplacePlants(Transform g, Transform map)
    {
        foreach (var zone in new[] { "Z2_MainHall", "Z4_Offices" })
            foreach (var t in map.Find(zone).GetComponentsInChildren<Transform>(true).Where(t => t.name is "Pot" or "Foliage"))
                if (!t.gameObject.activeSelf) { t.gameObject.SetActive(true); EditorUtility.SetDirty(t.gameObject); }
    }

    static void ReplaceOfficeDesks(Transform g, Transform map)
    {
        var desks = new List<GameObject>();
        foreach (var d in Named(map, "Z4_Offices", "Desk"))
        {
            HideQuiet(d);
            desks.Add(OfficeDesk(g, d.position, d.eulerAngles.y).gameObject);
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
