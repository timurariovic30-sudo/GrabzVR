using System.Collections;
using UnityEngine;

/// <summary>
/// Анимация отправки чёрного автобуса в тоннель (вызывает VanPortal):
///  1) Board(): задняя сдвижная дверь закрывается, включаются холодные LED-фары и объёмные лучи,
///     кузов мелко дрожит на холостом ходу (звук запуска двигателя играет VanPortal);
///  2) DriveOff(): автобус целиком (кузов + салон + коллайдеры) плавно трогается и въезжает в тёмный
///     тоннель (вперёд по +Z); свой игрок в салоне едет вместе с ним — экипаж видит дорогу сквозь
///     стеклянную кабину и свет фар в тоннеле;
///  Cancel(): игрок вышел из салона во время отсчёта — дверь открывается, фары гаснут.
/// Приборная панель кабины (сапфировая подсветка, полоса скорости) оживает при запуске и на ходу.
/// Снаружи салона не видно: после закрытия двери маска проёма выключена, изнутри проём закрывает
/// внутренняя дверь (слои BusPortal/BusInterior).
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
    public float headlightIdle = 3f;               // фары горят и на стоянке
    [Header("Движение")]
    public float driveDistance = 48f;
    public float driveTime = 5f;                  // поездка по тоннелю — ровно 5 секунд
    public float idleShake = 0.006f;
    [Header("Кабина водителя")]
    public Renderer[] dashGlow;        // контур панели, «контур пути», ступица руля
    public Renderer[] speedBar;        // сегменты полосы скорости
    public Color dashColor = new Color(0.04f, 0.31f, 0.82f);
    [Header("Тоннель")]
    [Tooltip("Слои тумана: гаснут, пока салон проезжает сквозь них")]
    public Renderer[] fogLayers;
    public BusPortal portal;

    Vector3 exteriorHome, rootHome;
    bool boarded, driving;
    float speed01;
    MaterialPropertyBlock mpb;
    PlayerModeSwitcher switcher;

    public bool IsDriving => driving;
    Coroutine doorAnim;
    float lightLevel;

    void Awake()
    {
        if (exterior) exteriorHome = exterior.localPosition;
        rootHome = transform.position;
        switcher = FindAnyObjectByType<PlayerModeSwitcher>();
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
        UpdateDash();
    }

    void SetLights(float k)
    {
        lightLevel = k;
        if (headlights != null) foreach (var l in headlights) if (l) { l.enabled = true; l.intensity = Mathf.Lerp(headlightIdle, headlightIntensity, k); }
        if (beams != null) foreach (var b in beams) if (b) b.enabled = k > 0.01f;
        if (ledStrips != null) foreach (var s in ledStrips) if (s) s.enabled = true;
    }

    /// Тусклая сапфировая приборка: в покое едва светится, при запуске двигателя оживает,
    /// на ходу полоса скорости заполняется по мере разгона.
    void UpdateDash()
    {
        mpb ??= new MaterialPropertyBlock();
        float glow = 0.12f + 0.88f * lightLevel;
        if (dashGlow != null)
            foreach (var r in dashGlow) if (r) { mpb.SetColor("_BaseColor", dashColor * glow * (1f + 0.15f * Mathf.Sin(Time.time * 3f) * lightLevel)); r.SetPropertyBlock(mpb); }
        if (speedBar != null)
            for (int i = 0; i < speedBar.Length; i++)
            {
                if (!speedBar[i]) continue;
                bool lit = (i + 0.5f) / speedBar.Length < speed01;
                mpb.SetColor("_BaseColor", dashColor * (lit ? 1.6f : 0.1f * glow));
                speedBar[i].SetPropertyBlock(mpb);
            }
    }

    /// Автобус трогается и въезжает в тоннель целиком (плавный разгон, лёгкое покачивание кузова);
    /// свой игрок в салоне переносится вместе с автобусом каждый кадр.
    public IEnumerator DriveOff()
    {
        driving = true;
        Board();
        rootHome = transform.position;
        float t = 0;
        while (t < driveTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / driveTime);
            speed01 = k;
            Vector3 next = rootHome + transform.forward * (driveDistance * k * k);   // равноускоренно
            Vector3 delta = next - transform.position;
            bool carry = IsLocalPlayerInside();
            transform.position = next;
            if (carry) CarryLocalPlayer(delta);
            if (exterior)
            {
                float sway = Mathf.Sin(t * 7f) * 0.008f * (1f - k);
                exterior.localPosition = exteriorHome + new Vector3(0, sway, 0);
                exterior.localRotation = Quaternion.Euler(-0.6f * Mathf.Clamp01(t * 2f) * (1f - k), 0, sway * 40f);
            }
            HideFogInsideSalon();
            yield return null;
        }
    }

    bool IsLocalPlayerInside()
    {
        if (!switcher) switcher = FindAnyObjectByType<PlayerModeSwitcher>();
        return switcher && portal && portal.IsInsideWorld(switcher.ActivePlayerPosition() + Vector3.up * 0.5f);
    }

    /// Переносит своего игрока (VR — GorillaLocomotion, ПК — CharacterController) на смещение автобуса.
    void CarryLocalPlayer(Vector3 delta)
    {
        bool vr = switcher.Current == PlayerModeSwitcher.Mode.VR || switcher.Current == PlayerModeSwitcher.Mode.VRSimulator;
        if (vr && GorillaLocomotion.Player.Instance != null)
        {
            var gp = GorillaLocomotion.Player.Instance;
            var head = gp.headCollider.transform;
            var pos = new Vector3(head.position.x + delta.x, gp.transform.position.y + delta.y, head.position.z + delta.z);
            switcher.TeleportActive(pos, head.eulerAngles.y);
        }
        else
        {
            var dp = FindAnyObjectByType<DesktopTestPlayer>();
            if (dp) switcher.TeleportActive(dp.transform.position + delta, dp.transform.eulerAngles.y);
        }
    }

    /// Слои «тумана» тоннеля — это плоскости поперёк тоннеля: пока салон едет сквозь них, прячем их.
    void HideFogInsideSalon()
    {
        if (fogLayers == null || !portal) return;
        foreach (var f in fogLayers)
            if (f) f.enabled = !portal.IsInsideWorld(new Vector3(transform.position.x, transform.position.y + 1.2f, f.transform.position.z)) &&
                               transform.InverseTransformPoint(f.transform.position).z > portal.interiorEndZ + 0.3f;
    }

    void OnDisable()
    {
        if (exterior) { exterior.localPosition = exteriorHome; exterior.localRotation = Quaternion.identity; }
    }
}