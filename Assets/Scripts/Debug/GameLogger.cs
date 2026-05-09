namespace MobileIdleBuilder
{
    public enum LogLevel { Develop = 0, Debug = 1, Info = 2, Warning = 3, Error = 4 }

    public static class GameLogger
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public static LogLevel MinLevel = LogLevel.Develop;
#else
        public static LogLevel MinLevel = LogLevel.Info;
#endif

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void Develop(string msg)
        {
            if (MinLevel <= LogLevel.Develop)
                UnityEngine.Debug.Log(msg);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void Debug(string msg)
        {
            if (MinLevel <= LogLevel.Debug)
                UnityEngine.Debug.Log(msg);
        }

        public static void Info(string msg)    { if (MinLevel <= LogLevel.Info)    UnityEngine.Debug.Log(msg); }
        public static void Warning(string msg) { if (MinLevel <= LogLevel.Warning) UnityEngine.Debug.LogWarning(msg); }
        public static void Error(string msg)   { if (MinLevel <= LogLevel.Error)   UnityEngine.Debug.LogError(msg); }
    }
}
