using UnityEngine;

/// <summary>Звуки, сгенерированные кодом (без аудиофайлов).</summary>
public static class ProceduralAudio
{
    const int Rate = 44100;
    static AudioClip coin, siren;

    /// Звон монет: несколько коротких «дзынь» с затуханием.
    public static AudioClip Coin()
    {
        if (coin != null) return coin;
        float len = 0.7f;
        var data = new float[(int)(Rate * len)];
        // (время старта, основная частота)
        var pings = new[] { (0.00f, 1975f), (0.07f, 2637f), (0.15f, 3136f), (0.24f, 2637f) };
        foreach (var (start, f) in pings)
        {
            int s0 = (int)(start * Rate);
            for (int i = s0; i < data.Length; i++)
            {
                float t = (i - s0) / (float)Rate;
                float env = Mathf.Exp(-t * 14f);
                float v = Mathf.Sin(2 * Mathf.PI * f * t) * 0.6f
                        + Mathf.Sin(2 * Mathf.PI * f * 2.76f * t) * 0.25f * Mathf.Exp(-t * 30f); // металлический обертон
                data[i] += v * env * 0.35f;
            }
        }
        coin = AudioClip.Create("ProceduralCoin", data.Length, 1, Rate, false);
        coin.SetData(data, 0);
        return coin;
    }

    /// Сирена-пищалка: чередование двух тонов (прямоугольная волна), удобно зациклить.
    public static AudioClip Siren()
    {
        if (siren != null) return siren;
        float len = 0.5f; // 2 тона по 0.25 c
        var data = new float[(int)(Rate * len)];
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            float f = t < 0.25f ? 1400f : 950f;
            float sq = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * f * t));
            data[i] = sq * 0.18f;
        }
        siren = AudioClip.Create("ProceduralSiren", data.Length, 1, Rate, false);
        siren.SetData(data, 0);
        return siren;
    }
}
