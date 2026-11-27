using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Вход на миссию через чёрный автобус в хабе (BlackBusBuilder).
/// Первый игрок зашёл через задний проём в салон → общий 15-секундный отсчёт на 3D-табличке над дверью
/// (в сети — HubVanSync, одинаковый у всех; без сети — локально). Отсчёт не отменяется.
/// Время вышло → каждый клиент переносит своего игрока в салон, если тот снаружи, рольставня закрывается,
/// звук зажигания → 5 с поездки по тоннелю (последние 1.5 с — затемнение) → загрузка уровня у всех.
/// Зона салона — BoxCollider-триггер на этом объекте (проверяется по позиции игрока, без физики).
/// Скин (PlayerPrefs), режим и косметика (GameSession) переходят на уровень автоматически.
/// </summary>
public class VanPortal : MonoBehaviour
{
    public string sceneName = GameSession.HeistScene;
    [Tooltip("Устарело: длительность отсчёта теперь boardingCountdown (и HubVanSync.countdown в сети)")]
    public float countdown = 10f;
    [Tooltip("Секунд от входа первого игрока до отправления (без сети)")]
    public float boardingCountdown = 15f;
    public PlayerModeSwitcher switcher;
    public TextMeshPro countdownText;
    public AudioSource engineSource;
    public Material fadeMaterial;
    public float fadeTime = 1.5f;                 // затемнение — последние 1.5 с поездки
    [Tooltip("Анимация отправки автобуса в тоннель (дверь, фары, дрожь, выезд)")]
    public BusDeparture bus;
    [Tooltip("Пауза между переносом опоздавших в салон и стартом (дверь успевает закрыться)")]
    public float doorCloseTime = 1.1f;

    BoxCollider zone;
    string idleText;
    float localEnd = -1f;     // без сети: когда отправление
    bool departing;

    void Awake()
    {
        zone = GetComponent<BoxCollider>();
        if (countdownText) idleText = countdownText.text;
        if (engineSource && !engineSource.clip) engineSource.clip = ProceduralAudio.EngineStart();
    }

    bool Networked => HubVanSync.Instance != null && HubVanSync.Instance.IsLive;

    void Update()
    {
        if (!zone || !switcher || departing) return;

        if (IsInside(switcher.ActivePlayerPosition()))
        {
            if (Networked) HubVanSync.Instance.ReportBoarded();
            else if (localEnd < 0f) localEnd = Time.time + boardingCountdown;
        }

        float left = Networked ? HubVanSync.Instance.SecondsLeft() : (localEnd < 0f ? -1f : localEnd - Time.time);
        bool go = Networked ? HubVanSync.Instance.Departed : (localEnd >= 0f && left <= 0f);

        if (countdownText)
            countdownText.text = left < 0f ? idleText : $"ПОЕХАЛИ ЧЕРЕЗ {Mathf.Max(1, Mathf.CeilToInt(left))}...";

        if (go) StartCoroutine(Depart());
    }

    bool IsInside(Vector3 p)
    {
        Vector3 local = transform.InverseTransformPoint(p) - zone.center;
        Vector3 half = zone.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    /// Точка на полу салона для опоздавшего: у каждого игрока своя, чтобы не стоять друг в друге.
    Vector3 BoardingPoint()
    {
        int id = 0;
        var runner = FusionNetworkManager.Instance ? FusionNetworkManager.Instance.Runner : null;
        if (runner != null && runner.IsRunning) id = runner.LocalPlayer.PlayerId;
        Vector3 half = zone.size * 0.5f;
        // сетка 2 × 4 мест в глубине салона
        float x = ((id % 2) - 0.5f) * Mathf.Min(1.1f, half.x);
        float z = (((id / 2) % 4) - 1.5f) / 1.5f * Mathf.Min(half.z * 0.6f, 2.2f);
        Vector3 top = transform.TransformPoint(zone.center + new Vector3(x, half.y * 0.8f, z));
        int mask = ~LayerMask.GetMask("Player", "LocalHead", "Ignore Raycast");
        if (Physics.Raycast(top, Vector3.down, out var hit, zone.size.y * transform.lossyScale.y + 1f, mask, QueryTriggerInteraction.Ignore))
            return hit.point;
        return transform.TransformPoint(zone.center + new Vector3(x, -half.y, z));
    }

    IEnumerator Depart()
    {
        departing = true;
        // кто не успел — в салон, до закрытия двери
        if (!IsInside(switcher.ActivePlayerPosition()))
            switcher.TeleportActive(BoardingPoint(), transform.eulerAngles.y);

        if (countdownText) countdownText.text = "НА ДЕЛО!";
        if (engineSource) engineSource.Play();
        if (bus) bus.Board();                                   // рольставня закрывается, фары, холостой ход
        yield return new WaitForSeconds(doorCloseTime);

        GameSession.ArrivedFromHub = true;
        // без сети — уровень грузится заранее в фоне (LoadSceneAsync) и включается в конце поездки: переход без зависания
        AsyncOperation preload = null;
        if (!Networked && (FusionNetworkManager.Instance == null || FusionNetworkManager.Instance.Runner == null || !FusionNetworkManager.Instance.Runner.IsRunning))
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
        yield return ScreenFader.FadeOut(fadeMaterial, fadeTime);
        if (preload != null) { preload.allowSceneActivation = true; yield break; }
        // в сети — едут все игроки сессии (общая смена сцены Fusion; повторные просьбы сервер отбрасывает)
        if (!FusionNetworkManager.LoadSceneSynced(sceneName)) SceneManager.LoadSceneAsync(sceneName);
    }
}
