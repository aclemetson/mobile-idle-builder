using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Records which platforms wrote a save (<see cref="SaveData.platforms"/>), so test players can be told
    /// apart from real ones later: Unity's player record does not say which device created a player.
    /// scripts/purge_test_players.py keeps any player whose save lists a phone platform.
    /// </summary>
    public static class SavePlatform
    {
        public const string Editor  = "Editor";
        public const string Android = "Android";
        public const string IOS     = "iOS";

        /// <summary>This build's tag: Editor (any OS), Android, iOS, or the RuntimePlatform name otherwise.</summary>
        public static string Current => Tag(Application.platform);

        internal static string Tag(RuntimePlatform p) => p switch
        {
            RuntimePlatform.WindowsEditor or RuntimePlatform.OSXEditor or RuntimePlatform.LinuxEditor => Editor,
            RuntimePlatform.Android      => Android,
            RuntimePlatform.IPhonePlayer => IOS,
            _                            => p.ToString(),
        };

        /// <summary>Adds the current platform to the save's list once (saves from before the field get it on their next write).</summary>
        public static void Stamp(SaveData save) => Stamp(save, Current);

        internal static void Stamp(SaveData save, string platform)
        {
            if (save == null || string.IsNullOrEmpty(platform)) return;
            save.platforms ??= new List<string>();
            if (!save.platforms.Contains(platform)) save.platforms.Add(platform);
        }
    }
}
