using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// #502: the what's-new notes CI publishes (whatsnew.notes / whatsnew.version) show once per text, only on
    /// a build at least as new as the notes, and never to a brand-new player.
    /// </summary>
    public class WhatsNewTests
    {
        const string Notes = "- Tabs in the Builder\n- Free Research";

        static WhatsNew.Decision Decide(string app = "0.6.90", string forVersion = "0.6.90", string notes = Notes,
                                        string seen = null, bool newPlayer = false)
            => WhatsNew.Decide(app, forVersion, notes, seen, newPlayer);

        [Test]
        public void Shows_OnTheReleasedBuild() => Assert.AreEqual(WhatsNew.Decision.Show, Decide());

        [Test]
        public void Shows_OnANewerBuild_TooAHotfixPlayerStillGetsThem() =>
            Assert.AreEqual(WhatsNew.Decision.Show, Decide(app: "0.6.93"));

        [Test]
        public void Waits_UntilThePlayerHasTheBuild() =>
            Assert.AreEqual(WhatsNew.Decision.Hide, Decide(app: "0.6.89"));

        [Test]
        public void OncePerText_EvenAcrossVersions()
        {
            string seen = WhatsNew.Hash(Notes);
            Assert.AreEqual(WhatsNew.Decision.Hide, Decide(seen: seen));
            Assert.AreEqual(WhatsNew.Decision.Hide, Decide(app: "0.6.95", forVersion: "0.6.95", seen: seen),
                "a hotfix release republishes the same notes: nobody sees them twice");
            Assert.AreEqual(WhatsNew.Decision.Show, Decide(notes: Notes + "\n- Ad-free", seen: seen));
        }

        [Test]
        public void NewPlayers_AreMarkedSilently() =>
            Assert.AreEqual(WhatsNew.Decision.MarkSeenSilently, Decide(newPlayer: true));

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   \n ")]
        public void BlankNotes_ShowNothing(string notes) => Assert.AreEqual(WhatsNew.Decision.Hide, Decide(notes: notes));

        [TestCase(null)]
        [TestCase("")]
        [TestCase("soon")]
        public void UnreadableVersion_ShowsNothing(string forVersion) =>
            Assert.AreEqual(WhatsNew.Decision.Hide, Decide(forVersion: forVersion));

        [Test]
        public void Hash_IgnoresLineEndingsAndOuterSpace()
        {
            Assert.AreEqual(WhatsNew.Hash("a\nb"), WhatsNew.Hash("  a\r\nb \n"));
            Assert.AreNotEqual(WhatsNew.Hash("a\nb"), WhatsNew.Hash("a\nc"));
            Assert.AreEqual(8, WhatsNew.Hash("x").Length);
        }

        [Test]
        public void Title_NamesTheVersion()
        {
            Assert.AreEqual("What's new in 0.6.90", WhatsNew.Title("0.6.90"));
            Assert.AreEqual("What's new", WhatsNew.Title(""));
        }

        [Test]
        public void Flags_AreRegisteredForTheFetch()
        {
            bool version = false, notes = false;
            foreach (var f in FeatureFlags.All)
            {
                version |= f.Key == FeatureFlags.WhatsNewVersionKey;
                notes   |= f.Key == FeatureFlags.WhatsNewNotesKey;
            }
            Assert.IsTrue(version && notes);
        }
    }
}
