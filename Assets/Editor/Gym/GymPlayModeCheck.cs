using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Play Mode проверка полигона TestGym (batchmode без -nographics), сцену не сохраняет:
/// VR-игрок включается вместо ПК-игрока, трекинг отключён — «контроллеры» двигает скрипт, сеть не подключается.
///  1. физика из префаба (история скоростей, прыжок, скорость, радиус сферы руки);
///  2. головы чужих игроков: копия NetworkGorillaAvatar до/после MakeHeadVisible глазами VR-камеры;
///  3. стенд физики: −/+ меняют значения игрока, СОХРАНИТЬ/СБРОС;
///  4. дрель: пуск → перегрев (клин) → удар рукой (сфера руки входит в зону удара) → сейф открыт;
///  5. щиток: 3 рубильника гасят лазеры; лазер с включённым питанием поднимает тревогу;
///  6. охранник: игрок перед ним → шокер → нокаут;
///  7. портал: игрок входит → загружается MainHub.
/// Лог: Logs/GymPlayModeCheck.txt, снимки production/qa/evidence/gymplay_*.png.  -executeMethod GymPlayModeCheck.RunBatch
/// </summary>
[InitializeOnLoad]
public static class GymPlayModeCheck
{
    const string Key = "GymPlayModeCheck.Running";
    static readonly StringBuilder log = new StringBuilder();
    static System.Collections.Generic.IEnumerator<float> routine;
    static double waitUntil;
    static int lastFrame = -1;

    static GymPlayModeCheck() { if (SessionState.GetBool(Key, false)) EditorApplication.playModeStateChanged += OnState; }

    public static void RunBatch()
    {
        SessionState.SetBool(Key, true);
        SessionState.SetBool(Key + ".opts", EditorSettings.enterPlayModeOptionsEnabled);
        SessionState.SetInt(Key + ".optv", (int)EditorSettings.enterPlayModeOptions);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorSceneManager.OpenScene("Assets/Scenes/TestGym.unity", OpenSceneMode.Single);
        // VR-игрок вместо ПК, без сети (изменения не сохраняются)
        foreach (var g in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (g.name == "DesktopTestPlayer" || g.name == "PlayerModeSwitcher" || g.name == "XR Device Simulator") g.SetActive(false);
            if (g.name == "VRGorillaPlayer") g.SetActive(true);
        }
        var fnm = Object.FindAnyObjectByType<FusionNetworkManager>(FindObjectsInactive.Include); if (fnm) fnm.autoConnect = false;
        EditorApplication.playModeStateChanged += OnState;
        EditorApplication.EnterPlaymode();
    }

    static void OnState(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.EnteredPlayMode) { routine = Run(); waitUntil = 0; EditorApplication.update += Tick; }
        if (s == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.playModeStateChanged -= OnState;
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(Key + ".opts", false);
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(Key + ".optv", 0);
            SessionState.EraseBool(Key);
            File.WriteAllText("Logs/GymPlayModeCheck.txt", log.ToString());
            Debug.Log(log.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(log.ToString().Contains("FAIL") ? 1 : 0);
        }
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < waitUntil) return;
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        bool more;
        try { more = routine.MoveNext(); }
        catch (System.Exception e) { log.AppendLine("  FAIL: exception " + e); more = false; }
        if (more) waitUntil = EditorApplication.timeSinceStartup + routine.Current;
        else { EditorApplication.update -= Tick; EditorApplication.ExitPlaymode(); }
    }

    static void Check(bool ok, string what) => log.AppendLine((ok ? "  ok   " : "  FAIL ") + what);

    static System.Collections.Generic.IEnumerator<float> Run()
    {
        log.AppendLine("[GymPlayModeCheck] TestGym, VR-игрок, 72 FPS, без сети");
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = 72;
        var gp = GorillaLocomotion.Player.Instance;
        if (!gp) { log.AppendLine("  FAIL: no GorillaLocomotion.Player"); yield break; }
        foreach (var b in gp.GetComponentsInChildren<Behaviour>(true))
            if (b.GetType().Name.Contains("TrackedPoseDriver") || b.GetType().Name.Contains("ActionBasedController")) b.enabled = false;
        var rb = gp.GetComponent<Rigidbody>(); var head = gp.headCollider.transform; var L = gp.leftHandTransform; var R = gp.rightHandTransform;
        void HandsDown() { L.position = head.position + new Vector3(-0.3f, -0.7f, 0.1f); R.position = head.position + new Vector3(0.3f, -0.7f, 0.1f); }
        void Teleport(Vector3 feet, float yaw)
        {
            var off = gp.transform.position - new Vector3(gp.bodyCollider.transform.position.x, gp.bodyCollider.bounds.min.y, gp.bodyCollider.transform.position.z);
            gp.transform.SetPositionAndRotation(feet + off + Vector3.up * 0.05f, Quaternion.Euler(0, yaw, 0));
            rb.position = gp.transform.position; rb.linearVelocity = Vector3.zero; HandsDown(); gp.InitializeValues(); Physics.SyncTransforms();
        }
        HandsDown();
        yield return 1.0f;

        // 1. физика
        Check(gp.velocityHistorySize == 4 && Mathf.Approximately(gp.minimumRaycastDistance, 0.075f), $"locomotion: history {gp.velocityHistorySize}, hand sphere r {gp.minimumRaycastDistance}, jump ×{gp.jumpMultiplier}, maxJumpSpeed {gp.maxJumpSpeed}, gravity {gp.gravityScale}g, groundDamping {gp.groundDamping}");
        Check(gp.bodyCollider.sharedMaterial && gp.bodyCollider.sharedMaterial.dynamicFriction == 0f, $"body collider friction {(gp.bodyCollider.sharedMaterial ? gp.bodyCollider.sharedMaterial.name : "none")}, grounded {gp.IsGrounded}");

        // 2. головы чужих игроков
        var cam = Camera.main;
        int localHead = LayerMask.NameToLayer("LocalHead");
        Check(cam && (cam.cullingMask & (1 << localHead)) == 0, $"VR camera '{(cam ? cam.name : "none")}' hides LocalHead (own head)");
        var ownHead = gp.GetComponentsInChildren<Renderer>(true).Count(r => r.gameObject.layer == localHead);
        Check(ownHead > 0, $"local VRGorillaPlayer keeps {ownHead} renderers on LocalHead");
        var netPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Network/NetworkGorillaAvatar.prefab");
        if (netPrefab && cam)
        {
            var holder = new GameObject("RemoteTest"); holder.SetActive(false);
            var before = Object.Instantiate(netPrefab, holder.transform); var after = Object.Instantiate(netPrefab, holder.transform);
            foreach (var mb in holder.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
            var fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized; var right = Vector3.Cross(Vector3.up, fwd);
            var basePos = new Vector3(cam.transform.position.x, gp.bodyCollider.bounds.min.y, cam.transform.position.z) + fwd * 1.8f;
            before.transform.SetPositionAndRotation(basePos - right * 0.55f, Quaternion.LookRotation(-fwd));
            after.transform.SetPositionAndRotation(basePos + right * 0.55f, Quaternion.LookRotation(-fwd));
            int lhBefore = before.GetComponentsInChildren<Transform>(true).Count(t => t.gameObject.layer == localHead);
            int moved = NetworkGorillaAvatar.MakeHeadVisible(after);
            int lhAfter = after.GetComponentsInChildren<Transform>(true).Count(t => t.gameObject.layer == localHead);
            holder.SetActive(true);
            // точка привязки префаба не на ногах: поднимаем так, чтобы кость головы была на высоте 1.45 м над полом
            foreach (var inst in new[] { before, after })
            {
                var hb = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Head");
                if (hb) inst.transform.position += Vector3.up * (basePos.y + 1.45f - hb.position.y);
            }
            Check(lhBefore > 0 && moved == lhBefore && lhAfter == 0, $"remote avatar: LocalHead objects {lhBefore} → after MakeHeadVisible {lhAfter} (moved {moved})");
            yield return 0.1f;
            foreach (var (nm, inst) in new[] { ("before", before), ("after", after) })
            {
                var rs = inst.GetComponentsInChildren<Renderer>(true);
                var vis = rs.Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                var bb = vis.Length > 0 ? vis[0].bounds : new Bounds(); foreach (var r in vis) bb.Encapsulate(r.bounds);
                int culled = vis.Count(r => (cam.cullingMask & (1 << r.gameObject.layer)) == 0);
                log.AppendLine($"       {nm}: renderers {rs.Length}, visible {vis.Length}, culled by VR camera {culled}, bounds {bb.center} size {bb.size}");
            }
            var shotPos = basePos - fwd * 1.0f + Vector3.up * 1.45f;
            Shot("gymplay_remote_heads", cam, shotPos, basePos + Vector3.up * 1.15f, cam.cullingMask);
            log.AppendLine("       (снимок глазами VR-камеры: слева чужая горилла ДО исправления — без головы, справа ПОСЛЕ)");
            Object.Destroy(holder);
        }
        else log.AppendLine("  FAIL: NetworkGorillaAvatar prefab or camera missing");

        // 3. стенд физики
        PushButton B(string n) => Object.FindObjectsByType<PushButton>(FindObjectsInactive.Exclude).FirstOrDefault(b => b.name == "Btn_" + n);
        var stand = Object.FindAnyObjectByType<LocomotionTuningStand>();
        B("ПРЫЖОК_Plus")?.Press(); B("СКОРОСТЬ_Minus")?.Press(); B("ГРАВИТАЦИЯ_Plus")?.Press();
        yield return 0.3f;                                                                   // несколько кадров: GorillaHeavySlowdown не должен откатить значения
        Check(Mathf.Abs(gp.jumpMultiplier - 1.35f) < 0.001f && Mathf.Abs(gp.maxJumpSpeed - 7.5f) < 0.001f && Mathf.Abs(gp.gravityScale - 1.1f) < 0.001f,
              $"stand +/−: jump ×{gp.jumpMultiplier:0.00}, speed {gp.maxJumpSpeed:0.0}, gravity {gp.gravityScale:0.0}g; display '{stand?.jumpText?.text.Replace("\n", " ")}'");
        B("Save")?.Press(); yield return 0.1f;
        Check(LocomotionTuning.HasSaved, "СОХРАНИТЬ → PlayerPrefs");
        B("Reset")?.Press(); yield return 0.1f;
        Check(!LocomotionTuning.HasSaved && Mathf.Approximately(gp.jumpMultiplier, LocomotionTuning.DefaultJump) && Mathf.Approximately(gp.gravityScale, 1f), $"СБРОС → эталон ×{gp.jumpMultiplier}, {gp.maxJumpSpeed} м/с, {gp.gravityScale}g, сохранение стёрто");

        // 4. дрель
        var drill = Object.FindAnyObjectByType<SafeDrill>();
        var zone = Object.FindAnyObjectByType<DrillHitZone>();
        if (drill && zone)
        {
            drill.drillSeconds = 2.5f; drill.heatSeconds = 1.0f;                            // ускоренный прогон
            Teleport(new Vector3(14f, 0f, 6.3f), 0f);
            yield return 0.5f;
            B("DrillStart")?.Press();
            float t = 0; while (t < 3f && !drill.Jammed) { yield return 0.05f; t += 0.05f; }
            Check(drill.Jammed && drill.Progress < 1f, $"дрель: перегрев и клин через {t:F2} с, прогресс {drill.Progress * 100f:0}%, нагрев {drill.Heat * 100f:0}%");
            Shot("gymplay_drill_jammed", null, head.position + new Vector3(0.9f, 0.1f, -0.3f), zone.transform.position, ~0);
            // удар правой рукой: контроллер проходит сквозь зону удара
            var hz = zone.transform.position;
            R.position = hz + new Vector3(0.35f, 0f, -0.45f); yield return 0f; yield return 0f;
            for (int i = 1; i <= 6; i++) { R.position = hz + new Vector3(0.35f - i * 0.06f, 0f, -0.45f + i * 0.08f); yield return 0f; }
            yield return 0.1f;
            Check(!drill.Jammed, $"удар рукой выбил клин (нагрев {drill.Heat * 100f:0}%)");
            HandsDown();
            t = 0; while (t < 6f && !drill.Done) { yield return 0.1f; t += 0.1f; if (drill.Jammed) { zone.Press(); } }
            yield return 1.6f;
            Check(drill.Done && Mathf.Abs(Mathf.DeltaAngle(drill.doorHinge.localEulerAngles.y, drill.doorOpenAngle)) < 5f, $"сейф просверлен, дверь открыта на {drill.doorHinge.localEulerAngles.y:0}°");
            Shot("gymplay_drill_open", null, head.position + new Vector3(-1.3f, 0.3f, -0.8f), drill.doorHinge.position + Vector3.right * 0.4f, ~0);
            B("DrillReset")?.Press(); yield return 0.2f;
            Check(drill.Progress == 0f, "СБРОС дрели");
        }
        else log.AppendLine("  FAIL: drill missing");

        // 5. лазеры и щиток
        var panel = Object.FindAnyObjectByType<BreakerPanel>();
        var alarm = AlarmSystem.Instance;
        var grids = panel ? panel.poweredObjects : new GameObject[0];
        var beam = Object.FindObjectsByType<LaserBeam>(FindObjectsInactive.Exclude).OrderBy(b => b.transform.position.y).FirstOrDefault();
        if (beam && alarm)
        {
            var bp = beam.transform.position;
            Teleport(new Vector3(bp.x, 0f, bp.z - 1.2f), 0f); yield return 0.3f;
            for (int i = 0; i < 20 && !alarm.IsActive; i++) { var p = gp.transform.position + Vector3.forward * 0.12f; gp.transform.position = p; rb.position = p; yield return 0f; }
            Check(alarm.IsActive, "лазер задет телом → тревога");
        }
        else log.AppendLine("  FAIL: laser beam or AlarmSystem missing");
        if (panel)
        {
            Teleport(new Vector3(20f, 0f, 6.8f), 0f); yield return 0.4f;
            for (int i = 0; i < 3; i++) { B("Breaker_" + i)?.Press(); yield return 0.1f; }
            yield return 0.4f;
            Check(panel.PowerCut && grids.All(gr => !gr.activeSelf), $"3 рубильника опущены → лазеры погасли ({grids.Length} решётки)");
            Shot("gymplay_breakers", null, new Vector3(18.6f, 1.8f, 5.4f), new Vector3(20f, 1.4f, 8.2f), ~0);
            yield return 0.7f;                                                               // задержка повторного нажатия кнопки 0.6 с
            B("Breaker_1")?.Press(); yield return 0.3f;
            Check(!panel.PowerCut && grids.All(gr => gr.activeSelf), "один рубильник поднят → лазеры снова под током");
        }

        // 6. охранник
        var guard = Object.FindAnyObjectByType<PatrolGuard>();
        var rag = PatrolGuard.LocalRagdoll();
        if (guard && rag)
        {
            yield return 5.5f;                                                               // откат шокера и тревоги
            var gb = guard.body;
            Teleport(gb.position + gb.forward * 1.6f, Quaternion.LookRotation(-gb.forward).eulerAngles.y);
            int z0 = guard.ZapCount; float t = 0;
            while (t < 2f && guard.ZapCount == z0) { yield return 0.05f; t += 0.05f; }
            Check(guard.ZapCount > z0, $"охранник ударил шокером через {t:F2} с");
            yield return 0.15f;
            Check(rag.IsKnockedOut, "игрок в нокауте от шокера");
            Shot("gymplay_guard_zap", null, gb.position + gb.right * 2.2f + Vector3.up * 1.6f + gb.forward * 0.8f, gb.position + gb.forward * 0.8f + Vector3.up * 0.8f, ~0);
            t = 0; while (t < 5f && rag.IsKnockedOut) { yield return 0.1f; t += 0.1f; }
            Check(!rag.IsKnockedOut, $"управление вернулось через {t:F1} с");
        }
        else log.AppendLine("  FAIL: guard or local ragdoll missing");

        // 7. прыжок в полигоне (новая физика)
        {
            Teleport(new Vector3(-2f, 0f, 2f), 0f); yield return 1.2f;
            var fwd = Vector3.forward; var right = Vector3.right;
            Vector3 Body() => gp.bodyCollider.transform.position;
            for (int i = 0; i < 30; i++) { rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0); yield return 0f; }
            Vector3 c = Body(); Vector3 l0 = new Vector3(c.x, gp.minimumRaycastDistance + 0.02f, c.z) + fwd * 0.25f - right * 0.3f, r0 = l0 + right * 0.6f;
            L.position = l0; R.position = r0; for (int i = 0; i < 12; i++) yield return 0f;
            float y0 = Body().y, maxY = y0, maxV = 0; Vector3 d = Vector3.zero;
            for (int i = 0; i < 6; i++) { d += Vector3.down * 0.06f - fwd * 0.03f; L.position = l0 + d; R.position = r0 + d; yield return 0f; maxV = Mathf.Max(maxV, rb.linearVelocity.y); }
            for (int i = 0; i < 60; i++) { L.position = Body() + Vector3.up * 0.4f - right * 0.35f; R.position = Body() + Vector3.up * 0.4f + right * 0.35f; yield return 0f; maxY = Mathf.Max(maxY, Body().y); maxV = Mathf.Max(maxV, rb.linearVelocity.y); }
            Check(maxY - y0 > 0.55f, $"прыжок тем же толчком, что в старой симуляции: {maxV:F2} м/с, высота {(maxY - y0):F2} м (было 2.52 м/с, 0.55 м)");
            // затухание на земле: толчок в сторону без рук → остановка
            HandsDown(); yield return 1.0f;
            rb.linearVelocity = new Vector3(3f, 0f, 0f); float t = 0;
            while (t < 2f && new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude > 0.1f) { yield return 0.02f; t += 0.02f; }
            Check(t < 1.0f, $"на земле инерция 3 м/с гаснет за {t:F2} с (grounded {gp.IsGrounded})");
        }

        // 8. портал в хаб
        Teleport(new Vector3(0f, 0f, -11.2f), 180f); yield return 0.3f;
        for (int i = 0; i < 30 && SceneManager.GetActiveScene().name != GameSession.HubScene; i++)
        {
            var p = gp.transform.position + Vector3.back * 0.1f; gp.transform.position = p; rb.position = p; yield return 0.05f;
        }
        yield return 1.5f;
        Check(SceneManager.GetActiveScene().name == GameSession.HubScene, $"портал → активная сцена {SceneManager.GetActiveScene().name}");
        log.AppendLine(log.ToString().Contains("FAIL") ? "  RESULT: problems found" : "  RESULT: OK");
    }

    static void Shot(string name, Camera like, Vector3 pos, Vector3 look, int mask)
    {
        var go = new GameObject("GymShotCam"); var c = go.AddComponent<Camera>();
        c.cullingMask = mask; c.fieldOfView = like ? like.fieldOfView : 65f; c.nearClipPlane = 0.03f; c.clearFlags = CameraClearFlags.Skybox;
        c.transform.position = pos; c.transform.LookAt(look);
        var rt = new RenderTexture(1280, 720, 24) { antiAliasing = 4 }; c.targetTexture = rt;
        for (int i = 0; i < 2; i++) c.Render();
        RenderTexture.active = rt; var t = new Texture2D(1280, 720, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); t.Apply();
        Directory.CreateDirectory("production/qa/evidence"); File.WriteAllBytes($"production/qa/evidence/{name}.png", t.EncodeToPNG());
        RenderTexture.active = null; c.targetTexture = null; Object.Destroy(rt); Object.Destroy(go); Object.Destroy(t);
        log.AppendLine($"       снимок production/qa/evidence/{name}.png");
    }
}
