using System.Threading.Tasks;
using Fusion;
using Photon.Voice.Unity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Сеть GRABZ: Photon Fusion 2 (Shared) + Photon Voice 2.
///  • при старте сам заходит в комнату "GRABZ_TEST" (или в код из GRABZ OS → ROOM) и включает микрофон;
///  • спавнит свою сетевую гориллу (NetworkGorillaAvatar) — работает и для VR-, и для ПК-игрока;
///  • GRABZ OS → ROOM + ENTER: переподключение в комнату с этим кодом;
///  • GRABZ OS → MIC: ALL CHAT (микрофон всегда), PUSH TO TALK (говорить, пока зажата A/X или V),
///    PUSH TO MUTE (молчать, пока зажата);
///  • смена сцен общая (NetworkSceneManagerDefault): фургон в хабе везёт всех в сессии на ограбление.
/// Живёт между сценами; стоит в MainHub и HeistLevel_Test (лишний экземпляр сам удаляется).
/// </summary>
public class FusionNetworkManager : MonoBehaviour
{
    public const string DefaultRoom = "GRABZ_TEST";

    [Tooltip("Префаб раннера: NetworkRunner + NetworkSceneManagerDefault + Recorder + FusionVoiceClient")]
    public GameObject runnerPrefab;
    [Tooltip("Префаб сетевой гориллы (NetworkObject + NetworkGorillaAvatar + VoiceNetworkObject)")]
    public NetworkObject avatarPrefab;
    public bool autoConnect = true;
    public int maxPlayers = 10;

    public static FusionNetworkManager Instance { get; private set; }
    public NetworkRunner Runner { get; private set; }
    public string Room { get; private set; } = "";
    public bool Connecting { get; private set; }
    public string LastError { get; private set; } = "";

    Recorder recorder;
    /// Свой микрофон (Photon Voice Recorder) — по его громкости хлопает рот своей гориллы в зеркале.
    public Recorder VoiceRecorder => recorder;
    InputAction pttLeft, pttRight;
    float spawnRetryAt;
    int lastLoadRequest = -1; float lastLoadTime = -10f;

    public static string RoomFromSettings() =>
        string.IsNullOrEmpty(GrabzSettings.RoomCode) ? DefaultRoom : GrabzSettings.RoomCode.ToUpperInvariant();

    /// Строка статуса для монитора GRABZ OS: «ONLINE · ROOM: GRABZ_TEST · PLAYERS: 2».
    public static string StatusLine
    {
        get
        {
            var m = Instance;
            if (m == null) return "OFFLINE";
            if (m.Connecting) return $"CONNECTING... ROOM: {m.Room}";
            if (m.Runner != null && m.Runner.IsRunning)
            {
                int n = 0; foreach (var _ in m.Runner.ActivePlayers) n++;
                return $"ONLINE · ROOM: {m.Room} · PLAYERS: {n}";
            }
            return string.IsNullOrEmpty(m.LastError) ? "OFFLINE" : "OFFLINE (" + m.LastError + ")";
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        pttLeft = new InputAction("PTT_L", InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
        pttRight = new InputAction("PTT_R", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
        pttLeft.Enable(); pttRight.Enable();
    }

    void OnDisable()
    {
        pttLeft?.Dispose(); pttRight?.Dispose();
    }

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
#endif
        if (autoConnect && Instance == this) Connect(RoomFromSettings());
    }

    // ------------------------------------------------------------------ подключение
    public async void Connect(string room)
    {
        if (Connecting) return;
        Connecting = true;
        LastError = "";
        Room = room;
        try
        {
            await Disconnect();
            var go = runnerPrefab ? Instantiate(runnerPrefab) : new GameObject("NetworkRunner", typeof(NetworkRunner), typeof(NetworkSceneManagerDefault));
            go.name = "NetworkRunner";
            DontDestroyOnLoad(go);
            var runner = go.GetComponent<NetworkRunner>();
            runner.ProvideInput = false;
            recorder = go.GetComponentInChildren<Recorder>();
            var sceneManager = go.GetComponent<NetworkSceneManagerDefault>();
            if (!sceneManager) sceneManager = go.AddComponent<NetworkSceneManagerDefault>();
            var provider = go.GetComponent<NetworkObjectProviderDefault>();
            if (!provider) provider = go.AddComponent<NetworkObjectProviderDefault>();
            Runner = runner;

            int buildIndex = SceneManager.GetActiveScene().buildIndex;
            var result = await runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = room,
                PlayerCount = maxPlayers,
                Scene = buildIndex >= 0 ? SceneRef.FromIndex(buildIndex) : default,
                SceneManager = sceneManager,
                ObjectProvider = provider,
            });
            if (!result.Ok)
            {
                LastError = result.ShutdownReason.ToString();
                Debug.LogWarning($"[Fusion] не удалось зайти в {room}: {result.ShutdownReason} {result.ErrorMessage}");
                Runner = null;
            }
            else Debug.Log($"[Fusion] ONLINE, комната {room}");
        }
        catch (System.Exception e)
        {
            LastError = e.GetType().Name;
            Debug.LogException(e);
            Runner = null;
        }
        finally { Connecting = false; }
    }

    public async Task Disconnect()
    {
        if (Runner == null) return;
        var r = Runner;
        Runner = null;
        if (r) await r.Shutdown(true, ShutdownReason.Ok, false);
        if (r) Destroy(r.gameObject);
    }

    /// GRABZ OS → ROOM → ENTER.
    public void Reconnect(string room) => Connect(string.IsNullOrEmpty(room) ? DefaultRoom : room.ToUpperInvariant());

    void OnApplicationQuit() { if (Runner) Runner.Shutdown(); }

    // ------------------------------------------------------------------ каждый кадр
    void Update()
    {
        UpdateMicrophone();
        // своя сетевая горилла (после входа и после каждой смены сцены, если её не стало)
        if (Runner != null && Runner.IsRunning && !Connecting && NetworkGorillaAvatar.Local == null && avatarPrefab
            && Time.unscaledTime > spawnRetryAt && !(Runner.SceneManager is NetworkSceneManagerDefault sm && sm.IsBusy))
        {
            spawnRetryAt = Time.unscaledTime + 2f;
            Runner.Spawn(avatarPrefab, Vector3.zero, Quaternion.identity, Runner.LocalPlayer);
        }
    }

    void UpdateMicrophone()
    {
        if (recorder == null) return;
        bool held = (pttLeft != null && pttLeft.IsPressed()) || (pttRight != null && pttRight.IsPressed())
                    || (Keyboard.current != null && Keyboard.current.vKey.isPressed);
        bool transmit = GrabzSettings.Mic switch
        {
            GrabzSettings.MicMode.PushToTalk => held,
            GrabzSettings.MicMode.PushToMute => !held,
            _ => true,
        };
        if (recorder.TransmitEnabled != transmit) recorder.TransmitEnabled = transmit;
    }

    // ------------------------------------------------------------------ общая смена сцены
    /// Загрузить сцену у всех игроков сессии. false — сети нет, грузите сцену локально как раньше.
    public static bool LoadSceneSynced(string sceneName)
    {
        var m = Instance;
        if (m == null || m.Runner == null || !m.Runner.IsRunning) return false;
        int index = BuildIndexOf(sceneName);
        if (index < 0) return false;
        if (NetworkGorillaAvatar.Local != null)
            NetworkGorillaAvatar.Local.RpcRequestScene(index, (int)GameSession.CurrentMode, GameSession.ArrivedFromHub);
        else m.LoadSceneAsAuthority(index);
        return true;
    }

    /// Сцену в Shared-режиме грузит «хозяин сцены» (мастер-клиент), остальные подтягиваются сами.
    public void LoadSceneAsAuthority(int buildIndex)
    {
        if (Runner == null || !Runner.IsRunning || !Runner.IsSceneAuthority) return;
        if (SceneManager.GetActiveScene().buildIndex == buildIndex) return;
        if (lastLoadRequest == buildIndex && Time.unscaledTime - lastLoadTime < 5f) return; // защита от двойного запроса
        lastLoadRequest = buildIndex; lastLoadTime = Time.unscaledTime;
        Runner.LoadScene(SceneRef.FromIndex(buildIndex), LoadSceneMode.Single, LocalPhysicsMode.None, true);
    }

    /// Отчёт о сети для отладки (раннер, комната, своя горилла, голос).
    public static string DebugInfo()
    {
        var m = Instance;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("status: " + StatusLine);
        if (m != null && m.Runner != null)
        {
            var r = m.Runner;
            sb.AppendLine($"runner: running={r.IsRunning} mode={r.GameMode} session={r.SessionInfo?.Name} region={r.SessionInfo?.Region} local={r.LocalPlayer} sceneAuthority={r.IsSceneAuthority}");
            var vc = r.GetComponent<Photon.Voice.Fusion.FusionVoiceClient>();
            if (vc) sb.AppendLine($"voice: state={vc.ClientState} transmit={(m.recorder ? m.recorder.TransmitEnabled : false)} sending={(m.recorder ? m.recorder.IsCurrentlyTransmitting : false)}");
        }
        var av = NetworkGorillaAvatar.Local;
        sb.AppendLine(av ? $"local avatar: id={av.Object.Id} head={av.HeadPos} fur={av.FurRGB} nick={av.Nick} hidden={!av.GetComponentInChildren<Renderer>(true).enabled}" : "local avatar: none");
        sb.Append("avatars in scene: " + FindObjectsByType<NetworkGorillaAvatar>().Length);
        return sb.ToString();
    }

    static int BuildIndexOf(string sceneName)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            if (System.IO.Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i)) == sceneName) return i;
        return -1;
    }
}
