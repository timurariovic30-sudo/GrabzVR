using System.Linq;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Точечная донастройка MainHub под общий отсчёт автобуса: добавляет один объект «HubVanSync»
/// (NetworkObject с Master Client Object + HubVanSync), запекает сетевые объекты сцены и сохраняет её.
/// MainHubBuilder не вызывается, остальное в сцене не трогается. Повторный запуск безопасен.
/// Меню: Tools/Heist/Hub/Setup Van Sync. Batchmode: -executeMethod HubVanSyncSetup.RunBatch
/// </summary>
public static class HubVanSyncSetup
{
    const string HubPath = "Assets/Scenes/MainHub.unity";

    [MenuItem("Tools/Heist/Hub/Setup Van Sync")]
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
        var scene = EditorSceneManager.OpenScene(HubPath, OpenSceneMode.Single);
        var report = "[HubVanSyncSetup]\n";
        var sync = Object.FindAnyObjectByType<HubVanSync>(FindObjectsInactive.Include);
        if (!sync)
        {
            var go = new GameObject("HubVanSync");
            go.AddComponent<NetworkObject>();
            sync = go.AddComponent<HubVanSync>();
            report += "  + HubVanSync\n";
        }
        sync.countdown = 15f;
        var no = sync.GetComponent<NetworkObject>();
        var f = no.Flags.SetCurrentVersion();
        f |= NetworkObjectFlags.MasterClientObject;
        f &= ~NetworkObjectFlags.AllowStateAuthorityOverride;
        f &= ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
        no.Flags = f;
        EditorUtility.SetDirty(no);
        EditorUtility.SetDirty(sync);

        var portal = Object.FindAnyObjectByType<VanPortal>(FindObjectsInactive.Include);
        if (!portal) report += "  ERROR: VanPortal not found\n";
        else
        {
            portal.boardingCountdown = 15f;
            EditorUtility.SetDirty(portal);
            report += $"  VanPortal: countdownText={(portal.countdownText ? portal.countdownText.name : "NONE")}, bus={(portal.bus ? "ok" : "NONE")}\n";
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorApplication.ExecuteMenuItem("Tools/Fusion/Scene/Bake Scene Objects");
        if (!EditorSceneManager.SaveScene(scene)) report += "  ERROR: scene save failed\n";
        var all = Object.FindObjectsByType<NetworkObject>(FindObjectsInactive.Include);
        report += $"  NetworkObjects {all.Length}, baked {all.Count(n => n.SortKey != 0)}, flags [{no.Flags}]\n";
        if (all.Any(n => n.SortKey == 0)) report += "  ERROR: not all scene NetworkObjects are baked\n";
        return report;
    }
}
