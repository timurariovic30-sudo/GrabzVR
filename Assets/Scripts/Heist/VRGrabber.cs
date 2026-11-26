using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Универсальный хват лута.
///  • Mouse — вешается на камеру: ЛКМ (удерживать) = взять предмет в прицеле и нести, ПКМ = бросить вперёд.
///  • LeftHand / RightHand — вешается на объект руки VR-рига: курок (trigger) = взять ближайший предмет, отпустить = бросить
///    (предмет улетает со скоростью руки).
/// Пока кто-то держит тяжёлый предмет (LootItem.isHeavy), VRGrabber.SpeedMultiplier = 0.6 (−40%).
/// Скрипт движения игрока должен умножать свою скорость на VRGrabber.SpeedMultiplier.
/// </summary>
public class VRGrabber : MonoBehaviour
{
    public enum GrabInput { Mouse, LeftHandTrigger, RightHandTrigger }

    [Header("Ввод")]
    public GrabInput input = GrabInput.Mouse;

    [Header("Мышь")]
    public float mouseGrabRange = 4f;
    public float holdDistance = 1.6f;
    public float throwSpeed = 9f;

    [Header("VR-рука")]
    [Tooltip("Радиус «магнитного» хвата рукой (0.15 → 0.195, +30%)")]
    public float handGrabRadius = 0.195f;
    [Tooltip("Откуда брать поворот предмета (обычно сам контроллер). Пусто = этот объект.")]
    public Transform rotationReference;
    [Tooltip("Пока предмет в руке, он переносится на этот слой, чтобы Gorilla-руки не отталкивались от него")]
    public bool moveHeldToIgnoreRaycast = true;

    [Header("Физика удержания")]
    [Range(0.05f, 1f)] public float followStrength = 0.6f;
    [Range(0.05f, 1f)] public float heavyFollowStrength = 0.2f;
    public float maxCarrySpeed = 12f;

    [Header("Тяжёлые предметы")]
    [Range(0f, 1f)] public float heavySpeedMultiplier = 0.6f; // = замедление на 40%

    /// Множитель скорости игрока (1 = норма, 0.6 = несёт тяжёлое).
    public static float SpeedMultiplier => heavyHolders > 0 ? heavyMultiplierStatic : 1f;
    static int heavyHolders;
    static float heavyMultiplierStatic = 0.6f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { heavyHolders = 0; heavyMultiplierStatic = 0.6f; }

    public LootItem Held { get; private set; }

    InputAction grabAction;
    Camera cam;
    Quaternion rotOffset;
    float currentHoldDistance;
    bool savedGravity;
    readonly List<Collider> ignoredPlayerColliders = new List<Collider>();

    void OnEnable()
    {
        cam = GetComponent<Camera>();
        grabAction = new InputAction("Grab", InputActionType.Button);
        switch (input)
        {
            case GrabInput.Mouse:
                grabAction.AddBinding("<Mouse>/leftButton");
                break;
            case GrabInput.LeftHandTrigger:
                AddHandBindings("LeftHand");
                break;
            case GrabInput.RightHandTrigger:
                AddHandBindings("RightHand");
                break;
        }
        grabAction.Enable();
    }

    /// Курок (Trigger) ИЛИ кнопка хвата (Grip) — любая из них берёт предмет.
    void AddHandBindings(string hand)
    {
        grabAction.AddBinding($"<XRController>{{{hand}}}/{{TriggerButton}}");
        grabAction.AddBinding($"<XRController>{{{hand}}}/triggerPressed");
        grabAction.AddBinding($"<XRController>{{{hand}}}/{{GripButton}}");
        grabAction.AddBinding($"<XRController>{{{hand}}}/gripPressed");
    }

    void OnDisable()
    {
        Release();
        grabAction?.Disable();
        grabAction?.Dispose();
        grabAction = null;
    }

    void Update()
    {
        if (grabAction == null) return;

        if (grabAction.WasPressedThisFrame() && Held == null)
        {
            // В десктопе первый клик просто захватывает курсор (см. DesktopTestPlayer)
            if (input == GrabInput.Mouse && Cursor.lockState != CursorLockMode.Locked) return;
            TryGrab();
        }
        else if (grabAction.WasReleasedThisFrame() && Held != null)
        {
            Release();
        }

        if (input == GrabInput.Mouse && Held != null && Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
        {
            var item = Held;
            Release();
            float speed = item.isHeavy ? throwSpeed * 0.35f : throwSpeed;
            if (!item.Body.isKinematic) item.Body.linearVelocity = AimTransform.forward * speed + Vector3.up * 1.5f;
        }
    }

    Transform AimTransform => cam != null ? cam.transform : rotationReference != null ? rotationReference : transform;

    readonly List<(GameObject go, int layer)> savedLayers = new List<(GameObject, int)>();

    void SetHeldLayer(LootItem item, bool held)
    {
        if (!moveHeldToIgnoreRaycast) return;
        if (held)
        {
            savedLayers.Clear();
            int ignore = LayerMask.NameToLayer("Ignore Raycast");
            foreach (var t in item.GetComponentsInChildren<Transform>(true))
            {
                savedLayers.Add((t.gameObject, t.gameObject.layer));
                t.gameObject.layer = ignore;
            }
        }
        else
        {
            foreach (var (go, layer) in savedLayers) if (go != null) go.layer = layer;
            savedLayers.Clear();
        }
    }

    void TryGrab()
    {
        LootItem best = null;
        if (input == GrabInput.Mouse)
        {
            var ray = cam != null ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f)) : new Ray(transform.position, transform.forward);
            // Кнопки (триггеры) и стёкла нажимаются/разбиваются кликом — берём ближайший такой объект на луче
            var hits = Physics.RaycastAll(ray, mouseGrabRange, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var bh in hits)
            {
                var pressable = bh.collider.GetComponentInParent<IPressable>();
                if (pressable != null) { pressable.Press(); return; }
                if (!bh.collider.isTrigger) break; // упёрлись в стену/предмет — дальше не ищем
            }
            if (Physics.Raycast(ray, out var hit, mouseGrabRange, ~0, QueryTriggerInteraction.Ignore))
                best = hit.collider.attachedRigidbody ? hit.collider.attachedRigidbody.GetComponent<LootItem>() : null;
            if (best != null) currentHoldDistance = Mathf.Clamp(hit.distance, 1.0f, holdDistance);
        }
        else
        {
            float bestDist = float.MaxValue;
            foreach (var c in Physics.OverlapSphere(transform.position, handGrabRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                var item = c.attachedRigidbody ? c.attachedRigidbody.GetComponent<LootItem>() : null;
                if (item == null || !item.CanBeGrabbed) continue;
                float d = (c.ClosestPoint(transform.position) - transform.position).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = item; }
            }
        }

        // предмет в руках другого игрока (сеть) или уже сдан/разбит — взять нельзя
        if (best == null || !best.CanBeGrabbed) return;
        if (best.Holder != null) best.Holder.Release(); // перехват из другой руки

        Held = best;
        best.OnGrabbed(this);
        rotOffset = Quaternion.Inverse(AimTransform.rotation) * best.Body.rotation;
        savedGravity = best.Body.useGravity;
        best.Body.useGravity = false;
        SetHeldLayer(best, true);
        if (best.isHeavy) { heavyHolders++; heavyMultiplierStatic = heavySpeedMultiplier; }

        // Не толкаем самого себя: игнорируем столкновения предмета с коллайдерами игрока
        ignoredPlayerColliders.Clear();
        var itemCols = best.GetComponentsInChildren<Collider>();
        foreach (var pc in transform.root.GetComponentsInChildren<Collider>())
        {
            if (pc.isTrigger) continue;
            foreach (var ic in itemCols) Physics.IgnoreCollision(pc, ic, true);
            ignoredPlayerColliders.Add(pc);
        }
    }

    /// Отпустить предмет (он сохраняет текущую скорость — так получается бросок).
    public void Release()
    {
        if (Held == null) return;
        var item = Held;
        Held = null;
        if (item.isHeavy) heavyHolders = Mathf.Max(0, heavyHolders - 1);

        if (item != null) // предмет мог быть уничтожен (например, в зоне эвакуации)
        {
            item.Body.useGravity = savedGravity;
            SetHeldLayer(item, false);
            var itemCols = item.GetComponentsInChildren<Collider>();
            foreach (var pc in ignoredPlayerColliders)
                if (pc != null) foreach (var ic in itemCols) Physics.IgnoreCollision(pc, ic, false);
            item.OnReleased(this);
        }
        ignoredPlayerColliders.Clear();
    }

    void FixedUpdate()
    {
        if (Held == null) return;
        var rb = Held.Body;
        // по сети власть над предметом ещё не пришла (тело кинематическое) — ждём
        if (rb.isKinematic) return;

        Vector3 targetPos = input == GrabInput.Mouse
            ? AimTransform.position + AimTransform.forward * currentHoldDistance
            : transform.position;
        Quaternion targetRot = AimTransform.rotation * rotOffset;

        float k = Held.isHeavy ? heavyFollowStrength : followStrength;
        Vector3 vel = (targetPos - rb.position) * (k / Time.fixedDeltaTime);
        rb.linearVelocity = Vector3.ClampMagnitude(vel, maxCarrySpeed);

        Quaternion dq = targetRot * Quaternion.Inverse(rb.rotation);
        dq.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        if (!float.IsNaN(axis.x) && Mathf.Abs(angle) > 0.01f)
            rb.angularVelocity = axis * (angle * Mathf.Deg2Rad * k / Time.fixedDeltaTime);
        else
            rb.angularVelocity = Vector3.zero;
    }

    void OnDrawGizmosSelected()
    {
        if (input == GrabInput.Mouse) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, handGrabRadius);
    }
}
