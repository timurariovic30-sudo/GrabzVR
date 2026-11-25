using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Всё, что можно «нажать»: VR-рукой (касанием) или кликом мыши в Desktop-режиме.</summary>
public interface IPressable
{
    bool Press();
}

/// <summary>
/// Большая физическая 3D-кнопка (пульт, двери, решётки). Нажимается касанием сферы-руки (на ней VRGrabber)
/// или кликом мыши. Триггер лежит на слое Ignore Raycast, чтобы GorillaLocomotion не упирался в него руками.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class PushButton : MonoBehaviour, IPressable
{
    public string label = "КНОПКА";
    public Transform cap;
    public float pressDepth = 0.025f;
    public float cooldown = 0.6f;
    public AudioClip[] clickClips;
    public UnityEvent onPressed = new UnityEvent();

    AudioSource source;
    float lastPress = -10f;
    Vector3 capRest;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        if (cap) capRest = cap.localPosition;
    }

    void OnTriggerEnter(Collider other)
    {
        var hand = other.GetComponent<VRGrabber>();
        if (hand == null || hand.input == VRGrabber.GrabInput.Mouse) return;
        if (Press()) GorillaHandFeedback.SendHaptic(hand.input == VRGrabber.GrabInput.LeftHandTrigger, 0.6f, 0.07f);
    }

    public bool Press()
    {
        if (Time.time - lastPress < cooldown) return false;
        lastPress = Time.time;
        if (clickClips != null && clickClips.Length > 0) source.PlayOneShot(clickClips[Random.Range(0, clickClips.Length)]);
        if (cap) { StopAllCoroutines(); StartCoroutine(Anim()); }
        Debug.Log($"[PushButton] {label}");
        onPressed.Invoke();
        return true;
    }

    IEnumerator Anim()
    {
        var down = capRest - Vector3.up * pressDepth;
        for (float t = 0; t < 1; t += Time.deltaTime / 0.06f) { cap.localPosition = Vector3.Lerp(capRest, down, t); yield return null; }
        yield return new WaitForSeconds(0.1f);
        for (float t = 0; t < 1; t += Time.deltaTime / 0.15f) { cap.localPosition = Vector3.Lerp(down, capRest, t); yield return null; }
        cap.localPosition = capRest;
    }
}
