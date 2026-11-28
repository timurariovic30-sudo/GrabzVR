using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Портал в другую сцену (MainHub ⇄ TestGym). Срабатывает, когда в триггер входит СВОЙ игрок (не чужая сетевая горилла).
/// В сети сцена меняется у всей комнаты (FusionNetworkManager.LoadSceneSynced), офлайн — обычная загрузка.
/// Первые armDelay секунд после загрузки не срабатывает (игрок может появиться рядом).
/// </summary>
public class ScenePortal : MonoBehaviour
{
    public string sceneName = GameSession.HubScene;
    public float armDelay = 2f;
    bool used;

    void OnTriggerEnter(Collider other)
    {
        if (used || Time.timeSinceLevelLoad < armDelay) return;
        if (!LaserBeam.IsPlayer(other) || other.GetComponentInParent<NetworkGorillaAvatar>() != null) return;
        Go();
    }

    public void Go()
    {
        used = true;
        Debug.Log("[ScenePortal] → " + sceneName);
        if (!FusionNetworkManager.LoadSceneSynced(sceneName)) SceneManager.LoadScene(sceneName);
    }
}
