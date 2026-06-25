using System;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>Outcome of an attempt to show a rewarded ad.</summary>
    public enum AdShowOutcome
    {
        Granted,           // ad watched and reward applied
        AtDailyCap,        // this placement's daily limit is reached
        Disabled,          // ads.enabled flag is off
        NotReady,          // no ad cached / provider not ready
        Declined,          // user dismissed the ad without earning the reward
        NoSave,            // SaveManager not ready
        NothingToCollect,  // reward would have been empty (e.g. TimeWarp with no production)
    }

    /// <summary>Result handed back to the UI after a show attempt.</summary>
    public struct AdShowResult
    {
        public AdShowOutcome Outcome;
        public AdPlacement   Placement;
        public string        Message;     // human-friendly summary for a toast
        public bool Granted => Outcome == AdShowOutcome.Granted;
    }

    /// <summary>
    /// Owns rewarded-ad presentation and reward granting. Talks to an <see cref="IAdProvider"/> (real
    /// LevelPlay on device, <see cref="MockAdProvider"/> in Editor/CI), enforces per-placement daily caps
    /// via <see cref="AdRewardCalculator"/>, and grants rewards through the same infrastructure the
    /// premium shop uses (ECSLoadBridge / SaveData), so no new currency plumbing is introduced.
    ///
    /// Created by <see cref="PremiumServicesBootstrap"/>; persists across scenes alongside the other
    /// premium-economy singletons. Gated by the <c>ads.enabled</c> remote kill-switch.
    /// </summary>
    public class AdService : SingletonMonoBehaviour<AdService>
    {
        protected override bool PersistAcrossScenes => true;

        IAdProvider _provider;
        bool        _initStarted;

        /// <summary>Exposed for tests so reward/limit logic can run against the mock without a device.</summary>
        public IAdProvider Provider => _provider;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Start()
        {
            if (!FeatureFlags.AdsEnabled)
            {
                GameLogger.Info("[Ads] Disabled by feature flag — provider not initialised.");
                return;
            }
            EnsureProvider();
        }

        void EnsureProvider()
        {
            if (_initStarted) return;
            _initStarted = true;

#if LEVELPLAY_ADS && !UNITY_EDITOR
            _provider = new LevelPlayAdProvider();
#else
            _provider = new MockAdProvider();
#endif
            _ = InitProviderAsync();
        }

        async System.Threading.Tasks.Task InitProviderAsync()
        {
            try
            {
                await _provider.InitializeAsync();
                _provider.LoadRewarded();
            }
            catch (Exception e)
            {
                GameLogger.Warning($"[Ads] Provider init failed: {e.Message}");
            }
        }

        /// <summary>Test seam: inject a provider directly (e.g. a configured MockAdProvider).</summary>
        public void SetProviderForTest(IAdProvider provider)
        {
            _provider    = provider;
            _initStarted = true;
        }

        /// <summary>
        /// Opens the ad network's integration Test Suite (development builds only — the provider only
        /// enables it under Debug.isDebugBuild). Use it on a device to load/show test rewarded ads and
        /// verify mediation without registering advertising IDs.
        /// </summary>
        public void LaunchTestSuite()
        {
            EnsureProvider();
            _provider?.LaunchTestSuite();
        }

        // ── Public query API (for the UI) ─────────────────────────────────────

        /// <summary>True when ads are enabled and a rewarded ad can currently be shown.</summary>
        public bool IsReady => FeatureFlags.AdsEnabled && _provider != null && _provider.IsRewardedReady;

        public int Remaining(AdPlacement p)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return 0;
            AdRewardCalculator.ApplyDailyReset(save, DateTime.UtcNow);
            return AdRewardCalculator.Remaining(save, p);
        }

        /// <summary>True if the player may watch this placement right now (enabled, ready, under cap).</summary>
        public bool CanWatch(AdPlacement p)
        {
            if (!FeatureFlags.AdsEnabled) return false;
            if (_provider == null || !_provider.IsRewardedReady) return false;
            return Remaining(p) > 0;
        }

        // ── Show / grant ──────────────────────────────────────────────────────

        /// <summary>
        /// Shows a rewarded ad for a standard placement (everything except DoubleOffline, which needs the
        /// pending idle result — use <see cref="ShowDoubleOffline"/>). On reward, applies the grant,
        /// increments the daily counter, and saves. <paramref name="onDone"/> always fires exactly once.
        /// </summary>
        public void Show(AdPlacement p, Action<AdShowResult> onDone)
        {
            if (p == AdPlacement.DoubleOffline)
            {
                GameLogger.Warning("[Ads] DoubleOffline must be shown via ShowDoubleOffline().");
                Finish(onDone, new AdShowResult { Outcome = AdShowOutcome.NothingToCollect, Placement = p });
                return;
            }
            ShowInternal(p, null, onDone);
        }

        /// <summary>Shows the "double your offline earnings" ad, re-granting <paramref name="pending"/>.</summary>
        public void ShowDoubleOffline(IdleCollectionResult pending, Action<AdShowResult> onDone)
        {
            if (pending == null || !pending.HasAnyOutput)
            {
                Finish(onDone, new AdShowResult
                {
                    Outcome = AdShowOutcome.NothingToCollect, Placement = AdPlacement.DoubleOffline,
                    Message = "Nothing to double.",
                });
                return;
            }
            ShowInternal(AdPlacement.DoubleOffline, pending, onDone);
        }

        void ShowInternal(AdPlacement p, IdleCollectionResult doubleOfflineResult, Action<AdShowResult> onDone)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) { Finish(onDone, Fail(p, AdShowOutcome.NoSave, "Save not ready.")); return; }

            if (!FeatureFlags.AdsEnabled) { Finish(onDone, Fail(p, AdShowOutcome.Disabled, "Ads are disabled.")); return; }

            AdRewardCalculator.ApplyDailyReset(save, DateTime.UtcNow);
            if (AdRewardCalculator.IsAtCap(save, p))
            {
                Finish(onDone, Fail(p, AdShowOutcome.AtDailyCap, "Daily limit reached — come back tomorrow."));
                return;
            }
            if (_provider == null || !_provider.IsRewardedReady)
            {
                Finish(onDone, Fail(p, AdShowOutcome.NotReady, "No ad available right now."));
                return;
            }

            _provider.ShowRewarded(earned =>
            {
                if (!earned)
                {
                    _provider.LoadRewarded();
                    Finish(onDone, Fail(p, AdShowOutcome.Declined, "Ad skipped — no reward."));
                    return;
                }

                bool granted = GrantReward(save, p, doubleOfflineResult, out string message);
                _provider.LoadRewarded();

                if (!granted)
                {
                    Finish(onDone, Fail(p, AdShowOutcome.NothingToCollect, message ?? "Nothing to collect."));
                    return;
                }

                AdRewardCalculator.IncrementWatch(save, p);
                SaveManager.Instance.SaveLocal();
                Finish(onDone, new AdShowResult { Outcome = AdShowOutcome.Granted, Placement = p, Message = message });
            });
        }

        /// <summary>
        /// Applies a placement's reward to the live game state, reusing the premium-shop grant paths.
        /// Returns false (with no mutation that counts) when there is nothing to grant — e.g. a Time Warp
        /// while nothing is producing. Out <paramref name="message"/> is a toast-ready summary.
        /// </summary>
        bool GrantReward(SaveData save, AdPlacement p, IdleCollectionResult doubleOfflineResult, out string message)
        {
            var def = AdRewardCalculator.Def(p);
            switch (def.Kind)
            {
                case AdRewardKind.Entropy:
                {
                    long amount = AdRewardCalculator.CalcEntropyReward(save.lastKnownNetWorth);
                    save.currentRun.baseCurrency += amount;
                    ApplyEntropyToECS(amount);
                    message = $"+{amount:N0} entropy";
                    return true;
                }
                case AdRewardKind.Crystals:
                {
                    save.paidCurrency += def.CrystalAmount;
                    message = $"+{def.CrystalAmount} crystals";
                    return true;
                }
                case AdRewardKind.SpeedBoost:
                {
                    save.speedBoostExpiryUtc = PremiumShopCalculator.CalcSpeedBoostExpiry(
                        save.speedBoostExpiryUtc, AdRewardCalculator.SpeedBoostDuration);
                    message = $"2x production for {AdRewardCalculator.SpeedBoostDuration.TotalMinutes:F0} min";
                    return true;
                }
                case AdRewardKind.IdleRateBoost:
                {
                    save.adsIdleBoostExpiryUtc = PremiumShopCalculator.CalcSpeedBoostExpiry(
                        save.adsIdleBoostExpiryUtc, TimeSpan.FromHours(def.DurationHours));
                    message = $"+50% idle collection for {def.DurationHours:F0}h";
                    return true;
                }
                case AdRewardKind.TimeWarp:
                {
                    return GrantTimeWarp(save, def.DurationHours, out message);
                }
                case AdRewardKind.DoubleOffline:
                {
                    return GrantDoubleOffline(save, doubleOfflineResult, out message);
                }
                default:
                    message = null;
                    return false;
            }
        }

        bool GrantTimeWarp(SaveData save, float hours, out string message)
        {
            // Refresh snapshot + currentRun from the live world, then compute what `hours` would yield.
            SaveManager.Instance.SaveLocal();
            var config   = GameBootstrap.Instance?.gameConfig;
            var upgrades = PersistentUpgradeService.Instance;
            var result   = OfflineCollectionService.ComputeForDuration(save, config, upgrades, hours * 3600f);

            if (result == null || !result.HasAnyOutput) { message = "Nothing producing to bank."; return false; }

            ApplyCollectionResult(save, result);
            message = $"Banked {hours:F0}h of production";
            return true;
        }

        bool GrantDoubleOffline(SaveData save, IdleCollectionResult result, out string message)
        {
            if (result == null || !result.HasAnyOutput) { message = "Nothing to double."; return false; }
            ApplyCollectionResult(save, result);
            message = "Offline earnings doubled!";
            return true;
        }

        // ── ECS write-through helpers (mirror PremiumShopService) ──────────────

        void ApplyEntropyToECS(long amount)
        {
            var bridge = ECSLoadBridge.Instance;
            if (bridge == null || !bridge.IsLoaded) return;
            bridge.AddEntropy(amount);
        }

        void ApplyCollectionResult(SaveData save, IdleCollectionResult result)
        {
            var bridge = ECSLoadBridge.Instance;
            if (bridge != null && bridge.IsLoaded)
            {
                ApplyEntropyToECS(result.EntropyEarned);
                bridge.AddInventoryItems(result.ItemsEarned);
            }
            else
            {
                OfflineCollectionService.ApplyResultToSave(save, result);
            }
        }

        // ── Internals ─────────────────────────────────────────────────────────

        static AdShowResult Fail(AdPlacement p, AdShowOutcome outcome, string message)
            => new AdShowResult { Outcome = outcome, Placement = p, Message = message };

        static void Finish(Action<AdShowResult> onDone, AdShowResult result) => onDone?.Invoke(result);
    }
}
