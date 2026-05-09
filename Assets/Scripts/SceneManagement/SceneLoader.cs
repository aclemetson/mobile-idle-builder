using System;
using UnityEngine.SceneManagement;

namespace MobileIdleBuilder
{
    public static class SceneLoader
    {
        public const string LoadingSceneName = "LoadingScreen";

        private static string _targetScene;
        public static string TargetScene => _targetScene;

        public static bool IsTransitioning { get; private set; }

        // Fired once when the loading overlay destroys itself; then cleared.
        public static event Action OnTransitionComplete;

        public static void GoTo(string sceneName)
        {
            _targetScene      = sceneName;
            IsTransitioning   = true;
            SceneManager.LoadScene(LoadingSceneName);
        }

        internal static void CompleteTransition()
        {
            IsTransitioning = false;
            OnTransitionComplete?.Invoke();
            OnTransitionComplete = null;
        }
    }
}
