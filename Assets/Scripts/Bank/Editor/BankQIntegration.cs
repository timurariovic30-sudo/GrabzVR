using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ЭТАП 2: переносит новый банк BankQ (CC0 Quaternius + модульный кит Blender, префаб BankQuaterniusBuilder) в HeistLevel_Test.
/// Ничего сетевого не создаёт и не удаляет: существующие объекты (двери NetworkVaultDoor, щиток лазеров, лут, лазеры,
/// камеры, табло, зона сдачи, автобус прибытия, телепорты) переставляются на якоря Anchor_* префаба.
/// Старая геометрия (HeistMap/Z*/ кроме Dynamic, BankDecor/Static и свет, BankDetail) прячется (SetActive false), не удаляется.
/// Идемпотентный: все позиции абсолютные (от якорей), корень сцены «BankQ» пересоздаётся.
/// Меню: Tools/Heist/Bank/Integrate BankQ Into Heist Level. Batchmode (без -nographics): -executeMethod BankQIntegration.RunBatch
/// </summary>
public static class BankQIntegration
{
    const string ScenePath = "Assets/Scenes/HeistLevel_Test.unity";
    const string PrefabPath = "Assets/Art/GeneratedModels/Bank/BankQ/BankQ.prefab";
    const float FL = BankQuaterniusBuilder.FL;
    static readonly StringBuilder log = new StringBuilder();
    static Dictionary<string, Transform> anchors;
    static Transform bank;
    static readonly HashSet<Transform> placed = new HashSet<Transform>();

    [MenuItem("Tools/Heist/Bank/Integrate BankQ Into Heist Level")]
    static void Menu() => Debug.Log(Run());

    public static void RunBatch()
    {
        string r;
        try { r = Run(); }
        catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); return; }
        Debug.Log(r);
        EditorApplication.Exit(r.Contains("ERROR") ? 1 : 0);
    }

    public static string Run()
    {
        log.Clear(); placed.Clear(); log.AppendLine("[BankQIntegration]");
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        Transform T(string path) { var t = all.FirstOrDefault(x => PathOf(x) == path); if (!t) log.AppendLine("  ERROR: not found " + path); return t; }

        HideOld(scene);
        // новый банк
        var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "BankQ"); if (old) Object.DestroyImmediate(old);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (!prefab) { log.AppendLine("  ERROR: BankQ prefab missing — run Tools/Heist/Bank/Quaternius Rebuild first"); return log.ToString(); }
        bank = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, scene)).transform;
        bank.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        int edOnly = 0;
        foreach (var t in bank.GetComponentsInChildren<Transform>(true)) if (t.CompareTag("EditorOnly") && t.gameObject.activeSelf) { t.gameObject.SetActive(false); edOnly++; }
        // припаркованная машина на маршруте отъезда автобуса
        foreach (var t in bank.GetComponentsInChildren<Transform>(true))
            if (t.parent && t.parent.name == "Street" && t.name.StartsWith("SimpleCarHigh") && t.position.x > 0) t.gameObject.SetActive(false);
        anchors = bank.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Anchor_")).ToDictionary(t => t.name.Substring(7), t => t);
        log.AppendLine($"  BankQ instance: anchors {anchors.Count}, editor-only previews hidden {edOnly}");
        Physics.SyncTransforms();

        PlaceVan(all);
        PlaceDoors(T);
        PlaceLasers(all);
        all.RemoveAll(t => !t);                       // старые лучи пересозданы
        all.AddRange(Object.FindObjectsByType<LaserBeam>(FindObjectsInactive.Include).Select(b => b.transform));
        PlaceShowcasesAndLoot(all);
        PlaceMisc(all, T);
        HideLeftovers(all);

        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorApplication.ExecuteMenuItem("Tools/Fusion/Scene/Bake Scene Objects");
        if (!EditorSceneManager.SaveScene(scene)) log.AppendLine("  ERROR: save failed");
        Checks();
        RenderShots();
        return log.ToString();
    }

    static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
    static Vector3 A(string n) { if (anchors.TryGetValue(n, out var t)) return t.position; log.AppendLine("  ERROR: anchor missing " + n); return Vector3.zero; }
    static float AYaw(string n) => anchors.TryGetValue(n, out var t) ? t.eulerAngles.y : 0f;

    static Bounds RB(Transform t, bool triggers = false)
    {
        var rs = t.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
        if (rs.Length == 0 || triggers)
        {
            var cs = t.GetComponentsInChildren<Collider>();
            if (cs.Length == 0) return new Bounds(t.position, Vector3.zero);
            var cb = cs[0].bounds; foreach (var c in cs) cb.Encapsulate(c.bounds); return cb;
        }
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
    }

    /// Поверхность нового банка под точкой (луч сверху вниз, только коллайдеры BankQ, без триггеров).
    static float Surface(float x, float z, float fromY)
    {
        var hits = Physics.RaycastAll(new Vector3(x, fromY, z), Vector3.down, fromY + 10f, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => h.transform.IsChildOf(bank)).OrderBy(h => h.distance).ToArray();
        return hits.Length > 0 ? hits[0].point.y : FL;
    }

    /// Поставить объект низом на поверхность в точке (x, z), повернув на yaw (относительно текущего поворота — абсолютный yaw).
    static void Drop(Transform t, float x, float z, float fromY, float yaw, float lift = 0.005f)
    {
        if (!t) return;
        t.rotation = Quaternion.Euler(t.eulerAngles.x, yaw, t.eulerAngles.z);
        var b = RB(t); var surf = Surface(x, z, fromY);
        t.position += new Vector3(x - b.center.x, surf + lift - b.min.y, z - b.center.z);
        placed.Add(t); Physics.SyncTransforms();
    }

    // ================================================================== старое
    static void HideOld(UnityEngine.SceneManagement.Scene scene)
    {
        int n = 0;
        var roots = scene.GetRootGameObjects();
        var map = roots.FirstOrDefault(g => g.name == "HeistMap");
        if (map) foreach (Transform zone in map.transform) foreach (Transform c in zone) if (c.name != "Dynamic" && c.gameObject.activeSelf) { c.gameObject.SetActive(false); n++; }
        var decor = roots.FirstOrDefault(g => g.name == "BankDecor");
        if (decor) foreach (var name in new[] { "Static", "StreetLights", "Lighting", "ServiceLights" })
            { var c = decor.transform.Find(name); if (c && c.gameObject.activeSelf) { c.gameObject.SetActive(false); n++; } }
        var detail = roots.FirstOrDefault(g => g.name == "BankDetail"); if (detail && detail.activeSelf) { detail.SetActive(false); n++; }
        log.AppendLine($"  old geometry hidden: {n} groups");
    }

    // ================================================================== автобус прибытия
    static void PlaceVan(List<Transform> all)
    {
        var trig = all.Select(t => t.GetComponent<NetworkHeistStartTrigger>()).FirstOrDefault(c => c);
        if (!trig) { log.AppendLine("  ERROR: NetworkHeistStartTrigger not found"); return; }
        var root = trig.transform;
        root.position = new Vector3(0f, root.position.y, -13.4f);
        Physics.SyncTransforms();
        var wheels = RB(trig.van ? trig.van : root).min.y;
        float dy = 0f - wheels;                                    // колёса на асфальт (верх дороги BankQ — y 0)
        root.position += Vector3.up * dy; placed.Add(root);
        float y = root.position.y;
        // маршрут: назад от ступенек, поворот направо и по южной полосе на восток, за край карты
        trig.path = new[] { new Vector3(0, y, -13.4f), new Vector3(0.05f, y, -14.6f), new Vector3(0.7f, y, -15.5f), new Vector3(2.2f, y, -15.95f),
                            new Vector3(5f, y, -16.05f), new Vector3(60f, y, -16.05f), new Vector3(85f, y, -16.05f) };
        EditorUtility.SetDirty(trig);
        var street = new Vector3(0f, 0.14f, -5.3f);
        if (trig.exitPoint) { trig.exitPoint.position = street; placed.Add(trig.exitPoint); }
        var spawn = all.FirstOrDefault(t => t.name == "PlayerSpawn_Van");
        if (spawn)
        {
            // точка появления в салоне: та же высота над колёсами, что была у старого автобуса (повторный запуск не сдвигает её второй раз)
            float above = spawn.position.y - (root.position.y - dy);
            if (Mathf.Abs(dy) < 0.01f) above = spawn.position.y - y;
            spawn.position = new Vector3(spawn.position.x, y + above, spawn.position.z); placed.Add(spawn);
            foreach (var p in all.Where(t => (t.name == "DesktopTestPlayer" || t.name == "VRGorillaPlayer") && t.parent == null))
                p.position = new Vector3(spawn.position.x, spawn.position.y, spawn.position.z);
        }
        var tp = all.FirstOrDefault(t => t.name == "TeleportPoint_Street"); if (tp) { tp.position = street; placed.Add(tp); }
        log.AppendLine($"  van: root y {y:F2} (dy {dy:F2}), path {trig.path.Length} pts along the south lane, exit {street}");
    }

    // ================================================================== двери и щиток
    static void PlaceDoors(System.Func<string, Transform> T)
    {
        foreach (var (obj, anchor) in new[] { ("BankDecor/ServiceDoor_A_Net", "ServiceDoorA"), ("BankDecor/ServiceDoor_B_Net", "ServiceDoorB"),
                                               ("BankDecor/RoomDoor_Director_Net", "RoomDoor_Director"), ("BankDecor/RoomDoor_BreakRoom_Net", "RoomDoor_BreakRoom"),
                                               ("BankDecor/RoomDoor_ServerRoom_Net", "RoomDoor_ServerRoom") })
        {
            var t = T(obj); if (!t) continue;
            t.SetPositionAndRotation(A(anchor), Quaternion.Euler(0, AYaw(anchor), 0)); placed.Add(t);
        }
        // круглая дверь хранилища: центр двери — в центр рамы, «внутрь хранилища» (+Z у старой) → +X
        var vd = T("BankDecor/VaultDoor_Net");
        if (vd)
        {
            vd.rotation = Quaternion.Euler(0, 90f, 0); Physics.SyncTransforms();
            var c = RB(vd).center; vd.position += A("VaultDoor") - c; placed.Add(vd);
            var size = RB(vd).size; log.AppendLine($"  vault door size {size.x:F2}x{size.y:F2}x{size.z:F2} at {RB(vd).center}");
        }
        // щиток лазеров: старый смотрит в +Z, новый — со стены x = -6 в кабинет (−X)
        var lc = T("BankDecor/LaserControl_Net");
        if (lc) { lc.SetPositionAndRotation(A("LaserControl"), Quaternion.Euler(0, -90f, 0)); placed.Add(lc); }
        log.AppendLine("  doors + laser control placed");
    }

    // ================================================================== лазеры: 3 решётки → 13 лучей по якорям, колонками по X
    static void PlaceLasers(List<Transform> all)
    {
        var grids = all.Select(t => t.GetComponent<LaserAlarm>()).Where(c => c).OrderBy(c => c.name).ToList();
        if (grids.Count == 0) { log.AppendLine("  ERROR: no LaserAlarm grids"); return; }
        var beamsA = anchors.Where(kv => kv.Key.StartsWith("Laser_")).Select(kv => kv.Value).OrderBy(t => t.position.x).ThenBy(t => t.position.y).ToList();
        var cols = beamsA.GroupBy(t => Mathf.Round(t.position.x * 10f)).Select(g => g.ToList()).ToList();
        // колонки 0..3 → решётки: 0 → [0], 1 → [1,2], 2 → [3]
        var assign = new List<List<Transform>>();
        for (int i = 0; i < grids.Count; i++) assign.Add(new List<Transform>());
        for (int c = 0; c < cols.Count; c++) assign[Mathf.Min(grids.Count - 1, c * grids.Count / cols.Count)].AddRange(cols[c]);
        int made = 0;
        for (int i = 0; i < grids.Count; i++)
        {
            var g = grids[i]; var tmpl = g.GetComponentInChildren<LaserBeam>(true);
            if (!tmpl || assign[i].Count == 0) { g.gameObject.SetActive(false); continue; }
            float cx = assign[i].Average(t => t.position.x);
            g.transform.SetPositionAndRotation(new Vector3(cx, FL, 24f), Quaternion.identity); g.transform.localScale = Vector3.one;
            // амплитуда под низкий тамбур: качание вдоль прохода (X) или вверх-вниз
            g.amplitude = Mathf.Min(g.amplitude, g.axis == LaserAlarm.MoveAxis.UpDown ? 0.18f : 0.35f);
            if (g.axis == LaserAlarm.MoveAxis.ForwardBack) g.axis = LaserAlarm.MoveAxis.LeftRight;
            var tmplGo = Object.Instantiate(tmpl.gameObject); tmplGo.name = "BeamTemplate";
            foreach (var b in g.GetComponentsInChildren<LaserBeam>(true).ToList()) Object.DestroyImmediate(b.gameObject);
            int k = 0;
            foreach (var a in assign[i])
            {
                var nb = Object.Instantiate(tmplGo, g.transform); nb.name = "Beam_" + k++;
                nb.transform.SetPositionAndRotation(a.position, Quaternion.identity);
                nb.transform.localScale = new Vector3(0.03f, 0.03f, a.lossyScale.z);
                nb.SetActive(true); made++;
            }
            Object.DestroyImmediate(tmplGo);
            EditorUtility.SetDirty(g); placed.Add(g.transform);
        }
        log.AppendLine($"  lasers: {grids.Count} grids, {made} beams in the vault anteroom");
    }

    // ================================================================== витрины и лут
    static void PlaceShowcasesAndLoot(List<Transform> all)
    {
        var loot = all.Select(t => t.GetComponent<LootItem>()).Where(c => c).Select(c => c.transform).ToList();
        var cases = all.Select(t => t.GetComponent<BreakableGlass>()).Where(c => c).Select(c => c.transform).ToList();
        var used = new HashSet<Transform>();
        // витрины в зал (с лутом внутри — тем же сдвигом)
        var caseSpots = new[] { new Vector2(-6f, 4.5f), new Vector2(6f, 4.5f), new Vector2(-6f, 9.6f), new Vector2(6f, 9.6f), new Vector2(-12.5f, 13f), new Vector2(12.5f, 13f) };
        for (int i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            var cb = RB(c);
            var inside = loot.Where(l => !used.Contains(l) && Mathf.Abs(l.position.x - cb.center.x) < cb.extents.x + 0.1f && Mathf.Abs(l.position.z - cb.center.z) < cb.extents.z + 0.1f).ToList();
            if (i >= caseSpots.Length) { log.AppendLine("  ERROR: no spot for showcase " + c.name); continue; }
            var before = c.position;
            Drop(c, caseSpots[i].x, caseSpots[i].y, FL + 3f, c.eulerAngles.y);
            var d = c.position - before;
            foreach (var l in inside) { l.position += d; used.Add(l); placed.Add(l); }
            log.AppendLine($"  showcase {c.name} → hall ({caseSpots[i].x}, {caseSpots[i].y}) with {inside.Count} loot");
        }
        Transform Take(System.Func<Transform, bool> f) { var t = loot.FirstOrDefault(l => !used.Contains(l) && f(l)); if (t) used.Add(t); return t; }
        bool Is(Transform t, string n) => t.name.StartsWith(n);
        // главное — в хранилище
        var statue = Take(t => Is(t, "Loot_GoldenStatue"));
        if (statue) { var a = A("Loot_Vault_4"); Drop(statue, a.x, a.z, FL + 3.5f, 270f); }
        var safe = Take(t => Is(t, "Loot_Safe"));
        if (safe) Drop(safe, 30.45f, 24f, FL + 3.5f, 270f);
        for (int i = 0; i < 4; i++)
        {
            var bar = Take(t => Is(t, "Loot_GoldBar") && t.position.y < -1f) ?? Take(t => Is(t, "Loot_GoldBar"));
            if (bar) { var a = A("Loot_Vault_" + i); Drop(bar, a.x, a.z, FL + 3.5f, 90f); }
        }
        // служебное крыло
        var card = Take(t => Is(t, "Loot_KeyCard")); if (card) { var a = A("Loot_KeyCard"); Drop(card, a.x, a.z, FL + 2.5f, 25f); }
        var cash = new[] { A("Loot_BreakCash"), A("Loot_DirectorSafe"), new Vector3(-10.2f, 0, 29.3f) };
        foreach (var p in cash) { var c = Take(t => Is(t, "Loot_CashStack")); if (c) Drop(c, p.x, p.z, FL + 2.5f, 15f); }
        // картины — на стену коридора (лицом в коридор: видимая сторона у них −Z)
        float[] px = { -14f, -6f, 4f, 14f }; int pi = 0;
        for (Transform p; pi < px.Length && (p = Take(t => Is(t, "Loot_Painting"))) != null; pi++)
        {
            p.rotation = Quaternion.Euler(0, 0, 0); Physics.SyncTransforms();
            float wall = 24f - 0.15f;
            if (Physics.Raycast(new Vector3(px[pi], FL + 1.9f, 22f), Vector3.forward, out var h, 4f, ~0, QueryTriggerInteraction.Ignore) && h.transform.IsChildOf(bank)) wall = h.point.z;
            var b = RB(p); p.position += new Vector3(px[pi] - b.center.x, FL + 1.9f - b.center.y, wall - 0.01f - b.max.z);
            placed.Add(p);
        }
        // вазы — на столики в зоне ожидания, остальное золото — на столы кассиров
        foreach (var s in new[] { -12.5f, 12.5f }) { var v = Take(t => Is(t, "Loot_Vase")); if (v) Drop(v, s, 6.5f, FL + 3f, 0f); }
        int desk = 0;
        for (Transform r; (r = Take(_ => true)) != null; desk++)
        {
            float x = -7.5f + (desk % 6) * 3f + (desk >= 6 ? 0.6f : 0f);
            Drop(r, x, 16.2f, FL + 1.6f, 180f);
            log.AppendLine($"  extra loot {r.name} → teller desk {desk}");
        }
        log.AppendLine($"  loot placed: {loot.Count} items ({cases.Count} showcases)");
    }

    // ================================================================== камеры, табло, эвакуация, телепорты, кассирши, батут
    static void PlaceMisc(List<Transform> all, System.Func<string, Transform> T)
    {
        var cams = all.Select(t => t.GetComponent<SecurityCamera>()).Where(c => c).Select(c => c.transform).OrderBy(t => PathOf(t)).ToList();
        var camAnchors = Enumerable.Range(0, 8).Select(i => ("Camera_" + i, A("Camera_" + i), AYaw("Camera_" + i))).ToList();
        camAnchors.Add(("extra", new Vector3(15.5f, FL + 3.1f, 20.6f), -90f));
        for (int i = 0; i < cams.Count; i++)
        {
            if (i >= camAnchors.Count) { cams[i].gameObject.SetActive(false); continue; }
            var e = cams[i].eulerAngles;
            cams[i].SetPositionAndRotation(camAnchors[i].Item2, Quaternion.Euler(e.x, camAnchors[i].Item3, e.z)); placed.Add(cams[i]);
        }
        log.AppendLine($"  security cameras: {cams.Count} → {Mathf.Min(cams.Count, camAnchors.Count)} anchors");
        // табло: на северной стене зала и на северной стене гаража
        foreach (var sb in all.Select(t => t.GetComponent<Scoreboard>()).Where(c => c && c.gameObject.activeInHierarchy).Select(c => c.transform))
        {
            if (sb.name.Contains("Garage")) sb.SetPositionAndRotation(new Vector3(24f, FL + 3.2f, 19.65f), Quaternion.identity);
            else sb.SetPositionAndRotation(A("Scoreboard_0"), Quaternion.identity);
            placed.Add(sb);
        }
        // зона сдачи лута — на площадку у машины в гараже
        foreach (var ez in all.Select(t => t.GetComponent<EvacuationZone>()).Where(c => c).Select(c => c.transform))
        {
            ez.rotation = Quaternion.identity; Physics.SyncTransforms();
            var b = RB(ez, true); var a = A("Evacuation");
            ez.position += new Vector3(a.x - b.center.x, FL + 0.02f - b.min.y, a.z - b.center.z); placed.Add(ez);
            log.AppendLine($"  evacuation zone {b.size.x:F1}x{b.size.y:F1}x{b.size.z:F1} → garage pad");
        }
        // плита «телепорт в хаб» в хранилище; точки телепорта отладки
        var plate = all.FirstOrDefault(t => t.name == "TeleportPlate_Vault");
        if (plate) { var a = A("TeleportVault"); Drop(plate, a.x, a.z, FL + 3.5f, 90f); }
        foreach (var dbg in all.Select(t => t.GetComponent<HeistDebugActions>()).Where(c => c))
        {
            if (dbg.vaultPoint) { dbg.vaultPoint.position = A("TeleportVault") + Vector3.up * 0.1f; placed.Add(dbg.vaultPoint); }
            if (dbg.hubPoint && !dbg.hubPoint.name.Contains("Street")) { dbg.hubPoint.position = new Vector3(0f, 0.14f, -5.3f); placed.Add(dbg.hubPoint); }
        }
        // точки под NPC-кассирш
        var cashiers = T("BankDecor/CashierSpawnPoints");
        if (cashiers) { int i = 1; foreach (Transform c in cashiers) { if (anchors.ContainsKey("Cashier_" + i)) c.SetPositionAndRotation(A("Cashier_" + i), Quaternion.Euler(0, AYaw("Cashier_" + i), 0)); i++; } placed.Add(cashiers); }
        // батут в углу зала (паркур к люстрам)
        var tramp = all.FirstOrDefault(t => t.name == "Trampoline" && PathOf(t).Contains("Z2_MainHall"));
        if (tramp) Drop(tramp, -12.8f, 17.6f, FL + 4f, 0f);
    }

    // остальное старое из Z*/Dynamic: без сети — прячем; сетевое оставшееся — сообщаем
    static void HideLeftovers(List<Transform> all)
    {
        int hidden = 0;
        foreach (var dyn in all.Where(t => t.name == "Dynamic" && t.parent && t.parent.parent && t.parent.parent.name == "HeistMap"))
            foreach (Transform c in dyn)
            {
                if (!c.gameObject.activeSelf || placed.Any(p => p == c || p.IsChildOf(c) || c.IsChildOf(p))) continue;
                if (c.GetComponentInChildren<NetworkObject>(true)) { log.AppendLine($"  WARNING: networked leftover kept in place: {PathOf(c)}"); continue; }
                c.gameObject.SetActive(false); hidden++;
            }
        log.AppendLine($"  leftovers hidden: {hidden}");
    }

    // ================================================================== проверки
    static void Checks()
    {
        var nos = Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include);
        int inactive = nos.Count(n => !n.gameObject.activeInHierarchy);
        log.AppendLine($"  check: NetworkObjects {nos.Length}, inactive {inactive}");
        // лут не висит в воздухе и не утоплен
        foreach (var l in Object.FindObjectsByType<LootItem>(FindObjectsInactive.Exclude))
        {
            var b = RB(l.transform);
            bool support = Physics.Raycast(new Vector3(b.center.x, b.min.y + 0.05f, b.center.z), Vector3.down, 0.3f, ~0, QueryTriggerInteraction.Ignore)
                           || l.name.StartsWith("Loot_Painting");
            if (!support) log.AppendLine($"  WARNING: loot without support: {l.name} at {b.center}");
        }
    }

    // ================================================================== снимки для QA
    static void RenderShots()
    {
        Directory.CreateDirectory("HubPreview");
        var shots = new (string name, Vector3 pos, Vector3 look)[]
        {
            ("BankGame_1_Street", new Vector3(-9f, 2.2f, -19f), new Vector3(0f, 2.5f, -6f)),
            ("BankGame_2_Hall", new Vector3(0f, FL + 1.7f, 1.2f), new Vector3(0f, FL + 1.2f, 14f)),
            ("BankGame_3_Corridor", new Vector3(-15f, FL + 1.6f, 22f), new Vector3(10f, FL + 1.2f, 22.5f)),
            ("BankGame_4_Director", new Vector3(-8.5f, FL + 1.6f, 25.2f), new Vector3(-12f, FL + 1.0f, 30.5f)),
            ("BankGame_5_Lasers", new Vector3(16.6f, FL + 1.6f, 22.2f), new Vector3(24f, FL + 1.3f, 24.5f)),
            ("BankGame_6_Vault", new Vector3(25f, FL + 1.8f, 21f), new Vector3(30f, FL + 0.8f, 25f)),
            ("BankGame_7_Garage", new Vector3(18.5f, FL + 2.2f, 2f), new Vector3(24f, FL + 0.8f, 13f)),
        };
        var camGo = new GameObject("ShotCam"); var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 70f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 300f;
        var rt = new RenderTexture(1600, 900, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
        cam.transform.position = shots[0].pos; cam.transform.LookAt(shots[0].look);
        for (int i = 0; i < 2; i++) cam.Render();
        foreach (var s in shots)
        {
            cam.transform.position = s.pos; cam.transform.LookAt(s.look); cam.Render(); RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            File.WriteAllBytes($"HubPreview/{s.name}.png", tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        }
        RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(camGo);
        log.AppendLine($"  shots: {shots.Length} → HubPreview/BankGame_*.png");
    }
}
