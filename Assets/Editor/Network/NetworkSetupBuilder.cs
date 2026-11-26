using System.IO;
using System.Linq;
using System.Text;
using Fusion;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Сетевая связка проекта GRABZ:
///  • PlayFab: Title ID + тихий вход (PlayFabAuthManager в MainHub);
///  • Photon: App ID Fusion и Voice в PhotonAppSettings;
///  • префаб NetworkRunner (Runner + NetworkSceneManagerDefault + Recorder + FusionVoiceClient);
///  • префаб NetworkGorillaAvatar (NetworkObject, модель Gorilla_Final, голос из рта);
///  • FusionNetworkManager в MainHub и HeistLevel_Test.
/// Меню: Tools/Heist/Network/Setup All.
/// </summary>
public static class NetworkSetupBuilder
{
    public const string PlayFabTitleId = "1FEC73";
    public const string FusionAppId = "bf8e9da6-ecbd-4dca-948d-27231b6911fb";
    public const string VoiceAppId = "67b15db2-fe80-4e37-b9f4-a28602a1898d";

    const string HubPath = "Assets/Scenes/MainHub.unity";
    const string HeistPath = "Assets/Scenes/HeistLevel_Test.unity";
    const string PlayerPrefab = "Assets/Prefabs/Player/VRGorillaPlayer.prefab";
    const string NetDir = "Assets/Prefabs/Network";
    const string RunnerPrefabPath = NetDir + "/NetworkRunner.prefab";
    const string AvatarPrefabPath = NetDir + "/NetworkGorillaAvatar.prefab";

    [MenuItem("Tools/Heist/Network/Setup All")]
    static void Menu() => Debug.Log(SetupAll());

    public static string SetupAll()
    {
        var sb = new StringBuilder();
        sb.AppendLine(SetupPhotonAppIds());
        sb.AppendLine(FixVoiceAndroidPlugins());
        sb.AppendLine(SetupPlayFab());
        var runner = BuildRunnerPrefab(); sb.AppendLine("[Runner] " + RunnerPrefabPath);
        var avatar = BuildAvatarPrefab(); sb.AppendLine("[Avatar] " + AvatarPrefabPath);
        EditorApplication.ExecuteMenuItem("Tools/Fusion/Rebuild Prefab Table");
        foreach (var s in new[] { HubPath, HeistPath }) sb.AppendLine(AddManager(s, runner, avatar));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    // ------------------------------------------------------------------ Photon
    public static string SetupPhotonAppIds()
    {
        var asset = AssetDatabase.FindAssets("PhotonAppSettings").Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".asset")).Select(AssetDatabase.LoadAssetAtPath<ScriptableObject>).FirstOrDefault(a => a);
        if (!asset) return "[Photon] PhotonAppSettings not found";
        var so = new SerializedObject(asset);
        so.FindProperty("AppSettings.AppIdFusion").stringValue = FusionAppId;
        so.FindProperty("AppSettings.AppIdVoice").stringValue = VoiceAppId;
        so.FindProperty("AppSettings.AppVersion").stringValue = "grabz-1";
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        return $"[Photon] {AssetDatabase.GetAssetPath(asset)}: Fusion + Voice App ID set";
    }

    /// У 32-битных Android-библиотек Voice устаревшие .meta (ошибка импорта) — пересохраняем импортёр.
    static string FixVoiceAndroidPlugins()
    {
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("", new[] { "Assets/Photon/PhotonVoice/PhotonVoiceLibs/Android" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            if (!p.EndsWith(".so")) continue;
            if (AssetImporter.GetAtPath(p) is PluginImporter pi) { EditorUtility.SetDirty(pi); pi.SaveAndReimport(); n++; }
        }
        return $"[Voice] re-saved {n} Android plugin importers";
    }

    // ------------------------------------------------------------------ PlayFab
    [MenuItem("Tools/Heist/Network/Setup PlayFab")]
    static void MenuPlayFab() => Debug.Log(SetupPlayFab());

    public static string SetupPlayFab()
    {
        var sb = new StringBuilder();
        var settings = AssetDatabase.FindAssets("t:PlayFabSharedSettings").Select(g => AssetDatabase.LoadAssetAtPath<PlayFabSharedSettings>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
        if (settings) { settings.TitleId = PlayFabTitleId; EditorUtility.SetDirty(settings); sb.Append($"TitleId={settings.TitleId}; "); }
        else sb.Append("no PlayFabSharedSettings; ");

        var scene = EditorSceneManager.OpenScene(HubPath, OpenSceneMode.Single);
        var mgr = Object.FindAnyObjectByType<PlayFabAuthManager>(FindObjectsInactive.Include);
        if (!mgr) mgr = new GameObject("PlayFabAuthManager").AddComponent<PlayFabAuthManager>();
        mgr.titleId = PlayFabTitleId;
        EditorUtility.SetDirty(mgr);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        sb.Append("PlayFabAuthManager in MainHub");
        return "[PlayFab] " + sb;
    }

    // ------------------------------------------------------------------ префабы
    static GameObject BuildRunnerPrefab()
    {
        EnsureFolder(NetDir);
        var go = new GameObject("NetworkRunner");
        try
        {
            go.AddComponent<NetworkRunner>();
            go.AddComponent<NetworkSceneManagerDefault>();
            go.AddComponent<NetworkObjectProviderDefault>();
            var rec = go.AddComponent<Recorder>();
            rec.TransmitEnabled = true;
            var voice = go.AddComponent<FusionVoiceClient>();
            var so = new SerializedObject(voice);
            var p = so.FindProperty("primaryRecorder") ?? so.FindProperty("PrimaryRecorder");
            if (p != null) p.objectReferenceValue = rec;
            var up = so.FindProperty("usePrimaryRecorder");
            if (up != null) up.boolValue = false; // запись идёт через VoiceNetworkObject нашей гориллы
            so.ApplyModifiedPropertiesWithoutUndo();
            return PrefabUtility.SaveAsPrefabAsset(go, RunnerPrefabPath);
        }
        finally { Object.DestroyImmediate(go); }
    }

    static GameObject BuildAvatarPrefab()
    {
        EnsureFolder(NetDir);
        var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
        var srcAvatar = player.transform.Find("GorillaAvatar");
        var vrSkin = player.GetComponent<PlayerSkinController>();

        var root = new GameObject("NetworkGorillaAvatar");
        try
        {
            root.AddComponent<NetworkObject>();
            var net = root.AddComponent<NetworkGorillaAvatar>();
            root.AddComponent<VoiceNetworkObject>();

            var av = Object.Instantiate(srcAvatar.gameObject, root.transform);
            av.name = "Avatar";
            av.transform.localPosition = Vector3.zero;
            foreach (var t in av.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;   // видна всем, включая голову
            foreach (var tip in av.GetComponentsInChildren<ComputerKeyTrigger>(true).Select(t => t.gameObject).ToArray()) Object.DestroyImmediate(tip);
            var rig = av.GetComponent<GorillaFinalRig>();
            var cos = av.GetComponent<PlayerCosmetics>();
            cos.useNetworkId = true;

            Transform Proxy(string n) { var t = new GameObject(n).transform; t.SetParent(root.transform, false); return t; }
            net.headProxy = Proxy("HeadProxy"); net.leftProxy = Proxy("LeftHandProxy"); net.rightProxy = Proxy("RightHandProxy");
            net.rig = rig; net.cosmetics = cos;

            var skin = root.AddComponent<PlayerSkinController>();
            skin.isNetworkProxy = true; skin.loadSavedColor = false;
            skin.furRenderers = av.GetComponentsInChildren<SkinnedMeshRenderer>(true).Cast<Renderer>().ToArray();
            skin.furMaterial = vrSkin ? vrSkin.furMaterial : null;
            skin.furColor = vrSkin ? vrSkin.furColor : Color.gray;
            net.skin = skin;

            var hands = root.AddComponent<VRHandPoseController>();
            hands.avatarRoot = rig.transform;
            hands.enabled = false;                  // позу пальцев задаёт сеть (SetPose), а не свои контроллеры
            net.hands = hands;

            // голос — изо рта (кость головы), 3D-звук, тише с расстоянием
            var head = rig.headBone;
            var mouth = new GameObject("Mouth_Voice");
            mouth.transform.SetParent(head ? head : root.transform, false);
            mouth.transform.position = (head ? head.position : Vector3.zero) + Vector3.forward * 0.15f + Vector3.down * 0.05f;
            var src = mouth.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 1.2f; src.maxDistance = 25f; src.dopplerLevel = 0f;
            mouth.AddComponent<Speaker>();

            // ник над головой — 3D TextMeshPro в мире (не Canvas)
            var tagGo = new GameObject("NameTag");
            tagGo.transform.SetParent(root.transform, false);
            var tmp = tagGo.AddComponent<TextMeshPro>();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MapKit.FontPath);
            if (font) tmp.font = font;
            tmp.fontSize = 1.4f; tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = new Vector2(2f, 0.3f);
            tmp.text = "GORILLA";
            net.nameTag = tmp;

            return PrefabUtility.SaveAsPrefabAsset(root, AvatarPrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    static string AddManager(string scenePath, GameObject runnerPrefab, GameObject avatarPrefab)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var mgr = Object.FindAnyObjectByType<FusionNetworkManager>(FindObjectsInactive.Include);
        if (!mgr) mgr = new GameObject("FusionNetworkManager").AddComponent<FusionNetworkManager>();
        mgr.runnerPrefab = runnerPrefab;
        mgr.avatarPrefab = avatarPrefab.GetComponent<NetworkObject>();
        mgr.autoConnect = true;
        EditorUtility.SetDirty(mgr);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"[{Path.GetFileNameWithoutExtension(scenePath)}] FusionNetworkManager ready";
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
