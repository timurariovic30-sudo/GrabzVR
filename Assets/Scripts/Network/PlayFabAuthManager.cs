using System.Collections.Generic;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

/// <summary>
/// Тихий вход в PlayFab (тайтл 1FEC73) без паролей: LoginWithCustomID по уникальному ID устройства,
/// аккаунт создаётся автоматически. После входа:
///  • загружает с сервера сохранённый цвет шерсти (UserData "FurColor") и применяет его;
///  • сохраняет цвет на сервер при каждом изменении (PlayerSkinController.ColorChanged).
/// Живёт между сценами (DontDestroyOnLoad), создаётся один раз в MainHub.
/// </summary>
public class PlayFabAuthManager : MonoBehaviour
{
    public const string FurColorKey = "FurColor";
    [Tooltip("PlayFab Title ID (также записан в PlayFabSharedSettings)")]
    public string titleId = "1FEC73";

    public static PlayFabAuthManager Instance { get; private set; }
    public static bool IsLoggedIn { get; private set; }
    public static string PlayFabId { get; private set; }
    public static string Status { get; private set; } = "PLAYFAB: ...";

    string lastSaved;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (string.IsNullOrEmpty(PlayFabSettings.staticSettings.TitleId)) PlayFabSettings.staticSettings.TitleId = titleId;
    }

    void OnEnable() => PlayerSkinController.ColorChanged += OnColorChanged;
    void OnDisable() => PlayerSkinController.ColorChanged -= OnColorChanged;

    void Start() => Login();

    public static string DeviceId()
    {
        string id = SystemInfo.deviceUniqueIdentifier;
        if (string.IsNullOrEmpty(id) || id == SystemInfo.unsupportedIdentifier)
        {
            // запасной вариант: свой GUID, сохранённый на устройстве
            if (!PlayerPrefs.HasKey("GrabzOS.DeviceId")) { PlayerPrefs.SetString("GrabzOS.DeviceId", System.Guid.NewGuid().ToString("N")); PlayerPrefs.Save(); }
            id = PlayerPrefs.GetString("GrabzOS.DeviceId");
        }
        return id;
    }

    public void Login()
    {
        Status = "PLAYFAB: ВХОД...";
        var req = new LoginWithCustomIDRequest
        {
            CustomId = DeviceId(),
            CreateAccount = true,
            InfoRequestParameters = new GetPlayerCombinedInfoRequestParams { GetUserData = true, UserDataKeys = new List<string> { FurColorKey } },
        };
        PlayFabClientAPI.LoginWithCustomID(req, OnLogin, e =>
        {
            IsLoggedIn = false;
            Status = "PLAYFAB: OFFLINE";
            Debug.LogWarning("[PlayFab] login failed: " + e.GenerateErrorReport());
        });
    }

    void OnLogin(LoginResult r)
    {
        IsLoggedIn = true;
        PlayFabId = r.PlayFabId;
        Status = r.NewlyCreated ? "PLAYFAB: НОВЫЙ ИГРОК" : "PLAYFAB: ONLINE";
        Debug.Log($"[PlayFab] logged in as {r.PlayFabId} (new={r.NewlyCreated})");

        // имя игрока = ник из GRABZ OS
        PlayFabClientAPI.UpdateUserTitleDisplayName(new UpdateUserTitleDisplayNameRequest { DisplayName = SafeName(GrabzSettings.PlayerName) },
            _ => { }, e => Debug.Log("[PlayFab] display name: " + e.ErrorMessage));

        var data = r.InfoResultPayload?.UserData;
        if (data != null && data.TryGetValue(FurColorKey, out var rec) && ColorUtility.TryParseHtmlString(rec.Value, out var c))
        {
            lastSaved = rec.Value;
            PlayerSkinController.SaveColor(c);
            foreach (var s in FindObjectsByType<PlayerSkinController>(FindObjectsInactive.Include)) s.SetFurColor(c, false);
            Debug.Log("[PlayFab] fur color loaded from server: " + rec.Value);
        }
        else if (PlayerSkinController.TryLoadColor(out var local)) SaveFurColor(local);
    }

    void OnColorChanged(Color c) => SaveFurColor(c);

    public void SaveFurColor(Color c)
    {
        string v = "#" + ColorUtility.ToHtmlStringRGB(c);
        if (!IsLoggedIn || v == lastSaved) return;
        lastSaved = v;
        PlayFabClientAPI.UpdateUserData(new UpdateUserDataRequest { Data = new Dictionary<string, string> { { FurColorKey, v } } },
            _ => Debug.Log("[PlayFab] fur color saved: " + v),
            e => { lastSaved = null; Debug.LogWarning("[PlayFab] save failed: " + e.ErrorMessage); });
    }

    static string SafeName(string n)
    {
        n = string.IsNullOrEmpty(n) ? "GORILLA" : n;
        if (n.Length < 3) n = n.PadRight(3, '_');
        return n.Length > 25 ? n.Substring(0, 25) : n;
    }
}
