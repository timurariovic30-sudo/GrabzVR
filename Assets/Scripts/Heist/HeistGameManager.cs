using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Игровой цикл: Лобби (таймер стоит) → СТАРТ (шлюз открывается, 3 минуты) → Победа / Поражение → перезапуск.
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

    public float TimeLeft { get; private set; }
    public int Loot { get; private set; }
    public RoundState State { get; private set; } = RoundState.Lobby;
    public bool IsPlaying => State == RoundState.Playing;

    public string LastEvent { get; private set; }
    public float LastEventTime { get; private set; } = -999f;

    /// Режим, выбранный в Главном Хабе (стол планирования).
    public GameSession.Mode Mode { get; private set; }

    void Awake()
    {
        Instance = this;
        Mode = GameSession.CurrentMode;
        if (Mode == GameSession.Mode.PoliceChase) roundDuration = Mathf.Min(roundDuration, 150f);
        TimeLeft = roundDuration;
    }

    void Start()
    {
        PostEvent("РЕЖИМ: " + GameSession.ModeTitles[(int)Mode]);
        // Приехали на фургоне из Хаба — ограбление стартует само
        if (GameSession.ArrivedFromHub) { GameSession.ArrivedFromHub = false; Invoke(nameof(StartHeist), 1.5f); }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (!IsPlaying) return;
        if (Mode == GameSession.Mode.ParkourTraining) return; // тренировка — без таймера
        TimeLeft -= Time.deltaTime;
        if (TimeLeft <= 0f) EndRound(RoundState.Lost);
    }

    /// Кнопка «СТАРТ ОГРАБЛЕНИЯ»: открывает шлюз и запускает таймер.
    public void StartHeist()
    {
        if (State != RoundState.Lobby) { PostEvent("ОГРАБЛЕНИЕ УЖЕ ИДЁТ!"); return; }
        State = RoundState.Playing;
        TimeLeft = roundDuration;
        if (airlock) airlock.Open();
        PostEvent("ПОЕХАЛИ! ШЛЮЗ ОТКРЫТ");
    }

    public void AddLoot(LootItem item)
    {
        if (!IsPlaying) return;
        Loot += item.value;
        PostEvent($"+${item.value}  {item.itemName}");
        if (Loot >= targetLoot) EndRound(RoundState.Won);
    }

    public void ApplyPenalty(float seconds, string reason)
    {
        if (!IsPlaying) { PostEvent(reason.Split('!')[0] + " (ТЕСТ)"); return; }
        switch (Mode)
        {
            case GameSession.Mode.ParkourTraining:
                PostEvent(reason.Split('!')[0] + " (ТРЕНИРОВКА — БЕЗ ШТРАФА)");
                return;
            case GameSession.Mode.Hardcore:
                PostEvent("ПОЙМАН! ХАРДКОР: ОДНА ЖИЗНЬ");
                EndRound(RoundState.Lost);
                return;
            case GameSession.Mode.PoliceChase:
                seconds *= 2f;
                reason = reason.Replace("15", "30");
                break;
        }
        TimeLeft -= seconds;
        PostEvent(reason);
        if (TimeLeft <= 0f) EndRound(RoundState.Lost);
    }

    /// Тестовая кнопка: обнулить счёт (лут возвращает LootRegistry).
    public void ResetScore()
    {
        Loot = 0;
        PostEvent("ЛУТ СБРОШЕН");
    }

    public void PostEvent(string text)
    {
        LastEvent = text;
        LastEventTime = Time.time;
    }

    void EndRound(RoundState result)
    {
        TimeLeft = Mathf.Max(0f, TimeLeft);
        State = result;
        StartCoroutine(RestartAfterDelay());
    }

    IEnumerator RestartAfterDelay()
    {
        yield return new WaitForSecondsRealtime(restartDelay);
        // После раунда — назад в Главный Хаб (если он есть в Build Settings), иначе перезапуск уровня
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
