using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Services.Core;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// A ban in Unity Cloud refuses every call with 403 / error 57 while the player stays signed in (seen on device
    /// 2026-11-28). AccountSuspension recognises it however the SDK wraps it, and the tournament client stops calling.
    /// </summary>
    public class AccountSuspensionTests
    {
        const string DeniedBody =
            "{\"code\":57,\"detail\":\"Principal is not authorized to access resource\",\"status\":403,\"title\":\"Forbidden\"}";

        [SetUp] public void SetUp() { AccountSuspension.ResetForTests(); _prevTransport = PveApi.Transport; }
        [TearDown] public void TearDown() { AccountSuspension.ResetForTests(); PveApi.Transport = _prevTransport; }
        IPveTransport _prevTransport;

        [Test]
        public void AccessDenied_IsRecognised_ByCode_Message_OrWrapped()
        {
            Assert.IsTrue(AccountSuspension.IsAccessDenied(new RequestFailedException(57, "Forbidden")));
            Assert.IsTrue(AccountSuspension.IsAccessDenied(new Exception("Principal is not authorized to access resource")));
            Assert.IsTrue(AccountSuspension.IsAccessDenied(new Exception("HTTP/1.1 403 Forbidden\n" + DeniedBody)));
            Assert.IsTrue(AccountSuspension.IsAccessDenied(new AggregateException(new RequestFailedException(57, "x"))));
            Assert.IsTrue(AccountSuspension.IsAccessDenied(new InvalidOperationException("outer", new RequestFailedException(57, "x"))));
        }

        [Test]
        public void OtherFailures_AreNotASuspension()
        {
            Assert.IsFalse(AccountSuspension.IsAccessDenied(null));
            Assert.IsFalse(AccountSuspension.IsAccessDenied(new RequestFailedException(10007, "Key-value pair limit")));
            Assert.IsFalse(AccountSuspension.IsAccessDenied(new Exception("Cannot resolve destination host")));
            Assert.IsFalse(AccountSuspension.IsAccessDenied(new Exception("{\"code\":570,\"detail\":\"other\"}")));
            Assert.IsFalse(AccountSuspension.Report(new Exception("offline")));
            Assert.IsFalse(AccountSuspension.IsSuspended);
        }

        [Test]
        public void Report_MarksTheSessionOnce_AndRaisesSuspendedOnce()
        {
            int raised = 0;
            void OnSuspended() => raised++;
            AccountSuspension.Suspended += OnSuspended;
            try
            {
                Assert.IsTrue(AccountSuspension.Report(new RequestFailedException(57, "Forbidden")));
                Assert.IsTrue(AccountSuspension.Report(new RequestFailedException(57, "Forbidden")));
                Assert.IsTrue(AccountSuspension.IsSuspended);
                Assert.AreEqual(1, raised);
            }
            finally
            {
                AccountSuspension.Suspended -= OnSuspended;
            }
        }

        [Test]
        public void Message_NamesTheSupportEmail_WhenThereIsOne()
        {
            StringAssert.Contains("help@example.com", AccountSuspension.Message("help@example.com"));
            StringAssert.DoesNotContain("email", AccountSuspension.Message(""));
            StringAssert.StartsWith("mailto:help@example.com?subject=Account%20suspended",
                                    AccountSuspensionNotice.SupportMailto("help@example.com"));
        }

        sealed class CountingTransport : IPveTransport
        {
            public int Calls;
            public Exception Throw;
            public Task<string> Call(string function, Dictionary<string, object> args)
            {
                Calls++;
                return Throw != null ? Task.FromException<string>(Throw) : Task.FromResult("{}");
            }
        }

        [Test]
        public void Tournament_DeniedCall_ReturnsSuspended_AndLaterCallsAreNotSent()
        {
            var t = new CountingTransport { Throw = new RequestFailedException(57, "Principal is not authorized to access resource") };
            PveApi.Transport = t;

            Assert.AreEqual(PveErrors.Suspended, PveApi.Status().GetAwaiter().GetResult().error);
            Assert.IsTrue(AccountSuspension.IsSuspended);
            Assert.AreEqual(PveErrors.Suspended, PveApi.Board().GetAwaiter().GetResult().error);
            Assert.AreEqual(1, t.Calls, "no more calls once the account is known to be suspended");
            StringAssert.Contains("suspended", TournamentRules.ErrorText(PveErrors.Suspended));
        }

        [Test]
        public void Tournament_OrdinaryNetworkFailure_StaysANetworkError()
        {
            PveApi.Transport = new CountingTransport { Throw = new Exception("offline") };
            StringAssert.StartsWith("network:", PveApi.Status().GetAwaiter().GetResult().error);
            Assert.IsFalse(AccountSuspension.IsSuspended);
        }
    }
}
