using UnityEngine;

/// <summary>
/// Риг сгенерированной (Tripo) модели гориллы без костей — в духе Gorilla Tag:
///  • корень (торс) стоит под камерой и поворачивается за взглядом только по Y;
///  • голова (отдельная часть, слой LocalHead — своей камере не видна) повторяет наклоны камеры;
///  • каждая рука — отдельная часть с опорой в плече: поворачивается к руке-фолловеру и тянется
///    по длине (масштаб по оси руки), поэтому кисть всегда приходит туда, где VR-контроллер.
/// Части меша нарезаются в редакторе (Tools/Heist/Player/Rebuild Tripo Gorilla Avatar).
/// </summary>
[DefaultExecutionOrder(1000)] // после GorillaLocomotion.Player и TrackedPoseDriver
public class GorillaAvatarRig : MonoBehaviour
{
    [System.Serializable]
    public class Arm
    {
        public Transform pivot;        // в плече; меш руки лежит вдоль restDir
        public Vector3 restDir = Vector3.right; // направление руки в T-позе (в осях корня)
        public float restLength = 0.5f;          // плечо → кончик кисти в T-позе (м)
        public Transform target;       // сфера-рука GorillaLocomotion (куда должна прийти кисть)
        [HideInInspector] public Vector3 restLocalPos;
    }

    public Transform head;             // камера игрока
    public Transform headPivot;        // шея; дочерние — меш головы и шапки
    public Arm leftArm = new Arm();
    public Arm rightArm = new Arm();
    [Tooltip("Насколько корень сдвинут назад от камеры (м)")]
    public float headBackOffset = 0.04f;
    public Vector2 stretchLimits = new Vector2(0.45f, 2.2f);

    Quaternion headPivotBind;
    bool init;

    void Awake() => Init();
    void OnEnable() => Application.onBeforeRender += UpdateRig;
    void OnDisable() => Application.onBeforeRender -= UpdateRig;
    void LateUpdate() => UpdateRig();

    void Init()
    {
        if (init) return;
        if (headPivot) headPivotBind = headPivot.localRotation;
        init = true;
    }

    public void UpdateRig()
    {
        if (!head) return;
        Init();
        var yaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
        transform.SetPositionAndRotation(head.position - yaw * Vector3.forward * headBackOffset, yaw);

        if (headPivot)
        {
            headPivot.localRotation = headPivotBind;
            var tilt = head.rotation * Quaternion.Inverse(yaw);
            headPivot.rotation = tilt * headPivot.rotation;
        }
        SolveArm(leftArm);
        SolveArm(rightArm);
    }

    void SolveArm(Arm a)
    {
        if (!a.pivot || !a.target) return;
        Vector3 toTarget = transform.InverseTransformPoint(a.target.position) - a.pivot.localPosition;
        float dist = toTarget.magnitude;
        if (dist < 1e-4f) return;
        float s = Mathf.Clamp(dist / Mathf.Max(0.01f, a.restLength), stretchLimits.x, stretchLimits.y);
        a.pivot.localRotation = Quaternion.FromToRotation(a.restDir, toTarget / dist);
        // меш руки в осях опоры лежит вдоль restDir → тянем только вдоль этой оси
        Vector3 d = a.restDir.normalized;
        a.pivot.localScale = new Vector3(
            Mathf.Lerp(1f, s, Mathf.Abs(d.x)),
            Mathf.Lerp(1f, s, Mathf.Abs(d.y)),
            Mathf.Lerp(1f, s, Mathf.Abs(d.z)));
    }
}
