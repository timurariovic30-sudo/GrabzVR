using UnityEngine;

/// <summary>
/// Действия тестового пульта в Убежище (их вызывают физические кнопки PushButton):
/// старт ограбления, тревога вкл/выкл, сброс лута, телепорт Хаб ⇄ Хранилище.
/// </summary>
public class HeistDebugActions : MonoBehaviour
{
    public PlayerModeSwitcher switcher;
    public LootRegistry lootRegistry;
    public Transform hubPoint;
    public Transform vaultPoint;
    [Tooltip("Граница Убежища по Z: игрок южнее неё считается «в Хабе»")]
    public float hubMaxZ = 0f;

    public void StartHeist()
    {
        if (HeistGameManager.Instance) HeistGameManager.Instance.StartHeist();
    }

    public void ToggleAlarm()
    {
        if (AlarmSystem.Instance) AlarmSystem.Instance.ToggleManual();
    }

    public void ResetLoot()
    {
        if (lootRegistry) lootRegistry.ResetAllLoot();
    }

    /// Из Хаба — в Хранилище, из любого другого места — обратно в Хаб.
    public void TeleportToggle()
    {
        if (!switcher) return;
        var pos = switcher.ActivePlayerPosition();
        var target = pos.z < hubMaxZ ? vaultPoint : hubPoint;
        if (target) switcher.TeleportActive(target.position, target.eulerAngles.y);
        if (HeistGameManager.Instance) HeistGameManager.Instance.PostEvent(target == vaultPoint ? "ТЕЛЕПОРТ: ХРАНИЛИЩЕ" : "ТЕЛЕПОРТ: ХАБ");
    }
}
