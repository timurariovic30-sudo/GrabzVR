using UnityEngine;

/// <summary>
/// Маленький триггер на кончике указательного пальца гориллы (кость *_IndexDistalEnd).
/// Только он нажимает клавиши компьютера GRABZ OS — так нажатия точные, а ладонь соседние клавиши не задевает.
/// </summary>
[RequireComponent(typeof(SphereCollider), typeof(Rigidbody))]
public class ComputerKeyTrigger : MonoBehaviour
{
    public bool isLeft;
}
