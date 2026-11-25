using UnityEngine;

/// <summary>Включает дополнительную камеру (зеркало) только когда игрок рядом — экономия кадров в VR.</summary>
[RequireComponent(typeof(Camera))]
public class ProximityCameraEnabler : MonoBehaviour
{
    public float distance = 9f;
    Camera cam;

    void Awake() => cam = GetComponent<Camera>();

    void Update()
    {
        var main = Camera.main;
        cam.enabled = main != null && (main.transform.position - transform.position).sqrMagnitude < distance * distance;
    }
}
