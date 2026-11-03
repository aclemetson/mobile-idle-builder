using System;
using System.Threading.Tasks;

namespace MobileIdleBuilder
{
    public interface ICloudSaveService
    {
        /// <summary>One-time async init (UGS auth + service setup). Must complete before Fetch/Push.</summary>
        Task InitializeAsync();

        /// <summary>Fetch the latest save from the cloud. Returns null if not found.</summary>
        Task<SaveData> FetchAsync(string playerId);

        /// <summary>
        /// Push local save to the cloud. Never throws; resolves to true only if the write reached the
        /// cloud. The SaveData must be serialized synchronously, before the first await, so a caller
        /// that is about to be suspended (app backgrounded) still pushes the state it had at call time.
        /// </summary>
        Task<bool> PushAsync(SaveData data);

        /// <summary>Returns true if a network connection is available and init succeeded.</summary>
        bool IsAvailable { get; }

        /// <summary>Delete the cloud save key for the signed-in player. No-op if unavailable.</summary>
        Task DeleteAsync();
    }
}
