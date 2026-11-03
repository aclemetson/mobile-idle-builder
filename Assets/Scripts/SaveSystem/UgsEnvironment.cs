namespace MobileIdleBuilder
{
    /// <summary>
    /// Single source of truth for the Unity Gaming Services environment name. Both Cloud Save
    /// (<see cref="UGSCloudSaveService"/>) and the splash maintenance gate must initialize UGS with the
    /// same environment so Remote Config / Cloud Save resolve against the same dashboard environment.
    /// Editor + internal/dev builds use "development"; store builds use "production".
    /// </summary>
    public static class UgsEnvironment
    {
        public static string Name =>
#if UNITY_EDITOR || DEV_ENVIRONMENT
            "development";
#else
            "production";
#endif
    }
}
