using System;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// Tests for AuthSessionPolicy — the 30-day inactivity and 90-day full-auth rules
    /// that determine whether Google Sign-In may proceed silently or must show the account picker.
    ///
    /// Setup/teardown backs up and restores the PlayerPrefs keys so running tests never
    /// corrupts a developer's local auth state.
    /// </summary>
    public class AuthSessionPolicyTests
    {
        string _savedLastOpened;
        string _savedLastFullAuth;

        [SetUp]
        public void SetUp()
        {
            _savedLastOpened  = PlayerPrefs.GetString(AuthSessionPolicy.k_LastOpenedKey,  "");
            _savedLastFullAuth = PlayerPrefs.GetString(AuthSessionPolicy.k_LastFullAuthKey, "");
            PlayerPrefs.DeleteKey(AuthSessionPolicy.k_LastOpenedKey);
            PlayerPrefs.DeleteKey(AuthSessionPolicy.k_LastFullAuthKey);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(AuthSessionPolicy.k_LastOpenedKey);
            PlayerPrefs.DeleteKey(AuthSessionPolicy.k_LastFullAuthKey);

            if (!string.IsNullOrEmpty(_savedLastOpened))
                PlayerPrefs.SetString(AuthSessionPolicy.k_LastOpenedKey, _savedLastOpened);
            if (!string.IsNullOrEmpty(_savedLastFullAuth))
                PlayerPrefs.SetString(AuthSessionPolicy.k_LastFullAuthKey, _savedLastFullAuth);

            PlayerPrefs.Save();
        }

        // ── Policy: interactive required ─────────────────────────────────────

        [Test]
        public void RequiresInteractiveSignIn_TrueWhenNoRecordsExist()
        {
            // Brand-new install — both keys absent
            Assert.IsTrue(AuthSessionPolicy.RequiresInteractiveSignIn());
        }

        [Test]
        public void RequiresInteractiveSignIn_TrueWhenOnlyLastOpenedExists()
        {
            // User opened app recently but never completed a full auth recording
            SetLastOpened(DateTime.UtcNow.AddDays(-1));
            Assert.IsTrue(AuthSessionPolicy.RequiresInteractiveSignIn());
        }

        [Test]
        public void RequiresInteractiveSignIn_TrueWhenInactive31Days()
        {
            SetLastFullAuth(DateTime.UtcNow.AddDays(-10));
            SetLastOpened(DateTime.UtcNow.AddDays(-31));
            Assert.IsTrue(AuthSessionPolicy.RequiresInteractiveSignIn());
        }

        [Test]
        public void RequiresInteractiveSignIn_TrueWhenFullAuthExpiredAt91Days()
        {
            SetLastFullAuth(DateTime.UtcNow.AddDays(-91));
            SetLastOpened(DateTime.UtcNow.AddDays(-1)); // active user
            Assert.IsTrue(AuthSessionPolicy.RequiresInteractiveSignIn());
        }

        // ── Policy: silent sign-in allowed ───────────────────────────────────

        [Test]
        public void RequiresInteractiveSignIn_FalseWhenRecentActivityAndRecentFullAuth()
        {
            SetLastFullAuth(DateTime.UtcNow.AddDays(-10));
            SetLastOpened(DateTime.UtcNow.AddDays(-1));
            Assert.IsFalse(AuthSessionPolicy.RequiresInteractiveSignIn());
        }

        [Test]
        public void RequiresInteractiveSignIn_FalseAtExactly29DaysInactivity()
        {
            // 29 days < 30-day threshold → silent still allowed
            SetLastFullAuth(DateTime.UtcNow.AddDays(-5));
            SetLastOpened(DateTime.UtcNow.AddDays(-29));
            Assert.IsFalse(AuthSessionPolicy.RequiresInteractiveSignIn());
        }

        [Test]
        public void RequiresInteractiveSignIn_FalseAtExactly89DaysSinceFullAuth()
        {
            // 89 days < 90-day threshold → silent still allowed
            SetLastFullAuth(DateTime.UtcNow.AddDays(-89));
            SetLastOpened(DateTime.UtcNow.AddDays(-1));
            Assert.IsFalse(AuthSessionPolicy.RequiresInteractiveSignIn());
        }

        // ── Record helpers ───────────────────────────────────────────────────

        [Test]
        public void RecordAppOpen_SetsLastOpenedKey()
        {
            AuthSessionPolicy.RecordAppOpen();
            string raw = PlayerPrefs.GetString(AuthSessionPolicy.k_LastOpenedKey, "");
            Assert.IsNotEmpty(raw);
            var parsed = DateTime.Parse(raw, null, DateTimeStyles.RoundtripKind);
            Assert.That((DateTime.UtcNow - parsed).TotalSeconds, Is.LessThan(5));
        }

        [Test]
        public void RecordFullAuth_SetsLastFullAuthKey()
        {
            AuthSessionPolicy.RecordFullAuth();
            string raw = PlayerPrefs.GetString(AuthSessionPolicy.k_LastFullAuthKey, "");
            Assert.IsNotEmpty(raw);
            var parsed = DateTime.Parse(raw, null, DateTimeStyles.RoundtripKind);
            Assert.That((DateTime.UtcNow - parsed).TotalSeconds, Is.LessThan(5));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static void SetLastOpened(DateTime utc) =>
            PlayerPrefs.SetString(AuthSessionPolicy.k_LastOpenedKey, utc.ToString("O"));

        static void SetLastFullAuth(DateTime utc) =>
            PlayerPrefs.SetString(AuthSessionPolicy.k_LastFullAuthKey, utc.ToString("O"));
    }
}
