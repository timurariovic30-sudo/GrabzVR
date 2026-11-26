using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Поворот правым стиком для VR-гориллы. Режим и скорость задаются на компьютере GRABZ OS (вкладка TURN):
///  SNAP — щелчками (15°…60° в зависимости от скорости), SMOOTH — плавно (30…300 °/с), NONE — выключен.
/// </summary>
[RequireComponent(typeof(GorillaLocomotion.Player))]
public class GorillaStickTurn : MonoBehaviour
{
    [Tooltip("Мёртвая зона стика")]
    public float deadZone = 0.55f;

    GorillaLocomotion.Player player;
    InputAction stick;
    bool snapReady = true;

    void Awake() => player = GetComponent<GorillaLocomotion.Player>();

    void OnEnable()
    {
        stick = new InputAction("TurnStick", InputActionType.Value, expectedControlType: "Vector2");
        stick.AddBinding("<XRController>{RightHand}/primary2DAxis");
        stick.AddBinding("<XRController>{RightHand}/thumbstick");
        stick.Enable();
    }

    void OnDisable()
    {
        stick?.Disable();
        stick?.Dispose();
        stick = null;
    }

    void Update()
    {
        if (stick == null || player == null) return;
        float x = stick.ReadValue<Vector2>().x;
        var mode = GrabzSettings.Turn;
        int speed = GrabzSettings.TurnSpeed;
        Vector3 pivot = player.headCollider ? player.headCollider.transform.position : transform.position;

        if (mode == GrabzSettings.TurnMode.Snap)
        {
            if (Mathf.Abs(x) < deadZone * 0.6f) { snapReady = true; return; }
            if (!snapReady || Mathf.Abs(x) < deadZone) return;
            snapReady = false;
            float step = Mathf.Lerp(15f, 60f, speed / 9f);
            player.TurnAround(pivot, Mathf.Sign(x) * step);
        }
        else if (mode == GrabzSettings.TurnMode.Smooth)
        {
            if (Mathf.Abs(x) < deadZone * 0.4f) return;
            float degPerSec = Mathf.Lerp(30f, 300f, speed / 9f);
            player.TurnAround(pivot, x * degPerSec * Time.deltaTime);
        }
    }
}
