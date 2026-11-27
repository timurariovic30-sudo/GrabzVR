using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Симуляция VR-управления без шлема (Play Mode, batchmode без -nographics):
/// во временной сцене (пол + ступенька) стоит VRGorillaPlayer; трекинг контроллеров отключён, скрипт сам двигает
/// «контроллеры» как живые руки и замеряет:
///  • ходьба: обе руки упираются в пол и гребут назад на 0.4 м → тело должно уехать вперёд;
///  • прыжок: резкий толчок руками вниз-назад → вертикальная скорость и высота подлёта;
///  • руки: центр ладони модели против точки контроллера (физическая сфера руки) и вылет кончиков пальцев за неё;
///  • нокаут: падение с 8 м → управление выключено ~2.5 с, затем возвращается.
/// Результат: Logs/VRLocomotionSim.txt, снимки production/qa/evidence/vrsim_*.png. Сцены проекта не меняет.
/// -executeMethod VRLocomotionSimCheck.RunBatch
/// </summary>
[InitializeOnLoad]
public static class VRLocomotionSimCheck
{
    const string Key = "VRLocomotionSimCheck.Running";
    const string VRPrefab = "Assets/Prefabs/Player/VRGorillaPlayer.prefab";
    static readonly StringBuilder log = new StringBuilder();
    static System.Collections.Generic.IEnumerator<float> routine;
    static double waitUntil;
    static int lastFrame = -1;

    static VRLocomotionSimCheck()
    {
        if (SessionState.GetBool(Key, false)) EditorApplication.playModeStateChanged += OnState;
    }

    public static void RunBatch()
    {
        SessionState.SetBool(Key, true);
        SessionState.SetBool(Key + ".opts", EditorSettings.enterPlayModeOptionsEnabled);
        SessionState.SetInt(Key + ".optv", (int)EditorSettings.enterPlayModeOptions);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Floor"; floor.transform.localScale = new Vector3(40, 1, 40); floor.transform.position = new Vector3(0, -0.5f, 0);
        var step = GameObject.CreatePrimitive(PrimitiveType.Cube); step.name = "Step"; step.transform.localScale = new Vector3(4, 0.8f, 1); step.transform.position = new Vector3(0, 0.4f, 6f);
        var p = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(VRPrefab));
        p.transform.position = Vector3.zero;
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
            File.WriteAllText("Logs/VRLocomotionSim.txt", log.ToString());
            Debug.Log(log.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(log.ToString().Contains("FAIL") ? 1 : 0);
        }
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < waitUntil) return;
        if (Time.frameCount == lastFrame) return;          // не больше одного шага сценария за кадр игры
        lastFrame = Time.frameCount;
        if (routine.MoveNext()) waitUntil = EditorApplication.timeSinceStartup + routine.Current;
        else { EditorApplication.update -= Tick; EditorApplication.ExitPlaymode(); }
    }

    // ------------------------------------------------------------------ сценарий (yield = пауза в секундах)
    static System.Collections.Generic.IEnumerator<float> Run()
    {
        log.AppendLine("[VRLocomotionSim] 72 FPS, контроллеры двигает скрипт");
        var gp = GorillaLocomotion.Player.Instance ?? Object.FindAnyObjectByType<GorillaLocomotion.Player>();
        if (!gp) { log.AppendLine("  FAIL: no GorillaLocomotion.Player"); yield break; }
        // трекинг выключаем — «контроллерами» управляет скрипт
        foreach (var b in gp.GetComponentsInChildren<Behaviour>(true))
            if (b.GetType().Name.Contains("TrackedPoseDriver") || b.GetType().Name.Contains("ActionBasedController")) b.enabled = false;
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = 72;      // частота кадров как на Quest
        var rb = gp.GetComponent<Rigidbody>();
        var L = gp.leftHandTransform; var R = gp.rightHandTransform; var head = gp.headCollider.transform;
        var rig = gp.GetComponentInChildren<GorillaFinalRig>(true);
        Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized; if (fwd.sqrMagnitude < 0.1f) fwd = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        void Hands(Vector3 l, Vector3 r) { L.position = l; R.position = r; }
        Vector3 Body() => gp.bodyCollider.transform.position;

        // стоим: руки опущены вдоль тела, ждём, пока упадём на пол
        Hands(head.position - Vector3.up * 0.7f - right * 0.3f, head.position - Vector3.up * 0.7f + right * 0.3f);
        yield return 1.2f;
        float floorY = Body().y;
        log.AppendLine($"  settled: head {head.position.y:F2} m, body {floorY:F2} m");

        // ---- руки: центр ладони против контроллера, вылет кончиков пальцев
        if (rig)
        {
            rig.UpdateRig();
            foreach (var (side, limb, target) in new[] { ("L", rig.left, L), ("R", rig.right, R) })
            {
                var hand = limb.hand; Vector3 palm = hand.position + hand.up * limb.palmOffset;
                float tips = hand.GetComponentsInChildren<Transform>(true).Where(t => t.name.EndsWith("Distal")).Select(t => Vector3.Distance(t.position, limb.target.position)).DefaultIfEmpty(0).Max();
                log.AppendLine($"  hand {side}: palm center → controller {Vector3.Distance(palm, limb.target.position) * 100f:F1} cm, fingertips reach {tips * 100f:F1} cm from controller (physics sphere r {gp.minimumRaycastDistance * 100f:F0} cm), palmOffset {limb.palmOffset * 100f:F1} cm");
            }
        }

        // ---- ходьба: 4 гребка (покадрово: руки опускаются на пол перед собой и ведут назад 0.45 м)
        Vector3 start = Body();
        for (int stroke = 0; stroke < 4; stroke++)
        {
            Vector3 c = Body();
            Vector3 l0 = new Vector3(c.x, 0.07f, c.z) + fwd * 0.3f - right * 0.3f, r0 = l0 + right * 0.6f;
            Hands(l0 + Vector3.up * 0.25f, r0 + Vector3.up * 0.25f); for (int i = 0; i < 3; i++) yield return 0f;   // занести руки
            Hands(l0, r0); for (int i = 0; i < 2; i++) yield return 0f;                                             // коснуться пола
            for (int i = 1; i <= 10; i++) { Vector3 d = -fwd * 0.045f * i + Vector3.down * 0.03f; Hands(l0 + d, r0 + d); yield return 0f; }
            Vector3 up = Vector3.up * 0.3f; Hands(l0 - fwd * 0.45f + up, r0 - fwd * 0.45f + up); for (int i = 0; i < 4; i++) yield return 0f;
        }
        float walked = Vector3.Dot(Body() - start, fwd);
        log.AppendLine($"  walk: 4 strokes → moved {walked:F2} m forward" + (walked > 0.4f ? "" : "  FAIL: body barely moved"));

        var ragC = gp.GetComponentInChildren<GorillaFallRagdoll>(true);
        log.AppendLine($"  knockouts during walk: {(ragC ? ragC.KnockoutCount : 0)}");
        // ---- прыжок: покадрово — руки упираются в пол, 6 кадров резкого толчка вниз-назад, отрыв
        yield return 1.0f;
        {
            rb.linearVelocity = Vector3.zero;                                              // после ходьбы тело ещё скользит
            Hands(Body() + Vector3.up * 0.3f - right * 0.3f, Body() + Vector3.up * 0.3f + right * 0.3f);
            for (int i = 0; i < 40; i++) { rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0); yield return 0f; }
            Vector3 c = Body(); Vector3 l0 = new Vector3(c.x, 0.07f, c.z) + fwd * 0.25f - right * 0.3f, r0 = l0 + right * 0.6f;
            Hands(l0, r0);                                                                  // руки над полом (сфера руки 5 см)
            for (int i = 0; i < 12; i++) yield return 0f;
            float y0 = Body().y, maxY = y0, maxV = 0;
            Vector3 d = Vector3.zero;
            for (int i = 0; i < 6; i++) { d += Vector3.down * 0.06f - fwd * 0.03f; Hands(l0 + d, r0 + d); yield return 0f; maxV = Mathf.Max(maxV, rb.linearVelocity.y); maxY = Mathf.Max(maxY, Body().y); }
            Hands(Body() + Vector3.up * 0.4f - right * 0.35f, Body() + Vector3.up * 0.4f + right * 0.35f);   // отрыв рук
            for (int i = 0; i < 60; i++) { yield return 0f; maxY = Mathf.Max(maxY, Body().y); maxV = Mathf.Max(maxV, rb.linearVelocity.y); Hands(Body() + Vector3.up * 0.4f - right * 0.35f, Body() + Vector3.up * 0.4f + right * 0.35f); }
            log.AppendLine($"  jump: up speed {maxV:F2} m/s, height {(maxY - y0):F2} m" + (maxY - y0 > 0.15f ? "" : "  FAIL: no jump"));
        }
        log.AppendLine($"  knockouts after jump: {(ragC ? ragC.KnockoutCount : 0)}");
        Hands(head.position - Vector3.up * 0.7f - right * 0.3f, head.position - Vector3.up * 0.7f + right * 0.3f);
        yield return 3.2f;                                                                   // если был нокаут — дождаться конца
        Shot("vrsim_hands", head.position + fwd * 0.9f + Vector3.up * 0.1f, head.position + Vector3.down * 0.55f);

        // ---- нокаут: падение с 8 м
        var rag = ragC;
        if (rag)
        {
            int before = rag.KnockoutCount;
            yield return 0.8f;
            rb.position = gp.transform.position = Body() + Vector3.up * 8f; rb.linearVelocity = Vector3.zero; gp.InitializeValues();
            Hands(head.position - Vector3.up * 0.7f - right * 0.3f, head.position - Vector3.up * 0.7f + right * 0.3f);
            float t = 0, koAt = -1, backAt = -1;
            while (t < 7f)
            {
                yield return 0.05f; t += 0.05f;
                if (koAt < 0 && rag.IsKnockedOut) { koAt = t; log.AppendLine($"  knockout after {t:F2} s of fall: locomotion enabled = {gp.enabled}, camera height above floor {(head.position.y - floorY + 1f):F2}"); Shot("vrsim_knockout", head.position + fwd * 1.6f + Vector3.up * 0.8f, head.position); }
                if (koAt >= 0 && backAt < 0 && gp.enabled) backAt = t;
            }
            log.AppendLine($"  knockouts from the fall: {rag.KnockoutCount - before}");
            if (koAt < 0) log.AppendLine("  FAIL: no knockout after 8 m fall");
            else log.AppendLine($"  control returned after {(backAt - koAt):F2} s" + (backAt > 0 && backAt - koAt > 2f && backAt - koAt < 3.6f ? "" : "  FAIL: control not returned in ~2.5 s"));
        }
        log.AppendLine(log.ToString().Contains("FAIL") ? "  RESULT: problems found" : "  RESULT: OK");
    }

    static void Shot(string name, Vector3 pos, Vector3 look)
    {
        var go = new GameObject("SimCam"); var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.7f, 0.82f); cam.fieldOfView = 50;
        cam.transform.position = pos; cam.transform.LookAt(look);
        var rt = new RenderTexture(1000, 1000, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
        for (int i = 0; i < 2; i++) cam.Render();
        RenderTexture.active = rt; var t = new Texture2D(1000, 1000, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 1000, 1000), 0, 0); t.Apply();
        Directory.CreateDirectory("production/qa/evidence"); File.WriteAllBytes($"production/qa/evidence/{name}.png", t.EncodeToPNG());
        RenderTexture.active = null; Object.Destroy(go); log.AppendLine($"  shot production/qa/evidence/{name}.png");
    }
}
