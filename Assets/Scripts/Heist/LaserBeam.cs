using UnityEngine;

/// <summary>Один луч лазера: триггер-коллайдер + кинематический Rigidbody. Сообщает LaserAlarm о касании игрока.</summary>
[RequireComponent(typeof(Collider))]
public class LaserBeam : MonoBehaviour
{
    [HideInInspector] public LaserAlarm alarm;

    public Rigidbody Body { get; private set; }
    Renderer rend;

    void Awake()
    {
        Body = GetComponent<Rigidbody>();
        rend = GetComponent<Renderer>();
    }

    public void SetVisible(bool v)
    {
        if (rend != null && rend.enabled != v) rend.enabled = v;
    }

    void OnTriggerEnter(Collider other)
    {
        if (alarm != null && IsPlayer(other)) alarm.TriggerAlarm();
    }

    /// Игрок = объект с тегом Player (сам коллайдер, его Rigidbody или корень иерархии, например VR-риг).
    public static bool IsPlayer(Collider c)
    {
        if (c.CompareTag("Player")) return true;
        if (c.attachedRigidbody != null && c.attachedRigidbody.CompareTag("Player")) return true;
        return c.transform.root.CompareTag("Player");
    }
}
