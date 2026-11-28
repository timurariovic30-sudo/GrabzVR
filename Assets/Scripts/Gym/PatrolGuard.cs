using UnityEngine;

/// <summary>
/// Патрульный охранник с шокером (полигон TestGym). Идёт по кругу через waypoints. Позиция считается от общего
/// времени комнаты (Runner.SimulationTime в Fusion, иначе Time.time), поэтому у всех игроков охранник в одной точке
/// без сетевой синхронизации трансформа. Каждый клиент проверяет только СВОЕГО игрока: подошёл ближе zapRange
/// в поле зрения — удар шокером: нокаут (GorillaFallRagdoll), тревога, вспышка. Чужой нокаут другим видно по сети
/// (NetworkGorillaAvatar.KnockoutCount).
/// </summary>
public class PatrolGuard : MonoBehaviour
{
    public Transform[] waypoints;
    public float speed = 1.3f;
    public float zapRange = 1.5f;
    [Range(10f, 180f)] public float viewAngle = 70f;
    public float zapCooldown = 5f;

    [Header("Ссылки (заполняет билдер)")]
    public Transform body;
    public Transform taserArm;
    public Renderer taserTip;
    public Light zapLight;
    public AudioSource fx;

    float lastZap = -10f, armKick, perimeter;
    float[] segStart;
    MaterialPropertyBlock mpb;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    public int ZapCount { get; private set; }

    void Awake()
    {
        if (fx) { fx.playOnAwake = false; fx.spatialBlend = 1f; }
        if (zapLight) zapLight.enabled = false;
        BuildPath();
    }

    void BuildPath()
    {
        if (waypoints == null || waypoints.Length < 2) return;
        segStart = new float[waypoints.Length + 1];
        perimeter = 0f;
        for (int i = 0; i < waypoints.Length; i++)
        {
            segStart[i] = perimeter;
            perimeter += Vector3.Distance(waypoints[i].position, waypoints[(i + 1) % waypoints.Length].position);
        }
        segStart[waypoints.Length] = perimeter;
    }

    static float SharedTime()
    {
        var m = FusionNetworkManager.Instance;
        if (m != null && m.Runner != null && m.Runner.IsRunning) return m.Runner.SimulationTime;
        return Time.time;
    }

    /// Точка и направление на маршруте по пройденной дистанции (для тестов — публично).
    public void Evaluate(float distance, out Vector3 pos, out Vector3 dir)
    {
        pos = transform.position; dir = transform.forward;
        if (segStart == null || perimeter <= 0f) return;
        float d = Mathf.Repeat(distance, perimeter);
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (d > segStart[i + 1]) continue;
            var a = waypoints[i].position; var b = waypoints[(i + 1) % waypoints.Length].position;
            float len = Mathf.Max(0.001f, segStart[i + 1] - segStart[i]);
            pos = Vector3.Lerp(a, b, (d - segStart[i]) / len); dir = (b - a).normalized; return;
        }
    }

    void Update()
    {
        if (segStart == null) return;
        Evaluate(SharedTime() * speed, out var pos, out var dir);
        var t = body ? body : transform;
        t.position = new Vector3(pos.x, t.position.y, pos.z);
        if (dir.sqrMagnitude > 0.001f) t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z)), 8f * Time.deltaTime);

        armKick = Mathf.MoveTowards(armKick, 0f, Time.deltaTime * 3f);
        if (taserArm) taserArm.localRotation = Quaternion.Euler(-70f * armKick, 0f, 0f);
        bool flash = Time.time - lastZap < 0.25f;
        if (zapLight) zapLight.enabled = flash;
        if (taserTip)
        {
            mpb ??= new MaterialPropertyBlock();
            mpb.SetColor(EmissionId, new Color(0.3f, 0.6f, 1f) * (flash ? 8f : 1.5f + Mathf.PingPong(Time.time * 4f, 1f)));
            taserTip.SetPropertyBlock(mpb);
        }

        var cam = Camera.main;
        if (cam == null || Time.time - lastZap < zapCooldown) return;
        var to = cam.transform.position - t.position; to.y = 0f;
        if (to.magnitude > zapRange || Vector3.Angle(t.forward, to) > viewAngle * 0.5f) return;
        Zap(t);
    }

    void Zap(Transform t)
    {
        lastZap = Time.time; armKick = 1f; ZapCount++;
        if (fx) fx.PlayOneShot(GymAudio.Zap);
        var rag = LocalRagdoll();
        if (rag != null && !rag.IsKnockedOut) rag.TriggerKnockout();
        if (AlarmSystem.Instance != null) AlarmSystem.Instance.TriggerIntrusion("ОХРАННИК");
        Debug.Log("[PatrolGuard] шокер! локальный игрок в нокауте");
    }

    /// Ragdoll своего активного игрока (VR или ПК), не сетевой копии.
    public static GorillaFallRagdoll LocalRagdoll()
    {
        foreach (var r in FindObjectsByType<GorillaFallRagdoll>(FindObjectsInactive.Exclude))
            if (r.GetComponentInParent<NetworkGorillaAvatar>() == null) return r;
        return null;
    }
}
