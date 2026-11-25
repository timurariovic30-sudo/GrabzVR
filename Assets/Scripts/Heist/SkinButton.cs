using System.Collections;
using UnityEngine;

/// <summary>
/// Физическая 3D-кнопка терминала цвета. Нажимается VR-рукой (касание сферы-руки) или кликом мыши в Desktop-режиме.
/// Меняет цвет шерсти игрока, играет клик и сохраняет выбор в PlayerPrefs (через PlayerSkinController).
/// Коллайдер-триггер кнопки лежит на слое Ignore Raycast, чтобы GorillaLocomotion не «упирался» в него руками.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class SkinButton : MonoBehaviour, IPressable
{
    public string colorName = "Чёрный";
    public Color furColor = Color.black;
    [Tooltip("Колпачок кнопки, который «утапливается» при нажатии")]
    public Transform cap;
    public float pressDepth = 0.015f;
    public AudioClip[] clickClips;
    public float cooldown = 0.4f;

    AudioSource source;
    float lastPress = -1f;
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
        if (Press())
            GorillaHandFeedback.SendHaptic(hand.input == VRGrabber.GrabInput.LeftHandTrigger, 0.5f, 0.06f);
    }

    /// Нажать кнопку. Возвращает false, если сработал антидребезг.
    public bool Press()
    {
        if (Time.time - lastPress < cooldown) return false;
        lastPress = Time.time;

        if (clickClips != null && clickClips.Length > 0)
            source.PlayOneShot(clickClips[Random.Range(0, clickClips.Length)]);

        var skins = FindObjectsByType<PlayerSkinController>(FindObjectsInactive.Include);
        foreach (var s in skins) s.SetFurColor(furColor, save: true);
        if (skins.Length == 0) PlayerSkinController.SaveColor(furColor);

        if (cap) { StopAllCoroutines(); StartCoroutine(PressAnim()); }
        Debug.Log($"[SkinButton] Цвет: {colorName}");
        return true;
    }

    IEnumerator PressAnim()
    {
        var down = capRest - Vector3.up * pressDepth;
        for (float t = 0; t < 1; t += Time.deltaTime / 0.06f) { cap.localPosition = Vector3.Lerp(capRest, down, t); yield return null; }
        yield return new WaitForSeconds(0.08f);
        for (float t = 0; t < 1; t += Time.deltaTime / 0.12f) { cap.localPosition = Vector3.Lerp(down, capRest, t); yield return null; }
        cap.localPosition = capRest;
    }
}
