using System;
using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

/// <summary>
/// Собирает VR-игрока (GorillaLocomotion + XR Origin), слой Player, OpenXR, XR Device Simulator и PlayerModeSwitcher
/// в сцене HeistLevel_Test. Меню: Tools > Heist > Setup VR Player. Повторный запуск безопасен.
/// </summary>
public static class HeistVRPlayerSetup
{
    const string ScenePath = "Assets/Scenes/HeistLevel_Test.unity";
    const string PrefabFolder = "Assets/Prefabs/Player";
    const string PrefabPath = PrefabFolder + "/VRGorillaPlayer.prefab";
    const string PlayerLayerName = "Player";

    [MenuItem("Tools/Heist/Setup VR Player")]
    public static void SetupVRPlayer()
    {
        try
        {
            int playerLayer = EnsureLayer(PlayerLayerName);
            SetupCollisionMatrix(playerLayer);
            SetupOpenXR();
            var prefab = BuildVRPlayerPrefab(playerLayer);
            SetupScene(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[HeistVR] Done.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    // ------------------------------------------------------------ layers & physics

    static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);
        if (existing >= 0) return existing;

        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(p.stringValue))
            {
                p.stringValue = name;
                tm.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[HeistVR] Layer '{name}' = {i}");
                return i;
            }
        }
        throw new Exception("No free user layer for " + name);
    }

    static void SetupCollisionMatrix(int playerLayer)
    {
        // Player ↔ Player: не сталкиваются (руки/голова/тело не цепляются друг за друга).
        // Player ↔ всё остальное (Default: стены, пол, столы) — сталкиваются.
        for (int i = 0; i < 32; i++)
            Physics.IgnoreLayerCollision(playerLayer, i, i == playerLayer);

        var dm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
        var matrix = dm.FindProperty("m_LayerCollisionMatrix");
        for (int i = 0; i < matrix.arraySize && i < 32; i++)
        {
            var el = matrix.GetArrayElementAtIndex(i);
            uint bits = (uint)el.longValue;
            if (i == playerLayer) bits = 0xFFFFFFFF & ~(1u << playerLayer);
            else bits |= 1u << playerLayer;
            el.longValue = bits;
        }
        dm.ApplyModifiedPropertiesWithoutUndo();
    }

    // ------------------------------------------------------------ OpenXR

    static void SetupOpenXR()
    {
        foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
        {
            try
            {
                var settings = GetOrCreateXRGeneralSettings(group);
                settings.InitManagerOnStart = true;
                XRPackageMetadataStore.AssignLoader(settings.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group);
                EditorUtility.SetDirty(settings);

                UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(group);
                var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                if (oxr != null)
                {
                    EnableFeature(oxr, "UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile");
                    EnableFeature(oxr, "UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchPlusControllerProfile");
                    EnableFeature(oxr, "UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchProControllerProfile");
                    if (group == BuildTargetGroup.Android)
                        EnableFeature(oxr, "UnityEngine.XR.OpenXR.Features.MetaQuestSupport.MetaQuestFeature");
                    EditorUtility.SetDirty(oxr);
                }
                Debug.Log($"[HeistVR] OpenXR configured for {group}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HeistVR] OpenXR setup for {group} skipped: {e.Message}");
            }
        }
    }

    static void EnableFeature(OpenXRSettings oxr, string typeName)
    {
        foreach (var f in oxr.GetFeatures())
            if (f != null && f.GetType().FullName == typeName) { f.enabled = true; EditorUtility.SetDirty(f); return; }
        Debug.Log($"[HeistVR] OpenXR feature not found (ok): {typeName}");
    }

    static XRGeneralSettings GetOrCreateXRGeneralSettings(BuildTargetGroup group)
    {
        const string folder = "Assets/XR";
        const string path = folder + "/XRGeneralSettingsPerBuildTarget.asset";
        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
        if (perTarget == null)
        {
            perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            if (perTarget == null)
            {
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, path);
            }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
        }
        string perTargetPath = AssetDatabase.GetAssetPath(perTarget);

        var settings = perTarget.SettingsForBuildTarget(group);
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
            settings.name = $"{group} Settings";
            perTarget.SetSettingsForBuildTarget(group, settings);
            AssetDatabase.AddObjectToAsset(settings, perTargetPath);
        }
        if (settings.Manager == null)
        {
            var mgr = ScriptableObject.CreateInstance<XRManagerSettings>();
            mgr.name = $"{group} Providers";
            settings.Manager = mgr;
            AssetDatabase.AddObjectToAsset(mgr, perTargetPath);
        }
        EditorUtility.SetDirty(perTarget);
        return settings;
    }

    // ------------------------------------------------------------ prefab

    static GameObject BuildVRPlayerPrefab(int playerLayer)
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
            AssetDatabase.CreateFolder("Assets/Prefabs", "Player");
        }

        var root = new GameObject("VRGorillaPlayer") { tag = "Player" };

        // Физическое тело
        var rb = root.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.None; // GorillaLocomotion сам двигает transform
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // XR Origin
        var offset = new GameObject("Camera Offset");
        offset.transform.SetParent(root.transform, false);

        var head = new GameObject("Main Camera") { tag = "MainCamera" };
        head.transform.SetParent(offset.transform, false);
        head.transform.localPosition = new Vector3(0, 1.6f, 0);
        var cam = head.AddComponent<Camera>();
        cam.nearClipPlane = 0.01f;
        head.AddComponent<AudioListener>();
        AddPoseDriver(head, "<XRHMD>/centerEyePosition", "<XRHMD>/centerEyeRotation", "<XRHMD>/trackingState");
        var headCol = head.AddComponent<SphereCollider>();
        headCol.radius = 0.15f;

        var leftCtrl = Controller(offset.transform, "Left Controller", "LeftHand", new Vector3(-0.25f, 1.1f, 0.3f));
        var rightCtrl = Controller(offset.transform, "Right Controller", "RightHand", new Vector3(0.25f, 1.1f, 0.3f));

        var origin = root.AddComponent<XROrigin>();
        origin.Origin = root;
        origin.CameraFloorOffsetObject = offset;
        origin.Camera = cam;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        origin.CameraYOffset = 1.6f;

        // Тело (капсула под головой)
        var body = new GameObject("BodyCollider");
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0, 1.15f, 0);
        var bodyCol = body.AddComponent<CapsuleCollider>();
        bodyCol.radius = 0.2f;
        bodyCol.height = 0.8f;
        var follower = body.AddComponent<GorillaBodyFollower>();
        follower.head = head.transform;

        // Сферы-руки
        var leftHand = HandSphere(root.transform, "LeftHandFollower", leftCtrl);
        var rightHand = HandSphere(root.transform, "RightHandFollower", rightCtrl);
        AddGrabber(leftHand, VRGrabber.GrabInput.LeftHandTrigger, leftCtrl);
        AddGrabber(rightHand, VRGrabber.GrabInput.RightHandTrigger, rightCtrl);

        // GorillaLocomotion.Player (значения из оригинального примера Gorilla Locomotion)
        var gp = root.AddComponent<GorillaLocomotion.Player>();
        gp.headCollider = headCol;
        gp.bodyCollider = bodyCol;
        gp.leftHandFollower = leftHand.transform;
        gp.rightHandFollower = rightHand.transform;
        gp.leftHandTransform = leftCtrl;
        gp.rightHandTransform = rightCtrl;
        gp.velocityHistorySize = 8;
        gp.maxArmLength = 1.5f;
        gp.unStickDistance = 1f;
        gp.velocityLimit = 0.3f;
        gp.maxJumpSpeed = 6.5f;
        gp.jumpMultiplier = 1.1f;
        gp.minimumRaycastDistance = 0.05f;
        gp.defaultSlideFactor = 0.03f;
        gp.defaultPrecision = 0.995f;
        gp.locomotionEnabledLayers = 1 << 0; // только Default: пол, стены, столы, лут (но не Player)

        root.AddComponent<GorillaHeavySlowdown>();

        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = playerLayer;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        return prefab;
    }

    static Transform Controller(Transform parent, string name, string hand, Vector3 pos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        AddPoseDriver(go, $"<XRController>{{{hand}}}/devicePosition", $"<XRController>{{{hand}}}/deviceRotation", $"<XRController>{{{hand}}}/trackingState");
        return go.transform;
    }

    static void AddPoseDriver(GameObject go, string pos, string rot, string state)
    {
        var tpd = go.AddComponent<TrackedPoseDriver>();
        tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        tpd.positionInput = new InputActionProperty(new InputAction("Position", InputActionType.Value, pos, expectedControlType: "Vector3"));
        tpd.rotationInput = new InputActionProperty(new InputAction("Rotation", InputActionType.Value, rot, expectedControlType: "Quaternion"));
        tpd.trackingStateInput = new InputActionProperty(new InputAction("Tracking State", InputActionType.Value, state, expectedControlType: "Integer"));
    }

    static GameObject HandSphere(Transform parent, string name, Transform controller)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = controller.position;
        go.transform.localScale = Vector3.one * 0.1f; // = 2 × minimumRaycastDistance
        // Коллайдер руки — триггер: не мешает Gorilla-движению, но рука может задеть лазер.
        var col = go.GetComponent<SphereCollider>();
        col.isTrigger = true;
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/M_Wood_Dark.mat");
        if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    static void AddGrabber(GameObject hand, VRGrabber.GrabInput input, Transform controller)
    {
        var g = hand.AddComponent<VRGrabber>();
        g.input = input;
        g.rotationReference = controller;
        g.handGrabRadius = 0.15f;
    }

    // ------------------------------------------------------------ scene

    static void SetupScene(GameObject vrPrefab)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var level = GameObject.Find("HeistLevel")?.transform ?? throw new Exception("HeistLevel not found");
        var spawn = level.Find("PlayerSpawn");
        var spawnPos = spawn != null ? spawn.position : Vector3.zero;

        // VR-игрок (экземпляр префаба)
        var old = GameObject.Find("VRGorillaPlayer");
        if (old != null) UnityEngine.Object.DestroyImmediate(old);
        var vr = (GameObject)PrefabUtility.InstantiatePrefab(vrPrefab);
        vr.transform.SetPositionAndRotation(spawnPos, Quaternion.Euler(0, 45, 0));
        vr.SetActive(false);

        var desktop = GameObject.Find("DesktopTestPlayer") ?? throw new Exception("DesktopTestPlayer not found");

        // XR Device Simulator (из сэмпла XR Interaction Toolkit)
        var oldSim = GameObject.Find("XR Device Simulator");
        if (oldSim != null) UnityEngine.Object.DestroyImmediate(oldSim);
        GameObject sim = null;
        var simGuids = AssetDatabase.FindAssets("XR Device Simulator t:Prefab", new[] { "Assets/Samples" });
        foreach (var g in simGuids)
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (Path.GetFileNameWithoutExtension(p) != "XR Device Simulator") continue;
            sim = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(p));
            sim.name = "XR Device Simulator";
            sim.SetActive(false);
            Debug.Log("[HeistVR] Simulator prefab: " + p);
            break;
        }
        if (sim == null) Debug.LogWarning("[HeistVR] XR Device Simulator prefab not found in Assets/Samples");

        // Переключатель режимов
        var swGo = GameObject.Find("PlayerModeSwitcher") ?? new GameObject("PlayerModeSwitcher");
        if (!swGo.TryGetComponent<PlayerModeSwitcher>(out var sw)) sw = swGo.AddComponent<PlayerModeSwitcher>();
        sw.vrPlayer = vr;
        sw.desktopPlayer = desktop;
        sw.deviceSimulator = sim;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
