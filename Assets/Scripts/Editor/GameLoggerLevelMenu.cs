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

    // Validators must always return true (returning false greys the item out, which made it
    // impossible to switch away from the active level). Use Menu.SetChecked for the indicator.
    [MenuItem("MobileIdleBuilder/Log Level/Develop", true)] static bool ValDevelop() => Check("Develop", LogLevel.Develop);
    [MenuItem("MobileIdleBuilder/Log Level/Debug",   true)] static bool ValDebug()   => Check("Debug",   LogLevel.Debug);
    [MenuItem("MobileIdleBuilder/Log Level/Info",    true)] static bool ValInfo()    => Check("Info",    LogLevel.Info);
    [MenuItem("MobileIdleBuilder/Log Level/Warning", true)] static bool ValWarning() => Check("Warning", LogLevel.Warning);
    [MenuItem("MobileIdleBuilder/Log Level/Error",   true)] static bool ValError()   => Check("Error",   LogLevel.Error);

    static bool Check(string item, LogLevel level)
    {
        Menu.SetChecked($"MobileIdleBuilder/Log Level/{item}", GameLogger.MinLevel == level);
        return true;
    }

    static void Set(LogLevel level)
    {
        EditorPrefs.SetInt(PrefKey, (int)level);
        GameLogger.MinLevel = level;
    }
}
