using UnityEngine;

/// <summary>
/// Риг финальной гориллы (Tripo, со скелетом) в духе Gorilla Tag:
///  • корень аватара стоит так, что глаза модели совпадают с камерой, и поворачивается за взглядом только по Y;
///  • кость головы повторяет наклоны шлема (сама голова — отдельный меш на слое LocalHead:
///    своей камере не видна, в зеркале и другим игрокам видна);
///  • руки — Two Bone IK (плечо → локоть → кисть) к сферам-рукам GorillaLocomotion, локти смотрят вниз-назад;
///    если контроллер дальше длины руки — рука «тянется» (плечевая кость выдвигается, как в Gorilla Tag);
///  • кисть поворачивается вместе с контроллером.
/// Каждый кадр кости сначала возвращаются в позу привязки, поэтому решение не «уплывает».
/// Собирается в редакторе: Tools/Heist/Player/Build Gorilla Final Avatar.
/// </summary>
[DefaultExecutionOrder(1000)] // после GorillaLocomotion.Player и TrackedPoseDriver
public class GorillaFinalRig : MonoBehaviour
{
    [System.Serializable]
    public class Limb
    {
        public Transform upper, lower, hand;
        [Tooltip("Куда должна прийти ладонь (сфера-рука GorillaLocomotion)")]
        public Transform target;
        [Tooltip("Откуда брать поворот кисти (VR-контроллер). Пусто — поворот сферы-руки")]
        public Transform rotationSource;
        [Tooltip("-1 левая, +1 правая")]
        public float side = -1f;
        [Tooltip("Расстояние запястье → центр ладони (м)")]
        public float palmOffset = 0.08f;
        [Tooltip("Доп. поворот кисти в осях контроллера (градусы). Подбирается так, чтобы при естественном хвате " +
                 "контроллера перед собой ладонь обезьяны смотрела вниз. Можно крутить прямо в Play Mode.")]
        public Vector3 rotationOffset;

        [HideInInspector] public Vector3 upperBindPos;
        [HideInInspector] public Quaternion upperBindRot, lowerBindRot, handBindRot;
        [HideInInspector] public Quaternion handFromController, handBindRel;
        [HideInInspector] public float lenUpper, lenLower;
    }

    public Transform head;            // камера игрока
    [Tooltip("Точка между глаз модели в осях корня аватара (м)")]
    public Vector3 eyeLocal;
    [Tooltip("Камера чуть впереди глаз модели (м) — изнутри головы ничего не попадёт в кадр")]
    public float eyeForwardOffset = 0.03f;
    public Transform headBone;
    public Limb left = new Limb { side = -1f };
    public Limb right = new Limb { side = 1f };
    [Tooltip("Насколько рука может «вытянуться» сверх своей длины (м)")]
    public float maxStretch = 0.35f;
    [Tooltip("Куда смотрят локти (в осях корня, на единицу длины руки): x — наружу, y — вниз, z — назад")]
    public Vector3 elbowHint = new Vector3(0.6f, -1f, -0.5f);
    [Range(0f, 1f), Tooltip("Какая доля скрутки кисти передаётся предплечью (иначе вся скрутка в запястье — «фантик»)")]
    public float forearmTwist = 0.6f;
    [Tooltip("Правая кисть = точное зеркальное отражение левой (модель симметрична, а оси костей правой кисти " +
             "в риге Tripo перекручены). Настраивается только Rotation Offset левой руки.")]
    public bool mirrorRightFromLeft = true;

    Quaternion headBind;
    bool init;

    void Awake() => Init();
    void OnEnable() => Application.onBeforeRender += UpdateRig;
    void OnDisable() => Application.onBeforeRender -= UpdateRig;
    void LateUpdate() => UpdateRig();

    void Init()
    {
        if (init) return;
        if (headBone) headBind = headBone.localRotation;
        var rootInv = Quaternion.Inverse(transform.rotation);
        foreach (var l in new[] { left, right })
        {
            if (!l.upper || !l.lower || !l.hand) continue;
            l.upperBindPos = l.upper.localPosition;
            l.upperBindRot = l.upper.localRotation;
            l.lowerBindRot = l.lower.localRotation;
            l.handBindRot = l.hand.localRotation;
            l.lenUpper = Vector3.Distance(l.upper.position, l.lower.position);
            l.lenLower = Vector3.Distance(l.lower.position, l.hand.position);
            // В T-позе: контроллер смотрит вдоль руки наружу, ладонь вниз
            Quaternion neutral = Quaternion.Euler(0f, 90f * l.side, 0f) * Quaternion.Euler(0f, 0f, 90f * l.side);
            l.handBindRel = rootInv * l.hand.rotation;
            l.handFromController = Quaternion.Inverse(neutral) * l.handBindRel;
        }
        init = true;
    }

    public void UpdateRig()
    {
        if (!head) return;
        Init();
        var yaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
        Vector3 eye = eyeLocal + Vector3.forward * eyeForwardOffset; // глаза модели чуть позади камеры
        transform.SetPositionAndRotation(head.position - yaw * Vector3.Scale(eye, transform.lossyScale), yaw);

        if (headBone)
        {
            headBone.localRotation = headBind;
            var tilt = head.rotation * Quaternion.Inverse(yaw);
            headBone.rotation = tilt * headBone.rotation;
        }
        Solve(left);
        Solve(right);
        Solved?.Invoke();
    }

    /// Вызывается после каждого решения рига (LateUpdate и перед рендером) —
    /// например, GorillaFallRagdoll плавно смешивает позу «лёжа» с позой рига, когда горилла встаёт.
    public event System.Action Solved;

    /// Кости, которые риг сам выставляет каждый кадр (голова и обе руки).
    public bool Drives(Transform bone) =>
        bone && (bone == headBone || bone == left.upper || bone == left.lower || bone == left.hand ||
                 bone == right.upper || bone == right.lower || bone == right.hand);

    /// Отражение поворота через плоскость YZ (лево ↔ право в осях корня).
    static Quaternion MirrorX(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);

    void Solve(Limb l)
    {
        if (!l.upper || !l.lower || !l.hand || !l.target) return;
        // 1) поза привязки
        l.upper.localPosition = l.upperBindPos;
        l.upper.localRotation = l.upperBindRot;
        l.lower.localRotation = l.lowerBindRot;
        l.hand.localRotation = l.handBindRot;

        // 2) поворот кисти = поворот контроллера
        var rotSrc = l.rotationSource ? l.rotationSource : l.target;
        Quaternion handRot;
        if (mirrorRightFromLeft && l == right && left.hand)
        {
            // зеркалим контроллер через плоскость тела → считаем, как повернулась бы ЛЕВАЯ кисть →
            // отражаем её поворот (относительно T-позы) обратно на правую сторону
            Quaternion root = transform.rotation;
            Quaternion ctrlRel = Quaternion.Inverse(root) * rotSrc.rotation;
            Quaternion leftRel = MirrorX(ctrlRel) * Quaternion.Euler(left.rotationOffset) * left.handFromController;
            Quaternion deltaL = leftRel * Quaternion.Inverse(left.handBindRel);
            handRot = root * (MirrorX(deltaL) * right.handBindRel);
        }
        else handRot = rotSrc.rotation * Quaternion.Euler(l.rotationOffset) * l.handFromController;
        // кость кисти направлена вдоль своей оси Y → запястье чуть позади центра ладони
        Vector3 wrist = l.target.position - handRot * Vector3.up * l.palmOffset;

        // 3) вытягивание, если не достаём
        float a = l.lenUpper, b = l.lenLower;
        Vector3 A = l.upper.position;
        float dist = Vector3.Distance(A, wrist);
        if (dist > a + b)
        {
            float shift = Mathf.Min(dist - (a + b) + 0.001f, maxStretch);
            A += (wrist - A).normalized * shift;
            l.upper.position = A;
        }

        // 4) Two Bone IK
        Vector3 toT = wrist - A;
        float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
        Vector3 n = toT.sqrMagnitude > 1e-8f ? toT.normalized : transform.forward;
        Vector3 hint = transform.rotation * new Vector3(elbowHint.x * l.side, elbowHint.y, elbowHint.z);
        Vector3 pole = Vector3.ProjectOnPlane(hint, n);
        if (pole.sqrMagnitude < 1e-6f) pole = Vector3.ProjectOnPlane(-transform.up, n);
        pole.Normalize();
        float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
        Vector3 elbow = A + n * (a * cosA) + pole * (a * Mathf.Sqrt(1f - cosA * cosA));

        l.upper.rotation = Quaternion.FromToRotation(l.lower.position - A, elbow - A) * l.upper.rotation;
        Vector3 W = A + n * d;
        l.lower.rotation = Quaternion.FromToRotation(l.hand.position - l.lower.position, W - l.lower.position) * l.lower.rotation;

        // 5) скрутку кисти частично отдаём предплечью (вращение вокруг его оси не сдвигает локоть и запястье)
        Vector3 axis = W - l.lower.position;
        if (forearmTwist > 0f && axis.sqrMagnitude > 1e-8f)
        {
            axis.Normalize();
            Quaternion delta = handRot * Quaternion.Inverse(l.lower.rotation * l.handBindRot);
            Vector3 v = new Vector3(delta.x, delta.y, delta.z);
            Vector3 p = Vector3.Dot(v, axis) * axis;
            var twist = new Quaternion(p.x, p.y, p.z, delta.w);
            float mag = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (mag > 1e-6f)
            {
                twist = new Quaternion(twist.x / mag, twist.y / mag, twist.z / mag, twist.w / mag);
                l.lower.rotation = Quaternion.Slerp(Quaternion.identity, twist, forearmTwist) * l.lower.rotation;
            }
        }
        l.hand.rotation = handRot;
    }
}
