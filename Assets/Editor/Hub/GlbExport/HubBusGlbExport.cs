using System.Linq;
using GLTFast;
using GLTFast.Export;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Экспорт внешнего вида чёрного автобуса из MainHub в .glb (glTFast): кузов Bus_Modern с чёрным
/// матово-сатиновым металлом и тонированными стёклами, рамка входа с закрытой рольставней, ступенька,
/// LED-фары на бампере и задние габариты. Салон, маски Stencil, свет и коллайдеры не выгружаются.
/// Копия собирается во временной preview-сцене — открытая сцена не меняется.
/// Меню: Tools/Heist/Hub/Export Bus GLB → Загрузки/GRABZ_BlackBus.glb
/// </summary>
public static class HubBusGlbExport
{
    static System.Threading.Tasks.Task<bool> task;
    static UnityEngine.SceneManagement.Scene preview;
    public static string LastResult = "";

    public static string OutPath => System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads", "GRABZ_BlackBus.glb");

    /// Batchmode: -executeMethod HubBusGlbExport.ExportBatch (сам открывает MainHub и выходит по готовности).
    public static void ExportBatch()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/MainHub.unity", OpenSceneMode.Single);
        Export();
        EditorApplication.update += () =>
        {
            if (LastResult.StartsWith("OK") || LastResult.StartsWith("ERROR"))
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "claude", "glb_export.txt"), LastResult);
                EditorApplication.Exit(LastResult.StartsWith("OK") ? 0 : 1);
            }
        };
    }

    [MenuItem("Tools/Heist/Hub/Export Bus GLB")]
    public static void Export()
    {
        var bus = GameObject.Find("BlackBus")?.transform;
        if (!bus) { LastResult = "ERROR: BlackBus not in the open scene (open MainHub)"; Debug.LogError(LastResult); return; }
        preview = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("GRABZ_BlackBus");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
        foreach (var name in new[] { "Exterior", "HeadlightModule_L", "HeadlightModule_R", "LED_Tail_L", "LED_Tail_R" })
        {
            var src = bus.Find(name);
            if (!src) continue;
            var copy = Object.Instantiate(src.gameObject, root.transform);
            copy.name = name;
            copy.transform.localPosition = bus.InverseTransformPoint(src.position);
            copy.transform.localRotation = Quaternion.Inverse(bus.rotation) * src.rotation;
            copy.transform.localScale = src.lossyScale;
        }
        foreach (var l in root.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l.gameObject);
        foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t && (t.name == "LightBeam" || t.name == "DoorLabel")).ToArray()) Object.DestroyImmediate(t.gameObject);
        var shutter = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "RearShutter");
        if (shutter) shutter.localScale = Vector3.one;                     // ставня закрыта
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        // у glTFast падает экспорт одного меша на многих узлах (встроенный Cube) — каждому свою копию
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh) { var m = Object.Instantiate(mf.sharedMesh); m.name = mf.sharedMesh.name; mf.sharedMesh = m; }
        int tris = root.GetComponentsInChildren<MeshFilter>(true).Sum(m => m.sharedMesh ? m.sharedMesh.triangles.Length / 3 : 0);

        var export = new GameObjectExport(new ExportSettings { Format = GltfFormat.Binary, FileConflictResolution = FileConflictResolution.Overwrite },
                                          new GameObjectExportSettings { OnlyActiveInHierarchy = false, DisabledComponents = true });
        export.AddScene(new[] { root }, "GRABZ_BlackBus");
        LastResult = $"exporting {tris} tris…";
        task = export.SaveToFileAndDispose(OutPath);
        EditorApplication.update -= Wait; EditorApplication.update += Wait;
    }

    static void Wait()
    {
        if (task == null || !task.IsCompleted) return;
        EditorApplication.update -= Wait;
        bool ok = !task.IsFaulted && task.Result;
        LastResult = (ok ? "OK " : "ERROR ") + OutPath + " " + (System.IO.File.Exists(OutPath) ? new System.IO.FileInfo(OutPath).Length / 1024 + " KB" : "missing") + (task.Exception != null ? " " + task.Exception.GetBaseException().Message : "");
        Debug.Log("[BusGLB] " + LastResult);
        EditorSceneManager.ClosePreviewScene(preview);
    }
}
