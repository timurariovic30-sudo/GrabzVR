using Fusion;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Двери комнат служебного крыла (часть BankVisualOverhaul): комната отдыха, серверная, кабинет директора.
/// Деревянная створка на петле в проёме перегородки z = 28, открывается внутрь комнаты. Сетевая (NetworkVaultDoor,
/// Master Client Object): кнопка в коридоре «ОТКРЫТЬ» (у кабинета директора — «ВЗЛОМ» 3 с), в комнате — «ДВЕРЬ».
/// </summary>
public static partial class BankVisualOverhaul
{
    static void BuildRoomDoors(Transform root)
    {
        const float wallZ = 28f;
        foreach (var (x0, x1, id, hack) in new[] { (-11.5f, -9.9f, "BreakRoom", 0.3f), (-5.7f, -4.3f, "ServerRoom", 0.3f), (4.0f, 5.6f, "Director", 3f) })
        {
            var dr = new GameObject($"RoomDoor_{id}_Net").transform;
            dr.SetParent(root, false);
            dr.SetPositionAndRotation(new Vector3(x0, 0, wallZ), Quaternion.identity);
            var hinge = Group(dr, "Hinge");
            float w = x1 - x0 - 0.04f, cx = x0 + 0.02f + w / 2;
            Box(hinge, "Leaf", new Vector3(cx, 1.19f, wallZ), new Vector3(w, 2.36f, 0.06f), mahogany, true);
            foreach (float y in new[] { 0.6f, 1.7f })                                 // филёнки с двух сторон
                foreach (float s in new[] { -1f, 1f })
                    Box(hinge, "Panel", new Vector3(cx, y, wallZ + s * 0.035f), new Vector3(w - 0.3f, 0.8f, 0.01f), woodPanel);
            foreach (float s in new[] { -1f, 1f })
                Box(hinge, "Handle", new Vector3(x1 - 0.22f, 1.05f, wallZ + s * 0.06f), new Vector3(0.12f, 0.03f, 0.03f), brass);
            var src = dr.gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 1f; src.maxDistance = 15f;
            SetMasterFlags(dr.gameObject.AddComponent<NetworkObject>());
            var door = dr.gameObject.AddComponent<NetworkVaultDoor>();
            door.hinge = hinge; door.openAngle = -100f; door.hackSeconds = hack; door.swingSpeed = 160f;
            door.announce = false;                                                    // без сообщений на табло
            door.audioSource = src;
            door.openClips = MapKit.Clips(UiDir, "open"); door.closeClips = MapKit.Clips(UiDir, "close"); door.hackClips = MapKit.Clips(UiDir, "click");
            MakeButton(dr, "Panel_Corridor", new Vector3(x1 + 0.35f, 1.2f, wallZ - 0.09f), Vector3.back, hack > 1f ? "ВЗЛОМ" : "ОТКРЫТЬ", new Color(0.2f, 0.9f, 0.8f), door.PressHack);
            MakeButton(dr, "Panel_Room", new Vector3(x1 + 0.35f, 1.2f, wallZ + 0.09f), Vector3.forward, "ДВЕРЬ", new Color(0.2f, 0.9f, 0.3f), door.PressToggle);
            foreach (var t in dr.GetComponentsInChildren<Transform>(true))
                if (t.gameObject.layer != LayerMask.NameToLayer("Ignore Raycast")) t.gameObject.layer = 0;
        }
        log.AppendLine("  room doors: break room, server room, director office (networked, buttons both sides)");
    }

    /// Проверка кнопок: у каждой есть действие, и луч «как у мыши» спереди упирается в кнопку раньше любой стены или створки.
    static void AuditButtons(UnityEngine.SceneManagement.Scene scene)
    {
        Physics.SyncTransforms();
        int ok = 0, bad = 0;
        foreach (var go in scene.GetRootGameObjects())
            foreach (var b in go.GetComponentsInChildren<PushButton>(false))
            {
                if (!b.gameObject.activeInHierarchy) continue;
                string why = null;
                if (b.onPressed.GetPersistentEventCount() == 0 || b.onPressed.GetPersistentTarget(0) == null) why = "no action";
                else
                {
                    var target = b.transform.TransformPoint(new Vector3(0, 0.06f, 0));
                    var from = target + b.transform.up * 0.8f;
                    var hits = Physics.RaycastAll(from, target - from, 1.2f, ~0, QueryTriggerInteraction.Collide);
                    System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
                    why = "not reachable";
                    foreach (var h in hits)
                    {
                        if (h.collider.GetComponentInParent<IPressable>() != null) { why = null; break; }
                        if (!h.collider.isTrigger) { why = "blocked by " + h.collider.name; break; }
                    }
                }
                if (why == null) ok++;
                else { bad++; log.AppendLine($"  BUTTON-WARN '{b.label}' at {b.transform.position}: {why}"); }
            }
        log.AppendLine($"  buttons: {ok} ok, {bad} with problems");
    }

    /// Проверка надписей: какие не влезли в табличку (шрифт сжат до минимума) — в лог, чтобы ничего не осталось «баганым».
    static void AuditTexts(UnityEngine.SceneManagement.Scene scene)
    {
        int bad = 0, total = 0;
        foreach (var go in scene.GetRootGameObjects())
            foreach (var t in go.GetComponentsInChildren<TMPro.TextMeshPro>(false))
            {
                if (!t.gameObject.activeInHierarchy || !t.enabled) continue;
                total++;
                t.ForceMeshUpdate();
                if (t.enableAutoSizing && t.fontSize <= t.fontSizeMin * 1.05f)
                {
                    bad++;
                    log.AppendLine($"  TEXT-WARN '{t.text}' at {t.transform.position} squeezed to {t.fontSize:0.00}");
                }
            }
        log.AppendLine($"  texts: {total} active, {bad} squeezed");
    }
}
