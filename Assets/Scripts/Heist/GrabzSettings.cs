using UnityEngine;

/// <summary>
/// Настройки игрока, которые меняются на ретро-компьютере GRABZ OS (хранятся в PlayerPrefs).
/// </summary>
public static class GrabzSettings
{
    public enum TurnMode { Snap, Smooth, None }
    public enum MicMode { AllChat, PushToTalk, PushToMute }

    public static readonly string[] TurnNames = { "SNAP", "SMOOTH", "NONE" };
    public static readonly string[] MicNames = { "ALL CHAT", "PUSH TO TALK", "PUSH TO MUTE" };
    public static readonly string[] QueueNames = { "DEFAULT", "MINIGAMES", "COMPETITIVE" };
    public static readonly string[] VoiceNames = { "HUMAN", "MONKE" };

    const string P = "GrabzOS.";

    public static string PlayerName
    {
        get => PlayerPrefs.GetString(P + "Name", "GORILLA" + Random.Range(100, 999));
        set { PlayerPrefs.SetString(P + "Name", value); PlayerPrefs.Save(); }
    }
    public static string RoomCode
    {
        get => PlayerPrefs.GetString(P + "Room", "");
        set { PlayerPrefs.SetString(P + "Room", value); PlayerPrefs.Save(); }
    }
    public static TurnMode Turn
    {
        get => (TurnMode)Mathf.Clamp(PlayerPrefs.GetInt(P + "Turn", 0), 0, 2);
        set { PlayerPrefs.SetInt(P + "Turn", (int)value); PlayerPrefs.Save(); }
    }
    /// 0…9 (как в Gorilla Tag)
    public static int TurnSpeed
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(P + "TurnSpeed", 5), 0, 9);
        set { PlayerPrefs.SetInt(P + "TurnSpeed", Mathf.Clamp(value, 0, 9)); PlayerPrefs.Save(); }
    }
    public static MicMode Mic
    {
        get => (MicMode)Mathf.Clamp(PlayerPrefs.GetInt(P + "Mic", 0), 0, 2);
        set { PlayerPrefs.SetInt(P + "Mic", (int)value); PlayerPrefs.Save(); }
    }
    public static int Queue
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(P + "Queue", 0), 0, QueueNames.Length - 1);
        set { PlayerPrefs.SetInt(P + "Queue", value); PlayerPrefs.Save(); }
    }
    public static int Voice
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(P + "Voice", 1), 0, VoiceNames.Length - 1);
        set { PlayerPrefs.SetInt(P + "Voice", value); PlayerPrefs.Save(); }
    }
}
