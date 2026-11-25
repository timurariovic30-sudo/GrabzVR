using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Тревога по всему зданию: сирена (процедурная), мигающий красный аварийный свет, мигание лазеров.
///  • TriggerIntrusion — сработка от лазера/камеры/стекла: тревога на N секунд + штраф таймеру (один раз за тревогу).
///  • ToggleManual — тестовая кнопка на пульте: включить/выключить без штрафа.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AlarmSystem : MonoBehaviour
{
    public static AlarmSystem Instance { get; private set; }

    public float intrusionDuration = 8f;
    public float penaltySeconds = 15f;
    [Tooltip("Красные аварийные лампы (обычно выключены)")]
    public Light[] alarmLights;
    public float alarmLightIntensity = 6f;
    [Tooltip("Красные «маячки» (эмиссия мигает вместе со светом)")]
    public Renderer[] beacons;

    public bool IsActive => manual || Time.time < intrusionUntil;
    public bool ManualOn => manual;

    AudioSource siren;
    float intrusionUntil = -1f;
    bool manual;
    MaterialPropertyBlock block;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        Instance = this;
        siren = GetComponent<AudioSource>();
        siren.clip = ProceduralAudio.AlarmBeep(); // короткие «пи… пи…» вместо непрерывного воя
        siren.loop = true;
        siren.playOnAwake = false;
        siren.spatialBlend = 0f;
        siren.volume = sirenVolume;
        SetLights(0f);
    }

    [Header("Звук")]
    [Tooltip("Громкость писка тревоги (раньше было 0.35 непрерывной сиреной)")]
    public float sirenVolume = 0.2f;
    [Tooltip("Сколько секунд пищит после начала тревоги (свет мигает всё время тревоги)")]
    public float beepDuration = 5f;
    float activeSince = -1f;

    void OnDestroy() { if (Instance == this) Instance = null; }

    public void TriggerIntrusion(string reason)
    {
        bool wasActive = IsActive;
        intrusionUntil = Mathf.Max(intrusionUntil, Time.time + intrusionDuration);
        if (!wasActive && HeistGameManager.Instance != null)
            HeistGameManager.Instance.ApplyPenalty(penaltySeconds, $"ТРЕВОГА: {reason}! −{penaltySeconds:0} СЕК");
    }

    public void ToggleManual()
    {
        manual = !manual;
        if (!manual) intrusionUntil = -1f;
        Debug.Log("[AlarmSystem] Ручная тревога: " + (manual ? "ВКЛ" : "ВЫКЛ"));
    }

    void Update()
    {
        bool active = IsActive;
        if (active && activeSince < 0f) activeSince = Time.time;
        if (!active) activeSince = -1f;
        bool beeping = active && Time.time - activeSince < beepDuration;
        siren.volume = sirenVolume;
        if (beeping && !siren.isPlaying) siren.Play();
        else if (!beeping && siren.isPlaying) siren.Stop();
        SetLights(active ? (0.5f + 0.5f * Mathf.Sin(Time.time * 9f)) : 0f);
    }

    void SetLights(float k)
    {
        if (alarmLights != null)
            foreach (var l in alarmLights) if (l) { l.enabled = k > 0.01f; l.intensity = alarmLightIntensity * k; }
        if (beacons != null)
        {
            block ??= new MaterialPropertyBlock();
            foreach (var r in beacons)
            {
                if (!r) continue;
                r.GetPropertyBlock(block);
                block.SetColor(EmissionId, new Color(1f, 0.05f, 0.03f) * (0.2f + 4f * k));
                r.SetPropertyBlock(block);
            }
        }
    }
}
