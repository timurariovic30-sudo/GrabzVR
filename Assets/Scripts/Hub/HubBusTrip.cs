using Fusion;
using UnityEngine;

/// <summary>
/// Общий рейс чёрного автобуса из Хаба на ограбление (Photon Fusion 2, Shared).
/// Объект сцены с флагом Master Client Object: всё решает хост комнаты (StateAuthority), остальные шлют RPC.
///
///  1) Ожидание: каждый клиент сам проверяет, в салоне ли его игрок (зона VanPortal), и сообщает хосту.
///     Первый зашедший запускает общий TickTimer на 10 с; кто зашёл позже — едет этим же рейсом.
///     Все вышли до конца отсчёта — таймер сбрасывается. Отсчёт «10..9..8» у всех по одному таймеру.
///  2) Отправление: по истечении таймера хост ставит Departing и тик старта. У всех синхронно: вход закрывается
///     (рольставня), фары, звук мотора, автобус едет 5 с по тоннелю (BusDeparture); с 3.5 с — плавное
///     затемнение 1.5 с (ScreenFade). Каждый клиент, у кого экран полностью чёрный, сообщает хосту.
///  3) Загрузка: только когда затемнились все игроки (или через 2.5 с запаса), хост грузит уровень у всех
///     (Runner.LoadScene через FusionNetworkManager.LoadSceneSynced).
///  4) На карте ограбления ScreenFade сам плавно осветляет экран после спавна.
/// Без сети работает старый локальный VanPortal (тоже с затемнением и осветлением).
/// </summary>
public class HubBusTrip : NetworkBehaviour
{
    public VanPortal portal;
    public BusDeparture bus;
    public float countdown = 10f;
    public float driveTime = 5f;
    public float fadeTime = 1.5f;
    public float fadeWaitLimit = 2.5f;

    [Networked] public int OccupantMask { get; set; }
    [Networked] public TickTimer Countdown { get; set; }
    [Networked] public NetworkBool Departing { get; set; }
    [Networked] public int DepartTick { get; set; }
    [Networked] public int FadedMask { get; set; }
    [Networked] public NetworkBool Loading { get; set; }

    public static HubBusTrip Instance { get; private set; }
    /// Сетевой рейс активен — локальный отсчёт VanPortal не работает.
    public static bool Active => Instance && Instance.spawned;

    bool spawned, localInside, sentInside, departedLocally, fadeStarted, fadeReported, boarded;
    PlayerModeSwitcher switcher;
    string idleText;

    /// Состояние рейса одной строкой (отладка, тесты).
    public string DebugState => !spawned ? "offline" :
        $"occ={OccupantMask} cd={(Countdown.IsRunning ? (Countdown.RemainingTime(Runner) ?? -1f).ToString("F1") : "-")} dep={Departing} faded={FadedMask} load={Loading} master={HasStateAuthority}";

    public static string Status => Instance ? Instance.DebugState : "none";

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    public override void Spawned()
    {
        spawned = true;
        switcher = FindAnyObjectByType<PlayerModeSwitcher>(FindObjectsInactive.Include);
        if (portal && portal.countdownText) idleText = portal.countdownText.text;
        if (portal) ScreenFade.Ensure(portal.fadeMaterial);
    }

    public override void Despawned(NetworkRunner runner, bool hasState) => spawned = false;

    static int Bit(PlayerRef p) => 1 << (p.PlayerId & 31);

    // ------------------------------------------------------------------ клиент: я в салоне?
    void Update()
    {
        if (!spawned) return;
        if (!Departing && portal && switcher)
        {
            localInside = portal.Contains(switcher.ActivePlayerPosition());
            if (localInside != sentInside) { sentInside = localInside; RPC_SetInside(Runner.LocalPlayer, localInside); }
        }
        UpdateBoard();
        // отсчёт идёт — у всех заведён мотор; дверь открыта, чтобы успели зайти остальные (закроется при отправлении)
        bool waiting = !Departing && Countdown.IsRunning;
        if (waiting != boarded && !Departing)
        {
            boarded = waiting;
            if (portal && portal.engineSource) { if (waiting) portal.engineSource.Play(); else portal.engineSource.Stop(); }
        }
        if (Departing) UpdateDeparture();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_SetInside(PlayerRef player, NetworkBool inside)
    {
        if (Departing) return;                                    // вход закрыт — рейс уже ушёл
        OccupantMask = inside ? OccupantMask | Bit(player) : OccupantMask & ~Bit(player);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Faded(PlayerRef player) => FadedMask |= Bit(player);

    // ------------------------------------------------------------------ хост: таймер, отправление, загрузка
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        // вышедших из комнаты убираем из экипажа
        int active = 0; foreach (var p in Runner.ActivePlayers) active |= Bit(p);
        OccupantMask &= active;

        if (!Departing)
        {
            if (OccupantMask == 0) { if (Countdown.IsRunning) Countdown = TickTimer.None; return; }   // все вышли — сброс
            if (!Countdown.IsRunning) Countdown = TickTimer.CreateFromSeconds(Runner, countdown);
            if (Countdown.Expired(Runner))
            {
                Departing = true;
                DepartTick = Runner.Tick;
                Countdown = TickTimer.None;
                FadedMask = 0;
            }
            return;
        }
        if (Loading) return;
        float elapsed = (Runner.Tick - DepartTick) * Runner.DeltaTime;
        bool allFaded = (FadedMask & active) == active;
        if (elapsed >= driveTime && (allFaded || elapsed >= driveTime + fadeWaitLimit))
        {
            Loading = true;
            GameSession.ArrivedFromHub = true;
            if (!FusionNetworkManager.LoadSceneSynced(GameSession.HeistScene))
                UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(GameSession.HeistScene);
        }
    }

    // ------------------------------------------------------------------ у всех: табло, поездка, затемнение
    void UpdateBoard()
    {
        var txt = portal ? portal.countdownText : null;
        if (!txt) return;
        string s;
        if (Departing) s = "НА ДЕЛО!";
        else if (Countdown.IsRunning) s = $"ПОЕХАЛИ ЧЕРЕЗ {Mathf.CeilToInt(Countdown.RemainingTime(Runner) ?? 0f)}...";
        else s = idleText;
        if (txt.text != s) txt.text = s;
    }

    void UpdateDeparture()
    {
        float elapsed = (Runner.Tick - DepartTick) * Runner.DeltaTime;
        if (!departedLocally)
        {
            departedLocally = true;
            GameSession.ArrivedFromHub = true;
            if (portal && portal.engineSource && !portal.engineSource.isPlaying) portal.engineSource.Play();
            if (bus) { bus.driveTime = driveTime; StartCoroutine(bus.DriveOff()); }   // рольставня, фары, тряска, выезд
        }
        if (!fadeStarted && elapsed >= driveTime - fadeTime)
        {
            fadeStarted = true;
            var f = ScreenFade.Ensure(portal ? portal.fadeMaterial : null);
            f.Out(Mathf.Max(0.1f, driveTime - elapsed), () => { if (!fadeReported) { fadeReported = true; RPC_Faded(Runner.LocalPlayer); } });
        }
    }
}
