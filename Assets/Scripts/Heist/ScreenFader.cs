using System.Collections;
using UnityEngine;

/// <summary>
/// Плавное затемнение экрана (работает и в VR): чёрный полупрозрачный квад прямо перед активной камерой.
/// Материал — прозрачный Unlit (M_ScreenFade), альфа меняется через MaterialPropertyBlock.
/// </summary>
public static class ScreenFader
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public static IEnumerator FadeOut(Material mat, float time)
    {
        var cam = Camera.main;
        if (!cam || !mat) { yield return new WaitForSeconds(time); yield break; }

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "ScreenFade";
        Object.Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(cam.transform, false);
        quad.transform.localPosition = new Vector3(0, 0, cam.nearClipPlane + 0.02f);
        quad.transform.localScale = Vector3.one * 2f;
        quad.layer = 0;
        var r = quad.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var block = new MaterialPropertyBlock();

        for (float t = 0; t < time; t += Time.unscaledDeltaTime)
        {
            block.SetColor(BaseColorId, new Color(0, 0, 0, t / time));
            r.SetPropertyBlock(block);
            yield return null;
        }
        block.SetColor(BaseColorId, Color.black);
        r.SetPropertyBlock(block);
    }
}
