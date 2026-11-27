using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Смешной нокаут при падении с высоты: горилла обмякает тряпичной куклой, звучит «бум-бонк-уа-уа»,
/// через knockoutDuration секунд она плавно встаёт обратно.
///  • Локальный игрок (VR или ПК): удар считается по камере — летела вниз быстрее impactSpeed (11 м/с)
///    и резко остановилась. Тогда растёт KnockoutCount, и NetworkGorillaAvatar отправляет его по сети;
///  • сетевая горилла: KnockoutCount вырос → у каждого клиента она падает куклой (физика своя у каждого,
///    это только визуал). Своя невидимая сетевая копия ничего не делает.
/// Модель в зеркале — это своя локальная горилла, поэтому нокаут виден и там.
/// Свой игрок на время нокаута (knockoutDuration) теряет управление: GorillaLocomotion / DesktopTestPlayer
/// и поворот стиком выключены, тело заморожено, камера опускается к полу рядом с куклой (с виньеткой),
/// чтобы игрок не бегал «невидимым», пока его модель лежит. Перед вставанием всё возвращается на место.
/// Рэгдолл собирается во время игры на костях Hips/Spine/Chest/Head/руки/ноги и после нокаута удаляется.
/// Кости на время нокаута переносятся на слой Ignore Raycast и не сталкиваются со слоями Player и LocalHead —
/// руки GorillaLocomotion за куклу не цепляются, своё тело её не пинает.
/// </summary>
[DefaultExecutionOrder(1100)] // после GorillaFinalRig
public class GorillaFallRagdoll : MonoBehaviour
{
    public GorillaFinalRig rig;

    [Header("Удар")]
    [Tooltip("Скорость падения (м/с), после которой резкая остановка — нокаут")]
    public float impactSpeed = 11f;
    [Tooltip("Остановка считается резкой, если скорость упала ниже этой доли за stopWindow секунд")]
    [Range(0.05f, 0.9f)] public float stopFraction = 0.35f;
    public float stopWindow = 0.15f;
    [Tooltip("Сдвиг камеры за кадр больше этого — телепорт (смена сцены, спавн), не падение")]
    public float teleportDistance = 1.5f;

    [Header("Нокаут")]
    public float knockoutDuration = 2.5f;
    [Tooltip("Сколько секунд горилла плавно встаёт")]
    public float getUpDuration = 0.9f;
    [Tooltip("Случайный толчок при падении (м/с) — чтобы кукла не оседала одинаково")]
    public float flopKick = 1.4f;
    public float soundVolume = 0.9f;

    [Header("Нокаут своего игрока: управление и камера")]
    [Tooltip("Отбирать управление руками / WASD на время нокаута")]
    public bool lockLocalControls = true;
    [Tooltip("Высота камеры над полом, пока игрок лежит (м)")]
    public float downedCameraHeight = 0.4f;
    [Tooltip("Насколько камера отходит назад от тела, чтобы куклу было видно (м)")]
    public float downedCameraBack = 0.6f;
    [Tooltip("Время опускания и подъёма камеры (с)")]
    public float downedCameraBlend = 0.35f;
    [Tooltip("Сила виньетки, пока игрок лежит (0 — без виньетки)")]
    [Range(0f, 1f)] public float vignetteIntensity = 0.45f;

    /// Сколько раз этот игрок получил нокаут (локальный — считает сам, по сети уходит в NetworkGorillaAvatar).
    public int KnockoutCount { get; private set; }
    /// Сейчас лежит (или встаёт).
    public bool IsKnockedOut => phase != Phase.None;
    /// Вертикальная скорость камеры локального игрока (м/с, вниз — минус). Для сетевой гориллы 0.
    public float VerticalSpeed { get; private set; }

    enum Phase { None, Ragdoll, GettingUp }

    class Part
    {
        public Transform bone;
        public Rigidbody body;
        public Collider collider;
        public CharacterJoint joint;
        public int layer;
        public Vector3 prePos, snapPos;
        public Quaternion preRot, snapRot;
        public bool rigDriven;
    }

    // кость → (радиус в метрах, масса, кость-конец для капсулы)
    static readonly (string bone, float radius, float mass, string end)[] Layout =
    {
        ("Hips", 0.17f, 6f, null),
        ("Spine", 0.15f, 4f, "Chest"),
        ("Chest", 0.19f, 5f, "Neck"),
        ("Head", 0.13f, 3f, null),
        ("Left_UpperArm", 0.07f, 1.6f, "Left_LowerArm"),
        ("Left_LowerArm", 0.06f, 1.1f, "Left_Hand"),
        ("Right_UpperArm", 0.07f, 1.6f, "Right_LowerArm"),
        ("Right_LowerArm", 0.06f, 1.1f, "Right_Hand"),
        ("Left_UpperLeg", 0.085f, 2.4f, "Left_LowerLeg"),
        ("Left_LowerLeg", 0.07f, 1.5f, "Left_Foot"),
        ("Right_UpperLeg", 0.085f, 2.4f, "Right_LowerLeg"),
        ("Right_LowerLeg", 0.07f, 1.5f, "Right_Foot"),
    };

    NetworkGorillaAvatar net;
    readonly List<Part> parts = new List<Part>();
    Dictionary<string, Transform> boneMap;
    Phase phase;
    float phaseUntil, getUpStart;
    bool rigWasEnabled;
    int seenNetCount = -1;
    AudioSource audioSource;
    Vector3 lastHeadPos;
    bool hasLastHead;
    float fastFallUntil;
    Transform hips;
    Vector3 hipsSnapWorldPos;
    Quaternion hipsSnapWorldRot;

    // нокаут своего игрока: что выключили и куда опустили камеру
    bool localLocked;
    float lockStart, lockEnd;
    GorillaLocomotion.Player lockedGorilla;
    DesktopTestPlayer lockedDesktop;
    GorillaStickTurn lockedStick;
    Rigidbody lockedBody;
    bool bodyWasKinematic;
    Transform viewOffset;
    Vector3 viewBase, viewDelta;
    static Volume vignetteVolume;

    void Awake()
    {
        if (!rig) rig = GetComponent<GorillaFinalRig>();
        net = GetComponentInParent<NetworkGorillaAvatar>(true);
    }

    void OnEnable() { if (rig) rig.Solved += OnRigSolved; hasLastHead = false; }

    void OnDisable()
    {
        if (rig) rig.Solved -= OnRigSolved;
        if (phase != Phase.None) EndRagdollImmediate();
        EndLocalLock();
    }

    void Update()
    {
        if (net)
        {
            VerticalSpeed = 0f;
            if (net.IsHiddenSelf) return;
            int c = net.KnockoutCount;
            if (seenNetCount < 0) seenNetCount = c;          // зашли в комнату — старые нокауты не проигрываем
            else if (c != seenNetCount) { seenNetCount = c; StartKnockout(); }
        }
        else TrackLocalFall();

        if (localLocked) UpdateLocalLock();
        if (phase == Phase.Ragdoll && Time.time >= phaseUntil) BeginGetUp();
        else if (phase == Phase.GettingUp && Time.time >= phaseUntil) FinishGetUp();
    }

    // ------------------------------------------------------------------ удар (локальный игрок)
    void TrackLocalFall()
    {
        if (!rig || !rig.head || Time.deltaTime <= 0f) return;
        Vector3 p = rig.head.position;
        if (!hasLastHead) { lastHeadPos = p; hasLastHead = true; return; }
        Vector3 d = p - lastHeadPos;
        lastHeadPos = p;
        if (d.magnitude > teleportDistance) { VerticalSpeed = 0f; fastFallUntil = 0f; return; }

        float vy = d.y / Time.deltaTime;
        VerticalSpeed = Mathf.Lerp(VerticalSpeed, vy, 1f - Mathf.Exp(-25f * Time.deltaTime));
        if (IsImpact(vy, impactSpeed, stopFraction, Time.time, ref fastFallUntil, stopWindow) && phase == Phase.None)
        {
            KnockoutCount++;
            StartKnockout();
        }
    }

    /// Чистое правило удара: была скорость вниз ≥ impact, и за window секунд она упала ниже impact × stopFraction.
    public static bool IsImpact(float vy, float impact, float stopFraction, float now, ref float fastFallUntil, float window)
    {
        if (-vy >= impact) { fastFallUntil = now + window; return false; }
        if (now <= fastFallUntil && -vy < impact * stopFraction) { fastFallUntil = 0f; return true; }
        return false;
    }

    /// Нокаут вручную (тест, отладка). Для локального игрока уходит и по сети.
    [ContextMenu("Knockout (test)")]
    public void TriggerKnockout()
    {
        if (phase != Phase.None) return;
        if (!net) KnockoutCount++;
        StartKnockout();
    }

    // ------------------------------------------------------------------ кукла
    void StartKnockout()
    {
        if (phase != Phase.None || !rig) return;
        if (!BuildRagdoll()) return;
        rigWasEnabled = rig.enabled;
        rig.enabled = false;                    // модель больше не идёт за камерой — падает на месте
        phase = Phase.Ragdoll;
        phaseUntil = Time.time + knockoutDuration;

        // толчок, чтобы кукла смешно завалилась
        var kick = Random.insideUnitSphere * flopKick; kick.y = Mathf.Abs(kick.y) * 0.5f + 0.6f;
        foreach (var p in parts)
        {
            p.body.linearVelocity = kick + Random.insideUnitSphere * 0.4f;
            p.body.angularVelocity = Random.insideUnitSphere * 4f;
        }
        PlayBonk();
        if (!net) BeginLocalLock();
    }

    // ------------------------------------------------------------------ свой игрок: без управления, камера у пола
    void BeginLocalLock()
    {
        if (!lockLocalControls || localLocked) return;
        Transform cam = null;
        lockedGorilla = GetComponentInParent<GorillaLocomotion.Player>();
        lockedDesktop = lockedGorilla ? null : GetComponentInParent<DesktopTestPlayer>();
        if (lockedGorilla && lockedGorilla.headCollider)
        {
            cam = lockedGorilla.headCollider.transform;
            viewOffset = lockedGorilla.transform.Find("Camera Offset");
            if (!viewOffset || !cam.IsChildOf(viewOffset)) viewOffset = cam.parent;
            lockedGorilla.enabled = false;                      // руки больше не толкают тело
            lockedStick = lockedGorilla.GetComponent<GorillaStickTurn>();
            if (lockedStick) lockedStick.enabled = false;
            lockedBody = lockedGorilla.GetComponent<Rigidbody>();
            if (lockedBody)
            {
                bodyWasKinematic = lockedBody.isKinematic;
                if (!bodyWasKinematic) lockedBody.linearVelocity = Vector3.zero;
                lockedBody.isKinematic = true;                  // лежит на месте, не съезжает и не падает
            }
        }
        else if (lockedDesktop && lockedDesktop.cameraPivot)
        {
            viewOffset = cam = lockedDesktop.cameraPivot;
            lockedDesktop.enabled = false;                      // WASD, мышь и прыжок
        }
        if (!cam || !viewOffset || !viewOffset.parent) { EndLocalLock(); return; }

        // точка у пола рядом с телом: под головой, чуть назад от взгляда (и не в стену)
        int mask = ~LayerMask.GetMask("Player", "LocalHead", "Ignore Raycast");
        Vector3 floor = Physics.Raycast(cam.position, Vector3.down, out var hit, 4f, mask, QueryTriggerInteraction.Ignore)
            ? hit.point : cam.position + Vector3.down * 1.5f;
        Vector3 low = floor + Vector3.up * downedCameraHeight;
        Vector3 back = -Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        float backDist = downedCameraBack;
        if (back.sqrMagnitude > 0.5f && Physics.SphereCast(low, 0.12f, back, out var wall, downedCameraBack, mask, QueryTriggerInteraction.Ignore))
            backDist = Mathf.Max(0f, wall.distance - 0.05f);
        Vector3 target = low + back * backDist;

        viewBase = viewOffset.localPosition;
        viewDelta = viewOffset.parent.InverseTransformVector(target - cam.position);
        lockStart = Time.time;
        lockEnd = Time.time + knockoutDuration;
        localLocked = true;
    }

    void UpdateLocalLock()
    {
        float blend = Mathf.Max(0.01f, downedCameraBlend);
        float w = Mathf.Clamp01(Mathf.Min((Time.time - lockStart) / blend, (lockEnd - Time.time) / blend));
        w = w * w * (3f - 2f * w);
        if (viewOffset) viewOffset.localPosition = viewBase + viewDelta * w;
        SetVignette(w);
    }

    /// Вернуть камеру и управление (перед вставанием, при выключении).
    void EndLocalLock()
    {
        if (!localLocked) return;
        localLocked = false;
        if (viewOffset) viewOffset.localPosition = viewBase;
        SetVignette(0f);
        if (lockedBody) { lockedBody.isKinematic = bodyWasKinematic; if (!bodyWasKinematic) lockedBody.linearVelocity = Vector3.zero; }
        if (lockedStick) lockedStick.enabled = true;
        if (lockedGorilla)
        {
            // руки «переносим» к контроллерам, иначе движок посчитает рывок за время нокаута
            lockedGorilla.leftHandFollower.position = lockedGorilla.leftHandTransform.position;
            lockedGorilla.rightHandFollower.position = lockedGorilla.rightHandTransform.position;
            lockedGorilla.InitializeValues();
            lockedGorilla.enabled = true;
        }
        if (lockedDesktop) lockedDesktop.enabled = true;
        hasLastHead = false;                                    // подъём камеры — не падение
        lockedGorilla = null; lockedDesktop = null; lockedStick = null; lockedBody = null; viewOffset = null;
    }

    /// Виньетка — глобальный URP Volume (только своя камера в этой сцене, у сетевых горилл не создаётся).
    void SetVignette(float w)
    {
        if (vignetteIntensity <= 0f) return;
        if (!vignetteVolume)
        {
            if (w <= 0f) return;
            var go = new GameObject("KnockoutVignette");
            vignetteVolume = go.AddComponent<Volume>();
            vignetteVolume.isGlobal = true;
            vignetteVolume.priority = 200f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var v = profile.Add<Vignette>(true);
            v.color.Override(Color.black);
            v.smoothness.Override(0.55f);
            v.intensity.Override(vignetteIntensity);
            vignetteVolume.sharedProfile = profile;
        }
        vignetteVolume.weight = w;
        vignetteVolume.enabled = w > 0.001f;
    }

    bool BuildRagdoll()
    {
        boneMap ??= BuildBoneMap();
        parts.Clear();
        hips = null;
        float scale = Mathf.Max(1e-4f, transform.lossyScale.x);
        int excluded = LayerMask.GetMask("Player", "LocalHead");
        int ragLayer = LayerMask.NameToLayer("Ignore Raycast");

        foreach (var (name, radius, mass, end) in Layout)
        {
            if (!boneMap.TryGetValue(name, out var bone)) continue;
            var part = new Part
            {
                bone = bone, layer = bone.gameObject.layer,
                prePos = bone.localPosition, preRot = bone.localRotation,
                rigDriven = rig.Drives(bone),
            };
            bone.gameObject.layer = ragLayer;
            float r = radius / Mathf.Max(1e-4f, bone.lossyScale.x);
            if (end != null && boneMap.TryGetValue(end, out var endBone))
            {
                var cap = bone.gameObject.AddComponent<CapsuleCollider>();
                Vector3 local = bone.InverseTransformPoint(endBone.position);
                int axis = Mathf.Abs(local.x) > Mathf.Abs(local.y) ? (Mathf.Abs(local.x) > Mathf.Abs(local.z) ? 0 : 2) : (Mathf.Abs(local.y) > Mathf.Abs(local.z) ? 1 : 2);
                cap.direction = axis;
                cap.center = local * 0.5f;
                cap.radius = r;
                cap.height = Mathf.Max(local.magnitude + r, r * 2f);
                part.collider = cap;
            }
            else
            {
                var sph = bone.gameObject.AddComponent<SphereCollider>();
                sph.radius = r;
                if (name == "Head") sph.center = bone.InverseTransformVector(transform.up * 0.08f * scale);
                part.collider = sph;
            }
            part.collider.excludeLayers = excluded;

            part.body = bone.gameObject.AddComponent<Rigidbody>();
            part.body.mass = mass;
            part.body.linearDamping = 0.15f;
            part.body.angularDamping = 0.6f;
            part.body.interpolation = RigidbodyInterpolation.Interpolate;
            part.body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            part.body.excludeLayers = excluded;
            parts.Add(part);
            if (name == "Hips") hips = bone;
        }
        if (parts.Count < 4 || !hips) { Cleanup(); return false; }

        // суставы: каждую кость к ближайшему предку-кукле
        foreach (var p in parts)
        {
            if (p.bone == hips) continue;
            var parentBody = FindParentBody(p.bone);
            if (!parentBody) continue;
            var j = p.bone.gameObject.AddComponent<CharacterJoint>();
            j.connectedBody = parentBody;
            j.enableProjection = true;
            bool limb = p.bone.name.Contains("Lower");
            j.lowTwistLimit = new SoftJointLimit { limit = limb ? -10f : -25f };
            j.highTwistLimit = new SoftJointLimit { limit = limb ? 100f : 25f };
            j.swing1Limit = new SoftJointLimit { limit = limb ? 15f : 55f };
            j.swing2Limit = new SoftJointLimit { limit = limb ? 15f : 55f };
            p.joint = j;
        }
        // сама с собой кукла не сталкивается (руки в груди не «взрываются»)
        for (int a = 0; a < parts.Count; a++)
            for (int b = a + 1; b < parts.Count; b++)
                Physics.IgnoreCollision(parts[a].collider, parts[b].collider, true);
        return true;
    }

    Rigidbody FindParentBody(Transform bone)
    {
        for (var t = bone.parent; t && t != transform; t = t.parent)
            foreach (var p in parts) if (p.bone == t) return p.body;
        return null;
    }

    Dictionary<string, Transform> BuildBoneMap()
    {
        var map = new Dictionary<string, Transform>();
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (!map.ContainsKey(t.name)) map[t.name] = t;
        return map;
    }

    void BeginGetUp()
    {
        EndLocalLock();                         // камера уже поднялась — риг снова идёт за ней
        // запоминаем позу «лёжа» и убираем физику
        foreach (var p in parts) { p.snapPos = p.bone.localPosition; p.snapRot = p.bone.localRotation; }
        hipsSnapWorldPos = hips.position; hipsSnapWorldRot = hips.rotation;
        Cleanup(keepParts: true);
        rig.enabled = rigWasEnabled;
        phase = Phase.GettingUp;
        getUpStart = Time.time;
        phaseUntil = Time.time + getUpDuration;
        if (!rig.enabled) ApplyGetUp(); // риг выключен — смешиваем сами
    }

    void OnRigSolved() { if (phase == Phase.GettingUp) ApplyGetUp(); }

    void LateUpdate() { if (phase == Phase.GettingUp && rig && !rig.enabled) ApplyGetUp(); }

    /// Плавно из позы «лёжа» в позу рига: таз — в мировых координатах (корень уже вернулся к камере),
    /// остальные кости — локально. Кости рига (голова, руки) смешиваются с тем, что риг решил в этом кадре.
    void ApplyGetUp()
    {
        float t = Mathf.Clamp01((Time.time - getUpStart) / Mathf.Max(0.01f, getUpDuration));
        float w = t * t * (3f - 2f * t);
        foreach (var p in parts)
        {
            if (p.bone == hips)
            {
                Vector3 targetPos = hips.parent ? hips.parent.TransformPoint(p.prePos) : p.prePos;
                Quaternion targetRot = hips.parent ? hips.parent.rotation * p.preRot : p.preRot;
                hips.SetPositionAndRotation(Vector3.Lerp(hipsSnapWorldPos, targetPos, w), Quaternion.Slerp(hipsSnapWorldRot, targetRot, w));
                continue;
            }
            // плечо риг сам ставит на место каждый кадр (вытягивание руки), остальные позиции — исходные
            bool rigPlacesIt = p.bone == rig.left.upper || p.bone == rig.right.upper;
            p.bone.localPosition = Vector3.Lerp(p.snapPos, rigPlacesIt ? p.bone.localPosition : p.prePos, w);
            Quaternion target = p.rigDriven ? p.bone.localRotation : p.preRot;
            p.bone.localRotation = Quaternion.Slerp(p.snapRot, target, w);
        }
    }

    void FinishGetUp()
    {
        foreach (var p in parts)
        {
            p.bone.localPosition = p.prePos;
            if (!p.rigDriven) p.bone.localRotation = p.preRot;
        }
        parts.Clear();
        phase = Phase.None;
    }

    void EndRagdollImmediate()
    {
        Cleanup();
        EndLocalLock();
        if (rig) rig.enabled = rigWasEnabled;
        phase = Phase.None;
    }

    /// Убрать физику с костей и вернуть слои. keepParts — позы нужны для вставания.
    void Cleanup(bool keepParts = false)
    {
        // сразу, а не в конце кадра: сустав держится за Rigidbody, поэтому сначала все суставы, потом тела
        foreach (var p in parts) { if (p.joint) DestroyImmediate(p.joint); p.joint = null; }
        foreach (var p in parts)
        {
            if (p.body) DestroyImmediate(p.body);
            if (p.collider) DestroyImmediate(p.collider);
            p.body = null; p.collider = null;
            if (p.bone) p.bone.gameObject.layer = p.layer;
            if (!keepParts && p.bone) { p.bone.localPosition = p.prePos; p.bone.localRotation = p.preRot; }
        }
        if (!keepParts) parts.Clear();
    }

    void PlayBonk()
    {
        if (!audioSource)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.minDistance = 1.5f;
            audioSource.maxDistance = 25f;
            audioSource.dopplerLevel = 0f;
        }
        audioSource.pitch = Random.Range(0.92f, 1.1f);
        audioSource.PlayOneShot(ProceduralAudio.Bonk(), soundVolume);
    }
}
