using Photon.Voice.Unity;
using UnityEngine;

/// <summary>
/// Рот гориллы «говорит» в такт голосу (Photon Voice 2).
/// Поверх нарисованной линии губ Gorilla_Final стоит тёмная 3D-накладка (эллипсоид с языком и зубами).
/// Она раскрывается вниз и тянется по вертикали по громкости голоса:
///  • своя горилла (видна в зеркале) — громкость своего микрофона (Recorder.LevelMeter, только пока идёт передача,
///    поэтому PUSH TO TALK / PUSH TO MUTE учитываются сами);
///  • горилла другого игрока — громкость его голоса, который играет AudioSource у рта (Speaker на Mouth_Voice).
/// По сети ничего дополнительно не шлётся: голос и так приходит каждому.
/// Накладку собирает Tools/Heist/Player/Reactions (PlayerReactionsSetup).
/// </summary>
[DefaultExecutionOrder(1100)] // после GorillaFinalRig
public class GorillaVoiceMouth : MonoBehaviour
{
    [Tooltip("Тёмная полость рта (сфера-эллипсоид). Её масштаб и позицию задаёт скрипт")]
    public Transform mouthHole;
    [Tooltip("Рендеры рта (полость, язык, зубы) — выключены, пока рот закрыт")]
    public Renderer[] mouthRenderers;

    [Header("Размер — полные диаметры эллипсоида (м, мировые)")]
    public float width = 0.11f;
    public float closedHeight = 0.006f;
    public float openHeight = 0.07f;
    public float depth = 0.03f;
    [Tooltip("Насколько полость утоплена в морду (доля глубины)")]
    public float sink = 0.3f;

    [Header("Громкость → раскрытие")]
    [Tooltip("Тише этого уровня рот закрыт (шум микрофона)")]
    public float noiseFloor = 0.006f;
    [Tooltip("Усиление: уровень × gain = доля раскрытия")]
    public float gain = 11f;
    [Tooltip("Скорость открытия и закрытия (1/с)")]
    public float attack = 30f, release = 13f;

    [Header("Чёткость (поверх значений из префабов)")]
    [Tooltip("Выдвинуть накладку вперёд перед мордочкой (м, по +Z рта), чтобы шерсть её не перекрывала")]
    public float forwardOffset = 0.014f;
    [Tooltip("Дополнительное усиление громкости Photon Voice")]
    public float extraGain = 2.5f;
    [Tooltip("Кривая раскрытия: меньше 1 — даже тихая речь заметно открывает рот")]
    [Range(0.3f, 1f)] public float openCurve = 0.6f;
    [Tooltip("Пиковая громкость микрофона в доле от средней (слоги «щёлкают» резче)")]
    [Range(0f, 1f)] public float peakMix = 0.5f;
    [Tooltip("Насколько рот растягивается вширь при полном открытии (доля ширины)")]
    [Range(0f, 0.5f)] public float stretch = 0.18f;

    [Tooltip("Тест без микрофона: рот хлопает, будто горилла болтает")]
    public bool simulateTalking;

    /// Текущее раскрытие рта 0…1.
    public float Openness { get; private set; }

    NetworkGorillaAvatar net;
    AudioSource remoteVoice;
    readonly float[] buffer = new float[256];
    bool shown = true;

    void Awake()
    {
        net = GetComponentInParent<NetworkGorillaAvatar>(true);
        Apply(0f);
    }

    void LateUpdate()
    {
        if (net && net.IsHiddenSelf) return; // своя невидимая сетевая копия — рендеры выключены сетью
        float target = LevelToOpen(ReadLevel(), noiseFloor, gain * extraGain, openCurve);
        float speed = target > Openness ? attack : release;
        Openness = Mathf.Lerp(Openness, target, 1f - Mathf.Exp(-speed * Time.deltaTime));
        Apply(Openness);
    }

    float ReadLevel()
    {
        if (simulateTalking) return SimulatedLevel(Time.time);
        if (!net)
        {
            var rec = FusionNetworkManager.Instance ? FusionNetworkManager.Instance.VoiceRecorder : null;
            if (!rec || !rec.TransmitEnabled || !rec.IsCurrentlyTransmitting || rec.LevelMeter == null) return 0f;
            return Mathf.Lerp(rec.LevelMeter.CurrentAvgAmp, rec.LevelMeter.CurrentPeakAmp, peakMix);
        }
        if (!remoteVoice)
        {
            var speaker = net.GetComponentInChildren<Speaker>(true);
            if (speaker) remoteVoice = speaker.GetComponent<AudioSource>();
            if (!remoteVoice) return 0f;
        }
        if (!remoteVoice.isPlaying) return 0f;
        remoteVoice.GetOutputData(buffer, 0);
        return Rms(buffer);
    }

    /// Громкость → раскрытие 0…1: всё, что тише шума, — закрыт; дальше усиление и «выпуклая» кривая.
    public static float LevelToOpen(float level, float floor, float totalGain, float curve)
    {
        float x = Mathf.Clamp01((level - floor) * totalGain);
        return x <= 0f ? 0f : Mathf.Pow(x, Mathf.Max(0.05f, curve));
    }

    /// Среднеквадратичная громкость отсчётов.
    public static float Rms(float[] samples)
    {
        if (samples == null || samples.Length == 0) return 0f;
        double sum = 0;
        foreach (var s in samples) sum += s * s;
        return (float)System.Math.Sqrt(sum / samples.Length);
    }

    /// «Болтовня» для теста: слоги ~4.5 в секунду, паузы между фразами.
    public static float SimulatedLevel(float t)
    {
        float phrase = Mathf.Repeat(t, 3.2f) < 2.4f ? 1f : 0f;
        float syllable = Mathf.Max(0f, Mathf.Sin(t * 28f));
        return phrase * syllable * (0.05f + 0.03f * Mathf.Sin(t * 2.3f));
    }

    /// Раскрыть рот сразу (превью и снимки в редакторе).
    public void PreviewOpen(float open) { shown = !(open > 0.03f); Apply(open); }

    void Apply(float open)
    {
        bool show = open > 0.03f;
        if (show != shown)
        {
            shown = show;
            if (mouthRenderers != null) foreach (var r in mouthRenderers) if (r) r.enabled = show;
        }
        if (!mouthHole || !show) return;
        float h = Mathf.Lerp(closedHeight, openHeight, open);
        // челюсть отвисает вниз, рот растягивается вширь — чётко видно, что горилла говорит
        mouthHole.localScale = new Vector3(width * (1f + stretch * open), h, depth);
        mouthHole.localPosition = new Vector3(0f, -h * 0.42f, forwardOffset - depth * sink);
    }
}
