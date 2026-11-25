using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Надевает модель обезьяны (Assets/Art/Models/monkey.glb) на префаб VRGorillaPlayer:
/// процедурные текстуры шерсти, URP-материалы, отделение головы (не видна от первого лица),
/// маска грабителя, глаза/мордочка/ладони, IK-риг рук, PlayerSkinController, зеркало в коридоре.
/// Меню: Tools > Heist > Setup Monkey Avatar. Повторный запуск пересобирает аватар.
/// </summary>
public static class HeistAvatarSetup
{
    const string ModelPath = "Assets/Art/Models/monkey.glb";
    const string PrefabPath = "Assets/Prefabs/Player/VRGorillaPlayer.prefab";
    const string MatFolder = "Assets/Art/Materials/Player";
    const string TexFolder = "Assets/Art/Textures/Player";
    const string GenMeshFolder = "Assets/Art/Models/Generated";
    const string ScenePath = "Assets/Scenes/HeistLevel_Test.unity";
    const string LocalHeadLayer = "LocalHead";
    const float ModelScale = 0.2f; // модель ~10 ед. в размахе рук → руки ~0.86 м

    static Material mFur, mFace, mEyes;

    [MenuItem("Tools/Heist/Setup Monkey Avatar")]
    public static void Setup()
    {
        try
        {
            EnsureFolder(MatFolder); EnsureFolder(TexFolder); EnsureFolder(GenMeshFolder);
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer < 0) throw new Exception("Layer 'Player' not found — run Tools > Heist > Setup VR Player first");
            int localHead = EnsureLayer(LocalHeadLayer);

            var (albedo, normal) = GenerateFurTextures();
            CreateMaterials(albedo, normal);
            BuildAvatarInPrefab(playerLayer, localHead);
            // Зеркало теперь живёт в лобби (Tools > Heist > Setup Lobby & Audio)
            AssetDatabase.SaveAssets();
            Debug.Log("[HeistAvatar] Done.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    // ------------------------------------------------------------------ textures

    static (Texture2D albedo, Texture2D normal) GenerateFurTextures()
    {
        const int N = 512;
        string albedoPath = TexFolder + "/T_MonkeyFur_Albedo.png";
        string normalPath = TexFolder + "/T_MonkeyFur_Normal.png";

        // Высота «шерсти»: вытянутые вдоль V пряди + мелкий шум. Периодический шум → текстура тайлится без швов.
        var h = new float[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float u = x / (float)N, v = y / (float)N;
            // Мягкий мелкий ворс: короткие ворсинки + мягкие «пучки», без резких полос
            float fine = TileNoise(u, v, 128, 96) * 0.5f + TileNoise(u + 0.37f, v + 0.11f, 256, 192) * 0.5f;
            float clumps = TileNoise(u + 0.71f, v + 0.53f, 16, 16);
            h[y * N + x] = Mathf.Clamp01(fine * 0.65f + clumps * 0.35f);
        }
        h = Blur(h, N, 2); // размытие → мягкий рельеф

        var albedo = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var normal = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var ca = new Color32[N * N];
        var cn = new Color32[N * N];
        const float strength = 2.0f;
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float hv = h[y * N + x];
            // Светлая, почти белая основа: итоговый цвет задаёт _BaseColor (скин).
            float g = Mathf.Lerp(0.82f, 1.0f, hv);
            ca[y * N + x] = new Color(g, g * 0.98f, g * 0.95f, 1f);

            float dx = h[y * N + (x + 1) % N] - h[y * N + (x - 1 + N) % N];
            float dy = h[((y + 1) % N) * N + x] - h[((y - 1 + N) % N) * N + x];
            var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
            cn[y * N + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
        }
        albedo.SetPixels32(ca); albedo.Apply();
        normal.SetPixels32(cn); normal.Apply();
        File.WriteAllBytes(albedoPath, albedo.EncodeToPNG());
        File.WriteAllBytes(normalPath, normal.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(albedo);
        UnityEngine.Object.DestroyImmediate(normal);

        AssetDatabase.ImportAsset(albedoPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(normalPath, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        ti.textureType = TextureImporterType.NormalMap;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.SaveAndReimport();
        var ta = (TextureImporter)AssetImporter.GetAtPath(albedoPath);
        ta.sRGBTexture = true;
        ta.wrapMode = TextureWrapMode.Repeat;
        ta.SaveAndReimport();

        return (AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath), AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
    }

    /// Бесшовное box-размытие (radius пикселей), несколько проходов ≈ гаусс.
    static float[] Blur(float[] src, int n, int radius)
    {
        var a = (float[])src.Clone(); var b = new float[a.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float s = 0; for (int k = -radius; k <= radius; k++) s += a[y * n + (x + k + n) % n];
                b[y * n + x] = s / (2 * radius + 1);
            }
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float s = 0; for (int k = -radius; k <= radius; k++) s += b[((y + k + n) % n) * n + x];
                a[y * n + x] = s / (2 * radius + 1);
            }
        }
        return a;
    }

    /// Сглаживание нормалей: вершины в одной точке получают общую усреднённую нормаль (убирает «пластиковые грани»).
    static void SmoothNormals(Mesh m)
    {
        var verts = m.vertices; var normals = m.normals;
        var groups = new Dictionary<Vector3Int, Vector3>();
        Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1000), Mathf.RoundToInt(p.y * 1000), Mathf.RoundToInt(p.z * 1000));
        for (int i = 0; i < verts.Length; i++)
        {
            var k = Key(verts[i]);
            groups[k] = (groups.TryGetValue(k, out var acc) ? acc : Vector3.zero) + normals[i];
        }
        for (int i = 0; i < verts.Length; i++) normals[i] = groups[Key(verts[i])].normalized;
        m.normals = normals;
        m.RecalculateTangents();
    }

    /// Периодический value-noise (бесшовный): fx/fy — число ячеек по U/V.
    static float TileNoise(float u, float v, int fx, int fy)
    {
        float x = Mathf.Repeat(u, 1f) * fx, y = Mathf.Repeat(v, 1f) * fy;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float tx = x - x0, ty = y - y0;
        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
        float a = Hash(x0 % fx, y0 % fy), b = Hash((x0 + 1) % fx, y0 % fy);
        float c = Hash(x0 % fx, (y0 + 1) % fy), d = Hash((x0 + 1) % fx, (y0 + 1) % fy);
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    static float Hash(int x, int y)
    {
        unchecked
        {
            uint hh = (uint)(x * 374761393 + y * 668265263);
            hh = (hh ^ (hh >> 13)) * 1274126177u;
            return ((hh ^ (hh >> 16)) & 0xFFFF) / 65535f;
        }
    }

    // ------------------------------------------------------------------ materials

    static void CreateMaterials(Texture2D albedo, Texture2D normal)
    {
        // Глубокий угольно-серый (не чистый чёрный — чтобы читался объём). Цвет = скин, его меняет PlayerSkinController.
        mFur = Mat("M_Monkey_Fur", new Color(0.17f, 0.17f, 0.19f), 0f, 0f);
        mFur.SetTexture("_BaseMap", albedo);
        mFur.SetTexture("_BumpMap", normal);
        mFur.SetFloat("_BumpScale", 0.35f);
        mFur.EnableKeyword("_NORMALMAP");
        mFur.SetTextureScale("_BaseMap", new Vector2(4f, 4f));
        mFur.SetFloat("_EnvironmentReflections", 0f);
        mFur.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

        // Лицо, живот, ладони — тёплый светло-серый/бежевый, матовый, с тем же мягким ворсом
        mFace = Mat("M_Monkey_Face", new Color(0.60f, 0.55f, 0.50f), 0f, 0f);
        mFace.SetTexture("_BaseMap", albedo);
        mFace.SetTexture("_BumpMap", normal);
        mFace.SetFloat("_BumpScale", 0.2f);
        mFace.EnableKeyword("_NORMALMAP");
        mFace.SetTextureScale("_BaseMap", new Vector2(4f, 4f));

        // Глаза-пуговки: тёмные, плоские, с лёгким бликом
        mEyes = Mat("M_Monkey_Eyes", new Color(0.03f, 0.03f, 0.035f), 0f, 0.3f);
        foreach (var m in new[] { mFur, mFace }) EditorUtility.SetDirty(m);

        // Маска больше не используется
        foreach (var unused in new[] { "M_Bandit_Mask", "M_Monkey_EyeWhite" })
            AssetDatabase.DeleteAsset($"{MatFolder}/{unused}.mat");
    }

    static Material Mat(string name, Color color, float metallic, float smoothness)
    {
        string path = $"{MatFolder}/{name}.mat";
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ head/body split

    /// Делит меш на «голова» (кости шеи/головы) и «всё остальное». Обе копии сохраняют вершины, кости и bindposes.
    static (Mesh body, Mesh head) SplitHead(Mesh src, Transform[] bones)
    {
        var headBoneIdx = new HashSet<int>();
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] && (bones[i].name.StartsWith("Bone.009") || bones[i].name.StartsWith("Bone.010"))) headBoneIdx.Add(i);

        var bw = src.boneWeights;
        bool IsHead(int v) => headBoneIdx.Contains(bw[v].boneIndex0);

        var tris = src.triangles;
        var bodyTris = new List<int>(); var headTris = new List<int>();
        for (int t = 0; t < tris.Length; t += 3)
        {
            bool head = IsHead(tris[t]) && IsHead(tris[t + 1]) && IsHead(tris[t + 2]);
            (head ? headTris : bodyTris).AddRange(new[] { tris[t], tris[t + 1], tris[t + 2] });
        }

        Mesh Make(string name, List<int> t)
        {
            var m = UnityEngine.Object.Instantiate(src);
            m.name = name;
            m.subMeshCount = 1;
            m.SetTriangles(t, 0);
            SmoothNormals(m);
            m.RecalculateBounds();
            return m;
        }
        var body = SaveMesh(Make("monkey_body", bodyTris));
        var headMesh = SaveMesh(Make("monkey_head", headTris));
        Debug.Log($"[HeistAvatar] Split: body tris={bodyTris.Count / 3}, head tris={headTris.Count / 3}");
        return (body, headMesh);
    }

    static Mesh SaveMesh(Mesh m)
    {
        string path = $"{GenMeshFolder}/{m.name}.asset";
        // Пересоздаём файл целиком: при смене раскладки вершин (например, добавились тангенты)
        // обновление «на месте» ломает уже привязанные SkinnedMeshRenderer.
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ------------------------------------------------------------------ prefab

    static void BuildAvatarInPrefab(int playerLayer, int localHead)
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var old = root.transform.Find("MonkeyAvatar");
            if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);

            var cam = root.GetComponentInChildren<Camera>(true);
            if (cam == null) throw new Exception("Camera not found in VRGorillaPlayer");
            var gp = root.GetComponent<GorillaLocomotion.Player>();

            // Контейнер: yaw + позиция от головы (MonkeyAvatarRig). Внутри — выравнивание модели (лицо модели смотрит в −X).
            var container = new GameObject("MonkeyAvatar").transform;
            container.SetParent(root.transform, false);
            container.position = cam.transform.position;
            var align = new GameObject("ModelAlign").transform;
            align.SetParent(container, false);
            align.localRotation = Quaternion.Euler(0, 90, 0);
            align.localScale = Vector3.one * ModelScale;

            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), align);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "MonkeyModel";
            // корень glb держит собственный поворот импорта — его не трогаем, только позицию
            model.transform.localPosition = Vector3.zero;

            // Лишние объекты без пользы
            var empty = FindDeep(model.transform, "Object_28");
            if (empty) UnityEngine.Object.DestroyImmediate(empty.gameObject);

            // Тело и голова
            var bodySmr = FindDeep(model.transform, "Object_29").GetComponent<SkinnedMeshRenderer>();
            var faceSmr = FindDeep(model.transform, "Object_30").GetComponent<SkinnedMeshRenderer>();
            var (bodyMesh, headMesh) = SplitHead(bodySmr.sharedMesh, bodySmr.bones);
            bodySmr.sharedMesh = bodyMesh;
            bodySmr.sharedMaterial = mFur;
            bodySmr.updateWhenOffscreen = true;
            bodySmr.name = "Monkey_Body";

            var headGo = new GameObject("Monkey_Head");
            headGo.transform.SetParent(bodySmr.transform.parent, false);
            headGo.transform.localPosition = bodySmr.transform.localPosition;
            headGo.transform.localRotation = bodySmr.transform.localRotation;
            headGo.transform.localScale = bodySmr.transform.localScale;
            var headSmr = headGo.AddComponent<SkinnedMeshRenderer>();
            headSmr.sharedMesh = headMesh;
            headSmr.bones = bodySmr.bones;
            headSmr.rootBone = bodySmr.rootBone;
            headSmr.sharedMaterial = mFur;
            headSmr.updateWhenOffscreen = true;

            faceSmr.sharedMaterial = mFace;
            faceSmr.updateWhenOffscreen = true;
            faceSmr.name = "Monkey_FacePlate";

            // Кости
            Transform B(string n) => FindDeep(model.transform, n) ?? throw new Exception("Bone not found: " + n);
            var headBone = B("Bone.010_010");

            // Аксессуары на голове (оси — в пространстве «ModelAlign» модели: лицо = −X, верх = +Y, правая сторона = +Z)
            var acc = new GameObject("HeadAccessories").transform;
            acc.SetParent(headBone, false);
            // Привязываем аксессуары в мировых координатах «как в T-позе», затем они едут вместе с костью головы.
            Vector3 headCenter = model.transform.position; // центр головы модели совпадает с корнем модели
            Quaternion modelRot = align.rotation;          // переводит оси модели в мир
            float s = ModelScale;
            Vector3 W(float x, float y, float z) => headCenter + modelRot * new Vector3(x, y, z) * s;

            // Лицо: плоский аккуратный овал (заменяет квадратную пластину модели), без выпирающего носа
            // Пластина модели закрывает отверстие в голове — оставляем её, но в цвет шерсти (её тоже красит скин).
            faceSmr.sharedMaterial = mFur;
            Prim(acc, "FaceOval", PrimitiveType.Sphere, W(-0.70f, -0.04f, 0), modelRot, new Vector3(0.2f, 1.22f, 1.1f) * s, mFace);

            // Глаза-пуговки: плоские тёмные диски, чуть утоплены в овал лица
            foreach (float z in new[] { -0.22f, 0.22f })
                Prim(acc, z < 0 ? "Eye_L" : "Eye_R", PrimitiveType.Sphere, W(-0.795f, 0.14f, z), modelRot, new Vector3(0.04f, 0.2f, 0.18f) * s, mEyes);

            // Живот: светлая овальная вставка на передней стороне торса (по размерам самого торса)
            var torsoBone = B("Bone_00");
            var bellyBounds = TorsoBounds(bodySmr, align, "Bone_00");
            var bellyLocal = new Vector3(bellyBounds.min.x + 0.07f, bellyBounds.center.y + bellyBounds.size.y * 0.2f, bellyBounds.center.z);
            Prim(torsoBone, "Belly", PrimitiveType.Sphere, align.TransformPoint(bellyLocal), modelRot,
                new Vector3(0.3f, bellyBounds.size.y * 0.8f, bellyBounds.size.z * 0.62f) * s, mFace);

            // Ладони (светлые) на кистях
            foreach (var handName in new[] { "Bone.004_04", "Bone.008_08" })
            {
                var hand = B(handName);
                var end = hand.GetChild(0);
                var palm = new GameObject("Palm").transform;
                palm.SetParent(hand, false);
                // ладонь — снизу кисти в T-позе (мировой −Y модели), на середине кисти
                Vector3 mid = Vector3.Lerp(hand.position, end.position, 0.55f);
                Prim(palm, "PalmPad", PrimitiveType.Sphere, mid + modelRot * Vector3.down * 0.2f * s, modelRot, new Vector3(0.36f, 0.12f, 0.5f) * s, mFace);
            }

            // Слои: всё тело/руки — Player; голова, лицо и аксессуары головы — LocalHead (не видны своей камере).
            foreach (var t in container.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = playerLayer;
            headGo.layer = localHead;
            faceSmr.gameObject.layer = localHead;
            foreach (var t in acc.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = localHead;

            // Никаких коллайдеров на модели
            foreach (var c in container.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);

            // Риг
            var rig = container.gameObject.AddComponent<MonkeyAvatarRig>();
            rig.head = cam.transform;
            rig.headBone = headBone;
            Transform lCtrl = gp ? gp.leftHandTransform : null, rCtrl = gp ? gp.rightHandTransform : null;
            rig.leftArm = new MonkeyAvatarRig.Arm { upper = B("Bone.002_02"), lower = B("Bone.003_03"), hand = B("Bone.004_04"), target = gp ? gp.leftHandFollower : null, controller = lCtrl };
            rig.rightArm = new MonkeyAvatarRig.Arm { upper = B("Bone.006_06"), lower = B("Bone.007_07"), hand = B("Bone.008_08"), target = gp ? gp.rightHandFollower : null, controller = rCtrl };

            // Скины
            if (!root.TryGetComponent<PlayerSkinController>(out var skin)) skin = root.AddComponent<PlayerSkinController>();
            skin.furRenderers = new Renderer[] { bodySmr, headSmr, faceSmr };
            skin.furColor = mFur.GetColor("_BaseColor"); // цвет по умолчанию = угольно-серый материала
            skin.presets = new[]
            {
                new Color(0.17f, 0.17f, 0.19f), new Color(0.45f, 0.29f, 0.17f), new Color(0.85f, 0.45f, 0.10f),
                new Color(0.25f, 0.55f, 0.95f), new Color(0.60f, 0.25f, 0.75f), new Color(0.95f, 0.95f, 0.92f),
            };

            // Камера игрока не видит свою голову
            cam.cullingMask &= ~(1 << localHead);

            // Старые сферы-руки больше не нужны визуально (коллайдер-триггер остаётся для лазеров)
            foreach (var f in new[] { gp?.leftHandFollower, gp?.rightHandFollower })
                if (f && f.TryGetComponent<MeshRenderer>(out var mr)) mr.enabled = false;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// Габариты торса в пространстве ModelAlign (до масштаба): вершины, которые в основном привязаны к кости торса.
    static Bounds TorsoBounds(SkinnedMeshRenderer smr, Transform align, string torsoBoneName)
    {
        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var bw = smr.sharedMesh.boneWeights;
        int torsoIdx = Array.FindIndex(smr.bones, b => b && b.name == torsoBoneName);
        var verts = baked.vertices;
        bool any = false; var bounds = new Bounds();
        for (int i = 0; i < verts.Length; i++)
        {
            if (bw[i].boneIndex0 != torsoIdx || bw[i].weight0 < 0.9f) continue;
            var p = align.InverseTransformPoint(smr.transform.TransformPoint(verts[i])); // align масштабирован → это единицы модели
            if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
        }
        UnityEngine.Object.DestroyImmediate(baked);
        if (!any) throw new Exception("Torso vertices not found");
        Debug.Log($"[HeistAvatar] Torso bounds (model units): {bounds}");
        return bounds;
    }

    static GameObject Prim(Transform parent, string name, PrimitiveType type, Vector3 worldPos, Quaternion worldRot, Vector3 worldScale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetPositionAndRotation(worldPos, worldRot);
        go.transform.localScale = worldScale;
        go.transform.SetParent(parent, true);
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = FindDeep(c, name); if (r) return r; }
        return null;
    }

    // ------------------------------------------------------------------ mirror

    static void SetupMirror(int localHead)
    {
        // Не выбрасываем несохранённое в открытом редакторе: если сцена уже открыта — работаем в ней,
        // иначе открываем дополнительно и потом закрываем.
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        Transform level = null;
        foreach (var r in scene.GetRootGameObjects()) if (r.name == "HeistLevel") level = r.transform;
        if (level == null) throw new Exception("HeistLevel not found in " + ScenePath);
        var old = level.Find("Mirror");
        if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);

        // Зеркало на стене Офиса со стороны коридора (стена z = 3, грань z = 2.85), смотрит в коридор (−Z).
        var mirror = new GameObject("Mirror").transform;
        mirror.SetParent(level, false);
        mirror.SetPositionAndRotation(new Vector3(-5.5f, 1.35f, 2.83f), Quaternion.Euler(0, 180, 0));

        string rtPath = TexFolder + "/RT_Mirror.renderTexture";
        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(rtPath);
        if (rt == null) { rt = new RenderTexture(512, 640, 24) { name = "RT_Mirror" }; AssetDatabase.CreateAsset(rt, rtPath); }

        var mirMat = AssetDatabase.LoadAssetAtPath<Material>(MatFolder + "/M_Mirror.mat");
        if (mirMat == null) { mirMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(mirMat, MatFolder + "/M_Mirror.mat"); }
        mirMat.SetTexture("_BaseMap", rt);
        mirMat.SetTextureScale("_BaseMap", new Vector2(-1, 1)); // отражение по горизонтали
        mirMat.SetTextureOffset("_BaseMap", new Vector2(1, 0));
        EditorUtility.SetDirty(mirMat);

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "MirrorSurface";
        UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(mirror, false);
        // Quad видим со стороны −Z своей локали; зеркало повёрнуто на 180°, поэтому разворачиваем quad обратно к коридору.
        quad.transform.localRotation = Quaternion.Euler(0, 180, 0);
        quad.transform.localScale = new Vector3(1.6f, 2.0f, 1f);
        quad.GetComponent<MeshRenderer>().sharedMaterial = mirMat;

        var gold = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/M_Loot_Gold.mat");
        void Frame(string n, Vector3 p, Vector3 sc)
        {
            var f = GameObject.CreatePrimitive(PrimitiveType.Cube);
            f.name = n;
            UnityEngine.Object.DestroyImmediate(f.GetComponent<Collider>());
            f.transform.SetParent(mirror, false);
            f.transform.localPosition = p; f.transform.localScale = sc;
            f.GetComponent<MeshRenderer>().sharedMaterial = gold;
        }
        Frame("Frame_Top", new Vector3(0, 1.04f, 0.01f), new Vector3(1.76f, 0.08f, 0.04f));
        Frame("Frame_Bottom", new Vector3(0, -1.04f, 0.01f), new Vector3(1.76f, 0.08f, 0.04f));
        Frame("Frame_Left", new Vector3(-0.84f, 0, 0.01f), new Vector3(0.08f, 2.0f, 0.04f));
        Frame("Frame_Right", new Vector3(0.84f, 0, 0.01f), new Vector3(0.08f, 2.0f, 0.04f));

        // Камера «зеркала»: стоит на поверхности и смотрит в коридор; видит всё, включая слой LocalHead.
        var camGo = new GameObject("MirrorCamera");
        camGo.transform.SetParent(mirror, false);
        camGo.transform.localPosition = new Vector3(0, 0, 0.05f);
        camGo.transform.localRotation = Quaternion.identity; // локальный +Z зеркала = мировой −Z (в коридор)
        var mc = camGo.AddComponent<Camera>();
        mc.targetTexture = rt;
        mc.fieldOfView = 70f;
        mc.nearClipPlane = 0.05f;
        mc.farClipPlane = 40f;
        mc.cullingMask = ~0;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        if (openedHere) EditorSceneManager.CloseScene(scene, true);
    }

    // ------------------------------------------------------------------ utils

    static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);
        if (existing >= 0) return existing;
        var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tm.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(p.stringValue)) { p.stringValue = name; tm.ApplyModifiedPropertiesWithoutUndo(); return i; }
        }
        throw new Exception("No free layer for " + name);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }
}
