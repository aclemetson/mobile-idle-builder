using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MobileIdleBuilder
{
    /// <summary>Where an account deletion stopped. Nothing was deleted locally in any of these cases.</summary>
    public enum AccountDeletionFailure
    {
        None,
        Busy,        // a deletion is already running
        Offline,     // no UGS session: nothing could be deleted
        ServerData,  // the Cloud Code delete failed: account and data untouched (or partly cleared, safe to retry)
        Account,     // the server data is gone but the UGS account delete failed: retrying finishes the job
    }

    /// <summary>The server-side steps, behind an interface so the order and failure handling can be tested.</summary>
    public interface IAccountDeletionBackend
    {
        bool IsOnline { get; }
        Task SuspendCloudSaveAsync();
        void ResumeCloudSave();
        /// <summary>Removes the player from their PvE bracket and deletes all their Cloud Save data. Null/"" = ok, else the error.</summary>
        Task<string> DeleteServerDataAsync();
        /// <summary>Deletes the UGS account. Throws on failure.</summary>
        Task DeleteAccountAsync();
    }

    /// <summary>
    /// Settings "Delete account" (store requirement: in-app account deletion). Server data first, the account last,
    /// local data only once both succeeded, so a failure at any step leaves the player able to retry:
    ///  1. stop cloud pushes (and wait for one in flight), so nothing recreates the save after step 2;
    ///  2. Cloud Code DeleteMyData: off the PvE bracket, every Cloud Save key deleted (the game save included);
    ///  3. delete the UGS account (and its Google link);
    ///  4. <see cref="ResetLocalGameAsync"/>: full local wipe, a brand-new guest player, reload as a new game.
    /// Spec: docs/agents/tasks/feature-account-deletion.md.
    /// </summary>
    public static class AccountDeletion
    {
        public static bool InProgress { get; private set; }

        /// <summary>Steps 1-3. Returns <see cref="AccountDeletionFailure.None"/> when the account is gone.</summary>
        public static async Task<(AccountDeletionFailure failure, string error)> RunAsync(IAccountDeletionBackend backend)
        {
            if (InProgress) return (AccountDeletionFailure.Busy, null);
            if (backend == null || !backend.IsOnline) return (AccountDeletionFailure.Offline, null);

            InProgress = true;
            try
            {
                await backend.SuspendCloudSaveAsync();

                string error;
                try { error = await backend.DeleteServerDataAsync(); }
                catch (Exception e) { error = e.Message; }
                if (!string.IsNullOrEmpty(error))
                {
                    backend.ResumeCloudSave();
                    GameLogger.Warning($"[AccountDeletion] Server data delete failed: {error}");
                    return (AccountDeletionFailure.ServerData, error);
                }

                try { await backend.DeleteAccountAsync(); }
                catch (Exception e)
                {
                    backend.ResumeCloudSave();
                    GameLogger.Warning($"[AccountDeletion] Account delete failed: {e.Message}");
                    return (AccountDeletionFailure.Account, e.Message);
                }

                GameLogger.Info("[AccountDeletion] Server data and account deleted.");
                return (AccountDeletionFailure.None, null);
            }
            finally
            {
                InProgress = false;
            }
        }

        /// <summary>The line Settings shows under the account after a failed attempt.</summary>
        public static string FailureMessage(AccountDeletionFailure failure, string error) => failure switch
        {
            AccountDeletionFailure.None    => null,
            AccountDeletionFailure.Busy    => "Already deleting your account...",
            AccountDeletionFailure.Offline => "Can't delete your account while offline. Connect and try again, or email us from the privacy policy.",
            AccountDeletionFailure.Account => "Your saved data was deleted but the account wasn't. Tap Delete account again to finish.",
            _                              => $"Couldn't delete your account ({Shorten(error)}). Nothing was deleted on this device. Try again later.",
        };

        static string Shorten(string error)
        {
            if (string.IsNullOrEmpty(error)) return "unknown error";
            return error.Length <= 80 ? error : error.Substring(0, 80) + "...";
        }

        /// <summary>
        /// Step 4, after the account is gone: wipes everything on the device the way a fresh install starts, signs
        /// in a new guest (after the wipe, which clears the auth session keys too), resets the persistent services
        /// that would otherwise write the old run back, and reloads the scene as a new game with the tutorial.
        /// Same reset list as the dev console's "clear all save".
        /// </summary>
        public static async Task ResetLocalGameAsync()
        {
            var sm = SaveManager.Instance;
            GridSaveService.Instance?.ClearGrid();
            SaveWipe.WipeEverything(scheduleCloudWipe: false);

            string newPlayerId = null;
            if (sm?.Cloud != null && await sm.Cloud.StartNewGuestAsync())
                newPlayerId = sm.Cloud.AccountPlayerId;

            sm?.ResetToFreshSave(newPlayerId);
            sm?.ResumeCloudPushes();
            PersistentUpgradeService.Instance?.LoadFromSave(new List<string>());
            AchievementService.Instance?.ResetInMemory();
            RecipeKnowledgeService.Instance?.ResetInMemory();
            SettingsService.Instance?.ResetToDefaults();
            TournamentService.Instance?.ResetForNewAccount();

            GameLogger.Info($"[AccountDeletion] Local data wiped; new player {newPlayerId ?? "(offline)"}. Reloading.");
            SceneLoader.GoTo(SceneManager.GetActiveScene().name);
        }
    }

    /// <summary>The real backend: SaveManager's cloud service and the PvE Cloud Code module.</summary>
    public sealed class UgsAccountDeletionBackend : IAccountDeletionBackend
    {
        static ICloudSaveService Cloud => SaveManager.Instance?.Cloud;

        public bool IsOnline => Cloud != null && Cloud.IsAvailable && !string.IsNullOrEmpty(Cloud.AccountPlayerId);

        public Task SuspendCloudSaveAsync() =>
            SaveManager.Instance != null ? SaveManager.Instance.SuspendCloudPushesAsync() : Task.CompletedTask;

        public void ResumeCloudSave() => SaveManager.Instance?.ResumeCloudPushes();

        public async Task<string> DeleteServerDataAsync()
        {
            var res = await PveApi.DeleteMyData();
            if (res != null && res.deleted && string.IsNullOrEmpty(res.error)) return null;
            return string.IsNullOrEmpty(res?.error) ? "the server did not confirm" : res.error;
        }

        public Task DeleteAccountAsync() => Cloud.DeleteAccountAsync();
    }
}
