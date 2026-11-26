using UnityEngine;

/// <summary>
/// Процедурные звуки карты Банка (без внешних файлов): бесшовная петля работающего двигателя фургона.
/// Высоту тона меняет AudioSource.pitch — на холостых 1, при отъезде до ~1.9 («газ»).
/// </summary>
public static class BankAudio
{
    const int Rate = 22050;
    static AudioClip engineLoop;

    /// Двигатель на холостом ходу: пила 42 Гц + гармоника, «тарахтение» 7 Гц, приглушённый шум. Ровно 2 с — петля без щелчка.
    public static AudioClip EngineLoop()
    {
        if (engineLoop != null) return engineLoop;
        const float len = 2f;
        int n = (int)(Rate * len);
        var data = new float[n];
        var rnd = new System.Random(11);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float ph = t * 42f;                                    // 84 периода за 2 с → бесшовно
            float saw = 2f * (ph - Mathf.Floor(ph + 0.5f));
            float harm = Mathf.Sin(2 * Mathf.PI * 84f * t) * 0.35f;
            float chug = 0.65f + 0.35f * Mathf.Sin(2 * Mathf.PI * 7f * t); // 14 периодов за 2 с
            float noise = (float)(rnd.NextDouble() * 2 - 1);
            lp += (noise - lp) * 0.06f;
            data[i] = Mathf.Clamp((saw * 0.4f + harm + lp * 0.6f) * chug * 0.55f, -1f, 1f);
        }
        // сглаживаем стык петли (последние 200 сэмплов плавно переходят в начало)
        const int blend = 200;
        for (int i = 0; i < blend; i++)
        {
            float k = i / (float)blend;
            data[n - blend + i] = Mathf.Lerp(data[n - blend + i], data[i], k);
        }
        engineLoop = AudioClip.Create("BankEngineLoop", n, 1, Rate, false);
        engineLoop.SetData(data, 0);
        return engineLoop;
    }
}
