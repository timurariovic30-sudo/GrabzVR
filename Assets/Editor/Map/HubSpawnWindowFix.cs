using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Засвет стартовой таблички (RulesSign, слева от спавна): южное окно над x = −2 бросало аддитивный
/// луч света (LightShaft) прямо между точкой спавна и табличкой — текст «выгорал».
/// Окно переносится левее (x = −5.2), луч падает на пол у стены, в стороне от таблички и от GRABZ OS.
/// Те же координаты прописаны в HubVisualOverhaul.BuildWindows — повторный апгрейд хаба их сохранит.
/// Идемпотентный. Меню: Tools/Heist/Hub/Fix Spawn Sign Window Glare
/// </summary>
public static class HubSpawnWindowFix
{
    public static readonly Vector3 WallPoint = new Vector3(-5.2f, 6.7f, -9.78f);
    public static readonly Vector3 FloorTarget = new Vector3(-4.9f, 0f, -6.6f);
    static readonly Vector3 SpawnEye = new Vector3(0f, 1.6f, -7f);

    [MenuItem("Tools/Heist/Hub/Fix Spawn Sign Window Glare")]
    static void Menu() => Debug.Log(Run(true));

    public static string Run(bool openScene)
    {
        if (openScene) EditorSceneManager.OpenScene("Assets/Scenes/MainHub.unity", OpenSceneMode.Single);
        var sign = GameObject.Find("MainHubMap/Static/RulesSign");
        var windows = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Where(t => t.name == "Window_S").ToArray();
        // окно, ближайшее к табличке (до фикса — x = −2, после — x = −5.2)
        var win = windows.OrderBy(w => Mathf.Abs(w.position.x - (sign ? sign.transform.position.x : -2.8f))).FirstOrDefault();
        if (!win || !sign) return "[SpawnWindow] ERROR: window or sign not found";
        var before = win.position;
        win.position = WallPoint;
        var shaft = win.Find("LightShaft");
        if (shaft)
        {
            var from = win.TransformPoint(new Vector3(0, 0, 0.2f));
            var dir = FloorTarget - from;
            shaft.SetPositionAndRotation(from + dir * 0.5f, Quaternion.LookRotation(dir, Vector3.up));
            var s = shaft.localScale; shaft.localScale = new Vector3(s.x, s.y, dir.magnitude);
        }
        EditorUtility.SetDirty(win);

        // проверка: луч не перекрывает взгляд со спавна на табличку и не касается её
        var sb = sign.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        var shb = shaft ? shaft.GetComponent<Renderer>().bounds : new Bounds();
        bool touchesSign = shaft && shb.Intersects(sb);
        bool blocksView = false;
        if (shaft)
            for (int i = 0; i <= 20; i++)
            {
                var p = Vector3.Lerp(SpawnEye, sb.center, i / 20f);
                // точный тест по повёрнутому объёму луча (единичный куб в осях LightShaft)
                var lp = shaft.InverseTransformPoint(p);
                if (Mathf.Abs(lp.x) <= 0.5f && Mathf.Abs(lp.y) <= 0.5f && Mathf.Abs(lp.z) <= 0.5f) blocksView = true;
            }
        EditorSceneManager.MarkSceneDirty(sign.scene);
        if (openScene) EditorSceneManager.SaveScene(sign.scene);
        return $"[SpawnWindow] Window_S {before:F2} → {win.position:F2}; sign bounds x {sb.min.x:F2}..{sb.max.x:F2}; shaft x {shb.min.x:F2}..{shb.max.x:F2}; " +
               $"touches sign={touchesSign}, blocks spawn view={blocksView}" + (touchesSign || blocksView ? " ERROR" : " OK");
    }
}
