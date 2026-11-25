using UnityEngine;

/// <summary>Шлюз между Убежищем и зданием. Обёртка над SlidingDoor, чтобы менеджер раунда мог его открыть.</summary>
[RequireComponent(typeof(SlidingDoor))]
public class AirlockDoor : MonoBehaviour
{
    SlidingDoor door;
    void Awake() => door = GetComponent<SlidingDoor>();
    public void Open() => door.Open();
    public void Close() => door.Close();
}
