using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Populates and manages the Daily Events panel (login reward + rotating challenges).
    /// Sits as a component alongside HUDController on the same GameObject; driven via
    /// Init/Refresh like the other HUD sub-controllers. Reward granting lives in
    /// DailyEventService — this controller is the view layer only.
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class DailyEventsSubController : MonoBehaviour
    {
        private HUDController _hud;
        private VisualElement _panel;
        private Label         _loginInfo;
        private Button        _claimLoginBtn;
        private VisualElement _challengesList;

        public void Init(VisualElement root, HUDController hud)
        {
            _hud            = hud;
            _panel          = root.Q("daily-panel");
            _loginInfo      = root.Q<Label>("daily-login-info");
            _claimLoginBtn  = root.Q<Button>("btn-claim-login");
            _challengesList = root.Q("daily-challenges-list");

            if (_claimLoginBtn != null)
                _claimLoginBtn.clicked += OnClaimLoginPressed;

            if (_panel == null)
                GameLogger.Warning("[DailyEvents] daily-panel not found in GameHUD.uxml — panel will not display.");
        }

        /// <summary>Rebuilds the panel contents from current DailyEventService state.</summary>
        public void Refresh()
        {
            var svc = DailyEventService.Instance;

            RefreshLoginSection(svc);
            RefreshChallenges(svc);
        }

        private void RefreshLoginSection(DailyEventService svc)
        {
            var reward = svc?.CurrentLoginReward;
            bool canClaim = svc != null && svc.CanClaimLoginReward();

            if (_loginInfo != null)
            {
                if (reward == null)
                    _loginInfo.text = "No login rewards available.";
                else
                {
                    var sb = new StringBuilder($"Day {reward.day}:  ◆ {reward.crystals}");
                    if (reward.entropy > 0)          sb.Append($"   e {reward.entropy:N0}");
                    if (reward.prestigeCurrency > 0) sb.Append($"   ✦ {reward.prestigeCurrency}");
                    if (!canClaim) sb.Append("\nClaimed — come back tomorrow!");
                    _loginInfo.text = sb.ToString();
                }
            }

            if (_claimLoginBtn != null)
            {
                _claimLoginBtn.text = canClaim ? "Claim" : "Claimed";
                _claimLoginBtn.SetEnabled(canClaim);
            }
        }

        private void RefreshChallenges(DailyEventService svc)
        {
            if (_challengesList == null) return;
            _challengesList.Clear();

            if (svc == null || svc.TodaysChallengeIds.Count == 0)
            {
                _challengesList.Add(new Label("No challenges available."));
                return;
            }

            foreach (var id in svc.TodaysChallengeIds)
            {
                var entry = svc.GetChallengeEntry(id);
                if (entry == null) continue;

                int  progress  = svc.GetChallengeProgress(id);
                bool complete  = svc.IsChallengeComplete(id);
                bool claimed   = svc.IsChallengeClaimed(id);

                var row = new VisualElement();
                row.AddToClassList("upgrade-row");
                if (claimed) row.AddToClassList("upgrade-row--maxed");

                var nameLabel = new Label(entry.description);
                nameLabel.AddToClassList("recipe-name");
                row.Add(nameLabel);

                var progressLabel = new Label($"{Mathf.Min(progress, entry.target)} / {entry.target}   (◆ {entry.crystals})");
                progressLabel.AddToClassList("recipe-inputs");
                row.Add(progressLabel);

                var claimBtn = new Button { text = claimed ? "Claimed" : "Claim" };
                claimBtn.AddToClassList("craft-btn");
                claimBtn.SetEnabled(complete && !claimed);

                string capturedId = id;
                claimBtn.clicked += () => OnClaimChallengePressed(capturedId);
                row.Add(claimBtn);

                _challengesList.Add(row);
            }
        }

        private void OnClaimLoginPressed()
        {
            var svc = DailyEventService.Instance;
            if (svc == null) return;

            var reward = svc.CurrentLoginReward;
            if (svc.ClaimLoginReward())
            {
                _hud?.ShowNotification("🎁", reward != null
                    ? $"Login reward claimed — +◆{reward.crystals}!"
                    : "Login reward claimed!");
            }
            Refresh();
        }

        private void OnClaimChallengePressed(string id)
        {
            var svc = DailyEventService.Instance;
            if (svc == null) return;

            var entry = svc.GetChallengeEntry(id);
            if (svc.ClaimChallenge(id))
                _hud?.ShowNotification("✅", $"Challenge complete — +◆{entry?.crystals ?? 0}!");
            Refresh();
        }
    }
}
