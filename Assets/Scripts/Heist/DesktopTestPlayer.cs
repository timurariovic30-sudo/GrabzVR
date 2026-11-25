using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Простой игрок для тестов без шлема: WASD, мышь — обзор, Shift — бег, Space — прыжок, Esc — освободить курсор.
/// Только для HeistLevel_Test; настоящий VR-игрок — у напарника. Учитывает VRGrabber.SpeedMultiplier.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class DesktopTestPlayer : MonoBehaviour
{
    public Transform cameraPivot;
    public float walkSpeed = 4f;
    public float runMultiplier = 1.6f;
    public float jumpHeight = 1.1f;
    public float gravity = -20f;
    public float mouseSensitivity = 0.12f;

    CharacterController cc;
    float pitch, verticalVel;
    VRGrabber grabber;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        grabber = GetComponentInChildren<VRGrabber>();
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        if (Cursor.lockState != CursorLockMode.Locked && mouse.leftButton.wasPressedThisFrame)
        { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        if (kb.escapeKey.wasPressedThisFrame)
        { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 d = mouse.delta.ReadValue() * mouseSensitivity;
            transform.Rotate(0f, d.x, 0f);
            pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        Vector2 move = Vector2.zero;
        if (kb.wKey.isPressed) move.y += 1;
        if (kb.sKey.isPressed) move.y -= 1;
        if (kb.dKey.isPressed) move.x += 1;
        if (kb.aKey.isPressed) move.x -= 1;
        move = Vector2.ClampMagnitude(move, 1f);

        float speed = walkSpeed * (kb.leftShiftKey.isPressed ? runMultiplier : 1f) * VRGrabber.SpeedMultiplier;
        Vector3 horizontal = (transform.forward * move.y + transform.right * move.x) * speed;

        if (cc.isGrounded && verticalVel <= 0f)
        {
            verticalVel = -1f;
            if (kb.spaceKey.wasPressedThisFrame) verticalVel = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        verticalVel += gravity * Time.deltaTime;

        cc.Move((horizontal + Vector3.up * verticalVel) * Time.deltaTime);
    }

    /// Подбросить вверх (батут).
    public void Launch(float upSpeed) => verticalVel = Mathf.Max(verticalVel, upSpeed);

    void OnGUI()
    {
        // экранный текст (IMGUI) в шлеме двоится и уезжает за край — показываем только на мониторе
        if (UnityEngine.XR.XRSettings.isDeviceActive) return;
        float cx = Screen.width / 2f, cy = Screen.height / 2f;
        GUI.Label(new Rect(cx - 5, cy - 11, 20, 22), "+");

        string info = "WASD — ходить, мышь — смотреть, ЛКМ — нести / нажать кнопку / разбить стекло, ПКМ — бросить, Esc — курсор";
        if (grabber != null && grabber.Held != null)
        {
            var h = grabber.Held;
            info = $"В руках: {h.itemName} (${h.value})" + (h.isHeavy ? "  —  ТЯЖЁЛЫЙ: скорость −40%" : "");
        }
        GUI.Label(new Rect(10, Screen.height - 30, Screen.width - 20, 24), info);
    }
}
