using UnityEngine;

/// <summary>
/// Держит капсулу тела под головой игрока (GorillaLocomotion сам поворачивает её только по Y).
/// </summary>
public class GorillaBodyFollower : MonoBehaviour
{
    public Transform head;
    [Tooltip("На сколько ниже головы находится центр тела")]
    public float offsetBelowHead = 0.45f;

    void LateUpdate()
    {
        if (head != null) transform.position = head.position + Vector3.down * offsetBelowHead;
    }
}
