using UnityEngine;

/// <summary>
/// Батут / пружинящий мат.
///  • VR: на коллайдере мата висит GorillaLocomotion.Surface с bounceMultiplier — толчок руками от мата в N раз сильнее;
///    если тело игрока падает на мат — его подбрасывает.
///  • Desktop: войди в триггер над матом — подбросит вверх.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class Trampoline : MonoBehaviour
{
    public float launchSpeed = 11f;
    public AudioClip[] boingClips;
    AudioSource source;
    float lastBoing;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
    }

    void OnCollisionEnter(Collision c) => TryLaunchRigidbody(c.rigidbody);

    void OnTriggerEnter(Collider other)
    {
        var desktop = other.GetComponentInParent<DesktopTestPlayer>();
        if (desktop != null) { desktop.Launch(launchSpeed); Boing(); return; }
        TryLaunchRigidbody(other.attachedRigidbody);
    }

    void TryLaunchRigidbody(Rigidbody rb)
    {
        if (rb == null || rb.GetComponent<GorillaLocomotion.Player>() == null) return;
        var v = rb.linearVelocity;
        if (v.y > launchSpeed * 0.5f) return;
        rb.linearVelocity = new Vector3(v.x, launchSpeed, v.z);
        Boing();
    }

    /// Вызывается и из GorillaHandFeedback при шлепке рукой по мату.
    public void Boing()
    {
        if (Time.time - lastBoing < 0.15f) return;
        lastBoing = Time.time;
        if (boingClips != null && boingClips.Length > 0)
        {
            source.pitch = Random.Range(0.9f, 1.15f);
            source.PlayOneShot(boingClips[Random.Range(0, boingClips.Length)]);
        }
    }
}
