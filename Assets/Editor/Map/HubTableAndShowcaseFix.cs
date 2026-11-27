using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Точечные правки MainHub (MainHubBuilder не вызывается), идемпотентно:
///  • стол планирования: остаются две кнопки — зелёная «КЛАССИЧЕСКОЕ ОГРАБЛЕНИЕ БАНКА» и красная
///    «ОГРАБЛЕНИЕ ХАРДКОР»; табло над автобусом (ModeBoard) красит текст и полосы FrameT/FrameB в цвет режима
///    (HubController.modeBoardBars), фон панели не меняется;
///  • витрина косметики: 4 места — корона, чёрная балаклава, белая балаклава, цепь
///    (образцы балаклав ставит FaceMaskBuilder в Sample_1 / Sample_3).
/// Меню: Tools/Heist/Hub/Planning Table + Showcase
/// </summary>
public static class HubTableAndShowcaseFix
{
    [MenuItem("Tools/Heist/Hub/Planning Table + Showcase")]
    static void Menu() => Debug.Log(Run(true));

    public static string Run(bool openScene)
    {
        if (openScene) EditorSceneManager.OpenScene("Assets/Scenes/MainHub.unity", OpenSceneMode.Single);
        var log = new System.Text.StringBuilder("[HubTableAndShowcase]\n");
        var hub = Object.FindAnyObjectByType<HubController>(FindObjectsInactive.Include);
        if (!hub) return "ERROR: HubController not found";
        Transform Find(string path) => GameObject.Find(path)?.transform;

        // ---------------------------------------------------------------- стол: 2 режима
        var panel = Find("MainHubMap/Dynamic/ModeConsole/Panel");
        if (!panel) log.AppendLine("  ERROR: ModeConsole/Panel not found");
        else
        {
            foreach (int i in new[] { 1, 3 })
                foreach (var n in new[] { $"Button_Mode_{i}", $"Label_{i}", $"Lamp_{i}" })
                {
                    var t = panel.Find(n);
                    if (t) { Object.DestroyImmediate(t.gameObject); log.AppendLine($"  removed {n}"); }
                }
            foreach (var (i, x) in new[] { (0, 0.62f), (2, -0.62f) })   // консоль развёрнута к игроку: +X — слева
            {
                var local = new Vector3(x, 0.025f, -0.08f);
                var btn = panel.Find($"Button_Mode_{i}");
                if (btn)
                {
                    btn.localPosition = local; btn.localScale = Vector3.one * 1.35f;
                    var pb = btn.GetComponent<PushButton>();
                    if (pb) { pb.label = GameSession.ModeTitles[i]; EditorUtility.SetDirty(pb); }
                }
                var lamp = panel.Find($"Lamp_{i}");
                if (lamp) lamp.localPosition = local + new Vector3(0, 0.03f, -0.27f);
                var label = panel.Find($"Label_{i}")?.GetComponent<TextMeshPro>();
                if (label)
                {
                    label.text = GameSession.ModeTitles[i];
                    label.color = HubController.ModeColor((GameSession.Mode)i);
                    var rt = label.rectTransform;
                    var p = local + new Vector3(0, 0.03f, 0.27f);
                    rt.anchoredPosition3D = p; rt.localPosition = p;
                    rt.sizeDelta = new Vector2(1.15f, 0.22f);
                    label.fontSizeMax = 1.2f; label.fontSize = 1.2f;
                    EditorUtility.SetDirty(label);
                }
            }
            var lamps = hub.modeLamps ?? new Renderer[4];
            if (lamps.Length < 4) System.Array.Resize(ref lamps, 4);
            lamps[1] = null; lamps[3] = null;
            hub.modeLamps = lamps;
            log.AppendLine("  planning table: 2 buttons (classic green, hardcore red)");
        }

        // табло над автобусом: полосы красятся вместе с текстом
        var bars = new[] { "ModeBoard_FrameT", "ModeBoard_FrameB" }
            .Select(n => Find("MainHubMap/Static/" + n)?.GetComponent<Renderer>()).Where(r => r).ToArray();
        foreach (var b in bars) GameObjectUtility.SetStaticEditorFlags(b.gameObject, 0);   // MaterialPropertyBlock без статического батча
        hub.modeBoardBars = bars;
        if (hub.modeBoardTitle)
        {
            hub.modeBoardTitle.text = GameSession.ModeTitles[0];
            hub.modeBoardTitle.color = HubController.ModeColor(GameSession.Mode.Classic);
            EditorUtility.SetDirty(hub.modeBoardTitle);
        }
        EditorUtility.SetDirty(hub);
        log.AppendLine($"  mode board bars: {bars.Length}");

        // ---------------------------------------------------------------- витрина: 4 предмета
        var show = Find("MainHubMap/Dynamic/CosmeticShowcase");
        if (!show) log.AppendLine("  ERROR: CosmeticShowcase not found");
        else
        {
            var spanel = show.Find("Panel");
            // убрать прошлую копию белой балаклавы
            foreach (var n in new[] { "Stand_3", "Sample_3", "PriceTag_3", "Stand_4", "Sample_4", "PriceTag_4" }) { var t = show.Find(n); if (t) Object.DestroyImmediate(t.gameObject); }
            foreach (var n in new[] { "Button_TryOn_3", "Button_TryOn_4" }) { var oldBtn = spanel ? spanel.Find(n) : null; if (oldBtn) Object.DestroyImmediate(oldBtn.gameObject); }

            // клон «чёрной» позиции → белая
            Transform Clone(Transform src, string name)
            {
                var c = Object.Instantiate(src.gameObject, src.parent).transform;
                c.name = name; c.localPosition = src.localPosition; c.localRotation = src.localRotation; c.localScale = src.localScale;
                return c;
            }
            Clone(show.Find("Stand_1"), "Stand_3");
            Clone(show.Find("Sample_1"), "Sample_3");
            Clone(show.Find("PriceTag_1"), "PriceTag_3");
            var wbtn = Clone(spanel.Find("Button_TryOn_1"), "Button_TryOn_3");
            var wpb = wbtn.GetComponent<PushButton>();
            while (wpb.onPressed.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(wpb.onPressed, 0);
            UnityEventTools.AddVoidPersistentListener(wpb.onPressed, hub.TryOnRobberMaskWhite);
            wpb.label = "БЕЛАЯ БАЛАКЛАВА";
            var cap = wbtn.Find("Cap")?.GetComponent<MeshRenderer>();
            if (cap)
            {
                const string capPath = "Assets/Art/Materials/Map/M_Btn_BalaclavaWhite.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(capPath);
                if (!m) { m = new Material(cap.sharedMaterial); AssetDatabase.CreateAsset(m, capPath); }
                m.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.95f));
                m.SetColor("_EmissionColor", new Color(0.35f, 0.35f, 0.38f));
                EditorUtility.SetDirty(m);
                cap.sharedMaterial = m;
            }
            var bpb = spanel.Find("Button_TryOn_1").GetComponent<PushButton>(); bpb.label = "ЧЁРНАЯ БАЛАКЛАВА"; EditorUtility.SetDirty(bpb);

            // серая балаклава (#6E7178) — ещё одна копия «чёрной» позиции
            Clone(show.Find("Stand_1"), "Stand_4");
            Clone(show.Find("Sample_1"), "Sample_4");
            Clone(show.Find("PriceTag_1"), "PriceTag_4");
            var gbtn = Clone(spanel.Find("Button_TryOn_1"), "Button_TryOn_4");
            var gpb = gbtn.GetComponent<PushButton>();
            while (gpb.onPressed.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(gpb.onPressed, 0);
            UnityEventTools.AddVoidPersistentListener(gpb.onPressed, hub.TryOnRobberMaskGray);
            gpb.label = "СЕРАЯ БАЛАКЛАВА";
            var gcap = gbtn.Find("Cap")?.GetComponent<MeshRenderer>();
            if (gcap)
            {
                const string gPath = "Assets/Art/Materials/Hub/M_Hub_Btn_BalaclavaGray.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(gPath);
                if (!m) { m = new Material(gcap.sharedMaterial); AssetDatabase.CreateAsset(m, gPath); }
                m.SetColor("_BaseColor", new Color32(0x6E, 0x71, 0x78, 0xFF));
                m.SetColor("_EmissionColor", new Color(0.15f, 0.15f, 0.17f));
                EditorUtility.SetDirty(m);
                gcap.sharedMaterial = m;
            }

            // раскладка слева направо (для зрителя): корона, чёрная, белая, цепь
            var order = new[] { (0, 0.88f), (1, 0.44f), (3, 0f), (4, -0.44f), (2, -0.88f) };
            string[] tags = { "КОРОНА", "ЧЁРНАЯ БАЛАКЛАВА", "ЗОЛОТАЯ ЦЕПЬ", "БЕЛАЯ БАЛАКЛАВА", "СЕРАЯ БАЛАКЛАВА" };
            foreach (var (i, x) in order)
            {
                void SetX(Transform t) { if (!t) return; var p = t.localPosition; p.x = x; t.localPosition = p; if (t is RectTransform rt) rt.anchoredPosition3D = p; }
                SetX(show.Find($"Stand_{i}")); SetX(show.Find($"Sample_{i}")); SetX(spanel.Find($"Button_TryOn_{i}"));
                var tag = show.Find($"PriceTag_{i}")?.GetComponent<TextMeshPro>();
                if (tag)
                {
                    SetX(tag.transform);
                    tag.text = tags[i] + "\n<size=70%>ПРИМЕРИТЬ БЕСПЛАТНО</size>";
                    tag.rectTransform.sizeDelta = new Vector2(0.42f, 0.2f);
                    EditorUtility.SetDirty(tag);
                }
            }
            log.AppendLine("  showcase: crown, black / white / gray balaclava, chain");
        }
        EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
        if (openScene) EditorSceneManager.SaveScene(hub.gameObject.scene);
        return log.ToString();
    }
}
