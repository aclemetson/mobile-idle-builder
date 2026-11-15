using System;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Cross-device saves (docs/agents/save-system.md): the local-vs-cloud decision, the sync marker,
    /// the save summaries shown in the "Two saves found" choice and the Settings account text.
    /// </summary>
    [TestFixture]
    public class SaveSyncTests
    {
        const string P = "player-1";
        const string T9  = "2026-11-14T09:00:00.0000000Z";
        const string T10 = "2026-11-14T10:00:00.0000000Z";
        const string T11 = "2026-11-14T11:00:00.0000000Z";

        static SaveData Save(string stamp) => new SaveData { lastSaved = stamp };
        static SaveSync.Marker Marker(string player, string stamp, ulong hash) =>
            SaveSync.ParseMarker(SaveSync.FormatMarker(player, stamp, hash));

        static SaveSyncAction Decide(SaveData local, ulong hash, SaveData cloud, SaveSync.Marker marker,
                                     bool isNew = false, bool switched = false) =>
            SaveSync.Decide(local, hash, isNew, cloud, marker, P, switched);

        [Test]
        public void NoCloudSave_KeepsLocal() =>
            Assert.AreEqual(SaveSyncAction.KeepLocal, Decide(Save(T10), 1, null, default));

        [Test]
        public void NewGame_TakesCloud() =>
            Assert.AreEqual(SaveSyncAction.TakeCloud, Decide(Save(null), 1, Save(T9), default, isNew: true));

        [Test]
        public void NoMarker_NewerStampWins()
        {
            Assert.AreEqual(SaveSyncAction.TakeCloud, Decide(Save(T9), 1, Save(T10), default));
            Assert.AreEqual(SaveSyncAction.KeepLocal, Decide(Save(T11), 1, Save(T10), default));
        }

        [Test]
        public void OnlyCloudChanged_TakesCloud() =>
            Assert.AreEqual(SaveSyncAction.TakeCloud, Decide(Save(T11), 5, Save(T10), Marker(P, T9, 5)));

        [Test]
        public void OnlyLocalChanged_KeepsLocal() =>
            Assert.AreEqual(SaveSyncAction.KeepLocal, Decide(Save(T11), 6, Save(T9), Marker(P, T9, 5)));

        [Test]
        public void NeitherChanged_KeepsLocal_EvenThoughTheLocalStampMoved() =>
            Assert.AreEqual(SaveSyncAction.KeepLocal, Decide(Save(T11), 5, Save(T9), Marker(P, T9, 5)),
                "every autosave re-stamps the file; only content counts on the local side");

        [Test]
        public void BothChanged_Asks() =>
            Assert.AreEqual(SaveSyncAction.AskPlayer, Decide(Save(T11), 6, Save(T10), Marker(P, T9, 5)));

        [Test]
        public void AccountSwitched_Asks() =>
            Assert.AreEqual(SaveSyncAction.AskPlayer, Decide(Save(T11), 6, Save(T10), default, switched: true));

        [Test]
        public void MarkerFromAnotherPlayer_Asks() =>
            Assert.AreEqual(SaveSyncAction.AskPlayer, Decide(Save(T11), 5, Save(T10), Marker("guest-7", T9, 5)));

        [Test]
        public void AccountSwitched_ButFreshInstall_TakesCloudWithoutAsking() =>
            Assert.AreEqual(SaveSyncAction.TakeCloud, Decide(Save(null), 1, Save(T10), default, isNew: true, switched: true));

        [Test]
        public void Marker_RoundTrips_AndRejectsGarbage()
        {
            var m = Marker(P, T10, 18446744073709551615UL);
            Assert.IsTrue(m.IsSet);
            Assert.AreEqual(P, m.PlayerId);
            Assert.AreEqual(T10, m.CloudStamp);
            Assert.AreEqual(ulong.MaxValue, m.LocalHash);
            Assert.IsFalse(SaveSync.ParseMarker("").IsSet);
            Assert.IsFalse(SaveSync.ParseMarker("a|b").IsSet);
            Assert.IsFalse(SaveSync.ParseMarker("a|b|notanumber").IsSet);
        }

        [Test]
        public void ContentHash_IgnoresTheStamp_AndLeavesItAlone()
        {
            var a = new SaveData { prestigeCount = 2, lastSaved = T9 };
            var b = new SaveData { prestigeCount = 2, lastSaved = T10 };
            Assert.AreEqual(LocalSaveService.ContentHash(a), LocalSaveService.ContentHash(b));
            Assert.AreEqual(T9, a.lastSaved);
            b.prestigeCount = 3;
            Assert.AreNotEqual(LocalSaveService.ContentHash(a), LocalSaveService.ContentHash(b));
        }

        [Test]
        public void Summary_ShowsPrestigeResearchNetWorthAndAge()
        {
            var save = new SaveData { prestigeCount = 3, lastKnownNetWorth = 1500f, lastSaved = T9 };
            save.unlockedResearch.Add("a");
            save.unlockedResearch.Add("b");
            string s = SaveSync.Summary(save, new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc));
            StringAssert.Contains("Prestige 3, 2 research", s);
            StringAssert.Contains("Net worth", s);
            StringAssert.Contains("Last played 3 h ago", s);
        }

        [TestCase(30,      "just now")]
        [TestCase(300,     "5 min ago")]
        [TestCase(7200,    "2 h ago")]
        [TestCase(86400,   "1 day ago")]
        [TestCase(259200,  "3 days ago")]
        public void Ago_Buckets(int secondsAgo, string expected)
        {
            var now = new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual(expected, SaveSync.Ago(now.AddSeconds(-secondsAgo).ToString("o"), now));
            Assert.AreEqual("unknown", SaveSync.Ago(null, now));
        }

        // ── Settings account row ──────────────────────────────────────────

        [Test]
        public void AccountText_Google_ShowsEmailAndShortId()
        {
            string s = HUDSettingsSubController.AccountText(CloudAccountKind.Google, "me@example.com",
                                                             "abcdefghijklmnop", null, null);
            StringAssert.Contains("Google: me@example.com", s);
            StringAssert.Contains("Player ID abcdefgh", s);
            StringAssert.DoesNotContain("ijkl", s);
        }

        [Test]
        public void AccountText_Guest_SaysDeviceOnly_AndShowsTheLastError()
        {
            string s = HUDSettingsSubController.AccountText(CloudAccountKind.Guest, null, "guest123", "audience mismatch", null);
            StringAssert.Contains("this device only", s);
            StringAssert.Contains("Last Google sign-in failed: audience mismatch", s);
        }

        [Test]
        public void AccountText_Offline_HasNoPlayerId() =>
            StringAssert.DoesNotContain("Player ID",
                HUDSettingsSubController.AccountText(CloudAccountKind.None, null, "x", null, null));

        // ── UXML ──────────────────────────────────────────────────────────

        [TestCase("save-choice-modal")]
        [TestCase("save-choice__local")]
        [TestCase("save-choice__cloud")]
        [TestCase("save-choice__local-summary")]
        [TestCase("save-choice__cloud-summary")]
        [TestCase("save-choice__confirm")]
        [TestCase("save-choice__back")]
        [TestCase("settings-account")]
        [TestCase("btn-google-sign-in")]
        public void GameHudUxml_HasElement(string name)
        {
            var doc = System.Xml.Linq.XDocument.Load("Assets/UI/GameHUD.uxml");
            bool found = false;
            foreach (var e in doc.Descendants())
                if ((string)e.Attribute("name") == name) { found = true; break; }
            Assert.IsTrue(found, $"{name} missing from GameHUD.uxml");
        }
    }
}
