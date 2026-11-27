using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Объёмные мультяшные глаза гориллы: выпуклый белок в глазнице (закрывает нарисованный глаз),
/// крупный чёрный зрачок с бликом, который «скользит» по поверхности белка туда, куда смотрит горилла.
///  • Смотрит на ближайшего игрока перед собой (голова локального игрока, другие гориллы); своя горилла
///    в зеркале смотрит в зеркало — то есть на тебя. Никого рядом — взгляд спокойно блуждает.
///  • Испуг (Fear, задаёт GorillaFaceEmotes): глаза чуть выпучиваются, зрачки сужаются.
/// По сети ничего не шлётся — каждый клиент сам решает, куда смотрят глаза.
/// Веки (GorillaFaceEmotes) — скорлупки чуть больше белка, моргание и прищур ложатся поверх.
/// Объекты собирает Tools/Heist/Player/Reactions (PlayerReactionsSetup).
/// </summary>
[DefaultExecutionOrder(1110)] // после GorillaFinalRig и GorillaFaceEmotes
public class GorillaEyeGaze : MonoBehaviour
{
    [System.Serializable]
    public class Eye
    {
        [Tooltip("Центр глазного яблока (ось Z — вперёд из лица)")]
        public Transform anchor;
        [Tooltip("Белок (сфера, масштаб = диаметры)")]
        public Transform eyeball;
        [Tooltip("Зрачок (сплюснутая сфера, ребёнок anchor)")]
        public Transform pupil;

        [HideInInspector] public Vector3 dir = Vector3.forward, ballScale, pupilScale;
        [HideInInspector] public bool cached;
    }

    public GorillaFinalRig rig;
    public Eye left = new Eye(), right = new Eye();

    [Header("Форма")]
    [Tooltip("Полуоси белка (м)")]
    public Vector3 radii = new Vector3(0.05f, 0.036f, 0.03f);
    [Tooltip("Насколько далеко зрачок может уйти от центра (градусы)")]
    public float maxYaw = 30f, maxPitch = 22f;

    [Header("Взгляд")]
    public float lookRange = 5f;
    [Tooltip("Цели дальше этого угла от лица не замечаются")]
    public float lookAngle = 80f;
    public float retargetInterval = 0.3f;
    [Tooltip("Скорость перевода взгляда (1/с)")]
    public float saccadeSpeed = 24f;
    [Tooltip("Микродвижения зрачков (градусы) и их частота (с)")]
    public float jitter = 1.6f;
    public Vector2 jitterInterval = new Vector2(0.35f, 1.1f);
    [Tooltip("Блуждание взгляда, когда никого рядом (градусы)")]
    public float wanderYaw = 16f, wanderPitch = 8f;
    public Vector2 wanderInterval = new Vector2(1.2f, 3f);

    [Header("Испуг")]
    [Range(0f, 0.4f)] public float fearBulge = 0.12f;
    [Range(0f, 0.9f)] public float fearPupilShrink = 0.45f;

    /// 0…1 — испуг (выпученные глаза, маленькие зрачки). Задаёт GorillaFaceEmotes.
    public float Fear { get; set; }

    static readonly List<GorillaEyeGaze> all = new List<GorillaEyeGaze>();
    static readonly List<Transform> mirrors = new List<Transform>();
    static Scene mirrorsScene;

    NetworkGorillaAvatar net;
    Transform target;
    Vector3 wanderLocal = Vector3.forward, jitterLocal;
    float nextRetarget, nextWander, nextJitter;

    void Awake()
    {
        net = GetComponentInParent<NetworkGorillaAvatar>(true);
        if (!rig) rig = GetComponent<GorillaFinalRig>();
        Cache(left); Cache(right);
    }

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    static void Cache(Eye e)
    {
        if (e.cached) return;
        if (e.eyeball) e.ballScale = e.eyeball.localScale;
        if (e.pupil) e.pupilScale = e.pupil.localScale;
        e.cached = true;
    }

    /// Точка между глаз (мир).
    public Vector3 HeadPoint =>
        left.anchor && right.anchor ? (left.anchor.position + right.anchor.position) * 0.5f : transform.position;

    bool Visible => !net || net.IsRemoteVisible;

    void LateUpdate()
    {
        if (!Visible || !left.anchor || !right.anchor) return;
        float now = Time.time;
        if (now >= nextRetarget) { nextRetarget = now + retargetInterval; target = PickTarget(); }
        if (now >= nextWander)
        {
            nextWander = now + Random.Range(wanderInterval.x, wanderInterval.y);
            wanderLocal = DirFromAngles(Random.Range(-wanderYaw, wanderYaw), Random.Range(-wanderPitch, wanderPitch * 0.6f));
        }
        if (now >= nextJitter)
        {
            nextJitter = now + Random.Range(jitterInterval.x, jitterInterval.y);
            jitterLocal = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * Mathf.Tan(jitter * Mathf.Deg2Rad);
        }

        float k = 1f - Mathf.Exp(-saccadeSpeed * Time.deltaTime);
        Aim(left, k);
        Aim(right, k);
        Place(left, Fear);
        Place(right, Fear);
    }

    void Aim(Eye e, float k)
    {
        Vector3 want = target
            ? e.anchor.InverseTransformDirection(target.position - e.anchor.position)
            : wanderLocal;
        want = Clamp((want.normalized + jitterLocal).normalized, maxYaw, maxPitch);
        e.dir = Vector3.Slerp(e.dir, want, k);
    }

    /// Направление в осях глаза, ограниченное углами поворота зрачка.
    public static Vector3 Clamp(Vector3 d, float maxYaw, float maxPitch)
    {
        float yaw = Mathf.Atan2(d.x, Mathf.Max(1e-4f, d.z)) * Mathf.Rad2Deg;
        float pitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
        if (d.z <= 0f) yaw = Mathf.Sign(d.x) * maxYaw;                       // цель сбоку/сзади — упор в угол
        return DirFromAngles(Mathf.Clamp(yaw, -maxYaw, maxYaw), Mathf.Clamp(pitch, -maxPitch, maxPitch));
    }

    static Vector3 DirFromAngles(float yaw, float pitch) =>
        Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;

    /// Точка на эллипсоиде с полуосями r в направлении d (из центра) и нормаль в ней.
    public static Vector3 OnEllipsoid(Vector3 d, Vector3 r, out Vector3 normal)
    {
        float t = 1f / Mathf.Sqrt(Sq(d.x / r.x) + Sq(d.y / r.y) + Sq(d.z / r.z));
        Vector3 p = d * t;
        normal = new Vector3(p.x / (r.x * r.x), p.y / (r.y * r.y), p.z / (r.z * r.z)).normalized;
        return p;
    }

    static float Sq(float v) => v * v;

    void Place(Eye e, float fear)
    {
        float bulge = 1f + fearBulge * fear;
        if (e.eyeball) e.eyeball.localScale = e.ballScale * bulge;
        if (!e.pupil) return;
        Vector3 p = OnEllipsoid(e.dir, radii * bulge, out var n);
        e.pupil.localPosition = p;
        e.pupil.localRotation = Quaternion.LookRotation(n, Vector3.up);
        e.pupil.localScale = e.pupilScale * (1f - fearPupilShrink * fear);
    }

    // ------------------------------------------------------------------ на кого смотреть
    Transform PickTarget()
    {
        Vector3 head = HeadPoint, fwd = left.anchor.forward;
        Transform best = null;
        float bestScore = float.MaxValue;

        void Consider(Transform t, bool current)
        {
            if (!t) return;
            Vector3 v = t.position - head;
            float d = v.magnitude;
            if (d < 0.3f || d > lookRange || Vector3.Angle(fwd, v) > lookAngle) return;
            float score = current ? d * 0.7f : d;                                 // не дёргаться между двумя целями
            if (score < bestScore) { bestScore = score; best = t; }
        }

        // голова локального игрока (для своей гориллы это своя камера — пропускаем)
        var cam = Camera.main;
        if (cam && (!rig || cam.transform != rig.head)) Consider(cam.transform, cam.transform == target);
        // другие гориллы
        foreach (var g in all)
            if (g != this && g.Visible && g.left.anchor) Consider(g.left.anchor, g.left.anchor == target);
        // своя горилла в зеркале смотрит в зеркало — на игрока
        if (!net)
        {
            RefreshMirrors();
            foreach (var m in mirrors) Consider(m, m == target);
        }
        return best;
    }

    static void RefreshMirrors()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene == mirrorsScene) { mirrors.RemoveAll(m => !m); return; }
        mirrorsScene = scene;
        mirrors.Clear();
        foreach (var p in FindObjectsByType<ProximityCameraEnabler>(FindObjectsInactive.Exclude)) mirrors.Add(p.transform);
    }

    /// Поставить зрачки сразу (превью и снимки в редакторе). localDir — в осях глаза.
    public void PreviewLook(Vector3 localDir, float fear)
    {
        Cache(left); Cache(right);
        left.dir = right.dir = Clamp(localDir.normalized, maxYaw, maxPitch);
        Place(left, fear);
        Place(right, fear);
    }
}
