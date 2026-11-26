using Fusion;
using TMPro;
using UnityEngine;

/// <summary>
/// Кинематографичный старт ограбления (Photon Fusion 2, Shared): все игроки появляются в кузове фургона
/// GetawayVan_Arrival у парадных ступенек Банка. Каждый клиент сам следит за своим игроком: вышел из кузова —
/// шлёт хосту RPC. Хост (StateAuthority, объект с Master Client Object) ставит [Networked] IsHeistStarted = true,
/// когда из фургона вышла вся команда или через teamWait секунд после первого вышедшего, и запускает общий таймер
/// раунда (HeistGameManager.StartHeist). У всех игроков синхронно: задние двери захлопываются, двигатель газует,
/// фургон уезжает вперёд за угол и пропадает. Кто остался в кузове — переносится на тротуар.
/// Зашедший позже видит фургон уже уехавшим. Без сети всё то же самое работает локально.
/// </summary>
public class NetworkHeistStartTrigger : NetworkBehaviour
{
    [Header("Фургон (движущаяся часть — НЕ корень NetworkObject)")]
    public Transform van;
    [Tooltip("Петли задних дверей: в сцене стоят открытыми, при старте поворачиваются в closedEuler")]
    public Transform[] doorHinges;
    public Vector3[] closedEuler;
    [Tooltip("Объём кузова в локальных координатах van")]
    public Vector3 cargoCenter = new Vector3(0, 1.2f, -1f);
    public Vector3 cargoSize = new Vector3(2f, 2.6f, 3.6f);
    [Tooltip("Куда перенести игрока, если он остался в кузове, когда фургон уезжает (тротуар у ступенек)")]
    public Transform exitPoint;
    [Tooltip("Маршрут отъезда (мировые точки, первая — место стоянки)")]
    public Vector3[] path;
    public float driveDuration = 7f;
    public float teamWait = 10f;

    [Header("Эффекты")]
    public AudioSource engine;
    public AudioSource doorSource;
    public AudioClip[] doorSlamClips;
    public Light[] headlights;
    public TextMeshPro statusText;

    [Networked] public NetworkBool IsHeistStarted { get; set; }
    [Networked] public int ExitedMask { get; set; }
    [Networked] public TickTimer TeamTimer { get; set; }

    public static NetworkHeistStartTrigger Instance { get; private set; }

    bool spawned, localStarted, reported, wasInside, departing, gone;
    float departT, localTeamEnd = -1f;
    PlayerModeSwitcher switcher;
    Quaternion[] openRot;
    float[] segLen;
    float pathLen;

    public bool Started => spawned ? (bool)IsHeistStarted : localStarted;

    void Awake()
    {
        Instance = this;
        // старт раунда теперь — выход из фургона, а не автостарт HeistGameManager после приезда из Хаба
        GameSession.ArrivedFromHub = false;
        openRot = new Quaternion[doorHinges.Length];
        for (int i = 0; i < doorHinges.Length; i++) openRot[i] = doorHinges[i].localRotation;
        segLen = new float[Mathf.Max(0, path.Length - 1)];
        for (int i = 0; i < segLen.Length; i++) { segLen[i] = Vector3.Distance(path[i], path[i + 1]); pathLen += segLen[i]; }
        if (engine)
        {
            engine.clip = BankAudio.EngineLoop();
            engine.loop = true;
            engine.pitch = 1f;
            engine.Play();
        }
    }

    void Start() => switcher = FindAnyObjectByType<PlayerModeSwitcher>(FindObjectsInactive.Include);

    void OnDestroy() { if (Instance == this) Instance = null; }

    public override void Spawned()
    {
        spawned = true;
        if (IsHeistStarted) HideInstant();                    // зашли в комнату, когда команда уже в банке
    }

    public override void Despawned(NetworkRunner runner, bool hasState) => spawned = false;

    // ------------------------------------------------------------------ выход из фургона
    bool InsideCargo(Vector3 worldPos)
    {
        if (!van || gone) return false;
        var p = van.InverseTransformPoint(worldPos) - cargoCenter;
        return Mathf.Abs(p.x) < cargoSize.x / 2 && Mathf.Abs(p.y) < cargoSize.y / 2 && Mathf.Abs(p.z) < cargoSize.z / 2;
    }

    void Update()
    {
        // первые 2 с после загрузки игрок ещё может «телепортироваться» на место — не считаем это выходом
        if (!Started && !reported && switcher && Time.timeSinceLevelLoad > 2f)
        {
            var pos = switcher.ActivePlayerPosition();
            bool inside = InsideCargo(pos);
            if (inside) wasInside = true;
            else if (pos != Vector3.zero && (wasInside || Vector3.Distance(pos, van.position) > 8f))   // zero — игрок ещё не активен
            {
                reported = true;
                ReportExit();
            }
        }

        // без сети: ждать команду некого — фургон уезжает сразу
        if (!spawned && !localStarted && localTeamEnd > 0f && Time.time >= localTeamEnd) StartLocal();

        UpdateStatus();
        if (Started && !departing && !gone) BeginDeparture();
        if (departing) AnimateDeparture();
    }

    void ReportExit()
    {
        if (spawned) RPC_PlayerExited(Runner.LocalPlayer);
        else localTeamEnd = Time.time + 0.5f;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_PlayerExited(PlayerRef player)
    {
        if (IsHeistStarted) return;
        ExitedMask |= 1 << (player.PlayerId & 31);
        if (!TeamTimer.IsRunning) TeamTimer = TickTimer.CreateFromSeconds(Runner, teamWait);
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || IsHeistStarted || ExitedMask == 0) return;
        bool all = true;
        foreach (var pl in Runner.ActivePlayers)
            if ((ExitedMask & (1 << (pl.PlayerId & 31))) == 0) { all = false; break; }
        if (all || TeamTimer.Expired(Runner))
        {
            IsHeistStarted = true;
            TeamTimer = TickTimer.None;
            if (HeistGameManager.Instance) HeistGameManager.Instance.StartHeist();   // общий сетевой таймер раунда
        }
    }

    void StartLocal()
    {
        localStarted = true;
        if (HeistGameManager.Instance) HeistGameManager.Instance.StartHeist();
    }

    void UpdateStatus()
    {
        if (!statusText) return;
        if (Started || gone) { if (statusText.gameObject.activeSelf) statusText.gameObject.SetActive(false); return; }
        float left = -1f;
        if (spawned && TeamTimer.IsRunning) left = TeamTimer.RemainingTime(Runner) ?? 0f;
        statusText.text = left >= 0f ? $"ЖДЁМ КОМАНДУ: {Mathf.CeilToInt(left)}" : "ВЫХОДИ ИЗ ФУРГОНА — В БАНК!";
    }

    // ------------------------------------------------------------------ отъезд (у каждого клиента локально, по сетевому флагу)
    void BeginDeparture()
    {
        departing = true;
        departT = 0f;
        // кто ещё в кузове — на тротуар у ступенек
        if (switcher && exitPoint && InsideCargo(switcher.ActivePlayerPosition()))
            switcher.TeleportActive(exitPoint.position, exitPoint.eulerAngles.y);
        PlaySlam();
    }

    void PlaySlam()
    {
        if (doorSource && doorSlamClips != null && doorSlamClips.Length > 0)
            doorSource.PlayOneShot(doorSlamClips[Random.Range(0, doorSlamClips.Length)]);
    }

    void AnimateDeparture()
    {
        float prev = departT;
        departT += Time.deltaTime;

        // 0…0.6 с: двери захлопываются (вторая — чуть позже, со вторым ударом)
        for (int i = 0; i < doorHinges.Length; i++)
        {
            float k = Mathf.Clamp01((departT - i * 0.25f) / 0.35f);
            doorHinges[i].localRotation = Quaternion.Slerp(openRot[i], Quaternion.Euler(closedEuler[i]), k * k);
        }
        if (prev < 0.25f && departT >= 0.25f) PlaySlam();

        // 0.6…1.4 с: газ на месте, дальше — разгон по маршруту
        if (engine) engine.pitch = Mathf.Lerp(1f, 1.9f, Mathf.Clamp01((departT - 0.6f) / 1.5f));
        float u = Mathf.Clamp01((departT - 1.2f) / driveDuration);
        if (u > 0f && pathLen > 0f)
        {
            float dist = u * u * pathLen;                       // плавный разгон
            int s = 0;
            while (s < segLen.Length - 1 && dist > segLen[s]) { dist -= segLen[s]; s++; }
            var a = path[s]; var b = path[s + 1];
            van.position = Vector3.Lerp(a, b, segLen[s] > 0 ? dist / segLen[s] : 1f);
            var dir = b - a; dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) van.rotation = Quaternion.Slerp(van.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 4f);
        }
        if (engine && u > 0.75f) engine.volume = Mathf.Lerp(engine.volume, 0f, Time.deltaTime * 2f);
        if (u >= 1f) HideInstant();
    }

    void HideInstant()
    {
        gone = true;
        departing = false;
        if (engine) engine.Stop();
        foreach (var l in headlights) if (l) l.enabled = false;
        if (van) van.gameObject.SetActive(false);
        if (statusText) statusText.gameObject.SetActive(false);
    }
}
