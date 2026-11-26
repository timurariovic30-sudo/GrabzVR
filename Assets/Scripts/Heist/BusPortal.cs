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
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
    }

    void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        // forceRenderingOff не сериализуется: в редакторе сцена не помечается изменённой каждый кадр
        if (insideMask) insideMask.forceRenderingOff = !IsInside(cam);
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
