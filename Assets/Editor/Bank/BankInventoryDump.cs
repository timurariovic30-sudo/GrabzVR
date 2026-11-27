using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Диагностика модульного кита банка: нормали (нулевые/NaN/направления) и размеры мешей. Только чтение.
/// Batchmode: -executeMethod BankInventoryDump.KitNormals (пишет Logs/BankKitNormals.txt)</summary>
public static class BankInventoryDump
{
    /// Диагностика освещения превью: улица при разных настройках (тени солнца, плоский эмбиент).
    public static void LightDebug()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Art/GeneratedModels/Bank/BankQ/BankQ_Preview.unity");
        var sun = Object.FindObjectsByType<Light>(FindObjectsInactive.Include).First(l => l.type == LightType.Directional);
        var cam = new GameObject("c").AddComponent<Camera>(); cam.fieldOfView = 66; cam.farClipPlane = 600;
        cam.transform.position = new Vector3(-5f, 2.4f, -16.5f); cam.transform.LookAt(new Vector3(0, 5, 0));
        var rt = new RenderTexture(800, 450, 24); cam.targetTexture = rt;
        var sb = new StringBuilder($"sun {sun.transform.forward} shadows {sun.shadows} intensity {sun.intensity}\n");
        void Shot(string n) { cam.Render(); RenderTexture.active = rt; var t = new Texture2D(800, 450, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 800, 450), 0, 0); t.Apply(); System.IO.File.WriteAllBytes($"HubPreview/dbg_{n}.png", t.EncodeToPNG()); }
        Shot("a_asis");
        sun.shadows = LightShadows.None; Shot("b_noshadow");
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = Color.white; DynamicGI.UpdateEnvironment(); Shot("c_flatwhite");
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            if (r.bounds.Contains(new Vector3(0f, 3f, -5.3f)) || r.bounds.Contains(new Vector3(-6f, 3f, 0f))) sb.AppendLine($"contains: {r.name} bounds {r.bounds}");
        System.IO.File.WriteAllText("Logs/BankLightDebug.txt", sb.ToString());
        EditorApplication.Exit(0);
    }

    /// Инвентарь MainHub: корни сцены, игровые компоненты с путями и позициями, габариты корней. Только чтение → Logs/HubInventory.txt
    public static void HubDump()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/MainHub.unity");
        var sb = new StringBuilder();
        string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
        foreach (var root in scene.GetRootGameObjects())
        {
            var rs = root.GetComponentsInChildren<Renderer>(true);
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(root.transform.position, Vector3.zero);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            sb.AppendLine($"ROOT {root.name}{(root.activeSelf ? "" : " (off)")} @{root.transform.position} children {root.transform.childCount} renderers {rs.Length} colliders {root.GetComponentsInChildren<Collider>(true).Length} bounds {b.min}..{b.max}");
            foreach (Transform c in root.transform)
            {
                var cr = c.GetComponentsInChildren<Renderer>(true);
                var comps = c.GetComponents<MonoBehaviour>().Where(m => m).Select(m => m.GetType().Name);
                sb.AppendLine($"   {c.name} @({c.position.x:F1},{c.position.y:F1},{c.position.z:F1}) r{cr.Length} [{string.Join(",", comps)}]");
            }
        }
        sb.AppendLine("--- functional components");
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include).Where(m => m && !(m.GetType().Namespace ?? "").StartsWith("UnityEngine")))
        {
            var n = mb.GetType().Name;
            if (n.StartsWith("TextMeshPro") || n == "TMP_SubMesh") continue;
            var p = mb.transform.position;
            sb.AppendLine($"{n} : {Path(mb.transform)} @({p.x:F2},{p.y:F2},{p.z:F2})");
        }
        sb.AppendLine($"--- RenderSettings: skybox {RenderSettings.skybox?.name}, fog {RenderSettings.fog}, ambient {RenderSettings.ambientMode}");
        System.IO.File.WriteAllText("Logs/HubInventory.txt", sb.ToString());
        EditorApplication.Exit(0);
    }

    /// Состав статических групп Хаба (имя, габариты, слой, коллайдеры) → Logs/HubStatic.txt
    public static void HubStaticDump()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/MainHub.unity");
        var sb = new StringBuilder();
        foreach (var path in new[] { "MainHubMap/Static", "MainHubMap/Lights", "MainHubMap/Dynamic", "HubDecor/Static", "HubDecor/Lighting", "BusTunnel" })
        {
            var g = GameObject.Find(path);
            if (!g) { sb.AppendLine("NO " + path); continue; }
            sb.AppendLine("== " + path);
            foreach (Transform c in g.transform)
            {
                var rs = c.GetComponentsInChildren<Renderer>(true);
                var b = rs.Length > 0 ? rs[0].bounds : new Bounds(c.position, Vector3.zero);
                foreach (var r in rs) b.Encapsulate(r.bounds);
                var lights = c.GetComponentsInChildren<Light>(true).Length;
                var cols = c.GetComponentsInChildren<Collider>(true).Length;
                sb.AppendLine($"  {c.name} [{LayerMask.LayerToName(c.gameObject.layer)}] min({b.min.x:F1},{b.min.y:F1},{b.min.z:F1}) max({b.max.x:F1},{b.max.y:F1},{b.max.z:F1}) r{rs.Length} col{cols}{(lights > 0 ? " light" + lights : "")}{(c.gameObject.activeSelf ? "" : " OFF")}");
            }
        }
        System.IO.File.WriteAllText("Logs/HubStatic.txt", sb.ToString());
        EditorApplication.Exit(0);
    }

    public static void KitNormals()
    {
        var sb = new StringBuilder();
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Art/GeneratedModels/Bank/Modular" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh; var n = m.normals;
                int bad = n.Count(v => float.IsNaN(v.x) || v.sqrMagnitude < 0.5f);
                int zpos = n.Count(v => v.z > 0.9f), zneg = n.Count(v => v.z < -0.9f), ypos = n.Count(v => v.y > 0.9f);
                sb.AppendLine($"{System.IO.Path.GetFileNameWithoutExtension(path)}/{mf.name}: verts {m.vertexCount} normals {n.Length} bad {bad} +z {zpos} -z {zneg} +y {ypos} bounds {m.bounds.size} rootRot {go.transform.localRotation.eulerAngles} scale {mf.transform.lossyScale}");
            }
        }
        System.IO.File.WriteAllText("Logs/BankKitNormals.txt", sb.ToString());
        EditorApplication.Exit(0);
    }
}
