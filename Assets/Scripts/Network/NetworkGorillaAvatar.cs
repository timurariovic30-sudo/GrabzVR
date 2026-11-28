using Fusion;
using TMPro;
using UnityEngine;

/// <summary>
/// Сетевая горилла (Photon Fusion 2, Shared). Каждый игрок спавнит одну такую на себя:
///  • у своего (HasStateAuthority) она невидима и каждый тик отправляет позу локального игрока —
///    голову, обе руки (позиция + поворот), хват/курок пальцев, цвет шерсти, косметику и ник.
///    Источник — активный локальный игрок: VRGorillaPlayer (шлем) или DesktopTestPlayer (ПК);
///  • у остальных — видимая модель Gorilla_Final с тем же ригом рук (GorillaFinalRig), пальцами
///    (VRHandPoseController.SetPose), перекраской и косметикой; голос (Photon Voice) звучит из рта
///    через 3D AudioSource (Spatial Blend = 1, тише с расстоянием).
/// Ни камеры, ни управления на ней нет.
/// </summary>
public class NetworkGorillaAvatar : NetworkBehaviour
{
    static readonly string[] CosmeticIds = { "", "Crown", "RobberMask", "GoldChain", "RobberMaskWhite", "RobberMaskGray" };

    [Networked] public Vector3 HeadPos { get; set; }
    [Networked] public Quaternion HeadRot { get; set; }
    [Networked] public Vector3 LeftPos { get; set; }
    [Networked] public Quaternion LeftRot { get; set; }
    [Networked] public Vector3 RightPos { get; set; }
    [Networked] public Quaternion RightRot { get; set; }
    [Networked] public Vector3 FurRGB { get; set; }
    [Networked] public float LeftGrip { get; set; }
    [Networked] public float LeftTrigger { get; set; }
    [Networked] public float RightGrip { get; set; }
    [Networked] public float RightTrigger { get; set; }
    /// Битовая маска надетой косметики: бит (i − 1) ↔ CosmeticIds[i] (корона + маска + цепь — одновременно).
    [Networked] public int Cosmetic { get; set; }

    static int EncodeCosmetics(string[] worn)
    {
        int mask = 0;
        for (int i = 1; i < CosmeticIds.Length; i++) if (System.Array.IndexOf(worn, CosmeticIds[i]) >= 0) mask |= 1 << (i - 1);
        return mask;
    }

    static string DecodeCosmetics(int mask)
    {
        var list = new System.Collections.Generic.List<string>();
        for (int i = 1; i < CosmeticIds.Length; i++) if ((mask & (1 << (i - 1))) != 0) list.Add(CosmeticIds[i]);
        return string.Join(",", list);
    }
    [Networked] public NetworkString<_16> Nick { get; set; }
    /// Выражение лица (GorillaFaceEmotes): обычное / натуга / испуг / нокаут.
    [Networked] public byte FaceEmote { get; set; }
    /// Счётчик нокаутов (GorillaFallRagdoll): вырос — у всех эта горилла падает тряпичной куклой.
    [Networked] public int KnockoutCount { get; set; }

    /// Это видимая горилла другого игрока (не своя невидимая копия и уже заспавнена).
    public bool IsRemoteVisible => Object != null && Object.IsValid && !HasStateAuthority;
    /// Своя невидимая копия (или ещё не заспавнена) — реакции на ней ничего не показывают.
    public bool IsHiddenSelf => Object == null || !Object.IsValid || HasStateAuthority;

    [Header("Ссылки (заполняет билдер)")]
    public GorillaFinalRig rig;
    public VRHandPoseController hands;
    public PlayerSkinController skin;
    public PlayerCosmetics cosmetics;
    public Transform headProxy, leftProxy, rightProxy;
    public TextMeshPro nameTag;
    [Tooltip("Плавность догоняния сетевой позы (больше — резче)")]
    public float smoothing = 18f;

    public static NetworkGorillaAvatar Local { get; private set; }

    GorillaFinalRig localRig;
    Vector3 shownFur = new Vector3(-1, -1, -1);
    int shownCosmetic = -99;
    bool snapped;

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            Local = this;
            // своя сетевая горилла невидима для своих глаз (видна только другим игрокам)
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            if (rig) rig.enabled = false;
            Runner.MakeDontDestroyOnLoad(gameObject);
        }
        else
        {
            // чужая горилла: голова, глаза, рот, балаклава и шапки — на Default, чтобы их видела наша камера
            // (LocalHead скрыт из culling mask камеры и нужен только СВОЕЙ голове у локального игрока VRGorillaPlayer)
            int moved = MakeHeadVisible(gameObject);
            if (moved > 0) Debug.Log($"[NetworkGorillaAvatar] голова чужого игрока видима: {moved} объектов LocalHead → Default");
            if (rig)
            {
                rig.head = headProxy;
                rig.left.target = leftProxy; rig.left.rotationSource = leftProxy;
                rig.right.target = rightProxy; rig.right.rotationSource = rightProxy;
            }
        }
    }

    /// Переводит все объекты слоя LocalHead внутри root (включая выключенную косметику) на слой Default.
    /// Возвращает число перенесённых объектов. Вызывается только для удалённых игроков (!HasStateAuthority:
    /// в Shared Mode владелец аватара — его StateAuthority, InputAuthority не используется).
    public static int MakeHeadVisible(GameObject root)
    {
        int localHead = LayerMask.NameToLayer("LocalHead");
        if (localHead < 0 || root == null) return 0;
        int n = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.gameObject.layer == localHead) { t.gameObject.layer = 0; n++; }
        return n;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Local == this) Local = null;
    }

    // ------------------------------------------------------------------ свой игрок → сеть
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;
        if (!FindLocalRig()) return;
        var r = localRig;
        HeadPos = r.head.position; HeadRot = r.head.rotation;
        var lr = r.left.rotationSource ? r.left.rotationSource : r.left.target;
        var rr = r.right.rotationSource ? r.right.rotationSource : r.right.target;
        if (r.left.target) { LeftPos = r.left.target.position; LeftRot = lr.rotation; }
        if (r.right.target) { RightPos = r.right.target.position; RightRot = rr.rotation; }

        var localSkin = r.GetComponentInParent<PlayerSkinController>();
        if (localSkin) FurRGB = new Vector3(localSkin.furColor.r, localSkin.furColor.g, localSkin.furColor.b);
        Cosmetic = EncodeCosmetics(GameSession.WornCosmetics);
        Nick = GrabzSettings.PlayerName;

        // живые реакции: лицо и нокаут считает локальный игрок, остальные только показывают
        var emotes = r.GetComponent<GorillaFaceEmotes>();
        if (emotes) FaceEmote = (byte)emotes.LocalEmote;
        var fall = r.GetComponent<GorillaFallRagdoll>();
        if (fall) KnockoutCount = fall.KnockoutCount;

        var pose = r.GetComponentInParent<VRHandPoseController>();
        if (pose)
        {
            LeftGrip = pose.left.grip; LeftTrigger = pose.left.trigger;
            RightGrip = pose.right.grip; RightTrigger = pose.right.trigger;
        }
        else
        {
            var grab = r.GetComponentInParent<DesktopTestPlayer>()?.GetComponentInChildren<VRGrabber>();
            RightGrip = grab && grab.Held ? 1f : 0f; RightTrigger = RightGrip;
            LeftGrip = LeftTrigger = 0f;
        }
    }

    /// Активный локальный игрок (VR или ПК), не сетевой.
    bool FindLocalRig()
    {
        if (localRig && localRig.isActiveAndEnabled && localRig.head) return true;
        localRig = null;
        foreach (var r in FindObjectsByType<GorillaFinalRig>())
            if (r.isActiveAndEnabled && r.head && !r.GetComponentInParent<NetworkGorillaAvatar>()) { localRig = r; break; }
        return localRig != null;
    }

    // ------------------------------------------------------------------ сеть → модель друга
    public override void Render()
    {
        if (HasStateAuthority) return;
        float k = snapped ? 1f - Mathf.Exp(-smoothing * Time.deltaTime) : 1f;
        snapped = true;
        Follow(headProxy, HeadPos, HeadRot, k);
        Follow(leftProxy, LeftPos, LeftRot, k);
        Follow(rightProxy, RightPos, RightRot, k);

        if (FurRGB != shownFur && skin)
        {
            shownFur = FurRGB;
            skin.SetNetworkColor(new Color(FurRGB.x, FurRGB.y, FurRGB.z));
        }
        if (Cosmetic != shownCosmetic && cosmetics)
        {
            shownCosmetic = Cosmetic;
            cosmetics.useNetworkId = true;
            cosmetics.networkId = DecodeCosmetics(Cosmetic);
            cosmetics.Apply();
        }
        if (hands)
        {
            hands.SetPose(true, LeftGrip, LeftTrigger);
            hands.SetPose(false, RightGrip, RightTrigger);
        }
        if (nameTag)
        {
            nameTag.text = Nick.ToString();
            nameTag.transform.position = headProxy.position + Vector3.up * 0.42f;
            var cam = Camera.main;
            if (cam) nameTag.transform.rotation = Quaternion.LookRotation(nameTag.transform.position - cam.transform.position);
        }
    }

    static void Follow(Transform t, Vector3 p, Quaternion q, float k)
    {
        if (!t) return;
        if (q.x == 0 && q.y == 0 && q.z == 0 && q.w == 0) q = Quaternion.identity;
        t.SetPositionAndRotation(Vector3.Lerp(t.position, p, k), Quaternion.Slerp(t.rotation, q, k));
    }

    // ------------------------------------------------------------------ общая смена сцены
    /// Кто-то запрыгнул в фургон (или раунд закончился) → все в сессии едут в одну сцену.
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RpcRequestScene(int buildIndex, int mode, NetworkBool fromHub)
    {
        GameSession.CurrentMode = (GameSession.Mode)mode;
        GameSession.ArrivedFromHub = fromHub;
        FusionNetworkManager.Instance?.LoadSceneAsAuthority(buildIndex);
    }
}
