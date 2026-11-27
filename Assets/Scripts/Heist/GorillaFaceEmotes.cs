using UnityEngine;

/// <summary>Выражение лица гориллы (по сети — байт NetworkGorillaAvatar.FaceEmote).</summary>
public enum FaceEmote : byte
{
    Normal = 0,     // обычное лицо, иногда моргает
    Strain = 1,     // прищур от натуги: тащит тяжёлый лут (сейф, золотую статую)
    Scared = 2,     // широкие испуганные глаза: тревога / сирена или летит вниз
    KnockedOut = 3, // нокаут после падения: глаза закрыты
}

/// <summary>
/// 3D-веки и брови на кости головы Gorilla_Final, которые меняют выражение лица.
///  • Веки — тонкие сферические «скорлупки» вокруг глаза; закрываются поворотом вокруг горизонтальной оси,
///    как настоящие (уголки глаз остаются на месте). Цвет — серая кожа морды.
///  • Брови — пучки шерсти над глазами в цвет шерсти (темнее), поднимаются, опускаются и наклоняются.
///  • Испуг — поверх нарисованных глаз «выпрыгивают» большие белки с маленькими зрачками.
/// Выражение решает локальный игрок: нокаут &gt; падение &gt; тревога &gt; тяжёлый лут &gt; обычное.
/// Сетевая горилла показывает то, что пришло в [Networked] FaceEmote, — у всех одинаково, и в зеркале тоже.
/// Объекты лица собирает Tools/Heist/Player/Reactions (PlayerReactionsSetup).
/// </summary>
[DefaultExecutionOrder(1100)] // после GorillaFinalRig
public class GorillaFaceEmotes : MonoBehaviour
{
    [System.Serializable]
    public class EyeParts
    {
        [Tooltip("Верхнее веко: поворот вокруг локальной X (отрицательный угол — веко поднимается)")]
        public Transform upperLid;
        [Tooltip("Нижнее веко: поворот вокруг локальной X (положительный угол — веко опускается)")]
        public Transform lowerLid;
        [Tooltip("Бровь (ось Z смотрит из лица наружу)")]
        public Transform brow;
        [Tooltip("Большой испуганный глаз (белок + зрачок), обычно спрятан")]
        public Transform wideEye;
        [Tooltip("-1 — левый глаз (x < 0), +1 — правый")]
        public float side = -1f;

        [HideInInspector] public Vector3 browPos, wideScale;
        [HideInInspector] public Quaternion browRot;
        [HideInInspector] public bool cached;
    }

    /// Поза лица: углы век (градусы), подъём бровей (м), наклон бровей (градусы, + внутренний край вниз), испуг 0…1.
    [System.Serializable]
    public struct Pose
    {
        public float upperLid, lowerLid, browRaise, browInnerDown, wide;
        public Pose(float u, float l, float b, float d, float w) { upperLid = u; lowerLid = l; browRaise = b; browInnerDown = d; wide = w; }

        public static Pose Lerp(Pose a, Pose b, float t) => new Pose(
            Mathf.Lerp(a.upperLid, b.upperLid, t), Mathf.Lerp(a.lowerLid, b.lowerLid, t),
            Mathf.Lerp(a.browRaise, b.browRaise, t), Mathf.Lerp(a.browInnerDown, b.browInnerDown, t), Mathf.Lerp(a.wide, b.wide, t));
    }

    public EyeParts left = new EyeParts { side = -1f };
    public EyeParts right = new EyeParts { side = 1f };
    [Tooltip("Рендеры бровей — перекрашиваются в цвет шерсти")]
    public Renderer[] browRenderers;
    [Tooltip("Рендеры испуганных глаз — включены только при испуге")]
    public Renderer[] wideEyeRenderers;
    [Tooltip("Шерсть этой гориллы (цвет бровей)")]
    public PlayerSkinController skin;
    [Range(0f, 1f), Tooltip("Брови темнее шерсти")]
    public float browDarken = 0.55f;

    [Header("Выражения")]
    public Pose normal = new Pose(36f, 62f, 0f, 4f, 0f);
    public Pose strain = new Pose(8f, 26f, -0.008f, 24f, 0f);
    public Pose scared = new Pose(82f, 82f, 0.017f, -16f, 1f);
    public Pose knockedOut = new Pose(-4f, 4f, -0.004f, -8f, 0f);
    [Tooltip("Скорость смены выражения (1/с)")]
    public float blendSpeed = 14f;

    [Header("Моргание")]
    public Vector2 blinkInterval = new Vector2(2.2f, 5.5f);
    public float blinkDuration = 0.13f;

    [Header("Что считать испугом / натугой (локальный игрок)")]
    [Tooltip("Скорость падения вниз (м/с), с которой горилла пугается")]
    public float fallScareSpeed = 6f;
    [Tooltip("Сколько секунд испуг держится после того, как причина пропала")]
    public float scaredHold = 0.7f;

    /// Выражение, которое решил локальный игрок (его отправляет NetworkGorillaAvatar).
    public FaceEmote LocalEmote { get; private set; }
    /// Выражение, которое сейчас показано.
    public FaceEmote Current { get; private set; }

    NetworkGorillaAvatar net;
    GorillaFallRagdoll fall;
    Pose shown;
    float nextBlink, blinkUntil, scaredUntil, wideShown;
    Color shownBrowColor = new Color(-1, -1, -1);
    MaterialPropertyBlock block;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        net = GetComponentInParent<NetworkGorillaAvatar>(true);
        fall = GetComponent<GorillaFallRagdoll>();
        if (!skin) skin = GetComponentInParent<PlayerSkinController>(true);
        if (!skin) skin = transform.root.GetComponentInChildren<PlayerSkinController>(true);
        Cache(left); Cache(right);
        shown = normal;
        nextBlink = Time.time + Random.Range(blinkInterval.x, blinkInterval.y);
        SetWideRenderers(false);
        ApplyPose(shown, 0f);
    }

    static void Cache(EyeParts e)
    {
        if (e.cached) return;
        if (e.brow) { e.browPos = e.brow.localPosition; e.browRot = e.brow.localRotation; }
        if (e.wideEye) e.wideScale = e.wideEye.localScale;
        e.cached = true;
    }

    /// Чистое правило выбора выражения (порядок важности).
    public static FaceEmote Decide(bool knockedOut, bool scaredNow, bool heavy)
    {
        if (knockedOut) return FaceEmote.KnockedOut;
        if (scaredNow) return FaceEmote.Scared;
        if (heavy) return FaceEmote.Strain;
        return FaceEmote.Normal;
    }

    void LateUpdate()
    {
        if (net && net.IsHiddenSelf) return; // своя невидимая сетевая копия

        if (!net)
        {
            bool falling = fall && fall.VerticalSpeed < -fallScareSpeed;
            bool alarm = AlarmSystem.Instance && AlarmSystem.Instance.IsActive;
            if (falling || alarm) scaredUntil = Time.time + scaredHold;
            LocalEmote = Decide(fall && fall.IsKnockedOut, Time.time < scaredUntil, VRGrabber.SpeedMultiplier < 0.999f);
            Current = LocalEmote;
        }
        else
        {
            byte e = net.FaceEmote;
            Current = e <= (byte)FaceEmote.KnockedOut ? (FaceEmote)e : FaceEmote.Normal; // мусор из сети → обычное лицо
        }

        var target = PoseOf(Current);
        // моргание (с открытыми глазами)
        if (Current == FaceEmote.Normal || Current == FaceEmote.Strain)
        {
            if (Time.time >= nextBlink)
            {
                blinkUntil = Time.time + blinkDuration;
                nextBlink = Time.time + Random.Range(blinkInterval.x, blinkInterval.y);
            }
            if (Time.time < blinkUntil) { target.upperLid = knockedOut.upperLid; target.lowerLid = Mathf.Min(target.lowerLid, 20f); }
        }

        float k = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
        float lidK = Time.time < blinkUntil ? 1f - Mathf.Exp(-40f * Time.deltaTime) : k; // моргание быстрее
        shown.upperLid = Mathf.Lerp(shown.upperLid, target.upperLid, lidK);
        shown.lowerLid = Mathf.Lerp(shown.lowerLid, target.lowerLid, lidK);
        shown.browRaise = Mathf.Lerp(shown.browRaise, target.browRaise, k);
        shown.browInnerDown = Mathf.Lerp(shown.browInnerDown, target.browInnerDown, k);
        // испуганные глаза «выпрыгивают» быстро, прячутся медленнее
        float wk = 1f - Mathf.Exp(-(target.wide > shown.wide ? 22f : 9f) * Time.deltaTime);
        shown.wide = Mathf.Lerp(shown.wide, target.wide, wk);

        float tremble = Current == FaceEmote.Strain ? Mathf.Sin(Time.time * 47f) * 0.0012f : 0f; // брови дрожат от натуги
        ApplyPose(shown, tremble);
        UpdateBrowColor();
    }

    /// Целевая поза для выражения.
    public Pose PoseOf(FaceEmote e) => e switch
    {
        FaceEmote.Strain => strain,
        FaceEmote.Scared => scared,
        FaceEmote.KnockedOut => knockedOut,
        _ => normal,
    };

    static void ApplyEye(EyeParts e, Pose p, float tremble)
    {
        if (e.upperLid) e.upperLid.localRotation = Quaternion.Euler(-p.upperLid, 0f, 0f);
        if (e.lowerLid) e.lowerLid.localRotation = Quaternion.Euler(p.lowerLid, 0f, 0f);
        if (e.brow)
        {
            // ось Z брови смотрит из лица, X — вправо: для правой брови внутренний край — это −X
            float angle = (e.side > 0f ? 1f : -1f) * p.browInnerDown;
            e.brow.localPosition = e.browPos + e.browRot * Vector3.up * (p.browRaise + tremble);
            e.brow.localRotation = e.browRot * Quaternion.Euler(0f, 0f, angle);
        }
        if (e.wideEye)
        {
            float s = EaseOutBack(Mathf.Clamp01(p.wide));
            e.wideEye.localScale = e.wideScale * Mathf.Max(0.001f, s);
        }
    }

    void ApplyPose(Pose p, float tremble)
    {
        ApplyEye(left, p, tremble);
        ApplyEye(right, p, tremble);
        bool wide = p.wide > 0.03f;
        if (wide != (wideShown > 0f)) { SetWideRenderers(wide); wideShown = wide ? 1f : 0f; }
    }

    /// Показать выражение сразу, без анимации (превью и снимки в редакторе).
    public void PreviewPose(FaceEmote e)
    {
        Cache(left); Cache(right);
        var p = PoseOf(e);
        ApplyEye(left, p, 0f);
        ApplyEye(right, p, 0f);
        if (wideEyeRenderers != null) foreach (var r in wideEyeRenderers) if (r) r.enabled = p.wide > 0.03f;
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.9f, c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }

    void SetWideRenderers(bool on)
    {
        if (wideEyeRenderers == null) return;
        foreach (var r in wideEyeRenderers) if (r) r.enabled = on;
    }

    void UpdateBrowColor()
    {
        if (!skin || browRenderers == null) return;
        var c = skin.furColor * browDarken; c.a = 1f;
        if (c == shownBrowColor) return;
        shownBrowColor = c;
        block ??= new MaterialPropertyBlock();
        foreach (var r in browRenderers)
        {
            if (!r) continue;
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            r.SetPropertyBlock(block);
        }
    }
}
