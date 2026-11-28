using TMPro;
using UnityEngine;

/// <summary>
/// Стенд подкрутки физики в TestGym: 3D-кнопки ± для силы прыжка, скорости и гравитации, СОХРАНИТЬ и СБРОС.
/// Значения сразу применяются к локальному VR-игроку (GorillaLocomotion.Player) — у каждого игрока свои.
/// СОХРАНИТЬ пишет их в PlayerPrefs: VR-игрок берёт их при старте в любой сцене (Player.applySavedTuning).
/// Кнопки — PushButton, их onPressed подключает билдер (TestGymBuilder).
/// </summary>
public class LocomotionTuningStand : MonoBehaviour
{
    public TextMeshPro jumpText, speedText, gravityText, statusText;

    float jump = LocomotionTuning.DefaultJump, speed = LocomotionTuning.DefaultSpeed, gravity = LocomotionTuning.DefaultGravity;
    float statusUntil;

    static GorillaLocomotion.Player P => GorillaLocomotion.Player.Instance;

    void Start()
    {
        var p = P;
        if (p != null) LocomotionTuning.Get(p, out jump, out speed, out gravity);
        Refresh(LocomotionTuning.HasSaved ? "ЗАГРУЖЕНЫ СОХРАНЁННЫЕ" : "ЗНАЧЕНИЯ ПО УМОЛЧАНИЮ");
    }

    public void JumpUp() => Set(ref jump, +LocomotionTuning.JumpStep, LocomotionTuning.JumpMin, LocomotionTuning.JumpMax, LocomotionTuning.JumpStep);
    public void JumpDown() => Set(ref jump, -LocomotionTuning.JumpStep, LocomotionTuning.JumpMin, LocomotionTuning.JumpMax, LocomotionTuning.JumpStep);
    public void SpeedUp() => Set(ref speed, +LocomotionTuning.SpeedStep, LocomotionTuning.SpeedMin, LocomotionTuning.SpeedMax, LocomotionTuning.SpeedStep);
    public void SpeedDown() => Set(ref speed, -LocomotionTuning.SpeedStep, LocomotionTuning.SpeedMin, LocomotionTuning.SpeedMax, LocomotionTuning.SpeedStep);
    public void GravityUp() => Set(ref gravity, +LocomotionTuning.GravityStep, LocomotionTuning.GravityMin, LocomotionTuning.GravityMax, LocomotionTuning.GravityStep);
    public void GravityDown() => Set(ref gravity, -LocomotionTuning.GravityStep, LocomotionTuning.GravityMin, LocomotionTuning.GravityMax, LocomotionTuning.GravityStep);

    public void SaveValues()
    {
        Push();
        // ПК-режим тоже сохраняет: VR-игрок возьмёт числа при следующем старте
        LocomotionTuning.Save(jump, speed, gravity);
        Refresh("СОХРАНЕНО!");
    }

    public void ResetValues()
    {
        LocomotionTuning.ResetToDefaults(P);
        jump = LocomotionTuning.DefaultJump; speed = LocomotionTuning.DefaultSpeed; gravity = LocomotionTuning.DefaultGravity;
        Push(); Refresh("СБРОС К ЭТАЛОНУ");
    }

    public float Jump => jump;
    public float Speed => speed;
    public float Gravity => gravity;

    void Set(ref float v, float delta, float min, float max, float step)
    {
        v = LocomotionTuning.Clamp(v + delta, min, max, step);
        Push(); Refresh(P != null ? "ПРИМЕНЕНО — ПРОБУЙ!" : "VR-ИГРОК НЕ АКТИВЕН (ПК)");
    }

    void Push()
    {
        LocomotionTuning.Set(P, jump, speed, gravity);
    }

    void Refresh(string status)
    {
        if (jumpText) jumpText.text = $"СИЛА ПРЫЖКА\n<size=150%>×{jump:0.00}</size>";
        if (speedText) speedText.text = $"СКОРОСТЬ\n<size=150%>{speed:0.0} м/с</size>";
        if (gravityText) gravityText.text = $"ГРАВИТАЦИЯ\n<size=150%>{gravity:0.0}g</size>";
        if (statusText) statusText.text = status;
        statusUntil = Time.time + 3f;
    }

    void Update()
    {
        if (statusText && statusUntil > 0f && Time.time > statusUntil)
        {
            statusUntil = 0f;
            statusText.text = LocomotionTuning.HasSaved ? "СОХРАНЕНО НА ЭТОМ УСТРОЙСТВЕ" : "НЕ СОХРАНЕНО";
        }
    }
}
