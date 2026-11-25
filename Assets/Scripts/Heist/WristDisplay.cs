using TMPro;
using UnityEngine;

/// <summary>
/// «Часы» на левом запястье VR-игрока: World Space Canvas, поэтому в шлеме текст не двоится
/// и не уезжает за край экрана (в отличие от Screen Space / OnGUI).
/// В хабе: выбранный режим. На уровне: таймер, добыча, последнее событие.
/// Canvas создаётся билдером (Tools/Heist/Player/Build Gorilla Final Avatar) на «Left Controller».
/// </summary>
public class WristDisplay : MonoBehaviour
{
    public TextMeshProUGUI text;
    [Tooltip("Сколько секунд показывать последнее событие")]
    public float eventShowTime = 6f;

    float nextRefresh;

    void Update()
    {
        if (!text || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;
        text.text = Compose();
    }

    string Compose()
    {
        var gm = HeistGameManager.Instance;
        string mode = GameSession.ModeTitles[(int)GameSession.CurrentMode];
        if (gm == null) return $"<size=70%>ХАБ · РЕЖИМ:</size>\n<b>{mode}</b>";

        string time;
        if (gm.Mode == GameSession.Mode.ParkourTraining) time = "БЕЗ ТАЙМЕРА";
        else
        {
            int t = Mathf.Max(0, Mathf.CeilToInt(gm.TimeLeft));
            time = $"{t / 60}:{t % 60:00}";
        }
        string state = gm.State switch
        {
            HeistGameManager.RoundState.Lobby => "ГОТОВЬСЯ",
            HeistGameManager.RoundState.Won => "ПОБЕДА!",
            HeistGameManager.RoundState.Lost => "ПРОВАЛ",
            _ => time,
        };
        string s = $"<b>{state}</b>   <color=#FFD54A>${gm.Loot}</color><size=70%>/{gm.targetLoot}</size>";
        if (!string.IsNullOrEmpty(gm.LastEvent) && Time.time - gm.LastEventTime < eventShowTime)
            s += $"\n<size=65%>{gm.LastEvent}</size>";
        return s;
    }
}
