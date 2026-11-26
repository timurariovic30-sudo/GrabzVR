using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Пальцы гориллы от кнопок VR-контроллеров (кости Thumb / Index / Middle / Ring / Pinky модели Gorilla_Final):
///  • Grip + Trigger — плотный кулак (все 5 пальцев);
///  • только Grip    — «указка»: большой, средний, безымянный и мизинец согнуты, указательный ПРЯМОЙ
///                     (удобно тыкать клавиши компьютера — на кончике указательного стоит ComputerKeyTrigger);
///  • только Trigger — указательный слегка согнут;
///  • ничего         — расслабленная ладонь.
/// Сгиб считается от позы привязки: ось сгиба каждой фаланги = перпендикуляр к пальцу в сторону ладони
/// (сторону ладони определяет то, куда пальцы уже чуть согнуты в T-позе).
/// Висит на VRGorillaPlayer, выполняется после рига рук.
/// </summary>
[DefaultExecutionOrder(1100)]
public class VRHandPoseController : MonoBehaviour
{
    [System.Serializable]
    public class Hand
    {
        public string prefix = "Left_";
        public Transform hand;
        [HideInInspector] public Finger[] fingers;
        [HideInInspector] public float grip, trigger;
        [HideInInspector] public InputAction gripAction, triggerAction;
    }

    public class Finger
    {
        public Transform[] bones;     // Proximal, Intermediate, Distal
        public Quaternion[] bind;
        public Vector3[] axis;        // ось сгиба в осях каждой кости
        public bool isThumb, isIndex;
    }

    public Transform avatarRoot;
    public Hand left = new Hand { prefix = "Left_" };
    public Hand right = new Hand { prefix = "Right_" };
    [Tooltip("Углы сгиба фаланг в кулаке: основная, средняя, ногтевая")]
    public Vector3 fistAngles = new Vector3(80f, 95f, 70f);
    public Vector3 thumbAngles = new Vector3(35f, 45f, 45f);
    [Tooltip("Скорость сгибания (1/с)")]
    public float speed = 18f;
    [Range(0f, 1f)] public float relaxedCurl = 0.12f;

    static readonly string[] Names = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
    static readonly string[] Parts = { "Proximal", "Intermediate", "Distal" };

    void Awake()
    {
        if (!avatarRoot) { var r = GetComponentInChildren<GorillaFinalRig>(true); if (r) avatarRoot = r.transform; }
        Setup(left); Setup(right);
    }

    void OnEnable()
    {
        foreach (var (h, side) in new[] { (left, "LeftHand"), (right, "RightHand") })
        {
            h.gripAction = new InputAction(h.prefix + "Grip", InputActionType.Value, expectedControlType: "Axis");
            h.gripAction.AddBinding($"<XRController>{{{side}}}/grip");
            h.triggerAction = new InputAction(h.prefix + "Trigger", InputActionType.Value, expectedControlType: "Axis");
            h.triggerAction.AddBinding($"<XRController>{{{side}}}/trigger");
            h.gripAction.Enable(); h.triggerAction.Enable();
        }
    }

    void OnDisable()
    {
        foreach (var h in new[] { left, right })
        {
            h.gripAction?.Dispose(); h.triggerAction?.Dispose();
            h.gripAction = h.triggerAction = null;
        }
    }

    Transform Find(string n)
    {
        foreach (var t in avatarRoot.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
        return null;
    }

    void Setup(Hand h)
    {
        if (!avatarRoot) return;
        if (!h.hand) h.hand = Find(h.prefix + "Hand");
        if (!h.hand) return;
        var list = new System.Collections.Generic.List<Finger>();
        // сторона ладони: куда пальцы уже согнуты в позе привязки
        Vector3 palm = Vector3.zero;
        foreach (var n in Names)
        {
            if (n == "Thumb") continue;
            var p = Find(h.prefix + n + "Proximal"); var d = Find(h.prefix + n + "Distal");
            var i = Find(h.prefix + n + "Intermediate");
            if (!p || !i || !d) continue;
            Vector3 baseDir = (i.position - p.position).normalized;
            palm += Vector3.ProjectOnPlane(d.position - i.position, baseDir);
        }
        if (palm.sqrMagnitude < 1e-8f) palm = -avatarRoot.up;
        palm.Normalize();

        foreach (var n in Names)
        {
            var f = new Finger { isThumb = n == "Thumb", isIndex = n == "Index", bones = new Transform[3], bind = new Quaternion[3], axis = new Vector3[3] };
            bool ok = true;
            for (int k = 0; k < 3; k++)
            {
                f.bones[k] = Find(h.prefix + n + Parts[k]);
                if (!f.bones[k]) { ok = false; break; }
            }
            if (!ok) continue;
            for (int k = 0; k < 3; k++)
            {
                var b = f.bones[k];
                Transform next = k < 2 ? f.bones[k + 1] : (b.childCount > 0 ? b.GetChild(0) : null);
                Vector3 dir = next ? (next.position - b.position).normalized : b.up;
                Vector3 axisW = Vector3.Cross(dir, palm);     // поворот вокруг неё ведёт палец к ладони
                if (axisW.sqrMagnitude < 1e-6f) axisW = Vector3.Cross(dir, -avatarRoot.up);
                f.bind[k] = b.localRotation;
                f.axis[k] = Quaternion.Inverse(b.rotation) * axisW.normalized;
            }
            list.Add(f);
        }
        h.fingers = list.ToArray();
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        foreach (var h in new[] { left, right })
        {
            if (h.fingers == null) continue;
            float g = h.gripAction != null ? h.gripAction.ReadValue<float>() : 0f;
            float t = h.triggerAction != null ? h.triggerAction.ReadValue<float>() : 0f;
            h.grip = Mathf.MoveTowards(h.grip, g, speed * dt);
            h.trigger = Mathf.MoveTowards(h.trigger, t, speed * dt);
            Apply(h);
        }
    }

    /// Сгиб 0…1 для пальца по текущим кнопкам.
    float Curl(Hand h, Finger f)
    {
        if (f.isIndex)
            return Mathf.Max(relaxedCurl, h.trigger * (h.grip > 0.5f ? 1f : 0.6f)); // при Grip без Trigger — прямой
        return Mathf.Max(relaxedCurl, h.grip);
    }

    void Apply(Hand h)
    {
        foreach (var f in h.fingers)
        {
            float c = Curl(h, f);
            Vector3 ang = f.isThumb ? thumbAngles : fistAngles;
            for (int k = 0; k < 3; k++)
                f.bones[k].localRotation = f.bind[k] * Quaternion.AngleAxis(ang[k] * c, f.axis[k]);
        }
    }

    /// Для тестов и ПК-режима: задать хват вручную (0…1).
    public void SetPose(bool leftHand, float grip, float trigger)
    {
        var h = leftHand ? left : right;
        if (h.fingers == null) Setup(h);
        h.grip = grip; h.trigger = trigger;
        if (h.fingers != null) Apply(h);
    }
}
