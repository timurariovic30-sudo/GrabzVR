using UnityEngine;

/// <summary>Процедурные звуки полигона (без файлов): гул дрели, лязг удара, щелчок рубильника, треск шокера.</summary>
public static class GymAudio
{
    static AudioClip drill, clang, flip, zap;
    const int Rate = 22050;

    public static AudioClip DrillLoop => drill ??= Make("GymDrill", 1f, (t, r) =>
        0.35f * Saw(t * 155f) + 0.2f * Saw(t * 312f) + 0.12f * (float)(r.NextDouble() * 2 - 1));

    public static AudioClip Clang => clang ??= Make("GymClang", 0.45f, (t, r) =>
        Mathf.Exp(-t * 9f) * (0.5f * Mathf.Sin(t * 2 * Mathf.PI * 820f) + 0.35f * Mathf.Sin(t * 2 * Mathf.PI * 1370f) + 0.3f * (float)(r.NextDouble() * 2 - 1) * Mathf.Exp(-t * 40f)));

    public static AudioClip Flip => flip ??= Make("GymFlip", 0.12f, (t, r) =>
        Mathf.Exp(-t * 60f) * (0.7f * (float)(r.NextDouble() * 2 - 1) + 0.4f * Mathf.Sin(t * 2 * Mathf.PI * 180f)));

    public static AudioClip Zap => zap ??= Make("GymZap", 0.6f, (t, r) =>
        (r.NextDouble() < 0.5 ? 1f : -1f) * 0.45f * Mathf.Abs(Mathf.Sin(t * 2 * Mathf.PI * 60f)) * (1f - t / 0.6f));

    static float Saw(float x) => 2f * (x - Mathf.Floor(x + 0.5f));

    static AudioClip Make(string name, float seconds, System.Func<float, System.Random, float> f)
    {
        int n = Mathf.CeilToInt(seconds * Rate);
        var data = new float[n]; var r = new System.Random(name.GetHashCode());
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate, r), -1f, 1f);
        var c = AudioClip.Create(name, n, 1, Rate, false); c.SetData(data, 0); return c;
    }
}
