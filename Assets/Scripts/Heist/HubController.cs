using TMPro;
using UnityEngine;

/// <summary>
/// Главный Хаб: выбор режима на столе планирования (табло над фургоном показывает выбранный режим)
/// и примерка пробной косметики на витрине. Методы вызываются физическими кнопками (PushButton.onPressed).
/// </summary>
public class HubController : MonoBehaviour
{
    [Header("Табло над фургоном")]
    public TextMeshPro modeBoardTitle;
    public TextMeshPro modeBoardHint;

    [Header("Лампочки у кнопок режимов (по порядку режимов)")]
    public Renderer[] modeLamps;
    public Color lampOn = new Color(0.3f, 1f, 0.3f);
    public Color lampOff = new Color(0.08f, 0.1f, 0.08f);

    [Header("Витрина косметики")]
    public TextMeshPro cosmeticStatus;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    MaterialPropertyBlock block;

    void Start()
    {
        ShowMode();
        ShowCosmetic();
    }

    // --------------------------------------------------------------- режимы
    public void SelectClassic() => Select(GameSession.Mode.Classic);
    public void SelectPoliceChase() => Select(GameSession.Mode.PoliceChase);
    public void SelectHardcore() => Select(GameSession.Mode.Hardcore);
    public void SelectParkour() => Select(GameSession.Mode.ParkourTraining);

    public void Select(GameSession.Mode mode)
    {
        GameSession.CurrentMode = mode;
        ShowMode();
    }

    void ShowMode()
    {
        int m = (int)GameSession.CurrentMode;
        if (modeBoardTitle) modeBoardTitle.text = "РЕЖИМ: " + GameSession.ModeTitles[m];
        if (modeBoardHint) modeBoardHint.text = GameSession.ModeHints[m] + "\nЗАЙДИ В АВТОБУС, ЧТОБЫ ПОЕХАТЬ НА ДЕЛО!";
        if (modeLamps == null) return;
        block ??= new MaterialPropertyBlock();
        for (int i = 0; i < modeLamps.Length; i++)
        {
            if (!modeLamps[i]) continue;
            var c = i == m ? lampOn : lampOff;
            modeLamps[i].GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            block.SetColor(EmissionId, i == m ? c * 3f : Color.black);
            modeLamps[i].SetPropertyBlock(block);
        }
    }

    // ------------------------------------------------------------ косметика
    public void TryOnCrown() => TryOn("Crown");
    public void TryOnRobberMask() => TryOn("RobberMask");
    public void TryOnGoldChain() => TryOn("GoldChain");

    /// Повторное нажатие той же кнопки снимает предмет.
    public void TryOn(string id)
    {
        GameSession.Cosmetic = GameSession.Cosmetic == id ? "" : id;
        foreach (var pc in FindObjectsByType<PlayerCosmetics>(FindObjectsInactive.Include)) pc.Apply();
        ShowCosmetic();
    }

    void ShowCosmetic()
    {
        if (!cosmeticStatus) return;
        string c = GameSession.Cosmetic;
        cosmeticStatus.text = c switch
        {
            "Crown" => "НА ТЕБЕ: КОРОНА",
            "RobberMask" => "НА ТЕБЕ: МАСКА ГРАБИТЕЛЯ",
            "GoldChain" => "НА ТЕБЕ: ЗОЛОТАЯ ЦЕПЬ",
            _ => "ПРИМЕРЬ! (ПОСМОТРИ В ЗЕРКАЛО)",
        };
    }
}
