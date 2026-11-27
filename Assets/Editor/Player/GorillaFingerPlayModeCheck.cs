using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Проверка сгиба пальцев в настоящем Play Mode (без шлема): во временной сцене стоит VRGorillaPlayer,
/// VRHandPoseController.SetPose сжимает обе руки в кулак (Grip + Trigger), затем — «указка» (только Grip),
/// замеряются углы фаланг большого, указательного и среднего пальцев относительно позы привязки и делается снимок кисти.
/// Batchmode (без -nographics): -executeMethod GorillaFingerPlayModeCheck.RunBatch → Logs/FingerPlayModeCheck.txt,
/// production/qa/evidence/final3f_playmode_fist.png. Сцены проекта не меняет; настройки Enter Play Mode возвращает как были.
/// </summary>
[InitializeOnLoad]
public static class GorillaFingerPlayModeCheck
{
    const string Key = "GorillaFingerPlayModeCheck.Running";
    const string VRPrefab = "Assets/Prefabs/Player/VRGorillaPlayer.prefab";
    static int frame;
    static readonly StringBuilder log = new StringBuilder();

    static GorillaFingerPlayModeCheck()
    {
        if (!SessionState.GetBool(Key, false)) return;
        EditorApplication.playModeStateChanged += OnState;
    }

    public static void RunBatch()
    {
        SessionState.SetBool(Key, true);
        SessionState.SetBool(Key + ".opts", EditorSettings.enterPlayModeOptionsEnabled);
        SessionState.SetInt(Key + ".optv", (int)EditorSettings.enterPlayModeOptions);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var p = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(VRPrefab));
        p.transform.position = Vector3.zero;
        EditorApplication.playModeStateChanged += OnState;
        EditorApplication.EnterPlaymode();
    }

    static void OnState(PlayModeStateChange s)
    {
        if (s == PlayModeStateChange.EnteredPlayMode) { frame = 0; EditorApplication.update += Tick; }
        if (s == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.playModeStateChanged -= OnState;
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(Key + ".opts", false);
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(Key + ".optv", 0);
            SessionState.EraseBool(Key);
            File.WriteAllText("Logs/FingerPlayModeCheck.txt", log.ToString());
            Debug.Log(log.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(log.ToString().Contains("FAIL") ? 1 : 0);
        }
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || ++frame < 20) return;
        EditorApplication.update -= Tick;
        log.AppendLine("[FingerPlayModeCheck] Play Mode: " + EditorApplication.isPlaying);
        var pose = Object.FindAnyObjectByType<VRHandPoseController>();
        if (!pose) { log.AppendLine("  FAIL: no VRHandPoseController"); EditorApplication.ExitPlaymode(); return; }
        var root = pose.avatarRoot ? pose.avatarRoot : pose.transform;
        Transform B(string n) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
        string[] fingers = { "Thumb", "Index", "Middle" };
        var bind = fingers.SelectMany(f => new[] { "Proximal", "Intermediate" }.Select(k => "Left_" + f + k))
                          .ToDictionary(n => n, n => B(n) ? B(n).localRotation : Quaternion.identity);
        string Measure(string label)
        {
            var sb = new StringBuilder($"  {label}:");
            foreach (var f in fingers)
            {
                var a = B("Left_" + f + "Proximal"); var b = B("Left_" + f + "Intermediate");
                if (!a || !b) { sb.Append($" {f} FAIL(no bones)"); continue; }
                sb.Append($" {f} {Quaternion.Angle(bind["Left_" + f + "Proximal"], a.localRotation):F0}°/{Quaternion.Angle(bind["Left_" + f + "Intermediate"], b.localRotation):F0}°");
            }
            return sb.ToString();
        }
        int found = fingers.Count(f => B("Left_" + f + "Proximal")), extra = new[] { "Ring", "Pinky" }.Count(f => B("Left_" + f + "Proximal"));
        log.AppendLine($"  finger chains: {found} (expected 3), extra Ring/Pinky: {extra}");
        if (found != 3 || extra != 0) log.AppendLine("  FAIL: unexpected finger set");

        pose.SetPose(true, 1f, 1f); pose.SetPose(false, 1f, 1f);
        string fist = Measure("fist (grip+trigger)"); log.AppendLine(fist);
        // снимок кисти в позе кулака (тот же кадр: LateUpdate ещё не вернул руку к кнопкам)
        var hand = B("Left_Hand");
        var camGo = new GameObject("CheckCam"); var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.62f, 0.7f, 0.82f); cam.fieldOfView = 35;
        cam.transform.position = hand.position + root.forward * 0.55f + Vector3.up * 0.15f; cam.transform.LookAt(hand.position + root.forward * 0.05f);
        var rt = new RenderTexture(1000, 1000, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
        for (int i = 0; i < 2; i++) cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(1000, 1000, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1000, 1000), 0, 0); tex.Apply();
        Directory.CreateDirectory("production/qa/evidence");
        File.WriteAllBytes("production/qa/evidence/final3f_playmode_fist.png", tex.EncodeToPNG());
        RenderTexture.active = null;
        log.AppendLine("  shot production/qa/evidence/final3f_playmode_fist.png");

        pose.SetPose(true, 1f, 0f);
        log.AppendLine(Measure("point (grip only, index straight)"));
        pose.SetPose(true, 0f, 0f);
        log.AppendLine(Measure("relaxed"));
        bool bent = fist.Contains("Index") && !fist.Contains(" 0°/");
        log.AppendLine(bent ? "  OK: fingers bend in Play Mode" : "  FAIL: fingers did not bend");
        EditorApplication.ExitPlaymode();
    }
}
