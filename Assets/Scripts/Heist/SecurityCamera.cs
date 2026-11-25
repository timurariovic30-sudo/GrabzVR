using UnityEngine;

/// <summary>
/// Камера слежения: поворачивается туда-сюда, показывает конус обзора. Зелёный — чисто, красный — видит игрока.
/// Если игрок в конусе дольше spotTime секунд (и нет стены между ними) — общая тревога.
/// </summary>
public class SecurityCamera : MonoBehaviour
{
    public Transform pivot;          // что вращается (корпус камеры)
    public Renderer cone;            // визуальный конус обзора
    public float sweepAngle = 45f;
    public float sweepSpeed = 0.5f;
    public float viewAngle = 22f;    // полуугол конуса
    public float range = 9f;
    public float spotTime = 0.8f;
    public Color clearColor = new Color(0.2f, 1f, 0.3f, 0.12f);
    public Color alertColor = new Color(1f, 0.1f, 0.05f, 0.2f);

    Quaternion baseRot;
    float seen, phase;
    MaterialPropertyBlock block;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        if (!pivot) pivot = transform;
        baseRot = pivot.localRotation;
        phase = Random.value * 6f;
    }

    void Update()
    {
        pivot.localRotation = baseRot * Quaternion.Euler(0f, Mathf.Sin(Time.time * sweepSpeed + phase) * sweepAngle, 0f);

        bool sees = SeesPlayer();
        seen = sees ? seen + Time.deltaTime : 0f;
        if (seen >= spotTime && AlarmSystem.Instance != null) AlarmSystem.Instance.TriggerIntrusion("КАМЕРА");

        if (cone)
        {
            block ??= new MaterialPropertyBlock();
            cone.GetPropertyBlock(block);
            bool alarm = AlarmSystem.Instance != null && AlarmSystem.Instance.IsActive;
            block.SetColor(BaseColorId, sees || alarm ? alertColor : clearColor);
            cone.SetPropertyBlock(block);
        }
    }

    bool SeesPlayer()
    {
        var cam = Camera.main;
        if (cam == null) return false;
        Vector3 eye = pivot.position, target = cam.transform.position;
        Vector3 to = target - eye;
        float dist = to.magnitude;
        if (dist > range || Vector3.Angle(pivot.forward, to) > viewAngle) return false;
        // Стена между камерой и игроком? (уровень — слой Default, игрок — слой Player/камера без коллайдера)
        return !Physics.Raycast(eye + to.normalized * 0.3f, to.normalized, dist - 0.5f, 1 << 0, QueryTriggerInteraction.Ignore);
    }
}
