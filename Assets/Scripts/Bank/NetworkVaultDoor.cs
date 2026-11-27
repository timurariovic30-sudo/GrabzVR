using Fusion;
using TMPro;
using UnityEngine;

/// <summary>
/// Круглая бронедверь хранилища, синхронизированная по сети (Photon Fusion 2, Shared).
/// Объект сцены с Master Client Object: состояние ([Networked] IsOpen и таймер взлома) хранит хост,
/// кнопки остальных игроков шлют ему RPC.
///  • Кнопка «ВЗЛОМ» в Холле (PressHack): закрытая дверь взламывается hackSeconds секунд и открывается у всех;
///    открытая — закрывается.
///  • Кнопка «ДВЕРЬ» внутри хранилища (PressToggle): открыть/закрыть сразу (выход изнутри).
/// Дверь поворачивается на петле плавно у каждого клиента; зашедший позже видит текущее состояние.
/// Без сети работает локально.
/// </summary>
public class NetworkVaultDoor : NetworkBehaviour
{
    public Transform hinge;
    [Tooltip("Угол открытой двери вокруг оси Y петли")]
    public float openAngle = -105f;
    public float swingSpeed = 70f;
    public float hackSeconds = 5f;
    [Tooltip("Тексты таблички и сообщения на табло (дверь универсальная: хранилище, служебные входы)")]
    public string openText = "ХРАНИЛИЩЕ ОТКРЫТО";
    public string closedText = "ХРАНИЛИЩЕ ЗАКРЫТО";
    [Tooltip("Писать об открытии/закрытии на табло (у дверей комнат — нет)")]
    public bool announce = true;

    [Header("Эффекты")]
    public TextMeshPro statusText;
    public Renderer statusLamp;
    public AudioSource audioSource;
    public AudioClip[] openClips;
    public AudioClip[] closeClips;
    public AudioClip[] hackClips;

    [Networked] public NetworkBool IsOpen { get; set; }
    [Networked] public TickTimer Hack { get; set; }

    bool spawned, localOpen, shownOpen;
    float localHackEnd = -1f, angle, nextBeep;
    MaterialPropertyBlock block;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    public bool Open => spawned ? (bool)IsOpen : localOpen;
    bool Hacking => spawned ? Hack.IsRunning : localHackEnd > 0f;
    float HackLeft => spawned ? (Hack.RemainingTime(Runner) ?? 0f) : Mathf.Max(0f, localHackEnd - Time.time);

    public override void Spawned()
    {
        spawned = true;
        shownOpen = IsOpen;
        angle = IsOpen ? openAngle : 0f;                       // новичок в комнате — сразу как у всех
        Apply();
    }

    public override void Despawned(NetworkRunner runner, bool hasState) => spawned = false;

    // ------------------------------------------------------------------ кнопки
    /// Кнопка «ВЗЛОМ» снаружи.
    public void PressHack()
    {
        if (spawned) { RPC_Hack(); return; }
        if (localOpen) localOpen = false;
        else if (localHackEnd < 0f) localHackEnd = Time.time + hackSeconds;
    }

    /// Кнопка «ДВЕРЬ» изнутри.
    public void PressToggle()
    {
        if (spawned) { RPC_Toggle(); return; }
        localOpen = !localOpen;
        localHackEnd = -1f;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Hack()
    {
        if (IsOpen) { IsOpen = false; return; }
        if (!Hack.IsRunning) Hack = TickTimer.CreateFromSeconds(Runner, hackSeconds);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Toggle()
    {
        IsOpen = !IsOpen;
        Hack = TickTimer.None;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        if (Hack.Expired(Runner)) { Hack = TickTimer.None; IsOpen = true; }
    }

    // ------------------------------------------------------------------ визуал (у каждого клиента)
    void Update()
    {
        if (!spawned && localHackEnd > 0f && Time.time >= localHackEnd) { localHackEnd = -1f; localOpen = true; }

        bool open = Open;
        if (open != shownOpen)
        {
            shownOpen = open;
            Play(open ? openClips : closeClips);
            if (announce && HeistGameManager.Instance) HeistGameManager.Instance.PostEvent(open ? openText + "!" : closedText);
        }
        if (Hacking && Time.time >= nextBeep) { nextBeep = Time.time + 0.5f; Play(hackClips); }

        angle = Mathf.MoveTowards(angle, open ? openAngle : 0f, swingSpeed * Time.deltaTime);
        Apply();
    }

    void Apply()
    {
        if (hinge) hinge.localRotation = Quaternion.Euler(0f, angle, 0f);
        bool open = Open, hacking = Hacking;
        if (statusText)
            statusText.text = hacking ? $"ВЗЛОМ… {HackLeft:0.0}" : open ? openText : closedText;
        if (statusLamp)
        {
            block ??= new MaterialPropertyBlock();
            var c = hacking ? new Color(1f, 0.7f, 0.1f) : open ? new Color(0.2f, 1f, 0.3f) : new Color(1f, 0.1f, 0.05f);
            statusLamp.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            block.SetColor(EmissionId, c * 3f);
            statusLamp.SetPropertyBlock(block);
        }
    }

    void Play(AudioClip[] clips)
    {
        if (audioSource && clips != null && clips.Length > 0) audioSource.PlayOneShot(clips[Random.Range(0, clips.Length)]);
    }
}
