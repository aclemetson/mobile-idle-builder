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

        /// <summary>Push local save to the cloud.</summary>
        Task PushAsync(SaveData data);

        /// <summary>Returns true if a network connection is available and init succeeded.</summary>
        bool IsAvailable { get; }
    }
}
