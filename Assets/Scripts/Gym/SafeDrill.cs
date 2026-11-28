using Fusion;
using TMPro;
using UnityEngine;

/// <summary>
/// Дрель для взлома двери сейфа (Photon Fusion 2, Shared; объект сцены с Master Client Object).
/// ПУСК — дрель сверлит drillSeconds секунд, но греется: за heatSeconds работы перегревается и клинит.
/// Заклинило — бей по дрели рукой (касание сферой-руки, в ПК-режиме — клик): она остывает и сверлит дальше.
/// Готово — дверь сейфа распахивается. СБРОС закрывает дверь и обнуляет дрель.
/// Состояние ([Networked] прогресс, нагрев, работа, заклинивание) считает хост, остальные шлют ему RPC.
/// Без сети работает локально.
/// </summary>
public class SafeDrill : NetworkBehaviour
{
    [Header("Баланс")]
    public float drillSeconds = 30f;
    public float heatSeconds = 9f;
    [Tooltip("До какого нагрева остывает дрель от удара рукой")]
    public float heatAfterHit = 0.3f;
    public float hitCooldown = 0.35f;

    [Header("Ссылки (заполняет билдер)")]
    public Transform bit;
    public Transform doorHinge;
    public float doorOpenAngle = -110f;
    public TextMeshPro display;
    public Renderer heatLamp;
    public ParticleSystem smoke;
    public AudioSource motor;
    public AudioSource fx;

    [Networked] public float NetProgress { get; set; }
    [Networked] public float NetHeat { get; set; }
    [Networked] public NetworkBool NetRunning { get; set; }
    [Networked] public NetworkBool NetJammed { get; set; }

    bool spawned, localRunning, localJammed, shownJammed;
    float localProgress, localHeat, lastHit = -10f, doorAngle, bitAngle;
    MaterialPropertyBlock mpb;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), EmissionId = Shader.PropertyToID("_EmissionColor");

    public float Progress => spawned ? NetProgress : localProgress;
    public float Heat => spawned ? NetHeat : localHeat;
    public bool Running => spawned ? (bool)NetRunning : localRunning;
    public bool Jammed => spawned ? (bool)NetJammed : localJammed;
    public bool Done => Progress >= 1f;

    void Awake()
    {
        if (motor) { motor.clip = GymAudio.DrillLoop; motor.loop = true; motor.playOnAwake = false; motor.spatialBlend = 1f; }
        if (fx) { fx.playOnAwake = false; fx.spatialBlend = 1f; }
    }

    public override void Spawned() { spawned = true; doorAngle = NetProgress >= 1f ? doorOpenAngle : 0f; }
    public override void Despawned(NetworkRunner runner, bool hasState) => spawned = false;

    // ------------------------------------------------------------------ управление
    public void PressStart() { if (spawned) RPC_Start(); else StartLocal(ref localRunning, ref localJammed, localProgress); }
    public void PressReset() { if (spawned) RPC_Reset(); else { localProgress = 0f; localHeat = 0f; localRunning = false; localJammed = false; } }

    /// Удар рукой или клик по дрели. Возвращает true, если удар засчитан (для вибро).
    public bool Hit()
    {
        if (Time.time - lastHit < hitCooldown) return false;
        lastHit = Time.time;
        if (fx) fx.PlayOneShot(GymAudio.Clang, 0.9f);
        if (spawned) RPC_Hit(); else HitState(ref localHeat, ref localJammed);
        return true;
    }

    static void StartLocal(ref bool running, ref bool jammed, float progress) { if (progress < 1f) running = true; }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)] void RPC_Start() { if (NetProgress < 1f) NetRunning = true; }
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)] void RPC_Reset() { NetProgress = 0f; NetHeat = 0f; NetRunning = false; NetJammed = false; }
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Hit() { float h = NetHeat; bool j = NetJammed; HitState(ref h, ref j); NetHeat = h; NetJammed = j; }

    void HitState(ref float heat, ref bool jammed)
    {
        if (!jammed) return;                       // работающую дрель удар не охлаждает — только клин выбивает
        jammed = false; heat = heatAfterHit;
    }

    /// Шаг симуляции (хост или офлайн). Публичный — для Play Mode проверки.
    public static void Step(float dt, float drillSeconds, float heatSeconds, ref float progress, ref float heat, ref bool running, ref bool jammed)
    {
        if (!running || jammed || progress >= 1f) { if (!running || progress >= 1f) heat = Mathf.Max(0f, heat - dt / heatSeconds * 0.5f); return; }
        progress = Mathf.Min(1f, progress + dt / drillSeconds);
        heat += dt / heatSeconds;
        if (heat >= 1f) { heat = 1f; jammed = true; }
        if (progress >= 1f) running = false;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        float p = NetProgress, h = NetHeat; bool r = NetRunning, j = NetJammed;
        Step(Runner.DeltaTime, drillSeconds, heatSeconds, ref p, ref h, ref r, ref j);
        NetProgress = p; NetHeat = h; NetRunning = r; NetJammed = j;
    }

    // ------------------------------------------------------------------ визуал (у каждого)
    void Update()
    {
        if (!spawned) Step(Time.deltaTime, drillSeconds, heatSeconds, ref localProgress, ref localHeat, ref localRunning, ref localJammed);

        bool working = Running && !Jammed && !Done;
        bitAngle += (working ? 1400f : 0f) * Time.deltaTime;
        if (bit) bit.localRotation = Quaternion.Euler(0f, 0f, bitAngle) ;
        doorAngle = Mathf.MoveTowards(doorAngle, Done ? doorOpenAngle : 0f, 90f * Time.deltaTime);
        if (doorHinge) doorHinge.localRotation = Quaternion.Euler(0f, doorAngle, 0f);

        if (motor)
        {
            if (working && !motor.isPlaying) motor.Play();
            if (!working && motor.isPlaying) motor.Stop();
            motor.pitch = 0.9f + Heat * 0.35f;
        }
        if (Jammed != shownJammed) { shownJammed = Jammed; if (Jammed && fx) fx.PlayOneShot(GymAudio.Zap, 0.6f); }

        if (smoke)
        {
            var em = smoke.emission; em.rateOverTime = Jammed ? 30f : Heat > 0.65f && working ? 12f : 0f;
        }
        if (heatLamp)
        {
            mpb ??= new MaterialPropertyBlock();
            var c = Jammed ? (Mathf.Repeat(Time.time * 4f, 1f) < 0.5f ? Color.red : new Color(0.3f, 0f, 0f)) : Color.Lerp(new Color(0.2f, 1f, 0.3f), new Color(1f, 0.35f, 0.05f), Heat);
            mpb.SetColor(BaseColorId, c); mpb.SetColor(EmissionId, c * 2.5f); heatLamp.SetPropertyBlock(mpb);
        }
        if (display)
        {
            string state = Done ? "<color=#5f5>СЕЙФ ОТКРЫТ!</color>"
                : Jammed ? "<color=#f44>ПЕРЕГРЕВ — УДАРЬ ПО ДРЕЛИ!</color>"
                : working ? "СВЕРЛИТ…" : Progress > 0f ? "ПАУЗА — ЖМИ ПУСК" : "ЖМИ ПУСК";
            display.text = $"ДРЕЛЬ  {Progress * 100f:0}%\nНАГРЕВ {Heat * 100f:0}%\n{state}";
        }
    }
}
