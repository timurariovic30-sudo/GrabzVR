using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Меняет цвет шерсти обезьяны (скины). Текстура шерсти светлая, поэтому итоговый цвет = цвет скина × текстура.
/// Использует MaterialPropertyBlock: общий материал M_Monkey_Fur не меняется, у каждого игрока свой цвет.
/// Выбранный цвет сохраняется в PlayerPrefs и восстанавливается при следующем запуске.
/// </summary>
public class PlayerSkinController : MonoBehaviour
{
    public const string PrefsKey = "Heist.FurColor";

    [Tooltip("Рендеры с материалом шерсти (тело и голова модели)")]
    public Renderer[] furRenderers;
    public Color furColor = new Color(0.17f, 0.17f, 0.19f);
    [Tooltip("Загружать сохранённый цвет из PlayerPrefs при старте")]
    public bool loadSavedColor = true;

    [Header("Готовые скины (для теста — клавиша ниже циклически их переключает)")]
    public Color[] presets =
    {
        new Color(0.17f, 0.17f, 0.19f), // угольно-серый (по умолчанию)
        new Color(0.45f, 0.29f, 0.17f), // коричневый
        new Color(0.85f, 0.45f, 0.10f), // оранжевый
        new Color(0.25f, 0.55f, 0.95f), // голубой
        new Color(0.60f, 0.25f, 0.75f), // фиолетовый
        new Color(0.95f, 0.95f, 0.92f), // белый
    };
    public Key cycleKey = Key.F2;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    MaterialPropertyBlock block;
    int presetIndex;

    void Awake()
    {
        if (loadSavedColor && TryLoadColor(out var saved)) furColor = saved;
    }

    void OnEnable() => Apply();
    void OnValidate() => Apply();

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && presets.Length > 0 && kb[cycleKey].wasPressedThisFrame)
        {
            presetIndex = (presetIndex + 1) % presets.Length;
            SetFurColor(presets[presetIndex], save: true);
        }
    }

    /// Поменять цвет шерсти (например, при выборе скина в магазине или по сети).
    public void SetFurColor(Color color, bool save = false)
    {
        furColor = color;
        Apply();
        if (save) SaveColor(color);
    }

    public static void SaveColor(Color c)
    {
        PlayerPrefs.SetString(PrefsKey, "#" + ColorUtility.ToHtmlStringRGB(c));
        PlayerPrefs.Save();
    }

    public static bool TryLoadColor(out Color c)
    {
        c = default;
        return PlayerPrefs.HasKey(PrefsKey) && ColorUtility.TryParseHtmlString(PlayerPrefs.GetString(PrefsKey), out c);
    }

    void Apply()
    {
        if (furRenderers == null) return;
        block ??= new MaterialPropertyBlock();
        foreach (var r in furRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, furColor);
            r.SetPropertyBlock(block);
        }
    }
}
