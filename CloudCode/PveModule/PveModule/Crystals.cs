using System;
using System.Threading.Tasks;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;

namespace MobileIdleBuilder.PveModule
{
    /// <summary>
    /// Crystals on UGS Economy (docs/agents/tasks/feature-economy-crystals.md). Lives in this module so it shares the
    /// deploy and the tournament Claim can pay crystals directly. Every Economy call uses the service token: an Access
    /// Control policy denies players any Economy write, so only these functions change a balance. Rules and models are
    /// shared with the client (CrystalRules.cs).
    /// </summary>
    public class Crystals
    {
        readonly IGameApiClient _api;

        public Crystals(IGameApiClient api) => _api = api;

        static string Ser<T>(T v) => PveStore.Serialize(v);

        /// <summary>
        /// The caller's balance. The first call for a player also carries over the save's balance once
        /// (<paramref name="localBalance"/>, capped by CrystalRules.MigrationCap); the ledger records it, so a second
        /// device or a retry never credits it again.
        /// </summary>
        [CloudCodeFunction("CrystalWallet")]
        public async Task<string> CrystalWallet(IExecutionContext ctx, long localBalance = 0)
        {
            var store = new PveStore(_api, ctx);
            long? balance = await store.GetCrystals(ctx.PlayerId);
            if (balance == null) return Ser(new CrystalWalletResponse { error = CrystalErrors.EconomyConfig });

            var read = await store.GetLedger(ctx.PlayerId);
            var ledger = read.Value ?? new CrystalLedger();
            if (ledger.migrated)
                return Ser(new CrystalWalletResponse { balance = balance.Value, migrated = true });

            long amount = CrystalRules.MigrationAmount(localBalance);
            ledger.migrated = true;
            ledger.migratedAmount = amount;
            ledger.migratedUtc = PveRules.Iso(DateTime.UtcNow);
            try
            {
                // The write lock makes this the only migration: a concurrent call (second device) fails here.
                await store.SetLedger(ctx.PlayerId, ledger, read.WriteLock);
            }
            catch (Exception e) when (PveStore.IsLockConflict(e))
            {
                return Ser(new CrystalWalletResponse { balance = await store.GetCrystals(ctx.PlayerId) ?? 0, migrated = true });
            }

            if (amount > 0)
            {
                try
                {
                    balance = await store.IncrementCrystals(ctx.PlayerId, amount);
                }
                catch
                {
                    // Undo the ledger so the next call retries the migration instead of losing the crystals.
                    try { await store.SetLedger(ctx.PlayerId, new CrystalLedger(), null); } catch { /* best effort */ }
                    throw;
                }
            }
            return Ser(new CrystalWalletResponse { balance = balance.Value, migrated = true, migratedAmount = amount });
        }
    }
}
