using UnityEngine;

/// <summary>Зона удара по дрели: касание сферой-руки (VRGrabber) или клик мышью (IPressable).</summary>
public class DrillHitZone : MonoBehaviour, IPressable
{
    public SafeDrill drill;

    void OnTriggerEnter(Collider other)
    {
        var hand = other.GetComponent<VRGrabber>();
        if (hand == null || hand.input == VRGrabber.GrabInput.Mouse || drill == null) return;
        if (drill.Hit()) GorillaHandFeedback.SendHaptic(hand.input == VRGrabber.GrabInput.LeftHandTrigger, 0.9f, 0.12f);
    }

    public bool Press() => drill != null && drill.Hit();
}
