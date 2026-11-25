using UnityEngine;

/// <summary>
/// Двигает модель обезьяны за VR-игроком:
///  • корень (торс) стоит под головой и поворачивается за взглядом (только по Y);
///  • кость головы повторяет наклоны камеры;
///  • руки — аналитический Two Bone IK: кисть приходит в LeftHandFollower / RightHandFollower.
/// Висит на контейнере модели внутри VRGorillaPlayer. Физики не касается (на модели нет коллайдеров).
/// </summary>
[DefaultExecutionOrder(1000)] // после GorillaLocomotion.Player и TrackedPoseDriver
public class MonkeyAvatarRig : MonoBehaviour
{
    [System.Serializable]
    public class Arm
    {
        public Transform upper, lower, hand;
        public Transform target;      // сфера-рука GorillaLocomotion (куда должна прийти кисть)
        public Transform controller;  // контроллер — откуда брать поворот кисти
        [HideInInspector] public Quaternion upperBind, lowerBind, handBind;
        [HideInInspector] public float lenUpper, lenLower, handLength, palmSign;
    }

    [Header("Источники")]
    public Transform head;             // камера игрока
    [Header("Кости")]
    public Transform headBone;
    public Arm leftArm = new Arm();
    public Arm rightArm = new Arm();

    [Header("Настройка")]
    [Tooltip("Насколько центр головы модели сдвинут назад от камеры (м), чтобы глаза были «у камеры»")]
    public float headBackOffset = 0.05f;
    [Tooltip("Куда сгибаются локти (в осях торса, м). X — наружу, Y — вверх, Z — вперёд")]
    public Vector3 elbowHint = new Vector3(0.35f, -0.5f, -0.25f);

    Quaternion headBoneBind;
    bool initialized;

    void Awake() => Init();
    void OnEnable() => Application.onBeforeRender += UpdateRig;
    void OnDisable() => Application.onBeforeRender -= UpdateRig;
    void LateUpdate() => UpdateRig();

    public void Init()
    {
        if (initialized) return;
        if (headBone) headBoneBind = headBone.localRotation;
        InitArm(leftArm);
        InitArm(rightArm);
        initialized = true;
    }

    void InitArm(Arm a)
    {
        if (!a.upper || !a.lower || !a.hand) return;
        a.upperBind = a.upper.localRotation;
        a.lowerBind = a.lower.localRotation;
        a.handBind = a.hand.localRotation;
        a.lenUpper = Vector3.Distance(a.upper.position, a.lower.position);
        a.lenLower = Vector3.Distance(a.lower.position, a.hand.position);
        var end = a.hand.childCount > 0 ? a.hand.GetChild(0) : null;
        a.handLength = end ? Vector3.Distance(a.hand.position, end.position) : a.lenLower * 0.25f;
        // В T-позе тыльная сторона кисти левой руки смотрит вверх по +Z кости, у правой — вниз (зеркальный скелет).
        a.palmSign = Mathf.Sign(Vector3.Dot(a.hand.forward, transform.up));
    }

    public void UpdateRig()
    {
        if (!head) return;
        Init();

        // 1) Торс: под головой, повёрнут по взгляду (только yaw)
        var yaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
        transform.SetPositionAndRotation(head.position - yaw * Vector3.forward * headBackOffset, yaw);

        // 2) Голова: наклоны камеры относительно торса
        if (headBone)
        {
            headBone.localRotation = headBoneBind;
            var tilt = head.rotation * Quaternion.Inverse(yaw);
            headBone.rotation = tilt * headBone.rotation;
        }

        // 3) Руки
        SolveArm(leftArm, -1f);
        SolveArm(rightArm, 1f);
    }

    void SolveArm(Arm a, float side)
    {
        if (!a.upper || !a.lower || !a.hand || !a.target) return;
        a.upper.localRotation = a.upperBind;
        a.lower.localRotation = a.lowerBind;
        a.hand.localRotation = a.handBind;

        float lu = a.lenUpper, ll = a.lenLower, lh = a.handLength; // в метрах мира (измерены в Init)

        Vector3 handDir = a.controller ? a.controller.forward : (a.target.position - a.upper.position).normalized;
        Vector3 shoulder = a.upper.position;
        Vector3 wristTarget = a.target.position - handDir * (lh * 0.5f);

        Vector3 toT = wristTarget - shoulder;
        float d = Mathf.Clamp(toT.magnitude, 0.01f, lu + ll - 0.001f);
        Vector3 dir = toT.sqrMagnitude > 1e-8f ? toT.normalized : transform.forward;

        Vector3 hint = transform.rotation * new Vector3(elbowHint.x * side, elbowHint.y, elbowHint.z);
        Vector3 pole = Vector3.ProjectOnPlane(hint, dir);
        if (pole.sqrMagnitude < 1e-6f) pole = Vector3.ProjectOnPlane(Vector3.down, dir);
        pole.Normalize();

        float cosA = Mathf.Clamp((lu * lu + d * d - ll * ll) / (2f * lu * d), -1f, 1f);
        float sinA = Mathf.Sqrt(1f - cosA * cosA);
        Vector3 elbow = shoulder + dir * (cosA * lu) + pole * (sinA * lu);
        Vector3 wrist = shoulder + dir * d;

        // У костей рук этой модели ось +Y смотрит вдоль кости
        a.upper.rotation = Quaternion.FromToRotation(a.upper.up, elbow - shoulder) * a.upper.rotation;
        a.lower.rotation = Quaternion.FromToRotation(a.lower.up, wrist - a.lower.position) * a.lower.rotation;

        if (a.controller)
            a.hand.rotation = Quaternion.LookRotation(a.controller.up * a.palmSign, handDir);
        else
            a.hand.rotation = Quaternion.FromToRotation(a.hand.up, handDir) * a.hand.rotation;
    }
}
