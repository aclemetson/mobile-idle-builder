using UnityEditor;
using MobileIdleBuilder;

[InitializeOnLoad]
public static class GameLoggerLevelMenu
{
    private const string PrefKey = "GameLogger.MinLevel";

    static GameLoggerLevelMenu()
    {
        GameLogger.MinLevel = (LogLevel)EditorPrefs.GetInt(PrefKey, (int)LogLevel.Develop);
    }

    [MenuItem("MobileIdleBuilder/Log Level/Develop")] static void SetDevelop() => Set(LogLevel.Develop);
    [MenuItem("MobileIdleBuilder/Log Level/Debug")]   static void SetDebug()   => Set(LogLevel.Debug);
    [MenuItem("MobileIdleBuilder/Log Level/Info")]    static void SetInfo()    => Set(LogLevel.Info);
    [MenuItem("MobileIdleBuilder/Log Level/Warning")] static void SetWarning() => Set(LogLevel.Warning);
    [MenuItem("MobileIdleBuilder/Log Level/Error")]   static void SetError()   => Set(LogLevel.Error);

    [MenuItem("MobileIdleBuilder/Log Level/Develop", true)] static bool ValDevelop() => GameLogger.MinLevel == LogLevel.Develop;
    [MenuItem("MobileIdleBuilder/Log Level/Debug",   true)] static bool ValDebug()   => GameLogger.MinLevel == LogLevel.Debug;
    [MenuItem("MobileIdleBuilder/Log Level/Info",    true)] static bool ValInfo()    => GameLogger.MinLevel == LogLevel.Info;
    [MenuItem("MobileIdleBuilder/Log Level/Warning", true)] static bool ValWarning() => GameLogger.MinLevel == LogLevel.Warning;
    [MenuItem("MobileIdleBuilder/Log Level/Error",   true)] static bool ValError()   => GameLogger.MinLevel == LogLevel.Error;

    static void Set(LogLevel level)
    {
        EditorPrefs.SetInt(PrefKey, (int)level);
        GameLogger.MinLevel = level;
    }
}
