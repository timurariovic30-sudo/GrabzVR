using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Запоминает исходные места всего лута на карте и умеет вернуть всё как было (тестовая кнопка «СБРОСИТЬ ВЕСЬ ЛУТ»):
/// удаляет текущие предметы, создаёт заново из префабов, чинит витрины и обнуляет счёт.
/// </summary>
public class LootRegistry : MonoBehaviour
{
    [System.Serializable]
    public struct Entry { public string id; public GameObject prefab; }

    public Entry[] prefabs;

    struct Spawn { public GameObject prefab; public Transform parent; public Vector3 pos; public Quaternion rot; }
    readonly List<Spawn> spawns = new List<Spawn>();

    void Start()
    {
        foreach (var item in FindObjectsByType<LootItem>(FindObjectsInactive.Exclude))
        {
            var prefab = FindPrefab(item.lootId);
            if (prefab == null) { Debug.LogWarning($"[LootRegistry] Нет префаба для '{item.lootId}'"); continue; }
            spawns.Add(new Spawn { prefab = prefab, parent = item.transform.parent, pos = item.transform.position, rot = item.transform.rotation });
        }
        Debug.Log($"[LootRegistry] Запомнено мест лута: {spawns.Count}");
    }

    GameObject FindPrefab(string id)
    {
        foreach (var e in prefabs) if (e.id == id) return e.prefab;
        return null;
    }

    public void ResetAllLoot()
    {
        foreach (var item in FindObjectsByType<LootItem>(FindObjectsInactive.Include))
        {
            if (item.Holder != null) item.Holder.Release();
            Destroy(item.gameObject);
        }
        foreach (var s in spawns)
            Instantiate(s.prefab, s.pos, s.rot, s.parent);
        foreach (var g in BreakableGlass.All.ToArray()) g.ResetGlass();
        if (HeistGameManager.Instance != null) HeistGameManager.Instance.ResetScore();
        Debug.Log("[LootRegistry] Весь лут возвращён на места");
    }
}
