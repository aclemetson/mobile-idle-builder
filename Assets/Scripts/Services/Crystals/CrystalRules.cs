using System;
using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Crystals on UGS Economy (docs/agents/tasks/feature-economy-crystals.md): rules and models shared by the
    /// client and the Cloud Code module (compiled into both, like PveRules). The server holds the balance;
    /// SaveData.paidCurrency is only the display cache while <c>economy.enabled</c> is on.
    /// </summary>
    public static class CrystalRules
    {
        /// <summary>The Economy currency id (dashboard: Economy > Currencies).</summary>
        public const string CurrencyId = "CRYSTALS";

        /// <summary>Protected Cloud Save player item: the client may read it, only the server writes it.</summary>
        public const string LedgerKey = "crystal_ledger";

        /// <summary>
        /// Most crystals the one-time migration carries over from a save: ten of the largest pack (20,000◆). Real
        /// balances are far below; an edited save can't mint more than this.
        /// </summary>
        public const long MigrationCap = 200_000;

        /// <summary>What the one-time migration credits for a save balance of <paramref name="localBalance"/>.</summary>
        public static long MigrationAmount(long localBalance) =>
            localBalance <= 0 ? 0 : Math.Min(localBalance, MigrationCap);
    }

    /// <summary>The server's per-player crystal record (Cloud Save protected item <see cref="CrystalRules.LedgerKey"/>).</summary>
    [Serializable]
    public class CrystalLedger
    {
        public bool migrated;           // the save balance was carried over (once per player)
        public long migratedAmount;
        public string migratedUtc;
    }

    /// <summary>CrystalWallet response: the server balance after any migration.</summary>
    [Serializable]
    public class CrystalWalletResponse
    {
        public string error;            // null/"" = ok; CrystalErrors or "network:..." / PveErrors.Suspended from the client
        public long balance;
        public bool migrated;
        public long migratedAmount;     // credited by THIS call (0 unless it ran the migration)
    }

    public static class CrystalErrors
    {
        public const string EconomyConfig = "economy_config";   // the CRYSTALS currency is not set up in this environment
    }
}
