using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using UnityEngine.Networking;

namespace MobileIdleBuilder
{
    /// <summary>
    /// A UTC "now" the player cannot move by changing the phone's date. It reads the HTTP
    /// <c>Date</c> header from a Unity Gaming Services host the game already talks to, then
    /// counts forward with a monotonic <see cref="Stopwatch"/>, so changing the device clock
    /// mid-session has no effect either.
    ///
    /// The stopwatch may stop while the device sleeps, so callers <see cref="Invalidate"/> on
    /// resume and sync again. Until a sync succeeds, <see cref="UtcNow"/> is null: anything that
    /// pays out on a timer (achievement period resets) waits rather than trusting the device.
    ///
    /// Editor and dev builds still apply <see cref="GameClock.Offset"/>, so AI playtests that
    /// fast-forward time also move this clock.
    /// </summary>
    public static class ServerClock
    {
        // Any HTTP answer (404 and 405 included) carries a Date header, so the path does not matter.
        // Two hosts in case one is down; both are already contacted by the auth and cloud-save SDKs.
        static readonly string[] Endpoints =
        {
            "https://player-auth.services.api.unity.com/",
            "https://cloud-save.services.api.unity.com/",
        };

        const int TimeoutSeconds = 10;

        static DateTime s_anchorUtc;
        static readonly Stopwatch s_sinceAnchor = new();
        static bool s_synced;

        // A sync in flight, shared by every caller (achievements and daily events both sync on resume).
        // A deadline rather than a flag, so a coroutine stopped mid-request cannot leave it stuck.
        static float s_inFlightUntil = -1f;
        static bool InFlight => UnityEngine.Time.realtimeSinceStartup < s_inFlightUntil;

        /// <summary>Device clock minus server clock at the last sync (positive = phone is ahead).</summary>
        public static TimeSpan LastSkew { get; private set; }

        /// <summary>Test seam: when set, <see cref="UtcNow"/> returns this and no network call is made.</summary>
        internal static Func<DateTime?> TestNowOverride;

        public static bool IsSynced => UtcNow.HasValue;

        /// <summary>Server-verified UTC now, or null when no sync has succeeded since the last <see cref="Invalidate"/>.</summary>
        public static DateTime? UtcNow
        {
            get
            {
                if (TestNowOverride != null) return TestNowOverride();
                if (!s_synced) return null;
                return s_anchorUtc + s_sinceAnchor.Elapsed + GameClock.Offset;
            }
        }

        /// <summary>Forget the last sync. Call on resume from background.</summary>
        public static void Invalidate() => s_synced = false;

        /// <summary>
        /// Fetches server time. Run as a coroutine; <paramref name="done"/> receives whether it worked.
        /// </summary>
        public static IEnumerator Sync(Action<bool> done)
        {
            if (TestNowOverride != null) { done?.Invoke(true); yield break; }

            if (InFlight)
            {
                while (InFlight) yield return null;   // another caller's sync; share its answer
                done?.Invoke(s_synced);
                yield break;
            }
            s_inFlightUntil = UnityEngine.Time.realtimeSinceStartup + Endpoints.Length * (TimeoutSeconds + 2);

            foreach (var url in Endpoints)
            {
                using var req = UnityWebRequest.Head(url);
                req.timeout = TimeoutSeconds;
                var sent = Stopwatch.StartNew();
                yield return req.SendWebRequest();
                sent.Stop();

                string header = req.GetResponseHeader("Date");
                if (req.result != UnityWebRequest.Result.ConnectionError && TryParseHttpDate(header, out var serverUtc))
                {
                    // The header was stamped somewhere mid-flight; half the round trip is the best estimate.
                    s_anchorUtc = serverUtc + TimeSpan.FromTicks(sent.Elapsed.Ticks / 2);
                    s_sinceAnchor.Restart();
                    s_synced = true;
                    LastSkew = DateTime.UtcNow - s_anchorUtc;

                    GameLogger.Info($"[ServerClock] Synced from {url}: server {s_anchorUtc:o}, device {DateTime.UtcNow:o}, " +
                                    $"device skew {FormatSkew(LastSkew)} (round trip {sent.ElapsedMilliseconds} ms)");
                    s_inFlightUntil = -1f;
                    done?.Invoke(true);
                    yield break;
                }

                GameLogger.Warning($"[ServerClock] No server time from {url}: result={req.result} " +
                                   $"http={req.responseCode} error='{req.error}' Date='{header}'");
            }

            s_inFlightUntil = -1f;
            done?.Invoke(false);
        }

        /// <summary>
        /// Starts <see cref="Sync"/> on <paramref name="host"/>. The first step runs right away and the rest
        /// continues as a coroutine only if it actually waits, so a sync that needs no network (test clock)
        /// completes synchronously.
        /// </summary>
        public static void StartSync(UnityEngine.MonoBehaviour host, Action<bool> done)
        {
            var routine = Sync(done);
            if (!routine.MoveNext()) return;
            host.StartCoroutine(Continue(routine));

            static IEnumerator Continue(IEnumerator e)
            {
                yield return e.Current;          // the step MoveNext already reached (e.g. the web request)
                while (e.MoveNext()) yield return e.Current;
            }
        }

        /// <summary>Parses an RFC 1123 HTTP date ("Wed, 18 Nov 2026 07:28:00 GMT") as UTC.</summary>
        internal static bool TryParseHttpDate(string value, out DateTime utc)
        {
            if (!string.IsNullOrEmpty(value) &&
                DateTime.TryParseExact(value.Trim(), "r", CultureInfo.InvariantCulture,
                                       DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc))
            {
                utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
                return true;
            }
            utc = default;
            return false;
        }

        internal static string FormatSkew(TimeSpan skew)
        {
            string sign = skew < TimeSpan.Zero ? "-" : "+";
            var a = skew.Duration();
            return a.TotalHours >= 1 ? $"{sign}{(int)a.TotalHours}h{a.Minutes:00}m"
                 : a.TotalMinutes >= 1 ? $"{sign}{(int)a.TotalMinutes}m{a.Seconds:00}s"
                 : $"{sign}{a.TotalSeconds:0.0}s";
        }

#if UNITY_EDITOR
        /// <summary>Clears static state on domain reload being disabled (Enter Play Mode Options).</summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_synced = false;
            s_inFlightUntil = -1f;
            LastSkew = TimeSpan.Zero;
        }
#endif
    }
}
