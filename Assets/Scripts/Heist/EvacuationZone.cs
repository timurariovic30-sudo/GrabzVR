using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Зона эвакуации (кузов фургона): принимает предметы с тегом Loot — начисляет деньги, играет звуки, удаляет предмет.
/// Нужен BoxCollider с isTrigger на этом же объекте (слой Ignore Raycast, чтобы руки GorillaLocomotion не упирались в объём).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class EvacuationZone : MonoBehaviour
{
    [Header("Звуки сдачи лута (Kenney CC0). Пусто = процедурный звон")]
    public AudioClip[] depositClips;
    public AudioClip[] coinClips;
    public AudioClip[] heavyClips;

    AudioSource audioSource;
    readonly HashSet<LootItem> collected = new HashSet<LootItem>();

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
        if (item == null || item.IsBroken || !collected.Add(item)) return;

        var gm = HeistGameManager.Instance;
        if (gm == null || !gm.IsPlaying)
        {
            collected.Remove(item);
            if (gm != null) gm.PostEvent("СНАЧАЛА НАЖМИ «СТАРТ»!");
            return;
        }

        gm.AddLoot(item);
        PlayDepositSound(item);
        if (item.Holder != null) item.Holder.Release();
        Destroy(item.gameObject);
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
