using Fusion;
using UnityEngine;

/// <summary>
/// Общий отсчёт посадки в автобус Хаба (Photon Fusion 2, Shared) — объект сцены MainHub.
/// NetworkObject с флагом Master Client Object: StateAuthority всегда у мастер-клиента.
///  • Первый игрок зашёл в салон → его клиент шлёт RPC хосту → хост запускает [Networked] TickTimer на countdown секунд.
///  • Таймер истёк → хост ставит [Networked] Departed; у каждого клиента VanPortal переносит своего игрока в салон
///    (если тот снаружи), закрывает дверь и увозит автобус. Все едут вместе, сцену грузит общая смена сцены Fusion.
/// Отсчёт не отменяется, даже если все вышли: кто не успел — будет перенесён в салон.
/// Без сети VanPortal ведёт тот же отсчёт локально.
/// Настройка сцены: Tools/Heist/Hub/Setup Van Sync (HubVanSyncSetup).
/// </summary>
public class HubVanSync : NetworkBehaviour
{
    public static HubVanSync Instance { get; private set; }

    [Tooltip("Секунд от входа первого игрока до отправления")]
    public float countdown = 15f;

    /// Идёт общий отсчёт.
    [Networked] public TickTimer Countdown { get; set; }
    /// Отсчёт кончился — все едут.
    [Networked] public NetworkBool Departed { get; set; }

    /// Объект заспавнен в сети (иначе VanPortal считает сам).
    public bool IsLive { get; private set; }

    public override void Spawned()
    {
        Instance = this;
        IsLive = true;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        IsLive = false;
        if (Instance == this) Instance = null;
    }

    /// Свой игрок зашёл в салон.
    public void ReportBoarded()
    {
        if (IsLive && !Departed && !Countdown.IsRunning) RPC_Boarded();
    }

    /// Секунд до отправления; меньше 0 — отсчёт не идёт.
    public float SecondsLeft()
    {
        if (!IsLive) return -1f;
        if (Departed) return 0f;
        return Countdown.IsRunning ? Mathf.Max(0f, Countdown.RemainingTime(Runner) ?? 0f) : -1f;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Boarded()
    {
        if (Departed || Countdown.IsRunning) return;
        Countdown = TickTimer.CreateFromSeconds(Runner, Mathf.Clamp(countdown, 1f, 120f));
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || Departed || !Countdown.Expired(Runner)) return;
        Departed = true;
        Countdown = TickTimer.None;
    }
}
