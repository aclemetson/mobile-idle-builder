using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Single formatting point for the build version shown to players (loading screen + Settings).
    /// </summary>
    public static class VersionLabel
    {
        public static string Format() => Format(Application.version);

        public static string Format(string version)
            => string.IsNullOrEmpty(version) ? "" : $"v{version}";
    }
}
