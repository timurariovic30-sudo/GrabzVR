using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Затемнение экрана для VR, переживающее смену сцены (DontDestroyOnLoad).
/// Чёрная сфера гранями внутрь радиусом 0.25 м вокруг головы (Camera.main) — закрывает весь обзор обоих глаз,
/// альфа плавно идёт 0 → 1 (Out) и 1 → 0 (In), без экранного Canvas и без «прилипания» к лицу.
/// Сфера не дочерняя камере: при загрузке сцены старая камера удаляется, а сфера остаётся и цепляется к новой.
/// После загрузки новой сцены, если экран был тёмным, сама ждёт, пока появится игрок (камера + 0.6 с на спавн
/// и телепорт), и плавно осветляет за fadeInTime.
/// </summary>
public class ScreenFade : MonoBehaviour
{
    public static ScreenFade Instance { get; private set; }
    public float Alpha { get; private set; }
    public float fadeInTime = 1.5f;
    public float fadeInDelay = 0.6f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    MeshRenderer sphere;
    MaterialPropertyBlock block;
    Coroutine running;

    /// Создаёт (или возвращает) единственный экземпляр. Материал — прозрачный Unlit (M_ScreenFade).
    public static ScreenFade Ensure(Material mat)
    {
        if (Instance) { if (mat && Instance.sphere.sharedMaterial != mat) Instance.sphere.sharedMaterial = mat; return Instance; }
        var go = new GameObject("ScreenFade (persistent)");
        DontDestroyOnLoad(go);
        var f = go.AddComponent<ScreenFade>();
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = "FadeSphere";
        Destroy(s.GetComponent<Collider>());
        s.transform.SetParent(go.transform, false);
        s.transform.localScale = Vector3.one * 0.5f;
        var mf = s.GetComponent<MeshFilter>();
        var m = Instantiate(mf.sharedMesh);                       // грани внутрь — видно изнутри
        var t = m.triangles; for (int i = 0; i < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
        m.triangles = t; m.RecalculateNormals();
        mf.sharedMesh = m;
        f.sphere = s.GetComponent<MeshRenderer>();
        f.sphere.sharedMaterial = mat;
        f.sphere.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        f.sphere.receiveShadows = false;
        f.block = new MaterialPropertyBlock();
        f.SetAlpha(0f);
        return f;
    }

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam) transform.SetPositionAndRotation(cam.transform.position, cam.transform.rotation);
    }

    void SetAlpha(float a)
    {
        Alpha = Mathf.Clamp01(a);
        if (!sphere) return;
        sphere.enabled = Alpha > 0.001f;
        block.SetColor(BaseColorId, new Color(0f, 0f, 0f, Alpha));
        sphere.SetPropertyBlock(block);
    }

    /// Плавно в чёрное (сглаженная кривая — без резкого обрыва кадра). onDone — когда альфа = 1.
    public void Out(float time, Action onDone = null) => Run(Fade(Alpha, 1f, time, onDone));

    /// Плавно из чёрного.
    public void In(float time, Action onDone = null) => Run(Fade(Alpha, 0f, time, onDone));

    void Run(IEnumerator c) { if (running != null) StopCoroutine(running); running = StartCoroutine(c); }

    IEnumerator Fade(float from, float to, float time, Action onDone)
    {
        for (float t = 0; t < time; t += Time.unscaledDeltaTime)
        {
            SetAlpha(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / time)));
            yield return null;
        }
        SetAlpha(to);
        running = null;
        onDone?.Invoke();
    }

    void OnSceneLoaded(Scene s, LoadSceneMode mode)
    {
        if (Alpha > 0.5f) Run(FadeInWhenReady());
    }

    IEnumerator FadeInWhenReady()
    {
        SetAlpha(1f);
        float t0 = Time.unscaledTime;
        while (!Camera.main && Time.unscaledTime - t0 < 5f) yield return null;   // игрок ещё не заспавнился
        yield return new WaitForSecondsRealtime(fadeInDelay);                     // спавн-точка, телепорт, аватар
        yield return Fade(1f, 0f, fadeInTime, null);
    }
}
