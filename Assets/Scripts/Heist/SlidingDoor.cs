using System.Collections;
using UnityEngine;

/// <summary>
/// Двигающаяся дверь/решётка: створки плавно уезжают на заданное смещение (локально) и возвращаются.
/// Используется для шлюза Убежища (AirlockDoor) и решётки Хранилища.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class SlidingDoor : MonoBehaviour
{
    [System.Serializable]
    public struct Leaf { public Transform part; public Vector3 openOffset; }

    public Leaf[] leaves;
    public float duration = 1.4f;
    public bool startOpen;
    public AudioClip[] openClips;
    public AudioClip[] closeClips;
    [Tooltip("Лампа над дверью: красная — закрыто, зелёная — открыто")]
    public Renderer statusLamp;

    public bool IsOpen { get; private set; }
    Vector3[] closedPos;
    AudioSource source;
    MaterialPropertyBlock block;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        closedPos = new Vector3[leaves.Length];
        for (int i = 0; i < leaves.Length; i++) if (leaves[i].part) closedPos[i] = leaves[i].part.localPosition;
        IsOpen = startOpen;
        Apply(startOpen ? 1f : 0f);
    }

    public void Open() { if (!IsOpen) StartMove(true); }
    public void Close() { if (IsOpen) StartMove(false); }
    public void Toggle() => StartMove(!IsOpen);

    void StartMove(bool open)
    {
        IsOpen = open;
        StopAllCoroutines();
        StartCoroutine(Move(open));
        var clips = open ? openClips : closeClips;
        if (clips != null && clips.Length > 0) source.PlayOneShot(clips[Random.Range(0, clips.Length)]);
    }

    IEnumerator Move(bool open)
    {
        float from = CurrentT(), to = open ? 1f : 0f;
        for (float t = 0; t < 1f; t += Time.deltaTime / duration)
        {
            Apply(Mathf.SmoothStep(from, to, t));
            yield return null;
        }
        Apply(to);
    }

    float lastT;
    float CurrentT() => lastT;

    void Apply(float t)
    {
        lastT = t;
        for (int i = 0; i < leaves.Length; i++)
            if (leaves[i].part) leaves[i].part.localPosition = closedPos[i] + leaves[i].openOffset * t;
        if (statusLamp)
        {
            block ??= new MaterialPropertyBlock();
            statusLamp.GetPropertyBlock(block);
            var c = t > 0.5f ? new Color(0.2f, 1f, 0.2f) : new Color(1f, 0.1f, 0.05f);
            block.SetColor("_BaseColor", c);
            block.SetColor("_EmissionColor", c * 2f);
            statusLamp.SetPropertyBlock(block);
        }
    }
}
