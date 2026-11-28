using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Settings "Delete account" (docs/agents/tasks/feature-account-deletion.md): step order, stopping at the
    /// first failure with cloud saving resumed, the server-side bracket rule, and the PvE client call.
    /// </summary>
    public class AccountDeletionTests
    {
        sealed class FakeBackend : IAccountDeletionBackend
        {
            public bool Online = true;
            public string ServerError;
            public Exception ServerThrows;
            public Exception AccountThrows;
            public readonly List<string> Calls = new();

            public bool IsOnline => Online;
            public Task SuspendCloudSaveAsync() { Calls.Add("suspend"); return Task.CompletedTask; }
            public void ResumeCloudSave() => Calls.Add("resume");
            public Task<string> DeleteServerDataAsync()
            {
                Calls.Add("server");
                return ServerThrows != null ? Task.FromException<string>(ServerThrows) : Task.FromResult(ServerError);
            }
            public Task DeleteAccountAsync()
            {
                Calls.Add("account");
                return AccountThrows != null ? Task.FromException(AccountThrows) : Task.CompletedTask;
            }
        }

        static (AccountDeletionFailure failure, string error) Run(FakeBackend b) =>
            AccountDeletion.RunAsync(b).GetAwaiter().GetResult();

        [Test]
        public void Success_SuspendsSaving_DeletesServerDataBeforeTheAccount_AndDoesNotResume()
        {
            var b = new FakeBackend();
            var (failure, _) = Run(b);
            Assert.AreEqual(AccountDeletionFailure.None, failure);
            CollectionAssert.AreEqual(new[] { "suspend", "server", "account" }, b.Calls,
                "saving stays suspended until the local reset; the account goes last");
            Assert.IsFalse(AccountDeletion.InProgress);
        }

        [Test]
        public void Offline_DeletesNothing()
        {
            var b = new FakeBackend { Online = false };
            Assert.AreEqual(AccountDeletionFailure.Offline, Run(b).failure);
            Assert.IsEmpty(b.Calls);
            Assert.AreEqual(AccountDeletionFailure.Offline, AccountDeletion.RunAsync(null).GetAwaiter().GetResult().failure);
        }

        [Test]
        public void ServerDataFailure_KeepsTheAccount_AndResumesSaving()
        {
            var b = new FakeBackend { ServerError = "network:offline" };
            var (failure, error) = Run(b);
            Assert.AreEqual(AccountDeletionFailure.ServerData, failure);
            Assert.AreEqual("network:offline", error);
            CollectionAssert.AreEqual(new[] { "suspend", "server", "resume" }, b.Calls);

            var thrower = new FakeBackend { ServerThrows = new InvalidOperationException("boom") };
            Assert.AreEqual(AccountDeletionFailure.ServerData, Run(thrower).failure);
            CollectionAssert.DoesNotContain(thrower.Calls, "account");
        }

        [Test]
        public void AccountFailure_ReportsIt_AndResumesSaving()
        {
            var b = new FakeBackend { AccountThrows = new InvalidOperationException("auth down") };
            var (failure, error) = Run(b);
            Assert.AreEqual(AccountDeletionFailure.Account, failure);
            Assert.AreEqual("auth down", error);
            CollectionAssert.AreEqual(new[] { "suspend", "server", "account", "resume" }, b.Calls);
            StringAssert.Contains("again", AccountDeletion.FailureMessage(failure, error));
        }

        [Test]
        public void FailureMessages_ExistForEveryFailure_AndNoneForSuccess()
        {
            Assert.IsNull(AccountDeletion.FailureMessage(AccountDeletionFailure.None, null));
            foreach (AccountDeletionFailure f in Enum.GetValues(typeof(AccountDeletionFailure)))
                if (f != AccountDeletionFailure.None)
                    Assert.IsNotEmpty(AccountDeletion.FailureMessage(f, new string('x', 300)), f.ToString());
        }

        [Test]
        public void WithoutPlayer_RemovesOnlyThatPlayer_AndReportsNothingToWriteWhenAbsent()
        {
            var meta = new PveBracketMeta { playerIds = new List<string> { "a", "me", "b" } };
            var written = PveRules.WithoutPlayer(meta, "me");
            CollectionAssert.AreEqual(new[] { "a", "b" }, written.playerIds);
            Assert.IsNull(PveRules.WithoutPlayer(written, "me"));
            Assert.IsNull(PveRules.WithoutPlayer(null, "me"));
            Assert.IsNull(PveRules.WithoutPlayer(meta, ""));
        }

        sealed class FakeTransport : IPveTransport
        {
            public string LastFunction;
            public string Reply;
            public Task<string> Call(string function, Dictionary<string, object> args)
            {
                LastFunction = function;
                return Task.FromResult(Reply);
            }
        }

        [Test]
        public void Backend_ServerDataDelete_CallsDeleteMyData_AndNeedsTheServersConfirmation()
        {
            var prev = PveApi.Transport;
            try
            {
                var fake = new FakeTransport { Reply = "{\"deleted\":true}" };
                PveApi.Transport = fake;
                var backend = new UgsAccountDeletionBackend();
                Assert.IsNull(backend.DeleteServerDataAsync().GetAwaiter().GetResult());
                Assert.AreEqual("DeleteMyData", fake.LastFunction);

                fake.Reply = "{}";
                Assert.IsNotEmpty(backend.DeleteServerDataAsync().GetAwaiter().GetResult(), "no confirmation = failure");
            }
            finally
            {
                PveApi.Transport = prev;
            }
        }
    }
}
