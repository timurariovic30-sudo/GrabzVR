using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Ставит сгенерированную Tripo-гориллу (Assets/Art/GeneratedModels/Player_Gorilla.glb) в префаб VRGorillaPlayer.
/// У модели нет костей, поэтому меш режется на части:
///   Torso (без ног — как в Gorilla Tag), Head (слой LocalHead — своей камере не виден, в зеркале виден),
///   Arm_L / Arm_R (опора в плече, GorillaAvatarRig поворачивает и тянет руку к VR-контроллеру).
/// Каждая часть ещё делится по цвету текстуры: тёмная шерсть (перекрашивается PlayerSkinController)
/// и светлые живот/морда/ладони (цвет не меняется).
/// Меню: Tools/Heist/Player/Build Tripo Gorilla Avatar.
/// </summary>
public static class TripoGorillaAvatarBuilder
{
    const string ModelPath = "Assets/Art/GeneratedModels/Player_Gorilla.glb";
    const string TexPath = "Assets/Art/GeneratedModels/Player_Gorilla/Textures/Player_Gorilla_Mat0_BaseColor.png";
    const string PrefabPath = "Assets/Prefabs/Player/VRGorillaPlayer.prefab";
    const string OutDir = "Assets/Art/Models/Generated/Gorilla";
    const string MatDir = "Assets/Art/Materials/Player";

    // Настройки нарезки (доли высоты модели)
    public static float LegCut = 0.30f;        // всё ниже этой доли высоты — ноги, выкидываем
    public static float ArmReach = 0.62f;      // длина руки (плечо → кончик кисти) в метрах после масштаба
    public static float FurTexScale = 0.8f;    // яркость «нейтральной» текстуры шерсти

    [MenuItem("Tools/Heist/Player/(Old) Build Tripo Gorilla Avatar v1")]
    public static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) ?? throw new Exception("No model " + ModelPath);
        var mf = model.GetComponentsInChildren<MeshFilter>(true).OrderByDescending(m => m.sharedMesh.vertexCount).First();
        var src = mf.sharedMesh;
        if (!src.isReadable) throw new Exception("Mesh is not readable: " + src.name);
        Matrix4x4 toModel = model.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;

        var verts = src.vertices.Select(v => toModel.MultiplyPoint3x4(v)).ToArray();
        var normals = src.normals.Select(n => toModel.MultiplyVector(n).normalized).ToArray();
        var uvs = src.uv;
        var tris = src.triangles;

        // ---------- текстура (читаем PNG 128px, сделанный постпроцессором)
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(TexPath));
        Color Sample(Vector2 uv) => tex.GetPixelBilinear(uv.x, uv.y);

        // ---------- ориентация: руки должны идти вдоль X, лицо/живот (светлые участки) смотреть в +Z
        {
            var b0 = new Bounds(verts[0], Vector3.zero);
            foreach (var v in verts) b0.Encapsulate(v);
            Quaternion fix = Quaternion.identity;
            if (b0.size.z > b0.size.x) fix = Quaternion.Euler(0, 90, 0);
            Vector3 Rot(Vector3 v) => fix * (v - b0.center) + b0.center;
            verts = verts.Select(Rot).ToArray();
            normals = normals.Select(n => fix * n).ToArray();
            // светлые треугольники средней части тела = живот → сторона «вперёд»
            float midLo = b0.min.y + b0.size.y * 0.35f, midHi = b0.min.y + b0.size.y * 0.7f;
            float zLight = 0f, zAll = 0f; int nLight = 0, nAll = 0;
            for (int t = 0; t < tris.Length / 3; t++)
            {
                int a = tris[t * 3], c1 = tris[t * 3 + 1], c2 = tris[t * 3 + 2];
                Vector3 cen = (verts[a] + verts[c1] + verts[c2]) / 3f;
                if (cen.y < midLo || cen.y > midHi || Mathf.Abs(cen.x - b0.center.x) > b0.size.x * 0.15f) continue;
                Color cc = (Sample(uvs[a]) + Sample(uvs[c1]) + Sample(uvs[c2])) / 3f;
                float l = 0.299f * cc.r + 0.587f * cc.g + 0.114f * cc.b;
                zAll += cen.z; nAll++;
                if (l > 0.35f) { zLight += cen.z; nLight++; }
            }
            if (nLight > 0 && nAll > 0 && zLight / nLight < zAll / nAll)
            {
                var flip = Quaternion.Euler(0, 180, 0);
                verts = verts.Select(v => flip * (v - b0.center) + b0.center).ToArray();
                normals = normals.Select(n => flip * n).ToArray();
            }
        }

        // ---------- габариты и срезы
        var b = new Bounds(verts[0], Vector3.zero);
        foreach (var v in verts) b.Encapsulate(v);
        float h = b.size.y, y0 = b.min.y;
        float legY = y0 + LegCut * h;

        const int Bins = 48;
        var width = new float[Bins];
        foreach (var v in verts)
        {
            int i = Mathf.Clamp((int)((v.y - y0) / h * Bins), 0, Bins - 1);
            width[i] = Mathf.Max(width[i], Mathf.Abs(v.x - b.center.x));
        }
        float maxW = width.Max();
        var armBins = Enumerable.Range(0, Bins).Where(i => width[i] > 0.62f * maxW).ToArray();
        float armYmin = y0 + armBins.Min() * h / Bins, armYmax = y0 + (armBins.Max() + 1) * h / Bins;
        // ширина торса: срезы чуть ниже рук
        float torsoW = 0f;
        for (int i = 0; i < Bins; i++)
        {
            float yy = y0 + (i + 0.5f) * h / Bins;
            if (yy < armYmin - 0.16f * h || yy > armYmin - 0.02f * h) continue;
            torsoW = Mathf.Max(torsoW, width[i]);
        }
        if (torsoW <= 0f) torsoW = maxW * 0.35f;
        float shoulderX = torsoW * 1.0f;
        float cx = b.center.x;

        // ---------- классификация треугольников
        int triCount = tris.Length / 3;
        var part = new int[triCount];   // 0 torso, 1 head, 2 armL, 3 armR, -1 drop
        var isSkin = new bool[triCount];
        var lum = new float[triCount];
        var col = new Color[triCount];
        for (int t = 0; t < triCount; t++)
        {
            int a = tris[t * 3], c1 = tris[t * 3 + 1], c2 = tris[t * 3 + 2];
            Vector3 cen = (verts[a] + verts[c1] + verts[c2]) / 3f;
            Color cc = (Sample(uvs[a]) + Sample(uvs[c1]) + Sample(uvs[c2]) + Sample((uvs[a] + uvs[c1] + uvs[c2]) / 3f)) / 4f;
            col[t] = cc;
            lum[t] = 0.299f * cc.r + 0.587f * cc.g + 0.114f * cc.b;
            float ax = cen.x - cx;
            if (cen.y < legY) part[t] = -1;
            else if (Mathf.Abs(ax) > shoulderX && cen.y > armYmin - 0.06f * h) part[t] = ax < 0 ? 2 : 3;
            else if (cen.y > armYmax + 0.01f * h) part[t] = 1;
            else part[t] = 0;
        }
        // порог шерсть/кожа: 2-кластерный k-means по яркости
        float lo = lum.Min(), hi = lum.Max();
        for (int it = 0; it < 12; it++)
        {
            float mid = (lo + hi) / 2f;
            var darks = lum.Where(l => l <= mid).DefaultIfEmpty(lo).Average();
            var lights = lum.Where(l => l > mid).DefaultIfEmpty(hi).Average();
            lo = darks; hi = lights;
        }
        float threshold = (lo + hi) / 2f;
        for (int t = 0; t < triCount; t++) isSkin[t] = lum[t] > threshold;
        Color furMean = Avg(Enumerable.Range(0, triCount).Where(t => !isSkin[t] && part[t] >= 0).Select(t => col[t]));

        // ---------- опорные точки (в осях модели; лицо модели смотрит в +Z)
        var headTris = Enumerable.Range(0, triCount).Where(t => part[t] == 1).ToArray();
        var hb = BoundsOf(headTris, tris, verts);
        float neckY = armYmax;
        Vector3 eye = new Vector3(cx, hb.center.y + hb.extents.y * 0.1f, hb.center.z + hb.extents.z * 0.25f);
        Vector3 neck = new Vector3(cx, neckY, hb.center.z);
        var armL = BoundsOf(Enumerable.Range(0, triCount).Where(t => part[t] == 2).ToArray(), tris, verts);
        var armR = BoundsOf(Enumerable.Range(0, triCount).Where(t => part[t] == 3).ToArray(), tris, verts);
        Vector3 shL = new Vector3(cx - shoulderX, armL.center.y, armL.center.z);
        Vector3 shR = new Vector3(cx + shoulderX, armR.center.y, armR.center.z);
        float reachModel = ((shL.x - armL.min.x) + (armR.max.x - shR.x)) / 2f;
        float s = ArmReach / Mathf.Max(0.01f, reachModel);

        // ---------- меши частей
        EnsureFolder(OutDir); EnsureFolder(MatDir);
        Mesh MakePart(string name, int partId, bool skin, Vector3 pivot)
        {
            var sel = Enumerable.Range(0, triCount).Where(t => part[t] == partId && isSkin[t] == skin).ToArray();
            var map = new Dictionary<int, int>();
            var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>(); var nt = new List<int>();
            foreach (int t in sel)
                for (int k = 0; k < 3; k++)
                {
                    int vi = tris[t * 3 + k];
                    if (!map.TryGetValue(vi, out int ni))
                    {
                        ni = nv.Count; map[vi] = ni;
                        nv.Add((verts[vi] - pivot) * s); nn.Add(normals[vi]); nu.Add(uvs[vi]);
                    }
                    nt.Add(ni);
                }
            var m = new Mesh { name = "Gorilla_" + name };
            m.SetVertices(nv); m.SetNormals(nn); m.SetUVs(0, nu); m.SetTriangles(nt, 0);
            m.RecalculateBounds();
            return SaveMesh(m, $"{OutDir}/Gorilla_{name}.asset");
        }

        // ---------- материалы и текстура шерсти
        var furTex = MakeFurTexture(tex, furMean);
        var mFur = Mat("M_Gorilla_Fur", furTex, Color.white);
        var mSkin = Mat("M_Gorilla_Skin", AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath), Color.white);
        Color furDefault = new Color(furMean.r / FurTexScale, furMean.g / FurTexScale, furMean.b / FurTexScale, 1f);

        // ---------- префаб
        int playerLayer = LayerMask.NameToLayer("Player"), localHead = LayerMask.NameToLayer("LocalHead");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        string report;
        try
        {
            foreach (var n in new[] { "MonkeyAvatar", "GorillaAvatar" })
            {
                var old = root.transform.Find(n);
                if (old) Object.DestroyImmediate(old.gameObject);
            }
            var cam = root.GetComponentInChildren<Camera>(true) ?? throw new Exception("Camera not found");
            var gp = root.GetComponent<GorillaLocomotion.Player>();

            var avatar = new GameObject("GorillaAvatar").transform;
            avatar.SetParent(root.transform, false);
            avatar.position = cam.transform.position;
            var furRenderers = new List<Renderer>();

            Transform Piece(Transform parent, string name, int partId, Vector3 pivot, int layer)
            {
                var p = new GameObject(name).transform;
                p.SetParent(parent, false);
                p.localPosition = (pivot - eye) * s - (parent == avatar ? Vector3.zero : parent.localPosition);
                foreach (bool skin in new[] { false, true })
                {
                    var mesh = MakePart($"{name}_{(skin ? "Skin" : "Fur")}", partId, skin, pivot);
                    if (mesh.vertexCount == 0) continue;
                    var go = new GameObject(skin ? "Skin" : "Fur");
                    go.transform.SetParent(p, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = skin ? mSkin : mFur;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    go.layer = layer;
                    if (!skin) furRenderers.Add(mr);
                }
                p.gameObject.layer = layer;
                return p;
            }

            var torso = Piece(avatar, "Torso", 0, eye, playerLayer);           // торс: опора = точка глаз (корень)
            var headPivot = Piece(avatar, "HeadPivot", 1, neck, localHead);
            var armLeft = Piece(avatar, "Arm_L", 2, shL, playerLayer);
            var armRight = Piece(avatar, "Arm_R", 3, shR, playerLayer);

            // Косметика: корона и маска на голове (LocalHead), цепь на груди
            var tb = BoundsOf(Enumerable.Range(0, triCount).Where(t => part[t] == 0).ToArray(), tris, verts);
            var chest = new Vector3(cx, armYmin - 0.03f * h, tb.center.z);
            var cosmetics = BuildCosmetics(headPivot, torso, hb, eye, neck, s, localHead, playerLayer, chest, shoulderX, tb.extents.z);

            var rig = avatar.gameObject.AddComponent<GorillaAvatarRig>();
            rig.head = cam.transform;
            rig.headPivot = headPivot;
            rig.leftArm = new GorillaAvatarRig.Arm { pivot = armLeft, restDir = Vector3.left, restLength = (shL.x - armL.min.x) * s, target = gp ? gp.leftHandFollower : null };
            rig.rightArm = new GorillaAvatarRig.Arm { pivot = armRight, restDir = Vector3.right, restLength = (armR.max.x - shR.x) * s, target = gp ? gp.rightHandFollower : null };

            var pc = avatar.gameObject.AddComponent<PlayerCosmetics>();
            pc.items = cosmetics;

            if (!root.TryGetComponent<PlayerSkinController>(out var skinCtl)) skinCtl = root.AddComponent<PlayerSkinController>();
            skinCtl.furRenderers = furRenderers.ToArray();
            skinCtl.furColor = furDefault;
            skinCtl.presets[0] = furDefault;

            cam.cullingMask &= ~(1 << localHead);
            foreach (var f in new[] { gp?.leftHandFollower, gp?.rightHandFollower })
                if (f && f.TryGetComponent<MeshRenderer>(out var fr)) fr.enabled = false;

            // Полировка: прыжок 6.5 → 7.5, магнит хвата +30%
            if (gp) gp.maxJumpSpeed = 7.5f;
            foreach (var g in root.GetComponentsInChildren<VRGrabber>(true))
                if (g.input != VRGrabber.GrabInput.Mouse) g.handGrabRadius = 0.195f;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            int total = furRenderers.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3);
            report = $"[TripoGorilla] scale={s:0.000}, height(model)={h:0.00}, legCut y<{legY:0.00}, arms y[{armYmin:0.00},{armYmax:0.00}] shoulderX={shoulderX:0.00}, " +
                     $"skin threshold={threshold:0.00} (fur {lo:0.00} / light {hi:0.00}), furMean={furMean}, fur tris={total}, " +
                     $"restLen L={(shL.x - armL.min.x) * s:0.00} R={(armR.max.x - shR.x) * s:0.00}, torso height={(eye.y - legY) * s:0.00} m";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        return report;
    }

    // ------------------------------------------------------------------ косметика
    static GameObject[] BuildCosmetics(Transform headPivot, Transform torso, Bounds hb, Vector3 eye, Vector3 neck, float s, int headLayer, int bodyLayer, Vector3 chest, float chestHalfW, float chestHalfD)
    {
        var gold = Mat("M_Cos_Gold", null, new Color(1f, 0.76f, 0.2f), 0.55f, 0.9f, new Color(0.35f, 0.22f, 0.02f));
        var black = Mat("M_Cos_Black", null, new Color(0.04f, 0.04f, 0.05f), 0.0f, 0.2f);
        var ruby = Mat("M_Cos_Ruby", null, new Color(0.85f, 0.05f, 0.12f), 0f, 0.8f, new Color(0.4f, 0f, 0.03f));
        var sapphire = Mat("M_Cos_Sapphire", null, new Color(0.1f, 0.3f, 0.95f), 0f, 0.8f, new Color(0f, 0.08f, 0.4f));

        // координаты относительно опоры головы (шеи), в метрах
        Vector3 H(Vector3 modelPoint) => (modelPoint - neck) * s;
        float headW = hb.size.x * s, headD = hb.size.z * s, headTop = H(new Vector3(0, hb.max.y, 0)).y;
        Vector3 headC = H(hb.center);

        // Корона
        var crown = new GameObject("Crown").transform;
        crown.SetParent(headPivot, false);
        crown.localPosition = new Vector3(headC.x, headTop - 0.02f, headC.z);
        float r = Mathf.Min(headW, headD) * 0.36f;
        Prim(crown, "Band", PrimitiveType.Cylinder, new Vector3(0, 0.035f, 0), Quaternion.identity, new Vector3(r * 2, 0.035f, r * 2), gold);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI * 2 / 6;
            var p = new Vector3(Mathf.Cos(a) * r * 0.92f, 0.1f, Mathf.Sin(a) * r * 0.92f);
            Prim(crown, "Spike", PrimitiveType.Cube, p, Quaternion.Euler(0, -a * Mathf.Rad2Deg, 45), new Vector3(0.045f, 0.045f, 0.02f), gold);
            Prim(crown, "Gem", PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * r * 1.0f, 0.04f, Mathf.Sin(a) * r * 1.0f), Quaternion.identity, Vector3.one * 0.022f, i % 2 == 0 ? ruby : sapphire);
        }

        // Маска грабителя: чёрная повязка вокруг глаз
        var mask = new GameObject("RobberMask").transform;
        mask.SetParent(headPivot, false);
        Vector3 e = H(eye);
        mask.localPosition = new Vector3(headC.x, e.y, headC.z);
        Prim(mask, "Band", PrimitiveType.Cylinder, Vector3.zero, Quaternion.identity, new Vector3(headW * 1.04f, 0.035f, headD * 1.04f), black);
        Prim(mask, "Knot", PrimitiveType.Sphere, new Vector3(0, 0, -headD * 0.53f), Quaternion.identity, new Vector3(0.05f, 0.04f, 0.04f), black);
        Prim(mask, "TailL", PrimitiveType.Cube, new Vector3(-0.025f, -0.05f, -headD * 0.55f), Quaternion.Euler(0, 0, 20), new Vector3(0.025f, 0.09f, 0.01f), black);
        Prim(mask, "TailR", PrimitiveType.Cube, new Vector3(0.025f, -0.05f, -headD * 0.55f), Quaternion.Euler(0, 0, -20), new Vector3(0.025f, 0.09f, 0.01f), black);

        // Золотая цепь на шее с медальоном «$»
        var chain = new GameObject("GoldChain").transform;
        chain.SetParent(torso, false);
        chain.localPosition = (chest - eye) * s;
        chain.localRotation = Quaternion.Euler(-18, 0, 0);
        float crx = chestHalfW * s * 0.95f, crz = chestHalfD * s * 1.02f;
        for (int i = 0; i < 20; i++)
        {
            float a = i * Mathf.PI * 2 / 20;
            Prim(chain, "Link", PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * crx, 0, Mathf.Sin(a) * crz), Quaternion.identity, Vector3.one * 0.035f, gold);
        }
        var medal = Prim(chain, "Medal", PrimitiveType.Cylinder, new Vector3(0, -0.07f, crz + 0.015f), Quaternion.Euler(90, 0, 0), new Vector3(0.1f, 0.01f, 0.1f), gold);
        Prim(medal.transform, "Dollar", PrimitiveType.Cube, new Vector3(0, 1.2f, 0), Quaternion.identity, new Vector3(0.18f, 1f, 0.6f), ruby);

        SetLayer(crown, headLayer); SetLayer(mask, headLayer); SetLayer(chain, bodyLayer);
        var items = new[] { crown.gameObject, mask.gameObject, chain.gameObject };
        foreach (var it in items) it.SetActive(false);
        return items;
    }

    internal static GameObject Prim(Transform parent, string name, PrimitiveType type, Vector3 localPos, Quaternion localRot, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = scale;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    internal static void SetLayer(Transform t, int layer)
    {
        foreach (var c in t.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = layer;
    }

    // ------------------------------------------------------------------ helpers
    static Texture2D MakeFurTexture(Texture2D src, Color furMean)
    {
        float meanLum = Mathf.Max(0.02f, 0.299f * furMean.r + 0.587f * furMean.g + 0.114f * furMean.b);
        var px = src.GetPixels();
        for (int i = 0; i < px.Length; i++)
        {
            float l = 0.299f * px[i].r + 0.587f * px[i].g + 0.114f * px[i].b;
            float v = Mathf.Clamp01(FurTexScale * (1f + 0.45f * (l / meanLum - 1f))); // вариации шерсти приглушены — меньше «пятен»
            px[i] = new Color(v, v, v, 1f);
        }
        var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        t.SetPixels(px); t.Apply();
        string path = $"{OutDir}/T_Gorilla_FurNeutral.png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.maxTextureSize = 128; ti.filterMode = FilterMode.Bilinear; ti.mipmapEnabled = true; ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    internal static Material Mat(string name, Texture tex, Color color, float metallic = 0f, float smoothness = 0f, Color? emission = null)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        if (smoothness <= 0f) TripoAssetPostprocessor.MakeMatte(m);
        if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); }
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

    static Bounds BoundsOf(int[] triIds, int[] tris, Vector3[] verts)
    {
        if (triIds.Length == 0) return new Bounds(Vector3.zero, Vector3.one * 0.1f);
        var b = new Bounds(verts[tris[triIds[0] * 3]], Vector3.zero);
        foreach (int t in triIds) for (int k = 0; k < 3; k++) b.Encapsulate(verts[tris[t * 3 + k]]);
        return b;
    }

    static Color Avg(IEnumerable<Color> cs)
    {
        Color sum = Color.black; int n = 0;
        foreach (var c in cs) { sum += c; n++; }
        return n > 0 ? sum / n : new Color(0.17f, 0.17f, 0.19f);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
