using Fusion;
using TMPro;
using UnityEngine;

/// <summary>
/// Щиток управления лазерами в кабинете директора (Photon Fusion 2, Shared).
/// Объект сцены с Master Client Object: кнопка «ЛАЗЕРЫ» шлёт хосту RPC, хост запускает [Networked] таймер,
/// и на offSeconds секунд все лазерные решётки хранилища выключаются у всех игроков (лучи и их триггеры).
/// Зашедший позже видит то же состояние. Без сети работает локально.
/// </summary>
public class NetworkLaserControl : NetworkBehaviour
{
    [Tooltip("Лазерные решётки (корни с LaserAlarm)")]
    public GameObject[] grids;
    public float offSeconds = 20f;
    public TextMeshPro statusText;
    public Renderer statusLamp;
    public AudioSource audioSource;
    public AudioClip[] offClips;
    public AudioClip[] onClips;

    [Networked] public TickTimer Off { get; set; }

    bool spawned, shownOff;
    float localOffEnd = -1f;
    MaterialPropertyBlock block;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    bool IsOff => spawned ? Off.IsRunning : localOffEnd > Time.time;
    float Left => spawned ? (Off.RemainingTime(Runner) ?? 0f) : Mathf.Max(0f, localOffEnd - Time.time);

    public override void Spawned() => spawned = true;
    public override void Despawned(NetworkRunner runner, bool hasState) => spawned = false;

    /// Кнопка на щитке.
    public void PressDisable()
    {
        if (spawned) RPC_Disable();
        else localOffEnd = Time.time + offSeconds;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Disable()
    {
        if (!Off.IsRunning) Off = TickTimer.CreateFromSeconds(Runner, offSeconds);
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority && Off.Expired(Runner)) Off = TickTimer.None;
    }

    void Update()
    {
        bool off = IsOff;
        if (off != shownOff)
        {
            shownOff = off;
            foreach (var g in grids) if (g) g.SetActive(!off);
            var clips = off ? offClips : onClips;
            if (audioSource && clips != null && clips.Length > 0) audioSource.PlayOneShot(clips[Random.Range(0, clips.Length)]);
            if (HeistGameManager.Instance) HeistGameManager.Instance.PostEvent(off ? "ЛАЗЕРЫ ОТКЛЮЧЕНЫ!" : "ЛАЗЕРЫ СНОВА РАБОТАЮТ");
        }
        if (statusText) statusText.text = off ? $"ЛАЗЕРЫ: ВЫКЛ {Mathf.CeilToInt(Left)}" : "ЛАЗЕРЫ: ВКЛ";
        if (statusLamp)
        {
            block ??= new MaterialPropertyBlock();
            var c = off ? new Color(0.2f, 1f, 0.3f) : new Color(1f, 0.1f, 0.05f);
            statusLamp.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            block.SetColor(EmissionId, c * 3f);
            statusLamp.SetPropertyBlock(block);
        }
    }
}
