using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// «Внутри больше, чем снаружи» у чёрного автобуса в хабе.
/// Салон (слой BusInterior) рисуется только там, где Stencil = 1:
///  • снаружи — сквозь проём входа (маска doorMask на слое BusPortal);
///  • изнутри — везде, кроме самого проёма (insideMask: та же коробка салона, грани внутрь).
/// insideMask включается отдельно для каждой камеры, если она стоит в салоне: так зеркало
/// и камеры других ракурсов продолжают видеть автобус снаружи как обычно.
/// Координаты салона — в локальных осях этого объекта (корень автобуса, +Z — к кабине).
/// </summary>
[ExecuteAlways]
public class BusPortal : MonoBehaviour
{
    [Tooltip("Маска салона изнутри (BusPortal-слой, грани смотрят внутрь, с дырой под проём)")]
    public Renderer insideMask;
    [Tooltip("Смарт-стекло салона и кабины: рисуется только для камеры в салоне. Снаружи автобус глухой, " +
             "даже если у буфера глубины нет Stencil (иначе стекло видно прозрачной коробкой больше кузова)")]
    public Renderer[] insideOnly;
    [Header("Салон, локальные оси")]
    public float doorZ = -3.2f;          // плоскость проёма (задняя стенка)
    public float interiorEndZ = 7.7f;    // дальняя стенка салона
    public float halfWidth = 1.25f;
    public float floorY = 0.4f, ceilingY = 2.43f;
    [Header("Проём")]
    public float doorHalfWidth = 0.65f;
    public float doorTopY = 2.1f;

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        if (insideMask) insideMask.forceRenderingOff = true;
        if (insideOnly != null) foreach (var r in insideOnly) if (r) r.forceRenderingOff = true;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
    }

    void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        // forceRenderingOff не сериализуется: в редакторе сцена не помечается изменённой каждый кадр
        bool inside = IsInside(cam);
        if (insideMask) insideMask.forceRenderingOff = !inside;
        if (insideOnly != null) foreach (var r in insideOnly) if (r) r.forceRenderingOff = !inside;
    }

    // ------------------------------------------------------------------ гориллы в салоне
    // Салон длиннее кузова и уходит в тоннель перед капотом: горилла в глубине салона снаружи стояла бы
    // в тоннеле. Пока голова гориллы в салоне, её рендеры (кроме своей головы LocalHead) переходят на слой
    // BusInterior — их видно только сквозь проём и изнутри, как сам салон.
    [Tooltip("Слой салона (BusInterior)")]
    public int interiorLayer = 11;
    readonly System.Collections.Generic.Dictionary<Renderer, int> movedRenderers = new System.Collections.Generic.Dictionary<Renderer, int>();
    readonly System.Collections.Generic.List<GorillaFinalRig> rigs = new System.Collections.Generic.List<GorillaFinalRig>();
    readonly System.Collections.Generic.HashSet<GorillaFinalRig> insideRigs = new System.Collections.Generic.HashSet<GorillaFinalRig>();
    float nextScan;

    public bool IsInsideWorld(Vector3 world)
    {
        Vector3 p = transform.InverseTransformPoint(world);
        return p.z > doorZ + 0.05f && p.z < interiorEndZ && Mathf.Abs(p.x) < halfWidth + 0.1f && p.y > floorY - 0.5f && p.y < ceilingY + 0.5f;
    }

    void LateUpdate()
    {
        if (!Application.isPlaying) return;
        if (Time.unscaledTime > nextScan)
        {
            nextScan = Time.unscaledTime + 1f;
            rigs.Clear();
            rigs.AddRange(FindObjectsByType<GorillaFinalRig>());
        }
        int localHead = LayerMask.NameToLayer("LocalHead");
        foreach (var rig in rigs)
        {
            if (!rig || !rig.headBone) continue;
            bool inside = rig.isActiveAndEnabled && IsInsideWorld(rig.headBone.position);
            if (inside == insideRigs.Contains(rig)) continue;
            if (inside) insideRigs.Add(rig); else insideRigs.Remove(rig);
            foreach (var r in rig.GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject.layer == localHead || r.GetComponent<Collider>()) continue;
                if (inside) { if (!movedRenderers.ContainsKey(r)) movedRenderers[r] = r.gameObject.layer; r.gameObject.layer = interiorLayer; }
                else if (movedRenderers.TryGetValue(r, out int was)) { r.gameObject.layer = was; movedRenderers.Remove(r); }
            }
        }
    }

    /// Камера в салоне? Возле проёма запас на ближнюю плоскость отсечения: пока голова проходит
    /// сквозь проём, маска проёма уже обрезана камерой — салон держит маска изнутри.
    public bool IsInside(Camera cam)
    {
        Vector3 p = transform.InverseTransformPoint(cam.transform.position);
        float margin = cam.nearClipPlane * 1.5f + 0.02f;
        if (p.z < doorZ - margin || p.z > interiorEndZ) return false;
        if (Mathf.Abs(p.x) > halfWidth || p.y < floorY - 0.3f || p.y > ceilingY + 0.2f) return false;
        if (p.z > doorZ + margin) return true;
        // в толще проёма: только если камера в его просвете
        return Mathf.Abs(p.x) < doorHalfWidth && p.y < doorTopY;
    }
}
