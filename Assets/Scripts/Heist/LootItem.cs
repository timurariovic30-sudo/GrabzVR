using UnityEngine;

/// <summary>
/// Предмет для воровства (корень префаба лута: Rigidbody + BoxCollider, тег Loot).
/// До первого взятия висит на месте, медленно крутится (по желанию) и мягко «пульсирует» подсветкой (эмиссия, без лишних источников света).
/// Хрупкий лут (ваза) разбивается при сильном ударе и теряет всю стоимость.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class LootItem : MonoBehaviour
{
    [Header("Ценность")]
    [Tooltip("Тип лута (нужен для сброса — по нему LootRegistry находит префаб)")]
    public string lootId = "GoldBar";
    public string itemName = "Лут";
    public int value = 100;
    [Tooltip("Тяжёлый предмет: пока его держат, игрок медленнее на 40%")]
    public bool isHeavy;

    [Header("Хрупкость")]
    public bool isFragile;
    [Tooltip("Скорость удара (м/с), при которой хрупкий предмет разбивается")]
    public float breakImpactSpeed = 5.5f;
    public AudioClip[] breakClips;
    public Material shardMaterial;

    [Header("Выделение до первого взятия")]
    public bool kinematicUntilGrabbed = true;
    public bool spinUntilGrabbed = true;
    public float spinSpeed = 35f;
    public bool glowUntilGrabbed = true;
    public Color glowColor = new Color(1f, 0.75f, 0.25f);
    [Tooltip("Дополнительно настоящий точечный свет (дорого для VR — только для самых ценных)")]
    public bool glowLight;
    public float glowRange = 1.5f;

    public Rigidbody Body { get; private set; }
    public bool WasGrabbed { get; private set; }
    public bool IsBroken { get; private set; }
    public VRGrabber Holder { get; private set; }

    Light lightGlow;
    Renderer[] renderers;
    MaterialPropertyBlock block;
    float phase;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        Body = GetComponent<Rigidbody>();
        renderers = GetComponentsInChildren<Renderer>();
        phase = Random.value * 10f;
        if (kinematicUntilGrabbed) Body.isKinematic = true;

        if (glowLight)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 0.3f;
            lightGlow = go.AddComponent<Light>();
            lightGlow.type = LightType.Point;
            lightGlow.color = glowColor;
            lightGlow.range = glowRange;
            lightGlow.shadows = LightShadows.None;
        }
    }

    void Update()
    {
        if (WasGrabbed || !glowUntilGrabbed) return;
        float k = 0.5f + 0.5f * Mathf.Sin((Time.time + phase) * 3f);
        if (lightGlow) lightGlow.intensity = 1f + k * 1.5f;
        block ??= new MaterialPropertyBlock();
        foreach (var r in renderers)
        {
            if (!r) continue;
            r.GetPropertyBlock(block);
            block.SetColor(EmissionId, glowColor * (0.15f + 0.6f * k));
            r.SetPropertyBlock(block);
        }
    }

    void FixedUpdate()
    {
        if (!WasGrabbed && spinUntilGrabbed && Body.isKinematic)
            Body.MoveRotation(Quaternion.AngleAxis(spinSpeed * Time.fixedDeltaTime, Vector3.up) * Body.rotation);
    }

    public void OnGrabbed(VRGrabber grabber)
    {
        Holder = grabber;
        if (WasGrabbed) return;
        WasGrabbed = true;
        Body.isKinematic = false;
        if (lightGlow) Destroy(lightGlow.gameObject);
        // гасим подсветку
        block ??= new MaterialPropertyBlock();
        foreach (var r in renderers)
        {
            if (!r) continue;
            r.GetPropertyBlock(block);
            block.SetColor(EmissionId, Color.black);
            r.SetPropertyBlock(block);
        }
    }

    public void OnReleased(VRGrabber grabber)
    {
        if (Holder == grabber) Holder = null;
    }

    void OnCollisionEnter(Collision c)
    {
        if (!isFragile || IsBroken || Body.isKinematic || Holder != null) return;
        if (c.relativeVelocity.magnitude >= breakImpactSpeed) Break();
    }

    /// Хрупкий лут разбивается: осколки, звук, стоимость потеряна.
    public void Break()
    {
        if (IsBroken) return;
        IsBroken = true;
        var b = new Bounds(transform.position, Vector3.one * 0.2f);
        foreach (var r in renderers) if (r) b.Encapsulate(r.bounds);
        Shatter.Spawn(b, shardMaterial, 10, Body.linearVelocity * 0.3f, breakClips);
        if (HeistGameManager.Instance != null) HeistGameManager.Instance.PostEvent($"{itemName.ToUpper()} РАЗБИТА! −${value}");
        if (Holder != null) Holder.Release();
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (Holder != null) Holder.Release();
    }
}
