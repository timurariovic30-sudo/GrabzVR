using UnityEngine;

/// <summary>
/// Живой огонь костра в Хабе: свет мягко мерцает (яркость и радиус), угли «дышат» эмиссией.
/// Дёшево для Quest: один источник света, без теней, только Perlin-шум.
/// </summary>
public class CampfireFlicker : MonoBehaviour
{
    public Light fireLight;
    public Renderer[] emberRenderers;
    public float baseIntensity = 2.2f;
    [Range(0f, 1f)] public float flicker = 0.28f;
    public float speed = 6f;
    public Color emberColor = new Color(1f, 0.42f, 0.08f);

    MaterialPropertyBlock mpb;
    float seed;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    void Awake() { mpb = new MaterialPropertyBlock(); seed = Random.value * 100f; }

    void Update()
    {
        float t = Time.time * speed + seed;
        float n = Mathf.PerlinNoise(t, 0.37f) * 0.7f + Mathf.PerlinNoise(t * 2.3f, 1.7f) * 0.3f;   // 0…1
        float k = 1f + (n - 0.5f) * 2f * flicker;
        if (fireLight) { fireLight.intensity = baseIntensity * k; fireLight.range = 8f + (k - 1f) * 1.5f; }
        if (emberRenderers == null) return;
        mpb.SetColor(EmissionId, emberColor * (1.6f + 1.2f * n));
        foreach (var r in emberRenderers) if (r) r.SetPropertyBlock(mpb);
    }
}
