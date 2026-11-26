using Fusion;
using UnityEngine;

/// <summary>
/// Сетевая часть лута (Photon Fusion 2, Shared). Стоит рядом с LootItem + NetworkObject + NetworkTransform на корне префаба.
///  • Взял предмет → RequestStateAuthority(): физику предмета теперь считает взявший игрок,
///    остальные видят движение и бросок через NetworkTransform (у них тело кинематическое).
///  • IsGrabbed / HeldBy: пока предмет держит другой игрок, взять его нельзя (один слиток не раздвоится).
///    Если двое схватили одновременно, побеждает последний запрос власти, а проигравший сам разжимает руку.
///  • StateValue: Available / Broken / Deposited — разбитая ваза и сданный лут пропадают сразу у всех.
///  • ResetNetworked: возврат на исходное место (тестовая кнопка «СБРОСИТЬ ВЕСЬ ЛУТ»).
/// Флаги NetworkObject: Allow State Authority Override = вкл, Master Client Object = выкл,
/// Destroy When State Authority Leaves = выкл (лут остаётся, если игрок вышел).
/// </summary>
[RequireComponent(typeof(LootItem))]
public class NetworkLootItem : NetworkBehaviour, IStateAuthorityChanged
{
    [Networked] public NetworkBool IsGrabbed { get; set; }
    [Networked] public PlayerRef HeldBy { get; set; }
    [Networked] public NetworkBool WasGrabbed { get; set; }
    [Networked] public int StateValue { get; set; }

    public bool IsSpawned { get; private set; }

    /// Предмет сейчас в руках другого игрока (и тот всё ещё им владеет).
    public bool IsHeldByOther => IsSpawned && IsGrabbed && HeldBy != Runner.LocalPlayer && Object.StateAuthority == HeldBy;

    LootItem item;
    NetworkTransform netTransform;
    bool pendingGrab, pendingReset;
    Vector3 resetPos; Quaternion resetRot;

    void Awake()
    {
        item = GetComponent<LootItem>();
        netTransform = GetComponent<NetworkTransform>();
    }

    public override void Spawned() => IsSpawned = true;

    public override void Despawned(NetworkRunner runner, bool hasState) => IsSpawned = false;

    // ------------------------------------------------------------------ хват
    public void LocalGrab()
    {
        if (!IsSpawned) return;
        if (HasStateAuthority) MarkGrabbed();
        else { pendingGrab = true; Object.RequestStateAuthority(); }
    }

    public void LocalRelease()
    {
        pendingGrab = false;
        if (IsSpawned && HasStateAuthority && HeldBy == Runner.LocalPlayer)
        {
            IsGrabbed = false;
            HeldBy = PlayerRef.None;
        }
    }

    void MarkGrabbed()
    {
        pendingGrab = false;
        IsGrabbed = true;
        HeldBy = Runner.LocalPlayer;
        WasGrabbed = true;
        if (item.Body.isKinematic) item.Body.isKinematic = false;
    }

    public void StateAuthorityChanged()
    {
        if (!IsSpawned) return;
        if (HasStateAuthority)
        {
            if (pendingReset) { pendingReset = false; ResetNetworked(resetPos, resetRot); }
            if (pendingGrab && item.Holder != null && !item.IsHidden) MarkGrabbed();
            else if (pendingGrab) { pendingGrab = false; if (HeldBy != PlayerRef.None && HeldBy != Runner.LocalPlayer) { IsGrabbed = false; HeldBy = PlayerRef.None; } }
        }
        else
        {
            // предмет перехватил другой игрок — разжимаем свою руку
            pendingGrab = false;
            item.ForceLocalRelease();
        }
    }

    // ------------------------------------------------------------------ состояние
    public void SetState(LootItem.LootState st)
    {
        if (!IsSpawned || !HasStateAuthority) return;
        StateValue = (int)st;
        IsGrabbed = false;
        HeldBy = PlayerRef.None;
    }

    public void ResetNetworked(Vector3 pos, Quaternion rot)
    {
        if (!IsSpawned) return;
        if (!HasStateAuthority)
        {
            // хозяина нет (игрок вышел) — забираем власть и сбрасываем после её получения
            resetPos = pos; resetRot = rot; pendingReset = true;
            Object.RequestStateAuthority();
            return;
        }
        StateValue = (int)LootItem.LootState.Available;
        WasGrabbed = false;
        IsGrabbed = false;
        HeldBy = PlayerRef.None;
        var body = item.Body;
        if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        body.isKinematic = item.kinematicUntilGrabbed;
        body.position = pos; body.rotation = rot;
        transform.SetPositionAndRotation(pos, rot);
        if (netTransform) netTransform.Teleport(pos, rot);
    }

    /// Сброс по команде сети (RPC от HeistNetworkState): предмет сбрасывает его хозяин,
    /// а «ничейный» предмет (хозяин вышел) забирает и сбрасывает мастер-клиент.
    public void ResetFromNetwork(bool isMasterClient)
    {
        if (!IsSpawned) { item.ResetToSpawn(); return; }
        if (HasStateAuthority) item.ResetToSpawn();
        else if (isMasterClient && Object.StateAuthority == PlayerRef.None) item.RequestReset();
        else item.ForceLocalRelease();
    }
}
