using UnityEngine;

/// <summary>
/// Тестовая кнопка «СБРОСИТЬ ВЕСЬ ЛУТ»: возвращает весь лут на исходные места (включая сданный и разбитый),
/// чинит витрины и обнуляет счёт.
/// Лут больше не пересоздаётся из префабов: каждый LootItem помнит своё место и просто возвращается туда —
/// так сцены остаются сетевыми объектами Fusion (в сети сброс делает хост через HeistNetworkState у всех игроков).
/// </summary>
public class LootRegistry : MonoBehaviour
{
    [System.Serializable]
    public struct Entry { public string id; public GameObject prefab; }

    [Tooltip("Справочник префабов лута по lootId (для билдеров; для сброса не нужен)")]
    public Entry[] prefabs;

    void Start()
    {
        int n = FindObjectsByType<LootItem>(FindObjectsInactive.Exclude).Length;
        Debug.Log($"[LootRegistry] Лута на карте: {n}");
    }

    public void ResetAllLoot()
    {
        var net = HeistNetworkState.Instance;
        if (net != null)
        {
            net.RequestResetLoot();
            return;
        }
        foreach (var item in FindObjectsByType<LootItem>(FindObjectsInactive.Include)) item.ResetToSpawn();
        foreach (var g in BreakableGlass.All.ToArray()) g.ResetGlass();
        if (HeistGameManager.Instance != null) HeistGameManager.Instance.ResetScore();
        Debug.Log("[LootRegistry] Весь лут возвращён на места");
    }
}
