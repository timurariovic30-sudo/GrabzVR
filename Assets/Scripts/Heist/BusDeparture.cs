using System.Collections;
using UnityEngine;

/// <summary>
/// Анимация отправки чёрного автобуса в тоннель (вызывает VanPortal):
///  1) Board(): задняя сдвижная дверь закрывается, включаются холодные LED-фары и объёмные лучи,
///     кузов мелко дрожит на холостом ходу (звук запуска двигателя играет VanPortal);
///  2) DriveOff(): автобус плавно трогается и уезжает в тёмный тоннель (вперёд по +Z).
///  Cancel(): игрок вышел из салона во время отсчёта — дверь открывается, фары гаснут.
///
/// Двигается только внешний кузов (Exterior + дверь + фары). Салон и его коллайдеры остаются на месте:
/// игроки внутри не «выпадают», а снаружи салона не видно — после закрытия двери маска проёма выключена,
/// а изнутри проём закрывает внутренняя дверь (слои BusPortal/BusInterior).
/// </summary>
public class BusDeparture : MonoBehaviour
{
    [Header("Двигается при выезде")]
    public Transform exterior;
    [Header("Задняя дверь")]
    public Transform outerDoor;                    // рольставня: опора у верха проёма, тянется вниз по Y
    public Vector3 doorOpenLocal, doorClosedLocal;
    public Vector3 doorOpenScale = new Vector3(1, 0.02f, 1), doorClosedScale = Vector3.one;
    public Renderer doorMask;          // маска проёма снаружи (Stencil) — гаснет, когда дверь закрыта
    public GameObject innerDoor;       // дверь изнутри салона + её маска
    [Header("Фары")]
    public Light[] headlights;
    public Renderer[] beams;           // объёмные лучи (аддитивные конусы)
    public Renderer[] ledStrips;       // узкие LED-полосы фар
    public float headlightIntensity = 9f;
    [Header("Движение")]
    public float driveDistance = 48f;
    public float driveTime = 4.2f;
    public float idleShake = 0.006f;

    Vector3 exteriorHome;
    bool boarded, driving;
    Coroutine doorAnim;
    float lightLevel;

    void Awake()
    {
        if (exterior) exteriorHome = exterior.localPosition;
        SetLights(0f);
        if (innerDoor) innerDoor.SetActive(false);
        if (outerDoor) { outerDoor.localPosition = doorOpenLocal; outerDoor.localScale = doorOpenScale; }
    }

    public void Board()
    {
        if (boarded) return;
        boarded = true;
        if (doorAnim != null) StopCoroutine(doorAnim);
        doorAnim = StartCoroutine(MoveDoor(doorClosedLocal, doorClosedScale, 0.9f, true));
    }

    public void Cancel()
    {
        if (!boarded || driving) return;
        boarded = false;
        if (doorAnim != null) StopCoroutine(doorAnim);
        if (innerDoor) innerDoor.SetActive(false);
        if (doorMask) doorMask.enabled = true;
        doorAnim = StartCoroutine(MoveDoor(doorOpenLocal, doorOpenScale, 0.7f, false));
    }

    IEnumerator MoveDoor(Vector3 target, Vector3 targetScale, float time, bool closing)
    {
        if (!outerDoor) yield break;
        Vector3 from = outerDoor.localPosition, fromScale = outerDoor.localScale;
        for (float t = 0; t < time; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / time);
            outerDoor.localPosition = Vector3.Lerp(from, target, k);
            outerDoor.localScale = Vector3.Lerp(fromScale, targetScale, k);
            yield return null;
        }
        outerDoor.localPosition = target; outerDoor.localScale = targetScale;
        if (closing)
        {
            if (doorMask) doorMask.enabled = false;   // снаружи салон больше не виден
            if (innerDoor) innerDoor.SetActive(true); // изнутри проём закрыт
        }
    }

    void Update()
    {
        // фары плавно разгораются / гаснут
        float target = boarded ? 1f : 0f;
        if (!Mathf.Approximately(lightLevel, target)) SetLights(Mathf.MoveTowards(lightLevel, target, Time.deltaTime * 2.5f));
        // холостой ход: мелкая дрожь кузова
        if (exterior && boarded && !driving)
        {
            float t = Time.time * 23f;
            exterior.localPosition = exteriorHome + new Vector3(Mathf.Sin(t * 1.3f) * 0.4f, Mathf.Sin(t), Mathf.Sin(t * 0.7f) * 0.3f) * idleShake;
        }
    }

    void SetLights(float k)
    {
        lightLevel = k;
        if (headlights != null) foreach (var l in headlights) if (l) { l.enabled = k > 0.01f; l.intensity = headlightIntensity * k; }
        if (beams != null) foreach (var b in beams) if (b) b.enabled = k > 0.01f;
        if (ledStrips != null) foreach (var s in ledStrips) if (s) s.enabled = k > 0.01f;
    }

    /// Автобус трогается и уезжает в тоннель (плавный разгон, лёгкое покачивание).
    public IEnumerator DriveOff()
    {
        if (!exterior) yield break;
        driving = true;
        Board();
        float t = 0;
        while (t < driveTime)
        {
            t += Time.deltaTime;
            float k = t / driveTime;
            float dist = driveDistance * k * k;                       // равноускоренно
            float sway = Mathf.Sin(t * 7f) * 0.008f * (1f - k);
            exterior.localPosition = exteriorHome + new Vector3(0, sway, dist);
            exterior.localRotation = Quaternion.Euler(-0.6f * Mathf.Clamp01(t * 2f) * (1f - k), 0, sway * 40f);
            yield return null;
        }
    }

    void OnDisable()
    {
        if (exterior) { exterior.localPosition = exteriorHome; exterior.localRotation = Quaternion.identity; }
    }
}
