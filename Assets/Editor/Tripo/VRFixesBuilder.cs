using System;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

/// <summary>
/// Починки после теста в шлеме:
///  1) Кириллица: запасной динамический шрифт «LiberationSans SDF - Fallback» ссылался на материал/атлас
///     основного шрифта → русские буквы рисовались из чужой текстуры (надписи пропадали), а глифы ещё и
///     генерировались прямо в игре (подтормаживания). Делаем статичный шрифт с готовым атласом кириллицы.
///  2) ПК-режим: на DesktopTestPlayer ставится такая же горилла (видна в зеркале, своей камере не видна).
///  3) Зеркала: без MSAA и в меньшем разрешении.
/// Меню: Tools/Heist/Fix VR Text + Desktop Mirror Avatar.
/// </summary>
public static class VRFixesBuilder
{
    const string MainFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    const string CyrFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Cyrillic.asset";
    const string PrefabPath = "Assets/Prefabs/Player/VRGorillaPlayer.prefab";
    static readonly string[] Scenes = { "Assets/Scenes/MainHub.unity", "Assets/Scenes/HeistLevel_Test.unity" };

    [MenuItem("Tools/Heist/Fix VR Text + Desktop Mirror Avatar")]
    static void Menu() => Debug.Log(Run());

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine(BuildCyrillicFont());
        sb.AppendLine(FixMirrors());
        sb.AppendLine(FixFontMaterials());
        sb.AppendLine(RemovePlayerUI());
        foreach (var s in Scenes) sb.AppendLine(FixScene(s));
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    // ------------------------------------------------------------------ шрифт
    static string BuildCyrillicFont()
    {
        var main = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MainFontPath) ?? throw new Exception("no main font");
        var oldFallback = main.fallbackFontAssetTable?.FirstOrDefault(f => f && f.sourceFontFile);
        Font src = oldFallback ? oldFallback.sourceFontFile : null;
        if (!src)
        {
            var guid = AssetDatabase.FindAssets("LiberationSans t:Font").FirstOrDefault();
            src = guid != null ? AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid)) : null;
        }
        if (!src) throw new Exception("LiberationSans source font not found");

        // всё, чего нет в основном (латинском) атласе: кириллица + типографика
        var chars = new StringBuilder();
        for (int c = 0x400; c <= 0x45F; c++) chars.Append((char)c);
        chars.Append("№«»—–…·•€°×÷→←↑↓„“”‘’§±−");
        // HasCharacter() смотрит в кэш, куда TMP подкладывает символы из запасных шрифтов → берём саму таблицу
        var own = new System.Collections.Generic.HashSet<uint>(main.characterTable.Select(c => c.unicode));
        string need = new string(chars.ToString().Where(ch => !own.Contains(ch)).ToArray());

        if (File.Exists(CyrFontPath)) { AssetDatabase.DeleteAsset(CyrFontPath); }
        var fa = TMP_FontAsset.CreateFontAsset(src, 64, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
        fa.name = "LiberationSans SDF - Cyrillic";
        fa.TryAddCharacters(need, out string missing);
        fa.atlasPopulationMode = AtlasPopulationMode.Static;   // атлас готовый — в игре ничего не генерируется
        AssetDatabase.CreateAsset(fa, CyrFontPath);
        var tex = fa.atlasTexture;
        tex.name = fa.name + " Atlas";
        AssetDatabase.AddObjectToAsset(tex, fa);
        var mat = fa.material;
        mat.name = fa.name + " Material";
        mat.shader = main.material.shader;                    // как у основного (Mobile/Distance Field)
        mat.SetTexture("_MainTex", tex);
        mat.SetFloat("_TextureWidth", tex.width); mat.SetFloat("_TextureHeight", tex.height);
        mat.SetFloat("_GradientScale", fa.atlasPadding + 1);
        AssetDatabase.AddObjectToAsset(mat, fa);
        EditorUtility.SetDirty(fa);

        main.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fa };
        main.ReadFontAssetDefinition();   // сбросить кэш символов (в нём ссылки на старый запасной шрифт)
        fa.ReadFontAssetDefinition();
        EditorUtility.SetDirty(main);
        AssetDatabase.SaveAssets();
        return $"[Font] static Cyrillic atlas: {fa.characterTable.Count} chars ({need.Length} requested, missing='{missing}'), main fallback -> {fa.name} (was {(oldFallback ? oldFallback.name : "none")})";
    }

    /// На игроке (камера, XR Origin, руки) не должно быть НИКАКОГО текста и Canvas — только мир.
    static string RemovePlayerUI()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            int n = 0;
            foreach (var c in root.GetComponentsInChildren<Canvas>(true).Select(c => c.gameObject).Distinct().ToArray())
                if (c) { Object.DestroyImmediate(c); n++; }
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true).Select(t => t.gameObject).ToArray())
                if (t) { Object.DestroyImmediate(t); n++; }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            return $"[PlayerUI] removed {n} UI/text objects from VRGorillaPlayer";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// Обычный мировой шейдер TextMeshPro/Distance Field (не Mobile, не Overlay) без «перспективного фильтра»:
    /// фильтр размывает SDF-текст, когда на табличку смотрят под углом, — в шлеме угол меняется при каждом
    /// повороте головы, и текст «плывёт».
    static string FixFontMaterials()
    {
        var sh = Shader.Find("TextMeshPro/Distance Field");
        int n = 0;
        foreach (var path in new[] { MainFontPath, CyrFontPath })
        {
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (!fa || !fa.material) continue;
            var m = fa.material;
            m.shader = sh;
            m.SetFloat("_PerspectiveFilter", 0f);
            if (m.HasProperty("_Sharpness")) m.SetFloat("_Sharpness", 0.3f);
            m.SetFloat("_FaceDilate", 0.05f);
            ShaderUtilities.UpdateShaderRatios(m);
            EditorUtility.SetDirty(m);
            n++;
        }
        return $"[Font] {n} materials -> {sh.name}, perspective filter off";
    }

    // ------------------------------------------------------------------ зеркала
    static string FixMirrors()
    {
        int n = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:RenderTexture", new[] { "Assets" }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (!p.Contains("Mirror")) continue;
            var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(p);
            rt.Release();
            rt.antiAliasing = 1;
            rt.width = 512; rt.height = 560;
            EditorUtility.SetDirty(rt);
            n++;
        }
        return $"[Mirror] {n} render textures -> 512x560, no MSAA";
    }

    // ------------------------------------------------------------------ сцена
    static string FixScene(string path)
    {
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        var sb = new StringBuilder($"[{Path.GetFileNameWithoutExtension(path)}] ");

        // 1) все тексты перестроить с новым шрифтом
        int texts = 0;
        foreach (var t in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include))
        {
            foreach (var sm in t.GetComponentsInChildren<TMP_SubMesh>(true)) Object.DestroyImmediate(sm.gameObject);
            t.havePropertiesChanged = true;
            t.ForceMeshUpdate(true, true);
            EditorUtility.SetDirty(t);
            texts++;
        }
        sb.Append($"texts rebuilt={texts}; ");

        // 2) зеркальные камеры: проверка «смотрит ли игрок»
        foreach (var pe in Object.FindObjectsByType<ProximityCameraEnabler>(FindObjectsInactive.Include))
        {
            pe.distance = 6f; pe.renderEveryNthFrame = 2;
            var s = pe.transform.parent ? pe.transform.parent.Find("MirrorSurface") : null;
            if (s) pe.surface = s.GetComponent<Renderer>();
            pe.GetComponent<Camera>().allowMSAA = false;
            EditorUtility.SetDirty(pe);
        }

        // 3) горилла для ПК-игрока
        var desk = Object.FindAnyObjectByType<DesktopTestPlayer>(FindObjectsInactive.Include);
        if (!desk) { sb.Append("no DesktopTestPlayer"); return Save(scene, sb); }
        var cam = desk.GetComponentInChildren<Camera>(true);
        int localHead = LayerMask.NameToLayer("LocalHead");
        cam.transform.localPosition = new Vector3(0, GorillaFinalAvatarBuilder.EyeHeight, 0); // тот же рост, что в VR
        cam.cullingMask &= ~(1 << localHead);
        foreach (var n in new[] { "GorillaAvatar_Desktop", "AvatarTargets" })
        {
            var old = desk.transform.Find(n);
            if (old) Object.DestroyImmediate(old.gameObject);
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var srcAvatar = prefab.transform.Find("GorillaAvatar");
        var av = Object.Instantiate(srcAvatar.gameObject, desk.transform);
        av.name = "GorillaAvatar_Desktop";
        av.transform.localPosition = cam.transform.localPosition;
        av.transform.localRotation = Quaternion.identity;
        foreach (var t in av.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = localHead;
        // кончики пальцев для клавиатуры нужны только VR-игроку
        foreach (var tip in av.GetComponentsInChildren<ComputerKeyTrigger>(true).Select(t => t.gameObject).ToArray()) Object.DestroyImmediate(tip);

        var targets = new GameObject("AvatarTargets").transform;
        targets.SetParent(desk.transform, false);
        var lt = new GameObject("HandL").transform; lt.SetParent(targets, false);
        var rtg = new GameObject("HandR").transform; rtg.SetParent(targets, false);

        var rig = av.GetComponent<GorillaFinalRig>();
        rig.head = cam.transform;
        rig.left.target = lt; rig.left.rotationSource = lt;
        rig.right.target = rtg; rig.right.rotationSource = rtg;
        var drv = av.AddComponent<DesktopAvatarDriver>();
        drv.rig = rig; drv.playerRoot = desk.transform; drv.grabber = cam.GetComponent<VRGrabber>();
        drv.leftTarget = lt; drv.rightTarget = rtg;

        // перекраска и косметика — как у VR-игрока
        var vrSkin = prefab.GetComponent<PlayerSkinController>();
        var skin = desk.GetComponent<PlayerSkinController>() ?? desk.gameObject.AddComponent<PlayerSkinController>();
        skin.furRenderers = av.GetComponentsInChildren<SkinnedMeshRenderer>(true).Cast<Renderer>().ToArray();
        skin.furMaterial = vrSkin.furMaterial;
        skin.furColor = vrSkin.furColor;
        skin.presets = (Color[])vrSkin.presets.Clone();

        sb.Append($"desktop avatar: {skin.furRenderers.Length} renderers, cosmetics={av.GetComponent<PlayerCosmetics>()?.items.Length}, cam y={cam.transform.localPosition.y}");
        return Save(scene, sb);
    }

    static string Save(UnityEngine.SceneManagement.Scene scene, StringBuilder sb)
    {
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return sb.ToString();
    }
}
