using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>Crystals on UGS Economy, phase 1: the wallet read, the one-time migration request, the kill-switch.</summary>
    public class CrystalWalletTests
    {
        sealed class FakeTransport : IPveTransport
        {
            public string Reply;
            public string LastFunction;
            public Dictionary<string, object> LastArgs;
            public int Calls;
            public Task<string> Call(string function, Dictionary<string, object> args)
            {
                Calls++; LastFunction = function; LastArgs = args;
                return Task.FromResult(Reply);
            }
        }

        IPveTransport _prev;
        FakeTransport _fake;

        [SetUp]
        public void SetUp()
        {
            CrystalWallet.ResetForTests();
            AccountSuspension.ResetForTests();
            _prev = PveApi.Transport;
            PveApi.Transport = _fake = new FakeTransport();
        }

        [TearDown]
        public void TearDown()
        {
            CrystalWallet.ResetForTests();
            PveApi.Transport = _prev;
        }

        static SaveData NewSave(long crystals)
        {
            var save = new SaveData { paidCurrency = crystals };
            CrystalWallet.SaveOverrideForTests = save;
            return save;
        }

        [Test]
        public void Off_DoesNothing()
        {
            CrystalWallet.EnabledOverrideForTests = false;
            Assert.IsFalse(CrystalWallet.RefreshAsync().GetAwaiter().GetResult());
            Assert.AreEqual(0, _fake.Calls);
        }

        [Test]
        public void Refresh_SendsTheSaveBalanceForMigration_AndCachesTheServerBalance()
        {
            CrystalWallet.EnabledOverrideForTests = true;
            var save = NewSave(1500);
            _fake.Reply = "{\"balance\":1700,\"migrated\":true,\"migratedAmount\":1500}";
            int changed = 0;
            void OnChanged() => changed++;
            CrystalWallet.BalanceChanged += OnChanged;
            try
            {
                Assert.IsTrue(CrystalWallet.RefreshAsync().GetAwaiter().GetResult());
            }
            finally { CrystalWallet.BalanceChanged -= OnChanged; }

            Assert.AreEqual("CrystalWallet", _fake.LastFunction);
            Assert.AreEqual(1500L, _fake.LastArgs["localBalance"]);
            Assert.AreEqual(1700, save.paidCurrency, "the server balance replaces the cached one");
            Assert.IsTrue(CrystalWallet.HasServerBalance);
            Assert.AreEqual(1, changed);
        }

        [Test]
        public void Error_KeepsTheCachedBalance()
        {
            CrystalWallet.EnabledOverrideForTests = true;
            var save = NewSave(42);
            _fake.Reply = "{\"error\":\"economy_config\"}";
            Assert.IsFalse(CrystalWallet.RefreshAsync().GetAwaiter().GetResult());
            Assert.AreEqual(42, save.paidCurrency);
            Assert.IsFalse(CrystalWallet.HasServerBalance);
        }

        [Test]
        public void Suspended_SendsNothing()
        {
            CrystalWallet.EnabledOverrideForTests = true;
            NewSave(10);
            AccountSuspension.MarkSuspended();
            try
            {
                Assert.IsFalse(CrystalWallet.RefreshAsync().GetAwaiter().GetResult());
                Assert.AreEqual(0, _fake.Calls);
            }
            finally { AccountSuspension.ResetForTests(); }
        }

        [Test]
        public void MigrationAmount_IsCapped()
        {
            Assert.AreEqual(0, CrystalRules.MigrationAmount(-1));
            Assert.AreEqual(600, CrystalRules.MigrationAmount(600));
            Assert.AreEqual(CrystalRules.MigrationCap, CrystalRules.MigrationAmount(CrystalRules.MigrationCap + 1));
        }
    }
}
