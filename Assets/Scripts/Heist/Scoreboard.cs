using TMPro;
using UnityEngine;

/// <summary>3D-табло: счёт, таймер и подсказка/событие. Таких табло на карте несколько (Убежище, Холл, Гараж).</summary>
public class Scoreboard : MonoBehaviour
{
    public TextMeshPro lootText;
    public TextMeshPro timerText;
    public TextMeshPro hintText;

    public string lobbyHint = "ЖМИ «СТАРТ» НА ПУЛЬТЕ";
    public string playingHint = "НЕСИ ЛУТ В ФУРГОН В ГАРАЖЕ!";
    public string winText = "ПОБЕДА! ОГРАБЛЕНИЕ УДАЛОСЬ";
    public string loseText = "ВРЕМЯ ВЫШЛО! ПОЛИЦИЯ ПРИЕХАЛА";

    static readonly Color Gold = new Color(1f, 0.8f, 0.2f);
    static readonly Color Neon = new Color(0.3f, 1f, 0.2f);
    static readonly Color Red = new Color(1f, 0.15f, 0.1f);

    void Update()
    {
        var gm = HeistGameManager.Instance;
        if (gm == null) return;

        if (lootText)
        {
            lootText.text = $"LOOT: ${gm.Loot} / ${gm.targetLoot}";
            lootText.color = gm.Loot >= gm.targetLoot ? Neon : Gold;
        }
        if (timerText)
        {
            int t = Mathf.CeilToInt(Mathf.Max(0f, gm.TimeLeft));
            timerText.text = $"ВРЕМЯ: {t / 60}:{t % 60:00}";
            bool alarm = AlarmSystem.Instance != null && AlarmSystem.Instance.IsActive;
            bool danger = gm.IsPlaying && (gm.TimeLeft < 30f || alarm);
            timerText.color = danger && Mathf.Repeat(Time.time, 0.6f) < 0.3f ? Red : Color.white;
        }
        if (hintText)
        {
            switch (gm.State)
            {
                case HeistGameManager.RoundState.Won: hintText.text = winText; hintText.color = Neon; break;
                case HeistGameManager.RoundState.Lost: hintText.text = loseText; hintText.color = Red; break;
                default:
                    bool recent = Time.time - gm.LastEventTime < 2.5f && !string.IsNullOrEmpty(gm.LastEvent);
                    hintText.text = recent ? gm.LastEvent : gm.State == HeistGameManager.RoundState.Lobby ? lobbyHint : playingHint;
                    hintText.color = recent ? (gm.LastEvent.StartsWith("+") ? Gold : gm.LastEvent.StartsWith("ТРЕВОГА") ? Red : Neon) : Neon;
                    break;
            }
        }
    }
}
