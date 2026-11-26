using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Ретро-компьютер GRABZ OS в Главном Хабе (в духе компьютера из Gorilla Tag).
/// Слева — вкладки (UP/DOWN), по центру — содержимое выбранной вкладки.
///  ROOM  — ввод кода комнаты (буквы/цифры, DELETE, ENTER);
///  NAME  — ввод ника (ENTER сохраняет);
///  COLOR — цвет шерсти: OPTION 1/2/3 выбирают R/G/B, цифры 0–9 задают значение (применяется сразу);
///  TURN  — поворот стиком: OPTION 1 SNAP, 2 SMOOTH, 3 NONE, цифры — скорость;
///  MIC   — OPTION 1 ALL CHAT, 2 PUSH TO TALK, 3 PUSH TO MUTE;
///  QUEUE, GROUP, VOICE, ITEMS (косметика), CREDITS.
/// В ПК-режиме можно печатать и с настоящей клавиатуры, стоя рядом.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class GrabzComputer : MonoBehaviour
{
    public static readonly string[] Tabs = { "ROOM", "NAME", "COLOR", "TURN", "MIC", "QUEUE", "GROUP", "VOICE", "ITEMS", "CREDITS" };
    static readonly string[] ItemIds = { "Crown", "RobberMask", "GoldChain" };
    static readonly string[] ItemNames = { "КОРОНА", "МАСКА ГРАБИТЕЛЯ", "ЗОЛОТАЯ ЦЕПЬ" };

    public TextMeshPro tabsText;
    public TextMeshPro screenText;
    [Tooltip("Минимальная пауза между нажатиями одной рукой (защита от двойных нажатий соседних клавиш)")]
    public float handCooldown = 0.14f;
    [Tooltip("Пауза перед повторным нажатием той же клавиши")]
    public float keyCooldown = 0.22f;
    [Tooltip("ПК-режим: с какого расстояния принимать ввод с настоящей клавиатуры")]
    public float desktopTypingRange = 3f;

    int tab;
    string roomInput = "", nameInput = "";
    int colorChannel;            // 0 R, 1 G, 2 B
    int[] rgb = { 4, 6, 2 };
    string status = "";
    float statusUntil;

    AudioSource source;
    float lastLeft = -1f, lastRight = -1f, lastMouse = -1f;
    readonly System.Collections.Generic.Dictionary<ComputerKey, float> lastKey = new System.Collections.Generic.Dictionary<ComputerKey, float>();

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.maxDistance = 12f;
        if (!PlayerPrefs.HasKey("GrabzOS.Name")) GrabzSettings.PlayerName = "GORILLA" + Random.Range(100, 999);
        nameInput = GrabzSettings.PlayerName;
        roomInput = GrabzSettings.RoomCode;
        Color c = PlayerSkinController.TryLoadColor(out var saved) ? saved : new Color(0.45f, 0.65f, 0.25f);
        rgb = new[] { Mathf.RoundToInt(c.r * 9), Mathf.RoundToInt(c.g * 9), Mathf.RoundToInt(c.b * 9) };
    }

    void Start() => Refresh();

    // ------------------------------------------------------------------ ввод
    /// left: true/false — VR-рука, null — мышь.
    public bool TryPressFromHand(ComputerKey k, bool? left)
    {
        float now = Time.time;
        ref float handT = ref lastMouse;
        if (left == true) handT = ref lastLeft; else if (left == false) handT = ref lastRight;
        if (now - handT < handCooldown) return false;
        if (lastKey.TryGetValue(k, out float kt) && now - kt < keyCooldown) return false;
        handT = now;
        lastKey[k] = now;
        k.Animate();
        source.pitch = Random.Range(0.92f, 1.08f);
        source.PlayOneShot(ProceduralAudio.KeyClick(), 0.9f);
        HandleKey(k.key);
        return true;
    }

    void Update()
    {
        // ПК-режим: печать с настоящей клавиатуры рядом с компьютером
        var kb = Keyboard.current;
        var cam = Camera.main;
        if (kb == null || cam == null || UnityEngine.XR.XRSettings.isDeviceActive) return;
        if ((cam.transform.position - transform.position).sqrMagnitude > desktopTypingRange * desktopTypingRange) return;
        for (Key k = Key.A; k <= Key.Z; k++) if (kb[k].wasPressedThisFrame) HandleKey(k.ToString());
        Key[] digits = { Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
        for (int d = 0; d <= 9; d++) if (kb[digits[d]].wasPressedThisFrame || kb[Key.Numpad0 + d].wasPressedThisFrame) HandleKey(d.ToString());
        if (kb.upArrowKey.wasPressedThisFrame) HandleKey("UP");
        if (kb.downArrowKey.wasPressedThisFrame) HandleKey("DOWN");
        if (kb.backspaceKey.wasPressedThisFrame || kb.deleteKey.wasPressedThisFrame) HandleKey("DELETE");
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) HandleKey("ENTER");
        if (kb.spaceKey.wasPressedThisFrame) HandleKey("SPACE");
        if (kb.f5Key.wasPressedThisFrame) HandleKey("OPTION 1");
        if (kb.f6Key.wasPressedThisFrame) HandleKey("OPTION 2");
        if (kb.f7Key.wasPressedThisFrame) HandleKey("OPTION 3");
    }

    public void HandleKey(string key)
    {
        switch (key)
        {
            case "UP": tab = (tab + Tabs.Length - 1) % Tabs.Length; Refresh(); return;
            case "DOWN": tab = (tab + 1) % Tabs.Length; Refresh(); return;
        }
        bool isDigit = key.Length == 1 && char.IsDigit(key[0]);
        bool isLetter = key.Length == 1 && char.IsLetter(key[0]);
        int opt = key.StartsWith("OPTION") ? key[key.Length - 1] - '0' : 0;

        switch (Tabs[tab])
        {
            case "ROOM":
                if ((isDigit || isLetter) && roomInput.Length < 10) roomInput += key;
                else if (key == "DELETE" && roomInput.Length > 0) roomInput = roomInput.Substring(0, roomInput.Length - 1);
                else if (key == "ENTER")
                {
                    GrabzSettings.RoomCode = roomInput;
                    string room = roomInput.Length == 0 ? FusionNetworkManager.DefaultRoom : roomInput;
                    if (FusionNetworkManager.Instance) { FusionNetworkManager.Instance.Reconnect(room); Status($"ПОДКЛЮЧАЮСЬ К КОМНАТЕ {room}..."); }
                    else Status($"КОМНАТА {room} СОХРАНЕНА (СЕТИ В ЭТОЙ СЦЕНЕ НЕТ)");
                }
                break;
            case "NAME":
                if ((isDigit || isLetter) && nameInput.Length < 12) nameInput += key;
                else if (key == "SPACE" && nameInput.Length < 12) nameInput += "_";
                else if (key == "DELETE" && nameInput.Length > 0) nameInput = nameInput.Substring(0, nameInput.Length - 1);
                else if (key == "ENTER" && nameInput.Length > 0) { GrabzSettings.PlayerName = nameInput; Status("НИК СОХРАНЁН: " + nameInput); }
                break;
            case "COLOR":
                if (opt >= 1 && opt <= 3) colorChannel = opt - 1;
                else if (isDigit) { rgb[colorChannel] = key[0] - '0'; ApplyColor(false); }
                else if (key == "ENTER") { ApplyColor(true); Status("ЦВЕТ СОХРАНЁН"); }
                break;
            case "TURN":
                if (opt >= 1 && opt <= 3) GrabzSettings.Turn = (GrabzSettings.TurnMode)(opt - 1);
                else if (isDigit) GrabzSettings.TurnSpeed = key[0] - '0';
                break;
            case "MIC":
                if (opt >= 1 && opt <= 3) GrabzSettings.Mic = (GrabzSettings.MicMode)(opt - 1);
                break;
            case "QUEUE":
                if (opt >= 1 && opt <= 3) GrabzSettings.Queue = opt - 1;
                break;
            case "GROUP":
                if (key == "ENTER") Status("ГРУППА = ОДНА КОМНАТА: ВВЕДИТЕ ОДИН КОД ВО ВКЛАДКЕ ROOM.\nВ АВТОБУСЕ ЕДЕТ ВСЯ КОМНАТА.");
                break;
            case "VOICE":
                if (opt == 1 || opt == 2) GrabzSettings.Voice = opt - 1;
                break;
            case "ITEMS":
                if (opt >= 1 && opt <= 3) ToggleItem(ItemIds[opt - 1]);
                break;
        }
        Refresh();
    }

    void ApplyColor(bool save)
    {
        var c = new Color(rgb[0] / 9f, rgb[1] / 9f, rgb[2] / 9f);
        foreach (var s in FindObjectsByType<PlayerSkinController>(FindObjectsInactive.Include)) s.SetFurColor(c, save);
        if (save) PlayerSkinController.SaveColor(c);
    }

    void ToggleItem(string id)
    {
        GameSession.ToggleCosmetic(id);   // корона и маска — разные слоты, носятся вместе
        foreach (var pc in FindObjectsByType<PlayerCosmetics>(FindObjectsInactive.Include)) pc.Apply();
        var hub = FindAnyObjectByType<HubController>();
        if (hub && hub.cosmeticStatus) hub.cosmeticStatus.text = HubController.WornSummary("НИЧЕГО НЕ НАДЕТО");
    }

    void Status(string s) { status = s; statusUntil = Time.time + 4f; }

    // ------------------------------------------------------------------ экран
    void LateUpdate()
    {
        if (status != "" && Time.time > statusUntil) { status = ""; Refresh(); }
        // статус сети (ONLINE / ROOM / PLAYERS) обновляется раз в секунду
        if (Time.unscaledTime > nextNetRefresh)
        {
            nextNetRefresh = Time.unscaledTime + 1f;
            string net = FusionNetworkManager.StatusLine;
            if (net != lastNet) { lastNet = net; Refresh(); }
        }
    }

    float nextNetRefresh;
    string lastNet = "";

    public void Refresh()
    {
        if (tabsText)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Tabs.Length; i++)
                sb.AppendLine(i == tab ? $"<color=#FFFFFF><b>> {Tabs[i]}</b></color>" : $"<color=#9CE89C>  {Tabs[i]}</color>");
            tabsText.text = sb.ToString();
        }
        if (!screenText) return;
        var s = new StringBuilder();
        s.AppendLine("<b>GRABZ OS v1.0</b>");
        s.AppendLine($"<size=80%><color=#B6FFB6>{FusionNetworkManager.StatusLine}</color></size>");
        s.AppendLine($"<size=80%>НИК: {GrabzSettings.PlayerName}   {PlayFabAuthManager.Status}</size>");
        s.AppendLine("<size=60%>------------------------------------------</size>");
        switch (Tabs[tab])
        {
            case "ROOM":
                s.AppendLine("КОД КОМНАТЫ:");
                s.AppendLine($"<b>{roomInput}_</b>");
                s.AppendLine("<size=70%>БУКВЫ/ЦИФРЫ — ВВОД, DELETE — СТЕРЕТЬ, ENTER — ВОЙТИ</size>");
                break;
            case "NAME":
                s.AppendLine("ТВОЙ НИК:");
                s.AppendLine($"<b>{nameInput}_</b>");
                s.AppendLine("<size=70%>ENTER — СОХРАНИТЬ (ДО 12 СИМВОЛОВ)</size>");
                break;
            case "COLOR":
                string[] ch = { "R", "G", "B" };
                for (int i = 0; i < 3; i++)
                    s.AppendLine((i == colorChannel ? "> " : "  ") + $"{ch[i]}: <b>{rgb[i]}</b>  " + new string('#', rgb[i]) + new string('.', 9 - rgb[i]));
                var c = new Color(rgb[0] / 9f, rgb[1] / 9f, rgb[2] / 9f);
                s.AppendLine($"<color=#{ColorUtility.ToHtmlStringRGB(c)}><b>#######</b></color>  <size=70%>ОБРАЗЕЦ</size>");
                s.AppendLine("<size=70%>OPTION 1/2/3 — R/G/B, ЦИФРЫ 0–9 — ЗНАЧЕНИЕ, ENTER — СОХРАНИТЬ</size>");
                break;
            case "TURN":
                for (int i = 0; i < 3; i++)
                    s.AppendLine(((int)GrabzSettings.Turn == i ? "> " : "  ") + $"OPTION {i + 1}: {GrabzSettings.TurnNames[i]}");
                s.AppendLine($"СКОРОСТЬ: <b>{GrabzSettings.TurnSpeed}</b>  <size=70%>(ЦИФРЫ 0–9)</size>");
                s.AppendLine("<size=70%>ПОВОРОТ — ПРАВЫЙ СТИК</size>");
                break;
            case "MIC":
                for (int i = 0; i < 3; i++)
                    s.AppendLine(((int)GrabzSettings.Mic == i ? "> " : "  ") + $"OPTION {i + 1}: {GrabzSettings.MicNames[i]}");
                s.AppendLine("<size=70%>КНОПКА РАЦИИ: A / X НА КОНТРОЛЛЕРЕ ИЛИ V НА КЛАВИАТУРЕ</size>");
                break;
            case "QUEUE":
                for (int i = 0; i < 3; i++)
                    s.AppendLine((GrabzSettings.Queue == i ? "> " : "  ") + $"OPTION {i + 1}: {GrabzSettings.QueueNames[i]}");
                break;
            case "GROUP":
                s.AppendLine("СОБРАТЬ ДРУЗЕЙ В ОДНУ КОМНАТУ");
                s.AppendLine("<size=70%>ENTER — СОБРАТЬ ГРУППУ</size>");
                break;
            case "VOICE":
                for (int i = 0; i < 2; i++)
                    s.AppendLine((GrabzSettings.Voice == i ? "> " : "  ") + $"OPTION {i + 1}: {GrabzSettings.VoiceNames[i]}");
                break;
            case "ITEMS":
                for (int i = 0; i < 3; i++)
                    s.AppendLine((GameSession.IsWorn(ItemIds[i]) ? "> " : "  ") + $"OPTION {i + 1}: {ItemNames[i]}" + (GameSession.IsWorn(ItemIds[i]) ? "  [НАДЕТО]" : ""));
                s.AppendLine("<size=70%>КОРОНА, МАСКА И ЦЕПЬ НАДЕВАЮТСЯ ВМЕСТЕ</size>");
                break;
            case "CREDITS":
                s.AppendLine("GRABZ VR — КООП-ОГРАБЛЕНИЕ ОБЕЗЬЯН");
                s.AppendLine("<size=80%>ДВИЖЕНИЕ: GORILLA LOCOMOTION\n3D-МОДЕЛИ: TRIPO AI\nСДЕЛАНО С ЛЮБОВЬЮ И БАНАНАМИ</size>");
                break;
        }
        if (status != "") s.AppendLine($"\n<color=#FFFF66>{status}</color>");
        screenText.text = s.ToString();
    }
}
