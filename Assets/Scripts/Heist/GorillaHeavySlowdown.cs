using UnityEngine;

/// <summary>
/// Замедляет Gorilla-передвижение, пока игрок держит тяжёлый лут (VRGrabber.SpeedMultiplier = 0.6 → −40%).
/// Не меняет сам GorillaLocomotion.Player — только подкручивает его силу/предел прыжка.
/// </summary>
[RequireComponent(typeof(GorillaLocomotion.Player))]
public class GorillaHeavySlowdown : MonoBehaviour
{
    GorillaLocomotion.Player player;
    float baseMaxJumpSpeed, baseJumpMultiplier;

    void Awake()
    {
        player = GetComponent<GorillaLocomotion.Player>();
        baseMaxJumpSpeed = player.maxJumpSpeed;
        baseJumpMultiplier = player.jumpMultiplier;
    }

    void Update()
    {
        float k = VRGrabber.SpeedMultiplier;
        player.maxJumpSpeed = baseMaxJumpSpeed * k;
        player.jumpMultiplier = baseJumpMultiplier * k;
    }

    void OnDisable()
    {
        if (player == null) return;
        player.maxJumpSpeed = baseMaxJumpSpeed;
        player.jumpMultiplier = baseJumpMultiplier;
    }
}
