using UnityEngine;

/// <summary>Звуки, сгенерированные кодом (без аудиофайлов).</summary>
public static class ProceduralAudio
{
    const int Rate = 44100;
    static AudioClip coin, siren, beep, engine, keyClick;

    /// Механический «клик» клавиши: резкий щелчок (шум + высокий тон, 8 мс) и глухой «тук» дна (через 25 мс).
    public static AudioClip KeyClick()
    {
        if (keyClick != null) return keyClick;
        var data = new float[(int)(Rate * 0.07f)];
        var rnd = new System.Random(7);
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            float click = t < 0.008f ? ((float)rnd.NextDouble() * 2f - 1f) * 0.5f * (1f - t / 0.008f) + Mathf.Sin(2 * Mathf.PI * 3800f * t) * 0.25f * (1f - t / 0.008f) : 0f;
            float bt = t - 0.025f;
            float thock = bt > 0f ? Mathf.Sin(2 * Mathf.PI * 520f * bt) * 0.35f * Mathf.Exp(-bt / 0.008f) + ((float)rnd.NextDouble() * 2f - 1f) * 0.12f * Mathf.Exp(-bt / 0.004f) : 0f;
            data[i] = click + thock;
        }
        keyClick = AudioClip.Create("ProceduralKeyClick", data.Length, 1, Rate, false);
        keyClick.SetData(data, 0);
        return keyClick;
    }

    /// Короткий писк тревоги: 0.18 с тона + 0.32 с тишины (клип зациклен → «пи… пи… пи…»).
    public static AudioClip AlarmBeep()
    {
        if (beep != null) return beep;
        float len = 0.5f, on = 0.18f;
        var data = new float[(int)(Rate * len)];
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            if (t > on) break;
            float env = Mathf.Clamp01(t / 0.005f) * Mathf.Clamp01((on - t) / 0.02f); // без щелчков
            data[i] = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 1250f * t)) * 0.18f * env;
        }
        beep = AudioClip.Create("ProceduralAlarmBeep", data.Length, 1, Rate, false);
        beep.SetData(data, 0);
        return beep;
    }

    /// Зажигание двигателя фургона: стартер «чух-чух» (~1 с), затем нарастающий рокот (~2 с).
    public static AudioClip EngineStart()
    {
        if (engine != null) return engine;
        float len = 3.2f;
        var data = new float[(int)(Rate * len)];
        var rnd = new System.Random(7);
        float phase = 0f, lp = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            float noise = (float)(rnd.NextDouble() * 2 - 1);
            lp += (noise - lp) * 0.08f; // приглушённый шум
            float v;
            if (t < 1.1f)
            {
                // стартер: шумовые толчки 9 раз в секунду + низкий гул
                float crank = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * 9f * t)), 6f);
                v = (lp * 1.6f + Mathf.Sin(2 * Mathf.PI * 70f * t) * 0.3f) * crank * 0.5f;
            }
            else
            {
                // мотор завёлся: пила 38→55 Гц с «тарахтением»
                float k = Mathf.Clamp01((t - 1.1f) / 0.4f);
                float f = Mathf.Lerp(38f, 55f, Mathf.Clamp01((t - 1.1f) / 2f));
                phase += f / Rate;
                float saw = 2f * (phase - Mathf.Floor(phase + 0.5f));
                float rumble = 0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * f * 0.5f * t);
                v = (saw * 0.45f + lp * 0.5f) * rumble * k;
            }
            float fadeOut = Mathf.Clamp01((len - t) / 0.3f);
            data[i] = Mathf.Clamp(v * fadeOut, -1f, 1f) * 0.8f;
        }
        engine = AudioClip.Create("ProceduralEngineStart", data.Length, 1, Rate, false);
        engine.SetData(data, 0);
        return engine;
    }

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
