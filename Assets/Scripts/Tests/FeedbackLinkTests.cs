using System;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Settings > Support: the Send Feedback mailto link and the link flag defaults.
    /// </summary>
    [TestFixture]
    public class FeedbackLinkTests
    {
        [Test]
        public void BuildFeedbackMailto_AddressesEmail_AndEscapesSubjectAndBody()
        {
            string url = HUDSettingsSubController.BuildFeedbackMailto(
                "dev@example.com", "0.4.16", "pid-42", "Pixel 8 / Android 15");

            StringAssert.StartsWith("mailto:dev@example.com?subject=Feedback%200.4.16&body=", url);
            StringAssert.DoesNotContain(" ", url, "spaces must be percent-encoded for mail clients");
            StringAssert.DoesNotContain("\n", url, "newlines must be percent-encoded");

            string body = Uri.UnescapeDataString(url.Substring(url.IndexOf("&body=", StringComparison.Ordinal) + 6));
            StringAssert.Contains("Version: 0.4.16", body);
            StringAssert.Contains("Player ID: pid-42", body);
            StringAssert.Contains("Device: Pixel 8 / Android 15", body);
        }

        [Test]
        public void BuildFeedbackMailto_MissingPlayerId_ReportsUnknown()
        {
            string url  = HUDSettingsSubController.BuildFeedbackMailto("dev@example.com", "1.0", null, "d");
            string body = Uri.UnescapeDataString(url.Substring(url.IndexOf("&body=", StringComparison.Ordinal) + 6));
            StringAssert.Contains("Player ID: unknown", body);
        }

        [Test]
        public void LinkFlags_FallBackToShippedDefaults_WithoutRemoteConfig()
        {
            Assert.AreEqual(FeatureFlags.FeedbackEmailDefault,    FeatureFlags.FeedbackEmail);
            Assert.AreEqual(FeatureFlags.PrivacyPolicyUrlDefault, FeatureFlags.PrivacyPolicyUrl);
            StringAssert.StartsWith("https://", FeatureFlags.PrivacyPolicyUrl);
            Assert.AreEqual(FeatureFlags.DiscordUrlDefault, FeatureFlags.DiscordUrl);
            Assert.IsTrue(HUDSettingsSubController.IsDiscordInvite(FeatureFlags.DiscordUrlDefault),
                "the shipped invite must pass the check, or the Settings button stays hidden");
        }

        [TestCase("https://discord.gg/AbCd123")]
        [TestCase("https://discord.com/invite/AbCd123")]
        [TestCase("  https://discord.gg/AbCd123  ")]
        public void IsDiscordInvite_AcceptsHttpsInvites(string url)
        {
            Assert.IsTrue(HUDSettingsSubController.IsDiscordInvite(url));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("discord.gg/AbCd123")]                 // no scheme
        [TestCase("http://discord.gg/AbCd123")]          // not https
        [TestCase("https://discord.gg/")]                // no code
        [TestCase("https://discord.com/channels/1/2")]   // not an invite
        [TestCase("https://discord.com/invite/")]
        [TestCase("https://evil.example/discord.gg/x")]
        [TestCase("https://discord.gg.evil.example/x")]
        public void IsDiscordInvite_RejectsEverythingElse(string url)
        {
            Assert.IsFalse(HUDSettingsSubController.IsDiscordInvite(url));
        }
    }
}
