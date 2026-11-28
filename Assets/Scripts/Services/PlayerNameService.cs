using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;

namespace MobileIdleBuilder
{
    /// <summary>Where the player name really lives. UGS in the game; a fake in tests.</summary>
    public interface IPlayerNameBackend
    {
        /// <summary>True when a name can be read or written now (signed in to UGS).</summary>
        bool IsReady { get; }

        /// <summary>The stored name ("Name#1234"), or null/"" when the player never chose one.</summary>
        Task<string> GetAsync();

        /// <summary>Stores <paramref name="name"/> and returns the stored form (UGS appends "#1234").</summary>
        Task<string> SetAsync(string name);
    }

    /// <summary>Outcome of <see cref="PlayerNameService.TrySetAsync"/>.</summary>
    public enum NameSetStatus { Saved, Invalid, Offline, Rejected, Failed }

    public readonly struct NameSetResult
    {
        public readonly NameSetStatus Status;
        public readonly NameCheck     Check;
        public readonly string        Name;     // the stored name on success
        public readonly string        Message;  // player-facing reason on failure

        public NameSetResult(NameSetStatus status, NameCheck check, string name, string message)
        { Status = status; Check = check; Name = name; Message = message; }

        public bool Ok => Status == NameSetStatus.Saved;
    }

    /// <summary>
    /// The public player name (PvE phase 1): set through UGS Authentication player names, cached in
    /// <see cref="SaveData.playerName"/> for offline display. Validation is <see cref="PlayerNameRules"/>
    /// with <c>game_config.name_filter_patterns</c>.
    /// </summary>
    public static class PlayerNameService
    {
        /// <summary>Swapped by tests; the UGS backend otherwise.</summary>
        public static IPlayerNameBackend Backend { get; set; } = new UgsPlayerNameBackend();

        /// <summary>Raised on the main thread after the cached name changes.</summary>
        public static event Action<string> OnNameChanged;

        /// <summary>The cached full name ("Name#1234"), or "" when none is set.</summary>
        public static string Current => SaveManager.Instance?.Current?.playerName ?? "";

        public static bool HasName => !string.IsNullOrEmpty(Current);

        /// <summary>The name without its "#1234" tag, or "" when none is set.</summary>
        public static string DisplayName => PlayerNameRules.WithoutTag(Current);

        /// <summary>Filter patterns from the live game config (empty when it is missing).</summary>
        public static IReadOnlyList<string> Patterns =>
            (IReadOnlyList<string>)SaveManager.Instance?.GameConfig?.nameFilterPatterns ?? Array.Empty<string>();

        /// <summary>Validates <paramref name="raw"/> and stores it with UGS; updates the save on success.</summary>
        public static async Task<NameSetResult> TrySetAsync(string raw, IReadOnlyList<string> patterns = null)
        {
            string name  = PlayerNameRules.Clean(raw);
            var    check = PlayerNameRules.Validate(name, patterns ?? Patterns);
            if (check != NameCheck.Ok)
                return new NameSetResult(NameSetStatus.Invalid, check, null, PlayerNameRules.Message(check));

            var backend = Backend;
            if (backend == null || !backend.IsReady)
                return new NameSetResult(NameSetStatus.Offline, check, null,
                    "You need to be online to set a name. Try again later.");

            string stored;
            try
            {
                stored = await backend.SetAsync(name);
            }
            catch (AuthenticationException e) when (e.ErrorCode == AuthenticationErrorCodes.InvalidParameters)
            {
                GameLogger.Warning($"[PlayerName] UGS refused the name: {e.Message}");
                return new NameSetResult(NameSetStatus.Rejected, check, null, "That name isn't allowed. Try another.");
            }
            catch (RequestFailedException e) when (e.ErrorCode == CommonErrorCodes.TransportError)
            {
                GameLogger.Warning($"[PlayerName] Network error setting the name: {e.Message}");
                return new NameSetResult(NameSetStatus.Offline, check, null,
                    "You need to be online to set a name. Try again later.");
            }
            catch (Exception e)
            {
                GameLogger.Warning($"[PlayerName] Setting the name failed: {e.Message}");
                return new NameSetResult(NameSetStatus.Failed, check, null, "Couldn't save the name. Try again.");
            }

            if (string.IsNullOrEmpty(stored)) stored = name;
            Store(stored);
            GameLogger.Info($"[PlayerName] Name set to {stored}");
            return new NameSetResult(NameSetStatus.Saved, check, stored, "");
        }

        /// <summary>
        /// Reads the name from UGS and updates the cache: UGS wins, so a name set on another device (same
        /// account) shows here. Does nothing when offline or on error; the cache stays as it was.
        /// </summary>
        public static async Task RefreshAsync()
        {
            var backend = Backend;
            if (backend == null || !backend.IsReady) return;
            string server;
            try { server = await backend.GetAsync(); }
            catch (Exception e)
            {
                AccountSuspension.Report(e);
                GameLogger.Warning($"[PlayerName] Reading the name failed: {e.Message}");
                return;
            }
            server ??= "";
            if (server != Current) Store(server);
        }

        static void Store(string name)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;
            save.playerName = name ?? "";
            SaveManager.Instance.SaveLocal();
            OnNameChanged?.Invoke(save.playerName);
        }
    }

    /// <summary>UGS Authentication player names. Never auto-generates one: "" means the player has not chosen.</summary>
    public sealed class UgsPlayerNameBackend : IPlayerNameBackend
    {
        public bool IsReady
        {
            get
            {
                try
                {
                    return UnityServices.State == ServicesInitializationState.Initialized
                           && AuthenticationService.Instance.IsSignedIn;
                }
                catch (Exception) { return false; }
            }
        }

        public Task<string> GetAsync() => AuthenticationService.Instance.GetPlayerNameAsync(autoGenerate: false);

        public Task<string> SetAsync(string name) => AuthenticationService.Instance.UpdatePlayerNameAsync(name);
    }
}
