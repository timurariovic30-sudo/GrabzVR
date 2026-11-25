using UnityEngine;

/// <summary>Разлёт осколков (стекло витрин, разбитая ваза). Осколки — простые физические кусочки, исчезают через пару секунд.</summary>
public static class Shatter
{
    public static void Spawn(Bounds area, Material mat, int count, Vector3 baseVelocity, AudioClip[] clips, float sizeScale = 1f)
    {
        if (clips != null && clips.Length > 0)
            AudioSource.PlayClipAtPoint(clips[Random.Range(0, clips.Length)], area.center, 1f);

        int ignore = LayerMask.NameToLayer("Ignore Raycast"); // руки GorillaLocomotion не цепляются за осколки
        for (int i = 0; i < count; i++)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            s.name = "Shard";
            s.layer = ignore;
            var p = new Vector3(
                Random.Range(area.min.x, area.max.x),
                Random.Range(area.min.y, area.max.y),
                Random.Range(area.min.z, area.max.z));
            s.transform.SetPositionAndRotation(p, Random.rotation);
            float sz = Random.Range(0.04f, 0.12f) * sizeScale;
            s.transform.localScale = new Vector3(sz, sz * Random.Range(0.6f, 1.4f), 0.01f + sz * 0.15f);
            if (mat) s.GetComponent<Renderer>().sharedMaterial = mat;
            var rb = s.AddComponent<Rigidbody>();
            rb.mass = 0.05f;
            rb.linearVelocity = baseVelocity + (p - area.center).normalized * Random.Range(1f, 3f) + Vector3.up * Random.Range(0.5f, 2f);
            rb.angularVelocity = Random.insideUnitSphere * 10f;
            Object.Destroy(s, Random.Range(2.5f, 4f));
        }
    }
}
