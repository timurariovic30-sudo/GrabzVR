using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Лазерная решётка: двигает свои лучи (дочерние LaserBeam) и при касании игрока включает общую тревогу (AlarmSystem).
/// Во время тревоги лучи быстро мигают и двигаются быстрее.
/// </summary>
public class LaserAlarm : MonoBehaviour
{
    public enum MoveAxis { UpDown, LeftRight, ForwardBack }

    [Header("Движение")]
    public MoveAxis axis = MoveAxis.UpDown;
    public float amplitude = 0.35f;
    public float speed = 1.2f;
    [Tooltip("Сдвиг фазы между соседними лучами — чтобы щели «гуляли»")]
    public float phaseStep = 2.1f;
    [Tooltip("Все лучи двигаются вместе (сплошная решётка)")]
    public bool moveAsOne;

    [Header("Тревога")]
    public float blinkRate = 12f;
    public float alarmSpeedMultiplier = 2f;

    readonly List<LaserBeam> beams = new List<LaserBeam>();
    readonly List<Vector3> basePositions = new List<Vector3>();
    float movePhase;

    static bool AlarmOn => AlarmSystem.Instance != null && AlarmSystem.Instance.IsActive;

    void Awake()
    {
        GetComponentsInChildren(beams);
        foreach (var b in beams)
        {
            b.alarm = this;
            basePositions.Add(b.transform.localPosition);
        }
    }

    void FixedUpdate()
    {
        movePhase += Time.fixedDeltaTime * speed * (AlarmOn ? alarmSpeedMultiplier : 1f);
        Vector3 dir = axis == MoveAxis.UpDown ? Vector3.up : axis == MoveAxis.LeftRight ? Vector3.right : Vector3.forward;
        for (int i = 0; i < beams.Count; i++)
        {
            float phase = moveAsOne ? 0f : i * phaseStep;
            var local = basePositions[i] + dir * (Mathf.Sin(movePhase + phase) * amplitude);
            var world = transform.TransformPoint(local);
            if (beams[i].Body != null) beams[i].Body.MovePosition(world);
            else beams[i].transform.position = world;
        }
    }

    void Update()
    {
        bool visible = !AlarmOn || Mathf.Repeat(Time.time * blinkRate, 1f) < 0.5f;
        foreach (var b in beams) b.SetVisible(visible);
    }

    public void TriggerAlarm()
    {
        if (AlarmSystem.Instance != null) AlarmSystem.Instance.TriggerIntrusion("ЛАЗЕР");
    }
}
