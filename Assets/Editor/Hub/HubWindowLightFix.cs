using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Окна MainHub без жёстких «объёмных лучей»: меши LightShaft выключаются (в них больше не упираешься
/// взглядом), вместо них у каждого окна — один мягкий Spot Light с плавным спадом, направленный туда же,
/// куда падал луч. Идемпотентно. Меню: Tools/Heist/Hub/Soft Window Light
/// </summary>
public static class HubWindowLightFix
{
    public static readonly Color Daylight = new Color(1f, 0.9f, 0.76f);

    [MenuItem("Tools/Heist/Hub/Soft Window Light")]
    static void Menu() => Debug.Log(Run(true));

    public static string Run(bool openScene)
    {
        if (openScene) EditorSceneManager.OpenScene("Assets/Scenes/MainHub.unity", OpenSceneMode.Single);
        var windows = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Where(t => t.name.StartsWith("Window_") && t.parent && t.parent.name == "Windows").ToArray();
        int off = 0, spots = 0;
        foreach (var w in windows)
        {
            Vector3 dir = Vector3.down;
            foreach (var s in w.GetComponentsInChildren<Transform>(true).Where(t => t.name == "LightShaft" || t.name.Contains("GodRay") || t.name.Contains("LightBeam")))
            {
                dir = s.forward;                                     // куда падал луч
                if (s.gameObject.activeSelf) { s.gameObject.SetActive(false); off++; }
            }
            var old = w.Find("WindowSoftLight"); if (old) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("WindowSoftLight");
            go.transform.SetParent(w, false);
            go.transform.position = w.position + w.forward * 0.5f;
            go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            AddSpot(go);
            spots++;
        }
        if (openScene) { EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); }
        return $"[WindowLight] windows {windows.Length}: shafts off {off}, soft spots {spots}";
    }

    /// Мягкий дневной спот: широкий конус, большой внутренний спад, умеренная яркость (без пересвета пола и стен).
    public static Light AddSpot(GameObject go)
    {
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot;
        l.color = Daylight;
        l.intensity = 1.1f;                                           // мягко, без пересвета пола и стен
        l.range = 9f;
        l.spotAngle = 75f;
        l.innerSpotAngle = 15f;                                      // плавный спад к краю пятна
        l.shadows = LightShadows.Soft;                               // мягкие тени (если доп. тени включены в URP-ассете)
        l.shadowStrength = 0.6f;
        l.renderMode = LightRenderMode.Auto;
        return l;
    }
}
