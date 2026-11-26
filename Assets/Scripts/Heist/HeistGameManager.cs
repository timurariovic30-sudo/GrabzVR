using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Игровой цикл: Лобби (таймер стоит) → СТАРТ (шлюз открывается, 3 минуты) → Победа / Поражение → назад в Хаб.
///
/// Сеть (Fusion Shared): если на сцене заспавнен HeistNetworkState, раунд, таймер, общий котёл и режим хранятся
/// в его [Networked]-свойствах. Меняет их только хост комнаты (StateAuthority = мастер-клиент), остальные игроки
/// отправляют ему просьбы через RPC (StartHeist / AddLoot / Announce). Табло, шлюз и конец раунда у всех одинаковые.
/// Без сети всё работает локально, как раньше.
/// </summary>
public class HeistGameManager : MonoBehaviour
{
    public enum RoundState { Lobby, Playing, Won, Lost }

    public static HeistGameManager Instance { get; private set; }

    [Header("Раунд")]
    public float roundDuration = 180f;
    public int targetLoot = 4000;
    public float restartDelay = 5f;
    [Tooltip("Шлюз между Убежищем и зданием — открывается по СТАРТ")]
    public AirlockDoor airlock;

    // ------------------------------------------------------------------ состояние (сеть или локально)
    static HeistNetworkState Net => HeistNetworkState.Instance;
    /// Сетевой раунд идёт (объект состояния заспавнен).
    public static bool Online => HeistNetworkState.Instance != null;
    /// Этот клиент решает за раунд: хост комнаты или одиночная игра.
    public static bool IsAuthority => !Online || Net.HasStateAuthority;

    float localTime;
    int localLoot;
    RoundState localState = RoundState.Lobby;
    GameSession.Mode localMode;

    public float TimeLeft
    {
        get => Online ? Net.TimeLeft : localTime;
        private set { if (Online) { if (Net.HasStateAuthority) Net.TimeLeft = value; } else localTime = value; }
    }

    public int Loot
    {
        get => Online ? Net.Loot : localLoot;
        private set { if (Online) { if (Net.HasStateAuthority) Net.Loot = value; } else localLoot = value; }
    }

    public RoundState State
    {
        get => Online ? (RoundState)Net.RoundState : localState;
        private set { if (Online) { if (Net.HasStateAuthority) Net.RoundState = (int)value; } else localState = value; }
    }

    /// Режим, выбранный в Главном Хабе (стол планирования). В сети — режим хоста.
    public GameSession.Mode Mode => Online ? (GameSession.Mode)Net.Mode : localMode;

    public bool IsPlaying => State == RoundState.Playing;

    public string LastEvent { get; private set; }
    public float LastEventTime { get; private set; } = -999f;

    bool restartScheduled;

    void Awake()
    {
        Instance = this;
        localMode = GameSession.CurrentMode;
        if (localMode == GameSession.Mode.PoliceChase) roundDuration = Mathf.Min(roundDuration, 150f);
        localTime = roundDuration;
    }

    void Start()
    {
        PostEvent("РЕЖИМ: " + GameSession.ModeTitles[(int)localMode]);
        // Приехали на фургоне из Хаба — ограбление стартует само (в сети это делает хост)
        if (GameSession.ArrivedFromHub) { GameSession.ArrivedFromHub = false; Invoke(nameof(AutoStart), 1.5f); }
    }

    void AutoStart()
    {
        if (IsAuthority && State == RoundState.Lobby) StartHeist();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// Хост только что получил сетевое состояние: переносим туда локальный раунд (уровень мог начаться до подключения).
    public void PushLocalToNetwork(HeistNetworkState net)
    {
        net.Mode = (int)localMode;
        net.TimeLeft = localTime;
        net.Loot = localLoot;
        net.RoundState = (int)localState;
    }

    void Update()
    {
        // шлюз открыт у всех, как только раунд начался (и у тех, кто зашёл позже)
        if (airlock && State != RoundState.Lobby && !airlock.IsOpen) airlock.Open();

        if (!IsAuthority) return;

        if ((State == RoundState.Won || State == RoundState.Lost) && !restartScheduled)
        {
            restartScheduled = true;
            StartCoroutine(RestartAfterDelay());
        }

        if (!IsPlaying) return;
        if (Mode == GameSession.Mode.ParkourTraining) return; // тренировка — без таймера
        TimeLeft -= Time.deltaTime;
        if (TimeLeft <= 0f) EndRound(RoundState.Lost);
    }

    /// Кнопка «СТАРТ ОГРАБЛЕНИЯ»: открывает шлюз и запускает таймер (у всех игроков).
    public void StartHeist()
    {
        if (State != RoundState.Lobby) { PostEvent("ОГРАБЛЕНИЕ УЖЕ ИДЁТ!"); return; }
        if (!IsAuthority) { Net.RpcStartHeist(); return; }
        State = RoundState.Playing;
        TimeLeft = roundDuration;
        if (airlock) airlock.Open();
        Announce(airlock ? "ПОЕХАЛИ! ШЛЮЗ ОТКРЫТ" : "ПОЕХАЛИ! ОГРАБЛЕНИЕ НАЧАЛОСЬ");
    }

    public void AddLoot(LootItem item) => AddLoot(item.value, item.itemName);

    /// Деньги в общий котёл команды (в сети считает хост).
    public void AddLoot(int value, string itemName)
    {
        if (!IsAuthority) { Net.RpcAddLoot(value, itemName); return; }
        if (!IsPlaying) return;
        Loot += value;
        Announce($"+${value}  {itemName}");
        if (Loot >= targetLoot) EndRound(RoundState.Won);
    }

    /// Штраф за тревогу. Вызывает AlarmSystem у хоста (или в одиночной игре).
    public void ApplyPenalty(float seconds, string reason)
    {
        if (!IsAuthority) return;
        if (!IsPlaying) { Announce(reason.Split('!')[0] + " (ТЕСТ)"); return; }
        switch (Mode)
        {
            case GameSession.Mode.ParkourTraining:
                Announce(reason.Split('!')[0] + " (ТРЕНИРОВКА — БЕЗ ШТРАФА)");
                return;
            case GameSession.Mode.Hardcore:
                Announce("ПОЙМАН! ХАРДКОР: ОДНА ЖИЗНЬ");
                EndRound(RoundState.Lost);
                return;
            case GameSession.Mode.PoliceChase:
                seconds *= 2f;
                reason = reason.Replace("15", "30");
                break;
        }
        TimeLeft -= seconds;
        Announce(reason);
        if (TimeLeft <= 0f) EndRound(RoundState.Lost);
    }

    /// Тестовая кнопка: обнулить счёт (лут возвращает LootRegistry). В сети — только у хоста.
    public void ResetScore()
    {
        if (!IsAuthority) return;
        Loot = 0;
        Announce("ЛУТ СБРОШЕН");
    }

    /// Событие на табло только у этого игрока (подсказки, телепорт).
    public void PostEvent(string text) => ShowEvent(text);

    /// Событие на табло у всей команды (в сети его рассылает хост).
    public void Announce(string text)
    {
        if (Online && !Net.HasStateAuthority) { Net.RpcAnnounce(text); return; }
        ShowEvent(text);
        if (Online) Net.PublishEvent(text);
    }

    public void ShowEvent(string text)
    {
        LastEvent = text;
        LastEventTime = Time.time;
    }

    void EndRound(RoundState result)
    {
        if (!IsAuthority || State != RoundState.Playing) return;
        TimeLeft = Mathf.Max(0f, TimeLeft);
        State = result;
    }

    IEnumerator RestartAfterDelay()
    {
        yield return new WaitForSecondsRealtime(restartDelay);
        // После раунда — назад в Главный Хаб (если он есть в Build Settings), иначе перезапуск уровня.
        // В сети сцену меняет хост для всей комнаты (FusionNetworkManager.LoadSceneSynced).
        if (Application.CanStreamedLevelBeLoaded(GameSession.HubScene))
        {
            GameSession.ArrivedFromHub = false;
            if (!FusionNetworkManager.LoadSceneSynced(GameSession.HubScene)) SceneManager.LoadScene(GameSession.HubScene);
            yield break;
        }
        var scene = gameObject.scene;
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        SceneManager.LoadScene(scene.name);
#endif
    }
}
