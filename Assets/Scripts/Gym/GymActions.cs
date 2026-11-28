using UnityEngine;

/// <summary>Мелкие действия кнопок полигона: вернуть весь лут на места.</summary>
public class GymActions : MonoBehaviour
{
    public void ResetLoot()
    {
        int n = 0;
        foreach (var l in FindObjectsByType<LootItem>(FindObjectsInactive.Include)) { l.RequestReset(); n++; }
        Debug.Log($"[GymActions] лут сброшен: {n}");
    }
}
