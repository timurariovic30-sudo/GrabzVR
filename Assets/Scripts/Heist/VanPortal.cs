using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Вход на миссию через чёрный автобус в хабе (BlackBusBuilder): игрок проходит через задний проём
/// в салон → 10-секундный отсчёт со звуком зажигания → 5 с поездки по тоннелю (последние 1.5 с — затемнение)
/// → бесшовная загрузка уровня.
/// Вышел из салона — отсчёт отменяется.
/// Зона салона — BoxCollider-триггер на этом объекте (проверяется по позиции игрока, без физики).
/// Скин (PlayerPrefs), режим и косметика (GameSession) переходят на уровень автоматически.
/// </summary>
public class VanPortal : MonoBehaviour
{
    public string sceneName = GameSession.HeistScene;
    public float countdown = 10f;                 // ожидание экипажа перед стартом
    public PlayerModeSwitcher switcher;
    public TextMeshPro countdownText;
    public AudioSource engineSource;
    public Material fadeMaterial;
    public float fadeTime = 1.5f;                 // затемнение — последние 1.5 с поездки
    [Tooltip("Анимация отправки автобуса в тоннель (дверь, фары, дрожь, выезд)")]
    public BusDeparture bus;

    BoxCollider zone;
    Coroutine running;
    string idleText;

    void Awake()
    {
        zone = GetComponent<BoxCollider>();
        if (countdownText) idleText = countdownText.text;
        if (engineSource && !engineSource.clip) engineSource.clip = ProceduralAudio.EngineStart();
    }

    /// Точка в зоне салона (BoxCollider этого объекта)?
    public bool Contains(Vector3 world)
    {
        if (!zone) zone = GetComponent<BoxCollider>();
        if (!zone) return false;
        Vector3 local = transform.InverseTransformPoint(world) - zone.center;
        Vector3 half = zone.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    void Update()
    {
        // в сети рейсом управляет HubBusTrip (общий таймер Fusion, отправление и загрузка у всех разом)
        if (HubBusTrip.Active) { if (running != null) { StopCoroutine(running); running = null; } return; }
        if (!zone || !switcher) return;
        bool inside = Contains(switcher.ActivePlayerPosition());

        if (inside && running == null) running = StartCoroutine(Countdown());
        else if (!inside && running != null && !(bus && bus.IsDriving))   // автобус уже едет (и везёт салон) — не отменяем
        {
            StopCoroutine(running);
            running = null;
            if (engineSource) engineSource.Stop();
            if (countdownText) countdownText.text = idleText;
            if (bus) bus.Cancel();
        }
    }

    IEnumerator Countdown()
    {
        if (engineSource) engineSource.Play();
        if (bus) bus.Board();                                   // дверь закрывается, фары, холостой ход
        for (int s = Mathf.CeilToInt(countdown); s > 0; s--)
        {
            if (countdownText) countdownText.text = $"ПОЕХАЛИ ЧЕРЕЗ {s}...";
            yield return new WaitForSeconds(1f);
        }
        if (countdownText) countdownText.text = "НА ДЕЛО!";
        GameSession.ArrivedFromHub = true;
        // без сети — уровень грузится заранее в фоне (LoadSceneAsync) и включается в конце поездки: переход без зависания
        AsyncOperation preload = null;
        if (FusionNetworkManager.Instance == null || FusionNetworkManager.Instance.Runner == null || !FusionNetworkManager.Instance.Runner.IsRunning)
        {
            preload = SceneManager.LoadSceneAsync(sceneName);
            if (preload != null) preload.allowSceneActivation = false;
        }
        if (bus)
        {
            // автобус едет по тоннелю ровно driveTime (5 с); в последние fadeTime (1.5 с) экран плавно гаснет
            StartCoroutine(bus.DriveOff());
            yield return new WaitForSeconds(Mathf.Max(0f, bus.driveTime - fadeTime));
        }
        // плавное затемнение (сфера вокруг головы, переживает смену сцены — на карте сама плавно осветлит)
        bool faded = false;
        ScreenFade.Ensure(fadeMaterial).Out(fadeTime, () => faded = true);
        while (!faded) yield return null;
        if (preload != null) { preload.allowSceneActivation = true; yield break; }
        // в сети — едут все игроки сессии (общая смена сцены Fusion), без сети — как раньше
        if (!FusionNetworkManager.LoadSceneSynced(sceneName)) SceneManager.LoadSceneAsync(sceneName);
    }
}
