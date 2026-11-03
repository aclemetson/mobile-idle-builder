using System;
using System.Globalization;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure decision seam for the game-data update notice. Kept free of MonoBehaviour/UGS so the
    /// gate logic is unit-testable (see GameUpdateNoticeTests) the same way <see cref="MaintenanceGate"/>
    /// isolates its formatting. The remote stamp comes from <c>gamedata.updatedUtc</c>; the saved
    /// marker is <c>SaveData.lastSeenGameDataUtc</c>.
    /// </summary>
    public static class GameUpdateNotice
    {
        /// <summary>
        /// True only when <paramref name="remoteUpdatedUtc"/> is a valid UTC datetime AND it is newer
        /// than <paramref name="lastSeenUtc"/> (or the player has never acknowledged any stamp).
        /// Any blank/unparseable input fails closed (returns false) and never throws — a fetch failure
        /// or a fresh install stays silent.
        /// </summary>
        public static bool ShouldShow(string remoteUpdatedUtc, string lastSeenUtc)
        {
            if (!TryParseUtc(remoteUpdatedUtc, out var remote))
                return false; // no valid published stamp => nothing to announce

            if (!TryParseUtc(lastSeenUtc, out var seen))
                return true;  // never acknowledged anything => show on first valid stamp

            return remote > seen;
        }

        static bool TryParseUtc(string iso, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrWhiteSpace(iso))
                return false;

            return DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utc);
        }
    }
}
