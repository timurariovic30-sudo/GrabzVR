using UnityEngine;

/// <summary>
/// Косметика на модели игрока: дочерние объекты с именами id (Crown, RobberMask, GoldChain).
/// Включён только тот, что выбран в GameSession.Cosmetic (сохраняется между сценами).
/// </summary>
public class PlayerCosmetics : MonoBehaviour
{
    public GameObject[] items;
    [Tooltip("Горилла другого игрока: косметика приходит по сети, а не из своего выбора")]
    public bool useNetworkId;
    public string networkId = "";

    void Start() => Apply();

    public void Apply()
    {
        string id = useNetworkId ? networkId : GameSession.Cosmetic;
        if (items == null) return;
        foreach (var it in items) if (it) it.SetActive(it.name == id);
    }
}
