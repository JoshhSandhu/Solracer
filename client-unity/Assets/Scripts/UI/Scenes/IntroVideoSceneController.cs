using Solracer.Config;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace Solracer.UI
{
    /// <summary>
    /// Plays an intro video and loads the next scene when playback finishes.
    /// </summary>
    public class IntroVideoSceneController : MonoBehaviour
    {
        [Header("Video")]
        [Tooltip("VideoPlayer used for the intro. Auto-found if left empty.")]
        [SerializeField] private VideoPlayer videoPlayer;

        [Tooltip("Play the video automatically on scene start.")]
        [SerializeField] private bool playOnStart = true;

        [Header("Scene Flow")]
        [Tooltip("Scene to load after the intro video ends.")]
        [SerializeField] private string nextSceneName = SceneNames.Login;

        private bool isLoadingNextScene;

        private void Awake()
        {
            if (videoPlayer == null)
            {
                videoPlayer = FindAnyObjectByType<VideoPlayer>();
            }
        }

        private void OnEnable()
        {
            if (videoPlayer != null)
            {
                videoPlayer.loopPointReached += HandleVideoFinished;
            }
        }

        private void Start()
        {
            if (videoPlayer == null)
            {
                Debug.LogWarning("[IntroVideoSceneController] VideoPlayer not found. Loading next scene immediately.");
                LoadNextScene();
                return;
            }

            videoPlayer.isLooping = false;

            if (playOnStart)
            {
                videoPlayer.Play();
            }
        }

        private void OnDisable()
        {
            if (videoPlayer != null)
            {
                videoPlayer.loopPointReached -= HandleVideoFinished;
            }
        }

        private void HandleVideoFinished(VideoPlayer source)
        {
            LoadNextScene();
        }

        public void LoadNextScene()
        {
            if (isLoadingNextScene)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(nextSceneName))
            {
                Debug.LogError("[IntroVideoSceneController] Next scene name is empty.");
                return;
            }

            isLoadingNextScene = true;
            SceneManager.LoadScene(nextSceneName);
        }
    }
}
