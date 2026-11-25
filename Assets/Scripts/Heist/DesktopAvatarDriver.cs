using UnityEngine;

/// <summary>
/// Горилла для ПК-режима (DesktopTestPlayer): та же модель и риг, что у VR-игрока, но руки ведутся
/// не контроллерами, а «по-обезьяньи» сами: висят перед телом, покачиваются при ходьбе,
/// а правая тянется к предмету, который игрок несёт. Весь аватар на слое LocalHead —
/// своя камера ПК-игрока его не рисует (не мешает целиться), а зеркало видит.
/// </summary>
[DefaultExecutionOrder(900)] // раньше GorillaFinalRig (1000)
public class DesktopAvatarDriver : MonoBehaviour
{
    public GorillaFinalRig rig;
    public Transform playerRoot;
    public VRGrabber grabber;
    public Transform leftTarget, rightTarget;
    public Vector3 restOffset = new Vector3(0.32f, -0.78f, 0.28f);
    public float swing = 0.12f;

    Vector3 lastPos;
    float phase, speed;

    void OnEnable() { if (playerRoot) lastPos = playerRoot.position; }

    void LateUpdate()
    {
        if (!rig || !rig.head || !leftTarget || !rightTarget) return;
        // скорость ходьбы → размах рук
        if (playerRoot && Time.deltaTime > 0f)
        {
            Vector3 v = playerRoot.position - lastPos; v.y = 0f;
            speed = Mathf.Lerp(speed, v.magnitude / Time.deltaTime, 10f * Time.deltaTime);
            lastPos = playerRoot.position;
        }
        phase += Time.deltaTime * (2f + speed * 2.2f);
        float k = Mathf.Clamp01(speed / 4f) * swing;

        var cam = rig.head;
        var yaw = Quaternion.Euler(0f, cam.eulerAngles.y, 0f);
        Vector3 l = new Vector3(-restOffset.x, restOffset.y, restOffset.z + Mathf.Sin(phase) * k);
        Vector3 r = new Vector3(restOffset.x, restOffset.y, restOffset.z - Mathf.Sin(phase) * k);
        leftTarget.SetPositionAndRotation(cam.position + yaw * l, yaw);
        rightTarget.SetPositionAndRotation(cam.position + yaw * r, yaw);

        if (grabber && grabber.Held)
            rightTarget.position = grabber.Held.transform.position;
        // GorillaFinalRig решит IK сам в своём LateUpdate (он выполняется позже)
    }
}
