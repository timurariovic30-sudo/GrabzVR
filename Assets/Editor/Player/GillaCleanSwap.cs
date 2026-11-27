using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Замена модели игрока на чистую стилизованную гориллу Gorilla_GillaClean (Blender, исходник Assets/Art/Models/Source/build_gilla_clean.py).
/// Точечно и повторяемо — префаб игрока НЕ пересобирается (GorillaFinalAvatarBuilder не вызывается):
///  • в каждом аватаре с GorillaFinalRig (VRGorillaPlayer, NetworkGorillaAvatar, ПК-гориллы в MainHub и HeistLevel_Test)
///    дочерний «Model» заменяется новой моделью: масштаб по длине руки 0.55 м, глаза модели = начало аватара;
///  • всё, что висело на костях старой модели (косметика, глаза/рот, триггеры клавиш), переносится на кости
///    с теми же именами; ссылки компонентов (риг, пальцы, перекраска, реакции…) перенаправляются по имени;
///  • Body — слой Player, Head — слой LocalHead (своей камере не видна, в зеркале видна);
///  • материалы: Fur_Main → M_GillaFur (зернистая ретро-текстура, Smoothness 0, перекрашивается PlayerSkinController),
///    Skin_Grey → M_GillaSkin, Mouth_Dark → M_GillaMouth;
///  • затем заново ставятся 3D-глаза/брови/рот (PlayerReactionsSetup) и балаклава (FaceMaskBuilder) — по форме новой головы.
/// Меню: Tools/Heist/Player/Swap To Gilla Clean Model. Batchmode (без -nographics — нужны снимки): -executeMethod GillaCleanSwap.RunBatch
/// </summary>
public static class GillaCleanSwap
{
    const string ModelPath = "Assets/Art/Models/Gorilla_GillaClean.fbx";
    const string OutDir = "Assets/Art/Models/GillaClean";
    const string VRPrefab = "Assets/Prefabs/Player/VRGorillaPlayer.prefab";
    const string NetPrefab = "Assets/Prefabs/Network/NetworkGorillaAvatar.prefab";
    const string HubPath = "Assets/Scenes/MainHub.unity";
    const string HeistPath = "Assets/Scenes/HeistLevel_Test.unity";
    const float ArmLength = 0.55f;

    static readonly StringBuilder log = new StringBuilder();
    static Material mFur, mSkin, mMouth;

    [MenuItem("Tools/Heist/Player/Swap To Gilla Clean Model")]
    static void Menu() => Debug.Log(Run(true));

    public static void RunBatch()
    {
        string r;
        try { r = Run(true); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); return; }
        Debug.Log(r);
        EditorApplication.Exit(r.Contains("ERROR") ? 1 : 0);
    }

    public static string Run(bool withReactions)
    {
        log.Clear(); log.AppendLine("[GillaCleanSwap]");
        if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/Art/Models", "GillaClean");
        CreateMaterials();
        SetupImport();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) ?? throw new Exception("no " + ModelPath);

        foreach (var path in new[] { VRPrefab, NetPrefab })
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var rig in root.GetComponentsInChildren<GorillaFinalRig>(true)) Swap(rig, asset, root.transform, Path.GetFileNameWithoutExtension(path));
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        foreach (var path in new[] { HubPath, HeistPath })
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            int n = 0;
            foreach (var rig in Object.FindObjectsByType<GorillaFinalRig>(FindObjectsInactive.Include))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(rig)) continue;   // VR-игрок обновился через префаб
                Swap(rig, asset, null, Path.GetFileNameWithoutExtension(path)); n++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.AppendLine($"  {Path.GetFileNameWithoutExtension(path)}: scene avatars swapped {n}");
        }
        AssetDatabase.SaveAssets();

        if (withReactions)
        {
            log.AppendLine(PlayerReactionsSetup.Run());
            log.AppendLine(FaceMaskBuilder.Run());
        }
        RenderShots();
        return log.ToString();
    }

    // ------------------------------------------------------------------ материалы и импорт
    static void CreateMaterials()
    {
        string texPath = OutDir + "/T_GillaFur.png";
        var t = new Texture2D(256, 256, TextureFormat.RGB24, true);
        var rnd = new System.Random(11);
        for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++)
            {
                // ретро-зерно: мелкий шум + короткие «штрихи» шерсти по диагонали, средняя яркость ~0.85 (цвет даёт PlayerSkinController)
                float n = (float)rnd.NextDouble();
                float stroke = Mathf.PerlinNoise(x * 0.09f + y * 0.05f, y * 0.35f - x * 0.12f);
                float v = 0.82f + (n - 0.5f) * 0.16f + (stroke - 0.5f) * 0.14f;
                t.SetPixel(x, y, new Color(v, v, v));
            }
        t.Apply();
        File.WriteAllBytes(texPath, t.EncodeToPNG()); Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
        ti.maxTextureSize = 256; ti.filterMode = FilterMode.Point; ti.wrapMode = TextureWrapMode.Repeat; ti.mipmapEnabled = true; ti.SaveAndReimport();
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        Material Get(string name, Color c, Texture map, Vector2 tiling)
        {
            string p = $"{OutDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetTexture("_BaseMap", map); m.SetTextureScale("_BaseMap", tiling); m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0f); m.SetFloat("_Metallic", 0f);
            m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(m); return m;
        }
        mFur = Get("M_GillaFur", Color.white, tex, new Vector2(6, 6));
        mSkin = Get("M_GillaSkin", new Color(0.58f, 0.58f, 0.6f), tex, new Vector2(10, 10));
        mMouth = Get("M_GillaMouth", new Color(0.08f, 0.05f, 0.06f), null, Vector2.one);
    }

    static void SetupImport()
    {
        var mi = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        mi.isReadable = true;
        mi.animationType = ModelImporterAnimationType.Generic;
        mi.importAnimation = false; mi.importCameras = false; mi.importLights = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Fur_Main"), mFur);
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Skin_Grey"), mSkin);
        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "Mouth_Dark"), mMouth);
        mi.SaveAndReimport();
    }

    // ------------------------------------------------------------------ замена
    static void Swap(GorillaFinalRig rig, GameObject asset, Transform scopeRoot, string who)
    {
        int playerLayer = LayerMask.NameToLayer("Player"), localHead = LayerMask.NameToLayer("LocalHead");
        var avatar = rig.transform;
        var oldModel = avatar.Find("Model");
        if (!oldModel) { log.AppendLine($"  ERROR {who}: no Model under {avatar.name}"); return; }
        var oldHead = oldModel.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name == "Head");
        Bounds oldHb = oldHead ? Baked(oldHead) : new Bounds(avatar.position + Vector3.up * 0.05f, Vector3.one * 0.3f);
        var oldBones = new HashSet<Transform>(oldModel.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s => s.bones).Where(b => b));
        // кости = всё дерево старого скелета
        foreach (var b in oldBones.ToList()) for (var p = b.parent; p && p != oldModel; p = p.parent) oldBones.Add(p);

        // ---- новая модель
        var model = Object.Instantiate(asset).transform;
        model.name = "Model";
        model.SetParent(avatar, false);
        model.localPosition = Vector3.zero; model.localRotation = Quaternion.identity; model.localScale = Vector3.one;
        var map = model.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        Transform B(string n) => map.TryGetValue(n, out var t) ? t : throw new Exception($"{who}: no bone {n}");
        float armLen = Vector3.Distance(B("Left_UpperArm").position, B("Left_LowerArm").position) + Vector3.Distance(B("Left_LowerArm").position, B("Left_Hand").position);
        model.localScale = Vector3.one * (ArmLength / armLen);
        // лицом вперёд: глаза перед головой; иначе разворачиваем на 180°
        Vector3 eyeMid = (B("Left_Eye").position + B("Right_Eye").position) * 0.5f;
        if (Vector3.Dot(eyeMid - B("Head").position, avatar.forward) < 0f) model.localRotation = Quaternion.Euler(0, 180, 0);
        eyeMid = (B("Left_Eye").position + B("Right_Eye").position) * 0.5f;
        model.position += avatar.position - eyeMid;
        bool leftOk = Vector3.Dot(B("Left_Hand").position - avatar.position, avatar.right) < 0f;
        if (!leftOk) log.AppendLine($"  ERROR {who}: Left_Hand is on the right side");

        foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = playerLayer;
        SkinnedMeshRenderer body = null, head = null;
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smr.updateWhenOffscreen = false;
            // границы — относительно корневой кости (Hips) и с запасом на вытянутые руки: иначе голову отсекает вблизи (зеркало)
            smr.rootBone = model.GetComponentsInChildren<Transform>(true).First(t => t.name == "Hips");
            smr.localBounds = new Bounds(Vector3.zero, Vector3.one * 2.4f);
            smr.sharedMaterials = smr.sharedMaterials.Select(m => m && m.name.StartsWith("Fur") ? mFur : m && m.name.StartsWith("Mouth") ? mMouth : m && (m.name.StartsWith("Skin") || m.name.Contains("Gilla")) ? (m.name.Contains("Fur") ? mFur : m.name.Contains("Mouth") ? mMouth : mSkin) : m).ToArray();
            if (smr.name == "Body") body = smr;
            if (smr.name == "Head") { head = smr; smr.gameObject.layer = localHead; }
        }
        if (!body || !head) { log.AppendLine($"  ERROR {who}: Body/Head meshes not found"); return; }

        // ---- перенос навесного (косметика, лицо, триггеры) на кости с теми же именами
        var attachments = oldModel.GetComponentsInChildren<Transform>(true)
            .Where(t => t != oldModel && !oldBones.Contains(t) && !t.GetComponent<SkinnedMeshRenderer>() && t.parent && oldBones.Contains(t.parent)).ToList();
        int moved = 0;
        foreach (var t in attachments)
        {
            var target = map.TryGetValue(t.parent.name, out var nb) ? nb : (t.parent.name.Contains("Head") || t.parent.name.Contains("Neck") || t.parent.name.Contains("Eye") ? B("Head") : B("UpperChest"));
            t.SetParent(target, true); moved++;
        }

        // ---- ссылки компонентов: всё, что указывало внутрь старой модели, — на одноимённое в новой
        int remapped = 0;
        IEnumerable<Component> comps = scopeRoot
            ? scopeRoot.GetComponentsInChildren<Component>(true)
            : avatar.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Component>(true));
        foreach (var c in comps)
        {
            if (!c || c.transform.IsChildOf(oldModel) || c is Transform) continue;
            var so = new SerializedObject(c); var it = so.GetIterator(); bool changed = false;
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || !it.objectReferenceValue) continue;
                var o = it.objectReferenceValue;
                Transform ot = o is GameObject go ? go.transform : o is Component oc ? oc.transform : null;
                if (!ot || !ot.IsChildOf(oldModel)) continue;
                Object repl = null;
                if (ot == oldModel) repl = o is GameObject ? model.gameObject : (Object)model;
                else if (map.TryGetValue(ot.name, out var nt))
                    repl = o is GameObject ? nt.gameObject : o is Transform ? nt : (Object)nt.GetComponent(o.GetType());
                it.objectReferenceValue = repl; changed = true; remapped++;
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }
        Object.DestroyImmediate(oldModel.gameObject);

        // ---- риг рук и пальцы
        rig.headBone = B("Head");
        float palm = Vector3.Distance(B("Left_Hand").position, B("Left_MiddleProximal").position) * 0.9f;
        rig.left.upper = B("Left_UpperArm"); rig.left.lower = B("Left_LowerArm"); rig.left.hand = B("Left_Hand");
        rig.right.upper = B("Right_UpperArm"); rig.right.lower = B("Right_LowerArm"); rig.right.hand = B("Right_Hand");
        rig.left.palmOffset = rig.right.palmOffset = palm;
        rig.left.rotationOffset = Vector3.zero; rig.right.rotationOffset = Vector3.zero;   // чистый риг: ладонь вниз в T-позе
        rig.mirrorRightFromLeft = true;
        EditorUtility.SetDirty(rig);
        var root = scopeRoot ? scopeRoot : avatar.root;
        var pose = root.GetComponentInChildren<VRHandPoseController>(true);
        if (pose) { pose.avatarRoot = avatar; pose.left.hand = B("Left_Hand"); pose.right.hand = B("Right_Hand"); EditorUtility.SetDirty(pose); }
        foreach (var key in model.GetComponentsInChildren<Transform>(true).Where(t => t.name == "KeyTipTrigger"))
        {
            key.localPosition = Vector3.zero;
            var col = key.GetComponent<SphereCollider>(); if (col) col.radius = 0.018f / Mathf.Max(1e-4f, key.lossyScale.x);
        }
        var skin = root.GetComponentInChildren<PlayerSkinController>(true);
        if (skin && skin.furRenderers != null && skin.furRenderers.Length > 0 && skin.furRenderers.All(r => !r || r.transform.IsChildOf(model)))
        { skin.furRenderers = new Renderer[] { body, head }; skin.furMaterial = mFur; EditorUtility.SetDirty(skin); }

        // ---- головные уборы и цепь — по новой голове
        var newHb = Baked(head);
        var cos = avatar.GetComponent<PlayerCosmetics>();
        if (cos && cos.items != null)
            foreach (var item in cos.items.Where(i => i))
            {
                var slot = GameSession.SlotOf(item.name);
                if (slot == GameSession.CosmeticSlot.Headwear)
                {
                    // головной убор — на макушке новой головы (хохолок ~5 см торчит сквозь корону), по центру, чуть меньше (балаклава закрывает голову почти до верха,
                    // FaceMaskBuilder требует зазор корона ↔ маска ≥ 0)
                    var rs = item.GetComponentsInChildren<Renderer>(true);
                    if (rs.Length == 0) continue;
                    var ib = rs[0].bounds; foreach (var r in rs) ib.Encapsulate(r.bounds);
                    // ширина убора = 55 % ширины головы (повторный запуск даёт тот же размер; имя не трогаем — по нему ищет PlayerCosmetics)
                    float k = newHb.size.x * 0.55f / Mathf.Max(0.01f, ib.size.x);
                    item.transform.localScale *= k;
                    ib = rs[0].bounds; foreach (var r in rs) ib.Encapsulate(r.bounds);
                    item.transform.position += new Vector3(newHb.center.x - ib.center.x, newHb.max.y - 0.05f - ib.min.y, newHb.center.z - ib.center.z);
                }
                else if (slot == GameSession.CosmeticSlot.Neck)
                    item.transform.position += Vector3.up * (newHb.min.y - oldHb.min.y);
            }
        log.AppendLine($"  {who}: scale {model.localScale.x:F3}, attachments moved {moved}, refs remapped {remapped}, palm {palm:F3}, head {newHb.size}, tris body {body.sharedMesh.triangles.Length / 3} head {head.sharedMesh.triangles.Length / 3}{(leftOk ? "" : " LEFT/RIGHT MISMATCH")}");
    }

    static Bounds Baked(SkinnedMeshRenderer smr)
    {
        var m = new Mesh(); smr.BakeMesh(m, true);
        var v = m.vertices; var b = new Bounds(smr.transform.TransformPoint(v[0]), Vector3.zero);
        foreach (var p in v) b.Encapsulate(smr.transform.TransformPoint(p));
        Object.DestroyImmediate(m); return b;
    }

    // ------------------------------------------------------------------ снимки: спереди, сбоку, «в зеркале», кулак
    static void RenderShots()
    {
        Directory.CreateDirectory("production/qa/evidence");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VRPrefab);
        var p = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        foreach (var c in p.GetComponentsInChildren<Camera>(true)) c.enabled = false;
        var rig = p.GetComponentInChildren<GorillaFinalRig>(true);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.8f, 0.85f, 0.95f); RenderSettings.ambientEquatorColor = new Color(0.6f, 0.6f, 0.62f); RenderSettings.ambientGroundColor = new Color(0.35f, 0.33f, 0.3f);
        var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.2f; sun.transform.rotation = Quaternion.Euler(35, 150, 0);
        RenderSettings.sun = sun; DynamicGI.UpdateEnvironment();
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.localScale = Vector3.one * 2;
        var skinCtl = p.GetComponentInChildren<PlayerSkinController>(true);
        var furCol = new Color(0.45f, 0.3f, 0.2f);
        foreach (var r in skinCtl ? skinCtl.furRenderers : new Renderer[0])
        {
            var mats = r.sharedMaterials; var blk = new MaterialPropertyBlock();
            for (int i = 0; i < mats.Length; i++) if (mats[i] == mFur) { r.GetPropertyBlock(blk, i); blk.SetColor("_BaseColor", furCol); r.SetPropertyBlock(blk, i); }
        }
        // голову показываем (на снимке нет «своей» камеры) — слой LocalHead камера снимка видит
        var a = rig.transform; Vector3 c0 = a.position + Vector3.down * 0.25f;
        var cam = new GameObject("ShotCam").AddComponent<Camera>(); cam.backgroundColor = new Color(0.62f, 0.7f, 0.82f); cam.clearFlags = CameraClearFlags.SolidColor; cam.fieldOfView = 35;
        var rt = new RenderTexture(1000, 1000, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
        void Shot(string name, Vector3 pos, Vector3 look, bool mirror = false)
        {
            cam.transform.position = pos; cam.transform.LookAt(look);
            for (int i = 0; i < 2; i++) cam.Render();          // первый кадр в batchmode без света
            RenderTexture.active = rt; var t = new Texture2D(1000, 1000, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 1000, 1000), 0, 0); t.Apply();
            if (mirror) { var px = t.GetPixels(); var o = new Color[px.Length]; for (int y = 0; y < 1000; y++) for (int x = 0; x < 1000; x++) o[y * 1000 + x] = px[y * 1000 + 999 - x]; t.SetPixels(o); t.Apply(); }
            File.WriteAllBytes($"production/qa/evidence/gilla_{name}.png", t.EncodeToPNG()); Object.DestroyImmediate(t);
            log.AppendLine($"  shot production/qa/evidence/gilla_{name}.png");
        }
        // крупный план: голова + грудь
        Vector3 bust = a.position + Vector3.down * 0.16f;
        Shot("1_front", a.position + a.forward * 1.25f + Vector3.up * 0.05f, bust);
        Shot("2_side", a.position + a.right * 1.25f + a.forward * 0.05f + Vector3.up * 0.05f, bust);
        Shot("0_full", a.position + a.forward * 2.6f + Vector3.up * 0.1f, c0);
        // «в зеркале»: вид спереди на уровне глаз, отражённый по горизонтали — так игрок видит себя в зеркале Хаба
        Shot("3_mirror", a.position + a.forward * 1.3f, a.position + Vector3.down * 0.15f, true);
        // косметика: корона + балаклава вместе
        var cosm = a.GetComponent<PlayerCosmetics>();
        if (cosm && cosm.items != null)
        {
            foreach (var it in cosm.items) if (it) it.SetActive(it.name == "Crown" || it.name == "RobberMask");
            Shot("5_crown_mask", a.position + a.forward * 1.0f + a.right * 0.45f + Vector3.up * 0.15f, a.position + Vector3.up * 0.03f);
            foreach (var it in cosm.items) if (it) it.SetActive(false);
        }
        // кулак: сгибаем фаланги так же, как VRHandPoseController (ось = dir × ладонь, ладонь вниз)
        foreach (var side in new[] { "Left_", "Right_" })
            foreach (var f in new[] { "Thumb", "Index", "Middle" })
            {
                var bones = new[] { "Proximal", "Intermediate", "Distal" }.Select(n => a.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == side + f + n)).ToArray();
                float[] ang = f == "Thumb" ? new[] { 35f, 45f, 45f } : new[] { 80f, 95f, 70f };
                for (int k = 0; k < 3; k++)
                {
                    var b = bones[k]; if (!b) continue;
                    var next = k < 2 ? bones[k + 1] : (b.childCount > 0 ? b.GetChild(0) : null);
                    Vector3 dir = next ? (next.position - b.position).normalized : b.up;
                    Vector3 axis = Vector3.Cross(dir, -a.up); if (axis.sqrMagnitude < 1e-6f) continue;
                    b.rotation = Quaternion.AngleAxis(ang[k], axis.normalized) * b.rotation;
                }
            }
        // в batchmode скиннинг не пересчитывается от позы костей — «запекаем» согнутую позу в обычные меши
        foreach (var smr in a.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var baked = new Mesh(); smr.BakeMesh(baked, true);
            var go = new GameObject(smr.name + "_Baked"); go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = baked;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = smr.sharedMaterials;
            var blk = new MaterialPropertyBlock();
            for (int i = 0; i < smr.sharedMaterials.Length; i++) { smr.GetPropertyBlock(blk, i); mr.SetPropertyBlock(blk, i); }
            smr.enabled = false;
        }
        var lh = a.GetComponentsInChildren<Transform>(true).First(t => t.name == "Left_Hand");
        Shot("4_fist", lh.position + a.forward * 0.45f + Vector3.up * 0.12f - a.right * 0.05f, lh.position - a.right * 0.05f);
        RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt);
    }
}
