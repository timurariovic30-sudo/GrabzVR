using UnityEngine;

/// <summary>
/// Предмет для воровства (корень префаба лута: Rigidbody + BoxCollider, тег Loot).
/// До первого взятия висит на месте, медленно крутится (по желанию) и мягко «пульсирует» подсветкой (эмиссия, без лишних источников света).
/// Хрупкий лут (ваза) разбивается при сильном ударе и теряет всю стоимость.
///
/// Сеть: если рядом есть NetworkLootItem и сессия Fusion запущена, состояние (взят / кто держит / разбит / сдан)
/// хранится в сети, а физику считает только тот, у кого StateAuthority (взявший игрок). Без сети всё работает локально.
/// Сданный в фургон или разбитый предмет не уничтожается, а прячется — так «СБРОСИТЬ ВЕСЬ ЛУТ» просто возвращает его на место.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class LootItem : MonoBehaviour
{
    public enum LootState { Available = 0, Broken = 1, Deposited = 2 }

    [Header("Ценность")]
    [Tooltip("Тип лута (корона, алмаз, золото, картина, сейф, ваза)")]
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
    /// Локальная рука, которая держит предмет (у других игроков — null).
    public VRGrabber Holder { get; private set; }
    public NetworkLootItem Net { get; private set; }

    /// В сети ли предмет прямо сейчас (сессия идёт и объект заспавнен).
    public bool Online => Net != null && Net.IsSpawned;
    /// Этот клиент считает физику предмета (без сети — всегда).
    public bool HasAuthority => !Online || Net.HasStateAuthority;

    public bool WasGrabbed => Online ? (bool)Net.WasGrabbed : localWasGrabbed;
    public LootState State => Online ? (LootState)Net.StateValue : localState;
    public bool IsBroken => State == LootState.Broken;
    public bool IsHidden => State != LootState.Available;

    /// Можно ли взять предмет (не спрятан и его не держит другой игрок).
    public bool CanBeGrabbed => !IsHidden && (!Online || !Net.IsHeldByOther);

    bool localWasGrabbed;
    LootState localState;
    LootState shownState;
    bool glowShown;
    Vector3 spawnPos;
    Quaternion spawnRot;

    Light lightGlow;
    Renderer[] renderers;
    Collider[] colliders;
    MaterialPropertyBlock block;
    float phase;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        Body = GetComponent<Rigidbody>();
        Net = GetComponent<NetworkLootItem>();
        renderers = GetComponentsInChildren<Renderer>();
        colliders = GetComponentsInChildren<Collider>();
        phase = Random.value * 10f;
        spawnPos = transform.position;
        spawnRot = transform.rotation;
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
        // состояние могло прийти по сети (разбит / сдан / сброшен) — показываем его
        var st = State;
        if (st != shownState) ApplyState(shownState, st);

        bool glow = glowUntilGrabbed && !WasGrabbed && !IsHidden;
        if (!glow)
        {
            if (glowShown) SetGlow(0f);
            return;
        }
        float k = 0.5f + 0.5f * Mathf.Sin((Time.time + phase) * 3f);
        if (lightGlow) lightGlow.intensity = 1f + k * 1.5f;
        SetGlow(0.15f + 0.6f * k);
    }

    void SetGlow(float k)
    {
        glowShown = k > 0f;
        if (lightGlow) lightGlow.enabled = glowShown;
        block ??= new MaterialPropertyBlock();
        foreach (var r in renderers)
        {
            if (!r) continue;
            r.GetPropertyBlock(block);
            block.SetColor(EmissionId, glowShown ? glowColor * k : Color.black);
            r.SetPropertyBlock(block);
        }
    }

    void FixedUpdate()
    {
        // у других игроков предметом двигает NetworkTransform (тело кинематическое)
        if (!HasAuthority) return;
        bool wantKinematic = IsHidden || (kinematicUntilGrabbed && !WasGrabbed);
        if (Body.isKinematic != wantKinematic) Body.isKinematic = wantKinematic;
        if (!WasGrabbed && !IsHidden && spinUntilGrabbed && Body.isKinematic)
            Body.MoveRotation(Quaternion.AngleAxis(spinSpeed * Time.fixedDeltaTime, Vector3.up) * Body.rotation);
    }

    public void OnGrabbed(VRGrabber grabber)
    {
        Holder = grabber;
        if (Online) Net.LocalGrab();
        else localWasGrabbed = true;
        if (HasAuthority && Body.isKinematic) Body.isKinematic = false;
    }

    public void OnReleased(VRGrabber grabber)
    {
        if (Holder != grabber) return;
        Holder = null;
        if (Online) Net.LocalRelease();
    }

    /// Предмет забрал другой игрок (у нас отняли StateAuthority) — отпускаем из своей руки.
    public void ForceLocalRelease()
    {
        if (Holder != null) Holder.Release();
        Holder = null;
    }

    void OnCollisionEnter(Collision c)
    {
        if (!isFragile || IsHidden || !HasAuthority || Body.isKinematic || Holder != null) return;
        if (c.relativeVelocity.magnitude >= breakImpactSpeed) Break();
    }

    /// Хрупкий лут разбивается: осколки, звук, стоимость потеряна — у всех игроков в комнате.
    public void Break()
    {
        if (IsHidden || !HasAuthority) return;
        SetState(LootState.Broken);
        var gm = HeistGameManager.Instance;
        if (gm != null) gm.Announce($"{itemName.ToUpper()} РАЗБИТА! −${value}");
    }

    /// Сдан в фургон (вызывает EvacuationZone у того, кто считает физику предмета).
    public void MarkDeposited()
    {
        if (IsHidden || !HasAuthority) return;
        SetState(LootState.Deposited);
    }

    void SetState(LootState st)
    {
        ForceLocalRelease();
        if (Online) Net.SetState(st);
        else localState = st;
        if (State != shownState) ApplyState(shownState, State);
    }

    /// Вернуть на исходное место (только тот, кто считает физику; без сети — локально).
    public void ResetToSpawn()
    {
        if (!HasAuthority) return;
        ForceLocalRelease();
        if (Online) Net.ResetNetworked(spawnPos, spawnRot);
        else
        {
            localWasGrabbed = false;
            localState = LootState.Available;
            if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            Body.position = spawnPos; Body.rotation = spawnRot;
            transform.SetPositionAndRotation(spawnPos, spawnRot);
        }
        Body.isKinematic = kinematicUntilGrabbed;
        if (State != shownState) ApplyState(shownState, State);
    }

    /// Сброс «ничейного» предмета: сначала забрать власть, затем вернуть на место (см. NetworkLootItem).
    public void RequestReset()
    {
        if (Online) Net.ResetNetworked(spawnPos, spawnRot);
        else ResetToSpawn();
    }

    /// Визуальная часть смены состояния — одинаково у всех игроков.
    void ApplyState(LootState from, LootState to)
    {
        shownState = to;
        bool visible = to == LootState.Available;
        foreach (var r in renderers) if (r) r.enabled = visible;
        foreach (var c in colliders) if (c) c.enabled = visible;
        if (lightGlow && !visible) lightGlow.enabled = false;
        if (from != LootState.Available || visible) return;

        if (to == LootState.Broken)
        {
            var b = new Bounds(transform.position, Vector3.one * 0.2f);
            foreach (var r in renderers) if (r) b.Encapsulate(r.bounds);
            Shatter.Spawn(b, shardMaterial, 10, Body.isKinematic ? Vector3.zero : Body.linearVelocity * 0.3f, breakClips);
        }
        else if (to == LootState.Deposited)
        {
            EvacuationZone.PlayDepositNear(this);
        }
    }

    void OnDestroy()
    {
        if (Holder != null) Holder.Release();
    }
}
