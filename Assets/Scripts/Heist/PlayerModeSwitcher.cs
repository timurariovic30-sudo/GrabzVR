using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Выбирает, каким игроком играть:
///  • шлем подключён и активен (XRSettings.isDeviceActive) → VRGorillaPlayer;
///  • шлема нет → DesktopTestPlayer (клавиатура + мышь);
///  • F1 без шлема → переключение Desktop ↔ «VR-руки с клавиатуры» (VRGorillaPlayer + XR Device Simulator).
/// </summary>
public class PlayerModeSwitcher : MonoBehaviour
{
    public enum Mode { Auto, Desktop, VRSimulator, VR }

    [Tooltip("Auto: шлем → VR, иначе Desktop. Остальные значения принудительно задают режим.")]
    public Mode startMode = Mode.Auto;
    public GameObject vrPlayer;
    public GameObject desktopPlayer;
    public GameObject deviceSimulator;
    public Key toggleKey = Key.F1;

    public Mode Current { get; private set; }

    void Awake()
    {
        // Сначала всё выключаем, чтобы не было двух камер/AudioListener'ов.
        if (vrPlayer) vrPlayer.SetActive(false);
        if (desktopPlayer) desktopPlayer.SetActive(false);
        if (deviceSimulator) deviceSimulator.SetActive(false);
    }

    IEnumerator Start()
    {
        if (startMode != Mode.Auto) { Apply(startMode); yield break; }

        Apply(XRSettings.isDeviceActive ? Mode.VR : Mode.Desktop);

        // OpenXR иногда поднимает шлем на кадр-другой позже — перепроверяем в течение 2 секунд.
        float until = Time.unscaledTime + 2f;
        while (Current == Mode.Desktop && Time.unscaledTime < until)
        {
            if (XRSettings.isDeviceActive) { Apply(Mode.VR); yield break; }
            yield return null;
        }
    }

    void Update()
    {
        // Шлем поднялся позже (Quest Link / AirLink бывает и через 5–10 с) → сразу в VR,
        // иначе в шлеме оставалась бы десктопная камера с экранным текстом
        if (startMode == Mode.Auto && Current != Mode.VR && XRSettings.isDeviceActive) { Apply(Mode.VR); return; }
        var kb = Keyboard.current;
        if (kb == null || Current == Mode.VR) return;
        if (kb[toggleKey].wasPressedThisFrame)
            Apply(Current == Mode.Desktop ? Mode.VRSimulator : Mode.Desktop);
    }

    public void Apply(Mode mode)
    {
        Current = mode;
        bool vr = mode == Mode.VR || mode == Mode.VRSimulator;

        // Новый игрок появляется там, где стоял предыдущий.
        var from = vr ? desktopPlayer : vrPlayer;
        var to = vr ? vrPlayer : desktopPlayer;
        if (from != null && to != null && from.activeSelf)
        {
            var p = from.transform.position;
            if (from == vrPlayer && GorillaLocomotion.Player.Instance != null)
                p = GorillaLocomotion.Player.Instance.bodyCollider.transform.position + Vector3.down * 0.9f;
            to.transform.position = p;
        }

        if (desktopPlayer) desktopPlayer.SetActive(!vr);
        if (vrPlayer) vrPlayer.SetActive(vr);
        if (deviceSimulator) deviceSimulator.SetActive(mode == Mode.VRSimulator);

        if (vr) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        Debug.Log($"[PlayerModeSwitcher] Mode: {mode} (XR device active: {XRSettings.isDeviceActive})");
    }

    /// Где сейчас активный игрок (для VR — его голова).
    public Vector3 ActivePlayerPosition()
    {
        bool vr = Current == Mode.VR || Current == Mode.VRSimulator;
        if (vr && GorillaLocomotion.Player.Instance != null) return GorillaLocomotion.Player.Instance.headCollider.transform.position;
        return desktopPlayer ? desktopPlayer.transform.position : Vector3.zero;
    }

    /// Мгновенно перенести активного игрока в точку (pos — пол под ногами, yaw — куда смотреть).
    public void TeleportActive(Vector3 pos, float yaw)
    {
        bool vr = Current == Mode.VR || Current == Mode.VRSimulator;
        if (vr && GorillaLocomotion.Player.Instance != null)
        {
            var gp = GorillaLocomotion.Player.Instance;
            var root = gp.transform;
            var head = gp.headCollider.transform;
            // поворот вокруг головы так, чтобы взгляд смотрел в yaw
            root.RotateAround(head.position, Vector3.up, Mathf.DeltaAngle(head.eulerAngles.y, yaw));
            // голова (по XZ) — в точку, ноги — на пол
            var off = head.position - root.position; off.y = 0f;
            root.position = pos - off;
            var rb = root.GetComponent<Rigidbody>();
            if (rb) { rb.position = root.position; rb.linearVelocity = Vector3.zero; }
            // руки «переносим» вместе с телом, иначе GorillaLocomotion посчитает огромный рывок
            gp.leftHandFollower.position = gp.leftHandTransform.position;
            gp.rightHandFollower.position = gp.rightHandTransform.position;
            gp.InitializeValues();
        }
        else if (desktopPlayer)
        {
            var cc = desktopPlayer.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            desktopPlayer.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            if (cc) cc.enabled = true;
        }
    }

    void OnGUI()
    {
        if (Current == Mode.VR || XRSettings.isDeviceActive) return; // в шлеме — только World Space
        string text = Current == Mode.Desktop
            ? "Режим: DESKTOP   •   F1 — VR-руки с клавиатуры (симулятор)"
            : "Режим: VR-СИМУЛЯТОР   •   F1 — обратно в Desktop";
        GUI.Label(new Rect(10, 10, 600, 24), text);
    }
}
