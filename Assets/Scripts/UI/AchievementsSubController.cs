using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The Achievements panel (achievements-panel in GameHUD.uxml): category tabs, the reset countdown, the
    /// card list (with the login calendar on the Daily tab) and Claim All (#286, moved out of HUDController); plus
    /// the drawer-side plumbing: the first-prestige unlock gate and its hint pulse (drawer handle, then the
    /// Achievements button once the drawer is open), the reminder dots, and claiming from a banner.
    /// HUDController closes other panels before <see cref="Open"/> and forwards drawer open/close to
    /// <see cref="SwapHintPulse"/>. Sibling MonoBehaviour on the HUD GameObject (added at runtime when missing).
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class AchievementsSubController : MonoBehaviour
    {
        AchievementService _service;
        DailyEventsSubController _daily;
        HUDController _hud;
        VisualElement _root;

        // Gate: Weekly/Monthly/Progress unlock with the first prestige (hasCompletedFirstRun).
        Button    _btnMenu;
        Label     _lockedHint;
        bool      _unlocked;
        Coroutine _hintPulse;
        Button    _hintTarget;

        VisualElement _panel;
        ScrollView    _list;
        Label         _title, _resetLabel;
        Button        _tabDaily, _tabWeekly, _tabMonthly, _tabProgression, _claimAll;
        IVisualElementScheduledItem _resetTicker;

        AchievementCategory _category = AchievementCategory.Daily;

        public bool IsOpen => _panel != null && !_panel.ClassListContains("hidden");

        public void Initialize(VisualElement root, AchievementService service, HUDController hud)
        {
            Cleanup();
            _service = service;
            _hud     = hud;
            _root    = root;
            _daily   = GetComponent<DailyEventsSubController>();
            _btnMenu    = root.Q<Button>("btn-achievements");
            _lockedHint = root.Q<Label>("achievements-locked-hint");

            _panel          = root.Q("achievements-panel");
            _list           = root.Q<ScrollView>("achievements-list");
            _title          = root.Q<Label>("achievements-title");
            _resetLabel     = root.Q<Label>("ach-reset-label");
            _tabDaily       = root.Q<Button>("ach-tab-daily");
            _tabWeekly      = root.Q<Button>("ach-tab-weekly");
            _tabMonthly     = root.Q<Button>("ach-tab-monthly");
            _tabProgression = root.Q<Button>("ach-tab-progression");
            _claimAll       = root.Q<Button>("ach-claim-all-btn");

            if (_tabDaily       != null) _tabDaily.clicked       += OnTabDaily;
            if (_tabWeekly      != null) _tabWeekly.clicked      += OnTabWeekly;
            if (_tabMonthly     != null) _tabMonthly.clicked     += OnTabMonthly;
            if (_tabProgression != null) _tabProgression.clicked += OnTabProgression;
            if (_claimAll       != null) _claimAll.clicked       += OnClaimAll;
            var close = root.Q<Button>("btn-close-achievements");
            if (close != null) close.clicked += Close;

            if (_service != null)
            {
                _service.OnAchievementUnlocked += OnUnlocked;
                _service.OnPeriodReset         += RefreshIfOpen;
            }
        }

        public void Cleanup()
        {
            if (_tabDaily       != null) _tabDaily.clicked       -= OnTabDaily;
            if (_tabWeekly      != null) _tabWeekly.clicked      -= OnTabWeekly;
            if (_tabMonthly     != null) _tabMonthly.clicked     -= OnTabMonthly;
            if (_tabProgression != null) _tabProgression.clicked -= OnTabProgression;
            if (_claimAll       != null) _claimAll.clicked       -= OnClaimAll;
            if (_panel != null)
            {
                var close = _panel.Q<Button>("btn-close-achievements");
                if (close != null) close.clicked -= Close;
            }
            if (_service != null)
            {
                _service.OnAchievementUnlocked -= OnUnlocked;
                _service.OnPeriodReset         -= RefreshIfOpen;
            }
        }

        void OnDestroy() => Cleanup();

        void OnTabDaily()       => SwitchCategory(AchievementCategory.Daily);
        void OnTabWeekly()      => SwitchCategory(AchievementCategory.Weekly);
        void OnTabMonthly()     => SwitchCategory(AchievementCategory.Monthly);
        void OnTabProgression() => SwitchCategory(AchievementCategory.Progression);
        void OnUnlocked(AchievementSO _) => RefreshIfOpen();

        void OnClaimAll()
        {
            _service?.ClaimAllRewards();
            Rebuild();
        }

        void Close() => HUDController.SetElementVisible(_panel, false);

        static bool FirstRunDone => SaveManager.Instance?.Current?.tutorial.hasCompletedFirstRun ?? false;

        /// <summary>
        /// Weekly, Monthly and Progress unlock with the first prestige; until then their tabs show a lock.
        /// The menu itself is always open (Daily works from the start).
        /// </summary>
        public void ApplyGate(bool unlocked)
        {
            _btnMenu?.SetEnabled(true);
            HUDController.SetElementVisible(_lockedHint, false);
            foreach (var tab in new[] { _tabWeekly, _tabMonthly, _tabProgression })
                tab?.EnableInClassList("achievements-tab--locked", !unlocked);
        }

        /// <summary>Applies the gate at scene start.</summary>
        public void InitGate()
        {
            _unlocked = FirstRunDone;
            ApplyGate(_unlocked);
            GameLogger.Info($"[HUD] Achievements gate at scene start: unlocked={_unlocked}");
        }

        /// <summary>
        /// Per-frame: the first prestige in this session unlocks Weekly/Monthly/Progress. Re-applies the gate, lets
        /// the service start those periods, and points the player at the menu (hint + pulse).
        /// </summary>
        public void TickGate()
        {
            if (_unlocked || !FirstRunDone) return;
            GameLogger.Info("[HUD] Achievements gate flipped: showing unlock hint.");
            _unlocked = true;
            ApplyGate(true);
            _service?.InitPostPrestige();
            _hud?.ShowTutorialHint("🏆 Weekly, Monthly and Progress achievements unlocked! Open the drawer and tap Achievements.");
            SwapHintPulse("btn-drawer-handle");
        }

        /// <summary>True while the unlock hint is pulsing a button.</summary>
        public bool HintPulsing => _hintPulse != null;

        /// <summary>Moves the unlock-hint pulse to <paramref name="buttonId"/> (the handle, or the drawer row once open).</summary>
        public void SwapHintPulse(string buttonId)
        {
            StopHintPulse();
            var btn = _root?.Q<Button>(buttonId);
            if (btn == null) return;
            _hintTarget = btn;
            _hintPulse  = StartCoroutine(PulseHint(btn));
        }

        /// <summary>The player found the menu: hide the hint and stop the pulse.</summary>
        public void DismissHint()
        {
            _hud?.HideTutorialHint();
            StopHintPulse();
        }

        void StopHintPulse()
        {
            if (_hintPulse != null) { StopCoroutine(_hintPulse); _hintPulse = null; }
            _hintTarget?.RemoveFromClassList("panel-btn--highlight");
            _hintTarget = null;
        }

        static IEnumerator PulseHint(Button btn)
        {
            while (true)
            {
                btn.AddToClassList("panel-btn--highlight");
                yield return new WaitForSecondsRealtime(0.7f);
                btn.RemoveFromClassList("panel-btn--highlight");
                yield return new WaitForSecondsRealtime(0.7f);
            }
        }

        // ── Reminder dots + banner claims ─────────────────────────────────────

        static bool TutorialActive => SaveManager.Instance?.Current?.tutorial?.isActive ?? false;

        static bool LoginRewardWaiting =>
            FeatureFlags.DailyEventsEnabled && (DailyEventService.Instance?.CanClaimLoginReward() ?? false);

        /// <summary>
        /// Registers the red dots: the drawer row (which also lights the ≡ handle) and each tab with a reward
        /// waiting. Add future menu-row dots (shop, daily) to <paramref name="reminders"/> with <c>menuRow: true</c>.
        /// </summary>
        public void RegisterReminders(MenuReminderController reminders, VisualElement root)
        {
            if (reminders == null) return;
            reminders.Register("btn-achievements",
                () => !TutorialActive && ((_service != null && _service.ClaimableCount > 0) || LoginRewardWaiting),
                menuRow: true);
            RegisterTab(reminders, "ach-tab-daily",       AchievementCategory.Daily);
            RegisterTab(reminders, "ach-tab-weekly",      AchievementCategory.Weekly);
            RegisterTab(reminders, "ach-tab-monthly",     AchievementCategory.Monthly);
            RegisterTab(reminders, "ach-tab-progression", AchievementCategory.Progression);
            reminders.Init(root);
        }

        void RegisterTab(MenuReminderController reminders, string tab, AchievementCategory category) =>
            reminders.Register(tab,
                () => !TutorialActive && ((_service != null && _service.HasClaimable(category))
                                          || (category == AchievementCategory.Daily && LoginRewardWaiting)),
                menuRow: false);

        /// <summary>The ✓ on an achievement banner: claims it and says so.</summary>
        public void ClaimFromBanner(string id)
        {
            if (_service == null || !_service.IsClaimable(id)) return;
            var a = _service.GetAll()?.FirstOrDefault(x => x != null && x.id == id);
            _service.ClaimReward(id);
            RefreshIfOpen();
            if (a != null) _hud?.ShowNotification("♦", $"Claimed: {a.displayName}");   // not ✓: blank on Android
        }

        /// <summary>The first tab with a reward waiting, so the menu opens where the red dot is; Daily otherwise.</summary>
        public AchievementCategory FirstClaimableCategory()
        {
            foreach (var c in new[] { AchievementCategory.Daily, AchievementCategory.Weekly,
                                      AchievementCategory.Monthly, AchievementCategory.Progression })
                if (_service != null && _service.HasClaimable(c)) return c;
            return AchievementCategory.Daily;
        }

        /// <summary>Shows the panel on <paramref name="category"/>. The caller closes other panels first.</summary>
        public void Open(AchievementCategory category)
        {
            _category = category;
            // A period may have ended since the last check (long session); a reset rebuilds via OnPeriodReset.
            _service?.RequestPeriodCheck("menu opened");
            Rebuild();
            HUDController.SetElementVisible(_panel, true);

            // Keep the reset countdown live while the panel is open (paused when it closes).
            if (_resetLabel == null) return;
            if (_resetTicker == null) _resetTicker = _resetLabel.schedule.Execute(TickResetLabel).Every(1000);
            else                      _resetTicker.Resume();
        }

        /// <summary>Rebuilds the list when the panel is showing (a claim from a banner, an unlock, a reset).</summary>
        public void RefreshIfOpen()
        {
            if (IsOpen) Rebuild();
        }

        void SwitchCategory(AchievementCategory category)
        {
            _category = category;
            Rebuild();
        }

        void TickResetLabel()
        {
            if (!IsOpen) { _resetTicker?.Pause(); return; }
            _service?.CheckPeriodsIfDue();   // countdown hit zero: reset now, not on next launch
            UpdateResetLabel();
        }

        // Sets only the label, so the tick does not rebuild the list and reset its scroll position.
        void UpdateResetLabel()
        {
            if (_resetLabel == null) return;
            _resetLabel.text = _category == AchievementCategory.Progression || !AchievementService.CategoryUnlocked(_category)
                ? ""
                : _service?.GetResetCountdown(_category) ?? "";
        }

        void Rebuild()
        {
            if (_list == null) return;
            _list.Clear();
            UpdateTabHighlights();
            UpdateResetLabel();

            var all = _service?.GetAll();
            if (all == null || all.Count == 0)
            {
                _list.Add(new Label("No achievements defined."));
                if (_title != null) _title.text = "Achievements (0 / 0)";
                return;
            }

            if (_title != null) _title.text = $"Achievements ({_service.CompletedCount} / {all.Count})";

            // The Daily tab opens with the 28-day login calendar (the old Daily panel, merged in 2026-11-20).
            if (_category == AchievementCategory.Daily)
            {
                var card = _daily?.BuildLoginCard(RefreshIfOpen);
                if (card != null) _list.Add(card);
            }

            if (!AchievementService.CategoryUnlocked(_category))
            {
                var locked = new Label("Unlocks after your first prestige.");
                locked.AddToClassList("ach-locked-msg");
                _list.Add(locked);
                HUDController.SetElementVisible(_claimAll, _service.ClaimableCount > 0);
                return;
            }

            foreach (var achievement in all)
            {
                if (achievement == null || achievement.category != _category) continue;
                _list.Add(BuildCard(achievement));
            }

            HUDController.SetElementVisible(_claimAll, _service.ClaimableCount > 0);
        }

        VisualElement BuildCard(AchievementSO achievement)
        {
            bool isDone      = _service.IsCompleted(achievement.id);
            bool isClaimable = _service.IsClaimable(achievement.id);
            bool isHidden    = achievement.isHidden && !isDone;

            var row = new VisualElement();
            row.AddToClassList("ach-card");
            if (isDone && !isClaimable) row.AddToClassList("ach-card--completed");
            else if (isClaimable)       row.AddToClassList("ach-card--claimable");

            var innerRow = new VisualElement();
            innerRow.AddToClassList("ach-card-row");

            var checkLabel = new Label(isDone ? "" : "○");
            checkLabel.AddToClassList("ach-card-check");
            if (isDone) GlyphIcon.SetOn(checkLabel, GlyphIcon.Kind.Check);
            if (isDone) checkLabel.AddToClassList("ach-card-check--done");

            var body = new VisualElement();
            body.AddToClassList("ach-card-body");

            var nameLabel = new Label(isHidden ? "???" : achievement.displayName);
            nameLabel.AddToClassList("ach-card-name");
            var descLabel = new Label(isHidden ? "Complete a hidden objective to reveal." : achievement.description);
            descLabel.AddToClassList("ach-card-desc");
            body.Add(nameLabel);
            body.Add(descLabel);

            // Currency reward hint
            if (!isHidden && (achievement.paidCurrencyReward > 0 || achievement.prestigeCurrencyReward > 0))
            {
                var parts = new System.Collections.Generic.List<string>();
                if (achievement.paidCurrencyReward     > 0) parts.Add($"♦ {achievement.paidCurrencyReward}");
                if (achievement.prestigeCurrencyReward > 0) parts.Add($"✦ {achievement.prestigeCurrencyReward}");
                var rewardLabel = new Label(string.Join("  ", parts));
                rewardLabel.AddToClassList("ach-card-reward");
                body.Add(rewardLabel);
            }

            // Cosmetic rewards (legacy)
            if (!isHidden && achievement.rewards != null && achievement.rewards.Length > 0)
            {
                var cosmeticNames = string.Join(", ", System.Array.ConvertAll(
                    achievement.rewards, r => r != null ? r.displayName : "?"));
                var cosmeticLabel = new Label($"Unlocks: {cosmeticNames}");
                cosmeticLabel.AddToClassList("ach-card-desc");
                body.Add(cosmeticLabel);
            }

            innerRow.Add(checkLabel);
            innerRow.Add(body);

            if (isClaimable)
            {
                var claimBtn = new Button { text = "Claim" };
                claimBtn.AddToClassList("ach-claim-btn");
                string capturedId = achievement.id;
                claimBtn.clicked += () =>
                {
                    _service?.ClaimReward(capturedId);
                    Rebuild();
                };
                innerRow.Add(claimBtn);
            }

            row.Add(innerRow);

            // Progress bar (in-progress achievements with quantity > 1)
            if (!isHidden && !isDone && achievement.triggerQuantity > 1)
            {
                int progress = _service.GetProgress(achievement.id);
                float ratio  = Mathf.Clamp01((float)progress / achievement.triggerQuantity);

                var barWrap = new VisualElement();
                barWrap.AddToClassList("ach-progress-bar");
                var fill = new VisualElement();
                fill.AddToClassList("ach-progress-fill");
                fill.style.width = new StyleLength(new Length(ratio * 100f, LengthUnit.Percent));
                barWrap.Add(fill);
                row.Add(barWrap);

                var progLabel = new Label($"{progress:N0} / {achievement.triggerQuantity:N0}");
                progLabel.AddToClassList("ach-card-desc");
                row.Add(progLabel);
            }
            return row;
        }

        void UpdateTabHighlights()
        {
            TabActive(_tabDaily,       _category == AchievementCategory.Daily);
            TabActive(_tabWeekly,      _category == AchievementCategory.Weekly);
            TabActive(_tabMonthly,     _category == AchievementCategory.Monthly);
            TabActive(_tabProgression, _category == AchievementCategory.Progression);
        }

        static void TabActive(Button tab, bool active) => tab?.EnableInClassList("achievements-tab--active", active);
    }
}
