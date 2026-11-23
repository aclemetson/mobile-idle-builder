using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// PvE phase 1: public player names. Shape rules, the leetspeak-aware word filter (against the real
    /// game_config list), the UGS-backed service (fake backend), the auto-prompt rule, the Settings row
    /// text, the HUD card markup and the save fields.
    /// </summary>
    public class PlayerNameTests
    {
        const string ConfigPath = "Assets/Data/settings/game_config.asset";

        static IReadOnlyList<string> RealPatterns()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<GameConfigSO>(ConfigPath);
            Assert.IsNotNull(cfg, ConfigPath);
            Assert.IsNotNull(cfg.nameFilterPatterns);
            Assert.Greater(cfg.nameFilterPatterns.Length, 20, "game_config.name_filter_patterns not imported");
            return cfg.nameFilterPatterns;
        }

        // ── Shape rules ───────────────────────────────────────────────────────

        [TestCase("Bob", NameCheck.Ok)]
        [TestCase("Quark_Queen-42", NameCheck.Ok)]
        [TestCase("", NameCheck.Empty)]
        [TestCase("Al", NameCheck.TooShort)]
        [TestCase("ABCDEFGHIJKLMNOPQ", NameCheck.TooLong)]
        [TestCase("Bob!", NameCheck.BadCharacters)]
        [TestCase("Bøb", NameCheck.BadCharacters)]
        [TestCase("12345", NameCheck.NoLetter)]
        [TestCase("___", NameCheck.NoLetter)]
        public void Validate_ShapeRules(string name, NameCheck expected) =>
            Assert.AreEqual(expected, PlayerNameRules.Validate(name, Array.Empty<string>()));

        [Test]
        public void Clean_TrimsAndTurnsSpacesIntoUnderscores()
        {
            Assert.AreEqual("Bob_Smith", PlayerNameRules.Clean("  Bob   Smith "));
            Assert.AreEqual("", PlayerNameRules.Clean(null));
        }

        [Test]
        public void Normalize_MapsLeetspeak_DropsSeparators_CollapsesRuns()
        {
            Assert.AreEqual("badword", PlayerNameRules.Normalize("B4d_W0rd"));
            Assert.AreEqual("badword", PlayerNameRules.Normalize("baaaddd-wooord"));
            Assert.AreEqual("sate", PlayerNameRules.Normalize("$@7E"));
        }

        [Test]
        public void Tag_SplitsTheUgsSuffix()
        {
            Assert.AreEqual("Bob", PlayerNameRules.WithoutTag("Bob#1234"));
            Assert.AreEqual("#1234", PlayerNameRules.Tag("Bob#1234"));
            Assert.AreEqual("Bob", PlayerNameRules.WithoutTag("Bob"));
            Assert.AreEqual("", PlayerNameRules.Tag("Bob"));
        }

        [Test]
        public void Filter_MatchesRawAndNormalisedForms()
        {
            var patterns = new[] { "b+a+d+w+o+r+d" };
            Assert.AreEqual(NameCheck.Blocked, PlayerNameRules.Validate("BadWord", patterns));
            Assert.AreEqual(NameCheck.Blocked, PlayerNameRules.Validate("B4d_W0rd", patterns));
            Assert.AreEqual(NameCheck.Blocked, PlayerNameRules.Validate("xxBAAADWORDxx", patterns));
            Assert.AreEqual(NameCheck.Ok, PlayerNameRules.Validate("GoodWord", patterns));
        }

        [Test]
        public void Filter_SkipsAMalformedPatternInsteadOfThrowing()
        {
            var patterns = new[] { "(unclosed", "b+a+d" };
            Assert.IsNull(PlayerNameRules.Compile("(unclosed"));
            Assert.AreEqual(NameCheck.Blocked, PlayerNameRules.Validate("bad_guy", patterns));
            Assert.AreEqual(NameCheck.Ok, PlayerNameRules.Validate("goodguy", patterns));
        }

        // ── The real list (game_data.json game_config.name_filter_patterns) ────

        [Test]
        public void RealList_EveryPatternCompiles()
        {
            foreach (var p in RealPatterns())
                Assert.IsNotNull(PlayerNameRules.Compile(p), $"pattern does not compile: {p}");
        }

        [TestCase("fuck")] [TestCase("F_U_C_K")] [TestCase("Fuuuck")] [TestCase("5h1t")] [TestCase("B1tchhh")]
        [TestCase("N1gga")] [TestCase("nigger")] [TestCase("Admin")] [TestCase("Moderator")] [TestCase("Hitler_88")]
        [TestCase("asshole")] [TestCase("A55hole")] [TestCase("Rapist")] [TestCase("QuantumFoundry")] [TestCase("Support")]
        public void RealList_BlocksOffensiveAndImpersonatingNames(string name) =>
            Assert.AreEqual(NameCheck.Blocked, PlayerNameRules.Validate(name, RealPatterns()), name);

        // Ordinary words that contain a blocked root: the anchors must let them through.
        [TestCase("Cassie")] [TestCase("Bassist")] [TestCase("Classic")] [TestCase("Swanky")] [TestCase("Thorny")]
        [TestCase("Therapist")] [TestCase("Grape")] [TestCase("Scraper")] [TestCase("Peacock")] [TestCase("Hancock")]
        [TestCase("Cocktail")] [TestCase("Night_Owl")] [TestCase("Essex")] [TestCase("Nazia")] [TestCase("Document")]
        [TestCase("Cucumber")] [TestCase("Title")] [TestCase("Pistol")] [TestCase("Dickens")] [TestCase("Spicey")]
        [TestCase("Assassin")] [TestCase("Mississippi")] [TestCase("Raccoon")] [TestCase("QuarkQueen")] [TestCase("Admiral")]
        public void RealList_AllowsOrdinaryNames(string name) =>
            Assert.AreEqual(NameCheck.Ok, PlayerNameRules.Validate(name, RealPatterns()), name);

        // ── Service (fake backend) ────────────────────────────────────────────

        sealed class FakeBackend : IPlayerNameBackend
        {
            public bool   Ready = true;
            public string Stored;
            public int    SetCalls;
            public Exception Throw;

            public bool IsReady => Ready;
            public Task<string> GetAsync() => Task.FromResult(Stored);
            public Task<string> SetAsync(string name)
            {
                SetCalls++;
                if (Throw != null) return Task.FromException<string>(Throw);
                Stored = name + "#1234";
                return Task.FromResult(Stored);
            }
        }

        IPlayerNameBackend _previousBackend;
        GameObject _saveManagerGO;
        string _savePath, _saveBackup;

        [SetUp]
        public void SetUp()
        {
            _previousBackend = PlayerNameService.Backend;
            _savePath   = Path.Combine(LocalSaveService.SaveDirectory, "save.json");
            _saveBackup = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
        }

        [TearDown]
        public void TearDown()
        {
            PlayerNameService.Backend = _previousBackend;
            if (_saveManagerGO != null)
            {
                UnityEngine.Object.DestroyImmediate(_saveManagerGO);
                _saveManagerGO = null;
                new LocalSaveService().Delete();
                if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);
            }
        }

        void SpawnSaveManager()
        {
            new LocalSaveService().Delete();
            _saveManagerGO = new GameObject("SaveManager");
            var sm = _saveManagerGO.AddComponent<SaveManager>();
            var t = sm.GetType();
            while (t != null && t != typeof(MonoBehaviour))
            {
                var m = t.GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                                             | BindingFlags.DeclaredOnly);
                if (m != null) { m.Invoke(sm, null); break; }
                t = t.BaseType;
            }
            Assert.IsNotNull(SaveManager.Instance?.Current);
        }

        static NameSetResult Set(string raw, params string[] patterns) =>
            PlayerNameService.TrySetAsync(raw, patterns).GetAwaiter().GetResult();

        [Test]
        public void TrySet_InvalidName_NeverReachesTheBackend()
        {
            var fake = new FakeBackend();
            PlayerNameService.Backend = fake;
            var r = Set("b4dw0rd", "b+a+d+w+o+r+d");
            Assert.AreEqual(NameSetStatus.Invalid, r.Status);
            Assert.AreEqual(NameCheck.Blocked, r.Check);
            Assert.AreEqual(0, fake.SetCalls);
        }

        [Test]
        public void TrySet_Offline_SaysSoAndStoresNothing()
        {
            var fake = new FakeBackend { Ready = false };
            PlayerNameService.Backend = fake;
            var r = Set("Bob");
            Assert.AreEqual(NameSetStatus.Offline, r.Status);
            Assert.AreEqual(0, fake.SetCalls);
        }

        [Test]
        public void TrySet_BackendFailure_ReportsFailed()
        {
            PlayerNameService.Backend = new FakeBackend { Throw = new InvalidOperationException("boom") };
            var r = Set("Bob");
            Assert.AreEqual(NameSetStatus.Failed, r.Status);
            Assert.IsFalse(string.IsNullOrEmpty(r.Message));
        }

        [Test]
        public void TrySet_Success_SendsTheCleanedName_AndCachesItInTheSave()
        {
            SpawnSaveManager();
            var fake = new FakeBackend();
            PlayerNameService.Backend = fake;
            string changed = null;
            void OnChanged(string n) => changed = n;
            PlayerNameService.OnNameChanged += OnChanged;
            try
            {
                var r = Set("  Quark Queen ");
                Assert.IsTrue(r.Ok, r.Message);
                Assert.AreEqual("Quark_Queen#1234", r.Name);
                Assert.AreEqual("Quark_Queen#1234", SaveManager.Instance.Current.playerName);
                Assert.AreEqual("Quark_Queen", PlayerNameService.DisplayName);
                Assert.AreEqual("Quark_Queen#1234", changed);
            }
            finally { PlayerNameService.OnNameChanged -= OnChanged; }
        }

        [Test]
        public void Refresh_UgsNameWins_OverTheCache()
        {
            SpawnSaveManager();
            SaveManager.Instance.Current.playerName = "";
            PlayerNameService.Backend = new FakeBackend { Stored = "FromOtherDevice#0007" };
            PlayerNameService.RefreshAsync().GetAwaiter().GetResult();
            Assert.AreEqual("FromOtherDevice#0007", SaveManager.Instance.Current.playerName);
        }

        [Test]
        public void Refresh_Offline_KeepsTheCache()
        {
            SpawnSaveManager();
            SaveManager.Instance.Current.playerName = "Bob#1234";
            PlayerNameService.Backend = new FakeBackend { Ready = false, Stored = "" };
            PlayerNameService.RefreshAsync().GetAwaiter().GetResult();
            Assert.AreEqual("Bob#1234", SaveManager.Instance.Current.playerName);
        }

        // ── Auto prompt rule + Settings row ───────────────────────────────────

        static bool Auto(bool loaded = true, bool tutorialActive = false, bool hasName = false, bool skipped = false,
                         bool online = true, bool otherModal = false, bool dialogue = false, bool reload = false) =>
            PlayerNamePromptController.ShouldAutoPrompt(loaded, tutorialActive, hasName, skipped, online, otherModal,
                                                        dialogue, reload);

        [Test]
        public void AutoPrompt_OnlyAfterTheTutorial_WhenNoNameAndNotSkipped()
        {
            Assert.IsTrue(Auto());
            Assert.IsFalse(Auto(loaded: false));
            Assert.IsFalse(Auto(tutorialActive: true));
            Assert.IsFalse(Auto(hasName: true));
            Assert.IsFalse(Auto(skipped: true));
            Assert.IsFalse(Auto(online: false));
            Assert.IsFalse(Auto(otherModal: true));
            Assert.IsFalse(Auto(dialogue: true));
            Assert.IsFalse(Auto(reload: true));
        }

        [Test]
        public void SettingsRow_ShowsTheNameWithItsTag_OrAnInvitation()
        {
            Assert.AreEqual("Choose a player name", HUDSettingsSubController.PlayerNameButtonText(""));
            Assert.AreEqual("Player name: Bob #1234", HUDSettingsSubController.PlayerNameButtonText("Bob#1234"));
            Assert.AreEqual("Player name: Bob", HUDSettingsSubController.PlayerNameButtonText("Bob"));
        }

        [Test]
        public void GameHud_HasTheNameCardAndSettingsRow()
        {
            var hud = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameHUD.uxml");
            Assert.IsNotNull(hud);
            var root = hud.Instantiate();
            Assert.IsNotNull(root.Q("name-prompt"));
            Assert.IsNotNull(root.Q<TextField>("name-prompt__input"));
            Assert.IsNotNull(root.Q<Label>("name-prompt__error"));
            Assert.IsNotNull(root.Q<Button>("name-prompt__save"));
            Assert.IsNotNull(root.Q<Button>("name-prompt__skip"));
            Assert.IsNotNull(root.Q<Button>("btn-player-name"));
        }

        // ── Save fields ───────────────────────────────────────────────────────

        [Test]
        public void Save_LegacyJsonWithoutNameFields_LoadsAsNeverChosen()
        {
            var save = JsonUtility.FromJson<SaveData>("{\"prestigeCount\":3}");
            Assert.AreEqual("", save.playerName);
            Assert.IsFalse(save.namePromptSkipped);
        }

        [Test]
        public void Save_NameFields_RoundTrip()
        {
            var save = new SaveData { playerName = "Bob#1234", namePromptSkipped = true };
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            Assert.AreEqual("Bob#1234", back.playerName);
            Assert.IsTrue(back.namePromptSkipped);
        }
    }
}
