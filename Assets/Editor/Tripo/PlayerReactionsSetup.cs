using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Живые реакции гориллы: рот в такт голосу (GorillaVoiceMouth), веки и брови (GorillaFaceEmotes),
/// нокаут-рэгдолл при падении (GorillaFallRagdoll).
///
/// Что делает (идемпотентно, можно запускать повторно):
///  • под костью Head пересоздаёт только объект FaceReactions: рот (полость, язык, зубы), веки, брови,
///    испуганные глаза. Точки берутся лучами по запечённой голове VR-гориллы (как у FaceMaskBuilder)
///    и хранятся в осях кости Head — одна разметка подходит всем копиям гориллы;
///  • добавляет/обновляет три компонента на объекте с GorillaFinalRig и заполняет ссылки;
///  • цели: VRGorillaPlayer.prefab, NetworkGorillaAvatar.prefab, ПК-гориллы в MainHub и HeistLevel_Test.
/// Ничего другого в префабах и сценах не трогает, билдеры не вызывает.
/// Меню: Tools/Heist/Player/Reactions (lip-sync, face emotes, fall ragdoll).
/// Снимки выражений: Tools/Heist/Player/Reactions Preview → production/qa/evidence/reactions_*.png
/// </summary>
public static class PlayerReactionsSetup
{
    const string VRPrefab = "Assets/Prefabs/Player/VRGorillaPlayer.prefab";
    const string NetPrefab = "Assets/Prefabs/Network/NetworkGorillaAvatar.prefab";
    const string HubPath = "Assets/Scenes/MainHub.unity";
    const string HeistPath = "Assets/Scenes/HeistLevel_Test.unity";
    const string MeshDir = "Assets/Art/Meshes/Reactions";
    const string MatDir = "Assets/Art/Materials/Player";
    const string RootName = "FaceReactions";
    const string EvidenceDir = "production/qa/evidence";

    // ---- разметка лица в осях корня VRGorillaPlayer (те же, что у FaceMaskBuilder)
    const float Cx = 0.004f;
    const float EyeDx = 0.0625f, EyeY = 1.464f;
    const float MouthY = 1.314f;
    // объёмный глаз: выпуклый эллипсоид, центр чуть позади нарисованного глаза → выступает из глазницы на ~1.8 см
    static readonly Vector3 EyeballRadii = new Vector3(0.05f, 0.036f, 0.03f);
    const float EyeCenterBehind = 0.012f;
    const float LidInner = 0.92f;                  // толщина века: внутренняя поверхность = 0.92 внешней
    // веки охватывают глаз с зазором 5 мм — закрытое веко не протыкают зрачок и блик
    const float LidGap = 0.005f;
    static readonly Vector3 LidRadii = (EyeballRadii + Vector3.one * LidGap) / LidInner;
    static readonly Vector3 PupilSize = new Vector3(0.036f, 0.04f, 0.004f);

    static readonly StringBuilder log = new StringBuilder();

    struct Layout
    {
        public Vector3 mouth, mouthFwd;
        public Vector3[] eye;                        // точка на поверхности глаза
        public Vector3[] brow, browFwd;
    }

    [MenuItem("Tools/Heist/Player/Reactions (lip-sync, face emotes, fall ragdoll)")]
    static void Menu() => Debug.Log(Run());

    [MenuItem("Tools/Heist/Player/Reactions Preview")]
    static void PreviewMenu() => Debug.Log(CapturePreview());

    public static string Run()
    {
        log.Clear().AppendLine("[PlayerReactionsSetup]");
        MapKit.EnsureFolder(MeshDir);
        MapKit.EnsureFolder(MatDir);
        var mats = Materials();
        var lidUpper = SaveMesh(LidMesh(true), "Face_Lid_Upper");
        var lidLower = SaveMesh(LidMesh(false), "Face_Lid_Lower");

        // 1) VR-горилла: разметка + установка
        Matrix4x4 headFromRoot;
        Layout lay;
        var root = PrefabUtility.LoadPrefabContents(VRPrefab);
        try
        {
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var rig = root.GetComponentInChildren<GorillaFinalRig>(true);
            var hsmr = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => s.name == "Head");
            lay = Measure(hsmr);
            headFromRoot = rig.headBone.worldToLocalMatrix;
            Install(rig, lay, headFromRoot, mats, lidUpper, lidLower, "VRGorillaPlayer");
            PrefabUtility.SaveAsPrefabAsset(root, VRPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        // 2) сетевая горилла
        var net = PrefabUtility.LoadPrefabContents(NetPrefab);
        try
        {
            var rig = net.GetComponentInChildren<GorillaFinalRig>(true);
            if (rig) { Install(rig, lay, headFromRoot, mats, lidUpper, lidLower, "NetworkGorillaAvatar"); PrefabUtility.SaveAsPrefabAsset(net, NetPrefab); }
            else log.AppendLine("  ERROR: network avatar rig not found");
        }
        finally { PrefabUtility.UnloadPrefabContents(net); }

        // 3) ПК-гориллы в сценах (VR-префаб уже обновлён выше)
        var setup = EditorSceneManager.GetSceneManagerSetup();
        foreach (var path in new[] { HubPath, HeistPath })
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            int n = 0;
            foreach (var rig in Object.FindObjectsByType<GorillaFinalRig>(FindObjectsInactive.Include))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(rig) && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(rig) == VRPrefab) continue;
                Install(rig, lay, headFromRoot, mats, lidUpper, lidLower, $"{scene.name}/{rig.transform.root.name}");
                n++;
            }
            log.AppendLine($"  {scene.name}: scene gorillas {n}");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) log.AppendLine("  ERROR: save failed " + path);
        }
        if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // =========================================================================== разметка
    struct Tri { public Vector3 a, b, c, na, nb, nc; }

    static Layout Measure(SkinnedMeshRenderer hsmr)
    {
        var baked = new Mesh();
        hsmr.BakeMesh(baked, true);
        var wv = baked.vertices.Select(p => hsmr.transform.TransformPoint(p)).ToArray();
        var wn = baked.normals.Select(n => hsmr.transform.TransformDirection(n).normalized).ToArray();
        var ti = baked.triangles;
        var tris = new List<Tri>();
        for (int i = 0; i < ti.Length; i += 3)
            tris.Add(new Tri { a = wv[ti[i]], b = wv[ti[i + 1]], c = wv[ti[i + 2]], na = wn[ti[i]], nb = wn[ti[i + 1]], nc = wn[ti[i + 2]] });
        Object.DestroyImmediate(baked);

        (Vector3 p, Vector3 n) Hit(float x, float y)
        {
            if (!Raycast(tris, new Vector3(x, y, 0.6f), Vector3.back, out var h, out var n)) { log.AppendLine($"  ERROR: no face hit at ({x:F3},{y:F3})"); return (new Vector3(x, y, 0f), Vector3.forward); }
            return (h, n);
        }

        var lay = new Layout { eye = new Vector3[2], brow = new Vector3[2], browFwd = new Vector3[2] };
        var m = Hit(Cx, MouthY);
        lay.mouth = m.p;
        lay.mouthFwd = (m.n + Vector3.forward).normalized;
        for (int s = 0; s < 2; s++)
        {
            float side = s == 0 ? -1f : 1f, x = Cx + side * EyeDx;
            lay.eye[s] = Hit(x, EyeY).p;
            // надбровье: самая выступающая вперёд точка над глазом
            float bestZ = float.MinValue, bestY = EyeY + 0.04f; Vector3 bestN = Vector3.forward, bestP = Vector3.zero;
            for (float dy = 0.022f; dy <= 0.075f; dy += 0.002f)
            {
                var h = Hit(x + side * 0.004f, EyeY + dy);
                if (h.p.z > bestZ) { bestZ = h.p.z; bestY = EyeY + dy; bestN = h.n; bestP = h.p; }
            }
            lay.brow[s] = bestP + bestN * 0.004f;
            lay.browFwd[s] = (bestN + Vector3.forward).normalized;
            log.AppendLine($"  eye {(s == 0 ? "L" : "R")}: surface z {lay.eye[s].z:F3}, brow ridge y {bestY:F3} z {bestZ:F3} (socket depth {(bestZ - lay.eye[s].z) * 100f:F1} cm)");
        }
        log.AppendLine($"  mouth: {lay.mouth:F3}");
        return lay;
    }

    static bool Raycast(List<Tri> tris, Vector3 o, Vector3 dir, out Vector3 hit, out Vector3 normal)
    {
        float best = float.MaxValue; hit = normal = Vector3.zero;
        foreach (var t in tris)
        {
            Vector3 e1 = t.b - t.a, e2 = t.c - t.a, pv = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, pv);
            if (Mathf.Abs(det) < 1e-10f) continue;
            float inv = 1f / det; Vector3 tv = o - t.a;
            float u = Vector3.Dot(tv, pv) * inv; if (u < 0 || u > 1) continue;
            Vector3 qv = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(dir, qv) * inv; if (v < 0 || u + v > 1) continue;
            float d = Vector3.Dot(e2, qv) * inv;
            if (d > 0 && d < best) { best = d; hit = o + dir * d; normal = (t.na * (1 - u - v) + t.nb * u + t.nc * v).normalized; }
        }
        return best < float.MaxValue;
    }

    // =========================================================================== установка
    static void Install(GorillaFinalRig rig, Layout lay, Matrix4x4 headFromRoot, Dictionary<string, Material> mats, Mesh lidUpper, Mesh lidLower, string who)
    {
        var head = rig.headBone;
        if (!head) { log.AppendLine($"  ERROR: {who}: no head bone"); return; }
        var hsmr = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name == "Head");
        int layer = hsmr ? hsmr.gameObject.layer : head.gameObject.layer;

        var old = head.Find(RootName);
        if (old) Object.DestroyImmediate(old.gameObject);
        var faceRoot = new GameObject(RootName).transform;
        faceRoot.SetParent(head, false);
        // дети FaceReactions — в метрах и в осях головы (масштаб кости гасим)
        var ls = head.lossyScale;
        faceRoot.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
        faceRoot.gameObject.layer = layer;

        // точки из осей корня VR-гориллы → оси кости Head → оси FaceReactions
        Vector3 P(Vector3 rootPoint) => faceRoot.InverseTransformPoint(head.TransformPoint(headFromRoot.MultiplyPoint3x4(rootPoint)));
        Quaternion R(Vector3 fwd) => Quaternion.Inverse(faceRoot.rotation) * head.rotation *
                                     Quaternion.LookRotation(headFromRoot.MultiplyVector(fwd), headFromRoot.MultiplyVector(Vector3.up));

        // ---- рот
        var mouth = Node("Mouth", faceRoot, P(lay.mouth), R(lay.mouthFwd), Vector3.one, layer);
        var hole = Ball("MouthHole", mouth, Vector3.zero, Quaternion.identity, new Vector3(0.11f, 0.006f, 0.03f), mats["MouthDark"], layer);
        var tongue = Ball("Tongue", hole, new Vector3(0f, -0.28f, 0.12f), Quaternion.identity, new Vector3(0.62f, 0.42f, 0.7f), mats["Tongue"], layer);
        var teeth = Ball("Teeth", hole, new Vector3(0f, 0.36f, 0.2f), Quaternion.identity, new Vector3(0.66f, 0.2f, 0.55f), mats["Teeth"], layer);

        // ---- глаза и брови
        var parts = new GorillaFaceEmotes.EyeParts[2];
        var browR = new List<Renderer>();
        var gazeEyes = new GorillaEyeGaze.Eye[2];
        for (int s = 0; s < 2; s++)
        {
            string tag = s == 0 ? "L" : "R";
            float side = s == 0 ? -1f : 1f;
            var eye = Node("Eye_" + tag, faceRoot, P(lay.eye[s] - Vector3.forward * EyeCenterBehind), R(Vector3.forward), Vector3.one, layer);
            var ball = Ball("Eyeball", eye, Vector3.zero, Quaternion.identity, EyeballRadii * 2f, mats["EyeWhite"], layer);
            var pupil = Ball("Pupil", eye, new Vector3(0f, 0f, EyeballRadii.z), Quaternion.identity, PupilSize, mats["Pupil"], layer);
            Ball("Glint", pupil, new Vector3(0.24f, 0.26f, 0.45f), Quaternion.identity, new Vector3(0.28f, 0.26f, 0.4f), mats["Glint"], layer);
            var lids = Node("Lids", eye, Vector3.zero, Quaternion.identity, LidRadii, layer);
            var up = MeshNode("UpperLid", lids, lidUpper, mats["Lid"], layer);
            var lo = MeshNode("LowerLid", lids, lidLower, mats["Lid"], layer);
            var brow = Ball("Brow_" + tag, faceRoot, P(lay.brow[s]), R(lay.browFwd[s]), new Vector3(0.085f, 0.022f, 0.024f), mats["Brow"], layer);
            browR.Add(brow.GetComponent<Renderer>());
            gazeEyes[s] = new GorillaEyeGaze.Eye { anchor = eye, eyeball = ball, pupil = pupil };
            parts[s] = new GorillaFaceEmotes.EyeParts { upperLid = up, lowerLid = lo, brow = brow, side = side };
        }

        // ---- компоненты
        var go = rig.gameObject;
        var mouthC = go.GetComponent<GorillaVoiceMouth>();
        if (!mouthC) mouthC = go.AddComponent<GorillaVoiceMouth>();
        mouthC.mouthHole = hole;
        mouthC.mouthRenderers = new[] { hole.GetComponent<Renderer>(), tongue.GetComponent<Renderer>(), teeth.GetComponent<Renderer>() };

        var emotes = go.GetComponent<GorillaFaceEmotes>();
        if (!emotes) emotes = go.AddComponent<GorillaFaceEmotes>();
        emotes.left = parts[0]; emotes.right = parts[1];
        emotes.browRenderers = browR.ToArray();
        emotes.normal = GorillaFaceEmotes.DefaultNormal;
        emotes.strain = GorillaFaceEmotes.DefaultStrain;
        emotes.scared = GorillaFaceEmotes.DefaultScared;
        emotes.knockedOut = GorillaFaceEmotes.DefaultKnockedOut;
        var netAv = rig.GetComponentInParent<NetworkGorillaAvatar>(true);
        emotes.skin = netAv && netAv.skin ? netAv.skin : rig.transform.root.GetComponentInChildren<PlayerSkinController>(true);

        var fall = go.GetComponent<GorillaFallRagdoll>();
        if (!fall) fall = go.AddComponent<GorillaFallRagdoll>();
        fall.rig = rig;

        var gaze = go.GetComponent<GorillaEyeGaze>();
        if (!gaze) gaze = go.AddComponent<GorillaEyeGaze>();
        gaze.rig = rig;
        gaze.left = gazeEyes[0]; gaze.right = gazeEyes[1];
        gaze.radii = EyeballRadii;
        emotes.gaze = gaze;

        // закрытый рот, обычное лицо, взгляд прямо
        foreach (var r in mouthC.mouthRenderers) r.enabled = false;
        emotes.PreviewPose(FaceEmote.Normal);
        gaze.PreviewLook(Vector3.forward, 0f);
        EditorUtility.SetDirty(go);

        int missing = new[] { "Hips", "Spine", "Chest", "Head", "Left_UpperArm", "Left_LowerArm", "Left_UpperLeg", "Left_LowerLeg" }
            .Count(b => !go.GetComponentsInChildren<Transform>(true).Any(t => t.name == b));
        log.AppendLine($"  {who}: face installed (layer {LayerMask.LayerToName(layer)}), skin {(emotes.skin ? "ok" : "MISSING")}, ragdoll bones {(missing == 0 ? "ok" : "ERROR: missing " + missing)}");
    }

    static Transform Node(string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, int layer)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = pos; t.localRotation = rot; t.localScale = scale;
        t.gameObject.layer = layer;
        return t;
    }

    static Transform MeshNode(string name, Transform parent, Mesh mesh, Material mat, int layer)
    {
        var t = Node(name, parent, Vector3.zero, Quaternion.identity, Vector3.one, layer);
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = t.gameObject.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.lightProbeUsage = LightProbeUsage.BlendProbes;
        return t;
    }

    static Mesh sphere;
    static Transform Ball(string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material mat, int layer)
    {
        if (!sphere) sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
        var t = MeshNode(name, parent, sphere, mat, layer);
        t.localPosition = pos; t.localRotation = rot; t.localScale = scale;
        return t;
    }

    // =========================================================================== меши и материалы
    /// Веко: толстая сферическая «скорлупка» на передней половине единичной сферы.
    /// Верхнее — от экватора (край века) вверх и назад, нижнее — вниз. Поворот вокруг X двигает край,
    /// а уголки глаза (точки ±X) остаются на месте.
    static Mesh LidMesh(bool upper)
    {
        const int Lon = 20, Lat = 10;
        const float Inner = LidInner, PhiMax = 115f;
        float sgn = upper ? 1f : -1f;
        var v = new List<Vector3>(); var tri = new List<int>();
        Vector3 S(float lon, float phi, float r)
        {
            float l = lon * Mathf.Deg2Rad, p = phi * Mathf.Deg2Rad * sgn;
            return new Vector3(Mathf.Sin(l) * Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(l) * Mathf.Cos(p)) * r;
        }
        void Grid(float r, bool flip)
        {
            int b = v.Count;
            for (int j = 0; j <= Lat; j++)
                for (int i = 0; i <= Lon; i++)
                    v.Add(S(-90f + 180f * i / Lon, PhiMax * j / Lat, r));
            for (int j = 0; j < Lat; j++)
                for (int i = 0; i < Lon; i++)
                {
                    int a = b + j * (Lon + 1) + i, c = a + Lon + 1;
                    Quad(a, a + 1, c + 1, c, flip);
                }
        }
        void Quad(int a, int b, int c, int d, bool flip)
        {
            // наружная сторона: обход по часовой, если смотреть снаружи (Unity)
            bool f = flip ^ !upper;
            if (!f) tri.AddRange(new[] { a, c, b, a, d, c });
            else tri.AddRange(new[] { a, b, c, a, c, d });
        }
        Grid(1f, false);
        Grid(Inner, true);
        // кромка века (phi = 0): соединяем внешнюю и внутреннюю поверхности
        int e0 = v.Count;
        for (int i = 0; i <= Lon; i++) { v.Add(S(-90f + 180f * i / Lon, 0f, 1f)); v.Add(S(-90f + 180f * i / Lon, 0f, Inner)); }
        for (int i = 0; i < Lon; i++)
        {
            int a = e0 + i * 2, b = a + 2;
            Quad(a + 1, b + 1, b, a, false);
        }
        var m = new Mesh { name = upper ? "Face_Lid_Upper" : "Face_Lid_Lower" };
        m.SetVertices(v); m.SetTriangles(tri, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        // нормали внешней поверхности должны смотреть от центра — если нет, разворачиваем все треугольники
        var n = m.normals; var verts = m.vertices;
        int mid = (Lat / 2) * (Lon + 1) + Lon / 2;
        if (Vector3.Dot(n[mid], verts[mid]) < 0f)
        {
            var t = m.triangles;
            for (int i = 0; i < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
            m.triangles = t; m.RecalculateNormals();
        }
        return m;
    }

    static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = $"{MeshDir}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing)
        {
            existing.Clear();
            existing.SetVertices(mesh.vertices); existing.SetNormals(mesh.normals); existing.SetTriangles(mesh.triangles, 0);
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static Dictionary<string, Material> Materials()
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        Material M(string name, Shader sh, Color c, float smooth)
        {
            string path = $"{MatDir}/M_Face_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetColor("_BaseColor", c);
            if (sh == lit)
            {
                m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Smoothness", smooth);
                m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                m.SetFloat("_SpecularHighlights", smooth > 0.3f ? 1f : 0f);
                if (smooth > 0.3f) m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF"); else m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            }
            if (name == "Lid") { m.SetFloat("_Cull", 0f); m.doubleSidedGI = true; } // кромка века видна с обеих сторон
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
        return new Dictionary<string, Material>
        {
            ["Lid"] = M("Lid", lit, new Color(0.50f, 0.50f, 0.52f), 0.05f),        // серая кожа морды
            ["Brow"] = M("Brow", lit, new Color(0.1f, 0.1f, 0.11f), 0f),           // цвет задаёт шерсть (×0.55)
            ["MouthDark"] = M("MouthDark", unlit, new Color(0.07f, 0.02f, 0.025f), 0f),
            ["Tongue"] = M("Tongue", lit, new Color(0.86f, 0.36f, 0.42f), 0.25f),
            ["Teeth"] = M("Teeth", lit, new Color(0.95f, 0.94f, 0.88f), 0.2f),
            ["EyeWhite"] = M("EyeWhite", lit, new Color(0.97f, 0.97f, 0.95f), 0.5f),
            ["Pupil"] = M("Pupil", lit, new Color(0.02f, 0.02f, 0.025f), 0.6f),
            ["Glint"] = M("Glint", unlit, new Color(1f, 1f, 1f), 0f),               // блик в зрачке
        };
    }

    // =========================================================================== снимки
    /// Лицо VR-гориллы в каждом выражении и с открытым ртом → production/qa/evidence/reactions_*.png
    public static string CapturePreview()
    {
        var sb = new StringBuilder("[PlayerReactions preview]\n");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VRPrefab);
        var scene = EditorSceneManager.NewPreviewScene();
        System.IO.Directory.CreateDirectory(EvidenceDir);
        try
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var rig = go.GetComponentInChildren<GorillaFinalRig>(true);
            var emotes = rig.GetComponent<GorillaFaceEmotes>();
            var mouth = rig.GetComponent<GorillaVoiceMouth>();
            if (!emotes || !mouth) return "ERROR: reactions not installed — run Tools/Heist/Player/Reactions first";
            var skin = rig.transform.root.GetComponentInChildren<PlayerSkinController>(true);
            // брови в цвет шерсти (в редакторе Update не идёт — красим сами)
            var c = (skin ? skin.furColor : new Color(0.17f, 0.17f, 0.19f)) * emotes.browDarken; c.a = 1f;
            var block = new MaterialPropertyBlock();
            foreach (var r in emotes.browRenderers) { r.GetPropertyBlock(block); block.SetColor("_BaseColor", c); r.SetPropertyBlock(block); }

            var lgo = new GameObject("L"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lgo, scene);
            var l = lgo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f;
            lgo.transform.rotation = Quaternion.Euler(25f, 160f, 0f);
            var l2go = new GameObject("L2"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(l2go, scene);
            var l2 = l2go.AddComponent<Light>(); l2.type = LightType.Directional; l2.intensity = 0.55f; l2go.transform.rotation = Quaternion.Euler(10f, -150f, 0f);
            var cgo = new GameObject("C"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cgo, scene);
            var cam = cgo.AddComponent<Camera>(); cam.scene = scene; cam.fieldOfView = 24f; cam.nearClipPlane = 0.01f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            const int Size = 640;
            var rt = new RenderTexture(Size, Size, 24); cam.targetTexture = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            var center = new Vector3(Cx, 1.40f, 0f);

            var gaze = rig.GetComponent<GorillaEyeGaze>();
            var shots = new (string name, FaceEmote e, float mouthOpen, Vector3 dir, Vector3 look)[]
            {
                ("normal", FaceEmote.Normal, 0f, new Vector3(0f, 0.05f, 1f), Vector3.forward),
                ("look_side", FaceEmote.Normal, 0f, new Vector3(0f, 0.05f, 1f), new Vector3(0.6f, 0.15f, 1f)),
                ("talking", FaceEmote.Normal, 0.85f, new Vector3(0f, 0.05f, 1f), Vector3.forward),
                ("strain", FaceEmote.Strain, 0f, new Vector3(0f, 0.05f, 1f), new Vector3(0f, -0.2f, 1f)),
                ("scared", FaceEmote.Scared, 0.6f, new Vector3(0f, 0.05f, 1f), Vector3.forward),
                ("knockedout", FaceEmote.KnockedOut, 0f, new Vector3(0f, 0.05f, 1f), Vector3.forward),
                ("threequarter", FaceEmote.Normal, 0f, new Vector3(0.75f, 0.12f, 1f), new Vector3(0.7f, 0.1f, 1f)),
                ("side", FaceEmote.Normal, 0f, new Vector3(1f, 0.05f, 0.1f), Vector3.forward),
            };
            foreach (var s in shots)
            {
                emotes.PreviewPose(s.e);
                if (gaze) gaze.PreviewLook(s.look, emotes.PoseOf(s.e).wide);
                mouth.PreviewOpen(s.mouthOpen);
                var dn = s.dir.normalized;
                cgo.transform.position = center + dn * 0.95f; cgo.transform.rotation = Quaternion.LookRotation(-dn);
                cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); tex.Apply();
                string file = $"{EvidenceDir}/reactions_{s.name}.png";
                System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
                sb.AppendLine("  " + file);
            }
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        return sb.ToString();
    }
}
