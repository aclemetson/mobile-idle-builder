using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>Settings used to show only "Exception of type 'Google.GoogleSignIn+SignInException' was thrown."</summary>
    [TestFixture]
    public class GoogleSignInErrorsTests
    {
        [Test]
        public void Android_UsesPlayServicesCodes()
        {
            StringAssert.StartsWith("DEVELOPER_ERROR (10): ", GoogleSignInErrors.Describe(10, "10", android: true));
            StringAssert.Contains("SHA-1", GoogleSignInErrors.Describe(10, "10", android: true));
            StringAssert.StartsWith("SIGN_IN_CANCELLED (12501)", GoogleSignInErrors.Describe(12501, "12501", android: true));
            // The enum would call 7 InternalError; on Android it is a network error.
            StringAssert.StartsWith("NETWORK_ERROR (7)", GoogleSignInErrors.Describe(7, "InternalError", android: true));
        }

        [Test]
        public void Ios_UsesThePluginEnum()
        {
            StringAssert.StartsWith("DeveloperError (6): ", GoogleSignInErrors.Describe(6, "DeveloperError", android: false));
            StringAssert.StartsWith("Canceled (2)", GoogleSignInErrors.Describe(2, "Canceled", android: false));
        }

        [Test]
        public void UnknownCode_StillShowsTheNumber()
        {
            Assert.AreEqual("Status (999)", GoogleSignInErrors.Describe(999, "999", android: true));
            Assert.AreEqual("Status (999)", GoogleSignInErrors.Describe(999, null, android: false));
        }
    }
}
