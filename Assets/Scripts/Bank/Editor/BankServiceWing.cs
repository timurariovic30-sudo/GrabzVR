using System.Collections.Generic;
using System.Linq;
using Fusion;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Настоящая планировка банка Майами (часть BankVisualOverhaul):
///  • Операционный зал: светлый мрамор с золотыми полосами, белые колонны с золотом, панорамные «закатные» окна
///    с лучами света, длинные кассовые стойки Bank_MiamiTellerDesk с логотипом SUNSET TRUST BANK, рабочая зона
///    кассиров (кресла, кассовые аппараты, мониторы) и пустые точки CashierSpawnPoint_1..4 под будущих NPC.
///  • Служебное крыло (бывшее хранилище на 1-м этаже): две сетевые служебные двери из зала (NetworkVaultDoor),
///    служебный коридор, комната отдыха (диван, кухня с кофемашиной и микроволновкой, шкафчики, кулер, ключ-карта
///    и пачки денег), серверная (стойки, люк вентиляции к подвалу), кабинет директора (стол, сейф за картиной,
///    пульт видеонаблюдения, сетевой щиток лазеров NetworkLaserControl).
///  • Подвал: широкая бетонная лестница с пандусом и перилами из служебного коридора, предбанник с выходом
///    в подземный гараж (эвакуация), круглая сетевая бронедверь, лазеры, ячейки и главный лут — сейф $2500,
///    золотая статуя $1500, слитки и бриллиант (перенесены из старого хранилища и галереи).
/// Всё новое — в BankDecor; старые объекты переносятся точечно (по абсолютным координатам или с меткой — повторный
/// запуск ничего не сдвигает дважды).
/// </summary>
public static partial class BankVisualOverhaul
{
    public const float BasementY = -3f;
    const float ServiceCeil = 3.4f;

    static void BuildServiceWingAndBasement(Transform root, Transform st, Transform map, UnityEngine.SceneManagement.Scene scene)
    {
        var sw = Group(st, "ServiceWing");
        var bm = Group(st, "Basement");
        MoveVaultDown(map);
        MoveDiamondShowcase(map);
        RebuildHallNorthWall(sw, map);
        BuildHallMiami(sw, root, map);
        var doors = BuildServiceDoors(root);
        BuildRoomDoors(root);
        BuildServiceRooms(sw, root);
        BuildBasement(bm, root, map);
        BuildBasementVaultDoor(root, map);
        BuildLaserControl(root, map);
        BuildStaffLoot(root, sw);
        log.AppendLine($"  service wing + basement: {doors} service doors, stairs, vent shaft, basement vault");
    }

    // =========================================================================== помощники
    /// Вертикальная стена с проёмами и коллайдером (как Panel, но толстая и твёрдая).
    static void WallC(Transform p, string name, bool axisX, float c, float u0, float u1, float y0, float y1, float thick, Material mat,
                      params (float u0, float u1, float y0, float y1)[] holes)
    {
        void Piece(float a0, float a1, float b0, float b1)
        {
            if (a1 - a0 < 0.01f || b1 - b0 < 0.01f) return;
            var pos = axisX ? new Vector3(c, (b0 + b1) / 2, (a0 + a1) / 2) : new Vector3((a0 + a1) / 2, (b0 + b1) / 2, c);
            var size = axisX ? new Vector3(thick, b1 - b0, a1 - a0) : new Vector3(a1 - a0, b1 - b0, thick);
            Box(p, name, pos, size, mat, true);
        }
        float cur = u0;
        foreach (var h in holes.OrderBy(h => h.u0))
        {
            Piece(cur, h.u0, y0, y1);
            Piece(h.u0, h.u1, y0, Mathf.Max(y0, h.y0));
            Piece(h.u0, h.u1, Mathf.Min(y1, h.y1), y1);
            cur = h.u1;
        }
        Piece(cur, u1, y0, y1);
    }

    /// Горизонтальная плита [x0,x1]×[z0,z1] с верхом на yTop.
    static GameObject Slab(Transform p, string name, float x0, float x1, float z0, float z1, float yTop, float thick, Material mat, bool collider = true)
    {
        if (x1 - x0 < 0.01f || z1 - z0 < 0.01f) return null;
        return Box(p, name, new Vector3((x0 + x1) / 2, yTop - thick / 2, (z0 + z1) / 2), new Vector3(x1 - x0, thick, z1 - z0), mat, collider);
    }

    static void Sign(Transform p, string name, string text, Vector3 pos, Vector3 facing, float size, float w, Material neon = null)
    {
        Box(p, name + "_Plate", pos - facing * 0.01f, new Vector3(w + 0.1f, size * 0.12f + 0.08f, 0.02f), windowDark, false, Quaternion.LookRotation(-facing));
        var t =Label(p, name, text, size, Color.white, pos + facing * 0.005f, facing, new Vector2(w, size * 0.12f + 0.05f));
        if (neon) t.fontSharedMaterial = neon;
    }

    /// Физическая кнопка на стене (как у билдера карты): пластина, корпус, колпачок, триггер, звук, PushButton + подпись.
    static PushButton MakeButton(Transform p, string name, Vector3 pos, Vector3 facing, string label, Color color, UnityAction action)
    {
        var plate = Group(p, name);
        plate.SetPositionAndRotation(pos, Quaternion.LookRotation(Vector3.down, facing));   // локальная Y = нормаль стены
        Box(plate, "Plate", plate.TransformPoint(new Vector3(0, -0.01f, 0)), new Vector3(0.45f, 0.02f, 0.55f), chromeMat, false, plate.rotation);
        var btn = Group(plate, "Button_" + label);
        btn.localPosition = Vector3.zero; btn.localRotation = Quaternion.identity;
        btn.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        Cyl(btn, "Housing", plate.TransformPoint(new Vector3(0, 0.01f, 0)), 0.13f, 0.02f, rubber, false, plate.rotation);
        var capMat = Emissive("Btn_" + ColorUtility.ToHtmlStringRGB(color), color, 1.5f);
        var cap = Cyl(btn, "Cap", plate.TransformPoint(new Vector3(0, 0.04f, 0)), 0.1f, 0.04f, capMat, false, plate.rotation);
        var trig = btn.gameObject.AddComponent<BoxCollider>();
        trig.isTrigger = true; trig.center = new Vector3(0, 0.06f, 0); trig.size = new Vector3(0.23f, 0.1f, 0.23f);
        var src = btn.gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 1f;
        var pb = btn.gameObject.AddComponent<PushButton>();
        pb.label = label; pb.cap = cap.transform; pb.clickClips = MapKit.Clips(UiDir, "click");
        UnityEventTools.AddVoidPersistentListener(pb.onPressed, action);
        var t = Label(plate, "Label_" + label, label, 0.9f, Color.white, pos + facing * 0.025f + Vector3.up * 0.22f, facing, new Vector2(0.5f, 0.15f));
        t.transform.SetParent(plate, true);
        return pb;
    }

    // =========================================================================== перенос старого хранилища в подвал
    static readonly string[] VaultStaticToBasement = { "DepositBox", "Keyhole", "SafePedestal", "SafePedestalTrim", "ShelfUpright", "ShelfBoard" };
    static readonly string[] VaultStaticHide = { "Floor", "Wall_E", "LaserEmitter", "LaserRail", "CeilingStrip" };

    static void MoveVaultDown(Transform map)
    {
        var z5 = map.Find("Z5_Vault");
        if (!z5) { log.AppendLine("ERROR: Z5_Vault not found"); return; }
        var stv = z5.Find("Static"); var dy = z5.Find("Dynamic");
        if (!z5.Find("MovedToBasement"))
        {
            foreach (Transform t in stv) if (VaultStaticToBasement.Contains(t.name)) t.position += Vector3.down * 3f;
            foreach (Transform t in dy) t.position += Vector3.down * 3f;
            new GameObject("MovedToBasement").transform.SetParent(z5, false);
            log.AppendLine("  vault contents moved 3 m down into the basement");
        }
        // куски стен билдер называет Wall_E_0, Wall_E_1… — прячем по префиксу
        foreach (Transform t in stv) if (VaultStaticHide.Any(n => t.name == n || t.name.StartsWith(n + "_"))) HideQuiet(t);

        // абсолютные места в подвале (повторный запуск ставит туда же)
        void At(string n, Vector3 p) { var t = dy.Find(n); if (t) { t.position = p; EditorUtility.SetDirty(t); } }
        At("LaserGrid_1_UpDown", new Vector3(-2, BasementY, 32.6f));
        At("LaserGrid_2_Sweep", new Vector3(-2, BasementY, 34.1f));
        At("LaserGrid_3_Wave", new Vector3(-2, BasementY, 35.7f));
        var sweep = dy.Find("LaserGrid_2_Sweep");
        if (sweep) foreach (Transform b in sweep)                                 // вертикальные лучи — под потолок подвала (2.8 м)
            {
                b.localScale = new Vector3(b.localScale.x, 2.6f, b.localScale.z);
                b.localPosition = new Vector3(b.localPosition.x, 1.35f, b.localPosition.z);
                EditorUtility.SetDirty(b);
            }
        At("GatePlate_Vault", new Vector3(-2.6f, BasementY + 1.3f, 31.21f));
        At("AlarmBeacon", new Vector3(-2f, -0.3f, 35f));
        var cam = dy.GetComponentInChildren<SecurityCamera>(true);
        if (cam)
        {
            var pos = new Vector3(9.5f, -0.6f, 39.4f); var target = new Vector3(-2f, BasementY, 33f);
            var dir = target - pos;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z).normalized));
            if (cam.pivot) cam.pivot.localRotation = Quaternion.LookRotation(Quaternion.Inverse(cam.transform.rotation) * dir.normalized);
            // конус обзора рисуется сквозь стены — у камеры в подвале он просвечивал бы через пол в кабинет директора
            if (cam.cone) { cam.cone.enabled = false; EditorUtility.SetDirty(cam.cone); }
            EditorUtility.SetDirty(cam.transform);
        }
        var tp = z5.Find("TeleportPoint_Vault");
        if (tp) { tp.position = new Vector3(-7f, BasementY, 27f); EditorUtility.SetDirty(tp); }
        foreach (var l in z5.GetComponentsInChildren<Light>(true))
        {
            if (l.name == "VaultLight") { l.transform.position = new Vector3(-2f, -0.6f, 35.5f); l.range = 10f; }
            else if (l.name == "VaultLightBack") { l.transform.position = new Vector3(-4f, -0.6f, 27.8f); l.range = 10f; }
            else continue;
            EditorUtility.SetDirty(l.transform); EditorUtility.SetDirty(l);
        }
    }

    /// Витрина с бриллиантом ($1000) из центра галереи — в подземное хранилище (со стёклами, лутом и стойками ограждения).
    static void MoveDiamondShowcase(Transform map)
    {
        var z3 = map.Find("Z3_Gallery");
        if (!z3) return;
        var from = new Vector3(24f, 0, 12f);
        var to = new Vector3(-7.5f, BasementY, 37f);
        int n = 0;
        foreach (var part in new[] { z3.Find("Static"), z3.Find("Dynamic") })
        {
            if (!part) continue;
            foreach (Transform t in part.Cast<Transform>().ToArray())
            {
                var p = t.position;
                if (Mathf.Abs(p.x - from.x) > 2.45f || Mathf.Abs(p.z - from.z) > 2.45f || p.y > 3f) continue;
                var r = t.GetComponent<Renderer>();
                if (r && (r.bounds.size.x > 3.5f || r.bounds.size.z > 3.5f)) continue;   // полоски паркета и пол — не трогаем
                t.position += to - from;
                EditorUtility.SetDirty(t);
                n++;
            }
        }
        if (n > 0) log.AppendLine($"  diamond showcase moved to basement vault ({n} parts)");
    }

    // =========================================================================== северная стена Холла и Холл в стиле Майами
    static void RebuildHallNorthWall(Transform p, Transform map)
    {
        // старая стена: куски Wall_N_0, Wall_N_1… (по точному имени их не найти — иначе она закрывает служебные двери)
        foreach (var w in map.Find("Z2_MainHall").Find("Static").Cast<Transform>().Where(t => t.name == "Wall_N" || t.name.StartsWith("Wall_N_")).ToArray()) HideQuiet(w);
        FixOldTexts(map);
        // старые надписи билдера у заделанного проёма хранилища (решётка, «ХРАНИЛИЩЕ») — убираем
        foreach (var t in map.Find("Z2_MainHall").GetComponentsInChildren<TMPro.TextMeshPro>(true))
            if (t.transform.position.z > 23.4f && t.transform.position.y < 5f && Mathf.Abs(t.transform.position.x) < 3f && !t.GetComponentInParent<PushButton>() && !t.transform.parent.name.StartsWith("GatePlate"))
                HideQuiet(t.transform);
        // стена от подвала до потолка: две служебные двери, центральный проём старого хранилища заделан
        WallC(p, "HallWall_N", false, 24f, -14f, 14f, BasementY, 10f, 0.3f, stuccoWhite, (-9.6f, -7.8f, 0f, 2.6f), (5.0f, 6.8f, 0f, 2.6f));
    }

    /// Надписи старого билдера, которые не подходят новому банку: старое название, «пульт», «фургон», табличка заделанного хранилища.
    static void FixOldTexts(Transform map)
    {
        var sv = map.Find("Z2_MainHall/Static/Sign_Vault");
        if (sv) HideQuiet(sv);
        foreach (var t in map.root.GetComponentsInChildren<TMPro.TextMeshPro>(true))
        {
            string s = t.text, n = s;
            if (s.Contains("ЗОЛОТОЙ БАНАН")) { n = "SUNSET TRUST BANK"; t.fontSharedMaterial = neonPink; t.color = Color.white; }
            else if (s.Contains("ГАРАЖ · ФУРГОН")) n = s.Replace("ФУРГОН", "ЭВАКУАЦИЯ");
            else if (s.Contains("ВЕНТИЛЯЦИЯ → ?")) n = "ВЕНТИЛЯЦИЯ →";
            else if (s.Contains("НА ПУЛЬТЕ")) n = s.Replace("ЖМИ «СТАРТ» НА ПУЛЬТЕ", "ВЫЙДИ ИЗ ФУРГОНА — СТАРТ!");
            if (n != s) { t.text = n; EditorUtility.SetDirty(t); }
        }
    }

    static void BuildHallMiami(Transform p, Transform root, Transform map)
    {
        var z2 = map.Find("Z2_MainHall");
        // материалы зала: мрамор с золотом, белые стены, бирюзовые цоколи, белые колонны с золотом, розоватый потолок
        foreach (var r in z2.Find("Static").GetComponentsInChildren<MeshRenderer>(true))
        {
            var src = r.sharedMaterial; if (!src) continue;
            var mf = r.GetComponent<MeshFilter>();
            Material dst = null;
            string n = r.name, m = src.name;
            if (n == "Ceiling") dst = stuccoPink;
            else if (n == "ColumnShaft") dst = stuccoWhite;
            else if (n is "ColumnBase" or "ColumnCapital") dst = brass;
            else if ((n is "Floor" or "BorderInner") && m.Contains("Marble")) dst = marbleGold;
            else if (m == "M_Bank_Limestone" || m == "M_Bank_StuccoWhite") dst = stuccoWhite;
            else if (m == "M_Bank_MarbleDark" || m == "M_Bank_MarbleTeal") dst = marbleTeal;
            if (!dst || !mf || !mf.sharedMesh) continue;
            r.sharedMaterial = dst;
            string kind = MeshKind(mf.sharedMesh);
            if (kind != null && tileOf.TryGetValue(dst, out var tile))
                mf.sharedMesh = kind == "box" ? BoxMesh(r.transform.lossyScale, tile) : CylMesh(r.transform.lossyScale, tile);
            EditorUtility.SetDirty(r);
        }

        // бирюзовый мраморный цоколь с золотой полосой по стенам зала
        const float T = 0.04f;
        Panel(p, "Wainscot_S", false, 0.17f, -14, 14, 0, 1.0f, T, marbleTeal, (-1.5f, 1.5f, 0f, 3f));
        Panel(p, "Wainscot_W", true, -13.83f, 0, 24, 0, 1.0f, T, marbleTeal, (4f, 6.5f, 0f, 2.8f));
        Panel(p, "Wainscot_E", true, 13.83f, 0, 24, 0, 1.0f, T, marbleTeal, (8f, 12f, 0f, 3.5f));
        Panel(p, "Wainscot_N", false, 23.83f, -14, 14, 0, 1.0f, T, marbleTeal, (-9.6f, -7.8f, 0f, 2.6f), (5.0f, 6.8f, 0f, 2.6f));
        Panel(p, "GoldLine_S", false, 0.18f, -14, 14, 1.0f, 1.06f, T, brass, (-1.5f, 1.5f, 0f, 3f));
        Panel(p, "GoldLine_N", false, 23.82f, -14, 14, 1.0f, 1.06f, T, brass, (-9.6f, -7.8f, 0f, 2.6f), (5.0f, 6.8f, 0f, 2.6f));

        // панорамные окна (изнутри — закат Майами) и тёплые лучи света в зал
        var sunGroup = Group(root, "SunsetLight");
        foreach (float x in new[] { -9.4f, -5.3f, 5.3f, 9.4f })
        {
            Box(p, "PanoramaGlass", new Vector3(x, 6.0f, 0.17f), new Vector3(1.8f, 4.0f, 0.02f), sunsetGlassUnlit);
            Box(p, "PanoramaFrame_T", new Vector3(x, 8.06f, 0.2f), new Vector3(2.0f, 0.12f, 0.08f), brass);
            Box(p, "PanoramaFrame_B", new Vector3(x, 3.94f, 0.2f), new Vector3(2.0f, 0.12f, 0.08f), brass);
            Box(p, "PanoramaMullion", new Vector3(x, 6.0f, 0.19f), new Vector3(0.06f, 4.0f, 0.04f), brass);
            var a = new Vector3(x, 6.0f, 0.3f); var dir = new Vector3(x * 0.1f, -0.55f, 1f).normalized;
            var shaft = Box(p, "LightShaft", a + dir * 4.5f, new Vector3(1.7f, 3.6f, 9f), lightShaft, false, Quaternion.LookRotation(dir));
            shaft.GetComponent<MeshRenderer>().sharedMaterial = lightShaft;
        }
        foreach (float x in new[] { -7.3f, 7.3f })
        {
            var go = new GameObject("SunsetBeam");
            go.transform.SetParent(sunGroup, false);
            go.transform.SetPositionAndRotation(new Vector3(x, 7.6f, 0.7f), Quaternion.LookRotation(new Vector3(-x * 0.25f, -7f, 10f)));
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot; l.color = new Color(1f, 0.62f, 0.5f); l.intensity = 4.5f; l.range = 18f;
            l.spotAngle = 65f; l.innerSpotAngle = 35f; l.shadows = LightShadows.None;
        }

        // длинные кассовые стойки (собраны из примитивов) с логотипом на фасаде, окошками к клиентам (−Z)
        var tellers = Group(p, "TellerDesks");
        foreach (float x in new[] { -9.8f, -5.2f, 5.2f, 9.8f }) TellerDesk(tellers, new Vector3(x, 0, 17.2f), 0f, 4.4f);
        // рабочая зона кассиров: задние стойки, кресла и точки появления NPC
        var work = Group(p, "TellerWorkZone");
        foreach (var (x0, x1) in new[] { (-12.8f, -10.2f), (-7.2f, -1.0f), (1.0f, 4.6f), (7.4f, 12.8f) })
        {
            Box(work, "BackCounter", new Vector3((x0 + x1) / 2, 0.45f, 20.5f), new Vector3(x1 - x0, 0.9f, 0.6f), woodPanel, true);
            Box(work, "BackCounterTop", new Vector3((x0 + x1) / 2, 0.925f, 20.5f), new Vector3(x1 - x0 + 0.06f, 0.05f, 0.66f), marbleGold, true);
            for (float x = x0 + 0.5f; x < x1 - 0.3f; x += 1.3f)
                Box(work, "PaperStack", new Vector3(x, 0.99f, 20.5f), new Vector3(0.3f, 0.08f, 0.22f), paper);
        }
        var spawns = Group(root, "CashierSpawnPoints");
        int i = 1;
        foreach (float x in new[] { -10.2f, -3.9f, 3.9f, 10.2f })
        {
            OfficeChair(work, new Vector3(x, 0, 18.9f), 0f);                     // кресло лицом к окошку
            var sp = new GameObject($"CashierSpawnPoint_{i++}").transform;          // пустой маркер под будущую NPC-кассиршу
            sp.SetParent(spawns, false);
            sp.SetPositionAndRotation(new Vector3(x, 0, 18.9f), Quaternion.Euler(0, 180, 0));
        }

        // логотип на стене за кассами (между служебными дверями)
        Logo(p, new Vector3(-1.4f, 3.45f, 23.8f), 0.55f);
        var wt = Label(p, "WallLogo", "SUNSET TRUST BANK", 3.2f, Color.white, new Vector3(-1.4f, 2.35f, 23.82f), Vector3.back, new Vector2(6.0f, 0.45f));
        wt.fontSharedMaterial = neonPink;
    }

    // =========================================================================== служебные двери (сеть)
    static int BuildServiceDoors(Transform root)
    {
        int n = 0;
        foreach (var (x0, x1, title, hack) in new[] { (-9.6f, -7.8f, "СЛУЖЕБНЫЙ ВХОД А", 0.5f), (5.0f, 6.8f, "СЛУЖЕБНЫЙ ВХОД Б", 4f) })
        {
            var dr = new GameObject("ServiceDoor_" + (n == 0 ? "A" : "B") + "_Net").transform;
            dr.SetParent(root, false);
            dr.SetPositionAndRotation(new Vector3(x0, 0, 24f), Quaternion.identity);
            var hinge = Group(dr, "Hinge");
            float w = x1 - x0 - 0.04f;
            Box(hinge, "Leaf", new Vector3(x0 + 0.02f + w / 2, 1.3f, 24f), new Vector3(w, 2.58f, 0.08f), stuccoTeal, true);
            Box(hinge, "LeafStripe", new Vector3(x0 + 0.02f + w / 2, 1.3f, 23.95f), new Vector3(w, 0.08f, 0.02f), brass);
            Box(hinge, "LeafWindow", new Vector3(x0 + 0.02f + w / 2, 1.85f, 23.95f), new Vector3(0.5f, 0.5f, 0.02f), sunsetGlass);
            Box(hinge, "Handle", new Vector3(x1 - 0.25f, 1.05f, 23.93f), new Vector3(0.05f, 0.3f, 0.05f), brass);
            var status = Label(dr, "DoorStatus", title, 1.0f, new Color(1f, 0.9f, 0.6f), new Vector3((x0 + x1) / 2, 3.15f, 23.82f), Vector3.back, new Vector2(2.2f, 0.25f));
            var lamp = Ball(dr, "DoorLamp", new Vector3(x1 + 0.25f, 2.85f, 23.8f), Vector3.one * 0.12f, stripRed);
            var src = dr.gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 1f; src.maxDistance = 20f;
            SetMasterFlags(dr.gameObject.AddComponent<NetworkObject>());
            var door = dr.gameObject.AddComponent<NetworkVaultDoor>();
            door.hinge = hinge; door.openAngle = -100f; door.hackSeconds = hack; door.swingSpeed = 140f;
            door.openText = title + " ОТКРЫТ"; door.closedText = title;
            door.statusText = status; door.statusLamp = lamp.GetComponent<Renderer>(); door.audioSource = src;
            door.openClips = MapKit.Clips(UiDir, "open", "maximize"); door.closeClips = MapKit.Clips(UiDir, "close", "minimize"); door.hackClips = MapKit.Clips(UiDir, "click");
            // со стороны зала — «ВЗЛОМ» (у двери А взлом мгновенный — просто открыть), из коридора — «ДВЕРЬ»
            MakeButton(dr, "Panel_Hall", new Vector3(x1 + 0.5f, 1.3f, 23.84f), Vector3.back, hack > 1f ? "ВЗЛОМ" : "ОТКРЫТЬ", new Color(0.2f, 0.9f, 0.8f), door.PressHack);
            MakeButton(dr, "Panel_Corridor", new Vector3(x0 - 0.5f, 1.3f, 24.16f), Vector3.forward, "ДВЕРЬ", new Color(0.2f, 0.9f, 0.3f), door.PressToggle);
            foreach (var t in dr.GetComponentsInChildren<Transform>(true)) if (t.gameObject.layer != LayerMask.NameToLayer("Ignore Raycast")) t.gameObject.layer = 0;
            n++;
        }
        return n;
    }

    // =========================================================================== служебное крыло 1-го этажа
    static void BuildServiceRooms(Transform p, Transform root)
    {
        var g = Group(p, "Rooms");
        // пол (с лестничным проёмом и люком вентиляции) и подвесной потолок (с люком под старым воздуховодом)
        Slab(g, "Floor", -14f, -5.2f, 24f, 40f, 0f, 0.2f, concrete);
        Slab(g, "Floor", -5.2f, -4.0f, 24f, 37.6f, 0f, 0.2f, concrete);
        Slab(g, "Floor", -5.2f, -4.0f, 38.8f, 40f, 0f, 0.2f, concrete);
        Slab(g, "Floor", -4.0f, -3.5f, 24f, 40f, 0f, 0.2f, concrete);
        Slab(g, "Floor", -3.5f, 2.5f, 27.6f, 40f, 0f, 0.2f, concrete);
        Slab(g, "Floor", 2.5f, 10f, 24f, 40f, 0f, 0.2f, concrete);
        Slab(g, "BreakRoomTiles", -14f, -6f, 28f, 40f, 0.01f, 0.01f, breakTile, false);
        Slab(g, "DirectorCarpet", 1f, 10f, 28f, 40f, 0.01f, 0.01f, carpet, false);
        Slab(g, "Ceiling", -14f, -5.4f, 24f, 40f, ServiceCeil + 0.1f, 0.1f, stuccoWhite);
        Slab(g, "Ceiling", -5.4f, -3.8f, 24f, 31.4f, ServiceCeil + 0.1f, 0.1f, stuccoWhite);
        Slab(g, "Ceiling", -5.4f, -3.8f, 32.6f, 40f, ServiceCeil + 0.1f, 0.1f, stuccoWhite);
        Slab(g, "Ceiling", -3.8f, 10f, 24f, 40f, ServiceCeil + 0.1f, 0.1f, stuccoWhite);
        Box(g, "HatchFrame", new Vector3(-4.6f, ServiceCeil - 0.01f, 32f), new Vector3(1.8f, 0.03f, 1.4f), paint);

        // перегородки: коридор | комнаты (z = 28), отдых | серверная (x = −6), серверная | директор (x = 1)
        WallC(g, "Partition_Corridor", false, 28f, -14f, 10f, 0f, ServiceCeil, 0.15f, serviceWall, (-11.5f, -9.9f, 0f, 2.4f), (-5.7f, -4.3f, 0f, 2.4f), (4.0f, 5.6f, 0f, 2.4f));
        WallC(g, "Partition_BreakServer", true, -6f, 28f, 40f, 0f, ServiceCeil, 0.15f, serviceWall);
        WallC(g, "Partition_ServerDirector", true, 1f, 28f, 40f, 0f, ServiceCeil, 0.15f, serviceWall);
        Sign(g, "Sign_Break", "КОМНАТА ОТДЫХА", new Vector3(-10.7f, 2.7f, 27.91f), Vector3.back, 1.4f, 1.9f);
        Sign(g, "Sign_Server", "СЕРВЕРНАЯ", new Vector3(-5.0f, 2.7f, 27.91f), Vector3.back, 1.4f, 1.4f);
        Sign(g, "Sign_Director", "ДИРЕКТОР", new Vector3(4.8f, 2.7f, 27.91f), Vector3.back, 1.4f, 1.4f);
        Sign(g, "Sign_Basement", "↓ ПОДВАЛ · ХРАНИЛИЩЕ", new Vector3(-0.5f, 2.6f, 24.17f), Vector3.forward, 1.6f, 3.0f, neonCyan);
        foreach (float x in new[] { -12.6f, 8.6f }) Box(g, "CorridorStripe", new Vector3(x, 0.006f, 26f), new Vector3(0.12f, 0.01f, 3.6f), paint);

        var lights = Group(root, "ServiceLights");
        void PL(string n, Vector3 pos, Color c, float i, float r)
        {
            var go = new GameObject(n); go.transform.SetParent(lights, false); go.transform.position = pos;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = i; l.range = r; l.shadows = LightShadows.None;
            Box(g, n + "_Panel", pos + Vector3.up * 0.28f, new Vector3(1.2f, 0.04f, 0.4f), lampBulb);
        }
        PL("CorridorLight_A", new Vector3(-9f, ServiceCeil - 0.3f, 26f), new Color(1f, 0.92f, 0.85f), 1.8f, 8f);
        PL("CorridorLight_B", new Vector3(6.5f, ServiceCeil - 0.3f, 26f), new Color(1f, 0.92f, 0.85f), 1.8f, 8f);
        PL("BreakRoomLight", new Vector3(-10f, ServiceCeil - 0.3f, 34f), new Color(1f, 0.85f, 0.65f), 2.0f, 9f);
        PL("ServerRoomLight", new Vector3(-2.5f, ServiceCeil - 0.3f, 34f), new Color(0.5f, 0.9f, 1f), 1.6f, 8f);
        PL("DirectorLight", new Vector3(5.5f, ServiceCeil - 0.3f, 34f), new Color(1f, 0.82f, 0.6f), 1.9f, 9f);

        // комната отдыха (мебель из примитивов)
        var br = Group(g, "BreakRoom");
        Sofa(br, new Vector3(-13.42f, 0, 34f), -90f, 2.2f);                        // спинкой к западной стене
        Kitchenette(br, new Vector3(-10f, 0, 39.52f), 0f, 2.2f);                   // у северной стены
        Lockers(br, new Vector3(-6.35f, 0, 33f), 90f, 4);                          // у перегородки с серверной
        WaterCooler(br, new Vector3(-13.6f, 0, 29.3f), -90f);
        RoundTable(br, new Vector3(-10f, 0, 33.5f));

        // серверная + люк вентиляции в подвал
        var sv = Group(g, "ServerRoom");
        foreach (float zz in new[] { 29.9f, 30.55f, 31.2f, 36.1f, 36.75f, 37.4f }) ServerRack(sv, new Vector3(0.5f, 0, zz), 90f);
        foreach (var (pos, size) in new[]
        {
            (new Vector3(-4.6f, 0.45f, 37.57f), new Vector3(1.26f, 0.9f, 0.06f)), (new Vector3(-4.6f, 0.45f, 38.83f), new Vector3(1.26f, 0.9f, 0.06f)),
            (new Vector3(-5.23f, 0.45f, 38.2f), new Vector3(0.06f, 0.9f, 1.32f)), (new Vector3(-3.97f, 0.45f, 38.2f), new Vector3(0.06f, 0.9f, 1.32f)),
        })
            Box(sv, "VentHousing", pos, size, armorLight, true);
        Box(sv, "VentGrille", new Vector3(-4.6f, 0.92f, 37.57f), new Vector3(1.3f, 0.06f, 0.1f), paint);
        Sign(sv, "Sign_Vent", "ВЕНТИЛЯЦИЯ ↓", new Vector3(-4.6f, 1.3f, 37.5f), Vector3.back, 1.0f, 1.2f, neonCyan);

        // кабинет директора: стол, кресло, пульт видеонаблюдения, сейф за картиной
        var dr = Group(g, "DirectorOffice");
        DirectorDesk(dr, new Vector3(5.5f, 0, 35.3f), 0f);                         // лицом к двери (−Z)
        OfficeChair(dr, new Vector3(5.5f, 0, 36.3f), 0f);
        SecurityConsole(dr, new Vector3(9.49f, 0, 31.8f), 90f);                    // у восточной стены
        Box(dr, "WallSafe", new Vector3(5.5f, 1.55f, 39.65f), new Vector3(0.8f, 0.8f, 0.4f), armor, true);
        Box(dr, "WallSafeInside", new Vector3(5.5f, 1.55f, 39.44f), new Vector3(0.62f, 0.62f, 0.02f), windowDark);
        Box(dr, "WallSafeShelf", new Vector3(5.5f, 1.2f, 39.5f), new Vector3(0.62f, 0.04f, 0.3f), armor, true);
        Box(dr, "WallSafeDoor", new Vector3(5.0f, 1.55f, 39.2f), new Vector3(0.05f, 0.7f, 0.7f), armor, true, Quaternion.Euler(0, -20, 0));
        var frame = Group(dr, "SafePainting");                                     // картина отодвинута — за ней сейф
        frame.SetPositionAndRotation(new Vector3(6.1f, 1.55f, 39.6f), Quaternion.Euler(0, -75, 0));
        Box(frame, "Frame", frame.TransformPoint(new Vector3(0.55f, 0, 0)), new Vector3(1.1f, 0.9f, 0.05f), brass, false, frame.rotation);
        var canvas = Box(frame, "Canvas", frame.TransformPoint(new Vector3(0.55f, 0, -0.03f)), new Vector3(0.95f, 0.75f, 0.01f), sunsetGlass, false, frame.rotation);
        var art = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Map/M_Art2.mat");
        if (art) canvas.GetComponent<MeshRenderer>().sharedMaterial = art;
    }

    // =========================================================================== подвал
    static void BuildBasement(Transform p, Transform root, Transform map)
    {
        const float yb = BasementY, ceil = -0.2f;
        Slab(p, "Floor_Antechamber", -14f, 10f, 24.15f, 30.8f, yb, 0.2f, concrete);
        Slab(p, "Floor_Vault", -14f, 10f, 30.8f, 40f, yb, 0.2f, tread);
        Slab(p, "VaultCeiling", -14f, 10f, 31.2f, 40f, ceil - 0.01f, 0.02f, armor, false);
        WallC(p, "Wall_W", true, -14.0f, 24f, 31f, yb, ceil, 0.3f, concrete);
        WallC(p, "Wall_W", true, -14.0f, 31f, 40f, yb, ceil, 0.3f, armor);
        WallC(p, "Wall_N", false, 40.0f, -14f, 10f, yb, ceil, 0.3f, armor);
        // восточная стена от подвала до потолка служебного крыла, проём в гараж
        WallC(p, "Wall_E", true, 10.0f, 24f, 40f, yb, 6f, 0.3f, concrete, (25f, 28.2f, yb, -0.8f));
        // стена хранилища с круглым проёмом под бронедверь
        WallC(p, "VaultWall", false, 31.0f, -14f, 10f, yb, ceil, 0.4f, armor, (-1.45f, 1.45f, yb, -0.55f));
        Sign(p, "Sign_Garage", "→ ГАРАЖ · ЭВАКУАЦИЯ", new Vector3(7.5f, -1.2f, 30.78f), Vector3.back, 1.2f, 2.4f, neonCyan);

        // лестница: 15 ступеней по 0.2 м вдоль +X (от x = −3.5 у коридора до x = 2.5 в подвале) и пандус рядом
        for (int k = 0; k < 15; k++)
        {
            float x0 = -3.5f + 0.4f * k, top = -0.2f * (k + 1);
            if (top - yb < 0.01f) continue;
            Box(p, "Step", new Vector3(x0 + 0.2f, (top + yb) / 2, 26.425f), new Vector3(0.4f, top - yb, 2.35f), concrete, true);
        }
        float ang = Mathf.Atan2(3f, 6f) * Mathf.Rad2Deg, len = Mathf.Sqrt(36f + 9f);
        Box(p, "Ramp", new Vector3(-0.5f, -1.5f - 0.1f / Mathf.Cos(ang * Mathf.Deg2Rad), 24.675f), new Vector3(len, 0.2f, 1.05f), tread, true, Quaternion.Euler(0, 0, -ang));
        Box(p, "RampStripe", new Vector3(-0.5f, -1.49f, 24.675f), new Vector3(len, 0.01f, 0.1f), paint, false, Quaternion.Euler(0, 0, -ang));
        // перила: поручень между пандусом и ступенями, ограждение проёма на 1-м этаже
        var a = new Vector3(-3.5f, 0.95f, 25.22f); var b = new Vector3(2.5f, yb + 0.95f, 25.22f);
        Box(p, "Handrail", (a + b) / 2, new Vector3(0.06f, 0.06f, (b - a).magnitude), brass, true, Quaternion.LookRotation(b - a));
        for (float x = -3.3f; x < 2.5f; x += 1.2f)
        {
            float y = -3f * (x + 3.5f) / 6f;
            Box(p, "HandrailPost", new Vector3(x, y + 0.47f, 25.22f), new Vector3(0.05f, 0.95f, 0.05f), brass, true);
        }
        for (float x = -3.3f; x <= 2.5f; x += 1f) Box(p, "RailPost", new Vector3(x, 0.5f, 27.66f), new Vector3(0.05f, 1.0f, 0.05f), brass, true);
        Box(p, "Rail_N", new Vector3(-0.5f, 1.0f, 27.66f), new Vector3(6.1f, 0.06f, 0.06f), brass, true);
        for (float z = 24.3f; z < 27.7f; z += 0.85f) Box(p, "RailPost", new Vector3(2.56f, 0.5f, z), new Vector3(0.05f, 1.0f, 0.05f), brass, true);
        Box(p, "Rail_E", new Vector3(2.56f, 1.0f, 25.9f), new Vector3(0.06f, 0.06f, 3.5f), brass, true);

        // в хранилище: решётка перед ячейками, люк вентиляции в потолке, освещение полосами (BuildAccentStrips)
        IronBarsPanel(p, new Vector3(-6.7f, yb, 39.2f), 0f, 6.6f, 2.4f);
        Box(p, "VentHatch_Basement", new Vector3(-4.6f, ceil - 0.02f, 38.2f), new Vector3(1.4f, 0.03f, 1.4f), paint);
    }

    /// Круглая бронедверь главного хранилища в подвале — сетевая (взлом 5 с из предбанника, кнопка изнутри).
    static void BuildBasementVaultDoor(Transform root, Transform map)
    {
        var prefab = Prefab("Bank_VaultDoor");
        if (!prefab) { log.AppendLine("ERROR: no Bank_VaultDoor"); return; }
        HideQuiet(Named(map, "Z2_MainHall", "VaultGate").FirstOrDefault());
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var b0 = WorldBounds(model);
        float s = 2.5f / b0.size.y;
        model.transform.localScale = new Vector3(s, s, s * (0.4f / (b0.size.z * s)));
        model.transform.rotation = Quaternion.Euler(0, 180, 0);
        var b = WorldBounds(model);
        float face = 30.8f, zc = face - b.size.z / 2 - 0.02f;
        model.transform.position += new Vector3(-b.center.x, BasementY - b.min.y, zc - b.center.z);
        b = WorldBounds(model);
        foreach (var bc in model.GetComponentsInChildren<BoxCollider>()) Object.DestroyImmediate(bc);
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;

        var doorRoot = new GameObject("VaultDoor_Net").transform;
        doorRoot.SetParent(root, false);
        doorRoot.SetPositionAndRotation(new Vector3(b.min.x, BasementY, zc), Quaternion.identity);
        var hinge = Group(doorRoot, "Hinge");
        model.transform.SetParent(hinge, true);
        foreach (var t in doorRoot.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        var status = Label(doorRoot, "VaultStatus", "ХРАНИЛИЩЕ ЗАКРЫТО", 1.6f, new Color(1f, 0.85f, 0.4f), new Vector3(3.2f, -1.0f, 30.77f), Vector3.back, new Vector2(2.6f, 0.3f));
        var lamp = Ball(doorRoot, "VaultLamp", new Vector3(3.2f, -0.6f, 30.76f), Vector3.one * 0.18f, stripRed);
        var src = doorRoot.gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 1f; src.maxDistance = 30f;
        SetMasterFlags(doorRoot.gameObject.AddComponent<NetworkObject>());
        var door = doorRoot.gameObject.AddComponent<NetworkVaultDoor>();
        door.hinge = hinge; door.openAngle = 105f; door.hackSeconds = 5f;
        door.statusText = status; door.statusLamp = lamp.GetComponent<Renderer>(); door.audioSource = src;
        door.openClips = MapKit.Clips(UiDir, "open", "maximize"); door.closeClips = MapKit.Clips(ImpactDir, "impact_metal_heavy"); door.hackClips = MapKit.Clips(UiDir, "click");

        // кнопки бывшей решётки: снаружи (предбанник) — «ВЗЛОМ», внутри хранилища — «ДВЕРЬ»
        var hallPlate = map.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "GatePlate_Hall");
        if (hallPlate) { hallPlate.position = new Vector3(-2.4f, BasementY + 1.3f, 30.79f); EditorUtility.SetDirty(hallPlate); }
        Rebind(map, "GatePlate_Hall", door.PressHack, "ВЗЛОМ");
        Rebind(map, "GatePlate_Vault", door.PressToggle, "ДВЕРЬ");
        log.AppendLine($"  basement vault door: width {b.size.x:0.00}, z {zc:0.00}");
    }

    // =========================================================================== щиток лазеров (сеть)
    static void BuildLaserControl(Transform root, Transform map)
    {
        var z5 = map.Find("Z5_Vault");
        var grids = z5 ? z5.GetComponentsInChildren<LaserAlarm>(true).Select(l => l.gameObject).ToArray() : new GameObject[0];
        var lc = new GameObject("LaserControl_Net").transform;
        lc.SetParent(root, false);
        lc.position = new Vector3(1.09f, 1.3f, 31f);
        Box(lc, "Panel", new Vector3(1.1f, 1.45f, 31f), new Vector3(0.04f, 1.1f, 0.9f), armorLight, true);
        Box(lc, "PanelStripe", new Vector3(1.13f, 1.98f, 31f), new Vector3(0.02f, 0.06f, 0.9f), paint);
        var status = Label(lc, "LaserStatus", "ЛАЗЕРЫ: ВКЛ", 1.2f, new Color(1f, 0.4f, 0.4f), new Vector3(1.14f, 1.85f, 31f), Vector3.right, new Vector2(0.85f, 0.2f));
        var lamp = Ball(lc, "LaserLamp", new Vector3(1.15f, 1.65f, 30.7f), Vector3.one * 0.1f, stripRed);
        var src = lc.gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 1f;
        SetMasterFlags(lc.gameObject.AddComponent<NetworkObject>());
        var ctl = lc.gameObject.AddComponent<NetworkLaserControl>();
        ctl.grids = grids; ctl.offSeconds = 20f; ctl.statusText = status; ctl.statusLamp = lamp.GetComponent<Renderer>(); ctl.audioSource = src;
        ctl.offClips = MapKit.Clips(UiDir, "close", "minimize"); ctl.onClips = MapKit.Clips(UiDir, "open", "maximize");
        MakeButton(lc, "LaserButton", new Vector3(1.13f, 1.3f, 31.15f), Vector3.right, "ЛАЗЕРЫ", new Color(1f, 0.25f, 0.2f), ctl.PressDisable);
        log.AppendLine($"  laser control panel: {grids.Length} grid(s)");
    }

    // =========================================================================== мелкий лут служебного крыла
    static GameObject SimpleLoot(string name, string id, string itemName, int value, float mass, Vector3 size, Material body, Material detail, Color glow)
    {
        string path = $"{Gen}/{name}.prefab";
        var root = new GameObject(name) { tag = "Loot" };
        var t = root.transform;
        Box(t, "Body", new Vector3(0, size.y / 2, 0), size, body);
        Box(t, "Band", new Vector3(0, size.y / 2, 0), new Vector3(size.x * 0.25f, size.y * 1.02f, size.z * 1.02f), detail);
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var col = root.AddComponent<BoxCollider>(); col.center = new Vector3(0, size.y / 2, 0); col.size = size;
        var rb = root.AddComponent<Rigidbody>(); rb.mass = mass; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var li = root.AddComponent<LootItem>();
        li.lootId = id; li.itemName = itemName; li.value = value; li.spinUntilGrabbed = true; li.glowColor = glow;
        var no = root.AddComponent<NetworkObject>();
        var f = no.Flags.SetCurrentVersion();
        f &= ~NetworkObjectFlags.MasterClientObject; f |= NetworkObjectFlags.AllowStateAuthorityOverride; f &= ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
        no.Flags = f;
        var nt = root.AddComponent<NetworkTransform>();
        var ps = nt.PhysicsSettings; ps.SetForecastDisabledProxyRigidbodiesToKinematic = true; nt.PhysicsSettings = ps;
        nt.ConfigFlags |= NetworkTransform.NetworkTransformFlags.DisableSharedModeInterpolation;
        root.AddComponent<NetworkLootItem>();
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void BuildStaffLoot(Transform root, Transform sw)
    {
        var cashMat = Emissive("CashGreen", new Color(0.35f, 0.62f, 0.32f), 0f);
        var keycard = SimpleLoot("Loot_KeyCard", "KeyCard", "Ключ-карта директора", 100, 0.2f, new Vector3(0.17f, 0.012f, 0.11f), stuccoWhite, neonCyanMesh, new Color(0.3f, 0.9f, 1f));
        var cash = SimpleLoot("Loot_CashStack", "CashStack", "Пачка денег", 250, 0.6f, new Vector3(0.2f, 0.09f, 0.1f), cashMat, paintWhite, new Color(0.5f, 1f, 0.5f));
        AssetDatabase.SaveAssets();
        EditorApplication.ExecuteMenuItem("Tools/Fusion/Rebuild Prefab Table");

        var lootRoot = root.Find("Loot") ?? Group(root, "Loot");
        GameObject Spawn(GameObject prefab, Vector3 pos, float yaw)
        {
            var i = (GameObject)PrefabUtility.InstantiatePrefab(prefab, lootRoot);
            i.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            return i;
        }
        Spawn(keycard, new Vector3(-10f, 0.79f, 33.5f), 25f);                     // на столике в комнате отдыха
        var c1 = Spawn(cash, new Vector3(-10f, 1.0f, 39.0f), 0f);                  // на кухне
        var kitchen = sw.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Kitchenette");
        if (kitchen) PutOnTop(c1.transform, kitchen.gameObject, 0.5f, 1.2f);
        Spawn(cash, new Vector3(5.5f, 1.23f, 39.5f), 0f);                          // в сейфе за картиной
        var c3 = Spawn(cash, new Vector3(5.5f, 0.9f, 35.3f), 30f);                 // на столе директора
        var desk = sw.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "DirectorDesk");
        if (desk) PutOnTop(c3.transform, desk.gameObject, 0.5f, 1.2f);

        var reg = Object.FindAnyObjectByType<LootRegistry>(FindObjectsInactive.Include);
        if (reg)
        {
            var list = (reg.prefabs ?? new LootRegistry.Entry[0]).ToList();
            if (list.All(e => e.id != "KeyCard")) list.Add(new LootRegistry.Entry { id = "KeyCard", prefab = keycard });
            if (list.All(e => e.id != "CashStack")) list.Add(new LootRegistry.Entry { id = "CashStack", prefab = cash });
            reg.prefabs = list.ToArray();
            EditorUtility.SetDirty(reg);
        }
        log.AppendLine("  staff loot: key card ($100) + 3 cash stacks ($250)");
    }
}
