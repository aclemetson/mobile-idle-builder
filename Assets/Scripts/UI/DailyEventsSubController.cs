using System;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Builds the login reward card at the top of the Achievements menu's Daily tab. The separate Daily panel
    /// (login reward + rotating challenges) was merged into the Achievements menu on 2026-11-20: the challenges
    /// became Daily achievements, and this card is all that is left of the panel. Reward granting lives in
    /// <see cref="DailyEventService"/>; this controller is the view only.
    /// Sits as a component alongside HUDController on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class DailyEventsSubController : MonoBehaviour
    {
        private HUDController     _hud;
        private DailyEventService _subscribedTo;
        private Action            _redraw;

        public void Init(VisualElement root, HUDController hud) => _hud = hud;

        /// <summary>
        /// The login reward card, or null when the calendar is switched off (remote flag) or has no content.
        /// <paramref name="redraw"/> rebuilds the tab; it runs after a claim and when a server-time sync lands.
        /// </summary>
        public VisualElement BuildLoginCard(Action redraw)
        {
            if (!FeatureFlags.DailyEventsEnabled) return null;
            var svc = DailyEventService.Instance;
            if (svc == null) return null;

            _redraw = redraw;
            if (_subscribedTo != svc)
            {
                if (_subscribedTo != null) _subscribedTo.OnChanged -= OnServiceChanged;
                svc.OnChanged += OnServiceChanged;
                _subscribedTo = svc;
            }
            svc.EnsureToday();   // starts a server-time sync if needed; OnChanged redraws when it lands

            var reward = svc.CurrentLoginReward;
            if (reward == null) return null;
            bool canClaim = svc.CanClaimLoginReward();
            bool offline  = svc.WaitingForServerTime;

            var card = new VisualElement();
            card.AddToClassList("ach-card");
            card.AddToClassList(canClaim ? "ach-card--claimable" : "ach-card--completed");

            var row = new VisualElement();
            row.AddToClassList("ach-card-row");

            var icon = new Label("🎁");
            icon.AddToClassList("ach-card-check");
            row.Add(icon);

            var body = new VisualElement();
            body.AddToClassList("ach-card-body");
            var name = new Label($"Login reward: day {reward.day} of {svc.Content.loginRewards.Length}");
            name.AddToClassList("ach-card-name");
            body.Add(name);

            var sb = new StringBuilder($"♦ {reward.crystals}");
            // Scales with production (entropyMinutes): priced from the idle snapshot each redraw.
            long entropy = DailyEventService.EntropyFor(reward, SaveManager.Instance?.Current);
            if (entropy > 0)                 sb.Append($"   {NumberFormat.Entropy(entropy)}");
            if (reward.prestigeCurrency > 0) sb.Append($"   ✦ {reward.prestigeCurrency}");
            var rewardLabel = new Label(sb.ToString());
            rewardLabel.AddToClassList("ach-card-reward");
            body.Add(rewardLabel);

            var desc = new Label(offline   ? "Connect to the internet to claim."
                               : canClaim  ? "Claim once a day. Missing a day never resets your streak."
                                           : "Claimed. Come back tomorrow!");
            desc.AddToClassList("ach-card-desc");
            body.Add(desc);
            row.Add(body);

            if (canClaim)
            {
                var claim = new Button { text = "Claim" };
                claim.AddToClassList("ach-claim-btn");
                claim.clicked += OnClaimPressed;
                row.Add(claim);
            }

            card.Add(row);
            return card;
        }

        private void OnServiceChanged() => _redraw?.Invoke();

        private void OnClaimPressed()
        {
            var svc = DailyEventService.Instance;
            if (svc == null) return;
            var reward = svc.CurrentLoginReward;
            if (svc.ClaimLoginReward())
                _hud?.ShowNotification("🎁", reward != null
                    ? $"Login reward claimed: {RewardSummary(reward.crystals, svc.LastClaimedEntropy, reward.prestigeCurrency)}"
                    : "Login reward claimed!");
            // ClaimLoginReward fires OnChanged, which redraws the tab.
        }

        /// <summary>"+♦40, +◈ 1.2M, +✦1": every currency a login reward granted.</summary>
        internal static string RewardSummary(int crystals, long entropy, int prestigeCurrency)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (crystals > 0)         parts.Add($"+♦{crystals}");
            if (entropy > 0)          parts.Add("+" + NumberFormat.Entropy(entropy));
            if (prestigeCurrency > 0) parts.Add($"+✦{prestigeCurrency}");
            return parts.Count > 0 ? string.Join(", ", parts) : "nothing";
        }

        private void OnDestroy()
        {
            if (_subscribedTo != null) _subscribedTo.OnChanged -= OnServiceChanged;
        }
    }
}
