using UnityEngine;

/// <summary>
/// Баскетбольный мяч в Хабе. Берётся руками как лут (на нём LootItem со стоимостью 0),
/// стучит при ударах и сам возвращается на стойку, если улетел за пределы Хаба или долго валяется в стороне.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(AudioSource))]
public class Basketball : MonoBehaviour
{
    public Transform respawnPoint;
    public AudioClip[] bounceClips;
    public float minY = -3f;
    public float maxDistance = 30f;
    [Tooltip("Если мяч лежит без движения дальше этого расстояния от стойки — через idleReturn секунд вернётся")]
    public float idleReturn = 20f;

    Rigidbody rb;
    AudioSource src;
    LootItem loot;
    float idleTime;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        src = GetComponent<AudioSource>();
        loot = GetComponent<LootItem>();
    }

    void Update()
    {
        if (!respawnPoint) return;
        bool held = loot && loot.Holder != null;
        float dist = Vector3.Distance(transform.position, respawnPoint.position);
        bool resting = !held && rb.linearVelocity.sqrMagnitude < 0.01f && dist > 1.5f;
        idleTime = resting ? idleTime + Time.deltaTime : 0f;

        if (transform.position.y < minY || dist > maxDistance || idleTime > idleReturn) Respawn();
    }

    public void Respawn()
    {
        if (loot && loot.Holder != null) loot.Holder.Release();
        rb.position = respawnPoint.position;
        transform.position = respawnPoint.position;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        idleTime = 0f;
    }

    void OnCollisionEnter(Collision c)
    {
        float v = c.relativeVelocity.magnitude;
        if (v < 0.8f || bounceClips == null || bounceClips.Length == 0) return;
        src.pitch = Random.Range(0.85f, 1.1f);
        src.PlayOneShot(bounceClips[Random.Range(0, bounceClips.Length)], Mathf.Clamp01(v / 6f));
    }
}
