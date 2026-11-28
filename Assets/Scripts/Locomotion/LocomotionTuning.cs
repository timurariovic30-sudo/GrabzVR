using UnityEngine;

/// <summary>
/// Подкрутка физики передвижения GorillaLocomotion на лету (стенд в TestGym) и сохранение на устройстве (PlayerPrefs).
/// Три ручки: сила прыжка (jumpMultiplier), скорость (maxJumpSpeed — предел скорости толчка) и гравитация игрока (gravityScale).
/// Прыжок и скорость задаются как БАЗОВЫЕ значения GorillaHeavySlowdown (он каждый кадр умножает их на замедление
/// от тяжёлого лута), без него — прямо в Player. RESET возвращает эталон и стирает сохранение.
/// </summary>
public static class LocomotionTuning
{
    public const float DefaultJump = 1.3f, DefaultSpeed = 8f, DefaultGravity = 1f;
    public const float JumpStep = 0.05f, SpeedStep = 0.5f, GravityStep = 0.1f;
    public const float JumpMin = 1f, JumpMax = 2f, SpeedMin = 5f, SpeedMax = 12f, GravityMin = 0.5f, GravityMax = 1.5f;
    const string KJump = "grabz.loco.jump", KSpeed = "grabz.loco.speed", KGravity = "grabz.loco.gravity";

    public static bool HasSaved => PlayerPrefs.HasKey(KJump);

    /// Ограничить значение диапазоном ручки (для тестов и кнопок).
    public static float Clamp(float v, float min, float max, float step) => Mathf.Clamp(Mathf.Round(v / step) * step, min, max);

    /// Текущие базовые значения игрока (без замедления от груза).
    public static void Get(GorillaLocomotion.Player p, out float jump, out float speed, out float gravity)
    {
        var slow = p.GetComponent<GorillaHeavySlowdown>();
        jump = slow ? slow.BaseJumpMultiplier : p.jumpMultiplier;
        speed = slow ? slow.BaseMaxJumpSpeed : p.maxJumpSpeed;
        gravity = p.gravityScale;
    }

    public static void Set(GorillaLocomotion.Player p, float jump, float speed, float gravity)
    {
        if (p == null) return;
        p.gravityScale = gravity;
        var slow = p.GetComponent<GorillaHeavySlowdown>();
        if (slow) slow.SetBase(jump, speed);
        else { p.jumpMultiplier = jump; p.maxJumpSpeed = speed; }
    }

    public static void ApplySaved(GorillaLocomotion.Player p)
    {
        if (p == null || !HasSaved) return;
        Get(p, out var j, out var s, out var g);
        Set(p, PlayerPrefs.GetFloat(KJump, j), PlayerPrefs.GetFloat(KSpeed, s), PlayerPrefs.GetFloat(KGravity, g));
    }

    public static void Save(GorillaLocomotion.Player p)
    {
        if (p == null) return;
        Get(p, out var j, out var s, out var g); Save(j, s, g);
    }

    public static void Save(float jump, float speed, float gravity)
    {
        PlayerPrefs.SetFloat(KJump, jump);
        PlayerPrefs.SetFloat(KSpeed, speed);
        PlayerPrefs.SetFloat(KGravity, gravity);
        PlayerPrefs.Save();
        Debug.Log($"[LocomotionTuning] сохранено: прыжок ×{jump:0.00}, скорость {speed:0.0} м/с, гравитация {gravity:0.00}g");
    }

    public static void ResetToDefaults(GorillaLocomotion.Player p)
    {
        PlayerPrefs.DeleteKey(KJump); PlayerPrefs.DeleteKey(KSpeed); PlayerPrefs.DeleteKey(KGravity); PlayerPrefs.Save();
        Set(p, DefaultJump, DefaultSpeed, DefaultGravity);
    }
}
