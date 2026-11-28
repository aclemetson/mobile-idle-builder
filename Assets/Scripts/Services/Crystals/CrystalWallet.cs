using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Client side of Crystals on UGS Economy (docs/agents/tasks/feature-economy-crystals.md). While
    /// <c>economy.enabled</c> is on, the server holds the balance and <see cref="SaveData.paidCurrency"/> is only the
    /// cached copy the HUD shows (and shows offline). Off: nothing here runs and the save is the balance, as shipped.
    ///
    /// Phase 1 (this): read the balance, running the one-time migration of the save's balance on the first call.
    /// Spends and grants move here in phases 2-4.
    /// </summary>
    public static class CrystalWallet
    {
        public static bool Enabled => EnabledOverrideForTests ?? FeatureFlags.EconomyEnabled;

        /// <summary>Test seam: forces <see cref="Enabled"/> without Remote Config.</summary>
        internal static bool? EnabledOverrideForTests;

        /// <summary>The server balance has been read this session (spends need it, phase 2).</summary>
        public static bool HasServerBalance { get; private set; }

        /// <summary>Raised after the cached balance changed to the server's value.</summary>
        public static event Action BalanceChanged;

        static Task<bool> s_refresh;

        /// <summary>
        /// Reads the server balance into the save cache. The first call for a player also carries the save's balance
        /// over to the server once (capped). Concurrent callers share one request. False when off, offline,
        /// suspended or on error (the cache keeps its last value).
        /// </summary>
        public static Task<bool> RefreshAsync()
        {
            if (!Enabled) return Task.FromResult(false);
            if (s_refresh != null && !s_refresh.IsCompleted) return s_refresh;
            return s_refresh = RefreshCore();
        }

        static async Task<bool> RefreshCore()
        {
            var sm = SaveManager.Instance;
            // The first call migrates the save's balance, so wait until the "which save?" choice has settled
            // which save this is.
            while (sm != null && sm.AwaitingSaveChoice) await Task.Delay(500);
            var save = CurrentSave();
            if (save == null) return false;
            var r = await CrystalApi.Wallet(save.paidCurrency);
            if (!string.IsNullOrEmpty(r.error))
            {
                GameLogger.Warning($"[Crystals] Wallet refresh failed: {r.error}");
                return false;
            }
            if (r.migratedAmount > 0)
                GameLogger.Info($"[Crystals] Moved {r.migratedAmount}◆ from the save to the server (one time).");
            ApplyServerBalance(CurrentSave() ?? save, r.balance);
            HasServerBalance = true;
            return true;
        }

        /// <summary>Writes the server's balance into the save cache (it is saved with the next autosave).</summary>
        internal static void ApplyServerBalance(SaveData save, long balance)
        {
            if (save == null) return;
            if (save.paidCurrency != balance)
            {
                GameLogger.Info($"[Crystals] Balance {save.paidCurrency}◆ -> {balance}◆ (server).");
                save.paidCurrency = balance;
            }
            try { BalanceChanged?.Invoke(); }
            catch (Exception e) { GameLogger.Error($"[Crystals] A BalanceChanged handler failed: {e}"); }
        }

        static SaveData CurrentSave() => SaveOverrideForTests ?? SaveManager.Instance?.Current;

        /// <summary>Test seam: the save to read and cache into, instead of SaveManager's.</summary>
        internal static SaveData SaveOverrideForTests;

        /// <summary>Test seam.</summary>
        internal static void ResetForTests()
        {
            HasServerBalance = false; s_refresh = null; EnabledOverrideForTests = null; SaveOverrideForTests = null;
        }
    }

    /// <summary>Crystal functions in the Cloud Code module (CloudCode/PveModule/PveModule/Crystals.cs).</summary>
    public static class CrystalApi
    {
        public static Task<CrystalWalletResponse> Wallet(long localBalance) =>
            PveApi.Send<CrystalWalletResponse>("CrystalWallet",
                new Dictionary<string, object> { { "localBalance", localBalance } });
    }
}
