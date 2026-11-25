using UnityEngine;

/// <summary>
/// Данные, которые переживают переход Хаб → Уровень: выбранный режим игры и примеренная косметика.
/// Хранятся в PlayerPrefs (цвет шерсти хранит PlayerSkinController).
/// </summary>
public static class GameSession
{
    public enum Mode { Classic = 0, PoliceChase = 1, Hardcore = 2, ParkourTraining = 3 }

    public const string HubScene = "MainHub";
    public const string HeistScene = "HeistLevel_Test";
    const string ModeKey = "Heist.Mode";
    const string CosmeticKey = "Heist.Cosmetic";

    public static readonly string[] ModeTitles =
    {
        "КЛАССИЧЕСКОЕ ОГРАБЛЕНИЕ (БАНК)",
        "ПОБЕГ ОТ ПОЛИЦИИ (СКОРО / ТЕСТ)",
        "ХАРДКОР: ОДНА ЖИЗНЬ",
        "ТРЕНИРОВКА ПАРКУРА",
    };

    public static readonly string[] ModeHints =
    {
        "3 минуты, цель $4000, тревога −15 сек",
        "тестовый режим: 2.5 минуты, тревога −30 сек",
        "одна сработка тревоги — и ты пойман!",
        "без таймера и без штрафов — лазай сколько хочешь",
    };

    /// true, если игрок приехал на уровень на фургоне из Хаба (уровень сам стартует ограбление).
    public static bool ArrivedFromHub;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => ArrivedFromHub = false;

    public static Mode CurrentMode
    {
        get => (Mode)Mathf.Clamp(PlayerPrefs.GetInt(ModeKey, 0), 0, ModeTitles.Length - 1);
        set { PlayerPrefs.SetInt(ModeKey, (int)value); PlayerPrefs.Save(); }
    }

    public static string ModeTitle => ModeTitles[(int)CurrentMode];

    /// Примеренная косметика ("" = ничего).
    public static string Cosmetic
    {
        get => PlayerPrefs.GetString(CosmeticKey, "");
        set { PlayerPrefs.SetString(CosmeticKey, value ?? ""); PlayerPrefs.Save(); }
    }
}
