using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The Entropy Surge offer: a side icon that appears whenever the Entropy Surge ad is off its 15-minute
    /// cooldown, and the card it opens.
    ///  - <b>Watch ad</b> plays a rewarded ad (AdService); on reward the cooldown restarts from the watch.
    ///  - <b>No thanks</b> closes the card and restarts the cooldown, so the icon comes back in 15 minutes.
    ///    It does not use up one of the day's watches.
    ///  - <b>X</b> (and Android back) only closes the card: the icon stays, and the next tap recalculates
    ///    the amount from the current income.
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
                                          int remainingToday, TimeSpan cooldownLeft, bool cardOpen)
            => adsEnabled && firstPrestigeDone && gameLoaded && adReady
               && remainingToday > 0 && cooldownLeft <= TimeSpan.Zero && !cardOpen;

        void Update()
        {
            if (_icon == null || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;

            var ads  = AdService.Instance;
            var save = SaveManager.Instance?.Current;
            bool show = ShouldShowIcon(
                FeatureFlags.AdsEnabled,
                save?.tutorial.hasCompletedFirstRun ?? false,
                ECSLoadBridge.Instance != null && ECSLoadBridge.Instance.IsLoaded,
                ads != null && ads.IsReady,
                ads?.Remaining(Placement) ?? 0,
                ads?.CooldownRemaining(Placement) ?? TimeSpan.Zero,
                IsOpen);
            SetIconVisible(show);
        }

        void SetIconVisible(bool visible)
        {
            if (_icon == null || visible == _iconShown) return;
            _iconShown = visible;
            HUDController.SetElementVisible(_icon, visible);
            if (!visible) return;
            // A short pop so the player notices it arriving.
            _icon.AddToClassList("surge-offer-btn--pop");
            _icon.schedule.Execute(() => _icon.RemoveFromClassList("surge-offer-btn--pop")).StartingIn(250);
        }

        void OpenCard()
        {
            var ads = AdService.Instance;
            if (ads == null || _card == null) return;

            // Recalculated on every open: income moves, so a card reopened after the X shows today's figure.
            long amount = ads.PreviewIncomeReward(Placement);
            if (_message != null)
                _message.text = MessageText(amount, ads.Remaining(Placement));

            IsOpen = true;
            SetIconVisible(false);
            HUDController.SetElementVisible(_card, true);
            UIInputBlocker.SetModal(_modalOwner, true);
        }

        internal static string MessageText(long amount, int remainingToday) =>
            $"Watch a short ad for +{amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} ◈ entropy?\n({remainingToday} left today)";

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
    }
}
