using NUnit.Framework;
using Unity.Entities;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// EntropyWallet is the single entropy spend path (#279). TrySpend is all-or-nothing;
    /// SpendUpTo is the post-placement clamp; ReducedCost must match the original formula exactly
    /// so displayed, charged and refunded building prices agree.
    /// </summary>
    [TestFixture]
    public class EntropyWalletTests
    {
        World       _world;
        EntityManager _em;
        Entity      _player;
        EntityQuery _query;

        [SetUp]
        public void SetUp()
        {
            _world  = new World("EntropyWalletTest");
            _em     = _world.EntityManager;
            _player = _em.CreateEntity();
            _em.AddComponent<PlayerProgressData>(_player);
            _query  = _em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
        }

        [TearDown]
        public void TearDown()
        {
            if (_world.IsCreated) _world.Dispose();
        }

        void SetBalance(long baseCurrency, long spent = 0) =>
            _em.SetComponentData(_player, new PlayerProgressData { BaseCurrency = baseCurrency, TotalEntropySpent = spent });

        PlayerProgressData Progress => _em.GetComponentData<PlayerProgressData>(_player);

        // ── Balance ──────────────────────────────────────────────────────────

        [Test]
        public void Balance_ReadsBaseCurrency()
        {
            SetBalance(1234);
            Assert.AreEqual(1234, EntropyWallet.Balance(_query));
        }

        [Test]
        public void Balance_NoSingleton_IsZero()
        {
            _em.DestroyEntity(_player);
            Assert.AreEqual(0, EntropyWallet.Balance(_query));
        }

        // ── TrySpend ─────────────────────────────────────────────────────────

        [Test]
        public void TrySpend_ExactBalance_SpendsAll()
        {
            SetBalance(100, spent: 5);
            Assert.IsTrue(EntropyWallet.TrySpend(_query, 100));
            Assert.AreEqual(0,   Progress.BaseCurrency);
            Assert.AreEqual(105, Progress.TotalEntropySpent, "TotalEntropySpent rises by the same amount");
        }

        [Test]
        public void TrySpend_ShortByOne_RefusesAndChangesNothing()
        {
            SetBalance(99, spent: 5);
            Assert.IsFalse(EntropyWallet.TrySpend(_query, 100));
            Assert.AreEqual(99, Progress.BaseCurrency);
            Assert.AreEqual(5,  Progress.TotalEntropySpent);
        }

        [Test]
        public void TrySpend_Zero_SucceedsWithoutChange()
        {
            SetBalance(10);
            Assert.IsTrue(EntropyWallet.TrySpend(_query, 0));
            Assert.AreEqual(10, Progress.BaseCurrency);
            Assert.AreEqual(0,  Progress.TotalEntropySpent);
        }

        [Test]
        public void TrySpend_Negative_IsRefused_NeverGrantsEntropy()
        {
            SetBalance(10);
            Assert.IsFalse(EntropyWallet.TrySpend(_query, -50));
            Assert.AreEqual(10, Progress.BaseCurrency);
        }

        [Test]
        public void TrySpend_NoSingleton_IsRefused()
        {
            _em.DestroyEntity(_player);
            Assert.IsFalse(EntropyWallet.TrySpend(_query, 1));
        }

        [Test]
        public void TrySpend_BeyondIntRange_Works()
        {
            long big = 5_000_000_000L;
            SetBalance(big);
            Assert.IsTrue(EntropyWallet.TrySpend(_query, big - 1));
            Assert.AreEqual(1, Progress.BaseCurrency);
        }

        // ── SpendUpTo (post-placement charge) ────────────────────────────────

        [Test]
        public void SpendUpTo_Affordable_ChargesFullAmount()
        {
            SetBalance(500);
            Assert.AreEqual(200, EntropyWallet.SpendUpTo(_query, 200));
            Assert.AreEqual(300, Progress.BaseCurrency);
            Assert.AreEqual(200, Progress.TotalEntropySpent);
        }

        [Test]
        public void SpendUpTo_Short_ChargesWhatIsThere_NeverNegative()
        {
            SetBalance(150);
            Assert.AreEqual(150, EntropyWallet.SpendUpTo(_query, 200));
            Assert.AreEqual(0,   Progress.BaseCurrency);
            Assert.AreEqual(150, Progress.TotalEntropySpent);
        }

        [Test]
        public void SpendUpTo_ZeroOrNegative_ChargesNothing()
        {
            SetBalance(150);
            Assert.AreEqual(0, EntropyWallet.SpendUpTo(_query, 0));
            Assert.AreEqual(0, EntropyWallet.SpendUpTo(_query, -10));
            Assert.AreEqual(150, Progress.BaseCurrency);
        }

        // ── ReducedCost ──────────────────────────────────────────────────────

        [TestCase(200,  0f,    200)]
        [TestCase(200,  0.25f, 150)]   // exactly representable reductions only; the
        [TestCase(333,  0.5f,  166)]   // 166.5 truncates. Inexact ones are covered below.
        [TestCase(0,    0.5f,  0)]
        [TestCase(-5,   0f,    0)]
        [TestCase(100,  1.5f,  0)]     // never negative
        public void ReducedCost_Values(long raw, float reduction, long expected) =>
            Assert.AreEqual(expected, EntropyWallet.ReducedCost(raw, reduction));

        [Test]
        public void ReducedCost_MatchesTheOriginalIntFormula()
        {
            // The formula previously copied into HUDController (display + charge) and DemolishRefund.
            foreach (int raw in new[] { 1, 25, 100, 150, 225, 350, 999, 3000, 80_000, 1_000_000 })
            foreach (float r in new[] { 0f, 0.05f, 0.1f, 0.15f, 0.2f, 0.25f, 0.3f, 0.5f })
                Assert.AreEqual((long)(int)(raw * (1f - r)), EntropyWallet.ReducedCost(raw, r), $"raw={raw} r={r}");
        }
    }
}
