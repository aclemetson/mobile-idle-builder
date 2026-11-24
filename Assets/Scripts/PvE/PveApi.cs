using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>Calls one PvE module function and returns its JSON string. Cloud Code in the game; a fake in tests.</summary>
    public interface IPveTransport
    {
        Task<string> Call(string function, Dictionary<string, object> args);
    }

    /// <summary>
    /// Client for the PvE Cloud Code module (CloudCode/PveModule, docs/runbooks/pve-backend-setup.md). Every call
    /// returns the shared response model; network and service failures come back as an <c>error</c> starting
    /// with "network:" instead of throwing. The server decides every time: <see cref="DebugNowUtc"/> is sent
    /// only when set (dev console <c>pve time</c>) and the server ignores it unless pve_settings.allowDebugTime is on.
    /// </summary>
    public static class PveApi
    {
        public const string Module = "PveModule";

        public static IPveTransport Transport { get; set; } = new CloudCodeTransport();

        /// <summary>Server-time override for testing (dev console). Null = real server time.</summary>
        public static DateTime? DebugNowUtc { get; set; }

        public static Task<PveStatusResponse> Status() =>
            Send<PveStatusResponse>("Status", Args());

        public static Task<PveStatusResponse> Join(bool devSave) =>
            Send<PveStatusResponse>("Join", Args(("dev", devSave)));

        /// <param name="atT">-1 = count it now; otherwise the elapsed seconds it was taken at (a delayed delivery).</param>
        public static Task<PveSnapshotResponse> Snapshot(double netWorth, double ratePerSecond, int idleCapSeconds, int atT = -1)
        {
            var args = Args(("netWorth", netWorth), ("rate", ratePerSecond), ("cap", idleCapSeconds));
            if (atT >= 0) args["atT"] = atT;
            return Send<PveSnapshotResponse>("Snapshot", args);
        }

        /// <summary>Leaves this week's run early at <paramref name="netWorth"/> (#505).</summary>
        public static Task<PveSnapshotResponse> Retire(double netWorth) =>
            Send<PveSnapshotResponse>("Retire", Args(("netWorth", netWorth)));

        public static Task<PveBoardResponse> Board() =>
            Send<PveBoardResponse>("Board", Args());

        public static Task<PveClaimResponse> Claim(string week) =>
            Send<PveClaimResponse>("Claim", new Dictionary<string, object> { { "week", week } });

        static Dictionary<string, object> Args(params (string key, object value)[] pairs)
        {
            var d = new Dictionary<string, object>();
            foreach (var (k, v) in pairs) d[k] = v;
            if (DebugNowUtc.HasValue) d["debugNow"] = PveRules.Iso(DebugNowUtc.Value);
            return d;
        }

        static async Task<T> Send<T>(string function, Dictionary<string, object> args) where T : class, new()
        {
            string json;
            try
            {
                json = await Transport.Call(function, args);
            }
            catch (Exception e)
            {
                GameLogger.Warning($"[PvE] {function} failed: {e.Message}");
                return WithError<T>("network:" + e.Message);
            }
            if (string.IsNullOrEmpty(json)) return WithError<T>("network:empty response");
            try
            {
                return JsonUtility.FromJson<T>(json) ?? WithError<T>("network:bad response");
            }
            catch (Exception e)
            {
                GameLogger.Warning($"[PvE] {function} returned unreadable JSON: {e.Message}");
                return WithError<T>("network:bad response");
            }
        }

        static T WithError<T>(string error) where T : class, new()
        {
            var t = new T();
            var f = typeof(T).GetField("error");
            f?.SetValue(t, error);
            return t;
        }

        /// <summary>UGS Cloud Code module calls. Requires UGS initialised and the player signed in.</summary>
        sealed class CloudCodeTransport : IPveTransport
        {
            public Task<string> Call(string function, Dictionary<string, object> args) =>
                CloudCodeService.Instance.CallModuleEndpointAsync<string>(Module, function, args);
        }
    }
}
