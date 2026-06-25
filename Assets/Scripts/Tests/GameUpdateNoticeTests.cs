using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Tests the pure decision seam for the game-data update notice. The remote stamp comes from
    /// <c>gamedata.updatedUtc</c> and the saved marker from <c>SaveData.lastSeenGameDataUtc</c>;
    /// <see cref="GameUpdateNotice.ShouldShow"/> decides whether the modal fires. Assertions avoid the
    /// local timezone by using explicit UTC ('Z') / offset stamps that normalize to the same instant.
    /// </summary>
    [TestFixture]
    public class GameUpdateNoticeTests
    {
        [Test]
        public void ShouldShow_RemoteNewerThanSaved_True()
        {
            Assert.IsTrue(GameUpdateNotice.ShouldShow("2026-06-24T12:00:00Z", "2026-06-20T12:00:00Z"));
        }

        [Test]
        public void ShouldShow_NeverAcknowledged_ValidRemote_True()
        {
            Assert.IsTrue(GameUpdateNotice.ShouldShow("2026-06-24T12:00:00Z", null));
            Assert.IsTrue(GameUpdateNotice.ShouldShow("2026-06-24T12:00:00Z", ""));
            Assert.IsTrue(GameUpdateNotice.ShouldShow("2026-06-24T12:00:00Z", "   "));
        }

        [Test]
        public void ShouldShow_RemoteEqualsSaved_False()
        {
            Assert.IsFalse(GameUpdateNotice.ShouldShow("2026-06-24T12:00:00Z", "2026-06-24T12:00:00Z"));
        }

        [Test]
        public void ShouldShow_RemoteOlderThanSaved_False()
        {
            Assert.IsFalse(GameUpdateNotice.ShouldShow("2026-06-20T12:00:00Z", "2026-06-24T12:00:00Z"));
        }

        [Test]
        public void ShouldShow_RemoteBlank_False()
        {
            Assert.IsFalse(GameUpdateNotice.ShouldShow(null, "2026-06-24T12:00:00Z"));
            Assert.IsFalse(GameUpdateNotice.ShouldShow("", "2026-06-24T12:00:00Z"));
            Assert.IsFalse(GameUpdateNotice.ShouldShow("   ", null));
        }

        [Test]
        public void ShouldShow_RemoteGarbage_False()
        {
            Assert.IsFalse(GameUpdateNotice.ShouldShow("not-a-date", "2026-06-24T12:00:00Z"));
            Assert.IsFalse(GameUpdateNotice.ShouldShow("2026-13-99", null));
        }

        [Test]
        public void ShouldShow_SavedGarbage_TreatedAsNeverAcknowledged_True()
        {
            // An unparseable saved marker must not suppress a valid remote stamp (fail toward showing).
            Assert.IsTrue(GameUpdateNotice.ShouldShow("2026-06-24T12:00:00Z", "garbage"));
        }

        [Test]
        public void ShouldShow_OffsetStamp_NormalizesToUtc()
        {
            // 2026-06-24T12:00:00Z == 2026-06-24T14:00:00+02:00 (same instant) => not newer, no notice.
            Assert.IsFalse(GameUpdateNotice.ShouldShow("2026-06-24T14:00:00+02:00", "2026-06-24T12:00:00Z"));
            // One minute later in a different offset IS newer.
            Assert.IsTrue(GameUpdateNotice.ShouldShow("2026-06-24T14:01:00+02:00", "2026-06-24T12:00:00Z"));
        }
    }
}
