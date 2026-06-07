using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Drives the full-screen achievements overlay on the main menu.
    /// Works without AchievementService (which lives only in the game scene) by
    /// reading directly from SaveManager.Current and an injected AchievementDatabase.
    ///
    /// On Start(), runs period resets and evaluates the Login trigger so the daily
    /// login achievement fires at the main menu rather than waiting for the game scene.
    ///
    /// Wire-up: Attach as a sibling MonoBehaviour to MainMenuController on the same
    /// GameObject. Assign the AchievementDatabase serialized field in the Inspector.
    /// </summary>
    [RequireComponent(typeof(MainMenuController))]
    public class MainMenuAchievementsController : MonoBehaviour
    {
        [SerializeField] AchievementDatabase database;
        [SerializeField] float notificationDuration = 3f;

        private VisualElement _overlay;
        private Button        _btnClose;
        private Button        _tabDaily, _tabWeekly, _tabMonthly, _tabProgression;
        private Label         _resetLabel;
        private Label         _crystalBalance;
        private ScrollView    _scroll;
        private Button        _btnClaimAll;

        private VisualElement _notificationBanner;
        private Label         _notificationIcon;
        private Label         _notificationMessage;
        private Coroutine     _hideNotificationCoroutine;

        private AchievementCategory _activeCategory = AchievementCategory.Daily;

        /// <summary>
        /// Called by MainMenuController after it has resolved the UIDocument root,
        /// guaranteeing UIDocument is ready before we query elements.
        /// </summary>
        public void Initialize(VisualElement root)
        {
            Cleanup();
            QueryElements(root);
            BindButtons();
            // Ensure the overlay is hidden until explicitly opened.
            HUDController.SetElementVisible(_overlay, false);
        }

        public void Cleanup()
        {
            if (_btnClose       != null) { _btnClose.clicked       -= Close;   }
            if (_tabDaily       != null) { _tabDaily.clicked       -= OnTabDaily; }
            if (_tabWeekly      != null) { _tabWeekly.clicked      -= OnTabWeekly; }
            if (_tabMonthly     != null) { _tabMonthly.clicked     -= OnTabMonthly; }
            if (_tabProgression != null) { _tabProgression.clicked -= OnTabProgression; }
            if (_btnClaimAll    != null) { _btnClaimAll.clicked    -= ClaimAll; }
        }

        private void OnDisable() => Cleanup();

        // Called after OnEnable (which wires up UI), so notification banner is ready.
        private void Start() => EvaluateSessionLogin();

        // ── Session-login achievement evaluation ──────────────────────────────

        /// <summary>
        /// Runs period resets then evaluates the Login trigger against save data directly.
        /// This fires daily_login (and any other Login-trigger achievements) at the main
        /// menu so the player sees the toast without having to enter the game first.
        /// AchievementService.Start() skips redundant re-fires because the id is already
        /// in _completed when the game scene loads.
        /// </summary>
        private void EvaluateSessionLogin()
        {
            if (database == null) return;
            var save = SaveManager.Instance?.Current;
            if (save == null || !save.tutorial.hasCompletedFirstRun) return;

            bool dirty = CheckAndApplyPeriodResets(save);

            foreach (var a in database.All)
            {
                if (a == null || a.triggerType != AchievementTrigger.Login) continue;
                if (save.achievements.Contains(a.id)) continue;

                var entry = save.achievementProgress?.Find(e => e.id == a.id);
                int updated = (entry?.count ?? 0) + 1;

                if (entry != null)
                    entry.count = updated;
                else
                    save.achievementProgress.Add(new AchievementProgressEntry { id = a.id, count = updated });

                dirty = true;

                if (updated < a.triggerQuantity) continue;

                save.achievements.Add(a.id);
                save.achievementProgress.RemoveAll(e => e.id == a.id);
                if (!save.unclaimedAchievements.Contains(a.id))
                    save.unclaimedAchievements.Add(a.id);

                ShowNotification("🏆", $"Achievement unlocked: {a.displayName}");
            }

            if (dirty)
                SaveManager.Instance.SaveLocal();
        }

        private bool CheckAndApplyPeriodResets(SaveData save)
        {
            var  now   = DateTime.UtcNow;
            bool dirty = false;

            if (now >= ParseOrEpoch(save.dailyResetUtc))
            {
                ResetCategoryInSave(save, AchievementCategory.Daily);
                save.dailyResetUtc = now.Date.AddDays(1).ToString("o");
                dirty = true;
            }

            if (now >= ParseOrEpoch(save.weeklyResetUtc))
            {
                ResetCategoryInSave(save, AchievementCategory.Weekly);
                int daysUntilMonday = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
                if (daysUntilMonday == 0) daysUntilMonday = 7;
                save.weeklyResetUtc = now.Date.AddDays(daysUntilMonday).ToString("o");
                dirty = true;
            }

            if (now >= ParseOrEpoch(save.monthlyResetUtc))
            {
                ResetCategoryInSave(save, AchievementCategory.Monthly);
                save.monthlyResetUtc = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddMonths(1).ToString("o");
                dirty = true;
            }

            return dirty;
        }

        private void ResetCategoryInSave(SaveData save, AchievementCategory category)
        {
            foreach (var a in database.All)
            {
                if (a == null || a.category != category) continue;
                save.achievements.Remove(a.id);
                save.unclaimedAchievements.Remove(a.id);
                save.achievementProgress.RemoveAll(e => e.id == a.id);
            }
        }

        static DateTime ParseOrEpoch(string iso) =>
            DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                ? dt : DateTime.MinValue;

        private void QueryElements(VisualElement root)
        {
            _overlay         = root.Q("achievements-overlay");
            _btnClose        = root.Q<Button>("btn-achievements-close");
            _tabDaily        = root.Q<Button>("tab-daily");
            _tabWeekly       = root.Q<Button>("tab-weekly");
            _tabMonthly      = root.Q<Button>("tab-monthly");
            _tabProgression  = root.Q<Button>("tab-progression");
            _resetLabel      = root.Q<Label>("achievements-reset-label");
            _crystalBalance  = root.Q<Label>("achievements-crystal-balance");
            _scroll          = root.Q<ScrollView>("achievements-scroll");
            _btnClaimAll     = root.Q<Button>("btn-claim-all");

            _notificationBanner  = root.Q("notification-banner");
            _notificationIcon    = root.Q<Label>("notification-banner__icon");
            _notificationMessage = root.Q<Label>("notification-banner__message");
        }

        private void BindButtons()
        {
            if (_btnClose       != null) _btnClose.clicked       += Close;
            if (_tabDaily       != null) _tabDaily.clicked       += OnTabDaily;
            if (_tabWeekly      != null) _tabWeekly.clicked      += OnTabWeekly;
            if (_tabMonthly     != null) _tabMonthly.clicked     += OnTabMonthly;
            if (_tabProgression != null) _tabProgression.clicked += OnTabProgression;
            if (_btnClaimAll    != null) _btnClaimAll.clicked    += ClaimAll;
        }

        private void OnTabDaily()       => SwitchTab(AchievementCategory.Daily);
        private void OnTabWeekly()      => SwitchTab(AchievementCategory.Weekly);
        private void OnTabMonthly()     => SwitchTab(AchievementCategory.Monthly);
        private void OnTabProgression() => SwitchTab(AchievementCategory.Progression);

        // ── Open / Close ──────────────────────────────────────────────────────

        public void Open()
        {
            if (_overlay == null) return;
            HUDController.SetElementVisible(_overlay, true);
            _activeCategory = AchievementCategory.Daily;
            RefreshAll();
        }

        private void Close()
        {
            if (_overlay == null) return;
            HUDController.SetElementVisible(_overlay, false);
        }

        // ── Tab switching ─────────────────────────────────────────────────────

        private void SwitchTab(AchievementCategory category)
        {
            _activeCategory = category;
            RefreshAll();
        }

        private void RefreshAll()
        {
            UpdateTabHighlights();
            UpdateCrystalBalance();
            UpdateResetLabel();
            BuildList();
            UpdateClaimAllButton();
        }

        private void UpdateTabHighlights()
        {
            SetTabActive(_tabDaily,       _activeCategory == AchievementCategory.Daily);
            SetTabActive(_tabWeekly,      _activeCategory == AchievementCategory.Weekly);
            SetTabActive(_tabMonthly,     _activeCategory == AchievementCategory.Monthly);
            SetTabActive(_tabProgression, _activeCategory == AchievementCategory.Progression);
        }

        private static void SetTabActive(Button tab, bool active)
        {
            if (tab == null) return;
            if (active) tab.AddToClassList("achievements-tab--active");
            else        tab.RemoveFromClassList("achievements-tab--active");
        }

        private void UpdateCrystalBalance()
        {
            if (_crystalBalance == null) return;
            long crystals = SaveManager.Instance?.Current?.paidCurrency ?? 0L;
            _crystalBalance.text = $"◆ {crystals:N0}";
        }

        private void UpdateResetLabel()
        {
            if (_resetLabel == null) return;
            if (_activeCategory == AchievementCategory.Progression)
            {
                _resetLabel.text = "";
                return;
            }
            _resetLabel.text = GetResetCountdown(_activeCategory);
        }

        private void BuildList()
        {
            if (_scroll == null || database == null) return;
            _scroll.Clear();

            var save = SaveManager.Instance?.Current;
            var all  = database.All;
            if (all == null) return;

            foreach (var a in all)
            {
                if (a == null || a.category != _activeCategory) continue;

                bool isDone      = save != null && save.achievements.Contains(a.id);
                bool isClaimable = save != null && save.unclaimedAchievements.Contains(a.id);
                bool isHidden    = a.isHidden && !isDone;

                var card = new VisualElement();
                card.AddToClassList("ach-card");
                if (isDone && !isClaimable)      card.AddToClassList("ach-card--completed");
                else if (isClaimable)            card.AddToClassList("ach-card--claimable");

                // Row: check + body + claim button
                var row = new VisualElement();
                row.AddToClassList("ach-card-row");

                var check = new Label(isDone ? "✓" : "○");
                check.AddToClassList("ach-card-check");
                if (isDone) check.AddToClassList("ach-card-check--done");

                var body = new VisualElement();
                body.AddToClassList("ach-card-body");

                var nameLabel = new Label(isHidden ? "???" : a.displayName);
                nameLabel.AddToClassList("ach-card-name");

                var descLabel = new Label(isHidden ? "Complete a hidden objective to reveal." : a.description);
                descLabel.AddToClassList("ach-card-desc");

                body.Add(nameLabel);
                body.Add(descLabel);

                // Reward hint
                if (!isHidden && (a.paidCurrencyReward > 0 || a.prestigeCurrencyReward > 0))
                {
                    var rewardParts = new System.Collections.Generic.List<string>();
                    if (a.paidCurrencyReward    > 0) rewardParts.Add($"◆ {a.paidCurrencyReward}");
                    if (a.prestigeCurrencyReward > 0) rewardParts.Add($"✦ {a.prestigeCurrencyReward}");
                    var rewardLabel = new Label(string.Join("  ", rewardParts));
                    rewardLabel.AddToClassList("ach-card-reward");
                    body.Add(rewardLabel);
                }

                row.Add(check);
                row.Add(body);

                // Claim button
                if (isClaimable)
                {
                    var claimBtn = new Button { text = "Claim" };
                    claimBtn.AddToClassList("ach-claim-btn");
                    string capturedId = a.id;
                    claimBtn.clicked += () => ClaimSingle(capturedId);
                    row.Add(claimBtn);
                }

                card.Add(row);

                // Progress bar (for in-progress achievements with quantity > 1)
                if (!isHidden && !isDone && a.triggerQuantity > 1 && save != null)
                {
                    int current = GetProgressFromSave(save, a.id);
                    float ratio = Mathf.Clamp01((float)current / a.triggerQuantity);

                    var barWrap = new VisualElement();
                    barWrap.AddToClassList("ach-progress-bar");
                    var fill = new VisualElement();
                    fill.AddToClassList("ach-progress-fill");
                    fill.style.width = new StyleLength(new Length(ratio * 100f, LengthUnit.Percent));
                    barWrap.Add(fill);
                    card.Add(barWrap);

                    var progLabel = new Label($"{current:N0} / {a.triggerQuantity:N0}");
                    progLabel.AddToClassList("ach-card-desc");
                    card.Add(progLabel);
                }

                _scroll.Add(card);
            }
        }

        private static int GetProgressFromSave(SaveData save, string id)
        {
            if (save?.achievementProgress == null) return 0;
            var entry = save.achievementProgress.Find(e => e.id == id);
            return entry?.count ?? 0;
        }

        // ── Claiming ──────────────────────────────────────────────────────────

        private void ClaimSingle(string id)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;
            if (!save.unclaimedAchievements.Contains(id)) return;

            var a = database?.Get(id);
            if (a == null) return;

            save.unclaimedAchievements.Remove(id);
            if (a.paidCurrencyReward    > 0) save.paidCurrency      += a.paidCurrencyReward;
            if (a.prestigeCurrencyReward > 0) save.prestigeCurrency  += a.prestigeCurrencyReward;

            SaveManager.Instance.SaveLocal();

            string rewardText = a.paidCurrencyReward > 0 ? $"◆ +{a.paidCurrencyReward}" : "Claimed!";
            ShowNotification("◆", rewardText);
            RefreshAll();
        }

        private void ClaimAll()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null || database == null) return;

            foreach (var id in save.unclaimedAchievements.ToList())
            {
                var a = database.Get(id);
                if (a == null || a.category != _activeCategory) continue;
                save.unclaimedAchievements.Remove(id);
                if (a.paidCurrencyReward    > 0) save.paidCurrency      += a.paidCurrencyReward;
                if (a.prestigeCurrencyReward > 0) save.prestigeCurrency  += a.prestigeCurrencyReward;
            }

            SaveManager.Instance.SaveLocal();
            ShowNotification("◆", "All rewards claimed!");
            RefreshAll();
        }

        private void UpdateClaimAllButton()
        {
            if (_btnClaimAll == null) return;
            var save = SaveManager.Instance?.Current;
            bool anyClaimable = save != null &&
                database?.All.Any(a => a != null && a.category == _activeCategory &&
                                       save.unclaimedAchievements.Contains(a.id)) == true;
            HUDController.SetElementVisible(_btnClaimAll, anyClaimable);
        }

        // ── Reset countdown ───────────────────────────────────────────────────

        static string GetResetCountdown(AchievementCategory category)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return "";

            string raw = category switch
            {
                AchievementCategory.Daily   => save.dailyResetUtc,
                AchievementCategory.Weekly  => save.weeklyResetUtc,
                AchievementCategory.Monthly => save.monthlyResetUtc,
                _                           => null
            };

            if (!DateTime.TryParse(raw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var resetAt))
                return "";

            var remaining = resetAt - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) return "Resetting…";

            if (remaining.TotalDays >= 1)
                return $"Resets in {(int)remaining.TotalDays}d {remaining.Hours}h";
            if (remaining.TotalHours >= 1)
                return $"Resets in {(int)remaining.TotalHours}h {remaining.Minutes}m";
            return $"Resets in {remaining.Minutes}m {remaining.Seconds}s";
        }

        // ── Notification banner ───────────────────────────────────────────────

        private void ShowNotification(string icon, string message)
        {
            if (_notificationBanner == null) return;
            if (_notificationIcon    != null) _notificationIcon.text    = icon;
            if (_notificationMessage != null) _notificationMessage.text = message;

            _notificationBanner.style.display = UnityEngine.UIElements.DisplayStyle.Flex;
            _notificationBanner.schedule.Execute(
                () => _notificationBanner?.AddToClassList("notification-banner--shown"));

            if (_hideNotificationCoroutine != null)
                StopCoroutine(_hideNotificationCoroutine);
            _hideNotificationCoroutine = StartCoroutine(HideNotificationAfterDelay());
        }

        private IEnumerator HideNotificationAfterDelay()
        {
            yield return new WaitForSeconds(notificationDuration);
            if (_notificationBanner == null) yield break;

            _notificationBanner.RemoveFromClassList("notification-banner--shown");

            yield return new WaitForSeconds(0.35f);
            _notificationBanner.style.display = UnityEngine.UIElements.DisplayStyle.None;
        }
    }
}
