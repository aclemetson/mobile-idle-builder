using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>The boot-time update prompt's decision and link rules (AppUpdateCheck).</summary>
    [TestFixture]
    public class AppUpdateCheckTests
    {
        [Test]
        public void Disabled_NeverPrompts()
        {
            Assert.AreEqual(AppUpdateKind.None, AppUpdateCheck.Decide(false, "0.5.1", "0.6.0", "0.6.0"));
        }

        [Test]
        public void UpToDateOrNewer_DoesNotPrompt()
        {
            Assert.AreEqual(AppUpdateKind.None, AppUpdateCheck.Decide(true, "0.5.52", "0.5.52", ""));
            Assert.AreEqual(AppUpdateKind.None, AppUpdateCheck.Decide(true, "0.5.53", "0.5.52", ""));
        }

        [Test]
        public void OlderThanLatest_IsOptional()
        {
            Assert.AreEqual(AppUpdateKind.Optional, AppUpdateCheck.Decide(true, "0.5.51", "0.5.52", ""));
        }

        [Test]
        public void BelowMinimum_IsRequired_EvenWhenLatestIsBlank()
        {
            Assert.AreEqual(AppUpdateKind.Required, AppUpdateCheck.Decide(true, "0.5.40", "0.5.52", "0.5.45"));
            Assert.AreEqual(AppUpdateKind.Required, AppUpdateCheck.Decide(true, "0.5.40", "", "0.5.45"));
        }

        [Test]
        public void AtMinimum_ButBehindLatest_IsOptional()
        {
            Assert.AreEqual(AppUpdateKind.Optional, AppUpdateCheck.Decide(true, "0.5.45", "0.5.52", "0.5.45"));
        }

        [Test]
        public void BlankOrGarbage_NeverPrompts()
        {
            Assert.AreEqual(AppUpdateKind.None, AppUpdateCheck.Decide(true, "0.5.1", "", ""));
            Assert.AreEqual(AppUpdateKind.None, AppUpdateCheck.Decide(true, "0.5.1", "latest", "soon"));
            Assert.AreEqual(AppUpdateKind.None, AppUpdateCheck.Decide(true, "", "0.6.0", "0.6.0"));
            Assert.AreEqual(AppUpdateKind.None, AppUpdateCheck.Decide(true, null, null, null));
        }

        [Test]
        public void ComparesNumerically_NotAsText()
        {
            Assert.AreEqual(AppUpdateKind.Optional, AppUpdateCheck.Decide(true, "0.5.9", "0.5.52", ""));
            Assert.AreEqual(AppUpdateKind.None,     AppUpdateCheck.Decide(true, "0.10.0", "0.9.99", ""));
        }

        [Test]
        public void MissingPartsCountAsZero_AndSuffixesAreIgnored()
        {
            Assert.AreEqual(AppUpdateKind.None,     AppUpdateCheck.Decide(true, "0.6.0", "0.6", ""));
            Assert.AreEqual(AppUpdateKind.Optional, AppUpdateCheck.Decide(true, "0.5.52-dev", "0.5.53", ""));
            Assert.AreEqual(AppUpdateKind.None,     AppUpdateCheck.Decide(true, " 0.5.53 ", "0.5.53", ""));
        }

        [Test]
        public void Url_RemoteValueWins()
        {
            Assert.AreEqual("https://example.com/x", AppUpdateCheck.ResolveUrl(true, false, " https://example.com/x "));
        }

        [Test]
        public void Url_Defaults_ByPlatformAndEnvironment()
        {
            Assert.AreEqual(AppUpdateCheck.AndroidStoreUrl, AppUpdateCheck.ResolveUrl(false, true,  ""));
            Assert.AreEqual(AppUpdateCheck.AndroidStoreUrl, AppUpdateCheck.ResolveUrl(false, false, null));
            Assert.AreEqual(AppUpdateCheck.TestFlightUrl,   AppUpdateCheck.ResolveUrl(true,  true,  ""));
            Assert.AreEqual("", AppUpdateCheck.ResolveUrl(true, false, ""), "iOS prod has no default: set the App Store link remotely");
        }
    }
}
