using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Стеклянная витрина/панель: разбивается сильным ударом VR-руки (через GorillaHandFeedback), брошенным лутом
/// или кликом мыши в Desktop-режиме. Осколки + звук стекла, по желанию — тревога.
/// Сеть: разбивший игрок сразу видит осколки и сообщает хосту (HeistNetworkState.GlassBroken — битовая маска по
/// стабильному номеру стекла), у остальных стекло разлетается по изменению маски; тревогу включает хост.
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
        var net = HeistNetworkState.Instance;
        if (net != null)
        {
            BreakLocal(false);
            net.RequestBreakGlass(Sorted().IndexOf(this));
            return;
        }
        BreakLocal(triggersAlarm);
    }

    /// Только визуал (осколки, звук, спрятать стёкла) + по желанию локальная тревога.
    public void BreakLocal(bool raiseAlarm)
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
        if (raiseAlarm && AlarmSystem.Instance != null) AlarmSystem.Instance.TriggerIntrusion("СТЕКЛО");
    }

    // ------------------------------------------------------------------ сеть: стабильный порядок стёкол
    /// Все стёкла сцены в одинаковом у всех игроков порядке (по пути в иерархии).
    public static List<BreakableGlass> Sorted()
    {
        var list = new List<BreakableGlass>(All);
        list.Sort((a, b) => string.CompareOrdinal(PathOf(a.transform), PathOf(b.transform)));
        return list;
    }

    static string PathOf(Transform t)
    {
        var sb = new System.Text.StringBuilder();
        for (; t != null; t = t.parent) sb.Insert(0, "/" + t.GetSiblingIndex().ToString("D4") + t.name);
        return sb.ToString();
    }

    public static int LocalBrokenMask()
    {
        var list = Sorted();
        int mask = 0;
        for (int i = 0; i < list.Count && i < 32; i++) if (list[i].IsBroken) mask |= 1 << i;
        return mask;
    }

    /// Привести стёкла к маске (без осколков — для зашедшего позже игрока).
    public static void ApplyMask(int mask, bool withEffects)
    {
        var list = Sorted();
        for (int i = 0; i < list.Count && i < 32; i++)
        {
            bool broken = (mask & (1 << i)) != 0;
            if (broken && !list[i].IsBroken) { if (withEffects) list[i].BreakLocal(false); else list[i].HidePanes(); }
            else if (!broken && list[i].IsBroken) list[i].ResetGlass();
        }
    }

    /// Маска сменилась: новые биты — разбить с осколками, снятые биты — починить (сброс лута).
    public static void ApplyMaskChange(int oldMask, int newMask)
    {
        var list = Sorted();
        for (int i = 0; i < list.Count && i < 32; i++)
        {
            int bit = 1 << i;
            if ((newMask & bit) != 0 && (oldMask & bit) == 0) list[i].BreakLocal(false);
            else if ((newMask & bit) == 0 && (oldMask & bit) != 0) list[i].ResetGlass();
        }
    }

    void HidePanes()
    {
        IsBroken = true;
        foreach (var p in panes) if (p) p.SetActive(false);
    }

    public void ResetGlass()
    {
        IsBroken = false;
        foreach (var p in panes) if (p) p.SetActive(true);
    }
}
