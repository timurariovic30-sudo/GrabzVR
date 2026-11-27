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

    [Tooltip("Декоративные полосы сверху и снизу текста табло: красятся в цвет режима")]
    public Renderer[] modeBoardBars;

    [Header("Лампочки у кнопок режимов (по порядку режимов)")]
    public Renderer[] modeLamps;
    public Color lampOn = new Color(0.3f, 1f, 0.3f);
    public Color lampOff = new Color(0.08f, 0.1f, 0.08f);

    /// На столе только два режима: зелёная кнопка — классика, красная — хардкор.
    public static Color ModeColor(GameSession.Mode m) => m == GameSession.Mode.Hardcore
        ? new Color(1f, 0.16f, 0.12f) : new Color(0.25f, 1f, 0.35f);

    [Header("Витрина косметики")]
    public TextMeshPro cosmeticStatus;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    MaterialPropertyBlock block;

    void Start()
    {
        // режимы «Побег от полиции» и «Паркур» со стола убраны — старый сохранённый выбор сводим к классике
        if (GameSession.CurrentMode != GameSession.Mode.Hardcore) GameSession.CurrentMode = GameSession.Mode.Classic;
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
        var col = ModeColor(GameSession.CurrentMode);
        if (modeBoardTitle) { modeBoardTitle.text = GameSession.ModeTitles[m]; modeBoardTitle.color = col; }
        if (modeBoardHint) modeBoardHint.text = GameSession.ModeHints[m] + "\nЗАЙДИ В АВТОБУС, ЧТОБЫ ПОЕХАТЬ НА ДЕЛО!";
        block ??= new MaterialPropertyBlock();
        if (modeBoardBars != null)
            foreach (var bar in modeBoardBars)
            {
                if (!bar) continue;
                bar.GetPropertyBlock(block);
                block.SetColor(BaseColorId, col);
                block.SetColor(EmissionId, col * 2.5f);
                bar.SetPropertyBlock(block);
            }
        if (modeLamps == null) return;
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
    public void TryOnRobberMaskWhite() => TryOn("RobberMaskWhite");
    public void TryOnRobberMaskGray() => TryOn("RobberMaskGray");
    public void TryOnGoldChain() => TryOn("GoldChain");

    /// Повторное нажатие той же кнопки снимает предмет. Головной убор, маска и цепь — разные слоты
    /// и носятся вместе; новый предмет снимает только старый из своего слота.
    public void TryOn(string id)
    {
        GameSession.ToggleCosmetic(id);
        foreach (var pc in FindObjectsByType<PlayerCosmetics>(FindObjectsInactive.Include)) pc.Apply();
        ShowCosmetic();
    }

    public static string CosmeticName(string id) => id switch
    {
        "Crown" => "КОРОНА",
        "RobberMask" => "ЧЁРНАЯ БАЛАКЛАВА",
        "RobberMaskWhite" => "БЕЛАЯ БАЛАКЛАВА",
        "RobberMaskGray" => "СЕРАЯ БАЛАКЛАВА",
        "GoldChain" => "ЗОЛОТАЯ ЦЕПЬ",
        _ => id,
    };

    /// «НА ТЕБЕ: КОРОНА + МАСКА ГРАБИТЕЛЯ» или подсказка, если ничего не надето.
    public static string WornSummary(string empty)
    {
        var worn = GameSession.WornCosmetics;
        return worn.Length == 0 ? empty : "НА ТЕБЕ: " + string.Join(" + ", System.Array.ConvertAll(worn, CosmeticName));
    }

    void ShowCosmetic()
    {
        if (cosmeticStatus) cosmeticStatus.text = WornSummary("ПРИМЕРЬ! (ПОСМОТРИ В ЗЕРКАЛО)");
    }
}
