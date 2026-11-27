using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools/Heist/Hub/Bus Trip (Network) — добавляет в MainHub сетевой объект BusTrip_Net (HubBusTrip,
/// NetworkObject с флагом Master Client Object) и связывает его с VanPortal и BusDeparture.
/// Идемпотентный: только создаёт/обновляет BusTrip_Net, автобус и остальную сцену не трогает.
/// Fusion запекает объект сцены при сохранении.
/// </summary>
public static class HubBusTripSetup
{
    [MenuItem("Tools/Heist/Hub/Bus Trip (Network)")]
    public static void Run()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "MainHub") { Debug.LogError("[BusTrip] Открой MainHub"); return; }
        var portal = Object.FindAnyObjectByType<VanPortal>(FindObjectsInactive.Include);
        var bus = Object.FindAnyObjectByType<BusDeparture>(FindObjectsInactive.Include);
        if (!portal || !bus) { Debug.LogError("[BusTrip] нет VanPortal или BusDeparture"); return; }

        GameObject go = null;
        foreach (var r in scene.GetRootGameObjects()) if (r.name == "BusTrip_Net") go = r;
        if (!go) go = new GameObject("BusTrip_Net");
        go.transform.SetPositionAndRotation(portal.transform.position, Quaternion.identity);

        var no = go.GetComponent<NetworkObject>() ?? go.AddComponent<NetworkObject>();
        var f = no.Flags.SetCurrentVersion();
        f |= NetworkObjectFlags.MasterClientObject;
        f &= ~NetworkObjectFlags.AllowStateAuthorityOverride;
        f &= ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
        no.Flags = f;

        var trip = go.GetComponent<HubBusTrip>() ?? go.AddComponent<HubBusTrip>();
        trip.portal = portal;
        trip.bus = bus;
        trip.countdown = 10f;
        trip.driveTime = 5f;
        trip.fadeTime = 1.5f;
        portal.countdown = 10f;
        portal.fadeTime = 1.5f;
        bus.driveTime = 5f;

        EditorUtility.SetDirty(go); EditorUtility.SetDirty(portal); EditorUtility.SetDirty(bus);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[BusTrip] готово: {go.name}, flags={no.Flags}");
    }
}
