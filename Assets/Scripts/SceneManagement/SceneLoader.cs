using System;
using UnityEngine;
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
            GameLogger.Info($"[SceneLoader] GoTo('{sceneName}') — IsTransitioning was {IsTransitioning}");
            _targetScene    = sceneName;
            IsTransitioning = true;
            GameLogger.Info($"[SceneLoader] Calling LoadScene('{LoadingSceneName}')");
            SceneManager.LoadScene(LoadingSceneName);
            GameLogger.Info($"[SceneLoader] LoadScene('{LoadingSceneName}') returned");
        }

        internal static void CompleteTransition()
        {
            IsTransitioning = false;

            // Invoke each subscriber independently so a bad subscriber can't kill
            // the loading screen coroutine that calls this.
            if (OnTransitionComplete != null)
            {
                foreach (var handler in OnTransitionComplete.GetInvocationList())
                {
                    try { handler.DynamicInvoke(); }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[SceneLoader] OnTransitionComplete subscriber threw: {ex.Message}\n{ex.StackTrace}");
                    }
                }
                OnTransitionComplete = null;
            }
        }
    }
}
