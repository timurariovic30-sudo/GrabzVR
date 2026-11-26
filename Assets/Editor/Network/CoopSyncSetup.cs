using System.Linq;
using System.Text;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Кооп-синхронизация ограбления — ТОЧЕЧНАЯ донастройка, сцену заново НЕ строит (HeistMapBuilder не вызывается):
///  • 6 префабов лута (Assets/Prefabs/Loot): + NetworkObject (Allow State Authority Override, без Master Client Object,
///    без Destroy When State Authority Leaves) + NetworkTransform (физика: у чужих тело кинематическое) + NetworkLootItem;
///  • HeistLevel_Test: + один объект «HeistNetworkState» (NetworkObject с Master Client Object) — раунд, котёл, тревога, стёкла;
///  • запекание сетевых объектов сцены, таблица сетевых префабов Fusion;
///  • Build Settings: SampleScene выключена (MainHub = 0, HeistLevel_Test = 1).
/// Меню: Tools/Heist/Network/Setup Co-op Sync. Повторный запуск безопасен (ничего не дублирует).
/// Batchmode: -executeMethod CoopSyncSetup.RunBatch
/// </summary>
public static class CoopSyncSetup
{
    const string LootDir = "Assets/Prefabs/Loot";
    const string HeistPath = "Assets/Scenes/HeistLevel_Test.unity";
    const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Tools/Heist/Network/Setup Co-op Sync")]
    static void Menu() => Debug.Log(Run());

    public static void RunBatch()
    {
        string report;
        try { report = Run(); }
        catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); return; }
        Debug.Log(report);
        EditorApplication.Exit(report.Contains("ERROR") ? 1 : 0);
    }

    public static string Run()
    {
        var sb = new StringBuilder("[CoopSyncSetup]\n");
        SetupLootPrefabs(sb);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorApplication.ExecuteMenuItem("Tools/Fusion/Rebuild Prefab Table");
        SetupHeistScene(sb);
        SetupBuildSettings(sb);
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    // ------------------------------------------------------------------ лут
    static void SetupLootPrefabs(StringBuilder sb)
    {
        var paths = AssetDatabase.FindAssets("t:Prefab", new[] { LootDir }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();
        foreach (var path in paths)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!root.GetComponent<LootItem>()) { sb.AppendLine($"  skip {path}: no LootItem"); continue; }
                var no = Ensure<NetworkObject>(root);
                SetFlags(no, masterClient: false, allowOverride: true, destroyOnLeave: false);
                var nt = Ensure<NetworkTransform>(root);
                var ps = nt.PhysicsSettings;
                ps.SetForecastDisabledProxyRigidbodiesToKinematic = true;
                nt.PhysicsSettings = ps;
                // лут двигает физика Unity (FixedUpdate), а не FixedUpdateNetwork — у владельца без интерполяции, чтобы предмет не отставал от руки
                nt.ConfigFlags |= NetworkTransform.NetworkTransformFlags.DisableSharedModeInterpolation;
                Ensure<NetworkLootItem>(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                sb.AppendLine($"  loot {System.IO.Path.GetFileNameWithoutExtension(path)}: NetworkObject[{no.Flags}] + NetworkTransform + NetworkLootItem");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        if (paths.Length != 6) sb.AppendLine($"  ERROR: expected 6 loot prefabs, found {paths.Length}");
    }

    // ------------------------------------------------------------------ сцена ограбления
    static void SetupHeistScene(StringBuilder sb)
    {
        var scene = EditorSceneManager.OpenScene(HeistPath, OpenSceneMode.Single);
        var state = Object.FindAnyObjectByType<HeistNetworkState>(FindObjectsInactive.Include);
        if (!state)
        {
            var go = new GameObject("HeistNetworkState");
            go.AddComponent<NetworkObject>();
            state = go.AddComponent<HeistNetworkState>();
            sb.AppendLine("  scene: + HeistNetworkState");
        }
        var stateNo = state.GetComponent<NetworkObject>();
        SetFlags(stateNo, masterClient: true, allowOverride: false, destroyOnLeave: false);
        EditorUtility.SetDirty(stateNo);

        var loot = Object.FindObjectsByType<LootItem>(FindObjectsInactive.Include);
        int net = loot.Count(l => l.GetComponent<NetworkLootItem>() && l.GetComponent<NetworkObject>() && l.GetComponent<NetworkTransform>());
        sb.AppendLine($"  scene: loot {loot.Length}, networked {net}, glass {Object.FindObjectsByType<BreakableGlass>(FindObjectsInactive.Include).Length}");
        if (net != loot.Length) sb.AppendLine("  ERROR: some scene loot has no network components");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorApplication.ExecuteMenuItem("Tools/Fusion/Scene/Bake Scene Objects");
        if (!EditorSceneManager.SaveScene(scene)) sb.AppendLine("  ERROR: scene save failed");

        int baked = Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include).Count(n => n.SortKey != 0);
        int total = Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include).Length;
        sb.AppendLine($"  scene: NetworkObjects {total}, baked (SortKey) {baked}, state flags [{stateNo.Flags}]");
        if (baked != total) sb.AppendLine("  ERROR: not all scene NetworkObjects are baked");
    }

    // ------------------------------------------------------------------ Build Settings
    static void SetupBuildSettings(StringBuilder sb)
    {
        var scenes = EditorBuildSettings.scenes;
        foreach (var s in scenes) if (s.path == SampleScenePath) s.enabled = false;
        EditorBuildSettings.scenes = scenes;
        sb.AppendLine("  build: " + string.Join(", ", EditorBuildSettings.scenes.Select(s => $"{System.IO.Path.GetFileNameWithoutExtension(s.path)}={(s.enabled ? "on" : "off")}")));
    }

    // ------------------------------------------------------------------ утилиты
    static T Ensure<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static void SetFlags(NetworkObject no, bool masterClient, bool allowOverride, bool destroyOnLeave)
    {
        var f = no.Flags.SetCurrentVersion();
        f = masterClient ? f | NetworkObjectFlags.MasterClientObject : f & ~NetworkObjectFlags.MasterClientObject;
        f = allowOverride ? f | NetworkObjectFlags.AllowStateAuthorityOverride : f & ~NetworkObjectFlags.AllowStateAuthorityOverride;
        f = destroyOnLeave ? f | NetworkObjectFlags.DestroyWhenStateAuthorityLeaves : f & ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
        no.Flags = f;
    }
}
