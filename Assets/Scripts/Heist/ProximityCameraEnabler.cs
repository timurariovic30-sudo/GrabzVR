using UnityEngine;

/// <summary>
/// Включает дополнительную камеру (зеркало) только когда игрок рядом И смотрит в её сторону —
/// зеркало рисует всю сцену второй раз, в VR это самое дорогое, что есть в хабе.
/// Зеркало обновляется через кадр (30 раз в секунду при 60–90 FPS) — на глаз незаметно, а вдвое дешевле.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ProximityCameraEnabler : MonoBehaviour
{
    public float distance = 6f;
    [Tooltip("Обновлять зеркало раз в N кадров")]
    public int renderEveryNthFrame = 2;
    [Tooltip("Поверхность зеркала (по ней проверяется, смотрит ли игрок в его сторону)")]
    public Renderer surface;
    Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (!surface && transform.parent)
        {
            var s = transform.parent.Find("MirrorSurface");
            if (s) surface = s.GetComponent<Renderer>();
        }
    }

    void Update()
    {
        var main = Camera.main;
        bool on = main != null && (main.transform.position - transform.position).sqrMagnitude < distance * distance;
        if (on && surface)
        {
            // зеркало вне поля зрения — не рисуем
            Vector3 to = surface.bounds.center - main.transform.position;
            on = Vector3.Dot(main.transform.forward, to.normalized) > 0.1f || to.sqrMagnitude < 1.5f;
        }
        if (on && renderEveryNthFrame > 1) on = Time.frameCount % renderEveryNthFrame == 0;
        cam.enabled = on;
    }
}
