using UnityEngine;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

public class ReloadScene : MonoBehaviour
{
    public Conductor conductor;
    public void ReloadCurrentScene()
    {
        UnitySceneManager.LoadScene(UnitySceneManager.GetActiveScene().buildIndex);
    }

    public void StartSong()
    {
        if (conductor != null && !conductor.IsPlaying)
        {
            conductor.PlaySong();
        }
    }
}