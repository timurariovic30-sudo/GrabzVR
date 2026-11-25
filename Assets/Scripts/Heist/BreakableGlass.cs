using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Стеклянная витрина/панель: разбивается сильным ударом VR-руки (через GorillaHandFeedback), брошенным лутом
/// или кликом мыши в Desktop-режиме. Осколки + звук стекла, по желанию — тревога.
/// </summary>
public class BreakableGlass : MonoBehaviour, IPressable
{
    public static readonly List<BreakableGlass> All = new List<BreakableGlass>();

    [Tooltip("Минимальная скорость руки (м/с) для разбивания")]
    public float handBreakSpeed = 1.8f;
    [Tooltip("Минимальная скорость брошенного предмета (м/с)")]
    public float impactBreakSpeed = 4f;
    public bool triggersAlarm = true;
    public AudioClip[] breakClips;
    public Material shardMaterial;
    [Tooltip("Стёкла (рендеры + коллайдеры) этой витрины")]
    public GameObject[] panes;

    public bool IsBroken { get; private set; }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public bool Press() { Break(); return true; }

    public void TryBreakByHand(float speed)
    {
        if (speed >= handBreakSpeed) Break();
    }

    void OnCollisionEnter(Collision c)
    {
        if (c.rigidbody != null && c.relativeVelocity.magnitude >= impactBreakSpeed) Break();
    }

    public void Break()
    {
        if (IsBroken) return;
        IsBroken = true;
        var b = new Bounds(transform.position, Vector3.zero);
        bool first = true;
        foreach (var p in panes)
        {
            if (!p) continue;
            var r = p.GetComponent<Renderer>();
            if (r) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            p.SetActive(false);
        }
        Shatter.Spawn(b, shardMaterial, 18, Vector3.zero, breakClips);
        if (triggersAlarm && AlarmSystem.Instance != null) AlarmSystem.Instance.TriggerIntrusion("СТЕКЛО");
    }

    public void ResetGlass()
    {
        IsBroken = false;
        foreach (var p in panes) if (p) p.SetActive(true);
    }
}
