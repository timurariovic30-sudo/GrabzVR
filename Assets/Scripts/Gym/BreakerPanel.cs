using Fusion;
using TMPro;
using UnityEngine;

/// <summary>
/// Щиток с тремя рубильниками (Photon Fusion 2, Shared; объект сцены с Master Client Object).
/// Каждый рубильник — PushButton, переключает свой бит ([Networked] Mask, бит = рубильник опущен).
/// Все три опущены — питание лазеров отключено (решётки LaserAlarm гаснут у всех). Без сети — локально.
/// </summary>
public class BreakerPanel : NetworkBehaviour
{
    public Transform[] levers = new Transform[3];
    public Renderer[] lamps = new Renderer[3];
    public GameObject[] poweredObjects;
    public TextMeshPro status;
    public AudioSource fx;
    public float leverUpAngle = -35f, leverDownAngle = 35f;

    [Networked] public int Mask { get; set; }

    bool spawned; int localMask, shownMask = -1;
    readonly float[] angles = new float[3];
    MaterialPropertyBlock mpb;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), EmissionId = Shader.PropertyToID("_EmissionColor");

    public int CurrentMask => spawned ? Mask : localMask;
    public bool PowerCut => (CurrentMask & 7) == 7;

    public override void Spawned() => spawned = true;
    public override void Despawned(NetworkRunner runner, bool hasState) => spawned = false;

    public void Toggle0() => Toggle(0);
    public void Toggle1() => Toggle(1);
    public void Toggle2() => Toggle(2);

    public void Toggle(int i)
    {
        if (spawned) RPC_Toggle(i); else localMask ^= 1 << i;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_Toggle(int i) { if (i >= 0 && i < 3) Mask ^= 1 << i; }

    void Update()
    {
        int m = CurrentMask;
        if (m != shownMask)
        {
            if (shownMask >= 0 && fx) fx.PlayOneShot(GymAudio.Flip);
            shownMask = m;
            bool cut = (m & 7) == 7;
            if (poweredObjects != null) foreach (var g in poweredObjects) if (g) g.SetActive(!cut);
            if (status) status.text = cut ? "<color=#5f5>ПИТАНИЕ ЛАЗЕРОВ ОТКЛЮЧЕНО</color>" : $"ЛАЗЕРЫ ПОД ТОКОМ\nОПУЩЕНО {Count(m)}/3";
            mpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < lamps.Length; i++)
            {
                if (!lamps[i]) continue;
                var c = (m & (1 << i)) != 0 ? new Color(0.2f, 1f, 0.3f) : new Color(1f, 0.15f, 0.1f);
                mpb.SetColor(BaseColorId, c); mpb.SetColor(EmissionId, c * 2f); lamps[i].SetPropertyBlock(mpb);
            }
        }
        for (int i = 0; i < levers.Length; i++)
        {
            if (!levers[i]) continue;
            angles[i] = Mathf.MoveTowards(angles[i], (m & (1 << i)) != 0 ? leverDownAngle : leverUpAngle, 400f * Time.deltaTime);
            levers[i].localRotation = Quaternion.Euler(angles[i], 0f, 0f);
        }
    }

    static int Count(int m) => (m & 1) + ((m >> 1) & 1) + ((m >> 2) & 1);
}
