using System.Collections;
using UnityEngine;

/// <summary>
/// Одна физическая клавиша ретро-компьютера GRABZ OS. Утапливается при касании VR-рукой
/// (сфера-рука с VRGrabber) или клике мышью, щёлкает и даёт короткую вибрацию в контроллер.
/// Триггер на слое Ignore Raycast — руки GorillaLocomotion сквозь него проходят.
/// </summary>
public class ComputerKey : MonoBehaviour, IPressable
{
    public string key = "A";
    public GrabzComputer computer;
    public Transform cap;
    public float pressDepth = 0.012f;

    Vector3 capRest;
    bool restSaved;

    void Awake() { if (cap) { capRest = cap.localPosition; restSaved = true; } }

    void OnTriggerEnter(Collider other)
    {
        // нажимает только кончик указательного пальца (ComputerKeyTrigger), не вся ладонь
        var tip = other.GetComponent<ComputerKeyTrigger>();
        if (tip == null || computer == null) return;
        if (computer.TryPressFromHand(this, tip.isLeft))
            GorillaHandFeedback.SendHaptic(tip.isLeft, 0.35f, 0.035f);
    }

    public bool Press() => computer != null && computer.TryPressFromHand(this, null);

    public void Animate()
    {
        if (!cap || !isActiveAndEnabled) return;
        if (!restSaved) { capRest = cap.localPosition; restSaved = true; }
        StopAllCoroutines();
        StartCoroutine(Anim());
    }

    IEnumerator Anim()
    {
        var down = capRest - Vector3.up * pressDepth;
        cap.localPosition = down;                 // мгновенно вниз — ощущается «щелчком»
        yield return new WaitForSeconds(0.08f);
        for (float t = 0; t < 1; t += Time.deltaTime / 0.08f) { cap.localPosition = Vector3.Lerp(down, capRest, t); yield return null; }
        cap.localPosition = capRest;
    }
}
