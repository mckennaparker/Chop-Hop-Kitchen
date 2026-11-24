using UnityEngine;
using UnityEngine.SceneManagement;

public class ReloadScene : MonoBehaviour
{
    public Conductor conductor;
    public void ReloadCurrentScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void StartSong()
    {
        if (conductor != null && !conductor.IsPlaying)
        {
            conductor.PlaySong();
        }
    }
}