using TMPro;
using UnityEngine;

/// <summary>
/// Счётчик голов: триггер прямо под кольцом. Гол засчитывается, когда мяч проходит сверху вниз.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class BasketballHoop : MonoBehaviour
{
    public TextMeshPro scoreText;
    public AudioClip[] scoreClips;
    public ParticleSystem confetti;

    int score;
    float lastScore = -10f;
    AudioSource src;

    void Awake()
    {
        src = GetComponent<AudioSource>();
        UpdateText();
    }

    void OnTriggerEnter(Collider other)
    {
        var ball = other.attachedRigidbody ? other.attachedRigidbody.GetComponent<Basketball>() : null;
        if (ball == null) return;
        if (other.attachedRigidbody.linearVelocity.y > 0.2f) return; // снизу вверх не считается
        if (Time.time - lastScore < 0.8f) return;
        lastScore = Time.time;
        score++;
        UpdateText();
        if (scoreClips != null && scoreClips.Length > 0) src.PlayOneShot(scoreClips[Random.Range(0, scoreClips.Length)]);
        src.PlayOneShot(ProceduralAudio.Coin(), 0.8f);
        if (confetti) confetti.Play();
    }

    void UpdateText()
    {
        if (scoreText) scoreText.text = $"ГОЛЫ: {score}";
    }
}
