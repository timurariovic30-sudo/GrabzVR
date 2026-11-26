using UnityEngine;

/// <summary>
/// Косметика на модели игрока: дочерние объекты с именами id (Crown, RobberMask, GoldChain).
/// Включены все надетые предметы из GameSession.Cosmetic (список через запятую, по одному на слот:
/// головной убор + маска + цепь носятся одновременно). Сохраняется между сценами.
/// </summary>
public class PlayerCosmetics : MonoBehaviour
{
    public GameObject[] items;
    [Tooltip("Горилла другого игрока: косметика приходит по сети, а не из своего выбора")]
    public bool useNetworkId;
    [Tooltip("Надетые предметы другого игрока, id через запятую")]
    public string networkId = "";

    void Start() => Apply();

    public void Apply()
    {
        var worn = GameSession.ParseCosmetics(useNetworkId ? networkId : GameSession.Cosmetic);
        if (items == null) return;
        foreach (var it in items) if (it) it.SetActive(System.Array.IndexOf(worn, it.name) >= 0);
    }
}
