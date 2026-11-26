using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Зона эвакуации (кузов фургона): принимает предметы с тегом Loot — начисляет деньги в общий котёл, играет звуки, прячет предмет.
/// В сети предмет засчитывает только тот, кто считает его физику (StateAuthority), — деньги уходят хосту комнаты (HeistNetworkState),
/// а предмет пропадает и звенит у всех игроков.
/// Нужен BoxCollider с isTrigger на этом же объекте (слой Ignore Raycast, чтобы руки GorillaLocomotion не упирались в объём).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class EvacuationZone : MonoBehaviour
{
    [Header("Звуки сдачи лута (Kenney CC0). Пусто = процедурный звон")]
    public AudioClip[] depositClips;
    public AudioClip[] coinClips;
    public AudioClip[] heavyClips;

    static readonly List<EvacuationZone> All = new List<EvacuationZone>();

    AudioSource audioSource;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0.6f;
    }

    void OnTriggerEnter(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (rb == null || !rb.CompareTag("Loot")) return;
        var item = rb.GetComponent<LootItem>();
        if (item == null || item.IsHidden || !item.HasAuthority) return;

        var gm = HeistGameManager.Instance;
        if (gm == null || !gm.IsPlaying)
        {
            if (gm != null) gm.PostEvent("СНАЧАЛА НАЖМИ «СТАРТ»!");
            return;
        }

        gm.AddLoot(item);
        item.MarkDeposited(); // звук сыграет LootItem у всех игроков (PlayDepositNear)
    }

    /// Звук сдачи лута в ближайшем фургоне (вызывается у каждого игрока, когда предмет стал «сдан»).
    public static void PlayDepositNear(LootItem item)
    {
        EvacuationZone best = null; float bestD = float.MaxValue;
        foreach (var z in All)
        {
            float d = (z.transform.position - item.transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = z; }
        }
        if (best != null) best.PlayDepositSound(item);
    }

    void PlayDepositSound(LootItem item)
    {
        bool any = false;
        any |= PlayRandom(depositClips, 0.9f);
        any |= PlayRandom(item.isHeavy && heavyClips != null && heavyClips.Length > 0 ? heavyClips : coinClips, 1f);
        if (!any) audioSource.PlayOneShot(ProceduralAudio.Coin());
    }

    bool PlayRandom(AudioClip[] clips, float volume)
    {
        if (clips == null || clips.Length == 0) return false;
        audioSource.PlayOneShot(clips[Random.Range(0, clips.Length)], volume);
        return true;
    }
}
