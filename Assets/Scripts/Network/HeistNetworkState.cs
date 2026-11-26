using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// Единое сетевое состояние ограбления (Photon Fusion 2, Shared) — объект сцены HeistLevel_Test.
/// NetworkObject с флагом Master Client Object: StateAuthority всегда у мастер-клиента (хоста комнаты),
/// при его выходе власть сама переходит к новому мастеру.
///  • [Networked]: раунд (Lobby/Playing/Won/Lost), таймер, общий котёл, режим, тревога, разбитые стёкла, последнее событие для табло.
///  • Остальные игроки шлют просьбы хосту через RPC (старт, сдал лут, тревога, стекло, сброс лута, событие).
///  • Render у каждого: событие на табло, осколки стёкол, сброс лута.
/// Без сети (HeistNetworkState.Instance == null) HeistGameManager / AlarmSystem / BreakableGlass работают локально, как раньше.
/// </summary>
public class HeistNetworkState : NetworkBehaviour
{
    public static HeistNetworkState Instance { get; private set; }

    [Networked] public int RoundState { get; set; }
    [Networked] public float TimeLeft { get; set; }
    [Networked] public int Loot { get; set; }
    [Networked] public int Mode { get; set; }
    [Networked] public float AlarmRemaining { get; set; }
    [Networked] public NetworkBool ManualAlarm { get; set; }
    [Networked] public int GlassBroken { get; set; }
    [Networked] public NetworkString<_64> EventText { get; set; }
    [Networked] public int EventSeq { get; set; }

    public bool AlarmActive => ManualAlarm || AlarmRemaining > 0f;

    int shownEventSeq;
    int shownGlassMask;

    public override void Spawned()
    {
        Instance = this;
        shownEventSeq = EventSeq;
        var gm = HeistGameManager.Instance;
        if (HasStateAuthority)
        {
            // первый в комнате (или уровень загружен до подключения): переносим локальное состояние в сеть
            if (gm != null) gm.PushLocalToNetwork(this);
            if (AlarmSystem.Instance != null) AlarmSystem.Instance.PushLocalToNetwork(this);
            GlassBroken = BreakableGlass.LocalBrokenMask();
            shownGlassMask = GlassBroken;
        }
        else
        {
            // новичок в комнате: стёкла как у всех
            shownGlassMask = GlassBroken;
            BreakableGlass.ApplyMask(GlassBroken, withEffects: false);
        }
        Debug.Log($"[HeistNet] state spawned, authority={HasStateAuthority}, round={RoundState}, loot={Loot}");
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }

    public override void Render()
    {
        if (EventSeq != shownEventSeq)
        {
            shownEventSeq = EventSeq;
            var gm = HeistGameManager.Instance;
            if (gm != null) gm.ShowEvent(EventText.ToString());
        }
        int mask = GlassBroken;
        if (mask != shownGlassMask)
        {
            BreakableGlass.ApplyMaskChange(shownGlassMask, mask);
            shownGlassMask = mask;
        }
    }

    // ------------------------------------------------------------------ события на табло
    /// Только у хоста: событие видят все игроки.
    public void PublishEvent(string text)
    {
        if (!HasStateAuthority) return;
        if (text.Length > 60) text = text.Substring(0, 60);
        EventText = text;
        EventSeq++;
        shownEventSeq = EventSeq; // у хоста оно уже показано локально
    }

    // ------------------------------------------------------------------ просьбы к хосту
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RpcStartHeist()
    {
        var gm = HeistGameManager.Instance;
        if (gm != null) gm.StartHeist();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RpcAddLoot(int value, string itemName)
    {
        var gm = HeistGameManager.Instance;
        if (gm != null) gm.AddLoot(value, itemName);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RpcIntrusion(string reason)
    {
        if (AlarmSystem.Instance != null) AlarmSystem.Instance.TriggerIntrusion(reason);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RpcToggleAlarm()
    {
        if (AlarmSystem.Instance != null) AlarmSystem.Instance.ToggleManual();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RpcAnnounce(string text)
    {
        var gm = HeistGameManager.Instance;
        if (gm != null) gm.Announce(text);
    }

    // ------------------------------------------------------------------ стёкла
    /// Стекло разбили у этого игрока: хосту — отметить в маске (и включить тревогу).
    public void RequestBreakGlass(int index)
    {
        if (index < 0 || index >= 32) return;
        if (HasStateAuthority) BreakGlassAuthority(index);
        else RpcBreakGlass(index);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RpcBreakGlass(int index) => BreakGlassAuthority(index);

    void BreakGlassAuthority(int index)
    {
        int bit = 1 << index;
        if ((GlassBroken & bit) != 0) return;
        GlassBroken |= bit;
        var glass = BreakableGlass.Sorted();
        if (index < glass.Count && glass[index].triggersAlarm && AlarmSystem.Instance != null)
            AlarmSystem.Instance.TriggerIntrusion("СТЕКЛО");
    }

    // ------------------------------------------------------------------ сброс лута
    public void RequestResetLoot()
    {
        if (HasStateAuthority) ResetLootAuthority();
        else RpcResetLoot();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RpcResetLoot() => ResetLootAuthority();

    void ResetLootAuthority()
    {
        GlassBroken = 0;
        var gm = HeistGameManager.Instance;
        if (gm != null) gm.ResetScore();
        RpcResetLootAll();
    }

    /// У каждого игрока: свой лут (где он хозяин) — на место; ничейный забирает мастер-клиент.
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RpcResetLootAll()
    {
        bool master = Runner != null && Runner.IsSharedModeMasterClient;
        var items = new List<LootItem>(FindObjectsByType<LootItem>(FindObjectsInactive.Exclude));
        foreach (var item in items)
        {
            if (item.Net != null) item.Net.ResetFromNetwork(master);
            else item.ResetToSpawn();
        }
    }
}
