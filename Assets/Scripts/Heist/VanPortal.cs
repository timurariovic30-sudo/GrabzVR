using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Вход на миссию через фургон: игрок залезает в открытый кузов → 3-секундный отсчёт со звуком
/// зажигания → плавное затемнение → загрузка уровня. Вылез из кузова — отсчёт отменяется.
/// Зона кузова — BoxCollider-триггер на этом объекте (проверяется по позиции игрока, без физики).
/// Скин (PlayerPrefs), режим и косметика (GameSession) переходят на уровень автоматически.
/// </summary>
public class VanPortal : MonoBehaviour
{
    public string sceneName = GameSession.HeistScene;
    public float countdown = 3f;
    public PlayerModeSwitcher switcher;
    public TextMeshPro countdownText;
    public AudioSource engineSource;
    public Material fadeMaterial;
    public float fadeTime = 0.8f;

    BoxCollider zone;
    Coroutine running;
    string idleText;

    void Awake()
    {
        zone = GetComponent<BoxCollider>();
        if (countdownText) idleText = countdownText.text;
        if (engineSource && !engineSource.clip) engineSource.clip = ProceduralAudio.EngineStart();
    }

    void Update()
    {
        if (!zone || !switcher) return;
        Vector3 p = switcher.ActivePlayerPosition();
        Vector3 local = transform.InverseTransformPoint(p) - zone.center;
        Vector3 half = zone.size * 0.5f;
        bool inside = Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;

        if (inside && running == null) running = StartCoroutine(Countdown());
        else if (!inside && running != null)
        {
            StopCoroutine(running);
            running = null;
            if (engineSource) engineSource.Stop();
            if (countdownText) countdownText.text = idleText;
        }
    }

    IEnumerator Countdown()
    {
        if (engineSource) engineSource.Play();
        for (int s = Mathf.CeilToInt(countdown); s > 0; s--)
        {
            if (countdownText) countdownText.text = $"ПОЕХАЛИ ЧЕРЕЗ {s}...";
            yield return new WaitForSeconds(1f);
        }
        if (countdownText) countdownText.text = "НА ДЕЛО!";
        GameSession.ArrivedFromHub = true;
        yield return ScreenFader.FadeOut(fadeMaterial, fadeTime);
        // в сети — едут все игроки сессии (общая смена сцены Fusion), без сети — как раньше
        if (!FusionNetworkManager.LoadSceneSynced(sceneName)) SceneManager.LoadScene(sceneName);
    }
}
