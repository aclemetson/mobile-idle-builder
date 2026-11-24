using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The Entropy Surge offer: a side icon that appears whenever the Entropy Surge ad is off its 15-minute
    /// cooldown, and the card it opens. Each watch rolls 10% of net worth in entropy (80%) or 10 crystals (20%);
    /// there is no daily cap (#489).
    ///  - <b>Watch ad</b> plays a rewarded ad (AdService); on reward the cooldown restarts from the watch.
    ///  - <b>No thanks</b> closes the card and restarts the cooldown, so the icon comes back in 15 minutes.
    ///  - <b>X</b> (and Android back) only closes the card: the icon stays, and the next tap recalculates
    ///    the amount from the current net worth.
    ///
    /// Added to the HUD GameObject by <see cref="HUDController"/> (GetComponent ?? AddComponent), so no
    /// scene wiring is needed. Shown only after the first prestige, when the post-prestige lull it is
    /// paced for begins (docs/balance/prestige-pacing.xlsx).
    /// </summary>
    public class EntropySurgeOfferController : MonoBehaviour
    {
        const AdPlacement Placement = AdPlacement.EntropySurge;
        const float RefreshSeconds  = 0.5f;

        HUDController _hud;
        Button        _icon;
        VisualElement _card;
        Label         _message;
        float         _nextRefresh;
        bool          _iconShown;
        bool          _menuOpen;      // HUDController.MenuOpen as of the last frame
        bool          _skipNextPop;   // icon returning after a menu closed: no "new offer" pop
        readonly object _modalOwner = new();

        public bool IsOpen { get; private set; }

        public void Init(VisualElement root, HUDController hud)
        {
            _hud     = hud;
            _icon    = root?.Q<Button>("surge-offer-btn");
            _card    = root?.Q("surge-offer-modal");
            _message = _card?.Q<Label>("surge-offer__message");

            if (_icon == null || _card == null)
            {
                GameLogger.Warning("[Ads] surge-offer-btn / surge-offer-modal missing from GameHUD.uxml — Entropy Surge offer disabled.");
                return;
            }

            _icon.clicked += OpenCard;
            _card.Q<Button>("surge-offer__watch")  .clicked += Watch;
            _card.Q<Button>("surge-offer__decline").clicked += Decline;
            _card.Q<Button>("surge-offer__close")  .clicked += Minimize;

            HUDController.SetElementVisible(_card, false);
            SetIconVisible(false);
        }

        /// <summary>
        /// Whether the side icon should be showing. Pure so the rule is unit-tested.
        /// </summary>
        public static bool ShouldShowIcon(bool adsEnabled, bool firstPrestigeDone, bool gameLoaded, bool adReady,
                                          TimeSpan cooldownLeft, bool cardOpen, bool menuOpen = false)
            => adsEnabled && firstPrestigeDone && gameLoaded && adReady
               && cooldownLeft <= TimeSpan.Zero && !cardOpen && !menuOpen;

        void Update()
        {
            if (_icon == null) return;

            // A menu opening or closing re-evaluates at once: the icon steps out of the way of any open
            // menu (it sat on the building menu's right edge) and returns, without its pop, when it closes.
            bool menuOpen = _hud != null && _hud.MenuOpen;
            if (menuOpen != _menuOpen)
            {
                _menuOpen = menuOpen;
                _nextRefresh = 0f;
                if (!menuOpen) _skipNextPop = true;
            }

            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;

            // Between a prestige and the reload the old run is gone: no offer, and an open card closes,
            // so nothing can be watched against the old run's income.
            if (PrestigeRunReset.ReloadPending)
            {
                if (IsOpen) CloseCard();
                SetIconVisible(false);
                return;
            }

            var ads  = AdService.Instance;
            var save = SaveManager.Instance?.Current;

            if (IsOpen && ads == null) CloseCard();
            // A timed boost started (shop, another ad, Cyclotron Kick): no other boost may be taken now.
            if (IsOpen && ads.BoostLockRemaining(Placement) > TimeSpan.Zero) CloseCard();

            bool show = ShouldShowIcon(
                FeatureFlags.AdsEnabled || (ads != null && ads.SkipsAds),   // ad-free keeps the surge (#503)
                save?.tutorial.hasCompletedFirstRun ?? false,
                ECSLoadBridge.Instance != null && ECSLoadBridge.Instance.IsLoaded,
                ads != null && ads.IsReady,
                // A running timed boost hides the offer like a cooldown does (BoostLock).
                Max(ads?.CooldownRemaining(Placement) ?? TimeSpan.Zero, ads?.BoostLockRemaining(Placement) ?? TimeSpan.Zero),
                IsOpen,
                _menuOpen);
            SetIconVisible(show);
            _skipNextPop = false;
        }

        void SetIconVisible(bool visible)
        {
            if (_icon == null || visible == _iconShown) return;
            _iconShown = visible;
            HUDController.SetElementVisible(_icon, visible);
            if (!visible || _skipNextPop) return;
            // A short pop so the player notices it arriving.
            _icon.AddToClassList("surge-offer-btn--pop");
            _icon.schedule.Execute(() => _icon.RemoveFromClassList("surge-offer-btn--pop")).StartingIn(250);
        }

        void OpenCard()
        {
            var ads = AdService.Instance;
            if (ads == null || _card == null) return;

            // Recalculated on every open: net worth moves, so a card reopened after the X shows today's figure.
            if (_message != null)
                _message.text = MessageText(ads.PreviewEntropyReward(), AdRewardCalculator.Def(Placement), ads.SkipsAds,
                                            AdRewardCalculator.SurgeCrystalChance(SaveManager.Instance?.Current));
            var watch = _card.Q<Button>("surge-offer__watch");
            if (watch != null) watch.text = ads.SkipsAds ? "Claim" : "▶ Watch ad";

            IsOpen = true;
            SetIconVisible(false);
            HUDController.SetElementVisible(_card, true);
            UIInputBlocker.SetModal(_modalOwner, true);
        }

        /// <summary>"Watch a short ad for +60,000 ◈ entropy (80%) or 10 ◆ crystals (20%)?" ("Claim ..." when ad-free).</summary>
        internal static string MessageText(long entropy, AdPlacementDef def, bool adFree = false, float crystalChance = -1f)
        {
            int crystalPct = UnityEngine.Mathf.RoundToInt((crystalChance >= 0f ? crystalChance : def.CrystalChance) * 100f);
            string amount = entropy.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            return (adFree ? "Claim" : "Watch a short ad for") + $" +{amount} ◈ entropy ({100 - crystalPct}%)"
                 + $" or {def.CrystalAmount} ◆ crystals ({crystalPct}%)?";
        }

        void Watch()
        {
            CloseCard();
            var ads = AdService.Instance;
            if (ads == null) return;
            ads.Show(Placement, result =>
            {
                if (result.Granted)
                    _hud?.ShowNotification("◈", $"Entropy Surge: {result.Message}");
                else if (!string.IsNullOrEmpty(result.Message))
                    _hud?.ShowNotification("▶", result.Message);
                _nextRefresh = 0f;   // re-evaluate the icon now
            });
        }

        void Decline()
        {
            CloseCard();
            AdService.Instance?.DeclineOffer(Placement);
            _nextRefresh = 0f;
        }

        /// <summary>X button / Android back: close the card, keep the offer (the icon comes straight back).</summary>
        public void Minimize()
        {
            if (!IsOpen) return;
            CloseCard();
            _nextRefresh = 0f;
        }

        void CloseCard()
        {
            IsOpen = false;
            HUDController.SetElementVisible(_card, false);
            UIInputBlocker.SetModal(_modalOwner, false);
        }

        void OnDisable() => UIInputBlocker.SetModal(_modalOwner, false);

        static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
    }
}
