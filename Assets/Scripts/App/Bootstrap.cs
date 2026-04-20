using UnityEngine;
using UnityEngine.SceneManagement;

namespace AiAvatarApp.App
{
    public sealed class Bootstrap : MonoBehaviour
    {
        [SerializeField] private string mainSceneName = "Main";

        private void Start()
        {
            SceneManager.LoadSceneAsync(mainSceneName);
        }
    }
}
