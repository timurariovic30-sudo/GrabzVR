using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Шлепок рукой: когда сфера-рука GorillaLocomotion касается поверхности — звук удара
/// (Pitch 0.85–1.15 по силе удара: сильнее = ниже и громче) и короткая вибрация контроллера этой руки.
/// Висит на LeftHandFollower / RightHandFollower.
/// </summary>
[RequireComponent(typeof(AudioSource))]
[DefaultExecutionOrder(100)] // после GorillaLocomotion.Player (он обновляет касания в Update)
public class GorillaHandFeedback : MonoBehaviour
{
    public bool isLeftHand;
    public GorillaLocomotion.Player player;
    [Tooltip("Контроллер этой руки — по нему считаем скорость удара")]
    public Transform controller;

    [Header("Звуки по типу поверхности")]
    public AudioClip[] softClips;   // пол, стены, всё прочее
    public AudioClip[] woodClips;   // столы, тумбы, ящики
    public AudioClip[] metalClips;  // золото, сейф, металл

    [Header("Сила удара (м/с)")]
    public float minSpeed = 0.4f;
    public float maxSpeed = 4f;
    public float cooldown = 0.08f;

    [Header("Вибрация")]
    [Range(0, 1)] public float minAmplitude = 0.15f;
    [Range(0, 1)] public float maxAmplitude = 0.8f;
    public float minDuration = 0.04f;
    public float maxDuration = 0.1f;

    AudioSource source;
    Vector3 lastPos;
    float speed, lastHitTime = -1f;
    bool wasTouching;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.minDistance = 0.5f;
        source.maxDistance = 15f;
        if (player == null) player = GetComponentInParent<GorillaLocomotion.Player>();
    }

    void OnEnable()
    {
        lastPos = Tracked.position;
        wasTouching = true; // не шлёпаем в первый же кадр после появления
    }

    Transform Tracked => controller != null ? controller : transform;

    void Update()
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        var p = Tracked.position;
        float instant = (p - lastPos).magnitude / dt;
        lastPos = p;
        // скорость руки перед ударом: быстрый рост, плавный спад
        speed = instant > speed ? instant : Mathf.Lerp(speed, instant, 10f * dt);

        bool touching = player != null && player.IsHandTouching(isLeftHand);
        if (touching && !wasTouching && Time.time - lastHitTime > cooldown && speed > minSpeed * 0.5f)
            Hit(speed);
        wasTouching = touching;
    }

    void Hit(float hitSpeed)
    {
        lastHitTime = Time.time;
        float t = Mathf.InverseLerp(minSpeed, maxSpeed, hitSpeed);

        var touched = NearestSurface();
        if (touched != null)
        {
            // Удар по стеклу витрины — разбить (если удар достаточно сильный)
            var glass = touched.GetComponentInParent<BreakableGlass>();
            if (glass != null) glass.TryBreakByHand(hitSpeed);
            // Шлепок по батуту — «боинг»
            var tramp = touched.GetComponentInParent<Trampoline>();
            if (tramp != null) tramp.Boing();
        }

        var clip = Pick(SurfaceClips(touched));
        if (clip != null)
        {
            source.pitch = Mathf.Lerp(1.15f, 0.85f, t) * Random.Range(0.98f, 1.02f);
            source.PlayOneShot(clip, Mathf.Lerp(0.35f, 1f, t));
        }
        SendHaptic(isLeftHand, Mathf.Lerp(minAmplitude, maxAmplitude, t), Mathf.Lerp(minDuration, maxDuration, t));
    }

    /// Ближайший коллайдер уровня под рукой.
    Collider NearestSurface()
    {
        var hits = Physics.OverlapSphere(transform.position, 0.12f, player ? player.locomotionEnabledLayers.value : ~0, QueryTriggerInteraction.Ignore);
        Collider best = null; float bestD = float.MaxValue;
        foreach (var h in hits)
        {
            float d = (h.ClosestPoint(transform.position) - transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = h; }
        }
        return best;
    }

    /// Тип поверхности по материалу → набор звуков.
    AudioClip[] SurfaceClips(Collider best)
    {
        if (best != null)
        {
            var r = best.GetComponentInChildren<Renderer>();
            string mat = r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "";
            if (mat.Contains("Wood") && woodClips.Length > 0) return woodClips;
            if ((mat.Contains("Gold") || best.attachedRigidbody != null) && metalClips.Length > 0) return metalClips;
        }
        return softClips;
    }

    static AudioClip Pick(AudioClip[] clips) => clips != null && clips.Length > 0 ? clips[Random.Range(0, clips.Length)] : null;

    /// Короткий виброимпульс в контроллер нужной руки (OpenXR / Quest).
    public static void SendHaptic(bool leftHand, float amplitude, float duration)
    {
        var device = InputDevices.GetDeviceAtXRNode(leftHand ? XRNode.LeftHand : XRNode.RightHand);
        if (device.isValid && device.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
            device.SendHapticImpulse(0, amplitude, duration);
    }
}
