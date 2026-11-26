using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Ставит финальную гориллу (Assets/Art/GeneratedModels/Gorilla_Final/Gorilla_Final.fbx, со скелетом Tripo)
/// на префаб VRGorillaPlayer:
///  • ноги удаляются из меша (треугольники, привязанные к костям ног), а отверстие снизу торса
///    зашивается куполом-заглушкой из того же материала шерсти;
///  • меш делится на «тело» (слой Player) и «голову» (слой LocalHead: своей камере не видна, зеркалу видна);
///  • в каждом меше два слота: шерсть (нейтральная серая текстура, цвет даёт PlayerSkinController) и
///    живот/морда/глаза (оригинальная текстура из архива — не перекрашиваются);
///  • рост: глаза ≈ 1.45 м (XR Origin в режиме Device, Camera Y Offset 1.45), длина руки ≈ 0.55 м;
///  • руки — Two Bone IK к LeftHandFollower / RightHandFollower (GorillaFinalRig).
/// Меню: Tools/Heist/Player/Build Gorilla Final Avatar.
/// </summary>
public static class GorillaFinalAvatarBuilder
{
    const string Dir = "Assets/Art/GeneratedModels/Gorilla_Final";
    const string ModelPath = Dir + "/Gorilla_Final.fbx";
    const string TexPath = Dir + "/Textures/Gorilla_Final_BaseColor.jpg";
    const string MatDir = Dir + "/Materials";
    const string OutDir = "Assets/Art/Models/Generated/GorillaFinal";
    const string PrefabPath = "Assets/Prefabs/Player/VRGorillaPlayer.prefab";

    public const float EyeHeight = 1.45f;   // уровень глаз камеры (м)
    const float ArmLength = 0.55f;          // плечо → запястье (м)
    const float FurTexScale = 0.8f;
    const int FurTexSize = 512;

    static readonly string[] LegBones = { "Left_UpperLeg", "Left_LowerLeg", "Left_Foot", "Left_Toes", "Left_ToesEnd",
                                          "Right_UpperLeg", "Right_LowerLeg", "Right_Foot", "Right_Toes", "Right_ToesEnd" };
    static readonly string[] HeadBones = { "Head", "Left_Eye", "Right_Eye", "Neck", "Neck_Twist_A" };

    [MenuItem("Tools/Heist/Player/Build Gorilla Final Avatar")]
    static void Menu() => Debug.Log(Build());

    public static string Build()
    {
        EnsureFolder(OutDir); EnsureFolder(MatDir);

        // ---------- импорт: читаемый меш, текстура 512, материал модели = текстура из архива
        AssetDatabase.ImportAsset(TexPath, ImportAssetOptions.ForceUpdate);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath) ?? throw new Exception("No texture " + TexPath);
        var mModel = GetMat(MatDir + "/M_Gorilla_Final.mat", tex);

        var mi = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        mi.isReadable = true;
        mi.animationType = ModelImporterAnimationType.Generic;
        mi.importAnimation = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        var srcAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        foreach (var r in srcAsset.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), mModel);
        mi.SaveAndReimport();
        srcAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);

        // исходник текстуры в полном разрешении (для разметки шерсть/живот)
        var full = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        full.LoadImage(File.ReadAllBytes(TexPath));
        int W = full.width, H = full.height;
        var px = full.GetPixels();
        bool Green(Color c) => c.g > 0.15f && c.g > c.r * 1.12f && c.g > c.b * 1.12f;
        Color Sample(Vector2 uv) => px[Mathf.Clamp((int)(uv.y * H), 0, H - 1) * W + Mathf.Clamp((int)(uv.x * W), 0, W - 1)];

        // ---------- меш
        var tmp = (GameObject)Object.Instantiate(srcAsset);
        try
        {
            tmp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            tmp.transform.localScale = Vector3.one;
            var smr = tmp.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var src = smr.sharedMesh;
            var bones = smr.bones;
            int BoneIdx(string n) => Array.FindIndex(bones, b => b && b.name == n);
            var legSet = new HashSet<int>(LegBones.Select(BoneIdx).Where(i => i >= 0));
            var headSet = new HashSet<int>(HeadBones.Select(BoneIdx).Where(i => i >= 0));
            int hips = BoneIdx("Hips");

            var verts = new List<Vector3>(src.vertices);
            var norms = new List<Vector3>(src.normals);
            var tans = src.tangents.Length == src.vertexCount ? new List<Vector4>(src.tangents) : null;
            var uvs = new List<Vector2>(src.uv);
            var bw = new List<BoneWeight>(src.boneWeights);
            int[] tris = src.triangles;
            int vc = verts.Count, tc = tris.Length / 3;

            float W8(BoneWeight w, HashSet<int> set) =>
                (set.Contains(w.boneIndex0) ? w.weight0 : 0) + (set.Contains(w.boneIndex1) ? w.weight1 : 0) +
                (set.Contains(w.boneIndex2) ? w.weight2 : 0) + (set.Contains(w.boneIndex3) ? w.weight3 : 0);

            // классификация треугольников
            var kind = new int[tc]; // 0 тело, 1 голова, -1 нога (удалить)
            for (int t = 0; t < tc; t++)
            {
                float leg = 0, hd = 0;
                for (int k = 0; k < 3; k++) { var w = bw[tris[t * 3 + k]]; leg += W8(w, legSet); hd += W8(w, headSet); }
                kind[t] = leg / 3f > 0.5f ? -1 : hd / 3f > 0.4f ? 1 : 0;
            }
            bool IsFur(int t)
            {
                int a = tris[t * 3], b = tris[t * 3 + 1], c = tris[t * 3 + 2];
                int votes = 0;
                if (Green(Sample(uvs[a]))) votes++;
                if (Green(Sample(uvs[b]))) votes++;
                if (Green(Sample(uvs[c]))) votes++;
                if (Green(Sample((uvs[a] + uvs[b] + uvs[c]) / 3f))) votes += 2;
                return votes >= 3;
            }

            // сварка по позиции (UV-швы дают дубликаты вершин)
            var canon = new int[vc];
            var posMap = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < vc; i++)
            {
                var key = Vector3Int.RoundToInt(verts[i] * 20000f);
                if (!posMap.TryGetValue(key, out int c)) { c = i; posMap[key] = i; }
                canon[i] = c;
            }
            long EdgeKey(int a, int b) => ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
            var removedEdges = new HashSet<long>();
            var keptCount = new Dictionary<long, int>();
            for (int t = 0; t < tc; t++)
                for (int k = 0; k < 3; k++)
                {
                    long e = EdgeKey(canon[tris[t * 3 + k]], canon[tris[t * 3 + (k + 1) % 3]]);
                    if (kind[t] < 0) removedEdges.Add(e);
                    else keptCount[e] = keptCount.TryGetValue(e, out int n) ? n + 1 : 1;
                }
            // направленные граничные рёбра (как они идут в оставшихся треугольниках)
            var next = new Dictionary<int, List<(int to, int va, int vb)>>();
            for (int t = 0; t < tc; t++)
            {
                if (kind[t] < 0) continue;
                for (int k = 0; k < 3; k++)
                {
                    int va = tris[t * 3 + k], vb = tris[t * 3 + (k + 1) % 3];
                    long e = EdgeKey(canon[va], canon[vb]);
                    if (keptCount[e] == 1 && removedEdges.Contains(e))
                    {
                        if (!next.TryGetValue(canon[va], out var l)) next[canon[va]] = l = new List<(int, int, int)>();
                        l.Add((canon[vb], va, vb));
                    }
                }
            }
            var loops = new List<List<int>>(); // списки исходных вершин по кругу
            var used = new HashSet<(int, int)>();
            foreach (var start in next.Keys.ToList())
                foreach (var e0 in next[start])
                {
                    if (used.Contains((start, e0.to))) continue;
                    var loop = new List<int>();
                    int cur = start; var e = e0;
                    for (int guard = 0; guard < 4000; guard++)
                    {
                        used.Add((cur, e.to)); loop.Add(e.va);
                        cur = e.to;
                        if (cur == start) break;
                        if (!next.TryGetValue(cur, out var outs)) { loop = null; break; }
                        int ni = outs.FindIndex(o => !used.Contains((cur, o.to)));
                        if (ni < 0) { loop = null; break; }
                        e = outs[ni];
                    }
                    if (loop != null && loop.Count >= 3) loops.Add(loop);
                }

            // UV чистой шерсти: центр самого большого зелёного пятна
            Vector2 furUV = new Vector2(0.5f, 0.5f); float best = -1;
            for (int y = 24; y < H - 24; y += 32)
                for (int x = 24; x < W - 24; x += 32)
                {
                    int g = 0, n = 0;
                    for (int dy = -20; dy <= 20; dy += 5) for (int dx = -20; dx <= 20; dx += 5) { n++; if (Green(px[(y + dy) * W + x + dx])) g++; }
                    float score = g / (float)n;
                    if (score > best) { best = score; furUV = new Vector2((x + 0.5f) / W, (y + 0.5f) / H); }
                    if (best >= 1f) break;
                }

            // купола-заглушки
            Vector3 down = smr.transform.InverseTransformDirection(Vector3.down).normalized;
            var capTris = new List<int>();
            int capVerts = 0;
            int AddV(Vector3 p, Vector3 nrm, BoneWeight w)
            {
                verts.Add(p); norms.Add(nrm); uvs.Add(furUV); bw.Add(w);
                tans?.Add(new Vector4(1, 0, 0, 1));
                capVerts++;
                return verts.Count - 1;
            }
            foreach (var loop in loops)
            {
                Vector3 c = Vector3.zero; foreach (var v in loop) c += verts[v]; c /= loop.Count;
                float r = loop.Average(v => Vector3.Distance(verts[v], c));
                Vector3 O = c - down * r * 0.5f; // центр «полусферы» чуть выше отверстия
                var hipsW = new BoneWeight { boneIndex0 = Mathf.Max(0, hips), weight0 = 1f };
                Vector3 centerP = c + down * r * 0.55f;
                int center = AddV(centerP, (centerP - O).normalized, hipsW);
                int n = loop.Count;
                var outer = new int[n]; var inner = new int[n];
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = verts[loop[i]];
                    outer[i] = AddV(p, (p - O).normalized, bw[loop[i]]);
                    Vector3 q = c + (p - c) * 0.62f + down * r * 0.38f;
                    inner[i] = AddV(q, (q - O).normalized, bw[loop[i]]);
                }
                for (int i = 0; i < n; i++)
                {
                    int u = outer[i], v = outer[(i + 1) % n], u2 = inner[i], v2 = inner[(i + 1) % n];
                    capTris.AddRange(new[] { v, u, u2, v, u2, v2, v2, u2, center });
                }
            }

            // сборка двух мешей: тело и голова, слоты 0 = шерсть, 1 = живот/морда
            var bodyFur = new List<int>(capTris); var bodySkin = new List<int>();
            var headFur = new List<int>(); var headSkin = new List<int>();
            int fur = 0, skin = 0;
            for (int t = 0; t < tc; t++)
            {
                if (kind[t] < 0) continue;
                bool f = IsFur(t);
                if (f) fur++; else skin++;
                var list = kind[t] == 1 ? (f ? headFur : headSkin) : (f ? bodyFur : bodySkin);
                list.Add(tris[t * 3]); list.Add(tris[t * 3 + 1]); list.Add(tris[t * 3 + 2]);
            }
            Mesh Make(string name, List<int> a, List<int> b)
            {
                var m = new Mesh { name = name };
                m.SetVertices(verts); m.SetNormals(norms); if (tans != null) m.SetTangents(tans);
                m.SetUVs(0, uvs);
                m.boneWeights = bw.ToArray();
                m.bindposes = src.bindposes;
                m.subMeshCount = 2;
                m.SetTriangles(a, 0); m.SetTriangles(b, 1);
                m.RecalculateBounds();
                return SaveMesh(m, $"{OutDir}/{name}.asset");
            }
            var bodyMesh = Make("GorillaFinal_Body", bodyFur, bodySkin);
            var headMesh = Make("GorillaFinal_Head", headFur, headSkin);

            // нейтральная текстура шерсти (серые оттенки; цвет даёт PlayerSkinController)
            Color furMean = Avg(px.Where(Green).Where((c, i) => i % 7 == 0));
            var furTex = MakeFurTexture(px, W, H, furMean);
            var mFur = GetMat(MatDir + "/M_Gorilla_Final_Fur.mat", furTex);
            Color furDefault = new Color(furMean.r / FurTexScale, furMean.g / FurTexScale, furMean.b / FurTexScale, 1f);
            // живот/морда: та же текстура, но зелёные пиксели на краях заменены серым — без зелёной каймы при перекраске
            Color skinMean = Avg(px.Where(c => !Green(c) && c.r > 0.35f).Where((c, i) => i % 5 == 0));
            var skinTex = MakeSkinTexture(px, W, H, Green, skinMean);
            var mSkinAvatar = GetMat(MatDir + "/M_Gorilla_Final_Skin.mat", skinTex);

            string meshReport = $"tris: removed legs={kind.Count(k => k < 0)}, head={kind.Count(k => k == 1)}, body={kind.Count(k => k == 0)}; " +
                                $"fur={fur} skin={skin}; cap loops={loops.Count} ({string.Join(",", loops.Select(l => l.Count))} edges), cap verts={capVerts}; furUV={furUV} ({best:0.00})";
            return meshReport + "\n" + BuildPrefab(srcAsset, bodyMesh, headMesh, mFur, mSkinAvatar, furDefault);
        }
        finally { Object.DestroyImmediate(tmp); Object.DestroyImmediate(full); }
    }

    // ------------------------------------------------------------------ префаб
    static string BuildPrefab(GameObject modelAsset, Mesh bodyMesh, Mesh headMesh, Material mFur, Material mSkin, Color furDefault)
    {
        int playerLayer = LayerMask.NameToLayer("Player"), localHead = LayerMask.NameToLayer("LocalHead");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            foreach (var n in new[] { "MonkeyAvatar", "GorillaAvatar" })
            {
                var old = root.transform.Find(n);
                if (old) Object.DestroyImmediate(old.gameObject);
            }
            var cam = root.GetComponentInChildren<Camera>(true) ?? throw new Exception("Camera not found");
            var offset = cam.transform.parent;
            var gp = root.GetComponent<GorillaLocomotion.Player>();

            // ---- рост: XR Origin в режиме Device → глаза всегда на 1.45 м над полом (независимо от роста игрока)
            var xr = root.GetComponent("XROrigin");
            if (xr)
            {
                var so = new SerializedObject(xr);
                so.FindProperty("m_Camera").objectReferenceValue = cam;
                so.FindProperty("m_CameraFloorOffsetObject").objectReferenceValue = offset.gameObject;
                so.FindProperty("m_RequestedTrackingOriginMode").intValue = 1; // Device
                so.FindProperty("m_CameraYOffset").floatValue = EyeHeight;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            offset.localPosition = new Vector3(0, EyeHeight, 0);
            cam.transform.localPosition = Vector3.zero;
            foreach (var cn in new[] { "Left Controller", "Right Controller" })
            {
                var c = offset.Find(cn);
                if (c) c.localPosition = new Vector3(c.localPosition.x, -0.5f, c.localPosition.z);
            }
            foreach (var f in new[] { gp ? gp.leftHandFollower : null, gp ? gp.rightHandFollower : null })
                if (f) f.localPosition = new Vector3(f.localPosition.x, EyeHeight - 0.5f, f.localPosition.z);
            var bodyCol = root.transform.Find("BodyCollider");
            if (bodyCol) bodyCol.localPosition = new Vector3(0, EyeHeight - 0.45f, 0);

            // ---- аватар
            var avatar = new GameObject("GorillaAvatar").transform;
            avatar.SetParent(root.transform, false);
            avatar.localPosition = new Vector3(0, EyeHeight, 0);
            avatar.gameObject.layer = playerLayer;

            var model = ((GameObject)Object.Instantiate(modelAsset)).transform;
            model.name = "Model";
            model.SetParent(avatar, false);
            Transform B(string n) => model.GetComponentsInChildren<Transform>(true).First(t => t.name == n);
            var lU = B("Left_UpperArm"); var lL = B("Left_LowerArm"); var lH = B("Left_Hand");
            var rU = B("Right_UpperArm"); var rL = B("Right_LowerArm"); var rH = B("Right_Hand");
            float armModel = Vector3.Distance(lU.position, lL.position) + Vector3.Distance(lL.position, lH.position);
            float s = ArmLength / armModel;
            model.localScale = Vector3.one * s;
            Vector3 eyeW = (B("Left_Eye").position + B("Right_Eye").position) * 0.5f;
            model.position += avatar.position - eyeW;                   // глаза модели = начало аватара
            float footY = model.position.y;                              // низ модели (ступни)

            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            smr.name = "Body";
            smr.sharedMesh = bodyMesh;
            smr.sharedMaterials = new[] { mFur, mSkin };
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smr.updateWhenOffscreen = false;
            var lb = bodyMesh.bounds; lb.Expand(lb.size.magnitude); smr.localBounds = lb;
            var headGo = Object.Instantiate(smr.gameObject, smr.transform.parent);
            headGo.name = "Head";
            var hsmr = headGo.GetComponent<SkinnedMeshRenderer>();
            hsmr.sharedMesh = headMesh;
            hsmr.localBounds = lb;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = playerLayer;
            headGo.layer = localHead;

            // ---- риг
            var rig = avatar.gameObject.AddComponent<GorillaFinalRig>();
            rig.head = cam.transform;
            rig.eyeLocal = Vector3.zero;
            rig.headBone = B("Head");
            float palm = Vector3.Distance(lH.position, B("Left_MiddleProximal").position) * 0.9f;
            rig.left = new GorillaFinalRig.Limb { upper = lU, lower = lL, hand = lH, side = -1f, palmOffset = palm, rotationOffset = new Vector3(0, 0, -90),
                target = gp ? gp.leftHandFollower : null, rotationSource = offset.Find("Left Controller") };
            rig.right = new GorillaFinalRig.Limb { upper = rU, lower = rL, hand = rH, side = 1f, palmOffset = palm, rotationOffset = new Vector3(0, 0, -66), // у правой кисти Tripo-рига оси перевёрнуты
                target = gp ? gp.rightHandFollower : null, rotationSource = offset.Find("Right Controller") };

            // ---- косметика на костях
            var hb = BakedBounds(hsmr);
            var items = BuildCosmetics(rig.headBone, B("UpperChest"), hb, eyeW: avatar.position, bodySmr: smr, localHead, playerLayer);
            var pc = avatar.gameObject.AddComponent<PlayerCosmetics>();
            pc.items = items;

            // ---- перекраска шерсти
            if (!root.TryGetComponent<PlayerSkinController>(out var skinCtl)) skinCtl = root.AddComponent<PlayerSkinController>();
            skinCtl.furRenderers = new Renderer[] { smr, hsmr };
            skinCtl.furMaterial = mFur;
            skinCtl.furColor = furDefault;
            skinCtl.presets[0] = furDefault;

            if (!root.GetComponent<GorillaStickTurn>()) root.AddComponent<GorillaStickTurn>(); // поворот стиком (GRABZ OS → TURN)
            SetupFingers(root);

            cam.cullingMask &= ~(1 << localHead);
            cam.nearClipPlane = 0.01f;
            foreach (var f in new[] { gp ? gp.leftHandFollower : null, gp ? gp.rightHandFollower : null })
                if (f && f.TryGetComponent<MeshRenderer>(out var fr)) fr.enabled = false;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            return $"[GorillaFinal] scale={s:0.000}, eye height above feet={(EyeHeight - footY):0.00} m (body bottom ≈ {(BakedBounds(smr).min.y):0.00} m), " +
                   $"arm={ArmLength} m, palm={palm:0.000}, head bounds={hb.size}, fur default={furDefault}, " +
                   $"body tris={bodyMesh.triangles.Length / 3}, head tris={headMesh.triangles.Length / 3}";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static GameObject[] BuildCosmetics(Transform headBone, Transform chestBone, Bounds hb, Vector3 eyeW, SkinnedMeshRenderer bodySmr, int headLayer, int bodyLayer)
    {
        var gold = TripoGorillaAvatarBuilder.Mat("M_Cos_Gold", null, new Color(1f, 0.76f, 0.2f), 0.55f, 0.9f, new Color(0.35f, 0.22f, 0.02f));
        var black = TripoGorillaAvatarBuilder.Mat("M_Cos_Black", null, new Color(0.04f, 0.04f, 0.05f), 0.0f, 0.2f);
        var ruby = TripoGorillaAvatarBuilder.Mat("M_Cos_Ruby", null, new Color(0.85f, 0.05f, 0.12f), 0f, 0.8f, new Color(0.4f, 0f, 0.03f));
        var sapphire = TripoGorillaAvatarBuilder.Mat("M_Cos_Sapphire", null, new Color(0.1f, 0.3f, 0.95f), 0f, 0.8f, new Color(0f, 0.08f, 0.4f));
        var prim = (Func<Transform, string, PrimitiveType, Vector3, Quaternion, Vector3, Material, GameObject>)TripoGorillaAvatarBuilder.Prim;

        float headW = hb.size.x, headD = hb.size.z;

        // Корона — на макушке
        var crown = new GameObject("Crown").transform;
        crown.position = new Vector3(hb.center.x, hb.max.y - 0.1f, hb.center.z - headD * 0.05f);
        float r = Mathf.Min(headW, headD) * 0.3f;
        prim(crown, "Band", PrimitiveType.Cylinder, new Vector3(0, 0.035f, 0), Quaternion.identity, new Vector3(r * 2, 0.035f, r * 2), gold);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI * 2 / 6;
            prim(crown, "Spike", PrimitiveType.Cube, new Vector3(Mathf.Cos(a) * r * 0.92f, 0.1f, Mathf.Sin(a) * r * 0.92f), Quaternion.Euler(0, -a * Mathf.Rad2Deg, 45), new Vector3(0.045f, 0.045f, 0.02f), gold);
            prim(crown, "Gem", PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * r, 0.04f, Mathf.Sin(a) * r), Quaternion.identity, Vector3.one * 0.022f, i % 2 == 0 ? ruby : sapphire);
        }

        // Маска грабителя — повязка на уровне глаз
        var mask = new GameObject("RobberMask").transform;
        mask.position = new Vector3(hb.center.x, eyeW.y - 0.04f, hb.center.z);
        prim(mask, "Band", PrimitiveType.Cylinder, Vector3.zero, Quaternion.identity, new Vector3(headW * 1.02f, 0.04f, headD * 1.04f), black);
        prim(mask, "Knot", PrimitiveType.Sphere, new Vector3(0, 0, -headD * 0.53f), Quaternion.identity, new Vector3(0.05f, 0.04f, 0.04f), black);
        prim(mask, "TailL", PrimitiveType.Cube, new Vector3(-0.025f, -0.05f, -headD * 0.55f), Quaternion.Euler(0, 0, 20), new Vector3(0.025f, 0.09f, 0.01f), black);
        prim(mask, "TailR", PrimitiveType.Cube, new Vector3(0.025f, -0.05f, -headD * 0.55f), Quaternion.Euler(0, 0, -20), new Vector3(0.025f, 0.09f, 0.01f), black);

        // Золотая цепь — на груди, под головой
        var bb = BakedBounds(bodySmr);
        var chain = new GameObject("GoldChain").transform;
        float chestY = hb.min.y - 0.02f;
        chain.position = new Vector3(hb.center.x, chestY, hb.center.z + headD * 0.1f);
        chain.rotation = Quaternion.Euler(-18, 0, 0);
        float crx = headW * 0.62f, crz = headD * 0.62f;
        for (int i = 0; i < 20; i++)
        {
            float a = i * Mathf.PI * 2 / 20;
            prim(chain, "Link", PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * crx, 0, Mathf.Sin(a) * crz), Quaternion.identity, Vector3.one * 0.035f, gold);
        }
        var medal = prim(chain, "Medal", PrimitiveType.Cylinder, new Vector3(0, -0.07f, crz + 0.015f), Quaternion.Euler(90, 0, 0), new Vector3(0.1f, 0.01f, 0.1f), gold);
        prim(medal.transform, "Dollar", PrimitiveType.Cube, new Vector3(0, 1.2f, 0), Quaternion.identity, new Vector3(0.18f, 1f, 0.6f), ruby);

        crown.SetParent(headBone, true); mask.SetParent(headBone, true); chain.SetParent(chestBone, true);
        TripoGorillaAvatarBuilder.SetLayer(crown, headLayer); TripoGorillaAvatarBuilder.SetLayer(mask, headLayer); TripoGorillaAvatarBuilder.SetLayer(chain, bodyLayer);
        var items = new[] { crown.gameObject, mask.gameObject, chain.gameObject };
        foreach (var it in items) it.SetActive(false);
        return items;
    }

    /// Пальцы от Grip/Trigger (VRHandPoseController) + точные триггеры клавиатуры на кончиках указательных.
    public static string SetupFingers(GameObject root)
    {
        var rig = root.GetComponentInChildren<GorillaFinalRig>(true);
        if (!rig) return "[Fingers] no rig";
        rig.mirrorRightFromLeft = true;
        var pose = root.GetComponent<VRHandPoseController>() ?? root.AddComponent<VRHandPoseController>();
        pose.avatarRoot = rig.transform;
        int ignore = LayerMask.NameToLayer("Ignore Raycast");
        string report = "";
        foreach (var (prefix, isLeft) in new[] { ("Left_", true), ("Right_", false) })
        {
            var hand = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == prefix + "Hand");
            if (hand) (isLeft ? pose.left : pose.right).hand = hand;
            var tipBone = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == prefix + "IndexDistalEnd");
            if (!tipBone) continue;
            var old = tipBone.Find("KeyTipTrigger");
            if (old) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("KeyTipTrigger");
            go.layer = ignore;
            go.transform.SetParent(tipBone, false);
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.018f / Mathf.Max(1e-4f, tipBone.lossyScale.x);   // 1.8 см в мире
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            go.AddComponent<ComputerKeyTrigger>().isLeft = isLeft;
            report += $"{prefix}tip r={col.radius:0.0000} ";
        }
        return "[Fingers] " + report;
    }

    // ------------------------------------------------------------------ helpers
    static Bounds BakedBounds(SkinnedMeshRenderer smr)
    {
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var v = baked.vertices;
        var tris = baked.triangles; // только используемые вершины
        var b = new Bounds(smr.transform.TransformPoint(v[tris[0]]), Vector3.zero);
        foreach (int i in tris) b.Encapsulate(smr.transform.TransformPoint(v[i]));
        Object.DestroyImmediate(baked);
        return b;
    }

    static Texture2D MakeFurTexture(Color[] px, int W, int H, Color furMean)
    {
        float meanLum = Mathf.Max(0.02f, 0.299f * furMean.r + 0.587f * furMean.g + 0.114f * furMean.b);
        int step = Mathf.Max(1, W / FurTexSize);
        int w = W / step, h = H / step;
        var outPx = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float l = 0;
                for (int dy = 0; dy < step; dy++) for (int dx = 0; dx < step; dx++)
                {
                    var c = px[(y * step + dy) * W + x * step + dx];
                    l += 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                }
                l /= step * step;
                float v = Mathf.Clamp01(FurTexScale * (1f + 0.45f * (l / meanLum - 1f)));
                outPx[y * w + x] = new Color(v, v, v, 1f);
            }
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.SetPixels(outPx); t.Apply();
        string path = $"{OutDir}/T_GorillaFinal_FurNeutral.png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.maxTextureSize = FurTexSize; ti.filterMode = FilterMode.Bilinear; ti.mipmapEnabled = true; ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Texture2D MakeSkinTexture(Color[] px, int W, int H, Func<Color, bool> green, Color skinMean)
    {
        int step = Mathf.Max(1, W / FurTexSize);
        int w = W / step, h = H / step;
        var outPx = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color sum = Color.black;
                for (int dy = 0; dy < step; dy++) for (int dx = 0; dx < step; dx++)
                {
                    var c = px[(y * step + dy) * W + x * step + dx];
                    sum += green(c) ? skinMean : c;
                }
                var o = sum / (step * step); o.a = 1f;
                outPx[y * w + x] = o;
            }
        // зелёное поле вокруг островков тоже серое → мип-уровни не подмешивают зелень
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.SetPixels(outPx); t.Apply();
        string path = $"{OutDir}/T_GorillaFinal_Skin.png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.maxTextureSize = FurTexSize; ti.filterMode = FilterMode.Bilinear; ti.mipmapEnabled = true; ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material GetMat(string path, Texture tex)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        TripoAssetPostprocessor.MakeMatte(m);
        EditorUtility.SetDirty(m);
        return m;
    }
    static Mesh SaveMesh(Mesh m, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(m, path); return m; }
        EditorUtility.CopySerialized(m, existing);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    static Color Avg(IEnumerable<Color> cs)
    {
        Color sum = Color.black; int n = 0;
        foreach (var c in cs) { sum += c; n++; }
        return n > 0 ? sum / n : new Color(0.3f, 0.55f, 0.2f);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
