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
    [Tooltip("Если задан — перекрашиваются только слоты с этим материалом (серый живот и морда остаются как есть)")]
    public Material furMaterial;
    public Color furColor = new Color(0.17f, 0.17f, 0.19f);
    [Tooltip("Загружать сохранённый цвет из PlayerPrefs при старте")]
    public bool loadSavedColor = true;
    [Tooltip("Это горилла другого игрока (сеть): её красит только сеть, кнопки хаба и F2 на неё не действуют")]
    public bool isNetworkProxy;

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
        if (loadSavedColor && !isNetworkProxy && TryLoadColor(out var saved)) furColor = saved;
    }

    void OnEnable() => Apply();
    void OnValidate() => Apply();

    void Update()
    {
        if (isNetworkProxy) return;
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
        if (isNetworkProxy) return; // чужую гориллу перекрашивает только сеть
        furColor = color;
        Apply();
        if (save) SaveColor(color);
    }

    /// Цвет гориллы другого игрока, пришедший по сети.
    public void SetNetworkColor(Color color)
    {
        furColor = color;
        Apply();
    }

    /// Срабатывает при каждом сохранении цвета (PlayFab отправляет его на сервер, сеть — другим игрокам).
    public static event System.Action<Color> ColorChanged;

    public static void SaveColor(Color c)
    {
        ColorChanged?.Invoke(c);
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
            if (furMaterial == null)
            {
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, furColor);
                r.SetPropertyBlock(block);
                continue;
            }
            // Модель с несколькими материалами (шерсть + живот/морда): красим только слоты шерсти
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != furMaterial) continue;
                r.GetPropertyBlock(block, i);
                block.SetColor(BaseColorId, furColor);
                r.SetPropertyBlock(block, i);
            }
        }
    }
}
